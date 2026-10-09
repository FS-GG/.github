#!/usr/bin/env python3
"""Fake GitHub API checks for live tag/draft observation and dispatch."""

import json
import hashlib
import pathlib
import sys
import tempfile

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[2] / "scripts"))

from release_successor_execution import Effect
from release_successor_provider import LiveProvider, NotFound


class FakeAPI:
    def __init__(self):
        self.tag = None
        self.release = None
        self.assets = {}
        self.writes = []

    def get(self, path):
        if path.endswith("/git/ref/tags/coherent-set/v0.91.3") and self.tag:
            return {"object": {"sha": self.tag}}
        if path.endswith("/releases/tags/coherent-set/v0.91.3") and self.release and not self.release["draft"]:
            return self.release
        if path.endswith("/releases?per_page=100&page=1"):
            return [self.release] if self.release else []
        if path.endswith("/releases/1/assets?per_page=100"):
            return [{"id": index, "name": name} for index, name in enumerate(self.assets, 1)]
        raise NotFound(path)

    def upload_asset(self, release_id, name, path):
        assert release_id == 1 and name not in self.assets
        self.assets[name] = path.read_bytes()
        self.writes.append(("asset", name))
        return {"id": len(self.assets), "name": name}

    def download_asset(self, asset_id):
        return list(self.assets.values())[asset_id - 1]

    def post(self, path, body):
        self.writes.append((path, body))
        if path.endswith("/git/refs"):
            self.tag = body["sha"]
            return {"object": {"sha": self.tag}}
        if path.endswith("/releases"):
            self.release = {"id": 1, "tag_name": body["tag_name"], "target_commitish": body["target_commitish"],
                            "body": body["body"], "draft": body["draft"]}
            return self.release
        raise AssertionError(path)


def qualify_live_provider():
    with tempfile.TemporaryDirectory() as temporary:
        root = pathlib.Path(temporary)
        source = "b" * 40
        content_id = "sha256:" + "a" * 64
        manifest = root / "release-manifest.json"
        manifest.write_text(json.dumps({"contentId": content_id, "descriptor": {"sourceSha": source, "version": "0.91.3"}}))
        api = FakeAPI()
        provider = LiveProvider(api, manifest, "github-token", "nuget-key")
        tag = Effect("tag", source, content_id)
        draft = Effect("draft", content_id, content_id)
        assert provider.observe(tag).state == "absent"
        assert provider.dispatch(tag).state == "applied"
        assert provider.observe(tag).state == "matched"
        assert provider.observe(draft).state == "absent"
        assert provider.dispatch(draft).state == "applied"
        assert provider.observe(draft).state == "matched"
        assert api.writes[0][1] == {"ref": "refs/tags/coherent-set/v0.91.3", "sha": source}
        assert len(api.writes) == 2
        package_name = "FS.GG.Kit.0.91.3.nupkg"
        (root / package_name).write_bytes(b"candidate package bytes")
        digest = hashlib.sha256((root / package_name).read_bytes()).hexdigest()
        archive = Effect("archive-asset:FS.GG.Kit", digest, digest)
        assert provider.observe(archive).state == "absent"
        assert provider.dispatch(archive).state == "applied"
        assert provider.observe(archive).state == "matched"
        api.assets[package_name] = b"different bytes"
        assert provider.observe(archive).state == "mismatched"
        api.tag = "c" * 40
        assert provider.observe(tag).state == "mismatched"
        api.release["body"] = "unrelated"
        assert provider.observe(draft).state == "mismatched"

    print("release successor live tag/draft fake API cases passed")


# Exercise the actual publisher's route and composite admission with bounded fake APIs.
import copy
import importlib.util
import unittest
from unittest.mock import patch
from release_successor_admission import SingleOperatorAdmission
from release_successor_execution import PACKAGES, JournalState, Observation, Dispatch, advance
from release_successor_journal import REF, REPOSITORY as AUTHORITY

spec = importlib.util.spec_from_file_location("tested_successor_publisher", pathlib.Path(__file__).resolve().parents[2] / "scripts/release-successor-publish.py")
publisher = importlib.util.module_from_spec(spec)
spec.loader.exec_module(publisher)
protection, reviewed_binding = publisher.authority_protection()


def candidate_manifest():
    descriptor = {"policyVersion": "release-successor/1", "sourceSha": "b" * 40,
                  "standaloneTelemetry": {"qualificationSha256": "c" * 64},
                  "packages": [{"id": package, "artifact": {"sha256": str(index) * 64,
                                "payloadSha256": "sha256:" + str(index) * 64}}
                               for index, package in enumerate(PACKAGES, 1)]}
    content = "sha256:" + hashlib.sha256(json.dumps(descriptor, sort_keys=True, separators=(",", ":")).encode()).hexdigest()
    return {"contentId": content, "descriptor": descriptor}


class AuthorityAPI:
    def __init__(self, present=False, legacy=False):
        self.present, self.legacy = present, legacy
        self.reads = []
        self.main_reads = 0
        self.controls = copy.deepcopy(reviewed_binding["rulesets"])
        self.omit_actors = False
        self.change_main_at = None
        self.override = {}
        self.root = "1" * 40
        self.state = "2" * 40
        self.releases = "3" * 40
        self.directory = "4" * 40

    def get(self, path):
        self.reads.append(path)
        if path in self.override:
            value = self.override[path]
            if isinstance(value, Exception):
                raise value
            return copy.deepcopy(value)
        if path == f"repos/{AUTHORITY}":
            return {"id": 1351660651, "full_name": AUTHORITY}
        if path == f"repos/{AUTHORITY}/git/ref/heads/main":
            self.main_reads += 1
            head = "f" * 40 if self.change_main_at and self.main_reads >= self.change_main_at else "a" * 40
            return {"object": {"sha": head, "type": "commit"}}
        if path == f"repos/{AUTHORITY}/git/ref/{REF.removeprefix('refs/')}":
            if self.legacy:
                return {"object": {"sha": "e" * 40, "type": "commit"}}
            raise NotFound(path)
        if path == f"repos/{AUTHORITY}/git/commits/" + "a" * 40:
            return {"sha": "a" * 40, "parents": [], "tree": {"sha": self.root}}
        if "/git/trees/" in path:
            tree = path.rsplit("/", 1)[1]
            rows = {self.root: [("state", self.state)], self.state: [("releases", self.releases)],
                    self.releases: [(REF.rsplit("/", 1)[1], self.directory)] if self.present else []}[tree]
            return {"sha": tree, "truncated": False,
                    "tree": [{"path": name, "mode": "040000", "type": "tree", "sha": sha} for name, sha in rows]}
        if "/rulesets/" in path:
            value = copy.deepcopy(next(x for x in self.controls.values() if x["id"] == int(path.rsplit("/", 1)[1])))
            if self.omit_actors:
                value.pop("bypass_actors")
            return value
        if "/rules/branches/" in path:
            pairs = [("creation", 24802693), ("update", 24802693), ("deletion", 24802698), ("non_fast_forward", 24802698)] if path.endswith("/main") else [("creation", 24812732), ("update", 24812732)]
            return [{"type": kind, "ruleset_id": identifier, "ruleset_source_type": "Repository", "ruleset_source": AUTHORITY} for kind, identifier in pairs]
        raise AssertionError("unselected read " + path)

    def post(self, *args):
        raise AssertionError("unexpected Authority write")

    patch = post


class PublisherAPI:
    def __init__(self):
        self.head = "b" * 40
        self.native_run = {"repository": {"id": 1269292704}, "path": ".github/workflows/release-successor-publish.yml",
                           "event": "workflow_dispatch", "head_branch": "main", "head_sha": self.head,
                           "actor": {"login": "EHotwagner"}, "run_attempt": 1, "status": "in_progress"}
        self.comparison = "ahead"
        self.reads = []

    def get(self, path):
        self.reads.append(path)
        if path == "repos/FS-GG/.github":
            return {"id": 1269292704, "full_name": "FS-GG/.github"}
        if path == "repos/FS-GG/.github/git/ref/heads/main":
            return {"object": {"sha": self.head}}
        if path == "repos/FS-GG/.github/actions/runs/1":
            return copy.deepcopy(self.native_run)
        if "/compare/" in path:
            return {"status": self.comparison}
        raise NotFound(path)


class PreparedJournal:
    ref = REF

    def __init__(self, intent, generation=33):
        self.intent = copy.deepcopy(intent)
        self.state = JournalState(generation, intent["contentId"], {})
        self.initializations = 0
        self.read_count = 0
        self.writes = []

    def read(self):
        self.read_count += 1
        return self.state

    def initialize(self, intent):
        self.initializations += 1
        self.state = JournalState(1, intent["contentId"], {})

    def validate_intent(self, intent):
        if intent != self.intent:
            raise publisher.Refused("protected release journal binds a different candidate")

    def compare_and_swap(self, expected, effect, state):
        assert expected == self.state
        self.writes.append((effect, state))
        self.state = JournalState(expected.generation + 1, expected.content_id, {**expected.effects, effect: state})
        return True


class QualifiedProvider:
    def __init__(self):
        self.calls = []
        self.visible = False

    def _release(self):
        return None

    def observe(self, effect):
        return Observation("matched", effect.target_digest) if self.visible else Observation("absent")

    def dispatch(self, effect):
        self.calls.append(effect.identity)
        return Dispatch("applied")


class PublisherJournalTests(unittest.TestCase):
    def setUp(self):
        self.manifest = candidate_manifest()
        self.intent = {"contentId": self.manifest["contentId"], "sourceSha": "b" * 40, "version": "0.100.0",
                       "candidateArchiveSha256": "c" * 64, "operator": "EHotwagner"}
        self.ledger = AuthorityAPI()
        self.source = PublisherAPI()
        base = SingleOperatorAdmission(self.source, self.manifest, "b" * 40, 1, "EHotwagner", "refs/heads/main")
        self.admission = publisher.ProtectedPublisherAdmission(base, self.ledger, REF)
        self.journal = PreparedJournal(self.intent)
        self.provider = QualifiedProvider()
        self.uniqueness_calls = 0

    def uniqueness(self):
        self.uniqueness_calls += 1

    def prepare(self, preflight=False, candidate=None):
        return publisher.prepare_release_journal(self.journal, self.ledger, self.admission, self.provider, self.source,
            self.manifest, self.intent, candidate or "b" * 40, "b" * 40, preflight, self.uniqueness)

    def test_unused_preflight_reads_physical_destination_and_writes_nothing(self):
        self.assertTrue(self.prepare(preflight=True))
        self.assertEqual(self.uniqueness_calls, 1)
        self.assertEqual(self.journal.initializations, 0)
        self.assertEqual(self.journal.writes, [])
        self.assertEqual(self.provider.calls, [])
        self.assertEqual(self.journal.read_count, 0)

    def test_fresh_genesis_composes_exact_publisher_and_protection_admission(self):
        self.assertFalse(self.prepare())
        self.assertEqual(self.journal.initializations, 1)
        self.assertEqual(self.journal.state.generation, 1)
        self.assertEqual(self.provider.calls, [])

    def test_consumed_main18_with_absent_legacy_is_recovery_never_genesis(self):
        self.ledger.present = True
        self.assertFalse(self.prepare())
        self.assertEqual(self.journal.state.generation, 33)
        self.assertEqual(self.journal.initializations, 0)
        self.assertEqual(self.journal.read_count, 1)
        self.assertEqual(self.uniqueness_calls, 0)
        with self.assertRaisesRegex(publisher.Refused, "unused release journal"):
            self.prepare(preflight=True)
        self.assertEqual(self.journal.initializations, 0)
        self.assertEqual(self.provider.calls, [])

    def test_legacy_only_and_dual_storage_refuse_without_migration(self):
        self.ledger.legacy = True
        for present in (False, True):
            self.ledger.present = present
            with self.subTest(present=present), self.assertRaisesRegex(publisher.Refused, "legacy release journal"):
                self.prepare()
        self.assertEqual(self.journal.initializations, 0)
        self.assertEqual(self.provider.calls, [])

    def test_missing_required_parent_directories_refuse_without_writes(self):
        for tree, segment in [(self.ledger.root, "state"), (self.ledger.state, "releases")]:
            for suffix in (REF.rsplit("/", 1)[1], segment):
                with self.subTest(segment=segment, selected=suffix):
                    self.journal.ref = "refs/fsgg/v2/journal/release/" + suffix
                    legacy_path = f"repos/{AUTHORITY}/git/ref/{self.journal.ref.removeprefix('refs/')}"
                    self.ledger.override = {f"repos/{AUTHORITY}/git/trees/{tree}":
                        {"sha": tree, "truncated": False, "tree": []}, legacy_path: NotFound("absent legacy ref")}
                    with self.assertRaisesRegex(publisher.Refused, "required parent directory"):
                        self.prepare()
                    self.assertEqual(self.journal.initializations, 0)
                    self.assertEqual(self.journal.writes, [])
                    self.assertEqual(self.provider.calls, [])
                    self.assertEqual(self.uniqueness_calls, 0)

    def test_nested_missing_object_and_unreadable_main_are_not_fresh(self):
        for path, error in [(f"repos/{AUTHORITY}/git/trees/{self.ledger.root}", NotFound("nested tree404")),
                            (f"repos/{AUTHORITY}/git/ref/heads/main", NotFound("main404")),
                            (f"repos/{AUTHORITY}/git/trees/{self.ledger.state}", RuntimeError("HTTP403"))]:
            with self.subTest(path=path):
                self.ledger.override = {path: error}
                with self.assertRaises(RuntimeError):
                    self.prepare()
        self.assertEqual(self.journal.initializations, 0)
        self.assertEqual(self.provider.calls, [])

    def test_malformed_and_incomplete_tree_or_identity_refuse(self):
        path = f"repos/{AUTHORITY}/git/trees/{self.ledger.releases}"
        entry = {"path": REF.rsplit("/", 1)[1], "sha": self.ledger.directory, "mode": "040000", "type": "tree"}
        values = [({"sha": self.ledger.releases, "truncated": True, "tree": []}),
                  ({"sha": self.ledger.releases, "tree": []}),
                  ({"sha": "e" * 40, "truncated": False, "tree": []}),
                  ({"sha": self.ledger.releases, "truncated": False, "tree": [entry, entry]}),
                  ({"sha": self.ledger.releases, "truncated": False, "tree": [{**entry, "type": "blob", "mode": "100644"}]}),
                  ({"sha": self.ledger.releases, "truncated": False, "tree": "unknown"}),
                  ({"sha": self.ledger.releases, "truncated": False, "tree": [None]})]
        for value in values:
            with self.subTest(value=value):
                self.ledger.override = {path: value}
                with self.assertRaises(publisher.Refused):
                    self.prepare()
        self.ledger.override = {f"repos/{AUTHORITY}": {"id": 1, "full_name": AUTHORITY}}
        with self.assertRaisesRegex(publisher.Refused, "identity"):
            self.prepare()
        self.assertEqual(self.journal.initializations, 0)

    def test_head_drift_during_classification_or_uniqueness_refuses(self):
        self.ledger.change_main_at = 2
        with self.assertRaisesRegex(publisher.Refused, "moved during destination"):
            self.prepare()
        self.ledger.change_main_at = None
        def move_during_uniqueness():
            self.ledger.change_main_at = self.ledger.main_reads + 1
        with patch.object(self, "uniqueness", move_during_uniqueness):
            with self.assertRaisesRegex(publisher.Refused, "moved before journal"):
                self.prepare()
        self.assertEqual(self.journal.initializations, 0)

    def test_existing_corruption_and_intent_disagreement_never_reset(self):
        self.ledger.present = True
        with patch.object(self.journal, "read", side_effect=RuntimeError("corrupt logical chain")):
            with self.assertRaisesRegex(RuntimeError, "corrupt"):
                self.prepare()
        for field in self.intent:
            with self.subTest(field=field):
                changed = {**self.intent, field: "different"}
                with patch.object(self.journal, "intent", changed), self.assertRaisesRegex(publisher.Refused, "different candidate"):
                    self.prepare()
        self.assertEqual(self.journal.initializations, 0)
        self.assertEqual(self.provider.calls, [])

    def test_fresh_source_must_match_and_recovery_requires_descendant(self):
        with self.assertRaisesRegex(publisher.Refused, "source to match"):
            self.prepare(candidate="c" * 40)
        self.ledger.present = True
        self.assertFalse(self.prepare(candidate="c" * 40))
        self.source.comparison = "diverged"
        with self.assertRaisesRegex(publisher.Refused, "does not descend"):
            self.prepare(candidate="c" * 40)
        self.assertEqual(self.journal.initializations, 0)

    def test_guard_changes_between_intent_and_dispatch_preserve_original_intent(self):
        effect = publisher.ordered_effects(self.manifest)[0]
        self.journal.state = JournalState(1, self.manifest["contentId"], {})
        original_cas = self.journal.compare_and_swap
        def persist_then_change(expected, identity, state):
            result = original_cas(expected, identity, state)
            self.ledger.controls["legacyFence"]["enforcement"] = "disabled"
            return result
        with patch.object(self.journal, "compare_and_swap", persist_then_change):
            with self.assertRaisesRegex(RuntimeError, "dispatch admission denied"):
                advance(self.manifest, self.journal, self.admission, self.provider)
        self.assertEqual(self.provider.calls, [])
        self.assertEqual(self.journal.writes, [(effect.identity, "intent")])
        self.assertEqual(self.journal.state.effects, {effect.identity: "intent"})

    def test_guard_changes_before_settlement_preserve_original_effect(self):
        effect = publisher.ordered_effects(self.manifest)[0]
        self.journal.state = JournalState(1, self.manifest["contentId"], {})
        self.assertEqual(advance(self.manifest, self.journal, self.admission, self.provider), "waiting")
        self.assertEqual(self.provider.calls, [effect.identity])
        self.provider.visible = True
        self.ledger.controls["mainIntegrity"]["updated_at"] = "2000-01-01T00:00:00Z"
        with self.assertRaisesRegex(RuntimeError, "settlement admission denied"):
            advance(self.manifest, self.journal, self.admission, self.provider)
        self.assertEqual(self.journal.writes, [(effect.identity, "intent")])
        self.assertEqual(self.provider.calls, [effect.identity])

    def test_each_native_admission_action_requires_unchanged_protection(self):
        for action in ("intent", "dispatch", "settle"):
            with self.subTest(action=action):
                self.assertTrue(self.admission.authorize(self.manifest["contentId"], "journal", action, self.manifest["contentId"]))
                self.ledger.controls["legacyFence"]["conditions"]["ref_name"]["exclude"] = [REF]
                self.assertFalse(self.admission.authorize(self.manifest["contentId"], "journal", action, self.manifest["contentId"]))
                self.ledger.controls = copy.deepcopy(reviewed_binding["rulesets"])

    def test_native_actor_omission_uses_stabilized_binding_not_empty_roster(self):
        self.ledger.omit_actors = True
        self.assertTrue(self.admission.authorize(self.manifest["contentId"], "journal", "intent", self.manifest["contentId"]))
        self.admission.binding = copy.deepcopy(self.admission.binding)
        self.admission.binding["rulesets"]["mainWriter"].pop("bypass_actors")
        self.assertFalse(self.admission.authorize(self.manifest["contentId"], "journal", "intent", self.manifest["contentId"]))

    def test_native_publisher_head_actor_attempt_context_stays_required(self):
        for field, value in [("head_sha", "c" * 40), ("run_attempt", 2), ("event", "push"),
                             ("head_branch", "other"), ("actor", {"login": "other"}), ("status", "completed")]:
            with self.subTest(field=field):
                old = copy.deepcopy(self.source.native_run)
                self.source.native_run[field] = value
                with self.assertRaisesRegex(publisher.Refused, "initialization admission denied"):
                    self.prepare()
                self.source.native_run = old
        self.source.head = "c" * 40
        with self.assertRaisesRegex(publisher.Refused, "initialization admission denied"):
            self.prepare()
        self.assertEqual(self.journal.initializations, 0)


if __name__ == "__main__":
    qualify_live_provider()
    unittest.main()
