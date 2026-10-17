namespace FS.GG.Coord.GitHub

/// Low-level JSON envelope reader used by both transport metering and the public GraphQl adapter.
/// No production reader may inspect a GraphQL envelope outside this boundary.
module internal GraphQlEnvelope =
    type MutationResult =
        | InvalidJson
        | NotObject
        | InvalidErrors
        | Errors
        | Partial
        | Applied
        | NoResult

    type MigrationClearResult =
        | ClearSuccess
        | ClearErrors
        | ClearMalformed

    /// Migration-only clear-success classification with a 4 MiB/depth-32 bound, recursively
    /// duplicate-free objects, a data object and absent or empty-array errors. Generic readers
    /// and transport metering retain their existing semantics.
    val classifyMigrationClear: body: string -> MigrationClearResult

    val tryMeter: body: string -> (int * int) option
    val classifyMutation: body: string -> MutationResult
