namespace FS.GG.Coord.GitHub

/// Low-level JSON envelope reader used by both transport metering and the public GraphQl adapter.
/// No production reader may inspect a GraphQL envelope outside this boundary.
module internal GraphQlEnvelope =
    val tryMeter: body: string -> (int * int) option
