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

    /// Does any error message carry this fragment?  Hoisted (#2221 review round 2) because four tests
    /// had defined it identically, and one of them needed it before its own definition.
    let saysThat fragment (errors: string list) = errors |> List.exists (fun e -> e.Contains(fragment: string))

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
                        ReceiptId = observationReceiptId kind 100L "snapshot" outcome } ]
              ContentIntakes = []
              ContentDispositions = [] }

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
    let ``#2162 triage content dispositions bind a durable consumer or evidenced one-off decision`` () =
        let observation kind outcome =
            { Kind = kind
              ObservedAt = 100L
              SourceSha = "snapshot"
              Outcome = outcome
              ReceiptId = observationReceiptId kind 100L "snapshot" outcome }
        let disposition kind consumers rationale evidence =
            { SourceFinding = "audit/2162: reusable failure boundary"
              Disposition = kind
              ConsumerPaths = consumers
              DecisionMaker = "host-2162"
              Rationale = rationale
              Evidence = evidence
              ObservedAt = 100L
              SourceSha = "snapshot"
              ReceiptId = contentDispositionReceiptId "audit/2162: reusable failure boundary" kind consumers "host-2162" rationale evidence 100L "snapshot" }
        let receipt contentDispositions =
            { ObservedAt = 100L
              SourceSha = "snapshot"
              Complete = true
              ConsolidationApproved = true
              Observations =
                [ "reconcile-dry-run", "clean"
                  "reconcile-apply", "applied-or-not-needed"
                  "reconcile-fresh", "clean"
                  "triage", "fresh"
                  "engine-currency", "current-scoped" ]
                |> List.map (fun (kind, outcome) -> observation kind outcome)
              ContentIntakes = [ "audit/2162: reusable failure boundary" ]
              ContentDispositions = contentDispositions }

        let reusable =
            disposition SkillAndExampleFixture
                [ ".agents/skills/drive-board/references/backlog-triage.md"
                  "tests/FS.GG.Coord.Cli.Tests/ApplicationServiceTests.fs" ]
                "The audit identifies a recurring worker-routing boundary."
                None
        Assert.True(planningReceiptFresh 120L 30L "snapshot" (receipt [ reusable ]))

        let proseOnly =
            disposition ExampleFixture [ "docs/coordination/triage.md" ] "A document is not an executable consumer." None
        Assert.False(planningReceiptFresh 120L 30L "snapshot" (receipt [ proseOnly ]))

        let oneOff = disposition NotReusable [] "Measured as a repository-specific one-off with no reusable operator learning." (Some(EvidencePath "tests/FS.GG.Coord.Core.Tests/DriverTests.fs:130"))
        Assert.True(planningReceiptFresh 120L 30L "snapshot" (receipt [ oneOff ]))

        let unsupportedOneOff =
            let evidence = None
            { oneOff with
                Evidence = evidence
                ReceiptId = contentDispositionReceiptId oneOff.SourceFinding oneOff.Disposition oneOff.ConsumerPaths oneOff.DecisionMaker oneOff.Rationale evidence oneOff.ObservedAt oneOff.SourceSha }
        Assert.False(planningReceiptFresh 120L 30L "snapshot" (receipt [ unsupportedOneOff ]))

        let malformedPath =
            let evidence = Some(EvidencePath "not-a-path:not-a-line")
            { oneOff with
                Evidence = evidence
                ReceiptId = contentDispositionReceiptId oneOff.SourceFinding oneOff.Disposition oneOff.ConsumerPaths oneOff.DecisionMaker oneOff.Rationale evidence oneOff.ObservedAt oneOff.SourceSha }
        Assert.False(planningReceiptFresh 120L 30L "snapshot" (receipt [ malformedPath ]))

        let httpsEvidence =
            let evidence = Some(EvidenceUrl "https://github.com/FS-GG/.github/issues/2162")
            { oneOff with
                Evidence = evidence
                ReceiptId = contentDispositionReceiptId oneOff.SourceFinding oneOff.Disposition oneOff.ConsumerPaths oneOff.DecisionMaker oneOff.Rationale evidence oneOff.ObservedAt oneOff.SourceSha }
        Assert.True(planningReceiptFresh 120L 30L "snapshot" (receipt [ httpsEvidence ]))

        Assert.False(planningReceiptFresh 120L 30L "snapshot" { receipt [] with ContentIntakes = [ "audit/2162: reusable failure boundary" ] })

        let stale = { reusable with SourceSha = "old" }
        Assert.False(planningReceiptFresh 120L 30L "snapshot" (receipt [ stale ]))

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
    let ``#2451 a generic agent-type critic identity is not proof of the same critic, even when it repeats`` () =
        // The negative case this row exists to prevent: two markers naming the bare agent-type string
        // `fsgg-critic-normal` — the exact shape measured live during .github#2417's own review, where
        // two DIFFERENT critics both posted it. Every OTHER field agrees perfectly (same initial URL,
        // round, preceding-comment link, increasing id) — the ONLY thing that could make this look like
        // "the same critic confirmed" is the string equality on `critic`, and that equality is
        // satisfiable by any critic ever dispatched at that route, so it must never be accepted.
        let initial =
            comment
                1L
                "https://reviews/1"
                "<!-- fsgg:independent-review:v1 -->\ncritic: fsgg-critic-normal\nreviewed-head: first\nverdict: changes-required"

        let confirm =
            comment
                2L
                "https://reviews/2"
                ("<!-- fsgg:independent-review-confirmation:v1 -->\ninitial-review: https://reviews/1\ncritic: fsgg-critic-normal\nround: 1\npreceding-review: https://reviews/1\nreviewed-head: second\nverdict: pass"
                 + notMeaningful)

        let accepted =
            comment
                3L
                "https://reviews/3"
                "<!-- fsgg:review-accepted:v1 -->\naccepted-head: second\ninitial-review: https://reviews/1\nlatest-confirmation: https://reviews/2"

        match parseReviewComments [ initial; confirm; accepted ] with
        | Ok chain ->
            failwithf
                "GATE INVERSION: a generic agent-type critic identity was accepted as proof of same-critic continuity: %A"
                chain
        | Error errors -> Assert.True(saysThat "minted, distinguishing identity" errors, sprintf "%A" errors)

    [<Fact>]
    let ``#2451 isGenericCriticIdentity recognises the bare agent-type shape and not a minted one`` () =
        Assert.True(isGenericCriticIdentity "fsgg-critic-normal")
        Assert.True(isGenericCriticIdentity "fsgg-critic-best")
        Assert.True(isGenericCriticIdentity "  fsgg-critic-normal  ")
        Assert.False(isGenericCriticIdentity "brant-99e5")
        Assert.False(isGenericCriticIdentity "shrike")
        Assert.False(isGenericCriticIdentity "")
        Assert.False(isGenericCriticIdentity null)

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

    /// .github#2221 — a comment that merely QUOTES a marker must not invalidate the real chain.
    ///
    /// Measured live on PR #2205 at head `6ba838ac`: one ordinary ready-for-review handoff whose only
    /// sin was naming `<!-- fsgg:independent-review-repair-phase:v1 -->` inside backticks made a
    /// syntactically perfect chain — posted LATER, by the critic — unparseable. The blast radius was the
    /// PR, not the comment, and the message named a marker KIND, so the reader was sent to the one
    /// correct marker of that kind and concluded the critic had got it wrong.
    ///
    /// The boundary encoded here: evidence is a marker occupying a WHOLE LINE in the comment's leading
    /// marker block; every other occurrence is INERT — never evidence, never an error. Only a marker
    /// REPEATED in that block competes with itself, and that error names the comment that carried it.
    [<Fact>]
    let ``#2221 quoted markers are inert, competing markers fail closed, and errors name the comment`` () =
        let initial =
            comment
                10L
                "https://reviews/initial"
                ("<!-- fsgg:independent-review:v1 -->\ncritic: heron\nreviewed-head: head\nverdict: pass"
                 + notMeaningful)

        let accepted =
            comment
                20L
                "https://reviews/accepted"
                "<!-- fsgg:review-accepted:v1 -->\naccepted-head: head\ninitial-review: https://reviews/initial\nlatest-confirmation: https://reviews/initial"

        let chainWith extra = [ initial; accepted ] @ extra
        let bystander id body = comment id $"https://reviews/bystander-%d{id}" body

        // The control: the chain alone parses. Every row below adds ONE bystander to exactly this.
        match parseReviewComments (chainWith []) with
        | Ok chain -> Assert.True(chain.HostAccepted)
        | Error errors -> failwithf "the control chain must parse: %A" errors

        let quotedRepairPhase =
            bystander
                30L
                "Ready for review. Per `independent-review` §3 the critic's initial review should additionally carry `<!-- fsgg:independent-review-repair-phase:v1 -->` naming that exhausted PR and escalation URL."

        let quotations =
            [ quotedRepairPhase
              // A fenced example, the marker alone on its own line inside the fence.
              bystander 31L "Post this:\n\n```\n<!-- fsgg:independent-review:v1 -->\ncritic: <you>\n```\n"
              // A blockquote of a real chain.
              bystander 32L "> <!-- fsgg:independent-review-confirmation:v1 -->\n> round: 1\n"
              // Bare bytes after prose, with no quoting of any kind.
              bystander 33L "The host posts <!-- fsgg:review-accepted:v1 --> once it accepts the head.\n"
              // An indented code block — markdown's other quotation form, alongside the fence.
              bystander 34L "Escalation:\n\n    <!-- fsgg:independent-review-escalation:v1 -->\n" ]

        for quotation in quotations do
            match parseReviewComments (chainWith [ quotation ]) with
            | Ok chain -> Assert.True(chain.HostAccepted)
            | Error errors -> failwithf "comment %d invalidated the chain: %A" quotation.Id errors

        match parseReviewComments (chainWith quotations) with
        | Ok chain -> Assert.True(chain.HostAccepted)
        | Error errors -> failwithf "the quotations together invalidated the chain: %A" errors

        // THE ONE AMBIGUOUS FORM, decided out loud (#2221 review round 2). A bare escalation marker on
        // its own line at column 0 after prose, in a comment with no fields, is byte-identical to a
        // MISPLACED field-less escalation marker — escalation has no field grammar, so nothing can
        // separate them. Silently calling it a quotation is the #266 shape; it is refused by name, and
        // the message says how to write either one you meant.
        let ambiguous =
            bystander 35L "the escalation marker is spelled\n<!-- fsgg:independent-review-escalation:v1 -->\n"

        match parseReviewComments (chainWith [ ambiguous ]) with
        | Ok _ -> failwith "an unclassifiable field-less marker must not be silently decided"
        | Error errors ->
            Assert.True(saysThat "comment 35" errors, $"%A{errors}")
            Assert.True(saysThat "no fields to tell a real one from a mention" errors, $"%A{errors}")
            Assert.True(saysThat "fence it, indent it, or write it inline" errors, $"%A{errors}")

        // And both escapes from that refusal work: fencing it or indenting it is fixture 31/34's shape.
        for escaped in
            [ bystander 38L "the escalation marker is spelled\n\n```\n<!-- fsgg:independent-review-escalation:v1 -->\n```\n"
              bystander 39L "the escalation marker is spelled\n\n    <!-- fsgg:independent-review-escalation:v1 -->\n" ] do
            match parseReviewComments (chainWith [ escaped ]) with
            | Ok chain -> Assert.True(chain.HostAccepted)
            | Error errors -> failwithf "comment %d should be inert: %A" escaped.Id errors

        // Inert means inert IN BOTH DIRECTIONS: a quoted repair-phase designation is not read as one.
        match parseReviewComments (chainWith [ quotedRepairPhase ]) with
        | Ok chain -> Assert.False(chain.RepairPhase)
        | Error errors -> failwithf "%A" errors

        // GitHub returns comment bodies with CRLF endings; the block is defined over lines, not bytes.
        let crlf (c: ReviewComment) = { c with Body = c.Body.Replace("\n", "\r\n") }

        match parseReviewComments [ crlf initial; crlf accepted; crlf quotedRepairPhase ] with
        | Ok chain -> Assert.True(chain.HostAccepted)
        | Error errors -> failwithf "CRLF bodies must parse: %A" errors

        // COMPETING, not quoted: one kind twice in one comment's leading block has one meaning and
        // cannot be given two. The error names the comment, not just the kind.
        let repeated =
            bystander
                40L
                "<!-- fsgg:independent-review:v1 -->\n<!-- fsgg:independent-review:v1 -->\ncritic: heron\nreviewed-head: head\nverdict: pass"

        match parseReviewComments (chainWith [ repeated ]) with
        | Ok _ -> failwith "a repeated canonical leading marker must fail closed"
        | Error errors ->
            Assert.True(saysThat "comment 40" errors, $"%A{errors}")
            Assert.True(saysThat "https://reviews/bystander-40" errors, $"%A{errors}")

        // Two comments each canonically carrying the initial marker: still exactly-one, and the message
        // names BOTH candidates so the reader can see which pair competes.
        let second =
            bystander 41L "<!-- fsgg:independent-review:v1 -->\ncritic: heron\nreviewed-head: head\nverdict: pass"

        match parseReviewComments (chainWith [ second ]) with
        | Ok _ -> failwith "two competing initial markers must fail closed"
        | Error errors ->
            Assert.True(saysThat "comment 10" errors, $"%A{errors}")
            Assert.True(saysThat "comment 41" errors, $"%A{errors}")

        // A chain with no initial marker at all still says so, and says it is missing rather than naming
        // a comment that does not exist.
        match parseReviewComments [ accepted ] with
        | Ok _ -> failwith "a chain with no initial marker must fail closed"
        | Error errors -> Assert.True(saysThat "no comment carries one" errors, $"%A{errors}")

        // `independent-review` §"Repair phase" step 3 sanctions the designation riding in "the same
        // comment" as the initial review (#2221 comment 5179330334). The offset-0 predecessor could not
        // represent that layout at all, so a critic following the reference literally produced an
        // unparseable chain. Two markers, two whole lines, one comment: both canonical.
        let colocated =
            comment
                10L
                "https://reviews/initial"
                ("<!-- fsgg:independent-review:v1 -->\n<!-- fsgg:independent-review-repair-phase:v1 -->\ncritic: heron\nreviewed-head: head\nverdict: pass"
                 + notMeaningful)

        match parseReviewComments [ colocated; accepted ] with
        | Ok chain -> Assert.True(chain.RepairPhase)
        | Error errors -> failwithf "a co-located repair-phase designation must parse: %A" errors

    /// .github#2221 review round 1, finding 1 — MISPLACED IS NOT QUOTED.
    ///
    /// Deleting `malformedMarker` outright traded one #266 shape for its mirror image. Only `initial` and
    /// `acceptances` are `requireOne`-guarded, so a confirmation, escalation or repair-phase comment that
    /// fails the placement rule ceased to exist: no error, no residue, and the parser then emitted
    /// `Ok (HostAccepted = true)`. "I could not read this comment" became "I read every comment and the
    /// chain is accepted".
    ///
    /// The escape found by the critic, reproduced here on the fixture route: a LATER confirmation carrying
    /// `verdict: changes-required` whose marker line has ONE LEADING SPACE. At column 0 it fails closed on
    /// `the latest review confirmation must have verdict pass`; indented by one space it vanished, and the
    /// chain came back accepted. The prose-first escalation does the same through
    /// `review escalation requires a repair-phase marker`.
    [<Fact>]
    let ``#2221 a misplaced marker is refused by name, not silently dropped`` () =
        let initial =
            comment
                10L
                "https://reviews/initial"
                ("<!-- fsgg:independent-review:v1 -->\ncritic: heron\nreviewed-head: head\nverdict: pass"
                 + notMeaningful)

        let accepted =
            comment
                20L
                "https://reviews/accepted"
                "<!-- fsgg:review-accepted:v1 -->\naccepted-head: head\ninitial-review: https://reviews/initial\nlatest-confirmation: https://reviews/initial"

        // The control: this chain is accepted, and stays accepted throughout.
        match parseReviewComments [ initial; accepted ] with
        | Ok chain -> Assert.True(chain.HostAccepted)
        | Error errors -> failwithf "the control chain must parse: %A" errors

        // ESCAPE 1 — a later `changes-required` confirmation, one leading space before its marker.
        let confirmationBody =
            "<!-- fsgg:independent-review-confirmation:v1 -->\ninitial-review: https://reviews/initial\ncritic: heron\nround: 1\npreceding-review: https://reviews/initial\nreviewed-head: head\nverdict: changes-required"

        // At column 0 it is a real confirmation and the chain fails closed on its verdict — proof that a
        // recognised comment of this shape is what makes the difference.
        match parseReviewComments [ initial; comment 15L "https://reviews/round-1" confirmationBody; accepted ] with
        | Ok _ -> failwith "a changes-required latest confirmation must fail closed"
        | Error errors -> Assert.True(saysThat "latest review confirmation must have verdict pass" errors, $"%A{errors}")

        let indented = comment 15L "https://reviews/round-1" (" " + confirmationBody)

        match parseReviewComments [ initial; indented; accepted ] with
        | Ok chain ->
            failwithf
                "a misplaced confirmation was dropped and the chain was accepted (repairPhase=%b, accepted=%b)"
                chain.RepairPhase
                chain.HostAccepted
        | Error errors ->
            Assert.True(saysThat "misplaced marker" errors, $"%A{errors}")
            Assert.True(saysThat "comment 15" errors, $"%A{errors}")
            Assert.True(saysThat "https://reviews/round-1" errors, $"%A{errors}")

        // ESCAPE 2 — a prose-first escalation. Dropping it removes `review escalation requires a
        // repair-phase marker`, so the failure mode is an acceptance rather than a refusal.
        //
        // ROUND 2: THIS IS THE ESCAPE VERBATIM, and the round-1 version of this test was not. It wrote
        // `critic:`/`reviewed-head:` under the marker; the escape the critic measured wrote `head:`. Only
        // the first satisfies a field-keyed condition, so the repair "closed" a case nobody had found
        // while seven of the critic's eight real shapes were still dropped. Escalation and repair-phase
        // have NO field grammar in this parser, so the key under the marker is chosen ad hoc — which is
        // exactly why the rule may not key on one. Every shape the critic measured is pinned below.
        let escalationBodies =
            [ "head", "head: abc123"
              "current-head", "current-head: abc123"
              "escalated-head", "escalated-head: abc123"
              "unresolved", "unresolved: finding 1 remains"
              "pr", "pr: https://github.com/FS-GG/.github/pull/2205"
              "confirmations", "confirmations: url1, url2, url3"
              "no fields at all", ""
              "reviewed-head", "reviewed-head: abc123" ]

        for label, fieldLine in escalationBodies do
            let proseFirstEscalation =
                comment
                    16L
                    "https://reviews/escalation"
                    ("Ordinary chain exhausted; three confirmations still report material findings.\n\n<!-- fsgg:independent-review-escalation:v1 -->\n"
                     + fieldLine)

            match parseReviewComments [ initial; proseFirstEscalation; accepted ] with
            | Ok chain ->
                failwithf
                    "a misplaced escalation with body key '%s' was dropped and the chain was accepted (repairPhase=%b, accepted=%b)"
                    label
                    chain.RepairPhase
                    chain.HostAccepted
            | Error errors ->
                Assert.True(saysThat "independent-review-escalation marker" errors, $"%s{label}: %A{errors}")
                Assert.True(saysThat "comment 16" errors, $"%s{label}: %A{errors}")

        // The canonical form of the same comment is still READ — it must produce the escalation's own
        // error, not the misplacement one. Without this the test would pass on a parser that refused
        // every escalation.
        match
            parseReviewComments
                [ initial; comment 16L "https://reviews/escalation" "<!-- fsgg:independent-review-escalation:v1 -->\nhead: abc123"; accepted ]
        with
        | Ok _ -> failwith "an escalation with no repair-phase marker must fail closed"
        | Error errors ->
            Assert.True(saysThat "review escalation requires a repair-phase marker" errors, $"%A{errors}")
            Assert.False(saysThat "comment 16" errors, $"%A{errors}")

        // ESCAPE 2b — the repair-phase designation, the ZERO-FIELD shape .github#2136's own fixture uses.
        // Dropping it reports a repair-phase landing as an ordinary one AND silently lowers the
        // confirmation ceiling from 10 to 3.
        let repairPhaseBody prefix =
            prefix + "<!-- fsgg:independent-review-repair-phase:v1 -->\nexhausted-pr: 2144\nescalation: https://x/y"

        match parseReviewComments [ initial; comment 17L "https://reviews/rp" (repairPhaseBody ""); accepted ] with
        | Ok chain -> Assert.True(chain.RepairPhase, "the canonical designation must still be read")
        | Error errors -> failwithf "the canonical repair-phase designation must parse: %A" errors

        for prefix in [ "Entering the repair phase.\n\n"; " " ] do
            match parseReviewComments [ initial; comment 17L "https://reviews/rp" (repairPhaseBody prefix); accepted ] with
            | Ok chain ->
                failwithf
                    "a misplaced repair-phase designation was dropped: repairPhase=%b, accepted=%b"
                    chain.RepairPhase
                    chain.HostAccepted
            | Error errors ->
                Assert.True(saysThat "independent-review-repair-phase marker" errors, $"%A{errors}")
                Assert.True(saysThat "comment 17" errors, $"%A{errors}")

        // A CRLF body reaches the same verdict — the rule is over lines, not bytes.
        match parseReviewComments [ initial; { indented with Body = indented.Body.Replace("\n", "\r\n") }; accepted ] with
        | Ok _ -> failwith "a misplaced confirmation with CRLF endings was dropped"
        | Error errors -> Assert.True(saysThat "misplaced marker" errors, $"%A{errors}")

        // AND THE QUOTATIONS STAY INERT. Both halves of the discriminator are load-bearing, so each of
        // these is a comment that satisfies exactly one of them.
        let bystander id body = comment id $"https://reviews/bystander-%d{id}" body

        let quotations =
            [ // marker bytes, no protocol fields at all
              bystander 30L "Ready for review. Per `independent-review` §3 the critic's initial review should additionally carry `<!-- fsgg:independent-review-repair-phase:v1 -->` naming that exhausted PR and escalation URL."
              // marker AND a field line, both inside a fence
              bystander 31L "Post this:\n\n```\n<!-- fsgg:independent-review:v1 -->\ncritic: <you>\n```\n"
              // a blockquoted marker and a blockquoted field: `> round: 1` is not a field line
              bystander 32L "> <!-- fsgg:independent-review-confirmation:v1 -->\n> round: 1\n"
              bystander 33L "The host posts <!-- fsgg:review-accepted:v1 --> once it accepts the head.\n"
              // an indented code block — markdown's other quotation form, alongside the fence
              bystander 34L "Escalation:\n\n    <!-- fsgg:independent-review-escalation:v1 -->\n"
              // a whole-line marker after prose, with the fields quoted in a fence below it
              bystander 36L "the confirmation is spelled\n<!-- fsgg:independent-review-confirmation:v1 -->\n\n```\nround: 1\nverdict: pass\n```\n"
              // protocol fields, but no marker bytes anywhere
              bystander 37L "round: 1\nverdict: pass\ncritic: heron\n" ]

        for quotation in quotations do
            match parseReviewComments [ initial; quotation; accepted ] with
            | Ok chain -> Assert.True(chain.HostAccepted)
            | Error errors -> failwithf "quotation in comment %d invalidated the chain: %A" quotation.Id errors

        match parseReviewComments ([ initial; accepted ] @ quotations) with
        | Ok chain -> Assert.True(chain.HostAccepted)
        | Error errors -> failwithf "the quotations together invalidated the chain: %A" errors

    /// .github#2221 review round 1, finding 2 — the likeliest marker error named nothing.
    ///
    /// `| _ -> Error [ "review markers are malformed" ]` fires whenever any of the six field reads on the
    /// initial or acceptance comment fails, and it discarded which comment, which field, and whether the
    /// field was absent or written twice. It is reachable by this item's own condition: a review comment
    /// that quotes an example `verdict:` line at column 0 inside a fence makes `field "verdict"` see two
    /// values, because `fieldValues` reads every line.
    [<Fact>]
    let ``#2221 a failed field read names the comment, the field, and missing versus duplicated`` () =
        let accepted =
            comment
                20L
                "https://reviews/accepted"
                "<!-- fsgg:review-accepted:v1 -->\naccepted-head: head\ninitial-review: https://reviews/initial\nlatest-confirmation: https://reviews/initial"

        let initialWith suffix =
            comment
                10L
                "https://reviews/initial"
                ("<!-- fsgg:independent-review:v1 -->\ncritic: heron\nreviewed-head: head\nverdict: pass"
                 + notMeaningful
                 + suffix)

        match parseReviewComments [ initialWith ""; accepted ] with
        | Ok chain -> Assert.True(chain.HostAccepted)
        | Error errors -> failwithf "the control chain must parse: %A" errors

        // DUPLICATED — the escape: an example field list quoted in a fence inside the review comment.
        match parseReviewComments [ initialWith "\n\nWrite it like this:\n\n```\nverdict: pass\n```\n"; accepted ] with
        | Ok _ -> failwith "a duplicated field must fail closed"
        | Error errors ->
            Assert.True(saysThat "comment 10" errors, $"%A{errors}")
            Assert.True(saysThat "https://reviews/initial" errors, $"%A{errors}")
            Assert.True(saysThat "'verdict'" errors, $"%A{errors}")
            Assert.True(saysThat "2 times" errors, $"%A{errors}")
            Assert.False(saysThat "review markers are malformed" errors, $"%A{errors}")

        // MISSING — a different repair, and it must read as a different sentence.
        let withoutCritic =
            comment
                10L
                "https://reviews/initial"
                ("<!-- fsgg:independent-review:v1 -->\nreviewed-head: head\nverdict: pass" + notMeaningful)

        match parseReviewComments [ withoutCritic; accepted ] with
        | Ok _ -> failwith "a missing field must fail closed"
        | Error errors ->
            Assert.True(saysThat "comment 10" errors, $"%A{errors}")
            Assert.True(saysThat "does not carry the required 'critic' field" errors, $"%A{errors}")
            Assert.False(saysThat "2 times" errors, $"%A{errors}")

        // The failure is attributed to the right COMMENT: a host-acceptance field names comment 20.
        let acceptedWithoutHead =
            comment
                20L
                "https://reviews/accepted"
                "<!-- fsgg:review-accepted:v1 -->\ninitial-review: https://reviews/initial\nlatest-confirmation: https://reviews/initial"

        match parseReviewComments [ initialWith ""; acceptedWithoutHead ] with
        | Ok _ -> failwith "a missing acceptance field must fail closed"
        | Error errors ->
            Assert.True(saysThat "comment 20" errors, $"%A{errors}")
            Assert.True(saysThat "'accepted-head'" errors, $"%A{errors}")
            Assert.False(saysThat "comment 10" errors, $"%A{errors}")

        // EMPTY is neither missing nor duplicated, and says so.
        let emptyVerdict =
            comment
                10L
                "https://reviews/initial"
                ("<!-- fsgg:independent-review:v1 -->\ncritic: heron\nreviewed-head: head\nverdict:" + notMeaningful)

        match parseReviewComments [ emptyVerdict; accepted ] with
        | Ok _ -> failwith "an empty field must fail closed"
        | Error errors -> Assert.True(saysThat "with an empty value" errors, $"%A{errors}")

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

    [<Fact>]
    let ``#2175 reviewPhaseFacts reads the same marker classification parseReviewComments reads, additively`` () =
        // Empty: nothing present, everything default/None.
        let empty = reviewPhaseFacts []
        Assert.Equal(0, empty.InitialCount)
        Assert.False(empty.InitialPresent)
        Assert.Equal(None, empty.CriticIdentity)
        Assert.Equal(0, empty.ConfirmationCount)
        Assert.False(empty.EscalationPresent)
        Assert.False(empty.RepairPhasePresent)
        Assert.Equal(0, empty.AcceptanceCount)
        Assert.False(empty.AcceptancePresent)

        let initial =
            comment
                1L
                "https://reviews/1"
                "<!-- fsgg:independent-review:v1 -->\ncritic: shrike\nreviewed-head: abc\nverdict: changes-required"

        let single = reviewPhaseFacts [ initial ]
        Assert.Equal(1, single.InitialCount)
        Assert.True(single.InitialPresent)
        Assert.Equal(Some "shrike", single.CriticIdentity)
        Assert.Equal(Some "abc", single.InitialHeadSha)
        Assert.Equal(Some "changes-required", single.InitialVerdict)
        Assert.Equal(Some "changes-required", single.LatestVerdict)
        Assert.Equal(Some "abc", single.LatestReviewedHeadSha)

        // A duplicate initial marker across two comments is a distinct, counted fact — not silently
        // collapsed to "the first one" (.github#2175 acceptance 8; the gap `Review.fs`'s guard closes).
        let duplicateInitial =
            comment
                2L
                "https://reviews/2"
                "<!-- fsgg:independent-review:v1 -->\ncritic: heron\nreviewed-head: def\nverdict: pass"

        let duplicated = reviewPhaseFacts [ initial; duplicateInitial ]
        Assert.Equal(2, duplicated.InitialCount)
        Assert.True(duplicated.InitialPresent)

        let confirmed =
            comment
                2L
                "https://reviews/2"
                "<!-- fsgg:independent-review-confirmation:v1 -->\ninitial-review: https://reviews/1\ncritic: shrike\nround: 1\npreceding-review: https://reviews/1\nreviewed-head: xyz\nverdict: pass"

        let afterConfirmation = reviewPhaseFacts [ initial; confirmed ]
        Assert.Equal(1, afterConfirmation.ConfirmationCount)
        Assert.Equal(Some "pass", afterConfirmation.LatestVerdict)
        Assert.Equal(Some "xyz", afterConfirmation.LatestReviewedHeadSha)
        // The INITIAL fields stay bound to the initial comment, not the latest confirmation.
        Assert.Equal(Some "abc", afterConfirmation.InitialHeadSha)
        Assert.Equal(Some "changes-required", afterConfirmation.InitialVerdict)

        let repairPhase =
            comment 3L "https://reviews/repair" "<!-- fsgg:independent-review-repair-phase:v1 -->"

        let escalation =
            comment 4L "https://reviews/escalation" "<!-- fsgg:independent-review-escalation:v1 -->"

        let withPhaseMarkers = reviewPhaseFacts [ initial; repairPhase; escalation ]
        Assert.True(withPhaseMarkers.RepairPhasePresent)
        Assert.True(withPhaseMarkers.EscalationPresent)

        let acceptance =
            comment
                5L
                "https://reviews/accepted"
                "<!-- fsgg:review-accepted:v1 -->\naccepted-head: abc\ninitial-review: https://reviews/1\nlatest-confirmation: https://reviews/1"

        let withAcceptance = reviewPhaseFacts [ initial; acceptance ]
        Assert.Equal(1, withAcceptance.AcceptanceCount)
        Assert.True(withAcceptance.AcceptancePresent)

        // A quoted mention inside a fence is not canonical — `reviewPhaseFacts` reuses the SAME
        // leading-marker-block/quoting detection `parseReviewComments` uses, so a quotation is inert
        // here too (.github#2221/#2175 acceptance 11: no second, less careful marker parser).
        let quoted =
            comment
                6L
                "https://reviews/quoted"
                "Example:\n\n```\n<!-- fsgg:independent-review:v1 -->\ncritic: x\nreviewed-head: y\nverdict: pass\n```\n"

        let withQuote = reviewPhaseFacts [ quoted ]
        Assert.Equal(0, withQuote.InitialCount)
        Assert.False(withQuote.InitialPresent)
