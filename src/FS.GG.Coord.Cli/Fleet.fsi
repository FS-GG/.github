namespace FS.GG.Coord.Cli

/// The wire contract for the fleet divergence ledger (#634).
///
/// Same division of labour as `Snapshot`, and for the same reason: **the engine reads nothing.** The
/// bash client has already paid for the ledger read — one REST-paginated fetch of the marker comments
/// on the ledger issue, on the 5,000-*requests*/hr budget that does NOT die under fan-out — and hands
/// the parsed rows over on stdin. The engine folds them into the verdict and returns it.
///
/// So the CRITERION is here, in one total function over typed rows, and the IO is over there. Which is
/// the whole of ADR-0034: the rule has exactly one home, and it is not a shell pipeline.
module Fleet =

    open System
    open FS.GG.Coord
    open FS.GG.Coord.Types

    type Error = Json.Error

    /// What the client asks: *"is ADR-0034 §5's cut-over criterion met, for THIS engine build, TODAY?"*
    ///
    /// `Today` is carried on the WIRE rather than read from the engine's clock. The engine is pure and
    /// reads nothing — not the board, not the network, and not the time. It also makes the day-boundary
    /// behaviour, which is exactly where a "three consecutive days" rule is most likely to be wrong,
    /// something a test can state rather than something a test has to wait for.
    type Query =
        { Engine: string
          RequiredDays: int
          MinWorkers: int
          Today: DateOnly
          Reports: Divergence.Report list }

    /// Read a ledger. Returns EVERY error, not the first.
    ///
    /// A NEGATIVE count is refused, not clamped. `compared: -1` is a client that computed its summary
    /// wrong, and silently reading it as `0` would turn a broken publisher into a day that merely looks
    /// uncovered — a defect disguised as a fact, which is the substitution this codec exists to refuse.
    val parse: json: string -> Result<Query, Error list>

    /// Render the verdict as the response document.
    val render: verdict: Verdict<Divergence.Evidence> -> string
