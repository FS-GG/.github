---
schemaVersion: 1
workId: 2813-release-saga-stable-predecessor
title: Stable predecessor authority decisions
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/2813-release-saga-stable-predecessor/spec.md
publicOrToolFacingImpact: true
---

# Stable predecessor authority decisions Clarifications

## Source Specification
- work/2813-release-saga-stable-predecessor/spec.md

## Clarification Questions
- CQ-001 [AMB:predecessor-authority]: What is authoritative when the registry projection and promoted channel disagree?
- CQ-002 [AMB:identity-width]: Which predecessor fields are part of prepared/retry identity?
- CQ-003 [AMB:recovery-version]: Can the already-published `0.70.0` identity be repaired in place?
- CQ-004 [AMB:validation-order]: At which workflow boundary must live-channel validation complete?
- CQ-005 [AMB:stable-version-spelling]: Which textual stable version spellings are authoritative?

## Answers
- CQ-001: The latest published `coherent-set/v*` release's `stable-channel.json` is authoritative.
  Registry metadata is a downstream projection and may neither select nor validate the predecessor.
- CQ-002: The stable receipt's exact `version` and `contentId` are both descriptor identity. The release
  tag must agree with version, and the tag target must agree with `sourceSha`, before they are trusted.
- CQ-003: No. NuGet packages and the three component tags are immutable. `0.70.0` remains unchanged,
  draft, and unpromoted; forward recovery uses unused stable coherent version `0.71.0`.
- CQ-004: Validation completes before build/test/pack. Consequently a failed read or contradiction occurs
  before any packed bytes or GitHub release mutation exists.
- CQ-005: Only the canonical three-component stable SemVer spelling is authoritative: each component is
  unsigned decimal, zero is written exactly `0`, and prerelease/build suffixes, whitespace, leading plus,
  multi-digit leading zeroes, and extra/missing segments are rejected before packing.

## Decisions
- DEC-001 [CQ-001] [FR-001]: Live promoted stable-channel receipt outranks registry projection.
- DEC-002 [CQ-002] [FR-001] [FR-002]: Add `previousStableContentId` beside
  `previousStableVersion`; require both in preparation and reusable-draft comparison.
- DEC-003 [CQ-003] [FR-003] [FR-004]: Preserve `0.70.0` byte/manifest/tag/journal/draft identity and
  advance source to `0.71.0`; actual preparation/publication/promotion remains post-merge work.
- DEC-004 [CQ-004] [FR-001]: Workflow order is resolve/validate predecessor, then build/test/pack,
  then manifest/preflight/draft. No fallback to registry or caller-provided predecessor is permitted.
- DEC-005 [CQ-005] [FR-001]: Receipt parsing and coherent tag comparison share the receipt's exact canonical
  stable SemVer triple; textual agreement cannot legalize a non-canonical spelling.

## Accepted Deferrals
No accepted deferrals recorded.

## Remaining Ambiguity
No blocking ambiguity remains.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 2813-release-saga-stable-predecessor`.
