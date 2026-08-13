---
schemaVersion: 1
workId: 2549-review-state-vocabulary
title: Review State Vocabulary
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Review State Vocabulary Specification

Prose status: specified

## User Value

`scripts/fsgg-coord review` reports exactly one closed state and one typed next action, and a host acts
on that word. Today two of those words are wrong in ways that point in opposite directions.

**State 1, measured.** Immediately after a well-formed host acceptance on `.github#2534` / PR #2541 —
one initial marker, one confirmation advancing the round, one critic, `preceding-review` correct,
acceptance bound to the latest reviewed head — `review` returns:

```json
{"state":"malformedEvidence","stateErrors":["review checks are not green"],"action":"park"}
```

*Verification (reproduced for this item, not inherited):* the live comment set of PR #2541 fetched with
`gh api repos/FS-GG/.github/issues/2541/comments`, fed to the shared Release engine as a `review
--snapshot` binding at head `30aa766ff68c2ef33282ee9bace3fc153756327a` with `checks: "pending"`. The
**identical** comment set with `checks: "green"` returns `{"state":"accepted","action":"accept"}`
carrying `criticIdentity: "teal-b3a9"`, `rounds: [1]`. Nothing about the evidence differs between the
two runs; only the live check state does.

By `.github#2504`, `claim-generation` **cannot** be green at that moment: it is a required status
context on `main` whose marker is written by the live `delivery` call that `pnext-item` §6 places
*after* acceptance. So `review` passes through `malformedEvidence` as a **designed step of every
ordinary landing**.

`malformedEvidence` is the same bucket as two competing initial markers and a missing critic identity,
and the recovery that word teaches is *close the pull request without merging*. That recovery ran on
PR #2514 on 2026-08-13; the branch was reopened as #2528 at the cost of a full fresh review. The word
gives a host no way to tell a destroyed-chain case from a healthy one.

**State 2, measured.** The same PR, one comment earlier — the round-1 finding was not in the diff but in
the post-merge obligations comment, so the repair was an edit to that comment body and the head
correctly did not move:

```json
{"state":"awaitingImplementerRepair","action":"resumeImplementer",
 "actionReason":"the critic requested changes at the current head; no new commit has landed yet"}
```

*Verification:* same live comment set with the confirmation and acceptance comments withheld, i.e. the
state that existed the moment the comment-shaped repair was complete.

`Review.fs:401-404` routes to the implementer whenever `reviewedHead = binding.HeadSha`, which models a
repair as **a commit**. The obligations declaration is a standing artefact of every item, so a finding
against it recurs by construction, and the only mechanical way through is a no-op commit — a gate that
rewards manufacturing evidence rather than producing it.

Both states are transient and self-clearing: `Driver.fs:1078` recomputes from live facts on every call
and nothing is persisted. Every refusal beneath them is correct and fail-closed; nothing unreviewed can
merge before or after this change. What is wrong is what the inspect verb **names**.

## Scope

- SB-001: `Driver.validateReviewChain`'s problem list is split at its source into **structural** facts
  about durable evidence and **liveness** facts about the pull request's current check state, so a
  caller can ask the narrower question without string-matching a message. `validateReviewChain` keeps
  its exact messages in its exact order.
- SB-002: A new `Review.State` case, `AcceptedAwaitingChecks of checks: PrState`, for a chain that is
  well-formed, host-accepted, critic-identified and bound to the current head, whose only outstanding
  condition is a non-green live check state. `stateErrors` is null for it.
- SB-003: A new `Review.NextAction` case, `AuthorizeDelivery of reason: string`, naming the §6
  `scripts/fsgg-coord delivery <ref> --pr <n>` call as the step that unblocks `claim-generation` — the
  cycle `.github#2504` identified, which passive waiting can never break.
- SB-004: A new accountable grant, `Review.RepairAssertionReceipt`, letting a comment-shaped repair be
  represented at an unmoved head. It is threaded exactly as `.github#2417`'s `CriticSuccessionReceipt`
  is: an explicit `inspect`/`advance` parameter rather than a `Facts` field, parsed additively at the
  `--snapshot` boundary, and `None` on the live path.
- SB-005: `Driver.ReviewPhaseFacts` gains `LatestReviewUrl`, read off the SAME `classifyMarkers` groups
  every other field is read off, so the receipt can be bound to the exact review it answers.
- SB-006: `ReviewApplication.fs` renders the new state, action and grant on the
  `fsgg.coord.review/1` wire.
- SB-007: `independent-review.md` (both kit-mirrored copies) states the post-acceptance §6 window, the
  new state word, and the repair-assertion grant with its guard conditions.
- SB-008: One hermetic wire fixture under `tests/review-state-vocabulary` driving the COMPILED engine
  over both new states, plus recorded gate-inversion evidence for every new leg.

## Non-Goals

- SB-101: Does NOT add a marker kind. `Protocol.reviewPolicy`'s vocabulary, the `.fsi` surface
  baselines, `Snapshot.fs`'s `markerAnchors` emission and every generated projection region are
  untouched. See AMB-001 for why the durable-marker shape was priced and rejected.
- SB-102: Does NOT change `landable`, `Landable.advisoryFrom`, or the derived-complement rule
  `.github#2517` installed. `.github#2360`'s requirement that the CI verdict stay wholly independent of
  the review chain is preserved by not touching the file.
- SB-103: Does NOT change the round ceilings (3 ordinary, 10 repair-phase), the repair phase, critic
  succession, the same-critic rule, or the host-acceptance marker requirement.
- SB-104: Does NOT address `.github#2487` (`awaitingHostAcceptance` reported at a moved head). See
  "Relationship to .github#2487" below.
- SB-105: Does NOT relax any refusal. Every state this change introduces is reachable only from inputs
  that already produced a *correct* refusal beneath a *wrong* word, or from an explicit grant.
- SB-106: Does not implement Governance policy enforcement.

## Relationship to `.github#2487` — separate, and why

`#2487` is the same family: `review`'s inspect path misclassifies a state and the host acts on it. It
stays a separate row for three reasons, and this specification records them so the separation is a
decision rather than an omission.

1. **Opposite direction.** `#2487` is too *permissive* — it reports `awaitingHostAcceptance` at a head
   the PR has moved off, inviting a host to accept work no critic reviewed. This row is too *alarming* —
   it reports complete evidence as broken, inviting a host to destroy it. A single change cannot be
   validated against both without one of them silently setting the other's tolerance.
2. **Different remedy.** `#2487` needs one cross-check of `reviewed-head` against the live head inside
   an arm that currently does not perform it. This row needs a *classification* change: partitioning one
   error list and adding vocabulary. Neither remedy is a step toward the other.
3. **Different blast radius on the same arm.** Folding them would put a permissiveness fix and a
   vocabulary fix in one diff over `acceptanceOutcome`, where the head-binding arm this row leaves
   deliberately unchanged (`"the accepted review chain is bound to a different head than the current
   commit"`) is exactly the arm `#2487` must rework. Landing them separately keeps each one's
   gate-inversion evidence attributable.

This row therefore leaves that arm byte-for-byte as it found it, and AC-006 pins that.

## User Stories

- US-001 (P1): As a host that has just posted a well-formed acceptance and is following §6, I read a
  state that tells me the chain is complete and the next step is the `delivery` call, so I never reach
  for the close-and-reopen recovery on a healthy chain.
- US-002 (P1): As the protocol's guarantee that structurally broken evidence is refused, I still see
  every structural malformation reported as `malformedEvidence`, and I am never softened into a
  reassuring word by a non-green check.
- US-003 (P1): As an implementer whose critic's finding was against a PR comment rather than the tree, I
  can have the round advance without manufacturing a no-op commit that verifies nothing.
- US-004 (P1): As the guard against a critic confirming a head no one repaired, I still refuse at an
  unmoved head unless an accountable third party — never the implementer, never the round's critic —
  has explicitly attested the repair, bound to this head and to the exact review it answers.
- US-005 (P2): As a consumer of `review --json`, I can tell "this chain is broken" from "this chain is
  fine and the next step is the delivery call" from the payload alone, without re-deriving §6 by hand.

## Acceptance Scenarios

- AC-001 [US-001] [FR-002]: Given the live PR #2541 comment set (one initial `changes-required`, one
  round-1 `pass` confirmation, one host acceptance, all bound to `30aa766f…`) and `checks: pending`,
  when `Review.inspect` runs at that head, then the state is `AcceptedAwaitingChecks PrPending`, the
  rendered `stateErrors` is null, and the action is `AuthorizeDelivery` whose reason names the §6
  `delivery` call and `.github#2504`.
- AC-002 [US-001] [FR-002]: Given AC-001's inputs with `checks: green`, when `Review.inspect` runs, then
  the state is `Accepted` and the action is `Accept`, unchanged from before this change.
- AC-003 [US-002] [FR-001]: Given a chain carrying a structural malformation (a confirmation whose
  `round` does not continue) AND `checks: pending`, when `Review.inspect` runs, then the state is
  `MalformedEvidence` and `stateErrors` contains the structural message and does NOT contain
  `"review checks are not green"`.
- AC-004 [US-002] [FR-003]: Given any `ReviewChain` value and any ceiling, when
  `Driver.validateReviewChain` runs before and after the split, then it returns the identical message
  list in the identical order — pinned by a test that names all nine messages positionally for a chain
  that fails every clause.
- AC-005 [US-001] [FR-002]: Given AC-001's inputs with `checks: red`, when `Review.inspect` runs, then
  the state is `AcceptedAwaitingChecks PrRed` and the action is `ResumeImplementer` whose reason names
  the failing checks — not `MalformedEvidence`, because red CI is not broken evidence.
- AC-006 [US-002] [FR-004]: Given an accepted chain whose `reviewed-head` differs from
  `Binding.HeadSha`, when `Review.inspect` runs, then the state is `MalformedEvidence [ "the accepted
  review chain is bound to a different head than the current commit" ]` and the action is `Park` —
  byte-for-byte as before this change, leaving `.github#2487`'s arm untouched.
- AC-007 [US-003] [FR-005]: Given a chain whose latest verdict is `changes-required` at a
  `reviewed-head` equal to `Binding.HeadSha`, and a `RepairAssertionReceipt` whose `CandidateHeadSha`
  equals that head, whose `AnsweredReviewUrl` equals the latest review comment's URL, and whose
  `GrantedBy` is neither the implementer nor the critic, when `Review.inspect` runs, then the state is
  `AwaitingSameCriticConfirmation` and the action is `ResumeSameCritic`.
- AC-008 [US-004] [FR-006]: Given AC-007's inputs but NO receipt, when `Review.inspect` runs, then the
  state is `AwaitingImplementerRepair` and the action is `ResumeImplementer` with its pre-existing
  reason — byte-for-byte as before this change.
- AC-009 [US-004] [FR-006]: Given AC-007's inputs with a receipt whose `CandidateHeadSha` names a
  different head, when `Review.inspect` runs, then the result is AC-008's, and the reason additionally
  records that a receipt was supplied and refused.
- AC-010 [US-004] [FR-006]: Given AC-007's inputs with a receipt whose `AnsweredReviewUrl` names a
  different comment, when `Review.inspect` runs, then the result is AC-009's.
- AC-011 [US-004] [FR-006]: Given AC-007's inputs with a receipt whose `GrantedBy` equals
  `Binding.ImplementerIdentity`, when `Review.inspect` runs, then the result is AC-009's — an
  implementer can never unlock its own round.
- AC-012 [US-004] [FR-006]: Given AC-007's inputs with a receipt whose `GrantedBy` equals the round's
  `critic:` identity, when `Review.inspect` runs, then the result is AC-009's — a critic can never
  manufacture its own trigger to confirm.
- AC-013 [US-003] [FR-005]: Given AC-007's inputs in the REPAIR phase with an active repair-phase
  marker, when `Review.inspect` runs, then the state is `RepairPhaseActive` and the action is
  `ResumeSameCritic`, so the two phases share one guard rather than two copies.
- AC-014 [US-005] [FR-007]: Given AC-001's and AC-007's inputs, when `review --snapshot … --json` runs
  against the COMPILED engine, then the payload carries `state: "acceptedAwaitingChecks"` with
  `stateErrors: null` and a `stateReason` naming the live check state, and the repair-assertion case
  renders `action: "resumeSameCritic"`; a snapshot omitting `repairAssertionGranted` entirely parses
  exactly as it does today.
- AC-015 [US-002] [FR-008]: Given the fixture from SB-008, when the structural/liveness split is
  reverted (the liveness tag flipped to structural), then the fixture reds; and when the receipt guard
  is weakened (any one conjunct dropped), the corresponding refusal leg reds.
- AC-016 [FR-009]: Given the merged diff, when its file list is read, then it contains no
  `src/FS.GG.Coord.Core/Landable.fs` and no `Protocol.fs`/`Protocol.fsi` path.

## Functional Requirements

- FR-001: `Driver` MUST expose the STRUCTURAL subset of the review-chain problem list separately from the live-check liveness problem, computed from ONE shared, order-preserving source rather than two hand-maintained lists, and `Review.acceptanceOutcome` MUST classify on the structural subset. (covers AC-003)
- FR-002: A chain that is well-formed, host-accepted, critic-identified and bound to `Binding.HeadSha` MUST report the new `AcceptedAwaitingChecks` state whenever the live check state is not green, with `stateErrors` null; its action MUST be `AuthorizeDelivery` for a pending or unknown check state, `ResumeImplementer` for red or conflicted, and `Park` for merged or closed. (covers AC-001, AC-002, AC-005)
- FR-003: `Driver.validateReviewChain` MUST return the identical messages in the identical order after the split as before it, so `Driver.receiptFresh` and every other existing caller is behaviourally unchanged. (covers AC-004)
- FR-004: The head-mismatch and missing-critic arms of `acceptanceOutcome` MUST be unchanged, so `.github#2487`'s remedy lands on the code this row found rather than on code this row rewrote. (covers AC-006)
- FR-005: A comment-shaped repair MUST be representable: at an unmoved head after a `changes-required` verdict, a valid `RepairAssertionReceipt` MUST route to the same-critic confirmation state in BOTH the ordinary and repair phases, through ONE shared guard function. (covers AC-007, AC-013)
- FR-006: The receipt MUST be admitted only when it binds the current head, names the exact latest review comment URL, and carries a `GrantedBy` that is neither `Binding.ImplementerIdentity` nor the round's `critic:` identity; ANY failure MUST fall back to the pre-existing `ResumeImplementer` behaviour, with the refused-grant fact appended to the reason on the same near-miss convention `resumeSameCriticReason` already uses. (covers AC-008, AC-009, AC-010, AC-011, AC-012)
- FR-007: `ReviewApplication.fs` MUST render the new state, its check-state reason, and the new action on the `fsgg.coord.review/1` wire, and MUST parse `repairAssertionGranted` additively so a snapshot that omits it behaves exactly as today. (covers AC-014)
- FR-008: Every new state and every refusal leg MUST ship with gate-inversion evidence: an executable fixture that reds when the split or any one guard conjunct is removed. (covers AC-015)
- FR-009: `landable`'s CI verdict MUST stay wholly independent of the review chain, and no marker kind may be added. (covers AC-016)

## Ambiguities

- AMB-001: Issue acceptance criterion 3 leaves the design question open: model comment-shaped repairs
  inside the review state machine, or move obligations findings outside the review chain entirely.
  Resolved as DEC-001.
- AMB-002: If comment-shaped repairs are modelled, the assertion is a fact the engine cannot observe.
  Its channel — a durable PR marker the implementer posts, or an accountable receipt a caller supplies —
  is undetermined and the two have different blast radii and different guard strengths. Resolved as
  DEC-002.
- AMB-003: What the action should be for the post-acceptance window. `AwaitChecks` already exists, but
  by `.github#2504` passive waiting can never clear `claim-generation`. Resolved as DEC-003.
- AMB-004: Whether a non-green check state that is genuinely RED belongs in the new state at all, or
  should stay where it is. Resolved as DEC-004.

## Public Or Tool-Facing Impact

- Adds one state word and one action word to the `fsgg.coord.review/1` wire. Both are additive; no
  existing state or action name, payload key, or freshness/action-key derivation changes meaning.
- Adds one optional `facts.repairAssertionGranted` snapshot key, parsed additively.
- Changes the review contract text agents read (`independent-review.md`), which is coordination-kit
  source and therefore carries a kit-release obligation.
- This specification is an SDD lifecycle artifact and command-report contract input.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2549-review-state-vocabulary`.
