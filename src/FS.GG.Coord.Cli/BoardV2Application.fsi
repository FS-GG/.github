namespace FS.GG.Coord.Cli

/// Fixed restricted Observation refresh; input files carry identity and historical evidence only.
module BoardV2Application =
    open FS.GG.Coord.GitHub

    /// Decode a closed binding wire shape and validate its immutable organization/target contract.
    val parseBinding: json: string -> Result<V2Projection.Binding, string>
    /// Emit an explicit report wire shape, including gaps and independent acceptance facts.
    val encodeReport: report: V2ProjectionSource.BatchReport -> string
    /// Decode historical verification evidence. It cannot authorize a current observation or write.
    val decodePreviousReport: json: string -> Result<V2ProjectionSource.BatchReport, string>
    /// Fixed file command against an injected metered transport, for offline refusal fixtures.
    val runWithTransport: transport: Transport.IGitHubTransport -> arguments: string list -> int
    /// Recognize board-v2 and construct the fixed root-local V2 Observation-only composition after validation.
    val tryRun: arguments: string list -> int option
