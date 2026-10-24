#!/usr/bin/env python3
"""Controls for the org-wide serialized publication writer (ADR-0097, FS-GG/.github#4753).

Credential-free. A deterministic virtual clock drives real threads through the real
`acquire` over a fake GitHub Actions job list, so waits of minutes run in milliseconds.
Every positive control has a mutant that must be caught.
"""

from __future__ import annotations

import copy
import importlib.util
import json
import pathlib
import re
import sys
import threading
from datetime import datetime, timezone

sys.dont_write_bytecode = True
ROOT = pathlib.Path(__file__).resolve().parents[2]


def load(name: str, path: str):
    spec = importlib.util.spec_from_file_location(name, ROOT / path)
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


lock = load("publication_writer_lock", "scripts/publication_writer_lock.py")
coverage = load("check_publication_writer_coverage", "scripts/check-publication-writer-coverage.py")
REGISTRY = json.loads((ROOT / "registry/publication-writers.json").read_text())
BASE = 1_800_000_000


class VirtualClock:
    """Threads sleep in virtual seconds; time jumps when every live thread is asleep."""

    def __init__(self, parties: int):
        self.now, self.active, self.sleepers = float(BASE), parties, []
        self.cond = threading.Condition()

    def time(self) -> float:
        with self.cond:
            return self.now

    def _advance(self) -> None:
        if self.active == 0 and self.sleepers:
            self.now = max(self.now, min(self.sleepers))
            woken = [w for w in self.sleepers if w <= self.now]
            self.active += len(woken)
            self.sleepers = [w for w in self.sleepers if w > self.now]
            self.cond.notify_all()

    def sleep(self, seconds: float) -> None:
        if seconds <= 0:
            return
        with self.cond:
            wake = self.now + seconds
            self.sleepers.append(wake)
            self.active -= 1
            self._advance()
            while self.now < wake:
                self.cond.wait()

    def done(self) -> None:
        with self.cond:
            self.active -= 1
            self._advance()


def stamp(value: float) -> str:
    return datetime.fromtimestamp(int(value), timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


class FakeGitHub:
    """The slice of the Actions REST surface the lock reads."""

    def __init__(self, clock: VirtualClock, registry=REGISTRY):
        self.clock, self.registry = clock, registry
        self.jobs = {}  # id -> dict
        self.guard = threading.Lock()
        self.fail_scans = 0
        self.always_fail = False
        self.reads = 0

    def start_job(self, repo, run, job, workflow=None, gate=True,
                  runner=None, at=None, status="in_progress"):
        workflow = workflow or WORKFLOW_OF.get(repo, ".github/workflows/release.yml")
        started = stamp(self.clock.time() if at is None else at)
        with self.guard:
            self.jobs[job] = {"repo": repo, "run": run, "id": job, "status": status, "workflow": workflow,
                              "runner_name": runner or f"runner-{job}", "steps": [],  # the live API often returns no steps
                              "name": f"publish {lock.MARKER}" if gate else "package-tests",
                              "started_at": started}

    def finish(self, job):
        with self.guard:
            self.jobs[job]["status"] = "completed"

    def get(self, path, raw=False):
        with self.guard:
            self.reads += 1
            if path.startswith("/repos/FS-GG/.github/contents/"):
                return copy.deepcopy(self.registry)
            runs = re.fullmatch(r"/repos/([^/]+/[^/]+)/actions/runs\?status=in_progress&per_page=100&page=(\d+)", path)
            if runs:
                if self.always_fail or self.fail_scans > 0:
                    self.fail_scans = max(0, self.fail_scans - 1)
                    raise lock.LockError("simulated read failure")
                repo = runs.group(1)
                seen, items = set(), []
                for job in self.jobs.values():
                    if job["repo"] == repo and job["status"] == "in_progress" and (repo, job["run"]) not in seen:
                        seen.add((repo, job["run"]))
                        items.append({"id": job["run"], "path": job["workflow"] + "@refs/heads/main"})
                return {"workflow_runs": items if runs.group(2) == "1" else []}
            listing = re.fullmatch(r"/repos/([^/]+/[^/]+)/actions/runs/(\d+)/jobs\?filter=latest&per_page=100", path)
            if listing:
                repo, run = listing.group(1), int(listing.group(2))
                return {"jobs": [copy.deepcopy(j) for j in self.jobs.values() if j["repo"] == repo and j["run"] == run]}
        raise AssertionError("unexpected read " + path)


FAST = dict(settle=2.0, confirm=1.0, poll_first=2.0, poll_max=4.0)


class Outcome:
    def __init__(self):
        self.entries, self.inside, self.max_inside, self.errors = [], 0, 0, {}
        self.guard = threading.Lock()


WORKFLOW_OF = {row["repo"]: row["workflows"][-1] for row in REGISTRY["writers"]}


def publisher(api, clock, out, name, repo, run, job, start, work, *, crash_after=None, workflow=None,
              max_wait=3600.0, **overrides):
    """One publishing job: start, take the lock, 'publish' for `work` virtual seconds, end."""
    workflow = workflow or WORKFLOW_OF.get(repo, ".github/workflows/release.yml")

    def body():
        try:
            clock.sleep(start)
            api.start_job(repo, run, job, workflow=workflow)
            try:
                lock.acquire(api, repo=repo, run_id=run, runner_name=f"runner-{job}", max_wait=max_wait,
                             clock=clock.time, sleep=clock.sleep, log=lambda *_: None, **{**FAST, **overrides})
            except lock.LockError as error:
                out.errors[name] = str(error)
                return
            with out.guard:
                out.inside += 1
                out.max_inside = max(out.max_inside, out.inside)
                out.entries.append((name, clock.time()))
            clock.sleep(work if crash_after is None else crash_after)
            with out.guard:
                out.inside -= 1
        finally:
            api.finish(job)  # the job ends, however it ended: this is the whole release protocol
            clock.done()
    thread = threading.Thread(target=body, daemon=True)
    return thread


def run_threads(threads):
    for t in threads:
        t.start()
    for t in threads:
        t.join(timeout=60)
        assert not t.is_alive(), "scenario did not terminate (deadlock)"


def scenario_serialization(label="serialization"):
    clock = VirtualClock(6)
    api, out = FakeGitHub(clock), Outcome()
    spec = [("a", "FS-GG/FS.GG.Rendering", 0), ("b", "FS-GG/FS.GG.SDD", 0), ("c", "FS-GG/.github", 1),
            ("d", "FS-GG/FS.GG.Game", 3), ("e", "FS-GG/FS.GG.Net", 3), ("f", "FS-GG/FsQuint", 10)]
    threads = [publisher(api, clock, out, n, repo, 1000 + i, 5000 + i, start, 120)
               for i, (n, repo, start) in enumerate(spec)]
    run_threads(threads)
    return out, spec


out, _ = scenario_serialization()
assert not out.errors, out.errors
assert len(out.entries) == 6 and out.max_inside == 1, f"two publishers overlapped: {out.entries}"
assert [n for n, _ in out.entries] == ["a", "b", "c", "d", "e", "f"], "tickets (start second, job id) must order the entries"
times = [t for _, t in out.entries]
assert all(b - a >= 120 for a, b in zip(times, times[1:])), "entries closer than one critical section"
print("serialization: 6 publishers across 6 repositories entered one at a time, oldest ticket first")

# Mutants: if the comparison, the repo coverage, or the ghost rule is wrong, the same scenario overlaps.
real_blockers, real_scan = lock.blockers, lock.scan
lock.blockers = lambda me, participants: []
mutant, _ = scenario_serialization()
lock.blockers = real_blockers
assert mutant.max_inside > 1, "control: a lock that never blocks must be caught by the serialization scenario"

def blind_scan(api, registry, now, lease=lock.LEASE_SECONDS):
    narrowed = {**registry, "writers": [row for row in registry["writers"] if row["repo"] == "FS-GG/.github"]}
    return real_scan(api, narrowed, now, lease)
lock.scan = blind_scan
mutant, _ = scenario_serialization()
lock.scan = real_scan
assert mutant.max_inside > 1 or mutant.errors, "control: a lock blind to other repositories must be caught"

lock.scan = lambda api, registry, now, lease=lock.LEASE_SECONDS: real_scan(api, registry, now, 0)
mutant, _ = scenario_serialization()
lock.scan = real_scan
assert mutant.max_inside > 1, "control: treating every live holder as a ghost must be caught"
print("serialization mutants: never-blocks, repo-blind and everyone-is-a-ghost all produce overlap")

# Crashed holder: the runner dies mid-publication, never reaching any release step.
clock = VirtualClock(2)
api, out = FakeGitHub(clock), Outcome()
run_threads([
    publisher(api, clock, out, "holder", "FS-GG/FS.GG.SDD", 1, 11, 0, 10_000, crash_after=40),
    publisher(api, clock, out, "waiter", "FS-GG/FS.GG.Governance", 2, 12, 5, 10, workflow=".github/workflows/publish.yml"),
])
assert not out.errors and [n for n, _ in out.entries] == ["holder", "waiter"], out
assert out.entries[1][1] - out.entries[0][1] < 40 + 3 * 4, "waiter must take over within one poll of the holder's death"
print("crash: a holder that dies without releasing is replaced within one poll interval")

# A job that is still 'in_progress' beyond the lease is a ghost; a young one is not.
clock = VirtualClock(1)
api, out = FakeGitHub(clock), Outcome()
api.start_job("FS-GG/FS.GG.SDD", 1, 11, at=BASE - lock.LEASE_SECONDS - 60)
run_threads([publisher(api, clock, out, "waiter", "FS-GG/FS.GG.Net", 2, 12, 0, 1)])
assert not out.errors and out.max_inside == 1, "a ghost older than the lease must not deadlock the org"
clock = VirtualClock(1)
api, out = FakeGitHub(clock), Outcome()
api.start_job("FS-GG/FS.GG.SDD", 1, 11, at=BASE - 600)
run_threads([publisher(api, clock, out, "waiter", "FS-GG/FS.GG.Net", 2, 12, 0, 1, max_wait=300)])
assert "not acquired" in out.errors.get("waiter", "") and not out.entries, "a live young holder must block, then time out closed"
print("lease: a 7h+ ghost is ignored; a live 10 minute holder blocks and the waiter fails closed at max-wait")

# Read failures never read as 'clear'.
clock = VirtualClock(1)
api, out = FakeGitHub(clock), Outcome()
api.always_fail = True
run_threads([publisher(api, clock, out, "w", "FS-GG/FS.GG.Net", 2, 12, 0, 1)])
assert "read failure" in out.errors.get("w", "") or "simulated" in out.errors.get("w", ""), out.errors
assert not out.entries, "persistent listing failure must not acquire"
clock = VirtualClock(1)
api, out = FakeGitHub(clock), Outcome()
api.fail_scans = 2
run_threads([publisher(api, clock, out, "w", "FS-GG/FS.GG.Net", 2, 12, 0, 1)])
assert not out.errors and len(out.entries) == 1, "transient failures are retried, then acquire"
print("fail-closed: persistent read failure never acquires; two transient failures are retried")

# Identity refusals: not registered, no gate step on this job, no such job, ambiguous runner.
clock = VirtualClock(1)
api = FakeGitHub(clock)
for case in ("unregistered", "no-gate", "no-job", "ambiguous"):
    api.jobs.clear()
    repo = "FS-GG/FS.GG.Net"
    if case == "unregistered":
        repo = "FS-GG/RecoilEngine"
        api.start_job(repo, 2, 12)
    elif case == "no-gate":
        api.start_job(repo, 2, 12, gate=False)
    elif case == "ambiguous":
        api.start_job(repo, 2, 12, runner="shared")
        api.start_job(repo, 2, 13, runner="shared")
    try:
        lock.acquire(api, repo=repo, run_id=2, runner_name="shared" if case == "ambiguous" else "runner-12",
                     max_wait=60, clock=clock.time, sleep=lambda s: None, log=lambda *_: None, **FAST)
    except lock.LockError:
        continue
    raise AssertionError("identity refusal accepted: " + case)
print("identity: unregistered repository, missing gate step, missing job and ambiguous runner all refuse")

# F1: the live Actions API frequently returns NO step list. Identification is by job name, never steps.
clock = VirtualClock(1)
api, out = FakeGitHub(clock), Outcome()
api.start_job("FS-GG/FS.GG.SDD", 1, 11, at=BASE - 120)  # older marked job, steps: [] (see FakeGitHub.start_job)
assert api.jobs[11]["steps"] == []
run_threads([publisher(api, clock, out, "waiter", "FS-GG/FS.GG.Net", 2, 12, 0, 1, max_wait=300)])
assert "not acquired" in out.errors.get("waiter", ""), "an older marked job with an empty step list must still block"
clock = VirtualClock(1)
api, out = FakeGitHub(clock), Outcome()
api.start_job("FS-GG/FS.GG.SDD", 1, 11, at=BASE - 120, gate=False)  # a job without the marker never blocks
run_threads([publisher(api, clock, out, "w", "FS-GG/FS.GG.Net", 2, 12, 0, 1)])
assert not out.errors, out.errors
print("lag: an older marked job with no step list blocks; an unmarked one does not")

# F6: the ticket is the server-side job start only; a job without started_at cannot be ticketed.
try:
    lock.ticket_of({"id": 5, "started_at": None, "steps": [{"started_at": "2026-10-24T00:00:00Z"}]})
except lock.LockError:
    pass
else:
    raise AssertionError("a job with no started_at must not be ticketed from a step clock")

# F2: transient HTTP failures are retried; a client error is not.
import io, urllib.error, urllib.request
class Resp(io.BytesIO):
    def __enter__(self): return self
    def __exit__(self, *a): return False
calls = {"n": 0}
def flaky(request, timeout=0):
    calls["n"] += 1
    if calls["n"] < 3:
        raise urllib.error.HTTPError(request.full_url, 502, "bad gateway", {}, None)
    return Resp(b'{"ok": true}')
real_urlopen, real_sleep = urllib.request.urlopen, lock.time.sleep
urllib.request.urlopen, lock.time.sleep = flaky, lambda s: None
try:
    assert lock.GitHubApi("t").get("/x") == {"ok": True} and calls["n"] == 3, "two 502s are retried"
    def denied(request, timeout=0): raise urllib.error.HTTPError(request.full_url, 404, "nf", {}, None)
    urllib.request.urlopen = denied
    try:
        lock.GitHubApi("t").get("/x")
    except lock.LockError as error:
        assert "404" in str(error)
    else:
        raise AssertionError("a 404 must fail closed immediately")
    urllib.request.urlopen = lambda request, timeout=0: (_ for _ in ()).throw(urllib.error.HTTPError(request.full_url, 503, "x", {}, None))
    try:
        lock.GitHubApi("t").get("/x")
    except lock.LockError as error:
        assert "attempts" in str(error)
    else:
        raise AssertionError("a persistent 503 must fail closed")
finally:
    urllib.request.urlopen, lock.time.sleep = real_urlopen, real_sleep
print("http: 5xx retried with backoff then fail closed; 4xx fails closed at once")

# The wait can never overshoot max-wait.
clock = VirtualClock(1)
api, out = FakeGitHub(clock), Outcome()
api.start_job("FS-GG/FS.GG.SDD", 1, 11, at=BASE - 60)
slept = []
def counting_sleep(seconds):
    slept.append(seconds); clock.sleep(seconds)
t0 = clock.time()
api.start_job("FS-GG/FS.GG.Net", 2, 12, at=BASE)
try:
    lock.acquire(api, repo="FS-GG/FS.GG.Net", run_id=2, runner_name="runner-12", max_wait=100, clock=clock.time, sleep=counting_sleep,
                 log=lambda *_: None, **FAST)
except lock.LockError:
    pass
else:
    raise AssertionError("must time out")
assert clock.time() - t0 <= 100, "the wait overshot max-wait"
print("deadline: the lock wait never overshoots max-wait-minutes")

# Tie-break: identical start second -> lower job id first; non-participants and unregistered workflows never block.
clock = VirtualClock(2)
api, out = FakeGitHub(clock), Outcome()
run_threads([publisher(api, clock, out, "hi", "FS-GG/FS.GG.Net", 2, 22, 0, 30),
             publisher(api, clock, out, "lo", "FS-GG/FS.GG.SDD", 1, 21, 0, 30)])
assert [n for n, _ in out.entries] == ["lo", "hi"] and out.max_inside == 1, out.entries
clock = VirtualClock(1)
api, out = FakeGitHub(clock), Outcome()
api.start_job("FS-GG/FS.GG.SDD", 1, 11, at=BASE - 300, gate=False)  # a build job in a release workflow
api.start_job("FS-GG/FS.GG.SDD", 3, 13, at=BASE - 300, workflow=".github/workflows/gate.yml")  # unregistered workflow
run_threads([publisher(api, clock, out, "w", "FS-GG/FS.GG.Net", 2, 12, 0, 1)])
assert not out.errors, "jobs without the lock step, or in unregistered workflows, must not block"
print("tie-break and scope: equal tickets order by job id; non-participant jobs do not block")

# Registry validation is closed.
good = copy.deepcopy(REGISTRY)
lock.validate_registry(good)
for label, mutate in {
    "schema": lambda d: d.update(schema=2),
    "gate-name": lambda d: d.update(gateStep="other"),
    "empty": lambda d: d.update(writers=[]),
    "bad-repo": lambda d: d["writers"][0].update(repo="evil/repo"),
    "dup-repo": lambda d: d["writers"].append(copy.deepcopy(d["writers"][0])),
    "extra-key": lambda d: d["writers"][0].update(extra=1),
    "bad-path": lambda d: d["writers"][0].update(workflows=["../x.yml"]),
    "dup-path": lambda d: d["writers"][0].update(workflows=[d["writers"][0]["workflows"][0]] * 2),
    "no-flows": lambda d: d["writers"][0].update(workflows=[]),
}.items():
    bad = copy.deepcopy(good)
    mutate(bad)
    try:
        lock.validate_registry(bad)
    except lock.LockError:
        continue
    raise AssertionError("registry defect accepted: " + label)
print("registry: nine closed-schema defects refused")

# Bounded growth and deadlock freedom under a pile-up: 12 jobs, all finish, strictly one at a time.
clock = VirtualClock(12)
api, out = FakeGitHub(clock), Outcome()
repos = [row["repo"] for row in REGISTRY["writers"]]
run_threads([publisher(api, clock, out, f"j{i}", repos[i % len(repos)], 100 + i, 900 + i, i % 4, 45) for i in range(12)])
assert not out.errors and len(out.entries) == 12 and out.max_inside == 1, (out.errors, out.max_inside)
print("pile-up: 12 simultaneous publishers all complete, one at a time, no deadlock")

# --- static coverage checker -------------------------------------------------------------------
PINNED = "FS-GG/.github/.github/actions/publication-writer-lock@" + "a" * 40


def workflow(gate="      - name: Acquire org publication writer lock\n        uses: " + PINNED + "\n",
             before="", after="      - name: Push\n        run: dotnet nuget push x.nupkg\n"):
    return ("name: release\non:\n  workflow_dispatch:\njobs:\n  publish:\n    name: publish " + lock.MARKER + "\n    runs-on: ubuntu-latest\n    steps:\n"
            "      - uses: actions/checkout@v7\n" + before + gate + after)


assert coverage.check_workflow(workflow(), "FS-GG/FS.GG.SDD") == {"gated": ["publish"], "problems": []}
assert coverage.check_workflow(workflow(gate="      - name: Acquire org publication writer lock\n        uses: ./.github/actions/publication-writer-lock\n"),
                               "FS-GG/.github")["problems"] == []
expected_bad = {
    "missing": workflow(gate=""),
    "after-effect": workflow(gate="", after="      - name: Push\n        run: dotnet nuget push x.nupkg\n"
                             "      - name: Acquire org publication writer lock\n        uses: " + PINNED + "\n"),
    "conditional": workflow(gate="      - name: Acquire org publication writer lock\n        if: inputs.publish\n        uses: " + PINNED + "\n"),
    "continue": workflow(gate="      - name: Acquire org publication writer lock\n        continue-on-error: true\n        uses: " + PINNED + "\n"),
    "unpinned": workflow(gate="      - name: Acquire org publication writer lock\n        uses: FS-GG/.github/.github/actions/publication-writer-lock@main\n"),
    "wrong-name": workflow(gate="      - name: Lock\n        uses: " + PINNED + "\n"),
    "local-in-producer": workflow(gate="      - name: Acquire org publication writer lock\n        uses: ./.github/actions/publication-writer-lock\n"),
    "duplicate": workflow(gate=("      - name: Acquire org publication writer lock\n        uses: " + PINNED + "\n") * 2),
    "login-first": workflow(before="      - uses: NuGet/login@v1\n"),
    "app-token-first": workflow(before="      - uses: actions/create-github-app-token@bcd2ba49218906704ab6c1aa796996da409d3eb1\n"),
    "setup-step-first": workflow(before="      - name: Build\n        run: dotnet build\n"),
    "unmarked-name": workflow().replace(" " + lock.MARKER, ""),
    "git-push-after-gate-missing": workflow(gate="", after="      - run: git push origin HEAD:refs/heads/x\n"),
}
for label, text in expected_bad.items():
    assert coverage.check_workflow(text, "FS-GG/FS.GG.SDD")["problems"], "coverage defect accepted: " + label
assert coverage.check_workflow(workflow(gate="", after="      - run: echo ok\n"), "FS-GG/FS.GG.SDD")["problems"] == []
assert coverage.check_workflow(workflow(gate="", after="      # dotnet nuget push is mentioned in a comment only\n"), "FS-GG/FS.GG.SDD")["problems"] == []
print(f"coverage checker: valid shapes pass; {len(expected_bad)} unsafe placements refused; comments are not effects")
assert coverage.audit({".github/workflows/x.yml": workflow(gate="", after="      - run: git tag -a v1 -m x && git push origin refs/tags/v1\n")},
                      "FS-GG/FS.GG.SDD", {"writers": [{"repo": "FS-GG/FS.GG.SDD", "workflows": [".github/workflows/other.yml"]}], "exempt": []}), "a tag cutter must be registered or exempt"

# Permission-based write capability needs the gate unless the job is declared read-only by contract.
def perm_workflow(job_perm="", wf_perm="permissions:\n  contents: write\n", extra_job="", gate=""):
    return ("name: x\non:\n  workflow_dispatch:\n" + wf_perm + "jobs:\n  writer:\n    name: writer " + lock.MARKER + "\n    runs-on: ubuntu-latest\n" + job_perm +
            "    steps:\n      - uses: actions/checkout@v7\n" + gate + "      - run: python3 publish.py\n" + extra_job)

assert coverage.check_workflow(perm_workflow(), "FS-GG/FS.GG.SDD")["problems"], "inherited contents: write job needs the gate"
assert coverage.check_workflow(perm_workflow(job_perm="    permissions:\n      packages: write\n", wf_perm="permissions:\n  contents: read\n"), "FS-GG/FS.GG.SDD")["problems"]
assert coverage.check_workflow(perm_workflow(job_perm="    permissions:\n      contents: read\n"), "FS-GG/FS.GG.SDD")["problems"] == []
assert coverage.check_workflow(perm_workflow(wf_perm="permissions:\n  contents: read\n"), "FS-GG/FS.GG.SDD")["problems"] == []
assert coverage.check_workflow(perm_workflow(), "FS-GG/FS.GG.SDD", observers=("writer",))["problems"] == []
assert coverage.check_workflow(perm_workflow(gate="      - name: Acquire org publication writer lock\n        uses: " + PINNED + "\n"),
                               "FS-GG/FS.GG.SDD") == {"gated": ["writer"], "problems": []}
assert coverage.check_workflow(perm_workflow(gate="      - name: Acquire org publication writer lock\n        uses: " + PINNED + "\n"),
                               "FS-GG/FS.GG.SDD", observers=("writer",))["problems"], "an observer may not take the lock"
caller = "name: x\non:\n  workflow_dispatch:\npermissions:\n  contents: write\njobs:\n  call:\n    uses: ./.github/workflows/release.yml\n"
assert coverage.check_workflow(caller, "FS-GG/FS.GG.SDD")["problems"] == [], "a reusable-workflow caller carries no steps"
bad_observers = copy.deepcopy(REGISTRY)
bad_observers["writers"][0]["observationJobs"] = {".github/workflows/not-listed.yml": ["x"]}
try:
    lock.validate_registry(bad_observers)
except lock.LockError:
    pass
else:
    raise AssertionError("observationJobs for an unlisted workflow accepted")
print("coverage checker: write permission (inherited or own) needs the gate; observers and callers are explicit")

registry = copy.deepcopy(REGISTRY)
gated_text = workflow()
files = {".github/workflows/release.yml": gated_text}
sdd = {"writers": [{"repo": "FS-GG/FS.GG.SDD", "workflows": [".github/workflows/release.yml"]}], "exempt": []}
assert coverage.audit(files, "FS-GG/FS.GG.SDD", sdd) == []
assert coverage.audit({".github/workflows/release.yml": workflow(gate="")}, "FS-GG/FS.GG.SDD", sdd), "registered but ungated"
assert coverage.audit({**files, ".github/workflows/other.yml": workflow(gate="")}, "FS-GG/FS.GG.SDD", sdd), "unregistered publisher"
assert coverage.audit({}, "FS-GG/FS.GG.SDD", sdd), "registered workflow missing"
exempt = {**sdd, "exempt": [{"repo": "FS-GG/FS.GG.SDD", "workflows": [".github/workflows/old.yml"], "reason": "x"}]}
assert coverage.audit({**files, ".github/workflows/old.yml": workflow(gate="")}, "FS-GG/FS.GG.SDD", exempt) == []
print("coverage audit: ungated, unregistered, missing and exempt cases behave")

# The real checkout: every .github publishing workflow is either gated and registered, or exempt.
local = {f".github/workflows/{p.name}": p.read_text() for p in sorted((ROOT / ".github/workflows").glob("*.y*ml"))}
problems = coverage.audit(local, "FS-GG/.github", REGISTRY)
assert problems == [], problems
print("this repository: all registered publisher workflows are gated; every other effect workflow is exempt with a reason")

# --- timeout ceiling: the lock wait is queueing, so it raises the per-job ceiling by exactly itself -----------
try:
    import yaml  # noqa: F401
except ImportError:
    print("timeout ceiling: PyYAML absent, skipped locally (the selftest workflow installs it)")
else:
    import subprocess, tempfile
    def timeouts(timeout, wait):
        with tempfile.TemporaryDirectory() as root:
            workflows = pathlib.Path(root, ".github/workflows")
            workflows.mkdir(parents=True)
            gate = "" if wait is None else (f"      - name: {lock.GATE_STEP}\n        uses: ./.github/actions/publication-writer-lock\n"
                                           f"        with:\n          max-wait-minutes: {wait}\n")
            workflows.joinpath("w.yml").write_text(f"name: w\non: workflow_dispatch\njobs:\n  j:\n    runs-on: ubuntu-latest\n    timeout-minutes: {timeout}\n    steps:\n{gate}      - run: echo ok\n")
            return subprocess.run([sys.executable, "-B", str(ROOT / "scripts/check-workflow-timeouts.py"), "--root", root],
                                  capture_output=True, text=True).returncode
    assert timeouts(60, None) == 0 and timeouts(61, None) == 1, "the unlocked ceiling stays 60"
    assert timeouts(90, 30) == 0 and timeouts(91, 30) == 1, "the ceiling rises by exactly the declared wait"
    assert timeouts(90, 0) == 1 and timeouts(90, 999) == 1, "an unbounded or absent wait raises nothing"
    print("timeout ceiling: 60 unlocked; 60 + declared max-wait with the lock step; nothing for a bogus wait")
