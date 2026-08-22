namespace FS.GG.Coord.Cli.BoardOps

open FS.GG.Coord.Cli
open FS.GG.Coord.Cli.Kernel

module Handlers =
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
