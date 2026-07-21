---
title: The consumer README standard
category: FS.GG
categoryindex: 6
index: 90
description: The checked-in standard for an FS-GG component README as a product page — the required section shape, a fill-in-the-blanks skeleton, the canonical acquisition snippet, and the compliance checklist. Scaffold a compliant README from this page without re-deriving the shape.
---

# The consumer README standard

This is the **authoring standard** every FS-GG component README converges on. Its
audience is whoever brings a repo's README up to standard (an agent or a human),
not the end consumer. It exists so that a worker can **scaffold a compliant
consumer README from this page alone, without re-deriving the shape** — copy the
[skeleton](#the-skeleton-copy-this), fill the placeholders, paste the
[acquisition snippet](#the-acquisition-snippet-paste-verbatim), then walk the
[compliance checklist](#compliance-checklist).

It is the shape the [consumer documentation roadmap][roadmap] (§5) defines and
that milestones M3–M5 implement. If you are correcting a stale count or a version
literal in an existing README, this is the target; if you are writing a new
component README, start here.

**The one rule behind every rule below:** *the README is the **product page**, not
the contributor guide.* Its first screen answers, in order — **what is this ·
what can it do · how do I get it · show me the smallest thing that works · where
do I go deeper.** Everything a *builder of the component* needs (build, test,
house style, CI, contributing) lives **below the fold** or in `CONTRIBUTING` /
`docs/`.

---

## The required shape

A compliant consumer README has these sections, **in this order**. Nothing above
the fold is optional except where marked.

| # | Section | It answers | Hard rules |
|---|---|---|---|
| 1 | **Title + one-liner** | *What is this, and who is it for?* | One sentence. **No component count, no repo count, no version** in the first sentence (or anywhere — see [Forbidden](#forbidden-hardcoded-counts-and-version-literals)). |
| 2 | **What it can do** | *What will this let me build?* | **3–7** concrete capability bullets a consumer recognizes. Capabilities, **not** internal module/project names. |
| 3 | **Acquire** | *How do I get it?* | The exact command(s) + package ID(s) + **"public on nuget.org — no credential."** Multi-package set: name the **entry package**, link the full map. Never link the credentialed org feed. Use the [snippet](#the-acquisition-snippet-paste-verbatim). |
| 4 | **Quick start** | *Show me the smallest thing that works.* | One **copy-pasteable** snippet that compiles/runs and produces a **visible result**. Smallest useful thing, not a tour. |
| 5 | **Go deeper** | *Where do I learn more?* | Link to this repo's `docs/` usage/getting-started guide **and** to related components. |
| 6 | **Where this sits** | *How does this fit the platform?* | **One line + links** to the [platform vocabulary (ADR-0020)][adr-0020] and [`docs/architecture.md`][arch]. **Linked, never restated** — no local component count, no local inventory table. |
| 7 | *(below the fold)* **Build / test / contributing / licensing** | *I want to hack on the component itself.* | Everything the *builder* needs. Anything not on the consumer's path lives here or in `CONTRIBUTING` / `docs/`. |

**Ordering is load-bearing.** Sections 1–4 are the product page and must be the
first screen. A README that is authoritative but internals-first (kernel theory,
project counts, exit-code families before the on-ramp) is **not** compliant even
if every fact in it is correct — put the consumer on-ramp first and push
internals below the fold.

---

## The acquisition snippet (paste verbatim)

Every consumable component pastes the matching block. Replace `<…>` placeholders;
**change nothing else** — the "public on nuget.org — no credential" wording and the
ADR-0039 reference are the point of a shared snippet.

The acquire **verb** is fixed by the artifact kind, so there is nothing to invent:

| Artifact kind | Acquire verb | Example placeholder |
|---|---|---|
| **Library** (a `PackageReference`) | `dotnet add package <id>` | `FS.GG.Game.Core` |
| **Global tool** (a CLI command) | `dotnet tool install --global <id>` | `FS.GG.SDD.Cli` (`fsgg-sdd`) |
| **`dotnet new` template pack** | `dotnet new install <id>` | `FS.GG.Templates` |

### Library set

````markdown
## Acquire

Every `FS.GG.*` package is **public on [nuget.org](https://www.nuget.org) and
restores with no credential** ([ADR-0039][adr-0039]). Add the entry package:

```sh
dotnet add package <entry-package-id>
```

The full package map is in [<link to this repo's package map / usage doc>].
````

### Global tool

````markdown
## Acquire

`<tool-package-id>` is a .NET global tool, **public on
[nuget.org](https://www.nuget.org) — no credential** ([ADR-0039][adr-0039]):

```sh
dotnet tool install --global <tool-package-id>
```

Then the `<command-name>` command is on your PATH.
````

> **Never** send a consumer to `https://nuget.pkg.github.com/FS-GG/…`. That feed is
> the org's **publish** path and needs auth; nuget.org is the **read** path
> (ADR-0039). Do not add `--add-source` and do not mention credentials.

**Package IDs are stable identity — spell them out. Versions are not — never pin
one in prose** (see [Forbidden](#forbidden-hardcoded-counts-and-version-literals)).
The authoritative package IDs per component are the [ground-truth acquisition
table][roadmap-gt] and the machine source `registry/dependencies.yml`.

---

## The skeleton (copy this)

Copy this whole block into the repo's `README.md` and replace every `<…>`
placeholder. The `<!-- … -->` comments are instructions — delete them as you go.
Do not reorder the sections.

````markdown
# <Component name>

<!-- One sentence: what it is + who it's for. No count, no version. -->
<One-line "what it is, and who it's for.">

## What it can do

<!-- 3–7 bullets. Consumer-recognizable capabilities, not internal module names. -->
- <capability 1>
- <capability 2>
- <capability 3>

## Acquire

<!-- Paste the matching block from "The acquisition snippet". -->
Every `FS.GG.*` package is **public on [nuget.org](https://www.nuget.org) and
restores with no credential** (ADR-0039).

```sh
<dotnet add package … | dotnet tool install --global … | dotnet new install …>
```

<!-- Multi-package set: name the entry package here and link the full map. -->

## Quick start

<!-- The smallest copy-pasteable thing that runs and shows a visible result. -->
```<lang>
<smallest runnable/usable snippet that produces a visible result>
```

<!-- One line stating what the consumer should now see. -->

## Go deeper

- <link to this repo's docs/ usage or getting-started guide>
- <link to related component(s)>

## Where this sits

<One line on where this component fits.> See the
[platform vocabulary (ADR-0020)](https://github.com/FS-GG/.github/blob/main/docs/adr/0020-platform-workspace-component-vocabulary.md)
and [`docs/architecture.md`](https://github.com/FS-GG/.github/blob/main/docs/architecture.md)
for how the whole platform fits together.
<!-- Link only. Do NOT restate a component count or an inventory table here. -->

---

<!-- Everything below the fold: for people hacking on the component itself. -->
## Building & contributing

<build/test/contributing/licensing — or link to CONTRIBUTING / docs/>
````

---

## Forbidden: hardcoded counts and version literals

Two classes of fact **must not be typed into a README as prose**, because they are
**derived** and rot the moment the platform changes. This is why the whole
consumer-doc surface fell out of date (roadmap §3a); a compliant README does not
reintroduce the rot.

- **Component / repository counts and inventories.** No "five repositories", no
  "four components", no "the N components" phrasing, no locally-maintained table
  of all components. State what *this* component is and does; for the whole-platform
  picture, **link** to [`docs/architecture.md`][arch] and the
  [platform vocabulary (ADR-0020)][adr-0020]. That canonical narrative carries the
  live inventory; every local copy drifts.
- **Version literals.** No `x.y.z` framework or package version pinned in prose.
  Name the package **ID** (stable) and let the consumer's `dotnet add package` /
  `dotnet tool install` resolve the current version from nuget.org. Where a
  version genuinely must appear, it is **rendered from `registry/dependencies.yml`
  at doc-build time**, not typed — that is what **milestone M6** delivers, and
  until it lands the safe move is to link the live source rather than type a
  literal.

**The test:** *if adding another component, or bumping a package, would require
editing this README by hand, the README is carrying a derived fact it should have
linked or generated instead.*

---

## Compliance checklist

Tick every box before merging a README as compliant:

- [ ] **Sections 1–6 present and in order**; build/test/contributing is below the fold.
- [ ] **First screen** answers what · what-it-does · acquire · quick start — no scrolling past internals to reach the on-ramp.
- [ ] **Title line** has no component count, no repo count, no version.
- [ ] **What it can do**: 3–7 consumer-recognizable capability bullets (not internal module names).
- [ ] **Acquire** shows the exact command + package ID(s) and says **"public on nuget.org — no credential"** (ADR-0039). Multi-package sets name the entry package and link the map.
- [ ] **No link to** `nuget.pkg.github.com/FS-GG`, no `--add-source`, no credential talk.
- [ ] **Quick start** is one copy-pasteable snippet that compiles/runs and produces a **visible result**.
- [ ] **Go deeper** links this repo's `docs/` guide and related components.
- [ ] **Where this sits** is one line + links to ADR-0020 and `docs/architecture.md` — **no restated count or inventory**.
- [ ] **No hardcoded count or version literal** anywhere (see [Forbidden](#forbidden-hardcoded-counts-and-version-literals)).

---

## Acceptance test

Two tests, and a compliant README passes **both**:

1. **The author's test (this standard's purpose):** a worker can scaffold a
   compliant consumer README from this page — [skeleton](#the-skeleton-copy-this)
   + [snippet](#the-acquisition-snippet-paste-verbatim) +
   [checklist](#compliance-checklist) — **without re-deriving the shape** and
   without opening another repo to learn what a README should contain.
2. **The consumer's test (roadmap §5):** *a consumer who has never seen FS-GG can,
   from this page alone, install the thing and run one working example.* If they
   have to leave the README to find the install command or a first snippet, it
   fails.

---

[roadmap]: https://github.com/FS-GG/.github/blob/main/docs/reports/2026-07-21-consumer-documentation-roadmap.md
[roadmap-gt]: https://github.com/FS-GG/.github/blob/main/docs/reports/2026-07-21-consumer-documentation-roadmap.md#2-ground-truth--what-a-consumer-can-actually-acquire-today
[arch]: https://github.com/FS-GG/.github/blob/main/docs/architecture.md
[adr-0020]: https://github.com/FS-GG/.github/blob/main/docs/adr/0020-platform-workspace-component-vocabulary.md
[adr-0039]: https://github.com/FS-GG/.github/blob/main/docs/adr/0039-nuget-org-is-the-read-path.md
