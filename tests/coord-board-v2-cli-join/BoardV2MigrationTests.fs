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
    let private actualScope () : Scope =
        let adapter = typeof<Scope>.Assembly
        let cli = typeof<FS.GG.Coord.Cli.LintApplication.Summary>.Assembly
        let sha (assembly: Assembly) = File.ReadAllBytes assembly.Location |> digest
        let attribute = adapter.GetCustomAttributes(typeof<AssemblyInformationalVersionAttribute>, false).[0] :?> AssemblyInformationalVersionAttribute
        { scope with
            SourceRevision = (attribute.InformationalVersion.Split('+') |> Array.last);
            ArtifactSha256 = sha adapter;
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
        member _.RunWithCustody(syncDirectory: string -> unit, clock: TimeProvider, arguments: string list) =
            use connection = native.Connection()
            BoardV2Application.runMigrationWithCustody connection syncDirectory clock arguments
        member _.Prepare(name: string) =
            let input = Path.Combine(directory, name + "-scope.json")
            let report = Path.Combine(directory, name + "-prepared.json")
            let planned = V2Migration.prepare scope native.Snapshot |> get
            use document = JsonDocument.Parse(V2Migration.encodePrepared ({ planned with PreparedPath = Some report }))
            File.WriteAllText(input, document.RootElement.GetProperty("scope").GetRawText())
            let code = run [ "migration-prepare"; "--scope-file"; input; "--report-file"; report ]
            code, report
        member _.ApplyArguments(path: string) =
            [ "migration-apply"; "--prepared-file"; path; "--prepared-sha256"; digest (File.ReadAllBytes path);
              "--accountable-owner"; "synthetic-accountable-owner"; "--selection-reference"; "synthetic-explicit-operation-selection";
              "--planning-window-until"; DateTimeOffset.UtcNow.AddMinutes(4.0).ToString("O") ]
        interface IDisposable with member _.Dispose() = Directory.Delete(directory, true)

    type private ManualTime(initial: DateTimeOffset) =
        inherit TimeProvider()
        let mutable now = initial
        override _.GetUtcNow() = now
        member _.Advance(duration: TimeSpan) = now <- now + duration

    [<Fact>]
    let ``actual CLI performs exact typed stages including dependent view filter and unchanged repeat writes nothing`` () =
        use fixture = new Fixture()
        let desired : DesiredView =
            { Id = None; Name = "synthetic blocked view"; Layout = "TABLE_LAYOUT"; Filter = "status:Blocked";
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
        let retained = Directory.GetFiles(directory) |> Array.map (fun (path: string) -> Path.GetFileName path, File.ReadAllBytes path) |> Map.ofArray
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
                    let fields =
                        fixture.Native.Snapshot.Fields
                        |> List.map (fun (field: Field) ->
                            if field.Id = scope.StatusFieldId then { field with Options = (field.Options |> List.map (fun (option: SelectOption) -> { option with Description = "owner changed descriptor" })) }
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
        let planned = V2Migration.prepare scope snapshot |> get
        let prepared = V2Migration.encodePrepared ({ planned with PreparedPath = Some(Path.GetFullPath "synthetic-prepared.json") })
        Assert.True(Result.isError (BoardV2Application.parseBinding prepared))

    let private activeOption: PlanningOption = { Id = "synthetic-active"; Name = "Active delivery" }
    let private planningSchema (snapshot: Snapshot) : Snapshot =
        let fields: Field list =
            snapshot.Fields |> List.map (fun (field: Field) ->
                if field.Id = scope.TrackFieldId then
                    { field with Options = [ { Id = activeOption.Id; Name = activeOption.Name; Color = "BLUE"; Description = "synthetic track" } ] }
                else field)
        { snapshot with Fields = fields }
    let private declaration (issueId: string) (statusName: string) : InitialPlanning =
        let native = snapshot.Fields.Head.Options |> List.find (fun (option: SelectOption) -> option.Name = statusName)
        { IssueId = issueId; Track = activeOption;
          Roadmap = "https://github.com/FS-GG/.github/blob/main/docs/coordination/synthetic.md";
          Status = Some { Id = native.Id; Name = native.Name } }

    [<Fact>]
    let ``actual CLI initializes only absent planning values after native membership and repeats without writes`` () =
        use fixture = new Fixture()
        fixture.Native.Snapshot <- planningSchema fixture.Native.Snapshot
        fixture.Scope <- { fixture.Scope with InitialPlanning = [ declaration "synthetic-issue" "Blocked" ] }
        let code, path = fixture.Prepare "initial-planning"
        Assert.Equal(0, code)
        Assert.Equal(0, fixture.Run(fixture.ApplyArguments path))
        Assert.Equal(5, fixture.Native.Mutations.Length)
        let item = Assert.Single fixture.Native.Snapshot.Items
        Assert.Equal(3, item.Values.Length)
        Assert.DoesNotContain(item.Values, fun (value: ItemValue) -> value.FieldId = scope.ObservationFieldId)
        for request in fixture.Native.Mutations |> List.skip 2 do
            match request.Body with
            | Transport.Json json ->
                use document = JsonDocument.Parse json
                let root = document.RootElement
                Assert.Contains("projectV2Item{id}", root.GetProperty("query").GetString())
                let input = root.GetProperty("variables").GetProperty "input"
                Assert.Equal(item.Id, input.GetProperty("itemId").GetString())
                Assert.Single(input.GetProperty("value").EnumerateObject()) |> ignore
            | _ -> failwith "exact typed planning request expected"
        let ready, repeated = fixture.Prepare "equal-planning-repeat"
        Assert.Equal(0, ready)
        Assert.Equal(0, fixture.Run(fixture.ApplyArguments repeated))
        Assert.Equal(5, fixture.Native.Mutations.Length)

    [<Fact>]
    let ``actual CLI automation value after membership conflicts and is preserved without initialization`` () =
        use fixture = new Fixture()
        fixture.Native.Snapshot <- planningSchema fixture.Native.Snapshot
        fixture.Scope <- { fixture.Scope with InitialPlanning = [ declaration "synthetic-issue" "Blocked" ] }
        let code, path = fixture.Prepare "automation-default"
        Assert.Equal(0, code)
        fixture.Native.BeforeRead <- fun (_: Transport.Request) ->
            if fixture.Native.Mutations.Length = 2 && not fixture.Native.Snapshot.Items.IsEmpty then
                let card = fixture.Native.Snapshot.Items.Head
                if card.Values.IsEmpty then
                    let option = snapshot.Fields.Head.Options.Head
                    let value: ItemValue =
                        { Id = scope.StatusFieldId; FieldId = scope.StatusFieldId; Kind = "ProjectV2ItemFieldSingleSelectValue";
                          Value = JsonSerializer.Serialize([ "id"; "\"synthetic-automation\""; "updatedAt"; "\"2026-10-17T13:00:00Z\"";
                                                             "optionId"; JsonSerializer.Serialize(option.Id); "name"; JsonSerializer.Serialize(option.Name) ]) }
                    fixture.Native.Snapshot <- { fixture.Native.Snapshot with Items = [ { card with Values = [ value ] } ] }
        Assert.Equal(3, fixture.Run(fixture.ApplyArguments path))
        Assert.Equal(2, fixture.Native.Mutations.Length)
        Assert.Equal("ProjectV2ItemFieldSingleSelectValue", fixture.Native.Snapshot.Items.Head.Values.Head.Kind)
        Assert.True(File.Exists(path + ".invocation/002-native-readback.json"))
        Assert.False(File.Exists(path + ".invocation/003-intent.json"))

    [<Fact>]
    let ``actual twenty-issue twenty-two-item eight-view planning journey stays below unchanged request cap`` () =
        use fixture = new Fixture()
        let selected: SelectedIssue list =
            [ 1 .. 20 ] |> List.map (fun index ->
                { Id = "synthetic-population-" + string index; Repository = "FS-GG/.github";
                  Number = 100 + index; Slice = "synthetic-slice-" + string index })
        let issues: NativeIssue list =
            selected |> List.map (fun (issue: SelectedIssue) ->
                { Id = issue.Id; Repository = issue.Repository; Number = issue.Number; UpdatedAt = "2026-10-17T12:00:00Z"; State = "OPEN" })
        let item (index: int) : Item =
            let retained (fieldId: string) (kind: string) (tokens: string list) : ItemValue =
                { Id = fieldId; FieldId = fieldId; Kind = kind;
                  Value = JsonSerializer.Serialize([ "id"; JsonSerializer.Serialize("synthetic-retained-" + string index + fieldId);
                                                     "updatedAt"; JsonSerializer.Serialize("2026-10-17T12:00:00Z") ] @ tokens) }
            let values: ItemValue list =
                if index <= 3 then
                    [ retained scope.StatusFieldId "ProjectV2ItemFieldSingleSelectValue"
                          [ "optionId"; JsonSerializer.Serialize(snapshot.Fields.Head.Options.Head.Id); "name"; "\"Backlog\"" ];
                      retained scope.TrackFieldId "ProjectV2ItemFieldSingleSelectValue"
                          [ "optionId"; JsonSerializer.Serialize(activeOption.Id); "name"; JsonSerializer.Serialize(activeOption.Name) ];
                      retained scope.RoadmapFieldId "ProjectV2ItemFieldTextValue" [ "text"; "\"Synthetic retained pilot plan\"" ] ]
                else [ retained scope.RoadmapFieldId "ProjectV2ItemFieldTextValue" [ "text"; "\"Synthetic unselected plan\"" ] ]
            { Id = "synthetic-retained-item-" + string index; ContentId = "synthetic-population-" + string index;
              ContentKind = "Issue"; Repository = Some "FS-GG/.github"; Number = Some(100 + index);
              UpdatedAt = "2026-10-17T12:00:00Z"; Archived = false; Values = values }
        let originalItems = [ item 1; item 2; item 3; item 21; item 22 ]
        let view (index: int) : View =
            { Id = "synthetic-retained-view-" + string index; Name = "synthetic retained " + string index;
              Layout = "TABLE_LAYOUT"; Filter = ""; VisibleFieldIds = [ scope.StatusFieldId ]; UnsupportedConfiguration = "[]" }
        let originalViews = [ 1 .. 5 ] |> List.map view
        let extraFields: Field list =
            [ 1 .. 12 ] |> List.map (fun index ->
                { Id = "synthetic-extra-field-" + string index; Name = "Synthetic extra " + string index; DataType = "TEXT"; Options = [] })
        let native = planningSchema fixture.Native.Snapshot
        fixture.Native.Snapshot <- { native with Issues = issues; Items = originalItems; Views = originalViews; Fields = native.Fields @ extraFields }
        let existing: DesiredView list =
            originalViews |> List.take 3 |> List.map (fun (view: View) ->
                { Id = Some view.Id; Name = view.Name; Layout = view.Layout; Filter = "status:Blocked"; VisibleFieldIds = view.VisibleFieldIds })
        let created: DesiredView list =
            [ 1 .. 3 ] |> List.map (fun index ->
                { Id = None; Name = "synthetic created " + string index; Layout = "TABLE_LAYOUT";
                  Filter = (if index < 3 then "status:Blocked" else ""); VisibleFieldIds = [ scope.StatusFieldId ] })
        let initial: InitialPlanning list =
            selected |> List.skip 3 |> List.mapi (fun index (issue: SelectedIssue) ->
                declaration issue.Id (if index < 5 then "Backlog" elif index < 9 then "In progress" else "Blocked"))
        fixture.Scope <- { fixture.Scope with SelectedIssues = selected; InitialPlanning = initial; Views = existing @ created }
        let code, path = fixture.Prepare "full-synthetic-journey"
        Assert.Equal(0, code)
        Assert.Equal(0, fixture.Run(fixture.ApplyArguments path))
        Assert.Equal(77, fixture.Native.Mutations.Length)
        Assert.Equal(22, fixture.Native.Snapshot.Items.Length)
        Assert.Equal(8, fixture.Native.Snapshot.Views.Length)
        Assert.Equal(16, fixture.Native.Snapshot.Fields.Length)
        Assert.True(fixture.Native.Requests.Length + fixture.Native.Mutations.Length < 500)
        for retained in originalItems do Assert.Contains(retained, fixture.Native.Snapshot.Items)
        Assert.Equal(62, fixture.Native.Snapshot.Items |> List.sumBy (fun (item: Item) -> item.Values.Length))
        let before = fixture.Native.Mutations.Length
        let repeatCode, repeatPath = fixture.Prepare "full-synthetic-repeat"
        Assert.Equal(0, repeatCode)
        Assert.Equal(0, fixture.Run(fixture.ApplyArguments repeatPath))
        Assert.Equal(before, fixture.Native.Mutations.Length)

    [<Fact>]
    let ``actual caller drains bundled top-level continuations under the same collection budget`` () =
        for name in [ "fields"; "items"; "views" ] do
            use fixture = new Fixture()
            let card (index: int) : Item =
                { Id = "synthetic-unselected-item-" + string index; ContentId = "synthetic-unselected-content-" + string index;
                  ContentKind = "Issue"; Repository = Some "FS-GG/.github"; Number = Some(900 + index);
                  UpdatedAt = "2026-10-17T12:00:00Z"; Archived = false; Values = [] }
            let view (index: int) : View =
                { Id = "synthetic-existing-" + string index; Name = "synthetic existing " + string index;
                  Layout = "TABLE_LAYOUT"; Filter = ""; VisibleFieldIds = [ scope.StatusFieldId ]; UnsupportedConfiguration = "[]" }
            fixture.Native.Snapshot <- { fixture.Native.Snapshot with Items = [ card 1; card 2 ]; Views = [ view 1; view 2 ] }
            let mutable continuations = 0
            fixture.Native.Transform <- fun (request: Transport.Request) (response: Transport.Response) ->
                match request.Body with
                | Transport.Query(document, variables) when document.Contains(name + "(first:") ->
                    let continued = variables |> List.exists (function "after", VString _ -> true | _ -> false)
                    if continued then continuations <- continuations + 1
                    let root = System.Text.Json.Nodes.JsonNode.Parse response.Body
                    let data = root.["data"]
                    let parent = if isNull data.["node"] then (data.["organization"]).["projectV2"] else data.["node"]
                    let connection = parent.[name]
                    let nodes = connection.["nodes"].AsArray()
                    let retained = nodes |> Seq.skip (if continued then 1 else 0) |> Seq.take (if continued then nodes.Count - 1 else 1) |> Seq.map (fun (node: System.Text.Json.Nodes.JsonNode) -> node.DeepClone()) |> Seq.toArray
                    nodes.Clear()
                    for node in retained do nodes.Add node
                    let page = connection.["pageInfo"]
                    page.["hasNextPage"] <- System.Text.Json.Nodes.JsonValue.Create(not continued)
                    let cursor: System.Text.Json.Nodes.JsonNode =
                        if continued then null else System.Text.Json.Nodes.JsonValue.Create("synthetic-next")
                    page.["endCursor"] <- cursor
                    Ok ({ response with Body = root.ToJsonString() })
                | _ -> Ok response
            let code, path = fixture.Prepare ("complete-" + name)
            Assert.Equal(0, code)
            Assert.Equal(2, continuations)
            Assert.Equal(6, fixture.Native.Requests.Length)
            Assert.Empty fixture.Native.Mutations
            use report = JsonDocument.Parse(File.ReadAllText path)
            Assert.False(String.IsNullOrWhiteSpace(report.RootElement.GetProperty("digest").GetString()))

    [<Fact>]
    let ``actual planning write uncertain response retains original identity and prohibits later writes`` () =
        for failure in [ "lost"; "wrong-item"; "partial-errors" ] do
            use fixture = new Fixture()
            fixture.Native.Snapshot <- planningSchema fixture.Native.Snapshot
            fixture.Scope <- { fixture.Scope with InitialPlanning = [ declaration "synthetic-issue" "Blocked" ] }
            let code, path = fixture.Prepare ("uncertain-planning-" + failure)
            Assert.Equal(0, code)
            fixture.Native.TransformDispatch <- fun (request: Transport.Request) (response: Transport.Response) ->
                match request.Body with
                | Transport.Json json when json.Contains("updateProjectV2ItemFieldValue") ->
                    if failure = "lost" then Error(Errors.Transport "synthetic response deadline after possible planning send")
                    elif failure = "wrong-item" then Ok { response with Body = response.Body.Replace("synthetic-item-synthetic-issue", "synthetic-other-item") }
                    else Ok { response with Body = "{\"data\":{\"updateProjectV2ItemFieldValue\":null},\"errors\":[{\"message\":\"synthetic partial response\"}]}" }
                | _ -> Ok response
            Assert.Equal(3, fixture.Run(fixture.ApplyArguments path))
            Assert.Equal(3, fixture.Native.Mutations.Length)
            let prefix = path + ".invocation/003"
            Assert.True(File.Exists(prefix + "-intent.json"))
            Assert.True(File.Exists(prefix + "-native-readback.json"))
            Assert.False(File.Exists(path + ".invocation/004-intent.json"))
            Assert.Equal(3, fixture.Run(fixture.ApplyArguments path))
            Assert.Equal(3, fixture.Native.Mutations.Length)

    [<Fact>]
    let ``actual CLI selected seventeen-issue population stays within shared request budget`` () =
        use fixture = new Fixture()
        let selected : SelectedIssue list =
            [ 1 .. 17 ] |> List.map (fun index ->
                { Id = "synthetic-selected-" + string index; Repository = "FS-GG/.github"; Number = 100 + index; Slice = "synthetic-parent-" + string index })
        fixture.Scope <- { fixture.Scope with SelectedIssues = selected }
        let issues: NativeIssue list =
            selected
            |> List.map (fun (issue: SelectedIssue) ->
                { Id = issue.Id; Repository = issue.Repository; Number = issue.Number;
                  UpdatedAt = "2026-10-17T12:00:00Z"; State = "OPEN" })
        fixture.Native.Snapshot <- { fixture.Native.Snapshot with Issues = issues }
        // Scope-only fixture construction must use the genuine selected identity snapshot too.
        let planned = V2Migration.prepare fixture.Scope fixture.Native.Snapshot |> get
        let input = Path.Combine(fixture.Directory, "seventeen-scope.json")
        let path = Path.Combine(fixture.Directory, "seventeen-prepared.json")
        use document = JsonDocument.Parse(V2Migration.encodePrepared ({ planned with PreparedPath = Some path }))
        File.WriteAllText(input, document.RootElement.GetProperty("scope").GetRawText())
        Assert.Equal(0, fixture.Run [ "migration-prepare"; "--scope-file"; input; "--report-file"; path ])
        Assert.Equal(0, fixture.Run(fixture.ApplyArguments path))
        Assert.Equal(18, fixture.Native.Mutations.Length)
        Assert.Equal(17, fixture.Native.Snapshot.Items.Length)
        Assert.True(fixture.Native.Requests.Length + fixture.Native.Mutations.Length < 500)
        let expectedCollections =
            fixture.Native.Requests
            |> List.filter (fun (request: Transport.Request) ->
                match request.Body with Transport.Query(document, _) -> document.Contains "viewer{id login}" | _ -> false)
            |> List.length
            |> fun count -> count / 2
        Assert.Equal(expectedCollections * 2, fixture.Native.Requests.Length)

    [<Fact>]
    let ``actual CLI incomplete nested inventory and malformed mutation payload never become empty success`` () =
        use fixture = new Fixture()
        let card : Item =
            { Id = "synthetic-existing-unselected-card"; ContentId = "synthetic-unselected-issue"; ContentKind = "Issue";
              Repository = Some "FS-GG/.github"; Number = Some 99; UpdatedAt = "2026-10-17T12:00:00Z"; Archived = false; Values = [] }
        fixture.Native.Snapshot <- { fixture.Native.Snapshot with Items = [ card ] }
        fixture.Native.Transform <- fun (request: Transport.Request) (response: Transport.Response) ->
            match request.Body with
            | Transport.Query(document, _) when document.Contains "items(first:" ->
                let root = System.Text.Json.Nodes.JsonNode.Parse response.Body
                let data = root.["data"]
                let project =
                    if isNull data.["node"] then (data.["organization"]).["projectV2"] else data.["node"]
                let inventory = project.["items"]
                let items = inventory.["nodes"].AsArray()
                let fields = items.[0].["fieldValues"]
                let pageInfo = fields.["pageInfo"]
                pageInfo.["hasNextPage"] <- System.Text.Json.Nodes.JsonValue.Create true
                pageInfo.["endCursor"] <- System.Text.Json.Nodes.JsonValue.Create "synthetic-tail"
                Ok ({ response with Body = root.ToJsonString() })
            | _ -> Ok response
        let code, _ = fixture.Prepare "incomplete"
        Assert.Equal(3, code)
        Assert.Empty fixture.Native.Mutations
        use malformed = new Fixture()
        let ready, path = malformed.Prepare "null-payload"
        Assert.Equal(0, ready)
        malformed.Native.TransformDispatch <- fun (_: Transport.Request) (response: Transport.Response) ->
            Ok ({ response with Body = "{\"data\":{\"updateProjectV2Field\":null}}" })
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
                Ok ({ response with Body = exactReply })
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
        let issues: NativeIssue list =
            selected
            |> List.map (fun (issue: SelectedIssue) ->
                { Id = issue.Id; Repository = issue.Repository; Number = issue.Number;
                  UpdatedAt = "2026-10-17T12:00:00Z"; State = "OPEN" })
        fixture.Native.Snapshot <- { fixture.Native.Snapshot with Issues = issues }
        let planned = V2Migration.prepare fixture.Scope fixture.Native.Snapshot |> get
        let input = Path.Combine(fixture.Directory, "bounded-scope.json")
        let path = Path.Combine(fixture.Directory, "bounded-prepared.json")
        use document = JsonDocument.Parse(V2Migration.encodePrepared ({ planned with PreparedPath = Some path }))
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
            { Id = None; Name = "synthetic owner view"; Layout = "TABLE_LAYOUT"; Filter = "status:Blocked";
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
                    fixture.Native.Snapshot <- { fixture.Native.Snapshot with Views = (fixture.Native.Snapshot.Views |> List.map (fun (view: View) -> { view with Filter = "owner-selected-filter" })) }
            | _ -> ()
        Assert.Equal(3, fixture.Run(fixture.ApplyArguments path))
        Assert.Equal(3, fixture.Native.Mutations.Length)
        Assert.Equal("owner-selected-filter", fixture.Native.Snapshot.Views.Head.Filter)
        Assert.False(File.Exists(Path.Combine(path + ".invocation", "004-intent.json")))

    [<Fact>]
    let ``actual CLI refuses copied prepared bytes without escaping original uncertain custody`` () =
        use fixture = new Fixture()
        let code, path = fixture.Prepare "bound-original"
        Assert.Equal(0, code)
        fixture.Native.FailAfterEffect <- true
        Assert.Equal(3, fixture.Run(fixture.ApplyArguments path))
        let directory = path + ".invocation"
        let original = Directory.GetFiles(directory) |> Array.map (fun (file: string) -> Path.GetFileName file, File.ReadAllBytes file) |> Map.ofArray
        let reads = fixture.Native.Requests.Length
        let effects = fixture.Native.Mutations.Length
        let copy = Path.Combine(fixture.Directory, "copied-identical-prepared.json")
        File.Copy(path, copy)
        Assert.Equal<byte>(File.ReadAllBytes path, File.ReadAllBytes copy)
        Assert.Equal(3, fixture.Run(fixture.ApplyArguments copy))
        Assert.Equal(reads, fixture.Native.Requests.Length)
        Assert.Equal(effects, fixture.Native.Mutations.Length)
        Assert.False(File.Exists(copy + ".invocation-reserved"))
        Assert.False(Directory.Exists(copy + ".invocation"))
        Assert.True((original |> Map.keys |> Set.ofSeq) = (Directory.GetFiles(directory) |> Seq.map (fun (file: string) -> Path.GetFileName file) |> Set.ofSeq))
        for KeyValue(name, bytes) in original do Assert.Equal<byte>(bytes, File.ReadAllBytes(Path.Combine(directory, name)))

    [<Fact>]
    let ``actual CLI expiry during intent directory barrier prevents dispatch`` () =
        use fixture = new Fixture()
        let code, path = fixture.Prepare "custody-expiry"
        Assert.Equal(0, code)
        let clock = ManualTime(DateTimeOffset.UtcNow)
        let metadata = Path.Combine(path + ".invocation", "001-intent-metadata.json")
        let mutable advanced = false
        let syncDirectory (_: string) =
            if File.Exists metadata && not advanced then
                advanced <- true
                clock.Advance(TimeSpan.FromMinutes 6.0)
        Assert.Equal(3, fixture.RunWithCustody(syncDirectory, clock, fixture.ApplyArguments path))
        Assert.True advanced
        Assert.True(File.Exists metadata)
        Assert.Empty fixture.Native.Mutations

    [<Fact>]
    let ``actual CLI directory durability failures refuse before any possible send`` () =
        for stage in [ "reservation-parent"; "new-directory"; "intent-directory" ] do
            use fixture = new Fixture()
            let code, path = fixture.Prepare stage
            Assert.Equal(0, code)
            let directory = path + ".invocation"
            let mutable refused = false
            let syncDirectory (target: string) =
                let selected =
                    match stage with
                    | "reservation-parent" -> File.Exists(path + ".invocation-reserved") && not (Directory.Exists directory)
                    | "new-directory" -> target = directory && not (File.Exists(Path.Combine(directory, "prepared.json")))
                    | _ -> target = directory && File.Exists(Path.Combine(directory, "001-intent.json"))
                if selected then
                    refused <- true
                    raise (IOException "synthetic unsupported directory barrier")
            Assert.Equal(3, fixture.RunWithCustody(syncDirectory, TimeProvider.System, fixture.ApplyArguments path))
            Assert.True(refused, stage)
            Assert.Empty fixture.Native.Mutations

    [<Fact>]
    let ``actual CLI acknowledges reservation directory and exact intent barriers before provider send`` () =
        use fixture = new Fixture()
        let code, path = fixture.Prepare "barrier-order"
        Assert.Equal(0, code)
        let directory = path + ".invocation"
        let mutable reservationParent = false
        let mutable newDirectory = false
        let mutable newDirectoryParent = false
        let metadataBarriers = System.Collections.Generic.HashSet<string>()
        let syncDirectory (target: string) =
            if File.Exists(path + ".invocation-reserved") && not (Directory.Exists directory) then reservationParent <- true
            if target = directory then newDirectory <- true
            if target = fixture.Directory && Directory.Exists directory then newDirectoryParent <- true
            if target = directory then
                for file in Directory.GetFiles(directory, "*-intent-metadata.json") do metadataBarriers.Add(Path.GetFileName file) |> ignore
        fixture.Native.BeforeDispatch <- fun (_: Transport.Request) ->
            Assert.True reservationParent
            Assert.True newDirectory
            Assert.True newDirectoryParent
            let metadata = fixture.Native.Mutations.Length.ToString("D3") + "-intent-metadata.json"
            Assert.True(metadataBarriers.Contains metadata)
        Assert.Equal(0, fixture.RunWithCustody(syncDirectory, TimeProvider.System, fixture.ApplyArguments path))
        Assert.Equal(2, fixture.Native.Mutations.Length)

    [<Fact>]
    let ``actual CLI duplicate explicit native view IDs refuse before reads and preserve existing view`` () =
        use fixture = new Fixture()
        let existing : View =
            { Id = "synthetic-existing-view"; Name = "original owner view"; Layout = "TABLE_LAYOUT"; Filter = "owner-filter";
              VisibleFieldIds = [ scope.StatusFieldId ]; UnsupportedConfiguration = "[]" }
        fixture.Native.Snapshot <- { fixture.Native.Snapshot with Views = [ existing ] }
        let scopeFile = Path.Combine(fixture.Directory, "duplicate-view-id-scope.json")
        let reportPath = Path.Combine(fixture.Directory, "duplicate-view-id-prepared.json")
        let prepared = V2Migration.prepare fixture.Scope snapshot |> get
        let root = System.Text.Json.Nodes.JsonNode.Parse(V2Migration.encodePrepared ({ prepared with PreparedPath = Some reportPath }))
        let scopeNode = root.["scope"]
        let views = scopeNode.["views"].AsArray()
        let item (name: string) =
            let desired =
                {| id = "synthetic-existing-view";
                   name = name;
                   layout = "TABLE_LAYOUT";
                   filter = "";
                   visibleFieldIds = [ scope.StatusFieldId ] |}
            System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(desired))
        views.Add(item "synthetic one")
        views.Add(item "synthetic two")
        File.WriteAllText(scopeFile, scopeNode.ToJsonString())
        let original = fixture.Native.Snapshot
        Assert.Equal(3, fixture.Run [ "migration-prepare"; "--scope-file"; scopeFile; "--report-file"; reportPath ])
        Assert.Empty fixture.Native.Requests
        Assert.Empty fixture.Native.Mutations
        Assert.Equal(original, fixture.Native.Snapshot)

    [<Fact>]
    let ``actual target grouping vertical grouping and sorting drift stop later view effects`` () =
        for connectionName in [ "groupByFields"; "verticalGroupByFields"; "sortByFields" ] do
            use fixture = new Fixture()
            let view (id: string) (name: string) (filter: string) : View =
                { Id = id; Name = name; Layout = "TABLE_LAYOUT"; Filter = filter; VisibleFieldIds = [ scope.StatusFieldId ]; UnsupportedConfiguration = "[]" }
            let originalA = view "synthetic-view-a" "synthetic A" "old-a"
            let originalB = view "synthetic-view-b" "synthetic B" "old-b"
            fixture.Native.Snapshot <- { fixture.Native.Snapshot with Views = [ originalA; originalB ] }
            fixture.Scope <-
                { fixture.Scope with Views = [ { Id = Some originalA.Id; Name = originalA.Name; Layout = originalA.Layout; Filter = "new-a"; VisibleFieldIds = originalA.VisibleFieldIds };
                                              { Id = Some originalB.Id; Name = originalB.Name; Layout = originalB.Layout; Filter = "new-b"; VisibleFieldIds = originalB.VisibleFieldIds } ] }
            let code, path = fixture.Prepare "target-group-drift"
            Assert.Equal(0, code)
            fixture.Native.Transform <- fun (request: Transport.Request) (response: Transport.Response) ->
                match request.Body with
                | Transport.Query(document, _) when document.Contains "views(first:" && fixture.Native.Mutations.Length = 3 ->
                    let root = System.Text.Json.Nodes.JsonNode.Parse response.Body
                    let data = root.["data"]
                    let project =
                        if isNull data.["node"] then (data.["organization"]).["projectV2"] else data.["node"]
                    let inventory = project.["views"]
                    let nodes = inventory.["nodes"].AsArray()
                    let target = nodes |> Seq.find (fun (node: System.Text.Json.Nodes.JsonNode) -> node.["id"].GetValue<string>() = originalA.Id)
                    let grouped = target.[connectionName]
                    let values = grouped.["nodes"].AsArray()
                    let inserted =
                        if connectionName = "sortByFields" then
                            let selectedField =
                                {| id = scope.StatusFieldId |}
                            let sort =
                                {| direction = "ASC";
                                   field = selectedField |}
                            JsonSerializer.Serialize(sort)
                        else
                            let selectedField =
                                {| id = scope.StatusFieldId |}
                            JsonSerializer.Serialize(selectedField)
                    values.Add(System.Text.Json.Nodes.JsonNode.Parse inserted)
                    grouped.["totalCount"] <- System.Text.Json.Nodes.JsonValue.Create 1
                    Ok ({ response with Body = root.ToJsonString() })
                | _ -> Ok response
            Assert.Equal(3, fixture.Run(fixture.ApplyArguments path))
            Assert.Equal(3, fixture.Native.Mutations.Length)
            Assert.Equal("old-b", (fixture.Native.Snapshot.Views |> List.find (fun (view: View) -> view.Id = originalB.Id)).Filter)
            Assert.False(File.Exists(Path.Combine(path + ".invocation", "004-intent.json")))

    [<Fact>]
    let ``actual migration reads and post-send responses reject every malformed present errors member`` () =
        for malformed in [ "null"; "{}"; "\"synthetic unreadable failure\""; "7"; "[null]" ] do
            use reader = new Fixture()
            reader.Native.Transform <- fun (_: Transport.Request) (response: Transport.Response) ->
                let root = System.Text.Json.Nodes.JsonNode.Parse response.Body
                root.["errors"] <- System.Text.Json.Nodes.JsonNode.Parse malformed
                Ok ({ response with Body = root.ToJsonString() })
            let readCode, readPath = reader.Prepare "malformed-read-errors"
            Assert.Equal(3, readCode)
            Assert.False(File.Exists readPath)
            Assert.Empty reader.Native.Mutations
            use writer = new Fixture()
            let code, path = writer.Prepare "malformed-write-errors"
            Assert.Equal(0, code)
            let mutable exactResponse = ""
            writer.Native.TransformDispatch <- fun (_: Transport.Request) (response: Transport.Response) ->
                let root = System.Text.Json.Nodes.JsonNode.Parse response.Body
                root.["errors"] <- System.Text.Json.Nodes.JsonNode.Parse malformed
                exactResponse <- root.ToJsonString()
                Ok ({ response with Body = exactResponse })
            Assert.Equal(3, writer.Run(writer.ApplyArguments path))
            Assert.Single writer.Native.Mutations |> ignore
            Assert.Equal(exactResponse, File.ReadAllText(Path.Combine(path + ".invocation", "001-response.json")))
            Assert.True(File.Exists(Path.Combine(path + ".invocation", "001-native-readback.json")))
