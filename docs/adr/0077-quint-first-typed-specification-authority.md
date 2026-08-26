# ADR-0077: Quint-first Typed SDD authority with a generated FS-GG contract

- **Status:** Accepted
- **Date:** 2026-08-25
- **Amended:** 2026-08-26 — locate model-based testing at the producer/consumer boundary; require and then
  accept the post-qualification authority amendment
- **Qualification:** Q1 accepted at FS.GG.SDD merge `60351fd0614a5c8e4bdf286c21f185196116fd69`
  and S.I.R. merge `77e56d11867a5e2e7ad99f4d61b0f0c9fff61a5f`; Q2 and the separately sequenced
  GS2-02 protocol work are authorized, but no production backend, artifact pin, provider floor, registry
  identity, or lifecycle default changes here
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

Future Typed SDD canonical sources will be authored as literate Quint specifications. The candidate
authoring container is Markdown with ordered, named `quint` blocks following Quint's
[literate-specification workflow](https://quint.sh/docs/literate). A pinned extractor deterministically
tangles those blocks into the module set consumed by Quint; generated `.qnt` files are never a separately
authored or coequal source. The embedded Quint owns behavioral semantics. Surrounding prose supplies human
review context, rationale, and navigation, but it cannot introduce an undeclared requirement, transition,
invariant, evidence obligation, or implementation binding.

The public lifecycle value remains
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

Model-based testing is a consumer-owned conformance test powered by producer-owned tooling. FS.GG.SDD
owns pinned Quint execution, ITF validation, the generic replay protocol, trace/evidence identity, and
impact selection. Each implementation repository owns its canonical domain modules, the adapter that
maps stable Quint actions to real production operations, and the projection from implementation state to
the smaller model-observable state. `.github` owns registry and CI-routing policy, not centralized product
replay. Neither the compiled contract nor a replay adapter may duplicate Quint transition semantics or
product business logic.

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

Successful Q1 qualification does not silently activate this record. It must produce an amendment to this
ADR that names the accepted literate source layout, extractor and Quint/profile identities, generated
module boundary, semantic-fingerprint rule, prose/semantics boundary, qualification receipts, and
compatibility disposition, including independent human readability findings over all three vertical
slices. Q2 producer implementation and GS2-02 protocol implementation remain blocked until that amendment
is independently accepted. A refused Q1 leaves `fsharp-specification-v1`
authoritative and requires a new decision rather than weakening the gate.

The current `fsharp-specification-v1` backend remains supported as shipped until an explicit migration.
The successor uses a new manifest schema and backend identity, provisionally `quint-specification-v1`.
Existing F# authorities remain inspectable and reproducible, are never silently reinterpreted, and migrate
only through an accepted semantic diff with rollback evidence. New Quint producer artifacts must be
published before any consumer pin, provider floor, registry description, or workspace default changes.

## Q1 qualification amendment — accepted 2026-08-26

Q1 is accepted for the exact producer and consumer objects named below. This amendment does not generalize
the verdict to a moving tool, a larger Quint language subset, another compiled-contract shape, or another
runtime adapter.

### Accepted authority and source layout

The successor backend identity is ratified as `quint-specification-v1` under manifest schema v2. Its
canonical source is one UTF-8 `work/<id>/specification.md` document. Ordered fences have the exact form
```` ```quint <plain-relative-target>.qnt += ````; targets contain no separator, absolute form, dot segment,
or duplicate module declaration. A committed manifest fixes source bytes, document order, fence order,
and targets. The extractor runs twice in fresh directories, compares every generated byte, and feeds the
result to Quint. Generated `.qnt` modules are disposable build products: they are neither committed
authority nor independently editable input.

The accepted closed language subset is `fsgg-quint-profile/1` as demonstrated by the three Q1 documents:
modules/imports, aliases/records/enums, `int`/`bool`/`str`, finite sets and lists, pure values/functions,
state variables, guarded actions, `all`/`any`, bounded nondeterministic choice, action composition in
`run`, invariants, and one temporal property. Everything else fails closed until a later profile revision
adds fixtures and compatibility judgement. In particular, raw compiler-node identities, arbitrary Quint
expression serialization, foreign execution, dynamic targets/imports, Choreo, and unbounded external data
are not part of profile 1.

The canonical semantic fingerprint is a tuple, never one ambiguous file hash: canonical literate UTF-8
bytes; ordered fence/source manifest; extracted module bytes; exact extractor, Quint, profile, evaluator,
and model-checker identities; and canonical compiled-contract bytes. The manifest and receipts SHA-256
each member and bind the tuple to the candidate commit. A semantic change to any member is a new candidate;
prose-only edits remain non-semantic only when catalogue and extracted bytes are unchanged.

Prose explains, motivates, and navigates. Behavioral or integration meaning exists only in embedded Quint
and its explicit catalogue rows. Every requirement, action, invariant, evidence obligation, implementation
binding, external subject, and verification profile has a unique stable catalogue identity. The generated
compiled contract contains those identities, source locations, declared reads/writes/subjects,
relationships, verification profiles, bounded-domain declarations, impact categories, compatibility
metadata, and digests. It contains no second expression tree and does not reinterpret Quint semantics.

### Accepted exact tool and guidance identities

- Extractor: `driusan/lmt@62fe18f2f6a6e11c158ff2b2209e1082a4fcd59c`, built with the pinned
  Go 1.24.1 archive; reviewed Linux-amd64 binary SHA-256
  `37e0b0365c2641edce40b48605471f61fa12e97c3e2376152f0e849abdc31f10`.
- Quint: 0.32.0, SHA-256
  `939b64095b706017f2f202c6f99c860c40be7c31bddc2b98557316e50f42cd7f`.
- Runtime verification: Rust evaluator 0.6.0; Eclipse Temurin 21.0.9+10; Apalache 0.56.1 with
  complete-tree receipt `3466d07f06d7ac80ee0f171a96383183cee9d91bf1b5995d897d4f15c004569f`;
  Node 26.7.0 and Ajv 8.17.1 with closure receipt
  `e14d4bfc96cce335d1d370f844294c8c6eeced38c61da0f5ae224e26f74d5007`.
- Optional authoring guidance: Apache-2.0 `quint-co/quint-llm-kit` commit
  `cc75369f741af7d490936f82002c2d28e3b3d78d`, tracked-tree receipt
  `68a11d403846de3af26759eef97f4a35eff5e71d561d41ea17d96e535c171556`.
  Its typecheck-first decomposition, seeded simulation, witnesses, and divergent-prefix explanations are
  adopted; action/coverage/correspondence advice is adapted to stable FS-GG identities; moving installers,
  runtime-specific orchestration, and Choreo for the qualified shared-state slice are rejected.

`lmt` has neither a tagged release nor a Go module declaration at the accepted commit. Q2 must package the
exact reviewed source and complete build closure or refuse it. This amendment does not permit
`go install ...@latest`, latest-Quint selection, network installation during qualification, or a silently
substituted local model-checker server.

### Qualification receipts and independent findings

The producer candidate is commit `3a0eced13305b146df2febd96698e38335cae99c`, tree
`bf5d69e62d99ba44a9b3db918af2370f4917745d`, manifest blob
`6080e16a537727a9c04fb6d1f0bcb8a05dca2dfb`, and manifest SHA-256
`0bb0a34f6e93c933b441fd34ebc0bbd521ac792b95955d49ed528cab3c014ddd`.
It is sealed by receipt head `6cf3f1f0746c817e1171cd3a7b63865c25c1e346` and accepted at exact PR head
`0a07769f16616b289772253cda5867cc64333983`, merged as
`60351fd0614a5c8e4bdf286c21f185196116fd69`. The harness qualified three literate slices through 23
positive commands and 19 independently failing mutations; all 32 SDD obligations were observed. Protected
merge run `32940713020` passed the exact merge commit.

The consumer-owned S.I.R. replay reviewed head `da6cbb45dd252e420f7991673e5781bdeb0aa52f`, merged as
`77e56d11867a5e2e7ad99f4d61b0f0c9fff61a5f`, and binds its exact response by blob
`0ab38f216176d39cd150ca89f573ac83063c035c` / SHA-256
`46c117744b867def807a6b5540effd23efd916e87fbd2c42530866bf66902e4c`. One exact three-state witness and
64 pinned generated traces totaling 576 states agreed with the real combat interpreter; five independent
mapping/implementation mutations failed with source-located first-divergence diagnostics. PR run
`32936448627` and protected-merge run `32937137259` passed.

The first independent architecture/tooling and domain/readability reviews correctly rejected incomplete
toolchain binding, shallow contract validation, generic diagnostics, missing negative controls, incomplete
catalogues, and an unmeasured guidance comparison. The repaired exact head passed fresh independent
[architecture/tooling review](https://github.com/FS-GG/FS.GG.SDD/pull/925#issuecomment-5421716329) and
[domain/readability review](https://github.com/FS-GG/FS.GG.SDD/pull/925#issuecomment-5421710997).
The [host acceptance](https://github.com/FS-GG/FS.GG.SDD/pull/925#issuecomment-5421736825) carries no
exception. The repair history is evidence that the literate surface exposed missing catalogue and test
relationships to unaided reviewers; only the repaired exact-head findings constitute acceptance.

### Compatibility and gate disposition

`typed-sdd` remains the lifecycle token, `fsharp-specification-v1` remains the production backend, and an
omitted lifecycle remains `sdd`. Existing F# authorities and P0-P4 receipts stay reproducible historical
objects and are never reinterpreted. Q2 may now implement the accepted profile, hermetic toolchain,
source-map codec, compiled contract, bindings, and generic replay protocol. The separately sequenced
GS2-02 work may implement its canonical coordination model after its own bootstrap prerequisites are met.

Q3 still owns additive backend publication and migration/rollback behavior. No consumer, GS2-01.4 pin,
provider floor, registry description, workspace UI, F# retirement, or default change may move until exact
Q3 artifacts are published and their respective downstream gates pass.

## Consequences

FS-GG reuses Quint's type/effect system, action language, nondeterminism, temporal logic, simulator, model
checkers, diagnostics, LSP, and ITF ecosystem instead of recreating them in an F# EDSL. The existing kernel
concepts—identity, provenance, evidence, canonical encoding, diff, migration, projections, and generated
bindings—remain useful after their authoring assumptions are separated from F#.

FS-GG must qualify and distribute a hermetic pinned toolchain, maintain a fail-closed adapter for each
supported Quint/profile version, and test runtime/model correspondence. Full model checking is an
impact-selected verification tier; documentation-only and unrelated changes do not start Apalache.

Real correspondence therefore requires a receiver-owned qualification slice. The Q1 producer experiment
may prove generic trace mechanics in FS.GG.SDD, but its S.I.R. claim is not accepted until a test-only
S.I.R. child replays the same fingerprinted traces through the real interpreter. The later production
adoption feature moves canonical rule authority and adds the complete native/Fable/rollback matrix; the
qualification child changes no production authority.

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
