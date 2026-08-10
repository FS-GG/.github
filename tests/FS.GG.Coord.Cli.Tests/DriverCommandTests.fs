namespace FS.GG.Coord.Cli.Tests

open Xunit
open FS.GG.Coord.Cli.Options

module DriverCommandTests =
    [<Fact>]
    let ``#2131 delivery is an additive JSON decision command`` () =
        match parse [ "delivery"; "--json" ] with
        | Ok opts -> Assert.Equal(DeliveryCmd, opts.Command)
        | Error message -> failwith message

    [<Fact>]
    let ``#2127 driver is an additive JSON decision command`` () =
        match parse [ "driver"; "--json" ] with
        | Ok opts -> Assert.Equal(DriverCmd, opts.Command)
        | Error message -> failwith message

    [<Fact>]
    let ``#2135 driver --events is an additive JSON/text projection over the same command`` () =
        match parse [ "driver"; "--events"; "--cursor"; "/tmp/cursor.json"; "--text" ] with
        | Ok opts ->
            Assert.Equal(DriverCmd, opts.Command)
            Assert.True(opts.Events)
            Assert.Equal(Some "/tmp/cursor.json", opts.CursorFile)
            Assert.Equal(Text, opts.Render)
        | Error message -> failwith message
