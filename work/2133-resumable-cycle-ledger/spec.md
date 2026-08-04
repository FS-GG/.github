---
schemaVersion: 1
workId: 2133-resumable-cycle-ledger
title: Resumable coordination cycle ledger
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Resumable coordination cycle ledger Specification

Prose status: specified

## User Value
The coordination host receives one typed, persisted next action for a roadmap or workspace cycle instead of reconstructing state from scattered artifacts.

## Scope
- SB-001: A generic Core cycle/ledger model and CLI document boundary usable by Markdown roadmaps and coordination-wired workspace boards.
- SB-002: Stable dependency-ready milestone inspection, resumable cycle registration, typed external-provider receipt validation, exact-head advancement, guarded ledger updates, and final roll-up validation.

## Non-Goals
- SB-003: Reimplementing SDD, independent-review, or feedback authoring semantics inside the coordination engine.
- SB-004: Inferring parallel authorization from dependency shape.

## User Stories
- US-001 (P1): As a user, I can the coordination host receives one typed, persisted next action for a roadmap or workspace cycle instead of reconstructing state from scattered artifacts.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given Resumable coordination cycle ledger is available, when the user exercises it, then they can the coordination host receives one typed, persisted next action for a roadmap or workspace cycle instead of reconstructing state from scattered artifacts.

## Functional Requirements
- FR-001: Inspect roadmap/workspace ledger inputs into stable unit ids, checked state, dependency edges, evidence pointers, and every dependency-ready candidate; reject malformed or ambiguous ledger state. (Stories: US-001; Acceptance: AC-001)
- FR-002: Register one stable cycle id bound to unit, executor, base commit, and repository; restart discovers that live cycle rather than minting another. (Stories: US-001; Acceptance: AC-001)
- FR-003: Consume SDD, critique, and feedback artifacts only through versioned provider receipts that bind work id, source currency, schema/generator identity, exact candidate head, round, and verdict; malformed, stale, wrong-cycle, incomplete, mismatched, or player-journey-missing evidence refuses. (Stories: US-001; Acceptance: AC-001)
- FR-004: Advance only where exact implementation/critic/head chains and activation/merged evidence agree on one cycle; preserve tenth-round escalation semantics. (Stories: US-001; Acceptance: AC-001)
- FR-005: Guard ledger update against stale source and bind it to the merged PR and evidence paths; final completion requires one accepted cycle per required unit, complete roll-up coverage, and dispositions for checkpoints/findings. (Stories: US-001; Acceptance: AC-001)
- FR-006: Keep sequential execution as default; require explicit ledger-disjointness and operator authorization for parallel milestones. (Stories: US-001; Acceptance: AC-001)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- This specification is an SDD lifecycle artifact and command-report contract input.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2133-resumable-cycle-ledger`.
