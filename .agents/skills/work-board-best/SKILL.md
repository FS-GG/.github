---
name: work-board-best
description: Use when explicitly asked to burn down a wired product board with best-quality routed workers and explicit runtime-specific subagent model selection.
---

# work-board-best

Run the complete canonical [work-board](../work-board/SKILL.md) host workflow. This variant changes
only the model routing of deployed workers; workspace checks, triage, lane selection, feedback,
verification, and termination remain owned by `work-board`.

Inherit `work-board`'s routine route unchanged. A routine item may remain with the accountable host and
never requires a dispatch; if isolation or useful parallel capacity calls for one delegated routine
owner, the table below selects that owner's model. It does not authorize a critic, confirmation worker,
or any strict-route artifact.
That inheritance explicitly includes canonical `work-board`'s ADR-0084 selection/reuse, coherent-pending,
late-dispute, and dependent-activation behavior; this variant changes none of it.

Before every worker dispatch, identify the active host runtime. Pass this route explicitly to every
subagent spawn:

| runtime | model | effort |
|---|---|---|
| Codex | `gpt-5.6-sol` | `medium` |
| Claude Code | `opus` | `high` |

Never let a host default choose the model or effort. If the active runtime cannot request this exact
model and effort, report the unsupported route and stop before dispatching a worker; do not downgrade,
fall back, or continue a partial wave.

**Repair-phase route.** When a strict item's three-round review chain exhausts, automatically enter the
repair phase and dispatch its fresh
implementer and fresh critic at this same route — `work-board-best`
already names the top capability tier this org's routing tables define, so its repair-phase route is
identical to its ordinary route above; the escalation is the fresh attempt and the higher round ceiling,
not a stronger model. The unsupported-route rule above applies to the repair-phase dispatch without
exception.
