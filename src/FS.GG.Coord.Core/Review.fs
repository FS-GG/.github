namespace FS.GG.Coord

module Review =
    open System
    open System.Security.Cryptography
    open System.Text
    open Types

    type Phase =
        | Ordinary
        | Repair

    type Binding =
        { ItemRef: string
          Pr: int
          HeadSha: string
          ClaimGeneration: string
          ImplementerIdentity: string
          Phase: Phase
          Round: int }

    type RepairPhaseReceipt =
        { ExhaustedPr: int
          EscalationCommentId: int64
          NewClaimGeneration: string
          NewBranchOrPr: string
          NewImplementerIdentity: string
          NewCriticIdentity: string
          CandidateHeadSha: string }

    // The accountable, out-of-band grant that lets a chain recover when its critic despawned mid-round
    // (.github#2417) — structurally the same "external fact the pure engine cannot observe itself"
    // pattern as `RepairPhaseReceipt` (.github#2175 clarifications DEC-002): the engine never infers
    // unavailability from silence, it only ever consumes a receipt the caller supplies. `GrantedBy` is
    // the accountable identity (typically the host) who attests the original critic is unavailable;
    // `SuccessorCriticIdentity` is the fresh critic who will perform a genuinely new, full review of the
    // current head rather than a "confirmation" of the despawned critic's finding — the property the
    // same-critic rule protects is preserved either by the same critic confirming, or by the chain being
    // honestly restarted, never by a stranger silently continuing it (`independent-review.md`).
    type CriticSuccessionReceipt =
        { OriginalCriticIdentity: string
          SuccessorCriticIdentity: string
          GrantedBy: string
          Reason: string
          CandidateHeadSha: string }

    // The accountable grant that lets a repair whose subject is a PR COMMENT rather than the tree
    // advance a round (.github#2549) — the third instance of the same "external fact the pure engine
    // cannot observe itself" pattern as `RepairPhaseReceipt` and `CriticSuccessionReceipt`.
    //
    // WHY A GRANT AND NOT AN OBSERVATION. `.github#2527`'s charter sets the test: prefer evidence the
    // engine can already observe, and grant only what it genuinely cannot. A `Driver.ReviewComment` is
    // `{ Id; Url; Body }`, so a comment's CURRENT body is observable but "it changed in answer to this
    // finding" is not. The fact qualifies for a grant on exactly the test that disqualified one there.
    //
    // WHY THIS IS A STRONGER GUARD THAN THE RULE IT SUPPLEMENTS. The head-equality rule below treats
    // "the tree moved" as proof the implementer did work. An empty commit satisfies it and proves
    // nothing, so an implementer that wants the lane to advance is actively incentivised to manufacture
    // one — which is what the live `.github#2534` instance declined to do. A grant carries `GrantedBy`,
    // so it can refuse the implementer and the round's critic BY NAME; a moved head cannot.
    //
    // `AnsweredReviewUrl` is the URL of the exact changes-required review comment this repair answers
    // (`Driver.ReviewPhaseFacts.LatestReviewUrl`), which is what stops a grant left over from an
    // earlier round being replayed against a later one. `CandidateHeadSha` binds it to the head the
    // repair was made at.
    type RepairAssertionReceipt =
        { AnsweredReviewUrl: string
          CandidateHeadSha: string
          GrantedBy: string
          Reason: string }

    type Facts =
        { Comments: Driver.ReviewComment list
          Checks: PrState
          RepairPhaseGranted: RepairPhaseReceipt option
          RepairRouteAvailable: bool
          DiffAuditTrusted: SemanticDiff.TrustedAudit option }

    type State =
        | AwaitingInitialReview
        | ChangesRequiringRepair of round: int
        | AwaitingImplementerRepair of round: int
        | AwaitingSuccessorReview of round: int
        | PassedAwaitingChecks
        | AwaitingHostAcceptance
        | AcceptedAwaitingChecks of checks: PrState
        | OrdinaryExhaustion
        | RepairPhaseSetup
        | RepairPhaseActive of round: int
        | Accepted
        | TerminalHumanPark of reason: string
        | MalformedEvidence of errors: string list
        | GuardViolation of reason: string

    type AcceptedReceipt =
        { HeadSha: string
          CriticIdentity: string
          Rounds: int list
          RepairPhase: bool
          ChecksGreen: bool
          RuntimeRouteEvidence: Driver.RuntimeRouteEvidence option
          DiffAuditRequired: bool
          DiffAuditHead: string option }

    type NextAction =
        | DispatchCritic
        | ResumeImplementer of reason: string
        | DispatchSuccessor of reason: string
        | AwaitChecks
        | AuthorizeDelivery of reason: string
        | RequestHostAcceptance
        | EnterRepairPhase of RepairPhaseReceipt
        | EnterCriticSuccession of CriticSuccessionReceipt
        | Accept of AcceptedReceipt
        | Park of reason: string

    type Verdict =
        { State: State
          NextAction: NextAction
          FreshnessToken: string
          ActionKey: string
          RetiredChains: Driver.ChainRetirement list }

    let private digest (value: string) =
        value
        |> Encoding.UTF8.GetBytes
        |> SHA256.HashData
        |> Convert.ToHexString
        |> fun hash -> hash.ToLowerInvariant()

    let private phaseToken =
        function
        | Ordinary -> "ordinary"
        | Repair -> "repair"

    let freshnessToken (binding: Binding) =
        [ binding.ItemRef
          string binding.Pr
          binding.HeadSha
          binding.ClaimGeneration
          binding.ImplementerIdentity
          phaseToken binding.Phase
          string binding.Round ]
        |> String.concat "\n"
        |> digest

    let private makeVerdict binding retiredChains state action =
        let token = freshnessToken binding
        let actionKey = digest $"%s{token}\n%A{state}\n%A{action}"
        { State = state
          NextAction = action
          FreshnessToken = token
          ActionKey = actionKey
          RetiredChains = retiredChains }

    let private missing value label =
        if String.IsNullOrWhiteSpace(value: string) then Some label else None

    let private validateBinding (binding: Binding) =
        [ missing binding.ItemRef "item ref"
          (if binding.Pr <= 0 then Some "pull request" else None)
          missing binding.HeadSha "head SHA"
          missing binding.ClaimGeneration "claim generation"
          missing binding.ImplementerIdentity "implementer identity"
          (if binding.Round < 1 then Some "round" else None) ]
        |> List.choose id

    let private ceilingFor phase =
        match phase with
        | Ordinary -> Protocol.reviewPolicy.MaxAutomatedRepairRounds
        | Repair -> Protocol.reviewPolicy.RepairPhaseMaxRounds

    /// The complete terminal-chain predicate shared by the read-side projection and the live
    /// escalation writer. Ordinary exhaustion requires an initial plus rounds one and two that all
    /// requested changes; a pre-round pass cannot be reclassified as exhaustion merely because a
    /// later round-three verdict is terminal. A round-three `changes-required` record is terminal
    /// regardless of check state. A round-three `pass` joins that set only after exact-head checks
    /// settle red: pending checks still have a reason to wait, while green checks remain eligible for
    /// host acceptance.
    let isOrdinaryExhaustionTerminal headSha checks comments =
        let phaseFacts = Driver.reviewPhaseFacts comments
        let marker = "<!-- fsgg:review-decision/v2 -->"
        let reviewRecords =
            comments
            |> List.sortBy _.Id
            |> List.choose (fun comment ->
                if comment.Body.StartsWith(marker + "\n", StringComparison.Ordinal) then
                    Driver.decodeStructuredReview (comment.Body.Substring(marker.Length).Trim())
                    |> Result.toOption
                else
                    None)
            |> List.filter (fun record ->
                record.Kind = StructuredDecision.Initial
                || record.Kind = StructuredDecision.Confirmation)
        let precedingChangesRequired =
            match reviewRecords with
            | initial :: roundOne :: roundTwo :: _ ->
                initial.Kind = StructuredDecision.Initial
                && initial.Round = 0
                && roundOne.Kind = StructuredDecision.Confirmation
                && roundOne.Round = 1
                && roundTwo.Kind = StructuredDecision.Confirmation
                && roundTwo.Round = 2
                && [ initial; roundOne; roundTwo ]
                   |> List.forall (fun record -> record.Verdict = StructuredDecision.ChangesRequired)
            | _ -> false

        precedingChangesRequired
        && phaseFacts.ConfirmationCount = Protocol.reviewPolicy.MaxAutomatedRepairRounds
        && phaseFacts.LatestReviewedHeadSha = Some headSha
        &&
            match phaseFacts.LatestVerdict, checks with
            | Some "changes-required", _ -> true
            | Some "pass", PrRed -> true
            | _ -> false

    let private ordinaryExhaustionOutcome (facts: Facts) =
        match facts.RepairPhaseGranted with
        | Some receipt -> RepairPhaseSetup, EnterRepairPhase receipt
        | None ->
            if facts.RepairRouteAvailable then
                let reason =
                    "the ordinary review chain is exhausted; the host must mint the one permitted "
                    + "fresh repair phase (new claim, branch/PR, implementer, critic) and re-inspect "
                    + "with a repair-phase receipt supplied"
                OrdinaryExhaustion, Park reason
            else
                let reason = "the ordinary review chain is exhausted and no repair route is available"
                TerminalHumanPark reason, Park reason

    /// Re-project a verdict after the live adapter has established the completed-wait and claim-
    /// turnover facts that pure `inspect` cannot observe. The terminal-set decision itself remains
    /// here, beside `classify`, and rebuilding through `makeVerdict` keeps the action key consistent.
    let projectCompletedOrdinaryExhaustion binding facts verdict =
        if binding.Phase = Ordinary
           && isOrdinaryExhaustionTerminal binding.HeadSha facts.Checks facts.Comments then
            let state, action = ordinaryExhaustionOutcome facts
            makeVerdict binding verdict.RetiredChains state action
        else
            verdict

    // A `critic:` value that is the bare, undifferentiated agent-type string every critic dispatched at
    // one route shares (`fsgg-critic-normal`, or any future `fsgg-critic-<route>`) rather than a
    // minted, distinguishing identity (.github#2451). The SAME RULE as the exported
    // `StructuredDecision.isGenericCriticIdentity`, which is now the canonical spelling: `.github#2662`
    // needed it in the ledger validator, so the predicate moved to the module that owns the record whose
    // `critic` field it judges, and `Driver.fs`'s former private copy is gone — it is an alias for the
    // export.
    //
    // THIS copy deliberately stays, and the reason is measurable rather than stylistic: the exact source
    // lines of `criticSuccessionValid` below are pinned as gate-inversion ANCHORS by
    // `tests/review-critic-succession-wire/run.sh`, which deletes one conjunct at a time from this file
    // and requires the matching refusal leg to red. Rewriting `isGenericCriticIdentity critic` to the
    // qualified spelling would move an anchor and silently regrade that leg NOT MEASURED — the exact
    // false green that fixture exists to catch. If this predicate's rule ever changes, update both.
    let private isGenericCriticIdentity (identity: string) =
        not (String.IsNullOrWhiteSpace identity)
        && identity.Trim().StartsWith("fsgg-critic-", StringComparison.OrdinalIgnoreCase)

    // The ONE guard both the ordinary and repair-phase `AwaitingSameCriticConfirmation`-shaped branches
    // consult (.github#2417 PD-002) — never two copies. A granted receipt is admitted only when it is
    // bound to the EXACT critic and head this round is stuck on, and neither the successor nor the
    // granter is the implementer: an implementer can never manufacture its own succession, and a stale
    // receipt left over from an earlier head or a different critic can never be silently reused
    // (acceptance AC-003/AC-004). Absent a receipt, or a receipt that fails any one of these, this
    // returns `None` and the caller's existing `ResumeSameCritic` behavior is completely unchanged
    // (acceptance AC-001) — the recovery path is never entered by inference, only by an explicit,
    // accountable grant.
    //
    // `.github#2451`: `receipt.OriginalCriticIdentity = critic` alone is NOT proof this receipt names
    // the exact stuck critic when `critic` is the bare agent-type string (`isGenericCriticIdentity`) —
    // every critic ever dispatched at that route would satisfy the equality, so the "exact critic"
    // property `independent-review.md` states is never actually witnessed by a generic string. A
    // receipt whose current-round critic identity is generic is refused exactly like a mismatched one:
    // the caller falls back to `ResumeSameCritic`, never to a succession the marker text cannot support.
    //
    // `.github#2557` — WHY THERE IS NO BLANK-STRING CONJUNCT HERE, and it is a recorded decision rather
    // than an omission. This guard carried two more conjuncts,
    // `not (String.IsNullOrWhiteSpace receipt.SuccessorCriticIdentity)` and the same for `GrantedBy`,
    // and NEITHER COULD EVER RUN.
    //
    // The claim is about CONSTRUCTION, not about callers, and stating it the other way round is how the
    // first draft of this comment got it wrong. A receipt reaches this guard only by being built, and
    // `ReviewApplication.criticSuccessionReceipt` is the sole site in `src/` that builds one — it reads
    // EVERY field through `ReviewApplication.readString`, which refuses an empty or whitespace string
    // and names the offending field. "Sole site in `src/`" would prove nothing for a library another
    // repository could reference, so the second half of the argument is that
    // `FS.GG.Coord.Core.fsproj` sets `<IsPackable>false</IsPackable>`: there is no packaged consumer to
    // construct one out of band. Both ways INTO this guard take the receipt as a parameter and cannot
    // manufacture it — `inspect`, and `advance` below, which calls `inspect` UNQUALIFIED from inside
    // this same module and is therefore invisible to a `Review.inspect`-shaped grep — while the live
    // `review <ref> --pr N` path supplies `None` outright. A blank value therefore fails at parse with
    // exit 1, this guard is never reached, and deleting either conjunct was invisible to every suite in
    // this repo: two lines that read as protection and could not be observed to protect anything, which
    // is `.github#2223`/`.github#2515`'s shape and exactly what `.github#2537` exists to stop shipping.
    //
    // The decisive evidence is behavioural rather than textual, which is the point: with `readString`'s
    // refusal deleted these conjuncts DO fire — they were never wrong, only unreachable, and the order
    // of the two layers is the whole finding. The property is not weakened by their removal, it is
    // enforced by the wire, and that enforcement is PINNED — `tests/review-critic-succession-wire/run.sh`
    // section 1 asserts the blank `successorCriticIdentity` and blank `grantedBy` refusals by field
    // name and exit code, and section 5 inverts `readString`'s own refusal and requires both legs to
    // flip. Every conjunct that remains below is a REACHABLE refusal with its own gate-inversion leg in
    // section 4. Do not re-add a precondition here that the type's sole producer already guarantees;
    // if a second producer is ever written, give the rule ONE home and a leg that reds without it.
    let private criticSuccessionValid
        (binding: Binding)
        (successionGranted: CriticSuccessionReceipt option)
        (currentCritic: string option)
        =
        match successionGranted, currentCritic with
        | Some receipt, Some critic when
            receipt.OriginalCriticIdentity = critic
            && not (isGenericCriticIdentity critic)
            && receipt.CandidateHeadSha = binding.HeadSha
            && receipt.SuccessorCriticIdentity <> binding.ImplementerIdentity
            && receipt.GrantedBy <> binding.ImplementerIdentity
            ->
            Some receipt
        | _ -> None

    // DEC-001 (.github#2417 clarifications): a receipt that was SUPPLIED but failed the guard reads
    // differently from no receipt at all, and a refused grant appends why, on the same near-miss-naming
    // convention `malformedVerdictReason` already uses for #2369.
    //
    // Factored out of `resumeSameCriticReason` by .github#2487 so the moved-head reason for a PASS
    // verdict carries the identical clause rather than a second, drifting copy of it —
    // `tests/review-critic-succession-wire/run.sh` asserts the exact substring "refused, not consumed"
    // on five refusal legs and asserts its ABSENCE on the no-grant control, so one home for the wording
    // is what keeps that fixture measuring the guard rather than a spelling.
    let private refusedGrantClause (successionGranted: CriticSuccessionReceipt option) =
        match successionGranted with
        | Some _ ->
            " (a critic-succession receipt was supplied but did not match this round's critic, head, or "
            + "guard conditions; it was refused, not consumed)"
        | None -> ""

    // AC3 of .github#2487, and the row treats it as the point rather than as polish: a verdict ABOUT
    // head divergence carries the two heads it was derived from, so a reader acts on one line of output
    // instead of re-deriving the comparison with a manual `git` call against a pull request they may not
    // have checked out.
    //
    // All three recorded instances cost at least one round-trip that this clause closes. Instance 3
    // (PR #2709) is the sharpest: `passedAwaitingChecks` / `awaitChecks` with `actionReason: null` and
    // `stateErrors: null` is indistinguishable, in the output alone, from a chain that really had
    // reviewed the current head — two agents had to go and read the chain's records by hand to find that
    // the only `pass` bound `48f8e6a5` while the pull request had moved to `bdf2a3e7`.
    //
    // Deliberately a CLAUSE appended to an existing sentence rather than a new field on `Verdict`. The
    // scoped question this row pilots is whether a verdict should carry its derivation at all; answering
    // it inside the reason text keeps `fsgg.coord.review/1`'s wire shape unchanged, so a consumer that
    // reads `actionReason` today gets the facts with no schema migration, and a later cut can promote
    // them to typed fields once the idea has been measured.
    let private headDivergenceClause (reviewedHead: string) (currentHead: string) =
        $" (the chain's latest review is bound to head %s{reviewedHead}; the pull request's current head "
        + $"is %s{currentHead})"

    // The leading sentence is byte-for-byte what .github#2417 shipped, and the refused-grant clause is
    // still appended in exactly the cases it was. What .github#2487 adds is the two heads, for the AC3
    // reason above — the change is additive to the message, and the pre-#2417 property DEC-001 protects
    // (a supplied-and-refused grant reads differently from no grant at all) is untouched.
    let private resumeSameCriticReason reviewedHead currentHead (successionGranted: CriticSuccessionReceipt option) =
        "a new commit landed after a changes-required verdict; a fresh successor must fully review it"
        + headDivergenceClause reviewedHead currentHead
        + refusedGrantClause successionGranted

    // The reason for .github#2487's own case: the chain's latest verdict IS a pass, but it passed a head
    // the pull request has since moved off, so nothing in the chain covers the commit a host is about to
    // be told it may accept.
    //
    // It names what acceptance will actually do, because the whole defect was that `review` and the
    // acceptance path answered the same question differently and only the cheaper door was optimistic:
    // `acceptanceOutcome` below refuses this exact chain with "the accepted review chain is bound to a
    // different head than the current commit", and `Driver.parseStructuredComments` refuses the marker
    // with "acceptance is not bound to the latest reviewed head". A host that follows this action never
    // authors either refusal.
    let private movedPassReason reviewedHead currentHead (successionGranted: CriticSuccessionReceipt option) =
        "the latest review verdict is pass, but it is bound to a head the pull request has moved off, so "
        + "no critic has reviewed the current head; a fresh successor must fully review it before a host can "
        + "author an acceptance the acceptance path would refuse"
        + headDivergenceClause reviewedHead currentHead
        + refusedGrantClause successionGranted

    // Ordinary queues need no host grant (.github#2756). A historical explicit grant is still
    // consumed when valid so already-authored snapshots and their tamper/refusal witnesses remain
    // readable; absent or refused grants take the ordinary durable fresh-successor route.
    let private successorAction
        (binding: Binding)
        (phaseFacts: Driver.ReviewPhaseFacts)
        (successionGranted: CriticSuccessionReceipt option)
        (reason: string)
        : NextAction =
        match criticSuccessionValid binding successionGranted phaseFacts.CriticIdentity with
        | Some receipt -> EnterCriticSuccession receipt
        | None -> DispatchSuccessor reason

    // Where the head the chain's latest review NAMES stands against the head this binding is being
    // decided at — the ONE comparison every verdict arm in both phases consults (.github#2487), never
    // four inline copies, on the same rule .github#2417 set for `criticSuccessionValid`.
    //
    // THE COMPARISON IS NOT NEW; ITS COVERAGE IS. The `changes-required` arms have always made it
    // inline, and `acceptanceOutcome` makes the equivalent one against the terminal parser's
    // `chain.HeadSha`. What was missing is that the two `pass` arms never read
    // `LatestReviewedHeadSha` at all — measured at source: the field had exactly two readers in all of
    // `FS.GG.Coord.Core`, both inside `changes-required` arms. So the engine was not generally
    // inattentive to head divergence; the check existed, was correct, and was applied to one of two
    // sibling branches. A `pass` at an abandoned head therefore reported `PassedAwaitingChecks` or
    // `AwaitingHostAcceptance` — terminal-looking answers whose documented next step is to finish the
    // cycle.
    //
    // THERE ARE TWO SITES, NOT ONE, and that is why this is a shared function rather than a fix at the
    // branch where the defect was observed. The identical `LatestVerdict`/`LatestReviewedHeadSha` shape
    // occurs once per phase; a repair-phase chain would have kept the optimistic answer, and would have
    // been far harder to notice because far fewer chains reach it.
    type private ReviewedHead =
        // Unreachable by construction today, and this is inherited rather than invented:
        // `Driver.reviewPhaseFacts` derives `LatestVerdict` and `LatestReviewedHeadSha` from the SAME
        // `latestReview` option, so a readable verdict implies a readable head, and the
        // `changes-required` arms have carried this same arm since #2175. It is not a new guard in
        // .github#2557's sense — the match must be total, and the only question is which answer an
        // unreadable head gets. Fail-closed is the only defensible one in a row whose entire subject is
        // that an unchecked fact must not take the reassuring path.
        | Unreadable
        | AtCurrentHead
        // The chain's latest review names this head, and the pull request has moved off it.
        | MovedFrom of reviewedHead: string

    let private reviewedHeadAgainst (binding: Binding) (phaseFacts: Driver.ReviewPhaseFacts) =
        match phaseFacts.LatestReviewedHeadSha with
        | None -> Unreadable
        | Some reviewedHead when reviewedHead = binding.HeadSha -> AtCurrentHead
        | Some reviewedHead -> MovedFrom reviewedHead

    // The ONE guard both the ordinary and repair-phase unmoved-head branches consult for a
    // comment-shaped repair (.github#2549) — never two copies, on the same rule `.github#2417` set for
    // `criticSuccessionValid`.
    //
    // This is what preserves the property the head-equality rule it supplements exists for: a critic
    // must never be sent to confirm a head no one repaired. Absent a receipt, or on ANY failed
    // conjunct, this returns `None` and the caller's pre-existing `ResumeImplementer` behavior is
    // completely unchanged — the route is entered only by an explicit, accountable grant, never by
    // inference and never by silence.
    //
    // Each conjunct answers a distinct way the grant could be abused:
    //  * `CandidateHeadSha = binding.HeadSha` — a grant made at an earlier head cannot be replayed
    //    after the tree moved.
    //  * `LatestReviewUrl = Some receipt.AnsweredReviewUrl` (non-blank) — a grant answering round 1
    //    cannot be replayed against round 2's finding. A blank URL can never be named, so a comment
    //    with no URL can never be answered.
    //  * `GrantedBy` is non-blank and is NOT `binding.ImplementerIdentity` — an implementer can never
    //    unlock its own round. This is the conjunct that makes the grant stronger than a moved head,
    //    which the implementer produces alone and unaccountably.
    //  * `GrantedBy` is NOT the round's critic — a critic can never manufacture its own trigger to
    //    confirm, which would collapse the two-party structure into one.
    let private repairAssertionValid
        (binding: Binding)
        (repairAssertionGranted: RepairAssertionReceipt option)
        (phaseFacts: Driver.ReviewPhaseFacts)
        =
        match repairAssertionGranted with
        | Some receipt when
            receipt.CandidateHeadSha = binding.HeadSha
            && not (String.IsNullOrWhiteSpace receipt.AnsweredReviewUrl)
            && phaseFacts.LatestReviewUrl = Some receipt.AnsweredReviewUrl
            && not (String.IsNullOrWhiteSpace receipt.GrantedBy)
            && receipt.GrantedBy <> binding.ImplementerIdentity
            && phaseFacts.CriticIdentity <> Some receipt.GrantedBy
            ->
            Some receipt
        | _ -> None

    // Same DEC-001 near-miss convention `resumeSameCriticReason` uses (.github#2369/.github#2417): the
    // base reason is byte-for-byte what it has always been when no grant was supplied, so a caller that
    // never grants one reads exactly the message it always did, and a REFUSED grant says so rather than
    // looking like no grant at all.
    let private resumeImplementerReason (repairAssertionGranted: RepairAssertionReceipt option) =
        let baseReason = "the critic requested changes at the current head; no new commit has landed yet"

        match repairAssertionGranted with
        | Some _ ->
            baseReason
            + " (a repair-assertion receipt was supplied but did not match this round's head, the review "
            + "comment it must answer, or its granter guards; it was refused, not consumed)"
        | None -> baseReason

    // The reason carried when a comment-shaped repair IS admitted. It names the granter, because the
    // whole safety argument for advancing at an unmoved head is that an accountable third party — not
    // the implementer — attested the repair.
    let private commentRepairConfirmationReason (receipt: RepairAssertionReceipt) =
        "the outstanding changes-required finding was repaired in a pull request comment rather than the "
        + $"tree, so the head correctly did not move; %s{receipt.GrantedBy} granted the repair assertion "
        + $"against review %s{receipt.AnsweredReviewUrl}, and a fresh successor must now fully review it"

    // The shared acceptance path for both ordinary and repair-phase review: reuses
    // `Driver.parseReviewCommentsWithFacts` (the terminal chain parser) and `Driver.validateReviewChain`
    // (the FS-GG/.github#2136 generated round-ceiling policy) as the sole authorities — no marker text
    // and no ceiling is defined in this module (acceptance 11). A chain whose head does not match the
    // current binding is treated exactly like any other unresolved evidence (acceptance 4): the prior
    // pass/checks/host-acceptance is invalidated by the new commit.
    // `live` is the retirement-filtered comment set (.github#2527), never `facts.Comments` directly:
    // the terminal chain parser must read the SAME chain `reviewPhaseFacts` classified, or the two
    // would disagree about which chain a PR carrying a retired one is being judged on.
    let private acceptanceOutcome (binding: Binding) (facts: Facts) (live: Driver.ReviewComment list) =
        let mechanicallyRequired = facts.DiffAuditTrusted.IsSome
        match Driver.parseReviewCommentsWithFacts mechanicallyRequired facts.DiffAuditTrusted live with
        | Error errors -> MalformedEvidence errors, Park(String.concat "; " errors)
        | Ok chain ->
            let checksGreen = facts.Checks = PrGreen
            let chainWithChecks = { chain with ChecksGreen = checksGreen }
            let ceiling = ceilingFor binding.Phase
            // .github#2549: STRUCTURE, not the full problem list. `validateReviewChain` also reports
            // "review checks are not green", and folding that into `MalformedEvidence` made every
            // ordinary landing pass through the state whose taught recovery is to close the pull
            // request without merging — on a chain with nothing wrong with it.
            match Driver.validateReviewChainStructure ceiling chainWithChecks, chain.CriticIdentity with
            | [], Some critic when chain.HeadSha = Some binding.HeadSha ->
                let receipt =
                    { HeadSha = binding.HeadSha
                      CriticIdentity = critic
                      Rounds = chain.Rounds
                      RepairPhase = chain.RepairPhase
                      ChecksGreen = checksGreen
                      RuntimeRouteEvidence = chain.RuntimeRouteEvidence
                      DiffAuditRequired = chain.DiffAuditRequired
                      DiffAuditHead = chain.DiffAuditHead }

                // Exhaustive over `PrState` with NO wildcard: a future check state cannot silently fall
                // into whichever arm happens to be last. The state is the same in every non-green case —
                // the chain is complete either way — and only the ACTION differs, because what a host
                // should do about "not reported yet" and "reported red" are opposites.
                match facts.Checks with
                | PrGreen -> Accepted, Accept receipt
                // `PrUnknown` is DELIBERATELY NOT GROUPED WITH `PrPending` (.github#2549 round-1 M1), and
                // the reason is this repository's own settled convention in three places rather than a
                // preference. `Landable.settled` scores `PrUnknown` alongside `PrConflicted` as SETTLED —
                // waiting cannot improve it — and explicitly not alongside `PrPending`, "the one verdict
                // worth waiting on". `Client.fs` maps it to `ExitNoVerdict`. And `Reads.fsi` records this
                // exact defect ALREADY FIXED ONCE: an unresolvable state used to be answered `PrPending`,
                // "the one verdict the exit contract defines as worth retrying, for a state that can never
                // change".
                //
                // It is not hypothetical here either. `Reads.fsi` degrades a multi-page runs list to
                // `PrUnknown` deterministically, so on a genuinely red pull request whose runs paginate,
                // grouping it with `PrPending` would tell the host to publish an authorization for a change
                // CI is failing, and never route to the implementer who owes the fix.
                //
                // It is not `ResumeImplementer` either: "the checks could not be read" is not evidence the
                // change is broken, and sending the implementer to fix an unnamed failure invents a fact in
                // the other direction. So it parks — a no-verdict on the CHECKS, over a chain whose evidence
                // is complete. An item whose whole subject is that an unreadable fact must not take a
                // reassuring path cannot make that mistake in its own new state.
                | PrUnknown ->
                    AcceptedAwaitingChecks facts.Checks,
                    Park(
                        "the review chain is complete and accepted at this head, but the pull request's "
                        + "check state could not be read; that is a no-verdict on the checks, not a wait "
                        + "and not a finding against the change, so a host must establish the real check "
                        + "state before either the delivery call or a repair round"
                    )
                | PrPending ->
                    AcceptedAwaitingChecks facts.Checks,
                    AuthorizeDelivery(
                        "the review chain is complete and accepted at this head; make the one live "
                        + "`scripts/fsgg-coord delivery <ref> --pr <n>` call pnext-item section 6 places here, "
                        + "which PATCHes the pull request's fsgg:pr-authorization marker onto this head "
                        + "and is what lets the required claim-generation context report at all "
                        + "(.github#2504) — waiting for green before making it is a cycle the marker can "
                        + "never break"
                    )
                | PrRed
                | PrConflicted ->
                    AcceptedAwaitingChecks facts.Checks,
                    ResumeImplementer(
                        "the review chain is complete and accepted at this head; the pull request's own "
                        + "checks are failing or conflicted, which is a defect in the change rather than "
                        + "in the review evidence"
                    )
                | PrMerged
                | PrClosed ->
                    AcceptedAwaitingChecks facts.Checks,
                    Park(
                        "the review chain is complete and accepted at this head, but the pull request is "
                        + "no longer open; no routine review action remains"
                    )
            | [], Some _ ->
                let reason = "the accepted review chain is bound to a different head than the current commit"
                MalformedEvidence [ reason ], Park reason
            | [], None ->
                let reason = "the accepted review chain carries no critic identity"
                MalformedEvidence [ reason ], Park reason
            | errors, _ -> MalformedEvidence errors, Park(String.concat "; " errors)

    // The base "neither readable pass nor changes-required" reason, extended with the exact expected
    // column-0 field form whenever `Driver.reviewPhaseFacts` found a markdown-emphasised near miss —
    // `**Verdict:** pass` rather than `verdict: pass` (.github#2369). A faithful critic following
    // `independent-review.md`'s prose produced exactly that shape: the marker looked canonical and the
    // chain parked with no signal about which field was unreadable or what the fix was. The base
    // message is unchanged when no near miss is found, so every existing malformed-verdict case keeps
    // its prior wording.
    let private malformedVerdictReason (phaseFacts: Driver.ReviewPhaseFacts) =
        let baseReason = "the latest review verdict is neither readable pass nor changes-required"

        match phaseFacts.LatestVerdictNearMissHints with
        | [] -> baseReason
        | hints -> baseReason + " (" + String.concat "; " hints + ")"

    // The competing-initial-marker refusal, extended with WHY no chain was retired (.github#2527).
    //
    // The leading sentence is byte-for-byte what it has always been — `ReviewTests.fs` asserts the
    // `"2 comments"` substring, and more importantly a reader who has seen this refusal before should
    // not have to re-learn it. What follows is the part that was missing: this state describes the
    // symptom and, before this change, named no remedy at all, so the one honest response to a head that
    // moved after acceptance (a full fresh review) looked indistinguishable from a stranger hijacking
    // someone else's chain. Same near-miss-naming convention as `malformedVerdictReason` (#2369).
    let private competingInitialMarkerReason (count: int) (diagnostics: string list) =
        let baseReason = $"the initial review marker is carried by %d{count} comments; exactly one is required"

        let rule =
            "a second chain is admitted only when a host-acceptance marker names an earlier chain's "
            + "initial-review comment URL AND carries an accepted-head other than the current head, which "
            + "retires that earlier chain without rewriting any of it (.github#2527)"

        let why =
            match diagnostics with
            | [] ->
                "no host-acceptance marker on this pull request names an initial review, so nothing is "
                + "retired: if the extra marker is a fresh review of a moved head, the accepted chain's "
                + "acceptance marker is what must name it; otherwise close and reopen the pull request so "
                + "the new chain starts alone, and leave both original chains intact on the closed one"
            | hints -> String.concat "; " hints

        $"%s{baseReason} — %s{rule}. %s{why}."

    // `live`/`diagnostics` come from `Driver.liveReviewComments` (.github#2527), computed once by
    // `inspect`. Every read below is of the LIVE set — the chain that binds this head — never
    // `facts.Comments` directly. On a pull request carrying one chain, which is every pull request this
    // protocol could already describe, `live` is `facts.Comments` ordered by id and nothing moves.
    let private classify
        (binding: Binding)
        (facts: Facts)
        (live: Driver.ReviewComment list)
        (diagnostics: string list)
        (successionGranted: CriticSuccessionReceipt option)
        (repairAssertionGranted: RepairAssertionReceipt option)
        : State * NextAction =
        let phaseFacts = Driver.reviewPhaseFacts live

        if not (List.isEmpty phaseFacts.StructuredErrors) then
            MalformedEvidence phaseFacts.StructuredErrors, Park(String.concat "; " phaseFacts.StructuredErrors)
        elif phaseFacts.CriticIdentity = Some binding.ImplementerIdentity then
            let reason = "the critic identity equals the implementer identity; an implementer cannot act as its own critic"
            GuardViolation reason, Park reason
        elif phaseFacts.InitialCount > 1 then
            let reason = competingInitialMarkerReason phaseFacts.InitialCount diagnostics
            MalformedEvidence [ reason ], Park reason
        elif phaseFacts.AcceptanceCount > 1 then
            let reason =
                $"the host-acceptance marker is carried by %d{phaseFacts.AcceptanceCount} comments; exactly one is required"
            MalformedEvidence [ reason ], Park reason
        else
            let ceiling = ceilingFor binding.Phase
            let exhausted = phaseFacts.ConfirmationCount > ceiling && not phaseFacts.AcceptancePresent

            match binding.Phase with
            | Repair ->
                if not phaseFacts.RepairPhasePresent then
                    match facts.RepairPhaseGranted with
                    | Some receipt -> RepairPhaseSetup, EnterRepairPhase receipt
                    | None ->
                        let reason =
                            "binding declares an active repair phase but no repair-phase marker is present in "
                            + "comments and no repair-phase receipt was supplied"
                        TerminalHumanPark reason, Park reason
                elif exhausted then
                    let reason = "the repair-phase confirmation round ceiling is exhausted with no acceptance; no further automatic route exists"
                    TerminalHumanPark reason, Park reason
                elif phaseFacts.AcceptancePresent then
                    acceptanceOutcome binding facts live
                elif not phaseFacts.InitialPresent then
                    RepairPhaseSetup, DispatchCritic
                else
                    let round = phaseFacts.ConfirmationCount + 1
                    match phaseFacts.LatestVerdict with
                    // .github#2487 site 1 of 2 — the REPAIR-phase `pass` arm. Structurally identical to
                    // the ordinary one below and repaired identically; fixing only the branch the defect
                    // was observed on would have left this one optimistic.
                    | Some "pass" ->
                        match reviewedHeadAgainst binding phaseFacts with
                        | Unreadable ->
                            let reason = "a pass verdict carries no readable reviewed-head field"
                            MalformedEvidence [ reason ], Park reason
                        | AtCurrentHead ->
                            if facts.Checks = PrGreen then
                                RepairPhaseActive round, RequestHostAcceptance
                            else
                                RepairPhaseActive round, AwaitChecks
                        | MovedFrom reviewedHead ->
                            RepairPhaseActive round,
                            successorAction binding phaseFacts successionGranted (movedPassReason reviewedHead binding.HeadSha successionGranted)
                    | Some "changes-required" ->
                        match reviewedHeadAgainst binding phaseFacts with
                        | Unreadable ->
                            let reason = "a changes-required verdict carries no readable reviewed-head field"
                            MalformedEvidence [ reason ], Park reason
                        | AtCurrentHead ->
                            match repairAssertionValid binding repairAssertionGranted phaseFacts with
                            | Some receipt ->
                                RepairPhaseActive round, DispatchSuccessor(commentRepairConfirmationReason receipt)
                            | None ->
                                RepairPhaseActive round,
                                ResumeImplementer(resumeImplementerReason repairAssertionGranted)
                        | MovedFrom reviewedHead ->
                            RepairPhaseActive round,
                            successorAction binding phaseFacts successionGranted (resumeSameCriticReason reviewedHead binding.HeadSha successionGranted)
                    | _ ->
                        let reason = malformedVerdictReason phaseFacts
                        MalformedEvidence [ reason ], Park reason
            | Ordinary ->
                if not phaseFacts.InitialPresent then
                    AwaitingInitialReview, DispatchCritic
                elif exhausted then
                    ordinaryExhaustionOutcome facts
                elif phaseFacts.AcceptancePresent then
                    acceptanceOutcome binding facts live
                else
                    let round = phaseFacts.ConfirmationCount + 1
                    match phaseFacts.LatestVerdict with
                    // .github#2487 site 2 of 2 — the ORDINARY `pass` arm, and the one all three recorded
                    // instances were measured on. AC1 and AC2 name the two answers it used to give at a
                    // moved head: `awaitingHostAcceptance`/`requestHostAcceptance` when the checks were
                    // green, and `passedAwaitingChecks`/`awaitChecks` when they were not. Both are
                    // terminal-looking, both told a host to finish a cycle over a commit no critic had
                    // read, and the second is what PR #2709 returned one careful reader away from a merge.
                    | Some "pass" ->
                        match reviewedHeadAgainst binding phaseFacts with
                        | Unreadable ->
                            let reason = "a pass verdict carries no readable reviewed-head field"
                            MalformedEvidence [ reason ], Park reason
                        | AtCurrentHead ->
                            if facts.Checks = PrGreen then
                                AwaitingHostAcceptance, RequestHostAcceptance
                            else
                                PassedAwaitingChecks, AwaitChecks
                        // AC1: the state that matches what acceptance ENFORCES — the same-critic
                        // confirmation the chain actually needs — and NOT a softer variant of the
                        // optimistic one. It is deliberately the same pair the `changes-required` sibling
                        // produces for the identical fact, including the succession recovery: a chain
                        // whose critic despawned after passing an abandoned head is in exactly the
                        // position .github#2417 built that grant for, and leaving it out would have made
                        // the pass arm the only place in the protocol where a moved head has no route.
                        | MovedFrom reviewedHead ->
                            AwaitingSuccessorReview round,
                            successorAction binding phaseFacts successionGranted (movedPassReason reviewedHead binding.HeadSha successionGranted)
                    | Some "changes-required" ->
                        match reviewedHeadAgainst binding phaseFacts with
                        | Unreadable ->
                            let reason = "a changes-required verdict carries no readable reviewed-head field"
                            MalformedEvidence [ reason ], Park reason
                        | AtCurrentHead ->
                            match repairAssertionValid binding repairAssertionGranted phaseFacts with
                            | Some receipt ->
                                AwaitingSuccessorReview round,
                                DispatchSuccessor(commentRepairConfirmationReason receipt)
                            | None ->
                                AwaitingImplementerRepair round,
                                ResumeImplementer(resumeImplementerReason repairAssertionGranted)
                        | MovedFrom reviewedHead ->
                            AwaitingSuccessorReview round,
                            successorAction binding phaseFacts successionGranted (resumeSameCriticReason reviewedHead binding.HeadSha successionGranted)
                    | _ ->
                        let reason = malformedVerdictReason phaseFacts
                        MalformedEvidence [ reason ], Park reason

    let inspect
        (binding: Binding)
        (facts: Facts)
        (successionGranted: CriticSuccessionReceipt option)
        (repairAssertionGranted: RepairAssertionReceipt option)
        : Result<Verdict, string list> =
        match validateBinding binding with
        | problems when not (List.isEmpty problems) ->
            Error(problems |> List.map (fun field -> $"review binding is incomplete: missing %s{field}"))
        | _ ->
            // .github#2527: the retirement partition is computed ONCE, here, and both the classifier
            // and the verdict read the same answer — so what the state was decided from and what the
            // verdict reports as retired can never disagree.
            let partition = Driver.liveReviewComments binding.HeadSha facts.Comments
            let expectedSubject = $"%s{binding.ItemRef}/pr/%d{binding.Pr}"
            if not (List.isEmpty partition.StructuredErrors) then
                let state = MalformedEvidence partition.StructuredErrors
                Ok(makeVerdict binding partition.Retired state (Park(String.concat "; " partition.StructuredErrors)))
            elif partition.StructuredSubject |> Option.exists ((<>) expectedSubject) then
                let reason = $"structured review subject does not match '%s{expectedSubject}'"
                Ok(makeVerdict binding partition.Retired (MalformedEvidence [ reason ]) (Park reason))
            else
                let state, action =
                    classify binding facts partition.Live partition.Diagnostics successionGranted repairAssertionGranted
                Ok(makeVerdict binding partition.Retired state action)

    let advance freshnessToken actionKey binding facts successionGranted repairAssertionGranted =
        match inspect binding facts successionGranted repairAssertionGranted with
        | Ok verdict when verdict.FreshnessToken = freshnessToken && verdict.ActionKey = actionKey -> Ok verdict
        | Ok _ -> Error [ "review verdict is stale or does not authorize this transition; re-inspect before advancing" ]
        | Error reasons -> Error reasons
