---
schemaVersion: 1
workId: 2968-new-sdd-workspace-0-10-1-release
title: New Sdd Workspace 0 10 1 Release
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# FS.GG.NewSddWorkspace 0.10.1 Release Specification

Prose status: specified

## User Value
Users can install and trace FS.GG.NewSddWorkspace 0.10.1 as a verified stable tool release from the already-merged authoritative source.

## Scope
- SB-001: Freeze the release source at merged commit `264725f374e3f05da46d7c3089462076a1f9bf7a`, whose evaluated package version is 0.10.1.
- SB-002: Exercise the real NewSddWorkspace self-test, locked Release build, deterministic pack, and release-workflow preflight without publishing.
- SB-003: After independent review and host acceptance, create `new-sdd-workspace/v0.10.1` at the frozen source, publish the one prepared package to GitHub Packages first and then nuget.org, and verify served payload equivalence.
- SB-004: Verify nuspec repository commit, clean isolated tool installation, reported package/tool version, and feed-derived registry reconciliation in a separately sequenced continuation after `.github#2941`.

## Non-Goals
- SB-005: Do not change scaffolder behavior, package identity, the already-merged version scalar, shared coherent-set files, or either release workflow unless a preflight demonstrates a release-blocking defect.
- SB-006: Do not merge, tag, dispatch, publish, or update registry/docs from the implementing worker or before fresh independent review and host acceptance.

## User Stories
- US-001 (P1): As a workspace author, I can install FS.GG.NewSddWorkspace 0.10.1 from the public feed and invoke the stable tool identity.
- US-002 (P1): As a release operator, I can trace one immutable source commit through tag, nuspec metadata, both served feeds, and a clean install without relying on workflow status alone.

## Acceptance Scenarios
- AC-001 [US-002] [FR-001]: Given the frozen source commit, when preparation gates run, then the project evaluates to version 0.10.1, the real self-test/build/pack pass, and the resulting package metadata names the frozen repository commit.
- AC-002 [US-002] [FR-002]: Given host authorization, when the release is cut, then `new-sdd-workspace/v0.10.1` resolves exactly to the frozen source and one prepared `.nupkg` is pushed to GitHub Packages before the same bytes are pushed to nuget.org.
- AC-003 [US-001] [FR-003]: Given both feeds expose 0.10.1, when their packages are downloaded and canonical contents compared, then they match excluding only feed-added signatures, and a clean isolated `dotnet tool install` exposes `new-sdd-workspace` at 0.10.1.
- AC-004 [US-002] [FR-004]: Given publication and installation evidence, when reconciliation is sequenced after `.github#2941`, then registry, changelog, and compatibility projections are derived from observed feed reality and name the same source/version facts.

## Functional Requirements
- FR-001: Preparation must prove source `264725f374e3f05da46d7c3089462076a1f9bf7a` evaluates package version 0.10.1 and passes the real self-test, locked Release build, pack, and package-metadata checks. (Stories: US-002; Acceptance: AC-001)
- FR-002: Only the host may create `new-sdd-workspace/v0.10.1` at the frozen source and publish; the release must push one prepared package to GitHub Packages first and then nuget.org without repacking. (Stories: US-002; Acceptance: AC-002)
- FR-003: Verification must compare both served packages canonically, bind the nuspec repository commit to the tag/source, and prove a clean isolated install exposes package/tool version 0.10.1. (Stories: US-001; Acceptance: AC-003)
- FR-004: Feed-derived registry, changelog, and compatibility reconciliation must occur only after publication and after `.github#2941` clears its overlapping registry lane. (Stories: US-002; Acceptance: AC-004)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- `FS.GG.NewSddWorkspace` 0.10.1, its stable tool command, immutable tag, feed payloads, and registry compatibility row are public distribution contracts; no runtime/API behavior change is introduced by this release item.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2968-new-sdd-workspace-0-10-1-release`.
