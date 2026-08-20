namespace FS.GG.Coord.Cli.Tests

open System
open System.IO
open Xunit
open FS.GG.Coord
open FS.GG.Coord.Cli

module IntakeCliTests =
    let private invokePure (json: string) (action: string) =
        let path = Path.GetTempFileName()
        File.WriteAllText(path, json)
        let old = Console.Out
        use writer = new StringWriter()
        try
            Console.SetOut(writer)
            let opts = Options.parse [ "intake"; action; path ] |> Result.defaultWith failwith
            let code = IntakeApplication.run opts
            code, writer.ToString().Trim()
        finally
            Console.SetOut(old)
            File.Delete(path)

    let private valid = """{"schema":"fsgg.coord.intake/v1","id":"intake-42","owner":"FS-GG","repository":".github","title":"t","observed":"o","rootCause":"r","acceptance":"a","verification":"v","paths":["src/FS.GG.Coord.Core"],"class":"hardening","status":"Backlog","backlogReason":"not-yet-actionable","disposition":"create"}"""

    [<Fact>]
    let ``#2134 intake validate renders a typed zero-write receipt`` () =
        let path = Path.GetTempFileName()
        File.WriteAllText(path, valid)
        let old = Console.Out
        let oldToken = Environment.GetEnvironmentVariable "GITHUB_TOKEN"
        use writer = new StringWriter()
        try
            Environment.SetEnvironmentVariable("GITHUB_TOKEN", null)
            Console.SetOut(writer)
            let code = Program.main [| "intake"; "validate"; path |]
            Assert.Equal(ExitCode.toInt ExitCode.Green, code)
            Assert.Contains("\"kind\":\"validated\"", writer.ToString())
            Assert.Contains("\"writes\":0", writer.ToString())
        finally
            Console.SetOut(old)
            Environment.SetEnvironmentVariable("GITHUB_TOKEN", oldToken)
            File.Delete(path)

    [<Fact>]
    let ``#2134 public validate refuses a nonexistent live path without a token`` () =
        let invalid = valid.Replace("src/FS.GG.Coord.Core", "definitely/not/a/live/path-2134")
        let code, output = invokePure invalid "validate"
        Assert.Equal(ExitCode.toInt ExitCode.Error, code)
        Assert.Contains("do not exist", output)

    [<Fact>]
    let ``#2134 intake decoder refuses unknown fields instead of ignoring them`` () =
        let code, output = invokePure (valid.Replace("}", ",\"surprise\":true}")) "validate"
        Assert.Equal(ExitCode.toInt ExitCode.Error, code)
        Assert.Contains("\"kind\":\"refusal\"", output)
        Assert.Contains("unknown draft field", output)

    [<Fact>]
    let ``#2134 public intake apply reaches the live transaction dispatcher`` () =
        let path = Path.GetTempFileName()
        File.WriteAllText(path, valid)
        let previousToken = Environment.GetEnvironmentVariable "GITHUB_TOKEN"
        let previousGhToken = Environment.GetEnvironmentVariable "GH_TOKEN"
        let old = Console.Error
        use writer = new StringWriter()
        try
            Environment.SetEnvironmentVariable("GITHUB_TOKEN", null)
            Environment.SetEnvironmentVariable("GH_TOKEN", null)
            Console.SetError(writer)
            let code = Program.main [| "intake"; "apply"; path |]
            let output = writer.ToString()
            Assert.Equal(ExitCode.toInt ExitCode.Error, code)
            Assert.Contains("needs a GitHub token", output)
            Assert.DoesNotContain("live intake apply is not wired", output)
        finally
            Console.SetError(old)
            Environment.SetEnvironmentVariable("GITHUB_TOKEN", previousToken)
            Environment.SetEnvironmentVariable("GH_TOKEN", previousGhToken)
            File.Delete(path)
