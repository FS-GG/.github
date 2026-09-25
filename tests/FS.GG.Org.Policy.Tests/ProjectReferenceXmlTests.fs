namespace FS.GG.Org.Policy.Tests

open FS.GG.Org.Policy
open Xunit

module ProjectReferenceXmlTests =
    let private parsed path xml =
        match ProjectReferenceXml.inspect path xml with
        | Ok references -> references
        | Error diagnostic -> failwithf "unexpected XML refusal: %A" diagnostic

    let private refused code path xml =
        match ProjectReferenceXml.inspect path xml with
        | Error diagnostic -> Assert.Equal(code, diagnostic.Code)
        | Ok references -> failwithf "expected %s refusal, got %A" code references

    [<Theory>]
    [<InlineData("&amp;")>]
    [<InlineData("&#38;")>]
    let ``decoded Include reaches the real Rule B dependency`` entity =
        let xml =
            "<Project><ItemGroup><ProjectReference Include='../B" + entity
            + "C/B" + entity + "C.fsproj' /></ItemGroup></Project>"
        let references = parsed "src/A/A.fsproj" xml
        Assert.Equal<string list>([ "src/B&C/B&C.fsproj" ], references)
        let graph = Map.empty.Add("src/A/A.fsproj", references)
        match RuleB.inspect [ "src/A/**"; "src/B" + entity + "C/**" ] graph with
        | Error diagnostic -> failwithf "unexpected coverage refusal: %A" diagnostic
        | Ok coverage ->
            Assert.Equal<(string * string) list>(
                [ "src/A/A.fsproj", "src/B&C/B&C.fsproj" ], coverage.Uncovered)

    [<Fact>]
    let ``single quoted Include and Windows separators resolve relative to project`` () =
        let xml = "<Project><ItemGroup><ProjectReference Include='..\\B\\B.fsproj' /></ItemGroup></Project>"
        Assert.Equal<string list>([ "src/B/B.fsproj" ], parsed "src/A/A.fsproj" xml)

    [<Fact>]
    let ``commented reference is not an edge`` () =
        let xml =
            "<Project><!-- <ProjectReference Include='../Fake/Fake.fsproj' /> -->"
            + "<ItemGroup><ProjectReference Include='../B/B.fsproj' /></ItemGroup></Project>"
        Assert.Equal<string list>([ "src/B/B.fsproj" ], parsed "src/A/A.fsproj" xml)

    [<Theory>]
    [<InlineData("<Project><Import Project='../../build/Refs.props' /></Project>")>]
    [<InlineData("<Project xmlns='urn:msbuild'><ImportGroup><Import Project='../../build/Refs.props' /></ImportGroup></Project>")>]
    let ``explicit Import cannot yield a complete single file project graph`` xml =
        // The imported file can add ProjectReference items absent from these XML bytes.
        // Returning [] lets an A-only workflow filter pass while imported B is uncovered.
        refused "project-reference" "src/A/A.fsproj" xml

    [<Fact>]
    let ``commented Import does not require evaluation`` () =
        let xml = "<Project><!-- <Import Project='../../build/Refs.props' /> --></Project>"
        Assert.Empty(parsed "src/A/A.fsproj" xml)

    [<Theory>]
    [<InlineData("<Target Name='Inject'><ItemGroup><ProjectReference Include='../B/B.fsproj' /></ItemGroup></Target>")>]
    [<InlineData("<ItemGroup><ProjectReference Include='../B/B.fsproj' /></ItemGroup><Target Name='Remove'><ItemGroup><ProjectReference Remove='../B/B.fsproj' /></ItemGroup></Target>")>]
    let ``target-time ProjectReference changes cannot become static graph facts`` inner =
        refused "project-reference" "src/A/A.fsproj" ("<Project>" + inner + "</Project>")

    [<Fact>]
    let ``unrelated target items do not obscure static ProjectReference`` () =
        let xml =
            "<Project><ItemGroup><ProjectReference Include='../B/B.fsproj' /></ItemGroup>"
            + "<Target Name='Generate'><ItemGroup><Content Include='generated.txt' /></ItemGroup></Target></Project>"
        Assert.Equal<string list>([ "src/B/B.fsproj" ], parsed "src/A/A.fsproj" xml)

    [<Fact>]
    let ``namespaced project reference is still an edge`` () =
        let xml =
            "<Project xmlns='urn:msbuild'><ItemGroup>"
            + "<ProjectReference Include='../B/B.fsproj' /></ItemGroup></Project>"
        Assert.Equal<string list>([ "src/B/B.fsproj" ], parsed "src/A/A.fsproj" xml)

    [<Fact>]
    let ``malformed project XML refuses`` () =
        refused "project-xml" "src/A/A.fsproj" "<Project><ItemGroup>"

    [<Fact>]
    let ``DTD supplied entity cannot become a project path`` () =
        let xml =
            "<!DOCTYPE Project [<!ENTITY target '../B/B.fsproj'>]>"
            + "<Project><ProjectReference Include='&target;' /></Project>"
        refused "project-xml" "src/A/A.fsproj" xml

    [<Fact>]
    let ``unnormalized project identity refuses before XML reduction`` () =
        refused "project-path" "../A/A.fsproj" "<Project />"

    [<Theory>]
    [<InlineData("../../../outside.fsproj")>]
    [<InlineData("$(OtherProject)")>]
    let ``unresolved or escaping Include refuses`` includePath =
        let xml = "<Project><ProjectReference Include='" + includePath + "' /></Project>"
        refused "project-reference" "src/A/A.fsproj" xml

    [<Theory>]
    [<InlineData("../B/B.fsproj;../C/C.fsproj")>]
    [<InlineData("../B/*.fsproj")>]
    [<InlineData("../B/B?.fsproj")>]
    [<InlineData("../B/B%3BC.fsproj")>]
    let ``MSBuild item expansion syntax cannot become one fabricated graph edge`` includePath =
        let xml = "<Project><ProjectReference Include='" + includePath + "' /></Project>"
        refused "project-reference" "src/A/A.fsproj" xml
