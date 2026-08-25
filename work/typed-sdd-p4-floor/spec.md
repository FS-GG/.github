---
schemaVersion: 1
workId: typed-sdd-p4-floor
title: P4 Typed SDD registry floor
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# P4 Typed SDD registry floor Specification

Prose status: specified

## User Value
Workspace consumers can rely on one authoritative minimum compiler floor for the typed-sdd lifecycle.

## Scope
- SB-001: Advance the fs-gg-ui-template and fs-gg-workspace-template registry mirrors to the already-published FS.GG.SDD.Cli 1.4.0-preview.1 and record provenance; do not change lifecycle defaults.

## Non-Goals
- SB-002: Do not implement later lifecycle commands or Governance enforcement in this specification.

## User Stories
- US-001 (P1): As a user, I can workspace consumers can rely on one authoritative minimum compiler floor for the typed-sdd lifecycle.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given P4 Typed SDD registry floor is available, when the user exercises it, then they can workspace consumers can rely on one authoritative minimum compiler floor for the typed-sdd lifecycle.

## Functional Requirements
- FR-001: Registry validation and projections pass with both provider families declaring 1.4.0-preview.1, and FS.GG.Templates five-descriptor equality validation becomes green. (Stories: US-001; Acceptance: AC-001)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- This specification is an SDD lifecycle artifact and command-report contract input.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work typed-sdd-p4-floor`.
