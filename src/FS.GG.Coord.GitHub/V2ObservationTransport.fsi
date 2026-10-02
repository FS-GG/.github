namespace FS.GG.Coord.GitHub

/// Stateless root-local authority for the reviewed Project 3 Observation-only exact three-issue pilot
/// or its exact four-issue successor. The selected binding, not their union, controls fresh dispatch.
/// The token has genuine Projects capability; this application's exact guard enforces its narrower scope.
module V2ObservationTransport =
    open Errors
    open Transport
    open V2Projection
    /// Transient dispatch evidence; never replay authority. Response bytes are hashed, tokens omitted.
    type DispatchReceipt =
        { HttpStatus: int option; Headers: Map<string,string>; ResponseSha256: string option; Error: string option }
    type IDispatchEvidence =
        abstract DispatchReceipts: DispatchReceipt list
    val validateScope: binding: Binding -> IoResult<unit>
    val authorize: binding: Binding -> intent: MutationIntent -> IoResult<Transport.Request>
    /// Rebuild the fixed request, freshly bind immutable item/content, dispatch once. No durable replay.
    val compose: binding: Binding -> reads: IGitHubTransport -> nativeOnce: (Transport.Request -> IoResult<Response>) -> IoResult<IGitHubTransport>
    /// Dedicated guarded live composition; never calls the legacy fence's raw provider port.
    val createLive: binding: Binding -> token: string -> IoResult<IGitHubTransport * System.IDisposable>
