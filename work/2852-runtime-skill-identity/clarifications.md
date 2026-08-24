---
schemaVersion: 1
workId: 2852-runtime-skill-identity
title: "Bind producer, package, materialized, and runtime-loaded skill identity"
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/2852-runtime-skill-identity/spec.md
publicOrToolFacingImpact: true
---

# Bind producer, package, materialized, and runtime-loaded skill identity Clarifications

## Source Specification
- work/2852-runtime-skill-identity/spec.md

## Clarification Questions
- CQ-001 [AMB:AMB-001] blocking open: Resolve source ambiguity AMB-001 before checklist.
- CQ-002 [AMB:AMB-002] blocking open: Resolve source ambiguity AMB-002 before checklist.
- CQ-003 [AMB:AMB-003] blocking open: Resolve source ambiguity AMB-003 before checklist.

## Answers
- CQ-001 [AMB:AMB-001] decision: The registry is producer authority; the package manifest is a sealed transport receipt and must agree when both are available.
- CQ-002 [AMB:AMB-002] decision: Add a dedicated identity projection while preserving existing check output.
- CQ-003 [AMB:AMB-003] decision: Read the live source and view roots from the receiver project declaration; report every root and reject duplicate divergent copies.

## Decisions
- DEC-001 [CQ-001] [AMB:AMB-001]: The registry is producer authority; the package manifest is a sealed transport receipt and must agree when both are available.
- DEC-002 [CQ-002] [AMB:AMB-002]: Add a dedicated identity projection while preserving existing check output.
- DEC-003 [CQ-003] [AMB:AMB-003]: Read the live source and view roots from the receiver project declaration; report every root and reject duplicate divergent copies.

## Accepted Deferrals
No accepted deferrals recorded.

## Remaining Ambiguity
No blocking ambiguity remains.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 2852-runtime-skill-identity`.
