---
schemaVersion: 1
workId: 3014-repair-phase-turnover
title: Repair-phase turnover for admitted post-ceiling ledgers
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/3014-repair-phase-turnover/spec.md
publicOrToolFacingImpact: true
---

# Repair-phase turnover for admitted post-ceiling ledgers Clarifications

## Source Specification
- work/3014-repair-phase-turnover/spec.md

## Clarification Questions
- CQ-001 [AMB-001]: How does a longer admitted chain bind its actual terminal confirmation without invalidating existing three-round markers?

## Answers
- CA-001 [CQ-001]: Add an optional `terminal-confirmation` URL field. It is required only when the terminal confirmation is not round three; exact three-round markers remain valid without it.

## Decisions
- DEC-001 [CA-001] [FR-001] [FR-005]: The typed writer derives the terminal record from the validated current generation. Legacy evidence must bind confirmation-1/2/3 and, for longer generations, `terminal-confirmation` to that derived record.
- DEC-002 [FR-003]: Authorization delegates exhaustion classification to `Review.decideOrdinaryExhaustion`; the writer adds provenance checks but does not invent a second exhaustion predicate.
- DEC-003 [FR-004]: Repair-phase entry remains unchanged after the typed escalation exists; the new compatibility path grants no direct acceptance or merge authority.

## Accepted Deferrals
No accepted deferrals recorded.

## Remaining Ambiguity
No blocking ambiguity remains.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 3014-repair-phase-turnover`.
