---
schemaVersion: 1
workId: 2178-workspace-collaborator-only-issues
title: "Workspace collaborator-only issues and Project access security"
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Workspace collaborator-only issues and Project access security Specification

Prose status: specified

## User Value
Workspace operators get typed, restart-safe repository issue policy and Project access receipts without treating public content as authority.

## Scope
- SB-001: Extend new-sdd-workspace, fleet audits, durable provenance, documentation, and release obligations for collaborator-only issue intake and least-privilege Project access.

## Non-Goals
- SB-002: Do not implement later lifecycle commands or Governance enforcement in this specification.

## User Stories
- US-001 (P1): As a workspace operator, I can provision and resume repository and Project security from typed receipts without treating public content as authority.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given workspace security provisioning is available, when the user exercises it, then they receive typed, restart-safe repository issue policy and Project access receipts without treating public content as authority.

## Functional Requirements
- FR-001: Existing repositories converge to IssueCreationPolicy.COLLABORATORS_ONLY with typed prior/final/actor/source receipts; disabled Issues are compliant and unreadable state is no-verdict. (Stories: US-001; Acceptance: AC-001)
- FR-002: Existing Projects apply supported visibility and requested writer grants through typed GraphQL variables, but mutation payloads are never represented as effective access reads. (Stories: US-001; Acceptance: AC-001)
- FR-003: Durable provenance independently records partial Project facts, one deduplicated human obligation for organization base Read plus the exact effective/exclusive writer set, and an idempotent two-fact completion command. (Stories: US-001; Acceptance: AC-001)
- FR-004: Fresh resources record pending obligations and never claim security before creation. (Stories: US-001; Acceptance: AC-001)
- FR-005: Tests parse the GraphQL request, materialize structured variables, and cover changed/stale reads, unexpected effective writers, failure, redaction, idempotency, and persistence. (Stories: US-001; Acceptance: AC-001)
- FR-006: Publish FS.GG.NewSddWorkspace 0.9.0 byte-identically before updating registry truth. (Stories: US-001; Acceptance: AC-001)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- This specification is an SDD lifecycle artifact and command-report contract input.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2178-workspace-collaborator-only-issues`.
