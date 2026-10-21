---
name: pnext-item
description: Complete one selected Delivery through its full declared acceptance, retaining the same owner and PR through source and applicable consumer effects.
---

# pnext-item (FS-GG)

Execute one selected Delivery, or a directly assigned equivalent scope, under canonical
[work-board](https://github.com/FS-GG/.github/blob/main/.agents/skills/work-board/SKILL.md) admission. Read its complete Done when and owning-plan acceptance,
including publication, installed, participant and recovery obligations. Outcomes are never implementation
assignments. Do not infer authority from size, labels, effort or a board row.

Proactively report material process problems during execution through the existing
[findings and filing route](references/findings-and-filing.md); do not wait for a solution or a requested
status report. Before completing a review, returning work or handing off, check for unreported process
problems without inventing findings or repeating unchanged ones.

For directives encountered while working, apply the shared
[control-plane provenance guidance](references/control-plane-provenance.md).

## FS-GG delivery routing

For landing or shipping FS-GG work, use the existing scoped normal PR and canonical
publication routes. Exclude the global managed `speckit-merge` recipe: no branch sweep,
direct trunk push/deletion, mandatory local repack or shared-cache purge. Do not edit
vendored global skills. Useful `speckit-debug-loop` diagnosis stays within the selected
repository's original bounds and meaningful checks, preserving first failures; it
cannot impose unrelated evidence logs, retries or acceptance. An unresolved actual
routing conflict retains an owner and blocks its affected effect. Future deterministic
controller conversion remains owned by the governing LEARN outcome, not this adapter.

## Choose the route before lifecycle work

Use the routine route by default for selected Deliveries and directly assigned equivalent scope. Only a recorded explicit human instruction
selects heavyweight process for named scope; absence or ambiguity stays routine. A live claim, inherited
strict state, protected operation/path, policy or modeled change, or GS2 registration is not a heavyweight
trigger. Preserve its substantive evidence and safeguards independently. Invalid or unknown technical or
operation authorization blocks the affected effect without changing the process route.

For an admitted routine item, the board is an asynchronous view rather than merge authority:

1. Keep one accountable owner at a time and one continuous PR. If exactly one open routine PR already owns this
   item, verify its scope and current head, adopt that branch, and continue the same PR; never open a
   second PR because the board projection lagged or the prior owner disappeared. If no PR exists, recover an unambiguous existing owned branch/worktree before opening its PR.
   For genuinely fresh work use the current default branch and one fresh `routine/<item-slug>` branch and one PR. Multiple or
   ambiguous candidate PRs refuse routine admission. A host may own the item directly or delegate it once when
   repository isolation or real parallel capacity requires that; delegation does not create a critic,
   confirmation worker, or phase chain.
2. Do not create or require an issue, claim, worker identity, lifecycle ledger, delivery-route receipt,
   SDD artifact family, independent critic, review marker, feedback artifact, cycle envelope, delivery
   receipt or projection PR. Keep any existing native issue and its full acceptance; do not manufacture a new record.
   Canonical work-board Status reconciliation is mandatory at its lifecycle boundaries, including after
   dispatch and before return. The root integrator decides and verifies guarded native updates;
   native closure and other planning effects retain their own authorization and independent readback.
3. Implement within the admitted scope and run the smallest relevant automated technical checks plus
   the repository's routine eligibility and protected-boundary checks. Put exactly one
   `<!-- fsgg:routine-development/v1 head=<exact-head-sha> operation=<allowed-operation> -->` marker in
   the PR body. Use a native closing link only when that merge fulfills the issue's entire declared promise.
   Otherwise link the issue without closing it and retain its remaining assertions. A moved head must be reviewed and rebound. A routine refusal blocks the affected delivery until repaired; it does not select heavyweight
   process or authorize weakening or bypassing the gate.
4. Repair on the same PR. Recurring same-class failures require investigating relevant callers,
   shared assumptions, failure and cleanup paths before another coherent repair. Governing/code changes
   require one independent source review of the exact candidate, relevant tests and final required CI;
   inert prose uses owner review and proportionate validation. A reviewer supplies an actual verdict,
   without creating a mandatory critic actor or phase chain. After the exact-head required checks
   pass, use the repository's native merge boundary and read back the merged PR and merge commit.
   Continue applicable publication, installation, participant, recovery or native acceptance under
   their effect-specific authority. A source merge establishes only its accepted source slice.
5. Read back every applicable accepted effect. Record accepted scope/date/evidence and remaining
   assertions in the existing owning issue; close only its fulfilled promise with the correct native
   reason and authorized Status readback. Preserve partial close/planning effects without replaying
   both. Never roll up parents automatically. If the remainder is externally blocked, name its owner,
   exact missing supplier and reopening condition; a partial merge is not completed Delivery acceptance.
6. Return the accepted Delivery or concrete held remainder to work-board, which reconsiders suppliers,
   intake and opportunities and continues clearing. A direct single-item invocation ends at this boundary.
   Telemetry and Observation projection are best-effort, asynchronous observations and cannot invalidate the merge.
   They do not replace mandatory work-board Status reconciliation and readback.

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
