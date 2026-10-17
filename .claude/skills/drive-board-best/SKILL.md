---
name: drive-board-best
description: Use when explicitly asked to burn down the FS-GG Coordination board with best-quality routed workers and explicit runtime-specific subagent model selection.
---

# drive-board-best

Inherit the complete canonical [drive-board](../drive-board/SKILL.md) organization route, including
its prepare-only .9/.11 boundary, native joins, per-slice ordered capacity accounting, routine owner,
independent review, required CI and ADR-0084 delivery. This variant changes only requested worker
routing. Historical no-dispatch acceptance supplies no successor activation or worker permission.

Before every authorized worker dispatch, identify the active runtime. Pass this route explicitly to every
subagent spawn and verify the actual selected model/effort:

| runtime | model | effort |
|---|---|---|
| Codex | `gpt-6-astra` | `high` |
| Claude Code | `opus` | `high` |

Never let a host default choose model or effort. If the active runtime cannot request and establish
this exact route, report the unsupported route and stop before that dispatch; do not downgrade,
fall back, silently substitute or start a partial assignment. Actual runtime capabilities govern;
a model name in this table proves neither availability nor execution. Product work-board model
routes are unchanged. A repair preserves the same accountable owner and existing PR; there is no
automatic heavier worker/critic chain, claim, ledger or new attempt merely because a check failed.
