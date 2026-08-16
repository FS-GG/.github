#!/usr/bin/env python3
"""Who calls `dispatch-sender.yml` in this repository?

Slice 5 of the GitHub-native executor fencing design
(`docs/reports/2026-08-04-github-native-executor-fencing-design.md` §5.2, §11.2), `.github#2720`.

WHY THIS EXISTS AT ALL. The per-receiver operation lock (§4.1) is only a fence if every dispatch
passes a point that checks it. `dispatch-sender.yml` checks nothing — it mints an App token and
POSTs — so a second caller inside this repository would not break a rule, it would silently demote
the lock from a fence to a suggestion. The design states the property ("the broker … becomes the only
caller of `dispatch-sender.yml`"); this is the thing that makes it true tomorrow as well as today.

THE ASSERTION IS TWO-SIDED, AND THAT IS THE WHOLE POINT. "No second caller exists" is an ABSENCE
assertion, and an absence assertion is satisfied just as well by a matcher that finds NOTHING as by a
tree that contains nothing. This board measured that exact failure the day this landed: `.github#2312`
shipped an ordering gate whose forbidden-pattern matcher was blind to F#'s `_.Id` shorthand, so a
critic added a fourth copy of the forbidden pattern and all 823 tests passed. A worker on
`.github#2653` hit the sibling shape — a literal-only matcher blind to `dotnet build "$PROJ"`, which
is how the script under test spells it.

So this refuses in BOTH directions:

  * MORE than the expected caller  -> exit 1, naming every extra file.
  * FEWER (i.e. zero)              -> exit 1, because a matcher that finds no caller at all in a tree
                                      that certainly has one is not evidence of a clean tree; it is
                                      evidence of a broken matcher, and it is the state in which every
                                      "no second caller" report becomes vacuous.

and `tests/dispatch-broker/run.sh` drives it over a corpus where nine real caller spellings MUST match
and six lookalikes MUST NOT — two of the lookalikes and one of the callers lifted verbatim from live
production code, so the boundary is pinned by things that actually exist rather than by things that
were easy to think of.

STRUCTURE, NOT TEXT. It parses the YAML and reads `jobs.<id>.uses` and `jobs.<id>.steps[*].uses`. A
text scan cannot tell `uses: …/dispatch-sender.yml@main` from a COMMENT saying a workflow deliberately
does not call it — and this repository contains four such comments, in `release-kit.yml`,
`release-coord-engine.yml`, `skill-union-assert.yml` and `lockfile-sync.yml`. A structural read gets
that right for free, and it also catches the folded-scalar and quoted spellings a line-anchored regex
misses.

FAILS CLOSED (epic #266). A workflow file that will not parse is exit 3 — a NO-VERDICT — never a skip.
A file this cannot read may be carrying the very caller it is looking for, and "I could not look" must
never be spelled the same way as "I looked and it is clean". Discovering zero workflow files at all is
exit 3 for the same reason.

REACH, STATED HONESTLY. This sees `FS-GG/.github`'s own `.github/workflows/`, which is the entire
surface from which GitHub will run a workflow in this repository — so within this repository the
inventory is complete, not a sample. It does NOT see other repositories, and cross-repo callers exist:
`FS.GG.Rendering`'s `template-dispatch.yml` carries two SHA-pinned
`FS-GG/.github/.github/workflows/dispatch-sender.yml@5fed2838…` job calls, measured live 2026-08-16.
Those belong to the auto-update fabric and carry no operation key, no grant and no fenced effect — but
they are callers, and the design's wording is broader than this slice's `Paths:` can reach. That exact
spelling is therefore a MUST-MATCH form in the corpus, so the day such a call arrives in this
repository this reds instead of shrugging.
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

try:
    import yaml
except ImportError:  # pragma: no cover - the runner installs it; say so rather than crash opaquely.
    print(
        "::error::single_caller: PyYAML is not installed. This gate reads workflow STRUCTURE, not text, "
        "and cannot fall back to a regex without becoming the fails-open matcher it exists to prevent.",
        file=sys.stderr,
    )
    sys.exit(3)

# The callee, by BASENAME. Both extensions, because GitHub accepts both and a repository that renamed
# the file would otherwise silently lose the gate. Matching on the basename — rather than on a
# substring of the path — is what keeps `dispatch-sender-selftest.yml` out: a substring test would
# call that a caller, and a future sibling workflow would red this gate for no reason.
CALLEE_BASENAMES = frozenset({"dispatch-sender.yml", "dispatch-sender.yaml"})

# The two path components that must sit immediately above the file. A `uses:` reference to a reusable
# workflow is always `<owner>/<repo>/.github/workflows/<file>@<ref>` or `./.github/workflows/<file>`,
# so this holds for the local and the cross-repo spelling alike without needing to know the owner.
WORKFLOW_PARENTS = (".github", "workflows")

NO_VERDICT = 3


def _no_verdict(reason: str) -> "None":
    print(f"::error::single_caller: {reason}", file=sys.stderr)
    sys.exit(NO_VERDICT)


def uses_references(workflow: object) -> list[str]:
    """Every `uses:` value in one parsed workflow, job-level and step-level alike.

    Step-level `uses:` cannot name a reusable workflow today — GitHub only resolves those at job
    level — but it is read anyway, because the cost of reading it is nothing and the cost of NOT
    reading it, on the day that changes or on the day someone writes it by mistake, is a caller this
    gate reports as absent.
    """
    found: list[str] = []
    if not isinstance(workflow, dict):
        return found
    jobs = workflow.get("jobs")
    if not isinstance(jobs, dict):
        return found
    for job in jobs.values():
        if not isinstance(job, dict):
            continue
        job_uses = job.get("uses")
        if isinstance(job_uses, str):
            found.append(job_uses)
        steps = job.get("steps")
        if isinstance(steps, list):
            for step in steps:
                if isinstance(step, dict) and isinstance(step.get("uses"), str):
                    found.append(step["uses"])
    return found


def is_callee(reference: str) -> bool:
    """Does this `uses:` value name `dispatch-sender.yml`?

    Normalisation is deliberately narrow: strip the `@<ref>` suffix (which may itself be a branch, a
    tag, a full `refs/heads/...` ref, or a 40-character SHA), drop empty and `.` path segments, and
    compare the last three components case-insensitively. GitHub treats owner and repository names
    case-insensitively, so `fs-gg/.github/...` and `FS-GG/.github/...` are the same call and must not
    be distinguishable here.
    """
    path = reference.split("@", 1)[0].strip()
    if not path:
        return False
    segments = [s for s in path.replace("\\", "/").split("/") if s not in ("", ".")]
    if len(segments) < 3:
        return False
    if segments[-1].lower() not in CALLEE_BASENAMES:
        return False
    return tuple(s.lower() for s in segments[-3:-1]) == WORKFLOW_PARENTS


def scan(workflows_dir: Path) -> dict[str, int]:
    """`{workflow filename: number of calls}` for every file that calls the sender."""
    if not workflows_dir.is_dir():
        _no_verdict(f"{workflows_dir} is not a directory — there is nothing to scan, which is not the same as a clean tree")
    files = sorted(p for p in workflows_dir.iterdir() if p.is_file() and p.suffix in (".yml", ".yaml"))
    if not files:
        _no_verdict(f"{workflows_dir} contains no workflow files — a scanner that discovers nothing must not report success")
    callers: dict[str, int] = {}
    for path in files:
        try:
            parsed = yaml.safe_load(path.read_text(encoding="utf-8"))
        except (yaml.YAMLError, UnicodeDecodeError) as exc:
            _no_verdict(
                f"{path.name} will not parse ({exc.__class__.__name__}), so this gate has no verdict for it. "
                "A file that cannot be read may be carrying the very caller this is looking for."
            )
        count = sum(1 for reference in uses_references(parsed) if is_callee(reference))
        if count:
            callers[path.name] = count
    return callers


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--root", default=".", help="repository root to scan (default: cwd)")
    parser.add_argument(
        "--expect",
        default="fsgg-dispatch-broker.yml",
        help="the ONE workflow permitted to call dispatch-sender.yml",
    )
    parser.add_argument("--json", action="store_true", help="emit the caller inventory as JSON and decide nothing")
    args = parser.parse_args()

    callers = scan(Path(args.root) / ".github" / "workflows")

    if args.json:
        print(json.dumps(callers, sort_keys=True))
        return 0

    extra = sorted(name for name in callers if name != args.expect)
    if extra:
        print(
            f"::error::single_caller: dispatch-sender.yml has {len(extra) + (1 if args.expect in callers else 0)} "
            f"callers in this repository; only {args.expect} may call it. Unexpected: {', '.join(extra)}. "
            "A second caller does not break a rule — it demotes the per-receiver operation lock from a "
            "fence to a suggestion, because dispatch-sender.yml itself checks nothing (design §5.2).",
            file=sys.stderr,
        )
        return 1

    if args.expect not in callers:
        print(
            f"::error::single_caller: {args.expect} does not call dispatch-sender.yml. This is a REFUSAL and "
            "not a clean tree: with zero callers found, every 'no second caller exists' report this gate "
            "makes is vacuous — the same failure .github#2312's ordering gate shipped with. Either the "
            "broker stopped calling the sender, or this matcher can no longer see the call.",
            file=sys.stderr,
        )
        return 1

    print(f"single caller: {args.expect} ({callers[args.expect]} call(s)) — no other workflow calls dispatch-sender.yml")
    return 0


if __name__ == "__main__":
    sys.exit(main())
