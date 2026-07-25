# Shared disposable-worker host loop

Every wave starts from fresh ground truth. Allocate only schedulable disjoint lanes and never exceed
the host's available worker slots. A worker gets a fresh identity, current default-branch worktree, one
bounded item, and the complete item-driver contract. Do not reuse its context for another item.

Worker completion is evidence to check, not truth: verify PR state/head/checks/review, merge reachability,
post-merge obligations, done stamp, issue/board state, claim release, pending writes, and newly filed
work. Reconcile after every wave because completion can clear blockers or create follow-ups.

Terminate only from a fresh read. “Nothing schedulable” may mean empty, blocked, contended, stale, or
unreadable; report the actual state. Continue while a safe lane or required repair exists.
