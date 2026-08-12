---
schemaVersion: 1
workId: 2417-critic-despawn-review-recovery
title: Critic Despawn Review Recovery
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/2417-critic-despawn-review-recovery/spec.md
publicOrToolFacingImpact: true
---

# Critic Despawn Review Recovery Clarifications

## Source Specification
- work/2417-critic-despawn-review-recovery/spec.md

## Clarification Questions
- CQ-001 [AMB:AMB-001] blocking open: Resolve source ambiguity AMB-001 before checklist.

## Answers
- CQ-001 [AMB:AMB-001] decision: resolved: when a CriticSuccessionReceipt is present but fails a guard (mismatched original critic, stale/mismatched head, or implementer as successor/granter), ResumeSameCritic names the refusal reason distinctly from the plain no-receipt-supplied case, matching the codebase convention of naming near-miss failures precisely (Driver.reviewPhaseFacts near-miss hints) rather than degrading to an unexplained identical default.

## Decisions
- DEC-001 [CQ-001] [AMB:AMB-001]: resolved: when a CriticSuccessionReceipt is present but fails a guard (mismatched original critic, stale/mismatched head, or implementer as successor/granter), ResumeSameCritic names the refusal reason distinctly from the plain no-receipt-supplied case, matching the codebase convention of naming near-miss failures precisely (Driver.reviewPhaseFacts near-miss hints) rather than degrading to an unexplained identical default.

## Accepted Deferrals
No accepted deferrals recorded.

## Remaining Ambiguity
No blocking ambiguity remains.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 2417-critic-despawn-review-recovery`.
