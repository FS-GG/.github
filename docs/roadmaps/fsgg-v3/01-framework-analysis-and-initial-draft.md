# FS.GG v3: framework analysis and initial architecture draft

Status: design proposal, 2026-10-10. This analysis precedes the external research and final design synthesis. It does not activate v3 or change v2 delivery rules.

## Scope and method

The requested outcome is a clean rewrite after v2 finishes, with no backward compatibility, one production cutover, typed-SDD development and an optimistic working process. Repository consolidation is a means to reduce coordination overhead, not the definition of success.

The inspection covers the platform's source/project graph, public F# signatures, orchestration reducers and persistence ports, runner/Host composition, workspace descriptors, typed lifecycle, governance kernel, template composition, release tooling and current roadmap. It samples implementation boundaries; it is not a line-by-line correctness audit, performance benchmark or proof of installed support. Local snapshots differ in age. The immutable revisions below identify exactly what was inspected; they are not asserted to be the eventual v2 completion baseline.

| Repository | Inspected commit | Tracked `src/` .NET projects | Tracked workflow files |
|---|---|---:|---:|
| `.github` | `79051e56b025dadfc6fcaabd58b79e33f2854928` | 18 | 176 |
| Coordination | `e8d85f8f4bff1c99c486c15a685ba1967d9382bf` | 15 | 22 |
| SDD | `3c431e92674bad3fb23611686fee3b96116d770e` | 7 | 9 |
| Governance | `b89a1251325b6b75ad0ecfc8486663ce8a2b09be` | 85 | 7 |
| Templates | `775853887a88a3c72e726daa475a49053e84c4bd` | 1 | 23 |
| Rendering | `dc6c907f4f27ff1ad5e1557ebb31af3a74998835` | 24 | 23 |
| Game | `5d5adfa7d92620e1bb3dc553ae3c49520814e1fb` | 12 | 16 |
| Audio | `097e1febe9f4dff8ca63d7571ef3bbff79822d2d` | 9 | 10 |
| Net | `5440ea46bfc3d63e77da2cbb778150a182dc94d1` | 6 | 6 |
| FsQuint | `43a847a55e21ba75e3ee7b8310a16ade17789040` | 2 | 3 |

Counts use `git ls-files`: `.fsproj`/`.csproj` below `src/`, and `.yml`/`.yaml` below `.github/workflows/`. They include alternate build targets and internal projects; they are not package, runtime service or conceptual subsystem counts. The Templates package and wizard also live outside `src/`. Counts alone do not prove overengineering.

## Initial diagnosis

The useful architecture is already visible. Coordination has a pure decision/evolution core, a thin Akka execution boundary, a PostgreSQL journal, separate execution providers and a Host composition root. SDD already defines a modular WorkspaceModel and exact-base ChangeProposal. Descriptor-driven providers distinguish language, product shape, parameters, toolchains, capabilities and evidence. Governance already has a pure rule/check kernel. These are strong semantic foundations for v3.

The main difficulty is that users experience their composition through many independently versioned and projected surfaces. `.github` contains organization infrastructure alongside the coordination CLI, telemetry Store/Host/dashboard, wizard, skill packaging, release machinery and historical coordination behavior. SDD owns both reusable contracts and lifecycle implementations. Product definitions are distributed among Templates, Governance bindings, SDD skill materialization, wizard behavior and organization registry pins. Correct cross-repository changes can require a chain of publication and installed-consumer joins before becoming usable.

These are source-grounded structural observations. The hypothesis that consolidation will improve lead time needs measurement; this analysis makes no savings claim.

## First architecture draft, before external research

Build one platform monorepo with a small set of dependency-enforced modules:

1. A pure domain layer for identities, workflow transitions and operation outcomes.
2. A typed-SDD model layer for accepted workspace semantics and proposed changes.
3. A pipeline compiler that combines a change, project definition and policy into a finite typed execution graph.
4. An Akka.NET runtime that schedules that graph through a durable PostgreSQL operation journal.
5. Separate adapters for GitHub, agent providers, processes, artifacts and publication.
6. Versioned product packs containing scaffolding, skills, commands and conformance tests.
7. A CLI, Host and read-only operational UI over the same application API.

GitHub supplies native collaboration and delivery facts. PostgreSQL records runtime work and effects. Git contains accepted product specifications and code. GitHub Projects should be a planning projection and command intake surface, not a second scheduler. Skills explain product work; typed contracts and executable code decide scheduling and effects.

Keep rendering, simulation, audio and networking independent from this development platform at runtime. Whether their sources join a larger umbrella repository is a separate question: no product library should need the orchestrator to build or execute.

Use one clean v3 schema and one CLI contract. Do not bring across old lifecycle tokens, compatibility facades, migration state machines, historical pilot exceptions or a second event authority. Preserve v2 as an immutable historical reference. Build and qualify v3 as a complete replacement, then switch once; implementation milestones do not imply staged production adoption.

The development default is one owner, one typed change, focused checks and one PR. Derive reports from the actual run instead of asking agents to create parallel ledgers. Serialize shared irreversible effects, not every piece of development work.

This draft is deliberately provisional. External research must test the monorepo boundary, durable-workflow design, actor/store authority, product-pack model, CI selection and clean-cutover strategy before the final proposal is fixed.

## Evidence behind the diagnosis

The following links use the inspected commits, so later v2 changes do not silently change the basis of this analysis. “Consequence” is a design inference, not a measured defect rate.

| Area | Direct source evidence | Consequence for v3 |
|---|---|---|
| Repository purpose | The [organization architecture](https://github.com/FS-GG/.github/blob/79051e56b025dadfc6fcaabd58b79e33f2854928/docs/architecture.md) describes the original split from FS-Skia-UI and independent product libraries. The [coordination CLI project](https://github.com/FS-GG/.github/blob/79051e56b025dadfc6fcaabd58b79e33f2854928/src/FS.GG.Coord.Cli/FS.GG.Coord.Cli.fsproj) composes coordination, lifecycle, board operations and several telemetry projects. | The organization-default repository has become a platform product repository too. Put platform implementation in a dedicated monorepo; keep organization defaults thin. |
| Domain identity | [Orchestration types](https://github.com/FS-GG/FS.GG.Coordination/blob/e8d85f8f4bff1c99c486c15a685ba1967d9382bf/src/FS.GG.Coordination.Core/Orchestration.fs) distinguish commands, operations, attempts, sessions, generations and revisions, but WorkItemId includes GitHub repository/issue identity. | Preserve explicit identity and fencing. Introduce platform work identity with an external binding so a local change or PR does not require pretending to be an issue. |
| Durable effects | [Persistence ports](https://github.com/FS-GG/FS.GG.Coordination/blob/e8d85f8f4bff1c99c486c15a685ba1967d9382bf/src/FS.GG.Coordination.Core/OrchestrationPersistence.fs) define atomic inbox/event append, expected sequence, duplicate/conflict outcomes, recovery and candidate custody. The [PostgreSQL adapter](https://github.com/FS-GG/FS.GG.Coordination/blob/e8d85f8f4bff1c99c486c15a685ba1967d9382bf/src/FS.GG.Coordination.Orchestration.PostgreSql/README.md) makes the journal authoritative and explicitly separates external actions from SQL atomicity. | This is valuable engineering, not ceremony to discard. Make it a small reusable runtime primitive with automatic recovery. Do not introduce a second authoritative Akka persistence history. |
| Execution boundary | The [neutral execution adapter](https://github.com/FS-GG/FS.GG.Coordination/blob/e8d85f8f4bff1c99c486c15a685ba1967d9382bf/src/FS.GG.Coordination.Orchestration.Execution/README.md) separates provider lifecycle, durable launch intent, original deadlines, cancellation and terminal settlement. | Keep provider-neutral execution, explicitly advertised resume capabilities and cancellation semantics. A provider SDK must not become the development workflow authority. |
| Runtime ergonomics | The [Host contract](https://github.com/FS-GG/FS.GG.Coordination/blob/e8d85f8f4bff1c99c486c15a685ba1967d9382bf/src/FS.GG.Coordination.Orchestration.Host/README.md) includes exact admission documents and specialized recovery cases; its described native qualification is bounded, not universal deployment proof. | Replace pilot-specific recovery surfaces with typed operation inspection and reconciliation. Do not infer that every source implementation has installed acceptance. |
| Product definition | The [provider signature](https://github.com/FS-GG/FS.GG.SDD/blob/3c431e92674bad3fb23611686fee3b96116d770e/src/FS.GG.Contracts/ProviderCatalog.fsi) already models typed parameters, exact tools, capabilities, evidence and skills. [ADR 0092](../../adr/0092-descriptor-driven-polyglot-workspace-providers.md) assigns related work across four repositories. | Keep the descriptor idea; colocate descriptor, scaffold, commands, skills and conformance fixtures as a product pack. Ordinary pack changes should be atomic source changes. |
| Workspace lifecycle | [WorkspaceLifecycle.fsi](https://github.com/FS-GG/FS.GG.SDD/blob/3c431e92674bad3fb23611686fee3b96116d770e/src/FS.GG.SDD.Artifacts/TypedSpecifications/WorkspaceLifecycle.fsi) has modular WorkspaceModel, exact-base proposals, explicit HumanAcceptance and correspondence outcomes. Its ChangeProposal currently requires IssueRef. | Reuse the semantics, not old wire contracts. For development with completed v2, respect its actual acceptance requirements; remove redundant data entry through tooling, not fabricated human acceptance. |
| Proportionate verification | [TypedLifecycleV2.fsi](https://github.com/FS-GG/FS.GG.SDD/blob/3c431e92674bad3fb23611686fee3b96116d770e/src/FS.GG.SDD.Artifacts/TypedSpecifications/TypedLifecycleV2.fsi) distinguishes prose, structural, simulation, model-check and full-corpus rungs. The [single lifecycle goal](../../design-goals/single-typed-sdd-lifecycle.md) separates accepted semantics from GitHub facts. | Typed-SDD can support a fast ordinary path. It need not mean full model checking or a new artifact family for every edit. |
| Governance | The [pure kernel project](https://github.com/FS-GG/FS.GG.Governance/blob/b89a1251325b6b75ad0ecfc8486663ce8a2b09be/src/FS.GG.Governance.Kernel/FS.GG.Governance.Kernel.fsproj) separates rules, checks and evidence from I/O. The repository has many additional small build units. | Preserve pure policy evaluation; consolidate internal implementation where there is no distinct dependency, trust or consumption boundary. Do not translate 85 project files into 85 v3 modules. |
| Scaffold ownership | The [Templates design](https://github.com/FS-GG/FS.GG.Templates/blob/775853887a88a3c72e726daa475a49053e84c4bd/docs/design.md) rejects vendored runtime source and composes dependencies at scaffold time. | A monorepo must not reintroduce copied runtime implementations in templates. Product packs depend on released libraries and contain minimal starter code. |
| Generic formal tooling | [ADR 0085](../../adr/0085-fsquint-single-owner-package-boundary.md) assigns generic trace/replay identity to FsQuint and domain semantics to their owners. | Keep this separation. Do not absorb generic formal tooling into a GitHub-aware orchestration kernel. |
| Release complexity | Historical comments in the CLI project describe release-note validation failures and partial publication across feeds. | Validate every candidate package before the first external write; promote the same bytes from an immutable candidate. A monorepo removes internal publication joins, not external feed failure modes. |
| Previous clean-start decision | [ADR 0091](../../adr/0091-speed-first-clean-v2-start.md) already rejected costly migration/compatibility work for v2 and separated source, publication and adoption evidence. | Avoid recreating a migration programme for v3. Preserve the distinction between built, published and usable while automating its evidence. |

## Responsibility map and expected change locality

| User outcome | Current composition seen in sources | Proposed cohesive owner |
|---|---|---|
| Create a workspace | Wizard in `.github`; contracts/lifecycle in SDD; content in Templates; capabilities in Governance; registry/skills in organization tooling | Workspace application service plus one product pack; GitHub administration through an adapter |
| Develop a feature | Typed proposal, coordination domain, execution provider, runner, repository CI, PR delivery and board projections | Explicit compiled development plan, interpreted by one runtime |
| Add a language or project kind | Descriptor plus tooling/skill/content across producer repositories | Product pack; add an adapter only for a genuinely new execution capability |
| Inspect stalled work | Host/pilot admission and recovery commands, native GitHub state, provider state and separate telemetry | One run view separating intent, observed effect, evidence and next action |
| Release platform tools | Coherent source/package version sets and installed joins across repositories | One platform candidate manifest and automated external installation qualification |
| Change rendering or audio behavior | Independent runtime product projects | Those libraries remain independent consumers; platform receives pack/conformance updates only when needed |

These mappings are hypotheses about change locality. A completion-baseline sample of representative v2 changes should measure repositories touched, commands invoked, duplicate facts entered, manual waiting and failure recovery steps. No such timing study was performed for this proposal. The design therefore specifies observable acceptance criteria instead of claiming a percentage improvement.

## What to retain, simplify and retire

Retain pure decision functions, explicit effect identities, durable intent, exact-base semantic changes, installed-consumer testing, deterministic artifact identity and product-independent formal tooling. Their implementation can be rewritten against new contracts. A clean rewrite does not require discarding learned invariants or useful regression scenarios.

Simplify the composition root, package graph, CLI, provider onboarding, skill materialization, release candidate handling and diagnostic vocabulary. Prefer a module inside one project when separate packaging has no real consumer. Keep a process boundary where untrusted code, credentials or resource isolation require one.

Retire old lifecycle mode switches, compatibility facades, migration receivers, pilot-specific exceptions, manually synchronized status ledgers and source-tree-generated copies treated as separate authorities. Historical v2 documents remain readable in their archive; v3 does not parse them as live execution input.

## Inspection limits that affect planning

The product library inspection was structural, not a runtime audit. Agent provider implementations and every workflow were not exhaustively tested. Repository/project counts cannot identify dead code by themselves. No builds, provider launches, board mutations or publication experiments were performed during analysis. The eventual completed-v2 installed release may differ materially from these snapshots.

Consequently, the roadmap starts with a finite capability inventory from completed v2 and a fresh installed baseline. Every capability receives an explicit retain, redesign or retire decision; unexamined behavior cannot silently disappear. This is a bounded product-scope exercise, not a requirement for API compatibility, old-state import or a line-by-line port.

Continue with the [research synthesis](02-prior-art-and-research.md), [proposed architecture](../2026-10-10-fsgg-v3-clean-rewrite.md) and [delivery roadmap](03-delivery-roadmap.md).
