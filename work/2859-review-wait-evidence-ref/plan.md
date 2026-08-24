---
schemaVersion: 1
workId: 2859-review-wait-evidence-ref
title: Host-Owned Review Wait Boundary
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2859-review-wait-evidence-ref/spec.md
sourceClarifications: work/2859-review-wait-evidence-ref/clarifications.md
sourceChecklist: work/2859-review-wait-evidence-ref/checklist.md
publicOrToolFacingImpact: true
---

# Host-Owned Review Wait Boundary Plan

Prose status: planned

## Source Snapshot
- spec: work/2859-review-wait-evidence-ref/spec.md sha256:a6b6c02d490c14b89331daa2fb1463cb13aef611d02e27bbeedcfec802c4ad3c schemaVersion:1
- clarifications: work/2859-review-wait-evidence-ref/clarifications.md sha256:e9efebb3537141f41f424d25dc2bc96309b4506383fd6688645a6b502572585f schemaVersion:1
- checklist: work/2859-review-wait-evidence-ref/checklist.md sha256:9b95744cece3a236f0b7849e79039a7eb97d4b5a11eb35f9152bbe3bbd243ac3 schemaVersion:1

## Plan Scope
- Work item 2859-review-wait-evidence-ref is planned from the current specification, clarification, and checklist facts.
- Requirement count: 4.
- Clarification decision count: 3.
- Checklist result count: 4.

## Plan Decisions
- PD-001 [AC-001] [FR-001] [DEC-001] [DEC-003] complete: Extend command parsing with `review wait <ref> enter --pr <n>` and route it through the existing live review/claim readers. Construct the entry only after current claim, current head, typed review action, and canonical kind/round are known; derive the generation token in the engine and refuse conflicting or unreadable state.
- PD-002 [AC-002] [FR-002] [DEC-002] complete: Before terminal completion append, load the live structured ledger and require the evidence reference to identify the immediately preceding structured review-decision record. Permit marker normalization only when the live artifacts establish one unique associated record; otherwise refuse with the required record URL/id and leave the generation unconsumed.
- PD-003 [AC-003] [FR-003] [DEC-001] complete: Retain the explicit JSON event-file path and its schema. Parse `enter` as a distinct argument shape so existing scripts remain byte-compatible and ambiguous invocations fail at options validation.
- PD-004 [AC-004] [FR-004] complete: Add end-to-end controls using the recording GitHub transport for initial and repair-confirmation generations, correct record completion, marker normalization/refusal, stale claim/head, duplicate entry, and invalid terminal pointer. Mutate the derived-token and pre-append evidence checks independently and require named red results before restoration.

## Contract Impact
- PC-001 [PD-001] [PD-002] [PD-003] command report: `review wait` accepts the new host-owned `enter` form while retaining the existing event-file form. Help and both skill projections identify which state is derived and which structured record a completion must cite.

## Verification Obligations
- VO-001 [PD-001] [PD-002] [PD-003] [PD-004] [PC-001] semanticTest: Run focused lifecycle/options tests and `tests/coord-engine-e2e/writes.sh`; execute the built CLI through production parsing; record initial and confirmation controls; invert derived-generation and terminal-pointer validation one at a time and show each named control red; then restore and run full affected project tests plus projections.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] compatible: Existing explicit JSON wait events remain valid. The new `enter` form is additive; documentation moves hosts to it without forcing simultaneous caller migration.

## Generated View Impact
- GV-001 [PD-001] workModel: Refresh SDD readiness from this package and regenerate the checked-in `.agents`/`.claude` skill parity through the repository projection gate.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2859-review-wait-evidence-ref`.
