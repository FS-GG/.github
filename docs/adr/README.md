# Architecture Decision Records (cross-repo)

ADRs for decisions that span more than one FS-GG repo. Per-repo decisions live in that
repo. Use [template.md](template.md) for a new record; number sequentially.

| ADR | Title | Status |
|---|---|---|
| [0001](0001-cross-repo-coordination-via-issues.md) | Cross-repo coordination via GitHub issues + a registry | Accepted |
| [0002](0002-composition-by-scaffold-lifecycle-parameter-governance-populated.md) | Composition by scaffold, `lifecycle` template parameter, governance populated-by-default | Accepted |
| [0003](0003-rename-fs-skia-ui-version-machinery-to-fs-gg-ui.md) | Rename the `fs-skia-ui` version-coherence machinery to `fs-gg-ui` (clean break) | Proposed |
| [0004](0004-constitution-ownership-for-lifecycle-sdd-products.md) | SDD owns the `lifecycle=sdd` constitution, shipped at `.fsgg/constitution.md` | Accepted |
| [0005](0005-fsgg-slot-ownership-sdd-project-governance-governance.md) | `.fsgg/` slot ownership — SDD owns `project.yml`, Governance owns `governance.yml` | Accepted |
| [0006](0006-org-shared-dotnet-build-config-and-unified-restore-locked-mode-gate.md) | `.github` owns the org-shared .NET build config; `RestoreLockedMode` gates on `GITHUB_ACTIONS` | Accepted |
