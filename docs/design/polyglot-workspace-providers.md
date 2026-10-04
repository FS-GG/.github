# Polyglot workspace-provider implementation design

This is the implementation route for [ADR-0092](../adr/0092-descriptor-driven-polyglot-workspace-providers.md)
and [.github#3009](https://github.com/FS-GG/.github/issues/3009). The architecture choice is accepted;
provider/catalog implementation and public installed adoption remain separate. Existing producer
source supplies generic invocation, capability evaluation and package composition, but does not
establish the complete four-provider catalog contract requested by this design.

## One owner per contract

| Owner and rollout child | Existing implementation boundary | Required outcome |
|---|---|---|
| [SDD#928](https://github.com/FS-GG/FS.GG.SDD/issues/928) | `src/FS.GG.Contracts/Provider.fs` / `.fsi`, `src/FS.GG.SDD.Artifacts/LifecycleArtifacts/Config.fs` / `.fsi`, `src/FS.GG.SDD.Commands/CommandWorkflow/HandlersScaffold.fs`, scaffold provenance | Versioned neutral catalog/descriptor, typed parameters, provider-owned identifiers, tool/capability/evidence/skill metadata, generic invocation and exact effective provenance |
| [Governance#423](https://github.com/FS-GG/FS.GG.Governance/issues/423) | Config, Gates, Inheritance, ProductSurfaces, Route, Scaffold and `reference-gates/` | Stable semantic capabilities with concrete ecosystem bindings, existing organization floors and missing/stale binding refusals |
| [Templates#441](https://github.com/FS-GG/FS.GG.Templates/issues/441) | `providers/`, `templates/`, product-skill manifest, `tests/composition/`, package and release workflow | Four concrete provider packs, deterministic toolchain/dependency closures, package-only functional and operational-skill qualification |
| [.github#3010](https://github.com/FS-GG/.github/issues/3010) | `scripts/NewSddWorkspace/Program.fs`, its tests/help, registry and compatibility projections | Ref-pinned catalog discovery, generic typed prompts/preview, exact producer pins and public installed creation |

Producer designs to extend are SDD's [scaffold-provider contract](https://github.com/FS-GG/FS.GG.SDD/blob/main/specs/030-scaffold-template-provider/contracts/providers-descriptor.schema.md)
and [identifier parameter contract](https://github.com/FS-GG/FS.GG.SDD/blob/main/specs/080-scaffold-name-sanitization/contracts/provider-descriptor-identifier-parameter.md),
Governance's [capability design](https://github.com/FS-GG/FS.GG.Governance/blob/main/docs/reports/2026-06-18-233718-fsgg-governance-capability-design.md)
and [generated-product capabilities](https://github.com/FS-GG/FS.GG.Governance/blob/main/specs/058-generated-product-capabilities/spec.md),
and Templates' [composition design](https://github.com/FS-GG/FS.GG.Templates/blob/main/docs/design.md).
These earlier contracts are foundations, not evidence that their language-neutral successors have
already shipped. In particular, current descriptor contract version `2.0.0` used for project-knowledge
admission must not be mistaken for delivery of SDD#928's complete catalog semantics. SDD assigns the
compatible successor and its exact supported range; this design reserves no package/schema version.

## Catalog and typed input

The catalog exposes provider ID, display name/description, language, product shape, descriptor identity,
source/package revision and supported platforms. Provider selection and prompts are data-driven.
Parameters declare string/enum/exact-version types, requiredness, defaults, validation and readable
prompt/help. Unsupported types/contracts, duplicate IDs/parameters, invalid defaults and unknown required
fields refuse before target mutation. Offline operation uses an explicitly pinned cached artifact;
missing or stale unverified catalog data never silently falls back to a newly selected provider.

Raw display/product name, package/module identity and source-code identifier are separate values.
An awkward display name does not become a module path by applying an F# transform. Provider-owned
or declared derivation returns deterministic validated values recorded in provenance. Go requires an
explicit module path. Node package naming, Rust crate naming and code identifiers follow the selected
provider's declared rules. The generic host forwards effective values and invokes the opaque transport.

| Selected new identity | Language / shape | Provider-owned qualification |
|---|---|---|
| `typescript-cli` | TypeScript / executable CLI | Strict type checking, locked Node/package manager, ESM build, actual entry point, lint/tests/package/security evidence |
| `javascript-cli` | JavaScript / executable CLI | Native ESM, declared JSDoc/checkJs posture, locked Node/package manager, actual entry point and the same declared evidence classes |
| `rust-cli` | Rust / executable CLI | Pinned toolchain/MSRV, Cargo lock, fmt, Clippy, tests, package and declared unsafe-code policy |
| `go-cli` | Go / executable CLI | Required module path, pinned Go toolchain, formatting, vet/static analysis, tests/race policy, package and vulnerability evidence |

Node infrastructure may be shared while TypeScript and JavaScript retain distinct output contracts.
Toolchain versions and OS support come from published qualified descriptors, not generic host guesses.
The existing V2-LANG native language fixtures are reusable test inputs, not these catalog packs' public
installed acceptance and not additions to the frozen R5 cohort.

## Shared semantics, commands and skills

Quint WorkspaceModel owns the revision-bound specification and required evidence. Freeform, structured
SDD and direct Quint are authoring projections; the provider adds no alternate lifecycle. GitHub owns
native issue/PR/commit/run/merge identities. ChangeProposal dispositions and accepted model reduction
remain the [SDD#927 producer](https://github.com/FS-GG/FS.GG.SDD/issues/927) contract; creating an issue
or generating a skill never accepts a semantic change. Ambiguous meaning stays explicit.

The neutral capability catalog uses the following stable semantic IDs. Existing `build:build`,
`test:test` and `evidence:evidence` are retained; the other rows are selected successor IDs for
Governance#423 to implement and publish, not claims of current package support.

| Capability ID | Semantic obligation |
|---|---|
| `build:build` | Build the selected product components |
| `test:test` | Execute the selected tests and functional journeys |
| `lint:lint` | Enforce the declared formatting/static-analysis posture |
| `evidence:evidence` | Validate actual required evidence and provenance |
| `package:package` | Produce and verify the declared distributable |
| `security:security` | Verify the declared dependency/code security policy |
| `public-surface:public-surface` | Validate the provider's declared public API/CLI/module surface |
| `release:release` | Verify release identities, coherence and applicable publication gates |

Bindings supply exact commands/arguments, paths, tools, cost/environment limits and evidence format.
Existing `fsharp:*` identities remain supported on their compatibility route; a neutral surface
cannot silently stand in for an F#-specific check. Organization command-free floors including
`gameplay:fr-covered` and `gameplay:production-journey` retain their existing applicability and
block-on-ship meaning; the CLI packs do not fabricate gameplay obligations or weaken an applicable floor. A required executable capability without a binding refuses. Semantic
command-free obligations remain command-free. TRX/JUnit, SARIF, coverage and package evidence retain
actual execution provenance; absence or unsupported normalization remains Unknown.

Templates authors only non-obvious product operations: `node-workspace`, `typescript-project`,
`javascript-project`, `node-testing`, `rust-workspace`, `rust-safety`, `go-workspace` and `go-testing`.
Executable commands and invariants own behavior; skills explain their use. Existing SDD-owned seeded
skills, `.github`-owned Kit/Drivers and product skills form the verified manifested union in both
runtime roots. SDD owns `.fsgg/`, work/readiness and lifecycle provenance; product payloads do not write
those reserved trees. Inspect the existing [driver materializer](https://github.com/FS-GG/FS.GG.SDD/blob/main/src/FS.GG.SDD.Commands/CommandWorkflow/DriverSkills.fs)
rather than creating another skill-copy authority.

## Delivery joins and controls

SDD and Governance source preparation can proceed independently against this accepted design. They
join at compatible published contracts before Templates adopts them. Templates can prepare payloads,
toolchain locks and package-only fixtures earlier, but its release uses actual published producers.
The wizard's generic catalog reader can be prepared against the accepted producer contract; activation
and registry pins wait for exact Templates publication/readback. No board projection is a source-merge
gate and no new blanket model-checking requirement is introduced.

The once-packed Templates artifact must instantiate all four providers without sibling checkouts.
Controls exercise typed/required/enum/exact-version inputs, awkward names and module paths, real
build/lint/test/entry-point/package/security results, required evidence, provider/version refusal,
reserved-tree ownership, cleanup/no partial state, repeat determinism, provenance and skill digests.
A fifth valid fixture added only to the catalog must appear in selection/help/preview and create a
workspace without a `Program.fs` provider branch. Published byte identity and exact tag/source/feed
readbacks precede registry and wizard pin changes.

Existing provider names remain supported in a producer-declared compatibility window. Legacy F#
identifier behavior stays confined to its versioned compatibility route; new providers never silently
inherit it. Retained upgrades preview conflicts, preserve owner content and original bytes, and record
Migrated/Ambiguous/Unsupported according to SDD's existing migration contract. Before activation,
retain previous published pins on a failed qualification; after V2 authority adoption, repair forward
under ADR-0091 without reopening V1. Local-only operation remains valid and needs no GitHub board.

The [V2 roadmap](../github-substrate-v2-roadmap.md#language-independent-product-workspaces--2026-09-29)
and [board design](../coordination/2026-09-29-coordination-v2-board-design.md) retain programme sequencing.
This document implements the settled architecture deliverable; it neither closes the native issue
before protected delivery nor claims downstream catalog, package publication or installed support.
