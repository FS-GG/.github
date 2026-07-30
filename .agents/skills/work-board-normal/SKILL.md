---
name: work-board-normal
description: Use when explicitly asked to burn down a wired product board with normal-cost routed workers and explicit runtime-specific subagent model selection.
---

# work-board-normal

Run the complete canonical [work-board](../work-board/SKILL.md) host workflow. This variant changes
only the model routing of deployed workers; workspace checks, triage, lane selection, feedback,
verification, and termination remain owned by `work-board`.

Before every worker dispatch, identify the active host runtime. Pass this route explicitly to every
subagent spawn:

| runtime | model | effort |
|---|---|---|
| Codex | `gpt-5.6-terra` | `medium` |
| Claude Code | `sonnet` | `high` |

Never let a host default choose the model or effort. If the active runtime cannot request this exact
model and effort, report the unsupported route and stop before dispatching a worker; do not downgrade,
fall back, or continue a partial wave.
