namespace FS.GG.Org.Policy.Tests

open FS.GG.Org.Policy
open Xunit

module RuleBExceptionTests =
    let private coverage =
        { Subjects = [ "src/A/A.fsproj" ]
          Uncovered =
            [ "src/A/A.fsproj", "src/B/B.fsproj"
              "src/A/A.fsproj", "src/C/C.fsproj" ] }

    let private workflow body = "on: {push: {paths: [src/A/**]}}\n" + body

    let private resolved yaml =
        match RuleBExceptions.inspect "sample.yml" yaml coverage with
        | Ok result -> result
        | Error diagnostic -> failwithf "unexpected syntax refusal: %A" diagnostic

    [<Theory>]
    [<InlineData("|")>]
    [<InlineData(">")>]
    let ``marker inside run block scalar cannot excuse an omitted dependency`` style =
        let yaml = workflow ("jobs:\n  check:\n    steps:\n      - run: " + style + "\n          # paths-coherence: allow-uncovered src/B — example only\n          echo done\n")
        Assert.Equal(RuleBExceptions.Uncovered, (resolved yaml).["src/B"])

    [<Fact>]
    let ``marker inside multiline quoted scalar cannot excuse an omitted dependency`` () =
        let yaml = workflow "name: \"example\n  # paths-coherence: allow-uncovered src/B — quoted only\n  continued\"\n"
        Assert.Equal(RuleBExceptions.Uncovered, (resolved yaml).["src/B"])

    [<Fact>]
    let ``standalone signed marker excuses only its exact directory`` () =
        let yaml = "# paths-coherence: allow-uncovered src/B — deliberate\n" + workflow ""
        let result = resolved yaml
        Assert.Equal(RuleBExceptions.Signed "deliberate", result.["src/B"])
        Assert.Equal(RuleBExceptions.Uncovered, result.["src/C"])

    [<Fact>]
    let ``similar marker path or extended marker verb cannot excuse a directory`` () =
        let yaml =
            "# paths-coherence: allow-uncovered src/B-extra — different directory\n"
            + "# paths-coherence: allow-uncoveredevil src/B — wrong marker verb\n"
            + workflow ""
        Assert.Equal(RuleBExceptions.Uncovered, (resolved yaml).["src/B"])

    [<Fact>]
    let ``unsigned marker is a finding input rather than a signed exemption`` () =
        let yaml = workflow "# paths-coherence: allow-uncovered src/B\n"
        Assert.Equal(RuleBExceptions.Unsigned, (resolved yaml).["src/B"])

    [<Fact>]
    let ``missing marker leaves every uncovered directory visible`` () =
        let result = resolved (workflow "")
        Assert.Equal(2, result.Count)
        Assert.Equal(RuleBExceptions.Uncovered, result.["src/B"])
        Assert.Equal(RuleBExceptions.Uncovered, result.["src/C"])

    [<Fact>]
    let ``malformed YAML refuses before applying a comment`` () =
        let yaml = "# paths-coherence: allow-uncovered src/B — signed\non: [broken\n"
        match RuleBExceptions.inspect "sample.yml" yaml coverage with
        | Error diagnostic -> Assert.Equal("yaml-invalid", diagnostic.Code)
        | Ok result -> failwithf "malformed YAML must refuse: %A" result
