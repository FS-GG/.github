# ADR-0055: `FS.GG.Governance.ReferenceGateSet` versioning — plain SemVer with an in-package schema manifest

- **Status:** Accepted
- **Date:** 2026-07-19
- **Affects:** FS.GG.Governance (producer — owns the rule, its single implementation, and the guard), FS.GG.Templates (consumer — the overlay drift gate, [Templates#14](https://github.com/FS-GG/FS.GG.Templates/issues/14)), `.github` (the `governance-reference-gate-set` registry contract + this ADR)
- **Supersedes:** [ADR-0007](0007-reference-gate-set-package-version-derivation.md) — replaces its 4-segment schema-derived version rule **in full**. ADR-0007's *other* facts (the package exists, packs the four files in place, is byte-identical to `samples/sdd-reference-gate-set/.fsgg/`, is gated on the G1–G7 reference-set guard, and is consumed exact-pinned by Templates) all stand; only the **version-derivation rule** is retired.

## Context

ADR-0007 derived the package version from the four contained `schemaVersion` values, composed as a
4-segment NuGet version in a fixed file order:

```
Version = "{governance}.{capabilities}.{policy}.{tooling}" = 1.2.1.1
```

The rule **cannot represent a content-only change** to the packed `.fsgg` set, and that is not
hypothetical — it is already blocking a shipped change ([#1228](https://github.com/FS-GG/.github/issues/1228)).
**WI-8** ([Governance#276](https://github.com/FS-GG/FS.GG.Governance/issues/276)) added a
`gameplay` domain + an `fr-covered` block-on-ship check to the packed `capabilities.yml` /
`governance.yml`. That content is **valid under the existing schemas** — it added no field, enum
case, or shape, and the WI-8 commit did not touch `Schema.fs` — so **no `schemaVersion`
legitimately changed**, and the derivation still yields `1.2.1.1`. The post-WI-8 package is
therefore **byte-different but version-identical** to the pre-WI-8 one: a republish `--skip-duplicate`s
it, and the drift is invisible to the version rule. `FS.GG.Governance.Cli 1.6.0` shipped WI-8, but
the gate-set was deliberately held at `1.2.1.1` rather than fake a schema bump.

The obvious repairs are both wrong:

- **Bumping a `schemaVersion` to force a segment change is dishonest** — a "v3" capabilities schema
  would be byte-shape-identical to v2 (WI-8 changed no shape); the version would claim the schema
  *generation* changed when only *content* did — **and fleet-breaking**: the loader gates
  `schemaVersion` by **exact match** (`FS.GG.Governance.Config/Schema.fs`, `readSchemaVersion`),
  single-sourced from `Fsgg.Schemas.capabilitiesVersion` in FS.GG.Contracts, so a bump instantly
  rejects **every `capabilities.yml` still at v2** — the Governance sample, `FS.GG.Game/.fsgg`, the
  `FS.GG.Templates` scaffold (so every newly-generated workspace), and ~30 Governance fixtures.
- **Appending a content-revision segment is impossible.** `1.2.1.1` already uses **all four**
  numeric parts a NuGet version allows (`Major.Minor.Patch.Revision`). NuGet has no 5th numeric
  segment, and SemVer `+build.metadata` does **not** create a distinct package identity (NuGet
  ignores it for version precedence and de-duplication).

So the derivation itself has to be redesigned. This decision was escalated on #1228 for a human
call; the chosen direction is recorded here.

## Decision

**The ReferenceGateSet version is a plain SemVer that bumps on _any_ change to the packed set —
schema or content. The version no longer encodes the contained schemaVersions.**

- **Bump class by impact**, by SemVer's ordinary meaning for a data package: a **content-only**
  change to the packed `.fsgg` set is at least a **PATCH**; a change that a consumer's drift gate
  must be re-pinned to absorb (a schema-generation bump, or any change to the set's shape) is at
  least a **MINOR**; a change that breaks an existing consumer pin's assumptions is a **MAJOR**.
  Every change is representable, because every change moves the version.
- **The four `schemaVersion` values move _into_ the package** as a machine-readable manifest packed
  alongside the `.fsgg` set (`schema-manifest.json`: `{ "governance", "capabilities", "policy",
  "tooling" }`). The schema generations stay queryable — and *more* reliably than before, because a
  consumer reads a field instead of parsing a version string.
- **Consumers still pin exact** (Templates#14's overlay drift gate); the pin is now an ordinary
  SemVer rather than a schema tuple.
- **Implemented once** in `FS.GG.Governance/pack-reference-gate-set.fsx` — the SemVer derivation
  *and* the manifest emission — and exposed via the existing `--print-version` dry-run hook so the
  guard asserts the *emitted* version. The guard tests (`ReferenceGateSetPackageTests.fs`) assert
  byte-identity + content-only + the manifest's contents + the version, so the shipped artifact is
  provably the tested one.

## Consequences

- **governance** owns the new rule, its single implementation, and the guard. WI-8's gameplay-gate
  content finally ships under a new, honest version.
- The version-derivation rule **remains a versioned contract** (`governance-reference-gate-set`):
  this ADR supersedes ADR-0007, and the *registry* side — flipping the row's derivation-rule
  documentation and its `package-version` to the new SemVer — is the **publish-before-flip step 2**
  ([ADR-0037](0037-schema-growth-is-publish-before-flip.md)) that follows Governance's republish,
  not part of recording this decision. `docs/architecture.md`'s ADR-0007 reference and its
  `governance-reference-gate-set` version row reconcile in that same step (the house rule sequences
  the architecture map *after* the registry update).
- The scheme **loses the "version shows the schema generation at a glance" legibility** that
  ADR-0007 prized. The in-package manifest replaces it — machine-readable, and it can carry all four
  generations without a NuGet-segment ceiling, which the version string could not.

## Alternatives considered

1. **Three schema segments + a content-revision counter.** Collapse two rarely-moving schemas
   (e.g. `policy` + `tooling`) into one derived segment, freeing the 4th NuGet slot for a monotonic
   content-revision. **Rejected:** lossy — it revives the "3-segment packing that breaks once a
   schema exceeds 9" that ADR-0007 itself rejected — and it drops the "one schema bump ⇒ one
   segment" distinguishability that was the whole point of the original scheme. It pays real
   complexity to *half*-preserve a property it also *half*-breaks.
2. **Forbid content-only drift.** Keep the 4-segment rule unchanged and make "a valid content change
   with no schema bump" an illegal state the pack gate refuses. **Rejected:** this forces exactly the
   schema-churn the problem calls dishonest-and-fleet-breaking — WI-8's content would have to fake a
   `capabilities` 2→3 bump, which the exact-match loader would then use to reject every v2
   `capabilities.yml` in the fleet.
3. **SemVer with the tuple in `+build.metadata`, or a content-hash suffix.** Already rejected by
   ADR-0007 (build metadata is ignored by NuGet identity; a hash is illegible and not
   schema-derived). The in-package manifest reaches the "schema generations recorded" goal without
   loading anything onto the version identity.
