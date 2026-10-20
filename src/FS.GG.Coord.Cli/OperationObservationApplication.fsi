namespace FS.GG.Coord.Cli

module OperationObservationApplication =
    type Input = { Identity: FS.GG.Coord.OperationObservation.Identity; Seconds: int }
    val parseInput: string -> Result<Input, string>
    val encodeCursor: FS.GG.Coord.OperationObservation.Cursor -> string
    val decodeCursor: string -> Result<FS.GG.Coord.OperationObservation.Cursor, string>
    /// Synchronous finite loop. Persist is called before each native request and before returning attention.
    /// Unchanged samples produce no emit/model callback. Clock and sleep share the original clock domain.
    val observe:
        transport: FS.GG.Coord.GitHub.Transport.IDeadlineSinglePageGitHubTransport ->
        clock: (unit -> int64) -> sleep: (int -> unit) ->
        persist: (FS.GG.Coord.OperationObservation.Cursor -> unit) ->
        evidence: (FS.GG.Coord.OperationObservation.Sample -> string * int * (unit -> unit)) ->
        initial: FS.GG.Coord.OperationObservation.Cursor -> FS.GG.Coord.OperationObservation.Decision
    val tryRun: string list -> int option
