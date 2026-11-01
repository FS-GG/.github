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
            Assert.InRange(variables.Length, 1, V2InspectionAcquisition.batchSize)
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
    // 45 reads in batches of 25 (#5119; it was 10, five sends): two sends.
    Assert.Equal(25, V2InspectionAcquisition.batchSize)
    Assert.Equal(2, calls)
    Assert.Equal<string>([1..45] |> List.map (fun number -> $"I_{number}"), first |> List.map (Result.defaultWith (Errors.explain >> failwith)))
    let final = V2InspectionAcquisition.run transport evaluate
    Assert.Equal(4, calls)
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

[<Fact>]
let ``a 500-row successor inspection folds blockers and Work level and stays inside the row-scaled budget`` () =
    // #5119: the 207-row cohort-90 inspection took 2,502 reads and exhausted the flat 500-send ceiling. Blockers now ride on
    // the native observation, Work level on the planning membership read, and reads are batched 25 at a time.
    let rows = [1..V2Projection.maximumProgrammeRows] |> List.map (fun number -> $"I_budget_{number}", { ProgrammeFixture.issue with Number = 6000 + number }, "docs/coordination/programme.md")
    let selected = ProgrammeFixture.enroll { ProgrammeFixture.successorBinding () with SelectedIssues = rows |> List.map (fun (id, issue, _) -> id, $"{issue.Owner}/{issue.Repository}#{issue.Number}") |> Map.ofList } rows
    let subjects = Collections.Generic.Dictionary<string, int>()
    let count (request: Request) result =
        let key = if request.Subject.EndsWith(" native observation", StringComparison.Ordinal) then "native observation"
                  elif request.Subject.EndsWith(" native blocked_by", StringComparison.Ordinal) then "native blocked_by"
                  else request.Subject
        subjects[key] <- (match subjects.TryGetValue key with | true, value -> value + 1 | _ -> 1)
        result
    let reads = ProgrammeFixture.successorTransportFor selected rows (Some "Delivery") count
    let report = V2ProjectionSource.inspectFixed reads selected None
    Assert.True(report.Evidence.PopulationGap.IsNone, string report.Evidence.PopulationGap)
    Assert.True(report.MembershipGap.IsNone, string report.MembershipGap)
    Assert.All(report.Items, fun item ->
        Assert.Equal("Current", item.SourceCurrentness)
        Assert.True(item.Evidence.DependencyReadComplete)
        Assert.Equal(Some("Delivery", Some "delivery-option"), item.Planning |> Option.bind _.WorkLevel)
        Assert.True(item.WorkItem.Value.Complete))
    let lookup key = match subjects.TryGetValue key with | true, value -> value | _ -> 0
    // Initial and final barrier read each row's native observation once; no separate dependency or classification read.
    Assert.Equal(2 * rows.Length, lookup "native observation")
    Assert.Equal(0, lookup "native blocked_by")
    Assert.Equal(0, lookup "selected work classification")
    // Two REST permission snapshots remain; every other read is aliased GraphQL.
    Assert.Equal(2, reads.RestCalls)
    let sends = reads.GraphQlCalls + reads.RestCalls
    // Measured 1,517 sends before #5119 (REST blocked_by and 10 reads per batch); the flat historical 500 now suffices.
    Assert.InRange(sends, 1, 250)
    Assert.InRange(sends, 1, V2Projection.refreshRequestCeiling rows.Length)
    let perRow = (report.Items |> List.sumBy _.Evidence.Reads) / rows.Length
    Assert.InRange(perRow, 1, 13)
    Assert.Empty reads.Mutations

// ---- programme refresh batching (#5131) ----

/// A programme refresh fixture: the successor population plus a per-item Observation value and a recorded writer.
type private RefreshFixture(count: int, initial: int -> string, ?failWrite: string -> bool) =
    let rows = [1..count] |> List.map (fun number -> $"I_refresh_{number}", { ProgrammeFixture.issue with Number = 6000 + number }, "docs/coordination/programme.md")
    let selected = ProgrammeFixture.enroll { ProgrammeFixture.successorBinding () with SelectedIssues = rows |> List.map (fun (id, issue, _) -> id, $"{issue.Owner}/{issue.Repository}#{issue.Number}") |> Map.ofList } rows
    let values = Collections.Generic.Dictionary<string, string>()
    do rows |> List.iteri (fun index (id, _, _) -> values["synthetic-member-" + id] <- initial (index + 1))
    let writes = Collections.Generic.List<string>()
    let mutable sourceReads = 0
    let change (request: Request) (result: Errors.IoResult<Response>) =
        match request.Body with
        | Query(document, _) when document.Contains("updatedAt state") && document.Contains("blockedBy(") ->
            sourceReads <- sourceReads + 1
            result
        | Query(document, variables) when document.Contains("fieldValueByName(name: \"Observation\")") && not (document.Contains "status:") ->
            let itemId = variables |> List.pick (function "itemId", VId id -> Some id | _ -> None)
            let id, issue, _ = rows |> List.find (fun (id, _, _) -> "synthetic-member-" + id = itemId)
            let body =
                Text.Json.JsonSerializer.Serialize
                    {| data = {| node = {| id = itemId; project = {| id = selected.ProjectId |}
                                           content = {| id = id; number = issue.Number; repository = {| nameWithOwner = issue.Owner + "/" + issue.Repository |} |}
                                           fieldValueByName = {| name = values[itemId] |} |} |} |}
            Ok { Status = 200; Body = body; Headers = Map.empty; ETag = None; NextLink = None }
        | _ -> result
    let reads = ProgrammeFixture.successorTransportFor selected rows (Some "Delivery") change
    let transport =
        { new IGitHubTransport with
            member _.Send request = (reads :> IGitHubTransport).Send request
            member _.SendMutation intent =
                let itemId = match intent.Request.Body with Query(_, variables) -> variables |> List.pick (function "itemId", VId id -> Some id | _ -> None) | _ -> failwith "mutation"
                writes.Add itemId
                if (defaultArg failWrite (fun _ -> false)) itemId then Error(Errors.Transport "response unavailable")
                else
                    values[itemId] <- "Verified"
                    Ok { Status = 200; Body = """{"data":{"updateProjectV2ItemFieldValue":{"clientMutationId":null}}}"""; Headers = Map.empty; ETag = None; NextLink = None }
            member _.RetryMutation effect = Error(Errors.Malformed(effect, "no retry")) }
    member _.Selected = selected
    member _.Transport = transport
    member _.Reads = reads
    member _.Writes = List.ofSeq writes
    member _.SourceReads = sourceReads

[<Fact>]
let ``a programme refresh of already-Verified rows is batched and writes nothing`` () =
    let fixture = RefreshFixture(207, fun _ -> "Verified")
    let report = V2ProjectionSource.runFixed fixture.Transport fixture.Selected None
    Assert.True(report.PopulationGap.IsNone, string report.PopulationGap)
    Assert.Equal(207, report.Verified)
    Assert.Empty(fixture.Writes)
    // Every Verified no-op is confirmed by a fresh second source read in its own phase: two native reads per row.
    Assert.Equal(2 * 207, fixture.SourceReads)
    let sends = fixture.Reads.GraphQlCalls + fixture.Reads.RestCalls
    let capture = Environment.GetEnvironmentVariable "FSGG_TEST_REFRESH_COUNTS"
    if not (String.IsNullOrWhiteSpace capture) then IO.File.AppendAllText(capture, $"verified rows=207 sends={sends}\n")
    // Unbatched this was about 6 sends per row (1,243 at 207 rows, #5131); batched it fits the historical 500.
    Assert.InRange(sends, 1, 150)
    Assert.InRange(sends, 1, V2Projection.refreshRequestCeiling 207)

[<Fact>]
let ``a programme refresh writes only the rows that change, each with its own preimage, one send and readback`` () =
    let fixture = RefreshFixture(40, fun index -> if index % 4 = 0 then "Unknown" else "Verified")
    let report = V2ProjectionSource.runFixed fixture.Transport fixture.Selected None
    Assert.True(report.PopulationGap.IsNone, string report.PopulationGap)
    Assert.Equal(40, report.Verified)
    let expected = [ for index in 1 .. 40 do if index % 4 = 0 then yield $"synthetic-member-I_refresh_{index}" ]
    Assert.Equal<string list>(expected, fixture.Writes)
    for item in report.Items do
        let written = item.Issue.Number % 4 = 0
        Assert.Equal((if written then 1 else 0), item.MutationAttempts)
        let itemId = $"synthetic-member-I_refresh_{item.Issue.Number - 6000}"
        Assert.Equal(Some(if written then V2Projection.Updated itemId else V2Projection.AlreadyCurrent itemId), item.Projection |> Option.map _.Outcome)
        // A written row's report comes from its own unbatched one-shot run: fresh preimage reads, the send and the readback.
        if written then Assert.InRange(item.Reads, 8, 20)
    let capture = Environment.GetEnvironmentVariable "FSGG_TEST_REFRESH_COUNTS"
    if not (String.IsNullOrWhiteSpace capture) then IO.File.AppendAllText(capture, $"mixed rows=40 writes={fixture.Writes.Length} sends={fixture.Reads.GraphQlCalls + fixture.Reads.RestCalls}\n")

[<Fact>]
let ``an uncertain programme refresh write is never resent and leaves its row unverified`` () =
    let failed = "synthetic-member-I_refresh_3"
    let fixture = RefreshFixture(6, (fun index -> if index = 3 || index = 5 then "Unknown" else "Verified"), (fun itemId -> itemId = failed))
    let report = V2ProjectionSource.runFixed fixture.Transport fixture.Selected None
    Assert.Equal<string list>([ failed; "synthetic-member-I_refresh_5" ], fixture.Writes)
    let health = report.Items |> List.map (fun item -> item.Issue.Number - 6000, item.Health) |> Map.ofList
    Assert.Equal("Unknown", health[3])
    Assert.Equal("Verified", health[5])
    Assert.Equal(5, report.Verified)

[<Fact>]
let ``a programme refresh at the 500-row cap fits the scaled budget`` () =
    let fixture = RefreshFixture(V2Projection.maximumProgrammeRows, fun index -> if index % 50 = 0 then "Unknown" else "Verified")
    let report = V2ProjectionSource.runFixed fixture.Transport fixture.Selected None
    let sends = fixture.Reads.GraphQlCalls + fixture.Reads.RestCalls
    let capture = Environment.GetEnvironmentVariable "FSGG_TEST_REFRESH_COUNTS"
    if not (String.IsNullOrWhiteSpace capture) then IO.File.AppendAllText(capture, $"cap rows=500 writes={fixture.Writes.Length} sends={sends}\n")
    Assert.True(report.PopulationGap.IsNone, string report.PopulationGap)
    Assert.Equal(500, report.Verified)
    Assert.Equal(10, fixture.Writes.Length)
    // No-op rows: first check plus the fresh confirmation. Written rows: phase 1 plus their own run's two checks.
    Assert.Equal(2 * 490 + 3 * 10, fixture.SourceReads)
    Assert.InRange(sends, 1, V2Projection.refreshRequestCeiling 500)
    Assert.InRange(sends, 1, 500)
