namespace FS.GG.Coord.Tests

open Xunit
open FS.GG.Coord.Batch
open FS.GG.Coord.Driver

module DriverTests =
    let model =
        { Waves = 2
          ImplementerSlotsPerWave = 3
          ReviewSlots = 2
          ConsolidationThreshold = 3 }

    let clean =
        { HasHostIdentity = true
          StaleClaim = false
          EngineCurrent = true
          PendingWrites = 0
          ReconcileDryRunFresh = true
          ReconcileApplied = true
          ReconcileFresh = true
          TriageFresh = true
          CurrencyScoped = true }

    let comment id url body : ReviewComment = { Id = id; Url = url; Body = body }

    let notMeaningful =
        "\nroute-applicability: not-meaningful\nroute-not-meaningful-reason: this review subject has no meaningful runtime-route comparison"

    let meaningful =
        "\nroute-applicability: meaningful\nbuilt-artifact: artifacts/product.dll\nexecuted-command: dotnet product.dll --compare-routes\ncompared-routes: production input vs direct dispatch\nobserved-result: both routes emitted the same effect"

    [<Fact>]
    let ``#2127 receipts are source-bound complete and fresh`` () =
        let review =
            { MarkerValid = true; CriticIdentity = Some "shrike"; HeadSha = Some "abc"; Rounds = [1]; RepairPhase = false
              ChecksGreen = true; HostAccepted = true
              RuntimeRouteEvidence = Some(NotMeaningful "receipt freshness has no runtime-route subject")
              DiffAuditRequired = false; DiffAuditHead = None }
        Assert.True(receiptFresh 120L 30L { ObservedAt = 100L; SourceSha = "abc"; Complete = true; Review = Some review })
        Assert.False(receiptFresh 120L 30L { ObservedAt = 80L; SourceSha = "abc"; Complete = true; Review = Some review })
        Assert.False(receiptFresh 120L 30L { ObservedAt = 100L; SourceSha = "abc"; Complete = true; Review = None })
        let planning =
            { ObservedAt = 100L
              SourceSha = "snapshot"
              Complete = true
              ConsolidationApproved = true
              Observations =
                [ for kind, outcome in
                      [ "reconcile-dry-run", "clean"
                        "reconcile-apply", "applied-or-not-needed"
                        "reconcile-fresh", "clean"
                        "triage", "fresh"
                        "engine-currency", "current-scoped" ] do
                      { Kind = kind
                        ObservedAt = 100L
                        SourceSha = "snapshot"
                        Outcome = outcome
                        ReceiptId = observationReceiptId kind 100L "snapshot" outcome } ] }

        Assert.True(planningReceiptFresh 120L 30L "snapshot" planning)
        Assert.False(planningReceiptFresh 120L 30L "other" planning)
        Assert.False(planningReceiptFresh 140L 30L "snapshot" planning)

        let replayed =
            { planning with
                Observations =
                    planning.Observations
                    |> List.map (fun observation -> { observation with SourceSha = "other" }) }

        Assert.False(planningReceiptFresh 120L 30L "snapshot" replayed)

        let forged =
            { planning with
                Observations =
                    planning.Observations
                    |> List.map (fun observation ->
                        { observation with
                            ReceiptId = "caller-authored" }) }

        Assert.False(planningReceiptFresh 120L 30L "snapshot" forged)

    [<Fact>]
    let ``#2127 6 to 2 consolidates then dispatches a fresh three-slot wave`` () =
        Assert.Equal(Consolidate, nextAction model 2 false clean [])
        Assert.Equal(DispatchWave 3, nextAction model 2 true clean [])

    [<Fact>]
    let ``#2127 housekeeping gates fail closed before dispatch`` () =
        Assert.Equal(RequestHostIdentity, nextAction model 2 true { clean with HasHostIdentity = false } [])
        Assert.Equal(ReapStaleClaims, nextAction model 2 true { clean with StaleClaim = true } [])
        Assert.Equal(RepairEngineCurrency, nextAction model 2 true { clean with EngineCurrent = false } [])
        Assert.Equal(FlushPendingWrites, nextAction model 2 true { clean with PendingWrites = 1 } [])
        Assert.Equal(RefreshTriage, nextAction model 2 true { clean with TriageFresh = false } [])

    [<Fact>]
    let ``#2127 stale reconciliation and scoped currency block dispatch`` () =
        Assert.Equal(ReconcileBoard, nextAction model 2 true { clean with ReconcileFresh = false } [])
        Assert.Equal(RepairEngineCurrency, nextAction model 2 true { clean with CurrencyScoped = false } [])

    [<Fact>]
    let ``#2127 no-pr and tests-running live claims resume their worker`` () =
        Assert.Equal(
            ResumeSameWorker,
            nextAction
                model
                2
                true
                clean
                [ { ClaimLive = true
                    ReviewReady = false
                    ParkedOrDone = false } ]
        )

    [<Fact>]
    let ``#2127 review validation rejects marker sha rounds checks and acceptance defects`` () =
        let errors = validateReviewChain 3 { MarkerValid = false; CriticIdentity = None; HeadSha = None; Rounds = [ 2 ]; RepairPhase = false; ChecksGreen = false; HostAccepted = false; RuntimeRouteEvidence = None; DiffAuditRequired = false; DiffAuditHead = None }
        Assert.True(List.length errors >= 5)

    [<Fact>]
    let ``#2127 review comment markers bind critic and identical reviewed accepted sha`` () =
        let comments =
            [ comment
                  1L
                  "https://reviews/1"
                  ("<!-- fsgg:independent-review:v1 -->\ncritic: shrike\nreviewed-head: abc\nverdict: pass"
                   + notMeaningful)
              comment
                  2L
                  "https://reviews/2"
                  "<!-- fsgg:review-accepted:v1 -->\naccepted-head: abc\ninitial-review: https://reviews/1\nlatest-confirmation: https://reviews/1" ]

        match parseReviewComments comments with
        | Ok chain -> Assert.Equal(Some "shrike", chain.CriticIdentity)
        | Error errors -> failwithf "%A" errors

        Assert.True(
            Result.isError (
                parseReviewComments
                    [ comment
                          1L
                          "https://reviews/1"
                          "<!-- fsgg:independent-review:v1 -->\ncritic: x\nreviewed-head: abc" ]
            )
        )

    [<Fact>]
    let ``#2086 passing review markers enforce the runtime-route evidence union`` () =
        let accepted =
            comment
                2L
                "https://reviews/accepted"
                "<!-- fsgg:review-accepted:v1 -->\naccepted-head: abc\ninitial-review: https://reviews/1\nlatest-confirmation: https://reviews/1"

        let parse suffix =
            parseReviewComments
                [ comment
                      1L
                      "https://reviews/1"
                      ("<!-- fsgg:independent-review:v1 -->\ncritic: kite\nreviewed-head: abc\nverdict: pass"
                       + suffix)
                  accepted ]

        match parse meaningful with
        | Ok chain ->
            match chain.RuntimeRouteEvidence with
            | Some(Meaningful(artifact, command, routes, result)) ->
                Assert.Equal("artifacts/product.dll", artifact)
                Assert.Contains("compare-routes", command)
                Assert.Contains("production input", routes)
                Assert.Contains("same effect", result)
            | evidence -> failwithf "unexpected evidence: %A" evidence
        | Error errors -> failwithf "%A" errors

        match parse notMeaningful with
        | Ok chain ->
            Assert.Equal(
                Some(NotMeaningful "this review subject has no meaningful runtime-route comparison"),
                chain.RuntimeRouteEvidence
            )
        | Error errors -> failwithf "%A" errors

        let rejects suffix =
            Assert.True(Result.isError (parse suffix))

        rejects ""

        rejects
            "\nroute-applicability: meaningful\nbuilt-artifact: product.dll\nexecuted-command: run\ncompared-routes: a vs b"

        rejects (meaningful + "\nroute-not-meaningful-reason: contradictory")
        rejects "\nroute-applicability: not-meaningful"
        rejects (notMeaningful + "\nbuilt-artifact: contradictory.dll")
        rejects (notMeaningful + "\nroute-applicability: not-meaningful")
        rejects "\nroute-applicability: \nroute-not-meaningful-reason: empty decision"
        rejects "\nroute-applicability: advisory\nroute-not-meaningful-reason: unknown decision"

        rejects (
            "\nroute-applicability: not-meaningful\nroute-not-meaningful-reason: "
            + System.String('x', 501)
        )

    [<Fact>]
    let ``#2127 latest confirmation round binds the accepted sha`` () =
        let comments =
            [ comment
                  1L
                  "https://reviews/1"
                  "<!-- fsgg:independent-review:v1 -->\ncritic: shrike\nreviewed-head: old\nverdict: changes-required"
              comment
                  2L
                  "https://reviews/2"
                  ("<!-- fsgg:independent-review-confirmation:v1 -->\ninitial-review: https://reviews/1\ncritic: shrike\nround: 1\npreceding-review: https://reviews/1\nreviewed-head: new\nverdict: pass"
                   + notMeaningful)
              comment
                  3L
                  "https://reviews/3"
                  "<!-- fsgg:review-accepted:v1 -->\naccepted-head: new\ninitial-review: https://reviews/1\nlatest-confirmation: https://reviews/2" ]

        match parseReviewComments comments with
        | Ok chain -> Assert.Equal(Some "new", chain.HeadSha)
        | Error errors -> failwithf "%A" errors

    [<Fact>]
    let ``#2127 repair rounds link the exact preceding comment URL in order`` () =
        let comments =
            [ comment
                  10L
                  "https://reviews/initial"
                  "<!-- fsgg:independent-review:v1 -->\ncritic: shrike\nreviewed-head: a\nverdict: changes-required"
              comment
                  20L
                  "https://reviews/round-1"
                  "<!-- fsgg:independent-review-confirmation:v1 -->\ninitial-review: https://reviews/initial\ncritic: shrike\nround: 1\npreceding-review: https://reviews/initial\nreviewed-head: b\nverdict: changes-required"
              comment
                  30L
                  "https://reviews/round-2"
                  ("<!-- fsgg:independent-review-confirmation:v1 -->\ninitial-review: https://reviews/initial\ncritic: shrike\nround: 2\npreceding-review: https://reviews/round-1\nreviewed-head: c\nverdict: pass"
                   + meaningful)
              comment
                  40L
                  "https://reviews/accepted"
                  "<!-- fsgg:review-accepted:v1 -->\naccepted-head: c\ninitial-review: https://reviews/initial\nlatest-confirmation: https://reviews/round-2" ]

        match parseReviewComments comments with
        | Ok chain ->
            Assert.Equal<int list>([ 1; 2 ], chain.Rounds)
            Assert.Equal(Some "c", chain.HeadSha)
        | Error errors -> failwithf "%A" errors

    [<Fact>]
    let ``#2127 review confirmations fail closed unless one critic advances every linked round`` () =
        let initial =
            comment
                1L
                "https://reviews/1"
                "<!-- fsgg:independent-review:v1 -->\ncritic: shrike\nreviewed-head: first\nverdict: changes-required"

        let accepted head =
            comment
                3L
                "https://reviews/3"
                $"<!-- fsgg:review-accepted:v1 -->\naccepted-head: %s{head}\ninitial-review: https://reviews/1\nlatest-confirmation: https://reviews/2"

        let confirmation critic round initialUrl preceding head verdict =
            let route = if verdict = "pass" then notMeaningful else ""

            comment
                2L
                "https://reviews/2"
                ($"<!-- fsgg:independent-review-confirmation:v1 -->\ninitial-review: %s{initialUrl}\ncritic: %s{critic}\nround: %s{round}\npreceding-review: %s{preceding}\nreviewed-head: %s{head}\nverdict: %s{verdict}"
                 + route)

        let rejects comments =
            Assert.True(Result.isError (parseReviewComments comments))

        rejects
            [ initial
              confirmation "other" "1" "https://reviews/1" "https://reviews/1" "second" "pass"
              accepted "second" ]

        rejects
            [ initial
              confirmation "shrike" "2" "https://reviews/1" "https://reviews/1" "second" "pass"
              accepted "second" ]

        rejects
            [ initial
              confirmation "shrike" "1" "wrong" "https://reviews/1" "second" "pass"
              accepted "second" ]

        rejects
            [ initial
              confirmation "shrike" "1" "https://reviews/1" "wrong" "second" "pass"
              accepted "second" ]

        rejects
            [ initial
              confirmation "shrike" "1" "https://reviews/1" "https://reviews/1" "second" "changes-required"
              accepted "second" ]

        rejects
            [ initial
              comment
                  2L
                  "https://reviews/2"
                  "<!-- fsgg:independent-review-confirmation:v1 -->\ncritic: shrike\nround: 1"
              accepted "second" ]

        rejects
            [ initial
              confirmation "shrike" "1" "https://reviews/1" "https://reviews/1" "second" "pass"
              accepted "first" ]

    [<Fact>]
    let ``#2127 markers and acceptance links fail closed at the live parser`` () =
        let initial =
            comment
                10L
                "https://reviews/initial"
                "<!-- fsgg:independent-review:v1 -->\ncritic: shrike\nreviewed-head: a\nverdict: changes-required"

        let confirmation id round preceding head =
            let verdict = if round = 4 then "pass" else "changes-required"

            comment
                id
                $"https://reviews/round-%d{round}"
                $"<!-- fsgg:independent-review-confirmation:v1 -->\ninitial-review: https://reviews/initial\ncritic: shrike\nround: %d{round}\npreceding-review: %s{preceding}\nreviewed-head: %s{head}\nverdict: %s{verdict}"

        let acceptance body =
            comment 100L "https://reviews/accepted" ("<!-- fsgg:review-accepted:v1 -->\n" + body)

        let rejects comments =
            Assert.True(Result.isError (parseReviewComments comments))

        rejects
            [ comment
                  1L
                  "https://reviews/quoted"
                  "> <!-- fsgg:independent-review:v1 -->\ncritic: shrike\nreviewed-head: a\nverdict: pass"
              acceptance
                  "accepted-head: a\ninitial-review: https://reviews/quoted\nlatest-confirmation: https://reviews/quoted" ]

        rejects
            [ comment
                  1L
                  "https://reviews/duplicate"
                  "<!-- fsgg:independent-review:v1 -->\n<!-- fsgg:independent-review:v1 -->\ncritic: shrike\nreviewed-head: a\nverdict: pass"
              acceptance
                  "accepted-head: a\ninitial-review: https://reviews/duplicate\nlatest-confirmation: https://reviews/duplicate" ]

        let rounds =
            [ confirmation 20L 1 "https://reviews/initial" "b"
              confirmation 30L 2 "https://reviews/round-1" "c"
              confirmation 40L 3 "https://reviews/round-2" "d"
              confirmation 50L 4 "https://reviews/round-3" "e" ]

        rejects (
            initial :: rounds
            @ [ acceptance
                    "accepted-head: e\ninitial-review: https://reviews/initial\nlatest-confirmation: https://reviews/round-4" ]
        )

        rejects
            [ initial
              confirmation 20L 1 "https://reviews/initial" "b"
              acceptance "accepted-head: b" ]

        rejects
            [ initial
              confirmation 20L 1 "https://reviews/initial" "b"
              acceptance "accepted-head: b\ninitial-review: https://reviews/initial\nlatest-confirmation: wrong" ]

    [<Fact>]
    let ``#2136 repair-phase confirmation ceilings are typed and marker comments are not rounds`` () =
        let initial = comment 10L "https://reviews/initial" "<!-- fsgg:independent-review:v1 -->\ncritic: kestrel\nreviewed-head: initial\nverdict: changes-required"
        let repairMarker id = comment id $"https://reviews/repair-phase-%d{id}" "<!-- fsgg:independent-review-repair-phase:v1 -->"
        let chain count markers =
            let confirmations =
                [ for round in 1 .. count do
                    let preceding = if round = 1 then "https://reviews/initial" else $"https://reviews/round-%d{round - 1}"
                    let verdict = if round = count then "pass" else "changes-required"
                    let route = if verdict = "pass" then notMeaningful else ""
                    yield comment (int64 (20 + round)) $"https://reviews/round-%d{round}" ($"<!-- fsgg:independent-review-confirmation:v1 -->\ninitial-review: https://reviews/initial\ncritic: kestrel\nround: %d{round}\npreceding-review: %s{preceding}\nreviewed-head: head-%d{round}\nverdict: %s{verdict}" + route) ]
            initial :: markers @ confirmations @
                [ comment 200L "https://reviews/accepted" $"<!-- fsgg:review-accepted:v1 -->\naccepted-head: head-%d{count}\ninitial-review: https://reviews/initial\nlatest-confirmation: https://reviews/round-%d{count}" ]
        let parsed count markers =
            match parseReviewComments (chain count markers) with
            | Ok review -> review
            | Error errors -> failwithf "expected accepted repair chain: %A" errors
        // Ordinary chains retain their smaller ceiling.
        Assert.True(Result.isError (parseReviewComments (chain 4 [])))
        let repairFour = parsed 4 [ repairMarker 1L ]
        Assert.True(repairFour.RepairPhase)
        Assert.Equal<int list>([ 1 .. 4 ], repairFour.Rounds)
        let repairTen = parsed 10 [ repairMarker 1L ]
        Assert.Equal<int list>([ 1 .. 10 ], repairTen.Rounds)
        Assert.True(receiptFresh 120L 30L { ObservedAt = 100L; SourceSha = "repair-head"; Complete = true; Review = Some { repairTen with ChecksGreen = true } })
        Assert.True(Result.isError (parseReviewComments (chain 11 [ repairMarker 1L ])))
        // Missing or non-canonical repair designations leave the chain ordinary and fail closed.
        Assert.True(Result.isError (parseReviewComments (chain 4 [])))
        Assert.True(Result.isError (parseReviewComments (chain 4 [ comment 1L "https://reviews/bad-phase" "> <!-- fsgg:independent-review-repair-phase:v1 -->" ])))
        // Multiple durable designations retain the phase boolean; neither marker consumes a confirmation.
        let multipleMarkers = parsed 10 [ repairMarker 1L; repairMarker 2L ]
        Assert.True(multipleMarkers.RepairPhase)
        Assert.Equal<int list>([ 1 .. 10 ], multipleMarkers.Rounds)

    [<Fact>]
    let ``#2127 live worker returns are resumed and invalid review chains are typed`` () =
        Assert.Equal(ResumeSameWorker, nextAction model 2 true clean [ { ClaimLive = true; ReviewReady = false; ParkedOrDone = false } ])
        let errors = validateReviewChain 3 { MarkerValid = false; CriticIdentity = None; HeadSha = None; Rounds = [ 1; 3 ]; RepairPhase = false; ChecksGreen = false; HostAccepted = false; RuntimeRouteEvidence = None; DiffAuditRequired = false; DiffAuditHead = None }
        Assert.Equal(7, List.length errors)

    /// .github#2144 repair-phase round 2 — honest receipts must also be COMPLETE.
    ///
    /// A receipt carries one rename pair, so a diff with two distinct renames cannot be covered by one.
    /// Checking a receipt only against a recomputation of its own pair therefore proved it honest about
    /// itself and nothing more: a receipt for 6 of 12 discovered occurrences, over 1 of 2 changed paths,
    /// validated. The author still chose how much of the diff the gate applied to.
    ///
    /// The four failure causes below are asserted to be DISTINGUISHABLE, not merely all-errors. .github
    /// #2207 is what collapsing causes into one sentence costs, and this parser is the one that bit.
    [<Fact>]
    let ``#2144 submitted diff audit receipts must cover every discovered occurrence`` () =
        let review =
            comment
                1L
                "https://reviews/1"
                ("<!-- fsgg:independent-review:v1 -->\ncritic: shrike\nreviewed-head: head\nverdict: pass\ndiff-audit-required: true"
                 + notMeaningful)

        let acceptance fields =
            comment
                2L
                "https://reviews/2"
                ("<!-- fsgg:review-accepted:v1 -->\naccepted-head: head\ninitial-review: https://reviews/1\nlatest-confirmation: https://reviews/1\n"
                 + fields)

        // Two files, two distinct renames, six quoted occurrences each.
        let beforeA = [ for i in 1..6 -> $"let a%d{i} = \"oldWide\"" ] |> String.concat "\n"
        let afterA = beforeA.Replace("oldWide", "newWide")
        let beforeB = [ for i in 1..6 -> $"let b%d{i} = \"oldOther\"" ] |> String.concat "\n"
        let afterB = beforeB.Replace("oldOther", "newOther")

        let dispositioned =
            List.map (fun (row: FS.GG.Coord.SemanticDiff.Occurrence) ->
                { row with Disposition = FS.GG.Coord.SemanticDiff.IntendedContractChange })

        let occurrencesA = FS.GG.Coord.SemanticDiff.inventory "src/A.fs" beforeA afterA "oldWide" "newWide"
        let occurrencesB = FS.GG.Coord.SemanticDiff.inventory "src/B.fs" beforeB afterB "oldOther" "newOther"
        Assert.Equal(6, List.length occurrencesA)
        Assert.Equal(6, List.length occurrencesB)

        let receiptFor path oldToken newToken occurrences =
            FS.GG.Coord.SemanticDiff.receipt "repo" "base" "head" oldToken newToken [ path ] true occurrences

        let expectedA = receiptFor "src/A.fs" "oldWide" "newWide" occurrencesA
        let expectedB = receiptFor "src/B.fs" "oldOther" "newOther" occurrencesB
        let submittedA = receiptFor "src/A.fs" "oldWide" "newWide" (dispositioned occurrencesA)
        let submittedB = receiptFor "src/B.fs" "oldOther" "newOther" (dispositioned occurrencesB)

        // What the engine independently established: both recomputations, and all 12 discovered.
        let trusted: FS.GG.Coord.SemanticDiff.TrustedAudit =
            { Expected = [ expectedA; expectedB ]
              Discovered = occurrencesA @ occurrencesB }

        let line (receipt: FS.GG.Coord.SemanticDiff.Receipt) =
            $"diff-audit-receipt-v1: %s{FS.GG.Coord.SemanticDiff.toBase64 receipt}"

        let errorsFor fields =
            match parseReviewCommentsWithFacts true (Some trusted) [ review; acceptance fields ] with
            | Ok _ -> []
            | Error errors -> errors

        let saysThat fragment errors =
            errors |> List.exists (fun (error: string) -> error.Contains(fragment: string))

        // 1. NOTHING SUBMITTED — its own cause, not folded into "does not cover".
        let missing = errorsFor ""
        Assert.True(saysThat "was not submitted" missing, $"%A{missing}")
        Assert.False(saysThat "discovered occurrences" missing, $"%A{missing}")

        // 2. MALFORMED — distinct from both absence and incompleteness.
        let malformed = errorsFor "diff-audit-receipt-v1: not-base64"
        Assert.True(saysThat "is malformed" malformed, $"%A{malformed}")
        Assert.False(saysThat "was not submitted" malformed, $"%A{malformed}")
        Assert.False(saysThat "discovered occurrences" malformed, $"%A{malformed}")

        // 3. HONEST BUT INCOMPLETE — the round-2 finding. One valid receipt, half the diff.
        let partial' = errorsFor (line submittedA)
        Assert.True(saysThat "account for 6 of 12 discovered occurrences" partial', $"%A{partial'}")
        Assert.False(saysThat "was not submitted" partial', $"%A{partial'}")
        Assert.False(saysThat "is malformed" partial', $"%A{partial'}")

        // 4. THE COVERING UNION — two receipts, one per discovered rename, and the gate opens.
        match parseReviewCommentsWithFacts true (Some trusted) [ review; acceptance (line submittedA + "\n" + line submittedB) ] with
        | Ok chain ->
            Assert.True(chain.DiffAuditRequired)
            Assert.Equal(Some "head", chain.DiffAuditHead)
        | Error errors -> failwithf "the covering union must be accepted, got %A" errors

        // 5. A receipt the engine never recomputed is its own cause, and is not "incomplete".
        let unknownPair =
            receiptFor "src/A.fs" "oldWide" "somethingElse" (dispositioned occurrencesA) |> line

        let unknown = errorsFor (unknownPair + "\n" + line submittedB)
        Assert.True(saysThat "did not recompute" unknown, $"%A{unknown}")

        // 6. Honesty still binds: a forged base on one receipt of an otherwise covering union.
        let forged = { submittedA with BaseSha = "forged" } |> line
        Assert.True(saysThat "is stale" (errorsFor (forged + "\n" + line submittedB)))

    [<Fact>]
    let ``#2144 required diff audit binds the complete receipt to the accepted head`` () =
        let review =
            comment
                1L
                "https://reviews/1"
                ("<!-- fsgg:independent-review:v1 -->\ncritic: shrike\nreviewed-head: head\nverdict: pass\ndiff-audit-required: true"
                 + notMeaningful)

        let acceptance fields =
            comment
                2L
                "https://reviews/2"
                ("<!-- fsgg:review-accepted:v1 -->\naccepted-head: head\ninitial-review: https://reviews/1\nlatest-confirmation: https://reviews/1\n"
                 + fields)

        let occurrence =
            FS.GG.Coord.SemanticDiff.inventory "src/A.fs" "let x = \"old\"" "let x = \"new\"" "old" "new"
            |> List.exactlyOne
            |> fun row ->
                { row with
                    Disposition = FS.GG.Coord.SemanticDiff.IntendedContractChange }

        let trusted =
            FS.GG.Coord.SemanticDiff.receipt "repo" "base" "head" "old" "new" [ "src/A.fs" ] true [ occurrence ]

        let encoded = trusted |> FS.GG.Coord.SemanticDiff.toBase64
        let valid = acceptance $"diff-audit-receipt-v1: %s{encoded}"

        match parseReviewCommentsWithAudit trusted [ review; valid ] with
        | Ok chain ->
            Assert.True(chain.DiffAuditRequired)
            Assert.Equal(Some "head", chain.DiffAuditHead)
        | Error errors -> failwithf "%A" errors

        let stale = { trusted with BaseSha = "forged" } |> FS.GG.Coord.SemanticDiff.toBase64

        Assert.True(
            Result.isError (
                parseReviewCommentsWithAudit trusted [ review; acceptance $"diff-audit-receipt-v1: %s{stale}" ]
            )
        )

        let omitted = { trusted with Occurrences = [] } |> FS.GG.Coord.SemanticDiff.toBase64

        Assert.True(
            Result.isError (
                parseReviewCommentsWithAudit trusted [ review; acceptance $"diff-audit-receipt-v1: %s{omitted}" ]
            )
        )

        Assert.True(Result.isError (parseReviewComments [ review; valid ]))
        Assert.True(Result.isError (parseReviewComments [ review; acceptance "diff-audit-receipt-v1: malformed" ]))

        let optedOut =
            { review with
                Body = review.Body.Replace("diff-audit-required: true", "diff-audit-required: false") }

        let trustedAudit: FS.GG.Coord.SemanticDiff.TrustedAudit =
            { Expected = [ trusted ]; Discovered = trusted.Occurrences }

        let callerCanDisable = Result.isOk(parseReviewCommentsWithFacts true (Some trustedAudit) [ optedOut; valid ])
        Assert.False(callerCanDisable)
        Assert.True(Result.isError(parseReviewCommentsWithFacts true None [ optedOut; acceptance "" ]))
