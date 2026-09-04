namespace FS.GG.Coord.Cli

open System
open System.IO
open System.Text
open System.Text.Json
open FS.GG.Coord

module TelemetryApplication =
    let private green = ExitCode.toInt ExitCode.Green
    let private error = ExitCode.toInt ExitCode.Error
    let private fail family reasons =
        reasons |> List.iter (fun reason -> Console.Error.WriteLine($"fsgg-coord-engine: %s{family}: %s{reason}"))
        error

    let private option (name: string) (args: string list) =
        args |> List.tryFindIndex ((=) name) |> Option.bind (fun index -> args |> List.tryItem (index + 1))
    let private options (name: string) (args: string list) =
        args |> List.indexed |> List.choose (fun (index, value) -> if value = name then args |> List.tryItem (index + 1) else None)
    let private has (name: string) (args: string list) = List.contains name args
    let private required (name: string) (args: string list) =
        match option name args |> Option.filter (String.IsNullOrWhiteSpace >> not) with
        | Some value -> Ok value
        | None -> Error $"%s{name} is required"
    let private read (path: string) = File.ReadAllBytes path
    let private writeOrPrint (args: string list) (content: string) =
        match option "--output" args with
        | Some path -> File.WriteAllText(path, content, UTF8Encoding(false))
        | None -> Console.Out.Write content

    let private emitUsage (args: string list) (format: string) (rows: RuntimeUsage.UsageRow list) =
        match option "--output" args, option "--append" args with
        | Some _, Some _ -> Error [ "--output and --append are mutually exclusive" ]
        | _, Some path when format = "json" ->
            Path.GetDirectoryName(Path.GetFullPath path) |> Directory.CreateDirectory |> ignore
            let rendered = RuntimeUsage.renderJsonLines rows
            File.AppendAllText(path, rendered, UTF8Encoding(false)); Ok ()
        | _, Some path ->
            Path.GetDirectoryName(Path.GetFullPath path) |> Directory.CreateDirectory |> ignore
            let exists = File.Exists path && FileInfo(path).Length > 0L
            let filtered =
                if not exists then Ok rows else
                match RuntimeUsage.parseCsvReceipt (File.ReadAllBytes path) with
                | Error errors -> Error errors
                | Ok(_, current) ->
                    let identities = current |> List.map _.ResponseId |> Set.ofList
                    Ok(rows |> List.filter (fun row -> not (Set.contains row.ResponseId identities)))
            match filtered with
            | Error errors -> Error errors
            | Ok values ->
                let rendered = RuntimeUsage.renderCsv values
                let content = if exists then rendered.Split('\n', 2)[1] else rendered
                File.AppendAllText(path, content, UTF8Encoding(false)); Ok ()
        | Some path, None ->
            let rendered = if format = "json" then RuntimeUsage.renderJsonLines rows else RuntimeUsage.renderCsv rows
            File.WriteAllText(path, rendered, UTF8Encoding(false)); Ok ()
        | None, None ->
            Console.Out.Write(if format = "json" then RuntimeUsage.renderJsonLines rows else RuntimeUsage.renderCsv rows)
            Ok ()

    let private usage runtime args =
        try
            match required "--task" args, required "--coord-version" args, required "--sdd-version" args, required "--contracts-version" args with
            | Ok task, Ok coordination, Ok sdd, Ok contracts ->
                let result =
                    match runtime with
                    | "codex" ->
                        match required "--session-file" args with
                        | Error reason -> Error [ reason ]
                        | Ok path -> RuntimeUsage.collectCodex task (option "--turn-id" args) (has "--all-responses" args) (option "--since" args) (option "--until" args) coordination sdd contracts (read path)
                    | "claude" ->
                        match required "--snapshot" args with
                        | Error reason -> Error [ reason ]
                        | Ok path -> RuntimeUsage.collectClaude task coordination sdd contracts (read path)
                    | _ -> Error [ "runtime must be codex or claude" ]
                match result with
                | Error reasons -> fail "telemetry usage" reasons
                | Ok collection ->
                    let format = option "--format" args |> Option.defaultValue "csv"
                    if format <> "csv" && format <> "json" then fail "telemetry usage" [ "--format must be csv or json" ] else
                    match emitUsage args format collection.Rows with Ok () -> green | Error reasons -> fail "telemetry usage" reasons
            | values ->
                [ match values with Error e, _, _, _ -> yield e | _ -> ()
                  match values with _, Error e, _, _ -> yield e | _ -> ()
                  match values with _, _, Error e, _ -> yield e | _ -> ()
                  match values with _, _, _, Error e -> yield e | _ -> () ] |> fail "telemetry usage"
        with ex -> fail "telemetry usage" [ ex.Message ]

    let private findings values = values |> List.map string
    let private lifecycle action args =
        try
            match required "--run" args, required "--unit" args with
            | Ok runId, Ok unitId ->
                match action with
                | "validate" ->
                    match required "--log" args with
                    | Error reason -> fail "telemetry lifecycle" [ reason ]
                    | Ok path ->
                        let usage =
                            options "--usage" args
                            |> List.map (fun usagePath -> RuntimeUsage.parseCsvReceipt (read usagePath))
                        let usageErrors = usage |> List.collect (function Error errors -> errors | _ -> [])
                        let reports = usage |> List.choose (function Ok report -> Some report | _ -> None)
                        let history =
                            match option "--history-report" args with
                            | None -> Ok []
                            | Some historyPath -> LifecycleTelemetry.parseHistoryCsv (File.ReadAllText historyPath)
                        let historyErrors = match history with Error errors -> errors | _ -> []
                        match usageErrors @ historyErrors with
                        | errors when not errors.IsEmpty -> fail "telemetry lifecycle" errors
                        | _ ->
                            match LifecycleTelemetry.validateWithEvidence runId unitId (has "--require-terminal" args) (options "--required-phase" args) reports (history |> Result.defaultValue []) (File.ReadAllText path) with
                            | Error values -> fail "telemetry lifecycle" (findings values)
                            | Ok result -> printfn "{\"schema\":\"fsgg.telemetry.lifecycle-validation/1\",\"events\":%d,\"completedPhases\":%s,\"activePhases\":%s,\"blockedPhases\":%s}" result.EventCount (JsonSerializer.Serialize result.CompletedPhases) (JsonSerializer.Serialize result.ActivePhases) (JsonSerializer.Serialize result.BlockedPhases); green
                | "seal-successor" ->
                    match required "--draft" args with
                    | Error reason -> fail "telemetry lifecycle" [ reason ]
                    | Ok draft ->
                        let existing = option "--existing" args |> Option.map File.ReadAllText |> Option.defaultValue ""
                        let usage = options "--usage" args |> List.map (fun path -> RuntimeUsage.parseCsvReceipt (read path))
                        let usageErrors = usage |> List.collect (function Error errors -> errors | _ -> [])
                        let reports = usage |> List.choose (function Ok report -> Some report | _ -> None)
                        let history = option "--history-report" args |> Option.map (File.ReadAllText >> LifecycleTelemetry.parseHistoryCsv) |> Option.defaultValue (Ok [])
                        let historyErrors = match history with Error errors -> errors | _ -> []
                        if not (usageErrors @ historyErrors).IsEmpty then fail "telemetry lifecycle" (usageErrors @ historyErrors) else
                        match LifecycleTelemetry.sealSuccessorWithEvidence runId unitId reports (history |> Result.defaultValue []) existing (File.ReadAllText draft) with Error values -> fail "telemetry lifecycle" (findings values) | Ok value -> writeOrPrint args value; green
                | "export-comments" ->
                    match required "--comments" args with
                    | Error reason -> fail "telemetry lifecycle" [ reason ]
                    | Ok comments ->
                        match LifecycleTelemetry.exportComments runId unitId (File.ReadAllText comments) with Error values -> fail "telemetry lifecycle" (findings values) | Ok(value, rejected) -> rejected |> List.iter (fun item -> Console.Error.WriteLine($"fsgg-coord-engine: telemetry lifecycle: %A{item}")); writeOrPrint args value; green
                | _ -> fail "telemetry lifecycle" [ "action must be export-comments, seal-successor, or validate" ]
            | Error reason, _ | _, Error reason -> fail "telemetry lifecycle" [ reason ]
        with ex -> fail "telemetry lifecycle" [ ex.Message ]

    let private property (name: string) (node: JsonElement) =
        match node.TryGetProperty name with true, value -> value | _ -> invalidArg name "is required"
    let private text name node =
        let value = property name node
        if value.ValueKind <> JsonValueKind.String || String.IsNullOrWhiteSpace(value.GetString()) then invalidArg name "must be a non-empty string"
        value.GetString()
    let private bool name node = (property name node).GetBoolean()
    let private integer name node = (property name node).GetInt32()
    let private evidence (manifestPath: string) : RoadmapClosure.Inputs =
        use document = JsonDocument.Parse(ReadOnlyMemory(File.ReadAllBytes manifestPath))
        let root = document.RootElement
        let directory = Path.GetDirectoryName(Path.GetFullPath manifestPath)
        let artifactPath name = Path.GetFullPath(text name root, directory)
        let artifact name = File.ReadAllBytes(artifactPath name)
        { UnitId = text "unitId" root
          Title = text "title" root
          RoadmapSourceDigest = text "roadmapSourceDigest" root
          AcceptedReceipt = artifact "acceptedReceiptPath"
          DeliveryReceipt = artifact "deliveryReceiptPath"
          Critique = artifact "critiquePath"
          FeedbackReportPath = text "feedbackReportPath" root
          FeedbackReport = artifact "feedbackReportPath"
          FeedbackAudit = artifact "feedbackAuditPath"
          FeedbackPhases = (property "feedbackPhases" root).EnumerateArray() |> Seq.map _.GetString() |> List.ofSeq
          FeedbackCheckpoint = match root.TryGetProperty "feedbackCheckpointPath" with true, value when value.ValueKind = JsonValueKind.String -> Some(File.ReadAllText(Path.GetFullPath(value.GetString(), directory))) | _ -> None
          FeedbackBinding = artifact "feedbackBindingPath"
          CycleUpdate = artifact "cycleUpdatePath"
          CheckReceipts = (property "checkReceiptPaths" root).EnumerateArray() |> Seq.map (fun value -> File.ReadAllBytes(Path.GetFullPath(value.GetString(), directory))) |> List.ofSeq }

    let private roadmap action args =
        try
            match required "--evidence" args with
            | Error reason -> fail "roadmap close" [ reason ]
            | Ok evidencePath ->
                match RoadmapClosure.inspect (evidence evidencePath) with
                | Error reasons -> fail "roadmap close" reasons
                | Ok closed ->
                    match action with
                    | "inspect" ->
                        printfn "{\"schema\":\"fsgg.roadmap-closure/1\",\"verdict\":\"accepted\",\"unitId\":%s,\"externalObligations\":%d}" (JsonSerializer.Serialize closed.Evidence.UnitId) closed.ExternalObligations.Length
                        green
                    | "render" | "verify" ->
                        match required "--roadmap" args, required "--source-digest" args with
                        | Ok path, Ok digest ->
                            if action = "render" then
                                match RoadmapProjection.render digest (read path) closed with Error reasons -> fail "roadmap close" reasons | Ok output -> writeOrPrint args output; green
                            else
                                match required "--source-roadmap" args with
                                | Error reason -> fail "roadmap close" [ reason ]
                                | Ok sourcePath -> match RoadmapProjection.verify digest (read sourcePath) (read path) closed with Error reasons -> fail "roadmap close" reasons | Ok () -> printfn "FSGG-ROADMAP-VERIFIED %s" closed.Evidence.UnitId; green
                        | Error reason, _ | _, Error reason -> fail "roadmap close" [ reason ]
                    | _ -> fail "roadmap close" [ "action must be inspect, render, or verify" ]
        with ex -> fail "roadmap close" [ ex.Message ]

    let private summarize args =
        try
            match required "--usage" args with
            | Error reason -> fail "telemetry summarize" [ reason ]
            | Ok path ->
                match RuntimeUsage.parseJsonLines (File.ReadAllText path) with
                | Error reasons -> fail "telemetry summarize" reasons
                | Ok rows ->
                    let summary = TelemetrySummary.summarize rows
                    printfn "{\"schema\":\"fsgg.telemetry.summary/1\",\"responses\":%d,\"sessions\":%d,\"turns\":%d,\"input\":%d,\"cachedInput\":%d,\"cacheWriteInput\":%d,\"freshInput\":%d,\"output\":%d,\"reasoning\":%s,\"total\":%d}" summary.Responses summary.Sessions summary.Turns summary.Input summary.CachedInput summary.CacheWriteInput summary.FreshInput summary.Output (summary.Reasoning |> Option.map string |> Option.defaultValue "null") summary.Total
                    green
        with ex -> fail "telemetry summarize" [ ex.Message ]

    let private critique args =
        try
            match required "--cycle" args, required "--artifact" args with
            | Ok cycle, Ok path ->
                match CritiqueReceipt.validate cycle (option "--head" args) (read path) with
                | Error reasons -> fail "telemetry critique" reasons
                | Ok receipt -> printfn "FSGG-CRITIQUE-VALID %s %s %d" receipt.CycleId receipt.ReviewedCommit receipt.RepairRounds; green
            | Error reason, _ | _, Error reason -> fail "telemetry critique" [ reason ]
        with ex -> fail "telemetry critique" [ ex.Message ]

    let private feedback args =
        try
            match required "--cycle" args, required "--report" args, required "--audit" args, required "--phases" args with
            | Ok cycle, Ok report, Ok audit, Ok phases ->
                let checkpoint = option "--checkpoint" args |> Option.map File.ReadAllText
                let expected = phases.Split(',') |> Array.map _.Trim() |> Array.filter (String.IsNullOrWhiteSpace >> not) |> List.ofArray
                match FeedbackReceipt.validate cycle expected report (read report) (read audit) checkpoint with
                | Error reasons -> fail "telemetry feedback" reasons
                | Ok receipt -> printfn "FSGG-FEEDBACK-VALID %s %d" receipt.CycleId receipt.MaterialEvents; green
            | values ->
                [ match values with Error e, _, _, _ -> yield e | _ -> ()
                  match values with _, Error e, _, _ -> yield e | _ -> ()
                  match values with _, _, Error e, _ -> yield e | _ -> ()
                  match values with _, _, _, Error e -> yield e | _ -> () ] |> fail "telemetry feedback"
        with ex -> fail "telemetry feedback" [ ex.Message ]

    let tryRun argv =
        match argv with
        | "telemetry" :: "usage" :: "collect" :: runtime :: args -> Some(usage runtime args)
        | "telemetry" :: "lifecycle" :: action :: args -> Some(lifecycle action args)
        | "telemetry" :: "critique" :: "validate" :: args -> Some(critique args)
        | "telemetry" :: "feedback" :: "validate" :: args -> Some(feedback args)
        | "roadmap" :: "close" :: action :: args -> Some(roadmap action args)
        | "telemetry" :: "summarize" :: args -> Some(summarize args)
        | _ -> None
