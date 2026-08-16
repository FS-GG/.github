---
schemaVersion: 1
workId: 2712-item-kind-standing-items
title: Item Kind Standing Items
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2712-item-kind-standing-items/spec.md
sourceClarifications: work/2712-item-kind-standing-items/clarifications.md
sourceChecklist: work/2712-item-kind-standing-items/checklist.md
publicOrToolFacingImpact: true
---

# Item Kind Standing Items Plan

Prose status: planned

## Source Snapshot
- spec: work/2712-item-kind-standing-items/spec.md sha256:dd8e88f313638b16dbe64ab03a67500deaba40cc322ccc4b50bcaca5508eb28e schemaVersion:1
- clarifications: work/2712-item-kind-standing-items/clarifications.md sha256:d82b38551f434db4fe27524658cce25510d3d2b78a13f43ff0a4b09b3e24797b schemaVersion:1
- checklist: work/2712-item-kind-standing-items/checklist.md sha256:49bf06025a166aa4f449efd546821b4e63f5c7e0376b8bf22876bec2bb679ba4 schemaVersion:1

## Plan Scope
- Work item 2712-item-kind-standing-items is planned from the current specification, clarification, and checklist facts.
- Requirement count: 11.
- Clarification decision count: 3.
- Checklist result count: 11.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Declare `ItemKind = Work | Anchor | Register | Directive` in `Types.fs`/`Types.fsi` immediately beside `ItemClass`, with `itemKindWireName`/`itemKindOfWireName` written on `itemClassWireName`'s exact terms — a total match with no wildcard and no empty case, the inverse DERIVED from the renderer rather than restated, and a `private everyItemKind` list pinned against the union by reflection in `TypesTests`. The vocabulary is three wires at birth (board option name, `Kind:` body value, options-table word) and is therefore spelled in exactly one function.
- PD-002 [AC-001] [FR-002] complete: Add `src/FS.GG.Coord.Core/Kind.fs`/`.fsi` beside `Class.fs`, reusing `Markdown.unfenced` and the `^ {0,3}[Kk]ind:` shape rather than inventing a third body-line grammar. It exposes `legalKinds` (reflection over the union, never a literal list), `unrecognised` (a declared-but-unreadable value is not an absent one — `.github#1651`'s rule), and `fromBody`. There is no title convention and none is invented.
- PD-003 [AC-001] [FR-003] complete: `Item` carries TWO fields on `Class`/`BoardClass`'s exact terms — `Kind: ItemKind option` (what the text declares) and `BoardKind: ItemKind option` (what the column renders). The reducer and the scheduler read ONLY the first. Reading the column would let a lagging projection decide, which `Schedulability.fs:126` refuses for `Class` and ADR-0066 settles; and it would hand anyone with board-edit rights a silent way to make a real work row unschedulable.
- PD-004 [AC-002] [FR-004] complete: Make the kind a REQUIRED POSITIONAL ARGUMENT of `LifecycleProjection.reduce` and `advance`, tested FIRST in `advance` — above the watermark staleness comparison and above `projectWithIntent`. This is the whole design, and it is chosen against both alternatives on measurement rather than taste: an exemption minted from a `Status` write would live on a watermark and inherit `Client.fs:2492`'s freeze; an exemption derived inside `lifecyclePolicyIntent` is suppressed by any pre-existing watermark, which since `.github#2690` every `add`-filed row has (critic `avocet-e644`'s probe 3, PR #2718). A parameter cannot be frozen, because the watermark carries `Intent` and never `Kind`, and the kind is re-read from the body on every pass.
- PD-005 [AC-002] [FR-005] complete: Add `Result.Exempt of kind: ItemKind` rather than folding the answer into `Withheld of reason: string`. `Withheld` is a string a caller may only log; `Exempt` is a case the compiler forces every caller to handle, which is what makes "the reducer wrote nothing" a checked property rather than a hoped-for one. `Client.lifecycleOfferChores` and the reconcile pass both emit no `Chore.lifecycleProjection` and no watermark entry on `Exempt`.
- PD-006 [AC-003] [FR-006] complete: `None` — no `Kind:` line — means `Work` at the reducer, which is every row on the board today, so the over-application leg is provable by construction as well as by fixture: the parameter Client passes is `item.Kind |> Option.defaultValue Work`, and an unchanged body therefore reaches an unchanged reducer.
- PD-007 [AC-004] [FR-007] complete: Add `Schedulability.NotAUnitOfWork of ItemKind` as step 0 of `schedulable`, BEFORE the issue-state check and before the column. `IssueClosed` and `WrongStatus` are both statements about a lifecycle this row does not have, and answering either sends the reader to fix something that is not broken — which is the precise defect `#266` reports today when `batch --explain` says `Status is Backlog`. `kind` gains the wire token `not-a-unit-of-work` and `explain` renders the kind by name.
- PD-008 [AC-005] [FR-008] complete: Carry `comments { totalCount }` on the `... on Issue` selection of both board documents. A connection's `totalCount` selects no nodes, so the 7-point board read is unchanged — the same argument `Scan.fs:158-162` already makes for `class`, `phase` and `createdAt`. Measured directly before adopting it: `comments { totalCount }` on `.github#2691` answered `83` at `rateLimit.cost` 1.
- PD-009 [AC-006] [FR-009] complete: Add `Chore.KindProjectionLag of declared: ItemKind` mapping to `Some("Kind", itemKindWireName declared)` in `ChoreKind.Write` — in Core beside `ClassProjectionLag`, never in `Client.fs`, which is where `Client.fs:3358-3362` records the field mapping belongs. `Client`'s existing field-missing gate is generalised so a board with no `Kind` field withholds the projection behind ONE diagnostic rather than one 422 per row.
- PD-010 [AC-006] [FR-010] complete: Add a `## Kind` section to `docs/coordination/board-schema.md` with a `kind-options` marker block, and register `Kind` in `scripts/project-field-options`'s `SCHEMA_MARKERS` and vocabulary check so the offline gate refuses drift in either direction and refuses an absent marker block.
- PD-011 [AC-007] [FR-011] complete: Migrate the five live rows LAST, after the exemption is proven to bind by executed test, because migrating first hands the reducer three new subjects it can mark `Done`. Each row gains a `Kind:` line in the ADR-0045 grammar; the three registers are placed on the board.

## Contract Impact
- PC-001 [PD-005] engine contract: `LifecycleProjection.Result` gains a union case. Every `match` over it in this repository is total and warnings-as-errors is on, so the break is a BUILD failure at each call site rather than a silent fallthrough — which is the point of choosing a case over a string.
- PC-002 [PD-004] engine contract: `reduce`/`advance` gain a required parameter. Same property: no caller can adopt the new signature by accident or omit the argument.
- PC-003 [PD-007] wire contract: `Schedulability.kind` publishes `not-a-unit-of-work`, which `Protocol.verdicts` derives, so the documented verdict table and the JSON a worker greps cannot disagree.
- PC-004 [PD-008] wire/board contract: both board GraphQL documents change, which invalidates every recorded replay transcript by `fixture_lib.request_key` (verb + path + canonical body). The four fixtures must be re-recorded hermetically against their own scenario servers.
- PC-005 [PD-001] board schema: a new closed single-select vocabulary is published to `docs/coordination/board-schema.md` and the offline gate.

## Verification Obligations
- VO-001 [PD-004] [PC-002] semanticTest: THE NEGATIVE LEG, and it is the one this row is judged on. A `LifecycleProjectionTests` fixture drives `advance` for each non-`work` kind with (a) a persisted watermark whose `Intent` would drive a transition and (b) an `Observation` whose facts would independently drive one — a done receipt on a closed issue, a live claim, an open PR, an unresolved blocker — and asserts `Exempt` every time. The same fixture asserts the identical inputs with `Work` produce the transition, so the test fails if the exemption over-applies AND fails if it under-applies.
- VO-002 [PD-004] [PC-002] gateInversion: Invert the exemption (make `advance` fall through for a non-`work` kind) and record the observed red, so the guard is shown to be able to fail rather than asserted to be present. Both arms of the introduced condition are new subjects, including the `work` arm whose behaviour is unchanged.
- VO-003 [PD-005] [PC-001] semanticTest: A CLI-level fixture proves the reconcile pass writes NO `Status` chore and NO lifecycle watermark for an exempt row, which is the half a Core-only test cannot reach.
- VO-004 [PD-007] [PC-003] semanticTest: `SchedulabilityTests` asserts a standing row answers `NotAUnitOfWork` and that `explain` names the kind, including for a row in `Ready` — so the verdict is shown to be reached from the kind rather than from the column.
- VO-005 [PD-002] [PC-005] semanticTest: `KindTests` binds the body grammar two-sided — spellings that must parse, and lookalikes that must not (a fenced `Kind:` line, `Kind: registers`, an empty value, `Kinder:`), with the negatives taken from the shapes this repo has actually met.
- VO-006 [PD-008] [PC-004] observedRun: Re-record all four replay transcripts hermetically and run `tests/coord-engine-replay/run.sh` green, with `/_fixture/misses` empty — an unmatched request is a hard failure and is exactly what a stale transcript looks like.
- VO-007 [PD-010] [PC-005] semanticTest: `scripts/project-field-options check --field Kind --schema docs/coordination/board-schema.md` passes, and is shown to FAIL on a mutated table in both directions (a missing option and an unexpected one).

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-004] stagedOptIn: No live board field is created by this change and none is required for it to be correct. The engine withholds the `Kind` projection behind one diagnostic while the field is absent, exactly as it does for `Class` — so the change is safe to land against today's board, and an operator creates the field with `createProjectV2Field` when they choose. Creating it is NOT a guarded single-select migration: a field that does not yet exist has no assignments to lose, which `docs/coordination/board-schema.md`'s `Class` precedent already records.
- PM-002 [PC-004] diagnoseOnly: The four replay transcripts are regenerated rather than hand-edited, from their own hermetic scenario servers, so the fixture remains a RECORDING of what the engine asked and not an author's belief about it.

## Generated View Impact
- GV-001 [PD-001] workModel: `readiness/2712-item-kind-standing-items/work-model.json` refreshes from these plan sources or reports `staleGeneratedView`.
- GV-002 [PD-008] fixture: `tests/coord-engine-replay/fixtures/*/transcript.json` and `expected/*.json` are generated artifacts of `scripts/record-board-fixture.py`; they are regenerated, never edited.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2712-item-kind-standing-items`.
