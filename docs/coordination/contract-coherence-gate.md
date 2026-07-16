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
2. **Build-config drift** — runs [`scripts/sync-build-config.sh --check`](../../scripts/sync-build-config.sh)
   so a repo that hand-edited or never re-synced a managed `Directory.*.props` / tool manifest goes
   red (`shared-build-config@1.0.0`). Skipped for non-consumers via `check-build-config: false`.
3. **Source XML well-formedness** — guards the `.github#29` defect class (a malformed-but-verbatim
   `.props` that passes the byte-for-byte drift check yet breaks every adopter's restore).

Every one of those is a **pure function of committed files**, and that is the rule this gate now
lives by ([.github#741](https://github.com/FS-GG/.github/issues/741)): six repos call this workflow,
so whatever it asserts is a required check on all of their PRs at once. The only safe subject is
state the caller's own PR can fix.

### What it deliberately no longer checks: `fsgg-contracts` pin drift

It used to assert, as check 2, that the registry's declared `fsgg-contracts` `version` equalled the
`FS.GG.Contracts` version read from **`FS-GG/FS.GG.SDD@main`'s source** — calling that, in its own
error message, *"the actual FS.GG.Contracts package version"*. That was two defects in one line:

- **It wedged the org on every Contracts bump.** The assertion coupled `FS-GG/.github@main`'s
  registry to `FS-GG/FS.GG.SDD@main`'s source, and **no PR spans both repos**. Bump SDD first and
  every caller reds the moment it merges; flip the registry first and every `.github` PR wedges
  instead. There was no landing order without a red window, at `enforce_admins` level — and since
  SDD calls this workflow too, a bare source bump wedged SDD's own merges
  ([FS.GG.SDD#432](https://github.com/FS-GG/FS.GG.SDD/issues/432)).
- **It was vacuously green.** `main` is not a release and a source tree is not a package. When
  SDD#426 grew the Contracts public surface under an unchanged `2.0.0`, the `.nupkg` on the feed and
  the source labelled `2.0.0` became different artifacts — and the gate printed
  `ok: ... == actual package version == 2.0.0`. That is [epic #266](https://github.com/FS-GG/.github/issues/266)'s
  signature: a confident verdict about a subject the code could not see.

**The coupling did not go away — it moved to where it cannot wedge anyone**, and it is now asserted
against the two subjects that are real, by two `.github`-local gates:

| gate | asserts | subject |
|---|---|---|
| [`source-coherence.yml`](../../.github/workflows/source-coherence.yml) | `version` == Contracts **source** SemVer | `FS.GG.SDD@main` |
| [`feed-coherence.yml`](../../.github/workflows/feed-coherence.yml) | `package-version` == newest **live on the feed** | the org NuGet feed |

Both are path-filtered PR + push + **daily schedule** (an SDD bump or a publish touches no file here,
so only a periodic read can see it), and both red only `.github` — the repo that owns the registry
and is the only one that can flip it. The `contracts-ref` input that fed the old check was removed
with it; no caller passed it.

**Do not add an assertion back here that depends on another repo's mutable `main`, or on the network
beyond this run's own restore.** A NuGet outage must not be able to wedge six repos' PRs — which is
also why `feed-coherence` reads the feed from `.github` and not from this workflow.

The registry + scripts are sourced from public FS-GG/.github. The typed validator (check 1) is
restored as a .NET tool from the org GitHub Packages feed, so every caller must grant
**`packages: read`** (the run-scoped `GITHUB_TOKEN` then authenticates the NuGet restore — the
package is public, but the feed still requires a token). This became viable once the H4 feed/App-token
provisioning ([#21](https://github.com/FS-GG/.github/issues/21)) closed.

> **Resolved** (coherence id `registry-validator-typed`, [.github#49](https://github.com/FS-GG/.github/issues/49)):
> the schema check is now the typed `Fsgg.Registry` validator, not a Python stand-in. SDD#26 gave
> `Fsgg.Registry` a real `RegistryDocument.load` (YAML) + `validateDocument` + a `fsgg-sdd registry
> validate` CLI; [SDD#32](https://github.com/FS-GG/FS.GG.SDD/issues/32) (CLI `0.2.1`) fixed the last
> divergence (4-segment versions such as `1.2.1.1`), restoring byte-for-byte parity, and #49 wired
> the gate onto it. (The version-coupling this note also referred to has since moved out of this
> workflow entirely — see *What it deliberately no longer checks*, [.github#741](https://github.com/FS-GG/.github/issues/741).)

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
