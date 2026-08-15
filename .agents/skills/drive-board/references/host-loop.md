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
complete item-driver contract, including the shared
control-plane provenance guidance in the `pnext-item` contract. Do not hand it a second item. It MAY, after its done stamp, drain its
OWN follow-up queue sequentially — one claim at a time, never interleaved.

Every specific, checkable assertion included in a worker dispatch, worker report, or host relay must
carry `Verification:` with the command, `file:line`, API call, or URL that established it, or exactly
`unverified` when it was not checked. The latter is a valid, non-pejorative value. Before forwarding a
handoff, check that each assertion has one of those two forms; a missing field is detectable incomplete
evidence, not permission for the receiver to assume the claim was verified. This binds the host when it
relays a worker's or critic's assertion as well as the original author.

When emitting the session report, build its snapshot from the project-scoped `who --repo` and board
receipts, not an organization-wide claim scan. Serialize that independent receipt as the reporter's
`--scope` input, then bind the exact Coordination project identity to every lane; an identity inside the
lane snapshot is not its own authority. Preserve both board and execution timestamps/evidence; a mismatch is a named report state, not a
reason to overwrite either fact. If a prior event used the wrong scope, append a typed correction that
supersedes it and continue from the corrected effective totals.

<!-- BEGIN GENERATED: fsgg-protocol:wave-policy -->
*Generated operational fact: the parser and driver consume this policy; do not restate its numbers.*

<!-- fsgg:wave-model:v1 waves=2 implementer-slots-per-wave=3 review-slots=2 consolidation-threshold=3 -->

**Two concurrent waves, a fixed 8-slot cap.** Each wave holds 3 implementer slots; 2 slots are reserved fleet-wide for independent critics. **Consolidation:** 3 or fewer active items consolidates before a fresh wave is started.

<!-- END GENERATED: fsgg-protocol:wave-policy -->

Critic reservation is **RESERVED, not advisory**; assigning it to an implementer is a contract
violation, not an efficiency gain.

**The host owns critic dispatch; a worker does not dispatch its own.** Assigning a fresh critic in
response to a worker's review handoff is a request the worker owes the host (`pnext-item` §5); a
worker calling the `Agent` tool with `subagent_type: fsgg-critic-normal` or any other
`fsgg-critic-<route>` type to spawn and confirm its own critic is exactly the contract violation the
reservation above forbids — measured twice in one run (`.github#2462`), and correctly disclosed both
times, which does not make the mechanism sound on its own. The one stated exception is a **solo
`pnext-item` invocation with no host to ask**; that mode has no reservation to violate, because no host
is dispatching in it.

Detection here is **by convention, not by construction**: host and worker share one GitHub account
(rate-limit note below — "every worker still authenticates as the one account"), so no marker field can
prove who dispatched a critic, and this loop does not claim otherwise. The reservation is enforced by
the host verifying the dispatch it itself made, not by a field the review chain carries.

**Collect the finding packets, then dispatch the analyst — at the re-plan boundary, not inside a wave.**
`pnext-item`'s findings-and-filing contract routes a finder that has established a distinct cause to post
an `fsgg:finding-packet` comment INSTEAD of filing, wherever a `board-analyst` resolves. Nothing waits on
that packet, by design — which is exactly why a loop with no collection step is worse than the filing it
replaced: the finding becomes a comment with no reader and no owner, where before it became a row a
scheduler could see (`.github#2675`). So the step is owned here, beside critic dispatch, and it runs on
the same boundary as the post-wave reconcile and re-triage: after this wave's merges are verified, before
the next wave is sized. Hand the analyst the packets you collected and nothing else — it adjudicates what
it is handed, it never re-derives a packet, and it never dispatches, claims, or merges.

- **Collect without a board scan.** One REST read reaches every packet: the repository-wide issue-comments
  listing, bounded by the previous boundary's timestamp —
  `gh api -X GET repos/<owner>/<repo>/issues/comments -f since=<previous-boundary> -f per_page=100`.
  It returns comments on issues AND on pull requests in one paginated call, which is both of the surfaces
  a packet is allowed to live on, and it never fans out per issue. That spelling is load-bearing rather
  than a preference: a `scan` costs more than the pass it would be deciding about, and
  `scripts/fsgg-coord issues` cannot stand in for it — that command reads the issue LIST endpoint, which
  carries a comment COUNT and no comment bodies, and it drops pull requests outright
  (`src/FS.GG.Coord.GitHub/Reads.fs`).
- **The analyst occupies no slot, and that is a stated exemption with its cost, not silence.** It holds no
  claim, takes no lane, and blocks no chain — `board-analyst` may never `claim`, `take`, or `release` — so
  it can consume neither an implementer slot nor one of the reserved critic slots, and the generated
  policy above is unchanged by it. What it does spend is the one shared REST budget that also holds the
  claim lock, at one `scan` per pass. Bound it exactly there: at most one analyst pass per boundary, never
  two at once, and none at all while an `EX_RATE` backoff is in effect.
- **Where no analyst resolves, the step is a no-op and there are no packets to collect.** `board-analyst`
  is `scope: operator` and materializes nowhere, so it resolves only in an operator checkout; everywhere
  else findings-and-filing's other branch governs and the finder files its own row. An empty collection is
  that branch reporting itself, not a broken loop.

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
`fsgg:review-decision/v2` marker; the worker may not merge before it observes that marker. If material
findings remain after round three, verify the escalation marker, close the ordinary PR without merging,
and automatically enter the repair phase; do not post acceptance, merge, or permit round four on the
exhausted chain.

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
require the `fsgg:review-decision/v2` marker naming the exhausted PR and its escalation
marker before treating any repair-phase pass as landable. If the required route is unavailable, or once
the repair phase itself exhausts its own round ceiling, verify the escalation marker, `Blocked on:
human/action` sentinel, `Blocked` status, and released claim; do not post acceptance, merge, start a
second repair phase, or permit a round beyond either ceiling. A
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
