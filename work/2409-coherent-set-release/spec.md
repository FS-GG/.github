---
schemaVersion: 1
workId: 2409-coherent-set-release
title: Coherent Set Release
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Coherent Set Release Specification

Prose status: specified

## User Value
Cut the deferred FS.GG.Kit/FS.GG.Drivers/coord-engine coherent-set release so the fleet's sole coordination-engine distribution path has one trigger and a real published, verified release instead of three independently-tagged workflows and an unpublished version scalar.

## Scope
- SB-001: One consolidated release workflow (or reusable job with three thin callers) triggered from one shared tag/dispatch that dual-feed-publishes FS.GG.Kit, FS.GG.Drivers and coord-engine from the shared FsggCoherentSetVersion in a stated dependency-publish order.
- SB-002: A real coherent-set release is cut and published to both feeds; registry/dependencies.yml's coord-engine row is flipped to the new version; a receiver's real dotnet tool restore / package restore is verified (not inferred from a source build) to restore all three packages at the same version.
- SB-003: Re-confirmation of DEC-002 (.github#2402) that no existing coherence gate is deletable, re-evaluated against the new consolidated release workflow design; any gate the new design makes genuinely unreachable is deleted and named, otherwise justified in one line each.
- SB-004: A migration note extending docs/registry/compatibility.md's Coherent-set versioning section with the actual cut version and publish evidence.
- SB-005: An explicit statement of whether the one-time cross-package version jump this release causes fits inside .github#2396's decided permitted-lag bound for every receiver, or whether a coordinated fan-out per .github#2249's AC1/AC2 is owed alongside it.

## Non-Goals
- SB-006: Do not implement later lifecycle commands or Governance enforcement in this specification.

## User Stories
- US-001 (P1): As a user, I can cut the deferred FS.GG.Kit/FS.GG.Drivers/coord-engine coherent-set release so the fleet's sole coordination-engine distribution path has one trigger and a real published, verified release instead of three independently-tagged workflows and an unpublished version scalar.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given Coherent Set Release is available, when the user exercises it, then they can cut the deferred FS.GG.Kit/FS.GG.Drivers/coord-engine coherent-set release so the fleet's sole coordination-engine distribution path has one trigger and a real published, verified release instead of three independently-tagged workflows and an unpublished version scalar.

## Functional Requirements
- FR-001: A single workflow trigger (tag push or dispatch) results in FS.GG.Kit, FS.GG.Drivers and coord-engine all being packed, verified and dual-feed-published together at the shared coherent-set version, in a stated dependency order, with no path left for one member to publish without the other two. (Stories: US-001; Acceptance: AC-001)
- FR-002: After the release, both feeds (org GitHub Packages and nuget.org) resolve the new version for all three packages, registry/dependencies.yml's coord-engine row matches it, and a real dotnet tool restore in an isolated environment proves all three restore at that version. (Stories: US-001; Acceptance: AC-001)
- FR-003: Every coherence gate named in .github#2402's evidence section is re-evaluated against the new workflow and the PR states, gate by gate, keep-and-why or delete-and-why. (Stories: US-001; Acceptance: AC-001)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- This specification is an SDD lifecycle artifact and command-report contract input.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2409-coherent-set-release`.
