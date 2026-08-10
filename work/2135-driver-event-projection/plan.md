---
schemaVersion: 1
workId: 2135-driver-event-projection
title: Driver Event Projection
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2135-driver-event-projection/spec.md
sourceClarifications: work/2135-driver-event-projection/clarifications.md
sourceChecklist: work/2135-driver-event-projection/checklist.md
publicOrToolFacingImpact: true
---

# Driver Event Projection Plan

Prose status: planned

## Source Snapshot
- spec: work/2135-driver-event-projection/spec.md sha256:7a2e3a0dd52f59e226d8916800d7e18b6e771d351d420a29146bfe3b415a4d08 schemaVersion:1
- clarifications: work/2135-driver-event-projection/clarifications.md sha256:9f64fcc5ae33e89219d7b1132aa62d6d21d950401bfab6563e66926fd594d93d schemaVersion:1
- checklist: work/2135-driver-event-projection/checklist.md sha256:07fd68d2d7ae7925324128e76c19f15e925482eb70dcb807f16afdb7eeefc434 schemaVersion:1

## Plan Scope
- Add a pure `FS.GG.Coord.Core` module (`DriverEvents`) that classifies one
  item's live facts into a typed `MaterialState`, diffs it against a durable
  per-item cursor to emit `TransitionEvent`s, and renders the complete active
  inventory every call — reusing `Delivery.Stage` and `Driver.ReviewChain`
  rather than inventing a second state vocabulary.
- Add a CLI command surface (extending `driver`) that assembles `ItemFacts` for
  every board candidate from the same live reads `driver`/`delivery` already
  perform, loads/writes a JSON cursor file, and renders JSON (authoritative) and
  stable two-line text projections.
- Update `.agents/skills/drive-board`, `.claude/skills/drive-board`, and their
  `work-board` counterparts to forward the projection's two-line text instead of
  authoring status prose from memory.
- Add Core and CLI regression tests reproducing the six named scenarios:
  omitted-active-item, premature-worker-return, review-to-repair,
  merged-awaiting-release, external-claim, and failed-read.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: `MaterialState` is a closed DU (Ready,
  Claimed, ReviewHandoff, ReviewRepair, CiLandable, MergedAwaitingObligations,
  Released, HumanBlocked, Done, Unreadable). `deriveEvents: Cursor -> ItemFacts
  list -> TransitionEvent list * Cursor` is pure and total; an event is emitted
  only when an item's classified state differs from `Cursor.TryFind ref`.
- PD-002 [AC-002] [FR-002] complete: The cursor is a `Map<string, MaterialState>`
  keyed by canonical item ref, serialized to a caller-supplied JSON file between
  runs. Feeding `deriveEvents`'s own returned cursor back as the next call's
  input over identical facts is the idempotency property under test.
- PD-003 [AC-003] [FR-003] complete: `isActive: MaterialState -> bool` is true
  exactly for Claimed, ReviewHandoff, ReviewRepair, CiLandable, and
  MergedAwaitingObligations (issue acceptance #4); `project` renders `Active =
  facts |> List.filter (isActive << .State)` unconditionally, independent of
  `Transitions`, so "nothing transitioned" and "nothing active" stay distinct
  renderings that can never collapse into each other.
- PD-004 [AC-004] [FR-004] complete: `ItemFacts` are assembled solely from a
  fresh `scanAndDecide`/`Snapshot.parse` board read plus `Reads.markerScan`,
  `Reads.prLandable`, `Driver.parseReviewComments`, and `Delivery.Obligation`
  reads — the same live facts `driver`/`delivery` already gather — so a claim,
  PR, or check made by any process is visible on the next read without the
  reading host having dispatched it.
- PD-005 [AC-005] [FR-005] complete: Classification never inspects worker
  process liveness or CLI invocation history — only claim marker + board +
  PR/review facts — so a returning worker process is structurally incapable of
  producing a transition by itself; `WorkerReturn`-shaped facts (live claim, no
  review-ready evidence, not parked/Done) classify as the same active state as
  before the return.
- PD-006 [AC-006] [FR-006] complete: Any `ItemFacts` construction step that
  fails (board scan, marker scan, PR read) sets `State = Unreadable reason`
  rather than being dropped from the input list; `Unreadable` is excluded from
  `isActive`, so it never masquerades as "nothing active" but is never silently
  omitted either — it is a transition event like any other typed state change.
- PD-007 [AC-007] [FR-007] complete: The CLI command emits
  `fsgg.coord.driver-events/1` JSON (`JsonSerializer.Serialize` over an
  anonymous record, matching the existing `driver`/`delivery-route` JSON
  convention) and a `renderText` two-line form (line 1: transitions or "no
  material transitions"; line 2: active inventory or "no active items").
  Drive-board/work-board skill guidance is edited in both `.agents` and
  `.claude` roots to call this command and paste its text output verbatim
  rather than composing a status update.

## Contract Impact
- PC-001 [PD-001] command report: New `DriverEvents` Core module and CLI
  surface; existing `driver`/`delivery` command contracts are unchanged.
- PC-002 [PD-007] skill contract: `drive-board`/`work-board` (both roots) gain a
  documented step that consumes the new projection; no existing skill step is
  removed.

## Verification Obligations
- VO-001 [PD-001] [PD-002] [PC-001] semanticTest: `DriverEventsTests` (Core)
  covers the six named scenarios plus idempotent re-read and unreadable-event
  cases; a CLI test exercises `--json`/text rendering and cursor round-trip.
- VO-002 [PD-006] semanticTest: A gate-inversion case proves `Unreadable` is
  excluded from `isActive` (invert the exclusion, observe the previously
  correct "not active" assertion go red).

## Performance Intent
No performance intent is declared for this work item.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] additive: The new `DriverEvents` module, CLI surface, and
  cursor file are purely additive; no existing `driver`/`delivery` command
  output or schema changes shape. A cursor file that does not yet exist reads
  as an empty `Cursor`, so first-run behavior needs no migration step.

## Generated View Impact
- GV-001 [PD-001] workModel: `readiness/2135-driver-event-projection/work-model.json`
  refreshes from current plan sources on each `fsgg-sdd` stage run; the
  `registry/driver-skill-manifest.json` and
  `registry/coordination-kit-skill-manifest.json` generated views are
  regenerated separately (`generate-driver-manifest`) once the drive-board/
  work-board skill edits (PC-002) land, so CI's `--check` gate stays green.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2135-driver-event-projection`.
