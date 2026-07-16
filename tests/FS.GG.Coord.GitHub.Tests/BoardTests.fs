module FS.GG.Coord.GitHub.Tests.BoardTests

open System
open System.IO
open Xunit
open FS.GG.Coord.Types
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

// ---- add: the verb the port dropped (#861) ---------------------------------------------------------

/// Not on this board, then the issue's node id, then the mutation.
let private addResponses =
    [ ok """{"data":{"repository":{"issue":{"projectItems":{"nodes":[]}}}}}"""
      ok """{"data":{"repository":{"issue":{"id":"I_issue42"}}}}"""
      ok """{"data":{"addProjectV2ItemById":{"item":{"id":"PVTI_added"}}}}""" ]

[<Fact>]
let ``#421 addItem REFUSES to add on a failed lookup - and spends no mutation`` () =
    // THE REASON THIS FUNCTION HAS A SHAPE AT ALL. `Ok None` licenses the mutation; an Error must not —
    // unreachable is not absent, which is #421's actual finding and #266's class.
    //
    // NOT because it would duplicate the row: `addProjectV2ItemById` is idempotent server-side, measured
    // (#861). Because a write decided from a read that did not happen is a definite answer built on no
    // information — and it would report `AddedToBoard` for an issue whose presence was never established.
    //
    // The call count is the real assertion. "Returned an Error" would also be true of a version that added
    // first and reported the failure afterwards.
    use _sandbox = new Sandbox()

    let transport = scripted [ Error(RateLimited None) ]

    match addItem transport board "FS-GG" "FS.GG.SDD" 42 with
    | Error(RateLimited _) -> ()
    | Ok(AddedToBoard _) -> failwith "added the item on a FAILED lookup — this is #421, and it duplicates a board row"
    | other -> failwith $"expected RateLimited — got %A{other}"

    Assert.Equal(1, transport.GraphQlCalls)

[<Fact>]
let ``addItem is IDEMPOTENT - an issue already on the board is a success, and writes nothing`` () =
    // `add` is the second line of the recipe's filing procedure, so a retry, a close-out pass, or two
    // workers racing the same follow-up all reach it. None of them may create a twin, and none of them is
    // an error.
    use _sandbox = new Sandbox()

    let transport = scripted [ ok itemOnBoard ]

    match addItem transport board "FS-GG" "FS.GG.SDD" 42 with
    | Ok(AlreadyOnBoard id) -> Assert.Equal("PVTI_coord123", id)
    | other -> failwith $"an issue already on the board is AlreadyOnBoard — got %A{other}"

    // One read, no mutation.
    Assert.Equal(1, transport.GraphQlCalls)

[<Fact>]
let ``addItem adds when the read DEFINITELY says not-on-board, and returns the new item id`` () =
    use _sandbox = new Sandbox()

    let transport = scripted addResponses

    match addItem transport board "FS-GG" "FS.GG.SDD" 42 with
    | Ok(AddedToBoard id) -> Assert.Equal("PVTI_added", id)
    | other -> failwith $"a definite absence licenses the add — got %A{other}"

    Assert.True(transport.Logged "item-add", "the add mutation must actually be sent")

[<Fact>]
let ``addItem sends the ISSUE's node id as contentId, not the board item id`` () =
    // `addProjectV2ItemById` takes `contentId` — the ISSUE's node id. Passing an item id would be a
    // different object entirely, and the API would reject it or attach the wrong thing.
    use _sandbox = new Sandbox()

    let mutable doc = ""
    let mutable vars: (string * Var) list = []
    let queue = System.Collections.Generic.Queue<IoResult<Response>>(addResponses)

    let transport =
        Fake.Recorder(fun (req: Request) ->
            match req.Body with
            | Query(d, v) when d.Contains "addProjectV2ItemById" ->
                doc <- d
                vars <- v
            | _ -> ()

            queue.Dequeue())

    addItem transport board "FS-GG" "FS.GG.SDD" 42 |> ignore

    Assert.Equal<Var>(VId "I_issue42", vars |> List.find (fun (k, _) -> k = "contentId") |> snd)
    Assert.Equal<Var>(VId "PVT_coord", vars |> List.find (fun (k, _) -> k = "projectId") |> snd)

    // #848, one verb along: both of THESE really are `ID!` — verified against the live schema, not assumed
    // from the shape of the string. The declaration is the thing the API validates.
    Assert.Contains("$projectId: ID!", doc)
    Assert.Contains("$contentId: ID!", doc)

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

// ---- the pre-claim column (#481) -------------------------------------------------------------------

[<Fact>]
let ``itemStatus reads the item's current Status column, narrowed to OUR board`` () =
    // #481's pre-claim read: the column a claim is about to overwrite. The wrong-board node is ignored, and
    // the Status name comes back through the ONE `statusOfName` parser as the typed column.
    let transport =
        serving
            """{"data":{"repository":{"issue":{"projectItems":{"nodes":[
                 {"project":{"number":99},"fieldValueByName":{"name":"Ready"}},
                 {"project":{"number":12},"fieldValueByName":{"name":"In progress"}}]}}}}}"""

    match itemStatus transport board "FS-GG" "FS.GG.SDD" 42 with
    | Ok(Some InProgress) -> ()
    | other -> failwith $"the Status on OUR board is In progress — got %A{other}"

[<Fact>]
let ``itemStatus is Ok None when the item is on the board with NO Status set`` () =
    // `fieldValueByName` is null — on the board, no column. A definite "nothing to restore", which a claim
    // records as none and `release` puts back as Ready.
    let transport =
        serving """{"data":{"repository":{"issue":{"projectItems":{"nodes":[{"project":{"number":12},"fieldValueByName":null}]}}}}}"""

    match itemStatus transport board "FS-GG" "FS.GG.SDD" 42 with
    | Ok None -> ()
    | other -> failwith $"an unset Status is Ok None — got %A{other}"

[<Fact>]
let ``itemStatus is Ok None when the issue is not on this board`` () =
    let transport =
        serving """{"data":{"repository":{"issue":{"projectItems":{"nodes":[]}}}}}"""

    match itemStatus transport board "FS-GG" "FS.GG.SDD" 42 with
    | Ok None -> ()
    | other -> failwith $"not on board is Ok None — got %A{other}"

[<Fact>]
let ``itemStatus fails CLOSED - a failed read is Error, never the definite Ok None`` () =
    // The same discipline `itemId` keeps (#421): a read that FAILED may not be manufactured into "there is
    // no column". `claim` treats the error as "recorded no column" and falls back to Ready, but it does so
    // from an Error it was handed, not an absence it invented.
    let transport = failing (RateLimited None)

    match itemStatus transport board "FS-GG" "FS.GG.SDD" 42 with
    | Error(RateLimited _) -> ()
    | Ok None -> failwith "a failed status read reported the column ABSENT — absence may not be manufactured"
    | other -> failwith $"expected RateLimited — got %A{other}"

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

/// The document a write EMITS, captured. The sibling tests assert on `transport.Logged`, which is a
/// human-readable DESCRIPTION of the request — it cannot carry a variable's declared type, so a whole class
/// of defect is invisible to it. These read the document itself.
let private emitted (write: IGitHubTransport -> Result<unit, IoError>) : string =
    let mutable doc = ""

    let transport =
        Fake.Recorder(fun (req: Request) ->
            match req.Body with
            | Query(d, _) -> doc <- d
            | _ -> ()

            ok """{"data":{"updateProjectV2ItemFieldValue":{"clientMutationId":null}}}""")

    match write transport with
    | Ok() -> doc
    | other -> failwith $"the write must be attempted — got %A{other}"

[<Fact>]
let ``a SINGLE_SELECT option id is declared String — the schema types it String, not ID (#848)`` () =
    // GraphQL validates the DECLARATION against the argument's schema type and never looks at the value.
    // `ProjectV2FieldValue.singleSelectOptionId` is `String`, so declaring `$optionId: ID!` is refused
    // BEFORE the write is attempted — and Status, Phase, Repo Scope, Workstream and Effort are all
    // single-selects, so that is every one of them.
    //
    // The test above already drove this exact path and passed throughout, because it asserts the option
    // RESOLVES (`opt_wip`) — which it always did. The defect was one layer down, in a type no assertion
    // read. A stub transport answers 200 to a document the real API would reject on sight, so covering the
    // path proves nothing here; the DECLARATION has to be the subject.
    let doc = emitted (fun t -> setField t board "PVTI_coord123" "Status" (Set "In progress"))

    Assert.Contains("$optionId: String!", doc)
    Assert.DoesNotContain("$optionId: ID!", doc)

[<Fact>]
let ``an ITERATION id is declared String too — same schema type, same fix (#848)`` () =
    // Latent: no field on the board is an Iteration today. It would have been refused exactly as the
    // single-select was, the day somebody added one — so it is pinned here rather than rediscovered there.
    let iterationBoard =
        { board with Fields = board.Fields |> Map.add "Sprint" { Id = "PVTIF_sprint"; Type = Iteration } }

    let doc =
        emitted (fun t -> setField t iterationBoard "PVTI_coord123" "Sprint" (Set "iter_abc"))

    Assert.Contains("$iterationId: String!", doc)
    Assert.DoesNotContain("$iterationId: ID!", doc)

[<Fact>]
let ``a DATE value is declared Date!, not String! — and this leg is REACHED (#848)`` () =
    // The same defect as the two above, and the one that is NOT latent: the board carries two DATE fields
    // (`Start`, `Target`), so `set-field <ref> Target 2026-08-01` was refused with
    //     Type mismatch on variable $date and argument date (String! / Date)
    // `Date` is a named scalar; "it is a string on the wire" is exactly the reasoning that produced the
    // original bug.
    let dateBoard =
        { board with Fields = board.Fields |> Map.add "Target" { Id = "PVTF_target"; Type = Date } }

    let doc =
        emitted (fun t -> setField t dateBoard "PVTI_coord123" "Target" (Set "2026-08-01"))

    Assert.Contains("$date: Date!", doc)
    Assert.DoesNotContain("$date: String!", doc)

[<Fact>]
let ``the board's OWN ids stay ID! — the fix is per-argument, not a blanket retag`` () =
    // The mirror of the two above: `projectId`/`itemId`/`fieldId` really ARE `ID!`, and a fix that moved
    // everything to String would break all three while making the tests above pass.
    let doc = emitted (fun t -> setField t board "PVTI_coord123" "Status" (Set "Ready"))

    Assert.Contains("$projectId: ID!", doc)
    Assert.Contains("$itemId: ID!", doc)
    Assert.Contains("$fieldId: ID!", doc)

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
let ``#862 flush DROPS an entry whose item left the board - but does NOT count it as written`` () =
    use _sandbox = new Sandbox()

    // QUEUE A WRITE, then have the item leave the board before the replay. `boardWrite` refuses to queue a
    // `NotOnBoard` (the #510 leg above), so this is the ONE way the case is reachable: the entry was
    // legitimately queued against an item that WAS on the board, and the board moved underneath it.
    let deferring = scripted [ ok itemOnBoard; Error(RateLimited None) ]
    boardWrite deferring board "FS-GG" "FS.GG.SDD" 810 "Status" (Set "Ready") "vole-418" |> ignore

    // The replay's lookup succeeds and finds NOTHING — the item is gone.
    let gone =
        scripted [ ok """{"data":{"repository":{"issue":{"projectItems":{"nodes":[]}}}}}""" ]

    match flush gone board with
    // ZERO, not one. The entry is permanent and correctly DROPPED — carrying it forever would mean the
    // queue never drains — but `written` is the count `flush` REPORTS, and a caller renders it as "replayed
    // N of M". Counting a drop there tells a worker their board write landed when nothing was written: the
    // precise failure #862 exists to end, rebuilt inside the verb that exists to repair it.
    | Ok 0 ->
        match Cache.pending () with
        | Ok [] -> ()
        | other -> failwith $"the un-writable entry must still be DROPPED — got %A{other}"
    | Ok n -> failwith $"a dropped entry must not be COUNTED as written — flush reported %d{n}"
    | other -> failwith $"expected a clean flush that wrote nothing — got %A{other}"

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

// ---- the board-map + item-id caches (#418, case 10) ------------------------------------------------

[<Fact>]
let ``boardToJson is the board's machine contract - number, id, and typed fields`` () =
    let json = boardToJson board
    use doc = System.Text.Json.JsonDocument.Parse json
    let root = doc.RootElement
    Assert.Equal(12, root.GetProperty("number").GetInt32())
    Assert.Equal("PVT_coord", root.GetProperty("id").GetString())
    let fields = root.GetProperty("fields")
    Assert.Equal("SINGLE_SELECT", fields.GetProperty("Status").GetProperty("dataType").GetString())
    Assert.Equal("opt_ready", fields.GetProperty("Status").GetProperty("options").GetProperty("Ready").GetString())
    Assert.Equal("NUMBER", fields.GetProperty("Estimate").GetProperty("dataType").GetString())
    Assert.Equal("TEXT", fields.GetProperty("Blocked by").GetProperty("dataType").GetString())

[<Fact>]
let ``bootstrapCached serves the day-cache on the second call - zero GraphQL (#418)`` () =
    use _sandbox = new Sandbox()

    let cold =
        scripted
            [ ok """{"data":{"organization":{"projectsV2":{"nodes":[{"number":12,"title":"Coordination","id":"PVT_coord"}]}}}}"""
              ok """{"data":{"organization":{"projectV2":{"fields":{"nodes":[
                     {"id":"PVTSSF_status","name":"Status","dataType":"SINGLE_SELECT","options":[{"id":"opt_ready","name":"Ready"}]},
                     {"id":"PVTF_est","name":"Estimate","dataType":"NUMBER"}]}}}}}""" ]

    match bootstrapCached cold "FS-GG" "Coordination" with
    | Ok _ -> Assert.Equal(2, cold.GraphQlCalls)
    | other -> failwith $"cold bootstrapCached must resolve — got %A{other}"

    // The second call must NOT touch the transport — a warm map costs zero. `scripted []` throws if called,
    // and the re-hydrated field map must reconstruct the single-select options too.
    let warm = scripted []

    match bootstrapCached warm "FS-GG" "Coordination" with
    | Ok b ->
        Assert.Equal(0, warm.GraphQlCalls)
        Assert.Equal(12, b.Number)
        Assert.Equal("PVT_coord", b.Id)

        match Map.tryFind "Status" b.Fields with
        | Some { Type = SingleSelect options } -> Assert.Equal("opt_ready", options.["Ready"])
        | other -> failwith $"the re-hydrated Status must be a single-select — got %A{other}"
    | other -> failwith $"warm bootstrapCached must serve the cache — got %A{other}"

[<Fact>]
let ``itemIdCached serves the forever-cache on the second lookup - one GraphQL, then zero`` () =
    use _sandbox = new Sandbox()

    let cold = scripted [ ok itemOnBoard ]

    match itemIdCached cold board "FS-GG" "FS.GG.SDD" 42 with
    | Ok(Some id) ->
        Assert.Equal("PVTI_coord123", id)
        Assert.Equal(1, cold.GraphQlCalls)
    | other -> failwith $"cold itemIdCached must resolve — got %A{other}"

    let warm = scripted []

    match itemIdCached warm board "FS-GG" "FS.GG.SDD" 42 with
    | Ok(Some id) ->
        Assert.Equal("PVTI_coord123", id)
        Assert.Equal(0, warm.GraphQlCalls)
    | other -> failwith $"warm itemIdCached must serve the cache — got %A{other}"

[<Fact>]
let ``itemIdCached never memoises 'not on board' - an item added later must still be found (#421)`` () =
    use _sandbox = new Sandbox()

    // A successful empty lookup is `Ok None`. It must NOT be cached: the issue could be added to the board a
    // minute later, and a memoised absence would hide it for the life of the cache.
    let first = scripted [ ok """{"data":{"repository":{"issue":{"projectItems":{"nodes":[]}}}}}""" ]

    match itemIdCached first board "FS-GG" "FS.GG.SDD" 42 with
    | Ok None -> ()
    | other -> failwith $"a successful empty lookup is 'not on board' — got %A{other}"

    // A later lookup must re-read (a real GraphQL call), not serve a cached absence.
    let second = scripted [ ok itemOnBoard ]

    match itemIdCached second board "FS-GG" "FS.GG.SDD" 42 with
    | Ok(Some id) ->
        Assert.Equal("PVTI_coord123", id)
        Assert.Equal(1, second.GraphQlCalls)
    | other -> failwith $"an absence must not be cached — got %A{other}"
