namespace FS.GG.Coord.Cli

/// Fixed restricted Observation refresh; input files carry identity and historical evidence only.
module BoardV2Application =
    open FS.GG.Coord.GitHub

    /// Decode the same closed wire shape for historical organization v2, receiver v3 or source-only programme v4.
    /// Product mode requires its selected organization-owned project, one repository and native cohort;
    /// no organization default, user-owned execution or legacy board fallback is introduced.
    val parseBinding: json: string -> Result<V2Projection.Binding, string>
    /// Emit an explicit report wire shape, including gaps and independent acceptance facts.
    val encodeReport: report: V2ProjectionSource.BatchReport -> string
    /// Encode structurally read-only inspection, human planning values and native PR facts and action-specific selection.
    val encodeInspection: report: V2ProjectionSource.InspectionReport -> string
    /// Existing integrators may supply typed current action facts; no command/file can mint acceptance.
    val encodeInspectionWithContext:
        report: V2ProjectionSource.InspectionReport -> context: V2ProjectionSource.SelectionContext option -> now: System.DateTimeOffset -> string
    /// Decode historical verification evidence. It cannot authorize a current observation or write.
    val decodePreviousReport: json: string -> Result<V2ProjectionSource.BatchReport, string>
    /// Validate the production Board V2 argument grammar without reading files or creating a transport.
    /// Return None for other command families; normalized-path and output custody remain runtime checks.
    val validateInvocation: arguments: string list -> Result<unit, string> option
    /// Fixed refresh or read-only inspect file command against an injected transport, for offline fixtures.
    val runWithTransport: transport: Transport.IGitHubTransport -> arguments: string list -> int
    /// Recognize board-v2 after validation; refresh composes the qualified writer, inspect only canonical reads.
    val tryRun: arguments: string list -> int option
