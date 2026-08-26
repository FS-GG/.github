---
schemaVersion: 1
workId: 3014-repair-phase-turnover
title: Repair-phase turnover for admitted post-ceiling ledgers
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Repair-phase turnover for admitted post-ceiling ledgers Specification

Prose status: specified

## User Value
Restore the documented one-fresh-repair-phase route so an engine-admitted exhausted review chain can recover without bypassing typed authority.

## Scope
- SB-001: Generalize live exhaustion turnover to bind the actual terminal contiguous confirmation while preserving the exact three-round route and every fail-closed provenance check.
- SB-002: Add regression coverage for the five-record `.github#3012` / PR #3013 ledger shape and its fresh repair-phase successor.
- SB-003: Document the terminal-record binding used when an admitted chain extends beyond confirmation round three.

## Non-Goals
- NG-001: Do not raise `max-automated-repair-rounds`, weaken contiguous-round validation, or permit a second repair phase.
- NG-002: Do not rewrite, delete, or reinterpret existing structured decisions.
- NG-003: Do not change unrelated delivery, claim, or acceptance policy.

## User Stories
- US-001 (P1): As a user, I can restore the documented one-fresh-repair-phase route so an engine-admitted exhausted review chain can recover without bypassing typed authority.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given initial plus contiguous confirmations 1-4 whose terminal exact head is changes-required and whose wait is completed, when a fresh post-exhaustion claimant records escalation, then the engine seals one escalation bound to confirmation 4.
- AC-002 [US-001] [FR-002]: Given the historical initial plus confirmations 1-3 shape, when it turns over, then the existing marker and typed escalation route remain accepted without a new field.
- AC-003 [US-001] [FR-003]: Given a stale head, digest, terminal URL, completed-wait evidence, critic, or non-contiguous round, when escalation is attempted, then no structured record is written.
- AC-004 [US-001] [FR-004]: Given the exhausted predecessor is closed unmerged and a newer claim owns a fresh PR, when its repair-phase record carries the seven-field receipt naming the exact escalation, then entry is authorized once.
- AC-005 [US-001] [FR-005]: Given a longer admitted chain, when legacy exhaustion evidence is created, then it binds both the historical first three confirmations and the actual terminal confirmation unambiguously.

## Functional Requirements
- FR-001: The live escalation writer MUST derive the terminal confirmation from the validated current generation and bind its round, exact head, digest, critic, backlinks, and completed durable wait rather than destructuring exactly three confirmations. (Stories: US-001; Acceptance: AC-001)
- FR-002: The exact initial-plus-confirmations-1/2/3 turnover route MUST remain accepted and backward compatible. (Stories: US-001; Acceptance: AC-002)
- FR-003: Invalid ledgers, non-exhausted projections, stale or mismatched terminal authority, malformed/duplicate exhaustion evidence, and non-fresh claims MUST fail before any structured escalation write. (Stories: US-001; Acceptance: AC-003)
- FR-004: A fresh repair-phase PR MUST still require the closed unmerged predecessor, newer live claim, exact candidate head, distinct implementer and critic identities, and the seven-field receipt naming the typed escalation. (Stories: US-001; Acceptance: AC-004)
- FR-005: Exhaustion evidence for a generation longer than round three MUST name the actual terminal confirmation while retaining the existing confirmation-1/2/3 fields for compatibility. (Stories: US-001; Acceptance: AC-005)

## Ambiguities
- AMB-001: The existing legacy v1 marker has fixed confirmation-1/2/3 fields and no terminal-record field for an admitted longer chain.

## Public Or Tool-Facing Impact
- This specification is an SDD lifecycle artifact and command-report contract input.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 3014-repair-phase-turnover`.
