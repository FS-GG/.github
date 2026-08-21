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
            { MarkerValid = true; Subject = None; ClaimGeneration = None; BaseSha = None
              CriticIdentity = Some "shrike"; HeadSha = Some "abc"; Rounds = [1]; RepairPhase = false
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
