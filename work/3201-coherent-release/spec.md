---
schemaVersion: 1
workId: 3201-coherent-release
title: Coherent Release 0.80.1
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Coherent Release 0.80.1 Specification

Prose status: specified

## User Value
Publish one byte-coherent 0.80.1 patch release containing the permanent supervisor telemetry process.

## Scope
- SB-001: Bump the coherent set to 0.80.1 from canonical main, update governed projections, use the automatic release saga, verify all three sibling packages and tags from one commit on GitHub Packages and NuGet.org, verify clean installs and packaged skill bytes, reconcile downstream registry/pins/docs, and retain phase telemetry through Done.

## Non-Goals
- SB-002: Do not bypass the automatic release saga, push component tags directly, overwrite an
  immutable published artifact, or expand unrelated product behavior.

## User Stories
- US-001 (P1): As a user, I can publish one byte-coherent 0.80.1 patch release containing the permanent supervisor telemetry process.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given Coherent Release 0.80.1 is available, when the user exercises it, then they can publish one byte-coherent 0.80.1 patch release containing the permanent supervisor telemetry process.

## Functional Requirements
- FR-001: The reviewed merge source must contain the supervisor telemetry skills and semantic enforcement introduced by issue 3199. (Stories: US-001; Acceptance: AC-001)
- FR-002: Publication must be atomic across FS.GG.Kit, FS.GG.Drivers, and FS.GG.Coord.Cli version 0.80.1 and promoted only after both public feeds are byte-equivalent except NuGet signature material. (Stories: US-001; Acceptance: AC-001)
- FR-003: Protected main, release evidence, receiver adoption, registry coherence, and terminal item lifecycle telemetry must all be verified before Done. (Stories: US-001; Acceptance: AC-001)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- This specification is an SDD lifecycle artifact and command-report contract input.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 3201-coherent-release`.
