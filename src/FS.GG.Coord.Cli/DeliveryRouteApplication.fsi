namespace FS.GG.Coord.Cli

/// Strict local codec and validation projection for an agent-authored delivery route receipt.
module DeliveryRouteApplication =
    /// The only route form accepted by write paths from M4 onward.
    val decodeStructured: string -> Result<FS.GG.Coord.StructuredDecision.RouteRecord, string>
    val run: Options.Options -> int
