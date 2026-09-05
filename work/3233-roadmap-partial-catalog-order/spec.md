---
schemaVersion: 1
workId: 3233-roadmap-partial-catalog-order
title: Accept ordered partial roadmap catalogs
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Accept ordered partial roadmap catalogs Specification

Prose status: specified

## User Value
Production work-unit preparation succeeds for the canonical GS2-07.3 roadmap and its ordered partial Coordination catalog.

## Scope
- SB-001: RoadmapWorkUnit preparation identity validation, focused regression coverage, and coherent-set 0.83.1 patch release.

## Non-Goals
- SB-002: Do not implement later lifecycle commands or Governance enforcement in this specification.

## User Stories
- US-001 (P1): As a user, I can production work-unit preparation succeeds for the canonical GS2-07.3 roadmap and its ordered partial Coordination catalog.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given Accept ordered partial roadmap catalogs is available, when the user exercises it, then they can production work-unit preparation succeeds for the canonical GS2-07.3 roadmap and its ordered partial Coordination catalog.

## Functional Requirements
- FR-001: Given the exact production roadmap, catalog, and request, preparation selects GS2-07.3 with accepted prerequisite GS2-07.2. (Stories: US-001; Acceptance: AC-001)
- FR-002: Catalog IDs through the selected row equal canonical roadmap order filtered to those catalog IDs. (Stories: US-001; Acceptance: AC-001)
- FR-003: Reordered catalog IDs, a noncanonical first-unchecked selection, or a missing/non-immediate accepted prerequisite remain rejected. (Stories: US-001; Acceptance: AC-001)
- FR-004: No command, option, result, or exit-code vocabulary changes. (Stories: US-001; Acceptance: AC-001)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- This specification is an SDD lifecycle artifact and command-report contract input.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 3233-roadmap-partial-catalog-order`.
