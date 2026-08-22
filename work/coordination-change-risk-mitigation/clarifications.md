---
schemaVersion: 1
workId: coordination-change-risk-mitigation
title: Coordination Change Risk Mitigation
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/coordination-change-risk-mitigation/spec.md
---

# Coordination Change Risk Mitigation Clarifications

## Source Specification
- `work/coordination-change-risk-mitigation/spec.md`
- Intent source: `docs/coordination/2026-08-22-coordination-change-risk-mitigation-design.md`

## Clarification Questions
- CQ-001 [AMB-001]: Does the catalogue replace duplicate BoardOps structures immediately?
- CQ-002 [AMB-002]: Do new internal decision records require an immediate external JSON schema migration?
- CQ-003 [AMB-003]: When does `change-completeness` become required?
- CQ-004 [AMB-004]: Where are receiver receipts persisted relative to immutable release assets?

## Answers
- CQ-001: No. Introduce descriptors alongside existing structures, prove byte-identical behavior, convert tests,
  then remove the implementation record and duplicate list only after parity is green.
- CQ-002: No immediate migration is assumed. Keep existing schemas stable and translate internal decisions at adapter
  boundaries; any unavoidable external change requires an explicit versioned migration in its implementation slice.
- CQ-003: Introduce the context as advisory, measure duration and false positives, then require it before making
  expensive jobs depend on it. The five-minute target is measured before enforcement.
- CQ-004: Persist receiver results as append-only delivery receipts outside the immutable release asset set, bound to
  the verified journal, package identity, receiver, source, artifact digests, and idempotency identity.

## Decisions
- DEC-001 [CQ-001]: Use expand-and-contract migration for the command catalogue; coexistence precedes deletion.
- DEC-002 [CQ-002]: Preserve current external JSON schemas by adapter translation unless a phase proves a versioned
  migration unavoidable.
- DEC-003 [CQ-003]: Stage `change-completeness` advisory first, then required only after its boundedness and signal are
  demonstrated.
- DEC-004 [CQ-004]: Store receiver receipts as append-only evidence outside immutable assets and include them in the
  coherent-set completion obligation.
- DEC-005: Treat the source design as one umbrella work package whose tasks map to phases 0 through 5; each phase may
  be implemented and reviewed independently, and completion of this work package requires all phase obligations.

## Accepted Deferrals
- No requirement is deferred. Concrete filenames and schema field layouts that are not already fixed by the source
  design are implementation details, constrained by the typed-authority and compatibility decisions above.

## Remaining Ambiguity
- None. Implementation may discover new facts, but a change to authority, schema compatibility, security posture,
  or phase deletion criteria must return to clarification rather than being inferred locally.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work coordination-change-risk-mitigation`.
