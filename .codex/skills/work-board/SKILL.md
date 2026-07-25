---
name: work-board
description: Use when explicitly asked to burn down one coordination-wired product workspace's board. Reconcile locally, fan out isolated item workers, verify merges and feedback, and re-plan until the board is empty.
---

# work-board

Burn down one coordination-wired workspace's board. The local board is both plan and ledger.

1. Reconcile the workspace and resolve mechanical drift.
2. Compute local disjoint lanes and bounded concurrency.
3. Spawn one fresh disposable worker per lane; each owns one item through claim, implementation,
   review, green merge, obligations, and verified done.
4. Verify the external state and feedback, then discard the worker.
5. Reconcile and re-plan after every wave.
6. When the fresh board is genuinely empty, update/land the workspace report if its policy requires one.

Load [host-loop](references/host-loop.md) for the shared worker/verification/termination contract and
[workspace-scope](references/workspace-scope.md) for the single-repository ledger rules.
Load [deep detail](references/deep-detail.md) only for recovery paths and extended rationale.
