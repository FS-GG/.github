# ADR-0072: Console and Fable bindings are separate workspace providers

- **Status:** Accepted
- **Date:** 2026-08-01
- **Amends:** [ADR-0071](0071-two-web-workspace-providers-one-template-package.md)
  §1 (package identities) and §6 (release/activation). Its web and game
  architecture remains unchanged.
- **Affects:** FS.GG.Templates (producer), FS.GG.SDD (provider lifecycle),
  FS-GG/.github (`new-sdd-workspace`, registry, and Coordination board)

## Context

ADR-0071 named an unpublished `FS.GG.Web.Template` package containing two web
workspace identities. Before that package exists, two more reusable scaffold
shapes have become concrete:

- a normal F# console product with the same SDD and repository discipline as
  other generated workspaces, but no browser lane; and
- an F# library that binds a pinned TypeScript/JavaScript package for Fable
  consumers.

These are not profiles of either web identity. A console product is executable
but has no npm runtime. A bindings product publishes an F# library and tests it
against an external JavaScript runtime; it is neither an executable application
nor a server/client workspace. Hiding either distinction behind `--profile`
would repeat the provider-selection ambiguity ADR-0071 rejected.

[`EHotwagner/babylonjsBindings`](https://github.com/EHotwagner/babylonjsBindings)
is the first bindings reference. At commit
`474573cc5695012c8c266f38cd6ebf0d970dacaf`, it demonstrates useful patterns:
deep Babylon ESM imports, interface/static-companion bindings, explicit
side-effect imports, subsystem files, dynamic escape hatches, documentation,
and a runnable HelloCube sample. It also provides forcing evidence for stronger
template guarantees: the npm dependency and Fable packages currently use
ranges, the upstream declaration closure is not locked, and the repository has
no automated compile/runtime/drift test suite. The reference informs the
generic contract; Babylon-specific API choices do not become platform defaults.

## Decision

### 1. One workspace-template package, four independent identities

Before its first publication, rename ADR-0071's planned package to
`FS.GG.Workspace.Template`. FS.GG.Templates owns four independent identities:

| Provider | Template identity | Product kind |
|---|---|---|
| `console` | `fs-gg-console` | F# executable |
| `web` | `fs-gg-web` | F# server plus TypeScript/Vite website |
| `fable-game` | `fs-gg-fable-game` | F# server plus Fable/Elmish game client |
| `fable-bindings` | `fs-gg-fable-bindings` | Fable interop library for a JS/TS package |

The package shares transport, composition, and release machinery, not generated
application abstractions or dependency graphs. A generated workspace selects
exactly one identity. Splitting the package remains possible if measured release
cadence or package size later justifies it.

`new-sdd-workspace --template` therefore accepts
`rendering|console|web|fable-game|fable-bindings`. Omission remains
rendering-compatible as ADR-0071 requires. Interactive and non-interactive
flows ask only for parameters declared by the selected provider.

### 2. Console workspace contract

`fs-gg-console` generates the smallest production-shaped F# executable:

- a pinned .NET SDK, solution, locked restore, root build/test entry point, and
  one root SDD lifecycle;
- `src/<Product>` and `tests/<Product>.Tests`, with an F#-native test runner and
  deterministic smoke evidence for arguments, standard output/error, and exit
  status;
- cancellation and orderly shutdown seams suitable for a long-running command,
  without forcing a daemon/worker-host architecture; and
- packaging/publish metadata only when the selected product intends to ship as
  a tool or application.

The v1 default is plain `System.Console`. It does not impose Spectre.Console, a
generic-host dependency, a command framework, npm, or a browser test lane.
Those can be downstream choices or later evidence-backed profiles.

### 3. Fable-bindings workspace contract

`fs-gg-fable-bindings` generates a versioned F# library workspace, not an
application. Its required inputs are a binding/product name, an exact npm
package version, and a target of `browser`, `node`, or `universal`. The generated
repository records:

- the exact npm package, lockfile, selected declaration entry points and their
  transitive declaration-file hashes;
- exact .NET/Fable/generator/package-manager versions and the commands used to
  refresh the candidate surface;
- curated public F# interop under `src`, compile tests, applicable browser and/or
  Node runtime tests, a small consumer sample, and a machine-readable coverage
  and drift report; and
- a NuGet package consumable by Fable projects plus documentation of the npm
  runtime dependency that consumers must install. It does not republish the
  upstream JavaScript package.

Generation is an assisted import step, not the public API authority. The
qualified spike may use Glutinum to translate the selected `.d.ts` closure;
`ts2fable` is a legacy comparison/fallback input. Generated candidates are
reviewed and normalized into maintained Fable interop using imports, `jsNative`,
interfaces, erased unions/options, parameter-object shapes, and explicit
side-effect imports as appropriate. Unsupported TypeScript constructs must be
reported, not silently weakened to `obj` in the public surface.

The default unit of work is a narrow, useful API slice. For Babylon.js, the
first slice follows the prototype's modular imports and runnable scene path—
engine, scene, core maths, camera/light, basic mesh construction, material, and
one loader/side-effect path—rather than attempting the entire Babylon API in one
generation. Dynamic escape hatches may remain documented for unbound APIs, but
they do not count as typed coverage.

### 4. Evidence and upstream drift

A bindings workspace is green only when all applicable layers pass against the
pinned npm artifact:

1. locked .NET and npm restore;
2. F# compilation and Fable compilation of the public surface;
3. TypeScript/bundler resolution of emitted imports;
4. Node and/or real-browser runtime smoke tests that construct and call the
   upstream library; and
5. declaration-closure comparison against the recorded hashes and coverage
   report.

An upstream version change deliberately breaks the drift check until the
declaration lock, curated bindings, runtime evidence, coverage report, and
release notes are reviewed together. A generator rerun may propose a diff; it
may not overwrite maintained source or advance the lock implicitly.

### 5. Skills and lifecycle ownership

FS.GG.Templates owns a generic Fable-bindings product skill covering declaration
analysis, Fable interop mapping, generated-candidate review, runtime verification,
drift triage, and release evidence. A library-specific skill belongs with that
library's producer repository if a durable workflow emerges; Babylon knowledge
does not enter every generated bindings workspace.

SDD must prove the provider lifecycle can express a package-producing F# lane
with npm-backed compile/runtime evidence. The existing mixed-workspace work may
provide shared mechanics, but acceptance must explicitly cover a bindings
package and a no-npm console workspace rather than inferring both from a web
application.

### 6. Release and activation

ADR-0071's publish-before-flip sequence widens to the four identities. Templates
publishes one exact `FS.GG.Workspace.Template` package only after every identity
passes clean instantiation and its applicable evidence. Independent consumers
install the package and instantiate all four templates from required public read
paths. The registry and wizard activate only those exact proven identities and
pins; they never predict a future package version.

## Consequences

- `console` stays genuinely small, while still receiving the platform's
  lifecycle, locking, testing, and evidence conventions.
- `fable-bindings` makes the unusual declaration-maintenance workflow explicit
  instead of disguising it as a console library or a Fable application.
- The broader package name remains truthful without multiplying publication
  machinery before independent cadence is demonstrated.
- Binding maintainers pay an intentional review cost on upstream upgrades;
  compile success alone cannot prove JavaScript runtime compatibility.
- The Babylon prototype becomes a forcing reference and future consumer, not a
  hidden template source or a requirement to bind all of Babylon.js.

## Alternatives considered

- **Use the console template for bindings.** Rejected: executable entry points,
  no-npm assumptions, testing, packaging, and upgrade semantics are different.
- **Make bindings a `fable-game` profile.** Rejected: bindings are reusable
  libraries and can target Node or arbitrary browser packages without a game,
  server, Elmish, SignalR, or Fable.Remoting.
- **Generate the complete `.d.ts` surface and publish it unchanged.** Rejected:
  TypeScript features and runtime side effects do not map mechanically enough
  for generated text alone to be a supported F# API.
- **Create a Babylon-only template.** Rejected initially: the reusable unit is
  the binding workflow. Babylon is the first proving instance.
- **Publish a separate package for each template now.** Rejected until measured
  cadence or dependency cost justifies the additional coherent-release lanes.
