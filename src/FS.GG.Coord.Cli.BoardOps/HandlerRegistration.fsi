namespace FS.GG.Coord.Cli.BoardOps

open FS.GG.Coord.Cli
open FS.GG.Coord.Cli.Kernel

module HandlerRegistration =
    type Handler = Context -> Options.Options -> int

    type Implementations =
        { Add: Handler
          Flush: Handler
          SetField: Handler
          Child: Handler
          BodyEdits: Handler
          FieldId: Handler
          OptionId: Handler
          ItemId: Handler
          Board: Handler
          Bootstrap: Handler
          Issues: Handler
          Intake: Handler
          Say: Handler
          Inbox: Handler
          RoomOpen: Handler }

    val commands: Options.Command list
    val handlers: implementations: Implementations -> (Options.Command * Handler) list
    val validate:
        allCommands: Options.Command list ->
        registrations: (Options.Command * 'handler) list ->
        Result<Map<Options.Command, 'handler>, string list>
