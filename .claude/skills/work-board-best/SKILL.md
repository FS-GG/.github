---
name: work-board-best
description: Use when explicitly asked to burn down a wired product board with best-quality routed workers and explicit runtime-specific subagent model selection.
---

# work-board-best

Run the complete canonical [work-board](../work-board/SKILL.md) host workflow. This variant changes
only the model routing of deployed workers; workspace checks, triage, lane selection, feedback,
verification, and termination remain owned by `work-board`.

Before every worker dispatch, identify the active host runtime. Pass this route explicitly to every
subagent spawn:

| runtime | model | effort |
|---|---|---|
| Codex | `gpt-5.6-sol` | `medium` |
| Claude Code | `opus` | `high` |

Never let a host default choose the model or effort. If the active runtime cannot request this exact
model and effort, report the unsupported route and stop before dispatching a worker; do not downgrade,
fall back, or continue a partial wave.

**Repair-phase route.** When an ordinary three-round chain exhausts, automatically enter the
repair phase and dispatch its fresh
implementer and fresh critic at this same route — `work-board-best`
already names the top capability tier this org's routing tables define, so its repair-phase route is
identical to its ordinary route above; the escalation is the fresh attempt and the higher round ceiling,
not a stronger model. The unsupported-route rule above applies to the repair-phase dispatch without
exception.
