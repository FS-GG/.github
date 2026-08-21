---
schemaVersion: 1
workId: 2794-coord-engine-release
title: Coord Engine Release
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Coord Engine Release Specification

Prose status: specified

## User Value
S.I.R. and the fleet can install a published coordination engine that executes the merged cross-claim exhausted-review escalation contract.

## Scope
- SB-001: Recompute freshness at the exact current `origin/main` base and inventory every engine commit since `coord-engine/v0.67.0`.
- SB-002: Advance the shared coherent-set version and Coord.Cli listing by the SemVer class derived from the merged behavior, then merge that prepared source before tagging.
- SB-003: Run the established release saga for FS.GG.Kit, FS.GG.Drivers, and FS.GG.Coord.Cli from the same immutable merge commit; verify both feeds, promote the manifest, update registry and compatibility records, and install the public tool locally/downstream.
- SB-004: Release only the three merged engine commits after `coord-engine/v0.67.0`; exclude all unmerged `.github#2772` behavior and do not represent it in notes or evidence.

## Non-Goals
- SB-005: Do not redesign the release saga, change engine behavior, or incorporate unmerged `.github#2772` source.
- SB-006: Do not stamp completion from workflow conclusions without served-byte and install evidence.

## User Stories
- US-001 (P1): As a S.I.R. worker, I can install the current published coordination engine and execute the merged cross-claim exhausted-review escalation contract.
- US-002 (P1): As a release operator, I can trace one coherent version from merged source through prepared bytes, both feeds, promoted manifest, registry metadata, and an isolated install.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given the current merged main after 0.67.0, when freshness and semantic impact are measured, then the next version is justified from the actual three-commit behavior and excludes unmerged #2772.
- AC-002 [US-002] [FR-002]: Given the prepared release source is merged, when all three tags and release jobs run, then one promoted manifest binds all three packages to that merge and both feeds expose identical canonical payloads.
- AC-003 [US-001] [FR-003]: Given nuget.org serves the new Coord.Cli, when a clean tool path installs it and drives the cross-claim repair/escalation fixture, then the command succeeds with the structured route introduced by #2797 and no review-wire drift.
- AC-004 [US-002] [FR-004]: Given publication and installation pass, when registry and compatibility records are updated, then source, package, tag, manifest, feed hashes, and install evidence all agree and freshness reports no standing release debt.

## Functional Requirements
- FR-001: The work records the exact release-base/head inventory, confirms zero wire-surface removals or defect drift, and chooses the next coherent SemVer from receiver-observable behavior; only merged-main content is included. (Stories: US-001; Acceptance: AC-001)
- FR-002: FS.GG.Kit, FS.GG.Drivers, and FS.GG.Coord.Cli are packed once from one merged source, tagged coherently, published to GitHub Packages and nuget.org without repacking, and promoted only after served payload identity is verified. (Stories: US-002; Acceptance: AC-002)
- FR-003: A clean public `dotnet tool install` of the released FS.GG.Coord.Cli reports the new version and passes an executable cross-claim exhausted-review escalation fixture covering #2797's structured route; legacy review-wire regression gates remain green. (Stories: US-001; Acceptance: AC-003)
- FR-004: Registry, changelog, compatibility metadata, release assets, and the post-release freshness report name the same immutable version/source/content facts, and the item remains claimed until those post-merge obligations complete. (Stories: US-002; Acceptance: AC-004)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- The shared coherent version, NuGet package listing, release tags/assets, registry contract, and installed CLI behavior are tool-facing distribution contracts.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2794-coord-engine-release`.
