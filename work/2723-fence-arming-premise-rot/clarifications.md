---
schemaVersion: 1
workId: 2723-fence-arming-premise-rot
title: Arm merge fence and repair repos.sh premise drift
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/2723-fence-arming-premise-rot/spec.md
publicOrToolFacingImpact: true
---

# Arm merge fence and repair repos.sh premise drift Clarifications

## Source Specification
- work/2723-fence-arming-premise-rot/spec.md

## Clarification Questions
- Q-001: Which repositories receive this fence, and is merge-queue enablement part of arming?
- Q-002: May the administrative branch-protection write occur before the implementation PR is
  independently reviewed and merged?

## Answers
- A-001: The producer exists only in `FS-GG/.github`, where the exact context is `claim-fence`.
  Receiver repositories are not targets unless they independently contain and report that producer.
  Merge-queue enablement remains a separate per-repository decision and is not performed here.
- A-002: No. The repository change first lands its reviewed procedure and evidence. The
  branch-protection apply is a declared post-merge obligation, keeping the fence observe-only
  throughout implementation review.

## Decisions
- DEC-001: Arm only repositories for which the exact context is both observed on a real pull request
  and statically producible; do not derive a fleet target from the coordination-kit receiver roster.
- DEC-002: Record `merge queue disabled; accepted stale-green residual` for `.github` rather than
  silently enabling a queue that changes how every pull request merges.
- DEC-003: Declare the administrative arming write as a post-merge obligation and verify the exact
  required-context set by read-back after applying it.

## Accepted Deferrals
No accepted deferrals recorded.

## Remaining Ambiguity
No blocking ambiguity remains.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 2723-fence-arming-premise-rot`.
