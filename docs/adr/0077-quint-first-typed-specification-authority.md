# ADR-0077: Quint-first Typed SDD authority with a generated FS-GG contract

- **Status:** Accepted
- **Date:** 2026-08-25
- **Decision owners:** FS-GG/.github, FS.GG.SDD, and S.I.R. maintainers
- **Affects:** FS-GG/.github, FS-GG/FS.GG.SDD, future FS-GG consumers, and EHotwagner/S.I.R.
- **Supersedes:** [ADR-0076](0076-agent-authored-fsharp-specification-kernel.md) for future canonical authoring; its P0–P4 delivery record remains historical fact
- **Related design:** [Quint-first Typed SDD migration and feature preparation](../coordination/2026-08-25-quint-first-typed-sdd-migration-design.md)

## Context

ADR-0076 established one typed specification kernel, stable identities, deterministic normalization,
provenance, evidence, semantic diff, projections, and the additive `typed-sdd` lifecycle. FS.GG.SDD
published that substrate and S.I.R. adopted it. The resulting lifecycle is valuable, but its canonical
`specification.fsx` is primarily a generated F# wrapper around normalized JSON. It does not provide a
purpose-built language for actions, nondeterminism, temporal properties, executable examples, or model
checking.

The subsequent Quint experiment modeled both implemented S.I.R. combat behavior and a concurrent
communication design. The same sources typechecked, executed examples, verified bounded invariants, and
emitted replayable ITF counterexamples for injected defects. Quint also exposes typechecked JSON IR with
inferred type and effect maps. Raw Quint IR is large, source-ID-sensitive, compiler-owned, and explicitly
subject to language evolution, so it is not a suitable public FS-GG schema.

FS-GG still needs stable domain identities, provenance, evidence obligations, source bindings, semantic
impact, projection freshness, compatibility, and language bindings. Those are contract concerns rather
than a reason to maintain an FS-GG-authored programming language.

## Decision

Future Typed SDD canonical sources will be authored in Quint. The public lifecycle value remains
`typed-sdd`; Standard SDD (`sdd`), Freeform (`none`), and the separately retiring `spec-kit` compatibility
token retain their present meanings. This decision does not change the omitted lifecycle default.

FS.GG.SDD will define a constrained, versioned FS-GG Quint profile. A pinned Quint compiler typechecks the
canonical modules. An FS-GG adapter validates profile conventions and extracts a small, language-neutral
compiled contract containing only stable FS-GG concepts such as identities, source locations, declared
actions and subjects, reads and writes, requirements, evidence obligations, implementation bindings,
verification profiles, projection digests, and semantic-impact relationships.

The compiled contract is generated and is not a second semantic authority. It must not copy or reinterpret
arbitrary Quint expression semantics. Behavioral meaning remains in Quint and is tested with Quint's
native examples, simulation, model checking, temporal properties, and ITF traces. Generated F# and Fable
types are consumer bindings over the compiled contract, not canonical authoring surfaces.

The first qualification milestone must author three complete vertical slices before a production backend
is implemented:

1. one document-shaped Typed SDD requirements and evidence package;
2. one executable S.I.R. rule with runtime/ITF correspondence; and
3. one concurrent coordination process with safety, retry, stale-observation, ordering, and liveness
   properties.

The qualification reuses and evaluates the Apache-2.0
[`quint-co/quint-llm-kit`](https://github.com/quint-co/quint-llm-kit) as upstream authoring guidance. Its
language, modeling, verification, implementation-correspondence, witness, trace-explanation, type-coverage,
and refactoring workflows are inputs to FS-GG skills and tests. The kit is not authority, is not installed
from a moving branch, and does not override the pinned Quint/profile/compiler contract. Its own public
disclaimer says it has not been thoroughly evaluated for general use, and its default of automatically
installing the latest Quint conflicts with FS-GG reproducibility; both are explicit qualification subjects.

If the requirements package is not readable, the profile needs a second authored authority, or extraction
requires reimplementing Quint semantics, the milestone is refused and this decision is revisited before
any lifecycle authority changes.

The current `fsharp-specification-v1` backend remains supported as shipped until an explicit migration.
The successor uses a new manifest schema and backend identity, provisionally `quint-specification-v1`.
Existing F# authorities remain inspectable and reproducible, are never silently reinterpreted, and migrate
only through an accepted semantic diff with rollback evidence. New Quint producer artifacts must be
published before any consumer pin, provider floor, registry description, or workspace default changes.

## Consequences

FS-GG reuses Quint's type/effect system, action language, nondeterminism, temporal logic, simulator, model
checkers, diagnostics, LSP, and ITF ecosystem instead of recreating them in an F# EDSL. The existing kernel
concepts—identity, provenance, evidence, canonical encoding, diff, migration, projections, and generated
bindings—remain useful after their authoring assumptions are separated from F#.

FS-GG must qualify and distribute a hermetic pinned toolchain, maintain a fail-closed adapter for each
supported Quint/profile version, and test runtime/model correspondence. Full model checking is an
impact-selected verification tier; documentation-only and unrelated changes do not start Apalache.

FS-GG must also pin, review, attribute, and adapt any `quint-llm-kit` material it uses. Agent prompts and
skills remain replaceable authoring assistance: only checked `.qnt` source, compiled-contract receipts, and
verification evidence can authorize a semantic or implementation change.

P0–P4 work packages, readiness receipts, release notes, and old projections remain immutable historical
evidence. New successor work records the migration. The Typed SDD default decision remains deferred until
the Quint backend, consumers, installed artifacts, migration, failure controls, and soak period are proven.

## Alternatives considered

1. **Expose raw Quint IR as the FS-GG contract.** Rejected because it is compiler-owned, unstable, large,
   and organized around syntax rather than FS-GG domain relationships.
2. **Lower all Quint expressions into an FS-GG normalized language.** Rejected because it creates a second
   Quint compiler and a shadow behavioral authority.
3. **Keep F# and Quint as permanent coequal authoring surfaces.** Rejected because equivalent meaning could
   drift between two canonical inputs.
4. **Keep F# canonical and generate Quint only for verification.** Rejected as the target direction because
   the F# surface would have to recreate or constrain the behavior/effect/temporal facilities Quint already
   supplies. Retained as the fallback if the cross-domain qualification milestone is refused.
5. **Replace the stable compiled contract with agent interpretation of Quint output.** Rejected because CI,
   migrations, compatibility checks, and remote mutations require deterministic, versioned decisions.
