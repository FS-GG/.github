# ADR-0024: Wire FS.GG.Audio into the game/sample-pack scaffold profile

- **Status:** Accepted
- **Date:** 2026-07-07
- **Affects:** **FS.GG.Rendering** (owner of the `fs-gg-ui` template — adds the `$(FsGgAudioVersion)` axis + 4 pins/refs under the game/sample-pack gate; and the donor that must retire `FS.GG.UI.Canvas.Audio` — a breaking surface change + coherent-set release), **FS.GG.Audio** (the producer; `FS.GG.Audio.Core` becomes the platform's single audio request vocabulary), **FS.GG.Game** (owner of the record-only `fs-gg-audio` skill — re-points its taught surface `Canvas.Audio` → `FS.GG.Audio.Core` + regenerates the skill-manifest sha256), **.github** (this ADR + the later `fs-gg-audio` package **consumer edge** in `registry/dependencies.yml` + compatibility projection + architecture-map reconcile)

## Context

**FS.GG.Audio is the platform's seventh component** — `v0.1.0-preview.1` live on the org
feed as four packages (`Core` / `Host` / `Engine` / `Elmish`), the standalone host-side
realization of the pure `AudioEffect` edge (named buses, fades/ducking, 3D, a device
backend that degrades to a headless Null/record path). Its registration (roster row,
`fs-gg-audio` **package** contract, architecture map 6→7) is the prerequisite decision —
reserved as **ADR-0023** and tracked by the epic's "register FS.GG.Audio" child; it must
land before the edges this record decides can be flipped.

Today a scaffolded `game` / `sample-pack` product only materializes the **record-only
`fs-gg-audio` skill** (surface in `FS.GG.UI.Canvas`); the game TestSpecs (flappy-bird §10,
metroidvania §10) explicitly say *"a real playback backend is deferred."* The design report
[`FS.GG.Game/docs/reports/2026-07-05-game-audio-library-architecture.md`](https://github.com/FS-GG/FS.GG.Game/blob/main/docs/reports/2026-07-05-game-audio-library-architecture.md)
designs that missing realization. This ADR records the decision to ship it — the **first
consumer edge** of the `fs-gg-audio` package contract. Draft wiring: FS.GG.Rendering#156.

Three forces shape the decision:

- **The design report predates the repo split, and reality diverged from it.** The report
  proposed `FS.GG.UI.Audio` as a *host-side library under Rendering* depending on Canvas.
  What actually shipped is a **standalone `FS.GG.Audio` repo** (a bottom-graph sibling, per
  the ADR-0022 extraction pattern) whose `Core` reaches up to nothing. The realization is
  therefore referenced *from the outside*, not vendored into Rendering. This ADR records the
  as-built shape and supersedes the report's placement recommendation.

- **The audio surface was COPIED, not moved — and the two copies have already diverged**
  (surfaced by a code finding; blocking prerequisite FS.GG.Rendering#158).
  `FS.GG.Audio.Core/Audio.fs` is labelled *"extracted verbatim from FS.GG.UI.Canvas.Audio,
  behavior byte-parity"* — but `FS.GG.UI.Canvas.Audio` was **never removed from Rendering**,
  and `Core.AudioEffect` has since grown `PlaySfx3D` / `SetBusVolume` / `Duck` and
  independently re-defines `SoundId` / `TrackId` / `AudioEvidence` / `Bus`. Because
  `FS.GG.Audio` never depends on `FS.GG.UI`, these are **two distinct F# types in two
  assemblies that do not interoperate**: a game emitting `Canvas.AudioEffect` cannot feed
  `FS.GG.Audio.Engine`, which wants `Core.AudioEffect`. Naïvely adding the audio packages to
  the template (as the draft does) would put *both* vocabularies on every scaffolded game —
  ambiguous `AudioEffect` / `SoundId` / `Audio.playSfx` on `open`, and the game *still*
  wouldn't reach the real engine. The template edge cannot land coherently until this is
  reconciled.

- **The `fs-gg-audio` id names two things at two layers.** There is a record-only
  `fs-gg-audio` **skill** (`registry/skills.yml`, owned by FS.GG.Game) *and* the
  `fs-gg-audio` **package** contract (`registry/dependencies.yml`). The layering model
  (ADR-0001) keeps skills and dependency contracts in separate registries, so the shared id
  must be a deliberate same-capability-two-layers pair, not an accidental collision.

The one-way dependency rule (`architecture.md` §2) and house style (`.fsi`-as-sole-surface
with committed baselines, `net10.0` + FSharp.Core `10.1.301`, locked restore, deterministic
builds, degrade-to-zero) must survive all three.

## Decision

1. **Ship the real realization by pinning all four FS.GG.Audio packages** — `Core`, `Host`,
   `Engine`, `Elmish` — into scaffolded **`game`** and **`sample-pack`** products. All four,
   not a subset: `Core` carries the vocabulary, `Host`/`Engine` the device + mixing backend,
   `Elmish` the MVU wiring a game product already uses. The Null/record backend keeps
   headless CI and snapshot tests deterministic (`AudioEvidence`, never samples).

2. **Inject from the Rendering `fs-gg-ui` template, on its own `$(FsGgAudioVersion)` axis** —
   *not* the scaffold provider and *not* `new-sdd-workspace`. This mirrors the
   `FS.GG.Game.Core` precedent (ADR-0022 P5) exactly: a dedicated version property in
   `template/base/Directory.Packages.props`, independent of `$(FsGgUiVersion)` /
   `$(FsGgGameVersion)`, with the pins and `PackageReference`s gated
   `#if (profile == "game" || profile == "sample-pack")`. Rationale: the template is the
   single sole scaffolder (ADR-0016); an independent axis lets audio version on its own
   cadence without forcing a UI/Game bump; the provider and `new-sdd-workspace` stay
   composition-only and gain the packages for free through the template. This creates a new
   **rendering-template → audio** dependency edge — a standalone sibling package referenced
   from the template (`new`, additive to the graph).

3. **Reconcile the two audio surfaces by *completing the extraction* — Option (a).** Retire
   `FS.GG.UI.Canvas.Audio` from Rendering and re-point everything to `FS.GG.Audio.Core` as
   the platform's **single** audio request vocabulary:
   - **Rendering** removes `src/Canvas/Audio.fs` + `.fsi` (a **breaking** public-surface
     change → ApiCompat gate + a preview/major bump + a coherent-set release).
   - **FS.GG.Game** re-points the `fs-gg-audio` skill (`SKILL.md` + `docs/api-surface/…`)
     from `Canvas/Audio.fsi` to `FS.GG.Audio.Core`, and regenerates the skill-manifest
     sha256.
   - **The game template** references `Core.AudioEffect` only.

   **Rejected:** (b) *alias* `Canvas.Audio` → `Core` — introduces a `Rendering → FS.GG.Audio`
   dependency that **inverts the one-way layering** (Rendering must depend on no other FS-GG
   component); (c) *adapter* `Canvas.AudioEffect → Core.AudioEffect` — keeps the duplication
   and the ongoing drift risk that created this problem. Both are strictly worse than one
   vocabulary. Extraction is executed under **FS.GG.Rendering#158** and **must land before**
   the template edge (#156).

4. **Keep the shared `fs-gg-audio` id — it names one capability at two layers.** No rename.
   The **skill** row (`registry/skills.yml`) is the *teaching* surface; the **package**
   contract (`registry/dependencies.yml`) is the *shipped-realization* edge. Both rows carry
   a cross-note declaring the pairing intentional, and — once decision (3) lands — the skill
   teaches `FS.GG.Audio.Core` (the same vocabulary the package ships), closing the last place
   the two surfaces could drift.

## Consequences

- **Ordering (publish-before-flip, FR-007).** This ADR is the decision layer only; execution
  is sequenced on the Coordination board under epic .github#234:
  1. FS.GG.Audio component registration lands (**ADR-0023** / the "register FS.GG.Audio"
     child): roster row, `fs-gg-audio` package contract in `registry/dependencies.yml`,
     architecture map 6→7.
  2. **FS.GG.Rendering#158** completes the extraction (Canvas.Audio retired) — a Rendering
     coherent-set release (`FS.GG.UI.Template` + the audio-adjacent Canvas major).
  3. FS.GG.Rendering#156 wires the template pins/refs; Package.Tests fixtures + the
     generated-product gate learn the new per-profile package set.
  4. FS.GG.Game re-points the `fs-gg-audio` skill body + regenerates the manifest sha256.
  5. **.github** flips the `fs-gg-audio` **consumer edge** in `registry/dependencies.yml`
     (rendering-template → audio @0.1.0-preview.1, issue .github#238), prepends a dated
     `registry/CHANGELOG.md` entry, and updates the `docs/registry/compatibility.md`
     projection — validated by `fsgg-sdd registry validate` and the `contract-coherence`
     gate.

- **Breaking Rendering change.** Retiring `Canvas.Audio` breaks any consumer of that public
  surface; the ApiCompat gate will (correctly) fail-closed until the version bumps. Scoped
  and owned by #158; acceptable while the platform is at `-preview`.

- **CI feed availability.** The generated-product restore needs FS.GG.Audio on a feed the
  gate reads. The **nuget.org dual-publish** (ADR-0012/0013) is now **live** via the org
  Trusted Publishing policy `fs-gg-audio-publishing` (OIDC, no stored key), so `fs-gg-audio`
  is restorable from nuget.org and CI restore of a scaffolded game no longer fails closed on
  feed availability.

- **Design-report reconciliation.** The report's `FS.GG.UI.Audio`-under-Rendering placement
  is **superseded** by the as-built standalone `FS.GG.Audio` repo; the report's *backend /
  effects / asset* design (OpenAL via Silk.NET, NVorbis decode, CC0 starter pack, the
  `IAudioBackend` seam, degrade-to-zero) stands and remains the FS.GG.Audio implementation
  reference.

- **What this obliges each repo to do** is enumerated in the ordering above. No repo may pin
  the audio packages ahead of decision (3): doing so re-creates the dual-vocabulary hazard on
  every scaffolded game.

<!-- Registration of FS.GG.Audio as the 7th component (ADR-0023) reconciles docs/architecture.md
(map 6→7) as part of *its* resolution; this ADR only adds the template consumer edge, reconciled
in the registry + compatibility projection per the ordering above. -->
