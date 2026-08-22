namespace FS.GG.Coord.Cli.BoardOps

open FS.GG.Coord.Cli
open FS.GG.Coord.Cli.Kernel

module HandlerRegistration =
    type Handler = Context -> Options.Options -> int

    val commands: Options.Command list
    val validate:
        allCommands: Options.Command list ->
        registrations: (Options.Command * 'handler) list ->
        Result<Map<Options.Command, 'handler>, string list>
