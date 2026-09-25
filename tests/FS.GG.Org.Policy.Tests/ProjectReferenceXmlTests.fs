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

    [<Theory>]
    [<InlineData("<ProjectReference Include='../B/B.fsproj' /><ProjectReference Remove='../B/B.fsproj' />")>]
    [<InlineData("<ProjectReference Remove='../B/B.fsproj' />")>]
    let ``top-level ProjectReference Remove requires item evaluation`` items =
        refused "project-reference" "src/A/A.fsproj" ("<Project><ItemGroup>" + items + "</ItemGroup></Project>")

    [<Fact>]
    let ``unrelated item Remove leaves static ProjectReference readable`` () =
        let xml =
            "<Project><ItemGroup><ProjectReference Include='../B/B.fsproj' />"
            + "<Content Include='generated.txt' /><Content Remove='generated.txt' /></ItemGroup></Project>"
        Assert.Equal<string list>([ "src/B/B.fsproj" ], parsed "src/A/A.fsproj" xml)

    [<Theory>]
    [<InlineData("projectreference")>]
    [<InlineData("PROJECTREFERENCE")>]
    let ``case-varied ProjectReference Include remains a Rule B edge`` itemName =
        let xml = "<Project><ItemGroup><" + itemName
                  + " Include='../B/B.fsproj' /></ItemGroup></Project>"
        let references = parsed "src/A/A.fsproj" xml
        Assert.Equal<string list>([ "src/B/B.fsproj" ], references)
        let graph = Map.empty.Add("src/A/A.fsproj", references)
        match RuleB.inspect [ "src/A/**" ] graph with
        | Error diagnostic -> failwithf "unexpected coverage refusal: %A" diagnostic
        | Ok coverage ->
            Assert.Equal<(string * string) list>(
                [ "src/A/A.fsproj", "src/B/B.fsproj" ], coverage.Uncovered)

    [<Fact>]
    let ``case-varied ProjectReference Remove cannot yield a stale edge`` () =
        let xml =
            "<Project><ItemGroup><ProjectReference Include='../B/B.fsproj' />"
            + "<projectreference Remove='../B/B.fsproj' /></ItemGroup></Project>"
        refused "project-reference" "src/A/A.fsproj" xml

    [<Fact>]
    let ``case-varied target-time ProjectReference requires evaluation`` () =
        let xml =
            "<Project><Target Name='Inject'><ItemGroup>"
            + "<projectreference Include='../B/B.fsproj' />"
            + "</ItemGroup></Target></Project>"
        refused "project-reference" "src/A/A.fsproj" xml

    [<Fact>]
    let ``project targets path override cannot certify nearest implicit source`` () =
        let xml =
            "<Project><PropertyGroup><DirectoryBuildTargetsPath>"
            + "$(MSBuildProjectDirectory)/../../Alternate.targets"
            + "</DirectoryBuildTargetsPath></PropertyGroup></Project>"
        match ProjectReferenceXml.inspect "src/A/A.fsproj" xml with
        | Error diagnostic ->
            Assert.Equal("project-reference", diagnostic.Code)
            Assert.Contains("DirectoryBuildTargetsPath", diagnostic.Message)
        | Ok references -> failwithf "expected targets-path refusal, got %A" references

    [<Fact>]
    let ``targets path item metadata is not a property override`` () =
        let xml =
            "<Project><ItemGroup><Content Include='readme'>"
            + "<DirectoryBuildTargetsPath>Alternate.targets</DirectoryBuildTargetsPath>"
            + "</Content></ItemGroup></Project>"
        Assert.Empty(parsed "src/A/A.fsproj" xml)

    [<Theory>]
    [<InlineData("Directory.Build.props")>]
    [<InlineData("src/A/Directory.Build.targets")>]
    let ``supplied implicit XML with direct reference refuses local observation`` sourcePath =
        let xml = "<Project><ItemGroup><ProjectReference Include='../B/B.fsproj' /></ItemGroup></Project>"
        match ProjectReferenceXml.inspectSuppliedImplicitXml sourcePath xml with
        | Error diagnostic -> Assert.Equal("implicit-project-reference", diagnostic.Code)
        | Ok observation -> failwithf "expected direct-reference refusal, got %A" observation

    [<Fact>]
    let ``case-varied MSBuild item name still denotes ProjectReference`` () =
        let xml = "<Project><ItemGroup><projectreference Include='../B/B.fsproj' /></ItemGroup></Project>"
        match ProjectReferenceXml.inspectSuppliedImplicitXml "Directory.Build.targets" xml with
        | Error diagnostic -> Assert.Equal("implicit-project-reference", diagnostic.Code)
        | Ok observation -> failwithf "expected case-varied item refusal, got %A" observation

    [<Fact>]
    let ``supplied implicit props targets override refuses local observation`` () =
        let xml =
            "<Project><PropertyGroup><directorybuildtargetspath>"
            + "$(MSBuildThisFileDirectory)Alternate.targets"
            + "</directorybuildtargetspath></PropertyGroup></Project>"
        match ProjectReferenceXml.inspectSuppliedImplicitXml "Directory.Build.props" xml with
        | Error diagnostic ->
            Assert.Equal("implicit-source-selection", diagnostic.Code)
            Assert.Contains("DirectoryBuildTargetsPath", diagnostic.Message)
        | Ok observation -> failwithf "expected targets-path refusal, got %A" observation

    [<Fact>]
    let ``supplied implicit XML with an Import refuses unresolved closure`` () =
        let xml = "<Project><Import Project='other.props' /></Project>"
        match ProjectReferenceXml.inspectSuppliedImplicitXml "Directory.Build.props" xml with
        | Error diagnostic -> Assert.Equal("implicit-import", diagnostic.Code)
        | Ok observation -> failwithf "expected import refusal, got %A" observation

    [<Fact>]
    let ``supplied implicit XML refuses task output to ProjectReference`` () =
        let xml =
            "<Project><Target Name='Inject'><CreateItem Include='../B/B.fsproj'>"
            + "<Output TaskParameter='Include' ItemName='ProjectReference' />"
            + "</CreateItem></Target></Project>"
        match ProjectReferenceXml.inspectSuppliedImplicitXml "Directory.Build.targets" xml with
        | Error diagnostic -> Assert.Equal("implicit-task-output", diagnostic.Code)
        | Ok observation -> failwithf "expected task-output refusal, got %A" observation

    [<Theory>]
    [<InlineData("build/Other.props", "<Project />", "implicit-source-path")>]
    [<InlineData("C:/repo/Directory.Build.props", "<Project />", "implicit-source-path")>]
    [<InlineData("Directory.Build.props", "<Project><ItemGroup>", "implicit-source-xml")>]
    let ``supplied implicit XML rejects wrong identity or malformed bytes`` sourcePath xml code =
        match ProjectReferenceXml.inspectSuppliedImplicitXml sourcePath xml with
        | Error diagnostic -> Assert.Equal(code, diagnostic.Code)
        | Ok observation -> failwithf "expected %s refusal, got %A" code observation

    [<Fact>]
    let ``supplied property-only implicit XML gives only a local no-direct-reference observation`` () =
        let xml = "<Project><!-- <ProjectReference Include='../Fake/Fake.fsproj' /> -->"
                  + "<PropertyGroup><Version>1.0</Version></PropertyGroup></Project>"
        match ProjectReferenceXml.inspectSuppliedImplicitXml "Directory.Build.props" xml with
        | Ok ProjectReferenceXml.NoDirectReferenceInSuppliedXml -> ()
        | result -> failwithf "expected local observation, got %A" result

    [<Fact>]
    let ``unrelated target items do not obscure static ProjectReference`` () =
        let xml =
            "<Project><ItemGroup><ProjectReference Include='../B/B.fsproj' /></ItemGroup>"
            + "<Target Name='Generate'><ItemGroup><Content Include='generated.txt' /></ItemGroup></Target></Project>"
        Assert.Equal<string list>([ "src/B/B.fsproj" ], parsed "src/A/A.fsproj" xml)

    [<Theory>]
    [<InlineData("ProjectReference")>]
    [<InlineData("projectreference")>]
    let ``task Output to ProjectReference cannot yield a static graph`` itemName =
        let xml =
            "<Project><Target Name='Inject' BeforeTargets='ResolveProjectReferences'>"
            + "<CreateItem Include='../B/B.fsproj'><Output TaskParameter='Include' ItemName='"
            + itemName + "' /></CreateItem></Target></Project>"
        refused "project-reference" "src/A/A.fsproj" xml

    [<Theory>]
    [<InlineData("$(OutputItem)")>]
    [<InlineData("Project$(Suffix)")>]
    let ``dynamic task Output item names require evaluation`` itemName =
        let xml =
            "<Project><PropertyGroup><OutputItem>ProjectReference</OutputItem><Suffix>Reference</Suffix></PropertyGroup>"
            + "<Target Name='Inject' BeforeTargets='ResolveProjectReferences'>"
            + "<CreateItem Include='../B/B.fsproj'><Output TaskParameter='Include' ItemName='"
            + itemName + "' /></CreateItem></Target></Project>"
        refused "project-reference" "src/A/A.fsproj" xml

    [<Fact>]
    let ``unrelated task Output leaves static references readable`` () =
        let xml =
            "<Project><ItemGroup><ProjectReference Include='../B/B.fsproj' /></ItemGroup>"
            + "<Target Name='Generate'><CreateItem Include='generated.txt'>"
            + "<Output TaskParameter='Include' ItemName='Content' /></CreateItem></Target></Project>"
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
