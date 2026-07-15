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
      IsPullRequest = false }

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
let ``#520 a CLOSED issue is a candidate, SWEPT with no read, and decided IssueClosed`` () =
    // One was handed to a worker two hours after it was closed as completed, because candidate selection
    // read the board COLUMN and nothing else — and then it was PROMOTED back to Ready on release, re-arming
    // it for the next worker. The fix is NOT to drop it from the candidates: that gets the right answer
    // (never scheduled) with no WORDS, so a worker asking "why isn't #502 offered?" gets nothing for it,
    // where bash names it "the issue is closed". So it stays a candidate and is SWEPT — `Schedulability`
    // answers `Closed -> IssueClosed` as its FIRST question, before the touch-set or the lock, so the sweep
    // needs neither a body read nor a marker read, and the reason survives to `decide`.
    let closed = { aRow with State = Closed; Status = Ready }

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
    // DROPPED such blockers — so an item it called BLOCKED arrived at the engine UNBLOCKED, which under
    // `--engine=fs` is a worker being handed blocked work.
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
            Assert.Equal(1, List.length request.InFlight)
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
                | Batch.LiveClaim(WorkerId w, ref, _) ->
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
