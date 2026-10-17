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
              AddMembership "unselected-native-issue" ]
        for change in mutations do Assert.True(Result.isError (request planned change))
        Assert.True(Result.isError (request { planned with Digest = String.replicate 64 "f" } planned.Changes.Head))

    [<Fact>]
    let ``wrong identity schema selected issue and incomplete native pages refuse before effects`` () =
        let mutations : Snapshot list =
            [ { snapshot with ProjectId = "legacy-project" };
              { snapshot with PrincipalId = "different-principal" };
              { snapshot with Private = false };
              { snapshot with Fields = snapshot.Fields.Tail };
              { snapshot with Issues = [] } ]
        for candidate in mutations do Assert.True(Result.isError (prepare scope candidate))
        let readOnly = prepare scope { snapshot with CanUpdate = false } |> get
        Assert.True(Result.isError (request readOnly readOnly.Changes.Head))
        let native = Native snapshot
        native.BeforeRead <- fun request ->
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
            { Id = "existing-card"; ContentId = "unselected-historical-issue"; ContentKind = "Issue"
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
            { Id = None; Name = "synthetic blocked view"; Layout = "TABLE_LAYOUT"; Filter = "status:Blocked"
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
        Assert.Equal(Some "synthetic-new-view", resolved.Views.Head.Id)
        let update = (prepare resolved after |> get).Changes |> List.find (function UpdateView _ -> true | _ -> false)
        let next = prepare resolved after |> get
        connection.Dispatch(next, update, DateTimeOffset.UtcNow.AddMinutes 4.0) |> get |> ignore
        let final = collect connection.Reads resolved |> get
        validateTransition resolved after final update |> get
        Assert.Equal(desired.Filter, final.Views.Head.Filter)
        Assert.True(Result.isError (resolveReadback scope snapshot { after with Views = after.Views @ after.Views } create))

    [<Fact>]
    let ``prepared report stays separate from v4 binding and arbitrary authority fields refuse`` () =
        let prepared = prepare scope snapshot |> get
        let originalPath = System.IO.Path.GetFullPath "synthetic-original-prepared.json"
        let report = encodePrepared { prepared with PreparedPath = Some originalPath }
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
