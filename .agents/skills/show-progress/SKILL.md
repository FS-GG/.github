---
name: show-progress
description: Report lane progress and development health using delivery evidence and available telemetry, including token efficiency, process pathologies, practical improvements and unresolved blockers.
---

# Show progress

Answer: **Are we making useful progress, what is getting in the way, and what should change?**
Write for the person directing development. Default to roughly 1,500 words;
adapt when a different length is requested. Explain practical consequences in plain language.

## Report

1. **Overall health:** two or three sentences on whether delivery is advancing, slowing,
   recovering or stuck, and why. Compare with the last report or recorded expectations
   where available. A mixed picture is valid; missing history does not prevent reporting
   what works now.
2. **Lanes:** one compact table: `Lane | Meaningful progress | Problem / next step`.
   Cover the selected lanes, including blocked ones. Say what became usable, what moved
   closer to completion, and the next useful result. Distinguish implemented work from
   something people can actually use. Group unchanged lanes; avoid a task-by-task diary.
3. **Token efficiency:** use available telemetry and the
   [analysis guidance](references/telemetry-analysis.md). Give a compact usage breakdown,
   coverage and the relationship to accepted delivery; compare with a compatible baseline
   when one exists. Missing telemetry is a visible evidence gap, not zero cost or a reason
   to omit the rest of the report.
4. **Process problems we can fix:** identify the few problems doing the most harm.
   For each, give the observed pattern, its effect, and a practical change with an owner
   or decision needed. Explain why the change would help. Label suspected causes as such.
   Summarize existing owned findings and their reasoned dispositions from owning issues/PRs; for material
   unresolved findings, retain the owner, next action and reconsideration condition. Use canonical
   [finding disposition](../work-board/references/host-loop.md#own-process-findings-and-disposition)
   without a separate findings ledger, reporting worker or recurring unchanged reports.
5. **Blockers the current process cannot resolve:** name the missing input or capability,
   affected lanes, who can supply it, and what must change. State plainly when there is
   no supported workaround. More retries or agents cannot supply missing access, legal
   permission, participants or irretrievable evidence. If the route is merely uninvestigated,
   say that instead. If none are established, say so briefly.

End with at most three priorities or decisions, ordered by likely effect on delivery.
Use the [compact template](references/report-template.md) when saving a report.

## Outcomes and delivery reporting

Use canonical [work-board](../work-board/SKILL.md)'s selected scope and installed/binding boundary.
For successor inspection/3, distinguish full outcomes from scoped delivery completions; retain accepted
milestones, original remaining promises and exact missing facts/suppliers. Join parent requirements only
from an actually available owning record, never from a parent identity or child count. Native completion
reason/date and raw evidence remain separate from accepted outcome scope. Old reports without Work level/
hierarchy retain visible Unknowns; do not silently filter them away, infer a leaf is a delivery, force
new grammar onto product3, or change the executing caller. Do not maintain a duplicate status/closure ledger.

Report against the user's current strategic focus without turning that focus into a permanent skill
allowlist. Lead with usable capabilities and the next independently meaningful, executable delivery.
For a blocked delivery, name the exact missing input, the assertion it affects and the supplying owner.
A broad product epic or a blocked sibling is not itself a prerequisite: identify the specific shared
capability required by the selected acceptance. Keep deferred scope and original operations visible
without treating deferral or source completion as full outcome acceptance.

## Exercise judgment

- Ground material claims in current owning plans, board state and existing results.
  Include a snapshot time and a few useful links. Flag stale or incomplete coverage once.
- Consult existing telemetry snapshots, summaries and assessments for the selected scope.
  Keep measured facts, diagnostic counters, supported explanations and hypotheses distinct.
  Report whether an improvement is demonstrated, merely plausible, or unmeasured.
- Judge progress by useful outcomes and removed obstacles. PRs, green checks, agent activity
  and paperwork are evidence to interpret, not progress measures on their own.
- Scrutinize the process itself: repeated repairs, late integration, excessive handoffs,
  unnecessary gates, serial approval waits and reporting overhead can all impede delivery.
  Recommend removing or simplifying steps when supported; do not reflexively add checks.
- Distinguish productive iteration from repeated work that leaves the same obstacle in place.
  Do not declare a lane healthy just because its workers are busy.
- Keep hashes, run IDs, test counts, internal contracts and acceptance bookkeeping behind
  links. Mention a technical detail only when it explains a consequence or decision.
- Do not require percentages, effort accounting or forecasts for every lane. Give timing
  only when useful and defensible; an unresolved external dependency has no reliable finish
  date. For requested numbers, use the [short estimation guidance](references/estimation-method.md).

## Run and deliver

Reporting is read-only apart from saving and delivering the report. Do not launch tests or
product work to manufacture status, interrupt owners, change board state or implement fixes.

For an explicit `$show-progress`, use one reporting worker when delegation is permitted;
leave ongoing development alone. Follow the saved-report delivery instructions in the template.
If delegation is unavailable or prohibited, answer directly within the conversation's limits.
Ordinary progress questions stay in chat. Editing this skill does not invoke reporting.
