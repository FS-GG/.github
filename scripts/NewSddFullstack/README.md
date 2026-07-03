# FS.GG.NewSddFullstack (`new-sdd-fullstack`)

The **sole** full-stack FS.GG product scaffolder — an F# / Spectre.Console dotnet tool that
composes the SDD lifecycle skeleton, a runnable [FS.GG.Rendering](https://github.com/FS-GG/FS.GG.Rendering)
`fs-gg-ui` app, and a Governance overlay in one command, using only existing published
machinery. No `FS.GG.Templates` checkout required. Successor to the retired
`scripts/new-sdd-fullstack.sh` (see [ADR-0016](https://github.com/FS-GG/.github/blob/main/docs/adr/0016-retire-templates-local-new-fullstack-single-scaffolder.md)).

## Install

```sh
dotnet tool install --global FS.GG.NewSddFullstack \
  --add-source https://nuget.pkg.github.com/FS-GG/index.json
```

Requires the `fsgg-sdd` CLI on PATH (`dotnet tool install --global FS.GG.SDD.Cli`).

## Use

```sh
new-sdd-fullstack ./Pong Pong          # <target-dir> <product-name>
```

| Option | Effect |
|---|---|
| `--ref <git-ref>` | `FS.GG.Templates` ref to fetch the provider descriptor from (default: `main` = newest coherent set). Pass a tag to pin a reproducible version. |
| `--upgrade` | also run `fsgg-sdd upgrade` after scaffolding (reconcile if behind — ADR-0009: never automatic) |
| `--no-governance` | skip the Governance overlay |

## What it does

It orchestrates the commands that already exist, and reports each step's outcome
(`✓ worked` / `⚠ warning` / `⊘ skipped` / `✗ failed`) in a summary table:

1. **fetch** the newest rendering provider descriptor from `FS.GG.Templates` (HTTP, no clone) — *fatal on failure*
2. **`fsgg-sdd scaffold`** — SDD skeleton + runnable Rendering app — *fatal on failure*
3. **governance overlay** (`dotnet new fs-gg-governance`, profile `light`) — *non-blocking; best-effort*
4. **`fsgg-sdd doctor`** — read-only coherence check — *non-blocking*
5. **`fsgg-sdd upgrade`** (only with `--upgrade`) — *fatal on failure*

## Develop

From an `FS-GG/.github` checkout:

```sh
dotnet run --project scripts/NewSddFullstack -- ./Pong Pong
```

Output degrades to plain (ANSI-free) when piped, under `NO_COLOR`, or on a non-TTY.
