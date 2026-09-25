namespace FS.GG.Org.Policy.Tests

open FS.GG.Org.Policy
open Xunit

module RuleBWorkflowTests =
    let private graph =
        Map.ofList
            [ "src/A/A.fsproj", [ "src/B/B.fsproj" ]
              "src/B/B.fsproj", [ "src/C/C.fsproj" ]
              "src/C/C.fsproj", [] ]

    let private observed yaml =
        match RuleBWorkflow.inspect "sample.yml" yaml graph with
        | Ok observation -> observation
        | Error diagnostic -> failwithf "unexpected refusal: %A" diagnostic

    [<Fact>]
    let ``push-only filter still exposes its missing project closure`` () =
        let observation = observed "on: {push: {paths: [src/A/**]}}\n"
        Assert.Equal<Set<string>>(Set.ofList [ "src/A/A.fsproj" ], observation.Subjects)
        Assert.Equal(RuleBExceptions.Uncovered, observation.Omitted.["src/B"])
        Assert.Equal(RuleBExceptions.Uncovered, observation.Omitted.["src/C"])

    [<Fact>]
    let ``paired identical filters report each omitted directory once`` () =
        let observation = observed "on: {pull_request: {paths: [src/A/**]}, push: {paths: [src/A/**]}}\n"
        Assert.Equal(2, observation.Omitted.Count)
        Assert.Equal(1, observation.Subjects.Count)

    [<Fact>]
    let ``a covered sibling trigger does not erase another trigger's omission`` () =
        let yaml = "on: {pull_request: {paths: [src/A/**]}, push: {paths: [src/A/**, src/B/**, src/C/**]}}\n"
        let observation = observed yaml
        Assert.Equal(RuleBExceptions.Uncovered, observation.Omitted.["src/B"])
        Assert.Equal(RuleBExceptions.Uncovered, observation.Omitted.["src/C"])

    [<Fact>]
    let ``signed marker applies only to the named omitted directory`` () =
        let yaml = "# paths-coherence: allow-uncovered src/B — deliberate\non: {push: {paths: [src/A/**]}}\n"
        let observation = observed yaml
        Assert.Equal(RuleBExceptions.Signed "deliberate", observation.Omitted.["src/B"])
        Assert.Equal(RuleBExceptions.Uncovered, observation.Omitted.["src/C"])

    [<Fact>]
    let ``quoted marker cannot excuse an omitted directory`` () =
        let yaml =
            "on: {push: {paths: [src/A/**]}}\n"
            + "name: \"example\n  # paths-coherence: allow-uncovered src/B — data\n  continued\"\n"
        let observation = observed yaml
        Assert.Equal(RuleBExceptions.Uncovered, observation.Omitted.["src/B"])

    [<Theory>]
    [<InlineData("|")>]
    [<InlineData(">")>]
    let ``final block scalar line without newline cannot sign an omission`` style =
        let yaml =
            "on: {push: {paths: [src/A/**]}}\nname: " + style
            + "\n  something\n  # paths-coherence: allow-uncovered src/B — inert data"
        let observation = observed yaml
        Assert.Equal(RuleBExceptions.Uncovered, observation.Omitted.["src/B"])

    [<Fact>]
    let ``dedented comment after block scalar can sign an omission`` () =
        let yaml =
            "on: {push: {paths: [src/A/**]}}\nname: |\n  ordinary data\njobs: {}\n"
            + "# paths-coherence: allow-uncovered src/B — deliberate\n"
        let observation = observed yaml
        Assert.Equal(RuleBExceptions.Signed "deliberate", observation.Omitted.["src/B"])

    [<Fact>]
    let ``malformed one-sided filter refuses instead of disappearing`` () =
        match RuleBWorkflow.inspect "sample.yml" "on: {push: {paths: null}}\n" graph with
        | Error diagnostic -> Assert.Equal("paths-shape", diagnostic.Code)
        | Ok observation -> failwithf "null paths must refuse: %A" observation

    [<Fact>]
    let ``unfiltered workflow is an unscoped observation`` () =
        let observation = observed "on: {push: null}\n"
        Assert.Empty(observation.Subjects)
        Assert.Empty(observation.Omitted)
