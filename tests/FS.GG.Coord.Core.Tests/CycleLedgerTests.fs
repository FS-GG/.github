namespace FS.GG.Coord.Tests

open System
open Xunit
open FS.GG.Coord.CycleLedger

module CycleLedgerTests =
    let unit id dependencies completed = { Id = id; Dependencies = dependencies; Completed = completed; Evidence = [] }
    let ledger = { SourceRevision = "ledger-sha"; Units = [ unit "first" [] false; unit "second" [ "first" ] false ] }
    let unwrap = function Ok value -> value | Error errors -> failwithf "%A" errors

    [<Fact>]
    let ``cycle ledger exposes only dependency-ready units and resumes a matching cycle`` () =
        Assert.Equal<string list>([ "first" ], inspect ledger |> unwrap |> List.map _.Id)
        let cycle = { Id = cycleId "first" "worker" ".github" "base"; UnitId = "first"; Executor = "worker"; Repository = ".github"; BaseCommit = "base" }
        Assert.Equal(Resume cycle, register ledger "worker" ".github" "base" None false false [ cycle ] |> unwrap)

    [<Fact>]
    let ``provider receipts fail closed on stale source wrong cycle and missing player journey`` () =
        let cycle = { Id = cycleId "work" "worker" ".github" "base"; UnitId = "work"; Executor = "worker"; Repository = ".github"; BaseCommit = "base" }
        let digest = "sha256:" + String.replicate 64 "a"
        let receipt = { Schema = "fsgg.critique.report/3"; Provider = "critique"; WorkId = "work"; CycleId = cycle.Id; SourceRevision = "old"; CandidateHead = "head"; Verdict = "pass"; Round = 1; PlayerJourney = None; JourneyRequired = true; GeneratorId = "FS.GG.Critique"; GeneratorVersion = "1.0.0"; ArtifactDigest = digest }
        Assert.True(validateProvider "work" cycle "current" "head" "critique" "fsgg.critique.report/3" "FS.GG.Critique" receipt |> Result.isError)
        let good = { receipt with SourceRevision = "base"; PlayerJourney = Some true }
        let evidence = { ImplementationHead = "head"; ReviewHead = "head"; FeedbackCycle = cycle.Id; FeedbackActive = true; MergedPr = Some 7; MergeHead = Some "head"; EvidencePaths = [ "evidence" ]; Dispositions = [ "accepted" ] }
        let implementation = { good with Provider = "fsgg-sdd"; Schema = "fsgg.sdd.report/1"; PlayerJourney = Some true; GeneratorId = "FS.GG.SDD.Artifacts" }
        let feedback = { good with Provider = "feedback"; Schema = "fsgg.feedback.report/2"; PlayerJourney = Some true; GeneratorId = "FS.GG.Feedback" }
        Assert.Equal(Advance cycle, advance { SourceRevision = "base"; Units = [ unit "work" [] false ] } cycle implementation good feedback evidence |> unwrap)
        Assert.True(advance { SourceRevision = "fresh"; Units = [ unit "work" [] false ] } cycle implementation good feedback evidence |> Result.isError)
        Assert.True(advance { SourceRevision = "base"; Units = [ unit "work" [] false ] } cycle { implementation with Provider = "invented" } good feedback evidence |> Result.isError)

    [<Fact>]
    let ``completion rejects a missing roll-up cycle`` () =
        let doneLedger = { ledger with Units = ledger.Units |> List.map (fun item -> { item with Completed = true; Evidence = [ "evidence" ] }) }
        let cycles = [ { Id = "one"; UnitId = "first"; Executor = "w"; Repository = "r"; BaseCommit = "b" }; { Id = "two"; UnitId = "second"; Executor = "w"; Repository = "r"; BaseCommit = "b" } ]
        Assert.True(complete doneLedger cycles [] [ "one" ] |> Result.isError)

    [<Fact>]
    let ``parallel ready units require explicit operator scheduling`` () =
        let twoReady = { ledger with Units = [ unit "one" [] false; unit "two" [] false ] }
        Assert.True(register twoReady "worker" ".github" "base" None false false [] |> Result.isError)
        Assert.True(register twoReady "worker" ".github" "base" (Some "one") true true [] |> Result.isOk)

    [<Fact>]
    let ``advancement requires a matching merged head and rejects an eleventh round`` () =
        let cycle = { Id = "cycle-1"; UnitId = "work"; Executor = "worker"; Repository = ".github"; BaseCommit = "base" }
        let receipt = { Schema = "fsgg.critique.report/3"; Provider = "critique"; WorkId = "work"; CycleId = "cycle-1"; SourceRevision = "base"; CandidateHead = "head"; Verdict = "pass"; Round = 11; PlayerJourney = Some true; JourneyRequired = true; GeneratorId = "FS.GG.Critique"; GeneratorVersion = "1.0.0"; ArtifactDigest = "sha256:" + String.replicate 64 "a" }
        let incomplete = { ImplementationHead = "head"; ReviewHead = "head"; FeedbackCycle = "cycle-1"; FeedbackActive = true; MergedPr = None; MergeHead = None; EvidencePaths = []; Dispositions = [] }
        Assert.True(advance { SourceRevision = "base"; Units = [ unit "work" [] false ] } cycle receipt receipt receipt incomplete |> Result.isError)

    [<Fact>]
    let ``inspect rejects a dependency cycle and completion rejects unchecked units`` () =
        let cyclic = { SourceRevision = "source"; Units = [ unit "a" [ "b" ] false; unit "b" [ "a" ] false ] }
        Assert.True(inspect cyclic |> Result.isError)
        let accepted = [ { Id = "a"; UnitId = "a"; Executor = "w"; Repository = "r"; BaseCommit = "source" } ]
        Assert.True(complete { SourceRevision = "source"; Units = [ unit "a" [] false ] } accepted [] [ "a" ] |> Result.isError)
