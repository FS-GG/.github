namespace FS.GG.Coord.Cli

#nowarn "3391"

open System
open System.Diagnostics
open System.IO
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open System.Threading.Channels
open FS.GG.Coord

module TelemetryRuntimeApplication =
    let private maxAssignmentBytes = 8L * 1024L

    let private option name args =
        args |> List.indexed |> List.tryPick (fun (index, value) -> if value = name || (name = "--model" && value = "-m") then List.tryItem (index + 1) args else None)

    let private requestedEffort args =
        args
        |> List.windowed 2
        |> List.tryPick (function
            | [ "-c"; value ] | [ "--config"; value ] when value.StartsWith("model_reasoning_effort=", StringComparison.Ordinal) -> Some(value.Substring(value.IndexOf('=') + 1).Trim('"', '\''))
            | _ -> None)

    let private backend args =
        match option "--local-provider" args with
        | Some value -> Some value
        | None when List.contains "--oss" args -> Some "oss"
        | None -> None

    let private addOptional (node: JsonObject) (name: string) (value: string option) =
        node[name] <- match value with Some text -> JsonValue.Create text | None -> null

    let private eventBatch (assignment: TelemetryRuntime.Assignment) invocation sequence (event: JsonObject) =
        let root = JsonObject()
        root["schema"] <- TelemetryStore.BatchSchema
        root["ingestId"] <- $"%s{invocation}-%06d{sequence}"
        root["sourceIdentity"] <- assignment.ProducerStream
        root["generation"] <- invocation
        root["cursor"] <- string sequence
        root["eventCount"] <- 1
        let events = JsonArray()
        events.Add event
        root["events"] <- events
        Encoding.UTF8.GetBytes(root.ToJsonString(JsonSerializerOptions(WriteIndented = false)))

    let private commonEvent kind identity (assignment: TelemetryRuntime.Assignment) =
        let event = JsonObject()
        event["kind"] <- kind
        event["identity"] <- identity
        event["itemId"] <- assignment.ItemId
        event["revision"] <- 0
        event

    let private digestIdentity prefix (value: string) =
        prefix + CanonicalJson.sha256(Encoding.UTF8.GetBytes value).Substring(0, 32)

    let runCodexExecWith executable (assignment: TelemetryRuntime.Assignment) codexArgs (publish: byte array -> Result<string, string list>) =
        if not (List.contains "--json" codexArgs && List.contains "--ephemeral" codexArgs) then
            Console.Error.WriteLine("fsgg-coord-engine: telemetry runtime codex-exec requires explicit --json and --ephemeral")
            2
        else
            let invocation = Guid.NewGuid().ToString("N")
            let requestedModel = option "--model" codexArgs
            let requestedEffort = requestedEffort codexArgs
            let requestedBackend = backend codexArgs
            let mutable sequence = 0L
            let mutable publicationLost = false
            let emit event =
                sequence <- sequence + 1L
                try
                    match publish (eventBatch assignment invocation sequence event) with
                    | Ok _ -> ()
                    | Error _ -> publicationLost <- true
                with _ -> publicationLost <- true
            let admission = commonEvent "runtime-admission" ($"runtime-admission-%s{invocation}") assignment
            admission["invocationId"] <- invocation
            admission["featureId"] <- assignment.FeatureId
            admission["attemptId"] <- assignment.AttemptId
            addOptional admission "parentAttemptId" assignment.ParentAttemptId
            admission["producerStream"] <- assignment.ProducerStream
            addOptional admission "requestedModel" requestedModel
            addOptional admission "requestedEffort" requestedEffort
            addOptional admission "backend" requestedBackend
            emit admission

            let info = ProcessStartInfo(executable)
            info.UseShellExecute <- false
            info.RedirectStandardOutput <- true
            info.WorkingDirectory <- Directory.GetCurrentDirectory()
            info.ArgumentList.Add "exec"
            codexArgs |> List.iter info.ArgumentList.Add
            try
                use child = Process.Start info
                let processStart = commonEvent "runtime-start" ($"runtime-process-%s{invocation}") assignment
                processStart["invocationId"] <- invocation
                processStart["threadId"] <- null
                processStart["turnId"] <- null
                processStart["turnSequence"] <- null
                processStart["processId"] <- child.Id
                processStart["phase"] <- "process"
                emit processStart

                let channel = Channel.CreateBounded<byte array>(BoundedChannelOptions(64, SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait))
                let mutable queueGap = false
                let mutable framingGap = false
                let mutable threadId: string option = None
                let mutable turnSequence = 0L
                let mutable usageCount = 0L
                let consumer =
                    task {
                        let reader = channel.Reader
                        while not reader.Completion.IsCompleted do
                            let! available = reader.WaitToReadAsync().AsTask()
                            if available then
                                let mutable frame = Unchecked.defaultof<byte array>
                                while reader.TryRead(&frame) do
                                    let raw = Encoding.UTF8.GetString frame
                                    match TelemetryRuntime.projectLine threadId (turnSequence + 1L) raw with
                                    | Some(TelemetryRuntime.ThreadStarted nativeThread) ->
                                        threadId <- Some nativeThread
                                        let started = commonEvent "runtime-start" ($"runtime-thread-%s{invocation}") assignment
                                        started["invocationId"] <- invocation
                                        started["threadId"] <- nativeThread
                                        started["turnId"] <- null
                                        started["turnSequence"] <- null
                                        started["processId"] <- child.Id
                                        started["phase"] <- "thread"
                                        emit started
                                    | Some(TelemetryRuntime.TurnStarted(nativeThread,nativeTurn,startedSequence)) ->
                                        let started = commonEvent "runtime-start" (digestIdentity "runtime-turn-start-" ($"%s{invocation}\u001f%s{nativeThread}\u001f%s{nativeTurn |> Option.defaultValue (string startedSequence)}")) assignment
                                        started["invocationId"] <- invocation
                                        started["threadId"] <- nativeThread
                                        addOptional started "turnId" nativeTurn
                                        started["turnSequence"] <- startedSequence
                                        started["processId"] <- child.Id
                                        started["phase"] <- "turn"
                                        emit started
                                    | Some(TelemetryRuntime.TurnUsageCompleted usage) ->
                                        turnSequence <- turnSequence + 1L
                                        usageCount <- usageCount + 1L
                                        let nativeKey = usage.TurnId |> Option.defaultValue (string usage.TurnSequence)
                                        let identity = digestIdentity "runtime-usage-" ($"%s{invocation}\u001f%s{usage.ThreadId}\u001f%s{nativeKey}")
                                        let observed = commonEvent "runtime-turn-usage" identity assignment
                                        observed["invocationId"] <- invocation
                                        observed["threadId"] <- usage.ThreadId
                                        addOptional observed "turnId" usage.TurnId
                                        observed["turnSequence"] <- usage.TurnSequence
                                        addOptional observed "provider" usage.Provider
                                        addOptional observed "requestedModel" requestedModel
                                        addOptional observed "observedModel" usage.ObservedModel
                                        addOptional observed "requestedEffort" requestedEffort
                                        addOptional observed "observedEffort" usage.ObservedEffort
                                        addOptional observed "backend" (usage.Backend |> Option.orElse requestedBackend)
                                        observed["scope"] <- "completed-turn"
                                        observed["provenance"] <- "codex-exec-jsonl"
                                        observed["input"] <- usage.Input
                                        observed["cachedInput"] <- usage.CachedInput
                                        observed["output"] <- usage.Output
                                        observed["reasoning"] <- match usage.Reasoning with Some value -> JsonValue.Create value | None -> null
                                        observed["total"] <- usage.Total
                                        emit observed
                                    | Some(TelemetryRuntime.Gap code) ->
                                        let gap = commonEvent "runtime-gap" (digestIdentity "runtime-gap-" ($"%s{invocation}\u001f%d{sequence}\u001f%s{code}")) assignment
                                        gap["invocationId"] <- invocation; gap["code"] <- code; emit gap
                                    | None -> ()
                    }
                use frame = new MemoryStream()
                let input = child.StandardOutput.BaseStream
                let output = Console.OpenStandardOutput()
                let buffer = Array.zeroCreate<byte> 4096
                let mutable reading = true
                let mutable oversized = false
                while reading do
                    let count = input.Read(buffer, 0, buffer.Length)
                    if count = 0 then reading <- false else
                    output.Write(buffer, 0, count); output.Flush()
                    for index in 0 .. count - 1 do
                        let value = buffer[index]
                        if value = byte '\n' then
                            if oversized then framingGap <- true
                            else
                                let bytes = frame.ToArray()
                                let trimmed = if bytes.Length > 0 && bytes[bytes.Length - 1] = byte '\r' then bytes[..bytes.Length - 2] else bytes
                                if not (channel.Writer.TryWrite trimmed) then queueGap <- true
                            frame.SetLength 0L; oversized <- false
                        elif not oversized then
                            if frame.Length < int64 TelemetryStore.MaxEventBytes then frame.WriteByte value else oversized <- true
                if frame.Length > 0L || oversized then
                    if oversized then framingGap <- true elif not (channel.Writer.TryWrite(frame.ToArray())) then queueGap <- true
                channel.Writer.Complete()
                consumer.GetAwaiter().GetResult()
                child.WaitForExit()
                let gap code =
                    let event = commonEvent "runtime-gap" (digestIdentity "runtime-gap-" ($"%s{invocation}\u001fterminal\u001f%s{code}")) assignment
                    event["invocationId"] <- invocation; event["code"] <- code; emit event
                if framingGap then gap "oversized-frame"
                if queueGap then gap "publisher-queue-exhausted"
                if threadId.IsNone then gap "missing-thread-start"
                if child.ExitCode = 0 && usageCount = 0L then gap "exit-zero-without-usage"
                if publicationLost then gap "publication-failure"
                let terminal = commonEvent "runtime-terminal" ($"runtime-terminal-%s{invocation}") assignment
                terminal["invocationId"] <- invocation
                addOptional terminal "threadId" threadId
                terminal["outcome"] <- if child.ExitCode = 0 then "completed" else "failed"
                terminal["exitCode"] <- child.ExitCode
                emit terminal
                child.ExitCode
            with error ->
                Console.Error.WriteLine("fsgg-coord-engine: telemetry runtime codex-exec: " + error.Message)
                let terminal = commonEvent "runtime-terminal" ($"runtime-terminal-%s{invocation}") assignment
                terminal["invocationId"] <- invocation; terminal["threadId"] <- null; terminal["outcome"] <- "launch-failed"; terminal["exitCode"] <- 127
                emit terminal
                127

    let private root args =
        option "--store-root" args
        |> Option.orElseWith (fun () -> Environment.GetEnvironmentVariable("FSGG_TELEMETRY_STORE") |> Option.ofObj |> Option.filter (String.IsNullOrWhiteSpace >> not))

    let runCodexExec args =
        match List.tryFindIndex ((=) "--") args, option "--assignment" args with
        | Some delimiter, Some assignmentPath ->
            let wrapperArgs = args[..delimiter - 1]
            let codexArgs = args[delimiter + 1..]
            let assignmentBytes =
                try
                    let fullPath = Path.GetFullPath assignmentPath
                    let info = FileInfo fullPath
                    if not (Path.IsPathFullyQualified assignmentPath) then Error [ "assignment path must be absolute" ]
                    elif not info.Exists || not (isNull info.LinkTarget) then Error [ "assignment must be a regular non-symlink file" ]
                    elif info.Length > maxAssignmentBytes then Error [ "assignment exceeds 8 KiB" ]
                    elif not (OperatingSystem.IsWindows()) && File.GetUnixFileMode(fullPath) <> (UnixFileMode.UserRead ||| UnixFileMode.UserWrite) then Error [ "assignment permissions must be 0600" ]
                    else Ok(File.ReadAllBytes fullPath)
                with error -> Error [ "assignment is unavailable: " + error.Message ]
            match assignmentBytes |> Result.bind TelemetryRuntime.parseAssignment with
            | Error errors -> errors |> List.iter (fun error -> Console.Error.WriteLine("fsgg-coord-engine: telemetry runtime assignment: " + error)); 2
            | Ok assignment ->
                let publish bytes =
                    match root wrapperArgs with
                    | None -> Error [ "store root is unconfigured" ]
                    | Some path -> TelemetryStoreApplication.publish path (TelemetryStoreApplication.assessProductionRoot path) bytes
                runCodexExecWith "codex" assignment codexArgs publish
        | _ -> Console.Error.WriteLine("fsgg-coord-engine: telemetry runtime codex-exec requires --assignment FILE -- CODEX_ARGS"); 2

    let capabilityStatus args =
        let version =
            try
                let info = ProcessStartInfo("codex")
                info.UseShellExecute <- false; info.RedirectStandardOutput <- true; info.RedirectStandardError <- true
                info.ArgumentList.Add "--version"
                use child = Process.Start info
                let value = child.StandardOutput.ReadToEnd().Trim()
                child.WaitForExit(2000) |> ignore
                if child.ExitCode = 0 then Some value else None
            with _ -> None
        let configured = root args |> Option.isSome
        Console.Out.WriteLine(JsonSerializer.Serialize {| schema = "fsgg.telemetry.runtime-capabilities/1"; codexVersion = version; codexExecAdapter = true; collaborationSpawnAgent = "unsupported"; store = if configured then "configured" else "unconfigured" |})
        0
