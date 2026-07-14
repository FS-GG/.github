# Org-shared .NET build configuration

The canonical MSBuild / CPM / tool-manifest baseline for every FS-GG .NET repo. The source of
truth is [`dist/dotnet/`](../../dist/dotnet/) in this repo; consumer repos take the files
**verbatim** via [`scripts/sync-build-config.sh`](../../scripts/sync-build-config.sh) rather than
hand-copying. Decision record: [ADR-0006](../adr/0006-org-shared-dotnet-build-config-and-unified-restore-locked-mode-gate.md).
Contracts: `shared-build-config` + `lockfile-restore-enforcement`
([registry](../../registry/dependencies.yml), [projection](../registry/compatibility.md)).

## What is distributed

| File | Purpose |
|---|---|
| `Directory.Build.props` | Deterministic builds; Central Package Management (CPM) + transitive pinning; the `lockfile-restore-enforcement` gate; `NU1603`/`NU1608`→error; the opt-in `api-breaking-change-gate`. |
| `Directory.Packages.props` | CPM enablement + the org-wide `FSharp.Core` pin (`10.1.301`) + the centrally-pinned `PublicApiAnalyzers` (`3.3.4`) for the gate. |
| `.config/dotnet-tools.json` | Pinned local tool manifest (`fake-cli` `6.1.4`, matching the `Fake.Core.*` library pin). |
| `.config/fsgg-build-config.sha` | **The provenance pin** ([#592](https://github.com/FS-GG/.github/issues/592), ADR-0036): which `.github` commit the three files above were synced from. Written by the syncer, bumped by the bot, **read by the drift check** — see below. Not byte-identical across repos, and deliberately not a "managed file" in the byte-identity sense. |

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

## How an update reaches you — the push arm (#626)

`build-config-propagate.yml` opens a rolling `build-config/sync` PR, with auto-merge armed, in every
`receives: build-config` repo whenever `dist/dotnet/` (or the syncer itself) changes on `.github@main`.
The receiver list comes from `registry/repos.yml` — no hardcoded targets. The PR carries the managed
files **and the pin** (`.config/fsgg-build-config.sha`) in one diff, which is what keeps the two
coherent: the files a repo has and the baseline it is judged against always land together.

**The push arm was necessary and not sufficient — #592 finished the job.** Shipping it shortened the
freeze; it could not remove it. The drift check still raced `.github@main`, so an edit here still red-lit
every open PR in a receiver until the sync PR landed there — and the freeze stayed *indefinitely*
reachable, because a sync PR whose own `gate` job goes red (a config change that moves the restore graph
needs a lockfile regenerated in the same PR) never auto-merges. Pinning the check (above) is what made
the freeze structurally impossible rather than merely brief. This workflow's job is now **currency, not
rescue**: when it fails, a repo is drifting out of date — it is no longer a repo that cannot merge.

**This did not exist until #626, and its absence was a ratchet.** The drift check has been failing PRs in
the four adopting repos for months; nothing ever sent them the update. So every edit to `dist/dotnet/`
red-lit all four until a human hand-synced each one — and because the coordination kit's sync PR is a
*rolling* branch, that red landed on the kit's own delivery vehicle. #627 added the coordination engine
to the shared tool manifest, and a day later FS.GG.SDD's kit-sync PR was still blocked by the resulting
drift: the one distribution fabric in this org that demonstrably runs was frozen out of the largest repo
([#634](https://github.com/FS-GG/.github/issues/634) found it stuck). The enforcement arm had taken the
delivery arm hostage.

The invariant the roster now asserts: **a repo receives `build-config` iff it enforces `build-config`.**
Both directions bite. A repo that enforces without receiving can only go red and stay red. A repo that
receives without having adopted gets build files written into it by a bot — which is why `templates` (no
`Directory.Build.props` at all) and `audio` (a hand-authored one, which [#387](https://github.com/FS-GG/.github/issues/387)'s
guard refuses to overwrite) are **deliberately not receivers**. Onboarding either is a `--adopt`, below —
a decision about somebody else's build, and not a propagation.

Nothing yet verifies that symmetry automatically: `repos-audit`'s mandate covers only capabilities wired
by a *reusable workflow*, and this one is wired by an inline `run:` in each receiver's `gate.yml`. That
gap is [#628](https://github.com/FS-GG/.github/issues/628) — and it is what let *"four repos enforce it,
the registry says zero"* go unnoticed.

## The drift check measures against your PIN, not against `main` (#592, ADR-0036)

Your `gate.yml` checks this repo out at `ref: main` and runs `sync-build-config.sh --check`. For as
long as that check compared your files against `dist/dotnet/` **as of main**, its answer was a function
of *another repo's moving branch, at the moment your CI happened to run*. You could not make it green
from your own PR — and the instant anything landed in `dist/dotnet/` here, **every open PR in your repo
went red on a required check**, through no fault of its own.

That is a race, not a drift check, and it fired twice: [#499](https://github.com/FS-GG/.github/issues/499)
froze FS.GG.SDD for hours, and [#536](https://github.com/FS-GG/.github/issues/536) then red-lit a green,
in-flight PR by editing an **XML comment**. A comment was enough.

So your repo now commits `.config/fsgg-build-config.sha` — the `.github` commit your managed files came
from — and `--check` diffs them against **that commit's** `dist/dotnet/`. Its verdict now depends only
on **your own tree**, which arrives on your PR's merge ref. Nothing we do to `main` can move it.

| `--check` says | It means | You do |
|---|---|---|
| **green**, with a `NOTICE: … BEHIND the org baseline` | Your files faithfully match your pin. `.github` has moved on since. | **Nothing.** Being behind is not a defect in your PR. The bot's sync PR (below) closes the gap and bumps the pin with the files. |
| **red**, `DRIFT (differs)` | A managed file has been **hand-edited in your repo**. | Re-sync (below). Put repo-specific settings in `*.local.props`, which the sync never touches. |

Two things worth knowing:

- **Being behind is green, on purpose.** This is a deliberate inversion — for a *required* check,
  stale-until-someone-merges beats merge-frozen-by-default. There is no staleness bound, because a
  bound would reintroduce a failure that depends on time and on our commit rate rather than on your
  tree. Currency is the bot's job.
- **No pin yet? Nothing changes.** An absent `.config/fsgg-build-config.sha` means *legacy mode* — the
  old compare-against-`main` behaviour, exactly as before. Your repo stays there until the propagate
  bot's next sync PR writes the pin. **You have no adoption step to do**, and this rollout cannot
  freeze you.

## Commands

```sh
# First-time adoption: move an existing hand-authored *.props to *.local.props, then write canonical.
scripts/sync-build-config.sh --adopt /path/to/FS.GG.Rendering

# Re-sync after the source of truth changes here. Also (re)writes .config/fsgg-build-config.sha.
scripts/sync-build-config.sh /path/to/FS.GG.Rendering

# Drift check (exit 1 on drift) — the hook the reusable coherence workflow (.github#18) runs in CI.
# Compares against the target's pin when it has one; against main when it does not.
scripts/sync-build-config.sh --check /path/to/FS.GG.Rendering
```

The pin is written **only on a fully clean sync**, and only when the commit being distributed is
knowable — a `dist/dotnet/` with uncommitted changes yields *no* pin rather than a false one, since
those files match no commit. Set `FSGG_BUILD_CONFIG_SHA=<sha>` to state it explicitly (which is what
`build-config-propagate.yml` does).

## Per-repo adoption status

Adoption is sequenced as H3 follow-ups on the Coordination board. Until a repo re-syncs and its
`--check` gate is wired (via the reusable [contract-coherence gate](../coordination/contract-coherence-gate.md),
[.github#18](https://github.com/FS-GG/.github/issues/18)), drift is flagged rather than silently tolerated.

| Repo | Action on adoption |
|---|---|
| Rendering | Migrate gate CIB→`GITHUB_ACTIONS`; move rich metadata/fsdocs/F# warnings to `*.local.props`. |
| SDD | Adopt files (gate already `GITHUB_ACTIONS`); drop local `FSharp.Core` pin. |
| Governance | Adopt files (gate already `GITHUB_ACTIONS`); drop local `FSharp.Core` pin. |
| Templates | Adopt as a forward guardrail (packaging-only; empty lockfile, as today). |
