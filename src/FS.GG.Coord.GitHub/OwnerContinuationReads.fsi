namespace FS.GG.Coord.GitHub

/// Bounded current readback of an existing finite Root envelope and the SAME original CI run.
/// No cursor, clock, reservation, journal or native object is written or replaced.
module OwnerContinuationReads =
    open System
    open FS.GG.Coord
    open V2ProjectionSource
    open Transport

    /// Facts are native only because this adapter reads their authenticated source itself.
    /// Caller-supplied author names, admin membership and cursor.Terminal never authenticate an envelope.
    val collect:
        transport: IGitHubTransport -> report: InspectionReport -> context: SelectionContext ->
        now: DateTimeOffset -> request: OwnerContinuation.Request -> OwnerContinuation.Fact list
