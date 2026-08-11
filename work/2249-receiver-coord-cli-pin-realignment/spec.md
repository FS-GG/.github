---
schemaVersion: 1
workId: 2249-receiver-coord-cli-pin-realignment
title: Receiver fs.gg.coord.cli pin realignment
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Receiver fs.gg.coord.cli pin realignment Specification

Prose status: specified

## User Value
Every coordination-kit receiver's fs.gg.coord.cli pin is verifiably comparable against the registry's declared coord-engine version, so drift is detected in a scheduled audit rather than discovered mid-review.

## Scope
- SB-001: Fresh live verification that (a) scripts/repos-audit.sh's engine-manifest sweep compares every coordination-kit receiver's own .config/dotnet-tools.json pin against registry/dependencies.yml's declared coord-engine version, (b) the sweep is wired into the scheduled repos-audit.yml workflow and correctly reports ok/drift per receiver on live data, and (c) tests/repos-audit/run.sh proves the sweep red under a version-mismatch mutation and green otherwise. No receiver repository is touched by this work item; a receiver's own pin moves through that receiver's own Renovate-managed dependency PR.

## Non-Goals
- SB-002: publishing a new coord-engine version, and moving any individual receiver's own pin, are both out of scope for this repository's Paths.

## User Stories
- US-001 (P1): As a user, I can every coordination-kit receiver's fs.gg.coord.cli pin is verifiably comparable against the registry's declared coord-engine version, so drift is detected in a scheduled audit rather than discovered mid-review.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given Receiver fs.gg.coord.cli pin realignment is available, when the user exercises it, then they can every coordination-kit receiver's fs.gg.coord.cli pin is verifiably comparable against the registry's declared coord-engine version, so drift is detected in a scheduled audit rather than discovered mid-review.

## Functional Requirements
- FR-001: scripts/repos-audit.sh's engine-manifest sweep, run against the live registry and live receiver manifests, reports ok for a receiver whose pin equals the declared coord-engine version and drift for a receiver whose pin differs, and tests/repos-audit/run.sh's engine-manifest suite is green before an injected version-mismatch mutation and red after it. (Stories: US-001; Acceptance: AC-001)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- This specification is an SDD lifecycle artifact and command-report contract input.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2249-receiver-coord-cli-pin-realignment`.
