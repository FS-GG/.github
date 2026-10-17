namespace FS.GG.Coord.Cli

/// Restricted Observation refresh and read-only programme inspection; files carry identity/history only.
module BoardV2Application =
    open FS.GG.Coord.GitHub

    /// Decode the same closed wire shape for historical organization v2, receiver v3 or source-only programme v4.
    /// Product mode requires its selected organization-owned project, one repository and native cohort;
    /// no organization default, user-owned execution or legacy board fallback is introduced.
    val parseBinding: json: string -> Result<V2Projection.Binding, string>
    /// Decode one ephemeral organization-v4 fsgg.coord.board-v2-integrator-observations/1 document.
    /// The schema is closed at every object: duplicate, unknown and missing keys are errors;
    /// null explicitly means a missing capacity/reservation observation, never an empty population.
    /// Bounds: 262144 UTF-8 bytes, depth 12, 64 unique slices, 256 reservations, 128 paths or
    /// prerequisites per slice, 16 evidence references per observation and nonnegative counts <=10000.
    /// Exact project/schema/recipe/artifact/population and enrolled issue identities are mandatory.
    /// Native issue ID/update/body/source-plan revision and PR ID/update/head are joined after reads.
    /// Original observation/expiry intervals are retained; decoding never renews a supplier fact.
    /// InputSha256 hashes the strict UTF-8 encoding of the received document text, not an authentication token.
    /// Supplier/evidence text and the exact input digest are integrator-reported, not authentication
    /// or native acceptance. Errors never echo raw input. Current native joins remain mandatory.
    val parseIntegratorObservations:
        binding: V2Projection.Binding -> json: string -> Result<V2ProjectionSource.SelectionContext, string>
    /// Emit an explicit report wire shape, including gaps and independent acceptance facts.
    val encodeReport: report: V2ProjectionSource.BatchReport -> string
    /// Encode structurally read-only inspection, human planning values, native PR facts and action-specific selection.
    /// Product inspection/1 retains its closed wire shape; programme inspection/2 alone exposes successor fields.
    /// Without --integrator-stdin the CLI reports missing attributed action/resource observations.
    val encodeInspection: report: V2ProjectionSource.InspectionReport -> string
    /// Existing integrators may supply attributed current action facts; no command/input can mint acceptance.
    /// Context is caller-asserted evidence, not authentication; every derived action still grants no dispatch authority.
    /// Per-slice output preserves stable IDs, owners, operation demand, native joins and reported provenance.
    /// Explicit ordered selection consumes only selected operation demand; unselected alternatives are not holds.
    val encodeInspectionWithContext:
        report: V2ProjectionSource.InspectionReport -> context: V2ProjectionSource.SelectionContext option -> now: System.DateTimeOffset -> string
    /// Decode historical verification evidence. It cannot authorize a current observation or write.
    val decodePreviousReport: json: string -> Result<V2ProjectionSource.BatchReport, string>
    /// Validate the production Board V2 argument grammar without reading files or creating a transport.
    /// Return None for other command families; normalized-path and output custody remain runtime checks.
    val validateInvocation: arguments: string list -> Result<unit, string> option
    /// Production inspect composition with one bounded --integrator-stdin observation document.
    /// The injected reader avoids process-global Console changes; input is consumed once and never cached.
    /// No flag leaves the reader untouched. Refresh and product bindings refuse the flag before reading it.
    /// Native queries/permission reads retain the existing shared request/time budget and no-disk intake gate.
    /// Missing runtime suppliers and relevant native gaps remain attributed Unknown, without blocking inspection.
    val runWithTransportAndInput:
        transport: Transport.IGitHubTransport -> reader: System.IO.TextReader -> arguments: string list -> int
    /// Fixed refresh or read-only inspect command against an injected transport, for offline fixtures.
    val runWithTransport: transport: Transport.IGitHubTransport -> arguments: string list -> int
    /// Recognize board-v2 after validation; refresh composes the qualified writer, inspect only canonical reads.
    val tryRun: arguments: string list -> int option
