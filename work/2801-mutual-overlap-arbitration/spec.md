---
schemaVersion: 1
workId: 2801-mutual-overlap-arbitration
title: Automatic mutual-overlap arbitration
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Automatic mutual-overlap arbitration Specification

Prose status: specified

## User Value
Mutual live holders escape overlap deadlock through one automatic, auditable arbitration.

## Scope
- SB-001: Add typed, durable wait-for receipts bound to waiter/predecessor items, current claim generations, shared reservation tokens, and recording host.
- SB-002: Detect only the authoritative reciprocal two-cycle and automatically reuse one ADR-0051 coordination room.
- SB-003: Record a revisioned host precedence chain and apply it through recoverable, idempotent writes.
- SB-004: Narrow only the loser's shared reservations without releasing its claim, then gate resume on winner landing, fetch/rebase, re-overlap, explicit re-widen, and exact-head review when required.
- SB-005: Document automatic arbitration before manual negotiation and fold occurrences onto `.github#2801` unless adjudication establishes another cause.
- SB-010: Make the Coordination board a single-authority orchestration domain: external repository orchestrators route blocking requests to the live board orchestrator, or acquire the next generation only after authoritative absence/expiry.

## Non-Goals
- SB-006: Do not replace comment-order claim election, introduce a second ownership store, or turn transient overlap into `Blocked by:` dependency.
- SB-007: Do not create a generic transaction framework, a room per observation, or a work-item child per deadlock occurrence.
- SB-008: Do not arbitrate one-way waits, non-overlapping pairs, stale claims, self-edges, durable dependencies, or unrelated rooms.

## User Stories
- US-001 (P1): As a coordination host, I can detect a mutual overlap deadlock from authoritative generation-bound receipts independent of comment order.
- US-002 (P1): As either live holder, I receive one auditable room and one current host precedence decision instead of contradictory prose sequencing.
- US-003 (P1): As the losing holder, I keep my claim while shared reservations are narrowed and can resume only after refreshing the landed winner's tree and re-establishing overlap/review authority.
- US-004 (P2): As an operator, I can retry after any writer transport failure without duplicating a room, precedence revision, back-reference, or path transition.
- US-005 (P1): As an external repository orchestrator needing a Coordination fix, I either route one highest-priority blocking request to the live board orchestrator or become that orchestrator when no live lease exists, never creating a competing lane.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given two current claims with intersecting reservations, when each posts a valid wait receipt naming the other's exact generation and the same shared tokens, then the detector returns one canonical two-cycle independent of receipt order.
- AC-002 [US-001] [FR-002]: Given a self-edge, stale generation, unreadable claim, non-overlap, one-way wait, durable dependency, or unrelated room, when detection runs, then it fails closed or reports no cycle and performs no write.
- AC-003 [US-002] [FR-003]: Given a detected cycle, when arbitration starts or retries, then exactly one ADR-0051 room exists and both items carry its back-reference.
- AC-004 [US-002] [FR-004]: Given no current precedence, when the host decides, then one revision names winner and loser; same-revision conflicts fail closed, and reversal requires a next revision referencing the prior digest and measured reason.
- AC-005 [US-003] [FR-005]: Given current precedence, when it is applied, then the loser's shared tokens are atomically narrowed without releasing either claim and the winner remains authorized to land.
- AC-006 [US-003] [FR-006]: Given the winner landed, when the loser resumes, then it must fetch/rebase, re-run overlap, explicitly re-widen, and obtain any required review for the new exact head before shared edits continue.
- AC-007 [US-004] [FR-007]: Given a transport failure at any room/back-reference/precedence/path write boundary, when the writer retries from live state, then it converges to the same single room, receipt revision, claim-preserving narrow, or resume state.
- AC-008 [US-004] [FR-008]: Given the checked-in pure and production-route tests, when each detector and arbitration predicate is independently inverted, then a bounded test fails and returns green after restoration.
- AC-009 [US-005] [FR-010]: Given live board-orchestrator generation A, when external orchestrator B needs a Coordination fix, then B durably routes one generation-bound idempotent request to A and is refused authority to start a competing lane.
- AC-010 [US-005] [FR-010]: Given A has external blocking requests, when A chooses its next board work, then those requests are promoted ahead of ordinary board work while existing in-flight safety is preserved.
- AC-011 [US-005] [FR-011]: Given no live lease, when B invokes the production route, then B acquires the next immutable generation and executes the standard board protocol; a merely stale generation cannot authorize a request.
- AC-012 [US-005] [FR-011]: Given B1 and B2 race for the same generation, then the authoritative comment order elects exactly one, the loser removes only its own candidate, and unreadable/conflicting state authorizes neither.
- AC-013 [US-005] [FR-008]: Pure, writer, and compiled production-route tests and mutations independently invert active-A refusal, A priority promotion, no-A takeover, stale-A generation, and two-B acquisition race predicates.

## Functional Requirements
- FR-001: The system MUST parse and validate typed durable wait receipts containing waiter item/generation, predecessor item/generation, canonical shared reservation tokens, and host authority; it MUST reject self-edges and any missing, stale, conflicting, non-overlapping, or unreadable authority. (covers AC-001, AC-002)
- FR-002: The pure detector MUST derive exactly reciprocal A-waits-B plus B-waits-A from the authoritative current receipt set independent of comment order, while one-way waits, cleared overlaps, durable dependencies, and unrelated rooms remain negative controls. (covers AC-001, AC-002)
- FR-003: A detected cycle MUST freeze shared-token edits and idempotently create or reuse exactly one ADR-0051 room with back-references on both participants. (covers AC-003)
- FR-004: Arbitration MUST require one current host-authored precedence receipt naming distinct winner and loser; same-revision conflicts fail closed, and revision N+1 MUST reference revision N's digest and state a measured reversal reason. (covers AC-004)
- FR-005: Applying precedence MUST narrow the loser's shared reservations without releasing its claim, retain the winner's claim and reservations, and expose an observed post-state rather than treating write completion as authority. (covers AC-005)
- FR-006: Resuming the loser MUST require observed winner landing, fetch/rebase onto the current base, a fresh overlap result, explicit re-widen, and any exact-head review required by the changed tree. (covers AC-006)
- FR-007: The production writer MUST recover from ambiguous transport failure at every mutation boundary by re-reading live state and MUST make retries idempotent without duplicate rooms, back-references, precedence receipts, or path mutations. (covers AC-003, AC-004, AC-005, AC-006, AC-007)
- FR-008: Pure and compiled production-route tests MUST reproduce the `.github#2772`/`.github#2797` cycle, assert one room/current precedence, preserve the narrowed losing claim, cover winner-land/loser-resume, inject every writer boundary, and record observed-red inversions for every detector/arbitration predicate. (covers AC-001, AC-002, AC-003, AC-004, AC-005, AC-006, AC-007, AC-008)
- FR-009: Policy documentation MUST put automatic detection/arbitration before manual negotiation and state that occurrences fold onto `.github#2801` unless a separate root cause is adjudicated. (covers AC-003, AC-008)
- FR-010: A complete authoritative lease census MUST route every external Coordination need to the sole live board-orchestrator generation, persist the blocking request idempotently against that generation, refuse a competing lane, and make the current orchestrator surface external blocks before ordinary board work. (covers AC-009, AC-010)
- FR-011: Only authoritative lease absence/expiry MAY enable takeover; takeover MUST use the next immutable generation and a lowest-comment-id CAS, while stale requests, conflicting/live duplicate generations, unreadable state, and losing contenders fail closed without deleting the winner. (covers AC-011, AC-012, AC-013)

## Ambiguities
- AMB-001: Which existing public model should own wait/arbitration types without expanding the issue's declared paths into a generic Core policy module?
- AMB-002: Which stable room key makes create-after-response-loss idempotent through the current ADR-0051 writer?
- AMB-003: How does the host express freeze, precedence application, and loser resume as closed outcomes without inventing a second claim authority?

## Public Or Tool-Facing Impact
- Adds a versioned coordination receipt contract and host arbitration behavior to the CLI/GitHub writer surface.
- Adds versioned single-board-orchestrator lease/request contracts and an acquisition/routing production route.
- Changes overlap policy from manual-only guidance to an automatic-first route with fail-closed diagnostics.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2801-mutual-overlap-arbitration`.
