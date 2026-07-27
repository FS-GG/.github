# Polyglot Web Product Architecture and Implementation Design

**Date:** 2026-07-27 16:35:09 +02:00
**Status:** Proposed architecture and implementation programme
**Scope:** FS-GG framework development and generated/product development
**Decision posture:** Plain TypeScript browser client; F# ASP.NET Core server; no Blazor; no Fable in the first implementation

## Executive summary

FS-GG should support web products through two related but deliberately separate structures:

1. **Framework development** remains distributed across FS-GG framework repositories and is
   coordinated on the existing FS-GG Coordination board by `drive-board`.
2. **Product development** happens in one polyglot product monorepo containing the F# ASP.NET Core
   server and the TypeScript browser client. That repository uses one product board and
   `work-board`, with one root SDD lifecycle.

The recommended browser renderer is a plain TypeScript package built directly on CanvasKit. Fable
is not required to decode or render the portable scene protocol, and adding it would introduce a
second compilation model before it has demonstrated unique value. The F# server remains the
authoritative application runtime and produces `FS.GG.UI.Scene` values. The browser consumes the
existing deterministic portable Scene package, renders it through CanvasKit, and returns normalized
input events.

The main discovery is that much of the hard architectural seam already exists:

- `FS.GG.UI.Scene` is dependency-light and renderer-independent.
- Feature 146 already shipped a deterministic `FSGGSCENE` binary package, resource and capability
  inspection, shaped-text evidence, and a Skia reference-rendering oracle.
- The recorded browser feasibility result explicitly chose a generated CanvasKit command-stream
  proof as the fallback.
- What does **not** exist is the browser execution, candidate image, or perceptual comparison. The
  current evidence says this plainly; cross-backend fidelity is still unproven.
- SDD lifecycle artifacts are language-neutral enough to govern the product. SDD already imports
  both TRX and JUnit XML and explicitly does not run tests itself.
- SDD's current golden path is nevertheless F#/.NET-centric in scaffolding, API-surface checks,
  generated builds, and release receipts. Polyglot support must be proved as a reference workspace,
  not asserted from the generic artifact model.

The recommended sequence is therefore:

1. establish a mixed F#/TypeScript SDD acceptance workspace;
2. implement and prove the TypeScript decoder and CanvasKit renderer against the existing corpus;
3. add a transport-independent live-session envelope and an ASP.NET Core SignalR adapter;
4. add a polyglot web product profile to Templates;
5. generate a reference product monorepo and run it through `work-board` plus the full SDD
   lifecycle;
6. add incremental scene delivery only after full-snapshot measurements justify it.

This avoids two common architectural mistakes: treating TypeScript as an awkward subproject of the
F# renderer, and creating a second planning system for the web half of a product.

## Decisions

### D1 — Product source lives in one polyglot monorepo

A web product is one deployable system and should be one repository:

```text
Product/
├── Product.slnx
├── global.json
├── package.json
├── package-lock.json
├── src/
│   ├── Server/                 # F# ASP.NET Core application
│   └── Web/                    # TypeScript browser application
├── packages/
│   └── product-protocol/       # product-owned TS types, only if needed
├── tests/
│   ├── Server.Tests/
│   ├── Web.Tests/
│   └── Browser.Tests/
├── work/                       # one SDD lifecycle
├── .fsgg/
├── .agents/
├── .codex/
└── .github/workflows/
```

The server and client should not be split merely because they use different languages. A split
would make every product feature a cross-repository change, duplicate planning state, and make
atomic contract changes unnecessarily difficult. A monorepo allows one issue, one specification,
one dependency graph, one review, and one release decision for a vertical product slice.

This does not make framework packages part of the product repository. Reusable scene decoding and
CanvasKit rendering belong to a framework repository and are consumed as versioned npm packages.

### D2 — Use plain TypeScript, not Blazor or Fable, for the browser runtime

The first browser implementation should be ordinary TypeScript with standard npm tooling.

Blazor is outside the requested design. Fable is a viable technology, but it does not currently
solve the principal risk: proving that a non-.NET browser can decode and faithfully render the
portable Scene protocol. Using Fable for that proof would couple the browser backend to F# compiler
output and weaken the value of the cross-language conformance test.

Fable should be reconsidered only for a bounded, measured reason, such as:

- a substantial pure application model must execute identically in server and browser;
- generated TypeScript contracts prove harder to maintain than shared F# contracts;
- an offline product mode needs meaningful domain logic rather than rendering and input bridging;
- profiling shows no unacceptable bundle, startup, debugging, or interop cost.

It should not be introduced merely to make the repository look more uniformly F#.

### D3 — Create a browser-rendering framework component

Create a new framework repository named **`FS.GG.Rendering.Web`**, coordinated on the existing
FS-GG Coordination board. It owns browser-specific implementation and npm identity, while
`FS.GG.Rendering` continues to own the renderer-neutral Scene vocabulary and portable protocol.

Start with one publishable package rather than a premature package family:

```text
FS.GG.Rendering.Web/
├── package.json
├── package-lock.json
├── src/
│   ├── protocol/               # FSGGSCENE decoder and inspection model
│   ├── canvaskit/              # Scene-to-CanvasKit rendering
│   ├── resources/              # font/image materialization and cache
│   └── diagnostics/
├── tests/
│   ├── protocol/
│   ├── conformance/
│   └── browser/
├── fixtures/                   # canonical packages and oracle metadata
└── docs/
```

Initial npm identity: `@fs-gg/rendering-web`.

Split packages later only when consumers need independent versioning or loading. Likely future
boundaries are `@fs-gg/scene-protocol` and `@fs-gg/canvaskit-renderer`, but publishing both on day
one would add release coordination without demonstrated benefit.

Ownership remains strict:

| Concern | Owner |
|---|---|
| `Scene`, `SceneCodec`, binary tags and compatibility rules | `FS.GG.Rendering` |
| Skia reference images and canonical cross-backend corpus | `FS.GG.Rendering` |
| TypeScript decoder, CanvasKit renderer, browser capability ledger | `FS.GG.Rendering.Web` |
| Generic SDD lifecycle and polyglot acceptance | `FS.GG.SDD` |
| Generated product layout and web profile | `FS.GG.Templates` |
| F# ASP.NET host and product UI/domain behaviour | Product repository |
| Generic transport abstraction, if later proven reusable | `FS.GG.Net` |
| Organisation board schema, registry and cross-repo sequence | `.github` |

### D4 — Keep the durable Scene package independent of the live transport

The existing `FSGGSCENE` portable package is the rendering contract. SignalR, WebSocket framing,
HTTP fetches, and session acknowledgements are delivery mechanisms around it, not additions to the
Scene format.

This separation produces two independently versioned contracts:

1. **Portable Scene protocol** — durable, deterministic, content-addressable, renderer-neutral;
   owned by `FS.GG.Rendering`.
2. **Live session envelope** — ephemeral connection and interaction messages; initially owned by
   the product/reference implementation and promoted only if more than one product needs it.

The browser must be able to load a saved Scene package without SignalR. The server must be able to
deliver the same package over SignalR, raw WebSocket, HTTP, or a test fixture without changing its
bytes.

### D5 — Use SignalR first, behind a small transport interface

ASP.NET Core SignalR is the recommended first hosting adapter. It supplies a supported TypeScript
client, automatic reconnection, WebSocket-first transport selection with fallbacks, streaming, and
JSON or MessagePack hub protocols. These are useful product concerns that should not be rebuilt for
the first vertical slice. Microsoft documents the JavaScript client as an npm package and supports
WebSockets, Server-Sent Events, long polling, automatic reconnection, and MessagePack
([client feature matrix](https://learn.microsoft.com/en-us/aspnet/core/signalr/client-features?view=aspnetcore-10.0),
[overview](https://learn.microsoft.com/en-us/aspnet/core/signalr/introduction?view=aspnetcore-10.0)).

Use JSON for the small control envelope during the first proof and carry the already-encoded Scene
package as binary data. Move the hub envelope to MessagePack only after measurement. SignalR's
MessagePack protocol is compact, but its JavaScript binding is case-sensitive and has date
representation caveats
([MessagePack guidance](https://learn.microsoft.com/en-us/aspnet/core/signalr/messagepackhubprotocol?view=aspnetcore-10.0)).
The Scene bytes must not be re-serialized into hub-specific object graphs.

The application-facing TypeScript surface should be approximately:

```ts
export interface SceneSessionTransport {
  connect(signal?: AbortSignal): Promise<SessionWelcome>;
  snapshots(): AsyncIterable<SceneSnapshot>;
  resources(): AsyncIterable<ResourcePayload>;
  sendInput(batch: InputBatch): Promise<void>;
  requestResync(reason: ResyncReason): Promise<void>;
  close(): Promise<void>;
}
```

Only the adapter imports `@microsoft/signalr`. Tests and future raw-WebSocket adapters implement the
same interface.

### D6 — One SDD lifecycle governs both language lanes

The product has one specification and one lifecycle. It must not create a F# SDD workflow and a
separate TypeScript planning convention.

SDD already supports the important evidence boundary. Its schema reference states that
`fsgg-sdd evidence --from-test-report` parses TRX or JUnit XML into observed-run receipts and that
SDD never executes the suite. Vitest and Playwright can emit JUnit XML, while .NET emits TRX.
Consequently, the root product build owns execution and SDD owns evidence interpretation.

A feature can therefore include obligations such as:

```yaml
evidence:
  - id: server-tests
    kind: verification
    source: artifacts/test-results/server.trx
  - id: web-unit-tests
    kind: verification
    source: artifacts/test-results/web-unit.junit.xml
  - id: browser-conformance
    kind: verification
    source: artifacts/test-results/browser.junit.xml
```

This is not equivalent to saying SDD is already fully polyglot. The following current assumptions
need explicit handling:

| SDD area | Current posture | Required action |
|---|---|---|
| Lifecycle artifacts and validation | Language-neutral | Reuse unchanged |
| Test evidence | TRX and JUnit supported | Prove two reports in one feature |
| Test execution | Deliberately external | Root scripts run both toolchains |
| Scaffold providers | `dotnet new`-centred | Reuse a `dotnet new` product template containing arbitrary TS files; do not add a second scaffold engine yet |
| Namespace derivation | F#-specific provider input | Keep inside the F# product provider, not the lifecycle core |
| `surface` command | `src/**/*.fsi` baseline | Keep for F#; use API Extractor or declaration snapshots for TS until a generic provider is justified |
| Generated root build | Primarily `dotnet`/solution oriented | Extend the web product template's root build orchestration |
| Release receipts | NuGet/MSBuild vocabulary dominates | Add an artifact-kind-neutral receipt before publishing reusable npm packages through SDD |
| Skills and examples | F#/.NET golden path | Add a mixed reference product and web product skills |

The first SDD change should be an executable mixed-workspace acceptance fixture, not a new
`language: typescript` field. The core model does not need to know a source language merely to
validate requirements, tasks, evidence, and readiness.

### D7 — `drive-board` governs framework work; `work-board` governs product work

The same board machinery should be reused, but the two drivers have different scopes.

```text
FS-GG Coordination board
  drive-board
    ├── .github: register repo/contracts/phase
    ├── SDD: prove polyglot lifecycle
    ├── Rendering: fixtures and protocol clarifications
    ├── Rendering.Web: decoder and CanvasKit backend
    └── Templates: web product profile

Product board
  work-board
    ├── server feature paths
    ├── client feature paths
    ├── shared live-envelope paths
    ├── browser tests
    └── deployment
```

`drive-board` is the organisation operator. It observes the cross-repository Coordination board and
materializes framework work in the appropriate framework repositories.

`work-board` is the product workspace driver. It operates the product's configured board and
coordinates multiple workers through declared, disjoint path sets. It can schedule a server lane
and a client lane concurrently when their contracts are already settled.

When product work discovers a framework gap, the product issue remains on the product board and is
blocked by a cross-repository request on the FS-GG Coordination board. The framework implementation
must not be copied into the product just to keep one board green.

`work-roadmap` remains appropriate for a small single-repository, sequential ledger. It is not the
primary driver for this product because the product already needs board state, parallel lanes,
blocking relationships, and SDD escalation.

## Current-state research

### FS.GG.Rendering already has the correct portability seam

Feature 146, **Render-Anywhere Scene Protocol**, is marked shipped. It establishes:

- a dependency-light public `SceneCodec`;
- a deterministic binary TLV encoding with the `FSGGSCENE` magic header;
- explicit protocol major/minor and profile;
- stable numeric tags and length-skippable optional fields;
- canonical resource ordering and content identity;
- required/optional capability inspection before rendering;
- font/image resource manifests without local paths;
- shaped glyph-run evidence;
- newer-major and unknown-required-field rejection;
- byte-identical repeat export acceptance;
- Skia reference images with checksums and metadata.

This is a substantially better browser boundary than JSON generated from F# records. It is already
designed for foreign consumers, supports deterministic conformance fixtures, and separates local
resource resolution from the scene.

The portable package is also a better boundary than a CanvasKit command stream. The browser backend
should interpret scene semantics and produce CanvasKit operations locally. A CanvasKit-specific
stream would make the server a backend compiler and bind the durable contract to one rendering
technology.

### The browser proof is still outstanding

The shipped browser feasibility record is intentionally not a passing browser result. It says:

- no candidate image was rendered;
- no perceptual diff was computed;
- all three scenarios were `candidate-not-executed`;
- direct browser execution was unavailable in the harness;
- the decision was to continue with a generated CanvasKit command-stream proof and not claim a
  production browser backend.

This is the immediate, evidence-backed starting point. The first web-rendering milestone must turn
those three existing corpus entries into actual browser artifacts and explicit comparisons. It
should not first design patches, a generic transport, or a product template.

### CanvasKit is the strongest fidelity candidate

CanvasKit is Skia compiled to WebAssembly. The project documentation describes a WebGL-accelerated
canvas with Skia painting, paths, text, and related APIs
([CanvasKit overview](https://docs.skia.org/docs/user/modules/canvaskit/),
[quick start](https://skia.org/docs/user/modules/quickstart/)). Because the reference renderer is
Skia-based, CanvasKit offers the most plausible route to matching paint, path, and text semantics.

Its costs are real:

- the WASM payload and initialization are larger than a DOM or Canvas2D client;
- fonts and shaped text require careful resource loading;
- browser GPU and Skia builds can still produce raster differences;
- CanvasKit objects backed by WASM memory require explicit deletion;
- canvas pixels do not provide a semantic accessibility tree;
- input, focus, IME, clipboard, selection, and navigation are browser/DOM responsibilities.

CanvasKit should therefore own rendering only. A DOM sidecar owns semantic interaction.

### Alternative browser strategies

| Option | Advantages | Disadvantages | Decision |
|---|---|---|---|
| TypeScript + CanvasKit | Closest renderer family to Skia oracle; direct browser ecosystem; no .NET runtime | WASM size/startup; explicit lifetime; accessibility sidecar required | **Adopt** |
| TypeScript + Canvas2D | Small and ubiquitous; easy debugging | Lower fidelity for text, filters, paths and compositing; likely growing subset policy | Keep as diagnostic/fallback subset only |
| DOM/SVG renderer | Native accessibility, text and layout | Poor match for arbitrary retained scene semantics; browser layout becomes a second layout engine | Not the general renderer; use DOM for semantics |
| Fable + CanvasKit | Shared language and potentially shared pure logic | Compiler/runtime/interoperability layer; weaker independent protocol proof; smaller contributor pool | Revisit only for measured shared-logic need |
| Pixel/video streaming | Exact server pixels; thin client | Bandwidth, latency, accessibility and input complexity; loses local rendering value | Separate remote-desktop product, not this design |
| WebGPU custom renderer | Maximum long-term control | Very high shader/text/compositor investment; duplicates Skia | Do not pursue while CanvasKit remains viable |

### Product package manager

Use npm workspaces and commit one root `package-lock.json` for the initial product and browser
framework repository. npm workspaces natively link packages in one local tree and run scripts per
workspace ([npm workspaces](https://docs.npmjs.com/misc/workspaces/)); the lockfile pins exact
resolved versions for reproducible installation
([npm install behaviour](https://docs.npmjs.com/cli/install/)).

pnpm is technically attractive, particularly for large monorepos, but no present requirement
justifies adding another required tool to the generated-product golden path. Reassess if install
time or disk duplication becomes material.

### Accessibility cannot be inferred from pixels

Canvas fallback/sub-DOM content is the browser accessibility route for an otherwise inaccessible
canvas ([MDN canvas guidance](https://developer.mozilla.org/en-US/docs/Web/API/Canvas_API/Tutorial/Basic_usage.)).
The product therefore needs a semantic projection separate from the visual Scene.

Do not try to reconstruct roles, names, values, focus order, editable state, and actions from paint
operations. Controls already know those facts before rendering and should emit them deliberately.
The semantic projection must satisfy the platform expectation that user-interface components expose
name, role and value
([WCAG 2.2, Name, Role, Value](https://www.w3.org/WAI/WCAG22/Understanding/name-role-value.html)).

## Target runtime architecture

### Server-authoritative first slice

The first product model is server-authoritative:

```text
domain message
      │
      ▼
F# update / application model
      │
      ├──────────────► semantic accessibility snapshot
      │
      ▼
FS.GG.UI.Scene
      │
      ▼
SceneCodec.export
      │
      ▼
immutable FSGGSCENE bytes + resource manifest
      │
      ▼
live session envelope / SignalR adapter
      │
      ▼
TypeScript decoder ──► capability/resource preflight
      │
      ▼
CanvasKit renderer ──► browser canvas

DOM input + semantic sidecar
      │
      ▼
normalized InputBatch
      │
      ▼
SignalR adapter ──► F# Elmish/application messages
```

This keeps domain truth in F#, makes the browser replaceable, and proves the rendering boundary
before attempting client-side domain execution.

### Live session envelope v1

The first envelope should be deliberately small:

| Message | Direction | Required fields |
|---|---|---|
| `ClientHello` | client → server | envelope versions, scene protocol range, renderer capabilities, viewport, DPR, locale |
| `ServerWelcome` | server → client | selected versions, session id, limits, initial sequence |
| `SceneSnapshot` | server → client | sequence, scene package identity, bytes or resource URL references |
| `ResourcePayload` | server → client | resource id, content hash, media type, bytes |
| `InputBatch` | client → server | client sequence, last rendered server sequence, timestamp, normalized events |
| `Ack` | both | highest accepted sequence |
| `ResyncRequest` | client → server | last accepted sequence and diagnostic code |
| `SessionDiagnostic` | server → client | stable code, severity, retry/resync guidance |

Rules:

- Sequence numbers are monotonic per session.
- A snapshot is applied only after protocol, capability, and required-resource preflight succeeds.
- Rendering a sequence is atomic from the user's perspective; never expose half-loaded required
  resources.
- The client retains the last successfully rendered frame until a replacement succeeds.
- Pointer-move events may be coalesced; button, key, composition, focus and command events may not.
- Queues are bounded. When rendering falls behind, intermediate full snapshots may be discarded in
  favour of the newest complete snapshot.
- Input includes the last rendered server sequence so the server can reject or reinterpret stale
  hit-test intent.
- Diagnostics use stable codes, not exception strings.
- Resource and scene byte sizes, node counts, nesting depth and decoded allocation have limits.

### Full snapshots before deltas

Protocol v1 sends complete Scene packages. Do not introduce a scene-diff language until measurements
show a real bandwidth or latency problem.

Full snapshots provide:

- deterministic replay;
- simple resynchronization;
- compatibility with saved fixtures;
- one decoder path;
- easy content hashing and caching;
- fewer ordering and partial-failure states.

Measure package size, encode time, transfer time, decode time, resource cache hit rate, render time
and dropped snapshots. Only then design a patch protocol. Any future patch format belongs to the live
envelope and must resolve to the same complete portable package semantics.

### Resources and fonts

Resources are content-addressed and never resolved from arbitrary producer paths or untrusted URLs.
The server either embeds small resources or supplies authenticated resource endpoints keyed by the
manifest identity. The client:

1. inspects the package;
2. checks its bounded cache;
3. requests missing required resources;
4. verifies kind, length and digest;
5. materializes CanvasKit objects;
6. renders only when the required set is complete.

Shaped glyph evidence remains authoritative for visual fidelity. Fonts are still needed where the
CanvasKit APIs require typeface data. The conformance corpus must include missing, corrupted, wrong
kind, fallback and shaped-text cases already named by Feature 146.

### CanvasKit renderer structure

Keep the renderer as a pure-ish interpreter around an explicit resource/lifetime boundary:

```ts
export interface SceneDecoder {
  inspect(bytes: Uint8Array): SceneInspection;
  decode(bytes: Uint8Array): PortableScene;
}

export interface RenderResources {
  image(id: ResourceId): CanvasKitImage;
  typeface(id: ResourceId): CanvasKitTypeface;
}

export interface SceneRenderer {
  render(
    target: RenderTarget,
    scene: PortableScene,
    resources: RenderResources,
    options: RenderOptions
  ): RenderReceipt;
}
```

Every CanvasKit allocation needs a documented owner and disposal point. Per-frame paint/path
objects should be scoped and deleted; reusable image, font, shader and surface objects belong to a
bounded cache. Tests should include repeated-render memory behaviour, not just final pixels.

### Input and semantic DOM sidecar

The canvas handles visual output and pointer coordinate capture. A synchronized DOM sidecar handles:

- accessible roles, names, descriptions, values and states;
- tab order and focus;
- keyboard activation;
- editable text and IME composition;
- clipboard operations;
- selection and caret interaction where supported;
- browser-native labels and live regions.

The sidecar nodes may be visually hidden or spatially aligned depending on the control. Their stable
semantic ids map to server control ids. Browser events are normalized into the existing FS.GG input
vocabulary where possible; product-only events remain in the product protocol until a reusable
contract is demonstrated.

The semantic snapshot and visual snapshot share a sequence. If they cannot be applied together, the
client retains the previous pair.

## Product development workflow

### One product board

The product repository configures `work-board` against its own GitHub Project. All work needed to
ship the product is visible there, including server, browser, integration, deployment and product
documentation.

Recommended board fields remain aligned with the existing machinery:

- Status;
- Priority;
- Size;
- Repository;
- Phase;
- Blocked By;
- Paths;
- Execution Mode;
- SDD required/readiness state.

`Paths` is especially useful in the monorepo. For example:

| Item | Declared paths | Parallelism |
|---|---|---|
| Add server scene session | `src/Server/**`, `tests/Server.Tests/**` | Can run with renderer after envelope contract settles |
| Add browser decoder | `src/Web/protocol/**`, `tests/Web.Tests/protocol/**` | Independent of server |
| Add CanvasKit painter | `src/Web/rendering/**`, `tests/Browser.Tests/**` | Depends on decoder fixtures |
| Add live envelope types | `src/Server/Protocol/**`, `src/Web/transport/**` | Single owner because it spans the boundary |
| Add deployment | `Dockerfile`, `.github/workflows/deploy.yml` | Depends on integrated build |

Cross-language paths are not a reason to create two boards. They are a reason to declare ownership
accurately.

### SDD escalation

Use the current work-board rule: large/extra-large work, explicit `needs-sdd`, or contract work
enters SDD. A product feature specification spans both languages when the user outcome spans both
languages.

A web feature plan should name:

- F# server modules and public interfaces;
- TypeScript modules and exported types;
- live-envelope changes;
- scene-protocol dependency and supported range;
- accessibility behaviour;
- browser and server evidence;
- deployment and compatibility impact.

Tasks can then be parallelized by paths without splitting the specification.

### Root developer commands

The generated product must have one discoverable root command surface:

```text
./build.sh restore
./build.sh build
./build.sh test
./build.sh browser-test
./build.sh check
./build.sh run
./build.sh pack
```

On Windows, provide the repository's standard equivalent. The script is an orchestrator, not a new
build system. It calls `dotnet` for the server and `npm ci`/npm scripts for the web workspace.

`check` should execute, in order:

1. deterministic restore;
2. F# format/lint/build/surface gates;
3. TypeScript typecheck/lint/build/API declaration check;
4. server unit/integration tests with TRX;
5. web unit tests with JUnit;
6. headless browser conformance with JUnit and image artifacts;
7. SDD evidence import and verify for the active work item where applicable;
8. generated-product drift and lockfile checks.

The root command exits non-zero if any lane fails and preserves each lane's native report.

### Continuous integration

Use separate CI jobs for fast feedback, plus one integrated product gate:

| Job | Outputs |
|---|---|
| `dotnet` | build logs, TRX, F# surface report |
| `web` | typecheck/lint, unit JUnit, declaration/API report |
| `browser-conformance` | Playwright JUnit, candidate PNGs, diffs, browser metadata |
| `integration` | built server + browser, SignalR session tests, accessibility results |
| `sdd` | lifecycle validation/verify reports |
| `package` | NuGet/npm dry-run artifacts and manifests when publishable |

Playwright supports JUnit reporting and screenshot capture
([reporters](https://playwright.dev/docs/test-reporters),
[screenshots](https://playwright.dev/docs/screenshots)). Use its visual comparison support for
workflow, but keep the FS-GG comparison receipt explicit: oracle identity, candidate identity,
browser, CanvasKit version, dimensions, metric, tolerance and verdict.

### TypeScript public-surface evidence

Do not pretend `.d.ts` files are `.fsi` files. For a publishable browser framework package:

- enable declaration generation;
- use API Extractor to produce a reviewed API report;
- fail CI on unreviewed report drift;
- classify semantic-version impact;
- verify the packed npm tarball's export map and declarations;
- record package digest and provenance.

API Extractor is designed to roll up TypeScript declarations and maintain an API report
([official overview](https://api-extractor.com/pages/overview/intro/)). SDD can initially reference
that report as feature evidence. A generic `surface` provider should be added only after this
pattern has been exercised and its stable contract is understood.

## Framework programme and dependency order

All framework items below live on the existing FS-GG Coordination board. They should use blocker
links, not numeric priority alone.

### Milestone 0 — Record the architecture and register the programme

**Owner:** `.github`

- accept this report;
- create an ADR for the durable decisions: repository boundary, product monorepo, TypeScript
  browser runtime, driver split and transport/protocol separation;
- add `FS.GG.Rendering.Web` to the component registry;
- add an appropriate Web phase/board option through the guarded board-schema process;
- add dependency edges for Scene protocol fixtures and Templates consumption;
- create the epic and dependency-ordered cross-repository requests.

Exit: every downstream item has one owner, one board location and explicit blockers.

### Milestone 1 — Prove the SDD polyglot lifecycle

**Owner:** `FS.GG.SDD`

Build a minimal mixed reference workspace containing:

- one F# server project and test project;
- one TypeScript package and Vitest suite;
- one Playwright browser test;
- one root SDD work item;
- TRX and JUnit receipts imported into that work item;
- verify/ship behaviour exercised without SDD running the tests;
- a package-free product case and a mixed NuGet/npm release-contract case.

Make the smallest changes exposed by this proof. Likely changes:

- document repeated/multiple report evidence clearly or add a bounded multi-report input if the
  existing command is awkward;
- remove generated-build assumptions that require every code lane to be in `Product.slnx`;
- generalize release artifact receipts away from exclusively NuGet/MSBuild terms;
- keep the existing F# `surface` command rather than weakening it;
- add a provider-neutral hook or evidence kind for a TypeScript API report only if required.

Exit: a CI acceptance test demonstrates one lifecycle across both lanes with real reports.

### Milestone 2 — Execute the browser feasibility proof

**Owners:** `FS.GG.Rendering`, then `FS.GG.Rendering.Web`

Rendering:

- freeze and publish canonical Feature 146 fixture bytes and expected inspection records;
- expose the oracle images and metadata as a versioned test-data artifact or documented fixture
  acquisition path;
- document every v1 binary tag and numeric representation needed by a foreign implementation;
- add language-neutral conformance vectors for malformed/newer/unknown/resource cases.

Rendering.Web:

- implement the TypeScript v1 decoder independently from the F# implementation;
- pass canonical byte and diagnostic fixtures;
- integrate a pinned CanvasKit build;
- render the three existing showcase scenarios in a real supported browser;
- emit candidate images and explicit comparison receipts;
- update the capability ledger honestly.

Exit: all three Feature 146 showcase entries produce browser images; the final decision is based on
measured comparisons rather than `candidate-not-executed`.

### Milestone 3 — Add interaction and ASP.NET hosting

**Owner:** reference product first

- define live envelope v1;
- add SignalR server and TypeScript adapter;
- deliver complete Scene snapshots and resources;
- normalize pointer, keyboard, focus, resize and composition events;
- apply bounded queues, acknowledgement and resync;
- prove reconnect and stale-input behaviour;
- add the semantic DOM sidecar for the first interactive controls.

Keep this in the reference product until a second product or stable reuse case justifies promotion.
If promotion occurs:

- renderer/session-neutral contracts go to `FS.GG.Net` only if they are truly transport concerns;
- control semantics go to Rendering;
- the SignalR adapter may become a narrow package;
- product-specific commands remain in the product.

Exit: a user can interact with a server-authoritative control showcase through the browser, and
disconnect/reconnect does not corrupt the session.

### Milestone 4 — Add the generated web product profile

**Owner:** `FS.GG.Templates`

Add a profile such as `web` or `app-web` that generates:

- F# ASP.NET Core server;
- TypeScript/Vite browser application;
- npm workspace and lockfile;
- server and web tests;
- Playwright configuration;
- root build orchestration;
- one SDD lifecycle;
- product `work-board` driver and board configuration guidance;
- framework package pins;
- deployment example;
- web-specific product skills.

Continue using `dotnet new` as the template transport. A dotnet template can contain arbitrary
TypeScript and configuration files; introducing a second scaffold engine is unnecessary.

Exit: generation is deterministic, generated output builds offline under the supported cache
conditions, all tests run, and drift checks cover both language lanes.

### Milestone 5 — Prove product development

**Owner:** generated reference product

Use the template to create a real product repository and deliver a vertical feature through:

- one product issue;
- one product board;
- `work-board`;
- one SDD specification;
- parallel server and client tasks with disjoint paths;
- framework blockers routed to the Coordination board;
- full CI evidence;
- production-like deployment.

Exit: the workflow—not just the code—has been demonstrated.

### Milestone 6 — Optimize and publish

Only after the reference product is measured:

- decide whether full snapshots meet performance budgets;
- design delta delivery if justified;
- decide whether reusable live-session packages should be promoted;
- finalize npm trusted publishing/provenance;
- extend generic FS-GG release contracts;
- set browser support and compatibility policy;
- publish stable framework packages and advance Templates pins.

## Acceptance criteria and budgets

The initial programme is complete when:

### Protocol and fidelity

- The TypeScript decoder passes every canonical v1 positive and negative fixture.
- Fifty repeated server exports remain byte-identical as Feature 146 requires.
- The three existing showcase scenes produce real browser candidate images.
- Every comparison records the exact oracle, candidate, environment, metric and tolerance.
- Unsupported capabilities reject or degrade explicitly; nothing silently disappears.
- A version-skew test proves newer major rejection and supported-minor behaviour.

### Runtime

- Initial connect, disconnect, reconnect and resync are deterministic.
- Required resources are digest-verified before display.
- Rendering failure retains the last good scene and surfaces a stable diagnostic.
- Queues have tested bounds and coalescing rules.
- Input is associated with the scene sequence the user actually saw.
- No arbitrary resource URL or unbounded package allocation is accepted.

### Accessibility and interaction

- The first control corpus exposes correct name, role, value, state and focus order.
- Keyboard-only operation works.
- Text input exercises composition events, not just keydown.
- Visual and semantic snapshots change atomically.
- Automated accessibility checks are supplemented by keyboard and screen-reader acceptance scripts.

### Developer workflow

- A clean clone builds and tests from root commands.
- Node, .NET, CanvasKit and package dependencies are pinned.
- Both TRX and JUnit evidence reach one SDD work item.
- `work-board` can schedule disjoint server and browser tasks.
- A product framework gap is demonstrated through a Coordination-board request.
- Generated output and template source remain drift-checked.

### Initial performance budgets

Budgets must be captured before optimization and ratified from measurements. Suitable provisional
targets for the reference corpus are:

- decoder plus preflight: p95 under 10 ms for a 1 MiB package on the reference desktop;
- render after resources are warm: p95 under 16.7 ms for the basic interactive corpus;
- pointer-to-visible-response on a local reference deployment: p95 under 100 ms;
- no monotonically growing WASM heap across 10,000 identical renders;
- bounded snapshot queue with at most one pending obsolete full frame;
- first usable render and compressed bundle/WASM size recorded as budgets before declaring the
  backend production-ready.

These are hypotheses, not promises. The milestone-2 proof must establish realistic baselines and
change them through an explicit decision.

## Security and operations

- Prefer same-origin hosting of the built web assets and SignalR endpoint.
- If cross-origin hosting is required, allow only explicit trusted origins; Microsoft explicitly
  warns against permissive SignalR CORS
  ([SignalR security](https://learn.microsoft.com/en-us/aspnet/core/signalr/security?view=aspnetcore-10.0)).
- Authenticate the HTTP negotiation and hub; authorize session joins and resource reads.
- Treat all browser input and capability declarations as untrusted.
- Apply per-session message rate, byte, node, depth, resource and allocation limits.
- Never log access tokens, raw sensitive input, or full scene payloads by default.
- Use content security policy and self-host/pin production CanvasKit assets.
- Verify resource digests and media kinds before decoding.
- Do not evaluate script, shader source, arbitrary URLs or product code from a Scene package.
- Record protocol versions, package identities and stable diagnostic codes in traces.
- Scale tests must account for persistent SignalR connections and any required session affinity.

## Risks and mitigations

| Risk | Consequence | Mitigation |
|---|---|---|
| CanvasKit output differs from SkiaSharp | Browser fidelity claim fails | Oracle corpus, explicit tolerance, capability ledger, pin versions |
| WASM size/startup is unacceptable | Slow first render | Measure early, cache assets, lazy initialize, consider declared Canvas2D subset only with evidence |
| Shaped text/font mismatch | Layout and pixels drift | Preserve glyph evidence, digest fonts, negative fixtures, never silently reshape |
| Canvas is inaccessible | Product unusable to assistive technology | First-class semantic DOM snapshot, not inferred fallback text |
| Server round trip feels laggy | Poor interaction | Coalesce safe events, optimistic local affordances only after correctness, measure before moving domain logic |
| Live protocol contaminates Scene v1 | Durable contract churn | Separate versioned envelope and carry unchanged Scene bytes |
| SDD changes become a generic plugin system | Delays product | Acceptance fixture first; smallest concrete changes only |
| Two boards duplicate product state | Conflicting truth | Product state stays on product board; only framework requests go to Coordination |
| New repo fragments ownership | Contract drift | Explicit ownership table, registry edges, conformance fixtures and blocker ordering |
| npm release bypasses FS-GG rigor | Untracked public break | API report, tarball inspection, digest/provenance receipt, generic release contract before stable publish |
| Fable debate delays proof | No browser evidence | TypeScript is the committed first proof; revisit with explicit criteria |

## Non-goals for the first implementation

- running the F# application model in the browser;
- offline-first product semantics;
- a general remote desktop or pixel-streaming service;
- a custom WebGPU renderer;
- a scene patch/diff protocol;
- a general-purpose SDD toolchain plugin system;
- putting npm packages into `FS.GG.Rendering`;
- moving product issues to the organisation Coordination board;
- moving browser-specific implementation into the F# server project;
- claiming accessibility from canvas fallback text alone.

## Recommended issue graph

The first epic should be decomposed as follows:

```text
A0  Org ADR + component/board registration
│
├── A1  SDD mixed F#/TS acceptance workspace
│   └── A2  SDD release-artifact generalization (only gaps proved by A1)
│
├── B1  Rendering publishes canonical Scene v1 foreign-consumer corpus
│   └── B2  Rendering.Web TypeScript decoder
│       └── B3  CanvasKit basic-primitives proof
│           ├── B4  layered-portal proof
│           ├── B5  shaped-text/resource proof
│           └── B6  browser feasibility verdict
│
├── C1  reference-product live envelope
│   ├── C2  ASP.NET Core SignalR adapter
│   ├── C3  TypeScript transport adapter
│   └── C4  semantic DOM/input bridge
│       └── C5  reconnect/resync/security/performance acceptance
│
└── D1  Templates web profile        (blocked by A1, B6, C5)
    └── D2  generated reference product
        └── D3  product-board/work-board/SDD workflow proof
            └── D4  stable package and template release
```

`A1` and `B1` can start concurrently. `C1` can be designed once the full-snapshot contract is
settled, but its implementation should not distract from `B6`, the browser proof that is currently
missing. Templates must consume proven contracts rather than becoming the integration laboratory.

## Final recommendation

Proceed with the plain TypeScript design.

The architecture is not “an F# repository with an awkward JavaScript folder.” It is:

- a language-neutral portable Scene contract owned by Rendering;
- an independently conforming TypeScript/CanvasKit renderer owned by a browser framework component;
- an F# ASP.NET Core product host;
- one polyglot product monorepo;
- one product board driven by `work-board`;
- one SDD lifecycle fed by native TRX and JUnit evidence;
- one organisation Coordination board driven by `drive-board` for the framework programme.

This arrangement uses the strengths of the existing FS-GG machinery without forcing TypeScript
through F#-specific surfaces. It also preserves a credible path to Fable later: Fable can be added
for proven shared application logic without being made a prerequisite for browser rendering,
protocol conformance, or product governance.

The immediate next action is not to build the product template. It is to create the Coordination
epic, prove the mixed SDD workspace, and convert Feature 146's three
`candidate-not-executed` entries into real CanvasKit comparison evidence.
