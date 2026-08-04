---
name: publishing-and-deployment
description: Use when publishing an FS-GG package, tool, template, or stable channel release. Apply coherent-set versioning, run release gates, publish byte-identical artifacts to both feeds, and verify downstream updates.
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

<!-- BEGIN GENERATED: fsgg-release-inventory -->
*Generated from every registry package-bearing contract. Package, producer and coherent-set counts are derived here; release judgement remains below.*

Registry release inventory: 9 package-bearing contracts across 7 producers; 8 coherent release sets.

| owner | contract | source version | published version | coherent set | surface |
|---|---|---|---|---|---|
| `sdd` | `fsgg-contracts` | `7.5.2` | `7.5.2` | `sdd:7.5.2` | FS.GG.Contracts package — Fsgg.Schemas (typed .fsgg schema records + version constants for providers/project/sdd/agents/governance/policy/capabilities/tooling/scaffold-provenance/ governance-handoff) / Fsgg.Provider (extended ProviderDescriptor + optional Build/Test/Run/Verify commands + canonical NameParameter default "name") / Fsgg.Registry (dependencies.yml types + validator; 1.1.0 adds the typed registry validator over the real dependencies.yml, SDD#26 / feature 042 — additive; 2.1.0 adds the SKILL-REGISTRY surface — `SkillRegistryEntry`, `SkillRegistryDocument`, `MirrorDeclaration`, `validateSkillRegistry`, and the `MalformedField` case on the public DU `RegistryRule` — +78 lines of Registry.fsi, additive, FS.GG.SDD#426. That surface SHIPPED IN THE 2.0.1 PATCH and 2.1.0 is the corrective relabel; see the `version` comment above) / Fsgg.SkillMirror (1.4.0, ADR-0014 P1 / SDD#61: the pure BCL-only materialize-and-verify library — `mirror`/`verify` over the single `agentSkillRoots` constant, the one skill-fan-out code path for scaffold/refresh/doctor/upgrade) / Fsgg.ContractVersion (= 7.4.0). As of 7.4.0 the `.nupkg` also packs `api-surface/*.fsi` — six files, one per public module — so the signature surface described here is readable from the PACKAGE and not only from SDD's source tree (FS.GG.Rendering#782's producer half; FS.GG.Rendering#1101 pins that capability as a floor).  |
| `governance` | `governance-reference-gate-set` | `1.7.0` | `1.7.0` | `governance:1.7.0` | FS.GG.Governance.ReferenceGateSet content package — contentFiles/any/any/.fsgg/{governance,capabilities,policy,tooling}.yml, byte-identical to samples/sdd-reference-gate-set/.fsgg/; version-derivation rule per ADR-0055.  |
| `rendering` | `fs-gg-ui-template` | `0.26.0` | `0.26.0` | `rendering:0.26.0` | dotnet new fs-gg-ui (template/base) + FS.GG.UI.* framework packages |
| `game` | `game-sim-core` | `0.13.0` | `0.13.0` | `game:0.13.0` | FS.GG.Game.Core package — BCL-only deterministic simulation primitives plus the packaged fs-gg-game-core-fable-lockstep-v1 bounded Fable source/profile and canonical oracle |
| `game` | `game-scene-adapter` | `0.13.0` | `0.13.0` | `game:0.13.0` | FS.GG.Game.Render package — Adapter (sim-state -> FS.GG.UI.Scene drawables) |
| `audio` | `fs-gg-audio` | `0.5.0` | `0.5.0` | `audio:0.5.0` | FS.GG.Audio.Core/.Host/.Engine/.Elmish public .fsi surfaces — AudioEffect vocabulary + IAudioBackend/IMixingBackend seam + mixing Engine + Audio.Cmd Elmish bridge |
| `net` | `fs-gg-net` | `0.5.0` | `0.5.0` | `net:0.5.0` | FS.GG.Net.Core/.WebSocket/.WebSocket.Server/.Protobuf/.Grpc/.Elmish public .fsi surfaces — ITransport/IMessageChannel seam + Sequential/Multiplexed correlation + serve/ServerEcho + WebSocket client/server transport + Google.Protobuf/protobuf-net codecs + gRPC lifecycle bridge + Elmish Cmd/Sub |
| `github` | `coord-engine` | `0.20.1` | `0.20.1` | `github:0.20.1` | the `fsgg-coord-engine` CLI verb surface (claim/take/batch/who/widen/set-paths/say/landable/done/release/flush/…) + its exit-code contract, emitted from src/FS.GG.Coord.Core/Protocol.fs; shipped as the FS.GG.Coord.Cli dotnet tool |
| `github` | `new-sdd-workspace` | `0.9.0` | `0.9.0` | `github:0.9.0` | the `new-sdd-workspace` scaffolder CLI (package FS.GG.NewSddWorkspace) — one-command full-stack SDD workspace creation, wrapping the FS.GG.Templates `rendering` provider (ADR-0016); shipped as a dotnet tool |

<!-- END GENERATED: fsgg-release-inventory -->

- **Feeds — there are two, and they have different jobs (ADR-0012, ADR-0039).**
  - **Publish path:** the org **GitHub Packages** feed, `https://nuget.pkg.github.com/FS-GG/index.json`.
    Every release pushes here **first**.
  - **Read path:** **public nuget.org.** Every `FS.GG.*` package is public there (32 of 32), the
    byte-identical `.nupkg`. Renovate resolves *all* `FS.GG.*` from nuget.org, and **five of the six
    receiver repos restore from it** — the org feed needs a credential even to read, and they don't
    configure one. A package that never reaches nuget.org cannot be consumed by most of the fleet.

  (Older text — in `default.json`, ADR-0007, or an earlier version of this skill — that says
  "**Not nuget.org**" or calls the feed dormant/deferred **predates #576 and is wrong**. Trust the
  registry.)
- **Channel:** FS-GG packages ship on a **stable** channel. `FS.GG.Audio` `0.1.0`
  (2026-07-09, FS.GG.Audio#4) promoted the **last `-preview` producer**, so every
  `FS.GG.*` producer is now stable and the Renovate preset pins to stable
  (`ignoreUnstable: true`, `respectLatest: true`). The org stays on the **0.x** line;
  "stable" here means *no `-preview` suffix*, not 1.0.
- **Feed provisioning** (`.github#21`) is **done + verified**: registered package versions
  resolve on the feed, `read:packages` auth works. (Older text in `default.json` /
  ADR-0007 that calls the feed "dormant/deferred" predates provisioning — trust the
  registry.)

## How a package gets published

Each **producer owns its release workflow**, including `.github` for its two org-level tools
(ADR-0039 §5). The shape is:

1. **Tag** the coherent set with its stable version (for example `v0.1.0` or the producer's
   component-qualified stable tag).
2. **`dotnet pack`** the packables → `.nupkg`.
3. **Push the gate-verified bytes to both feeds**, in order: GitHub Packages first, then the
   byte-identical `.nupkg` to nuget.org through Trusted Publishing. Never re-pack between pushes.
4. **Announce** downstream via the dispatch-sender job (see *Auto-update fabric*).

Publish gates run **before** the push (see *Gates*). A release publishes the whole
**coherent set together** — the `FS.GG.UI.*` members, their BOM, and the template move
as one version; don't publish a subset.

### There is no local-feed fallback. Publish to the feed.

This section used to say that where the org-feed push was blocked, `dotnet pack` to
`~/.local/share/nuget-local/` was the acceptance bar for a new package, citing ADR-0007.

**That was true in June 2026 and is false now.** `.github#21` (the admin block) is closed.
`FS.GG.Governance.ReferenceGateSet` is live on the org feed at `1.2.1.1`, and
`scripts/check-feed-coherence.py` **enforces** every `package-version` in the registry against
the live feed — so a package that exists only in someone's `~/.local` cannot satisfy the gate,
and "packed locally" is not a done-definition any more.

A new package's acceptance bar is: **published to the org feed, and — per ADR-0012 — the
byte-identical `.nupkg` pushed to nuget.org.** See ADR-0039: five of the six receivers restore
`FS.GG.*` from public nuget.org, so a package that never reaches it cannot be consumed by most
of the fleet.

## Versioning rules

- **Coherent sets move as one.** The `FS.GG.UI.*` set shares one version; bump and
  publish them together (Renovate groups them into a single PR).
- **Stable channel.** Do not add a `-preview` suffix. Consumers pin the exact stable version
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
| **ApiCompat / Package Validation** (`apicompat-publicapi-gate`, ✅ live) | a removed/changed public member fails CI ⇒ forces a SemVer major. F# packages, so this is the SDK's language-agnostic ApiCompat — **not** the C#-only `PublicApiAnalyzers`. Runs at pack vs the published-feed baseline. | Rendering (`FS.GG.UI.*` coherent set), SDD (`FS.GG.Contracts`) |
| **Reference-gate-set guard** (G1–G7) | `FS.GG.Governance.ReferenceGateSet` can't be produced unless the reference set is valid ⇒ shipped == tested; guard asserts byte-identity + content-only + derived version on the real `.nupkg`. | Governance CI (`reference-gate-set-pack`) |
| **Contract-coherence gate** | a version/API bump that breaks a registry range fails the consumer's CI. | consumer repos (see `docs/coordination/contract-coherence-gate.md`) |

These are **workflow-level** (most repos have no branch protection); to hard-block
the merge button, add the job to the repo's required checks.

## Auto-update fabric — how a release reaches consumers

Two complementary halves, both owned by `.github` (`docs/coordination/auto-update-fabric.md`):

- **Push (immediate, targeted):** the producer's release workflow calls the reusable
  [`dispatch-sender.yml`](../../../.github/workflows/dispatch-sender.yml) (`workflow_call`).
  It mints a **GitHub App token scoped to the target repo** (a `GITHUB_TOKEN` can't
  dispatch cross-repo) and POSTs `repository_dispatch {event_type,
  client_payload:{version,source_repo,source_sha,…}}`. The consumer handles it with
  `on: repository_dispatch` and opens a bump PR. App id/key are org secrets
  (`FSGG_DISPATCH_APP_ID` / `FSGG_DISPATCH_APP_PRIVATE_KEY`).
- **Pull (drift-catching backstop):** the org-shared Renovate preset
  [`default.json`](../../../default.json) — consumers add
  `"extends": ["github>FS-GG/.github"]`. Custom managers catch embedded pins the
  standard `nuget` manager misses (the `FsGgUiVersion` MSBuild property →
  `FS.GG.UI.Template`; annotation-driven `# renovate: datasource=nuget depName=…`).
  The feed `hostRules` token lives in each consumer's **own** `renovate.json`
  (Renovate won't substitute `{{ secrets }}` inside an `extends` preset).

Neither bypasses review — both open PRs, and the consumer's contract-coherence gate
still has to pass.

## The registry republish-train convention

When you publish a new coherent set, update `registry/dependencies.yml` (contract
`version`/`package-version`/`package-tag`, consuming edges, coherence flags the release
satisfies, and the top-level `updated:` date) and **prepend one dated entry** to the
registry changelog `registry/CHANGELOG.md` — `- **YYYY-MM-DD** — HEADER (owner; refs):
body` (see recent entries like *"republish train — fsgg-contracts 1.2.0, fs-gg-ui-template
0.1.58, fsgg-sdd 0.3.0"*). One entry per change keeps PR diffs reviewable; the former
single-line `updated:` comment is retired (.github#129). A `contract-change` issue MUST
update the registry as part of its resolution. Keep the projection
(`docs/registry/compatibility.md`) in sync.

Then reconcile the **architecture map** `docs/architecture.md` — a republish touches
`registry/dependencies.yml`, which is exactly the `architecture-map.yml` reconcile
trigger. A routine version bump does not change the map's shape: take the opt-out, a
one-line `architecture-map: unaffected` in the PR body (or the
`architecture-map:unaffected` label). A set that moves a coherent-set axis or the map's
§5 contract picture updates the map instead.

**A green validator is not a green PR:** neither `fsgg-sdd registry validate` nor
`check-feed-coherence` sees `docs/registry/compatibility.md` or `docs/architecture.md` —
those are gated by `projection` and `architecture-map` respectively, and only in CI.

## Per-product authoritative docs

- **SDD:** `FS.GG.SDD/docs/release/versioning-policy.md`, `.../compatibility-matrix.md`, `.../migrations/`
- **Rendering:** `FS.GG.Rendering/docs/usage.md#getting-the-packages`
- **Consumer-facing install/update:** `docs/consumer/versioning-and-updates.md`, `docs/consumer/getting-started.md`
- **Fabric & gates:** `docs/coordination/auto-update-fabric.md`, `docs/coordination/contract-coherence-gate.md`

## Historical rollout record

The migration from org-feed-only publishing to stable, byte-identical dual publishing is complete.
Its superseded rollout states, administrative prerequisites, and OIDC decision history remain in
[ADR-0012](../../../docs/adr/0012-dual-publish-to-nuget-org.md) and
[ADR-0013](../../../docs/adr/0013-trusted-publishing-oidc-for-nuget-org.md). They are historical
records, not operating instructions. For current package versions and coherence, read
`registry/dependencies.yml`.
