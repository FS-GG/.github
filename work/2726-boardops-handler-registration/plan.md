---
schemaVersion: 1
workId: 2726-boardops-handler-registration
title: Extract FS.GG.Coord.Cli.BoardOps and establish handler registration
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2726-boardops-handler-registration/spec.md
sourceClarifications: work/2726-boardops-handler-registration/clarifications.md
sourceChecklist: work/2726-boardops-handler-registration/checklist.md
publicOrToolFacingImpact: true
---

# Extract FS.GG.Coord.Cli.BoardOps and establish handler registration Plan

Prose status: planned

## Source Snapshot
- spec: work/2726-boardops-handler-registration/spec.md sha256:ee53d1f2c33c48c72755bdebbb3bedd2ad774b656bf9cb71efaedf7e44657b3a schemaVersion:1
- clarifications: work/2726-boardops-handler-registration/clarifications.md sha256:31ece806d391c02ee8a23186d4dbdd7abae6a74774755b7fdeaa26e597e95c4b schemaVersion:1
- checklist: work/2726-boardops-handler-registration/checklist.md sha256:9ccd274e213c03111ca11bebe7c544e210ae563492ae08a0ac8c0a1f2cb811dc schemaVersion:1

## Plan Scope
- Extract the fifteen issue-listed handlers from the existing CLI assembly into a new
  `FS.GG.Coord.Cli.BoardOps` project while retaining the Kernel and Options contracts.
- Introduce a data-driven handler registration table and a single dispatcher, then compose the
  BoardOps family with the remaining handlers without changing command behavior.
- Move the matching tests into `FS.GG.Coord.Cli.BoardOps.Tests` and preserve solution, pack, and
  release-payload coverage.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Represent registration as command-to-handler entries whose
  composition validates duplicate and missing command cases before dispatch.
- PD-002 [AC-001] [FR-001] complete: Keep command parsing and shared Context/Identity/Options types in
  the Kernel/current CLI boundary; BoardOps owns only the selected handlers and their direct helpers.
- PD-003 [AC-001] [FR-001] complete: Preserve the existing black-box/e2e coverage while moving focused
  unit tests into the new family test project.

## Contract Impact
- PC-001 [PD-001] internal dispatch: command selection becomes table-driven, but CLI arguments,
  stdout/stderr, side effects, and exit codes remain unchanged.
- PC-002 [PD-002] project boundary: BoardOps references Kernel/Core/GitHub contracts already consumed
  by the monolithic CLI; no new external package contract is introduced.
- PC-003 [PD-003] release payload: the new assembly must be present anywhere the CLI's existing
  project-reference payload is packed or copied.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: prove every `Options.Command` case has exactly one registered
  handler and demonstrate the check fails for a duplicate or missing entry.
- VO-002 [PD-002] [PC-002] build: build the Release solution and run focused CLI plus BoardOps suites.
- VO-003 [PD-003] [PC-003] packaging: run pack and release-payload checks with the BoardOps assembly.
- VO-004 [PD-001] [PC-001] compatibility: run the existing coordination engine e2e scripts covering
  the moved commands and compare their expected outputs/exit codes.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] atomic: land the registration table, BoardOps project, moved handlers, and test
  ownership together so no intermediate branch can omit or double-register a command.

## Generated View Impact
- GV-001 [PD-001] workModel: refresh SDD readiness views after implementation evidence is recorded;
  generated coordination projections are unchanged because the CLI command surface is unchanged.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2726-boardops-handler-registration`.
