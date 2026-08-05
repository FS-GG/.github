module FS.GG.Coord.Cli.Tests.ScanRoundTripTests

open Xunit
open FS.GG.Coord
open FS.GG.Coord.Types
open FS.GG.Coord.Cli
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Transport

/// THE ROUND TRIP IS THE WHOLE CONTRACT, AND IT IS THE ONE THING NEITHER SIDE CAN CHECK ALONE.
///
/// `Scan.snapshot` WRITES `fsgg.coord.snapshot/1`. `Snapshot.parse` READS it. They live in different
/// projects, they were written months apart, and until this file existed nothing anywhere asserted they
/// agreed about the format.
///
/// A writer and a parser that disagree do not fail loudly. `Snapshot.parse` refuses a malformed document —
/// which is right — so the failure mode is that `scan | decide` returns a parse error on every single run,
/// forever, and the engine looks broken rather than the codec. That is a bad half-hour, and it is entirely
/// avoidable: assert the two halves against each other.
///
/// Every test here drives the REAL writer against a fake transport and feeds its bytes to the REAL parser.
/// Nothing is hand-written; a fixture snapshot would only prove the parser agrees with the fixture.
let private ok (body: string) =
    Ok
        { Status = 200
          Body = body
          ETag = None
          NextLink = None }

let private aRow: Scan.Row =
    { Ref =
        { Owner = "FS-GG"
          Repo = "FS.GG.SDD"
          Number = 42 }
      Title = "a real item"
      Status = Ready
      BlockedByRaw = ""
      State = Open
      IsPullRequest = false
      PathRepo = "FS.GG.SDD"
      BoardClass = None
      Severity = Unset
      Phase = None
      CreatedAt = None }

/// A transport that answers by ENDPOINT, so one fake can serve a body read and a marker read differently —
/// which is what the snapshot assembler actually does. The off-board open-issue scan (case 25) rides on the
/// bare `/issues` list; these worlds have no off-board claim, so it answers empty — the honest scan result.
let private routed (body: string) (comments: string) =
    Fake.Recorder(fun req ->
        if req.Path.EndsWith "/issues" then
            ok "[]"
        elif req.Path.EndsWith "/comments" then
            ok comments
        else
            ok body)

let private issueBody (text: string) =
    let escaped = text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n")
    $"""{{"number":42,"body":"%s{escaped}"}}"""

// ---- the round trip ---------------------------------------------------------------------------------

[<Fact>]
let ``a scanned snapshot PARSES - the writer and the reader agree about the format`` () =
    let transport = routed (issueBody "Paths: src/Audio/**") "[]"

    match Scan.snapshot transport [ aRow ] None false None 120 with
    | Error e -> failwith $"the scan must produce a snapshot — got %A{e}"
    | Ok(document, _) ->

    match Snapshot.parse document with
    | Error errors ->
        let detail =
            errors |> List.map (fun e -> $"{e.Path}: {e.Message}") |> String.concat "; "

        failwith $"the snapshot the ENGINE writes must parse with the parser the ENGINE reads: %s{detail}"

    | Ok request ->
        Assert.Equal(1, List.length request.Candidates)
        Assert.Equal(120, request.LeaseMinutes)

        let item = request.Candidates.[0].Item
        Assert.Equal("FS.GG.SDD#42", item.Ref.Short)
        Assert.Equal(Ready, item.Status)
        Assert.Equal(Open, item.State)

[<Fact>]
let ``the touch-set survives the round trip, parsed by the ENGINE's own grammar`` () =
    // The RAW BODY is what travels, not bash's parse of it. That is deliberate: the touch-set grammar is its
    // own family of incidents (#273, #277, #435, #496), and a snapshot that carried somebody else's parse
    // would compare two schedulers over one parser and call the parser proven.
    let transport = routed (issueBody "Some prose\n\nPaths: src/Audio/** tests/Audio.fs") "[]"

    match Scan.snapshot transport [ aRow ] None false None 120 with
    | Ok(document, _) ->
        match Snapshot.parse document with
        | Ok request ->
            match request.Candidates.[0].Item.TouchSet with
            | Declared tokens ->
                let names =
                    tokens
                    |> List.map (function
                        | Matchable s -> s
                        | Unmatchable s -> s)

                Assert.Equal<string list>([ "src/Audio/**"; "tests/Audio.fs" ], names)
            | other -> failwith $"the touch-set must survive — got %A{other}"
        | Error e -> failwith $"parse failed: %A{e}"
    | Error e -> failwith $"scan failed: %A{e}"

[<Fact>]
let ``a scanned item is SCHEDULABLE end to end - scan, parse, decide`` () =
    // THE WHOLE POINT, IN ONE TEST. `fsgg-coord-engine scan | fsgg-coord-engine decide` — a complete
    // scheduling pass with no bash anywhere in it, which is the thing ADR-0034 said the IO layer was FOR and
    // the reason bash could not be deleted without it.
    let transport = routed (issueBody "Paths: src/Audio/**") "[]"

    match Scan.snapshot transport [ aRow ] None false None 120 with
    | Ok(document, _) ->
        match Snapshot.parse document with
        | Ok request ->
            let decision =
                Batch.schedule
                    request.AllowBacklog
                    request.Limit
                    request.InFlight
                    (request.Candidates |> List.map (fun c -> c.Item))

            match decision with
            | Green result ->
                Assert.Equal(1, List.length result.Chosen)
                Assert.Equal("FS.GG.SDD#42", result.Chosen.[0].Ref.Short)
            | other -> failwith $"a Ready item with a good touch-set and no blockers is startable — got %A{other}"
        | Error e -> failwith $"parse failed: %A{e}"
    | Error e -> failwith $"scan failed: %A{e}"

// ---- the failure legs, which are the ones that matter -----------------------------------------------

[<Fact>]
let ``#461 a marker read that FAILS aborts the scan - it never yields an unclaimed item`` () =
    // Guessing the lock state from a failed read is the one thing a lock may never do. An empty marker list
    // would say "nobody holds this", the item would be offered, and a second worker would be handed files
    // somebody is standing in.
    //
    // So this is the ONE read in the whole snapshot that is FATAL. A body we cannot read yields an item we
    // can still reason about (`bodyUnreadable` → Undetermined); a LOCK we cannot read yields nothing at all.
    let transport =
        Fake.Recorder(fun req ->
            if req.Path.EndsWith "/comments" then
                Error(Errors.Malformed("FS.GG.SDD#42", "not JSON"))
            else
                ok (issueBody "Paths: src/**"))

    match Scan.snapshot transport [ aRow ] None false None 120 with
    | Error(Errors.Malformed _) -> ()
    | Ok _ -> failwith "an unreadable lock produced a snapshot — this is #461, and it double-books the item"
    | other -> failwith $"expected the scan to refuse — got %A{other}"

[<Fact>]
let ``an unreadable BODY does not drop the item - it arrives UNREADABLE, and is UNDETERMINED`` () =
    // The client used to WITHHOLD such items entirely, which is worse than it sounds: an item bash had
    // classified BLOCKED simply VANISHED from the engine's world, so it could not be offered AND could not
    // even be passed over with a reason. A worker asking "why is there nothing to do?" got silence about it.
    //
    // Say what is true: UNREADABLE. Not `Undeclared` — that is a confident OMISSION about an item nobody
    // looked at (#496).
    let transport =
        Fake.Recorder(fun req ->
            if req.Path.EndsWith "/issues" then
                ok "[]" // the off-board scan finds no claim here; only the candidate BODY is the 502
            elif req.Path.EndsWith "/comments" then
                ok "[]"
            else
                Error(Errors.Http(502, "bad gateway")))

    match Scan.snapshot transport [ aRow ] None false None 120 with
    | Ok(document, receipt) ->
        Assert.Equal(1, receipt.BodiesUnreadable)

        match Snapshot.parse document with
        | Ok request ->
            match request.Candidates.[0].Item.TouchSet with
            | Unreadable _ -> ()
            | Undeclared -> failwith "an unread body became a confident 'no touch-set declared' — that is #496"
            | other -> failwith $"expected Unreadable — got %A{other}"

            // AND IT IS UNDETERMINED, NEVER SILENTLY SKIPPED.
            let verdict =
                Schedulability.schedulable false [] request.Candidates.[0].Item

            match verdict with
            | Schedulability.Undetermined _ -> ()
            | other -> failwith $"an item whose touch-set we could not read is UNDETERMINED — got %A{other}"

        | Error e -> failwith $"parse failed: %A{e}"
    | Error e -> failwith $"the scan must survive one unreadable body — got %A{e}"

[<Fact>]
let ``#641 a PULL REQUEST on the board is not a candidate`` () =
    let pr = { aRow with IsPullRequest = true }
    let transport = routed (issueBody "Paths: src/**") "[]"

    match Scan.snapshot transport [ pr ] None false None 120 with
    | Ok(_, receipt) -> Assert.Equal(0, receipt.Candidates)
    | Error e -> failwith $"scan failed: %A{e}"

[<Fact>]
let ``#520 a CLOSED and STAMPED issue is a candidate, SWEPT with no read, and decided IssueClosed`` () =
    // One was handed to a worker two hours after it was closed as completed, because candidate selection
    // read the board COLUMN and nothing else — and then it was PROMOTED back to Ready on release, re-arming
    // it for the next worker. The fix is NOT to drop it from the candidates: that gets the right answer
    // (never scheduled) with no WORDS, so a worker asking "why isn't #502 offered?" gets nothing for it,
    // where bash names it "the issue is closed". So it stays a candidate and is SWEPT — `Schedulability`
    // answers `Closed -> IssueClosed` as its FIRST question, before the touch-set or the lock, so the sweep
    // needs neither a body read nor a marker read, and the reason survives to `decide`.
    //
    // THE FIXTURE IS `Done`, NOT `Ready`, SINCE .github#2225. This test used to drive `State = Closed;
    // Status = Ready` — a CLOSED, UNSTAMPED row, which is now precisely the post-merge window that MUST be
    // read. Pinning the sweep on that fixture is what let the sweep grow over the whole window unnoticed:
    // the test agreed with the code, and both were describing a rule nobody had checked against `delivery`.
    // The sweep is real and worth keeping — it is just gated on the STAMP now, which is what `Done` says.
    // Its unstamped twin is the test immediately below.
    let closed = { aRow with State = Closed; Status = Done }

    // A transport that FAILS on every BODY and MARKER read — so a green here is proof the closed sweep read
    // no body and no lock. The off-board open-issue scan (case 25) still runs (a lock lives off the board),
    // and it finds none: the bare `/issues` list answers empty, which is the one endpoint allowed to succeed.
    let transport =
        Fake.Recorder(fun req ->
            if req.Path.EndsWith "/issues" then
                ok "[]"
            else
                Error(Errors.Transport "the closed sweep must read no body and no marker"))

    match Scan.snapshot transport [ closed ] None false None 120 with
    | Error e -> failwith $"a swept closed item reads nothing, so the scan cannot fail — got %A{e}"
    | Ok(document, receipt) ->
        Assert.Equal(1, receipt.Candidates) // it IS a candidate now — that is what carries the reason
        Assert.Equal(0, receipt.BodiesUnreadable)

        match Snapshot.parse document with
        | Error errors ->
            let detail =
                errors |> List.map (fun e -> $"{e.Path}: {e.Message}") |> String.concat "; "

            failwith $"a swept closed item, which carries no body, must still parse: %s{detail}"
        | Ok request ->
            let item = request.Candidates.[0].Item
            Assert.Equal(Closed, item.State)

            match Schedulability.schedulable false [] item with
            | Schedulability.IssueClosed -> ()
            | other -> failwith $"a closed issue is IssueClosed — the reason a worker reads — got %A{other}"

[<Fact>]
let ``#2225 a CLOSED but UNSTAMPED item is READ, and its live claim still RESERVES its touch-set`` () =
    // THE POST-MERGE WINDOW. Closing is the MIDDLE of an item in this protocol, not its end: the merge that
    // closes the issue is followed by publication, receipts and registry records, and only then a done stamp.
    // The sweep above used to cover that whole window on `Closed` alone, so the item's body AND its markers
    // went unread — and one sweep then failed in three registers: `who` answered EMPTY for a live claim,
    // `batch` reserved NOTHING (a second worker could be handed the holder's tree — #1858's class), and
    // `delivery` reported the reader's blindness as the ITEM's own incomplete `Paths:` declaration.
    //
    // The transport here is the INVERSE of the sweep test's: body and markers MUST be read, and the fixture
    // supplies both. If the sweep ever widens back over the stamp, the claim below stops reserving and this
    // test goes red on `InFlight` — the assertion the three registers all reduce to.
    let now = System.DateTimeOffset.UtcNow.ToString("o")

    let marker =
        $"""[{{"id":902,"body":"<!-- fsgg:claim worker=curlew-8afd lease=120 prev=In%%20review -->","updated_at":"%s{now}"}}]"""

    // `In review` + CLOSED: the exact shape of an item whose PR has merged while its worker still owes
    // obligations. Deliberately NOT `In progress` — that column is the one arm `who` already covered, and a
    // fixture using it would pass without the fix.
    let closedUnstamped =
        { aRow with
            State = Closed
            Status = InReview }

    let transport = routed (issueBody "Paths: src/Audio/**") marker

    match Scan.snapshot transport [ closedUnstamped ] None false None 120 with
    | Error e -> failwith $"a closed, unstamped item must be READ, not swept — got %A{e}"
    | Ok(document, receipt) ->
        Assert.Equal(1, receipt.Candidates)
        Assert.Equal(0, receipt.BodiesUnreadable)

        match Snapshot.parse document with
        | Error errors ->
            let detail =
                errors |> List.map (fun e -> $"{e.Path}: {e.Message}") |> String.concat "; "

            failwith $"the closed, unstamped row must round-trip: %s{detail}"
        | Ok request ->
            let item = request.Candidates.[0].Item
            Assert.Equal(Closed, item.State)

            // THE TOUCH-SET IS READ, not invented. `Undeclared` here is the #2225 defect exactly: the body
            // was never fetched and the engine reported a confident omission about an item nobody looked at.
            match item.TouchSet with
            | Declared [ Matchable "src/Audio/**" ] -> ()
            | Undeclared ->
                failwith "the closed body went unread and became a confident 'no touch-set declared' — that is .github#2225"
            | other -> failwith $"expected the declared touch-set — got %A{other}"

            // THE CLAIM SURVIVES, so `who` can see it.
            match item.Claim with
            | Some(claim, LeaseHeld) -> Assert.Equal(WorkerId "curlew-8afd", claim.Worker)
            | other -> failwith $"a live claim on a closed item must survive — got %A{other}"

            // AND IT RESERVES, so `batch` cannot hand a second worker the holder's tree.
            match request.InFlight with
            | [ r ] ->
                match r.Holder with
                | Batch.LiveClaim(worker, _, _, _) -> Assert.Equal(WorkerId "curlew-8afd", worker)
                | other -> failwith $"a live claim on a closed item reserves as a LiveClaim — got %A{other}"
            | other -> failwith $"the post-merge window must reserve exactly one touch-set — got %A{other}"

// ---- blockers ---------------------------------------------------------------------------------------

[<Fact>]
let ``a blocker pointing at ANOTHER BOARD ITEM is resolved for FREE - zero extra reads`` () =
    // The scan already carries every board item's state, so this costs nothing. That is why blocker-awareness
    // adds ZERO GraphQL, and it is the reason the thrifty scan is worth what it is.
    let blocked =
        { aRow with
            Ref = { aRow.Ref with Number = 43 }
            BlockedByRaw = "FS.GG.SDD#42" }

    let transport = routed (issueBody "Paths: src/**") "[]"

    match Scan.snapshot transport [ aRow; blocked ] None false None 120 with
    | Ok(document, receipt) ->
        // NOT ONE off-board read: the blocker was on the board, and the scan had already seen it.
        Assert.Equal(0, receipt.OffBoardResolved)

        match Snapshot.parse document with
        | Ok request ->
            let item = request.Candidates |> List.find (fun c -> c.Item.Ref.Number = 43)

            match item.Item.Blockers with
            | [ b ] ->
                Assert.Equal(BlockerOpen, b.State)
                Assert.Equal(Some 42, b.Ref |> Option.map (fun r -> r.Number))
            | other -> failwith $"the blocker must survive — got %A{other}"
        | Error e -> failwith $"parse failed: %A{e}"
    | Error e -> failwith $"scan failed: %A{e}"

[<Fact>]
let ``PROSE in a Blocked-by field is UNPARSEABLE - and it BLOCKS`` () =
    // "Blocked by RESOLVED: shipped last week" has no owner, no repo and no number. The bash client silently
    // DROPPED such blockers — so an item it called BLOCKED arrived at the engine UNBLOCKED, and the engine's
    // answer is the one a worker acts on: blocked work, handed out.
    let blocked =
        { aRow with
            Ref = { aRow.Ref with Number = 43 }
            BlockedByRaw = "RESOLVED: shipped last week" }

    let transport = routed (issueBody "Paths: src/**") "[]"

    match Scan.snapshot transport [ blocked ] None false None 120 with
    | Ok(document, _) ->
        match Snapshot.parse document with
        | Ok request ->
            match request.Candidates.[0].Item.Blockers with
            | [ b ] ->
                Assert.Equal(BlockerUnparseable, b.State)
                Assert.True(b.Ref.IsNone)

                // AND IT STILL BLOCKS. That is the whole point.
                Assert.False(Blockers.isResolved b)
            | other -> failwith $"prose in a dependency field is a blocker — got %A{other}"
        | Error e -> failwith $"parse failed: %A{e}"
    | Error e -> failwith $"scan failed: %A{e}"

// ---- the claim ---------------------------------------------------------------------------------------

[<Fact>]
let ``a live claim survives the round trip, and RESERVES its touch-set`` () =
    let now = System.DateTimeOffset.UtcNow.ToString("o")

    let marker =
        $"""[{{"id":901,"body":"<!-- fsgg:claim worker=vole-418 lease=120 prev=Ready -->","updated_at":"%s{now}"}}]"""

    let transport = routed (issueBody "Paths: src/Audio/**") marker

    match Scan.snapshot transport [ aRow ] None false None 120 with
    | Ok(document, _) ->
        match Snapshot.parse document with
        | Ok request ->
            match request.Candidates.[0].Item.Claim with
            | Some(claim, LeaseHeld) ->
                Assert.Equal(WorkerId "vole-418", claim.Worker)
                Assert.Equal(Some Ready, claim.PreviousStatus)
            | other -> failwith $"the live claim must survive — got %A{other}"

            // A LIVE CLAIM RESERVES ITS TOUCH-SET. That reservation is what stops a second worker being
            // handed the same files, and it comes from the body we already read — one read, two uses.
            match request.InFlight with
            | [ r ] ->
                match r.Holder with
                // #712: a claim WITHIN its lease carries NO proof of life — `livePr = None`. `None` is the
                // ordinary case, never a phantom PR, and the field is not even written (byte-identical
                // round trip), so nothing here could mistake a healthy claim for a PR-kept-alive one.
                | Batch.LiveClaim(_, _, _, livePr) -> Assert.Equal(None, livePr)
                | other -> failwith $"a live claim reserves as a LiveClaim — got %A{other}"
            | other -> failwith $"exactly one reservation expected — got %A{other}"
        | Error e -> failwith $"parse failed: %A{e}"
    | Error e -> failwith $"scan failed: %A{e}"

[<Fact>]
let ``an OFF-BOARD claim reserves its touch-set - the board scan misses it, the off-board scan catches it`` () =
    // #461/#581, case 25. A lock lives OFF the board: a marker on an issue whose column flip failed, or one
    // the board never listed. The board scan reads only board rows, so a candidate declaring the same files
    // would be handed a tree its holder is standing in — the exact double-book the scheduler exists to
    // prevent. The off-board open-issue scan (bash's `active_claims` arm B) reserves it. Here the board
    // candidate #42 declares `src/Audio/Sub`, a SUBTREE of the OFF-BOARD #99's `src/Audio`, so #42 is refused.
    let now = System.DateTimeOffset.UtcNow.ToString("o")

    let marker99 =
        $"""[{{"id":701,"body":"<!-- fsgg:claim worker=puffin-h11 lease=120 -->","updated_at":"%s{now}"}}]"""

    let transport =
        Fake.Recorder(fun req ->
            if req.Path.EndsWith "/issues" then
                // the off-board list — #99 is the claim the board never listed; its body rides along.
                ok """[{"number":99,"title":"off-board","state":"open","body":"Paths: src/Audio"}]"""
            elif req.Path.EndsWith "/99/comments" then
                ok marker99
            elif req.Path.EndsWith "/comments" then
                ok "[]" // the board candidate #42 carries no marker of its own
            else
                ok (issueBody "Paths: src/Audio/Sub")) // #42's body — a subtree of #99's reservation

    match Scan.snapshot transport [ aRow ] None false None 120 with
    | Ok(document, _) ->
        match Snapshot.parse document with
        | Ok request ->
            // The off-board claim is reserved, named by its holder and its item — a board scan never saw it.
            match request.InFlight with
            | [ r ] ->
                match r.Holder with
                | Batch.LiveClaim(WorkerId w, ref, _, _) ->
                    Assert.Equal("puffin-h11", w)
                    Assert.Equal(99, ref.Number)
                | other -> failwith $"the off-board reservation must name its holder — got %A{other}"
            | other -> failwith $"exactly one off-board reservation expected — got %A{other}"

            // AND THE OVERLAPPING CANDIDATE IS REFUSED, not scheduled over the lock the board could not see.
            let decision =
                Batch.schedule
                    request.AllowBacklog
                    request.Limit
                    request.InFlight
                    (request.Candidates |> List.map (fun c -> c.Item))

            match decision with
            | Green result -> Assert.Empty(result.Chosen)
            | other -> failwith $"an overlap with an off-board claim is not schedulable — got %A{other}"
        | Error e -> failwith $"parse failed: %A{e}"
    | Error e -> failwith $"scan failed: %A{e}"

[<Fact>]
let ``a STALE off-board claim still RESERVES its touch-set - a lock is broken only by reap, never a clock`` () =
    // #461/#581, case 25 (the starved-queue slice). A lapsed lease is EVIDENCE of abandonment, never
    // proof — and until `reap` collects it, the marker still holds the item and its files. The scheduler
    // reserves it exactly as it reserves a live claim, or it hands a second worker the tree a stale-but-
    // unreaped holder is standing in. The off-board scan reads it via `reserver` (not `winner`, which
    // drops stale), and carries its TRUE, expired age so the collision reads "lease EXPIRED — reapable".
    let stale =
        """[{"id":701,"body":"<!-- fsgg:claim worker=ghost-222 lease=120 -->","updated_at":"2020-01-01T00:00:00Z"}]"""

    let transport =
        Fake.Recorder(fun req ->
            if req.Path.EndsWith "/issues" then
                ok """[{"number":99,"title":"off-board, stale","state":"open","body":"Paths: src/Dead"}]"""
            elif req.Path.EndsWith "/99/comments" then
                ok stale
            elif req.Path.EndsWith "/comments" then
                ok "[]" // the board candidate #42 carries no marker of its own
            else
                ok (issueBody "Paths: src/Dead/Sub")) // #42's body — a subtree of the stale claim's reservation

    match Scan.snapshot transport [ aRow ] None false None 120 with
    | Ok(document, _) ->
        match Snapshot.parse document with
        | Ok request ->
            match request.InFlight with
            | [ r ] ->
                match r.Holder with
                | Batch.LiveClaim(WorkerId w, ref, age, _) ->
                    Assert.Equal("ghost-222", w)
                    Assert.Equal(99, ref.Number)
                    // The EXPIRED age survives — it is what turns the collision into a reap, not a wait.
                    Assert.True(age > 120 * 60, $"a stale claim must carry its expired age, got {age}s")
                | other -> failwith $"the stale off-board claim must still reserve, named — got %A{other}"
            | other -> failwith $"exactly one reservation expected — got %A{other}"

            // AND THE OVERLAPPING CANDIDATE IS REFUSED: a stale lock is not scheduled over, only reaped.
            match
                Batch.schedule request.AllowBacklog request.Limit request.InFlight (request.Candidates |> List.map (fun c -> c.Item))
            with
            | Green result -> Assert.Empty(result.Chosen)
            | other -> failwith $"an overlap with a stale-but-unreaped claim is not schedulable — got %A{other}"
        | Error e -> failwith $"parse failed: %A{e}"
    | Error e -> failwith $"scan failed: %A{e}"

[<Fact>]
let ``#712 a STALE off-board claim held open by a PR carries livePr on its reservation - arm B`` () =
    // #712/#581. A lapsed lease whose `item/<n>-*` PR is still open is NOT reapable — `reap` refuses it,
    // so the tools must not call its reservation "reapable". The proof of life rides onto the reservation
    // as `livePr = Some pr`, survives the scan→parse round trip, and is what lets `Batch` render the
    // collision honestly (name the PR, no lease window) instead of advertising an action reap declines.
    let stale =
        """[{"id":701,"body":"<!-- fsgg:claim worker=ghost-222 lease=120 -->","updated_at":"2020-01-01T00:00:00Z"}]"""

    let transport =
        Fake.Recorder(fun req ->
            if req.Path.EndsWith "/pulls" then
                // the item's own branch PR — server-side proof of life (#581).
                ok """[{"number":777,"head":{"ref":"item/99-resume"}}]"""
            elif req.Path.EndsWith "/issues" then
                ok """[{"number":99,"title":"off-board, stale, PR alive","state":"open","body":"Paths: src/Dead"}]"""
            elif req.Path.EndsWith "/99/comments" then
                ok stale
            elif req.Path.EndsWith "/comments" then
                ok "[]" // the board candidate #42 carries no marker of its own
            else
                ok (issueBody "Paths: src/Live")) // #42's body — disjoint from the stale claim

    match Scan.snapshot transport [ aRow ] None false None 120 with
    | Ok(document, _) ->
        match Snapshot.parse document with
        | Ok request ->
            match request.InFlight with
            | [ r ] ->
                match r.Holder with
                | Batch.LiveClaim(WorkerId w, ref, age, livePr) ->
                    Assert.Equal("ghost-222", w)
                    Assert.Equal(99, ref.Number)
                    Assert.True(age > 120 * 60, $"a stale claim must carry its expired age, got {age}s")
                    // THE POINT: the PR that keeps this lapsed claim alive survives to the reservation.
                    Assert.Equal(Some 777, livePr)
                | other -> failwith $"the PR-alive stale claim must reserve, named, with livePr — got %A{other}"
            | other -> failwith $"exactly one reservation expected — got %A{other}"
        | Error e -> failwith $"parse failed: %A{e}"
    | Error e -> failwith $"scan failed: %A{e}"

[<Fact>]
let ``#712 a STALE BOARD claim held open by a PR carries livePr, from the one liveness read - arm A`` () =
    // #712/#581, the board loop. The candidate's claim block ALREADY reads the proof of life to decide
    // whether the item is offered; the reservation reuses that same read rather than probing again (the
    // budget that dies first, #418). So the claim renders `lease-expired-pr-open` AND the reservation
    // carries `livePr = Some pr` — the two must agree, because they come from one read.
    let stale =
        """[{"id":901,"body":"<!-- fsgg:claim worker=vole-418 lease=120 prev=Ready -->","updated_at":"2020-01-01T00:00:00Z"}]"""

    let transport =
        Fake.Recorder(fun req ->
            if req.Path.EndsWith "/pulls" then
                ok """[{"number":888,"head":{"ref":"item/42-keep-going"}}]"""
            elif req.Path.EndsWith "/issues" then
                ok "[]" // no off-board claim; the board candidate #42 is the whole story here
            elif req.Path.EndsWith "/comments" then
                ok stale
            else
                ok (issueBody "Paths: src/Audio/**"))

    match Scan.snapshot transport [ aRow ] None false None 120 with
    | Ok(document, _) ->
        match Snapshot.parse document with
        | Ok request ->
            // The claim block reports the PR-open liveness (this is what withholds the item from `take`).
            match request.Candidates.[0].Item.Claim with
            | Some(claim, LeaseExpiredPrOpen pr) ->
                Assert.Equal(WorkerId "vole-418", claim.Worker)
                Assert.Equal(888, pr)
            | other -> failwith $"the claim block must render the PR-open liveness — got %A{other}"

            // AND the reservation carries the same PR — one read, both consumers agree.
            match request.InFlight with
            | [ r ] ->
                match r.Holder with
                | Batch.LiveClaim(WorkerId w, ref, _, livePr) ->
                    Assert.Equal("vole-418", w)
                    Assert.Equal(42, ref.Number)
                    Assert.Equal(Some 888, livePr)
                | other -> failwith $"the board reservation must carry livePr — got %A{other}"
            | other -> failwith $"exactly one reservation expected — got %A{other}"
        | Error e -> failwith $"parse failed: %A{e}"
    | Error e -> failwith $"scan failed: %A{e}"

[<Fact>]
let ``#1055 a STALE claim with a pushed branch and NO PR round-trips as lease-expired-branch-pushed`` () =
    // §3, before §5 opens the PR: the lease lapsed and there is no open PR, but a pushed `item/42-*` branch
    // exists. The claim must render `lease-expired-branch-pushed` on the wire and parse back to the same
    // case — proof of life the scheduler and reap both read off the snapshot.
    let stale =
        """[{"id":902,"body":"<!-- fsgg:claim worker=curlew-ab lease=120 prev=Ready -->","updated_at":"2020-01-01T00:00:00Z"}]"""

    let transport =
        Fake.Recorder(fun req ->
            if req.Path.Contains "matching-refs" then
                ok """[{"ref":"refs/heads/item/42-wip","object":{"sha":"abc"}}]"""
            elif req.Path.EndsWith "/pulls" then
                ok "[]" // no open PR — the branch probe decides
            elif req.Path.EndsWith "/issues" then
                ok "[]"
            elif req.Path.EndsWith "/comments" then
                ok stale
            else
                ok (issueBody "Paths: src/Audio/**"))

    match Scan.snapshot transport [ aRow ] None false None 120 with
    | Ok(document, _) ->
        match Snapshot.parse document with
        | Ok request ->
            match request.Candidates.[0].Item.Claim with
            | Some(claim, LeaseExpiredBranchPushed) -> Assert.Equal(WorkerId "curlew-ab", claim.Worker)
            | other -> failwith $"the claim block must render branch-pushed liveness — got %A{other}"
        | Error e -> failwith $"parse failed: %A{e}"
    | Error e -> failwith $"scan failed: %A{e}"

[<Fact>]
let ``a MARKERLESS In-progress row RESERVES its touch-set as Unowned - arm A of active_claims`` () =
    // Case 25 (starved). The board's In-progress column is a claim signal in its own right: something is
    // evidently editing those files, so the row reserves — but there is no marker, hence no worker to name
    // and no lease to wait out. It is `Unowned`, deliberately distinct from a live claim, so a colliding
    // candidate is told "In progress with NO claim marker" rather than sent to wait for a marker that is
    // never coming (#428). A Ready row with no marker, by contrast, reserves nothing — nobody is on it.
    let inProgress = { aRow with Status = InProgress }

    // #43 is a Ready candidate declaring a SUBTREE of the In-progress #42's touch-set, so it must be refused.
    let candidate43 =
        { aRow with
            Ref = { aRow.Ref with Number = 43 }
            Status = Ready }

    let transport =
        Fake.Recorder(fun req ->
            if req.Path.EndsWith "/issues" then
                ok "[]" // no OFF-board claim; the reservation comes from the board's In-progress column
            elif req.Path.EndsWith "/comments" then
                ok "[]" // NEITHER row carries a marker
            elif req.Path.Contains "/issues/43" then
                ok """{"number":43,"body":"Paths: src/Audio/Sub"}"""
            else
                ok (issueBody "Paths: src/Audio")) // #42's body

    match Scan.snapshot transport [ inProgress; candidate43 ] None false None 120 with
    | Ok(document, _) ->
        match Snapshot.parse document with
        | Ok request ->
            match request.InFlight with
            | [ r ] ->
                match r.Holder with
                | Batch.Unowned ref -> Assert.Equal(42, ref.Number)
                | other -> failwith $"a markerless In-progress row reserves as Unowned — got %A{other}"
            | other -> failwith $"exactly one reservation expected — got %A{other}"

            // AND THE OVERLAPPING Ready CANDIDATE IS REFUSED, its collision naming the Unowned reserver.
            match
                Batch.schedule request.AllowBacklog request.Limit request.InFlight (request.Candidates |> List.map (fun c -> c.Item))
            with
            | Green result ->
                Assert.DoesNotContain(43, result.Chosen |> List.map (fun i -> i.Ref.Number))

                let d43 = result.Decisions |> List.find (fun d -> d.Item.Ref.Number = 43)

                match d43.CollidedWith with
                | Some(Batch.Unowned ref) -> Assert.Equal(42, ref.Number)
                | other -> failwith $"#43 must collide with the Unowned reserver #42 — got %A{other}"
            | other -> failwith $"an overlap with an In-progress reserver is not schedulable — got %A{other}"
        | Error e -> failwith $"parse failed: %A{e}"
    | Error e -> failwith $"scan failed: %A{e}"

[<Fact>]
let ``#1150 a live-held item whose BODY READ FAILED reserves an UNREADABLE touch-set, and reds the batch`` () =
    // The FAIL-OPEN this closes: `Scan` built a reservation only for a live-held item with a READABLE body;
    // one whose body read FAILED fell to `| _ -> ()` and reserved NOTHING. Core has a fail-closed guard for
    // exactly this (`Batch.unusableReservation`, the `Unreadable` branch) — but it never received the input,
    // because Scan dropped the claim. So a readable candidate overlapping the held item's REAL files would be
    // scheduled, and a documented Core safety guarantee was non-load-bearing end to end.
    let now = System.DateTimeOffset.UtcNow.ToString("o")

    // #42 is LIVE-HELD (a within-lease marker), but its BODY read will FAIL — the exact trigger.
    let marker =
        $"""[{{"id":901,"body":"<!-- fsgg:claim worker=vole-418 lease=120 prev=Ready -->","updated_at":"%s{now}"}}]"""

    // #43 is a Ready candidate with a perfectly readable touch-set. It must NOT be scheduled: with #42's
    // surface unknown, no candidate can be proven disjoint from the lock, so the WHOLE batch reds.
    let candidate43 =
        { aRow with
            Ref = { aRow.Ref with Number = 43 }
            Status = Ready }

    let transport =
        Fake.Recorder(fun req ->
            if req.Path.EndsWith "/issues" then
                ok "[]" // the off-board scan finds no claim here
            elif req.Path.EndsWith "/42/comments" then
                ok marker // #42 carries a LIVE claim marker — the lock is real
            elif req.Path.EndsWith "/comments" then
                ok "[]" // #43 carries no marker
            elif req.Path.EndsWith "/issues/42" then
                Error(Errors.Http(502, "bad gateway")) // #42's BODY read FAILS — its touch-set is now UNKNOWN
            else
                ok """{"number":43,"body":"Paths: src/Audio/Sub"}""") // #43 reads fine, and would be schedulable

    match Scan.snapshot transport [ aRow; candidate43 ] None false None 120 with
    | Ok(document, receipt) ->
        Assert.Equal(1, receipt.BodiesUnreadable) // #42's body failed; #43's did not

        match Snapshot.parse document with
        | Ok request ->
            // THE FIX, ARM 1: the held item still RESERVES — an UNREADABLE surface, not nothing.
            match request.InFlight with
            | [ r ] ->
                match r.Paths with
                | Unreadable _ -> ()
                | other -> failwith $"a held claim with an unreadable body must reserve an Unreadable touch-set — got %A{other}"

                match r.Holder with
                | Batch.LiveClaim(WorkerId w, ref, _, _) ->
                    Assert.Equal("vole-418", w)
                    Assert.Equal(42, ref.Number)
                | other -> failwith $"the reservation must name its live holder — got %A{other}"
            | other -> failwith $"exactly one reservation expected (the held #42) — got %A{other}"

            // THE FIX, ARM 2: the batch is RED. A reservation whose surface we never saw makes every later
            // comparison a lie, so #43 is refused rather than handed files #42's holder may be standing in.
            match
                Batch.schedule request.AllowBacklog request.Limit request.InFlight (request.Candidates |> List.map (fun c -> c.Item))
            with
            | Red reasons -> Assert.True(reasons |> List.exists (fun (m: string) -> m.Contains "vole-418"))
            | other ->
                failwith
                    $"an Unreadable reservation must RED the batch, not schedule a candidate against a hole it cannot see — got %A{other}"
        | Error e -> failwith $"parse failed: %A{e}"
    | Error e -> failwith $"the scan must survive one unreadable body while still reserving the lock — got %A{e}"

// ====================================================================================================
// THE OWNERSHIP PIN (#1058) — `Protocol` states a document it does not render.
// ====================================================================================================

/// `Protocol.snapshotSchema` is a THIRD copy of a string `Scan` writes and `Snapshot` reads, and #1058
/// chose that deliberately: the snapshot's shape is owned in `Core`, beside #1027's fact inventory, so
/// `generate-projections` can read it off the one `facts` surface it already reads. The rejected
/// alternative — own the shape where it is rendered — is the stricter reading of #865/#916 trap 1.
///
/// THIS TEST IS THE PRICE OF THAT CALL, and it is the whole reason the call is defensible. A module
/// stating the shape of a document it does not render can drift from it, silently, in the direction
/// that matters most: `check-board`'s `jq` filters are generated FROM `Protocol`, so a `Protocol` that
/// disagrees with `Scan` publishes selectors that match nothing — and no rows reads as a CLEAN BOARD
/// (#476, #1058). The doc would be confidently, generatedly wrong.
///
/// It lives HERE rather than in `ProtocolTests` because `Core.Tests` references `Core` and nothing else:
/// it cannot see `Scan` or `Snapshot`, so it cannot compare `Protocol`'s claim against either. This file
/// already exists to assert exactly this class of cross-project agreement.
[<Fact>]
let ``Protocol states the schema Scan actually writes, and Snapshot actually accepts`` () =
    // The REAL writer, against a fake transport — never a hand-written fixture, per this file's rule.
    let transport = routed (issueBody "Paths: src/Audio/**") "[]"

    match Scan.snapshot transport [ aRow ] None false None 120 with
    | Error e -> failwith $"the scan must produce a snapshot — got %A{e}"
    | Ok(document, _) ->

    let written =
        use doc = System.Text.Json.JsonDocument.Parse(document: string)
        doc.RootElement.GetProperty("schema").GetString()

    Assert.Equal(Protocol.snapshotSchema, written)

    // And the parser accepts what Protocol claims: a document carrying Protocol's string parses.
    match Snapshot.parse document with
    | Ok _ -> ()
    | Error errors ->
        let detail = errors |> List.map (fun e -> $"{e.Path}: {e.Message}") |> String.concat "; "
        failwith $"Snapshot.parse refused a document carrying Protocol.snapshotSchema: {detail}"

/// The keys `Protocol` states are the keys the writer EMITS — no more, and none missing.
///
/// `snapshotKeys` is what `check-board`'s generated region tells a reader they may select on. A key
/// `Protocol` invents is a selector that matches nothing; a key it omits is a fact the reader is never
/// told exists. The literal this replaced had BOTH bugs latent — it spelled `leaseMinutes` before
/// `limit` while the writer emits `limit` first, and nothing compared them, because nothing could.
[<Fact>]
let ``Protocol states exactly the snapshot's top-level keys, in the writer's order`` () =
    let transport = routed (issueBody "Paths: src/Audio/**") "[]"

    match Scan.snapshot transport [ aRow ] None false None 120 with
    | Error e -> failwith $"the scan must produce a snapshot — got %A{e}"
    | Ok(document, _) ->

    let written =
        use doc = System.Text.Json.JsonDocument.Parse(document: string)
        doc.RootElement.EnumerateObject() |> Seq.map (fun p -> p.Name) |> List.ofSeq

    Assert.Equal<string list>(written, Protocol.snapshotKeys |> List.map (fun k -> k.Key))

// ---- the scan must COLLECT the fact `BLOCKER-CLEARED` reads (.github#1738) ---------------------------
//
// THE GATE AND ITS SUBJECT ARE ONE STORY, AND THIS FILE IS THE ONLY PLACE THAT CAN SAY SO. `Chore`'s #1738
// gate reads `Item.ItemPr`; `Scan` is the only thing that ever writes it; `ChoreTests` builds its items by
// hand, so it can prove the RULE and can never prove the rule has an INPUT. The probe used to fire only on
// `Ready`/`Backlog` — the columns a scheduler offers as they STAND — while `BLOCKER-CLEARED` acts
// exclusively on `Blocked` rows and writes the column that makes one offerable NEXT pass. So the field was
// `None` for the rule's entire population and the gate would have been dead on arrival: green, and blind
// (#266). Measured on `.github` on 2026-07-29 — `#1689` sat `Blocked` with PR #1911 open on `item/1689-*`,
// and the snapshot reported `itemPr: null` for it.

/// A `Blocked` board row whose recorded blocker has CLOSED — `BLOCKER-CLEARED`'s exact firing condition.
/// `FS.GG.SDD#7` is OFF the board here, so its state is resolved by a REST read of `/issues/7`.
let private aClearedBlockedRow: Scan.Row =
    { aRow with
        Status = Blocked
        BlockedByRaw = "FS.GG.SDD#7" }

/// A transport for the blocked-row legs. `pulls` counts its calls rather than asserting inside the
/// recorder: an exception thrown through the scan would be indistinguishable from any other IO failure,
/// and a leg that cannot tell "the probe fired" from "the scan broke" is not measuring the probe.
let private blockedRowTransport (blockerJson: string) (openPrs: string) =
    let mutable pullsReads = 0

    let recorder =
        Fake.Recorder(fun req ->
            if req.Path.EndsWith "/pulls" then
                pullsReads <- pullsReads + 1
                ok openPrs
            elif req.Path.Contains "matching-refs" then
                ok "[]" // no pushed branch either — #1055's second probe
            elif req.Path.EndsWith "/issues" then
                ok "[]" // no off-board claim
            elif req.Path.EndsWith "/comments" then
                ok "[]" // MARKERLESS — the whole point: no claim carries the liveness
            elif req.Path.EndsWith "/issues/7" then
                ok blockerJson
            else
                ok (issueBody "Paths: src/Audio/**"))

    recorder, (fun () -> pullsReads)

[<Fact>]
let ``#1738 a BLOCKED row whose blockers ALL resolved IS probed - its open item PR reaches Item.ItemPr`` () =
    let transport, pullsReads =
        blockedRowTransport """{"number":7,"state":"closed"}""" """[{"number":1911,"head":{"ref":"item/42-already-written"}}]"""

    match Scan.snapshot transport [ aClearedBlockedRow ] None false None 120 with
    | Error e -> failwith $"the scan must produce a snapshot — got %A{e}"
    | Ok(document, _) ->

    match Snapshot.parse document with
    | Error e -> failwith $"parse failed: %A{e}"
    | Ok request ->

    let item = request.Candidates.[0].Item

    Assert.Equal(1, pullsReads ())

    // THE FIELD IS POPULATED — the half that did not exist before #1738.
    Assert.Equal(Some 1911, item.ItemPr)
    Assert.False(item.ItemPrUnreadable)

    // AND THE GATE THEREFORE FIRES, over an item the REAL writer produced. "The rule holds" and "the rule
    // can see its subject" are different claims, and only this file can make the second one.
    Assert.Empty(Chore.derive [ item ])

[<Fact>]
let ``#1738 the SAME blocked row with NO open item PR still derives BLOCKER-CLEARED - #620 intact end to end`` () =
    // The mate, over the real writer: without it, "the probe reaches the gate" is satisfied by a scan that
    // holds every blocked row, and #620's remedy would be deleted at the impure edge rather than in the rule.
    let transport, _ = blockedRowTransport """{"number":7,"state":"closed"}""" "[]"

    match Scan.snapshot transport [ aClearedBlockedRow ] None false None 120 with
    | Error e -> failwith $"the scan must produce a snapshot — got %A{e}"
    | Ok(document, _) ->

    match Snapshot.parse document with
    | Error e -> failwith $"parse failed: %A{e}"
    | Ok request ->

    let item = request.Candidates.[0].Item
    Assert.Equal(None, item.ItemPr)

    match Chore.derive [ item ] |> List.map (fun c -> c.Kind.RuleId) with
    | [ "BLOCKER-CLEARED" ] -> ()
    | other -> failwith $"a cleared Blocked row with no in-flight PR must still promote — got %A{other}"

[<Fact>]
let ``#1738 a BLOCKED row with an OPEN blocker is NOT probed - the widened probe buys no request it need not`` () =
    // THE BOUND, AND THE REASON THIS IS NOT `| Open, _ ->`. Each probe is a REST request on the budget the
    // claim lock lives on (ADR-0034 §3, #418). The probe covers the `BLOCKER-CLEARED` candidate set and NOT
    // ONE ROW MORE, so a `Blocked` row still holding an open blocker — which the rule would refuse anyway —
    // costs nothing.
    let transport, pullsReads =
        blockedRowTransport """{"number":7,"state":"open"}""" "[]"

    match Scan.snapshot transport [ aClearedBlockedRow ] None false None 120 with
    | Error e -> failwith $"the scan must produce a snapshot — got %A{e}"
    | Ok(document, _) ->

    Assert.Equal(0, pullsReads ())

    match Snapshot.parse document with
    | Error e -> failwith $"parse failed: %A{e}"
    | Ok request -> Assert.Equal(None, request.Candidates.[0].Item.ItemPr)

[<Fact>]
let ``#1738 a BLOCKED row with NO blockers at all is NOT probed - an empty list is not "every blocker resolved"`` () =
    // `List.forall` over `[]` is TRUE, so an unguarded `forall` would probe every blocker-LESS `Blocked` row
    // on the board — a whole population the rule refuses on `not item.Blockers.IsEmpty`. The scan carries the
    // same `IsEmpty` guard for that reason, and this pins it. Not hypothetical: `.github#1689` and `#1737`
    // are both `Blocked` with an empty `Blocked by`, and #1689 has an open `item/1689-*` PR.
    let transport, pullsReads = blockedRowTransport "" "[]"

    match Scan.snapshot transport [ { aRow with Status = Blocked } ] None false None 120 with
    | Error e -> failwith $"the scan must produce a snapshot — got %A{e}"
    | Ok(document, _) ->

    Assert.Equal(0, pullsReads ())

    match Snapshot.parse document with
    | Error e -> failwith $"parse failed: %A{e}"
    | Ok request -> Assert.Equal(None, request.Candidates.[0].Item.ItemPr)
