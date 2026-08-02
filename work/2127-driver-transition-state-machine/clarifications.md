---
schemaVersion: 1
workId: 2127-driver-transition-state-machine
title: Driver Transition State Machine
stage: clarify
changeTier: tier1
status: needsAnswers
sourceSpec: work/2127-driver-transition-state-machine/spec.md
publicOrToolFacingImpact: true
---

# Driver Transition State Machine Clarifications

## Source Specification
- work/2127-driver-transition-state-machine/spec.md

## Clarification Questions
- CQ-001 [AMB:AMB-001]: How does the new surface compose with existing CLI and
  receipt contracts?
- CQ-002 [AMB:AMB-002]: Who decides whether overlapping objectives consolidate?

## Answers
- CQ-001: Add an additive typed planner/advance surface and preserve existing
  command contracts; model receipt freshness and validation as data.
- CQ-002: The host supplies the consolidation objective explicitly; the engine
  only orders and validates deterministic state transitions.

## Decisions
- DEC-001 [CQ-001] [AMB:AMB-001] [FR-001] [FR-002] [FR-003]: The planner will
  expose typed actions and receipt/validation data without replacing existing CLI
  command contracts.
- DEC-002 [CQ-002] [AMB:AMB-002] [FR-001] [FR-004]: Consolidation remains an
  explicit host judgement input; the state machine does not infer it.

## Accepted Deferrals
- None.

## Remaining Ambiguity
- None.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 2127-driver-transition-state-machine`.
