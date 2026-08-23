namespace FS.GG.Coord.Cli.Tests

open System
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json
open Xunit
open FS.GG.Coord
open FS.GG.Coord.Types
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Transport
open FS.GG.Coord.Cli
open FS.GG.Coord.Cli.BoardOps

module ApplicationServiceTests =

    /// Scripted success worlds deliberately model the production route gate: a receipt binds the exact
    /// issue body revision.  Callers that mean to exercise missing/stale/unreadable evidence override the
    /// endpoint instead; they never inherit an implicit lightweight route from this helper.
    let private currentRouteComment (subject: string) (body: string) =
        StructuredFixtures.routeComment subject (Some DeliveryRoute.Lightweight) "fixture-route" None

    /// .github#2698 — a ledger holding exactly that receipt, as `Reads.commentBodies` reads it.
    ///
    /// Every `Status=Ready` write now passes `requireCurrentRouteIfReady`, so a scripted success world
    /// that PROMOTES a row must model a routed row or it is modelling the defect. These fixtures answered
    /// their comment endpoint with `[]`, which is precisely the state the new refusal exists to stop, and
    /// each one below is a leg whose subject is something else entirely (external-owner canonicalisation,
    /// the partial two-field receipt, the sentinel gate, the intent channel). Overriding the endpoint is
    /// the convention the doc comment above already prescribes for legs that mean the OTHER answer.
    let private routedLedger (subject: string) =
        JsonSerializer.Serialize [| {| id = 7900; body = currentRouteComment subject "" |} |]

    [<Fact>]
    let ``#2137 SDD delivery evidence accepts only the current implementationReady work package`` () =
        let current : DeliveryRoute.Receipt =
            { Schema = DeliveryRoute.Schema
              Subject = "FS-GG/.github#2137"
              SubjectRevision = "fixture"
              Route = Some DeliveryRoute.SddRequired
              Agent = "fixture-route"
              Timestamp = "2026-01-01T00:00:00Z"
              ReasonCodes = [ "fixture" ]
              Rationale = "fixture route receipt"
              DeclaredImpacts = [ "internal" ]
              ObservedFacts = [ "localized" ]
              SddWorkId = Some "2137-delivery-route"
              SpecHome = Some "work/2137-delivery-route/spec.md"
              RequiredGates = [ "implementationReady"; "analyze"; "verify"; "ship" ] }

        Assert.Empty(Client.sddEvidenceErrors current)

        let nonexistent =
            { current with
                SddWorkId = Some "does-not-exist"
                SpecHome = Some "work/does-not-exist/spec.md" }

        Assert.NotEmpty(Client.sddEvidenceErrors nonexistent)

    [<Fact>]
    let ``#2137 SDD readiness rejects a substituted work id and non-implementation-ready status`` () =
        Assert.NotEmpty(Client.sddReadinessEvidenceErrors "2137-delivery-route" """{"workId":"other-work","status":"implementationReady"}""")
        Assert.NotEmpty(Client.sddReadinessEvidenceErrors "2137-delivery-route" """{"workId":"2137-delivery-route","status":"analyzing"}""")

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
          BoardKind = None
          CommentCount = None
          Severity = Unset
          Phase = None
          CreatedAt = None
          SweptBody = None
          NodeId = None }

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
            """[{"number":1,"repo":"FS-GG/.github","title":"quote: \u0022kept\u0022","status":"Ready","class":null,"kind":null,"commentCount":null,"severity":"Unset","state":"OPEN"}]""",
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
            """[{"number":1,"repo":"FS-GG/.github","title":"a defect","status":"Ready","class":"defect","kind":null,"commentCount":null,"severity":"Unset","state":"OPEN"}]""",
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
        $"""{{"status":{{"name":"%s{status}"}},"blockedBy":%s{blocked},"content":{{"__typename":"Issue","number":%d{number},"title":"%s{title}","body":"","state":"%s{state}","repository":{{"nameWithOwner":"FS-GG/FS.GG.SDD"}}}}}}"""

    let private boardItemInWithBody status number title blockedBy state body =
        let row = boardItemIn status number title blockedBy state
        let encoded = System.Text.Json.JsonSerializer.Serialize body
        row.Replace("\"body\":\"\"", $"\"body\":%s{encoded}")

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
        (bodies: Map<int, string>)
        (holders: Map<int, string>)
        (ageMinutes: Map<int, int>)
        (pathRepos: Map<int, string>)
        (number: int)
        =
        let age = Map.tryFind number ageMinutes |> Option.defaultValue 0
        let ts = DateTime.UtcNow.AddMinutes(float -age).ToString "yyyy-MM-ddTHH:mm:ssZ"

        let route =
            Map.tryFind number bodies
            |> Option.map (fun body ->
                JsonSerializer.Serialize
                    {| id = 7000 + number
                       body = currentRouteComment $"FS-GG/FS.GG.SDD#%d{number}" body
                       user = {| login = "EHotwagner" |}
                       created_at = ts
                       updated_at = ts |})
            |> Option.toList

        let claim =
            match Map.tryFind number holders with
            | None -> []
            | Some worker ->
                let pathRepo =
                    Map.tryFind number pathRepos
                    |> Option.map (fun repo -> $" pathRepo=%s{repo}")
                    |> Option.defaultValue ""

                [ $"""{{"id":%d{8000 + number},"body":"<!-- fsgg:claim worker=%s{worker} lease=120%s{pathRepo} -->\nheld","user":{{"login":"EHotwagner"}},"created_at":"%s{ts}","updated_at":"%s{ts}"}}""" ]

        "[" + String.concat "," (route @ claim) + "]"

    /// .github#2300 repair 2: the SAME route-marker-then-claim-marker thread `commentsAgedScoped` builds
    /// for the REST `/comments` endpoint, as bare BODIES rather than full comment JSON — what the bounded
    /// GraphQL `comments(last: N)` read now serves instead of `Reads.commentBodies`'s unbounded REST scan.
    /// No fixture in this file ever builds a thread longer than two comments (a route marker and a claim
    /// marker), so `last` truncation is a no-op in every existing test here; it is still applied for real
    /// rather than ignored, so a future test that DOES build a deep thread gets correct behaviour for
    /// free instead of a silent gap.
    let private recentCommentBodiesScoped
        (bodies: Map<int, string>)
        (holders: Map<int, string>)
        (pathRepos: Map<int, string>)
        (number: int)
        (last: int)
        =
        let route =
            Map.tryFind number bodies
            |> Option.map (fun body -> currentRouteComment $"FS-GG/FS.GG.SDD#%d{number}" body)
            |> Option.toList

        let claim =
            match Map.tryFind number holders with
            | None -> []
            | Some worker ->
                let pathRepo =
                    Map.tryFind number pathRepos
                    |> Option.map (fun repo -> $" pathRepo=%s{repo}")
                    |> Option.defaultValue ""

                [ $"<!-- fsgg:claim worker=%s{worker} lease=120%s{pathRepo} -->\nheld" ]

        (route @ claim) |> List.rev |> List.truncate last |> List.rev

    let private commentsAged bodies holders ageMinutes number =
        commentsAgedScoped bodies holders ageMinutes Map.empty number

    let private commentsFor bodies (holders: Map<int, string>) (number: int) = commentsAged bodies holders Map.empty number

    let private ok (body: string) : Errors.IoResult<Response> =
        Ok
            { Status = 200
              Body = body
              ETag = None
              NextLink = None; Headers = Map.empty }

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
        /// issue number → the number of an OPEN `item/<n>-*` PR on it (.github#2678). Empty for every
        /// pre-existing caller, and empty means the `/pulls` arm below is not served AT ALL rather than
        /// served as `[]` — `Reads.prAlive` reads those two answers differently (a failed read is
        /// `LivenessUnknown`, an empty list falls through to the #1055 branch probe), so an empty map has
        /// to leave every existing world byte-identical to what it was before this parameter existed.
        (itemPrs: Map<int, int>)
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
            // .github#2300 repair 2: the bounded route-marker search (`Reads.recentCommentBodies`) —
            // served from the SAME `bodies`/`holders`/`pathRepos` the REST `/comments` arm below reads,
            // via `recentCommentBodiesScoped`.
            | "POST", "graphql" when
                (match req.Body with
                 | Query(document, _) -> document.Contains "comments(last:"
                 | _ -> false)
                ->
                match req.Body with
                | Query(_, variables) ->
                    let numberVar =
                        variables
                        |> List.tryFind (fun (k, _) -> k = "number")
                        |> Option.bind (fun (_, v) -> match v with VNumber n -> Some(int n) | _ -> None)

                    let lastVar =
                        variables
                        |> List.tryFind (fun (k, _) -> k = "last")
                        |> Option.bind (fun (_, v) -> match v with VNumber n -> Some(int n) | _ -> None)

                    match numberVar, lastVar with
                    | Some n, Some last ->
                        let recent =
                            recentCommentBodiesScoped bodies holders pathRepos n last
                            |> List.map (fun body -> {| body = body |})
                            |> JsonSerializer.Serialize

                        let payload =
                            "{\"data\":{\"repository\":{\"issue\":{\"comments\":{\"nodes\":"
                            + recent
                            + "}}}},\"rateLimit\":{\"cost\":1,\"remaining\":4977}}"

                        ok payload
                    | _ -> Error(Errors.NotFound "the recent-comments query is missing owner/repo/number/last variables")
                | _ -> Error(Errors.NotFound "a graphql call with no document")
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
                // A closed item remains on the board, but GitHub's open-issues endpoint must not smuggle it
                // into `activeCollisions`.  #2250 needs the gate to reach that holder through the board
                // scan, exactly as production does.
                |> List.filter (fun (_, body) -> not (body.Contains("<!-- fixture:closed -->")))
                |> List.map (fun (n, body) -> {| number = n; state = "open"; body = body |})
                |> JsonSerializer.Serialize
                |> ok
            // THE REPO'S OPEN PRs (`Reads.prAlive`, #651/.github#2678). Served ONLY when a fixture asked
            // for one: this is what makes a markerless row's `itemPr` reach the snapshot, which is the
            // exact shape — work in flight with no marker on the issue — that used to be counted as an
            // occupied implementer slot.
            | "GET", "repos/FS-GG/FS.GG.SDD/pulls" when not (Map.isEmpty itemPrs) ->
                itemPrs
                |> Map.toList
                |> List.map (fun (issue, pr) ->
                    {| number = pr
                       head = {| ref = $"item/%d{issue}-fixture" |} |})
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
                let readable = commentsAgedScoped bodies holders markerAge pathRepos n

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
            Map.empty
            offBoard
            incomplete
            sayFails

    /// `worldOf` plus open `item/<n>-*` PRs, keyed by the issue they belong to (.github#2678). The one
    /// world in which a MARKERLESS row reaches the snapshot carrying an `itemPr`, which is the row the
    /// implementer-slot count used to swallow.
    let private worldOfWithItemPrsAged statusFor bodies holders markerAge itemPrs =
        worldOfWithScopesAndIncomplete
            statusFor
            bodies
            holders
            markerAge
            Map.empty
            itemPrs
            Set.empty
            Set.empty
            false

    let private worldOfWithItemPrs statusFor bodies holders itemPrs =
        worldOfWithItemPrsAged statusFor bodies holders Map.empty itemPrs

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
            Map.empty
            Set.empty
            Set.empty
            false

    let private context (transport: Fake.Recorder) : Kernel.Context =
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
    let ``#2155 a selected external row enters claim with its canonical owner, never a compact display ref`` () =
        // `Short` is deliberately ambiguous here: both rows render `rogue3#96`.  The scheduler's selected
        // `Item.Ref` is not ambiguous, so this is the boundary `take` must preserve into the mutating
        // command.  Before the repair it passed `[ "rogue3#96" ]`, which `claim` resolved as
        // `FS-GG/rogue3#96` and never contacted the selected external issue.
        let item =
            Snapshot.parse
                """{"schema":"fsgg.coord.snapshot/1","allowBacklog":false,"items":[{"owner":"EHotwagner","repo":"rogue3","number":96,"status":"Ready","state":"OPEN","body":"Paths: src/FS.GG.Coord.Cli/"}]}"""
            |> Result.defaultWith (fun errors -> failwithf "external-owner fixture did not parse: %A" errors)
            |> fun request -> request.Candidates |> List.exactlyOne |> fun candidate -> candidate.Item

        Assert.Equal<string list>([ "EHotwagner/rogue3#96" ], Client.claimArgsForSelected item)

    // ---- .github#2155: the REAL batch -> take -> claim/CAS route keeps the external owner -----------

    module private ExternalOwnerTakeFixture =

        type Failure =
            | Healthy
            | ExternalReadFails
            | ExternalClaimPostFails

        type World =
            { Transport: Fake.Recorder
              DefaultMutated: unit -> bool
              ExternalMutated: unit -> bool }

        let private variable name variables =
            variables
            |> List.tryPick (fun (n, value) -> if n = name then Some value else None)

        let private asString = function
            | VString value
            | VId value -> value
            | value -> failwithf "expected string GraphQL variable, got %A" value

        let create failure =
            let mutable externalMarker: string option = None
            let mutable externalStatus = "Ready"
            let mutable defaultMutated = false
            let mutable externalMutated = false

            let boardItems () =
                $"""{{"data":{{"organization":{{"projectV2":{{"items":{{"pageInfo":{{"hasNextPage":false,"endCursor":null}},"nodes":[{{"id":"PVTI_default96","status":{{"name":"Blocked"}},"blockedBy":null,"content":{{"__typename":"Issue","number":96,"title":"default twin","state":"OPEN","repository":{{"nameWithOwner":"FS-GG/rogue3"}}}}}},{{"id":"PVTI_external96","status":{{"name":"%s{externalStatus}"}},"blockedBy":null,"content":{{"__typename":"Issue","number":96,"title":"external target","state":"OPEN","repository":{{"nameWithOwner":"EHotwagner/rogue3"}}}}}}]}}}}}}}},"rateLimit":{{"cost":1,"remaining":4977}}}}"""

            // The issue-side connection, AS THE LIVE API ANSWERS IT (.github#2204).
            //
            // For the board owner's own issue it carries the Coordination row. For `EHotwagner/rogue3#96` it
            // carries ONLY that repository's own user project (7) and OMITS the organization board's row
            // entirely — measured against the fleet token on #96, #75 and `EHotwagner/S.I.R.#138`. This
            // fixture used to answer project 12 for both owners, which quietly made the cross-owner arm
            // untestable: it modelled an API that does not filter. Every external read must now reach the
            // board side, and this arm is the tripwire that says so.
            let projectItems owner includeStatus =
                let id, project, status =
                    if owner = "EHotwagner" then
                        "PVTI_user7", 7, "Backlog"
                    else
                        "PVTI_default96", 12, "Blocked"

                let field =
                    if includeStatus then
                        $""","fieldValueByName":{{"name":"%s{status}"}}"""
                    else
                        ""

                $"""{{"data":{{"repository":{{"issue":{{"projectItems":{{"nodes":[{{"id":"%s{id}","project":{{"number":%d{project}}}%s{field}}}]}}}}}}}},"rateLimit":{{"cost":1,"remaining":4977}}}}"""

            /// The board-side field read: one point on the resolved row's own node.
            let externalField (field: string) =
                let value =
                    if field = "Status" then
                        $"""{{"name":"%s{externalStatus}"}}"""
                    else
                        "null"

                $"""{{"data":{{"node":{{"fieldValueByName":%s{value}}}}},"rateLimit":{{"cost":1,"remaining":4977}}}}"""

            /// The bare bodies `comments` below serializes into full REST comment JSON — the single
            /// source both that REST arm and the new bounded GraphQL route read (.github#2300 repair 2)
            /// build their answer from, so an external claim posted mid-test (`externalMarker`) is visible
            /// to both identically.
            let bodies owner =
                let source = "Paths: src/Rogue3/"
                let subject = $"%s{owner}/rogue3#96"
                let route = currentRouteComment subject source

                let markers =
                    match owner, externalMarker with
                    | "EHotwagner", Some body -> [ body ]
                    | _ -> []

                [ route ] @ markers

            let comments owner =
                let timestamp = DateTime.UtcNow.ToString "yyyy-MM-ddTHH:mm:ssZ"
                bodies owner
                |> List.mapi (fun index body ->
                    {| id = 9195 + index
                       body = body
                       user = {| login = "EHotwagner" |}
                       created_at = timestamp
                       updated_at = timestamp |})
                |> JsonSerializer.Serialize

            let transport =
                Fake.Recorder(fun (req: Request) ->
                    let path = req.Path.Trim '/'

                    match req.Method, path with
                    | "GET", "rate_limit" -> ok """{"resources":{"graphql":{"remaining":4980,"limit":5000}}}"""
                    | "POST", "graphql" ->
                        match req.Body with
                        // .github#2300 repair 2: the bounded route-marker search. `owner` distinguishes
                        // the two same-numbered twins exactly as the REST arms below key on the URL's
                        // owner segment.
                        | Query(document, variables) when document.Contains "comments(last:" ->
                            let ownerVar =
                                variable "owner" variables |> Option.map asString
                            let lastVar =
                                variable "last" variables
                                |> Option.map (function
                                    | VNumber n -> int n
                                    | value -> failwithf "expected numeric `last`, got %A" value)

                            match ownerVar, lastVar with
                            | Some owner, Some last ->
                                let recent =
                                    bodies owner
                                    |> List.rev
                                    |> List.truncate last
                                    |> List.rev
                                    |> List.map (fun body -> {| body = body |})
                                    |> JsonSerializer.Serialize

                                let payload =
                                    "{\"data\":{\"repository\":{\"issue\":{\"comments\":{\"nodes\":"
                                    + recent
                                    + "}}}},\"rateLimit\":{\"cost\":1,\"remaining\":4977}}"

                                ok payload
                            | _ -> Error(Errors.NotFound $"the recent-comments query is missing owner/last: %A{variables}")
                        | Query(document, variables) when document.Contains "projectsV2" ->
                            ok """{"data":{"organization":{"projectsV2":{"nodes":[{"number":12,"title":"Coordination","id":"PVT_coord"}]}}},"rateLimit":{"cost":1,"remaining":4977}}"""
                        | Query(document, _) when document.Contains "fields(first" ->
                            // `Backlog` is a REAL option here on purpose: it is the #1823 default `add`
                            // applies over a column it believes is unset, and an assertion that it was NOT
                            // written is worthless if the write could not have resolved an option id anyway.
                            ok """{"data":{"organization":{"projectV2":{"fields":{"nodes":[{"id":"PVTSSF_status","name":"Status","dataType":"SINGLE_SELECT","options":[{"id":"opt_backlog","name":"Backlog"},{"id":"opt_ready","name":"Ready"},{"id":"opt_wip","name":"In progress"},{"id":"opt_blocked","name":"Blocked"}]},{"id":"PVTF_blocked","name":"Blocked by","dataType":"TEXT"}]}}}},"rateLimit":{"cost":1,"remaining":4977}}"""
                        | Query(document, _) when document.Contains "node(id: $projectId)" ->
                            ok
                                """{"data":{"node":{"items":{"pageInfo":{"hasNextPage":false,"endCursor":null},"nodes":[{"id":"PVTI_default96","content":{"number":96,"repository":{"nameWithOwner":"FS-GG/rogue3"}}},{"id":"PVTI_external96","content":{"number":96,"repository":{"nameWithOwner":"EHotwagner/rogue3"}}}]}}}}"""
                        | Query(document, variables) when document.Contains "node(id: $itemId)" ->
                            let item = variable "itemId" variables |> Option.map asString |> Option.defaultValue ""
                            let field = variable "field" variables |> Option.map asString |> Option.defaultValue ""

                            if item <> "PVTI_external96" then
                                Error(Errors.NotFound $"the board-side field read addressed the wrong row: %s{item}")
                            else
                                ok (externalField field)
                        | Query(document, _) when document.Contains "items(first" -> ok (boardItems ())
                        | Query(document, variables) when document.Contains "projectItems" ->
                            let owner = variable "owner" variables |> Option.map asString |> Option.defaultValue ""
                            ok (projectItems owner (document.Contains "fieldValueByName"))
                        | Query(document, variables) when document.Contains "updateProjectV2ItemFieldValue" ->
                            let item = variable "itemId" variables |> Option.map asString |> Option.defaultValue ""

                            if item = "PVTI_external96" then
                                externalMutated <- true
                                externalStatus <- "In progress"
                            elif item = "PVTI_default96" then
                                defaultMutated <- true

                            ok """{"data":{"updateProjectV2ItemFieldValue":{"projectV2Item":{"id":"PVTI_external96"}}}}"""
                        | Query(document, _) -> Error(Errors.NotFound $"unserved GraphQL: %s{document}")
                        | _ -> Error(Errors.NotFound "graphql call without a query")
                    | "GET", "repos/FS-GG/rogue3/issues" ->
                        ok """[{"number":96,"state":"open","body":"Paths: src/Rogue3/"}]"""
                    | "GET", "repos/EHotwagner/rogue3/issues" when failure = ExternalReadFails ->
                        Error(Errors.Transport "external owner issue list unavailable")
                    | "GET", "repos/EHotwagner/rogue3/issues" ->
                        ok """[{"number":96,"state":"open","body":"Paths: src/Rogue3/"}]"""
                    | "GET", "repos/FS-GG/rogue3/issues/96" ->
                        ok """{"number":96,"state":"open","body":"Paths: src/Rogue3/"}"""
                    | "GET", "repos/EHotwagner/rogue3/issues/96" when failure = ExternalReadFails ->
                        Error(Errors.Transport "external owner issue unavailable")
                    | "GET", "repos/EHotwagner/rogue3/issues/96" ->
                        ok """{"number":96,"state":"open","body":"Paths: src/Rogue3/"}"""
                    | "GET", "repos/FS-GG/rogue3/issues/96/comments" -> ok (comments "FS-GG")
                    | "GET", "repos/EHotwagner/rogue3/issues/96/comments" when failure = ExternalReadFails ->
                        Error(Errors.Transport "external owner claim read unavailable")
                    | "GET", "repos/EHotwagner/rogue3/issues/96/comments" -> ok (comments "EHotwagner")
                    | "POST", "repos/FS-GG/rogue3/issues/96/comments" ->
                        defaultMutated <- true
                        ok """{"id":9195}"""
                    | "POST", "repos/EHotwagner/rogue3/issues/96/comments" when failure = ExternalClaimPostFails ->
                        Error(Errors.Http(502, "external owner claim post failed"))
                    | "POST", "repos/EHotwagner/rogue3/issues/96/comments" ->
                        externalMutated <- true

                        match req.Body with
                        | Json payload ->
                            use document = JsonDocument.Parse payload
                            externalMarker <- Some(document.RootElement.GetProperty("body").GetString())
                        | _ -> failwith "claim POST did not carry JSON"

                        ok """{"id":9196}"""
                    | "GET", p when p.EndsWith "/pulls" -> ok "[]"
                    // .github#2645 — `Reads.prAlive`'s SECOND probe. With no open PR it asks whether a
                    // pushed `item/96-*` branch exists, and an UNREADABLE answer there is `LivenessUnknown`,
                    // never "no PR" — which `claim` now correctly refuses to project a column from. Neither
                    // twin has such a branch, so both answer the empty ref list.
                    | "GET", p when p.Contains "/git/matching-refs/heads/item/96-" -> ok "[]"
                    | method', target -> Error(Errors.NotFound $"unserved twin-owner request: %s{method'} %s{target}"))

            { Transport = transport
              DefaultMutated = fun () -> defaultMutated
              ExternalMutated = fun () -> externalMutated }

        let run (transport: Fake.Recorder) (args: string list) : int * string * string =
            let cache = Path.Combine(Path.GetTempPath(), "fsgg-2155-twins-" + Guid.NewGuid().ToString "n")
            let previousCache = Environment.GetEnvironmentVariable "FSGG_COORD_CACHE"
            let identityVars =
                [ "FSGG_WORKER"; "CLAUDE_CODE_SESSION_ID"; "OPENCODE_SESSION_ID"; "FSGG_AGENT_SESSION_ID" ]
            let previousIdentity =
                identityVars |> List.map (fun name -> name, Environment.GetEnvironmentVariable name)
            let stdout, stderr = Console.Out, Console.Error
            use capturedOut = new StringWriter()
            use capturedErr = new StringWriter()

            try
                Directory.CreateDirectory cache |> ignore
                Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", cache)
                previousIdentity |> List.iter (fun (name, _) -> Environment.SetEnvironmentVariable(name, null))
                Console.SetOut capturedOut
                Console.SetError capturedErr
                let opts = options args
                let code =
                    match opts.Command with
                    | Options.BatchCmd -> Client.batch (context transport) opts
                    | Options.Take -> Client.take (context transport) opts
                    | Options.Claim -> Client.claim (context transport) opts
                    | Options.Release -> Client.release (context transport) opts
                    | Options.Add -> Handlers.addCmd (context transport) opts
                    | command -> failwithf "twin-owner fixture drives batch/take/claim/release/add, got %A" command
                Console.Out.Flush()
                Console.Error.Flush()
                code, capturedOut.ToString(), capturedErr.ToString()
            finally
                Console.SetOut stdout
                Console.SetError stderr
                Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", previousCache)
                previousIdentity |> List.iter (fun (name, value) -> Environment.SetEnvironmentVariable(name, value))
                try Directory.Delete(cache, true) with _ -> ()

    [<Fact>]
    let ``#2155 batch then bare take claims only the selected external twin and reports its canonical identity`` () =
        let world = ExternalOwnerTakeFixture.create ExternalOwnerTakeFixture.Healthy
        let batchCode, batchOut, _ = ExternalOwnerTakeFixture.run world.Transport [ "batch"; "--json" ]
        Assert.Equal(0, batchCode)
        Assert.Equal("[\"EHotwagner/rogue3#96\"]" + Environment.NewLine, batchOut)

        let takeCode, takeOut, takeErr =
            ExternalOwnerTakeFixture.run world.Transport [ "take"; "--worker"; "otter-2155"; "--json" ]

        Assert.Equal(0, takeCode)
        use receipt = JsonDocument.Parse takeOut
        Assert.Equal("claimed", receipt.RootElement.GetProperty("kind").GetString())
        Assert.Equal("EHotwagner/rogue3", receipt.RootElement.GetProperty("repo").GetString())
        Assert.Equal(96, receipt.RootElement.GetProperty("number").GetInt32())
        Assert.True(receipt.RootElement.GetProperty("markerObserved").GetBoolean())
        Assert.Equal("In progress", receipt.RootElement.GetProperty("status").GetString())
        Assert.True(receipt.RootElement.GetProperty("converged").GetBoolean())
        Assert.True(world.ExternalMutated())
        Assert.False(world.DefaultMutated())
        Assert.DoesNotContain("FS-GG/rogue3#96", takeErr)

    [<Theory>]
    [<InlineData(true)>]
    [<InlineData(false)>]
    let ``#2155 an unavailable external read or claim mutation fails closed and never reports recovery``
        (readFails: bool)
        =
        let failure =
            if readFails then ExternalOwnerTakeFixture.ExternalReadFails
            else ExternalOwnerTakeFixture.ExternalClaimPostFails

        let world = ExternalOwnerTakeFixture.create failure
        let code, output, _ =
            ExternalOwnerTakeFixture.run world.Transport [ "take"; "--worker"; "otter-2155"; "--json" ]

        Assert.NotEqual(0, code)
        Assert.False(world.DefaultMutated())

        if output.Trim() <> "" then
            use receipt = JsonDocument.Parse output
            Assert.False(receipt.RootElement.TryGetProperty("converged") |> fst)

    [<Fact>]
    let ``#2204 a bare release RESTORES a cross-owner item's pre-claim column`` () =
        // CONSEQUENCE 1 OF THE FALSE `Ok None`. `release` without `--status` derives its decision from the
        // live column: `Ok None` becomes `Preserve None`, printed as "no column to reset", and the row is
        // LEFT at the `In progress` the claim wrote — unschedulable, and looking held by nobody.
        let world = ExternalOwnerTakeFixture.create ExternalOwnerTakeFixture.Healthy

        let claimCode, _, claimErr =
            ExternalOwnerTakeFixture.run world.Transport [ "claim"; "EHotwagner/rogue3#96"; "--worker"; "otter-2204" ]

        if claimCode <> 0 then failwithf "the external claim failed: %s" claimErr

        let releaseCode, releaseOut, releaseErr =
            ExternalOwnerTakeFixture.run world.Transport [ "release"; "EHotwagner/rogue3#96"; "--worker"; "otter-2204" ]

        if releaseCode <> 0 then failwithf "the external release failed: %s" releaseErr

        // The pre-claim column was `Ready`, so the restore names it. The pre-repair output was the bare
        // "(no column to reset — not on this board, or no Status set)" for a row that is on the board.
        Assert.Contains("released rogue3#96 → Ready", releaseOut)
        Assert.DoesNotContain("no column to reset", releaseOut)

    [<Fact>]
    let ``#2204 add does not lay the Backlog default over a live cross-owner column`` () =
        // CONSEQUENCE 2. `add` reads the column and, on `Ok None`, writes the #1823 `Backlog` default. The
        // comment above that call names this as "the ONE direction this change destroys information,
        // asserted to be impossible rather than made so" — and for a cross-owner row it was certain.
        let world = ExternalOwnerTakeFixture.create ExternalOwnerTakeFixture.Healthy

        let addCode, addOut, addErr =
            ExternalOwnerTakeFixture.run world.Transport [ "add"; "EHotwagner/rogue3#96"; "--worker"; "otter-2204" ]

        if addCode <> 0 then failwithf "the external add failed: %s" addErr

        // The row's live column is `Ready`. `add` is idempotent over it: no Status write at all, and in
        // particular never the default.
        Assert.False(
            world.Transport.Logged "--single-select-option-id opt_backlog",
            $"`add` wrote the Backlog default over a live external column — stdout: %s{addOut}")

        Assert.False(
            world.Transport.Logged "--id PVTI_default96",
            "`add` addressed the default-owner twin")

    [<Fact>]
    let ``#2127 driver review evidence is bound to the PR comment endpoint`` () =
        // This drives the real `Client.driver` handler through scan, the PR liveness probe, the green
        // landability read and the PR conversation.  The backing issue deliberately has no review marker:
        // a handler that accidentally reads #2127's comments for review evidence therefore produces zero.
        let mutable head = String.replicate 40 "3"
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
                    if claimed then
                        let timestamp = DateTime.UtcNow.ToString "yyyy-MM-ddTHH:mm:ssZ"
                        let payload =
                            [ {| id = 7127
                                 body = currentRouteComment "FS-GG/.github#2127" "Paths: src/FS.GG.Coord.Core/Driver.fs"
                                 user = {| login = "fixture" |}
                                 created_at = timestamp
                                 updated_at = timestamp |}
                              {| id = 8127
                                 body = "<!-- fsgg:claim worker=worker-2127 lease=120 -->"
                                 user = {| login = "fixture" |}
                                 created_at = timestamp
                                 updated_at = timestamp |} ]
                        ok (JsonSerializer.Serialize payload)
                    else ok "[]"
                | "GET", "repos/FS-GG/.github/pulls" -> ok """[{"number":2140,"head":{"ref":"item/2127-driver-transition-state-machine"}}]"""
                | "GET", "repos/FS-GG/.github/pulls/2140" -> ok $"""{{"number":2140,"state":"open","merged":false,"mergeable":true,"mergeable_state":"clean","head":{{"ref":"item/2127-driver-transition-state-machine","sha":"%s{head}"}},"base":{{"ref":"main"}}}}"""
                | "GET", "repos/FS-GG/.github/pulls/2140/files" -> ok "[]"
                | "GET", path when path = $"repos/FS-GG/.github/commits/%s{head}" ->
                    ok """{"commit":{"message":"ordinary driver change"}}"""
                | "GET", "repos/FS-GG/.github/actions/runs" -> ok """{"total_count":1,"workflow_runs":[{"path":".github/workflows/build.yml","event":"pull_request","head_branch":"item/2127-driver-transition-state-machine","run_number":1,"status":"completed","conclusion":"success","check_suite_id":1,"pull_requests":[{"number":2140}]}]}"""
                | "GET", path when path.StartsWith "repos/FS-GG/.github/commits/" && path.EndsWith "/check-runs" -> ok """{"total_count":1,"check_runs":[{"name":"build","check_suite":{"id":1},"status":"completed","conclusion":"success"}]}"""
                | "GET", "repos/FS-GG/.github/issues/2140/comments" ->
                    let comments =
                        StructuredFixtures.acceptedReviewComments "FS-GG/.github#2127/pr/2140" head "shrike-7194"
                        |> List.map (fun (id, url, body) -> {| id = id; html_url = url; body = body |})
                    ok (JsonSerializer.Serialize comments)
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
            let receipt schema approved observedAt source =
                let observation kind outcome =
                    let id = Driver.observationReceiptId kind observedAt source outcome
                    $"""{{"kind":"%s{kind}","observedAt":%d{observedAt},"sourceSha":"%s{source}","outcome":"%s{outcome}","receiptId":"%s{id}"}}"""
                let observations =
                    [ "reconcile-dry-run", "clean"; "reconcile-apply", "applied-or-not-needed"
                      "reconcile-fresh", "clean"; "triage", "fresh"; "engine-currency", "current-scoped" ]
                    |> List.map (fun (kind, outcome) -> observation kind outcome)
                    |> String.concat ","
                $"""{{"schema":"%s{schema}","observedAt":%d{observedAt},"sourceSha":"%s{source}","complete":true,"consolidationApproved":%s{if approved then "true" else "false"},"observations":[%s{observations}],"contentIntakes":[],"contentDispositions":[]}}"""
            let now = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            let schema = Protocol.ledgerPolicy.Schema
            File.WriteAllText(receiptPath, receipt schema false now sourceSha)
            let _, consolidate, _ = invoke (Some receiptPath)
            use consolidateDoc = JsonDocument.Parse consolidate
            Assert.True(consolidateDoc.RootElement.GetProperty("receiptValid").GetBoolean())
            Assert.Equal("Consolidate", consolidateDoc.RootElement.GetProperty("action").GetString())
            File.WriteAllText(receiptPath, receipt schema true now sourceSha)
            let _, dispatch, _ = invoke (Some receiptPath)
            use dispatchDoc = JsonDocument.Parse dispatch
            Assert.Equal("DispatchWave 3", dispatchDoc.RootElement.GetProperty("action").GetString())
            File.WriteAllText(receiptPath, receipt "fsgg.coord.planning-receipt/0" true now sourceSha)
            let _, wrongSchema, _ = invoke (Some receiptPath)
            use wrongSchemaDoc = JsonDocument.Parse wrongSchema
            Assert.False(wrongSchemaDoc.RootElement.GetProperty("receiptValid").GetBoolean())
            File.WriteAllText(receiptPath, receipt schema true now sourceSha)
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
            File.WriteAllText(receiptPath, receipt schema true now claimedSource)
            let _, resume, _ = invoke (Some receiptPath)
            use resumeDoc = JsonDocument.Parse resume
            Assert.Equal("ResumeSameWorker", resumeDoc.RootElement.GetProperty("action").GetString())
            claimed <- false
            File.WriteAllText(receiptPath, receipt schema true (now - 301L) sourceSha)
            let _, stale, _ = invoke (Some receiptPath)
            use staleDoc = JsonDocument.Parse stale
            Assert.False(staleDoc.RootElement.GetProperty("receiptValid").GetBoolean())
            Assert.Equal("RepairEngineCurrency", staleDoc.RootElement.GetProperty("action").GetString())
            File.WriteAllText(receiptPath, receipt schema true now "wrong-snapshot")
            let _, mismatched, _ = invoke (Some receiptPath)
            use mismatchedDoc = JsonDocument.Parse mismatched
            Assert.False(mismatchedDoc.RootElement.GetProperty("receiptValid").GetBoolean())
            Assert.Equal("RepairEngineCurrency", mismatchedDoc.RootElement.GetProperty("action").GetString())
            let malformed = (receipt schema true now sourceSha).Replace("\"receiptId\":\"", "\"receiptId\":\"malformed-")
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

    /// .github#2144's escalated finding, driven through the real `Client.driver` handler.
    ///
    /// Both runs change EXACTLY ONE file and carry a canonical passing chain that opts out with
    /// `diff-audit-required: false` and submits no receipt.  The only difference between them is how many
    /// quoted occurrences the single file's diff contains.  The old code passed the changed-FILE count
    /// (1, in both) to the occurrence threshold, so both were mechanically not-required and both merged
    /// green.  Measuring occurrences separates them, which is the whole repair.
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
                FS.GG.Coord.Cli.Lifecycle.LiveHandlers.followupAudit
                    (context transport)
                    (options [ "followup"; "audit"; "--apply" ])

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
                FS.GG.Coord.Cli.Lifecycle.LiveHandlers.followupAudit
                    (context transport)
                    (options [ "followup"; "audit"; "--apply" ])

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

    /// `internal`, not `private` — #2306's `WidenRefusalTests.fs` drives the same fixture rather than
    /// duplicating GraphQL board-bootstrap mocking that has nothing to do with what it tests.
    let internal run (transport: Fake.Recorder) (args: string list) : int * string =
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

    /// `internal` — reused by #2306's `WidenRefusalTests.fs` (see `run`, above).
    let internal disjointWorld () =
        world (Map.ofList [ 74, "Paths: scripts/fsgg-coord" ]) (Map.ofList [ 74, "kite-469" ]) false

    /// #74 is ours; #75 is a live neighbour reserving the very path we are about to declare.
    /// `internal` — reused by #2306's `WidenRefusalTests.fs` (see `run`, above).
    let internal overlappingWorld (sayFails: bool) =
        world
            (Map.ofList [ 74, "Paths: scripts/fsgg-coord"; 75, "Paths: src/Shared.fs" ])
            (Map.ofList [ 74, "kite-469"; 75, "otter-9c21" ])
            sayFails

    /// `internal` — reused by #2306's `WidenRefusalTests.fs` (see `run`, above).
    let internal parsed (out: string) : JsonElement =
        // A `--json` projection is a SINGLE object and nothing else on the stream. Parsing the WHOLE of
        // stdout — not a line grepped out of it — is what makes "and no prose" an assertion rather than a
        // hope: prose above or below the object is a parse failure here.
        try
            JsonDocument.Parse(out.Trim()).RootElement
        with e ->
            failwithf "stdout was not one JSON document — this is the #1517 defect.\nstdout was:\n%s\n(%s)" out e.Message

    /// `internal` — reused by #2306's `WidenRefusalTests.fs` (see `run`, above).
    let internal str (name: string) (el: JsonElement) = el.GetProperty(name).GetString()

    /// `internal` — reused by #2323 round 1's repair legs in #2306's `WidenRefusalTests.fs` (see `run`,
    /// above), which assert the JSON `paths` array on a refused update rather than merely its `verdict`.
    let internal strings (name: string) (el: JsonElement) =
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
        // #2250 adds the cached board scan so closed-but-unstamped holders participate.  Cold: bootstrap
        // (two resolver queries) plus one page; no per-holder GraphQL fan-out.
        Assert.Equal(3, world.GraphQlCalls)

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
        Assert.Equal(3, world.GraphQlCalls)

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
    [<InlineData("widen", "add paths to")>]
    [<InlineData("set-paths", "replace")>]
    let ``the OVERLAP human projection puts nothing else on stdout`` (verb: string, action: string) =
        let code, out =
            run (overlappingWorld false) [ verb; "FS.GG.SDD#74"; "--worker"; "kite-469"; "--paths"; "src/Shared.fs" ]

        // The human OVERLAP branch has always written its detail to stderr and only the receipt line to
        // stdout. That is the split #1517 fixes FOR MACHINES by putting the detail in the object — it does
        // not move a byte of the human form, which existing recipes read.
        //
        // #2306 — THE RECEIPT LINE ITSELF CHANGED, because the OLD line ("widened FS.GG.SDD#74 → Paths: …")
        // claimed a completed write, and a refused widen no longer writes anything. Pinned as an EQUALITY,
        // like the DISJOINT leg above: a `DoesNotContain "OVERLAP"` would still pass if the Text branch
        // also emitted the JSON object, whose verdict is the lowercase `"overlap"`.
        let would =
            if verb = "widen" then
                "scripts/fsgg-coord, src/Shared.fs"
            else
                "src/Shared.fs"

        Assert.Equal(
            $"refused to %s{action} FS.GG.SDD#74's touch-set → Paths: unchanged (%s{would} would overlap a live claim)"
            + Environment.NewLine,
            out
        )

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
    let ``#1740 cause 1: a claim landing inside the scan-cache window collides, and the cached board scan preserves the result`` () =
        // THE NAME CHANGED WITH THE MECHANISM, AND IT HAD TO. As #1740 wrote it, this leg drove the column
        // through a stale cache window and asserted the cache tier fixed it. Since #1779 nothing on this
        // path reads a board, so the `column` ref below is a DEAD INPUT — the assertion would pass
        // identically with it frozen. #2250 deliberately restores the scheduler's cached board scan so a
        // closed-but-unstamped holder reaches this gate too; this leg pins that the second command reuses
        // it rather than turning a cached scan into per-command GraphQL fan-out.
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
            //    the worker was told it may edit `src/Shared.fs`. THAT was the false DISJOINT. The open
            //    issue list still sees the marker regardless of the board cache, so this remains overlap.
            let code, out = runIn dir world (widenOnto "src/Shared.fs")

            let collision = soleCollision out
            Assert.Equal("FS.GG.SDD#75", str "ref" collision)
            Assert.Equal("otter-9c21", str "worker" collision)
            Assert.Equal<string list>([ "src/Shared.fs" ], strings "sharedTokens" collision)
            Assert.Equal(6, code)

            // Two commands share one cached scan: two resolver calls plus one page, not six.  This is the
            // bounded #2250 cost and fails if the cache is bypassed or a per-row query appears.
            Assert.Equal(3, world.GraphQlCalls)
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

        Assert.Contains("FS-GG/FS.GG.SDD#76", scheduled world)
        Assert.Equal("disjoint", gateVerdict (contendedWorld "Blocked" (Map.ofList [ 74, "kite-469" ]) Map.empty))

    [<Fact>]
    let ``#2250: a CLOSED unstamped holder reserves on BOTH scheduler and collision-gate surfaces`` () =
        // #75 is the post-merge window: CLOSED on the board, not Done, with a live marker.  The REST
        // open-issues answer deliberately omits it (the fixture's `fixture:closed` switch), so the gate can
        // only find it by selecting the closed board row and reading its body.  #76 proves the scheduler
        // refuses that same holder; #74 drives the real `widen` gate.  KILLS: removing the closed-row arm
        // makes the gate DISJOINT while #76 remains refused, which is the exact production divergence.
        let bodies =
            Map.ofList
                [ 74, "Paths: scripts/fsgg-coord"
                  75, "Paths: src/Shared.fs\n<!-- fixture:closed -->"
                  76, "Paths: src/Shared.fs" ]

        let status n =
            match n with
            | 75 -> "In review"
            | 76 -> "Ready"
            | _ -> "In progress"

        let holders = Map.ofList [ 74, "kite-469"; 75, "curlew-8afd" ]
        let world = worldOf status bodies holders Map.empty Set.empty false

        Assert.DoesNotContain("FS-GG/FS.GG.SDD#76", scheduled world)
        Assert.Equal("overlap", gateVerdict (worldOf status bodies holders Map.empty Set.empty false))

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

            // #2250 reads the same cached board universe as the scheduler: bootstrap plus one page.
            Assert.Equal(3, world.GraphQlCalls)

            // REST: the issue-list read and one marker read for the ONE colliding row, plus the ONE write
            // `widen` still makes on an OVERLAP verdict — the courtesy notice to the colliding holder.
            // #2306 removed the OTHER write this count used to include: the body PATCH on the SUBJECT
            // item, which a refused widen must not issue (a refusal that mutates the declaration is
            // exactly the defect #2306 fixes). The number is pinned rather than bounded so that a
            // re-introduced per-row marker sweep — the ~74-reads-per-widen shape #1779 measured and
            // refused — cannot land quietly, and so that the removed PATCH cannot quietly come back either.
            Assert.Equal(6, world.RestCalls)
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
    let ``#1779/#2250: the collision scan spends one cached board scan and ONE marker read per COLLIDING row`` () =
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

        // #2250 pays the scheduler-aligned cached board universe once: two bootstrap resolver queries and
        // one page.  It must not regress to GraphQL per colliding row.
        Assert.Equal(3, world.GraphQlCalls)

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
            Assert.Equal(3, world.GraphQlCalls)
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
    let private typedCompletionComments (number: int) =
        let facts: Delivery.CompletionFacts =
            { HeadSha = "head"
              Merged = true
              MergeReachable = true
              IssueClosed = true
              BoardDone = false
              ClaimReleased = false
              PendingWrites = 0
              CleanupEligible = false
              ObligationsDeclared = true
              Obligations = [] }

        let receipt =
            Delivery.createCompletionReceipt
                $"FS-GG/FS.GG.SDD#%d{number}"
                number
                $"merge-%d{number}"
                (DateTimeOffset.Parse "2026-08-22T15:00:00Z")
                $"freshness-%d{number}"
                $"action-%d{number}"
                facts
            |> Result.defaultWith (String.concat "; " >> failwith)
            |> Delivery.encodeCompletionReceipt

        JsonSerializer.Serialize [| {| body = receipt |} |]

    [<Literal>]
    let private SubjectBoundTypedCompletion = "__subject-bound-typed-completion__"

    let private reconcileWorldWithQueriesAndComments
        (closed: int list)
        (rateLimited: Set<int>)
        (queries: ResizeArray<string> option)
        (comments: string) =
        // A successful GraphQL mutation is not itself evidence that the board projection changed.  This
        // fixture therefore models the projection separately: only a subsequent scan after an accepted
        // mutation observes Done.  The rate-limited item never reaches that transition.
        let mutable written: Map<int, string> = Map.empty
        let mutable reopened: Set<int> =
            if comments.Contains(Delivery.CompletionReceiptMarker, StringComparison.Ordinal) then
                Set.ofList closed
            else
                Set.empty
        let projectedStatus = if comments = "[]" then "In review" else "Done"
        let mutable storedCommentBodies =
            if comments = SubjectBoundTypedCompletion then
                []
            else
                use document = JsonDocument.Parse comments
                document.RootElement.EnumerateArray()
                |> Seq.choose (fun entry ->
                    match entry.TryGetProperty "body" with
                    | true, body when body.ValueKind = JsonValueKind.String -> Some(body.GetString())
                    | _ -> None)
                |> List.ofSeq

        let commentsJson number =
            if comments = SubjectBoundTypedCompletion then
                typedCompletionComments number
            else
                storedCommentBodies
                |> List.mapi (fun index body -> {| id = 9001 + index; body = body |})
                |> List.toArray
                |> JsonSerializer.Serialize

        let items () =
            closed
            |> List.map (fun n ->
                let status = Map.tryFind n written |> Option.defaultValue "In progress"
                let state = if reopened.Contains n then "OPEN" else "CLOSED"
                $"""{{"status":{{"name":"%s{status}"}},"blockedBy":null,"content":{{"__typename":"Issue","number":%d{n},"title":"item %d{n}","body":"","state":"%s{state}","repository":{{"nameWithOwner":"FS-GG/FS.GG.SDD"}}}}}}""")
            |> String.concat ","

        Fake.Recorder(fun (req: Request) ->
            match req.Method, req.Path.Trim '/' with
            | "GET", "rate_limit" -> ok """{"resources":{"graphql":{"remaining":4980,"limit":5000}}}"""
            | "POST", "graphql" ->
                match req.Body with
                | Query(document, variables) ->
                    queries |> Option.iter (fun captured -> captured.Add document)
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
                    elif document.Contains "node(id: $itemId)" then
                        let item =
                            variables
                            |> List.tryPick (fun (k, v) ->
                                match k, v with
                                | "itemId", VId id when id.StartsWith "PVTI_" ->
                                    match Int32.TryParse(id.Substring "PVTI_".Length) with
                                    | true, n -> Some n
                                    | _ -> None
                                | _ -> None)

                        let field =
                            variables
                            |> List.tryPick (fun (k, v) -> match k, v with | "field", VString name -> Some name | _ -> None)

                        match item, field with
                        | Some n, Some "Status" when closed |> List.contains n ->
                            let status = Map.tryFind n written |> Option.defaultValue "In progress"
                            ok $"""{{"data":{{"node":{{"fieldValueByName":{{"name":"%s{status}"}}}}}},"rateLimit":{{"cost":1,"remaining":4977}}}}"""
                        | _ -> Error(Errors.NotFound "a targeted verification read addressed an unknown reconcile field")
                    elif document.Contains "updateProjectV2ItemFieldValue" then
                        variables
                        |> List.tryPick (fun (_, v) ->
                            match v with
                            | VId id when id.StartsWith "PVTI_" -> id.Substring("PVTI_".Length) |> Int32.TryParse |> function | true, n -> Some n | _ -> None
                            | _ -> None)
                        |> Option.iter (fun n -> written <- Map.add n projectedStatus written)
                        ok """{"data":{"updateProjectV2ItemFieldValue":{"clientMutationId":null}}}"""
                    elif document.Contains "projectsV2" then
                        ok
                            """{"data":{"organization":{"projectsV2":{"nodes":[{"number":12,"title":"Coordination","id":"PVT_coord"}]}}},"rateLimit":{"cost":1,"remaining":4977}}"""
                    elif document.Contains "fields(first" then
                        // `Done` is an option here because the remedy WRITES it: a single-select write
                        // resolves the value to an option id before it is attempted.
                        ok
                            """{"data":{"organization":{"projectV2":{"fields":{"nodes":[{"id":"PVTSSF_status","name":"Status","dataType":"SINGLE_SELECT","options":[{"id":"opt_ready","name":"Ready"},{"id":"opt_wip","name":"In progress"},{"id":"opt_review","name":"In review"},{"id":"opt_done","name":"Done"}]},{"id":"PVTF_blocked","name":"Blocked by","dataType":"TEXT"}]}}}},"rateLimit":{"cost":1,"remaining":4977}}"""
                    elif document.Contains "items(first" then
                        ok
                            $"""{{"data":{{"organization":{{"projectV2":{{"items":{{"pageInfo":{{"hasNextPage":false,"endCursor":null}},"nodes":[%s{items ()}]}}}}}}}},"rateLimit":{{"cost":1,"remaining":4977}}}}"""
                    else
                        Error(Errors.NotFound $"the fixture serves no answer for: %s{document}")
                | _ -> Error(Errors.NotFound "a graphql call with no document")
            // The OPEN-issue listing. Empty, and that is the fixture's point: every item here is CLOSED, so
            // none of them is open, none carries a claim marker, and `choresFor` takes its unreserved
            // branch — which is the one `CLOSED-ISSUE-NOT-DONE` lives on.
            | "GET", "repos/FS-GG/FS.GG.SDD/issues" -> ok "[]"
            // A CLOSED candidate is swept with no body, marker, or blocker read (`Scan.snapshot`), so this
            // exists only to keep an unexpected REST call loud rather than silently empty.
            | "GET", path when path.EndsWith "/comments" ->
                let parts = path.Split '/'
                let number = Int32.Parse parts.[parts.Length - 2]
                ok (commentsJson number)
            | "GET", path when path.StartsWith("repos/FS-GG/FS.GG.SDD/issues/", StringComparison.Ordinal) ->
                let number = path.Substring("repos/FS-GG/FS.GG.SDD/issues/".Length) |> Int32.Parse
                let state = if reopened.Contains number then "open" else "closed"
                ok ($"{{\"state\":\"%s{state}\",\"body\":\"\"}}")
            | "PATCH", path when path.StartsWith("repos/FS-GG/FS.GG.SDD/issues/", StringComparison.Ordinal) ->
                let number = path.Substring("repos/FS-GG/FS.GG.SDD/issues/".Length) |> Int32.Parse
                match req.Body with
                | Json payload ->
                    use document = JsonDocument.Parse payload
                    match document.RootElement.GetProperty("state").GetString() with
                    | "open" -> reopened <- reopened.Add number
                    | "closed" -> reopened <- reopened.Remove number
                    | state -> failwithf "unexpected issue state %s" state
                | _ -> failwith "issue PATCH carried no JSON body"
                ok "{}"
            | "POST", path when path.EndsWith "/comments" ->
                match req.Body with
                | Json payload ->
                    use document = JsonDocument.Parse payload
                    storedCommentBodies <-
                        storedCommentBodies @ [ document.RootElement.GetProperty("body").GetString() ]
                | _ -> failwith "comment POST carried no JSON body"
                ok """{"id":9001}"""
            | m, p -> Error(Errors.NotFound $"the fixture serves no %s{m} %s{p}"))

    let private reconcileWorldWithQueries (closed: int list) (rateLimited: Set<int>) (queries: ResizeArray<string> option) =
        reconcileWorldWithQueriesAndComments
            closed
            rateLimited
            queries
            SubjectBoundTypedCompletion

    let private reconcileWorld (closed: int list) (rateLimited: Set<int>) =
        reconcileWorldWithQueries closed rateLimited None

    /// A BLOCKER-CLEARED apply whose accepted batch projects Status=Ready but leaves Blocked by stale.
    /// The second board scan is a real fresh transport response, not a renderer-only synthetic row.
    let private partialBlockerReconcileWorld () =
        let mutable mutationAccepted = false
        let staleBlocker = "FS-GG/FS.GG.SDD#45"

        let items () =
            let status = if mutationAccepted then "Ready" else "Blocked"

            [ boardItemInWithBody status 47 "blocked item" (Some staleBlocker) "OPEN" "Paths: src/A.fs"
              boardItemIn "Done" 45 "resolved blocker" None "CLOSED" ]
            |> String.concat ","

        Fake.Recorder(fun (req: Request) ->
            match req.Method, req.Path.Trim '/' with
            | "GET", "rate_limit" -> ok """{"resources":{"graphql":{"remaining":4980,"limit":5000}}}"""
            | "POST", "graphql" ->
                match req.Body with
                | Query(document, variables) ->
                    if document.Contains "projectItems" then
                        ok """{"data":{"repository":{"issue":{"projectItems":{"nodes":[{"id":"PVTI_47","project":{"number":12}}]}}}},"rateLimit":{"cost":1,"remaining":4977}}"""
                    elif document.Contains "node(id: $itemId)" then
                        let field = variables |> List.tryPick (fun (k, v) -> match k, v with | "field", VString name -> Some name | _ -> None)
                        let value =
                            match field with
                            | Some "Status" -> $"""{{"name":"%s{if mutationAccepted then "Ready" else "Blocked"}"}}"""
                            | Some "Blocked by" -> $"""{{"text":"%s{staleBlocker}"}}"""
                            | _ -> "null"
                        ok ("{\"data\":{\"node\":{\"fieldValueByName\":" + value + "}},\"rateLimit\":{\"cost\":1,\"remaining\":4977}}")
                    elif document.Contains "updateProjectV2ItemFieldValue" then
                        mutationAccepted <- true
                        ok """{"data":{"f0":{"clientMutationId":null},"f1":{"clientMutationId":null}}}"""
                    elif document.Contains "projectsV2" then
                        ok
                            """{"data":{"organization":{"projectsV2":{"nodes":[{"number":12,"title":"Coordination","id":"PVT_coord"}]}}},"rateLimit":{"cost":1,"remaining":4977}}"""
                    elif document.Contains "fields(first" then
                        ok
                            """{"data":{"organization":{"projectV2":{"fields":{"nodes":[{"id":"PVTSSF_status","name":"Status","dataType":"SINGLE_SELECT","options":[{"id":"opt_ready","name":"Ready"},{"id":"opt_blocked","name":"Blocked"},{"id":"opt_done","name":"Done"}]},{"id":"PVTF_blocked","name":"Blocked by","dataType":"TEXT"}]}}}},"rateLimit":{"cost":1,"remaining":4977}}"""
                    elif document.Contains "items(first" then
                        ok
                            $"""{{"data":{{"organization":{{"projectV2":{{"items":{{"pageInfo":{{"hasNextPage":false,"endCursor":null}},"nodes":[%s{items ()}]}}}}}}}},"rateLimit":{{"cost":1,"remaining":4977}}}}"""
                    else
                        Error(Errors.NotFound $"the fixture serves no answer for: %s{document}")
                | _ -> Error(Errors.NotFound "a graphql call with no document")
            // A REAL touch-set, and .github#2220 is why it is spelled rather than left as filler. This body
            // was `Paths: none` — chosen as the shortest thing that parses, not as a fact about the row —
            // and `Paths: none` is `DeclaredNone`, whose remedy is now `Backlog` rather than `Ready`. This
            // fixture's subject is the two-FIELD receipt on an ORDINARY cleared row, so the ordinary row is
            // what it must carry; the `DeclaredNone` path has its own fixture below.
            | "GET", "repos/FS-GG/FS.GG.SDD/issues/47" ->
                ok """{"number":47,"body":"Paths: src/A.fs"}"""
            | "GET", "repos/FS-GG/FS.GG.SDD/issues/47/comments" -> ok (routedLedger "FS-GG/FS.GG.SDD#47")
            | "POST", "repos/FS-GG/FS.GG.SDD/issues/47/comments" -> ok """{"id":9047}"""
            | "GET", "repos/FS-GG/FS.GG.SDD/issues" -> ok "[]"
            | "GET", "repos/FS-GG/FS.GG.SDD/pulls" -> ok "[]"
            | "GET", "repos/FS-GG/FS.GG.SDD/git/matching-refs/heads/item/47-" -> ok "[]"
            | m, p -> Error(Errors.NotFound $"the fixture serves no %s{m} %s{p}"))

    /// `.github#1858`'S EXACT SHAPE: one OPEN `Blocked` row whose only blocker has CLOSED, whose body
    /// declares `Paths: none`, and which `BLOCKER-CLEARED` used to promote to `Ready` (.github#2220).
    ///
    /// THIS EXISTS BECAUSE A CORE TEST CANNOT REACH THE HALF THAT WAS BROKEN. `Chore.fs` decides the
    /// destination; `Client.fs` decides what actually goes on the wire — and they were two different
    /// answers, because `writesFor` hardcoded `statusWireName Ready` for the Status half of the
    /// two-field batch. Every assertion in `ChoreTests` would have stayed green over that: the receipt
    /// would have said `Backlog` while the mutation sent `Ready`, which is the board and its own audit
    /// trail disagreeing. So this fixture serves a `Backlog` OPTION and the test below reads the OPTION
    /// ID off the log — the bytes that reach GitHub, not the value the engine intended.
    let private declaredNoneReconcileWorld () =
        let mutable status = "Blocked"
        let mutable blockedBy = "FS-GG/FS.GG.SDD#45"

        let items () =
            [ boardItemInWithBody status 47 "a decision item" (if blockedBy = "" then None else Some blockedBy) "OPEN" "Paths: none"
              boardItemIn "Done" 45 "resolved blocker" None "CLOSED" ]
            |> String.concat ","

        Fake.Recorder(fun (req: Request) ->
            match req.Method, req.Path.Trim '/' with
            | "GET", "rate_limit" -> ok """{"resources":{"graphql":{"remaining":4980,"limit":5000}}}"""
            | "POST", "graphql" ->
                match req.Body with
                | Query(document, variables) ->
                    if document.Contains "projectItems" then
                        ok """{"data":{"repository":{"issue":{"projectItems":{"nodes":[{"id":"PVTI_47","project":{"number":12}}]}}}},"rateLimit":{"cost":1,"remaining":4977}}"""
                    elif document.Contains "node(id: $itemId)" then
                        let field = variables |> List.tryPick (fun (k, v) -> match k, v with | "field", VString name -> Some name | _ -> None)
                        let value =
                            match field with
                            | Some "Status" -> $"""{{"name":"%s{status}"}}"""
                            | Some "Blocked by" when blockedBy <> "" -> $"""{{"text":"%s{blockedBy}"}}"""
                            | Some "Blocked by" -> "null"
                            | _ -> "null"
                        ok ("{\"data\":{\"node\":{\"fieldValueByName\":" + value + "}},\"rateLimit\":{\"cost\":1,\"remaining\":4977}}")
                    elif document.Contains "updateProjectV2ItemFieldValue" then
                        // The repair landed: the fresh verification read below now observes both fields.
                        status <- "Backlog"
                        blockedBy <- ""
                        ok """{"data":{"f0":{"clientMutationId":null},"f1":{"clientMutationId":null}}}"""
                    elif document.Contains "projectsV2" then
                        ok
                            """{"data":{"organization":{"projectsV2":{"nodes":[{"number":12,"title":"Coordination","id":"PVT_coord"}]}}},"rateLimit":{"cost":1,"remaining":4977}}"""
                    elif document.Contains "fields(first" then
                        // `Backlog` IS an option here, unlike the fixtures above — a board that could not
                        // represent the destination would fail this test for the wrong reason (HTTP 422),
                        // which proves nothing about which column the CLI chose.
                        ok
                            """{"data":{"organization":{"projectV2":{"fields":{"nodes":[{"id":"PVTSSF_status","name":"Status","dataType":"SINGLE_SELECT","options":[{"id":"opt_ready","name":"Ready"},{"id":"opt_backlog","name":"Backlog"},{"id":"opt_blocked","name":"Blocked"},{"id":"opt_done","name":"Done"}]},{"id":"PVTF_blocked","name":"Blocked by","dataType":"TEXT"}]}}}},"rateLimit":{"cost":1,"remaining":4977}}"""
                    elif document.Contains "items(first" then
                        ok
                            $"""{{"data":{{"organization":{{"projectV2":{{"items":{{"pageInfo":{{"hasNextPage":false,"endCursor":null}},"nodes":[%s{items ()}]}}}}}}}},"rateLimit":{{"cost":1,"remaining":4977}}}}"""
                    else
                        Error(Errors.NotFound $"the fixture serves no answer for: %s{document}")
                | _ -> Error(Errors.NotFound "a graphql call with no document")
            | "GET", "repos/FS-GG/FS.GG.SDD/issues/47" ->
                ok """{"number":47,"body":"Paths: none"}"""
            | "GET", "repos/FS-GG/FS.GG.SDD/issues/47/comments" -> ok "[]"
            | "POST", "repos/FS-GG/FS.GG.SDD/issues/47/comments" -> ok """{"id":9047}"""
            | "GET", "repos/FS-GG/FS.GG.SDD/issues" -> ok "[]"
            | "GET", "repos/FS-GG/FS.GG.SDD/pulls" -> ok "[]"
            | "GET", "repos/FS-GG/FS.GG.SDD/git/matching-refs/heads/item/47-" -> ok "[]"
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
        let mutable classWritten = false

        let item () =
            let boardClass = if classWritten then "{\"name\":\"defect\"}" else "null"
            $"""{{"status":{{"name":"Ready"}},"blockedBy":null,"class":%s{boardClass},"content":{{"__typename":"Issue","number":301,"title":"ordinary title, class is in the body","state":"OPEN","repository":{{"nameWithOwner":"FS-GG/FS.GG.SDD"}}}}}}"""

        Fake.Recorder(fun (req: Request) ->
            match req.Method, req.Path.Trim '/' with
            | "GET", "rate_limit" -> ok """{"resources":{"graphql":{"remaining":4980,"limit":5000}}}"""
            | "POST", "graphql" ->
                match req.Body with
                | Query(document, variables) ->
                    if document.Contains "projectItems" then
                        ok """{"data":{"repository":{"issue":{"projectItems":{"nodes":[{"id":"PVTI_301","project":{"number":12}}]}}}},"rateLimit":{"cost":1,"remaining":4977}}"""
                    elif document.Contains "node(id: $itemId)" then
                        let field = variables |> List.tryPick (fun (k, v) -> match k, v with | "field", VString name -> Some name | _ -> None)
                        let value =
                            match field with
                            | Some "Class" when classWritten -> "{\"name\":\"defect\"}"
                            | Some "Class" -> "null"
                            | _ -> "null"
                        ok ("{\"data\":{\"node\":{\"fieldValueByName\":" + value + "}},\"rateLimit\":{\"cost\":1,\"remaining\":4977}}")
                    elif document.Contains "updateProjectV2ItemFieldValue" then
                        classWritten <- true
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
                        ok $"""{{"data":{{"organization":{{"projectV2":{{"items":{{"pageInfo":{{"hasNextPage":false,"endCursor":null}},"nodes":[%s{item ()}]}}}}}}}},"rateLimit":{{"cost":1,"remaining":4977}}}}"""
                    else
                        Error(Errors.NotFound $"the fixture serves no answer for: %s{document}")
                | _ -> Error(Errors.NotFound "a graphql call with no document")
            // OPEN, so its body IS read — that is where the class is declared. A real touch-set too, or the
            // item would not be a candidate at all.
            | "GET", "repos/FS-GG/FS.GG.SDD/issues/301" ->
                ok """{"number":301,"body":"Paths: src/Real/**\n\nClass: defect"}"""
            | "GET", "repos/FS-GG/FS.GG.SDD/issues" -> ok "[]"
            | "GET", path when path.EndsWith "/comments" -> ok "[]"
            | "POST", path when path.EndsWith "/comments" -> ok """{"id":9047}"""
            | m, p -> Error(Errors.NotFound $"the fixture serves no %s{m} %s{p}"))

    /// .github#2394 — a coherent `Blocked on: human/...` park must not be projected away by
    /// `LIFECYCLE-PROJECTION-LAG`. One OPEN item, `Status=Blocked`, with NO recorded `Blocked by` ref at
    /// all — deliberately, so `Blockers.cleared` is `false` and `BLOCKER-CLEARED` (and the
    /// `humanHoldAllowsFlip` gate it already carried before this fix) is never even the rule in play. No
    /// claim, no PR, no delivery obligation. Every mechanical fact the lifecycle reducer reads
    /// computes straight through to its final `Ready` fallthrough — exactly the reproduction recorded on
    /// the issue three times on one row — so only the body sentinel `sentinel` toggles stands between that
    /// computed destination and a board write.
    ///
    /// `sentinel = true` is the coherent park: the `updateProjectV2ItemFieldValue` branch below is
    /// deliberately UNREACHABLE (`Errors.NotFound`), so an inverted fix — one that dropped the new gate, or
    /// one that mistakenly keyed it off `Class: decision` instead of the sentinel itself (the exact
    /// masking AC2 forbids relying on) — is caught by a loud fixture refusal, not merely a soft assertion.
    /// `sentinel = false` is the SAME row with the ONE `Blocked on:` line removed: it must still project
    /// `Ready` normally (AC3), so the new gate cannot have been written to hold `Blocked` unconditionally.
    let private humanParkReconcileWorld (sentinel: bool) =
        let mutable status = "Blocked"

        let body =
            if sentinel then
                "Paths: src/A.fs\n\nBlocked on: human/action"
            else
                "Paths: src/A.fs"

        let items () =
            [ boardItemInWithBody status 47 "a human-parked item" None "OPEN" body ] |> String.concat ","

        Fake.Recorder(fun (req: Request) ->
            match req.Method, req.Path.Trim '/' with
            | "GET", "rate_limit" -> ok """{"resources":{"graphql":{"remaining":4980,"limit":5000}}}"""
            | "POST", "graphql" ->
                match req.Body with
                | Query(document, variables) ->
                    if document.Contains "projectItems" then
                        ok """{"data":{"repository":{"issue":{"projectItems":{"nodes":[{"id":"PVTI_47","project":{"number":12}}]}}}},"rateLimit":{"cost":1,"remaining":4977}}"""
                    elif document.Contains "node(id: $itemId)" then
                        let field =
                            variables
                            |> List.tryPick (fun (k, v) -> match k, v with | "field", VString name -> Some name | _ -> None)

                        let value =
                            match field with
                            | Some "Status" -> $"""{{"name":"%s{status}"}}"""
                            | _ -> "null"

                        ok ("{\"data\":{\"node\":{\"fieldValueByName\":" + value + "}},\"rateLimit\":{\"cost\":1,\"remaining\":4977}}")
                    elif document.Contains "updateProjectV2ItemFieldValue" then
                        if sentinel then
                            Error(
                                Errors.NotFound
                                    ".github#2394: a coherent human park must never reach a board write — \
                                     if this fired, LIFECYCLE-PROJECTION-LAG stopped respecting the sentinel"
                            )
                        else
                            status <- "Ready"
                            ok """{"data":{"updateProjectV2ItemFieldValue":{"clientMutationId":null}}}"""
                    elif document.Contains "projectsV2" then
                        ok
                            """{"data":{"organization":{"projectsV2":{"nodes":[{"number":12,"title":"Coordination","id":"PVT_coord"}]}}},"rateLimit":{"cost":1,"remaining":4977}}"""
                    elif document.Contains "fields(first" then
                        ok
                            """{"data":{"organization":{"projectV2":{"fields":{"nodes":[{"id":"PVTSSF_status","name":"Status","dataType":"SINGLE_SELECT","options":[{"id":"opt_ready","name":"Ready"},{"id":"opt_blocked","name":"Blocked"},{"id":"opt_done","name":"Done"}]},{"id":"PVTF_blocked","name":"Blocked by","dataType":"TEXT"}]}}}},"rateLimit":{"cost":1,"remaining":4977}}"""
                    elif document.Contains "items(first" then
                        ok
                            $"""{{"data":{{"organization":{{"projectV2":{{"items":{{"pageInfo":{{"hasNextPage":false,"endCursor":null}},"nodes":[%s{items ()}]}}}}}}}},"rateLimit":{{"cost":1,"remaining":4977}}}}"""
                    else
                        Error(Errors.NotFound $"the fixture serves no answer for: %s{document}")
                | _ -> Error(Errors.NotFound "a graphql call with no document")
            | "GET", "repos/FS-GG/FS.GG.SDD/issues/47" ->
                ok (System.Text.Json.JsonSerializer.Serialize {| number = 47; body = body |})
            | "GET", "repos/FS-GG/FS.GG.SDD/issues/47/comments" -> ok (routedLedger "FS-GG/FS.GG.SDD#47")
            | "GET", "repos/FS-GG/FS.GG.SDD/issues" -> ok "[]"
            | "GET", "repos/FS-GG/FS.GG.SDD/pulls" -> ok "[]"
            | "GET", "repos/FS-GG/FS.GG.SDD/git/matching-refs/heads/item/47-" -> ok "[]"
            // Only reached on the `sentinel = false` leg: a landed `LIFECYCLE-PROJECTION-LAG` write posts
            // its durable ordering watermark (`LifecycleProjection.watermarkMarker`) right after the fresh
            // verification read proves the Status mutation landed.
            | "POST", "repos/FS-GG/FS.GG.SDD/issues/47/comments" -> ok """{"id":9047}"""
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
                | Options.Ready -> Client.ready (context transport) opts
                | Options.SetField -> Handlers.setField (context transport) opts
                | other -> failwithf "this fixture drives reconciliation board surfaces only, got %A" other

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

    /// The live #2166 topology: the Coordination board is owned by FS-GG, while the blocked item is an
    /// issue in EHotwagner/rogue3.  The default-owner twin is present in the project lookup response too;
    /// every mutation must select only `PVTI_external96`.
    let private validExternalOwnerItemLookup =
        """{"data":{"node":{"items":{"pageInfo":{"hasNextPage":false,"endCursor":null},"nodes":[{"id":"PVTI_default96","content":{"number":96,"repository":{"nameWithOwner":"FS-GG/rogue3"}}},{"id":"PVTI_external96","content":{"number":96,"repository":{"nameWithOwner":"EHotwagner/rogue3"}}}]}}}}"""

    let private externalOwnerWriteWorldWithLookup (lookupResponse: string) =
        let mutable status = "Blocked"
        let mutable blockedBy = "FS-GG/.github#2155"

        let items () =
            let blocker =
                """{"status":{"name":"Done"},"blockedBy":null,"content":{"__typename":"Issue","number":2155,"title":"resolved blocker","body":"","state":"CLOSED","repository":{"nameWithOwner":"FS-GG/.github"}}}"""

            let external =
                $"""{{"status":{{"name":"%s{status}"}},"blockedBy":%s{if blockedBy = "" then "null" else $"{{\"text\":\"%s{blockedBy}\"}}"},"content":{{"__typename":"Issue","number":96,"title":"external target","body":"Paths: src/A.fs","state":"OPEN","repository":{{"nameWithOwner":"EHotwagner/rogue3"}}}}}}"""

            String.concat "," [ external; blocker ]

        Fake.Recorder(fun (req: Request) ->
            match req.Method, req.Path.Trim '/' with
            | "GET", "rate_limit" -> ok """{"resources":{"graphql":{"remaining":4980,"limit":5000}}}"""
            | "POST", "graphql" ->
                match req.Body with
                | Query(document, _) when document.Contains "node(id: $projectId)" ->
                    ok lookupResponse
                | Query(document, variables) when document.Contains "node(id: $itemId)" ->
                    let item = variables |> List.tryPick (fun (k, v) -> match k, v with | "itemId", VId id -> Some id | _ -> None)
                    let field = variables |> List.tryPick (fun (k, v) -> match k, v with | "field", VString name -> Some name | _ -> None)

                    match item, field with
                    | Some "PVTI_external96", Some "Status" ->
                        ok $"""{{"data":{{"node":{{"fieldValueByName":{{"name":"%s{status}"}}}}}},"rateLimit":{{"cost":1,"remaining":4977}}}}"""
                    | Some "PVTI_external96", Some "Blocked by" when blockedBy <> "" ->
                        ok $"""{{"data":{{"node":{{"fieldValueByName":{{"text":"%s{blockedBy}"}}}}}},"rateLimit":{{"cost":1,"remaining":4977}}}}"""
                    | Some "PVTI_external96", Some "Blocked by" ->
                        ok """{"data":{"node":{"fieldValueByName":null}},"rateLimit":{"cost":1,"remaining":4977}}"""
                    | _ -> Error(Errors.NotFound "the targeted verification read addressed the wrong external row or field")
                | Query(document, _) when document.Contains "f0:" ->
                    status <- "Ready"
                    blockedBy <- ""
                    ok """{"data":{"f0":{"projectV2Item":{"id":"PVTI_external96"}},"f1":{"projectV2Item":{"id":"PVTI_external96"}}}}"""
                | Query(document, _) when document.Contains "updateProjectV2ItemFieldValue" ->
                    status <- "Ready"
                    ok """{"data":{"updateProjectV2ItemFieldValue":{"projectV2Item":{"id":"PVTI_external96"}}}}"""
                | Query(document, _) when document.Contains "projectsV2" ->
                    ok
                        """{"data":{"organization":{"projectsV2":{"nodes":[{"number":12,"title":"Coordination","id":"PVT_coord"}]}}},"rateLimit":{"cost":1,"remaining":4977}}"""
                | Query(document, _) when document.Contains "fields(first" ->
                    ok
                        """{"data":{"organization":{"projectV2":{"fields":{"nodes":[{"id":"PVTSSF_status","name":"Status","dataType":"SINGLE_SELECT","options":[{"id":"opt_ready","name":"Ready"},{"id":"opt_blocked","name":"Blocked"},{"id":"opt_done","name":"Done"}]},{"id":"PVTF_blocked","name":"Blocked by","dataType":"TEXT"}]}}}},"rateLimit":{"cost":1,"remaining":4977}}"""
                | Query(document, _) when document.Contains "items(first" ->
                    ok
                        $"""{{"data":{{"organization":{{"projectV2":{{"items":{{"pageInfo":{{"hasNextPage":false,"endCursor":null}},"nodes":[%s{items ()}]}}}}}}}},"rateLimit":{{"cost":1,"remaining":4977}}}}"""
                | Query(document, _) -> Error(Errors.NotFound $"the external-owner fixture serves no answer for: %s{document}")
                | _ -> Error(Errors.NotFound "a graphql call with no document")
            // A REAL touch-set — .github#2220, for `partialBlockerReconcileWorld`'s reason exactly. This
            // fixture is about carrying the EXTERNAL OWNER into the lookup, and it needs an ordinary
            // cleared row to do it on; `Paths: none` would silently move it onto the `Backlog` path and
            // test the external owner against the wrong remedy.
            | "GET", "repos/EHotwagner/rogue3/issues" ->
                ok """[{"number":96,"state":"open","body":"Paths: src/A.fs"}]"""
            | "GET", "repos/FS-GG/.github/issues" -> ok "[]"
            | "GET", "repos/EHotwagner/rogue3/issues/96" ->
                ok """{"number":96,"state":"open","body":"Paths: src/A.fs"}"""
            | "GET", "repos/EHotwagner/rogue3/issues/96/comments" -> ok (routedLedger "EHotwagner/rogue3#96")
            | "POST", "repos/EHotwagner/rogue3/issues/96/comments" -> ok """{"id":9096}"""
            | "GET", "repos/EHotwagner/rogue3/pulls" -> ok "[]"
            | "GET", "repos/EHotwagner/rogue3/git/matching-refs/heads/item/96-" -> ok "[]"
            | "GET", path when path.EndsWith "/comments" -> ok "[]"
            | m, p -> Error(Errors.NotFound $"the external-owner fixture serves no %s{m} %s{p}"))

    let private externalOwnerWriteWorld () =
        externalOwnerWriteWorldWithLookup validExternalOwnerItemLookup

    [<Fact>]
    let ``#2166 ready and set-field preserve one external canonical row through fresh readback`` () =
        let world = externalOwnerWriteWorld ()
        let readyCode, readyOut, _ = runReconcile world [ "ready"; "--all"; "--json" ]
        let setCode, _, setErr =
            runReconcile world [ "set-field"; "EHotwagner/rogue3#96"; "Status"; "Ready"; "--worker"; "heron-2166" ]
        let verifyCode, verifyOut, _ = runReconcile world [ "ready"; "--all"; "--json" ]

        Assert.Equal(0, readyCode)
        Assert.Contains("\"repo\":\"EHotwagner/rogue3\"", readyOut)
        Assert.Contains("\"status\":\"Blocked\"", readyOut)
        if setCode <> 0 then failwithf "external set-field failed: %s" setErr
        Assert.Equal(0, verifyCode)
        Assert.Contains("\"repo\":\"EHotwagner/rogue3\"", verifyOut)
        Assert.Contains("\"status\":\"Ready\"", verifyOut)
        Assert.True(world.Logged "--id PVTI_external96")
        Assert.False(world.Logged "--id PVTI_default96")

    [<Fact>]
    let ``#2166 reconcile apply carries the external owner into lookup and verifies both repaired fields`` () =
        let world = externalOwnerWriteWorld ()
        let code, out, err =
            runReconcile world [ "reconcile"; "--worker"; "heron-2166"; "--apply"; "--json" ]

        if String.IsNullOrWhiteSpace out then
            failwithf "#2166 external reconcile emitted no receipt (exit %d): %s" code err

        let row = parsedArray out |> List.find (fun item -> str "subject" item = "rogue3#96")
        let observed = row.GetProperty("observed").EnumerateArray() |> List.ofSeq

        if code <> 0 then failwithf "external reconcile failed: %s" err
        Assert.Equal("written", str "outcome" row)
        Assert.Equal(2, List.length observed)
        Assert.Equal("Status", str "field" observed.[0])
        Assert.Equal("Ready", str "value" observed.[0])
        Assert.Equal("Blocked by", str "field" observed.[1])
        Assert.Equal("", str "value" observed.[1])
        Assert.True(world.Logged "itemId: \"PVTI_external96\"")
        Assert.False(world.Logged "itemId: \"PVTI_default96\"")

    [<Fact>]
    let ``#2166 set-field fails closed on malformed external-owner pagination completeness`` () =
        let malformedLookups =
            [ "pageInfo absent", """{"data":{"node":{"items":{"nodes":[]}}}}"""
              "pageInfo null", """{"data":{"node":{"items":{"pageInfo":null,"nodes":[]}}}}"""
              "hasNextPage absent", """{"data":{"node":{"items":{"pageInfo":{"endCursor":null},"nodes":[]}}}}"""
              "hasNextPage null", """{"data":{"node":{"items":{"pageInfo":{"hasNextPage":null,"endCursor":null},"nodes":[]}}}}"""
              "hasNextPage wrong type", """{"data":{"node":{"items":{"pageInfo":{"hasNextPage":"false","endCursor":null},"nodes":[]}}}}""" ]

        for label, lookup in malformedLookups do
            let world = externalOwnerWriteWorldWithLookup lookup
            let code, _, err =
                runReconcile world [ "set-field"; "EHotwagner/rogue3#96"; "Status"; "Ready"; "--worker"; "heron-2166" ]

            Assert.NotEqual(0, code)
            Assert.Contains("pageInfo", err)
            Assert.False(world.Logged "--id PVTI_external96", $"%s{label} reached the mutation")

    /// .github#2382: a board whose rows are all SETTLED — closed, and already `Done`.
    ///
    /// `commentReads` records every issue-comment thread the pass actually fetches, because that is the
    /// whole subject. A settled row's thread can change no reconcile outcome (the lifecycle reducer
    /// answers `Project(Done)`, which `Chore.lifecycleProjection` drops on `item.Status = destination`, or
    /// `Withheld`), so every one of those reads is pure budget — and they are the population that GROWS,
    /// one row per item the fleet ever completes.
    ///
    /// The rows carry a `body` deliberately. `Scan.parseRow` sweeps a closed/`Done`/unclassed row's body
    /// off the reconciling board document itself, and falls back to a REST `issueBody` read per row when
    /// that scalar is missing — a second per-row REST cost that would mask the one under test.
    let private settledDoneWorld (count: int) (commentReads: ResizeArray<string>) =
        let items () =
            [ 1..count ]
            |> List.map (fun n ->
                $"""{{"status":{{"name":"Done"}},"blockedBy":null,"content":{{"__typename":"Issue","number":%d{n},"title":"settled item %d{n}","body":"Class: hardening","state":"CLOSED","repository":{{"nameWithOwner":"FS-GG/FS.GG.SDD"}}}}}}""")
            |> String.concat ","

        Fake.Recorder(fun (req: Request) ->
            match req.Method, req.Path.Trim '/' with
            | "GET", "rate_limit" -> ok """{"resources":{"graphql":{"remaining":4980,"limit":5000}}}"""
            | "POST", "graphql" ->
                match req.Body with
                | Query(document, _) ->
                    if document.Contains "projectsV2" then
                        ok
                            """{"data":{"organization":{"projectsV2":{"nodes":[{"number":12,"title":"Coordination","id":"PVT_coord"}]}}},"rateLimit":{"cost":1,"remaining":4977}}"""
                    elif document.Contains "fields(first" then
                        ok
                            """{"data":{"organization":{"projectV2":{"fields":{"nodes":[{"id":"PVTSSF_status","name":"Status","dataType":"SINGLE_SELECT","options":[{"id":"opt_ready","name":"Ready"},{"id":"opt_wip","name":"In progress"},{"id":"opt_done","name":"Done"}]},{"id":"PVTF_blocked","name":"Blocked by","dataType":"TEXT"}]}}}},"rateLimit":{"cost":1,"remaining":4977}}"""
                    elif document.Contains "items(first" then
                        ok
                            $"""{{"data":{{"organization":{{"projectV2":{{"items":{{"pageInfo":{{"hasNextPage":false,"endCursor":null}},"nodes":[%s{items ()}]}}}}}}}},"rateLimit":{{"cost":1,"remaining":4977}}}}"""
                    else
                        Error(Errors.NotFound $"the fixture serves no answer for: %s{document}")
                | _ -> Error(Errors.NotFound "a graphql call with no document")
            | "GET", "repos/FS-GG/FS.GG.SDD/issues" -> ok "[]"
            | "GET", path when path.EndsWith "/comments" ->
                commentReads.Add path
                ok "[{\"body\":\"<!-- fsgg:done-receipt v=1 -->\\nverified\"}]"
            | m, p -> Error(Errors.NotFound $"the fixture serves no %s{m} %s{p}"))

    [<Fact>]
    let ``#2382 a settled Done row's comment thread is never read`` () =
        let commentReads = ResizeArray<string>()
        let code, _, err = runReconcile (settledDoneWorld 3 commentReads) (reconcileArgs [ "--json" ])
        let threads = String.Join(", ", commentReads)

        Assert.Equal(0, code)

        Assert.True(
            commentReads.Count = 0,
            $"a settled (closed and already Done) row can change no reconcile outcome, so its thread must \
not be fetched — read %d{commentReads.Count}: %s{threads}%s{err}"
        )

    /// THE DEFECT ITSELF, AS AN INVARIANT: reconcile's REST cost must not scale with the board's closed
    /// history. Measured on the live board before this bound (2026-08-11), it did — 2,159 of 2,181 rows were
    /// closed and one dry run spent 2,050+ billed REST requests against a 5,000/hr budget, so a four-scan
    /// `check-board` pass could not complete inside one hour. A count assertion, not a threshold: the two
    /// boards differ ONLY in how much finished work they carry.
    [<Fact>]
    let ``#2382 reconcile's REST cost does not grow with the board's closed history`` () =
        let small = settledDoneWorld 1 (ResizeArray<string>())
        let large = settledDoneWorld 40 (ResizeArray<string>())

        let smallCode, _, _ = runReconcile small (reconcileArgs [ "--json" ])
        let largeCode, _, _ = runReconcile large (reconcileArgs [ "--json" ])

        Assert.Equal(0, smallCode)
        Assert.Equal(0, largeCode)

        Assert.True(
            small.RestCalls = large.RestCalls,
            $"REST cost scaled with settled history: 1 closed row cost %d{small.RestCalls} REST calls, \
40 cost %d{large.RestCalls}. That is .github#2382 — the per-row read is back."
        )

    [<Fact>]
    let ``#2157 partial BLOCKER-CLEARED receipt retains both freshly observed values`` () =
        let code, out, err = runApplyJson (partialBlockerReconcileWorld ())

        if String.IsNullOrWhiteSpace out then
            failwithf "#2157 fixture emitted no receipt (exit %d): %s" code err

        let row = parsedArray out |> List.find (fun r -> str "subject" r = "FS.GG.SDD#47")
        let intended = row.GetProperty("writes").EnumerateArray() |> List.ofSeq
        let observed = row.GetProperty("observed").EnumerateArray() |> List.ofSeq

        Assert.NotEqual(0, code)
        Assert.Equal("failed", str "outcome" row)
        Assert.Contains("Blocked by", str "error" row)
        Assert.Contains("fresh verification failed", err)
        Assert.Equal(2, List.length intended)
        Assert.Equal(2, List.length observed)
        Assert.Equal("Status", str "field" observed.[0])
        Assert.Equal("Ready", str "value" observed.[0])
        Assert.Equal("Blocked by", str "field" observed.[1])
        Assert.Equal("FS-GG/FS.GG.SDD#45", str "value" observed.[1])

    [<Fact>]
    let ``.github#2220 reconcile --apply sends BACKLOG on the wire for a `Paths: none` row, never Ready`` () =
        // THE END-TO-END LEG, on `.github#1858`'s exact shape. The Core chooses the destination and the
        // CLI puts it on the wire; this asserts the two agree by reading the OPTION ID out of the
        // transport log — `opt_backlog`, the bytes GitHub would receive — rather than trusting the
        // receipt, which is the engine describing itself.
        let world = declaredNoneReconcileWorld ()
        let code, out, err = runReconcile world (reconcileArgs [ "--apply"; "--json" ])

        if String.IsNullOrWhiteSpace out then
            failwithf ".github#2220 fixture emitted no receipt (exit %d): %s" code err

        let row = parsedArray out |> List.find (fun r -> str "subject" r = "FS.GG.SDD#47")

        if code <> 0 then failwithf ".github#2220 reconcile failed: %s" err

        Assert.Equal("LIFECYCLE-PROJECTION-LAG", str "rule" row)
        Assert.Equal("written", str "outcome" row)

        // The RECEIPT names Backlog...
        Assert.Equal("Status", str "field" row)
        Assert.Equal("Backlog", str "value" row)

        // ...the FRESH VERIFICATION read agrees...
        let observed = row.GetProperty("observed").EnumerateArray() |> List.ofSeq
        Assert.Equal(2, List.length observed)
        Assert.Equal("Status", str "field" observed.[0])
        Assert.Equal("Backlog", str "value" observed.[0])
        Assert.Equal("Blocked by", str "field" observed.[1])
        Assert.Equal("", str "value" observed.[1])

        // ...AND THE WIRE CARRIED IT. This is the assertion the hardcoded `Ready` in `writesFor` would
        // have failed while every other line above still passed.
        Assert.True(world.Logged "opt_backlog", $"the mutation did not send the Backlog option: %A{world.Log}")
        Assert.False(world.Logged "opt_ready", $"the mutation sent Ready for a `Paths: none` row: %A{world.Log}")

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
        let intended = landed.GetProperty("writes").EnumerateArray() |> List.ofSeq
        let observed = landed.GetProperty("observed").EnumerateArray() |> List.ofSeq
        Assert.Single intended |> ignore
        Assert.Single observed |> ignore
        Assert.Equal("Status", str "field" intended.[0])
        Assert.Equal("Done", str "value" intended.[0])
        Assert.Equal("Done", str "value" observed.[0])

        // THE QUEUED WRITE — the leg whose loss is unrecoverable, and the reason this issue was filed. It
        // is a DISTINCT VALUE of a closed set, not a sentence a consumer greps for the word "queued" in.
        let queued = bySubject.["FS.GG.SDD#102"]
        Assert.Equal("deferred", str "outcome" queued)
        Assert.Equal("Status", str "field" queued)
        Assert.Equal("Done", str "value" queued)

        // The finding fields are still there — `--apply` ADDS to the dry-run row, it does not replace it.
        Assert.Equal("COMPLETION-PROJECTION-LAG", str "rule" queued)
        Assert.Equal("quick", str "size" queued)

        // Exit code UNCHANGED. A deferred write is not a failure — it is a promise the queue keeps.
        Assert.Equal(0, code)

    [<Fact>]
    let ``#2313 reconcile apply verifies N writes with N targeted reads and never repeats the board census`` () =
        let queries = ResizeArray<string>()
        let code, _, err = runApplyJson (reconcileWorldWithQueries [ 101; 102; 103 ] Set.empty (Some queries))

        Assert.Equal(0, code)
        Assert.True(String.IsNullOrWhiteSpace err, err)

        let boardScans = queries |> Seq.filter (fun document -> document.Contains "items(first") |> Seq.length
        let targeted = queries |> Seq.filter (fun document -> document.Contains "node(id: $itemId)") |> Seq.length

        // One initial census derives all three chores. Each accepted one-field write is then observed by
        // one resolver read; the old full-scan verifier would instead make this 1 + N board scans and 0
        // targeted reads, so both counts are necessary to pin the cost boundary.
        Assert.Equal(1, boardScans)
        Assert.Equal(3, targeted)

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
            + "  COMPLETION-PROJECTION-LAG FS.GG.SDD#101            Status=Done" + nl
            + "  COMPLETION-PROJECTION-LAG FS.GG.SDD#102            Status=Done" + nl
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

    // ---- .github#2394 — a coherent human park is not reverted by LIFECYCLE-PROJECTION-LAG --------------
    //
    // GATE-INVERSION EVIDENCE, AS A PAIR. The first leg is the fix: `reconcile --apply` against a `Blocked`
    // row carrying `Blocked on: human/action` derives NOTHING, and the fixture makes an attempted write
    // fail loudly rather than merely asserting the wrong text. The second leg is #620/AC3's other half —
    // the SAME row, sentinel removed, must still project `Ready` normally, so the fix cannot have been to
    // hold every `Blocked` row unconditionally. Run one after the other, the pair pins the fix to the
    // sentinel itself rather than to "reconcile fell silent."

    [<Fact>]
    let ``.github#2394 reconcile --apply derives nothing for a Blocked row carrying a human-action sentinel`` () =
        let code, out, err = runReconcile (humanParkReconcileWorld true) (reconcileArgs [ "--apply" ])

        Assert.Equal(0, code)
        Assert.Equal("", err)
        Assert.DoesNotContain("LIFECYCLE-PROJECTION-LAG", out)

        let nl = Environment.NewLine

        let expected =
            "clean — no mechanical board repairs" + nl
            + "judgement findings are report-only: scripts/fsgg-coord lint --repo FS.GG.SDD" + nl

        Assert.Equal(expected, out)

    [<Fact>]
    let ``.github#2394 the same row with a human-decision sentinel is held too — both sentinel variants gate, not just Class: decision`` () =
        // AC2: the fix must not depend on the incidental `Class: decision` projection. This row declares
        // no `Class:` line at all — only `Blocked on: human/decision` — so if the gate secretly rode on
        // `Class`, this leg would regress to the same board write the `human/action` leg above refuses.
        let mutable status = "Blocked"

        let items () =
            [ boardItemInWithBody status 47 "a human-parked item" None "OPEN" "Paths: src/A.fs\n\nBlocked on: human/decision" ]
            |> String.concat ","

        let transport =
            Fake.Recorder(fun (req: Request) ->
                match req.Method, req.Path.Trim '/' with
                | "GET", "rate_limit" -> ok """{"resources":{"graphql":{"remaining":4980,"limit":5000}}}"""
                | "POST", "graphql" ->
                    match req.Body with
                    | Query(document, variables) ->
                        if document.Contains "projectItems" then
                            ok """{"data":{"repository":{"issue":{"projectItems":{"nodes":[{"id":"PVTI_47","project":{"number":12}}]}}}},"rateLimit":{"cost":1,"remaining":4977}}"""
                        elif document.Contains "node(id: $itemId)" then
                            let field =
                                variables
                                |> List.tryPick (fun (k, v) -> match k, v with | "field", VString name -> Some name | _ -> None)

                            let value =
                                match field with
                                | Some "Status" -> $"""{{"name":"%s{status}"}}"""
                                | _ -> "null"

                            ok ("{\"data\":{\"node\":{\"fieldValueByName\":" + value + "}},\"rateLimit\":{\"cost\":1,\"remaining\":4977}}")
                        elif document.Contains "updateProjectV2ItemFieldValue" then
                            Error(
                                Errors.NotFound
                                    ".github#2394: a coherent human/decision park must never reach a board write either"
                            )
                        elif document.Contains "projectsV2" then
                            ok
                                """{"data":{"organization":{"projectsV2":{"nodes":[{"number":12,"title":"Coordination","id":"PVT_coord"}]}}},"rateLimit":{"cost":1,"remaining":4977}}"""
                        elif document.Contains "fields(first" then
                            ok
                                """{"data":{"organization":{"projectV2":{"fields":{"nodes":[{"id":"PVTSSF_status","name":"Status","dataType":"SINGLE_SELECT","options":[{"id":"opt_ready","name":"Ready"},{"id":"opt_blocked","name":"Blocked"},{"id":"opt_done","name":"Done"}]},{"id":"PVTF_blocked","name":"Blocked by","dataType":"TEXT"}]}}}},"rateLimit":{"cost":1,"remaining":4977}}"""
                        elif document.Contains "items(first" then
                            ok
                                $"""{{"data":{{"organization":{{"projectV2":{{"items":{{"pageInfo":{{"hasNextPage":false,"endCursor":null}},"nodes":[%s{items ()}]}}}}}}}},"rateLimit":{{"cost":1,"remaining":4977}}}}"""
                        else
                            Error(Errors.NotFound $"the fixture serves no answer for: %s{document}")
                    | _ -> Error(Errors.NotFound "a graphql call with no document")
                | "GET", "repos/FS-GG/FS.GG.SDD/issues/47" ->
                    ok """{"number":47,"body":"Paths: src/A.fs\n\nBlocked on: human/decision"}"""
                | "GET", "repos/FS-GG/FS.GG.SDD/issues/47/comments" -> ok (routedLedger "FS-GG/FS.GG.SDD#47")
                | "GET", "repos/FS-GG/FS.GG.SDD/issues" -> ok "[]"
                | "GET", "repos/FS-GG/FS.GG.SDD/pulls" -> ok "[]"
                | "GET", "repos/FS-GG/FS.GG.SDD/git/matching-refs/heads/item/47-" -> ok "[]"
                | m, p -> Error(Errors.NotFound $"the fixture serves no %s{m} %s{p}"))

        let code, out, err = runReconcile transport (reconcileArgs [ "--apply" ])

        // `Blocked on: human/decision` is ALSO evidence for `Class: decision` (ADR-0066,
        // `Types.fsi`'s `HumanBlock` doc comment) — a real, separate, unrelated projection this board
        // (deliberately, like `classWorld false`) declares no `Class` field for, so `CLASS-PROJECTION-LAG`
        // fires and is withheld exactly as `#1625` pins. That row is NOT the assertion here: it is left
        // running so this fixture cannot silently stop exercising the human/decision body shape. What
        // matters is what is ABSENT — `LIFECYCLE-PROJECTION-LAG`/`Status=Ready` — proving the Status gate
        // held on the sentinel itself, not on the Class row that happens to withhold for an unrelated
        // reason (a board that DID declare a Class field would apply that row and still have to hold
        // Status, which is exactly what AC2 requires the gate not to depend on).
        Assert.Equal(0, code)
        Assert.DoesNotContain("LIFECYCLE-PROJECTION-LAG", out)
        Assert.DoesNotContain("Status=Ready", out)

        let nl = Environment.NewLine

        let expected =
            "applying (1 mechanical finding(s))" + nl
            + "  CLASS-PROJECTION-LAG     FS.GG.SDD#47             Class=decision" + nl
            + "judgement findings are report-only: scripts/fsgg-coord lint --repo FS.GG.SDD" + nl

        Assert.Equal(expected, out)
        Assert.Contains("board has no Class field", err)

    [<Fact>]
    let ``.github#2394 AC3 — the sentinel removed, the SAME row projects Ready normally again`` () =
        // The gate-inversion counterpart to the two legs above: this pins that the fix is a SENTINEL gate,
        // not a blanket "never touch a Blocked row" regression that would just as silently break ordinary
        // stale-Blocked reconciliation.
        let code, out, err = runReconcile (humanParkReconcileWorld false) (reconcileArgs [ "--apply"; "--json" ])

        Assert.Equal(0, code)
        Assert.Equal("", err)

        let rows = parsedArray out
        Assert.Single rows |> ignore
        let row = rows.[0]
        Assert.Equal("LIFECYCLE-PROJECTION-LAG", row.GetProperty("rule").GetString())
        Assert.Equal("Status", row.GetProperty("field").GetString())
        Assert.Equal("Ready", row.GetProperty("value").GetString())
        Assert.Equal("Status=Ready", row.GetProperty("remedy").GetString())
        Assert.Equal("written", row.GetProperty("outcome").GetString())

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
        let expected =
            """[{"id":"COMPLETION-PROJECTION-LAG:FS-GG/FS.GG.SDD#101","remedy":"Status=Done","rule":"COMPLETION-PROJECTION-LAG","size":"quick","statement":"FS.GG.SDD#101: authoritative completion evidence exists; repair issue closure and Status=Done from that receipt.","subject":"FS.GG.SDD#101"}]"""

        Assert.Equal(expected, out.Trim())
        Assert.Equal(0, code)

    let private runReconcileWithHealthReport (world: Fake.Recorder) =
        let path = Path.Combine(Path.GetTempPath(), "fsgg-completion-health-" + Guid.NewGuid().ToString "n" + ".json")
        let previous = Environment.GetEnvironmentVariable "FSGG_COORD_HEALTH_REPORT"

        try
            Environment.SetEnvironmentVariable("FSGG_COORD_HEALTH_REPORT", path)
            let code, out, err = runReconcile world (reconcileArgs [ "--json" ])
            code, out, err, File.ReadAllText path
        finally
            Environment.SetEnvironmentVariable("FSGG_COORD_HEALTH_REPORT", previous)

            if File.Exists path then
                File.Delete path

    [<Fact>]
    let ``a closed non-Done row without a completion receipt exposes one dry-run correction without mutating`` () =
        let world = reconcileWorldWithQueriesAndComments [ 101 ] Set.empty None "[]"
        let code, out, err, report = runReconcileWithHealthReport world

        Assert.Equal(0, code)
        Assert.Equal("", err)
        let rows = parsedArray out
        Assert.Single rows |> ignore
        Assert.Equal("PREMATURE-COMPLETION", rows.Head.GetProperty("rule").GetString())
        Assert.Equal("Status=In review", rows.Head.GetProperty("remedy").GetString())
        Assert.False(world.Logged "updateProjectV2ItemFieldValue", $"a receipt-free row reached a mutation: %A{world.Log}")

        use document = JsonDocument.Parse report
        let root = document.RootElement
        let subjects = root.GetProperty("subjects").EnumerateArray() |> List.ofSeq
        Assert.Equal("typed-complete-success/1", root.GetProperty("completeReadBoundary").GetString())
        Assert.Equal(1, root.GetProperty("subjectCount").GetInt32())
        Assert.Single subjects |> ignore
        Assert.Equal("FS-GG/FS.GG.SDD#101", subjects.Head.GetProperty("subject").GetString())
        Assert.Equal("In progress", subjects.Head.GetProperty("current").GetString())
        Assert.Equal("In review", subjects.Head.GetProperty("intended").GetString())
        Assert.True(subjects.Head.GetProperty("readComplete").GetBoolean())

    [<Fact>]
    let ``premature completion apply persists authority then reopens then projects In review`` () =
        let world = reconcileWorldWithQueriesAndComments [ 101 ] Set.empty None "[]"
        let code, out, err = runReconcile world (reconcileArgs [ "--apply"; "--json" ])

        Assert.Equal(0, code)
        Assert.Equal("", err)
        let row = parsedArray out |> List.exactlyOne
        Assert.Equal("PREMATURE-COMPLETION", row.GetProperty("rule").GetString())
        Assert.Equal("written", row.GetProperty("outcome").GetString())
        Assert.Equal("In review", row.GetProperty("value").GetString())

        let firstComment = world.Log |> List.findIndex (fun entry -> entry.Contains "comment-post")
        let reopen = world.Log |> List.findIndex (fun entry -> entry.Contains "issue-patch")
        let statusWrite = world.Log |> List.findIndex (fun entry -> entry.Contains "item-edit")
        Assert.True(firstComment < reopen, $"correction authority was not first: %A{world.Log}")
        Assert.True(reopen < statusWrite, $"issue reopen did not precede Status projection: %A{world.Log}")

        let secondCode, secondOut, secondErr =
            runReconcile world (reconcileArgs [ "--apply"; "--json" ])
        if secondCode <> 0 then failwithf "second completion projection failed (exit %d): %s\n%s\n%A" secondCode secondErr secondOut world.Log
        Assert.Equal("", secondErr)
        Assert.Empty(parsedArray secondOut)
        Assert.Equal(1, world.Count "issue-patch")
        Assert.Equal(1, world.Count "item-edit")

    [<Fact>]
    let ``typed completion receipt repairs an open issue and Status Done idempotently`` () =
        let facts: Delivery.CompletionFacts =
            { HeadSha = "head"
              Merged = true
              MergeReachable = true
              IssueClosed = true
              BoardDone = false
              ClaimReleased = false
              PendingWrites = 0
              CleanupEligible = false
              ObligationsDeclared = true
              Obligations = [] }
        let receipt =
            Delivery.createCompletionReceipt
                "FS-GG/FS.GG.SDD#101"
                99
                "merge-sha"
                (DateTimeOffset.Parse("2026-08-22T17:00:00Z"))
                "freshness"
                "action"
                facts
            |> Result.defaultWith (String.concat "; " >> failwith)
        let comments =
            JsonSerializer.Serialize [| {| body = Delivery.encodeCompletionReceipt receipt |} |]
        let world = reconcileWorldWithQueriesAndComments [ 101 ] Set.empty None comments

        let code, out, err = runReconcile world (reconcileArgs [ "--apply"; "--json" ])
        if code <> 0 then failwithf "completion projection failed (exit %d): %s\n%s\n%A" code err out world.Log
        Assert.Equal("", err)
        let row = parsedArray out |> List.exactlyOne
        Assert.Equal("COMPLETION-PROJECTION-LAG", row.GetProperty("rule").GetString())
        Assert.Equal("Done", row.GetProperty("value").GetString())
        Assert.Equal("written", row.GetProperty("outcome").GetString())
        let close = world.Log |> List.findIndex (fun entry -> entry.Contains "issue-patch")
        let statusWrite = world.Log |> List.findIndex (fun entry -> entry.Contains "item-edit")
        Assert.True(close < statusWrite, $"issue closure did not precede Status=Done: %A{world.Log}")

        let secondCode, secondOut, secondErr =
            runReconcile world (reconcileArgs [ "--apply"; "--json" ])
        if secondCode <> 0 then failwithf "second typed completion projection failed (exit %d): %s\n%s\n%A" secondCode secondErr secondOut world.Log
        Assert.Equal("", secondErr)
        Assert.Empty(parsedArray secondOut)
        Assert.Equal(1, world.Count "issue-patch")
        Assert.Equal(1, world.Count "item-edit")

    [<Fact>]
    let ``malformed typed completion evidence is visible in health and never corrected`` () =
        let comments =
            JsonSerializer.Serialize
                [| {| body = Delivery.CompletionReceiptMarker + Environment.NewLine + "{}" |} |]
        let world = reconcileWorldWithQueriesAndComments [ 101 ] Set.empty None comments
        let code, out, err, report = runReconcileWithHealthReport world

        Assert.Equal(0, code)
        Assert.Empty(parsedArray out)
        Assert.Contains("invalid delivery completion evidence", err)
        Assert.False(world.Logged "updateProjectV2ItemFieldValue", $"invalid evidence reached a mutation: %A{world.Log}")

        use document = JsonDocument.Parse report
        let subjects = document.RootElement.GetProperty("subjects").EnumerateArray() |> List.ofSeq
        Assert.Single subjects |> ignore
        Assert.StartsWith(
            "withheld: invalid delivery completion evidence:",
            subjects.Head.GetProperty("intended").GetString())
        Assert.False(subjects.Head.GetProperty("readComplete").GetBoolean())

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
                // .github#2678 joined `who` for the reason .github#1562 joined `batch`: the claim this
                // item makes is that two verbs reading ONE board agree about how many implementer slots
                // are occupied, and a harness that can only run one of them cannot see the disagreement
                // at all. `who` needs nothing this fixture does not already serve — the board scan, the
                // repo's open issues, and each issue's comments are the same three reads `take` makes.
                | Options.Who -> Client.who (context transport) opts
                | other -> failwithf "this fixture drives take/next/batch/who only, got %A" other

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
            if hasReady then $"[\"FS-GG/FS.GG.SDD#%d{readyNumber}\"]" else "[]"

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

        Assert.Equal("[\"FS-GG/FS.GG.SDD#74\"]" + Environment.NewLine, out)
        Assert.Contains("wave occupancy: unavailable", err)
        Assert.Contains("must match the typed Protocol.wavePolicy", err)
        Assert.DoesNotContain("WAVE SHORTFALL", err)
        Assert.Equal(0, code)

    // ================================================================================================
    // .github#2678 — THREE PROJECTIONS OF ONE BOARD READ MUST NOT DISAGREE.
    // ================================================================================================
    // The measured event: `FS-GG/.github` at 2026-08-15T19:50Z carried exactly three live claims and
    // three rows whose `item/*` PR was open with no marker on the issue. `batch` reported
    // `activeItems: 6, openSlots: 0` with the shortfall headline silent, while `who` reported three held
    // items and `driver --events` three active ones. The value a host reads AT THE DISPATCH DECISION
    // POINT was the one that over-counted, and it is monotone: an orphan PR consumes a slot until a
    // human closes it, so `openSlots` pins at zero permanently once enough accumulate.

    [<Fact>]
    let ``#2678 an open item PR with no claim holds no slot, and batch and who agree on the count`` () =
        // #1 and #2 are genuinely held. #11, #12 and #13 are the phantom rows — `In review`, an open
        // `item/<n>-*` PR, and NO marker on the issue. #20 is ordinary schedulable work.
        let bodies =
            [ for n in [ 1; 2; 11; 12; 13; 20 ] -> n, $"Paths: src/%d{n}" ] |> Map.ofList

        let holders = Map.ofList [ 1, "finch-85f3"; 2, "rook-7f26" ]
        let itemPrs = Map.ofList [ 11, 2655; 12, 2651; 13, 2650 ]

        let statusFor n =
            if holders.ContainsKey n then "In progress"
            elif itemPrs.ContainsKey n then "In review"
            else "Ready"

        let transport = worldOfWithItemPrs statusFor bodies holders itemPrs

        let code, out, err =
            runQueueWithKit installWaveModel transport [ "batch"; "--repo"; "FS.GG.SDD"; "--json" ]

        Assert.Equal(0, code)

        // THE STDOUT MACHINE CONTRACT IS UNTOUCHED — this item changes what is counted, never what is
        // chosen. #20 is the one startable row either way.
        Assert.Equal("[\"FS-GG/FS.GG.SDD#20\"]" + Environment.NewLine, out)

        // TWO occupied slots out of six, so FOUR are open. Before this change the same board produced
        // `activeItems: 5, openSlots: 1` — the two claims plus all three orphan PRs.
        Assert.Contains("wave occupancy: {\"activeItems\":2,\"waveCapacity\":6,\"openSlots\":4}", err)

        // …AND THE SIGNAL #2096 ADDED FIRES. It is conditional on `OpenSlots > 0`, which is exactly what
        // the over-count suppressed.
        Assert.Contains("WAVE SHORTFALL", err)

        // ACCEPTANCE 5 — the three rows are not silently dropped either. They are real, and they are said
        // out loud under the name the reporter already gives them.
        Assert.Contains(
            "work without claim: {\"items\":3,\"refs\":[\"FS-GG/FS.GG.SDD#11\",\"FS-GG/FS.GG.SDD#12\",\"FS-GG/FS.GG.SDD#13\"]}",
            err
        )

        // ACCEPTANCE 3, HALF ONE — `who` reads the SAME board through a completely different path (the
        // marker scan, not the scheduler snapshot) and must arrive at the same number of occupied slots.
        let whoCode, whoOut, _ =
            runQueueWithKit installWaveModel transport [ "who"; "--repo"; "FS.GG.SDD"; "--json" ]

        Assert.Equal(0, whoCode)

        use whoDoc = JsonDocument.Parse whoOut

        let heldRows =
            whoDoc.RootElement.EnumerateArray()
            |> Seq.filter (fun row -> row.GetProperty("state").GetString() = "held")
            |> Seq.map (fun row -> row.GetProperty("number").GetInt32())
            |> Seq.sort
            |> List.ofSeq

        Assert.Equal<int list>([ 1; 2 ], heldRows)

        // Not merely the same COUNT — `who` also does not report the PR-bearing rows as held at all, so
        // the two verbs agree about which items those are.
        Assert.DoesNotContain("\"number\":11", whoOut)

    [<Fact>]
    let ``#2678 occupancy is who's held UNION stale — a lapsed lease still consumes its slot`` () =
        // THE OTHER HALF OF THE `who` RELATION, ON GROUND WHERE IT CAN FAIL (review round 1). Occupancy is
        // NOT `who`'s `held` — that is the LIVE winner only. A marker past its lease is `who`'s `stale`,
        // and it still occupies a slot here, because a lock is a lock and only `reap` breaks it
        // (#461/#581/#1792). Stating that in `Batch.fsi` without a fixture that can refute it would repeat
        // exactly the mistake this repair is fixing.
        //
        // HOW THIS LEG FAILS, MEASURED, BECAUSE IT IS NOT THE OBVIOUS ONE. A marker-backed row reaches
        // `Occupying` through TWO independent sources — the candidate's own `Item.Claim`, and the
        // `live-claim` RESERVATION `Scan.snapshot` writes from the same marker via `Reads.reserver` — and
        // `implementerSlots` unions them. So narrowing only the candidate predicate leaves this leg green
        // (measured: it survives both `Some(_, LeaseHeld)` and an age-vs-lease candidate mutation), while
        // the Core leg `a claim marker occupies its slot whether the lease is live or lapsed` reds on
        // either. Narrowing BOTH sources reds this one: `Not found: "wave occupancy: {"activeItems":2…`.
        // That redundancy is the point of having the leg at CLI level at all — it pins the END-TO-END
        // union over a real scan, not one predicate.
        //
        // #1 is within its lease, #2's marker is 300 minutes old against the default 120-minute lease;
        // the snapshot carries `age=18000, liveness=LivenessUnknown` for #2, measured directly.
        let bodies = [ for n in [ 1; 2; 20 ] -> n, $"Paths: src/%d{n}" ] |> Map.ofList
        let holders = Map.ofList [ 1, "finch-85f3"; 2, "ghost-2678" ]
        let markerAge = Map.ofList [ 2, 300 ]

        let statusFor n =
            if holders.ContainsKey n then "In progress" else "Ready"

        let transport = worldOfWithItemPrsAged statusFor bodies holders markerAge Map.empty

        let code, _, err =
            runQueueWithKit installWaveModel transport [ "batch"; "--repo"; "FS.GG.SDD"; "--json" ]

        Assert.Equal(0, code)

        // BOTH markers occupy: two slots, four open.
        Assert.Contains("wave occupancy: {\"activeItems\":2,\"waveCapacity\":6,\"openSlots\":4}", err)

        // …and neither is `work without claim`, because both are held.
        Assert.DoesNotContain("work without claim:", err)

        let whoCode, whoOut, _ =
            runQueueWithKit installWaveModel transport [ "who"; "--repo"; "FS.GG.SDD"; "--json" ]

        Assert.Equal(0, whoCode)

        use whoDoc = JsonDocument.Parse whoOut

        let byState =
            whoDoc.RootElement.EnumerateArray()
            |> Seq.map (fun row -> row.GetProperty("number").GetInt32(), row.GetProperty("state").GetString())
            |> Seq.sortBy fst
            |> List.ofSeq

        // `who` SPLITS them. Reading `activeItems` as "the count of `held` rows" would be off by one here
        // — the union is the relation, and this is the fixture that says so.
        Assert.Equal<(int * string) list>([ (1, "held"); (2, "stale") ], byState)

    // The snapshot vocabulary the two legs below share. Both drive the PRODUCTION derivations:
    // `Client.slotOccupancyOf` is what `batch` and `driver`'s planner call, and
    // `candidateToItemFacts |> DriverEvents.classify |> isActive` is what `driver --events` calls.
    let private snapshotOf inFlight items =
        let reservations = inFlight |> String.concat ","
        let rows = items |> String.concat ","
        $"""{{"schema":"fsgg.coord.snapshot/1","allowBacklog":false,"inFlight":[%s{reservations}],"items":[%s{rows}]}}"""

    let private claimedRow n status body =
        $"""{{"owner":"FS-GG","repo":"FS.GG.SDD","number":%d{n},"status":"%s{status}","state":"OPEN","body":"%s{body}","claim":{{"worker":"w-%d{n}","ageSeconds":60,"liveness":{{"kind":"lease-held"}}}}}}"""

    let private batchOccupancy doc =
        Client.slotOccupancyOf doc
        |> function
            | Ok s -> s
            | Error e -> failwithf "the snapshot did not parse: %s" e

    let private driverActiveOf merged doc =
        match Snapshot.parse doc with
        | Error e -> failwithf "the snapshot did not parse: %A" e
        | Ok parsed ->
            parsed.Candidates
            |> List.map (Client.candidateToItemFacts Map.empty merged 0L "fixture-sha")
            |> List.map DriverEvents.classify
            |> List.filter (fun c -> DriverEvents.isActive c.State)
            |> List.map (fun c -> c.Ref)

    [<Fact>]
    let ``#2678 on the ordinary in-flight population batch and driver --events agree, over one snapshot`` () =
        // ACCEPTANCE 3, THE OTHER HALF, AND THE ONE THAT NEEDS A SHARED SUBJECT. `who` scans markers and
        // `driver --events` classifies material state; the only way to ask them about the SAME facts
        // rather than about two fixtures is to hand both one snapshot document.
        //
        // THE POPULATION IS NAMED IN THE TITLE FOR A REASON (review round 1). This leg asserts agreement
        // on an OPEN, READABLE, UNPARKED, CLAIMED row — the population the wave model is about, and the
        // one the defect broke. It does NOT assert the two projections are the same function, which is
        // false in both directions; the leg below measures exactly where and how they part.
        let orphanPr n pr =
            $"""{{"owner":"FS-GG","repo":"FS.GG.SDD","number":%d{n},"status":"In review","state":"OPEN","body":"Paths: src/%d{n}","itemPr":%d{pr}}}"""

        let ready n =
            $"""{{"owner":"FS-GG","repo":"FS.GG.SDD","number":%d{n},"status":"Ready","state":"OPEN","body":"Paths: src/%d{n}"}}"""

        // …plus a markerless `In progress` row the assembler reserved as `unowned`. It reserves files and
        // holds no slot, and it is the reservation arm the old predicate also counted.
        let unowned =
            """{"owner":"FS-GG","repo":"FS.GG.SDD","paths":["src/91"],"holder":{"kind":"unowned","owner":"FS-GG","repo":"FS.GG.SDD","number":91}}"""

        let doc =
            snapshotOf
                [ unowned ]
                [ claimedRow 2664 "In progress" "Paths: src/2664"
                  claimedRow 2667 "In progress" "Paths: src/2667"
                  claimedRow 2668 "In progress" "Paths: src/2668"
                  orphanPr 2642 2655
                  orphanPr 2581 2651
                  orphanPr 2645 2650
                  ready 2690 ]

        let slots = batchOccupancy doc
        let driverActive = driverActiveOf Map.empty doc

        Assert.Equal<string list>(
            [ "FS-GG/FS.GG.SDD#2664"; "FS-GG/FS.GG.SDD#2667"; "FS-GG/FS.GG.SDD#2668" ],
            driverActive
        )

        Assert.Equal<string list>(driverActive, slots.Occupying |> List.map (fun r -> r.Canonical))

        // And the residue is accounted for rather than lost: three orphan PRs and the markerless row.
        Assert.Equal<string list>(
            [ "FS-GG/FS.GG.SDD#2642"
              "FS-GG/FS.GG.SDD#2581"
              "FS-GG/FS.GG.SDD#2645"
              "FS-GG/FS.GG.SDD#91" ],
            slots.WorkWithoutClaim |> List.map (fun r -> r.Canonical)
        )

    [<Theory>]
    // `DriverEvents.deriveState` tests `HumanBlock` and `BoardStatus = Blocked` BEFORE the claim match,
    // and `ReadOk` before either, so each of these outranks a live marker over there and not here.
    [<InlineData("Blocked", "Paths: src/7", false, "blocked:board status Blocked")>]
    [<InlineData("In progress", "Paths: src/7\\nBlocked on: human/decision", false, "blocked:Blocked on: human/decision")>]
    [<InlineData("In progress", "Paths: src/7\\nBlocked on: human/action", false, "blocked:Blocked on: human/action")>]
    [<InlineData("In progress", "Paths: src/7", true, "unreadable:the markerless item-PR probe was unreadable")>]
    let ``#2678 a CLAIMED but parked row occupies a slot here and is not active to driver --events``
        (status: string)
        (body: string)
        (itemPrUnreadable: bool)
        (expectedDriverState: string)
        =
        // THE INVARIANT THIS FILE USED TO ASSERT WAS FALSE, AND THE FIXTURE IT ASSERTED IT ON COULD NOT
        // FALSIFY IT (review round 1). These are the shapes where it fails, and they reach the live board:
        // `Scan` admits every non-PR row as a candidate with no status filter, and `check-board`'s
        // `BLOCKER-CLEARED` rule is conditioned on a `Blocked` row's claim precisely because a
        // claimed-and-`Blocked` row exists.
        //
        // THE DIVERGENCE IS DELIBERATE AND THIS SIDE IS THE RIGHT ONE FOR THIS QUESTION. A worker parked
        // on a blocked row is still standing in its lane and only `reap` frees it, so the slot is
        // consumed. A maintainer who reads the two answers and "fixes" the disagreement would be aligning
        // the correct side to the other one — which is why the comments now say which is which.
        let unreadable = if itemPrUnreadable then ""","itemPrUnreadable":true""" else ""

        let row =
            $"""{{"owner":"FS-GG","repo":"FS.GG.SDD","number":7,"status":"%s{status}","state":"OPEN","body":"%s{body}","claim":{{"worker":"w-7","ageSeconds":60,"liveness":{{"kind":"lease-held"}}}}%s{unreadable}}}"""

        let doc = snapshotOf [] [ row ]

        Assert.Equal<string list>(
            [ "FS-GG/FS.GG.SDD#7" ],
            (batchOccupancy doc).Occupying |> List.map (fun r -> r.Canonical)
        )

        Assert.Equal<string list>([], driverActiveOf Map.empty doc)

        // Named, not merely counted: the state is asserted so a change in WHY it diverges is a failure
        // here rather than a silent re-derivation of the same number.
        let observed =
            match Snapshot.parse doc with
            | Error e -> failwithf "the snapshot did not parse: %A" e
            | Ok parsed ->
                parsed.Candidates
                |> List.map (Client.candidateToItemFacts Map.empty Map.empty 0L "fixture-sha")
                |> List.map (fun f -> DriverEvents.encodeState (DriverEvents.classify f).State)

        Assert.Equal<string list>([ expectedDriverState ], observed)

    [<Fact>]
    let ``#2678 a merged row awaiting obligations is active to driver --events and occupies no slot here`` () =
        // AND IT RUNS THE OTHER WAY. `MergedAwaitingObligations` is reached with no claim at all —
        // merged, closed, and at least one declared obligation unverified — so `driver --events` calls it
        // active while this projection correctly occupies nothing: nobody is holding it. The old sentence
        // ("the same population") was false in this direction too.
        let row =
            """{"owner":"FS-GG","repo":"FS.GG.SDD","number":6,"status":"In review","state":"CLOSED","body":"Paths: src/6"}"""

        let doc = snapshotOf [] [ row ]

        let merged: Map<string, int * bool * Delivery.Obligation list> =
            Map.ofList
                [ "FS-GG/FS.GG.SDD#6",
                  (99,
                   true,
                   [ { Id = "o1"
                       Kind = "release-verification"
                       Evidence = None
                       HeadSha = "abc"
                       Verified = false } ]) ]

        Assert.Equal<string list>([ "FS-GG/FS.GG.SDD#6" ], driverActiveOf merged doc)
        Assert.Equal<string list>([], (batchOccupancy doc).Occupying |> List.map (fun r -> r.Canonical))

    [<Fact>]
    let ``#2678 the unknown holder kind parses and lands in neither list; a malformed live-claim refuses`` () =
        // THE REACHABILITY CLAIM THE COMMENT MAKES, MEASURED (review round 1). An earlier draft said
        // `UnknownHolder` was reachable BOTH through the `"unknown"` kind and through the codec's
        // malformed-`live-claim` fallback. Only the first is true: that fallback's
        // `Result.map (fun _ -> UnknownHolder)` is applied to a collected Error, so the snapshot is
        // refused outright and no holder is ever minted.
        let unknownKind =
            snapshotOf [ """{"owner":"FS-GG","repo":"FS.GG.SDD","paths":["src/9"],"holder":{"kind":"unknown"}}""" ] []

        let slots = batchOccupancy unknownKind
        Assert.Equal<Ref list>([], slots.Occupying)
        Assert.Equal<Ref list>([], slots.WorkWithoutClaim)

        let malformedLiveClaim =
            snapshotOf
                [ """{"owner":"FS-GG","repo":"FS.GG.SDD","paths":["src/9"],"holder":{"kind":"live-claim","owner":"FS-GG","repo":"FS.GG.SDD","number":9}}""" ]
                []

        match Client.slotOccupancyOf malformedLiveClaim with
        | Ok s -> failwithf "expected a REFUSAL, got %A" s
        | Error e -> Assert.Contains("inFlight[0].holder.worker: required field is missing", e)

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
              // .github#2459 adds this key to the wire shape; the empty case is what every claim before
              // #2459 would have reported had the key existed, so it is the byte-identical baseline here.
              Collisions = []
              ForcedClaimCensuses = None
              Converged = true }

        Assert.Equal(
            """{"ref":".github#1525","repo":"FS-GG/.github","number":1525,"worker":"snipe-6404","kind":"claimed","markerObserved":true,"markerId":5087533685,"assigneeObserved":null,"status":"In progress","statusRead":"observed","statusWrite":"written","pendingBoardWrites":0,"collisions":[],"converged":true}""",
            Render.renderClaimReceiptJson receipt
        )

    [<Fact>]
    let ``#2772 a non-green forced claim has a typed census receipt`` () =
        let census: Render.ClaimMarkerCensusReceipt =
            { WinnerMarkerId = Some 901L
              Markers =
                [ { MarkerId = 901L
                    Worker = "vole-418"
                    Live = true } ] }

        let receipt: Render.ForcedClaimOutcomeReceipt =
            { Ref =
                { Owner = "FS-GG"
                  Repo = ".github"
                  Number = 2772 }
              Worker = "kite-461"
              Kind = "replacement-post-failed"
              ReplacementMarkerId = None
              StandingWorker = Some "vole-418"
              StandingMarkerId = Some 901L
              RemovedWorkers = []
              FailedWorker = None
              FailedMarkerId = None
              Reason = Some "HTTP 500: post failed"
              ForcedClaimCensuses =
                { Before = census
                  After = Some census } }

        Assert.Equal(
            """{"ref":".github#2772","repo":"FS-GG/.github","number":2772,"worker":"kite-461","kind":"replacement-post-failed","replacementMarkerId":null,"standingWorker":"vole-418","standingMarkerId":901,"removedWorkers":[],"failedWorker":null,"failedMarkerId":null,"reason":"HTTP 500: post failed","forcedClaimCensuses":{"before":{"winnerMarkerId":901,"markers":[{"markerId":901,"worker":"vole-418","live":true}]},"after":{"winnerMarkerId":901,"markers":[{"markerId":901,"worker":"vole-418","live":true}]}}}""",
            Render.renderForcedClaimOutcomeJson receipt
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
    /// nothing"; the remaining reads are a green empty answer.  `claim` now refuses one step earlier on
    /// a missing source-bound route receipt (1): that is the required zero-write admission gate.
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
          Options.Claim, [ "claim"; "FS.GG.SDD#999"; "--worker"; "otter-9c21"; "--json" ], 1
          Options.Adopt, [ "adopt"; "FS.GG.SDD#999"; "--worker"; "otter-9c21"; "--json" ], 3
          Options.Widen, [ "widen"; "FS.GG.SDD#999"; "--worker"; "otter-9c21"; "--json"; "--paths"; "src/X.fs" ], 1
          Options.SetPaths,
          [ "set-paths"; "FS.GG.SDD#999"; "--worker"; "otter-9c21"; "--json"; "--paths"; "src/X.fs" ],
          1
          // `review` refuses before any board/GitHub read: `--pr` is required (.github#2175 — there is
          // no review protocol before a PR exists), so this lands on that refusal deterministically,
          // the same way the four lock verbs above land on theirs.
          Options.ReviewCmd, [ "review"; "FS.GG.SDD#999"; "--worker"; "otter-9c21"; "--json" ], 1
          // `.github#2477`: the fixture answers no GraphQL document naming `userContentEdits` (its
          // `graphqlAnswer` only knows the board-scan shapes), so `Reads.contentEditProvenance` gets a
          // `NotFound` back and the read refuses at `Errors.exitCode NotFound` — 1, the same code the
          // four lock verbs above land on for the same reason: the fixture has no answer for this call.
          Options.BodyEdits, [ "body-edits"; "FS.GG.SDD#999"; "--json" ], 1
          // `.github#2753`: an incomplete comment mutation request is refused before transport. Its
          // JSON projection keeps the diagnostic on stderr and stdout empty at exit 1.
          Options.CommentCmd, [ "comment"; "--json" ], 1
          // .github#2737. `packet validate` opens no transport seam at all, so it is driven here
          // against a path that does not exist: the refusing arm must still put ONE document on
          // stdout, with the per-field reading on stderr.
          Options.PacketCmd, [ "packet"; "validate"; "no-such-packet.json"; "--json" ], 1 ]
          // `.github#2312`. BOTH op-lock verbs are driven onto their fail-closed arm — a receiver with no
          // operation-lock issue — because that arm is reached BEFORE any network call by design ("a
          // receiver with no lock is a guaranteed refusal and must not cost a round trip"), so it is
          // deterministic here whatever the fixture's transport would have answered. Exit 1: `NoLockRef`
          // is a configuration fact somebody must change, not the contended `6` a busy receiver gets.
          @ [ Options.OpLockAcquire,
              [ "op-lock"
                "acquire"
                "FS-GG/.github#2312"
                "5319401108"
                "FS-GG/FS.GG.NotARepo"
                "dispatch:coordination-kit"
                "--worker"
                "otter-9c21"
                "--json" ],
              1
              Options.OpLockRelease,
              [ "op-lock"; "release"; "FS-GG/FS.GG.NotARepo"; "--worker"; "otter-9c21"; "--json" ], 1 ]

    /// The `Json`-admitting verbs this fixture cannot reach, each with the reason and what reading their
    /// arms found. The reason lives HERE rather than in a PR body because that is the whole argument of
    /// this block: the coverage leg quotes it, so moving a verb between the two lists costs a line in a
    /// diff instead of costing nothing.
    let private notDriven: (Options.Command * string) list =
        [ Options.Decide,
          "`Program.fs` `decide` is private to the entry point; audited by reading — under `Json` the SAME `printfn (Snapshot.render …)` runs for all three verdicts and Red/NoVerdict only pick the exit code, so there is no verdict that swaps the document for prose (the eprint-per-verdict projection is `renderText`, which is the Text arm). Its two refusal arms, empty stdin and an unparseable snapshot, are `eprint` at a non-zero code"
          Options.DeliveryCmd,
          "`Program.fs` dispatches the private `DeliveryApplication.run`; audited by reading and the delivery command tests. Its JSON arm serializes exactly one next/no-verdict document, and empty or malformed snapshots are stderr failures at a non-zero code. The command is pure snapshot interpretation; live GitHub acquisition remains an application-boundary follow-on."
          Options.CycleCmd,
          "`Program.fs` dispatches `CycleLedgerApplication.run`; audited by cycle-ledger command smoke coverage. Its Json projection serializes exactly one ready/next document, while malformed documents and every fail-closed provider or ledger mismatch use `fail` on stderr at a non-zero code."
          Options.LanesView,
          "`Program.fs` `lanes` is private to the entry point; audited by reading — `| Json -> printfn` emits one `Snapshot.renderLanes` document, and the empty partition renders as that document, not prose"
          Options.Facts,
          "`Program.fs` `facts` is private to the entry point; audited by reading — it reads nothing and cannot be empty, and `| Json -> printfn` emits one `Snapshot.renderFacts` document"
          Options.Scan,
          "`Program.fs` `scan` is private to the entry point; `JsonOnly`, and audited by reading — BOTH arms print the same snapshot document, and every failure is an `Error` on stderr at a non-zero code (never an empty snapshot, #344/#421/#461)"
          Options.CommandContractCmd,
          "`Program.fs` dispatches `renderCommandContract ()` inline; `JsonOnly`, it reads nothing, and `CommandSurfaceTests` already parses the emitted document"
          Options.Issues,
          "`Handlers.issues` is the family-owned handler; audited by reading — stdout is the raw REST body (`[]` on a repo with no issues), and BOTH its refusal arms are stderr at a non-zero code: the missing-repo refusal and the read failure, the latter through `fail` so a rate limit keeps EX_RATE"
          Options.IntakeCmd,
          "`Handlers.intakeCmd` is the family-owned handler and is audited by transaction tests. Its validate arm emits one typed zero-write receipt, while apply emits one receipt-bound issue/projection result; malformed drafts and unreadable receipts fail on stderr before a POST."
          Options.RouteCmd,
          "`Client.deliveryRouteCmd` is private; audited by reading pending its recording-transport fixture. Its show arm emits one typed current receipt only after reading both the body and append-only comment ledger; record validates the source-bound receipt before its sole comment POST, and malformed or unreadable evidence fails on stderr."
          Options.GraphQlOps,
          "`Client.graphQlOps` is the JSON-only operational facade over `OperationalGraphQl`; the typed boundary fault, pagination, duplicate, repeated-cursor and partial-mutation cases are driven in GraphQlBoundaryTests, while every migrated shell/Python consumer has an executable integration fixture."
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
                | Options.BoardCmd -> Handlers.boardCmd ctx
                | Options.Who -> Client.who ctx opts
                | Options.Inbox -> Handlers.inbox ctx opts
                | Options.Budget -> Client.budget ctx opts
                | Options.Predicate -> Client.predicate opts
                | Options.Claim -> Client.claim ctx opts
                | Options.Adopt -> Client.adopt ctx opts
                | Options.Widen -> Client.widen ctx opts
                | Options.SetPaths -> Client.setPaths ctx opts
                | Options.ReviewCmd -> FS.GG.Coord.Cli.Lifecycle.LiveHandlers.review ctx opts
                | Options.BodyEdits -> Handlers.bodyEditsCmd ctx opts
                | Options.CommentCmd -> Handlers.commentCmd ctx opts
                | Options.PacketCmd -> PacketApplication.run opts
                | Options.OpLockAcquire -> Client.opLockAcquire ctx opts
                | Options.OpLockRelease -> Client.opLockRelease ctx opts
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

    // Round-2 review repair (.github#2264 PR #2271): `coord-board-reconcile.yml` runs `reconcile --apply`
    // unattended, on a schedule, forever — against a Projects v2 board this repo's default `GITHUB_TOKEN`
    // cannot see (.github#2332, org-level, not fixable from this tree). Before this repair, that
    // condition hard-errored (exit 1) indistinguishably from any other reconcile failure, which is an
    // always-red unattended gate — `.github#1611`/`#1582`'s "trains a reader to ignore red" shape. These
    // tests pin `Client.boardUnreachable`'s classification, the seam the workflow step's `set +e` / rc
    // check (mapping ExitNoVerdict to `::warning::` + exit 0) depends on to stay a real distinction.
    [<Fact>]
    let ``#2264 round 2: a board-not-found error classifies as unreachable, not a generic finding`` () =
        let boardGap = Errors.NotFound "no Projects v2 board titled 'Coordination' in FS-GG"
        Assert.True(Client.boardUnreachable boardGap)

    [<Fact>]
    let ``#2264 round 2: an unrelated NotFound is a real finding, not a credential gap`` () =
        // The distinction is READING THE SUBJECT, not "any NotFound is soft" — an issue that genuinely
        // does not exist must stay a real, loud exit-1 finding for every OTHER reconcile caller
        // (Errors.exitCode's own comment: collapsing this would let a mistyped repo masquerade as an
        // un-boarded item). Only `Board.bootstrap`'s own exact message shape is a credential gap.
        Assert.False(Client.boardUnreachable (Errors.NotFound "issue FS-GG/.github#999999 not found"))

    [<Fact>]
    let ``#2264 round 2: rate limits and other IO error shapes are not board-unreachable`` () =
        Assert.False(Client.boardUnreachable (Errors.RateLimited(Errors.GraphQlBudget, None)))
        Assert.False(Client.boardUnreachable (Errors.Transport "connection reset"))
        Assert.False(Client.boardUnreachable (Errors.Unauthorized "no Projects v2 board titled 'Coordination' in FS-GG"))

    // ---- .github#2525 acceptance #2 — "nothing schedulable" must mean MEASURED nothing --------------
    //
    // `batch --text` over an empty candidate set printed one bare sentence on stdout, NOTHING on stderr,
    // and exit 0 — byte-identical to a board that was fully read and had nothing startable in it. Every
    // explanatory surface here (`sayPassedOver`, `starvedBanner`, `explainRanking`) is keyed on
    // `Decisions`, so all three fall silent on exactly the case that most needs explaining.
    //
    // The count rides on STDERR, and that placement is the .github#1562 property, not an afterthought:
    // the stdout headline is pinned byte-for-byte on both `take` and `batch --text` just above, and a
    // change that moved both streams at once would have gone green everywhere.

    [<Fact>]
    let ``.github#2525: batch --text states how many candidates it MEASURED, on stderr, leaving stdout byte-identical`` () =
        let code, out, err =
            runQueue (busyQueue ()) [ "batch"; "--text"; "--repo"; "FS.GG.SDD"; "-n"; "1" ]

        // The pinned machine contract is untouched — same assertion as the #1562 leg above.
        Assert.Equal("nothing schedulable right now." + Environment.NewLine, out)
        Assert.Equal(0, code)

        // …and the answer to "why nothing" now includes the measurement itself.
        Assert.Contains("considered 1 candidate(s)", err)
        Assert.Contains("measured count, not an assumption", err)

    [<Fact>]
    let ``.github#2525: a board with NO items reports zero considered — distinguishable from one that considered and refused`` () =
        // The distinction the acceptance is about. "I considered 1 and refused it" and "I considered
        // nothing" produced identical output before this; they are different facts, and only the second
        // is consistent with a scan that came back short.
        let code, out, err =
            runQueue (emptyQueue ()) [ "batch"; "--text"; "--repo"; "FS.GG.SDD"; "-n"; "1" ]

        Assert.Equal("nothing schedulable right now." + Environment.NewLine, out)
        Assert.Equal(0, code)
        Assert.Contains("considered 0 candidate(s)", err)

    [<Fact>]
    let ``.github#2525: a batch that CHOOSES work does not print the measured-count line`` () =
        // The controlled counterpart. This line answers "why did I get nothing"; printing it on a
        // successful batch would be noise, and noise on a healthy path is how a signal stops being read.
        let schedulable =
            worldIn "Ready" (Map.ofList [ 74, "Paths: scripts/fsgg-coord" ]) Map.empty false

        let _, out, err =
            runQueue schedulable [ "batch"; "--text"; "--repo"; "FS.GG.SDD"; "-n"; "1" ]

        Assert.Contains("FS.GG.SDD#74", out)
        Assert.DoesNotContain("nothing schedulable right now.", out)
        Assert.DoesNotContain("measured count, not an assumption", err)

    // ---- .github#2690: the operator-writable intent channel, end to end -------------------------------
    //
    // WHY THESE LEGS ARE A `set-field` FOLLOWED BY A `reconcile`, AND CANNOT BE EITHER ALONE. The defect is
    // invisible inside one command: `set-field` writes the column, exits 0, and the board is right at that
    // instant. It is the NEXT reducer pass that recomputes the row from inputs the operator never touched
    // and reverts it — ten minutes later, on `coord-board-reconcile.yml`'s `17 * * * *`. So the subject
    // here is the SEAM, and the only thing that crosses it is a comment on the issue. That is why the
    // fixture's comment list is mutable and is read back: served a canned `[]`, every assertion below
    // passes identically with the fix reverted, which is `#1772`'s shape — a fixture testing a hand-written
    // mirror of its subject instead of the subject.

    /// The frozen receipt measured on `.github#2695`, byte for byte, at `2026-08-16T10:22:40Z`.
    ///
    /// Copied rather than constructed, because it IS the evidence. Its `observedAt` had advanced from the
    /// `01:29:57Z` receipt (`1786843796660`) to `1786875759540` while its `revision` had NOT — the reducer
    /// reuses the prior `IntentRecord` by value — so the bot re-asserted a nine-hour-old
    /// `decision-class work requires a human decision` against a row whose `Class` had read `hardening` for
    /// ten minutes. It did not re-judge; it structurally cannot, because a watermark's mere EXISTENCE
    /// suppresses the policy re-derivation the reclass would have changed (`Client.lifecycleSelection`).
    let private FrozenDecisionWatermark =
        "<!-- fsgg:lifecycle-watermark v=2 observedAt=1786875759540 status=Blocked intent=human-decision revision=1786843796660 until=none reason=decision-class%20work%20requires%20a%20human%20decision -->"

    /// One row whose column really changes and whose comments really accumulate.
    type private IntentBoard(startColumn: string, seeded: string list) =
        let comments = ResizeArray<string> seeded
        let mutable column = startColumn

        member _.Column = column
        member _.SetColumn(value: string) = column <- value
        member _.Comments = List.ofSeq comments
        member _.Post(body: string) = comments.Add body

        /// Only whole-body watermark receipts — `tryWatermark`'s own anchored rule, so a comment that
        /// merely quotes one is not counted here either.
        member _.Watermarks =
            comments
            |> Seq.map (fun b -> b.Trim())
            |> Seq.filter (fun b -> b.StartsWith "<!-- fsgg:lifecycle-watermark" && b.EndsWith "-->")
            |> List.ofSeq

    let private StatusOptionNames =
        Map.ofList
            [ "opt_backlog", "Backlog"
              "opt_ready", "Ready"
              "opt_blocked", "Blocked"
              "opt_wip", "In progress"
              "opt_rev", "In review"
              "opt_done", "Done" ]

    let private intentChannelWorld (board: IntentBoard) (issueBody: string) =
        let encodedBody = JsonSerializer.Serialize issueBody

        let item () =
            $"""{{"status":{{"name":"%s{board.Column}"}},"blockedBy":null,"class":{{"name":"hardening"}},"content":{{"__typename":"Issue","number":47,"title":"an operator-parked row","state":"OPEN","repository":{{"nameWithOwner":"FS-GG/FS.GG.SDD"}},"body":%s{encodedBody}}}}}"""

        // .github#2698: the row's receipt rides in front of whatever the board has posted. It is prepended
        // rather than seeded per-test because EVERY leg here drives a lifecycle Status write, and
        // `board.Watermarks` filters on the watermark marker, so it cannot disturb what these legs assert.
        let commentsJson () =
            (currentRouteComment "FS-GG/FS.GG.SDD#47" "" :: board.Comments)
            |> List.mapi (fun i body ->
                $"""{{"id":%d{9000 + i},"html_url":"https://example.invalid/c%d{i}","body":%s{JsonSerializer.Serialize body}}}""")
            |> String.concat ","
            |> fun rows -> $"[%s{rows}]"

        Fake.Recorder(fun (req: Request) ->
            match req.Method, req.Path.Trim '/' with
            | "GET", "rate_limit" -> ok """{"resources":{"graphql":{"remaining":4980,"limit":5000}}}"""
            | "POST", "graphql" ->
                match req.Body with
                | Query(document, variables) ->
                    // THE DISPATCH ORDER IS `humanParkReconcileWorld`'S, DELIBERATELY. The board scan's own
                    // `items(first: 100` document also selects `fieldValueByName`, so keying the per-item
                    // read on that substring swallows the whole-board read and the pass fails as a
                    // malformed response rather than as the thing under test.
                    if document.Contains "projectItems" then
                        ok """{"data":{"repository":{"issue":{"projectItems":{"nodes":[{"id":"PVTI_47","project":{"number":12}}]}}}},"rateLimit":{"cost":1,"remaining":4977}}"""
                    elif document.Contains "node(id: $itemId)" then
                        let field =
                            variables
                            |> List.tryPick (fun (k, v) -> match k, v with | "field", VString name -> Some name | _ -> None)

                        let value =
                            match field with
                            | Some "Status" -> $"""{{"name":"%s{board.Column}"}}"""
                            | Some "Class" -> """{"name":"hardening"}"""
                            | _ -> "null"

                        ok $"""{{"data":{{"node":{{"fieldValueByName":%s{value}}}}},"rateLimit":{{"cost":1,"remaining":4977}}}}"""
                    elif document.Contains "updateProjectV2ItemFieldValue" then
                        // THE MUTATION MOVES THE FIXTURE'S OWN COLUMN. A board that did not change would
                        // answer every later read with the pre-write value, and the revert these legs are
                        // about would be unobservable — the fixture would agree with the defect.
                        let fieldId =
                            variables |> List.tryPick (fun (k, v) -> match k, v with | "fieldId", VId id -> Some id | _ -> None)

                        let optionId =
                            variables |> List.tryPick (fun (k, v) -> match k, v with | "optionId", VString id -> Some id | _ -> None)

                        match fieldId, optionId |> Option.bind (fun id -> Map.tryFind id StatusOptionNames) with
                        | Some "PVTSSF_status", Some name -> board.SetColumn name
                        | _ -> ()

                        ok """{"data":{"updateProjectV2ItemFieldValue":{"clientMutationId":null}},"rateLimit":{"cost":1,"remaining":4977}}"""
                    elif document.Contains "projectsV2" then
                        ok """{"data":{"organization":{"projectsV2":{"nodes":[{"number":12,"title":"Coordination","id":"PVT_coord"}]}}},"rateLimit":{"cost":1,"remaining":4977}}"""
                    elif document.Contains "fields(first" then
                        ok """{"data":{"organization":{"projectV2":{"fields":{"nodes":[{"id":"PVTSSF_status","name":"Status","dataType":"SINGLE_SELECT","options":[{"id":"opt_backlog","name":"Backlog"},{"id":"opt_ready","name":"Ready"},{"id":"opt_blocked","name":"Blocked"},{"id":"opt_wip","name":"In progress"},{"id":"opt_rev","name":"In review"},{"id":"opt_done","name":"Done"}]},{"id":"PVTSSF_class","name":"Class","dataType":"SINGLE_SELECT","options":[{"id":"opt_defect","name":"defect"},{"id":"opt_hard","name":"hardening"},{"id":"opt_dec","name":"decision"}]},{"id":"PVTF_blocked","name":"Blocked by","dataType":"TEXT"}]}}}},"rateLimit":{"cost":1,"remaining":4977}}"""
                    elif document.Contains "items(first" then
                        ok $"""{{"data":{{"organization":{{"projectV2":{{"items":{{"pageInfo":{{"hasNextPage":false,"endCursor":null}},"nodes":[%s{item ()}]}}}}}}}},"rateLimit":{{"cost":1,"remaining":4977}}}}"""
                    else
                        Error(Errors.NotFound $"the fixture serves no answer for: %s{document}")
                | _ -> Error(Errors.NotFound "a graphql call with no document")
            | "GET", "repos/FS-GG/FS.GG.SDD/issues/47" ->
                ok (JsonSerializer.Serialize {| number = 47; body = issueBody |})
            | "GET", "repos/FS-GG/FS.GG.SDD/issues/47/comments" -> ok (commentsJson ())
            | "POST", "repos/FS-GG/FS.GG.SDD/issues/47/comments" ->
                match req.Body with
                | Json payload ->
                    use doc = JsonDocument.Parse payload

                    match doc.RootElement.TryGetProperty "body" with
                    | true, value -> board.Post(value.GetString())
                    | _ -> failwith "the engine posted a comment with no body"

                    ok """{"id":9047}"""
                | _ -> Error(Errors.NotFound "a comment POST with no JSON payload")
            | "GET", "repos/FS-GG/FS.GG.SDD/issues" -> ok "[]"
            | "GET", "repos/FS-GG/FS.GG.SDD/pulls" -> ok "[]"
            | "GET", "repos/FS-GG/FS.GG.SDD/git/matching-refs/heads/item/47-" -> ok "[]"
            | m, p -> Error(Errors.NotFound $"the fixture serves no %s{m} %s{p}"))

    /// The declaration that makes `lifecyclePolicyIntent` answer `Auto` — declared paths, no human hold.
    /// It is what makes both legs below adversarial: policy, left to itself, promotes this row to `Ready`.
    let private SchedulableBody = "Paths: src/A.fs\n\nClass: hardening"

    [<Fact>]
    let ``.github#2690 direction A: a deliberate Backlog park survives the next reconcile pass`` () =
        let board = IntentBoard("Ready", [])
        let world = intentChannelWorld board SchedulableBody

        let setCode, _, setErr =
            runReconcile world [ "set-field"; "FS.GG.SDD#47"; "Status"; "Backlog"; "--worker"; "rook-2690" ]

        if setCode <> 0 then
            failwithf "the park write itself failed: %s" setErr

        Assert.Equal("Backlog", board.Column)

        // THE RECEIPT, NOT THE PROSE. `set-field` printed "set … = Backlog" before this change too; what
        // was missing is the durable intent, so that is what is asserted.
        Assert.Equal(1, List.length board.Watermarks)
        Assert.Contains("intent=backlog", List.exactlyOne board.Watermarks)
        Assert.Contains("status=Backlog", List.exactlyOne board.Watermarks)

        // AND NOW THE PASS THAT USED TO REVERT IT. `Paths: src/A.fs` is a schedulable declaration and the
        // row carries no human hold, so `lifecyclePolicyIntent` answers `Auto` — which projects `Ready`.
        // Only the receipt above stands between the operator's park and that promotion.
        let reconcileCode, _, reconcileErr = runReconcile world (reconcileArgs [ "--apply" ])

        if reconcileCode <> 0 then
            failwithf "the reconcile pass failed: %s" reconcileErr

        Assert.Equal("Backlog", board.Column)

    [<Fact>]
    let ``.github#2690 direction B: an explicit Ready outranks the frozen park that was re-parking the row`` () =
        // `.github#2695`, reproduced. The row is `Blocked` under the frozen `human-decision` receipt
        // measured on it; its `Class` reads `hardening` and has for ten minutes; and the operator schedules
        // it. Before this change the next pass read that receipt back — the ONLY intent on the row — and
        // re-parked it, because `lifecycleSelection` consults policy only when there is no watermark at all.
        let board = IntentBoard("Blocked", [ FrozenDecisionWatermark ])
        let world = intentChannelWorld board SchedulableBody

        let setCode, _, setErr =
            runReconcile world [ "set-field"; "FS.GG.SDD#47"; "Status"; "Ready"; "--worker"; "rook-2690" ]

        if setCode <> 0 then
            failwithf "the schedule write itself failed: %s" setErr

        Assert.Equal("Ready", board.Column)
        Assert.Equal(2, List.length board.Watermarks)

        let recorded = board.Watermarks |> List.last
        Assert.Contains("intent=auto", recorded)
        Assert.Contains("status=Ready", recorded)

        // THE FROZEN RECEIPT IS STILL THERE, and that is the point: nothing was deleted or rewritten. The
        // channel wins on ORDER — `tryWatermark` sorts by `observedAt` — so the repair is additive and a
        // reader can still see what the row used to believe.
        Assert.Contains(FrozenDecisionWatermark, board.Watermarks)

        let reconcileCode, _, reconcileErr = runReconcile world (reconcileArgs [ "--apply" ])

        if reconcileCode <> 0 then
            failwithf "the reconcile pass failed: %s" reconcileErr

        Assert.Equal("Ready", board.Column)

    [<Fact>]
    let ``.github#2690 Blocked records NO intent, so clearing its cause still lifts the park`` () =
        // THE DELIBERATE `None`, ASSERTED AT THE CLI BOUNDARY rather than only in the pure rule — a wiring
        // that reached past `explicitStatusWatermark` would satisfy the Core test and still fail here.
        // `Blocked` never had this defect (its coherent-park gate demands a durable cause that policy
        // re-derives every pass), and minting a `HumanPark` for it would freeze a park that
        // `projectWithIntent` deliberately lets outrank live facts — unliftable by closing the very blocker
        // that justified it.
        let board = IntentBoard("Ready", [])
        let world = intentChannelWorld board "Paths: src/A.fs\n\nBlocked on: human/action"

        let code, _, err =
            runReconcile world [ "set-field"; "FS.GG.SDD#47"; "Status"; "Blocked"; "--worker"; "rook-2690" ]

        if code <> 0 then
            failwithf "a coherent human park must still be writable: %s" err

        Assert.Equal("Blocked", board.Column)
        Assert.Empty(board.Watermarks)

    // ================================================================================================
    // .github#2712 — THE EXEMPTION THROUGH `reconcile`, WHICH IS THE ONLY THING THAT RUNS IT
    //
    // `classWorld`'s own note above states the rule this section obeys: *"A rule exercised only where it
    // is DERIVED is a rule nobody has watched run."* `LifecycleProjectionTests` proves the reducer answers
    // `Exempt`; only this proves the CLI then writes neither a `Status` column nor a lifecycle watermark —
    // and the watermark half is unreachable from Core, because Core never writes one.
    // ================================================================================================

    /// One OPEN row on the board whose body may or may not declare `Kind: register`, carrying a
    /// pre-existing lifecycle watermark whose intent WOULD drive a transition, and a claim-free,
    /// blocker-free, PR-free set of observations that would otherwise project `Ready`.
    ///
    /// The `postedComments` sink is what makes the watermark assertion possible at all: `Fake.Recorder`'s
    /// log summarises requests and does not carry bodies, so "no watermark was written" cannot be read out
    /// of it. The fixture records the bodies it is asked to post.
    let private kindReconcileWorld (declareKind: bool) (postedComments: ResizeArray<string>) =
        let mutable status = "Blocked"
        let body =
            if declareKind then "Paths: none\nKind: register\n" else "Paths: none\n"

        // A FROZEN BACKLOG PARK, at an `observedAt` in the past. On a `work` row this is exactly the
        // .github#2690 shape: `lifecycleSelection` replays the receipt's intent rather than re-deriving
        // policy, so the row projects `Backlog` and MOVES off its stale `Blocked` column. It is here so
        // the exempt assertion cannot pass merely because nothing was going to happen anyway.
        //
        // `backlog` rather than `auto` DELIBERATELY: promoting a row to `Ready` is separately refused
        // without a current delivery-route receipt (.github#2698), so an `auto` control would be blocked
        // by a gate that has nothing to do with this exemption and would prove nothing about it. A park
        // needs no receipt, so what this control observes is the lifecycle projection itself.
        let watermark =
            "<!-- fsgg:lifecycle-watermark v=2 observedAt=1 status=Backlog intent=backlog revision=1 until=none reason=operator%20park -->"

        let items () =
            [ boardItemInWithBody status 47 "a standing register" None "OPEN" body ] |> String.concat ","

        Fake.Recorder(fun (req: Request) ->
            match req.Method, req.Path.Trim '/' with
            | "GET", "rate_limit" -> ok """{"resources":{"graphql":{"remaining":4980,"limit":5000}}}"""
            | "POST", "graphql" ->
                match req.Body with
                | Query(document, variables) ->
                    if document.Contains "projectItems" then
                        ok """{"data":{"repository":{"issue":{"projectItems":{"nodes":[{"id":"PVTI_47","project":{"number":12}}]}}}},"rateLimit":{"cost":1,"remaining":4977}}"""
                    elif document.Contains "node(id: $itemId)" then
                        let field = variables |> List.tryPick (fun (k, v) -> match k, v with | "field", VString name -> Some name | _ -> None)
                        let value =
                            match field with
                            | Some "Status" -> $"""{{"name":"%s{status}"}}"""
                            | _ -> "null"
                        ok ("{\"data\":{\"node\":{\"fieldValueByName\":" + value + "}},\"rateLimit\":{\"cost\":1,\"remaining\":4977}}")
                    elif document.Contains "updateProjectV2ItemFieldValue" then
                        // The repair landed: the fresh verification read below observes the park.
                        status <- "Backlog"
                        ok """{"data":{"f0":{"clientMutationId":null}}}"""
                    elif document.Contains "projectsV2" then
                        ok """{"data":{"organization":{"projectsV2":{"nodes":[{"number":12,"title":"Coordination","id":"PVT_coord"}]}}},"rateLimit":{"cost":1,"remaining":4977}}"""
                    elif document.Contains "fields(first" then
                        // NO `Kind` FIELD, which is the state of every board in this org today — so the
                        // `KIND-PROJECTION-LAG` this row would otherwise derive is withheld behind one
                        // diagnostic, and the only thing left to observe is the lifecycle behaviour.
                        ok """{"data":{"organization":{"projectV2":{"fields":{"nodes":[{"id":"PVTSSF_status","name":"Status","dataType":"SINGLE_SELECT","options":[{"id":"opt_ready","name":"Ready"},{"id":"opt_backlog","name":"Backlog"},{"id":"opt_blocked","name":"Blocked"},{"id":"opt_done","name":"Done"}]},{"id":"PVTF_blocked","name":"Blocked by","dataType":"TEXT"}]}}}},"rateLimit":{"cost":1,"remaining":4977}}"""
                    elif document.Contains "items(first" then
                        ok $"""{{"data":{{"organization":{{"projectV2":{{"items":{{"pageInfo":{{"hasNextPage":false,"endCursor":null}},"nodes":[%s{items ()}]}}}}}}}},"rateLimit":{{"cost":1,"remaining":4977}}}}"""
                    else
                        Error(Errors.NotFound $"the fixture serves no answer for: %s{document}")
                | _ -> Error(Errors.NotFound "a graphql call with no document")
            | "GET", "repos/FS-GG/FS.GG.SDD/issues/47" ->
                ok (System.Text.Json.JsonSerializer.Serialize {| number = 47; body = body |})
            | "GET", "repos/FS-GG/FS.GG.SDD/issues/47/comments" ->
                ok (System.Text.Json.JsonSerializer.Serialize [| {| id = 1; body = watermark |} |])
            | "POST", "repos/FS-GG/FS.GG.SDD/issues/47/comments" ->
                postedComments.Add(
                    match req.Body with
                    | Json payload -> payload
                    | _ -> "<non-json comment>")
                ok """{"id":9047}"""
            | "GET", "repos/FS-GG/FS.GG.SDD/issues" -> ok "[]"
            | "GET", "repos/FS-GG/FS.GG.SDD/pulls" -> ok "[]"
            | "GET", "repos/FS-GG/FS.GG.SDD/git/matching-refs/heads/item/47-" -> ok "[]"
            | m, p -> Error(Errors.NotFound $"the fixture serves no %s{m} %s{p}"))

    [<Fact>]
    let ``2712 reconcile --apply writes NO Status and NO watermark for a standing row`` () =
        let posted = ResizeArray<string>()
        let world = kindReconcileWorld true posted
        let code, out, err = runReconcile world (reconcileArgs [ "--apply"; "--json" ])

        if code <> 0 then failwithf ".github#2712 reconcile failed (exit %d): %s\n%s" code err out

        // NO STATUS ON THE WIRE. Read from the transport log — the option ids GitHub would have received
        // — rather than from the receipt, which is the engine describing itself.
        Assert.False(world.Logged "opt_ready", $"a standing row was projected to Ready: %A{world.Log}")
        Assert.False(world.Logged "opt_backlog", $"a standing row was projected to Backlog: %A{world.Log}")
        Assert.False(world.Logged "opt_blocked", $"a standing row was projected to Blocked: %A{world.Log}")
        Assert.False(world.Logged "opt_done", $"a standing row was projected to Done: %A{world.Log}")

        // NO WATERMARK — the half AC2 names last and the half Core cannot assert, because Core never
        // writes one. A receipt here would be a durable ordering fact about a lifecycle the row does not
        // have, and `tryWatermark` would re-assert it with a fresh `ObservedAt` on every later pass.
        Assert.DoesNotContain(posted, fun c -> c.Contains "fsgg:lifecycle-watermark")

        // AND NO CHORE WAS EVEN DERIVED. The wire assertions above would also hold if a chore had been
        // derived and then refused for some unrelated reason; this says the reducer produced nothing.
        Assert.DoesNotContain("LIFECYCLE-PROJECTION-LAG", out)

    [<Fact>]
    let ``2712 NON-VACUITY — the identical world WITHOUT the Kind line does write both`` () =
        // THE LEG THAT MAKES THE TEST ABOVE EVIDENCE. The two fixtures differ in exactly one line of one
        // issue body. Without this, a world that had simply stopped producing a lifecycle chore — a
        // broken fixture, a changed reducer, a mis-served route — would pass every assertion above while
        // observing nothing at all.
        let posted = ResizeArray<string>()
        let world = kindReconcileWorld false posted
        let code, out, err = runReconcile world (reconcileArgs [ "--apply"; "--json" ])

        if code <> 0 then failwithf ".github#2712 control fixture failed (exit %d): %s\n%s" code err out

        Assert.True(
            world.Logged "opt_ready" || world.Logged "opt_backlog" || world.Logged "opt_blocked" || world.Logged "opt_done",
            $"the control row projected no Status at all, so the exempt assertion observes nothing: %A{world.Log}")

        // AND THE RECEIPT NAMES THE CHORE the exempt world must not produce, so the two fixtures are
        // shown to differ in the reducer's own decision and not only in what reached the wire.
        Assert.Contains("LIFECYCLE-PROJECTION-LAG", out)

        Assert.Contains(posted, fun c -> c.Contains "fsgg:lifecycle-watermark")
