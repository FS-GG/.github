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
    let ``multiline quoted path remains one scalar`` () =
        let value = parsed "on:\n  push:\n    paths:\n      - \"src/\n         **\"\n"
        Assert.Equal(Sequence ["src/ **"], value.Push.Paths)

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
    let ``duplicate on key with explicit string tag refuses`` () =
        refused "yaml-invalid" "on: push\n!!str on: pull_request\n"

    [<Fact>]
    let ``duplicate event key with explicit string tag refuses`` () =
        refused "yaml-invalid" "on:\n  push: null\n  !!str push: {paths: ['src/**']}\n"

    [<Fact>]
    let ``non scalar path pattern is kept invalid`` () =
        let value = parsed "on:\n  push:\n    paths: [src/**, {bad: shape}]\n"
        Assert.Equal(Invalid "paths contains a non-string pattern", value.Push.Paths)

    [<Theory>]
    [<InlineData("42")>]
    [<InlineData("true")>]
    [<InlineData("null")>]
    [<InlineData("~")>]
    let ``implicit non-string path pattern is invalid`` item =
        let value = parsed ("on: {push: {paths: [" + item + "]}}\n")
        Assert.Equal(Invalid "paths contains a non-string pattern", value.Push.Paths)

    [<Fact>]
    let ``quoted and explicitly tagged string paths are retained`` () =
        let value = parsed "on: {push: {paths: ['42', !!str null]}}\n"
        Assert.Equal(Sequence ["42"; "null"], value.Push.Paths)

    [<Fact>]
    let ``explicit string null is not a null event`` () =
        refused "event-shape" "on: {push: !!str null}\n"

    [<Fact>]
    let ``invalid explicitly tagged null event is not inferred as null`` () =
        refused "event-shape" "on: {push: !!int null}\n"

    [<Fact>]
    let ``invalid explicitly tagged event name is not inferred as push`` () =
        refused "on-shape" "on: {!!int push: null}\n"

    [<Fact>]
    let ``invalid explicitly tagged scalar event is not inferred as push`` () =
        refused "on-shape" "on: !!int push\n"

    [<Fact>]
    let ``self-referential alias refuses without a process crash`` () =
        refused "yaml-alias" "on: push\nloop: &x [*x]\n"

    [<Fact>]
    let ``unknown on shape refuses`` () =
        refused "on-shape" "on: 3\n"
