# Shared disposable-worker host loop

Use the worker/subagent mechanism the current host exposes. If it can create an isolated worktree for a
worker, request that isolation; otherwise create a fresh worktree from the fetched default branch
before starting the worker. If the host has no worker mechanism, run the same one-item loop
sequentially in fresh worktrees. Never invent another host's tool name or syntax.

Every wave starts from fresh ground truth. Allocate only schedulable disjoint lanes and never exceed
the host's available worker slots. Every worker must mint its own `FSGG_WORKER` identity and hold its
own claim: a host session, account, or parent identity is not a substitute. Give it one bounded item
and the complete item-driver contract. Do not reuse its context for another item.

Invoke skills through the selector supported by the current host: for example, `$drive-board` in
Codex or the host's skill picker. A literal `/skill` is documentation only unless that host explicitly
supports slash-based skill selection.

Worker completion is evidence to check, not truth: verify PR state/head/checks/review, merge reachability,
post-merge obligations, done stamp, issue/board state, claim release, pending writes, and newly filed
work. Reconcile after every wave because completion can clear blockers or create follow-ups.

Terminate only from a fresh read. “Nothing schedulable” may mean empty, blocked, contended, stale, or
unreadable; report the actual state. Continue while a safe lane or required repair exists.
