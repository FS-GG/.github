---
schemaVersion: 1
workId: 2907-blocked-by-set-mutations
title: Blocked-by Set Mutations
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Blocked-by Set Mutations Specification

Prose status: specified

## User Value
Operators can add or remove one dependency edge without overwriting concurrent edges, and lint diagnoses inert body-only declarations.

## Scope
- SB-001: Explicit set-valued Blocked by add/remove/replace/clear CLI semantics, one shared mutation lease for every authoritative writer, revision-bound stale detection, lint projection diagnostics, and focused mutation-proven tests.

## Non-Goals
- SB-002: Changing the Projects v2 field as dependency authority or changing unrelated board fields.

## User Stories
- US-001 (P1): An operator safely mutates one blocker while preserving every other live blocker.
- US-002 (P1): A maintainer sees body-only or divergent Blocked by projections in lint.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001] [FR-003]: Given live `Blocked by` contains `FS-GG/.github#290`, when an operator explicitly adds `FS-GG/.github#299` against the observed item revision, then the resulting value contains both canonical edges.
- AC-002 [US-001] [FR-002] [FR-003]: Given live `Blocked by` contains `#290, #299`, when an operator explicitly removes `#290` against the observed item revision, then `#299` remains and only `#290` is removed.
- AC-003 [US-001] [FR-003]: Given any authoritative `Blocked by` writer is active, when add/remove, explicit replacement/clear, batch, release, intake, or reconcile attempts a competing mutation, then all writers join the same server-ordered lease; after winning the lease, a derived writer also rejects a changed field value or Projects item revision before mutation.
- AC-004 [US-001] [FR-004]: Given a caller targets `Blocked by`, when it requests replacement or clearing, then replacement is explicitly named and clearing uses the distinct clear operation; an ambiguous bare replacement is refused before mutation.
- AC-005 [US-002] [FR-005]: Given a body contains a live `Blocked by:` line, when the authoritative board field is empty or differs, then lint reports the inert or divergent projection; when canonical sets agree, lint remains green for that rule.
- AC-006 [US-001] [US-002] [FR-006]: Given each new gate is inverted independently, when focused suites run against the bounded fixture, then the relevant add-preservation, remove-preservation, shared-lease bypass, stale-write, or lint-projection test turns red and returns green after restoration.

## Functional Requirements
- FR-001: `Blocked by` add canonicalizes and unions requested refs with the live canonical set, removes duplicates, and preserves every existing edge. (Stories: US-001; Acceptance: AC-001)
- FR-002: `Blocked by` remove canonicalizes and subtracts requested refs from the live canonical set, preserves every non-requested edge, and uses the distinct clear mutation when the result is empty. (Stories: US-001; Acceptance: AC-002)
- FR-003: Because Projects-v2 exposes no compare-and-set mutation, every authoritative `Blocked by` writer (add/remove, explicit replacement/clear, batch, release, intake, and reconcile) acquires the same server-ordered issue-comment mutation lease before observing or writing the field and retains it through the GraphQL mutation. Add/remove additionally bind the live field value and Projects-v2 item revision; a changed value or revision at the guarded write boundary fails stale and emits no mutation. (Stories: US-001; Acceptance: AC-001, AC-002, AC-003)
- FR-004: The command names add, remove, replace, and clear as distinct caller intents for `Blocked by`; an ambiguous bare replacement is refused while unrelated scalar-field syntax remains compatible. (Stories: US-001; Acceptance: AC-004)
- FR-005: Lint reports a body-only `Blocked by:` declaration when the authoritative field is empty and a divergent projection when their canonical sets differ, while an agreeing generated projection stays green. Invalid body syntax is reported rather than promoted to authority. (Stories: US-002; Acceptance: AC-005)
- FR-006: Focused tests and gate inversion prove add preserves `#290` while adding `#299`, remove preserves `#299` while removing `#290`, batch and release cannot bypass an active derived writer's lease, a changed observation is never overwritten, and lint distinguishes inert, divergent, invalid, absent, and agreeing body projections. (Stories: US-001, US-002; Acceptance: AC-006)

## Ambiguities
- AMB-001: The public spelling for the four explicit `Blocked by` intents must preserve the existing parser's total flag-residue checks and avoid changing unrelated fields.
- AMB-002: GitHub Projects-v2 does not expose an `If-Match` argument on field mutation; the plan must serialize every authoritative writer through the same lease, state where derived revisions are revalidated inside that lease, and must not describe the secondary re-read as atomic authority.

## Public Or Tool-Facing Impact
- `set-field` gains explicit set-mutation syntax and updated help/command-contract documentation.
- `lint` gains a stable finding code for inert or divergent body projections.
- No serialized board field or SDD schema changes.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2907-blocked-by-set-mutations`.
