---
title: Who drives the lifecycle — humans, agents & the CLI
category: FS.GG
categoryindex: 6
index: 14
description: Who is meant to run the fsgg-sdd lifecycle commands, how they differ from Spec Kit slash-commands, and exactly how an agent in Claude Code drives them through the seeded process skills.
---

# Who drives the lifecycle — humans, agents & the CLI

The lifecycle commands (`charter`, `specify`, `clarify`, `checklist`, `plan`,
`tasks`, `analyze`, `evidence`, `verify`, `ship`) are **real commands of the
`fsgg-sdd` CLI** — a .NET global tool. They are *not* slash-command skills like
Spec Kit's `/speckit-plan`. This page explains what that difference means, who is
supposed to run them, and — the part that surprises people coming from Spec Kit —
**how an agent inside Claude Code actually drives them.**

If you just want the stage-by-stage reference, that's
[The development lifecycle](lifecycle.md); this page is about *who runs them and
how they fit the process.*

---

## The one distinction that clears up the confusion

A Spec Kit `/speckit-plan` and an FS-GG `fsgg-sdd plan` are different **kinds of
thing**:

| | `/speckit-plan` (Spec Kit) | `fsgg-sdd plan` (FS-GG) |
|---|---|---|
| **What it is** | a *prompt* — a slash-command skill | a *program* — a real CLI invocation |
| **Where the logic lives** | in the agent following the prompt | in compiled, deterministic CLI code |
| **Who executes it** | the agent (an LLM), inside its host | the OS runs the tool; anyone can invoke it |
| **Determinism** | non-deterministic (it's a model) | deterministic — same input, same report |
| **Actor** | **A**gent | **M**achine |

This is the deliberate design, and it follows straight from the
[three-actor model](https://github.com/FS-GG/.github/blob/main/profile/README.md)
FS-GG is built around — **(H)uman**, **(A)gent**, **(M)achine** — and its bias:
*push each piece of work down to the cheapest actor provably competent for it.
Maximize M. Hand the rest to A. Reserve H for what only H can do.*

Spec Kit keeps the lifecycle **logic in agent prompts** (A re-derives the workflow
each run). FS-GG **freezes the lifecycle bookkeeping into the CLI** (M) — what stage
you're on, which artifacts exist, whether requirements are covered, whether the
readiness gates pass — and leaves the agent only the genuinely novel part. See
[Architecture §3, House style](https://github.com/FS-GG/.github/blob/main/docs/architecture.md#3-house-style-shared-across-all-four-product-repos)
and [§4.2](https://github.com/FS-GG/.github/blob/main/docs/architecture.md#42-fsggsdd--lifecycle-cli--contract-backbone).

---

## Who is supposed to run them: all three actors, one contract

The CLI is **actor-agnostic on purpose**. The same command is meant to be driven by
a human, an agent, or CI — and all three read the **same output**, because every
command projects one report three ways (precedence `--rich` > `--text` > `--json` >
default):

| Driver | How they invoke it | Projection they read |
|---|---|---|
| **Human** | types it at a terminal | `--rich` (Spectre.Console; degrades to zero-ANSI when redirected) |
| **Agent** (Claude Code / Codex) | shells out via a tool/Bash call | `--json` |
| **CI / scripts** | runs it in a pipeline | `--json` |

> **The JSON is the contract; plain and rich are projections of it.** There is
> **no separate "agent API"** — [as the automation guide puts it](automation.md#agents),
> *agents consume the same JSON contract you do.* Build scripts and CI on `--json`;
> use `--rich` at a terminal.

So "who uses the lifecycle commands?" — **whoever is doing the work.** In practice
that's usually *you and your coding agent together*: you (H) make the judgment calls
and sign off; the agent (A) authors and implements within known patterns; the CLI
(M) does the deterministic bookkeeping around both.

---

## Yes — agents drive them from Claude Code. Here's the wiring

This is the mechanism that isn't obvious. The CLI commands aren't skills, **but a
scaffolded project ships companion *process skills* that wrap them.**

When you scaffold with `lifecycle=sdd`, the `fsgg-sdd` CLI **seeds `fs-gg-sdd-*`
process skills** into the project's agent skill folders (`.claude/skills/` for
Claude Code, `.agents/skills/` for other runners). Unlike `/speckit-plan`, these
skills don't *contain* the lifecycle logic — they **tell the agent when and how to
shell out to the real `fsgg-sdd` commands**, and which strict authoring-contract
grammars to respect (see [Load-bearing authoring contracts](lifecycle.md#load-bearing-authoring-contracts)).

The loop inside Claude Code is therefore:

```text
 agent reads a seeded fs-gg-sdd-* skill      (process guidance: "to plan, run …")
   └─▶ runs:  fsgg-sdd plan --json            (a Bash/tool call — the real CLI)
        └─▶ parses the deterministic JSON report
             └─▶ acts on it (author the next artifact, fix a gap, advance a stage)
```

The deterministic bookkeeping is never re-derived by the model — it *reads* the
CLI's report and acts. Two supporting pieces:

- **`fsgg-sdd agents`** regenerates per-target Claude/Codex command + skill guidance
  from `readiness/<id>/work-model.json`, marked generated and never a second source
  of truth ([automation §Agents](automation.md#agents)).
- **These seeded skills are part of the coherent set.** Because a scaffolded product
  *= template@pin + `fsgg-sdd` CLI@installed*, and the CLI is what seeds the
  `fs-gg-sdd-*` skills, [ADR-0008](https://github.com/FS-GG/.github/blob/main/docs/adr/0008-fsgg-sdd-cli-first-class-member-of-coherent-set.md)
  makes the CLI a first-class member of the coherent set (the *orchestrator axis*).
  An old CLI that doesn't seed the current skills leaves the Claude Code agent with
  no process guidance — the invisible gap
  [ADR-0009](https://github.com/FS-GG/.github/blob/main/docs/adr/0009-cli-single-orchestrator-detect-and-remediate.md)
  makes detectable (`fsgg-sdd doctor` / `upgrade`; see
  [Versions & updates](versioning-and-updates.md#the-cli-keeps-you-coherent--but-never-behind-your-back)).

---

## Where Spec Kit's `/speckit-*` fits

The two worlds are **alternative lifecycle workspaces**, chosen by the template's
`lifecycle` parameter
([ADR-0002](https://github.com/FS-GG/.github/blob/main/docs/adr/0002-composition-by-scaffold-lifecycle-parameter-governance-populated.md)):

- **`lifecycle=spec-kit`** → you get the `/speckit-*` command skills (the
  prompt-driven Spec Kit flow; the agent does the work from prompts).
- **`lifecycle=sdd`** → you get the `fsgg-sdd` CLI plus the `fs-gg-sdd-*` process
  skills (the deterministic-CLI flow described here).
- **`lifecycle=none`** → neither; just the runnable app.

The stages map roughly one-to-one — with **one deliberate gap**:

| Spec Kit | FS-GG SDD |
|---|---|
| `/specify` `/clarify` `/plan` `/tasks` `/analyze` | `specify` `clarify` (`checklist`) `plan` `tasks` `analyze` |
| **`/implement`** | **— no command —** (you implement; `evidence` records that you did) |
| — | `verify` → `ship` (SDD-specific readiness / merge-boundary stages) |

**There is no `implement` command, by design.** SDD *brackets* implementation — it
tracks the artifacts and evidence *around* your work; it does not produce your
product code. The act of implementing is the gap between `analyze` and `evidence`
(see [Architecture §4.2](https://github.com/FS-GG/.github/blob/main/docs/architecture.md#42-fsggsdd--lifecycle-cli--contract-backbone)).

---

## The division of labor, stage by stage

Within any stage there are two surfaces (see
[Where the artifacts live](lifecycle.md#where-the-artifacts-live)):

- **Markdown under `work/<id>/`** — the *authoring* surface. This is **H/A** work:
  you (or your agent) write the actual spec text, the plan's approach, the task
  breakdown. `fsgg-sdd specify` is **not** "the AI writes your spec" — it's the
  deterministic scaffolding *around* the spec you author.
- **Schema-versioned JSON under `readiness/<id>/`** — the *machine* contract
  (`work-model.json`, `verify.json`, `ship.json`, …). This is **M** work: the CLI
  keeps it in sync with your authoring and computes coverage / readiness / gates,
  and never treats a generated view as a second source of truth.

So a typical unit of work is a back-and-forth:

| Actor | Does |
|---|---|
| **M** (`fsgg-sdd`) | scaffolds artifacts, keeps `readiness/*.json` coherent, reports coverage & readiness, enforces the ordering and the authoring grammars |
| **A** (agent) | authors the Markdown, **implements the product code** (the `analyze`→`evidence` gap), fixes what the CLI reports uncovered |
| **H** (you) | frames intent, makes novel calls, reviews, signs off at the merge boundary |

Governance, if adopted, is a fourth reader of the same contract — it *inspects*,
it never *blocks your inner loop* (see [Adopting governance](governance.md)).

---

## A concrete pass (agent in Claude Code)

```text
you:    "drive FR-003 through plan and tasks"
agent:  reads .claude/skills/fs-gg-sdd-plan  (seeded process skill)
        ├─ runs:  fsgg-sdd plan --json
        │         → report: plan scaffolded, 2 open decisions flagged
        ├─ authors the approach + resolves the decisions in work/003/plan.md
        ├─ runs:  fsgg-sdd tasks --json
        │         → report: 7 tasks, dependency-ordered; 1 requirement uncovered
        └─ fixes the uncovered requirement line, re-runs  fsgg-sdd tasks --json  → clean
you:    review the diffs, then let it implement (the analyze→evidence gap)
```

Every arrow that says *runs* is a real CLI invocation the agent makes as a Bash
call; every *report* is deterministic JSON the agent parses — the same JSON you'd
see with `--json`, or read yourself with `--rich`.

---

## See also

- [The development lifecycle](lifecycle.md) — the stage-by-stage reference.
- [Output, automation & CI](automation.md) — the JSON contract and the projections.
- [Getting started](getting-started.md) — install, scaffold, first pass.
- [Architecture](https://github.com/FS-GG/.github/blob/main/docs/architecture.md) —
  the H/A/M model, the house style, and the CLI-as-orchestrator design.
