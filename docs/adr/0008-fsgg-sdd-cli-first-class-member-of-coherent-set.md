# ADR-0008: The `fsgg-sdd` CLI is a first-class member of the coherent set (orchestrator axis)

- **Status:** Accepted
- **Date:** 2026-07-01
- **Affects:** .github (registry/ADR owner), FS.GG.SDD (CLI producer), FS.GG.Templates (provider descriptor)

## Context

The "coherent set" pins **template ↔ framework**: `fs-gg-ui-template@<V>` snapshots the
FS.GG.Rendering framework at a tag, and the registry
([`registry/dependencies.yml`](../../registry/dependencies.yml)) advertises that pin so a
consumer can install a version-coherent product. [ADR-0001](0001-cross-repo-coordination-via-issues.md)
established this registry-backed coherence model; [ADR-0002](0002-composition-by-scaffold-lifecycle-parameter-governance-populated.md)
made the product **composed at scaffold time** rather than vendored.

But a scaffolded product has **two** inputs, and the pin only governs one of them:

```
scaffold output = template@<pin>            (pin-controlled)
                + fsgg-sdd CLI@<installed>  (UN-pinned)
```

CLI-sourced artifacts escape the coherence guarantee entirely:

- the seeded `fs-gg-sdd-*` process skills (`FS.GG.SDD.Cli` `Foundation.fs:initEffects`, Feature 051)
- `.fsgg/early-stage-guidance.md`

So a product on the **newest pin** built with an **old CLI** silently lacks them, and nothing
detects it. Concretely (evidence from FS-GG/.github#85): `Breakout1/Breakout` is pinned
`fs-gg-ui-template::0.1.57-preview.1` — the newest coherent set, carrying `fs-gg-styling` from
Feature 226 — yet ships **zero** `fs-gg-sdd-*` skills and **no** `.fsgg/early-stage-guidance.md`,
because it was scaffolded by a pre-Feature-051 CLI. `scaffold-provenance.json` records the
generator (`FS.GG.SDD.Artifacts 0.2.1`) but nothing compares CLI version against the pin, so
the staleness is invisible — reading the newest pin actively *masks* the old-CLI gap. The pin's
coherence guarantee has a hole: it does not cover the orchestrator.

## Decision

Treat the **`fsgg-sdd` CLI as a first-class member of the coherent set**, extending ADR-0001's
coherence model to a third, *orchestrator* axis alongside template and framework:

1. **The coherent set gains a CLI dimension.** A `fs-gg-ui-template@<V>` coherent set carries a
   **minimum coherent `fsgg-sdd` version** — the oldest CLI that seeds the artifacts that pin's
   generated product is expected to contain.

2. **The registry is the source of that minimum** (as it is for every other pinned surface).
   The `fs-gg-ui-template` coherence entry records the minimum `fsgg-sdd` version, validated by
   `fsgg-sdd registry validate` and gated by `contract-coherence`.

3. **The staleness must be detectable at scaffold time.** `fsgg-sdd` records the **CLI version
   used** into `scaffold-provenance.json` and **warns (or fails) when the installed CLI is behind**
   the pin's required minimum, with a documented re-seed path (`refresh-agents`) for existing
   scaffolds.

4. **The provider descriptor carries the minimum** so the composition path is self-describing:
   `FS.GG.Templates` `providers/rendering.providers.yml` records the minimum coherent `fsgg-sdd`
   version alongside the `source:` pin.

The mechanism in each downstream repo is implemented under the sibling sub-issues of
FS-GG/.github#85 — this ADR records only the *decision*; the *contract* lands in the registry
(#87) and the *behaviour* in FS.GG.SDD and FS.GG.Templates.

## Consequences

- **.github** (this repo): the `fs-gg-ui-template` registry entry gains a minimum-`fsgg-sdd`
  field, and `docs/registry/compatibility.md` projects it (tracked as FS-GG/.github#87). The
  `contract-coherence` gate's typed validator (`fsgg-sdd registry validate`) must accept the new
  field.
- **FS.GG.SDD**: `fsgg-sdd` stamps its own version into `scaffold-provenance.json` and compares
  it to the pin's required minimum, warning (or failing) on a behind-CLI scaffold; the re-seed
  path is documented.
- **FS.GG.Templates**: `providers/rendering.providers.yml` records the minimum coherent
  `fsgg-sdd` version next to the `source:` pin.
- Coherence stops silently excluding the orchestrator: a newest-pin product built with an old
  CLI is now a **detected** incoherence rather than an invisible gap. The cost is one more axis
  to keep current on every coherent-set release — the CLI minimum moves when a pin starts
  depending on newly-seeded CLI artifacts.
- The coherence model now has three axes — template, framework, orchestrator — all anchored in
  the same registry that [ADR-0001](0001-cross-repo-coordination-via-issues.md) made the single
  place to detect incoherence.
