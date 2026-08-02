namespace FS.GG.Coord.Cli.Tests

open System
open System.IO
open System.Text.Json
open Xunit
open FS.GG.Coord
open FS.GG.Coord.Types
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Transport
open FS.GG.Coord.Cli

module ApplicationServiceTests =

    [<Fact>]
    let ``#1843 filing advisory finds a broad same-repo declaration and ignores reverse or other repos`` () =
        let existing =
            Snapshot.parse
                """{"schema":"fsgg.coord.snapshot/1","allowBacklog":false,"items":[{"owner":"FS-GG","repo":"FS.GG.SDD","number":9,"status":"Ready","state":"OPEN","body":"Paths: docs/reports/new-file.md"}]}"""
            |> Result.defaultWith (fun errors -> failwithf "fixture snapshot did not parse: %A" errors)
            |> fun request -> request.Candidates |> List.map _.Item

        let broad = { Owner = "FS-GG"; Repo = "FS.GG.SDD"; Number = 10 }
        let narrow = { broad with Number = 11 }
        let otherRepo = { broad with Repo = "FS.GG.Game" }

        Assert.Equal<Ref list>([ existing.Head.Ref ], Client.filingLaneOfOne broad (TouchSet.parse "Paths: docs/reports") existing)
        Assert.Empty(Client.filingLaneOfOne narrow (TouchSet.parse "Paths: docs/reports/new-file.md") existing)
        Assert.Empty(Client.filingLaneOfOne otherRepo (TouchSet.parse "Paths: docs/reports") existing)

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
          PathRepo = repo
          BoardClass = None
          Severity = Unset
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
            """[{"number":1,"repo":"FS-GG/.github","title":"quote: \u0022kept\u0022","status":"Ready","class":null,"severity":"Unset","state":"OPEN"}]""",
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
            """[{"number":1,"repo":"FS-GG/.github","title":"a defect","status":"Ready","class":"defect","severity":"Unset","state":"OPEN"}]""",
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

    [<Fact>]
    let ``#1901 Unset Severity lints until an open row is triaged`` () =
        Assert.True(LintApplication.severityVerdict IssueState.Open BoardStatus.Ready Unset |> Option.isSome)
        Assert.True(LintApplication.severityVerdict IssueState.Open BoardStatus.InProgress Unset |> Option.isSome)
        Assert.True(LintApplication.severityVerdict IssueState.Open BoardStatus.Done Unset |> Option.isNone)
        Assert.True(LintApplication.severityVerdict IssueState.Closed BoardStatus.Ready Unset |> Option.isNone)

        for severity in [ Critical; High; Medium; Low ] do
            Assert.True(
                LintApplication.severityVerdict IssueState.Open BoardStatus.Ready severity
                |> Option.isNone
            )

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
    let private boardItemIn (status: string) (number: int) (title: string) (blockedBy: string option) (state: string) =
        let blocked = blockedBy |> Option.map (fun value -> $"{{\"text\":\"%s{value}\"}}") |> Option.defaultValue "null"
        $"""{{"status":{{"name":"%s{status}"}},"blockedBy":%s{blocked},"content":{{"__typename":"Issue","number":%d{number},"title":"%s{title}","state":"%s{state}","repository":{{"nameWithOwner":"FS-GG/FS.GG.SDD"}}}}}}"""

    let private boardItem (number: int) (title: string) = boardItemIn "In progress" number title None "OPEN"

    /// One claim marker, `ageMinutes` old. Sessionless, exactly as `kit_server.py` serves it: a marker
    /// carrying no session is indistinguishable from ours, which is `verifyHeld`'s documented behaviour and
    /// not a shortcut taken here.
    ///
    /// THE AGE IS A PARAMETER, AND IT IS BACKDATED RATHER THAN SLEPT FOR (.github#1779). `winner` applies
    /// the lease, so "a marker exists" and "a live claim reserves" are two different facts — and a fixture
    /// that can only build the first cannot tell a scan that reads the lease from one that returns `Some`
    /// for any marker at all. 0 is NOW, which is what every pre-#1779 caller means.
    let private commentsAgedScoped
        (holders: Map<int, string>)
        (ageMinutes: Map<int, int>)
        (pathRepos: Map<int, string>)
        (number: int)
        =
        let age = Map.tryFind number ageMinutes |> Option.defaultValue 0
        let ts = DateTime.UtcNow.AddMinutes(float -age).ToString "yyyy-MM-ddTHH:mm:ssZ"

        match Map.tryFind number holders with
        | None -> "[]"
        | Some worker ->
            let pathRepo =
                Map.tryFind number pathRepos
                |> Option.map (fun repo -> $" pathRepo=%s{repo}")
                |> Option.defaultValue ""

            $"""[{{"id":%d{8000 + number},"body":"<!-- fsgg:claim worker=%s{worker} lease=120%s{pathRepo} -->\nheld","user":{{"login":"EHotwagner"}},"created_at":"%s{ts}","updated_at":"%s{ts}"}}]"""

    let private commentsAged holders ageMinutes number =
        commentsAgedScoped holders ageMinutes Map.empty number

    let private commentsFor (holders: Map<int, string>) (number: int) = commentsAged holders Map.empty number

    let private ok (body: string) : Errors.IoResult<Response> =
        Ok
            { Status = 200
              Body = body
              ETag = None
              NextLink = None }

    /// A transport serving one repo AND one board. `bodies` is issue number → issue body (its `Paths:`
    /// declaration), `holders` is issue number → the worker whose claim marker sits on it, `markerAge` is
    /// issue number → how many minutes ago that marker was posted (absent = now), `offBoard` is the issues
    /// that exist in the REPO but have no row on the BOARD at all, and `sayFails` makes the courtesy-notice
    /// POST fail so the receipt's `notified:false` leg can be pinned.
    ///
    /// THE REPO AND THE BOARD ARE SEPARATE INPUTS, AND THAT SEPARATION IS THE #1779 FIXTURE. Before it,
    /// `bodies` was both — every issue the fixture knew about was necessarily on the board, so the state
    /// `claim` reports as `statusWrite:"not-on-board"` was unrepresentable, and a test could not have
    /// failed for the reason #1779 is about even in principle.
    let private worldOfWithScopesAndIncomplete
        (statusFor: int -> string)
        (bodies: Map<int, string>)
        (holders: Map<int, string>)
        (markerAge: Map<int, int>)
        (pathRepos: Map<int, string>)
        (offBoard: Set<int>)
        (incomplete: Set<int>)
        (sayFails: bool)
        =
        // THE ROWS ARE RENDERED PER REQUEST, not once at construction. `statusFor` is a function, and it is
        // a function so that a fixture can make a column CHANGE BETWEEN TWO BOARD READS — which is the whole
        // of .github#1740 cause 1, and is unrepresentable if the board answer is frozen when the world is
        // built. Every existing caller passes a constant through `worldIn` and is unaffected.
        let items () =
            bodies
            |> Map.toList
            |> List.filter (fun (n, _) -> not (offBoard.Contains n))
            |> List.map (fun (n, body) ->
                let blocker = body.Split('\n') |> Array.tryPick (fun line -> if line.StartsWith("Blocked by: ") then Some(line.Substring("Blocked by: ".Length)) else None)
                let state = if body.Contains("<!-- fixture:closed -->") then "CLOSED" else "OPEN"
                boardItemIn (statusFor n) n $"item %d{n}" blocker state)
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
            // THE REPO'S OPEN ISSUES (`Reads.openIssues`, #1525/.github#1779) — WITH THEIR BODIES.
            //
            // It served `[]` until #1779, which was true of the scheduler's use of it (this fixture's whole
            // board WAS the board, so there was nothing off it to sweep) and is a lie about the repo. The
            // #353 collision scan now keys its candidate set on this read instead of on the board's rows,
            // so an empty answer here would make every OVERLAP leg below pass or fail for a reason that has
            // nothing to do with claims. The bodies ride along exactly as `Reads.openIssues` promises they
            // do — one list read serving both the marker scan and the touch-set extraction.
            | "GET", "repos/FS-GG/FS.GG.SDD/issues" ->
                bodies
                |> Map.toList
                |> List.map (fun (n, body) -> {| number = n; state = "open"; body = body |})
                |> JsonSerializer.Serialize
                |> ok
            // A DIFFERENT REPO'S issue list, and it is an ERROR rather than an empty array on purpose
            // (.github#1779). `openIssues` is repo-scoped by construction now, so nothing may ask for
            // another repo's issues; serving `[]` would let a cross-repo read pass unnoticed, which is the
            // phantom-collision failure #353 removed. This makes that a test failure instead.
            | "GET", p when p.EndsWith "/issues" ->
                Error(Errors.NotFound $"the #353 scan asked for another repo's issues: %s{p}")
            | "GET", _ when (issueNumber "/comments").IsSome ->
                let n = (issueNumber "/comments").Value
                let readable = commentsAgedScoped holders markerAge pathRepos n

                let body =
                    if incomplete.Contains n then
                        let unreadable = $"""{{"id":%d{9000 + n},"body":null}}"""

                        if readable = "[]" then
                            $"[%s{unreadable}]"
                        else
                            readable.TrimEnd(']') + "," + unreadable + "]"
                    else
                        readable

                ok body
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
                    ok (JsonSerializer.Serialize {| number = n; state = "open"; body = body |})
                | None -> Error(Errors.NotFound $"no issue %d{n}")
            | m, p -> Error(Errors.NotFound $"the fixture serves no %s{m} %s{p}"))

    let private worldOfWithIncomplete statusFor bodies holders markerAge offBoard incomplete sayFails =
        worldOfWithScopesAndIncomplete
            statusFor
            bodies
            holders
            markerAge
            Map.empty
            offBoard
            incomplete
            sayFails

    let private worldOf statusFor bodies holders markerAge offBoard sayFails =
        worldOfWithIncomplete statusFor bodies holders markerAge offBoard Set.empty sayFails

    /// `worldOf` with no aged markers and nothing off the board — every pre-#1779 caller's world.
    let private worldWith (statusFor: int -> string) (bodies: Map<int, string>) (holders: Map<int, string>) (sayFails: bool) =
        worldOf statusFor bodies holders Map.empty Set.empty sayFails

    let private worldIn (status: string) (bodies: Map<int, string>) (holders: Map<int, string>) (sayFails: bool) =
        worldWith (fun _ -> status) bodies holders sayFails

    let private world (bodies: Map<int, string>) (holders: Map<int, string>) (sayFails: bool) =
        worldIn "In progress" bodies holders sayFails

    let private worldWithPathRepos bodies holders pathRepos =
        worldOfWithScopesAndIncomplete
            (fun _ -> "In progress")
            bodies
            holders
            Map.empty
            pathRepos
            Set.empty
            Set.empty
            false

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

    [<Fact>]
    let ``#2127 driver review evidence is bound to the PR comment endpoint`` () =
        // This drives the real `Client.driver` handler through scan, the PR liveness probe, the green
        // landability read and the PR conversation.  The backing issue deliberately has no review marker:
        // a handler that accidentally reads #2127's comments for review evidence therefore produces zero.
        let mutable head = "31cffe6-driver-head"
        let mutable claimed = false
        let boardItem =
            """{"status":{"name":"Ready"},"blockedBy":null,"content":{"__typename":"Issue","number":2127,"title":"driver","state":"OPEN","repository":{"nameWithOwner":"FS-GG/.github"}}}"""
        let graph (query: string) =
            if query.Contains "projectsV2" then
                """{"data":{"organization":{"projectsV2":{"nodes":[{"number":12,"title":"Coordination","id":"PVT_coord"}]}},"rateLimit":{"cost":1,"remaining":4977}}}"""
            elif query.Contains "fields(first" then
                """{"data":{"organization":{"projectV2":{"fields":{"nodes":[{"id":"PVTSSF_status","name":"Status","dataType":"SINGLE_SELECT","options":[{"id":"opt_ready","name":"Ready"}]}]}}},"rateLimit":{"cost":1,"remaining":4977}}}"""
            else
                $"""{{"data":{{"organization":{{"projectV2":{{"items":{{"pageInfo":{{"hasNextPage":false,"endCursor":null}},"nodes":[%s{boardItem}]}}}}}}}},"rateLimit":{{"cost":1,"remaining":4977}}}}"""
        let transport =
            Fake.Recorder(fun (req: Request) ->
                match req.Method, req.Path.Trim '/' with
                | "POST", "graphql" ->
                    match req.Body with
                    | Query(query, _) -> ok (graph query)
                    | _ -> Error(Errors.NotFound "graphql without a query")
                | "GET", "repos/FS-GG/.github/issues" -> ok "[]"
                | "GET", "repos/FS-GG/.github/issues/2127" -> ok """{"number":2127,"state":"open","body":"Paths: src/FS.GG.Coord.Core/Driver.fs"}"""
                | "GET", "repos/FS-GG/.github/issues/2127/comments" ->
                    if claimed then ok (commentsFor (Map.ofList [ 2127, "worker-2127" ]) 2127) else ok "[]"
                | "GET", "repos/FS-GG/.github/pulls" -> ok """[{"number":2140,"head":{"ref":"item/2127-driver-transition-state-machine"}}]"""
                | "GET", "repos/FS-GG/.github/pulls/2140" -> ok $"""{{"number":2140,"state":"open","merged":false,"mergeable":true,"mergeable_state":"clean","head":{{"ref":"item/2127-driver-transition-state-machine","sha":"%s{head}"}},"base":{{"ref":"main"}}}}"""
                | "GET", "repos/FS-GG/.github/actions/runs" -> ok """{"total_count":1,"workflow_runs":[{"path":".github/workflows/build.yml","event":"pull_request","head_branch":"item/2127-driver-transition-state-machine","run_number":1,"status":"completed","conclusion":"success","check_suite_id":1,"pull_requests":[{"number":2140}]}]}"""
                | "GET", path when path.StartsWith "repos/FS-GG/.github/commits/" && path.EndsWith "/check-runs" -> ok """{"total_count":1,"check_runs":[{"name":"build","check_suite":{"id":1},"status":"completed","conclusion":"success"}]}"""
                | "GET", "repos/FS-GG/.github/issues/2140/comments" ->
                    let initial = $"<!-- fsgg:independent-review:v1 -->\ncritic: shrike-7194\nreviewed-head: %s{head}\nverdict: pass\nroute-applicability: not-meaningful\nroute-not-meaningful-reason: driver planning has no runtime-route comparison subject"
                    let accepted = $"<!-- fsgg:review-accepted:v1 -->\naccepted-head: %s{head}\ninitial-review: https://reviews/1\nlatest-confirmation: https://reviews/1"
                    ok (JsonSerializer.Serialize [ {| id = 1; html_url = "https://reviews/1"; body = initial |}; {| id = 2; html_url = "https://reviews/2"; body = accepted |} ])
                | method', path -> Error(Errors.NotFound $"unexpected driver read: %s{method'} %s{path}"))
        let previousCache = Environment.GetEnvironmentVariable "FSGG_COORD_CACHE"
        let cache = Path.Combine(Path.GetTempPath(), "fsgg-2127-driver-" + Guid.NewGuid().ToString "n")
        let previousOut, previousErr = Console.Out, Console.Error
        let invoke receipt =
            use capturedOut = new StringWriter()
            use capturedErr = new StringWriter()
            Console.SetOut capturedOut
            Console.SetError capturedErr
            let args =
                [ yield "driver"; yield "--repo"; yield ".github"; yield "--json"; yield "--worker"; yield "host-2127"
                  match receipt with
                  | Some path -> yield "--snapshot"; yield path
                  | None -> () ]
            let code = Client.driver (context transport) (options args)
            Console.Out.Flush()
            Console.Error.Flush()
            Console.SetOut previousOut
            Console.SetError previousErr
            code, capturedOut.ToString(), capturedErr.ToString()
        try
            Directory.CreateDirectory cache |> ignore
            Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", cache)
            let code, stdout, stderr = invoke None
            if code <> 0 then failwithf "exit=%d; stderr=%s; log=%A" code stderr transport.Log
            Assert.Equal("", stderr)
            use document = JsonDocument.Parse(stdout)
            let root = document.RootElement
            let evidence = root.GetProperty("reviewEvidence").GetInt32()
            if evidence <> 1 then failwithf "evidence=%d; stdout=%s; log=%A" evidence stdout transport.Log
            Assert.False(root.GetProperty("receiptValid").GetBoolean())
            Assert.Equal("RepairEngineCurrency", root.GetProperty("action").GetString())
            let sourceSha = root.GetProperty("sourceSha").GetString()
            let receiptPath = Path.Combine(cache, "receipt.json")
            let receipt approved observedAt source =
                let observation kind outcome =
                    let id = Driver.observationReceiptId kind observedAt source outcome
                    $"""{{"kind":"%s{kind}","observedAt":%d{observedAt},"sourceSha":"%s{source}","outcome":"%s{outcome}","receiptId":"%s{id}"}}"""
                let observations =
                    [ "reconcile-dry-run", "clean"; "reconcile-apply", "applied-or-not-needed"
                      "reconcile-fresh", "clean"; "triage", "fresh"; "engine-currency", "current-scoped" ]
                    |> List.map (fun (kind, outcome) -> observation kind outcome)
                    |> String.concat ","
                $"""{{"observedAt":%d{observedAt},"sourceSha":"%s{source}","complete":true,"consolidationApproved":%s{if approved then "true" else "false"},"observations":[%s{observations}]}}"""
            let now = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            File.WriteAllText(receiptPath, receipt false now sourceSha)
            let _, consolidate, _ = invoke (Some receiptPath)
            use consolidateDoc = JsonDocument.Parse consolidate
            Assert.True(consolidateDoc.RootElement.GetProperty("receiptValid").GetBoolean())
            Assert.Equal("Consolidate", consolidateDoc.RootElement.GetProperty("action").GetString())
            File.WriteAllText(receiptPath, receipt true now sourceSha)
            let _, dispatch, _ = invoke (Some receiptPath)
            use dispatchDoc = JsonDocument.Parse dispatch
            Assert.Equal("DispatchWave 3", dispatchDoc.RootElement.GetProperty("action").GetString())
            let queued: Cache.Deferred =
                { Ref = ".github#2127"; Field = "Status"; Value = "Ready"; At = "2026-08-02T00:00:00Z"
                  Worker = "host-2127"; Board = Some("FS-GG", "Coordination") }
            Cache.defer (Errors.RateLimited(Errors.GraphQlBudget, None)) queued
            |> Result.defaultWith (fun error -> failwithf "could not build pending-write provenance: %A" error)
            let _, pendingChanged, _ = invoke (Some receiptPath)
            use pendingChangedDoc = JsonDocument.Parse pendingChanged
            Assert.False(pendingChangedDoc.RootElement.GetProperty("receiptValid").GetBoolean())
            let pendingSource = pendingChangedDoc.RootElement.GetProperty("sourceSha").GetString()
            Assert.True(sourceSha <> pendingSource)
            Cache.clearPending ()
            // Same item count, different live facts: a claim makes the old receipt unreplayable.  A fresh
            // content-addressed chain over the claimed state then reaches the derived worker-return arm.
            claimed <- true
            let _, changed, _ = invoke (Some receiptPath)
            use changedDoc = JsonDocument.Parse changed
            Assert.False(changedDoc.RootElement.GetProperty("receiptValid").GetBoolean())
            let claimedSource = changedDoc.RootElement.GetProperty("sourceSha").GetString()
            File.WriteAllText(receiptPath, receipt true now claimedSource)
            let _, resume, _ = invoke (Some receiptPath)
            use resumeDoc = JsonDocument.Parse resume
            Assert.Equal("ResumeSameWorker", resumeDoc.RootElement.GetProperty("action").GetString())
            claimed <- false
            File.WriteAllText(receiptPath, receipt true (now - 301L) sourceSha)
            let _, stale, _ = invoke (Some receiptPath)
            use staleDoc = JsonDocument.Parse stale
            Assert.False(staleDoc.RootElement.GetProperty("receiptValid").GetBoolean())
            Assert.Equal("RepairEngineCurrency", staleDoc.RootElement.GetProperty("action").GetString())
            File.WriteAllText(receiptPath, receipt true now "wrong-snapshot")
            let _, mismatched, _ = invoke (Some receiptPath)
            use mismatchedDoc = JsonDocument.Parse mismatched
            Assert.False(mismatchedDoc.RootElement.GetProperty("receiptValid").GetBoolean())
            Assert.Equal("RepairEngineCurrency", mismatchedDoc.RootElement.GetProperty("action").GetString())
            let malformed = (receipt true now sourceSha).Replace("\"receiptId\":\"", "\"receiptId\":\"malformed-")
            File.WriteAllText(receiptPath, malformed)
            let _, malformedOutput, _ = invoke (Some receiptPath)
            use malformedDoc = JsonDocument.Parse malformedOutput
            Assert.False(malformedDoc.RootElement.GetProperty("receiptValid").GetBoolean())
            Assert.True(transport.Logged "comment-list FS-GG/.github 2140", $"log: %A{transport.Log}")
        finally
            Console.SetOut previousOut
            Console.SetError previousErr
            Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", previousCache)
            try Directory.Delete(cache, true) with _ -> ()

    [<Fact>]
    let ``followup audit apply disposes an abandoned queue's ref even when another worker holds the open issue`` () =
        let transport = world (Map.ofList [ 42, "Paths: src/A" ]) (Map.ofList [ 42, "other-123" ]) false
        let cache = Path.Combine(Path.GetTempPath(), "fsgg-followup-audit-" + Guid.NewGuid().ToString "n")
        let previous = Environment.GetEnvironmentVariable "FSGG_COORD_CACHE"
        let stderr = Console.Error
        use captured = new StringWriter()

        try
            let directory = Path.Combine(cache, "followups")
            Directory.CreateDirectory directory |> ignore
            let queue = Path.Combine(directory, "ghost-777.txt")
            File.WriteAllText(queue, "FS-GG/FS.GG.SDD#42\n")
            File.SetLastWriteTimeUtc(queue, DateTime.UtcNow.AddHours -3)
            Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", cache)
            Console.SetError captured

            let code =
                Client.withFollowupAuditContextForTest (context transport) (fun () ->
                    Client.followupAudit (options [ "followup"; "audit"; "--apply" ]))

            Assert.Equal(0, code)
            Assert.False(File.Exists queue, "the claimed open ref is already resurfaced; its abandoned queue must clear")
            Assert.Contains("FS.GG.SDD#42", captured.ToString())
            Assert.True(transport.Logged "comment-post FS-GG/FS.GG.SDD 42")
        finally
            Console.SetError stderr
            Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", previous)
            try Directory.Delete(cache, true) with _ -> ()

    [<Fact>]
    let ``followup audit apply leaves the queue byte-identical when its durable disposition fails`` () =
        let transport = world (Map.ofList [ 42, "Paths: src/A" ]) (Map.ofList [ 42, "other-123" ]) true
        let cache = Path.Combine(Path.GetTempPath(), "fsgg-followup-audit-fail-" + Guid.NewGuid().ToString "n")
        let previous = Environment.GetEnvironmentVariable "FSGG_COORD_CACHE"

        try
            let directory = Path.Combine(cache, "followups")
            Directory.CreateDirectory directory |> ignore
            let queue = Path.Combine(directory, "ghost-777.txt")
            let bytes = "FS-GG/FS.GG.SDD#42\n"
            File.WriteAllText(queue, bytes)
            File.SetLastWriteTimeUtc(queue, DateTime.UtcNow.AddHours -3)
            Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", cache)

            let code =
                Client.withFollowupAuditContextForTest (context transport) (fun () ->
                    Client.followupAudit (options [ "followup"; "audit"; "--apply" ]))

            Assert.Equal(3, code)
            Assert.Equal(bytes, File.ReadAllText queue)
        finally
            Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", previous)
            try Directory.Delete(cache, true) with _ -> ()

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
                | Options.Reap -> Client.reap (context transport) opts
                | Options.Claim -> Client.claim (context transport) opts
                | Options.Adopt -> Client.adopt (context transport) opts
                | Options.Heartbeat -> Client.heartbeat (context transport) opts
                // `batch` IS THE OTHER SURFACE, AND IT IS DRIVEN FROM THE SAME WORLD ON PURPOSE
                // (.github#1792). The defect that item is about is two components answering "who has
                // reserved this file" differently, so a fixture that could only reach ONE of them could
                // not have failed for the reason under test — it could only ever re-assert whichever
                // answer it was already able to see. `Client.batch` runs the real `Scan.snapshot`, so
                // this dispatch is what lets one board, one marker and one second be put to both.
                | Options.BatchCmd -> Client.batch (context transport) opts
                | other -> failwithf "this fixture does not drive %A" other

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

    /// `run`, plus the STDERR the command wrote (.github#1740 AC5).
    ///
    /// The OVERLAP diagnostics are `eprint`, and deliberately so — stdout is the machine contract and the
    /// collision detail belongs in the JSON object (#1517). But that put the AC5 sentence, whose whole
    /// defect was that it asserted causation, on the one stream no leg here could read. This captures it
    /// WITHOUT moving a byte of either projection: `run` touches `Console.Out` only, so wrapping
    /// `Console.Error` around it composes.
    let private runCapturingStderr (transport: Fake.Recorder) (args: string list) : int * string * string =
        let stderr = Console.Error
        use capturedErr = new StringWriter()

        try
            Console.SetError capturedErr
            let code, out = run transport args
            Console.Error.Flush()
            code, out, capturedErr.ToString()
        finally
            Console.SetError stderr

    /// Drive the claim-time advisory against a tiny authored roster.  The lock alone intentionally does
    /// not say whether a source is a skill directory or a plain client file, so this fixture includes both
    /// inputs the real engine has and captures the stderr-only operator guidance.
    let private declaredKitWarning (body: string) : string =
        let root = Path.Combine(Path.GetTempPath(), "fsgg-1878-" + Guid.NewGuid().ToString "n")
        let previousRoot = Environment.GetEnvironmentVariable "FSGG_KIT_ROOT"
        let stderr = Console.Error
        use captured = new StringWriter()

        try
            Directory.CreateDirectory(Path.Combine(root, "registry")) |> ignore
            File.WriteAllText(
                Path.Combine(root, "registry", "repos.lock"),
                "aaaa  .claude/skills/pnext-item\nbbbb  scripts/fsgg-coord\n"
            )
            File.WriteAllText(
                Path.Combine(root, "registry", "repos.yml"),
                "kit:\n  - { id: pnext-item, kind: skill, source: .claude/skills/pnext-item }\n  - { id: fsgg-coord, kind: client, source: scripts/fsgg-coord }\n"
            )
            Environment.SetEnvironmentVariable("FSGG_KIT_ROOT", root)
            Console.SetError captured

            KitDigest.declaredWarn
                (world (Map.ofList [ 74, body ]) Map.empty false)
                { Owner = "FS-GG"
                  Repo = "FS.GG.SDD"
                  Number = 74 }

            Console.Error.Flush()
            captured.ToString()
        finally
            Console.SetError stderr
            Environment.SetEnvironmentVariable("FSGG_KIT_ROOT", previousRoot)

            try
                Directory.Delete(root, true)
            with _ ->
                ()

    [<Fact>]
    let ``#1878 claim advice separates a skill SKILL.md digest from other packed skill files`` () =
        let referenceOnly = declaredKitWarning "Paths: .claude/skills/pnext-item/references/command-contracts.md"
        let skillMd = declaredKitWarning "Paths: .claude/skills/pnext-item/SKILL.md"
        let client = declaredKitWarning "Paths: scripts/fsgg-coord"

        Assert.Contains("do NOT change `registry/repos.lock`", referenceOnly)
        Assert.Contains("kit-published-coherence", referenceOnly)
        Assert.DoesNotContain("scripts/repos.sh relock", referenceOnly)

        Assert.Contains(".claude/skills/pnext-item/SKILL.md", skillMd)
        Assert.Contains("scripts/repos.sh relock", skillMd)
        Assert.DoesNotContain("do NOT change `registry/repos.lock`", skillMd)

        Assert.Contains("scripts/fsgg-coord", client)
        Assert.Contains("scripts/repos.sh relock", client)

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
    let ``#2104 both --paths verbs preserve every newline-separated path in one argv token`` (verb: string) =
        let code, out =
            run
                (disjointWorld ())
                [ verb
                  "FS.GG.SDD#74"
                  "--worker"
                  "kite-469"
                  "--json"
                  "--paths"
                  "src/First.fs\nsrc/Second.fs\nsrc/Third.fs" ]

        let receipt = parsed out

        let expected =
            if verb = "widen" then
                [ "scripts/fsgg-coord"; "src/First.fs"; "src/Second.fs"; "src/Third.fs" ]
            else
                [ "src/First.fs"; "src/Second.fs"; "src/Third.fs" ]

        Assert.Equal<string list>(expected, strings "paths" receipt)
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

    [<Fact>]
    let ``#1732 active claims with equal tokens in different marker path scopes are disjoint`` () =
        let bodies =
            Map.ofList [ 74, "Paths: scripts/fsgg-coord"; 75, "Paths: src/Shared.fs" ]

        let holders = Map.ofList [ 74, "kite-469"; 75, "otter-9c21" ]

        let world =
            worldWithPathRepos
                bodies
                holders
                (Map.ofList [ 74, "FS.GG.Audio"; 75, "FS.GG.Rendering" ])

        let code, out =
            run world [ "widen"; "FS.GG.SDD#74"; "--worker"; "kite-469"; "--json"; "--paths"; "src/Shared.fs" ]

        Assert.Equal(0, code)
        Assert.Equal("disjoint", str "verdict" (parsed out))
        Assert.Equal(0, world.GraphQlCalls)

    [<Fact>]
    let ``#1732 active claims with equal tokens in one marker path scope still collide`` () =
        let bodies =
            Map.ofList [ 74, "Paths: scripts/fsgg-coord"; 75, "Paths: src/Shared.fs" ]

        let holders = Map.ofList [ 74, "kite-469"; 75, "otter-9c21" ]

        let world =
            worldWithPathRepos
                bodies
                holders
                (Map.ofList [ 74, "FS.GG.Rendering"; 75, "FS.GG.Rendering" ])

        let code, out =
            run world [ "widen"; "FS.GG.SDD#74"; "--worker"; "kite-469"; "--json"; "--paths"; "src/Shared.fs" ]

        Assert.Equal(6, code)
        Assert.Equal("overlap", str "verdict" (parsed out))
        Assert.Equal(0, world.GraphQlCalls)

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

    // ---- .github#1896 — CLIENT CALLERS REFUSE INCOMPLETE LOCK READS -------------------------------

    let private incompleteWorld bodies holders ages incomplete =
        worldOfWithIncomplete
            (fun _ -> "In progress")
            bodies
            holders
            ages
            Set.empty
            incomplete
            false

    [<Fact>]
    let ``#1896 reap refuses a readable stale marker beside an unclassifiable comment`` () =
        let transport =
            incompleteWorld
                (Map.ofList [ 74, "Paths: scripts/fsgg-coord" ])
                (Map.ofList [ 74, "ghost-222" ])
                (Map.ofList [ 74, 180 ])
                (Set.ofList [ 74 ])

        let code, _, err = runCapturingStderr transport [ "reap"; "--repo"; "FS.GG.SDD" ]

        Assert.Equal(1, code)
        Assert.Contains("claim-marker scan is incomplete", err)
        Assert.False(transport.Logged "comment-delete", "reap deleted from a lower-bound marker list")

    [<Fact>]
    let ``#1896 claim's one-item guard refuses an incomplete read of another in-flight item`` () =
        let transport =
            incompleteWorld
                (Map.ofList [ 74, "Paths: src/Target.fs"; 75, "Paths: src/Other.fs" ])
                (Map.ofList [ 75, "kite-469" ])
                Map.empty
                (Set.ofList [ 75 ])

        let code, _, err =
            runCapturingStderr
                transport
                [ "claim"; "FS.GG.SDD#74"; "--worker"; "kite-469"; "--json" ]

        Assert.Equal(1, code)
        Assert.Contains("claim-marker scan is incomplete", err)
        Assert.False(transport.Logged "comment-post FS-GG/FS.GG.SDD 74", "claim reached its CAS after an incomplete guard read")

    [<Fact>]
    let ``#1896 adopt refuses an incomplete orphan read instead of choosing the readable marker`` () =
        let transport =
            incompleteWorld
                (Map.ofList [ 74, "Paths: src/Target.fs" ])
                (Map.ofList [ 74, "ghost-222" ])
                (Map.ofList [ 74, 180 ])
                (Set.ofList [ 74 ])

        let code, _, err =
            runCapturingStderr transport [ "adopt"; "FS.GG.SDD#74"; "--worker"; "kite-469" ]

        Assert.Equal(1, code)
        Assert.Contains("claim-marker scan is incomplete", err)

    [<Fact>]
    let ``#1896 touch-set collision scan refuses an incomplete neighbour lock`` () =
        let transport =
            incompleteWorld
                (Map.ofList [ 74, "Paths: scripts/fsgg-coord"; 75, "Paths: src/Shared.fs" ])
                (Map.ofList [ 74, "kite-469"; 75, "otter-9c21" ])
                Map.empty
                (Set.ofList [ 75 ])

        let code, _, err =
            runCapturingStderr
                transport
                [ "widen"; "FS.GG.SDD#74"; "--worker"; "kite-469"; "--paths"; "src/Shared.fs" ]

        Assert.Equal(1, code)
        Assert.Contains("claim-marker scan is incomplete", err)

    // ---- .github#1740 / .github#1779 — THE CANDIDATE SET IS THE LOCK, NEVER A PROJECTION OF IT ---------
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
    // FOUR WAYS THE COLUMN CAN DISAGREE WITH THE LOCK, AND #1740 CLOSED TWO OF THEM. `claim`'s receipt
    // names them all — `statusWrite: written | deferred | failed | not-on-board` — and it exits GREEN on
    // every one, because `converged:false` is a report and not a refusal. #1740 reached the stale-read case
    // with a cache tier and the DEFERRED case by reading the deferral queue. A permanently FAILED write is
    // never queued (#510 — a write replayed forever is a promise nobody can keep) and a `not-on-board`
    // claim has no row to select at all, so neither was reachable from the board's rows no matter what the
    // queue said.
    //
    // #1779 REPLACES THE CANDIDATE SET RATHER THAN ADDING A THIRD PATCH TO IT. `Reads.openIssues` lists the
    // repo's open issues WITH their bodies, the tokens are compared purely, and a marker is read only for a
    // row whose tokens actually collide. The column is never consulted, so all four rows are closed by ONE
    // mechanism. **#1740's two legs are kept verbatim below for exactly that reason**: they now assert
    // SUBSUMPTION, and if the replacement ever stopped covering a case the old patches covered, they are
    // what says so.

    /// #74 is ours. #75 holds a live claim on `src/Shared.fs` THROUGHOUT — only its board COLUMN moves.
    let private laggingBodies =
        Map.ofList [ 74, "Paths: scripts/fsgg-coord"; 75, "Paths: src/Shared.fs" ]

    let private laggingHolders = Map.ofList [ 74, "kite-469"; 75, "otter-9c21" ]

    let private cacheDir () =
        Path.Combine(Path.GetTempPath(), "fsgg-1740-" + Guid.NewGuid().ToString "n")

    let private widenOnto (paths: string) =
        [ "widen"; "FS.GG.SDD#74"; "--worker"; "kite-469"; "--json"; "--paths"; paths ]

    /// The single collision in a receipt that must have exactly one.
    let private soleCollision (out: string) =
        let receipt = parsed out
        Assert.Equal("overlap", str "verdict" receipt)
        Assert.Single(receipt.GetProperty("collisions").EnumerateArray() |> List.ofSeq)

    [<Fact>]
    let ``#1740 cause 1: a claim landing inside the scan-cache window collides, and no board read decides it`` () =
        // THE NAME CHANGED WITH THE MECHANISM, AND IT HAD TO. As #1740 wrote it, this leg drove the column
        // through a stale cache window and asserted the cache tier fixed it. Since #1779 nothing on this
        // path reads a board, so the `column` ref below is a DEAD INPUT — the assertion would pass
        // identically with it frozen. A test whose name promises a cache-tier check it can no longer make is
        // the #266 shape in a test file, so it says what it now proves: this state collides, and it does so
        // WITHOUT a board read (`world.GraphQlCalls = 0`, asserted at the end — which is the one thing here
        // that a re-introduced board scan would break, and the reason to keep the leg rather than delete it).
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

            // 3. ...but the cached scan is only seconds old. On `Cache.Scheduling` this second command was
            //    served that cached `Ready`, #75 never became a candidate, its marker was never read, and
            //    the worker was told it may edit `src/Shared.fs`. THAT was the false DISJOINT. Since #1779
            //    no board read decides this at all, so no cache tier can serve it a stale answer — this leg
            //    now proves the replacement covers what the tier covered.
            let code, out = runIn dir world (widenOnto "src/Shared.fs")

            let collision = soleCollision out
            Assert.Equal("FS.GG.SDD#75", str "ref" collision)
            Assert.Equal("otter-9c21", str "worker" collision)
            Assert.Equal<string list>([ "src/Shared.fs" ], strings "sharedTokens" collision)
            Assert.Equal(6, code)

            // TWO commands ran against this world and NEITHER asked GraphQL anything. That is what makes
            // the cache window irrelevant rather than merely survived, and it is the assertion that fails
            // if a board read ever returns to this path — where the verdict assertions above would not.
            Assert.Equal(0, world.GraphQlCalls)
        finally
            try
                Directory.Delete(dir, true)
            with _ ->
                ()

    /// One line of the deferral queue, in `Cache.renderDeferred`'s own shape.
    ///
    /// `boardTitle = ""` OMITS the board keys entirely rather than writing them empty — that is a pre-#882
    /// legacy entry, and `renderDeferred` omits rather than nulls for exactly this reason, so a fixture that
    /// wrote `""` would be testing a shape the queue never produces.
    let private queuedWrite (issueRef: string) (boardTitle: string) (field: string) (value: string) =
        let common =
            [ "ref", box issueRef
              "field", box field
              "value", box value
              "at", box (DateTimeOffset.UtcNow.ToString "o")
              "worker", box "otter-9c21" ]

        let board =
            if boardTitle = "" then
                []
            else
                [ "boardOwner", box "FS-GG"; "boardTitle", box boardTitle ]

        JsonSerializer.Serialize(dict (common @ board)) + "\n"

    let private queuedStatusWrite (boardTitle: string) (field: string) (value: string) =
        queuedWrite "FS-GG/FS.GG.SDD#75" boardTitle field value

    /// The AC3 fixture, LITERALLY: the board says `Ready` and it is not lying — the write has not happened.
    ///
    /// `queued` is the queue this run finds; `offBoard` is whether #75 has a board row at all.
    let private runOn (queued: string option) (offBoard: Set<int>) (holders: Map<int, string>) (age: Map<int, int>) (bodies: Map<int, string>) =
        let dir = cacheDir ()

        try
            Directory.CreateDirectory dir |> ignore

            match queued with
            | Some line -> File.WriteAllText(Path.Combine(dir, "pending.jsonl"), line)
            | None -> ()

            let world = worldOf (fun _ -> "Ready") bodies holders age offBoard false
            let code, out = runIn dir world (widenOnto "src/Shared.fs")
            code, out, world
        finally
            try
                Directory.Delete(dir, true)
            with _ ->
                ()

    /// The pre-#1779 default world: #75 on the board at `Ready`, live claim, colliding declaration.
    let private runWithQueue (queued: string option) =
        let code, out, _ = runOn queued Set.empty laggingHolders Map.empty laggingBodies
        code, out

    [<Fact>]
    let ``#1740 cause 2: a live claim whose Status write is still QUEUED collides from a Ready column`` () =
        let code, out = runWithQueue (Some(queuedStatusWrite "Coordination" "Status" "In progress"))

        let collision = soleCollision out
        Assert.Equal("FS.GG.SDD#75", str "ref" collision)
        Assert.Equal("otter-9c21", str "worker" collision)
        Assert.Equal(6, code)

    // ---- .github#1779 — THE TWO STATES #1740 DECLINED, EACH BUILT DELIBERATELY -------------------------

    [<Fact>]
    let ``#1779: a live claim whose Status write FAILED PERMANENTLY collides from a Ready column`` () =
        // THE STATE, STATED EXACTLY. #75's board row says `Ready` and is not lying: the `Status` write was
        // ATTEMPTED and it FAILED, permanently. #510 does not queue a permanent failure, so — unlike the
        // deferred case above — the queue is EMPTY and always will be, and nothing will ever write that
        // column. The marker is live and the touch-set is reserved for as long as its holder works.
        //
        // THIS EXACT FIXTURE ASSERTED `DISJOINT` BEFORE #1779, as one row of #1740's negative theory (the
        // `("", "", "", "")` case — "no queue at all: the honest DISJOINT this verb must still be able to
        // give"). It was not honest. It was the permanently-failed write, spelled as a control, and the
        // false verdict it pinned is the defect #1779 was filed to remove. That inversion is the whole
        // finding, and it is why the negative controls below vary the MARKER and the TOKENS instead of the
        // queue: only those two decide a reservation now.
        let code, out = runWithQueue None

        let collision = soleCollision out
        Assert.Equal("FS.GG.SDD#75", str "ref" collision)
        Assert.Equal("otter-9c21", str "worker" collision)
        Assert.Equal<string list>([ "src/Shared.fs" ], strings "sharedTokens" collision)
        Assert.Equal(6, code)

    [<Fact>]
    let ``#1779: a live claim on an item that is NOT ON THE BOARD AT ALL collides`` () =
        // `claim` reports `statusWrite:"not-on-board"` and exits GREEN. There is no row, so no row-derived
        // candidate set can select it — not by being fresher, and not by reading the deferral queue, which
        // is why #1740's queue leg structurally could not reach this one. `Reads.openIssues` is keyed on
        // the REPO, so it does.
        //
        // Measured live on the Coordination board, 2026-07-28, on this issue itself: `shrike-41c7` claimed
        // `.github#1779` (`statusWrite:"not-on-board"`, `converged:false`, declaring
        // `src/FS.GG.Coord.Cli/Client.fs`); `overlap .github#1688 --active` — #1688 declares the same file —
        // printed `DISJOINT` on `main` and `OVERLAP … held by shrike-41c7` on this branch, same board, same
        // second.
        let code, out, _ = runOn None (Set.ofList [ 75 ]) laggingHolders Map.empty laggingBodies

        let collision = soleCollision out
        Assert.Equal("FS.GG.SDD#75", str "ref" collision)
        Assert.Equal("otter-9c21", str "worker" collision)
        Assert.Equal(6, code)

    // THE NEGATIVE CONTROLS, AND EACH KILLS A DIFFERENT MUTANT. Without them "see every open issue" is
    // satisfied by `activeCollisions _ _ _ _ = every other open issue`, and both positive legs above would
    // still pass. Each varies ONE thing against the `#1779 not-on-board` leg, and each must be DISJOINT.
    //
    // These four are what carry that weight. The `queue` theory further down does NOT — see its own note.

    [<Fact>]
    let ``#1779 control: colliding TOKENS with no claim marker reserve NOTHING`` () =
        // The whole board is `Ready`, #75 declares exactly what we are widening onto — and NOBODY holds it.
        // An unclaimed issue that names the same files is work nobody is doing; reporting it would stop a
        // worker who has nothing to stop for. KILLS: dropping the marker read and colliding on tokens alone.
        let code, out, _ = runOn None Set.empty (Map.ofList [ 74, "kite-469" ]) Map.empty laggingBodies

        Assert.Equal("disjoint", str "verdict" (parsed out))
        Assert.Equal(0, code)

    [<Fact>]
    let ``#1792 (was a #1779 control): a marker whose LEASE HAS LAPSED still RESERVES`` () =
        // INVERTED BY .github#1792, NOT DELETED — this leg is the defect, written down as an assertion.
        //
        // As #1779 wrote it this asserted `DISJOINT`, with the note "`Reads.winner` applies the lease;
        // `Reads.reserver` does not. KILLS: swapping `winner` for `reserver`". That was an accurate
        // description of `activeCollisions` and the wrong answer: the SCHEDULER (`Scan.snapshot`) read the
        // same marker through `reserver` and called the file reserved, so the two surfaces disagreed and
        // "is this file taken?" depended on which verb you asked. #1792 settled it in `reserver`'s favour
        // at both sites — a lease is a clock, a lock is broken only by `reap` (#461/#581) — so the expected
        // verdict flips here. The leg is kept, and kept named, because a test that pinned the defect as
        // correct behaviour is exactly what the next reader needs to see flipped rather than vanished.
        //
        // Same colliding declaration, same holder; the marker is 240 minutes old against the 120-minute
        // default lease, BACKDATED rather than slept for. KILLS: reverting this call site to `winner`.
        let code, out, _ =
            runOn None Set.empty laggingHolders (Map.ofList [ 75, 240 ]) laggingBodies

        let collision = soleCollision out
        Assert.Equal("FS.GG.SDD#75", str "ref" collision)
        Assert.Equal("otter-9c21", str "worker" collision)
        Assert.Equal(6, code)

    [<Fact>]
    let ``#1792 control: NO marker at all still reserves nothing, lapsed or otherwise`` () =
        // THE LEG THAT KEEPS THE ONE ABOVE HONEST. `reserver` falls back to the lowest-id marker when no
        // marker is live, so the flip above is one step away from "any comment reserves" — and #1779's
        // token-only control uses a live-marker world, so it cannot see that step. Here #75 has NO marker
        // and the age map is set anyway, so the ONLY difference from the leg above is whether a marker
        // exists at all. KILLS: `reserver _ markers = List.tryHead markers` degrading into "the comments
        // array is non-empty", which is #461 inverted — the failure the marker read exists to prevent.
        let code, out, _ =
            runOn None Set.empty (Map.ofList [ 74, "kite-469" ]) (Map.ofList [ 75, 240 ]) laggingBodies

        Assert.Equal("disjoint", str "verdict" (parsed out))
        Assert.Equal(0, code)

    [<Fact>]
    let ``#1779 control: a live claim whose tokens do NOT collide reserves nothing`` () =
        // #75 is held, live, off the board and in a `Ready` column — every condition of the positive legs —
        // and it declares a file we are not asking for. KILLS: `TouchSet.conflicts _ _ = [ … ]`, i.e. a
        // token filter that is a constant. Without this leg both positive legs pass with no filter at all.
        let bodies = Map.ofList [ 74, "Paths: scripts/fsgg-coord"; 75, "Paths: docs/elsewhere.md" ]
        let code, out, _ = runOn None (Set.ofList [ 75 ]) laggingHolders Map.empty bodies

        Assert.Equal("disjoint", str "verdict" (parsed out))
        Assert.Equal(0, code)

    [<Fact>]
    let ``#1779 control: the item does not collide with ITSELF`` () =
        // #74 is the item being widened, it is HELD (by us), and after the widen it declares `src/Shared.fs`
        // — so a scan that did not exclude the subject would report the caller against their own claim on
        // every single widen. #75 is absent from this world entirely, so the only candidate IS the subject.
        // KILLS: dropping the `number = ref.Number` arm.
        let bodies = Map.ofList [ 74, "Paths: scripts/fsgg-coord src/Shared.fs" ]
        let code, out, _ = runOn None Set.empty (Map.ofList [ 74, "kite-469" ]) Map.empty bodies

        Assert.Equal("disjoint", str "verdict" (parsed out))
        Assert.Equal(0, code)

    // ---- .github#1792: ONE MARKER, ONE ANSWER --------------------------------------------------------
    //
    // The legs above ask `activeCollisions` alone. These ask it AND `Scan.snapshot` — the scheduler behind
    // `take`/`next`/`batch` — about ONE board, ONE marker and ONE second, because the #1792 defect is not
    // that either surface is wrong on its own. Both looked right in isolation, which is exactly what made
    // it survive three lock fixes in a day: `Scan.snapshot` read the marker through `Reads.reserver` (a
    // lapsed lease still reserves), `activeCollisions` read the SAME marker through `Reads.winner` (it does
    // not), and nothing in the codebase reconciled them. A worker reaching a file by the `take` path was
    // refused it while a worker reaching it by the `widen` path was cleared for it.
    //
    // Each leg below varies exactly ONE thing about row #75 and asserts BOTH answers, so no leg can pass by
    // agreeing with itself.

    /// The #1792 world. Three rows, and only #75 varies:
    ///   • **#74** — OURS (`kite-469`, `In progress`). The caller that runs `widen … --paths src/Shared.fs`.
    ///   • **#75** — THE CONTENDED ROW, declaring `src/Shared.fs`. Its marker and its column are the inputs.
    ///   • **#76** — an UNCLAIMED `Ready` candidate declaring `src/Shared.fs` and nothing else. It is what
    ///     the SCHEDULER is asked about, and it exists so the scheduler leg cannot pass for the gate leg's
    ///     reason: #74 is HELD, so `batch` passes over it whatever #75's marker says, and a test watching
    ///     #74 would be measuring the claim rather than the lapsed lease.
    let private contendedBodies =
        Map.ofList [ 74, "Paths: scripts/fsgg-coord"; 75, "Paths: src/Shared.fs"; 76, "Paths: src/Shared.fs" ]

    let private contendedWorld (column75: string) (holders: Map<int, string>) (age: Map<int, int>) =
        worldOf
            (fun n ->
                match n with
                | 75 -> column75
                | 76 -> "Ready"
                | _ -> "In progress")
            contendedBodies
            holders
            age
            Set.empty
            false

    /// THE SCHEDULER'S ANSWER: the refs `batch --json` offers. `Client.batch` runs the real `Scan.snapshot`,
    /// so this is the reservation rule under test and not a re-implementation of it.
    let private scheduled (world: Fake.Recorder) : string list =
        let dir = cacheDir ()

        try
            Directory.CreateDirectory dir |> ignore
            let _, out = runIn dir world [ "batch"; "--repo"; "FS.GG.SDD"; "--json" ]

            (parsed out).EnumerateArray() |> Seq.map (fun e -> e.GetString()) |> List.ofSeq
        finally
            try
                Directory.Delete(dir, true)
            with _ ->
                ()

    /// THE COLLISION GATE'S ANSWER: `widen`'s verdict for the same files on the same world.
    let private gateVerdict (world: Fake.Recorder) : string =
        let dir = cacheDir ()

        try
            Directory.CreateDirectory dir |> ignore
            let _, out = runIn dir world (widenOnto "src/Shared.fs")
            str "verdict" (parsed out)
        finally
            try
                Directory.Delete(dir, true)
            with _ ->
                ()

    [<Fact>]
    let ``#1792: a LAPSED lease reserves on BOTH surfaces`` () =
        // THE ITEM. #75's marker is 240 minutes old against the 120-minute default lease — backdated by the
        // fixture, never slept for, which is how production reaches this state and how a test may.
        //
        // Before #1792 this leg was the defect in one line: the scheduler refused #76 and the gate cleared
        // #74 onto the very same file, same board, same second. KILLS: reverting `activeCollisions` to
        // `Reads.winner` (the gate goes `disjoint` while the scheduler still refuses), and equally any
        // "fix" that instead moved `Scan.snapshot` to `winner` (the scheduler would then offer #76 and this
        // leg fails on the OTHER assertion). Both directions are killed here, deliberately: the item is
        // about the two agreeing, not about which one moved.
        //
        // #75 SITS IN `Blocked`, AND THE COLUMN IS LOAD-BEARING. An earlier draft of this leg used
        // `In progress` and SURVIVED the "move the scheduler to `winner`" mutation: with no live winner the
        // markerless-`In progress` arm (`RUnowned`) reserved the file anyway, so the scheduler assertion
        // passed for a reason that had nothing to do with the lapsed lease. A column that reserves NOTHING
        // on its own is what makes this leg measure the MARKER — which is the only thing #1792 is about.
        // `Blocked` also keeps #75 out of the candidate pool, so the assertion is about #76's admission and
        // not about which of two colliding rows won a lane.
        let world = contendedWorld "Blocked" laggingHolders (Map.ofList [ 75, 240 ])

        Assert.DoesNotContain("FS.GG.SDD#76", scheduled world)
        Assert.Equal("overlap", gateVerdict (contendedWorld "Blocked" laggingHolders (Map.ofList [ 75, 240 ])))

    [<Fact>]
    let ``#1792 control: with NO marker and no In-progress column, BOTH surfaces free the file`` () =
        // THE LEG THAT MAKES THE OTHER TWO CAPABLE OF FAILING, and without it neither is. #75 sits in a
        // column that reserves nothing and carries no marker, so the file is genuinely free — and #76 must
        // now be OFFERED and the gate must say `disjoint`. `Assert.DoesNotContain` is satisfied by a
        // scheduler that offers nothing ever (an empty board, a broken fixture, a `batch` that errored and
        // printed `[]`), and that is precisely the shape of the ten checks-that-could-not-fail found in
        // this codebase on 2026-07-28. This leg is what forces `scheduled` to be able to return #76 at all.
        //
        // #75 is `Blocked` rather than `Ready` so it is not itself a competing candidate for the same file —
        // the assertion is about #76's admission, not about which of two colliding rows won a lane.
        let world = contendedWorld "Blocked" (Map.ofList [ 74, "kite-469" ]) Map.empty

        Assert.Contains("FS.GG.SDD#76", scheduled world)
        Assert.Equal("disjoint", gateVerdict (contendedWorld "Blocked" (Map.ofList [ 74, "kite-469" ]) Map.empty))

    [<Fact>]
    let ``#1792: agreeing with the scheduler costs the gate NOTHING`` () =
        // .github#1779 made this scan CHEAPER — 24/27/31 GraphQL points to ZERO, REST at one issue-list read
        // plus one marker read per COLLIDING row — and #1792 must not spend that back. It does not, and this
        // is the measurement rather than the assertion that it does not: `Reads.reserver` is a pure function
        // over the complete marker scan already fetched on the line above it, so the reservation rule changed
        // and the REQUEST COUNT did not.
        //
        // COUNTED, NOT ESTIMATED (.github#1086 got this same trade wrong by an order of magnitude by
        // estimating). `Fake.Recorder` records every request the engine issues, so these are exact and
        // reproducible — which the live rate-limit counters, at a 2-4 call delta against seven concurrent
        // workers, are not.
        //
        // The world is the LAPSED one — the case whose answer #1792 changed — so this counts the path that
        // now reports OVERLAP where it used to report DISJOINT. Doing MORE work for the new answer is
        // exactly the regression this leg exists to catch.
        let world = contendedWorld "Blocked" laggingHolders (Map.ofList [ 75, 240 ])
        let dir = cacheDir ()

        try
            Directory.CreateDirectory dir |> ignore
            let _, out = runIn dir world (widenOnto "src/Shared.fs")
            Assert.Equal("overlap", str "verdict" (parsed out))

            // THE BOARD IS NOT READ. #1779's zero, still zero — and the only way to reach the one case
            // #1792 declined (`RUnowned`, which is column-derived) is to break this.
            Assert.Equal(0, world.GraphQlCalls)

            // REST: the issue-list read, one marker read for the ONE colliding row, and the writes `widen`
            // makes on top (the body PATCH and the courtesy notice). The number is pinned rather than
            // bounded so that a re-introduced per-row marker sweep — the ~74-reads-per-widen shape #1779
            // measured and refused — cannot land quietly.
            Assert.Equal(7, world.RestCalls)
        finally
            try
                Directory.Delete(dir, true)
            with _ ->
                ()

    [<Fact>]
    let ``#1792: a MARKERLESS In-progress row diverges, and that divergence is deliberate`` () =
        // THE REMAINDER #1792 DECLINED TO COLLAPSE, PINNED SO IT STAYS A DECISION.
        //
        // #75 has NO marker and sits in `In progress`. `Scan.snapshot` reserves it (`RUnowned` — something
        // is evidently editing those files) so #76 is refused. `activeCollisions` cannot see it at all:
        // #1779 keyed that scan on `Reads.openIssues`, which carries numbers and bodies and no board state,
        // so the reservation is unreachable there BY CONSTRUCTION rather than by omission.
        //
        // It is left that way for two reasons, both about the gate rather than the scheduler: reaching the
        // column costs a board read per call on a verb workers loop (the GraphQL half #1779 drove to zero —
        // #418/#1666), and a markerless row offers a blocked worker no exit at all: no worker to `say` to,
        // no marker to `reap`, no lease to wait out. A scheduler can absorb an unactionable stop by waiting;
        // a gate a worker is told to believe cannot, because the only remedy left is to ignore it.
        //
        // So the rule this pins is: THE TWO SURFACES AGREE ON EVERY MARKER, LIVE OR LAPSED, AND DIVERGE ONLY
        // WHERE THERE IS NO MARKER. KILLS: quietly teaching `activeCollisions` the column — which would be a
        // defensible change, and must be made with these two costs in front of whoever makes it, not as a
        // side effect. If you are here because this leg failed, that is the conversation it is asking for.
        let world = contendedWorld "In progress" (Map.ofList [ 74, "kite-469" ]) Map.empty

        Assert.DoesNotContain("FS.GG.SDD#76", scheduled world)
        Assert.Equal("disjoint", gateVerdict (contendedWorld "In progress" (Map.ofList [ 74, "kite-469" ]) Map.empty))

    // THE QUEUE IS NO LONGER CONSULTED, AND THESE SAY SO OUT LOUD (.github#1779).
    //
    // Every row below was a #1740 NEGATIVE leg asserting `DISJOINT` — a queue entry that is not a live claim
    // on this row, so the scan "correctly" fell back to the `Ready` column. But the FIXTURE underneath them
    // never changed: #75 has a live marker declaring the file we are asking for, in every one. `DISJOINT`
    // was the wrong answer in all six; the queue's precision was buying a false verdict. So they are
    // INVERTED rather than deleted — the record of a check that pinned the defect as correct behaviour is
    // worth more than the six lines it costs, and deleting it would leave the next reader to rediscover why
    // `runWithQueue None` flipped.
    //
    // WHAT THEY ARE WORTH, STATED HONESTLY, BECAUSE AN EARLIER DRAFT OF THIS NOTE OVERCLAIMED IT. It said
    // "a scan that reinstated a queue-derived candidate set would fail exactly here". That is true only of a
    // FULL revert (candidates = rows filtered by column-or-queue). A UNION reinstatement — `openIssues` ∪
    // queue-derived, which is closer to what a well-meaning re-patch would write — passes all six, because
    // the marker path already answers OVERLAP for #75. So these six rows are ONE assertion repeated with
    // dead inputs: `issueRef`/`boardTitle`/`field`/`value` reach no code, which is precisely the property
    // being asserted. The mutant that kills a queue-derived candidate set is M6 in the PR's matrix, and it
    // is killed by the two `#1779` positive legs above, not by these.
    [<Theory>]
    // No queue at all — the permanently-failed write, which nothing will ever replay.
    [<InlineData("", "", "", "")>]
    // Same value a real claim writes, different FIELD.
    [<InlineData("FS-GG/FS.GG.SDD#75", "Coordination", "Class", "In progress")>]
    // A `Status` write moving the row somewhere that is not a claim.
    [<InlineData("FS-GG/FS.GG.SDD#75", "Coordination", "Status", "Done")>]
    // #882 — queued against ANOTHER BOARD, which `flush` refuses to resolve here.
    [<InlineData("FS-GG/FS.GG.SDD#75", "Other Board", "Status", "In progress")>]
    // A perfectly valid live-claim entry naming a DIFFERENT row.
    [<InlineData("FS-GG/FS.GG.SDD#74", "Coordination", "Status", "In progress")>]
    // ...and a different REPO is a different row too (#353's whole subject).
    [<InlineData("FS-GG/FS.GG.Other#75", "Coordination", "Status", "In progress")>]
    let ``#1779: the deferral queue no longer decides anything — the live marker does``
        (issueRef: string, boardTitle: string, field: string, value: string)
        =
        let queued =
            if issueRef = "" then
                None
            else
                Some(queuedWrite issueRef boardTitle field value)

        let code, out = runWithQueue queued

        let collision = soleCollision out
        Assert.Equal("FS.GG.SDD#75", str "ref" collision)
        Assert.Equal("otter-9c21", str "worker" collision)
        Assert.Equal(6, code)

    [<Fact>]
    let ``#1779: an UNPARSEABLE deferral queue no longer refuses, because nothing reads it`` () =
        // #1740 made an unreadable queue a REFUSAL, and that was right while the queue was evidence: a
        // DISJOINT built out of not having been able to look is #266 exactly. It is not evidence any more —
        // the candidate set is the repo's open issues and their markers — so refusing on it would be a gate
        // that fails closed over a file it does not consult, which is its own defect.
        //
        // PINNED AS THE ORDINARY VERDICT, not merely as "exit 0": this must answer the collision question
        // on the merits, and the merits here are an OVERLAP.
        let dir = cacheDir ()

        try
            Directory.CreateDirectory dir |> ignore
            File.WriteAllText(Path.Combine(dir, "pending.jsonl"), "{not a queue entry\n")

            let code, out = runIn dir (overlappingWorld false) (widenOnto "src/Shared.fs")

            let collision = soleCollision out
            Assert.Equal("FS.GG.SDD#75", str "ref" collision)
            Assert.Equal(6, code)
        finally
            try
                Directory.Delete(dir, true)
            with _ ->
                ()

    // ---- .github#1779 AC2 — THE API COST, ASSERTED RATHER THAN DESCRIBED ------------------------------
    //
    // WHY THIS IS A TEST AND NOT A COMMENT. `.github#1086` got this same trade wrong by an order of
    // magnitude by ESTIMATING it, and the first draft of #1779 declined the whole design over an estimate
    // of "~74 REST marker reads per `widen`" that the source does not support. A number in prose rots
    // silently; a number in an assertion does not.
    //
    // The live half of the measurement is on the Coordination board and is recorded in `activeCollisions`'
    // own comment: 24/27/31 GraphQL points before, 0 after. GitHub's `/rate_limit` core counter is
    // eventually consistent and did NOT move for a single known REST call when checked, so the REST half is
    // NOT asserted from it — that would be a number nobody observed. `Fake.Recorder` counts every request
    // the engine makes, exactly, which is what these two legs read.

    [<Fact>]
    let ``#1779 AC2: the collision scan spends ZERO GraphQL and ONE marker read per COLLIDING row`` () =
        // Six open issues, one of which collides. The old scan read a marker AND a body for every
        // `In progress` row whether or not its tokens could ever collide; this one reads the repo's issue
        // list once and then exactly one marker.
        let bodies =
            Map.ofList
                [ 74, "Paths: scripts/fsgg-coord"
                  75, "Paths: src/Shared.fs"
                  76, "Paths: docs/a.md"
                  77, "Paths: docs/b.md"
                  78, "Paths: docs/c.md"
                  79, "Paths: docs/d.md" ]

        let holders =
            Map.ofList [ 74, "kite-469"; 75, "otter-9c21"; 76, "wren-1"; 77, "wren-2"; 78, "wren-3"; 79, "wren-4" ]

        let code, out, world = runOn None Set.empty holders Map.empty bodies

        Assert.Equal("overlap", str "verdict" (parsed out))
        Assert.Equal(6, code)

        // ONE issue-list read — the candidate set, bodies included, and `Reads.openIssues`' own contract
        // says the bodies are free here. `inm=none` rides in the log line: a 304 could serve a body
        // captured before a marker was posted.
        Assert.Equal(1, world.Count "issue-list FS-GG/FS.GG.SDD paginate=1 inm=none")

        // ONE marker read, for the one colliding row. FIVE other live claims are in this world and none of
        // their markers is read, because none of their tokens could ever collide. The old scan would have
        // read all six. (`verifyHeld` reads #74's own markers before any of this, which is the +1.)
        Assert.Equal(1, world.Count "comment-list FS-GG/FS.GG.SDD 75")
        Assert.Equal(0, world.Count "comment-list FS-GG/FS.GG.SDD 76")
        Assert.Equal(0, world.Count "comment-list FS-GG/FS.GG.SDD 79")

        // ZERO GraphQL. The board query and the `bootstrapCached` behind it are gone from this path — which
        // is why the live measurement is 0 points on a COLD cache too, not merely on a warm one.
        Assert.Equal(0, world.GraphQlCalls)

    [<Fact>]
    let ``#1779 AC2: a DISJOINT verdict costs the issue list and NOT ONE marker read`` () =
        // The cheap case is the common one, and it is the one that must not regress: nothing collides, so
        // nothing is confirmed, so the scan is a single list read. KILLS: reading markers before filtering
        // on tokens — the ~74-reads-per-widen shape #1779 was filed believing was unavoidable.
        let bodies =
            Map.ofList [ 74, "Paths: scripts/fsgg-coord"; 75, "Paths: docs/a.md"; 76, "Paths: docs/b.md" ]

        let holders = Map.ofList [ 74, "kite-469"; 75, "otter-9c21"; 76, "wren-1" ]

        let dir = cacheDir ()

        try
            Directory.CreateDirectory dir |> ignore
            let world = worldOf (fun _ -> "In progress") bodies holders Map.empty Set.empty false
            let code, out = runIn dir world [ "widen"; "FS.GG.SDD#74"; "--worker"; "kite-469"; "--json"; "--paths"; "docs/unrelated.md" ]

            Assert.Equal("disjoint", str "verdict" (parsed out))
            Assert.Equal(0, code)
            Assert.Equal(1, world.Count "issue-list FS-GG/FS.GG.SDD paginate=1 inm=none")
            Assert.Equal(0, world.Count "comment-list FS-GG/FS.GG.SDD 75")
            Assert.Equal(0, world.Count "comment-list FS-GG/FS.GG.SDD 76")
            Assert.Equal(0, world.GraphQlCalls)
        finally
            try
                Directory.Delete(dir, true)
            with _ ->
                ()

    // ---- .github#1740 AC5: the NARROWING sentence, and the one it must NOT be -------------------------
    //
    // WHY THIS IS HERE AND NOT ONLY IN `writes.sh`. The e2e leg exercises the narrowing branch alone, and a
    // branch with no counter-example is not pinned: `let isNarrowing = true` satisfies it. These two legs
    // are the counter-example — same board, same collision, same worker, and the ONLY difference is whether
    // the update is a proper subset of what was declared before.
    //
    // The sentence is on STDERR, so these read STDERR — see `runCapturingStderr`. Asserting the ABSENCE of
    // the narrowing claim as well as its presence is the point: a leg that only greps for the good sentence
    // passes when both branches print it.
    let private narrowingClaim = "cannot have introduced the collision"

    [<Fact>]
    let ``#1740 AC5: a proper narrowing that collides is reported as PRE-EXISTING`` () =
        // #74 declares two tokens; `set-paths` drops one. A subset names strictly fewer files, so this
        // command provably did not cause the collision it is about to be told about.
        let bodies = Map.ofList [ 74, "Paths: scripts/fsgg-coord src/Shared.fs"; 75, "Paths: src/Shared.fs" ]

        let code, out, err =
            runCapturingStderr
                (world bodies laggingHolders false)
                [ "set-paths"; "FS.GG.SDD#74"; "--worker"; "kite-469"; "--json"; "--paths"; "src/Shared.fs" ]

        Assert.Equal("overlap", str "verdict" (parsed out))
        Assert.Contains(narrowingClaim, err)
        Assert.Equal(6, code)

    [<Fact>]
    let ``#1740 AC5: a WIDENING that collides is NOT called a narrowing`` () =
        // THE COUNTER-EXAMPLE, and the reason the leg above proves anything. Same board, same collision,
        // same worker — the declaration GREW, so nothing may claim the overlap pre-dates the command.
        // Without this, `let isNarrowing = true` passes the entire suite.
        let code, out, err =
            runCapturingStderr
                (overlappingWorld false)
                [ "widen"; "FS.GG.SDD#74"; "--worker"; "kite-469"; "--json"; "--paths"; "src/Shared.fs" ]

        Assert.Equal("overlap", str "verdict" (parsed out))
        Assert.DoesNotContain(narrowingClaim, err)
        // ...and it must not have regressed to the sentence #1740 removed, either.
        Assert.DoesNotContain("introduced a collision — do NOT", err)
        Assert.Equal(6, code)

    [<Fact>]
    let ``#1740 AC5: an update that changes NOTHING is not a narrowing either`` () =
        // THE ARM THE LENGTH TEST EXISTS FOR. `widen` is a union, so an idempotent re-run arrives with
        // proposed = prior — a subset by `forall`, and not a narrowing by any honest reading. Both rows
        // declare the colliding token, so the update is an identity AND collides, which is the only way to
        // reach the sentence at all.
        let bodies = Map.ofList [ 74, "Paths: src/Shared.fs"; 75, "Paths: src/Shared.fs" ]

        let code, _, err =
            runCapturingStderr
                (world bodies laggingHolders false)
                [ "widen"; "FS.GG.SDD#74"; "--worker"; "kite-469"; "--json"; "--paths"; "src/Shared.fs" ]

        Assert.DoesNotContain(narrowingClaim, err)
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
    let private classWorld (withClass: bool) =
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
                        if withClass then
                            ok
                                """{"data":{"organization":{"projectV2":{"fields":{"nodes":[{"id":"PVTSSF_status","name":"Status","dataType":"SINGLE_SELECT","options":[{"id":"opt_ready","name":"Ready"},{"id":"opt_done","name":"Done"}]},{"id":"PVTSSF_class","name":"Class","dataType":"SINGLE_SELECT","options":[{"id":"opt_defect","name":"defect"},{"id":"opt_hard","name":"hardening"},{"id":"opt_dec","name":"decision"}]},{"id":"PVTF_blocked","name":"Blocked by","dataType":"TEXT"}]}}}},"rateLimit":{"cost":1,"remaining":4977}}"""
                        else
                            ok
                                """{"data":{"organization":{"projectV2":{"fields":{"nodes":[{"id":"PVTSSF_status","name":"Status","dataType":"SINGLE_SELECT","options":[{"id":"opt_ready","name":"Ready"},{"id":"opt_done","name":"Done"}]},{"id":"PVTF_blocked","name":"Blocked by","dataType":"TEXT"}]}}}},"rateLimit":{"cost":1,"remaining":4977}}"""
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
    ///
    /// Identity is scrubbed for `runIn`'s reason (.github#1646, .github#1817) — `reconcileArgs` below
    /// splices `--worker heron-1f20` into every leg that does not override it, and `reconcile --apply`
    /// attributes the writes it makes to whichever worker resolves, so a harness-derived session id would
    /// have every apply-phase assertion attribute to the wrong worker depending on which shell ran the
    /// suite. Scrubbed here rather than per-call, for `runQueue`'s reason: it is the one place this fixture
    /// touches process-global state, and the four ambient variables are exactly as global as `FSGG_COORD_CACHE`.
    let private runReconcileWith
        (transport: Fake.Recorder)
        (args: string list)
        (adjust: Options.Options -> Options.Options)
        : int * string * string =
        let dir = Path.Combine(Path.GetTempPath(), "fsgg-1524-" + Guid.NewGuid().ToString "n")
        let previousCache = Environment.GetEnvironmentVariable "FSGG_COORD_CACHE"
        let previousKitRoot = Environment.GetEnvironmentVariable "FSGG_KIT_ROOT"

        let identityVars =
            [ "FSGG_WORKER"; "CLAUDE_CODE_SESSION_ID"; "OPENCODE_SESSION_ID"; "FSGG_AGENT_SESSION_ID" ]

        let previousIdentity =
            identityVars |> List.map (fun v -> v, Environment.GetEnvironmentVariable v)

        let stdout = Console.Out
        let stderr = Console.Error
        use capturedOut = new StringWriter()
        use capturedErr = new StringWriter()

        try
            Directory.CreateDirectory dir |> ignore

            for v, _ in previousIdentity do
                Environment.SetEnvironmentVariable(v, null)

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

            for v, previous in previousIdentity do
                Environment.SetEnvironmentVariable(v, previous)

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
        let code, out, err = runReconcile (classWorld true) (reconcileArgs [ "--apply" ])
        Assert.True((code = 0), err)

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
        let code, out, _ = runApplyJson (classWorld true)

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
    let ``#1625 reconcile --apply with no Class field withholds every projection once`` () =
        let code, _, err = runReconcile (classWorld false) (reconcileArgs [ "--apply" ])

        Assert.Equal(0, code)
        Assert.Equal(1, err.Split("board has no Class field").Length - 1)
        Assert.Contains("createProjectV2Field", err)

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
    ///
    /// Identity is scrubbed for `runIn`'s reason (.github#1646, .github#1817) — every leg below names its
    /// worker with `--worker`, and `take`/`next`/`batch` do not check the derived identity against it TODAY
    /// (unlike `claim`/`release`/`heartbeat`/`widen`, which refuse the disagreement outright), so this was
    /// LATENT rather than live: it passed under CI, where no agent session variable exists, and would only
    /// misbehave the day one of these verbs starts consulting `Worker.Derived` too — exactly the condition
    /// the three shell harnesses in .github#1817's table were in until something did. Scrubbing here removes
    /// the ambient dependency before that day arrives, rather than after.
    let private runQueueWithKit
        (configureKit: string -> unit)
        (transport: Fake.Recorder)
        (args: string list)
        : int * string * string =
        let dir = Path.Combine(Path.GetTempPath(), "fsgg-1525-" + Guid.NewGuid().ToString "n")
        let previousCache = Environment.GetEnvironmentVariable "FSGG_COORD_CACHE"
        let previousKitRoot = Environment.GetEnvironmentVariable "FSGG_KIT_ROOT"

        let identityVars =
            [ "FSGG_WORKER"; "CLAUDE_CODE_SESSION_ID"; "OPENCODE_SESSION_ID"; "FSGG_AGENT_SESSION_ID" ]

        let previousIdentity =
            identityVars |> List.map (fun v -> v, Environment.GetEnvironmentVariable v)

        let stdout = Console.Out
        let stderr = Console.Error
        use capturedOut = new StringWriter()
        use capturedErr = new StringWriter()

        try
            Directory.CreateDirectory dir |> ignore
            configureKit dir

            for v, _ in previousIdentity do
                Environment.SetEnvironmentVariable(v, null)

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

            for v, previous in previousIdentity do
                Environment.SetEnvironmentVariable(v, previous)

            try
                Directory.Delete(dir, true)
            with _ ->
                ()

    let private runQueue (transport: Fake.Recorder) (args: string list) : int * string * string =
        runQueueWithKit ignore transport args

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

    let private waveModelDeclaration implementers =
        $"<!-- fsgg:wave-model:v1 waves=2 implementer-slots-per-wave=%d{implementers} review-slots=2 consolidation-threshold=3 -->"

    /// The supported receiver topology: work-board is always materialized; operator-only drive-board is not.
    let private installWaveModel (root: string) =
        let declaration =
            waveModelDeclaration 3

        let directory = Path.Combine(root, ".claude", "skills", "work-board", "references")
        Directory.CreateDirectory directory |> ignore
        File.WriteAllText(Path.Combine(directory, "host-loop.md"), declaration)

    let private installDisagreeingWaveModels (root: string) =
        for skill, implementers in [ "drive-board", 4; "work-board", 3 ] do
            let directory = Path.Combine(root, ".claude", "skills", skill, "references")
            Directory.CreateDirectory directory |> ignore
            File.WriteAllText(Path.Combine(directory, "host-loop.md"), waveModelDeclaration implementers)

    [<Theory>]
    [<InlineData(6, true, 0, false)>]
    [<InlineData(2, true, 4, true)>]
    [<InlineData(2, false, 4, false)>]
    let ``#2096 batch reports full partial and drained wave occupancy without changing stdout``
        (active: int)
        (hasReady: bool)
        (openSlots: int)
        (shortfall: bool)
        =
        let readyNumber = active + 1

        let bodies =
            [ for n in 1..active -> n, $"Paths: src/%d{n}" ]
            @ (if hasReady then [ readyNumber, $"Paths: src/%d{readyNumber}" ] else [])
            |> Map.ofList

        let holders =
            [ for n in 1..active -> n, $"worker-%d{n}" ] |> Map.ofList

        let statusFor n =
            if hasReady && n = readyNumber then "Ready" else "In progress"

        let transport = worldOf statusFor bodies holders Map.empty Set.empty false

        let code, out, err =
            runQueueWithKit
                installWaveModel
                transport
                [ "batch"; "--repo"; "FS.GG.SDD"; "--json" ]

        let expectedOut =
            if hasReady then $"[\"FS.GG.SDD#%d{readyNumber}\"]" else "[]"

        Assert.Equal(expectedOut + Environment.NewLine, out)
        Assert.Contains(
            $"wave occupancy: {{\"activeItems\":%d{active},\"waveCapacity\":6,\"openSlots\":%d{openSlots}}}",
            err
        )

        Assert.Equal(shortfall, err.Contains "WAVE SHORTFALL")
        Assert.Equal(0, code)

    [<Fact>]
    let ``#2096 installed driver contracts that disagree fail the occupancy read closed`` () =
        let transport =
            worldIn "Ready" (Map.ofList [ 74, "Paths: scripts/fsgg-coord" ]) Map.empty false

        let code, out, err =
            runQueueWithKit
                installDisagreeingWaveModels
                transport
                [ "batch"; "--repo"; "FS.GG.SDD"; "--json" ]

        Assert.Equal("[\"FS.GG.SDD#74\"]" + Environment.NewLine, out)
        Assert.Contains("wave occupancy: unavailable", err)
        Assert.Contains("declare different fsgg:wave-model:v1 values", err)
        Assert.DoesNotContain("WAVE SHORTFALL", err)
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

    // ---- .github#1688 — THE SIBLING SWEEP: no `Json`-admitting verb leaks prose onto stdout ----------
    //
    // WHAT THIS ITEM ASKED FOR, AND WHAT WAS ALREADY TRUE WHEN IT WAS FILED. #1688's ACs 1-3 and 5 are
    // about `take --json`'s exit-5 arm: a parseable document naming the outcome and the reason, prose kept
    // off stdout, and a fixture pinning both projections. All four had landed SEVENTEEN HOURS EARLIER as
    // `.github#1525` (PR #1563, `f7deb05`) — the receipt is `Render.renderNoItemJson` and the fixture is
    // the block directly above this one. The prose the issue records was measured against a STALE ENGINE
    // BINARY, which is `.github#1549`'s hazard rather than a second instance of `.github#1562`'s.
    //
    // AC 4 IS THE ONE THAT WAS REAL, and it is the only one of the five that is about a POPULATION rather
    // than about `take`: "the other `Json`-admitting verbs' empty and refusal arms are audited in the same
    // change". #1562 fixed `next`, #1525 fixed `take`, and NOTHING SWEPT — so a third instance would have
    // been found the way the first two were, by a worker hitting it on a live board.
    //
    // A STATED AUDIT ROTS; THIS ONE RUNS. The answer today is "no others leak", and a sentence saying so
    // is true only of the engine it was written against — the defect class is created by ADDING a verb or
    // by promoting one out of `TextOnly`, which is exactly the moment a sentence in a PR body is not
    // re-read. So the audit is a fixture over the population, and the population is DERIVED from
    // `renderSupport` rather than restated: the coverage leg below goes red on a verb this file has never
    // heard of, and names it.
    //
    // THE INVARIANT, AND WHY SILENCE PASSES. Under `--json`, stdout is either ONE parseable document or
    // EMPTY. A refusal that prints nothing on stdout is correct — the diagnostic belongs to `eprint` and
    // the verdict to the exit code — so what is refused here is prose, never silence. That is exactly the
    // property `.github#266` needs kept: "I found nothing" and "I could not look" stay distinguishable
    // because the first is a document and the second is an empty stream at a non-zero code.

    /// Every command whose declared `renderSupport` admits `Json` — AC 4's population, read off the DU.
    ///
    /// DERIVED, NOT LISTED, for `CommandSurfaceTests`' reason: `renderSupport` is the single hand-written
    /// fact about the renderers, and a second copy of the honouring set here would be free to drift from
    /// it exactly as the three copies #1523 found had drifted from each other.
    let private jsonAdmitting: Options.Command list =
        Microsoft.FSharp.Reflection.FSharpType.GetUnionCases typeof<Options.Command>
        |> Array.toList
        |> List.choose (fun case ->
            if case.GetFields().Length <> 0 then
                None
            else
                Some(Microsoft.FSharp.Reflection.FSharpValue.MakeUnion(case, [||]) :?> Options.Command))
        |> List.filter (fun c -> Options.renderSupport c <> Options.TextOnly)

    /// The verbs the sweep DRIVES: the command the row CLAIMS to cover, the argv, and the exit code that
    /// says the intended arm was actually reached.
    ///
    /// The board every one of them meets is `emptyQueue ()` — no rows at all — so the ten read verbs land
    /// on their EMPTY arm by construction. The four lock verbs are pointed at `#999`, which the fixture
    /// has no issue for: those verbs have no empty outcome to reach, so they land on a REFUSAL arm
    /// instead, and AC 4 names both kinds.
    ///
    /// THE EXIT CODE IS PINNED BECAUSE THE FOUR REFUSAL ROWS PRINT NOTHING, and "stdout is not prose" is
    /// satisfied perfectly by a verb that never ran. That is the vacuity the block above rejects for
    /// `take` (`#1562`'s "an engine that printed nothing on stdout ever would satisfy the assertion"), and
    /// it applies to a whole row here. The code is what makes "it reached a refusal" an assertion rather
    /// than an assumption — and it is a per-row constant rather than `<> 0` because the arms differ and
    /// are worth naming: `claim` posts its marker and then loses its own CAS re-read against a fixture
    /// whose `GET …/comments` answers `[]` (3, contended), `adopt` finds no expired claim to collect (3),
    /// `widen`/`set-paths` refuse a lock the worker does not hold (1, #706), and `predicate`'s oracle
    /// fails closed with no registry (4, no verdict). `take`'s 5 is EX_NONE, #585's "looked, found
    /// nothing"; the remaining reads are a green empty answer.
    let private sweptArms: (Options.Command * string list * int) list =
        [ Options.Take, [ "take"; "--repo"; "FS.GG.SDD"; "--worker"; "otter-9c21"; "--json" ], 5
          Options.DriverCmd, [ "driver"; "--repo"; "FS.GG.SDD"; "--json" ], 1
          Options.BatchCmd, [ "batch"; "--repo"; "FS.GG.SDD"; "--json" ], 0
          Options.Ready, [ "ready"; "--repo"; "FS.GG.SDD"; "--json" ], 0
          Options.Reconcile, [ "reconcile"; "--repo"; "FS.GG.SDD"; "--json" ], 0
          Options.LintCmd, [ "lint"; "--repo"; "FS.GG.SDD"; "--json" ], 0
          Options.BoardCmd, [ "board"; "--json" ], 0
          Options.Who, [ "who"; "--repo"; "FS.GG.SDD"; "--json" ], 0
          Options.Inbox, [ "inbox"; "--repo"; "FS.GG.SDD"; "--worker"; "otter-9c21"; "--json" ], 0
          Options.Budget, [ "budget"; "--json" ], 0
          Options.Predicate, [ "predicate"; "fsgg.kit"; "version"; "9.9.9"; "--json" ], 4
          Options.Claim, [ "claim"; "FS.GG.SDD#999"; "--worker"; "otter-9c21"; "--json" ], 3
          Options.Adopt, [ "adopt"; "FS.GG.SDD#999"; "--worker"; "otter-9c21"; "--json" ], 3
          Options.Widen, [ "widen"; "FS.GG.SDD#999"; "--worker"; "otter-9c21"; "--json"; "--paths"; "src/X.fs" ], 1
          Options.SetPaths,
          [ "set-paths"; "FS.GG.SDD#999"; "--worker"; "otter-9c21"; "--json"; "--paths"; "src/X.fs" ],
          1 ]

    /// The `Json`-admitting verbs this fixture cannot reach, each with the reason and what reading their
    /// arms found. The reason lives HERE rather than in a PR body because that is the whole argument of
    /// this block: the coverage leg quotes it, so moving a verb between the two lists costs a line in a
    /// diff instead of costing nothing.
    let private notDriven: (Options.Command * string) list =
        [ Options.Decide,
          "`Program.fs` `decide` is private to the entry point; audited by reading — under `Json` the SAME `printfn (Snapshot.render …)` runs for all three verdicts and Red/NoVerdict only pick the exit code, so there is no verdict that swaps the document for prose (the eprint-per-verdict projection is `renderText`, which is the Text arm). Its two refusal arms, empty stdin and an unparseable snapshot, are `eprint` at a non-zero code"
          Options.LanesView,
          "`Program.fs` `lanes` is private to the entry point; audited by reading — `| Json -> printfn` emits one `Snapshot.renderLanes` document, and the empty partition renders as that document, not prose"
          Options.Facts,
          "`Program.fs` `facts` is private to the entry point; audited by reading — it reads nothing and cannot be empty, and `| Json -> printfn` emits one `Snapshot.renderFacts` document"
          Options.Scan,
          "`Program.fs` `scan` is private to the entry point; `JsonOnly`, and audited by reading — BOTH arms print the same snapshot document, and every failure is an `Error` on stderr at a non-zero code (never an empty snapshot, #344/#421/#461)"
          Options.CommandContractCmd,
          "`Program.fs` dispatches `renderCommandContract ()` inline; `JsonOnly`, it reads nothing, and `CommandSurfaceTests` already parses the emitted document"
          Options.Issues,
          "`Client.issues` is private; audited by reading — stdout is the raw REST body (`[]` on a repo with no issues), and BOTH its refusal arms are stderr at a non-zero code: the missing-repo refusal and the read failure, the latter through `fail` so a rate limit keeps EX_RATE"
          Options.DiffAudit,
          "`SemanticDiffApplication.run` is a local git-object command; planted base/head, unresolved, resolved, stale, malformed, threshold and declaration arms are covered by SemanticDiffTests plus the executable engine fixture" ]

    /// Drive ONE verb's empty or refusing arm under `--json`, capturing stdout and stderr APART.
    ///
    /// The split IS the assertion, on `runQueue`'s argument above: the "why nothing" diagnostics belong on
    /// stderr in both projections, and only separate streams can show that the document on stdout is alone
    /// there. Identity is scrubbed for `runIn`'s reason (#1646) — these legs name their worker with
    /// `--worker`, and a harness-derived session id would have every lock verb refuse for the wrong reason.
    let private runJsonArm (transport: Fake.Recorder) (args: string list) : int * string * string =
        let dir = Path.Combine(Path.GetTempPath(), "fsgg-1688-" + Guid.NewGuid().ToString "n")
        let previousCache = Environment.GetEnvironmentVariable "FSGG_COORD_CACHE"
        let previousKitRoot = Environment.GetEnvironmentVariable "FSGG_KIT_ROOT"
        let previousRegistry = Environment.GetEnvironmentVariable "FSGG_REGISTRY"

        let identityVars =
            [ "FSGG_WORKER"; "CLAUDE_CODE_SESSION_ID"; "OPENCODE_SESSION_ID"; "FSGG_AGENT_SESSION_ID" ]

        let previousIdentity =
            identityVars |> List.map (fun v -> v, Environment.GetEnvironmentVariable v)

        let stdout = Console.Out
        let stderr = Console.Error
        use capturedOut = new StringWriter()
        use capturedErr = new StringWriter()

        try
            Directory.CreateDirectory dir |> ignore

            for v, _ in previousIdentity do
                Environment.SetEnvironmentVariable(v, null)

            Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", dir)
            Environment.SetEnvironmentVariable("FSGG_KIT_ROOT", dir)
            // `predicate`'s oracle is authority-scoped and fails closed where the registry is absent, so a
            // path that does not exist IS its deterministic refusal arm — and pointing it at the throwaway
            // directory keeps the leg off the developer's real `registry/skills.yml`, which would make the
            // verdict depend on the checkout rather than on the renderer.
            Environment.SetEnvironmentVariable("FSGG_REGISTRY", Path.Combine(dir, "no-registry.yml"))
            Console.SetOut capturedOut
            Console.SetError capturedErr

            let opts = options args
            let ctx = context transport

            let code =
                match opts.Command with
                | Options.Take -> Client.take ctx opts
                | Options.BatchCmd -> Client.batch ctx opts
                | Options.DriverCmd -> Client.driver ctx opts
                | Options.Ready -> Client.ready ctx opts
                | Options.Reconcile -> Client.reconcile ctx opts
                | Options.LintCmd -> Client.lint ctx opts
                | Options.BoardCmd -> Client.boardCmd ctx
                | Options.Who -> Client.who ctx opts
                | Options.Inbox -> Client.inbox ctx opts
                | Options.Budget -> Client.budget ctx opts
                | Options.Predicate -> Client.predicate opts
                | Options.Claim -> Client.claim ctx opts
                | Options.Adopt -> Client.adopt ctx opts
                | Options.Widen -> Client.widen ctx opts
                | Options.SetPaths -> Client.setPaths ctx opts
                | other ->
                    failwithf
                        "the .github#1688 sweep has no dispatch for %A — add one to `runJsonArm`, or give the verb a reason in `notDriven`"
                        other

            Console.Out.Flush()
            Console.Error.Flush()
            code, capturedOut.ToString(), capturedErr.ToString()
        finally
            Console.SetOut stdout
            Console.SetError stderr
            Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", previousCache)
            Environment.SetEnvironmentVariable("FSGG_KIT_ROOT", previousKitRoot)
            Environment.SetEnvironmentVariable("FSGG_REGISTRY", previousRegistry)

            for v, previous in previousIdentity do
                Environment.SetEnvironmentVariable(v, previous)

            try
                Directory.Delete(dir, true)
            with _ ->
                ()

    [<Fact>]
    let ``#1739 lint reports a human park only after its machine blocker resolves`` () =
        let transport =
            worldWith
                (fun number -> if number = 42 then "Blocked" else "Done")
                (Map.ofList
                    [ 42, "Blocked by: FS-GG/FS.GG.SDD#2\nBlocked on: human/decision\nPaths: src/A.fs"
                      2, "<!-- fixture:closed -->\nPaths: src/B.fs" ])
                Map.empty
                false
        let code, output, _ = runJsonArm transport [ "lint"; "--repo"; "FS.GG.SDD"; "--json" ]
        // The deliberately minimal fixture also trips unrelated board hygiene errors; the note is
        // nevertheless emitted in the same lint result.
        Assert.Equal(1, code)
        Assert.Contains("HUMAN-PARK-MACHINE-CLEARED", output)
        Assert.Contains("human decision", output)

    [<Fact>]
    let ``#1892 json worker failures classify rate limits without using stdout`` () =
        let cases =
            [ "primary", Errors.RateLimited(Errors.RestBudget(Some "core"), None)
              "secondary", Errors.RateLimited(Errors.SecondaryLimit(Some "core", None), None)
              "unknown", Errors.RateLimited(Errors.UnknownBudget, None) ]

        for expectedKind, error in cases do
            let transport = Fake.Recorder(fun _ -> Error error)

            for args in
                [ [ "take"; "--repo"; "FS.GG.SDD"; "--worker"; "otter-9c21"; "--json" ]
                  [ "claim"; "--json"; "FS.GG.SDD#42"; "--worker"; "otter-9c21" ] ] do
                let code, stdout, stderr = runJsonArm transport args
                Assert.Equal(Errors.ExRate, code)
                Assert.Equal("", stdout)
                use document = JsonDocument.Parse(stderr)
                let root = document.RootElement
                Assert.Equal("error", root.GetProperty("kind").GetString())
                Assert.Equal(75, root.GetProperty("exitCode").GetInt32())
                Assert.Equal(expectedKind, root.GetProperty("rateLimit").GetString())
                Assert.False(System.String.IsNullOrWhiteSpace(root.GetProperty("message").GetString()))

    [<Fact>]
    let ``.github#1688 AC4 every driven Json verb's empty or refusing arm is ONE document, never prose`` () =
        let failures =
            sweptArms
            |> List.collect (fun (declared, argv, expectedCode) ->
                let verb = List.head argv
                let parsedCommand = (options argv).Command
                let code, out, _ = runJsonArm (emptyQueue ()) argv
                let trimmed = out.Trim()

                let notOneDocument =
                    if trimmed = "" then
                        None
                    else
                        try
                            (JsonDocument.Parse trimmed).Dispose()
                            None
                        with e ->
                            Some e.Message

                [
                  // THE ROW MUST DRIVE THE VERB IT CLAIMS TO COVER. `runJsonArm` dispatches on the argv's
                  // OWN parse, so without this the `Command` in the row is read by the coverage leg alone
                  // — and a mis-paired row (`Options.Claim` beside an `adopt` argv) would mark a verb
                  // covered that nothing ever ran, with both legs green. That is exactly the hole this
                  // block claims to close.
                  if parsedCommand <> declared then
                      yield
                          $"%s{verb}: the row declares %A{declared} but its argv parses to %A{parsedCommand} — it marks a verb covered that it never drives"

                  // AND IT MUST REACH THE ARM IT WAS WRITTEN FOR. Four of these rows print NOTHING on
                  // stdout, and "not prose" is satisfied perfectly by a verb that failed earlier than
                  // intended — so the code is what stops a fixture change quietly moving a row onto some
                  // other arm while the assertion below keeps passing.
                  if code <> expectedCode then
                      yield
                          $"%s{verb}: exit %d{code}, expected %d{expectedCode} — this row no longer reaches the arm it pins, so its stdout says nothing about that arm"

                  // THE INVARIANT ITSELF: one parseable document, or nothing at all.
                  match notOneDocument with
                  | Some message ->
                      yield $"%s{verb}: stdout under --json is not one JSON document (%s{message}); stdout was:\n%s{out}"
                  | None -> () ])

        Assert.True(
            List.isEmpty failures,
            "a `--json` verb put prose on stdout — this is `.github#1562`/`.github#1688`'s defect, and the "
            + "stream a driver parses:\n  "
            + String.concat "\n  " failures
        )

    [<Fact>]
    let ``.github#1688 AC4 the sweep covers every Json-admitting verb — a new one cannot slip past it`` () =
        // THE LEG THAT MAKES THE ONE ABOVE AN AUDIT RATHER THAN A SAMPLE. `sweptArms` and `notDriven` are
        // hand-written, so on their own they describe whichever engine their author was looking at. This
        // binds them to the DECLARATION: a verb added to `Command`, or promoted out of `TextOnly`, is in
        // `jsonAdmitting` the moment `renderSupport` says so, and lands here as a named failure.
        let sweptCommands = sweptArms |> List.map (fun (c, _, _) -> c)
        let classifiedList = sweptCommands @ (notDriven |> List.map fst)
        let classified = classifiedList |> Set.ofList
        let population = jsonAdmitting |> Set.ofList

        // NO VERB IS CLASSIFIED TWICE, on `CommandSurfaceTests`' argument for the same guard: `Set.ofList`
        // over the concatenation SWALLOWS a duplicate, so a verb listed in both `sweptArms` and
        // `notDriven` — "we drive it" and "we cannot drive it" at once — would satisfy coverage while
        // being excused from the sweep, and a botched rebase could grow either list without growing the
        // audit. The set equality below cannot see that; this can.
        Assert.Equal<Options.Command list>(List.distinct classifiedList, classifiedList)

        // EVERY `Command` CASE IS NULLARY, which is the assumption `jsonAdmitting` is built on: it drops
        // any case carrying fields, so a future non-nullary `--json` verb would be silently absent from
        // the population and this leg could not fire on it. That is the one way a verb could still slip
        // past, and it is cheaper to refuse the shape than to guess at constructing it.
        let nonNullary =
            Microsoft.FSharp.Reflection.FSharpType.GetUnionCases typeof<Options.Command>
            |> Array.filter (fun case -> case.GetFields().Length <> 0)
            |> Array.map (fun case -> case.Name)
            |> Array.toList

        Assert.True(
            List.isEmpty nonNullary,
            "`Command` grew a case with fields, and `jsonAdmitting` silently drops those — so this audit "
            + "can no longer see every verb. Teach it to construct them before adding one:\n  "
            + String.concat "\n  " nonNullary
        )

        let unaudited =
            Set.difference population classified |> Set.toList |> List.map (sprintf "%A")

        let stale =
            Set.difference classified population |> Set.toList |> List.map (sprintf "%A")

        Assert.True(
            List.isEmpty unaudited,
            "these verbs admit `--json` and NOTHING here audits their empty/refusal arms. Drive them in "
            + "`runJsonArm`, or state in `notDriven` why they cannot be driven and what reading their arms "
            + "found (`.github#1688` AC 4):\n  "
            + String.concat "\n  " unaudited
        )

        Assert.True(
            List.isEmpty stale,
            "these verbs are audited here but no longer admit `--json` — drop the row rather than leaving a "
            + "fixture that describes a projection the engine does not have:\n  "
            + String.concat "\n  " stale
        )

    [<Fact>]
    let ``.github#1688 the take receipt survives its own sweep — kind:none, and no prose`` () =
        // WHAT THIS ADDS OVER THE #1525 LEG ABOVE IS SMALL, AND SAYING SO IS THE POINT. That leg already
        // asserts `kind:"none"` and EX_NONE for this same empty board; the only mechanical difference here
        // is the fixture, and since this argv names its worker explicitly, even the identity scrub cannot
        // change the outcome. So this is not independent evidence and is not offered as any.
        //
        // It is kept as the ANCHOR: the sweep above treats `take` as one row among fourteen and asserts
        // only "one document", which is a weaker claim than #1688's own arm deserves. This states the
        // stronger one in the sweep's own fixture, so a future narrowing of what the sweep calls a
        // document cannot quietly stop covering the verb the item was filed about.
        let code, out, _ =
            runJsonArm (emptyQueue ()) [ "take"; "--repo"; "FS.GG.SDD"; "--worker"; "otter-9c21"; "--json" ]

        let receipt = parsed out

        Assert.Equal("none", str "kind" receipt)
        Assert.DoesNotContain("nothing schedulable", out)
        Assert.Equal(5, code)
