---
schemaVersion: 1
workId: 3194-coherent-release
title: Coherent Release 0.80.0
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Coherent Release 0.80.0 Specification

Prose status: specified

## User Value
Release operators can prove that one canonical source commit produced every sibling tag and served package byte.

## Scope
- SB-001: Complete and verify coherent-set/v0.80.0 from exact source 4b93b10d8baf67c283aeed2f71b955cc7e721872 through the repository release saga.
- SB-002: Verify both feeds, sibling tags, clean installs, receiver dispatch outcomes, registry reconciliation, and terminal lifecycle telemetry.

## Non-Goals
- SB-003: Do not implement later lifecycle commands or Governance enforcement in this specification.

## User Stories
- US-001 (P1): As a user, I can release operators can prove that one canonical source commit produced every sibling tag and served package byte.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given Coherent Release 0.80.0 is available, when the user exercises it, then they can release operators can prove that one canonical source commit produced every sibling tag and served package byte.

## Functional Requirements
- FR-001: Publication must use release-saga-start and its stored prepare-once manifest; component tags must never be pushed directly. (Stories: US-001; Acceptance: AC-001)
- FR-002: Verification must read GitHub Packages and NuGet.org, compare every package payload excluding only NuGet.org signature material, validate sibling tag equality, and exercise clean installs. (Stories: US-001; Acceptance: AC-001)
- FR-003: Completion must record promoted stable-channel evidence, release manifest and content digests, receiver delivery outcomes, protected-main checks, and any independently schedulable reconciliation work. (Stories: US-001; Acceptance: AC-001)
- FR-004: The item must retain its claim and append phase-level lifecycle telemetry with model, toolchain, whole-minute duration, and honest token provenance through terminal delivery. (Stories: US-001; Acceptance: AC-001)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- This specification is an SDD lifecycle artifact and command-report contract input.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 3194-coherent-release`.
