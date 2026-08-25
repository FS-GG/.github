---
schemaVersion: 1
workId: 2905-post-merge-verification
title: Post-merge verification before terminal delivery completion
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Post-merge verification before terminal delivery completion Specification

Prose status: specified

## User Value
Operators can distinguish a merge that landed from a merge whose exact default-branch execution passed, so terminal delivery state never overclaims unverified work.

## Scope
- SB-001: Add a typed post-merge verification fact and receipt, exact-SHA/default-branch live evidence collection, success-admitting mixed-inventory classification, completion admission, durable receipt binding, and focused inversion-backed tests.

## Non-Goals
- SB-002: Changing guarded landing, independent review acceptance, required delivery obligations, GitHub branch policy, or the meaning of PR-head landability.

## User Stories
- US-001 (P1): An operator sees a merged item remain recoverably incomplete until the exact merge SHA has a successful default-branch execution.
- US-002 (P1): An operator can retry the same delivery completion saga after pending, red, absent, or temporarily unreadable post-merge evidence without duplicate terminal writes.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001] [FR-002]: Given a PR is green and has merged, but no successful default-branch run is bound to its exact merge SHA, when delivery completion is inspected or applied, then it remains `MergedAwaitingObligations` and refuses terminal projection with the post-merge state visible.
- AC-002 [US-001] [FR-001] [FR-003]: Given the merged PR's exact merge SHA has at least one completed successful run whose event and branch identify the repository default branch, including when unrelated exact-SHA/default-branch runs are red, pending, or cancelled, when delivery completion is retried, then the merge becomes `Verified`, retains the complete matching-run inventory for diagnostics, and may advance to terminal projection once every existing obligation also passes.
- AC-003 [US-001] [US-002] [FR-002] [FR-004]: Given no qualifying exact-SHA/default-branch successful push run exists and the observed evidence is absent, pending, red, cancelled, malformed, branch-mismatched, SHA-mismatched, or unreadable, when completion is inspected, then no completion receipt, issue closure, Done projection, or claim release occurs and the diagnostic distinguishes the observed state.
- AC-004 [US-002] [FR-003] [FR-005]: Given an exact matching green receipt is observed after an earlier refusal, when completion is retried one or more times, then the existing saga resumes, writes one bound completion receipt, and remains idempotent across closure, board projection, claim release, and cleanup recovery.
- AC-005 [US-001] [US-002] [FR-006]: Given the qualifying-evidence predicate or completion admission is inverted, when focused tests run, then a PR-green/no-post-merge-run world fails, a mixed inventory with one exact matching green plus unrelated non-success runs passes, and restoration returns all focused and affected suites to green.

## Functional Requirements
- FR-001: The delivery domain models `Merged` separately from post-merge `Verified`; verification carries the exact merge SHA, default branch, execution identity, event, status/conclusion, and evidence URL needed for durable audit. (Stories: US-001; Acceptance: AC-001, AC-002)
- FR-002: Completion admission fails closed unless at least one successful, completed execution exists for the exact merge SHA on the repository default branch with `event=push`; PR-head success, reachability, an empty or non-success-only matching run set, mismatched executions, and read failures never imply verification, while unrelated matching non-success runs cannot veto an existing qualifying success. (Stories: US-001, US-002; Acceptance: AC-001, AC-002, AC-003)
- FR-003: The live delivery adapter reads post-merge execution evidence only after GitHub reports the PR merged, preserves the complete matching-run inventory so unrelated red/pending/cancelled diagnostics remain visible, applies qualifying-success precedence, and feeds the same typed fact to both the lifecycle reducer and completion writer. (Stories: US-001, US-002; Acceptance: AC-002, AC-003, AC-004)
- FR-004: `Done`, issue closure, claim release, board `Status=Done`, and `fsgg.coord.delivery-completion/v1` creation remain unreachable until exact-merge verification and every pre-existing completion obligation pass. (Stories: US-001, US-002; Acceptance: AC-003, AC-004)
- FR-005: The post-merge verification fact participates in freshness and completion-receipt integrity so a receipt for another SHA, branch, or execution cannot authorize completion; retries and partially completed projections remain resumable and idempotent. (Stories: US-002; Acceptance: AC-004)
- FR-006: Focused unit, live-adapter, and production-route tests prove the inversion: PR-green plus merge/reachability but no qualifying branch run refuses all terminal writes, while adding one exact matching successful default-branch push run to a mixed inventory containing unrelated non-success runs enables precisely the existing completion saga and retains those diagnostics in its receipt. (Stories: US-001, US-002; Acceptance: AC-005)

## Ambiguities
- AMB-001: Define the minimum GitHub execution evidence that qualifies as a protected/default-branch receipt without mistaking a PR, workflow-dispatch, or branch-mismatched run for post-merge verification.
- AMB-002: Define how absent, pending, red, malformed, and unreadable evidence maps to the existing delivery stage/action vocabulary while keeping retries recoverable.

## Public Or Tool-Facing Impact
- Live `delivery` JSON/text can report post-merge verification waiting or refusal before terminal completion.
- The typed completion receipt gains exact post-merge verification evidence; its schema marker remains versioned and validation remains fail closed.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2905-post-merge-verification`.
