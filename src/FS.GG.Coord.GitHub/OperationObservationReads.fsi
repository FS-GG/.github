namespace FS.GG.Coord.GitHub

/// One complete bounded native sample for an explicitly selected original Actions run.
module OperationObservationReads =
    /// The caller charges and persists original request allowance before every GET.
    /// The transport physically caps transient bodies at4MiB; accepted pages are at most256KiB.
    /// No mutation interface, retry, redirect, implicit pagination or branch readiness is used.
    val collect:
        transport: Transport.IDeadlineSinglePageGitHubTransport ->
        identity: FS.GG.Coord.OperationObservation.Identity ->
        clock: (unit -> int64) -> workDeadline: int64 ->
        beforeRequest: (unit -> Result<unit, string>) ->
            Result<FS.GG.Coord.OperationObservation.Sample, string>
