---
schemaVersion: 1
workId: 2907-blocked-by-set-mutations
title: Blocked-by Set Mutations
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/2907-blocked-by-set-mutations/spec.md
publicOrToolFacingImpact: true
---

# Blocked-by Set Mutations Clarifications

## Source Specification
- work/2907-blocked-by-set-mutations/spec.md

## Clarification Questions
- CQ-001 [AMB:AMB-001] blocking answered: What public spelling makes add, remove, replace, and clear explicit without weakening the parser's residue rule or changing scalar field writes?
- CQ-002 [AMB:AMB-002] blocking answered: Where is revision freshness checked when Projects-v2 exposes item `updatedAt` but no native compare-and-set input?

## Answers
- CQ-001 [AMB:AMB-001] answer: Keep `set-field <ref> <field> <value>` for scalar fields. For `Blocked by`, accept exactly one of `--add <refs>`, `--remove <refs>`, `--replace <refs>`, or `--clear`; refuse the legacy bare positional value with a corrective diagnostic. The existing `--batch` `Field=Value` grammar remains explicitly replacement-shaped by `=`, and `Field=` remains its explicit clear spelling.
- CQ-002 [AMB:AMB-002] answer: Add/remove first read `{ Value; Revision }`, derive the canonical result, then pass the same observation to the one board-write boundary. That boundary immediately re-reads both facts and refuses stale before emitting the mutation when either differs. GitHub supplies no server-side `If-Match`, so documentation must describe this as the strongest revision guard Projects-v2 exposes, not claim a nonexistent transport-level compare-and-set. Tests must inject a changed second observation and prove mutation count zero.

## Decisions
- DEC-001 [CQ-001] [AMB:AMB-001] [FR-001] [FR-002] [FR-004] [AC-001] [AC-002] [AC-004]: Add four closed, mutually exclusive option fields and parser flags scoped only to `set-field`. `--add` and `--remove` derive from the live set, `--replace` writes only the supplied canonical set, and `--clear` uses `Board.Clear`. A bare `Blocked by` positional value refuses and names the four valid forms. Scalar fields retain their existing positional contract; batch equality remains an explicitly named replacement/clear operation.
- DEC-002 [CQ-002] [AMB:AMB-002] [FR-003] [AC-003]: Introduce a guarded single-field board write accepting `BlockedByObservation`. It resolves the item, re-reads the observation immediately before mutation, and emits no mutation on mismatch. The fixture controls the two reads independently and asserts a stale diagnostic plus zero update/clear documents when the second revision or value differs.
- DEC-003 [FR-005] [AC-005]: Parse only an unfenced, line-leading `Blocked by:` body projection using the same dependency canonicalizer as the field. No body line means no finding. A present line with empty authoritative field yields `BLOCKED-BY-BODY-INERT`; unequal canonical sets or invalid projection syntax yield the same stable rule family with a precise divergent/invalid detail; equal canonical sets stay green.
- DEC-004 [FR-006] [AC-006]: Mutation controls independently replace union with requested-only, subtraction with empty, and stale comparison with unconditional success; lint controls suppress the body comparison. Each control must red a named focused test, then be restored before the full gates.

## Accepted Deferrals
No accepted deferrals recorded.

## Remaining Ambiguity
No blocking ambiguity remains.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 2907-blocked-by-set-mutations`.
