---
schemaVersion: 1
workId: 2417-critic-despawn-review-recovery
title: Critic Despawn Review Recovery
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Critic Despawn Review Recovery Specification

Prose status: specified

## User Value
A host can recover a review chain whose critic despawned mid-round instead of the chain becoming
permanently unconfirmable. `scripts/fsgg-coord review` emits one typed, freshness-bound recovery
action — never an automatic guess — so the same-critic-continuity guarantee (`.github#2175`) is either
honestly preserved (the same critic confirms) or honestly restarted (a fresh critic reviews the current
head from scratch), and never silently defeated by a critic identity string that is not a reliable,
distinguishing identity.

## Scope
- SB-001: A new `Review.CriticSuccessionReceipt` fact, structurally analogous to the existing
  `RepairPhaseReceipt` external-grant pattern (`.github#2175`), supplied by the caller — never inferred
  or auto-detected by the pure engine — and bound to the exact original critic identity and candidate
  head SHA it was granted for.
- SB-002: A new `Review.NextAction` case, `EnterCriticSuccession`, returned by `Review.classify` in
  place of `ResumeSameCritic` only when a valid, matching receipt is present in `Facts`; absent a
  receipt, the classifier's behavior for an unconfirmed changes-required round is byte-for-byte
  unchanged from before this change.
- SB-003: Guard clauses so a receipt can never be exercised by the implementer against itself: the
  successor critic identity and the granting identity must each differ from `Binding.ImplementerIdentity`,
  and the receipt's candidate head must equal the binding's exact head SHA.
- SB-004: The `ReviewApplication.fs` JSON snapshot boundary (`--snapshot FILE`) parses an OPTIONAL
  `criticSuccessionGranted` key — absent or `null` defaults to `None`, so every existing snapshot
  producer (the live `review <ref> --pr N` path, and any existing `--snapshot` caller) keeps working
  unchanged — and serializes the new action and its receipt on the JSON wire.
- SB-005: The `independent-review.md` contract (both kit-mirrored copies) documents when and how a host
  grants a critic-succession receipt, what the successor critic must do (a genuinely fresh, full review
  of the current head — not a "trust the prior finding" confirmation), and the interaction with
  `.github#2360` (landable does not require or relax the host-acceptance marker): this recovery path
  changes who may produce an accepted review chain, never what `landable` or the host-acceptance
  marker themselves gate.

## Non-Goals
- SB-101: Does not implement automatic despawn DETECTION — the engine cannot observe whether a specific
  dispatched critic process is still alive, so it never infers unavailability; only an explicit,
  externally supplied receipt can trigger the recovery action (clarifications DEC-002 precedent,
  `.github#2175`).
- SB-102: Does not wire a granted receipt into the LIVE `review <ref> --pr N` command path
  (`Client.fs`) — that path already hardcodes `RepairPhaseGranted = None` and documents live-binding
  resolution as future work; this specification follows the identical, already-accepted precedent for
  the new fact rather than inventing a second one.
- SB-103: Does not change the closed `Review.State` model's named cases, the round-ceiling policy
  (`Protocol.reviewPolicy`), or `Driver.parseReviewComments`'s marker parsing.
- SB-104: Does not implement Governance policy enforcement.

## User Stories
- US-001 (P1): As a host inspecting a review chain stuck on `resumeSameCritic` naming a critic that has
  despawned, I can supply a typed, accountable succession receipt and receive a distinct, typed next
  action instead of a perpetually unactionable one.
- US-002 (P1): As a worker or the review-protocol event surface, I can trust that a chain whose original
  critic COULD still confirm is never silently diverted onto the recovery path — the recovery requires an
  affirmative, out-of-band grant, never an automatic guess about liveness.
- US-003 (P2): As the `.claude`/`.agents` skill contract's reader, I can find the exact machine-readable
  rule for this recovery path stated where the chain is evaluated, and its relationship to `landable`
  and the host-acceptance marker.

## Acceptance Scenarios
- AC-001 [US-002] [FR-001]: Given `Facts.CriticSuccessionGranted` is `None`, when `Review.inspect` is
  called on an ordinary-phase chain with an unconfirmed changes-required verdict at a new head, then it
  returns `AwaitingSameCriticConfirmation` and `ResumeSameCritic`, unchanged from current behavior.
- AC-002 [US-001] [FR-002]: Given a `CriticSuccessionReceipt` whose `OriginalCriticIdentity` matches the
  chain's recorded critic, whose `CandidateHeadSha` equals the binding's exact head SHA, and whose
  `SuccessorCriticIdentity` and `GrantedBy` both differ from `Binding.ImplementerIdentity`, when
  `Review.inspect` is called on the same chain, then it returns `EnterCriticSuccession` carrying that
  receipt.
- AC-003 [US-002] [FR-003]: Given a receipt whose `OriginalCriticIdentity` does not match the chain's
  recorded critic, or whose `CandidateHeadSha` does not equal the current head, when `Review.inspect`
  runs, then the receipt is refused and the action remains `ResumeSameCritic` — a stale or mismatched
  grant never substitutes for the guard.
- AC-004 [US-002] [FR-004]: Given a receipt whose `SuccessorCriticIdentity` or `GrantedBy` equals
  `Binding.ImplementerIdentity`, when `Review.inspect` runs, then the receipt is refused and the action
  remains `ResumeSameCritic` — an implementer can never grant itself succession.
- AC-005 [US-001] [FR-002]: Given the analogous repair-phase branch (`Phase = Repair`) reaches an
  unconfirmed changes-required round, when a valid matching receipt is present, then `EnterCriticSuccession`
  is returned there too, on the identical guard terms as the ordinary chain.
- AC-006 [US-003] [FR-005]: Given the `--snapshot FILE` JSON boundary, when the `criticSuccessionGranted`
  key is absent or `null`, then the parsed `Facts.CriticSuccessionGranted` is `None` and every existing
  snapshot payload (with no such key) parses exactly as it did before this change.
- AC-007 [US-003] [FR-006]: Given `independent-review.md` (both kit-mirrored copies), when a host needs
  to grant critic succession, then the contract states the exact typed fact, its guard conditions, what
  the successor critic must do, and that `landable`/`.github#2360` and the host-acceptance marker are
  unaffected by this recovery path.
- AC-008 [US-001] [FR-007]: Given the freshness/idempotency contract (`.github#2175` acceptance 9), when
  the same granted receipt is inspected twice against the unchanged binding, then `advance` re-converges
  on the identical verdict rather than minting a second succession.

## Functional Requirements
- FR-001: Absent `Facts.CriticSuccessionGranted`, `Review.classify` MUST return `ResumeSameCritic` for an unconfirmed changes-required round exactly as before this change. (covers AC-001)
- FR-002: `Review.classify` MUST return `EnterCriticSuccession` carrying the granted receipt when, and only when, the receipt's `OriginalCriticIdentity` matches the chain's recorded critic, its `CandidateHeadSha` equals the binding's exact head SHA, and both `SuccessorCriticIdentity` and `GrantedBy` differ from `Binding.ImplementerIdentity`, identically in the ordinary and repair-phase branches. (covers AC-002, AC-005)
- FR-003: A receipt whose original-critic identity or candidate head does not match the live chain MUST be refused (treated as absent), never partially honored. (covers AC-003)
- FR-004: A receipt naming the implementer as successor or granter MUST be refused (treated as absent). (covers AC-004)
- FR-005: The `--snapshot` JSON boundary MUST treat `criticSuccessionGranted` as optional; its absence or `null` MUST parse to `None` without error. (covers AC-006)
- FR-006: `independent-review.md` (`.claude/skills/pnext-item/references` and its `.agents/skills` kit mirror, kept byte-identical) MUST document the grant conditions, the successor critic's obligation to perform a genuinely fresh review, and the explicit, stated non-interaction with `landable`/`.github#2360` and the host-acceptance marker. (covers AC-007)
- FR-007: `Review.advance`'s existing freshness-token/action-key idempotency MUST cover the new action exactly as it covers `EnterRepairPhase` today. (covers AC-008)

## Ambiguities
- AMB-001: Whether a granted-but-refused receipt (mismatched critic/head, or self-granted) should also be
  surfaced as a distinct diagnostic string on the `ResumeSameCritic` action, versus silently falling back
  with no observable difference from "no receipt supplied at all". Resolved in `clarify` (see DEC-001).

## Public Or Tool-Facing Impact
- Adds `Review.CriticSuccessionReceipt` and `Review.NextAction.EnterCriticSuccession` to
  `FS.GG.Coord.Core`'s public review-protocol surface (`.github#2175`'s typed layer).
- Adds an optional `criticSuccessionGranted` key and a corresponding `criticSuccessionReceipt` output
  field to the `fsgg.coord.review/1` JSON wire contract (`ReviewApplication.fs`).
- Updates `independent-review.md` in both kit-mirrored locations (`.claude/skills/pnext-item/references`,
  `.agents/skills/pnext-item/references`) — a content-addressed kit source requiring a kit version bump
  and republish before merge.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2417-critic-despawn-review-recovery`.
