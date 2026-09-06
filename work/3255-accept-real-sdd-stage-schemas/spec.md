---
schemaVersion: 1
workId: 3255-accept-real-sdd-stage-schemas
title: Accept Real SDD Stage Schemas
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Accept Real SDD Stage Schemas Specification

Prose status: specified

## User Value
Roadmap acceptance can seal a real SDD 1.5.0 work item without weakening immutable candidate authority.

## Scope
- SB-001: Stage-specific analyze, verify, and ship observation validation plus isolated qualification execution.

## Non-Goals
- SB-002: Do not implement later lifecycle commands or Governance enforcement in this specification.

## User Stories
- US-001 (P1): As a user, I can roadmap acceptance can seal a real SDD 1.5.0 work item without weakening immutable candidate authority.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given Accept Real SDD Stage Schemas is available, when the user exercises it, then they can roadmap acceptance can seal a real SDD 1.5.0 work item without weakening immutable candidate authority.

## Functional Requirements
- FR-001: Validate analyze and verify against exact work, stage, ready status, generator, work-model source, and absence of blocking findings while permitting non-blocking warnings. (Stories: US-001; Acceptance: AC-001)
- FR-002: Validate ship against exact work, stage, shipReady status, generator, sourcesDigest, verificationReady status, and empty disposition blockingFindingIds. (Stories: US-001; Acceptance: AC-001)
- FR-003: Execute production qualification in a disposable exact-tree checkout so generated SDD projections cannot mutate the immutable candidate checkout. (Stories: US-001; Acceptance: AC-001)
- FR-004: Preserve exact qualification artifact binding, HEAD and tree identity, canonical observation equality, mutation controls, and every existing live acceptance authority. (Stories: US-001; Acceptance: AC-001)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- This specification is an SDD lifecycle artifact and command-report contract input.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 3255-accept-real-sdd-stage-schemas`.
