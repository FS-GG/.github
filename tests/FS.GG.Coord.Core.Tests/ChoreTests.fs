namespace FS.GG.Coord.Tests

open Xunit
open FS.GG.Coord
open FS.GG.Coord.Types
open FS.GG.Coord.Chore

/// THE CHORE QUEUE (ADR-0034 §4.6, Phase 4.3) — the four helping conditions, as tests.
///
/// The design doc is explicit that a subset of the four is not a smaller version of this feature, it is a
/// different and worse one: *"without those four, it is a machine for manufacturing duplicate work and false
/// green."* So the conditions are what is tested here, not the happy path:
///
///   1. CLAIMED — NOT BUILT, which is why `offer` is reachable from no command (see `Chore.fsi`). The lock
///      is IO and its substrate is an open decision. What is testable here is the half the lock rests on:
///      `offer` hands back at most ONE chore, and two callers deriving the same board agree about which.
///   2. VERIFIABLE — `derive` is the only door to a `Chore`, `isRetired` re-derives, and both are pure. A
///      chore cannot outlive its condition, because there is nowhere for it to live.
///   3. SAFE POINT + BOUNDED — `safePoint` refuses a worker mid-lease; `offer` returns an option.
///   4. DEPTH-0 — unwritable, so it has no test. `offer` needs a `SafePoint`, and the chore path mints none.
///
/// Every rule below fails CLOSED, and that asymmetry is the point: a chore we decline to derive costs one
/// round-trip, and a chore we derive WRONGLY is a board write nobody asked for on somebody else's item.
module ChoreTests =

    let private ref n : Ref = { Owner = "FS-GG"; Repo = ".github"; Number = n }

    let private worker = WorkerId "dunlin-753c"
    let private other = WorkerId "plover-a4cf"

    let private claim (w: WorkerId) : Claim =
        { Worker = w
          Session = None
          AgeSeconds = 60
          PreviousStatus = Some Ready }

    let private blocker n state : Blocker =
        { Ref = Some(ref n)
          Raw = $".github#%d{n}"
          State = state }

    /// An item with nothing wrong with it. Each test breaks exactly one thing.
    let private item n : Item =
        { Ref = ref n
          Status = Ready
          State = Open
          TouchSet = Declared [ Matchable "src/" ]
          Blockers = []
          Claim = None
          ItemPr = None }

    let private ids (items: Item list) = derive items |> List.map (fun c -> c.Id)

    let private rules (items: Item list) =
        derive items |> List.map (fun c -> c.Kind.RuleId) |> List.sort

    // ---- STALE-CLAIM: a lease is a clock, abandonment is a verdict (#581) ----------------------------

    [<Fact>]
    let ``STALE-CLAIM fires only once the lease lapsed AND we looked for the PR and found none (#581)`` () =
        let i = { item 1 with Claim = Some(claim other, LeaseExpiredNoPr) }
        Assert.Equal<string list>([ "STALE-CLAIM" ], rules [ i ])

    [<Fact>]
    let ``a lapsed lease with an OPEN item PR is NOT a chore — the worker is demonstrably working (#581)`` () =
        // The false positive is systematic: work that outlasts its lease. Offering this would hand the
        // reaper a chore `Writes.reapable` must then refuse — a queue entry that can never drain.
        let i = { item 1 with Claim = Some(claim other, LeaseExpiredPrOpen 433) }
        Assert.Empty(derive [ i ])

    [<Fact>]
    let ``a lapsed lease we could NOT probe is NOT a chore — could-not-look is not no-PR (#266, #581)`` () =
        let i = { item 1 with Claim = Some(claim other, LivenessUnknown) }
        Assert.Empty(derive [ i ])

    [<Fact>]
    let ``a LIVE lease is never a STALE-CLAIM`` () =
        let i = { item 1 with Status = InProgress; Claim = Some(claim other, LeaseHeld) }
        Assert.Empty(derive [ i ])

    [<Fact>]
    let ``STALE-CLAIM fires on a CLOSED issue too — an abandoned lease reserves its touch-set either way (#601)`` () =
        let i =
            { item 1 with
                State = Closed
                Status = Done
                Claim = Some(claim other, LeaseExpiredNoPr) }

        Assert.Equal<string list>([ "STALE-CLAIM" ], rules [ i ])

    // ---- CLAIM-STATUS-LAG: the holder's own decisions still win (#331) -------------------------------

    [<Fact>]
    let ``CLAIM-STATUS-LAG fires when a live claim holds an item the board still calls Ready`` () =
        let i = { item 1 with Claim = Some(claim other, LeaseHeld) }
        Assert.Equal<string list>([ "CLAIM-STATUS-LAG" ], rules [ i ])

    [<Fact>]
    let ``a live claim over a DELIBERATE Blocked is NOT a lag — a column set during a lease wins (#331)`` () =
        // The worker hit a blocker, set the column, and has not released yet. "Reconciling" it would
        // overwrite their judgement with a default — this mechanism running backwards.
        let i = { item 1 with Status = Blocked; Claim = Some(claim other, LeaseHeld) }
        Assert.Empty(derive [ i ])

    [<Fact>]
    let ``a live claim over In review is NOT a lag either (#331)`` () =
        let i = { item 1 with Status = InReview; Claim = Some(claim other, LeaseHeld) }
        Assert.Empty(derive [ i ])

    [<Fact>]
    let ``NoStatus under a live claim IS a lag — an unset column is a case, not a null (#437)`` () =
        let i = { item 1 with Status = NoStatus; Claim = Some(claim other, LeaseHeld) }
        Assert.Equal<string list>([ "CLAIM-STATUS-LAG" ], rules [ i ])

    [<Fact>]
    let ``a STALE claim with a lagging column reports STALE-CLAIM ONLY — its remedy restores it (#481)`` () =
        // Both conditions are literally true; reporting both would have two chores race to write one column.
        let i = { item 1 with Claim = Some(claim other, LeaseExpiredNoPr) }
        Assert.Equal<string list>([ "STALE-CLAIM" ], rules [ i ])

    // ---- CLOSED-ISSUE-NOT-DONE: the issue is the work, the column is the copy (#520) -----------------

    [<Fact>]
    let ``CLOSED-ISSUE-NOT-DONE fires — when the issue and the column disagree, the issue wins (#520)`` () =
        let i = { item 1 with State = Closed }
        Assert.Equal<string list>([ "CLOSED-ISSUE-NOT-DONE" ], rules [ i ])

    [<Fact>]
    let ``a closed, Done item is not a chore`` () =
        let i = { item 1 with State = Closed; Status = Done }
        Assert.Empty(derive [ i ])

    [<Fact>]
    let ``an OPEN item is never CLOSED-ISSUE-NOT-DONE, whatever its column`` () =
        let i = { item 1 with State = Open; Status = Backlog }
        Assert.Empty(derive [ i ])

    // ---- BLOCKER-CLEARED: resolved means CLOSED **or MERGED** (#476) ---------------------------------

    [<Fact>]
    let ``BLOCKER-CLEARED fires when every blocker is closed`` () =
        let i =
            { item 1 with
                Status = Blocked
                Blockers = [ blocker 2 BlockerClosed; blocker 3 BlockerClosed ] }

        Assert.Equal<string list>([ "BLOCKER-CLEARED" ], rules [ i ])

    [<Fact>]
    let ``a MERGED blocker is RESOLVED — clearing only on CLOSED unblocks on abandonment (#476)`` () =
        // The bug this carries: a rule that clears only on CLOSED opens the gate exactly when the blocking
        // work is thrown away, and shuts it forever once the work is FINISHED.
        let i = { item 1 with Status = Blocked; Blockers = [ blocker 2 BlockerMerged ] }
        Assert.Equal<string list>([ "BLOCKER-CLEARED" ], rules [ i ])

    [<Fact>]
    let ``one UNKNOWN blocker and BLOCKER-CLEARED does not fire — could-not-look is not cleared (#266, #421)`` () =
        let i =
            { item 1 with
                Status = Blocked
                Blockers = [ blocker 2 BlockerClosed; blocker 3 BlockerUnknown ] }

        Assert.Empty(derive [ i ])

    [<Fact>]
    let ``one UNPARSEABLE blocker and BLOCKER-CLEARED does not fire — prose in a dependency field blocks`` () =
        let i =
            { item 1 with
                Status = Blocked
                Blockers = [ blocker 2 BlockerClosed; { Ref = None; Raw = "RESOLVED: shipped last week"; State = BlockerUnparseable } ] }

        Assert.Empty(derive [ i ])

    [<Fact>]
    let ``one OPEN blocker and BLOCKER-CLEARED does not fire`` () =
        let i =
            { item 1 with
                Status = Blocked
                Blockers = [ blocker 2 BlockerClosed; blocker 3 BlockerOpen ] }

        Assert.Empty(derive [ i ])

    [<Fact>]
    let ``Blocked with NO blockers is not BLOCKER-CLEARED — there is nothing observed to have cleared`` () =
        // `forall` over an empty list is vacuously true, which would make every blocker-less Blocked item a
        // chore. That is the #266 shape — a verdict over a subject that does not exist.
        let i = { item 1 with Status = Blocked; Blockers = [] }
        Assert.Empty(derive [ i ])

    // ---- STATUS-NOT-BLOCKED: do not advertise work that cannot start ---------------------------------

    [<Fact>]
    let ``STATUS-NOT-BLOCKED fires when an OPEN blocker sits under a Ready column`` () =
        let i = { item 1 with Status = Ready; Blockers = [ blocker 2 BlockerOpen ] }
        Assert.Equal<string list>([ "STATUS-NOT-BLOCKED" ], rules [ i ])

    [<Fact>]
    let ``an UNKNOWN blocker alone does NOT write Blocked — a column stamped from a failed read (#266)`` () =
        // It blocks the SCHEDULER (fail closed, in `Schedulability`). It does not license a WRITE: those are
        // different acts, and only one of them is reversible by the next scan.
        let i = { item 1 with Status = Ready; Blockers = [ blocker 2 BlockerUnknown ] }
        Assert.Empty(derive [ i ])

    // ---- the RESERVER owns the scheduling column — where the rules meet each other (#331, #461, #581) --
    //
    // Every rule above is tested in isolation, on an UNCLAIMED item. These are the cases where two rules
    // look at one item at the same time, and they are where both of this module's real defects lived: the
    // corpus tested rules, not their interactions.

    [<Fact>]
    let ``a claimed, Ready, BLOCKED item derives ONE chore — never two that write opposite columns`` () =
        // The defect: this derived CLAIM-STATUS-LAG ("set In progress") **and** STATUS-NOT-BLOCKED ("set
        // Blocked") at once. Two callers draining the queue wrote opposite columns to one item and the
        // winner was whoever got there first — a board whose answer depends on a race.
        let i =
            { item 1 with
                Status = Ready
                Claim = Some(claim other, LeaseHeld)
                Blockers = [ blocker 2 BlockerOpen ] }

        Assert.Equal<string list>([ "CLAIM-STATUS-LAG" ], rules [ i ])

    [<Fact>]
    let ``BLOCKER-CLEARED does not flip a HOLDER's deliberate Blocked — their column wins (#331)`` () =
        // The worker hit the blocker, set the column per the protocol, and has not released yet. The blocker
        // then closed. Writing Ready here overwrites their judgement with a default — and it is the same
        // stomp CLAIM-STATUS-LAG already refuses to make, so making it here contradicts this module itself.
        let i =
            { item 1 with
                Status = Blocked
                Claim = Some(claim other, LeaseHeld)
                Blockers = [ blocker 2 BlockerClosed ] }

        Assert.Empty(derive [ i ])

    [<Fact>]
    let ``STATUS-NOT-BLOCKED does not fire on a reserved item — a claim reserves it, so nothing advertises it`` () =
        let i =
            { item 1 with
                Status = Backlog
                Claim = Some(claim other, LeaseHeld)
                Blockers = [ blocker 2 BlockerOpen ] }

        Assert.Equal<string list>([ "CLAIM-STATUS-LAG" ], rules [ i ])

    [<Fact>]
    let ``a STALE marker still owns the column — the lock breaks on reap, not on the clock (#461, #581)`` () =
        // Deference to a lapsed lease costs nothing: STALE-CLAIM collects the marker and restores the column
        // (#481), and the blocker rules fire on the next pass. It converges, and it never races the holder.
        let i =
            { item 1 with
                Status = Blocked
                Claim = Some(claim other, LeaseExpiredNoPr)
                Blockers = [ blocker 2 BlockerClosed ] }

        Assert.Equal<string list>([ "STALE-CLAIM" ], rules [ i ])

    [<Fact>]
    let ``once the claim is gone the blocker rules fire again — deference DEFERS, it does not suppress`` () =
        let i = { item 1 with Status = Blocked; Claim = None; Blockers = [ blocker 2 BlockerClosed ] }
        Assert.Equal<string list>([ "BLOCKER-CLEARED" ], rules [ i ])

    [<Fact>]
    let ``no item ever derives two chores that both write Status — the remedies cannot contradict`` () =
        // The invariant behind the two defects above, stated over every combination rather than by example:
        // whatever is wrong with an item, at most one chore may want to write its column. STALE-CLAIM is the
        // one that restores a column rather than setting one, so it is excluded from the count.
        let statuses = [ NoStatus; Backlog; Ready; InProgress; Blocked; InReview; Done ]
        let claims = [ None; Some(claim other, LeaseHeld); Some(claim other, LeaseExpiredNoPr); Some(claim other, LivenessUnknown) ]
        let blockerSets = [ []; [ blocker 2 BlockerOpen ]; [ blocker 2 BlockerClosed ]; [ blocker 2 BlockerMerged ]; [ blocker 2 BlockerUnknown ] ]

        for st in statuses do
            for cl in claims do
                for bs in blockerSets do
                    for state in [ Open; Closed ] do
                        let i = { item 1 with Status = st; State = state; Claim = cl; Blockers = bs }

                        let writers =
                            derive [ i ]
                            |> List.filter (fun c ->
                                match c.Kind with
                                | StaleClaim _ -> false
                                | _ -> true)

                        Assert.True(
                            writers.Length <= 1,
                            $"status=%A{st} state=%A{state} claim=%b{cl.IsSome} blockers=%d{bs.Length} derived %d{writers.Length} column-writing chores: %A{writers |> List.map (fun c -> c.Kind.RuleId)}"
                        )

    // ---- what is NOT a chore: fixes only ever write to the BOARD -------------------------------------

    [<Fact>]
    let ``UNCLAIMED-IN-PROGRESS is never a chore — someone working outside the protocol needs a human`` () =
        let i = { item 1 with Status = InProgress; Claim = None }
        Assert.Empty(derive [ i ])

    [<Fact>]
    let ``UNDECLARED-PATHS is never a chore — the fix is an ISSUE edit, and chores never write to issues`` () =
        let i = { item 1 with TouchSet = Undeclared }
        Assert.Empty(derive [ i ])

    [<Fact>]
    let ``DONE-STATUS-OPEN-ISSUE is never a chore — was the flip premature? is a judgement call`` () =
        let i = { item 1 with State = Open; Status = Done }
        Assert.Empty(derive [ i ])

    [<Fact>]
    let ``an unreadable touch-set is never a chore — we did not read the body, so we observed nothing`` () =
        let i = { item 1 with TouchSet = Unreadable "rate limited" }
        Assert.Empty(derive [ i ])

    [<Fact>]
    let ``a healthy board yields no chores at all`` () =
        Assert.Empty(derive [ item 1; item 2; item 3 ])

    // ---- condition 3: the safe point --------------------------------------------------------------

    [<Fact>]
    let ``a worker mid-lease is NOT at a safe point — never hand a live touch-set an unbounded side-quest`` () =
        let mine = { item 1 with Status = InProgress; Claim = Some(claim worker, LeaseHeld) }
        Assert.True((safePoint AtNext worker [ mine ]).IsNone)

    [<Fact>]
    let ``an idle worker IS at a safe point`` () =
        Assert.True((safePoint AtNext worker [ item 1 ]).IsSome)

    [<Fact>]
    let ``ANOTHER worker's live claim does not stop US being idle`` () =
        let theirs = { item 1 with Status = InProgress; Claim = Some(claim other, LeaseHeld) }
        Assert.True((safePoint AtNext worker [ theirs ]).IsSome)

    [<Fact>]
    let ``our own STALE claim still holds us mid-item — the lock breaks on reap, not on the clock (#461, #581)`` () =
        // The lease is a clock; the lock is a marker. Until it is collected our touch-set is still reserved,
        // so we are not idle — however loudly the clock says otherwise.
        let mine = { item 1 with Claim = Some(claim worker, LeaseExpiredNoPr) }
        Assert.True((safePoint AtNext worker [ mine ]).IsNone)

    [<Fact>]
    let ``our claim whose liveness we could not read still holds us — could-not-look is not idle (#266)`` () =
        let mine = { item 1 with Claim = Some(claim worker, LivenessUnknown) }
        Assert.True((safePoint AtNext worker [ mine ]).IsNone)

    [<Fact>]
    let ``AfterDone is a safe point once the claim is dropped (#533)`` () =
        // `done --flip` drops the marker, so the worker is idle by the time it is offered anything.
        Assert.True((safePoint AfterDone worker [ item 1 ]).IsSome)

    // ---- condition 3: bounded, and condition 1's other half: agreement -------------------------------

    [<Fact>]
    let ``offer hands back AT MOST ONE chore, however much is wrong`` () =
        let board =
            [ { item 1 with Claim = Some(claim other, LeaseExpiredNoPr) }
              { item 2 with State = Closed }
              { item 3 with Status = Blocked; Blockers = [ blocker 9 BlockerClosed ] } ]

        let at = (safePoint AtNext worker board).Value
        Assert.Equal(3, (derive board).Length)
        Assert.True((offer at).IsSome)

    [<Fact>]
    let ``the unlucky caller does not pay for everybody's garbage collection — 40 findings, one offer`` () =
        let board = [ for n in 1..40 -> { item n with State = Closed } ]
        let at = (safePoint AtNext worker board).Value
        Assert.Equal(40, (derive board).Length)
        Assert.Equal<string list>([ ".github#1" ], [ (offer at).Value.Subject.Short ])

    [<Fact>]
    let ``the offer is scoped to the board the idleness was OBSERVED on — evidence and subject are one value`` () =
        // `offer` takes no `items` argument, so a `SafePoint` minted from one board cannot be spent on
        // another. If it could, a caller could prove it was idle on an empty board and then be handed chores
        // from a board it holds a live lease on — condition 3 defeated by an argument list.
        let board = [ { item 1 with State = Closed } ]
        let at = (safePoint AtNext worker board).Value
        Assert.Equal(".github#1", (offer at).Value.Subject.Short)

    [<Fact>]
    let ``STALE-CLAIM is offered first — an unused reservation holds startable work off the board (#601)`` () =
        let board =
            [ { item 1 with State = Closed }
              { item 2 with Claim = Some(claim other, LeaseExpiredNoPr) } ]

        let at = (safePoint AtNext worker board).Value
        Assert.Equal("STALE-CLAIM", (offer at).Value.Kind.RuleId)

    [<Fact>]
    let ``two callers deriving the same board agree about what is next — the order is TOTAL (#464)`` () =
        // Not cosmetic. Under a fan-out the offer order IS the contention pattern: if two callers disagree
        // about what is next they take different locks and the queue drains in a different order every pass.
        let board = [ for n in [ 7; 3; 9; 1 ] -> { item n with State = Closed } ]
        let at = (safePoint AtNext worker board).Value
        let reversed = List.rev board
        let atR = (safePoint AtNext worker reversed).Value
        Assert.Equal((offer at).Value.Id, (offer atR).Value.Id)

    // ---- condition 2: verifiable, not merely reported ------------------------------------------------

    [<Fact>]
    let ``a chore is retired when its CONDITION is observably gone — never when somebody reports it done`` () =
        let before = [ { item 1 with State = Closed } ]
        let c = (derive before).Head
        Assert.False(isRetired c before)
        // The remedy landed: the column now matches the issue.
        let after = [ { item 1 with State = Closed; Status = Done } ]
        Assert.True(isRetired c after)

    [<Fact>]
    let ``a chore whose remedy silently did nothing is NOT retired — the fix for fail-open must not fail open (#510)`` () =
        // This is the whole of condition 2. #510: `set-field` said "queued" and dropped the write, and
        // `flush` then reported success and confirmed the lie. Re-deriving cannot be lied to.
        let before = [ { item 1 with State = Closed } ]
        let c = (derive before).Head
        Assert.False(isRetired c before)

    [<Fact>]
    let ``chores are IDEMPOTENT by construction — the same condition derives the same id every pass`` () =
        let board = [ { item 1 with State = Closed }; { item 2 with Claim = Some(claim other, LeaseExpiredNoPr) } ]
        Assert.Equal<string list>(ids board, ids board)

    [<Fact>]
    let ``two workers performing the same chore CONVERGE — the second finds it retired, not duplicated (#463)`` () =
        let before = [ { item 1 with Status = Blocked; Blockers = [ blocker 2 BlockerClosed ] } ]
        let c = (derive before).Head
        let after = [ { item 1 with Status = Ready; Blockers = [ blocker 2 BlockerClosed ] } ]
        Assert.True(isRetired c after)

    [<Fact>]
    let ``a chore id names its rule AND its subject, so it is stable across passes and unique per condition`` () =
        let board = [ { item 42 with State = Closed } ]
        Assert.Equal<string list>([ "CLOSED-ISSUE-NOT-DONE:FS-GG/.github#42" ], ids board)

    [<Fact>]
    let ``the statement names the subject and the remedy — an offer a worker can decline on the facts`` () =
        let board = [ { item 42 with State = Closed } ]
        let c = (derive board).Head
        Assert.Contains(".github#42", c.Statement)
        Assert.Contains("set Status to Done", c.Statement)
        Assert.Equal("quick", c.Size.Label)
