---
schemaVersion: 1
workId: 2106-auto-publish
title: FS.GG.Kit auto-publish state machine
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# FS.GG.Kit auto-publish state machine Specification

Prose status: specified

## User Value
An owed FS.GG.Kit patch releases without a manual choreography while irreversible outcomes remain bounded and auditable.

## Scope
- SB-001: A deterministic scheduled/push workflow detects an authored-but-unpublished FS.GG.Kit version, validates eligibility, creates one tag, lets release-kit preserve all existing gates and structural one-pack dual-feed publication, then opens one evidence PR.

## Non-Goals
- SB-002: Tests and dry runs must not publish, tag, or mutate external feeds; no release-kit gate may be removed or weakened.

## User Stories
- US-001 (P1): As a user, I can an owed FS.GG.Kit patch releases without a manual choreography while irreversible outcomes remain bounded and auditable.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given FS.GG.Kit auto-publish state machine is available, when the user exercises it, then they can an owed FS.GG.Kit patch releases without a manual choreography while irreversible outcomes remain bounded and auditable.

## Functional Requirements
- FR-001: The workflow must refuse and sticky-escalate a major version, existing version on either feed, missing merged-pr provenance, feed disagreement, failed pr-arm gate, concurrent duplicate trigger, or partial publish; it must never retry a partial publication. (Stories: US-001; Acceptance: AC-001)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- This specification is an SDD lifecycle artifact and command-report contract input.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2106-auto-publish`.
