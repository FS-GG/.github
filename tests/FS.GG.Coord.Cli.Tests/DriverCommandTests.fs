namespace FS.GG.Coord.Cli.Tests

open Xunit
open FS.GG.Coord.Cli.Options

module DriverCommandTests =
    [<Fact>]
    let ``#2127 driver is an additive JSON decision command`` () =
        match parse [ "driver"; "--json" ] with
        | Ok opts -> Assert.Equal(DriverCmd, opts.Command)
        | Error message -> failwith message
