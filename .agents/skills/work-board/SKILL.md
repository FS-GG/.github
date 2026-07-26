---
name: work-board
description: Use when explicitly asked to burn down one coordination-wired product workspace's board. Reconcile and triage backlog first, fan out isolated item workers through disjoint lanes, verify, and re-plan.
---

# work-board

Burn down one coordination-wired workspace's board. The local board is both plan and ledger.

1. Reconcile the workspace and consume the complete four-part `check-board` result.
2. Run [backlog-triage](references/backlog-triage.md), classifying every relevant parked row without
   guessing human judgement and promoting only evidenced actionable work to `Ready`.
3. Compute local disjoint lanes and bounded concurrency through the normal scheduler.
4. Spawn one fresh disposable worker per lane; each owns one item through claim, implementation,
   review, green merge, obligations, and verified done.
5. Verify the external state and schema-v2 development feedback, then discard the worker.
6. Reconcile and re-triage from a fresh read after every wave so worker-filed follow-ups enter the next
   plan while the simple-versus-complex SDD lifecycle branch remains inside each item worker.
7. Stop only when fresh reconciliation and triage leave no startable or actionable/untriaged work.
   Surface deliberately parked and human-blocked backlog without spinning; then update/land the
   workspace report if its policy requires one.

Load [host-loop](references/host-loop.md) for the shared worker/verification/termination contract and
[workspace-scope](references/workspace-scope.md) for the single-repository ledger rules.
Load [deep detail](references/deep-detail.md) only for recovery paths and extended rationale.
