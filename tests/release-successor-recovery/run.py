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
import urllib.request
import unittest
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


if __name__ == "__main__":
    unittest.main()
