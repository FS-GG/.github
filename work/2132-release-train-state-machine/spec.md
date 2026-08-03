---
schemaVersion: 1
workId: 2132-release-train-state-machine
title: "Coord release: resumable cross-repo NuGet train state machine"
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Coord release: resumable cross-repo NuGet train state machine Specification

Prose status: specified

## User Value
Release operators can resume a cross-repository NuGet release train after restart from one durable release-run identifier.

## Scope
- SB-001: Add release inspect, plan, advance, and verify commands and their typed state/evidence model without replacing producer-specific audit, workflow, or verification scripts.

## Non-Goals
- SB-002: Do not implement later lifecycle commands or Governance enforcement in this specification.

## User Stories
- US-001 (P1): As a user, I can release operators can resume a cross-repository NuGet release train after restart from one durable release-run identifier.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given Coord release: resumable cross-repo NuGet train state machine is available, when the user exercises it, then they can release operators can resume a cross-repository NuGet release train after restart from one durable release-run identifier.

## Functional Requirements
- FR-001: The coordinator must emit exactly one typed next action and exact missing receipt for every non-terminal run state. (Stories: US-001; Acceptance: AC-001)
- FR-002: The coordinator must derive ordered producers and refuse consumers whose required producer artifact or pin is unverified. (Stories: US-001; Acceptance: AC-001)
- FR-003: The coordinator must classify dual-feed state as none, org-only, public-only, both-equivalent, or disagree and terminally escalate partial or disagree states. (Stories: US-001; Acceptance: AC-001)
- FR-004: Automated fixtures must cover no-release, ordered multi-producer, missing package, partial publication, tag/commit mismatch, stale registry, missing consumer embedding, and complete train. (Stories: US-001; Acceptance: AC-001)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- This specification is an SDD lifecycle artifact and command-report contract input.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2132-release-train-state-machine`.
