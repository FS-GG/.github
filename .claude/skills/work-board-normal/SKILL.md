---
name: work-board-normal
description: Use when explicitly asked to coordinate an organization or selected product V2 board with normal-cost workers and explicit runtime-specific model selection.
---

# work-board-normal

Apply the complete canonical [work-board](../work-board/SKILL.md) workflow and its explicit scope.
This wrapper changes only model routing, never ownership, intake, capacity, review, qualification,
delivery, telemetry or operation authority. The host may remain accountable owner; no dispatch is mandatory.
Identify the active runtime and pass this route explicitly to every authorized subagent spawn:

| runtime | model | effort |
|---|---|---|
| Codex | `gpt-6.1-sol` | `medium` |
| Claude Code | `sonnet` | `high` |

Never use a host default. If unsupported, report the exact route and stop before dispatch;
do not downgrade, fall back or continue a partial wave. No fixed waves or critic/repair chains apply.
