---
name: drive-board
description: Use when explicitly asked to burn down the org-wide FS-GG Coordination board. Reconcile and triage backlog first, fan out disposable repo workers through safe scheduler lanes, verify, and re-plan.
---

# drive-board (FS-GG)

Burn down the org-wide Coordination board across repositories. The board is the ledger; this skill owns
cross-repo allocation, not item implementation.

1. Run [check-board](../check-board/SKILL.md), apply mechanical repairs, and consume its complete
   four-part result before making a scheduling decision.
2. Run the [backlog-triage](references/backlog-triage.md) stage. Classify every relevant `Backlog`
   row without guessing human judgement, and promote only evidenced actionable work to `Ready`. An
   implementation row is actionable only with a current typed delivery-route receipt; inspect that
   receipt and its SDD binding instead of inferring a route from effort, size, or prose.
3. Read typed lanes and active claims; choose bounded per-repo concurrency that respects touch-sets and
   available agent slots. **Dispatch breadth-first across repositories:** inspect each rostered repo's
   safe lanes and assign one disjoint, high-ranked lane per repo before assigning a second lane in any
   one repo. That prevents a `.github` chokepoint from consuming the whole worker pool while another
   repository has independently schedulable work. **Reorder by rank inputs, never by parking work in
   `Backlog`.** The scheduler
   packs lanes priority-greedily by a rank DERIVED from blocking count, `Class`, `Phase` and age
   (`.github#1598`), so raising an item's priority means fixing the fact that makes it important — draw
   the real `Blocked by` edge, set the `Class`, set the `Phase` — not moving a column. Read the ordering
   with `scripts/fsgg-coord batch --repo <repo> --explain`, which prints every candidate's rank, the
   inputs behind it, and how many lanes each admitted item displaced. Treat repeated path overlap as
   evidence to review, not permission to merge: consolidate only rows that are genuinely one operation,
   whose resulting acceptance criteria are their explicit union. Keep merely adjacent work separate and
   record why; a shared chokepoint alone is not a shared story.
4. Spawn fresh disposable workers with fresh identities/worktrees. Each runs exactly one
   [pnext-item](../pnext-item/SKILL.md) loop in its assigned repo, one item only. Dispatch under
   [host-loop](references/host-loop.md)'s two-wave, fixed-slot cap and consolidation rule — do not
   restate or vary those numbers here; its two review slots are reserved for independent critics and an
   implementer may never fill one.
5. Report live item state immediately. Use the kit-provided `scripts/fsgg-coord-report`. Start one
   explicit local session at driver entry. Every supplied lane snapshot must bind every lane to the
   exact Coordination project identity that produced it; pass the separate project-scoped driver
   receipt to the reporter as `--scope`, rather than trusting an identity embedded in that snapshot.
   `who --all-repos` is never a Coordination inventory. On every material transition — and on an unchanged
   heartbeat — pass its stable receipt as the trigger plus the already-cached lane snapshot; do not
   perform a compensating GitHub read merely to print. Emit the reporter's rich projection when the
   terminal supports it, otherwise its byte-stable plain projection. Its JSON/JSONL ledger is the
   session's source for cumulative totals, so never maintain parallel prose counters. Record a typed
   append-only correction that supersedes a bad event; never rewrite the ledger. The canonical
   workflow here is inherited unchanged by `drive-board-normal` and `drive-board-best`.
   The supplied snapshot always includes typed lane-capacity facts: configured implementation and
   review capacity, active lanes, open slots, and ordered limiting reasons with source/freshness.
   Account explicitly for slot/review caps, overlap, no schedulable item, REST reserve/backoff, claim
   contention or an indeterminate receipt, and human/decision blockers; never print a low activity
   count without its measured cause. Its row shows the fresh board projection beside timestamped
   execution evidence (claim, local worktree, PR/head, and check gate) and names their disagreement;
   do not collapse one into the other. Reuse the reporter's session-locked derived cache for unchanged
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
6. Verify each worker's PR, independent-review marker and ordered round/URL/SHA chain, critic
   independence, material finding dispositions, merge, publication/registry obligations, exact done
   stamp, released claim, and newly filed items against GitHub—not its narrative. Reject any new item
   whose review evidence does not establish materiality. After an exhausted third round, refuse a
   fourth round of that same chain and automatically dispatch the repair phase under
   [host-loop](references/host-loop.md)'s validated-exhaustion and escalated-route rules. Verify its own
   chain, fresh critic, and repair-phase marker exactly as host-loop describes. If the required route is
   unavailable, or once the repair phase itself exhausts, refuse further rounds or merge and verify the
   human-action park, released claim, and escalation marker instead.
7. **Once this wave's merges into `.github` are verified, and before the next wave is dispatched, bring
   the shared checkout's engine current.** In `.github` the engine is a *source build* under the **shared**
   checkout, so merging a worker's PR can leave the binary the whole fleet execs behind `origin/main` —
   and `.github#1549`'s guard then refuses every board write, the host's own included (measured twice in
   one run; one `set-field` was silently lost). **This host owns that repair**: `pnext-item` §1 makes
   every worker *check*, and escalates the *repair* here (`.github#1594`), because it mutates a checkout
   N workers share and the host is both the actor that creates the drift and the only one that can
   serialise the fix. After a `git fetch`, the check itself is four local `git` calls (~5 ms); gate the
   Release rebuild on it answering non-zero rather than rebuilding every wave. Exact spelling, and why the repair is
   `merge --ff-only` and never `pull --ff-only` (`.github#1664`), in
   [engine currency](references/deep-detail.md#engine-currency).
8. Despawn completed workers, then reconcile and re-triage from a fresh read so follow-ups and newly
   parked rows from that wave enter the next plan.
9. Stop only when a fresh reconcile and backlog triage leave **no startable `Class: defect`**, and no
   live claim, unresolved repair, queued write, or actionable follow-up. `hardening` accumulates as
   ordinary backlog and is drained deliberately — it is not a reason to keep running. `decision` is
   surfaced to a human and never dispatched. Surface deliberately parked and human-blocked backlog
   instead of spinning or declaring it completed.
10. **An unclassed row counts as a possible defect.** Read classes from `ready --json`'s `class` field
    *after* a `reconcile --apply` (it is the projection, current only as of the last reconcile), and
    read `lint`'s `CLASS-UNSET` for the rows that column cannot speak for. An unclassed row's severity
    is unknown, not minor — never count one as "no defect left". You may still **stop** with unclassed
    rows outstanding: report them by number as unresolved, and say the run ended without establishing
    the board is defect-free. Fixing one thing legitimately files two, so a wave producing only
    `hardening` and `decision` is completion, not a stall.

    **The census this depends on now also reaches a `Done`/closed row (.github#2254) — bounded, not
    exhaustive.** `CLASS-PROJECTION-LAG` is no longer `Open`-only: a row that reaches `Done` between two
    reconcile passes used to keep an EMPTY `Class` column forever, invisible to both `reconcile` and
    `lint` alike, because nothing examined it again once it closed. `reconcile`'s scan now pays one extra
    body read for exactly that population — a closed row whose board `Class` column is `None` — so a
    fresh `reconcile --apply` reaches it the same as an open row. **The bound is deliberate**: a closed
    row that already carries SOME `Class` value is never re-read (re-reading every `Done` row's body on
    every pass would undo the cost model the scan exists to keep cheap), so a WRONG (non-empty) `Class`
    value on an already-classed closed row is still not re-examined by this pass — that gap is unchanged
    from before #2254, and closing it would need a human or a fresh `Open` pass, not a bigger scan.

Load [host-loop](references/host-loop.md) for the shared concurrency, verification, and termination
contract. Load [org-scope](references/org-scope.md) for the ledger/scope rules unique to this driver.
Load [deep detail](references/deep-detail.md) only for recovery paths and extended rationale.
