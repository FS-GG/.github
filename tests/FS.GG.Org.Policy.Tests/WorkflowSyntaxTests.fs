namespace FS.GG.Org.Policy.Tests

open FS.GG.Org.Policy
open Xunit

module WorkflowSyntaxTests =
    let private parsed yaml =
        match WorkflowSyntax.inspect "sample.yml" yaml with
        | Ok value -> value
        | Error diagnostic -> failwithf "%s: %s" diagnostic.Code diagnostic.Message

    let private refused code yaml =
        match WorkflowSyntax.inspect "sample.yml" yaml with
        | Error diagnostic -> Assert.Equal(code, diagnostic.Code)
        | Ok _ -> failwithf "expected %s refusal" code

    [<Theory>]
    [<InlineData("on: pull_request\n", true, false)>]
    [<InlineData("'on': [push, pull_request]\n", true, true)>]
    [<InlineData("on: {push: null}\n", false, true)>]
    let ``event spelling stays visible`` yaml hasPr hasPush =
        let value = parsed yaml
        Assert.Equal(hasPr, value.PullRequest.Declared)
        Assert.Equal(hasPush, value.Push.Declared)

    [<Fact>]
    let ``null event differs from explicit null paths`` () =
        let value = parsed "on:\n  pull_request:\n  push: {paths: null}\n"
        Assert.Equal(Missing, value.PullRequest.Paths)
        Assert.Equal(Invalid "paths is present but is not a sequence", value.Push.Paths)

    [<Fact>]
    let ``sequence paths remain ordered syntax`` () =
        let value = parsed "on:\n  pull_request: {paths: ['src/**', '!src/private/**']}\n"
        Assert.Equal(Sequence ["src/**"; "!src/private/**"], value.PullRequest.Paths)

    [<Fact>]
    let ``block scalar marker stays run data`` () =
        let yaml = "on: [push, pull_request]\njobs:\n  test:\n    steps:\n      - run: |\n          # paths-coherence: allow-divergence — inert shell text\n"
        let value = parsed yaml
        Assert.Single(value.RunScalars) |> ignore
        Assert.Contains("allow-divergence", value.RunScalars.Head)

    [<Fact>]
    let ``multiple documents refuse`` () =
        refused "document-count" "on: push\n---\non: pull_request\n"

    [<Fact>]
    let ``duplicate keys refuse`` () =
        refused "yaml-invalid" "on:\n  push: null\n  push: null\n"

    [<Fact>]
    let ``non scalar path pattern is kept invalid`` () =
        let value = parsed "on:\n  push:\n    paths: [src/**, {bad: shape}]\n"
        Assert.Equal(Invalid "paths contains a non-scalar pattern", value.Push.Paths)

    [<Fact>]
    let ``unknown on shape refuses`` () =
        refused "on-shape" "on: 3\n"
