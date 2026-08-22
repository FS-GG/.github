namespace FS.GG.Coord.Cli.Lifecycle

open FS.GG.Coord.Cli

module Handlers =
    type Dependencies =
        { Delivery: Options.Options -> int
          Review: Options.Options -> int
          Route: Options.Options -> int
          Landable: Options.Options -> int
          Done: Options.Options -> int
          VerifyPaths: Options.Options -> int
          Followup: Options.Options -> int }

    val handlers: dependencies: Dependencies -> (Options.Command * HandlerRegistration.Handler) list
