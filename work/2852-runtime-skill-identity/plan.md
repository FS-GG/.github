---
schemaVersion: 1
workId: 2852-runtime-skill-identity
title: Bind producer, package, materialized, and runtime-loaded skill identity
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2852-runtime-skill-identity/spec.md
sourceClarifications: work/2852-runtime-skill-identity/clarifications.md
sourceChecklist: work/2852-runtime-skill-identity/checklist.md
publicOrToolFacingImpact: true
---

# Bind producer, package, materialized, and runtime-loaded skill identity Plan

Prose status: planned

## Source Snapshot
- spec: work/2852-runtime-skill-identity/spec.md sha256:e8dbbe0e8ba61cf28d5907fe2dff23a41a75347177db2bc1ba96facee0da3437 schemaVersion:1
- clarifications: work/2852-runtime-skill-identity/clarifications.md sha256:4da41852494d983769798a770a6aca1e7e8c0e3d1f640cb7234a93582643daf5 schemaVersion:1
- checklist: work/2852-runtime-skill-identity/checklist.md sha256:806ac27d0edaf9f6a9134f6ae000972376aeebed6a4a7f5de39f0d2227f106f1 schemaVersion:1

## Plan Scope
- Extend the existing producer-manifest/registry check with a deterministic per-skill identity projection; retain the registry's `sha256` as the authoritative `SKILL.md` identity and use producer-manifest file inventories for multi-file tree identity.
- Extend pinned coordination-kit verification to emit the package manifest's per-file identity beside the materialized destination and actual digest.
- Add a `skill-view identity` route that reads the declared live/view roots, inventories every file for one skill, and reports authority, root disposition, expected/actual digests, and verdict in deterministic JSON.
- Exercise `cross-repo-coordination` for package/materialized/runtime identity and `fs-gg-feedback-report` for producer-manifest/registry identity, covering the two measured occurrences folded into this class row.
- Change only the declared registry, tools, focused tests, ADR, and this work item's SDD package; preserve existing generate/check/apply behavior.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Treat the authoritative producer manifest plus the central registry row as the source-side identity receipt; the registry's source path and sha256 remain authoritative and the producer inventory supplies subordinate file digests.
- PD-002 [AC-001] [FR-002] complete: Use one stable JSON vocabulary (`skillId`, authority, artifacts, expectedSha256, actualSha256, verdict) across source, package/materialized, and runtime projections.
- PD-003 [AC-002] [FR-003] complete: Emit identity rows from the already-restored pinned package manifest and the bytes already hashed by `coordination-sync`; make no second network read and never compare against the moving hub checkout.
- PD-004 [AC-003] [FR-004] complete: Make the receiver project declaration authoritative for live versus view root disposition; inventory every declared root and refuse ambiguous or divergent copies.
- PD-005 [AC-004] [FR-005] complete: Add one-line mutation fixtures at source/registry, package/materialized, and runtime-view boundaries, assert non-zero plus the reason class, restore, and assert green.
- PD-006 [AC-005] [FR-006] complete: Distinguish drift from inconclusive input; malformed/absent/unreadable authority or manifests produce no-verdict errors and never empty success.

## Contract Impact
- PC-001 [PD-001] [PD-002] skill identity JSON: additive, deterministic identity projection shared by registry and runtime tooling; existing output remains unchanged unless the new route/flag is requested.
- PC-002 [PD-003] pinned coordination output: additive identity facts derived from the pinned package manifest, preserving current exit codes and prose checks.
- PC-003 [PD-004] runtime root identity: the receiver declaration, not traversal order, names live source versus views; all declared copies remain required to agree.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: Run `tests/skill-registry/run.sh` for coherent identity projection and missing/malformed/divergent producer controls.
- VO-002 [PD-003] [PC-002] integrationTest: Run `tests/coordination-sync/run.sh` for pinned package-to-materialized identity, including one-line divergence red evidence.
- VO-003 [PD-004] [PC-003] integrationTest: Run `tests/skill-view/run.sh` for live/view root identity and source-order independence.
- VO-004 [PD-005] [PC-001] mutationTest: Mutate one line of the measured skill at each supported boundary, capture the reason-specific red, restore, and rerun green.
- VO-005 [PD-001] [PD-006] build: Run shellcheck/syntax checks and the three complete focused suites with anti-vacuity leg counts.

## Performance Intent
- Identity adds no network request to existing checks. It hashes only the selected skill's declared file inventory and runtime copies, so cost is linear in that skill's files rather than the repository or registry.

## Migration Posture
- PM-001 [PC-001] [PC-002] compatible: Existing invocations and prose remain compatible; identity output is opt-in and additive, and the existing registry/package digests keep their meanings.

## Generated View Impact
- GV-001 [PD-001] workModel: `readiness/2852-runtime-skill-identity/work-model.json` and `analysis.json` are generated by `fsgg-sdd tasks`/`analyze` from the authored sources and are never hand-edited; implementation identity JSON is a separate runtime/tool projection.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2852-runtime-skill-identity`.
