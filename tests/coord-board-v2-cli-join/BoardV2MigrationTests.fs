namespace FS.GG.Coord.Tests

module BoardV2MigrationTests =
    open System
    open System.IO
    open System.Reflection
    open System.Security.Cryptography
    open System.Text.Json
    open Xunit
    open FS.GG.Coord.Cli
    open FS.GG.Coord.GitHub
    open FS.GG.Coord.GitHub.V2Migration
    open MigrationFixture

    let private get = function Ok value -> value | Error error -> failwith (Errors.explain error)
    let private digest (bytes: byte array) = SHA256.HashData bytes |> Convert.ToHexString |> _.ToLowerInvariant()
    let private actualScope () =
        let adapter = typeof<Scope>.Assembly
        let cli = typeof<FS.GG.Coord.Cli.LintApplication.Summary>.Assembly
        let sha (assembly: Assembly) = File.ReadAllBytes assembly.Location |> digest
        let attribute = adapter.GetCustomAttributes(typeof<AssemblyInformationalVersionAttribute>, false).[0] :?> AssemblyInformationalVersionAttribute
        { scope with
            SourceRevision = (attribute.InformationalVersion.Split('+') |> Array.last)
            ArtifactSha256 = sha adapter
            CliArtifactSha256 = sha cli }
    type private Fixture() =
        let directory = Path.Combine(Path.GetTempPath(), "fsgg-migration-test-" + Guid.NewGuid().ToString("N"))
        do Directory.CreateDirectory(directory, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute) |> ignore
        let mutable scope = actualScope()
        let native = Native snapshot
        let run (arguments: string list) =
            use connection = native.Connection()
            BoardV2Application.runMigrationWithConnection connection arguments
        member _.Directory = directory
        member _.Native = native
        member _.Scope with get() = scope and set value = scope <- value
        member _.Run arguments = run arguments
        member _.Prepare(name: string) =
            let input = Path.Combine(directory, name + "-scope.json")
            let report = Path.Combine(directory, name + "-prepared.json")
            let planned = V2Migration.prepare scope snapshot |> get
            use document = JsonDocument.Parse(V2Migration.encodePrepared planned)
            File.WriteAllText(input, document.RootElement.GetProperty("scope").GetRawText())
            let code = run [ "migration-prepare"; "--scope-file"; input; "--report-file"; report ]
            code, report
        member _.ApplyArguments(path: string) =
            [ "migration-apply"; "--prepared-file"; path; "--prepared-sha256"; digest (File.ReadAllBytes path);
              "--accountable-owner"; "synthetic-accountable-owner"; "--selection-reference"; "synthetic-explicit-operation-selection";
              "--planning-window-until"; DateTimeOffset.UtcNow.AddMinutes(4.0).ToString("O") ]
        interface IDisposable with member _.Dispose() = Directory.Delete(directory, true)

    [<Fact>]
    let ``actual CLI performs exact typed stages including dependent view filter and unchanged repeat writes nothing`` () =
        use fixture = new Fixture()
        let desired : DesiredView =
            { Id = None; Name = "synthetic blocked view"; Layout = "TABLE_LAYOUT"; Filter = "status:Blocked"
              VisibleFieldIds = [ scope.StatusFieldId; scope.RoadmapFieldId ] }
        fixture.Scope <- { fixture.Scope with Views = [ desired ] }
        let code, path = fixture.Prepare "selected"
        Assert.Equal(0, code)
        Assert.Empty fixture.Native.Mutations
        Assert.Equal(0, fixture.Run(fixture.ApplyArguments path))
        Assert.Equal(4, fixture.Native.Mutations.Length)
        Assert.Equal("status:Blocked", fixture.Native.Snapshot.Views.Head.Filter)
        Assert.Single(fixture.Native.Snapshot.Items) |> ignore
        let directory = path + ".invocation"
        for sequence in 1 .. 4 do
            let prefix = Path.Combine(directory, sequence.ToString "D3")
            Assert.True(File.Exists(prefix + "-intent.json"))
            Assert.True(File.Exists(prefix + "-response.json"))
            Assert.True(File.Exists(prefix + "-native-readback.json"))
        let before = fixture.Native.Mutations.Length
        let retained = Directory.GetFiles(directory) |> Array.map (fun path -> Path.GetFileName path, File.ReadAllBytes path) |> Map.ofArray
        Assert.Equal(3, fixture.Run(fixture.ApplyArguments path))
        Assert.Equal(before, fixture.Native.Mutations.Length)
        let afterRefusal = Directory.GetFiles(directory) |> Array.map (fun (path: string) -> Path.GetFileName path) |> Set.ofArray
        Assert.True((retained |> Map.keys |> Set.ofSeq) = afterRefusal)
        for KeyValue(name, bytes) in retained do Assert.Equal<byte>(bytes, File.ReadAllBytes(Path.Combine(directory, name)))
        let repeatCode, repeatPath = fixture.Prepare "reconciled-repeat"
        Assert.Equal(0, repeatCode)
        Assert.Equal(0, fixture.Run(fixture.ApplyArguments repeatPath))
        Assert.Equal(before, fixture.Native.Mutations.Length)

    [<Fact>]
    let ``actual CLI lost response keeps original intent stops later stages and readback never replays`` () =
        use fixture = new Fixture()
        let code, path = fixture.Prepare "uncertain"
        Assert.Equal(0, code)
        fixture.Native.FailAfterEffect <- true
        Assert.Equal(3, fixture.Run(fixture.ApplyArguments path))
        Assert.Single fixture.Native.Mutations |> ignore
        let directory = path + ".invocation"
        let errorPath = Path.Combine(directory, "001-error.json")
        let original = File.ReadAllBytes errorPath
        Assert.True(File.Exists(Path.Combine(directory, "001-intent.json")))
        use terminal = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "terminal.json")))
        Assert.Equal("Unknown", terminal.RootElement.GetProperty("state").GetString())
        Assert.Equal(3, fixture.Run [ "migration-readback"; "--prepared-file"; path ])
        Assert.Equal(3, fixture.Run(fixture.ApplyArguments path))
        Assert.Single fixture.Native.Mutations |> ignore
        Assert.Equal<byte>(original, File.ReadAllBytes errorPath)

    [<Fact>]
    let ``actual CLI refuses digest principal freshness and custody changes before mutation`` () =
        let cases = [ "digest"; "principal"; "expiry"; "custody"; "native-drift" ]
        for name in cases do
            use fixture = new Fixture()
            let code, path = fixture.Prepare name
            Assert.Equal(0, code)
            let arguments = fixture.ApplyArguments path
            let changed =
                match name with
                | "digest" -> arguments |> List.map (fun value -> if value = digest (File.ReadAllBytes path) then String.replicate 64 "0" else value)
                | "principal" ->
                    fixture.Native.Snapshot <- { fixture.Native.Snapshot with PrincipalId = "different-native-principal" }
                    arguments
                | "expiry" -> arguments |> List.mapi (fun index value -> if index = arguments.Length - 1 then DateTimeOffset.UtcNow.AddMinutes(-1.0).ToString("O") else value)
                | "custody" ->
                    File.WriteAllText(path + ".invocation-reserved", "existing original invocation")
                    arguments
                | _ ->
                    let fields = fixture.Native.Snapshot.Fields |> List.map (fun (field: Field) ->
                        if field.Id = scope.StatusFieldId then { field with Options = field.Options |> List.map (fun (option: SelectOption) -> { option with Description = "owner changed descriptor" }) }
                        else field)
                    fixture.Native.Snapshot <- { fixture.Native.Snapshot with Fields = fields }
                    arguments
            Assert.Equal(3, fixture.Run changed)
            Assert.Empty fixture.Native.Mutations

    [<Fact>]
    let ``actual CLI pre-send evidence collision prevents native send and retains first bytes`` () =
        use fixture = new Fixture()
        let code, path = fixture.Prepare "intent-collision"
        Assert.Equal(0, code)
        let mutable collections = 0
        let intent = Path.Combine(path + ".invocation", "001-intent.json")
        fixture.Native.BeforeRead <- fun (request: Transport.Request) ->
            match request.Body with
            | Transport.Query(document, _) when document.Contains "viewer{id login}" ->
                collections <- collections + 1
                if collections = 5 then File.WriteAllText(intent, "first retained original")
            | _ -> ()
        Assert.Equal(3, fixture.Run(fixture.ApplyArguments path))
        Assert.Empty fixture.Native.Mutations
        Assert.Equal("first retained original", File.ReadAllText intent)

    [<Fact>]
    let ``migration grammar rejects approval booleans unknown flags and v4 decoder is not a migration decoder`` () =
        let invalid =
            [ [ "board-v2"; "migration-apply"; "--authorized"; "true" ];
              [ "board-v2"; "migration-prepare"; "--binding-file"; "normal-v4.json"; "--report-file"; "prepared.json" ] ]
        for arguments in invalid do
            match BoardV2Application.validateInvocation arguments with
            | Some(Error _) -> ()
            | _ -> failwith "closed migration grammar should refuse"
        let prepared = V2Migration.prepare scope snapshot |> get |> V2Migration.encodePrepared
        Assert.True(Result.isError (BoardV2Application.parseBinding prepared))

    [<Fact>]
    let ``actual CLI selected seventeen-issue population stays within shared request budget`` () =
        use fixture = new Fixture()
        let selected : SelectedIssue list =
            [ 1 .. 17 ] |> List.map (fun index ->
                { Id = "synthetic-selected-" + string index; Repository = "FS-GG/.github"; Number = 100 + index; Slice = "synthetic-parent-" + string index })
        fixture.Scope <- { fixture.Scope with SelectedIssues = selected }
        fixture.Native.Snapshot <-
            { fixture.Native.Snapshot with Issues = selected |> List.map (fun (issue: SelectedIssue) ->
                { Id = issue.Id; Repository = issue.Repository; Number = issue.Number; UpdatedAt = "2026-10-17T12:00:00Z"; State = "OPEN" }) }
        // Scope-only fixture construction must use the genuine selected identity snapshot too.
        let planned = V2Migration.prepare fixture.Scope fixture.Native.Snapshot |> get
        use document = JsonDocument.Parse(V2Migration.encodePrepared planned)
        let input = Path.Combine(fixture.Directory, "seventeen-scope.json")
        let path = Path.Combine(fixture.Directory, "seventeen-prepared.json")
        File.WriteAllText(input, document.RootElement.GetProperty("scope").GetRawText())
        Assert.Equal(0, fixture.Run [ "migration-prepare"; "--scope-file"; input; "--report-file"; path ])
        Assert.Equal(0, fixture.Run(fixture.ApplyArguments path))
        Assert.Equal(18, fixture.Native.Mutations.Length)
        Assert.Equal(17, fixture.Native.Snapshot.Items.Length)
        Assert.True(fixture.Native.Requests.Length + fixture.Native.Mutations.Length < 500)
        let expectedCollections = fixture.Native.Requests |> List.filter (fun (request: Transport.Request) ->
            match request.Body with Transport.Query(document, _) -> document.Contains "viewer{id login}" | _ -> false) |> List.length |> fun count -> count / 2
        Assert.Equal(expectedCollections * 6, fixture.Native.Requests.Length)

    [<Fact>]
    let ``actual CLI incomplete nested inventory and malformed mutation payload never become empty success`` () =
        use fixture = new Fixture()
        let card : Item =
            { Id = "synthetic-existing-unselected-card"; ContentId = "synthetic-unselected-issue"; ContentKind = "Issue"
              Repository = Some "FS-GG/.github"; Number = Some 99; UpdatedAt = "2026-10-17T12:00:00Z"; Archived = false; Values = [] }
        fixture.Native.Snapshot <- { fixture.Native.Snapshot with Items = [ card ] }
        fixture.Native.Transform <- fun (request: Transport.Request) (response: Transport.Response) ->
            match request.Body with
            | Transport.Query(document, _) when document.Contains "items(first:" ->
                let root = System.Text.Json.Nodes.JsonNode.Parse response.Body
                let data = root.["data"]
                let project = data.["node"]
                let inventory = project.["items"]
                let items = inventory.["nodes"].AsArray()
                let fields = items.[0].["fieldValues"]
                let pageInfo = fields.["pageInfo"]
                pageInfo.["hasNextPage"] <- System.Text.Json.Nodes.JsonValue.Create true
                pageInfo.["endCursor"] <- System.Text.Json.Nodes.JsonValue.Create "synthetic-tail"
                Ok { response with Body = root.ToJsonString() }
            | _ -> Ok response
        let code, _ = fixture.Prepare "incomplete"
        Assert.Equal(3, code)
        Assert.Empty fixture.Native.Mutations
        use malformed = new Fixture()
        let ready, path = malformed.Prepare "null-payload"
        Assert.Equal(0, ready)
        malformed.Native.TransformDispatch <- fun (_: Transport.Request) (response: Transport.Response) ->
            Ok { response with Body = "{\"data\":{\"updateProjectV2Field\":null}}" }
        Assert.Equal(3, malformed.Run(malformed.ApplyArguments path))
        Assert.Single malformed.Native.Mutations |> ignore
        Assert.True(File.Exists(Path.Combine(path + ".invocation", "001-response.json")))
        Assert.True(File.Exists(Path.Combine(path + ".invocation", "001-native-readback.json")))

    [<Fact>]
    let ``actual CLI mismatched returned identity missing echo and partial errors stop dependent writes`` () =
        let replies : (string * string) list =
            [ ("wrong-native-result", "{\"data\":{\"updateProjectV2Field\":{\"clientMutationId\":null,\"projectV2Field\":{\"id\":\"wrong\"}}}}");
              ("missing-echo", "{\"data\":{\"updateProjectV2Field\":{\"projectV2Field\":{\"id\":\"synthetic-status\"}}}}");
              ("partial", "{\"data\":{\"updateProjectV2Field\":null},\"errors\":[{\"message\":\"synthetic private diagnostic\"}]}") ]
        for name, reply in replies do
            use fixture = new Fixture()
            let code, path = fixture.Prepare name
            Assert.Equal(0, code)
            let mutable exactReply = reply
            fixture.Native.TransformDispatch <- fun (_: Transport.Request) (response: Transport.Response) ->
                if name = "wrong-native-result" then
                    let root = System.Text.Json.Nodes.JsonNode.Parse response.Body
                    let data = root.["data"]
                    let payload = data.["updateProjectV2Field"]
                    let field = payload.["projectV2Field"]
                    field.["id"] <- System.Text.Json.Nodes.JsonValue.Create "wrong-native-result"
                    exactReply <- root.ToJsonString()
                Ok { response with Body = exactReply }
            Assert.Equal(3, fixture.Run(fixture.ApplyArguments path))
            Assert.Single fixture.Native.Mutations |> ignore
            Assert.Equal(exactReply, File.ReadAllText(Path.Combine(path + ".invocation", "001-response.json")))
            Assert.True(File.Exists(Path.Combine(path + ".invocation", "001-native-readback.json")))

    [<Fact>]
    let ``actual CLI request ceiling preserves partial original invocation without replay`` () =
        use fixture = new Fixture()
        let selected : SelectedIssue list =
            [ 1 .. 100 ] |> List.map (fun index ->
                { Id = "synthetic-bounded-" + string index; Repository = "FS-GG/.github"; Number = 200 + index; Slice = "synthetic-slice-" + string index })
        fixture.Scope <- { fixture.Scope with SelectedIssues = selected }
        fixture.Native.Snapshot <-
            { fixture.Native.Snapshot with Issues = selected |> List.map (fun (issue: SelectedIssue) ->
                { Id = issue.Id; Repository = issue.Repository; Number = issue.Number; UpdatedAt = "2026-10-17T12:00:00Z"; State = "OPEN" }) }
        let planned = V2Migration.prepare fixture.Scope fixture.Native.Snapshot |> get
        use document = JsonDocument.Parse(V2Migration.encodePrepared planned)
        let input = Path.Combine(fixture.Directory, "bounded-scope.json")
        let path = Path.Combine(fixture.Directory, "bounded-prepared.json")
        File.WriteAllText(input, document.RootElement.GetProperty("scope").GetRawText())
        Assert.Equal(0, fixture.Run [ "migration-prepare"; "--scope-file"; input; "--report-file"; path ])
        let beforeReads = fixture.Native.Requests.Length
        Assert.Equal(3, fixture.Run(fixture.ApplyArguments path))
        let effects = fixture.Native.Mutations.Length
        Assert.True(effects > 0 && effects < 101)
        Assert.Equal(500, fixture.Native.Requests.Length - beforeReads + effects)
        Assert.True(File.Exists(Path.Combine(path + ".invocation", "terminal.json")))
        Assert.Equal(3, fixture.Run(fixture.ApplyArguments path))
        Assert.Equal(effects, fixture.Native.Mutations.Length)

    [<Fact>]
    let ``actual read-only preparation records missing native capability while apply still refuses`` () =
        use fixture = new Fixture()
        fixture.Native.Snapshot <- { fixture.Native.Snapshot with CanUpdate = false }
        let code, path = fixture.Prepare "read-only-capability"
        Assert.Equal(0, code)
        Assert.Equal(3, fixture.Run(fixture.ApplyArguments path))
        Assert.Empty fixture.Native.Mutations
        use report = JsonDocument.Parse(File.ReadAllText path)
        Assert.Contains("lacks project update capability", report.RootElement.GetProperty("ownerActions").GetRawText())

    [<Fact>]
    let ``actual dependent view filter refuses an intervening owner edit after create readback`` () =
        use fixture = new Fixture()
        let desired : DesiredView =
            { Id = None; Name = "synthetic owner view"; Layout = "TABLE_LAYOUT"; Filter = "status:Blocked"
              VisibleFieldIds = [ scope.StatusFieldId; scope.RoadmapFieldId ] }
        fixture.Scope <- { fixture.Scope with Views = [ desired ] }
        let code, path = fixture.Prepare "dependent-owner-drift"
        Assert.Equal(0, code)
        let mutable postCreateMetas = 0
        fixture.Native.BeforeRead <- fun (request: Transport.Request) ->
            match request.Body with
            | Transport.Query(document, _) when document.Contains "viewer{id login}" && fixture.Native.Mutations.Length = 3 ->
                postCreateMetas <- postCreateMetas + 1
                if postCreateMetas = 3 then
                    fixture.Native.Snapshot <- { fixture.Native.Snapshot with Views = fixture.Native.Snapshot.Views |> List.map (fun (view: View) -> { view with Filter = "owner-selected-filter" }) }
            | _ -> ()
        Assert.Equal(3, fixture.Run(fixture.ApplyArguments path))
        Assert.Equal(3, fixture.Native.Mutations.Length)
        Assert.Equal("owner-selected-filter", fixture.Native.Snapshot.Views.Head.Filter)
        Assert.False(File.Exists(Path.Combine(path + ".invocation", "004-intent.json")))
