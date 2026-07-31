# Shared disposable-worker host loop

Use the worker/subagent mechanism the current host exposes. If it can create an isolated worktree for a
worker, request that isolation; otherwise create a fresh worktree from the fetched default branch
before starting the worker. If the host has no worker mechanism, run the same one-item loop
sequentially in fresh worktrees. Never invent another host's tool name or syntax.

Every wave starts from fresh ground truth. For `work-board`, the ordered planning boundary is: consume
the complete four-part `check-board` result, classify the current workspace Backlog through
[backlog-triage](backlog-triage.md), then size the Ready wave with `batch`. Never size before triage.
Allocate only schedulable touch-set-disjoint lanes and never exceed the host's available worker slots.
Reserve at least one available slot for an independent critic instead of filling the cap with
implementers.
Every worker must mint its own `FSGG_WORKER` identity and hold its own claim: a host session, account,
or parent identity is not a substitute. Give it one bounded item, a stable feedback cycle id, and the
complete item-driver contract, including the simple-versus-complex SDD lifecycle branch and schema-v2
feedback envelope. Do not hand it a second item. It MAY, after its done stamp, drain its OWN follow-up
queue sequentially — one claim at a time, never interleaved.

**That permission is a trade, and it is deliberate.** Disposal is the feature, not an accident of
implementation: ADR-0053 rejected the long-lived worker because context degrades across units, and a
fresh worker per unit of work is the org's established quality lever. That reasoning is real, but it
sits in one ADR's rejected alternatives, so the absolute this replaces read as costless while it
dead-lettered the follow-up queue — `followup add` banks a finding keyed to the worker that found it,
and a worker despawned at its done stamp leaves a promise no scheduler, gate, or driver can see (85
orphaned queues when it was measured: `.github#1900`, cause `.github#1902`). The exception buys back
exactly that: the author's context already holds the cause, the tree, and why the fix could not ride
the merged PR — which is what a stranger re-deriving the row from the board spends a whole worker slot
rebuilding. It buys nothing else, and the cost is real: a worker's context now grows across items. So
keep the drain to the worker's OWN entries after its OWN done stamp, and prefer stopping with entries
queued — they stay schedulable — over draining them from a context that has degraded.

Invoke skills through the selector supported by the current host: for example, `$work-board` in Codex
or the host's skill picker. A literal `/skill` is documentation only unless that host explicitly
supports slash-based skill selection.

At each worker's review handoff, spawn a fresh critic under the `independent-review` contract loaded
by `$pnext-item`, route
bounded repairs back to the still-live worker, and require the same critic's confirmation. Verify PR
state/head/checks, the durable review marker and SHAs, critic independence, each material finding's
disposition, merge reachability, post-merge obligations, done stamp, issue/board state, claim release,
pending writes, feedback, and newly filed work. A critic may file review-discovered work only when
materiality, distinct root cause, dedupe, and actionability are evidenced; nonmaterial observations
must not create issues, board rows, blocker edges, or follow-up entries. Post the exact-SHA
`fsgg:review-accepted:v1` marker only after those checks pass; the worker may not merge before it
observes that marker. Apply the exact fail-closed
commands in [feedback-contract](feedback-contract.md) to merged paths before accepting an item; worker
prose is not verification. Reconcile and re-triage after every wave because completion can clear
blockers or create Backlog follow-ups; a snapshot taken before worker dispatch cannot plan the next
wave.

Terminate only from a fresh read. Distinguish empty from blocked, contended, stale, or unreadable state.
An empty Ready batch is not completion while Backlog is actionable or untriaged, or while any completed
cycle lacks validated feedback and roll-up disposition. Report deliberately parked and human-blocked
Backlog without repeatedly dispatching or spinning on it.
