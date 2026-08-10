---
schemaVersion: 1
workId: 2206-board-roster-closure
title: Board Roster Closure
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Board Roster Closure Specification

Prose status: specified

## User Value
A worker driving the Coordination board can trust that every repository holding a live (non-Done) board
row is either rostered in `registry/repos.yml` or explicitly, reviewably excused — so a user-owned repo
like `EHotwagner/rogue3` or `EHotwagner/S.I.R.` cannot sit on the board unrostered and undetected the way
`.github#2206` found it, and a driver never again discovers the gap by reading epics by hand.

## Scope
- SB-001: Add a third BOARD-closure direction (C) to `scripts/check-roster-closure.py`, alongside the
  existing registry closure (A) and org closure (B) directions.
- SB-002: Reuse the existing, already-falsifiable disposition vocabulary — a `registry/repos.yml` row
  (including `role: non-participant` with its mandatory `reason:`, `.github#2245`) or an
  `outside-fabric:` entry with its mandatory `reason:` — as the BOARD direction's opt-out. No new
  schema or field is introduced.
- SB-003: Wire direction C into the `roster-closure` job in `.github/workflows/coherence.yml`, alongside
  directions A and B: an offline fixture pass first, then a live read against the real board.
- SB-004: Extend `tests/roster-closure/run.sh` with fixtures proving direction C's violation, no-verdict,
  and closed-world cases, including the case direction B cannot reach (a user-owned repository).
- SB-005: Record the BOARD-closure direction and its disposition-reuse decision in
  `docs/adr/0019-org-repo-roster-registry-and-coordination-kit.md`.

## Non-Goals
- SB-006: Do not decide `EHotwagner/rogue3`'s registry disposition (rostered, opted out, or removed from
  the board). `.github#2206` acceptance 6 explicitly excludes that decision from this item's scope —
  only making the case visible and required is in scope.
- SB-007: Do not introduce a new opt-out schema or field beyond the existing `role: non-participant` and
  `outside-fabric:` mechanisms `.github#2245` already shipped and `EHotwagner/S.I.R.` already uses.
- SB-008: Do not change the semantics, exit codes, or messages of registry closure (A) or org closure
  (B) — this item is additive.
- SB-009: Do not implement Governance enforcement or any later SDD lifecycle command surface.

## User Stories
- US-001 (P1): As a worker driving the Coordination board, I want `check-roster-closure.py` to assert
  BOARD closure, so that a repository holding a live board row cannot go unrostered and undetected.
- US-002 (P1): As a maintainer, I want the BOARD direction's disposition vocabulary to be expressive
  enough to record *why* a board-present repo is not a fabric participant, and falsifiable the way
  `outside-fabric:` already is, so a consumer-acceptance repo like `S.I.R.` is representable as
  deliberate rather than merely tolerated, and a stale claim cannot quietly become a lie.
- US-003 (P1): As a maintainer, I want the BOARD direction to read the board's own repo set rather than
  enumerate the GitHub organization, so a user-owned repository is covered rather than structurally
  invisible the way direction B's org enumeration is.
- US-004 (P1): As a CI operator, I want "could not read the board" to fail closed and be distinguishable
  from "read the board, and it is closed" and from a real violation, so a transient outage is never
  reported as a clean board and never sends a human to fix a roster that was fine.
- US-005 (P1): As a maintainer, I want `EHotwagner/rogue3` and `EHotwagner/S.I.R.` to each be
  dispositioned explicitly under the new mechanism once it exists, so the recurrence this item was filed
  to catch cannot happen silently again — without this item deciding `rogue3`'s case.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given a board snapshot with a non-Done row naming a repository absent from
  both `registry/repos.yml`'s `repos:` and its `outside-fabric:` list, when
  `check-roster-closure.py` runs direction C, then it exits 1 and names that repository as a BOARD
  closure violation.
- AC-002 [US-002] [FR-002]: Given a board snapshot with a non-Done row naming a repository that carries
  either a `registry/repos.yml` row (any `role:`, including `non-participant` with its `reason:`) or an
  `outside-fabric:` entry with its `reason:`, when direction C runs, then that row is treated as closed
  and contributes no violation — the existing falsifiable vocabulary is sufficient disposition.
- AC-003 [US-003] [FR-003]: Given a board snapshot with a non-Done row naming a repository owned by a
  user rather than the `FS-GG` organization, when direction C runs, then that row is graded identically
  to an org-owned row — direction C never calls or depends on an org repository listing.
- AC-004 [US-004] [FR-004]: Given the board cannot be read (unreachable, malformed, or an empty item
  set), when direction C runs, then it exits 3 with a message that is textually distinguishable from
  both the exit-0 closed-world message and every exit-1 violation message — never exit 0.
- AC-005 [US-001] [FR-005]: Given `.github/workflows/coherence.yml`'s `roster-closure` job, when a PR or
  push runs it, then direction C's offline fixture (`tests/roster-closure/run.sh`) and its live read
  against the real board both execute in that job, alongside directions A and B.
- AC-006 [US-005] [FR-006]: Given the implemented gate run against the live board, when its output is
  read, then `EHotwagner/rogue3` and `EHotwagner/S.I.R.` are each named with an explicit, legible
  disposition (rostered/opted-out, or excluded because its only board row is Done) — with `rogue3`'s
  registry disposition left undecided by this item.

## Functional Requirements
- FR-001: `check-roster-closure.py` gains a BOARD closure direction (C) that reports exit 1 for every non-Done board row naming a repository absent from both `registry/repos.yml` and `outside-fabric:`, named with the repository and an example row. (covers AC-001)
- FR-002: A schedulable board row's repository is closed by either an existing `repos.yml` row (any `role:`, including `non-participant` + mandatory `reason:`, `.github#2245`) or an existing `outside-fabric:` entry; no new opt-out field, list, or schema is added. (covers AC-002)
- FR-003: Direction C resolves each board row's repository from the board item's own `owner` and `repo` fields and never calls, requires, or depends on `GET /orgs/{org}/repos` or any other org-membership enumeration. (covers AC-003)
- FR-004: A board that cannot be read — the read fails, returns unparseable data, or returns zero items — is reported as a no-verdict (exit 3) with a message distinct from the exit-0 closed-world message and from every exit-1 violation message. (covers AC-004)
- FR-005: `.github/workflows/coherence.yml`'s `roster-closure` job runs direction C's offline fixture (`tests/roster-closure/run.sh`) unconditionally and its live board read as an explicitly named step, with the same exit-3-is-a-warning / exit-1-is-a-failure treatment directions A and B already receive there. (covers AC-005)
- FR-006: Run against the live Coordination board, direction C reports `EHotwagner/rogue3` and `EHotwagner/S.I.R.` each with a legible, explicit disposition (rostered, opted out with a reason, or outside the graded population because every board row it holds is `Done`), without this item authoring a registry change for `rogue3`. (covers AC-006)

## Ambiguities
No material ambiguities recorded. AC-2's design question (what shape the opt-out takes) and AC-4's
fail-closed exit-code split are resolved directly in FR-002 and FR-004 above rather than deferred.

## Public Or Tool-Facing Impact
- `scripts/check-roster-closure.py` is a required CI gate (`.github/workflows/coherence.yml`) that every
  rostered repository's fabric participation and the coherence workflow itself depend on; its verdict
  and exit-code vocabulary is a public contract per the delivery-route rationale already recorded on
  `.github#2206`. This specification adds a new direction to that contract without altering the existing
  one (SB-008).
- `docs/adr/0019-org-repo-roster-registry-and-coordination-kit.md` is the architecture-of-record for the
  roster; SB-005 amends it, which is itself a tool-facing/documentation change other readers rely on.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2206-board-roster-closure`.
