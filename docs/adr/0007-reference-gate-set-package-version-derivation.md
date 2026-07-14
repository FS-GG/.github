# ADR-0007: `FS.GG.Governance.ReferenceGateSet` version-derivation rule

- **Status:** Accepted
- **Date:** 2026-06-28
- **Affects:** governance (producer), templates (consumer)

## Context

The validated reference `.fsgg` gate set at
`FS.GG.Governance/samples/sdd-reference-gate-set/.fsgg/` (the populated set frozen by the G1–G7
reference-set guard; coherence id `governance-overlay-populated`) is being published as a
content-only NuGet package, `FS.GG.Governance.ReferenceGateSet`, so the Templates overlay drift
gate ([Templates#14](https://github.com/FS-GG/FS.GG.Templates/issues/14)) has **one published,
versioned source of truth** to `git diff --exit-code` against instead of a hand-copied overlay
([Governance#15](https://github.com/FS-GG/FS.GG.Governance/issues/15), H3, [epic
#16](https://github.com/FS-GG/.github/issues/16) Pillar 3).

The bundle is four files, and they do **not** share one schema version
(`capabilities.yml` is `schemaVersion: 2`; `governance.yml`, `policy.yml`, `tooling.yml` are `1`).
A consumer that pins this package wants the version to **reflect the contained schema generations**
so a schema bump is visible as a new, pinnable package version (FR-006 / SC-003). There was no
single obvious numbering; this ADR fixes one so the rule is a registered contract, not an
implementation detail that can silently drift.

## Decision

The package version is the four contained `schemaVersion` values, composed as a **4-segment
NuGet version in a fixed file order** (manifest root first):

```
Version = "{governance}.{capabilities}.{policy}.{tooling}"
        =       1      .      2        .   1     .   1       =  1.2.1.1   (current)
```

- **Deterministic.** Identical schema versions always yield the identical string — no clock, no
  environment, no build counter input. Reproducible across machines and CI.
- **Distinguishable.** A bump to *any one* file's `schemaVersion` changes *exactly one* segment
  (e.g. a `policy.yml` bump `1 → 2` ⇒ `1.2.2.1`), so every schema change is a distinct package
  version — structurally, not probabilistically.
- **Legible.** Each file's schema generation is independently visible in the version; the leading
  segment tracks the `governance.yml` manifest generation.
- **Exact-pin recommended.** Consumers SHOULD pin exact (`[1.2.1.1]`) to lock a coherent reference
  set.

The rule is implemented **once** in `FS.GG.Governance/pack-reference-gate-set.fsx` and exposed for
test via a `--print-version` dry-run hook, so the guard asserts the *actual emitted* version rather
than a re-encoded copy of the rule. Production is gated on the G1–G7 reference-set guard: the
package cannot be produced when those invariants are red, so the shipped artifact is provably the
tested artifact.

Alternatives rejected: a single SemVer with the tuple in build metadata (loses fidelity — two
different bumps can collide, and build metadata is ignored by version precedence); a content hash
suffix (distinguishable but illegible and not "schema-version derived"); a lossy 3-segment packing
(`gov.caps.(policy*10+tooling)`) that breaks once a schema exceeds 9.

## Consequences

- **governance** owns the rule and its single implementation (`pack-reference-gate-set.fsx`), the
  packaging project, and the guard that proves byte-identity + content-only + the derived version.
  A future `schemaVersion` bump to any of the four files automatically yields a new, distinguishable
  package version with no manual version edit.
- **templates** ([Templates#14](https://github.com/FS-GG/FS.GG.Templates/issues/14)) consumes the
  package as the authoritative source for its overlay drift gate, pinning exact.
- The contract is recorded in the registry as `governance-reference-gate-set` (consumed by
  templates). ~~The **org GitHub Packages feed push is deferred** (admin-blocked,
  [.github#21](https://github.com/FS-GG/.github/issues/21)); until it lands, a consumable artifact
  via local/CI `dotnet pack` to `~/.local/share/nuget-local/` is the done-definition. The registry
  entry records the deferred-feed status.~~

  > **Retired (2026-07-14) — the feed push landed, and this bullet outlived it.** `.github#21` is
  > closed: the package is **live on the org feed** at `package-version: "1.2.1.1"`
  > (`registry/dependencies.yml`), and `scripts/check-feed-coherence.py` now **enforces** that
  > scalar against the live feed on every run. The local-`dotnet pack` fallback was a done-definition
  > for a blocked feed; there is no blocked feed. It is recorded here because the claim did not stay
  > in this ADR — the `publishing-and-deployment` skill mirrored it forward as *"the acceptance bar
  > if you're wiring a new package"*, which is how a stale consequence in an Accepted record becomes
  > live advice. That section is deleted from the skill in the same change.
- The version-derivation rule is now a **versioned contract**: changing it (segment order, segment
  count, or source) is a `contract-change` that must update the registry and supersede this ADR.
