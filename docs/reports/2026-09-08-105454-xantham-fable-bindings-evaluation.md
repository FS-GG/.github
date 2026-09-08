---
title: "Xantham evaluation and Fable-bindings integration options"
category: Analysis
categoryindex: 5
index: 59
description: "Measured qualification of Xantham against the FS-GG bindings template, with candidate-generation, compiler-analysis and shared-declaration integration proposals."
date: 2026-09-08
status: complete
document-type: analysis-report
---

# Xantham evaluation and Fable-bindings integration options

**Recommendation:** qualify Xantham as an optional candidate-generation backend in the existing
Fable-bindings template. Its compiler analysis and structured loss reports are valuable. Its current
output is not ready to replace the maintained Babylon bindings or become the template's default public
API generator.

This report records research and disposable experiments only. The integration steps below are proposals;
they change no template behavior, accepted contract, dependency pin, release or roadmap sequence.

**Follow-on authorized on 2026-09-08:** implement the optional candidate workflow and make each load of
the bindings skill check current Xantham packages and source, assess relevant changes, and explain if
and how they should be integrated. The producer-owned feature plan is linked from
[Unified Roadmap section 9.8](../2026-09-07-154210-fs-gg-unified-development-roadmap.md#98-feature-parts-and-subroadmap-index).
Its implementation and publication evidence remain separate from this evaluation.

## Baseline and provenance

The evaluation inspected Xantham `master` at
[`0d785c68bbc539bb2f07ff2bc3e8750a153c8d55`][xantham], retrieved on 2026-09-08, and the live NuGet
indexes. The published CLI is **`xantham` 0.1.0-alpha.2**, whose installed version identifies source
`ce841e91500f8b4a668c6d64def61936eccda50f`. The indexes also list
`Xantham.TypeScript.Wire` 0.2.0 and `Xantham.Fable.Core` 0.1.0-alpha.1. The older README and
`docs/generator-usage.md` still name `Xantham.Cli` alpha.1; the newer site installation page correctly
names `xantham`. There was no `Xantham.Cli` package in the NuGet flat-container index at inspection.
Use exact published identities, not the older setup prose. [CLI project][cli-project],
[current installation page source][installation], [NuGet CLI index][nuget-cli].

FS.GG.Templates was inspected at
[`1cffa0603e8e5d0991ba5ec7b650fed75f748f7b`][templates], matching GitHub's live `main` when checked.
Its bindings template currently pins Fable compiler 5.13.0, Fable.Core 5.2.0, TypeScript 5.9.3 and
Babylon core/loaders 9.19.0. The library targets `netstandard2.1`.

The key existing seam is [scripts/generate-candidate.mjs][candidate-script]: it inventories declaration
hazards and writes an empty F# candidate shell. It does not translate declarations into bindings.
The workspace already contains declaration locking, a mapping ledger, maintained source, F# and Fable
compilation, emitted-import checks, Node/browser journeys, and a loader side-effect negative control.
Those are the framework in which Xantham should operate. See [ADR-0072](../adr/0072-console-and-fable-bindings-are-separate-workspace-providers.md).

## What Xantham contributes

The current architecture is **TypeScript 7 native compiler → Wire → Harvest → Resolve → Shape → Render**.
Wire is a separately packaged .NET client for the compiler's `tsc --api` protocol, using MessagePack
and a binary AST. This evaluation concerns that architecture, not the earlier JSON encoder/decoder
design described by some older material. The generator has a callable `Pipeline.run` as well as the CLI.
[Wire source][wire], [pipeline source][pipeline].

| Capability | Integration value | Boundary |
|---|---|---|
| Compiler-backed symbols, resolved types and declaration analysis | Avoid building another declaration reader from scratch | The CLI does not expose a complete target-host resolution policy; bootstrap currently supplies `lib` and `types` |
| Explicit declaration `entry`, runtime import and F# module | Fits Babylon's deep ESM imports and packages with separate public subpaths | One entry per invocation; entry selection does not bound the reachable type graph |
| Dependency groups: ship, reference, map or widen | Reuse existing browser/Fable types and define deliberate package boundaries | Unconfigured groups widen by default |
| `manifest.json` and `symbols.jsonl` | Turn conversion losses into actionable review inputs | Generator grades are not proof of compile success, semantic equivalence or runtime coverage |
| Declaration catalogs and references | Potentially share one F# `Scene`, `Vector3` or client type across separately generated entries | Ownership compatibility remains incomplete and catalog tests are currently disabled |
| F# compile goldens and Fable/Node fixture checks | Useful upstream regression discipline | Fixture runtime proof does not establish compatibility with every real npm package |

The four grades are `exact`, `ergonomic`, `widened` and `escape`; findings determine each symbol's
grade. Keep those as **generator classifications**. In particular, an `ergonomic` option projection
still needs the consumer's required distinctions between omission, `undefined` and `null` checked.
[Configuration and output documentation][usage], [finding definitions][findings].

The inspected golden directory contains 77 manifests and 83 F# files. Aggregating its committed
manifests gives 605 exact, 1,776 ergonomic, 928 widened and 270 escaped symbols: 1,198 of 3,579
graded symbols widen or escape. This is an inventory of committed upstream artifacts, not a fresh
execution of the full upstream suite or a productivity measurement.

## Local qualification results

Experiments ran in disposable directories with .NET SDK 10.0.400, Node 26.8.1, npm 12.0.2 and the
upstream lockfile's exact native compiler **TypeScript 7.1.0-dev.20260902.1**. The CLI was built from
the inspected source with zero build warnings/errors. Each generation had a 90-second timeout and
captured process status, elapsed time, output hashes and the generator's own diagnostics.

The published CLI was tested on both ANSI cases. The broader corpus used the source build. The two
builds produced identical ANSI candidate artifacts. Default settings were used except for the stated
RegExp map and explicit entry/runtime/module selections. These are bounded qualification observations,
not a tuned best-case benchmark of every configuration.

| Exact npm input | Generation | F# lines | Widened + escape / all graded symbols | F# compilation |
|---|---:|---:|---:|---|
| `ansi-regex@6.2.2`, defaults | 0.92 s, exit 0 | 50 | 1 / 2 | Not separately compiled |
| Same, explicit RegExp map | 0.97 s, exit 0 | 50 | 0 / 2 | Pass, both `netstandard2.1` and `net8.0` |
| `animejs@4.5.0` | 3.32 s, exit 0 | 5,119 | 70 / 251 (27.9%) | Pass, `net8.0` |
| `@babylonjs/core@9.19.0`, NullEngine entry | 44.15 s, exit 0 | 147,629 | 1,163 / 2,611 (44.5%) | Fail, FS1147 |
| Same, vector entry | 42.40 s, exit 0 | 141,939 | 1,070 / 2,454 (43.6%) | Fail, FS1147 |
| `zod@4.5.4`, root | 66.59 s, exit 0 | 21,110 | 1,248 / 1,946 (64.1%) | Fail, FS0883 |
| `xstate@5.32.6`, explicit declaration entry | 29.47 s, exit 0 | 8,832 | 584 / 705 (82.8%) | Fail, FS0037 |

The counts are over Xantham's graded symbols, including reached declarations. They are not percentages
of the upstream API successfully bound or percentages of our selected user journey covered. Timings
are local observations and include no authoring or review cost.

The RegExp map redirects `typescript/lib`'s `RegExp` to `System.Text.RegularExpressions.Regex`.
That removes a reported `obj` fallback. Fable 5.13.0 then compiled a consumer, and Node executed it
against the real installed ANSI package. It verified an ANSI match, a plain-text negative control,
omitted options, a generated parameter-object constructor, and the non-global regex returned by
`onlyFirst`. Emitted JavaScript imported the package's default export and passed `{ onlyFirst: true }`.

One initial consumer assertion used .NET `Regex.Matches` on that non-global regex and failed with
`Non-global RegExp`. The final consumer uses `Match` and checks the JavaScript flag directly. This is
a useful limit on the map: passing these operations does not make every .NET Regex operation valid
on every JavaScript RegExp. The initial failure is not counted as a passing test.

The minimal generated binding also compiled on `netstandard2.1`, despite upstream documentation saying
`net8.0` is required for generated static interface constructors. Therefore no blanket framework
migration is justified by this test. Qualify the actual retained constructs against the intended SDK
and consumer matrix. Larger corpus compilation used the upstream compile gate's exact browser package
pins and the support project from the inspected source; it did not establish clean consumption of the
published support NuGet package.

The two Babylon inputs were `Engines/nullEngine.d.ts` and `Maths/math.vector.d.ts`, paired with their
`.js` deep runtime imports and distinct F# modules. Even these entry choices expanded to **15,093,639**
and **15,645,125** bytes of F#. Both emitted unsuffixed numeric attribute literals `2147483648` and
`4294967295`, producing FS1147. That is the first observed blocker, not evidence that adding numeric
suffixes would make the remaining output correct. Zod failed on invalid generated names; XState failed
on duplicate type parameter `TExtendDelays`.

XState's automatic root lookup first returned exit 2 looking for a nonexistent `index.d.ts`.
Selecting `dist/declarations/src/index.d.ts` explicitly with runtime `xstate` made generation complete.
This reinforces the need to record declaration and runtime entry selection separately.

Two independent same-machine runs of the mapped ANSI case and Anime.js produced identical F#,
manifest and symbol-report bytes. Cross-machine and cross-OS reproducibility were not tested.
The wider outputs were not run through Fable or a real browser; Anime.js received F# compilation only.

Raw commands, configs, lockfiles, hashes, generated output and logs remain locally in
`/tmp/fsgg-xantham-spike-20260908`; the inspected source is in `/tmp/fsgg-xantham-eval-20260908`.
These scratch paths are temporary. The table and provenance above are the durable findings.

## Integration options

### 1. Optional candidate backend — recommended first

Extend the existing candidate command with an explicitly selected backend, initially Xantham alongside
the inventory-only path. Keep Glutinum available for comparison when qualified; avoid changing the
provider identity or introducing a second bindings template.

The proposed flow is:

1. Read the exact installed package, selected declaration entry, runtime import and reviewed mapping
   policy from binding configuration.
2. Invoke an exact Xantham/compiler pair into a new directory under `generated-candidates`, with
   time, memory and output-size limits.
3. Capture its files and findings automatically, adding our own provenance envelope: npm integrity,
   declaration-lock hash, generator/compiler hashes, config hash, platform and output hashes.
4. Reject unsuccessful conversion, missing reports, unaccounted losses in selected public signatures,
   unresolved imports, excessive output or compilation failure. Exit 0 from Xantham alone is insufficient.
5. Review and curate the useful result into maintained source, then run the template's existing
   compile, emitted-import, runtime, side-effect, drift and clean-consumer checks.

Preserve the distinction between untouched generated candidates and reviewed maintained source. A
regeneration must not overwrite `src`, advance `declaration-lock.json` or mark coverage accepted.
Failed proposals should retain diagnostic evidence without masquerading as the latest accepted result.

Likely producer touch points are the template's `scripts/generate-candidate.mjs`, binding configuration,
tool manifest, generator dependency lock, doctor checks, coverage-report adapter and
`template/product-skills/fable-bindings/SKILL.md`. These are proposed changes, not edits made here.
The normal build of maintained bindings should not run regeneration or install a native compiler just
to check an unchanged accepted API.

### 2. Compiler analysis through Wire — valuable follow-up

Evaluate `Xantham.TypeScript.Wire` as the compiler front end for the earlier proposal to replace the
template's regex declaration traversal. It could supply resolved source files, symbols, aliases and
type relations to an F# analysis tool while keeping API curation independent of the renderer.

Before replacement, compare its closure against the existing lock and the actual Node/browser
resolution conditions. The generator's current bootstrap passes `lib` and `types`, not a complete
explicit module-resolution configuration. Wire can expose more compiler options; making those options
match the target host remains our integration work. Hash declaration inputs and runtime exports
separately. A declaration catalog is not a replacement for the complete upstream lock.

This revises the [September 3 Glutinum analysis](2026-09-03-101829-fable-bindings-glutinum-quint-analysis.md):
evaluate Xantham's existing analysis capabilities before building equivalent infrastructure. Keep the
replaceable-engine architecture. No fresh Glutinum benchmark was run here, so this report makes no
claim that Xantham universally converts better than current Glutinum.

### 3. Shared declarations across modules — qualify later

Xantham's declaration catalogs can nominate canonical F# ownership and let later entries reuse those
types. This aligns closely with the template's subsystem ordering and Babylon's shared scene/math
types. The source validates package/declaration hashes, tool fingerprints, configuration and API shape.
[Declaration catalog implementation][catalog].

However, do not use it as a production ownership authority yet. Restore and run the catalog tests,
prove a producer/consumer pair with a shared parameter type, test stale-catalog rejection, and prove
both Fable compilation and real runtime calls. Browser and worker configurations with different
ambient providers need separate compatible catalog families. This evaluation inspected catalog
implementation but did not execute that qualification.

### 4. Package-specific mappings and narrow facades — selective adoption

Maintain reusable mappings for browser types, callbacks, literal unions and runtime imports where
their semantics are proven. The ANSI RegExp probe demonstrates the benefit and the need for tests.
Store mappings once per package family and make regressions visible through findings and consumer tests.

For Babylon, selecting a module is currently too broad. Prefer an upstream symbol-selection/reachability
improvement, or a deliberately small TypeScript facade whose runtime implementation and declaration
closure are both controlled and tested. A facade is extra maintained code and should be justified by
measured savings. Generating the whole package and then deleting megabytes is a poor default workflow.

For Zod and XState, target useful runtime operations and deliberate F# projections; the current probes
provide no basis for promising preservation of their TypeScript inference systems.

## Hardening needed before template adoption

**Compiler reproducibility is the first packaging issue.** The current CLI's `tsc init` writes a caret
range into a user cache and runs npm installation. Its `generate` path calls `checkCache`, which can
overwrite an explicit `XANTHAM_TSGO_EXE` with a cached compiler. The compiler protocol has no version
handshake. Merely adding an npm lock or setting the environment variable is therefore insufficient.
[CLI implementation][cli-source], [compiler pin][compiler-pin].

Prefer an upstream explicit-compiler option with validated precedence and fingerprint checking. Until
that exists, an isolated runner with a controlled cache, or a small host built against pinned generator
source and calling `Pipeline.run`, can provide a bounded experiment. Do not rely on private dotnet-tool
installation layout as a public library API. Keep its TS7 compiler installation separate from the
template's TS5.9 parser dependency; replacing the root `typescript` package would break the existing
JavaScript compiler-API usage.

**Upstream green CI has known exclusions.** Issue [#66][issue66] records declaration catalog tests
removed from the pipeline; six test attributes are commented out in the inspected file. Issue
[#68][issue68] records Solid.js ordering nondeterminism, with Linux golden/repeat checks skipped in
source. Issue [#67][issue67] records platform-dependent golden paths. These qualify the successful
[recent CI run][ci]; they are not grounds to claim that the whole project is untested.

**The schema command also needs repair.** Both installed alpha.2 and the inspected source build failed
when writing a schema through `schema -o`, with an `InvalidCastException` in the command-line binding.
The committed schema at the selected source revision is a possible temporary input, checked against
that build. Do not point generated workspaces at a moving `master` schema.

The highest-value upstream contributions would be compiler selection/version validation; restored
catalog and cross-platform determinism tests; bounded graph expansion and symbol selection; numeric
literal and identifier/type-parameter rendering fixes; and a strict diagnostic policy or reliable
machine-readable acceptance hook. No issues or messages were posted as part of this evaluation.

## Suggested delivery sequence and decision gates

| Step | Concrete result | Acceptance evidence |
|---|---|---|
| Small-package pilot | Optional backend plus exact compiler isolation, provenance and loss adapter | A real small-package journey passes; repeat output is stable; generation failure cannot change maintained source or locks |
| Second package and mapping policy | One useful Anime.js or similarly bounded slice | Selected signatures compile and execute against the real package; all losses are classified; review/curation time is measured |
| Shared-type qualification | Two entries sharing a canonical type | Catalog tests enabled, compatible type identity proved, stale inputs rejected, cross-platform reruns compared |
| Babylon evaluation | A bounded scene/math slice preserving deep imports | Output size is reviewable; F#/Fable compile; existing Node/browser and loader-negative-control journeys pass |
| Broader adoption | Backend selection documented in the producer skill and template | Fresh template instantiation and packaged clean-consumer proof, with coherent release under existing policy |

Automatic run logs should capture timings, findings, changed signatures, accepted versus generated
surface and verification results. Measure useful test execution separately from process overhead;
apply the agreed 10% bureaucracy ceiling and cumulative intervention policy if this becomes active
feature work. Do not invent manual evidence ledgers to compensate for missing tool output.

These are producer-owned integration proposals. The [Unified Roadmap](../2026-09-07-154210-fs-gg-unified-development-roadmap.md)
already treats bindings/converter improvements as producer work; this report does not create a new
v2 prerequisite. A later authorized major implementation should be planned just in time through the
agreed roadmap workflow. Quint remains useful for lifecycle/evidence invariants if that workflow grows;
it is not needed to establish whether these generated bindings compile and call the right JavaScript.

[xantham]: https://github.com/shayanhabibi/Xantham/tree/0d785c68bbc539bb2f07ff2bc3e8750a153c8d55
[templates]: https://github.com/FS-GG/FS.GG.Templates/tree/1cffa0603e8e5d0991ba5ec7b650fed75f748f7b/templates/fs-gg-fable-bindings
[candidate-script]: https://github.com/FS-GG/FS.GG.Templates/blob/1cffa0603e8e5d0991ba5ec7b650fed75f748f7b/templates/fs-gg-fable-bindings/scripts/generate-candidate.mjs
[cli-project]: https://github.com/shayanhabibi/Xantham/blob/0d785c68bbc539bb2f07ff2bc3e8750a153c8d55/src/Xantham.Cli/Xantham.Cli.fsproj
[installation]: https://github.com/shayanhabibi/Xantham/blob/0d785c68bbc539bb2f07ff2bc3e8750a153c8d55/site/content/xantham-cli/guide/installation.md
[nuget-cli]: https://api.nuget.org/v3-flatcontainer/xantham/index.json
[wire]: https://github.com/shayanhabibi/Xantham/tree/0d785c68bbc539bb2f07ff2bc3e8750a153c8d55/src/Xantham.TypeScript.Wire
[pipeline]: https://github.com/shayanhabibi/Xantham/blob/0d785c68bbc539bb2f07ff2bc3e8750a153c8d55/src/Xantham.Generator/Pipeline.fs
[usage]: https://github.com/shayanhabibi/Xantham/blob/0d785c68bbc539bb2f07ff2bc3e8750a153c8d55/site/content/xantham-cli/guide/usage.md
[findings]: https://github.com/shayanhabibi/Xantham/blob/0d785c68bbc539bb2f07ff2bc3e8750a153c8d55/src/Xantham.Generator/Findings.fs
[catalog]: https://github.com/shayanhabibi/Xantham/blob/0d785c68bbc539bb2f07ff2bc3e8750a153c8d55/src/Xantham.Generator/DeclarationCatalog.fs
[cli-source]: https://github.com/shayanhabibi/Xantham/blob/0d785c68bbc539bb2f07ff2bc3e8750a153c8d55/src/Xantham.Cli/Program.fs
[compiler-pin]: https://github.com/shayanhabibi/Xantham/blob/0d785c68bbc539bb2f07ff2bc3e8750a153c8d55/src/Xantham.Cli/Spec.fs
[issue66]: https://github.com/shayanhabibi/Xantham/issues/66
[issue67]: https://github.com/shayanhabibi/Xantham/issues/67
[issue68]: https://github.com/shayanhabibi/Xantham/issues/68
[ci]: https://github.com/shayanhabibi/Xantham/actions/runs/34191757897
