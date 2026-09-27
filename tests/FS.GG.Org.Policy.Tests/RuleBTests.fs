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

    [<Theory>]
    [<InlineData("src/B/B?.fsproj", "src/B/Bx.fsproj")>]
    [<InlineData("src/B/B+.fsproj", "src/B/B+.fsproj")>]
    [<InlineData("src/B/B[1].fsproj", "src/B/B[1].fsproj")>]
    let ``unimplemented GitHub glob operators cannot certify coverage`` pattern dependency =
        let source =
            Map.ofList [ "src/A/A.fsproj", [ dependency ]; dependency, [] ]
        match RuleB.inspect [ "src/A/**"; pattern ] source with
        | Error diagnostic -> Assert.Equal("coverage-input", diagnostic.Code)
        | Ok coverage -> failwithf "unsupported filter %s certified coverage: %A" pattern coverage

    [<Fact>]
    let ``reference cycles terminate and do not cover an omitted project`` () =
        let cyclic = graph.Add("src/C/C.fsproj", [ "src/B/B.fsproj" ])
        let coverage = inspected [ "src/A/**" ] cyclic
        Assert.Equal(2, coverage.Uncovered.Length)

    [<Fact>]
    let ``uncovered missing graph node still refuses incomplete closure`` () =
        let source = Map.ofList [ "src/A/A.fsproj", [ "src/Missing/Missing.fsproj" ] ]
        match RuleB.inspect [ "src/A/**" ] source with
        | Error diagnostic ->
            Assert.Equal("coverage-input", diagnostic.Code)
            Assert.Contains("src/Missing/Missing.fsproj", diagnostic.Message)
        | Ok coverage -> failwithf "missing graph node cannot certify closure: %A" coverage

    [<Fact>]
    let ``covered missing graph node cannot certify a complete project closure`` () =
        let source = Map.ofList [ "src/A/A.fsproj", [ "src/B/B.fsproj" ] ]
        match RuleB.inspect [ "src/A/**"; "src/B/**" ] source with
        | Error diagnostic ->
            Assert.Equal("coverage-input", diagnostic.Code)
            Assert.Contains("absent from supplied project graph", diagnostic.Message)
            Assert.Contains("src/B/B.fsproj", diagnostic.Message)
        | Ok coverage -> failwithf "missing B cannot certify closure: %A" coverage

    [<Fact>]
    let ``exact pattern without final newline cannot cover a newline suffixed dependency`` () =
        let dependency = "src/B/B.fsproj\n"
        let source = Map.ofList [ "src/A/A.fsproj", [ dependency ]; dependency, [] ]
        let coverage = inspected [ "src/A/**"; "src/B/B.fsproj" ] source
        Assert.Equal<(string * string) list>([ "src/A/A.fsproj", dependency ], coverage.Uncovered)

    [<Fact>]
    let ``exact pattern including final newline still covers the same dependency`` () =
        let dependency = "src/B/B.fsproj\n"
        let source = Map.ofList [ "src/A/A.fsproj", [ dependency ]; dependency, [] ]
        let coverage = inspected [ "src/A/**"; dependency ] source
        Assert.Empty(coverage.Uncovered)

    [<Theory>]
    [<InlineData("../src/A/**")>]
    [<InlineData("/src/A/**")>]
    [<InlineData("src\\A\\**")>]
    [<InlineData("C:/src/A/**")>]
    [<InlineData("C:src/A/**")>]
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

    [<Fact>]
    let ``drive-prefixed project identity is not a repo-relative graph node`` () =
        let malformed = Map.ofList [ "C:/repo/src/A/A.fsproj", [] ]
        match RuleB.inspect [ "src/A/**" ] malformed with
        | Error diagnostic ->
            Assert.Equal("coverage-input", diagnostic.Code)
            Assert.Contains("normalized repo-relative", diagnostic.Message)
        | Ok coverage -> failwithf "drive-prefixed node must refuse: %A" coverage

    [<Fact>]
    let ``drive-prefixed reference cannot be covered as a repo dependency`` () =
        let dependency = "C:/repo/src/B/B.fsproj"
        let malformed = Map.ofList [ "src/A/A.fsproj", [ dependency ]; dependency, [] ]
        match RuleB.inspect [ "src/A/**" ] malformed with
        | Error diagnostic ->
            Assert.Equal("coverage-input", diagnostic.Code)
            Assert.Contains("normalized repo-relative", diagnostic.Message)
        | Ok coverage -> failwithf "drive-prefixed dependency must refuse: %A" coverage
