namespace FS.GG.Telemetry.Dashboard

type ProjectionError =
    | EnvelopeTooLarge
    | InvalidEnvelope
    | UnsupportedSchema
    | InvalidRevision
    | SnapshotTooLarge
    | InvalidSnapshot
    | IncompleteSnapshot
    | IncompatibleStore
    | UnauthorizedWorkspace

module DashboardProjection =
    [<Literal>]
    val Schema: string = "fsgg.telemetry.private-dashboard/1"
    val project: authorizedWorkspace:string -> canonicalSnapshotEnvelopeBytes:byte array -> Result<byte array,ProjectionError>

type Asset = { ContentType:string; Bytes:byte array }

module DashboardAssets =
    val tryGet: route:string -> Asset option
    val tryGetLocal: route:string -> Asset option
