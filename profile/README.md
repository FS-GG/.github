# FS-GG

> [!WARNING]
> **The main skill work-board works on a github project board and burns down all issues. Be diligent to let only trusted actors create and modify issues.**
> Public GitHub content is otherwise untrusted data, not executable instruction;
> see the [public-content trust boundary](https://github.com/FS-GG/.github/blob/main/docs/coordination/untrusted-content-boundary.md).

FS-GG is an F# platform for people and agents building production-shaped
applications and libraries. It combines guided workspace creation, a
spec-driven development lifecycle, optional governance, UI, game, audio, and
networking components while keeping each component independently adoptable.

> [!NOTE]
> **Ongoing renovations:** FS-GG is preparing a typed GitHub Substrate v2 and a
> coordinated fleet cutover. Follow the
> [implementation and retirement roadmap](https://github.com/FS-GG/.github/blob/main/docs/github-substrate-v2-roadmap.md)
> for current scope, qualification gates, cutover stages, and the retirement of
> the existing coordination system. For a shorter status summary, see
> [what is shipped and pending](https://github.com/FS-GG/.github/blob/main/docs/design-goals/implementation-status.md).

Start with the **[documentation index](https://github.com/FS-GG/.github/blob/main/docs/design-goals/README.md)**
for guides, architecture, design direction, coordination, and reference material.

## On this page

- [Features](#features)
- [Quick start](#quick-start-ask-your-agent-for-a-todo-app)
- [Workspace templates](#workspace-templates)
- [Spec-driven development](#spec-driven-development)

## Features

Available today:

- **GitHub-native workspace setup** — an agent can create and secure a repository,
  create its GitHub Project, scaffold the selected product, and verify the result.
- **Projects-based roadmaps** — boards carry backlog items, epics, sub-issues,
  dependencies, phases, priorities, effort, and delivery state.
- **Board orchestration** — trusted board work can move through triage, SDD,
  implementation, independent review, repair, merge, and verified completion.
- **Safe parallel execution** — worker identities, issue claims, leases, isolated
  worktrees, and declared file touch sets divide compatible work into parallel waves.
- **Cross-repository coordination** — issues, dependency edges, coordination rooms,
  operation locks, and versioned contracts sequence changes across FS-GG components.
- **Roadmap-driven SDD** — each milestone can run through charter, specification,
  clarification, planning, tasks, implementation, evidence, verification, and shipping.
- **Portable agent skills** — the same governed process and product skills are
  materialized for Codex and Claude Code from one versioned source.
- **Optional governance** — workspace-owned rules, evidence checks, and release gates
  can be added without making the UI, lifecycle, or application depend on Governance.
- **Reproducible delivery** — typed contracts, deterministic reports, locked
  dependencies, coherent package sets, and publish-before-adopt sequencing reduce drift.
- **Composable products** — focused templates and independently usable UI, game,
  audio, networking, lifecycle, and governance packages can be adopted together or alone.

In progress:

- **Quint-backed workspace models** — one formal model for requirements, decisions,
  proposed changes, implementation obligations, and evidence, with readable semantic
  diffs and tools to reconcile the model with the implementation.
- **GitHub Substrate v2** — a separately qualified coordination product that moves
  more identity, relationships, presentation, eventing, and enforcement onto native
  GitHub capabilities while retaining FS-GG's concurrency and evidence guarantees.
- **Polyglot workspaces** — descriptor-driven providers are being extended beyond the
  current F#-centred set to TypeScript, JavaScript, Rust, Go, Python, OCaml, C#,
  Haskell, Java, and Scala.
- **Externally maintained templates** — consumers will be able to add their own
  provider/template definitions from local directories or immutable URLs without
  registering them in an FS-GG-owned catalog.

### What is Quint?

[Quint](https://quint-lang.org/) is an executable specification language from
[Informal Systems](https://github.com/informalsystems/quint), inspired by TLA+.
It describes a system as typed state, legal transitions, and properties that can be
simulated, tested, and model-checked before or alongside implementation. FS-GG's
accepted direction is to embed Quint in readable Markdown so people review the prose
and semantic diff while tools check the precise model; it is an active migration, not
yet the default for every workspace. See the
[FS-GG Quint overview](https://github.com/FS-GG/.github/blob/main/docs/design-goals/quint-backed-workspaces.md),
[ADR-0077](https://github.com/FS-GG/.github/blob/main/docs/adr/0077-quint-first-typed-specification-authority.md),
and [implementation status](https://github.com/FS-GG/.github/blob/main/docs/design-goals/implementation-status.md).

## Quick start: ask your agent for a Todo app

FS-GG assumes you already have a coding agent with terminal access, such as
[Codex](https://openai.com/codex/) or
[Claude Code](https://docs.anthropic.com/en/docs/claude-code). You describe the
workspace you want; the agent installs the tools, uses GitHub's CLI and token
machinery, creates and secures the repository and Project, and verifies the result.

Start a session in the directory where you keep projects and ask:

```text
Set up a new FS-GG Todo workspace for me. Use my active GitHub account unless I
name a different owner. Create a public repository named todo-fsgg and a Project
named Todo, then scaffold the console template with the SDD lifecycle and no
governance. Build a small Todo application that can add, list, and complete tasks,
persists them in a local JSON file, and has tests for its core behavior. Build,
test, and run a short add/list/complete demonstration.

Follow the FS-GG agent setup guide:
https://github.com/FS-GG/.github/blob/main/docs/consumer/agent-setup.md

Ask me only for choices or browser/security confirmations you cannot make. Never
print or commit a GitHub token. When finished, show me the repository, Project,
test result, program output, and anything that still needs my attention.
```

Your agent may pause once for GitHub's browser login or to confirm who may write
to a public Project. After that, the experience should end with a compact result,
not a page of setup commands:

```text
Ready
Repository  https://github.com/you/todo-fsgg
Project     https://github.com/users/you/projects/...
Build       passed
Tests       passed
Run         Added "Try FS-GG" → listed open → completed
```

The separate [agent setup guide](https://github.com/FS-GG/.github/blob/main/docs/consumer/agent-setup.md)
owns the operational details for both Codex and Claude Code: required tools and
GitHub permissions, safe token handoff, board creation, workspace wiring, and the
human-only Project access check.

### Shape the work in plain language

Ask the agent to add a roadmap:

```text
Create docs/roadmap.md with two milestones: first preserve and test the supplied
Todo add/list/complete behavior; then document usage and add a CI build. Review it
with me, then drive it milestone by milestone through FS-GG's SDD workflow.
```

The agent uses the process skills scaffolded for its runtime. Each milestone gets
its own SDD run—charter, specify, clarify, checklist, plan, tasks, analyze,
implement, evidence, verify, and ship—and is reviewed before the next begins. The
[`fsgg-sdd` quickstart](https://github.com/FS-GG/FS.GG.SDD/blob/main/docs/quickstart.md)
is available when you want to understand the lifecycle beneath that experience.

### Turn a review into board work

```text
Review this repository at high effort. Report only reproducible, material,
actionable findings. Write the report under docs/reports/, create one well-scoped
GitHub issue per finding with acceptance criteria and FS-GG coordination metadata,
and add those issues to this workspace's board. Do not implement them yet.
```

New rows enter `Backlog`; the board workflow reconciles and triages them before
deciding what is safe to start.

### Burn down the board

```text
Burn down this workspace's wired product board. Use the FS-GG board-working skill
that was scaffolded for your runtime, and stop only when no actionable work remains.
```

The same request works in Codex and Claude Code. The board workflow refreshes the
Project, triages the backlog, runs non-overlapping issue lanes through SDD and
independent review, merges green pull requests, and records follow-up findings.
Only use it on a board whose issue and Project writers you trust.

## Workspace templates

FS.GG.Templates owns five focused workspace shapes; see its
[detailed documentation](https://github.com/FS-GG/FS.GG.Templates) for current
availability.

- **rendering** — a SkiaSharp/OpenGL desktop UI built around Elmish/MVU.
- **console** — a registry-active, production-shaped F# executable without a browser or npm lane.
- **web** — a registry-active F# ASP.NET Core server paired with a neutral TypeScript/Vite site.
- **fable-game** — a registry-active, server-authoritative F# game with a Fable/Elmish browser client.
- **fable-bindings** — a registry-active, package-producing Fable interop library over an exactly pinned JavaScript or TypeScript dependency.

## Spec-driven development

Spec-driven development (SDD) is the lifecycle that keeps the charter and
specification, implementation evidence, verification, updates, and shipping
coherent as work changes. The
[lifecycle guide](https://github.com/FS-GG/.github/blob/main/docs/consumer/lifecycle.md)
explains the full flow.

## License

MIT.
