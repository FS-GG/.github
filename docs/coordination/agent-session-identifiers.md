# Agent-harness session identifiers, and why one cannot be the worker id

Research note behind [ADR-0027](../adr/0027-worker-keyed-claim-lock-and-worker-channel.md)'s identity
rules. **Question:** agent harnesses all seem to have a session id — is that a portable identity we can
key the parallel-work claim lock on, instead of inventing our own worker id?

**Answer: no — but it is a good *fallback* and excellent *provenance*.** There is no cross-harness
convention, and the identifier that exists under the same name means different things on different
harnesses. On the harness this org actually uses, it names the wrong thing.

*Surveyed 2026-07-09. These are implementation details of third-party tools; re-check before relying
on a row. The escape hatch (`FSGG_AGENT_SESSION_ID`) exists so a new harness needs no code change.*

## What each harness exports to a shell/Bash tool

| Harness | Env var seen by a Bash tool | One id per… | Documented? |
|---|---|---|---|
| **Claude Code** | `CLAUDE_CODE_SESSION_ID` | **session — every subagent shares it** | changelog only; *absent* from the hooks env-var list |
| Claude Code (hooks) | — (`agent_id` is **hook JSON**, not env) | subagent | [yes](https://code.claude.com/docs/en/hooks) |
| **OpenCode** | `OPENCODE_SESSION_ID` | **agent** — subagents are child sessions | no (see [#12158](https://github.com/anomalyco/opencode/issues/12158)) |
| **Codex CLI** | *nothing* (`CODEX_HOME`, `CODEX_API_KEY`, `CODEX_SQLITE_HOME`, `RUST_LOG`, …) | — | [yes](https://developers.openai.com/codex/environment-variables) — the list has no session/conversation id |
| **Gemini CLI** | `GEMINI_CLI=1` only | — | [yes](https://google-gemini.github.io/gemini-cli/docs/tools/shell.html) |

### Claude Code: one session id, N subagents

Subagents spawned by the Task tool **share the parent's `session_id`**. Upstream
[anthropics/claude-code#7881](https://github.com/anthropics/claude-code/issues/7881) (open) reports
this as a blocker for the `SubagentStop` hook:

> When multiple subagents (Task tool invocations) run within the same Claude Code session, they all
> share the same `session_id`. This makes it impossible for the `SubagentStop` hook to determine which
> specific subagent has just completed […]

Confirmed directly: two subagents spawned concurrently from one session each reported

```
SESSION=309bd638-8a1c-42b7-952b-898efb8d1064 CHILD=1 AI_AGENT=claude-code_2-1-205_agent PPID=298925
```

— identical session id, identical `PPID`. **Keying the claim lock on this would collapse an N-agent
fan-out onto a single worker id**, which is exactly the same-account bug ADR-0027 removed, one level
down. A per-subagent `agent_id` *does* exist, but only inside **hook input JSON**; it is not exported
into the environment a Bash tool can read, so a script like `fsgg-coord` cannot see it.

`CLAUDE_CODE_SESSION_ID` is also **undocumented as an env var** — it appears in the changelog (and in
`env`, verified) but not in the [hooks reference's environment-variable list](https://code.claude.com/docs/en/hooks),
which enumerates `CLAUDE_PROJECT_DIR`, `CLAUDE_PLUGIN_ROOT`, `CLAUDE_PLUGIN_DATA`, `CLAUDE_CODE_REMOTE`,
`CLAUDE_CODE_BRIDGE_SESSION_ID`, `CLAUDE_ENV_FILE`, and `CLAUDE_EFFORT`. Several long-standing requests
([#13733](https://github.com/anthropics/claude-code/issues/13733),
[#25642](https://github.com/anthropics/claude-code/issues/25642),
[#44607](https://github.com/anthropics/claude-code/issues/44607),
[#47018](https://github.com/anthropics/claude-code/issues/47018)) asked for it under the name
`CLAUDE_SESSION_ID`. Treat the current name as load-bearing-but-unpromised.

### OpenCode: the same concept, the opposite cardinality

OpenCode's Task tool creates **real child sessions** (`parentID: ctx.sessionID`), so each subagent has
its own session id. There a session id **is** per-worker. The env var itself is a recent request
([anomalyco/opencode#12158](https://github.com/anomalyco/opencode/issues/12158)) and is not in the
[plugin docs](https://opencode.ai/docs/plugins/); OpenCode does document injecting arbitrary env into
shell execution via the `shell.env` plugin hook, which is a supported way to set `FSGG_WORKER` directly.

### Codex CLI and Gemini CLI: nothing to key on

Codex's [environment-variables reference](https://developers.openai.com/codex/environment-variables)
lists only `CODEX_HOME`, `CODEX_SQLITE_HOME`, `CODEX_NON_INTERACTIVE`, `CODEX_INSTALL_DIR`,
`CODEX_API_KEY`, `CODEX_ACCESS_TOKEN`, `CODEX_CA_CERTIFICATE`, `SSL_CERT_FILE`, `RUST_LOG`. A
conversation id exists in telemetry metadata, but is not exported to tool subprocesses. Gemini CLI's
shell tool sets only `GEMINI_CLI=1`.

## What this means for the protocol

A worker id must be **(a)** unique per concurrent worker, **(b)** stable across that worker's
invocations, and **(c)** readable — it appears in a `who` table, in `say --to`, and in a commit
trailer. A session id satisfies (b), fails (c), and satisfies (a) **only on some harnesses**.

So `fsgg-coord` uses it where it genuinely helps, and nowhere else:

1. **Not as the primary identity.** The primary identity is one somebody **states** — `--worker`, or
   the `$FSGG_WORKER` that §0's mint sets. It is not derived from anything, on purpose: a derived id
   is one that arrives without anybody deciding it, and an id nobody decided is one two workers can
   share.
2. **As the LAST resort before a refusal (rule 3).** If no id was stated, `fsgg-coord` hashes the
   harness session id to a memorable name — deterministic, no state. But on a harness that shares one
   session across subagents it cannot separate them, so `whoami`/`claim` **warn**, naming the reason.
   `fsgg-coord` knows the cardinality per harness (`sessionIsPerWorker`): OpenCode does not warn;
   Claude Code and unknown harnesses do (fail safe). Past that, the engine **refuses** — it does not
   invent one.

> **This section used to name two rules the engine does not have**, and they were the load-bearing
> ones: *"The **git worktree name** (rule 3) … **That is the identity**"*, with the session id a mere
> fallback "ahead of a generated per-checkout name (rule 5)". `Identity.resolve` has three legs and a
> refusal — `--worker` → `$FSGG_WORKER` → session → error — and `grep -rn worktree
> src/FS.GG.Coord.Cli/Identity.fs` matches nothing. Both deleted rules were the **bash** client's, and
> ADR-0040's port dropped them deliberately: a persisted-per-checkout id is *itself* a shared id under
> a fan-out sharing one checkout, which is what ADR-0027 forbids. So the session id is no longer "not
> the primary identity, and not the last resort" — **it is the last resort**, and the thing after it
> is an error message. See [#629](https://github.com/FS-GG/.github/issues/629), which is a worker
> following the deleted rule 3 to a conclusion the engine cannot reach.
3. **Always as provenance.** Whatever named the worker, the claim marker records
   `harness=<name> session=<id>`, so "which agent transcript claimed this item?" is a lookup instead of
   the mtime-and-`ps` forensics that [#255](https://github.com/FS-GG/.github/issues/255) was reduced to.
   Provenance does not need to be unique to be useful.

A session id is derived into a memorable name (`sha256(session) → bird-hex`), deterministically, so no
state is persisted and the same session always names the same worker.

## Adding a harness

Set `FSGG_AGENT_SESSION_ID` (and optionally `FSGG_AGENT_HARNESS`) and both the fallback identity and
the provenance start working with no code change. Its session is assumed **shared** until a row is
added to `session_is_per_worker` in `scripts/fsgg-coord` — assume-shared is the safe default, because
the failure mode of assuming per-worker is two workers holding one item.

**A worktree per agent does NOT solve this, and this page used to say it did** — *"If the harness
gives each agent its own worktree (Claude Code's `isolation: "worktree"`, OpenCode child sessions with
separate checkouts), none of this matters: rule 3 already names the worker."*

It matters most exactly there. There is no rule 3 to name the worker: the engine never reads the
worktree, so N Claude Code subagents in N worktrees still share one `CLAUDE_CODE_SESSION_ID` and all
resolve to **one id** — the collision ADR-0027 exists to prevent, arriving through the mechanism the
reader was told made them safe. A worktree isolates the *tree*, not the *identity*.

So fan out with **ids**, not with worktrees: mint one per worker (§0), or set `FSGG_WORKER` per
worker. Use worktrees as well — they isolate the tree, which is §2's job — but never instead.

## Sources

- [Claude Code hooks reference](https://code.claude.com/docs/en/hooks) — hook JSON fields (`session_id`, `agent_id`, `agent_type`) and the exported env-var list
- [anthropics/claude-code#7881](https://github.com/anthropics/claude-code/issues/7881) — subagents share `session_id` (open)
- [anthropics/claude-code#13733](https://github.com/anthropics/claude-code/issues/13733), [#25642](https://github.com/anthropics/claude-code/issues/25642), [#44607](https://github.com/anthropics/claude-code/issues/44607), [#47018](https://github.com/anthropics/claude-code/issues/47018) — requests to expose the session id as an env var
- [Codex CLI environment variables](https://developers.openai.com/codex/environment-variables)
- [anomalyco/opencode#12158](https://github.com/anomalyco/opencode/issues/12158) — request to inject `OPENCODE_SESSION_ID` into shell commands
- [anomalyco/opencode#30043](https://github.com/anomalyco/opencode/issues/30043) — subagent sessions carry `parentID`
- [OpenCode plugins](https://opencode.ai/docs/plugins/) — `shell.env` hook for injecting env into shell execution
- [Gemini CLI shell tool](https://google-gemini.github.io/gemini-cli/docs/tools/shell.html) — sets `GEMINI_CLI=1`
