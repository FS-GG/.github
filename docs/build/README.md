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
| `Directory.Build.props` | Deterministic builds; Central Package Management (CPM) + transitive pinning; the `lockfile-restore-enforcement` gate; `NU1603`/`NU1608`→error. |
| `Directory.Packages.props` | CPM enablement + the org-wide `FSharp.Core` pin (`10.1.301`). |
| `.config/dotnet-tools.json` | Pinned local tool manifest (`fake-cli` `6.1.4`, matching the `Fake.Core.*` library pin). |

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

## Commands

```sh
# First-time adoption: move an existing hand-authored *.props to *.local.props, then write canonical.
scripts/sync-build-config.sh --adopt /path/to/FS.GG.Rendering

# Re-sync after the source of truth changes here.
scripts/sync-build-config.sh /path/to/FS.GG.Rendering

# Drift check (exit 1 on drift) — the hook the reusable coherence workflow (.github#18) runs in CI.
scripts/sync-build-config.sh --check /path/to/FS.GG.Rendering
```

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
