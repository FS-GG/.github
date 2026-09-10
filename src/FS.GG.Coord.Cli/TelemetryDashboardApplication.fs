namespace FS.GG.Coord.Cli

open System
open System.Diagnostics
open System.IO
open System.Runtime.InteropServices
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open FS.GG.Coord
open FS.GG.Telemetry.Dashboard

[<RequireQualifiedAccess>]
module TelemetryDashboardApplication =
    let private green = ExitCode.toInt ExitCode.Green
    let private red = ExitCode.toInt ExitCode.Error

    let private option name args =
        args
        |> List.indexed
        |> List.rev
        |> List.tryPick (fun (index, value) -> if value = name then List.tryItem (index + 1) args else None)

    let private emitStatus status workspace repository reasons =
        let bytes =
            JsonSerializer.SerializeToUtf8Bytes(
                {| schema = "fsgg.telemetry.local-dashboard-status/1"
                   status = status
                   workspaceId = workspace
                   repository = repository
                   reasons = reasons |}
            )

        Console.Out.WriteLine(Encoding.UTF8.GetString bytes)

    let private projectSnapshot (binding: WorkspaceTelemetryApplication.LocalDashboardBinding) =
        let assessment = TelemetryStoreApplication.assessProductionRoot binding.StoreRoot

        TelemetryStoreApplication.scopedDashboardSnapshot
            binding.StoreRoot
            assessment
            binding.WorkspaceId
            None
        |> Result.bind (fun snapshot ->
            DashboardProjection.project binding.WorkspaceId (Encoding.UTF8.GetBytes snapshot)
            |> Result.mapError (fun error -> [ $"projection-{error}" ]))

    let private resolve args =
        WorkspaceTelemetryApplication.resolveLocalDashboard (option "--config" args) (option "--repository" args)

    let private status args =
        let config = option "--config" args
        let path = WorkspaceTelemetryApplication.configuredPath config

        if not (File.Exists path) then
            emitStatus "unconfigured" null null [||]
            green
        else
            match resolve args with
            | Error errors ->
                emitStatus "unavailable" null null (List.toArray errors)
                red
            | Ok binding ->
                match projectSnapshot binding with
                | Ok _ ->
                    emitStatus "ready" binding.WorkspaceId binding.Repository [||]
                    green
                | Error errors ->
                    emitStatus "unavailable" binding.WorkspaceId binding.Repository (List.toArray errors)
                    red

    let private openBrowser (url: Uri) =
        try
            let info = ProcessStartInfo()
            info.FileName <- url.AbsoluteUri
            info.UseShellExecute <- true
            Process.Start(info) |> ignore
            true
        with _ -> false

    let private serve args =
        match resolve args with
        | Error errors ->
            errors |> List.iter (fun reason -> Console.Error.WriteLine($"fsgg-coord-engine: telemetry dashboard: {reason}"))
            red
        | Ok initial ->
            let snapshotProvider (_:string) (cancellationToken:CancellationToken) =
                Task.Run(
                    (fun () ->
                        if cancellationToken.IsCancellationRequested then
                            Error [ "snapshot-cancelled" ]
                        else
                            match resolve args with
                            | Ok current when current = initial -> projectSnapshot current
                            | Ok _ -> Error [ "association-stale" ]
                            | Error errors -> Error errors),
                    cancellationToken
                )

            let assetProvider route =
                DashboardAssets.tryGetLocal route
                |> Option.map (fun asset ->
                    { ContentType = asset.ContentType
                      Content = asset.Bytes })

            match projectSnapshot initial with
            | Error errors ->
                errors |> List.iter (fun reason -> Console.Error.WriteLine($"fsgg-coord-engine: telemetry dashboard: {reason}"))
                red
            | Ok _ ->
                use shutdown = new CancellationTokenSource()
                let cancelHandler =
                    ConsoleCancelEventHandler(fun _ event ->
                        event.Cancel <- true
                        shutdown.Cancel())
                Console.CancelKeyPress.AddHandler cancelHandler
                let terminate =
                    if OperatingSystem.IsWindows() then None
                    else
                        Some(PosixSignalRegistration.Create(PosixSignal.SIGTERM, fun context ->
                            context.Cancel <- true
                            shutdown.Cancel()))

                try
                    let options = TelemetryDashboardServer.defaultOptions initial.WorkspaceId assetProvider snapshotProvider

                    match TelemetryDashboardServer.start options shutdown.Token |> fun task -> task.GetAwaiter().GetResult() with
                    | Error errors ->
                        errors |> List.iter (fun reason -> Console.Error.WriteLine($"fsgg-coord-engine: telemetry dashboard: {reason}"))
                        red
                    | Ok server ->
                        use server = server
                        Console.Out.WriteLine(server.BootstrapUrl.AbsoluteUri)
                        Console.Out.Flush()

                        if not (List.contains "--no-open" args) && not (openBrowser server.BootstrapUrl) then
                            Console.Error.WriteLine("fsgg-coord-engine: telemetry dashboard: browser-open-failed; use the URL printed to stdout")

                        try
                            server.Completion.GetAwaiter().GetResult()
                            green
                        with :? OperationCanceledException -> green
                finally
                    terminate |> Option.iter _.Dispose()
                    Console.CancelKeyPress.RemoveHandler cancelHandler

    let run action args =
        match action with
        | "status" -> status args
        | "serve" -> serve args
        | _ ->
            Console.Error.WriteLine("fsgg-coord-engine: telemetry dashboard: unsupported action")
            red
