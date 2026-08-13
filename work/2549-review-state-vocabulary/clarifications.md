---
schemaVersion: 1
workId: 2549-review-state-vocabulary
title: Review State Vocabulary
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/2549-review-state-vocabulary/spec.md
publicOrToolFacingImpact: true
---

# Review State Vocabulary Clarifications

## Source Specification
- work/2549-review-state-vocabulary/spec.md

## Clarification Questions
- CQ-001 [AMB:AMB-001] blocking answered: Model comment-shaped repairs inside the review state machine,
  or move obligations findings outside the review chain entirely?
- CQ-002 [AMB:AMB-002] blocking answered: If modelled, through what channel does the unobservable
  "the repair has been made" fact enter the machine — a durable PR marker, or an accountable receipt?
- CQ-003 [AMB:AMB-003] blocking answered: What next action does the post-acceptance, pre-`delivery`
  window carry?
- CQ-004 [AMB:AMB-004] blocking answered: Does a genuinely RED check state belong in the new state?

## Answers
- ANS-001 [CQ-001]: Model it in the state machine. Moving obligations findings out of the chain removes
  review coverage of a class the live evidence proves is blocking, and does not generalise.
- ANS-002 [CQ-002]: An accountable receipt supplied by a caller, on `.github#2417`'s
  `CriticSuccessionReceipt` pattern — not a durable marker.
- ANS-003 [CQ-003]: A new `AuthorizeDelivery` action naming the §6 `delivery` call, not the existing
  `AwaitChecks`.
- ANS-004 [CQ-004]: Yes — the state is about the chain, and a red check does not make a chain broken;
  the action differentiates.

## Decisions

- DEC-001 [CQ-001] [AMB:AMB-001]: **Model comment-shaped repairs inside the review state machine.**
  Moving obligations findings outside the review chain was rejected on three measured grounds. (a) It
  removes coverage the live instance proves is load-bearing: on `.github#2534` the round-1 finding was
  that the obligations declaration did not parse, and `scripts/fsgg-coord delivery` would have refused
  with `delivery obligations are undeclared` at the §6 call — the critic caught, before merge, a defect
  that would otherwise have surfaced after acceptance. (b) It does not generalise: obligations are not
  the only critic-owned artefact that lives in a PR comment rather than the tree — the typed diff-audit
  receipt lives in the acceptance comment, and `independent-review.md` assigns release obligations to
  the critic explicitly. Removing one class leaves the shape unfixed for the rest. (c) It treats the
  surface, not the cause. The cause is that `Review.classify` models "the implementer produced new
  evidence" as "the tree moved", while `Driver`'s own terminal chain parser enforces no such thing:
  `Driver.fs:708` assigns `previousHead <- reviewedHead` and never compares it, so the invariant it
  actually enforces between rounds is comment-id monotonicity (`confirmation.Id > previousReviewId`) and
  `Driver.fs:820` requires only `acceptedHead = previousHead`. The classifier and the terminal parser
  disagree about whether an unmoved head between rounds is legal; the terminal parser is right, and this
  row makes the classifier agree with it.

- DEC-002 [CQ-002] [AMB:AMB-002]: **The assertion enters as an accountable `RepairAssertionReceipt`
  supplied by the caller, NOT as a durable PR marker.** The deciding test is the one `.github#2527`'s
  charter states: prefer evidence the engine can already observe over an out-of-band grant, and use a
  grant only where the fact is genuinely unobservable. Applied here: a `ReviewComment` is `{Id; Url;
  Body}`, so the comment's *current* body is observable but "it changed in answer to this finding" is
  not — the fact qualifies for a grant on exactly the test that disqualified one in `#2527`. The
  durable-marker alternative was priced, not hand-waved: it requires a new marker kind in
  `Protocol.reviewPolicy` (`markerNames` at `Driver.fs:46-51` sources `knownMarkerTexts`, and an
  unregistered marker line is inert), which pulls in `Protocol.fs`, `Protocol.fsi`, the
  `docs/api-surface` `.fsi` baselines, `Snapshot.fs`'s `markerAnchors` emission and its parity fixtures,
  and the generated projection regions — five surfaces this row's remedy does not otherwise touch. It is
  also the *weaker* guard: a marker the implementer posts is a self-assertion with no accountable
  granter, whereas the receipt carries `GrantedBy` and can refuse the implementer and the critic by
  name. `.github#2417` established this shape for a structurally identical problem (a fact about the
  world the pure engine cannot see) and its `criticSuccessionGranted` is threaded as an explicit
  `inspect`/`advance` parameter rather than a `Facts` field precisely so the live path's record literal
  need not change; this row follows it exactly.
  **Accepted limitation, stated rather than hidden:** the live `review <ref> --pr N` path passes `None`,
  so criterion 3's route is reachable through `review --snapshot` only. This is the same boundary
  `Review.fs:47-55` already documents for `RepairPhaseGranted` ("resolving that binding live is future
  work, not a silent wrong answer"). The live path's behaviour at an unmoved head is therefore
  byte-for-byte today's, which is the fail-closed default; the destructive misclassification this row
  exists to fix (DEC-003) IS fixed on the live path. A host-authored durable marker remains the natural
  follow-up if the live path later needs it, and is recorded here so that choice stays open rather than
  being foreclosed by this one.

- DEC-003 [CQ-003] [AMB:AMB-003]: **A new `AuthorizeDelivery` action, not the existing `AwaitChecks`.**
  `AwaitChecks` tells a host to wait, and by `.github#2504` waiting can never clear `claim-generation`:
  it is a required context on `main` whose marker is written by the §6 `delivery` call that follows
  acceptance, so "wait for green, then call `delivery`" is a cycle the marker can never break. Reusing
  `AwaitChecks` would replace one misleading word with another — quieter, but still sending the host to
  a dead end. The new action's reason names the exact command and the issue that ordered it.

- DEC-004 [CQ-004] [AMB:AMB-004]: **A red or conflicted check state reports the SAME new state, with a
  different action.** The state answers "is this chain's evidence complete and correctly bound?", and a
  failing CI run does not make durable review evidence malformed — reporting it as `malformedEvidence`
  is the identical category error this row exists to remove, merely less common. So the state is
  `AcceptedAwaitingChecks` carrying the live `PrState`, and the action differentiates: `AuthorizeDelivery`
  for pending/unknown (the §6 call is what unblocks them), `ResumeImplementer` for red/conflicted (the
  implementer genuinely owes a fix), and `Park` for merged/closed (nothing routine remains). This keeps
  ONE new state rather than three, and makes the check state itself the payload a consumer reads.

- DEC-005: **The structural/liveness split is made at the source, not by matching message strings.**
  `Driver.validateReviewChain`'s nine clauses become one list of `(isStructural, message)` pairs;
  `validateReviewChain` is that list's messages and `validateReviewChainStructure` is its structural
  subset. String-matching `"review checks are not green"` inside `Review.fs` was rejected: it would put
  a second, silent copy of the vocabulary in a second file, and a later reword of the message would
  reintroduce this exact defect with no test able to see it. Deriving both from one ordered source makes
  "same messages, same order" a property of the construction rather than a promise.

## Accepted Deferrals
- DEF-001: Resolving the repair-assertion grant on the live `review <ref> --pr N` path is deferred, per
  DEC-002's stated limitation, and is not required by any acceptance criterion of `.github#2549`.

## Remaining Ambiguity
No blocking ambiguity remains.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 2549-review-state-vocabulary`.
