---
schemaVersion: 1
workId: 2712-item-kind-standing-items
title: Item Kind and the standing-item reducer exemption
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Item Kind and the standing-item reducer exemption Specification

Prose status: specified

## User Value
The coordination board can represent an item that has no lifecycle. A class anchor, a register or a directive is a first-class row that the lifecycle reducer never projects a `Status` for, that the scheduler refuses by naming what the row *is* rather than what column it sits in, and whose depth — for a register — is readable from the board without opening the issue.

## Scope
- SB-001: A closed `ItemKind` vocabulary (`work`, `anchor`, `register`, `directive`) declared beside `ItemClass`, read from a `Kind:` body-line sentinel in ADR-0045's shared grammar, exempting every non-`work` kind from the lifecycle reducer *at the reducer*, refused by the scheduler with a reason naming the kind, projected onto a board column as a downstream lag chore, carrying observed register depth from the board scan, documented in `docs/coordination/board-schema.md` under the offline schema gate, and applied to the five live standing rows.

## Non-Goals
- SB-002: Do not repair the watermark freeze at `Client.fs:2492` (a watermark's mere existence suppressing `lifecyclePolicyIntent` re-derivation). That cause is packeted at `.github#2691` `5309171168` and is deliberately routed around rather than fixed here — see FR-004.
- SB-003: Do not create the live Projects v2 `Kind` field. Field creation is `createProjectV2Field` on a field that does not yet exist and is an operator action, exactly as `Class` and `Severity` were; the engine withholds the projection with one diagnostic until it exists.
- SB-004: Do not add a `KIND-UNSET` lint rule. An absent `Kind:` line is a real and correct answer meaning `work`, unlike an absent `Class:` line.

## User Stories
- US-001 (P1): As an operator, I can declare that a row is a standing item and rely on the lifecycle reducer never touching its `Status` again.
- US-002 (P2): As a host planning a wave, I can read a register's depth off the board rather than opening the issue and counting.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001] [FR-002] [FR-003]: Given a row whose body declares `Kind: register`, when any reducer or scheduler pass runs over it, then the engine names it a register rather than reading it as unclassified work.
- AC-002 [US-001] [FR-004] [FR-005]: Given a standing row that already carries a persisted lifecycle watermark whose intent would drive a transition, and observations that would otherwise drive a transition, when the reducer runs, then it answers `Exempt` and writes neither a `Status` nor a watermark.
- AC-003 [US-001] [FR-006]: Given a `work` row, when the reducer and scheduler run, then it parks and promotes exactly as before this change.
- AC-004 [US-001] [FR-007]: Given a standing row the scheduler is asked about, when it answers, then the reason names the kind instead of reporting the row's `Status`.
- AC-005 [US-002] [FR-008]: Given a register row on the board, when the board is scanned, then its depth is carried out of the same page query and rendered without a second read.
- AC-006 [US-001] [FR-009] [FR-010]: Given the closed vocabulary, when the offline schema gate runs, then drift between the engine's union and the documented options table is refused in either direction.
- AC-007 [US-001] [FR-011]: Given the exemption is proven to bind, when the five live standing rows are migrated, then each declares its kind and the three registers are on the board.

## Functional Requirements
- FR-001: `ItemKind` is a closed four-case union — `Work`, `Anchor`, `Register`, `Directive` — declared beside `ItemClass` in `src/FS.GG.Coord.Core/Types.fs` and `Types.fsi`, with an `itemKindWireName`/`itemKindOfWireName` render/parse pair that spells the vocabulary exactly once and a case list pinned against the union by reflection. (Stories: US-001; Acceptance: AC-001)
- FR-002: A `Kind` module reads a `Kind:` body line under the same grammar as `Paths:`, `Class:` and `Blocked on:` — up to three leading spaces, outside any fenced code block, case and surrounding space normalised — and reports unrecognised values separately from an absent declaration, on `Class.unrecognised`'s terms. (Stories: US-001; Acceptance: AC-001)
- FR-003: The item's own text is the sole authority for the reducer exemption and for the scheduler refusal; the board `Kind` column is a downstream projection that neither decision consults, on ADR-0066's terms for `Class`. (Stories: US-001; Acceptance: AC-001)
- FR-004: `LifecycleProjection.reduce` and `LifecycleProjection.advance` take the item's kind as a required positional argument and answer `Exempt` for every non-`work` kind, decided before the persisted watermark is consulted and before any status is projected, so that no receipt and no observation can produce a park, a promotion or a `Done` for a standing row. (Stories: US-001; Acceptance: AC-002)
- FR-005: `Result` gains a distinct `Exempt` case rather than reusing `Withheld`, so every caller is forced by the compiler to tell an exempt row from a withheld projection, and the reconcile pass writes neither a `Status` chore nor a lifecycle watermark for an exempt row. (Stories: US-001; Acceptance: AC-002)
- FR-006: A `work` row — and a row declaring no kind at all, which is every row on the board today — parks and promotes exactly as before this change, proven by a fixture that fails if the exemption over-applies. (Stories: US-001; Acceptance: AC-003)
- FR-007: `Schedulability` answers `NotAUnitOfWork` carrying the kind, decided before the issue state and before the board column, and `explain` renders a reason naming the kind rather than the row's `Status`. (Stories: US-001; Acceptance: AC-004)
- FR-008: The board scan carries each issue's comment count as observed register depth, taken from the existing board page query as a connection `totalCount` that requests no additional nodes, and it is rendered on the board reads for register rows. (Stories: US-002; Acceptance: AC-005)
- FR-009: A `Kind` board column is projected from the body by a `KIND-PROJECTION-LAG` chore modelled on `CLASS-PROJECTION-LAG`, withheld with exactly one diagnostic when the project declares no `Kind` field. (Stories: US-001; Acceptance: AC-006)
- FR-010: `docs/coordination/board-schema.md` documents the closed vocabulary inside a `kind-options` marker block, and `scripts/project-field-options` checks that table against the engine's own union, refusing drift in either direction and refusing an absent marker block. (Stories: US-001; Acceptance: AC-006)
- FR-011: The five live standing rows — `.github#266` (`anchor`), `.github#2691`, `.github#2687`, `.github#2703` (`register`) and `.github#2695` (`directive`) — declare their kind, and the three registers are placed on the board, performed only after the exemption is proven to bind. (Stories: US-001; Acceptance: AC-007)

## Ambiguities
- AMB-001: Whether the exemption is expressed inside `lifecyclePolicyIntent` (as `.github#2690`'s PR body suggests) or above the watermark read.
- AMB-002: What a body declaring more than one `Kind:` value resolves to.
- AMB-003: What happens when the body could not be read at all, so no kind is observable.

## Public Or Tool-Facing Impact
- The `ItemKind` wire vocabulary is simultaneously the Projects v2 option name, the word a filer writes in a `Kind:` body line, and the word the options table documents — three wires from one function, on `itemClassWireName`'s stated terms.
- `Schedulability.kind` gains a wire token published through `Protocol.verdicts`; `LifecycleProjection.Result` gains a case that is a source-compatible break for every caller.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2712-item-kind-standing-items`.
