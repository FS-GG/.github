---
title: "Fable bindings, current Glutinum, complex TypeScript libraries, and Quint analysis"
category: Analysis
categoryindex: 5
index: 58
description: "Evidence-backed assessment of how far the FS-GG Fable-bindings capability can improve on current Glutinum, including a complex-library corpus and the bounded role of Quint."
date: 2026-09-03
status: complete
document-type: analysis-report
---

# Fable bindings, current Glutinum, complex TypeScript libraries, and Quint analysis

**Analysis timestamp:** 2026-09-03T10:18:29+02:00

**Scope:** documentation and analysis only. No template, skill, binding, package, provider, registry, or
runtime behavior is changed by this report.

## Executive verdict

Yes, FS-GG can improve materially on current Glutinum, but only if “improve” means a dependable
binding-production system rather than a wholesale replacement for its converter.

Current Glutinum is the better TypeScript-to-F# syntax converter. Its current architecture has a real
TypeScript AST reader, an intermediate `GlueAST`, an F#-aware AST, a printer, and 355 enabled
declaration fixtures. FS-GG's candidate generator presently emits no candidate bindings: it counts five
hazard kinds, finds repeated declaration names, and writes an empty F# shell. Calling FS-GG's current
component a superior converter would be false.

FS-GG is already the stronger end-to-end binding discipline. It pins both ecosystems, locks a 2,898-file
Babylon declaration closure, keeps generated material out of maintained source, records a mapping
ledger, checks emitted imports, runs the same Fable-emitted journey in Node and Chromium, proves the
loader side-effect with a negative control, tests a clean consumer, and gates release and activation.
Glutinum deliberately presents a much smaller interface—one declaration input and one F# output—and
does not own those lifecycle guarantees. Its current CLI surface confirms that narrow contract.

The recommended product is therefore **Glutinum-assisted, FS-GG-governed binding synthesis**:

1. retain current Glutinum as a pinned, replaceable candidate-conversion engine;
2. add a TypeScript-compiler-backed package/closure and symbol-graph front end around it;
3. add strict, machine-readable diagnostics and fail-closed coverage accounting;
4. preserve human curation for semantic compression and F# API design; and
5. retain the FS-GG compile, import, runtime, browser, clean-consumer, drift, and release gates.

On an explicit 100-point end-to-end scorecard in this report, current Glutinum scores **39**, the current
FS-GG workspace scores **59**, and the proposed combined system has a credible **92-point** target.
That is a **20-point current advantage over the converter alone** and **33 points of improvement still
available over FS-GG today**. These are transparent engineering scores, not measured productivity.
The more useful quantitative promise is narrower: after qualification, assisted generation should
plausibly remove **40–70% of first-draft manual transcription for runtime-oriented complex-library
slices**, while acceptance remains 100% compile- and runtime-proven. Type-level libraries such as Zod,
React, and XState will be much lower, provisionally **10–35%**, until the benchmark says otherwise.

Quint is useful for the lifecycle state machine and evidence invariants. It is not useful as the
TypeScript type converter and cannot prove that a generated F# signature means the same thing as an
arbitrary TypeScript type. A small model is worthwhile after the pipeline states and commands stabilize;
putting Quint into the conversion hot path is not.

## What was reviewed

### FS-GG local baseline

The local baseline was FS.GG.Templates commit
[`fd6a97cb49cb19212f0f4e60e73ef37ecfed6539`](https://github.com/FS-GG/FS.GG.Templates/commit/fd6a97cb49cb19212f0f4e60e73ef37ecfed6539),
especially:

- the [`fable-bindings` product skill](https://github.com/FS-GG/FS.GG.Templates/blob/fd6a97cb49cb19212f0f4e60e73ef37ecfed6539/template/product-skills/fable-bindings/SKILL.md);
- the [`fs-gg-fable-bindings` workspace](https://github.com/FS-GG/FS.GG.Templates/tree/fd6a97cb49cb19212f0f4e60e73ef37ecfed6539/templates/fs-gg-fable-bindings);
- its [binding plan](https://github.com/FS-GG/FS.GG.Templates/blob/fd6a97cb49cb19212f0f4e60e73ef37ecfed6539/templates/fs-gg-fable-bindings/binding-plan.json),
  [coverage record](https://github.com/FS-GG/FS.GG.Templates/blob/fd6a97cb49cb19212f0f4e60e73ef37ecfed6539/templates/fs-gg-fable-bindings/coverage-and-drift.json),
  and [declaration analysis](https://github.com/FS-GG/FS.GG.Templates/blob/fd6a97cb49cb19212f0f4e60e73ef37ecfed6539/templates/fs-gg-fable-bindings/generated-candidates/declaration-analysis.json);
- the [curated Babylon binding](https://github.com/FS-GG/FS.GG.Templates/blob/fd6a97cb49cb19212f0f4e60e73ef37ecfed6539/templates/fs-gg-fable-bindings/src/BindingsProduct/Bindings.fs);
  and
- the [toolchain qualification](https://github.com/FS-GG/FS.GG.Templates/blob/fd6a97cb49cb19212f0f4e60e73ef37ecfed6539/docs/fable-bindings-toolchain.md).

The current 67-line maintained binding exposes seven journey capabilities: null engine, scene, vector,
camera, light, box creation, and glTF registration. These are **journey capabilities**, not seven out of
some meaningful declaration-symbol denominator. The report does not divide them by 2,898 files and call
the result “coverage”; files, symbols, overloads, and useful consumer operations are different units.

### Current Glutinum baseline only

“Glutinum” in every comparison below means the active
[`glutinum-org/cli`](https://github.com/glutinum-org/cli) converter, not the older bindings collection or
the project template. The analysis used:

- published `@glutinum/cli@0.13.0`, published 2026-02-24;
- current `main` commit
  [`4ee01d2be21466424ee066afbedb1cfcb85f893b`](https://github.com/glutinum-org/cli/commit/4ee01d2be21466424ee066afbedb1cfcb85f893b),
  dated 2026-06-16; and
- TypeScript 5.9.3, Node 26.8.1, and the repository's pinned Fable 5.2.0.

Glutinum describes its three stages as TypeScript AST to `GlueAST`, `GlueAST` to F# AST, and F# AST to
text. Its tests are declaration-to-expected-F# fixtures, and the reviewed checkout contained 355 enabled
`.d.ts` cases with matching `.fsx` files (plus two disabled declaration cases). These are meaningful
strengths. The CLI itself,
however, accepts one input and optionally one output file; it exposes no package resolution, module-name,
closure, strictness, diagnostic-format, partitioning, policy, or verification option. See the
[current README](https://github.com/glutinum-org/cli/blob/4ee01d2be21466424ee066afbedb1cfcb85f893b/README.md)
and [CLI program](https://github.com/glutinum-org/cli/blob/4ee01d2be21466424ee066afbedb1cfcb85f893b/src/Glutinum.Converter.CLI/Program.fs).

The older FS-GG qualification's observations about a Glutinum template are historical and are not used
as evidence against the current converter.

## Reproducible converter experiment

### Method

The test used exact current npm package versions, direct declaration paths, a fresh temporary install,
and a locally built current Glutinum checkout. Each conversion had a 180-second limit. Exit code, wall
time, F# line count, literal `obj` occurrences, and diagnostic output were recorded. Generated text was
not counted as accepted merely because the process returned zero.

Representative reproduction:

```bash
npm install --save-exact \
  @glutinum/cli@0.13.0 \
  @babylonjs/core@9.19.0 @babylonjs/loaders@9.19.0 \
  @types/react@19.2.18 @types/vscode@1.136.0 \
  @types/three@0.185.4 monaco-editor@0.56.0 \
  xstate@5.32.6 zod@4.5.4 obsidian@1.13.1

git clone https://github.com/glutinum-org/cli.git
git -C cli checkout 4ee01d2be21466424ee066afbedb1cfcb85f893b
# Install dependencies, then build current main directly with its pinned Fable tool.
dotnet fable cli/src/Glutinum.Converter.CLI --outDir cli/dist --sourceMaps
node cli/cli.js node_modules/@babylonjs/core/Engines/nullEngine.d.ts \
  --out-file out.fs
```

The current-main results matched the published package on the cases tested. Timings are local
orientation only; semantic outcomes are the useful evidence.

### Results

| Corpus and input | Current-main outcome | Generated F# | Material observation |
|---|---:|---:|---|
| Babylon `Engines/nullEngine.d.ts` barrel | exit 0, 1.70 s | 5 lines | Re-export-only input became an empty module. |
| Babylon `Engines/nullEngine.pure.d.ts` | exit 0, 1.69 s | 1,019 lines, 2 `obj` | Useful class surface, but imports are `REPLACE_ME_WITH_MODULE_NAME` and inherited types are unresolved in the single output. |
| Babylon `scene.pure.d.ts` | exit 0 | 3,380 lines, 12 `obj` | Large candidate, not a closed compilable binding. |
| Babylon `Maths/math.vector.pure.d.ts` | exit 0 | 9,109 lines, 64 `obj` | 220 diagnostic lines, including missing `Readonly<Dimension<N>>` declarations and unsupported rest types. |
| Babylon `freeCamera.pure.d.ts` | exit 0 | 178 lines, 1 `obj` | Useful leaf candidate, unresolved external base/type graph remains. |
| React `index.d.ts` | exit 1, 0.92 s | none | Uncaught failure while processing `Exclude`. |
| VS Code `index.d.ts` | exit 1, 0.89 s | none | Uncaught TypeScript/JSDoc source-file failure. |
| XState `types.d.ts` | exit 1, 0.86 s | none | Uncaught failure on `keyof any`. |
| Obsidian `obsidian.d.ts` | exit 1, 0.97 s | none | Same JSDoc failure as the current open upstream report. |
| Monaco bundled `monaco.d.ts` | exit 0, 1.10 s | 15,901 lines, 253 `obj` | Unsupported mapped/conditional/indexed/import-equals forms; 30,900 diagnostic lines because huge parent text is repeated. |
| Zod `v4/classic/schemas.d.ts` | exit 0, 1.07 s | 2,371 lines, 95 `obj` | 563 diagnostic lines; indexed access, mapped types, `bigint`, rest tuples, and variance lose information. |
| XState package index | exit 0, 0.92 s | 35 lines, 1 `obj` | Barrel handling is partial and does not create the declaration closure. |
| Zod package index | exit 0, 0.91 s | 5 lines | Re-export-only input became an empty module. |
| Three package index | exit 0, 0.85 s | 5 lines | Re-export-only input became an empty module. |

The React, VS Code, and Obsidian failures are not just local anomalies. Current upstream has open reports
for [Obsidian](https://github.com/glutinum-org/cli/issues/217),
[VS Code](https://github.com/glutinum-org/cli/issues/152), and
[Vue mapped/keyof syntax](https://github.com/glutinum-org/cli/issues/181). The local run reproduced the
first two reported stack shapes against current main. Conversely, many leaf Babylon declarations did
produce substantial and useful text. The evidence supports “valuable proposal engine with hard edges,”
not “unusable” and not “production binding generator.”

### What the experiment proves—and does not prove

It proves that a successful process exit is not a sufficient acceptance signal, package barrels cannot
currently be assumed to pull a declaration closure, module import identity is not currently synthesized,
and complex inputs need bounded diagnostics and explicit unsupported accounting.

It does not prove that every generated line is wrong, that all 357 isolated Glutinum constructs are
unusable, or that a curated result cannot start from the output. It also does not benchmark a future
Glutinum version. The converter should remain pinned and requalified, not frozen by this report's result.

## Why complex JavaScript/TypeScript libraries are difficult

TypeScript declarations are a program about types and modules, not merely a bag of nominal interfaces.
The TypeScript handbook explicitly documents cross-declaration merging, conditional types whose result
depends on assignability and inference, and module resolution modes whose behavior depends on host and
package metadata. See the official references for
[declaration merging](https://www.typescriptlang.org/docs/handbook/declaration-merging.html),
[conditional types](https://www.typescriptlang.org/docs/handbook/2/conditional-types.html), and
[module resolution](https://www.typescriptlang.org/docs/handbook/modules/reference.html).

The corpus exposes distinct classes of pressure:

| Library family | Dominant pressure | Sensible Fable strategy |
|---|---|---|
| Babylon.js / Three.js | Thousands of modules, deep ESM paths, large class graphs, DOM/WebGL/WebGPU types, overloads, side-effect registration | Journey slices; subsystem SCCs; browser shims; static/instance companions; exact runtime import tests. |
| Monaco / VS Code / Obsidian | Huge namespace-oriented APIs, callable/event patterns, declaration/global merging, JSDoc links, `Thenable`, index access, extension-host objects | Namespace-aware symbol graph; callback/event idioms; documentation isolation; selected extension journeys; Node/host fakes only where faithful. |
| React / Vue | JSX namespaces, conditional/mapped utility types, intrinsic element maps, overloaded components, framework-specific element types | Framework-specific projection plugin; do not pretend a generic TS converter can create an idiomatic Feliz DSL. |
| XState | Deep conditional types, `keyof`, typestate-like generic inference, variadic tuples, actor/event schemas | Bind stable runtime operations and selected event/state projections; treat much compile-time inference as non-portable. |
| Zod | Branded and input/output types, chained generic schemas, indexed access, mapped utilities, variance, recursive lazy schemas | Bind runtime schema values and parse results; do not promise to reconstruct TypeScript inference in F#. |
| RxJS-style APIs | Higher-order generics, many overloads, callbacks, iterables/observables, scheduler identity | Curated overload families, callback semantics, disposal/subscription runtime tests. |

No F# representation preserves every TypeScript relationship. For example, a conditional type over an
open generic parameter is a type-level function; an erased union is a runtime calling convenience, not a
proof of equivalent inference. A useful system needs a declared projection policy with three outcomes:

- preserve the relationship faithfully;
- materialize a bounded consumer-relevant projection and document the lost generality; or
- mark it unsupported/dynamic and exclude it from typed coverage.

Silently substituting `obj` is a fourth outcome only if it is recorded as dynamic debt. It is not typed
coverage.

## Necessary architecture

### 1. Resolve packages as the target host does

Replace the declaration lock's regex traversal with a TypeScript `Program` and compiler-host resolution
under explicit `moduleResolution`, `module`, `target`, `lib`, and conditions. Resolve `types`, `typings`,
`exports`, `typesVersions`, `.d.ts`/`.d.mts`/`.d.cts`, triple-slash references, ambient libraries, bare
package edges, and side-effect imports. Record every resolution edge and why it won. Hash both selected
declarations and the served runtime export targets.

The existing 2,898-file lock is valuable, but its presence should not be mistaken for proof that regex
discovery matches TypeScript or Node semantics.

### 2. Build a semantic symbol graph before file layout

Use TypeScript checker symbols, aliases, declarations, signatures, and resolved types to construct stable
IDs. Fold declaration and namespace merging. Calculate strongly connected components and topological
layers. Partition by consumer slice and subsystem, not source filename or alphabet. Preserve source maps
back to package, declaration, span, and runtime export.

The graph should be a generated analysis artifact, not a second API authority. Maintained F# plus its
mapping ledger remains the reviewed product surface.

### 3. Make Glutinum a replaceable candidate engine

Invoke a pinned current Glutinum against graph-selected regions and retain its output and diagnostics.
Add adapters around it before forking it. The highest-value upstreamable capabilities are:

- library/API invocation rather than only text CLI use;
- multiple resolved inputs and alias/merge context;
- explicit module name/import map;
- JSON diagnostics with stable codes and source spans;
- `--strict`/`--no-obj-fallback` and a nonzero exit when conversion errors occur;
- maximum diagnostic context and output limits; and
- plugin hooks for projection policies.

Glutinum already has the hard-earned syntax knowledge. Reimplementing 355 enabled cases locally would spend the
budget on parity rather than differentiation.

### 4. Add parser-aware policy passes

Policy must explicitly cover:

- static and instance class faces, callable/constructable hybrids, and constructor overloads;
- optional property versus omitted argument versus `undefined` versus `null`;
- closed and open literal unions, numeric unions, branded/opaque values, and unique symbols;
- `this` parameters, callbacks, promise/thenable shapes, async iterables, typed arrays, and disposal;
- conditional, mapped, indexed-access, template-literal, variadic tuple, and higher-kinded patterns;
- index signatures and exact property bags;
- identifier sanitation and collision stability; and
- namespace, global, and module augmentation.

Transforms operate on AST/IR nodes. Text replacement is allowed only for leaf formatting fixes with a
structural precondition and regression fixture.

### 5. Turn coverage into an executable contract

Every selected public symbol needs a stable ledger row with declaration source, runtime export, F# name,
mapping rule, coverage class, compile assertion, runtime assertion, and owner. Add separate denominators:

- selected symbols/signatures;
- emitted typed symbols/signatures;
- dynamic pass-throughs;
- unsupported symbols/signatures;
- compile-tested members; and
- runtime-observed journey operations.

Do not publish one synthetic percentage across these dimensions. A class with 200 untested members is not
“covered” because its name was emitted.

### 6. Preserve and deepen the verification pyramid

For every accepted slice require, in order:

1. deterministic resolution and declaration/runtime locks;
2. generator fixture and diagnostic assertions;
3. F# parse/typecheck and Fable compile;
4. emitted JavaScript import inspection;
5. runtime shape, call, mutation, identity, exception, async, and disposal assertions as applicable;
6. negative controls for side effects and drift;
7. Node and real-browser or real-host journeys as applicable;
8. clean NuGet plus exact npm consumer installation; and
9. artifact/package verification before activation.

The current FS-GG workspace is unusually strong from steps 4 through 9. Those are the features to retain.

### 7. Add a durable complex-library corpus

The current Babylon forcing corpus is necessary but insufficient. Add exact, license-reviewed fixtures
that isolate the patterns above and package-level journeys for Babylon, Monaco or VS Code, React or Vue,
XState, Zod, Three, and an RxJS-style API. Full upstream files need not be vendored when compact derived
fixtures reproduce the construct; package-level qualification should still run periodically against exact
artifacts.

Initial gates should be:

- zero uncaught converter exceptions;
- zero success exit when error diagnostics exist;
- 100% resolution of the selected declaration and runtime closure;
- zero unresolved `REPLACE_ME` or equivalent placeholders in an accepted candidate;
- zero unclassified `obj` in maintained public API;
- 100% accepted-source compile rate;
- 100% selected runtime journeys and negative controls passing; and
- deterministic candidate, diagnostic, graph, lock, and evidence bytes on rerun.

## Quantified comparison

The scorecard compares end-to-end production capability, not project quality or maintainer effort. Each
row states its weight; scores are based on inspected behavior and the reproduced corpus. A converter is
naturally penalized on lifecycle rows because those are outside its scope—precisely why FS-GG should
compose with it instead of declaring a converter contest.

| Capability | Weight | Current Glutinum | Current FS-GG | Combined target |
|---|---:|---:|---:|---:|
| TypeScript construct conversion | 20 | 15 | 2 | 17 |
| Package, declaration, symbol, and runtime graph | 15 | 2 | 9 | 14 |
| F# API synthesis and semantic projection | 15 | 11 | 6 | 13 |
| Controlled curation and honest coverage | 10 | 2 | 9 | 10 |
| Compile/import/runtime/consumer verification | 15 | 3 | 14 | 15 |
| Reproducibility, drift, provenance, release | 15 | 1 | 14 | 15 |
| Diagnostics, scale, and corpus diversity | 10 | 5 | 5 | 8 |
| **Total** | **100** | **39** | **59** | **92** |

The auditable totals are **39/59/92**. Thus:

- FS-GG is **20 points ahead** as an end-to-end production system;
- Glutinum is **18 points ahead** on conversion plus synthesis (26 versus 8 of 35);
- FS-GG has **33 points of credible headroom** through composition and new graph/corpus work; and
- the combined target is **56% higher than current FS-GG** by this score, but this must not be presented
  as 56% less labor or 56% fewer defects.

The provisional transcription-reduction ranges are intentionally broad:

| Slice class | First-draft manual transcription plausibly removed | Why |
|---|---:|---|
| Flat declarations and leaf classes | 70–90% | Glutinum already handles much of the mechanical surface. |
| Runtime-oriented slices in Babylon/Three/Monaco | 40–70% | Graph assembly, imports, and curation remain substantial. |
| Namespace/host APIs such as VS Code/Obsidian | 25–50% | Current crashes, merging, host semantics, and callbacks dominate. |
| Type-level React/Vue/XState/Zod surfaces | 10–35% | Much TypeScript inference has no faithful direct F# equivalent. |

These are hypotheses to accept or reject with elapsed authoring time, reviewed diff size, classified
fallbacks, compile rate, and runtime defects across at least three independent slices per class.

## Could Quint help?

### Yes: model the binding lifecycle and release safety

The pipeline has state, concurrency, retries, authority, and safety properties—Quint's natural domain.
A small model can cover states such as `Resolved`, `Locked`, `Proposed`, `Curated`, `Compiled`,
`RuntimeProven`, `ConsumerProven`, `Drifted`, `Reviewed`, `Packed`, `Published`, and `Activated`.

Useful invariants include:

- generated candidates never overwrite maintained source;
- a changed declaration or runtime lock invalidates downstream evidence;
- dynamic and unsupported rows never contribute to typed coverage;
- accepted source contains no unresolved import placeholder;
- publication requires the same package, declaration, generator, Fable, Node, and evidence identities
  observed by verification;
- activation requires verification of the published artifact, not merely the pre-pack workspace;
- concurrent refresh and release cannot combine evidence from different revisions; and
- negative-control failure blocks release even when the positive runtime journey passes.

Quint simulation is useful for transition exploration and model checking for bounded invariants. ITF
traces can drive a model-based test adapter around a temporary workspace, injecting drift, failed
generation, stale evidence, retry, and concurrent review. The official Quint CLI provides simulation,
tests, invariant checking, ITF output, and symbolic or explicit-state verification; its `--mbt` metadata
is explicitly experimental and should be pinned and qualified. See the
[official CLI reference](https://github.com/quint-co/quint/blob/main/docs/content/docs/quint.md) and the
[Quint Connect explanation](https://github.com/quint-co/quint/blob/main/docs/content/posts/quint_connect.mdx).

This use is consistent with the repository's accepted Quint-first direction: canonical behavior in
Quint, stable integration facts in a small compiled contract, and replay against real implementation
behavior rather than a second imitation.

### No: do not use Quint as the declaration converter

Quint should not encode the TypeScript type system, F# overload resolution, Node package resolution, or
the TypeScript checker. That would create a second incomplete compiler and an enormous state space. Nor
can model checking establish that `Emit` text calls the right JavaScript export, that omission differs
from `undefined`, or that a browser object has the asserted runtime identity. Those require the real
compiler, resolver, emitted JavaScript, and runtime observations.

Quint also should not be required for every candidate regeneration. Run ordinary deterministic gates on
every change and select Quint checks when the lifecycle model, transition implementation, evidence
schema, concurrency behavior, or release rules change. This avoids adding model-checker latency to a
syntax-conversion loop.

### Recommended Quint scope

Implement one bounded model only after the non-Quint pipeline contract exists. Model workflow state and
evidence identities, generate mutation traces, and replay them against a temporary workspace driver.
Success means the model finds injected stale-evidence, mixed-revision, overwrite, and premature-release
defects, while the real adapter reproduces those outcomes. It does not mean the model certifies binding
semantics.

## Delivery roadmap

### P0 — Correct the baseline and measure it

- Update the toolchain qualification to distinguish current Glutinum CLI from historical template work.
- Pin a Glutinum commit/package and TypeScript version separately.
- Commit a license-reviewed complex-syntax fixture corpus and a package-level benchmark harness.
- Record exit status, diagnostic codes, generated/accepted lines, `obj` classification, compile rate,
  runtime rate, and author/review time.

Exit: the current report's experiment is reproducible in CI and no success-with-errors result is green.

### P1 — TypeScript-backed closure and strict diagnostics

- Replace regex closure discovery with compiler-host resolution.
- Add resolved declaration/runtime graph, stable symbol IDs, aliases, merged declarations, SCCs, and
  source spans.
- Add bounded JSON diagnostics, strict exit semantics, and candidate size limits.

Exit: Babylon, Three, Zod, XState, React, Monaco, VS Code, and Obsidian inputs terminate without an
uncaught exception; every selected edge is classified.

### P2 — Glutinum adapter and mapping policy

- Consume current Glutinum as a pinned engine.
- Supply resolved context and module mappings through the smallest viable adapter/upstream changes.
- Implement explicit projections for options, classes, overloads, literals, callbacks, nullability,
  indexers, and typed pass-throughs.
- Preserve candidate-only writes.

Exit: three independent runtime-oriented slices beat the 40% transcription-reduction floor without any
accepted unclassified `obj` or unresolved placeholder.

### P3 — Corpus expansion and release-grade proof

- Add subsystem compilation, emitted-import checks, Node/browser/host runs, clean consumers, package
  verification, deterministic diffs, and semantic coverage reports to every corpus class.
- Split very large APIs into separately versioned feature packages where that reduces consumer compile
  cost and review scope.

Exit: accepted slices compile and run at 100%; generator failures are explainable and bounded.

### P4 — Selective Quint qualification

- Specify lifecycle transitions, evidence identities, drift invalidation, release, and activation.
- Inject overwrite, stale/mixed evidence, concurrent refresh/release, ignored negative control, and
  premature activation mutations.
- Replay selected ITF traces against the real workspace lifecycle.

Exit: model and adapter catch every declared mutation; ordinary binding regeneration remains independent
of Quint startup.

## Final recommendation

Proceed, but frame the effort as a **binding workbench and assurance pipeline**, not “our own Glutinum.”
Adopt current Glutinum's conversion knowledge, contribute generic improvements upstream where possible,
and differentiate on package-scale resolution, deliberate projection, honest coverage, executable
consumer journeys, reproducibility, and safe release.

The highest-return next change is not more Babylon bindings. It is a docs-and-test-backed P0/P1 harness
that makes the current benchmark permanent and turns conversion errors, empty barrels, placeholders, and
silent `obj` fallbacks into structured red outcomes. Once that exists, the team can measure exactly how
much Glutinum removes from each curated slice and invest in mappings that repeatedly pay off.

Quint belongs one layer above that work: prove that the workbench cannot publish stale or unproven
artifacts. Leave type conversion and runtime truth to TypeScript, F#, Fable, Node, and the browser.
