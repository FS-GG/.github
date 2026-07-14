module FS.GG.Coord.GitHub.Tests.BoardTests

open System
open System.IO
open Xunit
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Errors
open FS.GG.Coord.GitHub.Transport
open FS.GG.Coord.GitHub.Board

/// Each test owns its cache — `boardWrite` queues into it, and a test inheriting another's queue would be
/// asserting on somebody else's deferred writes.
type private Sandbox() =
    let dir =
        Path.Combine(Path.GetTempPath(), "fsgg-board-test-" + Guid.NewGuid().ToString("N"))

    do
        Directory.CreateDirectory dir |> ignore
        Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", dir)

    member _.Dir = dir

    interface IDisposable with
        member _.Dispose() =
            Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", null)

            try
                Directory.Delete(dir, true)
            with _ ->
                ()

let private board =
    { Number = 12
      Id = "PVT_coord"
      Owner = "FS-GG"
      Title = "Coordination"
      Fields =
        Map.ofList
            [ "Status",
              { Id = "PVTSSF_status"
                Type = SingleSelect(Map.ofList [ "Ready", "opt_ready"; "In progress", "opt_wip" ]) }
              "Estimate", { Id = "PVTF_est"; Type = Number }
              "Blocked by", { Id = "PVTF_blocked"; Type = Text } ] }

let private ok (body: string) =
    Ok
        { Status = 200
          Body = body
          ETag = None
          NextLink = None }

let private serving (body: string) = Fake.Recorder(fun _ -> ok body)
let private failing (e: IoError) = Fake.Recorder(fun _ -> Error e)

/// The item-id lookup, answered with our board.
let private itemOnBoard =
    """{"data":{"repository":{"issue":{"projectItems":{"nodes":[{"id":"PVTI_coord123","project":{"number":12}}]}}}}}"""

let private scripted (responses: IoResult<Response> list) =
    let queue = System.Collections.Generic.Queue<IoResult<Response>>(responses)

    Fake.Recorder(fun _ ->
        if queue.Count = 0 then
            failwith "the transport was called more times than the test scripted"
        else
            queue.Dequeue())

// ---- #421: a failed lookup is not an absence -------------------------------------------------------

[<Fact>]
let ``#421 a rate-limited item lookup is RateLimited - it is NEVER 'not on board'`` () =
    // THE INCIDENT, IN ONE ASSERTION. Under an exhausted budget the lookup failed, the failure came back as
    // the empty string, and the caller read it as "this issue is not on the board" — then printed a
    // remediation telling the worker to run `item-add`, which CREATED A SECOND BOARD ITEM for an issue that
    // already had one.
    //
    // `Ok None` is what licenses an `item-add`. It must be UNREACHABLE from a failure.
    let transport = failing (RateLimited None)

    match itemId transport board "FS-GG" "FS.GG.SDD" 42 with
    | Error(RateLimited _) -> ()
    | Ok None -> failwith "a rate-limited lookup reported the item ABSENT — this is #421, and it creates a duplicate board item"
    | other -> failwith $"expected RateLimited — got %A{other}"

[<Fact>]
let ``#421 'not on board' is reachable ONLY from a successful read`` () =
    // The counterweight. A real, successful lookup that found no item for this issue IS a definite answer,
    // and it is the only thing that may license an add.
    let transport =
        serving """{"data":{"repository":{"issue":{"projectItems":{"nodes":[]}}}}}"""

    match itemId transport board "FS-GG" "FS.GG.SDD" 42 with
    | Ok None -> ()
    | other -> failwith $"a successful empty lookup is 'not on board' — got %A{other}"

[<Fact>]
let ``the item lookup narrows to OUR board - an issue can sit on several`` () =
    // Writing a Status to another board's item is a silent cross-board write: a no-op here, vandalism over
    // there.
    let transport =
        serving
            """{"data":{"repository":{"issue":{"projectItems":{"nodes":[
                 {"id":"PVTI_other","project":{"number":99}},
                 {"id":"PVTI_coord123","project":{"number":12}}]}}}}}"""

    match itemId transport board "FS-GG" "FS.GG.SDD" 42 with
    | Ok(Some id) -> Assert.Equal("PVTI_coord123", id)
    | other -> failwith $"the item on OUR board must be the one chosen — got %A{other}"

// ---- the empty-value trap --------------------------------------------------------------------------

[<Fact>]
let ``an empty Set is REFUSED - the API would treat it as a no-op and leave the old value`` () =
    // `gh project item-edit --text ''` is a NO-OP: the API answers "no changes to make", so an empty write
    // silently left the old value in place and the board went on displaying a `Blocked by` that had been
    // cleared. `Set ""` means one of two things and we cannot know which, so it is refused rather than
    // quietly reinterpreted.
    let transport = serving "{}"

    match setField transport board "PVTI_coord123" "Blocked by" (Set "") with
    | Error(Http(422, message)) -> Assert.Contains("Use `Clear`", message)
    | other -> failwith $"an empty Set must be refused — got %A{other}"

    // AND IT COSTS ZERO GRAPHQL. A rejected value must not spend the budget that dies first.
    Assert.Equal(0, transport.GraphQlCalls)

[<Fact>]
let ``Clear is a DIFFERENT MUTATION, and the log shows it`` () =
    let transport = serving """{"data":{"clearProjectV2ItemFieldValue":{"clientMutationId":null}}}"""

    match setField transport board "PVTI_coord123" "Blocked by" Clear with
    | Ok() ->
        Assert.True(transport.Logged "--clear")
        Assert.False(transport.Logged "--text ")
    | other -> failwith $"a clear must land — got %A{other}"

// ---- routing by field type -------------------------------------------------------------------------

[<Fact>]
let ``a SINGLE_SELECT value is routed to its OPTION ID`` () =
    let transport = serving """{"data":{"updateProjectV2ItemFieldValue":{"clientMutationId":null}}}"""

    match setField transport board "PVTI_coord123" "Status" (Set "In progress") with
    | Ok() ->
        Assert.True(transport.Logged "--single-select-option-id opt_wip")
        Assert.True(transport.Logged "--field-id PVTSSF_status")
        Assert.True(transport.Logged "--id PVTI_coord123")
    | other -> failwith $"the option must resolve — got %A{other}"

[<Fact>]
let ``an UNKNOWN single-select option is refused, and costs ZERO GraphQL`` () =
    // A rejected value must not spend the budget that dies first. And the refusal NAMES the options that
    // would have been accepted — one that does not merely moves the confusion one step later.
    let transport = serving "{}"

    match setField transport board "PVTI_coord123" "Status" (Set "Nonexistent") with
    | Error(Http(422, message)) ->
        Assert.Contains("not an option", message)
        Assert.Contains("Ready", message)
    | other -> failwith $"an unknown option must be refused — got %A{other}"

    Assert.Equal(0, transport.GraphQlCalls)

[<Fact>]
let ``an UNKNOWN field is refused, and costs ZERO GraphQL`` () =
    let transport = serving "{}"

    match setField transport board "PVTI_coord123" "Nonexistent" (Set "x") with
    | Error(Http(422, message)) -> Assert.Contains("no field named", message)
    | other -> failwith $"an unknown field must be refused — got %A{other}"

    Assert.Equal(0, transport.GraphQlCalls)

[<Fact>]
let ``a NUMBER field is validated by a REAL numeric parse, not a character class`` () =
    // `1.2.3`, `e`, `+` and `--` are all made of legal numeric CHARACTERS, and every one of them emits a
    // document that does not parse — a whole batch lost to a value nobody actually checked.
    let transport = serving "{}"

    for bad in [ "1.2.3"; "e"; "+"; "--"; "" ] do
        match setField transport board "PVTI_coord123" "Estimate" (Set bad) with
        | Error(Http(422, _)) -> ()
        | other -> failwith $"'%s{bad}' is not a number — got %A{other}"

    Assert.Equal(0, transport.GraphQlCalls)

[<Fact>]
let ``a NUMBER is sent as a JSON NUMBER, not a quoted string`` () =
    // GraphQL is typed: a NUMBER field wants `{"number": 3}` and rejects `{"number": "3"}`. This is the trap
    // that was documented and then closed — every variable used to serialise as a string.
    let transport = serving """{"data":{"updateProjectV2ItemFieldValue":{"clientMutationId":null}}}"""

    match setField transport board "PVTI_coord123" "Estimate" (Set "3") with
    | Ok() -> Assert.True(transport.Logged "--number 3")
    | other -> failwith $"a number must land — got %A{other}"

// ---- #448: the aliased batch -----------------------------------------------------------------------

[<Fact>]
let ``#448 THREE fields cost exactly ONE GraphQL call`` () =
    // GitHub bills `cost = max(1, nodes/100)`, and a field mutation returns ~1 node — so it hits the
    // ONE-POINT FLOOR and the cost of a placement pass tracks the REQUEST COUNT and nothing else. Three
    // requests are three points; one aliased document is one point.
    let transport =
        serving """{"data":{"f0":{"clientMutationId":null},"f1":{"clientMutationId":null},"f2":{"clientMutationId":null}}}"""

    match setFieldBatch transport board "PVTI_coord123" [ "Status", Set "Ready"; "Estimate", Set "3"; "Blocked by", Clear ] with
    | Ok() ->
        Assert.Equal(1, transport.GraphQlCalls)
        Assert.True(transport.Logged "batch-mutation mutation {")
        Assert.True(transport.Logged "f0: updateProjectV2ItemFieldValue")
        Assert.True(transport.Logged "f2: clearProjectV2ItemFieldValue")
    | other -> failwith $"the batch must land — got %A{other}"

[<Fact>]
let ``#448 a BAD pair in the batch is caught BEFORE any mutation is sent`` () =
    // Mutations execute SERIALLY. A bad pair caught late would not merely waste a point — it would fail the
    // document AFTER its earlier aliases had already been written, which is a half-written board nobody
    // asked for. The check is free, and it happens first.
    let transport = serving "{}"

    match setFieldBatch transport board "PVTI_coord123" [ "Status", Set "Ready"; "Status", Set "Nonexistent" ] with
    | Error(Http(422, _)) -> Assert.Equal(0, transport.GraphQlCalls)
    | other -> failwith $"a bad pair must refuse the whole document — got %A{other}"

[<Fact>]
let ``EX_PARTIAL - some aliases landed and the rest did not, and it is NEVER queued`` () =
    // A GraphQL failure mid-document arrives as HTTP **200** carrying BOTH `data` and `errors`, with
    // `errors[].path[0]` naming the failing alias. Mutations run serially, so the aliases before the
    // failure DID land — and the body says exactly which.
    let transport =
        serving
            """{"data":{"f0":{"clientMutationId":null},"f1":null},
                "errors":[{"path":["f1"],"message":"No such option"}]}"""

    match setFieldBatch transport board "PVTI_coord123" [ "Status", Set "Ready"; "Estimate", Set "3" ] with
    | Error(Partial(applied, failed)) ->
        Assert.Equal<string list>([ "f0" ], applied)
        Assert.Equal(1, List.length failed)

        // Replaying the document would rewrite the half that already took effect. This is the one failure
        // that may never be queued, and the type says so.
        Assert.False(isQueueable (Partial(applied, failed)))
        Assert.Equal(Errors.ExPartial, exitCode (Partial(applied, failed)))

    | other -> failwith $"a partial apply must be reported as one — got %A{other}"

[<Fact>]
let ``a batch where NOTHING landed is a clean failure, not a partial one`` () =
    // The board is exactly as it was, so this is safe to retry or queue. Reporting it as PARTIAL would
    // refuse to queue a write that was never made.
    let transport =
        serving """{"data":{"f0":null},"errors":[{"path":["f0"],"message":"No such option"}]}"""

    match setFieldBatch transport board "PVTI_coord123" [ "Status", Set "Ready" ] with
    | Error(GraphQlErrors _) -> ()
    | other -> failwith $"nothing landed, so this is not a partial write — got %A{other}"

[<Fact>]
let ``a RATE LIMIT is tested BEFORE the partial arm - or it reads as a half-written board`` () =
    // GitHub reports an exhausted GraphQL budget as an HTTP **200** carrying `errors`, exactly like a failed
    // alias. Test the partial arm first and an exhausted budget is misreported as "the board is
    // half-written" — the caller then refuses to queue it (a partial is never queued) and the write is
    // silently LOST, on a condition that was only ever temporary.
    let transport =
        serving """{"data":{"f0":null},"errors":[{"path":["f0"],"message":"API rate limit exceeded"}]}"""

    match setFieldBatch transport board "PVTI_coord123" [ "Status", Set "Ready" ] with
    | Error(RateLimited _) -> ()
    | Error(Partial _) -> failwith "an exhausted budget was misreported as a half-written board — the write would be silently lost"
    | other -> failwith $"expected RateLimited — got %A{other}"

// ---- #510: the ONE board write, and the queue ------------------------------------------------------

[<Fact>]
let ``#510 an exhausted budget DEFERS the board write - and it is really queued`` () =
    use _sandbox = new Sandbox()

    let transport =
        scripted [ ok itemOnBoard; Error(RateLimited None) ]

    match boardWrite transport board "FS-GG" "FS.GG.SDD" 810 "Status" (Set "In progress") "vole-418" with
    | Ok Deferred ->
        match Cache.pending () with
        | Ok [ one ] ->
            Assert.Equal("FS-GG/FS.GG.SDD#810", one.Ref)
            Assert.Equal("Status", one.Field)
            Assert.Equal("In progress", one.Value)
        | other -> failwith $"the deferred write must actually be in the queue — got %A{other}"
    | other -> failwith $"an exhausted budget must defer — got %A{other}"

[<Fact>]
let ``#510 a REFUSED write is NEVER queued - replaying it would loop forever`` () =
    use _sandbox = new Sandbox()

    // A bad field, a bad option, a non-ref `Blocked by`. `flush` would replay it forever, each replay
    // failing identically, the queue never draining, and the refusal never reaching the human who could fix
    // it. The tool would go on reporting success over a write it had dropped.
    let transport = scripted [ ok itemOnBoard ]

    match boardWrite transport board "FS-GG" "FS.GG.SDD" 810 "Nonexistent" (Set "x") "vole-418" with
    | Error(Http(422, _)) ->
        match Cache.pending () with
        | Ok [] -> ()
        | other -> failwith $"a permanent refusal must not be queued — got %A{other}"
    | other -> failwith $"a bad field must be refused — got %A{other}"

[<Fact>]
let ``#510 an issue NOT on the board is permanent - reported, and NOT queued`` () =
    use _sandbox = new Sandbox()

    let transport =
        scripted [ ok """{"data":{"repository":{"issue":{"projectItems":{"nodes":[]}}}}}""" ]

    match boardWrite transport board "FS-GG" "FS.GG.SDD" 810 "Status" (Set "Ready") "vole-418" with
    | Ok NotOnBoard ->
        // `flush` would drop it too, so queuing it would be a second promise nobody could keep.
        match Cache.pending () with
        | Ok [] -> ()
        | other -> failwith $"an off-board item must not be queued — got %A{other}"
    | other -> failwith $"expected NotOnBoard — got %A{other}"

[<Fact>]
let ``flush replays a queued write and DROPS it`` () =
    use _sandbox = new Sandbox()

    let deferring = scripted [ ok itemOnBoard; Error(RateLimited None) ]
    boardWrite deferring board "FS-GG" "FS.GG.SDD" 810 "Status" (Set "Ready") "vole-418" |> ignore

    let replaying =
        scripted [ ok itemOnBoard; ok """{"data":{"updateProjectV2ItemFieldValue":{"clientMutationId":null}}}""" ]

    match flush replaying board with
    | Ok 1 ->
        // UNLINKED, not truncated. An empty file is a claim — "there is a queue and it is empty" — and that
        // is a statement about state nobody made.
        match Cache.pending () with
        | Ok [] -> ()
        | other -> failwith $"the replayed entry must be gone — got %A{other}"
    | other -> failwith $"flush must replay the queued write — got %A{other}"

[<Fact>]
let ``an exhausted budget STOPS the flush - the rest would fail identically`` () =
    use _sandbox = new Sandbox()

    let deferring =
        scripted
            [ ok itemOnBoard
              Error(RateLimited None)
              ok itemOnBoard
              Error(RateLimited None) ]

    boardWrite deferring board "FS-GG" "FS.GG.SDD" 810 "Status" (Set "Ready") "vole-418" |> ignore
    boardWrite deferring board "FS-GG" "FS.GG.SDD" 811 "Status" (Set "Ready") "vole-418" |> ignore

    // Spending REST calls to confirm that the budget is still exhausted is exactly the back-off EX_RATE
    // exists to signal. The remainder stays queued.
    let stillLimited = Fake.Recorder(fun _ -> Error(RateLimited None))

    match flush stillLimited board with
    | Error(RateLimited _) ->
        match Cache.pending () with
        | Ok entries -> Assert.Equal(2, List.length entries)
        | other -> failwith $"the queue must survive a stopped flush — got %A{other}"
    | other -> failwith $"an exhausted budget must stop the flush — got %A{other}"

[<Fact>]
let ``a stopped flush does NOT RE-QUEUE what it was replaying - the queue must not grow`` () =
    use _sandbox = new Sandbox()

    // THE BUG THIS SUITE CAUGHT, PINNED SO IT CANNOT COME BACK.
    //
    // `flush` originally replayed each entry through `boardWrite` — which carries the DEFER policy. So on
    // an exhausted budget the replay QUEUED the entry it was replaying. It was already in the queue. Every
    // flush under a dead budget therefore DOUBLED the queue, forever, while reporting that it had written
    // nothing and backing off from nothing.
    //
    // A queue that grows every time you try to drain it is worse than no queue at all: it is a promise that
    // gets louder the less able it is to keep it.
    let deferring = scripted [ ok itemOnBoard; Error(RateLimited None) ]
    boardWrite deferring board "FS-GG" "FS.GG.SDD" 810 "Status" (Set "Ready") "vole-418" |> ignore

    let depthBefore =
        match Cache.pending () with
        | Ok entries -> List.length entries
        | other -> failwith $"the write must be queued — got %A{other}"

    Assert.Equal(1, depthBefore)

    let stillLimited = Fake.Recorder(fun _ -> Error(RateLimited None))

    // Three flushes, all of them meeting the same dead budget.
    flush stillLimited board |> ignore
    flush stillLimited board |> ignore
    flush stillLimited board |> ignore

    match Cache.pending () with
    | Ok entries -> Assert.Equal(depthBefore, List.length entries)
    | other -> failwith $"the queue must not grow when a flush cannot drain it — got %A{other}"

// ---- bootstrap ------------------------------------------------------------------------------------

[<Fact>]
let ``a landed write folds into the cached scan for THIS board, not a hardcoded one`` () =
    use _sandbox = new Sandbox()

    // The scan cache is keyed on (owner, title), and `FSGG_COORD_OWNER` / `FSGG_COORD_PROJECT` can point
    // this client at a different board. A hardcoded title would fold the write into ANOTHER board's cache
    // file — or into none at all — and both are invisible: the write lands on the real board, the cache goes
    // on serving the old value for the next 90 seconds, and the very next `take` schedules against it.
    //
    // Folding rather than INVALIDATING is itself the point: invalidating would send the next `take` back to
    // a full-board scan, and a claim is ALWAYS followed by a take, so the cache would never survive the loop
    // it exists for.
    let scan = """[{"repo":"FS.GG.SDD","number":810,"status":"Ready"}]"""
    Assert.True(Cache.putScan board.Owner board.Title scan)

    let transport =
        scripted [ ok itemOnBoard; ok """{"data":{"updateProjectV2ItemFieldValue":{"clientMutationId":null}}}""" ]

    match boardWrite transport board "FS-GG" "FS.GG.SDD" 810 "Status" (Set "In progress") "vole-418" with
    | Ok Written ->
        match Cache.getScan Cache.Scheduling board.Owner board.Title with
        | Some folded -> Assert.Contains("In progress", folded)
        | None -> failwith "the cached scan must still be there, carrying our own write"
    | other -> failwith $"the write must land — got %A{other}"

[<Fact>]
let ``bootstrap resolves the field and option ids in TWO GraphQL calls`` () =
    let transport =
        scripted
            [ ok """{"data":{"organization":{"projectsV2":{"nodes":[{"number":12,"title":"Coordination","id":"PVT_coord"}]}}}}"""
              ok """{"data":{"organization":{"projectV2":{"fields":{"nodes":[
                     {"id":"PVTSSF_status","name":"Status","dataType":"SINGLE_SELECT","options":[{"id":"opt_ready","name":"Ready"}]},
                     {"id":"PVTF_est","name":"Estimate","dataType":"NUMBER"}]}}}}}""" ]

    match bootstrap transport "FS-GG" "Coordination" with
    | Ok b ->
        Assert.Equal(2, transport.GraphQlCalls)
        Assert.Equal(12, b.Number)
        Assert.Equal("PVT_coord", b.Id)

        // The board carries the owner and title it was resolved FROM, because the scan cache is keyed on
        // them and a write that folds itself into the cache must fold into the RIGHT one.
        Assert.Equal("FS-GG", b.Owner)
        Assert.Equal("Coordination", b.Title)

        match Map.tryFind "Status" b.Fields with
        | Some { Type = SingleSelect options } -> Assert.Equal("opt_ready", options.["Ready"])
        | other -> failwith $"Status must be a single-select with its options — got %A{other}"

    | other -> failwith $"bootstrap must resolve the board — got %A{other}"

[<Fact>]
let ``a board that reports NO fields is a failed read, and is never cached`` () =
    // Every board has at least `Status`. An empty field map is not an austere board — it is a document we
    // failed to walk, and caching it would make every write for the next DAY fail with "no field named
    // Status".
    let transport =
        scripted
            [ ok """{"data":{"organization":{"projectsV2":{"nodes":[{"number":12,"title":"Coordination","id":"PVT_coord"}]}}}}"""
              ok """{"data":{"organization":{"projectV2":{"fields":{"nodes":[]}}}}}""" ]

    match bootstrap transport "FS-GG" "Coordination" with
    | Error(Malformed _) -> ()
    | other -> failwith $"an empty field map must refuse — got %A{other}"

[<Fact>]
let ``a board TITLE that does not exist is named back, so it can be fixed`` () =
    let transport =
        serving """{"data":{"organization":{"projectsV2":{"nodes":[{"number":12,"title":"Something Else","id":"PVT_x"}]}}}}"""

    match bootstrap transport "FS-GG" "Coordination" with
    | Error(NotFound message) -> Assert.Contains("Coordination", message)
    | other -> failwith $"a missing board must name the title it looked for — got %A{other}"
