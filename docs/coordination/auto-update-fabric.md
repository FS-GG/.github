# Cross-repo auto-update fabric

How a new coherent version in one FS-GG repo reaches its downstream consumers **automatically**,
instead of via a hand-edited pin that silently goes stale. Two halves, both owned by
`FS-GG/.github` ([epic #16](https://github.com/FS-GG/.github/issues/16) Pillar 4,
[#22](https://github.com/FS-GG/.github/issues/22)):

| Half | Artifact | Direction |
|---|---|---|
| **Push** — producer announces a release | [`.github/workflows/dispatch-sender.yml`](../../.github/workflows/dispatch-sender.yml) (reusable `workflow_call`) | producer → consumer (`repository_dispatch`) |
| **Pull** — consumer watches a feed | [`default.json`](../../default.json) Renovate preset (custom managers) | consumer ← org GitHub Packages feed |

The two are complementary: dispatch is **immediate and targeted** (fires the consumer's
auto-update workflow the moment a coherent set is tagged); Renovate is the **drift-catching
backstop** (a scheduled sweep that opens a bump PR for every embedded pin even if a dispatch is
missed). Both write to the consumer, neither bypasses review — they open PRs, the consumer's CI
(the [contract-coherence gate](contract-coherence-gate.md)) still has to pass.

## ⚠️ Dormant until the H4 admin step (#21)

Both halves are authored and committed, but **inert** until org-admin completes
[#21](https://github.com/FS-GG/.github/issues/21):

- A `GITHUB_TOKEN` **cannot** dispatch to another repo or trigger its workflows — a GitHub App
  installation token is required. The dispatch-sender mints one with
  [`actions/create-github-app-token`](https://github.com/actions/create-github-app-token); the App
  and its `app-id` / `app-private-key` org secrets are #21's deliverable. Until they exist the
  sender **fails closed** with a pointer to #21 (it never silently no-ops).
- Renovate resolves FS.GG.* from `https://nuget.pkg.github.com/FS-GG/index.json` — the feed URL is
  deterministic from the org login, but the feed itself and the Renovate app's auth to it are #21's
  deliverable.

This ordering is deliberate (the same "forward-guardrail, opt-in" model as the
[shared-build-config api-gate](../build/README.md#the-api-breaking-change-gate-opt-in-advisory--required)):
ship the config now so the admin step is a pure secrets/feed provisioning with nothing left to
design, and each producer/consumer wires its end independently.

## The dispatch sender

A producer's **release** workflow adds one job:

```yaml
jobs:
  notify-templates:
    needs: release           # only after the coherent set is published + tagged
    uses: FS-GG/.github/.github/workflows/dispatch-sender.yml@main
    with:
      target-repo: FS-GG/FS.GG.Templates
      event-type:  fs-gg-ui-template-released
      version:     ${{ needs.release.outputs.version }}
      # payload:   '{"tag":"fs-gg-ui-template/v0.1.50-preview.1"}'   # optional extra client_payload fields
    secrets:
      app-id:          ${{ secrets.FSGG_DISPATCH_APP_ID }}
      app-private-key: ${{ secrets.FSGG_DISPATCH_APP_PRIVATE_KEY }}
```

It mints a token **scoped to the target repo only**, then POSTs
`repos/<target>/dispatches` with `{event_type, client_payload:{version, source_repo, source_sha,
source_ref, ...payload}}`. The target repo handles it with `on: repository_dispatch: types:
[<event-type>]` and opens the bump PR.

Known wirings on the roadmap: Rendering release → Templates
([FS.GG.Rendering H4 #10](https://github.com/FS-GG/FS.GG.Rendering/issues/10)); Templates
registry update → SDD composition-acceptance
([FS.GG.Templates H4 #15](https://github.com/FS-GG/FS.GG.Templates/issues/15)).

## The Renovate preset

Consumer repos add `"extends": ["github>FS-GG/.github"]` to their `renovate.json`. The preset
([`default.json`](../../default.json)) layers custom managers on top of `config:recommended` for the
embedded pins the standard `nuget` manager can't see:

- **`FsGgUiVersion`** MSBuild property → `FS.GG.UI.Template` (the published coherent-set version).
- **Annotation-driven** — any literal tagged `# renovate: datasource=nuget depName=<pkg>` on the
  line above it. Used for the `FS.GG.Governance` gate-set pin and any other non-standard embedded
  literal, so a new embedded pin only needs a one-line comment to become auto-updatable.
- Standard `<PackageReference>` / `<PackageVersion>` (e.g. `FS.GG.Contracts`) need no custom manager —
  `config:recommended`'s `nuget` manager already handles them; the preset only adds the
  org-feed `registryUrls`, groups `FS.GG.UI.*` into one PR, and allows the `-preview` channel.

## See also

- [coordination protocol](README.md) · [contract-coherence gate](contract-coherence-gate.md)
- [registry](../../registry/dependencies.yml) coherence id `cross-repo-auto-update`
  ([projection](../registry/compatibility.md))
- ADR-0001 (coordination via issues), [epic #16](https://github.com/FS-GG/.github/issues/16)
