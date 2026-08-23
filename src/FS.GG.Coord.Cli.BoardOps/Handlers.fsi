namespace FS.GG.Coord.Cli.BoardOps

open FS.GG.Coord.Cli
open FS.GG.Coord.Cli.Kernel

module Handlers =

    /// Decide Ready eligibility from ADR-0045's sole dependency-edge authority.
    val readyDependencyVerdict: blockedByColumn: string option -> string option

    /// True when the Projects-v2 item revision changed between decision and mutation.
    val readyDependencyStale:
        before: FS.GG.Coord.GitHub.Board.BlockedByObservation option ->
        after: FS.GG.Coord.GitHub.Board.BlockedByObservation option ->
            bool

    [<Sealed>]
    type CommentCapability =
        member Path: string
        member Body: string
        member Cleanup: unit -> unit

    val allocateCommentCapability:
        worker: string ->
        item: FS.GG.Coord.Types.Ref ->
        source: string ->
            Result<CommentCapability, string>

    val addCmd: Context -> Options.Options -> int
    val flushCmd: Context -> Options.Options -> int
    val setField: Context -> Options.Options -> int
    val child: Context -> Options.Options -> int
    val say: Context -> Options.Options -> int
    val inbox: Context -> Options.Options -> int
    val roomOpen: Context -> Options.Options -> int
    val commentCmd: Context -> Options.Options -> int
    val bootstrapCmd: Context -> Options.Options -> int
    val boardCmd: Context -> int
    val fieldId: Context -> Options.Options -> int
    val optionId: Context -> Options.Options -> int
    val itemIdCmd: Context -> Options.Options -> int
    val bodyEditsCmd: Context -> Options.Options -> int
    val issues: Context -> Options.Options -> int
    val intakeCmd: Context -> Options.Options -> int
    val handlers: (Options.Command * HandlerRegistration.Handler) list
    val programHandlers: runWithContext: (Options.Options -> int) -> (Options.Command * (Options.Options -> int)) list
