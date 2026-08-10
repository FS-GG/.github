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
new-sdd-workspace ./Pong Pong          # <target-dir> <product-name>; compatibility default = rendering
new-sdd-workspace ./Tool Tool --template console
new-sdd-workspace ./Portal Portal --template web
new-sdd-workspace ./Interop Interop --template fable-bindings --npm-package @babylonjs/core --npm-version 8.0.0 --binding-target browser
```

Run it with **no arguments** on an interactive terminal and it walks you through the meaningful
parameters with prompts (product → target → application type → provider-specific parameters → governance → descriptor ref → currency →
this workspace's repo / coordination org / board / chore-locks, defaulting to FS-GG).
The wizard follows the established defaults without asking redundant confirmations: coordination is
on and an immediate post-scaffold `fsgg-sdd upgrade` is off. Scripted callers can still opt out with
`--no-coordination` or intentionally request reconciliation with `--upgrade`.
Beside the prompts a live preview fills in as you answer — a **parameters** card next to a
**scaffold preview** tree of what the run will produce — and a final go/no-go confirmation
guards the disk. When stdin is redirected (pipes, CI), it skips the wizard and keeps the
usage-error contract, so scripted callers must still pass `<target-dir> <product-name>`.

| Option | Effect |
|---|---|
| `--template <name>` | Selects the scaffold provider: `rendering` (the compatibility default when omitted), `console`, `web`, `fable-game`, or `fable-bindings`. This chooses the generated workspace shape; it is not a rendering profile. |
| `--profile <name>` | Rendering-only `fs-gg-ui` profile: `game` (default — minimal Pong-style starter), `app`, `headless-scene`, `governed`, `sample-pack`. Omitted ⇒ the rendering provider default (`game`); other templates reject it. |
| `--npm-package <name>` / `--npm-version <exact>` / `--binding-target <browser\|node\|universal>` | Required together for `--template fable-bindings`, to pin its npm/declaration closure and target runtime. They are rejected for other templates. |
| `--ref <git-ref>` | `FS.GG.Templates` ref to fetch the provider descriptor from (default: `main` = newest coherent set). Pass a tag to pin a reproducible version. |
| `--pinned` | **skip** the pre-scaffold `fsgg-sdd` self-update and scaffold with the CLI you already have. The default is to update first (see below); pair `--pinned` with `--ref <tag>` for a fully reproducible, pinned scaffold. |
| `--upgrade` | after scaffolding, also run `fsgg-sdd upgrade` to reconcile an existing project (self-update + re-pin + re-seed). Largely redundant on a fresh scaffold now that the CLI is updated *before* scaffolding — kept for the reconcile-an-existing-project case. |
| `--no-governance` | skip the Governance overlay |
| `--board <owner>/<title>` | the coordination board the workspace joins — sets `FSGG_COORD_OWNER`/`FSGG_COORD_PROJECT` (default: `FS-GG/Coordination`). An `owner` with no `/title` defaults the title to `Coordination`. |
| `--repo <owner>/<repo>` | this workspace's own repo — its identity on the board and the basis for its chore-lock ref. In the wizard its owner defaults the board org; on the CLI it drives the non-FS-GG chore-lock next-step hint. Not consumed as env (the engine resolves the repo from the git remote). |
| `--public-board` / `--private-board` | Explicit desired visibility for a product Project; omitted preserves an existing Project. Public requires `--trusted-writers`. |
| `--trusted-writers <team-or-user,…>` | Explicit Project writer allowlist. Required with `--public-board`; it is recorded in security provenance, never inferred from viewer permissions. |
| `secure <workspace> --project … --verified-base-permission READ --verified-exclusive-writers <ids>` | After checking **Project → Settings → Manage access**, re-validates the supported visibility/requested-grant facts and records both human facts: base `Read` and the exact effective/exclusive writer set. It clears only the matching obligation when both assertions equal the requested allowlist. |
| `--chore-locks <refs>` | `FSGG_COORD_CHORE_LOCKS` for a **non-FS-GG** board's chore queue: comma-separated `owner/repo#n`. Unneeded for the FS-GG board (the engine carries its lock table). |
| `--no-coordination` | skip wiring the workspace to a coordination board entirely (no kit, no env). |

### Public-content and board-access boundary

When `--repo owner/repository` names an existing repository, the scaffolder reads
its typed GitHub `IssueCreationPolicy`, changes it to
`COLLABORATORS_ONLY` only when needed, and re-reads the policy before reporting a
receipt. An unreadable repository, inadequate administration permission, failed
mutation, or stale post-write result is reported as **pending**, never as secured.
Fresh workspaces that do not yet have a GitHub repository likewise remain pending
until the repository exists and the command is rerun with `secure <workspace>
--repo owner/repository`.

Project access is a separate boundary: public visibility means internet-readable,
not internet-writable. The supported configuration is organization base permission
`Read`, with `Write` granted only to explicit trusted teams or people; `Admin`
remains narrower. Project `Write` authorizes project-only draft issues—GitHub does
not offer a draft-item `COLLABORATORS_ONLY` switch. See the durable
[public-content trust boundary](../../docs/coordination/untrusted-content-boundary.md)
for the remaining untrusted inputs and operator verification path. The first
Project secure run records observable visibility and requested-grant payload facts
and one deduplicated access obligation. The mutation payload is not claimed as an
effective-writer read. Its recorded `--verified-base-permission READ
--verified-exclusive-writers …` resume command rechecks the observable facts and
converges only when the human-observed exclusive writer set matches the allowlist.

### Coordination by default (ADR-0019)

By default, **step 5 wires the workspace to a coordination board** so `/pnext-item` and `/check-board`
work out of the box: it vendors the coordination kit (the four coordination skills into `.claude`,
`.agents` skill root byte-identical, the `fsgg-coord` shim, and the `fs.gg.coord.cli`
tool manifest — fetched from `FS-GG/.github` over HTTP, no checkout, like the descriptor) and writes
`FSGG_COORD_OWNER`/`FSGG_COORD_PROJECT` (and `FSGG_COORD_CHORE_LOCKS` when given) into the workspace's
`.claude/settings.json` `env`. The board defaults to **FS-GG/Coordination**; `--board` retargets it and
`--no-coordination` skips the step. The **no-arg wizard** does not reconfirm this default; it asks only
for the wiring values — this workspace's repo, org, board title, and chore-locks — with Enter-through
giving FS-GG/Coordination, and the repo's owner defaulting the board-org prompt. This opens the product-mirror slice ADR-0019 §Consequences deferred
(distribution had been framework-repos-only); the engine is env-multi-tenant, so any board works (#1140).

Best-effort and non-blocking, like the governance overlay: a kit file that fails to fetch warns and the
env still lands. **Note:** `offer`/chores on a **non-FS-GG** board need an engine build that includes
#1140 (post-`0.4.0`); the default FS-GG board works on any engine (embedded lock table), so the
scaffolder surfaces the caveat only when you retarget the board.

### Retrofit coordination onto an existing workspace (`retrofit`)

The scaffold-time coordination step (above) only fires when you *create* a workspace. A workspace made
with `--no-coordination` — or before that step existed — has its `.fsgg/` config but **no** coordination
kit, no `fsgg-coord` shim, and no `FSGG_COORD_*` env, so `work-board` (ADR-0064) refuses to drive its
board. The **`retrofit`** subcommand wires coordination **onto** such a workspace — the exact inverse of
the scaffold-time wiring:

```sh
new-sdd-workspace retrofit ./MyApp                                  # FS-GG/Coordination (default)
new-sdd-workspace retrofit ./MyApp --board acme/Roadmap --repo acme/MyApp --chore-locks acme/MyApp#5
```

It is **idempotent** and never leaves partial state:

- it vendors the same kit as the scaffold step (the four coordination skills into `.claude`/`.agents`/
  `.agents` byte-identical, the `fsgg-coord` shim, the `fs.gg.coord.cli` tool manifest merged into any
  existing `.config/dotnet-tools.json`) and writes `FSGG_COORD_OWNER`/`FSGG_COORD_PROJECT`
  (+ `FSGG_COORD_CHORE_LOCKS`) merged into `.claude/settings.json` — **but writes each piece only if it
  is missing or has drifted**, leaving a coherent kit byte-for-byte untouched;
- it **records the retrofit** in `.fsgg/scaffold-provenance.json` (an additive `retrofits[]` entry
  naming what was materialized vs re-emitted as drift; SDD's own provenance keys are preserved and the
  key is read-safe — System.Text.Json ignores it);
- run again on an already-wired workspace it **refuses cleanly** ("already wired — no drift to re-emit")
  and appends no redundant provenance entry; if only some pieces drifted it **re-emits only those**;
- run against a directory that is **not** a scaffolded workspace (no `.fsgg/`) it refuses with exit `2`
  and names the fix (scaffold one first) — nothing is written.

Options accepted: `--board <owner>/<title>`, `--repo <owner>/<repo>`, `--chore-locks <refs>`, `--ref
<git-ref>` (the `FS-GG/.github` ref the kit is vendored from; default `main`). Best-effort like the
scaffold step: a kit file that fails to fetch warns; only when the workspace is unwired **and** every
fetch fails does it exit non-zero, having written nothing.

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
5. **coordination wiring** — vendor the coordination kit + write the `FSGG_COORD_*` env (default on; `--no-coordination` skips) — *non-blocking; best-effort*
6. **`fsgg-sdd doctor`** — read-only coherence check — *non-blocking*
7. **`fsgg-sdd upgrade`** (only with `--upgrade`) — *fatal on failure*

### Governance overlay & feeds

`FS.GG.Workspace.Template` (the `FS.GG.Templates` repo's package — renamed from the frozen
`FS.GG.Templates` package line at 0.7.1, ADR-0072 §1; it carries the `fs-gg-governance` template
alongside `fs-gg-console`/`fs-gg-web`/`fs-gg-fable-game`/`fs-gg-fable-bindings`) is published
anonymously on [nuget.org](https://www.nuget.org/packages/FS.GG.Workspace.Template), so the
overlay needs **no token** —
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
