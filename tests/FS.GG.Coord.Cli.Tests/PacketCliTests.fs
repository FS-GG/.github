namespace FS.GG.Coord.Cli.Tests

open System
open System.IO
open System.Text.Json
open Xunit
open FS.GG.Coord
open FS.GG.Coord.Cli

/// .github#2737 — `packet validate` driven through `Program.main`, the same entry point
/// `scripts/fsgg-coord` execs.
///
/// The decode and validation contract is exercised purely in `FindingPacketTests`; what is left to
/// pin HERE is the command boundary: the exit codes, and the split that makes the output usable —
/// one machine document on stdout, the per-field reading a finder acts on to stderr.
module PacketCliTests =
    /// A real filed packet: `.github#2691` comment 5304198465, lifted field by field.
    let private real =
        """{"schema":"fsgg.coord.finding-packet/v1",
            "surface":"src/FS.GG.Coord.Cli/DeliveryRouteApplication.fs",
            "cause":{"established":"the verb was never wired into the command surface"},
            "redToday":{"found":"nothing dispatches to DeliveryRouteApplication.run"},
            "derivedBy":{"notSearched":"an adjudicator should check whether a gate already derives this"},
            "classRow":{"notSearched":"this may be evidence on a wiring/coverage class row"},
            "whyNotHere":"no claim and no lane; the fix is engine source the pass did not declare",
            "paths":["src/FS.GG.Coord.Cli/Options.fs"],
            "finder":"merlin-efd3"}"""

    let private invoke (document: string) =
        let unique = Guid.NewGuid().ToString "n"
        let path = Path.Combine(Path.GetTempPath(), $"fsgg-2737-%s{unique}.json")
        File.WriteAllText(path, document)
        let previousOut = Console.Out
        let previousError = Console.Error
        use out = new StringWriter()
        use err = new StringWriter()

        try
            Console.SetOut out
            Console.SetError err
            let code = Program.main [| "packet"; "validate"; path |]
            Console.Out.Flush()
            Console.Error.Flush()
            code, out.ToString(), err.ToString()
        finally
            Console.SetOut previousOut
            Console.SetError previousError
            File.Delete path

    [<Fact>]
    let ``#2737 a valid packet exits green with an fsgg.coord.packet-result/v1 document`` () =
        let code, stdout, stderr = invoke real
        Assert.Equal(ExitCode.toInt ExitCode.Green, code)
        Assert.Equal("", stderr.Trim())

        use document = JsonDocument.Parse(stdout.Trim())
        let root = document.RootElement
        Assert.Equal(FindingPacket.ResultSchema, root.GetProperty("schema").GetString())
        Assert.Equal("validated", root.GetProperty("kind").GetString())
        Assert.Equal("merlin-efd3", root.GetProperty("finder").GetString())
        Assert.Equal(0, root.GetProperty("writes").GetInt32())
        // Which bar tests the finder actually searched, visible without reading the prose.
        Assert.Equal("notSearched", root.GetProperty("derivedBy").GetString())

    [<Fact>]
    let ``#2737 a malformed packet exits non-zero and reads its findings on STDERR`` () =
        // The sentinels written the way the whole register writes them today.
        let damaged = real.Replace("""{"notSearched":"an adjudicator should check whether a gate already derives this"}""", "\"none\"")
        Assert.NotEqual<string>(real, damaged) // the mutation must have applied, or this test asserts nothing

        let code, stdout, stderr = invoke damaged
        Assert.NotEqual<int>(ExitCode.toInt ExitCode.Green, code)

        // stdout stays ONE parseable document, so a caller that pipes it is never handed prose.
        use document = JsonDocument.Parse(stdout.Trim())
        Assert.Equal("refusal", document.RootElement.GetProperty("kind").GetString())

        // and the finder's actual reading — the field, and the shape that was meant — is on stderr.
        Assert.Contains("derivedBy", stderr)
        Assert.Contains("searchedNotFound", stderr)
        Assert.Contains("notSearched", stderr)

    [<Fact>]
    let ``#2737 an unreadable file is a refusal document, not an exception`` () =
        let previousOut = Console.Out
        use out = new StringWriter()

        try
            Console.SetOut out
            let code = Program.main [| "packet"; "validate"; "no-such-file-2737.json" |]
            Assert.NotEqual<int>(ExitCode.toInt ExitCode.Green, code)
            use document = JsonDocument.Parse(out.ToString().Trim())
            Assert.Equal("refusal", document.RootElement.GetProperty("kind").GetString())
        finally
            Console.SetOut previousOut

    [<Fact>]
    let ``#2737 an unknown action is refused rather than treated as validate`` () =
        let previousOut = Console.Out
        use out = new StringWriter()

        try
            Console.SetOut out
            let code = Program.main [| "packet"; "apply"; "anything.json" |]
            Assert.NotEqual<int>(ExitCode.toInt ExitCode.Green, code)
            Assert.Contains("expected validate", out.ToString())
        finally
            Console.SetOut previousOut
