namespace FS.GG.Coord.Cli

#nowarn "3391"

open System
open System.Diagnostics
open System.IO
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open FS.GG.Coord

module TelemetryRuntimeApplication =
    let private maxAssignmentBytes = 8L * 1024L

    let private option name args =
        args
        |> List.indexed
        |> List.tryPick (fun (index, value) ->
            if value = name || (name = "--model" && value = "-m") then
                List.tryItem (index + 1) args
            else
                None)

    let runObservedCodexExecWithPrelaunch executable assignment parentContext relation storeRoot lateAfterSeconds workspaceBinding codexArgs publish prelaunch =
        FS.GG.Telemetry.ObservedCodexProcess.runObservedCodexExecWithPrelaunch
            executable assignment parentContext relation storeRoot lateAfterSeconds workspaceBinding codexArgs publish prelaunch

    let runObservedCodexExecWith executable assignment parentContext relation storeRoot lateAfterSeconds workspaceBinding codexArgs publish =
        FS.GG.Telemetry.ObservedCodexProcess.runObservedCodexExecWith
            executable assignment parentContext relation storeRoot lateAfterSeconds workspaceBinding codexArgs publish

    let runCodexExecWith executable assignment codexArgs publish =
        FS.GG.Telemetry.ObservedCodexProcess.runCodexExecWith executable assignment codexArgs publish

    let private root args =
        option "--store-root" args
        |> Option.orElseWith (fun () ->
            Environment.GetEnvironmentVariable("FSGG_TELEMETRY_STORE")
            |> Option.ofObj
            |> Option.filter (String.IsNullOrWhiteSpace >> not))

    let private inheritableRoot (value: string option) =
        try
            value |> Option.filter Path.IsPathFullyQualified |> Option.map Path.GetFullPath
        with _ ->
            None

    let private parseLateAfter args fallback =
        match option "--late-after-seconds" args with
        | None -> Ok fallback
        | Some value ->
            match Int64.TryParse value with
            | true, parsed when parsed >= 0L -> Ok parsed
            | _ -> Error [ "--late-after-seconds must be a non-negative integer" ]

    let private readPrivateAssignment assignmentPath =
        try
            let fullPath = Path.GetFullPath assignmentPath
            let info = FileInfo fullPath

            if not (Path.IsPathFullyQualified assignmentPath) then
                Error [ "assignment path must be absolute" ]
            elif not info.Exists || not (isNull info.LinkTarget) then
                Error [ "assignment must be a regular non-symlink file" ]
            elif info.Length > maxAssignmentBytes then
                Error [ "assignment exceeds 8 KiB" ]
            elif
                not (OperatingSystem.IsWindows())
                && File.GetUnixFileMode(fullPath)
                   <> (UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
            then
                Error [ "assignment permissions must be 0600" ]
            else
                File.ReadAllBytes fullPath |> TelemetryRuntime.parseAssignment
        with error ->
            Error [ "assignment is unavailable: " + error.Message ]

    let private inheritedContext () =
        match
            Environment.GetEnvironmentVariable TelemetryRuntime.InvocationContextEnvironment
            |> Option.ofObj
        with
        | None ->
            Error
                [
                    "--assignment is required for a root launch; descendants require inherited private invocation context"
                ]
        | Some value when Encoding.UTF8.GetByteCount value > int maxAssignmentBytes ->
            Error [ "inherited invocation context exceeds 8 KiB" ]
        | Some value -> Encoding.UTF8.GetBytes value |> TelemetryRuntime.parseInvocationContext

    let runCodexExec args =
        match List.tryFindIndex ((=) "--") args with
        | Some delimiter ->
            let wrapperArgs = args[.. delimiter - 1]
            let codexArgs = args[delimiter + 1 ..]

            let launch =
                match option "--assignment" wrapperArgs with
                | Some assignmentPath ->
                    readPrivateAssignment assignmentPath
                    |> Result.bind (fun assignment ->
                        let relation = option "--relation" wrapperArgs |> Option.defaultValue "root"

                        if relation <> "root" then
                            Error [ "an explicit assignment starts a root invocation; --relation must be root" ]
                        else
                            parseLateAfter wrapperArgs 60L
                            |> Result.map (fun late ->
                                let storeRoot = root wrapperArgs |> inheritableRoot
                                assignment, None, TelemetryRuntime.Root, storeRoot, late))
                | None ->
                    inheritedContext ()
                    |> Result.bind (fun context ->
                        let relationText = option "--relation" wrapperArgs |> Option.defaultValue "child"

                        match TelemetryRuntime.parseRelation relationText with
                        | None
                        | Some TelemetryRuntime.Root ->
                            Error [ "an inherited invocation must use --relation child or follow-up" ]
                        | Some relation ->
                            let explicitRoot = option "--store-root" wrapperArgs
                            let resolvedExplicit = inheritableRoot explicitRoot

                            if explicitRoot.IsSome && resolvedExplicit.IsNone then
                                Error [ "--store-root must be an absolute valid path" ]
                            elif resolvedExplicit.IsSome && resolvedExplicit <> context.StoreRoot then
                                Error [ "--store-root cannot replace the inherited private store root" ]
                            else
                                parseLateAfter wrapperArgs context.LateAfterSeconds
                                |> Result.bind (fun late ->
                                    if late <> context.LateAfterSeconds then
                                        Error [ "--late-after-seconds cannot replace the inherited activation delay" ]
                                    else
                                        Ok(context.Assignment, Some context, relation, context.StoreRoot, late)))

            match launch with
            | Error errors ->
                errors
                |> List.iter (fun error ->
                    Console.Error.WriteLine("fsgg-coord-engine: telemetry runtime assignment: " + error))

                2
            | Ok(assignment, parent, relation, storeRoot, lateAfter) ->
                let assessment =
                    storeRoot |> Option.map TelemetryStoreApplication.assessProductionRoot

                let explicitConfig = option "--config" wrapperArgs
                let explicitRepository = option "--repository" wrapperArgs

                let inheritedConfig =
                    Environment.GetEnvironmentVariable("FSGG_TELEMETRY_CONFIG") |> Option.ofObj

                let inheritedRepository =
                    Environment.GetEnvironmentVariable("FSGG_TELEMETRY_REPOSITORY") |> Option.ofObj

                let inheritedDigest =
                    Environment.GetEnvironmentVariable("FSGG_TELEMETRY_BINDING_DIGEST")
                    |> Option.ofObj

                let workspaceConfig = explicitConfig |> Option.orElse inheritedConfig
                let workspaceRepository = explicitRepository |> Option.orElse inheritedRepository

                let conflicting =
                    parent.IsSome
                    && ((explicitConfig.IsSome
                         && inheritedConfig.IsSome
                         && explicitConfig <> inheritedConfig)
                        || (explicitRepository.IsSome
                            && inheritedRepository.IsSome
                            && explicitRepository <> inheritedRepository))

                let resolvedBinding =
                    WorkspaceTelemetryApplication.tryBinding workspaceConfig workspaceRepository

                let expectedProducer =
                    WorkspaceTelemetryApplication.selectedProducer workspaceConfig workspaceRepository

                let configuredPath = WorkspaceTelemetryApplication.configuredPath workspaceConfig
                let useWorkspace = workspaceConfig.IsSome || File.Exists configuredPath

                let frozenDigest =
                    inheritedDigest
                    |> Option.orElseWith (fun () -> resolvedBinding |> Option.map (fun (_, _, digest) -> digest))
                    |> Option.orElseWith (fun () -> if useWorkspace then Some "unresolved" else None)

                let publish bytes =
                    if useWorkspace then
                        WorkspaceTelemetryApplication.tryPublishBound
                            workspaceConfig
                            workspaceRepository
                            expectedProducer
                            frozenDigest
                            bytes
                    else
                        match storeRoot, assessment with
                        | Some path, Some approved -> TelemetryStoreApplication.publish path approved bytes
                        | _ -> Error [ "store root is unconfigured" ]

                let binding =
                    resolvedBinding
                    |> Option.orElseWith (fun () ->
                        if useWorkspace then
                            workspaceRepository
                            |> Option.orElseWith (fun () ->
                                Environment.GetEnvironmentVariable("GITHUB_REPOSITORY") |> Option.ofObj)
                            |> Option.bind (fun repository ->
                                frozenDigest |> Option.map (fun digest -> configuredPath, repository, digest))
                        else
                            None)

                let analysisPrelaunch =
                    option "--efficiency-claim-template" wrapperArgs
                    |> Option.map (fun templatePath ->
                        fun (context: TelemetryRuntime.InvocationContext) ->
                            match option "--model" codexArgs with
                            | None -> Error [ "analysis-prelaunch-explicit-model-required" ]
                            | Some selectedModel ->
                                match
                                    resolvedBinding,
                                    frozenDigest,
                                    storeRoot,
                                    WorkspaceTelemetryApplication.resolveEfficiencyProducer workspaceConfig workspaceRepository
                                with
                                | Some(_, _, bindingDigest), Some expectedDigest, Some selectedRoot, Ok(path, principal, producerDigest)
                                    when bindingDigest = producerDigest && expectedDigest = producerDigest &&
                                         Path.GetFullPath selectedRoot = Path.GetFullPath path ->
                                    WorkspaceTelemetryApplication.readEfficiencyClaimTemplate templatePath
                                    |> Result.bind (fun template ->
                                        use document = JsonDocument.Parse(ReadOnlyMemory<byte>(template))
                                        let root = document.RootElement
                                        if root.GetProperty("modelAlias").GetString() <> selectedModel ||
                                           root.GetProperty("invocationRef").ValueKind <> JsonValueKind.Null then
                                            Error [ "analysis-prelaunch-model-or-invocation-conflict" ]
                                        else
                                            WorkspaceTelemetryApplication.tryDrainExpected workspaceConfig workspaceRepository frozenDigest
                                            |> Result.bind (fun _ ->
                                                TelemetryStoreApplication.efficiencyAnalysisClaimProspective
                                                    path
                                                    (TelemetryStoreApplication.assessProductionRoot path)
                                                    principal
                                                    ($"expected-dispatch-%s{context.DispatchId}")
                                                    context.Assignment.ItemId
                                                    template)
                                            |> Result.map ignore)
                                | _ -> Error [ "analysis-prelaunch-binding-unavailable" ])

                let exitCode =
                    if conflicting then
                        Console.Error.WriteLine("fsgg-coord-engine: inherited workspace association cannot be replaced")
                        2
                    else
                        runObservedCodexExecWithPrelaunch
                            "codex"
                            assignment
                            parent
                            relation
                            storeRoot
                            lateAfter
                            binding
                            codexArgs
                            publish
                            analysisPrelaunch

                if useWorkspace then
                    match
                        WorkspaceTelemetryApplication.tryDrainExpected workspaceConfig workspaceRepository frozenDigest
                    with
                    | Ok _ -> ()
                    | Error errors ->
                        errors
                        |> List.iter (fun error ->
                            Console.Error.WriteLine(
                                "fsgg-coord-engine: telemetry runtime reconciliation pending: " + error
                            ))
                else
                    match storeRoot, assessment with
                    | Some path, Some approved ->
                        match TelemetryStoreApplication.drain path approved with
                        | Ok _ -> ()
                        | Error errors ->
                            errors
                            |> List.iter (fun error ->
                                Console.Error.WriteLine(
                                    "fsgg-coord-engine: telemetry runtime reconciliation pending: " + error
                                ))
                    | _ -> ()

                exitCode
        | _ ->
            Console.Error.WriteLine(
                "fsgg-coord-engine: telemetry runtime codex-exec requires -- before Codex arguments"
            )

            2

    let capabilityStatus args =
        let version =
            try
                let info = ProcessStartInfo("codex")
                info.UseShellExecute <- false
                info.RedirectStandardOutput <- true
                info.RedirectStandardError <- true
                info.ArgumentList.Add "--version"
                use child = Process.Start info
                let value = child.StandardOutput.ReadToEnd().Trim()
                child.WaitForExit(2000) |> ignore
                if child.ExitCode = 0 then Some value else None
            with _ ->
                None

        let configured = root args |> Option.isSome

        let workspaceAssociation =
            let config = option "--config" args
            let repository = option "--repository" args

            match WorkspaceTelemetryApplication.resolveBinding config repository with
            | Ok binding ->
                match WorkspaceTelemetryApplication.tryLocalStoreRootBound binding with
                | Ok(Some _) -> "configured-local"
                | Ok None -> "configured-remote"
                | Error _ -> "unavailable"
            | Error [ "unconfigured" ] -> "unconfigured"
            | Error [ "workspace-unassociated" ] -> "unassociated"
            | Error _ -> "unavailable"

        Console.Out.WriteLine(
            JsonSerializer.Serialize
                {|
                    schema = "fsgg.telemetry.runtime-capabilities/1"
                    codexVersion = version
                    codexExecAdapter = true
                    packagedWorkerLauncher = true
                    hostActivation = "not-assessed"
                    collaborationSpawnAgent = "unsupported"
                    store = if configured then "configured" else "unconfigured"
                    storeMeaning = "explicit-local-store-selection"
                    workspaceAssociation = workspaceAssociation
                    receiverReachability = "not-checked"
                |}
        )

        0
