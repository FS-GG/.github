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

    let private lightweightReceipt (subjectRevision: string) =
        $"""{{"schema":"fsgg.coord.delivery-route/v1","subject":"FS-GG/FS.GG.SDD#42","subjectRevision":"%s{subjectRevision}","route":"lightweight","agent":"fixture-2583","timestamp":"2026-01-01T00:00:00Z","reasonCodes":["fixture"],"rationale":"fixture lightweight receipt for .github#2583","declaredImpacts":["internal"],"observedFacts":["localized"],"sddWorkId":null,"specHome":null,"requiredGates":[]}}"""

    /// RECORD FIRST, THEN EDIT, THEN SHOW — the real sequence, and the reason the locator marker under
    /// test is the one PRODUCTION wrote rather than one this file authored.
    let private recordThenEdit (originalBody: string) (editedBody: string) =
        let world = World originalBody
        let transport = transportFor world
        let receiptPath = Path.Combine(Path.GetTempPath(), "fsgg-2583-receipt-" + Guid.NewGuid().ToString "n" + ".json")
        File.WriteAllText(receiptPath, lightweightReceipt (revision originalBody))

        try
            let recordCode, recordOut, _ = runRoute transport [ "delivery-route"; "record"; "FS.GG.SDD#42"; receiptPath ]
            Assert.Equal(0, recordCode)
            Assert.Equal("recorded", JsonDocument.Parse(recordOut.Trim()).RootElement.GetProperty("kind").GetString())

            world.Body <- editedBody
            runRoute transport [ "delivery-route"; "show"; "FS.GG.SDD#42" ]
        finally
            try File.Delete receiptPath with _ -> ()

    let private shown (out: string) = JsonDocument.Parse(out.Trim()).RootElement

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
    let ``#2583 record derives the locator marker and leaves the authored receipt JSON byte-verbatim`` () =
        forEachBody (fun _ body ->
            let world = World body
            let transport = transportFor world
            let receipt = lightweightReceipt (revision body)
            let receiptPath = Path.Combine(Path.GetTempPath(), "fsgg-2583-verbatim-" + Guid.NewGuid().ToString "n" + ".json")
            File.WriteAllText(receiptPath, receipt)

            try
                let code, _, _ = runRoute transport [ "delivery-route"; "record"; "FS.GG.SDD#42"; receiptPath ]
                Assert.Equal(0, code)

                // Exactly three parts, in this order, and the third is the author's bytes unchanged.
                Assert.Equal<string list>([ DeliveryRouteMarker + "\n" + locatorLine body + "\n" + receipt ], world.Bodies)
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

    // ---- the subject filter has exactly one statement in production ------------------------------------

    [<Fact>]
    let ``#2583 the line-wise subject and the joined subject agree on every corpus body`` () =
        forEachBody (fun name body ->
            // `deliveryRouteSubject` is `deliveryRouteSubjectLines` joined and nothing else. Measured
            // here through the observable consequence: a receipt whose `subjectRevision` is the JOINED
            // form is accepted by `record`, whose locator marker is built from the LINE-WISE form, and
            // `show` then resolves it canonically with zero additions.
            let world = World body
            let transport = transportFor world
            let receiptPath = Path.Combine(Path.GetTempPath(), "fsgg-2583-agree-" + Guid.NewGuid().ToString "n" + ".json")
            File.WriteAllText(receiptPath, lightweightReceipt (revision body))

            try
                let recorded, _, _ = runRoute transport [ "delivery-route"; "record"; "FS.GG.SDD#42"; receiptPath ]
                Assert.Equal(0, recorded)

                let code, out, _ = runRoute transport [ "delivery-route"; "show"; "FS.GG.SDD#42" ]
                Assert.Equal(0, code)

                let result = shown out
                Assert.Equal("canonical", result.GetProperty("subjectMatch").GetString())
                Assert.Equal(revision body, result.GetProperty("subjectRevision").GetString())
                Assert.True(List.length (subjectLines body) > 0, $"corpus body %s{name} has no subject lines at all")
            finally
                try File.Delete receiptPath with _ -> ())
