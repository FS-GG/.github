module FS.GG.Coord.GitHub.Tests.GraphQlBoundaryTests

open System.Text.Json
open System.IO
open Xunit
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Errors
open FS.GG.Coord.GitHub.Transport

type private Item = { Id: string }

let private item (node: JsonElement) =
    match node.TryGetProperty "id" with
    | true, value when value.ValueKind = JsonValueKind.String -> Ok { Id = value.GetString() }
    | _ -> Error(Malformed("fixture", "missing id"))

let private page subject body =
    GraphQl.decode subject body (fun data ->
        GraphQl.page subject "fixture connection" (fun value -> value.Id) item (data.GetProperty "connection"))

[<Fact>]
let ``mixed data and errors is never a successful typed value`` () =
    let body = """{"data":{"answer":42},"errors":[{"message":"field failed"}]}"""
    match GraphQl.decode "mixed" body (fun data -> Ok(data.GetProperty("answer").GetInt32())) with
    | Error(GraphQlErrors [ "field failed" ]) -> ()
    | other -> failwith $"expected generic GraphQL refusal, got %A{other}"

[<Fact>]
let ``rate limit carries retry and rate metadata`` () =
    let body = """{"data":{"answer":42},"errors":[{"message":"API rate limit exceeded"}]}"""
    match GraphQl.decode "limited" body (fun _ -> Ok 42) with
    | Error error ->
        let metadata = GraphQl.classify error
        match metadata.Retry, metadata.RateLimit with
        | GraphQl.Retryable Primary, Some(GraphQlBudget, None) -> ()
        | other -> failwith $"expected primary GraphQL retry metadata, got %A{other}"
    | Ok value -> failwith $"partial response escaped as %d{value}"

[<Fact>]
let ``repeated cursor refuses instead of looping or succeeding short`` () =
    let bodies =
        [ """{"data":{"connection":{"totalCount":2,"nodes":[{"id":"a"}],"pageInfo":{"hasNextPage":true,"endCursor":"same"}}}}"""
          """{"data":{"connection":{"totalCount":2,"nodes":[{"id":"b"}],"pageInfo":{"hasNextPage":true,"endCursor":"same"}}}}""" ]
    let mutable index = 0
    let fetch _ = let result = page "repeat" bodies.[index] in index <- index + 1; result
    match GraphQl.drain "repeat" "fixture connection" { MaxPages = 5; MaxItems = 10 } fetch with
    | Error(Malformed(_, detail)) -> Assert.Contains("repeated cursor", detail)
    | other -> failwith $"expected repeated-cursor refusal, got %A{other}"

[<Fact>]
let ``duplicate identity across pages exposes page-boundary mutation`` () =
    let bodies =
        [ """{"data":{"connection":{"totalCount":2,"nodes":[{"id":"a"}],"pageInfo":{"hasNextPage":true,"endCursor":"one"}}}}"""
          """{"data":{"connection":{"totalCount":2,"nodes":[{"id":"a"}],"pageInfo":{"hasNextPage":false,"endCursor":null}}}}""" ]
    let mutable index = 0
    let fetch _ = let result = page "mutation" bodies.[index] in index <- index + 1; result
    match GraphQl.drain "mutation" "fixture connection" { MaxPages = 5; MaxItems = 10 } fetch with
    | Error(Malformed(_, detail)) -> Assert.Contains("mutated", detail)
    | other -> failwith $"expected mutation refusal, got %A{other}"

[<Fact>]
let ``changing total count across pages is a typed incomplete read`` () =
    let bodies =
        [ """{"data":{"connection":{"totalCount":2,"nodes":[{"id":"a"}],"pageInfo":{"hasNextPage":true,"endCursor":"one"}}}}"""
          """{"data":{"connection":{"totalCount":3,"nodes":[{"id":"b"}],"pageInfo":{"hasNextPage":false,"endCursor":null}}}}""" ]
    let mutable index = 0
    let fetch _ = let result = page "count" bodies.[index] in index <- index + 1; result
    match GraphQl.drain "count" "fixture connection" { MaxPages = 5; MaxItems = 10 } fetch with
    | Error(Malformed(_, detail)) -> Assert.Contains("totalCount changed", detail)
    | other -> failwith $"expected count-mutation refusal, got %A{other}"

[<Fact>]
let ``empty continuing page and explicit item limit both fail closed`` () =
    let empty = """{"data":{"connection":{"totalCount":1,"nodes":[],"pageInfo":{"hasNextPage":true,"endCursor":"one"}}}}"""
    match page "empty" empty with
    | Error(Malformed(_, detail)) -> Assert.Contains("empty page", detail)
    | other -> failwith $"expected empty-page refusal, got %A{other}"

    let full = """{"data":{"connection":{"totalCount":2,"nodes":[{"id":"a"},{"id":"b"}],"pageInfo":{"hasNextPage":false,"endCursor":null}}}}"""
    match GraphQl.drain "limit" "fixture connection" { MaxPages = 1; MaxItems = 1 } (fun _ -> page "limit" full) with
    | Error(Malformed(_, detail)) -> Assert.Contains("item limit", detail)
    | other -> failwith $"expected explicit-limit refusal, got %A{other}"

let private response body =
    Ok { Status = 200; Body = body; Headers = Map.empty; ETag = None; NextLink = None }

[<Fact>]
let ``operational project lookup drains every page and returns one typed visibility`` () =
    let replies =
        System.Collections.Generic.Queue<_>(
            [ response """{"data":{"organization":{"projectsV2":{"totalCount":2,"nodes":[{"id":"one","title":"Other","public":false,"number":2}],"pageInfo":{"hasNextPage":true,"endCursor":"next"}}},"rateLimit":{"cost":1,"remaining":99}}}"""
              response """{"data":{"organization":{"projectsV2":{"totalCount":2,"nodes":[{"id":"target","title":"Coordination","public":true,"number":1}],"pageInfo":{"hasNextPage":false,"endCursor":null}}},"rateLimit":{"cost":1,"remaining":98}}}""" ])
    let transport = Fake.Recorder(fun _ -> replies.Dequeue())
    match OperationalGraphQl.projectVisibility transport "FS-GG" "Coordination" with
    | Ok(Some true) -> Assert.Equal(2, transport.GraphQlCalls)
    | other -> failwith $"expected typed visibility, got %A{other}"

[<Fact>]
let ``operational project lookup refuses duplicate title rather than choosing`` () =
    let body = """{"data":{"organization":{"projectsV2":{"totalCount":2,"nodes":[{"id":"one","title":"Coordination","public":false,"number":1},{"id":"two","title":"Coordination","public":true,"number":2}],"pageInfo":{"hasNextPage":false,"endCursor":null}}},"rateLimit":{"cost":1,"remaining":99}}}"""
    let transport = Fake.Recorder(fun _ -> response body)
    match OperationalGraphQl.projectVisibility transport "FS-GG" "Coordination" with
    | Error(Malformed(_, detail)) -> Assert.Contains("found 2", detail)
    | other -> failwith $"expected ambiguous-project refusal, got %A{other}"

[<Fact>]
let ``operational repository policy never inverts mixed data and errors`` () =
    let body = """{"data":{"repository":{"issueCreationPolicy":"COLLABORATORS_ONLY","hasIssuesEnabled":true}},"errors":[{"message":"field denied"}]}"""
    let transport = Fake.Recorder(fun _ -> response body)
    match OperationalGraphQl.repositoryPolicy transport "FS-GG" ".github" with
    | Error(GraphQlErrors [ "field denied" ]) -> ()
    | other -> failwith $"expected errors-first refusal, got %A{other}"

[<Fact>]
let ``#3091 repository policy observes every merge capability and selects deterministically`` () =
    let body = """{"data":{"repository":{"id":"R_fixture","issueCreationPolicy":"COLLABORATORS_ONLY","hasIssuesEnabled":true,"mergeCommitAllowed":true,"squashMergeAllowed":true,"rebaseMergeAllowed":true},"rateLimit":{"cost":1,"remaining":99}}}"""
    let transport = Fake.Recorder(fun _ -> response body)

    match OperationalGraphQl.repositoryPolicy transport "FS-GG" ".github" with
    | Ok policy ->
        Assert.True(policy.MergeCommitAllowed)
        Assert.True(policy.SquashMergeAllowed)
        Assert.True(policy.RebaseMergeAllowed)
        Assert.Equal(OperationalGraphQl.Selected OperationalGraphQl.Squash, OperationalGraphQl.selectMergeMethod policy)
    | other -> failwith $"expected complete repository policy, got %A{other}"

[<Theory>]
[<InlineData(false, false, true, "rebase")>]
[<InlineData(true, false, false, "merge")>]
[<InlineData(true, false, true, "rebase")>]
[<InlineData(false, false, false, "none")>]
let ``#3091 merge selection covers sole fallback and no-method policy``
    (mergeAllowed: bool, squashAllowed: bool, rebaseAllowed: bool, expected: string) =
    let policy : OperationalGraphQl.RepositoryPolicy =
        { RepositoryId = "R_fixture"
          IssueCreationPolicy = "COLLABORATORS_ONLY"
          HasIssuesEnabled = true
          MergeCommitAllowed = mergeAllowed
          SquashMergeAllowed = squashAllowed
          RebaseMergeAllowed = rebaseAllowed }

    let actual =
        match OperationalGraphQl.selectMergeMethod policy with
        | OperationalGraphQl.Selected OperationalGraphQl.Squash -> "squash"
        | OperationalGraphQl.Selected OperationalGraphQl.Rebase -> "rebase"
        | OperationalGraphQl.Selected OperationalGraphQl.Merge -> "merge"
        | OperationalGraphQl.NoAllowedMethod -> "none"

    Assert.Equal(expected, actual)

[<Fact>]
let ``#3091 incomplete merge policy fails closed`` () =
    let body = """{"data":{"repository":{"issueCreationPolicy":"COLLABORATORS_ONLY","hasIssuesEnabled":true,"mergeCommitAllowed":false,"squashMergeAllowed":true},"rateLimit":{"cost":1,"remaining":99}}}"""
    let transport = Fake.Recorder(fun _ -> response body)

    match OperationalGraphQl.repositoryPolicy transport "FS-GG" ".github" with
    | Error(Malformed(_, detail)) -> Assert.Contains("incomplete", detail)
    | other -> failwith $"expected incomplete-policy refusal, got %A{other}"

[<Fact>]
let ``board intake shares repository policy and author permission while rebinding edited issues`` () =
    let mutable revision = 0
    let isPolicy (request: Request) = match request.Body with Query(document,_) -> document.Contains "issueCreationPolicy" | _ -> false
    let transport = Fake.Recorder(fun request ->
        if isPolicy request then
            response """{"data":{"repository":{"id":"R_repo","issueCreationPolicy":"COLLABORATORS_ONLY","hasIssuesEnabled":true,"mergeCommitAllowed":true,"squashMergeAllowed":true,"rebaseMergeAllowed":true},"rateLimit":{"cost":1,"remaining":99}}}"""
        elif request.Budget = GraphQl then
            match request.Body with Query(document,_) -> Assert.Contains("... on Node{id}",document) | _ -> failwith "expected typed issue identity query"
            revision <- revision + 1
            response ($"{{\"data\":{{\"repository\":{{\"issue\":{{\"id\":\"I_issue\",\"updatedAt\":\"2026-09-10T00:00:0%d{revision}Z\",\"author\":{{\"id\":\"U_author\",\"login\":\"maintainer\"}}}}}},\"rateLimit\":{{\"cost\":1,\"remaining\":98}}}}}}")
        elif request.Path.Contains("/collaborators/") then response """{"permission":"write","user":{"node_id":"U_author"}}"""
        else failwith "unexpected request")
    let cache = Path.Combine(Path.GetTempPath(),System.Guid.NewGuid().ToString("N"))
    let gate = BoardIntake.Gate(transport,System.TimeSpan.FromMinutes 5.,cache)
    let require = function Ok value -> value | Error error -> failwith $"unexpected refusal: %A{error}"
    let first = gate.Authorize("FS-GG","demo",1) |> require
    let edited = gate.Authorize("FS-GG","demo",1) |> require
    let laterBatch = BoardIntake.Gate(transport,cacheRoot=cache).Authorize("FS-GG","demo",1) |> require
    Assert.False(first.UpdatedAt = edited.UpdatedAt)
    Assert.False(edited.UpdatedAt = laterBatch.UpdatedAt)
    Assert.Equal(4,transport.GraphQlCalls) // one policy, three current issue revisions across two processes
    Assert.Equal(1,transport.RestCalls) // one shared permission read

[<Fact>]
let ``board intake refuses all-users policy and current read-only author`` () =
    let mutable openPolicy = true
    let isPolicy (request: Request) = match request.Body with Query(document,_) -> document.Contains "issueCreationPolicy" | _ -> false
    let transport = Fake.Recorder(fun request ->
        if isPolicy request then
            let policy = if openPolicy then "ALL" else "COLLABORATORS_ONLY"
            openPolicy <- false
            response ($"{{\"data\":{{\"repository\":{{\"id\":\"R_repo\",\"issueCreationPolicy\":\"%s{policy}\",\"hasIssuesEnabled\":true,\"mergeCommitAllowed\":true,\"squashMergeAllowed\":true,\"rebaseMergeAllowed\":true}},\"rateLimit\":{{\"cost\":1,\"remaining\":99}}}}}}")
        elif request.Budget = GraphQl then response """{"data":{"repository":{"issue":{"id":"I_issue","updatedAt":"2026-09-10T00:00:00Z","author":{"id":"U_author","login":"reader"}}},"rateLimit":{"cost":1,"remaining":98}}}"""
        else response """{"permission":"read","user":{"node_id":"U_author"}}""")
    let cache = Path.Combine(Path.GetTempPath(),System.Guid.NewGuid().ToString("N"))
    match BoardIntake.Gate(transport,cacheRoot=Path.Combine(Path.GetTempPath(),System.Guid.NewGuid().ToString("N"))).Authorize("FS-GG","demo",1) with
    | Error(Malformed(_,detail)) -> Assert.Contains("creation policy",detail)
    | other -> failwith $"expected open-policy refusal, got %A{other}"
    match BoardIntake.Gate(transport,cacheRoot=cache).Authorize("FS-GG","demo",1) with
    | Error(Malformed(_,detail)) -> Assert.Contains("below write",detail)
    | other -> failwith $"expected read-author refusal, got %A{other}"

[<Fact>]
let ``operational archive mutation preserves partial alias accounting`` () =
    let body = """{"data":{"a0":{"item":{"id":"one"}},"a1":null},"errors":[{"message":"denied","path":["a1"]}]}"""
    let transport = Fake.Recorder(fun _ -> response body)
    match OperationalGraphQl.archiveItems transport "PVT_board" [ "one"; "two" ] with
    | Error(Partial([ "a0" ], [ ("a1", "denied") ])) -> ()
    | other -> failwith $"expected exact partial mutation facts, got %A{other}"

[<Fact>]
let ``operational roster board refuses a partially resolved issue node`` () =
    let projects = """{"data":{"organization":{"projectsV2":{"totalCount":1,"nodes":[{"id":"board","title":"Coordination","public":false,"number":1}],"pageInfo":{"hasNextPage":false,"endCursor":null}}},"rateLimit":{"cost":1,"remaining":99}}}"""
    let items = """{"data":{"organization":{"projectV2":{"items":{"totalCount":1,"nodes":[{"id":"row","status":{"name":"Ready"},"content":{"__typename":"Issue","number":1}}],"pageInfo":{"hasNextPage":false,"endCursor":null}}}},"rateLimit":{"cost":1,"remaining":98}}}"""
    let replies = System.Collections.Generic.Queue<_>([ response projects; response items ])
    let transport = Fake.Recorder(fun _ -> replies.Dequeue())
    match OperationalGraphQl.rosterBoard transport "FS-GG" "Coordination" with
    | Error(Malformed(_, detail)) -> Assert.Contains("roster board node", detail)
    | other -> failwith $"expected partial-node refusal, got %A{other}"

[<Fact>]
let ``board intake persistent observations retain their original expiry`` () =
    let mutable now = System.DateTimeOffset.Parse "2026-09-10T00:00:00Z"
    let mutable policyReads = 0
    let mutable permissionReads = 0
    let isPolicy (request: Request) = match request.Body with Query(document,_) -> document.Contains "issueCreationPolicy" | _ -> false
    let transport = Fake.Recorder(fun request ->
        if isPolicy request then
            policyReads <- policyReads + 1
            response """{"data":{"repository":{"id":"R_repo","issueCreationPolicy":"COLLABORATORS_ONLY","hasIssuesEnabled":true,"mergeCommitAllowed":true,"squashMergeAllowed":true,"rebaseMergeAllowed":true},"rateLimit":{"cost":1,"remaining":99}}}"""
        elif request.Budget = GraphQl then response """{"data":{"repository":{"issue":{"id":"I_issue","updatedAt":"2026-09-10T00:00:00Z","author":{"id":"U_author","login":"maintainer"}}},"rateLimit":{"cost":1,"remaining":98}}}"""
        else
            permissionReads <- permissionReads + 1
            response (if permissionReads = 1 then """{"permission":"write","user":{"node_id":"U_author"}}""" else """{"permission":"admin","user":{"node_id":"U_author"}}"""))
    let cache = Path.Combine(Path.GetTempPath(),System.Guid.NewGuid().ToString("N"))
    let first = BoardIntake.Gate(transport,System.TimeSpan.FromSeconds 30.,cache,fun () -> now)
    first.Authorize("FS-GG","CaseRepo",1) |> ignore
    now <- now.AddSeconds 29.
    let loaded = BoardIntake.Gate(transport,System.TimeSpan.FromSeconds 30.,cache,fun () -> now)
    loaded.Authorize("fs-gg","caserepo",1) |> ignore
    Assert.Equal(1,policyReads)
    now <- now.AddSeconds 2.
    let refreshed = loaded.Authorize("FS-GG","CaseRepo",1) |> Result.defaultWith (fun error -> failwithf "%A" error)
    Assert.Equal(2,policyReads)
    Assert.Equal("admin",refreshed.Permission)
    now <- now.AddSeconds -1.
    BoardIntake.Gate(transport,System.TimeSpan.FromSeconds 30.,cache,fun () -> now).Authorize("FS-GG","CaseRepo",1) |> ignore
    Assert.Equal(3,policyReads) // a future-dated observation never extends the revocation window

[<Fact>]
let ``board intake never authorizes from corrupt persistent evidence when refresh fails`` () =
    let good = Fake.Recorder(fun request ->
        match request.Body with
        | Query(document,_) when document.Contains "issueCreationPolicy" -> response """{"data":{"repository":{"id":"R_repo","issueCreationPolicy":"COLLABORATORS_ONLY","hasIssuesEnabled":true,"mergeCommitAllowed":true,"squashMergeAllowed":true,"rebaseMergeAllowed":true},"rateLimit":{"cost":1,"remaining":99}}}"""
        | Query _ -> response """{"data":{"repository":{"issue":{"id":"I_issue","updatedAt":"2026-09-10T00:00:00Z","author":{"id":"U_author","login":"maintainer"}}},"rateLimit":{"cost":1,"remaining":98}}}"""
        | _ -> response """{"permission":"write","user":{"node_id":"U_author"}}""")
    let cache = Path.Combine(Path.GetTempPath(),System.Guid.NewGuid().ToString("N"))
    BoardIntake.Gate(good,cacheRoot=cache).Authorize("FS-GG","demo",1) |> ignore
    Directory.EnumerateFiles(Path.Combine(cache,"board-intake-v1"),"policy-*.json")
    |> Seq.iter (fun path -> File.WriteAllText(path,"{}"))
    let failed = Fake.Recorder(fun _ -> Error(Transport "refresh unavailable"))
    match BoardIntake.Gate(failed,cacheRoot=cache).Authorize("FS-GG","demo",1) with
    | Error(Transport detail) -> Assert.Contains("unavailable",detail)
    | other -> failwith $"corrupt cache must require a successful live refresh, got %A{other}"
