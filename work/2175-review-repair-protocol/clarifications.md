---
schemaVersion: 1
workId: 2175-review-repair-protocol
title: "coord review: make independent review and repair a resumable typed protocol"
stage: clarify
changeTier: tier1
status: needsAnswers
sourceSpec: work/2175-review-repair-protocol/spec.md
publicOrToolFacingImpact: true
---

# coord review: make independent review and repair a resumable typed protocol Clarifications

## Source Specification
- work/2175-review-repair-protocol/spec.md

## Clarification Questions
- **CQ-001** (AMB-001): When a changes-required verdict's `reviewed-head` field is
  itself malformed or absent, is that a distinct closed-model case, or does it fold
  into an existing named state?
- **CQ-002** (AMB-002): Is "repair route availability" (FR-006/FR-007) itself
  computed by this engine, or supplied as an external fact?

## Answers
- CQ-001 → Keep `ChangesRequiringRepair round` as the coarser, general-fact case
  (the verdict is changes-required) and `AwaitingImplementerRepair round` /
  `AwaitingSameCriticConfirmation round` as its two refinements once the current
  head can be compared against the verdict's reviewed head. When the reviewed-head
  field cannot be read at all, the engine reports `MalformedEvidence` carrying the
  parser's own errors (FR-008) rather than guessing a refinement — never a silent
  `AwaitingImplementerRepair` default (resolves AMB-001).
- CQ-002 → "Repair route availability" is an external fact supplied by the caller
  (worker/host), because whether a fresh critic/worker slot exists is a scheduler
  fact this pure engine cannot observe. The engine's contract is: given
  `repairRouteAvailable: bool`, decide `RepairPhaseSetup`/`EnterRepairPhase` when
  true and `TerminalHumanPark` when false, at ordinary exhaustion (resolves AMB-002).

## Decisions
- **DEC-001** [CQ-001] [AMB:AMB-001] [FR-008] [AC-008]: A changes-required verdict
  with an unreadable reviewed-head field surfaces as `MalformedEvidence` carrying
  the underlying parser errors; it never silently defaults to
  `AwaitingImplementerRepair`.
- **DEC-002** [CQ-002] [AMB:AMB-002] [FR-006] [FR-007] [AC-006] [AC-007]:
  `repairRouteAvailable` is an external fact on the engine's `Facts` input, supplied
  by the caller; the engine only decides what follows from it (grant the one fresh
  repair phase, or park).

## Accepted Deferrals
No accepted deferrals recorded.

## Remaining Ambiguity
- None. AMB-001 and AMB-002 resolved above.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 2175-review-repair-protocol`.
