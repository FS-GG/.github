---
schemaVersion: 1
workId: 2905-post-merge-verification
title: Post-merge verification before terminal delivery completion
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/2905-post-merge-verification/spec.md
publicOrToolFacingImpact: true
---

# Post-merge verification before terminal delivery completion Clarifications

## Source Specification
- work/2905-post-merge-verification/spec.md

## Clarification Questions
- CQ-001 [AMB:AMB-001] blocking answered: What minimum GitHub execution evidence qualifies as post-merge verification without admitting a PR, manual, or branch-mismatched run?
- CQ-002 [AMB:AMB-002] blocking answered: How do non-green and unreadable observations map into the current lifecycle without creating a dead-end state?

## Answers
- CQ-001 [AMB:AMB-001] answer: Qualifying evidence is a completed successful Actions workflow run returned for the immutable merge SHA, with `event=push`, `head_sha` equal to that merge SHA, and `head_branch` equal to the merged pull request's base/default branch. The receipt records run id, attempt, workflow path/name, event, branch, SHA, conclusion, and URL. PR-event, workflow-dispatch, branch-mismatched, or SHA-mismatched runs are explicit non-qualifying observations. The read must be complete over every page; an unread tail is not an empty or green set.
- CQ-002 [AMB:AMB-002] answer: Keep every merged-but-unverified result in `MergedAwaitingObligations`. Absence or pending evidence selects a retryable wait action; completed non-success, malformed, mismatch, or unreadable evidence returns a fail-closed refusal that names the observed cause. A later exact matching green read re-enters the same deterministic completion transition; no separate irreversible state or manual repair is introduced.

## Decisions
- DEC-001 [CQ-001] [AMB:AMB-001] [FR-001] [FR-002] [FR-003] [FR-005] [AC-001] [AC-002] [AC-003] [AC-004]: Add a closed `PostMergeVerification` domain value whose verified arm carries an immutable receipt. Derive it from paginated Actions workflow runs filtered to the exact merge SHA, `push` event, and merged PR base/default branch. At least one matching completed-success run verifies; any matching pending run is pending; a completed matching non-success is failed; no matching run is absent. Partial, malformed, or transport-failed reads are unreadable and never collapse to absent or verified.
- DEC-002 [CQ-002] [AMB:AMB-002] [FR-002] [FR-003] [FR-004] [FR-006] [AC-001] [AC-003] [AC-004] [AC-005]: Extend the existing completion decision rather than inventing a second writer. `MergedAwaitingObligations` remains the recovery stage; outstanding delivery obligations retain precedence, then post-merge verification gates `ProjectCompletion`. Missing/pending verification returns a typed wait, while failed/unreadable/mismatched verification is a visible refusal. `runDone`, completion-receipt creation, issue closure, Done projection, and claim release remain downstream of the one shared `Delivery.decideCompletion` answer.
- DEC-003 [FR-005] [AC-004]: Bind the selected verification receipt into the completion receipt digest and verifier. An existing valid completion receipt remains replay authority for idempotent cleanup; new completion authority cannot be minted without exact-merge verification.
- DEC-004 [FR-006] [AC-005]: Tests invert both the exact-match classifier and the completion gate. The bounded control starts with a PR-green merged snapshot and no matching post-merge run, proves terminal writes remain zero, adds one exact matching successful push run to prove completion, then restores production predicates before all affected suites run.

## Accepted Deferrals
No accepted deferrals recorded.

## Remaining Ambiguity
- None.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 2905-post-merge-verification`.
