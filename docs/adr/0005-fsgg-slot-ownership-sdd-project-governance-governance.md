# ADR-0005: `.fsgg/` slot ownership — SDD owns `project.yml`, Governance owns `governance.yml`

- **Status:** Accepted
- **Date:** 2026-06-28
- **Affects:** FS.GG.SDD, FS.GG.Governance, FS.GG.Templates, .github

## Context

The `.fsgg/` directory is a **shared namespace** that more than one FS-GG product writes
into. [ADR-0002](0002-composition-by-scaffold-lifecycle-parameter-governance-populated.md)
composes a product at scaffold time, and [ADR-0004](0004-constitution-ownership-for-lifecycle-sdd-products.md)
adds `.fsgg/constitution.md` to the SDD skeleton — so SDD and Governance artifacts coexist
in one `.fsgg/` of a generated product:

- **SDD** lays down the lifecycle skeleton: `.fsgg/project.yml`, `.fsgg/sdd.yml`,
  `.fsgg/agents.yml`, `.fsgg/constitution.md`, `.fsgg/providers.yml`,
  `.fsgg/scaffold-provenance.json`.
- **Governance** lays down its config/gate set: `.fsgg/policy.yml`, `.fsgg/capabilities.yml`,
  `.fsgg/tooling.yml`, plus a **project descriptor** (`ProjectFacts`) that its `Config.Loader`
  reads.

The collision: Governance's project descriptor was **also** named `.fsgg/project.yml`. Two
different repos claimed the same on-disk slot for two different schemas — exactly the
`.fsgg/project.yml` namespace collision called out as systemic problem (3) in the
homogeneous-build epic ([.github#16](https://github.com/FS-GG/.github/issues/16)). A
scaffolded `lifecycle=sdd` product with the governance overlay would have one repo's file
silently shadow the other's.

A related naming inconsistency sat next to it: the canonical scaffold product-name parameter
had drifted (`productName` in the Templates composition path vs `name` elsewhere), so the
`name`/`productName` collision (also problem (3)) is settled here too.

## Decision

1. **SDD owns `.fsgg/project.yml`.** It is the SDD lifecycle project descriptor, part of the
   SDD-owned skeleton. Governance does not write or read this slot.

2. **Governance owns `.fsgg/governance.yml`.** Governance's project descriptor (`ProjectFacts`,
   read by `Config.Loader`) is renamed `project.yml` → `governance.yml` — a **clean break**,
   no compatibility alias. Internal type names (e.g. `ProjectFacts`) are unaffected; only the
   on-disk filename/slot changes.

3. **The two coexist in a single `.fsgg/`.** A scaffolded product carries both `project.yml`
   (SDD) and `governance.yml` (Governance) with no shadowing. The full `.fsgg/` slot ownership
   map is:

   | Slot | Owner | Schema |
   |---|---|---|
   | `project.yml` | SDD | lifecycle project descriptor |
   | `sdd.yml`, `agents.yml`, `constitution.md` | SDD | lifecycle skeleton |
   | `providers.yml`, `scaffold-provenance.json` | SDD | scaffold contracts |
   | `governance.yml` | Governance | governance project descriptor (`ProjectFacts`) |
   | `policy.yml`, `capabilities.yml`, `tooling.yml` | Governance | gate set config |

4. **The canonical scaffold product-name parameter is `name`** (not `productName`). The
   `scaffold-provider` invocation passes `--param name=<product>`.

## Consequences

- **FS.GG.Governance** renames its slot file `project.yml` → `governance.yml` across
  `Config.Loader`, all fixtures, and `samples/sdd-reference-gate-set/.fsgg/`, with docs
  updated and build+test green. Tracked as
  [Governance#13](https://github.com/FS-GG/FS.GG.Governance/issues/13) (H0).
- **FS.GG.Templates** points its `fs-gg-governance` overlay at `governance.yml` and uses
  `--param name=`. Tracked as [Templates#12](https://github.com/FS-GG/FS.GG.Templates/issues/12) (H0).
- **FS.GG.SDD** is unchanged: `.fsgg/project.yml` stays SDD's; the skeleton needs no edit.
- **The registry** records the Governance descriptor surface as `.fsgg/governance.yml` and the
  canonical `name` param on `scaffold-provider`; `registry/dependencies.yml` +
  `docs/registry/compatibility.md` are updated and `updated:` bumped. Tracked as
  [.github#17](https://github.com/FS-GG/.github/issues/17) (H0).
- Slot ownership is now an **explicit contract**: a future product writing into `.fsgg/` checks
  this map before claiming a filename, so the collision class cannot silently recur.
