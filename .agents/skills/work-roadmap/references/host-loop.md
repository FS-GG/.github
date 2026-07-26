# Shared disposable-worker host loop

Use the worker/subagent mechanism the current host exposes. If it can create an isolated worktree for a
worker, request that isolation; otherwise create a fresh worktree from the fetched default branch
before starting the worker. If the host has no worker mechanism, run the same milestone loop
sequentially in fresh worktrees. Never invent another host's tool name or syntax.

Each worker starts with fresh context and a current default-branch worktree, mints its own
`FSGG_WORKER` identity, owns its own claim and one bounded unit, and is discarded after verified
completion. A host session, account, or parent identity is not a substitute for the worker identity.
Check its PR, merge, tests, release obligations, feedback, and ledger update against repository state.
Apply the exact fail-closed commands in [feedback-contract](feedback-contract.md) to the merged cycle
paths before accepting it; worker prose is not verification. Re-read the ledger after every completion;
never schedule from the stale copy given to the previous worker.

Invoke skills through the selector supported by the current host: for example, `$work-roadmap` in
Codex or the host's skill picker. A literal `/skill` is documentation only unless that host explicitly
supports slash-based skill selection.

Terminate only after a fresh read proves no required unit remains, every cycle feedback gate passes,
and the final reporting and cross-cycle disposition obligations are landed.
