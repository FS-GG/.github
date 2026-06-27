# Contract & compatibility registry

Human projection of [`registry/dependencies.yml`](../../registry/dependencies.yml) —
the cross-repo source of truth for who depends on whom and which contract versions are
coherent. Update **both** when a versioned cross-repo contract changes (a
`contract-change` issue must do this as part of its resolution).

## Dependency graph

```text
FS.GG.Templates ──vendors fs-gg-ui + FS.GG.UI.* framework──▶ FS.GG.Rendering
        │  ──scaffold-provider@1 + SDD skeleton──▶ FS.GG.SDD
        │  ──policy@1 / capabilities@2 / tooling@1 config──▶ FS.GG.Governance
FS.GG.SDD ──governance-handoff@1 (OPTIONAL)──▶ FS.GG.Governance
FS.GG.Rendering ──(depends on no FS-GG product; never depends on Governance)
```

## Versioned contracts

| Contract | Version | Owner | Surface | Consumers |
|---|---|---|---|---|
| `scaffold-provider` | 1.0.0 | SDD | `.fsgg/providers.yml` + invocation protocol | Templates, Rendering |
| `scaffold-provenance` | 1.0.0 | SDD | `.fsgg/scaffold-provenance.json` | SDD |
| `governance-handoff` | 1.0.0 (`1.x`) | SDD | `readiness/<id>/governance-handoff.json` | Governance |
| `governance-policy` | 1 | Governance | `.fsgg/policy.yml` | Templates |
| `governance-capabilities` | 2 | Governance | `.fsgg/capabilities.yml` | Templates |
| `governance-tooling` | 1 | Governance | `.fsgg/tooling.yml` | Templates |
| `fs-gg-ui-template` | 0.1.0-preview.1 | Rendering | `dotnet new fs-gg-ui` + `FS.GG.UI.*` packages | Templates |

## Coherence state

| Id | Coherent? | Owner | Summary |
|---|---|---|---|
| `fs-skia-ui-version` | ❌ no | Rendering | The `fs-gg-ui` template pins `FsSkiaUiVersion=0.1.0-preview.1`, but the `FS.GG.UI.*` framework HEAD is `0.1.36–0.1.47` with a refactored Scene API. The template's sample app (2026-06-15) no longer compiles against `src/Scene/Types.fsi` (2026-06-22), and there are **no release tags** pinning a coherent set. **Impact:** `fs-gg-fullstack` and SDD scaffold consumers can't build the rendering app. Open a `cross-repo:request` in FS.GG.Rendering and link it here. |

This row is exactly the failure that a cross-repo coordination mechanism exists to make
visible: a downstream consumer (Templates) discovered an upstream (Rendering)
template↔framework incoherence that has no release tag and no notification path.
