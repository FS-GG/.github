module FS.GG.Coord.GitHub.Tests.GraphQlErrorsFirstTests

open System
open System.IO
open System.Text.RegularExpressions
open Xunit
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Errors
open FS.GG.Coord.GitHub.Transport

/// A PARTIAL 200 IS NOT A COMPLETE ANSWER (`.github#2534`).
///
/// GitHub reports an exhausted GraphQL budget — and any other partial field failure — as an HTTP **200
/// carrying BOTH `data` and `errors`**. At the transport layer it is byte-indistinguishable from a
/// complete response: same status, same shape, populated `data`. The only thing that separates them is
/// whether the reader ASKS.
///
/// Three reads in `Reads.fs` did not ask. `recentCommentBodies` and `subIssues` accepted a partially
/// populated `nodes` array as a complete one; `prClosingRef` turned a rate-limited response into
/// `Ok None` — the positive assertion *"this PR closes nothing"* — which `verify-paths` renders as
/// `FSGG-PATHS SKIP … ExitGreen`. A failed read wearing a green verdict's clothes: #461's founding shape,
/// one layer up.
///
/// THIS MODULE HOLDS THE TWO HALVES OF THE REPAIR.
///
/// 1. **Behaviour** — each read, driven through the real transport seam with a real partial 200, must
///    report a FAILED READ, and must report a rate limit AS a rate limit (`Budget.ofGraphQlErrors` first)
///    rather than folding it into the generic malformed arm. Each has a controlled counterpart: the same
///    read, the same fixture minus the `errors` key, still parses normally. A refusal that also refuses
///    the good response is not a guard, it is an outage.
///
/// 2. **Structure** — the ordering was a convention enforced by repetition (`Board.graphQlData` was
///    correct; `Scan.fs`, `Done.fs` and `Reads.fs`'s content-edit read each re-implemented it correctly;
///    nothing made a read that SKIPPED it fail). `graphQlData` in `Reads.fs` makes it a function every
///    read must call to reach `data`, and `the errors check precedes every data extraction` below makes
///    the layer-wide property a build failure rather than a fourth comment.
module private Fixtures =

    /// A transport that answers every request with one canned body, at HTTP 200.
    let serving (body: string) =
        Fake.Recorder(fun _ ->
            Ok
                { Status = 200
                  Body = body
                  ETag = None
                  NextLink = None
                  Headers = Map.empty })

    /// GitHub's primary-budget wording, as it arrives inside a 200's `errors` array.
    let [<Literal>] RateLimitMessage = "API rate limit exceeded for installation ID 1234."

    /// GitHub's secondary (abuse-detection) wording — a DIFFERENT fact, and #1666 is the cost of
    /// conflating them.
    let [<Literal>] SecondaryMessage = "You have exceeded a secondary rate limit."

    /// A generic partial-field failure: not a budget, still not a complete answer.
    let [<Literal>] PartialMessage = "Could not resolve to a node with the global id of 'abc'."

    /// `errors`, rendered as GitHub renders it.
    let errorsArray (message: string) = $""""errors":[{{"message":"{message}","path":["repository"]}}]"""

    // ---- the four GraphQL reads in `Reads.fs`, each as a COMPLETE payload -------------------------
    //
    // Every one of these is a genuinely well-formed, fully-populated response. That is the point: the
    // `errors` key is the ONLY difference between the partial fixtures below and these, so a test that
    // passes on both has proved nothing, and one that fails on both has broken the read.

    let [<Literal>] CommentsData =
        """"data":{"repository":{"issue":{"comments":{"nodes":[{"body":"first"},{"body":"second"}]}}}}"""

    let [<Literal>] SubIssuesData =
        """"data":{"repository":{"issue":{"subIssues":{"totalCount":1,"nodes":[{"number":398,"state":"CLOSED","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}]}}}}"""

    /// A PR that genuinely closes ONE issue.
    let [<Literal>] ClosingRefData =
        """"data":{"repository":{"pullRequest":{"closingIssuesReferences":{"nodes":[{"number":2534,"repository":{"nameWithOwner":"FS-GG/.github"}}]}}}}"""

    /// A PR that genuinely closes NOTHING — the one state `Ok None` is entitled to describe.
    let [<Literal>] ClosingRefEmptyData =
        """"data":{"repository":{"pullRequest":{"closingIssuesReferences":{"nodes":[]}}}}"""

    let [<Literal>] ContentEditsData =
        """"data":{"repository":{"issueOrPullRequest":{"userContentEdits":{"totalCount":1,"nodes":[{"editedAt":"2026-08-13T10:00:00Z","editor":{"login":"EHotwagner"}}]}}}}"""

    /// The complete payload, alone — a clean 200 with no `errors` key at all.
    let clean (data: string) = "{" + data + "}"

    /// The SAME complete payload, with `errors` beside it — GitHub's partial 200.
    let partial (data: string) (message: string) = "{" + data + "," + errorsArray message + "}"

open Fixtures

// ---- 1. behaviour: each read refuses a partial 200 --------------------------------------------------

[<Fact>]
let ``.github#2534 recentCommentBodies refuses a partial 200 instead of returning the visible comments`` () =
    // BEFORE THE REPAIR this returned `Ok [ "first"; "second" ]` — a truncated comment tail rendered as
    // the whole one. `Client.readDeliveryRouteVerdict` searches that tail for the newest receipt marker
    // and reads "no match" as "no current receipt", so a partial page becomes a refusal to schedule an
    // item that is in fact routed — or, on the other side of the same coin, a stale receipt read as
    // current because the fresher one was in the part that did not arrive.
    let transport = serving (partial CommentsData PartialMessage)

    match Reads.recentCommentBodies transport "FS-GG" ".github" 2534 20 with
    | Error(GraphQlErrors messages) -> Assert.Contains(PartialMessage, messages)
    | other -> failwith $"a partial 200 is a failed read, never a complete comment tail — got %A{other}"

[<Fact>]
let ``.github#2534 recentCommentBodies reports an exhausted budget AS a rate limit`` () =
    // `Budget.ofGraphQlErrors` FIRST. Folding this into the generic malformed arm destroys the one fact
    // the caller needs — that the condition is TEMPORARY — and turns a back-off into a refusal.
    let transport = serving (partial CommentsData RateLimitMessage)

    match Reads.recentCommentBodies transport "FS-GG" ".github" 2534 20 with
    | Error(RateLimited(GraphQlBudget, None)) -> ()
    | other -> failwith $"an exhausted GraphQL budget must be reported as one — got %A{other}"

[<Fact>]
let ``.github#2534 recentCommentBodies still reads a clean 200`` () =
    // THE CONTROLLED COUNTERPART. Same payload, no `errors` key.
    let transport = serving (clean CommentsData)

    match Reads.recentCommentBodies transport "FS-GG" ".github" 2534 20 with
    | Ok bodies -> Assert.Equal<string list>([ "first"; "second" ], bodies)
    | other -> failwith $"a clean 200 must still parse — got %A{other}"

[<Fact>]
let ``.github#2534 subIssues refuses a partial 200 instead of rolling up a truncated graph`` () =
    // BEFORE THE REPAIR this returned `Ok { Total = 1; Children = [ #398 closed ] }`, and the rollup
    // reads "every child is done" off a set already known to be short — #266's shape exactly, and
    // `SubIssueSet`'s own `Total`-vs-`Children` guard cannot see it, because a partial 200 corrupts
    // BOTH numbers consistently.
    let transport = serving (partial SubIssuesData PartialMessage)

    match Reads.subIssues transport "FS-GG" "FS.GG.SDD" 50 with
    | Error(GraphQlErrors messages) -> Assert.Contains(PartialMessage, messages)
    | other -> failwith $"a partial 200 is a failed read, never a complete sub-issue graph — got %A{other}"

[<Fact>]
let ``.github#2534 subIssues reports a SECONDARY limit as a secondary limit, not the GraphQL budget`` () =
    // #1666: ties go to `Secondary`, and the reset is `None` — a secondary limit is account-wide and
    // carries no budget reset. Printing the primary reset here sends the fleet back in at full
    // concurrency the moment it elapses.
    let transport = serving (partial SubIssuesData SecondaryMessage)

    match Reads.subIssues transport "FS-GG" "FS.GG.SDD" 50 with
    | Error(RateLimited(SecondaryLimit(None, None), None)) -> ()
    | other -> failwith $"a secondary limit must not be reported as the GraphQL budget — got %A{other}"

[<Fact>]
let ``.github#2534 subIssues still reads a clean 200`` () =
    let transport = serving (clean SubIssuesData)

    match Reads.subIssues transport "FS-GG" "FS.GG.SDD" 50 with
    | Ok graph ->
        Assert.Equal(1, graph.Total)
        Assert.Equal<string list>([ "FS-GG/FS.GG.SDD#398" ], graph.Children |> List.map _.Ref)
    | other -> failwith $"a clean 200 must still parse — got %A{other}"

[<Fact>]
let ``.github#2534 prClosingRef refuses a partial 200 instead of answering 'this PR closes nothing'`` () =
    // THE SHARPEST OF THE THREE. Before the repair a rate-limited response reached the `nodes` extraction,
    // threw, and was caught into `Ok None` — which `verify-paths` prints as
    // `FSGG-PATHS SKIP … ExitGreen`: a GREEN touch-set verdict on a PR whose closing graph nobody read.
    let transport = serving (partial ClosingRefEmptyData RateLimitMessage)

    match Reads.prClosingRef transport "FS-GG" ".github" 2540 with
    | Error(RateLimited(GraphQlBudget, None)) -> ()
    | other -> failwith $"a rate-limited closing-ref read is not 'closes nothing' — got %A{other}"

[<Fact>]
let ``.github#2534 prClosingRef refuses a partial 200 even when the visible nodes look complete`` () =
    let transport = serving (partial ClosingRefData PartialMessage)

    match Reads.prClosingRef transport "FS-GG" ".github" 2540 with
    | Error(GraphQlErrors messages) -> Assert.Contains(PartialMessage, messages)
    | other -> failwith $"a partial 200 is a failed read, never a closing reference — got %A{other}"

[<Fact>]
let ``.github#2534 prClosingRef Ok None means MEASURED none - an empty connection, cleanly read`` () =
    // THE ONE STATE `Ok None` STILL DESCRIBES, and it must survive the repair intact: a PR that genuinely
    // closes no issue is a real answer, not a failure, and `verify-paths`'s green skip is correct for it.
    let transport = serving (clean ClosingRefEmptyData)

    match Reads.prClosingRef transport "FS-GG" ".github" 2540 with
    | Ok None -> ()
    | other -> failwith $"an empty, cleanly-read connection is a measured none — got %A{other}"

[<Fact>]
let ``.github#2534 prClosingRef still reads a real closing reference`` () =
    let transport = serving (clean ClosingRefData)

    match Reads.prClosingRef transport "FS-GG" ".github" 2540 with
    | Ok(Some ref) ->
        Assert.Equal("FS-GG", ref.Owner)
        Assert.Equal(".github", ref.Repo)
        Assert.Equal(2534, ref.Number)
    | other -> failwith $"a clean 200 must still parse — got %A{other}"

[<Fact>]
let ``.github#2534 prClosingRef refuses a reference it cannot NAME`` () =
    // A node is PRESENT and unreadable. The connection reported a closing reference and we could not name
    // it — the opposite of "it closes nothing", so it may not borrow that answer's value.
    let transport =
        serving """{"data":{"repository":{"pullRequest":{"closingIssuesReferences":{"nodes":[{"number":null,"repository":{}}]}}}}}"""

    match Reads.prClosingRef transport "FS-GG" ".github" 2540 with
    | Error(Malformed(_, detail)) -> Assert.Contains("FAILED READ", detail)
    | other -> failwith $"an unnameable reference is a failed read — got %A{other}"

[<Fact>]
let ``.github#2534 contentEditProvenance keeps refusing a partial 200 through the shared helper`` () =
    // This read was ALREADY correct — `.github#2477` wrote the ordering into it by hand. The repair
    // hoisted that hand-written block into `graphQlData`, so this leg is the regression proof that the
    // hoist preserved it, including the non-object guard `.github#2418` put there.
    let transport = serving (partial ContentEditsData RateLimitMessage)

    match Reads.contentEditProvenance transport "FS-GG" ".github" 2534 with
    | Error(RateLimited(GraphQlBudget, None)) -> ()
    | other -> failwith $"an exhausted budget must survive the hoist as a rate limit — got %A{other}"

[<Fact>]
let ``.github#2534 the non-object guard survives the hoist into graphQlData`` () =
    // `[]`, `"text"`, `7`, `true` — valid JSON, not a GraphQL response of any shape. `TryGetProperty` on
    // these THROWS rather than answering false (`.github#2418`/PR #2419), and the throw is outside every
    // caller's `try`. Now guarded once, for every read in the module rather than the one that wrote it.
    for body in [ "[]"; "\"text\""; "7"; "true"; "null" ] do
        let transport = serving body

        match Reads.contentEditProvenance transport "FS-GG" ".github" 2534 with
        | Error(Malformed(_, detail)) -> Assert.Contains("FAILED READ", detail)
        | other -> failwith $"a non-object root is a failed read, never zero edits — %s{body} gave %A{other}"

        match Reads.subIssues transport "FS-GG" "FS.GG.SDD" 50 with
        | Error(Malformed _) -> ()
        | other -> failwith $"a non-object root must refuse in subIssues too — %s{body} gave %A{other}"

        match Reads.prClosingRef transport "FS-GG" ".github" 2540 with
        | Error(Malformed _) -> ()
        | other -> failwith $"a non-object root must refuse in prClosingRef too — %s{body} gave %A{other}"

[<Fact>]
let ``.github#2534 a response carrying neither data nor errors is a failed read`` () =
    let transport = serving """{"extensions":{"warnings":[]}}"""

    match Reads.subIssues transport "FS-GG" "FS.GG.SDD" 50 with
    | Error(Malformed(_, detail)) -> Assert.Contains("neither `data` nor `errors`", detail)
    | other -> failwith $"a response with no data and no errors is a failed read — got %A{other}"

// ---- 2. structure: the ordering is a build failure, not a comment -----------------------------------

/// THE SCANNER, kept apart from the corpus it scans so it can be pointed at a synthetic violation and
/// PROVED to fire. A gate that has only ever seen compliant input has not been shown to be a gate.
module private Ordering =

    /// One place a GraphQL response's root `data` is opened.
    type Site = { File: string; Line: int; Fn: string; Text: string }

    /// `.GetProperty("data")`, `.GetProperty "data"`, `.TryGetProperty "data"` — every spelling the layer
    /// actually uses, and the space form is not hypothetical: `Scan.fs` uses it.
    let private dataExtraction =
        Regex(@"(Try)?GetProperty\s*\(?\s*""data""", RegexOptions.Compiled)

    /// Either half of the ordering's first step: reading the `errors` array, or handing its messages to
    /// the classifier. `graphQlData` callers do neither — they do not open `data` either, so they are
    /// never sites at all.
    let private errorsInspection =
        Regex(@"(Try)?GetProperty\s*\(?\s*""errors""|ofGraphQlErrors", RegexOptions.Compiled)

    /// A TOP-LEVEL binding in a `module X =` — four spaces, then `let`. Nested `let`s (a `let rec page`
    /// inside a paging read, a lambda's binding) sit deeper and deliberately do NOT reset the enclosing
    /// function: the ordering is a property of the whole read, and `Scan.fs`'s paging loop checks
    /// `errors` in the outer scope of the extraction it guards.
    let private topLevelLet = Regex(@"^    let\s+(?:private\s+|rec\s+|inline\s+)*([^\s(:]+)", RegexOptions.Compiled)

    /// Every site in one file's text, paired with whether its enclosing binding asked about `errors`
    /// FIRST. Whole-line comments are skipped — the corpus is full of prose about this very rule, and
    /// counting a comment as either a violation or an absolution would make the gate about the wrong
    /// artefact. A TRAILING comment is deliberately not stripped: `//` inside a string literal cannot be
    /// told from a comment by a line scanner, and of the two errors, over-reporting a site is the one
    /// that fails closed.
    let scan (file: string) (text: string) : Site list * Site list =
        let lines = text.Replace("\r\n", "\n").Split('\n')
        let mutable fn = "(module level)"
        let mutable errorsSeenInFn = false
        let guarded = ResizeArray<Site>()
        let unguarded = ResizeArray<Site>()

        for i in 0 .. lines.Length - 1 do
            let line = lines[i]
            let trimmed = line.TrimStart()

            let m = topLevelLet.Match line

            if m.Success then
                fn <- m.Groups[1].Value
                errorsSeenInFn <- false

            if not (trimmed.StartsWith "//") then
                if errorsInspection.IsMatch line then
                    errorsSeenInFn <- true

                if dataExtraction.IsMatch line then
                    let site = { File = file; Line = i + 1; Fn = fn; Text = trimmed }
                    if errorsSeenInFn then guarded.Add site else unguarded.Add site

        List.ofSeq guarded, List.ofSeq unguarded

    let private rec_root (dir: DirectoryInfo) =
        let rec walk (d: DirectoryInfo) =
            if isNull (box d) then
                failwith "walked past the filesystem root without finding a .git — cannot locate the layer's sources"
            elif File.Exists(Path.Combine(d.FullName, ".git")) || Directory.Exists(Path.Combine(d.FullName, ".git")) then
                d.FullName
            else
                walk d.Parent

        walk dir

    /// The layer's own sources, on disk. A worktree, not a build output — the property being asserted is
    /// about what is WRITTEN.
    let layerSources () =
        let root = rec_root (DirectoryInfo(AppContext.BaseDirectory))
        let dir = Path.Combine(root, "src", "FS.GG.Coord.GitHub")

        Assert.True(Directory.Exists dir, $"the layer's source directory is not where this gate looks: {dir}")

        Directory.GetFiles(dir, "*.fs")
        |> Array.sort
        |> Array.map (fun path -> Path.GetFileName path, File.ReadAllText path)

    /// The ONE site in this layer that opens `data` without asking about `errors`, by design.
    ///
    /// `Budget.readMeter` is not a payload read: it lifts `data.rateLimit` off ANY 2xx GraphQL body —
    /// including the partial ones this whole gate exists to refuse — and answers `option`, where `None`
    /// means "no meter here" and asserts nothing about completeness. Reading the meter off a rate-limited
    /// response is the point of it. The exemption is NAMED rather than pattern-shaped, and
    /// `no exemption outlives its reason` below fails the moment it stops being needed.
    let exemptions = set [ "Budget.fs", "readMeter" ]

[<Fact>]
let ``.github#2534 the errors check precedes every data extraction in the GitHub layer`` () =
    // THIS IS AC3, AND IT IS THE HALF THAT OUTLIVES THE THREE REPAIRS. `Board.graphQlData` was correct
    // from the start; `Scan.fs`, `Done.fs` and `Reads.fs`'s content-edit read each re-derived it
    // correctly; and three reads still drifted, because a convention that nothing can fail is not
    // enforced. Adding a fourth read that opens `data` first now reds the build here.
    let offenders =
        Ordering.layerSources ()
        |> Array.collect (fun (file, text) -> Ordering.scan file text |> snd |> Array.ofList)
        |> Array.filter (fun site -> not (Ordering.exemptions.Contains(site.File, site.Fn)))

    Assert.True(
        offenders.Length = 0,
        "a GraphQL `data` extraction is not preceded by an `errors` inspection in its own function — a "
        + "partial HTTP 200 (how GitHub reports an exhausted budget) would be read as a complete answer "
        + "there. Route it through `graphQlData` (`Reads.fs`) or `Board.graphQlData`:\n"
        + (offenders
           |> Array.map (fun s -> $"  {s.File}:{s.Line} in `{s.Fn}` — {s.Text}")
           |> String.concat "\n")
    )

[<Fact>]
let ``.github#2534 the ordering gate is measuring a non-empty corpus`` () =
    // A GATE THAT SCANS NOTHING PASSES. Renaming `GetProperty`, moving the layer, or pointing the scan at
    // a build output would each empty the corpus silently and leave the assertion above vacuously green —
    // which is the failure mode a source-text gate has and a behavioural one does not. So the corpus's
    // own size is asserted, and the guarded sites are named: these are the re-derivations the repair
    // consolidated, and if they vanish this gate needs re-deriving too, not re-baselining.
    let guarded =
        Ordering.layerSources ()
        |> Array.collect (fun (file, text) -> Ordering.scan file text |> fst |> Array.ofList)

    Assert.True(
        guarded.Length >= 5,
        $"the ordering gate found only {guarded.Length} guarded `data` extractions — the corpus it scans has "
        + "moved or been renamed, and the gate above is passing on an empty set"
    )

    let files = guarded |> Array.map _.File |> Set.ofArray
    Assert.Contains("Reads.fs", files)
    Assert.Contains("Board.fs", files)
    Assert.Contains("Scan.fs", files)
    Assert.Contains("Done.fs", files)

[<Fact>]
let ``.github#2534 the ordering gate FIRES on a data extraction that skips the errors check`` () =
    // THE GATE'S OWN INVERSION, RUN RATHER THAN ASSERTED. The scanner is pointed at a synthetic read
    // shaped exactly like the three `.github#2534` repaired — `data` opened, `errors` never asked — and
    // must report it. Without this leg, "no offenders" is indistinguishable from "the scanner never
    // detects anything", and a gate that cannot fail is not a gate.
    let drifted =
        String.concat
            "\n"
            [ "module Reads ="
              "    let subIssues (transport: IGitHubTransport) ="
              "        match parse subject response.Body with"
              "        | Ok doc ->"
              "            let nodes = doc.RootElement.GetProperty(\"data\").GetProperty(\"repository\")"
              "            Ok nodes" ]

    let _, unguarded = Ordering.scan "Drifted.fs" drifted
    let site = Assert.Single unguarded
    Assert.Equal("subIssues", site.Fn)
    Assert.Equal(5, site.Line)

[<Fact>]
let ``.github#2534 the ordering gate ACCEPTS the errors-first shape, in both spellings`` () =
    // The counterpart: the same read with the check restored is NOT reported. A gate that flags
    // everything is as useless as one that flags nothing, and it would be repaired by weakening it.
    let repairedByInspection =
        String.concat
            "\n"
            [ "module Reads ="
              "    let subIssues (transport: IGitHubTransport) ="
              "        match doc.RootElement.TryGetProperty \"errors\" with"
              "        | true, errs -> Error(GraphQlErrors [])"
              "        | _ -> Ok(doc.RootElement.GetProperty(\"data\"))" ]

    // `Done.fs`'s spelling: the classifier call, without a literal `"errors"` on the same line.
    let repairedByClassifier =
        String.concat
            "\n"
            [ "module Reads ="
              "    let subIssues (transport: IGitHubTransport) ="
              "        match Budget.ofGraphQlErrors messages with"
              "        | Some limited -> Error limited"
              "        | None -> Ok(doc.RootElement.GetProperty \"data\")" ]

    for source in [ repairedByInspection; repairedByClassifier ] do
        let guarded, unguarded = Ordering.scan "Repaired.fs" source
        Assert.Empty unguarded
        Assert.Single guarded |> ignore

[<Fact>]
let ``.github#2534 a data extraction in a LATER function does not inherit an earlier one's errors check`` () =
    // THE SCANNER'S OWN SHARP EDGE. `errors` seen anywhere in the file would make the gate unfalsifiable
    // in exactly this layer, where every file already contains a correct read: the check is per enclosing
    // top-level binding, and crossing into the next one resets it.
    let twoReads =
        String.concat
            "\n"
            [ "module Reads ="
              "    let good (transport: IGitHubTransport) ="
              "        match doc.RootElement.TryGetProperty \"errors\" with"
              "        | _ -> Ok(doc.RootElement.GetProperty(\"data\"))"
              "    let drifted (transport: IGitHubTransport) ="
              "        Ok(doc.RootElement.GetProperty(\"data\"))" ]

    let guarded, unguarded = Ordering.scan "TwoReads.fs" twoReads
    Assert.Equal("good", (Assert.Single guarded).Fn)
    Assert.Equal("drifted", (Assert.Single unguarded).Fn)

[<Fact>]
let ``.github#2534 no exemption outlives its reason`` () =
    // AN ALLOWLIST IS A LIABILITY THE MOMENT IT STOPS BEING NEEDED — it silently absolves a site that has
    // since drifted back into scope, or a name that no longer exists. Every exemption must still BE an
    // unguarded site in the live sources; a stale one reds here rather than quietly widening the gate.
    let unguarded =
        Ordering.layerSources ()
        |> Array.collect (fun (file, text) -> Ordering.scan file text |> snd |> Array.ofList)
        |> Array.map (fun site -> site.File, site.Fn)
        |> Set.ofArray

    for exemption in Ordering.exemptions do
        Assert.True(
            unguarded.Contains exemption,
            $"the exemption %A{exemption} no longer names an unguarded `data` extraction — delete it "
            + "rather than leaving a hole in the gate"
        )
