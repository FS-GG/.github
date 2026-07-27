# Shared disposable-worker host loop

Use the worker/subagent mechanism the current host exposes. If it can create an isolated worktree for a
worker, request that isolation; otherwise create a fresh worktree from the fetched default branch
before starting the worker. If the host has no worker mechanism, run the same one-item loop
sequentially in fresh worktrees. Never invent another host's tool name or syntax.

Every wave starts from fresh ground truth. For `drive-board`, the ordered planning boundary is:
consume the complete four-part `check-board` result, classify the current Backlog through
[backlog-triage](backlog-triage.md), then size repo lanes with `batch`. Never size a Ready-only wave
before that triage stage. Allocate only schedulable disjoint lanes and never exceed the host's
available worker slots. Every worker must mint its own `FSGG_WORKER` identity and hold its own claim:
a host session, account, or parent identity is not a substitute. Give it one bounded item and the
complete item-driver contract. Do not reuse its context for another item.

Invoke skills through the selector supported by the current host: for example, `$drive-board` in
Codex or the host's skill picker. A literal `/skill` is documentation only unless that host explicitly
supports slash-based skill selection.

Worker completion is evidence to check, not truth: verify PR state/head/checks/review, merge reachability,
post-merge obligations, done stamp, issue/board state, claim release, pending writes, and newly filed
work. Reconcile and re-triage after every wave because completion can clear blockers or create
Backlog follow-ups; a snapshot taken before worker dispatch cannot plan the next wave.

Terminate only from a fresh read. “Nothing schedulable” may mean empty, blocked, contended, stale, or
unreadable. An empty Ready batch is not completion while Backlog is actionable or untriaged. Report
deliberately parked and human-blocked Backlog without repeatedly redispatching or spinning on it.

The stopping test is **no startable `defect`**, not an empty board: a run in which fixing one thing files
two can never reach an empty board, and every row it files is real. `hardening` is drained deliberately as
ordinary backlog; `decision` is surfaced and never dispatched. **An unclassed row is a possible defect, not
a minor one** — you may stop with some outstanding, but report them by number and do not claim the board is
defect-free. Read classes from `ready --json`'s `class` field *after* `reconcile --apply` (the column is a
projection, current only as of the last reconcile) plus `lint`'s `CLASS-UNSET` for the rest; the authority
is the item's own `Class:` body line, so never hand-edit the column.
