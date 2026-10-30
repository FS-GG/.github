# GAME-RENDER-MULTI-01 — Elmish-switchable SVG, PixiJS and BabylonJS game rendering

Date: 2026-10-30. Status: active; planned and enrolled on 2026-10-30, source preparation started on
.1 and on the .4 switching core. Nothing is published, adopted or accepted yet.

This is the owning plan for the Outcome [FS-GG/.github#4930](https://github.com/FS-GG/.github/issues/4930).
Root/fdev owns intake, decomposition, integration and Outcome acceptance. Each Delivery has its own
source owner, named on its issue, and its own independent reviewer. The plan extends the
[staged Fable game adoption plan](2026-10-01-staged-fable-game-adoption.md) and the
[Fable bindings toolchain](https://github.com/FS-GG/FS.GG.Templates/blob/main/docs/fable-bindings-toolchain.md).
It creates no competing product-adoption sequence and changes no existing backend's behavior.

## Promise and acceptance

The text below is copied verbatim from the Outcome's Done when.

User request (2026-10-30, verbatim intent):
1. Update the Fable binding creation machinery.
2. Create current PixiJS (https://pixijs.com/) bindings.
3. Update the BabylonJS bindings.
4. Update the game rendering section to use Elmish, so that the renderer can be switched between SVG, Pixi and Babylon whenever an adapter or renderer exists for that game-state type.

Acceptance:
- **Machinery.** The maintained FS.GG Fable binding toolchain (the FS.GG.Templates `fs-gg-fable-bindings` workflow, its Glutinum, Xantham, Fable, SDK and Node pins, and the lock/drift/curation/runtime/consumer evidence rules in `docs/fable-bindings-toolchain.md`) runs on current supported tool versions. It is re-qualified on the existing Babylon corpus.
- **PixiJS bindings.** Curated Fable bindings for the current PixiJS major (exact npm artifact and `.d.ts` closure locked and hashed) are produced through that machinery. They have compile, import-resolution, Node or browser runtime and drift evidence, and are consumable as a package.
- **Babylon bindings.** The Babylon bindings are moved from the pinned `@babylonjs/core@9.19.0` spike to the current Babylon release through the same machinery, with the same evidence classes.
- **Game rendering.** `FS.GG.Game` `src/Game.Render` exposes an Elmish-driven renderer selection. It has a registry of renderer adapters keyed by game-state type, and the available backends (SVG, Pixi, Babylon) are offered only when an adapter exists for the current state type. Switching happens at runtime through an Elmish message without losing game state. The existing SVG path keeps working unchanged as the default.
- At least one shared game state renders through all three backends in a browser test.
- Published packages, template adoption and any FourD or product adoption remain explicit later scope unless a child accepts them.

## Deliveries

| Delivery | Owner repository | Promise | Depends on |
|---|---|---|---|
| [.1 — Update the Fable binding creation machinery](https://github.com/FS-GG/FS.GG.Templates/issues/807) | Templates | The `fs-gg-fable-bindings` toolchain runs on current supported .NET SDK, Node/npm, Fable, Fable.Core, Glutinum and the Xantham candidate path, keeping its lock, curation and evidence rules, and is re-qualified on the existing Babylon corpus (`spikes/fable-bindings`). It reconciles with the open FBX-07 Xantham watch items rather than duplicating them. | None |
| [.2 — Current PixiJS Fable bindings](https://github.com/FS-GG/FS.GG.Templates/issues/808) | Templates | Curated bindings for the current PixiJS major, with a locked and hashed npm artifact and `.d.ts` closure, covering enough for a 2D game renderer. Compile, import-resolution, runtime and drift evidence, and consumable by Game as a package or project reference. | .1; corpus selection can start before .1 lands |
| [.3 — Update BabylonJS Fable bindings](https://github.com/FS-GG/FS.GG.Templates/issues/809) | Templates | Move the Babylon bindings from the pinned `@babylonjs/core@9.19.0` spike to the current Babylon release through the updated machinery, with the same evidence classes, covering what a 3D game renderer needs and consumable by Game. | .1 |
| [.4 — Elmish renderer selection in Game.Render](https://github.com/FS-GG/FS.GG.Game/issues/730) | Game | `Game.Render` exposes an Elmish model and message for the selected backend and a registry of adapters keyed by game-state type. Only backends with an adapter for the current state type are selectable, switching at runtime keeps game state, and the SVG adapter remains the default and unchanged. Pixi and Babylon adapters for at least one shared game state, and a browser test proving all three render and switch. The public surface baseline is updated deliberately. | Switching core, registry and SVG adapter: none. Pixi and Babylon adapters: .2 and .3. Three-backend browser test: both adapters |

## Sequencing

1. .1 comes first because it unblocks .2 and .3. .2 and .3 can then proceed in parallel.
2. .4 starts now with the switching core, registry and SVG default against the existing SVG adapter.
   It needs only the adapter interface.
3. The Pixi and Babylon adapters in .4 follow the .2 and .3 bindings. The shared-state browser test
   that renders and switches through all three backends is the last step, and the Outcome's
   three-backend acceptance depends on it.
4. Outcome acceptance is evaluated by Root only after every Delivery's own acceptance is recorded.
   Source delivery of one Delivery does not establish another's acceptance.

## Current state

- .1 ([Templates#807](https://github.com/FS-GG/FS.GG.Templates/issues/807)): source preparation in progress. No Templates pull request is open yet.
- .2 and .3: blocked on .1.
- .4 ([Game#730](https://github.com/FS-GG/FS.GG.Game/issues/730)): the switching core is in review in
  [Game#731](https://github.com/FS-GG/FS.GG.Game/pull/731), which references #730 without closing it. The
  Pixi and Babylon adapters and the three-backend browser test remain open.
- Native hierarchy: the four Deliveries are native children of #4930. Project 3 enrollment and its planning fields
  are tracked on the issues and the board, not in this plan.

## Out of scope unless a child accepts it

- Package publication of the new or updated bindings and of any Game change.
- Template adoption, including the `fable-game` reference template.
- FourD or any other product adoption, and any change to a product's default renderer.
- Backends other than SVG, PixiJS and BabylonJS.
- Changes to existing SVG behavior, which must stay unchanged.

Each of these needs its own selected Delivery and its own acceptance evidence. Home is not required for any
Delivery in this plan.
