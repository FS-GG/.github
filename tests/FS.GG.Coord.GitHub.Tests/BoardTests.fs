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
          NextLink = None; Headers = Map.empty }

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
    // remediation telling the worker to run `item-add` for an issue that already had a board item.
    //
    // The damage was the ANSWER, not a row: `addProjectV2ItemById` is idempotent server-side, so following
    // that remediation would have printed an id and changed nothing (#871). What #421 actually caught is a
    // definite "no" manufactured from a read that never happened — #266's class.
    //
    // `Ok None` is what licenses an `item-add`. It must be UNREACHABLE from a failure.
    let transport = failing (RateLimited(UnknownBudget, None))

    match itemId transport board "FS-GG" "FS.GG.SDD" 42 with
    | Error(RateLimited _) -> ()
    | Ok None -> failwith "a rate-limited lookup reported the item ABSENT — this is #421: 'could not ask' became 'the answer is no'"
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

    let transport = scripted [ Error(RateLimited(UnknownBudget, None)) ]

    match addItem transport board "FS-GG" "FS.GG.SDD" 42 with
    | Error(RateLimited _) -> ()
    | Ok(AddedToBoard _) -> failwith "added the item on a FAILED lookup — this is #421: it reports AddedToBoard for an issue whose presence was never established"
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
    let transport = failing (RateLimited(UnknownBudget, None))

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
        scripted [ ok itemOnBoard; Error(RateLimited(UnknownBudget, None)) ]

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
let ``#2143 an external-owner item cached by intake is written without re-parsing it as the default owner`` () =
    use _sandbox = new Sandbox()

    // The coordination owner and an external owner can have the same repository name and issue number.
    // `add` / `item-id` obtained both canonical ids, so the write must preserve the explicit issue owner
    // all the way to the mutation target rather than selecting the default owner's same-name twin.
    Cache.putItemId "FS-GG" "rogue3" 96 board.Number "PVTI_default96"
    Cache.putItemId "EHotwagner" "rogue3" 96 board.Number "PVTI_external96"

    let transport =
        serving """{"data":{"updateProjectV2ItemFieldValue":{"projectV2Item":{"id":"PVTI_external96"}}}}"""

    match boardWrite transport board "EHotwagner" "rogue3" 96 "Status" (Set "Ready") "vole-418" with
    | Ok Written ->
        Assert.Equal(1, transport.GraphQlCalls)
        Assert.True(transport.Logged "--id PVTI_external96")
        Assert.False(transport.Logged "PVTI_default96")
    | other -> failwith $"the external-owner cached item must be written — got %A{other}"

[<Fact>]
let ``#2143 an external-owner batch uses the same canonical cached item as a single field write`` () =
    use _sandbox = new Sandbox()
    Cache.putItemId "FS-GG" "rogue3" 96 board.Number "PVTI_default96"
    Cache.putItemId "EHotwagner" "rogue3" 96 board.Number "PVTI_external96"

    let transport =
        serving """{"data":{"f0":{"projectV2Item":{"id":"PVTI_external96"}},"f1":{"projectV2Item":{"id":"PVTI_external96"}}}}"""

    match boardWriteBatch transport board "EHotwagner" "rogue3" 96 [ "Status", Set "Ready"; "Blocked by", Clear ] "vole-418" with
    | Ok Written ->
        Assert.Equal(1, transport.GraphQlCalls)
        Assert.True(transport.Logged "itemId: \"PVTI_external96\"")
        Assert.False(transport.Logged "PVTI_default96")
    | other -> failwith $"the external-owner batch must use the canonical cached item — got %A{other}"

[<Fact>]
let ``#2166 a cold external-owner write resolves the exact ProjectV2 row across pages`` () =
    use _sandbox = new Sandbox()

    // The first page contains the same repository name and issue number under the board's default owner.
    // Matching only repo/number would silently mutate that twin; the external canonical owner appears on
    // page two and is the only legal mutation target.
    let firstPage =
        ok
            """{"data":{"node":{"items":{"pageInfo":{"hasNextPage":true,"endCursor":"page-2"},"nodes":[{"id":"PVTI_default96","content":{"number":96,"repository":{"nameWithOwner":"FS-GG/rogue3"}}}]}}}}"""

    let secondPage =
        ok
            """{"data":{"node":{"items":{"pageInfo":{"hasNextPage":false,"endCursor":null},"nodes":[{"id":"PVTI_external96","content":{"number":96,"repository":{"nameWithOwner":"EHotwagner/rogue3"}}}]}}}}"""

    let mutation =
        ok """{"data":{"updateProjectV2ItemFieldValue":{"projectV2Item":{"id":"PVTI_external96"}}}}"""

    let transport = scripted [ firstPage; secondPage; mutation ]

    match boardWrite transport board "EHotwagner" "rogue3" 96 "Status" (Set "Ready") "vole-418" with
    | Ok Written ->
        Assert.Equal(3, transport.GraphQlCalls)
        Assert.True(transport.Logged "--id PVTI_external96")
        Assert.False(transport.Logged "--id PVTI_default96")
        Assert.Equal(Some "PVTI_external96", Cache.getItemId "EHotwagner" "rogue3" 96 board.Number)
        Assert.Equal(None, Cache.getItemId "FS-GG" "rogue3" 96 board.Number)
    | other -> failwith $"the cold external-owner board row must be written — got %A{other}"

[<Fact>]
let ``#2166 a complete external-owner ProjectV2 lookup may report genuine non-membership`` () =
    use _sandbox = new Sandbox()

    let transport =
        serving
            """{"data":{"node":{"items":{"pageInfo":{"hasNextPage":false,"endCursor":null},"nodes":[{"id":"PVTI_default96","content":{"number":96,"repository":{"nameWithOwner":"FS-GG/rogue3"}}}]}}}}"""

    match itemId transport board "EHotwagner" "rogue3" 96 with
    | Ok None -> Assert.Equal(1, transport.GraphQlCalls)
    | other -> failwith $"a successful complete scan without the canonical external row is absence — got %A{other}"

[<Fact>]
let ``#2166 an incomplete external-owner ProjectV2 page fails closed instead of reporting absence`` () =
    use _sandbox = new Sandbox()

    let transport =
        serving
            """{"data":{"node":{"items":{"pageInfo":{"hasNextPage":true,"endCursor":null},"nodes":[]}}}}"""

    match itemId transport board "EHotwagner" "rogue3" 96 with
    | Error(Malformed(_, message)) -> Assert.Contains("another page but no usable cursor", message)
    | Ok None -> failwith "an incomplete paginated lookup was manufactured into external non-membership"
    | other -> failwith $"the incomplete external-owner lookup must fail closed — got %A{other}"

[<Fact>]
let ``#2166 malformed pagination completeness is never external non-membership`` () =
    use _sandbox = new Sandbox()

    let malformedItems =
        [ "pageInfo absent", """{"nodes":[]}"""
          "pageInfo null", """{"pageInfo":null,"nodes":[]}"""
          "hasNextPage absent", """{"pageInfo":{"endCursor":null},"nodes":[]}"""
          "hasNextPage null", """{"pageInfo":{"hasNextPage":null,"endCursor":null},"nodes":[]}"""
          "hasNextPage wrong type", """{"pageInfo":{"hasNextPage":"false","endCursor":null},"nodes":[]}""" ]

    for label, items in malformedItems do
        let transport = serving $"""{{"data":{{"node":{{"items":%s{items}}}}}}}"""

        match itemId transport board "EHotwagner" "rogue3" 96 with
        | Error(Malformed(_, message)) -> Assert.Contains("pageInfo", message)
        | Ok None -> failwith $"%s{label} was manufactured into external non-membership"
        | other -> failwith $"%s{label} must fail closed — got %A{other}"

[<Fact>]
let ``#2166 an unavailable external-owner ProjectV2 lookup is never non-membership`` () =
    use _sandbox = new Sandbox()
    let transport = failing (RateLimited(GraphQlBudget, None))

    match itemId transport board "EHotwagner" "rogue3" 96 with
    | Error(RateLimited(GraphQlBudget, _)) -> ()
    | Ok None -> failwith "an unavailable external-owner board lookup was manufactured into non-membership"
    | other -> failwith $"the unavailable external-owner lookup must fail closed — got %A{other}"

// ---- #2204: the two readers #2172 did not reach --------------------------------------------------

/// The MEASURED #2204 topology, in one transport.
///
/// The issue-side `repository.issue.projectItems` connection returns ONLY the external repository's own
/// user project — it omits the FS-GG organization board's row entirely, which is exactly what
/// `EHotwagner/rogue3#96`, `EHotwagner/rogue3#75` and `EHotwagner/S.I.R.#138` return against the live API.
/// The board node carries that row and its column all along. A reader that narrows the issue-side answer
/// to `board.Number` therefore reports the DEFINITE "no column" for a row that has one.
let private externalOwnerFieldWorld (projectLookup: string) (fieldNode: string) =
    Fake.Recorder(fun (req: Request) ->
        match req.Body with
        | Query(document, _) when document.Contains "node(id: $projectId)" -> ok projectLookup
        | Query(document, _) when document.Contains "node(id: $itemId)" -> ok fieldNode
        | Query(document, _) when document.Contains "repository(owner: $owner" ->
            ok
                """{"data":{"repository":{"issue":{"projectItems":{"nodes":[
                     {"id":"PVTI_user7","project":{"number":7},"fieldValueByName":{"name":"Backlog"}}]}}}}}"""
        | Query(document, _) -> Error(NotFound $"the #2204 fixture serves no answer for: %s{document}")
        | _ -> Error(NotFound "the #2204 fixture serves no answer for a non-GraphQL request"))

/// The board node carries the external row; the twin under the default owner is present so a match on
/// repo/number alone would pick the wrong one.
let private externalRowOnBoard =
    """{"data":{"node":{"items":{"pageInfo":{"hasNextPage":false,"endCursor":null},"nodes":[
         {"id":"PVTI_default96","content":{"number":96,"repository":{"nameWithOwner":"FS-GG/rogue3"}}},
         {"id":"PVTI_external96","content":{"number":96,"repository":{"nameWithOwner":"EHotwagner/rogue3"}}}]}}}}"""

[<Fact>]
let ``#2204 itemStatus reads an external-owner column the issue-side connection omits`` () =
    use _sandbox = new Sandbox()

    // THE DEFECT, IN ONE ASSERTION. `claim`'s convergence gate is `status = Some "In progress"`, and the
    // issue-side reader made that permanently unreachable for every cross-owner row on the board.
    let transport =
        externalOwnerFieldWorld externalRowOnBoard """{"data":{"node":{"fieldValueByName":{"name":"In progress"}}}}"""

    match itemStatus transport board "EHotwagner" "rogue3" 96 with
    | Ok(Some InProgress) ->
        // Two points: the project walk that resolves the row, then the one-point field read on its node.
        // The issue-side arm of this fixture is live and would have answered `Ok None` — the pre-repair
        // result — so reaching `In progress` at all is the assertion.
        Assert.Equal(2, transport.GraphQlCalls)
    | Ok None ->
        failwith "the external-owner column was manufactured into 'no column' — this is #2204: a filtered row became a definite absence"
    | other -> failwith $"the external-owner Status must be read from the board — got %A{other}"

[<Fact>]
let ``#2204 itemBlockedBy reads an external-owner edge from the board side too`` () =
    use _sandbox = new Sandbox()

    // The twin reader. #2172 repaired `itemId` alone and left BOTH of these carrying the defect verbatim;
    // they now share one mechanism so a future repair cannot land on one and miss the other.
    let transport =
        externalOwnerFieldWorld externalRowOnBoard """{"data":{"node":{"fieldValueByName":{"text":"FS-GG/.github#2155"}}}}"""

    match itemBlockedBy transport board "EHotwagner" "rogue3" 96 with
    | Ok(Some edge) -> Assert.Equal("FS-GG/.github#2155", edge)
    | Ok None -> failwith "a live external-owner `Blocked by` edge was reported as absent"
    | other -> failwith $"the external-owner Blocked by must be read from the board — got %A{other}"

[<Fact>]
let ``#2204 an external-owner row on the board with the field unset is a measured Ok None`` () =
    use _sandbox = new Sandbox()

    // The legitimate absence, and the one this repair must NOT lose: on the board, no column. `null`
    // `fieldValueByName` is GitHub's own answer for an unset field.
    let statusWorld =
        externalOwnerFieldWorld externalRowOnBoard """{"data":{"node":{"fieldValueByName":null}}}"""

    match itemStatus statusWorld board "EHotwagner" "rogue3" 96 with
    | Ok None -> ()
    | other -> failwith $"an unset external Status is Ok None — got %A{other}"

    let blockedWorld =
        externalOwnerFieldWorld externalRowOnBoard """{"data":{"node":{"fieldValueByName":null}}}"""

    match itemBlockedBy blockedWorld board "EHotwagner" "rogue3" 96 with
    | Ok None -> ()
    | other -> failwith $"an unset external Blocked by is Ok None — got %A{other}"

[<Fact>]
let ``#2204 an external issue genuinely not on this board is still Ok None`` () =
    use _sandbox = new Sandbox()

    // A COMPLETE walk of the project that did not find the canonical row. This is the one absence that may
    // be manufactured into "there is no column", because it was measured rather than filtered.
    let notOnBoard =
        """{"data":{"node":{"items":{"pageInfo":{"hasNextPage":false,"endCursor":null},"nodes":[
             {"id":"PVTI_default96","content":{"number":96,"repository":{"nameWithOwner":"FS-GG/rogue3"}}}]}}}}"""

    let transport = externalOwnerFieldWorld notOnBoard """{"data":{"node":null}}"""

    match itemStatus transport board "EHotwagner" "rogue3" 96 with
    | Ok None -> ()
    | other -> failwith $"a complete project walk without the external row is Ok None — got %A{other}"

[<Fact>]
let ``#2204 an incomplete external-owner lookup is Error, never 'no column'`` () =
    use _sandbox = new Sandbox()

    // #2166's fail-closed discipline reaches these readers through the same `itemId`. An incomplete page is
    // a read that did not finish, and #266's class is precisely turning that into a definite answer.
    let incomplete =
        """{"data":{"node":{"items":{"pageInfo":{"hasNextPage":true,"endCursor":null},"nodes":[]}}}}"""

    match itemStatus (externalOwnerFieldWorld incomplete "{}") board "EHotwagner" "rogue3" 96 with
    | Error(Malformed(_, message)) -> Assert.Contains("another page but no usable cursor", message)
    | Ok None -> failwith "an incomplete external lookup was manufactured into 'no column'"
    | other -> failwith $"the incomplete external Status lookup must fail closed — got %A{other}"

    match itemBlockedBy (externalOwnerFieldWorld incomplete "{}") board "EHotwagner" "rogue3" 96 with
    | Error(Malformed(_, message)) -> Assert.Contains("another page but no usable cursor", message)
    | Ok None -> failwith "an incomplete external lookup was manufactured into 'no edge'"
    | other -> failwith $"the incomplete external Blocked by lookup must fail closed — got %A{other}"

[<Fact>]
let ``#2204 a resolved external row whose board node does not resolve is Error, never an empty column`` () =
    use _sandbox = new Sandbox()

    // The id came FROM the board, so a null node is an unresolvable read — the row moved, or the read did
    // not happen. Either way the column was not measured, and absence may not be manufactured from it.
    let transport = externalOwnerFieldWorld externalRowOnBoard """{"data":{"node":null}}"""

    match itemStatus transport board "EHotwagner" "rogue3" 96 with
    | Error(NotFound message) -> Assert.Contains("PVTI_external96", message)
    | Ok None -> failwith "an unresolvable external field read was manufactured into 'no column'"
    | other -> failwith $"the unresolvable external field read must fail closed — got %A{other}"

[<Fact>]
let ``#2204 an unavailable external field read is never a manufactured absence`` () =
    use _sandbox = new Sandbox()
    let transport = failing (RateLimited(GraphQlBudget, None))

    match itemStatus transport board "EHotwagner" "rogue3" 96 with
    | Error(RateLimited(GraphQlBudget, _)) -> ()
    | Ok None -> failwith "a rate-limited external Status read reported the column ABSENT"
    | other -> failwith $"expected RateLimited — got %A{other}"

    match itemBlockedBy transport board "EHotwagner" "rogue3" 96 with
    | Error(RateLimited(GraphQlBudget, _)) -> ()
    | Ok None -> failwith "a rate-limited external Blocked by read reported the edge ABSENT"
    | other -> failwith $"expected RateLimited — got %A{other}"

[<Fact>]
let ``#2204 the board owner's own issues keep the ONE-POINT issue-side resolver read`` () =
    use _sandbox = new Sandbox()

    // THE THRIFT #481/#418 BOUGHT, AND THE REASON THIS BRANCHES ON OWNER AT ALL. `take` → `claim` runs this
    // for every worker on every round, against the budget that dies first. The board-side route costs a
    // project walk on its first lookup; routing the board owner's own issues through it would put that walk
    // on the hottest path in the org for no gain, because the issue-side connection answers them correctly.
    let transport =
        serving
            """{"data":{"repository":{"issue":{"projectItems":{"nodes":[
                 {"project":{"number":12},"fieldValueByName":{"name":"In progress"}}]}}}}}"""

    // ONE call, and it parsed an issue-side body. The board-side route would have paged the project first
    // and then failed to find `data.node.items` in this response, so `Ok(Some InProgress)` at one GraphQL
    // call is the whole assertion.
    match itemStatus transport board "FS-GG" "FS.GG.SDD" 42 with
    | Ok(Some InProgress) -> Assert.Equal(1, transport.GraphQlCalls)
    | other -> failwith $"the board owner's own Status is the resolver read — got %A{other}"

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

    let deferring = scripted [ ok itemOnBoard; Error(RateLimited(UnknownBudget, None)) ]
    boardWrite deferring board "FS-GG" "FS.GG.SDD" 810 "Status" (Set "Ready") "vole-418" |> ignore

    let replaying =
        scripted [ ok itemOnBoard; ok """{"data":{"updateProjectV2ItemFieldValue":{"clientMutationId":null}}}""" ]

    match flush replaying board with
    | Ok r when r.Written = 1 && r.Queued = 1 && r.Dropped = 0 && r.Stopped.IsNone ->
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
    let deferring = scripted [ ok itemOnBoard; Error(RateLimited(UnknownBudget, None)) ]
    boardWrite deferring board "FS-GG" "FS.GG.SDD" 810 "Status" (Set "Ready") "vole-418" |> ignore

    // The replay's lookup succeeds and finds NOTHING — the item is gone.
    let gone =
        scripted [ ok """{"data":{"repository":{"issue":{"projectItems":{"nodes":[]}}}}}""" ]

    match flush gone board with
    // ZERO, not one. The entry is permanent and correctly DROPPED — carrying it forever would mean the
    // queue never drains — but `written` is the count `flush` REPORTS, and a caller renders it as "replayed
    // N of M". Counting a drop there tells a worker their board write landed when nothing was written: the
    // precise failure #862 exists to end, rebuilt inside the verb that exists to repair it.
    | Ok r when r.Written = 0 ->
        // The drop is REPORTED as a drop — a fact of its own, next to a `Written` that stays honest.
        Assert.Equal(1, r.Dropped)
        Assert.Equal(1, r.Queued)

        match Cache.pending () with
        | Ok [] -> ()
        | other -> failwith $"the un-writable entry must still be DROPPED — got %A{other}"
    | Ok r -> failwith $"a dropped entry must not be COUNTED as written — flush reported %d{r.Written}"
    | other -> failwith $"expected a clean flush that wrote nothing — got %A{other}"

// ---- #882: a queued write records the board it was queued against -----------------------------------

/// The same board, repointed: `FSGG_COORD_OWNER`/`FSGG_COORD_PROJECT` name a DIFFERENT board, which the
/// engine bootstraps just as legitimately. Same fields, same shape — only the identity differs.
let private otherBoard =
    { board with
        Number = 13
        Id = "PVT_other"
        Title = "Some Other Board" }

[<Fact>]
let ``#882 a queued write RECORDS the board it was queued against`` () =
    use _sandbox = new Sandbox()

    let deferring = scripted [ ok itemOnBoard; Error(RateLimited(UnknownBudget, None)) ]
    boardWrite deferring board "FS-GG" "FS.GG.SDD" 810 "Status" (Set "Ready") "vole-418" |> ignore

    // THE BOARD IS KNOWN AT QUEUE TIME AND NOWHERE ELSE. `flush` bootstraps from an environment that may
    // since have been repointed, so an entry that does not carry its own board cannot be resolved — only
    // guessed at, which is what #882 was.
    match Cache.pending () with
    | Ok [ one ] -> Assert.Equal(Some("FS-GG", "Coordination"), one.Board)
    | other -> failwith $"the queued write must record its board — got %A{other}"

[<Fact>]
let ``#882 flush SKIPS a write queued against ANOTHER board - it must not be dropped`` () =
    use _sandbox = new Sandbox()

    // Queue against the Coordination board...
    let deferring = scripted [ ok itemOnBoard; Error(RateLimited(UnknownBudget, None)) ]
    boardWrite deferring board "FS-GG" "FS.GG.SDD" 810 "Status" (Set "Ready") "vole-418" |> ignore

    // ...then flush against a DIFFERENT one. THE WHOLE BUG IS THAT THIS LOOKS LEGITIMATE: the lookup on the
    // other board succeeds and finds nothing, which is `NotOnBoard` — permanent, and therefore dropped and
    // reported as "permanently un-writable". Every step is locally correct and the conclusion is false: the
    // write was perfectly writable, against the board nobody recorded.
    //
    // THE SKIP IS DECIDED FROM THE ENTRY, BEFORE ANY LOOKUP, and this transport is what pins it: it refuses
    // every call. Asking THIS board about another board's item cannot produce a useful answer — the only
    // ones available are "not on board" (permanent, and FALSE) and a rate limit — so the question must not be
    // asked at all. That makes the unfixed code fail here as a DROP rather than as an exception, and
    // `GraphQlCalls` then pins the cheaper fact too: another board's entry costs nothing to skip.
    let wrongBoard =
        Fake.Recorder(fun _ -> Error(NotFound "flush must not resolve an entry queued against another board"))

    match flush wrongBoard otherBoard with
    | Ok r ->
        // SKIPPED, NOT DROPPED. These are opposite facts: dropped means "this will never land", skipped
        // means "not by this pass, against this board".
        Assert.Equal(1, r.Skipped)
        Assert.Equal(0, r.Dropped)
        Assert.Equal(0, r.Written)
        Assert.Equal(1, r.Queued)
        Assert.Equal(0, wrongBoard.GraphQlCalls)

        // AND THE WRITE IS STILL THERE. This is the assertion the bug fails: the entry was silently
        // discarded, so the worker's board write was lost with a message saying it was unwritable.
        match Cache.pending () with
        | Ok [ one ] -> Assert.Equal(Some("FS-GG", "Coordination"), one.Board)
        | other -> failwith $"another board's write must REMAIN QUEUED — got %A{other}"
    | other -> failwith $"expected a clean flush that skipped the entry — got %A{other}"

[<Fact>]
let ``#882 the board that OWNS the queued write still replays it`` () =
    use _sandbox = new Sandbox()

    // THE OTHER HALF OF THE SKIP, and the one that makes it a deferral rather than a leak: an entry skipped
    // by the wrong board must still land when its own board flushes. A "fix" that merely stopped dropping
    // would strand it — real, on disk, and reachable by no verb, which is exactly what #878 repaired.
    let deferring = scripted [ ok itemOnBoard; Error(RateLimited(UnknownBudget, None)) ]
    boardWrite deferring board "FS-GG" "FS.GG.SDD" 810 "Status" (Set "Ready") "vole-418" |> ignore

    let wrongBoard =
        Fake.Recorder(fun _ -> Error(NotFound "flush must not resolve an entry queued against another board"))

    flush wrongBoard otherBoard |> ignore

    let replaying =
        scripted [ ok itemOnBoard; ok """{"data":{"updateProjectV2ItemFieldValue":{"clientMutationId":null}}}""" ]

    match flush replaying board with
    | Ok r when r.Written = 1 && r.Skipped = 0 ->
        match Cache.pending () with
        | Ok [] -> ()
        | other -> failwith $"the replayed entry must be gone — got %A{other}"
    | other -> failwith $"the owning board must replay what the other board skipped — got %A{other}"

[<Fact>]
let ``#882 an entry that recorded NO board is replayed, not skipped forever`` () =
    use _sandbox = new Sandbox()

    // A PRE-#882 ENTRY, sitting in a queue written by the previous build. Its board is genuinely unknown, and
    // "unknown" must not become "skip forever" — that would strand it exactly as #878's queue was stranded.
    // Replaying it against the current board is the behaviour it was queued under: no worse than before, and
    // right in the single-board case that is every real one.
    let legacy: Cache.Deferred =
        { Ref = "FS-GG/FS.GG.SDD#810"
          Field = "Status"
          Value = "Ready"
          At = "2026-07-14T12:00:00Z"
          Worker = "vole-418"
          Board = None }

    Cache.defer (RateLimited(UnknownBudget, None)) legacy |> ignore

    let replaying =
        scripted [ ok itemOnBoard; ok """{"data":{"updateProjectV2ItemFieldValue":{"clientMutationId":null}}}""" ]

    match flush replaying board with
    | Ok r when r.Written = 1 && r.Skipped = 0 && r.Dropped = 0 -> ()
    | other -> failwith $"a board-less legacy entry must still replay — got %A{other}"

[<Fact>]
let ``an exhausted budget STOPS the flush - the rest would fail identically`` () =
    use _sandbox = new Sandbox()

    let deferring =
        scripted
            [ ok itemOnBoard
              Error(RateLimited(UnknownBudget, None))
              ok itemOnBoard
              Error(RateLimited(UnknownBudget, None)) ]

    boardWrite deferring board "FS-GG" "FS.GG.SDD" 810 "Status" (Set "Ready") "vole-418" |> ignore
    boardWrite deferring board "FS-GG" "FS.GG.SDD" 811 "Status" (Set "Ready") "vole-418" |> ignore

    // Spending REST calls to confirm that the budget is still exhausted is exactly the back-off EX_RATE
    // exists to signal. The remainder stays queued.
    let stillLimited = Fake.Recorder(fun _ -> Error(RateLimited(UnknownBudget, None)))

    // A STOP IS `Stopped`, NOT `Error` (#862). `Error` is reserved for a queue that could not be READ, so
    // that the count this pass landed survives alongside the rate limit rather than being discarded with it.
    match flush stillLimited board with
    | Ok r when r.Stopped.IsSome ->
        Assert.Equal(0, r.Written)

        match Cache.pending () with
        | Ok entries -> Assert.Equal(2, List.length entries)
        | other -> failwith $"the queue must survive a stopped flush — got %A{other}"
    | other -> failwith $"an exhausted budget must stop the flush — got %A{other}"

[<Fact>]
let ``#862 a PARTIAL flush reports the writes it DID land, alongside the stop`` () =
    use _sandbox = new Sandbox()

    // Two queued writes; the replay lands the first and meets a fresh rate limit on the second.
    let deferring =
        scripted [ ok itemOnBoard; Error(RateLimited(UnknownBudget, None)); ok itemOnBoard; Error(RateLimited(UnknownBudget, None)) ]

    boardWrite deferring board "FS-GG" "FS.GG.SDD" 810 "Status" (Set "Ready") "vole-418" |> ignore
    boardWrite deferring board "FS-GG" "FS.GG.SDD" 811 "Status" (Set "Ready") "vole-418" |> ignore

    let partial =
        scripted
            [ ok itemOnBoard
              ok """{"data":{"updateProjectV2ItemFieldValue":{"clientMutationId":null}}}"""
              Error(RateLimited(UnknownBudget, None)) ]

    // THE COUNT MUST SURVIVE THE STOP. `flush` used to return `IoResult<int>`, so a stop returned `Error e`
    // and DISCARDED the 1 it had just written — leaving the caller to re-read the shared queue file and
    // infer it, which a concurrent `defer` makes wrong. "One landed, one did not" is the whole answer the
    // worker needs, and it is exactly what the old shape could not say.
    match flush partial board with
    | Ok r when r.Stopped.IsSome ->
        Assert.Equal(2, r.Queued)
        Assert.Equal(1, r.Written)
        Assert.Equal(0, r.Dropped)

        // The one that landed is gone; the one that did not is still queued, untouched.
        match Cache.pending () with
        | Ok [ survivor ] -> Assert.Equal("FS-GG/FS.GG.SDD#811", survivor.Ref)
        | other -> failwith $"exactly the unreplayed entry must remain — got %A{other}"
    | other -> failwith $"a partial flush must report the write it landed AND the stop — got %A{other}"

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
    let deferring = scripted [ ok itemOnBoard; Error(RateLimited(UnknownBudget, None)) ]
    boardWrite deferring board "FS-GG" "FS.GG.SDD" 810 "Status" (Set "Ready") "vole-418" |> ignore

    let depthBefore =
        match Cache.pending () with
        | Ok entries -> List.length entries
        | other -> failwith $"the write must be queued — got %A{other}"

    Assert.Equal(1, depthBefore)

    let stillLimited = Fake.Recorder(fun _ -> Error(RateLimited(UnknownBudget, None)))

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
    // THE FIXTURE GAINED `pageInfo` FOR `.github#2535`, and that is the point rather than an accommodation:
    // `NotFound` is a definite CONFIGURATION verdict ("your `FSGG_COORD_PROJECT` names a board that is not
    // here"), and since `bootstrap` walks the project list, it is only entitled to be said once the walk
    // has been shown to FINISH. `hasNextPage: false` is that proof, and the sibling test below is the same
    // fixture without it.
    let transport =
        serving
            """{"data":{"organization":{"projectsV2":{"pageInfo":{"hasNextPage":false,"endCursor":null},"nodes":[{"number":12,"title":"Something Else","id":"PVT_x"}]}}}}"""

    match bootstrap transport "FS-GG" "Coordination" with
    | Error(NotFound message) -> Assert.Contains("Coordination", message)
    | other -> failwith $"a missing board must name the title it looked for — got %A{other}"

// ---- owner-kind awareness (#1344) ------------------------------------------------------------------

/// A recorder that CAPTURES each request's GraphQL document into `docs`, then serves the scripted responses.
let private capturing (docs: System.Collections.Generic.List<string>) (responses: IoResult<Response> list) =
    let queue = System.Collections.Generic.Queue<IoResult<Response>>(responses)

    Fake.Recorder(fun req ->
        match req.Body with
        | Query(doc, _) -> docs.Add doc
        | _ -> ()

        if queue.Count = 0 then
            failwith "the transport was called more times than the test scripted"
        else
            queue.Dequeue())

[<Fact>]
let ``bootstrap resolves a USER-owned board through user(login:) (#1344)`` () =
    // A personal-account board answers to `user(login:)`, and both the project list and the field schema come
    // back nested under `data.user`. `FSGG_COORD_OWNER_TYPE=user` WITH an explicit `FSGG_COORD_OWNER` selects
    // that shape; without it a user login queried through `organization(login:)` resolves to null and every
    // board read fails. (With NO explicit owner, `user` falls to viewer-scoping — a separate test, #1349.)
    Environment.SetEnvironmentVariable("FSGG_COORD_OWNER_TYPE", "user")
    Environment.SetEnvironmentVariable("FSGG_COORD_OWNER", "EHotwagner")

    try
        let docs = System.Collections.Generic.List<string>()

        let transport =
            capturing
                docs
                [ ok """{"data":{"user":{"projectsV2":{"nodes":[{"number":3,"title":"TowerDefense","id":"PVT_user"}]}}}}"""
                  ok """{"data":{"user":{"projectV2":{"fields":{"nodes":[
                         {"id":"PVTSSF_status","name":"Status","dataType":"SINGLE_SELECT","options":[{"id":"opt_ready","name":"Ready"}]}]}}}}}""" ]

        match bootstrap transport "EHotwagner" "TowerDefense" with
        | Ok b ->
            Assert.Equal(3, b.Number)
            Assert.Equal("PVT_user", b.Id)
            Assert.Equal("EHotwagner", b.Owner)

            match Map.tryFind "Status" b.Fields with
            | Some { Type = SingleSelect options } -> Assert.Equal("opt_ready", options.["Ready"])
            | other -> failwith $"the user board's Status must resolve — got %A{other}"

            // Both documents (project list + field schema) hit the user node, not the organization node.
            Assert.All(
                docs,
                fun d ->
                    Assert.Contains("user(login: $owner)", d)
                    Assert.DoesNotContain("organization(login: $owner)", d)
            )
        | other -> failwith $"a user-owned board must bootstrap — got %A{other}"
    finally
        Environment.SetEnvironmentVariable("FSGG_COORD_OWNER_TYPE", null)
        Environment.SetEnvironmentVariable("FSGG_COORD_OWNER", null)

[<Fact>]
let ``bootstrap resolves a VIEWER-owned board through viewer, with no login in config (#1349)`` () =
    // `FSGG_COORD_OWNER_TYPE=user` with NO explicit `FSGG_COORD_OWNER` resolves the board from the token's OWN
    // `viewer` identity. Both documents select the argument-less `viewer` root, carry no `$owner` variable at
    // all (GraphQL rejects a declared-but-unused variable), and the responses come back nested under
    // `data.viewer`. Nothing about the operator's own login lives in plaintext config.
    Environment.SetEnvironmentVariable("FSGG_COORD_OWNER_TYPE", "user")
    Environment.SetEnvironmentVariable("FSGG_COORD_OWNER", null)

    try
        let docs = System.Collections.Generic.List<string>()

        let transport =
            capturing
                docs
                [ ok """{"data":{"viewer":{"projectsV2":{"nodes":[{"number":3,"title":"TowerDefense","id":"PVT_viewer"}]}}}}"""
                  ok """{"data":{"viewer":{"projectV2":{"fields":{"nodes":[
                         {"id":"PVTSSF_status","name":"Status","dataType":"SINGLE_SELECT","options":[{"id":"opt_ready","name":"Ready"}]}]}}}}}""" ]

        match bootstrap transport "@me" "TowerDefense" with
        | Ok b ->
            Assert.Equal(3, b.Number)
            Assert.Equal("PVT_viewer", b.Id)

            match Map.tryFind "Status" b.Fields with
            | Some { Type = SingleSelect options } -> Assert.Equal("opt_ready", options.["Ready"])
            | other -> failwith $"the viewer board's Status must resolve — got %A{other}"

            // Both documents hit the `viewer` root — not organization, not user(login:) — and neither declares
            // or references a `$owner` variable: no login travels to the API at all.
            Assert.All(
                docs,
                fun d ->
                    Assert.Contains("viewer {", d)
                    Assert.DoesNotContain("organization(login: $owner)", d)
                    Assert.DoesNotContain("user(login: $owner)", d)
                    Assert.DoesNotContain("$owner", d)
            )
        | other -> failwith $"a viewer-owned board must bootstrap — got %A{other}"
    finally
        Environment.SetEnvironmentVariable("FSGG_COORD_OWNER_TYPE", null)

[<Fact>]
let ``bootstrap still queries organization(login:) by default - org behaviour is byte-identical (#1344)`` () =
    // THE REGRESSION GUARD. Env var unset ⇒ the org path is exactly what it was before #1344: both documents
    // select `organization(login:)`, and neither carries a `user(login:)` selection.
    Environment.SetEnvironmentVariable("FSGG_COORD_OWNER_TYPE", null)

    let docs = System.Collections.Generic.List<string>()

    let transport =
        capturing
            docs
            [ ok """{"data":{"organization":{"projectsV2":{"nodes":[{"number":12,"title":"Coordination","id":"PVT_coord"}]}}}}"""
              ok """{"data":{"organization":{"projectV2":{"fields":{"nodes":[
                     {"id":"PVTSSF_status","name":"Status","dataType":"SINGLE_SELECT","options":[{"id":"opt_ready","name":"Ready"}]}]}}}}}""" ]

    match bootstrap transport "FS-GG" "Coordination" with
    | Ok b ->
        Assert.Equal(12, b.Number)

        Assert.All(
            docs,
            fun d ->
                Assert.Contains("organization(login: $owner)", d)
                Assert.DoesNotContain("user(login: $owner)", d)
        )
    | other -> failwith $"the org bootstrap must resolve unchanged — got %A{other}"

[<Fact>]
let ``OwnerKind.fromEnv resolves org, user, and viewer from the environment (#1349)`` () =
    let saved = Environment.GetEnvironmentVariable "FSGG_COORD_OWNER"
    let savedType = Environment.GetEnvironmentVariable "FSGG_COORD_OWNER_TYPE"

    try
        // Unset ⇒ Org (the default — the FS-GG board stays reachable no matter what else is set).
        Environment.SetEnvironmentVariable("FSGG_COORD_OWNER_TYPE", null)
        Environment.SetEnvironmentVariable("FSGG_COORD_OWNER", "EHotwagner")
        Assert.Equal(OwnerKind.Org, OwnerKind.fromEnv ())

        // `org`/`organization`/unrecognised ⇒ Org.
        Environment.SetEnvironmentVariable("FSGG_COORD_OWNER_TYPE", "org")
        Assert.Equal(OwnerKind.Org, OwnerKind.fromEnv ())
        Environment.SetEnvironmentVariable("FSGG_COORD_OWNER_TYPE", "something-else")
        Assert.Equal(OwnerKind.Org, OwnerKind.fromEnv ())

        // `user` WITH an explicit login ⇒ User (queries `user(login:)`, #1344).
        Environment.SetEnvironmentVariable("FSGG_COORD_OWNER_TYPE", "user")
        Environment.SetEnvironmentVariable("FSGG_COORD_OWNER", "EHotwagner")
        Assert.Equal(OwnerKind.User, OwnerKind.fromEnv ())

        // `user` with NO login ⇒ Viewer (resolve from the token's own identity, #1349).
        Environment.SetEnvironmentVariable("FSGG_COORD_OWNER", null)
        Assert.Equal(OwnerKind.Viewer, OwnerKind.fromEnv ())

        // A blank login is treated as absent ⇒ Viewer, not a `user(login: "")` that would resolve to null.
        Environment.SetEnvironmentVariable("FSGG_COORD_OWNER", "   ")
        Assert.Equal(OwnerKind.Viewer, OwnerKind.fromEnv ())
    finally
        Environment.SetEnvironmentVariable("FSGG_COORD_OWNER", saved)
        Environment.SetEnvironmentVariable("FSGG_COORD_OWNER_TYPE", savedType)

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
