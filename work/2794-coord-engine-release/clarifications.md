---
schemaVersion: 1
workId: 2794-coord-engine-release
title: Coord Engine Release
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/2794-coord-engine-release/spec.md
publicOrToolFacingImpact: true
---

# Coord Engine Release Clarifications

## Source Specification
- work/2794-coord-engine-release/spec.md

## Clarification Questions
- CQ-001: Which SemVer follows 0.67.0 when four merged commits add the cross-claim exhausted-review route and repair its forced-claim transition without changing `Protocol.fs`?
- CQ-002: Does the dedicated release item finish before or after publication and registry recording?
- CQ-003: How does the release boundary change when `.github#2772` merges before the cut freezes?

## Answers
- CQ-001: 0.68.0. The merged behavior adds receiver-observable structured review actions and authorization across claim turnover; the coherent-set policy has consistently classified additive coordination behavior as MINOR even when the wire-surface file is unchanged.
- CQ-002: After publication, promoted-manifest/feed verification, registry recording, clean install, and post-release freshness. This item was filed specifically for that debt and retains its claim through post-merge obligations.
- CQ-003: Release source remains immutable merged `origin/main`. Because #2772 merged as `7cef7301` before this cut froze, it is the fourth unreleased engine commit and must be included and re-reviewed here; only still-unmerged work remains excluded.

## Decisions
- DEC-001 [CQ-001]: Cut coherent set `0.68.0`. The 0.67.0 frontier lacks four merged engine commits: three preserve/project/authorize the exhausted cross-claim review route, and `7cef7301` repairs its forced-claim repair-phase latest-review/backlink transition. The set remains additive/compatible, with no removal and zero `Protocol.fs` wire commits; MINOR matches the repository's coherent-set convention.
- DEC-002 [CQ-002] [CQ-003]: Prepare from the latest merged main, including merged #2772, merge the version/release-note/SDD source PR, and tag only that merge commit. Recompute freshness immediately before freeze; no unmerged behavior is packaged or announced.
- DEC-003 [CQ-002]: Use the established `release-saga/1`: prepare the three packages once, atomically push the existing `kit/v0.68.0`, `drivers/v0.68.0`, and `coord-engine/v0.68.0` tags at one commit, resume manifest-bound publishers without repacking or duplicate push, then promote only after both feeds are observed.
- DEC-004 [CQ-002]: The promoted release manifest and package journals establish source/content/feed identity; an independently downloaded public package and isolated install establish consumability. Workflow status is diagnostic, not publication authority.
- DEC-005 [CQ-002]: Drive the installed 0.68.0 tool through the exact #2797 cross-claim exhausted-review route and run the existing review-wire/defect gates. A version print or source-built test alone is insufficient.

## Accepted Deferrals
- Publication and registry-finalization occur after the prepared source PR merges because tags must identify an immutable merged commit. They remain obligations of this same item and claim, not deferrals to later work.

## Remaining Ambiguity
No blocking ambiguity remains.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 2794-coord-engine-release`.
