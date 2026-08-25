# FS-GG

> [!WARNING]
> **The main skill work-board works on a github project board and burns down all issues. Be diligent to let only trusted actors create and modify issues.**
> Public GitHub content is otherwise untrusted data, not executable instruction;
> see the [public-content trust boundary](https://github.com/FS-GG/.github/blob/main/docs/coordination/untrusted-content-boundary.md).

FS-GG is an F# platform for people and agents building production-shaped
applications and libraries. It combines guided workspace creation, a
spec-driven development lifecycle, optional governance, UI, game, audio, and
networking components while keeping each component independently adoptable.

## Create a workspace

Install the published workspace tool by following its
[installation guide](https://github.com/FS-GG/.github/blob/main/scripts/NewSddWorkspace/README.md),
then launch its interactive wizard:

```console
new-sdd-workspace
```

Follow the prompts and confirm the preview. `new-sdd-workspace` is the currently
published command.

### What the wizard creates

The published wizard creates a runnable rendering workspace with its solution,
application, tests, pinned dependencies, and `.fsgg/` lifecycle guidance ready
to continue from specification through shipping.

## Workspace templates

FS.GG.Templates owns five focused workspace shapes; see its
[detailed documentation](https://github.com/FS-GG/FS.GG.Templates) for current
availability.

- **rendering** — a SkiaSharp/OpenGL desktop UI built around Elmish/MVU.
- **console** — a registry-active, production-shaped F# executable without a browser or npm lane.
- **web** — a registry-active F# ASP.NET Core server paired with a neutral TypeScript/Vite site.
- **fable-game** — a registry-active, server-authoritative F# game with a Fable/Elmish browser client.
- **fable-bindings** — a registry-active, package-producing Fable interop library over an exactly pinned JavaScript or TypeScript dependency.

## Spec-driven development

Spec-driven development (SDD) is the lifecycle that keeps the charter and
specification, implementation evidence, verification, updates, and shipping
coherent as work changes. The
[lifecycle guide](https://github.com/FS-GG/.github/blob/main/docs/consumer/lifecycle.md)
explains the full flow.

## The components

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
| [**FS.GG.Templates**](https://github.com/FS-GG/FS.GG.Templates) | Owns workspace providers and templates — rendering composition plus console, web, Fable-game and Fable-bindings shapes | `0.9.0` |
| [**FS.GG.Game**](https://github.com/FS-GG/FS.GG.Game) | Game-simulation libraries — a render-independent simulation core with a companion renderer, usable as plain F# libraries | `0.13.0` / `0.8.0` |
| [**FS.GG.Audio**](https://github.com/FS-GG/FS.GG.Audio) | Audio-engine libraries — synthesis, playback and mixing (buses, fades, ducking, 3D), with an optional Elmish adapter | `0.5.0` |
| [**FS.GG.Net**](https://github.com/FS-GG/FS.GG.Net) | Networking/transport libraries — protobuf messaging over WebSocket or gRPC, render-independent, with an optional Elmish adapter | `0.5.0` |

<!-- END GENERATED: fsgg-component-inventory -->

Each component's linked repository owns its detailed installation, API, and
version documentation.

## Tools and deeper documentation

- [`new-sdd-workspace`](https://github.com/FS-GG/.github/blob/main/scripts/NewSddWorkspace/README.md) launches the supported interactive workspace wizard.
- [`fsgg-sdd`](https://github.com/FS-GG/FS.GG.SDD/blob/main/docs/quickstart.md) drives the SDD lifecycle and keeps workspace artifacts coherent.
- [`fsgg-coord`](https://github.com/FS-GG/.github/blob/main/src/FS.GG.Coord.Cli/README.md) coordinates claimed work, review, and delivery across FS-GG repositories.
- [FS.GG.Governance](https://github.com/FS-GG/.github/blob/main/docs/consumer/governance.md) adds optional, workspace-owned rules and gates.
- The [consumer guide](https://github.com/FS-GG/.github/blob/main/docs/consumer/index.md) covers everyday use, while the [architecture guide](https://github.com/FS-GG/.github/blob/main/docs/architecture.md) owns design and composition detail.
- The [versioning guide](https://github.com/FS-GG/.github/blob/main/docs/consumer/versioning-and-updates.md) owns compatibility, feeds, pins, and update policy.

## License

MIT.
