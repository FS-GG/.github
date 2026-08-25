---
schemaVersion: 1
workId: 2871-closed-delivery-paths
title: Preserve declared paths across closed-item delivery
stage: checklist
changeTier: tier1
status: checklistReady
sourceSpec: work/2871-closed-delivery-paths/spec.md
sourceClarifications: work/2871-closed-delivery-paths/clarifications.md
publicOrToolFacingImpact: true
---

# Preserve declared paths across closed-item delivery Checklist

Prose status: checklistReady

## Source Specification
- work/2871-closed-delivery-paths/spec.md

## Source Clarifications
- work/2871-closed-delivery-paths/clarifications.md

## Source Snapshot
- spec: work/2871-closed-delivery-paths/spec.md sha256:01cafc3faa8e5a4b103061ffff9197babfec80479d5688421c1b86db19bf333e schemaVersion:1
- clarifications: work/2871-closed-delivery-paths/clarifications.md sha256:1394fd166a483968a50bddb78d5ceba323ec8a7f853782cc2bfcea4dcdfb289c schemaVersion:1

## Checklist Items
- CHK-001 [FR-001] [AC-001] blocking: Requirement FR-001 is testable and linked to acceptance coverage.
- CHK-002 [FR-002] [AC-002] blocking: The unread-body case names an observable refusal distinct from an absent declaration.
- CHK-003 [FR-003] [AC-003] blocking: The genuinely undeclared control preserves the existing diagnosis.
- CHK-004 [FR-004] [AC-004] blocking: The unchanged open-item route provides a regression boundary for authorization and fail-closed behavior.

## Review Results
- CR-001 [CHK:CHK-001] [FR-001] [AC-001] pass: Requirement FR-001 is testable and linked to acceptance coverage.
- CR-002 [CHK:CHK-002] [FR-002] [AC-002] pass: The production e2e transport can return an unread body and assert the exact refusal class.
- CR-003 [CHK:CHK-003] [FR-003] [AC-003] pass: A readable body without `Paths:` is a deterministic negative control.
- CR-004 [CHK:CHK-004] [FR-004] [AC-004] pass: Existing open-item delivery fixtures exercise claim and authorization behavior.

## Accepted Deferrals
No accepted checklist deferrals recorded.

## Blocking Findings
No blocking findings recorded.

## Advisory Notes
No advisory notes recorded.

## Lifecycle Notes
- Specification requirements reviewed: 4.
- Clarification decisions reviewed: 3.
- Next lifecycle action: `fsgg-sdd plan --work 2871-closed-delivery-paths`.
