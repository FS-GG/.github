---
schemaVersion: 1
workId: 2662-critic-succession-ledger
title: Critic Succession Ledger
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Critic Succession Ledger Specification

Prose status: specified

## User Value

A host-granted successor critic can record its verdict in the structured review ledger under its own
minted identity, so a review chain whose critic despawned mid-round finishes instead of parking on
`human/action` or laundering the successor's verdict under the despawned critic's name.

`.github#2417` gave the engine a typed, host-granted critic succession at the DECISION layer:
`Review.advance` answers `EnterCriticSuccession` instead of the unsatisfiable `ResumeSameCritic`. It
never taught the LEDGER. `StructuredDecision.validateReviewLedger` has no succession branch, so the
successor can be dispatched, can review, and then cannot record a verdict in any honest shape —
`confirmation`, `escalation` and a second `initial` are all refused. Two independent live chains hit
this in one session (`FS-GG/.github#2645` / PR #2650 and `FS-GG/.github#2642` / PR #2655), and a third
(`FS-GG/.github#2581` / PR #2651) is parked behind them.

## Scope

- SB-001: The `fsgg.coord.review-decision/v2` record shape — one additive, optional field carrying the
  succession grant — together with its digest inputs and its JSON wire codec.
- SB-002: `StructuredDecision.validateReviewLedger`'s critic-continuity rule, widened by exactly one
  admission and no other.
- SB-003: The derived "current generation critic" fact that `Driver.reviewPhaseFacts` and
  `Driver.parseReviewComments` publish, so that a second succession and the downstream accepted
  receipt name the critic that is actually in force.
- SB-004: Fixture and unit coverage that drives the LEDGER, not only the decision path, with a
  mutation proving the new admission can be removed and observed red.
- SB-005: The prose that states the successor's record shape: both `independent-review.md` mirrors and
  `docs/coordination/structured-decisions.md`.

## Non-Goals

- SB-006: `Review.criticSuccessionValid` and the rest of the `.github#2417` decision layer are not
  changed. That guard is correct and binds a grant to the EXACT head, which is the property that stops
  a grant being replayed across a moved head; this work must not weaken or restate it.
- SB-007: A machine-readable, durable on-PR marker for the grant itself is not built here. Today the
  grant reaches the engine only through a hand-assembled `--snapshot` fact and exists on the pull
  request as prose plus a fenced JSON block. Recording the grant's URL in the review record is what
  this work owes; parsing a grant marker is a separate contract.
- SB-008: No change to the claim, delivery, landable, or board surfaces, and no `v3` review schema.
- SB-009: Do not implement later lifecycle commands or Governance enforcement in this specification.

## User Stories

- US-001 (P1): As a successor critic granted succession over a despawned critic, I can append my
  verdict to the structured review ledger under my own minted identity, so that the chain reaches host
  acceptance instead of parking.
- US-002 (P1): As the host reading a pull request's ledger, I can tell a record confirmed by a granted
  successor from one confirmed by the original critic, and see who granted it and where, without
  reconstructing the chain's history from other comments.
- US-003 (P1): As a reviewer of this protocol, I can rely on an identity change with no valid grant
  still being refused exactly as it is today, so that succession is an accountable exception and never
  a general weakening of critic continuity.
- US-004 (P2): As an operator of an engine built before this field existed, I get a closed failure on a
  record that uses it rather than a silent acceptance that ignores it, and every ledger already written
  keeps validating byte-for-byte.

## Acceptance Scenarios

- AC-001 [US-001] [FR-001]: Given a live generation whose initial record binds critic `A` and whose
  latest verdict is `changes-required`, when successor `B` appends a `confirmation` record bearing
  `critic: B` and a succession grant naming `A`, then `validateReviewLedger` returns `Ok` and
  `review record` posts it.
- AC-002 [US-001] [FR-002]: Given the same generation, when the successor appends an `escalation` or a
  `repair-phase` record under the same grant shape, then `validateReviewLedger` returns `Ok`, so the
  escalate-into-repair-phase route is open and not only `confirmation`.
- AC-003 [US-002] [FR-003]: Given a succession record on the wire, when any consumer decodes it, then
  the outgoing critic, the granting identity and the grant's URL are all readable from that record
  alone.
- AC-004 [US-003] [FR-004]: Given a `confirmation` record whose critic differs from the generation
  critic and which carries NO succession grant, when `validateReviewLedger` runs, then it returns
  `Error` carrying `every record in one review generation must bind the same critic`, unchanged.
- AC-005 [US-003] [FR-005]: Given a succession grant whose `originalCritic` is not the generation
  critic in force, or whose `grantedBy` or `grantUrl` is blank, or whose successor, outgoing critic or
  granter is a generic route identity (`fsgg-critic-<route>`), when `validateReviewLedger` runs, then
  it returns `Error` and the record is refused.
- AC-006 [US-003] [FR-006]: Given a succession grant carried by a record that does NOT change the
  generation's critic, or carried by an `initial` or `acceptance` record, when `validateReviewLedger`
  runs, then it returns `Error`; a grant is admissible only where a critic actually changes hands.
- AC-007 [US-004] [FR-007]: Given every review record already written to a pull request before this
  change, when `reviewDigest` recomputes it under the new code, then the digest is byte-identical to
  the one recorded, and the record still decodes.
- AC-008 [US-004] [FR-008]: Given a succession record, when an engine that predates the field
  recomputes its digest, then the digest does not match and the ledger is refused; the record is never
  silently accepted as if no grant were present.
- AC-009 [US-001] [FR-009]: Given a generation whose critic changed by grant from `A` to `B`, when the
  engine next reports the generation's critic, then it reports `B` — so a further grant must name `B`
  as the outgoing critic and the accepted receipt names the critic that actually passed.
- AC-010 [US-003] [FR-010]: Given the succession admission removed from the validator in a scratch
  copy of the tree, when the ledger legs of `tests/review-critic-succession-wire/run.sh` run against a
  rebuilt engine, then the accepting legs red and the refusing legs stay green.
- AC-011 [US-002] [FR-011]: Given an agent reading `independent-review.md` or
  `docs/coordination/structured-decisions.md`, when it needs the successor's record shape, then the
  concrete shape is stated there, and no sentence implies a grant survives a moved head.

## Functional Requirements

- FR-001: `StructuredDecision.validateReviewLedger` MUST accept a non-`initial` record whose `critic` differs from the generation critic in force when, and only when, that record carries a well-formed succession grant naming that generation critic as its outgoing critic, and MUST then rebind the generation critic to the record's own `critic`. (Stories: US-001; Acceptance: AC-001)
- FR-002: The admission MUST apply identically to `confirmation`, `escalation` and `repair-phase` records, so that a successor can escalate into the repair phase rather than only confirm. (Stories: US-001; Acceptance: AC-002)
- FR-003: The `fsgg.coord.review-decision/v2` record MUST carry the grant as one additive, optional object naming the outgoing critic, the granting identity, and the grant's URL, readable from the record alone. (Stories: US-002; Acceptance: AC-003)
- FR-004: An identity change carrying no succession grant MUST still be refused, with the existing message text unchanged. (Stories: US-003; Acceptance: AC-004)
- FR-005: A grant whose outgoing critic is not the generation critic in force, whose granting identity or grant URL is blank, or whose successor, outgoing critic or granter is a generic route identity MUST be refused. (Stories: US-003; Acceptance: AC-005)
- FR-006: A grant carried by a record that does not change the generation's critic, or carried by an `initial` or `acceptance` record, MUST be refused. (Stories: US-003; Acceptance: AC-006)
- FR-007: The grant MUST contribute to `reviewDigest` only when present, so that every already-written record's digest is unchanged and every already-written record still decodes. (Stories: US-004; Acceptance: AC-007)
- FR-008: A record carrying a grant MUST fail closed against an engine that predates the field, by digest mismatch rather than by silent acceptance. (Stories: US-004; Acceptance: AC-008)
- FR-009: After a succession, the engine's published generation-critic fact MUST name the successor, not the record that opened the generation. (Stories: US-001; Acceptance: AC-009)
- FR-010: `tests/review-critic-succession-wire/run.sh` MUST gain at least one leg that drives the ledger, not only the decision path, and a mutation leg that reds when the succession admission is removed. (Stories: US-003; Acceptance: AC-010)
- FR-011: Both `independent-review.md` mirrors and `docs/coordination/structured-decisions.md` MUST state the successor's record shape concretely, and MUST NOT state or imply that a grant survives a moved head. (Stories: US-002; Acceptance: AC-011)

## Ambiguities

- AMB-001: Which wire shape carries the grant — an additive object, an overload of an existing field,
  or a new `kind` value.
- AMB-002: Whether and how the grant participates in `reviewDigest`, given that every already-written
  digest must stay stable and a pre-field engine must not silently accept a succession record.
- AMB-003: Which record kinds may carry a grant, and whether a grant is admissible on a record that
  does not actually change the critic.
- AMB-004: What "the generation critic" denotes after a succession, for the next round's grant
  comparison and for the accepted receipt the host and `landable` read.
- AMB-005: Whether the grant's URL must be resolved or verified by the engine.
- AMB-006: How the ledger leg required by FR-010 is driven, given the fixture is deliberately
  hermetic — no board, no token, no network — while the live ledger write is a REST call.
- AMB-007: Whether `Review.criticSuccessionValid` needs any change to keep the two layers consistent.

## Public Or Tool-Facing Impact

- `fsgg.coord.review-decision/v2` gains one optional field. The schema identifier does not change: the
  field is additive, absent on every ordinary record, and digest-conditional, so all existing records
  remain valid without migration.
- `FS.GG.Coord.Core`'s public signature grows one type and one record field
  (`StructuredDecision.fsi`), which is a declared public-surface change under constitution III.
- `.claude/skills/pnext-item/references/independent-review.md` is kit-published skill source, so the
  change carries a `FS.GG.Kit` version bump and a kit publish/verify obligation.
- This specification is an SDD lifecycle artifact and command-report contract input.

## Lifecycle Notes

- Next lifecycle action: `fsgg-sdd clarify --work 2662-critic-succession-ledger`.
