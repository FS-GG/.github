# The contract-coherence gate

A single reusable GitHub Actions workflow — [`contract-coherence.yml`](../../.github/workflows/contract-coherence.yml)
(`workflow_call`) — that every FS-GG repo's CI calls to fail fast on cross-repo drift.
Delivered by [.github#18](https://github.com/FS-GG/.github/issues/18) (H3, [epic #16](https://github.com/FS-GG/.github/issues/16) Pillar 3).

It is the **enforcement arm** of the coordination layers: ADRs decide *why*, the
[registry](../../registry/dependencies.yml) declares *what* (contracts), and this gate makes
a repo's CI go red when reality stops matching the registry.

## What it checks

1. **Registry schema** — the **typed** `Fsgg.Registry` validator (`fsgg-sdd registry validate`,
   shipped in `FS.GG.Contracts` via the `FS.GG.SDD.Cli` tool) validates the org registry's real
   on-disk grammar (required fields, known repos, well-formed versions, no duplicate ids):
   `RegistryDocument.load` (YAML) + `validateDocument`, emitting `MissingField` /
   `UnknownComponent` / `MalformedVersion` / `DuplicateComponent` / `MalformedDocument`. This
   **replaced** the `scripts/validate-registry.py` stand-in once the typed validator reached
   parity ([.github#49](https://github.com/FS-GG/.github/issues/49); 4-segment version grammar
   fixed in [SDD#32](https://github.com/FS-GG/FS.GG.SDD/issues/32), CLI `0.2.1`).
2. **`fsgg-contracts` pin drift** — asserts the registry's declared `fsgg-contracts` version
   equals the **actual** `FS.GG.Contracts` package version read from FS-GG/FS.GG.SDD (the schema
   authority — both its `.fsproj <Version>` and `ContractVersion.value`). Fails on any mismatch.
3. **Build-config drift** — runs [`scripts/sync-build-config.sh --check`](../../scripts/sync-build-config.sh)
   so a repo that hand-edited or never re-synced a managed `Directory.*.props` / tool manifest goes
   red (`shared-build-config@1.0.0`). Skipped for non-consumers via `check-build-config: false`.
4. **Source XML well-formedness** — guards the `.github#29` defect class (a malformed-but-verbatim
   `.props` that passes the byte-for-byte drift check yet breaks every adopter's restore).

The registry + scripts are sourced from public FS-GG/.github; `FS.GG.Contracts` is read from public
FS-GG/FS.GG.SDD. The typed validator (check 1) is restored as a .NET tool from the org GitHub
Packages feed, so every caller must grant **`packages: read`** (the run-scoped `GITHUB_TOKEN` then
authenticates the NuGet restore — the package is public, but the feed still requires a token). This
became viable once the H4 feed/App-token provisioning ([#21](https://github.com/FS-GG/.github/issues/21))
closed.

> **Resolved** (coherence id `registry-validator-typed`, [.github#49](https://github.com/FS-GG/.github/issues/49)):
> the schema check is now the typed `Fsgg.Registry` validator, not a Python stand-in. SDD#26 gave
> `Fsgg.Registry` a real `RegistryDocument.load` (YAML) + `validateDocument` + a `fsgg-sdd registry
> validate` CLI; [SDD#32](https://github.com/FS-GG/FS.GG.SDD/issues/32) (CLI `0.2.1`) fixed the last
> divergence (4-segment versions such as `1.2.1.1`), restoring byte-for-byte parity, and #49 wired
> the gate onto it. The typed version-coupling (check 2) was already live.

## Adoption — wiring it into a consumer repo's CI

Add a job to the repo's existing CI workflow (the four product repos set `check-build-config: true`).
Grant **`packages: read`** so the gate can restore the typed validator from the org feed — a called
workflow's `GITHUB_TOKEN` scopes are capped by the caller, so it must be granted here:

```yaml
permissions:
  contents: read
  packages: read            # required: the gate restores the fsgg-sdd validator from the org feed
jobs:
  contract-coherence:
    uses: FS-GG/.github/.github/workflows/contract-coherence.yml@main
    with:
      check-build-config: true
      # repo-path: "."          # path to drift-check, if not the repo root
```

`.github` gates itself with [`coherence.yml`](../../.github/workflows/coherence.yml) (the local
`./.github/workflows/contract-coherence.yml@<ref>` form) and `check-build-config: false`, since it
**owns** the build config rather than consuming it.
