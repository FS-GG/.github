---
schemaVersion: 1
workId: ci-runtime-optimization
title: CI Runtime Optimization Implementation Plan
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/ci-runtime-optimization/spec.md
sourceClarifications: work/ci-runtime-optimization/clarifications.md
sourceChecklist: work/ci-runtime-optimization/checklist.md
publicOrToolFacingImpact: true
---

# CI Runtime Optimization Implementation Plan Plan

Prose status: planned

## Source Snapshot
- spec: work/ci-runtime-optimization/spec.md sha256:8c33e01d7ce3f1d41758c6ed435ffd26c3dd6730981ce5855736f4705de6f5b9 schemaVersion:1
- clarifications: work/ci-runtime-optimization/clarifications.md sha256:37fee34ae29c19b1381633353cbffd009ef1ccb282c3725d4d68c7d01073d0d9 schemaVersion:1
- checklist: work/ci-runtime-optimization/checklist.md sha256:30005a433204f86bb76e9f3e9130197dc72cdef0fa35b2b8aaeea0cc32e6817f schemaVersion:1

## Plan Scope
- Work item ci-runtime-optimization is planned from the current specification, clarification, and checklist facts.
- Requirement count: 11.
- Clarification decision count: 0.
- Checklist result count: 11.

## Plan Decisions
- PD-001 [AC-001] [FR-001] [FR-002] complete: Add a conservative signature-doc
  subject classifier as a fast workflow job. It emits `run_sweep=true` for every
  `src/**` change and every checker, fixture, allowlist, or workflow change; uncertainty
  also emits true. The existing `mutation-sweep` context always exists but its expensive
  step is conditional and writes an explicit summary when omitted.
- PD-002 [AC-001] [FR-003] complete: Preserve `mutants.py` population enumeration,
  unmutated control, allowlist, skipped-site proof, kill accounting, and zero-survivor
  exit contract. Trigger optimization is the first safe cut; witness-level acceleration
  is deferred until a shadow benchmark proves equivalent sensitivity.
- PD-003 [AC-001] [FR-004] complete: Keep shell `lint` unconditional because membership
  is a content predicate spanning extensionless shebang files, workflow `run:` blocks,
  composite actions, renames, and deletions.
- PD-004 [AC-001] [FR-005] complete: Remove the shipped-tree invocation from the shell
  synthetic fixture. The dedicated `lint` job becomes the only live-tree authority,
  eliminating same-tree duplicate work without changing its context.
- PD-005 [AC-001] [FR-005] [FR-006] complete: Add a conservative shell-fixture change
  classifier. Run the synthetic fixture for changes to its workflow, installer,
  linter, extractor, filters, or fixture; unknown classification runs it. Keep the
  fixture job/context present and explain an omission in the step summary.
- PD-006 [AC-001] [FR-007] complete: Give CLI, BoardOps, and Kernel test invocations
  deterministic TRX paths. Replace their second `dotnet test --no-build` executions
  with a parser over the original TRX result.
- PD-007 [AC-001] [FR-008] complete: Implement one repository test helper that reads
  TRX XML without external packages, selects the `Counters` element, validates integer
  `total`/`passed`/`failed`/`error`, requires zero failed/error and the configured floor,
  and fails closed on missing, duplicate, malformed, or inconsistent counters.
- PD-008 [AC-001] [FR-009] complete: Preserve workflow and job names so required status
  contexts remain stable. Do not modify temporal, claim, publication, replay, parity,
  or external-state workflows.
- PD-009 [AC-001] [FR-010] complete: Add fixture controls for signature subject
  classification, shell-fixture classification/live-tree separation, and TRX parsing.
  Each classifier gets irrelevant, relevant, topology, and failure-path cases.
- PD-010 [AC-001] [FR-010] complete: Run bounded inversions: make unknown classification
  skip, restore the shell fixture's live-tree coupling, and allow zero-test TRX; each
  must turn its named focused fixture red before source restoration.
- PD-011 [AC-001] [FR-011] complete: Refresh analyze, evidence, verify, and ship receipts
  only after focused gates, workflow coherence, YAML parsing, and diff checks are green.

## Contract Impact
- PC-001 [PD-001] [PD-005] workflow: Existing workflow and job context names remain
  stable. New classifier outputs control expensive steps, with `true` as the fail-closed
  default and explicit summaries for measured omissions.
- PC-002 [PD-006] [PD-007] testEvidence: `coord-engine` consumes the original TRX
  counters rather than console text from a second execution. Floors remain unchanged.
- PC-003 [PD-004] shellFixture: `tests/shell-lint/run.sh` becomes purely synthetic;
  `scripts/lint-shell.sh` over the repository is owned solely by the workflow's `lint` job.

## Verification Obligations
- VO-001 [PD-001] [PD-005] [PC-001] semanticTest: Feed synthetic changed-file lists
  through both classifiers and prove relevant, topology, rename/delete, and error inputs
  run expensive evidence while unrelated inputs emit a measured omission.
- VO-002 [PD-002] mutationTest: Run the complete signature-doc control and mutant
  accounting once on the final source, or explicitly report environmental inability;
  the classifier fixture must independently prove the sweep cannot be skipped on its subject.
- VO-003 [PD-003] [PD-004] [PC-003] semanticTest: Run live shell lint and the synthetic
  fixture independently; prove the fixture no longer invokes the shipped tree and still
  kills its synthetic bad cases.
- VO-004 [PD-006] [PD-007] [PC-002] semanticTest: Generate representative passing,
  zero-test, below-floor, failed, malformed, missing, and duplicate-counter TRX inputs;
  only the valid above-floor receipt may pass.
- VO-005 [PD-008] staticGate: Parse every edited workflow as YAML and compare workflow/job
  context names before and after; unrelated required contexts must be byte-identical.
- VO-006 [PD-009] [PD-010] mutationTest: Run the three bounded source inversions and
  record the named focused failure before restoration and a final green rerun.
- VO-007 [PD-011] readiness: Run `fsgg-sdd analyze`, `evidence`, `verify`, and `ship` in
  dependency order; require current generated views and zero blocking diagnostics.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] [PC-002] failClosed: Rollout changes no required context names.
  Reverting the workflow/helper/fixture commit restores prior scheduling. Classification
  failure runs more evidence, so partial deployment cannot silently omit a subject.

## Generated View Impact
- GV-001 [PD-011] workModel: `readiness/ci-runtime-optimization/**` is regenerated only
  through `fsgg-sdd`; authored sources remain under `work/ci-runtime-optimization/**`.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work ci-runtime-optimization`.
