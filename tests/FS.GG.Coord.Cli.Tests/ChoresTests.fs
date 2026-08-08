module FS.GG.Coord.Cli.Tests.ChoresTests

open Xunit
open FS.GG.Coord
open FS.GG.Coord.Types
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Errors
open FS.GG.Coord.GitHub.Transport
open FS.GG.Coord.Cli

// `Chores.offer` is the chore queue's IO edge — the composition ADR-0041 authorised and #733 wires:
// idle? → anything to do? → whose turn? Each of those is already tested where it lives (`Chore.safePoint`
// and `Chore.offer` in ChoreTests, the CAS in WriteTests' chore-lock legs). What is testable ONLY here is
// the composition itself: the ORDER, and what is spent when the answer is no.
//
// THE ORDER IS THE CORRECTNESS ARGUMENT, so it is what these pin. `scripted` answers a FIXED list and
// `failwith`s on the call after it — so scripting ZERO responses asserts "this path touched the network
// NOT AT ALL", which is a claim about call shape that no assertion on the return value could make. Three
// of the five legs below are exactly that claim, because three of the ways to answer `None` must cost
// nothing: they are the common case on a healthy board, and the budget they would spend is REST — the one
// the item CAS itself lives on (ADR-0034 §3).

let private me = WorkerId "vole-418"
let private them = WorkerId "kite-461"

/// #1646 — this process's own id IS the one it is acting as. The chore lock is `Writes.claim` unchanged
/// (ADR-0041), so it asks the same identity question every other lock does, and these tests answer it the
/// way every prescribed invocation does.
let private itsMe = Writes.Derives me

let private now = System.DateTimeOffset.UtcNow.ToString("o")

let private scripted (responses: IoResult<Response> list) =
    let queue = System.Collections.Generic.Queue<IoResult<Response>>(responses)

    Fake.Recorder(fun _ ->
        if queue.Count = 0 then
            failwith "the transport was called more times than the test scripted"
        else
            queue.Dequeue())

let private ok (body: string) =
    Ok
        { Status = 200
          Body = body
          ETag = None
          NextLink = None; Headers = Map.empty }

let private marker (id: int) (worker: string) =
    let body = $"<!-- fsgg:claim worker=%s{worker} lease=10 -->"
    $"""{{"id":%d{id},"body":"%s{body}","updated_at":"%s{now}"}}"""

let private comments (ms: string list) = "[" + String.concat "," ms + "]"

/// A transport that must never be reached. Any call at all is a `failwith`, not a bad assertion — the
/// difference matters, because "it returned None" and "it returned None WITHOUT paying" are different
/// facts and only one of them is what the ordering claims.
let private unreachable = scripted []

let private ref' n =
    { Owner = "FS-GG"
      Repo = ".github"
      Number = n }

/// `.github` is the ONE repo with a chore lock today (#1033) — `Options.choreLockRef` says so, and these
/// tests read that rather than restating it, so a repo that gains or loses a lock cannot leave a fixture
/// asserting the old answer.
let private lockRef = ref' 1033

let private item n status state blockers claim =
    { Ref = ref' n
      PathRepo = ".github"
      Status = status
      State = state
      TouchSet = Declared [ Matchable "src/" ]
      Blockers = blockers
      Claim = claim
      ItemPr = None
      ItemPrUnreadable = false
      HumanBlock = None
      Predicate = None
      Class = None
      BoardClass = None
      Severity = Unset
      Phase = None
      AgeDays = None }

let private blocker n state =
    { Ref = Some(ref' n)
      Raw = $".github#%d{n}"
      State = state }

/// The board that produced this item: `.github#733` sat `Blocked` behind `#979` for 3 minutes short of
/// forever, because #979 CLOSED and nothing re-asks a blocker when its blocker closes. That is the exact
/// condition `BLOCKER-CLEARED` names, and it is why the wiring is worth having: the rule was written,
/// tested, and unreachable while the item it would have freed sat invisible to every scheduler path.
let private blockerClearedBoard =
    [ item 733 Blocked Open [ blocker 979 BlockerClosed ] None ]

[<Fact>]
let ``an idle worker on a board with a cleared blocker is offered the chore, and HOLDS the lock`` () =
    // read the lock (free), post our marker, re-read (we are the live winner). The same three calls
    // WriteTests scripts for the item CAS, because it IS the item CAS (ADR-0041).
    let transport =
        scripted [ ok "[]"; ok """{"id":901}"""; ok (comments [ marker 901 "vole-418" ]) ]

    match Chores.offer transport Chore.AtNext me itsMe None [] "FS-GG" ".github" (Chore.Whole blockerClearedBoard) with
    | None -> failwith "expected an offer: the worker is idle and the board implies a chore"
    | Some(chore, got) ->
        // The chore names the SUBJECT it observed, not the lock it was serialised on. Conflating those
        // would hand a worker a remedy pointed at the lock issue.
        Assert.Equal(ref' 733, chore.Subject)
        Assert.Equal(lockRef, got)
        Assert.Equal("BLOCKER-CLEARED:FS-GG/.github#733", chore.Id)

[<Fact>]
let ``a worker holding a live claim is offered NOTHING, and no lock is attempted`` () =
    // CONDITION 3: never mid-claim. A worker with a live lease and a live touch-set must not be handed an
    // unbounded side-quest — and the refusal must be free, since a fleet at work is the common case.
    let held =
        [ item
              733
              Blocked
              Open
              [ blocker 979 BlockerClosed ]
              (Some(
                  { Worker = me
                    Session = None
                    AgeSeconds = 60
                    PreviousStatus = Some Ready },
                  LeaseHeld
              )) ]

    Assert.Equal(None, Chores.offer unreachable Chore.AtNext me itsMe None [] "FS-GG" ".github" (Chore.Whole held))

[<Fact>]
let ``a clean board costs NO REST — the lock is taken only when there is a chore to take it for`` () =
    // The ordering leg. `derive` is pure and free; the lock is a REST request on the budget the item CAS
    // lives on (ADR-0034 §3). Taking the lock first would spend one on EVERY idle `next` in the fleet, to
    // discover there is nothing to do — which is the common case on a healthy board. `unreachable` is what
    // makes this an assertion rather than a comment.
    let clean = [ item 733 Ready Open [] None ]

    Assert.Equal(None, Chores.offer unreachable Chore.AtNext me itsMe None [] "FS-GG" ".github" (Chore.Whole clean))

[<Fact>]
let ``a repo with no chore lock offers nothing, and never asks the network`` () =
    // ADR-0041 verbatim: absent ⇒ `offer` refuses, failing CLOSED like every other "could not look" here
    // (#266). All SEVEN FS-GG repos have a lock as of #1087, so the honest stand-in for "no lock" is now a
    // repo the map does not know at all — not a receiver (those all resolve). `choreLockRef` returns `None`,
    // and `offer` refuses before touching the network (`unreachable` proves it: any transport call throws).
    let unrostered = blockerClearedBoard |> List.map (fun i -> { i with Ref = { i.Ref with Repo = "FS.GG.Nonexistent" } })

    Assert.Equal(None, Chores.offer unreachable Chore.AtNext me itsMe None [] "FS-GG" "FS.GG.Nonexistent" (Chore.Whole unrostered))

/// The same cleared-blocker condition, on a row belonging to a DIFFERENT repo. The org board is one board
/// for seven repos, so this is what a bare `next` (no `--repo`, hence `Scan.scope None`) actually hands us.
let private otherRepoBoard =
    [ { item 733 Blocked Open [ blocker 979 BlockerClosed ] None with
          Ref =
            { Owner = "FS-GG"
              Repo = "FS.GG.Rendering"
              Number = 640 } } ]

[<Fact>]
let ``a chore is NEVER offered under another repo's lock — the subject and the lock must name one repo`` () =
    // A lock is PER-REPO (ADR-0041). Deriving over the org-wide board and locking `.github#1033` would hand
    // out an `FS.GG.Rendering` chore serialised on `.github`'s lock — so two workers holding two DIFFERENT
    // repos' locks could each be handed the same chore, which is condition 1 defeated by the mechanism meant
    // to enforce it. `unreachable`: the rows are dropped before the lock is ever reached, so this costs
    // nothing on a board whose chores all belong to somebody else.
    Assert.Equal(None, Chores.offer unreachable Chore.AtNext me itsMe None [] "FS-GG" ".github" (Chore.Whole otherRepoBoard))

[<Fact>]
let ``a worker mid-item in ANOTHER repo is not idle — idleness is asked of the WHOLE board`` () =
    // Condition 3, and the reason the scoping is asymmetric: idleness is a fact about the WORKER, a chore is
    // a fact about the REPO. This worker holds a live claim in FS.GG.Rendering and there is a real `.github`
    // chore going begging. Scoping the IDLENESS question to `.github` would not see the Rendering claim and
    // would hand a side-quest to somebody mid-item with a live touch-set — the one thing condition 3 forbids.
    let busyElsewhere =
        { item 640 InProgress Open [] (Some(
              { Worker = me
                Session = None
                AgeSeconds = 60
                PreviousStatus = Some Ready },
              LeaseHeld
          )) with
            Ref =
              { Owner = "FS-GG"
                Repo = "FS.GG.Rendering"
                Number = 640 } }

    let board = busyElsewhere :: blockerClearedBoard

    Assert.Equal(None, Chores.offer unreachable Chore.AtNext me itsMe None [] "FS-GG" ".github" (Chore.Whole board))

[<Fact>]
let ``a cross-repo board still yields THIS repo's chore, under THIS repo's lock`` () =
    // The other side of the scoping: dropping foreign rows must not drop OURS. An idle worker on the org-wide
    // board gets the `.github` chore, locked on `.github#1033` — the Rendering row is simply not this lock's
    // business.
    let transport =
        scripted [ ok "[]"; ok """{"id":901}"""; ok (comments [ marker 901 "vole-418" ]) ]

    match Chores.offer transport Chore.AtNext me itsMe None [] "FS-GG" ".github" (Chore.Whole(otherRepoBoard @ blockerClearedBoard)) with
    | None -> failwith "expected the .github chore: a foreign row must not suppress this repo's own"
    | Some(chore, got) ->
        Assert.Equal(ref' 733, chore.Subject)
        Assert.Equal(lockRef, got)

[<Fact>]
let ``a short-id repo still finds its lock — the scope is the LOCK's canonical repo, not the raw argument`` () =
    // `choreLockRef` canonicalises on the way in, so `.GitHub` resolves to the same lock. The scope filter
    // must compare against the LOCK's repo rather than the caller's spelling — comparing against the raw
    // argument would drop every row for a caller who typed a short id, offering nothing on a board full of
    // chores. That is a silent, total failure, so it is pinned rather than trusted.
    let transport =
        scripted [ ok "[]"; ok """{"id":901}"""; ok (comments [ marker 901 "vole-418" ]) ]

    match Chores.offer transport Chore.AtNext me itsMe None [] "FS-GG" ".GitHub" (Chore.Whole blockerClearedBoard) with
    | None -> failwith "expected an offer: `.GitHub` is `.github`, and its rows are this lock's own"
    | Some(chore, got) ->
        Assert.Equal(ref' 733, chore.Subject)
        Assert.Equal(lockRef, got)

[<Fact>]
let ``losing the lock race offers nothing — the rival is draining this repo, and we say so to nobody`` () =
    // CONDITION 1, end to end: claimed, not broadcast. This is the leg that makes the queue safe under the
    // fan-out it is designed for — #464 (N workers file one finding N times) is what a broadcast queue
    // rediscovers. The CAS refuses on its own (WriteTests pins that); what this pins is that `offer`
    // INHERITS the refusal rather than second-guessing it, and returns the same `None` as a clean board.
    let transport =
        scripted
            [ ok "[]"
              ok """{"id":902}""" // ours
              ok (comments [ marker 901 "kite-461"; marker 902 "vole-418" ]) // they got there first
              ok "" ] // so we withdraw

    Assert.Equal(None, Chores.offer transport Chore.AtNext me itsMe None [] "FS-GG" ".github" (Chore.Whole blockerClearedBoard))

// ---- #1086: a FILTERED board is refused, and refused for FREE ------------------------------------------
//
// The composition's own fail-open. Every leg above hands `offer` a board it built WHOLE — which is what the
// contract always asked for and what nothing enforced. `next --repo <r>` handed it a board `Scan.scope` had
// already filtered, so the idleness question was put to a list that could not answer it: our claim in another
// repo is not in it, and invisible read as absent. The scope now rides in the type, so "I have only a slice"
// is expressible and `safePoint` refuses it.

[<Fact>]
let ``#1086: a FILTERED board offers nothing, and spends NOTHING finding that out`` () =
    // `unreachable` failwiths on the FIRST transport call, so a green here asserts the refusal never touched
    // the network — a claim about call SHAPE that `Assert.Equal(None, ...)` could not make on its own. It
    // matters because the budget it would spend is REST, the one the item CAS itself lives on (ADR-0034 §3).
    //
    // The board carries a REAL chore, so the refusal is the FILTERING talking rather than an empty queue —
    // see the control leg below, which offers on exactly these rows.
    Assert.Equal(None, Chores.offer unreachable Chore.AtNext me itsMe None [] "FS-GG" ".github" (Chore.Filtered blockerClearedBoard))

[<Fact>]
let ``#1086: the SAME rows offer when the board is WHOLE — the refusal above is the scope, not the rows`` () =
    // The control, and the leg above is worthless without it: without this, that assertion would pass against
    // a board that simply had no chore on it, and would keep passing if `Filtered` were quietly made to mean
    // nothing at all. Same rows, same worker, one word different.
    let transport =
        scripted [ ok "[]"; ok """{"id":901}"""; ok (comments [ marker 901 "vole-418" ]) ]

    match Chores.offer transport Chore.AtNext me itsMe None [] "FS-GG" ".github" (Chore.Whole blockerClearedBoard) with
    | None -> failwith "a WHOLE board carrying a real chore must offer it — otherwise the Filtered leg proves nothing"
    | Some(chore, got) ->
        Assert.Equal(ref' 733, chore.Subject)
        Assert.Equal(lockRef, got)

// ---- #1649: THE OFFER PATH READS THE COLUMN IT IS ABOUT TO ASK SOMEBODY TO WRITE --------------------
//
// Eight offers across three repos on ONE fan-out, seven of them for a chore that was ALREADY SATISFIED —
// and the eighth genuinely owed, arriving through the same channel, which is what made the ratio dangerous
// rather than merely wasteful. Three cheap explanations were measured and refuted before this was found:
// the 90s scan cache (the read was fresh, and a fresh `ready` contradicted the offer at the same instant),
// a caller reusing an old snapshot (the offers came from `AfterDone`, which buys its OWN scan for exactly
// this purpose), and duplication (one measured offer's premise was independently FALSE, not a repeat).
//
// The cause was none of those. `Client.wholeBoard` DISCARDED the scan rows and built the offer's board from
// `Snapshot.parse` alone — and that parser sets `BoardClass = None` meaning "I did not look", because the
// board's `Class` column is a SCAN fact the pure document cannot carry. `CLASS-PROJECTION-LAG` fires on
// `BoardClass <> Some declared`, so against an unjoined board it fired for EVERY open classed item, forever,
// and `Chore.isRetired` re-derived the same "still owed" after the write that satisfied it.
//
// THESE DRIVE THE REAL WRITER. `Scan.snapshot` produces the bytes and `Client.offerBoardOf` consumes them
// with the rows beside them, so what is pinned is the JOIN — the one thing a hand-built fixture board (every
// other leg in this file) structurally cannot check, because a hand-built `Item` has already been TOLD what
// its `BoardClass` is.

let private classedBody = "Paths: src/FS.GG.Coord.Cli/Client.fs\n\nClass: hardening\n"

/// A scan row for `.github#1524` — the item eight offers named — with the `Class` COLUMN under our control.
let private classRow (boardClass: ItemClass option) : Scan.Row =
    { Ref = ref' 1524
      Title = "an item whose body declares a class"
      Status = Ready
      BlockedByRaw = ""
      State = Open
      IsPullRequest = false
      PathRepo = ".github"
      BoardClass = boardClass
      Severity = Unset
      Phase = None
      CreatedAt = None }

/// The scan's OWN document for those rows, written by the engine's writer rather than by this test.
let private snapshotOf (rows: Scan.Row list) =
    let body = classedBody.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n")

    let transport =
        Fake.Recorder(fun req ->
            if req.Path.EndsWith "/issues" then ok "[]"
            elif req.Path.EndsWith "/comments" then ok "[]"
            else ok $"""{{"number":1524,"body":"%s{body}"}}""")

    match Scan.snapshot transport rows None false None 120 with
    | Error e -> failwith $"the scan must produce a snapshot — got %A{e}"
    | Ok(document, _) -> document

/// The offer path's board, built the way the engine builds it.
let private offerBoard (rows: Scan.Row list) =
    match Client.offerBoardOf rows (snapshotOf rows) with
    | None -> failwith "the offer path must build a board from a document its own writer produced"
    | Some board -> board

[<Fact>]
let ``#1649: a chore ALREADY DISCHARGED is not offered again — and costs no lock to decline`` () =
    // Worker A discharged `CLASS-PROJECTION-LAG` on #1524: the column now says `hardening` and so does the
    // body. Worker B completes an item in the same repo immediately after and must be offered NOTHING.
    //
    // `unreachable` is the second half of the claim and the more valuable half. #1649's measured cost was
    // eight NEEDLESS REPO-LOCK ACQUISITIONS — the chore lock is per repo, so a stale offer serialises
    // against every other worker in it. A green here says the decline never reached the network at all, so
    // no lock was taken. `Assert.Equal(None, ...)` on its own could not say that.
    let board = offerBoard [ classRow (Some Hardening) ]

    Assert.Equal(None, Chores.offer unreachable Chore.AfterDone me itsMe None [] "FS-GG" ".github" board)

[<Fact>]
let ``#1649: the SAME item IS offered while the column genuinely lags — the silence above is the read`` () =
    // THE CONTROL, and the leg above is worth nothing without it: that assertion would pass just as well
    // against an offer mechanism deleted outright, which #1649 forbids in as many words — the mechanism is
    // good, and one of the eight offers was real work correctly assigned. Same body, same worker, same
    // boundary; the board's `Class` column is the ONE thing that differs, and it is now what decides.
    let transport =
        scripted [ ok "[]"; ok """{"id":901}"""; ok (comments [ marker 901 "vole-418" ]) ]

    let board = offerBoard [ classRow None ]

    match Chores.offer transport Chore.AfterDone me itsMe None [] "FS-GG" ".github" board with
    | None -> failwith "a genuinely lagging Class column must still be offered — the fix is freshness, not removal"
    | Some(chore, got) ->
        Assert.Equal(ref' 1524, chore.Subject)
        Assert.Equal(lockRef, got)
        Assert.Equal("CLASS-PROJECTION-LAG:FS-GG/.github#1524", chore.Id)

[<Fact>]
let ``#1649: the board the offer path builds CARRIES the scanned column — the join, not its consequence`` () =
    // The two legs above are the behaviour; this is the fact underneath them, asserted directly so a future
    // regression names itself instead of surfacing as a mystery offer. Before #1649 this read `None` for
    // BOTH rows, which is how an offer came to assert a disagreement it had never read either side of.
    let observed (boardClass: ItemClass option) =
        match offerBoard [ classRow boardClass ] with
        | Chore.Whole [ item ] -> item.BoardClass, item.Class
        | other -> failwith $"expected one whole-board item — got %A{other}"

    Assert.Equal((Some Hardening, Some Hardening), observed (Some Hardening))
    // And an ABSENT column still travels as absent, so a genuinely unset one reads as a real disagreement
    // rather than a suppressed projection — the fail-closed direction `Chore.fs` argues for by name.
    Assert.Equal((None, Some Hardening), observed None)

// ---- #1679: THE OFFER'S BOARD READ IS FRESH — a chore may not survive the write that discharges it ----
//
// THE REMAINING HALF OF THE SAME SYMPTOM, AND THE SUBTLER ONE. #1649 above was a JOIN: the rows were
// discarded, `BoardClass` was pinned at `None`, and the guard was unconditionally true — so the offer named
// a disagreement it had never read either side of. Its fix is present and working, and the offers came
// BACK. This cause is a CLOCK. `Client.wholeBoard` read the board through `Cache.Scheduling`, so for up to
// ninety seconds after any `Class` write the offer derived against the column as it was BEFORE the write.
// The offer was not wrong about the board. It was right about a ninety-second-old board, which is
// indistinguishable from wrong to the worker handed the instruction.
//
// AND `Chore.isRetired` CANNOT RESCUE IT, which is what lifts this above an ordinary stale read: retirement
// works by RE-DERIVING, and a re-derivation over the same cached scan answers "still owed" against a write
// that has already landed. The offer survives its own discharge for the whole window.
//
// WHAT THESE LEGS DO NOT DO IS WAIT. The distinguishing fact is the READ INTENT, not the elapsed time: the
// cache these warm is well inside its TTL and `Cache.Scheduling` WOULD serve it, which is precisely why a
// mutation of `wholeBoard`'s intent back to `Scheduling` reds every leg below. A guard that needed ninety
// seconds to pass would be a guard that also passes on a slow machine for no reason at all.

/// A board row for `.github#1524` in the GraphQL shape the SCAN parses, with the `Class` COLUMN under the
/// fixture's control. `None` is an unset column — the state a `CLASS-PROJECTION-LAG` chore exists to repair.
let private boardRowJson (classColumn: string option) =
    let classField =
        match classColumn with
        | None -> "null"
        | Some c -> $"""{{"name":"%s{c}"}}"""

    $"""{{"status":{{"name":"Ready"}},"blockedBy":null,"class":%s{classField},"phase":null,"content":{{"__typename":"Issue","number":1524,"title":"an item whose body declares a class","state":"OPEN","repository":{{"nameWithOwner":"FS-GG/.github"}}}}}}"""

/// The `.github` board, served live: the `Class` column is read from `column` at the moment of each request,
/// so a test can WRITE the column between two reads and see which read the engine actually made.
let private boardWorld (column: unit -> string option) =
    let body = classedBody.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n")

    Fake.Recorder(fun (req: Request) ->
        let path = req.Path.Trim '/'

        match req.Method, path with
        | "POST", "graphql" ->
            match req.Body with
            | Query(document, _) ->
                if document.Contains "projectsV2" then
                    ok """{"data":{"organization":{"projectsV2":{"nodes":[{"number":12,"title":"Coordination","id":"PVT_coord"}]}}},"rateLimit":{"cost":1,"remaining":4977}}"""
                elif document.Contains "fields(first" then
                    ok """{"data":{"organization":{"projectV2":{"fields":{"nodes":[{"id":"PVTSSF_status","name":"Status","dataType":"SINGLE_SELECT","options":[{"id":"opt_ready","name":"Ready"}]},{"id":"PVTSSF_class","name":"Class","dataType":"SINGLE_SELECT","options":[{"id":"opt_hardening","name":"hardening"}]}]}}}},"rateLimit":{"cost":1,"remaining":4977}}"""
                elif document.Contains "items(first" then
                    ok
                        $"""{{"data":{{"organization":{{"projectV2":{{"items":{{"pageInfo":{{"hasNextPage":false,"endCursor":null}},"nodes":[%s{boardRowJson (column ())}]}}}}}}}},"rateLimit":{{"cost":1,"remaining":4977}}}}"""
                else
                    Error(Errors.NotFound $"the fixture serves no answer for: %s{document}")
            | _ -> Error(Errors.NotFound "a graphql call with no document")
        // The off-board sweep (`Reads.openIssues`): every scheduling read makes it, and the whole board is
        // on the board, so there is nothing off it.
        | "GET", "repos/FS-GG/.github/issues" -> ok "[]"
        | "GET", "repos/FS-GG/.github/issues/1524" -> ok $"""{{"number":1524,"body":"%s{body}"}}"""
        | "GET", "repos/FS-GG/.github/issues/1524/comments" -> ok "[]"
        | m, p -> Error(Errors.NotFound $"the fixture serves no %s{m} %s{p}"))

/// Run `f` against a THROWAWAY cache root, so the shared 90s scan cache is this test's alone.
///
/// `FSGG_COORD_CACHE` is process-global and this is safe only because `AssemblyInfo.fs` disables xUnit's
/// cross-class parallelism — the same licence `ApplicationServiceTests.run` and `FollowupsTests.withCache`
/// take, and it is stated here rather than assumed because the whole subject of these legs IS that cache.
let private withCache (f: unit -> 'a) : 'a =
    let dir =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fsgg-1679-" + System.Guid.NewGuid().ToString "n")

    let previous = System.Environment.GetEnvironmentVariable "FSGG_COORD_CACHE"

    try
        System.IO.Directory.CreateDirectory dir |> ignore
        System.Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", dir)
        f ()
    finally
        System.Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", previous)

        try
            System.IO.Directory.Delete(dir, true)
        with _ ->
            ()

let private context (transport: Fake.Recorder) : Client.Context =
    { Transport = transport
      Owner = "FS-GG"
      Title = "Coordination"
      DefaultRepo = Some ".github"
      ChoreLocks = [] }

let private optionsOf (args: string list) : Options.Options =
    match Options.parse args with
    | Ok o -> o
    | Error e -> failwithf "the fixture's own argv did not parse: %s" e

/// A SCHEDULING read memoises the board — a `take`/`next` anywhere in the fleet, the shared cache doing
/// exactly the job #418 gave it. Then it ASSERTS the cache is warm.
///
/// THAT ASSERTION IS THE LEG'S PREMISE, AND IT IS WHY IT IS AN ASSERTION. Without it, a fixture change that
/// stopped the warm from landing would leave every test below passing over an EMPTY cache — green, for the
/// reason that there was nothing to serve staleness from, which is the one way these could pass while the
/// defect was live. "I could not warm it" is not "the cache did not lie to me" (#266).
let private warmSchedulingCache (transport: Fake.Recorder) =
    match Scan.board transport Cache.Scheduling "FS-GG" "Coordination" 12 with
    | Error e -> failwith $"the fixture's own warming scan failed — got %A{e}"
    | Ok _ -> ()

    Assert.True(
        (Cache.getScan Cache.Scheduling "FS-GG" "Coordination").IsSome,
        "the cache is NOT warm, so nothing below could be served a stale board — the leg would pass vacuously"
    )

/// The offer path's board, read the way `next` and `done --flip` read it — through `Client.wholeBoard`, the
/// real function, so the intent it names is the thing under test.
let private offerPathBoard (transport: Fake.Recorder) =
    match Client.wholeBoard (context transport) (optionsOf [ "next" ]) with
    | None -> failwith "the offer path must build a board — the fixture serves one"
    | Some(Chore.Whole items) -> items
    | Some other -> failwith $"the offer path must hand `Chores.offer` a WHOLE board — got %A{other}"

[<Fact>]
let ``#1679: a Class column written AFTER the cached scan is SEEN — the offer does not survive its discharge`` () =
    withCache (fun () ->
        // The board BEFORE the write: the body declares `hardening`, the column is unset. The chore is
        // genuinely owed at this instant — the control leg below drives exactly this state and offers.
        let mutable column = None
        let transport = boardWorld (fun () -> column)

        warmSchedulingCache transport

        // THE WRITE THAT DISCHARGES IT. Worker A sets `Class = hardening`; body and column now agree, and
        // nothing is owed. The cached scan from a moment ago still says otherwise and is still well inside
        // its TTL.
        column <- Some "hardening"

        match offerPathBoard transport with
        | [ item ] ->
            // The fresh column, not the cached absence. This is the fact the offer is derived from, asserted
            // directly so a regression names itself rather than surfacing as a mystery offer.
            Assert.Equal(Some Hardening, item.BoardClass)

            // And the consequence: nothing is offered. `unreachable` is the second half of the claim —
            // #1679's measured cost is a worker handed a written instruction to perform a board write that
            // is already done, and a PER-REPO CHORE LOCK taken to serialise it against every other worker in
            // that repo. A green here says the decline never reached the network, so no lock was taken.
            Assert.Equal(None, Chores.offer unreachable Chore.AfterDone me itsMe None [] "FS-GG" ".github" (Chore.Whole [ item ]))
        | other -> failwith $"expected the one board row — got %A{other}")

[<Fact>]
let ``#1679: a column that genuinely lags is STILL offered through the fresh read — the fix is freshness, not removal`` () =
    // THE CONTROL, and the leg above is worth nothing without it: that assertion would pass just as well
    // against an offer mechanism deleted outright, or against a `wholeBoard` that had stopped returning a
    // board at all. Same fixture, same warm cache, ONE difference — no write lands — and the chore arrives.
    // The issue says it in as many words: whether the 90s scheduling cache should exist is NOT in scope.
    withCache (fun () ->
        let mutable column = None
        let transport = boardWorld (fun () -> column)

        warmSchedulingCache transport

        // No write. The column still disagrees with the body, and it disagrees on the FRESH read too.
        match offerPathBoard transport with
        | [ item ] ->
            Assert.Equal(None, item.BoardClass)

            let transport' =
                scripted [ ok "[]"; ok """{"id":901}"""; ok (comments [ marker 901 "vole-418" ]) ]

            match Chores.offer transport' Chore.AfterDone me itsMe None [] "FS-GG" ".github" (Chore.Whole [ item ]) with
            | None -> failwith "a genuinely lagging Class column must still be offered — the fix is freshness, not removal"
            | Some(chore, got) ->
                Assert.Equal(ref' 1524, chore.Subject)
                Assert.Equal(lockRef, got)
                Assert.Equal("CLASS-PROJECTION-LAG:FS-GG/.github#1524", chore.Id)
        | other -> failwith $"expected the one board row — got %A{other}")

/// `reconcile --json`'s findings, as the rule ids it proposes. The REAL verb over the REAL fixture: a test
/// that re-derived reconcile's answer in the fixture would be comparing the offer path against a hand-copy
/// of the thing it is supposed to agree with.
let private reconcileIds (transport: Fake.Recorder) : string list =
    let stdout = System.Console.Out
    use captured = new System.IO.StringWriter()

    let out =
        try
            System.Console.SetOut captured
            Client.reconcile (context transport) (optionsOf [ "reconcile"; "--json" ]) |> ignore
            System.Console.Out.Flush()
            captured.ToString()
        finally
            System.Console.SetOut stdout

    System.Text.Json.JsonDocument.Parse(out.Trim()).RootElement.EnumerateArray()
    |> Seq.map (fun e -> e.GetProperty("id").GetString())
    |> List.ofSeq

// AC4 — THE TWO PATHS AGREE AT ONE INSTANT, and this is the assertion that stops a THIRD cause reproducing
// the symptom. `reconcile` proposing nothing while the offer path proposed `CLASS-PROJECTION-LAG` for the
// same item at the same moment was #1649's headline symptom, and it was STILL observable after #1649 landed
// — because the two are the same `Chore.derive` over the same `enrichBoardFacts`, differing in exactly one
// thing: which clock's board they read. Pinning the verdicts together is what makes that difference
// unable to hide, whatever produces it next.
//
// BOTH ROWS MATTER, AND THEY FAIL IN OPPOSITE DIRECTIONS — which is what keeps the equality from being
// satisfiable by two paths that are broken alike. The first is #1679 as measured: the chore is DISCHARGED
// after the cached scan, so a cached read says "still owed" and a fresh one says nothing. The second is its
// mirror: the column is CLEARED after the cached scan, so a cached read says nothing and a fresh one says
// it is owed. A `wholeBoard` back on `Cache.Scheduling` reds one row by over-reporting and the other by
// under-reporting; a comparison that could only catch one of those would be half a test.
[<Theory>]
[<InlineData(null, "hardening", "")>]
[<InlineData("hardening", null, "CLASS-PROJECTION-LAG:FS-GG/.github#1524")>]
let ``#1679: reconcile and the offer path reach the SAME verdict for the same item at the same instant``
    (cached: string)
    (live: string)
    (expected: string)
    =
    withCache (fun () ->
        let nullable (s: string) = if isNull s then None else Some s

        let mutable column = nullable cached
        let transport = boardWorld (fun () -> column)

        warmSchedulingCache transport

        // The board moves. Whatever the cache holds is now a description of a board that no longer exists.
        column <- nullable live

        // THE OFFER PATH FIRST, AND THE ORDER IS LOAD-BEARING. A fresh read REWRITES the shared cache
        // (`Scan.scanFresh` → `putScan`), so running `reconcile` first would hand the offer path a cache
        // holding the CURRENT board — and the comparison would pass under the defect it exists to catch.
        let offered =
            offerPathBoard transport |> Chore.derive |> List.map (fun c -> c.Id)

        let reconciled = reconcileIds transport

        let expectedIds =
            if expected = "" then [] else [ expected ]

        // The verdicts agree...
        Assert.Equal<string list>(reconciled, offered)
        // ...and they agree on the RIGHT answer. Without this, two paths broken in the same direction would
        // satisfy the line above, which is the failure mode an equality assertion has by construction.
        Assert.Equal<string list>(expectedIds, offered))
