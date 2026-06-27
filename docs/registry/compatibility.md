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
| `fs-gg-ui-template` | 0.1.50-preview.1 | Rendering | `dotnet new fs-gg-ui` + `FS.GG.UI.*` packages | Templates |

## Coherence state

| Id | Coherent? | Owner | Summary |
|---|---|---|---|
| `fs-skia-ui-version` | ✅ yes | Rendering | Resolved 2026-06-27: the `fs-gg-ui` template pins `FsSkiaUiVersion=0.1.50-preview.1` (the complete coherent 16-package `FS.GG.UI.*` set) behind the immutable tag [`fs-skia-ui/v0.1.50-preview.1`](https://github.com/FS-GG/FS.GG.Rendering/releases/tag/fs-skia-ui/v0.1.50-preview.1) (commit `57be86c`). All four profiles (app, headless-scene, governed, sample-pack) generate→restore→build→evidence→governance green; the phantom `FS.GG.UI.Color`/`FS.GG.UI.SkillSupport` pins were removed; restores are byte-reproducible (`RestorePackagesWithLockFile`). **Tracking:** [FS.GG.Rendering#1](https://github.com/FS-GG/FS.GG.Rendering/issues/1) (resolved). |

This row was exactly the failure that a cross-repo coordination mechanism exists to make
visible: a downstream consumer (Templates) discovered an upstream (Rendering)
template↔framework incoherence that had no release tag and no notification path. It was
resolved 2026-06-27 by pinning the template to a tagged, reproducible `FS.GG.UI.*` snapshot
(`fs-skia-ui/v0.1.50-preview.1`) and closing [FS.GG.Rendering#1](https://github.com/FS-GG/FS.GG.Rendering/issues/1).
