# Shared disposable-worker host loop

Each worker starts with fresh context and a current default-branch worktree, owns one bounded unit, and
is discarded after verified completion. Check its PR, merge, tests, release obligations, feedback, and
ledger update against repository state. Re-read the ledger after every completion; never schedule from
the stale copy given to the previous worker.

Terminate only after a fresh read proves no required unit remains and the final reporting obligation is
landed.
