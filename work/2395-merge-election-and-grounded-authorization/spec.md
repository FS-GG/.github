---
schemaVersion: 1
workId: 2395-merge-election-and-grounded-authorization
title: Merge Election And Grounded Authorization
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Merge Election And Grounded Authorization Specification

Prose status: specified

## User Value

A worker running the documented `delivery` step obtains a merge authorization that is **grounded in a
server-assigned merge election**, so that the merge fence's check 4 — the only one of its six checks a
forger cannot satisfy by typing — is actually evaluated on real pull requests instead of never being
reached.

Today it is never reached. `scripts/check-claim-fence.py` requires six authorization fields
(`REQUIRED_AUTH_FIELDS`); `src/FS.GG.Coord.Cli/Client.fs` composes four; so every real pull request
stops at check 1. `.github/workflows/fsgg-claim-fence.yml` nevertheless tells operators that check 4
"is expected to fail on every real pull request today", which is a documented expectation about a
branch that is not being evaluated at all.

## Scope

- SB-001: `delivery` posts the `fsgg:merge-election` marker on the item and composes a six-field
  `fsgg:pr-authorization` marker naming it, in `src/FS.GG.Coord.Cli/Client.fs` and
  `src/FS.GG.Coord.Cli/DeliveryApplication.fs` with their signature files.
- SB-002: the operation key is composed by the already-landed `FS.GG.Coord.Operation.compose`
  (`src/FS.GG.Coord.Core/Operation.fsi`); the election's ordering is asked of the already-exported
  `Reads.lowestId` (`src/FS.GG.Coord.GitHub/Reads.fsi`).
- SB-003: the coupled cross-check in `tests/receiver-validate/run.sh` is corrected from an equality
  assertion to the subset assertion the receiver gate's own forward-compatibility rule states.
- SB-004: prose in `scripts/check-claim-generation.py` that asserts the production path writes only
  four fields is repaired.

## Non-Goals

- SB-005: the bidirectional producer-versus-gate agreement leg against `scripts/check-claim-fence.py`
  and `tests/claim-fence` belongs to `.github#2719`, which declares those paths; this work declares
  neither and does not write that leg.
- SB-006: arming the fence as a required status context is slice 8 (`.github#2723`).
- SB-007: the receiver-side gate's own `REQUIRED` field set in
  `.github/workflows/kit-materialize.yml` stays four fields. It runs on pull requests in seven
  receiver repositories and tolerates additional pairs by design; widening it would red every
  receiver PR whose marker predates this change.
- SB-008: no change to the claim CAS, to `Reads.winner`, or to any lock path.

## User Stories

- US-001 (P1): As a worker carrying an item, I run one `delivery <ref> --pr N` call and the pull
  request I am about to merge carries an authorization the merge fence can evaluate end to end,
  without my composing an operation key or an election by hand.
- US-002 (P1): As an operator reading a fence verdict, the check the workflow's own note discusses is
  a check that ran, so a finding on check 4 tells me something about the pull request rather than
  about a missing producer.
- US-003 (P2): As a reviewer of this change, I can see the fence executing check 4 and failing at that
  check's own boundary, rather than being asked to infer reachability from source.

## Acceptance Scenarios

- AC-001 [US-001] [FR-001]: Given a live claim on an item and an open pull request, when `delivery`
  runs, then the pull request body carries exactly one `fsgg:pr-authorization` marker bearing
  `v`, `item`, `gen`, `opkey`, `grant` and `head`.
- AC-002 [US-001] [FR-002]: Given the item carries no election for this operation key and pull
  request, when `delivery` runs, then a comment whose very first byte begins the
  `fsgg:merge-election` marker is appended to the item, recording `v`, `opkey`, `item`, `gen`,
  `receiver` and `op=merge`.
- AC-003 [US-001] [FR-003]: Given a second `delivery` call for the same item, generation and pull
  request, when it runs, then no second election is posted and the authorization names the same
  `grant` as before.
- AC-004 [US-001] [FR-003]: Given the item already carries two elections this delivery target owns,
  when `delivery` runs, then `grant` names the lower comment id of the two.
- AC-005 [US-001] [FR-004]: Given elections supplied out of comment-id order, when the grant is
  selected, then the selection is made by `Reads.lowestId` and agrees with the id order.
- AC-006 [US-001] [FR-005]: Given the item's comments cannot be read, or the election comment cannot
  be posted, or the operation key cannot be composed, when `delivery` runs, then no pull-request body
  PATCH is issued at all and the call reports the failure.
- AC-007 [US-002] [FR-006]: Given a pull request whose body carries the old four-field marker, when
  the required `claim-generation` context evaluates it, then it still passes, because that gate
  requires four fields and accepts additional pairs.
- AC-008 [US-001] [FR-006]: Given a pull request whose body carries the old four-field marker, when
  `delivery` next runs on it, then the marker is replaced in place by the six-field marker and
  exactly one marker remains.
- AC-009 [US-003] [FR-007]: Given a pull-request body composed by the production writer and an item
  carrying its election, when `scripts/check-claim-fence.py` classifies it, then all five substantive
  checks pass; and given the same body with the election absent, with a lower-id competing election,
  or with an election recording a different receiver, then the gate returns a check 4 diagnosis.
- AC-010 [US-003] [FR-008]: Given the producer emits two fields the receiver gate does not require,
  when `tests/receiver-validate/run.sh` runs, then its producer-versus-gate leg passes; and when the
  producer is mutated to drop a field the gate does require, then that leg fails.

## Functional Requirements

- FR-001: The `fsgg:pr-authorization` marker `delivery` writes MUST carry `v`, `item`, `gen`, `opkey`, `grant` and `head`, and exactly one such marker MUST remain in the pull-request body. (covers AC-001)
- FR-002: `delivery` MUST post the `fsgg:merge-election` marker on the item as an append-only comment whose leading bytes are the marker itself, recording `v=1`, the operation key, the item, the claim generation, the receiver and `op=merge`. (covers AC-002)
- FR-003: Election posting MUST be idempotent for a delivery target: `delivery` MUST reuse an existing election it owns for this operation key and pull request rather than posting a second, and MUST name the lowest-id such election as `grant`. (covers AC-003, AC-004)
- FR-004: The lowest-id selection MUST be asked of `Reads.lowestId` rather than re-implemented in the CLI layer. (covers AC-005)
- FR-005: A grounding that cannot be established MUST refuse rather than degrade: no authorization marker is written when the operation key cannot be composed, the item's comments cannot be read, or the election cannot be posted. (covers AC-006)
- FR-006: The change MUST NOT alter the verdict of any required status context for any pull request, and an existing four-field marker MUST be upgraded in place by the next `delivery` call rather than requiring a cutover or a rebinding campaign. (covers AC-007, AC-008)
- FR-007: The change MUST ship executed evidence that `scripts/check-claim-fence.py` reaches check 4 on a body composed by the production writer, and that check 4 can fail at its own boundary — the lowest-election comparison — not merely inside its branch. (covers AC-009)
- FR-008: The producer-versus-gate cross-check in `tests/receiver-validate/run.sh` MUST state the contract the receiver gate actually has — its required fields are a subset of what the producer writes — and MUST ship with evidence that the corrected assertion still fails when the producer drops a required field. (covers AC-010)

## Ambiguities

- AMB-001 open: who posts the election and when — one `delivery` call or two — and what the design
  owes when the second write fails after the first succeeded.
- AMB-002 open: how `opkey` and `grant` are derived, and what makes them verifiable rather than
  decorative.
- AMB-003 open: migration — every open pull request today carries a four-field marker; whether the
  fence accepts both shapes, cuts over, or rebinds must be stated rather than discovered at merge.
- AMB-004 open: what proves check 4 is now reachable, given that a six-field marker with no executed
  gate run would move the problem rather than close it.

## Public Or Tool-Facing Impact

- The `fsgg:pr-authorization` marker's wire form gains two fields. Three readers parse that marker:
  `scripts/check-claim-generation.py` (required on `main`), `scripts/check-claim-fence.py`
  (observe-only), and the receiver-side validation job in `.github/workflows/kit-materialize.yml`
  (seven receiver repositories). All three tolerate additional pairs; see FR-006 and SB-007.
- The `fsgg:merge-election` marker gains its first producer. Its reader is
  `scripts/check-claim-fence.py`; the CAS reads `fsgg:claim` and nothing else, so a new prefix decides
  no lock.

## Lifecycle Notes

- Next lifecycle action: `fsgg-sdd clarify --work 2395-merge-election-and-grounded-authorization`.
