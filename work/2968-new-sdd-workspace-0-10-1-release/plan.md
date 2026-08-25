---
schemaVersion: 1
workId: 2968-new-sdd-workspace-0-10-1-release
title: New Sdd Workspace 0 10 1 Release
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2968-new-sdd-workspace-0-10-1-release/spec.md
sourceClarifications: work/2968-new-sdd-workspace-0-10-1-release/clarifications.md
sourceChecklist: work/2968-new-sdd-workspace-0-10-1-release/checklist.md
publicOrToolFacingImpact: true
---

# New Sdd Workspace 0 10 1 Release Plan

Prose status: planned

## Source Snapshot
- spec: work/2968-new-sdd-workspace-0-10-1-release/spec.md sha256:44d81f347f3b55f661153cdbd492829421dc162aed6d1c98d2f92e46a7810c35 schemaVersion:1
- clarifications: work/2968-new-sdd-workspace-0-10-1-release/clarifications.md sha256:fb51568ea04e610c1455bce28530f398053b5c8c94ed237d80cde78fab74a509 schemaVersion:1
- checklist: work/2968-new-sdd-workspace-0-10-1-release/checklist.md sha256:cdad6cfa2614a344e72b7de59a032ae6ab52fddf64b821472044e45cfaaeda82 schemaVersion:1

## Plan Scope
- Treat `264725f374e3f05da46d7c3089462076a1f9bf7a` as immutable release input; inspect and pack it directly rather than moving the tag to this preparation branch.
- Keep implementation limited to SDD/readiness evidence unless a real preflight fails. The already-merged `.fsproj` version and established workflows are otherwise unchanged.
- Produce local preparation evidence now. Model tag creation, publishing, served-feed comparison, install proof, and registry reconciliation as explicit host/post-merge obligations rather than synthetic passes.

## Plan Decisions
- PD-001 [DEC-001] [AC-001] [FR-001] complete: Check the exact source commit, evaluated `Version`, self-test, locked Release build, and package metadata; wire the refusal into the matching hosted NewSdd self-test job and record reproducible command-produced results.
- PD-002 [DEC-002] [AC-002] [FR-002] complete: Leave tag/publish authority to the host and declare one publication obligation bound to the exact source and package hash.
- PD-003 [DEC-003] [AC-003] [FR-003] complete: Require both feed downloads, canonical archive comparison excluding `.signature.p7s`, nuspec commit verification, and a clean isolated global-tool install/version invocation.
- PD-004 [DEC-004] [AC-004] [FR-004] complete: Exclude registry/docs from this PR and preserve a separately sequenced feed-derived reconciliation obligation after `.github#2941`.

## Contract Impact
- PC-001 [PD-001] package: `FS.GG.NewSddWorkspace` remains the stable NuGet/tool identity at version 0.10.1; its command remains `new-sdd-workspace`.
- PC-002 [PD-002] release-coordinate: `new-sdd-workspace/v0.10.1` must resolve exactly to `264725f374e3f05da46d7c3089462076a1f9bf7a`.
- PC-003 [PD-003] artifact-provenance: both feeds must serve canonically identical payloads whose nuspec repository commit equals the release coordinate.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: `bash tests/new-sdd-workspace/run.sh` passes against the real CLI.
- VO-002 [PD-001] [PC-001] build: locked `dotnet build scripts/NewSddWorkspace/NewSddWorkspace.fsproj -c Release` and `dotnet pack` pass at the frozen source, with evaluated version 0.10.1 and repository commit metadata.
- VO-003 [PD-002] [PC-002] release: after host authorization, the immutable tag and release workflow bind to the frozen source and publish GitHub Packages before nuget.org without repacking.
- VO-004 [PD-003] [PC-003] distribution: both served packages compare canonically equal and a clean isolated tool path installs and reports 0.10.1.
- VO-005 [PD-004] registry: after `.github#2941`, feed-derived registry/changelog/compatibility reconciliation passes the registry and projection gates.
- VO-006 [PD-001] [PC-001] gateEfficacy: The hosted NewSdd self-test job runs the one-release preflight when the exact 2968 work/readiness subject changes, byte-compares its deterministic receipt, and runs tracked controls proving version, source-diff, inventory, self-test, build, nuspec, tool-list, tool-help, subject-routing, unreadable-input, and empty-input refusals red.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] compatible: This is a patch release of an existing stable tool identity with no new runtime/API behavior; consumers may opt in by exact version update.

## Generated View Impact
- GV-001 [PD-001] workModel: `readiness/2968-new-sdd-workspace-0-10-1-release/**` is regenerated from the exact authored SDD sources and must be current at handoff.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2968-new-sdd-workspace-0-10-1-release`.
