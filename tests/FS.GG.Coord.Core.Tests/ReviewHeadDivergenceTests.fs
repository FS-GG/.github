namespace FS.GG.Coord.Tests

open System
open Xunit
open FS.GG.Coord

/// .github#2487 — `review` must not report a terminal-looking state for a chain whose only `pass` binds a
/// head the pull request has moved off.
///
/// THE MEASUREMENT THIS SUITE PINS, and it is the row's AC5. Every moved-head fact below is asserted
/// against the state the OLD code produced, by name, so a leg cannot pass for a reason unrelated to its
/// subject: `passedAwaitingChecks` and `awaitingHostAcceptance` are the two answers `.github#2487`
/// records, and each moved-head test refuses them explicitly rather than only affirming the new answer.
/// Reverting either comparison in `Review.classify` therefore reds these legs at the exact string a
/// reader can compare against the row.
///
/// The positive controls are the other half and they are load-bearing: with the heads AGREEING, every
/// pre-existing answer must survive byte-for-byte, in both phases and at both check states. A repair that
/// reported divergence unconditionally would satisfy every negative leg and be worse than the defect.
module ReviewHeadDivergenceTests =

    /// Distinct 40-hex heads. `reviewedHead` is what the chain's records bind; `movedHead` is where the
    /// pull request actually is — the `48f8e6a5` -> `bdf2a3e7` shape instance 3 measured on PR #2709.
    let private reviewedHead = String.replicate 40 "a"
    let private movedHead = String.replicate 40 "b"
    let private implementer = "impl-worker"

    let private seal (record: StructuredDecision.ReviewRecord) =
        StructuredDecisionTests.reseal { record with Digest = "" }

    let private initial verdict head =
        seal
            { StructuredDecisionTests.review 1 None StructuredDecision.Initial verdict 0 None None with
                HeadSha = head }

    /// `revision`/`previousDigest` chain the ledger; `initialReview`/`precedingReview` are the
    /// back-references `validateReviewLedger` requires of every non-initial record.
    let private following revision previous kind verdict round head preceding =
        seal
            { StructuredDecisionTests.review revision (Some previous) kind verdict round
                (Some "https://review/1") (Some preceding) with
                HeadSha = head }

    let private facts comments checks : Review.Facts =
        { Comments = comments
          Checks = checks
          RepairPhaseGranted = None
          RepairRouteAvailable = true
          DiffAuditTrusted = None }

    let private binding phase head : Review.Binding =
        { ItemRef = "FS-GG/.github#42"
          Pr = 77
          HeadSha = head
          ClaimGeneration = "claim-1"
          // Never the chain's critic `tern-42`: `inspect` fails closed when the two are equal, and a leg
          // that tripped that guard would measure the guard instead of this row's subject.
          ImplementerIdentity = implementer
          Phase = phase
          Round = 1 }

    let private verdictOf phase head comments checks =
        match Review.inspect (binding phase head) (facts comments checks) None None with
        | Error errors -> failwithf "review refused a well-formed binding: %A" errors
        | Ok verdict -> verdict

    /// The ORDINARY chain of instance 3, to the record: an initial `changes-required`, then the same
    /// critic's round-1 `pass` — and nothing binding the head the pull request later moved to.
    let private ordinaryChain () =
        let first = initial StructuredDecision.ChangesRequired reviewedHead
        let pass =
            following 2 first.Digest StructuredDecision.Confirmation StructuredDecision.Pass 1 reviewedHead
                "https://review/1"
        [ StructuredDecisionTests.reviewComment 1L first
          StructuredDecisionTests.reviewComment 2L pass ]

    let private ordinaryRoundThreeChainWithPrefix initialVerdict terminalVerdict =
        let head0 = String.replicate 40 "0"
        let head1 = String.replicate 40 "1"
        let head2 = String.replicate 40 "2"
        let first = initial initialVerdict head0
        let round1 =
            following 2 first.Digest StructuredDecision.Confirmation StructuredDecision.ChangesRequired 1
                head1 "https://review/1"
        let round2 =
            following 3 round1.Digest StructuredDecision.Confirmation StructuredDecision.ChangesRequired 2
                head2 "https://review/2"
        let round3 =
            following 4 round2.Digest StructuredDecision.Confirmation terminalVerdict 3
                reviewedHead "https://review/3"
        [ StructuredDecisionTests.reviewComment 1L first
          StructuredDecisionTests.reviewComment 2L round1
          StructuredDecisionTests.reviewComment 3L round2
          StructuredDecisionTests.reviewComment 4L round3 ]

    let private ordinaryRoundThreeChain terminalVerdict =
        ordinaryRoundThreeChainWithPrefix StructuredDecision.ChangesRequired terminalVerdict

    let private ordinaryRoundFourSuccessorChain terminalVerdict =
        let first = initial StructuredDecision.ChangesRequired (String.replicate 40 "0")
        let next revision previous round head verdict preceding =
            following revision previous StructuredDecision.Confirmation verdict round head preceding
        let round1 =
            next 2 first.Digest 1 (String.replicate 40 "1") StructuredDecision.ChangesRequired
                "https://review/1"
        let round2 =
            next 3 round1.Digest 2 (String.replicate 40 "2") StructuredDecision.ChangesRequired
                "https://review/2"
        let round3 =
            next 4 round2.Digest 3 reviewedHead StructuredDecision.Pass "https://review/3"
        let round4 =
            next 5 round3.Digest 4 movedHead terminalVerdict "https://review/4"
        [ StructuredDecisionTests.reviewComment 1L first
          StructuredDecisionTests.reviewComment 2L round1
          StructuredDecisionTests.reviewComment 3L round2
          StructuredDecisionTests.reviewComment 4L round3
          StructuredDecisionTests.reviewComment 5L round4 ]

    let private boundedOrdinaryChain initialVerdict terminalVerdict round head =
        let firstHead = if round = 0 then head else String.replicate 40 "0"
        let first = initial initialVerdict firstHead
        let comments = ResizeArray [ StructuredDecisionTests.reviewComment 1L first ]
        let mutable previous = first
        for currentRound in 1..round do
            let verdict =
                if currentRound = round then terminalVerdict
                else StructuredDecision.ChangesRequired
            let currentHead = if currentRound = round then head else String.replicate 40 (string currentRound)
            let current =
                following
                    (currentRound + 1)
                    previous.Digest
                    StructuredDecision.Confirmation
                    verdict
                    currentRound
                    currentHead
                    $"https://review/%d{currentRound}"
            comments.Add(StructuredDecisionTests.reviewComment (int64 (currentRound + 1)) current)
            previous <- current
        comments |> Seq.toList

    let private exhaustionWait claimGeneration head : ReviewWait.WaitReceipt =
        { Item = "FS-GG/.github#2819"
          ClaimGeneration = claimGeneration
          ReviewGeneration =
            ReviewWait.generationToken
                head
                ReviewWait.RepairConfirmation
                Protocol.reviewPolicy.MaxAutomatedRepairRounds
          Kind = ReviewWait.RepairConfirmation
          EnteredAt = DateTimeOffset.Parse("2026-08-22T10:00:00Z")
          ExpiresAt = DateTimeOffset.Parse("2026-08-22T11:00:00Z")
          EvidenceRef = "https://review/4" }

    let private withRetiredAcceptedGeneration (liveComments: Driver.ReviewComment list) =
        let retiredHead = String.replicate 40 "c"
        let retiredInitial = initial StructuredDecision.Pass retiredHead
        let retiredAcceptance =
            seal
                { StructuredDecisionTests.review 2 (Some retiredInitial.Digest)
                    StructuredDecision.Acceptance StructuredDecision.Accepted 0
                    (Some "https://review/1") (Some "https://review/1") with
                    HeadSha = retiredHead }
        let liveRecords =
            liveComments
            |> List.map (fun (comment: Driver.ReviewComment) ->
                Driver.decodeStructuredReview
                    (comment.Body.Substring("<!-- fsgg:review-decision/v2 -->".Length).Trim())
                |> function Ok record -> record | Error error -> failwith error)
        let rebuilt, _ =
            liveRecords
            |> List.indexed
            |> List.mapFold (fun previousDigest (index, record) ->
                let commentId = int64 (index + 3)
                let rewritten =
                    seal
                        { record with
                            Revision = index + 3
                            PreviousDigest = Some previousDigest
                            InitialReview = if index = 0 then None else Some "https://review/3"
                            PrecedingReview =
                                if index = 0 then None else Some $"https://review/%d{index + 2}" }
                StructuredDecisionTests.reviewComment commentId rewritten, rewritten.Digest)
                retiredAcceptance.Digest
        [ StructuredDecisionTests.reviewComment 1L retiredInitial
          StructuredDecisionTests.reviewComment 2L retiredAcceptance
          yield! rebuilt ]

    [<Fact>]
    let ``2819 shared ordinary exhaustion terminal set admits pass only after checks settle red`` () =
        let pass = ordinaryRoundThreeChain StructuredDecision.Pass
        Assert.False(Review.isOrdinaryExhaustionTerminal reviewedHead Types.PrPending pass)
        Assert.False(Review.isOrdinaryExhaustionTerminal reviewedHead Types.PrGreen pass)
        Assert.True(Review.isOrdinaryExhaustionTerminal reviewedHead Types.PrRed pass)

        let changesRequired = ordinaryRoundThreeChain StructuredDecision.ChangesRequired
        Assert.True(Review.isOrdinaryExhaustionTerminal reviewedHead Types.PrPending changesRequired)

        Assert.False(Review.isOrdinaryExhaustionTerminal movedHead Types.PrRed pass)

    [<Fact>]
    let ``2883 a passing round-four successor reaches host acceptance instead of exhaustion`` () =
        let passing =
            verdictOf Review.Ordinary movedHead
                (ordinaryRoundFourSuccessorChain StructuredDecision.Pass) Types.PrGreen
        Assert.Equal(Review.AwaitingHostAcceptance, passing.State)
        Assert.Equal(Review.RequestHostAcceptance, passing.NextAction)

        // Inversion: the same ordinal still exhausts when the successor requests another repair.
        let repairing =
            verdictOf Review.Ordinary movedHead
                (ordinaryRoundFourSuccessorChain StructuredDecision.ChangesRequired) Types.PrGreen
        Assert.Equal(Review.OrdinaryExhaustion, repairing.State)
        match repairing.NextAction with
        | Review.Park _ -> ()
        | action -> failwithf "expected an exhaustion park, got %A" action

    [<Fact>]
    let ``3014 shared exhaustion decision binds an admitted round-four terminal and its exact wait`` () =
        let chain = ordinaryRoundFourSuccessorChain StructuredDecision.ChangesRequired
        let receipt =
            { exhaustionWait "old-claim" movedHead with
                ReviewGeneration =
                    ReviewWait.generationToken movedHead ReviewWait.RepairConfirmation 4
                EvidenceRef = "https://review/5" }

        let decision =
            Review.decideOrdinaryExhaustion
                { Phase = Review.Ordinary
                  HeadSha = movedHead
                  CurrentClaimGeneration = "fresh-claim"
                  Checks = Types.PrGreen
                  Comments = chain
                  WaitState = Some(ReviewWait.Completed(receipt, receipt.EvidenceRef)) }

        Assert.Equal(Review.OrdinaryExhaustionDecision.CompletedOrdinaryExhaustion, decision)

    [<Fact>]
    let ``complete ordinary exhaustion decision owns checks wait generation and claim turnover`` () =
        let receipt: ReviewWait.WaitReceipt =
            { Item = "FS-GG/.github#2819"
              ClaimGeneration = "old-claim"
              ReviewGeneration =
                ReviewWait.generationToken
                    reviewedHead
                    ReviewWait.RepairConfirmation
                    Protocol.reviewPolicy.MaxAutomatedRepairRounds
              Kind = ReviewWait.RepairConfirmation
              EnteredAt = DateTimeOffset.Parse("2026-08-22T10:00:00Z")
              ExpiresAt = DateTimeOffset.Parse("2026-08-22T11:00:00Z")
              EvidenceRef = "https://review/4" }

        let decide checks claim waitState =
            Review.decideOrdinaryExhaustion
                { Phase = Review.Ordinary
                  HeadSha = reviewedHead
                  CurrentClaimGeneration = claim
                  Checks = checks
                  Comments = ordinaryRoundThreeChain StructuredDecision.Pass
                  WaitState = waitState }

        Assert.Equal(Review.OrdinaryExhaustionDecision.AwaitChecks, decide Types.PrPending "new-claim" (Some(ReviewWait.Completed(receipt, receipt.EvidenceRef))))
        Assert.Equal(Review.OrdinaryExhaustionDecision.HostAcceptanceEligible, decide Types.PrGreen "new-claim" (Some(ReviewWait.Completed(receipt, receipt.EvidenceRef))))
        Assert.Equal(Review.OrdinaryExhaustionDecision.CompletedOrdinaryExhaustion, decide Types.PrRed "new-claim" (Some(ReviewWait.Completed(receipt, receipt.EvidenceRef))))

        match decide Types.PrRed "old-claim" (Some(ReviewWait.Completed(receipt, receipt.EvidenceRef))) with
        | Review.OrdinaryExhaustionDecision.NotExhausted reason -> Assert.Contains("prior claim generation", reason)
        | other -> failwithf "same-claim wait was admitted as %A" other

    [<Fact>]
    let ``bounded ordinary exhaustion model keeps reducer projection and writer admission identical`` () =
        let oldClaim = "old-claim"
        let receipt = exhaustionWait oldClaim reviewedHead
        let waitNames = [ "waiting"; "completed"; "cancelled"; "expired"; "malformed" ]
        let claims = [ "same", oldClaim; "renewed", "renewed-claim"; "fresh", "fresh-claim" ]
        let checks = [ Types.PrPending; Types.PrGreen; Types.PrRed; Types.PrUnknown ]
        let verdicts =
            [ StructuredDecision.Pass
              StructuredDecision.ChangesRequired
              StructuredDecision.Accepted ]
        let original = verdictOf Review.Ordinary reviewedHead (ordinaryChain ()) Types.PrPending
        let projectionFacts = facts (ordinaryRoundThreeChain StructuredDecision.ChangesRequired) Types.PrRed
        let decisionClass = function
            | Review.OrdinaryExhaustionDecision.NotExhausted _ -> "notExhausted"
            | Review.OrdinaryExhaustionDecision.AwaitChecks -> "awaitChecks"
            | Review.OrdinaryExhaustionDecision.HostAcceptanceEligible -> "hostAcceptanceEligible"
            | Review.OrdinaryExhaustionDecision.CompletedOrdinaryExhaustion -> "completed"

        let mutable histories = 0
        for initialVerdict in verdicts do
            for terminalVerdict in verdicts do
                for round in 0..(Protocol.reviewPolicy.MaxAutomatedRepairRounds + 1) do
                    for check in checks do
                        for matchingHead in [ true; false ] do
                            for waitName in waitNames do
                                for claimName, currentClaim in claims do
                                    histories <- histories + 1
                                    let currentHead = if matchingHead then reviewedHead else movedHead
                                    let comments =
                                        boundedOrdinaryChain initialVerdict terminalVerdict round reviewedHead
                                    let terminalReceipt =
                                        { receipt with
                                            ReviewGeneration =
                                                ReviewWait.generationToken
                                                    reviewedHead
                                                    ReviewWait.RepairConfirmation
                                                    round }
                                    let waitState =
                                        match waitName with
                                        | "waiting" -> ReviewWait.Waiting terminalReceipt
                                        | "completed" -> ReviewWait.Completed(terminalReceipt, terminalReceipt.EvidenceRef)
                                        | "cancelled" -> ReviewWait.Cancelled(terminalReceipt, "https://cancel")
                                        | "expired" -> ReviewWait.Recoverable(terminalReceipt, "expired")
                                        | _ -> ReviewWait.Invalid [ "malformed wait" ]
                                    let decision =
                                        Review.decideOrdinaryExhaustion
                                            { Phase = Review.Ordinary
                                              HeadSha = currentHead
                                              CurrentClaimGeneration = currentClaim
                                              Checks = check
                                              Comments = comments
                                              WaitState = Some waitState }

                                    let passingTerminal =
                                        initialVerdict = StructuredDecision.ChangesRequired
                                        && terminalVerdict = StructuredDecision.Pass
                                        && round >= Protocol.reviewPolicy.MaxAutomatedRepairRounds
                                        && matchingHead
                                    let terminalChanges =
                                        initialVerdict = StructuredDecision.ChangesRequired
                                        && terminalVerdict = StructuredDecision.ChangesRequired
                                        && round >= Protocol.reviewPolicy.MaxAutomatedRepairRounds
                                        && matchingHead
                                    let terminal = terminalChanges || (passingTerminal && check = Types.PrRed)
                                    let expected =
                                        if terminal && waitName = "completed" && currentClaim <> oldClaim then "completed"
                                        elif passingTerminal && check = Types.PrPending then "awaitChecks"
                                        elif passingTerminal && check = Types.PrGreen then "hostAcceptanceEligible"
                                        else "notExhausted"

                                    let context =
                                        $"initial=%A{initialVerdict}; terminal=%A{terminalVerdict}; round=%d{round}; checks=%A{check}; matchingHead=%b{matchingHead}; wait=%s{waitName}; claim=%s{claimName}"
                                    Assert.True(decisionClass decision = expected, context)

                                    let writerAdmission =
                                        decision = Review.OrdinaryExhaustionDecision.CompletedOrdinaryExhaustion
                                    Assert.Equal((expected = "completed"), writerAdmission)

                                    let projected =
                                        Review.projectOrdinaryExhaustion
                                            decision
                                            (binding Review.Ordinary reviewedHead)
                                            projectionFacts
                                            original
                                    Assert.Equal(
                                        (expected = "completed"),
                                        (projected.State = Review.OrdinaryExhaustion)
                                    )

        Assert.Equal(5400, histories)

    [<Fact>]
    let ``ordinary exhaustion mutation controls kill head check wait claim and consumer forks`` () =
        let receipt = exhaustionWait "old-claim" reviewedHead
        let completed = ReviewWait.Completed(receipt, receipt.EvidenceRef)
        let chain = ordinaryRoundThreeChain StructuredDecision.Pass
        let decide head checks claim waitState =
            Review.decideOrdinaryExhaustion
                { Phase = Review.Ordinary
                  HeadSha = head
                  CurrentClaimGeneration = claim
                  Checks = checks
                  Comments = chain
                  WaitState = Some waitState }

        let authoritative = decide reviewedHead Types.PrRed "fresh-claim" completed
        Assert.Equal(Review.OrdinaryExhaustionDecision.CompletedOrdinaryExhaustion, authoritative)

        // Each right-hand decision is the answer a one-clause mutant would substitute. If the named
        // production clause disappears, the pair converges and this control turns red.
        Assert.NotEqual(authoritative, decide movedHead Types.PrRed "fresh-claim" completed)
        Assert.NotEqual(authoritative, decide reviewedHead Types.PrPending "fresh-claim" completed)
        Assert.NotEqual(authoritative, decide reviewedHead Types.PrRed "fresh-claim" (ReviewWait.Waiting receipt))
        Assert.NotEqual(authoritative, decide reviewedHead Types.PrRed "old-claim" completed)

        let original = verdictOf Review.Ordinary reviewedHead (ordinaryChain ()) Types.PrPending
        let projectionFacts = facts chain Types.PrRed
        let admitted =
            Review.projectOrdinaryExhaustion
                authoritative
                (binding Review.Ordinary reviewedHead)
                projectionFacts
                original
        let forked =
            Review.projectOrdinaryExhaustion
                (Review.OrdinaryExhaustionDecision.NotExhausted "mutant consumer fork")
                (binding Review.Ordinary reviewedHead)
                projectionFacts
                original
        Assert.NotEqual(admitted.State, forked.State)

    [<Fact>]
    let ``2819 pre-round pass refuses both shared predicate and exhaustion projection`` () =
        let comments =
            ordinaryRoundThreeChainWithPrefix StructuredDecision.Pass StructuredDecision.Pass
        Assert.False(Review.isOrdinaryExhaustionTerminal reviewedHead Types.PrRed comments)

        let original = verdictOf Review.Ordinary reviewedHead comments Types.PrRed
        let projected =
            Review.projectOrdinaryExhaustion
                (Review.OrdinaryExhaustionDecision.NotExhausted "pre-round pass")
                (binding Review.Ordinary reviewedHead)
                (facts comments Types.PrRed)
                original
        Assert.NotEqual(Review.OrdinaryExhaustion, projected.State)

    [<Fact>]
    let ``2819 retired accepted generation cannot poison live exhaustion projection`` () =
        let comments =
            ordinaryRoundThreeChain StructuredDecision.Pass
            |> withRetiredAcceptedGeneration
        let selected = Driver.liveReviewComments reviewedHead comments
        Assert.Empty(selected.StructuredErrors)
        Assert.Single(selected.Retired) |> ignore
        Assert.Equal(4, selected.Live.Length)
        Assert.True(Review.isOrdinaryExhaustionTerminal reviewedHead Types.PrRed comments)

        let original = verdictOf Review.Ordinary reviewedHead comments Types.PrRed
        let projected =
            Review.projectOrdinaryExhaustion
                Review.OrdinaryExhaustionDecision.CompletedOrdinaryExhaustion
                (binding Review.Ordinary reviewedHead)
                (facts comments Types.PrRed)
                original
        Assert.Equal(Review.OrdinaryExhaustion, projected.State)

    /// The REPAIR-phase chain. `RepairPhasePresent` is what puts `classify` down the repair branch, and
    /// the ledger requires a repair-phase record to carry `changes-required`; the round-1 confirmation
    /// then supplies the `pass` whose head the binding has moved off.
    let private repairChain () =
        let first = initial StructuredDecision.ChangesRequired reviewedHead
        let entered =
            following 2 first.Digest StructuredDecision.RepairPhase StructuredDecision.ChangesRequired 0
                reviewedHead "https://review/1"
        let pass =
            following 3 entered.Digest StructuredDecision.Confirmation StructuredDecision.Pass 1 reviewedHead
                "https://review/2"
        [ StructuredDecisionTests.reviewComment 1L first
          StructuredDecisionTests.reviewComment 2L entered
          StructuredDecisionTests.reviewComment 3L pass ]

    /// Both heads, in the text a host reads (AC3).
    let private assertNamesBothHeads (verdict: Review.Verdict) =
        match verdict.NextAction with
        | Review.DispatchSuccessor reason ->
            Assert.Contains(reviewedHead, reason)
            Assert.Contains(movedHead, reason)
        | other -> failwithf "expected dispatchSuccessor carrying the head divergence, got %A" other

    /// The state names the old code produced. Asserted as a REFUSAL on every moved-head leg, because
    /// "the new answer appeared" and "the optimistic answer is gone" are different claims and this row
    /// was filed for the second.
    let private assertNotOptimistic (verdict: Review.Verdict) =
        match verdict.State with
        | Review.PassedAwaitingChecks ->
            failwith "AC2: a pass bound to a superseded head was still reported as terminal (passedAwaitingChecks)"
        | Review.AwaitingHostAcceptance ->
            failwith "AC1: a pass bound to a superseded head still told the host to author an acceptance"
        | _ -> ()

        match verdict.NextAction with
        | Review.AwaitChecks -> failwith "AC2: the moved head still produced awaitChecks"
        | Review.RequestHostAcceptance -> failwith "AC1: the moved head still produced requestHostAcceptance"
        | _ -> ()

    // ── AC1/AC2/AC3 — the ordinary phase, both check states ────────────────────────────────────────

    /// AC2, and instance 3 exactly: checks not green, so the old code answered
    /// `passedAwaitingChecks`/`awaitChecks` with `actionReason: null` — the shape a worker whose only
    /// remaining red is the by-construction `claim-generation` reads as "you are clear, finish the cycle".
    [<Fact>]
    let ``2487 an ordinary pass at a moved head is not terminal and names both heads`` () =
        let verdict = verdictOf Review.Ordinary movedHead (ordinaryChain ()) Types.PrPending
        assertNotOptimistic verdict
        Assert.Equal(Review.AwaitingSuccessorReview 2, verdict.State)
        assertNamesBothHeads verdict

    /// AC1: the same chain with GREEN checks. This is the leg that produced the durable, refused
    /// acceptance markers of instance 1 — the host was told `requestHostAcceptance`, authored the marker,
    /// and the engine then refused it.
    [<Fact>]
    let ``2487 an ordinary pass at a moved head does not request host acceptance`` () =
        let verdict = verdictOf Review.Ordinary movedHead (ordinaryChain ()) Types.PrGreen
        assertNotOptimistic verdict
        Assert.Equal(Review.AwaitingSuccessorReview 2, verdict.State)
        assertNamesBothHeads verdict

    // ── AC1/AC2/AC3 — the repair phase, the second site ────────────────────────────────────────────

    /// THE SECOND SITE. The identical `LatestVerdict`/`LatestReviewedHeadSha` shape occurs once per
    /// phase, and a fix applied only to the ordinary branch would leave this one optimistic — the harder
    /// case to notice precisely because far fewer chains reach it.
    [<Fact>]
    let ``2487 a repair-phase pass at a moved head is not terminal and names both heads`` () =
        let verdict = verdictOf Review.Repair movedHead (repairChain ()) Types.PrPending
        assertNotOptimistic verdict
        Assert.Equal(Review.RepairPhaseActive 2, verdict.State)
        assertNamesBothHeads verdict

    [<Fact>]
    let ``2487 a repair-phase pass at a moved head does not request host acceptance`` () =
        let verdict = verdictOf Review.Repair movedHead (repairChain ()) Types.PrGreen
        assertNotOptimistic verdict
        Assert.Equal(Review.RepairPhaseActive 2, verdict.State)
        assertNamesBothHeads verdict

    // ── The positive controls: an unmoved head keeps every pre-existing answer ──────────────────────

    /// Without these four, a repair that flagged divergence unconditionally would pass every leg above.
    [<Fact>]
    let ``2487 an unmoved ordinary pass keeps both of its pre-existing answers`` () =
        let pending = verdictOf Review.Ordinary reviewedHead (ordinaryChain ()) Types.PrPending
        Assert.Equal(Review.PassedAwaitingChecks, pending.State)
        Assert.Equal(Review.AwaitChecks, pending.NextAction)

        let green = verdictOf Review.Ordinary reviewedHead (ordinaryChain ()) Types.PrGreen
        Assert.Equal(Review.AwaitingHostAcceptance, green.State)
        Assert.Equal(Review.RequestHostAcceptance, green.NextAction)

    [<Fact>]
    let ``2487 an unmoved repair-phase pass keeps both of its pre-existing answers`` () =
        let pending = verdictOf Review.Repair reviewedHead (repairChain ()) Types.PrPending
        Assert.Equal(Review.RepairPhaseActive 2, pending.State)
        Assert.Equal(Review.AwaitChecks, pending.NextAction)

        let green = verdictOf Review.Repair reviewedHead (repairChain ()) Types.PrGreen
        Assert.Equal(Review.RepairPhaseActive 2, green.State)
        Assert.Equal(Review.RequestHostAcceptance, green.NextAction)

    // ── The recovery route, and the row's Outcome ──────────────────────────────────────────────────

    /// A chain whose critic despawned after passing a head the tree then moved off is in exactly the
    /// position .github#2417 built the succession grant for. The `changes-required` sibling has always
    /// offered it; leaving it out of the `pass` arm would have made this the one place in the protocol
    /// where a moved head has no route at all.
    [<Fact>]
    let ``2487 a valid succession grant still reaches the moved-head pass`` () =
        let granted: Review.CriticSuccessionReceipt =
            { OriginalCriticIdentity = "tern-42"
              SuccessorCriticIdentity = "fresh-critic-9b63"
              GrantedBy = "host-9b63"
              Reason = "the reviewing critic despawned"
              CandidateHeadSha = movedHead }

        match Review.inspect (binding Review.Ordinary movedHead) (facts (ordinaryChain ()) Types.PrPending) (Some granted) None with
        | Error errors -> failwithf "review refused a well-formed binding: %A" errors
        | Ok verdict ->
            Assert.Equal(Review.AwaitingSuccessorReview 2, verdict.State)
            Assert.Equal(Review.EnterCriticSuccession granted, verdict.NextAction)

    /// THE ROW'S OUTCOME, as one test: the same engine no longer gives two different answers about the
    /// same state depending on which door you knock on.
    ///
    /// The left half is what a host was told before it acted. The right half is what the ACCEPTANCE path
    /// says about the identical chain once the host has authored the marker `review` invited — the
    /// durable, permanently-fenced marker instance 1 records. They now agree, and the cheap door is no
    /// longer the optimistic one.
    [<Fact>]
    let ``2487 the inspect answer and the acceptance answer agree about a moved head`` () =
        let first = initial StructuredDecision.Pass reviewedHead
        let accepted =
            following 2 first.Digest StructuredDecision.Acceptance StructuredDecision.Accepted 0 reviewedHead
                "https://review/1"

        let beforeAcceptance = verdictOf Review.Ordinary movedHead [ StructuredDecisionTests.reviewComment 1L first ] Types.PrPending
        assertNotOptimistic beforeAcceptance
        Assert.Equal(Review.AwaitingSuccessorReview 1, beforeAcceptance.State)

        let afterAcceptance =
            verdictOf Review.Ordinary movedHead
                [ StructuredDecisionTests.reviewComment 1L first
                  StructuredDecisionTests.reviewComment 2L accepted ]
                Types.PrPending

        match afterAcceptance.State with
        | Review.MalformedEvidence errors ->
            Assert.Contains(
                errors,
                fun (problem: string) -> problem.Contains "bound to a different head than the current commit")
        | other ->
            failwithf "the acceptance path stopped refusing a chain bound to a superseded head: %A" other
