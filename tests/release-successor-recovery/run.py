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
from release_successor_journal import ProtectedReleaseJournal, REF, REPOSITORY, canonical
from release_successor_provider import NotFound

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


if __name__ == "__main__":
    unittest.main()
