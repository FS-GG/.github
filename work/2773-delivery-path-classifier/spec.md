---
schemaVersion: 1
workId: 2773-delivery-path-classifier
title: Single authoritative delivery path classifier
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Single authoritative delivery path classifier Specification

Prose status: specified

## User Value
Delivery and verify-paths must share one authoritative typed path-admission decision.

## Scope
- SB-001: FS.GG.Coord.Core path classification, CLI authority-input adapters, delivery and verify-paths projections, operator diagnostics, and focused parity tests.

## Non-Goals
- SB-002: Do not implement later lifecycle commands or Governance enforcement in this specification.

## User Stories
- US-001 (P1): As a user, I can delivery and verify-paths must share one authoritative typed path-admission decision.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given Single authoritative delivery path classifier is available, when the user exercises it, then they can delivery and verify-paths must share one authoritative typed path-admission decision.

## Functional Requirements
- FR-001: Every declared, generated, SDD-package, undeclared, and unreadable-authority case yields one typed classification with a reason; both callers produce the same authorization result for that classification. (Stories: US-001; Acceptance: AC-001)
- FR-002: Mandatory work/2773-delivery-path-classifier and readiness/2773-delivery-path-classifier files are admitted on the sdd-required route without widening Paths. (Stories: US-001; Acceptance: AC-001)
- FR-003: A genuinely undeclared authored file and any unreadable authority input cannot authorize landing. (Stories: US-001; Acceptance: AC-001)
- FR-004: repairReviewHandoff includes its underlying delivery problem text. (Stories: US-001; Acceptance: AC-001)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- This specification is an SDD lifecycle artifact and command-report contract input.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2773-delivery-path-classifier`.
