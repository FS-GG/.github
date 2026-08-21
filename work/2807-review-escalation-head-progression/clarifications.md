---
schemaVersion: 1
workId: 2807-review-escalation-head-progression
title: Review Escalation Head Progression
stage: clarify
changeTier: tier1
status: needsAnswers
sourceSpec: work/2807-review-escalation-head-progression/spec.md
publicOrToolFacingImpact: true
---

# Review Escalation Head Progression Clarifications

## Source Specification
- work/2807-review-escalation-head-progression/spec.md

## Clarification Questions
- CQ-001 [AMB:AMB-001]: Which head equalities are terminal invariants and which are per-record invariants?
- CQ-002 [AMB:AMB-002]: How does the fixture prove progression and preserve every existing mutation fence?
- CQ-003 [AMB:AMB-003]: How is the coherent release bound to this source repair?

## Answers
- CQ-001: Each initial/confirmation record binds the head it reviewed; ordered predecessor URL, digest, critic, and round link records across different heads. Only the round-three confirmation, completed wait, legacy exhaustion marker, escalation draft, and live PR must all equal the final head.
- CQ-002: Give initial and rounds 1/2/3 four fixed distinct 40-hex heads, retain the exact predecessor/digest/round links, and run the valid cross-claim escalation first. Then mutate the final head, round sequence, predecessor/backlink, claim freshness, duplication, and round-four route one at a time while checking the comment count stays unchanged.
- CQ-003: This item advances the authored coherent set to 0.69.0 and declares one mapped `coherent-set-release` obligation on the exact reviewed head. After guarded source merge, the same live claim publishes the immutable merge bytes to both feeds, verifies package identity and a clean public install for S.I.R., records registry evidence, and only then permits Done.

## Decisions
- **DEC-001** [CQ-001] [AMB:AMB-001] [FR-001] [FR-002]: Replace only the invalid all-records-equal-final-head predicate. Preserve each record's exact own head and require the terminal round-three confirmation, completed wait, legacy escalation, draft, and live PR to bind the same final head.
- **DEC-002** [CQ-002] [AMB:AMB-002] [FR-003] [FR-004]: The production writer fixture uses four distinct deterministic heads and preserves ordered predecessor/digest/critic/round facts; each changed gate receives one subject mutation that must refuse before writes.
- **DEC-003** [CQ-003] [AMB:AMB-003] [FR-005]: Source and release are one bounded delivery for 0.69.0: the exact reviewed head carries a mapped coherent-set-release obligation, and the current claim remains live through dual-feed identity, clean public install, registry evidence, and the S.I.R. handoff. No unreleased source build is used as the handoff.
- **DEC-004** [FR-003]: No generic claim-turnover relaxation is introduced. Authorization remains escalation-only after exact ordinary exhaustion and a fresh current claim; confirmation, acceptance, duplicate escalation, and round four remain unauthorized.

## Accepted Deferrals
No accepted deferrals recorded.

## Remaining Ambiguity
- None. The three blocking ambiguities are resolved by the decisions above.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 2807-review-escalation-head-progression`.
