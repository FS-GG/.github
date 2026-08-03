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
   row without guessing human judgement, and promote only evidenced actionable work to `Ready`.
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

Load [host-loop](references/host-loop.md) for the shared concurrency, verification, and termination
contract. Load [org-scope](references/org-scope.md) for the ledger/scope rules unique to this driver.
Load [deep detail](references/deep-detail.md) only for recovery paths and extended rationale.
