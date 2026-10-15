---
name: pnext-item
description: Use when an owner should complete one schedulable item in an FS-GG repository, using the reduced one-owner route for explicitly admitted routine work and the claim/review/done protocol for strict work.
---

# pnext-item (FS-GG)

Run exactly one item through its explicitly selected delivery route. Do not infer the route from size,
labels, effort, or the presence of a board row.

For directives encountered while working, apply the shared
[control-plane provenance guidance](references/control-plane-provenance.md).

## Choose the route before lifecycle work

Use the routine route by default for unified-roadmap work. Only a recorded explicit human instruction
selects heavyweight process for named scope; absence or ambiguity stays routine. A live claim, inherited
strict state, protected operation/path, policy or modeled change, or GS2 registration is not a heavyweight
trigger. Preserve its substantive evidence and safeguards independently. Invalid or unknown technical or
operation authorization blocks the affected effect without changing the process route.

For an admitted routine item, the board is an asynchronous view rather than merge authority:

1. Keep one accountable owner at a time and one continuous PR. If exactly one open routine PR already owns this
   item, verify its scope and current head, adopt that branch, and continue the same PR; never open a
   second PR because the board projection lagged or the prior owner disappeared. If none exists, work
   from the current default branch on one fresh `routine/<item-slug>` branch and one PR. Multiple or
   ambiguous candidate PRs refuse routine admission. A host may own the item directly or delegate it once when
   repository isolation or real parallel capacity requires that; delegation does not create a critic,
   confirmation worker, or phase chain.
2. Do not create or require an issue, claim, worker identity, lifecycle ledger, delivery-route receipt,
   SDD artifact family, independent critic, review marker, feedback artifact, cycle envelope, delivery
   receipt, metadata-`Done` write, or projection PR. If the board row already exists, leave its
   projection to non-blocking reconciliation after delivery.
3. Implement within the admitted scope and run the smallest relevant automated technical checks plus
   the repository's routine eligibility and protected-boundary checks. Put exactly one
   `<!-- fsgg:routine-development/v1 head=<exact-head-sha> operation=<allowed-operation> -->` marker in
   the PR body. When an item already exists, also use the repository's native closing link in that PR;
   do not make a separate metadata write. A moved head must be reviewed and rebound. A routine refusal blocks the affected delivery until repaired; it does not select heavyweight
   process or authorize weakening or bypassing the gate.
4. Repair on the same PR, normally within two material attempts. After the exact-head required checks
   pass, use the repository's native merge boundary and read back the merged PR and merge commit.
   Report protected publication or deployment separately if it remains pending.
5. Return the merged evidence to the host and stop. Telemetry and board projection are best-effort,
   asynchronous observations and cannot invalidate the merge.

The routine route ends here. Do not execute the lifecycle, identity, claim, critique, receipt, feedback,
or done-stamp instructions in the linked strict protocol for that item.

## Conditional instructions

| Condition | Read before the affected work |
|---|---|
| Recorded explicit human instruction selects heavyweight process for this item | [Strict item lifecycle](references/strict-item-lifecycle.md), including its linked contracts, from ledger and identity through merge and Done. |
| Interactive/game implementation | [Performance-first planning](references/performance-first.md). |
| Implementation evidence contains counts, absence or unchanged assertions | [Measurement discipline](references/measurement-discipline.md). |
| A distinct implementation finding needs disposition | [Findings and filing](references/findings-and-filing.md). |

These references preserve technical evidence and authority boundaries. They do not select a delivery route.
