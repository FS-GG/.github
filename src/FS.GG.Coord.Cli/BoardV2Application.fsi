namespace FS.GG.Coord.Cli

/// Fixed restricted Observation refresh; input files carry identity and historical evidence only.
module BoardV2Application =
    open FS.GG.Coord.GitHub

    /// Decode a closed binding wire shape and validate its immutable organization/target contract.
    val parseBinding: json: string -> Result<V2Projection.Binding, string>
    /// Emit an explicit report wire shape, including gaps and independent acceptance facts.
    val encodeReport: report: V2ProjectionSource.BatchReport -> string
    /// Encode structurally read-only inspection, human planning values and Unknown integrator facts.
    val encodeInspection: report: V2ProjectionSource.InspectionReport -> string
    /// Decode historical verification evidence. It cannot authorize a current observation or write.
    val decodePreviousReport: json: string -> Result<V2ProjectionSource.BatchReport, string>
    /// Fixed refresh or read-only inspect file command against an injected transport, for offline fixtures.
    val runWithTransport: transport: Transport.IGitHubTransport -> arguments: string list -> int
    /// Recognize board-v2 after validation; refresh composes the qualified writer, inspect only canonical reads.
    val tryRun: arguments: string list -> int option
