namespace FS.GG.Coord.Cli

/// Pure JSON boundary for resumable roadmap/workspace cycle ledgers.
module CycleLedgerApplication =
    open System
    open System.Diagnostics
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
                  ProviderCycleId = text "providerCycleId" unit
                  Dependencies = strings "dependencies" unit
                  Completed = bool "completed" unit
                  Evidence = strings "evidence" unit })
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
          Nonce = text "nonce" node
          EvidenceDigest = text "evidenceDigest" node }
    let private providerRoot node =
        optionalText "rootPath" node
        |> Option.defaultValue "."
        |> Path.GetFullPath

    let private runValidator workingDirectory executable arguments =
        let start = ProcessStartInfo(executable)
        start.WorkingDirectory <- workingDirectory
        start.UseShellExecute <- false
        start.RedirectStandardOutput <- true
        start.RedirectStandardError <- true
        for argument in arguments do start.ArgumentList.Add argument
        use child = Process.Start start
        let output = child.StandardOutput.ReadToEnd()
        let error = child.StandardError.ReadToEnd()
        child.WaitForExit()
        if child.ExitCode <> 0 then
            invalidArg "artifactPath" $"provider validator refused the artifact: %s{error.Trim()}"
        output

    let private relativeArtifact root path =
        let absolute = Path.GetFullPath(path, root)
        let relative = Path.GetRelativePath(root, absolute)
        if relative = ".." || relative.StartsWith(".." + string Path.DirectorySeparatorChar, StringComparison.Ordinal) then
            invalidArg "artifactPath" "must resolve beneath rootPath"
        relative.Replace(Path.DirectorySeparatorChar, '/')

    let private trustedValidator fileName expectedDigest =
        let path = Path.Combine(AppContext.BaseDirectory, "provider-validators", fileName)
        if not (File.Exists path) then
            invalidOp $"trusted provider validator is missing beside the engine: %s{fileName}"
        let digest =
            File.ReadAllBytes path
            |> Security.Cryptography.SHA256.HashData
            |> Convert.ToHexString
            |> _.ToLowerInvariant()
        if digest <> expectedDigest then
            invalidOp $"trusted provider validator identity is unsupported: %s{fileName} sha256:%s{digest}"
        path

    let private validateProviderArtifact expectedIdentity provider node =
        let root = providerRoot node
        let path = text "artifactPath" node
        let relative = relativeArtifact root path
        match provider with
        | "fsgg-sdd" ->
            let expected = $"readiness/%s{expectedIdentity}/verify.json"
            if relative <> expected then invalidArg "artifactPath" $"SDD verification artifact must be %s{expected}"
            let output = runValidator root "fsgg-sdd" [ "verify"; "--root"; root; "--work"; expectedIdentity; "--require-observed"; "--dry-run" ]
            use report = JsonDocument.Parse output
            let result = report.RootElement
            let command = property "command" result
            let context = property "context" result
            if text "toolVersion" result <> "1.0.0" || text "name" command <> "verify" || text "workId" context <> expectedIdentity then
                invalidArg "artifactPath" "fsgg-sdd validator identity, version, command, or work binding is unsupported"
            if not (bool "coherent" result) || text "outcome" result <> "noChange" then
                invalidArg "artifactPath" "fsgg-sdd verify did not confirm a coherent, byte-current provider view"
        | "critique" ->
            let script = trustedValidator "validate-critique-state.py" "90b8be5782e5d314c8c7f7ab8556b4859a714c9882ede2be81c75ce408dba9c9"
            runValidator root "python3" [ script; "--root"; root; "--cycle"; expectedIdentity; "--artifact"; relative ] |> ignore
        | "feedback" ->
            let script = trustedValidator "validate-feedback-state.py" "8a28ff24719a11204f168456974b4941026111d498039bf88a679cbca5f11a07"
            let audit = text "auditPath" node |> relativeArtifact root
            let phases = strings "phases" node
            if List.isEmpty phases || phases |> List.exists String.IsNullOrWhiteSpace then invalidArg "phases" "must contain the exercised feedback phases"
            runValidator root "python3" [ script; "--root"; root; "--cycle"; expectedIdentity; "--report"; relative; "--audit"; audit; "--phases"; String.concat "," phases ] |> ignore
        | _ -> invalidArg "provider" $"unsupported provider adapter: %s{provider}"

    let private receipt expectedIdentity target source head provider node =
        validateProviderArtifact expectedIdentity provider node
        let root = providerRoot node
        let path = text "artifactPath" node
        let absolute = Path.GetFullPath(path, root)
        if not (File.Exists absolute) then invalidArg "artifactPath" $"does not exist: %s{absolute}"
        match parseProviderReceipt expectedIdentity target source head provider (File.ReadAllBytes absolute) with
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
                   nonce = receipt.Nonce
                   evidenceDigest = receipt.EvidenceDigest |}
            match options.Render with
            | Json -> printfn "%s" (JsonSerializer.Serialize {| schema = "fsgg.coord.cycle-ledger/1"; verdict = "next"; action = "update"; cycleId = cycle.Id; unitId = cycle.UnitId; updateReceipt = updateReceipt |})
            | Text -> printfn "update %s %s" cycle.Id receipt.EvidenceDigest
        | _ ->
            let value = match action with | Resume cycle -> {| action = "resume"; cycleId = cycle.Id; unitId = cycle.UnitId |} | Register cycle -> {| action = "register"; cycleId = cycle.Id; unitId = cycle.UnitId |} | Advance cycle -> {| action = "advance"; cycleId = cycle.Id; unitId = cycle.UnitId |} | Escalate cycle -> {| action = "escalate"; cycleId = cycle.Id; unitId = cycle.UnitId |} | Complete -> {| action = "complete"; cycleId = ""; unitId = "" |} | Update _ -> failwith "unreachable"
            match options.Render with | Json -> printfn "%s" (JsonSerializer.Serialize {| schema = "fsgg.coord.cycle-ledger/1"; verdict = "next"; action = value.action; cycleId = value.cycleId; unitId = value.unitId |}) | Text -> printfn "%s %s" value.action value.cycleId
        ExitCode.toInt ExitCode.Green

    let private journalOptions = JsonSerializerOptions(PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase)

    let private journalPath () =
        match Environment.GetEnvironmentVariable "FSGG_CYCLE_JOURNAL" with
        | value when not (String.IsNullOrWhiteSpace value) -> value
        | _ ->
            let resolved = runValidator Environment.CurrentDirectory "git" [ "rev-parse"; "--git-path"; "fsgg-cycle-journal.json" ]
            resolved.Trim()

    let private readJournal () =
        let path = journalPath ()
        if File.Exists path then
            JsonSerializer.Deserialize<UpdateReceipt list>(File.ReadAllText path, journalOptions)
            |> Option.ofObj
            |> Option.defaultValue []
        else []

    let private appendJournal receipt =
        let path = journalPath ()
        let directory = Path.GetDirectoryName path
        if not (String.IsNullOrWhiteSpace directory) then Directory.CreateDirectory directory |> ignore
        let receipts = receipt :: (readJournal () |> List.filter (fun existing -> existing.CycleId <> receipt.CycleId))
        let temporary = path + ".tmp"
        File.WriteAllText(temporary, JsonSerializer.Serialize(receipts, journalOptions))
        File.Move(temporary, path, true)
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
                let journal = readJournal ()
                let unissued = guarded |> List.filter (fun receipt -> not (List.contains receipt journal))
                if not (List.isEmpty unissued) then fail "completion requires an exact update receipt issued by the durable update journal"
                else
                    match complete model accepted guarded (strings "rollupCycleIds" root) with
                    | Ok transition -> render options transition
                    | Error errors -> fail (String.concat "; " errors)
            | "advance" ->
                let target = cycle (property "cycle" root)
                let unit =
                    model.Units
                    |> List.tryFind (fun unit -> unit.Id = target.UnitId)
                    |> Option.defaultWith (fun () -> invalidArg "cycle.unitId" "does not identify a ledger unit")
                let proof = evidence (property "evidence" root)
                let expected = proof.ImplementationHead
                match advance model target (receipt target.UnitId target model.SourceRevision expected "fsgg-sdd" (property "implementation" root)) (receipt unit.ProviderCycleId target model.SourceRevision expected "critique" (property "review" root)) (receipt unit.ProviderCycleId target model.SourceRevision expected "feedback" (property "feedback" root)) proof with
                | Ok transition -> render options transition
                | Error errors -> fail (String.concat "; " errors)
            | "update" ->
                let target = cycle (property "cycle" root)
                let nonce = Guid.NewGuid().ToString("N")
                match update model target (evidence (property "evidence" root)) nonce with
                | Ok(Update(_, receipt) as transition) -> appendJournal receipt; render options transition
                | Ok transition -> render options transition
                | Error errors -> fail (String.concat "; " errors)
            | _ -> fail "unknown action; expected inspect, register, advance, update, or complete"
        with error -> fail error.Message
