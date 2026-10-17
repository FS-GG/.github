namespace FS.GG.Coord.GitHub

/// Dedicated bounded one-shot migration edge. Observation and V1 authorizers are unchanged.
module V2MigrationTransport =
    open System
    open Errors

    type IConnection =
        inherit IDisposable
        abstract Reads: Transport.IGitHubTransport
        /// Reconstructs exactly one allowlisted typed effect. No automatic retry or redirect.
        abstract Dispatch: prepared: V2Migration.Prepared * change: V2Migration.Change * planningWindowUntil: DateTimeOffset -> IoResult<Transport.Response>

    /// Migration-only clear-success envelope: present errors must be an empty array; complete data
    /// and duplicate-free JSON objects are required. This does not change generic/product GraphQl.
    val validateClearEnvelope: body: string -> IoResult<unit>

    /// Inert/provider composition. Both read and effect paths share 500 requests/270 seconds.
    /// The native effect callback is called at most once per explicit Dispatch.
    val compose:
        reads: Transport.IGitHubTransport ->
        nativeOnce: (Transport.Request -> IoResult<Transport.Response>) -> IConnection

    /// Inert HTTP-handler seam exercising the same bounded stream and fixed-origin production sender.
    /// A supplied handler is trusted test/provider infrastructure, never CLI input or operation approval.
    val createWithHandler: token: string -> handler: System.Net.Http.HttpMessageHandler -> IoResult<IConnection>

    /// Fixed api.github.com HTTP origin, no redirects, bounded 256 KiB request and 4 MiB response
    /// acquisition, one shared invocation deadline. After a possible send any failure is uncertain.
    val createLive: token: string -> IoResult<IConnection>
