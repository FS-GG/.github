---
schemaVersion: 1
workId: 3068-repair-phase-live-assertion-writer
title: Repair Phase Live Assertion Writer
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/3068-repair-phase-live-assertion-writer/spec.md
sourceClarifications: work/3068-repair-phase-live-assertion-writer/clarifications.md
sourceChecklist: work/3068-repair-phase-live-assertion-writer/checklist.md
publicOrToolFacingImpact: true
---

# Repair Phase Live Assertion Writer Plan

Prose status: planned

## Source Snapshot
- spec: work/3068-repair-phase-live-assertion-writer/spec.md sha256:4f7b7cfae55e90e532053e550a0bcb80f6537768a704b72f6976632e414324f9 schemaVersion:1
- clarifications: work/3068-repair-phase-live-assertion-writer/clarifications.md sha256:ffac4e8d1a600af548440a73fbb7d5097da205654dfad57a24d947c5fb23c381 schemaVersion:1
- checklist: work/3068-repair-phase-live-assertion-writer/checklist.md sha256:419adea4a9d1f0b0293cde059379f8cec5a241c759ee001119981bcfbe4814a4 schemaVersion:1

## Plan Scope
- Work item 3068-repair-phase-live-assertion-writer is planned from the current specification, clarification, and checklist facts.
- Requirement count: 4.
- Clarification decision count: 0.
- Checklist result count: 4.

## Plan Decisions
- PD-001 [AC-001] [FR-001] in-progress: Add host-owned `review host-grant` and derived `review assert-repair` live commands that append canonical comment projections through the typed GitHub adapter without downstream mutation.
- PD-002 [AC-002] [FR-002] in-progress: Parse the assertion in the live review snapshot and pass it into every `Review.inspect` call used by inspect, wait-enter, and review-record; preserve the pure reducer as the sole transition authority.
- PD-003 [AC-003] [FR-003] done: Classify malformed, duplicate, stale review/head/PR, implementer/current-critic, wrong-derived-field, and old-schema projections independently; invalid projections remain audit noise while exact eligible duplicates collapse, with Lifecycle and black-box E2E inversion cases proving zero unauthorized assertion/wait mutation and no poisoning of later valid authority.
- PD-004 [AC-004] [AC-005] [FR-004] done: Make predecessor discovery a state-driven, provenance-bearing dependency: parse and classify the exact current PR first; read predecessor topology only when the derived transition requires repair-entry authority; exclude the current PR explicitly; and fail closed on an unreadable, malformed, ambiguous, or mismatched selected exhausted predecessor. Share the total purpose/round/evidence derivation with writer and reader and render an executable command with no caller-purpose input.
- PD-005 [AC-006] [AC-007] [AC-008] [AC-009] [AC-010] [AC-011] [FR-005] done: Export Kernel minted predicate over resolved canonical id; accept raw spellings that normalize equivalently. Model/parser keep `opts.Worker` independent: both producers require None plus FromEnv plus minted id. Pin wrapper+engine before-command nonroute and after-command parsed refusal. Preserve per-host non-poisoning/existential semantics and downstream.

## Contract Impact
- PC-001 [PD-001] [PD-004] [PD-005] command report: `review host-grant REF --pr N --json` derives exact immutable decision and env-minted self identity; host-owned `review assert-repair REF --pr N --json` consumes only that identity's receipt and separately derives live transition. Neither accepts caller semantic authority. Existing downstream contracts are unchanged.

## Planned Touch-Set Extension
- Kernel identity grammar requires only `src/FS.GG.Coord.Cli.Kernel/Identity.fs`, `src/FS.GG.Coord.Cli.Kernel/Identity.fsi`, and `tests/FS.GG.Coord.Cli.Kernel.Tests/IdentityTests.fs` beyond the existing route. Typed `widen` was attempted under `snipe-5bf9` and correctly refused because the 120-minute claim expired during architecture review. These paths MUST be added by typed widen and route revision after a fresh claim and before any source edit; issue-body hand editing is forbidden.

## Verification Obligations
- VO-001 [PD-001] [PD-002] [PD-003] [PD-004] [PD-005] [PC-001] semanticTest: Model parsed Option<Id> separately from resolved source/id. With valid env identity plus Some option, grant/assertion invocations refuse and post counts stay zero. Use actual engine/wrapper black-box for before/after flag placement. Retain baseline-first one-field behavioral controls without redundant full-record equality, noise monotonicity, host survival and zero downstream mutation. EV009 is sealed by the production-equivalent Lifecycle TRX and black-box E2E evidence.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] additive-before-release: Add `review-host-grant/v1` and replace only the unshipped caller-selectable assertion shape. Existing claim heartbeat, `review-wait/v1`, `review-decision/v2`, succession, terminal event-file, host-acceptance, and concurrency contracts remain byte-for-byte unchanged. Legacy decisions require a real backward-linked receipt or fresh review.

## Generated View Impact
- GV-001 [PD-001] [PD-002] workModel: SDD readiness views refresh from the authored package; runtime state remains GitHub comment authority and is never generated from SDD prose.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- The writer is deliberately narrow: a third-party assertion unblocks only the exact unchanged-head review it names, derives immutable semantic purpose from live state, and creates no review verdict by itself.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 3068-repair-phase-live-assertion-writer`.
