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
5. Report live item state immediately. Whenever the host changes or observes a material transition
   (`Ready`, `In progress`, review, CI, merged, release, downstream adoption, `Blocked`, or `Done`),
   emit exactly two concise user-facing lines:
   - `<item> — <new status>: <work in progress or gate being awaited>`
   - `Active: <item> — <current activity/gate>; ...` listing every currently active item and its
     current activity or gate.
   Do not defer either line to a wave summary or final response. Keep the driver turn alive while any
   item remains active, continue the host loop, and report each transition when it occurs. A `Done`
   transition that landed via the repair phase names it explicitly — `<item> — Done (repair phase):
   <PR>` — so a completion report cannot describe a repair-phase landing as an ordinary one.
6. Verify the independent-review marker, ordered round/URL/SHA chain, critic independence, and every
   material finding disposition; reject any critic-filed item without evidence-backed materiality.
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
   plan while the simple-versus-complex SDD lifecycle branch remains inside each item worker.
8. Stop only when fresh reconciliation and triage leave **no startable `Class: defect`**, no actionable
   or untriaged work, and every completed cycle is covered by a validated workspace feedback roll-up.
   `hardening` accumulates as ordinary backlog and is drained deliberately — it is not a reason to keep
   running; `decision` is surfaced to a human and never dispatched. **An unclassed row counts as a
   possible defect**, not a minor one: you may stop with some outstanding, but report them by number and
   say the run ended without establishing the board is defect-free. Read classes from `ready --json`'s
   `class` field *after* a `reconcile --apply`, and `lint`'s `CLASS-UNSET` for the rows that column
   cannot speak for. Surface deliberately parked and human-blocked backlog without spinning; then
   update/land the workspace report.

Load [host-loop](references/host-loop.md) for the shared worker/verification/termination contract and
[workspace-scope](references/workspace-scope.md) for the single-repository ledger rules.
Load [feedback-contract](references/feedback-contract.md) for worker activation, exact validation
commands, zero-event representation, host acceptance, and board termination.
Load [deep detail](references/deep-detail.md) only for recovery paths and extended rationale.
