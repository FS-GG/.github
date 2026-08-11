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
- SB-006 (repair 1): Lanes.partition and Schedulability.schedulable — the functions AC-1 names — apply the same generated-artifact exclusion directly, reached by the live take/batch/next scheduling path and by the lanes decision command, not only by overlap/activeCollisions
- SB-007 (repair 1): add prints a non-fatal advisory when the issue body it boards already declares a generated-artifact token, since add has no --paths argument for a refusal to intercept and the declaration already exists before add runs

## Non-Goals
- SB-004: the FS.GG.Kit csproj Version field is not in the generated-paths roster, so it is unaffected and continues to genuinely serialize check-kit-published-coherence's single-writer claim
- SB-005 (SUPERSEDED by SB-006 in repair 1 — kept for the record, not the current design): this line originally claimed "the scheduler's own lane-partitioning in Lanes.fs and Schedulability.fs is not touched; disjoint real touch-sets already lane concurrently once the generated token is kept out of every declaration." The independent critic's round-1 review proved that premise false by executing Lanes.partition and Schedulability.schedulable directly against the row's own #2254/#2248-shaped pair: neither function reached the exclusion, so a live take/batch could still refuse two items overlap already called DISJOINT. SB-006 is the corrected scope.

## User Stories
- US-001 (P1): As a worker whose edit only touches its own skill file, I can widen or claim without the engine reserving a generated manifest file against me, so a second worker whose real subject is disjoint is never serialized behind me for a file neither of us authors

## Acceptance Scenarios
- AC-001 [US-001] [FR-001] [FR-003] [FR-004] [FR-005] [FR-007]: given two items whose declared paths are their own disjoint skill files plus the same generated manifest token, when overlap, widen, take, batch, or lanes is asked, then the generated token contributes no collision and the items are DISJOINT / lane separately / schedule concurrently
- AC-002 [US-001] [FR-002]: given a widen request naming a generated artifact token, when the call is made, then it is refused, Paths stays byte-identical, and the message names ADR-0044
- AC-003 [US-001] [FR-006]: given generate-driver-manifest --check run against a tree with an unregenerated skill edit, when the check runs, then it exits non-zero
- AC-004 [US-001] [FR-008] (repair 1): given add boards an issue body whose Paths: already names a generated artifact, when add runs, then it boards successfully and prints a non-fatal advisory naming the token and ADR-0044

## Functional Requirements
- FR-001: TouchSet exposes a pure function that names the subset of a requested token list which exactly names one of a supplied set of generated-artifact paths (Stories: US-001; Acceptance: AC-001)
- FR-002: widen and set-paths refuse the whole call before any PATCH when a requested token exactly names a generated artifact, leaving Paths byte-identical, and name ADR-0044 and the offending token in the refusal (Stories: US-001; Acceptance: AC-002)
- FR-003: TouchSet exposes a pure function that drops a conflict pair when both sides stem to the same generated-artifact path, and activeCollisions and overlapCmd apply it using the existing generatedPathCollector (Stories: US-001; Acceptance: AC-001)
- FR-004: a genuinely non-generated shared path, and a shared generated path claimed alongside a genuinely disjoint directory declaration, still report OVERLAP (the ADR-0044 parent-directory trap is not reopened) (Stories: US-001; Acceptance: AC-001)
- FR-005: the FS.GG.Kit Version field continues to collide normally because it is absent from the generated-paths roster (Stories: US-001; Acceptance: AC-001)
- FR-006: generate-driver-manifest --check is left unmodified and continues to red when a skill body is edited without regenerating the manifest, proven by inverting it (dirty the artifact, run the guard, observe red) (Stories: US-001; Acceptance: AC-003)
- FR-007: (repair 1) Schedulability.schedulable and Lanes.partition accept the generated-artifact set and apply the same exclusion TouchSet.excludeGenerated states; Batch.scheduleWith/schedule thread it through from their callers; the live take/batch/next path resolves the real roster and the pure lanes/decide commands pass an explicit empty set (Stories: US-001; Acceptance: AC-001)
- FR-008: (repair 1) add advises, rather than refuses, when a boarded body's Paths: already names a generated artifact, because add has no request of its own for a PD-002-style refusal to intercept (Stories: US-001; Acceptance: AC-004)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- this changes FS.GG.Coord.Core's public TouchSet.fsi surface (two new, additive functions) AND, as of repair 1, makes a real non-additive signature change to Schedulability.fsi (schedulable gains a leading generated parameter), Lanes.fsi (partition, same), and Batch.fsi (scheduleWith and schedule, same) — every call site in this coherent set (FS.GG.Coord.Core, FS.GG.Coord.Cli, both test projects) was updated. It also changes the coord-engine CLI's widen/set-paths/overlap/take/batch/next/lanes/add behavior, which every FS-GG repo running verify-paths and widen depends on.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2305-generated-artifact-lanes`.
