# FABLE-ADOPT-01.1 current inventory and disposition

**Accepted source disposition dated 2026-10-01.** This is the Stage .1 decision packet for the
[FABLE-ADOPT-01 plan](../2026-10-01-staged-fable-game-adoption.md).
It inventories exact source and public NuGet state, assigns each selected finding, and defines the
smallest FourD-shaped reference seam. It changes no producer, template, product, package, runtime,
installed workspace, or knowledge store.

## Verdict

Stage .1 is accepted at source as the current disposition. Rendering already provides the shared retained SVG,
input normalization, focus/editing preservation, coordinate/hit-test and owned lifecycle surfaces.
Game already provides deterministic session semantics and an optional pure Game-to-Scene adapter.
The first implementation should compose and qualify those existing APIs in the `fable-game`
candidate before adding another abstraction.

No inspected product incident is promoted to a Rendering or Game defect without a producer-level
reproducer. FourD has the smallest suitable pilot because its browser is a product Fable bridge plus
custom DOM and its authority is local. SC2 and BAR retain their native gateway authority and guest
ABI. Their external-authority seam must be specified and qualified before either product uses the
Game session clock.

## Exact inventory boundary

The companion [machine inventory](fable-adopt-01.1-source-inventory-20261001.json) records each selected Git blob, SHA-256 and byte count.

| Repository | Inspected commit | Tree | Current boundary |
|---|---|---|---|
| Rendering | `be57d2927d10b7f9defca8c45f8e0971de0abddc` | `59cd344ece1d6229a1fb16dd27936cf689fbe268` | `SvgBrowser`, `SvgInputHost`, `SvgSessionHost`; Fable package payload |
| Game | `6e2558d811037104b0984d17e19a05dfe5a38421` | `930016adfe4278a972939764feb164bcced37249` | Game.Core sessions; optional pure `Game.Render` adapter and keymap |
| Templates | `cfe37f35a66e3494b211197a1c84de69f08bf87e` | `006553d8a59b6fef729553ed6b3093d94471dd27` | source candidate `fable-game` composition and browser tests |
| FourD | `60f2a41eeaf22325b6af7a264791c79644c6fb6d` | `713db27cb5a39e5896f7ddd82f222b26b6dbe984` | product Fable bridge and custom DOM grid/forms |
| SC2 | `e1f3fe8cdaca33a2c2e29c1aca6091b5f0a2633e` | `79ff99d0644cb7accab85dfb9b16924df2b32973` | direct SVG/DOM and native-session gateway |
| BAR | `bcba08f6dcc100d0bfcde4c9416a8f1101dedb82` | `3b63c26b925e7a0d0989d56de4bc317158790e77` | Elmish/Fable wrapper, product JavaScript, gateway and guest ABI |

These commits describe source. They do not prove that any corresponding package is installed in a
consumer or that a native product route has accepted it.

### Public package observation

On 2026-10-01, the public NuGet flat-container indexes returned:

| Package | Latest public NuGet version | Candidate/source observation |
|---|---:|---|
| `FS.GG.UI.Scene` | `0.31.0` | Template lock records this exact version and content hash. |
| `FS.GG.UI.Scene.SvgBrowser` | `0.31.0` | Template lock records this exact version and content hash. |
| `FS.GG.UI.KeyboardInput` | `0.31.0` | Template lock records this exact version and content hash. |
| `FS.GG.Game.Core` | `0.16.0` | Template lock records this exact version and content hash. |
| `FS.GG.Game.Render` | `0.16.0` | Optional pure adapter; the inspected template does not directly require it. |
| `FS.GG.Workspace.Template` | `0.15.0` | Templates source says `0.16.0`, while `fable-game.providers.yml` still selects `0.15.0`. Source `0.16.0` is not publication evidence. |

The available read-only GitHub Packages endpoint returned HTTP 403, so its exact publication state
is **unknown**. GitHub Releases are not substituted for package feed evidence. Installed state is
also **unknown** because this source-only window did not inspect a receiver. Stage .4 still owns
byte-identical publication readback and fresh installed qualification.

## Existing shared surfaces

1. **Rendering input and retained SVG.** `SvgInputHost` normalizes keyboard, pointer, touch and
   gamepad observations while preserving native editing and focus. `SvgBrowserHost` exposes local
   SVG coordinate pan/zoom, inverse-transform hit testing, retained state, owned-resource counts and
   disposal. `SvgDocumentBrowserHost` uses browser paint/transform/clip/mask semantics for hit tests.
2. **Rendering lifecycle.** `SvgSessionHost` exposes generation and projection revision, ordered
   session effects, replaceable projection coalescing, pause/recovery/replacement and observable
   listener/frame/request settlement through disposal.
3. **Game semantics.** Game.Core owns deterministic sessions and ordered input. `Game.Render` is a
   total side-effect-free projection/keymap helper where a product uses compatible Game primitives;
   direct product-to-Scene projection remains supported.
4. **Template composition.** The source candidate contains an SVG foundation, arcade composition,
   browser tests and direct Scene/SvgBrowser package references. The SvgBrowser package declares
   Fable source plus its worker/package resources. End-to-end consumer delivery of the required
   native JavaScript graph remains a qualification obligation.
5. **Product ownership.** FourD retains four-axis rules, saves and accessible controls. SC2 and BAR
   retain gateway identity, command receipts, native session authority, guest/module ABI and their
   product acceptance baselines.

## Finding disposition matrix

| ID | Finding or capability | Classification and owner | Reproducer or explicit gap | Disposition and acceptance |
|---|---|---|---|---|
| F01 | Keyboard/pointer/touch/gamepad normalization, editing/IME exclusion, focus scope and held-input release | Existing shared capability; **Rendering** owns producer behavior, each product owns semantic mapping | Existing producer tests are the baseline. The candidate consumer still lacks one end-to-end browser case combining editable focus/IME, blur/visibility and held release. | Reuse `SvgInputHost`. Add a generated-workspace Chromium case. Route a producer change only if that case first reproduces a Rendering failure. |
| F02 | SVG screen coordinates, transforms, overlays and topmost semantic hit testing | Existing shared capability; **Rendering** | Exercise transformed SVG, overlay/clip/mask, zoom anchor and pointer target through the published Fable package. No shared failure is currently demonstrated. | Use `SvgBrowser` coordinates/hit tests. Product adapters convert the returned semantic target into commands. |
| F03 | Ordered commands, generation/revision fencing, replaceable projection coalescing, repeated replacement and disposal | Existing shared capability; **Rendering** for host lifecycle, **Game** for session meaning | Run delayed stale reply, ordered command, projection burst and repeated mount/replace/dispose through a fresh candidate. External authority is an explicit unqualified gap. | Coalesce presentation only. Never coalesce ordered commands or required completions. Require zero owned listeners/frames/requests after dispose. |
| F04 | Ordinary forms, editors and accessible panels | Consumer composition; **Templates** demonstrates it, each **product** retains its controls | Candidate browser test puts form/editable elements beside the SVG and verifies tab order, IME text and screen-reader names survive render/replacement. | Keep normal DOM for forms/editors. Do not force every surface into Scene. |
| F05 | Game-to-Scene mapping and default keymap | Existing optional capability; **Game** | A pure adapter test can compare domain state and semantic input to expected Scene/command values. Applicability to a product is an explicit per-slice decision. | Use `Game.Render` when primitives fit. It is not a mandatory dependency and does not own browser or native authority. |
| F06 | Producer Fable source and native JavaScript package closure | Missing installed proof; each **producer** owns its payload, **Templates** owns composition | Pack the exact candidate, create a clean workspace with no repository-source fallback, inspect delivered files, build with Fable/Vite and load the production import graph in Chromium. | Require exact package bytes and imports. A NuGet reference alone does not prove worker or transitive ESM availability. |
| F07 | FourD custom DOM grid/forms can supply the smallest reference seam | Consumer adoption gap; **FourD**, with **Templates** for the reference | Candidate fixture projects one representative encounter, maps one normalized observation to one legal semantic command, preserves form/grid focus, and disposes. No gameplay/save change. | Define the seam below, qualify it in the candidate, then implement one FourD pilot slice in Stage .5. |
| F08 | SC2 direct SVG/DOM and native gateway | Product adoption gap; **SC2** | Current source proves product-specific gateway/reconnect/authority and input behavior. A common external-authority composition reproducer is absent. | Inventory now. Do not use Game clock or replace gateway authority until a separately qualified seam exists. Preserve native receipts and ABI. |
| F09 | BAR product JavaScript rendering, module graph, gateway and guest ABI | Product/adoption and packaging work; **BAR** | BAR source has explicit IME, SVG coordinate, blur/visibility and dispose handling. Earlier missing production module graph is a product packet-closure incident, not a Rendering defect. | Keep BAR ownership. A clean product package/import-graph test must pass before adoption; no shared defect claim without a producer-level reproducer. |
| F10 | BAR stock handoff rejects a semantically equal closed selection record because `JSON.stringify` compares insertion order | Demonstrated product validator defect; **BAR** | Exact diagnosis SHA-256 `af3679d9d4d3e7038cb8bade413db8b20277b5464360b0752303e5e1c94b28e6`: the NullAI closure and production module graph passed, then `validateStockSmokeHandoff` rejected the four correct scalar values before test registration. | Repair the narrow BAR validator with exact-key plus named scalar comparison and a canonical Python-to-JavaScript regression. Reuse the general canonical-identity principle only when another producer has its own reproducer. No Rendering defect is inferred. |
| F11 | Process cleanup, deployment records, artifact custody and native acceptance | Operational work; existing **product/tool owner** | Product evidence route, not a framework unit fixture. | Remains outside this adoption source contract and outside Stage .1. |
| F12 | SC2/BAR external-native projections and semantic commands | Missing shared composition seam; **Rendering/Game jointly only after contract admission**, products retain authority | Required reproducer must model externally supplied revision/generation, accepted/rejected command receipt, reconnect/replacement and disposal without advancing a local Game clock. | Stage .1 records the gap. Stage .2 may add a seam only after its independent contract and producer tests exist. |

## Proposed common browser contract

The common contract is a composition of current APIs plus one explicitly open authority boundary:

- **Projection:** a product supplies an immutable `Scene`/retained scene and stable semantic target
  identifiers. Rendering owns SVG mount, transform-aware hit testing, replaceable projection and
  owned browser-resource cleanup.
- **Observation:** `SvgInputHost` supplies normalized physical observations. It must ignore gameplay
  bindings while native text editing or IME composition owns the event, release held state on the
  documented blur/visibility paths, and report listener/deadline/gamepad/source counts.
- **Semantic command:** a thin product adapter maps a normalized observation plus current product
  availability to a domain command. Physical gesture and command meaning remain distinct. Game.Render
  may provide a pure adapter/keymap where compatible.
- **Ordering:** the authority orders commands and required completions. Rendering may coalesce only
  replaceable projections. Generation/revision rejects stale presentation and replies.
- **Authority modes:** `LocalGameSession` may use the Game session clock. `ExternalAuthority` supplies
  authoritative projections and command receipts from the product gateway; it must never be advanced
  by the Game clock. The latter is a proposed shape and is not qualified or implemented here.
- **DOM islands:** forms, editors, file inputs, dialogs and accessible panels stay ordinary DOM.
  Focus order, labels, editing, IME and restoration are part of browser conformance.
- **Payload:** every producer declares Fable source and native JavaScript/worker assets needed by its
  public API. A clean generated receiver must resolve the complete production graph from delivered
  package/template bytes.
- **Disposal:** the composition has one owner and an idempotent disposal boundary. It closes product
  subscriptions/transports it created and disposes input/session/SVG hosts; observations must show
  the documented settled counts.

The external-authority mode is deliberately a contract gap. Its future implementation cannot grant,
replace or weaken SC2/BAR gateway authority, native receipts, module sandbox or guest ABI.

Administrative and qualification contracts should derive their wire descriptor from an F#-validated
schema and model their state changes in Quint. The separately planned `OPS-TYPED-01` track owns that
operational work after a fresh Astra review; it is not an existing typed replacement and does not
duplicate Rendering or Game ownership.

## Smallest FourD-shaped reference seam

Stage .3 should add a reference slice to the candidate template with these narrow ports; names are
illustrative until implementation review:

```text
FourDProjection -> Scene                         // pure product projection
NormalizedBrowserObservation * FourDProjection
  -> FourDSemanticCommand option                 // pure product input adapter
LocalGameSession<FourDState, FourDSemanticCommand>
  -> SvgSessionHost callbacks                    // local authority only
```

The fixture contains one representative encounter projection, one legal full-cell destination and
one semantic command. It keeps the existing DOM form/grid and accessible panel beside the SVG. It
must not change FourD rules, Commitment/Pressure choices, four-axis representation, save/replay
formats or evaluation behavior.

Reference acceptance requires:

- a clean generated candidate builds from declared package and JavaScript assets with no repository
  source fallback;
- pointer coordinates under an SVG transform select the expected semantic target;
- keyboard alternative produces the same command while editable focus and IME text remain native;
- blur/visibility releases held input, stale generation/revision work has no effect, and ordered
  commands survive projection coalescing;
- repeated mount, session replacement and dispose leave the documented owned counts settled;
- existing FourD product regression/save baselines remain the Stage .5 gate, not evidence inferred
  from the reference fixture.

## Later stage outline

- **.2:** implement an admitted shared capability gap or a shared failure reproduced by F01–F03/F12
  tests. A missing product package graph stays with that product. Preserve public compatibility or
  publish a reviewed successor.
- **.3:** compose the FourD-shaped reference in Templates against exact producer candidates and run
  .NET/Fable plus actual browser conformance.
- **.4:** publish the coherent producer/template set, verify byte-identical feed readback, then create
  and qualify a fresh installed workspace. Source versions do not satisfy this stage.
- **.5:** adopt one FourD encounter with product parity and save/replay evidence.
- **.6:** adopt selected SC2 and BAR surfaces independently after external authority is qualified;
  retain each native acceptance route and ABI.
- **.7:** remove only the product infrastructure proven superseded after parity.

## Stage .1 closure and open evidence

This packet supplies one owner and disposition for every selected finding, plus a reproducer or an
explicit gap. It does not establish GitHub Packages publication, installed receiver state, an
external-authority seam, fresh generated-workspace conformance, any product adoption, or native
acceptance. Those results remain attached to their owning stages and exact future evidence.
