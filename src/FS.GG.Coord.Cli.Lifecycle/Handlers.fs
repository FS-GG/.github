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

    /// Handler ownership is data. The production composition supplies the IO boundary while this
    /// family supplies the command inventory, so additions cannot silently fall back to Client.run.
    let handlers dependencies =
        [ Options.DeliveryCmd, dependencies.Delivery
          Options.ReviewCmd, dependencies.Review
          Options.RouteCmd, dependencies.Route
          Options.Landable, dependencies.Landable
          Options.DoneCmd, dependencies.Done
          Options.VerifyPaths, dependencies.VerifyPaths
          Options.Followup, dependencies.Followup ]
