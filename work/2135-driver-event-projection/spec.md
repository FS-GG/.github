---
schemaVersion: 1
workId: 2135-driver-event-projection
title: Driver Event Projection
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Driver Event Projection Specification

Prose status: specified

## User Value
A drive-board or work-board host renders the exact two-line material-transition
update and the complete active-item inventory by reading one engine projection,
instead of detecting transitions and reconstructing the active set from memory.

## Scope
- SB-001: A durable, versioned per-item event cursor (Core) that classifies live
  board/claim/PR/review/delivery facts into a typed material state and emits a
  transition event only when that state changes since the cursor.
- SB-002: A complete active-item inventory (claimed, in review, newly dispatched,
  or merged with unverified obligations), rendered every time regardless of
  whether anything transitioned.
- SB-003: JSON (authoritative) and stable two-line text projections of both the
  transitions and the active inventory, exposed on the `fsgg-coord` CLI.
- SB-004: Equivalent drive-board/work-board skill guidance, in both `.agents` and
  `.claude` roots, that forwards the projection instead of authoring status prose.

## Non-Goals
- SB-005: Do not decide wave consolidation, dispatch sizing, or review-chain
  validity — those remain `Driver.nextAction` (`.github#2127`).
- SB-006: Do not implement later lifecycle commands or Governance enforcement in
  this specification.

## User Stories
- US-001 (P1): As a drive-board host, I receive only the material transitions
  that actually happened since my last read, so my update is never late and
  never duplicated.
- US-002 (P1): As a host, I see the complete active-item inventory on every read,
  including items claimed or advanced by another process, so I never report a
  live claim as terminal or omit externally claimed work.
- US-003 (P1): As a host, a failed live read is reported as an explicit
  unreadable event, never rendered as an empty, all-clear active list.
- US-004 (P2): As a runtime-specific host adapter (Claude or Codex), I forward
  the same JSON/text projection without needing my own transition logic.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given a prior cursor and a live fact read in which an
  item's classified material state differs from the cursor, when the projection
  is derived, then exactly one transition event is emitted for that item, naming
  its previous and new state, reason, evidence, and observed timestamp.
- AC-002 [US-001] [FR-002]: Given a prior cursor and a live fact read in which no
  item's classified state differs from the cursor, when the projection is
  derived twice in a row over the same facts, then zero transition events are
  emitted on the second call (idempotent re-read).
- AC-003 [US-002] [FR-003]: Given items in Ready, Claimed, review handoff,
  review/repair, CI/landable, merged-awaiting-obligations, released, Blocked, and
  Done states, when the active inventory is rendered, then it contains exactly
  the items that are claimed, in review, newly dispatched, or merged with
  unverified obligations — and nothing else — even when zero items transitioned.
- AC-004 [US-002] [FR-004]: Given a claim, PR, or check-state change made by a
  process other than the host issuing the read, when the projection is next
  derived, then that external change appears as a transition and/or in the
  active inventory exactly as if the host itself had made it.
- AC-005 [US-002] [FR-005]: Given a worker process returns while its claim
  remains live and the item is neither review-ready nor parked/Done, when the
  projection is derived, then the item remains in the active inventory and no
  terminal transition is emitted for it.
- AC-006 [US-003] [FR-006]: Given a live read that fails or returns incomplete
  data for an item, when the projection is derived, then an explicit
  unreadable/no-verdict event is emitted for that item and it is never silently
  dropped from an otherwise-empty active list.
- AC-007 [US-004] [FR-007]: Given the CLI projection command, when invoked with
  `--json` or the default text renderer, then the output is exactly two stable
  lines of text (material transitions; complete active inventory) or one JSON
  document carrying the same facts, and the drive-board/work-board skills in
  both roots consume this output rather than generating their own prose.

## Functional Requirements
- FR-001: The engine MUST derive a typed material transition event whenever a classified item state differs from its cursor's last-known state, carrying ref, previous/new state, reason/evidence, observed timestamp, and source freshness identity. (covers AC-001)
- FR-002: Re-deriving the projection over an unchanged live-fact read MUST be idempotent and emit zero duplicate transition events. (covers AC-002)
- FR-003: The active inventory MUST be rendered completely on every read — claimed, in-review (handoff or repair), newly dispatched, and merged-with-unverified-obligations items — independent of whether any transition occurred in that read. (covers AC-003)
- FR-004: Classification MUST be derived solely from live facts (board status, claim liveness, PR/review state, delivery obligations) so that externally created claims, PRs, and check-state changes appear without requiring the host to have dispatched them. (covers AC-004)
- FR-005: A worker process returning while its claim is live and the item is unresolved MUST NOT itself be classified as a terminal transition; the item stays active. (covers AC-005)
- FR-006: A failed or incomplete live read for an item MUST produce an explicit unreadable/no-verdict transition event rather than being omitted, and MUST NOT be able to make an otherwise non-empty active inventory render empty by silent omission. (covers AC-006)
- FR-007: The CLI MUST expose the projection as an authoritative JSON document and a stable two-line text rendering, and the drive-board/work-board skill guidance (both `.agents` and `.claude` roots) MUST forward this projection instead of deriving status prose independently. (covers AC-007)

## Ambiguities
- AMB-001: The exact set of GitHub reads used to classify "review handoff" vs
  "review/repair" reuses `Driver.parseReviewComments`'s existing marker/round
  vocabulary from `.github#2127` rather than inventing a second one.
- AMB-002: "Release/deployment/downstream adoption" (issue acceptance #2) is
  represented as the existing `Delivery.Obligation` receipt vocabulary; no new
  release-tracking surface is introduced.

## Public Or Tool-Facing Impact
- New typed Core module and CLI JSON schema (`fsgg.coord.driver-events/1` or
  equivalent) are expected, plus a durable on-disk cursor file format.
- Drive-board and work-board guidance changes must stay equivalent across the
  `.agents` and `.claude` skill roots (existing repo invariant).

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2135-driver-event-projection`.
