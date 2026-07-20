# ADR-0002: Products are composed by scaffold, `lifecycle` is a template parameter, governance is populated-by-default

- **Status:** Accepted
- **Date:** 2026-06-27
- **Affects:** FS.GG.Rendering, FS.GG.SDD, FS.GG.Governance, FS.GG.Templates, .github
- **Amended by:**
  - [ADR-0004](0004-constitution-ownership-for-lifecycle-sdd-products.md) §D4 — constitution ownership for `lifecycle=sdd` products: SDD owns it.
  - [ADR-0056](0056-sdd-is-the-default-lifecycle-spec-kit-is-legacy-and-scheduled-for-removal.md) §D2 — the `lifecycle` **default** flips `spec-kit → sdd` and `spec-kit` becomes legacy, frozen, and scheduled for removal. D1 (composition-by-scaffold) and D3 (governance populated-by-default) stand.

## Context

The original FS.GG.Templates shape vendored a full rendering app (`fs-gg-fullstack`) plus a
`scripts/sync-from-rendering.sh` mirror, and shipped an **empty** `fs-gg-governance` overlay.
This made Templates a *fork host*: it duplicated rendering source, drifted from upstream
(the `FsSkiaUiVersion` staleness that motivated [ADR-0001](0001-cross-repo-coordination-via-issues.md)),
and advertised governance it did not actually enforce.

The architecture analysis
([report](https://github.com/FS-GG/FS.GG.Templates/blob/main/docs/reports/2026-06-27-fsgg-packaging-composition-and-governance-architecture.md))
proposed a different decomposition: generated products are **composed at scaffold time** from
independently-versioned pieces rather than vendored, the Spec Kit lifecycle becomes an
**opt-in template parameter** instead of an always-on fork, and governance ships **real,
populated** gates so the advertised capability is the enforced one. The four decisions below
were reviewed and accepted; this ADR records them and gates the P0–P5 roadmap work tracked on
the org **Coordination** board (Projects v2 #1).

## Decision

1. **Composition, not vendoring.** Generated products are composed by `fsgg-sdd scaffold`
   invoking the rendering provider (`scaffold-provider`), not by vendoring a rendering
   monolith into Templates. Templates becomes *registry + populated overlay*, and the
   `fs-gg-fullstack` monolith plus `scripts/sync-from-rendering.sh` are retired.

2. **`lifecycle` is a template parameter.** `fs-gg-ui` gains a `lifecycle` choice symbol
   (`spec-kit` | `sdd` | `none`). The `spec-kit` default is **byte-identical** to today's
   output (existing profile tests unchanged); `sdd` emits an app-only product plus the SDD
   skeleton; `none` emits neither `.specify/` nor a constitution. The Spec Kit lifecycle is
   thus opt-in, not always-on.

3. **Governance populated-by-default.** Governance ships a **populated reference `.fsgg`
   gate set** (non-empty `checks:`/`commands:` — build/test + EvidenceGraph/EvidenceAudit),
   and Templates' `fs-gg-governance` overlay carries those real gates. Empty overlays that
   advertise un-enforced capability are not shipped. The handoff *consumer* (enforcing
   `governance-handoff.json`, not merely producing it) is tracked as the real enforcement gap
   under Governance ADR-0002.

4. **Constitution ownership is decided per the P0 gate.** For `lifecycle=sdd` products, which
   repo ships the F# lifecycle constitution (Rendering vs SDD) is resolved as a P0 decision
   item and reflected in P2 once settled.

   > **Amendment (2026-07-14). This gate is CLOSED — it is no longer an open P0.**
   > [ADR-0004](0004-constitution-ownership-for-lifecycle-sdd-products.md) took the decision:
   > **SDD owns the lifecycle constitution for `lifecycle=sdd` products, shipped at
   > `.fsgg/constitution.md`.** ADR-0004 says so in as many words — *"ADR-0002 Decision 4 is hereby
   > resolved"* — and marks the P0 board card `Done`.

5. **Versioning is hardened so the staleness bug class is structurally impossible.** Consumer
   products and composition tests commit `packages.lock.json` and restore with `--locked-mode`
   in CI, and promote `NU1603` to an error (the silent nearest-version float that broke
   `fs-gg-fullstack` becomes a hard failure). Rendering keeps the lockstep single-version
   `FS.GG.UI.*` set behind the coherent tag, optionally fronted by a `FS.GG.UI` BOM/metapackage;
   `sync-from-rendering.sh` is replaced by version bumps (Renovate grouped `FS.GG.UI.*`, or a
   `repository_dispatch` auto-PR on upstream release tags). Tracked as P5.

## Consequences

- **Templates** repoints `providers/rendering.providers.yml` at `FS.GG.UI.Template@<ver>` with
  `lifecycle=sdd`, populates the governance overlay, deletes `fs-gg-fullstack/` and
  `sync-from-rendering.sh`, and adds composition tests (pack → install → instantiate → build →
  verify pins/links). (P4)
- **Rendering** adds the `lifecycle` symbol + source conditions, moves git-init/chmod out of
  template post-actions, and publishes `FS.GG.UI.Template` carrying the parameter. (P1)
- **SDD** confirms `scaffold --provider rendering --param lifecycle=sdd` yields app-only +
  skeleton and records app-only provenance. (P2)
- **Governance** publishes the populated reference gate set and ships the handoff consumer. (P3)
- The **registry** is updated as part of this decision: the `templates → rendering` *vendors*
  edge is retired in favour of scaffold composition, `fs-gg-ui-template` is annotated with the
  `lifecycle` parameter, and the governance overlay's empty → populated move is tracked as a
  standing coherence item until P3/P4 land.
- Versioning is hardened so the `FsSkiaUiVersion` staleness bug class becomes structurally
  impossible (lockfiles + locked-mode CI, optional BOM, Renovate/`repository_dispatch`). (P5)
