namespace FS.GG.Coord.Tests

open System
open System.Text
open Xunit
open FS.GG.Coord.CycleLedger

module CycleLedgerTests =
    let unit id dependencies completed journeyRequired =
        { Id = id
          Dependencies = dependencies
          Completed = completed
          Evidence = []
          PlayerJourneyRequired = journeyRequired }

    let ledger =
        { SourceRevision = "ledger-sha"
          Units = [ unit "first" [] false false; unit "second" [ "first" ] false false ] }

    let unwrap = function Ok value -> value | Error errors -> failwithf "%A" errors

    let providerBytes provider schema generator version workId cycleId source head verdict round playerJourney =
        let journey = playerJourney |> Option.map string |> Option.defaultValue "null" |> _.ToLowerInvariant()
        $"""{{"schema":"%s{schema}","provider":"%s{provider}","workId":"%s{workId}","cycleId":"%s{cycleId}","sourceRevision":"%s{source}","candidateHead":"%s{head}","verdict":"%s{verdict}","round":%d{round},"playerJourney":%s{journey},"generator":{{"id":"%s{generator}","version":"%s{version}"}}}}"""
        |> Encoding.UTF8.GetBytes

    let receipt provider schema generator version workId cycleId source head verdict round playerJourney =
        providerBytes provider schema generator version workId cycleId source head verdict round playerJourney
        |> parseProviderReceipt
        |> unwrap

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
        let valid = receipt "critique" "fsgg.critique.report/3" "FS.GG.Critique" "1.0.0" "work" target.Id "base" "head" "pass" 1 (Some true)
        Assert.True(validateProvider "work" target "base" "head" "critique" "fsgg.critique.report/3" "FS.GG.Critique" valid |> Result.isOk)

        let inventedVersion = receipt "critique" "fsgg.critique.report/3" "FS.GG.Critique" "9.9.9" "work" target.Id "base" "head" "pass" 1 (Some true)
        let errors = validateProvider "work" target "base" "head" "critique" "fsgg.critique.report/3" "FS.GG.Critique" inventedVersion
        let message = match errors with Error reasons -> String.concat "; " reasons | Ok () -> ""
        Assert.Contains("unsupported", message)

        let malformed = Encoding.UTF8.GetBytes "{\"schema\":\"fsgg.critique.report/3\"}"
        Assert.True(parseProviderReceipt malformed |> Result.isError)

    [<Fact>]
    let ``player journey applicability is derived from the ledger unit`` () =
        let target = cycle "work" "base"
        let model = { SourceRevision = "base"; Units = [ unit "work" [] false true ] }
        let implementation = receipt "fsgg-sdd" "fsgg.sdd.report/1" "FS.GG.SDD.Artifacts" "1.0.0" "work" target.Id "base" "head" "pass" 0 None
        let review = receipt "critique" "fsgg.critique.report/3" "FS.GG.Critique" "1.0.0" "work" target.Id "base" "head" "pass" 1 None
        let feedback = receipt "feedback" "fsgg.feedback.report/2" "FS.GG.Feedback" "1.0.0" "work" target.Id "base" "head" "pass" 0 None
        Assert.True(advance model target implementation review feedback (evidence target "head") |> Result.isError)
        let passingReview = receipt "critique" "fsgg.critique.report/3" "FS.GG.Critique" "1.0.0" "work" target.Id "base" "head" "pass" 1 (Some true)
        Assert.Equal(Advance target, advance model target implementation passingReview feedback (evidence target "head") |> unwrap)

    [<Fact>]
    let ``guarded update emits the exact receipt completion revalidates`` () =
        let target = cycle "work" "base"
        let model = { SourceRevision = "base"; Units = [ { unit "work" [] true false with Evidence = [ "evidence/report.json" ] } ] }
        let guarded =
            match update model target (evidence target "head") |> unwrap with
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
        let twoReady = { ledger with Units = [ unit "one" [] false false; unit "two" [] false false ] }
        Assert.True(register twoReady "worker" ".github" "base" None false false [] |> Result.isError)
        let target = cycle "one" "base"
        Assert.Equal(Resume target, register twoReady "worker" ".github" "base" (Some "one") true true [ target ] |> unwrap)

    [<Fact>]
    let ``inspect rejects a dependency cycle and completion rejects unchecked units`` () =
        let cyclic = { SourceRevision = "source"; Units = [ unit "a" [ "b" ] false false; unit "b" [ "a" ] false false ] }
        Assert.True(inspect cyclic |> Result.isError)
        let accepted = [ cycle "a" "source" ]
        Assert.True(complete { SourceRevision = "source"; Units = [ unit "a" [] false false ] } accepted [] [ accepted.Head.Id ] |> Result.isError)
