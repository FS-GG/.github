---
title: "Design: Quint-first Typed SDD migration and feature preparation"
category: Design
categoryindex: 4
index: 27
description: "Authority, compatibility, qualification, feature sequence, documentation, and selective CI for migrating Typed SDD to canonical Quint specifications."
---

# Design: Quint-first Typed SDD migration and feature preparation

This design prepares the successor program authorized by
[ADR-0077](../adr/0077-quint-first-typed-specification-authority.md). It changes no production backend,
package version, provider floor, lifecycle default, projection, or runtime behavior. It defines the
bounded features and publish-before-adopt sequence required to make Quint canonical without turning raw
compiler IR or a generated AST into a second authority.

| Field | Value |
|---|---|
| Status | Accepted design; Q1 qualification complete; Q2 implementation authorized but not started |
| Authored | 2026-08-25 |
| Amended | 2026-08-26 — model-based testing ownership, Q1 consumer split, literate Quint qualification, and Q1 acceptance |
| Current authority | `fsharp-specification-v1` as published by Typed SDD P4 |
| Target authority | Literate Markdown with canonical embedded Quint under a versioned FS-GG Quint profile |
| Stable lifecycle token | `typed-sdd` |
| Target backend identity | `quint-specification-v1` (provisional until producer ratification) |
| Default lifecycle | Unchanged: omitted selection remains `sdd` |
| Producer | FS.GG.SDD |
| First behavioral consumer | EHotwagner/S.I.R. |
| First process consumer | Future FS.GG.Coordination / GitHub Substrate v2 |

## 1. Proven baseline

The migration starts from shipped assets rather than a blank design:

- FS.GG.SDD publishes specification identity, provenance, evidence, deterministic diagnostics, canonical
  bytes, fingerprints, semantic diff, projections, freshness, migration outcomes, and Fable sources.
- `typed-sdd` is additive across every provider/profile, with author, inspect, migrate, rollback, refresh,
  upgrade, doctor, provenance, and installed-artifact acceptance.
- S.I.R. consumes the kernel for a real rule, preserves a frozen canonical corpus, and proves native/Fable
  equality and runtime correspondence.
- The Quint experiment typechecks, runs, symbolically verifies, and mutation-tests two different models
  with pinned tool archives and ITF counterexamples.
- GitHub Substrate v2 already requires one typed observation/decision/mutation/receipt model and
  deterministic CI impact selection.

The current F# authority is less costly to replace than its name suggests: generated
`specification.fsx` embeds normalized JSON and invokes the package compiler. The enduring work is the
contract and lifecycle machinery around it, not a rich F# authoring language.

## 2. Authority and compilation boundary

```text
canonical literate Markdown
          |
          | pinned deterministic extraction of named Quint blocks
          v
generated .qnt module set (never edited or coequal)
          |
          | pinned Quint parse, resolve, typecheck
          v
versioned FS-GG Quint-profile validator
          |                         \
          |                          +--> Quint run/test/verify --> fingerprinted ITF evidence
          v
small generated FS-GG compiled contract
          |
          +--> F# / Fable bindings
          +--> Markdown and JSON projections
          +--> semantic diff and migration report
          +--> evidence, provenance, and projection receipts
          +--> deterministic CI impact index

consumer-owned literate source + generated .qnt/ITF
          |
          v
consumer replay adapter --> real implementation --> model-observable state comparison
```

The embedded Quint source owns behavioral meaning: types, pure calculations, state, actions,
nondeterministic choices, invariants, temporal properties, and executable examples. The surrounding
Markdown owns human explanation and navigation, not additional semantics. The literate source is the
authored artifact; extraction output is ephemeral or freshness-checked generated material and is never
edited independently. Quint's compiler owns its raw typed IR.

The generated FS-GG contract owns only stable integration facts:

- specification, module, requirement, action, invariant, evidence, implementation, and external-subject
  identities;
- source locations and exact source/compiler/profile identities;
- declared action reads, writes, authority and revision subjects;
- requirement, action, evidence, projection, test, and implementation relationships;
- verification profiles, bounded-domain declarations, and ITF correspondence bindings; and
- canonical ordering, digests, semantic-impact categories, and compatibility metadata.

It does not contain a parallel expression tree for arbitrary Quint semantics. An agent may read either
projection, but enforcement, compatibility, CI selection, and mutation authorization consume the stable
compiled contract.

## 3. FS-GG Quint profile

The producer qualification feature defines a closed, versioned profile before implementation:

1. supported Quint and profile versions;
2. canonical module-set discovery and import rules;
3. explicit stable IDs represented as typed data rather than compiler source IDs or declaration spelling;
4. catalogue forms for requirements, actions, invariants, evidence, implementations, and external subjects;
5. a closed action catalogue and dispatcher relationship;
6. separation of production-domain meaning from finite model-checking bounds;
7. canonical rules for ordered and semantically unordered collections;
8. allowed language features and verification modes;
9. unsupported or unstable constructs that fail closed with source-located diagnostics; and
10. the literate Markdown fence syntax, output targets, ordering, import boundary, and source-map rules;
11. deterministic extraction under an exactly pinned, content-addressed extractor with wrong-order,
    missing-block, duplicate-target, stale-output, and hand-edited-output controls; and
12. prose/docstring/projection conventions that keep document-shaped Typed SDD packages readable while
    preventing prose from becoming hidden semantic authority.

The profile prefers declarations-as-data where structural reflection is required. The compiler verifies a
catalogue against resolved/typechecked declarations; it never guesses stable identity from a Quint AST
node number.

Quint's documented candidate extractor is [`lmt`](https://quint.sh/docs/literate). Q1 must pin and review
an exact `lmt` source/artifact identity, its Go closure, license, output-path behavior, and source-location
fidelity. The documentation's moving `go install ...@latest` recipe is suitable for exploration but cannot
satisfy an FS-GG qualification receipt. If `lmt` cannot meet the hermeticity or diagnostic contract, Q1 may
qualify another bounded extractor, but it must record the incompatibility and cannot silently introduce a
home-grown Markdown grammar.

## 4. Compatibility and artifacts

The current manifest retains its exact meaning:

```text
lifecycle: typed-sdd
backend: fsharp-specification-v1
compiler: dotnet-fsi/net10.0
canonical source: work/<id>/specification.fsx
```

The successor is a distinct versioned contract:

```text
lifecycle: typed-sdd
backend: quint-specification-v1
compiler: literate-extractor/<version> + quint/<version> + fsgg-quint-profile/<version> + bundle digest
canonical source: work/<id>/specification.<ratified-literate-suffix> plus an exact declared block/module set
generated modules: ephemeral or freshness-checked; never hand-authored authority
compiled contract: readiness/<id>/typed-specification.contract.json
manifest schema: v2
```

Exact paths beyond the lifecycle/backend identities are ratified by the producer feature. These rules are
not deferred:

- v1 remains inspectable and reproducible throughout the migration window;
- commands do not infer a backend from file presence;
- upgrades never reinterpret `.fsx` as Quint;
- migration first reports `Migrated`, `Ambiguous`, or `Unsupported`;
- acceptance writes Quint authority, projections, semantic diff, migration receipt, and recoverable v1
  rollback material atomically;
- Quint becomes the default backend for new explicit `typed-sdd` work only after producer publication and
  installed-artifact acceptance;
- F# authoring retirement requires all known consumers, fixtures, provider floors, and registry descriptions
  to migrate and complete a declared soak; and
- omitted lifecycle remains `sdd` until a separate ADR changes every default-bearing surface.

## 5. Quint LLM Kit integration

The program incorporates [`quint-co/quint-llm-kit`](https://github.com/quint-co/quint-llm-kit) as an
upstream authoring and verification-guidance corpus. The preparation review used commit
`cc75369f741af7d490936f82002c2d28e3b3d78d`; Q1 must deliberately select and digest its own candidate
rather than inheriting that snapshot silently. At the reviewed revision the kit provides:

- lightweight `quint-lang`, `quint-modeling`, and `quint-execute-spec` skills usable without its Docker
  environment;
- a larger `/spec:*`, `/verify:*`, `/code:*`, and `/refactor:*` workflow;
- executable-first modeling guidance using typecheck followed by simulation;
- reachability witnesses so a vacuously constrained model does not look safe;
- trace explanation, type/message reachability, listener coverage, transition labeling, gap analysis, and
  implementation-migration workflows; and
- optional Quint LSP/knowledge MCP services and Choreo-oriented distributed-protocol patterns.

These are valuable accelerators, but their boundary differs from the FS-GG contract:

1. The kit is Apache-2.0 and reusable with attribution, but its maintainers explicitly describe it as
   internally developed, not thoroughly evaluated for general use, and supplied without suitability or
   reliability guarantees.
2. Its Docker path is Claude-oriented and installs the latest Quint. FS-GG requires a runtime-neutral skill
   path and exact Quint, kit revision, dependency, profile, and compiler identities.
3. Its modeling skill deliberately favors sampled `quint run` during iteration and reserves
   `quint verify` for explicit model-checking work. FS-GG adopts that fast inner loop but adds declared,
   impact-selected model-checking gates for production invariants and temporal properties.
4. Its implementation skill treats the Quint spec as ground truth and separates research, plan,
   implementation, and verification. FS-GG adds compiled-contract identities, evidence receipts,
   authorization boundaries, no-silent-spec-change rules, and runtime/ITF correspondence.
5. Its Choreo default for message-passing protocols is an evaluation candidate for the coordination model,
   not an automatic dependency. Q1 must compare it with plain Quint against readability, state space,
   generated-contract extraction, supply chain, and long-term ownership.

No `curl .../main | bash`, unpinned plugin install, moving Docker image, or moving prompt corpus may satisfy
an acceptance gate. Q1 records the reviewed upstream commit and content digests, runs upstream examples,
and dispositions every imported/adapted instruction. Q2 decides whether FS-GG references a pinned upstream
checkout, packages an attributed snapshot, or derives smaller FS-GG-owned skills. Q3 publishes whichever
choice is accepted through the existing skill manifest/materialization contract. Agent guidance remains
replaceable: canonical `.qnt` bytes and deterministic tool evidence decide correctness.

## 6. Model-based testing ownership and protocol

Quint is not compiled into the production application. FS-GG uses the model-based-testing pattern
described by the [Quint documentation](https://quint.sh/docs/model-based-testing): Quint produces valid
ITF executions and expected model states; an implementation-language adapter replays those actions
through the real system and compares an explicit observable-state projection after each step.

Ownership is split at the repository boundary:

| Owner | Owns | Must not own |
|---|---|---|
| FS.GG.SDD | Hermetic Quint invocation, ITF validation/decoding, stable action and observation codecs, generic replay-loop contract, trace/evidence receipts, fixtures, failure rendering, and deterministic CI impact selection | S.I.R., Governance, Rendering, or Coordination business behavior; source-project references to consumers |
| Implementing product | Canonical product `.qnt` modules at the backend-ratified stable location, action adapter, real implementation invocation, observable-state projection, normalization/quiescence rules, product witnesses, and replay tests | A second transition implementation that merely imitates Quint; a private Quint toolchain fork |
| `.github` | Registry/pin reconciliation, cross-repository ordering, selective-CI policy, and fleet evidence | Centralized execution of every product's replay suite or product-specific adapter logic |

The generic protocol is deliberately smaller than a generated application. For each trace it initializes
the implementation, decodes a stable action identity and arguments, invokes the real operation, waits only
at declared deterministic quiescence points, projects implementation state into the model's observable
state, normalizes declared representation differences, and reports the first divergence. An adapter that
reimplements the rule instead of calling production behavior is a failed control.

Every replay receipt binds at least the canonical module digest, Quint/toolchain/profile versions,
compiled-contract digest, adapter digest, implementation revision, trace digest, seed, and bounds. A small
reviewed witness/regression corpus is committed; larger sampled corpora are generated in CI and retained
only when they fail. Model checking proves properties of the specification. Replay proves sampled
correspondence with the implementation. Neither result alone proves the other.

Product-emitted trace validation is the reverse direction and follows only after forward replay is
qualified: implementation events are normalized to the same stable action/state vocabulary and Quint
decides whether the observed execution is admitted. It is useful for isolated GitHub and staging
evidence, but it is not required to make the first S.I.R. qualification decision.

Q1 is consequently split without moving product ownership into FS.GG.SDD:

1. FS.GG.SDD owns the generic protocol, hermetic experiment, toy/reference controls, contract proposal,
   and cross-domain measurement report.
2. The test-only [S.I.R. Q1 child `EHotwagner/S.I.R.#353`](https://github.com/EHotwagner/S.I.R./issues/353)
   owns the real combat-interpreter adapter and fingerprinted runtime replay. It changes no rule authority,
   runtime API, package, provider, or default.
3. Q1 accepts implementation only when both sides pass. Q4 later moves canonical S.I.R. authority and
   adds production native/Fable, frozen-corpus, package-only, migration, and rollback qualification.

The future FS.GG.Coordination repository follows the same rule: its Quint protocol and replay adapter live
with its pure model and implementation. `.github` retains the frozen v1 corpus and isolated qualification
environment but does not become the v2 model or adapter owner.

## 7. Prepared feature sequence

### Q0 — Decision and program preparation (`.github`)

Publish ADR-0077, this design, successor notices, and bounded feature issues. Preserve all P0–P4 evidence.

Exit: the decision and issue graph are merged; no implementation or live contract changed.

### Q1 — Cross-domain authoring qualification (FS.GG.SDD plus test-only `S.I.R.#353`)

Author three non-production vertical slices as literate Quint documents: a complete requirements/evidence
package, one S.I.R. rule with
runtime/ITF correspondence, and one concurrent coordination process with retry, stale observation, lost
update, double apply, ordering, deadlock, safety, and liveness controls. Measure readability, diagnostics,
size, time, dependencies, upgrade sensitivity, semantic-diff usefulness, and whether a reviewer can follow
each requirement from prose to executable declaration, property, example, and counterexample. Prove that
extraction is deterministic, source-located, and red for missing, reordered, duplicated, stale, or
independently edited generated modules. Draft the profile and contract only from demonstrated needs. Run
the same corpus through a pinned `quint-llm-kit` workflow and an
FS-GG-minimal workflow; evaluate the standalone language/modeling/execution skills, witnesses, trace
explanations, transition labels, type/listener coverage, and Choreo/plain-Quint choice. Record which pieces
are adopted, adapted, or rejected and why.

At least one independent domain reviewer and one independent architecture/tooling reviewer inspect all
three literate documents and their semantic diffs without relying on the authors' explanation. Their
findings must distinguish prose clarity, traceability to embedded Quint, counterexample readability, and
any fact that exists only in prose. An author-scored readability claim is measurement input, not acceptance.

FS.GG.SDD proves the generic replay protocol without referencing consumer source projects. The S.I.R.
child binds the exact candidate to the real combat interpreter through an initialize/apply/observe adapter,
checks the first divergent transition, and proves injected implementation or mapping defects are detected.
The child owns product fixtures and state normalization; it does not move canonical rule authority.

Exit: accept or refuse implementation only after the producer experiment and S.I.R. child agree on exact
literate source, extracted module, model, trace, adapter, and implementation fingerprints. Success amends
ADR-0077 with the accepted authoring and authority contract before Q2 starts. Refusal changes no authority
and returns to decision rather than permitting plain Quint or F# as an undeclared fallback.

**Accepted 2026-08-26.** The
[post-Q1 amendment](2026-08-26-adr-0077-q1-qualification-amendment.md) preserves ADR-0077's frozen Q0
bytes while extending its authority with the accepted source layout, profile/tool identities, fingerprint
and compiled-contract boundaries, receipts, compatibility disposition, and independent findings.
FS.GG.SDD PR #925 merged as `60351fd0614a5c8e4bdf286c21f185196116fd69`; S.I.R. PR #354 merged as
`77e56d11867a5e2e7ad99f4d61b0f0c9fff61a5f`. Q2 is unblocked. Production authority, Q3 publication,
downstream pins, provider floors, F# retirement, and the workspace-default decision remain blocked on
their named later gates.

### Q2 — Hermetic toolchain, validator, and compiled contract (FS.GG.SDD)

Qualify a content-addressed literate extractor plus Quint/Node closure and separately cached Apalache/JRE
toolchain. Implement deterministic extraction and source mapping, the pinned IR adapter, profile validation,
contract codec, canonical bytes, diagnostics, semantic diff,
projections, generated bindings, golden IR fixtures, version refusal, mutation controls, ITF decoding, and
the language-neutral replay protocol/evidence envelope. Product adapters remain consumer-owned.

Pin the accepted `quint-llm-kit` revision/content digests separately from Quint itself. Re-run its adopted
workflow fixtures on upgrades and reject an incompatible moving-kit/latest-Quint combination.

Exit: a clean consumer compiles the Q1 corpus deterministically without a source-project reference; raw
Quint IR is not a public or committed authority.

### Q3 — Typed SDD backend v2 and migration (FS.GG.SDD)

Add the new backend and manifest to author, inspect, migrate, rollback, refresh, upgrade, doctor, readiness,
and packaged skills. Package or resolve only the reviewed, attributed `quint-llm-kit` guidance selected by
Q1/Q2; do not expose an unpinned installer. Preserve v1 inspection and rollback. Publish and verify exact
producer artifacts from both feeds before adoption.

Exit: installed-artifact and negative-control suites pass; current `sdd` default is unchanged.

### Q4 — S.I.R. adoption and correspondence (S.I.R.)

Move canonical rule specification to Quint, consume generated F#/Fable bindings, and replay ITF traces
through the real interpreter by productionizing—not silently replacing—the accepted Q1 child adapter.
Preserve frozen rule/application bytes, public API, native/Fable equality, registered opaque implementation
fingerprints, package-only consumption, migration evidence, and rollback.

Exit: no F# and Quint co-authority remains; correspondence and rollback controls pass.

### Q5 — GitHub Substrate v2 protocol qualification (`.github` / FS.GG.Coordination)

Make the canonical coordination process a Quint model before GS2-02 implements its protocol surface.
Generate stable observation, decision, mutation, receipt, action, and CI-impact identities. Exercise retry,
stale authorization, reordering, partial failure, idempotency, safety, liveness, and convergence.

Exit: GS2 consumes the published producer and exact model/contract fingerprints; v1 is not extended with
the superseded F# protocol AST.

### Q6 — Provider, registry, and fleet adoption

After Q3 publication and Q4/Q5 acceptance, update provider minimums, installed-artifact matrices, registry
semantics, compatibility docs, architecture text, scaffolder labels, and examples. Templates and Rendering
retain `typed-sdd` and do not own lifecycle files.

Exit: every provider/profile preserves explicit `typed-sdd`, rejects stale floors, and consumes the
published Quint backend cleanly.

### Q7 — F# authoring retirement and default decision

After migration and soak, remove new F# authoring while retaining promised reader/rollback compatibility.
Independently decide whether omitted lifecycle moves from `sdd` to `typed-sdd`.

Exit: one canonical authoring language remains; any default change has its own ADR and fleet candidate.

## 8. Selective CI contract

| Change | Minimum checks |
|---|---|
| Markdown or README only | Documentation, projection, and link checks |
| Quint catalogue, requirements, evidence, or metadata | Typecheck, profile validation, compilation, freshness, structural tests |
| Pure calculation or action | Previous row plus named Quint tests and bounded simulation |
| Invariant, temporal property, dispatcher, or model bound | Previous row plus affected model checking and negative controls |
| Bound runtime implementation | Consumer-owned adapter tests and affected ITF/runtime correspondence; no unrelated product replay |
| Consumer replay adapter or observable-state projection | Adapter completeness, golden witnesses, mapping mutations, failing-prefix diagnostics, and affected runtime replay |
| Quint/profile/compiler/toolchain adapter | Full corpus, version controls, model checks, S.I.R. replay, native/Fable parity |
| Unrelated product surface | No Quint or Apalache startup unless the compiled impact graph reaches it |
| Adopted Quint LLM Kit guidance/pin | Skill quality, upstream fixture replay, prompt/command diff review, profile/tool version compatibility |

Agent judgement may explain a selection but does not replace the deterministic selector. Selector,
profile, compiler, or impact-graph changes fail safe to the broader affected suite.

## 9. Documentation and evidence policy

Decision and active-design documents receive successor notices. Current producer/provider manuals continue
describing the shipped F# backend until Q3/Q6 changes the published artifacts, preventing documentation
from advertising unavailable behavior.

Historical work packages, readiness receipts, generated guidance, release notes, changelog entries, and
the F* and initial Quint experiment reports are not rewritten. New work and migration receipts provide the
successor evidence chain.

## 10. Program acceptance

Quint becomes production authority only when:

- the complete requirements/evidence package is readable and projection-complete;
- stable IDs are independent of Quint compiler node IDs;
- unsupported constructs and compiler drift fail with stable diagnostics;
- the compiled contract does not encode arbitrary Quint expressions;
- S.I.R. corpus bytes and runtime behavior remain equivalent;
- the Q1 S.I.R. child replays fingerprinted ITF traces against the real interpreter without duplicating
  combat semantics, and Q4 productionizes that boundary;
- coordination safety and liveness controls are meaningful;
- the dependency closure is hermetic, licensed, cached, and reproducible;
- every adopted `quint-llm-kit` file is pinned, attributed, reviewed, fixture-tested, and compatible with
  the supported Quint/profile version;
- selective CI is narrow and fail-safe, including docs-only changes;
- v1 authorities remain inspectable and explicitly migratable; and
- publication, registry, provider, and consumer order is verified against actual artifacts.

Until those gates pass, `fsharp-specification-v1` remains production authority and no roadmap checkbox may
imply otherwise.
