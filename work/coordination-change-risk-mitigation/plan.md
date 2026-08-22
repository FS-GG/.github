---
schemaVersion: 1
workId: coordination-change-risk-mitigation
title: Coordination Change Risk Mitigation
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/coordination-change-risk-mitigation/spec.md
sourceClarifications: work/coordination-change-risk-mitigation/clarifications.md
sourceChecklist: work/coordination-change-risk-mitigation/checklist.md
publicOrToolFacingImpact: true
---

# Coordination Change Risk Mitigation Plan

Prose status: planned

## Source Snapshot
- spec: work/coordination-change-risk-mitigation/spec.md sha256:596003775028144db71fd9a411385e205ed07488ee164d79471c153bb0ec084e schemaVersion:1
- clarifications: work/coordination-change-risk-mitigation/clarifications.md sha256:02eedf550b282d9788487b3efbd6f0034d35e5982b70dcf8abf01bd6fa61b18c schemaVersion:1
- checklist: work/coordination-change-risk-mitigation/checklist.md sha256:ac1c0ac8b8feafbedd9582ac17b8196e57b46867459a31c380cbb83e0a342548 schemaVersion:1

## Plan Scope
- Execute phases 0 through 5 from the source design as an expand-and-contract program; every phase is independently
  mergeable and reversible, and no phase silently absorbs another phase's authority or cleanup.
- Preserve existing wire schemas during compatibility slices. New internal discriminated unions and records translate
  at adapter boundaries until a separately reviewed schema migration is required.
- Keep full suites, production-shaped HTTP tests, independent review, provenance checks, and release gates. The new
  catalogue and models remove authored duplication, not independent observation.
- Treat source snapshots from `.github#2753`, `#2773`, `#2819`, `#2820`, and `#643` as the initial behavioral corpus.

## Plan Decisions
- PD-001 [AC-009] [FR-009] complete: Phase 0 records command-addition edit count, first-actionable-red time,
  structural-review findings, review rounds, divergence and premature-completion incidents, self-host receipts, and
  release interventions; freeze incident snapshots before adding authority.
- PD-002 [AC-001] [FR-001] complete: Phase 1 adds the kernel-owned `CommandDescriptor` catalogue beside existing
  tables, exposes one `(Command * Handler)` binding list from BoardOps, derives positive conformance cases, and removes
  duplicate structures only after byte-identical contract/help output and an effective missing-member inversion pass.
- PD-003 [AC-002] [FR-002] complete: Phase 2 introduces complete fact and decision types for ordinary exhaustion,
  delivery completion, host acceptance, merge authorization, and recovery; route every projection and writer through
  the returned decision and add a source gate against consumer-local fact matching.
- PD-004 [AC-003] [FR-003] complete: Phase 3 appends `DeliveryCompletionReceipt` before projections, converts close
  automation to reconciliation dispatch, observes would-correct behavior, then enables fail-closed reopen/status
  correction and finally requires the receipt.
- PD-005 [AC-004] [FR-004] complete: Phase 4 binds candidate bytes, version, base/head, refusal, evidence, decision and
  action keys in `SelfHostBootstrapReceipt`; only the stable shared verifier plus accountable host acceptance may
  authorize a write, and post-merge shared-engine replay blocks completion on disagreement.
- PD-006 [AC-005] [FR-005] complete: Phase 4 also introduces `change-completeness` as advisory, measures duration and
  false positives, then makes it required and a prerequisite of expensive mutation jobs and critic dispatch without
  removing their path-sensitive execution.
- PD-007 [AC-006] [FR-006] complete: Build bounded Review and Delivery reference models over verdict/check/round/head/
  wait/claim/obligation classes and assert `projection == writer == reducer`; retain production-shaped HTTP fixtures
  and add a mutation for each new shared boundary that forks or removes one predicate clause.
- PD-008 [AC-007] [FR-007] complete: Phase 5 keeps promotion as the only immutability transition, makes immutable retry
  verify existing journals with zero asset writes, persists append-only receiver receipts outside the asset set, and
  includes every declared receiver in coherent-set completion.
- PD-009 [AC-008] [FR-008] complete: Gate every predecessor-path deletion on behavior parity plus a demonstrated
  inversion, record rollback as restoring the coexistence path for that phase, and never combine deletion with first
  introduction of its replacement.

## Contract Impact
- PC-001 [PD-002] command surface: Catalogue descriptors become the source for verb, render support, mutation kind,
  documentation coverage, and expected handler ownership; parser behavior remains explicit.
- PC-002 [PD-003] lifecycle decisions: Complete typed fact/decision contracts become the in-process authority shared
  by projection, admission, and reduction while current JSON is translated at adapters.
- PC-003 [PD-004] completion evidence: `DeliveryCompletionReceipt` becomes the durable authority for closure, `Done`,
  claim release, and cleanup projections.
- PC-004 [PD-005] self-host evidence: `SelfHostBootstrapReceipt` and its enumerated reason vocabulary become required
  inputs to any candidate-engine write path.
- PC-005 [PD-006] CI contract: `change-completeness` becomes a required context and dependency predecessor after its
  advisory qualification period.
- PC-006 [PD-007] conformance contract: Each shared decision boundary declares a bounded reference model and an
  effective divergence mutation.
- PC-007 [PD-008] release contract: Receiver delivery receipts become append-only coherent-set obligations outside
  immutable assets.

## Verification Obligations
- VO-001 [PD-001] baseline: Record reproducible phase-0 measurements and freeze incident snapshots before authority
  migration, including the exact sample and timestamp for every metric.
- VO-002 [PD-002] [PC-001] catalogueConformance: Prove descriptor closure over nullary union cases, unique verbs,
  parser/render/help/write/documentation conformance, exact BoardOps handler ownership, and byte-identical public
  output during coexistence.
- VO-003 [PD-002] [PC-001] catalogueMutation: Remove one descriptor, handler, parser arm, render implementation, and
  write classification in turn and observe a named `change-completeness` failure for each.
- VO-004 [PD-003] [PD-007] [PC-002] lifecycleParity: Run the bounded state cross-products and prove projection,
  writer admission, and reducer transition agree for every generated history.
- VO-005 [PD-004] [PC-003] completionReconciliation: Exercise exact-merge success plus absent, stale, contradictory,
  unreachable, pending-board-write, and auto-closed-without-receipt cases, proving receipt-first idempotent correction.
- VO-006 [PD-005] [PC-004] selfHostTrust: Exercise every allowed bootstrap reason and refuse unknown reasons,
  business-rule disagreement, digest/version/head drift, incomplete evidence, missing host acceptance, and replay
  disagreement with zero remote mutation.
- VO-007 [PD-006] [PC-005] ciOrdering: Measure the focused gate under ordinary changes, prove structural reds occur
  before critic dispatch, and inspect workflow dependencies showing expensive jobs cannot start before it settles.
- VO-008 [PD-007] [PC-006] modelMutation: Remove each predicate clause or fork one consumer and record an observed
  focused-suite failure before restoring and rerunning green.
- VO-009 [PD-008] [PC-007] releaseRecovery: Rehearse mutable, promoted-immutable, partial-feed, and partial-receiver
  histories; assert zero immutable asset writes and verified receipts for every required receiver.
- VO-010 [PD-009] compatibilityAndRollback: For every phase, capture before/after parity, effective inversion, the exact
  predecessor deletion, and a rollback rehearsal that restores the coexistence path without data loss.

## Performance Intent
- `change-completeness` targets completion within five minutes for ordinary changes and adds no unnecessary network
  sweep; longer mutation, derived, and cross-repository checks remain later stages.
- Shared lifecycle decisions operate over already collected complete facts and do not add duplicate GitHub reads.
- Monthly metrics cover at least twenty engine PRs before claiming the command-change amplification target.

## Migration Posture
- PM-001 [PC-001] expandContract: Catalogue and legacy command tables coexist until byte/behavior parity and inversion.
- PM-002 [PC-002] compatibleAdapters: New internal decisions translate to existing JSON schemas unless a separately
  versioned migration is explicitly approved.
- PM-003 [PC-003] stagedAuthority: Completion correction runs in observe-only mode before writes and becomes required
  only after reconciliation evidence is stable.
- PM-004 [PC-005] advisoryToRequired: `change-completeness` is advisory until its duration and false-positive rate are
  measured, then becomes required before dependency rewiring.
- PM-005 [PC-007] appendOnly: Receiver receipts extend release evidence without rewriting promoted assets or journals.

## Generated View Impact
- GV-001 [PD-001] workModel: `readiness/coordination-change-risk-mitigation/work-model.json` and `analysis.json` are
  regenerated from current lifecycle sources by `fsgg-sdd analyze`; they are projections and are never hand-edited.
- GV-002 [PD-002] commandProjections: Command contracts, help coverage, render defaults, write classification, and
  positive conformance cases become deterministic projections over the catalogue.
- GV-003 [PD-004] completionProjections: Issue state, Projects status, claim, and cleanup become reconcilable projections
  over the completion receipt rather than authorities.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- The source design's monthly success targets remain outcome measures, not permission to weaken fail-closed behavior.
- Exact implementation touch sets must be declared per phase because this umbrella plan intentionally spans the
  coordination engine, tests, workflows, and release lifecycle.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work coordination-change-risk-mitigation`.
