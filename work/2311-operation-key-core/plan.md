---
schemaVersion: 1
workId: 2311-operation-key-core
title: Operation Key And Closed Vocabulary In The Pure Core
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2311-operation-key-core/spec.md
sourceClarifications: work/2311-operation-key-core/clarifications.md
sourceChecklist: work/2311-operation-key-core/checklist.md
publicOrToolFacingImpact: true
---

# Operation Key And Closed Vocabulary In The Pure Core Plan

Prose status: planned

## Source Snapshot
- spec: work/2311-operation-key-core/spec.md sha256:5c56c5e80f3165c32e8f7b18a8abe47ae08def5973b533a9b75722c70dec5e65 schemaVersion:1
- clarifications: work/2311-operation-key-core/clarifications.md sha256:2652aa35e3c5979381bc62ef6862c81998ff079c62c9676f9c7439f231cc8193 schemaVersion:1
- checklist: work/2311-operation-key-core/checklist.md sha256:2161320b3e4e99f69c77aaab135dd743145376396d7ec814aa69f714e8af1eec schemaVersion:1

## Plan Scope
- Work item 2311-operation-key-core is planned from the current specification, clarification, and checklist facts.
- Requirement count: 6.
- Clarification decision count: 6.
- Checklist result count: 7.
- Two source files are added (`src/FS.GG.Coord.Core/Operation.fsi`, `src/FS.GG.Coord.Core/Operation.fs`), one test file is added (`tests/FS.GG.Coord.Core.Tests/OpKeyTests.fs`), and the two explicit `<Compile Include=…/>` lists gain one entry each. No existing source file is edited.

## Plan Decisions

- PD-001 [AC-006] [FR-001] complete: **Shape and placement.** `Operation.fsi`/`Operation.fs` declare `namespace FS.GG.Coord` and `module Operation`, matching every sibling in this project. They are inserted into `FS.GG.Coord.Core.fsproj`'s compile list **immediately after `RepoScope`** and before `RegistryPredicate`, because the module opens no sibling module and must not become an ordering constraint on anything already there; the surrounding comments in that file document ordering as load-bearing, so a new entry states its own reason. The signature file carries, for every exported type and value, the invariant it holds and which of the design's two questions it answers — exclusion (answered by the *subject*, §4.1) or idempotence (answered by the *opkey*, §4.3).
- PD-002 [AC-005] [FR-002] complete: **The vocabulary is a union, and closedness is the compiler's.** `type Op = Merge | Dispatch of eventType: string | Publish of package: string`. There is no `Other of string`, no `Unknown`, and no `parse: string -> Op`; the only direction across the string boundary is outward (`wire: Op -> string`, spelling `merge`, `dispatch:<event-type>`, `publish:<package>` exactly as §3's table gives them). `wire` matches without a wildcard, so FS0025 is an error and a fourth case breaks the build at every consumer that does not handle it. The two projects reach that by slightly different routes, stated exactly because an earlier wording got it wrong: `src/FS.GG.Coord.Core` sets `TreatWarningsAsErrors=true` and then an EMPTY `WarningsNotAsErrors`, which demotes nothing, while `tests/FS.GG.Coord.Core.Tests` sets `TreatWarningsAsErrors=true` and NO demotion list at all. Neither demotes FS0025, and it is measured at both consumers rather than read off the project files.
- PD-003 [AC-001] [AC-002] [AC-003] [AC-004] [FR-003] complete: **Composition, and why its injectivity is structural.** `preimage` renders `[ item; generation; receiver; wire op ] |> String.concat "\n"`, and `compose` is `preimage >> Result.map (hex >> OpKey)` where `hex` is SHA-256, `Convert.ToHexString`, lowercased — the same shape the seven existing private copies in this project use (DEC-001). Injectivity is not sampled, and it needs one clause **per stage** of `sha256(UTF8(concat …))`: validation removes the separator from the domain so `String.concat "\n"` is injective, and it removes unpaired surrogates so `Encoding.UTF8.GetBytes` is injective (its replacement fallback collapses every lone surrogate to one byte sequence). The tests assert distinctness of the **pre-images** as the load-bearing claim and of the digests as its consequence, and each clause has its own mutation. The encoder clause was added in Repair 1 after independent review found the first head's guarantee false as stated.
- PD-004 [AC-007] [FR-004] complete: **Purity is asserted over the compiled graph.** The test reads `typeof<Operation.OpKey>.Assembly.GetReferencedAssemblies()` and requires no referenced simple name to begin with `FS.GG.` or to contain `Http`, `Octokit`, `GitHub`, or `WebSocket`. The `FS.GG.` prefix ban is rule-shaped rather than name-shaped so a future sibling assembly is excluded without editing the test. The test states in its own comment the boundary it cannot cross: `FS.GG.Coord.GitHub` references `FS.GG.Coord.Core`, so the reverse edge is a compile-time circularity and this assertion's real subject is a directly pulled-in transport.
- PD-005 [AC-008] [FR-005] complete: **Gate-inversion evidence is produced at authoring time, not at review time.** Five mutations, each applied alone, built, run, reverted, with the exact command and observed red recorded on the pull request: (a) replace the composed digest with a constant, (b) delete one arm of `wire`, (c) drop the control-character refusal, (d) add a real `System.Net.Http.HttpClient` reference to `Operation.fs`, (e) drop the unpaired-surrogate refusal. Each multi-leg assertion evaluates every leg BEFORE comparing, because xUnit stops at the first failing assert and an inverted gate then reports only its first leg — that is how the first head's mutation (c) left its decisive leg unmeasured, and the shape is fixed rather than the symptom.
- PD-006 [AC-009] [FR-006] complete: **Totality by typed refusal.** `compose` returns `Result<OpKey, Refusal list>`; there is no exception path and no silent normalization. The refusals are: blank component; control character in a component (which is what keeps the separator out of the domain); `item` not matching `owner/repo#N`; `receiver` not matching `owner/repo`; `generation` not a non-empty run of decimal digits, which refuses the engine's `released` sentinel by rule rather than by special case. Every refusal names the component it is about, so a caller can report which one it was without re-deriving it.
- PD-007 [DEC-007] acceptedDeferral: Accepted deferral DEC-007 remains visible to task generation — consolidating the SHA-256-hex primitive across the seven `FS.GG.Coord.Core` modules that each carry a private copy is out of this touch-set and routes to the board analyst as a finding packet.
- PD-008 [CR-007] acceptedDeferral: Accepted deferral CR-007 remains visible to task generation.

## Contract Impact
- PC-001 [PD-001] command report: No `fsgg-coord` verb, flag, help entry, render name, result name, or exit code moves in this slice, and no wire schema gains or loses a field — `Operation` has no caller yet, so nothing the engine emits today can change shape. The only tool-facing artifacts this work produces are its own SDD lifecycle documents and command-report JSON, which are additive by construction.
- PC-002 [PD-001] library surface: `FS.GG.Coord.Core` gains one public module. It is additive — no existing type, value, or signature moves — and the assembly is `IsPackable=false`, consumed in-repo by `FS.GG.Coord.Cli` only. `src/FS.GG.Coord.Core` is not a `kit:` source in `registry/repos.yml`, so no kit payload changes and this slice implies no coherent-set version bump on its own.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: `dotnet build src/FS.GG.Coord.Core -c Release` is green with `TreatWarningsAsErrors` in force, establishing that the new `.fsi` and `.fs` agree and that no incomplete match survives; the compile-list edits are proved load-bearing by the fact that an unlisted file in these two projects is not compiled at all (neither project uses globs).
- VO-002 [PD-003] [PD-006] semanticTest: `dotnet test tests/FS.GG.Coord.Core.Tests` green, with the new `OpKeyTests` covering composition determinism, per-component distinctness over the whole varied set, separator-injection refusal, and each typed refusal.
- VO-003 [PD-005] mutationEvidence: Each of the five mutations in PD-005 applied alone, built, run, and reverted, with the observed red recorded verbatim and the source confirmed byte-identical afterwards.
- VO-004 [PD-004] semanticTest: The reference-graph assertion runs against the compiled `FS.GG.Coord.Core` assembly, and its inversion (a real `HttpClient` reference) is observed red.
- VO-005 [PD-001] buildGate: `dotnet build src/FS.GG.Coord.Cli -c Release` green, proving the new module compiles into the consumer that actually ships, not only into its own project.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] diagnoseOnly: There is nothing to migrate and nothing to roll forward. `Operation` is new, unreferenced, and emits no persisted value anywhere — no marker, no receipt, no snapshot field — so no already-written bytes can be reinterpreted by it and no consumer can observe a change. A revert of this commit restores the previous tree exactly.
- PM-002 [PC-002] additiveOnly: No migration. Nothing consumes `Operation` yet — slices 2–6 are its first callers — so there is no existing behaviour to preserve, no wire format already written, and no rollback beyond reverting the commit.

## Generated View Impact
- GV-001 [PD-001] workModel: `readiness/2311-operation-key-core/work-model.json` and `analysis.json` are the only generated views this work touches, and they are regenerated by the lifecycle commands themselves rather than hand-edited. No projection outside `readiness/` is affected: this slice adds no verb, no flag, and no registry row, so the repository's generated agent-guidance and skill projections read exactly what they read before.

## Accepted Deferrals
- DEC-007 acceptedDeferral: The SHA-256-hex primitive stays duplicated — this slice adds the eighth per-module private copy in `FS.GG.Coord.Core` rather than consolidating the seven that exist. Accepted because the consolidation's subject is seven modules that are not in this item's declared `Paths:`, one of which (`Delivery.fs`) sits in the engine's delivery decision path; a pure, first-in-order slice must not edit it. What is *not* deferred is the composition rule this slice owns, which has exactly one home. The observation routes to the board analyst as a `fsgg:finding-packet` for a filing verdict, so it is recorded on the board rather than only here.
- CR-007 acceptedDeferral: The checklist's carry of DEC-007 is accepted unchanged for the same reason and with the same disposition; no requirement in this spec depends on the consolidation, so nothing in FR-001..FR-006 is weakened by deferring it.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.
- The shared checkout `/home/developer/projects/.github` went two commits stale under the engine's own source trees during this work; the tier-2a repair in `pnext-item` §1 (rebase this worktree, `dotnet build src/FS.GG.Coord.Cli -c Release`) was applied here and the shared-checkout repair is reported to the host. Recorded because a later staleness refusal in this item will name **this worktree**, not the shared checkout.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2311-operation-key-core`.
