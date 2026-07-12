#!/usr/bin/env python3
"""Assert every job in this repo's workflows declares a `timeout-minutes` bound.

.github#541, epic #266 (coherence gates that fail open). Found while adopting the lock-range gate in
FS.GG.Game#153 — the first receiver to wire .github#495.

THE DEFECT THIS CLOSES. A job that declares no `timeout-minutes` inherits GitHub's default of **360
minutes** — six hours. Every one of this repo's workflows was in that state: 31 workflows, not one
job bounded. The realistic hang is not the check itself but the `actions/checkout` of FS-GG/.github
that each of them does first; when it wedges, the job sits there for six hours.

WHY THE RECEIVER CANNOT FIX THIS, WHICH IS WHAT MAKES IT OURS. A job that calls a reusable workflow
accepts only `name`, `needs`, `if`, `permissions`, `uses`, `with`, `secrets`, `strategy`, and
`concurrency`. `timeout-minutes` is NOT among them — it is a normal-job key, and setting it on a
`uses:` job is a workflow validation error. So a caller has no way to bound the job it is calling.
The bound must be declared INSIDE the reusable workflow or it does not exist anywhere.

That asymmetry is the whole reason this gate lives here and not in the receivers. FS.GG.Game's
`gate.yml` bounds every one of its eight hand-authored jobs (5..20 minutes); adopting
`lock-range-coherence.yml` introduced the repo's ONLY job that can hang for six hours — in a
workflow whose `concurrency` group is `cancel-in-progress: true`, so a wedged run also holds the
group. The irony is that these are the jobs LEAST likely to need the headroom: `lock-range-coherence`
is explicitly static — committed files only, no SDK, no restore, no network — and finishes in
seconds. A six-hour ceiling on a ten-second job is not a safety margin; it is the absence of one.

WHAT IT ASSERTS, over every workflow in <root>/.github/workflows:

  1. A NORMAL job (one with `steps:`) declares `timeout-minutes`, as a literal positive integer no
     greater than --max-minutes (default 60).

  2. A `uses:` job declares NO `timeout-minutes`. This is not a stylistic preference: GitHub rejects
     the workflow. The rule reads as the mirror image of (1) on purpose — the two together are the
     whole content of #541. A caller cannot bound its callee, therefore the callee must bound
     itself.

  3. A REUSABLE workflow (`on: workflow_call`) is held to (1) with a louder message, because its job
     timeout is the ONLY bound anyone can ever get: no caller can supply one, so an unbounded job
     here exports a six-hour ceiling into every repo that adopts it.

WHY A LITERAL, AND WHY A CEILING
  An expression (`timeout-minutes: ${{ inputs.t }}`) is not a bound this gate can prove — the value
  arrives from a caller at run time, and "unbounded" is among the things it could be. Demand a
  literal. And a literal of 360 is the six-hour hole spelled out longhand rather than inherited, so
  the ceiling is what makes the rule mean anything. --max-minutes exists to be raised deliberately,
  by someone who writes down why, rather than drifted past.

FAILS CLOSED (epic #266). Auditing ZERO workflows, or zero jobs, is exit 3 — examining nothing is a
failure to audit, not a clean audit. So is a workflow that will not parse.

THIS GATE IS STATIC, so it never exits 2. It reads the working tree and nothing else: no API, no
network, no run history, no credentials. There is no condition under which "try again" is the right
advice, and a gate that can emit a retryable verdict it can never mean is a gate whose exit-code
contract lies. Exit 2 is therefore deliberately absent from the vocabulary rather than reserved.

Usage:
  check-workflow-timeouts.py [--root <dir>] [--max-minutes <n>]
Exit: 0 = every job is bounded; 1 = at least one job is unbounded, over-bounded, or illegal; 3 = no
verdict, PERMANENT — an unparsable workflow, or an audit that examined nothing.

"I could not check" must never share an exit code with "I checked, and it's fine" (#266) — nor with
"I checked, and it's broken" (#320).
"""
from __future__ import annotations

import argparse
import glob
import os
import sys
import traceback

import yaml

OK, FINDING, NO_VERDICT_PERMANENT = 0, 1, 3

# GitHub's default when a job declares no `timeout-minutes`. The number this gate exists to abolish.
GITHUB_DEFAULT_TIMEOUT_MINUTES = 360

# The keys GitHub allows on a job that calls a reusable workflow. `timeout-minutes` is not one of
# them — that absence IS #541.
CALLER_JOB_KEYS = frozenset(
    {"name", "needs", "if", "permissions", "uses", "with", "secrets", "strategy", "concurrency"}
)


class GateError(Exception):
    """A condition under which the gate must fail rather than skip. Maps to exit 3."""


def load_yaml(text: str, what: str) -> dict:
    try:
        doc = yaml.safe_load(text)
    except yaml.YAMLError as e:
        raise GateError(f"{what}: not parsable as YAML — {e}") from e
    if not isinstance(doc, dict):
        raise GateError(f"{what}: not a YAML mapping")
    return doc


def triggers(doc: dict) -> dict:
    """The `on:` block. PyYAML resolves the bare key `on` to the boolean True (YAML 1.1), so a plain
    doc["on"] misses it and every reusable workflow would look like a non-callee. Same trap, same
    handling, as scripts/check-workflow-permissions.py."""
    for key in ("on", True):
        if key in doc:
            got = doc[key]
            if isinstance(got, dict):
                return got
            if isinstance(got, list):
                return {k: None for k in got}
            if isinstance(got, str):
                return {got: None}
    return {}


def workflow_files(root: str) -> list[str]:
    d = os.path.join(root, ".github", "workflows")
    if not os.path.isdir(d):
        raise GateError(f"{d} is not a directory — there are no workflows to audit")
    files = sorted(
        f for ext in ("yml", "yaml") for f in glob.glob(os.path.join(d, f"*.{ext}"))
    )
    if not files:
        raise GateError(f"{d} contains no workflow files — there is nothing to audit")
    return files


def check_timeout(raw: object, max_minutes: int) -> str | None:
    """The reason `timeout-minutes: <raw>` is not a provable bound, or None if it is one.

    `bool` is checked before `int` on purpose: in Python `True` IS an `int` (it equals 1), so
    `timeout-minutes: true` would otherwise sail through as a one-minute bound.
    """
    if isinstance(raw, bool) or not isinstance(raw, int):
        return (
            f"`timeout-minutes: {raw!r}` is not a literal integer. An expression is resolved at run "
            f"time by a value this gate cannot see — and 'unbounded' is among the things it could "
            f"be — so it is not a bound anyone can prove. Declare a literal."
        )
    if raw <= 0:
        return f"`timeout-minutes: {raw}` is not a positive number of minutes"
    if raw > max_minutes:
        return (
            f"`timeout-minutes: {raw}` exceeds the ceiling of {max_minutes}. "
            + (
                f"{GITHUB_DEFAULT_TIMEOUT_MINUTES} is GitHub's unbounded default spelled out "
                "longhand, not a bound. "
                if raw == GITHUB_DEFAULT_TIMEOUT_MINUTES
                else ""
            )
            + "Size the job to what it does, or raise --max-minutes deliberately and say why."
        )
    return None


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(
        description=__doc__.splitlines()[0],
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    ap.add_argument("--root", default=".", help="the working tree to audit (default: .)")
    ap.add_argument(
        "--max-minutes", type=int, default=60,
        help="the largest timeout a job may declare (default: 60). Raise it deliberately.",
    )
    args = ap.parse_args(argv)

    if args.max_minutes <= 0:
        raise GateError(f"--max-minutes {args.max_minutes} is not a positive number of minutes")

    findings: list[str] = []
    jobs_seen = 0

    for path in workflow_files(args.root):
        where = os.path.relpath(path, args.root)
        with open(path, encoding="utf-8") as fh:
            doc = load_yaml(fh.read(), where)

        reusable = "workflow_call" in triggers(doc)
        jobs = doc.get("jobs")
        if not isinstance(jobs, dict) or not jobs:
            raise GateError(f"{where}: declares no `jobs:` mapping — it cannot run")

        for job_id, job in jobs.items():
            if not isinstance(job, dict):
                raise GateError(f"{where} [{job_id}]: the job is not a YAML mapping")
            jobs_seen += 1
            subject = f"{where} [{job_id}]"
            declared = "timeout-minutes" in job

            # A `uses:` job CANNOT carry a timeout — GitHub rejects the workflow. The bound for the
            # work it triggers lives in the callee, which this same audit holds to rule (1).
            if "uses" in job:
                if declared:
                    findings.append(
                        f"{subject}: declares `timeout-minutes` on a job that calls a reusable "
                        f"workflow (`uses:`). GitHub REJECTS this — a caller job accepts only "
                        f"{', '.join(sorted(CALLER_JOB_KEYS))}, and the workflow will not validate. "
                        f"Remove it; the bound belongs in the callee, which cannot be bound from "
                        f"here. That asymmetry is .github#541."
                    )
                else:
                    print(f"  ok   {subject:<58} uses: -> bound by its callee")
                continue

            if not declared:
                findings.append(
                    f"{subject}: declares no `timeout-minutes`, so it inherits GitHub's default of "
                    f"{GITHUB_DEFAULT_TIMEOUT_MINUTES} minutes (six hours)."
                    + (
                        " This is a REUSABLE workflow: no caller can supply a timeout for it — "
                        "`timeout-minutes` is not a legal key on a `uses:` job — so this job's own "
                        "bound is the only one anyone can ever get, and without it every repo that "
                        "adopts this workflow inherits the six-hour ceiling."
                        if reusable
                        else ""
                    )
                )
                continue

            reason = check_timeout(job["timeout-minutes"], args.max_minutes)
            if reason:
                findings.append(f"{subject}: {reason}")
            else:
                print(
                    f"  ok   {subject:<58} timeout-minutes: {job['timeout-minutes']}"
                    + ("  (reusable)" if reusable else "")
                )

    if jobs_seen == 0:
        raise GateError(
            "audited 0 job(s) — examining nothing is a failure to audit, not a clean audit"
        )

    if findings:
        for f in findings:
            print(f"::error::check-workflow-timeouts: {f}", file=sys.stderr)
        print(
            f"\n{len(findings)} unbounded or illegal job(s), of {jobs_seen} job(s) audited.",
            file=sys.stderr,
        )
        return FINDING

    print(f"ok: every job declares a timeout — {jobs_seen} job(s) audited.")
    return OK


def cli(argv: list[str]) -> int:
    """Guarantee the exit code is a VERDICT, never an accident.

    Python exits 1 on any uncaught exception — and 1 is this gate's "a job is unbounded". So a crash
    anywhere in here would be dressed up by timeout-coherence.yml as a specific, confident, WRONG
    finding about somebody's job. That is the conflation the exit-code contract exists to prevent
    (#266, #320): "I could not check" must never share a code with "I checked, and it's broken".
    """
    try:
        return main(argv)
    except GateError as e:
        print(f"::error::check-workflow-timeouts: no verdict — {e}", file=sys.stderr)
        return NO_VERDICT_PERMANENT
    except Exception:  # noqa: BLE001 — deliberately broad; see the docstring
        traceback.print_exc()
        print(
            "::error::check-workflow-timeouts: the gate crashed, so it has NO VERDICT. This is not "
            "a finding about any job's timeout — it is a bug in the gate. See the traceback above.",
            file=sys.stderr,
        )
        return NO_VERDICT_PERMANENT


if __name__ == "__main__":
    sys.exit(cli(sys.argv[1:]))
