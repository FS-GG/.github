namespace FS.GG.Coord.Cli

/// JSON snapshot boundary for the pure resumable review/repair protocol (.github#2175), mirroring
/// `DeliveryApplication`'s pure snapshot-JSON contract exactly.
module ReviewApplication =
    /// Render one review-protocol verdict from a snapshot supplied on `--snapshot FILE` or stdin.
    val render: Options.Options -> FS.GG.Coord.Review.Binding -> FS.GG.Coord.Review.Facts -> int

    val run: Options.Options -> int
