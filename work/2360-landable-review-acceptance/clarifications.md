---
schemaVersion: 1
workId: 2360-landable-review-acceptance
title: Require review acceptance in landable by default
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/2360-landable-review-acceptance/spec.md
publicOrToolFacingImpact: true
---

# Require review acceptance in landable by default Clarifications

## Source Specification
- work/2360-landable-review-acceptance/spec.md

## Clarification Questions
- CQ-001 [AMB:AMB-001] blocking open: Resolve source ambiguity AMB-001 before checklist.
- CQ-002 [AMB:AMB-002] blocking open: Resolve source ambiguity AMB-002 before checklist.

## Answers
- CQ-001 [AMB:AMB-001] decision: Preserve only the known registry-coherence unattended caller as a narrow exemption; the explicit review token always wins.
- CQ-002 [AMB:AMB-002] decision: Keep stdout and exit codes unchanged and write evaluation or exemption provenance to stderr.

## Decisions
- DEC-001 [CQ-001] [AMB:AMB-001]: Preserve only the known registry-coherence unattended caller as a narrow exemption; the explicit review token always wins.
- DEC-002 [CQ-002] [AMB:AMB-002]: Keep stdout and exit codes unchanged and write evaluation or exemption provenance to stderr.
- DEC-003: Treat the structured acceptance record as the single authorization receipt. Preserve old
  non-acceptance record digests, while the owned live producer derives and seals claim/base bindings.
- DEC-004: Model GitHub's available conditional landing as a final base/head re-read followed by the
  existing head-conditional merge request; publish both revisions in the landing receipt.

## Accepted Deferrals
No accepted deferrals recorded.

## Remaining Ambiguity
No blocking ambiguity remains.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 2360-landable-review-acceptance`.
