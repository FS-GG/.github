# ADR-0006: `.github` owns the org-shared .NET build config; `RestoreLockedMode` gates on `GITHUB_ACTIONS`

- **Status:** Accepted — **§Decision 3's `--check` drift gate is superseded by
  [ADR-0036](0036-the-build-config-drift-check-pins-its-source.md)**; §Decision 1 (`.github` is the
  source of truth) and §Decision 2 (the `GITHUB_ACTIONS` gate) stand.
- **Date:** 2026-06-28
- **Affects:** .github, FS.GG.Rendering, FS.GG.SDD, FS.GG.Governance, FS.GG.Templates
- **Amended by:** [ADR-0036](0036-the-build-config-drift-check-pins-its-source.md) — `--check`'s
  semantics are **inverted**: it diffs against the receiver's committed
  `.config/fsgg-build-config.sha` pin, not against `.github@main`. See §Decision 3.

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

   > **Amendment (2026-07-14).** The canonical set is **no longer three files**. `dist/dotnet/` now
   > also holds **`global.json`** ([#536](https://github.com/FS-GG/.github/issues/536)) — four
   > canonical files — and, per
   > [ADR-0036](0036-the-build-config-drift-check-pins-its-source.md), each receiver additionally
   > carries **`.config/fsgg-build-config.sha`**, the provenance pin `--check` now compares against.
   > Neither is in `sync-build-config.sh`'s `FILES` list, and both omissions are deliberate: the pin
   > is excluded by design — `--check` treats a missing member of `FILES` as drift, so listing it
   > would red-light every unpinned receiver on day one — and `global.json` is **unmanaged**, per the
   > amendment below.
   >
   > **Amendment (2026-07-17) — `global.json` is UNMANAGED, and that is settled**
   > ([#903](https://github.com/FS-GG/.github/issues/903)). This supersedes the "not yet checked,
   > until the per-repo adoption items land" reading above, which described an intention the org has
   > since dropped. **There is no pending step here.** `global.json` is distributed under
   > `dist/dotnet/` and will not join `FILES`; per-repo SDK bands are legitimate.
   >
   > The rollout ([#561](https://github.com/FS-GG/.github/issues/561)) did complete its adoption
   > phase: every consumer carries a `global.json`, and #561's four children adopted byte-identically
   > at the canonical `10.0.301` of the day. Renovate then bumped the **canonical** to `10.0.302`
   > (`bff95e4`, [#804](https://github.com/FS-GG/.github/issues/804)) and bumped only some receivers
   > in their own repos. Because `--check` compares **content**, enforcing would have red-lit the
   > receivers that had adopted exactly as instructed. Nothing fans a canonical bump out, so
   > **divergence is the steady state of this file, not an accident** — it re-opens on every SDK
   > patch, and coherence here is not a state you reach once. Add `rollForward: latestFeature`
   > (which makes the pin a floor) and the incompatibility with Renovate's per-repo bumps
   > ([#678](https://github.com/FS-GG/.github/issues/678)), and enforcement costs a standing tax
   > forever to buy a floor.
   >
   > `tests/sync-build-config` asserts the name stays out of `FILES` — a regression test on a decided
   > end state, not a tripwire awaiting a step. **#561 is closed and still carries the un-taken step;
   > a reader who lands there and concludes the rollout stalled should read #903 instead.** To change
   > any of this, re-open #903.

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

   > **Amendment (2026-07-14, [ADR-0036](0036-the-build-config-drift-check-pins-its-source.md),
   > [#592](https://github.com/FS-GG/.github/issues/592)). `--check`'s semantics are INVERTED.** It no
   > longer requires the receiver's managed files to be byte-identical to `dist/dotnet/` as of
   > **`.github@main`**; it diffs them against `dist/dotnet/` **at the commit the receiver's own
   > `.config/fsgg-build-config.sha` pins**. As written below, the verdict was a function of *another
   > repo's moving branch*: a receiver could not green the check from its own PR, and the instant
   > anything landed in `dist/dotnet/` here, every open PR in every adopting repo went red on a
   > **required** check — which fired twice ([#499](https://github.com/FS-GG/.github/issues/499),
   > [#536](https://github.com/FS-GG/.github/issues/536); a *comment* edit was enough). The verdict is
   > now a pure function of the receiver's own tree. **Being behind the pin is GREEN** (and
   > bot-remediated by `build-config-propagate.yml`'s rolling sync PR); only a hand-edited managed
   > file is RED — the only thing a required check can honestly demand. An absent pin means legacy
   > mode (compare against `main`), so nothing froze on rollout. Read the sentence above as history.

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
