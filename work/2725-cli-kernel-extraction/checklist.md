---
schemaVersion: 1
workId: 2725-cli-kernel-extraction
title: Cli Kernel Extraction
stage: checklist
changeTier: tier1
status: checklistReady
sourceSpec: work/2725-cli-kernel-extraction/spec.md
sourceClarifications: work/2725-cli-kernel-extraction/clarifications.md
publicOrToolFacingImpact: true
---

# Cli Kernel Extraction Checklist

Prose status: checklistReady

## Source Specification
- work/2725-cli-kernel-extraction/spec.md

## Source Clarifications
- work/2725-cli-kernel-extraction/clarifications.md

## Source Snapshot
- spec: work/2725-cli-kernel-extraction/spec.md sha256:adf71ee0826b4aacbe5a0a0c212ec88a79fe398278b9cf9b910f6e876164978b schemaVersion:1
- clarifications: work/2725-cli-kernel-extraction/clarifications.md sha256:2b8b03d6721374c488fb2623d202a19031daeed76e64585d7b323bb2f7cb3892 schemaVersion:1

## Checklist Items
- CHK-001 [FR-001] [AC-001] blocking: Requirement FR-001 is testable and linked to acceptance coverage.
- CHK-002 [FR-002] [AC-002] blocking: Requirement FR-002 is testable and linked to acceptance coverage.
- CHK-003 [FR-003] [AC-003] blocking: Requirement FR-003 is testable and linked to acceptance coverage.
- CHK-004 [FR-004] [AC-004] blocking: Requirement FR-004 is testable and linked to acceptance coverage.
- CHK-005 [FR-005] [AC-005] blocking: Requirement FR-005 is testable and linked to acceptance coverage.
- CHK-006 [FR-006] [AC-006] blocking: Requirement FR-006 is testable and linked to acceptance coverage.
- CHK-007 [FR-007] [AC-007] blocking: Requirement FR-007 is testable and linked to acceptance coverage.
- CHK-008 [FR-008] [AC-008] blocking: Requirement FR-008 is testable and linked to acceptance coverage.

## Review Results
- CR-001 [CHK:CHK-001] [FR-001] [AC-001] pass: Requirement FR-001 is testable and linked to acceptance coverage.
- CR-002 [CHK:CHK-002] [FR-002] [AC-002] pass: Requirement FR-002 is testable and linked to acceptance coverage.
- CR-003 [CHK:CHK-003] [FR-003] [AC-003] pass: Requirement FR-003 is testable and linked to acceptance coverage.
- CR-004 [CHK:CHK-004] [FR-004] [AC-004] pass: Requirement FR-004 is testable and linked to acceptance coverage.
- CR-005 [CHK:CHK-005] [FR-005] [AC-005] pass: Requirement FR-005 is testable and linked to acceptance coverage.
- CR-006 [CHK:CHK-006] [FR-006] [AC-006] pass: Requirement FR-006 is testable and linked to acceptance coverage.
- CR-007 [CHK:CHK-007] [FR-007] [AC-007] pass: Requirement FR-007 is testable and linked to acceptance coverage.
- CR-008 [CHK:CHK-008] [FR-008] [AC-008] pass: Requirement FR-008 is testable and linked to acceptance coverage.

## Accepted Deferrals
No accepted checklist deferrals recorded.

## Blocking Findings
No blocking findings recorded.

## Advisory Notes
- CR-006's `pass` grades FR-006's TESTABILITY and its linkage to AC-006. It is not a finding that FR-006
  was met, and it must not be read as one. FR-006 was subsequently tested at implementation and is
  recorded NOT MET: see the amendments on AC-006 and FR-006 in
  work/2725-cli-kernel-extraction/spec.md and the corrected discharge at PD-006 in
  work/2725-cli-kernel-extraction/plan.md. Independent review read this `pass` as a discharge, which is
  a reading the row invited, so the distinction is stated here rather than left to the reader.
- What the checklist stage could not have caught, and is worth saying because a future checklist can:
  AC-006's first conjunct is unsatisfiable by any module boundary in this repository. CHK-006 asks
  whether a requirement is testable and linked, never whether its criterion is achievable, so a
  criterion that no implementation can meet passes this stage by construction.

## Lifecycle Notes
- Specification requirements reviewed: 8.
- Clarification decisions reviewed: 3.
- Next lifecycle action: `fsgg-sdd plan --work 2725-cli-kernel-extraction`.
