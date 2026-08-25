---
schemaVersion: 1
workId: 2905-post-merge-verification
title: Post Merge Verification
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2905-post-merge-verification/spec.md
sourceClarifications: work/2905-post-merge-verification/clarifications.md
sourceChecklist: work/2905-post-merge-verification/checklist.md
publicOrToolFacingImpact: true
---

# Post Merge Verification Plan

Prose status: planned

## Source Snapshot
- spec: work/2905-post-merge-verification/spec.md sha256:c1905298c0cdd7b52c48f905804c8202a82cf2344e6306c988d6f994afd23ce4 schemaVersion:1
- clarifications: work/2905-post-merge-verification/clarifications.md sha256:e125a5eb5345c6bbe3ea10b61c2fc732eaf50f109a6ac0e719dc0ab92717e04e schemaVersion:1
- checklist: work/2905-post-merge-verification/checklist.md sha256:17b68d376ca373cbbe904455f94d099d7c40d9897324425ff1d8c845e1839663 schemaVersion:1

## Plan Scope
- Work item 2905-post-merge-verification is planned from the current specification, clarification, and checklist facts.
- Requirement count: 6.
- Clarification decision count: 4.
- Checklist result count: 6.

## Plan Decisions
- PD-001 [AC-001] [AC-002] [FR-001] [DEC-001] complete: Declare `PostMergeRun`, `PostMergeVerificationReceipt`, and the closed `PostMergeVerification` union in `Delivery.fsi` before implementation. Add `MergeSha` and `PostMergeVerification` to snapshot/completion facts so `Merged` cannot silently mean `Verified`.
- PD-002 [AC-001] [AC-003] [FR-002] [DEC-001] [DEC-002] complete: Extend `Delivery.decideCompletion` with one fail-closed verification classifier after outstanding delivery obligations and merge reachability, but before `ProjectCompletion`. Absent or pending evidence selects a retryable `AwaitPostMergeVerification`; failed or unreadable evidence returns a causal refusal; only `Verified` for the exact merge SHA proceeds.
- PD-003 [AC-002] [AC-003] [AC-004] [FR-003] [DEC-001] [DEC-002] complete: Repair the GitHub read boundary's mixed-inventory precedence after reading the merged PR's immutable `merge_commit_sha`, base/default branch, and every Actions page. Classify only `push` runs whose `head_sha` and `head_branch` exactly match; any completed success verifies and the immutable receipt retains every matching run, otherwise pending waits, completed non-success fails, and zero is absent. Any partial, malformed, or transport-failed read is unreadable before classification.
- PD-004 [AC-003] [AC-004] [FR-004] [DEC-002] complete: Feed that one typed live observation into both `Delivery.inspect` and `runDone`; keep receipt creation, issue closure, Done projection, and claim release in their existing order and downstream of `ProjectCompletion`. Re-read/verify the closer merge in `runDone` and refuse if it differs from the receipt-bound merge SHA.
- PD-005 [AC-004] [FR-005] [DEC-003] complete: Add the verified post-merge receipt to `DeliveryCompletionReceipt`, its digest, JSON codec, and validator. Decode legacy completion receipts only as replay authority under their existing marker contract; never mint a new receipt without the new exact-merge evidence. Repeated apply after a partial projection continues through the current idempotent saga.
- PD-006 [AC-005] [FR-006] [DEC-004] complete: Extend pure decision/receipt matrix tests and fake-transport live read/completion tests with the observed production inversion. The production-route control asserts a PR-green merged world with no qualifying success cannot call the completion receipt/close/Done/release writers, then adds an exact matching successful default-branch push run beside unrelated red, pending, and cancelled runs and observes the existing ordered saga while the receipt retains the mixed inventory.

## Contract Impact
- PC-001 [PD-001] [PD-002] [PD-003] [PD-005] command report: `Delivery.fsi` gains the post-merge verification vocabulary and completion receipt field; `Reads.fsi` gains the exact-merge/default-branch evidence read. Supplied delivery snapshots may omit the new field only to mean explicit `NotObserved`, which fails closed; live JSON/text gains an `awaitPostMergeVerification` action without weakening existing inputs.

## Verification Obligations
- VO-001 [PD-001] [PD-002] [PD-003] [PD-004] [PD-005] [PD-006] [PC-001] semanticTest: Run core delivery, lifecycle adapter, GitHub read, and Done stderr suites with real fake-transport HTTP fixtures; record exact run/test counts from fresh commands. Demonstrate red/green mutations for removing exact-SHA/default-branch/event matching, restoring non-success veto precedence, and bypassing the completion verification arm, including zero terminal writer calls in the no-success world and completion from a mixed inventory. Then run affected projects, full solution, formatting, signatures/projections, M6 policy, and the SDD evidence/analyze/verify/ship fixed point.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] compatible: Existing supplied snapshots without `postMergeVerification` parse as explicit not-observed and therefore cannot complete prematurely. Existing durable completion receipts remain verifiable replay authority; all newly minted receipts carry the new evidence and digest binding.

## Generated View Impact
- GV-001 [PD-001] workModel: Regenerate `readiness/2905-post-merge-verification/work-model.json` and `analysis.json` through `tasks`/`analyze`; after implementation, keep evidence, verification, ship, and generated agent guidance current through `fsgg-sdd` rather than hand editing.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- The filed touch-set omits the existing GitHub read boundary and its focused tests. Widen `src/FS.GG.Coord.GitHub/Reads.fs`, `Reads.fsi`, and `tests/FS.GG.Coord.GitHub.Tests/ReadTests.fs` before implementation, and stop if that exact set overlaps another live claim.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2905-post-merge-verification`.
