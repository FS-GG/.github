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


def candidate_manifest(version="0.101.0"):
    descriptor = {"policyVersion": "release-successor/1", "sourceSha": "b" * 40,
                  "version": version,
                  "standaloneTelemetry": {"qualificationSha256": "c" * 64},
                  "packages": [{"id": package, "artifact": {"sha256": str(index) * 64,
                                "payloadSha256": "sha256:" + str(index) * 64}}
                               for index, package in enumerate(PACKAGES, 1)]}
    content = "sha256:" + hashlib.sha256(json.dumps(descriptor, sort_keys=True, separators=(",", ":")).encode()).hexdigest()
    return {"contentId": content, "descriptor": descriptor}


class AuthorityAPI:
    def __init__(self, present=False, legacy=False, ref=REF):
        self.ref = ref
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
        if path == f"repos/{AUTHORITY}/git/ref/{self.ref.removeprefix('refs/')}":
            if self.legacy:
                return {"object": {"sha": "e" * 40, "type": "commit"}}
            raise NotFound(path)
        if path == f"repos/{AUTHORITY}/git/commits/" + "a" * 40:
            return {"sha": "a" * 40, "parents": [], "tree": {"sha": self.root}}
        if "/git/trees/" in path:
            tree = path.rsplit("/", 1)[1]
            rows = {self.root: [("state", self.state)], self.state: [("releases", self.releases)],
                    self.releases: [(self.ref.rsplit("/", 1)[1], self.directory)] if self.present else []}[tree]
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
    main_directory = True
    _observed = type("Observed", (), {"head": "1" * 40})()

    def _main_head(self, physical):
        return "1" * 40  # The logical journal head bound at every physical head of this fixture.

    def __init__(self, intent, generation=33, ref=REF):
        self.ref = ref
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


class BurstAuthorityAPI(AuthorityAPI):
    """Authority main follows a scripted head sequence (last head repeats).

    Settlement-bot commits never touch state/releases, so every head shares the release tree,
    except heads in `present_heads`, where another writer created the selected destination.
    """

    def __init__(self, heads, present_heads=(), **kwargs):
        super().__init__(**kwargs)
        self.heads = list(heads)
        self.present_heads = set(present_heads)
        self.initially_present = self.present

    def get(self, path):
        if path == f"repos/{AUTHORITY}/git/ref/heads/main":
            self.reads.append(path)
            self.main_reads += 1
            return {"object": {"sha": self.heads[min(self.main_reads, len(self.heads)) - 1], "type": "commit"}}
        if path.startswith(f"repos/{AUTHORITY}/git/commits/"):
            self.reads.append(path)
            sha = path.rsplit("/", 1)[1]
            self.present = self.initially_present or sha in self.present_heads
            return {"sha": sha, "parents": [], "tree": {"sha": self.root}}
        return super().get(path)


class PublisherJournalTests(unittest.TestCase):
    def setUp(self):
        self.manifest = candidate_manifest()
        self.intent = {"contentId": self.manifest["contentId"], "sourceSha": "b" * 40, "version": self.manifest["descriptor"]["version"],
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

    def test_original103_denial_uses_fixed_identity_after_current_default_advances(self):
        self.manifest = candidate_manifest("0.103.0")
        self.intent = {**self.intent, "contentId": self.manifest["contentId"], "version": "0.103.0"}
        self.ledger = AuthorityAPI(ref=publisher.SUSPENDED_REF)
        self.journal = PreparedJournal(self.intent, ref=publisher.SUSPENDED_REF)
        base = SingleOperatorAdmission(self.source, self.manifest, "b" * 40, 1, "EHotwagner", "refs/heads/main")
        self.admission = publisher.ProtectedPublisherAdmission(base, self.ledger, publisher.SUSPENDED_REF)
        # The original default binding is retained for explicit denial; it cannot
        # initialize a new .103 journal after the prospective suspension cut.
        with self.assertRaises(RuntimeError):
            self.prepare()
        self.assertEqual(self.journal.initializations, 0)
        self.assertEqual(self.journal.writes, [])
        self.assertEqual(self.provider.calls, [])

    def test_consumed_selected_main_journal_with_absent_legacy_is_recovery_never_genesis(self):
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
                    self.journal.ref = REF.rsplit("/", 1)[0] + "/" + suffix
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

    def test_head_drift_that_never_settles_during_classification_or_uniqueness_refuses(self):
        # #5108 keeps refusing a persistent change: a main that moves on every read never settles.
        sleeps = []
        moving = [f"{index:040x}" for index in range(1, 200)]
        self.ledger = BurstAuthorityAPI(moving)
        with patch.object(publisher.time, "sleep", sleeps.append), \
             self.assertRaisesRegex(publisher.Refused, "did not settle"):
            self.prepare()
        self.assertEqual(sleeps, [publisher.AUTHORITY_SETTLE_SECONDS] * publisher.AUTHORITY_SETTLE_ATTEMPTS)
        sleeps.clear()
        self.ledger = BurstAuthorityAPI(["a" * 40] * 2 + moving)
        with patch.object(publisher.time, "sleep", sleeps.append), \
             self.assertRaisesRegex(publisher.Refused, "did not settle"):
            self.prepare()
        self.assertEqual(len(sleeps), publisher.AUTHORITY_SETTLE_ATTEMPTS)
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



class AuthoritySettlementBurstTests(unittest.TestCase):
    """#5108: a settlement burst inside a publisher check window is re-read, not refused."""

    setUp = PublisherJournalTests.setUp
    uniqueness = PublisherJournalTests.uniqueness
    prepare = PublisherJournalTests.prepare

    A, B, C, D = "a" * 40, "b" * 40, "c" * 40, "d" * 40

    def run_prepare(self, heads, *, present_heads=(), present=False, preflight=False):
        self.ledger = BurstAuthorityAPI(heads, present_heads, present=present)
        self.admission = publisher.ProtectedPublisherAdmission(
            SingleOperatorAdmission(self.source, self.manifest, "b" * 40, 1, "EHotwagner", "refs/heads/main"), self.ledger, REF)
        sleeps = []
        with patch.object(publisher.time, "sleep", sleeps.append):
            result = self.prepare(preflight=preflight)
        return result, sleeps

    def test_unmoved_main_never_waits(self):
        result, sleeps = self.run_prepare([self.A])
        self.assertFalse(result)
        self.assertEqual((sleeps, self.journal.initializations), ([], 1))

    def test_burst_inside_classification_is_accepted_after_it_settles(self):
        result, sleeps = self.run_prepare([self.A, self.B, self.C, self.C], preflight=True)
        self.assertTrue(result)
        self.assertEqual(sleeps, [publisher.AUTHORITY_SETTLE_SECONDS])
        self.assertEqual(self.journal.initializations, 0)

    def test_burst_before_genesis_is_accepted_after_it_settles(self):
        result, sleeps = self.run_prepare([self.A, self.A, self.B, self.C, self.D, self.D])
        self.assertFalse(result)
        self.assertEqual(sleeps, [publisher.AUTHORITY_SETTLE_SECONDS] * 2)
        self.assertEqual(self.journal.initializations, 1)

    def test_destination_created_while_main_settled_is_real_divergence(self):
        with self.assertRaisesRegex(publisher.Refused, "destination changed"):
            self.run_prepare([self.A, self.A, self.B], present_heads={self.B})
        self.assertEqual(self.journal.initializations, 0)

    def test_recovery_burst_rereads_the_same_journal_state(self):
        result, sleeps = self.run_prepare([self.A, self.A, self.A, self.B, self.C, self.C], present=True)
        self.assertFalse(result)
        self.assertEqual(sleeps, [publisher.AUTHORITY_SETTLE_SECONDS])
        self.assertEqual(self.journal.read_count, 2)

    def test_recovery_journal_change_while_main_settled_is_real_divergence(self):
        states = iter([self.journal.state, JournalState(34, self.intent["contentId"], {"journal": "intent"})])
        with patch.object(self.journal, "read", lambda: next(states)), \
             self.assertRaisesRegex(publisher.Refused, "Authority journal changed during journal recovery"):
            self.run_prepare([self.A, self.A, self.A, self.B, self.C, self.C], present=True)

    def test_total_settle_wait_is_bounded_per_publisher_run(self):
        # Two separate drifts in one run share one budget: 5 waits settle the first, the second
        # exhausts the remaining 5 and refuses instead of waiting another 8 x 15 s.
        moving = [f"{index:040x}" for index in range(1, 200)]
        heads = [self.A, self.B] + moving[:4] + [self.C, self.C, self.C, self.C] + moving[4:]
        with self.assertRaisesRegex(publisher.Refused, "bounded re-read budget"):
            self.run_prepare(heads)
        self.assertEqual(self.journal.initializations, 0)
        budget = publisher.AuthorityRereadBudget()
        self.assertEqual(budget.remaining // publisher.AUTHORITY_SETTLE_SECONDS, 10)

    def test_recovery_main_that_never_settles_refuses(self):
        moving = [f"{index:040x}" for index in range(1, 200)]
        with self.assertRaisesRegex(publisher.Refused, "did not settle"):
            self.run_prepare([self.A, self.A] + moving, present=True)


def _real_git():
    import ast, base64, os, subprocess
    source = ast.parse((pathlib.Path(__file__).resolve().parents[1] / "release-successor-journal/run.py").read_text())
    cls = next(node for node in source.body if isinstance(node, ast.ClassDef) and node.name == "RealGit")
    namespace = {"os": os, "subprocess": subprocess, "base64": base64, "tempfile": tempfile, "pathlib": pathlib,
                 "REPOSITORY": AUTHORITY}
    exec(compile(ast.Module(body=[cls], type_ignores=[]), "existing-RealGit", "exec"), namespace)
    return namespace["RealGit"]


class RealGitRecoveryBurstTests(unittest.TestCase):
    """#5108 against the actual journal reader on real Git objects: a burst can land inside
    journal.read() itself, which refuses with its typed JournalMainMoved."""

    INTENT = {"contentId": "sha256:" + "a" * 64, "sourceSha": "b" * 40, "version": "0.91.3",
              "candidateArchiveSha256": "c" * 64, "operator": "EHotwagner"}
    MAIN = f"repos/{AUTHORITY}/git/ref/heads/main"

    def recover(self, event_at, event):
        from release_successor_journal import ProtectedReleaseJournal
        RealGit = _real_git()
        with tempfile.TemporaryDirectory() as scratch:
            real = RealGit(pathlib.Path(scratch) / "authority.git")
            ProtectedReleaseJournal(real, main_directory=True).initialize(self.INTENT)
            physical = real.get(self.MAIN)["object"]["sha"]

            class Burst:
                def __init__(inner):
                    inner.main_reads = 0
                def get(inner, path):
                    if path == self.MAIN:
                        inner.main_reads += 1
                        if inner.main_reads in event_at:
                            event(real)
                    value = real.get(path)
                    if path == self.MAIN:
                        value["object"]["type"] = "commit"
                    return value
                def __getattr__(inner, name):
                    return getattr(real, name)
            api = Burst()
            journal = ProtectedReleaseJournal(api, main_directory=True)
            sleeps = []
            budget = publisher.AuthorityRereadBudget(sleep=sleeps.append)
            head = publisher.read_journal_at_stable_main(journal, api, physical, "Host recovery", budget)
            return head, physical, journal, sleeps

    @staticmethod
    def sibling(real):
        real.sibling("settlement-burst.txt")

    @staticmethod
    def competing_intent(real):
        from release_successor_journal import ProtectedReleaseJournal
        other = ProtectedReleaseJournal(real, main_directory=True)
        assert other.compare_and_swap(other.read(), "nuget:FS.GG.Kit", "intent")

    def test_burst_inside_journal_read_is_reread_after_it_settles(self):
        # Main reads: 1 outer opening, 2/3 read()'s own sandwich. The commit lands before read 3.
        head, physical, journal, sleeps = self.recover({3}, self.sibling)
        self.assertNotEqual(head, physical)
        self.assertEqual(len(sleeps), 1)
        self.assertEqual(journal._observed.state["effects"], {})

    def test_burst_before_the_outer_closing_read_is_reread(self):
        head, physical, _, sleeps = self.recover({4}, self.sibling)
        self.assertNotEqual(head, physical)
        self.assertEqual(len(sleeps), 1)

    def test_competing_journal_write_inside_read_is_divergence_even_without_a_prior_read(self):
        with self.assertRaisesRegex(publisher.Refused, "Authority journal changed during Host recovery"):
            self.recover({3}, self.competing_intent)

    def test_competing_journal_write_before_recovery_is_divergence(self):
        with self.assertRaisesRegex(publisher.Refused, "Authority journal changed during Host recovery"):
            self.recover({1}, self.competing_intent)


class TerminalAcquisitionControls(unittest.TestCase):
    def fixture(self, conclusion="failure", names=("release-successor-result.json",)):
        import datetime,io,zipfile
        from release_successor_terminal import collect_terminal
        run={"id":123,"head_sha":"b"*40,"status":"completed","conclusion":conclusion}
        jobs=[{"id":456,"run_id":123,"run_attempt":1,"head_sha":"b"*40,"name":"publish","status":"completed","conclusion":conclusion,"started_at":"2026-10-11T11:25:00Z","completed_at":"2026-10-11T11:28:57Z"}]
        stream=io.BytesIO()
        with zipfile.ZipFile(stream,"w")as z:
            for name in names:z.writestr(name,json.dumps({"publicationComplete":False,"failure":{"type":"quota"}}))
        archive=stream.getvalue()
        artifact={"id":789,"name":"release-successor-result","size_in_bytes":len(archive),
                  "digest":"sha256:"+hashlib.sha256(archive).hexdigest(),"expired":False,
                  "expires_at":"2027-01-09T10:45:49Z","workflow_run":{"id":123,"head_sha":"b"*40}}
        terminal={"run":copy.deepcopy(run),"jobs":copy.deepcopy(jobs),"resultArtifact":artifact,
                  "timing":{"retentionDeadlineUtc":"2026-10-11T11:33:57Z"}}
        now=[datetime.datetime(2026,10,11,11,30,tzinfo=datetime.timezone.utc)]
        entries=[]
        class Reader:
            def required(self,name,path):entries.append((name,path));return copy.deepcopy(artifact)
        def payload(name,path,cap):
            entries.append((name,path))
            body=b"native failed log"if name=="job-log"else archive
            assert len(body)<=cap
            return body,{"status":200,"complete":True},0
        def decoder(members):return json.loads(members["release-successor-result.json"])
        return collect_terminal,Reader(),run,jobs,terminal,payload,decoder,lambda:None,lambda:now[0],entries,now

    def call(self,parts,**kw):
        f,reader,run,jobs,terminal,payload,decoder,current,clock,*_=parts
        return f(reader,run,jobs,terminal,payload,decoder,current,clock=clock,**kw)

    def test_queued_cancellation_without_started_job_is_partial_not_fabricated(self):
        import datetime,importlib.util
        path=pathlib.Path(__file__).resolve().parents[2]/'scripts/release_successor_terminal.py'
        spec=importlib.util.spec_from_file_location('terminal_queued',path);m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
        parts=self.fixture();run=parts[2];terminal=parts[4];now=parts[-1][0]
        post=now-datetime.timedelta(seconds=60)
        run.update(status='completed',conclusion='cancelled',created_at=post.isoformat(),updated_at=(now-datetime.timedelta(seconds=5)).isoformat())
        intent={'postUtc':post.isoformat(),'deadlineUtc':(post+datetime.timedelta(seconds=4500)).isoformat(),
                'queueSeconds':600,'activeJobsSeconds':3600,'retentionSeconds':300,'aggregateSeconds':4500}
        for jobs in ([],[{**parts[3][0],'status':'completed','started_at':None,'completed_at':None,'conclusion':'cancelled'}]):
            timing=m.terminal_timing(run,jobs,intent,now)
            self.assertIsNone(timing['jobStartedUtc']);self.assertIsNone(timing['jobCompletedUtc'])
            self.assertEqual(timing['terminalTimestampSource'],'run.updated_at')
            selected={**terminal,'run':run,'jobs':jobs,'timing':timing,'resultArtifact':None}
            calls=[]
            value=m.collect_terminal(parts[1],run,jobs,selected,lambda *args:calls.append(args),parts[6],parts[7],clock=lambda:now)
            self.assertEqual(calls,[]);self.assertFalse(value['evidenceComplete']);self.assertFalse(value['archiveDownloaded'])
            self.assertIsNone(value['jobLogSHA256']);self.assertIsNone(value['nativeResult'])
            self.assertTrue(any('no started job' in x for x in value['gaps']))
            # Any independently available artifact is still attempted; no joblog ID invented.
            parts[-2].clear();selected['resultArtifact']=terminal['resultArtifact']
            value=m.collect_terminal(parts[1],run,jobs,selected,parts[5],parts[6],parts[7],clock=lambda:now)
            self.assertTrue(value['archiveDownloaded']);self.assertFalse(value['evidenceComplete'])
            self.assertFalse(any(x[0]=='job-log'for x in parts[-2]))
        with self.assertRaises(AssertionError):m.terminal_timing(run,[],intent,now+datetime.timedelta(seconds=296))

    def test_all_terminal_conclusions_acquire_log_and_available_result_in_one_call(self):
        for conclusion in ("success","failure","cancelled","timed_out","action_required","neutral","skipped","stale"):
            with self.subTest(conclusion=conclusion):
                parts=self.fixture(conclusion);value=self.call(parts)
                self.assertTrue(value["archiveDownloaded"]);self.assertTrue(value["evidenceComplete"])
                self.assertFalse(value["nativeResult"]["publicationComplete"])
                self.assertEqual([x[0]for x in parts[-2]], ["job-log","artifact","result-archive"])

    def test_missing_result_is_explicit_log_only_partial_evidence(self):
        parts=self.fixture();parts[4]["resultArtifact"]=None;value=self.call(parts)
        self.assertFalse(value["archiveDownloaded"]);self.assertFalse(value["evidenceComplete"])
        self.assertIsNone(value["nativeResult"]);self.assertEqual(len(parts[-2]),1)
        self.assertTrue(value["gaps"])

    def test_log_failure_preserved_while_available_result_is_still_acquired(self):
        parts=list(self.fixture());original=parts[5];first=RuntimeError("original log refusal")
        def fail_log(name,path,cap):
            if name=="job-log":raise first
            return original(name,path,cap)
        parts[5]=fail_log
        with self.assertRaises(RuntimeError)as error:self.call(parts)
        self.assertIs(error.exception,first)
        self.assertTrue(first.terminalEvidence["archiveDownloaded"])
        self.assertIsNotNone(first.terminalEvidence["nativeResult"])
        self.assertFalse(first.terminalEvidence["evidenceComplete"])

    def test_artifact_failure_keeps_log_and_both_failures_keep_first(self):
        for log_fail in (False,True):
            parts=list(self.fixture());first=RuntimeError("log first");original=parts[5]
            def fail(name,path,cap):
                if name=="job-log":
                    if log_fail:raise first
                    return original(name,path,cap)
                raise ValueError("archive second")
            parts[5]=fail
            with self.assertRaises((RuntimeError,ValueError))as error:self.call(parts)
            if log_fail:self.assertIs(error.exception,first);self.assertTrue(first.__notes__)
            else:self.assertIsNotNone(error.exception.terminalEvidence["jobLogSHA256"])
            self.assertFalse(error.exception.terminalEvidence["evidenceComplete"])

    def test_expired_original_grant_stops_before_requests(self):
        import datetime
        parts=self.fixture();parts[-1][0]=datetime.datetime(2026,10,11,11,34,tzinfo=datetime.timezone.utc)
        with self.assertRaisesRegex(AssertionError,"grant expired"):self.call(parts)
        self.assertEqual(parts[-2],[])

    def test_foreign_artifact_source_and_duplicate_member_refused(self):
        parts=self.fixture();parts[4]["resultArtifact"]["workflow_run"]["head_sha"]="c"*40
        with self.assertRaisesRegex(AssertionError,"source differs"):self.call(parts)
        self.assertEqual([x[0]for x in parts[-2]],["job-log","artifact"])
        parts=self.fixture(names=("release-successor-result.json","unexpected.json"))
        with self.assertRaisesRegex(AssertionError,"member set differs"):self.call(parts)

    def test_decoder_failure_retains_authenticated_archive_and_member_proof(self):
        parts=list(self.fixture())
        def reject(members):raise ValueError("native identity mismatch")
        parts[6]=reject
        with self.assertRaises(ValueError)as error:self.call(parts)
        self.assertTrue(error.exception.terminalEvidence["archiveAuthenticated"])
        self.assertIn("release-successor-result.json",error.exception.terminalEvidence["memberSHA256"])
        self.assertIsNone(error.exception.terminalEvidence["nativeResult"])

class ProviderDiagnosticTests(unittest.TestCase):
    def setUp(self):
        import io
        import release_successor_provider as provider_module
        self.io, self.module = io, provider_module
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = pathlib.Path(self.temporary.name)
        self.manifest = candidate_manifest("0.91.3")
        self.manifest["descriptor"].update(previousStableVersion="0.91.2", previousStableContentId="sha256:" + "d" * 64)
        self.path = self.root / "release-manifest.json"
        self.path.write_text(json.dumps(self.manifest))
        self.api = provider_module.GitHubAPI("SECRET-github-token")
        self.provider = provider_module.LiveProvider(self.api, self.path, "SECRET-github-token", "SECRET-nuget-key")
        for name in [*(f"{package}.0.91.3.nupkg" for package in PACKAGES),
                     "standalone-telemetry-evidence.json", "standalone-telemetry-runtime-evidence.json",
                     "promoted-release-manifest.json"]:
            (self.root / name).write_text("fixture")
        (self.root / "previous-stable-channel.json").write_text(json.dumps({
            "version": "0.91.2", "contentId": "sha256:" + "d" * 64}))
        self.release = {"id": 1, "draft": True}
        self.writes = []

    def test_claimed_provider_refuses_unowned_changed_or_settled_state_before_any_send(self):
        import release_successor_journal as policy
        window={'authorityEntries':500,'seconds':900,'visibilityProbes':32,'quotaFloor':1800}
        execution={'runId':39080000000,'attempt':1,'sourceSha':'a'*40,'operator':'EHotwagner'}
        with patch.object(policy,'CONTINUATION_WINDOW',window):
            record=policy.continuation_record(execution)
            candidate=policy.CONTINUATION_CANDIDATE
            state={'schema':policy.SCHEMA,'contentId':candidate['contentId'],'sourceSha':candidate['sourceSha'],
                   'version':'0.104.0','candidateArchiveSha256':candidate['archiveSha256'],
                   'operator':'EHotwagner','generation':29,'effects':policy.continuation_effects(29),'continuation':record}
            self.provider.version,self.provider.content_id,self.provider.source='0.104.0',state['contentId'],state['sourceSha']
            self.provider.logical_ref=policy.ALLOWED_SUCCESSOR['logicalRef']
            self.provider.suspension=policy.ReleaseSuspension(AuthorityAPI())
            permit=policy.ContinuationPermit(policy._CONTINUATION_SEAL,record,'b'*40)
            effect=Effect('channel-asset',self.provider.content_id,'request')
            with patch.object(self.provider.suspension,'authorize'), \
                 patch.object(self.provider.suspension,'main',return_value='c'*40), \
                 patch.object(self.provider.suspension,'file',side_effect=lambda *args:policy.canonical(state)), \
                 patch.object(self.api,'post',side_effect=AssertionError('unauthorized send')), \
                 patch.object(self.api,'upload_asset',side_effect=AssertionError('unauthorized upload')):
                # A durable marker never reconstructs a local first-send permit.
                with self.assertRaisesRegex(self.module.Refused,'send permit'):self.provider.dispatch(effect)
                self.provider._continuation_permit=permit
                with self.assertRaisesRegex(self.module.Refused,'send permit'):self.provider.dispatch(effect)
                permit.consume(effect.identity)
                self.provider._authorize_release(effect)
                for changed in ({**state,'effects':policy.continuation_effects(30),'generation':30},
                                {**state,'extra':1},{**state,'contentId':'sha256:'+'f'*64},
                                {**state,'continuation':{**record,'execution':{**execution,'runId':39080000001}}}):
                    with self.subTest(changed=changed),patch.object(self.provider.suspension,'file',return_value=policy.canonical(changed)),self.assertRaises(self.module.Refused):
                        self.provider.dispatch(effect)
                with patch.object(self.provider.suspension,'main',side_effect=['c'*40,'d'*40]),self.assertRaisesRegex(self.module.Refused,'moved'):
                    self.provider.dispatch(effect)
                self.provider.content_id='sha256:'+'e'*64
                with self.assertRaises(self.module.Refused):self.provider.dispatch(effect)

    def test_continuation_channel_prepares_once_and_freezes_exact_upload_bytes(self):
        effect=Effect("channel-asset",self.provider.content_id,"request")
        payload=self.provider.prepare_continuation_asset(effect)
        output=self.provider._continuation_asset_path(effect)
        raw=output.read_bytes()
        self.assertEqual(payload,{"name":"stable-channel.json","sha256":hashlib.sha256(raw).hexdigest(),"bytes":len(raw)})
        with self.assertRaisesRegex(self.module.Refused,"already prepared"):
            self.provider.prepare_continuation_asset(effect)
        output.write_bytes(raw+b"changed")
        with self.assertRaisesRegex(self.module.Refused,"changed"):
            self.provider._continuation_asset_path(effect)
        output.unlink();output.symlink_to(self.path)
        with self.assertRaisesRegex(self.module.Refused,"changed"):
            self.provider._continuation_asset_path(effect)

    def test_continuation_transport_hashes_the_same_bytes_it_submits(self):
        output=self.root/'stable-channel.json';output.write_bytes(b'prepared bytes')
        digest=hashlib.sha256(output.read_bytes()).hexdigest()
        requests=[]
        with patch.object(self.api,'_request',side_effect=lambda *args:requests.append(args) or b'{}'):
            self.api.upload_asset(416097241,'stable-channel.json',output,digest)
            self.assertEqual(requests[0][2],b'prepared bytes')
            output.write_bytes(b'changed after provider check')
            with self.assertRaisesRegex(self.module.Refused,'presend receipt'):
                self.api.upload_asset(416097241,'stable-channel.json',output,digest)
            output.write_bytes(b'x'*1048577)
            with self.assertRaisesRegex(self.module.Refused,'presend receipt'):
                self.api.upload_asset(416097241,'stable-channel.json',output,digest)
        self.assertEqual(len(requests),1)

    def test_continuation_manifest_dispatch_uses_prepared_path_without_regeneration(self):
        effect=Effect("manifest-asset",self.provider.content_id,"request")
        output=self.root/"promoted-release-manifest.json"
        with patch.object(self.provider,"_prepare_manifest_asset",return_value=output) as prepare:
            payload=self.provider.prepare_continuation_asset(effect)
        self.assertEqual(prepare.call_count,1)
        self.provider._continuation_permit=object() # Historical version fixture exercises only byte custody.
        writes=[]
        with patch.object(self.provider,"_prepare_manifest_asset",side_effect=AssertionError("payload regenerated")), \
             patch.object(self.provider,"_release",return_value=self.release), \
             patch.object(self.api,"upload_asset",side_effect=lambda release,name,path,expected:writes.append((release,name,path,path.read_bytes()))):
            self.assertEqual(self.provider.dispatch(effect).state,"applied")
        self.assertEqual(writes,[(1,"release-manifest.json",output,b"fixture")])
        self.assertEqual(payload['sha256'],hashlib.sha256(writes[0][3]).hexdigest())

    def test_continuation_visibility_uses_one_validated_dataset_and_refuses_next_partial_read(self):
        release={"id":416097241,"tag_name":self.provider.tag,"target_commitish":self.provider.source,
                 "body":self.provider.marker,"draft":True,"prerelease":False}
        names=[*(f"{package}.{self.provider.version}.nupkg" for package in PACKAGES),
               "standalone-telemetry-evidence.json","standalone-telemetry-runtime-evidence.json"]
        rows=[{"id":index,"name":name,"state":"uploaded","size":1} for index,name in enumerate(names,1)]
        effect=Effect('channel-asset',self.provider.content_id,'request')
        with patch.object(self.provider,'_release',return_value=release), \
             patch.object(self.api,'get',side_effect=[rows,rows[:-1]]) as read:
            self.assertEqual(self.provider.continuation_visibility(effect),'absent')
            self.assertEqual(read.call_count,1)
            with self.assertRaisesRegex(self.module.Refused,'population'):
                self.provider.continuation_visibility(effect)
            self.assertEqual(read.call_count,2)

    def test_continuation_visibility_never_matches_and_refuses_incomplete_or_duplicate_population(self):
        release={"id":416097241,"tag_name":self.provider.tag,"target_commitish":self.provider.source,
                 "body":self.provider.marker,"draft":True,"prerelease":False}
        names=[*(f"{package}.{self.provider.version}.nupkg" for package in PACKAGES),
               "standalone-telemetry-evidence.json","standalone-telemetry-runtime-evidence.json"]
        rows=[{"id":index,"name":name,"state":"uploaded","size":1} for index,name in enumerate(names,1)]
        effect=Effect("channel-asset",self.provider.content_id,"request")
        with patch.object(self.provider,"_release",return_value=release),patch.object(self.api,"get",return_value=rows):
            self.assertEqual(self.provider.continuation_visibility(effect),"absent")
            for bad in (rows[:-1],rows+[rows[0]],rows+[{"id":8,"name":"other","state":"uploaded","size":1}],
                        [{**rows[0],"state":"new"},*rows[1:]]):
                with self.subTest(bad=bad),patch.object(self.api,"get",return_value=bad),self.assertRaises(self.module.Refused):
                    self.provider.continuation_visibility(effect)
            rows.append({"id":6,"name":"stable-channel.json","state":"uploaded","size":1})
            self.assertEqual(self.provider.continuation_visibility(effect),"potential")

    def http_error(self, request, code=403, body=None, headers=None):
        import urllib.error
        return urllib.error.HTTPError(request.full_url, code, "SECRET-free-form-error", headers or {
            "X-GitHub-Request-Id": "ABC:123", "X-RateLimit-Remaining": "4567",
            "X-RateLimit-Reset": "1791991722", "X-RateLimit-Resource": "core", "Retry-After": "20",
            "X-Accepted-GitHub-Permissions": "contents=write; workflows=write"},
            self.io.BytesIO(body or b'{"message":"Resource not accessible by integration","extra":"SECRET-body"}'))

    def transport(self, request, **kwargs):
        if request.get_method() == "GET":
            reply = self.io.BytesIO(json.dumps(self.release).encode())
            reply.status, reply.headers = 200, {}
            return reply
        self.writes.append((request.get_method(), request.full_url))
        raise self.http_error(request)

    def test_all_github_write_branches_retain_safe_cause_and_remain_unknown(self):
        identities = ["tag", "draft", *("archive-asset:" + x for x in PACKAGES),
                      "qualification-asset:evidence", "qualification-asset:runtime",
                      "channel-asset", "manifest-asset", "promote"]
        for identity in identities:
            with self.subTest(identity=identity), patch.object(self.module.urllib.request, "urlopen", self.transport), \
                 patch.object(self.provider, "_prepare_manifest_asset", return_value=self.root / "promoted-release-manifest.json"), \
                 patch.object(self.provider, "_final_manifest", return_value={}):
                rows = {}
                outcome = publisher.ReportingProvider(self.provider, rows).dispatch(Effect(identity, "d", "r"))
                self.assertEqual("unknown", outcome.state)
                diagnostic = rows["lastDispatch"]["diagnostic"]["request"]
                self.assertEqual(403, diagnostic["status"])
                self.assertEqual("integration-permission-denied", diagnostic["responseClass"])
                self.assertEqual("ABC:123", diagnostic["requestId"])
                self.assertEqual("contents=write; workflows=write", diagnostic["acceptedPermissions"])
                self.assertNotIn("?", diagnostic["path"])
                self.assertNotIn("SECRET", json.dumps(rows))
        self.assertEqual(len(identities), len(self.writes))

    def test_all_feed_write_failures_exclude_command_keys_and_raw_output(self):
        import subprocess
        for feed in ("github", "nuget"):
            for package in PACKAGES:
                with self.subTest(feed=feed, package=package), patch.object(self.module.subprocess, "run", side_effect=
                        subprocess.CalledProcessError(1, ["SECRET-command-key"], output="SECRET-stdout", stderr="SECRET-stderr")):
                    rows = {}
                    outcome = publisher.ReportingProvider(self.provider, rows).dispatch(Effect(feed + ":" + package, "d", "r"))
                    self.assertEqual("unknown", outcome.state)
                    self.assertEqual({"category": "process-failure", "returnCode": 1}, rows["lastDispatch"]["diagnostic"])
                    self.assertNotIn("SECRET", json.dumps(rows))

    def test_bounded_body_headers_and_workflow_class_do_not_echo_untrusted_values(self):
        headers = {"X-GitHub-Request-Id": "SECRET\ninvalid", "Retry-After": "SECRET",
                   "X-RateLimit-Resource": ["SECRET"], "X-RateLimit-Remaining": "SECRET"}
        body = json.dumps({"message": "refusing to allow a GitHub App to create or update workflow 'SECRET' without workflows permission"}).encode()
        error = self.http_error(type("Request", (), {"full_url": "https://api.github.com/repos/FS-GG/.github/git/refs"})(), body=body, headers=headers)
        row = self.module.request_diagnostic(error.url + "?token=SECRET", "POST", 403, headers, error)
        self.assertEqual("workflow-permission-denied", row["responseClass"])
        self.assertNotIn("SECRET", json.dumps(row))
        oversized = self.http_error(type("Request", (), {"full_url": error.url})(), body=b"SECRET" * 1000)
        row = self.module.request_diagnostic(error.url, "POST", 403, {}, oversized)
        self.assertTrue(row["bodyTruncated"])
        self.assertTrue(row["bodyUnavailable"])
        self.assertNotIn("SECRET", json.dumps(row))

    def test_diagnostic_read_failure_preserves_original_http_cause(self):
        request = type("Request", (), {"full_url": "https://api.github.com/repos/FS-GG/.github/git/refs"})()
        error = self.http_error(request)
        error.read = lambda size: (_ for _ in ()).throw(OSError("SECRET-body-read"))
        with patch.object(self.module.urllib.request, "urlopen", side_effect=error):
            with self.assertRaises(self.module.Refused) as caught:
                self.api.post("repos/FS-GG/.github/git/refs", {"ref": "refs/tags/coherent-set/v0.91.3"})
        self.assertIs(error, caught.exception.__cause__)
        self.assertTrue(caught.exception.diagnostic["bodyUnavailable"])
        self.assertNotIn("SECRET", json.dumps(caught.exception.diagnostic))

    def test_reporting_failure_preserves_provider_outcome_and_original_exception(self):
        class BrokenDiagnostic:
            @property
            def last_dispatch_diagnostic(self):
                raise KeyboardInterrupt("SECRET-diagnostic")
            def dispatch(self, effect):
                return Dispatch("unknown")
        rows = {}
        self.assertEqual("unknown", publisher.ReportingProvider(BrokenDiagnostic(), rows).dispatch(Effect("tag", "d", "r")).state)
        self.assertTrue(rows["lastDispatch"]["diagnosticUnavailable"])
        original = NotFound("original-first-cause")
        class RaisedDiagnostic(BrokenDiagnostic):
            def dispatch(self, effect):
                raise original
        with self.assertRaises(NotFound) as caught:
            publisher.ReportingProvider(RaisedDiagnostic(), {}).dispatch(Effect("tag", "d", "r"))
        self.assertIs(original, caught.exception)

    def test_actual_api_and_provider_diagnostic_failures_never_replace_http_or_success(self):
        request = type("Request", (), {"full_url": "https://api.github.com/repos/FS-GG/.github/git/refs"})()
        original = self.http_error(request)
        with patch.object(self.module, "request_diagnostic", side_effect=KeyboardInterrupt("diagnostic failure")), \
             patch.object(self.module.urllib.request, "urlopen", side_effect=original):
            with self.assertRaises(self.module.Refused) as caught:
                self.api.post("repos/FS-GG/.github/git/refs", {})
            self.assertIs(original, caught.exception.__cause__)
            self.assertEqual("unknown", self.provider.dispatch(Effect("tag", "d", "r")).state)
            self.assertTrue(self.provider.last_dispatch_diagnostic["diagnosticUnavailable"])
        reply = self.io.BytesIO(b'{"object":{"sha":"bbbb"}}')
        reply.status, reply.headers = 201, {}
        with patch.object(self.module, "request_diagnostic", side_effect=KeyboardInterrupt("diagnostic failure")), \
             patch.object(self.module.urllib.request, "urlopen", return_value=reply):
            self.assertEqual({"object": {"sha": "bbbb"}}, self.api.post("repos/FS-GG/.github/git/refs", {}))
        with patch.object(self.module, "sanitized_dispatch_diagnostic", side_effect=KeyboardInterrupt("projection failure")), \
             patch.object(self.module.urllib.request, "urlopen", side_effect=self.http_error(request)):
            self.assertEqual("unknown", self.provider.dispatch(Effect("tag", "d", "r")).state)
            self.assertTrue(self.provider.last_dispatch_diagnostic["diagnosticUnavailable"])

    def test_earlier_observation_cause_never_joins_a_later_local_prerequisite_failure(self):
        self.api.last_request_diagnostic = {"method": "POST", "status": 403, "requestId": "STALE"}
        self.provider.last_dispatch_diagnostic = {"category": "process-failure", "returnCode": 1}
        self.provider.nuget_key = ""
        self.assertEqual("unknown", self.provider.dispatch(Effect("nuget:FS.GG.Kit", "d", "r")).state)
        self.assertEqual({"category": "provider-refused"}, self.provider.last_dispatch_diagnostic)


class InstallMetadataTransportControls(unittest.TestCase):
    """Actual _request acquisition; fake HTTP only, never a socket or SDK."""
    def setUp(self):
        import io
        import release_successor_provider as module
        self.module=module;self.io=io;self.budget=module.MetadataRequestBudget()
        self.api=module.GitHubAPI('SECRET-inert',metadata_budget=self.budget)
        self.calls=[];self.reads=[];self.handlers=[];self.responses=[]

    def response(self,raw,headers=None,short=False):
        parent=self
        class Response(self.io.BytesIO):
            status=200
            def read(self,size=-1):
                parent.assertGreater(size,0);parent.assertLessEqual(size,65536)
                parent.reads.append(size)
                return super().read(min(size,7) if short else size)
        response=Response(raw);response.headers=headers or {};self.responses.append(response);return response

    def opener(self,reply=None,error=None):
        parent=self
        def build(*handlers):
            parent.handlers.extend(handlers)
            class Opener:
                def open(self,request,**kwargs):
                    parent.calls.append((request.get_method(),request.full_url))
                    parent.assertEqual(kwargs,{'timeout':60})
                    if error is not None:raise error
                    return reply
            return Opener()
        return build

    def test_exact_cap_and_short_chunks_are_fully_acquired_before_json(self):
        from unittest.mock import patch
        cap=self.module.INSTALL_METADATA_RESPONSE_LIMIT
        raw=b'{"value":"'+b'x'*(cap-12)+b'"}'
        self.assertEqual(len(raw),cap)
        with patch.object(self.module.urllib.request,'build_opener',self.opener(self.response(raw))):
            self.assertEqual(len(self.api.get('repos/FS-GG/.github')['value']),cap-12)
        self.assertEqual(self.budget.attempted,{'GET':1,'POST':0,'PATCH':0})
        self.reads.clear()
        with patch.object(self.module.urllib.request,'build_opener',self.opener(self.response(b'{"valid":true}',short=True))):
            self.assertEqual(self.api.get('repos/FS-GG/.github'),{'valid':True})
        self.assertGreater(len(self.reads),2)
        self.assertTrue(all(response.closed for response in self.responses))

    def test_oversize_and_declared_partial_never_reach_json(self):
        from unittest.mock import patch
        cap=self.module.INSTALL_METADATA_RESPONSE_LIMIT
        for raw,headers in ((b'x'*(cap+2),{}),(b'{}',{'Content-Length':'3'}),(b'{}',{'Content-Length':str(cap+1)})):
            with self.subTest(headers=headers),patch.object(self.module.urllib.request,'build_opener',self.opener(self.response(raw,headers))), \
                 patch.object(self.module.json,'loads',side_effect=AssertionError('JSON before acquisition validation')), \
                 self.assertRaises(self.module.Refused):self.api.get('repos/FS-GG/.github')
            self.assertTrue(self.responses[-1].closed)

    def test_partial_transport_and_malformed_json_keep_post_unconfirmed_without_retry(self):
        import http.client
        from unittest.mock import patch
        class Broken:
            status=200;headers={}
            def __enter__(self):return self
            def __exit__(self,*args):pass
            closed=False
            def read(self,size):raise http.client.IncompleteRead(b'partial',20)
            def close(self):self.closed=True
        for response,error_type in ((Broken(),self.module.Refused),(self.response(b'{broken'),ValueError)):
            before=len(self.calls)
            with patch.object(self.module.urllib.request,'build_opener',self.opener(response)),self.assertRaises(error_type):
                self.api.post('repos/FS-GG/.github/git/blobs',{'inert':True})
            self.assertEqual(len(self.calls),before+1);self.assertTrue(response.closed)

    def test_error_body_is_single_bounded_acquisition_and_original_status_survives(self):
        from unittest.mock import patch
        import urllib.error
        reads=[]
        class ErrorBody(self.io.BytesIO):
            def read(self,size=-1):reads.append(size);return super().read(size)
        body=ErrorBody(b'{"message":"Not Found","padding":"'+b'x'*4096+b'"}')
        error=urllib.error.HTTPError('https://api.github.com/repos/FS-GG/.github',404,'inert',{},body)
        with patch.object(self.module.urllib.request,'build_opener',self.opener(error=error)),self.assertRaises(self.module.NotFound):
            self.api.get('repos/FS-GG/.github')
        self.assertEqual(reads,[2049]);self.assertTrue(body.closed)
        self.assertTrue(self.api.last_request_diagnostic['bodyTruncated']);self.assertEqual(self.api.last_request_diagnostic['status'],404)

    def test_partial_error_body_retains_status_without_second_acquisition(self):
        from unittest.mock import patch
        import urllib.error
        import http.client
        reads=[]
        class PartialError(self.io.BytesIO):
            def read(self,size=-1):
                reads.append(size);raise http.client.IncompleteRead(b'partial',10)
        body=PartialError(b'')
        error=urllib.error.HTTPError('https://api.github.com/repos/FS-GG/.github',503,'inert',{},body)
        with patch.object(self.module.urllib.request,'build_opener',self.opener(error=error)),self.assertRaises(self.module.Refused):
            self.api.get('repos/FS-GG/.github')
        self.assertEqual(reads,[2049]);self.assertTrue(body.closed)
        self.assertEqual(self.api.last_request_diagnostic['status'],503)
        self.assertTrue(self.api.last_request_diagnostic['bodyUnavailable']);self.assertEqual(len(self.calls),1)

    def test_cleanup_failure_is_separate_and_never_replaces_first_http_or_acquisition_cause(self):
        from unittest.mock import patch
        import urllib.error
        class FailingClose(self.io.BytesIO):
            status=200
            def close(self):
                super().close();raise OSError('inert close failed')
        for mode in ('success','oversize','http-error'):
            with self.subTest(mode=mode):
                response=FailingClose(b'{}');response.headers={'Content-Length':'2097153'} if mode=='oversize' else {}
                error=urllib.error.HTTPError('https://api.github.com/repos/FS-GG/.github',503,'inert',{},response) if mode=='http-error' else None
                cause='returned HTTP 503' if mode=='http-error' else 'content length' if mode=='oversize' else 'stream cleanup failed'
                before=self.budget.stream_close_failures
                with patch.object(self.module.urllib.request,'build_opener',self.opener(response,error)),self.assertRaisesRegex(self.module.Refused,cause):
                    self.api.get('repos/FS-GG/.github')
                self.assertTrue(response.closed);self.assertEqual(self.budget.stream_close_failures,before+1)

    def test_shared_counter_stops_before_open_and_fixed_origin_redirect_never_follow(self):
        from unittest.mock import patch
        self.budget.attempted['GET']=255
        writer=self.module.GitHubAPI('SECRET-writer',metadata_budget=self.budget)
        with patch.object(self.module.urllib.request,'build_opener',self.opener(self.response(b'{}'))):
            self.api.get('repos/FS-GG/.github')
            with self.assertRaisesRegex(self.module.Refused,'ceiling'):writer.patch('repos/FS-GG/.github/git/refs/heads/main',{'sha':'b'*40})
        self.assertEqual(len(self.calls),1);self.assertEqual(sum(self.budget.attempted.values()),256)
        with self.assertRaisesRegex(self.module.Refused,'fixed GitHub API origin'):
            self.api._request('https://other.invalid/redirect')
        redirect=next(handler for handler in self.handlers if isinstance(handler,self.module._DenyMetadataRedirect))
        body=self.io.BytesIO(b'undrained')
        with self.assertRaisesRegex(self.module.Refused,'redirect refused'):
            redirect.redirect_request(None,body,302,'Found',{},'https://other.invalid/')
        self.assertTrue(body.closed);self.assertEqual(len(self.calls),1)


if __name__ == "__main__":
    qualify_live_provider()
    unittest.main()
