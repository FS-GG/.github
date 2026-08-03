# Shared disposable-worker host loop

Use the worker/subagent mechanism the current host exposes. If it can create an isolated worktree for a
worker, request that isolation; otherwise create a fresh worktree from the fetched default branch
before starting the worker. If the host has no worker mechanism, run the same one-item loop
sequentially in fresh worktrees. Never invent another host's tool name or syntax.

Every wave starts from fresh ground truth. For `work-board`, the ordered planning boundary is: consume
the complete four-part `check-board` result, classify the current workspace Backlog through
[backlog-triage](backlog-triage.md), then size the Ready wave with `batch`. Never size before triage.
Allocate only schedulable touch-set-disjoint lanes within the fixed slot cap below; never exceed it.
Every worker must mint its own `FSGG_WORKER` identity and hold its own claim: a host session, account,
or parent identity is not a substitute. Give it one bounded item, a stable feedback cycle id, and the
complete item-driver contract, including the simple-versus-complex SDD lifecycle branch and schema-v2
feedback envelope, plus the shared
control-plane provenance guidance in the `pnext-item` contract. Do not hand it a second item. It MAY, after its done stamp, drain its OWN follow-up
queue sequentially — one claim at a time, never interleaved.

<!-- BEGIN GENERATED: fsgg-protocol:wave-policy -->
*Generated operational fact: the parser and driver consume this policy; do not restate its numbers.*

<!-- fsgg:wave-model:v1 waves=2 implementer-slots-per-wave=3 review-slots=2 consolidation-threshold=3 -->

**Two concurrent waves, a fixed 8-slot cap.** Each wave holds 3 implementer slots; 2 slots are reserved fleet-wide for independent critics. **Consolidation:** 3 or fewer active items consolidates before a fresh wave is started.

<!-- END GENERATED: fsgg-protocol:wave-policy -->

Critic reservation is **RESERVED, not advisory**; assigning it to an implementer is a contract
violation, not an efficiency gain.

`batch` reads the machine declaration above and emits `activeItems`, `waveCapacity`, and `openSlots`
beside its scheduling answer. When schedulable work and open slots coexist it also emits `WAVE
SHORTFALL`; treat that headline as an immediate re-plan/dispatch instruction. The signal is advisory
because an enforcing refusal would also prevent the dispatch that fills the slot, and ordinary
drain-down can legitimately leave capacity open. Advisory does not mean optional: the host loop owns
acting on the measured deficit.

For deterministic rollover and housekeeping ordering, consume the coordination driver's typed next
action/receipt validation when available; consolidation itself remains an explicit host judgement.

**Consolidation.** Count the items being worked across both waves combined — claimed, in review, or
newly dispatched; not yet-verified follow-ups. Follow the generated threshold above: fold every active
item into one wave and immediately start a second wave from a fresh reconcile/triage, not a re-slice of
the current plan. Re-check this count at every re-plan, not once at loop start.

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

Invoke skills through the selector supported by the current host: for example, `$work-board` in Codex
or the host's skill picker. A literal `/skill` is documentation only unless that host explicitly
supports slash-based skill selection.

At each worker's review handoff, spawn a fresh critic under the `independent-review` contract loaded
by `$pnext-item`, route
up to three numbered repairs back to the still-live worker, and require the same critic's confirmation
after each repair. Before merge, verify PR state/head/checks, the durable review marker, the ordered
round/URL/SHA chain, critic independence, each material finding's disposition, and newly filed work.
Validate the chain and confirm its latest round is less than three before routing each repair.
A critic may file review-discovered work only when
materiality, distinct root cause, dedupe, and actionability are evidenced; nonmaterial observations
must not create issues, board rows, blocker edges, or follow-up entries. Post the exact-SHA
`fsgg:review-accepted:v1` marker only after these pre-merge checks pass; the worker may not merge
before it observes that marker. If material findings remain after round three, verify the escalation
marker, close the ordinary PR without merging, and automatically enter the repair phase; do not post
acceptance, merge, or permit round four on the exhausted chain.

**Repair phase.** A validated exhausted three-round chain automatically enters one fresh repair phase.
Dispatch it exactly like a fresh item: a new worktree, a fresh implementing worker and a fresh critic,
both at the escalated route the active variant's own routing table names (`drive-board-best`/`-normal`,
`work-board-best`/`-normal`); a bare canonical invocation uses its corresponding `-best` repair route.
A passing check, new commit, or host judgement is not an entry trigger: verify the complete ordinary
chain and escalation marker first. On the next board-driver pass, an already parked item with that
evidence has its human-action sentinel removed and `Status` returned to `Ready`, then enters
automatically. Reserve the same two review slots for its critic
that every wave already reserves; an implementer may never fill one, in the repair phase either. Verify
the repair-phase chain under the identical rules — durable markers, ordered round/URL/SHA chain, critic
independence — but against `repair-phase-max-rounds: 10`, not `max-automated-repair-rounds: 3`, and
require the `fsgg:independent-review-repair-phase:v1` marker naming the exhausted PR and its escalation
marker before treating any repair-phase pass as landable. If the required route is unavailable, or once
the repair phase itself exhausts its own round ceiling, verify the escalation marker, `Blocked on:
human/action` sentinel, `Blocked` status, and released claim; do not post acceptance, merge, start a
second repair phase, or permit a round beyond either ceiling. A
`Done` transition that landed via the repair phase names it explicitly — `<item> — Done (repair phase):
<PR>` — so a completion report cannot describe a repair-phase landing as an ordinary one. See
`pnext-item`'s independent-review contract (its "Repair phase" section) for the full contract this
paragraph summarizes; the bounds live there once and are not restated per variant.

After merge and obligations, verify merge reachability, post-merge obligations, done stamp,
issue/board state, claim release, pending writes, and feedback. Apply the exact fail-closed
commands in [feedback-contract](feedback-contract.md) to merged paths before accepting an item; worker
prose is not verification. Reconcile and re-triage after every wave because completion can clear
blockers or create Backlog follow-ups; a snapshot taken before worker dispatch cannot plan the next
wave.

Terminate only from a fresh read. Distinguish empty from blocked, contended, stale, or unreadable state.
An empty Ready batch is not completion while Backlog is actionable or untriaged, or while any completed
cycle lacks validated feedback and roll-up disposition. Report deliberately parked and human-blocked
Backlog without repeatedly dispatching or spinning on it.
