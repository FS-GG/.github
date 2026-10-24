#!/usr/bin/env python3
"""Org-wide serialized publication writer (FS-GG/.github#4753, ADR-0097).

GitHub Actions `concurrency` groups are scoped to ONE repository, so a group can never
serialize publishers that live in different repositories. This lock therefore uses the one
registry every repository shares and every workflow can already read without a new
credential: the public Actions job list.

Protocol (a bakery lock over live jobs):

* A *participant* is a job that contains a step named exactly GATE_STEP, in a workflow
  registered in `registry/publication-writers.json` (read live from `FS-GG/.github` main).
* A participant's *ticket* is the moment its runner started it, tie-broken by job id. The
  ticket is fixed by GitHub before any step runs, so it cannot be renewed or reordered.
* A participant may proceed only when no OTHER live participant (status `in_progress`)
  holds a strictly smaller ticket. It waits otherwise. Waiting is only ever on strictly
  older jobs, so the wait graph is acyclic: the oldest live participant always proceeds.
* The lock IS the job. There is no release step: when the job ends for any reason
  (success, failure, cancellation, runner loss, GitHub's own timeout) it stops being
  `in_progress` and the next ticket proceeds. A crashed holder cannot deadlock the org.
* Bounded recovery: a job still listed `in_progress` after LEASE_SECONDS (longer than
  GitHub's hard hosted-job limit) is a ghost and is ignored; waiters give up after
  `--max-wait-minutes` and fail closed.
* Fail closed: an unreadable registry, an unreadable repository listing, a malformed job
  record, or a caller whose own job cannot be found and ticketed never acquires.

The lock fences concurrent *writers*. It grants no authority: candidate, journal CAS,
revocation, protection and every other publisher guard are unchanged and still decide
whether an effect may happen.
"""

from __future__ import annotations

import argparse
import json
import os
import re
import sys
import time
import urllib.error
import urllib.request
from datetime import datetime, timezone

sys.dont_write_bytecode = True

GATE_STEP = "Acquire org publication writer lock"
REGISTRY_REPOSITORY = "FS-GG/.github"
REGISTRY_PATH = "registry/publication-writers.json"
API = "https://api.github.com"
REPO_RE = re.compile(r"^FS-GG/[A-Za-z0-9_.-]+$")
WORKFLOW_RE = re.compile(r"^\.github/workflows/[A-Za-z0-9_.-]+\.ya?ml$")
LEASE_SECONDS = 7 * 3600  # above GitHub's 6 h hosted-job hard limit
SETTLE_SECONDS = 20.0  # absorb job-listing propagation delay before the first decision
CONFIRM_SECONDS = 10.0  # a second clear scan this much later must also be clear
POLL_FIRST_SECONDS = 30.0
POLL_MAX_SECONDS = 90.0
MAX_CONSECUTIVE_READ_FAILURES = 5
OWN_JOB_ATTEMPTS = 12
OWN_JOB_RETRY_SECONDS = 5.0


class LockError(Exception):
    """The lock was not acquired. The caller must not perform a publication effect."""


class GitHubApi:
    """Minimal read-only GitHub REST client over a bearer token (urllib, stdlib only)."""

    def __init__(self, token: str, base: str = API, timeout: float = 30.0):
        self.token, self.base, self.timeout = token, base, timeout

    def get(self, path: str, raw: bool = False):
        request = urllib.request.Request(
            self.base + path,
            headers={
                "Accept": "application/vnd.github.raw" if raw else "application/vnd.github+json",
                "Authorization": "Bearer " + self.token,
                "X-GitHub-Api-Version": "2022-11-28",
                "User-Agent": "fsgg-publication-writer-lock",
            },
        )
        try:
            with urllib.request.urlopen(request, timeout=self.timeout) as response:  # noqa: S310 - fixed https base
                body = response.read(8_000_000)
        except (urllib.error.URLError, OSError, TimeoutError) as error:
            raise LockError(f"GitHub read failed for {path}: {error}") from error
        try:
            return json.loads(body)
        except ValueError as error:
            raise LockError(f"GitHub read returned non-JSON for {path}") from error


def parse_time(value) -> float:
    if not isinstance(value, str):
        raise LockError("timestamp is not a string")
    try:
        return datetime.strptime(value, "%Y-%m-%dT%H:%M:%SZ").replace(tzinfo=timezone.utc).timestamp()
    except ValueError as error:
        raise LockError(f"malformed timestamp {value!r}") from error


def validate_registry(document) -> dict:
    """Return the closed registry or refuse. The registry is data from a mutable branch."""
    if not isinstance(document, dict) or document.get("schema") != 1:
        raise LockError("publication writer registry has the wrong schema")
    if document.get("gateStep") != GATE_STEP:
        raise LockError("publication writer registry names a different gate step")
    writers = document.get("writers")
    if not isinstance(writers, list) or not writers:
        raise LockError("publication writer registry lists no writers")
    seen = set()
    for row in writers:
        if not isinstance(row, dict) or set(row) != {"repo", "workflows"}:
            raise LockError("publication writer row is not closed")
        if not isinstance(row["repo"], str) or not REPO_RE.fullmatch(row["repo"]) or row["repo"] in seen:
            raise LockError("publication writer repository is malformed or duplicated")
        seen.add(row["repo"])
        flows = row["workflows"]
        if not isinstance(flows, list) or not flows or len(set(flows)) != len(flows):
            raise LockError("publication writer workflows are empty or duplicated")
        if not all(isinstance(path, str) and WORKFLOW_RE.fullmatch(path) for path in flows):
            raise LockError("publication writer workflow path is malformed")
    return document


def load_registry(api) -> dict:
    text = api.get(f"/repos/{REGISTRY_REPOSITORY}/contents/{REGISTRY_PATH}?ref=main", raw=True)
    return validate_registry(text)


def ticket_of(job: dict):
    """(runner start, job id): fixed by GitHub before any step, so it cannot be renewed."""
    steps = job.get("steps")
    first = None
    if isinstance(steps, list) and steps and isinstance(steps[0], dict):
        first = steps[0].get("started_at")
    started = first if isinstance(first, str) else job.get("started_at")
    job_id = job.get("id")
    if not isinstance(job_id, int) or isinstance(job_id, bool):
        raise LockError("job has no integer id")
    return (parse_time(started), job_id)


def participates(job: dict) -> bool:
    steps = job.get("steps")
    return isinstance(steps, list) and any(isinstance(s, dict) and s.get("name") == GATE_STEP for s in steps)


def scan(api, registry: dict, now: float, lease: float = LEASE_SECONDS) -> list:
    """Every live participant: [{'repo','run','job','ticket','ghost'}]. Any read failure raises."""
    live = []
    for row in registry["writers"]:
        repo, workflows = row["repo"], set(row["workflows"])
        page = 1
        while True:
            runs = api.get(f"/repos/{repo}/actions/runs?status=in_progress&per_page=100&page={page}")
            items = runs.get("workflow_runs") if isinstance(runs, dict) else None
            if not isinstance(items, list):
                raise LockError(f"{repo}: run listing is malformed")
            for run in items:
                path = str(run.get("path", "")).split("@", 1)[0]
                if path not in workflows:
                    continue
                run_id = run.get("id")
                if not isinstance(run_id, int) or isinstance(run_id, bool):
                    raise LockError(f"{repo}: run has no integer id")
                jobs = api.get(f"/repos/{repo}/actions/runs/{run_id}/jobs?filter=latest&per_page=100")
                job_items = jobs.get("jobs") if isinstance(jobs, dict) else None
                if not isinstance(job_items, list):
                    raise LockError(f"{repo}#{run_id}: job listing is malformed")
                for job in job_items:
                    if job.get("status") != "in_progress" or not participates(job):
                        continue
                    ticket = ticket_of(job)
                    live.append({
                        "repo": repo, "run": run_id, "job": job["id"], "ticket": ticket,
                        "ghost": now - ticket[0] > lease,
                    })
            if len(items) < 100:
                break
            page += 1
            if page > 5:
                raise LockError(f"{repo}: more than 500 in-progress runs; refusing to guess")
    return live


def blockers(me: tuple, participants: list) -> list:
    """Live, non-ghost participants with a strictly smaller ticket than `me`."""
    return [p for p in participants if not p["ghost"] and p["ticket"] < me]


def find_self(api, repo: str, run_id: int, runner_name: str, sleep) -> dict:
    """Locate this very job (same run, same runner) and ticket it; fail closed otherwise."""
    for attempt in range(OWN_JOB_ATTEMPTS):
        jobs = api.get(f"/repos/{repo}/actions/runs/{run_id}/jobs?filter=latest&per_page=100")
        items = jobs.get("jobs") if isinstance(jobs, dict) else None
        mine = [j for j in items or [] if j.get("status") == "in_progress" and j.get("runner_name") == runner_name]
        if len(mine) == 1:
            job = mine[0]
            if not participates(job):
                raise LockError(f"this job has no step named {GATE_STEP!r}; other publishers cannot see it, so it must not publish")
            return {"job": job["id"], "ticket": ticket_of(job)}
        if len(mine) > 1:
            raise LockError("more than one in-progress job matches this runner; refusing to guess")
        if attempt + 1 < OWN_JOB_ATTEMPTS:
            sleep(OWN_JOB_RETRY_SECONDS)
    raise LockError("this job could not be found in the Actions job list")


def acquire(api, *, repo: str, run_id: int, runner_name: str, max_wait: float,
            clock=time.time, sleep=time.sleep, settle=SETTLE_SECONDS, confirm=CONFIRM_SECONDS,
            poll_first=POLL_FIRST_SECONDS, poll_max=POLL_MAX_SECONDS, lease=LEASE_SECONDS,
            log=print) -> dict:
    """Block until this job holds the lowest live ticket, or raise LockError (never degrade)."""
    started = clock()
    registry = load_registry(api)
    if repo not in {row["repo"] for row in registry["writers"]}:
        raise LockError(f"{repo} is not a registered publication writer repository")
    me = find_self(api, repo, run_id, runner_name, sleep)
    ticket = me["ticket"]
    # The first decision happens only after every older participant has had time to appear.
    sleep(max(0.0, ticket[0] + settle - clock()))
    failures, delay, clear_once = 0, poll_first, False
    log(f"publication lock: job {me['job']} ticket {ticket[0]:.0f}/{ticket[1]}; registry {len(registry['writers'])} repositories")
    while True:
        if clock() - started > max_wait:
            raise LockError(f"publication lock not acquired within {max_wait:.0f}s; no effect was attempted")
        try:
            participants = scan(api, registry, clock(), lease)
            failures = 0
        except LockError as error:
            failures += 1
            clear_once = False
            log(f"publication lock: read failure {failures}/{MAX_CONSECUTIVE_READ_FAILURES}: {error}")
            if failures >= MAX_CONSECUTIVE_READ_FAILURES:
                raise
            sleep(poll_first)
            continue
        if not any(p["job"] == me["job"] for p in participants):
            # Our own job disappeared from its own listing: the view is stale or wrong. Never proceed on it.
            clear_once = False
            log("publication lock: own job absent from the scan; rescanning")
            sleep(poll_first)
            continue
        for ghost in (p for p in participants if p["ghost"]):
            log(f"publication lock: ignoring ghost job {ghost['repo']}#{ghost['run']} job {ghost['job']} (older than lease)")
        ahead = blockers(ticket, participants)
        if not ahead:
            if clear_once:
                waited = clock() - started
                log(f"publication lock: acquired after {waited:.0f}s with {len(participants) - 1} other live participant(s)")
                return {"job": me["job"], "ticket": list(ticket), "waitedSeconds": round(waited, 3),
                        "otherLiveParticipants": len(participants) - 1}
            clear_once = True
            sleep(confirm)
            continue
        clear_once = False
        names = ", ".join(f"{p['repo']}#{p['run']}/{p['job']}" for p in ahead)
        log(f"publication lock: waiting for {len(ahead)} older publisher job(s): {names}")
        sleep(delay)
        delay = min(poll_max, delay * 1.5)


def observe(api, now=None) -> dict:
    """Read-only: who currently holds or contends for the lock, oldest first."""
    registry = load_registry(api)
    participants = scan(api, registry, time.time() if now is None else now)
    participants.sort(key=lambda p: p["ticket"])
    return {"registry": [row["repo"] for row in registry["writers"]],
            "participants": [{"repo": p["repo"], "run": p["run"], "job": p["job"],
                              "ticketUnix": p["ticket"][0], "ghost": p["ghost"]} for p in participants],
            "holder": None if not participants else participants[0]["job"]}


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    sub = parser.add_subparsers(dest="command", required=True)
    grab = sub.add_parser("acquire", help="block until this job holds the org publication writer lock")
    grab.add_argument("--max-wait-minutes", type=float, default=45.0)
    sub.add_parser("observe", help="print current holders and contenders (read-only)")
    args = parser.parse_args(argv)
    token = os.environ.get("GH_TOKEN") or os.environ.get("GITHUB_TOKEN") or ""
    if not token:
        print("publication lock: no GitHub token in GH_TOKEN/GITHUB_TOKEN", file=sys.stderr)
        return 1
    api = GitHubApi(token)
    try:
        if args.command == "observe":
            print(json.dumps(observe(api), indent=2, sort_keys=True))
            return 0
        if not 0 < args.max_wait_minutes <= 330:
            raise LockError("--max-wait-minutes must be within 1..330")
        env = os.environ
        for name in ("GITHUB_REPOSITORY", "GITHUB_RUN_ID", "RUNNER_NAME"):
            if not env.get(name):
                raise LockError(f"{name} is not set; this is not a GitHub Actions job")
        receipt = acquire(api, repo=env["GITHUB_REPOSITORY"], run_id=int(env["GITHUB_RUN_ID"]),
                          runner_name=env["RUNNER_NAME"], max_wait=args.max_wait_minutes * 60)
    except LockError as error:
        print(f"::error::publication lock refused: {error}", file=sys.stderr)
        return 1
    out = os.environ.get("GITHUB_OUTPUT")
    if out:
        with open(out, "a", encoding="utf-8") as handle:
            handle.write(f"waited-seconds={receipt['waitedSeconds']}\n")
    print(json.dumps(receipt, sort_keys=True))
    return 0


if __name__ == "__main__":
    sys.exit(main())
