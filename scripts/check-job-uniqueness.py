#!/usr/bin/env python3
"""Assert this PR does not make two of this repo's OWN top-level jobs newly share a check-run name.

.github#2354 AC-2, filed as .github#2374 by the independent review of PR #2371 (which fixed AC-1/AC-3
of #2354 but explicitly deferred this gate). Epic #266 (coherence gates that fail open).

THE DEFECT THIS CLOSES. `architecture-map.yml`, `coord-board-reconcile.yml` and `feed-autofix.yml` all
declared a top-level job id `reconcile`, with no job-level `name:` to tell them apart. GitHub names a
plain job's check run after its `name:` if it has one, else its job id — with NO workflow-file prefix
— so all three produced an IDENTICAL check-run name: `reconcile`. Branch protection's required-status-
check matching is purely string-based: it required `reconcile`, and ANY of the three satisfied it,
regardless of which one actually ran. Confirmed live twice (`.github#2352`'s PR #2353, and
`.github#2347`'s PR #2367 — see `.github#2354`'s comments). PR #2371 gave two of the three a
disambiguating `name:`, leaving `coord-board-reconcile.yml`'s job bare because THAT is the one branch
protection's required `reconcile` context actually means.

THIS IS THE SAME HAZARD `.github#549`'s `check-reusable-job-ids.py` guards for a reusable workflow's
PUBLISHED (`<caller> / <callee>`) names — but that gate is explicitly scoped to jobs with `uses:`, and
a plain top-level job has no caller prefix to disambiguate it in the first place. Nothing audited the
top-level case at all.

WHY THIS IS A NON-REGRESSION GATE, NOT AN ABSOLUTE ONE — READ THIS BEFORE WIDENING IT. The literal ask
("no two top-level jobs anywhere in this repo may ever share a name") does not hold today, and cannot
be made to hold by this PR alone. Measured directly against this repo's own `.github/workflows/*.yml`
at the time this gate was written: 8 names are shared by 2 or more top-level jobs across DIFFERENT
files — `fixture` (48 jobs; the org-wide "prove the gate can say NO" first step, named identically by
strong, deliberate convention in nearly every `*-coherence.yml`), `coherence` (5), `verify` (3),
`selftest` (3), `audit`/`dogfood`/`freshness`/`monopoly` (2 each). None of them is in this repo's
required status checks today (`projection`, `roster-closure`, `drift`, `reconcile`, and the nested
`contract-coherence / coherence` — verified via `gh api repos/FS-GG/.github/branches/main/protection`,
also quoted in `coherence.yml`'s `claim-generation` job comment), so none is exploitable YET — but an
absolute gate would still be born red, on ~65 jobs, for a hazard nobody introduced today. Worse: it
would then RED EVERY FUTURE PR THAT TOUCHES ANY WORKFLOW FILE AT ALL, for a pre-existing shared name
that PR never touched — the `#266` fail-open class turned inside out into a fail-SHUT that trains
everyone to ignore this gate within a week.

RENAMING SIXTY-FIVE JOBS TO CLEAR THAT BACKLOG FIRST WAS CONSIDERED AND REJECTED HERE. It is a mechanical
bulk rename across most of this repo's workflow surface, for a Medium-severity, `lightweight`-routed
gate-addition item — disproportionate blast radius for the hazard being closed, and it would need its
own `diff-audit` receipt. Filed as a separate, narrower hardening follow-up instead of folded in here:
`.github#2447` tracks disambiguating (or otherwise closing the gap on) the 8 names above.

So — matching `check-reusable-job-ids.py`'s own precedent exactly (a diff over base vs head, not a
scan of head alone) — this gate asserts the ONE thing achievable without that rename and without
leaving the repo permanently red: **this PR must not make a name newly ambiguous.** A name is "newly
ambiguous" when, at HEAD, two or more top-level jobs resolve to it, but at the MERGE-BASE, fewer than
two did (0, or exactly 1). That is exactly AC-3's two examples:

    revert PR #2371's `name:` overrides   -> `reconcile` goes from 1 producer (base) to 3 (head)
    add a fourth job named `reconcile`    -> `reconcile` goes from 1 producer (base) to 2 (head)

and it is exactly what the current, accepted duplicates do NOT trigger: `fixture` has 48 producers at
base AND at head (a rename, an unrelated edit, or a 49th `fixture` job all leave it at "already >= 2",
never a 0/1 -> 2+ transition) — so an ordinary PR touching an unrelated workflow, or adding one more
conventionally-named `fixture` job, stays green. AC-2's floor — "at minimum, cover names that also
appear in `required_status_checks.contexts`" — holds for free: this gate does not special-case OR
exclude any name, required or not; it grades every top-level job's effective name identically, so a
required name inherits the exact same non-regression guarantee as everything else.

THE RESIDUAL GAP, AND `--required-context` (.github#2447). None of the 8 names above is required today,
but nothing stops an ORDINARY branch-protection edit — an admin action in GitHub's UI/API, not a
reviewed workflow-YAML change — from requiring one tomorrow. The moment that happens, the exact
`.github#2354` defect recurs, and this gate's own base-vs-head diff cannot see it: the name was already
ambiguous before it became required, so neither the old nor the new state trips a 0/1 -> 2+ transition.
Disambiguating all 65 (measured 2026-08-12: 70) jobs to close that residually was REJECTED — see
`.github#2447` — as a disproportionate mechanical rename for a Low-severity, not-yet-exploitable gap.
Instead, `--required-context NAME` (repeatable) names a check-run this repo's OWN branch protection
CURRENTLY requires, and this gate then asserts, UNCONDITIONALLY (not merely as a regression), that name
does not resolve to 2+ top-level producers at HEAD. This is deliberately NOT the absolute "no two jobs
ever" gate rejected above — it only grades the small, explicit set the caller passes, so it stays green
for every one of the 65 (70) accepted duplicates that is not required, and only reds if a name BOTH is
required AND is ambiguous. The caller (`job-uniqueness-coherence.yml`) hardcodes today's required set,
because this repo's own CI cannot read its own branch protection live: `administration: read` is not a
valid `permissions:` scope for a workflow's `GITHUB_TOKEN` (`check-reusable-job-ids.py`'s own docstring),
and the org dispatch App is not installed on `.github` itself (`required-context-coherence.yml`: the
App's roster is the six receivers, not the authority that owns their callees). Whoever changes this
repo's own required status checks MUST update that hardcoded set in the SAME reviewed PR — the existing
practice this repo already follows for protection changes (see `coherence.yml`'s `claim-generation` job,
which documents an admin `gh api -X PUT .../required_status_checks/contexts` command the same way).

AN EFFECTIVE NAME is what GitHub actually uses for the check run: a job's `name:` if it has one, else
its job id (job.get("name") or job.get("uses") on the CALLING side never applies here — a job WITH
`uses:` publishes a nested `<caller> / <callee>` name instead, is #549's gate's subject, and is
excluded from this one entirely, matching "non-`workflow_call`-invoking" in AC-1).

A "producer" is identified by (workflow file, job id) — NOT by the expanded name — so a job gaining or
losing a `strategy.matrix` never counts as a new or vanished producer by itself: GitHub disambiguates a
matrix job's own combinations with an automatic ` (v1, v2)` suffix, so one matrixed job cannot become
"two jobs" under this model, and this gate does not attempt to enumerate matrix combinations (unlike
`check-required-contexts.py`, which must, because IT needs the exact literal string a receiver could
require — this gate only needs to know how many DISTINCT jobs claim a base name).

Usage:
  check-job-uniqueness.py [--root <dir>] --base <git-ref> [--required-context NAME ...]
Exit: 0 = no name became newly ambiguous, and no given --required-context is ambiguous at HEAD;
1 = at least one of those is true (a real regression, or an already-required name is already
ambiguous — the log says which); 3 = no verdict, PERMANENT — a workflow that will not parse, or a
base ref that cannot be read.

"I could not check" must never share an exit code with "I checked, and it's fine" (#266) — nor with
"I checked, and it's broken" (#320).
"""
from __future__ import annotations

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from lib.gate import (  # noqa: E402
    ExitCode,
    GateError,
    base_parser,
    load_yaml,
    run,
    workflow_files,
)

import subprocess  # noqa: E402

NAME = "check-job-uniqueness"


def git(root: str, *args: str) -> str:
    proc = subprocess.run(
        ["git", "-C", root, *args], capture_output=True, text=True, check=False,
    )
    if proc.returncode != 0:
        raise GateError(f"git {' '.join(args)} failed: {' '.join(proc.stderr.split())}")
    return proc.stdout


def effective_names(doc: dict, what: str) -> dict[str, str]:
    """job id -> effective check-run name, for every TOP-LEVEL job that does not itself call a
    reusable workflow (`uses:`). Those are #549's subject, not this gate's."""
    jobs = doc.get("jobs")
    if not isinstance(jobs, dict) or not jobs:
        raise GateError(f"{what}: declares no `jobs:` mapping — it cannot run")
    out: dict[str, str] = {}
    for job_id, job in jobs.items():
        if not isinstance(job, dict):
            raise GateError(f"{what} [{job_id}]: the job is not a YAML mapping")
        if "uses" in job:
            continue  # publishes a nested `<caller> / <callee>` name — #549's gate, not this one
        name = job.get("name")
        out[str(job_id)] = str(name) if isinstance(name, (str, int, float)) and str(name) else str(job_id)
    return out


def producers(files_and_texts: list[tuple[str, str]]) -> dict[str, set[tuple[str, str]]]:
    """name -> the set of (workflow file, job id) that resolve to it, across every given workflow."""
    out: dict[str, set[tuple[str, str]]] = {}
    for path, text in files_and_texts:
        doc = load_yaml(text, path)
        for job_id, name in effective_names(doc, path).items():
            out.setdefault(name, set()).add((path, job_id))
    return out


def base_workflow_files(root: str, base: str) -> list[str]:
    out = git(root, "ls-tree", "--name-only", base, "--", ".github/workflows/")
    return [p for p in out.splitlines() if p.endswith((".yml", ".yaml"))]


def at_base(root: str, base: str, path: str) -> str | None:
    """The file's content at the base ref, or None if it did not exist there (new at HEAD)."""
    proc = subprocess.run(
        ["git", "-C", root, "show", f"{base}:{path}"],
        capture_output=True, text=True, check=False,
    )
    if proc.returncode != 0:
        err = " ".join(proc.stderr.split())
        if "does not exist" in err or "exists on disk" in err:
            return None
        raise GateError(f"cannot read {path} at {base}: {err}")
    return proc.stdout


def head_files_and_texts(root: str) -> list[tuple[str, str]]:
    out: list[tuple[str, str]] = []
    for path in workflow_files(root):
        rel = os.path.relpath(path, root)
        with open(path, encoding="utf-8") as fh:
            out.append((rel, fh.read()))
    return out


def base_files_and_texts(root: str, base: str) -> list[tuple[str, str]]:
    out: list[tuple[str, str]] = []
    for rel in base_workflow_files(root, base):
        text = at_base(root, base, rel)
        if text is None:  # listed by ls-tree a moment ago; cannot vanish mid-read
            raise GateError(f"{rel} is in {base} but could not be read")
        out.append((rel, text))
    return out


def main(argv: list[str]) -> int:
    ap = base_parser(__doc__.splitlines()[0])
    ap.add_argument("--base", required=True, help="the git ref to compare against (the merge-base)")
    ap.add_argument(
        "--required-context",
        action="append",
        default=[],
        metavar="NAME",
        dest="required_contexts",
        help=(
            "a check-run name this repo's OWN branch protection CURRENTLY requires (repeatable; "
            ".github#2447). Graded unconditionally against HEAD alone — not as a base-vs-head "
            "regression — because an already-required name that is already ambiguous is a live "
            "exposure every day it stays that way, not something introduced by this PR."
        ),
    )
    args = ap.parse_args(argv)

    head = producers(head_files_and_texts(args.root))
    base = producers(base_files_and_texts(args.root, args.base))

    if not head:
        raise GateError(
            "no top-level, non-workflow_call-invoking job was found in any workflow at HEAD — this "
            "gate compared nothing, which is a failure to audit, not a clean audit."
        )

    findings: list[str] = []
    for name in sorted(head):
        head_producers = head[name]
        if len(head_producers) < 2:
            continue  # not ambiguous at HEAD — nothing to regress
        base_producers = base.get(name, set())
        if len(base_producers) >= 2:
            continue  # already ambiguous at base — not a regression THIS PR introduced
        where = ", ".join(f"{f}:{j}" for f, j in sorted(head_producers))
        findings.append(
            f"the check-run name {name!r} is now produced by {len(head_producers)} top-level jobs "
            f"that did not all resolve to it at the merge-base ({where}). GitHub's required-status-"
            f"check matching is purely string-based, so if {name!r} is ever required, ANY of these "
            f"jobs satisfies it regardless of which one actually ran — the exact .github#2354 defect "
            f"(three jobs sharing the bare id `reconcile`). Give every job but one an unambiguous "
            f"job-level `name:`, as PR #2371 did for `architecture-map.yml` and `feed-autofix.yml`."
        )

    # .github#2447: the ABSOLUTE tripwire for the small, explicit set of names this repo's OWN
    # branch protection currently requires. Unconditional — not gated on the base comparison above —
    # because an already-required, already-ambiguous name is a live exposure regardless of whether
    # THIS PR introduced it. Deliberately narrow: only the names the caller passes are graded, so
    # every other accepted duplicate (e.g. `fixture`) stays ungraded here, exactly as the docstring's
    # "non-regression, not absolute" framing requires for the general case.
    required_findings: list[str] = []
    for name in sorted(set(args.required_contexts)):
        required_producers = head.get(name, set())
        if len(required_producers) < 2:
            continue
        where = ", ".join(f"{f}:{j}" for f, j in sorted(required_producers))
        required_findings.append(
            f"the REQUIRED status check {name!r} is produced by {len(required_producers)} top-level "
            f"jobs at HEAD ({where}). This repo's OWN branch protection requires this exact context "
            f"today, and GitHub's required-status-check matching is purely string-based, so ANY of "
            f"these jobs satisfies it regardless of which one actually ran — the exact .github#2354 "
            f"defect, LIVE, today (not merely a future risk). Disambiguate all producers but one "
            f"before this can be green again."
        )

    if findings or required_findings:
        for f in findings:
            print(f"::error::{NAME}: {f}", file=sys.stderr)
        for f in required_findings:
            print(f"::error::{NAME}: {f}", file=sys.stderr)
        print(
            f"\n{len(findings)} check-run name(s) became newly ambiguous across top-level jobs in "
            f"this repo's own workflows, comparing {args.base} to HEAD, and {len(required_findings)} "
            f"already-required check-run name(s) are already ambiguous at HEAD.",
            file=sys.stderr,
        )
        return int(ExitCode.FINDING)

    ambiguous_today = sorted(name for name, ps in head.items() if len(ps) >= 2)
    print(
        f"ok: no top-level job's check-run name became newly ambiguous ({len(head)} distinct name(s) "
        f"graded, comparing {args.base} to HEAD)."
        + (
            f" {len(ambiguous_today)} name(s) were ALREADY shared by 2+ jobs at the merge-base and "
            f"remain so — not re-graded here; see this gate's own docstring: "
            f"{', '.join(ambiguous_today)}."
            if ambiguous_today
            else ""
        )
        + (
            f" {len(args.required_contexts)} required context(s) checked, none ambiguous "
            f"({', '.join(sorted(set(args.required_contexts)))})."
            if args.required_contexts
            else ""
        )
    )
    return int(ExitCode.OK)


if __name__ == "__main__":
    sys.exit(run(main, sys.argv[1:], name=NAME))
