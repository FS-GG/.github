#!/usr/bin/env python3
"""Assert every schedule-only workflow job that runs the coordination engine builds it first.

Refs #4730 (main health). `coord-board-archive` and `projects-audit` were red on every scheduled run
from 2026-08-16 to 2026-10-30 (the whole retained history) with `fsgg-coord: no fsgg-coord engine
found` (exit 69), and nothing before merge could have said so.

THE DEFECT THIS CLOSES. `scripts/fsgg-coord` is a resolver, not an engine. In THIS repository its
only engine on a hosted runner is a source build under `src/FS.GG.Coord.Cli/bin/Release/`
(ADR-0034 decision 2: `.github` never consumes the feed). Commit 77938c5a (2026-08-15, "graphql:
retire raw compatibility boundary") moved `coord-board-archive.py` and `projects-audit.sh` from raw
GraphQL onto `scripts/fsgg-coord graphql ...` and did not give either workflow a build step.
`repos-audit.yml` already built the engine, which is why it kept working and why the omission was
invisible by example.

WHY A GATE AND NOT ONLY THE FIX. A job that triggers on `pull_request` or `push` runs before or at
merge, so a missing engine fails there, loudly, on the PR that introduced it. A workflow whose only
triggers are `schedule` / `workflow_dispatch` (and the like) never runs until a clock fires it, so
the same omission is merged green and stays red for months. Those are exactly the workflows this
gate looks at. A path-filtered push workflow is deliberately out of scope: it already exercises its
own steps.

WHAT IT ASSERTS, per job in a workflow that declares no pull_request / pull_request_target / push /
workflow_call trigger:

  1. Find the first step whose `run:` executes the engine: it names `scripts/fsgg-coord`, or a
     script under scripts/ whose own code (comments stripped) resolves `fsgg-coord` or sets
     FSGG_COORD_BIN / FSGG_COORD_ENGINE_BIN (one level, which is how the two defects reached it).
  2. Require an EARLIER step in the same job that provisions it: a `dotnet build|publish|run`
     of FS.GG.Coord.Cli, a `dotnet tool restore|install`, or a step/job/workflow `env:` that names
     FSGG_COORD_ENGINE_BIN. "Earlier" is the point: a build after the first use is the same failure.

It is a static reading of the working tree: no API, no network, no credentials. Examining zero
workflows is a failure to audit, not a clean audit.

Usage: check-scheduled-engine-provisioning.py [--root <dir>]
Exit: 0 = every engine-running job in a schedule-only workflow provisions it first; 1 = at least one
does not; 3 = no verdict, PERMANENT (an unparsable workflow or an audit that examined nothing).
There is no exit 2: a static gate has no retryable condition.
"""
from __future__ import annotations

import argparse
import glob
import os
import re
import sys

import yaml

OK, FINDING, NO_VERDICT = 0, 1, 3

# Triggers that put a workflow in front of a pull request or a push, i.e. run it before or at merge.
# `workflow_call` is included because its trigger context belongs to the caller, which is judged on
# its own triggers.
PRE_MERGE = ("pull_request", "pull_request_target", "push", "workflow_call")

ENGINE_NAME = re.compile(r"fsgg-coord(?![-\w.])|FSGG_COORD_(?:ENGINE_)?BIN")
# A Python script mentions `scripts/fsgg-coord` in docstrings all the time (check-claim-generation.py
# does, and never runs it), so prose cannot count. It RESOLVES the engine only through the env var
# that overrides it or a quoted path component naming it.
ENGINE_NAME_PY = re.compile(r"FSGG_COORD_(?:ENGINE_)?BIN|[\"']fsgg-coord[\"']")
PROVISION_RUN = re.compile(
    r"dotnet\s+(?:build|publish|run)\b[^\n]*FS\.GG\.Coord\.Cli|dotnet\s+tool\s+(?:restore|install)\b"
)
PROVISION_ENV = re.compile(r"FSGG_COORD_ENGINE_BIN")


class GateError(Exception):
    """A condition under which the gate must fail rather than skip. Maps to exit 3."""


def triggers(doc: dict) -> dict:
    # PyYAML resolves the bare key `on` to boolean True (YAML 1.1); same handling as
    # scripts/check-workflow-timeouts.py.
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


def strip_comments(text: str) -> str:
    return "\n".join(
        line for line in text.splitlines() if not line.lstrip().startswith("#")
    )


def engine_scripts(root: str) -> set[str]:
    """Repo-relative paths of scripts under scripts/ that resolve the engine in their own code."""
    found: set[str] = set()
    for path in sorted(glob.glob(os.path.join(root, "scripts", "*"))):
        base = os.path.basename(path)
        if not os.path.isfile(path) or base in ("fsgg-coord", "fsgg-coord-guards.sh"):
            continue
        try:
            with open(path, encoding="utf-8") as fh:
                code = strip_comments(fh.read())
        except (UnicodeDecodeError, OSError):
            continue
        pattern = ENGINE_NAME_PY if base.endswith(".py") else ENGINE_NAME
        if pattern.search(code):
            found.add(f"scripts/{base}")
    return found


def runs_engine(run: str, scripts: set[str]) -> bool:
    code = strip_comments(run)
    if re.search(r"scripts/fsgg-coord(?![-\w.])", code):
        return True
    return any(re.search(re.escape(s) + r"(?![-\w.])", code) for s in scripts)


def provisions(step: dict, wf_env: object, job_env: object) -> bool:
    run = strip_comments(str(step.get("run") or ""))
    if PROVISION_RUN.search(run):
        return True
    for env in (wf_env, job_env, step.get("env")):
        if isinstance(env, dict) and any(PROVISION_ENV.search(str(k)) for k in env):
            return True
    return False


def audit(root: str) -> tuple[list[str], int]:
    wf_dir = os.path.join(root, ".github", "workflows")
    files = sorted(
        f for ext in ("yml", "yaml") for f in glob.glob(os.path.join(wf_dir, f"*.{ext}"))
    )
    if not files:
        raise GateError(f"{wf_dir} contains no workflow files — there is nothing to audit")
    scripts = engine_scripts(root)
    findings: list[str] = []
    examined = 0
    for path in files:
        where = os.path.relpath(path, root)
        try:
            with open(path, encoding="utf-8") as fh:
                doc = yaml.safe_load(fh)
        except yaml.YAMLError as e:
            raise GateError(f"{where}: not parsable as YAML — {e}") from e
        if not isinstance(doc, dict):
            raise GateError(f"{where}: not a YAML mapping")
        if any(t in triggers(doc) for t in PRE_MERGE):
            continue
        jobs = doc.get("jobs")
        if not isinstance(jobs, dict) or not jobs:
            raise GateError(f"{where}: declares no jobs")
        for name, job in jobs.items():
            examined += 1
            steps = job.get("steps") if isinstance(job, dict) else None
            if not isinstance(steps, list):
                continue
            provisioned = False
            for i, step in enumerate(steps):
                if not isinstance(step, dict):
                    continue
                if provisions(step, doc.get("env"), job.get("env")):
                    provisioned = True
                if runs_engine(str(step.get("run") or ""), scripts) and not provisioned:
                    label = step.get("name") or step.get("id") or f"step {i + 1}"
                    findings.append(
                        f"{where} [{name}]: step '{label}' runs the coordination engine but no "
                        f"earlier step in the job builds it. Add actions/setup-dotnet and "
                        f"`dotnet build src/FS.GG.Coord.Cli/FS.GG.Coord.Cli.fsproj -c Release` "
                        f"before it (scripts/fsgg-coord exits 69 'no fsgg-coord engine found' on a "
                        f"hosted runner otherwise), or set FSGG_COORD_ENGINE_BIN."
                    )
                    break
    return findings, examined


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(
        description=__doc__.splitlines()[0],
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    ap.add_argument("--root", default=".", help="the working tree to audit (default: .)")
    args = ap.parse_args(argv)
    try:
        findings, examined = audit(args.root)
    except GateError as e:
        print(f"check-scheduled-engine-provisioning: no verdict — {e}", file=sys.stderr)
        return NO_VERDICT
    if examined == 0:
        print(
            "check-scheduled-engine-provisioning: no verdict — no schedule-only workflow job was "
            "examined; examining nothing is not a clean audit.",
            file=sys.stderr,
        )
        return NO_VERDICT
    for f in findings:
        print(f"::error::{f}")
    if findings:
        return FINDING
    print(
        f"check-scheduled-engine-provisioning: OK — {examined} job(s) in schedule-only workflows; "
        "every one that runs the engine builds it first"
    )
    return OK


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
