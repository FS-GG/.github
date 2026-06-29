# FS-GG

F# UI tooling, split into focused products that each stand on their own.

The project began as one self-hosting platform (the archived
[`FS-Skia-UI`](https://github.com/EHotwagner/FS-Skia-UI)), which bundled a UI
runtime together with an experimental governance system. That got too heavy to
develop on. **FS-GG** is the split: the rendering product, the governance
tooling, the spec-driven development lifecycle, and the downstream composition
each live in their own repository, each using standard
[Spec Kit](https://github.com/github/spec-kit), and each shippable on its own.

## Repositories

| Repo | What it is | Ships | Status |
|---|---|---|---|
| [**FS.GG.Rendering**](https://github.com/FS-GG/FS.GG.Rendering) | The UI framework — Elmish/MVU apps rendered with SkiaSharp over OpenGL. Scene, layout, input, viewer/host, controls, design-system/themes. | `FS.GG.UI.*` packages + the `fs-gg-ui` `dotnet new` template (`net10.0`) | Active preview |
| [**FS.GG.Governance**](https://github.com/FS-GG/FS.GG.Governance) | Optional rule / evidence / route tooling, developed as a normal tool product. A pure inference kernel over typed facts and rules — light and advisory by default. | the `fsgg-governance` CLI, ~70 `FS.GG.Governance.*` packages, and the reference gate set | Active |
| [**FS.GG.SDD**](https://github.com/FS-GG/FS.GG.SDD) | Spec-driven development lifecycle tooling: `init`/`scaffold → charter → specify → clarify → checklist → plan → tasks → analyze → evidence → verify → ship`, a normalized work model, generated views, and agent guidance. Also the org's typed contract backbone. | `FS.GG.SDD.Cli` (`fsgg-sdd`) + the `FS.GG.Contracts` schema-authority package | Active |
| [**FS.GG.Templates**](https://github.com/FS-GG/FS.GG.Templates) | Downstream composition: wires SDD + Rendering + Governance into a ready-to-run product. Depends on the others; none depend back on it. | the `fs-gg-governance` config overlay template + the rendering scaffold provider | Active |

## How they compose

Composition happens **at scaffold time**, not by vendoring
([ADR-0002](https://github.com/FS-GG/.github/blob/main/docs/adr/0002-composition-by-scaffold-lifecycle-parameter-governance-populated.md)):
`fsgg-sdd scaffold` installs and drives the live, version-pinned upstream
rendering template, and the Governance overlay drops the reference gate set into
the project. There is no single "full-stack" template, because `dotnet new`
cannot depend on another template — a one-shot template could only exist by
vendoring a rendering copy that goes stale, exactly the failure mode that broke
the old monolith.

```text
FS.GG.Templates ──compose (scaffold-time)──▶ FS.GG.SDD · FS.GG.Rendering · FS.GG.Governance
FS.GG.SDD ──── governance-handoff@1 (optional) ────▶ FS.GG.Governance
FS.GG.SDD ──── owns FS.GG.Contracts ───────────────▶ consumed by Governance + the coherence gate
FS.GG.Rendering ── depends on no FS-GG product (never on Governance)
```

## Operating rule

> Governance tooling may *inspect* rendering or SDD artifacts; rendering and SDD
> must never *require* governance tooling for ordinary local build, test,
> document, package, or release work.

A contributor should be able to clone a product repo, read its Spec Kit
artifacts, run the documented commands, and ship a change without learning a
custom platform. The dependency direction is one-way and the inner loop is never
blocked by governance — that escape valve is what keeps the split honest.

## Cross-repo coordination

The four products evolve independently but share versioned contracts. The
coordination machinery lives in this repository
([`docs/coordination/`](https://github.com/FS-GG/.github/tree/main/docs/coordination)):

- **Requests are GitHub issues.** A cross-repo request is an issue opened in the
  target repo with the `cross-repo` label (template:
  [`cross-repo-request`](https://github.com/FS-GG/.github/blob/main/.github/ISSUE_TEMPLATE/cross-repo-request.yml));
  the org-level **Coordination** Projects-v2 board aggregates them, anchored by
  the *Homogeneous build · contracts · auto-update fabric* epic.
- **Durable facts live in the registry, not issues.** Who depends on whom and
  which contract versions are coherent is the machine-readable source of truth in
  [`registry/dependencies.yml`](https://github.com/FS-GG/.github/blob/main/registry/dependencies.yml)
  (`fsgg-contracts`, `governance-handoff`, `governance-{policy,capabilities,tooling,descriptor}`,
  `governance-reference-gate-set`, `fs-gg-ui-template`, `scaffold-provider`,
  `shared-build-config`, …). Larger decisions are recorded as
  [ADRs](https://github.com/FS-GG/.github/tree/main/docs/adr).
- **A CI coherence gate enforces it.** The reusable `contract-coherence`
  workflow turns a repo's CI red when reality stops matching the registry — it
  validates the registry with the typed `fsgg-sdd registry validate`, asserts the
  declared `FS.GG.Contracts` pin equals the published package, and checks the
  shared build config for drift.
- **Shared, drift-checked .NET build config.** `Directory.Build.props` /
  `Directory.Packages.props` are distributed from `dist/dotnet/` with a unified
  locked-restore gate ([ADR-0006](https://github.com/FS-GG/.github/blob/main/docs/adr/0006-org-shared-dotnet-build-config-and-unified-restore-locked-mode-gate.md)),
  and an API-compat breaking-change gate keeps published-package version numbers
  honest. An auto-update fabric (cross-repo dispatch + an org Renovate preset)
  keeps pins fresh so the gate rarely goes red.

## Cross-repo docs

The split decision and the staged implementation plans live in
[`docs/`](../../tree/main/docs) — see [`index.md`](../../blob/main/docs/index.md)
for the map. These supersede the earlier monolithic plan; the archived
`FS-Skia-UI` repo remains as source inventory and provenance only.
