namespace FS.GG.Org.Policy.Tests

open FS.GG.Org.Policy
open Xunit

module RuleBTests =
    let private graph =
        Map.ofList
            [ "src/A/A.fsproj", [ "src/B/B.fsproj" ]
              "src/B/B.fsproj", [ "src/C/C.fsproj" ]
              "src/C/C.fsproj", [] ]

    let private inspected patterns source =
        match RuleB.inspect patterns source with
        | Ok coverage -> coverage
        | Error diagnostic -> failwithf "unexpected refusal: %A" diagnostic

    [<Fact>]
    let ``single star cannot cover a nested transitive dependency`` () =
        let coverage = inspected [ "src/A/**"; "src/*" ] graph
        Assert.Equal<string list>([ "src/A/A.fsproj" ], coverage.Subjects)
        Assert.Equal<(string * string) list>(
            [ "src/A/A.fsproj", "src/B/B.fsproj"
              "src/A/A.fsproj", "src/C/C.fsproj" ], coverage.Uncovered)

    [<Fact>]
    let ``complete declared transitive closure has no uncovered dependency`` () =
        let coverage = inspected [ "src/A/**"; "src/B/**"; "src/C/**" ] graph
        Assert.Equal(3, coverage.Subjects.Length)
        Assert.Empty(coverage.Uncovered)

    [<Fact>]
    let ``catchall and single file patterns do not declare a project subject`` () =
        let coverage = inspected [ "src/**"; "src/A/Options.fs" ] graph
        Assert.Empty(coverage.Subjects)
        Assert.Empty(coverage.Uncovered)

    [<Fact>]
    let ``double star directory form matches zero or more directories`` () =
        let source = Map.ofList [ "src/A/A.fsproj", [ "src/B.fsproj" ]; "src/B.fsproj", [] ]
        let coverage = inspected [ "src/A/**"; "src/**/B.fsproj" ] source
        Assert.Empty(coverage.Uncovered)

    [<Fact>]
    let ``reference cycles terminate and do not cover an omitted project`` () =
        let cyclic = graph.Add("src/C/C.fsproj", [ "src/B/B.fsproj" ])
        let coverage = inspected [ "src/A/**" ] cyclic
        Assert.Equal(2, coverage.Uncovered.Length)

    [<Fact>]
    let ``unknown referenced project remains a visible uncovered dependency`` () =
        let source = Map.ofList [ "src/A/A.fsproj", [ "src/Missing/Missing.fsproj" ] ]
        let coverage = inspected [ "src/A/**" ] source
        Assert.Equal<(string * string) list>(
            [ "src/A/A.fsproj", "src/Missing/Missing.fsproj" ], coverage.Uncovered)

    [<Fact>]
    let ``exact pattern without final newline cannot cover a newline suffixed dependency`` () =
        let dependency = "src/B/B.fsproj\n"
        let source = Map.ofList [ "src/A/A.fsproj", [ dependency ] ]
        let coverage = inspected [ "src/A/**"; "src/B/B.fsproj" ] source
        Assert.Equal<(string * string) list>([ "src/A/A.fsproj", dependency ], coverage.Uncovered)

    [<Fact>]
    let ``exact pattern including final newline still covers the same dependency`` () =
        let dependency = "src/B/B.fsproj\n"
        let source = Map.ofList [ "src/A/A.fsproj", [ dependency ] ]
        let coverage = inspected [ "src/A/**"; dependency ] source
        Assert.Empty(coverage.Uncovered)

    [<Theory>]
    [<InlineData("../src/A/**")>]
    [<InlineData("/src/A/**")>]
    [<InlineData("src\\A\\**")>]
    [<InlineData("!src/A/**")>]
    [<InlineData("")>]
    let ``untrusted pattern shape refuses before a coverage verdict`` pattern =
        match RuleB.inspect [ pattern ] graph with
        | Error diagnostic -> Assert.Equal("coverage-input", diagnostic.Code)
        | Ok coverage -> failwithf "invalid pattern must refuse: %A" coverage

    [<Fact>]
    let ``unnormalized project reference refuses instead of disappearing from closure`` () =
        let malformed = Map.ofList [ "src/A/A.fsproj", [ "src/../B/B.fsproj" ] ]
        match RuleB.inspect [ "src/A/**" ] malformed with
        | Error diagnostic -> Assert.Equal("coverage-input", diagnostic.Code)
        | Ok coverage -> failwithf "unnormalized dependency must refuse: %A" coverage
