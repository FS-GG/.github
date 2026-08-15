---
schemaVersion: 1
workId: 2654-tag-without-publish
title: kit-auto-publish tags without prepared bytes
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/2654-tag-without-publish/spec.md
publicOrToolFacingImpact: true
---

# kit-auto-publish tags without prepared bytes Clarifications

## Source Specification
- work/2654-tag-without-publish/spec.md

## Clarification Questions
- CQ-001 [AMB:AMB-001] blocking open: Resolve source ambiguity AMB-001 before checklist.
- CQ-002 [AMB:AMB-002] blocking open: Resolve source ambiguity AMB-002 before checklist.
- CQ-003 [AMB:AMB-003] blocking open: Resolve source ambiguity AMB-003 before checklist.
- CQ-004 [AMB:AMB-004] blocking open: Resolve source ambiguity AMB-004 before checklist.

## Answers
- CQ-001 [AMB:AMB-001] decision: Take the second direction. kit-auto-publish CALLS release-saga-prepare as a reusable workflow and pushes its tags only in a job that needs that call, and the header assumption is corrected in the same change. The first direction was rejected on the record: release-kit packed its own artifact until .github#2600 moved packing into a dispatch-only workflow, so making the auto rail read for a human-prepared release would narrow a capability that a refactor removed by accident rather than by decision, and the prepared release provably cannot exist before the merge commit it must be bound to.
- CQ-002 [AMB:AMB-002] decision: Pack, preflight and bind the manifest at the exact commit the coherent-set tags name, supplied to the called workflow as an explicit source-sha input. On the fresh tag path that equals the caller head; on the tagSiblings repair path it is the commit the existing kit tag already names.
- CQ-003 [AMB:AMB-003] decision: No new machinery. The residual state where tags exist and both feeds are empty is already read by decide as tag-exists-without-both-feed-publication, which escalates stickily and reds the job at the streak bound, so it is detected today and was only ever unrepairable.
- CQ-004 [AMB:AMB-004] accepted deferral: accepted deferral: Executing the 0.58.1 recovery is operator-gated and outside this work. This work records the disposition and the exact recovery route at commit a415652f, and surfaces the dispatch for a human rather than performing it.

## Decisions
- DEC-001 [CQ-001] [AMB:AMB-001]: Take the second direction. kit-auto-publish CALLS release-saga-prepare as a reusable workflow and pushes its tags only in a job that needs that call, and the header assumption is corrected in the same change. The first direction was rejected on the record: release-kit packed its own artifact until .github#2600 moved packing into a dispatch-only workflow, so making the auto rail read for a human-prepared release would narrow a capability that a refactor removed by accident rather than by decision, and the prepared release provably cannot exist before the merge commit it must be bound to.
- DEC-002 [CQ-002] [AMB:AMB-002]: Pack, preflight and bind the manifest at the exact commit the coherent-set tags name, supplied to the called workflow as an explicit source-sha input. On the fresh tag path that equals the caller head; on the tagSiblings repair path it is the commit the existing kit tag already names.
- DEC-003 [CQ-003] [AMB:AMB-003]: No new machinery. The residual state where tags exist and both feeds are empty is already read by decide as tag-exists-without-both-feed-publication, which escalates stickily and reds the job at the streak bound, so it is detected today and was only ever unrepairable.

## Accepted Deferrals
- DEC-004 [CQ-004] [AMB:AMB-004]: accepted deferral: Executing the 0.58.1 recovery is operator-gated and outside this work. This work records the disposition and the exact recovery route at commit a415652f, and surfaces the dispatch for a human rather than performing it.

## Remaining Ambiguity
No blocking ambiguity remains.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 2654-tag-without-publish`.
