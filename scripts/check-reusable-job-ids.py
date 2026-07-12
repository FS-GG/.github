#!/usr/bin/env python3
"""Assert this PR does not silently rename or remove a job in a reusable workflow.

.github#549, epic #266. Found while working FS.GG.Audio#60 — the first FS-GG repo to require a
reusable-workflow-nested context.

A reusable workflow's JOB IDS ARE PUBLIC API. When a caller invokes one, GitHub names the resulting
check `<caller job> / <callee job>`, and a receiver's branch protection can REQUIRE that string.
FS.GG.Audio requires exactly this:

    lock-ranges / lock-ranges
                  ^^^^^^^^^^^ a job id inside THIS repo, tracked at the moving ref @main

Rename that job — an ordinary refactor of a workflow's own job id, in a repo whose CI is green,
reviewed by people with no reason to look downstream — and Audio's gate reports
`lock-ranges / <newname>` instead. The context Audio REQUIRES is never reported again, so GitHub
holds every pull request at "Expected — waiting for status to be reported", forever. Audio runs
`enforce_admins: true` with no required reviews, so there is no bypass: the whole repo stops merging,
no commit in Audio changed, and the cause is a commit here.

WHY THIS GATE, AND NOT THE ONE THE ISSUE ASKED FOR. #549 proposed reading each repo's
`branches/main/protection` and asserting every required context is produced. That check is not
implementable in GitHub Actions — by anyone, in any repo:

  - the protection endpoint requires `administration: read`;
  - `administration` IS NOT A VALID `permissions:` SCOPE for a workflow's GITHUB_TOKEN (declaring it
    is a workflow validation error — the run never starts);
  - and the org's dispatch App does not hold it either, which .github#463 already learned the hard
    way: coordination-propagate's protection probe 403'd on every receiver and had to be rewritten
    to ask the pull request instead.

So no workflow can read branch protection today. Rather than ship a gate that cannot authenticate,
this one asks a question that needs no credential at all — and it is the better question, because it
is asked HERE, on the PR that would cause the outage, instead of in the victim's repo afterwards.

WHAT IT ASSERTS
  For every workflow that declares `on: workflow_call`, at the BASE of this PR:
      every context name it published then, it still publishes now.

  A "published name" is what GitHub puts on the right-hand side of `<caller> / <callee>`: the job's
  `name:` if it has one, else its job id. So this catches all four ways to break a caller:

      renaming a job id            lock-ranges:      -> check-lock-ranges:
      renaming a job's `name:`     name: Lock ranges -> name: Lock-range check
      deleting a job
      removing `on: workflow_call` (or deleting the workflow) — every name it published is gone

  Adding a job is always safe, and is not reported.

IT IS A LOUD GATE, NOT A LOCKED DOOR. A rename may be exactly what you want — but it is a BREAKING
CONTRACT CHANGE, and it must be sequenced: land the receivers' protection change first, or accept
that you are about to stop their PRs from merging. reusable-job-id-coherence.yml therefore offers
the same explicit opt-out as architecture-map.yml (a label, or a PR-body marker), so that breaking a
contract is a decision somebody made rather than an omission nobody noticed.

Usage:
  check-reusable-job-ids.py [--root <dir>] --base <git-ref>
Exit: 0 = every published context name survives; 1 = at least one was renamed or removed (a breaking
change for any caller that requires it); 3 = no verdict, PERMANENT — a workflow that will not parse,
or a base ref that cannot be read.

"I could not check" must never share an exit code with "I checked, and it's fine" (#266) — nor with
"I checked, and it's broken" (#320).
"""
from __future__ import annotations

import argparse
import os
import subprocess
import sys
import traceback

import yaml

OK, FINDING, NO_VERDICT_PERMANENT = 0, 1, 3

WORKFLOW_DIR = ".github/workflows"


class GateError(Exception):
    """A condition under which the gate must fail rather than skip. Maps to exit 3."""


def git(root: str, *args: str) -> str:
    proc = subprocess.run(
        ["git", "-C", root, *args], capture_output=True, text=True, check=False,
    )
    if proc.returncode != 0:
        raise GateError(f"git {' '.join(args)} failed: {' '.join(proc.stderr.split())}")
    return proc.stdout


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
    doc["on"] misses it — and EVERY reusable workflow would look like a non-callee, making this gate
    silently vacuous: it would compare an empty set to an empty set and report green over any rename
    at all. Same trap, same handling, as scripts/check-workflow-permissions.py."""
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


def published_names(text: str, what: str) -> set[str]:
    """The context names this workflow publishes to callers — empty if it is not reusable.

    A published name is the right-hand half of GitHub's `<caller job> / <callee job>` check name:
    the job's `name:` if it has one, else its job id.
    """
    doc = load_yaml(text, what)
    if "workflow_call" not in triggers(doc):
        return set()
    jobs = doc.get("jobs")
    if not isinstance(jobs, dict) or not jobs:
        raise GateError(f"{what}: declares `on: workflow_call` but no `jobs:` — it publishes nothing")
    out = set()
    for job_id, job in jobs.items():
        if not isinstance(job, dict):
            raise GateError(f"{what} [{job_id}]: the job is not a YAML mapping")
        name = job.get("name")
        out.add(str(name) if isinstance(name, (str, int, float)) and str(name) else str(job_id))
    return out


def at_base(root: str, base: str, path: str) -> str | None:
    """The file's content at the base ref, or None if it did not exist there."""
    proc = subprocess.run(
        ["git", "-C", root, "show", f"{base}:{path}"],
        capture_output=True, text=True, check=False,
    )
    if proc.returncode != 0:
        err = " ".join(proc.stderr.split())
        # "does not exist" / "exists on disk, but not in" — the file is NEW in this PR. That is an
        # answer, not a failure: a workflow that did not exist at base published nothing to break.
        if "does not exist" in err or "exists on disk" in err:
            return None
        raise GateError(f"cannot read {path} at {base}: {err}")
    return proc.stdout


def base_workflow_files(root: str, base: str) -> list[str]:
    out = git(root, "ls-tree", "--name-only", base, f"{WORKFLOW_DIR}/")
    return [p for p in out.splitlines() if p.endswith((".yml", ".yaml"))]


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(
        description=__doc__.splitlines()[0],
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    ap.add_argument("--root", default=".", help="the working tree (default: .)")
    ap.add_argument("--base", required=True, help="the git ref to compare against (the merge-base)")
    args = ap.parse_args(argv)

    findings: list[str] = []
    reusable_seen = 0

    for path in base_workflow_files(args.root, args.base):
        old_text = at_base(args.root, args.base, path)
        if old_text is None:  # listed by ls-tree a moment ago; cannot vanish
            raise GateError(f"{path} is in {args.base} but could not be read")
        old = published_names(old_text, f"{path}@{args.base}")
        if not old:
            continue  # not a reusable workflow at base: it published nothing, so nothing can break
        reusable_seen += 1

        full = os.path.join(args.root, path)
        if os.path.isfile(full):
            with open(full, encoding="utf-8") as fh:
                new = published_names(fh.read(), path)
        else:
            new = set()  # the workflow was DELETED — every name it published is gone

        for lost in sorted(old - new):
            gained = sorted(new - old)
            findings.append(
                f"{path}: the reusable workflow no longer publishes the context name {lost!r}"
                + (f" (it now publishes {', '.join(repr(g) for g in gained)})" if gained else
                   " (the workflow, or that job, was removed entirely)")
                + f". GitHub names a called workflow's check `<caller job> / {lost}`, so ANY repo "
                f"whose branch protection requires that context will never see it reported again — "
                f"every pull request there hangs at \"Expected — waiting for status to be "
                f"reported\", with no commit in that repo having changed. FS.GG.Audio requires "
                f"`lock-ranges / lock-ranges` today. A reusable workflow's job ids are API: see "
                f"docs/coordination/reusable-workflow-contract.md."
            )

    if reusable_seen == 0:
        raise GateError(
            f"no workflow at {args.base} declares `on: workflow_call`, so this gate compared nothing. "
            f"Examining nothing is a failure to audit, not a clean audit — this repo is the org's "
            f"reusable-workflow authority and always has callees."
        )

    if findings:
        for f in findings:
            print(f"::error::check-reusable-job-ids: {f}", file=sys.stderr)
        print(
            f"\n{len(findings)} published context name(s) renamed or removed, across "
            f"{reusable_seen} reusable workflow(s). This is a BREAKING CONTRACT CHANGE.",
            file=sys.stderr,
        )
        return FINDING

    print(f"ok: every context name published by {reusable_seen} reusable workflow(s) survives.")
    return OK


def cli(argv: list[str]) -> int:
    """Guarantee the exit code is a VERDICT, never an accident.

    Python exits 1 on any uncaught exception — and 1 is this gate's "you have made a breaking
    contract change". A crash would therefore be dressed up as a confident, specific, WRONG claim
    that the author broke six repos' CI. "I could not check" must never share a code with "I
    checked, and it's broken" (#266, #320).
    """
    try:
        return main(argv)
    except GateError as e:
        print(f"::error::check-reusable-job-ids: no verdict — {e}", file=sys.stderr)
        return NO_VERDICT_PERMANENT
    except Exception:  # noqa: BLE001 — deliberately broad; see the docstring
        traceback.print_exc()
        print(
            "::error::check-reusable-job-ids: the gate crashed, so it has NO VERDICT. This is not a "
            "finding about any job id — it is a bug in the gate. See the traceback above.",
            file=sys.stderr,
        )
        return NO_VERDICT_PERMANENT


if __name__ == "__main__":
    sys.exit(cli(sys.argv[1:]))
