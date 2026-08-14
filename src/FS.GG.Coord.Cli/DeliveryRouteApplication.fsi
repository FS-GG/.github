namespace FS.GG.Coord.Cli

/// Strict local codec and validation projection for an agent-authored delivery route receipt.
module DeliveryRouteApplication =
    /// Read-only compatibility decoder for legacy v1 receipts. Removed at the explicit M6 trigger.
    val decode: string -> Result<FS.GG.Coord.DeliveryRoute.Receipt, string>
    /// The only route form accepted by write paths from M4 onward.
    val decodeStructured: string -> Result<FS.GG.Coord.StructuredDecision.RouteRecord, string>
    val run: Options.Options -> int
