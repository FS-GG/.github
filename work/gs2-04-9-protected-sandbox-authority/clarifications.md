---
schemaVersion: 1
workId: gs2-04-9-protected-sandbox-authority
title: Gs2 04 9 Protected Sandbox Authority
stage: clarify
changeTier: tier1
status: needsAnswers
sourceSpec: work/gs2-04-9-protected-sandbox-authority/spec.md
publicOrToolFacingImpact: true
---

# GS2-04.9 Protected Sandbox Authority Clarifications

## Source Specification
- work/gs2-04-9-protected-sandbox-authority/spec.md

## Clarification Questions
- CQ-001 [AMB:AMB-001]: Which subset of the installed App grant is necessary for the bounded live plan?
- CQ-002 [AMB:AMB-002]: How does the workflow preserve the harness failure while guaranteeing cleanup and artifact retention?

## Answers
- CQ-001 → Request repository `administration:write`, `contents:write`, `issues:write`, `pull_requests:write`, and organization `projects:write`, scoped to the one sandbox repository; do not request packages or organization administration.
- CQ-002 → The live command records its status without terminating the step, an unconditional cleanup command runs next, artifact upload uses `always()`, and a terminal assertion exits non-zero if either execution or cleanup was non-green.

## Decisions
- DEC-001 [CQ-001] [AMB:AMB-001] [FR-002] [AC-002]: Mint only administration, contents, issues, pull requests, and organization Projects write for `FS.GG.GitHub.Substrate.Sandbox`; every other installation grant remains unavailable to the job token.
- DEC-002 [CQ-002] [AMB:AMB-002] [FR-004] [AC-004]: Separate execute, cleanup, upload, and terminal-verdict steps; execution failure is data passed forward, cleanup is unconditional, upload is unconditional, and the final step preserves any red outcome.

## Accepted Deferrals
No accepted deferrals recorded.

## Remaining Ambiguity
- None. DEC-001 and DEC-002 resolve both ambiguities without deferral.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work gs2-04-9-protected-sandbox-authority`.
