namespace FS.GG.Coord.Cli

/// Pure JSON boundary for resumable roadmap/workspace cycle ledgers.
module CycleLedgerApplication =
    open System
    open System.IO
    open System.Text.Json
    open FS.GG.Coord
    open FS.GG.Coord.CycleLedger
    open FS.GG.Coord.Cli.Options

    let private input options = options.SnapshotFile |> Option.map File.ReadAllText |> Option.defaultWith Console.In.ReadToEnd
    let private fail message = Console.Error.WriteLine($"fsgg-coord-engine: cycle: %s{message}"); ExitCode.toInt ExitCode.Error
    let private property (name: string) (node: JsonElement) : JsonElement =
        match node.TryGetProperty name with | true, value -> value | _ -> invalidArg name "is required"
    let private text (name: string) (node: JsonElement) : string =
        let value = property name node
        if value.ValueKind <> JsonValueKind.String || String.IsNullOrWhiteSpace(value.GetString()) then invalidArg name "must be a non-empty string"
        value.GetString()
    let private strings (name: string) (node: JsonElement) : string list =
        let value = property name node
        if value.ValueKind <> JsonValueKind.Array then invalidArg name "must be an array"
        value.EnumerateArray()
        |> Seq.map (fun item ->
            if item.ValueKind <> JsonValueKind.String then invalidArg name "must contain strings"
            item.GetString())
        |> List.ofSeq
    let private bool (name: string) (node: JsonElement) : bool =
        match (property name node).ValueKind with | JsonValueKind.True -> true | JsonValueKind.False -> false | _ -> invalidArg name "must be a boolean"
    let private optionalText name node = match node.TryGetProperty name with | true, value when value.ValueKind = JsonValueKind.String -> Some(value.GetString()) | true, value when value.ValueKind = JsonValueKind.Null -> None | false, _ -> None | _ -> invalidArg name "must be string or null"
    let private optionalBool name node = match node.TryGetProperty name with | true, value when value.ValueKind = JsonValueKind.True -> true | true, value when value.ValueKind = JsonValueKind.False -> false | false, _ -> false | _ -> invalidArg name "must be boolean"
    let private ledger root =
        { SourceRevision = text "sourceRevision" root
          Units =
            (property "units" root).EnumerateArray()
            |> Seq.map (fun unit -> { Id = text "id" unit; Dependencies = strings "dependencies" unit; Completed = bool "completed" unit; Evidence = strings "evidence" unit })
            |> List.ofSeq }
    let private cycle node = { Id = text "id" node; UnitId = text "unitId" node; Executor = text "executor" node; Repository = text "repository" node; BaseCommit = text "baseCommit" node }
    let private receipt node =
        let journey =
            match (property "playerJourney" node).ValueKind with
            | JsonValueKind.True -> Some true
            | JsonValueKind.False -> Some false
            | JsonValueKind.Null -> None
            | _ -> invalidArg "playerJourney" "must be boolean or null"
        let round = property "round" node
        match round.TryGetInt32() with
        | false, _ -> invalidArg "round" "must be an integer"
        | true, value -> { Schema = text "schema" node; Provider = text "provider" node; WorkId = text "workId" node; CycleId = text "cycleId" node; SourceRevision = text "sourceRevision" node; CandidateHead = text "candidateHead" node; Verdict = text "verdict" node; Round = value; PlayerJourney = journey }
    let private evidence node =
        let merged = property "mergedPr" node
        let mergedPr = match merged.ValueKind with | JsonValueKind.Null -> None | _ -> match merged.TryGetInt32() with | true, value -> Some value | _ -> invalidArg "mergedPr" "must be integer or null"
        let mergeHead = match (property "mergeHead" node).ValueKind with | JsonValueKind.Null -> None | JsonValueKind.String -> Some(text "mergeHead" node) | _ -> invalidArg "mergeHead" "must be string or null"
        { ImplementationHead = text "implementationHead" node; ReviewHead = text "reviewHead" node; FeedbackCycle = text "feedbackCycle" node; FeedbackActive = bool "feedbackActive" node; MergedPr = mergedPr; MergeHead = mergeHead }
    let private render options action =
        let value = match action with | Resume cycle -> {| action = "resume"; cycleId = cycle.Id; unitId = cycle.UnitId |} | Register cycle -> {| action = "register"; cycleId = cycle.Id; unitId = cycle.UnitId |} | Advance cycle -> {| action = "advance"; cycleId = cycle.Id; unitId = cycle.UnitId |} | Escalate cycle -> {| action = "escalate"; cycleId = cycle.Id; unitId = cycle.UnitId |} | Complete -> {| action = "complete"; cycleId = ""; unitId = "" |}
        match options.Render with | Json -> printfn "%s" (JsonSerializer.Serialize {| schema = "fsgg.coord.cycle-ledger/1"; verdict = "next"; action = value.action; cycleId = value.cycleId; unitId = value.unitId |}) | Text -> printfn "%s %s" value.action value.cycleId
        ExitCode.toInt ExitCode.Green
    let run options =
        try
            let action = match options.Args with | [ value ] -> value | _ -> invalidArg "cycle" "requires exactly one action: inspect, register, advance, or complete"
            use document = JsonDocument.Parse(input options)
            let root = document.RootElement
            let model = ledger root
            match action with
            | "inspect" ->
                match inspect model with
                | Error errors -> fail (String.concat "; " errors)
                | Ok units ->
                    match options.Render with | Json -> printfn "%s" (JsonSerializer.Serialize {| schema = "fsgg.coord.cycle-ledger/1"; verdict = "ready"; units = units |> List.map _.Id |}) | Text -> units |> List.iter (fun unit -> printfn "%s" unit.Id)
                    ExitCode.toInt ExitCode.Green
            | "register" ->
                let live = (property "liveCycles" root).EnumerateArray() |> Seq.map cycle |> List.ofSeq
                match register model (text "executor" root) (text "repository" root) (text "baseCommit" root) (optionalText "selectedUnit" root) (optionalBool "parallelAuthorized" root) (optionalBool "disjointTouchSets" root) live with
                | Ok transition -> render options transition
                | Error errors -> fail (String.concat "; " errors)
            | "complete" ->
                let accepted = (property "acceptedCycles" root).EnumerateArray() |> Seq.map cycle |> List.ofSeq
                match complete model accepted (strings "rollupCycleIds" root) with
                | Ok transition -> render options transition
                | Error errors -> fail (String.concat "; " errors)
            | "advance" ->
                let target = cycle (property "cycle" root)
                match advance model target (receipt (property "implementation" root)) (receipt (property "review" root)) (receipt (property "feedback" root)) (evidence (property "evidence" root)) with
                | Ok transition -> render options transition
                | Error errors -> fail (String.concat "; " errors)
            | _ -> fail "unknown action; expected inspect, register, advance, or complete"
        with error -> fail error.Message
