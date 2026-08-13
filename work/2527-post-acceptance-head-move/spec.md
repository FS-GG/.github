---
schemaVersion: 1
workId: 2527-post-acceptance-head-move
title: Post Acceptance Head Move
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Post Acceptance Head Move Specification

Prose status: specified

## User Value
A worker whose PR head moves AFTER its review chain completed and was host-accepted can post the one
honest response — a full fresh review of the tree that will actually merge — and reach a well-formed
chain, instead of the engine parking on `malformedEvidence` ("the initial review marker is carried by 2
comments") which describes the symptom, names no remedy, and costs a PR container, a re-posted
obligations declaration and a re-issued `delivery` authorization to work around.

The recovery must not weaken what the one-initial-marker rule buys: a stranger may still never silently
continue another critic's chain, and no prior critic's durable evidence is ever edited, quoted-inert, or
deleted by the recovery.

## Scope
- SB-001: A **chain-retirement** rule in `Review.fs`: when a host-acceptance marker names an initial
  review (`initial-review:`) and its `accepted-head:` differs from the binding's current head SHA, that
  chain — its initial marker, its confirmations, and the acceptance itself — is RETIRED and excluded
  from the comment set the protocol classifies. Every remaining state classification then runs
  unchanged over the live remainder.
- SB-002: The chain attribution the rule needs, exposed off `Driver`'s EXISTING marker classification
  and field grammar (`classifyMarkers`, `field`) rather than a second parser (`.github#2175`
  acceptance 11): the initial marker comments with their URLs, each confirmation's `initial-review:`
  back-reference, and each acceptance's `accepted-head:`/`initial-review:` pair.
- SB-003: A distinguishable refusal. When more than one initial marker survives retirement, the
  refusal states the retirement rule and why it did not apply to this PR, instead of the bare count.
- SB-004: `Review.Verdict` carries the retirement as an observable fact, and `ReviewApplication.fs`
  serializes it on the `review --json` wire, so a reader can see WHY a PR that visibly carries two
  initial markers is being classified against the later one.
- SB-005: `independent-review.md` (both kit-mirrored copies) states the post-acceptance head-move case,
  the retirement rule, what a fresh chain's critic must do, and — explicitly — that the retired chain
  is never rewritten and stays readable in place.
- SB-006: One executable fixture, wired to a workflow, driving the COMPILED engine over
  `review --snapshot`, covering the recovery, the controlled counterpart, and a gate inversion.

## Non-Goals
- SB-101: Does NOT add a marker kind. `Protocol.reviewPolicy`'s vocabulary and every generated
  projection region over it are untouched, so no `generate-projections` region changes.
- SB-102: Does NOT introduce an out-of-band grant (a `SupersedingChainReceipt` analogous to
  `RepairPhaseReceipt`/`CriticSuccessionReceipt`). The fact "this chain was accepted at a head that is
  no longer current" is written in the acceptance marker's own required fields; a grant would convert an
  observable fact into an assertable one, which is precisely the laundering surface AC5 guards.
- SB-103: Does NOT change the repair phase, critic succession, the round ceilings, the same-critic rule,
  or `Driver.parseReviewCommentsCore`'s own validation of whatever chain it is handed.
- SB-104: Does NOT change how a head moves, nor advise moving one; the change is about representing the
  consequence honestly, not about permitting it.
- SB-105: Does NOT relax `landable`, the host-acceptance marker requirement, or the `delivery`
  authorization step. A retired chain's acceptance grants the new head nothing.
- SB-106: Does not implement Governance policy enforcement.

## User Stories
- US-001 (P1): As a worker whose accepted PR conflicted and had to take a merge before it could land, I
  can have a fresh critic post a genuinely new initial review of the current head and have
  `scripts/fsgg-coord review` classify the chain that actually describes the candidate.
- US-002 (P1): As the protocol's guarantee that a stranger cannot continue another critic's chain, I am
  preserved: a second initial marker is admitted ONLY where durable evidence shows a completed
  acceptance whose head has since moved, and never on an assertion.
- US-003 (P1): As a host reading `review --json` on a PR carrying two initial markers, I can see which
  chain binds and which was retired, and why, rather than inferring it.
- US-004 (P2): As a reader of the retired chain, I find it intact — unedited, unquoted, still carrying
  the critic's original verdict and the acceptance that was granted for the head it reviewed.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given a PR whose comments carry initial marker A (critic `c1`,
  `reviewed-head: H1`, `verdict: pass`), a host-acceptance marker naming A with `accepted-head: H1`, and
  a later initial marker B (critic `c2`, `reviewed-head: H2`, `verdict: pass`), when `Review.inspect`
  runs with `Binding.HeadSha = H2`, then chain A is retired and the verdict is a well-formed non-
  malformed state classified from B alone.
- AC-002 [US-002] [FR-002]: Given the same PR but with NO host-acceptance marker, when `Review.inspect`
  runs with `Binding.HeadSha = H2`, then the verdict is `MalformedEvidence`/`Park` — the two competing
  initial markers still fail closed.
- AC-003 [US-002] [FR-002]: Given a host-acceptance marker naming A whose `accepted-head` EQUALS the
  binding's current head, when `Review.inspect` runs, then nothing is retired and the verdict is
  `MalformedEvidence`/`Park`. An acceptance that still binds cannot retire anything.
- AC-004 [US-002] [FR-002]: Given a host-acceptance marker whose `initial-review:` matches NO initial
  marker on the PR, when `Review.inspect` runs against two initial markers, then nothing is retired and
  the verdict is `MalformedEvidence`/`Park`.
- AC-005 [US-003] [FR-003]: Given AC-002's or AC-003's inputs, when the refusal is rendered, then its
  reason states the retirement rule and why it did not apply, and is not the bare
  "carried by N comments" count alone.
- AC-006 [US-003] [FR-004]: Given AC-001's inputs, when `review --snapshot ... --json` runs against the
  compiled engine, then the JSON carries the retired chain's initial-review reference and accepted head.
- AC-007 [US-001] [FR-005]: Given AC-001's inputs, when the verdict is produced, then `acceptedReceipt`
  is null — the retired chain's acceptance never binds the new head.
- AC-008 [US-004] [FR-006]: Given AC-001's inputs, when retirement is applied, then the input comment
  list is not mutated: retirement is a read-time exclusion, and the same comments re-inspected at head
  H1 classify chain A exactly as they did before this change.
- AC-009 [FR-007]: Given the fixture from SB-006, when the retirement rule is deleted (the live-comment
  filter replaced by the identity function), then the fixture reds on the recovery leg.
- AC-010 [US-002] [FR-002]: Given TWO acceptance markers both naming retired chains and one live chain,
  when `Review.inspect` runs, then the surviving acceptance count is what is judged — retirement removes
  a retired chain's acceptance along with it, so a retired acceptance cannot itself trip the
  "host-acceptance marker is carried by N comments" refusal.

## Functional Requirements
- FR-001: `Review.inspect`/`advance` MUST classify the protocol state from the LIVE comment set — all supplied comments minus every retired chain's markers — where a chain is retired if and only if a host-acceptance marker names its initial review (`initial-review:`) and carries an `accepted-head:` different from `Binding.HeadSha`. (covers AC-001)
- FR-002: Retirement MUST be admitted only on that exact durable evidence: no acceptance marker, an acceptance still bound to the current head, or an acceptance naming no initial marker present on the PR MUST retire nothing, and the pre-existing competing-initial-marker refusal MUST stand unchanged. (covers AC-002, AC-003, AC-004, AC-010)
- FR-003: The competing-initial-marker refusal MUST name the retirement rule and which of its conditions was not met, rather than the bare marker count alone. (covers AC-005)
- FR-004: `Review.Verdict` MUST expose the retired chains, and `ReviewApplication.fs` MUST serialize them on the `review --json` wire; the field MUST be empty for every chain that retires nothing. (covers AC-006)
- FR-005: A retired chain's acceptance MUST never produce an `AcceptedReceipt` for the new head; `acceptedReceipt` MUST stay null until a fresh chain is itself accepted at the current head. (covers AC-007)
- FR-006: Retirement MUST be a pure read-time partition of the supplied comment list — never an edit, reorder, or drop of evidence at the source — so the same comments inspected at the retired chain's own head classify exactly as they did before this change. (covers AC-008)
- FR-007: The mechanism MUST ship with gate-inversion evidence: an executable, workflow-wired fixture that reds when the retirement rule is removed. (covers AC-009)

## Ambiguities
- AMB-001: Which of the three viable shapes (an out-of-band superseding-chain marker, an explicit chain
  generation, or a documented close-and-reopen procedure) the mechanism takes. Resolved in
  `clarifications.md` DEC-001.
- AMB-002: What "reports this state distinguishably" (issue AC3) requires once the state is no longer
  malformed at all. Resolved in `clarifications.md` DEC-003.

## Public Or Tool-Facing Impact
- `independent-review.md` is coordination-kit content: a change to it is a kit-content change and owes a
  coherent-set version bump and a published kit before merge (`kit-published-coherence`).
- `review --json` gains an additive field; absent/empty for every chain that retires nothing, so every
  existing consumer is unaffected.
- This specification is an SDD lifecycle artifact and command-report contract input.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2527-post-acceptance-head-move`.
