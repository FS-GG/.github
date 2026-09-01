---
schemaVersion: 1
workId: gs2-04-9-protected-sandbox-authority
title: Gs2 04 9 Protected Sandbox Authority
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/gs2-04-9-protected-sandbox-authority/spec.md
sourceClarifications: work/gs2-04-9-protected-sandbox-authority/clarifications.md
sourceChecklist: work/gs2-04-9-protected-sandbox-authority/checklist.md
publicOrToolFacingImpact: true
---

# GS2-04.9 Protected Sandbox Authority Plan

Prose status: planned

## Source Snapshot
- spec: work/gs2-04-9-protected-sandbox-authority/spec.md sha256:99772085ff93d3ef7528060c26196e305bd3c6ecabb7d2e67a57e26557120c48 schemaVersion:1
- clarifications: work/gs2-04-9-protected-sandbox-authority/clarifications.md sha256:8e8d19f5b06ffba4f78fbd036f6ffb036df8f0a819ab7dbc08044481f1679386 schemaVersion:1
- checklist: work/gs2-04-9-protected-sandbox-authority/checklist.md sha256:70be775b8059b07c58a5dd8cc2edbafd7c48ffbff0221415ff57636bdced3784 schemaVersion:1

## Plan Scope
- Add one repository-owned workflow with no reusable secret-bearing call surface.
- Keep target and actor constants visible in the workflow and validate them from live API responses before passing the ephemeral token to the product harness.
- Separate execution, cleanup, evidence upload, and terminal verdict so failure cannot skip cleanup or become green.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Declare a required string `candidate_sha` dispatch input, validate exactly 40 lowercase hex characters, checkout the coordination repository at that value with pinned `actions/checkout`, and compare `git rev-parse HEAD` byte-for-byte.
- PD-002 [AC-002] [FR-002] [DEC-001] complete: Mint with pinned `actions/create-github-app-token`, owner `FS-GG`, repository `FS.GG.GitHub.Substrate.Sandbox`, and only administration/contents/issues/pull-requests/organization-projects write requests; mask and never persist the output token.
- PD-003 [AC-003] [FR-003] complete: Query `/user`, the sandbox repository, and Project 2 with the ephemeral App token; compare exact actor id, repository node/private/description, and Project node/title before exporting fixed harness coordinates.
- PD-004 [AC-004] [FR-004] [DEC-002] complete: Run execute and cleanup as separate status-recording steps, upload the evidence directory with `always()` and run/attempt-qualified naming, then fail a terminal verdict when either recorded status or cleanup receipt is non-green.

## Contract Impact
- PC-001 [PD-001] [PD-002] workflowContract: `.github/workflows/github-substrate-v2-sandbox-qualification.yml` is manual-only, exact-candidate, secret-owning, and non-reusable; its visible constants and pinned action SHAs are the reviewed authority boundary.

## Verification Obligations
- VO-001 [PD-001] [PD-002] [PD-003] [PC-001] staticTest: Workflow-lint and focused source assertions prove manual-only triggering, exact SHA validation, pinned actions, fixed sandbox identities, minimum permission requests, and absence of production coordinates or token upload.
- VO-002 [PD-004] [PC-001] negativeTest: Wrong candidate, actor, repository node, Project node, missing secret, execution failure, cleanup failure, and missing artifact mutations each remain red.
- VO-003 [PD-004] [PC-001] protectedTest: A protected live dispatch for the exact product candidate retains execution and cleanup artifacts and reports the App actor and zero residue.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] additive: This is a new manual workflow; no existing trigger, secret consumer, or production workflow is migrated.

## Generated View Impact
- GV-001 [PD-004] protectedEvidence: GitHub Actions artifacts are immutable run-scoped views named by exact candidate, run, and attempt; they never become reusable tokens or configuration.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- The product live executor must be present at the candidate SHA. This workflow is allowed to run against a PR head so protected evidence can precede product merge, but it never fetches code from an unbound branch name.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work gs2-04-9-protected-sandbox-authority`.
