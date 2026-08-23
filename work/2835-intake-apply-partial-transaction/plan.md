---
schemaVersion: 1
workId: 2835-intake-apply-partial-transaction
title: Intake Apply Partial Transaction
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2835-intake-apply-partial-transaction/spec.md
sourceClarifications: work/2835-intake-apply-partial-transaction/clarifications.md
sourceChecklist: work/2835-intake-apply-partial-transaction/checklist.md
publicOrToolFacingImpact: true
---

# Intake Apply Partial Transaction Plan

Prose status: planned

## Source Snapshot
- spec: work/2835-intake-apply-partial-transaction/spec.md sha256:0021a464fb25fd5e711aa53c61761c85abea148fcc6389fd35f289b99f9a3282 schemaVersion:1
- clarifications: work/2835-intake-apply-partial-transaction/clarifications.md sha256:e52c01538a34b141a0c799072f780f7129fc3f1c3462323f095e665e88a4df6c schemaVersion:1
- checklist: work/2835-intake-apply-partial-transaction/checklist.md sha256:2f308f70e8191c296a16b3878e0a4b2237a8050f8fd71a4bda6a89a8caca6a37 schemaVersion:1

## Plan Scope
- Work item 2835-intake-apply-partial-transaction is planned from the current specification, clarification, and checklist facts.
- Requirement count: 4.
- Clarification decision count: 0.
- Checklist result count: 4.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Canonicalize the documented lowercase severity vocabulary at JSON decode and validate the resulting value against the board's known option vocabulary before any receipt or external write. Preserve canonical capitalized input unchanged and refuse unknown values.
- PD-002 [AC-001] [FR-002] complete: Once a receipt resolves an issue, wrap every later projection refusal with a structured partial-state diagnostic naming the stable draft id and existing issue ref, and state that retry resumes without a second create.
- PD-003 [AC-001] [FR-003] complete: Treat the pre-fix lowercase severity digest as a compatible legacy representation of the same canonical draft. Receipt and intent recovery accept only that bounded normalization difference; all other content mismatches remain fail-closed.
- PD-004 [AC-001] [FR-004] complete: Extend the recording-transport transaction fixture and end-to-end intake route for lowercase normalization, unknown-value pre-write refusal, legacy receipt/intent recovery, precise partial-state output, and discriminating mutations for canonicalization, legacy-digest compatibility, and issue-bound recovery. Advance the receiver-visible coherent set from 0.74.0 to the next stable minor, 0.75.0, and verify the full Kit/release coherence surface before merge.

## Contract Impact
- PC-001 [PD-001] command report: `fsgg.coord.intake/v1` continues accepting the documented lowercase severity tokens while also accepting canonical board casing; unknown values now fail during validation instead of at the GraphQL mutation. Existing receipt cache JSON remains schema-compatible, with legacy digest compatibility computed rather than persisted as a new field. The receiver-visible engine and corrected packed Kit guidance ship together in coherent set 0.75.0.

## Verification Obligations
- VO-001 [PD-001] [PD-002] [PD-003] [PD-004] [PC-001] semanticTest: Run focused and full BoardOps tests, Core tests, coord-engine e2e intake tests, the three discriminating mutations, skill parity/quality, coherent-set version, Kit package self-test, published-coherence obligation arm, release-saga tests, and projection checks; then restore all mutations and record the exact post-merge coherent-set 0.75.0 release obligation.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] compatible: No cache migration is required. New reads accept the one legacy digest produced by lowercase severity and rewrite no existing receipt; successful retries continue through the existing issue-bound receipt.

## Generated View Impact
- GV-001 [PD-001] workModel: Refresh the SDD-owned work model, analysis, evidence, verification, and ship receipts from the authored lifecycle sources, plus the generated coordination-kit skill manifest after the mirrored skill change.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2835-intake-apply-partial-transaction`.
