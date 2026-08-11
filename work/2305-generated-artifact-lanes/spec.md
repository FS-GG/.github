---
schemaVersion: 1
workId: 2305-generated-artifact-lanes
title: Generated skill manifests should not serialize disjoint skill-editing lanes
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Generated skill manifests should not serialize disjoint skill-editing lanes Specification

Prose status: specified

## User Value
Two items whose real subjects are disjoint but which both edit skill bodies can hold concurrent lanes, even though both would regenerate the same generated, CI-gated skill manifest

## Scope
- SB-001: widen and set-paths refuse any requested token that exactly names a generator-declared, CI-gated artifact per ADR-0044's own generated-paths roster, instead of silently granting it as a real reservation
- SB-002: the overlap collision verdict (activeCollisions and the overlap command) excludes a conflict pair when both sides stem to the same generator-declared artifact, so a pre-existing declaration of one cannot serialize a second worker either
- SB-003: a stale committed manifest is still detected exactly as before; generate-driver-manifest --check still reds on a skill-body edit that is not regenerated

## Non-Goals
- SB-004: the FS.GG.Kit csproj Version field is not in the generated-paths roster, so it is unaffected and continues to genuinely serialize check-kit-published-coherence's single-writer claim
- SB-005: the scheduler's own lane-partitioning in Lanes.fs and Schedulability.fs is not touched; disjoint real touch-sets already lane concurrently once the generated token is kept out of every declaration

## User Stories
- US-001 (P1): As a worker whose edit only touches its own skill file, I can widen or claim without the engine reserving a generated manifest file against me, so a second worker whose real subject is disjoint is never serialized behind me for a file neither of us authors

## Acceptance Scenarios
- AC-001 [US-001] [FR-001] [FR-003] [FR-004] [FR-005]: given two items whose declared paths are their own disjoint skill files plus the same generated manifest token, when overlap or widen is asked, then the generated token contributes no collision and the items are DISJOINT
- AC-002 [US-001] [FR-002]: given a widen request naming a generated artifact token, when the call is made, then it is refused, Paths stays byte-identical, and the message names ADR-0044
- AC-003 [US-001] [FR-006]: given generate-driver-manifest --check run against a tree with an unregenerated skill edit, when the check runs, then it exits non-zero

## Functional Requirements
- FR-001: TouchSet exposes a pure function that names the subset of a requested token list which exactly names one of a supplied set of generated-artifact paths (Stories: US-001; Acceptance: AC-001)
- FR-002: widen and set-paths refuse the whole call before any PATCH when a requested token exactly names a generated artifact, leaving Paths byte-identical, and name ADR-0044 and the offending token in the refusal (Stories: US-001; Acceptance: AC-002)
- FR-003: TouchSet exposes a pure function that drops a conflict pair when both sides stem to the same generated-artifact path, and activeCollisions and overlapCmd apply it using the existing generatedPathCollector (Stories: US-001; Acceptance: AC-001)
- FR-004: a genuinely non-generated shared path, and a shared generated path claimed alongside a genuinely disjoint directory declaration, still report OVERLAP (the ADR-0044 parent-directory trap is not reopened) (Stories: US-001; Acceptance: AC-001)
- FR-005: the FS.GG.Kit Version field continues to collide normally because it is absent from the generated-paths roster (Stories: US-001; Acceptance: AC-001)
- FR-006: generate-driver-manifest --check is left unmodified and continues to red when a skill body is edited without regenerating the manifest, proven by inverting it (dirty the artifact, run the guard, observe red) (Stories: US-001; Acceptance: AC-003)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- this changes FS.GG.Coord.Core's public TouchSet.fsi surface (two new functions) and the coord-engine CLI's widen/set-paths/overlap refusal and collision behavior, which every FS-GG repo running verify-paths and widen depends on

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2305-generated-artifact-lanes`.
