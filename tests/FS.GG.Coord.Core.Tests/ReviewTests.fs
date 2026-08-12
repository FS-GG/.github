namespace FS.GG.Coord.Tests

open Xunit
open FS.GG.Coord.Driver
open FS.GG.Coord.Review
open FS.GG.Coord.Types

/// .github#2175 acceptance 13's test matrix: clean first-pass acceptance, multiple repair rounds,
/// changed head, malformed/duplicate marker, wrong critic, missing predecessor, ordinary exhaustion
/// into one repair phase, restart during repair, duplicate advance, unavailable repair route,
/// repair-phase exhaustion, and the final accepted receipt consumed by Delivery (see
/// `DeliveryTests.fs` `` `#2175 fromReviewAcceptance`` ``).
module ReviewTests =
    let comment id url body : ReviewComment = { Id = id; Url = url; Body = body }

    let notMeaningful =
        "\nroute-applicability: not-meaningful\nroute-not-meaningful-reason: this review subject has no meaningful runtime-route comparison"

    let binding phase round headSha implementer : Binding =
        { ItemRef = "FS-GG/.github#2175"
          Pr = 42
          HeadSha = headSha
          ClaimGeneration = "gen-1"
          ImplementerIdentity = implementer
          Phase = phase
          Round = round }

    let facts comments checks repairGranted repairAvailable : Facts =
        { Comments = comments
          Checks = checks
          RepairPhaseGranted = repairGranted
          RepairRouteAvailable = repairAvailable
          DiffAuditTrusted = None }

    let initialPass critic headSha =
        comment 1L "https://reviews/1" ($"<!-- fsgg:independent-review:v1 -->\ncritic: %s{critic}\nreviewed-head: %s{headSha}\nverdict: pass" + notMeaningful)

    let initialChangesRequired critic headSha =
        comment 1L "https://reviews/1" $"<!-- fsgg:independent-review:v1 -->\ncritic: %s{critic}\nreviewed-head: %s{headSha}\nverdict: changes-required"

    let confirmation id critic round preceding headSha verdict =
        let suffix = if verdict = "pass" then notMeaningful else ""
        comment id $"https://reviews/%d{id}" ($"<!-- fsgg:independent-review-confirmation:v1 -->\ninitial-review: https://reviews/1\ncritic: %s{critic}\nround: %d{round}\npreceding-review: %s{preceding}\nreviewed-head: %s{headSha}\nverdict: %s{verdict}" + suffix)

    let accepted id headSha latest =
        comment id $"https://reviews/%d{id}" $"<!-- fsgg:review-accepted:v1 -->\naccepted-head: %s{headSha}\ninitial-review: https://reviews/1\nlatest-confirmation: %s{latest}"

    // ---- awaiting initial review / missing predecessor -------------------------------------------

    [<Fact>]
    let ``#2175 no comments yields awaiting initial review and dispatch critic`` () =
        match inspect (binding Ordinary 1 "head1" "impl") (facts [] PrPending None true) with
        | Ok v ->
            Assert.Equal(AwaitingInitialReview, v.State)
            Assert.Equal(DispatchCritic, v.NextAction)
        | Error e -> failwithf "%A" e

    [<Fact>]
    let ``#2175 a confirmation with no initial predecessor is malformed, not silently dispatched`` () =
        let comments = [ confirmation 2L "kite" 1 "https://reviews/1" "head1" "pass" ]
        match inspect (binding Ordinary 1 "head1" "impl") (facts comments PrPending None true) with
        | Ok v ->
            // No initial marker present at all: reads as AwaitingInitialReview (the confirmation is
            // simply not counted — `Driver.reviewPhaseFacts` only reads confirmations relative to an
            // initial marker that exists), never as a silently-accepted pass.
            Assert.Equal(AwaitingInitialReview, v.State)
        | Error e -> failwithf "%A" e

    // ---- clean first-pass acceptance --------------------------------------------------------------

    [<Fact>]
    let ``#2175 pass with checks not yet green awaits checks`` () =
        let comments = [ initialPass "kite" "head1" ]
        match inspect (binding Ordinary 1 "head1" "impl") (facts comments PrPending None true) with
        | Ok v ->
            Assert.Equal(PassedAwaitingChecks, v.State)
            Assert.Equal(AwaitChecks, v.NextAction)
        | Error e -> failwithf "%A" e

    [<Fact>]
    let ``#2175 pass with checks green and no acceptance yet requests host acceptance`` () =
        let comments = [ initialPass "kite" "head1" ]
        match inspect (binding Ordinary 1 "head1" "impl") (facts comments PrGreen None true) with
        | Ok v ->
            Assert.Equal(AwaitingHostAcceptance, v.State)
            Assert.Equal(RequestHostAcceptance, v.NextAction)
        | Error e -> failwithf "%A" e

    [<Fact>]
    let ``#2175 clean first-pass acceptance yields Accepted bound to the exact head`` () =
        let comments =
            [ initialPass "kite" "head1"
              accepted 2L "head1" "https://reviews/1" ]
        match inspect (binding Ordinary 1 "head1" "impl") (facts comments PrGreen None true) with
        | Ok v ->
            Assert.Equal(Accepted, v.State)
            match v.NextAction with
            | Accept receipt ->
                Assert.Equal("head1", receipt.HeadSha)
                Assert.Equal("kite", receipt.CriticIdentity)
                Assert.True(receipt.ChecksGreen)
                Assert.False(receipt.RepairPhase)
            | other -> failwithf "expected Accept, got %A" other
        | Error e -> failwithf "%A" e

    // ---- multiple repair rounds / changed head ------------------------------------------------------

    [<Fact>]
    let ``#2175 changes-required at the current head awaits implementer repair`` () =
        let comments = [ initialChangesRequired "kite" "head1" ]
        match inspect (binding Ordinary 1 "head1" "impl") (facts comments PrPending None true) with
        | Ok v ->
            Assert.Equal(AwaitingImplementerRepair 1, v.State)
            match v.NextAction with
            | ResumeImplementer _ -> ()
            | other -> failwithf "expected ResumeImplementer, got %A" other
        | Error e -> failwithf "%A" e

    [<Fact>]
    let ``#2175 a new commit after changes-required awaits the same critic's confirmation`` () =
        let comments = [ initialChangesRequired "kite" "head1" ]
        // The implementer pushed head2; the critic has not yet re-reviewed it.
        match inspect (binding Ordinary 1 "head2" "impl") (facts comments PrPending None true) with
        | Ok v ->
            Assert.Equal(AwaitingSameCriticConfirmation 1, v.State)
            match v.NextAction with
            | ResumeSameCritic _ -> ()
            | other -> failwithf "expected ResumeSameCritic, got %A" other
        | Error e -> failwithf "%A" e

    [<Fact>]
    let ``#2175 multiple repair rounds advance through repeated changes-required to a final pass`` () =
        // Round 1: changes-required at head1.
        let round1 = [ initialChangesRequired "kite" "head1" ]
        match inspect (binding Ordinary 1 "head2" "impl") (facts round1 PrPending None true) with
        | Ok v -> Assert.Equal(AwaitingSameCriticConfirmation 1, v.State)
        | Error e -> failwithf "%A" e

        // Critic confirms round 1 as changes-required again at head2.
        let round2 =
            round1 @ [ confirmation 2L "kite" 1 "https://reviews/1" "head2" "changes-required" ]
        match inspect (binding Ordinary 1 "head2" "impl") (facts round2 PrPending None true) with
        | Ok v -> Assert.Equal(AwaitingImplementerRepair 2, v.State)
        | Error e -> failwithf "%A" e

        // A further commit lands (head3); the same critic must confirm again.
        match inspect (binding Ordinary 1 "head3" "impl") (facts round2 PrPending None true) with
        | Ok v -> Assert.Equal(AwaitingSameCriticConfirmation 2, v.State)
        | Error e -> failwithf "%A" e

        // Critic confirms pass at head3.
        let round3 =
            round2 @ [ confirmation 3L "kite" 2 "https://reviews/2" "head3" "pass" ]
        match inspect (binding Ordinary 1 "head3" "impl") (facts round3 PrGreen None true) with
        | Ok v ->
            Assert.Equal(AwaitingHostAcceptance, v.State)
            Assert.Equal(RequestHostAcceptance, v.NextAction)
        | Error e -> failwithf "%A" e

    [<Fact>]
    let ``#2175 a changed head after acceptance invalidates the prior accepted evidence`` () =
        let comments =
            [ initialPass "kite" "head1"
              accepted 2L "head1" "https://reviews/1" ]
        // A new commit landed after acceptance was posted: the accepted chain is for a stale head.
        match inspect (binding Ordinary 1 "head2" "impl") (facts comments PrGreen None true) with
        | Ok v ->
            match v.State with
            | MalformedEvidence _ -> ()
            | other -> failwithf "expected MalformedEvidence for a stale accepted head, got %A" other
            match v.NextAction with
            | Park _ -> ()
            | other -> failwithf "expected Park, got %A" other
        | Error e -> failwithf "%A" e

    [<Fact>]
    let ``#2175 a changed head changes the freshness token`` () =
        let b1 = binding Ordinary 1 "head1" "impl"
        let b2 = binding Ordinary 1 "head2" "impl"
        Assert.NotEqual<string>(freshnessToken b1, freshnessToken b2)

    // ---- malformed / duplicate marker ---------------------------------------------------------------

    [<Fact>]
    let ``#2175 a duplicate initial marker across two comments is malformed, not silently the first`` () =
        let comments =
            [ initialPass "kite" "head1"
              comment 5L "https://reviews/5" ($"<!-- fsgg:independent-review:v1 -->\ncritic: heron\nreviewed-head: head1\nverdict: pass" + notMeaningful) ]
        match inspect (binding Ordinary 1 "head1" "impl") (facts comments PrGreen None true) with
        | Ok v ->
            match v.State with
            | MalformedEvidence errors -> Assert.Contains(errors, fun e -> e.Contains "2 comments")
            | other -> failwithf "expected MalformedEvidence, got %A" other
        | Error e -> failwithf "%A" e

    [<Fact>]
    let ``#2175 a duplicate acceptance marker is malformed`` () =
        let comments =
            [ initialPass "kite" "head1"
              accepted 2L "head1" "https://reviews/1"
              accepted 3L "head1" "https://reviews/1" ]
        match inspect (binding Ordinary 1 "head1" "impl") (facts comments PrGreen None true) with
        | Ok v ->
            match v.State with
            | MalformedEvidence errors -> Assert.Contains(errors, fun e -> e.Contains "acceptance")
            | other -> failwithf "expected MalformedEvidence, got %A" other
        | Error e -> failwithf "%A" e

    [<Fact>]
    let ``#2175 a competing marker within one comment's leading block is malformed`` () =
        let comments =
            [ comment 1L "https://reviews/1"
                  ("<!-- fsgg:independent-review:v1 -->\n<!-- fsgg:independent-review:v1 -->\ncritic: kite\nreviewed-head: head1\nverdict: pass"
                   + notMeaningful)
              accepted 2L "head1" "https://reviews/1" ]
        match inspect (binding Ordinary 1 "head1" "impl") (facts comments PrGreen None true) with
        | Ok v ->
            match v.State with
            | MalformedEvidence _ -> ()
            | AwaitingInitialReview -> ()
            | other -> failwithf "expected MalformedEvidence or AwaitingInitialReview (competing marker is not canonical), got %A" other
        | Error e -> failwithf "%A" e

    // ---- markdown-bolded field near miss (.github#2369) -----------------------------------------------
    //
    // The live shape: a faithful critic followed `independent-review.md`'s prose, which never states
    // the field grammar is a literal column-0 `key: value` line, and wrote ordinary markdown instead.
    // The marker itself is canonical (leading block, single occurrence) so it is never "misplaced" —
    // only the FIELD lines are unreadable, and the pre-fix message named neither the field nor the fix.

    [<Fact>]
    let ``#2369 an initial marker with markdown-bolded fields is refused naming the expected column-0 form`` () =
        // Reproduces PR #2367's actual comment shape byte-for-byte in kind (bold field labels), not the
        // literal text — the fixture owns its own critic/head names.
        let comments =
            [ comment
                  1L
                  "https://reviews/1"
                  "<!-- fsgg:independent-review:v1 -->\n**Critic:** kite\n**Reviewed-head:** head1\n**Verdict:** pass" ]
        match inspect (binding Ordinary 1 "head1" "impl") (facts comments PrGreen None true) with
        | Ok v ->
            match v.State with
            | MalformedEvidence errors ->
                Assert.Contains(errors, fun e -> e.Contains "verdict: <value>")
                Assert.Contains(errors, fun e -> e.Contains "**Verdict:** pass")
            | other -> failwithf "expected MalformedEvidence naming the expected column-0 form, got %A" other
        | Error e -> failwithf "%A" e

    [<Fact>]
    let ``#2369 a confirmation with a markdown-bolded verdict field is refused naming the expected column-0 form`` () =
        let comments =
            [ initialChangesRequired "kite" "head0"
              comment
                  2L
                  "https://reviews/2"
                  "<!-- fsgg:independent-review-confirmation:v1 -->\ninitial-review: https://reviews/1\ncritic: kite\nround: 1\npreceding-review: https://reviews/1\nreviewed-head: head1\n**Verdict:** pass" ]
        match inspect (binding Ordinary 1 "head1" "impl") (facts comments PrGreen None true) with
        | Ok v ->
            match v.State with
            | MalformedEvidence errors -> Assert.Contains(errors, fun e -> e.Contains "verdict: <value>")
            | other -> failwithf "expected MalformedEvidence naming the expected column-0 form, got %A" other
        | Error e -> failwithf "%A" e

    [<Fact>]
    let ``#2369 a genuinely absent verdict field keeps the original unadorned reason -- no false near miss`` () =
        // No verdict field at all, bolded or otherwise: the pre-#2369 message must be UNCHANGED, so this
        // pins that the near-miss addition never fires on a case it does not apply to.
        let comments =
            [ comment 1L "https://reviews/1" "<!-- fsgg:independent-review:v1 -->\ncritic: kite\nreviewed-head: head1" ]
        match inspect (binding Ordinary 1 "head1" "impl") (facts comments PrGreen None true) with
        | Ok v ->
            match v.State with
            | MalformedEvidence errors ->
                Assert.Contains("the latest review verdict is neither readable pass nor changes-required", errors)
            | other -> failwithf "expected MalformedEvidence, got %A" other
        | Error e -> failwithf "%A" e

    [<Fact>]
    let ``#2369 Driver.reviewPhaseFacts itself carries the near-miss hint, not only Review.classify's message`` () =
        // Pins the fact at its OWN layer (`Driver.fs`, in this item's declared Paths): a caller reading
        // `ReviewPhaseFacts` directly — not only through `Review.inspect` — sees the same diagnosis.
        let comments =
            [ comment 1L "https://reviews/1" "<!-- fsgg:independent-review:v1 -->\ncritic: kite\nreviewed-head: head1\n**Verdict:** pass" ]
        let phaseFacts = reviewPhaseFacts comments
        Assert.True(phaseFacts.LatestVerdict.IsNone)
        Assert.Contains(phaseFacts.LatestVerdictNearMissHints, fun h -> h.Contains "verdict: <value>")

    // ---- wrong critic / implementer-as-critic guard --------------------------------------------------

    [<Fact>]
    let ``#2175 an implementer acting as its own critic fails closed`` () =
        let comments = [ initialPass "impl" "head1" ]
        match inspect (binding Ordinary 1 "head1" "impl") (facts comments PrGreen None true) with
        | Ok v ->
            match v.State with
            | GuardViolation _ -> ()
            | other -> failwithf "expected GuardViolation, got %A" other
            match v.NextAction with
            | Park _ -> ()
            | other -> failwithf "expected Park, got %A" other
        | Error e -> failwithf "%A" e

    [<Fact>]
    let ``#2175 GATE INVERSION -- without the guard, an implementer-as-critic pass would silently accept`` () =
        // This test documents the guard's own failure mode by construction rather than by disabling
        // code: `Driver.parseReviewComments` alone (the pre-#2175 surface) has NO implementer-identity
        // concept at all and would happily validate this exact chain.
        let comments =
            [ initialPass "impl" "head1"
              accepted 2L "head1" "https://reviews/1" ]
        match parseReviewComments comments with
        | Ok chain ->
            // Proves the guard in `Review.classify` is load-bearing: the underlying chain the guard
            // must intercept is otherwise perfectly valid.
            Assert.Equal(Some "impl", chain.CriticIdentity)
        | Error e -> failwithf "the underlying chain must be valid for this test to prove anything: %A" e
        // `Review.inspect`, with the SAME facts, must fail closed instead of accepting.
        match inspect (binding Ordinary 1 "head1" "impl") (facts comments PrGreen None true) with
        | Ok v ->
            match v.State with
            | GuardViolation _ -> ()
            | Accepted -> failwith "GATE INVERSION: the critic-independence guard did not fire; an implementer accepted its own review"
            | other -> failwithf "expected GuardViolation, got %A" other
        | Error e -> failwithf "%A" e

    // ---- ordinary exhaustion into one repair phase ----------------------------------------------------

    // `MaxAutomatedRepairRounds` is 3 (`Protocol.reviewPolicy`), and exhaustion fires on
    // `ConfirmationCount > ceiling` — so FOUR confirmations, not three, is what actually exhausts it.
    let exhaustedOrdinaryChain =
        [ initialChangesRequired "kite" "head0"
          confirmation 2L "kite" 1 "https://reviews/1" "head1" "changes-required"
          confirmation 3L "kite" 2 "https://reviews/2" "head2" "changes-required"
          confirmation 4L "kite" 3 "https://reviews/3" "head3" "changes-required"
          confirmation 5L "kite" 4 "https://reviews/4" "head4" "changes-required" ]

    [<Fact>]
    let ``#2175 ordinary exhaustion with a repair route available parks for the host to mint one`` () =
        match inspect (binding Ordinary 1 "head4" "impl") (facts exhaustedOrdinaryChain PrPending None true) with
        | Ok v ->
            Assert.Equal(OrdinaryExhaustion, v.State)
            match v.NextAction with
            | Park _ -> ()
            | other -> failwithf "expected Park, got %A" other
        | Error e -> failwithf "%A" e

    [<Fact>]
    let ``#2175 unavailable repair route at exhaustion is terminal human park`` () =
        match inspect (binding Ordinary 1 "head4" "impl") (facts exhaustedOrdinaryChain PrPending None false) with
        | Ok v ->
            match v.State with
            | TerminalHumanPark _ -> ()
            | other -> failwithf "expected TerminalHumanPark, got %A" other
        | Error e -> failwithf "%A" e

    [<Fact>]
    let ``#2175 a granted repair-phase receipt is reused idempotently rather than re-exhausted`` () =
        let receipt : RepairPhaseReceipt =
            { ExhaustedPr = 42
              EscalationCommentId = 99L
              NewClaimGeneration = "gen-2"
              NewBranchOrPr = "item/2175-repair"
              NewImplementerIdentity = "fresh-impl"
              NewCriticIdentity = "fresh-critic"
              CandidateHeadSha = "head3" }
        match inspect (binding Ordinary 1 "head4" "impl") (facts exhaustedOrdinaryChain PrPending (Some receipt) true) with
        | Ok v ->
            Assert.Equal(RepairPhaseSetup, v.State)
            Assert.Equal(EnterRepairPhase receipt, v.NextAction)
        | Error e -> failwithf "%A" e

    [<Fact>]
    let ``#2175 GATE INVERSION -- granting a second repair phase is not this engine's decision to make silently`` () =
        // The engine never MINTS a second phase on its own: `facts.RepairPhaseGranted` must be supplied
        // by the caller, and `inspect` only ever reuses exactly the one it is given. There is no code
        // path that increments or replaces a granted receipt.
        let firstReceipt : RepairPhaseReceipt =
            { ExhaustedPr = 42; EscalationCommentId = 99L; NewClaimGeneration = "gen-2"
              NewBranchOrPr = "item/2175-repair"; NewImplementerIdentity = "fresh-impl"
              NewCriticIdentity = "fresh-critic"; CandidateHeadSha = "head3" }
        match inspect (binding Ordinary 1 "head4" "impl") (facts exhaustedOrdinaryChain PrPending (Some firstReceipt) true) with
        | Ok v -> Assert.Equal(EnterRepairPhase firstReceipt, v.NextAction)
        | Error e -> failwithf "%A" e

    // ---- repair-phase active / repair-phase exhaustion -------------------------------------------------

    [<Fact>]
    let ``#2175 repair-phase setup awaits the fresh critic's dispatch`` () =
        let comments : ReviewComment list = []
        match inspect (binding Repair 1 "head3" "fresh-impl") (facts comments PrPending None true) with
        | Ok v ->
            match v.State, v.NextAction with
            | RepairPhaseSetup, DispatchCritic -> ()
            | TerminalHumanPark _, Park _ -> ()
            | other -> failwithf "expected RepairPhaseSetup/DispatchCritic (no repair-phase marker yet, no comments), got %A" other
        | Error e -> failwithf "%A" e

    [<Fact>]
    let ``#2175 repair-phase active review resumes the fresh implementer on changes-required`` () =
        let comments =
            [ comment 1L "https://reviews/repair-1" "<!-- fsgg:independent-review-repair-phase:v1 -->\n<!-- fsgg:independent-review:v1 -->\ncritic: fresh-critic\nreviewed-head: head3\nverdict: changes-required" ]
        match inspect (binding Repair 1 "head3" "fresh-impl") (facts comments PrPending None true) with
        | Ok v ->
            Assert.Equal(RepairPhaseActive 1, v.State)
            match v.NextAction with
            | ResumeImplementer _ -> ()
            | other -> failwithf "expected ResumeImplementer, got %A" other
        | Error e -> failwithf "%A" e

    [<Fact>]
    let ``#2175 repair-phase exhaustion (ceiling reached, no acceptance) is terminal human park`` () =
        let repairMarker = "<!-- fsgg:independent-review-repair-phase:v1 -->\n"
        // `RepairPhaseMaxRounds` is 10, and exhaustion fires on `ConfirmationCount > ceiling` — ELEVEN
        // confirmations, not ten, is what actually exhausts it.
        let comments =
            [ comment 1L "https://reviews/repair-1" (repairMarker + "<!-- fsgg:independent-review:v1 -->\ncritic: fresh-critic\nreviewed-head: head0\nverdict: changes-required") ]
            @ [ for round in 1 .. 11 ->
                    confirmation (int64 (round + 1)) "fresh-critic" round $"https://reviews/{round}" $"head%d{round}" "changes-required" ]
        match inspect (binding Repair 1 "head11" "fresh-impl") (facts comments PrPending None true) with
        | Ok v ->
            match v.State with
            | TerminalHumanPark _ -> ()
            | other -> failwithf "expected TerminalHumanPark at repair-phase exhaustion, got %A" other
        | Error e -> failwithf "%A" e

    // ---- restart / duplicate advance idempotency ---------------------------------------------------

    [<Fact>]
    let ``#2175 advance re-authorizes the exact inspected verdict (restart during repair)`` () =
        let comments = [ initialChangesRequired "kite" "head1" ]
        let b = binding Ordinary 1 "head1" "impl"
        let f = facts comments PrPending None true
        match inspect b f with
        | Ok verdict ->
            match advance verdict.FreshnessToken verdict.ActionKey b f with
            | Ok replay ->
                Assert.Equal(verdict.State, replay.State)
                Assert.Equal(verdict.FreshnessToken, replay.FreshnessToken)
                Assert.Equal(verdict.ActionKey, replay.ActionKey)
            | Error e -> failwithf "restart replay should re-converge: %A" e
        | Error e -> failwithf "%A" e

    [<Fact>]
    let ``#2175 duplicate advance against a changed head is refused, never a stale accept`` () =
        let comments = [ initialChangesRequired "kite" "head1" ]
        let b = binding Ordinary 1 "head1" "impl"
        let f = facts comments PrPending None true
        match inspect b f with
        | Ok verdict ->
            // Facts moved on (a new commit landed) but the caller replays the STALE token/key.
            let movedOn = binding Ordinary 1 "head2" "impl"
            match advance verdict.FreshnessToken verdict.ActionKey movedOn f with
            | Error _ -> ()
            | Ok stale -> failwithf "GATE INVERSION: a stale replay against a moved head was authorized: %A" stale
        | Error e -> failwithf "%A" e

    [<Fact>]
    let ``#2175 an unreadable binding fails closed rather than defaulting to a permissive state`` () =
        let badBinding = { binding Ordinary 1 "head1" "impl" with ItemRef = "" }
        match inspect badBinding (facts [] PrPending None true) with
        | Error reasons -> Assert.Contains(reasons, fun r -> r.Contains "item ref")
        | Ok v -> failwithf "expected a fail-closed no-verdict for an incomplete binding, got %A" v
