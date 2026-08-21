---
schemaVersion: 1
workId: 2807-review-escalation-head-progression
title: Review Escalation Head Progression
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Review Escalation Head Progression Specification

Prose status: specified

## User Value
An exhausted review chain with legitimate repaired heads can enter its one bounded repair phase after claim turnover.

## Scope
- SB-001: Structured escalation authorization and its production writer fixture only; no round four, no S.I.R. source changes, and no unrelated review semantics.

## Non-Goals
- SB-002: Do not implement later lifecycle commands or Governance enforcement in this specification.

## User Stories
- US-001 (P1): As a board driver, I can append the one structured escalation for a genuinely exhausted multi-round chain after claim turnover, so the authorized repair phase starts without rewriting history or weakening exact-head safety.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given initial review and confirmation rounds 1, 2, and 3 bind ordered heads `H0`, `H1`, `H2`, and `H3`, when a fresh claimant records the authorized exhaustion escalation for `H3`, then exactly one structured escalation is appended.
- AC-002 [US-001] [FR-002]: Given the same valid chain, when validation examines an historical record, then that record remains bound to its own exact head and its successor remains bound by the ordered predecessor URL, digest, critic, and round sequence.
- AC-003 [US-001] [FR-003]: Given a stale final head, missing/noncontiguous round, changed critic, malformed predecessor or legacy backlink, unchanged/stale claim, duplicate escalation, or attempted round four, when `review record` runs, then it refuses before any GitHub mutation.
- AC-004 [US-001] [FR-004]: Given the production writer fixture, when it exercises the changed-claim route with four distinct heads and a completed round-three wait, then the exact valid escalation passes and every specified subject mutation reds.
- AC-005 [US-001] [FR-005]: Given the source change is merged, when engine freshness is evaluated, then a coherent released engine is published and publicly installable before S.I.R. resumes its blocked repair phase.

## Functional Requirements
- FR-001: Cross-claim ordinary-exhaustion authorization MUST accept initial and confirmation rounds 1/2/3 whose exact heads advance, while the escalation draft, completed round-three wait, legacy marker, and live PR all bind the final round-three head. (Stories: US-001; Acceptance: AC-001)
- FR-002: Every structured decision MUST remain exact-head-bound to its own reviewed revision, and the chain MUST preserve ordered predecessor URL, digest, critic identity, and contiguous round semantics across head progression. (Stories: US-001; Acceptance: AC-002)
- FR-003: The writer MUST refuse stale final heads, missing or noncontiguous rounds, changed critics, malformed predecessor or legacy backlinks, unchanged/stale claims, duplicates, and round four before writes. (Stories: US-001; Acceptance: AC-003)
- FR-004: The production writer fixture MUST use four distinct deterministic heads and demonstrate one exact valid escalation plus fail-before-write mutations for every acceptance boundary it adds or changes. (Stories: US-001; Acceptance: AC-004)
- FR-005: The repaired behavior MUST be released as a coherent engine artifact and verified through public install before the blocked S.I.R. consumer resumes; this item MUST NOT edit S.I.R. source. (Stories: US-001; Acceptance: AC-005)

## Ambiguities
- AMB-001: Which head equalities are true terminal invariants and which incorrectly compare historical records with the final escalation head?
- AMB-002: How should the fixture prove both legitimate head progression and the existing fail-closed chain invariants without broadening production authority?
- AMB-003: Does this source item cut the owed engine release itself or file and sequence a separate coherent release item after merge?

## Public Or Tool-Facing Impact
- `fsgg-coord review record` accepts the already-valid multi-round exhaustion shape that public engine 0.68.0 rejects; marker schema and command syntax remain unchanged.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2807-review-escalation-head-progression`.
