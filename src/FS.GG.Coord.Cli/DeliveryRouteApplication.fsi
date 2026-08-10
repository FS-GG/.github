namespace FS.GG.Coord.Cli

/// Strict local codec and validation projection for an agent-authored delivery route receipt.
module DeliveryRouteApplication =
    val decode: string -> Result<FS.GG.Coord.DeliveryRoute.Receipt, string>
    val run: Options.Options -> int
