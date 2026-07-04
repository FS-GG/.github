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

Run it with **no arguments** on an interactive terminal and it walks you through the same
parameters with prompts (product → target → profile → governance → descriptor ref → upgrade).
Beside the prompts a live preview fills in as you answer — a **parameters** card next to a
**scaffold preview** tree of what the run will produce — and a final go/no-go confirmation
guards the disk. When stdin is redirected (pipes, CI), it skips the wizard and keeps the
usage-error contract, so scripted callers must still pass `<target-dir> <product-name>`.

| Option | Effect |
|---|---|
| `--profile <name>` | `fs-gg-ui` render profile: `game` (default — minimal Pong-style starter), `app`, `headless-scene`, `governed`, `sample-pack`. Omitted ⇒ the scaffold-provider default (`game`). |
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

### Governance overlay & feeds

`FS.GG.Templates` (which carries the `fs-gg-governance` template) is published anonymously on
[nuget.org](https://www.nuget.org/packages/FS.GG.Templates), so the overlay needs **no token** —
the install runs from an isolated `nuget.config` exposing only nuget.org, so an
anonymous-401-on-read org feed in your global config can't poison the restore. If
`FSGG_PACKAGES_TOKEN` / `GH_TOKEN` / `GITHUB_TOKEN` (a `read:packages` token) is set, the org feed
is tried first (it may carry a newer build) with nuget.org as the fallback. The step only skips
when **both** feeds fail, and the product is fine without it.

## Develop

From an `FS-GG/.github` checkout:

```sh
dotnet run --project scripts/NewSddFullstack -- ./Pong Pong
```

Output degrades to plain (ANSI-free) when piped, under `NO_COLOR`, or on a non-TTY.
