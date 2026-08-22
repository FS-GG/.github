---
schemaVersion: 1
workId: 2723-fence-arming-premise-rot
title: Arm merge fence and repair repos.sh premise drift
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Arm merge fence and repair repos.sh premise drift Specification

Prose status: specified

## User Value
Make the landed GitHub-native merge fence binding only after each receiver has demonstrated a real, producible check and an explicit merge-queue decision.

## Scope
- SB-001: Repair the stale administration-write credential guidance in scripts/repos.sh, document and execute the five-step per-repository arming sequence, evidence the residuals and rollback order, and leave the producer workflow unchanged.

## Non-Goals
- SB-002: Do not implement later lifecycle commands or Governance enforcement in this specification.

## User Stories
- US-001 (P1): As a user, I can make the landed GitHub-native merge fence binding only after each receiver has demonstrated a real, producible check and an explicit merge-queue decision.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given Arm merge fence and repair repos.sh premise drift is available, when the user exercises it, then they can make the landed GitHub-native merge fence binding only after each receiver has demonstrated a real, producible check and an explicit merge-queue decision.

## Functional Requirements
- FR-001: Before any apply, observe the exact claim-fence context on a real pull request, prove it statically producible, confirm a full 120-minute observe-only window, census every open item pull request for a current authorization marker, record the merge-queue decision, dry-run each target, then apply hub-first and per receiver with read-back. (Stories: US-001; Acceptance: AC-001)
- FR-002: The documentation must name exact immutable evidence for each arming precondition, per-repository result, accepted residual, and rollback sequence. (Stories: US-001; Acceptance: AC-001)
- FR-003: The scripts/repos.sh credential guidance must state the current App administration:write capability without implying that a workflow GITHUB_TOKEN can hold it. (Stories: US-001; Acceptance: AC-001)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- This specification is an SDD lifecycle artifact and command-report contract input.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2723-fence-arming-premise-rot`.
