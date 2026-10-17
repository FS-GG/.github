#!/usr/bin/env python3
"""Pure offline checkpoint controls; no git subprocess, network or native provider effect.

Synthetic Git object identifiers identify fixture data, not GitHub evidence.
All setup writes are in memory and counted separately from each recovery slice.
The actual journal, advance policy, publisher admission and protection predicate
are used without importing the existing subprocess-based journal test runner.
"""

import base64
from contextlib import contextmanager, redirect_stdout
import copy
import hashlib
import io
import importlib.util
import json
import os
import pathlib
import re
import socket
import subprocess
import sys
import tempfile
import types
import urllib.error
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
    def __init__(self, ref=REF):
        self.logical_ref = ref
        self.directory_suffix = ref.rsplit("/", 1)[1]
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
        for name in ("state", "releases", self.directory_suffix):
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
        if path == PREFIX + "/git/ref/" + self.logical_ref.removeprefix("refs/"):
            raise NotFound(path)
        if "/git/" in path:
            kind, oid = path.split("/git/", 1)[1].split("/", 1)
            return copy.deepcopy(self.objects[(kind, oid)])
        if path.startswith(PREFIX + "/commits?"):
            query = parse_qs(urlsplit(path).query)
            assert query["path"] == ["state/releases/" + self.directory_suffix]
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
                    "event": "workflow_dispatch", "head_branch": "main", "head_sha": self.head,
                    "actor": {"login": "EHotwagner"}, "run_attempt": 1, "status": "in_progress"}
        if path.endswith("/git/ref/heads/main"):
            return {"object": {"sha": self.head}}
        if "/compare/" in path:
            return {"status": "ahead"}
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


def fixture(generation, candidate=None, archive_sha256="a" * 64, ref=REF):
    candidate = manifest() if candidate is None else candidate
    effects = ordered_effects(candidate)
    authority = MemoryAuthority(ref=ref)
    setup = ProtectedReleaseJournal(authority, ref=ref, main_directory=True)
    intent = {"contentId": candidate["contentId"], "sourceSha": candidate["descriptor"]["sourceSha"], "version": candidate["descriptor"]["version"],
              "candidateArchiveSha256": archive_sha256, "operator": "EHotwagner"}
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

    def test_new_corrupt_oid_and_live_head_are_rechecked(self):
        candidate, authority, intent, _ = fixture(14)
        _, rows, journal, _, _, _ = slice_context(candidate, authority, intent)
        logical = journal._observed.head
        tree = authority.objects[("commits", logical)]["tree"]["sha"]
        entry = authority.objects[("trees", tree)]["tree"][0]
        blob = entry["sha"]
        accepted = json.loads(base64.b64decode(authority.objects[("blobs", blob)]["content"]))
        value = {**accepted, "generation": 15}  # No effect transition: invalid successor.
        new_blob = authority.put("blobs", {"encoding": "base64", "content": base64.b64encode(canonical(value)).decode()})
        self.assertNotEqual(blob, new_blob)
        new_tree = authority.tree([{**entry, "sha": new_blob}])
        corrupt_logical = authority.commit(new_tree, [logical], "invalid unchanged effect transition")
        physical = authority.head
        corrupt_physical = journal.journal._overlay(physical, corrupt_logical, value)
        authority.patch(PREFIX + "/git/refs/heads/main", {"sha": corrupt_physical, "force": False})
        with self.assertRaises(RuntimeError): journal.read()
        self.assertEqual(rows["lastObservedJournal"]["generation"], 14)
        self.assertNotIn("intent", rows["lastObservedJournal"])
        self.assertNotIn("stateSHA256", rows["lastObservedJournal"])
        self.assertEqual(json.loads(base64.b64decode(journal.journal._immutable_object("blobs", blob)["content"])), accepted)

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
        if '/releases?per_page=100&page=' in path or '/releases?per_page=25&page=' in path:
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
        candidate = manifest()
        payload = b'owned original synthetic runtime payload\n'
        (root / publisher.RUNTIME_NAME).write_bytes(payload)
        (root / 'standalone-telemetry-evidence.json').write_bytes(b'owned evidence\n')
        for row in candidate['descriptor']['packages']:
            name = row['id'] + '.0.101.0.nupkg';raw = name.encode();(root / name).write_bytes(raw)
            row['artifact']['sha256'] = hashlib.sha256(raw).hexdigest()
        candidate['descriptor']['standaloneTelemetry'] = {'qualificationPath': publisher.RUNTIME_NAME,
                         'qualificationSha256': hashlib.sha256(payload).hexdigest()}
        candidate['contentId'] = 'sha256:' + hashlib.sha256(
            json.dumps(candidate['descriptor'], sort_keys=True, separators=(',', ':'), ensure_ascii=False).encode()).hexdigest()
        manifest_raw = (json.dumps(candidate, sort_keys=True, separators=(',', ':'))+'\n').encode()
        (root / 'release-manifest.json').write_bytes(manifest_raw)
        archive = root / 'original-candidate.zip';archive.write_bytes(b'original synthetic candidate archive')
        fixed = {**publisher.RECOVERY_CANDIDATE, 'contentId':candidate['contentId'], 'sourceSha':'b'*40,
                 'archiveSha256':hashlib.sha256(archive.read_bytes()).hexdigest()}
        candidate, authority, intent, setup = fixture(26, candidate, fixed['archiveSha256'])
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
                if mode=='release-overflow':api.release_pages={i:[{'id':i*25+j,'tag_name':'other'} for j in range(25)] for i in range(1,41)}
                if mode=='asset-overflow':api.asset_pages={i:[{'id':i*100+j,'name':str(i*100+j),'size':0} for j in range(100)] for i in range(1,5)}
                if mode=='duplicate-release-later':api.release_pages={1:[api.release]+[{'id':i+1,'tag_name':'other'} for i in range(24)],2:[{**api.release,'id':123}]}
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

    def runtime_request_fixture(self, bodies):
        """Keep the real Request/read loop and inject only inert HTTP responses."""
        requests, handlers = [], []
        case = self
        class Response:
            status = 200
            def __init__(self, raw):
                self.raw = raw
                self.headers = {'Content-Length': str(len(raw))}
                def timeout(left):
                    case.assertGreater(left, 0)
                    case.assertLessEqual(left, 12)
                self.fp = types.SimpleNamespace(raw=types.SimpleNamespace(
                    _sock=types.SimpleNamespace(settimeout=timeout)))
            def read1(self, cap):
                result, self.raw = self.raw[:cap], self.raw[cap:]
                return result
            def __enter__(self):return self
            def __exit__(self, *args):pass
        def build(*selected):
            handlers.append(selected[-1])
            def open_request(request, timeout):
                requests.append(request)
                case.assertEqual(request.get_method(), 'GET')
                case.assertEqual(timeout, 12)
                if request.full_url.endswith('/zip') and request.get_header('Accept') is not None:
                    raise urllib.error.HTTPError(request.full_url, 415, 'Unsupported Media Type', {}, None)
                case.assertIn(request.full_url, bodies)
                return Response(bodies[request.full_url])
            return types.SimpleNamespace(open=open_request)
        return build, requests, handlers

    def release_transport_fixture(self, source, releases):
        """Model real JSON response expansion; requests/read loop/decoder stay real."""
        prefix = 'https://api.github.com/repos/FS-GG/.github/'
        bodies = {prefix+'releases?per_page=25&page='+str(page):
                  json.dumps(releases[(page-1)*25:page*25], separators=(',', ':')).encode()
                  for page in range(1,41)}
        bodies[prefix+'releases/408709919/assets?per_page=100&page=1'] = json.dumps(source.assets).encode()
        return bodies

    def test_actual_release_page_transport_realistic_oversized_population_succeeds(self):
        with tempfile.TemporaryDirectory() as temp:
            c=self.context(pathlib.Path(temp));source=c[7]
            rows=[copy.deepcopy(source.release)]+[{'id':200000+i,'tag_name':'retained-'+str(i),
                    'body':'release notes '*850,'assets':[]} for i in range(99)]
            old=json.dumps(rows,separators=(',', ':')).encode()
            self.assertGreater(len(old),1048576)
            self.assertLess(len(old),2*1048576)
            bodies=self.release_transport_fixture(source,rows)
            self.assertTrue(all(len(raw)<=1048576 for raw in bodies.values()))
            build,requests,_=self.runtime_request_fixture(bodies)
            c[6].api=GitHubAPI('inert-fixture-token')
            with patch('urllib.request.build_opener',side_effect=build):
                observed,witness=c[6].observe_runtime_recovery(ordered_effects(c[0])[12])
            self.assertEqual(observed.state,'absent');self.assertEqual(witness['releaseCount'],100)
            self.assertEqual(witness['releasePages'],5);self.assertTrue(witness['complete'])
            self.assertEqual(len(requests),6);self.assertEqual(source.uploads,[])
            self.assertTrue(all(req.get_header('Accept')=='application/vnd.github+json' for req in requests))
            self.assertFalse(any('per_page=100&page=' in req.full_url and '/assets?' not in req.full_url for req in requests))

    def test_actual_release_single_fat_row_still_refuses_before_census_or_upload(self):
        with tempfile.TemporaryDirectory() as temp:
            c=self.context(pathlib.Path(temp));rows=[{**c[7].release,'body':'x'*1048576}]
            bodies=self.release_transport_fixture(c[7],rows);build,requests,_=self.runtime_request_fixture(bodies)
            c[6].api=GitHubAPI('inert-fixture-token')
            with patch('urllib.request.build_opener',side_effect=build),self.assertRaisesRegex(RuntimeError,'content length bound'):
                c[6].observe_runtime_recovery(ordered_effects(c[0])[12])
            self.assertEqual(len(requests),1);self.assertEqual(c[7].uploads,[])
            self.assertIsNone(c[6]._runtime_witness)

    def test_actual_release_999_terminator_and_1000_full_pages_boundary(self):
        for count in (999,1000):
            with self.subTest(count=count),tempfile.TemporaryDirectory() as temp:
                c=self.context(pathlib.Path(temp));rows=[c[7].release]+[{'id':300000+i,'tag_name':'retained-'+str(i)} for i in range(count-1)]
                bodies=self.release_transport_fixture(c[7],rows);build,requests,_=self.runtime_request_fixture(bodies)
                c[6].api=GitHubAPI('inert-fixture-token')
                with patch('urllib.request.build_opener',side_effect=build):
                    if count==999:
                        observed,witness=c[6].observe_runtime_recovery(ordered_effects(c[0])[12])
                        self.assertEqual(observed.state,'absent');self.assertEqual(witness['releaseCount'],999)
                        self.assertEqual(witness['releasePages'],40);self.assertEqual(len(requests),41)
                    else:
                        with self.assertRaisesRegex(RuntimeError,'census incomplete'):
                            c[6].observe_runtime_recovery(ordered_effects(c[0])[12])
                        self.assertEqual(len(requests),40)
                self.assertEqual(c[7].uploads,[])

    def test_actual_release_duplicate_or_repeated_pages_refuse(self):
        for mode in ('duplicate-id','duplicate-tag','repeated-page'):
            with self.subTest(mode=mode),tempfile.TemporaryDirectory() as temp:
                c=self.context(pathlib.Path(temp));rows=[c[7].release]+[{'id':400000+i,'tag_name':'retained-'+str(i)} for i in range(24)]
                if mode=='duplicate-id':rows[-1]['id']=rows[0]['id']
                if mode=='duplicate-tag':rows[-1]={**c[7].release,'id':400999}
                bodies=self.release_transport_fixture(c[7],rows)
                if mode=='repeated-page':
                    first=bodies['https://api.github.com/repos/FS-GG/.github/releases?per_page=25&page=1']
                    for page in range(1,41):bodies['https://api.github.com/repos/FS-GG/.github/releases?per_page=25&page='+str(page)]=first
                build,requests,_=self.runtime_request_fixture(bodies);c[6].api=GitHubAPI('inert-fixture-token')
                with patch('urllib.request.build_opener',side_effect=build),self.assertRaises(RuntimeError):
                    c[6].observe_runtime_recovery(ordered_effects(c[0])[12])
                self.assertLessEqual(len(requests),40);self.assertEqual(c[7].uploads,[])

    def test_actual_release_content_length_and_json_shape_refusals(self):
        url='https://api.github.com/repos/FS-GG/.github/releases?per_page=25&page=1'
        for mode in ('non-ascii','non-decimal','truncated','missing','duplicate-json'):
            with self.subTest(mode=mode):
                raw=b'[{}]' if mode!='duplicate-json' else b'[ {"id":1,"id":2} ]'
                build,requests,_=self.runtime_request_fixture({url:raw})
                def changed_build(*handlers):
                    opener=build(*handlers);original=opener.open
                    def open_request(*args,**kwargs):
                        response=original(*args,**kwargs)
                        if mode=='non-ascii':response.headers['Content-Length']='１２'
                        if mode=='non-decimal':response.headers['Content-Length']='-1'
                        if mode=='truncated':response.headers['Content-Length']=str(len(raw)+1)
                        if mode=='missing':response.headers.pop('Content-Length')
                        return response
                    opener.open=open_request;return opener
                with patch('urllib.request.build_opener',side_effect=changed_build):
                    api=GitHubAPI('inert-fixture-token')
                    if mode=='missing':self.assertEqual(api.runtime_recovery_json('repos/FS-GG/.github/releases?per_page=25&page=1'),[{}])
                    else:
                        with self.assertRaises(RuntimeError):api.runtime_recovery_json('repos/FS-GG/.github/releases?per_page=25&page=1')
                self.assertEqual(len(requests),1)

    def test_actual_release_selected_endpoint_and_deadline_remain_closed(self):
        for suffix in ('releases?per_page=100&page=1','releases?per_page=25&page=0',
                       'releases?per_page=25&page=41','releases?per_page=25&page=01'):
            with self.subTest(suffix=suffix),patch('urllib.request.build_opener',side_effect=AssertionError('unexpected transport')),self.assertRaises(RuntimeError):
                GitHubAPI('inert-fixture-token').runtime_recovery_json('repos/FS-GG/.github/'+suffix)
        url='https://api.github.com/repos/FS-GG/.github/releases?per_page=25&page=1'
        build,requests,_=self.runtime_request_fixture({url:b'[]'})
        with patch('urllib.request.build_opener',side_effect=build),patch('release_successor_provider.time.monotonic',side_effect=[0,13]),self.assertRaisesRegex(RuntimeError,'deadline'):
            GitHubAPI('inert-fixture-token').runtime_recovery_json('repos/FS-GG/.github/releases?per_page=25&page=1')
        self.assertEqual(len(requests),1)

    def test_actual_refusal_authentication_artifact_request_omits_accept(self):
        with tempfile.TemporaryDirectory() as temp:
            source = self.context(pathlib.Path(temp))[7]
            prefix = 'https://api.github.com/repos/FS-GG/.github/'
            bodies = {prefix+'actions/runs/38063768172': json.dumps(source.run).encode(),
                      prefix+'actions/artifacts/11675095562': json.dumps(source.artifact).encode(),
                      prefix+'actions/artifacts/11675095562/zip': source.proof_zip}
            build, requests, _ = self.runtime_request_fixture(bodies)
            with patch('urllib.request.build_opener', side_effect=build), \
                 patch.object(publisher, 'RUNTIME_REFUSAL_ZIP_SHA256', hashlib.sha256(source.proof_zip).hexdigest()), \
                 patch.object(publisher, 'RUNTIME_REFUSAL_MEMBER_SHA256', hashlib.sha256(source.proof_body).hexdigest()):
                proof = publisher.authenticate_runtime_refusal(GitHubAPI('inert-fixture-token'))
            self.assertEqual(proof['runId'], 38063768172)
            self.assertFalse(proof['thisInvocationRuntimeDispatchEntered'])
            self.assertEqual(len(requests), 3)
            self.assertEqual([r.get_header('Accept') for r in requests],
                             ['application/vnd.github+json', 'application/vnd.github+json', None])
            self.assertTrue(all(r.get_header('Authorization') == 'Bearer inert-fixture-token' for r in requests))

    def test_actual_runtime_json_and_release_asset_media_headers_unchanged(self):
        prefix = 'https://api.github.com/repos/FS-GG/.github/'
        build, requests, _ = self.runtime_request_fixture({prefix+'actions/runs/38063768172': b'{}',
                                                          prefix+'releases/assets/77': b'raw'})
        with patch('urllib.request.build_opener', side_effect=build):
            api = GitHubAPI('inert-fixture-token')
            self.assertEqual(api.runtime_recovery_json('repos/FS-GG/.github/actions/runs/38063768172'), {})
            self.assertEqual(api.download_runtime_recovery_asset(77, 3), b'raw')
        self.assertEqual([r.get_header('Accept') for r in requests],
                         ['application/vnd.github+json', 'application/octet-stream'])

    def test_actual_artifact_http_refusal_has_one_request_and_no_fallback(self):
        calls = []
        def fail(request, **kwargs):
            calls.append(request)
            raise urllib.error.HTTPError(request.full_url, 415, 'Unsupported Media Type', {}, None)
        with patch('urllib.request.build_opener', return_value=types.SimpleNamespace(open=fail)), \
             self.assertRaises(urllib.error.HTTPError) as failure:
            GitHubAPI('inert-fixture-token').download_runtime_refusal_artifact()
        self.assertEqual(failure.exception.code, 415)
        self.assertEqual(len(calls), 1)
        self.assertIsNone(calls[0].get_header('Accept'))

    def test_actual_artifact_redirect_keeps_bound_and_strips_authorization(self):
        url = 'https://api.github.com/repos/FS-GG/.github/actions/artifacts/11675095562/zip'
        build, requests, handlers = self.runtime_request_fixture({url:b'archive'})
        with patch('urllib.request.build_opener', side_effect=build):
            self.assertEqual(GitHubAPI('inert-fixture-token').download_runtime_refusal_artifact(), b'archive')
        redirect = handlers[0]
        target = 'https://retained-artifact.invalid/owned.zip'
        for _ in range(3):
            request = redirect.redirect_request(requests[0], None, 302, 'Found', {}, target)
            self.assertIsNone(request.get_header('Authorization'))
            self.assertIsNone(request.get_header('Accept'))
        with self.assertRaises(RuntimeError):
            redirect.redirect_request(requests[0], None, 302, 'Found', {}, target)

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
        for matched,dense in ((False,False),(True,False),(False,True),(True,True)):
            with self.subTest(matched=matched,dense=dense),tempfile.TemporaryDirectory() as temp:
                root=pathlib.Path(temp)/'fixture';root.mkdir();c=self.context(root,matched)
                if dense:
                    rows=[c[7].release]+[{'id':100000+i,'tag_name':'retained-'+str(i)} for i in range(998)]
                    c[7].release_pages={page:rows[(page-1)*25:page*25] for page in range(1,41)}
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
                print(json.dumps({'runtimeRecoveryActualCLI':{'matched':matched,'dense999':dense,'authority':receipt['authorityRequests'],'publisher':receipt['publisherRequests']}},sort_keys=True))

    def test_matched_observation_expiry_prevents_all_authority_mutations(self):
        for expiry_stage in ('observation', 'settlement-admission', 'cas-read'):
            with self.subTest(stage=expiry_stage), tempfile.TemporaryDirectory() as temp:
                root=pathlib.Path(temp);c=self.context(root,True);now=[0]
                original_observe=c[6].observe_runtime_recovery
                original_authorize=c[5].authorize
                original_get=c[1].get
                admitted=[False]
                def observe(effect):
                    value=original_observe(effect)
                    if expiry_stage=='observation':now[0]=11
                    return value
                def authorize(*args):
                    result=original_authorize(*args)
                    if args[2]=='settle':
                        admitted[0]=True
                        if expiry_stage=='settlement-admission':now[0]=11
                    return result
                def get(path):
                    result=original_get(path)
                    if admitted[0] and expiry_stage=='cas-read':now[0]=11
                    return result
                with patch.object(c[6],'observe_runtime_recovery',side_effect=observe), \
                     patch.object(c[5],'authorize',side_effect=authorize),patch.object(c[1],'get',side_effect=get),self.assertRaises(RuntimeError):
                    self.run_runtime(c,root/'attempt.json',clock=lambda:now[0])
                self.assertFalse(any(method in {'post','patch'}for method,_ in c[1].calls))
                self.assertEqual(c[4]._observed.state['generation'],26)
                self.assertEqual(c[7].uploads,[])

    def test_matched_payload_drift_during_observation_cannot_settle(self):
        with tempfile.TemporaryDirectory() as temp:
            root=pathlib.Path(temp);c=self.context(root,True)
            original=c[7].runtime_recovery_json
            moved=[False]
            def read(path):
                if '/releases?per_page=' in path and not moved[0]:
                    moved[0]=True;raw=b'different local and remote bytes'
                    (root/publisher.RUNTIME_NAME).write_bytes(raw)
                    row=next(x for x in c[7].assets if x['name']==publisher.RUNTIME_NAME)
                    row.update(size=len(raw),digest='sha256:'+hashlib.sha256(raw).hexdigest())
                return original(path)
            with patch.object(c[7],'runtime_recovery_json',side_effect=read),self.assertRaises(RuntimeError):
                self.run_runtime(c,root/'attempt.json')
            self.assertTrue(moved[0]);self.assertEqual(c[4]._observed.state['generation'],26)
            self.assertFalse(any(method in {'post','patch'}for method,_ in c[1].calls));self.assertEqual(c[7].uploads,[])

    def test_primary_upload_uncertainty_survives_report_failure(self):
        import io
        argv=['publisher','--publish','--recover-original-runtime-intent','--candidate-run-id','38026714312',
              '--candidate-artifact-id','11660242226','--candidate-archive-sha256',publisher.RECOVERY_CANDIDATE['archiveSha256'],'--workdir','/offline/unused']
        with patch.object(sys,'argv',argv),patch.object(publisher,'execute_publisher',side_effect=OSError('runtime upload uncertain')), \
             patch.object(publisher,'write_result',side_effect=OSError('report unavailable')),patch.object(sys,'stderr',new_callable=io.StringIO) as output:
            self.assertEqual(publisher.main(),1);self.assertIn('runtime upload uncertain',output.getvalue());self.assertIn('report unavailable',output.getvalue())


class SelectedReleaseBindingTests(unittest.TestCase):
    def test_new_ordinary_cut_has_103_102_and_distinct21(self):
        args = types.SimpleNamespace(recovery_checkpoint=False, repeat_original_nuget_kit_intent=False,
                                     recover_original_runtime_intent=False)
        self.assertEqual(publisher.selected_release_binding(args),
                         ("0.103.0", "0.102.0", "refs/heads/fsgg/v2/journal/release/utel-rel-21"))

    def test_original_102_checkpoint_retains_consumed20_after_ordinary_cut_moves(self):
        args = types.SimpleNamespace(recover_original_successor_checkpoint=True)
        self.assertEqual(publisher.selected_release_binding(args),
                         ("0.102.0", "0.101.0", "refs/heads/fsgg/v2/journal/release/utel-rel-20"))
        self.assertNotEqual(publisher.ORIGINAL_SUCCESSOR_REF, publisher.SUCCESSOR_REF)
        self.assertNotEqual(publisher.ORIGINAL_SUCCESSOR_VERSION, publisher.SUCCESSOR_VERSION)

    def test_every_explicit_original_profile_retains_101_100_and19(self):
        for selected in ("recovery_checkpoint", "repeat_original_nuget_kit_intent", "recover_original_runtime_intent"):
            args = types.SimpleNamespace(**{flag: flag == selected for flag in
                 ("recovery_checkpoint", "repeat_original_nuget_kit_intent", "recover_original_runtime_intent")})
            with self.subTest(profile=selected):
                self.assertEqual(publisher.selected_release_binding(args),
                                 ("0.101.0", "0.100.0", "refs/heads/fsgg/v2/journal/release/utel-rel-19"))



class PublicationReliabilityControls(unittest.TestCase):
    def context(self, generation=21, ordinary=False):
        candidate = json.loads((pathlib.Path(__file__).parent / "original-0102-manifest.json").read_bytes())
        candidate, authority, intent, _ = fixture(generation, candidate,
                    publisher.SUCCESSOR_CHECKPOINT_CANDIDATE["archiveSha256"], publisher.ORIGINAL_SUCCESSOR_REF)
        counter = publisher.CountedAuthorityAPI(authority,
                    ceiling=publisher.ORDINARY_AUTHORITY_CALL_CEILING,
                    quota_floor=publisher.ORDINARY_AUTHORITY_QUOTA_FLOOR)
        counter.admit_quota(now=1900000000)
        rows = report()
        journal = publisher.ReportingJournal(ProtectedReleaseJournal(counter, ref=publisher.ORIGINAL_SUCCESSOR_REF, main_directory=True), rows, detailed=True)
        api = MemoryPublisher();api.head = candidate["descriptor"]["sourceSha"]
        provider = MemoryProvider(ordered_effects(candidate), {})
        admission = publisher.ProtectedPublisherAdmission(
            SingleOperatorAdmission(api, candidate, api.head, 1, "EHotwagner", "refs/heads/main"), counter, publisher.ORIGINAL_SUCCESSOR_REF)
        publisher.prepare_release_journal(journal, counter, admission, provider, api, candidate, intent,
                                         api.head, api.head, False, lambda: None, recovery_checkpoint=not ordinary)
        journal.capture()
        provider.matched = set(journal._observed.state["effects"])
        expected = {"generation": generation, "logicalHead": journal._observed.head,
                    "stateSHA256": hashlib.sha256(canonical(journal._observed.state)).hexdigest()}
        return candidate, authority, counter, rows, journal, admission, publisher.ReportingProvider(provider, rows), expected

    def test_fixed_original_checkpoint_and_later_generation_share_one_interface(self):
        for generation in (21, 25, 32, 33):
            with self.subTest(generation=generation):
                c,a,counter,r,j,ad,p,expected = self.context(generation)
                publisher.validate_successor_checkpoint(c,j,expected)
                result = publisher.run_forward(c,j,ad,p,r,10,clock=lambda:0)
                self.assertIn(result,("checkpoint","complete"))
                self.assertLessEqual(r["iterationsAttempted"],4)
                self.assertEqual(sum(counter.attempted.values()),len(a.calls))
                self.assertLessEqual(sum(counter.attempted.values()),4080)
                self.assertEqual(r["startingJournal"]["generation"],generation)
                self.assertIsNotNone(r["lastObservedJournal"])
                observed = r["lastObservedJournal"]
                self.assertEqual(observed["stateSHA256"], hashlib.sha256(canonical(j._observed.state)).hexdigest())
                self.assertEqual(observed["intent"], {key: j._observed.state[key] for key in
                                  ("contentId", "sourceSha", "version", "candidateArchiveSha256", "operator")})
                next_expected = {key: observed[key] for key in ("generation", "logicalHead", "stateSHA256")}
                publisher.validate_successor_checkpoint(c, j, next_expected)
                self.assertFalse(r["publicationComplete"])
                print(json.dumps({"composedForwardStart":generation,"disposition":result,"authority":counter.report(),"providerDispatches":p.dispatched},sort_keys=True))

    def test_exact_expected_checkpoint_changes_refuse_before_provider_write(self):
        for key,value in (("generation",20),("logicalHead","0"*40),("stateSHA256","0"*64)):
            c,a,n,r,j,ad,p,expected=self.context()
            expected[key]=value
            with self.assertRaises(publisher.Refused):publisher.validate_successor_checkpoint(c,j,expected)
            self.assertEqual(p.dispatched,[])
            self.assertEqual(n.attempted["post"],0)
            self.assertEqual(n.attempted["patch"],0)

    def test_original_candidate_and_journal_mismatch_refuse(self):
        for target in ("content", "ref", "ordinary-ref", "ordinary-version"):
            c,a,n,r,j,ad,p,e=self.context()
            if target=="content":c=copy.deepcopy(c);c["contentId"]="sha256:"+"0"*64
            elif target=="ref":j.journal.ref=REF
            elif target=="ordinary-ref":j.journal.ref=publisher.SUCCESSOR_REF
            else:c=copy.deepcopy(c);c["descriptor"]["version"]=publisher.SUCCESSOR_VERSION
            with self.assertRaises(publisher.Refused):publisher.validate_successor_checkpoint(c,j,e)
            self.assertEqual(p.dispatched,[])

    def test_ordinary_wait_checkpoint_records_exact_intent_without_polling_or_resend(self):
        c,a,n,r,j,ad,p,e=self.context(ordinary=True)
        self.assertEqual(publisher.run_forward(c,j,ad,p,r,10,maximum=152,clock=lambda:0),"checkpoint")
        self.assertEqual(r["iterationsAttempted"],1)
        self.assertEqual(len(p.dispatched),1)
        self.assertEqual(r["lastObservedJournal"]["generation"],22)
        self.assertTrue(r["journalMutationMayHaveOccurred"])
        self.assertEqual(r["lastDispatch"]["state"],"applied")
        p.matched.remove(p.dispatched[0])
        self.assertEqual(publisher.run_forward(c,j,ad,p,r,10,maximum=152,clock=lambda:0),"checkpoint")
        self.assertEqual(len(p.dispatched),1)
        self.assertEqual(r["iterationsAttempted"],1)
        self.assertLess(sum(n.attempted.values()),publisher.ORDINARY_AUTHORITY_CALL_CEILING-100)
        self.assertEqual(sum(n.attempted.values()),len(a.calls))
        print(json.dumps({"ordinaryWaitActualEntries":n.report(),"providerDispatches":p.dispatched},sort_keys=True))

    def test_ordinary_budget_and_quota_are_distinct_and_refuse_before_send(self):
        a=MemoryAuthority();a.quota.update(remaining=1799,used=3201)
        n=publisher.CountedAuthorityAPI(a,ceiling=1500,quota_floor=1800)
        with self.assertRaisesRegex(publisher.Refused,"ordinary Authority quota"):n.admit_quota(now=1900000000)
        self.assertEqual(n.attempted,{"get":1,"post":0,"patch":0})
        n.attempted["get"]=1500
        before=len(a.calls)
        for method,args in (("get",()),("post",({},)),("patch",({},))):
            with self.assertRaises(publisher.Refused):getattr(n,method)("fixture",*args)
        self.assertEqual(len(a.calls),before)
        self.assertEqual(n.report()["ceiling"],1500)

    def test_actual_result_entry_marks_quota_wait_as_incomplete_checkpoint(self):
        with tempfile.TemporaryDirectory() as temp:
            result=pathlib.Path(temp)/'result.json'
            argv=['publisher','--publish','--candidate-run-id','38128101680',
                  '--candidate-artifact-id','11698335858','--candidate-archive-sha256',
                  publisher.SUCCESSOR_CHECKPOINT_CANDIDATE['archiveSha256'],
                  '--workdir',str(pathlib.Path(temp)/'uncreated'),'--result-path',str(result)]
            with patch.object(sys,'argv',argv),patch.object(publisher,'execute_publisher',side_effect=publisher.AuthorityQuotaWaiting('fixture quota waiting')):
                self.assertEqual(publisher.main(),0)
            value=json.loads(result.read_bytes())
            self.assertEqual(value['disposition'],'checkpoint');self.assertFalse(value['publicationComplete'])
            self.assertIsNone(value['failure']);self.assertIsNotNone(value['quotaWaiting'])
            self.assertFalse((pathlib.Path(temp)/'uncreated').exists())

    def test_budget_wait_propagates_through_real_protected_admission(self):
        c,a,n,r,j,ad,p,e=self.context()
        n.attempted['get']=n.ceiling-1
        before=len(a.calls)
        with self.assertRaises(publisher.AuthorityQuotaWaiting):
            ad.authorize(c['contentId'],ordered_effects(c)[10].identity,'intent',ordered_effects(c)[10].request_digest)
        self.assertLessEqual(len(a.calls)-before,1)
        self.assertIsNotNone(n.blocked);self.assertEqual(n.attempted['post'],0);self.assertEqual(n.attempted['patch'],0)
        # Genuine transport/CAS failures retain their different refusal semantics.
        n.attempted['get']=n.ceiling-1;n.api=types.SimpleNamespace(get=lambda path:(_ for _ in ()).throw(OSError('transport unavailable')))
        self.assertFalse(ad.authorize(c['contentId'],'tag','intent','0'*64))

    def test_fresh_journal_preparation_uses_original_mutation_deadline(self):
        candidate=json.loads((pathlib.Path(__file__).parent/'original-0102-manifest.json').read_bytes())
        a=MemoryAuthority(ref=publisher.ORIGINAL_SUCCESSOR_REF)
        n=publisher.CountedAuthorityAPI(a,ceiling=1500,quota_floor=1800);n.admit_quota(now=1900000000)
        now=[0];n.api=publisher.RuntimeDeadlineAuthority(n.api,10,lambda:now[0])
        r=report();j=publisher.ReportingJournal(ProtectedReleaseJournal(n,ref=publisher.ORIGINAL_SUCCESSOR_REF,main_directory=True),r)
        api=MemoryPublisher();api.head=candidate['descriptor']['sourceSha']
        p=MemoryProvider(ordered_effects(candidate),{});p._release=lambda:None
        composite=publisher.ProtectedPublisherAdmission(SingleOperatorAdmission(api,candidate,api.head,1,'EHotwagner','refs/heads/main'),n,publisher.ORIGINAL_SUCCESSOR_REF)
        ad=publisher.RuntimeDeadlineAdmission(composite,10,lambda:now[0])
        intent={'contentId':candidate['contentId'],'sourceSha':api.head,'version':'0.102.0','candidateArchiveSha256':publisher.SUCCESSOR_CHECKPOINT_CANDIDATE['archiveSha256'],'operator':'EHotwagner'}
        def uniqueness():now[0]=10
        with self.assertRaises(publisher.Refused):
            publisher.prepare_release_journal(j,n,ad,p,api,candidate,intent,api.head,api.head,False,uniqueness)
        self.assertEqual(n.attempted['post'],0);self.assertEqual(n.attempted['patch'],0)
        self.assertFalse(r['journalMutationMayHaveOccurred']);self.assertIsNone(j._observed)

    def test_actual_result_entry_preserves_primary_when_stderr_fails(self):
        class BrokenStderr:
            def write(self,*args):raise OSError('stderr unavailable')
            def flush(self):raise OSError('stderr unavailable')
        with tempfile.TemporaryDirectory() as temp:
            result=pathlib.Path(temp)/'result.json'
            argv=['publisher','--publish','--candidate-run-id','38128101680','--candidate-artifact-id','11698335858',
                  '--candidate-archive-sha256',publisher.SUCCESSOR_CHECKPOINT_CANDIDATE['archiveSha256'],
                  '--workdir',str(pathlib.Path(temp)/'unused'),'--result-path',str(result)]
            with patch.object(sys,'argv',argv),patch.object(publisher,'execute_publisher',side_effect=OSError('original execution failure')),patch.object(sys,'stderr',BrokenStderr()):
                self.assertEqual(publisher.main(),1)
            value=json.loads(result.read_bytes())
            self.assertEqual(value['failure']['chain'][0]['message'],'original execution failure')
            self.assertEqual(value['additionalReportingFailures'],[{'phase':'primary diagnostic','type':'OSError'}])
            self.assertFalse(value['publicationComplete'])
            argv[-1]=str(pathlib.Path(temp)/'second-result.json')
            retained=[]
            def fail_report(path,report):retained.append(report);raise OSError('result unavailable')
            with patch.object(sys,'argv',argv),patch.object(publisher,'execute_publisher',side_effect=RuntimeError('original execution failure')),patch.object(publisher,'write_result',side_effect=fail_report),patch.object(sys,'stderr',BrokenStderr()):
                self.assertEqual(publisher.main(),1)
            self.assertEqual(retained[0]['failure']['chain'][0]['message'],'original execution failure')
            self.assertEqual([x['phase']for x in retained[0]['additionalReportingFailures']],['primary diagnostic','result reporting diagnostic'])

    def test_matched_observation_expiry_refuses_dependent_settlement(self):
        c,a,n,r,j,ad,p,e=self.context(22)
        selected=ordered_effects(c)[10].identity
        now=[0]
        observe=p.provider.observe
        def expiring(effect):
            result=observe(effect)
            if effect.identity==selected:now[0]=10
            return result
        p.provider.observe=expiring
        with self.assertRaises(publisher.Refused):
            publisher.run_forward(c,j,ad,p,r,10,clock=lambda:now[0])
        self.assertEqual(n.attempted['post'],0);self.assertEqual(n.attempted['patch'],0)
        self.assertEqual(p.dispatched,[]);self.assertEqual(r['lastObservedJournal']['generation'],22)

    def test_deadline_failure_and_dispatch_failure_keep_truthful_checkpoint(self):
        c,a,n,r,j,ad,p,e=self.context()
        with self.assertRaises(publisher.Refused):publisher.run_forward(c,j,ad,p,r,0,clock=lambda:0)
        self.assertEqual(r["iterationsAttempted"],0);self.assertEqual(p.dispatched,[])
        p.provider.fail_dispatch=True
        with self.assertRaises(RuntimeError):publisher.run_forward(c,j,ad,p,r,10,clock=lambda:0)
        self.assertEqual(r["lastDispatch"]["state"],"unknown")
        self.assertEqual(r["lastObservedJournal"]["generation"],22)
        self.assertTrue(r["journalMutationMayHaveOccurred"])
        self.assertFalse(r["publicationComplete"])


class Original0103IdentityControls(unittest.TestCase):
    """Reachable immutable helper checks; historical full-entry positives stay in Git.

    Current original entry refusal and the permitted .104 journey are exercised by
    SuspensionControls. No old source or factory is repinned or bypassed here.
    """
    def test_production_tuple_and_real_retained_manifest_are_fixed(self):
        candidate = json.loads((pathlib.Path(__file__).parent / 'original-0103-manifest.json').read_bytes())
        publisher.validate_original_0103_manifest(candidate)
        self.assertEqual(publisher.ORIGINAL_0103_CANDIDATE['runId'], 38290966724)
        self.assertEqual(publisher.ORIGINAL_0103_CANDIDATE['artifactId'], 11766710761)
        self.assertEqual(publisher.ORIGINAL_0103_CANDIDATE['archiveSha256'],
            'd8b22728b9e36932bf8d31e1ab486b312f2d1aedddf96660c872fc69f97f25ba')
        self.assertEqual(hashlib.sha256((pathlib.Path(__file__).parent / 'original-0103-manifest.json').read_bytes()).hexdigest(),
            publisher.ORIGINAL_0103_CANDIDATE['manifestSha256'])
        with self.assertRaisesRegex(publisher.Refused,"original .103 dependency size differs"):
            publisher.verify_original_0103_dependencies()

    def test_manifest_source_content_package_and_predecessor_deviations_refuse(self):
        original = json.loads((pathlib.Path(__file__).parent / 'original-0103-manifest.json').read_bytes())
        for field in ('sourceSha', 'version', 'policyVersion', 'previousStableVersion', 'previousStableContentId'):
            altered = copy.deepcopy(original); altered['descriptor'][field] = 'different'
            with self.subTest(field=field), self.assertRaises(publisher.Refused):
                publisher.validate_original_0103_manifest(altered)
        altered = copy.deepcopy(original); altered['descriptor']['packages'][0]['artifact']['payloadSha256'] = 'sha256:' + 'f' * 64
        with self.assertRaises(publisher.Refused): publisher.validate_original_0103_manifest(altered)


class Original0103DescendantCompatibilityControls(unittest.TestCase):
    """Retain historical pins; bound the reviewed PR4544 descendant as a whole.

    Original full recovery baseline was110tests with8failures/3errors, first
    refusing dependency size. These controls do not erase original tag Unknown,
    authenticate credentials or authorize a native operation.
    """
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(); self.addCleanup(self.tmp.cleanup)
        self.root = pathlib.Path(self.tmp.name)
        for name in publisher.ORIGINAL_0103_DEPENDENCIES:
            target = self.root / name; target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes((ROOT / name).read_bytes())

    def test_all_nine_historical_pins_remain_original_and_only_two_descendants_are_selected(self):
        digest = hashlib.sha256((json.dumps(publisher.ORIGINAL_0103_DEPENDENCIES,
            sort_keys=True, separators=(',', ':')) + '\n').encode()).hexdigest()
        self.assertEqual(digest, '65556e0bd07ea88c687839321aafa761be66aebfcd2ca7d7ef821d9eab218520')
        self.assertEqual(set(publisher.ORIGINAL_0103_DESCENDANT_DEPENDENCIES), {
            'scripts/release_successor_provider.py', 'scripts/fsgg_feed.py'})
        # Exact reviewed descendant2fb32c51 source blobs, not current-file auto-repin.
        self.assertEqual(publisher.ORIGINAL_0103_DESCENDANT_DEPENDENCIES, {
            'scripts/release_successor_provider.py': {'bytes': 85901,
                'sha256': 'b20239f394379e2c42dba023d43065bdebe4e6a620ab8adba77be77c4689bd17'},
            'scripts/fsgg_feed.py': {'bytes': 22991,
                'sha256': 'f7650429432417af72168c288cdee347b338328848870cb4f2f18d6b62d0733f'}})

    def test_changed_current_source_does_not_refresh_reviewed_descendant_closure(self):
        with self.assertRaisesRegex(publisher.Refused, "original .103 dependency size differs"):
            publisher.verify_original_0103_dependencies(self.root)
        self.assertEqual(publisher.ORIGINAL_0103_CANDIDATE['sourceSha'],
                         '4d136d5450a7032d0b87c4b8a37f69f020144792')
        self.assertEqual(publisher.ORIGINAL_0103_TAG_CHECKPOINT['stateSHA256'],
                         '67018515b0e6b49f96986d7844729461099e950058d20d0a9fa0b8ba3f3864d4')

    def test_historical_first_size_failure_remains_reproducible_without_explicit_amendment(self):
        with patch.object(publisher, 'ORIGINAL_0103_DESCENDANT_DEPENDENCIES', {}):
            with self.assertRaisesRegex(publisher.Refused, 'original .103 dependency size differs'):
                publisher.verify_original_0103_dependencies(self.root)

    def synthetic_dependencies(self):
        # Test the pure whole-set matching algorithm with visibly synthetic bytes.
        # This never changes the immutable production maps or qualifies current .103.
        historical, descendant = {}, {}
        for name in publisher.ORIGINAL_0103_DEPENDENCIES:
            body = ('fixture-historical:' + name).encode()
            historical[name] = {'bytes':len(body),'sha256':hashlib.sha256(body).hexdigest()}
            (self.root/name).write_bytes(body)
        for name in publisher.ORIGINAL_0103_DESCENDANT_DEPENDENCIES:
            body = ('fixture-descendant:' + name).encode()
            descendant[name] = {'bytes':len(body),'sha256':hashlib.sha256(body).hexdigest()}
        return historical,descendant

    def test_complete_synthetic_historical_and_descendant_sets_pass_but_hybrids_refuse(self):
        historical,descendant = self.synthetic_dependencies()
        with patch.object(publisher,'ORIGINAL_0103_DEPENDENCIES',historical), \
             patch.object(publisher,'ORIGINAL_0103_DESCENDANT_DEPENDENCIES',descendant):
            self.assertEqual(publisher.verify_original_0103_dependencies(self.root),'utel-rel-21/frozen-source-compatibility/1')
            for name in descendant:
                path=self.root/name; old=path.read_bytes(); path.write_bytes(('fixture-descendant:'+name).encode())
                with self.subTest(name=name),self.assertRaisesRegex(publisher.Refused,'mixes unreviewed'):
                    publisher.verify_original_0103_dependencies(self.root)
                path.write_bytes(old)
            for name in descendant: (self.root/name).write_bytes(('fixture-descendant:'+name).encode())
            self.assertEqual(publisher.verify_original_0103_dependencies(self.root),publisher.ORIGINAL_0103_DESCENDANT_PROFILE)

    def test_all_nine_arbitrary_future_same_size_and_size_drift_refuse(self):
        historical,descendant = self.synthetic_dependencies()
        with patch.object(publisher,'ORIGINAL_0103_DEPENDENCIES',historical), \
             patch.object(publisher,'ORIGINAL_0103_DESCENDANT_DEPENDENCIES',descendant):
            for name in historical:
                path=self.root/name; body=path.read_bytes()
                for changed in (bytes([body[0]^1])+body[1:],body+b'future-drift'):
                    path.write_bytes(changed)
                    with self.subTest(name=name,size=len(changed)),self.assertRaises(publisher.Refused):
                        publisher.verify_original_0103_dependencies(self.root)
                path.write_bytes(body)


class Literal104ProviderFixture(MemoryPublisher):
    """Real provider behavior; only external API/process transports are inert.

    Package bytes are synthetic ZIPs, never an executable or a release candidate.
    The real saga checks and receipts run in process beneath the audit fence.
    """
    def __init__(self, root, suspension):
        super().__init__()
        self.root=root; self.tag=None; self.release=None; self.assets={}; self.feeds=set()
        spec=importlib.util.spec_from_file_location("literal104_saga_fixture",ROOT/'scripts/release-saga.py')
        self.saga=importlib.util.module_from_spec(spec);spec.loader.exec_module(self.saga)
        for package in PACKAGES:
            path=root/f'{package}.0.104.0.nupkg'
            with zipfile.ZipFile(path,'w') as archive:
                archive.writestr(package+'.nuspec',f'<package><metadata><id>{package}</id><version>0.104.0</version><repository type="git" commit="{self.head}" /></metadata></package>')
                archive.writestr('lib/inert-fixture.txt',b'inert source fixture; not executable package output')
        qualification={'schema':'fsgg.telemetry.standalone-qualification/2','qualified':True,
            'binding':{'sourceSha':self.head,'packageSha256':self.saga.sha256(root/'FS.GG.Coord.Cli.0.104.0.nupkg'),'sourceBinding':'prepared-for-release-manifest'},
            'checks':{key:True for key in ('startupP95Milliseconds','idleRssPeakBytes','coldCliSubmissionP95Milliseconds','warmInProcessAdmissionP95Milliseconds')},
            'limits':{'coldCliSubmissionP95Milliseconds':1000,'warmInProcessAdmissionP95Milliseconds':100},
            'packagedStore':{'schema':'fsgg.telemetry.packaged-store-performance/1','qualified':True,'assessment':'approved-local-durable'},
            'claims':{'processCrashAndFilesystemApi':True,'physicalPowerLoss':False,'mainInstalled':False,'publicRelease':False}}
        (root/'standalone-telemetry-runtime-evidence.json').write_bytes(canonical(qualification))
        (root/'standalone-telemetry-evidence.json').write_bytes(b'{}\n')
        (root/'previous-stable-channel.json').write_bytes(canonical({'version':'0.102.0','contentId':'sha256:'+'c'*64,'sourceSha':'d'*40,'promotedAt':'2026-10-01T00:00:00Z'}))
        assets=root/'dashboard';assets.mkdir();(assets/'index.html').write_text('inert dashboard fixture')
        self.path=root/'release-manifest.json'
        args=self.saga.parser().parse_args(['prepare','--release-id','github:0.104.0','--version','0.104.0',
            '--source-sha',self.head,'--source-tree','d'*40,'--policy-version','release-successor/1',
            '--previous-channel',str(root/'previous-stable-channel.json'),'--artifact-dir',str(root),
            *(arg for package in PACKAGES for arg in ('--expected-package',package)),
            '--dashboard-assets',str(assets),'--standalone-qualification',str(root/'standalone-telemetry-runtime-evidence.json'),'--output',str(self.path)])
        self.run_saga(args)
        args=self.saga.parser().parse_args(['preflight','--manifest',str(self.path),'--feed','both']);self.run_saga(args)
        self.candidate=json.loads(self.path.read_bytes())
        self.provider=LiveProvider(self,self.path,'inert-github','inert-nuget',suspension=suspension,logical_ref='refs/heads/fsgg/v2/journal/release/utel-rel-22')
        self.provider._download_package=self.download_package
        self.provider.dispatched=[]
        dispatch=self.provider.dispatch
        def record(effect):
            self.provider.dispatched.append(effect.identity)
            return dispatch(effect)
        self.provider.dispatch=record

    def run_saga(self,args):
        with redirect_stdout(io.StringIO()):args.run(args)

    def get(self,path):
        self.calls.append(path)
        if '/git/ref/tags/' in path:
            if self.tag is None:raise NotFound(path)
            return {'object':{'sha':self.tag}}
        if '/releases/tags/' in path:
            if self.release is None:raise NotFound(path)
            return copy.deepcopy(self.release)
        if '/releases?' in path:return [] if self.release is None else [copy.deepcopy(self.release)]
        if path.endswith('/releases/1/assets?per_page=100'):
            return [{'id':ident,'name':name,'size':len(raw),'state':'uploaded','digest':'sha256:'+hashlib.sha256(raw).hexdigest()} for ident,(name,raw) in self.assets.items()]
        return super().get(path)

    def post(self,path,body):
        if path.endswith('/git/refs'):
            assert self.tag is None and body['ref']=='refs/tags/coherent-set/v0.104.0'
            self.tag=body['sha'];return {'object':{'sha':self.tag}}
        assert path.endswith('/releases') and self.release is None
        self.release={**body,'id':1};return copy.deepcopy(self.release)

    def patch(self,path,body):
        assert path.endswith('/releases/1') and self.release['draft'] is True and body=={'draft':False,'make_latest':'true'}
        self.release.update(body);return copy.deepcopy(self.release)

    def upload_asset(self,release,name,path):
        assert release==1 and all(value[0]!=name for value in self.assets.values())
        ident=len(self.assets)+1;self.assets[ident]=(name,path.read_bytes());return {'id':ident}

    def download_asset(self,ident):return self.assets[ident][1]

    def download_package(self,feed,package):
        if (feed,package) not in self.feeds:return None
        source=self.root/f'{package}.0.104.0.nupkg'
        target=self.provider.observed/f'{feed}-{package}.0.104.0.nupkg';target.write_bytes(source.read_bytes())
        args=self.saga.parser().parse_args(['verify-external','--manifest',str(self.path),'--package',package,'--artifact',str(target)]);self.run_saga(args)
        return target

    def process(self,argv,**kwargs):
        if argv[:3]==['dotnet','nuget','push']:
            assert len(argv)==8 and argv[4]=='--source' and argv[6]=='--api-key'
            path=pathlib.Path(argv[3]);package=path.name.removesuffix('.0.104.0.nupkg')
            feed='github' if argv[5]=='https://nuget.pkg.github.com/FS-GG/index.json' else 'nuget'
            assert argv[5] in {'https://nuget.pkg.github.com/FS-GG/index.json','https://api.nuget.org/v3/index.json'}
            assert (feed,package) not in self.feeds and package in PACKAGES
            self.feeds.add((feed,package))
        else:
            assert argv[:2]==[sys.executable,str(ROOT/'scripts/release-saga.py')] and argv[2]=='record-observed'
            args=self.saga.parser().parse_args(argv[2:]);self.run_saga(args)
        return types.SimpleNamespace(returncode=0)


class SuspensionFixture(unittest.TestCase):
    """Actual maintained callers with in-memory Git and an authenticated source fixture.

    Only the external accepted-source payload/hash is synthetic. Original journal bytes,
    immutable identity pins, strict schema, protected writer and all caller code are real.
    The runner's audit fence forbids every process/network effect.
    """
    def setUp(self):
        import release_successor_journal as journal_module
        self.mod = journal_module
        self.allowed = journal_module.ALLOWED_SUCCESSOR
        self.original = journal_module.SUSPENDED_ORIGINAL
        self.authority = MemoryAuthority(self.allowed['logicalRef'])
        self.decision_source = b'inert accepted source fixture; no operation authority\n'
        self.original_decision = copy.deepcopy(journal_module.SUSPENSION_DECISION)
        self.pin = patch.dict(journal_module.SUSPENSION_DECISION,
                             {'sha256': hashlib.sha256(self.decision_source).hexdigest()})
        self.pin.start(); self.addCleanup(self.pin.stop)
        decision = journal_module.SUSPENSION_DECISION
        self.decision_path = f"repos/{decision['repository']}/contents/{decision['path']}?ref={decision['revision']}"
        self.authority.override[self.decision_path] = {'encoding': 'base64', 'content': base64.b64encode(self.decision_source).decode()}
        self.state = {'schema': 'fsgg.release-successor-journal/1', 'contentId': self.original['contentId'],
                      'sourceSha': self.original['sourceSha'], 'version': '0.103.0',
                      'candidateArchiveSha256': self.original['archiveSHA256'], 'operator': 'EHotwagner',
                      'generation': 2, 'effects': {'tag': 'intent'}}
        self.raw_state = canonical(self.state)
        self.assertEqual(hashlib.sha256(self.raw_state).hexdigest(), self.original['stateSHA256'])
        self.add_file('state/releases/utel-rel-21/release-state.json', self.raw_state)
        self.add_file('state/releases/utel-rel-21/release-head.txt', (self.original['logicalHead'] + '\n').encode())
        self.add_file('state/ordinary/sibling.json', b'original sibling bytes\n')
        self.record = {'schema': journal_module.SUSPENSION_SCHEMA, 'disposition': 'suspended',
                       'original': copy.deepcopy(self.original), 'allowedSuccessor': copy.deepcopy(self.allowed),
                       'decision': copy.deepcopy(decision)}
        self.raw = canonical(self.record)
        self.authority.calls.clear()

    def add_file(self, path, raw):
        api = self.authority
        parent = api.head
        tree = api.objects['commits', parent]['tree']['sha']
        blob = api.post(PREFIX + '/git/blobs', {'encoding': 'base64', 'content': base64.b64encode(raw).decode()})
        tree = api.post(PREFIX + '/git/trees', {'base_tree': tree, 'tree': [api.entry(path, 'blob', blob['sha'])]})
        commit = api.post(PREFIX + '/git/commits', {'tree': tree['sha'], 'parents': [parent], 'message': 'inert fixture'})
        api.patch(PREFIX + '/git/refs/heads/main', {'sha': commit['sha'], 'force': False})

    def install(self):
        return publisher.install_release_suspension(self.authority, self.raw, self.authority.head)

    def reader(self):
        return self.mod.ReleaseSuspension(self.authority)

    def writes(self):
        return [row for row in self.authority.calls if row[0] in {'post', 'patch'}]

class SuspensionControls(SuspensionFixture):
    def test_installer_preserves_old_unknown_and_unrelated_tree_exactly(self):
        before = self.authority.head
        after = self.install()
        self.assertNotEqual(before, after)
        reader = self.reader()
        self.assertEqual(reader.file(after, 'state/releases/utel-rel-21/release-state.json'), self.raw_state)
        self.assertEqual(reader.file(after, 'state/releases/utel-rel-21/release-head.txt'), (self.original['logicalHead']+'\n').encode())
        self.assertEqual(reader.file(after, 'state/ordinary/sibling.json'), b'original sibling bytes\n')
        self.assertEqual(reader.read(), self.record)
        self.authority.calls.clear()
        with self.assertRaisesRegex(publisher.Refused, 'already exists'):
            self.install()
        self.assertEqual(self.writes(), [])

    def test_actual_install_cli_binds_owner_source_input_and_expected_main(self):
        with tempfile.TemporaryDirectory() as directory:
            path=pathlib.Path(directory)/'suspension.json';path.write_bytes(self.raw)
            argv=['publisher','--install-release-suspension','--suspension-file',str(path),
                  '--expected-authority-main',self.authority.head,'--publisher-source-sha','b'*40]
            native=MemoryPublisher(); native_get=native.get
            def get(path):
                if path=='user': return {'login':'EHotwagner','type':'User'}
                if path.startswith(PREFIX) or path==self.decision_path:return self.authority.get(path)
                row=native_get(path)
                if path.endswith('/git/ref/heads/main'): row['object']['type']='commit'
                return row
            native.get=get
            def local(command,**kwargs):
                self.assertIn(command,(['git','rev-parse','HEAD'],['git','diff','--quiet','HEAD'],['git','status','--porcelain=v1','--untracked-files=all','--','scripts']))
                return types.SimpleNamespace(stdout='b'*40+'\n' if command[1]=='rev-parse' else '',returncode=0)
            environment={'GH_TOKEN':'inert-owner','ORDINARY_LEDGER_TOKEN':'inert-writer'}
            with patch.object(sys,'argv',argv),patch.dict(os.environ,environment,clear=True), \
                 patch.object(publisher,'GitHubAPI',side_effect=lambda token,**kwargs:native if token=='inert-owner' else self.authority), \
                 patch.object(publisher.subprocess,'run',side_effect=local):
                self.assertEqual(publisher.main(),0)
            self.assertEqual(self.reader().read(),self.record)
            for mutation in ('owner','source','local','record','parent'):
                api=copy.deepcopy(self.authority);api.calls.clear()
                def changed(path):
                    row=get(path)
                    if path=='user' and mutation=='owner': row['login']='caller'
                    if path.endswith('/git/ref/heads/main') and mutation=='source': row['object']['sha']='e'*40
                    return row
                native.get=changed
                body=self.raw if mutation!='record' else canonical({**self.record,'disposition':'resumed'})
                path.write_bytes(body)
                with self.subTest(mutation=mutation),patch.object(sys,'argv',argv), \
                     patch.dict(os.environ,environment,clear=True), \
                     patch.object(publisher,'GitHubAPI',side_effect=lambda token,**kwargs:native if token=='inert-owner' else api), \
                     patch.object(publisher.subprocess,'run',side_effect=lambda command,**k:types.SimpleNamespace(stdout=(('e' if mutation=='local' else 'b')*40+'\n') if command[1]=='rev-parse' else '',returncode=0)):
                    self.assertEqual(publisher.main(),1)
                self.assertEqual([row for row in api.calls if row[0] in {'post','patch'}],[])
                native.get=get

    def test_wrong_parent_protection_state_and_decision_refuse_before_writer(self):
        for mutation in ('parent', 'protection', 'state', 'decision'):
            with self.subTest(mutation=mutation):
                api = copy.deepcopy(self.authority)
                expected = api.head
                if mutation == 'parent': expected = 'e'*40
                if mutation == 'protection': api.controls['mainWriter']['enforcement'] = 'disabled'
                if mutation == 'state':
                    reader = self.mod.ReleaseSuspension(api)
                    rawpath = 'state/releases/utel-rel-21/release-state.json'
                    # Mutate the exact source blob in this isolated in-memory fixture.
                    tree = api.objects['commits', api.head]['tree']['sha']
                    for segment in rawpath.split('/'):
                        entry = next(row for row in api.objects['trees', tree]['tree'] if row['path'] == segment)
                        tree = entry['sha']
                    api.objects['blobs', tree]['content'] = base64.b64encode(self.raw_state+b' ').decode()
                if mutation == 'decision': api.override[self.decision_path]['content'] = base64.b64encode(b'wrong decision').decode()
                api.calls.clear()
                with self.assertRaises((RuntimeError, ValueError)):
                    publisher.install_release_suspension(api, self.raw, expected)
                self.assertEqual([row for row in api.calls if row[0] in {'post','patch'}], [])

    def test_install_main_cas_race_never_overwrites_sibling(self):
        api = self.authority
        original_post = api.post
        sibling = None
        def race(path, body):
            nonlocal sibling
            result = original_post(path, body)
            if path.endswith('/git/commits'):
                # An unrelated protected-main commit wins after our object is created.
                sibling = api.commit(api.objects['commits', api.head]['tree']['sha'], [api.head], 'competing sibling')
                api.head = sibling
            return result
        with patch.object(api, 'post', side_effect=race), self.assertRaisesRegex(publisher.Refused, 'moved before suspension CAS'):
            self.install()
        self.assertEqual(api.head, sibling)
        self.assertFalse(any(row[0] == 'patch' for row in api.calls))

    def test_strict_closed_bindings_duplicate_json_and_wrong_types_refuse(self):
        mutations = [('schema','unsupported'), ('disposition','resumed'), ('extra', True)]
        for field, value in mutations:
            row = copy.deepcopy(self.record); row[field] = value
            with self.subTest(field=field), self.assertRaises(self.mod.Refused):
                self.mod.strict_suspension(canonical(row))
        for section, field, value in [('original','generation',True), ('original','logicalHead','f'*40),
                ('original','stateSHA256','0'*64), ('original','runId',38580684078),
                ('allowedSuccessor','version','0.105.0'), ('allowedSuccessor','logicalRef',publisher.SUCCESSOR_REF),
                ('allowedSuccessor','predecessor','0.103.0'), ('allowedSuccessor','profile','ordinary/1'),
                ('decision','owner','caller'), ('decision','revision','f'*40), ('decision','url','https://example.invalid')]:
            row = copy.deepcopy(self.record); row[section][field] = value
            with self.subTest(section=section,field=field), self.assertRaises(self.mod.Refused):
                self.mod.strict_suspension(canonical(row))
        duplicate = self.raw.replace(b'"generation":2', b'"generation":2,"generation":2')
        with self.assertRaisesRegex(self.mod.Refused, 'duplicate'):
            self.mod.strict_suspension(duplicate)
        for raw in (b'{', b'[]', b'null', b'"x"', self.raw+b'NaN', b'x'*16385):
            with self.subTest(raw=raw[:10]), self.assertRaises(self.mod.Refused): self.mod.strict_suspension(raw)

    def test_reader_missing_changed_deleted_record_and_main_race_refuse(self):
        with self.assertRaisesRegex(self.mod.Refused, 'absent'): self.reader().read()
        self.install(); reader = self.reader(); reader.read()
        self.add_file(self.mod.SUSPENSION_PATH, self.raw + b' ')
        with self.assertRaisesRegex(self.mod.Refused, 'not canonical'): reader.read()
        with patch.object(reader,'file',return_value=None),self.assertRaisesRegex(self.mod.Refused,'absent'):
            reader.read()
        self.add_file(self.mod.SUSPENSION_PATH,self.raw)
        reader = self.reader()
        original_get = self.authority.get
        reads = 0
        def moving(path):
            nonlocal reads
            result = original_get(path)
            if path == PREFIX + '/git/ref/heads/main':
                reads += 1
                if reads == 2: result['object']['sha'] = 'f'*40
            return result
        with patch.object(self.authority, 'get', side_effect=moving), self.assertRaisesRegex(self.mod.Refused, 'moved'):
            reader.read()

    def test_actual_precredential_parser_denies_original_and_conflicts_before_exchange(self):
        self.install()
        for flags in ([], ['--original-0103-candidate'], ['--recover-original-0103-tag-intent']):
            with self.subTest(flags=flags), patch.object(sys, 'argv', ['publisher','--check-release-suspension',*flags]), \
                 patch.dict(os.environ, {'GH_TOKEN':'inert-read-only'}, clear=True), \
                 patch.object(publisher,'GitHubAPI', return_value=self.authority):
                self.authority.calls.clear()
                self.assertEqual(publisher.main(), 1)
                self.assertEqual(self.writes(), [])
        with patch.object(sys,'argv',['publisher','--check-release-suspension','--suspended-0103-successor']), \
             patch.dict(os.environ,{'GH_TOKEN':'inert-read-only'},clear=True), \
             patch.object(publisher,'GitHubAPI',return_value=self.authority):
            self.assertEqual(publisher.main(),0)
        for flag in ('original_0103_candidate','recover_original_0103_tag_intent','recovery_checkpoint',
                     'repeat_original_nuget_kit_intent','recover_original_runtime_intent','recover_original_successor_checkpoint'):
            with self.subTest(conflict=flag), self.assertRaises(publisher.Refused):
                publisher.selected_release_binding(types.SimpleNamespace(suspended_0103_successor=True, **{flag:True}))

    def test_real_successor_precredential_refuses_absent_malformed_and_wrong_binding(self):
        args = types.SimpleNamespace(suspended_0103_successor=True)
        with self.assertRaisesRegex(self.mod.Refused,'absent'):
            publisher.precredential_release_check(args,self.authority)
        for raw in (b'{', canonical({**self.record,'disposition':'resumed'}),
                    canonical({**self.record,'allowedSuccessor':{**self.allowed,'version':'0.105.0'}})):
            self.add_file(self.mod.SUSPENSION_PATH,raw)
            self.authority.calls.clear()
            with self.subTest(raw=raw[:20]),self.assertRaises(self.mod.Refused):
                publisher.precredential_release_check(args,self.authority)
            self.assertEqual(self.writes(),[])

    def test_production_default_entry_refuses_before_candidate_workdir_or_credential_read(self):
        self.install()
        with tempfile.TemporaryDirectory() as directory:
            args = types.SimpleNamespace(candidate_run_id=1,candidate_artifact_id=2,candidate_archive_sha256='a'*64,
                publish=False,preflight_only=True,recovery_checkpoint=False,repeat_original_nuget_kit_intent=False,
                recover_original_runtime_intent=False,workdir=pathlib.Path(directory)/'never-created')
            env={'GITHUB_SHA':'b'*40,'GITHUB_ACTOR':'EHotwagner','GITHUB_RUN_ID':'1','GITHUB_EVENT_NAME':'workflow_dispatch',
                 'GITHUB_REPOSITORY':'FS-GG/.github','GITHUB_REF':'refs/heads/main','GITHUB_RUN_ATTEMPT':'1','GH_TOKEN':'inert'}
            with patch.dict(os.environ,env,clear=True),patch.object(publisher,'GitHubAPI',return_value=self.authority), \
                 self.assertRaisesRegex(self.mod.Refused,'prospectively suspended'):
                publisher.execute_publisher(args, {'candidate':{}})
            self.assertFalse(args.workdir.exists())

    def test_allowed_real_preparation_genesis_and_per_effect_fence_keep_original_unknown(self):
        self.install()
        candidate = manifest(); candidate['descriptor'].update(version='0.104.0',previousStableVersion='0.102.0')
        candidate['contentId'] = 'sha256:' + hashlib.sha256(json.dumps(candidate['descriptor'],sort_keys=True,separators=(',',':')).encode()).hexdigest()
        intent = {'contentId':candidate['contentId'],'sourceSha':'b'*40,'version':'0.104.0',
                  'candidateArchiveSha256':'c'*64,'operator':'EHotwagner'}
        native = MemoryPublisher()
        journal = ProtectedReleaseJournal(self.authority, self.allowed['logicalRef'], main_directory=True)
        admission = publisher.ProtectedPublisherAdmission(SingleOperatorAdmission(native,candidate,'b'*40,1,'EHotwagner','refs/heads/main'),
                                                        self.authority,self.allowed['logicalRef'])
        temporary = tempfile.TemporaryDirectory(); self.addCleanup(temporary.cleanup)
        path = pathlib.Path(temporary.name)/'manifest.json'; path.write_bytes(canonical(candidate))
        native.tag = None
        native_get = native.get
        def get(path):
            if path.endswith('/git/ref/tags/coherent-set/v0.104.0') and native.tag:
                return {'object':{'sha':native.tag}}
            if path.endswith('/releases?per_page=100&page=1'): return []
            return native_get(path)
        native.get = get
        native.posts = []
        def post(path, body):
            native.posts.append((path,body)); native.tag = body['sha']; return {'object':{'sha':native.tag}}
        native.post = post
        provider = LiveProvider(native,path,'inert','inert',suspension=self.reader(),logical_ref=self.allowed['logicalRef'])
        uniqueness = []
        self.assertFalse(publisher.prepare_release_journal(journal,self.authority,admission,provider,native,candidate,intent,
                                                         'b'*40,'b'*40,False,lambda: uniqueness.append(True)))
        self.assertEqual(uniqueness,[True]); self.assertEqual(journal.read().generation,1)
        from release_successor_execution import advance
        self.assertEqual(advance(candidate,journal,admission,provider),'waiting')
        self.assertEqual(len(native.posts),1); self.assertEqual(journal.read().effects,{'tag':'intent'})
        self.assertEqual(advance(candidate,journal,admission,provider),'verified')
        self.assertEqual(journal.read().effects,{'tag':'verified'})
        native.head = 'e'*40
        self.assertFalse(admission.authorize(candidate['contentId'],'tag','dispatch',candidate['contentId']))
        native.head = 'b'*40
        native_get_run = native.get
        def terminal(path):
            row = native_get_run(path)
            if path.endswith('/actions/runs/1'): row['status'] = 'completed'
            return row
        with patch.object(native,'get',side_effect=terminal):
            self.assertFalse(admission.authorize(candidate['contentId'],'tag','dispatch',candidate['contentId']))
        def rerun(path):
            row = native_get_run(path)
            if path.endswith('/actions/runs/1'): row['run_attempt'] = 2
            return row
        with patch.object(native,'get',side_effect=rerun):
            self.assertFalse(admission.authorize(candidate['contentId'],'tag','dispatch',candidate['contentId']))
        for action in ('intent','dispatch','settle'):
            self.assertTrue(admission.authorize(candidate['contentId'],'tag',action,candidate['contentId']))
        self.add_file(self.mod.SUSPENSION_PATH,self.raw+b' ')
        self.authority.calls.clear()
        for action in ('intent','dispatch','settle'):
            self.assertFalse(admission.authorize(candidate['contentId'],'tag',action,candidate['contentId']))
        with self.assertRaises(self.mod.Refused): journal.compare_and_swap(journal.read(),'tag','intent')
        self.assertEqual(self.writes(),[])
        self.assertEqual(self.reader().file(self.authority.head,'state/releases/utel-rel-21/release-state.json'),self.raw_state)

    def test_shared_immutable_reader_requires_same_api_repository_and_literal_route(self):
        self.install();reader=self.reader();reader.read()
        journal=ProtectedReleaseJournal(self.authority,self.allowed['logicalRef'],main_directory=True,suspension=reader)
        before=len(self.authority.calls)
        first=journal._immutable_object('commits',self.authority.head)
        first['parents'].clear()
        self.assertTrue(journal._immutable_object('commits',self.authority.head)['parents'])
        self.assertEqual(len(self.authority.calls),before)
        for api,ref,main in ((MemoryAuthority(),self.allowed['logicalRef'],True),
                             (self.authority,self.mod.SUSPENDED_REF,True),
                             (self.authority,self.allowed['logicalRef'],False)):
            with self.subTest(ref=ref,main=main),self.assertRaisesRegex(self.mod.Refused,'different owner or binding'):
                ProtectedReleaseJournal(api,ref,main_directory=main,suspension=reader)
        for kind,oid in (('refs',self.authority.head),('commits','main'),('trees','x'*40)):
            with self.subTest(kind=kind,oid=oid),self.assertRaisesRegex(self.mod.Refused,'identity is invalid'):
                journal._immutable_object(kind,oid)

    def test_full_literal_104_sixteen_effects_fit_unchanged_authority_budget(self):
        self.install();self.authority.calls.clear()
        temporary=tempfile.TemporaryDirectory();self.addCleanup(temporary.cleanup)
        root=pathlib.Path(temporary.name)
        counted=publisher.CountedAuthorityAPI(self.authority,ceiling=publisher.ORDINARY_AUTHORITY_CALL_CEILING,
                                             quota_floor=publisher.ORDINARY_AUTHORITY_QUOTA_FLOOR)
        shared=self.mod.ReleaseSuspension(counted)
        native=Literal104ProviderFixture(root,shared);candidate=native.candidate;provider=native.provider
        effects=ordered_effects(candidate);self.assertEqual(len(effects),16)
        intent={'contentId':candidate['contentId'],'sourceSha':'b'*40,'version':'0.104.0','candidateArchiveSha256':'c'*64,'operator':'EHotwagner'}
        rows=report()
        counted.admit_quota()
        shared.authorize(self.allowed['logicalRef'],'0.104.0')
        journal=publisher.ReportingJournal(ProtectedReleaseJournal(counted,self.allowed['logicalRef'],main_directory=True,suspension=shared),rows,detailed=True)
        admission=publisher.ProtectedPublisherAdmission(SingleOperatorAdmission(native,candidate,'b'*40,1,'EHotwagner','refs/heads/main'),counted,self.allowed['logicalRef'],suspension=shared)
        publisher.prepare_release_journal(journal,counted,admission,provider,native,candidate,intent,'b'*40,'b'*40,False,lambda:None)
        journal.capture(); ticks=[0];sleeps=[]
        def sleep(seconds):sleeps.append(seconds);ticks[0]+=seconds
        try:
            with patch.object(publisher.subprocess,'run',side_effect=native.process):
                result=publisher.run_forward(candidate,journal,admission,publisher.ReportingProvider(provider,rows),rows,2700,
                    maximum=152,clock=lambda:ticks[0],sleep=sleep,continue_literal_successor=True)
        except publisher.AuthorityQuotaWaiting:
            from collections import Counter
            categories=Counter()
            for method,path in self.authority.calls:
                label=('immutable-object' if '/git/' in path and any('/git/'+kind+'/' in path for kind in ('commits','trees','blobs'))
                    else 'fresh-main-ref' if '/git/ref/' in path else 'pinned-path-history' if '/commits?' in path
                    else 'fresh-protection' if '/rules' in path else 'pinned-decision-source' if '/contents/' in path
                    else 'quota' if path=='rate_limit' else 'repository-identity' if path==PREFIX else 'write')
                categories[method+':'+label]+=1
            print(json.dumps({'literal104WholeInvocation':'budget-exhausted','authority':counted.report(),'requestCensus':dict(categories),
                'lastObservedGeneration':journal._observed.state['generation'],'dispatchedEffects':provider.dispatched,
                'virtualSleepSeconds':sum(sleeps),'iterations':rows['iterationsAttempted']},sort_keys=True))
            raise
        self.assertEqual(result,'complete')
        self.assertEqual(provider.dispatched,[effect.identity for effect in effects])
        self.assertEqual(journal.read().generation,33)
        self.assertEqual(journal.journal._observed.state['effects'],{effect.identity:'verified' for effect in effects})
        self.assertFalse(native.release['draft'])
        self.assertIsNotNone(provider._final_manifest())
        self.assertEqual(shared.file(self.authority.head,'state/releases/utel-rel-21/release-state.json'),self.raw_state)
        self.assertEqual(shared.file(self.authority.head,'state/releases/utel-rel-21/release-head.txt'),(self.original['logicalHead']+'\n').encode())
        self.assertEqual(shared.file(self.authority.head,'state/ordinary/sibling.json'),b'original sibling bytes\n')
        self.assertEqual(sum(counted.attempted.values()),len(self.authority.calls))
        self.assertLess(sum(counted.attempted.values()),1500)
        self.assertEqual(sleeps,[5]*16)
        print(json.dumps({'literal104FullEffects':16,'authorityRequests':counted.attempted,'ceiling':counted.ceiling,
                          'virtualSleepSeconds':sum(sleeps),'iterations':rows['iterationsAttempted'],'disposition':result},sort_keys=True))

    def test_direct_provider_original_and_missing_reader_cannot_dispatch(self):
        self.install()
        from release_successor_execution import Effect
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory)/'manifest.json'
            for version, logical_ref in (('0.103.0', self.mod.SUSPENDED_REF), ('0.104.0',self.allowed['logicalRef'])):
                row = manifest(); row['descriptor']['version'] = version; path.write_bytes(canonical(row))
                native = MemoryPublisher()
                provider = LiveProvider(native,path,'inert','inert')
                with self.assertRaises(ProviderRefused): provider.dispatch(Effect('tag','b'*40,row['contentId']))
                provider.suspension = self.reader(); provider.logical_ref = logical_ref
                if version == '0.103.0':
                    with self.assertRaises(self.mod.Refused): provider.dispatch(Effect('tag','b'*40,row['contentId']))
                else:
                    with patch.object(native,'post',return_value={},create=True) as post:
                        self.assertEqual(provider.dispatch(Effect('tag','b'*40,row['contentId'])).state,'applied')
                        post.assert_called_once()
                    provider.suspension.read(); self.add_file(self.mod.SUSPENSION_PATH,self.raw+b' ')
                    with patch.object(native,'post',create=True) as post, self.assertRaises(self.mod.Refused):
                        provider.dispatch(Effect('tag','b'*40,row['contentId']))
                    post.assert_not_called()

    def test_original_journal_init_and_direct_recovery_are_denied(self):
        self.install()
        journal = ProtectedReleaseJournal(self.authority,self.mod.SUSPENDED_REF,main_directory=True)
        self.authority.calls.clear()
        intent = {key:self.state[key] for key in ('contentId','sourceSha','version','candidateArchiveSha256','operator')}
        with self.assertRaises(self.mod.Refused): journal.initialize(intent)
        with self.assertRaises(self.mod.Refused):
            publisher.run_original_tag_recovery({},journal,None,None,self.authority,None,{},100,None,None,preflight=True)
        for logical_ref in (self.mod.SUSPENDED_REF,self.allowed['logicalRef']):
            legacy = ProtectedReleaseJournal(self.authority,logical_ref)
            with self.assertRaisesRegex(self.mod.Refused,'protected-main'):
                legacy.initialize({**intent,'version':'0.104.0' if logical_ref == self.allowed['logicalRef'] else '0.103.0'})
        self.assertEqual(self.writes(),[])

    def test_precredential_workflow_order_and_candidate_binding(self):
        workflow = (ROOT/'.github/workflows/release-successor-publish.yml').read_text()
        fence = workflow.index('name: Read the installed release suspension')
        for name in ('actions/setup-dotnet','actions/create-github-app-token','NuGet/login'):
            self.assertLess(fence,workflow.index(name))
        self.assertNotIn('disposition_json:',workflow)
        candidate = (ROOT/'.github/workflows/release-successor-candidate.yml').read_text()
        self.assertIn('test "$version" = 0.104.0',candidate)
        self.assertNotIn('-p:Version=',candidate)
        self.assertIn('name: release-successor-candidate-${{ github.sha }}-${{ github.run_id }}',candidate)
        self.assertIn('Separate evidence: never add a member to the exact seven-member publisher archive.',candidate)


class LiteralForwardControls(SuspensionFixture):
    """Real first-effect journal/admission controls for the selected bounded loop."""
    def context(self):
        if not hasattr(self,'_forward_base'):self._forward_base=copy.deepcopy(self.authority)
        self.authority=copy.deepcopy(self._forward_base)
        self.install();self.authority.calls.clear()
        candidate=manifest();candidate['descriptor'].update(version='0.104.0',previousStableVersion='0.102.0')
        candidate['contentId']='sha256:'+hashlib.sha256(json.dumps(candidate['descriptor'],sort_keys=True,separators=(',',':')).encode()).hexdigest()
        counted=publisher.CountedAuthorityAPI(self.authority,ceiling=1500,quota_floor=1800);counted.admit_quota()
        shared=self.mod.ReleaseSuspension(counted);rows=report();native=MemoryPublisher()
        journal=publisher.ReportingJournal(ProtectedReleaseJournal(counted,self.allowed['logicalRef'],main_directory=True,suspension=shared),rows,detailed=True)
        admission=publisher.ProtectedPublisherAdmission(SingleOperatorAdmission(native,candidate,'b'*40,1,'EHotwagner','refs/heads/main'),counted,self.allowed['logicalRef'],suspension=shared)
        provider=MemoryProvider(ordered_effects(candidate),{});provider._release=lambda:None
        intent={'contentId':candidate['contentId'],'sourceSha':'b'*40,'version':'0.104.0','candidateArchiveSha256':'c'*64,'operator':'EHotwagner'}
        publisher.prepare_release_journal(journal,counted,admission,provider,native,candidate,intent,'b'*40,'b'*40,False,lambda:None)
        journal.capture();ticks=[0];sleeps=[]
        def sleep(seconds):sleeps.append(seconds);ticks[0]+=seconds
        def run(**kwargs):
            return publisher.run_forward(candidate,journal,admission,publisher.ReportingProvider(provider,rows),rows,
                kwargs.pop('deadline',2700),maximum=kwargs.pop('maximum',3),clock=lambda:ticks[0],sleep=kwargs.pop('sleep',sleep),
                continue_literal_successor=True,**kwargs)
        return candidate,counted,shared,rows,native,journal,admission,provider,ticks,sleeps,run

    def test_joined_cas_boundaries_refuse_drift_without_reusing_mutable_observation(self):
        for edge in (1,2,3,4):
            with self.subTest(edge=edge):
                c,api,reader,rows,native,journal,admission,provider,ticks,sleeps,run=self.context()
                expected=journal.read();before=dict(api.attempted);original=self.authority.get;seen=[0];triggered=[False]
                def get(path):
                    if path==PREFIX+'/git/ref/heads/main':
                        seen[0]+=1
                        if seen[0]==edge and not triggered[0]:
                            triggered[0]=True
                            if edge==1:self.add_file('state/releases/utel-rel-21/release-state.json',self.raw_state+b' ')
                            else:self.add_file('state/ordinary/sibling.json',b'concurrent sibling change\n')
                    return original(path)
                with patch.object(self.authority,'get',side_effect=get),self.assertRaises(self.mod.Refused):
                    journal.compare_and_swap(expected,'tag','intent')
                self.assertTrue(triggered[0]);self.assertEqual(api.attempted['patch'],before['patch'])
                self.assertEqual(api.attempted['post']-before['post'],0 if edge<=2 else 7)
                self.assertEqual(provider.dispatched,[])

    def test_joined_cas_changed_record_and_nonforced_conflict_preserve_original(self):
        for mode in ('missing','malformed','wrong-version','conflict'):
            with self.subTest(mode=mode):
                c,api,reader,rows,native,journal,admission,provider,ticks,sleeps,run=self.context()
                expected=journal.read();before=dict(api.attempted)
                if mode=='conflict':self.authority.reject_patch=True
                elif mode=='wrong-version':
                    changed=copy.deepcopy(self.record);changed['allowedSuccessor']['version']='0.105.0'
                    self.add_file(self.mod.SUSPENSION_PATH,canonical(changed))
                elif mode=='malformed':self.add_file(self.mod.SUSPENSION_PATH,self.raw+b' ')
                else:
                    # A new physical tree deliberately lacks the required entry.
                    path=self.mod.SUSPENSION_PATH
                    parent=self.authority.head;tree=self.authority.objects['commits',parent]['tree']['sha']
                    removed=self.authority.post(PREFIX+'/git/trees',{'base_tree':tree,'tree':[{'path':path,'sha':None}]})
                    commit=self.authority.post(PREFIX+'/git/commits',{'tree':removed['sha'],'parents':[parent],'message':'inert missing record'})
                    self.authority.patch(PREFIX+'/git/refs/heads/main',{'sha':commit['sha'],'force':False})
                with self.assertRaises(self.mod.Refused):journal.compare_and_swap(expected,'tag','intent')
                self.assertEqual(api.attempted['patch']-before['patch'],1 if mode=='conflict' else 0)
                self.assertEqual(api.attempted['post']-before['post'],7 if mode=='conflict' else 0)
                self.assertEqual(reader.file(self.authority.head,'state/releases/utel-rel-21/release-state.json'),self.raw_state)
                self.assertEqual(provider.dispatched,[])

    def test_joined_cas_allows_unrelated_write_before_opening_and_preserves_it(self):
        c,api,reader,rows,native,journal,admission,provider,ticks,sleeps,run=self.context()
        expected=journal.read();self.add_file('state/ordinary/sibling.json',b'new sibling before boundary\n')
        self.assertTrue(journal.compare_and_swap(expected,'tag','intent'))
        self.assertEqual(reader.file(self.authority.head,'state/ordinary/sibling.json'),b'new sibling before boundary\n')
        self.assertEqual(reader.file(self.authority.head,'state/releases/utel-rel-21/release-state.json'),self.raw_state)

    def test_delayed_visibility_and_unknown_send_never_resend_retained_intent(self):
        for response in ('applied','unknown'):
            with self.subTest(response=response):
                c,api,reader,rows,native,journal,admission,provider,ticks,sleeps,run=self.context()
                def dispatch(effect):provider.dispatched.append(effect.identity);return Dispatch(response)
                provider.dispatch=dispatch
                def sleep(seconds):
                    sleeps.append(seconds);ticks[0]+=seconds
                    if len(sleeps)==2:provider.matched.add('tag')
                self.assertEqual(run(maximum=4,sleep=sleep),'checkpoint')
                self.assertEqual(provider.dispatched,['tag','draft'])
                self.assertEqual(journal._observed.state['effects'],{'tag':'verified','draft':'intent'})
                self.assertEqual(sleeps,[5,5])

    def test_permanent_absence_stops_at_original_iteration_bound_without_resend(self):
        *_,provider,ticks,sleeps,run=self.context()
        provider.dispatch=lambda effect:(provider.dispatched.append(effect.identity) or Dispatch('unknown'))
        self.assertEqual(run(maximum=3),'checkpoint')
        self.assertEqual(provider.dispatched,['tag']);self.assertEqual(sleeps,[5,5])

    def test_no_sleep_without_next_iteration_or_full_interval(self):
        for maximum,deadline in ((1,2700),(3,5)):
            with self.subTest(maximum=maximum,deadline=deadline):
                *_,provider,ticks,sleeps,run=self.context()
                self.assertEqual(run(maximum=maximum,deadline=deadline),'checkpoint')
                self.assertEqual(provider.dispatched,['tag']);self.assertEqual(sleeps,[])

    def test_error_unknown_and_mismatch_stop_without_poll_or_later_send(self):
        for state in ('unknown','mismatched','error'):
            with self.subTest(state=state):
                c,api,reader,rows,native,journal,admission,provider,ticks,sleeps,run=self.context()
                original=provider.observe
                def observe(effect):
                    if provider.dispatched and effect.identity=='tag':
                        if state=='error':raise RuntimeError('inert observation failed')
                        return Observation(state)
                    return original(effect)
                provider.observe=observe
                with self.assertRaises(RuntimeError):run()
                self.assertEqual(provider.dispatched,['tag']);self.assertEqual(sleeps,[5])

    def test_post_sleep_main_run_suspension_and_protection_drift_stop_before_settlement(self):
        for mode in ('source','run','suspension','protection'):
            with self.subTest(mode=mode):
                c,api,reader,rows,native,journal,admission,provider,ticks,sleeps,run=self.context()
                original=native.get
                def sleep(seconds):
                    ticks[0]+=seconds;sleeps.append(seconds)
                    if mode=='source':native.head='e'*40
                    elif mode=='run':
                        def get(path):
                            row=original(path)
                            if '/actions/runs/' in path:row['status']='cancelled'
                            return row
                        native.get=get
                    elif mode=='suspension':self.add_file(self.mod.SUSPENSION_PATH,self.raw+b' ')
                    else:next(iter(self.authority.controls.values()))['enforcement']='disabled'
                with self.assertRaises(RuntimeError):run(sleep=sleep)
                self.assertEqual(provider.dispatched,['tag']);self.assertEqual(sleeps,[5])
                self.assertEqual(journal._observed.state['effects'],{'tag':'intent'})

    def test_deadline_expiration_during_wait_stops_before_another_advance(self):
        *_,provider,ticks,sleeps,run=self.context()
        def sleep(seconds):ticks[0]=11;sleeps.append(seconds)
        with self.assertRaisesRegex(publisher.Refused,'deadline expired'):run(deadline=10,sleep=sleep)
        self.assertEqual(provider.dispatched,['tag']);self.assertEqual(sleeps,[5])


class HostedSuspensionInstallControls(SuspensionFixture):
    """Actual two-stage CLI and four guarded Git edges; no issuer/process/network runs."""
    def setUp(self):
        super().setUp()
        temporary=tempfile.TemporaryDirectory(); self.addCleanup(temporary.cleanup)
        self.root=pathlib.Path(temporary.name)
        self.inputs={
            'install_release_suspension':True,'expected_authority_main':self.authority.head,
            'candidate_run_id':'','candidate_artifact_id':'','candidate_archive_sha256':'',
            'publish':False,'qualify_original_tag_authentication_only':False,'suspended_0103_successor':False,
            'original_0103_candidate':False,'recover_original_0103_tag_intent':False,'recovery_checkpoint':False,
            'repeat_original_nuget_kit_intent':False,'recover_original_runtime_intent':False,
            'recover_original_successor_checkpoint':False,'expected_checkpoint':''}
        self.environment={'RUNNER_TEMP':str(self.root),'GITHUB_OUTPUT':str(self.root/'outputs'),
            'GH_TOKEN':'inert-reader','ORDINARY_LEDGER_TOKEN':'inert-writer','ORDINARY_LEDGER_APP_ID':'4882140',
            'ACTUAL_INSTALLATION_ID':'160261608','EXPECTED_INSTALLATION_ID':'160261608',
            'GITHUB_SHA':'b'*40,'GITHUB_RUN_ID':'1','GITHUB_RUN_ATTEMPT':'1','GITHUB_ACTOR':'EHotwagner',
            'GITHUB_TRIGGERING_ACTOR':'EHotwagner','GITHUB_REPOSITORY':'FS-GG/.github',
            'GITHUB_REF':'refs/heads/main','GITHUB_EVENT_NAME':'workflow_dispatch'}
        self.native=MemoryPublisher()
        self.run=self.native.get('repos/FS-GG/.github/actions/runs/1')
        self.run.update(id=1,triggering_actor={'login':'EHotwagner'},repository={'id':1269292704,'full_name':'FS-GG/.github'})
        original_get=self.native.get
        def native_get(path):
            if path.startswith(PREFIX) or path==self.decision_path:
                return self.authority.get(path)
            if path.endswith('/actions/runs/1'): return copy.deepcopy(self.run)
            row=original_get(path)
            if path.endswith('/git/ref/heads/main'): row['object']['type']='commit'
            return row
        self.native.get=native_get
        self.authority.override['installation/repositories?per_page=100']={
            'total_count':1,'repositories':[{'id':1351660651,'full_name':REPOSITORY}]}
        self.status=''; self.tokens=[]
        self.prepared_path=self.root/'release-successor-suspension-prepared.json'
        self.result_path=self.root/'release-successor-result.json'

    def invoke(self,flag,digest=None):
        argv=['publisher',flag]+(['--prepared-sha256',digest] if digest is not None else [])
        def api(token,**kwargs):
            self.tokens.append(token)
            return self.native if token=='inert-reader' else self.authority
        def local(command,**kwargs):
            self.assertIn(command,(['git','rev-parse','HEAD'],['git','diff','--quiet','HEAD'],
                                  ['git','status','--porcelain=v1','--untracked-files=all','--','scripts']))
            return types.SimpleNamespace(stdout='b'*40+'\n' if command[1]=='rev-parse' else self.status if command[1]=='status' else '',returncode=0)
        with patch.object(sys,'argv',argv),patch.dict(os.environ,{**self.environment,'FSGG_RELEASE_INPUTS':json.dumps(self.inputs)},clear=True), \
             patch.object(publisher,'GitHubAPI',side_effect=api),patch.object(publisher.subprocess,'run',side_effect=local), \
             patch.object(sys,'stderr',io.StringIO()) as errors:
            result=publisher.main()
            self.last_error=errors.getvalue()
            return result

    def prepare(self):
        self.assertEqual(self.invoke('--prepare-hosted-release-suspension'),0,self.last_error)
        self.assertEqual(self.tokens,['inert-reader'])
        return hashlib.sha256(self.prepared_path.read_bytes()).hexdigest()

    def test_real_hosted_prepare_and_install_only_preserve_original_and_result_custody(self):
        digest=self.prepare()
        prepared=json.loads(self.prepared_path.read_bytes())
        self.assertEqual(canonical(prepared['record']),self.raw)
        self.assertEqual(prepared['execution'],{'sourceSha':'b'*40,'runId':1,'attempt':1,'operator':'EHotwagner'})
        self.assertEqual(self.writes(),[])
        self.assertEqual(self.invoke('--install-hosted-release-suspension',digest),0,self.last_error)
        report=json.loads(self.result_path.read_bytes())
        self.assertEqual(report['disposition'],'install-only-complete'); self.assertIs(report['publicationComplete'],False)
        self.assertEqual([row['method'] for row in report['attemptedRequests']],['post','post','post','patch'])
        self.assertTrue(all(re.fullmatch('[0-9a-f]{40}',row['returnedSHA']) for row in report['attemptedRequests']))
        self.assertEqual(report['attemptedRequests'][-1]['intendedRefUpdate']['expectedParent'],prepared['expectedParent'])
        self.assertEqual(self.reader().read(),self.record)
        self.assertEqual(self.reader().file(self.authority.head,'state/releases/utel-rel-21/release-state.json'),self.raw_state)
        self.assertEqual(self.reader().file(self.authority.head,'state/ordinary/sibling.json'),b'original sibling bytes\n')
        retained=self.result_path.read_bytes(); writes=list(self.writes())
        self.assertEqual(self.invoke('--install-hosted-release-suspension',digest),1)
        self.assertEqual(self.result_path.read_bytes(),retained); self.assertEqual(self.writes(),writes)

    def test_every_mixed_install_input_refuses_before_reader_or_writer_credential_access(self):
        for key,value in list((key,True) for key in ('publish','qualify_original_tag_authentication_only','suspended_0103_successor',
                'original_0103_candidate','recover_original_0103_tag_intent','recovery_checkpoint','repeat_original_nuget_kit_intent',
                'recover_original_runtime_intent','recover_original_successor_checkpoint'))+[
                ('candidate_run_id','1'),('candidate_artifact_id','2'),('candidate_archive_sha256','a'*64),('expected_checkpoint','{}'),
                ('expected_authority_main',''),('expected_authority_main','e'*39),('unknown',False)]:
            old=copy.deepcopy(self.inputs); self.inputs[key]=value
            with self.subTest(key=key,value=value):
                self.assertEqual(self.invoke('--prepare-hosted-release-suspension'),1)
                self.assertEqual(self.tokens,[]); self.assertEqual(self.writes(),[])
                self.assertFalse(self.prepared_path.exists())
            self.inputs=old

    def test_publication_coordinates_and_install_parent_are_checked_before_issuers(self):
        value={**self.inputs,'install_release_suspension':False,'expected_authority_main':''}
        for changes in ({},{'candidate_run_id':'0'},{'candidate_run_id':'1','candidate_artifact_id':'2','candidate_archive_sha256':'a'*63}):
            with self.subTest(changes=changes),self.assertRaises(publisher.Refused): publisher.validate_workflow_inputs({**value,**changes})
        good={**value,'candidate_run_id':'1','candidate_artifact_id':'2','candidate_archive_sha256':'a'*64}
        self.assertEqual(publisher.validate_workflow_inputs(good),'publication')
        with self.assertRaisesRegex(publisher.Refused,'requires install-only'):
            publisher.validate_workflow_inputs({**good,'expected_authority_main':'e'*40})

    def test_native_run_actor_source_attempt_and_reader_refusals_precede_issuer(self):
        for field,value in [('id',2),('status','completed'),('run_attempt',2),('run_attempt',True),('head_sha','e'*40),
                ('path','.github/workflows/other.yml'),('actor',{'login':'caller'}),('triggering_actor',{'login':'caller'}),
                ('repository',{'id':1,'full_name':'FS-GG/.github'})]:
            old=copy.deepcopy(self.run); self.run[field]=value
            with self.subTest(field=field,value=value):
                self.assertEqual(self.invoke('--prepare-hosted-release-suspension'),1)
                self.assertNotIn('inert-writer',self.tokens); self.assertFalse(self.prepared_path.exists()); self.assertEqual(self.writes(),[])
            self.run=old
        self.authority.override[PREFIX]=NotFound('reader denied')
        self.assertEqual(self.invoke('--prepare-hosted-release-suspension'),1)
        self.assertFalse(self.prepared_path.exists()); self.assertNotIn('inert-writer',self.tokens)

    def test_actual_cli_rejects_relevant_untracked_import_sibling_before_issuer(self):
        scripts=self.root/'source'/'scripts';scripts.mkdir(parents=True)
        sibling=scripts/'untracked_installer_sibling.py';sibling.write_text('# inert, never imported\n')
        self.status='?? scripts/'+sibling.name+'\n'
        with patch.object(publisher,'__file__',str(scripts/'release-successor-publish.py')):
            self.assertEqual(self.invoke('--prepare-hosted-release-suspension'),1)
        self.assertIn('import-root source is not clean',self.last_error)
        self.assertFalse(self.prepared_path.exists()); self.assertNotIn('inert-writer',self.tokens); self.assertEqual(self.writes(),[])

    def test_wrong_installation_or_repository_scope_refuses_before_writes(self):
        digest=self.prepare()
        for key in ('ACTUAL_INSTALLATION_ID','EXPECTED_INSTALLATION_ID'):
            old=self.environment[key];self.environment[key]='1'
            with self.subTest(key=key):
                self.assertEqual(self.invoke('--install-hosted-release-suspension',digest),1)
                self.assertEqual(self.writes(),[])
            self.environment[key]=old
            # No effect was sent; reset only this independent synthetic fixture result.
            self.result_path.unlink(); self.prepared_path.unlink(); self.tokens=[];digest=self.prepare()
        self.authority.override['installation/repositories?per_page=100']['total_count']=2
        self.assertEqual(self.invoke('--install-hosted-release-suspension',digest),1)
        self.assertEqual(self.writes(),[])

    def test_prepared_record_digest_input_and_parent_drift_cannot_acquire_writer(self):
        digest=self.prepare(); original=self.prepared_path.read_bytes()
        for raw,selected in [(original+b' ',digest),(original,'0'*64),
                (canonical({**json.loads(original),'recordSHA256':'0'*64}),digest)]:
            self.prepared_path.write_bytes(raw)
            with self.subTest(raw=raw[:20],digest=selected):
                self.assertEqual(self.invoke('--install-hosted-release-suspension',selected),1)
                self.assertNotIn('inert-writer',self.tokens); self.assertEqual(self.writes(),[])
        self.prepared_path.write_bytes(original)
        self.inputs['expected_authority_main']='e'*40
        self.assertEqual(self.invoke('--install-hosted-release-suspension',digest),1)
        self.assertNotIn('inert-writer',self.tokens); self.assertEqual(self.writes(),[])

    def test_cancellation_at_each_post_stops_the_next_effect_with_original_identity(self):
        for cutoff in (1,2,3):
            # Each subcase is an independent original fixture, never a native retry.
            case=HostedSuspensionInstallControls();case.setUp()
            try:
                digest=case.prepare(); original=case.authority.post; count=0
                def post(path,body):
                    nonlocal count
                    response=original(path,body);count+=1
                    if count==cutoff:case.run['status']='completed'
                    return response
                with patch.object(case.authority,'post',side_effect=post):
                    self.assertEqual(case.invoke('--install-hosted-release-suspension',digest),1)
                report=json.loads(case.result_path.read_bytes())
                self.assertEqual(len(report['attemptedRequests']),cutoff)
                self.assertFalse(any(row[0]=='patch' for row in case.writes()))
                self.assertEqual(case.reader().file(case.authority.head,'state/releases/utel-rel-21/release-state.json'),case.raw_state)
            finally:case.doCleanups()

    def test_parent_race_and_uncertain_patch_preserve_original_and_never_retry(self):
        digest=self.prepare(); original_patch=self.authority.patch
        def uncertain(path,body):
            original_patch(path,body)
            raise RuntimeError('inert lost PATCH response')
        with patch.object(self.authority,'patch',side_effect=uncertain):
            self.assertEqual(self.invoke('--install-hosted-release-suspension',digest),1)
        report=json.loads(self.result_path.read_bytes())
        self.assertEqual(report['disposition'],'install-only-failed-or-unknown')
        self.assertEqual(report['attemptedRequests'][-1]['outcome'],'unknown')
        self.assertIn('intendedRefUpdate',report['attemptedRequests'][-1])
        retained=self.result_path.read_bytes(); writes=list(self.writes())
        self.assertEqual(self.invoke('--install-hosted-release-suspension',digest),1)
        self.assertEqual(self.result_path.read_bytes(),retained);self.assertEqual(self.writes(),writes)

    def test_actual_workflow_jobs_are_structurally_exclusive_and_install_has_no_publication_reachability(self):
        workflow=(ROOT/'.github/workflows/release-successor-publish.yml').read_text()
        publish,install=workflow.split('\n  install-suspension:\n')
        self.assertIn('inputs.install_release_suspension != true',publish)
        self.assertIn('if: inputs.install_release_suspension == true',install)
        self.assertIn('permissions:\n      contents: read\n      actions: read',install)
        self.assertLess(install.index('--check-workflow-inputs'),install.index('--prepare-hosted-release-suspension'))
        self.assertLess(install.index('--prepare-hosted-release-suspension'),install.index('actions/create-github-app-token@'))
        self.assertLess(install.index('actions/create-github-app-token@'),install.index('--install-hosted-release-suspension'))
        self.assertLess(publish.index('--check-workflow-inputs'),publish.index('actions/create-github-app-token@'))
        self.assertLess(publish.index('--check-workflow-inputs'),publish.index('NuGet/login@'))
        for forbidden in ('NuGet/login','id-token:','packages:','setup-dotnet','candidate_run_id:','execute_publisher',
                          '--publish','--preflight-only','--suspension-file','skip-token-revoke'):
            with self.subTest(forbidden=forbidden):self.assertNotIn(forbidden,install)
        self.assertIn('repositories: FS.GG.Coordination.Authority',install)
        self.assertIn('permission-contents: write',install)
        self.assertIn('cancel-in-progress: false',publish)


class HostedInstallMetadataHTTPControls(HostedSuspensionInstallControls):
    """Same maintained CLI/control cases through actual metadata acquisition code.

    Only opener.open's external I/O is replaced; journal/protection/native admission,
    JSON decoding, stream caps, shared request charging and both CLI stages are real.
    """
    def setUp(self):
        super().setUp()
        self.http_calls=[];self.http_reads=[];self.http_streams=[];self.budgets=[];self.http_failure=None;self.exhaust_after_post=False

    def invoke(self,flag,digest=None):
        import release_successor_provider as module
        real_api=module.GitHubAPI;parent=self
        argv=['publisher',*flag] if isinstance(flag,list) else ['publisher',flag]+(['--prepared-sha256',digest] if digest is not None else [])
        def api(token,**kwargs):
            self.tokens.append(token);self.budgets.append(kwargs['metadata_budget'])
            return real_api(token,**kwargs)
        def local(command,**kwargs):
            self.assertIn(command,(['git','rev-parse','HEAD'],['git','diff','--quiet','HEAD'],
                                  ['git','status','--porcelain=v1','--untracked-files=all','--','scripts']))
            return types.SimpleNamespace(stdout='b'*40+'\n' if command[1]=='rev-parse' else self.status if command[1]=='status' else '',returncode=0)
        class Response(io.BytesIO):
            status=200
            def read(self,size=-1):
                parent.assertGreater(size,0);parent.assertLessEqual(size,65536);parent.http_reads.append(size)
                return super().read(size)
            def close(self):
                self.close_attempts+=1
                super().close()
                if self.close_mode=='cleanup':raise OSError('inert close failed')
        def response(raw,headers,mode=None):
            row=Response(raw);row.headers=headers;row.close_mode=mode;row.close_attempts=0
            parent.http_streams.append(row);return row
        def build(*handlers):
            parent.assertTrue(any(isinstance(handler,module._DenyMetadataRedirect) for handler in handlers))
            class Opener:
                def open(self,request,**kwargs):
                    parent.assertEqual(kwargs,{'timeout':60})
                    method=request.get_method();path=request.full_url.removeprefix('https://api.github.com/')
                    token=request.get_header('Authorization');parent.http_calls.append((method,path,token))
                    target=parent.native if token=='Bearer inert-reader' else parent.authority
                    if path=='user':value={'login':'EHotwagner','type':'User'}
                    else:
                        try:value=getattr(target,method.lower())(path,*([json.loads(request.data)] if request.data is not None else []))
                        except NotFound:
                            raise urllib.error.HTTPError(request.full_url,404,'inert',{},response(b'{"message":"Not Found"}',{}))
                    raw=canonical(value);headers={'Content-Length':str(len(raw))}
                    mode=parent.http_failure(method,path) if parent.http_failure else None
                    if mode in {'oversize','oversize-cleanup'}:raw=b'x'*(module.INSTALL_METADATA_RESPONSE_LIMIT+1);headers={}
                    elif mode=='partial':headers={'Content-Length':str(len(raw)+1)}
                    elif mode=='malformed':raw=b'{broken';headers={'Content-Length':str(len(raw))}
                    elif mode in {'error','error-cleanup'}:
                        raise urllib.error.HTTPError(request.full_url,503,'inert',{},response(b'x'*4096,{},'cleanup' if mode=='error-cleanup' else None))
                    if parent.exhaust_after_post and method=='POST':
                        budget=parent.budgets[-1];budget.attempted['GET']=256-budget.attempted['POST']-budget.attempted['PATCH']
                    return response(raw,headers,'cleanup' if mode in {'cleanup','oversize-cleanup'} else None)
            return Opener()
        environment={**self.environment,'FSGG_RELEASE_INPUTS':json.dumps(self.inputs)}
        with patch.object(sys,'argv',argv),patch.dict(os.environ,environment,clear=True), \
             patch.object(publisher,'GitHubAPI',side_effect=api),patch.object(publisher.subprocess,'run',side_effect=local), \
             patch.object(module.urllib.request,'build_opener',side_effect=build),patch.object(sys,'stderr',io.StringIO()) as errors:
            code=publisher.main();self.last_error=errors.getvalue()
            self.assertTrue(all(stream.closed and stream.close_attempts>=1 for stream in self.http_streams))
            return code

    def test_actual_standalone_user_route_uses_same_bounded_reader_writer_and_preserves_original(self):
        record=self.root/'standalone-record.json';record.write_bytes(self.raw)
        argv=['--install-release-suspension','--suspension-file',str(record),
              '--expected-authority-main',self.authority.head,'--publisher-source-sha','b'*40]
        self.assertEqual(self.invoke(argv),0,self.last_error)
        self.assertIs(self.budgets[0],self.budgets[1])
        self.assertEqual(sum(self.budgets[0].attempted.values()),len(self.http_calls))
        self.assertEqual(self.budgets[0].attempted['POST'],3);self.assertEqual(self.budgets[0].attempted['PATCH'],1)
        self.assertEqual(self.reader().file(self.authority.head,'state/releases/utel-rel-21/release-state.json'),self.raw_state)
        print(json.dumps({'standaloneInstallMetadataHTTP':self.budgets[0].report(),'actualTotalOpens':len(self.http_calls)},sort_keys=True))

    def test_actual_stage_counts_are_shared_and_composed_without_cross_process_counter_claim(self):
        digest=self.prepare();prepare_calls=len(self.http_calls)
        self.assertEqual(self.invoke('--install-hosted-release-suspension',digest),0,self.last_error)
        result=json.loads(self.result_path.read_bytes())
        prep=result['preparationMetadataAcquisition'];apply=result['metadataAcquisition']
        self.assertIsNot(self.budgets[0],self.budgets[1]);self.assertIs(self.budgets[1],self.budgets[2])
        self.assertEqual(sum(prep['attempted'].values()),prepare_calls)
        self.assertEqual(sum(apply['attempted'].values()),len(self.http_calls)-prepare_calls)
        self.assertEqual(apply['attempted']['POST'],3);self.assertEqual(apply['attempted']['PATCH'],1)
        self.assertLessEqual(sum(prep['attempted'].values()),256);self.assertLessEqual(sum(apply['attempted'].values()),256)
        print(json.dumps({'installMetadataHTTP':{'prepare':prep,'apply':apply,'actualTotalOpens':len(self.http_calls),
            'conservativeTwoStageCeiling':512,'counterInstances':2,'nativeQualification':False}},sort_keys=True))

    def test_actual_preissuer_transport_oversize_partial_error_refuses_without_writer(self):
        for mode in ('oversize','partial','error'):
            with self.subTest(mode=mode):
                self.http_failure=lambda method,path:mode
                self.assertEqual(self.invoke('--prepare-hosted-release-suspension'),1)
                self.assertEqual(self.writes(),[]);self.assertNotIn('inert-writer',self.tokens)
                self.assertFalse(self.prepared_path.exists())
                self.result_path.unlink(missing_ok=True)

    def test_actual_post_and_patch_bad_acquisition_preserve_unknown_and_stop_all_later_writes(self):
        # Each selected operation is isolated; never retry an uncertain mutation.
        for method in ('POST','PATCH'):
            for mode in ('oversize','partial','malformed','error','cleanup','error-cleanup','oversize-cleanup'):
                with self.subTest(method=method,mode=mode):
                    case=type(self)('test_actual_stage_counts_are_shared_and_composed_without_cross_process_counter_claim')
                    case.setUp()
                    try:
                        digest=case.prepare();case.http_failure=lambda actual,path:mode if actual==method else None
                        self.assertEqual(case.invoke('--install-hosted-release-suspension',digest),1,case.last_error)
                        rows=json.loads(case.result_path.read_bytes());attempts=rows['attemptedRequests']
                        self.assertEqual(len(attempts),1 if method=='POST' else 4)
                        self.assertEqual(attempts[-1]['outcome'],'unknown');self.assertFalse(rows['publicationComplete'])
                        self.assertEqual(rows['metadataAcquisition']['streamCloseFailures'],1 if 'cleanup' in mode else 0)
                        cause=rows['failure']['chain'][0]['message']
                        if mode=='error-cleanup':self.assertIn('returned HTTP 503',cause)
                        if mode=='oversize-cleanup':self.assertIn('exceeds acquisition bound',cause)
                        before=list(case.http_calls);retained=case.result_path.read_bytes()
                        self.assertEqual(case.invoke('--install-hosted-release-suspension',digest),1)
                        self.assertEqual(case.result_path.read_bytes(),retained)
                        self.assertFalse(any(row[0] in {'POST','PATCH'} for row in case.http_calls[len(before):]))
                    finally:case.doCleanups()

    def test_actual_request_exhaustion_after_first_post_forbids_next_guard_and_write(self):
        digest=self.prepare();self.exhaust_after_post=True
        self.assertEqual(self.invoke('--install-hosted-release-suspension',digest),1,self.last_error)
        rows=json.loads(self.result_path.read_bytes())
        self.assertEqual(len(rows['attemptedRequests']),1);self.assertEqual(rows['attemptedRequests'][0]['outcome'],'response-received')
        self.assertEqual(sum(rows['metadataAcquisition']['attempted'].values()),256)
        self.assertEqual(len(self.writes()),1)


if __name__ == "__main__":
    unittest.main()
