namespace FS.GG.Coord.Cli.Tests

open System
open System.Collections.Generic
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json
open Xunit
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Transport
open FS.GG.Coord.Cli

/// THE `--force` AGREEMENT GATE (.github#1620).
///
/// `WriteTests` pins the CAS: `StealLiveHolder` evicts a live holder and reports `Stolen`. That is the
/// mechanism. THIS file pins the thing that was actually broken, which was never the mechanism —
/// **`--force` on argv did not reach it.**
///
/// The defect, exactly. `adopt`'s live-claim refusal ended with
///
///     If you genuinely mean to take it anyway, that is a steal, and the flag says so:
///       scripts/fsgg-coord claim .github#1596 --force
///
/// and the usage block advertised `claim <ref> [--worker W] [--force] [--json]`. Following that
/// instruction verbatim printed `.github#1596 is already held by snipe-f893. Pick another, or wait for
/// the lease.` and exited 3 — identically with and without the flag. `--force` was read in exactly ONE
/// place, `claim`'s #516 one-item-per-worker pre-check, so its implemented meaning was "let me hold TWO
/// items" while its advertised meaning was "let me take SOMEONE ELSE'S item". Different powers; only the
/// first existed.
///
/// This is #991's shape recurring inside #991's own repair. #991 built `scopeOf` after finding
/// `release --force` documented and read by nothing, and `scopeOf` was RIGHT about `--force`: one reader,
/// `claim`, exactly as it says. A scope table cannot see a flag that reaches the right command and is
/// then wired to the wrong DECISION inside it. So the table is not the gate for this, and a test that
/// only drove `Writes.claim` directly would not be either — it would pass while argv's `--force` went
/// on doing nothing.
///
/// **The gate is therefore a PAIR: one board, two argv lines, two outcomes.** If these two tests ever
/// agree again, `--force` has drifted back to a no-op, and it costs a red build rather than an operator
/// discovering it mid-recovery with a dead holder's release PR stranded on the item.
module ForceStealTests =

    let private currentRouteComment () =
        let source = "Paths: src/Thing.fs"
        let revision = SHA256.HashData(Encoding.UTF8.GetBytes source) |> Convert.ToHexString |> _.ToLowerInvariant()
        "<!-- fsgg:delivery-route/v1 -->\n"
        + $"""{{"schema":"fsgg.coord.delivery-route/v1","subject":"FS-GG/FS.GG.SDD#42","subjectRevision":"%s{revision}","route":"lightweight","agent":"fixture-force","timestamp":"2026-01-01T00:00:00Z","reasonCodes":["fixture"],"rationale":"fixture route receipt","declaredImpacts":["internal"],"observedFacts":["localized"],"sddWorkId":null,"specHome":null,"requiredGates":[]}}"""

    let private ok (body: string) : Errors.IoResult<Response> =
        Ok
            { Status = 200
              Body = body
              ETag = None
              NextLink = None; Headers = Map.empty }

    /// The board the fixture serves — enough of one that `claim` can bootstrap and read a column back.
    /// The Status WRITE is deliberately not served: it fails, the receipt reports `statusWrite:"failed"`,
    /// and the claim still exits green, because the LOCK is the thing under test here and `Client.claim`
    /// already treats the column as a separate observation (#1369). A fixture that served the mutation
    /// would be testing `Board`, which `BoardTests` owns.
    let private graphqlAnswer (query: string) : string option =
        if query.Contains "projectsV2" then
            Some
                """{"data":{"organization":{"projectsV2":{"nodes":[{"number":12,"title":"Coordination","id":"PVT_coord"}]}}},"rateLimit":{"cost":1,"remaining":4977}}"""
        elif query.Contains "fields(first" then
            Some
                """{"data":{"organization":{"projectV2":{"fields":{"nodes":[{"id":"PVTSSF_status","name":"Status","dataType":"SINGLE_SELECT","options":[{"id":"opt_ready","name":"Ready"},{"id":"opt_wip","name":"In progress"}]}]}}}},"rateLimit":{"cost":1,"remaining":4977}}"""
        elif query.Contains "items(first" then
            Some
                """{"data":{"organization":{"projectV2":{"items":{"pageInfo":{"hasNextPage":false,"endCursor":null},"nodes":[{"status":{"name":"In progress"},"blockedBy":null,"content":{"__typename":"Issue","number":42,"title":"item 42","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}}]}}}},"rateLimit":{"cost":1,"remaining":4977}}"""
        else
            None

    /// A LIVE comment thread on issue #42, mutated by the requests the way GitHub would mutate it: a
    /// DELETE removes a comment, a POST adds one with the next id. A canned SEQUENCE would not do here —
    /// the whole question is whether the eviction and the post happen at all, and in that order, and a
    /// scripted transport answers that by construction rather than by observation.
    ///
    /// `posted` keeps every comment BODY, which the `gh`-grammar log deliberately does not carry. The
    /// theft notice is the obligation this issue's decision put above the mechanism — "a steal is
    /// recorded on the ITEM, not just in a receipt" — so a test that could not read the body could not
    /// assert the part that matters most.
    type private Thread(holder: string option) =
        let comments = Dictionary<int64, string>()
        let posted = ResizeArray<string>()
        let mutable nextId = 9000L

        do
            match holder with
            | Some w -> comments.[8042L] <- $"<!-- fsgg:claim worker=%s{w} lease=120 -->\\nheld"
            | None -> ()

        member _.Posted = List.ofSeq posted
        member _.Ids = comments.Keys |> List.ofSeq

        member _.Json() =
            let ts = DateTime.UtcNow.ToString "yyyy-MM-ddTHH:mm:ssZ"

            let route =
                JsonSerializer.Serialize
                    {| id = 7001L
                       body = currentRouteComment ()
                       user = {| login = "EHotwagner" |}
                       created_at = ts
                       updated_at = ts |}

            let claims =
                comments
                |> Seq.sortBy (fun kv -> kv.Key)
                |> Seq.map (fun kv ->
                    $"""{{"id":%d{kv.Key},"body":"%s{kv.Value}","user":{{"login":"EHotwagner"}},"created_at":"%s{ts}","updated_at":"%s{ts}"}}""")
                |> String.concat ","

            "[" + route + (if String.IsNullOrEmpty claims then "" else "," + claims) + "]"

        member _.Add(body: string) =
            nextId <- nextId + 1L
            comments.[nextId] <- body.Replace("\n", "\\n").Replace("\"", "\\\"")
            posted.Add body
            nextId

        member _.Remove(id: int64) = comments.Remove id |> ignore

        /// .github#2300 repair 2: the SAME thread `.Json()` serves the REST `/comments` endpoint from —
        /// the route marker first, then any live claim marker — as bare bodies, oldest first, for the
        /// bounded GraphQL `comments(last: N)` read to truncate. `\\n`/`\"` are un-escaped back out of a
        /// claim body's stored form (`.Add` escapes them for the REST JSON string); the route marker was
        /// never escaped in the first place, so it passes through unchanged.
        member _.Bodies =
            let claims =
                comments
                |> Seq.sortBy (fun kv -> kv.Key)
                |> Seq.map (fun kv -> kv.Value.Replace("\\n", "\n").Replace("\\\"", "\""))
                |> List.ofSeq

            currentRouteComment () :: claims

    let private world (thread: Thread) =
        Fake.Recorder(fun (req: Request) ->
            let path = req.Path.Trim '/'

            match req.Method, path with
            // .github#2300 repair 2: `requireCurrentDeliveryRoute`'s bounded marker search — served from
            // the SAME `thread` the REST `/comments` arm below reads, so a steal/renewal that appends a
            // new claim marker mid-test is visible to both arms identically.
            | "POST", "graphql" when
                (match req.Body with
                 | Query(document, _) -> document.Contains "comments(last:"
                 | _ -> false)
                ->
                match req.Body with
                | Query(_, variables) ->
                    let lastVar =
                        variables
                        |> List.tryFind (fun (k, _) -> k = "last")
                        |> Option.bind (fun (_, v) -> match v with VNumber n -> Some(int n) | _ -> None)

                    match lastVar with
                    | Some last ->
                        let recent =
                            thread.Bodies
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
                    | None -> Error(Errors.NotFound "the recent-comments query is missing a `last` variable")
                | _ -> Error(Errors.NotFound "a graphql call with no document")
            | "POST", "graphql" ->
                match req.Body with
                | Query(document, _) ->
                    match graphqlAnswer document with
                    | Some answer -> ok answer
                    | None -> Error(Errors.NotFound "the fixture serves no board WRITE — the lock is what is under test")
                | _ -> Error(Errors.NotFound "a graphql call with no document")
            | "GET", "rate_limit" -> ok """{"resources":{"graphql":{"remaining":4980,"limit":5000}}}"""
            | "GET", "repos/FS-GG/FS.GG.SDD/issues" -> ok "[]"
            | "GET", "repos/FS-GG/FS.GG.SDD/issues/42/comments" -> ok (thread.Json())
            | "POST", "repos/FS-GG/FS.GG.SDD/issues/42/comments" ->
                let body =
                    match req.Body with
                    | Json payload ->
                        JsonDocument.Parse(payload).RootElement.GetProperty("body").GetString()
                    | _ -> ""

                ok (sprintf """{"id":%d}""" (thread.Add body))
            | "DELETE", _ when path.StartsWith "repos/FS-GG/FS.GG.SDD/issues/comments/" ->
                thread.Remove(Int64.Parse(path.Substring(path.LastIndexOf '/' + 1)))
                ok ""
            | "GET", "repos/FS-GG/FS.GG.SDD/issues/42" ->
                ok """{"number":42,"body":"Paths: src/Thing.fs"}"""
            | m, p -> Error(Errors.NotFound $"the fixture serves no %s{m} %s{p}"))

    let private context (transport: Fake.Recorder) : Client.Context =
        { Transport = transport
          Owner = "FS-GG"
          Title = "Coordination"
          DefaultRepo = Some "FS.GG.SDD"
          ChoreLocks = [] }

    /// Drive `Client.claim` against a throwaway cache, exactly as `ApplicationServiceTests.run` does and
    /// on the same licence: `AssemblyInfo.fs` disables cross-class parallelism, so the process-global
    /// `FSGG_COORD_CACHE` is safe to point somewhere private per call. A fresh directory also stops the
    /// board map leaking between the two legs of the pair, which would let the second leg pass on the
    /// first leg's reads.
    /// The identity ladder reads the HARNESS's session id from the environment, so a test that says
    /// nothing about it asserts something different on a developer's machine (inside Claude Code:
    /// `CLAUDE_CODE_SESSION_ID` is set, and our session is known) than in CI (nothing set, our session is
    /// `None`). That difference decides the TWIN leg exactly: `twinSession` concludes twin only when BOTH
    /// sessions are known, so an unpinned session would turn that refusal into a renewal and the test
    /// would pass locally and fail in CI — or, worse, the reverse. Pin the whole ladder, both rungs above
    /// the escape hatch included.
    ///
    /// **AND `$FSGG_WORKER` IS PART OF THAT LADDER, WHICH #1646 IS WHAT MADE VISIBLE.** Pinning the session
    /// alone left this fixture saying "our session is `ed60050b`" while argv said "we are `vole-418`" — and
    /// nothing joined the two, so who this PROCESS was remained whatever the runner's environment said. Once
    /// `claim` measures the named worker against the derived one (#1646), an unpinned `$FSGG_WORKER` makes
    /// every leg here an impersonation: `--worker vole-418` from a process that derives its id from
    /// `ed60050b` is a caller naming somebody it is not.
    ///
    /// So the fixture states BOTH halves of the identity it means — `vole-418`, in session `ed60050b` — and
    /// the legs below are unchanged by it: the twin leg still turns on the MARKER's session differing from
    /// ours, and the steal legs still turn on the marker's WORKER differing from ours. Neither ever depended
    /// on this process being anonymous.
    let private sessionVars =
        [ "CLAUDE_CODE_SESSION_ID"; "OPENCODE_SESSION_ID"; "FSGG_AGENT_SESSION_ID"; "FSGG_WORKER" ]

    let private runClaim (transport: Fake.Recorder) (args: string list) : int * string =
        let dir = Path.Combine(Path.GetTempPath(), "fsgg-1620-" + Guid.NewGuid().ToString "n")
        let previousCache = Environment.GetEnvironmentVariable "FSGG_COORD_CACHE"
        let previousKitRoot = Environment.GetEnvironmentVariable "FSGG_KIT_ROOT"
        let previousSessions = sessionVars |> List.map (fun v -> v, Environment.GetEnvironmentVariable v)
        let stdout = Console.Out
        use captured = new StringWriter()

        try
            Directory.CreateDirectory dir |> ignore
            Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", dir)
            Environment.SetEnvironmentVariable("FSGG_KIT_ROOT", dir)
            Environment.SetEnvironmentVariable("CLAUDE_CODE_SESSION_ID", null)
            Environment.SetEnvironmentVariable("OPENCODE_SESSION_ID", null)
            Environment.SetEnvironmentVariable("FSGG_AGENT_SESSION_ID", "ed60050b")
            // The other half of the identity (#1646) — and it MUST match `claimArgs`' `--worker`, or this
            // process is naming a worker it is not and `claim` refuses before it reads anything.
            Environment.SetEnvironmentVariable("FSGG_WORKER", "vole-418")
            Console.SetOut captured

            let opts =
                match Options.parse args with
                | Ok o -> o
                | Error e -> failwithf "the fixture's own argv did not parse: %s" e

            let code = Client.claim (context transport) opts
            Console.Out.Flush()
            code, captured.ToString()
        finally
            Console.SetOut stdout
            Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", previousCache)
            Environment.SetEnvironmentVariable("FSGG_KIT_ROOT", previousKitRoot)

            for name, value in previousSessions do
                Environment.SetEnvironmentVariable(name, value)

            try
                Directory.Delete(dir, true)
            with _ ->
                ()

    /// The argv under test, spelled as the tool's own messages spell it.
    let private claimArgs (extra: string list) =
        [ "claim"; "FS.GG.SDD#42"; "--worker"; "vole-418"; "--json" ] @ extra

    // ---- THE PAIR --------------------------------------------------------------------------------

    [<Fact>]
    let ``#1620 claim --force TAKES an item a live worker holds, and the receipt says it stole it`` () =
        let thread = Thread(Some "snipe-f893")
        let transport = world thread

        let code, out = runClaim transport (claimArgs [ "--force" ])

        Assert.Equal(0, code)

        // THE HOLDER'S MARKER IS GONE. This is the assertion that failed before #1620: the flag was
        // parsed, scoped to `claim`, and reached a pre-check that had nothing to do with the holder.
        Assert.False(List.contains 8042L thread.Ids)
        Assert.True(transport.Logged "comment-delete FS-GG/FS.GG.SDD 8042")

        // AND THE RECEIPT NAMES IT A THEFT. `kind` is what a scripted caller reads, and a steal that
        // reported `claimed` would be the silent transfer the decision explicitly rejected as "worse than
        // a refusal".
        let receipt = JsonDocument.Parse(out.Trim()).RootElement
        Assert.Equal("stolen", receipt.GetProperty("kind").GetString())
        Assert.Equal("vole-418", receipt.GetProperty("worker").GetString())

    [<Fact>]
    let ``#1620 the SAME board with no --force still refuses - the flag is the whole difference`` () =
        // The other half. Before the fix both legs produced this, which is precisely why the dead end was
        // invisible to every test in the suite: the tool behaved consistently, and consistently wrongly.
        let thread = Thread(Some "snipe-f893")
        let transport = world thread

        let code, _ = runClaim transport (claimArgs [])

        Assert.Equal(3, code)

        // Untouched: refused BEFORE anything is posted, and the holder keeps their lock.
        Assert.True(List.contains 8042L thread.Ids)
        Assert.Equal(0, transport.Count "comment-delete")
        Assert.Equal(0, transport.Count "comment-post")

    // ---- THE OBLIGATIONS THE DECISION ATTACHED TO THE STEAL ---------------------------------------

    [<Fact>]
    let ``#1620 a steal is RECORDED ON THE ITEM, naming the prior holder and the worker that took it`` () =
        // "A steal is recorded on the item, not just in a receipt. The prior holder, the stealing worker,
        // and the time must be visible to a reader of the issue afterwards. A silent transfer is worse
        // than a refusal." — the decision, verbatim.
        //
        // The evicted MARKER is deleted, so this comment is the only surviving trace of the claim that was
        // taken; the comment's own GitHub timestamp is the third fact. It is one comment doing two jobs:
        // a reader of the issue sees the theft, and the displaced worker — which is still RUNNING, unlike
        // the holder of a lapsed lease — finds it in `inbox`.
        let thread = Thread(Some "snipe-f893")
        let transport = world thread

        let code, _ = runClaim transport (claimArgs [ "--force" ])
        Assert.Equal(0, code)

        let notice =
            thread.Posted
            |> List.tryFind (fun b -> b.Contains "fsgg:msg" && b.Contains "snipe-f893")

        match notice with
        | None -> failwith $"a steal must post a notice naming the displaced worker — posted: %A{thread.Posted}"
        | Some body ->
            Assert.Contains("to=snipe-f893", body)
            Assert.Contains("vole-418", body)
            Assert.Contains("--force", body)
            // It must tell them to STOP. A notice that only records the fact leaves the displaced worker
            // building against a lock it no longer holds, which is the failure the notice exists to stop.
            Assert.Contains("STOP working", body)

    [<Fact>]
    let ``#1620 --force does NOT steal from a TWIN - the one refusal it may never override`` () =
        // #419/#1031 stay absolute, and reachably so: every subagent of one Claude Code session derives
        // the SAME worker id, so a twin is not an exotic state — it is the default failure of an
        // unminted identity. Forcing here deletes a lock a sibling is working behind, and the remedy for
        // a broken identity is a new identity. The CAS enforces it (`WriteTests`); this asserts the
        // refusal survives the trip through argv, where `--force` is turned on.
        // OUR id, ANOTHER session. `runClaim` pins ours to `ed60050b`, so the two are known and differ —
        // which is the only configuration in which the protocol is entitled to call this a twin.
        let thread = Thread(None)
        thread.Add "<!-- fsgg:claim worker=vole-418 lease=120 session=79b9e347 -->" |> ignore

        let transport = world thread
        let before = thread.Ids

        let code, _ = runClaim transport (claimArgs [ "--force" ])

        // ExitRed, and nothing deleted: a twin's marker is not a contested item.
        Assert.Equal(3, code)
        Assert.Equal<int64 list>(before, thread.Ids)
        Assert.Equal(0, transport.Count "comment-delete")

    [<Fact>]
    let ``#1620 --force on a FREE item claims it ordinarily and reports no theft`` () =
        // `--force` is commonly typed by a worker that does not KNOW whether the item is still held — that
        // is the whole recovery scenario. When there is nobody to displace, nothing may be announced: the
        // receipt must say `claimed`, and no theft notice may reach the item. The client keys its
        // displacement message on what was actually evicted rather than on the flag, and this is the leg
        // that would fail if it went back to keying on the flag.
        let thread = Thread(None)
        let transport = world thread

        let code, out = runClaim transport (claimArgs [ "--force" ])

        Assert.Equal(0, code)
        Assert.Equal("claimed", JsonDocument.Parse(out.Trim()).RootElement.GetProperty("kind").GetString())
        Assert.Equal(0, transport.Count "comment-delete")
        Assert.DoesNotContain(thread.Posted, fun (b: string) -> b.Contains "fsgg:msg")

    [<Fact>]
    let ``#1620 the #516 refusal that RECOMMENDS --force says what else the flag will do`` () =
        // The refusal for holding two items points a worker straight at `--force`. Since #1620 that flag
        // also DELETES a live holder's lock, so a line recommending it without saying so re-creates the
        // exact message-vs-behaviour disagreement this issue exists to close — in the one place the tool
        // actively recommends the flag. Asserted on `Options.usage`'s sibling: the text is in `Client.fs`,
        // so this pins the usage block's version, and the e2e leg exercises the behaviour.
        Assert.Contains("--force STEALS", Options.usage)
        Assert.Contains("one-item-per-worker", Options.usage)

    // ---- THE MESSAGE THAT SENT OPERATORS HERE -----------------------------------------------------

    [<Fact>]
    let ``#1620 the usage block advertises --force as a STEAL, not merely as a flag`` () =
        // Acceptance criterion 3: the usage line and the behaviour must agree. This is a weaker assertion
        // than the pair above and it is here for a specific reason — the ORIGINAL defect was found by an
        // operator reading this text and following it. Text that promises a power is a load-bearing part
        // of the tool, and the promise is now true; if somebody softens the behaviour, the sentence that
        // sent them here should have to be edited in the same diff.
        let usage = Options.usage

        Assert.Contains("--force STEALS", usage)
        // And it must keep naming the refusals it does NOT override, or the next operator reads a blanket
        // permission into it.
        Assert.Contains("#419", usage)
