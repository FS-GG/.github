---
name: work-board
description: Use when explicitly asked to burn down one coordination-wired product workspace's board. Reconcile and triage backlog first, fan out isolated item workers through disjoint lanes, verify, and re-plan.
---

# work-board

Burn down one coordination-wired workspace's board. The local board is both plan and ledger.

1. Reconcile the workspace and consume the complete four-part `check-board` result.
2. Run [backlog-triage](references/backlog-triage.md), classifying every relevant parked row without
   guessing human judgement and promoting only evidenced actionable work to `Ready`.
3. Compute local disjoint lanes and bounded concurrency through the normal scheduler.
4. Spawn workers under [host-loop](references/host-loop.md)'s two-wave, fixed-slot cap and
   consolidation rule — do not restate or vary those numbers here; its two review slots are reserved for
   independent critics and an implementer may never fill one. Give each worker a stable feedback cycle
   id. Each owns one item through claim, implementation, critique and up to three repair/review rounds,
   green merge, obligations, verified feedback, and done—or human escalation after an exhausted third
   round. During worker setup, interactive/game work must explicitly invoke the `pnext-item`
   performance-first planning gate before implementation begins.
   Persist a typed cycle envelope beside the board before scheduling: fresh-read the board into its
   source revision and units, run `fsgg-coord cycle inspect`, then `register` (or resume its exact
   stable id) for the selected unit. Bind each unit's stable external feedback/critique identity as
   `providerCycleId`, then pass the actual generated SDD verification, validated schema-v3 critique,
   and validated schema-v2 feedback artifacts to `advance`. Each provider input names `rootPath` and
   `artifactPath`; feedback additionally names its `auditPath` and ordered `phases`. The engine reruns
   `fsgg-sdd verify` and the canonical critique/feedback validators itself; normalized or minimally
   shaped caller-authored envelopes are not provider evidence, and journey applicability comes from
   the validated critique artifact. Persist the `updateReceipt` emitted and durably journaled
   by the guarded merged-head/checkpoint `update` and pass that exact receipt to `complete`, then
   fresh-read and inspect again. Multiple ready units require an explicit operator
   parallel authorization and recorded disjoint touch-sets; otherwise schedule one. Missing receipts,
   evidence paths, or a stale source/head fail closed.
5. Report live item state immediately. Use the kit-provided `scripts/fsgg-coord-report`. Start one
   explicit local session at driver entry. On every material transition — and on an unchanged
   heartbeat — pass its stable receipt as the trigger plus the already-cached lane snapshot; do not
   perform a compensating GitHub read merely to print. Emit the reporter's rich projection when the
   terminal supports it, otherwise its byte-stable plain projection. Its JSON/JSONL ledger is the
   session's source for cumulative totals, so never maintain parallel prose counters. The canonical
   workflow here is inherited unchanged by `work-board-normal` and `work-board-best`.
   The supplied snapshot always includes typed lane-capacity facts: configured implementation and
   review capacity, active lanes, open slots, and ordered limiting reasons with source/freshness.
   Account explicitly for slot/review caps, overlap, no schedulable item, REST reserve/backoff, claim
   contention or an indeterminate receipt, and human/decision blockers; never print a low activity
   count without its measured cause. Reuse the reporter's session-locked derived cache for unchanged
   heartbeats; width and color are local projections and never justify another board read.
   **Do not detect transitions or reconstruct the active set from memory** (`.github#2135`) — that is
   exactly what went late, omitted externally claimed work, and reported a still-live claim as
   terminal. After every fresh board read, run
   `scripts/fsgg-coord driver --events --cursor <session-scoped-cursor-file> --text` (or `--json` for
   the reporter) and forward its two-line projection: it is engine-derived from live board, claim, PR,
   review, and delivery-obligation facts, is idempotent (an unchanged read emits no duplicate line), and
   its active inventory is always the COMPLETE set — claimed, in review, newly dispatched, or merged
   with unverified obligations — independent of whether anything transitioned this read, including work
   claimed or advanced by a process other than this host. Emit the two lines it produces:
   - line 1 — the material transition(s) since the cursor's last read (`no material transitions` when
     none occurred; never fabricate one to fill this line).
   - line 2 — the complete active inventory (`no active items` when the projection reports none).
   Do not defer either line to a wave summary or final response. Keep the driver turn alive while any
   item remains active, continue the host loop, and report each transition when it occurs. A read that
   fails renders as the projection's own `unreadable` state for that item, never as a silently emptied
   active line — surface it exactly as reported, do not paper over it with the prior read's line. Each
   transition names both its previous and new state (`<item>: <previous> -> <new> (<reason>)`), so a
   `Done` that passed through `review-repair:N` is visible in the line itself — read the `previous`
   state before describing a landing as an ordinary one; never paraphrase it away.
6. Verify the independent-review marker, ordered round/URL/SHA chain, critic independence, and every
   material finding disposition; reject any critic-filed item without evidence-backed materiality.
   Where the typed review/repair protocol surface (`scripts/fsgg-coord review --snapshot ...`) is
   available, its one current state/action is a mechanical cross-check on the same chain — never a
   substitute for reading the marker chain and materiality yourself.
   After an exhausted third round, refuse a fourth round of that same chain and automatically dispatch
   the repair phase under [host-loop](references/host-loop.md)'s validated-exhaustion and
   escalated-route rules. Verify its own chain, fresh critic, and repair-phase marker exactly as
   host-loop describes. If the required route is unavailable, or once the repair phase itself exhausts,
   refuse further rounds or merge and verify the human-action park, released claim, and escalation
   marker instead. Then run the exact
   checkpoint, schema-v2 report, and activation-envelope validators against merged paths. Missing,
   invalid, unreadable, or wrong-cycle evidence fails closed; retain or explicitly transfer the repair
   owner until validation passes, then discard the worker and critic.
7. Reconcile and re-triage from a fresh read after every wave so worker-filed follow-ups enter the next
   plan while each item worker consumes its current agent-authored delivery-route receipt. The fixed
   checklist is evidence only: it never derives a simple/complex or lightweight/SDD route.
8. Stop only when fresh reconciliation and triage leave no startable or actionable/untriaged work and
   every completed cycle is covered by a validated workspace feedback roll-up. Surface deliberately
   parked and human-blocked backlog without spinning; then update/land the workspace report.

Load [host-loop](references/host-loop.md) for the shared worker/verification/termination contract and
[workspace-scope](references/workspace-scope.md) for the single-repository ledger rules.
Load [feedback-contract](references/feedback-contract.md) for worker activation, exact validation
commands, zero-event representation, host acceptance, and board termination.
Load [deep detail](references/deep-detail.md) only for recovery paths and extended rationale.
