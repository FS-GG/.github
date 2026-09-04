---
schemaVersion: 1
workId: 3208-fsharp-roadmap-telemetry-projection
title: Typed F# roadmap telemetry and projection automation
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/3208-fsharp-roadmap-telemetry-projection/spec.md
sourceClarifications: work/3208-fsharp-roadmap-telemetry-projection/clarifications.md
sourceChecklist: work/3208-fsharp-roadmap-telemetry-projection/checklist.md
publicOrToolFacingImpact: true
---

# Typed F# roadmap telemetry and projection automation Plan

Prose status: planned

## Source Snapshot
- spec: work/3208-fsharp-roadmap-telemetry-projection/spec.md sha256:4f1557d86451aceb2e51d889f251cdfb41ce806d5d4281ebbddf3569ea1cc568 schemaVersion:1
- clarifications: work/3208-fsharp-roadmap-telemetry-projection/clarifications.md sha256:78e4b8b964e46844262a4028960a78156a462e454a3f8a1268c834c82d30bb5d schemaVersion:1
- checklist: work/3208-fsharp-roadmap-telemetry-projection/checklist.md sha256:cf604e0e1444466d5a07e8b4058c613c64df07db3db1d3ea8ca0fae3dbb85bb3 schemaVersion:1

## Plan Scope
- Work item 3208-fsharp-roadmap-telemetry-projection is planned from the current specification, clarification, and checklist facts.
- Requirement count: 11.
- Clarification decision count: 0.
- Checklist result count: 1.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Add closed telemetry, lifecycle, critique, feedback, closure, and projection types to `FS.GG.Coord.Core`; keep parsing and IO adapters outside the pure reducers and expose signatures for every public module.
- PD-002 [AC-001] [AC-003] [FR-002] complete: Route the new command family through the existing kernel/parser/application layering; defer edits to #3068-overlapping Kernel paths until its merge boundary, then rebase before touching them.
- PD-003 [AC-001] [AC-002] [FR-003] complete: Treat the current Python collectors and validators as frozen compatibility oracles; canonical serialization, counter arithmetic, source joins, fork election, and exit categories are contract tests rather than implementation conveniences.
- PD-004 [AC-003] [AC-004] [FR-004] complete: Implement roadmap close as pure inspect/render/verify reducers whose only renderable region is an explicit marker-bounded block; all remote and filesystem mutation remains in existing guarded commands.
- PD-005 [AC-005] [FR-005] complete: Materialize differential fixtures from every current self-test and mutation case, then add independent F# and black-box inversions so deleting or weakening either side turns the parity gate red.
- PD-006 [AC-005] [AC-007] [FR-006] complete: Deliver in publish-before-adopt order: compiled source and packages, immutable dual-feed verification and receiver smoke, skill caller flip, then deletion. A compatibility launcher is admitted only by measured receiver evidence and contains no parsing or policy.
- PD-007 [AC-005] [AC-006] [FR-007] complete: Update `.agents` as the only authored skill source, regenerate `.claude` with the repository projection command, and add an absence gate covering source, manifests, packages, and receiver invocations for all four Python helper names.
- PD-008 [AC-007] [FR-008] complete: Represent non-required failed checks as typed external obligations with subject and materiality identity; terminal unit state is immutable unless a separately authorized materiality decision explicitly reopens it.
- PD-009 [AC-008] [FR-009] complete: Runtime adapters stream only schema fields required for counters and identifiers, retain private phase receipts outside Git, and expose content digests rather than absolute paths or conversation payloads.
- PD-010 [AC-003] [AC-007] [FR-010] complete: Reducers consume signed judgment records but never author critique sufficiency, novel finding dispositions, exceptions, or merge decisions; those remain fresh independent review phases.
- PD-011 [AC-001] [AC-002] [AC-005] [AC-006] [FR-011] complete: Gate each migration boundary independently: warning-free builds and unit tests, engine E2E, differential/mutation parity, two-pass replay, package byte verification, clean receiver smoke, generated projection agreement, privacy scan, and final helper absence.

## Contract Impact
- PC-001 [PD-001] command report: `fsgg-coord telemetry ...` and `fsgg-coord roadmap close ...` extend the command contract; stable legacy successful bytes and exit semantics remain compatibility requirements until the caller flip and deletion boundary are accepted.

## Verification Obligations
- VO-001 [PD-001] [PD-003] [PD-011] [PC-001] semanticTest: Run Core and CLI unit tests plus black-box differential fixtures for every valid mode and rejection mutation, including exact compact bytes and distinct refusal categories.

## Performance Intent
- Record phase wall time and exact post-turn token usage against the GS2-07.2 telemetry/projection baseline; the design target is evidence to assess, not an acceptance shortcut.

## Migration Posture
- PM-001 [PC-001] staged: Keep Python as frozen oracle while F# parity runs, publish compiled commands before adoption, flip generated skill callers only after receiver proof, and delete Python implementations/package references at the accepted compatibility boundary.

## Generated View Impact
- GV-001 [PD-001] [PD-011] workModel: Refresh SDD work-model, evidence, verification, ship, skill mirrors, command-contract projections, package manifests, and registry views from their owning sources; no candidate branch may hand-edit generated mirrors.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 3208-fsharp-roadmap-telemetry-projection`.
