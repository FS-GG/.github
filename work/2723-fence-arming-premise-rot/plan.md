---
schemaVersion: 1
workId: 2723-fence-arming-premise-rot
title: Fence Arming Premise Rot
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2723-fence-arming-premise-rot/spec.md
sourceClarifications: work/2723-fence-arming-premise-rot/clarifications.md
sourceChecklist: work/2723-fence-arming-premise-rot/checklist.md
publicOrToolFacingImpact: true
---

# Fence Arming Premise Rot Plan

Prose status: planned

## Source Snapshot
- spec: work/2723-fence-arming-premise-rot/spec.md sha256:76b2fa86eb896809b8b8f0108ee9c93efa2974df176d1df5b01f5551b143bdd4 schemaVersion:1
- clarifications: work/2723-fence-arming-premise-rot/clarifications.md sha256:c86f6e45846039d4590b652082a793649e1bff93f24e7f5f7ce61ebec93c121c schemaVersion:1
- checklist: work/2723-fence-arming-premise-rot/checklist.md sha256:ac8a50ff51a092f043c4722238acf1bd5784daa38eab80535100df7e5a286a62 schemaVersion:1

## Plan Scope
- Work item 2723-fence-arming-premise-rot is planned from the current specification, clarification, and checklist facts.
- Requirement count: 4.
- Clarification decision count: 4.
- Checklist result count: 4.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Make the hub `claim-fence` job consume the captured gate exit
  after rendering its diagnostic and make the shared `receiver-validate` job exit its captured code.
  Treat exit 0 as the only green; known refusal/no-verdict codes and unclassified codes fail closed.
  Keep branch-protection activation separate and ordered hub first, then seven receivers.
- PD-002 [AC-001] [FR-002] complete: Add an arming section to
  `docs/coordination/reusable-workflow-contract.md` that records both exact context names, the hub-first
  then seven-receiver dry-run/apply/read-back order, the currently failed authorization census, the
  post-merge obligation, accepted stale-green residual, and rollback order. No administrative write
  occurs while the precondition is false.
- PD-003 [AC-001] [FR-003] complete: Replace the obsolete `scripts/repos.sh` claim that no org
  credential can apply with the measured current boundary: the dispatch App installation can mint an
  `administration:write` token, while an Actions `GITHUB_TOKEN` still cannot request that permission.
- PD-004 [AC-001] [FR-004] complete: Execute both real workflow steps across positive, finding,
  retryable/permanent no-verdict, and unclassified results. Invert each consumer by replacing the
  failing exit with success and require the focused fixture to catch that fail-open mutant.

## Contract Impact
- PC-001 [PD-001] operator contract: `scripts/repos.sh require-context` keeps its command syntax and
  add-only/read-back behavior; only its credential guidance changes. The workflow contract gains the
  durable operating record for `claim-fence` on the hub and `materialize / receiver-validate` on all
  seven `coordination-kit` receivers.

## Verification Obligations
- VO-001 [PD-001] [PD-004] [PC-001] semanticTest: Run `tests/claim-fence/run.sh`,
  `tests/receiver-validate/run.sh`, and `tests/repos-registry/run.sh`; require explicit positive,
  negative, no-verdict, unclassified, and fail-open mutation evidence. Validate the SDD package,
  provenance, declared paths, and CI. The post-merge obligation repeats the live census, dry-runs,
  applies to the hub first and then each receiver, and verifies every exact after-set.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] additiveExternalState: The reviewed PR changes no branch protection. After merge and
  only after a clean authorization census, the live claim holder executes the documented add-only
  apply for `.github/main`, verifies it, then applies receiver contexts one at a time. Rollback removes
  receiver requirements in reverse order, removes the hub requirement, and only then may neutralize
  either producer; every step reads both classic protection and rulesets.

## Generated View Impact
- GV-001 [PD-001] workModel: `readiness/2723-fence-arming-premise-rot/work-model.json` and
  `analysis.json` are regenerated from the final lifecycle sources and committed with this item.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2723-fence-arming-premise-rot`.
