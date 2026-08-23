namespace FS.GG.Coord.Cli.Lifecycle

open FS.GG.Coord.Cli

module HandlerRegistration =
    type Handler = Options.Options -> int

    val commands: Options.Command list
    val validate: expected: Options.Command list -> registrations: (Options.Command * Handler) list -> Result<Map<Options.Command, Handler>, string list>
