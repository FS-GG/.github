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

    /// .github#2417: a critic-succession receipt naming `original`, granting `successor` as the fresh
    /// critic, `grantedBy` as the accountable identity, and bound to `headSha`.
    let successionReceipt original successor grantedBy headSha : CriticSuccessionReceipt =
        { OriginalCriticIdentity = original
          SuccessorCriticIdentity = successor
          GrantedBy = grantedBy
          Reason = "the original critic despawned before confirming the new commit"
          CandidateHeadSha = headSha }

    // ---- awaiting initial review / missing predecessor -------------------------------------------

    [<Fact>]
    let ``#2175 no comments yields awaiting initial review and dispatch critic`` () =
        match inspect (binding Ordinary 1 "head1" "impl") (facts [] PrPending None true) None None with
        | Ok v ->
            Assert.Equal(AwaitingInitialReview, v.State)
            Assert.Equal(DispatchCritic, v.NextAction)
        | Error e -> failwithf "%A" e

    [<Fact>]
    let ``#2175 a confirmation with no initial predecessor is malformed, not silently dispatched`` () =
        let comments = [ confirmation 2L "kite" 1 "https://reviews/1" "head1" "pass" ]
        match inspect (binding Ordinary 1 "head1" "impl") (facts comments PrPending None true) None None with
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
        match inspect (binding Ordinary 1 "head1" "impl") (facts comments PrPending None true) None None with
        | Ok v ->
            Assert.Equal(PassedAwaitingChecks, v.State)
            Assert.Equal(AwaitChecks, v.NextAction)
        | Error e -> failwithf "%A" e

    [<Fact>]
    let ``#2175 pass with checks green and no acceptance yet requests host acceptance`` () =
        let comments = [ initialPass "kite" "head1" ]
        match inspect (binding Ordinary 1 "head1" "impl") (facts comments PrGreen None true) None None with
        | Ok v ->
            Assert.Equal(AwaitingHostAcceptance, v.State)
            Assert.Equal(RequestHostAcceptance, v.NextAction)
        | Error e -> failwithf "%A" e

    [<Fact>]
    let ``#2175 clean first-pass acceptance yields Accepted bound to the exact head`` () =
        let comments =
            [ initialPass "kite" "head1"
              accepted 2L "head1" "https://reviews/1" ]
        match inspect (binding Ordinary 1 "head1" "impl") (facts comments PrGreen None true) None None with
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
        match inspect (binding Ordinary 1 "head1" "impl") (facts comments PrPending None true) None None with
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
        match inspect (binding Ordinary 1 "head2" "impl") (facts comments PrPending None true) None None with
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
        match inspect (binding Ordinary 1 "head2" "impl") (facts round1 PrPending None true) None None with
        | Ok v -> Assert.Equal(AwaitingSameCriticConfirmation 1, v.State)
        | Error e -> failwithf "%A" e

        // Critic confirms round 1 as changes-required again at head2.
        let round2 =
            round1 @ [ confirmation 2L "kite" 1 "https://reviews/1" "head2" "changes-required" ]
        match inspect (binding Ordinary 1 "head2" "impl") (facts round2 PrPending None true) None None with
        | Ok v -> Assert.Equal(AwaitingImplementerRepair 2, v.State)
        | Error e -> failwithf "%A" e

        // A further commit lands (head3); the same critic must confirm again.
        match inspect (binding Ordinary 1 "head3" "impl") (facts round2 PrPending None true) None None with
        | Ok v -> Assert.Equal(AwaitingSameCriticConfirmation 2, v.State)
        | Error e -> failwithf "%A" e

        // Critic confirms pass at head3.
        let round3 =
            round2 @ [ confirmation 3L "kite" 2 "https://reviews/2" "head3" "pass" ]
        match inspect (binding Ordinary 1 "head3" "impl") (facts round3 PrGreen None true) None None with
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
        match inspect (binding Ordinary 1 "head2" "impl") (facts comments PrGreen None true) None None with
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
        match inspect (binding Ordinary 1 "head1" "impl") (facts comments PrGreen None true) None None with
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
        match inspect (binding Ordinary 1 "head1" "impl") (facts comments PrGreen None true) None None with
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
        match inspect (binding Ordinary 1 "head1" "impl") (facts comments PrGreen None true) None None with
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
        match inspect (binding Ordinary 1 "head1" "impl") (facts comments PrGreen None true) None None with
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
        match inspect (binding Ordinary 1 "head1" "impl") (facts comments PrGreen None true) None None with
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
        match inspect (binding Ordinary 1 "head1" "impl") (facts comments PrGreen None true) None None with
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
        match inspect (binding Ordinary 1 "head1" "impl") (facts comments PrGreen None true) None None with
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
        match inspect (binding Ordinary 1 "head1" "impl") (facts comments PrGreen None true) None None with
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
        match inspect (binding Ordinary 1 "head4" "impl") (facts exhaustedOrdinaryChain PrPending None true) None None with
        | Ok v ->
            Assert.Equal(OrdinaryExhaustion, v.State)
            match v.NextAction with
            | Park _ -> ()
            | other -> failwithf "expected Park, got %A" other
        | Error e -> failwithf "%A" e

    [<Fact>]
    let ``#2175 unavailable repair route at exhaustion is terminal human park`` () =
        match inspect (binding Ordinary 1 "head4" "impl") (facts exhaustedOrdinaryChain PrPending None false) None None with
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
        match inspect (binding Ordinary 1 "head4" "impl") (facts exhaustedOrdinaryChain PrPending (Some receipt) true) None None with
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
        match inspect (binding Ordinary 1 "head4" "impl") (facts exhaustedOrdinaryChain PrPending (Some firstReceipt) true) None None with
        | Ok v -> Assert.Equal(EnterRepairPhase firstReceipt, v.NextAction)
        | Error e -> failwithf "%A" e

    // ---- repair-phase active / repair-phase exhaustion -------------------------------------------------

    [<Fact>]
    let ``#2175 repair-phase setup awaits the fresh critic's dispatch`` () =
        let comments : ReviewComment list = []
        match inspect (binding Repair 1 "head3" "fresh-impl") (facts comments PrPending None true) None None with
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
        match inspect (binding Repair 1 "head3" "fresh-impl") (facts comments PrPending None true) None None with
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
        match inspect (binding Repair 1 "head11" "fresh-impl") (facts comments PrPending None true) None None with
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
        match inspect b f None None with
        | Ok verdict ->
            match advance verdict.FreshnessToken verdict.ActionKey b f None None with
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
        match inspect b f None None with
        | Ok verdict ->
            // Facts moved on (a new commit landed) but the caller replays the STALE token/key.
            let movedOn = binding Ordinary 1 "head2" "impl"
            match advance verdict.FreshnessToken verdict.ActionKey movedOn f None None with
            | Error _ -> ()
            | Ok stale -> failwithf "GATE INVERSION: a stale replay against a moved head was authorized: %A" stale
        | Error e -> failwithf "%A" e

    [<Fact>]
    let ``#2175 an unreadable binding fails closed rather than defaulting to a permissive state`` () =
        let badBinding = { binding Ordinary 1 "head1" "impl" with ItemRef = "" }
        match inspect badBinding (facts [] PrPending None true) None None with
        | Error reasons -> Assert.Contains(reasons, fun r -> r.Contains "item ref")
        | Ok v -> failwithf "expected a fail-closed no-verdict for an incomplete binding, got %A" v

    // ---- .github#2417: critic-succession recovery when the same critic despawns mid-round ------------

    [<Fact>]
    let ``#2417 no succession receipt leaves ResumeSameCritic unchanged (default path)`` () =
        // Byte-for-byte the same fixture as the pre-#2417 case above, but explicit that a caller who
        // never grants succession sees the identical, unmodified behavior (acceptance AC-001).
        let comments = [ initialChangesRequired "kite" "head1" ]
        match inspect (binding Ordinary 1 "head2" "impl") (facts comments PrPending None true) None None with
        | Ok v ->
            Assert.Equal(AwaitingSameCriticConfirmation 1, v.State)
            match v.NextAction with
            | ResumeSameCritic reason -> Assert.DoesNotContain("refused, not consumed", reason)
            | other -> failwithf "expected ResumeSameCritic, got %A" other
        | Error e -> failwithf "%A" e

    [<Fact>]
    let ``#2417 a valid granted receipt yields EnterCriticSuccession in the ordinary phase`` () =
        let comments = [ initialChangesRequired "kite" "head1" ]
        let receipt = successionReceipt "kite" "fresh-critic" "host-9b63" "head2"
        match inspect (binding Ordinary 1 "head2" "impl") (facts comments PrPending None true) (Some receipt) None with
        | Ok v ->
            Assert.Equal(AwaitingSameCriticConfirmation 1, v.State)
            Assert.Equal(EnterCriticSuccession receipt, v.NextAction)
        | Error e -> failwithf "%A" e

    [<Fact>]
    let ``#2417 a valid granted receipt yields EnterCriticSuccession in the repair phase too`` () =
        let comments =
            [ comment 1L "https://reviews/repair-1" "<!-- fsgg:independent-review-repair-phase:v1 -->\n<!-- fsgg:independent-review:v1 -->\ncritic: fresh-critic\nreviewed-head: head3\nverdict: changes-required" ]
        let receipt = successionReceipt "fresh-critic" "second-critic" "host-9b63" "head4"
        match inspect (binding Repair 1 "head4" "fresh-impl") (facts comments PrPending None true) (Some receipt) None with
        | Ok v ->
            Assert.Equal(RepairPhaseActive 1, v.State)
            Assert.Equal(EnterCriticSuccession receipt, v.NextAction)
        | Error e -> failwithf "%A" e

    [<Fact>]
    let ``#2417 a receipt naming the wrong original critic is refused -- the chain stays confirmable by its own critic`` () =
        // AC-003 / the checklist's own negative case: a chain that CAN still be confirmed by its own
        // critic (here, "kite") must not be diverted onto the recovery path by a receipt that names a
        // DIFFERENT original critic.
        let comments = [ initialChangesRequired "kite" "head1" ]
        let receipt = successionReceipt "some-other-critic" "fresh-critic" "host-9b63" "head2"
        match inspect (binding Ordinary 1 "head2" "impl") (facts comments PrPending None true) (Some receipt) None with
        | Ok v ->
            match v.NextAction with
            | ResumeSameCritic reason -> Assert.Contains("refused, not consumed", reason)
            | other -> failwithf "expected ResumeSameCritic (receipt refused), got %A" other
        | Error e -> failwithf "%A" e

    [<Fact>]
    let ``#2417 a receipt bound to a stale head is refused`` () =
        // The receipt was granted for "head2", but a further commit ("head3") has since landed — the
        // grant no longer matches the exact stuck round and must not be silently reused.
        let comments = [ initialChangesRequired "kite" "head1" ]
        let receipt = successionReceipt "kite" "fresh-critic" "host-9b63" "head2"
        match inspect (binding Ordinary 1 "head3" "impl") (facts comments PrPending None true) (Some receipt) None with
        | Ok v ->
            match v.NextAction with
            | ResumeSameCritic reason -> Assert.Contains("refused, not consumed", reason)
            | other -> failwithf "expected ResumeSameCritic (receipt refused), got %A" other
        | Error e -> failwithf "%A" e

    [<Fact>]
    let ``#2417 a receipt naming the implementer as successor is refused`` () =
        let comments = [ initialChangesRequired "kite" "head1" ]
        let receipt = successionReceipt "kite" "impl" "host-9b63" "head2"
        match inspect (binding Ordinary 1 "head2" "impl") (facts comments PrPending None true) (Some receipt) None with
        | Ok v ->
            match v.NextAction with
            | ResumeSameCritic _ -> ()
            | other -> failwithf "expected ResumeSameCritic (implementer cannot be its own successor), got %A" other
        | Error e -> failwithf "%A" e

    [<Fact>]
    let ``#2417 a receipt granted by the implementer is refused`` () =
        let comments = [ initialChangesRequired "kite" "head1" ]
        let receipt = successionReceipt "kite" "fresh-critic" "impl" "head2"
        match inspect (binding Ordinary 1 "head2" "impl") (facts comments PrPending None true) (Some receipt) None with
        | Ok v ->
            match v.NextAction with
            | ResumeSameCritic _ -> ()
            | other -> failwithf "expected ResumeSameCritic (implementer cannot grant its own succession), got %A" other
        | Error e -> failwithf "%A" e

    [<Fact>]
    let ``#2417 GATE INVERSION -- without the matching-critic guard, a mismatched receipt would wrongly grant succession`` () =
        // Documents the guard's own failure mode by construction: a receipt naming the WRONG original
        // critic must never validate merely because SOME receipt was supplied. This pins the conjunction
        // in `criticSuccessionValid` rather than only a disjunction of individually-necessary checks.
        let comments = [ initialChangesRequired "kite" "head1" ]
        let wrongCriticReceipt = successionReceipt "not-kite" "fresh-critic" "host-9b63" "head2"
        match inspect (binding Ordinary 1 "head2" "impl") (facts comments PrPending None true) (Some wrongCriticReceipt) None with
        | Ok v ->
            match v.NextAction with
            | EnterCriticSuccession _ -> failwith "GATE INVERSION: a receipt naming the wrong original critic was admitted"
            | ResumeSameCritic _ -> ()
            | other -> failwithf "expected ResumeSameCritic, got %A" other
        | Error e -> failwithf "%A" e

    [<Fact>]
    let ``#2451 a receipt naming the generic agent-type string as original critic is refused, even when it matches`` () =
        // The negative case for succession: the stuck round's marker names critic
        // `fsgg-critic-normal` — the bare agent-type string every critic dispatched at that route
        // shares — and the receipt names the SAME string as `original-critic`. The equality holds
        // textually, but it proves nothing: any critic at that route would satisfy it, so this can
        // never be "the exact stuck critic" the guard's own doc comment promises. Must fall back to
        // `ResumeSameCritic`, exactly like a receipt naming the wrong critic.
        let comments = [ initialChangesRequired "fsgg-critic-normal" "head1" ]
        let receipt = successionReceipt "fsgg-critic-normal" "fresh-critic" "host-9b63" "head2"
        match inspect (binding Ordinary 1 "head2" "impl") (facts comments PrPending None true) (Some receipt) None with
        | Ok v ->
            match v.NextAction with
            | EnterCriticSuccession _ ->
                failwith "GATE INVERSION: a generic agent-type critic identity was admitted as the exact stuck critic"
            | ResumeSameCritic reason -> Assert.Contains("refused, not consumed", reason)
            | other -> failwithf "expected ResumeSameCritic (receipt refused), got %A" other
        | Error e -> failwithf "%A" e

    [<Fact>]
    let ``#2417 advance re-converges idempotently on a granted EnterCriticSuccession verdict`` () =
        let comments = [ initialChangesRequired "kite" "head1" ]
        let receipt = successionReceipt "kite" "fresh-critic" "host-9b63" "head2"
        let b = binding Ordinary 1 "head2" "impl"
        let f = facts comments PrPending None true
        match inspect b f (Some receipt) None with
        | Ok verdict ->
            Assert.Equal(EnterCriticSuccession receipt, verdict.NextAction)
            match advance verdict.FreshnessToken verdict.ActionKey b f (Some receipt) None with
            | Ok replay ->
                Assert.Equal(verdict.NextAction, replay.NextAction)
                Assert.Equal(verdict.FreshnessToken, replay.FreshnessToken)
                Assert.Equal(verdict.ActionKey, replay.ActionKey)
            | Error e -> failwithf "restart replay should re-converge: %A" e
        | Error e -> failwithf "%A" e

    // ---- .github#2549: the designed post-acceptance §6 window is not malformed evidence -------------
    //
    // Reproduced from the LIVE chain of `.github#2534` / PR #2541, whose comment set was fetched with
    // `gh api repos/FS-GG/.github/issues/2541/comments` and fed to the shipped engine at head
    // `30aa766ff68c2ef33282ee9bace3fc153756327a`: `checks: pending` returned
    // `{"state":"malformedEvidence","stateErrors":["review checks are not green"],"action":"park"}`
    // while the IDENTICAL comment set at `checks: green` returned `accepted`/`accept`. The evidence was
    // the same in both runs; only the live check state differed. `malformedEvidence` is also what a
    // chain carrying two competing initial markers reports, and the recovery that word teaches — close
    // the pull request without merging — ran on PR #2514 on 2026-08-13.
    //
    // The shape below is that chain: an initial `changes-required`, a round-1 `pass` confirmation at
    // the SAME head (the finding was against a PR comment, not the tree), and a bound host acceptance.

    let acceptedChain =
        [ initialChangesRequired "kite" "head1"
          confirmation 2L "kite" 1 "https://reviews/1" "head1" "pass"
          accepted 3L "head1" "https://reviews/2" ]

    [<Fact>]
    let ``#2549 an accepted chain whose checks have not reported is complete, not malformed`` () =
        match inspect (binding Ordinary 1 "head1" "impl") (facts acceptedChain PrPending None true) None None with
        | Ok v ->
            Assert.Equal(AcceptedAwaitingChecks PrPending, v.State)
            match v.NextAction with
            | AuthorizeDelivery reason ->
                // The action must name the §6 call, because by .github#2504 `claim-generation` cannot
                // report until that call writes the authorization marker: passive waiting is a cycle.
                Assert.Contains("delivery", reason)
                Assert.Contains("claim-generation", reason)
            | other -> failwithf "expected AuthorizeDelivery, got %A" other
        | Error e -> failwithf "%A" e

    [<Fact>]
    let ``#2549 the same accepted chain with green checks still accepts, unchanged`` () =
        match inspect (binding Ordinary 1 "head1" "impl") (facts acceptedChain PrGreen None true) None None with
        | Ok v ->
            Assert.Equal(Accepted, v.State)
            match v.NextAction with
            | Accept receipt ->
                Assert.Equal("kite", receipt.CriticIdentity)
                Assert.Equal<int list>([ 1 ], receipt.Rounds)
                Assert.True receipt.ChecksGreen
            | other -> failwithf "expected Accept, got %A" other
        | Error e -> failwithf "%A" e

    [<Fact>]
    let ``#2549 an accepted chain with RED checks is a failing change, not broken evidence`` () =
        match inspect (binding Ordinary 1 "head1" "impl") (facts acceptedChain PrRed None true) None None with
        | Ok v ->
            Assert.Equal(AcceptedAwaitingChecks PrRed, v.State)
            match v.NextAction with
            | ResumeImplementer reason -> Assert.Contains("checks are failing", reason)
            | other -> failwithf "expected ResumeImplementer, got %A" other
        | Error e -> failwithf "%A" e

    /// A chain the terminal parser ACCEPTS but whose round count exceeds the ceiling the BINDING's phase
    /// imposes: four confirmations under a repair-phase marker (parser ceiling 10) inspected as an
    /// ordinary chain (`Review` ceiling 3). This is the one structural clause reachable with a
    /// successfully parsed chain, so it is what pins that the checks message no longer travels with a
    /// structural malformation.
    let ceilingMismatchChain =
        [ comment
              1L
              "https://reviews/1"
              "<!-- fsgg:independent-review-repair-phase:v1 -->\n<!-- fsgg:independent-review:v1 -->\ncritic: kite\nreviewed-head: head0\nverdict: changes-required"
          confirmation 2L "kite" 1 "https://reviews/1" "head1" "changes-required"
          confirmation 3L "kite" 2 "https://reviews/2" "head2" "changes-required"
          confirmation 4L "kite" 3 "https://reviews/3" "head3" "changes-required"
          confirmation 5L "kite" 4 "https://reviews/4" "head4" "pass"
          accepted 6L "head4" "https://reviews/5" ]

    [<Fact>]
    let ``#2549 a structurally malformed chain with pending checks reports ONLY the structural error`` () =
        match inspect (binding Ordinary 1 "head4" "impl") (facts ceilingMismatchChain PrPending None true) None None with
        | Ok v ->
            match v.State with
            | MalformedEvidence errors ->
                Assert.Contains("review round ceiling exceeded", errors)
                // GATE INVERSION TARGET. Before .github#2549 this list also carried the liveness
                // message, so a consumer could not tell "this chain is broken" from "this chain is fine
                // and CI has not reported". Re-tagging the checks clause structural in
                // `Driver.reviewChainProblems` reds exactly this assertion.
                Assert.DoesNotContain("review checks are not green", errors)
            | other -> failwithf "expected MalformedEvidence, got %A" other
        | Error e -> failwithf "%A" e

    /// THE ARM IS UNCHANGED; ITS REACHABILITY IS NOT — and the first draft of this file claimed
    /// otherwise (.github#2549 round-1 M3). At GREEN checks this is byte-for-byte the pre-change
    /// behaviour, which the leg below pins. At a NON-GREEN check state it is not: before the
    /// structural/liveness split, `validateReviewChain` returned `["review checks are not green"]`, that
    /// list was non-empty, and the head-mismatch arm was never reached — the chain reported the checks
    /// message instead. So the honest statement is that the arm's reason and shape are untouched and it
    /// is now REACHABLE where the liveness clause previously masked it.
    ///
    /// The direction is benign and toward `.github#2487`'s own remedy: a chain bound to the wrong head
    /// now says so at every check state instead of only at green. `.github#2487` still owns the arm, and
    /// this row does not rewrite it.
    [<Fact>]
    let ``#2549 an acceptance bound to a different head is malformed evidence at GREEN checks, byte-for-byte`` () =
        match inspect (binding Ordinary 1 "head2" "impl") (facts acceptedChain PrGreen None true) None None with
        | Ok v ->
            match v.State with
            | MalformedEvidence [ reason ] ->
                Assert.Equal("the accepted review chain is bound to a different head than the current commit", reason)
            | other -> failwithf "expected the single head-mismatch MalformedEvidence, got %A" other
        | Error e -> failwithf "%A" e

    [<Fact>]
    let ``#2549 the head-mismatch arm is now REACHABLE at pending, where the liveness clause masked it`` () =
        match inspect (binding Ordinary 1 "head2" "impl") (facts acceptedChain PrPending None true) None None with
        | Ok v ->
            match v.State with
            | MalformedEvidence [ reason ] ->
                // Pre-change this was `["review checks are not green"]`. The reason string itself is
                // untouched; what changed is that the reader now learns the thing that actually matters.
                Assert.Equal("the accepted review chain is bound to a different head than the current commit", reason)
            | other -> failwithf "expected the single head-mismatch MalformedEvidence, got %A" other
        | Error e -> failwithf "%A" e

    [<Fact>]
    let ``#2549 an UNREADABLE check state parks rather than authorizing delivery`` () =
        // .github#2549 round-1 M1. `PrUnknown` is not a wait: `Landable.settled` scores it with
        // `PrConflicted`, `Client` maps it to `ExitNoVerdict`, and `Reads.fsi` degrades a multi-page
        // runs list to it deterministically. Grouping it with `PrPending` told the host to publish an
        // authorization for a change whose checks nobody had read.
        match inspect (binding Ordinary 1 "head1" "impl") (facts acceptedChain PrUnknown None true) None None with
        | Ok v ->
            Assert.Equal(AcceptedAwaitingChecks PrUnknown, v.State)
            match v.NextAction with
            | Park reason ->
                Assert.Contains("could not be read", reason)
                Assert.Contains("no-verdict", reason)
            | AuthorizeDelivery _ ->
                failwith "GATE INVERSION: an unreadable check state took the reassuring path — the exact defect this item exists to remove, reproduced inside its own new state"
            | other -> failwithf "expected Park, got %A" other
        | Error e -> failwithf "%A" e

    [<Fact>]
    let ``#2549 an unreadable check state is not a finding against the change either`` () =
        // The opposite over-reading: "the checks could not be read" is not evidence the tree is broken,
        // so it must not route to the implementer with an unnamed failure to chase.
        match inspect (binding Ordinary 1 "head1" "impl") (facts acceptedChain PrUnknown None true) None None with
        | Ok v ->
            match v.NextAction with
            | ResumeImplementer _ -> failwith "an unreadable check state invented a defect in the change"
            | _ -> ()
        | Error e -> failwithf "%A" e

    // ---- .github#2549: a repair whose subject is a PR comment rather than the tree -----------------

    let repairAssertion url head grantedBy : RepairAssertionReceipt =
        { AnsweredReviewUrl = url
          CandidateHeadSha = head
          GrantedBy = grantedBy
          Reason = "the finding was against the post-merge obligations comment, repaired in place" }

    /// The state the live `.github#2534` chain sat in the moment its comment-shaped repair was complete:
    /// one initial `changes-required` at the current head, and no commit — because none was owed.
    let unmovedAfterChangesRequired = [ initialChangesRequired "kite" "head1" ]

    [<Fact>]
    let ``#2549 a granted repair assertion advances an unmoved head to the same critic`` () =
        let grant = repairAssertion "https://reviews/1" "head1" "host-9b63"
        match inspect (binding Ordinary 1 "head1" "impl") (facts unmovedAfterChangesRequired PrPending None true) None (Some grant) with
        | Ok v ->
            Assert.Equal(AwaitingSameCriticConfirmation 1, v.State)
            match v.NextAction with
            | ResumeSameCritic reason ->
                Assert.Contains("host-9b63", reason)
                Assert.Contains("rather than the tree", reason)
            | other -> failwithf "expected ResumeSameCritic, got %A" other
        | Error e -> failwithf "%A" e

    [<Fact>]
    let ``#2549 the same grant works in the repair phase, through the one shared guard`` () =
        let comments =
            [ comment
                  1L
                  "https://reviews/repair-1"
                  "<!-- fsgg:independent-review-repair-phase:v1 -->\n<!-- fsgg:independent-review:v1 -->\ncritic: fresh-critic\nreviewed-head: head3\nverdict: changes-required" ]
        let grant = repairAssertion "https://reviews/repair-1" "head3" "host-9b63"
        match inspect (binding Repair 1 "head3" "fresh-impl") (facts comments PrPending None true) None (Some grant) with
        | Ok v ->
            Assert.Equal(RepairPhaseActive 1, v.State)
            match v.NextAction with
            | ResumeSameCritic _ -> ()
            | other -> failwithf "expected ResumeSameCritic in the repair phase, got %A" other
        | Error e -> failwithf "%A" e

    /// Every refusal leg asserts the SAME pre-existing outcome, because the guard's whole contract is
    /// that a failed conjunct is indistinguishable from no grant at all in what it permits. Each of
    /// these is a gate-inversion target: dropping the named conjunct from `repairAssertionValid` turns
    /// that leg into `ResumeSameCritic` and reds it.
    let private assertRefused (grant: RepairAssertionReceipt option) =
        match inspect (binding Ordinary 1 "head1" "impl") (facts unmovedAfterChangesRequired PrPending None true) None grant with
        | Ok v ->
            Assert.Equal(AwaitingImplementerRepair 1, v.State)
            match v.NextAction with
            | ResumeImplementer reason ->
                Assert.Contains("no new commit has landed yet", reason)
                if grant.IsSome then
                    Assert.Contains("refused, not consumed", reason)
            | other -> failwithf "GATE INVERSION: expected ResumeImplementer, got %A" other
        | Error e -> failwithf "%A" e

    [<Fact>]
    let ``#2549 with NO grant an unmoved head still resumes the implementer, byte-for-byte`` () = assertRefused None

    [<Fact>]
    let ``#2549 a grant bound to a different head is refused`` () =
        assertRefused (Some(repairAssertion "https://reviews/1" "head0" "host-9b63"))

    [<Fact>]
    let ``#2549 a grant answering a different review comment is refused`` () =
        assertRefused (Some(repairAssertion "https://reviews/9" "head1" "host-9b63"))

    [<Fact>]
    let ``#2549 an implementer can never grant its own repair assertion`` () =
        assertRefused (Some(repairAssertion "https://reviews/1" "head1" "impl"))

    [<Fact>]
    let ``#2549 the round's own critic can never grant the trigger it will then confirm`` () =
        assertRefused (Some(repairAssertion "https://reviews/1" "head1" "kite"))

    [<Fact>]
    let ``#2549 a grant with no accountable granter is refused`` () =
        assertRefused (Some(repairAssertion "https://reviews/1" "head1" "   "))

    [<Fact>]
    let ``#2549 advance re-converges idempotently on a granted comment-shaped repair`` () =
        let grant = repairAssertion "https://reviews/1" "head1" "host-9b63"
        let b = binding Ordinary 1 "head1" "impl"
        let f = facts unmovedAfterChangesRequired PrPending None true
        match inspect b f None (Some grant) with
        | Ok verdict ->
            match advance verdict.FreshnessToken verdict.ActionKey b f None (Some grant) with
            | Ok replay ->
                Assert.Equal(verdict.NextAction, replay.NextAction)
                Assert.Equal(verdict.ActionKey, replay.ActionKey)
            | Error e -> failwithf "replay should re-converge: %A" e
        | Error e -> failwithf "%A" e

    [<Fact>]
    let ``#2549 a grant cannot survive the head moving under it`` () =
        // The freshness token folds the full binding including `HeadSha`, and the guard independently
        // re-checks `CandidateHeadSha`. Both must hold, so a verdict inspected at head1 can never
        // authorize a transition at head2.
        let grant = repairAssertion "https://reviews/1" "head1" "host-9b63"
        let b = binding Ordinary 1 "head1" "impl"
        let f = facts unmovedAfterChangesRequired PrPending None true
        match inspect b f None (Some grant) with
        | Ok verdict ->
            let moved = binding Ordinary 1 "head2" "impl"
            match advance verdict.FreshnessToken verdict.ActionKey moved f None (Some grant) with
            | Ok _ -> failwith "GATE INVERSION: a stale verdict authorized a transition at a moved head"
            | Error _ -> ()
        | Error e -> failwithf "%A" e

    [<Fact>]
    let ``#2549 a review comment carrying no URL can never be answered by a grant`` () =
        // Without the non-blank check, a grant whose `AnsweredReviewUrl` is empty would MATCH a review
        // comment that also carries no URL — binding the grant to nothing at all while looking bound.
        let noUrl =
            [ comment 1L "" "<!-- fsgg:independent-review:v1 -->\ncritic: kite\nreviewed-head: head1\nverdict: changes-required" ]
        let grant = repairAssertion "" "head1" "host-9b63"
        match inspect (binding Ordinary 1 "head1" "impl") (facts noUrl PrPending None true) None (Some grant) with
        | Ok v ->
            Assert.Equal(AwaitingImplementerRepair 1, v.State)
            match v.NextAction with
            | ResumeImplementer reason -> Assert.Contains("refused, not consumed", reason)
            | other -> failwithf "GATE INVERSION: expected ResumeImplementer, got %A" other
        | Error e -> failwithf "%A" e
