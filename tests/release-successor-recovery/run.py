#!/usr/bin/env python3
"""Pure offline checkpoint controls; no git subprocess, network or live provider.

Synthetic Git object identifiers identify fixture data, not GitHub evidence.
All setup writes are in memory and counted separately from each recovery slice.
The actual journal, advance policy, publisher admission and protection predicate
are used without importing the existing subprocess-based journal test runner.
"""

import base64
import copy
import hashlib
import importlib.util
import json
import os
import pathlib
import socket
import subprocess
import sys
import tempfile
import types
import urllib.request
import unittest
import zipfile
from urllib.parse import parse_qs, urlsplit
from unittest.mock import patch


def forbidden_effect(*args, **kwargs):
    raise AssertionError("offline recovery controls forbid subprocess and network effects")


def effect_audit(event, args):
    if event in {"subprocess.Popen", "os.system", "os.posix_spawn", "os.exec", "os.fork", "os.forkpty", "socket.__new__",
                 "socket.connect", "socket.getaddrinfo", "socket.bind"}:
        forbidden_effect()


# Install before project imports, so captured transport aliases are also inert.
# The audit hook additionally covers direct calls through saved native aliases.
sys.addaudithook(effect_audit)
subprocess.Popen = subprocess.run = subprocess.call = forbidden_effect
subprocess.check_call = subprocess.check_output = forbidden_effect
os.system = os.popen = forbidden_effect
for name in ("fork", "forkpty", "posix_spawn", "posix_spawnp", "spawnl", "spawnle", "spawnlp", "spawnlpe",
             "spawnv", "spawnve", "spawnvp", "spawnvpe", "execl", "execle", "execlp", "execlpe",
             "execv", "execve", "execvp", "execvpe"):
    if hasattr(os, name):
        setattr(os, name, forbidden_effect)
socket.socket = socket.create_connection = socket.getaddrinfo = forbidden_effect
urllib.request.urlopen = forbidden_effect

ROOT = pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts"))
from release_successor_admission import SingleOperatorAdmission
from release_successor_execution import PACKAGES, Observation, Dispatch, ordered_effects
from release_successor_journal import ProtectedReleaseJournal, REF, REPOSITORY, canonical, Refused as JournalRefused
from release_successor_provider import NotFound, LiveProvider, GitHubAPI, Refused as ProviderRefused

spec = importlib.util.spec_from_file_location("recovery_publisher", ROOT / "scripts/release-successor-publish.py")
publisher = importlib.util.module_from_spec(spec)
spec.loader.exec_module(publisher)
_, BINDING = publisher.authority_protection()
PREFIX = f"repos/{REPOSITORY}"


def manifest():
    descriptor = {"policyVersion": "release-successor/1", "sourceSha": "b" * 40,
                  "standaloneTelemetry": {}, "version": "0.101.0",
                  "packages": [{"id": name, "artifact": {"sha256": str(i) * 64,
                               "payloadSha256": "sha256:" + str(i) * 64}}
                               for i, name in enumerate(PACKAGES, 1)]}
    return {"descriptor": descriptor, "contentId": "sha256:" + hashlib.sha256(
        json.dumps(descriptor, sort_keys=True, separators=(",", ":")).encode()).hexdigest()}


class MemoryAuthority:
    """GitHub-shaped immutable objects and first-parent selected path history."""
    def __init__(self):
        self.objects = {}
        self.calls = []
        self.override = {}
        self.fail_at = None
        self.reject_patch = False
        self.omit_actors = False
        self.controls = copy.deepcopy(BINDING["rulesets"])
        self.quota = {"limit": 5000, "remaining": 4500, "used": 500, "reset": 2000000000}
        releases = self.tree([])
        state = self.tree([self.entry("releases", "tree", releases)])
        root = self.tree([self.entry("state", "tree", state)])
        self.head = self.commit(root, [], "fixture base")

    @staticmethod
    def entry(name, kind, oid):
        return {"path": name, "type": kind, "mode": "040000" if kind == "tree" else "100644", "sha": oid}

    def put(self, kind, value):
        oid = hashlib.sha1((kind + json.dumps(value, sort_keys=True)).encode()).hexdigest()
        self.objects[(kind, oid)] = {"sha": oid, **copy.deepcopy(value)}
        return oid

    def tree(self, rows):
        return self.put("trees", {"truncated": False, "tree": sorted(rows, key=lambda row: row["path"])})

    def commit(self, tree, parents, message):
        return self.put("commits", {"tree": {"sha": tree}, "parents": [{"sha": x} for x in parents], "message": message})

    def update_tree(self, oid, parts, entry):
        rows = copy.deepcopy(self.objects[("trees", oid)]["tree"])
        old = next((row for row in rows if row["path"] == parts[0]), None)
        if len(parts) == 1:
            new = {**entry, "path": parts[0]}
        else:
            child = old["sha"] if old else self.tree([])
            new = self.entry(parts[0], "tree", self.update_tree(child, parts[1:], entry))
        return self.tree([row for row in rows if row["path"] != parts[0]] + [new])

    def selected(self, commit):
        oid = self.objects[("commits", commit)]["tree"]["sha"]
        for name in ("state", "releases", "utel-rel-19"):
            row = next((x for x in self.objects[("trees", oid)]["tree"] if x["path"] == name), None)
            if row is None:
                return None
            oid = row["sha"]
        return oid

    def call(self, method, path):
        self.calls.append((method, path))
        if len(self.calls) == self.fail_at:
            raise RuntimeError("fixture transport response unknown")
        if path in self.override:
            value = self.override[path]
            if isinstance(value, BaseException):
                raise value
            return copy.deepcopy(value)

    def get(self, path):
        value = self.call("get", path)
        if value is not None:
            return value
        if path == "rate_limit":
            return {"resources": {"core": copy.deepcopy(self.quota)}}
        if path == PREFIX:
            return {"id": 1351660651, "full_name": REPOSITORY}
        if path == PREFIX + "/git/ref/heads/main":
            return {"object": {"sha": self.head, "type": "commit"}}
        if path == PREFIX + "/git/ref/" + REF.removeprefix("refs/"):
            raise NotFound(path)
        if "/git/" in path:
            kind, oid = path.split("/git/", 1)[1].split("/", 1)
            return copy.deepcopy(self.objects[(kind, oid)])
        if path.startswith(PREFIX + "/commits?"):
            query = parse_qs(urlsplit(path).query)
            assert query["path"] == ["state/releases/utel-rel-19"]
            oid = query["sha"][0]
            rows = []
            while oid:
                commit = self.objects[("commits", oid)]
                parent = commit["parents"][0]["sha"] if commit["parents"] else None
                if self.selected(oid) != (self.selected(parent) if parent else None):
                    rows.append({"sha": oid})
                oid = parent
            start = (int(query["page"][0]) - 1) * 100
            return rows[start:start + 100]
        if "/rulesets/" in path:
            value = copy.deepcopy(next(row for row in self.controls.values() if row["id"] == int(path.rsplit("/", 1)[1])))
            if self.omit_actors:
                value.pop("bypass_actors")
            return value
        if "/rules/branches/" in path:
            pairs = [("creation", 24802693), ("update", 24802693), ("deletion", 24802698), ("non_fast_forward", 24802698)] if path.endswith("/main") else [("creation", 24812732), ("update", 24812732)]
            return [{"type": kind, "ruleset_id": identifier, "ruleset_source_type": "Repository", "ruleset_source": REPOSITORY} for kind, identifier in pairs]
        raise AssertionError("unselected fixture read " + path)

    def post(self, path, body):
        self.call("post", path)
        kind = path.rsplit("/", 1)[1]
        if kind == "blobs":
            oid = self.put(kind, {"encoding": body["encoding"], "content": body["content"]})
        elif kind == "trees":
            oid = body.get("base_tree") or self.tree([])
            for row in body["tree"]:
                oid = self.update_tree(oid, row["path"].split("/"), row)
        elif kind == "commits":
            oid = self.commit(body["tree"], body["parents"], body["message"])
        else:
            raise AssertionError(path)
        return {"sha": oid}

    def patch(self, path, body):
        self.call("patch", path)
        assert path == PREFIX + "/git/refs/heads/main" and body["force"] is False
        if self.reject_patch:
            raise RuntimeError("fixture CAS conflict")
        assert self.objects[("commits", body["sha"])]["parents"][0]["sha"] == self.head
        self.head = body["sha"]
        return {"object": {"sha": self.head}}


class MemoryPublisher:
    def __init__(self):
        self.head = "b" * 40
        self.calls = []

    def get(self, path):
        self.calls.append(path)
        if path == "repos/FS-GG/.github":
            return {"id": 1269292704, "full_name": "FS-GG/.github"}
        if path.endswith("/actions/runs/1"):
            return {"repository": {"id": 1269292704}, "path": ".github/workflows/release-successor-publish.yml",
                    "event": "workflow_dispatch", "head_branch": "main", "head_sha": "b" * 40,
                    "actor": {"login": "EHotwagner"}, "run_attempt": 1, "status": "in_progress"}
        if path.endswith("/git/ref/heads/main"):
            return {"object": {"sha": self.head}}
        raise NotFound(path)


class MemoryProvider:
    def __init__(self, effects, state):
        self.matched = {identity for identity, value in state.items() if value in {"intent", "verified"}}
        self.dispatched = []
        self.fail_observe = None
        self.fail_dispatch = False

    def observe(self, effect):
        if effect.identity == self.fail_observe:
            raise RuntimeError("fixture unreadable provider")
        return Observation("matched", effect.target_digest) if effect.identity in self.matched else Observation("absent")

    def dispatch(self, effect):
        self.dispatched.append(effect.identity)
        if self.fail_dispatch:
            raise RuntimeError("fixture dispatch uncertainty")
        self.matched.add(effect.identity)
        return Dispatch("applied")


def report():
    return {"startingJournal": None, "lastObservedJournal": None, "iterationsAttempted": 0,
            "lastAdvanceResult": None, "lastDispatch": None, "journalMutationMayHaveOccurred": False,
            "publicationComplete": False}


def fixture(generation):
    candidate = manifest()
    effects = ordered_effects(candidate)
    authority = MemoryAuthority()
    setup = ProtectedReleaseJournal(authority, main_directory=True)
    intent = {"contentId": candidate["contentId"], "sourceSha": "b" * 40, "version": "0.101.0",
              "candidateArchiveSha256": "a" * 64, "operator": "EHotwagner"}
    state = setup.initialize(intent)
    for transition in range(generation - 1):
        effect = effects[transition // 2].identity
        assert setup.compare_and_swap(state, effect, "intent" if transition % 2 == 0 else "verified")
        state = setup.read()
    setup_calls = len(authority.calls)
    authority.calls.clear()
    return candidate, authority, intent, setup_calls


def slice_context(candidate, authority, intent):
    counter = publisher.CountedAuthorityAPI(authority)
    counter.admit_quota(now=1900000000)
    rows = report()
    journal = publisher.ReportingJournal(ProtectedReleaseJournal(counter, main_directory=True), rows)
    publisher_api = MemoryPublisher()
    provider = MemoryProvider(ordered_effects(candidate), {})
    admission = publisher.ProtectedPublisherAdmission(
        SingleOperatorAdmission(publisher_api, candidate, "b" * 40, 1, "EHotwagner", "refs/heads/main"), counter, REF)
    publisher.prepare_release_journal(journal, counter, admission, provider, publisher_api, candidate, intent,
                                      "b" * 40, "b" * 40, False, lambda: None, recovery_checkpoint=True)
    provider.matched = set(journal._observed.state["effects"])
    return counter, rows, journal, admission, publisher.ReportingProvider(provider, rows), publisher_api


class CASFreshnessControls(unittest.TestCase):
    """Actual journal/admission/provider paths with in-memory Authority races."""

    @staticmethod
    def sibling(authority, name):
        prior = authority.head
        blob = authority.put("blobs", {"encoding": "base64", "content": base64.b64encode(b"fresh sibling\n").decode()})
        tree = authority.update_tree(authority.objects[("commits", prior)]["tree"]["sha"],
                                     [name], authority.entry(name, "blob", blob))
        authority.head = authority.commit(tree, [prior], "unrelated sibling")
        return authority.head

    def test_counted_actual_advance_refreshes_after_package_observation(self):
        candidate, authority, intent, _ = fixture(14)
        counter, rows, journal, admission, provider, _ = slice_context(candidate, authority, intent)
        original_observe = provider.provider.observe
        refreshed = []
        def observe(effect):
            result = original_observe(effect)
            if effect.identity == ordered_effects(candidate)[6].identity:
                refreshed.append(self.sibling(authority, "package-window-sibling.txt"))
            return result
        provider.provider.observe = observe
        authority.calls.clear()
        self.assertEqual(publisher.advance(candidate, journal, admission, provider), "verified")
        current = authority.objects[("commits", authority.head)]
        self.assertEqual(current["parents"][0]["sha"], refreshed[0])
        self.assertEqual(current["parents"][1]["sha"], journal._observed.head)
        root = authority.objects[("trees", current["tree"]["sha"])]["tree"]
        self.assertTrue(any(row["path"] == "package-window-sibling.txt" for row in root))
        self.assertEqual(journal.read().generation, 15)
        self.assertEqual(provider.provider.dispatched, [])
        self.assertIsNone(rows["lastDispatch"])
        self.assertEqual(sum(method == "patch" for method, _ in authority.calls), 1)
        self.assertLess(sum(counter.attempted.values()), 4200)

    def test_counted_pending_intent_refresh_preserves_one_dispatch(self):
        candidate, authority, intent, _ = fixture(15)
        counter, rows, journal, admission, provider, _ = slice_context(candidate, authority, intent)
        original_observe = provider.provider.observe
        siblings = []
        target = ordered_effects(candidate)[7].identity
        def observe(effect):
            result = original_observe(effect)
            if effect.identity == target:
                siblings.append(self.sibling(authority, "intent-window-sibling.txt"))
            return result
        provider.provider.observe = observe
        self.assertEqual(publisher.advance(candidate, journal, admission, provider), "waiting")
        self.assertEqual(provider.provider.dispatched, [target])
        self.assertEqual(rows["lastDispatch"]["effect"], target)
        self.assertEqual(journal.read().effects[target], "intent")
        self.assertEqual(authority.objects[("commits", authority.head)]["parents"][0]["sha"], siblings[0])
        self.assertLess(sum(counter.attempted.values()), 4200)

    def test_pending_late_fence_prevents_provider_dispatch(self):
        candidate, authority, intent, _ = fixture(15)
        _, rows, journal, admission, provider, _ = slice_context(candidate, authority, intent)
        original_post = authority.post
        posts = []
        def post(path, body):
            value = original_post(path, body)
            posts.append(path)
            if len(posts) == 7:
                self.sibling(authority, "pending-late-sibling.txt")
            return value
        authority.post = post
        authority.calls.clear()
        with self.assertRaisesRegex(JournalRefused, "moved before CAS PATCH"):
            publisher.advance(candidate, journal, admission, provider)
        self.assertEqual(len(posts), 7)
        self.assertEqual(provider.provider.dispatched, [])
        self.assertIsNone(rows["lastDispatch"])
        self.assertFalse(any(method == "patch" for method, _ in authority.calls))
        self.assertEqual(ProtectedReleaseJournal(authority, main_directory=True).read().generation, 15)

    def test_changed_canonical_bytes_refuse_before_objects(self):
        candidate, authority, intent, _ = fixture(14)
        _, _, journal, _, _, _ = slice_context(candidate, authority, intent)
        expected = journal.read()
        underlying = journal.journal
        changed = {**underlying._observed.state, "operator": "other-fixture-operator"}
        overlay = underlying._overlay(authority.head, underlying._observed.head, changed)
        authority.head = overlay
        authority.calls.clear()
        with self.assertRaisesRegex(JournalRefused, "state differs from its logical head"):
            journal.compare_and_swap(expected, ordered_effects(candidate)[6].identity, "verified")
        self.assertTrue(all(method == "get" for method, _ in authority.calls))
        self.assertEqual(authority.head, overlay)

    def test_selected_change_refuses_before_objects_or_patch(self):
        candidate, authority, intent, _ = fixture(14)
        _, rows, journal, _, provider, _ = slice_context(candidate, authority, intent)
        expected = journal.read()
        competing = ProtectedReleaseJournal(authority, main_directory=True)
        self.assertTrue(competing.compare_and_swap(competing.read(), ordered_effects(candidate)[6].identity, "verified"))
        changed = authority.head
        authority.calls.clear()
        with self.assertRaisesRegex(JournalRefused, "selected binding changed"):
            journal.compare_and_swap(expected, ordered_effects(candidate)[6].identity, "verified")
        self.assertEqual(authority.head, changed)
        self.assertTrue(all(method == "get" for method, _ in authority.calls))
        self.assertEqual(provider.provider.dispatched, [])
        self.assertEqual(rows["lastObservedJournal"]["generation"], 14)

    def test_movement_during_freshness_read_stops_before_objects(self):
        candidate, authority, intent, _ = fixture(14)
        _, _, journal, _, _, _ = slice_context(candidate, authority, intent)
        expected = journal.read()
        original_get = authority.get
        ref_reads = []
        def get(path):
            if path == PREFIX + "/git/ref/heads/main":
                ref_reads.append(path)
                if len(ref_reads) == 2:
                    self.sibling(authority, "read-window-sibling.txt")
            return original_get(path)
        authority.get = get
        authority.calls.clear()
        with self.assertRaisesRegex(JournalRefused, "moved during CAS freshness read"):
            journal.compare_and_swap(expected, ordered_effects(candidate)[6].identity, "verified")
        self.assertTrue(all(method == "get" for method, _ in authority.calls))
        self.assertEqual(len(ref_reads), 2)

    def test_late_object_window_movement_stops_without_patch_or_retry(self):
        candidate, authority, intent, _ = fixture(14)
        _, rows, journal, _, provider, _ = slice_context(candidate, authority, intent)
        expected = journal.read()
        original_post = authority.post
        posts = []
        def post(path, body):
            value = original_post(path, body)
            posts.append(path)
            if len(posts) == 7:
                self.sibling(authority, "object-window-sibling.txt")
            return value
        authority.post = post
        authority.calls.clear()
        with self.assertRaisesRegex(JournalRefused, "moved before CAS PATCH"):
            journal.compare_and_swap(expected, ordered_effects(candidate)[6].identity, "verified")
        self.assertEqual(len(posts), 7)
        self.assertFalse(any(method == "patch" for method, _ in authority.calls))
        self.assertEqual(provider.provider.dispatched, [])
        self.assertTrue(rows["journalMutationMayHaveOccurred"])
        self.assertEqual(ProtectedReleaseJournal(authority, main_directory=True).read().generation, 14)

    def test_after_final_fence_patch_race_remains_uncertain_once(self):
        candidate, authority, intent, _ = fixture(14)
        _, rows, journal, _, provider, _ = slice_context(candidate, authority, intent)
        expected = journal.read()
        original_patch = authority.patch
        attempts = []
        def patch_at_boundary(path, body):
            attempts.append(body)
            self.sibling(authority, "patch-window-sibling.txt")
            return original_patch(path, body)
        authority.patch = patch_at_boundary
        with self.assertRaisesRegex(JournalRefused, "journal CAS response uncertain"):
            journal.compare_and_swap(expected, ordered_effects(candidate)[6].identity, "verified")
        self.assertEqual(len(attempts), 1)
        self.assertIs(attempts[0]["force"], False)
        self.assertTrue(rows["journalMutationMayHaveOccurred"])
        self.assertEqual(provider.provider.dispatched, [])
        self.assertEqual(ProtectedReleaseJournal(authority, main_directory=True).read().generation, 14)

    def test_applied_lost_response_stops_until_independent_reread(self):
        candidate, authority, intent, _ = fixture(14)
        _, rows, journal, _, provider, _ = slice_context(candidate, authority, intent)
        expected = journal.read()
        original_patch = authority.patch
        attempts = []
        def lost_response(path, body):
            attempts.append(body)
            original_patch(path, body)
            raise RuntimeError("lost response after application")
        authority.patch = lost_response
        with self.assertRaisesRegex(JournalRefused, "journal CAS response uncertain"):
            journal.compare_and_swap(expected, ordered_effects(candidate)[6].identity, "verified")
        self.assertEqual(len(attempts), 1)
        self.assertTrue(rows["journalMutationMayHaveOccurred"])
        self.assertEqual(rows["lastObservedJournal"]["generation"], 14)
        self.assertEqual(provider.provider.dispatched, [])
        independent = ProtectedReleaseJournal(authority, main_directory=True)
        self.assertEqual(independent.read().generation, 15)


class RecoveryControls(unittest.TestCase):
    def test_effect_guards_are_inert(self):
        for call, args in ((subprocess.run, (["git", "status"],)),
                           (socket.create_connection, (("invalid", 443),)),
                           (urllib.request.urlopen, ("https://invalid/",)),
                           (os.system, ("git status",))):
            with self.assertRaises(AssertionError): call(*args)

    def test_original_four_slice_trace_and_bounds(self):
        candidate, authority, intent, setup_calls = fixture(14)
        dispatches = []
        measurements = []
        for start in (14, 18, 22, 26, 30, 33):
            authority.calls.clear()
            counter, rows, journal, admission, provider, _ = slice_context(candidate, authority, intent)
            self.assertEqual(rows["startingJournal"]["generation"], start)
            sleeps = []
            disposition = publisher.run_recovery_checkpoint(candidate, journal, admission, provider, rows, 10,
                                                            clock=lambda: 0, sleep=sleeps.append)
            self.assertEqual(disposition, "complete" if start >= 30 else "checkpoint")
            self.assertEqual(rows["iterationsAttempted"], 1 if start == 33 else 4)
            self.assertEqual(sleeps, [] if start == 33 else [5, 5, 5])
            self.assertLessEqual(sum(counter.attempted.values()), 4080)
            self.assertEqual(sum(counter.attempted.values()), len(authority.calls))
            self.assertFalse(rows["publicationComplete"])  # Only main records the returned disposition.
            dispatches.extend(provider.dispatched)
            measurements.append({"start": start, "calls": dict(counter.attempted)})
        self.assertEqual(len(dispatches), len(set(dispatches)))
        self.assertNotIn(ordered_effects(candidate)[6].identity, dispatches)
        print(json.dumps({"fixtureSetupCalls": setup_calls, "slices": measurements}, sort_keys=True))

    def test_absent_original_intent_never_redispatches_or_rereads(self):
        candidate, authority, intent, _ = fixture(14)
        counter, rows, journal, admission, provider, _ = slice_context(candidate, authority, intent)
        provider.matched.remove(ordered_effects(candidate)[6].identity)
        with patch.object(journal, "read", wraps=journal.read) as reads:
            self.assertEqual(publisher.run_recovery_checkpoint(candidate, journal, admission, provider, rows, 10,
                             clock=lambda: 0, sleep=lambda _: None), "checkpoint")
            self.assertEqual(reads.call_count, 4)
        self.assertEqual(provider.dispatched, [])
        self.assertEqual(rows["lastObservedJournal"]["generation"], 14)

    def test_quota_floor_shape_and_403(self):
        for remaining, accepted in ((4499, False), (4500, True)):
            authority = MemoryAuthority()
            authority.quota.update(remaining=remaining, used=5000 - remaining)
            counter = publisher.CountedAuthorityAPI(authority)
            if accepted:
                counter.admit_quota(now=1900000000)
            else:
                with self.assertRaises(publisher.Refused):
                    counter.admit_quota(now=1900000000)
            self.assertEqual(counter.attempted, {"get": 1, "post": 0, "patch": 0})
        for reset in (True, "2000000000", 0, 1900000000):
            authority = MemoryAuthority()
            authority.quota["reset"] = reset
            with self.assertRaises(publisher.Refused):
                publisher.CountedAuthorityAPI(authority).admit_quota(now=1900000000)
        authority = MemoryAuthority()
        authority.override["rate_limit"] = RuntimeError("403 core remaining=0")
        counter = publisher.CountedAuthorityAPI(authority)
        with self.assertRaises(RuntimeError):
            counter.admit_quota()
        self.assertEqual(len(authority.calls), 1)
        self.assertEqual(counter.succeeded["get"], 0)

    def test_ceiling_stops_each_primitive_before_send(self):
        for method in ("get", "post", "patch"):
            authority = MemoryAuthority()
            counter = publisher.CountedAuthorityAPI(authority)
            counter.attempted["get"] = 4200
            with self.assertRaises(publisher.Refused):
                getattr(counter, method)("fixture", *([] if method == "get" else [{}]))
            self.assertEqual(authority.calls, [])
            self.assertEqual(counter.blocked["method"], method)

    def test_native_omitted_actors_keep_original_reviewed_binding_semantics(self):
        candidate, authority, intent, _ = fixture(14)
        _, rows, journal, admission, provider, _ = slice_context(candidate, authority, intent)
        authority.omit_actors = True
        self.assertEqual(publisher.run_recovery_checkpoint(candidate, journal, admission, provider, rows, 10,
                         clock=lambda: 0, sleep=lambda _: None), "checkpoint")
        self.assertEqual(rows["lastObservedJournal"]["generation"], 18)

    def test_live_admission_and_cas_failures_block_dispatch(self):
        for mode in ("protection", "source", "cas", "cache", "quota-boundary", "verified-observation"):
            candidate, authority, intent, _ = fixture(14)
            counter, rows, journal, admission, provider, publisher_api = slice_context(candidate, authority, intent)
            if mode == "protection": authority.controls["mainWriter"]["enforcement"] = "disabled"
            if mode == "source": publisher_api.head = "c" * 40
            if mode == "cas": authority.reject_patch = True
            if mode == "cache": journal.journal._MAX_IMMUTABLE_OBJECTS = 0; journal.journal._immutable_objects.clear()
            if mode == "quota-boundary": counter.attempted["get"] = 4200
            if mode == "verified-observation": provider.provider.fail_observe = "tag"
            with self.assertRaises(RuntimeError):
                publisher.run_recovery_checkpoint(candidate, journal, admission, provider, rows, 10,
                                                 clock=lambda: 0, sleep=lambda _: None)
            self.assertEqual(provider.dispatched, [])
            self.assertFalse(rows["publicationComplete"])

    def test_same_oid_corruption_is_rechecked(self):
        candidate, authority, intent, _ = fixture(14)
        _, rows, journal, _, _, _ = slice_context(candidate, authority, intent)
        logical = journal._observed.head
        tree = authority.objects[("commits", logical)]["tree"]["sha"]
        blob = authority.objects[("trees", tree)]["tree"][0]["sha"]
        value = json.loads(base64.b64decode(authority.objects[("blobs", blob)]["content"]))
        value["generation"] = 15
        authority.objects[("blobs", blob)]["content"] = base64.b64encode(canonical(value)).decode()
        with self.assertRaises(RuntimeError): journal.read()
        self.assertEqual(rows["lastObservedJournal"]["generation"], 14)

    def test_read_movement_and_draft_failure_retain_prior_observation(self):
        for mode in ("moved-head", "draft-unreadable"):
            candidate, authority, intent, _ = fixture(14)
            _, rows, journal, admission, provider, _ = slice_context(candidate, authority, intent)
            last = copy.deepcopy(rows["lastObservedJournal"])
            original_get = authority.get
            main_reads = []
            def moved_get(path):
                value = original_get(path)
                if path == PREFIX + "/git/ref/heads/main":
                    main_reads.append(path)
                    if len(main_reads) == 2:
                        value["object"]["sha"] = "f" * 40
                return value
            if mode == "moved-head":
                authority.get = moved_get
            else:
                provider.provider.fail_observe = "draft"
            with self.assertRaises(RuntimeError):
                publisher.run_recovery_checkpoint(candidate, journal, admission, provider, rows, 10,
                                                 clock=lambda: 0, sleep=lambda _: None)
            self.assertEqual(rows["lastObservedJournal"], last)
            self.assertEqual(provider.dispatched, [])

    def test_ceiling_at_remote_boundary_does_not_dispatch(self):
        candidate, authority, intent, _ = fixture(15)
        counter, rows, journal, admission, provider, _ = slice_context(candidate, authority, intent)
        original_cas = journal.compare_and_swap
        def exhausted_after_intent(*args):
            result = original_cas(*args)
            counter.attempted["get"] = 4200 - counter.attempted["post"] - counter.attempted["patch"]
            return result
        with patch.object(journal, "compare_and_swap", side_effect=exhausted_after_intent):
            with self.assertRaises(RuntimeError):
                publisher.run_recovery_checkpoint(candidate, journal, admission, provider, rows, 10,
                                                 clock=lambda: 0, sleep=lambda _: None)
        self.assertEqual(provider.dispatched, [])
        self.assertEqual(rows["lastObservedJournal"]["generation"], 16)
        self.assertEqual(sum(counter.attempted.values()), 4200)

    def test_fresh_destination_refused_before_write(self):
        candidate = manifest()
        authority = MemoryAuthority()
        counter = publisher.CountedAuthorityAPI(authority)
        rows = report()
        journal = publisher.ReportingJournal(ProtectedReleaseJournal(counter, main_directory=True), rows)
        with self.assertRaises(publisher.Refused):
            publisher.prepare_release_journal(journal, counter, None, None, None, candidate, {},
                                             "b" * 40, "b" * 40, False, lambda: None,
                                             recovery_checkpoint=True)
        self.assertTrue(all(method == "get" for method, _ in authority.calls))

    def test_checkpoint_report_is_explicitly_incomplete(self):
        argv = ["publisher", "--publish", "--recovery-checkpoint", "--candidate-run-id", "38026714312",
                "--candidate-artifact-id", "11660242226", "--candidate-archive-sha256", "a" * 64]
        with tempfile.TemporaryDirectory() as directory:
            result = pathlib.Path(directory) / "result.json"
            with patch.object(sys, "argv", argv + ["--workdir", directory + "/candidate", "--result-path", str(result)]), \
                 patch.object(publisher, "execute_publisher", return_value="checkpoint"), patch.dict(publisher.os.environ, {}, clear=True):
                self.assertEqual(publisher.main(), 0)
            value = json.loads(result.read_text())
            self.assertEqual(value["disposition"], "checkpoint")
            self.assertFalse(value["publicationComplete"])
            self.assertLessEqual(result.stat().st_size, 16384)
            with self.assertRaises(FileExistsError): publisher.write_result(result, value)

    def test_deadline_no_fifth_and_dispatch_uncertainty(self):
        candidate, authority, intent, _ = fixture(14)
        _, rows, journal, admission, provider, _ = slice_context(candidate, authority, intent)
        with self.assertRaises(publisher.Refused):
            publisher.run_recovery_checkpoint(candidate, journal, admission, provider, rows, 0,
                                             clock=lambda: 0, sleep=lambda _: None)
        self.assertEqual(rows["iterationsAttempted"], 0)
        provider.provider.fail_dispatch = True
        with self.assertRaises(RuntimeError):
            publisher.run_recovery_checkpoint(candidate, journal, admission, provider, rows, 10,
                                             clock=lambda: 0, sleep=lambda _: None)
        self.assertEqual(rows["lastDispatch"]["state"], "unknown")
        self.assertEqual(len(provider.dispatched), 1)

    def test_result_failure_preserves_primary_in_stderr(self):
        argv = ["publisher", "--publish", "--recovery-checkpoint", "--candidate-run-id", "38026714312",
                "--candidate-artifact-id", "11660242226", "--candidate-archive-sha256", "a" * 64,
                "--workdir", "/offline/unused", "--result-path", "/offline/report"]
        import io
        with patch.object(sys, "argv", argv), patch.object(publisher, "execute_publisher", side_effect=RuntimeError("primary unknown")), \
             patch.object(publisher, "write_result", side_effect=OSError("report unavailable")), patch.object(sys, "stderr", new_callable=io.StringIO) as output:
            self.assertEqual(publisher.main(), 1)
            self.assertIn("primary unknown", output.getvalue())
            self.assertIn("report unavailable", output.getvalue())


def repeat_candidate(candidate):
    effect = ordered_effects(candidate)[6]
    return {"effect": publisher.KIT_EFFECT, "packageId": "FS.GG.Kit", "version": "0.101.0",
            "feed": "nuget.org", "archiveSha256": effect.request_digest,
            "payloadSha256": effect.target_digest,
            "argv": ["dotnet", "nuget", "push", "/fixture/FS.GG.Kit.0.101.0.nupkg", "--source",
                     publisher.KIT_ENDPOINT, "--api-key", "<credential omitted>"],
            "localFile": {"device": 1, "inode": 2, "size": 3, "mtimeNs": 4}}


class OriginalKitRepeatControls(unittest.TestCase):
    def context(self, matched=False):
        candidate, authority, intent, setup = fixture(14)
        counter, rows, journal, admission, provider, source = slice_context(candidate, authority, intent)
        if not matched:
            provider.matched.remove(publisher.KIT_EFFECT)
        return candidate, authority, counter, rows, journal, admission, provider, source, setup

    def run_repeat(self, context, path, verify=None):
        candidate, _, counter, rows, journal, admission, provider, _, _ = context
        # The fixture's synthetic Git identifiers are not original native evidence.
        # Bind only this internal guard constant to the fixture's synthetic head.
        with patch.object(publisher, "ORIGINAL_KIT_JOURNAL_HEAD", journal._observed.head), \
             patch.object(publisher, "ORIGINAL_KIT_STATE_SHA256", hashlib.sha256(canonical(journal._observed.state)).hexdigest()):
            return publisher.run_original_kit_repeat(candidate, journal, admission, provider, counter,
                   rows, 10, verify or (lambda: repeat_candidate(candidate)), path, clock=lambda: 0)

    def test_explicit_absent_repeat_once_immediate_checkpoint_and_count(self):
        context = self.context()
        candidate, authority, counter, rows, journal, _, provider, _, setup = context
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory) / "attempt.json"
            with patch.object(journal, "read", wraps=journal.read) as reads:
                self.assertEqual(self.run_repeat(context, path), "checkpoint")
                self.assertEqual(reads.call_count, 2)  # Ordinary read plus one unchanged read.
            attempt = json.loads(path.read_text())
            self.assertEqual(attempt["effect"]["feed"], "nuget.org")
            self.assertFalse(attempt["historicalNonDispatchProved"])
            self.assertFalse(attempt["globalOnceClaim"])
            self.assertEqual(attempt["journal"]["generation"], 14)
            self.assertEqual(attempt["providerOutcome"], "not yet delegated; any prior send remains uncertain")
            self.assertEqual(provider.dispatched, [publisher.KIT_EFFECT])
            self.assertEqual(journal._observed.state["generation"], 14)
            self.assertEqual(rows["lastDispatch"]["state"], "applied")
            self.assertFalse(rows["publicationComplete"])
            self.assertLessEqual(path.stat().st_size, 16384)
        self.assertTrue(all(method == "get" for method, _ in authority.calls))
        self.assertEqual(sum(counter.attempted.values()), len(authority.calls))
        self.assertLessEqual(sum(counter.attempted.values()), 4080)
        print(json.dumps({"kitRepeatAbsent": {"setupCalls": setup, "calls": counter.attempted}}, sort_keys=True))

    def test_already_matched_settles_without_repeat_or_extra_read(self):
        context = self.context(matched=True)
        _, authority, counter, rows, journal, _, provider, _, setup = context
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory) / "attempt.json"
            verify = unittest.mock.Mock(side_effect=AssertionError("matched must not prepare a push"))
            with patch.object(journal, "read", wraps=journal.read) as reads:
                self.assertEqual(self.run_repeat(context, path, verify), "checkpoint")
                self.assertEqual(reads.call_count, 1)
            self.assertFalse(path.exists())
            self.assertEqual(provider.dispatched, [])
            self.assertEqual(journal._observed.state["generation"], 15)
            self.assertEqual(rows["kitRepeat"]["branch"], "matched-settlement-no-send")
            self.assertEqual(rows["lastAdvanceResult"], "verified")
        self.assertLessEqual(sum(counter.attempted.values()), 4080)
        print(json.dumps({"kitRepeatMatched": {"setupCalls": setup, "calls": counter.attempted}}, sort_keys=True))

    def test_ordinary_absent_intent_has_no_repeat_permission(self):
        candidate, _, _, rows, journal, admission, provider, _, _ = self.context()
        self.assertEqual(publisher.advance(candidate, journal, admission, provider), "waiting")
        self.assertEqual(provider.dispatched, [])
        self.assertEqual(journal._observed.state["generation"], 14)

    def test_unreadable_conflicting_target_and_predecessors_never_send(self):
        for mode in ("target-unreadable", "target-unknown", "target-conflict", "predecessor-absent", "predecessor-unreadable"):
            context = self.context()
            candidate, _, _, _, _, _, provider, _, _ = context
            original = provider.provider.observe
            def observe(effect):
                if mode == "target-unreadable" and effect.identity == publisher.KIT_EFFECT:
                    raise RuntimeError("target unreadable")
                if mode in {"target-unknown", "target-conflict"} and effect.identity == publisher.KIT_EFFECT:
                    return Observation("unknown" if mode == "target-unknown" else "mismatched")
                return original(effect)
            provider.provider.observe = observe
            if mode == "predecessor-absent": provider.matched.remove("tag")
            if mode == "predecessor-unreadable": provider.provider.fail_observe = "draft"
            with tempfile.TemporaryDirectory() as directory, self.assertRaises(RuntimeError):
                self.run_repeat(context, pathlib.Path(directory) / "attempt.json")
            self.assertEqual(provider.dispatched, [])

    def test_changed_journal_head_prefix_and_native_admission_refuse(self):
        for mode in ("wrong-head", "wrong-prefix", "physical-movement", "source-movement", "protection-drift"):
            context = self.context()
            candidate, authority, _, _, journal, _, provider, source, _ = context
            verify = lambda: repeat_candidate(candidate)
            if mode == "wrong-prefix":
                journal._observed.state["effects"]["promote"] = "intent"
            if mode == "physical-movement":
                original = provider.provider.observe
                def observe(effect):
                    value = original(effect)
                    if effect.identity == publisher.KIT_EFFECT:
                        tree = authority.objects[("commits", authority.head)]["tree"]["sha"]
                        authority.head = authority.commit(tree, [authority.head], "unrelated main movement")
                    return value
                provider.provider.observe = observe
            if mode in {"source-movement", "protection-drift"}:
                def verify():
                    if mode == "source-movement": source.head = "c" * 40
                    else: authority.controls["mainWriter"]["enforcement"] = "disabled"
                    return repeat_candidate(candidate)
            with tempfile.TemporaryDirectory() as directory:
                path = pathlib.Path(directory) / "attempt.json"
                if mode == "wrong-head":
                    with self.assertRaises(RuntimeError):
                        publisher.run_original_kit_repeat(candidate, journal, context[5], provider, context[2],
                                                        context[3], 10, verify, path, clock=lambda: 0)
                else:
                    with self.assertRaises(RuntimeError): self.run_repeat(context, path, verify)
            self.assertEqual(provider.dispatched, [])

    def test_wrong_effect_feed_version_digests_and_presend_failures_never_send(self):
        for mode in ("effect", "feed", "version", "archive", "payload", "route", "record", "existing-record", "local-drift"):
            context = self.context()
            candidate, _, _, rows, _, _, provider, _, _ = context
            calls = []
            def verify():
                value = repeat_candidate(candidate)
                calls.append(True)
                if mode == "effect": value["effect"] = "nuget:FS.GG.Drivers"
                if mode == "feed": value["feed"] = "github"
                if mode == "version": value["version"] = "0.102.0"
                if mode == "archive": value["archiveSha256"] = "a" * 64
                if mode == "payload": value["payloadSha256"] = "sha256:" + "a" * 64
                if mode == "route": value["argv"][5] = "https://nuget.pkg.github.com/FS-GG/index.json"
                if mode == "local-drift" and len(calls) == 2: value["localFile"]["inode"] = 9
                return value
            with tempfile.TemporaryDirectory() as directory:
                path = pathlib.Path(directory) / "attempt.json"
                if mode == "existing-record": path.write_text("original retained attempt")
                record_patch = patch.object(publisher, "write_kit_repeat_attempt", side_effect=OSError("presend fsync failed")) if mode == "record" else patch.object(publisher, "write_kit_repeat_attempt", wraps=publisher.write_kit_repeat_attempt)
                with record_patch, self.assertRaises((RuntimeError, OSError)):
                    self.run_repeat(context, path, verify)
            self.assertEqual(provider.dispatched, [])
            self.assertFalse(rows["publicationComplete"])

    def test_counter_exhaustion_before_reads_admission_and_delegation(self):
        for mode in ("first-read", "extra-read", "dispatch-admission", "delegation"):
            context = self.context()
            candidate, _, counter, _, _, _, provider, _, _ = context
            def exhaust(): counter.attempted["get"] = 4200 - counter.attempted["post"] - counter.attempted["patch"]
            if mode == "first-read": exhaust()
            if mode == "extra-read":
                original = provider.provider.observe
                def observe(effect):
                    value = original(effect)
                    if effect.identity == publisher.KIT_EFFECT: exhaust()
                    return value
                provider.provider.observe = observe
            def verify():
                if mode == "dispatch-admission": exhaust()
                return repeat_candidate(candidate)
            record = publisher.write_kit_repeat_attempt
            def write(path, value):
                record(path, value)
                if mode == "delegation": exhaust()
            with tempfile.TemporaryDirectory() as directory, patch.object(publisher, "write_kit_repeat_attempt", side_effect=write), self.assertRaises(RuntimeError):
                self.run_repeat(context, pathlib.Path(directory) / "attempt.json", verify)
            self.assertEqual(provider.dispatched, [])

    def test_duplicate_unknown_index_lag_conflict_and_later_matched_settlement(self):
        for mode in ("409", "applied-response-lost", "unknown-return"):
            context = self.context()
            candidate, _, _, rows, journal, admission, provider, _, _ = context
            def dispatch(effect):
                provider.dispatched.append(effect.identity)
                if mode == "applied-response-lost": provider.matched.add(effect.identity)
                if mode == "unknown-return": return Dispatch("unknown")
                raise RuntimeError("409 duplicate is not payload equality" if mode == "409" else "applied response lost")
            provider.provider.dispatch = dispatch
            with tempfile.TemporaryDirectory() as directory, self.assertRaises(RuntimeError):
                self.run_repeat(context, pathlib.Path(directory) / "attempt.json")
            self.assertEqual(provider.dispatched, [publisher.KIT_EFFECT])
            self.assertEqual(journal._observed.state["generation"], 14)
            self.assertEqual(rows["lastDispatch"]["state"], "unknown")
            self.assertFalse(rows["publicationComplete"])
            if mode != "applied-response-lost":
                self.assertEqual(publisher.advance(candidate, journal, admission, provider), "waiting")
                original = provider.provider.observe
                provider.provider.observe = lambda effect: Observation("mismatched") if effect.identity == publisher.KIT_EFFECT else original(effect)
                with self.assertRaises(RuntimeError): publisher.advance(candidate, journal, admission, provider)
                provider.provider.observe = original
                provider.matched.add(publisher.KIT_EFFECT)
            self.assertEqual(publisher.advance(candidate, journal, admission, provider), "verified")
            self.assertEqual(journal._observed.state["generation"], 15)
            self.assertEqual(provider.dispatched, [publisher.KIT_EFFECT])

    def test_port_consumes_single_send_on_success_or_exception(self):
        for fail in (False, True):
            context = self.context()
            candidate, _, _, rows, _, _, provider, _, _ = context
            rows["kitRepeat"] = {"observations": []}
            port = publisher.KitObservationProvider(provider, rows, ordered_effects(candidate)[6])
            port.repeat_enabled = True
            provider.provider.fail_dispatch = fail
            if fail:
                with self.assertRaises(RuntimeError): port.dispatch(ordered_effects(candidate)[6])
            else: port.dispatch(ordered_effects(candidate)[6])
            with self.assertRaises(RuntimeError): port.dispatch(ordered_effects(candidate)[6])
            self.assertEqual(provider.dispatched, [publisher.KIT_EFFECT])

    def test_real_local_archive_payload_manifest_and_endpoint_checks(self):
        saga_spec = importlib.util.spec_from_file_location("repeat_saga_fixture", ROOT / "scripts/release-saga.py")
        saga = importlib.util.module_from_spec(saga_spec)
        saga_spec.loader.exec_module(saga)
        for mode in ("valid", "archive-drift", "payload-drift", "manifest-drift", "wrong-version", "wrong-endpoint"):
            with tempfile.TemporaryDirectory() as directory:
                root = pathlib.Path(directory)
                path = root / "FS.GG.Kit.0.101.0.nupkg"
                version = "0.102.0" if mode == "wrong-version" else "0.101.0"
                with zipfile.ZipFile(path, "w") as archive:
                    archive.writestr("FS.GG.Kit.nuspec", f"<package><metadata><id>FS.GG.Kit</id><version>{version}</version></metadata></package>")
                    archive.writestr("lib/kit.dll", b"synthetic fixture payload")
                candidate = manifest()
                row = next(row for row in candidate["descriptor"]["packages"] if row["id"] == "FS.GG.Kit")
                row["artifact"].update(path=path.name, sha256=saga.sha256(path), payloadSha256=saga.payload_id(path))
                if mode == "payload-drift": row["artifact"]["payloadSha256"] = "sha256:" + "a" * 64
                candidate["contentId"] = "sha256:" + hashlib.sha256(json.dumps(candidate["descriptor"], sort_keys=True, separators=(",", ":")).encode()).hexdigest()
                manifest_path = root / "release-manifest.json"
                raw = json.dumps(candidate).encode();manifest_path.write_bytes(raw)
                provider = publisher.LiveProvider(MemoryPublisher(), manifest_path, "fixture-token", "fixture-key")
                if mode == "archive-drift": path.write_bytes(path.read_bytes() + b"drift")
                if mode == "manifest-drift": manifest_path.write_bytes(raw + b" ")
                if mode == "wrong-endpoint": provider._feed_url = lambda *args: "https://other-feed.invalid/"
                if mode == "valid":
                    value = publisher.verify_kit_repeat_candidate(candidate, provider, raw)
                    self.assertEqual(value["feed"], "nuget.org")
                    self.assertNotIn("--skip-duplicate", value["argv"])
                    requested = []
                    def inert_push(argv, **kwargs):
                        requested.append(argv)
                        return types.SimpleNamespace(returncode=0)
                    # Inspect actual LiveProvider argv with an inert port; the
                    # audit/process/network guards remain installed underneath.
                    with patch.object(publisher.subprocess, "run", side_effect=inert_push):
                        self.assertEqual(provider.dispatch(ordered_effects(candidate)[6]).state, "applied")
                    self.assertEqual(requested, [["dotnet", "nuget", "push", str(path),
                                     "--source", publisher.KIT_ENDPOINT, "--api-key", "fixture-key"]])
                else:
                    with self.assertRaises(RuntimeError): publisher.verify_kit_repeat_candidate(candidate, provider, raw)

    def test_profile_flags_and_original_tuple_are_closed(self):
        env = {"GITHUB_SHA": "b" * 40, "GITHUB_ACTOR": "EHotwagner", "GITHUB_RUN_ID": "1",
               "GITHUB_EVENT_NAME": "workflow_dispatch", "GITHUB_REPOSITORY": "FS-GG/.github",
               "GITHUB_REF": "refs/heads/main", "GITHUB_RUN_ATTEMPT": "1"}
        with tempfile.TemporaryDirectory() as directory:
            for key in ("candidate_run_id", "candidate_artifact_id", "candidate_archive_sha256", "preflight_only"):
                args = types.SimpleNamespace(candidate_run_id=38026714312, candidate_artifact_id=11660242226,
                        candidate_archive_sha256=publisher.RECOVERY_CANDIDATE["archiveSha256"],
                        recovery_checkpoint=False, repeat_original_nuget_kit_intent=True, publish=True,
                        preflight_only=False, workdir=pathlib.Path(directory) / key)
                setattr(args, key, True if key == "preflight_only" else 9 if key.endswith("id") else "a" * 64)
                with patch.dict(publisher.os.environ, env, clear=True), self.assertRaises(RuntimeError):
                    publisher.execute_publisher(args, report())
                self.assertFalse(args.workdir.exists())
        argv = ["publisher", "--publish", "--recovery-checkpoint", "--repeat-original-nuget-kit-intent",
                "--candidate-run-id", "38026714312", "--candidate-artifact-id", "11660242226",
                "--candidate-archive-sha256", "a" * 64, "--workdir", "/offline/unused"]
        with patch.object(sys, "argv", argv), self.assertRaises(SystemExit): publisher.main()

    def test_repeat_primary_cause_survives_result_reporting_failure(self):
        argv = ["publisher", "--publish", "--repeat-original-nuget-kit-intent",
                "--candidate-run-id", "38026714312", "--candidate-artifact-id", "11660242226",
                "--candidate-archive-sha256", "a" * 64, "--workdir", "/offline/unused"]
        primary = RuntimeError("repeat outcome unknown")
        primary.__cause__ = OSError("original response lost")
        captured = []
        def failed_report(path, value):
            captured.append(copy.deepcopy(value))
            raise OSError("repeat result unavailable")
        import io
        with patch.object(sys, "argv", argv), patch.object(publisher, "execute_publisher", side_effect=primary), \
             patch.object(publisher, "write_result", side_effect=failed_report), patch.object(sys, "stderr", new_callable=io.StringIO) as output:
            self.assertEqual(publisher.main(), 1)
            self.assertIn("repeat outcome unknown", output.getvalue())
            self.assertIn("repeat result unavailable", output.getvalue())
        self.assertEqual(captured[0]["profile"], publisher.KIT_REPEAT_PROFILE)
        self.assertEqual([row["message"] for row in captured[0]["failure"]["chain"]],
                         ["repeat outcome unknown", "original response lost"])
        self.assertFalse(captured[0]["publicationComplete"])


class RuntimeAPI(MemoryPublisher):
    """Actual scoped provider/proof methods consume bounded inert API responses."""
    def __init__(self, candidate, root, matched=False):
        super().__init__()
        self.candidate, self.root = candidate, root
        self.uploads, self.downloads = [], []
        self.mode, self.release_pages, self.asset_pages = None, {}, {}
        self.release = {"id": 408709919, "tag_name": "coherent-set/v0.101.0", "target_commitish": "b" * 40,
                        "body": "release-successor:" + candidate["contentId"], "draft": True, "prerelease": False}
        self.assets = []
        for i, name in enumerate([*(f"{name}.0.101.0.nupkg" for name in PACKAGES),
                                  "standalone-telemetry-evidence.json", publisher.RUNTIME_NAME], 1):
            if name == publisher.RUNTIME_NAME and not matched: continue
            raw = (root / name).read_bytes()
            self.assets.append({"id": i, "name": name, "size": len(raw), "state": "uploaded",
                                "digest": "sha256:" + hashlib.sha256(raw).hexdigest()})
        body = {"schema": "fsgg.release-successor-result/1", "profile": publisher.RECOVERY_PROFILE,
                "execution": {"operator": "EHotwagner", "runAttempt": "1", "runId": 38063768172,
                              "sourceSha": publisher.RUNTIME_REFUSAL_SOURCE},
                "candidate": {**publisher.RECOVERY_CANDIDATE, "version": "0.101.0"},
                "originalPublisher": {"runId": 38029395937, "runAttempt": 1, "jobId": 114146971214},
                "disposition": "failed-or-unknown", "publicationComplete": False, "stage": "advance",
                "iterationsAttempted": 3, "journalRef": REF, "journalMutationMayHaveOccurred": True,
                "lastDispatch": {"attempted": True, "effect": "qualification-asset:evidence", "state": "applied"},
                "failure": {"chain": [{"message": "qualification-asset:runtime: dispatch admission denied",
                                         "messageTruncated": False, "type": "Refused"}], "chainIncomplete": False},
                "lastObservedJournal": {"generation": 26, "logicalHead": publisher.ORIGINAL_RUNTIME_HEAD}}
        self.proof_body = json.dumps(body, sort_keys=True, separators=(",", ":")).encode()
        import io
        archive = io.BytesIO()
        with zipfile.ZipFile(archive, "w", compression=zipfile.ZIP_DEFLATED) as z:
            z.writestr("release-successor-result.json", self.proof_body)
        self.proof_zip = archive.getvalue()
        assert len(self.proof_zip) <= 1413
        self.proof_zip += b"\0" * (1413 - len(self.proof_zip))
        self.run = {"id": 38063768172, "workflow_id": 362181999, "run_attempt": 1,
                    "head_sha": publisher.RUNTIME_REFUSAL_SOURCE, "path": ".github/workflows/release-successor-publish.yml",
                    "head_branch": "main", "event": "workflow_dispatch", "actor": {"login": "EHotwagner"},
                    "repository": {"id": 1269292704, "full_name": "FS-GG/.github"},
                    "status": "completed", "conclusion": "failure"}
        self.artifact = {"id": 11675095562, "name": "release-successor-result", "size_in_bytes": 1413,
                         "expired": False, "expires_at": "2030-01-01T00:00:00Z", "digest": "sha256:" + hashlib.sha256(self.proof_zip).hexdigest(),
                         "workflow_run": {"id": 38063768172, "head_sha": publisher.RUNTIME_REFUSAL_SOURCE}}

    def get(self, path):
        if path.endswith('/actions/runs/38026714312'): self.calls.append(path);return {'head_sha':self.candidate['descriptor']['sourceSha']}
        if path.endswith('/actions/artifacts/11660242226'): self.calls.append(path);return {'digest':'sha256:'+publisher.RECOVERY_CANDIDATE['archiveSha256']}
        if path.endswith('/actions/runs/38063768172'): self.calls.append(path); return copy.deepcopy(self.run)
        if path.endswith('/actions/artifacts/11675095562'): self.calls.append(path); return copy.deepcopy(self.artifact)
        if '/releases?per_page=100&page=' in path:
            self.calls.append(path);page = int(path.rsplit('=', 1)[1]);return copy.deepcopy(self.release_pages.get(page, [self.release] if page == 1 else []))
        if '/releases/408709919/assets?' in path:
            self.calls.append(path);page = int(path.rsplit('=', 1)[1]) if '&page=' in path else 1
            return copy.deepcopy(self.asset_pages.get(page, self.assets if page == 1 else []))
        if '/git/ref/tags/coherent-set/v0.101.0' in path: self.calls.append(path);return {'object': {'sha': 'b' * 40}}
        return super().get(path)

    def runtime_recovery_json(self,path):
        return self.get(path)

    def download_runtime_refusal_artifact(self):
        self.downloads.append('refusal');return self.proof_zip
    def download_asset(self, ident):
        self.downloads.append(ident);name = next(row['name'] for row in self.assets if row['id'] == ident)
        return (self.root / name).read_bytes()
    def download_runtime_recovery_asset(self, ident, size):
        raw = self.download_asset(ident)
        return raw + b'drift' if self.mode == 'wrong-bytes' else raw
    def upload_runtime_recovery_asset(self, release_id, raw):
        self.uploads.append((release_id, raw))
        if self.mode == 'throw': raise OSError('upload response lost')
        if self.mode == 'unknown': return {}
        return {'id': 99, 'name': publisher.RUNTIME_NAME, 'size': len(raw), 'state': 'uploaded',
                'digest': 'sha256:' + hashlib.sha256(raw).hexdigest()}


class OriginalRuntimeRecoveryControls(unittest.TestCase):
    def context(self, root, matched=False):
        candidate, authority, intent, setup = fixture(26)
        payload = b'owned original synthetic runtime payload\n'
        (root / publisher.RUNTIME_NAME).write_bytes(payload)
        (root / 'standalone-telemetry-evidence.json').write_bytes(b'owned evidence\n')
        for row in candidate['descriptor']['packages']:
            name = row['id'] + '.0.101.0.nupkg';raw = name.encode();(root / name).write_bytes(raw)
            row['artifact']['sha256'] = hashlib.sha256(raw).hexdigest()
        candidate['descriptor']['standaloneTelemetry'] = {'qualificationPath': publisher.RUNTIME_NAME,
                         'qualificationSha256': hashlib.sha256(payload).hexdigest()}
        manifest_raw = (json.dumps(candidate, sort_keys=True, separators=(',', ':'))+'\n').encode()
        (root / 'release-manifest.json').write_bytes(manifest_raw)
        archive = root / 'original-candidate.zip';archive.write_bytes(b'original synthetic candidate archive')
        fixed = {**publisher.RECOVERY_CANDIDATE, 'contentId':candidate['contentId'], 'sourceSha':'b'*40,
                 'archiveSha256':hashlib.sha256(archive.read_bytes()).hexdigest()}
        counter, rows, journal, _, _, _ = slice_context(candidate, authority, intent)
        source = RuntimeAPI(candidate, root, matched)
        api = publisher.RuntimePublisherAPI(source)
        provider = LiveProvider(api, root/'release-manifest.json', 'fixture-token', 'fixture-key')
        admission = publisher.ProtectedPublisherAdmission(SingleOperatorAdmission(api,candidate,'b'*40,1,'EHotwagner','refs/heads/main'),counter,REF)
        return candidate,authority,counter,rows,journal,admission,provider,source,api,archive,manifest_raw,fixed,setup

    def run_runtime(self, c, path, mutate=None, clock=None):
        candidate,_,counter,rows,journal,admission,provider,source,api,archive,manifest_raw,fixed,_ = c
        # Pin only immutable synthetic fixture identities, never replace guard functions.
        with patch.object(publisher,'RECOVERY_CANDIDATE',fixed), \
             patch.object(publisher,'ORIGINAL_RUNTIME_HEAD',journal._observed.head), \
             patch.object(publisher,'ORIGINAL_RUNTIME_STATE_SHA256',hashlib.sha256(canonical(journal._observed.state)).hexdigest()):
            # Recreate proof under the same explicit synthetic immutable identities.
            rebuilt = RuntimeAPI(candidate,provider.root,any(x['name']==publisher.RUNTIME_NAME for x in source.assets))
            source.proof_zip,source.proof_body,source.artifact = rebuilt.proof_zip,rebuilt.proof_body,rebuilt.artifact
            with patch.object(publisher,'RUNTIME_REFUSAL_ZIP_SHA256',hashlib.sha256(source.proof_zip).hexdigest()), \
                 patch.object(publisher,'RUNTIME_REFUSAL_MEMBER_SHA256',hashlib.sha256(source.proof_body).hexdigest()), \
                 patch.object(LiveProvider,'_download_package',lambda self,feed,package:self.root/(package+'.0.101.0.nupkg')):
                proof=publisher.authenticate_runtime_refusal(api)
                verify=lambda:publisher.verify_runtime_candidate(candidate,provider,manifest_raw,archive)
                if mutate:mutate(c)
                return publisher.run_original_runtime_recovery(candidate,journal,admission,provider,counter,rows,10,proof,verify,path,clock=clock or (lambda:0))

    def test_absent_one_upload_checkpoint26_full_composed_counts(self):
        with tempfile.TemporaryDirectory() as temp:
            root=pathlib.Path(temp)/'candidate';root.mkdir();c=self.context(root);path=root/'attempt.json'
            self.assertEqual(self.run_runtime(c,path),'checkpoint')
            self.assertEqual(len(c[7].uploads),1);self.assertEqual(c[7].uploads[0],(408709919,(root/publisher.RUNTIME_NAME).read_bytes()))
            self.assertEqual(c[4]._observed.state['generation'],26);self.assertFalse(c[3]['publicationComplete'])
            self.assertTrue(all(method=='get' for method,_ in c[1].calls));self.assertLessEqual(sum(c[2].attempted.values()),4080)
            self.assertEqual(c[3]['lastDispatch']['effect'],publisher.RUNTIME_EFFECT)
            self.assertEqual(json.loads(path.read_bytes())['journal']['generation'],26)
            print(json.dumps({'runtimeRecoveryAbsent':{'setupCalls':c[12],'authority':c[2].report(),'publisher':c[8].report()}},sort_keys=True))

    def test_matched_zero_send_normal_cas27_and_sibling_preservation(self):
        with tempfile.TemporaryDirectory() as temp:
            root=pathlib.Path(temp)/'candidate';root.mkdir();c=self.context(root,True)
            original=c[6].observe_runtime_recovery
            def observe(effect):
                value=original(effect);CASFreshnessControls.sibling(c[1],'runtime-sibling.txt');return value
            c[6].observe_runtime_recovery=observe
            self.assertEqual(self.run_runtime(c,root/'attempt.json'),'checkpoint')
            self.assertEqual(c[7].uploads,[]);self.assertEqual(c[4]._observed.state['generation'],27)
            self.assertEqual(c[2].attempted['patch'],1);self.assertLessEqual(sum(c[2].attempted.values()),4080)
            self.assertTrue(any(r['path']=='runtime-sibling.txt' for r in c[1].objects[('trees',c[1].objects[('commits',c[1].head)]['tree']['sha'])]['tree']))
            print(json.dumps({'runtimeRecoveryMatched':{'setupCalls':c[12],'authority':c[2].report(),'publisher':c[8].report()}},sort_keys=True))

    def test_ordinary_runtime_absent_still_waits_and_kit_rejects26(self):
        candidate,authority,intent,_=fixture(26);counter,rows,journal,admission,provider,_=slice_context(candidate,authority,intent)
        provider.matched.remove(publisher.RUNTIME_EFFECT)
        self.assertEqual(publisher.advance(candidate,journal,admission,provider),'waiting');self.assertEqual(provider.dispatched,[])
        with self.assertRaises(RuntimeError):publisher.validate_original_kit_state(candidate,journal)

    def test_original_run_artifact_archive_member_and_semantic_proof_refuse(self):
        modes=('run','attempt','source','actor','artifact','expiry','archive','member','stage','missing-authentication')
        import io
        for mode in modes:
            with self.subTest(mode=mode),tempfile.TemporaryDirectory() as temp:
                root=pathlib.Path(temp);c=self.context(root);api=c[7]
                if mode=='run':api.run['id']=2
                if mode=='attempt':api.run['run_attempt']=2
                if mode=='source':api.run['head_sha']='c'*40
                if mode=='actor':api.run['actor']['login']='other'
                if mode=='artifact':api.artifact['id']=2
                if mode=='expiry':api.artifact['expires_at']='2000-01-01T00:00:00Z'
                body_sha=hashlib.sha256(api.proof_body).hexdigest()
                if mode=='stage':
                    body=json.loads(api.proof_body);body['stage']='dispatch';api.proof_body=json.dumps(body).encode()
                    archive=io.BytesIO()
                    with zipfile.ZipFile(archive,'w',compression=zipfile.ZIP_DEFLATED) as z:z.writestr('release-successor-result.json',api.proof_body)
                    api.proof_zip=archive.getvalue();api.proof_zip+=b'\0'*(1413-len(api.proof_zip));body_sha=hashlib.sha256(api.proof_body).hexdigest()
                zip_sha=hashlib.sha256(api.proof_zip).hexdigest();api.artifact['digest']='sha256:'+zip_sha
                if mode=='archive':api.proof_zip=b'changed'
                if mode=='member':body_sha='0'*64
                def unauthenticated(*args):raise ProviderRefused('credential unavailable')
                target=patch.object(api,'get',side_effect=unauthenticated) if mode=='missing-authentication' else patch.object(api,'get',wraps=api.get)
                with patch.object(publisher,'RUNTIME_REFUSAL_ZIP_SHA256',zip_sha),patch.object(publisher,'RUNTIME_REFUSAL_MEMBER_SHA256',body_sha),target,self.assertRaises(RuntimeError):
                    publisher.authenticate_runtime_refusal(api)
                self.assertEqual(api.uploads,[])

    def test_closed_generation_head_canonical_prefix_and_content_refuse(self):
        for mode in ('generation','head','canonical','prefix','content'):
            with self.subTest(mode=mode),tempfile.TemporaryDirectory() as temp:
                root=pathlib.Path(temp);c=self.context(root);journal=c[4];good_head=journal._observed.head;good_sha=hashlib.sha256(canonical(journal._observed.state)).hexdigest()
                if mode=='generation':journal._observed.state['generation']=24
                if mode=='prefix':journal._observed.state['effects']['channel-asset']='intent'
                if mode=='content':journal._observed.state['contentId']='sha256:'+'0'*64
                with patch.object(publisher,'ORIGINAL_RUNTIME_HEAD','0'*40 if mode=='head' else good_head),patch.object(publisher,'ORIGINAL_RUNTIME_STATE_SHA256','0'*64 if mode=='canonical' else good_sha),self.assertRaises(RuntimeError):
                    publisher.validate_original_runtime_state(c[0],journal)
                self.assertEqual(c[7].uploads,[])

    def test_complete_populations_duplicates_latepages_replacement_and_bytes_refuse(self):
        for mode in ('release-overflow','asset-overflow','duplicate-release-later','duplicate-asset-later','promoted','replacement','wrong-target-source','wrong-bytes','bad-shape','prior-asset-moved','later-asset'):
            with self.subTest(mode=mode),tempfile.TemporaryDirectory() as temp:
                root=pathlib.Path(temp);c=self.context(root,mode=='wrong-bytes');api=c[7]
                if mode=='release-overflow':api.release_pages={i:[{'id':i*100+j,'tag_name':'other'} for j in range(100)] for i in range(1,11)}
                if mode=='asset-overflow':api.asset_pages={i:[{'id':i*100+j,'name':str(i*100+j),'size':0} for j in range(100)] for i in range(1,5)}
                if mode=='duplicate-release-later':api.release_pages={1:[api.release]+[{'id':i+1,'tag_name':'other'} for i in range(99)],2:[{**api.release,'id':123}]}
                if mode=='duplicate-asset-later':api.asset_pages={1:[{'id':i+100,'name':str(i),'size':0} for i in range(100)],2:[{'id':100,'name':'late','size':0}]}
                if mode=='promoted':api.release['draft']=False
                if mode=='replacement':api.release['id']=123
                if mode=='wrong-target-source':api.release['target_commitish']='c'*40
                if mode=='wrong-bytes':api.mode=mode
                if mode=='bad-shape':api.asset_pages={1:[{'id':True,'name':'x','size':0}]}
                if mode=='prior-asset-moved':api.assets[0]['digest']='sha256:'+'0'*64
                if mode=='later-asset':api.assets.append({'id':999,'name':'stable-channel.json','size':0})
                with self.assertRaises(RuntimeError):c[6].observe_runtime_recovery(ordered_effects(c[0])[12])
                self.assertEqual(api.uploads,[])

    def test_payload_currentmain_protection_selected_and_physical_movement_refuse(self):
        for mode in ('payload','main','protection','physical','selected','population'):
            with self.subTest(mode=mode),tempfile.TemporaryDirectory() as temp:
                root=pathlib.Path(temp);c=self.context(root)
                def mutate(c):
                    if mode=='payload':(root/publisher.RUNTIME_NAME).write_bytes(b'changed')
                    if mode=='main':c[7].head='c'*40
                    if mode=='protection':c[1].controls['mainWriter']['enforcement']='disabled'
                    if mode in ('physical','selected'):
                        original=c[6].observe_runtime_recovery
                        def observe(effect):
                            result=original(effect);CASFreshnessControls.sibling(c[1],'move.txt')
                            if mode=='selected':c[4]._observed.state['effects']['channel-asset']='intent'
                            return result
                        c[6].observe_runtime_recovery=observe
                    if mode=='population':
                        original=c[6].observe_runtime_recovery;seen=[]
                        def observe(effect):
                            seen.append(True)
                            if len(seen)>1:c[7].assets.append({'id':999,'name':'late','size':0})
                            return original(effect)
                        c[6].observe_runtime_recovery=observe
                with self.assertRaises(RuntimeError):self.run_runtime(c,root/'attempt.json',mutate)
                self.assertEqual(c[7].uploads,[])

    def test_unauthorized_current_run_and_unmatched_verified_prefix_refuse(self):
        for mode in ('actor','attempt','status','head','prefix-unreadable','prefix-absent'):
            with self.subTest(mode=mode),tempfile.TemporaryDirectory() as temp:
                root=pathlib.Path(temp);c=self.context(root);get=c[7].get
                def changed(path):
                    value=get(path)
                    if path.endswith('/actions/runs/1'):
                        if mode=='actor':value['actor']['login']='other'
                        if mode=='attempt':value['run_attempt']=2
                        if mode=='status':value['status']='completed'
                        if mode=='head':value['head_sha']='c'*40
                    return value
                c[7].get=changed
                if mode=='prefix-unreadable':c[6].observe=lambda effect:(_ for _ in ()).throw(ProviderRefused('prefix unavailable'))
                if mode=='prefix-absent':c[6].observe=lambda effect:Observation('absent')
                with self.assertRaises(RuntimeError):self.run_runtime(c,root/'attempt.json')
                self.assertEqual(c[7].uploads,[])

    def test_record_failure_postrecord_payload_counter_quota_deadline_block(self):
        for mode in ('record','existing-record','postrecord-payload','counter','quota','deadline'):
            with self.subTest(mode=mode),tempfile.TemporaryDirectory() as temp:
                root=pathlib.Path(temp);c=self.context(root);path=root/'attempt.json';original=publisher.write_kit_repeat_attempt
                if mode=='existing-record':path.write_text('retained original attempt')
                def record(path,value):
                    if mode=='record':raise OSError('fsync failed')
                    original(path,value)
                    if mode=='postrecord-payload':(root/publisher.RUNTIME_NAME).write_bytes(b'changed')
                    if mode=='counter':c[2].attempted['get']=4200-c[2].attempted['post']-c[2].attempted['patch']
                    if mode=='quota':c[2].quota['reset']=1
                with patch.object(publisher,'write_kit_repeat_attempt',side_effect=record),self.assertRaises((RuntimeError,OSError)):
                    self.run_runtime(c,path,clock=(lambda:11) if mode=='deadline' else None)
                self.assertEqual(c[7].uploads,[])

    def test_unknown_and_throw_consume_send_without_second_upload(self):
        for mode in ('unknown','throw'):
            with self.subTest(mode=mode),tempfile.TemporaryDirectory() as temp:
                root=pathlib.Path(temp);c=self.context(root);c[7].mode=mode
                with self.assertRaises(RuntimeError):self.run_runtime(c,root/'attempt.json')
                self.assertEqual(len(c[7].uploads),1);self.assertEqual(c[3]['lastDispatch']['state'],'unknown')
                self.assertEqual(c[4]._observed.state['generation'],26)

    def test_scoped_provider_refuses_unobserved_and_second_upload(self):
        with tempfile.TemporaryDirectory() as temp:
            root=pathlib.Path(temp);c=self.context(root);effect=ordered_effects(c[0])[12];provider=c[6]
            with self.assertRaises(RuntimeError):provider.dispatch_runtime_recovery(effect,{'releaseId':408709919})
            observation,witness=provider.observe_runtime_recovery(effect);self.assertEqual(observation.state,'absent')
            self.assertEqual(provider.dispatch_runtime_recovery(effect,witness).state,'applied')
            with self.assertRaises(RuntimeError):provider.dispatch_runtime_recovery(effect,witness)
            self.assertEqual(len(c[7].uploads),1)

    def test_closed_scoped_json_endpoint_and_publisher_api_ceiling(self):
        with self.assertRaises(RuntimeError):GitHubAPI('fixture').runtime_recovery_json('repos/other/releases')
        donor=types.SimpleNamespace(get=lambda path:{})
        counted=publisher.RuntimePublisherAPI(donor)
        for _ in range(256):counted.get('fixed synthetic path')
        with self.assertRaises(RuntimeError):counted.get('257th path')
        self.assertEqual(sum(counted.attempted.values()),256)
        with self.assertRaises(RuntimeError):counted.post('unselected',{})

    def test_actual_bounded_runtime_download_refuses_oversize_and_deadline(self):
        response=types.SimpleNamespace(status=200,headers={},fp=types.SimpleNamespace(raw=types.SimpleNamespace(_sock=types.SimpleNamespace(settimeout=lambda left:None))))
        response.read1=lambda cap:b'x'*cap
        class Context:
            def __enter__(self):return response
            def __exit__(self,*args):pass
        opener=types.SimpleNamespace(open=lambda *a,**k:Context())
        client=GitHubAPI('fixture')
        with patch('urllib.request.build_opener',return_value=opener),self.assertRaises(RuntimeError):
            client._runtime_read('https://api.github.com/fixed',100)
        response.read1=lambda cap:b'x'
        with patch('urllib.request.build_opener',return_value=opener),patch('release_successor_provider.time.monotonic',side_effect=[0,13]),self.assertRaises(RuntimeError):
            client._runtime_read('https://api.github.com/fixed',100)

    def test_wrapper_consumes_permission_on_callback_exception_and_repeated_call(self):
        for outcome in ('applied','throw','unknown'):
            candidate=manifest();effect=ordered_effects(candidate)[12];rows=report();rows['runtimeRecovery']={}
            target=types.SimpleNamespace(dispatch_runtime_recovery=lambda *a:Dispatch('applied'))
            if outcome=='throw':target.dispatch_runtime_recovery=lambda *a:(_ for _ in ()).throw(OSError('lost'))
            if outcome=='unknown':target.dispatch_runtime_recovery=lambda *a:Dispatch('unknown')
            guarded=publisher.RuntimeRecoveryProvider(target,rows,effect);guarded.send_enabled=True
            try:guarded.dispatch(effect)
            except OSError:pass
            self.assertTrue(guarded.send_consumed)
            with self.assertRaises(RuntimeError):guarded.dispatch(effect)

    def test_matched_uncertain_cas_and_readback_stop_without_send(self):
        for mode in ('patch','readback'):
            with self.subTest(mode=mode),tempfile.TemporaryDirectory() as temp:
                root=pathlib.Path(temp);c=self.context(root,True)
                if mode=='patch':c[1].reject_patch=True
                else:
                    original=c[1].patch
                    def patch_then_unknown(path,body):
                        result=original(path,body);c[1].fail_at=len(c[1].calls)+1;return result
                    c[1].patch=patch_then_unknown
                with self.assertRaises(RuntimeError):self.run_runtime(c,root/'attempt.json')
                self.assertEqual(c[7].uploads,[]);self.assertTrue(c[3]['journalMutationMayHaveOccurred'])

    def test_actual_cli_workflow_profile_and_conflicting_flags(self):
        workflow=(ROOT/'.github/workflows/release-successor-publish.yml').read_text()
        self.assertIn('RECOVER_ORIGINAL_RUNTIME_INTENT: ${{ inputs.recover_original_runtime_intent }}',workflow)
        self.assertIn('profile+=(--recover-original-runtime-intent)',workflow)
        self.assertIn('release-successor-result-runtime-recovery-attempt.json',workflow)
        base=['publisher','--publish','--recover-original-runtime-intent','--candidate-run-id','38026714312',
              '--candidate-artifact-id','11660242226','--candidate-archive-sha256',publisher.RECOVERY_CANDIDATE['archiveSha256']]
        for flag in ('--recovery-checkpoint','--repeat-original-nuget-kit-intent'):
            with patch.object(sys,'argv',base+[flag,'--workdir','/offline/unused']),self.assertRaises(SystemExit):publisher.main()
        env={'GITHUB_SHA':'b'*40,'GITHUB_ACTOR':'EHotwagner','GITHUB_RUN_ID':'1','GITHUB_EVENT_NAME':'workflow_dispatch',
             'GITHUB_REPOSITORY':'FS-GG/.github','GITHUB_REF':'refs/heads/main','GITHUB_RUN_ATTEMPT':'1'}
        with tempfile.TemporaryDirectory() as temp:
            work=pathlib.Path(temp)/'candidate';result=pathlib.Path(temp)/'result.json'
            with patch.object(sys,'argv',[x if x!='--publish' else '--preflight-only' for x in base]+['--workdir',str(work),'--result-path',str(result)]),patch.dict(publisher.os.environ,env,clear=True):
                self.assertEqual(publisher.main(),1)
            self.assertFalse(work.exists());self.assertFalse(json.loads(result.read_bytes())['publicationComplete'])

    def test_actual_cli_execute_setup_refusal_provider_counter_and_checkpoint(self):
        import shutil
        for matched in (False,True):
            with self.subTest(matched=matched),tempfile.TemporaryDirectory() as temp:
                root=pathlib.Path(temp)/'fixture';root.mkdir();c=self.context(root,matched)
                candidate,authority,_,_,journal,_,_,source,_,archive,_,fixed,_=c
                work=pathlib.Path(temp)/'owned';result=pathlib.Path(temp)/'result.json'
                env={'GITHUB_SHA':'b'*40,'GITHUB_ACTOR':'EHotwagner','GITHUB_RUN_ID':'1','GITHUB_EVENT_NAME':'workflow_dispatch',
                     'GITHUB_REPOSITORY':'FS-GG/.github','GITHUB_REF':'refs/heads/main','GITHUB_RUN_ATTEMPT':'1',
                     'GH_TOKEN':'fixture-token','ORDINARY_LEDGER_TOKEN':'fixture-ledger','NUGET_API_KEY':'fixture-key'}
                argv=['publisher','--publish','--recover-original-runtime-intent','--candidate-run-id','38026714312',
                      '--candidate-artifact-id','11660242226','--candidate-archive-sha256',fixed['archiveSha256'],'--workdir',str(work),'--result-path',str(result)]
                def subprocess_fixture(argv,**kwargs):
                    if argv[:2]==['gh','api']:
                        self.assertEqual(argv[2],'repos/FS-GG/.github/actions/artifacts/11660242226/zip');kwargs['stdout'].write(archive.read_bytes())
                    elif pathlib.Path(argv[1]).name=='release-successor-artifact.py':
                        target=pathlib.Path(argv[argv.index('--output')+1]);target.mkdir()
                        for name in [*(p+'.0.101.0.nupkg' for p in PACKAGES),'release-manifest.json','standalone-telemetry-evidence.json',publisher.RUNTIME_NAME]:shutil.copyfile(root/name,target/name)
                    else:raise AssertionError('unselected subprocess fixture '+repr(argv))
                    return types.SimpleNamespace(returncode=0)
                with patch.object(publisher,'RECOVERY_CANDIDATE',fixed),patch.object(publisher,'ORIGINAL_RUNTIME_HEAD',journal._observed.head),patch.object(publisher,'ORIGINAL_RUNTIME_STATE_SHA256',hashlib.sha256(canonical(journal._observed.state)).hexdigest()):
                    rebuilt=RuntimeAPI(candidate,root,matched);source.proof_body,source.proof_zip,source.artifact=rebuilt.proof_body,rebuilt.proof_zip,rebuilt.artifact
                    with patch.object(publisher,'RUNTIME_REFUSAL_ZIP_SHA256',hashlib.sha256(source.proof_zip).hexdigest()),patch.object(publisher,'RUNTIME_REFUSAL_MEMBER_SHA256',hashlib.sha256(source.proof_body).hexdigest()), \
                         patch.object(publisher,'GitHubAPI',side_effect=[source,authority]),patch.object(publisher.subprocess,'run',side_effect=subprocess_fixture), \
                         patch.object(LiveProvider,'_download_package',lambda self,feed,package:self.root/(package+'.0.101.0.nupkg')),patch.dict(publisher.os.environ,env,clear=True),patch.object(sys,'argv',argv):
                        authority.calls.clear();self.assertEqual(publisher.main(),0)
                receipt=json.loads(result.read_bytes());self.assertEqual(receipt['profile'],publisher.RUNTIME_RECOVERY_PROFILE);self.assertFalse(receipt['publicationComplete'])
                self.assertEqual(receipt['lastObservedJournal']['generation'],27 if matched else 26)
                self.assertEqual(len(source.uploads),0 if matched else 1);self.assertLessEqual(sum(receipt['authorityRequests']['attemptedByMethod'].values()),4080)
                self.assertEqual(sum(receipt['authorityRequests']['attemptedByMethod'].values()),len(authority.calls))
                self.assertLessEqual(sum(receipt['publisherRequests']['attempted'].values()),256)
                self.assertLessEqual(result.stat().st_size,16384)
                print(json.dumps({'runtimeRecoveryActualCLI':{'matched':matched,'authority':receipt['authorityRequests'],'publisher':receipt['publisherRequests']}},sort_keys=True))

    def test_primary_upload_uncertainty_survives_report_failure(self):
        import io
        argv=['publisher','--publish','--recover-original-runtime-intent','--candidate-run-id','38026714312',
              '--candidate-artifact-id','11660242226','--candidate-archive-sha256',publisher.RECOVERY_CANDIDATE['archiveSha256'],'--workdir','/offline/unused']
        with patch.object(sys,'argv',argv),patch.object(publisher,'execute_publisher',side_effect=OSError('runtime upload uncertain')), \
             patch.object(publisher,'write_result',side_effect=OSError('report unavailable')),patch.object(sys,'stderr',new_callable=io.StringIO) as output:
            self.assertEqual(publisher.main(),1);self.assertIn('runtime upload uncertain',output.getvalue());self.assertIn('report unavailable',output.getvalue())


if __name__ == "__main__":
    unittest.main()
