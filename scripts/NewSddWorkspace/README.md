# FS.GG.NewSddWorkspace (`new-sdd-workspace`)

The **sole** FS.GG workspace scaffolder — an F# / Spectre.Console dotnet tool that
composes the SDD lifecycle skeleton, a runnable [FS.GG.Rendering](https://github.com/FS-GG/FS.GG.Rendering)
`fs-gg-ui` app (profile-selectable), and an optional Governance overlay in one command, using
only existing published machinery. No `FS.GG.Templates` checkout required. Successor to the
retired `scripts/new-sdd-fullstack.sh` (see [ADR-0016](https://github.com/FS-GG/.github/blob/main/docs/adr/0016-retire-templates-local-new-fullstack-single-scaffolder.md)).

> **Renamed from `new-sdd-fullstack` / `FS.GG.NewSddFullstack`.** "Fullstack" implied a single
> app shape, but `--profile` lets you pick the output app (`game`/`app`/`headless-scene`/…) and
> `--no-governance` drops a layer — so the honest name is **workspace**, the ADR-0020 term for
> what a consumer scaffolds. The old preview id (≤`0.1.1-preview.1`) is superseded.

## Install

```sh
dotnet tool install --global FS.GG.NewSddWorkspace \
  --add-source https://nuget.pkg.github.com/FS-GG/index.json
```

Requires the `fsgg-sdd` CLI on PATH (`dotnet tool install --global FS.GG.SDD.Cli`).

## Use

```sh
new-sdd-workspace ./Pong Pong          # <target-dir> <product-name>
```

Run it with **no arguments** on an interactive terminal and it walks you through the same
parameters with prompts (product → target → profile → governance → descriptor ref → currency → upgrade).
Beside the prompts a live preview fills in as you answer — a **parameters** card next to a
**scaffold preview** tree of what the run will produce — and a final go/no-go confirmation
guards the disk. When stdin is redirected (pipes, CI), it skips the wizard and keeps the
usage-error contract, so scripted callers must still pass `<target-dir> <product-name>`.

| Option | Effect |
|---|---|
| `--profile <name>` | `fs-gg-ui` render profile: `game` (default — minimal Pong-style starter), `app`, `headless-scene`, `governed`, `sample-pack`. Omitted ⇒ the scaffold-provider default (`game`). |
| `--ref <git-ref>` | `FS.GG.Templates` ref to fetch the provider descriptor from (default: `main` = newest coherent set). Pass a tag to pin a reproducible version. |
| `--pinned` | **skip** the pre-scaffold `fsgg-sdd` self-update and scaffold with the CLI you already have. The default is to update first (see below); pair `--pinned` with `--ref <tag>` for a fully reproducible, pinned scaffold. |
| `--upgrade` | after scaffolding, also run `fsgg-sdd upgrade` to reconcile an existing project (self-update + re-pin + re-seed). Largely redundant on a fresh scaffold now that the CLI is updated *before* scaffolding — kept for the reconcile-an-existing-project case. |
| `--no-governance` | skip the Governance overlay |

### Currency by default (ADR-0030)

By default, **step 2 self-updates the `fsgg-sdd` CLI to the newest published build before it
scaffolds**, so a fresh workspace is always produced by the current coherent set's tooling — you
don't have to remember to update your CLI first. This is the deliberate creation-time carve-out to
[ADR-0009](https://github.com/FS-GG/.github/blob/main/docs/adr/0009-cli-single-orchestrator-detect-and-remediate.md)'s
"never silently auto-update" rule ([ADR-0030](https://github.com/FS-GG/.github/blob/main/docs/adr/0030-creation-time-scaffolding-self-updates-by-default.md)):
there is no existing consumer artifact to clobber, and newest-by-default is the whole point of
*creating* a workspace. ADR-0009 still governs the in-project `fsgg-sdd` verbs — this default only
touches the CLI used to create a brand-new workspace.

The self-update is **best-effort and non-blocking**. `FS.GG.SDD.Cli` is dual-published (ADR-0012):
anonymously on nuget.org **and** — possibly a newer build — on the org GitHub Packages feed (whose
reads are all authenticated). It reuses the governance overlay's feed ladder: with a `read:packages`
token (`FSGG_PACKAGES_TOKEN` / `GH_TOKEN` / `GITHUB_TOKEN`) it tries the org feed first and falls
back to nuget.org; **with no token it updates from nuget.org anonymously**. If the update fails or
you are offline, the step warns and scaffolding proceeds with the installed CLI. Pass `--pinned` to
opt out entirely; `--pinned --ref <tag>` gives a byte-reproducible pinned scaffold.

## What it does

It orchestrates the commands that already exist, and reports each step's outcome
(`✓ worked` / `⚠ warning` / `⊘ skipped` / `✗ failed`) in a summary table:

1. **fetch** the newest rendering provider descriptor from `FS.GG.Templates` (HTTP, no clone) — *fatal on failure*
2. **update `fsgg-sdd`** — self-update the CLI to the newest build so the scaffold is current (default; `--pinned` skips) — *non-blocking; best-effort* (ADR-0030)
3. **`fsgg-sdd scaffold`** — SDD skeleton + runnable Rendering app — *fatal on failure*
4. **governance overlay** (`dotnet new fs-gg-governance`, profile `light`) — *non-blocking; best-effort*
5. **`fsgg-sdd doctor`** — read-only coherence check — *non-blocking*
6. **`fsgg-sdd upgrade`** (only with `--upgrade`) — *fatal on failure*

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
dotnet run --project scripts/NewSddWorkspace -- ./Pong Pong
```

Output degrades to plain (ANSI-free) when piped, under `NO_COLOR`, or on a non-TTY.
