---
schemaVersion: 1
workId: 2102
title: "Converge receiver-proj migration shape across SDD, Audio, and Governance"
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Converge receiver-proj migration shape across SDD, Audio, and Governance Specification

Prose status: specified

## User Value
Future receiver migrations have one low-drift receiver-proj generation shape and executable proof that a wrong invocation fails.

## Scope
- SB-001: Decide the shared shape, publish durable migration guidance, and coordinate SDD, Audio, and Governance receiver convergence.

## Non-Goals
- SB-002: Do not implement later lifecycle commands or Governance enforcement in this specification.

## User Stories
- US-001 (P1): As a user, I can future receiver migrations have one low-drift receiver-proj generation shape and executable proof that a wrong invocation fails.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given Converge receiver-proj migration shape across SDD, Audio, and Governance is available, when the user exercises it, then they can future receiver migrations have one low-drift receiver-proj generation shape and executable proof that a wrong invocation fails.

## Functional Requirements
- FR-001: All three receivers must use receiver-proj generation, retain tool-owned swapped-root behaviour coverage, and reject a wrong invocation at the receiver call site. (Stories: US-001; Acceptance: AC-001)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- This specification is an SDD lifecycle artifact and command-report contract input.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2102`.
