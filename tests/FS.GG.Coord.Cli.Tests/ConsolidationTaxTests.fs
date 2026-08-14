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

/// .github#2583 — THE CONSOLIDATION TAX.
///
/// A delivery-route receipt binds to a hash of the issue BODY. So folding another row's cause into an
/// existing row's body staled that row's receipt, and `Schedulability.fs:146-148` maps `DeliveryRoute.
/// Stale` to `AwaitingDeliveryRouteDecision`, which removes the row from `batch`. Minting a fresh row
/// cost nothing. The board therefore priced consolidation ABOVE filing, in the direction that grows it.
///
/// MEASURED BEFORE THE FIX, on `.github#2583`'s own live body at `cb33188c` (recorded revision
/// `7a1157a6…3e`, `delivery-route show` returning `kind: current`), by replaying `Client.fs`'s own
/// `deliveryRouteSubject`/`hashHex` computation over four candidate edits:
///
///   * append a `## Folded from …` section — adds only            → `591081ac…9e`  STALE
///   * insert `## Folded from …` before `## Dedupe` — adds only    → `e663c869…4e`  STALE
///   * widen the `Paths:` line (.github#2392's own fix)            → `7a1157a6…3e`  current
///   * change `Severity: High` to `Severity: Low`                  → `b6a9e19e…d6`  STALE
///
/// The first two are what a consolidation actually looks like and the fourth is a genuine scope change;
/// before this fix all three were the same answer. The legs below drive exactly those shapes through the
/// `delivery-route` command boundary, and the file's whole point is that the first two now separate from
/// the fourth while the third keeps working.
///
/// THE RULE, and it is a property of the edit rather than a shape of the diff: an edit is ROUTE-NEUTRAL
/// when every subject line the receipt judged is still present, in the same relative order, byte-
/// identical — the judged subject survives as an ordered SUBSEQUENCE of the current subject. Insertion
/// at ANY position qualifies; append is not privileged.
///
/// WHY THE CORPUS IS REAL BODIES, AND WHY IT LIVES HERE. `.github#2551` requires a gate to be shown red
/// under inversion on a NON-EMPTY corpus, and `.github#2534`'s vacuous green is what an empty one buys.
/// Hand-written two-line fixtures would also miss the fenced-code and volatile-declaration interactions
/// these bodies carry for free. The corpus sits under `tests/FS.GG.Coord.Cli.Tests/` deliberately: that
/// prefix is ALREADY in `.github/workflows/coord-engine.yml`'s `pull_request` and `push` `paths:`
/// filters, so an edit to the corpus starts the workflow that grades it. A new top-level `tests/<name>/`
/// directory would have needed its own `paths:` entry and would have been selectively silent without one
/// — the trap `.github#2563` records one directory over.
module ConsolidationTaxTests =

    let private ok (body: string) : Errors.IoResult<Response> =
        Ok
            { Status = 200
              Body = body
              ETag = None
              NextLink = None
              Headers = Map.empty }

    let private hashHex (text: string) =
        SHA256.HashData(Encoding.UTF8.GetBytes text) |> Convert.ToHexString |> _.ToLowerInvariant()

    /// Mirrors `Client.volatileDeclarationLine`/`deliveryRouteSubjectLines` (.github#2392). Duplicated
    /// rather than called for the same reason `DeliveryRouteCliTests` duplicates it — those helpers are
    /// `private` to `Client` — and the duplication is deliberately kept off the critical path below: the
    /// main legs make PRODUCTION write the locator marker (via `record`) and only then read it back, so a
    /// drifted mirror cannot manufacture a pass. The mirror is used to build receipts and to author the
    /// two deliberately-malformed markers, where being an independent statement is the point.
    let private volatileDeclarationLine =
        Text.RegularExpressions.Regex(@"^ {0,3}([Pp]aths|[Cc]lass|[Bb]locked [Oo]n|[Bb]locked [Bb]y):.*$", Text.RegularExpressions.RegexOptions.Compiled)

    let private subjectLines (body: string) =
        Markdown.classify body
        |> List.choose (fun (line, kind) ->
            if kind = Markdown.Text && (volatileDeclarationLine.IsMatch line || String.IsNullOrWhiteSpace line) then None
            else Some line)

    let private revision (body: string) = hashHex (subjectLines body |> String.concat "\n")

    [<Literal>]
    let private DeliveryRouteMarker = "<!-- fsgg:delivery-route/v1 -->"

    [<Literal>]
    let private SubjectLinesMarkerPrefix = "<!-- fsgg:delivery-route-subject-lines/v1 "

    let private locatorLine (body: string) =
        SubjectLinesMarkerPrefix
        + (subjectLines body |> List.map (fun line -> (hashHex line).Substring(0, 16)) |> String.concat " ")
        + " -->"

    // ---- the corpus ------------------------------------------------------------------------------------

    /// Repository DATA, not a build output — no `.fsproj` here copies it, so it is found by walking up
    /// from the test assembly exactly as `DeliveryApplicationTests` locates the shared cross-language
    /// corpus. A tree that does not carry it fails naming the path it looked for, rather than passing.
    let private corpusDir =
        let relative = Path.Combine("tests", "FS.GG.Coord.Cli.Tests", "consolidation-corpus")

        let rec up (dir: DirectoryInfo) =
            match dir with
            | null ->
                failwith
                    $"ConsolidationTaxTests: walked past the filesystem root without finding %s{relative}. The real-issue-body corpus is what keeps .github#2583's gate-inversion evidence non-vacuous, and this suite refuses to pass without reading it."
            | _ ->
                let candidate = Path.Combine(dir.FullName, relative)
                if Directory.Exists candidate then candidate else up dir.Parent

        up (DirectoryInfo AppContext.BaseDirectory)

    /// STATED, not counted out of the directory (`DeliveryApplicationTests`' rule, and `.github#2534`'s
    /// vacuous green is why). A floor read from the data it guards can be edited in the same breath as
    /// the entry it counts; a stated one makes shrinking the corpus a deliberate two-file edit.
    [<Literal>]
    let private CorpusBodyCount = 4

    /// A second, independent non-vacuity floor. Four EMPTY files would satisfy the count above while
    /// proving nothing: a body with no subject lines makes every leg below degenerate, because the
    /// judged subsequence and the current subject are both empty and every verdict collapses to the
    /// canonical one.
    [<Literal>]
    let private MinimumSubjectLinesPerBody = 30

    let private corpus =
        Directory.GetFiles(corpusDir, "*.md")
        |> Array.sortBy Path.GetFileName
        |> Array.map (fun path -> Path.GetFileNameWithoutExtension path, (File.ReadAllText path).Replace("\r\n", "\n").TrimEnd '\n')
        |> List.ofArray

    /// Every corpus leg runs through here so the non-vacuity floors are asserted BEFORE any verdict is
    /// compared, in every leg, rather than once in a test that could be skipped.
    let private forEachBody (leg: string -> string -> unit) =
        // A FLOOR THAT CAN BE SET TO ZERO IS NOT A FLOOR — ported from
        // `scripts/check-gate-finding-history.py:951`, which refuses `--min-runs 0` outright with its
        // reason at the site: "a floor of 0 would make every never-red gate an EXERCISED-adjacent pass
        // over a sample of nothing." That gate has a runtime flag to refuse; these are compile-time
        // literals, so the faithful port is the REASONING rather than the mechanism — assert them here,
        // where they are consumed, so lowering either to zero fails loudly instead of quietly emptying
        // every leg below.
        Assert.True(CorpusBodyCount > 0, "CorpusBodyCount of 0 would make every corpus leg pass over no bodies at all")

        Assert.True(
            MinimumSubjectLinesPerBody > 0,
            "MinimumSubjectLinesPerBody of 0 would readmit the degenerate empty-subject shape into the corpus, where the vacuous-alignment false positive of review round 1 would once again be indistinguishable from a real match"
        )

        Assert.Equal(CorpusBodyCount, List.length corpus)

        for name, body in corpus do
            Assert.True(
                List.length (subjectLines body) >= MinimumSubjectLinesPerBody,
                $"corpus body %s{name} has %d{List.length (subjectLines body)} subject lines, below the stated floor of %d{MinimumSubjectLinesPerBody} — a corpus this thin cannot exercise insertion, modification, deletion and reordering as distinct edits"
            )

            leg name body

    // ---- the fixture -----------------------------------------------------------------------------------

    /// The live issue body is MUTABLE here, which is the whole point: `record` writes a receipt against
    /// one body and `show` then reads it back against an EDITED one, which is what a consolidating agent
    /// actually does to a row.
    type private World(initialBody: string) =
        let comments = ResizeArray<int64 * string>()
        let mutable nextId = 9000L
        let mutable body = initialBody

        member _.Body
            with get () = body
            and set value = body <- value

        member _.Bodies = comments |> Seq.map snd |> List.ofSeq

        member _.Add(text: string) =
            nextId <- nextId + 1L
            comments.Add(nextId, text)
            nextId

        member this.Seed(text: string) = this.Add text |> ignore

        member _.CommentsJson() =
            comments
            |> Seq.map (fun (id, text) ->
                {| id = id
                   html_url = $"https://example.invalid/comments/%d{id}"
                   body = text
                   user = {| login = "EHotwagner" |}
                   created_at = "2026-01-01T00:00:00Z"
                   updated_at = "2026-01-01T00:00:00Z" |})
            |> List.ofSeq
            |> JsonSerializer.Serialize

    let private transportFor (world: World) =
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
                            world.Bodies
                            |> List.rev
                            |> List.truncate last
                            |> List.rev
                            |> List.map (fun b -> {| body = b |})
                            |> JsonSerializer.Serialize

                        ok (
                            "{\"data\":{\"repository\":{\"issue\":{\"comments\":{\"nodes\":"
                            + recent
                            + "}}}},\"rateLimit\":{\"cost\":1,\"remaining\":4977}}"
                        )
                    | None -> Error(Errors.NotFound "the recent-comments query is missing a `last` variable")
                | _ -> Error(Errors.NotFound "this fixture answers only the recent-comments GraphQL query")
            | "GET", "repos/FS-GG/FS.GG.SDD/issues/42" -> ok (JsonSerializer.Serialize {| number = 42; body = world.Body |})
            | "GET", "repos/FS-GG/FS.GG.SDD/issues/42/comments" -> ok (world.CommentsJson())
            | "POST", "repos/FS-GG/FS.GG.SDD/issues/42/comments" ->
                let posted =
                    match req.Body with
                    | Json payload -> JsonDocument.Parse(payload).RootElement.GetProperty("body").GetString()
                    | _ -> ""

                ok (sprintf """{"id":%d}""" (world.Add posted))
            | m, p -> Error(Errors.NotFound $"the fixture serves no %s{m} %s{p}"))

    let private context (transport: Fake.Recorder) : Client.Context =
        { Transport = transport
          Owner = "FS-GG"
          Title = "Coordination"
          DefaultRepo = Some "FS.GG.SDD"
          ChoreLocks = [] }

    /// `FSGG_COORD_SDD_ROOT` is pinned explicitly, never left unset, or `sddEvidenceErrors`' upward
    /// search could walk out of the temp dir into THIS checkout's real `work/`/`readiness/` trees.
    let private runRoute (transport: Fake.Recorder) (args: string list) : int * string * string =
        let previousRoot = Environment.GetEnvironmentVariable "FSGG_COORD_SDD_ROOT"
        let stdout = Console.Out
        let stderr = Console.Error
        use capturedOut = new StringWriter()
        use capturedErr = new StringWriter()
        let sddRoot = Path.Combine(Path.GetTempPath(), "fsgg-2583-" + Guid.NewGuid().ToString "n")

        try
            Directory.CreateDirectory sddRoot |> ignore
            Environment.SetEnvironmentVariable("FSGG_COORD_SDD_ROOT", sddRoot)
            Console.SetOut capturedOut
            Console.SetError capturedErr

            let opts =
                match Options.parse args with
                | Ok o -> o
                | Error e -> failwithf "the fixture's own argv did not parse: %s" e

            let code = Client.deliveryRouteCmd (context transport) opts
            Console.Out.Flush()
            Console.Error.Flush()
            code, capturedOut.ToString(), capturedErr.ToString()
        finally
            Console.SetOut stdout
            Console.SetError stderr
            Environment.SetEnvironmentVariable("FSGG_COORD_SDD_ROOT", previousRoot)
            try Directory.Delete(sddRoot, true) with _ -> ()

    /// Drive `Client.claim` — the MUTATION boundary — against a throwaway cache, on the same licence
    /// `ForceStealTests.runClaim` states: `AssemblyInfo.fs` disables cross-class parallelism, so the
    /// process-global `FSGG_COORD_CACHE` is safe to point somewhere private per call, and a fresh
    /// directory stops a board map leaking between legs. The identity ladder is pinned in BOTH halves
    /// (#1646) for the same reason it is there: an unpinned `$FSGG_WORKER` makes every leg an
    /// impersonation and `claim` refuses before it reads anything at all — which would make these legs
    /// pass for the wrong reason.
    ///
    /// The claim's own EXIT CODE is not what any leg below asserts. This fixture serves no board
    /// bootstrap, so `claim` fails after the route check; what is under test is whether the route check
    /// reported, and stderr is where that lives.
    let private runClaim (transport: Fake.Recorder) (args: string list) : int * string * string =
        let dir = Path.Combine(Path.GetTempPath(), "fsgg-2583-claim-" + Guid.NewGuid().ToString "n")
        let sessionVars = [ "CLAUDE_CODE_SESSION_ID"; "OPENCODE_SESSION_ID"; "FSGG_AGENT_SESSION_ID"; "FSGG_WORKER" ]
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

            try Directory.Delete(dir, true) with _ -> ()

    let private lightweightReceipt (subjectRevision: string) =
        $"""{{"schema":"fsgg.coord.delivery-route/v1","subject":"FS-GG/FS.GG.SDD#42","subjectRevision":"%s{subjectRevision}","route":"lightweight","agent":"fixture-2583","timestamp":"2026-01-01T00:00:00Z","reasonCodes":["fixture"],"rationale":"fixture lightweight receipt for .github#2583","declaredImpacts":["internal"],"observedFacts":["localized"],"sddWorkId":null,"specHome":null,"requiredGates":[]}}"""

    let private structuredLightweightRecord () =
        let draft : StructuredDecision.RouteRecord =
            { Schema = StructuredDecision.RouteSchema; Subject = "FS-GG/FS.GG.SDD#42"; Revision = 1
              PreviousDigest = None; Scope = [ "consolidation contract" ]; Dependencies = [ "none" ]
              TouchSet = [ "src/FS.GG.Coord.Cli/Client.fs" ]; PolicyVersion = StructuredDecision.PolicyVersion
              Route = Some DeliveryRoute.Lightweight; Agent = "fixture-2583"; Timestamp = "2026-01-01T00:00:00Z"
              ReasonCodes = [ "structured" ]; Rationale = "body is narrative"; SddWorkId = None
              SpecHome = None; RequiredGates = []; Digest = "" }
        let record = { draft with Digest = StructuredDecision.routeDigest draft }
        record,
        JsonSerializer.Serialize
            {| schema = record.Schema; subject = record.Subject; revision = record.Revision
               previousDigest = record.PreviousDigest; scope = record.Scope; dependencies = record.Dependencies
               touchSet = record.TouchSet; policyVersion = record.PolicyVersion; route = "lightweight"
               agent = record.Agent; timestamp = record.Timestamp; reasonCodes = record.ReasonCodes
               rationale = record.Rationale; sddWorkId = record.SddWorkId; specHome = record.SpecHome
               requiredGates = record.RequiredGates; digest = record.Digest |}

    /// RECORD FIRST, THEN EDIT, THEN SHOW — the real sequence, and the reason the locator marker under
    /// test is the one PRODUCTION wrote rather than one this file authored.
    let private recordThenEdit (originalBody: string) (editedBody: string) =
        let world = World originalBody
        let transport = transportFor world
        // M4 retains this evidence as READ-ONLY compatibility. New writes are v2 structured records;
        // seeding the exact historical bytes is the representative legacy replay.
        world.Seed(DeliveryRouteMarker + "\n" + locatorLine originalBody + "\n" + lightweightReceipt (revision originalBody))
        world.Body <- editedBody
        runRoute transport [ "delivery-route"; "show"; "FS.GG.SDD#42" ]

    let private shown (out: string) = JsonDocument.Parse(out.Trim()).RootElement

    [<Fact>]
    let ``M4 body-only edits neither revoke nor alter a structured route authorization`` () =
        let world = World "Original narrative\nPaths: src/A.fs"
        let transport = transportFor world
        let record, json = structuredLightweightRecord ()
        let path = Path.Combine(Path.GetTempPath(), "fsgg-m4-route-" + Guid.NewGuid().ToString "n" + ".json")
        File.WriteAllText(path, json)
        try
            let recorded, _, _ = runRoute transport [ "delivery-route"; "record"; "FS.GG.SDD#42"; path ]
            Assert.Equal(0, recorded)
            world.Body <- "Completely rewritten human narrative\nPaths: docs/**\nBlocked on: human/decision"
            let code, output, _ = runRoute transport [ "delivery-route"; "show"; "FS.GG.SDD#42" ]
            Assert.Equal(0, code)
            let result = shown output
            Assert.Equal("structured-only", result.GetProperty("evidenceClassification").GetString())
            Assert.Equal(record.Digest, result.GetProperty("decisionRevision").GetString())
            Assert.Equal(record.Revision, result.GetProperty("revision").GetInt32())
            Assert.Equal(record.Digest, result.GetProperty("digest").GetString())
            Assert.Equal("lightweight", result.GetProperty("route").GetString())
        finally
            try File.Delete path with _ -> ()

    // ---- edits that ADD ONLY: route-neutral (FR-001 / AC-001) ------------------------------------------

    let private consolidationBlock =
        "\n\n## Folded from .github#9999\n\nThat row carries the same cause; its evidence is transplanted here rather than\nleft on a second number.\n"

    [<Fact>]
    let ``#2583 an APPENDED consolidation leaves the receipt current and is reported as additive`` () =
        forEachBody (fun _ body ->
            let code, out, err = recordThenEdit body (body + consolidationBlock)

            Assert.Equal(0, code)
            let result = shown out
            Assert.Equal("current", result.GetProperty("kind").GetString())
            Assert.Equal("additive", result.GetProperty("subjectMatch").GetString())

            // The block contributes exactly three subject lines: the heading and the two prose lines.
            // Blank lines are dropped by the subject filter, so the count is stated, not derived.
            Assert.Equal(3, result.GetProperty("addedSubjectLines").GetInt32())

            // FR-004: the one edge a structural rule cannot see is REPORTED, never silent.
            Assert.Contains("3 subject line(s) added since this route was decided", err)
            Assert.Contains("Re-record it if the addition changed the work's scope.", err))

    [<Fact>]
    let ``#2583 a MID-BODY consolidation is route-neutral too, so append is not special-cased`` () =
        forEachBody (fun _ body ->
            // Insert immediately before the LAST subject line the body carries, which for every corpus
            // body is deep inside it — the shape a real fold takes when it lands in `## Dedupe` or
            // `## Root cause` rather than at the end.
            let lines = body.Split '\n' |> List.ofArray
            let lastSubject = subjectLines body |> List.last
            let index = lines |> List.findIndex (fun line -> line = lastSubject)

            let edited =
                (List.truncate index lines) @ [ "## Folded from .github#9999"; ""; "Same cause, evidence transplanted." ] @ (List.skip index lines)
                |> String.concat "\n"

            let code, out, _ = recordThenEdit body edited

            Assert.Equal(0, code)
            let result = shown out
            Assert.Equal("current", result.GetProperty("kind").GetString())
            Assert.Equal("additive", result.GetProperty("subjectMatch").GetString())
            Assert.Equal(2, result.GetProperty("addedSubjectLines").GetInt32()))

    [<Fact>]
    let ``#2583 a PREPENDED consolidation is route-neutral, so the rule is not an append rule wearing a hat`` () =
        forEachBody (fun _ body ->
            let code, out, _ = recordThenEdit body ("## Folded from .github#9999\n\nSame cause.\n\n" + body)

            Assert.Equal(0, code)
            let result = shown out
            Assert.Equal("additive", result.GetProperty("subjectMatch").GetString())
            Assert.Equal(2, result.GetProperty("addedSubjectLines").GetInt32()))

    // ---- edits that CHANGE what was judged: still stale (FR-002 / AC-002) -------------------------------

    /// `.github#2392` exists because a route affirmed against one subject is not affirmed against
    /// another. Each of these three legs breaks the subsequence in a different way — the text at a
    /// judged position, the presence of a judged line, and the ORDER of two of them — and each must
    /// still cost the row its receipt.
    let private assertStale (code: int) (out: string) (err: string) =
        Assert.NotEqual(0, code)
        Assert.Equal("", out.Trim())
        Assert.Contains("subjectRevision is stale", err)

    [<Fact>]
    let ``#2583 MODIFYING a judged subject line still stales the receipt`` () =
        forEachBody (fun _ body ->
            let target = subjectLines body |> List.last
            let code, out, err = recordThenEdit body (body.Replace(target, target + " AND ALSO EVERY DOWNSTREAM REPOSITORY"))
            assertStale code out err)

    [<Fact>]
    let ``#2583 DELETING a judged subject line still stales the receipt`` () =
        forEachBody (fun _ body ->
            let target = subjectLines body |> List.last

            let edited =
                body.Split '\n'
                |> Array.filter (fun line -> line <> target)
                |> String.concat "\n"

            let code, out, err = recordThenEdit body edited
            assertStale code out err)

    [<Fact>]
    let ``#2583 REORDERING two judged subject lines still stales the receipt`` () =
        forEachBody (fun _ body ->
            let judged = subjectLines body
            let first = List.head judged
            let last = List.last judged
            Assert.NotEqual<string>(first, last)

            let edited =
                body.Split '\n'
                |> Array.map (fun line ->
                    if line = first then last
                    elif line = last then first
                    else line)
                |> String.concat "\n"

            let code, out, err = recordThenEdit body edited
            assertStale code out err)

    // ---- nothing that was current becomes stale (FR-003 / AC-003) --------------------------------------

    [<Fact>]
    let ``#2583 a Paths widen is still route-neutral and still resolves through the CANONICAL candidate`` () =
        forEachBody (fun name body ->
            // `.github#2392`'s own guarantee. It must keep resolving canonically — not merely resolve —
            // or this change would have quietly moved a case from one candidate to another.
            let hasPaths = body.Split '\n' |> Array.exists (fun line -> volatileDeclarationLine.IsMatch line && line.StartsWith("Paths:", StringComparison.Ordinal))
            Assert.True(hasPaths, $"corpus body %s{name} carries no `Paths:` declaration, so it cannot witness .github#2392's exclusion")

            let edited =
                body.Split '\n'
                |> Array.map (fun line -> if line.StartsWith("Paths:", StringComparison.Ordinal) then line + " docs/newly-widened.md" else line)
                |> String.concat "\n"

            let code, out, _ = recordThenEdit body edited

            Assert.Equal(0, code)
            let result = shown out
            Assert.Equal("current", result.GetProperty("kind").GetString())
            Assert.Equal("canonical", result.GetProperty("subjectMatch").GetString())
            Assert.Equal(0, result.GetProperty("addedSubjectLines").GetInt32()))

    [<Fact>]
    let ``#2583 an unedited body still resolves through the CANONICAL candidate, reporting no additions`` () =
        forEachBody (fun _ body ->
            let code, out, err = recordThenEdit body body

            Assert.Equal(0, code)
            let result = shown out
            Assert.Equal("canonical", result.GetProperty("subjectMatch").GetString())
            Assert.Equal(0, result.GetProperty("addedSubjectLines").GetInt32())
            Assert.DoesNotContain("subject line(s) added", err))

    [<Fact>]
    let ``#2583 .github#2392's pre-fix whole-body receipt still resolves through the LEGACY candidate`` () =
        forEachBody (fun _ body ->
            // A receipt carrying the PRE-#2392 whole-body hash, exactly as one recorded before that fix
            // does. It must still be `Current`, and it must reach that verdict through the legacy arm.
            let world = World body
            let transport = transportFor world
            world.Seed(DeliveryRouteMarker + "\n" + lightweightReceipt (hashHex body))

            let code, out, _ = runRoute transport [ "delivery-route"; "show"; "FS.GG.SDD#42" ]

            Assert.Equal(0, code)
            let result = shown out
            Assert.Equal("current", result.GetProperty("kind").GetString())
            Assert.Equal("legacy", result.GetProperty("subjectMatch").GetString()))

    // ---- the locator record: derived, fail-closed (FR-006 / FR-007) ------------------------------------

    [<Fact>]
    let ``M4 legacy route records are readable but the write path refuses to author another v1 record`` () =
        forEachBody (fun _ body ->
            let world = World body
            let transport = transportFor world
            let receipt = lightweightReceipt (revision body)
            let receiptPath = Path.Combine(Path.GetTempPath(), "fsgg-2583-verbatim-" + Guid.NewGuid().ToString "n" + ".json")
            File.WriteAllText(receiptPath, receipt)

            try
                let code, _, _ = runRoute transport [ "delivery-route"; "record"; "FS.GG.SDD#42"; receiptPath ]
                Assert.Equal(1, code)
                Assert.Empty(world.Bodies)
            finally
                try File.Delete receiptPath with _ -> ())

    [<Fact>]
    let ``#2583 a receipt with NO locator marker decides exactly as it did before this change`` () =
        forEachBody (fun _ body ->
            // The pre-.github#2583 comment shape, which every e2e, parity and replay fixture in this
            // repository still writes. An additive edit against it must STILL be stale: absence of the
            // locator record is never read as permission (#266).
            let world = World body
            let transport = transportFor world
            world.Seed(DeliveryRouteMarker + "\n" + lightweightReceipt (revision body))

            let unedited, _, _ = runRoute transport [ "delivery-route"; "show"; "FS.GG.SDD#42" ]
            Assert.Equal(0, unedited)

            world.Body <- body + consolidationBlock
            let code, out, err = runRoute transport [ "delivery-route"; "show"; "FS.GG.SDD#42" ]
            assertStale code out err)

    [<Fact>]
    let ``#2583 a locator marker carrying a wrong locator is refused, not trusted`` () =
        forEachBody (fun _ body ->
            let world = World body
            let transport = transportFor world

            let corrupted =
                let good = locatorLine body
                good.Replace(good.Substring(SubjectLinesMarkerPrefix.Length, 16), "0000000000000000")

            world.Seed(DeliveryRouteMarker + "\n" + corrupted + "\n" + lightweightReceipt (revision body))

            world.Body <- body + consolidationBlock
            let code, out, err = runRoute transport [ "delivery-route"; "show"; "FS.GG.SDD#42" ]
            assertStale code out err)

    [<Fact>]
    let ``#2583 a locator marker whose envelope is truncated is refused, not silently skipped`` () =
        forEachBody (fun _ body ->
            let world = World body
            let transport = transportFor world

            // No closing `-->`. `splitSubjectLineLocators` declines to consume it, so it stays in the
            // JSON payload and the decode fails — the receipt reads as MISSING rather than as a
            // well-formed receipt with no locator record.
            let truncated = (locatorLine body).Replace(" -->", "")
            world.Seed(DeliveryRouteMarker + "\n" + truncated + "\n" + lightweightReceipt (revision body))

            let code, out, err = runRoute transport [ "delivery-route"; "show"; "FS.GG.SDD#42" ]

            Assert.NotEqual(0, code)
            Assert.Equal("", out.Trim())
            Assert.Contains("delivery-route receipt is missing", err))

    /// THE FULL-WIDTH CHECK IS THE ARBITER, AND THIS IS THE LEG THAT PROVES IT (.github#2583 DEC-002).
    ///
    /// The locator digests only SELECT which current lines correspond to the judged ones; acceptance is
    /// the full `hashHex` of the reconstructed subsequence against the receipt's own `subjectRevision`.
    /// A 16-hex-character collision will never occur in a test, so the check would otherwise be
    /// defence-in-depth that nothing can red. This leg reproduces its exact consequence deterministically:
    /// a locator record that ALIGNS perfectly (it is a genuine subsequence of the body's own locators)
    /// but whose reconstruction is NOT the judged subject, because one judged line is missing from it.
    ///
    /// That is precisely the shape a locator collision would produce — a successful but WRONG alignment —
    /// and it must be refused. Delete the `hashHex … = recordedRevision` guard and this leg goes red while
    /// every other leg in this file stays green, which is what makes the guard's necessity measured rather
    /// than asserted.
    [<Fact>]
    let ``#2583 a locator record that aligns but does not reconstruct the judged subject is refused`` () =
        forEachBody (fun _ body ->
            let world = World body
            let transport = transportFor world

            // Drop the middle locator. The remainder is still an ordered subsequence of the body's own
            // locators, so alignment succeeds — but the lines it reconstructs are one short of the
            // subject `subjectRevision` was computed over.
            let locators = subjectLines body |> List.map (fun line -> (hashHex line).Substring(0, 16))
            let middle = List.length locators / 2
            let aligning = (List.truncate middle locators) @ (List.skip (middle + 1) locators)

            world.Seed(
                DeliveryRouteMarker
                + "\n"
                + SubjectLinesMarkerPrefix + String.concat " " aligning + " -->"
                + "\n"
                + lightweightReceipt (revision body)
            )

            world.Body <- body + consolidationBlock
            let code, out, err = runRoute transport [ "delivery-route"; "show"; "FS.GG.SDD#42" ]
            assertStale code out err)

    // ---- the DEGENERATE body: a judged subject that constrains nothing (review round 1, finding 1) ------

    /// A body whose EVERY line is a volatile declaration or blank, so its subject is EMPTY. This is not a
    /// contrived shape — it is what a freshly filed row looks like before anyone writes prose into it.
    ///
    /// IT CANNOT LIVE IN THE CORPUS, AND THAT IS THE LESSON. `forEachBody` asserts
    /// `MinimumSubjectLinesPerBody = 30` before any verdict, which is a good non-vacuity floor for the
    /// real-body corpus AND excludes this shape by construction. Round 1 of this PR's review found a
    /// permanent false positive living exactly there, invisible to every corpus leg: a non-vacuity floor
    /// on a CORPUS is not a non-vacuity guarantee for the CODE, and a floor that keeps fixtures honest can
    /// simultaneously hide the degenerate case. So the degenerate case gets its own legs, outside the
    /// floor, named for what they are.
    let private emptySubjectBody = "Paths: src/FS.GG.Coord.Cli/Client.fs\n\nClass: defect\n\nBlocked on: \n"

    [<Fact>]
    let ``#2583 an empty judged subject never authorises an additive match against a DIFFERENT body`` () =
        // Without the empty-`recorded` guard this is `current`/`additive`: `record` writes a zero-locator
        // marker and a `subjectRevision` of `hashHex ""`, alignment consumes the empty want-list
        // vacuously, and the full-width guard compares `hashHex ""` to a recorded revision that IS
        // `hashHex ""`. It holds for EVERY body — the receipt would match a wholesale replacement of the
        // issue, permanently, with no hash collision anywhere. Measured in review round 1 as
        // `addedSubjectLines: 60` against the unrelated `2392` body.
        Assert.Empty(subjectLines emptySubjectBody)
        Assert.Equal(hashHex "", revision emptySubjectBody)

        let _, replacement = corpus |> List.find (fun (name, _) -> name = "2392")
        let code, out, err = recordThenEdit emptySubjectBody replacement

        assertStale code out err

        // ARM 1 OF THE PORTED SHAPE, and the half the first cut missed: the degenerate case carries its
        // OWN diagnosis, not merely its own refusal. `check-gate-finding-history.py` gives zero-runs
        // `NEVER-RAN`/`REUSABLE-ELSEWHERE` with their own detail strings rather than folding them into
        // the floor; refusing correctly while reporting it as an ordinary stale receipt would send a
        // reader hunting for a damaged locator record that is in fact perfectly well formed.
        Assert.Contains("judged an EMPTY subject", err)
        Assert.Contains("re-record the receipt against the current body", err)

    [<Fact>]
    let ``#2583 an ORDINARY stale receipt does NOT carry the vacuous-subject diagnosis`` () =
        // The discriminator for the leg above. A named verdict that fires on every refusal names nothing.
        forEachBody (fun _ body ->
            let target = subjectLines body |> List.last
            let _, _, err = recordThenEdit body (body.Replace(target, target + " AND ALSO EVERYTHING ELSE"))

            Assert.Contains("subjectRevision is stale", err)
            Assert.DoesNotContain("judged an EMPTY subject", err))

    [<Fact>]
    let ``#2583 an empty judged subject is still CANONICALLY current while the body's subject stays empty`` () =
        // The guard must refuse the vacuous ACCEPTANCE without breaking the legitimate degenerate case:
        // an unchanged empty-subject body is `current` through the canonical arm, exactly as before this
        // PR. Nothing is lost by refusing the additive arm here, because the canonical arm answers first.
        let code, out, _ = recordThenEdit emptySubjectBody (emptySubjectBody + "\nClass: chore\n")

        Assert.Equal(0, code)
        let result = shown out
        Assert.Equal("current", result.GetProperty("kind").GetString())
        Assert.Equal("canonical", result.GetProperty("subjectMatch").GetString())

    [<Fact>]
    let ``#2583 an empty judged subject is refused even when the new body only ADDS lines`` () =
        // The narrowest form: the edit really is purely additive by the diff, and the answer is still
        // `Stale`, because the route decision judged nothing that could survive. "Nothing was judged" is
        // never "everything was judged and all of it survived" (#266).
        let code, out, err = recordThenEdit emptySubjectBody (emptySubjectBody + "\n## Newly written scope\n\nEverything about this row is now different.\n")

        assertStale code out err

    // ---- the DEC-001 payment reaches the boundary that acts on the row (review round 1, finding 2) -----

    /// DEC-001 accepts that a pure insertion can redefine scope BECAUSE the residual is paid for by
    /// reporting rather than silence. Round 1 established that the payment was made only at
    /// `delivery-route show`, while the claim/take mutation boundary — the moment a worker commits to the
    /// route — accepted an additive match and said nothing at all. A trade whose consideration is not
    /// delivered where it is spent is not the trade that was agreed.
    [<Fact>]
    let ``#2583 the claim boundary emits the additive notice, not only delivery-route show`` () =
        forEachBody (fun _ body ->
            let world = World body
            let transport = transportFor world
            world.Seed(DeliveryRouteMarker + "\n" + locatorLine body + "\n" + lightweightReceipt (revision body))
            world.Body <- body + consolidationBlock

            // `Client.claim` runs the SAME route check (`requireCurrentDeliveryRoute`) and then goes
            // on to a board bootstrap this fixture does not serve. The claim's own exit code is not
            // the subject here — passing the route check is, and the notice is what proves it did.
            let _, _, err = runClaim transport [ "claim"; "FS.GG.SDD#42" ]
            Assert.Contains("3 subject line(s) added since this route was decided", err))

    [<Fact>]
    let ``#2583 the claim boundary stays SILENT when the match is canonical`` () =
        // The notice must be conditional, or it is not information. An unedited body reaches the claim
        // boundary through the canonical arm and says nothing.
        forEachBody (fun _ body ->
            let world = World body
            let transport = transportFor world
            world.Seed(DeliveryRouteMarker + "\n" + locatorLine body + "\n" + lightweightReceipt (revision body))

            let _, _, err = runClaim transport [ "claim"; "FS.GG.SDD#42" ]
            Assert.DoesNotContain("subject line(s) added", err))

    [<Fact>]
    let ``#2583 the claim boundary still REFUSES a modified judged line`` () =
        // The discriminator, so the two legs above are not both explained by "the route check never runs".
        forEachBody (fun _ body ->
            let world = World body
            let transport = transportFor world
            world.Seed(DeliveryRouteMarker + "\n" + locatorLine body + "\n" + lightweightReceipt (revision body))

            let target = subjectLines body |> List.last
            world.Body <- body.Replace(target, target + " AND ALSO EVERY DOWNSTREAM REPOSITORY")

            let _, _, err = runClaim transport [ "claim"; "FS.GG.SDD#42" ]
            Assert.Contains("delivery route is not current", err)
            Assert.DoesNotContain("subject line(s) added", err))

    // ---- the subject filter has exactly one statement in production ------------------------------------

    [<Fact>]
    let ``#2583 the line-wise subject and the joined subject agree on every corpus body`` () =
        forEachBody (fun name body ->
            // The v1 record is seeded as migration evidence; only reads remain legal after M4.
            let world = World body
            let transport = transportFor world
            world.Seed(DeliveryRouteMarker + "\n" + locatorLine body + "\n" + lightweightReceipt (revision body))
            let code, out, _ = runRoute transport [ "delivery-route"; "show"; "FS.GG.SDD#42" ]
            Assert.Equal(0, code)
            let result = shown out
            Assert.Equal("canonical", result.GetProperty("subjectMatch").GetString())
            Assert.Equal(revision body, result.GetProperty("decisionRevision").GetString())
            Assert.True(List.length (subjectLines body) > 0, $"corpus body %s{name} has no subject lines at all"))
