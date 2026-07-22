---
name: workRoadmap
description: Drive a markdown roadmap to completion, one milestone at a time, each in a fresh disposable subagent. Use when a repo has a roadmap doc whose checklist items are milestones and you want them worked end-to-end without babysitting. For each unchecked milestone the parent spawns a NEW subagent that takes it to done via the SDD lifecycle (fs-gg-sdd-* — charter/specify through ship), ticks the roadmap with a progress note, runs fs-gg-feedback-report, and opens/reviews/merges its own PR on green — then the subagent dies and the parent spawns the next one against updated main. When the roadmap is fully checked, the parent writes a detailed timestamped report to docs/reports/ and lands that as its own PR. Runs in a scaffolded fsgg-sdd product repo where the fs-gg-sdd-* and fs-gg-feedback-report skills are materialized. Canonized by ADR-0053 (§6 as amended by ADR-0056); see also the fs-gg-sdd-lifecycle and pnext-item skills.
---

# workRoadmap

One command's worth of intent: **"take this roadmap and burn it down, milestone by milestone, and
don't make me drive."** The roadmap is the plan and the ledger both — the parent reads it to find
the next milestone, and the milestone's own worker writes back to it when the work lands. Every
milestone gets a **fresh subagent with fresh context**; when it has shipped and merged, that
subagent **dies**, and the parent spawns the next one against updated `main`. The loop ends when the
roadmap has no unchecked milestone left, and then the parent writes the report and lands it.

This skill is the **loop and its contract**. It does not re-teach the SDD lifecycle — that is
[fs-gg-sdd-lifecycle](../fs-gg-sdd-lifecycle/SKILL.md)'s, and each subagent runs it. It does not
re-teach PR-to-merge discipline — that is [pnext-item](../pnext-item/SKILL.md) §5–6's, and each
subagent follows it. It owns the thing neither of those does: **the sequencing of many milestones
across many disposable workers, driven off one markdown file.**

## Where this runs

A **scaffolded fsgg-sdd product repo** — one where `fs-gg-sdd-*` (the lifecycle skills) and
`fs-gg-feedback-report` are materialized. If those skills are not present, you are in the wrong tree
(e.g. FS-GG/.github itself, a kit source, does not materialize them) and this skill has nothing to
drive. Stop and say so rather than degrading into a plain edit loop — the whole value here is the
SDD-per-milestone discipline. (`fs-gg-feedback-capture` — the Spec Kit `after_*` hook skill — is
**not** required: it is frozen and scheduled for removal, gated to the legacy `spec-kit` lane per
ADR-0056 D3. On the default `sdd` lane it is absent, and the driver no longer invokes it; where a
legacy tree still carries capture records, `fs-gg-feedback-report` reads them.)

Preconditions, checked once before the loop:

- The working tree is **clean** and `main` is up to date (`git fetch && git status`).
- The roadmap doc exists (see below) and has at least one unchecked milestone.
- You are NOT going to push to `main` directly — the merge guard blocks it, and every change lands
  as a PR. Agent PR *merges* are allowed; direct pushes to `main` are not.

## The roadmap contract

**Locating the roadmap.** If `/workRoadmap <path>` was given an argument, that is the roadmap. With
no argument, auto-locate in this order and take the first hit: `docs/roadmap.md`, then any
`docs/**/*roadmap*.md`, then `ROADMAP.md` at the repo root. If two or more match and it is not
obvious which is live, **ask** rather than guess.

**What a milestone is.** The **top-level checklist items** are the milestones:

```markdown
## Milestones
- [x] M1 — transport wire contract      # done
- [ ] M2 — inbox widening               # <- the next milestone
  - [ ] narrow the Paths: touch-set     # a SUB-task of M2, not its own milestone
  - [ ] wire the derived-close predicate
- [ ] M3 — derived close
```

- **"Next" is the first unchecked (`- [ ]`) top-level item in document order.** The parent
  re-derives this from the file each round — the file, not memory, is the source of truth, because
  the previous subagent just edited it.
- **Nested checkboxes are that milestone's internal tasks**, not milestones of their own. The
  subagent works them all as part of finishing its one milestone; the parent never splits them out.
- A milestone with no checkbox syntax (a bare `## M2 …` heading with a status line) is fine too —
  treat the first heading whose status is not "done"/"shipped" as next, and record completion the
  same shape the doc already uses. Match the doc's existing convention; do not impose this one.

**Recording progress.** When a milestone lands, its subagent flips `- [ ]` → `- [x]` on that top-level
item and appends a compact progress note on the same line or just beneath it:

```markdown
- [x] M2 — inbox widening — PR #219, merged 2026-07-19; feedback: 2 findings filed (#221, #222)
```

Keep it one line and deterministic: **PR number, merge date, one-clause outcome, feedback pointer.**
The tick is what the parent reads next round; the note is the audit trail the final report rolls up.

## The loop (what the PARENT does)

The parent is the agent that invoked `/workRoadmap`. It does **not** implement milestones itself — it
schedules them. The loop is **strictly sequential**: milestone N+1's subagent does not start until
milestone N's PR is merged, because N+1 must branch from a `main` that already contains N, and because
each subagent deserves a fresh, uncluttered context.

Repeat until the roadmap has no unchecked milestone:

1. **Re-read the roadmap file** and pick the next unchecked top-level milestone. If there is none,
   break to the report step.
2. **`git fetch` and confirm `main` is current** (the previous PR merged into it). Each milestone
   branches from up-to-date `main`.
3. **Spawn a fresh subagent** (Agent tool) and hand it the per-milestone brief below, filled in with
   this milestone's heading text and the roadmap path. One subagent, one milestone.
4. **Wait for it to return.** On success it reports the merged PR number and the roadmap edit. The
   subagent is now dead; its context does not carry forward — that is deliberate.
5. **Verify the world matches the report** before trusting it: the PR is merged, `main` moved, and
   the roadmap file on `main` shows the milestone ticked. Do not take the subagent's word for a merge
   you can check — pull and look. (A subagent that reports "merged" on an unmerged PR is the failure
   mode most worth catching.)
6. **Loop.** Fresh subagent, next milestone.

Never run two milestones in parallel — the sequencing above is the point. Never let the parent "just
finish this one quickly" itself; the discipline is that every milestone goes through a subagent that
runs the full SDD lifecycle.

## The per-milestone subagent brief

The parent hands each subagent essentially this, with `<MILESTONE>` and `<ROADMAP>` substituted:

> You are working exactly one roadmap milestone to done, then you exit. Milestone: **`<MILESTONE>`**.
> Roadmap file: **`<ROADMAP>`**. You have the full repo and may not push to `main` (every change is a
> PR; agent PR merges are allowed).
>
> 1. **Branch** from up-to-date `main`: `git fetch && git switch -c <slug-of-milestone> origin/main`.
> 2. **Work the milestone to completion via the SDD lifecycle.** Follow
>    [fs-gg-sdd-lifecycle](../fs-gg-sdd-lifecycle/SKILL.md) end to end — charter/specify → clarify →
>    plan → tasks → implement → verify/validate → **fs-gg-sdd-ship**. The milestone's nested
>    checkboxes are your task list; all of them are in scope. Do not stop at "specified" or
>    "planned" — the milestone is done when it is shipped and green.
> 3. **Update the roadmap.** In `<ROADMAP>`, flip this milestone's top-level `- [ ]` → `- [x]` and
>    append the one-line progress note (PR number filled in at step 5, merge date, one-clause
>    outcome, feedback pointer). Commit it on your branch as part of the milestone.
> 4. **Run `fs-gg-feedback-report`.** It rolls up the milestone's feedback — reading any
>    `fs-gg-feedback-capture` records only where a legacy `spec-kit` tree still has them; the driver
>    itself invokes nothing else (capture is frozen and off the default `sdd` lane, ADR-0056 D3). File
>    whatever findings report surfaces as issues (do not silently drop them) and put their numbers in
>    the roadmap progress note.
> 5. **Open a PR, review it, merge on green** — the [pnext-item](../pnext-item/SKILL.md) §5–6 way:
>    open the PR, give it a real review, wait for required checks, and merge once green. A problem you
>    find on the way you FIX in this PR when that keeps it reviewable, or file at its root cause when
>    it does not belong here. Backfill the PR number into the roadmap note before you merge (or in an
>    immediate follow-up commit if the number only exists after open).
> 6. **Report back** to the parent: the milestone, the merged PR number, the roadmap line you wrote,
>    and any findings you filed. Then you are done — exit.
>
> If the milestone genuinely cannot land from here — it needs a human decision, or it is blocked on
> another repo — do NOT tick it and do NOT fake a merge. Report the blocker to the parent and stop.

## Termination and the final report

When step 1 of the loop finds no unchecked milestone, the parent — **itself, not a subagent** — writes
a detailed report and lands it:

1. **Write** `docs/reports/<YYYY-MM-DD>-<roadmap-slug>-completion.md`, timestamped for today. It
   should be a real report, not a changelog line — cover, per milestone: what shipped, the merged PR,
   the merge timestamp, what the SDD lifecycle produced (spec/plan/evidence pointers), the feedback
   findings and where they were filed, and any deviations from the original roadmap. Close with a
   roll-up: total milestones, total PRs, open follow-ups the feedback runs generated, and anything a
   human should look at. Follow the house report style already in `docs/reports/`.
2. **Land it as its own PR** — feature branch, open, review, merge on green — the same discipline every
   milestone used. The report is the last thing to merge.
3. Report completion to the operator: roadmap fully checked, report PR number, follow-ups outstanding.

## Failure handling

- **A milestone that will not land** stops the loop. The parent surfaces the subagent's blocker and
  does not advance — skipping a milestone silently would corrupt the "roadmap is the ledger" contract
  and strand everything sequenced behind it. A human decides whether to unblock, re-scope, or reorder.
- **A subagent that reports a merge that did not happen** is caught by loop step 5 (the parent
  verifies against `main`, not against the report). Treat a mismatch as a failed milestone, not a
  passed one.
- **Never bypass the merge guard.** No direct push to `main`, no local `git merge` into `main` — the
  guard exists because an agent once merged its own PRs unasked. Everything here is still a PR; the
  allowance is only that the agent may *merge* a green, reviewed one.

## See also

- **ADR-0053** — the org record that canonizes this loop, its cross-repo forces, and the roads not
  taken. This file is the protocol; that record is the *why*.
- [fs-gg-sdd-lifecycle](../fs-gg-sdd-lifecycle/SKILL.md) — the SDD workflow each subagent runs; the
  authority on stage order, not this file.
- [pnext-item](../pnext-item/SKILL.md) — the open-PR → review → merge-on-green loop each subagent
  reuses, and the "fix the cause, then take it" discipline for problems found mid-milestone.
- `fs-gg-feedback-report` — the post-ship feedback roll-up the driver runs (step 4). Its legacy
  sibling `fs-gg-feedback-capture` is frozen and scheduled for removal (ADR-0056 D3); report reads
  capture records where a `spec-kit` tree still has them, but the driver invokes only report.
- ADR-0018 (transient/durable SDD artifact taxonomy) — what the lifecycle leaves behind per milestone.
