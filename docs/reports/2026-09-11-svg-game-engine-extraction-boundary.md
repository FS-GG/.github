# SVG game engine extraction boundary

Audit identity: **SVG-FOUND-01.1**. Inspected 2026-09-11 from clean default-branch checkouts.

This audit fixes the first implementation boundary for the SVG game engine foundation. It authorizes no
publication or default change. Later work must refresh a repository revision if it moves before that repository's
implementation begins.

## Inspected owners and revisions

| Owner | Revision | Binding source |
|---|---|---|
| Rendering | [`86102999e7f60a494bed74e825e36b284fef6d62`](https://github.com/FS-GG/FS.GG.Rendering/commit/86102999e7f60a494bed74e825e36b284fef6d62) | [`src/Scene`](https://github.com/FS-GG/FS.GG.Rendering/tree/86102999e7f60a494bed74e825e36b284fef6d62/src/Scene), [`src/KeyboardInput`](https://github.com/FS-GG/FS.GG.Rendering/tree/86102999e7f60a494bed74e825e36b284fef6d62/src/KeyboardInput) |
| Game | [`24f79084fdd289f34387f91b1d4398c78fde16eb`](https://github.com/FS-GG/FS.GG.Game/commit/24f79084fdd289f34387f91b1d4398c78fde16eb) | [`FS.GG.Game.Core.fsproj`](https://github.com/FS-GG/FS.GG.Game/blob/24f79084fdd289f34387f91b1d4398c78fde16eb/src/Game.Core/FS.GG.Game.Core.fsproj), [`InputCommand.fsi`](https://github.com/FS-GG/FS.GG.Game/blob/24f79084fdd289f34387f91b1d4398c78fde16eb/src/Game.Core/InputCommand.fsi) |
| Templates | [`61091078337689c6ab1aac139bc03f6a07ca8f99`](https://github.com/FS-GG/FS.GG.Templates/commit/61091078337689c6ab1aac139bc03f6a07ca8f99) | [`fs-gg-fable-game`](https://github.com/FS-GG/FS.GG.Templates/tree/61091078337689c6ab1aac139bc03f6a07ca8f99/templates/fs-gg-fable-game), [`fable-game.providers.yml`](https://github.com/FS-GG/FS.GG.Templates/blob/61091078337689c6ab1aac139bc03f6a07ca8f99/providers/fable-game.providers.yml) |
| S.I.R. | [`80e1ac9328865ec8d1ee3eeea130560ef22b1b01`](https://github.com/EHotwagner/S.I.R./commit/80e1ac9328865ec8d1ee3eeea130560ef22b1b01) | [`TacticalSceneProjection.fsi`](https://github.com/EHotwagner/S.I.R./blob/80e1ac9328865ec8d1ee3eeea130560ef22b1b01/src/SIR.Client/TacticalSceneProjection.fsi), [`App.fs`](https://github.com/EHotwagner/S.I.R./blob/80e1ac9328865ec8d1ee3eeea130560ef22b1b01/src/SIR.Client.Web/App.fs), [`SceneAdapters.fs`](https://github.com/EHotwagner/S.I.R./blob/80e1ac9328865ec8d1ee3eeea130560ef22b1b01/src/SIR.Client.Web/SceneAdapters.fs), [`TacticalUnitSymbolView.fs`](https://github.com/EHotwagner/S.I.R./blob/80e1ac9328865ec8d1ee3eeea130560ef22b1b01/src/SIR.Client.Web/TacticalUnitSymbolView.fs) |
| SDD | [`8d648c8deaf1edc16b942d0cfccee722c3a0a24c`](https://github.com/FS-GG/FS.GG.SDD/commit/8d648c8deaf1edc16b942d0cfccee722c3a0a24c) | [`typed-sdd-lifecycle.md`](https://github.com/FS-GG/FS.GG.SDD/blob/8d648c8deaf1edc16b942d0cfccee722c3a0a24c/docs/typed-sdd-lifecycle.md) |

These assignments preserve ADR-0022 and ADR-0028. Rendering owns reusable scene and device-input mechanisms.
Game owns simulation and abstract game-command policy and keeps no Rendering dependency. Templates composes
published producer artifacts. S.I.R. owns combat, disclosure and its tactical adapter. SDD owns model tooling.

## Selected scene and input cut

The portable surface will reuse `FS.GG.UI.Scene` values rather than introduce a second geometry vocabulary.
Its retained wrapper is a Rendering-owned additive contract over:

- a stable root identity and monotonically changing revision identity;
- ordered layers with stable identities and visibility;
- semantic objects with stable identities, selectable state, an accessible label, and one `Scene` subtree;
- a camera transform containing finite pan X/Y and positive finite zoom; and
- explicit adapter results: rendered, unsupported, or invalid, with the object path and reason.

The first SVG adapter subset is `Empty`, `Group`, `Rectangle`/`PaintedRectangle`, `Circle`,
`FilledEllipse`/`Ellipse`, `Line`, `Path` using `MoveTo`, `LineTo`, `QuadTo`, `CubicTo` and `Close`, `Text`,
`SizedText`, and `Translate`. Paint is limited to solid fill, stroke width/cap/join/miter and opacity under
`SrcOver`. These cover the grid and free-coordinate fixtures while keeping the translation exact.

The first adapter explicitly rejects `Points`, `Vertices`, `Arc` and `ArcTo`, `TextRun`, `GlyphRun`, `Image`,
clips, regions, non-sRGB color spaces, perspective, pictures, charts, cached subtrees, gradients, non-`SrcOver`
blend modes, color/mask/image filters and path effects. No case may disappear or silently approximate. A later
milestone can add a rejected case with its own mapping and evidence.

Rendering's `FS.GG.UI.KeyboardInput` supplies raw-key normalization and command-id-agnostic keymap resolution.
The shared interaction reducer will own semantic select, focus-next/previous, pan and zoom messages plus pointer
capture state. Browser pointer/wheel normalization stays at the SVG adapter edge. A product maps opaque command
ids to its own policy. This reuses the accepted mechanism/policy split and does not widen Game.Core's Fable
profile or teach Rendering a game command.

The current `Scene` record has neither retained object identities nor root/layer/revision metadata, and its only
scene transform node is translation. Those are demonstrated gaps for the additive wrapper; changing the existing
`SceneNode` union is unnecessary for this foundation.

## Package and browser closure

`FS.GG.UI.Scene` currently has no explicit package or project reference, but the project compiles all scene,
codec, hashing, inspection, text and animation files as one assembly. It has no `Fable.Package.SDK` source view.
Several files use `System.IO`, cryptography and other runtime APIs, so the .NET package's dependency-light status
does not prove a browser source closure.

SVG-FOUND-01.2 must therefore prove a producer-owned curated Fable view containing only the selected scene types,
constructors and retained SVG contract, or split an equally owner-controlled portable project if curation cannot
form a closed compile graph. Its isolated consumer must restore a packed candidate and compile without sibling
source links, Skia, `FS.GG.UI.SkiaViewer`, or `FS.GG.UI.Controls.Elmish`. The DOM adapter may depend on Fable browser
packages at the consumer edge; the shared scene/reducer package may not.

Game.Core remains unchanged. Its current `fable/` package view deliberately contains only `Primitives`,
`Pathfinding`, `Edges` and `Los`, with profile identity `fs-gg-game-core-fable-lockstep-v1`. `InputCommand`,
floating-point geometry, fixed-step and other .NET surfaces are outside that view. The SVG foundation needs none
of them in its portable package.

## Consumer fixture routes

| Fixture | Route into the same public surface | Required proof in the dependent milestone |
|---|---|---|
| Grid | The `fs-gg-fable-game` client maps its authoritative room snapshot cells and path preview to stable scene objects: rectangles/lines for the board and path, circles/text for players, and opaque command ids for selection. | Isolated generated client compiles against packed candidate packages; stable root/layer/object identities survive a revision, and pointer plus keyboard select the same object. |
| Free coordinate | A neutral Fable fixture constructs fractional `Point`/`Rect` objects directly, using a circle, cubic path, line and label under pan/zoom without Game.Core. S.I.R. then maps `SharedSceneProjection` presentation coordinates and revision identity through a product-owned adapter. | Neutral and S.I.R. adapters produce the same retained contract; the neutral fixture proves no grid assumption, and S.I.R. retains disclosure and gameplay ownership. |

S.I.R.'s production SVG is characterization evidence: one focusable application root, title/description,
root/layer/revision data, camera transform, pointer capture, wheel zoom, keyboard dispatch, semantic selection and
stable primitive keys. Its full editor, overlay, raster background, symbol and tactical rules are outside the
first reusable subset.

## Current template identities and commands

The actual template owner records package `FS.GG.Workspace.Template` **0.10.0**, template identity
`FS.GG.Workspace.Template.FableGame`, short name `fs-gg-fable-game`, provider `fable-game`, and provider source
`FS.GG.Workspace.Template::0.10.0`. The org registry records both source and feed at 0.10.0 and the wizard
`FS.GG.NewSddWorkspace` at 0.11.1; the provider file's older “not registry-active” comment is stale prose.

Baseline creation and build commands are:

```console
dotnet new install FS.GG.Workspace.Template
dotnet new fs-gg-fable-game --name SvgGameFixture
./build.sh
```

The generated development route is `dotnet run --project Server/Server.fsproj` plus
`npm run dev --prefix Client`. The client pins Fable.Core 5.2.0, Fable.Elmish 5.0.2,
Fable.Browser.Dom 2.20.0, Thoth.Json 10.5.1, `@microsoft/signalr` 10.0.0 and Vite 7.1.3; it consumes
`FS.GG.Game.Core` 0.13.0. These are baseline identities, not the future SVG package version.

## SDD/profile observation

The installed `fsgg-sdd` is **1.6.0** and recognizes `quint-specification-v1` with
`fsgg-quint-profile/2`. An isolated profile-2 author command against an empty content-addressed cache exited 1
with `typedSdd.v2.cacheInvalid` for both `lmt-binary` and `quint-binary`. The ambient `quint` reports 0.32.0 but
has SHA-256 `ac12595b1cb7253feec93c79417615c6eb20fc3b6a3df35c5e3530b24e90a501`, not the profile's required
`939b64095b706017f2f202c6f99c860c40be7c31bddc2b98557316e50f42cd7f`; no `lmt` executable is installed.

The owning seam is exact-tool provisioning for the SDD profile cache. SVG-FOUND-01.3 may model the bounded
stateful reducer only after the required Quint 0.32.0 and lmt objects are preseeded and the installed CLI authors
and inspects the selected profile successfully. This gap does not block SVG-FOUND-01.2's pure contract and package
closure.

## S.I.R. provenance disposition

The S.I.R. repository is AGPL-3.0. The user states that they own the S.I.R. copyright and, on 2026-09-11,
authorized this programme to reuse their code without AGPL conditions. History for the named donor source and
measurement paths reports only `EHotwagner <ehotwagner@gmail.com>` as author. This records the supplied owner
authorization; it does not relicense the S.I.R. repository or determine rights in third-party material.

Third-party package implementations are dependencies, not donor code. S.I.R.'s web edge references Fable,
Feliz, Elmish, Thoth, React, SignalR, Vite and Playwright packages. No dependency source, generated package bytes,
font, PNG, SVG, raster background or other asset is approved for copying by this audit. The reusable extraction
may copy only the identified owner-authored F# behavior, or reimplement it from the contract. Any later need for
a dependency or asset must record its exact upstream license and required notices first.

The focused static M0 fixture passed with 36 registry commands, 252 modal ids, 14 contextual namespaces,
33 render/control tokens, 11 selection/focus tokens and 19 responsive tokens. The SVG pipeline measurement
contract test could not run because the local npm closure lacks `playwright/package.json`. Installing the locked
S.I.R. npm closure is therefore a prerequisite for later donor-performance characterization; no performance
number is inferred from the unavailable run.
