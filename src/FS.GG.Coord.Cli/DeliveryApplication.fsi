namespace FS.GG.Coord.Cli

/// JSON snapshot boundary for the pure claim-to-done lifecycle decision.
module DeliveryApplication =
    /// Render one lifecycle verdict from facts observed by either the snapshot or live adapter.
    val render: Options.Options -> FS.GG.Coord.Delivery.Snapshot -> int

    /// Parse only exact, head-bound v1 delivery obligation declarations and receipts from PR comments.
    val obligationsFromComments:
        headSha: string ->
        comments: FS.GG.Coord.Driver.ReviewComment list ->
            Result<FS.GG.Coord.Delivery.Obligation list, string>

    val run: Options.Options -> int
