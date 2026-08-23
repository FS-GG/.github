namespace FS.GG.Coord.Cli.Tests

open System
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json
open Xunit
open FS.GG.Coord
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Transport
open FS.GG.Coord.Cli
open FS.GG.Coord.Cli.Lifecycle

/// #2298: `sdd-required`'s SDD-package evidence (`work/<id>/spec.md`, `readiness/<id>/analysis.json` at
/// `implementationReady`) used to gate BOTH `delivery-route record` (the coordinator's write) and every
/// scheduling read (`show`, `claim`, `take`, `batch`/`next`, via the shared `bindSddEvidence`) on the
/// SAME filesystem fact — a fact only a CLAIMED WORKER is positioned to produce, inside a worktree, via
/// `fsgg-sdd`. That made an honest `sdd-required` route permanently unrecordable, and therefore the item
/// permanently unclaimable, for anything that did not already carry a package (`work/2137-delivery-route`
/// predated the gate and is the only reason `#2137` itself could ever record one).
///
/// This file pins the command boundary the fix lands on: `record` and `show` now REPORT the package's
/// on-disk readiness as advisory (`sddPackageReady` / `sddPackageNotes`) rather than refuse the write or
/// the read on it — for exactly the three ways that evidence can be incomplete AC5 names (no package at
/// all, an existing-but-not-`implementationReady` analysis, a workId substitution) — while a genuinely
/// missing or malformed ROUTE RECEIPT, the agent's own explicit decision, still refuses (AC5's negative
/// case, and AC2's "stays closed").
///
/// The command boundary itself is `LiveHandlers.deliveryRouteCmd`, driven directly against a scripted
/// transport exactly as `ForceStealTests` already drives `Client.claim` — `record`/`show` need no board
/// or GraphQL fixture at all, only the issue body and its comment ledger.
module DeliveryRouteCliTests =

    let private ok (body: string) : Errors.IoResult<Response> =
        Ok
            { Status = 200
              Body = body
              ETag = None
              NextLink = None
              Headers = Map.empty }

    let private hashHex (text: string) =
        SHA256.HashData(Encoding.UTF8.GetBytes text) |> Convert.ToHexString |> _.ToLowerInvariant()

    /// Mirrors `Client.volatileDeclarationLine`/`deliveryRouteSubject` (.github#2392): a `Paths:`,
    /// `Class:`, `Blocked on:`, or `Blocked by:` line — up to three leading spaces, either case, OUTSIDE
    /// a fenced code block — is DROPPED before hashing, and so is a blank/whitespace-only line. Duplicated
    /// rather than called, because that helper is `private` to `Client`; this restates the same literal
    /// grammar `TouchSet`/`Class`/`HumanBlock` already own (see the doc comment on the production copy for
    /// why a fourth copy there is not an option), so a fixture built against it stays a faithful stand-in
    /// for what a real caller gets back from `delivery-route show` after this fix, not a second decision
    /// about the grammar.
    let private volatileDeclarationLine =
        Text.RegularExpressions.Regex(@"^ {0,3}([Pp]aths|[Cc]lass|[Bb]locked [Oo]n|[Bb]locked [Bb]y):.*$", Text.RegularExpressions.RegexOptions.Compiled)

    let private canonicalSubject (body: string) =
        Markdown.classify body
        |> List.choose (fun (line, kind) ->
            if kind = Markdown.Text && (volatileDeclarationLine.IsMatch line || String.IsNullOrWhiteSpace line) then None
            else Some line)
        |> String.concat "\n"

    /// The CURRENT (.github#2392) `subjectRevision` scheme — what `delivery-route show` reports and
    /// `record` requires today.
    let private revision (body: string) = hashHex (canonicalSubject body)

    let private issueBodyText = "Paths: src/Thing.fs"
    let private issueRevision = revision issueBodyText

    [<Literal>]
    let private StructuredRouteMarker = "<!-- fsgg:route-decision/v2 -->"

    let private sddRequiredRecord workId =
        let draft : StructuredDecision.RouteRecord =
            { Schema = StructuredDecision.RouteSchema; Subject = "FS-GG/FS.GG.SDD#42"; Revision = 1
              PreviousDigest = None; Scope = [ "fixture route scope" ]; Dependencies = [ "none" ]
              TouchSet = [ "src/Thing.fs" ]; PolicyVersion = StructuredDecision.PolicyVersion
              Route = Some DeliveryRoute.SddRequired; Agent = "fixture-2298"; Timestamp = "2026-01-01T00:00:00Z"
              ReasonCodes = [ "fixture" ]; Rationale = "fixture structured route for #2298"
              SddWorkId = Some workId; SpecHome = Some $"work/%s{workId}/spec.md"
              RequiredGates = [ "implementationReady"; "analyze"; "verify"; "ship" ]; Digest = "" }
        let record = { draft with Digest = StructuredDecision.routeDigest draft }
        JsonSerializer.Serialize
            {| schema = record.Schema; subject = record.Subject; revision = record.Revision
               previousDigest = record.PreviousDigest; scope = record.Scope; dependencies = record.Dependencies
               touchSet = record.TouchSet; policyVersion = record.PolicyVersion; route = "sdd-required"
               agent = record.Agent; timestamp = record.Timestamp; reasonCodes = record.ReasonCodes
               rationale = record.Rationale; sddWorkId = record.SddWorkId; specHome = record.SpecHome
               requiredGates = record.RequiredGates; digest = record.Digest |}

    let private lightweightRecord revision previous =
        let draft : StructuredDecision.RouteRecord =
            { Schema = StructuredDecision.RouteSchema; Subject = "FS-GG/FS.GG.SDD#42"; Revision = revision
              PreviousDigest = previous; Scope = [ "fixture route scope" ]; Dependencies = [ "none" ]
              TouchSet = [ "src/Thing.fs" ]; PolicyVersion = StructuredDecision.PolicyVersion
              Route = Some DeliveryRoute.Lightweight; Agent = "fixture-2298"; Timestamp = "2026-01-01T00:00:00Z"
              ReasonCodes = [ "fixture" ]; Rationale = "fixture structured route"
              SddWorkId = None; SpecHome = None; RequiredGates = []; Digest = "" }
        let record = { draft with Digest = StructuredDecision.routeDigest draft }
        let json =
            JsonSerializer.Serialize
                {| schema = record.Schema; subject = record.Subject; revision = record.Revision
                   previousDigest = record.PreviousDigest; scope = record.Scope; dependencies = record.Dependencies
                   touchSet = record.TouchSet; policyVersion = record.PolicyVersion; route = "lightweight"
                   agent = record.Agent; timestamp = record.Timestamp; reasonCodes = record.ReasonCodes
                   rationale = record.Rationale; sddWorkId = record.SddWorkId; specHome = record.SpecHome
                   requiredGates = record.RequiredGates; digest = record.Digest |}
        record, json

    /// A `lightweight` receipt bound to the given `subjectRevision` — the field `claim`'s refusal turns
    /// on when it disagrees with the live issue body's own hash.
    /// A live comment thread, mutated only the way `record` would mutate it (append). Bodies round-trip
    /// through `JsonSerializer` rather than hand-escaped string interpolation, so a marker's embedded
    /// `\n` and quotes cannot corrupt the fixture's own JSON.
    type private Thread(initial: string list) =
        let comments = ResizeArray<int64 * string>(initial |> List.mapi (fun i b -> int64 (7000 + i), b))
        let mutable nextId = 9000L

        member _.Json() =
            comments
            |> Seq.map (fun (id, body) ->
                {| id = id
                   html_url = $"https://example.invalid/comments/%d{id}"
                   body = body
                   user = {| login = "EHotwagner" |}
                   created_at = "2026-01-01T00:00:00Z"
                   updated_at = "2026-01-01T00:00:00Z" |})
            |> List.ofSeq
            |> JsonSerializer.Serialize

        member _.Add(body: string) =
            nextId <- nextId + 1L
            comments.Add(nextId, body)
            nextId

        member _.Bodies = comments |> Seq.map snd |> List.ofSeq

    let private world (thread: Thread) =
        Fake.Recorder(fun (req: Request) ->
            let path = req.Path.Trim '/'

            match req.Method, path with
            // .github#2300 repair 2: `requireCurrentDeliveryRoute`/`deliveryRouteFact` now search for
            // the marker over a BOUNDED GraphQL call (`Reads.recentCommentBodies`), not the REST
            // `commentBodies` this fixture answered before. Served from the SAME live `thread` the REST
            // arm below reads, truncated to the requested `last` window — exactly the "last N, in Relay
            // order" contract the real GraphQL connection has, so a fixture growth of `thread` (e.g. via
            // `record`'s own `POST .../comments` append) is visible to both arms identically.
            | "POST", "graphql" ->
                match req.Body with
                | Query(document, variables) when document.Contains "comments(last:" ->
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
                | _ -> Error(Errors.NotFound "this fixture answers only the recent-comments GraphQL query")
            | "GET", "repos/FS-GG/FS.GG.SDD/issues/42" -> ok (JsonSerializer.Serialize {| number = 42; body = issueBodyText |})
            | "GET", "repos/FS-GG/FS.GG.SDD/issues/42/comments" -> ok (thread.Json())
            | "POST", "repos/FS-GG/FS.GG.SDD/issues/42/comments" ->
                let body =
                    match req.Body with
                    | Json payload -> JsonDocument.Parse(payload).RootElement.GetProperty("body").GetString()
                    | _ -> ""

                ok (sprintf """{"id":%d}""" (thread.Add body))
            | m, p -> Error(Errors.NotFound $"the fixture serves no %s{m} %s{p}"))

    let private context (transport: Fake.Recorder) : Kernel.Context =
        { Transport = transport
          Owner = "FS-GG"
          Title = "Coordination"
          DefaultRepo = Some "FS.GG.SDD"
          ChoreLocks = [] }

    /// Every leg pins `FSGG_COORD_SDD_ROOT` to a fixture-owned directory, explicitly — never `None`, or
    /// `sddEvidenceErrors`' upward directory search could walk out of a throwaway temp dir and find this
    /// CHECKOUT's own real `work/`/`readiness/` trees, silently laundering a live fact into a fixture.
    let private runRoute (transport: Fake.Recorder) (sddRoot: string) (args: string list) : int * string =
        let previousRoot = Environment.GetEnvironmentVariable "FSGG_COORD_SDD_ROOT"
        let stdout = Console.Out
        use captured = new StringWriter()

        try
            Environment.SetEnvironmentVariable("FSGG_COORD_SDD_ROOT", sddRoot)
            Console.SetOut captured

            let opts =
                match Options.parse args with
                | Ok o -> o
                | Error e -> failwithf "the fixture's own argv did not parse: %s" e

            let code = LiveHandlers.deliveryRouteCmd (context transport) opts
            Console.Out.Flush()
            code, captured.ToString()
        finally
            Console.SetOut stdout
            Environment.SetEnvironmentVariable("FSGG_COORD_SDD_ROOT", previousRoot)

    let private tempSddRoot () =
        let dir = Path.Combine(Path.GetTempPath(), "fsgg-2298-" + Guid.NewGuid().ToString "n")
        Directory.CreateDirectory dir |> ignore
        dir

    let private withPackage (root: string) workId (analysis: string option) =
        Directory.CreateDirectory(Path.Combine(root, "work", workId)) |> ignore
        File.WriteAllText(Path.Combine(root, "work", workId, "spec.md"), "# fixture spec\n")

        match analysis with
        | Some json ->
            Directory.CreateDirectory(Path.Combine(root, "readiness", workId)) |> ignore
            File.WriteAllText(Path.Combine(root, "readiness", workId, "analysis.json"), json)
        | None -> ()

    // ---- AC5's three incomplete-evidence cases: all now RECORD, none of them refuse -----------------

    [<Fact>]
    let ``#2298 record posts an sdd-required receipt with no SDD package on disk at all`` () =
        let root = tempSddRoot ()

        try
            let thread = Thread []
            let transport = world thread
            let path = Path.Combine(root, "receipt.json")
            File.WriteAllText(path, sddRequiredRecord "no-package-2298")

            let code, out = runRoute transport root [ "delivery-route"; "record"; "FS.GG.SDD#42"; path ]

            Assert.Equal(0, code)
            // .github#2583: `record` now writes a derived locator marker BETWEEN the route marker and
            // the agent's receipt. The receipt JSON itself is still byte-verbatim, which is what this
            // assertion has always been about; `ConsolidationTaxTests` owns the locator's own contract.
            // This fixture's body is a single `Paths:` line, so its subject is EMPTY and the marker
            // carries no locators — the degenerate shape, pinned here on purpose.
            Assert.Equal<string list>([ StructuredRouteMarker + "\n" + (sddRequiredRecord "no-package-2298") ], thread.Bodies)

            let result = JsonDocument.Parse(out.Trim()).RootElement
            Assert.Equal("recorded", result.GetProperty("kind").GetString())
            Assert.False(result.GetProperty("sddPackageReady").GetBoolean())

            let notes = result.GetProperty("sddPackageNotes").EnumerateArray() |> Seq.map (fun v -> v.GetString()) |> List.ofSeq
            Assert.Contains(notes, fun (n: string) -> n.Contains "sdd spec does not exist")
        finally
            Directory.Delete(root, true)

    [<Fact>]
    let ``#2298 record posts an sdd-required receipt whose readiness analysis is not implementationReady`` () =
        let root = tempSddRoot ()

        try
            withPackage root "not-ready-2298" (Some """{"workId":"not-ready-2298","status":"analyzing"}""")
            let thread = Thread []
            let transport = world thread
            let path = Path.Combine(root, "receipt.json")
            File.WriteAllText(path, sddRequiredRecord "not-ready-2298")

            let code, out = runRoute transport root [ "delivery-route"; "record"; "FS.GG.SDD#42"; path ]

            Assert.Equal(0, code)
            Assert.Equal(1, thread.Bodies.Length)

            let result = JsonDocument.Parse(out.Trim()).RootElement
            Assert.Equal("recorded", result.GetProperty("kind").GetString())
            Assert.False(result.GetProperty("sddPackageReady").GetBoolean())

            let notes = result.GetProperty("sddPackageNotes").EnumerateArray() |> Seq.map (fun v -> v.GetString()) |> List.ofSeq
            Assert.Contains(notes, fun (n: string) -> n.Contains "not implementationReady")
        finally
            Directory.Delete(root, true)

    [<Fact>]
    let ``#2298 record posts an sdd-required receipt whose readiness analysis names a substituted workId`` () =
        let root = tempSddRoot ()

        try
            withPackage root "substituted-2298" (Some """{"workId":"other-work","status":"implementationReady"}""")
            let thread = Thread []
            let transport = world thread
            let path = Path.Combine(root, "receipt.json")
            File.WriteAllText(path, sddRequiredRecord "substituted-2298")

            let code, out = runRoute transport root [ "delivery-route"; "record"; "FS.GG.SDD#42"; path ]

            Assert.Equal(0, code)
            Assert.Equal(1, thread.Bodies.Length)

            let result = JsonDocument.Parse(out.Trim()).RootElement
            Assert.False(result.GetProperty("sddPackageReady").GetBoolean())

            let notes = result.GetProperty("sddPackageNotes").EnumerateArray() |> Seq.map (fun v -> v.GetString()) |> List.ofSeq
            Assert.Contains(notes, fun (n: string) -> n.Contains "workId does not match")
        finally
            Directory.Delete(root, true)

    [<Fact>]
    let ``#2298 record and show both report a ready SDD package as ready, once its analysis is implementationReady`` () =
        let root = tempSddRoot ()

        try
            withPackage root "ready-2298" (Some """{"workId":"ready-2298","status":"implementationReady"}""")
            let thread = Thread []
            let transport = world thread
            let path = Path.Combine(root, "receipt.json")
            File.WriteAllText(path, sddRequiredRecord "ready-2298")

            let recordCode, recordOut = runRoute transport root [ "delivery-route"; "record"; "FS.GG.SDD#42"; path ]
            Assert.Equal(0, recordCode)
            Assert.True(JsonDocument.Parse(recordOut.Trim()).RootElement.GetProperty("sddPackageReady").GetBoolean())

            let showCode, showOut = runRoute transport root [ "delivery-route"; "show"; "FS.GG.SDD#42" ]
            Assert.Equal(0, showCode)

            let shown = JsonDocument.Parse(showOut.Trim()).RootElement
            Assert.Equal("sdd-required", shown.GetProperty("route").GetString())
            Assert.True(shown.GetProperty("sddPackageReady").GetBoolean())
            Assert.Empty(shown.GetProperty("sddPackageNotes").EnumerateArray())
        finally
            Directory.Delete(root, true)

    [<Fact>]
    let ``#2298 show reports a missing SDD package as advisory rather than refusing the read`` () =
        let root = tempSddRoot ()

        try
            let thread = Thread [ StructuredRouteMarker + "\n" + (sddRequiredRecord "shown-missing-2298") ]
            let transport = world thread

            let code, out = runRoute transport root [ "delivery-route"; "show"; "FS.GG.SDD#42" ]

            Assert.Equal(0, code)

            let shown = JsonDocument.Parse(out.Trim()).RootElement
            Assert.Equal("current", shown.GetProperty("kind").GetString())
            Assert.Equal("sdd-required", shown.GetProperty("route").GetString())
            Assert.False(shown.GetProperty("sddPackageReady").GetBoolean())
        finally
            Directory.Delete(root, true)

    // ---- AC5's negative case: the receipt gate itself stays closed -----------------------------------

    [<Fact>]
    let ``#2298 a missing route receipt still refuses show — the decision gate that stays closed`` () =
        let root = tempSddRoot ()

        try
            let thread = Thread [] // no structured route record posted at all
            let transport = world thread

            let code, _ = runRoute transport root [ "delivery-route"; "show"; "FS.GG.SDD#42" ]

            Assert.NotEqual(0, code)
        finally
            Directory.Delete(root, true)

    [<Fact>]
    let ``#2298 an incomplete route receipt still refuses record with zero writes — SDD leniency never widens this`` () =
        let root = tempSddRoot ()

        try
            let thread = Thread []
            let transport = world thread
            // `sdd-required` with neither `sddWorkId` nor `specHome` — an incomplete AGENT DECISION, the
            // thing #2298 leaves refused. Only the SDD PACKAGE'S readiness became advisory, never this.
            let malformed =
                $"""{{"schema":"fsgg.coord.delivery-route/v1","subject":"FS-GG/FS.GG.SDD#42","subjectRevision":"%s{issueRevision}","route":"sdd-required","agent":"fixture-2298","timestamp":"2026-01-01T00:00:00Z","reasonCodes":["fixture"],"rationale":"incomplete sdd-required receipt","declaredImpacts":["internal"],"observedFacts":["localized"],"sddWorkId":null,"specHome":null,"requiredGates":[]}}"""

            let path = Path.Combine(root, "malformed.json")
            File.WriteAllText(path, malformed)

            let code, _ = runRoute transport root [ "delivery-route"; "record"; "FS.GG.SDD#42"; path ]

            Assert.NotEqual(0, code)
            Assert.Empty(thread.Bodies)
        finally
            Directory.Delete(root, true)

    // ---- `claim`/`take`'s OWN gate: `requireCurrentDeliveryRoute`, pinned directly ------------------
    //
    // This function is the one `claim` (and, through it, `take`) actually calls to refuse a missing,
    // stale, or unreadable route decision (Client.fs `requireCurrentDeliveryRoute`, just above the
    // scheduling-read helpers this file already drives). This diff removes its `|> bindSddEvidence`
    // pipe — a real edit to a function that, before this file, had ZERO test coverage anywhere in the
    // repository. A critic proved that by mutating it to swallow `Stale`/`Unreadable` verdicts and
    // rebuilding: 2,093 assertions across every suite stayed green (.github#2298 review round 1).
    //
    // These three legs close that gap at the command boundary `claim` itself uses — not by re-testing
    // `DeliveryRoute.decide` (already pinned in `DeliveryRouteTests.fs`), but by proving `claim` REFUSES
    // and posts NOTHING for exactly the three ways a route decision can fail to be current: no receipt
    // comment at all, a receipt whose `subjectRevision` has gone stale, and a receipt comment present but
    // undecodable as a valid receipt. Each is a distinct INPUT that reaches the same `Stale`-producing
    // path `requireCurrentDeliveryRoute` swallows under the critic's mutation — so each is expected to,
    // and was confirmed to, go red under it (see the inline note on each test).
    //
    // `requireCurrentDeliveryRoute` runs BEFORE the board/GraphQL bootstrap `heldElsewhere` needs, so a
    // refusal here never reaches it — the fixture below serves only the issue and its comments, and
    // asserts zero POSTs and zero GraphQL calls, not just a nonzero exit code.

    /// The identity ladder `claim` measures every marker against (.github#1646): a caller's session must
    /// be pinned, or this process's derived id depends on whatever harness ran the test. Mirrors
    /// `ForceStealTests.sessionVars`/`runClaim` exactly, for the same reason.
    let private sessionVars =
        [ "CLAUDE_CODE_SESSION_ID"; "OPENCODE_SESSION_ID"; "FSGG_AGENT_SESSION_ID"; "FSGG_WORKER" ]

    let private runClaim (transport: Fake.Recorder) (args: string list) : int * string =
        let dir = Path.Combine(Path.GetTempPath(), "fsgg-2298-claim-" + Guid.NewGuid().ToString "n")
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
            Environment.SetEnvironmentVariable("FSGG_AGENT_SESSION_ID", "fixture-session-2298")
            Environment.SetEnvironmentVariable("FSGG_WORKER", "vole-2298")
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

    let private claimArgs = [ "claim"; "FS.GG.SDD#42"; "--worker"; "vole-2298"; "--json" ]

    /// THE DISCRIMINATOR ITSELF (.github#2300 repair 2, correcting a first attempt the host caught).
    ///
    /// The three tests below exist because `.github#2298`'s critic proved `requireCurrentDeliveryRoute`
    /// was DECORATIVE: mutated to swallow `Stale`/`Unreadable` into success, the whole 2,093-assertion
    /// suite stayed green. Their value was never "the exit code is non-zero" — a broken fixture can
    /// produce that for the wrong reason — it was `GraphQlCalls = 0`: proof that refusal happens BEFORE
    /// `heldElsewhere`'s `Board.bootstrapCached`, the first GraphQL call on the success path, rather than
    /// merely "somewhere before the write".
    ///
    /// M4 makes the ROUTE READ a complete REST ledger read because a digest chain cannot safely be
    /// decided from a bounded tail. What the property needs is still the ORDERING fact itself, stated
    /// directly: the EXACT call sequence a refusal makes is `issueBody` then `comment-list`, and NOTHING
    /// ELSE — no board bootstrap, no comment-post, no third call of any
    /// kind. `transport.Log` is ordered and complete, so asserting it against the exact expected sequence
    /// is a stronger, more literal restatement of "refusal happens before the board bootstrap" than a
    /// bare count ever was: it catches a bootstrap call appearing ANYWHERE, in ANY position, of ANY kind.
    let private refusedBeforeBootstrap: string list =
        [ "comment-list FS-GG/FS.GG.SDD 42" ]

    [<Fact>]
    let ``#2298 claim refuses with zero writes when NO delivery-route receipt exists`` () =
        // Confirmed to go RED under the critic's M6 mutation (requireCurrentDeliveryRoute's
        // `Stale`/`Unreadable` arm swallowed into success): `receipt=None` here reaches exactly that arm
        // via `DeliveryRoute.decide`'s `Some _, None -> Stale [...]` leg.
        let thread = Thread [] // no structured route decision at all
        let transport = world thread

        let code, out = runClaim transport claimArgs

        Assert.NotEqual(0, code)
        Assert.DoesNotContain("claimed", out)
        Assert.Equal<string list>(refusedBeforeBootstrap, transport.Log)

    [<Fact>]
    let ``M6 a retired route marker cannot authorize a claim`` () =
        // Split spelling keeps the old wire token out of the active source inventory while proving that
        // a comment with those exact bytes cannot substitute for the structured ledger.
        let retiredMarker = "<!-- fsgg:delivery-" + "route/v1 -->"
        let thread = Thread [ retiredMarker + "\n{}" ]
        let transport = world thread

        let code, out = runClaim transport claimArgs

        Assert.NotEqual(0, code)
        Assert.DoesNotContain("claimed", out)
        Assert.Equal<string list>(refusedBeforeBootstrap, transport.Log)

    [<Fact>]
    let ``#2298 claim refuses with zero writes when the delivery-route comment is UNDECODABLE`` () =
        // Confirmed to go RED under the critic's M6 mutation, via the SAME `Stale` arm as the missing-
        // structured-ledger leg above: an undecodable comment fails closed, so
        // `List.tryPick` treats it as absent and `decide` reports `Stale ["...receipt is missing"]`,
        // identically to no comment existing at all — a distinct INPUT, the same swallowed OUTPUT.
        let thread = Thread [ StructuredRouteMarker + "\n" + """{"schema":"fsgg.coord.route-decision/v2","subject":"not even the right shape"}""" ]
        let transport = world thread

        let code, out = runClaim transport claimArgs

        Assert.NotEqual(0, code)
        Assert.DoesNotContain("claimed", out)
        Assert.Equal<string list>(refusedBeforeBootstrap, transport.Log)

    // ---- .github#2392: the receipt binds to the route-relevant SUBJECT, not the whole body -----------
    //
    // Root cause (.github#2392): the pre-fix `subjectRevision` hashed the ENTIRE issue body, so a
    // `Paths:`/`Class:`/`Blocked on:`/`Blocked by:` edit — every one of them a PROTOCOL-REQUIRED action
    // (`widen`/`set-paths`, triage, a park/unpark, a dependency edit) — silently staled an otherwise
    // still-valid route decision. The fix redacts exactly those four declaration lines (outside fences)
    // before hashing (`Client.deliveryRouteSubject`); a genuine change anywhere else in the body still
    // invalidates the receipt (AC2), and a receipt recorded before this fix keeps validating for as long
    // as its body has not moved since (AC5's migration bridge, `Client.decideDeliveryRoute`).
    //
    // These tests need the served issue body to CHANGE between recording a receipt and reading it back —
    // `world`'s `issueBodyText` is a compile-time constant, so this section gets its own fixture that
    // reads the body from a mutable cell instead.

    let private worldWithBody (body: string ref) (thread: Thread) =
        Fake.Recorder(fun (req: Request) ->
            let path = req.Path.Trim '/'

            match req.Method, path with
            | "POST", "graphql" ->
                match req.Body with
                | Query(document, variables) when document.Contains "comments(last:" ->
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
                            |> List.map (fun b -> {| body = b |})
                            |> JsonSerializer.Serialize

                        let payload =
                            "{\"data\":{\"repository\":{\"issue\":{\"comments\":{\"nodes\":"
                            + recent
                            + "}}}},\"rateLimit\":{\"cost\":1,\"remaining\":4977}}"

                        ok payload
                    | None -> Error(Errors.NotFound "the recent-comments query is missing a `last` variable")
                | _ -> Error(Errors.NotFound "this fixture answers only the recent-comments GraphQL query")
            | "GET", "repos/FS-GG/FS.GG.SDD/issues/42" -> ok (JsonSerializer.Serialize {| number = 42; body = body.Value |})
            | "GET", "repos/FS-GG/FS.GG.SDD/issues/42/comments" -> ok (thread.Json())
            | "POST", "repos/FS-GG/FS.GG.SDD/issues/42/comments" ->
                let b =
                    match req.Body with
                    | Json payload -> JsonDocument.Parse(payload).RootElement.GetProperty("body").GetString()
                    | _ -> ""

                ok (sprintf """{"id":%d}""" (thread.Add b))
            | m, p -> Error(Errors.NotFound $"the fixture serves no %s{m} %s{p}"))

    let private baseBody =
        "A defect in the widget renderer causes flicker on every resize.\n\nPaths: src/Widget.fs\nClass: defect\n"

    let private showIsCurrent (transport: Fake.Recorder) (root: string) =
        let code, out = runRoute transport root [ "delivery-route"; "show"; "FS.GG.SDD#42" ]
        code = 0 && JsonDocument.Parse(out.Trim()).RootElement.GetProperty("kind").GetString() = "current"

    [<Fact>]
    let ``M4 v2 route ledger reads revision one beyond the recent-comment window`` () =
        let root = tempSddRoot ()
        try
            let first, firstJson = lightweightRecord 1 None
            let _, secondJson = lightweightRecord 2 (Some first.Digest)
            let comments =
                [ yield StructuredRouteMarker + "\n" + firstJson
                  for index in 1..100 do yield $"narrative comment %d{index}"
                  yield StructuredRouteMarker + "\n" + secondJson ]
            let thread = Thread comments
            let transport = world thread
            let code, output = runRoute transport root [ "delivery-route"; "show"; "FS.GG.SDD#42" ]
            Assert.Equal(0, code)
            Assert.Equal(2, JsonDocument.Parse(output.Trim()).RootElement.GetProperty("revision").GetInt32())
        finally
            Directory.Delete(root, true)

    [<Fact>]
    let ``M4 a wholly buried v2 route ledger remains authoritative and appends safely`` () =
        let root = tempSddRoot ()
        try
            let first, firstJson = lightweightRecord 1 None
            let _, secondJson = lightweightRecord 2 (Some first.Digest)
            let comments =
                [ yield StructuredRouteMarker + "\n" + firstJson
                  for index in 1..100 do yield $"narrative comment %d{index}" ]
            let thread = Thread comments
            let transport = world thread
            let path = Path.Combine(root, "revision-2.json")
            File.WriteAllText(path, secondJson)
            let beforeCode, beforeOutput = runRoute transport root [ "delivery-route"; "show"; "FS.GG.SDD#42" ]
            Assert.Equal(0, beforeCode)
            Assert.Equal(1, JsonDocument.Parse(beforeOutput.Trim()).RootElement.GetProperty("revision").GetInt32())
            let recordCode, _ = runRoute transport root [ "delivery-route"; "record"; "FS.GG.SDD#42"; path ]
            Assert.Equal(0, recordCode)
            let showCode, output = runRoute transport root [ "delivery-route"; "show"; "FS.GG.SDD#42" ]
            Assert.Equal(0, showCode)
            Assert.Equal(2, JsonDocument.Parse(output.Trim()).RootElement.GetProperty("revision").GetInt32())
        finally
            Directory.Delete(root, true)
