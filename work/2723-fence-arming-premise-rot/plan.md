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
- spec: work/2723-fence-arming-premise-rot/spec.md sha256:29f2cb8a7ec153c14b98f7c1bc63db3b2b1b72dfa71d3c98409659fa8473abdf schemaVersion:1
- clarifications: work/2723-fence-arming-premise-rot/clarifications.md sha256:d14483cfd6a8cf8e3c82b47a730cbc158c4e5382b7a63802b901062b84caf842 schemaVersion:1
- checklist: work/2723-fence-arming-premise-rot/checklist.md sha256:102e2b9535d2ab976eb0229a4f2c72ef9e85c5d494233da28306a6d65d801fa9 schemaVersion:1

## Plan Scope
- Work item 2723-fence-arming-premise-rot is planned from the current specification, clarification, and checklist facts.
- Requirement count: 3.
- Clarification decision count: 3.
- Checklist result count: 3.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Establish a measurement packet for `.github/main`: identify
  the exact `claim-fence` check run on an immutable pull-request head, run the static producer
  verifier against that checkout, prove the observe-only interval exceeds 120 minutes, enumerate all
  open `item/<n>-*` pull requests and validate their authorization markers, and record the explicit
  no-merge-queue decision. Keep every API census paginated and bind its result to immutable SHAs.
- PD-002 [AC-001] [FR-002] complete: Add an arming section to
  `docs/coordination/reusable-workflow-contract.md` that records each precondition, the hub-first dry
  run, the post-merge apply obligation, the accepted stale-green residual, and the neuter-then-disarm
  rollback order. The administrative write remains outside the implementation PR and occurs only from
  the landed procedure.
- PD-003 [AC-001] [FR-003] complete: Replace the obsolete `scripts/repos.sh` claim that no org
  credential can apply with the measured current boundary: the dispatch App installation can mint an
  `administration:write` token, while an Actions `GITHUB_TOKEN` still cannot request that permission.

## Contract Impact
- PC-001 [PD-001] operator contract: `scripts/repos.sh require-context` keeps its command syntax and
  add-only/read-back behavior; only its credential guidance changes. The reusable-workflow contract
  gains the durable operating record for arming `claim-fence` on `.github/main`.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: Run `bash tests/repos-registry/run.sh`, invert the edited
  credential premise so the focused assertion reds, and re-run after restoring it. Validate every
  live precondition with paginated GitHub API reads, a static `check-required-contexts.py` invocation,
  and exact required-context read-back in dry-run mode. The post-merge obligation repeats dry-run,
  applies only to `.github`, and verifies the exact after-set.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] additiveExternalState: The reviewed PR changes no branch protection. After merge,
  the live claim holder executes the documented add-only apply for `.github/main`; rollback first
  neuters the producer, then removes the classic context with explicit confirmation and checks both
  classic protection and rulesets.

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
