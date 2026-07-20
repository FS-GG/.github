# Org-shared .NET build configuration

The canonical MSBuild / CPM / tool-manifest baseline for every FS-GG .NET repo. The source of
truth is [`dist/dotnet/`](../../dist/dotnet/) in this repo; consumer repos **materialize** the files
from the versioned **FS.GG.Kit** package ([ADR-0062](../adr/0062-versioned-kit-package-replaces-byte-copy-sync.md),
#1262) rather than hand-copying or the retired byte-copy sync. Decision record: [ADR-0006](../adr/0006-org-shared-dotnet-build-config-and-unified-restore-locked-mode-gate.md).
Contracts: `shared-build-config` + `lockfile-restore-enforcement`
([registry](../../registry/dependencies.yml), [projection](../registry/compatibility.md)).

## What is distributed

| File | Purpose |
|---|---|
| `Directory.Build.props` | Deterministic builds; Central Package Management (CPM) + transitive pinning; the `lockfile-restore-enforcement` gate; `NU1603`/`NU1608`→error; the opt-in `api-breaking-change-gate`. |
| `Directory.Packages.props` | CPM enablement + the org-wide `FSharp.Core` pin (`10.1.301`) + the centrally-pinned `PublicApiAnalyzers` (`3.3.4`) for the gate. |
| `.config/dotnet-tools.json` | Pinned local tool manifest (`fake-cli` `6.1.4`, matching the `Fake.Core.*` library pin). |
| ~~`.config/fsgg-build-config.sha`~~ | **RETIRED** (ADR-0062, #1262). The ADR-0036 provenance pin was the baseline the *byte-copy* drift check judged against; under package delivery the pin is unmanaged, so it was deleted from every receiver and the check now measures against the pinned `FS.GG.Kit` instead — see below. |

## The unified `RestoreLockedMode` gate

```xml
<RestoreLockedMode Condition="'$(GITHUB_ACTIONS)' == 'true' And Exists('$(MSBuildProjectDirectory)/packages.lock.json')">true</RestoreLockedMode>
```

- **`GITHUB_ACTIONS`, not `ContinuousIntegrationBuild`.** SDD forces `ContinuousIntegrationBuild=true`
  unconditionally for determinism, so a CIB gate fails-closed on a fresh local clone. `GITHUB_ACTIONS`
  is the real CI signal present in every repo's workflow and is never forced on locally. This unifies
  the prior CIB-vs-`GITHUB_ACTIONS` divergence (Rendering was the lone CIB outlier — ADR-0006).
- **`And Exists(...lockfile)`** keeps a first restore bootstrappable: a brand-new project generates
  its `packages.lock.json` instead of failing before one exists.

Net effect: any stale or silently substituted dependency version fails restore **in CI** in every
repo, while local development stays unblocked.

## The `api-breaking-change-gate` (opt-in; advisory → required)

Pillar 5 of [epic #16](https://github.com/FS-GG/.github/issues/16) ([.github#20](https://github.com/FS-GG/.github/issues/20)):
catch an accidental public-API break on a packable library (`FS.GG.Contracts`, `FS.GG.UI.*`) so it
forces a **SemVer major** — which the registry version ranges then enforce — instead of slipping out
in a patch and silently breaking a consumer.

Two mechanisms, both carried by the shared config:

- **`Microsoft.CodeAnalysis.PublicApiAnalyzers`** — tracks the declared public surface in committed
  `PublicAPI.Shipped.txt` / `PublicAPI.Unshipped.txt` files; flags any addition/removal not recorded
  there. Centrally pinned (`3.3.4`); analyzer-only (`PrivateAssets=all`).
- **Package Validation** — the SDK-integrated `ApiCompat`. With `EnablePackageValidation` on, `pack`
  compares the package against the last published baseline (`PackageValidationBaselineVersion`) and
  fails on a break. No package reference needed.

### Off by default — turning it on is an adoption step

The gate is **off** unless a repo sets `FsggApiGate` in `Directory.Build.local.props`. It is off by
default on purpose: enabling it adds a `PackageReference`, which changes the restore graph and so the
committed `packages.lock.json`. A repo turns it on deliberately and **regenerates its lockfile** in
the same change, keeping a plain re-sync of the shared config non-breaking under locked restore.

| `FsggApiGate` | Effect |
|---|---|
| unset / `false` | Off (default). |
| `advisory` / `true` | Analyzer on; its diagnostics stay **warnings** even under the repo's `TreatWarningsAsErrors`, so adoption never breaks a build. Add `PublicAPI.{Shipped,Unshipped}.txt` per packable project (the analyzer's code-fix generates them). |
| `required` | The same diagnostics become **build-breaking**; a repo that also sets `PackageValidationBaselineVersion` gets `ApiCompat` at `pack`. |

Adoption is sequenced per repo as H3 follow-ups (the consumer-repo children of
[.github#20](https://github.com/FS-GG/.github/issues/20)): start `advisory`, commit the API baselines,
then ratchet to `required` once the surface is stable.

## Adoption model — sync, don't fork

The canonical files import a repo-local override that is **not** managed by the sync, so repo-specific
settings survive:

```
Directory.Build.props      (synced; DO NOT EDIT)  ──imports──▶  Directory.Build.local.props      (repo-owned)
Directory.Packages.props   (synced; DO NOT EDIT)  ──imports──▶  Directory.Packages.local.props   (repo-owned)
```

Put repo-specific properties (`TargetFramework`, package metadata, F# warning promotions, fsdocs, …)
and repo-specific `PackageVersion` items in the `*.local.props` files. The import is **last**, so a
repo can override any org default. A package pinned in the org baseline (`FSharp.Core`) must **not** be
re-declared locally — CPM raises `NU1504`/`NU1011` on a duplicate `PackageVersion`.

## How an update reaches you — the FS.GG.Kit package (ADR-0062, #1262)

Build config is **no longer byte-copied.** It ships inside the versioned **FS.GG.Kit** package. Your repo
references it at a pinned version (`.config/kit/FS.GG.Kit.receiver.proj`, with
`FsggKitMaterializeBuildConfig=true`) and **materializes** `Directory.Build.props` /
`Directory.Packages.props` onto disk from that pin. A change to `dist/dotnet/` here is one *publish* of a
new `FS.GG.Kit`; **Renovate** then opens the pin bump in your repo and `kit-materialize.yml` re-materializes
the committed files on that PR — the same auto-update fabric every other shared artifact already uses. There
is no rolling `build-config/sync` PR and no hand-maintained `paths:` filter any more.

> **Retired (ADR-0062).** The byte-copy `build-config-propagate.yml` push arm (#626) and the
> `.config/fsgg-build-config.sha` provenance pin (#592 / ADR-0036) are **gone**, deleted from every
> receiver. They existed only to make a byte-copy drift check safe: the check once raced `.github@main` and
> twice froze a receiver's merges ([#499](https://github.com/FS-GG/.github/issues/499),
> [#536](https://github.com/FS-GG/.github/issues/536) — an **XML comment** was enough), so #592 pinned the
> check to the commit the files came from and #626 added a push so receivers weren't frozen waiting for a
> hand-sync. A pinned *package* dependency does not race a moving branch, so the pin, the push, and the
> pin-based check all retire together. [`scripts/sync-build-config.sh`](../../scripts/sync-build-config.sh)
> survives only as the package's `FILES`/marker **derive source** (ADR-0058) — it is no longer run by any
> workflow.

The roster invariant still holds: **a repo receives `build-config` iff it enforces it** — now by
materializing the package and gating on it. `templates`, `audio`, and `net` are **deliberately not
build-config receivers** (`templates` has no `Directory.Build.props`; `audio`/`net` hand-author their own),
so the package's build-config half is opt-in and off for them.

## The drift check now measures against your PINNED FS.GG.Kit

Your `gate.yml` still runs a **`build-config-drift`** job — its `name:` unchanged, so it is the same
required status check — but its body no longer byte-compares against `.github`. It materializes the kit and
asserts your committed `.props` still match it:

```sh
dotnet build .config/kit/FS.GG.Kit.receiver.proj -t:FsggKitMaterialize
git diff --quiet -- Directory.Build.props Directory.Packages.props   # non-empty ⇒ drift
```

| `build-config-drift` says | It means | You do |
|---|---|---|
| **green** | Your committed `.props` match the `FS.GG.Kit` you pin. | Nothing. |
| **red** (`git diff` non-empty) | A managed `.props` was **hand-edited** — the materialize overwrote it back to canonical, so it differs from what you committed. | Take the Renovate `FS.GG.Kit` bump, or run the materialize above and commit. Put repo-specific settings in `*.local.props`, which the materialize never touches. |

Being **behind** the org baseline is now just being on an older `FS.GG.Kit` pin — a visible version-pin
decision, answered like any dependency, not an invisible drift. Renovate opens the bump when a new kit
publishes.

## Commands

```sh
# Materialize your build config (and the rest of the kit) from the pinned FS.GG.Kit, then commit the result.
# This is what kit-materialize.yml runs on a Renovate bump; run it by hand to refresh out of band.
dotnet build .config/kit/FS.GG.Kit.receiver.proj -t:FsggKitMaterialize

# First-time build-config adoption of a hand-authored *.props (moves it to *.local.props, imported):
dotnet build .config/kit/FS.GG.Kit.receiver.proj -t:FsggKitMaterialize -p:FsggKitAdoptBuildConfig=true
```

`sync-build-config.sh` is retained as the kit's derive source, not a distribution tool; do not run it to
sync a receiver. See [`src/FS.GG.Kit/README.md`](../../src/FS.GG.Kit/README.md) for the package.

## Per-repo status

All four build-config receivers (`sdd`, `rendering`, `governance`, `game`) materialize their `.props` from
a pinned `FS.GG.Kit` and gate on it via `build-config-drift`. Adoption is complete; currency is Renovate's
job, visible as an ordinary pin bump.

| Repo | Action on adoption |
|---|---|
| Rendering | Migrate gate CIB→`GITHUB_ACTIONS`; move rich metadata/fsdocs/F# warnings to `*.local.props`. |
| SDD | Adopt files (gate already `GITHUB_ACTIONS`); drop local `FSharp.Core` pin. |
| Governance | Adopt files (gate already `GITHUB_ACTIONS`); drop local `FSharp.Core` pin. |
| Templates | Adopt as a forward guardrail (packaging-only; empty lockfile, as today). |
