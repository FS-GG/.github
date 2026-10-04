# ADR-0092: Descriptor-driven polyglot workspace providers

- **Status:** Accepted architecture; this record implements the maintainer decision of 2026-08-26
- **Date:** 2026-10-04
- **Decision owner:** FS-GG accountable programme owner
- **Decision:** [.github#3009 maintainer decision](https://github.com/FS-GG/.github/issues/3009#issuecomment-5430619257)
- **Implementation design:** [Polyglot workspace providers](../design/polyglot-workspace-providers.md)

## Context

The compiled workspace wizard and existing scaffold descriptors expose the first F# providers' names,
parameters and naming rules. Extending that list with language-specific branches would duplicate
provider knowledge across SDD, Templates, Governance and the wizard. The maintainer has already
accepted a descriptor-driven catalog; the remaining outcome is its organization record and linked
implementation design, not another architecture decision.

## Decision

Keep `new-sdd-workspace` and `fsgg-sdd` as F# orchestration. Keep `dotnet new` and NuGet as an allowed
opaque template transport. Neither choice constrains the generated product's source language.

A versioned catalog declares stable provider identity, source language and concrete product shape
separately. Its descriptor supplies typed prompts, defaults, enums and validation, exact toolchain
requirements, supported platforms, capability-command and evidence bindings, and product-skill
selection. The wizard discovers and renders these declarations; adding a valid provider requires no
provider-specific wizard branch. Providers derive ecosystem identifiers from the raw product name
or require explicit input, such as a Go module path. Generic SDD performs no language-specific naming
transform for the new contract.

The initial executable/CLI identities are `typescript-cli`, `javascript-cli`, `rust-cli` and `go-cli`.
These are selected catalog identities to implement, not assertions that their packages exist. Existing
`console`, `web`, `fable-game`, `fable-bindings` and other published identities remain distinct and
compatible. TypeScript uses strict checking; JavaScript uses native ESM with an explicit JSDoc/checkJs
posture. Quint types specification semantics; it does not make JavaScript source statically typed.

Every new provider uses the same Quint-backed WorkspaceModel, ChangeProposal, GitHub identities,
CI, coordination, provenance and evidence substrate. Authoring depth is a projection over that
lifecycle, not a second lifecycle mode. The [single-lifecycle producer](https://github.com/FS-GG/FS.GG.SDD/issues/927)
and [Quint backend](https://github.com/FS-GG/FS.GG.SDD/issues/924) keep their own delivered and installed
boundaries. This decision does not silently reinterpret existing lifecycle tokens or workspaces.

SDD owns the public descriptor/catalog and generic invocation/provenance contracts. Governance owns
language-neutral capability semantics and enforcement floors. Templates owns concrete catalog entries,
payloads, toolchain bindings and operational product skills. `.github` owns the wizard, shared
coordination/process skills and coherent registry adoption. The linked design assigns exact producer
paths and rollout children; no second scheduler or lifecycle authority is introduced.

Publish and independently verify SDD and Governance producers before Templates adoption; publish and
qualify the once-packed Templates artifact before wizard and registry adoption. Qualify all four
providers through installed packages without sibling source references. Existing providers remain
inspectable and invocable during the producer-declared compatibility window. Unsupported contract
versions refuse before invocation; failed creation preserves the original target and no partial SDD
state. Retained upgrades preview changes and preserve original bytes and rollback evidence.

## Consequences

Provider source, contract publication, installed qualification and receiver activation are separate
outcomes. Existing legacy providers do not acquire new semantics merely because this ADR lands.
Before activation, a consumer can retain its previous published pins. After selected generation-2
V2 activation, [ADR-0091](0091-speed-first-clean-v2-start.md) requires repair forward; package/configuration
rollback cannot restore V1 write authority. Hard-coded incremental provider branches and deferral of
the architecture until prototypes exist were explicitly rejected by the maintainer.

The [GitHub Substrate V2 roadmap](../github-substrate-v2-roadmap.md#language-independent-product-workspaces--2026-09-29)
and [Coordination V2 board](../coordination/2026-09-29-coordination-v2-board-design.md) track distinct execution
and planning boundaries. This ADR supplies design authority and acceptance examples, not publication,
provider support, a board write or completion of the downstream rollout.
