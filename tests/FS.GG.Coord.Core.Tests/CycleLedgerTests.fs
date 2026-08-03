namespace FS.GG.Coord.Tests

open System
open System.Text
open Xunit
open FS.GG.Coord.CycleLedger

module CycleLedgerTests =
    let unit id dependencies completed =
        { Id = id
          ProviderCycleId = "roadmap-cycle-ledger-m1-" + id
          Dependencies = dependencies
          Completed = completed
          Evidence = [] }

    let ledger =
        { SourceRevision = "ledger-sha"
          Units = [ unit "first" [] false; unit "second" [ "first" ] false ] }

    let unwrap = function Ok value -> value | Error errors -> failwithf "%A" errors

    let sddBytes workId =
        $"""{{"schemaVersion":1,"workId":"%s{workId}","stage":"verify","status":"verificationReady","generator":"FS.GG.SDD.Artifacts/1.0.0","readiness":"verificationReady","diagnostics":[]}}"""
        |> Encoding.UTF8.GetBytes

    let critiqueBytes cycleId head game journey =
        let journeys = if journey then "[{\"entry_point\":\"product-boot\",\"input_surface\":\"player-control-messages\",\"reached\":true}]" else "[]"
        $"""{{"schema_version":3,"cycle_id":"%s{cycleId}","repair_rounds":0,"confirmation":{{"reviewed_commit":"%s{head}","verdict":"pass"}},"game_functionality":%s{(string game).ToLowerInvariant()},"player_journeys":%s{journeys},"uncovered_functionality":[]}}"""
        |> Encoding.UTF8.GetBytes

    let feedbackBytes cycleId =
        [ "---"
          "feedbackSchema: 2"
          $"cycle: %s{cycleId}"
          "---"
          "## §1 Provenance and confidence"
          "- **activation:** active"
          "- **phases:** onboarding-first-build, lifecycle-authoring, implementation-test-evidence, verify-ship-pr"
          "## §2 Findings" ]
        |> String.concat "\n"
        |> Encoding.UTF8.GetBytes

    let receipt workId target source head provider bytes =
        parseProviderReceipt workId target source head provider bytes |> unwrap

    let cycle unitId source =
        { Id = cycleId unitId "worker" ".github" source
          UnitId = unitId
          Executor = "worker"
          Repository = ".github"
          BaseCommit = source }

    let evidence target head =
        { ImplementationHead = head
          ReviewHead = head
          FeedbackCycle = target.Id
          FeedbackActive = true
          MergedPr = Some 7
          MergeHead = Some head
          EvidencePaths = [ "evidence/report.json" ]
          Dispositions = [ "all-findings-disposed" ] }

    [<Fact>]
    let ``cycle ledger exposes only dependency-ready units and resumes a matching cycle`` () =
        Assert.Equal<string list>([ "first" ], inspect ledger |> unwrap |> List.map _.Id)
        let target = cycle "first" "base"
        Assert.Equal(Resume target, register ledger "worker" ".github" "base" None false false [ target ] |> unwrap)

    [<Fact>]
    let ``provider artifacts are byte-bound and reject unsupported generator provenance`` () =
        let target = cycle "work" "base"
        let providerCycle = "roadmap-cycle-ledger-m1-work"
        let valid = receipt providerCycle target "base" "head" "critique" (critiqueBytes providerCycle "head" false false)
        Assert.True(validateProvider "work" target "base" "head" "critique" "fsgg.critique.report/3" "FS.GG.Critique.Validator" valid |> Result.isOk)

        let normalizedForgery = Encoding.UTF8.GetBytes $"""{{"schema":"fsgg.critique.report/3","provider":"critique","workId":"work","cycleId":"%s{target.Id}","sourceRevision":"base","candidateHead":"head","verdict":"pass","round":0,"playerJourney":null,"generator":{{"id":"FS.GG.Critique.Validator","version":"1.0.0"}}}}"""
        let forged = parseProviderReceipt "work" target "base" "head" "critique" normalizedForgery
        Assert.True(forged |> Result.isError)

    [<Fact>]
    let ``player journey applicability is derived from the ledger unit`` () =
        let target = cycle "work" "base"
        let model = { SourceRevision = "base"; Units = [ unit "work" [] false ] }
        let providerCycle = model.Units.Head.ProviderCycleId
        let implementation = receipt "work" target "base" "head" "fsgg-sdd" (sddBytes "work")
        let feedback = receipt providerCycle target "base" "head" "feedback" (feedbackBytes providerCycle)
        Assert.True(parseProviderReceipt providerCycle target "base" "head" "critique" (critiqueBytes providerCycle "head" true false) |> Result.isError)
        let passingReview = receipt providerCycle target "base" "head" "critique" (critiqueBytes providerCycle "head" true true)
        Assert.Equal(Advance target, advance model target implementation passingReview feedback (evidence target "head") |> unwrap)

    [<Fact>]
    let ``guarded update emits the exact receipt completion revalidates`` () =
        let target = cycle "work" "base"
        let model = { SourceRevision = "base"; Units = [ { unit "work" [] true with Evidence = [ "evidence/report.json" ] } ] }
        let guarded =
            match update model target (evidence target "head") "0123456789abcdef0123456789abcdef" |> unwrap with
            | Update(actual, receipt) ->
                Assert.Equal(target, actual)
                Assert.Equal("fsgg.coord.cycle-update/1", receipt.Schema)
                Assert.StartsWith("sha256:", receipt.EvidenceDigest)
                receipt
            | other -> failwithf "expected update, got %A" other
        Assert.Equal(Complete, complete model [ target ] [ guarded ] [ target.Id ] |> unwrap)
        Assert.True(complete model [ target ] [ { guarded with EvidenceDigest = "sha256:" + String.replicate 64 "a" } ] [ target.Id ] |> Result.isError)

    [<Fact>]
    let ``completion rejects a missing roll-up cycle`` () =
        let doneLedger = { ledger with Units = ledger.Units |> List.map (fun item -> { item with Completed = true; Evidence = [ "evidence" ] }) }
        let cycles = [ cycle "first" "ledger-sha"; cycle "second" "ledger-sha" ]
        Assert.True(complete doneLedger cycles [] [ cycles.Head.Id ] |> Result.isError)

    [<Fact>]
    let ``parallel ready units require explicit operator scheduling and resume the selected live cycle`` () =
        let twoReady = { ledger with Units = [ unit "one" [] false; unit "two" [] false ] }
        Assert.True(register twoReady "worker" ".github" "base" None false false [] |> Result.isError)
        let target = cycle "one" "base"
        Assert.Equal(Resume target, register twoReady "worker" ".github" "base" (Some "one") true true [ target ] |> unwrap)

    [<Fact>]
    let ``inspect rejects a dependency cycle and completion rejects unchecked units`` () =
        let cyclic = { SourceRevision = "source"; Units = [ unit "a" [ "b" ] false; unit "b" [ "a" ] false ] }
        Assert.True(inspect cyclic |> Result.isError)
        let accepted = [ cycle "a" "source" ]
        Assert.True(complete { SourceRevision = "source"; Units = [ unit "a" [] false ] } accepted [] [ accepted.Head.Id ] |> Result.isError)
