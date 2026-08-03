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
    let private optionalText (name: string) (node: JsonElement) = match node.TryGetProperty name with | true, value when value.ValueKind = JsonValueKind.String -> Some(value.GetString()) | true, value when value.ValueKind = JsonValueKind.Null -> None | false, _ -> None | _ -> invalidArg name "must be string or null"
    let private optionalBool (name: string) (node: JsonElement) = match node.TryGetProperty name with | true, value when value.ValueKind = JsonValueKind.True -> true | true, value when value.ValueKind = JsonValueKind.False -> false | false, _ -> false | _ -> invalidArg name "must be boolean"
    let private ledger root =
        { SourceRevision = text "sourceRevision" root
          Units =
            (property "units" root).EnumerateArray()
            |> Seq.map (fun unit ->
                { Id = text "id" unit
                  Dependencies = strings "dependencies" unit
                  Completed = bool "completed" unit
                  Evidence = strings "evidence" unit
                  PlayerJourneyRequired = bool "playerJourneyRequired" unit })
            |> List.ofSeq }
    let private cycle node = { Id = text "id" node; UnitId = text "unitId" node; Executor = text "executor" node; Repository = text "repository" node; BaseCommit = text "baseCommit" node }
    let private updateReceipt node =
        { Schema = text "schema" node
          CycleId = text "cycleId" node
          UnitId = text "unitId" node
          SourceRevision = text "sourceRevision" node
          ImplementationHead = text "implementationHead" node
          ReviewHead = text "reviewHead" node
          FeedbackCycle = text "feedbackCycle" node
          FeedbackActive = bool "feedbackActive" node
          MergedPr =
            match (property "mergedPr" node).TryGetInt32() with
            | true, value -> value
            | _ -> invalidArg "mergedPr" "must be an integer"
          MergeHead = text "mergeHead" node
          EvidencePaths = strings "evidencePaths" node
          Dispositions = strings "dispositions" node
          EvidenceDigest = text "evidenceDigest" node }
    let private receipt node =
        let path = text "artifactPath" node
        if not (File.Exists path) then invalidArg "artifactPath" $"does not exist: %s{path}"
        match parseProviderReceipt (File.ReadAllBytes path) with
        | Ok parsed -> parsed
        | Error errors -> invalidArg "artifactPath" (String.concat "; " errors)
    let private evidence node =
        let merged = property "mergedPr" node
        let mergedPr = match merged.ValueKind with | JsonValueKind.Null -> None | _ -> match merged.TryGetInt32() with | true, value -> Some value | _ -> invalidArg "mergedPr" "must be integer or null"
        let mergeHead = match (property "mergeHead" node).ValueKind with | JsonValueKind.Null -> None | JsonValueKind.String -> Some(text "mergeHead" node) | _ -> invalidArg "mergeHead" "must be string or null"
        { ImplementationHead = text "implementationHead" node; ReviewHead = text "reviewHead" node; FeedbackCycle = text "feedbackCycle" node; FeedbackActive = bool "feedbackActive" node; MergedPr = mergedPr; MergeHead = mergeHead; EvidencePaths = strings "evidencePaths" node; Dispositions = strings "dispositions" node }
    let private render options action =
        match action with
        | Update(cycle, receipt) ->
            let updateReceipt =
                {| schema = receipt.Schema
                   cycleId = receipt.CycleId
                   unitId = receipt.UnitId
                   sourceRevision = receipt.SourceRevision
                   implementationHead = receipt.ImplementationHead
                   reviewHead = receipt.ReviewHead
                   feedbackCycle = receipt.FeedbackCycle
                   feedbackActive = receipt.FeedbackActive
                   mergedPr = receipt.MergedPr
                   mergeHead = receipt.MergeHead
                   evidencePaths = receipt.EvidencePaths
                   dispositions = receipt.Dispositions
                   evidenceDigest = receipt.EvidenceDigest |}
            match options.Render with
            | Json -> printfn "%s" (JsonSerializer.Serialize {| schema = "fsgg.coord.cycle-ledger/1"; verdict = "next"; action = "update"; cycleId = cycle.Id; unitId = cycle.UnitId; updateReceipt = updateReceipt |})
            | Text -> printfn "update %s %s" cycle.Id receipt.EvidenceDigest
        | _ ->
            let value = match action with | Resume cycle -> {| action = "resume"; cycleId = cycle.Id; unitId = cycle.UnitId |} | Register cycle -> {| action = "register"; cycleId = cycle.Id; unitId = cycle.UnitId |} | Advance cycle -> {| action = "advance"; cycleId = cycle.Id; unitId = cycle.UnitId |} | Escalate cycle -> {| action = "escalate"; cycleId = cycle.Id; unitId = cycle.UnitId |} | Complete -> {| action = "complete"; cycleId = ""; unitId = "" |} | Update _ -> failwith "unreachable"
            match options.Render with | Json -> printfn "%s" (JsonSerializer.Serialize {| schema = "fsgg.coord.cycle-ledger/1"; verdict = "next"; action = value.action; cycleId = value.cycleId; unitId = value.unitId |}) | Text -> printfn "%s %s" value.action value.cycleId
        ExitCode.toInt ExitCode.Green
    let run options =
        try
            let action = match options.Args with | [ value ] -> value | _ -> invalidArg "cycle" "requires exactly one action: inspect, register, advance, update, or complete"
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
                let guarded = (property "guardedUpdates" root).EnumerateArray() |> Seq.map updateReceipt |> List.ofSeq
                match complete model accepted guarded (strings "rollupCycleIds" root) with
                | Ok transition -> render options transition
                | Error errors -> fail (String.concat "; " errors)
            | "advance" ->
                let target = cycle (property "cycle" root)
                match advance model target (receipt (property "implementation" root)) (receipt (property "review" root)) (receipt (property "feedback" root)) (evidence (property "evidence" root)) with
                | Ok transition -> render options transition
                | Error errors -> fail (String.concat "; " errors)
            | "update" ->
                let target = cycle (property "cycle" root)
                match update model target (evidence (property "evidence" root)) with
                | Ok transition -> render options transition
                | Error errors -> fail (String.concat "; " errors)
            | _ -> fail "unknown action; expected inspect, register, advance, update, or complete"
        with error -> fail error.Message
