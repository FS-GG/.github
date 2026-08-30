---
schemaVersion: 1
workId: 3068-repair-phase-live-assertion-writer
title: Repair Phase Live Assertion Writer
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Repair Phase Live Assertion Writer Specification

Prose status: specified

## User Value
Review hosts can durably authorize an accountable same-head repair assertion through the typed live CLI.

## Scope
- SB-001: Live review inspect, wait-enter, and review-record wiring plus lifecycle and end-to-end coverage; no compatibility event or no-op commit authority.

## Non-Goals
- SB-002: Do not change ordinary or repair-phase ceilings, authorize no-op commits, or add compatibility explicit-event authority.
- SB-003: Do not alter or merge the parked GS2-03.7 implementation while this blocker is being fixed.

## User Stories
- US-001 (P1): As a review host, I can append an accountable repair assertion and receive the next live protocol command without manufacturing a tree change.
- US-002 (P1): As a critic or landing host, I can re-read the assertion from durable PR authority and verify every binding before advancing the chain.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given an unchanged repaired head after a changes-required review, when an accountable third party writes the typed assertion, then live inspect emits the repair-confirmation wait command and wait-enter succeeds.
- AC-002 [US-001] [FR-002]: Given validated ordinary exhaustion, a fresh claim and PR, and an initial changes-required record on the already-repaired head, when the assertion and wait are present, then a kind=repair-phase record is reachable and a fresh successor pass can reach host acceptance.
- AC-003 [US-002] [FR-003]: Given a stale head, wrong review URL, wrong PR, implementer grantor, current-critic grantor, duplicate assertion, or malformed assertion, when the live reader or writer evaluates it, then it refuses without advancing review state.
- AC-004 [US-002] [FR-004]: Given an item, a separately numbered exhausted predecessor PR, and a fresh current PR, when live review state is inspected, then the item timeline and predecessor ledger select the exact purpose-bearing next command without assuming item/PR number equality.

## Functional Requirements
- FR-001: After ordinary exhaustion and an accountable assertion bound to the exact review, head, and disjoint grantor identity, the live oracle MUST make repair-confirmation wait and kind=repair-phase record reachable without changing the candidate head. (Stories: US-001; Acceptance: AC-001)
- FR-002: The production review path MUST carry the durable assertion through inspect, wait-enter, and review-record so exhaustion through repair-phase successor pass and host acceptance is executable. (Stories: US-001; Acceptance: AC-002)
- FR-003: The writer and reader MUST reject stale, wrong-subject, self-granted, critic-granted, duplicate, and malformed assertions without selecting a latest-wins fallback. (Stories: US-002; Acceptance: AC-003)
- FR-004: The CLI contract and live oracle MUST render the exact authorized writer or next command, including all required bindings, by resolving a unique exhausted predecessor from typed item cross-references and its review ledger rather than requiring hand-authored authority or item/PR number equality. (Stories: US-002; Acceptance: AC-004)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- Adds a typed live review CLI writer and extends live review command guidance; documentation and command-contract tests change with the implementation.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 3068-repair-phase-live-assertion-writer`.
