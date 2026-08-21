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
- CQ-001: Which SemVer follows 0.67.0 when the three merged commits add a cross-claim exhausted-review escalation route without changing `Protocol.fs`?
- CQ-002: Does the dedicated release item finish before or after publication and registry recording?
- CQ-003: May the release claim include `.github#2772`, whose repair PR is reviewed but unmerged?

## Answers
- CQ-001: 0.68.0. The merged behavior adds receiver-observable structured review actions and authorization across claim turnover; the coherent-set policy has consistently classified additive coordination behavior as MINOR even when the wire-surface file is unchanged.
- CQ-002: After publication, promoted-manifest/feed verification, registry recording, clean install, and post-release freshness. This item was filed specifically for that debt and retains its claim through post-merge obligations.
- CQ-003: No. Release source is the immutable merged `origin/main`; #2772 remains a separately claimed, unmerged repair and must rebase/re-review after this release.

## Decisions
- DEC-001 [CQ-001]: Cut coherent set `0.68.0`. The 0.67.0 frontier lacks three merged engine commits that preserve completed review exhaustion across claim turnover, project the exhausted repair phase, and authorize one structured cross-claim escalation. Those are additive receiver-observable semantics, with no removal and zero `Protocol.fs` wire commits; MINOR matches the repository's coherent-set convention.
- DEC-002 [CQ-002] [CQ-003]: Prepare from the latest merged main, merge the version/release-note/SDD source PR, and tag only that merge commit. Recompute freshness immediately before freeze. Unmerged #2772 bytes and claims are neither packaged nor announced.
- DEC-003 [CQ-002]: Use the established `release-saga/1`: prepare the three packages once, atomically push the existing `kit/v0.68.0`, `drivers/v0.68.0`, and `coord-engine/v0.68.0` tags at one commit, resume manifest-bound publishers without repacking or duplicate push, then promote only after both feeds are observed.
- DEC-004 [CQ-002]: The promoted release manifest and package journals establish source/content/feed identity; an independently downloaded public package and isolated install establish consumability. Workflow status is diagnostic, not publication authority.
- DEC-005 [CQ-002]: Drive the installed 0.68.0 tool through the exact #2797 cross-claim exhausted-review route and run the existing review-wire/defect gates. A version print or source-built test alone is insufficient.

## Accepted Deferrals
- Publication and registry-finalization occur after the prepared source PR merges because tags must identify an immutable merged commit. They remain obligations of this same item and claim, not deferrals to later work.

## Remaining Ambiguity
No blocking ambiguity remains.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 2794-coord-engine-release`.
