---
name: drive-board-best
description: Use when explicitly asked to burn down the FS-GG Coordination board with best-quality routed workers and explicit runtime-specific subagent model selection.
---

# drive-board-best

Run the complete canonical [drive-board](../drive-board/SKILL.md) host workflow. This variant changes
only the model routing of deployed workers; reconciliation, triage, lane selection, verification,
engine-currency repair, and termination remain owned by `drive-board`.

Before every worker dispatch, identify the active host runtime. Pass this route explicitly to every
subagent spawn:

| runtime | model | effort |
|---|---|---|
| Codex | `gpt-5.6-sol` | `medium` |
| Claude Code | `opus` | `high` |

Never let a host default choose the model or effort. If the active runtime cannot request this exact
model and effort, report the unsupported route and stop before dispatching a worker; do not downgrade,
fall back, or continue a partial wave.

**How to request that route.** "Cannot request it" means the runtime exposes no mechanism — not that
the mechanism is inconvenient, and not that a host default happens to match:

- **Claude Code** — the `Agent` tool carries `model` but has **no `effort` parameter**, so effort is
  requestable only through an agent definition's frontmatter (`model:` and `effort:` in
  `.claude/agents/<name>.md` or `~/.claude/agents/<name>.md`), dispatched by `subagent_type`. Claude
  Code *watches* both directories and picks up an added or edited definition within seconds, with no
  restart — with one exception, and it is exactly the trap: a running session never detects an `agents`
  directory that **did not exist when it started**. The directory must pre-exist; its contents need
  not.
- **Any other runtime** — use its own explicit per-spawn model/effort arguments. Never invent another
  host's tool name or syntax, and never read a matching host default as a requested route.

The FS-GG `.github` repo ships those definitions — `.claude/agents/fsgg-worker-normal.md`
(`sonnet`/`high`) and `.claude/agents/fsgg-worker-repair.md` (`opus`/`high`) — so a checkout holding
them dispatches both routes without authoring anything, and `.claude/agents/` exists before any session
starts. Measured 2026-08-04, before they were checked in: `~/.claude/agents/` did not exist on that
host, a run reached dispatch with reconcile, engine currency, triage and lanes all green, and every
spawn failed `Agent type '<name>' not found` — creating the directory mid-run did not rescue that run,
which is the documented exception above. A workspace without the definitions must add them, creating the
directory **before** the dispatching session. Until then report the unsupported route **and name the
definition the next run needs** — a report that does not say what would make the route supported costs a
whole run to rediscover. Stopping is cheap: no claim is held, no lease is spent, and every item stays
schedulable.

**Repair-phase route.** When an ordinary three-round chain exhausts, automatically enter the
[repair phase](../pnext-item/references/independent-review.md#repair-phase) and dispatch its fresh
implementer and fresh critic at this same route — `drive-board-best`
already names the top capability tier this org's routing tables define, so its repair-phase route is
identical to its ordinary route above; the escalation is the fresh attempt and the higher round ceiling,
not a stronger model. The unsupported-route rule above applies to the repair-phase dispatch without
exception.
