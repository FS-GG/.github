#!/usr/bin/env python3
"""Assert coord-board-reconcile.yml's concurrency block stays fan-out-safe (.github#2361, round 3).

THE INVARIANT THIS PROTECTS. The workflow's single `reconcile` job scans and can WRITE the entire
~2000-item board on every run that reaches it — not just the item that triggered it — and posts
`Writes.lifecycleWatermark` comments with no dedup/CAS guard (`src/FS.GG.Coord.GitHub/Writes.fs:933-934`
is a blind `postComment`). Any design that lets two runs able to reach that step execute
CONCURRENTLY reopens a double-write hole; the file's own `--worker coord-board-reconcile` comments
rest on there being no such fan-out.

THREE DESIGNS were tried on this one item, in order, each one superseding the last:
  1. (pre-.github#2361) a flat, single, repo-wide group with GitHub's DEFAULT `queue: single` —
     genuinely serialised (no fan-out), but `queue: single` CANCELS the pending run whenever a newer
     one for the same group arrives, before any job `if:` is ever evaluated. Measured: 22/37
     issue_comment-triggered runs cancelled; a `pull_request` run's cancellation silently blocked
     PR #2383 (a required status context went MISSING, not red).
  2. (round 1) the group keyed by the triggering issue/PR number, still on `queue: single`. Fixed
     the eviction for UNRELATED events, but reintroduced fan-out: a PR's push, an unrelated `issues`
     edit, and the hourly `schedule` could now land in different groups and run their write steps
     CONCURRENTLY. This is the double-write hole above, for real.
  3. (round 2) the group's isolation narrowed to ONLY the runs the job's OWN `if:` already proves
     are guaranteed no-ops (marker-matching `issue_comment`), mirroring that predicate verbatim, on
     `queue: single`. Closed the double-write hole (still exactly one shared group for anything that
     could write) — but a GENUINE (non-marker) `issue_comment`, or any two UNRELATED real events
     (two different PRs' pushes, a PR push vs `schedule`), still shared that one group under
     `queue: single`'s eviction default, so the item's own named worse-than-visible harm — a
     required status context silently MISSING — was STILL OPEN for real (not merely synthetic)
     traffic. Confirmed live: the comment that evicted PR #2383's own run carries no `<!-- fsgg:`
     prefix at all.

THE SHIPPED DESIGN (round 3): `concurrency.queue: max` (GitHub Actions, GA 2026-05-07) replaces
`queue: single`'s CANCEL-on-arrival behaviour with FIFO QUEUEING, up to 100 pending runs per group,
documented incompatible ONLY with `cancel-in-progress: true` (this workflow keeps `false`). That
means the group no longer has to choose between "narrow it and risk fan-out" and "keep it flat and
accept eviction" — it can stay the single, flat, repo-wide group it was before .github#2361 EVER
existed, and simply never evicts anything (short of >100 simultaneously pending, far above this
repo's observed worst case). One group, one FIFO queue, unconditionally: the double-write hole has
nothing left to reopen, and the eviction defect has nothing left to trigger it.

WHAT THIS SCRIPT CHECKS, entirely offline (no GitHub API, no live scheduler):

  1. `concurrency.group` is a STRING CONSTANT with no per-event branching: it may reference
     `github.repository` (repo-scoping is fine and expected) but must not reference `github.event`
     at all. A group that varies by triggering event is exactly what reopens the fan-out/double-write
     hole rounds 1 and 2 each had to reason about by hand — this makes "the group cannot vary" a
     mechanically checked fact instead of a read-the-comments one.
  2. `concurrency.queue` is exactly `max`. Its absence is `queue: single`'s implicit default — the
     root cause of the ORIGINAL .github#2361 defect and of round 2's still-open residual — so a
     missing or wrong value is a finding, not a warning.
  3. `concurrency.cancel-in-progress` is exactly `false`. `src/FS.GG.Coord.Core/Landable.fs:56-59`
     (`#698`) requires a run that starts be allowed to finish rather than killed mid-scan by a newer
     trigger; GitHub also outright rejects `queue: max` combined with `cancel-in-progress: true`, so
     this is both a local correctness requirement and a workflow-validity requirement.
  4. `on` does not list `issue_comment` or `issues` at all (.github#2416). `queue: max` (check 2)
     stops a queued comment-triggered run from EVICTING a pending sibling, but never stopped GitHub
     from CREATING one and giving it a FIFO slot in the one shared group check 1 protects — and
     comment traffic was measured to dwarf every other trigger (42/45 runs, 22->67 pending in 16
     minutes), so a required `pull_request` context sharing that queue went ABSENT, not red, and
     silently blocked seven independently-reviewed PRs. GitHub's `on:` cannot inspect a comment's
     body (no `paths` for issue_comment/issues events), so the job-level marker-prefix filter this
     workflow used to carry (.github#2352 AC-1) answered "should the job DO work", never "should a
     run be CREATED at all" — removing the trigger is the only way to stop run creation itself, and
     this check makes "the trigger stays removed" a mechanically checked fact instead of a
     read-the-comments one, the same shape check 1 already established for the group.

Usage: check-reconcile-concurrency-scope.py [--root <dir>]
Exit: 0 = the concurrency block is fan-out-safe; 1 = a finding (branching group, missing/wrong
queue depth, wrong cancel-in-progress, or a reintroduced issue_comment/issues trigger); 3 = no
verdict, PERMANENT — the workflow/concurrency/trigger block could not be read.

THIS GATE IS STATIC, so exit 2 is deliberately absent, matching check-workflow-timeouts.py's own
reasoning: it reads one committed file and nothing else, so there is no transport failure it could
ever report, and a retryable verdict it can never mean would be a lie about the exit contract.
"""
from __future__ import annotations

import argparse
import sys
from pathlib import Path

try:
    import yaml
except ImportError:
    print("check-reconcile-concurrency-scope: PyYAML is required (pip install pyyaml)", file=sys.stderr)
    sys.exit(3)

WORKFLOW_REL = ".github/workflows/coord-board-reconcile.yml"
JOB_ID = "reconcile"


def fail(msg: str, code: int) -> int:
    print(f"check-reconcile-concurrency-scope: {msg}", file=sys.stderr)
    return code


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", default=".")
    args = parser.parse_args()

    path = Path(args.root) / WORKFLOW_REL
    if not path.is_file():
        return fail(f"NO VERDICT (permanent) — {path} does not exist", 3)

    try:
        doc = yaml.safe_load(path.read_text(encoding="utf-8"))
    except yaml.YAMLError as e:
        return fail(f"NO VERDICT (permanent) — {path} would not parse: {e}", 3)

    if not isinstance(doc, dict):
        return fail(f"NO VERDICT (permanent) — {path} did not parse to a mapping", 3)

    jobs = doc.get("jobs") or {}
    if not isinstance(jobs.get(JOB_ID), dict):
        return fail(f"NO VERDICT (permanent) — no `{JOB_ID}` job in {path}", 3)

    # PyYAML (YAML 1.1) parses the bare, unquoted `on` mapping key as the boolean `True`, not the
    # string `"on"` — this is the same landmine GitHub's own docs warn workflow authors about
    # (quote it, or read it back knowing this). `doc.get("on")` would silently return `None` here and
    # make every workflow look like it has no triggers at all; `doc.get(True)` is the correct read.
    on_block = doc.get(True)
    if not isinstance(on_block, dict):
        return fail("NO VERDICT (permanent) — no `on` trigger block in the workflow", 3)

    concurrency = doc.get("concurrency")
    if not isinstance(concurrency, dict):
        return fail("NO VERDICT (permanent) — no `concurrency` block in the workflow", 3)

    group = concurrency.get("group")
    if not isinstance(group, str) or not group.strip():
        return fail("NO VERDICT (permanent) — `concurrency.group` is not a non-empty string", 3)

    findings = []

    # Check 1: no per-event branching. `github.repository` is the only context reference allowed;
    # anything mentioning `github.event` varies the group by the triggering event, which is the
    # exact shape both prior rounds on this item had to reason about by hand to avoid reopening the
    # double-write hole. A simple substring check is sufficient and deliberately conservative: ANY
    # occurrence of `github.event` in the group is disqualifying, not just the specific expressions
    # rounds 1/2 happened to use.
    if "github.event" in group:
        findings.append(
            f"`concurrency.group` references `github.event...` ({group!r}) — the group varies by "
            "triggering event, which can let two runs able to reach the write step execute "
            "concurrently (the double-write hole rounds 1/2 of this same item each reopened or had "
            "to reason around by hand). The group must be a constant, repo-scoped string only."
        )

    # Check 2: queue: max. Its absence silently reverts to `queue: single`'s cancel-on-arrival
    # default — the root cause of the original defect and of round 2's still-open residual.
    queue = concurrency.get("queue")
    if queue != "max":
        findings.append(
            f"`concurrency.queue` is {queue!r}, not `'max'` — this workflow inherits GitHub's "
            "`single` default (at most one pending run; a newer run for the same group CANCELS the "
            "pending one before any job `if:` is ever evaluated), which is the exact defect "
            ".github#2361 exists to close, not merely mitigate for the marker-comment subset."
        )

    # Check 3: cancel-in-progress must be false, both for #698 (a run that starts must be allowed to
    # finish) and because GitHub rejects `queue: max` combined with `cancel-in-progress: true`
    # outright as a workflow validation error.
    cip = concurrency.get("cancel-in-progress")
    if cip is not False:
        findings.append(
            f"`concurrency.cancel-in-progress` is {cip!r}, not `false` — src/FS.GG.Coord.Core/"
            "Landable.fs:56-59 (#698) requires a run that starts be allowed to finish, and GitHub "
            "rejects `queue: max` paired with `cancel-in-progress: true` as a validation error."
        )

    # Check 4: `issue_comment`/`issues` must not be triggers at all (.github#2416). `queue: max`
    # (check 2) only stops a queued comment-triggered run from evicting a pending sibling; it never
    # stopped GitHub from creating one and giving it a FIFO slot in the one shared group check 1
    # protects. Comment traffic dwarfs every other trigger this workflow has, so a required
    # `pull_request` context sharing that queue can still go ABSENT behind comment volume even with
    # every other check here passing. `on:` carries no `paths` for these two event types, so a
    # types-narrowing or a job-level body filter (the retired .github#2352 AC-1 design) cannot
    # distinguish a genuine comment from a marker one at trigger time — only removing the trigger
    # stops the run from being created.
    reintroduced = [k for k in ("issue_comment", "issues") if k in on_block]
    if reintroduced:
        findings.append(
            f"`on` lists {reintroduced!r} as a trigger — comment/issue events create a full-scan run "
            "sharing this workflow's one FIFO concurrency group (check 1) with `pull_request`, so a "
            "required `pull_request` status context can again go ABSENT (not red) behind comment "
            "traffic, the exact .github#2416 defect. `on:` cannot filter these events by body or "
            "path, so narrowing `types:` or a job-level `if:` cannot fix this — the trigger itself "
            "must stay removed."
        )

    if findings:
        for f in findings:
            print(f"check-reconcile-concurrency-scope: FINDING — {f}", file=sys.stderr)
        return 1

    print(
        "check-reconcile-concurrency-scope: OK — one constant, repo-scoped group; "
        "queue: max; cancel-in-progress: false; no issue_comment/issues trigger."
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
