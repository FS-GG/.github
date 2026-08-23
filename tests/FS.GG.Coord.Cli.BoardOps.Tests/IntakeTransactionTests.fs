namespace FS.GG.Coord.Cli.Tests

open System
open System.IO
open System.Text.Json
open Xunit
open FS.GG.Coord
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Errors
open FS.GG.Coord.GitHub.Transport
open FS.GG.Coord.Cli
open FS.GG.Coord.Cli.BoardOps

/// Transaction fixtures deliberately stop immediately after the receipt boundary.  The recording
/// transport makes the invariant observable: a retry may repair projection, but it cannot issue a
/// second `POST /issues`.
module IntakeTransactionTests =
    let private ok body = Ok { Status = 200; Body = body; ETag = None; NextLink = None; Headers = Map.empty }
    let private draft = """{"schema":"fsgg.coord.intake/v1","id":"tx-2134","owner":"FS-GG","repository":".github","title":"same","observed":"o","rootCause":"r","acceptance":"a","verification":"v","paths":["src/FS.GG.Coord.Core"],"class":"hardening","status":"Backlog","backlogReason":"not-yet-actionable","disposition":"create"}"""

    // `Options.parse` intentionally leaves intake's action/path in its generic positional bucket;
    // keep the test on the real parser, then make that command-local shape explicit for the handler.
    let private options path =
        let parsed = Options.parse [ "intake"; "apply"; path ] |> Result.defaultWith failwith
        { parsed with Args = [ "apply"; path ] }
    let private context transport : Kernel.Context = { Transport = transport; Owner = "FS-GG"; Title = "Coordination"; DefaultRepo = Some ".github"; ChoreLocks = [] }

    let private invokeDraft cache (json: string) transport =
        let path = Path.Combine(cache, "draft.json")
        File.WriteAllText(path, json)
        match IntakeApplication.readDraft path with
        | Error error -> failwith error
        | Ok _ -> ()
        Handlers.intakeCmd (context transport) (options path)

    let private invoke cache transport = invokeDraft cache draft transport

    let private draftDigest cache =
        let path = Path.Combine(cache, "digest-draft.json")
        File.WriteAllText(path, draft)
        match IntakeApplication.readDraft path with
        | Ok parsed -> IntakeReceipt.digest parsed
        | Error error -> failwith error

    let private draftMarker cache =
        let path = Path.Combine(cache, "marker-draft.json")
        File.WriteAllText(path, draft)
        match IntakeApplication.readDraft path with
        | Ok parsed -> IntakeReceipt.marker parsed
        | Error error -> failwith error

    let private lightweightRouteComment subject =
        let draft: StructuredDecision.RouteRecord =
            { Schema = StructuredDecision.RouteSchema
              Subject = subject
              Revision = 1
              PreviousDigest = None
              Scope = [ "intake Ready guard" ]
              Dependencies = [ "none" ]
              TouchSet = [ "src/FS.GG.Coord.Core" ]
              PolicyVersion = StructuredDecision.PolicyVersion
              Route = Some DeliveryRoute.Lightweight
              Agent = "intake-test"
              Timestamp = "2026-08-23T00:00:00Z"
              ReasonCodes = [ "fixture" ]
              Rationale = "current route receipt for the intake Ready guard fixture"
              SddWorkId = None
              SpecHome = None
              RequiredGates = []
              Digest = "" }

        let record =
            { draft with
                Digest = StructuredDecision.routeDigest draft }

        "<!-- fsgg:route-decision/v2 -->\n"
        + JsonSerializer.Serialize
            {| schema = record.Schema
               subject = record.Subject
               revision = record.Revision
               previousDigest = record.PreviousDigest
               scope = record.Scope
               dependencies = record.Dependencies
               touchSet = record.TouchSet
               policyVersion = record.PolicyVersion
               route = "lightweight"
               agent = record.Agent
               timestamp = record.Timestamp
               reasonCodes = record.ReasonCodes
               rationale = record.Rationale
               sddWorkId = record.SddWorkId
               specHome = record.SpecHome
               requiredGates = record.RequiredGates
               digest = record.Digest |}

    let private withCache action =
        let cache = Path.Combine(Path.GetTempPath(), "fsgg-intake-" + Guid.NewGuid().ToString("N"))
        let previous = Environment.GetEnvironmentVariable "FSGG_COORD_CACHE"
        let previousWorker = Environment.GetEnvironmentVariable "FSGG_WORKER"
        Directory.CreateDirectory cache |> ignore
        Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", cache)
        Environment.SetEnvironmentVariable("FSGG_WORKER", "intake-test")
        try action cache finally Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", previous); Environment.SetEnvironmentVariable("FSGG_WORKER", previousWorker); Directory.Delete(cache, true)

    [<Theory>]
    [<InlineData(null, false)>]
    [<InlineData("", false)>]
    [<InlineData("   ", false)>]
    [<InlineData("FS-GG/.github#42", true)>]
    let ``#2738 Ready eligibility consumes only the typed Blocked by column`` (raw: string) refused =
        let verdict = Handlers.readyDependencyVerdict (Option.ofObj raw)
        Assert.Equal(refused, verdict.IsSome)

    [<Fact>]
    let ``#2738 projected Blocked by prose is not an input to Ready eligibility`` () =
        let body = "Blocked by: FS-GG/.github#42"
        Assert.Contains("Blocked by:", body)
        Assert.True((Handlers.readyDependencyVerdict None).IsNone)

    [<Fact>]
    let ``#2738 a moved Projects item revision stales the Ready decision even when the edge value is unchanged`` () =
        let observedAt revision : Board.BlockedByObservation =
            { Value = None
              Revision = Some revision }

        Assert.False(Handlers.readyDependencyStale (Some(observedAt "r1")) (Some(observedAt "r1")))
        Assert.True(Handlers.readyDependencyStale (Some(observedAt "r1")) (Some(observedAt "r2")))

    [<Theory>]
    [<InlineData(false)>]
    [<InlineData(true)>]
    let ``#2738 intake apply refuses Ready from a live column edge regardless of body prose`` bodyProjectsEdge =
        withCache
        <| fun cache ->
            let ready =
                """{"schema":"fsgg.coord.intake/v1","id":"ready-edge-2738","owner":"FS-GG","repository":".github","title":"ready edge","observed":"o","rootCause":"r","acceptance":"a","verification":"v","paths":["src/FS.GG.Coord.Core"],"class":"hardening","status":"Ready","disposition":"reuse"}"""

            let draftPath = Path.Combine(cache, "ready-edge-digest.json")
            File.WriteAllText(draftPath, ready)
            let parsed = IntakeApplication.readDraft draftPath |> Result.defaultWith failwith

            Cache.putIntakeReceipt
                { IntakeReceipt.Receipt.DraftId = parsed.Id
                  Owner = parsed.Owner
                  Repository = parsed.Repository
                  IssueNumber = 88
                  DraftDigest = IntakeReceipt.digest parsed }
            |> Result.defaultWith failwith

            let body =
                if bodyProjectsEdge then
                    "Blocked by: FS-GG/.github#42"
                else
                    "No dependency prose here."

            let route = lightweightRouteComment "FS-GG/.github#88" |> JsonSerializer.Serialize
            let mutable edgeReads = 0

            let world =
                Fake.Recorder(fun req ->
                    match req.Method, req.Path.Trim '/' with
                    | "GET", "repos/FS-GG/.github/issues/88" ->
                        ok ($"{{\"number\":88,\"state\":\"open\",\"body\":%s{JsonSerializer.Serialize body}}}")
                    | "GET", "repos/FS-GG/.github/issues/88/comments" -> ok ($"[{{\"body\":%s{route}}}]")
                    | "POST", "graphql" ->
                        match req.Body with
                        | Query(document, _) when document.Contains "projectsV2" ->
                            ok
                                """{"data":{"organization":{"projectsV2":{"nodes":[{"number":1,"title":"Coordination","id":"PVT"}]}},"rateLimit":{"cost":1,"remaining":4977}}}"""
                        | Query(document, _) when document.Contains "fields(first" ->
                            ok
                                """{"data":{"organization":{"projectV2":{"fields":{"nodes":[{"id":"S","name":"Status","dataType":"SINGLE_SELECT","options":[{"id":"R","name":"Ready"}]},{"id":"B","name":"Blocked by","dataType":"TEXT"}]}}},"rateLimit":{"cost":1,"remaining":4977}}}"""
                        | Query(document, _) when document.Contains "fieldValueByName(name: \"Blocked by\")" ->
                            edgeReads <- edgeReads + 1

                            ok
                                """{"data":{"repository":{"issue":{"projectItems":{"totalCount":1,"nodes":[{"updatedAt":"2026-08-23T10:00:00Z","project":{"number":1},"fieldValueByName":{"text":"FS-GG/.github#42"}}]}}},"rateLimit":{"cost":1,"remaining":4977}}}"""
                        | _ -> Error(NotFound "Ready must refuse before any other board operation")
                    | _ -> Error(NotFound "unexpected request"))

            Assert.Equal(Kernel.ExitError, invokeDraft cache ready world)
            Assert.Equal(1, edgeReads)

    [<Fact>]
    let ``#2134 a durable receipt bypasses issue creation`` () =
        withCache <| fun cache ->
            Cache.putIntakeReceipt { IntakeReceipt.Receipt.DraftId = "tx-2134"; Owner = "FS-GG"; Repository = ".github"; IssueNumber = 77; DraftDigest = draftDigest cache } |> ignore
            let mutable posts = 0
            let world = Fake.Recorder(fun req ->
                if req.Method = "POST" && req.Path.EndsWith "/issues" then posts <- posts + 1
                Error(NotFound "stop after receipt recovery"))
            Assert.Equal(Kernel.ExitError, invoke cache world)
            Assert.Equal(0, posts)

    [<Fact>]
    let ``#2134 apply refuses a nonexistent live path before transport`` () =
        withCache <| fun cache ->
            let invalid = draft.Replace("src/FS.GG.Coord.Core", "definitely/not/a/live/path-2134")
            let world = Fake.Recorder(fun _ -> Error(NotFound "live-path refusal must precede transport"))
            Assert.Equal(Kernel.ExitError, invokeDraft cache invalid world)
            Assert.Equal(0, world.RestCalls + world.GraphQlCalls)

    [<Fact>]
    let ``#2134 apply refuses a resolved Blocked dependency before create`` () =
        withCache <| fun cache ->
            let blocked = """{"schema":"fsgg.coord.intake/v1","id":"blocked-2134","owner":"FS-GG","repository":".github","title":"blocked","observed":"o","rootCause":"r","acceptance":"a","verification":"v","paths":["src/FS.GG.Coord.Core"],"class":"hardening","status":"Blocked","blockedBy":"FS-GG/.github#42","disposition":"create"}"""
            let mutable creates = 0
            let world = Fake.Recorder(fun req ->
                match req.Method, req.Path.Trim '/' with
                | "GET", "repos/FS-GG/.github/issues/42" -> ok "{\"number\":42,\"state\":\"closed\"}"
                | "POST", path when path.EndsWith "/issues" -> creates <- creates + 1; Error(NotFound "must refuse before create")
                | _ -> Error(NotFound "unexpected request"))
            Assert.Equal(Kernel.ExitError, invokeDraft cache blocked world)
            Assert.Equal(0, creates)

    [<Fact>]
    let ``#2134 Ready reuse refuses a live human-choice marker`` () =
        withCache <| fun cache ->
            let ready = """{"schema":"fsgg.coord.intake/v1","id":"ready-2134","owner":"FS-GG","repository":".github","title":"ready","observed":"o","rootCause":"r","acceptance":"a","verification":"v","paths":["src/FS.GG.Coord.Core"],"class":"hardening","status":"Ready","disposition":"reuse"}"""
            let draftPath = Path.Combine(cache, "ready-digest.json")
            File.WriteAllText(draftPath, ready)
            let parsed = IntakeApplication.readDraft draftPath |> Result.defaultWith failwith
            Cache.putIntakeReceipt { IntakeReceipt.Receipt.DraftId = parsed.Id; Owner = parsed.Owner; Repository = parsed.Repository; IssueNumber = 88; DraftDigest = IntakeReceipt.digest parsed } |> Result.defaultWith failwith
            let mutable bodyReads = 0
            let world = Fake.Recorder(fun req ->
                match req.Method, req.Path.Trim '/' with
                | "GET", "repos/FS-GG/.github/issues/88" -> bodyReads <- bodyReads + 1; ok "{\"number\":88,\"state\":\"open\",\"body\":\"Blocked on: human/decision\"}"
                | _ -> Error(NotFound "Ready guard must refuse before board projection"))
            Assert.Equal(Kernel.ExitError, invokeDraft cache ready world)
            Assert.True(bodyReads >= 1, "the Ready gate must inspect the live issue body")

    [<Fact>]
    let ``#2134 interruption after create persists receipt and retry issues no second POST`` () =
        withCache <| fun cache ->
            let mutable posts = 0
            let first = Fake.Recorder(fun req ->
                match req.Method, req.Path.Trim '/' with
                | "GET", "repos/FS-GG/.github/issues" -> ok "[]"
                | "POST", "repos/FS-GG/.github/issues" -> posts <- posts + 1; ok "{\"number\":77}"
                | _ -> Error(NotFound "interrupted after persisted create"))
            Assert.Equal(Kernel.ExitError, invoke cache first)
            if posts <> 1 then failwith (String.concat "\n" first.Log)
            let mutable retryPosts = 0
            let retry = Fake.Recorder(fun req ->
                if req.Method = "POST" && req.Path.EndsWith "/issues" then retryPosts <- retryPosts + 1
                Error(NotFound "stop after recovered receipt"))
            Assert.Equal(Kernel.ExitError, invoke cache retry)
            Assert.Equal(0, retryPosts)

    [<Fact>]
    let ``#2134 create-before-receipt crash converges through the durable intent`` () =
        withCache <| fun cache ->
            let digest = draftDigest cache
            Cache.putIntakeIntent { Cache.IntakeIntent.DraftId = "tx-2134"; Owner = "FS-GG"; Repository = ".github"; DraftDigest = digest } |> Result.defaultWith failwith
            let mutable posts = 0
            let world = Fake.Recorder(fun req ->
                match req.Method, req.Path.Trim '/' with
                | "GET", "repos/FS-GG/.github/issues" -> ok ($"[{{\"number\":77,\"state\":\"open\",\"title\":\"same\",\"body\":{System.Text.Json.JsonSerializer.Serialize(draftMarker cache)}}}]")
                | "POST", path when path.EndsWith "/issues" -> posts <- posts + 1; Error(NotFound "must not create again")
                | _ -> Error(NotFound "stop after intent recovery"))
            Assert.Equal(Kernel.ExitError, invoke cache world)
            Assert.Equal(0, posts)
            match Cache.getIntakeReceipt "tx-2134" with
            | Ok(Some receipt) -> Assert.Equal(77, receipt.IssueNumber)
            | other -> failwithf "intent recovery did not bind a receipt: %A" other

    [<Fact>]
    let ``#2134 intent never binds an unrelated same-title issue`` () =
        withCache <| fun cache ->
            Cache.putIntakeIntent { Cache.IntakeIntent.DraftId = "tx-2134"; Owner = "FS-GG"; Repository = ".github"; DraftDigest = draftDigest cache } |> Result.defaultWith failwith
            let mutable posts = 0
            let world = Fake.Recorder(fun req ->
                match req.Method, req.Path.Trim '/' with
                | "GET", "repos/FS-GG/.github/issues" -> ok "[{\"number\":77,\"state\":\"open\",\"title\":\"same\",\"body\":\"unrelated issue filed by another actor\"}]"
                | "POST", path when path.EndsWith "/issues" -> posts <- posts + 1; Error(NotFound "must refuse, not create")
                | _ -> Error(NotFound "stop after provenance refusal"))
            Assert.Equal(Kernel.ExitError, invoke cache world)
            Assert.Equal(0, posts)
            Assert.Equal(Ok None, Cache.getIntakeReceipt "tx-2134")

    [<Fact>]
    let ``#2134 the per-draft lock refuses a concurrent create window`` () =
        withCache <| fun _ ->
            let result = Cache.withIntakeLock "tx-2134" (fun () -> Cache.withIntakeLock "tx-2134" (fun () -> 1))
            match result with
            | Ok(Error reason) -> Assert.Contains("already being applied", reason)
            | other -> failwithf "concurrent intake unexpectedly entered the create window: %A" other

    [<Theory>]
    [<InlineData("{\"number\":7,\"state\":\"closed\",\"title\":\"same\",\"body\":\"x\"}")>]
    [<InlineData("{\"number\":8,\"state\":\"open\",\"title\":\"same\",\"body\":\"x\",\"pull_request\":{}}")>]
    let ``#2134 duplicate closed issue or PR refuses before create POST`` candidate =
        withCache <| fun cache ->
            let mutable posts = 0
            let world = Fake.Recorder(fun req ->
                match req.Method, req.Path.Trim '/' with
                | "GET", "repos/FS-GG/.github/issues" -> ok ("[" + candidate + "]")
                | "POST", _ -> posts <- posts + 1; Error(NotFound "duplicate must stop before any other write")
                | _ -> Error(NotFound "duplicate must stop before any other write"))
            Assert.Equal(Kernel.ExitError, invoke cache world)
            Assert.Equal(0, posts)

    [<Fact>]
    let ``#2134 explicit reuse binds the selected candidate without create`` () =
        withCache <| fun cache ->
            let reuse = draft.Replace("\"disposition\":\"create\"", "\"disposition\":\"reuse\"")
            let mutable creates = 0
            let world = Fake.Recorder(fun req ->
                match req.Method, req.Path.Trim '/' with
                | "GET", "repos/FS-GG/.github/issues" -> ok "[{\"number\":88,\"state\":\"open\",\"title\":\"same\",\"body\":\"existing\"}]"
                | "POST", path when path.EndsWith "/issues" -> creates <- creates + 1; Error(NotFound "reuse must not create")
                | _ -> Error(NotFound "stop after reuse binding"))
            Assert.Equal(Kernel.ExitError, invokeDraft cache reuse world)
            Assert.Equal(0, creates)
            match Cache.getIntakeReceipt "tx-2134" with
            | Ok(Some receipt) -> Assert.Equal(88, receipt.IssueNumber)
            | other -> failwithf "reuse did not bind a receipt: %A" other

    [<Fact>]
    let ``#2134 unreadable and binding-mismatched receipts fail closed before POST`` () =
        withCache <| fun cache ->
            File.WriteAllText(Path.Combine(cache, "intake-tx-2134.json"), "not-json")
            let unreadable = Fake.Recorder(fun _ -> Error(NotFound "must not read network"))
            Assert.Equal(Kernel.ExitError, invoke cache unreadable)
            Assert.Equal(0, unreadable.RestCalls)
            File.WriteAllText(Path.Combine(cache, "intake-tx-2134.json"), "{\"draftId\":\"tx-2134\",\"owner\":\"other\",\"repository\":\".github\",\"issueNumber\":77}")
            let mismatched = Fake.Recorder(fun _ -> Error(NotFound "must not read network"))
            Assert.Equal(Kernel.ExitError, invoke cache mismatched)
            Assert.Equal(0, mismatched.RestCalls)

    [<Theory>]
    [<InlineData(false)>]
    [<InlineData(true)>]
    let ``#2134 successful create binds receipt and verifies org or user-owned board projection`` userOwned =
        withCache <| fun cache ->
            let priorKind = Environment.GetEnvironmentVariable "FSGG_COORD_OWNER_TYPE"
            let priorOwner = Environment.GetEnvironmentVariable "FSGG_COORD_OWNER"
            Environment.SetEnvironmentVariable("FSGG_COORD_OWNER_TYPE", if userOwned then "user" else null)
            Environment.SetEnvironmentVariable("FSGG_COORD_OWNER", "FS-GG")
            use _restore =
                { new IDisposable with
                    member _.Dispose() =
                        Environment.SetEnvironmentVariable("FSGG_COORD_OWNER_TYPE", priorKind)
                        Environment.SetEnvironmentVariable("FSGG_COORD_OWNER", priorOwner) }
            let ownerNode = if userOwned then "user" else "organization"
            let mutable creates = 0
            let mutable added = 0
            let mutable statusWrites = 0
            let mutable boardAdded = false
            let mutable projectedStatus: string option = None
            let mutable projectedClass: string option = None
            let world = Fake.Recorder(fun req ->
                match req.Method, req.Path.Trim '/' with
                | "GET", "repos/FS-GG/.github/issues" -> ok "[]"
                | "POST", "repos/FS-GG/.github/issues" -> creates <- creates + 1; ok "{\"number\":77}"
                | "GET", "repos/FS-GG/.github/issues/77" -> ok "{\"number\":77,\"state\":\"open\"}"
                | "POST", "graphql" ->
                    match req.Body with
                    | Query(doc, _) when doc.Contains "projectsV2" -> ok ("{\"data\":{\"OWNER\":{\"projectsV2\":{\"nodes\":[{\"number\":1,\"title\":\"Coordination\",\"id\":\"PVT\"}]}}},\"rateLimit\":{\"cost\":1,\"remaining\":1}}".Replace("OWNER", ownerNode))
                    | Query(doc, _) when doc.Contains "fields(first" -> ok ("{\"data\":{\"OWNER\":{\"projectV2\":{\"fields\":{\"nodes\":[{\"id\":\"F\",\"name\":\"Status\",\"dataType\":\"SINGLE_SELECT\",\"options\":[{\"id\":\"B\",\"name\":\"Backlog\"}]},{\"id\":\"C\",\"name\":\"Class\",\"dataType\":\"SINGLE_SELECT\",\"options\":[{\"id\":\"H\",\"name\":\"hardening\"}]}]}}}},\"rateLimit\":{\"cost\":1,\"remaining\":1}}".Replace("OWNER", ownerNode))
                    | Query(doc, variables) when doc.Contains "node(id: $itemId)" ->
                        let field = variables |> List.tryPick (function "field", VString value -> Some value | _ -> None)
                        let value = match field with Some "Status" -> projectedStatus | Some "Class" -> projectedClass | _ -> None
                        let node = value |> Option.map (fun projected -> $"{{\"name\":{System.Text.Json.JsonSerializer.Serialize projected}}}") |> Option.defaultValue "null"
                        ok $"{{\"data\":{{\"node\":{{\"fieldValueByName\":%s{node}}},\"rateLimit\":{{\"cost\":1,\"remaining\":1}}}}}}"
                    | Query(doc, _) when doc.Contains "projectItems(first" && doc.Contains "fieldValueByName" ->
                        let field =
                            projectedStatus
                            |> Option.map (fun status -> $"{{\"name\":\"%s{status}\"}}")
                            |> Option.defaultValue "null"
                        ok $"{{\"data\":{{\"repository\":{{\"issue\":{{\"projectItems\":{{\"nodes\":[{{\"project\":{{\"number\":1}},\"fieldValueByName\":%s{field}}}]}}}}}}}},\"rateLimit\":{{\"cost\":1,\"remaining\":1}}}}"
                    | Query(doc, _) when doc.Contains "projectItems(first" ->
                        let nodes = if boardAdded then "[{\"id\":\"PI\",\"project\":{\"number\":1}}]" else "[]"
                        ok ("{\"data\":{\"repository\":{\"issue\":{\"projectItems\":{\"nodes\":" + nodes + "}}}},\"rateLimit\":{\"cost\":1,\"remaining\":1}}")
                    | Query(doc, _) when doc.Contains "issue(number" -> ok "{\"data\":{\"repository\":{\"issue\":{\"id\":\"I\"}}},\"rateLimit\":{\"cost\":1,\"remaining\":1}}"
                    | Query(doc, _) when doc.Contains "addProjectV2ItemById" -> added <- added + 1; boardAdded <- true; ok "{\"data\":{\"addProjectV2ItemById\":{\"item\":{\"id\":\"PI\"}}},\"rateLimit\":{\"cost\":1,\"remaining\":1}}"
                    | Query(doc, _) when doc.Contains "updateProjectV2ItemFieldValue" -> statusWrites <- statusWrites + 1; projectedStatus <- Some "Backlog"; projectedClass <- Some "hardening"; ok "{\"data\":{\"updateProjectV2ItemFieldValue\":{\"projectV2Item\":{\"id\":\"PI\"}}},\"rateLimit\":{\"cost\":1,\"remaining\":1}}"
                    | _ -> Error(NotFound "unrecognised board request")
                | _ -> Error(NotFound "unrecognised request"))
            let code = invoke cache world
            if code <> Kernel.ExitGreen then failwith (String.concat "\n" world.Log)
            Assert.Equal(1, creates)
            Assert.Equal(1, added)
            Assert.Equal(1, statusWrites)
            match Cache.getIntakeReceipt "tx-2134" with
            | Ok(Some receipt) -> Assert.Equal(77, receipt.IssueNumber)
            | other -> failwithf "receipt was not durably persisted: %A" other
