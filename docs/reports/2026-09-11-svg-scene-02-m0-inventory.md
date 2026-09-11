# SVG-SCENE-02 M0 inventory and qualification subjects

Date: 2026-09-11. Feature: **SVG-SCENE-02.1**. Status: accepted inventory; implementation and
qualification remain with their named milestones.

This report freezes the evidence subjects required by the
[scene/renderer subroadmap](../roadmaps/svg-game-engine-scene-renderer.md). It is an inventory, not a
capability-completion claim. The inspection used `.github` `528ce616965ac58afa95b540537d7ba78fe43ca1`,
Rendering `815783987fbf1d6f2e8165e2ae31ddf0bf61db2d`, Game
`24f79084fdd289f34387f91b1d4398c78fde16eb` and Templates
`b081808cc8c802d264d8fdeaf417911ca821c211`. S.I.R. was not accessed. Its programme boundary comes only
from the previously merged [extraction report](2026-09-11-svg-game-engine-extraction-boundary.md) and
Templates-owned characterized fixture evidence.

## Capability ledger

The states distinguish source, local demonstration, publication and installed behavior. A partial row
remains open even when one useful constituent is published.

| ID | Evidence state at the inspected revisions | Remaining owner/window |
|---|---|---|
| C01 | **Locally demonstrated, unpublished additions.** Rendering's `RetainedScene` supplies stable semantic object/layer/root IDs over existing Scene leaves; the public 0.28.0 package predates it. Identified child nodes and general affine placement are missing. | Rendering SVG-SCENE-02.2/.4; public installation in SVG-PREVIEW-A |
| C02 | **Locally demonstrated, bounded.** The foundation proves retained root/layers, pan/positive uniform zoom, selection/focus and inverse picking in Chromium. Object nodes are rebuilt after accepted transitions. | Rendering SVG-SCENE-02.2/.4–.6; complete responsive/scale proof in SVG-SCALE-01 |
| C03 | **Published primitives plus local subset; incomplete.** Scene 0.28.0 has paths, paint, text and other native vocabulary. The unpublished adapter accepts only its foundation subset and has no identified SVG document, definitions or full export. | Rendering SVG-SCENE-02.2–.7 for the Preview-A rendering/document subset; arbitrary import and authoring in SVG-AUTHOR-01 |
| C04 | **Missing as a qualified SVG workspace capability.** Existing generic controls do not establish vector-art creation/editing. | Rendering SVG-AUTHOR-01/M3 |
| C05 | **Missing.** There is no qualified SVG asset catalog, override or prefab/reference contract. | Rendering SVG-AUTHOR-01/M3 |
| C06 | **Missing.** No generated grid/freeform scene editor, snapping or validated authoring journey exists. | Rendering and Templates SVG-AUTHOR-01/M4 |
| C07 | **Published mechanism fragments.** Game `InputCommand` and Rendering `KeyboardInput`/controls expose command IDs, keymaps, focus and composition pieces; no one effective SVG workspace catalog or complete modal/sequence route is qualified. | Rendering SVG-SCENE-02.4 for minimal reducers; complete workspace behavior in SVG-INPUT-01 |
| C08 | **Published simulation primitives, runtime missing.** Game.Core 0.14.0 supplies fixed-step advancement and Harness supplies journeys, but no generic initialize/admit/project/snapshot/restore session envelope or M5 implementation exists. | Game SVG-SCENE-02.2 contract foundation; SVG-RUNTIME-01 implementation |
| C09 | **Partial published keyboard mechanism.** Pointer/touch parity, capture loss, held-action recovery, gamepad and action-map behavior are not a complete capability. | Rendering SVG-SCENE-02.4 bounded interaction; SVG-INPUT-01 and SVG-RUNTIME-01 |
| C10 | **Published partial algorithms.** Game.Core has AABB/spatial/pathfinding/physics primitives; the required circle/convex/raycast/swept adapters and game journey are incomplete. | Game SVG-RUNTIME-01/M5 |
| C11 | **Published Scene animation vocabulary; integrated outcome missing.** | Rendering SVG-PRESENT-01/M6 |
| C12 | **Separate Audio producer capability exists; browser game outcome unqualified here.** | Audio/Rendering/Templates SVG-PRESENT-01/M6 |
| C13 | **Product-local persistence pieces exist; versioned SVG documents, IndexedDB recovery and independent migrations are missing.** | Rendering/Game/Templates SVG-PRESENT-01/M6 |
| C14 | **Published Harness trace/replay pieces; incomplete.** Worker cancellation, seek/checkpoint behavior and qualified deterministic export remain open. | Game/Templates SVG-REPLAY-01/M7 |
| C15 | **Missing generic qualified capability.** | Game/Templates SVG-REPLAY-01/M7 |
| C16 | **Public SDD tooling and specification evidence exist; no generic game rule catalog/explorer capability is qualified.** | Game/SDD/Templates SVG-REPLAY-01/M7 |
| C17 | **Public installed arena baseline; complete outcome open.** Template 0.10.0 has HTTP/SignalR and two-client behavior, but the later game/session/resync contract and saved replay journey remain M8. | Game/Net/Templates SVG-NETWORK-01/M8 |
| C18 | **Partial local automation.** The arena has an accessibility baseline and the SVG foundation has keyboard/pointer semantics, but its ARIA state mapping is wrong for button semantics and no actual assistive-technology or complete reflow matrix has passed. | Rendering SVG-SCENE-02.4–.6 bounded proof; SVG-SCALE-01/M9–M10 completion |
| C19 | **Missing accepted workload evidence.** Only a small Chromium foundation observation exists; it is not the ordinary/dense/idle/startup contract. | Rendering SVG-SCENE-02.6 bounded Preview-A budgets; SVG-SCALE-01/M9 completion |
| C20 | **Public baseline plus local candidate evidence.** SDD CLI 1.7.0 and Workspace.Template 0.10.0 are public; the SVG producer/template changes have only isolated local candidate/receiver evidence. | Templates SVG-SCENE-02.7 local handoff; SVG-PREVIEW-A publication/installed proof; SVG-WORKSPACE-01/M10–M11 completion |

## M0.1–M0.7 dispositions

| Work package | Disposition and evidence boundary |
|---|---|
| M0.1 donor/license and capability audit | The prior extraction report and Templates fixture provenance retain the copyright-owner authorization and separate third-party boundary. This run performed no donor access. New selected third-party inputs are inventoried below before any copy. **Recorded; later assets still require their own notices.** |
| M0.2 source/test characterization | Rendering #1279–#1281 and Templates #462 bind the portable contract, retained browser, canonical reducer correspondence and clean/retained receiver evidence. The exact gaps in the C ledger remain open. **Recorded, without promoting the bounded foundation fixture.** |
| M0.3 ownership/Scene/package decision | The accepted subroadmap keeps portable Scene/document data in `FS.GG.UI.Scene`, DOM/font/browser behavior in `FS.GG.UI.Scene.SvgBrowser`, generic session policy in Game, and generated/external consumers in Templates. It adds no package per logical module and forbids a Rendering-to-Game dependency. **Accepted.** |
| M0.4 SVG geometry/font spike | `polygon-clipping` 0.15.7 and Fontsource Noto Sans 5.3.0 are fixed below. Executed spike results cover a hole, coincident edge, degenerate input and 10,001 vertices. The library has no cooperative cancellation, so future M3 use requires a terminable authoring worker plus pre/post size limits. **Selected for later authoring only; absent from Preview-A executable JS.** |
| M0.5 browser/performance fixture specification | The manifest below freezes browsers, viewports, workloads, limits and the reference-host conditions. Existing evidence remains the small Chromium fixture. **Subjects frozen; measurements remain .5/.6.** |
| M0.6 candidate migration catalog | The staged foundation upgrader already performs bounded file adoption, authored-file preservation, collision refusal and rollback from source control/retained input. `fsgg.svg-document/1`, package/config transitions and independent data/replay/keymap migrations remain future work. **Catalogued, not executed for the new document.** |
| M0.7 installed compatibility census | Public SDD 1.7.0/profile-2 clean and retained receivers passed in SVG-QUAL-01. Profile-1 remains readable there. Retained F# v1, older SVG game, document/data/replay/keymap and later coordination-epoch refusal subjects remain open at their owning features. **Known subjects separated; no epoch gate applies to local source work.** |

## Package and tool identities

NuGet.org V3 was read on 2026-09-11. The SHA-256 values below are the downloaded public archive bytes;
NuGet repository signing can make them differ from pre-push candidate hashes. The earlier SDD release receipt
remains authority for its paired feed/payload qualification.

| Identity | Public/source fact | Downloaded archive SHA-256 or source |
|---|---|---|
| `FS.GG.UI.Scene` | Public latest `0.28.0`, source tag commit `6f0c46f5…`; it does not contain `RetainedSvg`. Current coherent UI pin is 0.28.0. | `3f858a774687fdec7f615cb8aed766bd81dc70fc35753be4feeffcb88e8c5445` |
| `FS.GG.UI.Scene.SvgBrowser` | Absent from NuGet.org; current source exists only after Rendering #1280. | no public archive |
| Rendering local `<Version>` | `0.4.0-preview.1` in `Directory.Build.local.props` is explicitly vestigial; the real framework release axis is `<FsGgUiVersion>` 0.28.0. | source `815783987…` |
| `FS.GG.Game.Core` | Public/current coherent Game axis `0.14.0`; its public Fable view omits `InputCommand`/session contracts. | `cb580bb8576a767124a58097df35f4b6ad021bc6f0c34784b8b51de0decfe4d7` |
| `FS.GG.Workspace.Template` | Public axis `0.10.0`; the public tag predates the SVG foundation commits. | `69cbed30447e6bd4d221e0ce78060c8245d2744fc3993b4c27368aeeb48d11c8` |
| `FS.GG.SDD.Cli` | Public exact installed authority `1.7.0`; preserve the SVG-QUAL-01 provisioned profile-2 receipts. | current NuGet archive `7b52d641af27aaf80a77ec158f7d781c5577808c89851d52230a73ab45a27f1c` |

Local candidate identities are assigned from these axes: Rendering's additive source window uses
`0.29.0-preview.1` for the coherent Scene/SvgBrowser pair, Game uses `0.15.0-preview.1` only if .2 adds its
public contract, and Templates uses `0.11.0-preview.1`. These are isolated candidate identities. Final
promotion, immutable pins, dual-feed bytes and installed public receivers remain SVG-PREVIEW-A.

## Current Scene boundary and required additions

`SvgRetained.project` currently accepts Empty, Group, Rectangle, PaintedRectangle, Circle, FilledEllipse,
Ellipse, Line, Path without ArcTo, Text, SizedText, Translate, sRGB wrappers, and paint with SrcOver plus no
shader or `Shader.SolidColor`. It validates finite coordinates, nonnegative radii/strokes, opacity, camera,
accessible labels and unique layer/object IDs.

It explicitly rejects Points, Vertices, Arc, TextRun, GlyphRun, Image, ClipNode, RegionNode, non-sRGB color,
PerspectiveNode, PictureNode, Chart, CachedSubtree, gradients, non-SrcOver blending, color/mask/image filters
and path effects. SVG-SCENE-02.2/.3 will add the accepted identified-document surface while retaining loud
rejection for raster/external resources, script/CSS/event handlers, `foreignObject`, filters, sweep gradients,
perspective, non-sRGB color, native glyph proof data, pictures/charts/regions and executable extensions.
Transparent cache wrappers may recurse through validated content but do not export cache behavior.

Rendering's `src/KeyboardInput/KeyboardInput.fsi` owns command-agnostic key state/keymaps and current raw-key
seams. Game's `src/Game.Core/InputCommand.fsi` owns six product intent cases; `Loop.fsi`/`FixedStep.fsi` own
deterministic advancement. `Game.Harness` owns journey/trace evidence. No generic session/snapshot/restore
envelope or editor transaction exists. .2 must add Rendering-free Game identities for initialize, input
admission, advance, projection, snapshot and restore, with a compiling Fable consumer and honest support
classification. .4 must add only minimal guarded scene/document edit, undo/redo, immutable play handoff and
semantic input reducers/adapters. It must reuse KeyboardInput and must not claim M3–M5 implementation.

## Third-party and browser subjects

| Input | Selected identity and provenance | Result/boundary |
|---|---|---|
| `polygon-clipping` | npm 0.15.7, MIT, tarball SHA-256 `23bdea671a65011107d91536881f527c8e2e6820f59e4bc1408a94d7cc11d5b7` | Installed-package spike: difference produced one polygon/two rings with areas 100/36; adjacent coincident-edge union produced one polygon and intersection none; collinear degenerate input produced none; 10,001 input/output vertices took 42.855 ms on the inventory host. Raw-tar direct import first failed because `splaytree` was absent; the supported npm-installed dependency graph passed. No cancellation API. Authoring worker only, later M3. |
| `@fontsource/noto-sans` | npm 5.3.0, OFL-1.1, archive SHA-256 `f62f8ed40e378f15134e32bed2fd5718d81e9ec7a5603d1022d7b385e2297abc`; LICENSE SHA-256 `54ec7b5a35310ad66f9f3091426f7028484cbf9ae1ab5da30122ee412a3009e1` | Selected file `files/noto-sans-latin-400-normal.woff2`, SHA-256 `09aee8065d25508f23a4c3d92cd777ac869c52d93fd868a88f025d888a7937d6`. Self-host only; carry OFL notice. |
| `playwright-core` | npm 1.62.1, Apache-2.0; package lock SHA-256 `6e07fd6e03811c90a92be0966f86d5e2aee79c4b6c229ee66084cde32ff0559f` | Chromium/Headless Shell revision 1234, Chrome 151.0.7922.34; Firefox revision 1538/version 153.0; WebKit revision 2336/version 26.5. |
| Vite | npm 8.1.5, MIT | Existing Rendering browser-fixture build server; loopback preview with deterministic static candidate bytes. |

### Frozen browser/workload manifest

- Reference: Linux x86_64 Arch build `20260906.0.587075`, kernel `7.2.2-arch1-1`, Podman container on
  AMD Ryzen 9 7900 (12 cores/24 threads), 66,509,987,840 bytes RAM, no swap, headless software/browser
  rendering. Run benchmarks sequentially with all 24 CPUs available, CPU governor `powersave`, no concurrent
  repository build, and start/end one-minute load at most 1.0. Record load, browser executable hash and actual
  browser version per run. AC state is unavailable in the container and is recorded as unavailable.
- Serving/cache: Vite 8.1.5 static preview over loopback. Startup runs use a new browser context and empty HTTP
  cache; interaction runs warm fixture bytes during five discarded warm-ups.
- Functional matrix: Chromium, Firefox and WebKit at 1280×720/DPR1, 800×600/DPR2 and touch-emulated
  390×844; 320 CSS-pixel reflow and 400% zoom. Touch emulation is not physical-mobile evidence. One desktop
  Chromium journey uses actual assistive technology; automated role inspection cannot replace it.
- Ordinary: 100 visible semantic objects/four layers, each with shape, 12-segment path and short label, sharing
  gradients/symbols; three runs and at least 200 samples, p95 input-to-presentation at most 100 ms.
- Dense: 200 equivalent objects plus 20 clip/mask groups and selection overlay; same sampling, p95 at most
  150 ms. Gallery: 200×256-segment paths, 64 gradients, 32 masks, 100 text runs and symbols; fidelity and
  staged timing only.
- Idle: stable ordinary scene for ten seconds, zero scheduled frames/rebuilds. Interaction: 100 selections,
  100 camera changes and one-object revisions with retained unaffected DOM identities. Lifecycle: 100
  mount/update/dispose cycles with no owned listeners, frames, observers, font handles or roots.
- Limits before DOM mutation: 4 MiB serialized document, 10,000 nodes, 100,000 path segments, 512 definitions,
  32 nesting/reference levels and 50,000 expanded symbol nodes; cycles fail independently.
- Startup: minimal grid/continuous executable JS/CSS at most 150 KiB gzip, font/content bytes separate, first
  usable interaction under two seconds. Callback-to-rAF is never substituted for unavailable presentation
  timing; heap and compositor evidence remain explicitly unavailable when the browser cannot provide them.

## Template and migration inventory

The selected template remains `FS.GG.Workspace.Template.FableGame`, short name/provider template ID
`fs-gg-fable-game`/`fable-game`. `--svgFoundation` defaults false. `--lifecycle` keeps
`none|sdd|typed-sdd|spec-kit` with `sdd` default. The provider is not registry-active. Fresh candidate content
first changes only in .7. The ordinary arena and omitted-provider behavior remain unchanged.

The existing staged adopter manages seven `SvgFoundation/` files, compares the retained baseline before any
write, refuses collisions, and relies on retained input/source control for rollback. .7 must extend that
bounded manifest for the versioned document, font/license and candidate package/config delta, preserve authored
lifecycle/skill content, and prove rollback without destructive scaffold rerun. Profile/lifecycle tokens,
F# manifest v1, profile-1/profile-2 models, SVG documents, product saves/replays and keymaps remain separate
migration subjects. Unsupported versions, stale bindings, wrong tools/profiles, collisions and interrupted
adoption must refuse without partial semantic change.

The current template pins Fable.Core 5.2.0, Fable.Browser.Dom 2.20.0, Fable.Elmish 5.0.2 and tool Fable
5.13.0. Its general client remains on Vite 7.1.3/Playwright 1.55.0, while the SVG qualification fixture uses
the frozen Rendering 8.1.5/1.62.1 pair. .7 must consume isolated local candidate packages and keep the
authoring-only geometry package out of the minimal browser graph.

## Exit and open prerequisites

This inventory closes only SVG-SCENE-02.1. The next source work is .2's additive Rendering document/affine
contract and Game session envelopes. No package was published, installed default changed, or Release A claim
made. The actual assistive-technology journey, three-browser matrix, dedicated measurements, producer/template
publication and public installed receivers remain at their named later boundaries.
