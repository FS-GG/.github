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
          IsPullRequest = isPullRequest
          BoardClass = None
          Phase = None
          CreatedAt = None }

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
            """[{"number":1,"repo":"FS-GG/.github","title":"quote: \u0022kept\u0022","status":"Ready","class":null,"state":"OPEN"}]""",
            Render.renderReadyJson selected.Rows
        )

    [<Fact>]
    let ``#1588 ready --json renders a SET class, not only the null case`` () =
        // THE WIRE GREW ONE KEY, DELIBERATELY: `class`, between `status` and `state`. It is pinned above
        // as an equality over the whole document so it cannot grow again by accident — that document is
        // the contract `/check-board` and `next` read, and `drive-board`'s stopping rule is not
        // executable without this key, because nothing else emits an item's class to a machine. `null`
        // rather than omitted, on `status`'s terms: an unset column is a modelled fact (#437).
        //
        // This case exists because the `null` above would ALSO pass against a renderer that hardcoded
        // `WriteNull("class")` — a key always present and always empty, off which every consumer would
        // read "no defects anywhere". That is the vacuity #266 is an epic about, and the stopping rule
        // turns on this value being real.
        let classed =
            { row 1 ".github" "a defect" BoardStatus.Ready IssueState.Open false with
                BoardClass = Some Defect }

        Assert.Equal(
            """[{"number":1,"repo":"FS-GG/.github","title":"a defect","status":"Ready","class":"defect","state":"OPEN"}]""",
            Render.renderReadyJson [ classed ]
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

    /// One board row, in the COLUMN the caller names. The column is a parameter because it is the whole of
    /// the difference between a queue that schedules something and one that does not: `Ready` is the only
    /// startable column, so every fixture built on the `In progress` default below is — by construction —
    /// a queue whose empty arm is under test (.github#1562 needed the OTHER arm as well).
    let private boardItemIn (status: string) (number: int) (title: string) =
        $"""{{"status":{{"name":"%s{status}"}},"blockedBy":null,"content":{{"__typename":"Issue","number":%d{number},"title":"%s{title}","state":"OPEN","repository":{{"nameWithOwner":"FS-GG/FS.GG.SDD"}}}}}}"""

    let private boardItem (number: int) (title: string) = boardItemIn "In progress" number title

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
    let private worldWith (statusFor: int -> string) (bodies: Map<int, string>) (holders: Map<int, string>) (sayFails: bool) =
        // THE ROWS ARE RENDERED PER REQUEST, not once at construction. `statusFor` is a function, and it is
        // a function so that a fixture can make a column CHANGE BETWEEN TWO BOARD READS — which is the whole
        // of .github#1740 cause 1, and is unrepresentable if the board answer is frozen when the world is
        // built. Every existing caller passes a constant through `worldIn` and is unaffected.
        let items () =
            bodies
            |> Map.toList
            |> List.map (fun (n, _) -> boardItemIn (statusFor n) n $"item %d{n}")
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
                    match graphqlAnswer (items ()) document with
                    | Some answer -> ok answer
                    | None -> Error(Errors.NotFound $"the fixture serves no answer for: %s{document}")
                | _ -> Error(Errors.NotFound "a graphql call with no document")
            | "GET", "rate_limit" -> ok """{"resources":{"graphql":{"remaining":4980,"limit":5000}}}"""
            // The OFF-BOARD SWEEP (`Reads.openIssues`, .github#1525). `Scan.snapshot` lists a repo's open
            // issues to find claims sitting on items the board never listed, so every scheduling verb
            // (`take`/`next`/`batch`) makes this call and the `--paths` verbs above do not. An empty-but-
            // PRESENT array is a real answer this layer accepts (#461 tells it from a failed read): the
            // fixture's whole board is on the board, so there is nothing off it to sweep.
            | "GET", "repos/FS-GG/FS.GG.SDD/issues" -> ok "[]"
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

    let private worldIn (status: string) (bodies: Map<int, string>) (holders: Map<int, string>) (sayFails: bool) =
        worldWith (fun _ -> status) bodies holders sayFails

    let private world (bodies: Map<int, string>) (holders: Map<int, string>) (sayFails: bool) =
        worldIn "In progress" bodies holders sayFails

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
    /// The same, against a cache root the CALLER owns — so two invocations can share one, which is what a
    /// 90-second scan cache surviving between commands actually is (.github#1740). `run` below keeps the
    /// throwaway-per-call behaviour every other leg here relies on.
    let private runIn (dir: string) (transport: Fake.Recorder) (args: string list) : int * string =
        let previousCache = Environment.GetEnvironmentVariable "FSGG_COORD_CACHE"
        let previousKitRoot = Environment.GetEnvironmentVariable "FSGG_KIT_ROOT"

        // THE IDENTITY IS PART OF THE FIXTURE, AND IT WAS THE ONE PART LEFT AMBIENT (#1646).
        //
        // These legs name their worker with `--worker kite-469`, and `Identity.resolve` reads `--worker`
        // FIRST — so who this process is besides that was, until #1646, a fact nothing consulted. It is
        // consulted now: the lock verbs refuse `--worker <somebody else>` over that worker's live marker.
        // Run under an agent harness (`CLAUDE_CODE_SESSION_ID` is exported by Claude Code, and these tests
        // are written and run there), the test process derives a session id of its own, disagrees with
        // `kite-469`, and every leg below is refused — while the same suite passes in CI, where no such
        // variable exists.
        //
        // A test that passes only because a variable happens to be ABSENT is the shape #1646 is about, so
        // scrub them for the duration exactly as `FSGG_COORD_CACHE` is scrubbed. What remains is a caller
        // that derives NOTHING and names itself with `--worker` — which is precisely what an in-process
        // argv fixture is, and the human-operator case the flag exists for.
        let identityVars =
            [ "FSGG_WORKER"; "CLAUDE_CODE_SESSION_ID"; "OPENCODE_SESSION_ID"; "FSGG_AGENT_SESSION_ID" ]

        let previousIdentity =
            identityVars |> List.map (fun v -> v, Environment.GetEnvironmentVariable v)

        let stdout = Console.Out
        use captured = new StringWriter()

        try
            Directory.CreateDirectory dir |> ignore
            for v, _ in previousIdentity do
                Environment.SetEnvironmentVariable(v, null)

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

            for v, previous in previousIdentity do
                Environment.SetEnvironmentVariable(v, previous)

    let private run (transport: Fake.Recorder) (args: string list) : int * string =
        let dir = Path.Combine(Path.GetTempPath(), "fsgg-1517-" + Guid.NewGuid().ToString "n")

        try
            runIn dir transport args
        finally
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

    // ---- .github#1740 — A LIVE CLAIM WHOSE `Status` COLUMN HAS NOT LANDED STILL RESERVES ---------------
    //
    // THE DEFECT THESE PIN, AND WHY IT IS WORSE THAN IT LOOKS. `activeCollisions` picked which claims to
    // check by reading the board's `Status` COLUMN, so a claim whose MARKER was live but whose column had
    // not landed reserved nothing at all — and the answer it produced was `DISJOINT`, which is the one
    // verdict the touch-set protocol exists to make trustworthy. Measured live on 2026-07-28: two workers
    // declared `src/FS.GG.Coord.Cli/Client.fs` 41 seconds apart, `widen` printed `DISJOINT` 53 seconds
    // later, and the same check two hours on printed `OVERLAP`. Nothing about either declaration changed.
    //
    // WHY EVERY EXISTING OVERLAP LEG ABOVE IS BLIND TO THIS. They all run over `overlappingWorld`, whose
    // rows are `In progress` — the column the broken filter selects on. A fixture that pre-sets the
    // colliding row to the value under test cannot fail for the reason under test, so it never has.
    //
    // TWO CAUSES, TWO LEGS, AND THEY FAIL FOR DIFFERENT REASONS. The first is a STALE READ — the column had
    // landed and the scan was served from the 90s scheduling cache. The second is a WRITE THAT HAS NOT
    // HAPPENED — the column genuinely still reads `Ready` because the write is sitting in the deferral
    // queue. No amount of freshness closes the second, and the queue closes none of the first.

    /// #74 is ours. #75 holds a live claim on `src/Shared.fs` THROUGHOUT — only its board COLUMN moves.
    let private laggingBodies =
        Map.ofList [ 74, "Paths: scripts/fsgg-coord"; 75, "Paths: src/Shared.fs" ]

    let private laggingHolders = Map.ofList [ 74, "kite-469"; 75, "otter-9c21" ]

    let private cacheDir () =
        Path.Combine(Path.GetTempPath(), "fsgg-1740-" + Guid.NewGuid().ToString "n")

    let private widenOnto (paths: string) =
        [ "widen"; "FS.GG.SDD#74"; "--worker"; "kite-469"; "--json"; "--paths"; paths ]

    [<Fact>]
    let ``#1740 cause 1: a claim landing inside the scan-cache window still collides`` () =
        let dir = cacheDir ()

        // The column is a `ref` the TEST moves, not something the transport counts — a fixture that flipped
        // on "the second board read" would be asserting a request count, and would keep passing if the
        // request count changed for an unrelated reason.
        let column = ref "Ready"

        let world =
            worldWith (fun n -> if n = 75 then column.Value else "In progress") laggingBodies laggingHolders false

        try
            // 1. A first command populates the scan cache while #75's claim has not yet reached the board.
            let first, _ = runIn dir world (widenOnto "docs/unrelated.md")
            Assert.Equal(0, first)

            // 2. #75's `Status` write LANDS. Any read of the live board from here on sees `In progress`.
            column.Value <- "In progress"

            // 3. ...but the cached scan is only seconds old. On `Cache.Scheduling` this second command is
            //    served that cached `Ready`, #75 never becomes a candidate, its marker is never read, and
            //    the worker is told it may edit `src/Shared.fs`. THAT is the false DISJOINT.
            let code, out = runIn dir world (widenOnto "src/Shared.fs")

            let receipt = parsed out
            Assert.Equal("overlap", str "verdict" receipt)

            let collision = Assert.Single(receipt.GetProperty("collisions").EnumerateArray() |> List.ofSeq)
            Assert.Equal("FS.GG.SDD#75", str "ref" collision)
            Assert.Equal("otter-9c21", str "worker" collision)
            Assert.Equal<string list>([ "src/Shared.fs" ], strings "sharedTokens" collision)
            Assert.Equal(6, code)
        finally
            try
                Directory.Delete(dir, true)
            with _ ->
                ()

    /// One line of the deferral queue, in `Cache.renderDeferred`'s own shape.
    let private queuedStatusWrite (boardTitle: string) (field: string) (value: string) =
        JsonSerializer.Serialize
            {| ``ref`` = "FS-GG/FS.GG.SDD#75"
               field = field
               value = value
               at = DateTimeOffset.UtcNow.ToString "o"
               worker = "otter-9c21"
               boardOwner = "FS-GG"
               boardTitle = boardTitle |}
        + "\n"

    /// The AC3 fixture, LITERALLY: the board says `Ready` and it is not lying — the write has not happened.
    ///
    /// `queued` is the queue this run finds. It is a parameter because the NEGATIVE legs are what make this
    /// an assertion about the deferral queue rather than about "some file existing in the cache dir".
    let private runWithQueue (queued: string option) =
        let dir = cacheDir ()

        try
            Directory.CreateDirectory dir |> ignore

            match queued with
            | Some line -> File.WriteAllText(Path.Combine(dir, "pending.jsonl"), line)
            | None -> ()

            runIn dir (worldIn "Ready" laggingBodies laggingHolders false) (widenOnto "src/Shared.fs")
        finally
            try
                Directory.Delete(dir, true)
            with _ ->
                ()

    [<Fact>]
    let ``#1740 cause 2: a live claim whose Status write is still QUEUED collides from a Ready column`` () =
        let code, out = runWithQueue (Some(queuedStatusWrite "Coordination" "Status" "In progress"))

        let receipt = parsed out
        Assert.Equal("overlap", str "verdict" receipt)

        let collision = Assert.Single(receipt.GetProperty("collisions").EnumerateArray() |> List.ofSeq)
        Assert.Equal("FS.GG.SDD#75", str "ref" collision)
        Assert.Equal("otter-9c21", str "worker" collision)
        Assert.Equal(6, code)

    // THE NEGATIVE LEGS. Without these, "read the queue" could be satisfied by treating ANY queued entry —
    // or any non-empty queue file — as a claim, and the positive leg above would not notice. Each of these
    // is the SAME board, the SAME row and the SAME live marker; only the queue entry differs, and each must
    // fall back to the column, which says `Ready`.
    [<Theory>]
    // No queue at all: the honest DISJOINT this verb must still be able to give.
    [<InlineData("", "", "")>]
    // A write to a DIFFERENT FIELD says nothing about who holds the lock.
    [<InlineData("Coordination", "Class", "defect")>]
    // A `Status` write moving the row somewhere that is not a claim.
    [<InlineData("Coordination", "Status", "Done")>]
    // #882 — queued against ANOTHER BOARD. `flush` refuses to resolve it here; so must this.
    [<InlineData("Other Board", "Status", "In progress")>]
    let ``#1740: the queue leg reserves nothing on an entry that is not a live claim on THIS board``
        (boardTitle: string, field: string, value: string)
        =
        let queued =
            if boardTitle = "" then
                None
            else
                Some(queuedStatusWrite boardTitle field value)

        let code, out = runWithQueue queued

        Assert.Equal("disjoint", str "verdict" (parsed out))
        Assert.Equal(0, code)

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
    // THE QUEUED WRITE IS THE LEG THAT MATTERS. A deferred board write is one the budget refused. It is
    // QUEUED, not lost — `flush` replays it — but nothing replays it ON ITS OWN, so a caller that never
    // learns the write deferred is a caller that never runs `flush`. Of every fact this verb reports, that
    // is the one whose loss costs the most, and it was riding on the half of the stream a parser discards.

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

    /// A board with ONE OPEN item that declares `Class: defect` in its body and carries NO `Class`
    /// column — so `CLASS-PROJECTION-LAG` is the single chore (.github#1588).
    ///
    /// THIS FIXTURE EXISTS BECAUSE ITS ABSENCE HID A DEFECT. The new chore had thorough coverage in
    /// `ChoreTests` — the derivation, the retirement, the never-default rule — and none at all through
    /// `reconcile`, which is the only thing that RUNS it. The apply phase then printed
    /// `applied <item> Status=defect`: a receipt naming a column never touched, with a value `Status` has
    /// no option for, three lines under a table that correctly said `Class=defect`. Every test passed,
    /// because the four older kinds all write `Status` and so the hardcoded word was right for all of them.
    /// A rule exercised only where it is DERIVED is a rule nobody has watched run.
    let private classWorld () =
        let item =
            """{"status":{"name":"Ready"},"blockedBy":null,"class":null,"content":{"__typename":"Issue","number":301,"title":"ordinary title, class is in the body","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}}"""

        Fake.Recorder(fun (req: Request) ->
            match req.Method, req.Path.Trim '/' with
            | "GET", "rate_limit" -> ok """{"resources":{"graphql":{"remaining":4980,"limit":5000}}}"""
            | "POST", "graphql" ->
                match req.Body with
                | Query(document, _) ->
                    if document.Contains "projectItems" then
                        ok """{"data":{"repository":{"issue":{"projectItems":{"nodes":[{"id":"PVTI_301","project":{"number":12}}]}}}},"rateLimit":{"cost":1,"remaining":4977}}"""
                    elif document.Contains "updateProjectV2ItemFieldValue" then
                        ok """{"data":{"updateProjectV2ItemFieldValue":{"clientMutationId":null}}}"""
                    elif document.Contains "projectsV2" then
                        ok
                            """{"data":{"organization":{"projectsV2":{"nodes":[{"number":12,"title":"Coordination","id":"PVT_coord"}]}}},"rateLimit":{"cost":1,"remaining":4977}}"""
                    elif document.Contains "fields(first" then
                        // The `Class` field, with its three options — a single-select write resolves its
                        // value to an option id before it is attempted, so the write cannot even be tried
                        // against a project that does not declare it.
                        ok """{"data":{"organization":{"projectV2":{"fields":{"nodes":[{"id":"PVTSSF_status","name":"Status","dataType":"SINGLE_SELECT","options":[{"id":"opt_ready","name":"Ready"},{"id":"opt_done","name":"Done"}]},{"id":"PVTSSF_class","name":"Class","dataType":"SINGLE_SELECT","options":[{"id":"opt_defect","name":"defect"},{"id":"opt_hard","name":"hardening"},{"id":"opt_dec","name":"decision"}]},{"id":"PVTF_blocked","name":"Blocked by","dataType":"TEXT"}]}}}},"rateLimit":{"cost":1,"remaining":4977}}"""
                    elif document.Contains "items(first" then
                        ok $"""{{"data":{{"organization":{{"projectV2":{{"items":{{"pageInfo":{{"hasNextPage":false,"endCursor":null}},"nodes":[%s{item}]}}}}}}}},"rateLimit":{{"cost":1,"remaining":4977}}}}"""
                    else
                        Error(Errors.NotFound $"the fixture serves no answer for: %s{document}")
                | _ -> Error(Errors.NotFound "a graphql call with no document")
            // OPEN, so its body IS read — that is where the class is declared. A real touch-set too, or the
            // item would not be a candidate at all.
            | "GET", "repos/FS-GG/FS.GG.SDD/issues/301" ->
                ok """{"number":301,"body":"Paths: src/Real/**\n\nClass: defect"}"""
            | "GET", "repos/FS-GG/FS.GG.SDD/issues" -> ok "[]"
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

    /// Drive `reconcile --apply --json` AS A COMMAND LINE (.github#1541).
    ///
    /// **THIS USED TO SET `Apply`/`Render` ON A PARSED `Options` DIRECTLY, AND THAT WAS A REAL LIMIT, not
    /// a shortcut.** `.github#1524` fixed the handler while `Options.parse` still refused this exact pair
    /// (#1429's "--apply and --json are mutually exclusive"), so the contract it pinned was reachable only
    /// from inside — correct in the code and unreachable through the CLI. A companion test pinned that
    /// refusal so lifting it would be a conscious edit rather than an accident; .github#1541 made the edit
    /// and this is its other half. Everything below now asserts on the stream a caller actually gets:
    /// `runReconcileWith`'s `options` helper parses this argv for real and fails loudly if the guard ever
    /// comes back, so the tripwire survives the pin it replaced.
    let private runApplyJson (transport: Fake.Recorder) =
        runReconcile transport (reconcileArgs [ "--apply"; "--json" ])

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

        // ...and the operator remedy is not simply dropped. `scripts/fsgg-coord flush` is a REMEDY, not a
        // fact about the board, so it does not belong in the document — but nothing runs it on its own, so
        // it must not vanish either. stderr is where this CLI already puts diagnostics.
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
    let ``#1588 reconcile --apply names the FIELD it wrote, not the literal Status`` () =
        let code, out, _ = runReconcile (classWorld ()) (reconcileArgs [ "--apply" ])

        // THE ASSERTION THAT WAS MISSING. Both projections of one write — the finding table and the
        // apply receipt — must name `Class`, because that is the column `boardWrite` was handed. The
        // receipt used to interpolate the literal "Status" beside a value taken from `Kind.Write`, so the
        // two lines of one document described two different writes. That is the exact failure
        // `Client.write`'s own comment says it exists to prevent, and it does not get a pass for being
        // prose: a reader checking this receipt would go auditing a Status column nothing had touched.
        let nl = Environment.NewLine

        let expected =
            "applying (1 mechanical finding(s))" + nl
            + "  CLASS-PROJECTION-LAG     FS.GG.SDD#301            Class=defect" + nl
            + "judgement findings are report-only: scripts/fsgg-coord lint --repo FS.GG.SDD" + nl
            + "applied  FS.GG.SDD#301  Class=defect" + nl

        Assert.Equal(expected, out)
        Assert.Equal(0, code)

        // Stated separately as well, because the equality above would also pass if BOTH lines said
        // `Status` — it pins the bytes, and this pins the meaning.
        Assert.DoesNotContain("Status=defect", out)

    [<Fact>]
    let ``#1588 the --json write object agrees with the text receipt about the field`` () =
        // `runApplyJson` is now `--apply --json` on argv itself (.github#1541) — the same command line a
        // caller runs, not an internal state this fixture reaches around the parser.
        let code, out, _ = runApplyJson (classWorld ())

        // The machine projection was CORRECT the whole time the human one was wrong, which is what made
        // the defect survivable long enough to reach a live board: `--json` said `Class`, the text said
        // `Status`, and nothing compared them. This is that comparison.
        let rows = parsedArray out
        Assert.Single rows |> ignore
        let row = rows.[0]
        Assert.Equal("CLASS-PROJECTION-LAG", row.GetProperty("rule").GetString())
        Assert.Equal("Class", row.GetProperty("field").GetString())
        Assert.Equal("defect", row.GetProperty("value").GetString())
        Assert.Equal("Class=defect", row.GetProperty("remedy").GetString())
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

    /// The four environment variables `Identity.resolve` consults, cleared and restored. Clearing ALL of
    /// them is what makes "no worker id resolves" a property of the TEST rather than of whoever's machine
    /// is running it — under a Claude Code or opencode harness, `CLAUDE_CODE_SESSION_ID` alone would
    /// resolve an id and the branch under test would never be reached.
    let private withNoIdentity (body: unit -> 'a) : 'a =
        let names =
            [ "FSGG_WORKER"; "CLAUDE_CODE_SESSION_ID"; "OPENCODE_SESSION_ID"; "FSGG_AGENT_SESSION_ID" ]

        let saved = names |> List.map (fun n -> n, Environment.GetEnvironmentVariable n)

        try
            for n in names do
                Environment.SetEnvironmentVariable(n, null)

            body ()
        finally
            for (n, v) in saved do
                Environment.SetEnvironmentVariable(n, v)

    [<Fact>]
    let ``an apply phase that cannot START still emits a document, not an empty stream`` () =
        // The `--apply` phase refuses before it writes anything when no worker id resolves — there would be
        // nobody to attribute a board write to. A `--json` caller must still get something PARSEABLE: an
        // empty stream is not an answer, and it is indistinguishable from a crash.
        //
        // `not-attempted` is a distinct outcome for exactly this reason. Reporting these rows with no
        // outcome, or omitting them, would let "found these and deliberately tried none" read as either a
        // clean board or a completed run — the #266 confusion this verb exists to avoid.
        // A REAL COMMAND LINE, like every other apply-JSON leg here (.github#1541) — and note what is
        // absent from it. `reconcileArgs` splices `--worker heron-1f20`, which is precisely the identity
        // `withNoIdentity` exists to remove, so this argv is spelled out rather than reusing that helper.
        let code, out, _ =
            withNoIdentity (fun () ->
                runReconcile
                    (reconcileWorld [ 101; 102 ] Set.empty)
                    [ "reconcile"; "--repo"; "FS.GG.SDD"; "--apply"; "--json" ])

        let rows = parsedArray out
        Assert.Equal(2, List.length rows)

        for r in rows do
            Assert.Equal("not-attempted", str "outcome" r)
            // The reason is IN the row, not only on stderr.
            Assert.Equal(JsonValueKind.String, r.GetProperty("error").ValueKind)
            // ...and the write it did NOT make is still named, so a caller knows what is outstanding.
            Assert.Equal("Status", str "field" r)
            Assert.Equal("Done", str "value" r)

        Assert.NotEqual(0, code)

    [<Fact>]
    let ``a clean board still emits an empty array, not nothing`` () =
        let code, out, _ = runApplyJson (reconcileWorld [] Set.empty)

        Assert.Equal("[]", out.Trim())
        Assert.Empty(parsedArray out)
        Assert.Equal(0, code)

    // ---- .github#1525 — `take --json`'s EMPTY arm is a RECEIPT, not prose --------------------------
    //
    // THE DEFECT THESE PIN. `take` has no JSON renderer of its own: it delegates its success path to
    // `claim`, which honours `opts.Render`, so `take --json` LOOKS like it has a machine projection. The
    // one arm `take` owns — the empty queue — calls the shared `printChosen`, which never consults
    // `opts.Render` and writes the prose line `nothing schedulable right now.` to STDOUT. So the document
    // a driver parses is JSON or prose depending on the very fact the driver was asking about, and the
    // projection cannot describe its own outcome without the exit code held beside it. Every other
    // `--json` verb's can.
    //
    // WHY THEY DRIVE `Client.take` AND PARSE THE WHOLE OF STDOUT — the same argument #1517 and #1524 make
    // above: the renderer was never the bug. A test over `Render` alone would pass on the broken engine,
    // because the broken engine never CALLS a renderer here. The subject is the handler's dispatch, and
    // the assertion is that every byte on stdout is one document.
    //
    // WHAT IS DELIBERATELY NOT HERE — `next --json`. The issue as filed asks for it (AC 3), and that AC
    // was overtaken by `.github#1523` (PR #1550), which landed `Next -> TextOnly`: the flag is now REFUSED
    // at parse time, pinned by `OptionsTests`' ``#1523 --json is REFUSED on a command with no machine
    // projection``. Reaching AC 3 would mean reopening that classification and editing an options surface
    // this item does not declare. The residual `next` hazard — the same prose line on STDOUT, at exit 0,
    // from a verb whose stdout contract is a bare ref read with `$(…)` — was pinned below as the behaviour
    // of record and filed apart as `.github#1562`, which CLOSED it without giving `next` a machine
    // projection: the line moved to stderr, and stdout is now the ref or nothing at all. Those pins carry
    // the old assertion's history; see the `.github#1562` block below.

    /// Run a queue verb against a THROWAWAY cache root, capturing stdout and stderr APART. The split IS
    /// the assertion rather than a convenience: the skip reasons and #428's starved banner belong on
    /// stderr in BOTH projections (that is `batch --json`'s landed dialect), and only separate streams can
    /// show that the document on stdout is alone there.
    let private runQueue (transport: Fake.Recorder) (args: string list) : int * string * string =
        let dir = Path.Combine(Path.GetTempPath(), "fsgg-1525-" + Guid.NewGuid().ToString "n")
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

            let opts = options args

            let code =
                match opts.Command with
                | Options.Take -> Client.take (context transport) opts
                | Options.Next -> Client.next (context transport) opts
                // `batch` joined at .github#1562, and it is the CONTROL rather than a third subject: that
                // item moved `next`'s headline to stderr and claims `batch --text` still puts it on
                // stdout. Nothing in the repo could see that claim — every `batch` assertion captures the
                // streams MERGED — so the verb this fixture refused was the one the fix's blast radius
                // needed measuring on.
                | Options.BatchCmd -> Client.batch (context transport) opts
                | other -> failwithf "this fixture drives take/next/batch only, got %A" other

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

    /// A BUSY queue: one candidate the scheduler LOOKED AT and refused (#74), against a board with none at
    /// all. #428's distinction — this is the board that "nothing schedulable" misreports as an empty
    /// backlog.
    ///
    /// WHAT REFUSES #74, CORRECTED BY .github#1562. This read "startable but for the live claim
    /// `kite-469` holds on it", and that is not what happens: `boardItem`'s column is `In progress`, which
    /// `columnStartability` maps to `NeverStartable`, and the column is decided BEFORE any overlap. So #74
    /// is passed over at the COLUMN and `kite-469`'s marker is never reached. The property every leg here
    /// turns on — one candidate looked at and refused, so `passedOver` is 1 and the reasons name #74 —
    /// is unchanged and is what those legs assert; only the reason this docstring named was wrong.
    let private busyQueue () = disjointWorld ()

    /// A board with NO items at all. The other side of #428's distinction, and the reason the receipt
    /// carries a count rather than only a word.
    let private emptyQueue () = world Map.empty Map.empty false

    [<Theory>]
    [<InlineData(true, 1)>]
    [<InlineData(false, 0)>]
    let ``take --json emits ONE receipt on the empty-queue arm too`` (busy: bool, passedOver: int) =
        let code, out, err =
            runQueue
                (if busy then busyQueue () else emptyQueue ())
                [ "take"; "--repo"; "FS.GG.SDD"; "--worker"; "otter-9c21"; "--json" ]

        let receipt = parsed out

        // THE FIELD A CONSUMER KEYS ON. Not an absence, not a prose line, and not the exit code fetched
        // from somewhere else: the document says what happened.
        Assert.Equal("none", str "kind" receipt)

        // The identity keys of the claimed receipt, PRESENT and NULL. `null` is the modelled "there is no
        // item" (#437's habit), so one consumer reads one key set in both outcomes rather than branching
        // on which keys exist.
        Assert.Equal(JsonValueKind.Null, receipt.GetProperty("ref").ValueKind)
        Assert.Equal(JsonValueKind.Null, receipt.GetProperty("repo").ValueKind)
        Assert.Equal(JsonValueKind.Null, receipt.GetProperty("number").ValueKind)

        // WHO looked — the same worker id the claimed receipt carries.
        Assert.Equal("otter-9c21", str "worker" receipt)

        // #428's fact, in the machine form: HOW MANY candidates were looked at and refused. A board with
        // items the scheduler passed over is BUSY; a board with none is EMPTY, and the two want opposite
        // instructions (diagnose before idling vs. there is genuinely nothing here). The human form has
        // carried the distinction on stderr since #428 — as per-item reasons a machine must not scrape —
        // so the count is the smallest honest machine form of it, and it is a COUNT rather than a verdict
        // because the reasons themselves stay on stderr where `batch --json` already puts them.
        Assert.Equal(passedOver, receipt.GetProperty("passedOver").GetInt32())

        // EX_NONE, unchanged, in both projections (#585).
        Assert.Equal(5, code)

        // The prose headline is GONE FROM STDOUT. `parsed` already proved stdout is one document; this
        // says which document it stopped being.
        Assert.DoesNotContain("nothing schedulable", out)

        // A scope that named SOMETHING carries no advisory. `null`, not absent — see the misspelt-`--repo`
        // leg below for the fact this key exists to carry.
        Assert.Equal(JsonValueKind.Null, receipt.GetProperty("repoAdvisory").ValueKind)

        // The per-item skip reasons stay on STDERR, exactly where `batch --json` puts them. A busy queue
        // has one to give; an empty board has none.
        if busy then
            Assert.Contains("FS.GG.SDD#74", err)

    [<Fact>]
    let ``a misspelt --repo is IN the receipt, not only on stderr`` () =
        // THE ONE THIS CHANGE CREATES, AND THEREFORE OWES. #979 put the "that `--repo` named nothing"
        // advisory on stderr because the only reader was a human — `take` had no machine projection of
        // this outcome for it to ride in. It has one now, and a typo produces EXACTLY the document an
        // empty board produces: `kind:"none"`, `passedOver:0`. A driver reading that off `--repo
        // FS.GG.SDDD` would conclude the repo has no work and stop dispatching to a full one, which is
        // #979's harm arriving on the surface this item adds. So the advisory rides in the object.
        let code, out, err =
            runQueue (busyQueue ()) [ "take"; "--repo"; "NOPE"; "--worker"; "otter-9c21"; "--json" ]

        let receipt = parsed out

        Assert.Equal("none", str "kind" receipt)
        // Indistinguishable from an empty board on every OTHER key — which is the whole argument.
        Assert.Equal(0, receipt.GetProperty("passedOver").GetInt32())

        let advisory = str "repoAdvisory" receipt
        Assert.Contains("NOPE", advisory)

        // Still on stderr as well (#979's human form is untouched), and still EX_NONE.
        Assert.Contains("NOPE", err)
        Assert.Equal(5, code)

    [<Fact>]
    let ``without --json take is byte-identical to today`` () =
        let code, out, err =
            runQueue (busyQueue ()) [ "take"; "--repo"; "FS.GG.SDD"; "--worker"; "otter-9c21" ]

        // #1525 is an ADDITION to the machine projection and nothing else. `printChosen`'s line is the
        // headline #440 settled on, and `batch`'s Text arm prints it too, so it is pinned as an EQUALITY
        // over the whole stream rather than as a `Contains`.
        Assert.Equal("nothing schedulable right now." + Environment.NewLine, out)
        Assert.Equal(5, code)
        Assert.Contains("FS.GG.SDD#74", err)

    // ---- .github#1562 — `next`'s EMPTY arm keeps its hands off the `$(…)` ref contract ---------------
    //
    // WHAT WAS HERE, AND WHY IT IS EDITED RATHER THAN DELETED (AC 5). This block was one test named
    // ``next is UNCHANGED by 1525 — its empty arm still prints prose on stdout at exit 0``. It was not an
    // endorsement: #1525's AC 5 froze `take`/`next`'s un-flagged bytes, so #1525 could not be the item
    // that changed this, and pinning the defect made it a deliberate disposition rather than an oversight.
    // It is the record of what the behaviour WAS, so the assertions below inherit its subject and its
    // argument and invert the one line it pinned. Deleting it would delete the only place the org states
    // that `ref="$(fsgg-coord next --repo X)"` once yielded the STRING "nothing schedulable right now.".
    //
    // STDOUT AND STDERR APART, NOT MERGED — the property AC 4 is about, and the reason this stood. Every
    // assertion that could see this arm captured the streams TOGETHER (`tests/coord-engine-parity`'s
    // `ge()` helper is `2>&1`), and a merged capture cannot tell a machine contract from prose printed
    // beside it: the defect and the fix render identically through it. `runQueue` above returns the two
    // separately, so "stdout carries the ref and NOTHING else" is assertable here.

    [<Fact>]
    let ``#1562 next's empty arm puts NOTHING on stdout — the ref contract is not prose`` () =
        let code, out, err =
            runQueue (busyQueue ()) [ "next"; "--repo"; "FS.GG.SDD"; "--worker"; "otter-9c21" ]

        // AC 1, AS AN EQUALITY OVER THE WHOLE STREAM rather than a `DoesNotContain` of the old sentence.
        // The subject is not that one phrase moved; it is that `$(…)` captures an EMPTY ref, so ANY byte
        // arriving here later — a second headline, a courtesy line, a future banner — fails this.
        Assert.Equal("", out)

        // ...and the answer was not DROPPED to buy that. #440's headline and the OBSERVED per-item reason
        // (never a guessed list of causes) are both still emitted, one stream over.
        Assert.Contains("nothing schedulable right now.", err)
        Assert.Contains("FS.GG.SDD#74", err)

        // EXIT 0, and the disagreement it refuses to invent. `next` is `batch` capped at one; `batch`
        // answers this same board at 0, and #1535 made `batch --text -n 1` the documented substitute for
        // `next` when a caller wants the decision without the chore offer. `take`'s EX_NONE means "I
        // CLAIMED NOTHING" — a fact about a write `next` never attempts.
        Assert.Equal(0, code)

    [<Fact>]
    let ``#1562 next over a NON-empty queue is byte-identical: one line, the short ref, exit 0`` () =
        // AC 3, AND THE LEG THAT MAKES AC 1 A FIX RATHER THAN A MUTE BUTTON. An engine that printed
        // nothing on stdout ever would satisfy the assertion above perfectly. This is the arm the contract
        // exists for, held to the exact bytes a caller substitutes into `$(…)`.
        let startable =
            worldIn "Ready" (Map.ofList [ 74, "Paths: scripts/fsgg-coord" ]) Map.empty false

        let code, out, _ =
            runQueue startable [ "next"; "--repo"; "FS.GG.SDD"; "--worker"; "otter-9c21" ]

        Assert.Equal("FS.GG.SDD#74" + Environment.NewLine, out)
        Assert.Equal(0, code)

    [<Fact>]
    let ``#1562 batch --text KEEPS the headline on stdout — the blast radius, pinned`` () =
        // THE CLAIM THIS ITEM MAKES AND NOTHING COULD SEE. `next` and `batch --text` share the words
        // (`nothingSchedulable`) and the tail (`sayPassedOver`); they differ in ONE thing, the stream the
        // headline goes to, and .github#1562 asserts it moved for `next` ALONE. That "alone" was
        // unfalsifiable: `runQueue` refused `batch`, and `writes.sh`'s leg — the only other place
        // `batch --text`'s headline is checked — greps a `2>&1` merge, which is the exact blindness this
        // item was filed about, one verb over. A change that moved BOTH would have been green everywhere.
        //
        // AND IT IS NOT A DUPLICATE OF `without --json take is byte-identical to today`. That pins `take`,
        // whose empty arm also carries EX_NONE; this pins the verb `/pnext-item` prescribes as the READ
        // substitute for `next` (#1535), which is the one a worker actually redirects.
        let code, out, err =
            runQueue (busyQueue ()) [ "batch"; "--text"; "--repo"; "FS.GG.SDD"; "-n"; "1" ]

        Assert.Equal("nothing schedulable right now." + Environment.NewLine, out)
        Assert.Contains("FS.GG.SDD#74", err)
        Assert.Equal(0, code)

    [<Fact>]
    let ``the CLAIMED receipt shape is unchanged, byte for byte`` () =
        // AC 2. NOTHING pinned these bytes before .github#1525 — the shape `claim --json`/`take --json`
        // emit is a landed machine contract with live consumers (every `pnext-item` worker gates startup
        // on `.converged`), and the empty-arm receipt added beside it must not have drifted a key of it.
        // Byte equality, in key order, because a consumer keying on position — or a human diffing a
        // receipt log — sees the bytes and not the record.
        let receipt: Render.ClaimReceipt =
            { Ref =
                { Owner = "FS-GG"
                  Repo = ".github"
                  Number = 1525 }
              Worker = "snipe-6404"
              Kind = "claimed"
              MarkerObserved = true
              MarkerId = Some 5087533685L
              AssigneeObserved = None
              Status = Some "In progress"
              StatusRead = "observed"
              StatusWrite = "written"
              PendingBoardWrites = Some 0
              Converged = true }

        Assert.Equal(
            """{"ref":".github#1525","repo":"FS-GG/.github","number":1525,"worker":"snipe-6404","kind":"claimed","markerObserved":true,"markerId":5087533685,"assigneeObserved":null,"status":"In progress","statusRead":"observed","statusWrite":"written","pendingBoardWrites":0,"converged":true}""",
            Render.renderClaimReceiptJson receipt
        )
