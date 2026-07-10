# ADR-0023: Onboard FS.GG.Audio as an SDD-driven component

- **Status:** Accepted
- **Date:** 2026-07-07
- **Affects:** **.github** (this ADR + roster row + `fs-gg-audio` registry contract + architecture map 6→7), **FS-GG/FS.GG.Audio** (new repo — transferred in + first release)

## Context

The platform needs game audio, and audio is render-independent — a product's
`update` emits pure request values and something downstream turns them into sound.
That subsystem was prototyped as a standalone repo (`EHotwagner/FS.GG.Audio-component`)
and built **spec-first** through the FS.GG SDD lifecycle: four work items
(`001-audio-core` … `004-audio-engine`), each carried `charter → ship` with committed
`.fsi` baselines, headless evidence, and readiness JSON. The result is four coherent
packages:

- **FS.GG.Audio.Core** — the pure request vocabulary (`AudioEffect`, `Bus`,
  `SoundId`/`TrackId`, `AudioEvidence`) + a record-only interpreter. BCL-only.
- **FS.GG.Audio.Host** — the `IAudioBackend` device seam: a deterministic Null/record
  backend (default) and a real OpenAL (Silk.NET) backend that degrades to Null with no
  device; plus an optional `IMixingBackend` for mixing/spatial control.
- **FS.GG.Audio.Engine** — named buses (Master/Music/Sfx/Ui/Ambient), linear +
  equal-power fades/cross-fades, side-chain ducking, and 3D listener/emitters, as a
  pure deterministic `Engine.step`.
- **FS.GG.Audio.Elmish** — a thin Elmish `Cmd` authoring bridge (`Audio.Cmd.playSfx …`).
  Never depends on `FS.GG.UI`.

It shares the org house style already: `.fsi`-as-sole-surface with committed baselines,
pure cores / effect at the edge, `net10.0` + FSharp.Core `10.1.301`, locked restore,
deterministic builds, and `Json-is-contract / Plain+Rich-are-projections` at the tooling
edge. It is time to make it a first-class org repo.

## Decision

Onboard **FS.GG.Audio** as the platform's **seventh** repo and a contract owner.

1. **Transfer + rename.** `EHotwagner/FS.GG.Audio-component` → **`FS-GG/FS.GG.Audio`**
   (public), preserving history.
2. **Roster + registry.** Add the `audio` repo to `registry/dependencies.yml` `repos:`
   and register the **`fs-gg-audio`** contract (owner `audio`) covering the four
   packages' public `.fsi` surfaces. It ships as a coherent NuGet set on one version
   (`<FsGgAudioVersion>`); first release **0.1.0-preview.1**.
3. **Publishing.** A tag-triggered `release.yml` verifies (locked restore + headless
   build/test), publishes the set to the org GitHub Packages feed
   (`nuget.pkg.github.com/FS-GG`) and — via the org Trusted Publishing policy
   `fs-gg-audio-publishing` — dual-publishes to nuget.org via OIDC (ADR-0012/0013, no stored
   key), and attaches the
   `.fsi` API surface, the SDD lifecycle artifacts, and the sample app to the GitHub
   Release. **publish-before-flip (FR-007):** the registry's `package-version` follows a
   real feed publish.

## Consequences

- **The one-way rule holds trivially.** FS.GG.Audio depends on **no** FS-GG component —
  only public packages (FSharp.Core, Silk.NET.OpenAL, Elmish, test-only tooling). It is a
  standalone island: nothing consumes it yet, so `fs-gg-audio` carries no cross-repo
  dependency edge (its `consumers` are within the component — the sample app + test
  suites, mirroring `game-scene-adapter`'s within-component consumer).
- **Not an extraction.** Unlike ADR-0022 (Game, carved out of Rendering), Audio was born
  standalone, so there is no donor major, no frozen profile, and no two-copies transitional
  cost. The Core vocabulary was originally lifted from `FS.GG.UI.Canvas.Audio` (Feature
  243) with behavior parity, but that donor severance already happened upstream.
- **Deferred, recorded.** The native EFX effect graph + managed DSP micro-layer, Doppler /
  full-3D, the `miniaudio` backend, decoders beyond WAV, and an `Audio.Sub` events surface
  are deferred to follow-up work items (`004-audio-engine` DEC-004). Both the org feed and
  nuget.org publishes are live (nuget.org via the `fs-gg-audio-publishing` Trusted
  Publishing policy, OIDC).
- **Future consumer edge.** When a product or scaffold provider adopts audio, it adds a
  `{ from: <consumer>, to: audio, via: "fs-gg-audio@<V>" }` edge — an additive
  contract-change at that time.

## Alternatives considered

- **Keep it a profile/flag inside Rendering** (as game logic once was). Rejected for the
  same reason ADR-0022 gave: a subsystem this size is not a flag, and it would accrete
  audio into `FS.GG.UI.*`, breaking render-independence.
- **Register source-only now, publish later.** Viable, but the packages were release-ready
  and headless-verified, so cutting 0.1.0-preview.1 and flipping `package-version` in one
  step (publish-before-flip satisfied) is cleaner than a source-only placeholder.
