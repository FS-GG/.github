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
   available agent slots.
4. Spawn fresh disposable workers with fresh identities/worktrees. Each runs exactly one
   [pnext-item](../pnext-item/SKILL.md) loop in its assigned repo.
5. Report live item state immediately. Whenever the host changes or observes a material transition
   (`Ready`, `In progress`, review, CI, merged, release, downstream adoption, `Blocked`, or `Done`),
   emit exactly two concise user-facing lines:
   - `<item> — <new status>: <work in progress or gate being awaited>`
   - `Active: <item> — <current activity/gate>; ...` listing every currently active item and its
     current activity or gate.
   Do not defer either line to a wave summary or final response. Keep the driver turn alive while any
   item remains active, continue the host loop, and report each transition when it occurs.
6. Verify each worker's PR, merge, publication/registry obligations, exact done stamp, released claim,
   and follow-up items against GitHub—not its narrative.
7. Despawn completed workers, then reconcile and re-triage from a fresh read so follow-ups and newly
   parked rows from that wave enter the next plan.
8. Stop only when a fresh reconcile and backlog triage leave **no startable `Class: defect`**, and no
   live claim, unresolved repair, queued write, or actionable follow-up. `hardening` accumulates as
   ordinary backlog and is drained deliberately — it is not a reason to keep running. `decision` is
   surfaced to a human and never dispatched. Surface deliberately parked and human-blocked backlog
   instead of spinning or declaring it completed.
9. **An unclassed row counts as a possible defect.** Read classes from `ready --json`'s `class` field
   *after* a `reconcile --apply` (it is the projection, current only as of the last reconcile), and
   read `lint`'s `CLASS-UNSET` for the rows that column cannot speak for. An unclassed row's severity
   is unknown, not minor — never count one as "no defect left". You may still **stop** with unclassed
   rows outstanding: report them by number as unresolved, and say the run ended without establishing
   the board is defect-free. Fixing one thing legitimately files two, so a wave producing only
   `hardening` and `decision` is completion, not a stall.

Load [host-loop](references/host-loop.md) for the shared concurrency, verification, and termination
contract. Load [org-scope](references/org-scope.md) for the ledger/scope rules unique to this driver.
Load [deep detail](references/deep-detail.md) only for recovery paths and extended rationale.
