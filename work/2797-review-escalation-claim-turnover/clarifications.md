---
schemaVersion: 1
workId: 2797-review-escalation-claim-turnover
title: Review Escalation Claim Turnover
stage: clarify
changeTier: tier1
status: needsAnswers
sourceSpec: work/2797-review-escalation-claim-turnover/spec.md
publicOrToolFacingImpact: true
---

# Review Escalation Claim Turnover Clarifications

## Source Specification
- work/2797-review-escalation-claim-turnover/spec.md

## Clarification Questions
- CQ-001 [AMB:AMB-001]: Which existing durable facts are sufficient to distinguish a valid post-turnover escalation from unauthorized claim replacement?
- CQ-002 [AMB:AMB-002]: Where should the narrow authority decision live so pure projection and the production writer cannot drift?
- CQ-003 [AMB:AMB-003]: How does the projection expose exhaustion without manufacturing round four?

## Answers
- CQ-001: Require the exact completed repair-confirmation round-3 wait, the exhausted initial plus ordered confirmation 1/2/3 structured chain, the legacy escalation marker that documents unresolved ordinary exhaustion, exact item/PR/head/review-generation/digest bindings, absence of a prior structured escalation, and a current claimant whose generation differs from the released wait claim.
- CQ-002: Add a typed `ReviewWait` authority classifier consumed by the live `review record` writer before mutation; keep review-state classification in the existing typed `Review` projection and exercise both through focused tests.
- CQ-003: Project the third changes-required confirmation plus valid exhaustion evidence as ordinary exhaustion and repair-phase handoff; never increment the ordinary confirmation round beyond three.

## Decisions
- DEC-001 [AMB:AMB-001] [FR-001] [FR-003]: A changed-claim escalation grant exists only for the exact exhausted ordinary chain, completed exact round-3 wait, legacy escalation evidence, no prior structured escalation, and one current fresh replacement claim.
- DEC-002 [AMB:AMB-002] [FR-001] [FR-002] [FR-003]: `ReviewWait` owns the pure grant/refusal vocabulary; `ReviewApplication` and the live client consume it before writes, and no generic claim-generation relaxation is introduced.
- DEC-003 [AMB:AMB-003] [FR-004]: The ordinary projection terminates at confirmation round 3 and emits repair-phase/escalation state; the replacement claimant receives no authority for confirmation, pass, acceptance, or round four on the exhausted PR.
- DEC-004 [FR-005]: Pure tests enumerate every grant input and refusal dimension; the production writer reproduces old-claim enter+complete, release/current fresh claim, and exactly one escalation append, with subject mutations proving the gate fires.
- DEC-005 [FR-006]: Merge completes source delivery only; engine freshness determines a separately boarded coherent release/install obligation when unreleased wire-surface commits remain.

## Accepted Deferrals
No accepted deferrals recorded.

## Remaining Ambiguity
- None. AMB-001, AMB-002, and AMB-003 are resolved by DEC-001 through DEC-003.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 2797-review-escalation-claim-turnover`.
