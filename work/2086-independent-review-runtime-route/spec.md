---
schemaVersion: 1
workId: 2086-independent-review-runtime-route
title: Require critic production-route execution evidence
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Require critic production-route execution evidence Specification

Prose status: specified

## User Value
Critics catch route-divergence defects that source review and unit tests can miss.

## Scope
- SB-001: pnext-item independent-review contract, pnext-item worker guidance, both generated runtime views, review-contract fixture, SDD artifacts, and kit version.

## Non-Goals
- SB-002: Do not implement later lifecycle commands or Governance enforcement in this specification.

## User Stories
- US-001 (P1): As a user, I can critics catch route-divergence defects that source review and unit tests can miss.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given Require critic production-route execution evidence is available, when the user exercises it, then they can critics catch route-divergence defects that source review and unit tests can miss.

## Functional Requirements
- FR-001: For a meaningful runtime behavior reachable through more than one route, a critic must execute or measure a comparison through a production route against the built artifact and record the result; source review remains required; a fixture rejects a contract with this obligation removed. (Stories: US-001; Acceptance: AC-001)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- This specification is an SDD lifecycle artifact and command-report contract input.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2086-independent-review-runtime-route`.
