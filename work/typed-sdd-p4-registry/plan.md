---
schemaVersion: 1
workId: typed-sdd-p4-registry
title: P4 Typed SDD registry and workspace creation contracts
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/typed-sdd-p4-registry/spec.md
sourceClarifications: work/typed-sdd-p4-registry/clarifications.md
sourceChecklist: work/typed-sdd-p4-registry/checklist.md
publicOrToolFacingImpact: true
---

# P4 Typed SDD registry and workspace creation contracts Plan

Prose status: planned

## Source Snapshot
- spec: work/typed-sdd-p4-registry/spec.md sha256:4b4c4d974472c18c46cb14d0e4b64539c4b40a476dfa3b3e19c9aa8beb8b1ae5 schemaVersion:1
- clarifications: work/typed-sdd-p4-registry/clarifications.md sha256:3735767218a7be33019c35c08f9cce091e74caef1604dd38085e08f3281fccb9 schemaVersion:1
- checklist: work/typed-sdd-p4-registry/checklist.md sha256:a37f9b853320e4d26d419c25c3731219306d61f6dd56b1cc225f38fa6db4ea55 schemaVersion:1

## Plan Scope
- Work item typed-sdd-p4-registry is planned from the current specification, clarification, and checklist facts.
- Requirement count: 7.
- Clarification decision count: 0.
- Checklist result count: 7.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Extend the single registry lifecycle choice to include `typed-sdd` while leaving `sdd` as its explicit default. Generate every compatibility view from that registry and add a subject mutation that changes the default so the coherence gate proves it detects a premature P5 flip.
- PD-002 [AC-001] [FR-002] complete: Advance the registered `fs-gg-ui-template` package/version/tag evidence to published `0.28.0` and the `minimum-fsgg-sdd` floor to exact published `1.4.0-preview.1`. Verify both identities against nuget.org and their producer release/tag evidence before any Templates mirror advances.
- PD-003 [AC-001] [FR-003] complete: Treat `spec-kit|sdd|typed-sdd|none` as the complete lifecycle wire vocabulary. Preserve `spec-kit` as legacy/frozen, `none` as Freeform, `sdd` as Standard SDD, and `typed-sdd` as a distinct canonical-F# backend with package, normalized-AST, receipt, projection, and readiness provenance requirements.
- PD-004 [AC-001] [FR-004] complete: Add a `Lifecycle` parameter to `NewSddWorkspace.ps1` and the interactive wizard, validate the four exact values, default omission to `sdd`, and forward the selected token unchanged as `--param lifecycle=<value>` to `fsgg-sdd scaffold`. Extend the self-test fixture with omitted, explicit `none`, `sdd`, `typed-sdd`, `spec-kit`, and invalid-value observations.
- PD-005 [AC-001] [FR-005] complete: Amend ADR-0056 and the accepted Typed SDD design only with the additive P4 contract, update the registry changelog, regenerate dependency/compatibility projections with `scripts/generate-projections`, and keep feed-coherence plus contract-coherence workflows bound to the exact registry source.
- PD-006 [AC-001] [FR-006] complete: Add executable wrong-default and lifecycle-loss controls to the registry/feed and workspace self-test suites. Each control changes only its named subject, must make the owning gate red, and restores the exact source afterward.
- PD-007 [AC-001] [FR-007] complete: Retain existing Standard SDD, Freeform, and frozen Spec Kit branches and assertions unchanged except where their shared choice enumeration must include `typed-sdd`; test their output and forwarding separately so no fallback can make a missing branch appear green.

## Contract Impact
- PC-001 [PD-001] [PD-002] [PD-003] [PD-004] registryAndWorkspaceCreation: `registry/dependencies.yml` adds one lifecycle choice and advances published dependency identities; `NewSddWorkspace.ps1` adds an optional lifecycle parameter whose omission remains backward-compatible `sdd`. Generated compatibility Markdown and provider-floor consumers derive from those authorities.

## Verification Obligations
- VO-001 [PD-001] [PD-002] [PD-003] [PC-001] registryCoherence: Run the registry validator, feed-coherence suite, projection freshness check, and live nuget.org/tag probes; mutate the default and exact dependency identities independently and require red results.
- VO-002 [PD-004] [PD-006] [PD-007] [PC-001] workspaceMatrix: Run `tests/new-sdd-workspace/run.sh` and the PowerShell self-test over omitted, `none`, `sdd`, `typed-sdd`, `spec-kit`, and invalid values; assert exact unchanged forwarding and mutate default/typed forwarding independently to red.
- VO-003 [PD-005] [PC-001] generatedViews: Run `scripts/generate-projections --check` (or repository-equivalent freshness mode), contract coherence, and documentation link/source checks after `.github#2852` releases `docs/registry/compatibility.md` and the branch is rebased.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] additiveCompatible: Existing omitted creation remains Standard SDD and every existing explicit lifecycle keeps its semantics. Consumers that do not understand `typed-sdd` remain behind the raised exact `minimum-fsgg-sdd` floor; no registry or wizard input is silently reinterpreted.

## Generated View Impact
- GV-001 [PD-001] [PD-002] [PD-005] registryCompatibility: `docs/registry/compatibility.md` and other dependency projections regenerate only from `registry/dependencies.yml`, name the additive lifecycle/default and exact published versions, and fail freshness checks if hand-edited or stale. SDD readiness views regenerate separately from these authored lifecycle sources.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work typed-sdd-p4-registry`.
