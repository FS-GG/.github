# Shared disposable-worker host loop

Use the worker/subagent mechanism the current host exposes. If it can create an isolated worktree for a
worker, request that isolation; otherwise create a fresh worktree from the fetched default branch
before starting the worker. If the host has no worker mechanism, run the same one-item loop
sequentially in fresh worktrees. Never invent another host's tool name or syntax.

Every wave starts from fresh ground truth. For `drive-board`, the ordered planning boundary is:
consume the complete four-part `check-board` result, classify the current Backlog through
[backlog-triage](backlog-triage.md), then size repo lanes with `batch`. Never size a Ready-only wave
before that triage stage. Allocate only schedulable disjoint lanes within the fixed slot cap below;
never exceed it. Every worker must mint its own `FSGG_WORKER` identity and hold its own claim:
a host session, account, or parent identity is not a substitute. Give it one bounded item and the
complete item-driver contract. Do not hand it a second item. It MAY, after its done stamp, drain its
OWN follow-up queue sequentially — one claim at a time, never interleaved.

**Two concurrent waves, a fixed eight-slot cap.** Run two waves in parallel, not sequentially: each
wave holds three implementer slots, six implementer slots total across both waves. Two additional
subagent slots are reserved fleet-wide for independent critics at each wave's review boundary —
RESERVED, not advisory. An implementer may never occupy a review slot, and filling all eight slots with
implementers is a contract violation, not an efficiency gain. Dispatch does not wait on one wave's
verification boundary before the other wave's implementers start; that overlap is the whole point of
running two waves instead of one.

**Consolidation.** Count the items being worked across both waves combined — claimed, in review, or
newly dispatched; not yet-verified follow-ups. Three or fewer consolidates: fold every active item into
one wave and immediately start a second wave from a fresh reconcile/triage, not a re-slice of the
current plan. Four or more runs two full waves as designed. Re-check this count at every re-plan, not
once at loop start.

**Rate limit under two waves.** Every worker still authenticates as the one account whose REST budget
holds the claim lock — unbatchable, and its remaining budget is not queryable. Six implementers plus two
critics is a real increase over a single conservative wave, and the shared budget does not split by
wave: an `EX_RATE` (75) from ANY worker in EITHER wave is a fleet-wide stop for BOTH waves, not only the
wave that reported it. Stop spawning into both waves, let every in-flight worker in both drain or
report, back off to the reset the failure envelope names, then `flush --dry-run` before resuming either
wave.

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

Invoke skills through the selector supported by the current host: for example, `$drive-board` in
Codex or the host's skill picker. A literal `/skill` is documentation only unless that host explicitly
supports slash-based skill selection.

At each worker's review handoff, spawn a fresh critic under the `independent-review` contract loaded
by `$pnext-item`, route
up to three numbered repairs back to the still-live worker, and require the same critic's confirmation
after each repair. Before merge, verify PR state/head/checks, the durable review marker, the ordered
round/URL/SHA chain, critic independence, each material finding's disposition, and newly filed work.
Validate the chain and confirm its latest round is less than three before routing each repair.
The critic may file review-discovered work only when materiality, distinct root cause, dedupe, and
actionability are all evidenced; nonmaterial observations must not create issues, board rows, blocker
edges, or follow-up entries. Only after these pre-merge checks pass, post the exact-SHA
`fsgg:review-accepted:v1` marker; the worker may not merge before it observes that marker. If material
findings remain after round three, verify the escalation marker, `Blocked on: human/action` sentinel,
`Blocked` status, and released claim; do not post acceptance, merge, or permit round four.

**Repair phase.** An exhausted three-round chain is not automatically a human park: when the operator
has explicitly authorized repair-phase entry for that item — a declared parameter of the invocation,
never inferred from a passing check, a new commit, or this host's own judgement — dispatch it exactly
like a fresh item: a new worktree, a fresh implementing worker and a fresh critic, both at the escalated
route the active variant's own routing table names (`drive-board-best`/`-normal`,
`work-board-best`/`-normal`); a bare canonical invocation with no routing table of its own has no
escalated route to supply and cannot authorize entry. Reserve the same two review slots for its critic
that every wave already reserves; an implementer may never fill one, in the repair phase either. Verify
the repair-phase chain under the identical rules — durable markers, ordered round/URL/SHA chain, critic
independence — but against `repair-phase-max-rounds: 10`, not `max-automated-repair-rounds: 3`, and
require the `fsgg:independent-review-repair-phase:v1` marker naming the exhausted PR and its escalation
marker before treating any repair-phase pass as landable. Absent explicit authorization, or once the
repair phase itself exhausts its own round ceiling, verify the escalation marker, `Blocked on:
human/action` sentinel, `Blocked` status, and released claim exactly as an unauthorized exhaustion; do
not post acceptance, merge, start a second repair phase, or permit a round beyond either ceiling. A
`Done` transition that landed via the repair phase names it explicitly — `<item> — Done (repair phase):
<PR>` — so a completion report cannot describe a repair-phase landing as an ordinary one. See
`pnext-item`'s independent-review contract (its "Repair phase" section) for the full contract this
paragraph summarizes; the bounds live there once and are not restated per variant.

After merge and obligations, worker completion is evidence to check, not truth: verify merge
reachability, post-merge obligations, done stamp, issue/board state, claim release, and pending writes.
Reconcile and re-triage after every wave because completion can clear blockers or create
Backlog follow-ups; a snapshot taken before worker dispatch cannot plan the next wave.

**"I am refused the shared checkout, and the engine is N commits behind" is an escalation, and this loop
is where it lands.** `pnext-item` §1 makes every worker check the shared checkout's engine before its
first board write, but the *repair* is a mutation of a tree N workers share, and a worktree-isolated
worker may have its git operations against that checkout refused outright — so reporting it is the only
move it has (`.github#1594`, `.github#1663`). Treat such a report as a wave-blocking repair **this host
owns**, not as a worker failure and not as a note to file: run the engine-currency step, and do not
dispatch the next wave until the check answers zero. The reporting worker is already gone — workers are
disposable — so there is nothing to re-dispatch; what must not happen is the *next* wave going out over
the same refusal. A worker that stopped this way spent no lease and holds no claim, which is the
protocol working: the item it did not take is still schedulable.

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
