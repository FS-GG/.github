---
name: work-roadmap
description: "Execute an explicitly selected technical plan slice; canonical work-board owns scheduling, recovery, capacity, qualification and native delivery."
---

# Execute a selected plan slice

Use [$atlas](../atlas/SKILL.md) first. The owning plan defines technical acceptance and history;
it is not a scheduling ledger. Canonical [$work-board](../work-board/SKILL.md) supplies explicit scope,
current selection, recovery, accountable owner, capacity, operation authority and exact delivery.
When invoked directly, resolve that coordinator boundary before selecting work. Do not burn down
unchecked roadmap rows, restart completed work, create duplicate status/closure tables or a closure-only PR.

Execute the already assigned slice through [pnext-item](https://github.com/FS-GG/.github/blob/main/.agents/skills/pnext-item/SKILL.md), preserving its
complete plan/issue acceptance, existing owner, attempt, worktree and PR. This adapter supplies
technical references and qualification below; it adds no scheduler, claims, packets or second lifecycle.
A valid selected plan needs no restart planning. Return accepted scope and remaining assertions to
work-board so clearing continues. Preserve technical history, incomplete outcomes and original Unknowns;
update strategic links only for scope/navigation changes. Do not select work from roadmap checkboxes.

## Qualification and conditional routes

Apply the permanent qualification-selection doctrine in
ADR-0084 (`https://github.com/FS-GG/.github/blob/main/docs/adr/0084-semantic-reuse-never-cancels-coherent-validation.md`). Run the cheap
content/semantic classifier first; after acceptance, start the coherent run, and only a validated exact-head
`reused` receipt may start delivery concurrently with it. `current` or missing valid reuse waits for a
pass, while `deferred`/`failed` refuses. Keep post-merge pending distinct from disputed, and block dependent
acceptance or activation on a late failure. Use the shared `tools/routine-delivery.py` behavior rather than
reimplementing these transitions in the skill.

For source work associated with a protected operation, use the routine steps for the source PR and leave the
operation pending until its independent authority and safeguards permit it. Invalid or unknown technical or
operation authorization fails the affected effect; it does not reclassify delivery as heavyweight.

Apply the permanent [diagnostic execution policy](https://github.com/FS-GG/.github/blob/main/docs/coordination/diagnostic-execution.md)
within the original budgets. Preserve first causes and additional independent findings; failed or unknown
prerequisites block dependent effects, and partial coverage cannot qualify. Read [execution diagnostics](references/execution-diagnostics.md) and its policy before diagnostic execution.

| Condition | Read before the affected work |
|---|---|
| Native dispatch or a prospectively selected observed normal-root item | [Runtime observation](references/runtime-observation.md), including the default `fsgg-coord-engine skill roadmap-telemetry` route. Missing usage stays unknown; observation cannot block delivery. |
| Receiver has no routine producer | [Routine producer installation](references/routine-producer-installation.md). Its closed installation predicate does not establish ordinary eligibility or activation. |
| Recorded explicit human instruction selects heavyweight process for named scope | [Heavyweight roadmap](references/heavyweight-roadmap.md), including lifecycle, status, [feedback-contract](references/feedback-contract.md) and critique obligations. Read its complete applicable protocol before dispatch. |
| Interactive/game implementation | Load the local [performance-first planning gate](references/performance-first.md) before implementation begins. |

The selected owner executes only the assigned acceptance slice; new scheduling returns to work-board. Source delivery, publication, installation and activation remain separate claims.
