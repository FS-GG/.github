namespace FS.GG.Coord.Cli

open System
open System.Threading
open System.Threading.Tasks

type DashboardAsset =
    { ContentType: string
      Content: byte array }

type TelemetryDashboardServerOptions =
    { WorkspaceId: string
      AssetProvider: string -> DashboardAsset option
      SnapshotProvider: string -> CancellationToken -> Task<Result<byte array, string list>>
      BootstrapLifetime: TimeSpan
      SessionIdleTimeout: TimeSpan
      SessionAbsoluteTimeout: TimeSpan
      RequestTimeout: TimeSpan
      SnapshotTimeout: TimeSpan
      ShutdownTimeout: TimeSpan
      MaxSessions: int
      MaxConcurrentRequests: int
      MaxConcurrentQueries: int
      MaxRequestBodyBytes: int
      MaxResponseBodyBytes: int
      MaxHeaderBytes: int
      BindAttempts: int }

type RunningTelemetryDashboardServer =
    inherit IDisposable

    abstract BootstrapUrl: Uri
    abstract Origin: Uri
    abstract Completion: Task
    abstract StopAsync: unit -> Task

[<RequireQualifiedAccess>]
module TelemetryDashboardServer =
    val defaultOptions:
        workspaceId: string ->
        assetProvider: (string -> DashboardAsset option) ->
        snapshotProvider: (string -> CancellationToken -> Task<Result<byte array, string list>>) ->
            TelemetryDashboardServerOptions

    val start:
        options: TelemetryDashboardServerOptions ->
        cancellationToken: CancellationToken ->
            Task<Result<RunningTelemetryDashboardServer, string list>>
