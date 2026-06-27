# ADR-0003: Rename the `fs-skia-ui` version-coherence machinery to `fs-gg-ui`

- **Status:** Proposed
- **Date:** 2026-06-27
- **Affects:** FS.GG.Rendering, FS.GG.Templates, FS.GG.SDD, .github

## Context

The framework packages were renamed from the old **FS.Skia.UI** identity to **FS.GG.UI** (see
`FS.GG.Rendering/docs/bridge/package-identity-migration.md`). The *packages* migrated cleanly to
`FS.GG.UI.*`, but the **version-coherence machinery that pins them kept the old name**:

- the coherent-snapshot **git tag namespace** `fs-skia-ui/v<version>` (feature 204; tags
  `fs-skia-ui/v0.1.50-preview.1`, `fs-skia-ui/v0.1.51-preview.1`),
- the `fs-gg-ui` template's CPM property **`FsSkiaUiVersion`** (`template/base/Directory.Packages.props`,
  `build.fsx`, READMEs — consumer-visible in every generated product), and
- the cross-repo registry contract ids **`fs-skia-ui-version`** and **`fs-skia-ui-bom`**
  (`registry/dependencies.yml` + `docs/registry/compatibility.md`).

So a consumer sees `FS.GG.UI.*` packages pinned by `fs-skia-ui`-named machinery — a stale,
confusing split. Surfaced while landing feature 207 (the BOM/metapackage), which extended the
`fs-skia-ui/*` tag namespace and added the `fs-skia-ui-bom` registry id rather than fork a new name
mid-snapshot.

## Decision

Rename the version-coherence machinery to the `fs-gg-ui` root so it matches the `FS.GG.UI` package
identity. **Clean break — no backward-compatibility aliases** (the org is pre-1.0; all consumers are
sibling repos coordinated through this board):

1. **Tag namespace** `fs-skia-ui/v<V>` → **`fs-gg-ui/v<V>`**. Re-tag the current coherent snapshot
   (`0.1.51-preview.1`) under the new namespace; the brand-new, unconsumed `fs-skia-ui/v0.1.51-preview.1`
   tag is dropped. Historical `fs-skia-ui/v0.1.50-preview.1` is re-tagged too (no aliasing — the old
   tag is removed once the registry/consumers reference the new one).
2. **Template CPM property** `FsSkiaUiVersion` → **`FsGgUiVersion`** across `template/base/**` and the
   generated tree, with a coherent template version bump (the property rename is a breaking change to
   every generated product; no dual-name support).
3. **Registry contract ids** `fs-skia-ui-version` → **`fs-gg-ui-version`** and `fs-skia-ui-bom` →
   **`fs-gg-ui-bom`** in `registry/dependencies.yml` + `docs/registry/compatibility.md`. No alias
   rows; the old ids are replaced, with this ADR linked from the renamed entries' provenance.

## Alternatives considered

- **Keep `fs-skia-ui` as an internal codename** — rejected: it is consumer-visible (the property and
  tags), so the split stays confusing.
- **Rename with deprecation aliases** (dual property names, alias tags/registry rows) — rejected by
  decision: no backward compatibility is required at this stage; aliases would add carrying cost for
  no consumer benefit.

## Consequences

- One-time breaking rename for generated products (`FsSkiaUiVersion` → `FsGgUiVersion`): consumers
  update one property name on next template adoption. Sequenced on the Coordination board (Phase
  **P5 Versioning**) as a `contract-change`; the registry is updated as part of resolution (ADR-0001).
- Naming is coherent end-to-end: `FS.GG.UI.*` packages, the `FS.GG.UI` BOM, the `fs-gg-ui/v<V>`
  snapshot tags, the `FsGgUiVersion` pin, and the `fs-gg-ui-version` / `fs-gg-ui-bom` registry
  contracts all share the `fs-gg-ui` root.
- Per-repo work (tracked as the issue's checklist): **Rendering** owns the property + tags + template;
  **.github** owns the registry + this ADR; **Templates / SDD** verify no vendored reference to the
  old property/tag remains in their scaffold paths.
