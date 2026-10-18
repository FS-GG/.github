namespace FS.GG.Coord.Tests

module V2MigrationTests =
    open System
    open System.Text.Json
    open Xunit
    open FS.GG.Coord.GitHub
    open FS.GG.Coord.GitHub.V2Migration
    open MigrationFixture

    let private get = function Ok value -> value | Error error -> failwith (Errors.explain error)

    [<Fact>]
    let ``synthetic migration drains native caller and preserves exact existing option descriptors`` () =
        let native = Native snapshot
        use connection = native.Connection()
        let actual = collect connection.Reads scope |> get
        let planned = prepare scope actual |> get
        Assert.Equal(2, planned.Changes.Length)
        let change = planned.Changes.Head
        let request = request planned change |> get
        match request.Body with
        | Transport.Json json ->
            use document = JsonDocument.Parse json
            let options = document.RootElement.GetProperty("variables").GetProperty("input").GetProperty "singleSelectOptions"
            Assert.Equal(6, options.GetArrayLength())
            let retained = actual.Fields |> List.find (fun (field: Field) -> field.Id = scope.StatusFieldId)
            for index in 0 .. 4 do
                Assert.Equal(retained.Options.[index].Id, options.[index].GetProperty("id").GetString())
                Assert.Equal(retained.Options.[index].Description, options.[index].GetProperty("description").GetString())
            Assert.False(options.[5].TryGetProperty("id") |> fst)
        | _ -> failwith "typed JSON request expected"
        connection.Dispatch(planned, change, DateTimeOffset.UtcNow.AddMinutes 4.0) |> get |> ignore
        let after = collect connection.Reads scope |> get
        validateTransition scope actual after change |> get
        Assert.Equal(1, native.Mutations.Length)
        Assert.Equal(1, (prepare scope after |> get).Changes.Length)

    [<Fact>]
    let ``mutation reconstruction refuses forged deltas option omission and broad effects`` () =
        let planned = prepare scope snapshot |> get
        let status = snapshot.Fields |> List.find (fun (field: Field) -> field.Id = scope.StatusFieldId)
        let mutations : Change list =
            [ AddInReview(scope.StatusFieldId, status.Options.Tail, "BLUE", scope.InReviewDescription);
              AddInReview("other-field", status.Options, "BLUE", scope.InReviewDescription);
              AddMembership "unselected-native-issue";
              InitializePlanningValue("synthetic-issue", "synthetic-item", scope.ObservationFieldId, PlanningText "forged Observation") ]
        for change in mutations do Assert.True(Result.isError (request planned change))
        Assert.True(Result.isError (request ({ planned with Digest = String.replicate 64 "f" }) planned.Changes.Head))

    [<Fact>]
    let ``wrong identity schema selected issue and incomplete native pages refuse before effects`` () =
        let mutations : Snapshot list =
            [ { snapshot with ProjectId = "legacy-project" };
              { snapshot with PrincipalId = "different-principal" };
              { snapshot with Private = false };
              { snapshot with Fields = snapshot.Fields.Tail };
              { snapshot with Issues = [] } ]
        for candidate in mutations do Assert.True(Result.isError (prepare scope candidate))
        let readOnly = prepare scope ({ snapshot with CanUpdate = false }) |> get
        Assert.True(Result.isError (request readOnly readOnly.Changes.Head))
        let native = Native snapshot
        native.BeforeRead <- fun (request: Transport.Request) ->
            match request.Body with
            | Transport.Query(document, _) when document.Contains "items(first:" ->
                native.Snapshot <- { native.Snapshot with ProjectId = "changed-native-target" }
            | _ -> ()
        use connection = native.Connection()
        Assert.True(Result.isError (collect connection.Reads scope))
        Assert.Empty native.Mutations

    [<Fact>]
    let ``independent readback refuses human planning drift after one exact schema effect`` () =
        let item : Item =
            { Id = "existing-card"; ContentId = "unselected-historical-issue"; ContentKind = "Issue";
              Repository = Some "FS-GG/.github"; Number = Some 99; UpdatedAt = "original-update"; Archived = false; Values = [] }
        let original = { snapshot with Items = [ item ] }
        let native = Native original
        use connection = native.Connection()
        let planned = prepare scope original |> get
        connection.Dispatch(planned, planned.Changes.Head, DateTimeOffset.UtcNow.AddMinutes 4.0) |> get |> ignore
        let changed = { native.Snapshot with Items = [ { item with UpdatedAt = "human-edit" } ] }
        Assert.True(Result.isError (validateTransition scope original changed planned.Changes.Head))
        Assert.Equal(item, native.Snapshot.Items.Head)

    [<Fact>]
    let ``new view filter is an exact ID dependent stage only after unambiguous native create`` () =
        let desired : DesiredView =
            { Id = None; Name = "synthetic blocked view"; Layout = "TABLE_LAYOUT"; Filter = "status:Blocked";
              VisibleFieldIds = [ scope.StatusFieldId; scope.RoadmapFieldId ] }
        let scope = { scope with Views = [ desired ] }
        let native = Native snapshot
        use connection = native.Connection()
        let before = prepare scope snapshot |> get
        let create = before.Changes |> List.find (function CreateView _ -> true | _ -> false)
        connection.Dispatch(before, create, DateTimeOffset.UtcNow.AddMinutes 4.0) |> get |> ignore
        let after = collect connection.Reads scope |> get
        validateTransition scope snapshot after create |> get
        let resolved = resolveReadback scope snapshot after create |> get
        Assert.Equal<string option>(Some "synthetic-new-view-0", resolved.Views.Head.Id)
        let update = (prepare resolved after |> get).Changes |> List.find (function UpdateView _ -> true | _ -> false)
        let next = prepare resolved after |> get
        connection.Dispatch(next, update, DateTimeOffset.UtcNow.AddMinutes 4.0) |> get |> ignore
        let final = collect connection.Reads resolved |> get
        validateTransition resolved after final update |> get
        Assert.Equal(desired.Filter, final.Views.Head.Filter)
        Assert.True(Result.isError (resolveReadback scope snapshot ({ after with Views = after.Views @ after.Views }) create))

    [<Fact>]
    let ``prepared report stays separate from v4 binding and arbitrary authority fields refuse`` () =
        let prepared = prepare scope snapshot |> get
        let originalPath = System.IO.Path.GetFullPath "synthetic-original-prepared.json"
        let report = encodePrepared ({ prepared with PreparedPath = Some originalPath })
        let parsedScope, digest, _, boundPath = decodePrepared report |> get
        Assert.Equal(originalPath, boundPath)
        Assert.Equal(scope, parsedScope)
        Assert.Equal(prepared.Digest, digest)
        use document = JsonDocument.Parse report
        let input = document.RootElement.GetProperty("scope").GetRawText()
        Assert.True(Result.isError (parseScope (input.Insert(1, "\"authorized\":true,"))))
        Assert.True(Result.isError (decodePrepared (encodeReadback scope snapshot)))

    [<Fact>]
    let ``actual bounded HTTP sender bounds overflow retains redirect response and never retries acquisition failure`` () =
        let cases : (string * (unit -> System.Net.Http.HttpResponseMessage)) list =
            [ ("overflow", fun () ->
                let response = new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
                response.Content <- new System.Net.Http.ByteArrayContent(Array.zeroCreate<byte> (4 * 1024 * 1024 + 1))
                response);
              ("redirect", fun () ->
                let response = new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.TemporaryRedirect)
                response.Headers.Location <- Uri "https://untrusted.invalid/graphql"
                response.Content <- new System.Net.Http.StringContent "redirect"
                response) ]
        for name, response in cases do
            let mutable calls = 0
            let handler =
                { new System.Net.Http.HttpMessageHandler() with
                    override _.SendAsync(request: System.Net.Http.HttpRequestMessage, _: System.Threading.CancellationToken) =
                        calls <- calls + 1
                        Assert.Equal("https://api.github.com/graphql", request.RequestUri.AbsoluteUri)
                        System.Threading.Tasks.Task.FromResult(response()) }
            use connection = V2MigrationTransport.createWithHandler "synthetic-not-a-credential" handler |> get
            let prepared = prepare scope snapshot |> get
            let outcome = connection.Dispatch(prepared, prepared.Changes.Head, DateTimeOffset.UtcNow.AddMinutes 4.0)
            if name = "redirect" then
                let receipt = outcome |> get
                Assert.Equal(307, receipt.Status)
                Assert.Contains("redirect", receipt.Body)
            else Assert.True(Result.isError outcome, name)
            Assert.Equal(1, calls)
        let mutable failedCalls = 0
        let failed =
            { new System.Net.Http.HttpMessageHandler() with
                override _.SendAsync(_: System.Net.Http.HttpRequestMessage, _: System.Threading.CancellationToken) =
                    failedCalls <- failedCalls + 1
                    System.Threading.Tasks.Task.FromException<System.Net.Http.HttpResponseMessage>(new System.Net.Http.HttpRequestException("synthetic private error")) }
        use connection = V2MigrationTransport.createWithHandler "synthetic-not-a-credential" failed |> get
        let prepared = prepare scope snapshot |> get
        let outcome = connection.Dispatch(prepared, prepared.Changes.Head, DateTimeOffset.UtcNow.AddMinutes 4.0)
        Assert.True(Result.isError outcome)
        Assert.Equal(1, failedCalls)
        match outcome with
        | Error error -> Assert.DoesNotContain("synthetic private error", Errors.explain error)
        | Ok _ -> failwith "failed response acquisition must stay Unknown"

    [<Fact>]
    let ``actual HTTP sender deadline exception is uncertain with exactly one acquisition`` () =
        let mutable calls = 0
        let handler =
            { new System.Net.Http.HttpMessageHandler() with
                override _.SendAsync(_: System.Net.Http.HttpRequestMessage, _: System.Threading.CancellationToken) =
                    calls <- calls + 1
                    System.Threading.Tasks.Task.FromCanceled<System.Net.Http.HttpResponseMessage>(System.Threading.CancellationToken(true)) }
        use connection = V2MigrationTransport.createWithHandler "synthetic-not-a-credential" handler |> get
        let prepared = prepare scope snapshot |> get
        let outcome = connection.Dispatch(prepared, prepared.Changes.Head, DateTimeOffset.UtcNow.AddMinutes 4.0)
        Assert.True(Result.isError outcome)
        Assert.Equal(1, calls)
        match outcome with
        | Error error -> Assert.Contains("Unknown; readback only", Errors.explain error)
        | Ok _ -> failwith "a cancelled acquisition cannot authorize a retry"

    [<Fact>]
    let ``expired explicit window refuses at actual bounded dispatch edge without HTTP acquisition`` () =
        let mutable calls = 0
        let handler =
            { new System.Net.Http.HttpMessageHandler() with
                override _.SendAsync(_: System.Net.Http.HttpRequestMessage, _: System.Threading.CancellationToken) =
                    calls <- calls + 1
                    System.Threading.Tasks.Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)) }
        use connection = V2MigrationTransport.createWithHandler "synthetic-not-a-credential" handler |> get
        let prepared = prepare scope snapshot |> get
        Assert.True(Result.isError (connection.Dispatch(prepared, prepared.Changes.Head, DateTimeOffset.UtcNow.AddMinutes(-1.0))))
        Assert.Equal(0, calls)

    [<Fact>]
    let ``migration native read classifies errors before missing malformed or ambiguous data`` () =
        let bodies: string list =
            [ """{"errors":[{"message":"synthetic native error"}]}""";
              """{"errors":[{"message":"synthetic native error"}],"data":null}""";
              """{"errors":[{"message":"synthetic native error"}],"data":7}""";
              """{"errors":[{"message":"synthetic native error"}],"data":{"node":{},"node":{}}}""";
              """{"errors":null,"data":7}""";
              """{"errors":{}}""" ]
        for body in bodies do
            let native = Native snapshot
            native.Transform <- fun (_: Transport.Request) (response: Transport.Response) ->
                Ok({ response with Body = body })
            use connection = native.Connection()
            match collect connection.Reads scope with
            | Error error -> Assert.Contains("has errors or malformed errors", Errors.explain error)
            | Ok _ -> failwith "native errors cannot be hidden by missing or malformed data"
            Assert.Single native.Requests |> ignore
            Assert.Empty native.Mutations

    [<Fact>]
    let ``planning declarations bind digest exact schema and native membership without overwrite`` () =
        let selected: PlanningOption = { Id = "synthetic-active"; Name = "Active delivery" }
        let initial: InitialPlanning =
            { IssueId = scope.SelectedIssues.Head.Id; Track = selected; Roadmap = "https://github.com/FS-GG/FS.GG.SDD/blob/main/docs/roadmap.md";
              Status = Some { Id = snapshot.Fields.Head.Options.Head.Id; Name = "Backlog" } }
        let fields: Field list =
            snapshot.Fields |> List.map (fun (field: Field) ->
                if field.Id = scope.TrackFieldId then
                    { field with Options = [ { Id = selected.Id; Name = selected.Name; Color = "BLUE"; Description = "synthetic" } ] }
                else field)
        let scoped: Scope = { scope with InitialPlanning = [ initial ] }
        let native: Snapshot = { snapshot with Fields = fields }
        let planned = prepare scoped native |> get
        let changed = prepare ({ scoped with InitialPlanning = [ { initial with Roadmap = initial.Roadmap + "#changed" } ] }) native |> get
        Assert.NotEqual<string>(planned.Digest, changed.Digest)
        for invalid in [ { scoped with InitialPlanning = [ initial; initial ] };
                         { scoped with InitialPlanning = [ { initial with IssueId = "unselected" } ] };
                         { scoped with InitialPlanning = [ { initial with Track = { selected with Id = "wrong-option" } } ] } ] do
            Assert.True(Result.isError (prepare invalid native))
        let memberItem: Item =
            { Id = "synthetic-live-item"; ContentId = initial.IssueId; ContentKind = "Issue"; Repository = Some "FS-GG/.github";
              Number = Some 42; UpdatedAt = "2026-10-17T12:00:00Z"; Archived = false; Values = [] }
        let populated = { native with Items = [ memberItem ] }
        let current = prepare scoped populated |> get
        let initialized = current.Changes |> List.filter (function InitializePlanningValue _ -> true | _ -> false)
        Assert.Equal(3, initialized.Length)
        for change in initialized do request current change |> get |> ignore
        for memberItem in [ { memberItem with Repository = Some "FS-GG/Other" };
                            { memberItem with Number = Some 99 };
                            { memberItem with Archived = true } ] do
            Assert.True(Result.isError (prepare scoped ({ native with Items = [ memberItem ] })))
        for fieldId, kind, tokens in
            [ (scope.RoadmapFieldId, "ProjectV2ItemFieldTextValue", [ "text"; "\"Preserve another owner's plan\"" ]);
              (scope.TrackFieldId, "ProjectV2ItemFieldSingleSelectValue", [ "optionId"; "\"other-track\""; "name"; "\"Follow-up\"" ]);
              (scope.StatusFieldId, "ProjectV2ItemFieldSingleSelectValue", [ "optionId"; "\"other-status\""; "name"; "\"Done\"" ]) ] do
            let existing: ItemValue =
                { Id = fieldId; FieldId = fieldId; Kind = kind;
                  Value = JsonSerializer.Serialize([ "id"; "\"retained-native-value\""; "updatedAt"; "\"2026-10-17T12:00:00Z\"" ] @ tokens) }
            let candidate: Snapshot = { native with Items = [ { memberItem with Values = [ existing ] } ] }
            Assert.True(Result.isError (prepare scoped candidate))
            Assert.Equal<ItemValue>(existing, candidate.Items.Head.Values.Head)
        let roadmapChange = InitializePlanningValue(initial.IssueId, memberItem.Id, scope.RoadmapFieldId, PlanningText initial.Roadmap)
        let withRoadmapValue (nativeId: string) (updatedAt: string) : Snapshot =
            let value: ItemValue =
                { Id = scope.RoadmapFieldId; FieldId = scope.RoadmapFieldId; Kind = "ProjectV2ItemFieldTextValue";
                  Value = JsonSerializer.Serialize([ "id"; nativeId; "updatedAt"; updatedAt; "text"; JsonSerializer.Serialize(initial.Roadmap) ]) }
            { populated with Items = [ { memberItem with Values = [ value ]; UpdatedAt = "2026-10-17T13:00:00Z" } ] }
        let nativeId = "\"synthetic-roadmap-value\""
        let nativeTime = "\"2026-10-17T13:00:00Z\""
        validateTransition scoped populated (withRoadmapValue nativeId nativeTime) roadmapChange |> get |> ignore
        let invalidIdentities: (string * string) list =
            [ ("null", nativeTime); ("123", nativeTime); ("\" padded \"", nativeTime);
              (nativeId, "null"); (nativeId, "123"); (nativeId, "\"17/10/2026 13:00:00\"") ]
        for id, time in invalidIdentities do
            let malformed = withRoadmapValue id time
            Assert.True(Result.isError (prepare scoped malformed))
            Assert.True(Result.isError (validateTransition scoped populated malformed roadmapChange))
        let omitted = prepare ({ scoped with InitialPlanning = [ { initial with Status = None } ] }) populated |> get
        Assert.Contains<string>(omitted.OwnerActions, fun action -> action.Contains("intentionally unresolved"))
        Assert.Equal(2, omitted.Changes |> List.filter (function InitializePlanningValue _ -> true | _ -> false) |> List.length)
        let encoded = encodePrepared ({ current with PreparedPath = Some(System.IO.Path.GetFullPath "synthetic-initialization.json") })
        use document = JsonDocument.Parse encoded
        let scopeJson = document.RootElement.GetProperty("scope").GetRawText()
        Assert.Equal<Scope>(scoped, parseScope scopeJson |> get)
        let unknown = scopeJson.Replace("\"initialPlanning\":", "\"authorized\":true,\"initialPlanning\":")
        Assert.True(Result.isError (parseScope unknown))

    [<Fact>]
    let ``readback observation distinguishes exact pre-effect state from all sibling transitions and drift`` () =
        let track: PlanningOption = { Id = "synthetic-track-active"; Name = "Active delivery" }
        let initial: InitialPlanning =
            { IssueId = scope.SelectedIssues.Head.Id; Track = track; Roadmap = "synthetic roadmap";
              Status = Some { Id = snapshot.Fields.Head.Options.Head.Id; Name = "Backlog" } }
        let existing: View =
            { Id = "synthetic-existing-view"; Name = "synthetic existing"; Layout = "TABLE_LAYOUT"; Filter = "";
              VisibleFieldIds = [ scope.StatusFieldId ]; UnsupportedConfiguration = "[[\"\"],[],[],[]]" }
        let desired: DesiredView =
            { Id = Some existing.Id; Name = existing.Name; Layout = existing.Layout; Filter = "status:Blocked";
              VisibleFieldIds = existing.VisibleFieldIds }
        let created: DesiredView =
            { Id = None; Name = "synthetic new"; Layout = "TABLE_LAYOUT"; Filter = "status:Blocked";
              VisibleFieldIds = [ scope.StatusFieldId ] }
        let mutable selected: Scope = { scope with InitialPlanning = [ initial ]; Views = [ desired; created ] }
        let fields: Field list =
            snapshot.Fields |> List.map (fun (field: Field) ->
                if field.Id = scope.TrackFieldId then
                    { field with Options = [ { Id = track.Id; Name = track.Name; Color = "BLUE"; Description = "synthetic" } ] }
                else field)
        let native = Native ({ snapshot with Fields = fields; Views = [ existing ] })
        use connection = native.Connection()
        let kinds = System.Collections.Generic.HashSet<string>()
        let mutable before = collect connection.Reads selected |> get
        for _ in 1 .. 8 do
            let prepared = prepare selected before |> get
            let change = prepared.Changes.Head
            let response: Transport.Response = connection.Dispatch(prepared, change, DateTimeOffset.UtcNow.AddMinutes 4.0) |> get
            let after = collect connection.Reads selected |> get
            let operation, payload, kind =
                match change with
            | CreateWorkLevel -> failwith "legacy manual fixture unexpectedly selected successor field creation"
                | AddInReview _ -> "updateProjectV2Field", "projectV2Field", "status"
                | AddMembership _ -> "addProjectV2ItemById", "item", "membership"
                | InitializePlanningValue _ -> "updateProjectV2ItemFieldValue", "projectV2Item", "initialize"
                | CreateView _ -> "createProjectV2View", "projectV2View", "create"
                | UpdateView _ -> "updateProjectV2View", "projectV2View", "update"
            kinds.Add kind |> ignore
            use document = JsonDocument.Parse response.Body
            let returnedId = document.RootElement.GetProperty("data").GetProperty(operation).GetProperty(payload).GetProperty("id").GetString()
            let stale: Snapshot = { before with UpdatedAt = "2026-10-18T00:32:24Z" }
            Assert.Equal<ReadbackObservation>(PendingVisibility, observeTransition selected before stale change returnedId |> get)
            let changedIssues: NativeIssue list = before.Issues |> List.map (fun (issue: NativeIssue) -> { issue with UpdatedAt = "2026-10-18T01:00:00Z" })
            Assert.True(Result.isError (observeTransition selected before ({ stale with Issues = changedIssues }) change returnedId))
            Assert.True(Result.isError (observeTransition selected before ({ stale with CanUpdate = false }) change returnedId))
            Assert.True(Result.isError (observeTransition selected before after change "synthetic-unrelated-returned-id"))
            match change with
            | InitializePlanningValue(_, itemId, _, _) ->
                let items: Item list =
                    stale.Items |> List.map (fun (item: Item) ->
                        if item.Id = itemId then { item with UpdatedAt = "2026-10-18T01:00:00Z" } else item)
                Assert.Equal<ReadbackObservation>(PendingVisibility, observeTransition selected before ({ stale with Items = items }) change returnedId |> get)
            | _ -> ()
            match observeTransition selected before after change returnedId |> get with
            | ObservedEffect resolved -> selected <- resolved
            | PendingVisibility -> failwith "one complete exact native witness must confirm the correlated effect"
            before <- after
        Assert.Equal(5, kinds.Count)
        Assert.Empty((prepare selected before |> get).Changes)
        Assert.Equal(8, native.Mutations.Length)

    [<Fact>]
    let ``migration remaining duration belongs to original composition and is zero after disposal`` () =
        let native = Native snapshot
        let connection = native.Connection()
        let first = connection.RemainingTime
        Assert.True(first > TimeSpan.Zero && first <= TimeSpan.FromSeconds 270.0)
        collect connection.Reads scope |> get |> ignore
        Assert.True(connection.RemainingTime <= first)
        connection.Dispose()
        Assert.Equal<TimeSpan>(TimeSpan.Zero, connection.RemainingTime)

    [<Fact>]
    let ``observation reader refuses expired planning deadline before any actual native acquisition`` () =
        let native = Native snapshot
        use connection = native.Connection()
        let expired = connection.ObservationReads(DateTimeOffset.UtcNow.AddSeconds(-1.0))
        Assert.True(Result.isError (collect expired scope))
        Assert.Empty native.Requests
        Assert.Empty native.Mutations
        collect connection.Reads scope |> get |> ignore
        Assert.Equal(2, native.Requests.Length)

    [<Fact>]
    let ``Work level creation uses one typed effect then exact readback before absent initialization`` () =
        let originalScope = { scope with WorkLevel = Some { FieldId = None; Assignments = [ { IssueId = "synthetic-issue"; Name = "Delivery" } ] } }
        let native = Native snapshot
        use connection = native.Connection()
        let before = collect connection.Reads originalScope |> get
        let prepared = prepare originalScope before |> get
        Assert.Equal(CreateWorkLevel, prepared.Changes.Head)
        // New semantics are digest-bound; original scope/prepare remains exactly the old mode.
        Assert.NotEqual<string>((prepare scope before |> get).Digest, prepared.Digest)
        let bound = { prepared with PreparedPath = Some(System.IO.Path.GetFullPath "synthetic-work-level-prepared.json") }
        let wire = encodePrepared bound
        Assert.Contains("fs.gg.board-v2.migration-prepare/2", wire)
        Assert.Contains("fs.gg.board-v2.work-level-migration/1", wire)
        let decoded, digest, _, _ = decodePrepared wire |> get
        Assert.Equal(originalScope, decoded)
        Assert.Equal(prepared.Digest, digest)
        let request = request prepared CreateWorkLevel |> get
        match request.Body with
        | Transport.Json json ->
            use document = JsonDocument.Parse json
            let input = document.RootElement.GetProperty("variables").GetProperty "input"
            Assert.Equal("Work level", input.GetProperty("name").GetString())
            Assert.Equal("SINGLE_SELECT", input.GetProperty("dataType").GetString())
            Assert.Equal(2, input.GetProperty("singleSelectOptions").GetArrayLength())
        | _ -> failwith "typed request expected"
        connection.Dispatch(prepared, CreateWorkLevel, DateTimeOffset.UtcNow.AddMinutes 4.0) |> get |> ignore
        let after = collect connection.Reads originalScope |> get
        validateTransition originalScope before after CreateWorkLevel |> get
        let resolved = resolveReadback originalScope before after CreateWorkLevel |> get
        Assert.Equal(Some "synthetic-work-level", resolved.WorkLevel |> Option.bind _.FieldId)
        Assert.DoesNotContain(CreateWorkLevel, (prepare resolved after |> get).Changes)
        // Reusing an unresolved absent preimage cannot adopt a same-name field or send a duplicate.
        Assert.True(Result.isError (prepare originalScope after))
        Assert.Single(native.Mutations) |> ignore

    [<Fact>]
    let ``Work level cannot reclassify existing intent or create duplicate field identities`` () =
        let native = Native snapshot
        use connection = native.Connection()
        let selected = { scope with WorkLevel = Some { FieldId = None; Assignments = [] } }
        let prepared = prepare selected snapshot |> get
        connection.Dispatch(prepared, CreateWorkLevel, DateTimeOffset.UtcNow.AddMinutes 4.0) |> get |> ignore
        let after = collect connection.Reads selected |> get
        let resolved = resolveReadback selected snapshot after CreateWorkLevel |> get
        let field = after.Fields |> List.find (fun (field: Field) -> field.Name = "Work level")
        let existing: Item =
            { Id = "synthetic-card"; ContentId = "synthetic-issue"; ContentKind = "Issue";
              Repository = Some "FS-GG/.github"; Number = Some 42; UpdatedAt = "2026-10-17T13:00:00Z";
              Archived = false; Values = [] }
        let populated = { after with Items = [ existing ] }
        let classified =
            { resolved with
                WorkLevel = resolved.WorkLevel |> Option.map (fun plan ->
                    { plan with Assignments = [ { IssueId = "synthetic-issue"; Name = "Delivery" } ] }) }
        let declaration = prepare classified populated |> get
        let initialization = declaration.Changes |> List.find (function InitializePlanningValue(_, _, id, _) -> id = field.Id | _ -> false)
        let option = field.Options |> List.find (fun option -> option.Name = "Outcome")
        let value: ItemValue =
            { Id = field.Id; FieldId = field.Id; Kind = "ProjectV2ItemFieldSingleSelectValue"
              Value = JsonSerializer.Serialize([ "id"; JsonSerializer.Serialize "synthetic-value"; "updatedAt"; JsonSerializer.Serialize "2026-10-17T13:00:00Z";
                                                 "optionId"; JsonSerializer.Serialize option.Id; "name"; JsonSerializer.Serialize option.Name ]) }
        let conflicting = { populated with Items = [ { existing with Values = [ value ] } ] }
        Assert.True(Result.isError (prepare classified conflicting))
        Assert.True(Result.isError (request declaration CreateWorkLevel))
        Assert.True(Result.isError (validateTransition classified populated conflicting initialization))
        for invalid in [ { classified with WorkLevel = Some { FieldId = Some field.Id; Assignments = [ { IssueId = "not-selected"; Name = "Delivery" } ] } }
                         { classified with WorkLevel = Some { FieldId = Some scope.StatusFieldId; Assignments = [] } }
                         { classified with WorkLevel = Some { FieldId = Some field.Id; Assignments = [ { IssueId = "synthetic-issue"; Name = "Ready" } ] } } ] do
            Assert.True(Result.isError (validateScope invalid))
        let drifted = { after with Fields = after.Fields |> List.map (fun current -> if current.Id = field.Id then { current with Options = current.Options.Tail } else current) }
        Assert.True(Result.isError (prepare resolved drifted))

    [<Fact>]
    let ``successor view repair cannot replace layout columns or filter away All items`` () =
        let selected = { scope with WorkLevel = Some { FieldId = None; Assignments = [] } }
        let original: View =
            { Id = "synthetic-existing-view"; Name = "All items"; Layout = "TABLE_LAYOUT"; Filter = "";
              VisibleFieldIds = [ scope.StatusFieldId; scope.RoadmapFieldId ]; UnsupportedConfiguration = "[[],[],[]]" }
        let current = { snapshot with Views = [ original ] }
        let desired: DesiredView =
            { Id = Some original.Id; Name = original.Name; Layout = original.Layout; Filter = ""; VisibleFieldIds = original.VisibleFieldIds }
        for mutated in [ { desired with Filter = "is:open" }; { desired with Layout = "BOARD_LAYOUT" };
                         { desired with VisibleFieldIds = [ scope.StatusFieldId ] } ] do
            Assert.True(Result.isError(prepare { selected with Views = [ mutated ] } current))
        Assert.True(Result.isOk(prepare { selected with Views = [ desired ] } current))
