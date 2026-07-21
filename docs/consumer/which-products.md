---
title: Which FS-GG components do I need?
category: FS.GG
categoryindex: 6
index: 12
description: A decision guide by goal — render a UI, run a lifecycle, scaffold the full stack, add governance, or pull in a render-independent building block (game-sim, audio, networking).
---

# Which components do I need?

You never adopt the whole platform at once. Pick by your goal. The composition
paths (rendering → lifecycle → full stack → governance) build on each other; the
render-independent building blocks below the line each stand alone.

## Quick decision

| Your goal | What you adopt | Don't need |
|---|---|---|
| Render an F# UI inside an app you already have | **Rendering** only | SDD, Governance, Templates |
| Start a new workspace with a managed dev lifecycle | **SDD** (+ Rendering via the provider) | Governance |
| Stand up a runnable full-stack workspace in one go | **Templates** (drives SDD + Rendering) | — |
| Add rules / gates to an existing SDD workspace | **Governance** overlay | new scaffolding |
| Simulate game state without pulling in a renderer | **Game** (`FS.GG.Game.Core`) | Rendering, SDD, Governance |
| Add game audio (effects, mixing, 3D) to an app | **Audio** (`FS.GG.Audio.*`) | Rendering, SDD, Governance |
| Talk to a server / peer over the wire (protobuf, WebSocket, gRPC) | **Net** (`FS.GG.Net.*`) | Rendering, SDD, Governance |

## The rendering-only path

You just want the UI framework — you have your own app, build, and release.

- Reference the `FS.GG.UI.*` packages (or the `fs-gg-ui` template). They're
  preview packages on `net10.0`, published on **public nuget.org** — add a
  `<PackageReference>` (restore needs no extra feed) or scaffold from the template.
- No `fsgg-sdd`, no `.fsgg/`, no governance.

→ [FS.GG.Rendering · usage](https://github.com/FS-GG/FS.GG.Rendering/blob/main/docs/usage.md#getting-the-packages)
covers all three ways to get the packages and the package map.

## The lifecycle path

You want a structured `charter → ship` lifecycle around a new workspace, and the
reference UI app as the starting point.

```sh
dotnet tool install --global FS.GG.SDD.Cli
fsgg-sdd scaffold --root ./MyApp --provider rendering --param productName=MyApp
fsgg-sdd charter
```

`scaffold` gives you the runnable app **and** the lifecycle skeleton; `init`
gives you the skeleton only (bring your own template). Governance stays off.

→ [Getting started](getting-started.md) · [SDD quickstart](https://github.com/FS-GG/FS.GG.SDD/blob/main/docs/quickstart.md)

## The full-stack path

You want SDD + a runnable Rendering app + Governance config wired together. This
is the [FS.GG.Templates](https://github.com/FS-GG/FS.GG.Templates) composition.
Composition happens at scaffold time — there is no single all-in-one `dotnet new`
template, because that could only exist by bundling a rendering copy that goes
stale.

```sh
# 1. Register the rendering provider in your project.
mkdir -p ./MyApp/.fsgg
cp providers/rendering.providers.yml ./MyApp/.fsgg/providers.yml   # from FS.GG.Templates

# 2. Scaffold the SDD skeleton + the live FS.GG.Rendering app.
fsgg-sdd scaffold --root ./MyApp --provider rendering --param productName=MyApp

# 3. Activate governance with the populated reference gate set.
dotnet new install ./templates/fs-gg-governance
dotnet new fs-gg-governance -o ./MyApp --appName MyApp --defaultProfile light

cd ./MyApp && dotnet build && dotnet run
```

The [`new-sdd-workspace <target> <product>`](https://github.com/FS-GG/.github/tree/main/scripts/NewSddWorkspace)
dotnet tool (in FS-GG/.github) wraps these three steps — no FS.GG.Templates checkout required.

→ [FS.GG.Templates · full-stack](https://github.com/FS-GG/FS.GG.Templates#create-a-full-stack-workspace-composition-primary-path)

## The add-governance path

You already have an SDD-managed project and want to turn on gates.

```sh
dotnet new install FS.GG.Templates
dotnet new fs-gg-governance -o ./MyApp --appName MyApp --defaultProfile light
```

This drops the four `.fsgg/*.yml` governance files (the reference gate set) into
the project. Governance only inspects — it never becomes a build dependency.

→ [Adopting governance](governance.md) · [FS.GG.SDD · adopting governance](https://github.com/FS-GG/FS.GG.SDD/blob/main/docs/adopting-governance.md)

## The game-sim path

You want deterministic game/simulation state — entities, systems, stepping — with
**no renderer, no lifecycle, no governance**. [FS.GG.Game](https://github.com/FS-GG/FS.GG.Game)
is a BCL-only simulation core extracted from Rendering; it depends on no other
FS-GG component. Add just the sim core, or add the thin Scene adapter when you also
render with FS.GG.Rendering.

```sh
dotnet add package FS.GG.Game.Core     # BCL-only simulation core
dotnet add package FS.GG.Game.Render   # optional: Scene adapter for FS.GG.Rendering
```

Both are public on nuget.org and restore with no credential (ADR-0039).

→ [FS.GG.Game](https://github.com/FS-GG/FS.GG.Game)

## The audio path

You want game audio — an effect vocabulary, a device seam, a mixing engine (buses,
fades, ducking, 3D), and an Elmish `Cmd` bridge — independent of what draws your
frames. [FS.GG.Audio](https://github.com/FS-GG/FS.GG.Audio) depends on no FS-GG
component; start from `FS.GG.Audio.Core` and add the siblings you need.

```sh
dotnet add package FS.GG.Audio.Core    # then .Host / .Engine / .Elmish as needed
```

Public on nuget.org, no credential (ADR-0039).

→ [FS.GG.Audio](https://github.com/FS-GG/FS.GG.Audio)

## The networking path

You want to talk to a server or peer over the wire — a domain-neutral `ITransport`
/ `IMessageChannel` seam with WebSocket + gRPC transports, Protobuf codecs, and an
Elmish `Cmd` / `Sub` bridge. [FS.GG.Net](https://github.com/FS-GG/FS.GG.Net)
depends on no FS-GG component; start from `FS.GG.Net.Core` and add the transport /
codec packages your app needs.

```sh
dotnet add package FS.GG.Net.Core      # then .WebSocket[.Server] / .Protobuf / .Grpc / .Elmish
```

Public on nuget.org, no credential (ADR-0039).

→ [FS.GG.Net](https://github.com/FS-GG/FS.GG.Net)

## How the dependencies actually run

```text
Templates ──compose (scaffold-time)──▶ SDD · Rendering · Governance
SDD ──── governance-handoff (optional) ────▶ Governance
Rendering ── depends on no FS-GG component — never on Governance
```

Rendering is always at the bottom and never reaches up. That's what lets you stop
at any path above without the one below it falling apart.
