// Shares a collection with V2WriterLockTests: both mutate XDG_RUNTIME_DIR, so they never run in parallel.
[<Xunit.Collection("xdg-runtime-dir")>]
module FS.GG.Coord.GitHub.Tests.BoardV2ApplicationTests

open System
open System.IO
open System.Text.Json
open System.Security.Cryptography
open Xunit
open FS.GG.Coord.Cli
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Transport
open FS.GG.Coord.GitHub.V2Projection
open FS.GG.Coord.GitHub.V2ProjectionSource

type private UnreadableInput() =
    inherit TextReader()
    let mutable reads = 0
    member _.Reads = reads
    override _.Read(_: char[], _: int, _: int) =
        reads <- reads + 1
        failwith "stdin must not be consumed for this invocation"

let private unreadableInput () = new UnreadableInput()

let private bindingJson artifact =
    JsonSerializer.Serialize
        {| bindingVersion = 2; importRecipeRevision = String.replicate 40 "e"; importArtifactSha256 = String.replicate 64 "f"
           selectedIssues = Map.ofList [ "I_1", "FS-GG/.github#1"; "I_2", "FS-GG/.github#2"; "I_3", "FS-GG/.github#3" ]
           schemaVersion = 1; recipeRevision = String.replicate 40 "a"; populationRevision = String.replicate 40 "c"
           organizationId = "O_fsgg"; artifactSha256 = artifact; ownerKind = "organization"; owner = "FS-GG"
           projectNumber = 77; projectTitle = "Coordination V2"; projectId = "PVT_coord_v2"
           status = {| id = "status"; options = Map.ofList ["Backlog", "b"; "Ready", "r"; "In progress", "i"; "Blocked", "x"; "Done", "d"] |}
           roadmapFieldId = "roadmap"; track = {| id = "track"; options = Map.ofList ["Active delivery", "a"; "Follow-up", "f"] |}
           observation = {| id = "observation"; options = Map.ofList ["Verified", "v"; "Stale", "s"; "Unknown", "u"] |}
           repositories = ["FS-GG/.github"] |}

let private loadedDigest () =
    use stream = File.OpenRead typeof<Binding>.Assembly.Location
    SHA256.HashData stream |> Convert.ToHexString |> _.ToLowerInvariant()

[<Fact>]
let ``binding refuses caller currentness and executable inputs`` () =
    let valid = bindingJson (loadedDigest ())
    Assert.True(BoardV2Application.parseBinding valid |> Result.isOk)
    for extra in ["current"; "verifier"; "recipe"; "url"] do
        let spoofed = valid.Insert(1, sprintf "\"%s\":\"Current\"," extra)
        Assert.True(BoardV2Application.parseBinding spoofed |> Result.isError)
    Assert.True(BoardV2Application.parseBinding (valid.Replace("organization", "user")) |> Result.isError)
    Assert.True(BoardV2Application.parseBinding (valid.Replace("Coordination V2", "Coordination")) |> Result.isError)

[<Fact>]
let ``invalid loaded artifact and unknown flags perform no transport calls or report writes`` () =
    let directory = Path.Combine(Path.GetTempPath(), "board-v2-cli-" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(directory, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute) |> ignore
    try
        let bindingPath = Path.Combine(directory, "binding.json")
        let reportPath = Path.Combine(directory, "report.json")
        File.WriteAllText(bindingPath, bindingJson (String.replicate 64 "0"))
        let mutable calls = 0
        let transport =
            { new IGitHubTransport with
                member _.Send request = calls <- calls + 1; Error(Errors.Unauthorized request.Subject)
                member _.SendMutation mutation = calls <- calls + 1; Error(Errors.Unauthorized mutation.Request.Subject)
                member _.RetryMutation effect = calls <- calls + 1; Error(Errors.Unauthorized effect) }
        let args = ["refresh"; "--binding-file"; bindingPath; "--report-file"; reportPath]
        Assert.Equal(1, BoardV2Application.runWithTransport (TestLock.fresh ()) transport args)
        Assert.Equal(1, BoardV2Application.runWithTransport (TestLock.fresh ()) transport (args @ ["--current"; "true"]))
        Assert.Equal(0, calls)
        Assert.False(File.Exists reportPath)
    finally Directory.Delete(directory, true)

[<Fact>]
let ``successor actual CLI reconciles twenty two items before its unchanged request ceiling`` () =
    let repositories = [| ".github"; "FS.GG.Rendering"; "FS.GG.Game"; "FS.GG.Audio"; "FS.GG.Net"; "FS.GG.SDD" |]
    let rows =
        [ for index in 0 .. 21 ->
            let issue: IssueRef = { Owner = "FS-GG"; Repository = repositories[index % repositories.Length]; Number = 6000 + index }
            "I_cohort_" + string index, issue, "docs/coordination/programme.md" ]
    let selected =
        ProgrammeFixture.enroll
            { ProgrammeFixture.successorBinding () with
                SelectedIssues = rows |> List.map (fun (id, issue, _) -> id, $"{issue.Owner}/{issue.Repository}#{issue.Number}") |> Map.ofList
                Repositories = repositories |> Array.map (fun name -> "FS-GG/" + name) |> Set.ofArray } rows
    let change (request: Transport.Request) result =
        result |> Result.map (fun (response: Transport.Response) ->
            let root = Text.Json.Nodes.JsonNode.Parse response.Body
            if request.Subject = "complete V2 project memberships" then
                let connection = root.["data"].["node"].["items"]
                for index in 0 .. 2 do
                    connection.["nodes"].AsArray().Add(JsonSerializer.SerializeToNode
                        {| id = "PVTI_unselected_" + string index
                           content = {| __typename = "Issue"; id = "I_unselected_" + string index
                                        number = 8000 + index; repository = {| nameWithOwner = "FS-GG/.github" |} |} |})
                connection.["totalCount"] <- Text.Json.Nodes.JsonValue.Create 25
            elif request.Subject = "bound V2 successor planning memberships" then
                // The Work level classification rides on the planning membership read (#5119).
                let number = match request.Body with Query(_, variables) -> variables |> List.pick (function "number", VNumber value -> Some(int value) | _ -> None) | _ -> failwith "expected query"
                let index = rows |> List.findIndex (fun (_, issue, _) -> issue.Number = number)
                let level, option = if index < 17 then "Outcome", "outcome-option" else "Delivery", "delivery-option"
                let workLevel = root.["data"].["repository"].["issue"].["projectItems"].["nodes"].[0].["workLevel"]
                workLevel.["name"] <- Text.Json.Nodes.JsonValue.Create level
                workLevel.["optionId"] <- Text.Json.Nodes.JsonValue.Create option
            { response with Body = root.ToJsonString() })
    let reads = ProgrammeFixture.successorTransportFor selected rows (Some "Delivery") change
    let directory = Path.Combine(Path.GetTempPath(), "board-v2-cohort-budget-" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(directory, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute) |> ignore
    try
        let input, output = Path.Combine(directory, "binding.json"), Path.Combine(directory, "report.json")
        File.WriteAllText(input, ProgrammeFixture.bindingJsonFor selected)
        let code = BoardV2Application.runWithTransport (TestLock.fresh ()) reads ["inspect"; "--binding-file"; input; "--report-file"; output]
        use report = JsonDocument.Parse(File.ReadAllText output)
        let root = report.RootElement
        let sends = reads.GraphQlCalls + reads.RestCalls
        let populationGap, membershipGap = root.GetProperty("populationGap").ToString(), root.GetProperty("membershipGap").ToString()
        Assert.True((code = 0), $"exit={code}; sends={sends}; populationGap={populationGap}; membershipGap={membershipGap}")
        Assert.InRange(sends, 1, 499)
        Assert.Equal(22, root.GetProperty("selected").GetInt32())
        Assert.Equal(25, root.GetProperty("memberships").GetArrayLength())
        Assert.Equal(3, root.GetProperty("memberships").EnumerateArray() |> Seq.filter (fun item -> not (item.GetProperty("enrolled").GetBoolean())) |> Seq.length)
        let items = root.GetProperty("items").EnumerateArray() |> Seq.toList
        Assert.Equal(22, items.Length)
        Assert.All(items, fun item ->
            Assert.Equal("Current", item.GetProperty("sourceCurrentness").GetString())
            Assert.True(item.GetProperty("workItem").GetProperty("hierarchy").GetProperty("complete").GetBoolean()))
        Assert.Equal(17, items |> List.filter (fun item -> item.GetProperty("workItem").GetProperty("workLevel").GetString() = "Outcome") |> List.length)
        Assert.Equal(0, root.GetProperty("mutationAttempts").GetInt32())
        Assert.Empty reads.Mutations
    finally Directory.Delete(directory, true)

let private cohortOf (count: int) =
    let rows = [ for index in 1 .. count -> $"I_cap_{index}", { ProgrammeFixture.issue with Number = 6000 + index }, "docs/coordination/programme.md" ]
    let selected =
        ProgrammeFixture.enroll
            { ProgrammeFixture.successorBinding () with
                SelectedIssues = rows |> List.map (fun (id, issue, _) -> id, $"{issue.Owner}/{issue.Repository}#{issue.Number}") |> Map.ofList } rows
    rows, selected

[<Fact>]
let ``actual CLI inspects a full 500-row cohort inside its scaled request ceiling`` () =
    // #5119: the flat 500-send ceiling was exhausted by the 207-row cohort-90 inspection. The ceiling now scales with the
    // selected population, and the reads per row are cut, so the full 500-row cap is readable well inside it.
    let rows, selected = cohortOf V2Projection.maximumProgrammeRows
    let reads = ProgrammeFixture.successorTransportFor selected rows (Some "Delivery") ProgrammeFixture.unchanged
    let directory = Path.Combine(Path.GetTempPath(), "board-v2-cap500-" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(directory, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute) |> ignore
    try
        let input, output = Path.Combine(directory, "binding.json"), Path.Combine(directory, "report.json")
        File.WriteAllText(input, ProgrammeFixture.bindingJsonFor selected)
        let code = BoardV2Application.runWithTransport (TestLock.fresh ()) reads ["inspect"; "--binding-file"; input; "--report-file"; output]
        use report = JsonDocument.Parse(File.ReadAllText output)
        let root = report.RootElement
        let sends = reads.GraphQlCalls + reads.RestCalls
        let populationGap, membershipGap = root.GetProperty("populationGap").ToString(), root.GetProperty("membershipGap").ToString()
        Assert.True((code = 0), $"exit={code}; sends={sends}; populationGap={populationGap}; membershipGap={membershipGap}")
        Assert.Equal(500, root.GetProperty("selected").GetInt32())
        Assert.Equal(500, root.GetProperty("memberships").GetArrayLength())
        Assert.InRange(sends, 1, V2Projection.refreshRequestCeiling 500)
        Assert.InRange(sends, 1, 500)
        Assert.Empty reads.Mutations
    finally Directory.Delete(directory, true)

[<Fact>]
let ``actual CLI reports an archived enrolled card as an explicit archived membership`` () =
    // #5119: Done retention archives cards; the inspection wire keeps the row, marked archived, with its discrepancy.
    let rows, selected = cohortOf 3
    let archivedId, _, _ = rows.[1]
    let change (request: Transport.Request) (result: Errors.IoResult<Transport.Response>) =
        result |> Result.map (fun (response: Transport.Response) ->
            let root = Text.Json.Nodes.JsonNode.Parse response.Body
            if request.Subject = "bound V2 successor planning memberships" then
                let node = root.["data"].["repository"].["issue"].["projectItems"].["nodes"].[0]
                if node.["content"].["id"].GetValue<string>() = archivedId then node.["isArchived"] <- Text.Json.Nodes.JsonValue.Create true
            elif request.Subject = "complete V2 project memberships" then
                let items = root.["data"].["node"].["items"]
                let nodes = items.["nodes"].AsArray()
                for index in nodes.Count - 1 .. -1 .. 0 do
                    if nodes.[index].["content"].["id"].GetValue<string>() = archivedId then nodes.RemoveAt index
                items.["totalCount"] <- Text.Json.Nodes.JsonValue.Create nodes.Count
            { response with Body = root.ToJsonString() })
    let reads = ProgrammeFixture.successorTransportFor selected rows (Some "Delivery") change
    let directory = Path.Combine(Path.GetTempPath(), "board-v2-archived-" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(directory, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute) |> ignore
    try
        let input, output = Path.Combine(directory, "binding.json"), Path.Combine(directory, "report.json")
        File.WriteAllText(input, ProgrammeFixture.bindingJsonFor selected)
        let code = BoardV2Application.runWithTransport (TestLock.fresh ()) reads ["inspect"; "--binding-file"; input; "--report-file"; output]
        use report = JsonDocument.Parse(File.ReadAllText output)
        let root = report.RootElement
        let membershipGap = root.GetProperty("membershipGap").ToString()
        Assert.True((code = 0), $"exit={code}; membershipGap={membershipGap}")
        let memberships = root.GetProperty("memberships").EnumerateArray() |> Seq.toList
        Assert.Equal(3, memberships.Length)
        let archived = memberships |> List.filter (fun entry -> entry.GetProperty("archived").GetBoolean())
        Assert.Equal(1, archived.Length)
        Assert.Equal(archivedId, archived.Head.GetProperty("contentId").GetString())
        Assert.True(archived.Head.GetProperty("enrolled").GetBoolean())
        Assert.Equal(V2ProjectionSource.archivedCardDiscrepancy, archived.Head.GetProperty("discrepancy").GetString())
        let item = root.GetProperty("items").EnumerateArray() |> Seq.find (fun item -> item.GetProperty("expectedNodeId").GetString() = archivedId)
        Assert.Contains(V2ProjectionSource.archivedCardDiscrepancy, item.GetProperty("discrepancies").EnumerateArray() |> Seq.map _.GetString())
    finally Directory.Delete(directory, true)

[<Fact>]
let ``actual CLI refuses a 501-row binding before any send`` () =
    let rows, selected = cohortOf (V2Projection.maximumProgrammeRows + 1)
    let reads = ProgrammeFixture.successorTransportFor selected rows (Some "Delivery") ProgrammeFixture.unchanged
    let directory = Path.Combine(Path.GetTempPath(), "board-v2-cap501-" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(directory, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute) |> ignore
    try
        let input, output = Path.Combine(directory, "binding.json"), Path.Combine(directory, "report.json")
        File.WriteAllText(input, ProgrammeFixture.bindingJsonFor selected)
        let code = BoardV2Application.runWithTransport (TestLock.fresh ()) reads ["inspect"; "--binding-file"; input; "--report-file"; output]
        Assert.NotEqual(0, code)
        Assert.Equal(0, reads.GraphQlCalls + reads.RestCalls)
        Assert.False(File.Exists output)
    finally Directory.Delete(directory, true)

[<Fact>]
let ``the refresh request ceiling and deadline keep their historical floors and scale to the row cap`` () =
    Assert.Equal(500, V2Projection.refreshRequestCeiling 0)
    Assert.Equal(500, V2Projection.refreshRequestCeiling 22)
    Assert.Equal(500, V2Projection.refreshRequestCeiling 218)
    Assert.Equal(502, V2Projection.refreshRequestCeiling 219)
    Assert.Equal(1064, V2Projection.refreshRequestCeiling 500)
    Assert.Equal(1064, V2Projection.refreshRequestCeiling 501)
    Assert.Equal(500, V2Projection.refreshRequestCeiling -1)
    Assert.Equal(270.0, V2Projection.refreshDeadlineSeconds 0)
    Assert.Equal(270.0, V2Projection.refreshDeadlineSeconds 207)
    Assert.Equal(270.0, V2Projection.refreshDeadlineSeconds 300)
    Assert.Equal(410.0, V2Projection.refreshDeadlineSeconds 500, 6)
    Assert.Equal(410.0, V2Projection.refreshDeadlineSeconds 501, 6)

[<Fact>]
let ``the actual bounded transport stops at the binding's scaled ceiling and deadline`` () =
    let probe : Transport.Request = { Method = "GET"; Path = "rate_limit"; Query = []; Body = Transport.NoBody; Budget = Transport.Rest; IfNoneMatch = None; Subject = "probe" }
    let counting () =
        let mutable sent = 0
        (fun () -> sent),
        { new IGitHubTransport with
            member _.Send _ = sent <- sent + 1; Ok { Status = 200; Body = "{}"; Headers = Map.empty; ETag = None; NextLink = None }
            member _.SendMutation _ = Error(Errors.Unauthorized "read-only probe")
            member _.RetryMutation effect = Error(Errors.Unauthorized effect) }
    let refusedAfter (binding: Binding) (elapsed: unit -> TimeSpan) attempts =
        let sent, inner = counting ()
        let bounded = BoardV2Application.boundedWith elapsed binding inner
        let results = [ for _ in 1 .. attempts -> bounded.Send probe ]
        sent (), results |> List.filter Result.isError |> List.length
    let small = snd (cohortOf 22)
    let full = snd (cohortOf 500)
    let now = fun () -> TimeSpan.Zero
    // Request ceiling: 500 for a small binding, 1,064 at the 500-row cap; the next send is refused before dispatch.
    Assert.Equal((500, 1), refusedAfter small now 501)
    Assert.Equal((1064, 1), refusedAfter full now 1065)
    // Deadline: 270 seconds for a small binding, 410 at the cap.
    Assert.Equal((0, 1), refusedAfter small (fun () -> TimeSpan.FromSeconds 280.0) 1)
    Assert.Equal((1, 0), refusedAfter full (fun () -> TimeSpan.FromSeconds 280.0) 1)
    Assert.Equal((0, 1), refusedAfter full (fun () -> TimeSpan.FromSeconds 411.0) 1)

[<Fact>]
let ``report history roundtrip strips current authority while retaining last verification`` () =
    let issue = { Owner = "FS-GG"; Repository = ".github"; Number = 2963 }
    let projection =
        { ProjectId = "PVT_coord_v2"; RecipeRevision = String.replicate 40 "a"; Issue = issue
          ObservedRevision = "native-observed"; Observation = "Verified"; Outcome = Updated "PVTI_native"
          SourceChecks = 2; ProjectReads = 5; Mutations = 1; VerifiedAt = DateTimeOffset.Parse "2026-10-02T01:02:03Z" }
    let report =
        { ProjectId = projection.ProjectId; RecipeRevision = projection.RecipeRevision; PopulationRevision = String.replicate 40 "c"
          ImportRecipeRevision = String.replicate 40 "e"; ImportArtifactSha256 = String.replicate 64 "f"; ArtifactSha256 = String.replicate 64 "b"; NativeDispatch = []; ProtectedInputs = []
          Selected = Some 1; Attempted = 1; Verified = 1; PopulationGap = None; Cleanup = "complete"
          Items = [ { Issue = issue; Native = None; DependencyObservations = []; DependencyReadComplete = false; PlanObservation = None; Delivery = "Unknown"; Publication = "Unknown"
                      NativeAcceptance = "Unknown"; Health = "Verified"; Reads = 5; MutationAttempts = 1; MembershipPages = Some 1
                      Projection = Some projection; Gap = None; LastVerified = Some projection } ] }
    let wire = BoardV2Application.encodeReport report
    match BoardV2Application.decodePreviousReport wire with
    | Error error -> failwith error
    | Ok history ->
        Assert.Equal(0, history.Verified)
        Assert.True(history.Selected.IsNone)
        Assert.True(history.Items.Head.Projection.IsNone)
        Assert.Equal("Unknown", history.Items.Head.Health)
        Assert.Equal(Some projection, history.Items.Head.LastVerified)
    let failedRefresh =
        { report with
            RecipeRevision = String.replicate 40 "b"
            Verified = 0
            PopulationGap = Some(Errors.Unauthorized "protected population")
            Items = report.Items |> List.map (fun item ->
                { item with Health = "Unknown"; Projection = None; Gap = Some(Errors.Unauthorized "native issue") }) }
    let failedWire = BoardV2Application.encodeReport failedRefresh
    match BoardV2Application.decodePreviousReport failedWire with
    | Error error -> failwith error
    | Ok history ->
        Assert.Equal(String.replicate 40 "b", history.RecipeRevision)
        Assert.Equal(Some projection, history.Items.Head.LastVerified)
        Assert.True(history.Items.Head.Projection.IsNone)
        Assert.Equal("Unknown", history.Items.Head.Health)
    Assert.True(BoardV2Application.decodePreviousReport (failedWire.Replace(String.replicate 40 "a", "caller-current")) |> Result.isError)
    Assert.True(BoardV2Application.decodePreviousReport (wire.Replace("\"outcome\":\"updated\"", "\"outcome\":\"Current\"")) |> Result.isError)

[<Fact>]
let ``partial CLI report is truthful and local lock releases after report failure`` () =
    let directory = Path.Combine(Path.GetTempPath(), "board-v2-cli-" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(directory, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute) |> ignore
    try
        let bindingPath = Path.Combine(directory, "binding.json")
        File.WriteAllText(bindingPath, bindingJson (loadedDigest ()))
        let mutable calls = 0
        let transport =
            { new IGitHubTransport with
                member _.Send request = calls <- calls + 1; Error(Errors.Unauthorized request.Subject)
                member _.SendMutation _ = failwith "partial source must never dispatch"
                member _.RetryMutation _ = failwith "no retry" }
        let lockDirectory = TestLock.fresh ()
        let run path = BoardV2Application.runWithTransport lockDirectory transport [ "refresh"; "--binding-file"; bindingPath; "--report-file"; path ]
        let missingParent = Path.Combine(directory, "missing", "report.json")
        Assert.Equal(1, run missingParent)
        let reportPath = Path.Combine(directory, "report.json")
        Assert.Equal(3, run reportPath)
        Assert.Equal(2, calls)
        use document = JsonDocument.Parse(File.ReadAllText reportPath)
        let root = document.RootElement
        Assert.Equal(0, root.GetProperty("verified").GetInt32())
        Assert.Equal(JsonValueKind.Null, root.GetProperty("selected").ValueKind)
        Assert.NotEqual(JsonValueKind.Null, root.GetProperty("populationGap").ValueKind)
        Assert.Equal(0, root.GetProperty("nativeDispatch").GetArrayLength())
        Assert.Contains("released", root.GetProperty("cleanup").GetString())
        // The refresh released the shared writer lock (and its legacy companion) in its injected directory.
        match V2WriterLock.tryAcquire (V2WriterLock.inDirectory lockDirectory "PVT_coord_v2" (V2WriterLock.observationLegacyName "PVT_coord_v2")) with
        | Ok lease -> lease.Dispose()
        | Error reason -> failwithf "writer lock still held: %s" reason
        Assert.True(File.Exists(Path.Combine(lockDirectory, V2WriterLock.fileName "PVT_coord_v2")))
    finally Directory.Delete(directory, true)

[<Fact>]
let ``refresh takes the writer lock in the injected directory and never in the production one`` () =
    // Causal control for #5126: XDG_RUNTIME_DIR points at a sentinel. If the seam ignored the injected directory
    // and resolved the production lock, the sentinel would receive fsgg/board-writer-*.lock.
    let directory = Path.Combine(Path.GetTempPath(), "board-v2-lock-" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(directory, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute) |> ignore
    let sentinel = Path.Combine(directory, "runtime")
    Directory.CreateDirectory(sentinel, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute) |> ignore
    let saved = Environment.GetEnvironmentVariable "XDG_RUNTIME_DIR"
    try
        Environment.SetEnvironmentVariable("XDG_RUNTIME_DIR", sentinel)
        let bindingPath = Path.Combine(directory, "binding.json")
        File.WriteAllText(bindingPath, bindingJson (loadedDigest ()))
        let transport =
            { new IGitHubTransport with
                member _.Send request = Error(Errors.Unauthorized request.Subject)
                member _.SendMutation _ = failwith "partial source must never dispatch"
                member _.RetryMutation _ = failwith "no retry" }
        let lockDirectory = TestLock.fresh ()
        Assert.Equal(3, BoardV2Application.runWithTransport lockDirectory transport [ "refresh"; "--binding-file"; bindingPath; "--report-file"; Path.Combine(directory, "report.json") ])
        Assert.True(File.Exists(Path.Combine(lockDirectory, V2WriterLock.fileName "PVT_coord_v2")))
        Assert.True(File.Exists(Path.Combine(lockDirectory, V2WriterLock.observationLegacyName "PVT_coord_v2")))
        Assert.Empty(Directory.EnumerateFileSystemEntries sentinel)
    finally
        Environment.SetEnvironmentVariable("XDG_RUNTIME_DIR", saved)
        Directory.Delete(directory, true)

[<Fact>]
let ``inspect emits unknown population evidence without any mutation dispatch`` () =
    let directory = Path.Combine(Path.GetTempPath(), "board-v2-inspect-" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(directory, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute) |> ignore
    try
        let bindingPath = Path.Combine(directory, "binding.json")
        let reportPath = Path.Combine(directory, "inspection.json")
        File.WriteAllText(bindingPath, bindingJson (loadedDigest ()))
        let mutable reads = 0
        let mutable mutations = 0
        let transport =
            { new IGitHubTransport with
                member _.Send request = reads <- reads + 1; Error(Errors.Unauthorized request.Subject)
                member _.SendMutation _ = mutations <- mutations + 1; failwith "inspect attempted mutation"
                member _.RetryMutation _ = mutations <- mutations + 1; failwith "inspect attempted retry" }
        Assert.Equal(3, BoardV2Application.runWithTransport (TestLock.fresh ()) transport ["inspect"; "--binding-file"; bindingPath; "--report-file"; reportPath])
        Assert.Equal(1, reads)
        Assert.Equal(0, mutations)
        use document = JsonDocument.Parse(File.ReadAllText reportPath)
        let root = document.RootElement
        Assert.Equal("fsgg.coord.board-v2-inspection/1", root.GetProperty("schema").GetString())
        Assert.Equal(JsonValueKind.Null, root.GetProperty("selected").ValueKind)
        Assert.Equal(0, root.GetProperty("mutationAttempts").GetInt32())
        Assert.Equal("status", root.GetProperty("status").GetProperty("id").GetString())
        Assert.Equal("r", root.GetProperty("status").GetProperty("options").GetProperty("Ready").GetString())
        Assert.Equal("roadmap", root.GetProperty("roadmapFieldId").GetString())
        Assert.Equal("track", root.GetProperty("track").GetProperty("id").GetString())
        Assert.Equal("observation", root.GetProperty("observation").GetProperty("id").GetString())
        Assert.Equal("FS-GG/.github", (root.GetProperty("repositories").[0]).GetString())
        Assert.Equal(0, root.GetProperty("candidates").GetArrayLength())
        Assert.Equal(0, root.GetProperty("items").GetArrayLength())
    finally Directory.Delete(directory, true)

let private productJson repository project number =
    let json = bindingJson (loadedDigest ())
    json.Replace("\"bindingVersion\":2", "\"bindingVersion\":3")
        .Replace("FS-GG/.github", "ExampleOrg/" + repository).Replace("FS-GG", "ExampleOrg")
        .Replace("O_fsgg", "O_products").Replace("Coordination V2", project + " V2").Replace("PVT_coord_v2", "PVT_" + project)
        .Replace("\"projectNumber\":77", $"\"projectNumber\":{number}")

[<Fact>]
let ``unchanged closed wire decoder distinguishes two receiver repository bindings`` () =
    for repository, project, number in [ "WidgetA", "product_a", 81; "WidgetB", "product_b", 82 ] do
        let json = productJson repository project number
        match BoardV2Application.parseBinding json with
        | Ok binding ->
            Assert.Equal(3, binding.BindingVersion)
            Assert.Equal("PVT_" + project, binding.ProjectId)
            Assert.Equal<Set<string>>(Set.singleton ("ExampleOrg/" + repository), binding.Repositories)
        | Error error -> failwith error
        for bad in [ json.Replace("organization", "user")
                     json.Replace("ExampleOrg/" + repository + "#1", "ExampleOrg/Foreign#1")
                     json.Replace("PVT_" + project, "PVT_kwDOEYAWY84Bldpa")
                     json.Replace("PVT_" + project, "PVT_kwDOEYAWY84Bb08W")
                     json.Insert(1, "\"current\":true,") ] do
            Assert.True(BoardV2Application.parseBinding bad |> Result.isError)

[<Fact>]
let ``product inspect missing receiver provenance stays Unknown and never queries organization population`` () =
    let directory = Path.Combine(Path.GetTempPath(), "board-v2-product-" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(directory, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute) |> ignore
    try
        for repository, project, number in [ "WidgetA", "product_a", 81; "WidgetB", "product_b", 82 ] do
            let input = Path.Combine(directory, project + "-binding.json")
            let output = Path.Combine(directory, project + "-report.json")
            File.WriteAllText(input, productJson repository project number)
            let mutable calls = 0
            let transport =
                { new IGitHubTransport with
                    member _.Send request =
                        calls <- calls + 1
                        match request.Body with
                        | Query(document, variables) ->
                            Assert.DoesNotContain("repository(owner: \"FS-GG\"", document)
                            Assert.Equal(Some(VString "ExampleOrg"), variables |> List.tryFind (fst >> (=) "owner") |> Option.map snd)
                            Assert.Equal(Some(VString repository), variables |> List.tryFind (fst >> (=) "repo") |> Option.map snd)
                        | _ -> failwith "receiver population must be a fixed query"
                        Error(Errors.Unauthorized "receiver provenance unavailable")
                    member _.SendMutation _ = failwith "inspect is structurally read-only"
                    member _.RetryMutation _ = failwith "inspect cannot retry" }
            Assert.Equal(3, BoardV2Application.runWithTransport (TestLock.fresh ()) transport [ "inspect"; "--binding-file"; input; "--report-file"; output ])
            Assert.Equal(1, calls)
            use document = JsonDocument.Parse(File.ReadAllText output)
            let root = document.RootElement
            Assert.Equal(3, root.GetProperty("bindingVersion").GetInt32())
            Assert.Equal("PVT_" + project, root.GetProperty("projectId").GetString())
            Assert.Equal(JsonValueKind.Null, root.GetProperty("selected").ValueKind)
            Assert.Equal(0, root.GetProperty("mutationAttempts").GetInt32())
        Assert.True(BoardV2Application.tryRun [] |> Option.isNone)
    finally Directory.Delete(directory, true)

[<Fact>]
let ``actual programme CLI caller joins native PR facts and retains attributed selection gaps`` () =
    let directory = Path.Combine(Path.GetTempPath(), "board-v2-programme-" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(directory, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute) |> ignore
    try
        let bindingPath = Path.Combine(directory, "binding.json")
        let reportPath = Path.Combine(directory, "inspection.json")
        File.WriteAllText(bindingPath, ProgrammeFixture.bindingJson ())
        let transport = ProgrammeFixture.transport ProgrammeFixture.unchanged
        use unusedInput = unreadableInput ()
        Assert.Equal(0, BoardV2Application.runWithTransportAndInput (TestLock.fresh ()) transport unusedInput [ "inspect"; "--binding-file"; bindingPath; "--report-file"; reportPath ])
        Assert.Empty transport.Mutations
        use document = JsonDocument.Parse(File.ReadAllText reportPath)
        let root = document.RootElement
        Assert.Equal("fsgg.coord.board-v2-inspection/2", root.GetProperty("schema").GetString())
        Assert.Equal(2, root.GetProperty("memberships").GetArrayLength())
        for name in [ "membershipGap"; "memberships"; "nativeIntegrator"; "selectionCoverage"; "selection" ] do
            Assert.True(root.TryGetProperty(name) |> fst, name)
        let item = (root.GetProperty("items")).[0]
        Assert.True(item.TryGetProperty("sourceRevision") |> fst)
        Assert.True(item.TryGetProperty("accountableOwner") |> fst)
        let candidate = (root.GetProperty("candidates")).[0]
        Assert.Equal(1, candidate.GetProperty("openPullRequests").GetArrayLength())
        Assert.Equal(JsonValueKind.Object, candidate.GetProperty("touchSets").ValueKind)
        let selected = (root.GetProperty("selection")).[0]
        Assert.Equal("unknown", selected.GetProperty("decision").GetString())
        Assert.Equal("https://github.com/FS-GG/.github/pull/42", selected.GetProperty("pullRequest").GetString())
        Assert.Equal(ProgrammeFixture.head, selected.GetProperty("head").GetString())
        Assert.False(selected.GetProperty("dispatchAuthority").GetBoolean())
        Assert.Contains("Partial .8", root.GetProperty("selectionCoverage").GetString())
        Assert.Equal(JsonValueKind.String, root.GetProperty("nativeIntegrator").GetProperty("observedAt").ValueKind)
        let missingPath = Path.Combine(directory, "uncollected.json")
        let missingReads =
            ProgrammeFixture.transport (fun request response ->
                if request.Subject = "complete V2 project memberships" then Error(Errors.Unauthorized "membership unavailable") else response)
        Assert.Equal(3, BoardV2Application.runWithTransport (TestLock.fresh ()) missingReads [ "inspect"; "--binding-file"; bindingPath; "--report-file"; missingPath ])
        use missingDocument = JsonDocument.Parse(File.ReadAllText missingPath)
        Assert.Equal(JsonValueKind.Null, missingDocument.RootElement.GetProperty("nativeIntegrator").GetProperty("observedAt").ValueKind)
        Assert.Empty missingReads.Mutations
        let refreshPath = Path.Combine(directory, "refresh.json")
        use refreshInput = unreadableInput ()
        Assert.Equal(1, BoardV2Application.runWithTransportAndInput (TestLock.fresh ()) transport refreshInput [ "refresh"; "--binding-file"; bindingPath; "--report-file"; refreshPath; "--integrator-stdin" ])
        Assert.Equal(0, refreshInput.Reads)
        Assert.Empty transport.Mutations
    finally Directory.Delete(directory, true)


[<Fact>]
let ``actual product CLI retains closed schema one top level and item property sets`` () =
    let directory = Path.Combine(Path.GetTempPath(), "board-v2-product-wire-" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(directory, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute) |> ignore
    try
        let input = Path.Combine(directory, "binding.json")
        let output = Path.Combine(directory, "inspection.json")
        File.WriteAllText(input, productJson "WidgetA" "product_a" 81)
        let rows =
            [ 1; 2; 3 ]
            |> List.map (fun number ->
                {| issue = $"ExampleOrg/WidgetA#{number}"; nodeId = $"I_{number}"; observedUpdatedAt = "2026-10-17T12:00:00Z"
                   observedState = "open"; decision = "import"; adjudication = "verified-remaining"; pilot = true
                   roadmap = "docs/product.md"; dependencies = ([]: obj list) |})
        let manifest =
            JsonSerializer.Serialize(
                {| schema = "fsgg.coordination-board-v2-import/v1"
                   target = {| ownerKind = "organization"; owner = "ExampleOrg"; title = "product_a V2"; id = "PVT_product_a"; number = 81; creationState = "created-and-read-back" |}
                   binding = {| organizationId = "O_products"; visibility = "complete"; authorization = "root-selected"
                                recipeRevision = String.replicate 40 "e"; artifactSha256 = String.replicate 64 "f"; repositories = [ "ExampleOrg/WidgetA" ] |}
                   items = rows |})
        let mutable reads = 0
        let transport =
            { new IGitHubTransport with
                member _.Send request =
                    reads <- reads + 1
                    if reads = 1 then
                        let blob = {| oid = String.replicate 40 "b"; text = manifest |}
                        Ok { Status = 200; ETag = None; NextLink = None; Headers = Map.empty
                             Body = JsonSerializer.Serialize ({| data = {| repository = {| nameWithOwner = "ExampleOrg/WidgetA"; ref = {| target = {| oid = String.replicate 40 "c" |} |}; object = blob; current = blob |} |} |}) }
                    else Error(Errors.Unauthorized request.Subject)
                member _.SendMutation _ = failwith "product inspect cannot mutate"
                member _.RetryMutation _ = failwith "product inspect cannot retry" }
        use rejectedInput = unreadableInput ()
        let rejectedOutput = Path.Combine(directory, "rejected.json")
        Assert.Equal(1, BoardV2Application.runWithTransportAndInput (TestLock.fresh ()) transport rejectedInput [ "inspect"; "--binding-file"; input; "--report-file"; rejectedOutput; "--integrator-stdin" ])
        Assert.Equal(0, reads)
        Assert.Equal(0, rejectedInput.Reads)
        Assert.False(File.Exists rejectedOutput)
        Assert.Equal(3, BoardV2Application.runWithTransport (TestLock.fresh ()) transport [ "inspect"; "--binding-file"; input; "--report-file"; output ])
        use document = JsonDocument.Parse(File.ReadAllText output)
        let root = document.RootElement
        let names (value: JsonElement) = value.EnumerateObject() |> Seq.map _.Name |> Set.ofSeq
        let expectedTop =
            Set.ofList(
                [ "schema"; "bindingVersion"; "organizationId"; "owner"; "projectNumber"; "projectId"; "projectTitle"; "schemaVersion"; "ownerKind"; "repositories";
                  "status"; "roadmapFieldId"; "track"; "observation"; "selectedIssues"; "recipeRevision"; "artifactSha256"; "populationRevision"; "importRecipeRevision";
                  "importArtifactSha256"; "protectedInputs"; "selected"; "attempted"; "verified"; "mutationAttempts"; "populationGap"; "cleanup"; "items"; "candidates" ])
        let expectedItem =
            Set.ofList(
                [ "issue"; "expectedNodeId"; "native"; "dependencyObservations"; "dependencyReadComplete"; "planObservation"; "sourceCurrentness"; "planning"; "planningGap";
                  "discrepancies"; "health"; "delivery"; "publication"; "nativeAcceptance"; "reads"; "mutationAttempts"; "gap"; "lastVerified" ])
        Assert.Equal("fsgg.coord.board-v2-inspection/1", root.GetProperty("schema").GetString())
        Assert.Equal<Set<string>>(expectedTop, names root)
        Assert.Equal(3, root.GetProperty("items").GetArrayLength())
        for item in root.GetProperty("items").EnumerateArray() do Assert.Equal<Set<string>>(expectedItem, names item)
    finally Directory.Delete(directory, true)

let private inspectProgrammeBindingInput (selectedBinding: string) (json: string option) (change: Transport.Request -> Errors.IoResult<Transport.Response> -> Errors.IoResult<Transport.Response>) =
    let directory = Path.Combine(Path.GetTempPath(), "board-v2-input-" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(directory, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute) |> ignore
    try
        let binding = Path.Combine(directory, "binding.json")
        let output = Path.Combine(directory, "report.json")
        File.WriteAllText(binding, selectedBinding)
        let transport = ProgrammeFixture.transport change
        let arguments =
            [ "inspect"; "--binding-file"; binding; "--report-file"; output ]
            @ (if json.IsSome then [ "--integrator-stdin" ] else [])
        use reader = new StringReader(json |> Option.defaultValue "must not be read")
        let code = BoardV2Application.runWithTransportAndInput (TestLock.fresh ()) transport reader arguments
        let report =
            if File.Exists output then
                Assert.Equal<UnixFileMode>(UnixFileMode.UserRead ||| UnixFileMode.UserWrite, File.GetUnixFileMode output)
                use document = JsonDocument.Parse(File.ReadAllText output)
                Some(document.RootElement.Clone())
            else None
        Assert.Empty transport.Mutations
        code, report, transport
    finally Directory.Delete(directory, true)

let private inspectProgrammeInput json change = inspectProgrammeBindingInput (ProgrammeFixture.bindingJson ()) json change

let private inspectDeclaredPopulation (rows: (string * IssueRef * string) list) (repositories: Set<string>) change =
    let selected: Binding =
        { ProgrammeFixture.binding () with
            SelectedIssues = (rows |> List.map (fun (id, issue, _) -> id, $"{issue.Owner}/{issue.Repository}#{issue.Number}") |> Map.ofList);
            Repositories = repositories }
        |> fun value -> ProgrammeFixture.enroll value rows
    let directory = Path.Combine(Path.GetTempPath(), "board-v2-native-plans-" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(directory, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute) |> ignore
    try
        let bindingPath = Path.Combine(directory, "binding.json")
        let reportPath = Path.Combine(directory, "report.json")
        File.WriteAllText(bindingPath, ProgrammeFixture.bindingJsonFor selected)
        let requests = ResizeArray<Transport.Request>()
        let transport = ProgrammeFixture.populationTransport selected rows (fun request response ->
            requests.Add request
            change request response)
        let code = BoardV2Application.runWithTransport (TestLock.fresh ()) transport [ "inspect"; "--binding-file"; bindingPath; "--report-file"; reportPath ]
        use document = JsonDocument.Parse(File.ReadAllText reportPath)
        Assert.Empty transport.Mutations
        code, document.RootElement.Clone(), List.ofSeq requests
    finally Directory.Delete(directory, true)

[<Fact>]
let ``actual CLI joins twenty enrolled issues to current native plan blobs across nine declared repositories`` () =
    let repositories =
        [ ".github"; "FS.GG.Coordination"; "FS.GG.Game"; "FS.GG.Governance"; "FS.GG.Rendering";
          "FS.GG.SC2.Client"; "FS.GG.SDD"; "FS.GG.Templates"; "FS.GG.Unity.Client" ]
    let ownerRepositories =
        [ ".github"; "FS.GG.Coordination"; "FS.GG.Game"; "FS.GG.SDD"; "FS.GG.Templates"; "FS.GG.Unity.Client" ]
    let readOnlyRepositories = [ "FS.GG.Governance"; "FS.GG.Rendering"; "FS.GG.SC2.Client" ]
    let plans: (string * string) list =
        [ (".github", ".agents/skills/work-programme/references/observation.md");
          ("FS.GG.SDD", "docs/roadmaps/tsdd-knowledge-01.md");
          ("FS.GG.Templates", "docs/roadmaps/fable-bindings-xantham-candidates.md");
          ("FS.GG.Unity.Client", "docs/roadmaps/unityc-01-reference-admission.md") ]
    let rows: (string * IssueRef * string) list =
        [ 1 .. 20 ] |> List.map (fun index ->
            let issue: IssueRef = { Owner = "FS-GG"; Repository = ownerRepositories.[(index - 1) % ownerRepositories.Length]; Number = 100 + index }
            let planRepository =
                if List.contains issue.Repository [ "FS.GG.SDD"; "FS.GG.Templates"; "FS.GG.Unity.Client" ] then issue.Repository else ".github"
            let repo, path = plans |> List.find (fun (repository, _) -> repository = planRepository)
            let reference = if repo = "FS.GG.Templates" then String.replicate 40 "a" else "main"
            "synthetic-enrolled-" + string index, issue, $"https://github.com/fs-gg/{repo}/blob/{reference}/{path}#selected-outcome")
    let scope = repositories |> List.map (fun repo -> "FS-GG/" + repo) |> Set.ofList
    for includeLegacy in [ false; true ] do
        let forms (request: Transport.Request) (response: Errors.IoResult<Transport.Response>) =
            if request.Subject.EndsWith("native observation", StringComparison.Ordinal) then
                response |> Result.map (fun (value: Transport.Response) ->
                    let root = Text.Json.Nodes.JsonNode.Parse value.Body
                    let issue = root.["data"].["repository"].["issue"]
                    let index = issue.["number"].GetValue<int>() - 100
                    let body =
                        if includeLegacy && index > 17 then "Historical narrative without an accountable six-field record"
                        elif index <= 16 then ProgrammeFixture.inlineOwnerBody.Replace("https://github.com/FS-GG/.github/pull/42", "None")
                        else ProgrammeFixture.ownerBody.Replace("https://github.com/FS-GG/.github/pull/42", "None")
                    issue.["body"] <- Text.Json.Nodes.JsonValue.Create body
                    { value with Body = root.ToJsonString() })
            else response
        let code, report, transport = inspectDeclaredPopulation rows scope forms
        Assert.Equal(0, code)
        Assert.Equal(20, report.GetProperty("items").GetArrayLength())
        let items = report.GetProperty("items").EnumerateArray() |> Seq.toArray
        let missing = items |> Array.filter (fun item -> item.GetProperty("accountableOwner").ValueKind = JsonValueKind.Null)
        Assert.Equal((if includeLegacy then 3 else 0), missing.Length)
        for item in items do
            Assert.Equal<string>("Current", item.GetProperty("sourceCurrentness").GetString())
            let provenance = item.GetProperty("sourceRevision").GetString()
            Assert.Contains("plan=FS-GG/", provenance)
            Assert.Contains("#selected-outcome", provenance)
            if item.GetProperty("accountableOwner").ValueKind <> JsonValueKind.Null then
                Assert.Equal("worker", item.GetProperty("accountableOwner").GetString())
        let ownerDecisions =
            report.GetProperty("selection").EnumerateArray()
            |> Seq.filter (fun selection -> selection.GetProperty("decision").GetString() = "needs-decision")
            |> Seq.toArray
        Assert.Equal((if includeLegacy then 3 else 0), ownerDecisions.Length)
        for selection in ownerDecisions do
            Assert.Contains("accountable owner is missing", selection.GetProperty("reasons").ToString())
        let actualOwners = rows |> List.map (fun (_, issue, _) -> issue.Repository) |> Set.ofList
        Assert.Equal<Set<string>>(Set.ofList ownerRepositories, actualOwners)
        for repository in readOnlyRepositories do
            Assert.False(actualOwners.Contains repository)
            Assert.Contains<Transport.Request>(transport, fun (request: Transport.Request) -> request.Subject = $"FS-GG/{repository} open PR inventory")
        for repository in ownerRepositories do
            let permissionPath = $"repos/FS-GG/{repository}/collaborators/author/permission"
            let decidingReads = transport |> List.filter (fun (request: Transport.Request) -> request.Method = "GET" && request.Path = permissionPath)
            Assert.True(decidingReads.Length >= 2, repository)
        let planReads = transport |> List.filter (fun (request: Transport.Request) -> request.Subject = "enrolled native owning plan")
        Assert.NotEmpty planReads
        Assert.True(transport.Length < 500)
        for request in planReads do
            match request.Body with
            | Transport.Query(document, variables) when document.Contains("object(expression:") ->
                let expression = variables |> List.pick (function "expression", Transport.VString value -> Some value | _ -> None)
                Assert.DoesNotContain("main:", expression)
                let repo = variables |> List.pick (function "repo", Transport.VString value -> Some value | _ -> None)
                let commit = if repo = "FS.GG.Templates" then String.replicate 40 "a" else ProgrammeFixture.head
                Assert.StartsWith(commit + ":", expression)
            | Transport.Query(document, variables) when document.Contains("qualifiedName:") ->
                let repo = variables |> List.pick (function "repo", Transport.VString value -> Some value | _ -> None)
                Assert.NotEqual<string>("FS.GG.Templates", repo)
            | _ -> ()

[<Fact>]
let ``actual CLI enrolled main plan content advances without enrollment republish but mid-snapshot drift refuses`` () =
    let url = "https://github.com/FS-GG/FS.GG.SDD/blob/main/docs/roadmap.md"
    let rows = [ "I_outcome", ProgrammeFixture.issue, url ]
    let repositories = Set.ofList [ "FS-GG/.github"; "FS-GG/FS.GG.SDD" ]
    let observe revision =
        inspectDeclaredPopulation rows repositories (fun (request: Transport.Request) result ->
            if request.Subject = "enrolled native owning plan" then
                result |> Result.map (fun (response: Transport.Response) -> { response with Body = response.Body.Replace(ProgrammeFixture.head, revision) })
            else result)
    let before, old, _ = observe (String.replicate 40 "1")
    let after, current, _ = observe (String.replicate 40 "2")
    Assert.Equal(0, before)
    Assert.Equal(0, after)
    let first = old.GetProperty("items").[0].GetProperty("sourceRevision").GetString()
    let second = current.GetProperty("items").[0].GetProperty("sourceRevision").GetString()
    Assert.NotEqual<string>(first, second)
    let mutable reads = 0
    let code, report, _ =
        inspectDeclaredPopulation rows repositories (fun (request: Transport.Request) result ->
            if request.Subject = "enrolled native owning plan" then
                reads <- reads + 1
                result |> Result.map (fun (response: Transport.Response) ->
                    { response with Body = response.Body.Replace(ProgrammeFixture.head, String.replicate 40 (if reads <= 2 then "1" else "2")) })
            else result)
    Assert.Equal(3, code)
    Assert.Equal<string>("Unknown", report.GetProperty("items").[0].GetProperty("sourceCurrentness").GetString())

[<Fact>]
let ``actual CLI refuses undeclared origin repository path ref and unreadable plan identity without fallback`` () =
    let invalid: string list =
        [ "https://example.invalid/FS-GG/FS.GG.SDD/blob/main/docs/roadmap.md";
          "https://github.com/FS-GG/Other/blob/main/docs/roadmap.md";
          "https://github.com/FS-GG/FS.GG.SDD/raw/main/docs/roadmap.md";
          "https://github.com/FS-GG/FS.GG.SDD/blob/feature/docs/roadmap.md";
          "https://github.com/FS-GG/FS.GG.SDD/blob/main/src/roadmap.md";
          "https://github.com/FS-GG/FS.GG.SDD/blob/main/docs/../secret.md";
          "https://github.com/FS-GG/FS.GG.SDD/blob/main/docs/%2e%2e/secret.md";
          "https://github.com/FS-GG/FS.GG.SDD/blob/main/.agents/skills/other/SKILL.md";
          "https://github.com/FS-GG/FS.GG.SDD/blob/main/docs/roadmap.md?raw=1" ]
    let repositories = Set.ofList [ "FS-GG/.github"; "FS-GG/FS.GG.SDD" ]
    for url in invalid do
        let code, report, _ = inspectDeclaredPopulation [ "I_outcome", ProgrammeFixture.issue, url ] repositories ProgrammeFixture.unchanged
        Assert.Equal(3, code)
        Assert.Equal<string>("Unknown", report.GetProperty("items").[0].GetProperty("sourceCurrentness").GetString())
    for mutation in [ "repository"; "missing"; "denied"; "revision"; "empty-blob" ] do
        let code, _, _ =
            inspectDeclaredPopulation [ "I_outcome", ProgrammeFixture.issue, "https://github.com/FS-GG/FS.GG.SDD/blob/main/docs/roadmap.md" ] repositories
                (fun (request: Transport.Request) result ->
                    if request.Subject <> "enrolled native owning plan" then result
                    elif mutation = "denied" then Error(Errors.Http(403, "synthetic denied"))
                    else result |> Result.map (fun (response: Transport.Response) ->
                        let body =
                            match mutation with
                            | "repository" -> response.Body.Replace("FS-GG/FS.GG.SDD", "FS-GG/Other")
                            | "revision" -> response.Body.Replace(ProgrammeFixture.head, "not-a-native-commit")
                            | "empty-blob" -> response.Body.Replace("Synthetic current owning plan", "")
                            | _ -> "{\"data\":{\"repository\":null}}"
                        { response with Body = body }))
        Assert.Equal(3, code)

let private observedContext (change: Transport.Request -> Errors.IoResult<Transport.Response> -> Errors.IoResult<Transport.Response>) =
    V2ProjectionSource.inspectFixed (ProgrammeFixture.transport change) (ProgrammeFixture.binding ()) None
    |> ProgrammeFixture.context

let private inputSelections (root: JsonElement) = root.GetProperty("selection").EnumerateArray() |> Seq.toArray

[<Fact>]
let ``actual stdin caller joins attributed observations and folds independent outcome slices within remaining capacity`` () =
    let context = observedContext ProgrammeFixture.newAssignment
    let first = context.Slices.Head
    let slices =
        [ { first with Id = "implementation-one"; Owner = "implementation-owner"; Paths = [ "src/one.fs" ] };
          { first with Id = "implementation-two"; Owner = "second-owner"; Paths = [ "src/two.fs" ] };
          { first with Id = "implementation-three"; Owner = "third-owner"; Paths = [ "src/three.fs" ] } ]
    let input = { context with Slices = slices; ImplementationSlots = Some 1; ReviewSlots = Some 0; HostedCiSlots = None }
    let json = ProgrammeFixture.integratorJson input
    let code, report, _ = inspectProgrammeInput (Some json) ProgrammeFixture.newAssignment
    Assert.Equal(0, code)
    let root = report.Value
    let selected = inputSelections root
    Assert.Equal<string list>([ "ready"; "deferred"; "deferred" ], selected |> Array.map (fun value -> value.GetProperty("decision").GetString()) |> Array.toList)
    Assert.Equal<string list>(slices |> List.map _.Id, selected |> Array.map (fun value -> value.GetProperty("sliceId").GetString()) |> Array.toList)
    Assert.Equal("implementation-owner", selected.[0].GetProperty("owner").GetString())
    Assert.Equal("integrator-reported", root.GetProperty("integratorObservation").GetProperty("provenance").GetString())
    let digest = SHA256.HashData(Text.Encoding.UTF8.GetBytes json) |> Convert.ToHexStringLower
    Assert.Equal(digest, root.GetProperty("integratorObservation").GetProperty("inputSha256").GetString())
    for value in selected do Assert.False(value.GetProperty("dispatchAuthority").GetBoolean())
    let alternative = { first with Id = "unselected-alternative"; Paths = [ "src/one.fs" ]; IntakeAuthorized = Some false }
    let independent = { input with Slices = [ alternative; slices.Head ] }
    let _, second, _ = inspectProgrammeInput (Some(ProgrammeFixture.integratorJson independent)) ProgrammeFixture.newAssignment
    Assert.Equal<string list>([ "blocked"; "ready" ], inputSelections second.Value |> Array.map (fun value -> value.GetProperty("decision").GetString()) |> Array.toList)
    let existing = observedContext ProgrammeFixture.unchanged
    for operation, resource in [ (ReviewWork, "review"); (QualifyWork, "ci") ] do
        let slice = { existing.Slices.Head with Operation = operation; Paths = [ "src/current.fs" ] }
        let available =
            { existing with
                Slices = [ slice ]
                ImplementationSlots = Some 0
                ReviewSlots = if resource = "review" then Some 1 else None
                HostedCiSlots = if resource = "ci" then Some 1 else None }
        let _, ready, _ = inspectProgrammeInput (Some(ProgrammeFixture.integratorJson available)) ProgrammeFixture.unchanged
        Assert.Equal("ready", (inputSelections ready.Value).[0].GetProperty("decision").GetString())
        let exhausted = { available with ReviewSlots = Some 0; HostedCiSlots = Some 0 }
        let _, deferred, _ = inspectProgrammeInput (Some(ProgrammeFixture.integratorJson exhausted)) ProgrammeFixture.unchanged
        Assert.Equal("deferred", (inputSelections deferred.Value).[0].GetProperty("decision").GetString())

[<Fact>]
let ``actual stdin caller refuses stale native joins and original per group expiry without renewing evidence`` () =
    let context = observedContext ProgrammeFixture.newAssignment
    let slice = context.Slices.Head
    let old = slice.Observation.ObservedAt.AddMinutes(-6.0)
    let expired = { slice.Observation with ObservedAt = old; ValidUntil = old.AddMinutes 1.0 }
    let future = { slice.Observation with ObservedAt = DateTimeOffset.UtcNow.AddMinutes 1.0; ValidUntil = DateTimeOffset.UtcNow.AddMinutes 2.0 }
    let cases: SelectionContext list =
        [ { context with Slices = [ { slice with Join = { slice.Join with BodySha256 = String.replicate 64 "f" } } ] };
          { context with Slices = [ { slice with Join = { slice.Join with UpdatedAt = "2026-10-17T01:00:00Z" } } ] };
          { context with Slices = [ { slice with Join = { slice.Join with SourceRevision = "old owning-plan revision" } } ] };
          { context with Slices = [ { slice with Observation = expired } ] };
          { context with Slices = [ { slice with Observation = future } ] };
          { context with Slices = [ { slice with PrerequisitesComplete = false } ] };
          { context with Slices = [ { slice with IntakeAuthorized = None } ] };
          { context with CapacityObservation = Some expired };
          { context with ReservationObservation = Some expired };
          { context with CapacityMeaning = "total-before-active" };
          { context with EnrollmentBlob = String.replicate 40 "f" } ]
    for candidate in cases do
        let _, report, _ = inspectProgrammeInput (Some(ProgrammeFixture.integratorJson candidate)) ProgrammeFixture.newAssignment
        let selected = (inputSelections report.Value).[0]
        Assert.Equal("unknown", selected.GetProperty("decision").GetString())
    let changedPlan (request: Transport.Request) (response: Errors.IoResult<Transport.Response>) =
        let response = ProgrammeFixture.newAssignment request response
        if request.Subject.EndsWith("programme.md", StringComparison.Ordinal) then
            response |> Result.map (fun value -> { value with Body = value.Body.Replace(ProgrammeFixture.head, String.replicate 40 "f") })
        else response
    let _, changed, _ = inspectProgrammeInput (Some(ProgrammeFixture.integratorJson context)) changedPlan
    Assert.Equal("unknown", (inputSelections changed.Value).[0].GetProperty("decision").GetString())

[<Fact>]
let ``actual stdin caller keeps original holds and matches self by exact attempt operation PR and head`` () =
    let context = observedContext ProgrammeFixture.unchanged
    let slice = { context.Slices.Head with Operation = Implement; Paths = [ "src/current.fs" ] }
    let hold: Reservation =
        { SliceId = slice.Id; Attempt = slice.Attempt; Operation = slice.Operation; Owner = slice.Owner
          Repository = slice.Repository; Paths = slice.Paths; PullRequest = slice.PullRequest; Head = slice.Head; CustodyReference = "original-operation/439" }
    let current = { context with Slices = [ slice ]; Reservations = Some [ hold ] }
    let _, exact, _ = inspectProgrammeInput (Some(ProgrammeFixture.integratorJson current)) ProgrammeFixture.unchanged
    Assert.Equal("ready", (inputSelections exact.Value).[0].GetProperty("decision").GetString())
    let holds: Reservation list =
        [ { hold with Attempt = "different-attempt" };
          { hold with Operation = ReviewWork };
          { hold with Head = Some(String.replicate 40 "f") };
          { hold with PullRequest = Some "https://github.com/FS-GG/.github/pull/43" } ]
    for other in holds do
        let _, report, _ = inspectProgrammeInput (Some(ProgrammeFixture.integratorJson { current with Reservations = Some [ other ] })) ProgrammeFixture.unchanged
        Assert.Equal("blocked", (inputSelections report.Value).[0].GetProperty("decision").GetString())
    let expired = { context.ReservationObservation.Value with ObservedAt = context.ObservedAt.AddMinutes(-6.0); ValidUntil = context.ObservedAt.AddMinutes(-5.0) }
    let _, retained, _ = inspectProgrammeInput (Some(ProgrammeFixture.integratorJson { current with Reservations = Some [ { hold with Attempt = "uncertain-original" } ]; ReservationObservation = Some expired })) ProgrammeFixture.unchanged
    Assert.Equal("blocked", (inputSelections retained.Value).[0].GetProperty("decision").GetString())
    let readOnly = { context with ImplementationSlots = Some 0; ReviewSlots = None; HostedCiSlots = None; CapacityObservation = None; Reservations = None; ReservationObservation = None }
    let _, continuation, _ = inspectProgrammeInput (Some(ProgrammeFixture.integratorJson readOnly)) ProgrammeFixture.unchanged
    Assert.Equal("continue-existing", (inputSelections continuation.Value).[0].GetProperty("decision").GetString())

[<Fact>]
let ``actual stdin closed parser refuses duplicate unknown malformed oversized and wrong binding input before native reads`` () =
    let context = observedContext ProgrammeFixture.newAssignment
    let valid = ProgrammeFixture.integratorJson context
    let invalid: string list =
        [ valid.Insert(1, "\"schema\":\"duplicate\",");
          valid.Insert(1, "\"authority\":\"root\",");
          valid + "{}";
          valid.Replace("\"schemaVersion\":2", "\"schemaVersion\":999");
          valid.Replace("inert-invocation", String.replicate 129 "x");
          valid.Replace("remaining-after-active", "not-an-authority").Replace("\"implementation\":1", "\"implementation\":-1");
          valid.Replace("\"repository\":\"FS-GG/.github\"", "\"repository\":\"https://evil.invalid/path\"");
          valid.Replace("\"id\":\"source-slice\"", "\"id\":\"source-slice\",\"id\":\"again\"");
          String.replicate 262145 "x" ]
    for json in invalid do
        let code, report, transport = inspectProgrammeInput (Some json) ProgrammeFixture.newAssignment
        Assert.Equal(1, code)
        Assert.True(report.IsNone)
        Assert.Equal(0, transport.GraphQlCalls)
        Assert.Equal(0, transport.RestCalls)
    let negative = { context with ImplementationSlots = Some -1 }
    Assert.True(BoardV2Application.parseIntegratorObservations (ProgrammeFixture.binding ()) (ProgrammeFixture.integratorJson negative) |> Result.isError)

let private currentDeliverableBody (body: string) (request: Transport.Request) (response: Errors.IoResult<Transport.Response>) =
    if request.Subject.EndsWith("native observation", StringComparison.Ordinal) then
        response |> Result.map (fun value ->
            { value with Body = value.Body.Replace(JsonSerializer.Serialize ProgrammeFixture.ownerBody, JsonSerializer.Serialize body) })
    else response

[<Fact>]
let ``actual stdin caller keeps source eligibility with typed review check availability while aggregate remains incomplete`` () =
    for denied in [ "head checks"; "reviews"; "both" ] do
        let change (request: Transport.Request) (result: Errors.IoResult<Transport.Response>) =
            let selected = request.Subject.EndsWith("head checks", StringComparison.Ordinal) || request.Subject.EndsWith("reviews", StringComparison.Ordinal)
            if selected && (denied = "both" || request.Subject.EndsWith(denied, StringComparison.Ordinal)) then
                // Actual GraphQL shape: errors with partial/null check nodes must never be decoded.
                Ok { Status = 200; Headers = Map.empty; ETag = None; NextLink = None;
                     Body = "{\"data\":{\"repository\":{\"nameWithOwner\":\"FS-GG/.github\",\"object\":{\"oid\":\"wrong-partial-head\",\"statusCheckRollup\":{\"contexts\":{\"nodes\":[null]}}}}},\"errors\":[{\"type\":\"FORBIDDEN\",\"message\":\"Resource not accessible by integration\"}]}" }
            else result
        let context = observedContext change
        for operation in [ ObserveExisting; Implement; ReviewWork; QualifyWork; ActivateWork ] do
            let slice =
                { context.Slices.Head with Operation = operation;
                                           Action = (if operation = ActivateWork then Activation else Source) }
            let input =
                { context with Slices = [ slice ];
                               ImplementationSlots = (if operation = ObserveExisting then None else Some 1);
                               ReviewSlots = (if operation = ObserveExisting then None else Some 1);
                               HostedCiSlots = (if operation = ObserveExisting then None else Some 1) }
            let _, report, transport = inspectProgrammeInput (Some(ProgrammeFixture.integratorJson input)) change
            let selected = (inputSelections report.Value).[0]
            let expected = if operation = ObserveExisting then "continue-existing" elif operation = Implement then "ready" else "unknown"
            Assert.Equal<string>(expected, selected.GetProperty("decision").GetString())
            Assert.False(selected.GetProperty("dispatchAuthority").GetBoolean())
            let native = report.Value.GetProperty("nativeIntegrator").GetProperty("pullRequests").[0]
            Assert.True(native.GetProperty("ScopeCurrent").GetBoolean())
            Assert.NotEmpty(native.GetProperty("Gaps").EnumerateArray())
            Assert.NotEmpty(native.GetProperty("GapDetails").EnumerateArray())
            Assert.Equal(JsonValueKind.Null, report.Value.GetProperty("candidates").[0].GetProperty("openPullRequests").ValueKind)
            Assert.Empty transport.Mutations

[<Fact>]
let ``actual stdin caller never ignores native scope association identity head or files failures`` () =
    let cases: (string * (Transport.Request -> Errors.IoResult<Transport.Response> -> Errors.IoResult<Transport.Response>)) list =
        [ ("review identity", fun (request: Transport.Request) (result: Errors.IoResult<Transport.Response>) ->
              result |> Result.map (fun (response: Transport.Response) -> if request.Subject.EndsWith("reviews", StringComparison.Ordinal) then { response with Body = response.Body.Replace(ProgrammeFixture.head, String.replicate 40 "f") } else response));
          ("check repository", fun (request: Transport.Request) (result: Errors.IoResult<Transport.Response>) ->
              result |> Result.map (fun (response: Transport.Response) -> if request.Subject.EndsWith("head checks", StringComparison.Ordinal) then { response with Body = response.Body.Replace("FS-GG/.github", "FS-GG/Other") } else response));
          ("check head", fun (request: Transport.Request) (result: Errors.IoResult<Transport.Response>) ->
              result |> Result.map (fun (response: Transport.Response) -> if request.Subject.EndsWith("head checks", StringComparison.Ordinal) then { response with Body = response.Body.Replace(ProgrammeFixture.head, String.replicate 40 "f") } else response));
          ("files", fun (request: Transport.Request) (result: Errors.IoResult<Transport.Response>) -> if request.Subject.EndsWith(" files", StringComparison.Ordinal) then Error(Errors.Http(403, "synthetic files denial")) else result);
          ("association", fun (request: Transport.Request) (result: Errors.IoResult<Transport.Response>) -> if request.Subject.EndsWith("closingIssuesReferences", StringComparison.Ordinal) then Error(Errors.Http(403, "synthetic association denial")) else result);
          ("final head", fun (request: Transport.Request) (result: Errors.IoResult<Transport.Response>) -> if request.Subject.EndsWith("final head barrier", StringComparison.Ordinal) then Error(Errors.Http(403, "synthetic final head denial")) else result);
          ("Current deliverable", fun (request: Transport.Request) (result: Errors.IoResult<Transport.Response>) -> if request.Subject.EndsWith("Current deliverable native join", StringComparison.Ordinal) then Error(Errors.Http(403, "synthetic native join denial")) else result) ]
    for name, change in cases do
        let context = observedContext ProgrammeFixture.unchanged
        for operation in [ ObserveExisting; Implement ] do
            let input = { context with Slices = [ { context.Slices.Head with Operation = operation } ] }
            let _, report, transport = inspectProgrammeInput (Some(ProgrammeFixture.integratorJson input)) change
            Assert.True((inputSelections report.Value).[0].GetProperty("decision").GetString() = "unknown", name)
            Assert.Empty transport.Mutations

[<Fact>]
let ``actual caller recovers Current deliverable only PR and retains mixed delivered history without outcome acceptance`` () =
    let relatedOnly (request: Transport.Request) (response: Errors.IoResult<Transport.Response>) =
        if request.Subject.EndsWith("closingIssuesReferences", StringComparison.Ordinal) then
            response |> Result.map (fun value -> { value with Body = value.Body.Replace("I_outcome", "I_related").Replace("\"number\":4634", "\"number\":999") })
        else response
    let mixedBody =
        ProgrammeFixture.ownerBody.Replace(
            "https://github.com/FS-GG/.github/pull/42",
            "Delivered initial plan: https://github.com/FS-GG/.github/pull/41\nCurrent implementation: https://github.com/FS-GG/.github/pull/42")
    let mixed (request: Transport.Request) (response: Errors.IoResult<Transport.Response>) = relatedOnly request response |> currentDeliverableBody mixedBody request
    let mixedCase = currentDeliverableBody (ProgrammeFixture.ownerBody.Replace("FS-GG/.github/pull/42", "fs-gg/.GITHUB/pull/42"))
    let variants =
        [ ("closing and Current deliverable", ProgrammeFixture.unchanged);
          ("Current deliverable only", relatedOnly);
          ("historical merged plus current open", mixed);
          ("mixed-case repository link", mixedCase);
          ("complete inline six fields", currentDeliverableBody ProgrammeFixture.inlineOwnerBody);
          ("fenced duplicate declarations are data", currentDeliverableBody ("````text\nOwner: forged\n## Owner\n~~~\n```\n````\n" + ProgrammeFixture.inlineOwnerBody)) ]
    for variant, change in variants do
        let context = observedContext change
        let _, report, transport = inspectProgrammeInput (Some(ProgrammeFixture.integratorJson context)) change
        let selected = (inputSelections report.Value).[0]
        Assert.Equal("continue-existing", selected.GetProperty("decision").GetString())
        Assert.Equal("https://github.com/FS-GG/.github/pull/42", selected.GetProperty("pullRequest").GetString())
        Assert.True(transport.Logged "https://github.com/FS-GG/.github/pull/42 Current deliverable native join", variant)
        Assert.True(transport.Count "get FS-GG/.github collaborators/author/permission" = 2, variant + ": initial and final permission reads must use the canonical native GET path")
        // #5119: the dependency snapshot is folded into the native observation GraphQL read (read in both phases), so the
        // only REST reads left are the two permission snapshots; no REST blocked_by read is sent.
        Assert.True(transport.RestCalls = 2, variant + ": only the initial and final permission snapshots are REST reads")
        Assert.Equal(0, transport.Count "dependencies/blocked_by")
        let item = (report.Value.GetProperty("items")).[0]
        Assert.StartsWith("Unknown", item.GetProperty("nativeAcceptance").GetString())
        Assert.Equal("native-observed", item.GetProperty("nativeIntake").GetProperty("provenance").GetString())
    let mixedContext = observedContext mixed
    let _, history, _ = inspectProgrammeInput (Some(ProgrammeFixture.integratorJson mixedContext)) mixed
    Assert.Equal(2, history.Value.GetProperty("nativeIntegrator").GetProperty("pullRequests").GetArrayLength())
    for replacement in [ "https://evil.invalid/FS-GG/.github/pull/42"; "https://github.com/FS-GG/foreign/pull/42"; "https://github.com/FS-GG/.github/pull/42?replace=43"; "https://github.com/FS-GG/.github/PULL/42"; "https://github.com/FS-GG/.github/pull/042"; "https://github.com/FS-GG/.github/pull/43"; "HTTPS://github.com/FS-GG/.github/pull/42"; "https://github.com/FS-GG/.github/pulls/42"; "https://github.com/FS-GG/.github/pull%2F42"; "ftp://github.com/FS-GG/.github/pull/42"; "github.com/FS-GG/.github/pull/42"; "https://github.com/FS-GG/.github/%70ull/42"; "github.com/FS-GG/.github/%70ull/42"; "//github.com/FS-GG/.github/pull/42"; "/github.com/FS-GG/.github/pull/42"; "https%3A%2F%2Fgithub.com%2FFS-GG%2F.github%2Fpull%2F42"; "//github.com/FS-GG/.github/%70ull/42"; "https:/github.com/FS-GG/.github/pull/42"; "https://github.com@evil.invalid/FS-GG/.github/pull/42"; "https://github.com.evil.invalid/FS-GG/.github/pull/42"; "https://github.com:bad/FS-GG/.github/pull/42" ] do
        let body = ProgrammeFixture.ownerBody.Replace("https://github.com/FS-GG/.github/pull/42", replacement)
        let change = currentDeliverableBody body
        let context = observedContext change
        let _, report, _ = inspectProgrammeInput (Some(ProgrammeFixture.integratorJson context)) change
        Assert.Equal("unknown", (inputSelections report.Value).[0].GetProperty("decision").GetString())

[<Fact>]
let ``actual caller retains declared work while discovering only canonical PR references`` () =
    let declarations: (string * bool) list =
        [ ("None", false);
          ("Investigate the current source contract", false);
          ("Retain the original release operation: https://github.com/FS-GG/.github/releases/tag/synthetic", false);
          ("None plus retained work is still declared work", false);
          ("Existing PR", false);
          ("Artifact protocol reference https://artifacts.example/pull/protocol", false);
          ("Artifact selected-route protocol https://artifacts.example/FS-GG/.github/pull/protocol", false);
          ("Artifact source file https://github.com/FS-GG/.github/blob/main/docs/pull/protocol.md", false);
          ("Historical plan https://github.com/FS-GG/.github/pull/41 current source investigation remains independent", false);
          ("Source https://github.com/FS-GG/.github/pull/42\nReceipt evidence https://github.com/FS-GG/.github/issues/1 https://github.com/FS-GG/.github/actions/runs/1 https://github.com/FS-GG/.github/releases/tag/synthetic", true) ]
    for declaration, hasReference in declarations do
        for inlineForm in [ false; true ] do
            let template = if inlineForm then ProgrammeFixture.inlineOwnerBody else ProgrammeFixture.ownerBody
            let body = template.Replace("https://github.com/FS-GG/.github/pull/42", declaration)
            let requests = ResizeArray<Transport.Request>()
            let change (request: Transport.Request) (response: Errors.IoResult<Transport.Response>) =
                requests.Add request
                let changed = currentDeliverableBody body request response
                if hasReference then changed else ProgrammeFixture.newAssignment request changed
            let context = observedContext change
            let _, report, transport = inspectProgrammeInput (Some(ProgrammeFixture.integratorJson context)) change
            let root = report.Value
            let current = (root.GetProperty("items")).[0].GetProperty("currentDeliverable")
            Assert.Equal(declaration, current.GetProperty("declaration").GetString())
            Assert.Equal("native-observed", current.GetProperty("provenance").GetString())
            Assert.Equal(declaration <> "None", current.GetProperty("reconciliationRequired").GetBoolean())
            Assert.Equal(JsonValueKind.Null, current.GetProperty("gap").ValueKind)
            let historicalReference = declaration.Contains("/pull/41", StringComparison.Ordinal)
            Assert.Equal((if hasReference || historicalReference then 1 else 0), current.GetProperty("references").GetArrayLength())
            if historicalReference then
                Assert.True(transport.Logged "https://github.com/FS-GG/.github/pull/41 Current deliverable native join")
                Assert.Equal(JsonValueKind.Null, (inputSelections root).[0].GetProperty("pullRequest").ValueKind)
            Assert.Equal((if hasReference then "continue-existing" else "ready"), (inputSelections root).[0].GetProperty("decision").GetString())
            Assert.Equal(hasReference, transport.Logged "https://github.com/FS-GG/.github/pull/42 Current deliverable native join")
            Assert.DoesNotContain<Transport.Request>(requests, fun request -> request.Subject.Contains("actions/runs/1") || request.Path.Contains("releases/tag/synthetic"))
            Assert.Empty transport.Mutations
            if declaration <> "None" then
                let slice = { context.Slices.Head with Prerequisites = [] }
                let _, absent, _ = inspectProgrammeInput (Some(ProgrammeFixture.integratorJson { context with Slices = [ slice ] })) change
                Assert.Equal("unknown", (inputSelections absent.Value).[0].GetProperty("decision").GetString())
                Assert.Contains("current-deliverable reconciliation", (inputSelections absent.Value).[0].GetProperty("reasons").ToString())

[<Fact>]
let ``actual caller refuses empty and oversized declarations without replacing them with None`` () =
    for declaration in [ ""; "   "; String.replicate (16 * 1024 + 1) "x" ] do
        let body = ProgrammeFixture.ownerBody.Replace("https://github.com/FS-GG/.github/pull/42", declaration)
        let change = currentDeliverableBody body
        let context = observedContext change
        let _, report, _ = inspectProgrammeInput (Some(ProgrammeFixture.integratorJson context)) change
        let field = (report.Value.GetProperty("items")).[0].GetProperty("currentDeliverable")
        Assert.False(field.GetProperty("declaration").GetString() = "None")
        Assert.Equal(JsonValueKind.String, field.GetProperty("gap").ValueKind)
        Assert.Equal("unknown", (inputSelections report.Value).[0].GetProperty("decision").GetString())

[<Fact>]
let ``actual caller reservation input preserves unknown versus explicit empty observations`` () =
    let context = observedContext ProgrammeFixture.newAssignment
    let cases: (Reservation list option * AttributedObservation option * JsonValueKind * string) list =
        [ (None, context.ReservationObservation, JsonValueKind.Null, "unknown");
          (Some [], None, JsonValueKind.Null, "unknown");
          (None, None, JsonValueKind.Null, "unknown");
          (Some [], context.ReservationObservation, JsonValueKind.Object, "ready") ]
    for items, observation, expectedKind, expectedDecision in cases do
        let input = { context with Reservations = items; ReservationObservation = observation }
        let json = ProgrammeFixture.integratorJson input
        use document = JsonDocument.Parse json
        let encoded = document.RootElement.GetProperty("reservations")
        Assert.Equal(expectedKind, encoded.ValueKind)
        let decoded = BoardV2Application.parseIntegratorObservations (ProgrammeFixture.binding ()) json |> Result.defaultWith failwith
        if expectedKind = JsonValueKind.Null then
            Assert.True(decoded.Reservations.IsNone)
            Assert.True(decoded.ReservationObservation.IsNone)
        else
            Assert.True(decoded.Reservations.IsSome)
            Assert.Empty decoded.Reservations.Value
            Assert.True(decoded.ReservationObservation.IsSome)
            Assert.Equal(0, encoded.GetProperty("items").GetArrayLength())
        let _, report, _ = inspectProgrammeInput (Some json) ProgrammeFixture.newAssignment
        Assert.Equal(expectedDecision, (inputSelections report.Value).[0].GetProperty("decision").GetString())
    let reconciliation: Prerequisite = { Action = Source; Reference = "current-deliverable"; Satisfied = None; SupplyingOwner = "programme-owner" }
    let input = { context with Slices = [ { context.Slices.Head with Prerequisites = [ reconciliation ] } ] }
    let json = ProgrammeFixture.integratorJson input
    use document = JsonDocument.Parse json
    let sliceNode = (document.RootElement.GetProperty("slices")).[0]
    Assert.Equal(JsonValueKind.Null, (sliceNode.GetProperty("prerequisites")).[0].GetProperty("satisfied").ValueKind)
    let decoded = BoardV2Application.parseIntegratorObservations (ProgrammeFixture.binding ()) json |> Result.defaultWith failwith
    Assert.True(decoded.Slices.Head.Prerequisites.Head.Satisfied.IsNone)
    Assert.True(decoded.Slices.Head.PullRequest.IsNone)
    Assert.Equal(JsonValueKind.Null, sliceNode.GetProperty("pullRequest").ValueKind)

[<Fact>]
let ``actual caller binds current work reconciliation to action native body scope and original holds`` () =
    let declaration = "Retain original operation custody and investigate independent source"
    let body = ProgrammeFixture.ownerBody.Replace("https://github.com/FS-GG/.github/pull/42", declaration)
    let change (request: Transport.Request) (response: Errors.IoResult<Transport.Response>) =
        currentDeliverableBody body request response |> ProgrammeFixture.newAssignment request
    let context = observedContext change
    let slice = context.Slices.Head
    let reconciliation = slice.Prerequisites.Head
    let choose (input: SelectionContext) =
        let _, report, _ = inspectProgrammeInput (Some(ProgrammeFixture.integratorJson input)) change
        (inputSelections report.Value).[0]
    let variants: (string * CurrentSlice * string) list =
        [ ("missing", { slice with Prerequisites = [] }, "unknown");
          ("null", { slice with Prerequisites = [ { reconciliation with Satisfied = None } ] }, "unknown");
          ("wrong action", { slice with Prerequisites = [ { reconciliation with Action = Activation } ] }, "unknown");
          ("duplicate", { slice with Prerequisites = [ reconciliation; reconciliation ] }, "unknown");
          ("false", { slice with Prerequisites = [ { reconciliation with Satisfied = Some false } ] }, "blocked");
          ("wrong body", { slice with Join = { slice.Join with BodySha256 = String.replicate 64 "f" } }, "unknown");
          ("wrong revision", { slice with Join = { slice.Join with UpdatedAt = "2000-01-01T00:00:00Z" } }, "unknown");
          ("stale attribution", { slice with Observation = { slice.Observation with ObservedAt = slice.Observation.ObservedAt.AddMinutes(-6.0); ValidUntil = slice.Observation.ObservedAt.AddMinutes(-5.0) } }, "unknown") ]
    for name, candidate, expected in variants do
        Assert.True((choose { context with Slices = [ candidate ] }).GetProperty("decision").GetString() = expected, name)
    let missingSupplier = { slice with Prerequisites = [ { reconciliation with SupplyingOwner = "" } ] }
    let refusedCode, refusedReport, refusedTransport = inspectProgrammeInput (Some(ProgrammeFixture.integratorJson { context with Slices = [ missingSupplier ] })) change
    Assert.Equal(1, refusedCode)
    Assert.True(refusedReport.IsNone)
    Assert.Equal(0, refusedTransport.GraphQlCalls)
    Assert.Equal(0, refusedTransport.RestCalls)
    let activationHold: Prerequisite = { Action = Activation; Reference = "original-operation/current-custody"; Satisfied = Some false; SupplyingOwner = "operation-owner" }
    let independent = { slice with Prerequisites = activationHold :: slice.Prerequisites }
    Assert.Equal("ready", (choose { context with Slices = [ independent ] }).GetProperty("decision").GetString())
    let sourceHold = { independent with Prerequisites = { activationHold with Action = Source } :: slice.Prerequisites }
    Assert.Equal("blocked", (choose { context with Slices = [ sourceHold ] }).GetProperty("decision").GetString())
    let hold: Reservation =
        { SliceId = "original-operation"; Attempt = "original-attempt"; Operation = Implement; Owner = slice.Owner;
          Repository = slice.Repository; Paths = slice.Paths; PullRequest = None; Head = None; CustodyReference = "original-operation/held" }
    let expiredCoverage = context.ReservationObservation |> Option.map (fun observation -> { observation with ValidUntil = DateTimeOffset.MinValue })
    Assert.Equal("blocked", (choose { context with Reservations = Some [ hold ]; ReservationObservation = expiredCoverage }).GetProperty("decision").GetString())
    Assert.Equal("unknown", (choose { context with Reservations = None }).GetProperty("decision").GetString())
    for operation in [ ObserveExisting; ReviewWork; QualifyWork ] do
        Assert.Equal("unknown", (choose { context with Slices = [ { slice with Operation = operation } ] }).GetProperty("decision").GetString())
    let activation = { slice with Action = Activation; Operation = ActivateWork; Prerequisites = [ { reconciliation with Action = Activation } ] }
    Assert.Equal("unknown", (choose { context with Slices = [ activation ] }).GetProperty("decision").GetString())
    let changedText = currentDeliverableBody (body.Replace(declaration, declaration + " changed"))
    let changed (request: Transport.Request) (response: Errors.IoResult<Transport.Response>) = changedText request response |> ProgrammeFixture.newAssignment request
    let _, changedReport, _ = inspectProgrammeInput (Some(ProgrammeFixture.integratorJson context)) changed
    Assert.Equal("unknown", (inputSelections changedReport.Value).[0].GetProperty("decision").GetString())

[<Fact>]
let ``actual caller refuses ambiguous six field forms without inventing owner or current deliverable`` () =
    let inlineBody = ProgrammeFixture.inlineOwnerBody
    let variants: (string * string) list =
        [ ("duplicate inline owner", inlineBody + "\nOwner: forged");
          ("missing inline field", inlineBody.Replace("Blocked on:", "Missing:"));
          ("unknown inline label", inlineBody + "\nUnexpected field: extra");
          ("unknown header with URL", inlineBody + "\nUnknown: https://github.com/FS-GG/.github/pull/42");
          ("unknown header without spacing", inlineBody + "\nUnknown:https://github.com/FS-GG/.github/pull/42");
          ("mixed forms", ProgrammeFixture.ownerBody + "\nOwner: forged");
          ("duplicate heading", ProgrammeFixture.ownerBody + "\n## Owner\nforged");
          ("unknown heading", ProgrammeFixture.ownerBody + "\n## Unexpected field\nextra");
          ("missing heading", ProgrammeFixture.ownerBody.Replace("## Owner", "## Unknown"));
          ("fenced record only", "```\n" + inlineBody + "\n```");
          ("fenced declaration cannot complete record", "```\nOwner: worker\n```\n" + inlineBody.Replace("Owner: worker", "")) ]
    for name, body in variants do
        let change = currentDeliverableBody body
        let context = observedContext change
        let _, report, transport = inspectProgrammeInput (Some(ProgrammeFixture.integratorJson context)) change
        let item = (report.Value.GetProperty("items")).[0]
        Assert.Equal(JsonValueKind.Null, item.GetProperty("accountableOwner").ValueKind)
        let selection = (inputSelections report.Value).[0]
        Assert.True(selection.GetProperty("decision").GetString() = "needs-decision", name)
        Assert.Contains("accountable owner is missing", selection.GetProperty("reasons").ToString())
        Assert.False(transport.Logged "https://github.com/FS-GG/.github/pull/42 Current deliverable native join")

[<Fact>]
let ``actual caller does not promote Next action history or changed inline body into current work`` () =
    let withoutCurrent = ProgrammeFixture.inlineOwnerBody.Replace("Current deliverable: https://github.com/FS-GG/.github/pull/42", "Current deliverable: None")
    let body = withoutCurrent.Replace("Next action: Current source", "Next action: https://github.com/FS-GG/.github/pull/42")
    let noClosing (request: Transport.Request) (response: Errors.IoResult<Transport.Response>) =
        let changed = currentDeliverableBody body request response
        if request.Subject.EndsWith("closingIssuesReferences", StringComparison.Ordinal) then
            changed |> Result.map (fun (value: Transport.Response) -> { value with Body = value.Body.Replace("I_outcome", "I_related").Replace("\"number\":4634", "\"number\":999") })
        else changed
    let native = V2ProjectionSource.inspectFixed (ProgrammeFixture.transport noClosing) (ProgrammeFixture.binding ()) None
    Assert.Empty native.Items.Head.Deliverables
    Assert.True(native.Items.Head.DeliverableGap.IsNone)
    let current = ProgrammeFixture.context native
    let known = (observedContext ProgrammeFixture.unchanged).Slices.Head
    let context =
        { current with Slices = [ { current.Slices.Head with Operation = ObserveExisting; PullRequest = known.PullRequest; PullRequestId = known.PullRequestId; PullRequestUpdatedAt = known.PullRequestUpdatedAt; Head = known.Head } ] }
    let _, report, transport = inspectProgrammeInput (Some(ProgrammeFixture.integratorJson context)) noClosing
    Assert.Equal("needs-decision", (inputSelections report.Value).[0].GetProperty("decision").GetString())
    Assert.False(transport.Logged "https://github.com/FS-GG/.github/pull/42 Current deliverable native join")
    let mutable reads = 0
    let changedBody (request: Transport.Request) (response: Errors.IoResult<Transport.Response>) =
        if request.Subject.EndsWith("native observation", StringComparison.Ordinal) then
            reads <- reads + 1
            currentDeliverableBody (if reads = 1 then ProgrammeFixture.inlineOwnerBody else ProgrammeFixture.inlineOwnerBody.Replace("Owner: worker", "Owner: changed")) request response
        else response
    let code, changed, _ = inspectProgrammeInput None changedBody
    Assert.Equal(3, code)
    Assert.NotEqual<string>("Current", (changed.Value.GetProperty("items")).[0].GetProperty("sourceCurrentness").GetString())
    Assert.Equal("unknown", (inputSelections changed.Value).[0].GetProperty("decision").GetString())

[<Fact>]
let ``actual native caller refuses conflicting open deliverables changed heads and native intake read gaps`` () =
    let ambiguous (request: Transport.Request) (response: Errors.IoResult<Transport.Response>) =
        let body = ProgrammeFixture.ownerBody.Replace("https://github.com/FS-GG/.github/pull/42", "https://github.com/FS-GG/.github/pull/42\nhttps://github.com/FS-GG/.github/pull/43")
        let response = currentDeliverableBody body request response
        if request.Subject.EndsWith("open PR inventory", StringComparison.Ordinal) then
            response |> Result.map (fun value ->
                let root = Text.Json.Nodes.JsonNode.Parse value.Body
                let repository = root.["data"].["repository"]
                let inventory = repository.["pullRequests"]
                let nodes = inventory.["nodes"].AsArray()
                let second = nodes.[0].DeepClone()
                second.["id"] <- Text.Json.Nodes.JsonValue.Create("PR_second")
                second.["number"] <- Text.Json.Nodes.JsonValue.Create(43)
                second.["url"] <- Text.Json.Nodes.JsonValue.Create("https://github.com/FS-GG/.github/pull/43")
                nodes.Add second
                inventory.["totalCount"] <- Text.Json.Nodes.JsonValue.Create(2)
                { value with Body = root.ToJsonString() })
        else response
    let context = observedContext ambiguous
    let _, ambiguousReport, _ = inspectProgrammeInput (Some(ProgrammeFixture.integratorJson context)) ambiguous
    Assert.Equal("needs-decision", (inputSelections ambiguousReport.Value).[0].GetProperty("decision").GetString())
    let baseline = observedContext ProgrammeFixture.unchanged
    let wrongHead = { baseline with Slices = [ { baseline.Slices.Head with Head = Some(String.replicate 40 "f") } ] }
    let _, changed, _ = inspectProgrammeInput (Some(ProgrammeFixture.integratorJson wrongHead)) ProgrammeFixture.unchanged
    Assert.Equal("unknown", (inputSelections changed.Value).[0].GetProperty("decision").GetString())
    for subject in [ "repository policy"; "intake identity"; "permission for author" ] do
        let denied (request: Transport.Request) (response: Errors.IoResult<Transport.Response>) =
            if request.Subject.Contains subject then Error(Errors.Unauthorized "native intake unavailable") else response
        let _, report, _ = inspectProgrammeInput (Some(ProgrammeFixture.integratorJson baseline)) denied
        Assert.Equal("unknown", (inputSelections report.Value).[0].GetProperty("decision").GetString())
        Assert.Equal("Current", (report.Value.GetProperty("items")).[0].GetProperty("sourceCurrentness").GetString())

[<Fact>]
let ``actual native inventory gaps stay within proposed repository scope while binding gaps remain global`` () =
    let binding =
        { ProgrammeFixture.binding () with Repositories = Set.ofList [ "FS-GG/.github"; "FS-GG/independent" ] }
        |> fun value -> ProgrammeFixture.enroll value [ "I_outcome", ProgrammeFixture.issue, "docs/coordination/programme.md" ]
    let selectedBinding = ProgrammeFixture.bindingJsonFor binding
    let change (request: Transport.Request) (response: Errors.IoResult<Transport.Response>) =
        if request.Subject.EndsWith("board-v2-import-manifest.json", StringComparison.Ordinal) then
            response |> Result.map (fun value ->
                let root = Text.Json.Nodes.JsonNode.Parse value.Body
                let repository = root.["data"].["repository"]
                let enrolled = ProgrammeFixture.publicEnrollment binding
                repository.["object"].["text"] <- Text.Json.Nodes.JsonValue.Create enrolled
                repository.["current"].["text"] <- Text.Json.Nodes.JsonValue.Create enrolled
                { value with Body = root.ToJsonString() })
        elif request.Subject = "FS-GG/.github open PR inventory" then Error(Errors.Unauthorized "repository A inventory unavailable")
        elif request.Subject = "FS-GG/independent open PR inventory" then
            Ok { Status = 200; ETag = None; NextLink = None; Headers = Map.empty
                 Body = """{"data":{"repository":{"nameWithOwner":"FS-GG/independent","pullRequests":{"totalCount":0,"nodes":[],"pageInfo":{"hasNextPage":false,"endCursor":null}}}}}""" }
        else response
    let native = V2ProjectionSource.inspectFixed (ProgrammeFixture.transport change) binding None
    let context = ProgrammeFixture.context native
    let first = context.Slices.Head
    let independent: CurrentSlice =
        { first with
            Id = "independent-repository"
            Repository = "FS-GG/independent"
            Paths = [ "src/independent.fs" ]
            Operation = Implement
            PullRequest = None
            PullRequestId = None
            PullRequestUpdatedAt = None
            Head = None }
    let input = { context with Slices = [ first; independent ] }
    let _, report, _ = inspectProgrammeBindingInput selectedBinding (Some(ProgrammeFixture.integratorJson input)) change
    Assert.Equal<string list>([ "unknown"; "ready" ], inputSelections report.Value |> Array.map (fun value -> value.GetProperty("decision").GetString()) |> Array.toList)
    let globalGap (request: Transport.Request) (response: Errors.IoResult<Transport.Response>) =
        if request.Subject = "complete V2 project memberships" then Error(Errors.Unauthorized "global membership unavailable") else change request response
    let code, globalReport, _ = inspectProgrammeBindingInput selectedBinding (Some(ProgrammeFixture.integratorJson input)) globalGap
    Assert.Equal(3, code)
    Assert.True(inputSelections globalReport.Value |> Array.forall (fun value -> value.GetProperty("decision").GetString() = "unknown"))

[<Fact>]
let ``actual programme refresh caller updates only Observation then independently noops without integrator input`` () =
    let directory = Path.Combine(Path.GetTempPath(), "board-v2-refresh-" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(directory, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute) |> ignore
    try
        let bindingPath = Path.Combine(directory, "binding.json")
        File.WriteAllText(bindingPath, ProgrammeFixture.bindingJson ())
        let binding = ProgrammeFixture.binding ()
        let mutable observation = "Unknown"
        let mutable humanStatus = "In review"
        let reads =
            ProgrammeFixture.transport (fun request response ->
                if request.Subject = "fresh V2 Observation dispatch identity" then humanStatus <- "Ready"
                ProgrammeFixture.observation observation request response)
        let mutable writes = 0
        let guarded =
            V2ObservationTransport.compose binding reads (fun request ->
                writes <- writes + 1
                match request.Body with
                | Query(_, variables) ->
                    Assert.Equal(Some(VId binding.Observation.Id), variables |> List.tryFind (fst >> (=) "fieldId") |> Option.map snd)
                | _ -> failwith "typed Observation update required"
                observation <- "Verified"
                Ok { Status = 200; ETag = None; NextLink = None; Headers = Map.empty; Body = """{"data":{"updateProjectV2ItemFieldValue":{"clientMutationId":null}}}""" })
            |> Result.defaultWith (Errors.explain >> failwith)
        for index in [ 1; 2 ] do
            let output = Path.Combine(directory, $"refresh-{index}.json")
            use input = unreadableInput ()
            Assert.Equal(0, BoardV2Application.runWithTransportAndInput (TestLock.fresh ()) guarded input [ "refresh"; "--binding-file"; bindingPath; "--report-file"; output ])
            Assert.Equal(0, input.Reads)
            use document = JsonDocument.Parse(File.ReadAllText output)
            Assert.Equal(1, document.RootElement.GetProperty("verified").GetInt32())
            let item = (document.RootElement.GetProperty("items")).[0]
            Assert.Equal((if index = 1 then 1 else 0), item.GetProperty("mutationAttempts").GetInt32())
        Assert.Equal(1, writes)
        Assert.Equal("Ready", humanStatus)
        Assert.Equal("Verified", observation)
        let oldReads = ProgrammeFixture.transport ProgrammeFixture.oldSchema
        let oldGuard =
            V2ObservationTransport.compose binding oldReads (fun _ -> failwith "old native schema must refuse before mutation")
            |> Result.defaultWith (Errors.explain >> failwith)
        let oldOutput = Path.Combine(directory, "old-schema.json")
        Assert.Equal(3, BoardV2Application.runWithTransport (TestLock.fresh ()) oldGuard [ "refresh"; "--binding-file"; bindingPath; "--report-file"; oldOutput ])
        Assert.Equal(1, writes)

    finally Directory.Delete(directory, true)

[<Fact>]
let ``refresh refuses while the writer lock is held and sends no mutation`` () =
    let directory = Path.Combine(Path.GetTempPath(), "board-v2-refresh-locked-" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(directory, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute) |> ignore
    try
        let bindingPath = Path.Combine(directory, "binding.json")
        File.WriteAllText(bindingPath, ProgrammeFixture.bindingJson ())
        let binding = ProgrammeFixture.binding ()
        let mutable observation = "Unknown"
        let reads = ProgrammeFixture.transport (fun request response -> ProgrammeFixture.observation observation request response)
        let mutable writes = 0
        let guarded =
            V2ObservationTransport.compose binding reads (fun _ ->
                writes <- writes + 1
                observation <- "Verified"
                Ok { Status = 200; ETag = None; NextLink = None; Headers = Map.empty; Body = """{"data":{"updateProjectV2ItemFieldValue":{"clientMutationId":null}}}""" })
            |> Result.defaultWith (Errors.explain >> failwith)
        let lockDirectory = TestLock.fresh ()
        let location = V2WriterLock.inDirectory lockDirectory binding.ProjectId (V2WriterLock.observationLegacyName binding.ProjectId)
        let lockedOutput = Path.Combine(directory, "locked.json")
        (
            use _held = match V2WriterLock.tryAcquire location with Ok lease -> lease | Error reason -> failwith reason
            use input = unreadableInput ()
            Assert.Equal(1, BoardV2Application.runWithTransportAndInput lockDirectory guarded input [ "refresh"; "--binding-file"; bindingPath; "--report-file"; lockedOutput ])
            Assert.Equal(0, writes)
            Assert.False(File.Exists lockedOutput))
        // Non-vacuous: once the lock is free the same refresh sends its one Observation update.
        use input = unreadableInput ()
        Assert.Equal(0, BoardV2Application.runWithTransportAndInput lockDirectory guarded input [ "refresh"; "--binding-file"; bindingPath; "--report-file"; Path.Combine(directory, "free.json") ])
        Assert.Equal(1, writes)
    finally Directory.Delete(directory, true)

[<Fact>]
let ``actual caller localizes unusable PR evidence to current action and uncertain file scope`` () =
    let newContext = observedContext ProgrammeFixture.newAssignment
    for suffix in [ " reviews"; " head checks" ] do
        let disjoint (request: Transport.Request) (response: Errors.IoResult<Transport.Response>) =
            if request.Subject.EndsWith(suffix, StringComparison.Ordinal) then Error(Errors.Unauthorized "unrelated PR review/check read unavailable")
            else ProgrammeFixture.newAssignment request response
        let _, report, _ = inspectProgrammeInput (Some(ProgrammeFixture.integratorJson newContext)) disjoint
        Assert.Equal("ready", (inputSelections report.Value).[0].GetProperty("decision").GetString())
    for suffix in [ " files"; " final head barrier" ] do
        let unknownScope (request: Transport.Request) (response: Errors.IoResult<Transport.Response>) =
            if request.Subject.EndsWith(suffix, StringComparison.Ordinal) then Error(Errors.Unauthorized "native file/head scope unavailable")
            else ProgrammeFixture.newAssignment request response
        let _, report, _ = inspectProgrammeInput (Some(ProgrammeFixture.integratorJson newContext)) unknownScope
        Assert.Equal("unknown", (inputSelections report.Value).[0].GetProperty("decision").GetString())

[<Fact>]
let ``actual caller keeps source editing independent of activation acceptance and isolates stale slice joins`` () =
    let closedDependency (request: Transport.Request) (response: Errors.IoResult<Transport.Response>) =
        let response = ProgrammeFixture.newAssignment request response
        ProgrammeFixture.withBlockers [ ProgrammeFixture.blocker 1 "I_dependency" "2026-10-17T12:00:00Z" "CLOSED" ] request response
    let context = observedContext closedDependency
    let publication: Prerequisite =
        { Action = Activation; Reference = "publication/owner-current-evidence"; Satisfied = Some false; SupplyingOwner = "publication-owner" }
    let source = { context.Slices.Head with Prerequisites = publication :: context.Slices.Head.Prerequisites }
    let activation = { source with Id = "activation"; Action = Activation; Operation = ActivateWork }
    let _, report, _ = inspectProgrammeInput (Some(ProgrammeFixture.integratorJson { context with Slices = [ source; activation ] })) closedDependency
    Assert.Equal<string list>([ "ready"; "blocked" ], inputSelections report.Value |> Array.map (fun value -> value.GetProperty("decision").GetString()) |> Array.toList)
    let existing = observedContext ProgrammeFixture.unchanged
    let current = existing.Slices.Head
    let independent =
        { current with
            Id = "independent-source"
            Owner = "another-implementation-owner"
            Operation = Implement
            Paths = [ "src/independent.fs" ]
            PullRequest = None
            PullRequestId = None
            PullRequestUpdatedAt = None
            Head = None }
    let stale = { current with Join = { current.Join with SourceRevision = "old action/plan observation" } }
    let _, isolated, _ = inspectProgrammeInput (Some(ProgrammeFixture.integratorJson { existing with Slices = [ stale; independent ] })) ProgrammeFixture.unchanged
    Assert.Equal<string list>([ "unknown"; "ready" ], inputSelections isolated.Value |> Array.map (fun value -> value.GetProperty("decision").GetString()) |> Array.toList)
    let conflicting = { independent with Paths = [ "src/current.fs" ] }
    let _, blocked, _ = inspectProgrammeInput (Some(ProgrammeFixture.integratorJson { existing with Slices = [ current; conflicting ] })) ProgrammeFixture.unchanged
    Assert.Equal<string list>([ "continue-existing"; "blocked" ], inputSelections blocked.Value |> Array.map (fun value -> value.GetProperty("decision").GetString()) |> Array.toList)


[<Fact>]
let ``actual caller final intake barrier rereads unchanged author policy and permission`` () =
    let context = observedContext ProgrammeFixture.unchanged
    let mutations: (string * string * string) list =
        [ ("repository policy", "COLLABORATORS_ONLY", "ALL");
          ("permission for author", "write", "read") ]
    for subject, original, changed in mutations do
        let mutable determiningReads = 0
        let mutate (request: Transport.Request) (response: Errors.IoResult<Transport.Response>) =
            if request.Subject.Contains(subject, StringComparison.Ordinal) then
                determiningReads <- determiningReads + 1
                if determiningReads = 2 then
                    response |> Result.map (fun value -> { value with Body = value.Body.Replace(original, changed) })
                else response
            else response
        let code, report, transport = inspectProgrammeInput (Some(ProgrammeFixture.integratorJson context)) mutate
        Assert.Equal(0, code) // the independent source/planning observation remains available
        Assert.Equal(2, determiningReads)
        let root = report.Value
        let item = (root.GetProperty("items")).[0]
        Assert.Equal("Current", item.GetProperty("sourceCurrentness").GetString())
        let intake = item.GetProperty("nativeIntake")
        Assert.Equal(JsonValueKind.Null, intake.GetProperty("allowed").ValueKind)
        Assert.Contains("changed or became unavailable", intake.GetProperty("gap").GetString())
        Assert.Equal("unknown", (inputSelections root).[0].GetProperty("decision").GetString())
        Assert.Empty transport.Mutations

[<Fact>]
let ``actual binding file acquisition enforces byte boundary before transport and keeps product input closed`` () =
    let original = ProgrammeFixture.bindingJson ()
    for size in [ 262144; 262145 ] do
        let binding = original + String.replicate (size - Text.Encoding.UTF8.GetByteCount original) " "
        let mutable reads = 0
        let code, report, _ = inspectProgrammeBindingInput binding None (fun (_: Transport.Request) response -> reads <- reads + 1; response)
        if size = 262144 then
            Assert.Equal(0, code)
            Assert.True(report.IsSome)
            Assert.True(reads > 0)
        else
            Assert.Equal(1, code)
            Assert.True(report.IsNone)
            Assert.Equal(0, reads)
    let product = Text.Json.Nodes.JsonNode.Parse(productJson "WidgetA" "product_a" 81)
    Assert.True(Result.isOk (BoardV2Application.parseBinding (product.ToJsonString())))
    product.AsObject().Add("programmeEnrollment", (Text.Json.Nodes.JsonNode.Parse original).["programmeEnrollment"].DeepClone())
    let mutable productReads = 0
    let code, report, _ = inspectProgrammeBindingInput (product.ToJsonString()) None (fun (_: Transport.Request) response -> productReads <- productReads + 1; response)
    Assert.Equal(1, code)
    Assert.True(report.IsNone)
    Assert.Equal(0, productReads)

[<Fact>]
let ``actual private binding malformed fields refuse before native acquisition without sensitive diagnostics`` () =
    let sensitive = "synthetic-sensitive-never-public"
    for mode in [ "missing"; "nonce"; "base64"; "duplicate"; "payload"; "utf8" ] do
        let root = Text.Json.Nodes.JsonNode.Parse(ProgrammeFixture.bindingJson ())
        match mode with
        | "missing" -> root.AsObject().Remove "programmeEnrollment" |> ignore
        | "nonce" -> root.["programmeEnrollment"].["nonceHex"] <- Text.Json.Nodes.JsonValue.Create sensitive
        | "base64" -> root.["programmeEnrollment"].["payloadBase64"] <- Text.Json.Nodes.JsonValue.Create sensitive
        | "payload" -> root.["programmeEnrollment"].["payloadBase64"] <- Text.Json.Nodes.JsonValue.Create(Convert.ToBase64String(Text.Encoding.UTF8.GetBytes("{\"" + sensitive + "\":1}")))
        | "utf8" -> root.["programmeEnrollment"].["payloadBase64"] <- Text.Json.Nodes.JsonValue.Create "/w=="
        | _ -> ()
        let json = root.ToJsonString()
        let json = if mode = "duplicate" then json.Replace("\"nonceHex\":", "\"nonceHex\":\"" + sensitive + "\",\"nonceHex\":") else json
        let mutable reads = 0
        let code, report, _ = inspectProgrammeBindingInput json None (fun (_: Transport.Request) response -> reads <- reads + 1; response)
        Assert.Equal(1, code)
        Assert.True(report.IsNone)
        Assert.Equal(0, reads)
        match BoardV2Application.parseBinding json with
        | Error error -> Assert.DoesNotContain(sensitive, error)
        | Ok _ -> failwith "invalid private enrollment accepted"

[<Fact>]
let ``actual public commitment mismatch or raw public payload cannot enroll private rows or leak bytes`` () =
    let sensitive = "synthetic-sensitive-never-public"
    let selected = ProgrammeFixture.enroll (ProgrammeFixture.binding ()) [ "I_outcome", ProgrammeFixture.issue, "docs/" + sensitive + ".md" ]
    let enrollment = selected.ProgrammeEnrollment |> Option.get
    let payload = Convert.FromBase64String enrollment.PayloadBase64 |> Text.Encoding.UTF8.GetString
    for publicText in [ payload; ProgrammeFixture.publicEnrollment (ProgrammeFixture.binding ()) ] do
        let mutable reads = 0
        let change (request: Transport.Request) (response: Errors.IoResult<Transport.Response>) =
            reads <- reads + 1
            if request.Subject.EndsWith("board-v2-import-manifest.json", StringComparison.Ordinal) then
                response |> Result.map (fun value ->
                    let root = Text.Json.Nodes.JsonNode.Parse value.Body
                    root.["data"].["repository"].["object"].["text"] <- Text.Json.Nodes.JsonValue.Create publicText
                    root.["data"].["repository"].["current"].["text"] <- Text.Json.Nodes.JsonValue.Create publicText
                    { value with Body = root.ToJsonString() })
            else response
        let code, report, _ = inspectProgrammeBindingInput (ProgrammeFixture.bindingJsonFor selected) None change
        Assert.Equal(3, code)
        Assert.Equal(1, reads)
        Assert.Equal(0, report.Value.GetProperty("items").GetArrayLength())
        let output = report.Value.GetRawText()
        for secret in [ sensitive; enrollment.NonceHex; enrollment.PayloadBase64 ] do Assert.DoesNotContain(secret, output)

[<Fact>]
let ``actual inspection revokes selection when protected commitment changes after slow native reads`` () =
    let baseline = V2ProjectionSource.inspectFixed (ProgrammeFixture.transport ProgrammeFixture.unchanged) (ProgrammeFixture.binding ()) None
    let input = ProgrammeFixture.context baseline |> ProgrammeFixture.integratorJson
    let mutable protectedReads = 0
    let change (request: Transport.Request) (response: Errors.IoResult<Transport.Response>) =
        if request.Subject.EndsWith("board-v2-import-manifest.json", StringComparison.Ordinal) then
            protectedReads <- protectedReads + 1
            if protectedReads > 1 then
                response |> Result.map (fun value ->
                    let root = Text.Json.Nodes.JsonNode.Parse value.Body
                    root.["data"].["repository"].["current"].["oid"] <- Text.Json.Nodes.JsonValue.Create(String.replicate 40 "f")
                    { value with Body = root.ToJsonString() })
            else response
        else response
    let code, report, _ = inspectProgrammeInput (Some input) change
    Assert.Equal(3, code)
    Assert.Equal(2, protectedReads)
    Assert.NotEqual<JsonValueKind>(JsonValueKind.Null, report.Value.GetProperty("populationGap").ValueKind)
    Assert.Equal("unknown", (inputSelections report.Value).[0].GetProperty("decision").GetString())
    let publicCommitment = (report.Value.GetProperty("enrollmentCommitmentSha256")).GetString()
    Assert.Equal<string>(ProgrammeFixture.binding () |> _.ProgrammeEnrollment |> Option.get |> programmeCommitment |> Result.defaultWith (Errors.explain >> failwith), publicCommitment)

[<Fact>]
let ``actual refresh reauthenticates public commitment before Observation send and Verified noop`` () =
    for observation in [ "Unknown"; "Verified" ] do
        let directory = Path.Combine(Path.GetTempPath(), "board-v2-private-refresh-" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(directory, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute) |> ignore
        try
            let binding = ProgrammeFixture.binding ()
            let input = Path.Combine(directory, "binding.json")
            let output = Path.Combine(directory, "report.json")
            File.WriteAllText(input, ProgrammeFixture.bindingJsonFor binding)
            let mutable publicReads = 0
            let mutable writes = 0
            let reads = ProgrammeFixture.transport (fun (request: Transport.Request) response ->
                let response = ProgrammeFixture.observation observation request response
                if request.Subject.EndsWith("board-v2-import-manifest.json", StringComparison.Ordinal) then
                    publicReads <- publicReads + 1
                    if publicReads > 1 then
                        response |> Result.map (fun value ->
                            let root = Text.Json.Nodes.JsonNode.Parse value.Body
                            root.["data"].["repository"].["current"].["oid"] <- Text.Json.Nodes.JsonValue.Create(String.replicate 40 "f")
                            { value with Body = root.ToJsonString() })
                    else response
                else response)
            let guarded =
                V2ObservationTransport.compose binding reads (fun (_: Transport.Request) ->
                    writes <- writes + 1
                    failwith "revoked public enrollment must never dispatch")
                |> Result.defaultWith (Errors.explain >> failwith)
            Assert.Equal(3, BoardV2Application.runWithTransport (TestLock.fresh ()) guarded [ "refresh"; "--binding-file"; input; "--report-file"; output ])
            Assert.Equal(2, publicReads)
            Assert.Equal(0, writes)
            Assert.Equal<UnixFileMode>(UnixFileMode.UserRead ||| UnixFileMode.UserWrite, File.GetUnixFileMode output)
            use report = JsonDocument.Parse(File.ReadAllText output)
            Assert.Equal(0, report.RootElement.GetProperty("verified").GetInt32())
            Assert.Equal(0, (report.RootElement.GetProperty("items")).[0].GetProperty("mutationAttempts").GetInt32())
        finally Directory.Delete(directory, true)

[<Fact>]
let ``actual integrator input must bind the authenticated whole enrollment commitment before reads`` () =
    let baseline = V2ProjectionSource.inspectFixed (ProgrammeFixture.transport ProgrammeFixture.unchanged) (ProgrammeFixture.binding ()) None
    let context = ProgrammeFixture.context baseline
    for mode in [ "missing"; "wrong"; "duplicate" ] do
        let root = Text.Json.Nodes.JsonNode.Parse(ProgrammeFixture.integratorJson context)
        if mode = "missing" then root.AsObject().Remove "enrollmentCommitmentSha256" |> ignore
        elif mode = "wrong" then root.["enrollmentCommitmentSha256"] <- Text.Json.Nodes.JsonValue.Create(String.replicate 64 "f")
        let json = root.ToJsonString()
        let json = if mode = "duplicate" then json.Replace("\"enrollmentCommitmentSha256\":", "\"enrollmentCommitmentSha256\":\"duplicate\",\"enrollmentCommitmentSha256\":") else json
        let mutable reads = 0
        let code, report, _ = inspectProgrammeInput (Some json) (fun (_: Transport.Request) response -> reads <- reads + 1; response)
        Assert.Equal(1, code)
        Assert.True(report.IsNone)
        Assert.Equal(0, reads)

[<Fact>]
let ``successor actual CLI requires versioned hierarchy join and emits scope without parent acceptance`` () =
    let selected = ProgrammeFixture.successorBinding ()
    let baseline = V2ProjectionSource.inspectFixed (ProgrammeFixture.successorTransport selected (Some "Delivery") ProgrammeFixture.unchanged) selected None
    let context = ProgrammeFixture.context baseline
    let input = ProgrammeFixture.successorIntegratorJson context
    Assert.True(Result.isOk(BoardV2Application.parseIntegratorObservations selected input))
    Assert.True(Result.isError(BoardV2Application.parseIntegratorObservations selected (ProgrammeFixture.integratorJson context)))
    let stale = Text.Json.Nodes.JsonNode.Parse input
    stale.["slices"].[0].["join"].["hierarchySha256"] <- Text.Json.Nodes.JsonValue.Create(String.replicate 64 "f")
    let directory = Path.Combine(Path.GetTempPath(), "board-v2-successor-" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(directory, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute) |> ignore
    try
        let bindingPath = Path.Combine(directory, "binding.json")
        File.WriteAllText(bindingPath, ProgrammeFixture.bindingJsonFor selected)
        for index, json, expected in
            [ 0, input, "ready"
              1, stale.ToJsonString(), "unknown"
              2, ProgrammeFixture.successorIntegratorJson {context with ImplementationSlots = Some 0}, "deferred"
              3, ProgrammeFixture.successorIntegratorJson {context with ImplementationSlots = None}, "unknown" ] do
            let output = Path.Combine(directory, $"report-{index}.json")
            let reads = ProgrammeFixture.successorTransport selected (Some "Delivery") ProgrammeFixture.unchanged
            use reader = new StringReader(json)
            let code = BoardV2Application.runWithTransportAndInput (TestLock.fresh ()) reads reader ["inspect"; "--binding-file"; bindingPath; "--report-file"; output; "--integrator-stdin"]
            Assert.Equal(0, code)
            use report = JsonDocument.Parse(File.ReadAllText output)
            let root = report.RootElement
            Assert.Equal("fsgg.coord.board-v2-inspection/3", root.GetProperty("schema").GetString())
            let work = root.GetProperty("items").[0].GetProperty("workItem")
            Assert.Equal("Delivery", work.GetProperty("workLevel").GetString())
            Assert.Equal("Unverified", work.GetProperty("closure").GetProperty("acceptance").GetString())
            Assert.Equal("I_parent", work.GetProperty("hierarchy").GetProperty("parent").GetProperty("nodeId").GetString())
            let hierarchy = work.GetProperty("hierarchy")
            Assert.True(hierarchy.GetProperty("complete").GetBoolean())
            Assert.Equal(baseline.Items.Head.WorkItem.Value.Sha256.Value, hierarchy.GetProperty("sha256").GetString())
            let unselected = hierarchy.GetProperty("children").[0].GetProperty("nodeId").GetString()
            Assert.False(selected.SelectedIssues.ContainsKey unselected)
            let action = root.GetProperty("selection").[0]
            let supplied = context.Slices.Head
            Assert.Equal(expected, action.GetProperty("decision").GetString())
            Assert.Equal("source", action.GetProperty("action").GetString())
            Assert.Equal("implement", action.GetProperty("operation").GetString())
            Assert.Equal(supplied.Owner, action.GetProperty("owner").GetString())
            Assert.Equal(supplied.Id, action.GetProperty("sliceId").GetString())
            Assert.Equal(supplied.Attempt, action.GetProperty("attempt").GetString())
            Assert.Equal(supplied.Issue.Number, action.GetProperty("issue").GetProperty("number").GetInt32())
            Assert.Equal(supplied.Issue.Owner, action.GetProperty("issue").GetProperty("owner").GetString())
            Assert.Equal(supplied.Issue.Repository, action.GetProperty("issue").GetProperty("repository").GetString())
            let demand = action.GetProperty("resourceDemand")
            Assert.Equal(1, demand.GetProperty("implementation").GetInt32())
            Assert.Equal(0, demand.GetProperty("review").GetInt32())
            Assert.Equal(0, demand.GetProperty("hostedCi").GetInt32())
            Assert.False(action.GetProperty("dispatchAuthority").GetBoolean())
            Assert.Empty reads.Mutations
    finally Directory.Delete(directory, true)
