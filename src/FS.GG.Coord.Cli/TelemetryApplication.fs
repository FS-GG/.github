namespace FS.GG.Coord.Cli

open System
open System.IO
open System.Diagnostics
open System.Text
open System.Text.Json
open System.Text.RegularExpressions
open FS.GG.Coord
open FS.GG.Coord.GitHub

module TelemetryApplication =
    let private green = ExitCode.toInt ExitCode.Green
    let private error = ExitCode.toInt ExitCode.Error
    let private fail family reasons =
        reasons |> List.iter (fun reason -> Console.Error.WriteLine($"fsgg-coord-engine: %s{family}: %s{reason}"))
        error

    let private option (name: string) (args: string list) =
        args
        |> List.indexed
        |> List.rev
        |> List.tryPick (fun (index, value) -> if value = name then args |> List.tryItem (index + 1) else None)
    let private options (name: string) (args: string list) =
        args |> List.indexed |> List.choose (fun (index, value) -> if value = name then args |> List.tryItem (index + 1) else None)
    let private has (name: string) (args: string list) = List.contains name args
    let private validateArgs (valueOptions: string list) (switches: string list) (args: string list) =
        let values, flags = Set.ofList valueOptions, Set.ofList switches
        let rec validate remaining =
            match remaining with
            | [] -> Ok ()
            | name :: tail when Set.contains name values ->
                match tail with
                | value :: rest when not (value.StartsWith("-", StringComparison.Ordinal)) -> validate rest
                | _ -> Error $"%s{name} requires a value"
            | name :: tail when Set.contains name flags -> validate tail
            | name :: _ when name.StartsWith("-", StringComparison.Ordinal) -> Error $"unrecognized argument '%s{name}'"
            | value :: _ -> Error $"unexpected positional argument '%s{value}'"
        validate args
    let private validated family valueOptions switches args run =
        match validateArgs valueOptions switches args with
        | Ok () -> run args
        | Error reason -> fail family [ reason ]

    let validateInvocation argv =
        let shape valueOptions switches args = validateArgs valueOptions switches args |> Some
        match argv with
        | [ "telemetry"; "usage"; "collect" ] -> Some(Ok())
        | "telemetry" :: "usage" :: "collect" :: runtime :: args when runtime = "codex" || runtime = "claude" ->
            shape
                [ "--session-file"; "--snapshot"; "--task"; "--turn-id"; "--since"; "--until"; "--format"; "--append"; "--output"; "--coord-version"; "--sdd-version"; "--contracts-version" ]
                [ "--all-responses" ] args
        | "telemetry" :: "lifecycle" :: action :: args
            when action = "export-comments" || action = "seal-successor" || action = "validate" ->
            shape
                [ "--run"; "--unit"; "--comments"; "--draft"; "--usage"; "--history-report"; "--existing"; "--log"; "--output"; "--required-phase" ]
                [ "--require-terminal"; "--require-reconciled" ] args
        | "telemetry" :: "critique" :: "validate" :: args ->
            shape [ "--cycle"; "--artifact"; "--head" ] [] args
        | "telemetry" :: "feedback" :: "validate" :: args ->
            shape [ "--cycle"; "--report"; "--audit"; "--phases"; "--checkpoint" ] [] args
        | "telemetry" :: "qualification" :: "validate" :: args ->
            shape [ "--input"; "--output" ] [] args
        | "telemetry" :: "qualification" :: "run" :: args ->
            shape [ "--input"; "--execution"; "--output" ] [] args
        | "telemetry" :: "qualification" :: "obligation" :: "render" :: args ->
            shape [ "--head"; "--kind"; "--id"; "--output" ] [] args
        | "telemetry" :: "qualification" :: "obligation" :: "verify" :: args ->
            shape [ "--head"; "--kind"; "--id"; "--readback"; "--output" ] [] args
        | "roadmap" :: "unit" :: "prepare" :: action :: args
            when action = "inspect" || action = "render" || action = "verify" ->
            shape [ "--input"; "--registry"; "--source-registry"; "--output" ] [] args
        | "roadmap" :: "unit" :: "accept" :: action :: args
            when action = "inspect" || action = "render" || action = "verify" ->
            shape [ "--input"; "--bundle"; "--output" ] [] args
        | "telemetry" :: "summarize" :: args -> shape [ "--usage" ] [] args
        | "telemetry" :: _ -> Some(Error "unknown telemetry command shape")
        | _ -> None
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
            let exists = File.Exists path && FileInfo(path).Length > 0L
            let filtered =
                if not exists then Ok rows else
                match RuntimeUsage.parseJsonLines (File.ReadAllText path) with
                | Error errors -> Error errors
                | Ok current ->
                    let identities = current |> List.map (fun row -> row.Provider, row.ResponseId) |> Set.ofList
                    Ok(rows |> List.filter (fun row -> not (Set.contains (row.Provider, row.ResponseId) identities)))
            match filtered with
            | Error errors -> Error errors
            | Ok values ->
                File.AppendAllText(path, RuntimeUsage.renderJsonLines values, UTF8Encoding(false)); Ok ()
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
                        printfn "{\"schema\":\"fsgg.roadmap-closure-candidate/1\",\"verdict\":\"internally-coherent-close-candidate\",\"unitId\":%s,\"externalObligations\":%d}" (JsonSerializer.Serialize closed.Evidence.UnitId) closed.ExternalObligations.Length
                        green
                    | "render" | "verify" ->
                        match required "--roadmap" args, required "--source-digest" args with
                        | Ok path, Ok digest ->
                            if action = "render" then
                                match RoadmapProjection.render digest (read path) closed with Error reasons -> fail "roadmap close" reasons | Ok output -> writeOrPrint args output; green
                            else
                                match required "--source-roadmap" args with
                                | Error reason -> fail "roadmap close" [ reason ]
                                | Ok sourcePath -> match RoadmapProjection.verify digest (read sourcePath) (read path) closed with Error reasons -> fail "roadmap close" reasons | Ok () -> printfn "FSGG-ROADMAP-CLOSE-CANDIDATE-VERIFIED %s" closed.Evidence.UnitId; green
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

    let private qualification action args =
        try
            match required "--input" args with
            | Error reason -> fail "telemetry qualification" [ reason ]
            | Ok path ->
                let result =
                    match action with
                    | "validate" ->
                        Qualification.parseInput (read path)
                        |> Result.bind (Qualification.validate >> Result.mapError (List.map string))
                    | "run" ->
                        match required "--execution" args with
                        | Error reason -> Error [ reason ]
                        | Ok execution -> QualificationApplication.run path execution
                    | _ -> Error [ "action must be run or validate" ]
                match result with
                | Error reasons -> fail "telemetry qualification" reasons
                | Ok accepted -> writeOrPrint args (Qualification.canonicalResult accepted); green
        with ex -> fail "telemetry qualification" [ ex.Message ]

    let private obligationDeclaration args =
        match required "--kind" args with
        | Error reason -> Error [ reason ]
        | Ok "none" when options "--id" args |> List.isEmpty -> Ok Qualification.NoObligations
        | Ok "none" -> Error [ "--kind none does not accept --id" ]
        | Ok kind ->
            match options "--id" args with
            | [ id ] when Regex.IsMatch(id, "^[a-z0-9][a-z0-9._-]*$") && Regex.IsMatch(kind, "^[a-z0-9][a-z0-9_-]*$") ->
                Ok(Qualification.Obligation { Id = id; Kind = kind })
            | [ _ ] -> Error [ "delivery obligation id or kind has invalid characters" ]
            | _ -> Error [ "a delivery obligation kind requires exactly one --id" ]

    let private inspectObligationComments head declaration (comments: QualificationEvidence.ObligationComment list) =
        let reviewComments =
            comments
            |> List.map (fun comment ->
                ({ Id = comment.CommentId; Url = comment.Url; Body = comment.Body }: Driver.ReviewComment))
        DeliveryApplication.obligationsFromComments head reviewComments
        |> Result.mapError List.singleton
        |> Result.bind (fun observed ->
            let expected =
                match declaration with
                | Qualification.NoObligations -> []
                | Qualification.Obligation obligation -> [ obligation.Id, obligation.Kind ]
            let actual = observed |> List.map (fun obligation -> obligation.Id, obligation.Kind)
            if actual = expected && comments.Length = 1 then Ok comments.Head
            else Error [ "obligation readback does not exactly match the expected current-head delivery declaration" ])

    let private qualificationObligation action args =
        try
            match required "--head" args, obligationDeclaration args with
            | Error reason, _ -> fail "telemetry qualification obligation" [ reason ]
            | _, Error reasons -> fail "telemetry qualification obligation" reasons
            | Ok head, _ when head.Length <> 40 || (head |> Seq.exists (fun value -> not (Char.IsAsciiHexDigitLower value))) ->
                fail "telemetry qualification obligation" [ "--head must be exactly 40 lowercase hexadecimal characters" ]
            | Ok head, Ok declaration when action = "render" ->
                writeOrPrint args (QualificationEvidence.renderObligationComment head declaration)
                green
            | Ok head, Ok declaration when action = "verify" ->
                let receipts =
                    options "--readback" args
                    |> List.map (fun path -> QualificationEvidence.parseObligationReadback (read path))
                let errors = receipts |> List.collect (function Error values -> values | _ -> [])
                if not errors.IsEmpty then fail "telemetry qualification obligation" errors else
                let comments = receipts |> List.choose (function Ok value -> Some value | _ -> None)
                if comments.IsEmpty then
                    fail "telemetry qualification obligation" [ "no authoritative current-head readback exists; run obligation render and create it through the guarded comment boundary" ]
                else
                match inspectObligationComments head declaration comments with
                | Error reasons -> fail "telemetry qualification obligation" reasons
                | Ok authority ->
                    let value =
                        JsonSerializer.SerializeToUtf8Bytes
                            {| schema = "fsgg.qualification.obligation-verification/1"
                               headSha = head
                               commentId = authority.CommentId
                               url = authority.Url
                               author = authority.Author |}
                        |> CanonicalJson.canonicalize
                        |> Result.defaultWith invalidOp
                    writeOrPrint args (value + "\n")
                    green
            | _, _ -> fail "telemetry qualification obligation" [ "action must be render or verify" ]
        with ex -> fail "telemetry qualification obligation" [ ex.Message ]

    let private preparation action args =
        try
            match required "--input" args, required "--roadmap" args, required "--catalog" args with
            | Ok inputPath, Ok roadmapPath, Ok catalogPath ->
                let result =
                    RoadmapWorkUnit.parsePreparationRequest (read inputPath)
                    |> Result.mapError id
                    |> Result.bind (RoadmapWorkUnit.compilePreparation (read roadmapPath) (read catalogPath) >> Result.mapError (List.map string))
                match result with
                | Error reasons -> fail "roadmap unit prepare" reasons
                | Ok plan ->
                    match action with
                    | "inspect" -> writeOrPrint args (RoadmapWorkUnit.canonicalPlan plan); green
                    | "render" ->
                        match required "--registry" args with
                        | Error reason -> fail "roadmap unit prepare" [ reason ]
                        | Ok registry ->
                            match RoadmapWorkUnit.renderPreparation (read registry) plan with
                            | Error findings -> fail "roadmap unit prepare" (findings |> List.map string)
                            | Ok rendered -> writeOrPrint args rendered; green
                    | "verify" ->
                        match required "--source-registry" args, required "--registry" args with
                        | Ok source, Ok candidate ->
                            match RoadmapWorkUnit.verifyPreparation (read source) (read candidate) plan with
                            | Error findings -> fail "roadmap unit prepare" (findings |> List.map string)
                            | Ok () -> printfn "FSGG-ROADMAP-UNIT-PREPARATION-VERIFIED %s" plan.Unit.UnitId; green
                        | Error reason, _ | _, Error reason -> fail "roadmap unit prepare" [ reason ]
                    | _ -> fail "roadmap unit prepare" [ "action must be inspect, render, or verify" ]
            | Error reason, _, _ | _, Error reason, _ | _, _, Error reason -> fail "roadmap unit prepare" [ reason ]
        with ex -> fail "roadmap unit prepare" [ ex.Message ]

    let private acceptanceCandidate action args =
        try
            match required "--input" args with
            | Error reason -> fail "roadmap unit accept" [ reason ]
            | Ok inputPath ->
                match RoadmapWorkUnit.parseAcceptanceInput (read inputPath) with
                | Error reasons -> fail "roadmap unit accept" reasons
                | Ok input ->
                    match RoadmapWorkUnit.inspectAcceptanceCandidate input with
                    | Error findings -> fail "roadmap unit accept" (findings |> List.map string)
                    | Ok candidate ->
                        match action with
                        | "inspect" ->
                            printfn "{\"schema\":\"fsgg.roadmap-unit.acceptance-candidate-verdict/1\",\"unitId\":%s,\"digest\":%s,\"verdict\":\"internally-coherent-candidate\"}" (JsonSerializer.Serialize input.Plan.Unit.UnitId) (JsonSerializer.Serialize(RoadmapWorkUnit.candidateDigest candidate))
                            green
                        | "render" -> writeOrPrint args (RoadmapWorkUnit.canonicalAcceptanceInput input); green
                        | "verify" ->
                            match required "--bundle" args with
                            | Error reason -> fail "roadmap unit accept" [ reason ]
                            | Ok bundle ->
                                match RoadmapWorkUnit.parseAcceptanceInput (read bundle) with
                                | Error reasons -> fail "roadmap unit accept" reasons
                                | Ok observed ->
                                    match RoadmapWorkUnit.inspectAcceptanceCandidate observed with
                                    | Error findings -> fail "roadmap unit accept" (findings |> List.map string)
                                    | Ok replay when RoadmapWorkUnit.candidateDigest replay = RoadmapWorkUnit.candidateDigest candidate ->
                                        printfn "FSGG-ROADMAP-UNIT-CANDIDATE-VERIFIED %s %s" input.Plan.Unit.UnitId (RoadmapWorkUnit.candidateDigest candidate)
                                        green
                                    | Ok _ -> fail "roadmap unit accept" [ "candidate envelope differs from the expected canonical input" ]
                        | _ -> fail "roadmap unit accept" [ "action must be inspect, render, or verify" ]
        with ex -> fail "roadmap unit accept" [ ex.Message ]

    let private revisionBinding args =
        let runGit repository values =
            let info = ProcessStartInfo("git")
            info.WorkingDirectory <- repository
            info.RedirectStandardOutput <- true
            info.RedirectStandardError <- true
            info.UseShellExecute <- false
            values |> List.iter info.ArgumentList.Add
            use gitProcess = Process.Start info
            let output = gitProcess.StandardOutput.ReadToEnd().Trim()
            let errorText = gitProcess.StandardError.ReadToEnd().Trim()
            gitProcess.WaitForExit()
            gitProcess.ExitCode, output, errorText
        try
            match required "--repository" args, required "--repository-id" args, required "--candidate" args, required "--merge" args with
            | Ok repository, Ok repositoryId, Ok candidate, Ok merge ->
                let candidateExit, candidateTree, candidateError = runGit repository [ "rev-parse"; candidate + "^{tree}" ]
                let mergeExit, mergeTree, mergeError = runGit repository [ "rev-parse"; merge + "^{tree}" ]
                let equalityExit, _, equalityError = runGit repository [ "diff"; "--quiet"; candidate + "^{tree}"; merge + "^{tree}" ]
                if candidateExit <> 0 || mergeExit <> 0 || equalityExit <> 0 then
                    fail "roadmap unit revision" [ candidateError; mergeError; equalityError ]
                else
                    let binding = RoadmapWorkUnit.sealRevisionBinding repositoryId candidate merge candidateTree mergeTree 0
                    writeOrPrint args (RoadmapWorkUnit.canonicalRevisionBinding binding)
                    green
            | Error reason, _, _, _ | _, Error reason, _, _ | _, _, Error reason, _ | _, _, _, Error reason ->
                fail "roadmap unit revision" [ reason ]
        with ex -> fail "roadmap unit revision" [ ex.Message ]

    let tryRun argv =
        match argv with
        | "telemetry" :: "usage" :: "collect" :: runtime :: args ->
            Some(validated "telemetry usage"
                    [ "--session-file"; "--snapshot"; "--task"; "--turn-id"; "--since"; "--until"; "--format"; "--append"; "--output"; "--coord-version"; "--sdd-version"; "--contracts-version" ]
                    [ "--all-responses" ] args (usage runtime))
        | "telemetry" :: "lifecycle" :: action :: args ->
            let valueOptions, switches =
                match action with
                | "validate" -> [ "--run"; "--unit"; "--log"; "--usage"; "--history-report"; "--required-phase" ], [ "--require-terminal"; "--require-reconciled" ]
                | "seal-successor" -> [ "--run"; "--unit"; "--draft"; "--existing"; "--usage"; "--history-report"; "--output" ], []
                | "export-comments" -> [ "--run"; "--unit"; "--comments"; "--output" ], []
                | _ -> [], []
            Some(validated "telemetry lifecycle" valueOptions switches args (lifecycle action))
        | "telemetry" :: "critique" :: "validate" :: args ->
            Some(validated "telemetry critique" [ "--cycle"; "--artifact"; "--head" ] [] args critique)
        | "telemetry" :: "feedback" :: "validate" :: args ->
            Some(validated "telemetry feedback" [ "--cycle"; "--report"; "--audit"; "--phases"; "--checkpoint" ] [] args feedback)
        | "telemetry" :: "qualification" :: "validate" :: args ->
            Some(validated "telemetry qualification" [ "--input"; "--output" ] [] args (qualification "validate"))
        | "telemetry" :: "qualification" :: "run" :: args ->
            Some(validated "telemetry qualification" [ "--input"; "--execution"; "--output" ] [] args (qualification "run"))
        | "telemetry" :: "qualification" :: "obligation" :: "render" :: args ->
            Some(validated "telemetry qualification obligation" [ "--head"; "--kind"; "--id"; "--output" ] [] args (qualificationObligation "render"))
        | "telemetry" :: "qualification" :: "obligation" :: "verify" :: args ->
            Some(validated "telemetry qualification obligation" [ "--head"; "--kind"; "--id"; "--readback"; "--output" ] [] args (qualificationObligation "verify"))
        | "roadmap" :: "close" :: action :: args ->
            let valueOptions =
                match action with
                | "inspect" -> [ "--evidence" ]
                | "render" -> [ "--evidence"; "--roadmap"; "--source-digest"; "--output" ]
                | "verify" -> [ "--evidence"; "--roadmap"; "--source-digest"; "--source-roadmap" ]
                | _ -> []
            Some(validated "roadmap close" valueOptions [] args (roadmap action))
        | "roadmap" :: "unit" :: "prepare" :: "apply" :: _ -> None
        | "roadmap" :: "unit" :: "prepare" :: action :: args ->
            Some(validated "roadmap unit prepare" [ "--input"; "--roadmap"; "--catalog"; "--registry"; "--source-registry"; "--output" ] [] args (preparation action))
        | "roadmap" :: "unit" :: "accept" :: "seal" :: _ -> None
        | "roadmap" :: "unit" :: "accept" :: action :: args ->
            Some(validated "roadmap unit accept" [ "--input"; "--bundle"; "--output" ] [] args (acceptanceCandidate action))
        | "roadmap" :: "unit" :: "revision" :: "inspect" :: args ->
            Some(validated "roadmap unit revision" [ "--repository"; "--repository-id"; "--candidate"; "--merge"; "--output" ] [] args revisionBinding)
        | "telemetry" :: "summarize" :: args ->
            Some(validated "telemetry summarize" [ "--usage" ] [] args summarize)
        | _ -> None
