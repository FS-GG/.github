# ADR-0015: Register the registry schema as a governed contract

- **Status:** Accepted — **§3 (the "same change" procedure) is superseded by
  [ADR-0037](0037-schema-growth-is-publish-before-flip.md)**; §1 (`registry-schema` as a governed
  contract, SDD-owned typed authority) and §2 (`.github` as a first-class registry node) stand.
- **Date:** 2026-07-02
- **Affects:** .github (registry + contract-coherence gate), SDD (Fsgg.Registry authority)
- **Amended by:** [ADR-0037](0037-schema-growth-is-publish-before-flip.md) — §3's atomic
  "same change" **cannot exist**: the schema document is in `.github`, its validator is in FS.GG.SDD,
  and no PR spans two repos. Schema growth is **publish-before-flip**, in two ordered PRs. See §3.

## Context

The cross-repo registry (`registry/dependencies.yml`) is validated in CI by the **typed**
`Fsgg.Registry` validator — `fsgg-sdd registry validate`, shipped in `FS.GG.Contracts` via the
`FS.GG.SDD.Cli` tool ([ADR-0001], [registry-validator-typed], .github#49). That was a deliberate
investment: a real `RegistryDocument` model + `validateDocument`, not a "the YAML parses" check.

The [2026-07-02 code-quality & architecture review][review] found that investment quietly
eroding (§6.9, combining findings H2 and M5):

- **H2 (.github#127):** the gate's `FS.GG.SDD.Cli` pin sat frozen at `0.2.1` for three CLI minors
  while the org shipped `0.5.0`. A frozen validator can only enforce the schema it knew about.
- **M5:** over the same window the registry **grew one-off fields** (`minimum-fsgg-sdd`,
  `package-tag`, `profiles`, the skill-manifest-era fields). Because the typed validator treats
  most fields as optional (additive tolerance), a stale validator silently degrades toward a
  YAML-parses check — exactly the outcome the typed validator was built to avoid.

The two findings compound: **the schema can grow and the validator can freeze, independently and
invisibly.** Every other cross-repo surface in the system is a versioned, owned entry in the
registry; the registry's *own* schema was the one contract that was not.

## Decision

**Register the registry schema as a first-class governed contract.**

1. **`registry-schema` contract entry** (owner `sdd` — `Fsgg.Registry` in `FS.GG.Contracts` is the
   typed authority; consumer `github` — the contract-coherence gate). Its `version` tracks the
   on-disk **`schemaVersion`** scalar (currently `1`). A new `validator` field names the gate pin
   (`FS.GG.SDD.Cli` in `contract-coherence.yml`) that validates this document, making the
   schema↔validator coupling explicit in the registry itself.

2. **`.github` becomes a first-class registry node.** It was already a contract *owner*
   (`shared-build-config`); it is now also a *consumer* and the `from` of a `github → sdd`
   dependency edge (`via: registry-schema@1`). `github` is added to the `repos:` map so the typed
   validator resolves those references (it cross-checks `consumers` and edge endpoints — verified:
   without the map entry, `UnknownComponent` reds the gate).

3. **Schema growth is a registered contract-change.** Teaching `Fsgg.Registry` a new field or a
   tightened rule now obliges, in the **same** change:
   - bump `registry-schema.version` **and** the file's top-level `schemaVersion`;
   - advance the `contract-coherence.yml` `FS.GG.SDD.Cli` pin to a CLI carrying that
     `Fsgg.Registry` version;
   - keep the field-vocabulary comment (the human schema-of-record) current.

   The pin↔feed coupling already in place ([the org Renovate annotation manager][127], `# renovate:
   datasource=nuget depName=FS.GG.SDD.Cli`) keeps the validator current *structurally* between
   deliberate schema bumps, so the H2 freeze cannot silently recur.

   > **Amendment (2026-07-14, [ADR-0037](0037-schema-growth-is-publish-before-flip.md),
   > [#689](https://github.com/FS-GG/.github/issues/689)). Do not follow the procedure above — the
   > "same change" it commands CANNOT EXIST.** For *both* contracts this ADR governs, the schema
   > **document** and its **validator** live in different repos: `registry/dependencies.yml` and
   > `registry/skills.yml` are here in `.github`; `Fsgg.Registry` is in **FS.GG.SDD**. No PR spans two
   > repos — and the second bullet is unsatisfiable even on its own terms, because the pin cannot
   > advance to a CLI that **does not yet exist**. (This ADR was not blind to it, it was *inconsistent*
   > about it: the Consequences below already say "SDD's own release cadence already publishes the CLI
   > the gate then pins", which is publish-then-pin in as many words. §3 nevertheless worded the
   > obligation as atomic, and the procedure is the half people follow.)
   >
   > **The procedure is publish-before-flip — two ordered PRs, never one:**
   >
   > 1. **FS.GG.SDD** teaches `Fsgg.Registry` the new field or rule and **publishes** an
   >    `FS.GG.SDD.Cli` carrying it. That validator **must still accept the document as it stands at
   >    `.github` HEAD** — the new rule may reject nothing the current document does.
   > 2. **`.github`** *then* bumps the document's `schemaVersion` **and** the contract `version`,
   >    advances the `contract-coherence.yml` `FS.GG.SDD.Cli` pin to that published CLI, and keeps the
   >    field-vocabulary comment current — one `.github` PR, after the CLI is live.
   >
   > The invariant §3 exists to protect — *the schema and its validator must not drift apart* — is
   > preserved exactly. What is dropped is an **atomicity requirement the repo topology makes
   > impossible**, and which was never the thing being protected. The pin↔feed coupling below is why
   > step 1 must be safe alone: `pin-coherence.yml` hard-fails on a pin behind feed-newest, so the pin
   > advances **ahead** of the schema bump, on Renovate's own PR — pointing the new validator at the
   > un-bumped document, on `main`, on a required gate. §1 and §2 stand.

This registration is **governance, not a behavioural gate change**: `schemaVersion` stays `1`, and
`fsgg-sdd registry validate` stays valid / 0 diagnostics over the current file. What changes is that
the *next* schema growth is a tracked, owned event instead of silent additive drift.

## Consequences

- **`.github`** owns the enforcement point: the `registry-schema` + `registry-schema-governed`
  entries, the `github → sdd` edge, and the obligation to advance the gate pin when the schema
  grows. The field-vocabulary comment documents the `validator` field and the "schema growth is a
  contract-change" protocol.
- **SDD** owns the surface: a `Fsgg.Registry` model/validator change is the trigger for a
  `registry-schema.version` + `schemaVersion` bump. SDD's own release cadence already publishes the
  CLI the gate then pins.
- **Tightening path stays open.** Registering the schema does not itself tighten validation (unknown
  fields are still tolerated). It gives the future tightening — making a today-optional field
  required, rejecting unknown fields — a *version* and an *owner*, so it can land as a normal
  `contract-change` (bump `schemaVersion` 1→2, advance the pin) rather than a silent breaking edit.
- **No repo re-pin required.** Consumers already run the reusable `contract-coherence.yml`; the
  registered contract adds no new gate step and no new caller obligation beyond the `packages: read`
  grant that [registry-validator-typed] already established.

<!-- This decision changes the §5 contract picture (a new contract + a new dependency edge + a new
registry node), so docs/architecture.md is reconciled as part of this resolution — after the
registry update. See docs/coordination/README.md#system-overview--the-architecture-map. -->

[ADR-0001]: 0001-cross-repo-coordination-via-issues.md
[registry-validator-typed]: ../registry/compatibility.md#coherence-state
[review]: ../reports/2026-07-02-code-quality-architecture-review.md
[127]: https://github.com/FS-GG/.github/issues/127
