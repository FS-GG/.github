---
schemaVersion: 1
workId: 2859-review-wait-evidence-ref
title: Host-Owned Review Wait Boundary
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Host-Owned Review Wait Boundary Specification

Prose status: specified

## User Value
Review waits can be entered and completed without hand-transcribing state the engine already owns.

## Scope
- SB-001: Host-owned wait entry, engine-derived generation, terminal evidence validation and normalization, CLI/docs/tests.

## Non-Goals
- SB-002: Do not implement later lifecycle commands or Governance enforcement in this specification.

## User Stories
- US-001 (P1): A host enters the required review wait without authoring JSON or generation tokens.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given a live claim and PR awaiting review, when the host runs `review wait enter` without an event file, then the engine derives claim generation, current head, required kind, and round and writes exactly one canonical entry.
- AC-002 [US-001] [FR-002]: Given a completed critic record and a completion request citing its prose marker, when the host completes the wait, then the engine either resolves the marker to that structured record or refuses before any terminal append and identifies the required record.
- AC-003 [US-001] [FR-003]: Given an existing caller using an explicit wait-event file, when it enters or completes a valid wait, then the command remains accepted during migration.
- AC-004 [US-001] [FR-004]: Given production-shaped initial and confirmation fixtures, when the focused gate and its subject-breaking inversions run, then canonical entry succeeds and malformed generation or evidence routes fail.

## Functional Requirements
- FR-001: A host-owned `review wait enter` form MUST derive the current claim generation, PR head, required review kind, round, generation token, timestamps, and durable evidence anchor from live state; callers MUST NOT author or override those fields. (Stories: US-001; Acceptance: AC-001)
- FR-002: Before appending a terminal completion, the engine MUST validate that `evidenceRef` identifies the required structured review-decision record, or normalize a uniquely resolvable prose marker to that record; an invalid or ambiguous pointer MUST be refused before the immutable append with a diagnostic naming the required record. (Stories: US-001; Acceptance: AC-002)
- FR-003: Existing valid explicit event-file entry and terminal-event invocations MUST remain compatible during a documented migration period. (Stories: US-001; Acceptance: AC-003)
- FR-004: Production-shaped tests MUST execute initial and repair-confirmation entry, completion by structured record, marker-reference handling, stale/unreadable live-state refusals, and subject-breaking inversion controls. (Stories: US-001; Acceptance: AC-004)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- `scripts/fsgg-coord review wait` gains a host-owned entry invocation that does not require a JSON body.
- Help, structured-decision documentation, and both pnext skill projections state the canonical entry and completion-reference contracts.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2859-review-wait-evidence-ref`.
