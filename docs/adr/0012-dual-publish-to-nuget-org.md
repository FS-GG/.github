# ADR-0012: Dual-publish FS-GG packages to nuget.org (public) alongside the org GitHub Packages feed

- **Status:** Accepted — **§6 (push authentication / admin gate) superseded by [ADR-0013](0013-trusted-publishing-oidc-for-nuget-org.md)** (2026-07-01): the nuget.org push authenticates via **Trusted Publishing (OIDC)**, not a long-lived `NUGET_ORG_API_KEY` secret; login+push live in each producer's own workflow (no cross-repo reusable workflow). **§1's "the org feed stays the coherence source of truth" is amended by [ADR-0039](0039-nuget-org-is-the-read-path.md)** (2026-07-14): the READ path moved to nuget.org (#576) — Renovate resolves every `FS.GG.*` there, and five of six receivers restore from it. §2, §3 and §5 (scope, byte-identical, listing metadata) stand. **§4 (gated ordering) corrected 2026-08-12** (FS-GG/.github#2240): its unqualified "the push is safe to retry" was wrong — see §4's dated note.
- **Date:** 2026-07-01
- **Affects:** `.github` (registry, org provisioning, **and — since #624 — its own two producer workflows, which §2's scope does not yet name**), FS.GG.SDD, FS.GG.Rendering, FS.GG.Governance (producer release workflows)

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

   > **Amended by [ADR-0039](0039-nuget-org-is-the-read-path.md) (2026-07-14) — the READ path moved,
   > and this paragraph is now false of Renovate.** `default.json` routes **every** `FS.GG.*` lookup
   > to `api.nuget.org`, and `renovate.json` **removed** the GitHub Packages token outright (#576):
   > the org feed needs a credential even to *read*, and a 401 on a Renovate datasource is not an
   > error — it is an **empty version list**. So the bot enumerated no versions and opened no PR, and
   > the `FS.GG.SDD.Cli` pin froze four times (#127, #263, #566, and again at 0.10.0) while the config
   > "obviously" already had the token. Nor is nuget.org merely "additional" for consumers: only **1
   > of 6** receivers configures the org feed, so the other five restore every `FS.GG.*` package from
   > public nuget.org. **nuget.org is the road into the fleet.** ADR-0039 ratifies that split — read
   > path nuget.org, publish path the org feed — and §2–§5 below are untouched by it.

2. **Scope: everything currently published.** The two global tools (`FS.GG.SDD.Cli`,
   `FS.GG.Governance.Cli`), `FS.GG.Contracts`, the `FS.GG.UI.*` coherent set + the
   `FS.GG.UI` BOM + `FS.GG.UI.Template`, and the content-only
   `FS.GG.Governance.ReferenceGateSet`. The `-preview` channel is preserved (nuget.org
   serves prereleases).

3. **Same artifact, no re-pack.** The `.nupkg` pushed to nuget.org is **byte-identical**
   to the one pushed to the org feed — the same file that passed the publish gates
   (ApiCompat / Package Validation, the G1–G7 reference-gate-set guard). No separate
   build, so the gate-verified artifact is exactly what goes public.

4. **Gated ordering within a release, and the retry path is not "just retry."** A
   producer's release job pushes to the org feed first; **only after** that push and all
   gates are green does it push the identical `.nupkg` to
   `https://api.nuget.org/v3/index.json`. A failed nuget.org push **fails the release
   loud** and does not corrupt coherence within that run — the org feed remains
   authoritative for the bytes it already holds.

   > **Corrected 2026-08-12 (FS-GG/.github#2240) — "the push is safe to retry" was wrong.**
   > `dotnet pack` is **not reproducible**: it writes the OPC core-properties part as
   > `package/services/metadata/core-properties/<guid>.psmdcp` with a freshly generated
   > `<guid>` on every invocation, so two packs of an identical, clean checkout produce
   > `.nupkg` archives with different sha256 (`-p:ContinuousIntegrationBuild=true` does not
   > change this — measured on FS.GG.Templates 0.8.0, FS-GG/FS.GG.Templates#349).
   >
   > Whether a retry path is safe depends on **job topology**, and it is not uniform
   > across producers.
   >
   > For a producer whose release job **splits `pack` from `publish`** — a `pack` job
   > that uploads the `.nupkg` as a build artifact, and a separate `publish` job that
   > downloads it (the shape this issue's own measurement observed in the FS.GG.Templates
   > pipeline, run 30984547985, job `pack`) — GitHub Actions **"Re-run failed jobs"** is
   > safe: it skips the already-succeeded `pack` job and lets `publish` re-download the
   > *original* artifact. It is safe only **within the artifact's retention window**
   > (`retention-days` on the upload); past that window even this path re-packs.
   >
   > **`.github`'s own three dual-push producers do not have that shape.**
   > `release-kit.yml`, `release-drivers.yml` and `release-coord-engine.yml` each run
   > `pack` and both pushes as sequential **steps inside one `publish` job** — no
   > `needs:`, no `upload-artifact`/`download-artifact`. GitHub Actions reruns at **job**
   > granularity with no per-step memoization, so "Re-run failed jobs" on these three
   > re-executes `pack` exactly as "Re-run all jobs" does — there is no artifact to
   > redownload. **For these three, no safe retry path exists today**: a nuget.org push
   > that fails after the org-feed push already succeeded has no recovery that does not
   > risk the divergence below. Whether the remaining external producers share the
   > split-job or the single-job shape is not verified here.
   >
   > **"Re-run all jobs" and re-pushing/re-tagging the release are NOT safe.** Both
   > re-invoke `pack`, which mints a new `<guid>` and therefore new bytes. Because both the
   > org-feed and nuget.org pushes carry `--skip-duplicate`, a re-packed retry after a
   > partial success does not fail and does not no-op: the org feed silently **keeps** the
   > first archive (already pushed; `--skip-duplicate` no-ops on it) while nuget.org
   > **receives** the second, different one. The two feeds then permanently serve different
   > bytes for one immutable version — the exact corruption this section originally said
   > could not happen.
   >
   > **The correct fix is at the push, not at retry discipline.** A blind
   > `--skip-duplicate` treats "the feed already has this id+version" as always safe to
   > no-op past. It is only safe when the bytes match. The publish step must instead
   > compare the artifact it is about to push against what the target feed already serves
   > for that id+version and **fail closed** — non-zero exit, loud error — on any mismatch,
   > exactly as §6 already requires for a missing credential. A duplicate push is safe to
   > no-op **only when the bytes are identical**; everything else is a coherence break
   > masquerading as an idempotent retry.
   >
   > No producer implements this compare-and-fail-closed guard today — every dual-push
   > producer measured (`.github`'s own `release-kit.yml`, `release-drivers.yml`,
   > `release-coord-engine.yml`, and the six external producers named in
   > FS-GG/.github#2240) uses a blind `--skip-duplicate` on both legs. Implementing the
   > guard is a release-workflow behavior change, not a documentation correction, and is
   > tracked separately: FS-GG/.github#2428.

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

   > **Superseded by [ADR-0013](0013-trusted-publishing-oidc-for-nuget-org.md).** The push
   > authenticates via **Trusted Publishing (OIDC)** instead: no `NUGET_ORG_API_KEY` secret;
   > `NuGet/login@v1` mints a short-lived key per run; login+push live in **each producer's own
   > workflow** (a cross-repo reusable workflow fails the OIDC policy match — NuGet/login#6).
   > The admin gate becomes **one Trusted Publishing policy per producer repo** (+ the `FS.GG.`
   > prefix reservation, still required). Fail-closed is intrinsic: no policy → `NuGet/login`
   > 401 → the release fails loud.

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
- **Retry safety (corrected 2026-08-12, FS-GG/.github#2240):** §4's "safe to retry" claim
  was wrong and is corrected in place there. Implementing the compare-and-fail-closed
  push guard §4 now calls for, across `.github`'s own three dual-push producer workflows
  and the six external producers sharing the same pattern, is tracked in
  FS-GG/.github#2428.

<!-- Follow-up: reconcile docs/architecture.md (the feed/distribution picture) after the
registry `nuget-org-published` entry lands and the first package resolves on nuget.org. -->
