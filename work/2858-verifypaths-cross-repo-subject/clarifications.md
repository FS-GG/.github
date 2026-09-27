---
schemaVersion: 1
workId: 2858-verifypaths-cross-repo-subject
title: Explicit verify-paths subject and Coordination cross-repo paths
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/2858-verifypaths-cross-repo-subject/spec.md
publicOrToolFacingImpact: true
---

# Explicit verify-paths subject and Coordination cross-repo paths Clarifications

## Source Specification
- work/2858-verifypaths-cross-repo-subject/spec.md

## Clarification Questions
- CQ-001 [AMB:AMB-001] blocking answered: What identifies the path namespace when a `.github` issue declares paths for another repository?

## Answers
- CQ-001: The caller must name both the Coordination issue and the PR repository with `--repo`. The explicit PR repository gives the repo-relative `Paths:` tokens their namespace. An omitted target repo cannot authorize a cross-repository verdict.

## Decisions
- DEC-001 [CQ-001] [AMB:AMB-001] [FR-002] [FR-004] [AC-002] [AC-004]: Permit the exception only for an explicitly named `FS-GG/.github` issue paired with an explicit PR `--repo`. Refuse unmatchable or undeclared cross-repository declarations. Continue to skip implicit cross-repository closing refs.

## Accepted Deferrals
No accepted deferrals recorded.

## Remaining Ambiguity
None. AMB-001 is resolved by DEC-001.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 2858-verifypaths-cross-repo-subject`.
