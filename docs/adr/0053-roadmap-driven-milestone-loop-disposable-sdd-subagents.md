# ADR-0053: A roadmap is driven one milestone at a time, each in a fresh disposable subagent running the full SDD lifecycle

- **Status:** Accepted
- **Date:** 2026-07-19
- **Affects:** All scaffolded `fsgg-sdd` product components (they run the loop); FS.GG.SDD (owns the `fs-gg-sdd-*` lifecycle the loop drives) and FS.GG.Game (owns the `fs-gg-feedback-*` pair the loop runs); FS-GG/.github (canonical author of the `workRoadmap` skill).
- **Amended by:** [ADR-0056](0056-sdd-is-the-default-lifecycle-spec-kit-is-legacy-and-scheduled-for-removal.md) §Decision.3 — **§6 is re-keyed.** Its refuse-if-incomplete clause no longer keys on the `fs-gg-feedback-*` *pair*: `fs-gg-feedback-capture` is Spec Kit `after_*` hook machinery frozen and scheduled for removal, so §6 keys on `fs-gg-feedback-report` (`always`) alone, alongside `fs-gg-sdd-*`. The loop's feedback step (Decision 3's `fs-gg-feedback-capture` then `fs-gg-feedback-report` sub-clause) becomes **report-only** — capture is no longer invoked, and where a legacy `spec-kit` tree still holds capture records `fs-gg-feedback-report` reads them — and the loop now materializes on the default `sdd` lane it was built for (the Rouge1 symptom ADR-0056 Context names). The one-milestone-per-fresh-disposable-subagent decision, the fail-closed ledger, and the merge boundary are all unchanged.

## Context

A scaffolded product component keeps a **roadmap** — a markdown doc whose checklist items are the
milestones it intends to ship. The org already holds a strong opinion about *how* a milestone should
be worked: through the SDD lifecycle end to end ([ADR-0004](0004-constitution-ownership-for-lifecycle-sdd-products.md),
[ADR-0008](0008-fsgg-sdd-cli-first-class-member-of-coherent-set.md), [ADR-0018](0018-transient-durable-sdd-artifact-taxonomy.md);
the `fs-gg-sdd-*` skills own the stage order), then shipped, then followed by the feedback pair
(`fs-gg-feedback-capture` / `fs-gg-feedback-report`, owned by FS.GG.Game), then landed as a reviewed
PR. That opinion is real but **unenforced when a human drives it**: the operator picks a milestone,
runs the lifecycle, ticks the roadmap, remembers (or forgets) the feedback step, opens/reviews/merges,
and repeats — N times, in one degrading context, with the feedback step the first casualty of fatigue.

Two protocols already own *pieces* of this and neither owns the whole:

- **`fs-gg-sdd-lifecycle`** (an FS.GG.SDD-owned skill, materialized in product trees) owns the stage
  order of a single milestone. It says nothing about *which* milestone, or about the one *after* it.
- **[pnext-item](../../.claude/skills/pnext-item/SKILL.md)** / **[ADR-0021](0021-parallel-intra-repo-work-claim-worktree-touchset.md)** /
  **[ADR-0027](0027-worker-keyed-claim-lock-and-worker-channel.md)** own the claim → worktree →
  open-PR → review → merge-on-green loop — but for *board-scheduled issues* worked by *parallel*
  workers sharing one account, whose whole apparatus (comment-order CAS, worker-keyed lease, touch-set)
  exists to keep concurrent workers off each other. A roadmap's milestones are **sequential** — N+1
  branches from a `main` that already contains N — and **file-driven**, not board-scheduled.

The gap is the *sequencing of many milestones across many disposable workers, off one markdown ledger*.
The org already knows the quality lever for the "many workers" half: a **fresh, disposable context per
unit of work** (pnext-item spawns per item). What is missing is a driver that applies it to a roadmap.

## Decision

Canonize a **`workRoadmap`** driver skill (authored in FS-GG/.github). It defines the loop and its
contract; it **delegates** the two things it does not own, because a rule stated twice drifts in one
of the two ([ADR-0034](0034-typed-coordination-engine.md) §4).

1. **The markdown roadmap is plan and ledger both.** The top-level checklist items are the milestones;
   "next" is the first unchecked (`- [ ]`) item, **re-derived from the file each round** — the file,
   not the parent's memory, is the source of truth, because the milestone's own worker just edited it.
   Nested checkboxes are that milestone's internal tasks, never milestones of their own.

2. **One fresh disposable subagent per milestone, strictly sequential.** Milestone N+1 does not start
   until N's PR is merged, because N+1 branches from a `main` containing N and because each milestone
   deserves an uncluttered context. The parent schedules; it never implements a milestone itself.

3. **Each subagent takes its one milestone to done, then dies:** work it to *shipped* via the SDD
   lifecycle (stage order deferred to `fs-gg-sdd-lifecycle`) → tick the roadmap with a fixed-shape
   progress note (PR #, merge date, one-clause outcome, feedback pointer) → run
   `fs-gg-feedback-capture` then `fs-gg-feedback-report`, filing their findings as issues → open a PR,
   review it, merge on green (the ADR-0021/0027 discipline) → report back → exit.

4. **The parent verifies each merge against `main`, not against the subagent's report**, before it
   advances. A milestone that genuinely cannot land **stops the loop** — it is never ticked, never
   faked, never skipped — because a corrupted ledger strands everything sequenced behind it. The loop
   **fails closed**.

5. **On completion the parent writes a timestamped `docs/reports/<date>-<slug>-completion.md`** rolling
   up every milestone, its PR, its feedback findings, and any deviations, and lands *that* as its own
   PR — the last thing to merge.

6. **It runs only where the composed producers' skills are materialized** — a scaffolded product with
   `fs-gg-sdd-*` and `fs-gg-feedback-*` present. In a kit source that lacks them (FS-GG/.github itself)
   it refuses rather than degrading into a plain edit loop; the SDD-per-milestone discipline is the
   whole value.

## Consequences

- **The roadmap file becomes a governed ledger.** The tick is the state the next round reads; the note
  is the audit trail the final report rolls up. This is why (4) fails closed: the value of the ledger
  is that it never lies, so a milestone that cannot land must not leave a mark that says it did.
- **This composes two producers' skills into a loop it does not own.** It obliges FS.GG.SDD's
  `fs-gg-sdd-lifecycle` and FS.GG.Game's `fs-gg-feedback-*` to keep meaning what they mean; the loop
  names them and defers to them rather than restating their behavior, so a stage rename in the producer
  does not silently rot a hardcoded sequence here.
- **It rides the merge boundary, never bypasses it.** Every milestone and the final report land as
  reviewed PRs; the agent may *merge* a green, reviewed PR (the 2026-07-15 re-authorization) but never
  pushes `main` directly. The loop inherits that boundary as a hard rail.
- **The loop generates its own backlog.** The feedback pair's findings are filed as issues and surfaced
  in the completion report, rather than swallowed — a roadmap burndown ends with a visible follow-up
  set, not a claim of spotless completion.
- **Delivery to product trees is a follow-up, not decided here.** The skill's canonical home is
  FS-GG/.github's two agent-skill roots ([ADR-0011](0011-agent-skill-roots-full-union-orchestrator-owned-mirror.md)).
  It is authored, not yet mirrored: it is neither coordination-kit protocol (the kit carries the
  cross-repo coordination skills — [ADR-0019](0019-org-repo-roster-registry-and-coordination-kit.md))
  nor a producer-emitted SDD skill ([ADR-0014](0014-skill-vendoring-one-manifest-one-materialize-verify.md)/[ADR-0017](0017-skill-registry-condition-aware-materialization.md)).
  Which fabric delivers a `.github`-authored SDD *driver* to product components is a genuine open
  question and is tracked on the Coordination board, not prejudged in this record.

## Alternatives considered

- **Extend `pnext-item` to read a roadmap instead of the board.** Rejected: pnext-item's unit is a
  board-scheduled issue guarded by a worker-keyed claim lock and touch-set *for parallel workers on one
  account* ([ADR-0027](0027-worker-keyed-claim-lock-and-worker-channel.md)). A sequential, single-driver
  roadmap loop has no contention to arbitrate; folding it in would overload one skill with two
  incompatible coordination models and make the lock machinery dead weight on the roadmap path.
- **One long-lived agent walks the entire roadmap.** Rejected: context degrades across N milestones,
  and the org's established quality lever is a fresh worker per unit of work. Disposal is the feature,
  not an accident of implementation.
- **GitHub milestones or the Coordination board as the driver.** Rejected for this skill's job: a
  product roadmap is an in-repo narrative the team already keeps, and its sequencing is a
  *single-component, internal* concern. Forcing it onto the org board would couple product-internal
  ordering to cross-repo coordination; the board stays right for cross-repo epics
  ([ADR-0001](0001-cross-repo-coordination-via-issues.md)), and this is deliberately its in-repo analog.
- **Run milestones in parallel.** Rejected: N+1 branches from a `main` containing N; parallel milestones
  would fork the ledger and break the dependency chain the sequential rule exists to preserve.
