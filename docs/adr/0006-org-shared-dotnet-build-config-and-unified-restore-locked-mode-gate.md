# ADR-0006: `.github` owns the org-shared .NET build config; `RestoreLockedMode` gates on `GITHUB_ACTIONS`

- **Status:** Accepted
- **Date:** 2026-06-28
- **Affects:** .github, FS.GG.Rendering, FS.GG.SDD, FS.GG.Governance, FS.GG.Templates

## Context

Pillar 5 of the homogeneous-build epic ([.github#16](https://github.com/FS-GG/.github/issues/16))
calls out packaging as per-repo copy-paste. Two concrete symptoms:

1. **The `lockfile-restore-enforcement` contract is coherent but divergent.** Every .NET repo
   commits `packages.lock.json` and restores in locked mode in CI, but the *gate condition*
   drifted:
   - **Rendering** gates `RestoreLockedMode` on `ContinuousIntegrationBuild` (CIB).
   - **SDD** and **Governance** gate on `GITHUB_ACTIONS` — because SDD sets
     `ContinuousIntegrationBuild=true` *unconditionally* (for deterministic builds), so a CIB
     gate would also fire on a fresh local clone and wedge the first restore before a lockfile
     exists.
   - **Templates** has no root `Directory.Build.props` at all (packaging-only repo).

   Same intent, four spellings — exactly the "duplicated facts" the epic exists to kill.

2. **MSBuild props, CPM enablement, and the tools manifest are hand-copied per repo.** There is
   no source of truth, so determinism settings, CPM/transitive-pinning, the `NU1603`/`NU1608`
   promotions, and the (absent) `dotnet tool` manifest drift independently.

The forces: the gate must be **on in CI** (so drift fails the build) and **off on a fresh local
clone** (so a first `restore` can bootstrap the lockfile instead of blocking). `GITHUB_ACTIONS`
is the only CI signal that is true in every repo's workflow and is *not* forced on locally by any
repo; CIB fails the second requirement wherever a repo forces it for determinism.

## Decision

1. **`.github` is the source of truth for the org-shared .NET build config.** It publishes the
   canonical files under [`dist/dotnet/`](../../dist/dotnet/):
   - `Directory.Build.props` — determinism, CPM + transitive pinning, the
     `lockfile-restore-enforcement` gate, `NU1603`/`NU1608`→error.
   - `Directory.Packages.props` — CPM baseline + the org-wide `FSharp.Core` pin (`10.1.301`).
   - `.config/dotnet-tools.json` — pinned tool manifest (`fake-cli` `6.1.4`, matching Rendering's
     `Fake.Core.*` library pin).

2. **The unified gate is `GITHUB_ACTIONS`, not `ContinuousIntegrationBuild`:**
   ```xml
   <RestoreLockedMode Condition="'$(GITHUB_ACTIONS)' == 'true' And Exists('$(MSBuildProjectDirectory)/packages.lock.json')">true</RestoreLockedMode>
   ```
   This canonicalises the SDD/Governance spelling and **supersedes Rendering's CIB gate**. The
   `Exists(lockfile)` clause keeps a first restore bootstrappable.

3. **Adoption is by sync, not fork.** Repos take the canonical files verbatim via
   [`scripts/sync-build-config.sh`](../../scripts/sync-build-config.sh); repo-specific settings
   move into `Directory.Build.local.props` / `Directory.Packages.local.props`, which the canonical
   files import last (so a repo can still override any default). The script's `--check` mode is the
   drift gate consumed by the reusable coherence workflow ([.github#18](https://github.com/FS-GG/.github/issues/18)).

## Consequences

- **Rendering** migrates its `RestoreLockedMode` gate `ContinuousIntegrationBuild` →
  `GITHUB_ACTIONS` and adopts the synced files (its rich package metadata / fsdocs / F# warning
  promotions move to `Directory.Build.local.props`). This is the only behavioural change; the
  others already gate on `GITHUB_ACTIONS`.
- **SDD** and **Governance** adopt the synced files; their gate condition is unchanged. Each
  removes its local `FSharp.Core` pin in favour of the org baseline (CPM forbids a duplicate
  `PackageVersion`).
- **Templates** gains the synced files as a forward guardrail (its packaging project still has no
  `PackageReference`s → empty lockfile, as today).
- **The registry** records `.github` as the source of truth: the `lockfile-restore-enforcement`
  coherence note is updated to the unified `GITHUB_ACTIONS` gate, and a new `shared-build-config`
  contract is added (owner `.github`, consumers = all four). `registry/dependencies.yml` +
  `docs/registry/compatibility.md` are updated and `updated:` bumped.
- Per-repo adoption is sequenced as H3 follow-ups on the Coordination board; until a repo re-syncs,
  its `--check` gate (once wired by [.github#18](https://github.com/FS-GG/.github/issues/18)) flags
  the drift rather than silently diverging.
