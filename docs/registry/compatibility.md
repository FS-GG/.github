# Contract & compatibility registry

Human projection of [`registry/dependencies.yml`](../../registry/dependencies.yml) —
the cross-repo source of truth for who depends on whom and which contract versions are
coherent. Update **both** when a versioned cross-repo contract changes (a
`contract-change` issue must do this as part of its resolution).

## Dependency graph

```text
FS.GG.Templates ──fs-gg-ui-template@0.1.50-preview.1 composed via scaffold (lifecycle=sdd); no vendored payload──▶ FS.GG.Rendering
        │  ──scaffold-provider@1 + SDD skeleton──▶ FS.GG.SDD
        │  ──policy@1 / capabilities@2 / tooling@1 (populated overlay)──▶ FS.GG.Governance
FS.GG.SDD ──governance-handoff@1 (OPTIONAL)──▶ FS.GG.Governance
FS.GG.Rendering ──(depends on no FS-GG product; never depends on Governance)
```

## Versioned contracts

| Contract | Version | Owner | Surface | Consumers |
|---|---|---|---|---|
| `scaffold-provider` | 1.0.0 | SDD | `.fsgg/providers.yml` + invocation protocol (canonical product-name param `name`, ADR-0005) | Templates, Rendering |
| `fsgg-contracts` | 1.0.0 | SDD | `FS.GG.Contracts` BCL-only F# package (H2, [epic #16](https://github.com/FS-GG/.github/issues/16) Pillar 2) — `Fsgg.Schemas` (typed `.fsgg` schema records + version constants) / `Fsgg.Provider` (extended `ProviderDescriptor` + `Build/Test/Run/Verify` + canonical `NameParameter`) / `Fsgg.Registry` (`dependencies.yml` types + validator) / `Fsgg.ContractVersion`. Additive; local feed until H4 ([SDD#8](https://github.com/FS-GG/FS.GG.SDD/issues/8)). Consumers re-type next: [SDD#9](https://github.com/FS-GG/FS.GG.SDD/issues/9), [Governance#14](https://github.com/FS-GG/FS.GG.Governance/issues/14), [Templates#13](https://github.com/FS-GG/FS.GG.Templates/issues/13). | SDD, Governance, Templates |
| `scaffold-provenance` | 1.0.0 | SDD | `.fsgg/scaffold-provenance.json` | SDD |
| `governance-handoff` | 1.0.0 (`1.x`) | SDD | `readiness/<id>/governance-handoff.json` | Governance |
| `governance-policy` | 1 | Governance | `.fsgg/policy.yml` | Templates |
| `governance-capabilities` | 2 | Governance | `.fsgg/capabilities.yml` | Templates |
| `governance-tooling` | 1 | Governance | `.fsgg/tooling.yml` | Templates |
| `governance-descriptor` | 1 | Governance | `.fsgg/governance.yml` (project descriptor / `ProjectFacts`; renamed from `project.yml`, ADR-0005) | Governance |
| `fs-gg-ui-template` | 0.1.50-preview.1 (pkg published; tag [`fs-gg-ui-template/v0.1.50-preview.1`](https://github.com/FS-GG/FS.GG.Rendering/releases/tag/fs-gg-ui-template/v0.1.50-preview.1)) | Rendering | `dotnet new fs-gg-ui` + `FS.GG.UI.*` packages | Templates, SDD |
| `shared-build-config` | 1.0.0 | .github | [`dist/dotnet/`](../../dist/dotnet/) `Directory.Build.props` + `Directory.Packages.props` + `.config/dotnet-tools.json`, synced by [`scripts/sync-build-config.sh`](../../scripts/sync-build-config.sh) ([ADR-0006](../adr/0006-org-shared-dotnet-build-config-and-unified-restore-locked-mode-gate.md), [guide](../build/README.md)) | Rendering, SDD, Governance, Templates |

## `.fsgg/` slot ownership

`.fsgg/` is a **shared namespace**; a scaffolded product carries both SDD and Governance
artifacts in one directory. Ownership is an explicit contract
([ADR-0005](../adr/0005-fsgg-slot-ownership-sdd-project-governance-governance.md)) — a new
product writing into `.fsgg/` checks this map before claiming a filename, so the
`project.yml` collision class cannot silently recur.

| Slot | Owner | Schema |
|---|---|---|
| `project.yml` | SDD | lifecycle project descriptor |
| `sdd.yml`, `agents.yml`, `constitution.md` | SDD | lifecycle skeleton (constitution per ADR-0004) |
| `providers.yml`, `scaffold-provenance.json` | SDD | scaffold contracts |
| `governance.yml` | Governance | governance project descriptor (`ProjectFacts`; was `project.yml`) |
| `policy.yml`, `capabilities.yml`, `tooling.yml` | Governance | gate set config |

## Coherence state

| Id | Coherent? | Owner | Summary |
|---|---|---|---|
| `fs-gg-ui-version` | ✅ yes | Rendering | Resolved 2026-06-27: the `fs-gg-ui` template pins `FsGgUiVersion=0.1.50-preview.1` (the complete coherent 16-package `FS.GG.UI.*` set) behind the immutable tag [`fs-gg-ui/v0.1.50-preview.1`](https://github.com/FS-GG/FS.GG.Rendering/releases/tag/fs-gg-ui/v0.1.50-preview.1) (commit `57be86c`). All four profiles (app, headless-scene, governed, sample-pack) generate→restore→build→evidence→governance green; the phantom `FS.GG.UI.Color`/`FS.GG.UI.SkillSupport` pins were removed; restores are byte-reproducible (`RestorePackagesWithLockFile`). **Tracking:** [FS.GG.Rendering#1](https://github.com/FS-GG/FS.GG.Rendering/issues/1) (resolved). |
| `fs-gg-ui-bom` | ✅ yes | Rendering | Resolved 2026-06-27 (Feature 207): an **optional full-set BOM / metapackage `FS.GG.UI`** is published at **0.1.51-preview.1** alongside the 16 coherent `FS.GG.UI.*` members, behind the immutable tag [`fs-gg-ui/v0.1.51-preview.1`](https://github.com/FS-GG/FS.GG.Rendering/releases/tag/fs-gg-ui/v0.1.51-preview.1) (commit `d9f4c81`). One `FS.GG.UI@0.1.51-preview.1` reference pins the whole set (live-verified: all 16 members at V, clean build, byte-reproducible across two cache-cleared restores, preview channel). Dependencies-only (exact `[$version$]` member deps, no assembly); membership locked to the packable `FS.GG.UI.*` set by an always-on parity test. Deviation is detected both ways — `NU1605` (downgrade) / `NU1608` (upgrade) — a hard restore/build failure under `WarningsAsErrors=NU1605;NU1608`/`TreatWarningsAsErrors` (repo + governed-template default), warnings by default. **Additive/optional**: the `fs-gg-ui` template default pin stays `FsGgUiVersion=0.1.50-preview.1` (not migrated). **Tracking:** feature 207 (no separate issue). |
| `fs-gg-ui-template` | ✅ yes | Rendering | Resolved 2026-06-27 (Feature 206): the `FS.GG.UI.Template` package is **published at 0.1.50-preview.1** (was 0.1.17-preview.1), carrying the ADR-0002 `lifecycle` choice symbol (`spec-kit\|sdd\|none`) and the Feature 205 side-effect-free `initGit` opt-in (`skipGitInit` removed), over the coherent `FS.GG.UI.* 0.1.50-preview.1` set. Snapshotted behind the immutable tag [`fs-gg-ui-template/v0.1.50-preview.1`](https://github.com/FS-GG/FS.GG.Rendering/releases/tag/fs-gg-ui-template/v0.1.50-preview.1) (commit `2862caf`). Verified against the **installed** package: resolvable > 0.1.17; manifest carries `lifecycle`+`initGit`; `spec-kit` byte-identical for all four profiles; default generation side-effect-free (no `.git`); all four profiles restore→build green (0 NU1101 / 0 conflict); double-restore byte-reproducible; from-tag repack reproduces the package. **Tracking:** [FS.GG.SDD#1](https://github.com/FS-GG/FS.GG.SDD/issues/1) (dependent scaffold-path request — responded). |
| `governance-overlay-populated` | ✅ yes | Governance | Resolved 2026-06-28 (ADR-0002; P3 Governance + P4 Templates): Templates' `fs-gg-governance` overlay no longer ships **empty** — it carries **real gates**. Governance (P3) published the populated reference `.fsgg` gate set at `samples/sdd-reference-gate-set/.fsgg/`; Templates (P4) populated `templates/fs-gg-governance/.fsgg/` from it — `capabilities.yml` has non-empty `checks:` (build/test/evidence, each wired to a `tooling` command), `tooling.yml` has non-empty `commands:` (`dotnet-build`, `dotnet-test`, `build-evidence`). The advertised `governance-policy@1`/`capabilities@2`/`tooling@1` surfaces are now **enforced**, not just declared. **Resolved by:** [Governance#9](https://github.com/FS-GG/FS.GG.Governance/issues/9) (gate set), [Templates#9](https://github.com/FS-GG/FS.GG.Templates/issues/9) (overlay), [Templates#10](https://github.com/FS-GG/FS.GG.Templates/issues/10) (deleted `fs-gg-fullstack/` + `sync-from-rendering.sh`). **Tracking:** [.github#14](https://github.com/FS-GG/.github/issues/14). |
| `lockfile-restore-enforcement` | ✅ yes | Rendering | Resolved 2026-06-28 (P5 — [.github#7](https://github.com/FS-GG/.github/issues/7), epic [#5](https://github.com/FS-GG/.github/issues/5)): the `FsGgUiVersion` staleness bug class is **structurally impossible** in every consumer repo — committed `packages.lock.json` + CI `--locked-mode` + `NU1603`/`NU1608`→error — not just convention-enforced. Rendering is the reference impl (Feature 211). Others adopt the same posture gating `RestoreLockedMode` on `GITHUB_ACTIONS` (SDD forces `ContinuousIntegrationBuild=true` unconditionally, so a CIB gate would fail-closed on a fresh clone); SDD & Governance each gained their first/only per-PR CI gate. Templates' packaging project has no `PackageReference`s → empty lockfile (forward guardrail only; load-bearing `FS.GG.UI.*` enforcement for scaffolded apps stays upstream). **Resolved by:** [SDD#6](https://github.com/FS-GG/FS.GG.SDD/pull/6) (gate green), [Governance#12](https://github.com/FS-GG/FS.GG.Governance/pull/12) (gate green, full 165-project build), [Templates#11](https://github.com/FS-GG/FS.GG.Templates/pull/11) (composition green). **Unified 2026-06-28 ([ADR-0006](../adr/0006-org-shared-dotnet-build-config-and-unified-restore-locked-mode-gate.md), [.github#19](https://github.com/FS-GG/.github/issues/19)):** the gate is canonicalized to `GITHUB_ACTIONS` (superseding Rendering's lone `ContinuousIntegrationBuild` gate) and published in the org-shared [`shared-build-config`](#versioned-contracts); per-repo re-sync is sequenced as H3 follow-ups, drift caught by `sync-build-config.sh --check` (wired by [.github#18](https://github.com/FS-GG/.github/issues/18)). |

This row was exactly the failure that a cross-repo coordination mechanism exists to make
visible: a downstream consumer (Templates) discovered an upstream (Rendering)
template↔framework incoherence that had no release tag and no notification path. It was
resolved 2026-06-27 by pinning the template to a tagged, reproducible `FS.GG.UI.*` snapshot
(`fs-gg-ui/v0.1.50-preview.1`) and closing [FS.GG.Rendering#1](https://github.com/FS-GG/FS.GG.Rendering/issues/1).

## Behavioral breaks

| Contract | Break | Consumer action |
|---|---|---|
| `fs-gg-ui-template` | **Feature 205 (2026-06-27): side-effect-free generation.** The `fs-gg-ui` template no longer auto-runs git-init/chmod post-actions at generation time. `skipGitInit` (opt-out) is **removed**; `initGit` (opt-in, bool, default `false`) is **added**; default generation spawns no process, creates no repo, and never hangs in CI/IDE hosts. No emitted-file changes. Contract: [`fs-gg-ui-template-generation.md`](https://github.com/FS-GG/FS.GG.Rendering/blob/main/specs/205-scaffold-git-init-chmod/contracts/fs-gg-ui-template-generation.md) (Accepted). | **SDD scaffold path** must own repo-init + chmod as explicit post-instantiation steps (contract §5 S1–S3) and stop relying on template auto-init. Direct callers: drop `--skipGitInit true`; pass `--initGit true` (plus `--allow-scripts yes` non-interactively) to reproduce the old auto-init. |
