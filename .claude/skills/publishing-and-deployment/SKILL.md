---
name: publishing-and-deployment
description: How FS-GG packages, tools, and templates get built, versioned, and published — the GitHub Packages NuGet feed, the -preview channel, `dotnet pack` + the local-feed fallback, version-derivation and coherent-set rules, the publish gates (ApiCompat, reference-gate-set guard), the producer-release → dispatch + Renovate auto-update fabric, and package-identity rules. Use when publishing/releasing an FS-GG artifact, wiring or debugging a release workflow, adding a new package to a feed, deciding a package version, or extending publishing to a new feed (e.g. nuget.org). Authoritative status lives in `registry/dependencies.yml` (projection: `docs/registry/compatibility.md`) — always confirm coherence flags there before acting.
---

# Publishing & deployment (FS-GG)

FS-GG ships **standard .NET artifacts** — nothing bespoke. The knowledge that's easy
to re-research is *which* feed, *which* channel, *how* a version is derived, *what
gates* a publish, and *how* a release propagates. That's what this skill holds.

> **Source of truth:** the registry (`registry/dependencies.yml`) and its human
> projection (`docs/registry/compatibility.md`) record the live coherence flags and
> published versions. Docs and ADRs can lag the registry — when they disagree, the
> registry wins. Confirm status there before you publish or claim something is done.

## What ships, and where from

| Product | Artifact(s) | Kind |
|---|---|---|
| **SDD** | `FS.GG.SDD.Cli` (`fsgg-sdd` global tool), `FS.GG.Contracts` | tool + contract package |
| **Rendering** | 17 `FS.GG.UI.*` packages + `FS.GG.UI.Template` (`fs-gg-ui` template) | package coherent set + template |
| **Governance** | `FS.GG.Governance.Cli` (`fsgg-governance` global tool), `FS.GG.Governance.ReferenceGateSet` | tool + content-only package |
| **Templates** | `FS.GG.Templates` (`dotnet new` template package) | template |

- **Feed:** the org **GitHub Packages** NuGet feed —
  `https://nuget.pkg.github.com/FS-GG/index.json`. **Not nuget.org.** Consumers who
  can't restore add it to their `NuGet.config` (or `--add-source` on a tool install).
- **Channel:** FS-GG packages ship on a **`-preview`** channel (e.g.
  `0.1.58-preview.1`). There is no stable line yet — tooling must track prereleases
  (`ignoreUnstable: false`, `respectLatest: false` in the Renovate preset).
- **Feed provisioning** (`.github#21`) is **done + verified**: ~19 `FS.GG.*` packages
  resolve on the feed, `read:packages` auth works. (Older text in `default.json` /
  ADR-0007 that calls the feed "dormant/deferred" predates provisioning — trust the
  registry.)

## How a package gets published

Each **product repo owns its release workflow** (not `.github`). The shape is:

1. **Tag** the coherent set (e.g. `fs-gg-ui-template/v0.1.58-preview.1`).
2. **`dotnet pack`** the packables → `.nupkg`.
3. **Push** to the org feed: `dotnet nuget push *.nupkg --source https://nuget.pkg.github.com/FS-GG/index.json --api-key <token>` (a GitHub token with `write:packages`).
4. **Announce** downstream via the dispatch-sender job (see *Auto-update fabric*).

Publish gates run **before** the push (see *Gates*). A release publishes the whole
**coherent set together** — the `FS.GG.UI.*` members, their BOM, and the template move
as one version; don't publish a subset.

### Local-feed fallback (the "done-definition" where feed push is deferred)

Some producers' done-definition is a consumable artifact via **`dotnet pack` to a
local feed**, not the org feed push:

```sh
dotnet pack -o ~/.local/share/nuget-local/
# consumer adds that dir as a NuGet source
```

ADR-0007 sets this for `FS.GG.Governance.ReferenceGateSet`. If you're wiring a new
package and the org-feed push is blocked, local-feed pack is the acceptance bar —
and the registry entry must record the deferred-feed status.

## Versioning rules

- **Coherent sets move as one.** The `FS.GG.UI.*` set shares one version; bump and
  publish them together (Renovate groups them into a single PR).
- **`-preview` always.** No stable pins — consumers pin the exact preview
  (`dotnet-tools.json` for tools, `Directory.Packages.props` for packages).
- **Schema-derived versions** (ADR-0007): `FS.GG.Governance.ReferenceGateSet`'s
  version is the 4 contained `schemaVersion`s composed in fixed file order —
  `{governance}.{capabilities}.{policy}.{tooling}` (= `1.2.1.1`). Deterministic (no
  clock/counter), distinguishable (one schema bump ⇒ one segment changes). The rule
  itself is a **versioned contract** — changing segment order/count/source is a
  `contract-change`. Consumers pin exact (`[1.2.1.1]`).
- **The CLI is part of the coherent set.** A scaffolded product has three pins —
  template, framework (`FS.GG.UI.*`), and `fsgg-sdd` itself (ADR-0008/0009). Bumping
  one may require the pinned-minimum-CLI (`minimum-fsgg-sdd.version`) to advance.

## Package identity — don't churn it

NuGet **package ID == product identity**. Never rename a package in place: a rename
is a *new* identity. Deprecate an old ID toward its replacement **only after** the
replacement is published and verified. Runtime IDs belong to Rendering; governance
IDs belong to Governance — never share identity across products by accident.

## Gates that guard a publish

| Gate | What it enforces | Where |
|---|---|---|
| **ApiCompat / Package Validation** (`apicompat-publicapi-gate`, ✅ live) | a removed/changed public member fails CI ⇒ forces a SemVer major. F# packages, so this is the SDK's language-agnostic ApiCompat — **not** the C#-only `PublicApiAnalyzers`. Runs at pack vs the published-feed baseline. | Rendering (17 `FS.GG.UI.*` vs `0.1.52-preview.1`), SDD (`FS.GG.Contracts` vs `1.0.1`) |
| **Reference-gate-set guard** (G1–G7) | `FS.GG.Governance.ReferenceGateSet` can't be produced unless the reference set is valid ⇒ shipped == tested; guard asserts byte-identity + content-only + derived version on the real `.nupkg`. | Governance CI (`reference-gate-set-pack`) |
| **Contract-coherence gate** | a version/API bump that breaks a registry range fails the consumer's CI. | consumer repos (see `docs/coordination/contract-coherence-gate.md`) |

These are **workflow-level** (most repos have no branch protection); to hard-block
the merge button, add the job to the repo's required checks.

## Auto-update fabric — how a release reaches consumers

Two complementary halves, both owned by `.github` (`docs/coordination/auto-update-fabric.md`):

- **Push (immediate, targeted):** the producer's release workflow calls the reusable
  [`dispatch-sender.yml`](../../.github/workflows/dispatch-sender.yml) (`workflow_call`).
  It mints a **GitHub App token scoped to the target repo** (a `GITHUB_TOKEN` can't
  dispatch cross-repo) and POSTs `repository_dispatch {event_type,
  client_payload:{version,source_repo,source_sha,…}}`. The consumer handles it with
  `on: repository_dispatch` and opens a bump PR. App id/key are org secrets
  (`FSGG_DISPATCH_APP_ID` / `FSGG_DISPATCH_APP_PRIVATE_KEY`).
- **Pull (drift-catching backstop):** the org-shared Renovate preset
  [`default.json`](../../default.json) — consumers add
  `"extends": ["github>FS-GG/.github"]`. Custom managers catch embedded pins the
  standard `nuget` manager misses (the `FsGgUiVersion` MSBuild property →
  `FS.GG.UI.Template`; annotation-driven `# renovate: datasource=nuget depName=…`).
  The feed `hostRules` token lives in each consumer's **own** `renovate.json`
  (Renovate won't substitute `{{ secrets }}` inside an `extends` preset).

Neither bypasses review — both open PRs, and the consumer's contract-coherence gate
still has to pass.

## The registry republish-train convention

When you publish a new coherent set, record it in `registry/dependencies.yml` as a
**republish-train** update (see recent commits like *"registry: republish train —
fsgg-contracts 1.2.0, fs-gg-ui-template 0.1.58, fsgg-sdd 0.3.0"*), and flip any
coherence flags the release satisfies. A `contract-change` issue MUST update the
registry as part of its resolution. Keep the projection (`docs/registry/compatibility.md`)
in sync.

## Per-product authoritative docs

- **SDD:** `FS.GG.SDD/docs/release/versioning-policy.md`, `.../compatibility-matrix.md`, `.../migrations/`
- **Rendering:** `FS.GG.Rendering/docs/usage.md#getting-the-packages`
- **Consumer-facing install/update:** `docs/consumer/versioning-and-updates.md`, `docs/consumer/getting-started.md`
- **Fabric & gates:** `docs/coordination/auto-update-fabric.md`, `docs/coordination/contract-coherence-gate.md`

## Public nuget.org (decided, wiring pending — ADR-0012 + ADR-0013)

Everything above targets the **org GitHub Packages** feed. **[ADR-0012](../../../docs/adr/0012-dual-publish-to-nuget-org.md)**
adds **dual-publish to public nuget.org** for **all** currently-published packages (both
`.Cli` tools, `FS.GG.Contracts`, the `FS.GG.UI.*` set + BOM + Template,
`FS.GG.Governance.ReferenceGateSet`) — **additive**: the org feed stays the coherence
source of truth (Renovate / contract-coherence gate / registry `package-version` keep
reading it), nuget.org is a public distribution target. Registry coherence id:
**`nuget-org-published`** (`coherent: false` until wired).

**Auth = Trusted Publishing (OIDC), not a stored key** — [ADR-0013](../../../docs/adr/0013-trusted-publishing-oidc-for-nuget-org.md)
supersedes ADR-0012 §6. There is **no `NUGET_ORG_API_KEY` secret**: each producer's release
job requests an OIDC token (`id-token: write`), `NuGet/login@v1` exchanges it at nuget.org
for a **single-use key valid ~1 hour**, and the push uses that. Login+push live in **each
producer's own `release.yml`** — a cross-repo reusable workflow trips the OIDC policy match
([NuGet/login#6](https://github.com/NuGet/login/issues/6)), so there is **no** shared
`.github` push workflow for this (the earlier `nuget-org-push.yml` from `.github#104` was
retired). Wire it inline, **after** the org-feed push + all gates (ApiCompat, G1–G7 guard)
are green (ADR-0012 §4 gated ordering):

```yaml
# in the producer's release.yml — the job that already holds the gated .nupkg set:
    permissions:
      id-token: write      # mint the GitHub OIDC token
      contents: read
    steps:
      # ... org-feed push + gates already green ...
      - name: NuGet login (OIDC → short-lived key)
        uses: NuGet/login@v1
        id: login
        with:
          user: ${{ secrets.NUGET_USER }}    # nuget.org PROFILE name (not email); non-sensitive
      - name: Push byte-identical set to nuget.org
        run: >
          dotnet nuget push "**/*.nupkg"
          --api-key ${{ steps.login.outputs.NUGET_API_KEY }}
          --source https://api.nuget.org/v3/index.json --skip-duplicate
```

Push the **byte-identical** gate-verified `.nupkg` (no re-pack — ADR-0012 §3). Each packable
also needs listing metadata (`PackageLicenseExpression`, `PackageReadmeFile`, `RepositoryUrl`,
icon — ADR-0012 §5).

**Blocked on an admin gate** — now **Trusted Publishing policies**, not a secret (ADR-0013 §4).
An org-admin signed in to nuget.org as the FS-GG-org owner creates **one policy per producer
repo** (Repository Owner `FS-GG`; Repository `FS.GG.SDD` / `FS.GG.Rendering` /
`FS.GG.Governance`; Workflow File = that repo's release workflow filename only, e.g.
`release.yml`), reserves the `FS.GG.` **ID prefix** (anti-squat — still required), and
optionally sets `NUGET_USER`. **Fail-closed is intrinsic:** until a matching policy exists,
`NuGet/login` returns `401` and the release fails loud — never a silent no-op, never a
half-published set. **Permanence:** a nuget.org ID is claimed forever (unlist ≠ delete), so
the current `FS.GG.*` IDs are frozen as the public identities (no rename — ADR-0003).
