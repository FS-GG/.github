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

WHAT COUNTS AS PROVISIONING, precisely (the first cut accepted too much): a `dotnet build|publish|run`
naming FS.GG.Coord.Cli with an explicit Release configuration (the shim looks under bin/Release/, and
`dotnet build` defaults to Debug); a `dotnet tool install` that installs fs.gg.coord.cli (not any tool);
`dotnet tool restore` only where `.config/dotnet-tools.json` declares the engine; or
FSGG_COORD_ENGINE_BIN. Backslash-continued commands are read as one line. The steps of a LOCAL composite action (`uses: ./...`) are
inlined, so a build or an engine call inside one is judged in order.

KNOWN FALSE NEGATIVE, stated rather than hidden: a step that runs a script which runs ANOTHER script which
runs the engine (two levels) is not seen. A path-reference closure was tried and rejected: it matched
docstrings and data (policy-checkers.json) and turned 2 true findings into 18 false ones. Closing it needs a
call graph that knows invocation from mention.

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
import json
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
# What provisions the engine the shim can find. Three false negatives of the first cut are closed here:
#   * a build must be a RELEASE build: the shim looks at src/FS.GG.Coord.Cli/bin/Release/..., and
#     `dotnet build` defaults to Debug, so a bare or `-c Debug` build leaves the same exit 69 behind;
#   * `dotnet tool install` counts only when it installs the engine (fs.gg.coord.cli), not any tool;
#   * `dotnet tool restore` counts only where the repo's `.config/dotnet-tools.json` names the engine.
BUILD_CMD = re.compile(r"dotnet\s+(?:build|publish|run)\b([^\n]*)")
RELEASE_FLAG = re.compile(r"(?:^|\s)(?:-c|--configuration)(?:\s+|=)Release\b", re.IGNORECASE)
TOOL_RESTORE = re.compile(r"dotnet\s+tool\s+restore\b")
TOOL_INSTALL = re.compile(r"dotnet\s+tool\s+install\b([^\n]*)")
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
    """Drop comment lines, then join backslash-continued lines into one logical line, so a command split
    over several physical lines (`dotnet build \\` / `project.fsproj \\` / `-c Release`) is read whole.
    The order matters: a comment line ending in a backslash does not continue in shell."""
    kept = "\n".join(line for line in text.splitlines() if not line.lstrip().startswith("#"))
    return re.sub(r"\\[ \t]*\r?\n[ \t]*", " ", kept)


def restore_provisions(root: str) -> bool:
    """`dotnet tool restore` provisions the engine only where the repo's tool manifest declares it.

    The restore resolves the manifest at `.config/dotnet-tools.json`; if that file does not name the
    engine (this repo has no manifest at all: its engine is a source build), a restore restores
    nothing the shim looks for. Accepting it unconditionally was a false negative.
    """
    path = os.path.join(root, ".config", "dotnet-tools.json")
    try:
        with open(path, encoding="utf-8") as fh:
            tools = (json.load(fh) or {}).get("tools") or {}
    except (OSError, ValueError):
        return False
    for package, entry in tools.items():
        commands = (entry or {}).get("commands") or [] if isinstance(entry, dict) else []
        if str(package).lower() == "fs.gg.coord.cli" or "fsgg-coord-engine" in commands:
            return True
    return False


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


def provisions_run(run: str, restore_ok: bool = False) -> bool:
    """Does this shell text build, install or restore an engine the shim can resolve?"""
    code = strip_comments(run)
    for m in BUILD_CMD.finditer(code):
        rest = m.group(1)
        if "FS.GG.Coord.Cli" in rest and RELEASE_FLAG.search(rest):
            return True
    for m in TOOL_INSTALL.finditer(code):
        if "fs.gg.coord.cli" in m.group(1).lower():
            return True
    return restore_ok and bool(TOOL_RESTORE.search(code))


def provisions(step: dict, wf_env: object, job_env: object, restore_ok: bool = False) -> bool:
    if provisions_run(str(step.get("run") or ""), restore_ok):
        return True
    for env in (wf_env, job_env, step.get("env")):
        if isinstance(env, dict) and any(PROVISION_ENV.search(str(k)) for k in env):
            return True
    return False


def expand_steps(steps: list, root: str, depth: int = 0) -> list:
    """Inline the steps of every LOCAL composite action (`uses: ./path`), in order, so a build or an
    engine call hidden inside one is judged where it runs. A local action that is missing or not a
    composite is left as a plain step (it cannot be read, and it is not claimed to provision)."""
    out: list = []
    for step in steps:
        uses = step.get("uses") if isinstance(step, dict) else None
        if isinstance(uses, str) and uses.startswith("./") and depth < 4:
            base = os.path.join(root, uses[2:])
            for name in ("action.yml", "action.yaml"):
                path = os.path.join(base, name)
                if os.path.isfile(path):
                    try:
                        with open(path, encoding="utf-8") as fh:
                            action = yaml.safe_load(fh)
                    except yaml.YAMLError as e:
                        raise GateError(f"{os.path.relpath(path, root)}: not parsable as YAML — {e}") from e
                    inner = ((action or {}).get("runs") or {}).get("steps")
                    if isinstance(inner, list):
                        label = step.get("name") or uses
                        for sub in expand_steps(inner, root, depth + 1):
                            if isinstance(sub, dict):
                                sub = {**sub, "name": sub.get("name") or f"{label} (composite)"}
                            out.append(sub)
                        break
            else:
                out.append(step)
            continue
        out.append(step)
    return out


def audit(root: str) -> tuple[list[str], int]:
    wf_dir = os.path.join(root, ".github", "workflows")
    files = sorted(
        f for ext in ("yml", "yaml") for f in glob.glob(os.path.join(wf_dir, f"*.{ext}"))
    )
    if not files:
        raise GateError(f"{wf_dir} contains no workflow files — there is nothing to audit")
    scripts = engine_scripts(root)
    restore_ok = restore_provisions(root)
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
            steps = expand_steps(steps, root)
            for i, step in enumerate(steps):
                if not isinstance(step, dict):
                    continue
                if provisions(step, doc.get("env"), job.get("env"), restore_ok):
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
