# Architecture Decision Records (cross-repo)

ADRs for decisions that span more than one FS-GG repo. Per-repo decisions live in that
repo. Use [template.md](template.md) for a new record; number sequentially. Withdrawn
numbers are **retired, not reused** — a withdrawn ADR keeps a tombstone row so the
sequence stays gap-free to a reader.

| ADR | Title | Status |
|---|---|---|
| [0001](0001-cross-repo-coordination-via-issues.md) | Cross-repo coordination via GitHub issues + a registry | Accepted |
| [0002](0002-composition-by-scaffold-lifecycle-parameter-governance-populated.md) | Composition by scaffold, `lifecycle` template parameter, governance populated-by-default | Accepted |
| [0003](0003-rename-fs-skia-ui-version-machinery-to-fs-gg-ui.md) | Rename the `fs-skia-ui` version-coherence machinery to `fs-gg-ui` (clean break) | Accepted |
| [0004](0004-constitution-ownership-for-lifecycle-sdd-products.md) | SDD owns the `lifecycle=sdd` constitution, shipped at `.fsgg/constitution.md` | Accepted |
| [0005](0005-fsgg-slot-ownership-sdd-project-governance-governance.md) | `.fsgg/` slot ownership — SDD owns `project.yml`, Governance owns `governance.yml` | Accepted |
| [0006](0006-org-shared-dotnet-build-config-and-unified-restore-locked-mode-gate.md) | `.github` owns the org-shared .NET build config; `RestoreLockedMode` gates on `GITHUB_ACTIONS` | Accepted |
| [0007](0007-reference-gate-set-package-version-derivation.md) | `FS.GG.Governance.ReferenceGateSet` version-derivation rule | Accepted |
| [0008](0008-fsgg-sdd-cli-first-class-member-of-coherent-set.md) | The `fsgg-sdd` CLI is a first-class member of the coherent set (orchestrator axis) | Accepted |
| [0009](0009-cli-single-orchestrator-detect-and-remediate.md) | The `fsgg-sdd` CLI is the single orchestrator — detect-and-remediate, not silent auto-update | Accepted |
| ~~0010~~ | *SDD-native scaffold (inline `--provider-source`, explicit currency, config-driven governance default)* — declined in favour of the clone-free `scripts/new-sdd-fullstack.sh` working through existing machinery ([#100](https://github.com/FS-GG/.github/pull/100)) | **Withdrawn** |
| [0011](0011-agent-skill-roots-full-union-orchestrator-owned-mirror.md) | Every agent-skill root carries the full skill union; `fsgg-sdd` owns the mirror (orchestrator fan-out) | Accepted (implementation superseded by [0014](0014-skill-vendoring-one-manifest-one-materialize-verify.md); invariants stand) |
| [0012](0012-dual-publish-to-nuget-org.md) | Dual-publish FS-GG packages to nuget.org (public) alongside the org GitHub Packages feed | Accepted (§6 auth superseded by 0013) |
| [0013](0013-trusted-publishing-oidc-for-nuget-org.md) | Publish to nuget.org via Trusted Publishing (OIDC), not a long-lived API key | Accepted |
| [0014](0014-skill-vendoring-one-manifest-one-materialize-verify.md) | Skill vendoring & mirroring — one manifest, one materialize-and-verify, content-addressed (extends 0011) | Accepted |
| [0015](0015-register-the-registry-schema-as-a-governed-contract.md) | Register the registry schema as a governed contract (`registry-schema`; schema growth = tracked contract-change) | Accepted |
