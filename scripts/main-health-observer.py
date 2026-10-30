#!/usr/bin/env python3
r"""Main-health observer: run what path filters skip, read what push runs left behind, keep one issue (Refs #4730).

WHY THIS EXISTS
===============

Three checks went red on main in October 2026 and nobody noticed, because each is path-filtered and none is
required: `skill-registry-coherence` (red 6 days), `coord-github`/`edge` (broken by an unrelated PR that
touched files `edge` did not watch) and `skill-union-bundle`/`skill-union-selftest` (red 12 days, the filters
never listed `.agents/skills/**`). A path-filtered push gate only runs when its own filter matches, and a
pull-request run is judged against the merge base, so a break caused by a file outside the filter is red on
main with no run to show it. This observer is the deterministic net under that: nightly, and on dispatch, it

  1. FRESH RUN   runs every path-filtered push check that can run standalone against main, whatever changed,
                 by calling the check's own workflow (`workflow_call`); the check logic is not copied;
  2. LATEST RUN  reads the latest conclusive push-run and schedule-run conclusion of every workflow that
                 triggers on push to main or on a schedule (canaries such as `v1-writer-census-drift`, or
                 `installation-grants-current`, which needs secrets and so cannot be called);
  3. FRESHNESS   compares the newest scheduled run of every workflow that has a cron with that cron's cadence:
                 a schedule that has not fired for more than twice its longest gap (never less than two hours),
                 a scheduled workflow that has never fired since it was registered on main, and a scheduled
                 workflow GitHub has disabled are findings. GitHub does not guarantee a schedule fires, and a
                 silent scheduled canary is indistinguishable from a healthy one without this;
  4. REPORT      keeps ONE issue, "Main health: FS-GG/.github": opened or rewritten when the set of red
                 workflows changes, commented once per change, closed when everything is green.

It never edits a workflow, a branch or a board field. It is a detector.

THE LIST IS DERIVED, NOT WRITTEN BY HAND
========================================

`plan` parses `.github/workflows/*.yml`:

  candidate  a workflow with a `push:` trigger that has a `paths:` filter and runs on main
  eligible   a candidate that can run standalone from a schedule with no secrets and no event payload:
             hosted ubuntu runners only, no secrets other than GITHUB_TOKEN, no `environment:`, no
             permission above read (the caller job is granted exactly the read scopes the callee names),
             no `workflow_run`, no nested `uses:` job, no `inputs.*`,
             no `github.event.*` payload read outside a step or job that is guarded by an `if:` on
             `github.event_name` (or carries a `||` fallback), and at least one job that runs under a
             `schedule` event. Each exclusion carries its reason in `plan --json`.
  covered    an eligible workflow that declares `workflow_call:` (the opt-in: the callee says "calling me is
             safe"). The observer workflow carries exactly one `check-<stem>` caller job per covered
             workflow, between GENERATED markers; `plan --check` fails if the two differ, if a covered
             workflow stopped being eligible, or if more than 50 workflows are covered (GitHub's limit on
             unique reusable workflows per file).

Eligible workflows that have not opted in are listed by `plan` as `uncovered`. They are still read by the
latest-run side, and nothing hides them.

FAIL CLOSED
===========

`collect` writes a findings document whose `unknown` list names every check it could not judge: a caller job
that was cancelled or skipped, a missing result, an API error, an unreadable response, a workflow whose
latest-run read failed. The report job refuses to rewrite or close the issue while `unknown` is non-empty and
then fails the run. There is no path from "could not read" to "healthy": the issue is closed only when every
fresh run succeeded, every latest-run read succeeded, and nothing is red.

A workflow that has NO conclusive run in the retention window is not unknown: it has not run, and `collect`
says so in its log. (For a scheduled workflow, "has never run" is the freshness finding above, not health.) A cancelled or skipped latest run is not conclusive and the next older run is used.

FRESHNESS DETAILS
=================

The cadence is read from the workflow's own `cron:` lines (union of all of them): the longest gap between
consecutive firings over the next 62 days, so a weekday-only cron has a 3-day gap and a monthly one 31. Overdue
means the newest run created by a `schedule` event on main is older than max(2 x cadence, 2 hours). The floor
keeps a 5-minute cron from paging on GitHub's ordinary queueing delay. A scheduled workflow with no scheduled
run is a finding once it has been registered for longer than that threshold, and is listed as pending before
that. An unparsable cron, or a failed read, is UNKNOWN. A freshness finding has its own key (`stale:<file>`) so
its start and end each produce one comment.

THIS OBSERVER IS SCHEDULED TOO. If GitHub stops firing its cron nothing here can say so: the only reader of its
staleness is another observer. The board-drift observer runs hourly and is the natural reader; see the pull
request for the proposed reciprocal check.

DEDUPE
======

The finding set is identified by a digest of finding keys (one per red workflow, no run ids, no ages). The
digest is carried in a hidden marker in the issue body. Unchanged set -> no body edit and no comment. A
changed set -> body rewritten and ONE comment saying what is new and what cleared. Everything green -> one
closing comment, marker rewritten to the empty set, issue closed. An issue closed by a human while the same
set is still red stays closed until the set changes.

PRIVACY AND CREDENTIAL SPLIT
============================

This repository is public and every workflow named here is in it, so there is nothing private to mask. The
credential split is still two-sided: the `collect` job holds `actions: read` (run history) and the check jobs
hold `contents: read`; the `report` job holds `issues: write` and nothing else. The job that can write cannot
read run history, and the job that reads run history cannot write.

Exit codes: 0 success; 1 an unexpected error or a plan drift; 2 NO VERDICT (the input could not be trusted).
"""
from __future__ import annotations

import argparse
import datetime as dt
import fnmatch
import hashlib
import json
import os
import re
import subprocess
import sys
from pathlib import Path

try:
    import yaml
except ImportError:  # pragma: no cover - the workflow installs the pinned dependency
    print("main-health-observer: PyYAML is required (the workflow runs setup-policy-python)", file=sys.stderr)
    raise SystemExit(1)

FINDINGS_SCHEMA = "fsgg:main-health-findings/v1"
REPORT_TITLE = "Main health: FS-GG/.github"
REPORT_LABEL = "main-health"
REPORT_REPO = "FS-GG/.github"
MARKER_PREFIX = "fsgg:main-health/v1"
OBSERVER_WORKFLOW = "main-health-observer.yml"
CHECK_PREFIX = "check-"
GENERATED_CHECKS = ("# BEGIN GENERATED CHECK JOBS", "# END GENERATED CHECK JOBS")
GENERATED_NEEDS = ("# BEGIN GENERATED NEEDS", "# END GENERATED NEEDS")
MAX_UNIQUE_CALLS = 50  # GitHub: unique reusable workflows per workflow file
NO_VERDICT = 2

BODY_LIMIT = 60000
MARKER_KEYS_LIMIT = 12000
STALE_FACTOR = 2
STALE_FLOOR = dt.timedelta(hours=2)
CRON_HORIZON_DAYS = 62
RUN_PAGE = 10  # completed runs read per workflow and event, to find the latest conclusive one
GH_TIMEOUT = 90

RED = ("failure", "timed_out", "startup_failure")
GREEN = ("success",)
CONCLUSIVE = RED + GREEN


class NoVerdict(Exception):
    pass


# --------------------------------------------------------------------------------------------------
# Plan: derive the list from the workflows. PURE (file reads only).
# --------------------------------------------------------------------------------------------------

def on_block(doc: dict) -> dict:
    """The `on:` mapping. PyYAML parses a bare `on` key as boolean True."""
    on = doc.get(True, doc.get("on"))
    if isinstance(on, str):
        return {on: None}
    if isinstance(on, list):
        return {str(k): None for k in on}
    return on if isinstance(on, dict) else {}


def push_branches(on: dict) -> list[str] | None:
    push = on.get("push")
    if push is None and "push" not in on:
        return None
    if isinstance(push, dict) and "branches" in push:
        branches = push["branches"]
        return [branches] if isinstance(branches, str) else list(branches or [])
    return ["*"]  # a push trigger with no branch filter runs on every branch, main included


def on_main(branches: list[str] | None) -> bool:
    if branches is None:
        return False
    return any(fnmatch.fnmatchcase("main", pattern) for pattern in branches)


def push_has_paths(on: dict) -> bool:
    push = on.get("push")
    return isinstance(push, dict) and bool(push.get("paths"))


def has_schedule(on: dict) -> bool:
    return bool(on.get("schedule"))


_COMPARE = re.compile(r"github\.event_name\s*([=!]=)\s*'([a-z_]+)'")
_SAFE_EXPR = re.compile(r"^[\sTrueFalsnot()&|!]*$")


def job_runs_on_schedule(condition: object) -> bool | None:
    """Does a job-level `if:` hold under a scheduled run? None when the expression is not simple enough to decide.

    Only comparisons of `github.event_name` joined by `&&`, `||`, `!` and parentheses are evaluated; any other
    context makes the answer unknown, and unknown makes the workflow ineligible (we never call what we cannot
    predict)."""
    if condition is None or condition is True:
        return True
    if condition is False:
        return False
    text = str(condition).strip()
    text = re.sub(r"^\$\{\{\s*(.*?)\s*\}\}$", r"\1", text, flags=re.S)
    if text in ("always()", "!cancelled()", "success()"):
        return True

    def compare(match: re.Match[str]) -> str:
        equal = match.group(1) == "=="
        return str(("schedule" == match.group(2)) == equal)

    reduced = _COMPARE.sub(compare, text)
    reduced = reduced.replace("&&", " and ").replace("||", " or ")
    reduced = re.sub(r"!(?!=)", " not ", reduced)
    if not re.fullmatch(r"[\sTrueFalsandornt()]*", reduced) or not reduced.strip():
        return None
    try:
        return bool(eval(reduced, {"__builtins__": {}}, {}))  # noqa: S307 - charset-restricted above
    except Exception:
        return None


def _payload_unsafe(chunk: object) -> list[str]:
    """Event payload / PR-only references in a step or job that nothing guards."""
    if not isinstance(chunk, dict):
        return []
    guard = str(chunk.get("if", ""))
    if "github.event_name" in guard:
        return []
    text = yaml.safe_dump({k: v for k, v in chunk.items() if k != "if"}, default_flow_style=False)
    problems = []
    for match in re.finditer(r"\$\{\{(.*?)\}\}", text, re.S):
        expr = match.group(1)
        if re.search(r"github\.event\.(?!inputs\b)|github\.(base_ref|head_ref)\b", expr) and "||" not in expr:
            problems.append(expr.strip())
    return problems


def read_scopes(doc: dict) -> list[str]:
    """Permission scopes the callee asks for at workflow or job level (all read, once classified eligible)."""
    scopes = {"contents"}
    for block in [doc.get("permissions")] + [j.get("permissions") for j in (doc.get("jobs") or {}).values() if isinstance(j, dict)]:
        if isinstance(block, dict):
            scopes |= {s for s, level in block.items() if level == "read"}
    return sorted(scopes)


def classify(doc: dict, raw: str) -> list[str]:
    """Reasons a candidate cannot be called standalone from a scheduled run. Empty means eligible."""
    reasons: list[str] = []
    on = on_block(doc)
    if "workflow_run" in on:
        reasons.append("workflow_run trigger")
    body = "\n".join(line for line in raw.splitlines() if not line.lstrip().startswith("#"))
    if re.search(r"secrets\.(?!GITHUB_TOKEN\b)\w+|secrets:\s*inherit", body):
        reasons.append("uses secrets other than GITHUB_TOKEN")
    if re.search(r"(?<![\w.])inputs\.|github\.event\.inputs", body):
        reasons.append("reads workflow inputs")

    def check_permissions(block: object, where: str) -> None:
        if block is None:
            return
        if isinstance(block, str):
            reasons.append(f"{where} permissions: {block}")
            return
        for scope, level in block.items():
            if level not in ("read", "none"):
                reasons.append(f"{where} permission {scope}: {level} is more than read")

    check_permissions(doc.get("permissions"), "workflow")
    jobs = doc.get("jobs") or {}
    if not jobs:
        reasons.append("no jobs")
    runnable = 0
    for name, job in jobs.items():
        where = f"job {name}"
        if "uses" in job:
            reasons.append(f"{where} calls another workflow")
            continue
        runs_on = job.get("runs-on")
        if not (isinstance(runs_on, str) and re.fullmatch(r"ubuntu-[\w.-]+", runs_on)):
            reasons.append(f"{where} runs on {runs_on!r}, not a GitHub-hosted ubuntu runner")
        if "environment" in job:
            reasons.append(f"{where} targets an environment")
        check_permissions(job.get("permissions"), where)
        verdict = job_runs_on_schedule(job.get("if"))
        if verdict is None:
            reasons.append(f"{where} has an `if:` this observer cannot evaluate")
        elif verdict:
            runnable += 1
        for problem in _payload_unsafe({k: v for k, v in job.items() if k != "steps"}):
            reasons.append(f"{where} reads the event payload unguarded: {problem}")
        for index, step in enumerate(job.get("steps") or []):
            for problem in _payload_unsafe(step):
                reasons.append(f"{where} step {index + 1} reads the event payload unguarded: {problem}")
    if jobs and not runnable and not any("cannot evaluate" in r for r in reasons):
        reasons.append("no job runs under a schedule event")
    return sorted(set(reasons))


def stem(name: str) -> str:
    return name[:-4] if name.endswith(".yml") else name


def derive(root: Path) -> dict:
    """The whole derivation from the workflows under `root`. Deterministic and sorted."""
    wf_dir = root / ".github" / "workflows"
    files = sorted(p for p in wf_dir.glob("*.yml"))
    if not files:
        raise NoVerdict(f"no workflows under {wf_dir}")
    candidates: list[str] = []
    excluded: dict[str, list[str]] = {}
    eligible: list[str] = []
    callable_: set[str] = set()
    push_main: list[str] = []
    scheduled: list[str] = []
    crons: dict[str, list[str]] = {}
    names: dict[str, str] = {}
    scopes: dict[str, list[str]] = {}
    for path in files:
        raw = path.read_text(encoding="utf-8")
        try:
            doc = yaml.safe_load(raw)
        except yaml.YAMLError as exc:
            raise NoVerdict(f"{path.name} is not valid YAML: {exc.__class__.__name__}") from exc
        if not isinstance(doc, dict):
            raise NoVerdict(f"{path.name} is not a workflow mapping")
        on = on_block(doc)
        names[path.name] = str(doc.get("name") or stem(path.name))
        if "workflow_call" in on:
            callable_.add(path.name)
        if has_schedule(on):
            # Includes this observer: a run that finds its own previous night missing catches a dropped night
            # (not a permanent stop; nothing here can see that, see the module docstring).
            scheduled.append(path.name)
            crons[path.name] = [str(e.get("cron")) for e in on["schedule"] if isinstance(e, dict)]
        if path.name == OBSERVER_WORKFLOW:
            continue
        if on_main(push_branches(on)):
            push_main.append(path.name)
            if push_has_paths(on):
                candidates.append(path.name)
                reasons = classify(doc, raw)
                if reasons:
                    excluded[path.name] = reasons
                else:
                    eligible.append(path.name)
                    scopes[path.name] = read_scopes(doc)
    covered = sorted(n for n in eligible if n in callable_)
    bad_stems = [n for n in covered if not re.fullmatch(r"[a-z][a-z0-9-]*\.yml", n)]
    if bad_stems:
        raise NoVerdict(f"covered workflow file names that cannot become a job id: {bad_stems}")
    return {
        "candidates": candidates,
        "eligible": eligible,
        "covered": covered,
        "uncovered": sorted(set(eligible) - set(covered)),
        "excluded": {k: excluded[k] for k in sorted(excluded)},
        "coveredButIneligible": sorted(n for n in candidates if n in callable_ and n in excluded),
        "callScopes": {n: scopes[n] for n in covered},
        "latestPush": sorted(push_main),
        "latestSchedule": sorted(scheduled),
        "crons": {n: crons[n] for n in sorted(crons)},
        "names": names,
    }


def render_check_jobs(covered: list[str], scopes: dict[str, list[str]]) -> str:
    lines = []
    for name in covered:
        lines += [
            f"  {CHECK_PREFIX}{stem(name)}:",
            f"    uses: ./.github/workflows/{name}",
            "    permissions:",
        ] + [f"      {scope}: read" for scope in scopes[name]]
    return "\n".join(lines) + "\n"


def render_needs(covered: list[str], scopes: dict[str, list[str]]) -> str:
    return "".join(f"      - {CHECK_PREFIX}{stem(n)}\n" for n in covered)


def _region(text: str, markers: tuple[str, str]) -> tuple[int, int] | None:
    begin, end = markers
    b = text.find(begin)
    e = text.find(end)
    if b < 0 or e < b:
        return None
    start = text.index("\n", b) + 1
    line_start = text.rfind("\n", 0, e) + 1
    return start, line_start


def workflow_problems(plan: dict, observer_text: str) -> list[str]:
    """Differences between the derived plan and the observer workflow, as readable lines."""
    problems: list[str] = []
    covered = plan["covered"]
    if len(covered) > MAX_UNIQUE_CALLS:
        problems.append(f"{len(covered)} covered workflows exceed GitHub's limit of {MAX_UNIQUE_CALLS} unique "
                        "reusable workflows per file; drop `workflow_call` from the least valuable")
    for name in plan["coveredButIneligible"]:
        problems.append(f"{name} declares workflow_call but cannot run standalone: " + "; ".join(plan["excluded"][name]))
    for marker, render in ((GENERATED_CHECKS, render_check_jobs), (GENERATED_NEEDS, render_needs)):
        span = _region(observer_text, marker)
        if span is None:
            problems.append(f"{OBSERVER_WORKFLOW} has no `{marker[0]}` .. `{marker[1]}` region")
        elif observer_text[span[0]:span[1]] != render(covered, plan["callScopes"]):
            problems.append(f"{OBSERVER_WORKFLOW} {marker[0].replace('# BEGIN ', '').lower()} differ from the derived "
                            "list; regenerate with `plan --write`")
    return problems


def write_workflow(plan: dict, observer_path: Path) -> bool:
    text = observer_path.read_text(encoding="utf-8")
    new = text
    for marker, render in ((GENERATED_CHECKS, render_check_jobs), (GENERATED_NEEDS, render_needs)):
        span = _region(new, marker)
        if span is None:
            raise NoVerdict(f"{observer_path.name} has no `{marker[0]}` region")
        new = new[:span[0]] + render(plan["covered"], plan["callScopes"]) + new[span[1]:]
    if new != text:
        observer_path.write_text(new, encoding="utf-8")
    return new != text


def cmd_plan(args: argparse.Namespace) -> int:
    root = Path(args.root).resolve()
    try:
        plan = derive(root)
    except NoVerdict as exc:
        print(f"main-health-observer: NO VERDICT: {exc}", file=sys.stderr)
        return NO_VERDICT
    observer = root / ".github" / "workflows" / OBSERVER_WORKFLOW
    if args.write:
        print("main-health-observer: " + ("rewrote" if write_workflow(plan, observer) else "left unchanged") + f" {OBSERVER_WORKFLOW}")
    problems = workflow_problems(plan, observer.read_text(encoding="utf-8")) if observer.exists() else [f"{OBSERVER_WORKFLOW} is missing"]
    if args.json:
        print(json.dumps({k: v for k, v in plan.items() if k != "names"}, indent=2, sort_keys=True))
    else:
        print(f"main-health-observer: {len(plan['candidates'])} path-filtered push workflows; "
              f"{len(plan['eligible'])} can run standalone; {len(plan['covered'])} covered by a fresh run; "
              f"{len(plan['uncovered'])} eligible but not opted in; {len(plan['excluded'])} excluded; "
              f"{len(plan['latestPush'])} read for the latest push run; {len(plan['latestSchedule'])} for the latest schedule run")
        for name in plan["uncovered"]:
            print(f"  uncovered (latest-run read only): {name}")
        for name, reasons in plan["excluded"].items():
            print(f"  excluded: {name}: {'; '.join(reasons)}")
    if args.check:
        for line in problems:
            print(f"main-health-observer: DRIFT: {line}", file=sys.stderr)
        return 1 if problems else 0
    return 0


# --------------------------------------------------------------------------------------------------
# Cron cadence. PURE.
# --------------------------------------------------------------------------------------------------

_MONTHS = {m: i + 1 for i, m in enumerate("jan feb mar apr may jun jul aug sep oct nov dec".split())}
_DOWS = {d: i for i, d in enumerate("sun mon tue wed thu fri sat".split())}


def _cron_field(text: str, lo: int, hi: int, names: dict[str, int] | None = None) -> tuple[set[int], bool]:
    """(values, restricted). `restricted` is False for a bare `*`, which matters for the day-of-month/week rule."""
    values: set[int] = set()
    restricted = text != "*"
    for part in text.split(","):
        base, _, step_text = part.partition("/")
        step = int(step_text) if step_text else 1
        if step < 1:
            raise ValueError("step")

        def num(token: str) -> int:
            token = token.lower()
            return names[token[:3]] if names and token[:3] in names and not token.isdigit() else int(token)

        if base == "*":
            start, end = lo, hi
        elif "-" in base:
            a, b = base.split("-", 1)
            start, end = num(a), num(b)
        else:
            start = num(base)
            end = hi if step_text else start
        if start < lo or end > hi or start > end:
            raise ValueError("range")
        values |= set(range(start, end + 1, step))
        if step_text or "-" in base:
            restricted = True
    return values, restricted


def cron_fires(expr: str, start: dt.datetime, days: int) -> list[dt.datetime]:
    """Every firing of a 5-field cron from `start` (a UTC datetime) for `days` days, sorted."""
    fields = expr.split()
    if len(fields) != 5:
        raise ValueError("five fields expected")
    minutes, _ = _cron_field(fields[0], 0, 59)
    hours, _ = _cron_field(fields[1], 0, 23)
    doms, dom_r = _cron_field(fields[2], 1, 31)
    months, _ = _cron_field(fields[3], 1, 12, _MONTHS)
    dows, dow_r = _cron_field(fields[4], 0, 7, _DOWS)
    dows = {d % 7 for d in dows}
    out: list[dt.datetime] = []
    day0 = start.replace(hour=0, minute=0, second=0, microsecond=0)
    for offset in range(days + 1):
        day = day0 + dt.timedelta(days=offset)
        if day.month not in months:
            continue
        dom_ok, dow_ok = day.day in doms, ((day.weekday() + 1) % 7) in dows
        if dom_r and dow_r:
            ok = dom_ok or dow_ok  # cron: both restricted means either
        else:
            ok = dom_ok and dow_ok
        if not ok:
            continue
        for h in sorted(hours):
            for m in sorted(minutes):
                t = day.replace(hour=h, minute=m)
                if t >= start:
                    out.append(t)
    return sorted(set(out))


def cadence(crons: list[str], now: dt.datetime) -> dt.timedelta:
    """Longest gap between consecutive firings of the union of `crons` over the horizon."""
    fires: list[dt.datetime] = []
    for expr in crons:
        fires += cron_fires(expr, now, CRON_HORIZON_DAYS)
    fires = sorted(set(fires))
    if len(fires) < 2:
        raise ValueError("fewer than two firings in the horizon")
    return max(b - a for a, b in zip(fires, fires[1:]))


def freshness_threshold(period: dt.timedelta) -> dt.timedelta:
    return max(period * STALE_FACTOR, STALE_FLOOR)


def hours(delta: dt.timedelta) -> str:
    return f"{delta.total_seconds() / 3600:.1f}h"


def freshness(name: str, crons: list[str], meta: dict, newest: dict | None, now: dt.datetime) -> dict | None:
    """PURE. A freshness evidence dict for one scheduled workflow, 'pending' for a grace case, or None when fresh.

    `meta` is the workflow object ({state, created_at}); `newest` the newest run created by a schedule event on main."""
    period = cadence(crons, now)
    limit = freshness_threshold(period)
    state = meta.get("state")
    if state != "active":
        return {"event": "schedule", "conclusion": "overdue", "detail": f"workflow state is {state!r}; GitHub will not fire its schedule"}
    if newest is None:
        registered = parse_time(meta.get("created_at"))
        if registered is None:
            raise ValueError("workflow has no created_at")
        if now - registered <= limit:
            return {"event": "schedule", "conclusion": "pending",
                    "detail": f"never run, registered {hours(now - registered)} ago, within the {hours(limit)} allowance"}
        return {"event": "schedule", "conclusion": "overdue",
                "detail": f"never run on a schedule since it was registered on main {hours(now - registered)} ago "
                          f"(cadence {hours(period)}, allowance {hours(limit)})"}
    last = parse_time(newest.get("created_at"))
    if last is None:
        raise ValueError("newest run has no created_at")
    if now - last > limit:
        return {"event": "schedule", "conclusion": "overdue", "runId": newest.get("id"), "url": newest.get("html_url"),
                "detail": f"last scheduled run {last.strftime('%Y-%m-%dT%H:%MZ')}, {hours(now - last)} ago; "
                          f"cadence {hours(period)}, allowance {hours(limit)}"}
    return None


# --------------------------------------------------------------------------------------------------
# Collect: fresh results from `needs`, latest-run reads from the API. IO is one function: gh_json.
# --------------------------------------------------------------------------------------------------

def parse_time(value: object) -> dt.datetime | None:
    if not isinstance(value, str) or not value:
        return None
    try:
        return dt.datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError:
        return None


def gh_json(path: str, *, jq_lines: str | None = None):
    """GET a REST path with the `gh` CLI. A failure raises RuntimeError with fixed text only."""
    gh = os.environ.get("MAIN_HEALTH_GH", "gh")
    cmd = [gh, "api", path]
    if jq_lines:
        cmd += ["--paginate", "--jq", jq_lines]
    try:
        proc = subprocess.run(cmd, capture_output=True, text=True, timeout=GH_TIMEOUT, check=False)
    except (OSError, subprocess.TimeoutExpired) as exc:
        raise RuntimeError(f"gh api could not be run ({exc.__class__.__name__})") from exc
    if proc.returncode:
        raise RuntimeError(f"gh api call failed (exit {proc.returncode})")
    if jq_lines:
        return [json.loads(line) for line in proc.stdout.splitlines() if line.strip()]
    try:
        return json.loads(proc.stdout)
    except json.JSONDecodeError as exc:
        raise RuntimeError("gh api returned an unreadable response") from exc


def latest_conclusive(repo: str, workflow: str, event: str) -> dict | None:
    """The newest completed run on main for `event` whose conclusion says something, or None if there is none."""
    data = gh_json(f"repos/{repo}/actions/workflows/{workflow}/runs?branch=main&event={event}&status=completed&per_page={RUN_PAGE}")
    runs = data.get("workflow_runs") if isinstance(data, dict) else None
    if not isinstance(runs, list):
        raise RuntimeError("unexpected run-list shape")
    for run in runs:
        if run.get("conclusion") in CONCLUSIVE:
            return {
                "event": event,
                "conclusion": run["conclusion"],
                "runId": run.get("id"),
                "url": run.get("html_url"),
                "sha": str(run.get("head_sha", ""))[:10],
                "at": run.get("updated_at") or run.get("created_at"),
            }
    return None


def failed_jobs(repo: str, run_id: object, prefix: str | None = None) -> list[str]:
    """Names of failed jobs of one run. Detail only: its failure never changes a verdict."""
    jobs = gh_json(f"repos/{repo}/actions/runs/{run_id}/jobs?per_page=100", jq_lines=".jobs[]")
    names = [str(j.get("name")) for j in jobs if j.get("conclusion") in RED]
    if prefix:
        names = [n for n in names if n.startswith(prefix)]
    return sorted(names)


def fresh_results(plan: dict, needs: object) -> tuple[dict[str, str], list[dict]]:
    """Map covered workflow -> 'success' | 'failure' from the `needs` context; anything else is unknown."""
    unknown: list[dict] = []
    results: dict[str, str] = {}
    if not isinstance(needs, dict):
        raise NoVerdict("needs context is not a JSON object")
    for name in plan["covered"]:
        entry = needs.get(CHECK_PREFIX + stem(name))
        result = entry.get("result") if isinstance(entry, dict) else None
        if result == "success":
            results[name] = "success"
        elif result == "failure":
            results[name] = "failure"
        else:
            unknown.append({"workflow": name, "source": "fresh", "why": f"caller job result is {result!r}, not a verdict"})
    extra = sorted(k for k in needs if k.startswith(CHECK_PREFIX) and k[len(CHECK_PREFIX):] + ".yml" not in plan["covered"])
    if extra:
        raise NoVerdict(f"needs holds check jobs the derived list does not cover: {extra}")
    return results, unknown


def collect(plan: dict, needs: object, repo: str, run_id: str | None, now: dt.datetime) -> dict:
    fresh, unknown = fresh_results(plan, needs)
    evidence: dict[str, list[dict]] = {}
    superseded: dict[str, list[dict]] = {}
    clean = {"fresh": len(fresh), "latestRuns": 0, "noRun": []}
    for name, verdict in fresh.items():
        if verdict == "failure":
            evidence.setdefault(name, []).append({"event": "fresh", "conclusion": "failure", "runId": run_id,
                                                  "url": f"https://github.com/{repo}/actions/runs/{run_id}" if run_id else None,
                                                  "sha": None, "at": None})
    # The observer's own previous nights are judged for freshness below, not for conclusion: a failed night
    # would otherwise keep a finding about this very workflow alive until the next night.
    scheduled_others = [n for n in plan["latestSchedule"] if n != OBSERVER_WORKFLOW]
    for event, workflows in (("push", plan["latestPush"]), ("schedule", scheduled_others)):
        for name in workflows:
            try:
                latest = latest_conclusive(repo, name, event)
            except RuntimeError as exc:
                unknown.append({"workflow": name, "source": event, "why": str(exc)})
                continue
            clean["latestRuns"] += 1
            if latest is None:
                clean["noRun"].append(f"{name}:{event}")
                continue
            if latest["conclusion"] not in RED:
                continue
            if fresh.get(name) == "success":
                superseded.setdefault(name, []).append(latest)  # a green fresh run on main outranks an older red
            else:
                evidence.setdefault(name, []).append(latest)
    stale: dict[str, dict] = {}
    pending: list[str] = []
    for name in plan["latestSchedule"]:
        try:
            meta = gh_json(f"repos/{repo}/actions/workflows/{name}")
            runs = gh_json(f"repos/{repo}/actions/workflows/{name}/runs?branch=main&event=schedule&per_page=1")
            listed = runs.get("workflow_runs") if isinstance(runs, dict) else None
            if not isinstance(meta, dict) or not isinstance(listed, list):
                raise RuntimeError("unexpected workflow or run-list shape")
            verdict = freshness(name, plan["crons"][name], meta, listed[0] if listed else None, now)
        except RuntimeError as exc:
            unknown.append({"workflow": name, "source": "freshness", "why": str(exc)})
            continue
        except ValueError as exc:
            unknown.append({"workflow": name, "source": "freshness", "why": f"cannot judge the schedule: {exc}"})
            continue
        clean["freshness"] = clean.get("freshness", 0) + 1
        if verdict is None:
            continue
        if verdict["conclusion"] == "pending":
            pending.append(name)
        else:
            stale[name] = verdict
    findings = []
    for name in sorted(stale):
        findings.append({"key": f"stale:{name}", "workflow": plan["names"].get(name, stem(name)), "file": name,
                         "evidence": [stale[name]], "failedJobs": []})
    for name in sorted(evidence):
        details: list[str] = []
        for item in evidence[name]:
            try:
                if item["event"] == "fresh":
                    details += failed_jobs(repo, run_id, CHECK_PREFIX + stem(name) + " /") if run_id else []
                else:
                    details += failed_jobs(repo, item["runId"])
            except RuntimeError:
                details.append("(failed job names unavailable)")
        findings.append({
            "key": f"wf:{name}",
            "workflow": plan["names"].get(name, stem(name)),
            "file": name,
            "evidence": evidence[name],
            "failedJobs": sorted(set(details)),
        })
    findings.sort(key=lambda f: f["key"])
    return {
        "schema": FINDINGS_SCHEMA,
        "evaluatedAt": now.strftime("%Y-%m-%dT%H:%M:%SZ"),
        "repo": repo,
        "counts": {"covered": len(plan["covered"]), "fresh": clean["fresh"], "latestRunReads": clean["latestRuns"],
                   "red": len(findings), "unknown": len(unknown), "noRun": len(clean["noRun"]),
                   "superseded": len(superseded), "freshness": clean.get("freshness", 0), "stale": len(stale),
                   "pendingFirstRun": len(pending)},
        "pendingFirstRun": pending,
        "noRun": clean["noRun"],
        "superseded": {k: [e["runId"] for e in v] for k, v in sorted(superseded.items())},
        "unknown": unknown,
        "digest": digest(findings),
        "findings": findings,
    }


def cmd_collect(args: argparse.Namespace) -> int:
    try:
        plan = derive(Path(args.root).resolve())
        observer = Path(args.root).resolve() / ".github" / "workflows" / OBSERVER_WORKFLOW
        problems = workflow_problems(plan, observer.read_text(encoding="utf-8"))
        if problems:
            raise NoVerdict("the observer workflow no longer matches the derived list: " + problems[0])
        needs = json.loads(os.environ.get("MAIN_HEALTH_NEEDS", ""))
        now = parse_time(args.now) if args.now else dt.datetime.now(dt.timezone.utc)
        if now is None:
            raise NoVerdict(f"--now {args.now!r} is not an ISO timestamp")
        doc = collect(plan, needs, args.repo, args.run_id, now)
    except (NoVerdict, OSError, json.JSONDecodeError) as exc:
        print(f"main-health-observer: NO VERDICT: {exc.__class__.__name__}: {exc}", file=sys.stderr)
        return NO_VERDICT
    Path(args.out).write_text(json.dumps(doc, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    c = doc["counts"]
    print(f"main-health-observer: {c['red']} red, {c['unknown']} unknown; {c['fresh']} fresh run(s) judged of "
          f"{c['covered']} covered, {c['latestRunReads']} latest-run read(s), {c['noRun']} with no conclusive run, "
          f"{c['superseded']} stale red superseded by a green fresh run; {c['freshness']} schedule(s) checked for "
          f"freshness, {c['stale']} overdue, {c['pendingFirstRun']} awaiting a first run")
    for f in doc["findings"]:
        print(f"  {'stale' if f['key'].startswith('stale:') else 'red  '}    {f['file']}: "
              + ", ".join(f"{e['event']} {e['conclusion']}" for e in f["evidence"]))
    for u in doc["unknown"]:
        print(f"  UNKNOWN  {u['workflow']} ({u['source']}): {u['why']}")
    return 0


# --------------------------------------------------------------------------------------------------
# Report: the one issue. PURE planner + REST writer.
# --------------------------------------------------------------------------------------------------

def digest(findings: list[dict]) -> str:
    return hashlib.sha256("\n".join(sorted(f["key"] for f in findings)).encode()).hexdigest()[:16]


def code(value: object) -> str:
    return "`" + str(value).replace("`", "'").replace("\n", " ") + "`"


def marker(findings: list[dict]) -> str:
    keys = sorted(f["key"] for f in findings)
    data: dict = {"digest": digest(findings), "count": len(keys), "keys": keys}
    if len(json.dumps(data)) > MARKER_KEYS_LIMIT:
        data = {"digest": data["digest"], "count": len(keys)}
    return f"<!-- {MARKER_PREFIX} {json.dumps(data, separators=(',', ':'), sort_keys=True)} -->"


def parse_marker(body: str | None) -> dict | None:
    match = re.search(rf"<!--\s*{re.escape(MARKER_PREFIX)}\s+(\{{.*?\}})\s*-->", body or "", re.S)
    if not match:
        return None
    try:
        data = json.loads(match.group(1))
    except json.JSONDecodeError:
        return None
    return data if isinstance(data, dict) and isinstance(data.get("digest"), str) else None


def finding_lines(f: dict) -> list[str]:
    parts = []
    for e in f["evidence"]:
        link = f" ({e['url']})" if e.get("url") else ""
        if e.get("detail"):
            parts.append(f"schedule {e['conclusion']}: {e['detail']}{link}")
            continue
        parts.append(f"{e['event']} run {e['conclusion']}{' at ' + e['sha'] if e.get('sha') else ''}{link}")
    line = f"- {code(f['file'])}: " + "; ".join(parts)
    if f.get("failedJobs"):
        line += ". Failed: " + ", ".join(code(j) for j in f["failedJobs"])
    return [line]


def render_body(doc: dict, limit: int = BODY_LIMIT) -> str:
    findings = doc["findings"]
    c = doc.get("counts", {})
    head = [
        marker(findings),
        "",
        f"Generated by the scheduled main-health observer at {doc.get('evaluatedAt', 'unknown')}. It runs every "
        "path-filtered push check that can run standalone against main whatever changed, and reads the latest "
        "push-run and schedule-run conclusion of each push or scheduled workflow. It never edits anything: the "
        "owner of each red workflow repairs it, and this issue is rewritten when the red set changes.",
        "",
        f"{len(findings)} finding(s): red workflows and overdue schedules ({c.get('fresh', '?')} fresh run(s) judged, "
        f"{c.get('latestRunReads', '?')} latest-run read(s), {c.get('freshness', '?')} schedule(s) checked for freshness).",
        "",
    ]
    foot = ["", f"Owner: {code('FS-GG/.github#4730')} (Design A, deterministic observation). Reopen or comment here is not "
            "needed: the issue closes itself when everything is green."]
    lines: list[str] = []
    budget = limit - sum(len(x) + 1 for x in head + foot) - 300
    shown = 0
    for f in findings:
        block = finding_lines(f)
        cost = sum(len(x) + 1 for x in block)
        if budget - cost < 0:
            break
        lines += block
        budget -= cost
        shown += 1
    if shown < len(findings):
        lines += ["", f"- ... {len(findings) - shown} more red workflow(s) not shown to stay under the issue size limit; "
                      "the digest above covers all of them."]
    return "\n".join(head + lines + foot) + "\n"


def render_clear_body(doc: dict) -> str:
    return "\n".join([
        marker([]), "",
        f"Every judged workflow was green at {doc.get('evaluatedAt', 'unknown')}. The observer reopens this issue if a "
        "workflow goes red.",
    ]) + "\n"


def plan_report(doc: dict, existing: dict | None) -> dict:
    """PURE. What to do to the one report issue. `existing` is None or {number, state, body}."""
    findings = doc["findings"]
    if doc.get("unknown"):
        # Fail closed: with anything unjudged, neither a rewrite (it could drop a red) nor a close is safe.
        return {"action": "refuse", "reason": f"{len(doc['unknown'])} check(s) could not be judged"}
    if not findings:
        if existing and existing.get("state") == "open":
            return {"action": "close", "body": render_clear_body(doc),
                    "comment": f"All judged workflows were green at {doc.get('evaluatedAt', 'unknown')}. Closing; the observer "
                               "reopens this issue if one goes red.\n"}
        return {"action": "noop", "reason": "nothing red and no open report issue"}
    current = digest(findings)
    previous = parse_marker(existing.get("body")) if existing else None
    if existing is not None and previous and previous.get("digest") == current:
        return {"action": "noop", "reason": "red set unchanged"
                + ("" if existing.get("state") == "open" else " (issue closed; not reopened)")}
    body = render_body(doc)
    if existing is None:
        return {"action": "create", "body": body}
    now_keys = {f["key"] for f in findings}
    if previous and isinstance(previous.get("keys"), list):
        before = set(previous["keys"])
        added, removed = sorted(now_keys - before), sorted(before - now_keys)
        comment = [f"Red set changed: {len(added)} newly red, {len(removed)} cleared, {len(now_keys)} red now."]
        comment += [f"- newly red {code(k)}" for k in added[:50]] + [f"- cleared {code(k)}" for k in removed[:50]]
        if len(added) > 50 or len(removed) > 50:
            comment.append("- (list truncated; the body holds the current set)")
    else:
        comment = [f"Red set changed: {len(now_keys)} red now (previous set not recorded in full)."]
    return {"action": "update" if existing.get("state") == "open" else "reopen", "body": body,
            "comment": "\n".join(comment)[:BODY_LIMIT] + "\n"}


def gh_write(args: list[str], payload: dict | None = None, *, allow_status: tuple[str, ...] = ()) -> str:
    gh = os.environ.get("MAIN_HEALTH_GH", "gh")
    cmd = [gh, "api", *args] + (["--input", "-"] if payload is not None else [])
    proc = subprocess.run(cmd, input=json.dumps(payload) if payload is not None else None,
                          capture_output=True, text=True, timeout=GH_TIMEOUT, check=False)
    if proc.returncode:
        if any(s in (proc.stdout + proc.stderr) for s in allow_status):
            return ""
        raise RuntimeError(f"gh api call failed (exit {proc.returncode})")
    return proc.stdout


def find_report_issue(repo: str) -> dict | None:
    """The single issue with REPORT_TITLE and REPORT_LABEL. Two open matches are a hard error."""
    out = gh_write([f"repos/{repo}/issues?labels={REPORT_LABEL}&state=all&per_page=100", "--paginate", "--jq", ".[]"])
    matches = []
    for line in out.splitlines():
        if line.strip():
            issue = json.loads(line)
            if "pull_request" not in issue and issue.get("title") == REPORT_TITLE:
                matches.append(issue)
    open_ones = [i for i in matches if i.get("state") == "open"]
    if len(open_ones) > 1:
        raise RuntimeError(f"{len(open_ones)} open issues titled {REPORT_TITLE!r}: refusing to guess which to update")
    if open_ones:
        return open_ones[0]
    return max(matches, key=lambda i: i["number"]) if matches else None


def apply_plan(repo: str, plan: dict, existing: dict | None) -> str:
    action = plan["action"]
    if action in ("noop", "refuse"):
        return f"{action}: {plan['reason']}"
    if action in ("create", "reopen", "update"):
        gh_write([f"repos/{repo}/labels", "-X", "POST", "-f", f"name={REPORT_LABEL}", "-f", "color=d93f0b",
                  "-f", "description=Scheduled main-health report (read-only observer)"],
                 allow_status=("already_exists", "422"))
    if action == "create":
        created = json.loads(gh_write([f"repos/{repo}/issues"], {"title": REPORT_TITLE, "body": plan["body"], "labels": [REPORT_LABEL]}))
        return f"created #{created['number']}"
    number = existing["number"]
    if action == "close":
        gh_write([f"repos/{repo}/issues/{number}/comments"], {"body": plan["comment"]})
        gh_write([f"repos/{repo}/issues/{number}", "-X", "PATCH"], {"state": "closed", "state_reason": "completed", "body": plan["body"]})
        return f"closed #{number}"
    patch = {"body": plan["body"]}
    if action == "reopen":
        patch["state"] = "open"
    gh_write([f"repos/{repo}/issues/{number}", "-X", "PATCH"], patch)
    gh_write([f"repos/{repo}/issues/{number}/comments"], {"body": plan["comment"]})
    return f"{'reopened' if action == 'reopen' else 'updated'} #{number}"


def cmd_report(args: argparse.Namespace) -> int:
    try:
        doc = json.loads(Path(args.findings).read_text(encoding="utf-8"))
        if doc.get("schema") != FINDINGS_SCHEMA or not isinstance(doc.get("findings"), list) or not isinstance(doc.get("unknown"), list):
            raise NoVerdict("findings file is not a main-health findings document")
    except (NoVerdict, OSError, json.JSONDecodeError) as exc:
        print(f"main-health-observer: NO VERDICT: {exc.__class__.__name__}: {exc}", file=sys.stderr)
        return NO_VERDICT
    existing = find_report_issue(args.repo)
    plan = plan_report(doc, existing)
    print(f"main-health-observer: plan={plan['action']}")
    if args.dry_run:
        print(json.dumps(plan, indent=2))
    else:
        print(f"main-health-observer: {apply_plan(args.repo, plan, existing)}")
    if plan["action"] == "refuse":
        print(f"main-health-observer: NO VERDICT: {plan['reason']}; the issue is untouched and this run fails", file=sys.stderr)
        for u in doc["unknown"]:
            print(f"  UNKNOWN {u['workflow']} ({u['source']}): {u['why']}", file=sys.stderr)
        return NO_VERDICT
    return 0


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="command", required=True)

    plan = sub.add_parser("plan", help="derive the check list from the workflows; --check compares it with the observer workflow")
    plan.add_argument("--root", default=".")
    plan.add_argument("--json", action="store_true")
    plan.add_argument("--check", action="store_true", help="exit 1 if the observer workflow differs from the derived list")
    plan.add_argument("--write", action="store_true", help="rewrite the GENERATED regions of the observer workflow")
    plan.set_defaults(func=cmd_plan)

    col = sub.add_parser("collect", help="judge fresh results (MAIN_HEALTH_NEEDS) and latest runs; write findings.json")
    col.add_argument("--root", default=".")
    col.add_argument("--repo", default=REPORT_REPO)
    col.add_argument("--run-id", default=os.environ.get("GITHUB_RUN_ID"))
    col.add_argument("--out", required=True)
    col.add_argument("--now", default=None, help="ISO timestamp override, for tests")
    col.set_defaults(func=cmd_collect)

    rep = sub.add_parser("report", help="open, update or close the single report issue")
    rep.add_argument("--findings", required=True)
    rep.add_argument("--repo", default=REPORT_REPO)
    rep.add_argument("--dry-run", action="store_true")
    rep.set_defaults(func=cmd_report)

    args = ap.parse_args(argv)
    try:
        return args.func(args)
    except RuntimeError as exc:
        print(f"main-health-observer: ERROR: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
