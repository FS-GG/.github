# Quint-first specification-authoring assessment

## Bottom line

Quint 0.32.0 is a credible replacement for the F# EDSL authoring surface, not
just a complementary model checker. The experiment proves that Quint source can
be parsed and typechecked into machine-readable IR with inferred types and
effects, while the same source supplies executable examples, symbolic
invariants, and replayable counterexamples.

It does **not** yet justify changing authority. These two slices cover executable
semantics but not the kernel's general requirements, provenance, evidence,
extension, projection, compatibility, or semantic-diff corpus. The next
experiment should implement a real lowering into a language-neutral normalized
`SpecificationModel` and author one complete Typed SDD package in Quint.

The promising target is:

```text
Quint source is canonical authoring authority
                   |
                   v
       pinned typed Quint IR
                   |
                   v
 versioned FS-GG semantic lowering
                   |
                   v
 language-neutral normalized AST
       |             |             |
       v             v             v
 F# bindings    projections    proof/model backends
```

If that cross-domain experiment passes, ADR-0076 should be amended and the F#
EDSL authoring surface retired. Maintaining both as canonical inputs would
recreate the duplicate-authority problem the specification kernel exists to
remove.

## How the experiment went

Two materially different slices were modeled:

1. an implemented arithmetic consequence transition; and
2. route and knowledge rules from an accepted design with no corresponding
   runtime implementation.

Both modules typechecked without warnings. Four named executable examples
passed. Apalache found no violation of either combined invariant through two
symbolic transitions. Both injected controls failed with machine-readable
one-state ITF counterexamples.

The combat source contains 32 declarations and its emitted typed-IR document
contains 386 type entries and 386 effect entries. The communication source has
34 declarations with 377 entries in each map. The JSON documents were about
208 KB and 202 KB respectively. This is useful compiler IR, but far too large,
syntax-oriented, and tool-version-specific to expose directly as the stable
FS-GG model contract.

The reproducible run used:

- Quint 0.32.0 from an npm archive pinned by SHA-512;
- Apalache 0.56.1 from a release archive pinned by SHA-256;
- Eclipse Temurin JRE 21.0.12.1+1 pinned by SHA-256; and
- Node.js 26.7.0 for the recorded run.

## Readability and authoring

For behavioral specifications, Quint is more direct than the F# EDSL concept:

- `type`, records, and variants state the domain vocabulary;
- `pure def` distinguishes calculations from state reads;
- `action` and primed assignments expose transitions;
- `val` exposes invariants; and
- `run` keeps executable examples beside the model.

The language removes arbitrary host-language closures and uses an effect system
to distinguish pure, read, update, and temporal expressions. Those are exactly
the categories FS-GG currently has to enforce through EDSL conventions and
compiler inspection.

Quint is not automatically better for every specification. Requirements,
evidence obligations, provenance, examples, explicit unknowns, documentation
structure, and extension metadata can probably be expressed as typed records,
variants, constants, and docstrings, but these examples did not test whether
that remains pleasant to read. Encoding a document-shaped corpus as awkward
model-checker data would be a regression even if it typechecks.

## Typed AST finding

There is no technical obstacle to producing a typed AST from Quint. The CLI's
`typecheck --out` result contains resolved module IR plus type and effect maps
keyed by source identities. A lowering can consume that structure and emit a
small FS-GG-owned normal form.

The stable AST should be language-neutral. “Typed” means that its schema,
variants, references, validation, canonical encoding, and migrations are
defined; it does not mean its authority must be an F# discriminated union.
Generated F# bindings can preserve exhaustive matching for current consumers.

Raw Quint IR should not become that contract:

- Quint documents that the language and IR are still evolving;
- source/compiler nodes are not FS-GG domain concepts;
- inferred maps make the output large and source-ID-sensitive; and
- FS-GG still needs stronger authority, revision, provenance, evidence,
  mutation, and projection validation than Quint supplies generically.

## What the F# EDSL can and cannot economically recreate

FS-GG can reasonably build typed records, validation, normalization,
fingerprints, semantic diff, projections, interpreters, generators, and
property/state-machine tests around an F# EDSL. Those capabilities remain
FS-GG-owned even under Quint-first authoring.

Recreating Quint's language semantics, effect analysis, nondeterminism,
temporal logic, symbolic checker integration, counterexample format, REPL, LSP,
and diagnostic ecosystem would turn the kernel into a programming-language and
formal-tools project. The experiment found no FS-GG-specific benefit that
justifies owning that entire stack.

## Costs and limits observed

- Quint does not extract production F# semantics. A lowering or model-based
  adapter is required; otherwise the model and runtime remain separate copies.
- Recursive types and functions are unavailable. The communication proof had
  to become a bounded list/corpus check, weaker than F*'s arbitrary-depth lemma.
- The npm archive is pinned, but its ranged transitive dependencies are not
  hermetic without an additional lock or bundled artifact.
- Quint's typed IR is easy to consume as JSON but is compiler-owned and carries
  much more syntax detail than the desired domain AST.
- Each symbolic verification starts a local Apalache server and costs roughly
  three seconds for these tiny models, before npm/cache setup.
- Apalache 0.56.1 emitted a protobuf generated-code denial-of-service warning
  under the pinned current Java runtime. The local, short-lived checker boundary
  limits exposure in this experiment, but production qualification must resolve
  or explicitly accept it rather than suppressing the warning.
- The examples establish safety invariants, not meaningful temporal liveness.
  A coordination protocol must exercise `eventually`, fairness assumptions,
  retries, and dropped/reordered events before selection.

## Recommendation

Do not expand the F# authoring EDSL merely to reproduce facilities Quint already
provides. Also do not immediately install Quint beside it as a permanent second
authority.

Run one replacement-oriented milestone with these exit criteria:

1. define a versioned, language-neutral FS-GG `SpecificationModel` encoding;
2. lower typed Quint IR into it with source locations and deterministic
   canonical bytes;
3. author one complete requirements/evidence package, one executable S.I.R.
   rule, and one concurrent coordination protocol;
4. generate the current Markdown, schema, semantic diff, and F# consumer API;
5. replay ITF traces against one real F# interpreter;
6. prove equivalent Quint forms normalize identically and unsupported Quint
   constructs fail with useful diagnostics;
7. measure agent/human readability, evolution cost, CI time, dependency size,
   and upgrade churn; and
8. exercise safety and liveness plus injected lost-update, stale-authorize,
   double-apply, and deadlock controls.

If all three domains pass, select Quint as canonical source, amend ADR-0076, and
delete the F# EDSL authoring path in the same migration program. If the general
Typed SDD package is unnatural or the lowering becomes a second compiler of
Quint semantics, retain the language-neutral AST and the existing F# surface,
using Quint only as a generated bounded-verification target.
