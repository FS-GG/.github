# ADR-0012: Dual-publish FS-GG packages to nuget.org (public) alongside the org GitHub Packages feed

- **Status:** Accepted
- **Date:** 2026-07-01
- **Affects:** `.github` (registry, org provisioning), FS.GG.SDD, FS.GG.Rendering, FS.GG.Governance (producer release workflows)

## Context

Every FS-GG artifact publishes today to the **org GitHub Packages** NuGet feed,
`https://nuget.pkg.github.com/FS-GG/index.json` (ADR-0007, coherence ids
`cross-repo-auto-update` / `fs-gg-ui-template`). The `.Cli` tools and the whole
`FS.GG.UI.*` set are already **public on that feed** (the free org has no `internal`
visibility, so they were flipped private→public — Rendering Feature 218).

"Public on GitHub Packages" is **not** the same as "on nuget.org", though:

- GitHub Packages is not where `dotnet tool install -g …`, `dotnet add package …`, or
  the nuget.org search index look by default — a consumer must add the feed to
  `NuGet.config` (`--add-source`) and, for some scopes, authenticate.
- It is not the discovery surface a public .NET consumer expects.

The goal is **frictionless public consumption** — `dotnet tool install -g FS.GG.SDD.Cli`
and `dotnet add package FS.GG.Contracts` with no `--add-source`, discoverable on
nuget.org — for **all** currently-published FS-GG packages.

This is a **one-way door**. A package ID pushed to nuget.org is **claimed
permanently**: a version can be unlisted but never truly deleted, and the ID is
reserved forever. Package ID == product identity (`docs/transition-and-boundaries.md`,
`docs/research-notes.md`; cf. ADR-0003 on rename discipline), so this decision also
freezes the current `FS.GG.*` IDs as the public identities. That is acceptable
because the IDs already exist unchanged on the org feed and this ADR does **not**
rename anything.

## Decision

1. **Additive dual-publish — do not move the primary.** The org GitHub Packages feed
   stays the **coherence source of truth**: Renovate (`default.json`), the
   contract-coherence gate, and the registry `package-version` fields continue to read
   from it. nuget.org is an **additional public distribution target**, not a
   replacement.

2. **Scope: everything currently published.** The two global tools (`FS.GG.SDD.Cli`,
   `FS.GG.Governance.Cli`), `FS.GG.Contracts`, the `FS.GG.UI.*` coherent set + the
   `FS.GG.UI` BOM + `FS.GG.UI.Template`, and the content-only
   `FS.GG.Governance.ReferenceGateSet`. The `-preview` channel is preserved (nuget.org
   serves prereleases).

3. **Same artifact, no re-pack.** The `.nupkg` pushed to nuget.org is **byte-identical**
   to the one pushed to the org feed — the same file that passed the publish gates
   (ApiCompat / Package Validation, the G1–G7 reference-gate-set guard). No separate
   build, so the gate-verified artifact is exactly what goes public.

4. **Gated ordering within a release.** A producer's release job pushes to the org feed
   first; **only after** that push and all gates are green does it push the identical
   `.nupkg` to `https://api.nuget.org/v3/index.json` with `--skip-duplicate` (idempotent
   retry). A failed nuget.org push **fails the release loud** but does not corrupt
   coherence — the org feed remains authoritative and the push is safe to retry.

5. **Package listing metadata.** nuget.org listing requires `PackageLicenseExpression`
   (or file), `PackageReadmeFile`, `RepositoryUrl`, and ideally an icon in each
   packable. Adding these is a one-time per-repo packaging change; it does not alter the
   assembly/content surface (no ApiCompat impact).

6. **Org provisioning is the admin gate** (same forward-guardrail model as `.github#21`
   for the GitHub feed). An org-admin: registers the reserved ID **prefix `FS.GG.`** on
   nuget.org (anti-squat), owns it under an FS-GG nuget.org organization, and stores the
   push key as the org secret **`NUGET_ORG_API_KEY`**. Until that secret exists, each
   producer's nuget.org push step **fails closed with a pointer to this ADR** — it never
   silently no-ops, and never half-publishes a subset of a coherent set.

## Consequences

- **`.github` (this repo):** records the decision (this ADR); adds coherence id
  **`nuget-org-published`** (`coherent: false` — a standing request) tracking the admin
  provisioning + the three producer wirings; updates the `publishing-and-deployment`
  skill and consumer docs (`docs/consumer/versioning-and-updates.md`,
  `getting-started.md`) to drop the `--add-source` requirement **once** a package
  resolves on nuget.org. **Admin task:** provision the nuget.org org, reserve the
  `FS.GG.` prefix, add the `NUGET_ORG_API_KEY` org secret.
- **FS.GG.SDD:** release workflow adds the nuget.org push for `FS.GG.SDD.Cli` +
  `FS.GG.Contracts`; add listing metadata to those packables.
- **FS.GG.Rendering:** release workflow adds the nuget.org push for the `FS.GG.UI.*`
  set + `FS.GG.UI` BOM + `FS.GG.UI.Template` (pushed as one coherent set); add metadata.
- **FS.GG.Governance:** release workflow adds the nuget.org push for
  `FS.GG.Governance.Cli` + `FS.GG.Governance.ReferenceGateSet`; add metadata. Confirm
  intended public exposure of the content-only gate-set package (decided: **yes**).
- **Consumers:** once packages land on nuget.org they resolve with no `--add-source`;
  the org feed stays the `-preview` coherence feed and the Renovate/auto-update fabric
  is unchanged (it keeps reading the org feed).
- **Ordering:** admin provisioning (prefix + `NUGET_ORG_API_KEY`) **first**; then each
  producer wires its push independently. `nuget-org-published` flips `coherent: true`
  when every in-scope package resolves on nuget.org at its current version.
- **Reversibility:** none for the IDs — this ADR is a `contract-change`-class commitment.
  Changing scope later (e.g. un-publishing a package) is an unlist, not a delete; a
  future rename is a new ID + deprecation (ADR-0003 discipline), never an in-place edit.

<!-- Follow-up: reconcile docs/architecture.md (the feed/distribution picture) after the
registry `nuget-org-published` entry lands and the first package resolves on nuget.org. -->
