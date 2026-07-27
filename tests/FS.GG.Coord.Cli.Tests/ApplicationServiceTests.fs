namespace FS.GG.Coord.Cli.Tests

open System
open System.IO
open System.Text.Json
open Xunit
open FS.GG.Coord.Types
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Transport
open FS.GG.Coord.Cli

module ApplicationServiceTests =

    let private row number repo title status state isPullRequest : Scan.Row =
        { Ref =
            { Owner = "FS-GG"
              Repo = repo
              Number = number }
          Title = title
          Status = status
          BlockedByRaw = ""
          State = state
          IsPullRequest = isPullRequest }

    [<Fact>]
    let ``ready application service preserves the exact JSON projection contract`` () =
        let rows =
            [ row 1 ".github" "quote: \"kept\"" BoardStatus.Ready IssueState.Open false
              row 2 ".github" "done" BoardStatus.Done IssueState.Closed false
              row 3 ".github" "pull request" BoardStatus.Ready IssueState.Open true
              row 4 "FS.GG.Game" "other repo" BoardStatus.Backlog IssueState.Open false ]

        let selected = ReadyApplication.select (Some ".github") None false rows

        Assert.Equal(1, List.length selected.Rows)
        Assert.Equal(
            """[{"number":1,"repo":"FS-GG/.github","title":"quote: \u0022kept\u0022","status":"Ready","state":"OPEN"}]""",
            Render.renderReadyJson selected.Rows
        )

    [<Fact>]
    let ``ready status selection is case-insensitive and can explicitly include Done`` () =
        let rows =
            [ row 1 ".github" "ready" BoardStatus.Ready IssueState.Open false
              row 2 ".github" "done" BoardStatus.Done IssueState.Closed false ]

        let selected = ReadyApplication.select None (Some "done") false rows

        Assert.Equal<int list>([ 2 ], selected.Rows |> List.map (fun item -> item.Ref.Number))

    [<Fact>]
    let ``lint summary owns strict gate semantics`` () =
        let permissive = LintApplication.summarize false [ "note" ]
        let strict = LintApplication.summarize true [ "note" ]
        let error = LintApplication.summarize false [ "error" ]

        Assert.False(permissive.Fails)
        Assert.True(strict.Fails)
        Assert.True(error.Fails)
        Assert.Equal(1, error.Errors)

    // ---- .github#1517 — `widen`/`set-paths` HONOUR `--json` -----------------------------------------
    //
    // THE DEFECT THESE PIN. `--json` is `Global` in `scopeOf` and `command-contract` advertises it on both
    // verbs, so the parser accepted it and the #991 residue rule had nothing to refuse — and then
    // `updateTouchSet` rendered with a bare `printfn` and never read `opts.Render`. Both verbs printed
    // human prose on `--json` and exited 0, which is #867/#991's "accepted and ignored" defect arriving
    // through the one door the residue rule cannot watch: the flag really IS this command's.
    //
    // WHY THESE DRIVE THE REAL HANDLER RATHER THAN THE RENDERER. A test that only called
    // `Render.renderPathUpdateJson` would have passed on the broken engine the moment the function existed
    // — the bug was never in a renderer, it was in a handler that did not CALL one. The subject has to be
    // `Client.widen`/`Client.setPaths` and their stdout, so the assertion is about the dispatch decision.
    // That is why these live here, over `Fake.Recorder`, rather than beside the string-shape tests above.
    //
    // BOTH VERBS, ALWAYS. They are one shared `updateTouchSet`, so a test that exercised only `widen`
    // would pass for `set-paths` by luck rather than by construction — `OptionsTests`' `pathsVerbs` makes
    // the same argument about the parser half of this same command line (#1507).

    /// The board the fixture serves: project `Coordination` #12, one Status field, and the items below.
    let private graphqlAnswer (items: string) (query: string) : string option =
        if query.Contains "projectsV2" then
            Some
                """{"data":{"organization":{"projectsV2":{"nodes":[{"number":12,"title":"Coordination","id":"PVT_coord"}]}}},"rateLimit":{"cost":1,"remaining":4977}}"""
        elif query.Contains "fields(first" then
            Some
                """{"data":{"organization":{"projectV2":{"fields":{"nodes":[{"id":"PVTSSF_status","name":"Status","dataType":"SINGLE_SELECT","options":[{"id":"opt_ready","name":"Ready"},{"id":"opt_wip","name":"In progress"}]},{"id":"PVTF_blocked","name":"Blocked by","dataType":"TEXT"}]}}}},"rateLimit":{"cost":1,"remaining":4977}}"""
        elif query.Contains "items(first" then
            Some
                $"""{{"data":{{"organization":{{"projectV2":{{"items":{{"pageInfo":{{"hasNextPage":false,"endCursor":null}},"nodes":[%s{items}]}}}}}}}},"rateLimit":{{"cost":1,"remaining":4977}}}}"""
        else
            None

    let private boardItem (number: int) (title: string) =
        $"""{{"status":{{"name":"In progress"}},"blockedBy":null,"content":{{"__typename":"Issue","number":%d{number},"title":"%s{title}","state":"OPEN","repository":{{"nameWithOwner":"FS-GG/FS.GG.SDD"}}}}}}"""

    /// One live claim marker, timestamped NOW so the lease is fresh. Sessionless, exactly as
    /// `kit_server.py` serves it: a marker carrying no session is indistinguishable from ours, which is
    /// `verifyHeld`'s documented behaviour and not a shortcut taken here.
    let private commentsFor (holders: Map<int, string>) (number: int) =
        let ts = DateTime.UtcNow.ToString "yyyy-MM-ddTHH:mm:ssZ"

        match Map.tryFind number holders with
        | None -> "[]"
        | Some worker ->
            $"""[{{"id":%d{8000 + number},"body":"<!-- fsgg:claim worker=%s{worker} lease=120 -->\nheld","user":{{"login":"EHotwagner"}},"created_at":"%s{ts}","updated_at":"%s{ts}"}}]"""

    let private ok (body: string) : Errors.IoResult<Response> =
        Ok
            { Status = 200
              Body = body
              ETag = None
              NextLink = None }

    /// A transport serving one board. `bodies` is issue number → issue body (its `Paths:` declaration),
    /// `holders` is issue number → the worker whose claim marker sits on it, and `sayFails` makes the
    /// courtesy-notice POST fail so the receipt's `notified:false` leg can be pinned.
    let private world (bodies: Map<int, string>) (holders: Map<int, string>) (sayFails: bool) =
        let items =
            bodies
            |> Map.toList
            |> List.map (fun (n, _) -> boardItem n $"item %d{n}")
            |> String.concat ","

        Fake.Recorder(fun (req: Request) ->
            let path = req.Path.Trim '/'

            let issueNumber (suffix: string) =
                let prefix = "repos/FS-GG/FS.GG.SDD/issues/"

                if path.StartsWith prefix && path.EndsWith suffix then
                    let middle = path.Substring(prefix.Length, path.Length - prefix.Length - suffix.Length)

                    match Int32.TryParse middle with
                    | true, n -> Some n
                    | _ -> None
                else
                    None

            match req.Method, path with
            | "POST", "graphql" ->
                match req.Body with
                | Query(document, _) ->
                    match graphqlAnswer items document with
                    | Some answer -> ok answer
                    | None -> Error(Errors.NotFound $"the fixture serves no answer for: %s{document}")
                | _ -> Error(Errors.NotFound "a graphql call with no document")
            | "GET", "rate_limit" -> ok """{"resources":{"graphql":{"remaining":4980,"limit":5000}}}"""
            | "GET", _ when (issueNumber "/comments").IsSome ->
                ok (commentsFor holders (issueNumber "/comments").Value)
            | "POST", _ when (issueNumber "/comments").IsSome ->
                if sayFails then
                    Error(Errors.NotFound "the notice could not be posted")
                else
                    ok """{"id":9001}"""
            | ("GET" | "PATCH"), _ when (issueNumber "").IsSome ->
                let n = (issueNumber "").Value

                match Map.tryFind n bodies with
                | Some body ->
                    // A PATCH is accepted but not persisted: the #523 re-check compares the touch-set it
                    // REWROTE in memory, never a re-read, so persisting it would test nothing extra.
                    ok (JsonSerializer.Serialize {| number = n; body = body |})
                | None -> Error(Errors.NotFound $"no issue %d{n}")
            | m, p -> Error(Errors.NotFound $"the fixture serves no %s{m} %s{p}"))

    let private context (transport: Fake.Recorder) : Client.Context =
        { Transport = transport
          Owner = "FS-GG"
          Title = "Coordination"
          DefaultRepo = Some "FS.GG.SDD"
          ChoreLocks = [] }

    let private options (args: string list) : Options.Options =
        match Options.parse args with
        | Ok o -> o
        | Error e -> failwithf "the fixture's own argv did not parse: %s" e

    /// Run one verb against a THROWAWAY cache root and capture its stdout.
    ///
    /// `FSGG_COORD_CACHE` is process-global; this is safe only because `AssemblyInfo.fs` disables xUnit's
    /// cross-class parallelism, which is the same licence `FollowupsTests.withCache` runs on. A fresh
    /// directory per call also keeps the board scan and board map from leaking BETWEEN these legs, which
    /// matters: the OVERLAP legs serve a different board from the DISJOINT ones.
    let private run (transport: Fake.Recorder) (args: string list) : int * string =
        let dir = Path.Combine(Path.GetTempPath(), "fsgg-1517-" + Guid.NewGuid().ToString "n")
        let previousCache = Environment.GetEnvironmentVariable "FSGG_COORD_CACHE"
        let previousKitRoot = Environment.GetEnvironmentVariable "FSGG_KIT_ROOT"
        let stdout = Console.Out
        use captured = new StringWriter()

        try
            Directory.CreateDirectory dir |> ignore
            Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", dir)
            // The kit-digest warning is an observation off the TREE, and it is stderr-only, so it cannot
            // corrupt stdout in either projection. Pointed at an empty directory it stays silent, which
            // keeps the captured stderr about this command rather than about the developer's checkout.
            Environment.SetEnvironmentVariable("FSGG_KIT_ROOT", dir)
            Console.SetOut captured

            let opts = options args

            let code =
                match opts.Command with
                | Options.Widen -> Client.widen (context transport) opts
                | Options.SetPaths -> Client.setPaths (context transport) opts
                | other -> failwithf "this fixture drives widen/set-paths only, got %A" other

            Console.Out.Flush()
            code, captured.ToString()
        finally
            Console.SetOut stdout
            Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", previousCache)
            Environment.SetEnvironmentVariable("FSGG_KIT_ROOT", previousKitRoot)

            try
                Directory.Delete(dir, true)
            with _ ->
                ()

    let private disjointWorld () =
        world (Map.ofList [ 74, "Paths: scripts/fsgg-coord" ]) (Map.ofList [ 74, "kite-469" ]) false

    /// #74 is ours; #75 is a live neighbour reserving the very path we are about to declare.
    let private overlappingWorld (sayFails: bool) =
        world
            (Map.ofList [ 74, "Paths: scripts/fsgg-coord"; 75, "Paths: src/Shared.fs" ])
            (Map.ofList [ 74, "kite-469"; 75, "otter-9c21" ])
            sayFails

    let private parsed (out: string) : JsonElement =
        // A `--json` projection is a SINGLE object and nothing else on the stream. Parsing the WHOLE of
        // stdout — not a line grepped out of it — is what makes "and no prose" an assertion rather than a
        // hope: prose above or below the object is a parse failure here.
        try
            JsonDocument.Parse(out.Trim()).RootElement
        with e ->
            failwithf "stdout was not one JSON document — this is the #1517 defect.\nstdout was:\n%s\n(%s)" out e.Message

    let private str (name: string) (el: JsonElement) = el.GetProperty(name).GetString()

    let private strings (name: string) (el: JsonElement) =
        el.GetProperty(name).EnumerateArray() |> Seq.map (fun e -> e.GetString()) |> List.ofSeq

    [<Theory>]
    [<InlineData("widen", "widened")>]
    [<InlineData("set-paths", "set")>]
    let ``both --paths verbs emit ONE parseable JSON object under --json`` (verb: string, kind: string) =
        let code, out =
            run (disjointWorld ()) [ verb; "FS.GG.SDD#74"; "--worker"; "kite-469"; "--json"; "--paths"; "src/Shared.fs" ]

        let receipt = parsed out

        Assert.Equal("FS.GG.SDD#74", str "ref" receipt)
        Assert.Equal("FS-GG/FS.GG.SDD", str "repo" receipt)
        Assert.Equal(74, receipt.GetProperty("number").GetInt32())
        Assert.Equal("kite-469", str "worker" receipt)
        Assert.Equal(kind, str "kind" receipt)

        // THE RESULTING DECLARATION, not the tokens that were asked for. `widen` is a UNION (#1377), so it
        // carries the token that was already there; `set-paths` REPLACES, so it does not. A receipt that
        // merely echoed `--paths` back could not tell those two apart, and telling them apart is the whole
        // reason a machine caller reads this field.
        let expected =
            if verb = "widen" then
                [ "scripts/fsgg-coord"; "src/Shared.fs" ]
            else
                [ "src/Shared.fs" ]

        Assert.Equal<string list>(expected, strings "paths" receipt)

        // The DISJOINT verdict the human form prints — IN the object, so a machine consumer never learns
        // it by matching prose.
        Assert.Equal("disjoint", str "verdict" receipt)
        Assert.Empty(receipt.GetProperty("collisions").EnumerateArray())

        // Exit code UNCHANGED. The renderer was the bug; the verbs' semantics were not.
        Assert.Equal(0, code)

    [<Theory>]
    [<InlineData("widen", "widened")>]
    [<InlineData("set-paths", "set")>]
    let ``without --json both verbs print the byte-identical human receipt`` (verb: string, past: string) =
        let code, out =
            run (disjointWorld ()) [ verb; "FS.GG.SDD#74"; "--worker"; "kite-469"; "--paths"; "src/Shared.fs" ]

        // #1517 is an ADDITION, not a reformat: every existing recipe reads these two lines, so they are
        // pinned byte for byte rather than merely "contains".
        let declared =
            if verb = "widen" then
                "scripts/fsgg-coord, src/Shared.fs"
            else
                "src/Shared.fs"

        let expected =
            $"%s{past} FS.GG.SDD#74 → Paths: %s{declared}"
            + Environment.NewLine
            + "DISJOINT — the updated touch-set clears every live claim in FS-GG/FS.GG.SDD (#353)."
            + Environment.NewLine

        Assert.Equal(expected, out)
        Assert.Equal(0, code)

    [<Theory>]
    [<InlineData "widen">]
    [<InlineData "set-paths">]
    let ``the OVERLAP branch rides IN the JSON object, not on a second stream`` (verb: string) =
        let code, out =
            run
                (overlappingWorld false)
                [ verb; "FS.GG.SDD#74"; "--worker"; "kite-469"; "--json"; "--paths"; "src/Shared.fs" ]

        let receipt = parsed out

        Assert.Equal("overlap", str "verdict" receipt)

        let collisions = receipt.GetProperty("collisions").EnumerateArray() |> List.ofSeq
        let collision = Assert.Single collisions

        Assert.Equal("FS.GG.SDD#75", str "ref" collision)
        Assert.Equal("FS-GG/FS.GG.SDD", str "repo" collision)
        Assert.Equal(75, collision.GetProperty("number").GetInt32())
        Assert.Equal("otter-9c21", str "worker" collision)

        // An ARRAY, like the `paths` beside it — not the comma-joined string the human stderr line uses.
        Assert.Equal<string list>([ "src/Shared.fs" ], strings "sharedTokens" collision)

        // The notice this command posts on the OTHER worker's item is part of the receipt. A notice that
        // FAILED still leaves a standing collision, so a consumer must be able to read the outcome rather
        // than infer it from an absent stderr line.
        Assert.True(collision.GetProperty("notified").GetBoolean())
        Assert.Equal(JsonValueKind.Null, collision.GetProperty("notifyError").ValueKind)

        // ExitContended (6) — UNCHANGED, and the same in both projections.
        Assert.Equal(6, code)

    [<Theory>]
    [<InlineData "widen">]
    [<InlineData "set-paths">]
    let ``a courtesy notice that failed is reported IN the receipt, not by silence`` (verb: string) =
        let code, out =
            run
                (overlappingWorld true)
                [ verb; "FS.GG.SDD#74"; "--worker"; "kite-469"; "--json"; "--paths"; "src/Shared.fs" ]

        let collision =
            (parsed out).GetProperty("collisions").EnumerateArray() |> Seq.head

        Assert.False(collision.GetProperty("notified").GetBoolean())
        Assert.Equal(JsonValueKind.String, collision.GetProperty("notifyError").ValueKind)

        // A failed notice does NOT downgrade the collision to disjoint, and does not change the exit code.
        Assert.Equal("overlap", str "verdict" (parsed out))
        Assert.Equal(6, code)

    [<Theory>]
    [<InlineData("widen", "widened")>]
    [<InlineData("set-paths", "set")>]
    let ``the OVERLAP human projection is unchanged and puts nothing else on stdout`` (verb: string, past: string) =
        let code, out =
            run (overlappingWorld false) [ verb; "FS.GG.SDD#74"; "--worker"; "kite-469"; "--paths"; "src/Shared.fs" ]

        // The human OVERLAP branch has always written its detail to stderr and only the receipt line to
        // stdout. That is the split #1517 fixes FOR MACHINES by putting the detail in the object — it does
        // not move a byte of the human form, which existing recipes read.
        //
        // Pinned as an EQUALITY, like the DISJOINT leg above. A `DoesNotContain "OVERLAP"` would still pass
        // if the Text branch also emitted the JSON object, whose verdict is the lowercase `"overlap"`.
        let declared =
            if verb = "widen" then
                "scripts/fsgg-coord, src/Shared.fs"
            else
                "src/Shared.fs"

        Assert.Equal($"%s{past} FS.GG.SDD#74 → Paths: %s{declared}" + Environment.NewLine, out)
        Assert.Equal(6, code)

    // ---- .github#1524 — `reconcile --apply --json` is ONE document -----------------------------------
    //
    // THE DEFECT THESE PIN, AND WHY IT IS THE OPPOSITE OF #1517's. `#1517` fixed handlers that never read
    // `opts.Render` at all. `reconcile` DOES read it: the branch that chooses the JSON array from the human
    // table is right there and always has been. It renders correctly — and then the `--apply` phase BELOW
    // that branch prints its per-write outcome to stdout unconditionally, past a document that has already
    // ended. So a gate asking "does this handler consult the render mode?" answers YES and reports green
    // over a stream no parser can read. That is #266's shape exactly: a check reporting green over the one
    // thing it cannot see.
    //
    // WHY THESE DRIVE `Client.reconcile` AND ASSERT ON THE WHOLE OF STDOUT. The renderer was never the bug
    // — a test calling `Render.renderReconcileJson` would pass on the broken engine. The subject has to be
    // the handler, and the assertion has to be "the ENTIRE stream is one document", because the defect is
    // precisely bytes arriving AFTER a correct document.
    //
    // THE QUEUED WRITE IS THE LEG THAT MATTERS. A deferred board write is one the budget refused and
    // NOTHING replays — so of every fact this verb reports, it is the one whose loss is unrecoverable, and
    // it was riding on the half of the stream a parser throws away.

    /// The board `reconcile` reads. Every number in `closed` is on the board as a CLOSED issue whose column
    /// still says In progress — a `CLOSED-ISSUE-NOT-DONE` chore, whose remedy is Status=Done. A number in
    /// `rateLimited` meets an exhausted GraphQL budget on its item-id lookup, which is what makes its board
    /// write QUEUE instead of land (`Errors.isQueueable`: a rate limit, and nothing else, may be deferred).
    let private reconcileWorld (closed: int list) (rateLimited: Set<int>) =
        let items =
            closed
            |> List.map (fun n ->
                $"""{{"status":{{"name":"In progress"}},"blockedBy":null,"content":{{"__typename":"Issue","number":%d{n},"title":"item %d{n}","state":"CLOSED","repository":{{"nameWithOwner":"FS-GG/FS.GG.SDD"}}}}}}""")
            |> String.concat ","

        Fake.Recorder(fun (req: Request) ->
            match req.Method, req.Path.Trim '/' with
            | "GET", "rate_limit" -> ok """{"resources":{"graphql":{"remaining":4980,"limit":5000}}}"""
            | "POST", "graphql" ->
                match req.Body with
                | Query(document, variables) ->
                    let number =
                        variables
                        |> List.tryPick (fun (k, v) ->
                            match k, v with
                            | "number", VNumber n -> Some(int n)
                            | _ -> None)

                    // `projectItems` FIRST — it is unique to the item-id lookup, and testing it before the
                    // board queries keeps a document that mentions both from answering the wrong branch.
                    if document.Contains "projectItems" then
                        match number with
                        | Some n when rateLimited.Contains n ->
                            // How GitHub actually reports an exhausted GraphQL budget: HTTP **200**
                            // carrying `errors`. `Budget.isRateLimited` matches the message, and only this
                            // shape yields `RateLimited` — which is the only error `boardWrite` may queue.
                            ok """{"errors":[{"message":"API rate limit exceeded for this token"}]}"""
                        | Some n ->
                            ok
                                $"""{{"data":{{"repository":{{"issue":{{"projectItems":{{"nodes":[{{"id":"PVTI_%d{n}","project":{{"number":12}}}}]}}}}}}}},"rateLimit":{{"cost":1,"remaining":4977}}}}"""
                        | None -> Error(Errors.NotFound "an item-id lookup with no number")
                    elif document.Contains "updateProjectV2ItemFieldValue" then
                        ok """{"data":{"updateProjectV2ItemFieldValue":{"clientMutationId":null}}}"""
                    elif document.Contains "projectsV2" then
                        ok
                            """{"data":{"organization":{"projectsV2":{"nodes":[{"number":12,"title":"Coordination","id":"PVT_coord"}]}}},"rateLimit":{"cost":1,"remaining":4977}}"""
                    elif document.Contains "fields(first" then
                        // `Done` is an option here because the remedy WRITES it: a single-select write
                        // resolves the value to an option id before it is attempted.
                        ok
                            """{"data":{"organization":{"projectV2":{"fields":{"nodes":[{"id":"PVTSSF_status","name":"Status","dataType":"SINGLE_SELECT","options":[{"id":"opt_ready","name":"Ready"},{"id":"opt_wip","name":"In progress"},{"id":"opt_done","name":"Done"}]},{"id":"PVTF_blocked","name":"Blocked by","dataType":"TEXT"}]}}}},"rateLimit":{"cost":1,"remaining":4977}}"""
                    elif document.Contains "items(first" then
                        ok
                            $"""{{"data":{{"organization":{{"projectV2":{{"items":{{"pageInfo":{{"hasNextPage":false,"endCursor":null}},"nodes":[%s{items}]}}}}}}}},"rateLimit":{{"cost":1,"remaining":4977}}}}"""
                    else
                        Error(Errors.NotFound $"the fixture serves no answer for: %s{document}")
                | _ -> Error(Errors.NotFound "a graphql call with no document")
            // The OPEN-issue listing. Empty, and that is the fixture's point: every item here is CLOSED, so
            // none of them is open, none carries a claim marker, and `choresFor` takes its unreserved
            // branch — which is the one `CLOSED-ISSUE-NOT-DONE` lives on.
            | "GET", "repos/FS-GG/FS.GG.SDD/issues" -> ok "[]"
            // A CLOSED candidate is swept with no body, marker, or blocker read (`Scan.snapshot`), so this
            // exists only to keep an unexpected REST call loud rather than silently empty.
            | "GET", path when path.EndsWith "/comments" -> ok "[]"
            | m, p -> Error(Errors.NotFound $"the fixture serves no %s{m} %s{p}"))

    /// Run `reconcile` against a throwaway cache root, capturing stdout AND stderr separately.
    ///
    /// Separately, because the whole question here is WHICH STREAM a fact went out on. A helper that merged
    /// them could not tell the fix from the defect.
    let private runReconcileWith
        (transport: Fake.Recorder)
        (args: string list)
        (adjust: Options.Options -> Options.Options)
        : int * string * string =
        let dir = Path.Combine(Path.GetTempPath(), "fsgg-1524-" + Guid.NewGuid().ToString "n")
        let previousCache = Environment.GetEnvironmentVariable "FSGG_COORD_CACHE"
        let previousKitRoot = Environment.GetEnvironmentVariable "FSGG_KIT_ROOT"
        let stdout = Console.Out
        let stderr = Console.Error
        use capturedOut = new StringWriter()
        use capturedErr = new StringWriter()

        try
            Directory.CreateDirectory dir |> ignore
            Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", dir)
            Environment.SetEnvironmentVariable("FSGG_KIT_ROOT", dir)
            Console.SetOut capturedOut
            Console.SetError capturedErr

            let opts = adjust (options args)

            let code =
                match opts.Command with
                | Options.Reconcile -> Client.reconcile (context transport) opts
                | other -> failwithf "this fixture drives reconcile only, got %A" other

            Console.Out.Flush()
            Console.Error.Flush()
            code, capturedOut.ToString(), capturedErr.ToString()
        finally
            Console.SetOut stdout
            Console.SetError stderr
            Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", previousCache)
            Environment.SetEnvironmentVariable("FSGG_KIT_ROOT", previousKitRoot)

            try
                Directory.Delete(dir, true)
            with _ ->
                ()

    /// Parse the WHOLE of stdout as one JSON array. Not a line grepped out of it — that is the entire
    /// assertion: .github#1524 is a correct document with prose printed after it, and only parsing every
    /// byte can see the difference.
    let private parsedArray (out: string) : JsonElement list =
        let root =
            try
                JsonDocument.Parse(out.Trim()).RootElement
            with e ->
                failwithf
                    "stdout was not ONE JSON document — this is the .github#1524 defect.\nstdout was:\n%s\n(%s)"
                    out
                    e.Message

        Assert.Equal(JsonValueKind.Array, root.ValueKind)
        root.EnumerateArray() |> List.ofSeq

    let private reconcileArgs (extra: string list) =
        [ "reconcile"; "--repo"; "FS.GG.SDD"; "--worker"; "heron-1f20" ] @ extra

    /// Run a command line as written.
    let private runReconcile (transport: Fake.Recorder) (args: string list) = runReconcileWith transport args id

    /// Drive the handler in the `--apply --json` combination THE PARSER CURRENTLY REFUSES.
    ///
    /// **THE ISSUE AS FILED RESTS ON A FALSE PREMISE, AND THIS IS WHERE IT SHOWS.** `.github#1524` was
    /// derived by READING `Client.fs`, not by running the verb — and `Options.parse` has refused
    /// `reconcile --apply --json` outright since #1429 ("--apply and --json are mutually exclusive"). So
    /// the interleaved stream the issue reports is real IN THE CODE and unreachable THROUGH THE CLI: the
    /// command exits 1 with a refusal instead.
    ///
    /// That refusal is a WORKAROUND, not a fix — it deletes the machine projection of a MUTATING verb, and
    /// it is exactly what leaves `reconcile --apply` with no way to report which writes queued. Lifting it
    /// is a one-line change in `src/FS.GG.Coord.Cli/Options.fs`, which this item does not declare and which
    /// `.github#1523` holds while it redesigns that very field. So the handler is fixed here, the guard is
    /// filed separately, and the two land in that order — a guard lifted over the UNFIXED handler would
    /// ship the defect the moment it opened.
    ///
    /// Setting the two fields directly is therefore not a shortcut around a check: it is the only way to
    /// reach the handler contract this item owns. `parseRefusal` below pins the guard itself, so lifting it
    /// is a conscious edit rather than an accident.
    let private runApplyJson (transport: Fake.Recorder) =
        runReconcileWith transport (reconcileArgs []) (fun o ->
            { o with
                Apply = true
                Render = Options.Json })

    [<Fact>]
    let ``the parser still refuses --apply --json, which is why the handler is driven directly`` () =
        // THE PREMISE, PINNED. When `.github#1523` lifts this guard, this test fails and its replacement is
        // a deliberate decision — which is the point. It is also the record that the interleaving defect
        // was never reachable from a command line, so nobody re-derives it from the code a third time.
        match Options.parse (reconcileArgs [ "--apply"; "--json" ]) with
        | Ok _ -> failwith "the guard is gone — lift the handler tests to a real command line (.github#1524)"
        | Error message -> Assert.Contains("mutually exclusive", message)

    [<Fact>]
    let ``reconcile --apply --json puts the whole stream in ONE document`` () =
        let code, out, _ = runApplyJson (reconcileWorld [ 101; 102 ] (Set.ofList [ 102 ]))

        // Before the fix this array parsed and then TWO prose lines followed it on the same stream.
        let rows = parsedArray out
        Assert.Equal(2, List.length rows)

        let bySubject =
            rows |> List.map (fun r -> str "subject" r, r) |> Map.ofList

        // THE WRITE THAT LANDED. The wire word is `written`, which is `ClaimReceipt.statusWrite`'s name for
        // this exact `Board.WriteOutcome` case — one CLI, one name per fact. The human line still says
        // "applied", and that is the human line's business.
        let landed = bySubject.["FS.GG.SDD#101"]
        Assert.Equal("written", str "outcome" landed)
        Assert.Equal("Status", str "field" landed)
        Assert.Equal("Done", str "value" landed)
        Assert.Equal(JsonValueKind.Null, landed.GetProperty("error").ValueKind)

        // THE QUEUED WRITE — the leg whose loss is unrecoverable, and the reason this issue was filed. It
        // is a DISTINCT VALUE of a closed set, not a sentence a consumer greps for the word "queued" in.
        let queued = bySubject.["FS.GG.SDD#102"]
        Assert.Equal("deferred", str "outcome" queued)
        Assert.Equal("Status", str "field" queued)
        Assert.Equal("Done", str "value" queued)

        // The finding fields are still there — `--apply` ADDS to the dry-run row, it does not replace it.
        Assert.Equal("CLOSED-ISSUE-NOT-DONE", str "rule" queued)
        Assert.Equal("quick", str "size" queued)

        // Exit code UNCHANGED. A deferred write is not a failure — it is a promise the queue keeps.
        Assert.Equal(0, code)

    [<Fact>]
    let ``the queued-write remedy moves to stderr rather than into the document`` () =
        let _, out, err = runApplyJson (reconcileWorld [ 101; 102 ] (Set.ofList [ 102 ]))

        // stdout stays parseable...
        parsedArray out |> ignore

        // ...and the operator remedy is not simply dropped. `run scripts/fsgg-coord flush` is a REMEDY, not
        // a fact about the board, so it does not belong in the document — but nothing replays a queued
        // write, so it must not vanish either. stderr is where this CLI already puts diagnostics.
        Assert.Contains("QUEUED", err)
        Assert.Contains("flush", err)
        Assert.Contains("FS.GG.SDD#102", err)

        // ...and it names ONLY the write that actually queued.
        Assert.DoesNotContain("FS.GG.SDD#101", err)

    [<Fact>]
    let ``reconcile --apply without --json is byte-identical`` () =
        let code, out, _ =
            runReconcile (reconcileWorld [ 101; 102 ] (Set.ofList [ 102 ])) (reconcileArgs [ "--apply" ])

        // THE BARE FORM IS THE ONE EVERY RECIPE RUNS. Pinned as an EQUALITY over the whole stream, byte for
        // byte, because breaking it would be worse than the bug being fixed.
        let nl = Environment.NewLine

        let expected =
            "applying (2 mechanical finding(s))" + nl
            + "  CLOSED-ISSUE-NOT-DONE    FS.GG.SDD#101            Status=Done" + nl
            + "  CLOSED-ISSUE-NOT-DONE    FS.GG.SDD#102            Status=Done" + nl
            + "judgement findings are report-only: scripts/fsgg-coord lint --repo FS.GG.SDD" + nl
            + "applied  FS.GG.SDD#101  Status=Done" + nl
            + "queued   FS.GG.SDD#102  Status=Done (run scripts/fsgg-coord flush)" + nl

        Assert.Equal(expected, out)
        Assert.Equal(0, code)

    [<Fact>]
    let ``the dry-run --json projection is unchanged, alphabetical keys and all`` () =
        let code, out, _ = runReconcile (reconcileWorld [ 101 ] Set.empty) (reconcileArgs [ "--json" ])

        // THE KEY ORDER IS THE CONTRACT, AND IT IS ALPHABETICAL — `id,remedy,rule,size,statement,subject`.
        // Not the order the old source literal read as: this projection was an F# ANONYMOUS RECORD, whose
        // fields the compiler sorts by name, so those are the bytes consumers have always received. Pinned
        // here because the fix rewrites this as a real `Utf8JsonWriter`, and a hand-written renderer in the
        // "obvious" order would have silently rewritten the wire contract while every test still passed.
        //
        // NO `outcome`/`field`/`value`/`error` — a dry run attempted nothing, so it claims nothing.
        // The em dash in `Chore.Statement` is NOT emitted raw: the default encoder escapes every
        // non-ASCII character, and it does so identically for the old `JsonSerializer` and the new
        // `Utf8JsonWriter`. Spelled from ASCII so the assertion says which bytes it means.
        let escapedEmDash = "\\u2014"

        let expected =
            """[{"id":"CLOSED-ISSUE-NOT-DONE:FS-GG/FS.GG.SDD#101","remedy":"Status=Done","rule":"CLOSED-ISSUE-NOT-DONE","size":"quick","statement":"FS.GG.SDD#101: the issue is CLOSED but the board says In progress """
            + escapedEmDash
            + """ set Status to Done.","subject":"FS.GG.SDD#101"}]"""

        Assert.Equal(expected, out.Trim())
        Assert.Equal(0, code)

    [<Fact>]
    let ``a clean board still emits an empty array, not nothing`` () =
        let code, out, _ = runApplyJson (reconcileWorld [] Set.empty)

        Assert.Equal("[]", out.Trim())
        Assert.Empty(parsedArray out)
        Assert.Equal(0, code)
