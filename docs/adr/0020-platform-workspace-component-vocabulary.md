# ADR-0020: Name the two products precisely — **platform**, **component**, **workspace**

- **Status:** Accepted
- **Date:** 2026-07-04
- **Affects:** all repos (front-door docs) — .github (this ADR + org landing + architecture + consumer guide), Rendering, SDD, Governance, Templates (READMEs + narrative docs)

## Context

FS-GG ships **two distinct things**, and our docs called both of them "product":

1. **The thing we build and maintain** — the five repositories in the org, the
   framework and machinery.
2. **The thing a consumer generates and ships** — the scaffolded repo carrying a
   runnable app, the `.fsgg/` lifecycle, skills, and optional governance.

A quick scan of the front-door docs shows "product" used for the first sense
("the four-**product** split", "each **product** repository", "depends on no other
FS-GG **product**"), the second sense ("scaffold a full-stack **product**",
"one runnable **product**", "your first **product**"), **and** a third, generic
sense (the end-user's application: "F# tooling for building desktop UI
**products**"). One word, three referents — the reader cannot tell which repo or
artifact a sentence means. This is a documentation-clarity defect, not a code one,
but in a project whose whole thesis is *making "who/what" an explicit, honest
choice*, an ambiguous top-level noun is a real cost.

## Decision

Adopt a four-term vocabulary and use it consistently in all narrative
documentation. The generator/generated split is named so that one term visibly
**produces** the other.

| Term | Means |
|---|---|
| **platform** | FS-GG as a whole: the five repositories in the org — the framework we build, maintain, and publish. |
| **component** | One repository *within* the platform (Rendering, SDD, Governance, Templates; `.github` is the coordination component). Replaces "product" as a building block. |
| **workspace** | What a consumer **scaffolds** with the platform: the generated repo with a runnable app, the `.fsgg/` lifecycle skeleton, skills, and optional governance. The consumer's deliverable. Replaces "product" as the scaffolded output. |
| **app** | The runnable Skia/Elmish application that lives *inside* a workspace. A workspace **contains** an app. Unchanged. |

**Mnemonic (canonical gloss):** *The platform is what we maintain; a workspace is
what you build with it. Each repository in the platform is a component.*

### Rewrite rules

- "product" = one of the repos → **component** ("four-component split", "component
  repository", "cross-component processes").
- "product" = the scaffolded output → **workspace** ("scaffold a workspace", "a
  full-stack workspace", "your workspace").
- "product" = the end-user's application, generically → **app** / **application**.

### Scope and non-goals

This standardizes **prose in front-door / narrative docs** (org landing,
architecture guide, consumer guide, each component's README and primary
usage/quickstart/tutorial docs). It deliberately does **not** touch:

- **Code, package IDs, CLI names, parameters, paths, filenames** — `productName` /
  `--param productName=`, `FS.GG.*`, `Product.slnx`, `ProductGraph`, the
  `docs/product/` directory. These are contracts and identifiers, not prose, and
  renaming them would be a breaking change for zero clarity gain.
- **Immutable records** — prior ADRs and `docs/decisions/**`. They stand as written.
- **`specs/**`, `.specify/**`, vendored mirrors, test corpora/fixtures** — engineering
  history where "product" is generic.

## Consequences

- Every component README carries a short **"Platform vs. workspace"** banner near
  the top, linking here, so the distinction is visible before any mistake can be made.
- The org landing page and architecture guide define the terms once, authoritatively.
- New docs inherit the vocabulary; a term used off-spec is a reviewable nit.
- Like the architecture map, this record is a light process obligation: a future
  rename of any of these four terms updates **this ADR first**, then the banners.
