# FS-GG

F# UI tooling, split into focused products that each stand on their own.

The project used to be one self-hosting platform (the archived
[`FS-Skia-UI`](https://github.com/EHotwagner/FS-Skia-UI)), which bundled a UI
runtime together with an experimental governance system. That got too heavy to
develop on. **FS-GG** is the split: the rendering product, governance tooling,
and spec-driven development lifecycle tooling live in separate repositories,
each using standard [Spec Kit](https://github.com/github/spec-kit).

## Repositories

| Repo | What it is | Status |
|---|---|---|
| [**FS.GG.Rendering**](https://github.com/FS-GG/FS.GG.Rendering) | The UI framework — Elmish/MVU apps rendered with SkiaSharp over OpenGL (GL). Scene, layout, input, viewer/host, controls, design-system/themes, templates. | Active |
| [**FS.GG.Governance**](https://github.com/FS-GG/FS.GG.Governance) | Optional rule/evidence/route tooling, developed as a normal tool product. Rendering and SDD never depend on it for ordinary local work. | Active |
| [**FS.GG.SDD**](https://github.com/FS-GG/FS.GG.SDD) | Spec-driven development lifecycle tooling: charter, specify, clarify, checklist, plan, tasks, normalized work model, generated views, and agent guidance. | Scaffolded |

## Operating rule

Governance tooling may *inspect* rendering or SDD artifacts; rendering and SDD
must never *require* governance tooling for ordinary local build, test, document,
package, or release work. A contributor should be able to clone a product repo,
read its Spec Kit artifacts, run the documented commands, and ship a change
without learning a custom platform.

## Cross-repo docs

The split decision and the staged implementation plans live in
[`docs/`](../../tree/main/docs) of this repository — see
[`index.md`](../../blob/main/docs/index.md) for the map. These supersede the
earlier monolithic plan; the archived `FS-Skia-UI` repo remains as source
inventory and provenance only.
