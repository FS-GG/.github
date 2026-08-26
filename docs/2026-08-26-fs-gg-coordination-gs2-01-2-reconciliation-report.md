# FS.GG.Coordination GS2-01.2 reconciliation report

**Observed:** 2026-08-26T14:00Z  
**Scope:** live repository settings, organization App reach, producer prerequisite, and guarded Project migration

This addendum records observations made while registering `FS-GG/FS.GG.Coordination` as an inert governed
component. The original administrator settings report remains byte-stable because its digest is part of the
independently reviewed GS2-00 evidence subject.

## Live repository state

- Merge commits and rebase merges are disabled; squash is the only ordinary merge method.
- Auto-merge and automatic head-branch deletion are enabled.
- Issues remain enabled while repository Projects and the wiki are disabled.
- Issue intake is restricted to collaborators (`issueCreationPolicy: COLLABORATORS_ONLY`).
- The default workflow token is read-only and workflows cannot approve pull-request reviews.
- Dependency graph and alerts, Dependabot security updates, secret scanning, and push protection are enabled.
- `coordination-maintainers` has `Maintain`, not `Admin`, and no outside collaborator was observed.
- No ruleset, environment, webhook, or production event subscription exists.
- The public repository remains on `main` at node id `R_kgDOUEVTyg`.

GitHub accepted neither `secret_scanning_validity_checks=enabled` nor
`secret_scanning_non_provider_patterns=enabled`: an immediate authoritative reread returned both as
`disabled`. These are unavailable controls for the present repository configuration, not completed settings.

## Organization App action still required

Both organization App installations reported `repository_selection: all`. Renovate therefore includes
`FS.GG.Coordination`, which is the intended dependency-maintenance disposition.

The `fs-gg-cross-repo-dispatch` installation also includes the repository implicitly and carries repository
administration, content, issue, and pull-request write permissions. This is broader than the GS2 new-only
boundary. An organization owner should open the App installation settings, change **Repository access** from
**All repositories** to **Only select repositories**, retain the legacy receiver set, and exclude
`FS.GG.Coordination` until a qualified dispatch contract names it. GitHub provides no repository-local toggle
for narrowing an installation configured for all repositories. The repository currently has no dispatch
listener or production writer, so this is a latent authority exposure rather than an active v2 path.

## Producer and Project receipts

The producer prerequisite is complete: `FS.GG.SDD` release
[`v1.4.0`](https://github.com/FS-GG/FS.GG.SDD/releases/tag/v1.4.0) is anchored at merge commit
`7fec4dd4549789bca67aae004b3dad8ee0b7a4fd`. [Run 32975483123](https://github.com/FS-GG/FS.GG.SDD/actions/runs/32975483123)
passed clean public installs, dual-feed payload comparison, public-package Q2 (10/10), and Q3 (13/13).

The guarded `Repo Scope` migration added `coordination` and preserved all 321 prior Project item states: 86
assigned values and 235 unassigned values, with zero item changes and zero restore writes. The durable
[before snapshot](../work/3012-register-fs-gg-coordination/project-migration-before.json) has canonical digest
`10f66690950621e2d37d7a2bc2deab9a270b18306850bfdfa1e5717e41e2a6b0`; the complete after snapshot had
[canonical digest](../work/3012-register-fs-gg-coordination/project-migration-after.json)
`254d5a06cab54ac8f6cd89206121609da6188a6e0ec2da8b3e77449249b6c79c`. Both complete 321-item snapshots pass
the repository's integrity verifier, and their item-state arrays are identical. The live
roster/schema/resolver check reports 10 roster identities plus only the deliberate `cross-repo` aggregate
option.
