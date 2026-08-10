---
schemaVersion: 1
workId: 2175-review-repair-protocol
title: "coord review: make independent review and repair a resumable typed protocol"
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# coord review: make independent review and repair a resumable typed protocol Specification

Prose status: specified

## User Value
Workers and hosts receive one typed, resumable next action for every alternating
critic/implementer transition inside `ReviewActive` — dispatch critic, resume
implementer, resume the same critic for confirmation, await checks, request host
acceptance, enter the one permitted fresh repair phase, accept, or park for human
action — instead of manually correlating PR comments, round counts, critic
identity, and repair-phase provenance by hand.

## Scope
- SB-001: A closed review-protocol state model covering at least: awaiting initial
  review, changes requiring repair, awaiting implementer repair, awaiting
  same-critic confirmation, passed awaiting checks, awaiting host acceptance,
  ordinary exhaustion, repair-phase setup, repair-phase active review, accepted,
  and terminal human park.
- SB-002: Fail-closed inspection of live PR comments, claim/worker facts, PR/head/check
  state, and board state into exactly one typed next action or a fail-closed
  no-verdict; unreadable or contradictory facts never become an empty/no-review state.
- SB-003: Critic-independence and same-critic-continuity guards: a confirmation from
  another critic, a confirmation before repair evidence, or an implementer acting as
  critic fails closed.
- SB-004: Ordinary-exhaustion-to-one-fresh-repair-phase enforcement, bound to the
  exhausted PR/escalation marker, the new claim, branch/PR, implementer, fresh
  critic, and candidate head.
- SB-005: Idempotent, freshness-token-bound mutating transitions with a deterministic
  action key so restart/retry converges on the same durable marker/receipt.
- SB-006: One accepted-current-head receipt compatible with the FS-GG/.github#2131
  `Delivery` lifecycle boundary, consumed without `Delivery` learning the inner
  review/repair state graph.
- SB-007: Reuse of the FS-GG/.github#2127 review-chain validator and the
  FS-GG/.github#2136 generated marker/round policy as authorities; no second marker
  parser and no hand-maintained round ceiling.
- SB-008: Typed review/repair transitions exposed for the FS-GG/.github#2135 event
  projection, and pnext-item/drive-board/work-board process-skill guidance updated
  to consume typed next actions while retaining qualitative review guidance.

## Non-Goals
- SB-101: Does not replace FS-GG/.github#2131's claim-to-Done lifecycle, guarded
  landing, or post-merge obligation tracking — this specification produces the
  typed review status, next action, and accepted-current-head receipt that
  lifecycle consumes.
- SB-102: Does not re-implement or duplicate the `Driver.parseReviewComments`
  marker-block/quoting parser; the review-protocol layer classifies structural
  facts the existing parser already computes rather than re-scanning comment
  bodies for marker text.
- SB-103: Does not implement Governance policy enforcement.

## User Stories
- US-001 (P1): As a worker or host mid-review, I can ask one typed inspection for
  the exact current review-protocol state and the single next action, instead of
  reading PR comments and round counts by hand.
- US-002 (P1): As a critic or the host, I can trust that a same-critic confirmation
  requirement, a repair-phase round ceiling, and the one-fresh-repair-phase rule are
  enforced mechanically, so a stale or dishonest confirmation cannot advance the chain.
- US-003 (P2): As the `Delivery` lifecycle boundary, I can consume one accepted
  receipt for the review protocol without learning its internal states.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given a PR with no review comments yet, when inspect
  runs, then it returns `AwaitingInitialReview` and the `DispatchCritic` action.
- AC-002 [US-001] [FR-002]: Given PR comments, claim facts, check state, and board
  state are all readable, when inspect runs, then it returns exactly one typed
  state and next action, or a fail-closed no-verdict naming the unreadable or
  contradictory fact; a parse error never collapses to an absent-review state.
- AC-003 [US-001] [FR-003]: Given the review protocol's current state, when inspect
  selects a next action, then that action is one of dispatch critic, resume
  implementer, resume the same critic for confirmation, await checks, request host
  acceptance, enter the fresh repair phase, accept, or park for human action.
- AC-004 [US-001] [FR-004]: Given a state and action, when they are produced, then
  both are bound to item ref, PR, exact head SHA, phase, round, implementer
  identity, critic identity, initial review URL, preceding review URL, and claim
  generation; a new commit invalidates prior pass, checks, and host-acceptance
  evidence for the old head.
- AC-005 [US-002] [FR-005]: Given a confirmation posted by a critic other than the
  initial reviewer, an implementer identity equal to the critic identity, or a
  confirmation whose reviewed head did not change since the prior changes-required
  verdict, when inspect runs, then it fails closed rather than advancing the chain.
- AC-006 [US-002] [FR-006]: Given the ordinary chain is exhausted (round ceiling
  reached without acceptance), when inspect runs, then it returns
  `OrdinaryExhaustion` and, once a repair route is available, permits exactly one
  fresh repair phase whose receipt binds the exhausted PR/escalation marker to the
  new claim, branch/PR, implementer, fresh critic, and candidate head; it never
  silently resets the ordinary chain or grants a second repair phase.
- AC-007 [US-002] [FR-007]: Given no repair route is available or the repair-phase
  round ceiling is also exhausted, when inspect runs, then it returns
  `TerminalHumanPark` with complete provenance, and that state is never
  interpretable as passing acceptance.
- AC-008 [US-001] [FR-008]: Given malformed, absent, changes-required, exhausted, or
  repair-phase evidence, when inspect runs, then the parser's own errors are
  carried into the typed status rather than discarded through `Result.toOption` or
  an equivalent lossy conversion.
- AC-009 [US-001] [FR-009]: Given the same inspected facts are advanced twice (a
  retry after restart), when the mutating transition is applied, then it converges
  on the same durable marker/receipt; a stale freshness token or action key never
  dispatches a duplicate critic, mints a second repair phase, or accepts the wrong
  head.
- AC-010 [US-003] [FR-010]: Given the review protocol reaches `Accepted` for a head
  SHA, when the accepted-current-head receipt is produced, then `Delivery.inspect`
  can consume it (as `Driver.ReviewChain` facts) without any change to `Delivery`'s
  own `Stage`/`Action` union.
- AC-011 [US-002] [FR-011]: Given the FS-GG/.github#2127 validator and the
  FS-GG/.github#2136 generated marker/round policy, when the review protocol
  classifies live facts, then it calls into those authorities rather than
  re-implementing marker parsing or hand-maintaining a round ceiling.
- AC-012 [US-003] [FR-012]: Given the FS-GG/.github#2135 event projection and the
  pnext-item/drive-board/work-board process skills, when they need the current
  review/repair transition, then a typed surface (CLI JSON contract) exposes it,
  and the skill guidance references it while retaining qualitative review guidance.
- AC-013 [US-001] [FR-013]: Given the test matrix (clean first-pass acceptance,
  multiple repair rounds, changed head, malformed/duplicate marker, wrong critic,
  missing predecessor, ordinary exhaustion into one repair phase, restart during
  repair, duplicate advance, unavailable repair route, repair-phase exhaustion,
  final accepted receipt consumed by Delivery), when the suite runs, then every
  case is covered and green.

## Functional Requirements
- FR-001: The engine MUST expose a closed review-protocol state model covering at least awaiting initial review, changes requiring repair, awaiting implementer repair, awaiting same-critic confirmation, passed awaiting checks, awaiting host acceptance, ordinary exhaustion, repair-phase setup, repair-phase active review, accepted, and terminal human park. (Stories: US-001; Acceptance: AC-001)
- FR-002: The engine MUST inspect live PR comments, claim/worker facts, PR/head/check state, and board state into exactly one typed next action or a fail-closed no-verdict; unreadable or contradictory facts MUST NOT become an empty/no-review state. (Stories: US-001; Acceptance: AC-002)
- FR-003: Typed actions MUST distinguish dispatch critic, resume implementer, resume the same critic for confirmation, await checks, request host acceptance, enter the fresh repair phase, accept, and park for human action. (Stories: US-001; Acceptance: AC-003)
- FR-004: Every state and action MUST bind item ref, PR, exact head SHA, phase, round, implementer identity, critic identity, initial review URL, preceding review URL, and relevant claim generation; a new commit MUST invalidate pass, checks, and host-acceptance evidence for the prior head. (Stories: US-001; Acceptance: AC-004)
- FR-005: The engine MUST preserve critic independence and same-critic continuity mechanically: a confirmation from another critic, a confirmation before repair evidence, or an implementer acting as critic MUST fail closed. (Stories: US-002; Acceptance: AC-005)
- FR-006: Ordinary exhaustion MUST automatically permit exactly one fresh repair phase, whose receipt binds the exhausted PR/escalation marker to the new claim, branch/PR, implementer, fresh critic, and candidate head; it MUST NOT silently reset the ordinary chain or enter a second repair phase. (Stories: US-002; Acceptance: AC-006)
- FR-007: Unavailable repair routing or exhausted repair-phase rounds MUST emit a typed terminal human-park action with complete provenance; neither state may be interpreted as passing acceptance. (Stories: US-002; Acceptance: AC-007)
- FR-008: Partial and in-progress review evidence MUST be represented as typed status; parser errors for malformed, absent, changes-required, exhausted, and repair-phase states MUST NOT be discarded through `Result.toOption` or an equivalent lossy conversion. (Stories: US-001; Acceptance: AC-008)
- FR-009: Mutating transitions MUST consume a freshness token and a deterministic action key; retry after restart MUST converge on the same durable marker/receipt, and stale replay MUST NOT dispatch a duplicate critic, mint another phase, or accept the wrong head. (Stories: US-001; Acceptance: AC-009)
- FR-010: The engine MUST produce one accepted-current-head receipt compatible with the FS-GG/.github#2131 `Delivery` boundary; `Delivery` MUST consume it without learning the inner review/repair state graph. (Stories: US-003; Acceptance: AC-010)
- FR-011: The engine MUST reuse the FS-GG/.github#2127 validator and the FS-GG/.github#2136 generated marker/round policy as authorities, migrating them behind the new protocol where necessary, rather than creating a second marker parser or a hand-maintained ceiling. (Stories: US-002; Acceptance: AC-011)
- FR-012: The engine MUST expose review/repair transitions for the FS-GG/.github#2135 event projection, and the pnext-item/drive-board/work-board process skills MUST be updated to consume typed next actions while retaining qualitative review guidance. (Stories: US-003; Acceptance: AC-012)
- FR-013: The test suite MUST cover clean first-pass acceptance, multiple repair rounds, changed head, malformed/duplicate marker, wrong critic, missing predecessor, ordinary exhaustion into one repair phase, restart during repair, duplicate advance, unavailable repair route, repair-phase exhaustion, and the final accepted receipt consumed by Delivery. (Stories: US-001; Acceptance: AC-013)

## Ambiguities
- AMB-001: The exact typed shape of "resume implementer" vs. "changes requiring
  repair" as distinct closed-model cases when the reviewed-head field on a
  changes-required verdict is itself malformed or absent.
- AMB-002: Whether "repair route availability" (FR-006/FR-007) is itself inspected
  by this engine or supplied as an external fact by the scheduler/host.

## Public Or Tool-Facing Impact
- Adds a new `FS.GG.Coord.Core.Review` module and CLI `review` command (JSON
  contract) to the coordination engine's public/tool-facing surface.
- Updates `pnext-item`, `drive-board`, and `work-board` skill guidance (both
  `.agents/skills` and `.claude/skills` copies) to reference the typed next-action
  surface.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2175-review-repair-protocol`.
