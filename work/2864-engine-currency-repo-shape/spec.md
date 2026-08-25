---
schemaVersion: 1
workId: 2864-engine-currency-repo-shape
title: Repository-shape-aware engine currency verification
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Repository-shape-aware engine currency verification Specification

Prose status: specified

## User Value
Workers receive a truthful pre-write engine-currency verdict in authoring and receiver repositories.

## Scope
- SB-001: Update canonical pnext and driver guidance, their tracked mirrors and manifests, and the packaged kit version.

## Non-Goals
- SB-002: Do not change runtime engine resolution or board-write semantics.

## User Stories
- US-001 (P1): As a coordination worker, I can establish engine currency before my first board write in either an authoring or receiver repository.
- US-002 (P1): As a board host, I can run the same repo-shape-aware protocol between waves without interpreting an empty pathspec as current.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given the coordination engine source trees exist at `origin/main`, when currency is checked, then the shared checkout's immutable HEAD is compared with `origin/main` over those source trees and any positive drift triggers the existing fast-forward and rebuild route.
- AC-002 [US-001] [FR-002] [FR-003]: Given the engine source trees do not exist at `origin/main`, when currency is checked, then the `fs.gg.coord.cli` version pinned in `origin/main:.config/dotnet-tools.json` is compared with the version actually resolved by `scripts/fsgg-coord --version`, with the assembly-version trailing `.0` normalized, and disagreement refuses board writes.
- AC-003 [US-001] [US-002] [FR-004]: Given the source-shape probe, manifest, tool entry, origin ref, or resolved version is absent, unreadable, ambiguous, or unparsable, when currency is checked, then the protocol refuses with a named cause rather than reporting current.
- AC-004 [US-002] [FR-005]: Given either repository shape, when generated projections and manifests are rebuilt, then `.agents` and `.claude` guidance remain byte-coherent and the packaged kit version records the receiver-visible protocol change.

## Functional Requirements
- FR-001: The repo-shape decision MUST be derived from the immutable `origin/main` tree, and the authoring branch MUST retain the shared-checkout source-drift measurement and repair. (Stories: US-001; Acceptance: AC-001)
- FR-002: The receiver branch MUST read exactly one `fs.gg.coord.cli` pin from `origin/main:.config/dotnet-tools.json` and compare it with the engine version actually resolved from the caller context. (Stories: US-001; Acceptance: AC-002)
- FR-003: Version comparison MUST account for the .NET assembly version's ordinary trailing `.0` without weakening comparison of the remaining SemVer value. (Stories: US-001; Acceptance: AC-002)
- FR-004: Every missing, unreadable, empty, duplicate, or unparsable subject in either branch MUST refuse before a board write. (Stories: US-001, US-002; Acceptance: AC-003)
- FR-005: Canonical pnext and drive-board guidance, their tracked `.claude` projections, both skill manifests, and the FS.GG.Kit version MUST remain coherent. (Stories: US-002; Acceptance: AC-004)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- Receiver workers gain a real package-pin currency check in place of a vacuous source-path count.
- Authoring workers and board hosts retain the existing source-build freshness and repair contract.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2864-engine-currency-repo-shape`.
