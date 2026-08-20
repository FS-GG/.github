namespace FS.GG.Coord.Cli.Tests

open System
open System.IO
open System.Text.Json
open Xunit
open FS.GG.Coord
open FS.GG.Coord.Types
open FS.GG.Coord.Cli

/// `driver --events` CLI-layer coverage (.github#2135 repair round 1,
/// independent-review finding wrenlet-9f2c: the critic found the module (`DriverEvents`) fully
/// tested and the WIRING around it — fact assembly, cursor file I/O, JSON/text rendering,
/// `Client.fs:1343-1472` — completely untested. `driver`'s `--events` branch now delegates to four
/// NAMED functions (`Client.candidateToItemFacts`, `Client.readEventsCursor`,
/// `Client.writeEventsCursorAtomic`, `Client.renderEventsJson`) precisely so each is directly
/// testable here without a live board scan.
module DriverEventsCliTests =

    let private tempCursorPath () =
        Path.Combine(Path.GetTempPath(), $"fsgg-2135-cursor-{Guid.NewGuid():N}.json")

    // ---- readEventsCursor — repair round 1, finding 2 (corrupt vs absent) -------------------------

    [<Fact>]
    let ``readEventsCursor: no --cursor flag reads as an empty cursor`` () =
        Assert.Equal(Ok Map.empty, Client.readEventsCursor None)

    [<Fact>]
    let ``readEventsCursor: a --cursor path that has never been written reads as an empty cursor (legitimate first run)`` () =
        let path = tempCursorPath ()
        Assert.False(File.Exists path)
        Assert.Equal(Ok Map.empty, Client.readEventsCursor (Some path))

    [<Fact>]
    let ``readEventsCursor: a valid cursor file round-trips its exact states`` () =
        let path = tempCursorPath ()
        try
            File.WriteAllText(path, """{".github#1":"ready",".github#2":"claimed:snipe-f30c"}""")
            let result = Client.readEventsCursor (Some path)
            match result with
            | Ok cursor ->
                Assert.Equal(2, cursor.Count)
                Assert.Equal(DriverEvents.Ready, Map.find ".github#1" cursor)
                Assert.Equal(DriverEvents.Claimed "snipe-f30c", Map.find ".github#2" cursor)
            | Error message -> Assert.Fail $"expected a valid cursor to parse, got: {message}"
        finally
            File.Delete path

    [<Fact>]
    let ``readEventsCursor: a CORRUPT (truncated) cursor file is a REFUSED read, never a silent empty cursor`` () =
        // The exact independent-review round 1 finding 2 reproduced live: a process
        // killed mid-write leaves a truncated file. Before the fix, `readEventsCursor`'s predecessor
        // caught every parse exception and returned `Map.empty` — indistinguishable from a legitimate
        // first run. This is the gate-inversion evidence: this exact assertion fails against that old
        // shape (reproduced below, then reverted) and passes against the fix.
        let path = tempCursorPath ()
        try
            File.WriteAllText(path, """{".github#1":"ready", "truncated""")
            match Client.readEventsCursor (Some path) with
            | Error message -> Assert.Contains(path, message)
            | Ok cursor -> Assert.Fail $"expected the corrupt cursor to be REFUSED, got a silent Ok {cursor}"
        finally
            File.Delete path

    [<Fact>]
    let ``readEventsCursor: a cursor file with an unrecognized state encoding is REFUSED, not silently dropped`` () =
        let path = tempCursorPath ()
        try
            File.WriteAllText(path, """{".github#1":"some-future-schema:x"}""")
            match Client.readEventsCursor (Some path) with
            | Error message -> Assert.Contains(".github#1", message)
            | Ok cursor -> Assert.Fail $"expected the unrecognized encoding to be REFUSED, got a silent Ok {cursor}"
        finally
            File.Delete path

    [<Fact>]
    let ``readEventsCursor: a DIRECTORY at the cursor path is a REFUSED read, never a silent empty cursor (repair round 2)`` () =
        // Independent-review round 2 (wrenlet-9f2c): `File.Exists` returns FALSE for a
        // directory as well as for a genuinely missing path, so the round-1 fix's existence check
        // silently classified a directory as "never written" — the exact absent-vs-corrupt confusion
        // repair round 1 closed everywhere else, surviving in the one case that does not look like a
        // file. This is the gate-inversion evidence: this assertion fails against the pre-fix
        // `not (File.Exists path)` shape (reproduced below, then reverted) and passes against the fix.
        let path = tempCursorPath ()
        Directory.CreateDirectory path |> ignore
        try
            match Client.readEventsCursor (Some path) with
            | Error message -> Assert.Contains(path, message)
            | Ok cursor -> Assert.Fail $"expected a directory at the cursor path to be REFUSED, got a silent Ok {cursor}"
        finally
            Directory.Delete path

    [<Fact>]
    let ``readEventsCursor: a directory at the cursor path never reaches writeEventsCursorAtomic, so no temp file is left behind (repair round 2)`` () =
        // The critic's second, more serious consequence: control used to fall through past the
        // silent `Ok Map.empty` into `writeEventsCursorAtomic`, whose `File.Move` cannot rename onto
        // an existing directory and threw uncaught — leaking a `.tmp-<guid>` sibling on the crash.
        // Confirming the fix means confirming BOTH halves: the read is refused (above), AND the write
        // path is consequently never reached, so the directory's contents stay exactly empty.
        let path = tempCursorPath ()
        Directory.CreateDirectory path |> ignore
        try
            match Client.readEventsCursor (Some path) with
            | Error _ -> ()
            | Ok cursor -> Client.writeEventsCursorAtomic path cursor
            Assert.Empty(Directory.GetFileSystemEntries path)
        finally
            Directory.Delete path

    // ---- writeEventsCursorAtomic — repair round 1, finding 2's second half ------------------------

    [<Fact>]
    let ``writeEventsCursorAtomic: writes a cursor readEventsCursor reads back exactly`` () =
        let path = tempCursorPath ()
        try
            let cursor =
                Map.ofList
                    [ ".github#1", DriverEvents.Ready
                      ".github#2", DriverEvents.ReviewHandoff(Some "critic-1")
                      ".github#3", DriverEvents.Unreadable "board scan timed out" ]

            Client.writeEventsCursorAtomic path cursor
            Assert.Equal(Ok cursor, Client.readEventsCursor (Some path))
        finally
            File.Delete path

    [<Fact>]
    let ``writeEventsCursorAtomic: leaves no leftover temp file beside the target`` () =
        let path = tempCursorPath ()
        try
            Client.writeEventsCursorAtomic path (Map.ofList [ ".github#1", DriverEvents.Ready ])
            let directory = Path.GetDirectoryName path
            let leftovers = Directory.GetFiles(directory, $"{Path.GetFileName path}.tmp-*")
            Assert.Empty(leftovers)
        finally
            File.Delete path

    [<Fact>]
    let ``writeEventsCursorAtomic: overwrites an existing cursor rather than merging with it`` () =
        let path = tempCursorPath ()
        try
            Client.writeEventsCursorAtomic path (Map.ofList [ ".github#1", DriverEvents.Ready ])
            Client.writeEventsCursorAtomic path (Map.ofList [ ".github#2", DriverEvents.Claimed "w" ])
            match Client.readEventsCursor (Some path) with
            | Ok cursor ->
                Assert.False(cursor.ContainsKey ".github#1")
                Assert.Equal(DriverEvents.Claimed "w", Map.find ".github#2" cursor)
            | Error message -> Assert.Fail message
        finally
            File.Delete path

    // ---- renderEventsJson — "JSON/text rendering" coverage -----------------------------------------

    [<Fact>]
    let ``renderEventsJson: emits the fsgg.coord.driver-events/1 schema with transitions and active items`` () =
        let projection: DriverEvents.Projection =
            { Transitions =
                [ { Ref = ".github#1"
                    Previous = None
                    New = DriverEvents.Claimed "snipe-f30c"
                    Reason = "claim marker live; no review evidence yet"
                    Evidence = "claim:worker=snipe-f30c"
                    ObservedAt = 100L
                    SourceSha = "sha-1" } ]
              Active =
                [ { Ref = ".github#1"
                    State = DriverEvents.Claimed "snipe-f30c"
                    Reason = "claim marker live; no review evidence yet"
                    Evidence = "claim:worker=snipe-f30c"
                    ObservedAt = 100L
                    SourceSha = "sha-1" } ]
              Unreadable = []
              Cursor = Map.ofList [ ".github#1", DriverEvents.Claimed "snipe-f30c" ]
              RenderedAt = 100L }

        let json = Client.renderEventsJson "sha-1" projection
        use document = JsonDocument.Parse json
        let root = document.RootElement
        Assert.Equal("fsgg.coord.driver-events/1", root.GetProperty("schema").GetString())
        Assert.Equal("sha-1", root.GetProperty("sourceSha").GetString())
        let transitions = root.GetProperty("transitions")
        Assert.Equal(1, transitions.GetArrayLength())
        Assert.Equal(".github#1", transitions.[0].GetProperty("ref").GetString())
        Assert.Equal("claimed:snipe-f30c", transitions.[0].GetProperty("state").GetString())
        Assert.Equal(JsonValueKind.Null, transitions.[0].GetProperty("previous").ValueKind)
        let active = root.GetProperty("active")
        Assert.Equal(1, active.GetArrayLength())
        Assert.Equal(".github#1", active.[0].GetProperty("ref").GetString())

    [<Fact>]
    let ``renderEventsJson: a persistent Unreadable transition is present in BOTH successive renders (repair round 1, finding 1, end to end)`` () =
        // Reproduces the critic's finding through the ACTUAL rendering function the CLI calls, not
        // just through `DriverEvents.project` in isolation: two successive projections over an
        // unchanged Unreadable fact must both render a non-empty `transitions` array.
        let facts: DriverEvents.ItemFacts =
            { Ref = ".github#9"
              ReadOk = false
              UnreadableReason = Some "PR read timed out"
              BoardStatus = None
              IssueState = None
              ClaimWorker = None
              HumanBlock = None
              Pr = None
              Review = None
              Merged = false
              ObligationsDeclared = false
              Obligations = []
              Evidence = "pr:9"
              ObservedAt = 100L
              SourceSha = "sha-1" }

        let first = DriverEvents.project Map.empty [ facts ] 100L
        let firstJson = Client.renderEventsJson "sha-1" first
        use firstDoc = JsonDocument.Parse firstJson
        Assert.Equal(1, firstDoc.RootElement.GetProperty("transitions").GetArrayLength())

        let second = DriverEvents.project first.Cursor [ { facts with ObservedAt = 200L } ] 200L
        let secondJson = Client.renderEventsJson "sha-1" second
        use secondDoc = JsonDocument.Parse secondJson
        Assert.Equal(1, secondDoc.RootElement.GetProperty("transitions").GetArrayLength())
        Assert.Contains(".github#9", secondJson)

    [<Fact>]
    let ``renderEventsJson: an empty projection renders empty transitions/active arrays, not an omitted field`` () =
        let projection: DriverEvents.Projection =
            { Transitions = []; Active = []; Unreadable = []; Cursor = Map.empty; RenderedAt = 0L }

        let json = Client.renderEventsJson "sha-1" projection
        use document = JsonDocument.Parse json
        Assert.Equal(0, document.RootElement.GetProperty("transitions").GetArrayLength())
        Assert.Equal(0, document.RootElement.GetProperty("active").GetArrayLength())

    // ---- candidateToItemFacts — "fact assembly" coverage --------------------------------------------

    let private baseCandidate : Snapshot.Candidate =
        { Item =
            { Ref = { Owner = "FS-GG"; Repo = ".github"; Number = 1 }
              PathRepo = ".github"
              Status = Ready
              State = Open
              TouchSet = Undeclared
              Blockers = []
              Claim = None
              ItemPr = None
              ItemPrUnreadable = false
              HumanBlock = None
              Predicate = None
              Class = None
              Kind = None
              BoardKind = None
              CommentCount = None
              BoardClass = None
              DeliveryRoute = DeliveryRoute.Stale [ "fixture" ]
              Severity = Unset
              Phase = None
              AgeDays = None }
          BashPaths = None
          DeclaredPredicate = None }

    [<Fact>]
    let ``candidateToItemFacts: an unclaimed Ready candidate assembles into a Ready ItemFacts`` () =
        let facts = Client.candidateToItemFacts Map.empty Map.empty 100L "sha-1" baseCandidate
        Assert.Equal("FS-GG/.github#1", facts.Ref)
        Assert.True facts.ReadOk
        Assert.Equal(None, facts.ClaimWorker)
        Assert.Equal(DriverEvents.Ready, (DriverEvents.classify facts).State)

    [<Fact>]
    let ``candidateToItemFacts: ItemPrUnreadable becomes ReadOk = false with a named reason`` () =
        let candidate = { baseCandidate with Item = { baseCandidate.Item with ItemPrUnreadable = true } }
        let facts = Client.candidateToItemFacts Map.empty Map.empty 100L "sha-1" candidate
        Assert.False facts.ReadOk
        Assert.Equal(Some "the markerless item-PR probe was unreadable", facts.UnreadableReason)
        match (DriverEvents.classify facts).State with
        | DriverEvents.Unreadable _ -> ()
        | other -> Assert.Fail $"expected Unreadable, got %A{other}"

    [<Fact>]
    let ``candidateToItemFacts: a live claim carries the worker id through to ItemFacts`` () =
        let claim: Claim = { Worker = WorkerId "snipe-f30c"; Session = None; AgeSeconds = 10; PreviousStatus = None }
        let candidate = { baseCandidate with Item = { baseCandidate.Item with Claim = Some(claim, LeaseHeld) } }
        let facts = Client.candidateToItemFacts Map.empty Map.empty 100L "sha-1" candidate
        Assert.Equal(Some "snipe-f30c", facts.ClaimWorker)
        Assert.Equal(DriverEvents.Claimed "snipe-f30c", (DriverEvents.classify facts).State)

    [<Fact>]
    let ``candidateToItemFacts: review evidence keyed by the candidate's ItemPr is attached`` () =
        let review: Driver.ReviewChain =
            { MarkerValid = true; Subject = None; ClaimGeneration = None; BaseSha = None
              CriticIdentity = Some "critic-1"; HeadSha = Some "head-1"
              Rounds = []; RepairPhase = false; ChecksGreen = false; HostAccepted = false
              RuntimeRouteEvidence = None; DiffAuditRequired = false; DiffAuditHead = None }

        let claim: Claim = { Worker = WorkerId "snipe-f30c"; Session = None; AgeSeconds = 10; PreviousStatus = None }
        let candidate =
            { baseCandidate with
                Item = { baseCandidate.Item with Claim = Some(claim, LeaseHeld); ItemPr = Some 42 } }

        let facts = Client.candidateToItemFacts (Map.ofList [ 42, review ]) Map.empty 100L "sha-1" candidate
        Assert.Equal(Some review, facts.Review)
        Assert.Equal(DriverEvents.ReviewHandoff(Some "critic-1"), (DriverEvents.classify facts).State)

    [<Fact>]
    let ``candidateToItemFacts: merged-obligations facts are keyed by canonical ref`` () =
        let closedCandidate = { baseCandidate with Item = { baseCandidate.Item with State = Closed } }
        let obligations = [ ({ Id = "kit-release"; Kind = "release"; Evidence = None; HeadSha = "h1"; Verified = false }: Delivery.Obligation) ]
        let mergedFactsByRef = Map.ofList [ "FS-GG/.github#1", (42, true, obligations) ]
        let facts = Client.candidateToItemFacts Map.empty mergedFactsByRef 100L "sha-1" closedCandidate
        Assert.Equal(true, facts.Merged)
        Assert.Equal(Some 42, facts.Pr)
        Assert.Equal(DriverEvents.MergedAwaitingObligations 42, (DriverEvents.classify facts).State)

    // ---- .github#2525 — the machine projection carries the same completeness fact -----------------

    [<Fact>]
    let ``.github#2525: renderEventsJson reports activeComplete=false and the unreadable rows when a read fell short`` () =
        // A consumer reading only `active: []` cannot tell a measured-empty inventory from one this read
        // never finished — the same collapse the text renderer carried, one surface over. `activeComplete`
        // is the single boolean a driver branches on.
        let unreadable: DriverEvents.Classified =
            { Ref = "FS-GG/.github#2512"
              State = DriverEvents.Unreadable "absent from the current facts batch"
              Reason = "missing from this read: previously active, absent from the current facts batch"
              Evidence = "cursor-only; absent from current read"
              ObservedAt = 100L
              SourceSha = "sha-1" }

        let projection: DriverEvents.Projection =
            { Transitions = []
              Active = []
              Unreadable = [ unreadable ]
              Cursor = Map.empty
              RenderedAt = 100L }

        let json = Client.renderEventsJson "sha-1" projection
        use document = JsonDocument.Parse json

        Assert.False(document.RootElement.GetProperty("activeComplete").GetBoolean())
        Assert.Equal(0, document.RootElement.GetProperty("active").GetArrayLength())
        Assert.Equal(1, document.RootElement.GetProperty("unreadable").GetArrayLength())

        Assert.Equal(
            "FS-GG/.github#2512",
            document.RootElement.GetProperty("unreadable").[0].GetProperty("ref").GetString()
        )

    [<Fact>]
    let ``.github#2525 acceptance #4: a complete read reports activeComplete=true with an empty unreadable array`` () =
        // The controlled counterpart on the JSON surface. `unreadable` must be present and empty rather
        // than omitted, for the same reason `active` is (.github#2135) — an absent key is not a fact.
        let projection: DriverEvents.Projection =
            { Transitions = []; Active = []; Unreadable = []; Cursor = Map.empty; RenderedAt = 0L }

        let json = Client.renderEventsJson "sha-1" projection
        use document = JsonDocument.Parse json

        Assert.True(document.RootElement.GetProperty("activeComplete").GetBoolean())
        Assert.Equal(0, document.RootElement.GetProperty("unreadable").GetArrayLength())
