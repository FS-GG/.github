namespace FS.GG.Coord.Tests

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
        Assert.Equal(Resume cycle, register ledger "worker" ".github" "base" [ cycle ] |> unwrap)

    [<Fact>]
    let ``provider receipts fail closed on stale source wrong cycle and missing player journey`` () =
        let cycle = { Id = "cycle-1"; UnitId = "work"; Executor = "worker"; Repository = ".github"; BaseCommit = "base" }
        let receipt = { Schema = "provider/1"; Provider = "critique"; WorkId = "work"; CycleId = "cycle-1"; SourceRevision = "old"; CandidateHead = "head"; Verdict = "pass"; Round = 1; PlayerJourney = None }
        Assert.True(validateProvider "work" cycle "current" "head" receipt |> Result.isError)
        let good = { receipt with SourceRevision = "base"; PlayerJourney = Some true }
        let evidence = { ImplementationHead = "head"; ReviewHead = "head"; FeedbackCycle = "cycle-1"; FeedbackActive = true; MergedPr = Some 7; MergeHead = Some "head" }
        Assert.Equal(Advance cycle, advance cycle good good good evidence |> unwrap)

    [<Fact>]
    let ``completion rejects a missing roll-up cycle`` () =
        let doneLedger = { ledger with Units = ledger.Units |> List.map (fun item -> { item with Completed = true }) }
        let cycles = [ { Id = "one"; UnitId = "first"; Executor = "w"; Repository = "r"; BaseCommit = "b" }; { Id = "two"; UnitId = "second"; Executor = "w"; Repository = "r"; BaseCommit = "b" } ]
        Assert.True(complete doneLedger cycles [ "one" ] |> Result.isError)

    [<Fact>]
    let ``parallel ready units require explicit operator scheduling`` () =
        let twoReady = { ledger with Units = [ unit "one" [] false; unit "two" [] false ] }
        Assert.True(register twoReady "worker" ".github" "base" [] |> Result.isError)

    [<Fact>]
    let ``advancement requires a matching merged head and rejects an eleventh round`` () =
        let cycle = { Id = "cycle-1"; UnitId = "work"; Executor = "worker"; Repository = ".github"; BaseCommit = "base" }
        let receipt = { Schema = "provider/1"; Provider = "critique"; WorkId = "work"; CycleId = "cycle-1"; SourceRevision = "base"; CandidateHead = "head"; Verdict = "pass"; Round = 11; PlayerJourney = Some true }
        let incomplete = { ImplementationHead = "head"; ReviewHead = "head"; FeedbackCycle = "cycle-1"; FeedbackActive = true; MergedPr = None; MergeHead = None }
        Assert.True(advance cycle receipt receipt receipt incomplete |> Result.isError)
