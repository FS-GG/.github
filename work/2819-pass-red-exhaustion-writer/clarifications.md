---
schemaVersion: 1
workId: 2819-pass-red-exhaustion-writer
title: Round-three pass/red repair-phase agreement
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/2819-pass-red-exhaustion-writer/spec.md
publicOrToolFacingImpact: true
---

# Round-three pass/red repair-phase agreement Clarifications

## Source Specification
- work/2819-pass-red-exhaustion-writer/spec.md

## Clarification Questions
- CQ-001 [AMB:AMB-001] blocking open: Resolve source ambiguity AMB-001 before checklist.
- CQ-002 [AMB:AMB-002] blocking open: Resolve source ambiguity AMB-002 before checklist.

## Answers
- CQ-001 [AMB:AMB-001] decision: Represent the settled-red terminal fact in the typed Core review classification and consume that fact consistently from Lifecycle projection and live writer admission; do not duplicate marker parsing.
- CQ-002 [AMB:AMB-002] decision: Reuse the existing exact-head required-check classification already consumed by review projection, preserving distinct pending, green, and settled-red outcomes.

## Decisions
- DEC-001 [CQ-001] [AMB:AMB-001]: A completed ordinary round-three wait with an immutable pass becomes recovery-eligible only after exact-head required checks settle red and claim turnover satisfies existing recovery preconditions; projection and writer share this terminal-set rule.
- DEC-002 [CQ-002] [AMB:AMB-002]: The accepted escalation enters only the existing single repair phase and seals its predecessor link to the immutable round-three digest; exact-head, backlink, predecessor, claim-generation, ordinary-round ceiling, and one-repair-phase refusals remain unchanged.

## Accepted Deferrals
No accepted deferrals recorded.

## Remaining Ambiguity
No blocking ambiguity remains.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 2819-pass-red-exhaustion-writer`.
