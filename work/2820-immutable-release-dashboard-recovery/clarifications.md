---
schemaVersion: 1
workId: 2820-immutable-release-dashboard-recovery
title: Immutable Release Dashboard Recovery
stage: clarify
changeTier: tier1
status: needsAnswers
sourceSpec: work/2820-immutable-release-dashboard-recovery/spec.md
publicOrToolFacingImpact: true
---

# Immutable Release Dashboard Recovery Clarifications

## Source Specification
- work/2820-immutable-release-dashboard-recovery/spec.md

## Clarification Questions
- CQ-001 [AMB:AMB-001] blocking answered: Should delivery move to promotion, or should package recovery make journal persistence read-only after immutability?
- CQ-002 [AMB:AMB-002] blocking answered: Which immutable remote journal facts must be revalidated before a skipped upload is safe?
- CQ-003 [AMB:AMB-003] blocking answered: How does the fixture prove workflow reachability and not merely source-token presence?

## Answers
- CQ-001 [AMB:AMB-001] answer: Keep package-owned receiver delivery where it is. Change the shared `upload_journal` boundary so an immutable release causes an exact remote journal download, validation, and read-back with no asset mutation; mutable drafts retain their existing clobber upload.
- CQ-002 [AMB:AMB-002] answer: The immutable branch must download the exact `journal-$package.json` asset and validate its release id, version, source SHA, policy version, package identity, and prepared artifacts against the current run. Missing, unreadable, or mismatched state is a refusal, never a skipped success.
- CQ-003 [AMB:AMB-003] answer: The release-saga fixture drives the production adapter against a fake `gh`: immutable `release view` is followed by exact `release download`, while every `release upload --clobber` would return HTTP 422. It asserts both a green valid read-back and a red missing/mismatched journal. A topology parser binds both package workflows' journal sequence to their later dashboard token/write/read-back steps, and the immutable branch is subject-mutated to prove the gate goes red.

## Decisions
- DEC-001 [CQ-001] [AMB:AMB-001] [FR-001] [FR-002] [FR-003]: Preserve package-owned dashboard delivery and make `upload_journal` state-sensitive: mutable draft means clobber upload; immutable published release means download and validate the existing exact journal, with no upload/delete/edit call.
- DEC-002 [CQ-002] [AMB:AMB-002] [FR-004]: Immutable read-back is fail-closed and validates the remote journal through the production `release-saga.py` identity/artifact contracts; release immutability alone is not permission to ignore a missing or stale journal.
- DEC-003 [CQ-003] [AMB:AMB-003] [FR-005] [FR-006]: Verification combines executed production-adapter recovery against a rejecting fake GitHub API with parsed workflow ordering for both package routes and the existing executed dashboard-tick refusal suite. Every added or modified gate receives a subject-breaking mutation and unreadable-input leg.
- DEC-004 [FR-003]: Promotion remains the sole immutable-release transition and continues to require exactly three package journals and externally observed byte-identical feeds; no package payload, feed, tag, or stable-channel format changes.

## Accepted Deferrals
No accepted deferrals recorded.

## Remaining Ambiguity
- None. AMB-001 through AMB-003 are resolved by DEC-001 through DEC-003.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 2820-immutable-release-dashboard-recovery`.
