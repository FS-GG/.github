# ADR-0028: Keyboard input-config boundary — mechanism (Rendering) vs policy (Game)

- **Status:** Accepted
- **Date:** 2026-07-10
- **Affects:** Rendering, Game, .github (coordination)

## Context

Epic [FS.GG.Rendering#330](https://github.com/FS-GG/FS.GG.Rendering/issues/330) wants a
fully data-driven keyboard configuration for FS-GG products — a **default keymap**, an
in-app **rebind/config screen**, and runtime **configuration changes** — so that
"rebinding" means editing data, not product code.

Two forces pull on where that machinery lives:

- **The one-way dependency rule (ADR-0022).** Dependencies run downstream → upstream
  only. `FS.GG.Game.Core` reaches up to **nothing**; `FS.GG.Game.Render` may reach **up**
  to Rendering (the existing `game-scene-adapter` edge). Rendering never reaches **down**
  to Game. Any design in which Rendering must *know a game action* would force a
  `rendering → game` edge — the forbidden direction — and is therefore off the table.
- **Reuse vs vocabulary.** The keymap *mechanism* (a keymap-set type, rebind ops, resolve,
  conflict detection, serialization, the config-screen widget, live-dispatch wiring) is
  generic and reusable across every product. The *policy* (which abstract commands exist,
  and the default command→key map) is specific to one game.

Current state (verified 2026-07-10, per the epic):

- Rendering's `FS.GG.UI.KeyboardInput` already ships raw keys/chords/modifiers, host-OS
  capture, and an MVU reducer holding a `KeyboardBinding list` — but there is **no
  keymap-set type, no conflict detection, no serialization, and no config screen**, and
  the reducer's bindings are **not wired into the live dispatch path** (the live path uses
  a hand-written `MapKey`/`MapKeyChord`). So today "rebinding" means editing product code.
- Game has **no** input/command/keymap concept at all — a pure sim substrate plus a
  draw-only Scene adapter.

This ADR fixes the boundary the mechanism children (Rendering P1) and the policy children
(Game P6) build against. It must be **ratified before** the registry edge — the contract
child [.github#365](https://github.com/FS-GG/.github/issues/365), which registers the
`keyboard-input` contract and the `Game.Render → FS.GG.UI.KeyboardInput` edge — lands.

## Decision

1. **Mechanism is Rendering-owned and command-id-agnostic**, in `FS.GG.UI.KeyboardInput`.
   Rendering owns: the keymap-set type, rebind operations, binding resolution, conflict
   detection, serialization/persistence, the config-screen widget, and the wiring that
   makes bindings drive live dispatch. None of this names a game action.

2. **The keymap is the data that crosses the boundary.** A keymap is a mapping from an
   **abstract command id** to a key/chord binding. The command id is an **opaque token**
   to Rendering — Rendering stores it, resolves against it, serializes it, and shows it in
   the config screen, but never interprets *which* command it is. Rendering's machinery is
   generic over the command id.

3. **Policy is Game-owned.** The abstract **command vocabulary** lives in `FS.GG.Game.Core`
   (pure — the command ids as values, reaching up to nothing). The **default keymap** lives
   in `FS.GG.Game.Render`, which hands Rendering a keymap as **data**.

4. **The dependency edge runs `FS.GG.Game.Render → FS.GG.UI.KeyboardInput`** — downstream
   (Game) → upstream (Rendering), the same direction as the existing `game-scene-adapter`
   edge and consistent with ADR-0022. **Rendering never gains an edge to Game, and never
   learns a game action.**

5. **The registry edit is not this ADR's job.** The `keyboard-input` contract row and the
   `Game.Render → KeyboardInput` edge are registered by the contract child
   [.github#365](https://github.com/FS-GG/.github/issues/365), which also reconciles the
   architecture map. This ADR ratifies the boundary those edits encode; sequencing is
   ADR → contract child.

## Consequences

- **Rendering (mechanism, P1)** must build `FS.GG.UI.KeyboardInput`'s keymap machinery with
  **no `FS.GG.Game` reference** and no game-action enum baked in — the command id stays an
  opaque token throughout the type, the conflict detector, the serializer, and the config
  widget. Rendering must also **wire the reducer bindings into the live dispatch path**,
  retiring the hand-written `MapKey`/`MapKeyChord` route so that rebinding is data, not
  code. R1 (the keymap type) is the critical path and can start against this boundary now.
- **Game (policy, P6)** must define the command vocabulary in `FS.GG.Game.Core` (pure) and
  the default keymap in `FS.GG.Game.Render`, handing Rendering a keymap as data over the
  edge in (4).
- **.github (coordination)** lands the contract child #365 (registry `keyboard-input` + the
  `Game.Render → KeyboardInput` edge) **after** this ADR, and keeps the architecture map
  coherent as part of *that* change — this ADR does not touch the registry or the map.
- **Trade-off — Rendering cannot validate command ids.** Because the command id is opaque,
  Rendering cannot check that a keymap references *real* game commands; that validation is
  Game's, since `FS.GG.Game.Core` owns the vocabulary. This is the deliberate price of the
  one-way rule: the alternative — teaching Rendering the game's actions — is exactly the
  `rendering → game` edge ADR-0022 forbids.
- **Serialization is mechanism, but token-blind.** The persistence format is Rendering-owned
  and must serialize the opaque command-id token verbatim, so a keymap round-trips without
  Rendering understanding what any command means.
