---
name: work-board-best
description: Use when explicitly asked to coordinate an organization or selected product V2 board with best-quality workers and explicit runtime-specific model selection.
---

# work-board-best

Apply the complete canonical [work-board](../work-board/SKILL.md) workflow and its explicit scope.
This wrapper changes only model routing, never ownership, intake, capacity, review, qualification,
delivery, telemetry or operation authority. The host may remain accountable owner; no dispatch is mandatory.
Identify the active runtime and pass this route explicitly to every authorized subagent spawn:

| runtime | model | effort |
|---|---|---|
| Codex | `gpt-6-astra` | `high` |
| Claude Code | `opus` | `high` |

Never use a host default. If unsupported, report the exact route and stop before dispatch;
do not downgrade, fall back or continue a partial wave. No fixed waves or critic/repair chains apply.
