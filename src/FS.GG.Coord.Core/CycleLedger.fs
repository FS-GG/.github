namespace FS.GG.Coord

open System
open System.Security.Cryptography
open System.Text

module CycleLedger =
    type Unit = { Id: string; Dependencies: string list; Completed: bool; Evidence: string list }
    type Ledger = { SourceRevision: string; Units: Unit list }
    type Cycle = { Id: string; UnitId: string; Executor: string; Repository: string; BaseCommit: string }
    type ProviderReceipt =
        { Schema: string; Provider: string; WorkId: string; CycleId: string; SourceRevision: string
          CandidateHead: string; Verdict: string; Round: int; PlayerJourney: bool option }
    type Evidence =
        { ImplementationHead: string; ReviewHead: string; FeedbackCycle: string; FeedbackActive: bool
          MergedPr: int option; MergeHead: string option }
    type Action = | Resume of Cycle | Register of Cycle | Advance of Cycle | Complete

    let private required name value =
        if String.IsNullOrWhiteSpace value then Some $"%s{name} is required" else None

    let cycleId unitId executor repository baseCommit =
        [ unitId; executor; repository; baseCommit ]
        |> String.concat "\n"
        |> Encoding.UTF8.GetBytes
        |> SHA256.HashData
        |> Convert.ToHexString
        |> fun digest -> "cycle-" + digest.ToLowerInvariant().Substring(0, 16)

    let inspect ledger =
        let ids = ledger.Units |> List.map _.Id
        let errors =
            [ yield! required "ledger source revision" ledger.SourceRevision |> Option.toList
              for unit in ledger.Units do
                  yield! required "unit id" unit.Id |> Option.toList
                  for dependency in unit.Dependencies do
                      if not (List.contains dependency ids) then yield $"unit %s{unit.Id} names unknown dependency %s{dependency}"
                      if dependency = unit.Id then yield $"unit %s{unit.Id} depends on itself"
              if ids |> List.distinct |> List.length <> ids.Length then yield "ledger unit ids must be unique" ]
        if not (List.isEmpty errors) then Error errors
        else
            let complete id = ledger.Units |> List.exists (fun unit -> unit.Id = id && unit.Completed)
            ledger.Units |> List.filter (fun unit -> not unit.Completed && (unit.Dependencies |> List.forall complete)) |> Ok

    let register ledger executor repository baseCommit live =
        match inspect ledger with
        | Error errors -> Error errors
        | Ok ready ->
            match ready with
            | [] -> if ledger.Units |> List.forall _.Completed then Ok Complete else Error [ "no dependency-ready ledger unit exists" ]
            | [ unit ] ->
                let errors = [ yield! required "executor" executor |> Option.toList; yield! required "repository" repository |> Option.toList; yield! required "base commit" baseCommit |> Option.toList ]
                if not (List.isEmpty errors) then Error errors
                else
                    let id = cycleId unit.Id executor repository baseCommit
                    match live |> List.filter (fun cycle -> cycle.UnitId = unit.Id) with
                    | [] -> Ok(Register { Id = id; UnitId = unit.Id; Executor = executor; Repository = repository; BaseCommit = baseCommit })
                    | [ cycle ] when cycle.Id = id -> Ok(Resume cycle)
                    | _ -> Error [ $"unit %s{unit.Id} has an incompatible live cycle" ]
            | _ -> Error [ "multiple dependency-ready units require explicit operator scheduling" ]

    let validateProvider expectedWorkId expectedCycle expectedSourceRevision expectedHead receipt =
        let errors =
            [ yield! required "provider schema" receipt.Schema |> Option.toList
              yield! required "provider" receipt.Provider |> Option.toList
              if receipt.WorkId <> expectedWorkId then yield "provider receipt work id does not match"
              if receipt.CycleId <> expectedCycle.Id then yield "provider receipt cycle id does not match"
              if receipt.SourceRevision <> expectedSourceRevision then yield "provider receipt source revision is stale"
              if receipt.CandidateHead <> expectedHead then yield "provider receipt candidate head does not match"
              if receipt.Verdict <> "pass" then yield "provider receipt verdict is not pass"
              if receipt.Round < 0 || receipt.Round > 10 then yield "provider receipt round is outside the supported range" ]
        let errors =
            if receipt.Provider = "critique" && receipt.PlayerJourney <> Some true then
                errors @ [ "critique receipt is missing a passing player journey" ]
            else errors
        if List.isEmpty errors then Ok () else Error errors

    let advance cycle implementation review feedback evidence =
        let expected = evidence.ImplementationHead
        let validate receipt = validateProvider cycle.UnitId cycle cycle.BaseCommit expected receipt
        let errors =
            [ for receipt in [ implementation; review; feedback ] do
                  match validate receipt with Error reasons -> yield! reasons | Ok () -> ()
              if evidence.ReviewHead <> expected then yield "review evidence head does not match implementation head"
              if evidence.FeedbackCycle <> cycle.Id then yield "feedback evidence cycle does not match"
              if not evidence.FeedbackActive then yield "feedback activation is missing"
              if review.Round = 10 && review.Verdict <> "pass" then yield "tenth review round requires escalation rather than advancement" ]
        if List.isEmpty errors then Ok(Advance cycle) else Error errors

    let complete ledger accepted rollupCycleIds =
        match inspect ledger with
        | Error errors -> Error errors
        | Ok _ ->
            let acceptedUnits = accepted |> List.map _.UnitId
            let required = ledger.Units |> List.map _.Id
            let missing = required |> List.filter (fun id -> not (List.contains id acceptedUnits))
            let missingRollup = accepted |> List.map _.Id |> List.filter (fun id -> not (List.contains id rollupCycleIds))
            if not (List.isEmpty missing) then Error [ "required units are missing accepted cycles: " + String.concat ", " missing ]
            elif not (List.isEmpty missingRollup) then Error [ "accepted cycles are missing from roll-up: " + String.concat ", " missingRollup ]
            else Ok Complete
