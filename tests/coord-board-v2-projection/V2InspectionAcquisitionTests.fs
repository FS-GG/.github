module FS.GG.Coord.GitHub.Tests.V2InspectionAcquisitionTests

open System
open System.Text.Json.Nodes
open Xunit
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Transport

let private response body = Ok { Status = 200; Body = body; Headers = Map.empty; ETag = None; NextLink = None }
let private request number =
    { Method = "POST"; Path = "graphql"; Query = []; Budget = GraphQl; IfNoneMatch = None
      Subject = string number; Body = Query("query($number:Int!){repository(owner:\"FS-GG\",name:\".github\"){issue(number:$number){id}}}", ["number", VNumber(double number)]) }

[<Fact>]
let ``bounded batches preserve every opaque identity and final phases reread`` () =
    let mutable calls = 0
    let transport = Fake.Recorder(fun sent ->
        calls <- calls + 1
        let root = JsonObject()
        let data = JsonObject()
        match sent.Body with
        | Query(document, variables) ->
            Assert.DoesNotContain("mutation", document)
            Assert.InRange(variables.Length, 1, 10)
            for name, value in variables do
                let number = match value with VNumber value -> int value | _ -> failwith "number"
                let alias = if name = "number" then "repository" else name.Replace("number", "repository")
                data[alias] <- JsonNode.Parse($"{{\"issue\":{{\"id\":\"I_{number}\"}}}}")
        | _ -> failwith "query expected"
        root.["data"] <- data
        response (root.ToJsonString()))
    let evaluate (phase: IGitHubTransport) =
        [1..45] |> List.map (fun number -> GraphQl.read phase (request number) (fun data -> Ok(data.GetProperty("repository").GetProperty("issue").GetProperty("id").GetString())))
    let first = V2InspectionAcquisition.run transport evaluate
    Assert.Equal(5, calls)
    Assert.Equal<string>([1..45] |> List.map (fun number -> $"I_{number}"), first |> List.map (Result.defaultWith (Errors.explain >> failwith)))
    let final = V2InspectionAcquisition.run transport evaluate
    Assert.Equal(10, calls)
    Assert.True((first = final))

[<Fact>]
let ``partial batch errors cannot qualify their data and do not poison independent request`` () =
    let transport = Fake.Recorder(fun _ -> response """{"data":{"b0_repository":{"issue":{"id":"I_1"}},"b1_repository":{"issue":{"id":"I_2"}}},"errors":[{"message":"denied","path":["b0_repository","issue"]}]}""")
    let result = V2InspectionAcquisition.run transport (fun phase ->
        [1;2] |> List.map (fun number -> GraphQl.read phase (request number) (fun data -> Ok(data.GetProperty("repository").GetProperty("issue").GetProperty("id").GetString()))))
    Assert.True(Result.isError result[0])
    Assert.Equal(Ok "I_2", result[1])
    Assert.Equal(1, transport.GraphQlCalls)

[<Fact>]
let ``batch missing identity remains failure and exhausted reads never retry`` () =
    let transport = Fake.Recorder(fun _ -> response """{"data":{"b0_repository":{"issue":{"id":"I_1"}}}}""")
    let result = V2InspectionAcquisition.run transport (fun phase ->
        [1;2] |> List.map (fun number -> GraphQl.read phase (request number) (fun data -> Ok(data.GetProperty("repository").GetProperty("issue").GetProperty("id").GetString()))))
    Assert.True(Result.isError result[1])
    Assert.Equal(1, transport.GraphQlCalls)
    let exhausted = Fake.Recorder(fun _ -> Error(Errors.Transport "bounded provider exhausted"))
    let refused = V2InspectionAcquisition.run exhausted (fun phase -> [1;2] |> List.map (fun number -> phase.Send (request number)))
    Assert.All(refused, fun result -> Assert.True(Result.isError result))
    Assert.Equal(1, exhausted.GraphQlCalls)

[<Fact>]
let ``full successor cohort keeps all decisions with bounded actual provider requests`` () =
    let rows = [1..40] |> List.map (fun number -> $"I_scaling_{number}", { ProgrammeFixture.issue with Number = 6000 + number }, "docs/coordination/programme.md")
    let selected = ProgrammeFixture.enroll { ProgrammeFixture.successorBinding () with SelectedIssues = rows |> List.map (fun (id, issue, _) -> id, $"{issue.Owner}/{issue.Repository}#{issue.Number}") |> Map.ofList } rows
    let reads = ProgrammeFixture.successorTransportFor selected rows (Some "Delivery") ProgrammeFixture.unchanged
    let report = V2ProjectionSource.inspectFixed reads selected None
    Assert.True(report.Evidence.PopulationGap.IsNone, string report.Evidence.PopulationGap)
    Assert.Equal(40, report.Items.Length)
    Assert.Equal(40, report.Memberships.Length)
    Assert.All(report.Items, fun item ->
        Assert.Equal("Current", item.SourceCurrentness)
        Assert.True(item.WorkItem.Value.Complete)
        Assert.True(item.Evidence.DependencyReadComplete)
        Assert.Single(item.WorkItem.Value.Children) |> ignore)
    Assert.InRange(reads.GraphQlCalls + reads.RestCalls, 1, 220)
    Assert.Empty reads.Mutations
    let baselineReads = ProgrammeFixture.successorTransportFor selected rows (Some "Delivery") ProgrammeFixture.unchanged
    let baseline = V2ProjectionSource.inspectUnbatched baselineReads selected None
    let semantics (value: V2ProjectionSource.InspectionReport) =
        value.Items |> List.map (fun item -> item.Evidence.Issue, item.SourceCurrentness, item.SourceRevision, item.Planning,
                                             item.Evidence.DependencyObservations, item.Evidence.DependencyReadComplete,
                                             item.WorkItem |> Option.map (fun work -> work.Complete, work.Sha256, work.Children))
    Assert.True((semantics baseline = semantics report))
    let now = DateTimeOffset.UtcNow
    Assert.True((V2ProjectionSource.selectActions baseline None now = V2ProjectionSource.selectActions report None now))
    let originalCalls = baselineReads.GraphQlCalls + baselineReads.RestCalls
    let batchedCalls = reads.GraphQlCalls + reads.RestCalls
    Assert.True(batchedCalls * 2 < originalCalls, $"Original={originalCalls}; batched={batchedCalls}")
    let capture = Environment.GetEnvironmentVariable "FSGG_TEST_NATIVE_QUERY_CAPTURE"
    if not (String.IsNullOrWhiteSpace capture) then IO.File.WriteAllText(capture + ".counts", $"members=40 original={originalCalls} batched={batchedCalls}")

[<Fact>]
let ``variables avoid quoted literals and preserve nested aliases and fragments`` () =
    let mutable actual = ""
    let transport = Fake.Recorder(fun sent ->
        match sent.Body with Query(document, _) -> actual <- document | _ -> failwith "query expected"
        response """{"data":{"b0_chosen":{"alias":{"id":"I_1"}},"b1_chosen":{"alias":{"id":"I_2"}}}}""")
    let custom number = { request number with Body = Query("query($number:Int!){chosen:repository(owner:\"$number\",name:\"quoted\\\"$number\"){alias:issue(number:$number){... on Issue{id}}}}", ["number", VNumber(double number)]) }
    let results = V2InspectionAcquisition.run transport (fun phase -> [1;2] |> List.map (fun number -> GraphQl.read phase (custom number) (fun data -> Ok(data.GetProperty("chosen").GetProperty("alias").GetProperty("id").GetString()))))
    Assert.Equal(1, transport.GraphQlCalls)
    Assert.Contains("owner:\"$number\"", actual)
    Assert.Contains("name:\"quoted\\\"$number\"", actual)
    Assert.Contains("alias:issue(number:$b1_number){... on Issue{id}}", actual)
    Assert.Equal(Ok "I_1", results[0])
    Assert.Equal(Ok "I_2", results[1])

[<Fact>]
let ``unsupported root fragments and directives use independent native reads`` () =
    let transport = Fake.Recorder(fun sent ->
        Assert.NotEqual<string>("bounded complete-cohort inspection batch", sent.Subject)
        response """{"data":{"repository":{"issue":{"id":"I_native"}}}}""")
    let custom number = { request number with Body = Query("query($number:Int!){... on Query{repository(owner:\"FS-GG\",name:\".github\"){issue(number:$number) @include(if:true){id}}}}", ["number", VNumber(double number)]) }
    let results = V2InspectionAcquisition.run transport (fun phase -> [1;2] |> List.map (fun number -> GraphQl.read phase (custom number) (fun data -> Ok(data.GetProperty("repository").GetProperty("issue").GetProperty("id").GetString()))))
    Assert.Equal(2, transport.GraphQlCalls)
    Assert.All(results, fun result -> Assert.Equal(Ok "I_native", result))

[<Fact>]
let ``nested child pagination drains every page and final drift or partial loss refuses`` () =
    for mode in ["complete"; "partial"; "drift"] do
        let mutable pages = 0
        let reads = ProgrammeFixture.successorTransport (ProgrammeFixture.successorBinding ()) (Some "Delivery") (fun sent value ->
            if sent.Subject <> "selected native work hierarchy" then value
            else
                pages <- pages + 1
                let continuation = match sent.Body with Query(_, variables) -> variables |> List.exists (fun (name, _) -> name = "after") | _ -> false
                if mode = "partial" && continuation then Error(Errors.Transport "lost nested page")
                else value |> Result.map (fun response ->
                    let root = JsonNode.Parse response.Body
                    let connection = root.["data"].["repository"].["issue"].["subIssues"]
                    connection.["totalCount"] <- JsonValue.Create 2
                    connection.["pageInfo"].["hasNextPage"] <- JsonValue.Create(not continuation)
                    connection.["pageInfo"].["endCursor"] <- if continuation then null else JsonValue.Create "page2"
                    if continuation then
                        connection.["nodes"].[0].["id"] <- JsonValue.Create(if mode = "drift" && pages > 2 then "I_changed-page2" else "I_page2")
                        connection.["nodes"].[0].["number"] <- JsonValue.Create 9997
                    { response with Body = root.ToJsonString() }))
        let result = V2ProjectionSource.inspectFixed reads (ProgrammeFixture.successorBinding ()) None
        if mode = "complete" then
            Assert.Equal(4, pages)
            Assert.Equal("Current", result.Items.Head.SourceCurrentness)
            Assert.Equal(2, result.Items.Head.WorkItem.Value.Children.Length)
        else
            Assert.Equal("Unknown", result.Items.Head.SourceCurrentness)
            Assert.False(result.Items.Head.WorkItem.Value.Complete)

[<Fact>]
let ``phase reuse never suppresses protected plan policy or permission final barriers`` () =
    for mode in ["plan"; "policy"; "permission"] do
        let mutable readsOfFact = 0
        let reads = ProgrammeFixture.successorTransport (ProgrammeFixture.successorBinding ()) (Some "Delivery") (fun sent value ->
            let matching =
                match sent.Body with
                | Query(document, _) when mode = "plan" -> document.Contains("currentExpression") && not (sent.Subject.EndsWith("board-v2-import-manifest.json", StringComparison.Ordinal))
                | Query(document, _) when mode = "policy" -> document.Contains("issueCreationPolicy")
                | NoBody when mode = "permission" -> sent.Path.EndsWith("/collaborators/author/permission", StringComparison.Ordinal)
                | _ -> false
            if not matching then value
            else
                readsOfFact <- readsOfFact + 1
                if readsOfFact = 1 then value
                else value |> Result.map (fun response ->
                    let body = if mode = "plan" then response.Body.Replace(ProgrammeFixture.head, String.replicate 40 "a")
                               elif mode = "policy" then response.Body.Replace("COLLABORATORS_ONLY", "ALL")
                               else response.Body.Replace("write", "read")
                    { response with Body = body }))
        let result = V2ProjectionSource.inspectFixed reads (ProgrammeFixture.successorBinding ()) None
        Assert.Equal(2, readsOfFact)
        if mode = "plan" then Assert.Equal("Unknown", result.Items.Head.SourceCurrentness)
        else Assert.True(result.Items.Head.Intake.Allowed.IsNone)

[<Fact>]
let ``unattributable batch errors refuse every request even when all data is present`` () =
    for path in ["missing"; "null"; "[]"; "[\"not_a_generated_alias\"]"; "[\"b99_repository\"]"; "[null]"; "[7]"; "{}"; "\"b0_repository\""; "[\"b0_repository\",{}]"; "[\"b0_repository\",-1]"] do
        let pathProperty = if path = "missing" then "" else ",\"path\":" + path
        let payload = "{\"data\":{\"b0_repository\":{\"issue\":{\"id\":\"I_1\"}},\"b1_repository\":{\"issue\":{\"id\":\"I_2\"}}},\"errors\":[{\"message\":\"unattributable native failure\"" + pathProperty + "}]}"
        let transport = Fake.Recorder(fun _ -> response payload)
        let result = V2InspectionAcquisition.run transport (fun phase ->
            [1;2] |> List.map (fun number -> GraphQl.read phase (request number) (fun data -> Ok(data.GetProperty("repository").GetProperty("issue").GetProperty("id").GetString()))))
        Assert.All(result, fun value -> Assert.True(Result.isError value, "error path=" + path))
        Assert.Equal(1, transport.GraphQlCalls)

[<Fact>]
let ``one character variable and root alias retain their exact GraphQL names`` () =
    let mutable actual = ""
    let transport = Fake.Recorder(fun sent ->
        match sent.Body with
        | Query(document, variables) ->
            actual <- document
            Assert.Equal<string>(["b0_n"; "b1_n"], variables |> List.map fst)
        | _ -> failwith "query expected"
        response """{"data":{"b0_a":{"id":"I_1"},"b1_a":{"id":"I_2"}}}""")
    let custom number = { request number with Body = Query("query($n:ID!){a:node(id:$n){... on Issue{id}}}", ["n", VId $"I_{number}"]) }
    let results = V2InspectionAcquisition.run transport (fun phase ->
        [1;2] |> List.map (fun number -> GraphQl.read phase (custom number) (fun data -> Ok(data.GetProperty("a").GetProperty("id").GetString()))))
    Assert.Contains("$b0_n:ID!", actual)
    Assert.Contains("b0_a:node(id:$b0_n)", actual)
    Assert.Contains("b1_a:node(id:$b1_n)", actual)
    Assert.Equal(Ok "I_1", results[0])
    Assert.Equal(Ok "I_2", results[1])
