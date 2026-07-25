# Shared disposable-worker host loop

Every wave starts from fresh ground truth. Allocate only schedulable disjoint lanes and never exceed
the host's available worker slots. A worker gets a fresh identity, current default-branch worktree, one
bounded item, and the complete item-driver contract. Do not reuse its context for another item.

Verify PR state/head/checks/review, merge reachability, post-merge obligations, done stamp, issue/board
state, claim release, pending writes, feedback, and newly filed work. Reconcile after every wave.

Terminate only from a fresh read. Distinguish empty from blocked, contended, stale, or unreadable state.
