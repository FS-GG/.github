# F*-first specification and F# extraction assessment

## Bottom line

F* is usable for authoring and proving selected FS-GG/S.I.R. semantics, and its
F# extraction is useful enough to justify a bounded production experiment. It
does not remove the need for a typed, normalized AST.

The promising architecture is therefore not “F* instead of an AST.” It is:

```text
F* specifications + proofs
          |
          +--> extracted, sealed semantic library for execution
          |
          +--> normalized, digest-bound AST for every other consumer
                    (F# EDSL, documentation, schemas, replay, diffing, tools)
```

Use F* where the same pure definition should be both proved and executed. Keep
the AST as the durable interchange and introspection surface. This preserves the
split architecture's ability to run other proof engines over the corpus and
avoids forcing every representation through generated F# source.

## How the experiment went

Two materially different slices were modeled:

1. an implemented arithmetic state transition from
   `CombatRules.resolveConsequences`; and
2. route and knowledge rules from the accepted communications design, before a
   corresponding runtime implementation exists.

Both models verified without admitted axioms, unsafe assumptions, custom SMT
tactics, or solver timeouts. The communication route-floor theorem required a
normal recursive lemma; the remaining local arithmetic and monotonicity
properties were discharged from refinements and definitions.

The executable definitions extracted to ordinary F# records, discriminated
unions, functions, `bigint` values, and options. The committed smoke project
compiled the freshly extracted files and called both models successfully. Proof
lemmas erased, as desired.

The reproducible run used:

- F* `2026.08.23`, commit
  `f3dd580cd58cdc5ccc0a64e4697c8cdad5a9e208`;
- the release-bundled Z3 and extraction backend;
- the Linux x64 release artifact pinned by SHA-256 in `toolchain.json`;
- .NET SDK `8.0.419` for the optional extracted-F# smoke test.

## Where extraction is powerful

- There is one executable semantic definition instead of a theorem about a
  separately maintained implementation.
- Refinement types and proof-only values disappear at runtime, keeping the
  execution boundary comparatively small.
- Pure transition functions are easy to wrap behind an idiomatic hand-written
  F# facade and test against AST fixtures.
- A compiled library boundary shields application projects from generated
  source conventions and lets them consume verified functions normally.

For combat consequence commitment, extraction could eliminate the current
manual-equivalence gap: production code could call the extracted `resolve`
through a small adapter. For communication rules, the same definition could
serve as an executable oracle while the larger state machine is built.

## Costs and limits observed

- The current F# backend emits legacy ML-compatible syntax, including
  `#light "off"`, and requires F# 5 compatibility mode. It is not drop-in source
  for a modern F# project using the repository's normal language defaults.
- The generated route functions produced three incomplete-pattern warnings even
  though the source matches are exhaustive and F* verified them. This is a code
  quality/tooling mismatch at the extraction boundary, not a failed theorem.
- Extracted code depends on F* runtime shapes such as `Prims` and
  `FStar_Pervasives_Native`. The smoke test needs only a tiny subset, but a
  larger corpus should consume a pinned, maintained runtime package rather than
  grow ad hoc compatibility shims.
- F* naturals and integers extract to `bigint`; F# domain adapters must perform
  explicit checked conversions where product code uses fixed-width integers.
- Debugging, stack traces, API documentation, and IDE navigation are worse in
  generated code. Generated files should be treated as build artifacts, not as
  the public EDSL.
- A transcription beside an existing F# implementation does not prove the two
  agree. The extraction advantage becomes material only when production calls
  the extracted semantic core or both are generated from one authority.
- Temporal, probabilistic, distributed, and liveness properties need more than
  the small total functions demonstrated here. F* can model state machines, but
  another proof language or model checker may be more economical for some AST
  projections.

## Recommended next experiment

Keep the typed AST as the corpus authority and introduce one narrow, sealed F*
semantic component rather than converting all specifications at once:

1. define a versioned normalized AST encoding for the combat consequence rule;
2. decode it into the proved F* semantic function and extract that function;
3. expose an idiomatic F# facade with checked numeric conversions and stable
   domain types;
4. generate conformance vectors from the AST and run them against both the
   extracted component and the current S.I.R. implementation;
5. measure build latency, proof maintenance, generated-library size, and
   debugging cost through one real rule evolution.

Advance the approach if the extracted component becomes the runtime semantic
authority cleanly. If it remains only a second executable copy, prefer the
language-neutral AST plus independently selected proof backends: that retains
most of the assurance while avoiding a synchronization obligation.
