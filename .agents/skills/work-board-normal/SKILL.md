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

**How to request that route.** "Cannot request it" means the runtime exposes no mechanism — not that
the mechanism is inconvenient, and not that a host default happens to match:

- **Claude Code** — the `Agent` tool carries `model` but has **no `effort` parameter**, so effort is
  requestable only through an agent definition's frontmatter (`model:` and `effort:` in
  `.claude/agents/<name>.md` or `~/.claude/agents/<name>.md`), dispatched by `subagent_type`. That
  registry is read **at session start**: a definition written mid-run does not resolve in that run —
  the spawn fails `Agent type '<name>' not found` — so the definitions must already exist in the
  session that dispatches.
- **Any other runtime** — use its own explicit per-spawn model/effort arguments. Never invent another
  host's tool name or syntax, and never read a matching host default as a requested route.

The FS-GG `.github` repo ships those definitions — `.claude/agents/fsgg-worker-normal.md`
(`sonnet`/`high`) and `.claude/agents/fsgg-worker-repair.md` (`opus`/`high`) — so a checkout holding
them dispatches both routes without authoring anything; a workspace without them must add them **before**
the dispatching session. Measured 2026-08-04 on Claude Code, before they were checked in: a run reached
dispatch with reconcile, engine currency, triage and lanes all green, then stopped here because no
definition carried `effort: high`, and writing them repaired the *next* session rather than that one. So
if they are absent, report the unsupported route **and name the definition the next run needs** — a
report that does not say what would make the route supported costs a whole run to rediscover. Stopping
is cheap: no claim is held, no lease is spent, and every item stays schedulable.

**Repair-phase route.** When an ordinary three-round chain exhausts, automatically enter the
[repair phase](../pnext-item/references/independent-review.md#repair-phase) and dispatch its fresh
implementer and fresh critic at `work-board-best`'s route instead
of the table above:

| runtime | model | effort |
|---|---|---|
| Codex | `gpt-5.6-sol` | `medium` |
| Claude Code | `opus` | `high` |

Never let a host default choose the model or effort for the repair-phase dispatch either. If the active
runtime cannot request this exact model and effort, report the unsupported route and stop before
dispatching a repair-phase worker; do not downgrade, fall back, or continue a partial repair-phase
chain.
