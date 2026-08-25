---
schemaVersion: 1
workId: typed-sdd-p4-registry
title: P4 Typed SDD registry and workspace creation contracts
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# P4 Typed SDD registry and workspace creation contracts Specification

Prose status: specified

## User Value
Every supported workspace entry point can preserve an explicit Typed SDD choice without silently changing Standard SDD, Freeform, or legacy spec-kit behavior.

## Scope
- SB-001: Organization dependency registry, lifecycle vocabulary/default contract, NewSddWorkspace wizard and scripted creation, generated compatibility projections, feed coherence, ADR/design guidance, and their tests.

## Non-Goals
- SB-002: Do not implement later lifecycle commands or Governance enforcement in this specification.

## User Stories
- US-001 (P1): As a user, I can every supported workspace entry point can preserve an explicit Typed SDD choice without silently changing Standard SDD, Freeform, or legacy spec-kit behavior.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given P4 Typed SDD registry and workspace creation contracts is available, when the user exercises it, then they can every supported workspace entry point can preserve an explicit Typed SDD choice without silently changing Standard SDD, Freeform, or legacy spec-kit behavior.

## Functional Requirements
- FR-001: Omitted lifecycle resolves to sdd throughout P4; typed-sdd is additive and is not the default until P5. (Stories: US-001; Acceptance: AC-001)
- FR-002: Registry authority lands before provider mirrors and names only published FS.GG.SDD 1.4.0-preview.1 and FS.GG.UI.Template 0.28.0 identities. (Stories: US-001; Acceptance: AC-001)
- FR-003: Registry lifecycle vocabulary is spec-kit|sdd|typed-sdd|none and the minimum fsgg-sdd floor names 1.4.0-preview.1 with provenance. (Stories: US-001; Acceptance: AC-001)
- FR-004: NewSddWorkspace accepts and preserves explicit none, sdd, and typed-sdd while omission resolves to sdd and invalid input fails actionably. (Stories: US-001; Acceptance: AC-001)
- FR-005: Registry changelog, ADR/design amendment, generated compatibility projection, feed coherence, and self-tests remain derived and current. (Stories: US-001; Acceptance: AC-001)
- FR-006: Wrong-default and lifecycle-loss mutation controls fail. (Stories: US-001; Acceptance: AC-001)
- FR-007: Existing explicit sdd, none, and frozen spec-kit behavior remains compatible. (Stories: US-001; Acceptance: AC-001)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- This specification is an SDD lifecycle artifact and command-report contract input.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work typed-sdd-p4-registry`.
