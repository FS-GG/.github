---
title: FS.GG components
category: FS.GG
description: The independently adoptable framework components maintained by FS.GG.
---

# FS.GG components

<!-- BEGIN GENERATED: fsgg-component-count -->
<!--
  DO NOT EDIT THIS REGION. It is emitted from registry/repos.yml by
  scripts/generate-projections, and `projections` in CI fails on any diff.

  The component/repository COUNT was hand-typed into a dozen consumer-facing places and rotted
  the instant Game/Audio/Net were added — "five repositories" / "four components" were wrong in
  the org profile, four component READMEs and five consumer-guide files (roadmap §3a, #1313). It
  is now a pure count of registry/repos.yml's roster rows, whose SET is held closed by
  check-roster-closure.py; add or retire a repo THERE and every doc that states the count follows.
-->

**Seven framework components** ship independently — each public on [nuget.org](https://www.nuget.org) and restoring with no credential (ADR-0039) — across **eight** repositories in the org (those seven plus this `.github` coordination repo).

<!-- END GENERATED: fsgg-component-count -->

<!-- BEGIN GENERATED: fsgg-component-inventory -->
<!--
  DO NOT EDIT THIS REGION. It is emitted from registry/repos.yml + registry/dependencies.yml by
  scripts/generate-projections, and `projections` in CI fails on any diff.

  The component inventory was a hand-maintained table in the profile page and the consumer
  'what ships' guide, and it rotted: Game/Audio/Net shipped without ever being added (roadmap
  §3a, #1313). The ROW SET is now the framework rows of registry/repos.yml's roster; each row's
  description is that component's `role` in registry/dependencies.yml, and the version is its
  package-bearing contracts' live `package-version` (held to the feed by check-feed-coherence.py).
  Add a framework repo to the roster and a row appears; bump a package and the version follows —
  with no hand edit to any consumer doc. If a description reads too technically, fix the `role`
  in registry/dependencies.yml (the one home), not a copy here.
-->

*Generated from `registry/repos.yml` (the org repo roster, ADR-0019) joined with
`registry/dependencies.yml` (each component's `role` and its contracts' live `package-version`).
`Current version` is `—` for a component whose packages are not (yet) tracked as a
package-bearing contract owned by that component. The exact acquire command and package IDs are
authored beside this table — package IDs are stable identity, versions are not (readme-standard).*

| Component | What it does | Current version |
|---|---|---|
| [**FS.GG.SDD**](https://github.com/FS-GG/FS.GG.SDD) | Lifecycle CLI to scaffold a workspace and drive it from charter to ship; ships the typed cross-repo contracts | `7.5.2` |
| [**FS.GG.Rendering**](https://github.com/FS-GG/FS.GG.Rendering) | The UI framework — MVU over SkiaSharp/OpenGL with layout, input, controls and themes, plus the fs-gg-ui template | `0.1.1` / `0.28.0` |
| [**FS.GG.Governance**](https://github.com/FS-GG/FS.GG.Governance) | Optional tooling that checks your artifacts against rules you control — advisory by default | `1.7.0` |
| [**FS.GG.Templates**](https://github.com/FS-GG/FS.GG.Templates) | Owns workspace providers and templates — rendering composition plus console, web, Fable-game and Fable-bindings shapes | `0.10.0` |
| [**FS.GG.Game**](https://github.com/FS-GG/FS.GG.Game) | Game-simulation libraries — a render-independent simulation core with a companion renderer, usable as plain F# libraries | `0.14.0` / `0.8.0` |
| [**FS.GG.Audio**](https://github.com/FS-GG/FS.GG.Audio) | Audio-engine libraries — synthesis, playback and mixing (buses, fades, ducking, 3D), with an optional Elmish adapter | `0.5.0` |
| [**FS.GG.Net**](https://github.com/FS-GG/FS.GG.Net) | Networking/transport libraries — protobuf messaging over WebSocket or gRPC, render-independent, with an optional Elmish adapter | `0.5.0` |

<!-- END GENERATED: fsgg-component-inventory -->

Each component's linked repository owns its detailed installation, API, and
version documentation. See [Versioning and updates](consumer/versioning-and-updates.md)
for compatibility, feeds, pins, and update policy.
