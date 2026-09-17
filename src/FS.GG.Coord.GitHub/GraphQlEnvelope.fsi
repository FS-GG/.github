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

    val tryMeter: body: string -> (int * int) option
    val classifyMutation: body: string -> MutationResult
