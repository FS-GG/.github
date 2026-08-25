---
schemaVersion: 1
workId: 2941-coherent-release
title: Coherent Release
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Coherent Release Specification

Prose status: specified

## User Value
Fleet users receive a coherent stable release whose public package listing accurately describes the
accumulated coordination-engine fixes, while immutable `0.75.3` remains untouched.

## Scope
- SB-001: Prepare and, after reviewed merge, cut only the `.github` coherent set containing
  `FS.GG.Coord.Cli`, `FS.GG.Kit`, and `FS.GG.Drivers` through the repository release saga.
- SB-002: Reconcile the shared version source, bounded engine release notes, canonical distributed
  tool pin, registry source/package axes, changelog, compatibility projection, publishing skill
  inventory, and architecture projection through publish-before-flip ordering.
- SB-003: The reviewed source-preparation PR may name only versions already served by the feeds in
  public package projections. The prepared candidate version reaches public package projections only
  after both feeds serve it, through the repository's feed-derived reconciliation route.

## Non-Goals
- SB-004: Do not redesign or rename release workflows, package identities, tag namespaces, or NuGet
  Trusted Publishing bindings.
- SB-005: Do not release another FS-GG product family, add engine features, or use a local feed as a
  publication substitute.

## User Stories
- US-001 (P1): As a fleet user, I can restore the accumulated coordination-engine fixes and current
  Kit/Drivers bytes from a public stable release whose own listing accurately describes them.
- US-002 (P1): As a release operator, I can prove that one reviewed merge commit produced all three
  sibling artifacts and that both feeds, immutable tags, registry projections, and install routes agree.
- US-003 (P2): As a maintainer, I can recover or audit the release from the durable coherent-release
  saga without repacking or treating an upload as completed publication.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given both feeds and all three sibling tag namespaces coherently serve
  immutable `0.75.3` with materially incomplete engine release notes, when the source preparation is
  reviewed, then it selects unused stable `0.75.4`, evaluates every package to `0.75.4`, and accurately
  announces the compatible engine frontier without rewriting `0.75.3`.
- AC-002 [US-002] [FR-002]: Given the preparation PR is accepted and merged, when the repository
  coherent-release saga runs, then it packs once from that exact merge, records the prepared manifest,
  and all three sibling tags resolve to that one commit before any member publishes.
- AC-003 [US-001] [US-002] [FR-003]: Given publication completes, when both feeds are downloaded and
  compared, then every package's non-signature entries are byte-identical and an isolated tool/package
  install restores all three at `0.75.4`.
- AC-004 [US-002] [US-003] [FR-004]: Given both feeds serve the candidate, when feed-derived
  reconciliation completes, then registry source/package axes, changelog, compatibility projection,
  architecture projection, publishing inventory, and canonical distributed tool pin all name `0.75.4`.
- AC-005 [US-001] [US-003] [FR-005]: Given the reconciled release commit is on `main`, when engine
  freshness and release coherence are checked, then `releaseOwed=false`, no completion gap remains,
  and the exact-merge verification run that exposed the release blocker is rerunnable against `0.75.4`.

## Functional Requirements
- FR-001: The reviewed source MUST advance `FsggCoherentSetVersion` and all evaluated package versions from immutable public `0.75.3` to the live-audited, unused stable `0.75.4`, accurately repair the engine's bounded release notes forward-only, advance the distributed public pin only to observed `0.75.3`, and keep feed-facing registry/package projections at observed public values until publication. (Stories: US-001; Acceptance: AC-001)
- FR-002: Publication MUST use the repository coherent-release saga from the exact merged source, prepare one manifest-bound artifact set, and require `kit/v0.75.4`, `drivers/v0.75.4`, and `coord-engine/v0.75.4` to resolve to that same commit. (Stories: US-002, US-003; Acceptance: AC-002)
- FR-003: The exact prepared packages MUST be published to GitHub Packages first and nuget.org second without repacking, and verification MUST compare served payload entries modulo NuGet signatures and exercise isolated public install/restore routes for all three identities. (Stories: US-001, US-002; Acceptance: AC-003)
- FR-004: Feed-derived post-publication reconciliation MUST advance the `coord-engine` registry source/package versions, changelog, generated compatibility/architecture/publishing projections, and distributed tool manifest to the verified public version; architecture shape MUST remain explicitly unaffected unless the generated map proves otherwise. (Stories: US-002, US-003; Acceptance: AC-004)
- FR-005: Completion MUST require sibling-tag equality, a promoted immutable coherent-set release, `check-release-coherence` without a gap, `check-engine-freshness` with `releaseOwed=false`, zero pending board writes, and evidence that the downstream exact-merge verification can rerun. (Stories: US-001, US-003; Acceptance: AC-005)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- Public stable package version `0.75.4` accurately announces the compatible coordination repairs;
  immutable `0.75.3` remains unchanged, and package identities,
  command names, tag namespaces, and release workflow filenames remain unchanged.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2941-coherent-release`.
