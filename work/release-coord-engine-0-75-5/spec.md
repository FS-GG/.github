---
schemaVersion: 1
workId: release-coord-engine-0-75-5
title: Coherent coordination release 0.75.5
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Coherent coordination release 0.75.5 Specification

Prose status: specified

## User Value
Fleet users can install the post-`0.75.4` coordination lifecycle fixes from the public stable channel,
with package bytes, source tags, registry authority, and consumer pins that agree.

## Scope
- SB-001: Prepare and, after reviewed merge, cut only the `.github` coherent set containing
  `FS.GG.Coord.Cli`, `FS.GG.Kit`, and `FS.GG.Drivers` through the repository release saga.
- SB-002: Reconcile the shared version source, bounded engine release notes, canonical distributed
  tool pin, registry source/package axes, changelog, compatibility projection, publishing inventory,
  architecture disposition, and release/freshness evidence.
- SB-003: Feed-facing public projections advance to `0.75.5` only after both feeds serve the prepared
  bytes; preparation may advance only source-owned candidate facts.

## Non-Goals
- SB-004: Do not redesign or rename release workflows, package identities, command names, tag
  namespaces, stable-channel schemas, NuGet identities, or Trusted Publishing bindings.
- SB-005: Do not add engine behavior, publish another FS-GG product family, substitute a local feed,
  or perform the Typed SDD P5 default flip before OperatingV2.

## User Stories
- US-001 (P1): As a fleet user, I can restore the full-SHA completion and exact-merge verification
  repairs from a public stable release.
- US-002 (P1): As a release operator, I can prove that one reviewed merge source produced all three
  sibling packages and that both feeds, immutable tags, registry projections, and install routes agree.
- US-003 (P2): As a maintainer, I can audit or recover the release from the durable coherent-release
  saga without repacking, overwriting, or treating an upload as completed publication.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given both feeds and all three sibling tags coherently serve immutable
  `0.75.4`, when preparation is reviewed, then the shared source and every evaluated package select
  unused stable `0.75.5` with accurate bounded release notes and unchanged identities.
- AC-002 [US-002] [US-003] [FR-002]: Given the preparation PR is accepted and merged, when the existing
  coherent-release saga runs, then it packs once from that exact merge, records one manifest-bound
  artifact set, and resolves `kit/v0.75.5`, `drivers/v0.75.5`, and `coord-engine/v0.75.5` to that source.
- AC-003 [US-001] [US-002] [FR-003]: Given publication completes in the required order, when packages
  are downloaded from both feeds, then every non-signature archive entry is byte-identical and isolated
  public-feed installs execute Coord.Cli and restore Kit/Drivers content at `0.75.5`.
- AC-004 [US-002] [US-003] [FR-004]: Given both feeds serve the candidate, when feed-derived
  reconciliation completes, then registry source/package axes, changelog, compatibility projection,
  architecture disposition, publishing inventory, and canonical distributed tool pins name `0.75.5`.
- AC-005 [US-001] [US-003] [FR-005]: Given reconciliation is on `main`, when release coherence and
  engine freshness run, then sibling tags and immutable release agree, `releaseOwed=false`, no stale
  declared `0.75.4` pin remains, and both full-SHA completion residues remain terminal.

## Functional Requirements
- FR-001: The reviewed preparation MUST advance the single coherent-set source and all evaluated (covers AC-001)
  package versions from immutable public `0.75.4` to unused stable `0.75.5`, accurately describe the
  compatible release frontier, and preserve package identities and command surfaces. (Stories: US-001;
  Acceptance: AC-001)
- FR-002: Publication MUST use the existing repository coherent-release saga from the exact merged (covers AC-002)
  source, prepare one manifest-bound artifact set, and require all three sibling component tags to
  resolve to that same commit before publication. (Stories: US-002, US-003; Acceptance: AC-002)
- FR-003: The prepared packages MUST publish to GitHub Packages first and nuget.org second without (covers AC-003)
  repacking; verification MUST compare served payload entries modulo NuGet signatures and exercise
  isolated public install/restore routes for all three identities. (Stories: US-001, US-002;
  Acceptance: AC-003)
- FR-004: Feed-derived post-publication reconciliation MUST advance registry source/package versions, (covers AC-004)
  prepend release evidence to the changelog, regenerate compatibility/publishing projections, record
  architecture shape as unchanged unless derivation proves otherwise, and advance every declared
  distributed pin only to the verified public version. (Stories: US-002, US-003; Acceptance: AC-004)
- FR-005: Completion MUST require sibling-tag equality, immutable coherent-set promotion, release (covers AC-005)
  coherence, `check-engine-freshness` with `releaseOwed=false`, zero pending writes, full-SHA completion
  receipts for the repaired residues, and no remaining release-surface `0.75.4` pin. (Stories: US-001,
  US-003; Acceptance: AC-005)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- The public stable package version advances to `0.75.5`; package identities, commands, workflow
  filenames, tag namespaces, and protocol schemas remain unchanged. This is a compatible patch release.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work release-coord-engine-0-75-5`.
