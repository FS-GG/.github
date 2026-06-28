# The contract-coherence gate

A single reusable GitHub Actions workflow — [`contract-coherence.yml`](../../.github/workflows/contract-coherence.yml)
(`workflow_call`) — that every FS-GG repo's CI calls to fail fast on cross-repo drift.
Delivered by [.github#18](https://github.com/FS-GG/.github/issues/18) (H3, [epic #16](https://github.com/FS-GG/.github/issues/16) Pillar 3).

It is the **enforcement arm** of the coordination layers: ADRs decide *why*, the
[registry](../../registry/dependencies.yml) declares *what* (contracts), and this gate makes
a repo's CI go red when reality stops matching the registry.

## What it checks

1. **Registry schema** — [`scripts/validate-registry.py`](../../scripts/validate-registry.py)
   validates the org registry's real on-disk grammar (required fields, known repos, well-formed
   versions, no duplicate ids), mirroring the rule *kinds* of FS.GG.Contracts' `Fsgg.Registry`
   validator (`MissingField` / `UnknownComponent` / `MalformedVersion`).
2. **`fsgg-contracts` pin drift** — asserts the registry's declared `fsgg-contracts` version
   equals the **actual** `FS.GG.Contracts` package version read from FS-GG/FS.GG.SDD (the schema
   authority — both its `.fsproj <Version>` and `ContractVersion.value`). Fails on any mismatch.
3. **Build-config drift** — runs [`scripts/sync-build-config.sh --check`](../../scripts/sync-build-config.sh)
   so a repo that hand-edited or never re-synced a managed `Directory.*.props` / tool manifest goes
   red (`shared-build-config@1.0.0`). Skipped for non-consumers via `check-build-config: false`.
4. **Source XML well-formedness** — guards the `.github#29` defect class (a malformed-but-verbatim
   `.props` that passes the byte-for-byte drift check yet breaks every adopter's restore).

The registry + scripts are sourced from public FS-GG/.github; `FS.GG.Contracts` is read from public
FS-GG/FS.GG.SDD. **No org token required** — this intentionally sidesteps the H4 feed/App-token
blocker ([#21](https://github.com/FS-GG/.github/issues/21)).

> **Known gap** (coherence id `registry-validator-typed`, [FS.GG.SDD#12](https://github.com/FS-GG/FS.GG.SDD/issues/12)):
> the schema check is a Python stand-in because `Fsgg.Registry` has no YAML loader and its abstract
> model/version-grammar diverge from the real registry file. When SDD converges the typed validator,
> #18 swaps the stand-in for it. The typed version-coupling (check 2) is already live.

## Adoption — wiring it into a consumer repo's CI

Add a job to the repo's existing CI workflow (the four product repos set `check-build-config: true`):

```yaml
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
