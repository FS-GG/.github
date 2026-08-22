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
- A-001: Two producers are in scope. `FS-GG/.github` reports `claim-fence`; the shared
  `kit-materialize.yml` called by each `receives: coordination-kit` repository reports
  `materialize / receiver-validate`. The seven receivers are `FS.GG.SDD`, `FS.GG.Rendering`,
  `FS.GG.Governance`, `FS.GG.Templates`, `FS.GG.Game`, `FS.GG.Audio`, and `FS.GG.Net`.
  Merge-queue enablement remains a separate per-repository decision and is not performed here.
- A-002: No. The repository change first lands its reviewed procedure and evidence. The
  branch-protection apply is a declared post-merge obligation, keeping the fence observe-only
  throughout implementation review.

## Decisions
- DEC-001: After both producers fail closed and authorization preconditions pass, activate
  `claim-fence` on the hub first, verify it, then activate `materialize / receiver-validate` on each
  of the seven coordination-kit receivers with per-repository dry run and read-back.
- DEC-002: Record `merge queue disabled; accepted stale-green residual` for `.github` rather than
  silently enabling a queue that changes how every pull request merges.
- DEC-003: Declare the administrative arming write as a post-merge obligation and verify the exact
  required-context set by read-back after applying it.
- DEC-004: Treat missing, stale, unreadable, malformed, and unclassified authorization as red in the
  producer itself. Branch protection remains unchanged while the live authorization census is dirty.

## Accepted Deferrals
No accepted deferrals recorded.

## Remaining Ambiguity
No blocking ambiguity remains.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 2723-fence-arming-premise-rot`.
