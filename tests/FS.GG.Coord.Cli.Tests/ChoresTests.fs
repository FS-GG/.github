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
          NextLink = None }

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
      Status = status
      State = state
      TouchSet = Declared [ Matchable "src/" ]
      Blockers = blockers
      Claim = claim
      ItemPr = None }

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

    match Chores.offer transport Chore.AtNext me None "FS-GG" ".github" (Chore.Whole blockerClearedBoard) with
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

    Assert.Equal(None, Chores.offer unreachable Chore.AtNext me None "FS-GG" ".github" (Chore.Whole held))

[<Fact>]
let ``a clean board costs NO REST — the lock is taken only when there is a chore to take it for`` () =
    // The ordering leg. `derive` is pure and free; the lock is a REST request on the budget the item CAS
    // lives on (ADR-0034 §3). Taking the lock first would spend one on EVERY idle `next` in the fleet, to
    // discover there is nothing to do — which is the common case on a healthy board. `unreachable` is what
    // makes this an assertion rather than a comment.
    let clean = [ item 733 Ready Open [] None ]

    Assert.Equal(None, Chores.offer unreachable Chore.AtNext me None "FS-GG" ".github" (Chore.Whole clean))

[<Fact>]
let ``a repo with no chore lock offers nothing, and never asks the network`` () =
    // ADR-0041 verbatim: absent ⇒ `offer` refuses, failing CLOSED like every other "could not look" here
    // (#266). All SEVEN FS-GG repos have a lock as of #1087, so the honest stand-in for "no lock" is now a
    // repo the map does not know at all — not a receiver (those all resolve). `choreLockRef` returns `None`,
    // and `offer` refuses before touching the network (`unreachable` proves it: any transport call throws).
    let unrostered = blockerClearedBoard |> List.map (fun i -> { i with Ref = { i.Ref with Repo = "FS.GG.Nonexistent" } })

    Assert.Equal(None, Chores.offer unreachable Chore.AtNext me None "FS-GG" "FS.GG.Nonexistent" (Chore.Whole unrostered))

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
    Assert.Equal(None, Chores.offer unreachable Chore.AtNext me None "FS-GG" ".github" (Chore.Whole otherRepoBoard))

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

    Assert.Equal(None, Chores.offer unreachable Chore.AtNext me None "FS-GG" ".github" (Chore.Whole board))

[<Fact>]
let ``a cross-repo board still yields THIS repo's chore, under THIS repo's lock`` () =
    // The other side of the scoping: dropping foreign rows must not drop OURS. An idle worker on the org-wide
    // board gets the `.github` chore, locked on `.github#1033` — the Rendering row is simply not this lock's
    // business.
    let transport =
        scripted [ ok "[]"; ok """{"id":901}"""; ok (comments [ marker 901 "vole-418" ]) ]

    match Chores.offer transport Chore.AtNext me None "FS-GG" ".github" (Chore.Whole(otherRepoBoard @ blockerClearedBoard)) with
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

    match Chores.offer transport Chore.AtNext me None "FS-GG" ".GitHub" (Chore.Whole blockerClearedBoard) with
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

    Assert.Equal(None, Chores.offer transport Chore.AtNext me None "FS-GG" ".github" (Chore.Whole blockerClearedBoard))

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
    Assert.Equal(None, Chores.offer unreachable Chore.AtNext me None "FS-GG" ".github" (Chore.Filtered blockerClearedBoard))

[<Fact>]
let ``#1086: the SAME rows offer when the board is WHOLE — the refusal above is the scope, not the rows`` () =
    // The control, and the leg above is worthless without it: without this, that assertion would pass against
    // a board that simply had no chore on it, and would keep passing if `Filtered` were quietly made to mean
    // nothing at all. Same rows, same worker, one word different.
    let transport =
        scripted [ ok "[]"; ok """{"id":901}"""; ok (comments [ marker 901 "vole-418" ]) ]

    match Chores.offer transport Chore.AtNext me None "FS-GG" ".github" (Chore.Whole blockerClearedBoard) with
    | None -> failwith "a WHOLE board carrying a real chore must offer it — otherwise the Filtered leg proves nothing"
    | Some(chore, got) ->
        Assert.Equal(ref' 733, chore.Subject)
        Assert.Equal(lockRef, got)
