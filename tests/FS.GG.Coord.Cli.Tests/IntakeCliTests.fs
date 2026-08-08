namespace FS.GG.Coord.Cli.Tests

open System
open System.IO
open Xunit
open FS.GG.Coord
open FS.GG.Coord.Cli

module IntakeCliTests =
    let private invoke (json: string) (action: string) =
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

    let private valid = """{"schema":"fsgg.coord.intake/v1","id":"intake-42","owner":"FS-GG","repository":".github","title":"t","observed":"o","rootCause":"r","acceptance":"a","verification":"v","paths":["src/FS.GG.Coord.Core"],"class":"hardening","status":"Backlog","disposition":"create"}"""

    [<Fact>]
    let ``#2134 intake validate renders a typed zero-write receipt`` () =
        let code, output = invoke valid "validate"
        Assert.Equal(ExitCode.toInt ExitCode.Green, code)
        Assert.Contains("\"kind\":\"validated\"", output)
        Assert.Contains("\"writes\":0", output)

    [<Fact>]
    let ``#2134 intake decoder refuses unknown fields instead of ignoring them`` () =
        let code, output = invoke (valid.Replace("}", ",\"surprise\":true}")) "validate"
        Assert.Equal(ExitCode.toInt ExitCode.Error, code)
        Assert.Contains("\"kind\":\"refusal\"", output)
        Assert.Contains("unknown draft field", output)

    [<Fact>]
    let ``#2134 intake apply refuses before its live transaction exists`` () =
        let code, output = invoke valid "apply"
        Assert.Equal(ExitCode.toInt ExitCode.Error, code)
        Assert.Contains("live intake apply is not wired", output)
