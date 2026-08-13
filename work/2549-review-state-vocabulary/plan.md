---
schemaVersion: 1
workId: 2549-review-state-vocabulary
title: Review State Vocabulary
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2549-review-state-vocabulary/spec.md
sourceClarifications: work/2549-review-state-vocabulary/clarifications.md
sourceChecklist: work/2549-review-state-vocabulary/checklist.md
publicOrToolFacingImpact: true
---

# Review State Vocabulary Plan

Prose status: planned

## Source Snapshot
- spec: work/2549-review-state-vocabulary/spec.md sha256:b044a60d8060e65e59eb52c973607282f3d10daff3264e8f65ff63349b727937 schemaVersion:1
- clarifications: work/2549-review-state-vocabulary/clarifications.md sha256:b8b3654ffedae199d03b834a0096a22830e4cb5fe68279f3c6f48afff6fd5479 schemaVersion:1
- checklist: work/2549-review-state-vocabulary/checklist.md sha256:e3b69f29356d3a5991f43a01390eaf70a9a67a319dc96cce7c4a4665195d4353 schemaVersion:1

## Plan Scope
- Work item 2549-review-state-vocabulary is planned from the current specification, clarification, and checklist facts.
- Requirement count: 9.
- Clarification decision count: 5.
- Checklist result count: 9.

## Plan Decisions

- PD-001 [AC-003] [FR-001] complete: `Driver.validateReviewChain`'s nine clauses become one private
  `reviewChainProblems maxRounds chain` returning `(isStructural: bool) * string` pairs in the current
  order. `validateReviewChain` is `reviewChainProblems … |> List.map snd` — provably the same messages in
  the same order, which is what keeps `Delivery.fs:191` and `Driver.receiptFresh` (both outside this
  item's declared paths) behaviourally untouched. `validateReviewChainStructure` is the structural
  subset. Exactly one clause is tagged non-structural: `"review checks are not green"`. `"host
  acceptance is missing"` stays STRUCTURAL — it is a completeness fact about durable evidence, not about
  a CI run, and `acceptanceOutcome` is only ever reached when an acceptance marker is present, so
  tagging it structural cannot change any state this row introduces.

- PD-002 [AC-001] [AC-002] [AC-005] [FR-002] complete: `Review.acceptanceOutcome` classifies on
  `Driver.validateReviewChainStructure` instead of `validateReviewChain`. Its accepting arm — structural
  errors empty, critic identity present, `chain.HeadSha = Some binding.HeadSha` — now branches on
  `facts.Checks` rather than assuming green: `PrGreen` keeps today's `Accepted`/`Accept receipt`;
  `PrPending`/`PrUnknown` give `AcceptedAwaitingChecks checks` with `AuthorizeDelivery`;
  `PrRed`/`PrConflicted` give `AcceptedAwaitingChecks checks` with `ResumeImplementer`;
  `PrMerged`/`PrClosed` give `AcceptedAwaitingChecks checks` with `Park`. The match on `PrState` is
  exhaustive with no wildcard, so a future `PrState` case cannot silently fall into a wrong arm.

- PD-003 [AC-004] [FR-003] complete: `DriverTests.fs` gains a leg constructing a `ReviewChain` that
  fails every clause and asserting `validateReviewChain` returns all nine messages in the exact current
  order, plus a leg asserting `validateReviewChainStructure` returns those nine minus the checks message
  in the same relative order. The first leg is the compatibility pin for `Delivery.fs` and
  `receiptFresh`; it is authored to pass BEFORE the change as well as after, which is stated explicitly
  so it is not mistaken for gate-inversion evidence.

- PD-004 [AC-006] [FR-004] complete: The `| [], Some _ ->` (head mismatch) and `| [], None ->` (no
  critic identity) arms of `acceptanceOutcome` keep their exact reasons and shapes. `.github#2487`'s
  remedy lands on the arm this row found, not on one this row rewrote. `ReviewTests.fs` gains an
  assertion pinning the head-mismatch reason string so a later edit to it is a deliberate act.

- PD-005 [AC-007] [AC-013] [FR-005] complete: `Review.RepairAssertionReceipt` is added with fields
  `AnsweredReviewUrl`, `CandidateHeadSha`, `GrantedBy`, `Reason`. `inspect`/`advance` gain a fourth
  explicit parameter `repairAssertionGranted: RepairAssertionReceipt option`, threaded exactly as
  `.github#2417` threaded `successionGranted` and for the identical reason: `Review.Facts` is built as a
  record literal in `Client.fs`, which this item's declared paths do not include, and a new required
  field would force that file to change. `ReviewApplication.render` keeps its pinned 3-argument shape
  (`ReviewApplication.fsi`, also outside the declared paths) by passing `None` for both grants, so
  `Client.fs:2215` compiles unchanged.

- PD-006 [AC-008]…[AC-012] [FR-006] complete: ONE private guard,
  `repairAssertionValid binding repairAssertionGranted phaseFacts`, consulted by BOTH the ordinary and
  repair-phase `changes-required`/unmoved-head branches — never two copies, on the same single-guard rule
  `.github#2417` set for `criticSuccessionValid`. It admits a receipt only when every one of these
  holds: `receipt.CandidateHeadSha = binding.HeadSha`; `phaseFacts.LatestReviewUrl = Some
  receipt.AnsweredReviewUrl` with a non-blank URL; `receipt.GrantedBy` non-blank;
  `receipt.GrantedBy <> binding.ImplementerIdentity`; and `Some receipt.GrantedBy <>
  phaseFacts.CriticIdentity`. Any failure returns `None` and the caller emits today's
  `AwaitingImplementerRepair`/`ResumeImplementer` unchanged, with the refused-grant clause appended to
  the reason on the `resumeSameCriticReason` convention. Where a receipt IS admitted the state becomes
  `AwaitingSameCriticConfirmation round` / `RepairPhaseActive round` with `ResumeSameCritic`, whose
  reason names the comment-shaped repair and the granter.

- PD-007 [AC-007] [FR-005] complete: `Driver.ReviewPhaseFacts` gains `LatestReviewUrl: string option`,
  read off the SAME `classifyMarkers` groups every sibling field is read off — the latest confirmation's
  `Url` when one exists, else the single initial comment's. No new parsing, no new marker, no second
  classification. `Client.fs` only reads fields off this record, so adding one is source-compatible
  there; the record is constructed in exactly one place, `Driver.reviewPhaseFacts`.

- PD-008 [AC-014] [FR-007] complete: `ReviewApplication.fs` gains `stateName` `"acceptedAwaitingChecks"`,
  an `actionName` `"authorizeDelivery"`, a `stateReason` arm naming the live check word, an
  `actionReason` arm for `AuthorizeDelivery`, and an additive `repairAssertionGranted` reader modelled
  byte-for-byte on `criticSuccessionGranted` (absent key parses as `None`, a non-object is an
  `invalidArg`). `stateErrors` is untouched: it already returns `Some` only for `MalformedEvidence`, so
  the new state renders `null` by construction rather than by a new branch. `stateName` and `actionName`
  carry no wildcard arm and `FS.GG.Coord.Cli.fsproj` sets `TreatWarningsAsErrors`, so omitting either
  arm is a build error, not a silent `null`.

- PD-009 [AC-015] [FR-008] complete: All executable coverage lands in `tests/FS.GG.Coord.Core.Tests`
  (`ReviewTests.fs`, `DriverTests.fs`), which is wired into CI already. The two natural additional homes
  were BOTH refused by live claims and this is recorded rather than worked around:
  `.github/workflows/coord-engine.yml` collides with `.github#2537` (worker `avocet-7275`) and
  `tests/FS.GG.Coord.Cli.Tests` collides with `.github#2544` (worker `curlew-84dc`), each measured by
  `scripts/fsgg-coord widen … --json` returning `verdict: overlap` with the declaration left unchanged.
  Consequently NO new `tests/<suite>/run.sh` fixture is created: an unwired fixture is a gate CI never
  runs, which is the `#266` defect. The CLI wire rendering is instead evidenced by a recorded
  measurement against the engine built from the candidate head, reported in the pull request, and by the
  compiler-enforced exhaustiveness of the rendering match described above.

- PD-010 [FR-007] complete: `independent-review.md` is edited once and the byte-identical `.agents`
  mirror updated to match. The new subsection states: the designed post-acceptance §6 window and its new
  state word; that `malformedEvidence` now means structurally broken evidence ONLY; that the recovery
  for it (close and reopen without merging) must never be reached from a non-green check; the
  repair-assertion grant, its four guard conditions, and that the implementer and the round's critic can
  never grant it; and the machine-readable literal
  `comment-shaped-repair-requires-explicit-grant: true`. No `Protocol.reviewPolicy` fact changes, so no
  generated projection region in that file moves.

## Contract Impact
- PC-001 [PD-002] [PD-005] [PD-007] [PD-008] public-surface: `FS.GG.Coord.Core.Review` gains one `State`
  case, one `NextAction` case, one record type, and one parameter on `inspect`/`advance`;
  `FS.GG.Coord.Core.Driver` gains one function and one `ReviewPhaseFacts` field; the `review --json` wire
  gains one state word, one action word, and one optional input key. Every addition is additive: no
  existing state name, action name, payload key, or freshness/action-key derivation changes meaning, and
  no file outside this item's declared paths needs an edit — `ReviewApplication.render` and
  `Driver.validateReviewChain` both keep their pinned shapes and behaviour.
- PC-002 [PD-010] kit-content: `.claude`/`.agents` `pnext-item/references/independent-review.md` is
  coordination-kit source. Its change is picked up by `kit-auto-publish` on push to `main`; the merged
  precedent `0ca9d308` (`.github#2527`) carried the identical two-file kit edit with no in-PR version
  bump or registry change, so this row plans the same shape and owes a POST-MERGE verification that the
  published bytes match canonical.

## Verification Obligations
- VO-001 [PD-001] [PD-002] [PD-003] semanticTest: `dotnet test tests/FS.GG.Coord.Core.Tests` covers
  (1) the live PR #2541 chain at `checks: pending` → `AcceptedAwaitingChecks PrPending` /
  `AuthorizeDelivery`; (2) the same at `green` → `Accepted`/`Accept`; (3) the same at `red` →
  `AcceptedAwaitingChecks PrRed`/`ResumeImplementer`; (4) a structurally malformed chain at
  `pending` → `MalformedEvidence` whose errors EXCLUDE `"review checks are not green"`; (5)
  `validateReviewChain`'s nine messages in order, before and after.
- VO-002 [PD-005] [PD-006] [PD-007] semanticTest: the same suite covers the admitted receipt in BOTH
  phases, and one refusal leg per guard conjunct (wrong head, wrong review URL, granter is the
  implementer, granter is the critic, blank granter), each asserting the pre-existing
  `AwaitingImplementerRepair`/`ResumeImplementer` result.
- VO-003 [PD-001] [PD-006] gateInversion: each new gate is inverted at authoring time and the observed
  red recorded — the liveness tag flipped to structural (the malformed-chain leg reds), and each guard conjunct
  dropped in turn (the corresponding refusal leg reds). Recorded as the exact mutation plus the
  failing test name.
- VO-004 [PD-002] regressionTest: `dotnet test tests/FS.GG.Coord.Core.Tests`,
  `tests/FS.GG.Coord.Cli.Tests` and `tests/FS.GG.Coord.GitHub.Tests` must be green UNCHANGED — in
  particular every existing `#2175`/`#2417`/`#2527` review leg, `LandableTests.fs`, and
  `DeliveryTests.fs`, which together are the compatibility boundary for FR-003 and FR-009.
- VO-005 [PD-010] projectionCheck: `scripts/generate-projections --check` stays clean, proving the added
  prose sits outside every generated region and no `Protocol.fs` fact moved implicitly.
- VO-006 [FR-009] diffAudit: the merged diff's file list contains no `Landable.fs`, no `Protocol.fs`,
  and no `Protocol.fsi`.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] additive-no-migration: Every surface change is additive; no stored snapshot, marker
  text, or persisted artifact requires migration. Both new states are recomputed from live facts on
  every call (`Driver.fs:1078`) and nothing is persisted, so there is no historical value to migrate.

## Generated View Impact
- GV-001 [PD-001] workModel: `readiness/2549-review-state-vocabulary/` is regenerated by
  `fsgg-sdd analyze`/`tasks` from this plan's PD/PC/VO/PM entries.

## Accepted Deferrals
- DEF-001 [PD-005]: The live `review <ref> --pr N` path passes `None` for the repair-assertion grant,
  per clarifications DEC-002. Criterion 3's route is reachable through `review --snapshot`.
- DEF-002 [PD-009]: CLI-level unit coverage of the wire rendering is deferred to whichever item next
  holds `tests/FS.GG.Coord.Cli.Tests`; the overlap that forces this is recorded in the coverage decision above with the
  colliding claim and worker.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2549-review-state-vocabulary`.
