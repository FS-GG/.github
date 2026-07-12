namespace FS.GG.Coord.Cli.Tests

open Xunit
open FS.GG.Coord.Cli
open FS.GG.Coord.Cli.Options

/// THE RESIDUE RULE. An argument that is ignored is indistinguishable, from the caller's side, from an
/// argument that was honoured — and the caller then acts on an answer to a question it did not ask.
///
/// SDD paid for this once: `init --project-root /tmp/b` silently seeded the CURRENT directory and
/// reported success. The parser had simply not been taught the flag, so it dropped it and carried on
/// confidently. Same shape as a gate reporting green over a subject it never read (#266) — one layer
/// down, in argv.
module OptionsTests =

    let private ok =
        function
        | Ok o -> o
        | Error(e: string) -> failwithf "expected the args to parse, got: %s" e

    let private rejected =
        function
        | Ok(o: Options) -> failwithf "expected the args to be REJECTED, but they parsed to %A" o
        | Error e -> e

    [<Fact>]
    let ``an unknown flag is NAMED and refused, never shrugged off`` () =
        let e = parse [ "decide"; "--engine=fs" ] |> rejected
        Assert.Contains("--engine=fs", e)

    [<Fact>]
    let ``an unknown command is named and refused`` () =
        let e = parse [ "schedule" ] |> rejected
        Assert.Contains("schedule", e)

    [<Fact>]
    let ``a flag given without its value does NOT swallow the next flag`` () =
        // The subtle one. `--snapshot --json` must be "you forgot the filename", not "the snapshot
        // lives in a file called --json" — and certainly not a silently-unset option plus a silently
        // consumed one.
        let e = parse [ "decide"; "--snapshot"; "--json" ] |> rejected
        Assert.Contains("--snapshot", e)

    [<Fact>]
    let ``a trailing flag with no value at all is refused`` () =
        let e = parse [ "decide"; "--snapshot" ] |> rejected
        Assert.Contains("--snapshot", e)

    [<Fact>]
    let ``JSON is the default projection — it is the contract, and the client parses it`` () =
        Assert.Equal(Json, (parse [ "decide" ] |> ok).Render)

    [<Fact>]
    let ``the text projection is opt-in`` () =
        Assert.Equal(Text, (parse [ "decide"; "--text" ] |> ok).Render)

    [<Fact>]
    let ``a snapshot file is accepted for debugging`` () =
        Assert.Equal(Some "/tmp/s.json", (parse [ "decide"; "--snapshot"; "/tmp/s.json" ] |> ok).SnapshotFile)

    [<Fact>]
    let ``no arguments prints help rather than deciding over an empty board`` () =
        Assert.Equal(Help, (parse [] |> ok).Command)
