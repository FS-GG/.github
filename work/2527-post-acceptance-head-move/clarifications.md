---
schemaVersion: 1
workId: 2527-post-acceptance-head-move
title: Post Acceptance Head Move
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/2527-post-acceptance-head-move/spec.md
publicOrToolFacingImpact: true
---

# Post Acceptance Head Move Clarifications

## Source Specification
- work/2527-post-acceptance-head-move/spec.md

## Clarification Questions
- CQ-001 [AMB:AMB-001] blocking open: Resolve source ambiguity AMB-001 before checklist.
- CQ-002 [AMB:AMB-002] blocking open: Resolve source ambiguity AMB-002 before checklist.

## Answers
- CQ-001 [AMB:AMB-001] decision: CQ-001 decision: evidence-derived chain retirement, not an out-of-band marker or grant and not close-and-reopen. A chain is retired when a host-acceptance marker names its initial review and carries an accepted-head that is no longer the binding's head; the acceptance marker's own required fields (accepted-head, initial-review) and the confirmation marker's initial-review back-reference already make every chain self-attributing, so the engine can OBSERVE the accepted-then-moved structure rather than being TOLD it. A superseding-chain marker or a host grant was rejected because it converts an observable fact into an assertable one, which is exactly the laundering route AC5 forbids; close-and-reopen was rejected as the mechanism because it is a procedure with nothing deletable, so it cannot carry the gate-inversion evidence AC4 requires, and it costs a PR container, a re-posted obligations declaration and a re-issued delivery authorization each time. Close-and-reopen remains documented as the manual fallback when the evidence is absent.
- CQ-002 [AMB:AMB-002] decision: CQ-002 decision: distinguishability is delivered on two surfaces rather than by a new State case. Where retirement APPLIES the state is no longer malformed at all, so the verdict carries the retired chains as an additive Verdict field serialized on the review --json wire (retiredChains, with each retired chain's initial-review reference and accepted head), which is what lets a reader see why a PR visibly carrying two initial markers is classified against the later one. Where retirement does NOT apply the pre-existing MalformedEvidence refusal stands but its reason now states the retirement rule and which of its conditions failed. A new State case was rejected because it would widen the closed state model for a transient condition every existing consumer would have to learn, while the additive field is empty for every chain that retires nothing.

## Decisions
- DEC-001 [CQ-001] [AMB:AMB-001]: CQ-001 decision: evidence-derived chain retirement, not an out-of-band marker or grant and not close-and-reopen. A chain is retired when a host-acceptance marker names its initial review and carries an accepted-head that is no longer the binding's head; the acceptance marker's own required fields (accepted-head, initial-review) and the confirmation marker's initial-review back-reference already make every chain self-attributing, so the engine can OBSERVE the accepted-then-moved structure rather than being TOLD it. A superseding-chain marker or a host grant was rejected because it converts an observable fact into an assertable one, which is exactly the laundering route AC5 forbids; close-and-reopen was rejected as the mechanism because it is a procedure with nothing deletable, so it cannot carry the gate-inversion evidence AC4 requires, and it costs a PR container, a re-posted obligations declaration and a re-issued delivery authorization each time. Close-and-reopen remains documented as the manual fallback when the evidence is absent.
- DEC-002 [CQ-002] [AMB:AMB-002]: CQ-002 decision: distinguishability is delivered on two surfaces rather than by a new State case. Where retirement APPLIES the state is no longer malformed at all, so the verdict carries the retired chains as an additive Verdict field serialized on the review --json wire (retiredChains, with each retired chain's initial-review reference and accepted head), which is what lets a reader see why a PR visibly carrying two initial markers is classified against the later one. Where retirement does NOT apply the pre-existing MalformedEvidence refusal stands but its reason now states the retirement rule and which of its conditions failed. A new State case was rejected because it would widen the closed state model for a transient condition every existing consumer would have to learn, while the additive field is empty for every chain that retires nothing.

## Accepted Deferrals
No accepted deferrals recorded.

## Remaining Ambiguity
No blocking ambiguity remains.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 2527-post-acceptance-head-move`.
