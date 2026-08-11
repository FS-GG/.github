---
schemaVersion: 1
workId: 2402-coherent-set-versioning
title: Coherent Set Versioning
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Coherent Set Versioning Specification

Prose status: specified

## User Value
Ship FS.GG.Kit, FS.GG.Drivers and coord-engine from one shared version scalar so a bump to any one of them cannot land without the others, closing the class of drift that let two independently hand-advanced version numbers diverge twenty minutes apart (.github#2402, commits f26da6ed/d48e1ec2).

## Scope
- SB-001: One MSBuild version property declared once in Directory.Build.props and consumed by FS.GG.Kit.csproj, FS.GG.Drivers.csproj and FS.GG.Coord.Cli.fsproj in place of each project's own independent <Version>, so the three packages cannot diverge by construction.
- SB-002: A new regression gate, with gate-inversion (mutation) evidence, that reds if any of the three projects reintroduces an independent <Version> literal that disagrees with the shared property.
- SB-003: A migration note recording the set's starting version (the max of the three current versions, so no member appears to downgrade) and the explicit reconciliation with the competing .github#2396 proposal: #2396's permitted lag governs receiver pins outside the set; this item removes lag inside the set.
- SB-004: An evidence-based accounting of every coherence gate named in this item's evidence section (source-coherence, feed-coherence, pin-coherence, engine-pin-coherence, kit-published-coherence, lock-range-coherence, contract-coherence), stating its true subject and whether the shared version scalar makes it redundant or why it is justified to keep.

## Non-Goals
- SB-005: Consolidating release-kit.yml, release-drivers.yml and release-coord-engine.yml into one workflow that actually cuts and publishes all three packages together, and cutting + verifying that real release across both feeds, is deferred to a follow-up item: rewriting the fleet's sole distribution mechanism for its coordination engine, and performing a real multi-package publish, is a larger and higher-risk change than one bounded worker session should attempt unilaterally without a maintainer decision on sequencing.

## User Stories
- US-001 (P1): As a user, I can ship FS.GG.Kit, FS.GG.Drivers and coord-engine from one shared version scalar so a bump to any one of them cannot land without the others, closing the class of drift that let two independently hand-advanced version numbers diverge twenty minutes apart (.github#2402, commits f26da6ed/d48e1ec2).

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given Coherent Set Versioning is available, when the user exercises it, then they can ship FS.GG.Kit, FS.GG.Drivers and coord-engine from one shared version scalar so a bump to any one of them cannot land without the others, closing the class of drift that let two independently hand-advanced version numbers diverge twenty minutes apart (.github#2402, commits f26da6ed/d48e1ec2).

## Functional Requirements
- FR-001: Directory.Build.props declares exactly one MSBuild version property, and FS.GG.Kit.csproj, FS.GG.Drivers.csproj and FS.GG.Coord.Cli.fsproj each resolve their <Version> from that property with no independent literal version remaining in any of the three project files. (Stories: US-001; Acceptance: AC-001)
- FR-002: A hermetic test proves the shared-version mechanism by mutation: reverting any one of the three project files to an independent literal <Version> is caught red by the new regression gate, and the unmodified tree is green. (Stories: US-001; Acceptance: AC-001)
- FR-003: The migration note states the set's starting version, shows it is greater than or equal to max(0.49.0, 0.18.0, 0.23.0), and states the .github#2396 reconciliation explicitly. (Stories: US-001; Acceptance: AC-001)
- FR-004: Every coherence gate named in this item's evidence section is evaluated against its own source, and the PR states for each whether the shared version scalar makes it redundant (and it is deleted) or why it is justified to keep (one line). (Stories: US-001; Acceptance: AC-001)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- public-contract — the three packages' published <Version> values change together going forward; the mechanism is documented in a migration note rather than silently changing release cadence.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2402-coherent-set-versioning`.
