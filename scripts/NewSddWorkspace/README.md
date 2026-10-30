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

## Current source and published boundary

Wizard **0.23.0** is published: tag `new-sdd-workspace/v0.23.0` (release 429016553) and nuget.org
`FS.GG.NewSddWorkspace` 0.23.0. It provides descriptor-driven catalog creation with stable SDD CLI
**2.5.3 or newer**. Creation and dry-run admit that runtime before target mutation and recheck the
exact producer immediately before scaffold. Legacy typed project knowledge retains its 2.1.0 floor;
list/help require no catalog runtime.
The catalog route described below describes the published FS.GG.Templates **0.20.1**
(tag `fs-gg-templates/v0.20.1`, package `FS.GG.Workspace.Template::0.20.1`), the release that carries the
common project-knowledge composition for the TypeScript, JavaScript, Rust and Go providers.
The four-product public-tool journeys passed on Linux x64 **with `--no-coordination`**: the public Wizard 0.23.0,
SDD 2.5.3 and Governance 1.15.0 installed from nuget.org created TypeScript, JavaScript, Rust and Go CLI workspaces
against `fs-gg-templates/v0.20.1`, and 85 of 85 checks passed (creation, knowledge, Doctor, Governance Verify with all
eight gates, and five refusal controls per product); see the
[#3010 Step 3 result](https://github.com/FS-GG/.github/issues/3010#issuecomment-6523620705). The retained receipts are
in private custody; their manifest digest is `ea322f1526a048836deb2f1eaa2e817ccdaffc91647e4cc19cd69bd191e8d217`
and the run summary `evidence/results.json` is `aa01a492c6ded094ac988cb2953d02ff8cfdb74e6a27ac3ac8abc9b3fc97737d`.

**Known defect in 0.23.0: creation with default coordination yields an incoherent `doctor`.** With coordination
on (the default), Wizard 0.23.0 writes a `securityObligations` key into `.fsgg/scaffold-provenance.json`. That file
belongs to SDD, and SDD 2.5.3 rejects the unknown property: `doctor` reports `isCoherent:false` with
`scaffold.provenanceMalformed` on all four catalog providers. The default-coordination journey found this (see the
[#3010 result](https://github.com/FS-GG/.github/issues/3010#issuecomment-6523944130)); the repair is tracked in
.github#4964 (obligations move to a Wizard-owned file; planned for Wizard 0.23.1) and is not yet published.
Until 0.23.1, pass `--no-coordination` (the configuration the 85/85 result covers), or accept that default-coordination
creations need the `securityObligations` key removed from `.fsgg/scaffold-provenance.json`, which restored
`isCoherent:true` in that journey. The security obligations were recorded as pending, never claimed satisfied, and
`knowledge check` passed on all four products. The key removal was observed by the qualification run, not
a Wizard-supported operation; the pending obligations it carries are then not visible to SDD.

Default coordination also fetches the coordination kit over HTTPS from the moving `main` ref of this repository, so
the materialized kit is not reproducible across fetches. The kit observed in the journey matched `main@a9a6f77f`
byte for byte; pinning it is part of #4964.

Stated limits remain: the GitHub Packages feed was not read directly (the qualifying token received 403), only
one Linux x64 carrier was exercised, no reproducibility pack exists, and independent custody of the advisory inputs
is still owed.

## Install

```sh
dotnet tool install --global FS.GG.NewSddWorkspace \
  --add-source https://nuget.pkg.github.com/FS-GG/index.json
```

Requires the `fsgg-sdd` CLI on PATH (`dotnet tool install --global FS.GG.SDD.Cli`).

## Use

```sh
new-sdd-workspace ./Pong Pong          # <target-dir> <product-name>; compatibility default = rendering
new-sdd-workspace ./Typed Typed --lifecycle typed-sdd
new-sdd-workspace ./Tool Tool --template console
new-sdd-workspace ./Portal Portal --template web
new-sdd-workspace ./Interop Interop --template fable-bindings --npm-package @babylonjs/core --npm-version 8.0.0 --binding-target browser
new-sdd-workspace ./Arena Arena --template fable-game --bundle studio
```

Run it with **no arguments** on an interactive terminal and it asks for the product name, target
directory, and workspace template. Choosing `fable-bindings` also asks for its required exact npm
package/version and binding target. Standard SDD remains the default for other templates. When
`--lifecycle` is omitted, `fable-game` uses the lifecycle default its fetched descriptor declares:
`typed-sdd` for `FS.GG.Workspace.Template` 0.15.0 and later. Light governance remains the default; GitHub
coordination stays unset, and the wizard installs
`$initialize-sdd-workspace` for the agent you open inside the new repository. That skill discovers
the real local/provider state and asks about repository, board, access, collaborators, chore locks,
or package configuration only when the selected setup actually needs them. Scripted callers retain
all explicit flags for advanced providers and automation.
Beside the prompts a live preview fills in as you answer — a **parameters** card next to a
**scaffold preview** tree of what the run will produce — and a final go/no-go confirmation
guards the disk. When stdin is redirected (pipes, CI), it skips the wizard and keeps the
usage-error contract, so scripted callers must still pass `<target-dir> <product-name>`.

> **Initialization is a separate first-agent step.** The wizard installs the initialization
> handoff but does not run it. After scaffolding, start an agent inside the new workspace for the
> first time, then ask it to run `$initialize-sdd-workspace` before doing repository work.

| Option | Effect |
|---|---|
| `--template <name>` | Selects the scaffold provider: `rendering` (the compatibility default when omitted), `console`, `web`, `fable-game`, or `fable-bindings`. This chooses the generated workspace shape; it is not a rendering profile. |
| `--lifecycle <none\|sdd\|typed-sdd\|spec-kit>` | Selects the root lifecycle and forwards an explicit token unchanged to `fsgg-sdd scaffold`. Omission is `sdd`, except that `fable-game` forwards the `lifecycle` default its fetched descriptor declares (`typed-sdd` from Workspace.Template 0.15.0; `sdd` before). A missing or unrecognized declaration keeps `sdd` with a warning. `none` is Freeform; `spec-kit` remains legacy/frozen. |
| `--profile <name>` | Rendering-only `fs-gg-ui` profile: `game` (default — minimal Pong-style starter), `app`, `headless-scene`, `governed`, `sample-pack`. Omitted ⇒ the rendering provider default (`game`); other templates reject it. |
| `--bundle <player\|studio\|tactical\|arcade\|complete>` | `fable-game` composition only. Omitted selects `player`; unrelated providers reject the flag. The value is forwarded as the provider's `bundle` parameter and does not change lifecycle or Rendering profile selection. |
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

### Catalog providers (TypeScript, JavaScript, Rust, Go)

Providers that FS.GG.Templates declares in its ref-pinned provider catalog
(`providers/polyglot.catalog.json`) need no Wizard release to appear: the Wizard reads the catalog
**before** any selection and renders what it declares. `--list-providers` prints the built-in templates
and every catalog provider with its language, product shape and typed parameters:

```sh
new-sdd-workspace --list-providers --ref fs-gg-templates/v0.20.1
```

```sh
new-sdd-workspace ./Greeter "Greeter" --template go-cli --ref fs-gg-templates/v0.20.1 \
  --param packageIdentity=example.org/acme/greeter --param codeIdentifier=greet \
  --admission-policy ./host-policy.json --no-coordination
```

- **Catalog and archive.** The catalog is fetched from `FS.GG.Templates@<--ref>` (default `main`;
  pair a release tag with `--pinned` for a reproducible creation). The template archive is the exact
  package the provider's `templateSource` names (for example `FS.GG.Workspace.Template::0.20.1`), read from
  the NuGet flat container. Both are verified by SHA-256 and handed to `fsgg-sdd scaffold --catalog ...`
  as the exact bytes verified; `--catalog-sha256` and `--template-sha256` pin an expected digest. An
  unreadable, stale or unknown-ref catalog is a refusal, never a fallback to a previous copy.
- **Offline.** `--catalog-file <file> --catalog-sha256 <digest>` and `--template-archive <file> --template-sha256 <digest>`
  select local files; the digest is mandatory because nothing else vouches for them.
- **Typed values.** Each `--param key=value` is validated against the descriptor (string, enum, exact-version,
  required, length and allow-list) before any step runs. The parameter the descriptor binds to the raw product
  name defaults to the product name you typed; no other value is derived and no identifier transform exists, so a
  Go module path, package name or code identifier is always yours. Values are forwarded to SDD literally.
- **Admission policy.** `--admission-policy <file>` is required: it is the host owner's independently selected policy
  for the tools to probe and the archive to admit (SDD#928). The Wizard reads its time budgets and checks that it
  admits the provider's template source, but never authors or widens it. Author it in SDD's fixed probe
  environment (`PATH=/usr/bin:/bin`), for example with `eng/polyglot-qualification/make-policy.py` in FS.GG.Templates.
- **Host.** The first SDD catalog profile is Linux x64 with the qualified glibc, .NET 10.0.12 runtime and
  SDK 10.0.401 transport; SDD refuses any other host before it writes. The Wizard additionally refuses a provider
  that does not list the host platform. The target must not exist and its parent must.
- **No target mutation before SDD commits.** Everything is acquired into a private scratch directory that is removed
  afterwards; `fsgg-sdd catalog inspect` (read-only) and then `fsgg-sdd scaffold --catalog` compose the workspace in
  staging and commit it atomically. `--dry-run` stops after SDD's own preparation.
- **What does not apply.** The .NET Governance overlay is skipped (the catalog declares the provider's capability
  bindings), `--lifecycle` is accepted only if the descriptor declares a `lifecycle` parameter, and coordination,
  repository security, doctor and the initialization handoff behave as for any other workspace.
- **Interactive.** With no arguments the Wizard first asks for the language (built-in templates remain one choice),
  then the product shape, then each descriptor prompt, then the policy file, and shows the resolved selection
  before it asks to create.

The catalog route is published with Wizard 0.23.0 over Templates 0.20.1 and its public-tool journeys are
recorded in [#3010](https://github.com/FS-GG/.github/issues/3010) (the limits are stated in the first section).
The published Templates 0.20.0 polyglot payloads lack the common project-knowledge CI/checker composition that
0.20.1 carries. Every catalog creation initializes the canonical store through the exact
capable SDD producer, pins that version locally, and requires the guide, schema, CI and checker before
`knowledge check` and the initialization handoff. Known-missing CI/checker payloads refuse before target
creation. Missing generated assets or refused knowledge checks fail creation;
the partially created workspace is retained for diagnosis. The legacy Rendering overlay is not applied to
catalog providers. Future execution inputs can use descriptor-declared parameters; this release
does not claim the accepted Coordination controller is implemented or activated.

### Typed project knowledge bootstrap

A `typed-sdd` creation requires an actual stable `fsgg-sdd` version of at least 2.1.0
before scaffold effects. The wizard rechecks that exact producer before handoff and records
it in the root `.config/dotnet-tools.json`. It preserves unrelated tool pins and refuses
an authored SDD pin conflict or a manifest explicitly marked as non-root.

The capable scaffold seeds `.fsgg/knowledge-guide.md`, the knowledge schema, and initial
records. Owned templates emit the central project-knowledge workflow and checker. Rendering
app and game creation composes the separate `fs-gg-project-knowledge` overlay from exact
`FS.GG.Workspace.Template::0.18.0`; the Rendering archive remains immutable. The installer
may refresh package registration, but overlay creation never uses `--force`. A uniquely
registered cached package is reused only after its actual archive's complete overlay payload
matches the qualified central workflow, checker, and template configuration. Unknown bytes,
malformed metadata, and duplicate registrations refuse before overlay emission.

The wizard checks all bootstrap artifacts and runs the actual producer's knowledge check
before the first-agent handoff. It creates no Git commit: the caller's normal initial commit
includes the guide, schema, records, exact local tool pin, workflow, and checker. Standard SDD
and the separate `--upgrade` operation retain their existing behavior.

This source candidate depends on coherent public SDD 2.1.0 and Workspace.Template 0.18.0
publication, installed-package checks, and guarded descriptor/registry adoption. Source
qualification with injected exact compiled tools and immutable archives does not activate
those public pins. See the [qualification record](../../docs/roadmaps/tsdd-knowledge-wizard.md).

### Release D.5 qualification boundary

Version 0.12.0 introduced the selected SVG default, scoped to a descriptor whose exact `source:`
was `FS.GG.Workspace.Template::0.15.0`. Wizards 0.12.0 to 0.21.0 therefore forwarded `sdd` once
that pin moved on, contrary to D.5 (FS-GG/.github#4811). The current source, intended for the
next Wizard release (0.22.0), makes the omitted `fable-game` lifecycle the `lifecycle` default the
fetched descriptor declares: `typed-sdd` for 0.15.0 and later, `sdd` for 0.14.0 and earlier. A
descriptor with no readable declaration keeps `sdd` and prints a warning. Spelling `--lifecycle`
always preserves that selection.
A source change does not publish the package or activate the registry default.

Before public activation, verify immutable Templates 0.15.0 and wizard 0.12.0 packages and
the selected SDD release (2.0.2 or later, independently qualified) from public feeds.
With empty package caches and no sibling source, create the omitted SVG Player through
raw `dotnet new fs-gg-fable-game` for product files, then through installed
`fsgg-sdd scaffold` and this wizard for the root `typed-sdd` lifecycle and default
Quint backend. Exercise an authored and verified path plus a refusal case. Check
explicit lifecycle tokens, older provider pins, unrelated templates and SVG bundles.
Repeat clean creation after the effective registry pins the published pair; record
package hashes and generated results. Existing workspace upgrades need their own
promised compatibility proof.

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

### Initialization before coordination

The no-argument wizard does not guess a repository or attach every new product to
`FS-GG/Coordination`. It writes `.fsgg/workspace-initialization.json` with `status: pending`, installs
the initialize skill into both agent roots, and adds a conditional warning to `AGENTS.md` and
`CLAUDE.md`. The first agent entering the repository should run `$initialize-sdd-workspace`;
local-only is a complete supported result, while users who select GitHub coordination are guided
through the existing `retrofit` and `secure` commands with real repository context available.

Explicit CLI invocations retain their compatible default: **step 5 wires the workspace to a
coordination board** so `/pnext-item` and `/check-board` work out of the box. It vendors the
coordination kit (the coordination skills into `.claude`,
`.agents` skill root byte-identical, the `fsgg-coord` shim, and the `fs.gg.coord.cli`
tool manifest — fetched from `FS-GG/.github` over HTTP, no checkout, like the descriptor) and writes
`FSGG_COORD_OWNER`/`FSGG_COORD_PROJECT` (and `FSGG_COORD_CHORE_LOCKS` when given) into the workspace's
`.claude/settings.json` `env`. The board defaults to **FS-GG/Coordination**; `--board` retargets it and
`--no-coordination` skips the step. This opens the product-mirror slice ADR-0019 §Consequences deferred
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

- it vendors the same kit as the scaffold step (the coordination skills into `.claude`/`.agents`/
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

### Selected product V2 integration (source preparation)

For an explicitly selected product board, supply its canonical closed Binding v3, exact repository
and immutable producer revision. The published tool pin and both skill manifests must match the
creator's adapter closure. This source route is unqualified until coherent publication and actual
fresh/retained receiver journeys pass; a local result never proves board adoption.

```sh
new-sdd-workspace ./MyApp MyApp --repo acme/MyApp --board-binding /private/product-binding.json --board-kit-ref <immutable-sha>
new-sdd-workspace retrofit ./MyApp --repo acme/MyApp --board-binding /private/product-binding.json --board-kit-ref <immutable-sha> --preview
```

Preview stages exact manifest-verified kit and product consumer bytes, tool pin, binding, settings
and additive `productBoard` scaffold provenance without writes. Repeat without `--preview` to apply
within authorized local configuration scope. The prepared plan rechecks its file preimages before
writing and rolls back original bytes on a write failure, reporting incomplete rollback explicitly.
An unchanged integration is a no-op. Unrelated settings, tools and provenance are preserved;
conflicting owner edits, legacy board environment, missing producer bytes, wrong repository,
organization bindings and adapter mismatch refuse before installation. The original legacy route
remains historical compatibility; it cannot authorize V2 consumers or migrate selected board rows.

V2 writes `.fsgg/board-v2-binding.json`, selected V2 settings and immutable publication/provenance
identities. It never embeds credentials in templates, creates a GitHub resource or performs a board
write. Product guidance uses the canonical read-only `board-v2 inspect` route and preserves human
planning fields, including Blocked. The bounded adapter does not implement general issue/membership
intake; padd-item returns a concrete pending draft for that missing operation. Local-only creation
and initialization continue without GitHub. Existing provider/lifecycle selections are independent.

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
8. **initialization handoff** — install the embedded initialize skill, pending marker, and conditional agent warning after all overlays have written — *fatal on failure*

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

For a target that was absent when this invocation began, successful SDD scaffold provenance
and an immediate tool-manifest byte/mode snapshot identify its generated coordination default.
Creator0.16 and its source successor can replace only that unchanged generated Coord entry with the exact selected Kit pin,
preserving SDD and other tools. An existing directory without Git, retained repository or retrofit
receives no such allowance. Missing ownership evidence or edits after the snapshot refuse; preview
and atomic apply continue to protect all owner preimages. Published0.15 remains immutable.
