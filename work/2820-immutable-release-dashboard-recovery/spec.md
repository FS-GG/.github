---
schemaVersion: 1
workId: 2820-immutable-release-dashboard-recovery
title: Immutable Release Dashboard Recovery
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Immutable Release Dashboard Recovery Specification

Prose status: specified

## User Value
An operator can retry the exact prepared source after both feeds have completed and release promotion has made the GitHub release immutable; the kit and coordination-engine runs verify immutable journal state without mutating it and still notify every roster receiver.

## Scope
- SB-001: Release-kit and coordination-engine workflows, their shared release-saga journal adapter, promotion ordering, the release-saga harness, and CI.

## Non-Goals
- SB-002: Never delete, replace, or rewrite immutable release assets; do not alter package payloads or receiver repositories.

## User Stories
- US-001 (P1): As a release operator, I can retry the exact prepared source after promotion and observe receiver dashboard delivery complete idempotently without weakening immutable release history.
- US-002 (P1): As a receiver owner, I can rely on a successful recovery run meaning the roster was non-empty, my dashboard write was attempted, and its result was read back.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given both feeds are complete and the coherent-set release is immutable, when release-kit retries the exact source, then journal persistence verifies the existing immutable journal without delete, replacement, clobber, or rewrite and the receiver dashboard step executes.
- AC-002 [US-001] [FR-002]: Given the same recovery state, when release-coord-engine retries the exact source, then it follows the same read-only journal recovery and reaches its receiver dashboard step.
- AC-003 [US-001] [FR-003]: Given a draft mutable release, when either package workflow advances its feed journal, then the adapter still uploads the changed journal and promotion still observes exactly three package journals before making the release immutable.
- AC-004 [US-001] [FR-004]: Given the immutable journal is missing, unreadable, or bound to another release identity, when recovery tries to persist it, then the adapter refuses and no dashboard-delivery success is claimed.
- AC-005 [US-002] [FR-005]: Given dashboard delivery runs, when the roster is empty, a receiver write is refused, or read-back cannot grade the result, then the workflow remains red; on a successful replay every roster-derived write is read back and duplicate retries are idempotent.
- AC-006 [US-001] [FR-006]: Given the release-saga CI fixture, when the immutable-recovery subject is mutated back to an unconditional clobber, then both package topology gates go red before review.

## Functional Requirements
- FR-001: Release-kit recovery MUST detect an immutable release, read back and validate the existing package journal, perform no release-asset mutation, and continue to receiver delivery. (Stories: US-001; Acceptance: AC-001)
- FR-002: Release-coord-engine recovery MUST use the identical immutable-journal contract and continue to receiver delivery. (Stories: US-001; Acceptance: AC-002)
- FR-003: Mutable draft runs MUST retain durable `--clobber` journal persistence, and promotion MUST continue to require the complete three-journal coherent set before publishing immutable stable-channel state. (Stories: US-001; Acceptance: AC-003)
- FR-004: Immutable recovery MUST fail closed when the remote journal cannot be read or validated against package, version, source SHA, release id, policy version, and prepared artifacts. (Stories: US-001; Acceptance: AC-004)
- FR-005: Receiver delivery MUST remain roster-derived, idempotent, and red for zero receivers, refused writes, or unreadable read-back; a successful run MUST report that every receiver was reached. (Stories: US-002; Acceptance: AC-005)
- FR-006: The touched release-saga gates MUST include a two-workflow immutable HTTP-422 escape reproduction, green read-only recovery, retry/idempotency, and subject-breaking red controls for clobber regression, missing/invalid remote journal, zero receivers, and refused dashboard writes. (Stories: US-001; Acceptance: AC-006)

## Ambiguities
- AMB-001 open: Should receiver delivery move to promotion, or should package recovery make journal persistence read-only after immutability so the existing delivery step remains reachable?
- AMB-002 open: Which immutable remote journal facts must be revalidated before a skipped upload is safe?
- AMB-003 open: How should the fixture prove both workflows reach dashboard delivery without copying GitHub Actions' step-state semantics into an ungrounded text assertion?

## Public Or Tool-Facing Impact
- Recovery semantics change for the public `release-kit` and `release-coord-engine` workflow_dispatch routes; their inputs, package bytes, feed behavior, dashboard mechanism, and release asset names remain compatible.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2820-immutable-release-dashboard-recovery`.
