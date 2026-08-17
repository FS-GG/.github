namespace FS.GG.Coord.Cli.Tests

open System
open System.Collections.Generic
open System.IO
open System.Text.Json
open Xunit
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Transport
open FS.GG.Coord.Cli

/// .github#2645 — `claim`'s BOARD DESTINATION, derived from the item's LIVE facts.
///
/// **THE DEFECT.** `claimLifecycleDestination` routed its answer through the same lifecycle reducer as
/// `next`/`reconcile` — which is right, and is what the function's own comment demanded — but handed that
/// reducer a FABRICATED observation:
///
/// ```
/// PullRequest = fact None            // "this item has no PR"
/// Blockers    = fact []              // "nothing is blocking it"
/// Delivery    = fact { false; false }
/// Issue       = fact Open
/// ```
///
/// The invention had not been removed, only moved one level down — from the column to the reducer's
/// INPUTS. `PullRequest = fact None` is the one that bit: it asserts *"there is no PR"* to a reducer whose
/// entire job is deciding a column from exactly that kind of fact, so the reducer correctly answered
/// `In progress`. A worker holding an item at `In review` who renewed its own lock with a bare
/// `claim` therefore had the column silently reverted, and `converged` — which required the literal
/// `In progress` — reported that revert as SUCCESS. Measured live on `.github#2546`.
///
/// **THE ACCEPTANCE CRITERIA THESE PIN.**
///   AC1 — a re-affirm on an item that is `In review` LEAVES it `In review`; the lock is untouched.
///   AC2 — every fact is READ (PR / blockers / delivery / issue), and an unreadable one WITHHOLDS the
///         write rather than being defaulted.
///   AC3 — `converged` is agreement with the PROJECTION, not with one hard-coded column.
///   AC4 — this module, plus the gate-inversion evidence recorded at its foot.
///   AC5 — `claimed`, `renewed` and `stolen` all reach `emitClaimReceipt`, so all three are driven here.
///
/// **THE CONTROL LEG MATTERS AS MUCH AS THE DEFECT LEG.** A "fix" that projected `In review` more often
/// would satisfy AC1 and be worse than the bug. `a fresh claim on an item with no PR still lands In
/// progress` is the leg that refuses it, and it is deliberately the same fixture read with one knob moved.
module ClaimLifecycleDestinationTests =

    let private ok (body: string) : Errors.IoResult<Response> =
        Ok
            { Status = 200
              Body = body
              ETag = None
              NextLink = None
              Headers = Map.empty }

    let private routeComment () =
        StructuredFixtures.routeComment "FS-GG/FS.GG.SDD#42" (Some FS.GG.Coord.DeliveryRoute.Lightweight) "fixture-2645" None

    /// How this fixture's world is shaped. Every field is a FACT ABOUT THE ITEM that `claim` now reads,
    /// so a leg below differs from its control in exactly one of them — which is what makes each leg an
    /// observation about one input rather than about the fixture as a whole.
    type private World =
        { /// The board column the item currently shows. The fixture MUTATES it on a served write, so the
          /// post-claim readback observes what the claim actually did rather than a canned answer.
          Column: string
          /// An OPEN pull request on this item's own `item/42-*` branch, if any.
          ItemPr: int option
          /// The item's live `Blocked by` COLUMN — ADR-0045's typed dependency edge, and the source
          /// `claim` resolves blockers from. `""` is "no edge", and costs no further read.
          BlockedBy: string
          /// The state the blocker `BlockedBy` names answers with, when it names one.
          BlockerState: string
          /// The ITEM issue's own OPEN/CLOSED state.
          IssueState: string
          /// Make `Reads.prAlive`'s open-PR list unreadable — a transport failure, not a 404. This is the
          /// "I could not look" input, and `LivenessUnknown` must never launder into "no PR".
          PrProbeFails: bool
          /// A claim marker already live on the item, as `(worker, session)`.
          Holder: (string * string) option
          /// A done receipt already on the item's comment thread (`Done.hasReceipt`).
          DoneStamped: bool }

    let private world =
        { Column = "Ready"
          ItemPr = None
          BlockedBy = ""
          BlockerState = "open"
          IssueState = "open"
          PrProbeFails = false
          Holder = None
          DoneStamped = false }

    /// The item's comment thread, mutated the way GitHub would: a POST adds a comment with the next id, a
    /// DELETE removes one. Seeded with the current delivery-route receipt (which `claim` requires) and,
    /// when the world says so, a live claim marker and/or a done receipt.
    type private Thread(seed: (string * string) option, doneStamped: bool) =
        let comments = Dictionary<int64, string>()
        let posted = ResizeArray<string>()
        let mutable nextId = 9000L

        do
            match seed with
            | Some(holder, session) ->
                comments.[8042L] <-
                    $"<!-- fsgg:claim worker=%s{holder} lease=120 renewed=1 session=%s{session} prev=In%%20review pathRepo=FS.GG.SDD -->"
            | None -> ()

            if doneStamped then
                comments.[8043L] <- "<!-- fsgg:done ref=FS-GG/FS.GG.SDD#42 -->"

        member _.Posted = List.ofSeq posted

        member _.Json() =
            let ts = DateTime.UtcNow.ToString "yyyy-MM-ddTHH:mm:ssZ"

            let route =
                JsonSerializer.Serialize
                    {| id = 7001L
                       body = routeComment ()
                       user = {| login = "EHotwagner" |}
                       created_at = ts
                       updated_at = ts |}

            let rest =
                comments
                |> Seq.sortBy (fun kv -> kv.Key)
                |> Seq.map (fun kv ->
                    $"""{{"id":%d{kv.Key},"body":"%s{kv.Value}","user":{{"login":"EHotwagner"}},"created_at":"%s{ts}","updated_at":"%s{ts}"}}""")
                |> String.concat ","

            "[" + route + (if String.IsNullOrEmpty rest then "" else "," + rest) + "]"

        member _.Bodies =
            let rest =
                comments
                |> Seq.sortBy (fun kv -> kv.Key)
                |> Seq.map (fun kv -> kv.Value.Replace("\\n", "\n").Replace("\\\"", "\""))
                |> List.ofSeq

            routeComment () :: rest

        member _.Add(body: string) =
            nextId <- nextId + 1L
            comments.[nextId] <- body.Replace("\n", "\\n").Replace("\"", "\\\"")
            posted.Add body
            nextId

        member _.Remove(id: int64) = comments.Remove id |> ignore

    /// The transport, plus the Status option ids it was asked to write, oldest first.
    type private Fixture =
        { Transport: Fake.Recorder
          Thread: Thread
          StatusWrites: unit -> string list
          Column: unit -> string }

    let private build (w: World) : Fixture =
        let thread = Thread(w.Holder, w.DoneStamped)
        let writes = ResizeArray<string>()
        let mutable column = w.Column

        // Every column this fixture can be asked to project must be a REAL option, or `Board.setField`
        // refuses the value BEFORE sending the mutation and the leg would measure the fixture's field
        // definition rather than the engine's decision.
        let options =
            """[{"id":"opt_backlog","name":"Backlog"},{"id":"opt_ready","name":"Ready"},{"id":"opt_wip","name":"In progress"},{"id":"opt_review","name":"In review"},{"id":"opt_blocked","name":"Blocked"}]"""

        let optionName id =
            match id with
            | "opt_backlog" -> "Backlog"
            | "opt_ready" -> "Ready"
            | "opt_wip" -> "In progress"
            | "opt_review" -> "In review"
            | "opt_blocked" -> "Blocked"
            | other -> other

        let graphql (document: string) (variables: (string * Var) list) : Errors.IoResult<Response> =
            if document.Contains "comments(last:" then
                let last =
                    variables
                    |> List.tryFind (fun (k, _) -> k = "last")
                    |> Option.bind (fun (_, v) -> match v with VNumber n -> Some(int n) | _ -> None)
                    |> Option.defaultValue 100

                let recent =
                    thread.Bodies
                    |> List.rev
                    |> List.truncate last
                    |> List.rev
                    |> List.map (fun body -> {| body = body |})
                    |> JsonSerializer.Serialize

                ok ("{\"data\":{\"repository\":{\"issue\":{\"comments\":{\"nodes\":" + recent + "}}}},\"rateLimit\":{\"cost\":1,\"remaining\":4977}}")
            elif document.Contains "projectsV2" then
                ok """{"data":{"organization":{"projectsV2":{"nodes":[{"number":12,"title":"Coordination","id":"PVT_coord"}]}}},"rateLimit":{"cost":1,"remaining":4977}}"""
            elif document.Contains "fields(first" then
                ok
                    ("""{"data":{"organization":{"projectV2":{"fields":{"nodes":[{"id":"PVTSSF_status","name":"Status","dataType":"SINGLE_SELECT","options":"""
                     + options
                     + """},{"id":"PVTF_blocked","name":"Blocked by","dataType":"TEXT"}]}}}},"rateLimit":{"cost":1,"remaining":4977}}""")
            elif document.Contains "items(first" then
                let blocked = if w.BlockedBy = "" then "null" else $"{{\"text\":\"%s{w.BlockedBy}\"}}"
                let state = w.IssueState.ToUpperInvariant()

                ok
                    $"""{{"data":{{"organization":{{"projectV2":{{"items":{{"pageInfo":{{"hasNextPage":false,"endCursor":null}},"nodes":[{{"id":"PVTI_42","status":{{"name":"%s{column}"}},"blockedBy":%s{blocked},"content":{{"__typename":"Issue","number":42,"title":"item 42","state":"%s{state}","repository":{{"nameWithOwner":"FS-GG/FS.GG.SDD"}}}}}}]}}}}}}}},"rateLimit":{{"cost":1,"remaining":4977}}}}"""
            elif document.Contains "fieldValueByName(name: \"Blocked by\")" then
                // `Board.itemBlockedBy` — the source `claim` now resolves this item's live blocker edges
                // from, rather than asserting `[]`.
                let value = if w.BlockedBy = "" then "null" else $"{{\"text\":\"%s{w.BlockedBy}\"}}"

                ok
                    $"""{{"data":{{"repository":{{"issue":{{"projectItems":{{"totalCount":1,"nodes":[{{"project":{{"number":12}},"fieldValueByName":%s{value}}}]}}}}}}}},"rateLimit":{{"cost":1,"remaining":4977}}}}"""
            elif document.Contains "fieldValueByName(name: \"Status\")" then
                ok
                    $"""{{"data":{{"repository":{{"issue":{{"projectItems":{{"totalCount":1,"nodes":[{{"project":{{"number":12}},"fieldValueByName":{{"name":"%s{column}"}}}}]}}}}}}}},"rateLimit":{{"cost":1,"remaining":4977}}}}"""
            elif document.Contains "projectItems" then
                ok """{"data":{"repository":{"issue":{"projectItems":{"totalCount":1,"nodes":[{"id":"PVTI_42","project":{"number":12}}]}}}},"rateLimit":{"cost":1,"remaining":4977}}"""
            elif document.Contains "updateProjectV2ItemFieldValue" then
                // THE WRITE MOVES THE COLUMN. Without this the readback would report the seeded value
                // forever and `converged` would be measuring the fixture rather than the claim.
                match variables |> List.tryFind (fun (k, _) -> k = "optionId") with
                | Some(_, VString id)
                | Some(_, VId id) ->
                    writes.Add id
                    column <- optionName id
                | _ -> ()

                ok """{"data":{"updateProjectV2ItemFieldValue":{"projectV2Item":{"id":"PVTI_42"}}}}"""
            else
                Error(Errors.NotFound $"the fixture serves no GraphQL document: %s{document}")

        let transport =
            Fake.Recorder(fun (req: Request) ->
                let path = req.Path.Trim '/'

                match req.Method, path with
                | "POST", "graphql" ->
                    match req.Body with
                    | Query(document, variables) -> graphql document variables
                    | _ -> Error(Errors.NotFound "a graphql call with no document")
                | "GET", "rate_limit" -> ok """{"resources":{"graphql":{"remaining":4980,"limit":5000}}}"""
                | "GET", "repos/FS-GG/FS.GG.SDD/issues" ->
                    ok """[{"number":42,"state":"open","body":"Paths: src/Thing.fs"}]"""
                | "GET", "repos/FS-GG/FS.GG.SDD/issues/42" ->
                    ok $"""{{"number":42,"state":"%s{w.IssueState}","body":"Paths: src/Thing.fs"}}"""
                | "GET", "repos/FS-GG/FS.GG.SDD/issues/42/comments" -> ok (thread.Json())
                | "POST", "repos/FS-GG/FS.GG.SDD/issues/42/comments" ->
                    let body =
                        match req.Body with
                        | Json payload -> JsonDocument.Parse(payload).RootElement.GetProperty("body").GetString()
                        | _ -> ""

                    ok (sprintf """{"id":%d}""" (thread.Add body))
                | "DELETE", _ when path.StartsWith "repos/FS-GG/FS.GG.SDD/issues/comments/" ->
                    thread.Remove(Int64.Parse(path.Substring(path.LastIndexOf '/' + 1)))
                    ok ""
                // The blocker `w.BlockedBy` names — resolved over REST, exactly as `Scan.resolveBlocker`
                // resolves an off-board edge, because a PR is an issue in REST and only that read
                // distinguishes MERGED from CLOSED (#476).
                | "GET", "repos/FS-GG/FS.GG.SDD/issues/999" ->
                    ok $"""{{"number":999,"state":"%s{w.BlockerState}","body":""}}"""
                // `Reads.prAlive`, both probes. The open-PR list first; then, only if it found none, the
                // pushed-branch probe (#1055).
                | "GET", "repos/FS-GG/FS.GG.SDD/pulls" when w.PrProbeFails ->
                    Error(Errors.Transport "fixture: the item's open-PR list is UNREADABLE")
                | "GET", "repos/FS-GG/FS.GG.SDD/pulls" ->
                    match w.ItemPr with
                    | Some pr -> ok $"""[{{"number":%d{pr},"head":{{"ref":"item/42-a-slug"}}}}]"""
                    | None -> ok "[]"
                | "GET", "repos/FS-GG/FS.GG.SDD/git/matching-refs/heads/item/42-" -> ok "[]"
                | m, p -> Error(Errors.NotFound $"the fixture serves no %s{m} %s{p}"))

        { Transport = transport
          Thread = thread
          StatusWrites = fun () -> List.ofSeq writes
          Column = fun () -> column }

    let private context (transport: Fake.Recorder) : Kernel.Context =
        { Transport = transport
          Owner = "FS-GG"
          Title = "Coordination"
          DefaultRepo = Some "FS.GG.SDD"
          ChoreLocks = [] }

    /// The identity ladder, pinned on BOTH rungs — `ForceStealTests` carries the full argument for why an
    /// unpinned `$FSGG_WORKER`/session makes every leg here an impersonation (#1646/#419).
    let private sessionVars =
        [ "CLAUDE_CODE_SESSION_ID"; "OPENCODE_SESSION_ID"; "FSGG_AGENT_SESSION_ID"; "FSGG_WORKER" ]

    let private runClaim (transport: Fake.Recorder) (args: string list) : int * string * string =
        let dir = Path.Combine(Path.GetTempPath(), "fsgg-2645-" + Guid.NewGuid().ToString "n")
        let previousCache = Environment.GetEnvironmentVariable "FSGG_COORD_CACHE"
        let previousKitRoot = Environment.GetEnvironmentVariable "FSGG_KIT_ROOT"
        let previousSessions = sessionVars |> List.map (fun v -> v, Environment.GetEnvironmentVariable v)
        let stdout = Console.Out
        let stderr = Console.Error
        use capturedOut = new StringWriter()
        use capturedErr = new StringWriter()

        try
            Directory.CreateDirectory dir |> ignore
            Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", dir)
            Environment.SetEnvironmentVariable("FSGG_KIT_ROOT", dir)
            Environment.SetEnvironmentVariable("CLAUDE_CODE_SESSION_ID", null)
            Environment.SetEnvironmentVariable("OPENCODE_SESSION_ID", null)
            Environment.SetEnvironmentVariable("FSGG_AGENT_SESSION_ID", "ed60050b")
            Environment.SetEnvironmentVariable("FSGG_WORKER", "vole-418")
            Console.SetOut capturedOut
            Console.SetError capturedErr

            let opts =
                match Options.parse args with
                | Ok o -> o
                | Error e -> failwithf "the fixture's own argv did not parse: %s" e

            let code = Client.claim (context transport) opts
            Console.Out.Flush()
            Console.Error.Flush()
            code, capturedOut.ToString(), capturedErr.ToString()
        finally
            Console.SetOut stdout
            Console.SetError stderr
            Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", previousCache)
            Environment.SetEnvironmentVariable("FSGG_KIT_ROOT", previousKitRoot)

            for name, value in previousSessions do
                Environment.SetEnvironmentVariable(name, value)

            try
                Directory.Delete(dir, true)
            with _ ->
                ()

    let private claimArgs (extra: string list) =
        [ "claim"; "FS.GG.SDD#42"; "--worker"; "vole-418"; "--json" ] @ extra

    let private receiptOf (out: string) = JsonDocument.Parse(out.Trim()).RootElement

    // ---- AC1/AC3/AC5: the measured defect — a mid-review re-affirm ------------------------------------

    [<Fact>]
    let ``#2645 a RENEWED claim on an item in review leaves the column In review`` () =
        // The exact shape measured on `.github#2546`: the worker already holds the lock, the item is at
        // `In review` with its `item/42-*` PR open, and it renews the lease with a bare `claim`.
        let fixture =
            build
                { world with
                    Column = "In review"
                    ItemPr = Some 77
                    Holder = Some("vole-418", "ed60050b") }

        let code, out, _ = runClaim fixture.Transport (claimArgs [])

        Assert.Equal(0, code)

        let receipt = receiptOf out
        // AC5 — this is the `renewed` arm of `emitClaimReceipt`, the one the live incident hit.
        Assert.Equal("renewed", receipt.GetProperty("kind").GetString())

        // AC1 — the column is STILL `In review`. Pre-fix this read `In progress`, written by a reducer
        // that had been told the review does not exist.
        Assert.Equal<string list>([ "opt_review" ], fixture.StatusWrites())
        Assert.Equal("In review", fixture.Column())
        Assert.Equal("In review", receipt.GetProperty("status").GetString())
        Assert.Equal("written", receipt.GetProperty("statusWrite").GetString())

        // AC3 — and that IS convergence. Pre-fix `converged` required the literal `In progress`, so the
        // correct answer would have been reported as drift and the revert as success.
        Assert.True(receipt.GetProperty("converged").GetBoolean())

    // ---- The control: the fix must not simply project `In review` more often --------------------------

    [<Fact>]
    let ``#2645 a fresh claim on an item with NO pull request still lands In progress`` () =
        // Identical fixture, ONE knob moved: no open PR, no prior holder, the ordinary `Ready` row a
        // scheduler hands out. If this ever reads `In review`, the reducer is being fed a new fiction in
        // the opposite direction and the repair is worse than the defect.
        let fixture = build world

        let code, out, _ = runClaim fixture.Transport (claimArgs [])

        Assert.Equal(0, code)

        let receipt = receiptOf out
        Assert.Equal("claimed", receipt.GetProperty("kind").GetString())
        Assert.Equal<string list>([ "opt_wip" ], fixture.StatusWrites())
        Assert.Equal("In progress", receipt.GetProperty("status").GetString())
        Assert.True(receipt.GetProperty("converged").GetBoolean())

    // ---- AC5: the `stolen` arm reaches the same projection --------------------------------------------

    [<Fact>]
    let ``#2645 a STOLEN claim projects from the same live facts as a fresh one`` () =
        // `--force` over ANOTHER worker's live marker. The destination is a fact about the ITEM, so it
        // cannot depend on which of the three CAS outcomes delivered the lock — and all three funnel
        // through one `emitClaimReceipt`, which is exactly why AC5 asks for all three.
        let fixture =
            build
                { world with
                    Column = "In review"
                    ItemPr = Some 77
                    Holder = Some("smew-e1d9", "0e0e0e0e") }

        let code, out, _ = runClaim fixture.Transport (claimArgs [ "--force" ])

        Assert.Equal(0, code)

        let receipt = receiptOf out
        Assert.Equal("stolen", receipt.GetProperty("kind").GetString())
        Assert.Equal<string list>([ "opt_review" ], fixture.StatusWrites())
        Assert.True(receipt.GetProperty("converged").GetBoolean())

    // ---- AC2: the blockers fact is READ, not asserted empty --------------------------------------------

    [<Fact>]
    let ``#2645 a claim on a row with a LIVE blocker projects Blocked, never In progress`` () =
        // `Blockers = fact []` was the second fabrication. The row's `Blocked by` column names an OPEN
        // issue, so the reducer's blocker arm fires and the claim must not write `In progress` over it —
        // which is also what stops `claim` and `reconcile` arguing about the same row every pass.
        let fixture = build { world with BlockedBy = "FS-GG/FS.GG.SDD#999"; BlockerState = "open" }

        let code, out, _ = runClaim fixture.Transport (claimArgs [])

        Assert.Equal(0, code)
        Assert.Equal<string list>([ "opt_blocked" ], fixture.StatusWrites())
        Assert.Equal("Blocked", (receiptOf out).GetProperty("status").GetString())

    [<Fact>]
    let ``#2645 a claim whose blocker has CLOSED lands In progress — the edge is resolved, not merely present`` () =
        // The control for the leg above, and it is not decoration: a fix that read the FIELD but not the
        // referenced issue's STATE would block on a dependency that finished, which is #476's defect
        // arriving through this new read.
        let fixture = build { world with BlockedBy = "FS-GG/FS.GG.SDD#999"; BlockerState = "closed" }

        let code, out, _ = runClaim fixture.Transport (claimArgs [])

        Assert.Equal(0, code)
        Assert.Equal<string list>([ "opt_wip" ], fixture.StatusWrites())
        Assert.Equal("In progress", (receiptOf out).GetProperty("status").GetString())

    // ---- AC2: an unreadable fact WITHHOLDS the write, and says so ---------------------------------------

    [<Fact>]
    let ``#2645 an UNREADABLE open-PR probe withholds the column and never defaults to In progress`` () =
        // "I could not look" is not "I looked and there is no PR" (#266). This is the arm the old code
        // could not even express: `PullRequest = fact None` had no room for an unknown, so a transport
        // failure and a genuine absence produced the same confident `In progress`.
        let fixture = build { world with Column = "In review"; PrProbeFails = true }

        let code, out, err = runClaim fixture.Transport (claimArgs [])

        // The LOCK is unaffected — a column we cannot derive is not a reason to refuse a claim.
        Assert.Equal(0, code)
        Assert.NotEmpty(fixture.Thread.Posted)

        // ...and NOTHING was written. Not `In progress`, not anything.
        Assert.Empty(fixture.StatusWrites())
        Assert.Equal("In review", fixture.Column())

        let receipt = receiptOf out
        Assert.True(receipt.GetProperty("markerObserved").GetBoolean())
        Assert.Equal("withheld", receipt.GetProperty("statusWrite").GetString())
        Assert.False(receipt.GetProperty("converged").GetBoolean())

        // A withheld write that nobody can read is a withheld write that did not happen.
        Assert.Contains("WITHHELD", err)
        Assert.Contains("open-PR probe", err)

    [<Fact>]
    let ``#2645 a CLOSED issue with no done receipt withholds rather than projecting a working column`` () =
        // `Issue = fact Open` was the fourth fabrication. A closed row has exactly two reducer answers and
        // neither is a column a claim may invent: with a done receipt it is `Done`, and without one the
        // reducer itself says `closed issue has no verified done receipt` and withholds.
        let fixture = build { world with IssueState = "closed" }

        let code, out, err = runClaim fixture.Transport (claimArgs [])

        Assert.Equal(0, code)
        Assert.Empty(fixture.StatusWrites())

        let receipt = receiptOf out
        Assert.Equal("withheld", receipt.GetProperty("statusWrite").GetString())
        Assert.False(receipt.GetProperty("converged").GetBoolean())
        Assert.Contains("done receipt", err)

    // ---- The COST of reading those facts, pinned rather than asserted ----------------------------------

    [<Fact>]
    let ``#2645 deriving the destination costs exactly five extra REST reads and one GraphQL read`` () =
        // `claim` is the hottest write path in the org and rides the budget that dies first under fan-out
        // (#418/#1666), so "read the facts instead of inventing them" is a claim about SPEND as well as
        // about honesty. This leg is the measurement, kept in the tree so a later change that quietly adds
        // a fifth read to this path has to say so here.
        //
        // MEASURED, on this exact fixture and its ordinary no-PR/no-blocker row (`world`): the pre-fix
        // engine — `git show origin/main:src/FS.GG.Coord.Cli/Client.fs`, built and run against this same
        // module — spent 7 REST and 7 GraphQL calls; this one spends 12 REST and 8. The delta is the six
        // reads asserted below and nothing else.
        //
        // WHY EACH ONE IS BOUGHT, in the order the reducer's cascade consumes them:
        //   * `pulls-list` + `matching-refs` — `Reads.prAlive`. Two calls, not one: with no open PR it
        //     asks whether a pushed `item/<n>-*` branch exists (#1055), and an UNREADABLE answer there is
        //     `LivenessUnknown`, which withholds. This is the fact whose fabrication (#2645) reverted a
        //     review, so it is the one read that is not optional at any price.
        //   * `issue-get` — `Reads.issueState`. The OPEN/CLOSED arms sit ABOVE everything else in the
        //     cascade, so no later fact can be trusted without it.
        //   * `comment-list` — `Reads.commentBodies`. ONE read, TWO facts: the done receipt and the
        //     projection watermark. The claim path already reads this thread three times for the lock, so
        //     this is a fourth of a shape the path already has, not a new one.
        //   * `graphql … Blocked by` — `Board.itemBlockedBy`, against the board this call already
        //     bootstrapped. A row with no edge (the common case, and this fixture's) stops there and
        //     resolves no blocker: zero further REST.
        //   * `issue-get` (body) — `Reads.issueBody`, for the item's `Kind:` line (.github#2712). THE
        //     FIFTH READ, ADDED DELIBERATELY, and this comment is the "has to say so here" the paragraph
        //     above demanded rather than an edit that quietly moved the number. It buys the one fact that
        //     decides whether this row has a lifecycle at all: a class anchor, a register or a directive
        //     must get NO Status column from the reducer, and the kind is a BODY fact — it cannot be taken
        //     from the board column (which may lag, and which a dropdown edit could weaponise) nor from
        //     the watermark (which would freeze the exemption, `Client.fs:2492`).
        //
        //     THE BUDGET ARGUMENT, honestly stated: this is one request per CLAIM, not per candidate
        //     scanned. `take` — measured at roughly 190 REST requests for one run on `FS-GG/.github`,
        //     2026-08-16 — reaches this function exactly once, for the single item it claims. Contrast
        //     the #353 collision scan, which `take` deliberately skips precisely because that one WOULD be
        //     paid per candidate. The thing bought is the difference between a register that the reducer
        //     leaves alone and one it writes a lifecycle column onto.
        let fixture = build world
        let code, _, _ = runClaim fixture.Transport (claimArgs [])

        Assert.Equal(0, code)
        Assert.Equal(1, fixture.Transport.Count "pulls-list FS-GG/FS.GG.SDD")
        Assert.Equal(1, fixture.Transport.Count "git/matching-refs/heads/item/42-")
        Assert.Equal(1, fixture.Transport.Count "graphql FS-GG/FS.GG.SDD#42 Blocked by")
        Assert.Equal(12, fixture.Transport.RestCalls)
        Assert.Equal(8, fixture.Transport.GraphQlCalls)

    // ---- GATE-INVERSION EVIDENCE (.github#2645, recorded by hand against the mutated binary) -----------
    //
    // The mutation is SURGICAL — the exact fabrication this issue names, reinstated, and nothing else —
    // because a blanket revert "can fail tests for incidental reasons and give a false sense of coverage"
    // (`ClaimOverlapTests`' own note, quoting the .github#2454 lesson).
    //
    // MUTATION, in `src/FS.GG.Coord.Cli/Client.fs`'s `claimLifecycleDestination`: replace the four read
    // facts in the `observation` record with the pre-fix constants, leaving every read, the fail-closed
    // plumbing, `converged` and the receipt untouched —
    //
    //     PullRequest = fact pullRequest        ->  PullRequest = fact None
    //     Blockers    = fact blockers           ->  Blockers    = fact []
    //     Delivery    = fact { …; DoneStamped = Done.hasReceipt comments }
    //                                           ->  Delivery    = fact { Outstanding = false; DoneStamped = false }
    //     Issue       = fact issue              ->  Issue       = fact Open
    //
    // OBSERVED RED against the mutated binary — 4 of the 8 legs above, and the four that stay green are
    // the four whose subject the mutation does not touch:
    //   - `a RENEWED claim on an item in review leaves the column In review`: FAILS.
    //     `Assert.Equal<string list>(["opt_review"], …)` observed `["opt_wip"]` — the measured .github#2546
    //     revert, reproduced exactly. The SECOND half of the defect — the receipt calling that revert a
    //     success — was measured separately rather than inferred: with `Assert.True(converged)` hoisted
    //     above the write assertion in a scratch edit of this file, the mutated binary PASSED it and still
    //     failed on `["opt_wip"]` below, i.e. `converged: true` over a reverted column. Edit reverted.
    //   - `a STOLEN claim projects from the same live facts as a fresh one`: FAILS identically
    //     (`["opt_wip"]` for `["opt_review"]`) — AC5's point that all three CAS outcomes share one path.
    //   - `a claim on a row with a LIVE blocker projects Blocked, never In progress`: FAILS —
    //     `["opt_wip"]` observed for `["opt_blocked"]`; the reducer was told nothing blocks the row.
    //   - `a CLOSED issue with no done receipt withholds…`: FAILS — `statusWrite` observed `written`
    //     (and `["opt_wip"]` written) for the expected `withheld`; the reducer was told the issue is open.
    //   - STILL GREEN, correctly: `a fresh claim … still lands In progress` (the fabricated facts and the
    //     real ones AGREE on this row — which is precisely why the defect survived every existing test),
    //     `a claim whose blocker has CLOSED …` (same agreement), and `an UNREADABLE open-PR probe
    //     withholds …` (the mutation leaves the READS and their fail-closed plumbing in place, so the
    //     probe still fails and still withholds before the observation is ever built), and the read-cost leg
    //     (the mutation changes what the reducer is TOLD, never which reads are made).
    // RESTORED, rebuilt, all eight green — and the full 795-test suite green with them.

