namespace FS.GG.Coord.Tests

open FSharp.Reflection
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
          PathRepo = (ref n).Repo
          Status = Ready
          State = Open
          TouchSet = Declared [ Matchable "src/" ]
          Blockers = []
          Claim = None
          ItemPr = None
          ItemPrUnreadable = false
          HumanBlock = None
          Predicate = None
          Class = None
          BoardClass = None
          Severity = Unset
          Phase = None
          AgeDays = None }

    /// Every case of a union, by reflection — the sweep's axes are DERIVED, never typed out.
    ///
    /// `TypesTests` says why in as many words, about the vocabulary this file's sweep quantifies over: *"a
    /// hand-written list is the fifth copy of the vocabulary wearing a different hat — correct on the day it
    /// was typed and silently short by one the day a case is added."* An exhaustive sweep is exactly where
    /// that bites hardest, because a missing axis value does not fail: it narrows the state space in silence
    /// and the test still reports green over the combination it stopped generating. This sweep was written
    /// hand-listed and was already short by two (`LeaseExpiredPrOpen`, `BlockerUnparseable`) on the day it
    /// merged, which is the whole demonstration.
    ///
    /// `fill` supplies a value for the one case that carries a field (`LeaseExpiredPrOpen of pr: int`) and
    /// THROWS on a field type it does not know. That is deliberate and it fails CLOSED: a new case with an
    /// unfamiliar field stops this suite rather than quietly dropping itself from the sweep.
    let private everyCaseOf<'T> (fill: System.Type -> obj) : 'T list =
        FSharpType.GetUnionCases typeof<'T>
        |> Array.map (fun c -> FSharpValue.MakeUnion(c, c.GetFields() |> Array.map (fun f -> fill f.PropertyType)) :?> 'T)
        |> Array.toList

    let private noFields (t: System.Type) : obj =
        failwith $"a new union case carries a %s{t.Name} field — teach the sweep to build it, do not let it drop out"

    let private everyStatus: BoardStatus list = everyCaseOf<BoardStatus> noFields

    let private everyBlockerState: BlockerState list = everyCaseOf<BlockerState> noFields

    let private everyLiveness: Liveness list =
        everyCaseOf<Liveness> (fun t -> if t = typeof<int> then box 42 else noFields t)

    /// `None` and every `ItemClass`, because BOTH class fields are options and the interesting cases are
    /// the disagreements between them (.github#1588). Derived from the union like every other axis, so a
    /// fourth class widens the sweep rather than escaping it.
    let private everyClassValue: ItemClass option list =
        None :: (everyCaseOf<ItemClass> noFields |> List.map Some)

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
    let ``a lapsed lease with a pushed BRANCH but no PR is NOT a chore — work in progress (#1055)`` () =
        // A pushed `item/<n>-*` branch is proof of life before §5 opens the PR. Offering it would hand the
        // reaper a chore `Writes.reapable` must then refuse (WorkAliveBranch) — the same never-drains queue
        // as the open-PR case above.
        let i = { item 1 with Claim = Some(claim other, LeaseExpiredBranchPushed) }
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
        // `Status = Ready`, and the column is the point rather than a detail. This test used to say `Done` —
        // the ONE value of seven that made `item.Status <> Done` false and so kept CLOSED-ISSUE-NOT-DONE
        // from firing alongside. On every other column the pair derived together and wrote opposite things:
        // STALE-CLAIM restores what the claim overwrote, CLOSED-ISSUE-NOT-DONE sets `Done`. The rule under
        // test was right; the fixture had picked the one column that could not see the rule next to it.
        let i =
            { item 1 with
                State = Closed
                Status = Ready
                Claim = Some(claim other, LeaseExpiredNoPr) }

        Assert.Equal<string list>([ "STALE-CLAIM" ], rules [ i ])

    [<Fact>]
    let ``a CLOSED issue's column waits for the reserver — CLOSED-ISSUE-NOT-DONE defers, it does not race`` () =
        // The live-lease half of the same rule. The holder closed the issue and is about to `done --flip` it;
        // handing anyone a chore to write `Done` underneath them is the race #331 forbids, and the column is
        // theirs until they release. Deference DEFERS: once the marker is gone the rule fires (below).
        let held = { item 1 with State = Closed; Status = InReview; Claim = Some(claim other, LeaseHeld) }
        Assert.Empty(derive [ held ])

        Assert.Equal<string list>([ "CLOSED-ISSUE-NOT-DONE" ], rules [ { held with Claim = None } ])

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

    // ---- BLOCKER-CLEARED is ALSO gated on the item's declared registry predicate (ADR-0050 call-site B) --
    //
    // A recorded blocker can be a PROXY for the item's real acceptance predicate — FS.GG.Rendering#923's
    // "WI-2 (Game publishes the skill)" closing would flip it to `Ready` while the semantic dependency (the
    // registry row exists AND the owning manifest agrees) is not satisfied. So an item that DECLARES a
    // machine-checkable predicate does not flip on blockers-cleared alone: the resolved verdict must Agrees.
    // The verdict is a FACT on the item (`Item.Predicate`), resolved at the impure edge, so `derive` reads
    // it exactly as it reads `Blocker.State` and stays pure. `None` — no declared predicate — is ungated.

    [<Fact>]
    let ``BLOCKER-CLEARED still fires when the declared predicate AGREES — blockers cleared AND row agrees`` () =
        let i =
            { item 1 with
                Status = Blocked
                Blockers = [ blocker 2 BlockerClosed ]
                Predicate = Some RegistryPredicate.Agrees }

        Assert.Equal<string list>([ "BLOCKER-CLEARED" ], rules [ i ])

    [<Fact>]
    let ``a CONTRADICTS predicate HOLDS the item — a proxy blocker closing cannot fake readiness (ADR-0050)`` () =
        // The motivating bug: every blocker resolves, but the owning manifest declares a DIFFERENT value, so
        // the item is not actually ready. Flipping it to Ready would advertise unstartable work.
        let i =
            { item 1 with
                Status = Blocked
                Blockers = [ blocker 2 BlockerClosed ]
                Predicate = Some(RegistryPredicate.Contradicts("false", "owner declares false")) }

        Assert.Empty(derive [ i ])

    [<Fact>]
    let ``an UNKNOWN predicate FAILS CLOSED — could-not-evaluate is not the-predicate-holds (#266, #421)`` () =
        // The same fail-closed shape a `BlockerUnknown` already gives BLOCKER-CLEARED, one step out: a
        // predicate we could not evaluate must hold the item, never advance it.
        let i =
            { item 1 with
                Status = Blocked
                Blockers = [ blocker 2 BlockerClosed ]
                Predicate = Some(RegistryPredicate.Unknown "owning manifest could not be read") }

        Assert.Empty(derive [ i ])

    [<Fact>]
    let ``NO declared predicate is UNGATED — blockers-cleared flips it exactly as today (ADR-0050 decision 5)`` () =
        // The common case, and the boundary the ADR draws: a general item has no machine-checkable predicate,
        // and inventing one for it is out of scope. `None` must not be read as a failing verdict.
        let i =
            { item 1 with
                Status = Blocked
                Blockers = [ blocker 2 BlockerClosed ]
                Predicate = None }

        Assert.Equal<string list>([ "BLOCKER-CLEARED" ], rules [ i ])

    [<Fact>]
    let ``the predicate gate does NOT reach an item whose blockers still HOLD — the blocker gate is first`` () =
        // An Agrees predicate cannot manufacture a flip on its own: BLOCKER-CLEARED still requires every
        // blocker resolved. The predicate is a NARROWING of the flip condition, never a second way to trigger.
        let i =
            { item 1 with
                Status = Blocked
                Blockers = [ blocker 2 BlockerOpen ]
                Predicate = Some RegistryPredicate.Agrees }

        Assert.Empty(derive [ i ])

    [<Fact>]
    let ``the predicate is read ONLY by BLOCKER-CLEARED — a Contradicts on a Ready item does not gate STATUS-NOT-BLOCKED`` () =
        // The gate is scoped to the Blocked→Ready flip. A Ready item with an open blocker is STATUS-NOT-BLOCKED
        // whatever its predicate says — ADR-0050 gates the flip, not the reverse.
        let i =
            { item 1 with
                Status = Ready
                Blockers = [ blocker 2 BlockerOpen ]
                Predicate = Some(RegistryPredicate.Contradicts("false", "owner declares false")) }

        Assert.Equal<string list>([ "STATUS-NOT-BLOCKED" ], rules [ i ])

    // ---- BLOCKER-CLEARED must RESPECT the human-park sentinel it would otherwise overwrite (#1644) ----
    //
    // ADR-0045's `Blocked on: human/...` body line is how the board says "a PERSON must act before this is
    // startable". `Schedulability` step 3b already refuses to HAND such an item out. This rule could still
    // WRITE `Ready` onto it — and that write is the whole incident: a promotion nothing reverses (with every
    // blocker resolved, STATUS-NOT-BLOCKED cannot push it back; once it is not `Blocked`, `BLOCKED-NO-REASON`
    // stops watching it), turning "a human must decide this" into "a worker may pick this up".
    //
    // The pair below is the shape #620 requires: the SAME fixture with and without the parking record. #620's
    // remedy — a `Blocked` row whose blockers are all closed is invisible to every scheduler, so promote it —
    // is correct and must stay firing for every item that carries no park.

    [<Fact>]
    let ``#1644 BLOCKER-CLEARED does NOT promote a row parked on a human DECISION — the sentinel it must respect`` () =
        let i =
            { item 1 with
                Status = Blocked
                Blockers = [ blocker 2 BlockerClosed ]
                HumanBlock = Some AwaitingHumanDecision }

        Assert.Empty(derive [ i ])

    [<Fact>]
    let ``#1887 BLOCKER-CLEARED does NOT promote a decision-class row with no sentinel`` () =
        // The live #1864 shape: concrete blockers cleared, no duplicated `Blocked on:
        // human/decision`, and the item's own Class body line is the hold. The board projection is stale
        // in the opposite direction to prove this gate reads the item, not the column.
        let i =
            { item 1864 with
                Status = Blocked
                Blockers = [ blocker 2 BlockerClosed ]
                HumanBlock = None
                Class = Some Decision
                BoardClass = Some Hardening }

        let derived = rules [ i ]
        Assert.DoesNotContain("BLOCKER-CLEARED", derived)
        Assert.Contains("CLASS-PROJECTION-LAG", derived)

    [<Fact>]
    let ``#1887 BLOCKER-CLEARED negative control still promotes the same non-decision row`` () =
        let i =
            { item 1864 with
                Status = Blocked
                Blockers = [ blocker 2 BlockerClosed ]
                HumanBlock = None
                Class = Some Hardening
                BoardClass = Some Hardening }

        Assert.Equal<string list>([ "BLOCKER-CLEARED" ], rules [ i ])

    [<Fact>]
    let ``#1887 every recorded decision row is held, including the eight without a sentinel`` () =
        // The eleven refs and sentinel states measured in #1887 AC3. The mechanism reconciles all eleven
        // without rewriting the eight sentinel-less bodies to duplicate their decision in a second
        // grammar.
        let recorded =
            [ "FS.GG.SDD", 754, true
              "FS.GG.SDD", 778, false
              "FS.GG.Game", 525, false
              ".github", 1737, true
              ".github", 1814, false
              ".github", 1843, false
              ".github", 1855, true
              ".github", 1860, false
              ".github", 1861, false
              ".github", 1863, false
              ".github", 1864, false ]

        let rows =
            recorded
            |> List.map (fun (repo, n, hasSentinel) ->
                { item n with
                    Ref = { Owner = "FS-GG"; Repo = repo; Number = n }
                    Status = Blocked
                    Blockers = [ blocker 2 BlockerClosed ]
                    HumanBlock =
                        if hasSentinel then Some AwaitingHumanDecision else None
                    Class = Some Decision
                    BoardClass = Some Decision })

        Assert.DoesNotContain("BLOCKER-CLEARED", rules rows)

    [<Fact>]
    let ``#1644 the SAME fixture WITHOUT the parking record still promotes — #620's remedy is intact`` () =
        // The other half of the pair, and the one that makes the half above a NARROWING rather than a
        // deletion. Identical in every field but `HumanBlock`.
        let i =
            { item 1 with
                Status = Blocked
                Blockers = [ blocker 2 BlockerClosed ]
                HumanBlock = None }

        Assert.Equal<string list>([ "BLOCKER-CLEARED" ], rules [ i ])

    [<Fact>]
    let ``#1644 EVERY human-park sentinel holds the flip — derived from the union, so a third case cannot escape`` () =
        // Hand-listing the two cases would be the fifth copy of the vocabulary this module keeps refusing to
        // write: correct today, and silently short by one the day ADR-0045 grows a `human/review`. Derived
        // from the union, an added case widens this loop instead of slipping past it — and `noFields` throws
        // on a case carrying a field rather than dropping it.
        let sentinels = everyCaseOf<HumanBlock> noFields

        // NON-VACUITY. An empty list satisfies the `for` below by iterating nothing, and this test would
        // report green over a sentinel set it never generated — the #266 shape, inside the guard for it.
        Assert.NotEmpty(sentinels)

        for hb in sentinels do
            let i =
                { item 1 with
                    Status = Blocked
                    Blockers = [ blocker 2 BlockerClosed ]
                    HumanBlock = Some hb }

            Assert.True(
                List.isEmpty (derive [ i ]),
                $"a %A{hb} park was PROMOTED: %A{derive [ i ] |> List.map (fun c -> c.Kind.RuleId)}"
            )

    [<Fact>]
    let ``#1644 an UNREAD body HOLDS the flip — HumanBlock=None cannot tell "no sentinel" from "we did not look" (#266)`` () =
        // THE FAIL-CLOSED LEG, and the one that makes this a fix rather than a half of one. `Snapshot.parse`
        // renders an unreadable body as `HumanBlock = None` — the same value as "declares no sentinel" —
        // because `HumanBlock option` has nowhere to put "I could not look". A gate that keyed on
        // `HumanBlock.IsSome` alone would therefore promote a PARKED row whose body read failed, which is
        // the fail-open wearing the fix's clothes.
        //
        // `TouchSet.Unreadable` is the fact that disambiguates: it is "we did not read the body", parsed off
        // the SAME body by the SAME parse, and `Client.enrichBoardFacts` already consults it for exactly
        // this collapse on `Class`. Note `HumanBlock = None` here — that is the point: the hold comes from
        // the touch-set, not from a sentinel we can see.
        let i =
            { item 1 with
                Status = Blocked
                Blockers = [ blocker 2 BlockerClosed ]
                HumanBlock = None
                TouchSet = Unreadable "rate limited" }

        Assert.Empty(derive [ i ])

    [<Fact>]
    let ``#1644 a body we DID read with no sentinel still promotes — the hold is the failed read, not the field`` () =
        // The mate of the leg above, over the same axis. Without it, "unread holds the flip" is satisfied by
        // a rule that holds the flip for EVERY touch-set, and the fail-closed claim would be untestable.
        for ts in [ Undeclared; DeclaredNone; DeclaredChore; Declared [ Matchable "src/" ] ] do
            let i =
                { item 1 with
                    Status = Blocked
                    Blockers = [ blocker 2 BlockerClosed ]
                    HumanBlock = None
                    TouchSet = ts }

            Assert.Equal<string list>([ "BLOCKER-CLEARED" ], rules [ i ])

    // ---- .github#2220: the REMEDY's column is read off the touch-set, not fixed at `Ready` ------------
    //
    // MEASURED live on `.github#1858`, the board's only `Severity: Critical` row. Its one blocker cleared,
    // the chore offered *"set Status to Ready"*, and the row declares `Paths: none` — `DeclaredNone`, which
    // `Types.fsi:130-132` documents as "unschedulable BY DESIGN". `Ready` is the one column
    // `columnStartability` calls `AlwaysStartable`, so applying that remedy would have put a row at the head
    // of the queue that `batch`/`take` list as a candidate and then decline forever. The worker declined the
    // chore by hand; nothing in the code would have stopped it.
    //
    // THE GATES ABOVE ALL HOLD THE ROW; THIS ONE REDIRECTS IT, and the tests below are shaped by that
    // difference. `Blocked` really is stale once the blockers resolve, so declining to write anything would
    // leave the lie in place — the fix has to choose a DIFFERENT column, not refuse. So every leg comes in a
    // pair: what the redirected populations now get, and the unchanged `Ready` for everything else.

    [<Fact>]
    let ``.github#2220 a `Paths: none` row is cleared to Backlog, NOT Ready — Ready is a column no scheduler can admit`` () =
        let i =
            { item 1858 with
                Status = Blocked
                Blockers = [ blocker 2 BlockerClosed ]
                TouchSet = DeclaredNone }

        // The chore still FIRES — the stale `Blocked` is real and something must clear it.
        Assert.Equal<string list>([ "BLOCKER-CLEARED" ], rules [ i ])
        Assert.Equal<(string * string) option>(Some("Status", statusWireName Backlog), (List.head (derive [ i ])).Kind.Write)

    [<Fact>]
    let ``.github#2220 a row with NO `Paths:` line is cleared to Backlog too — same board facts, different repair`` () =
        let i =
            { item 1 with
                Status = Blocked
                Blockers = [ blocker 2 BlockerClosed ]
                TouchSet = Undeclared }

        Assert.Equal<string list>([ "BLOCKER-CLEARED" ], rules [ i ])
        Assert.Equal<(string * string) option>(Some("Status", statusWireName Backlog), (List.head (derive [ i ])).Kind.Write)

    [<Fact>]
    let ``.github#2220 AC3 the OMISSION and the DECISION are told apart in the statement — collapsing them re-opens #496`` () =
        // `Types.fsi:127-140` exists to separate "somebody forgot" from "somebody decided". They take the
        // same column here, so the COLUMN cannot carry the distinction and the sentence must — a worker
        // reading the offer acts differently on each: one is an issue edit somebody owes, the other is
        // nothing to repair at all.
        let statementFor ts =
            let i =
                { item 1 with
                    Status = Blocked
                    Blockers = [ blocker 2 BlockerClosed ]
                    TouchSet = ts }

            (List.head (derive [ i ])).Statement

        let omission = statementFor Undeclared
        let decision = statementFor DeclaredNone

        Assert.NotEqual<string>(omission, decision)

        // Each names the fact that produced it, so the two cannot be told apart only by being different.
        Assert.Contains("no `Paths:` line", omission)
        Assert.Contains("UNDECLARED-PATHS", omission)
        Assert.Contains("`Paths: none`", decision)

        // And NEITHER promises the column it is not writing — the sentence and `Write` are the same value.
        for s in [ omission; decision ] do
            Assert.Contains("set Status to Backlog", s)
            Assert.DoesNotContain("set Status to Ready", s)

    [<Fact>]
    let ``.github#2220 negative control: an ordinary Declared row still goes to Ready — #620's remedy is intact`` () =
        // The half that makes the two above a NARROWING rather than a deletion. Identical in every field
        // but the touch-set.
        let i =
            { item 1 with
                Status = Blocked
                Blockers = [ blocker 2 BlockerClosed ]
                TouchSet = Declared [ Matchable "src/" ] }

        Assert.Equal<string list>([ "BLOCKER-CLEARED" ], rules [ i ])
        Assert.Equal<(string * string) option>(Some("Status", statusWireName Ready), (List.head (derive [ i ])).Kind.Write)
        Assert.Contains("set Status to Ready", (List.head (derive [ i ])).Statement)

    [<Fact>]
    let ``.github#2220 `Paths: any` is DeclaredNone's SCHEDULABLE counterpart and still goes to Ready`` () =
        // The case a rule keyed on "reserves no files" would get wrong. `DeclaredChore` reserves nothing
        // EXACTLY as `DeclaredNone` does — and ADR-0045 makes it schedulable anyway, which is why
        // `Types.fsi` splits them. Keying on emptiness instead of on the case would park every file-less
        // chore in `Backlog`.
        let i =
            { item 1 with
                Status = Blocked
                Blockers = [ blocker 2 BlockerClosed ]
                TouchSet = DeclaredChore }

        Assert.Equal<(string * string) option>(Some("Status", statusWireName Ready), (List.head (derive [ i ])).Kind.Write)

    [<Fact>]
    let ``.github#2220 an UNREAD body picks no column at all — a destination chosen from a failed read (#266)`` () =
        // The fail-closed leg. `humanHoldAllowsFlip` already holds every `Unreadable` row, so this is a
        // second lock on one door — spelled anyway, because a gate that is only correct while some OTHER
        // gate keeps its subject away breaks the first time the other gate moves.
        let i =
            { item 1 with
                Status = Blocked
                Blockers = [ blocker 2 BlockerClosed ]
                TouchSet = Unreadable "rate limited" }

        Assert.Empty(derive [ i ])

    [<Fact>]
    let ``.github#2220 THE GENERAL RULE: a chore may only write `Ready` onto a row Schedulability actually calls Startable`` () =
        // THE STATEMENT THE TWO SPECIAL CASES ARE INSTANCES OF, and the reason this is a fix rather than a
        // pair of patches. The defect is not that `Paths: none` is special — it is that the remedy asserted
        // a startability WITHOUT ASKING the one module that decides startability. So this asks it: put the
        // row in the column the chore chose and make `Schedulability.schedulable` agree.
        //
        // GATED ON `AlwaysStartable`, which is the precise claim. `Ready` ADVERTISES the row unconditionally,
        // so writing it is a promise that the scheduler will hand the row out. `Backlog` is
        // `WithBacklogOptIn` — a parking column that promises nothing — which is exactly why it is the right
        // destination for a row that is genuinely unblocked and genuinely not startable.
        //
        // Swept over every `TouchSet` case, derived from the union, so a SEVENTH case cannot default into
        // `Ready` and ship as a new unfillable lane. `noFields` throws on a field type it does not know
        // rather than dropping the case.
        let touchSets =
            everyCaseOf<TouchSet> (fun t ->
                if t = typeof<PathToken list> then box ([ Matchable "src/" ]: PathToken list)
                elif t = typeof<string> then box "rate limited"
                else noFields t)

        Assert.NotEmpty(touchSets)

        // NON-VACUITY, the other half. If no touch-set produced a chore that writes `Ready`, the loop below
        // would report green having asserted nothing at all — the #266 shape, inside the guard for it.
        let mutable everAdvertised = false

        for ts in touchSets do
            let i =
                { item 1 with
                    Status = Blocked
                    Blockers = [ blocker 2 BlockerClosed ]
                    TouchSet = ts }

            for c in derive [ i ] do
                match c.Kind.Write with
                | Some("Status", wire) ->
                    let column = everyStatus |> List.find (fun s -> statusWireName s = wire)

                    if Schedulability.columnStartability column = Schedulability.AlwaysStartable then
                        everAdvertised <- true

                        Assert.Equal<Schedulability.Schedulability>(
                            Schedulability.Startable,
                            Schedulability.schedulable false [] { i with Status = column }
                        )
                | _ -> ()

        Assert.True(everAdvertised, "no touch-set produced a chore writing an AlwaysStartable column — this test asserted nothing")

    [<Fact>]
    let ``#1644 the park gate does NOT reach an item whose blockers still HOLD — the blocker gate is first`` () =
        // The park NARROWS the flip condition; it is not a second thing that can trigger or suppress one.
        // An open blocker is still what governs, and the sentence a reader gets is still about the blocker.
        let i =
            { item 1 with
                Status = Blocked
                Blockers = [ blocker 2 BlockerOpen ]
                HumanBlock = Some AwaitingHumanDecision }

        Assert.Empty(derive [ i ])

    [<Fact>]
    let ``#1644 the park is read ONLY by BLOCKER-CLEARED — a parked Ready item still derives STATUS-NOT-BLOCKED`` () =
        // Scoped to the `Blocked → Ready` flip, exactly as the ADR-0050 predicate gate is. A parked row
        // wearing `Ready` over an OPEN blocker is still falsely advertised, and the rule that pushes it back
        // to `Blocked` must keep firing — suppressing it would leave the park sitting in the one column that
        // invites a worker, which is the failure this item is about, arrived at from the other side.
        let i =
            { item 1 with
                Status = Ready
                Blockers = [ blocker 2 BlockerOpen ]
                HumanBlock = Some AwaitingHumanDecision }

        Assert.Equal<string list>([ "STATUS-NOT-BLOCKED" ], rules [ i ])

    [<Fact>]
    let ``#1644 a parked CLOSED issue is still CLOSED-ISSUE-NOT-DONE — the park gates the flip, not the board`` () =
        // The park must not become a blanket "derive nothing about this item". A closed issue wearing a live
        // column is a projection error whatever its body says, and a human-park that suppressed it would be
        // a new way for the board to lie, introduced by the fix for a way it lied.
        let i =
            { item 1 with
                State = Closed
                Status = Ready
                HumanBlock = Some AwaitingHumanDecision }

        Assert.Equal<string list>([ "CLOSED-ISSUE-NOT-DONE" ], rules [ i ])

    // ---- BLOCKER-CLEARED must RESPECT the in-flight implementation step 5b refuses on (.github#1738) --
    //
    // `Schedulability` step 5b refuses a markerless row carrying an open `item/<n>-*` PR: an implementation
    // is already written, and offering the row costs a DUPLICATE one (#651). This rule's remedy is
    // `Status = Ready` — the one column `columnStartability` calls `AlwaysStartable`, hence the one that
    // ADVERTISES the row. So the scheduler refused and the chore promoted, and the write won: #1644's shape,
    // one field over. Measured three times in ONE board event — `FS.GG.Rendering#1086`/`#1089`/`#1092` when
    // `#1094` merged, each with a complete open PR.
    //
    // The pair below is #1644's shape and #620's requirement: the SAME fixture with and without the PR.

    [<Fact>]
    let ``#1738 BLOCKER-CLEARED does NOT promote a row whose item PR is already open — step 5b's refusal`` () =
        let i =
            { item 1 with
                Status = Blocked
                Blockers = [ blocker 2 BlockerClosed ]
                ItemPr = Some 1911 }

        Assert.Empty(derive [ i ])

    [<Fact>]
    let ``#1924 BLOCKER-CLEARED does NOT promote a row whose markerless PR probe was unreadable`` () =
        let i =
            { item 1 with
                Status = Blocked
                Blockers = [ blocker 2 BlockerClosed ]
                ItemPr = None
                ItemPrUnreadable = true }

        Assert.Empty(derive [ i ])

    [<Fact>]
    let ``#1738 the SAME fixture WITHOUT the open item PR still promotes — #620's remedy is intact`` () =
        // The other half of the pair, and the one that makes the half above a NARROWING rather than a
        // deletion. Identical in every field but `ItemPr`.
        let i =
            { item 1 with
                Status = Blocked
                Blockers = [ blocker 2 BlockerClosed ]
                ItemPr = None }

        Assert.Equal<string list>([ "BLOCKER-CLEARED" ], rules [ i ])

    [<Fact>]
    let ``#1738 the PR gate does NOT reach an item whose blockers still HOLD — the blocker gate is first`` () =
        // The in-flight PR NARROWS the flip condition; it is not a second thing that can trigger or suppress
        // one. An open blocker still governs, and the sentence a reader gets is still about the blocker.
        let i =
            { item 1 with
                Status = Blocked
                Blockers = [ blocker 2 BlockerOpen ]
                ItemPr = Some 1911 }

        Assert.Empty(derive [ i ])

    [<Fact>]
    let ``#1738 the item PR is read ONLY by BLOCKER-CLEARED — a Ready row with one still derives STATUS-NOT-BLOCKED`` () =
        // Scoped to the `Blocked → Ready` flip, exactly as the park and the predicate gates are. A row wearing
        // `Ready` over an OPEN blocker is still falsely advertised whatever PR is open on it, and the rule
        // that pushes it back to `Blocked` must keep firing — suppressing it would leave the row in the one
        // column that invites a worker, which is the failure this item is about, arrived at from the other
        // side.
        let i =
            { item 1 with
                Status = Ready
                Blockers = [ blocker 2 BlockerOpen ]
                ItemPr = Some 1911 }

        Assert.Equal<string list>([ "STATUS-NOT-BLOCKED" ], rules [ i ])

    [<Fact>]
    let ``#1738 a CLOSED issue with an open item PR is still CLOSED-ISSUE-NOT-DONE — the PR gates the flip`` () =
        // The in-flight PR must not become a blanket "derive nothing about this item". A closed issue wearing
        // a live column is a projection error whatever is open on its branch, and a gate that suppressed it
        // would be a new way for the board to lie, introduced by the fix for a way it lied.
        let i =
            { item 1 with
                State = Closed
                Status = Ready
                ItemPr = Some 1911 }

        Assert.Equal<string list>([ "CLOSED-ISSUE-NOT-DONE" ], rules [ i ])

    // ---- AC5: the OTHER column-writing rules, CHECKED rather than assumed (.github#1738) -------------
    //
    // #1644's general statement is *no mechanical remedy may overwrite a fact the scheduler refuses on*.
    // #1738's AC5 refuses to let that be restated per rule and asks it once, of every kind: is
    // `BLOCKER-CLEARED` special, or is this a family? The answer is that it is special, and `Write` is what
    // makes it so — it is the only kind whose remedy writes a column `columnStartability` calls startable.
    // The two legs below pin the two rules AC5 names by hand; the third derives the claim from the union so
    // a SIXTH kind cannot join the family unclassified.

    [<Fact>]
    let ``#2264 a held implementation PR advances through the lifecycle projector`` () =
        // TWO independent reasons, and this leg pins the structural one. `CLAIM-STATUS-LAG` lives in the
        // RESERVED branch, and `Scan` probes for a markerless `item/<n>-*` PR ONLY where there is no marker —
        // so on this branch `ItemPr` is `None` by construction and there is no refusal to contradict. (The
        // second reason is `Write`: it writes `In progress`, which `columnStartability` calls NeverStartable,
        // so even a populated field could not turn into an advertisement.) The fixture sets `ItemPr` anyway —
        // an impossible combination on purpose — because a leg that could only assert the rule on inputs the
        // scan cannot produce would be asserting the scan, not the rule.
        let i =
            { item 1 with
                Status = Ready
                Claim = Some(claim other, LeaseHeld)
                ItemPr = Some 1911 }

        Assert.Equal<string list>([ "CLAIM-REVIEW-LAG" ], rules [ i ])

    [<Fact>]
    let ``#1738 AC5 STATUS-NOT-BLOCKED cannot contradict step 5b — its write AGREES with a refusal, in the same direction`` () =
        // The other rule AC5 names, and the answer is NO for a different reason: it writes `Blocked`, which
        // moves the row FURTHER from startable, and it requires an OPEN blocker — on which `Schedulability`
        // step 3 already refuses, before step 5b is ever reached. So the scheduler's verdict on such a row is
        // `BlockedBy`, not `ItemPrOpen`, and the write agrees with it rather than overwriting it. A gate here
        // would suppress a correction the board needs.
        let i =
            { item 1 with
                Status = Ready
                Blockers = [ blocker 2 BlockerOpen ]
                ItemPr = Some 1911 }

        Assert.Equal<string list>([ "STATUS-NOT-BLOCKED" ], rules [ i ])

    [<Fact>]
    let ``#1738 AC5 BLOCKER-CLEARED is the ONLY kind that writes a STARTABLE column — derived, so a sixth kind cannot escape`` () =
        // THE GENERAL STATEMENT, ASSERTED ONCE. AC5 asks whether the other column-writing rules share this
        // contradiction; the reason they cannot is that a chore can only contradict a refusal by writing a
        // column that ADVERTISES the row, and exactly one kind does. Derived from the union by reflection —
        // the same refusal to hand-list a vocabulary the rest of this module makes — so a sixth `ChoreKind`
        // whose remedy writes `Ready` fails HERE rather than shipping ungated.
        //
        // `Write` is the one source for the field/value pair (`Chore.fsi`), and `columnStartability` is the
        // one source for which columns are startable (`Schedulability`, #1057). This joins them; it re-decides
        // neither.
        //
        // EVERY ARM IS CLASSIFIED, AND THE TWO `failwith`s ARE THE POINT. A `| _ -> false` here would exempt
        // exactly the cases nobody thought about: a kind whose `Write` is `None` (its remedy is DELEGATED —
        // `STALE-CLAIM`'s is `reap`, which restores `PreviousStatus`, and that can be `Ready`), and a
        // `Status` string no `BoardStatus` renders (a hand-written literal instead of `statusWireName`).
        // Both would answer "harmless" by DEFAULT, which is the fail-open this whole item is about.
        //
        // SWEPT OVER EVERY `ClearedDestination` — .github#2220. `BLOCKER-CLEARED`'s `Write` now varies with
        // the destination the derivation chose, so filling ONE value would assert this about a third of the
        // case and go green over the other two. The expected answer is the SAME for all three, and that is
        // the claim: which KIND can contradict a refusal is a property of the kind, not of the column it
        // happened to pick on the day the fixture was written.
        let destinations = everyCaseOf<ClearedDestination> noFields

        Assert.NotEmpty(destinations)

        let writesStartableColumn (k: ChoreKind) =
            match k.Write with
            | Some("Status", wire) ->
                match everyStatus |> List.tryFind (fun s -> statusWireName s = wire) with
                | Some s -> Schedulability.columnStartability s <> Schedulability.NeverStartable
                | None ->
                    failwith
                        $"%s{k.RuleId} writes Status=%s{wire}, which no BoardStatus renders — this test cannot classify it, and answering `not startable` by default is the fail-open it exists to catch"
            | Some(field, _) ->
                // Not a scheduling column at all (`CLASS-PROJECTION-LAG` writes `Class`). Named, not defaulted.
                Assert.Equal("Class", field)
                false
            | None ->
                // A DELEGATED remedy. `STALE-CLAIM` is the only one, and `reap` restoring `PreviousStatus`
                // really can write `Ready` — so this arm may not simply answer `false`. It cannot contradict
                // step 5b for a reason of its own: STALE-CLAIM fires ONLY on `LeaseExpiredNoPr`, a probe that
                // SUCCEEDED and found no PR (pinned by the first test in this file). A SECOND delegating kind
                // would need that argument made for it, so it fails here instead.
                Assert.Equal("STALE-CLAIM", k.RuleId)
                false

        for destination in destinations do
            let kinds =
                everyCaseOf<ChoreKind> (fun t ->
                    if t = typeof<WorkerId> then box (WorkerId "wren-0001")
                    elif t = typeof<BoardStatus> then box Ready
                    elif t = typeof<string list> then box ([ ".github#2" ]: string list)
                    elif t = typeof<ItemClass> then box (List.head (everyCaseOf<ItemClass> noFields))
                    elif t = typeof<ClearedDestination> then box destination
                    else noFields t)

            Assert.NotEmpty(kinds)

            let offenders = kinds |> List.filter writesStartableColumn |> List.map (fun k -> k.RuleId)

            Assert.Equal<string list>([ "BLOCKER-CLEARED" ], offenders)

    [<Fact>]
    let ``#1738 BLOCKER-CLEARED fires exactly where Blockers.cleared says — the predicate Scan probes on`` () =
        // THE SEAM, PINNED FROM THE CORE SIDE. `Scan` must probe for `Item.ItemPr` on exactly the rows this
        // rule can fire on; if its population ever drifts NARROWER, the #1738 gate stops seeing its subject
        // and goes green over the promotion it exists to refuse. Both sides now ask `Blockers.cleared`, and
        // this asserts the rule really is keyed on it rather than on a `forall` that merely agrees today —
        // the #1012 shape (two spellings pointing opposite ways, 775 tests green over the disagreement).
        //
        // Swept over every `BlockerState`, derived from the union, plus the EMPTY list — which is the case a
        // bare `forall` gets wrong, and the one `.github#1689`/`#1737` actually sit in.
        Assert.NotEmpty(everyBlockerState)

        let fires (blockers: Blocker list) =
            rules [ { item 1 with Status = Blocked; Blockers = blockers } ] = [ "BLOCKER-CLEARED" ]

        Assert.Equal(Blockers.cleared [], fires [])

        for st in everyBlockerState do
            let bs = [ blocker 2 st ]
            Assert.Equal(Blockers.cleared bs, fires bs)

            // ...and over a PAIR in BOTH ORDERS, so "every" is asserted rather than "the first" OR "the
            // last". One order alone refutes only the implementation that reads the other end: with the
            // swept state second, a first-blocker-only rule is caught and a last-blocker-only rule passes.
            let firstClosed = [ blocker 2 BlockerClosed; blocker 3 st ]
            Assert.Equal(Blockers.cleared firstClosed, fires firstClosed)

            let lastClosed = [ blocker 3 st; blocker 2 BlockerClosed ]
            Assert.Equal(Blockers.cleared lastClosed, fires lastClosed)

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
    let ``reflection can actually see the unions — the sweep below is not vacuous`` () =
        // If any of these came back empty, the `for` loops in the sweep would pass by iterating nothing and
        // this module would report green over a state space it never generated. `TypesTests` carries the
        // identical guard for the identical reason.
        Assert.Equal(7, List.length everyStatus)
        Assert.Equal(5, List.length everyBlockerState)
        Assert.Equal(5, List.length everyLiveness)
        Assert.Equal(4, List.length everyClassValue)

    [<Fact>]
    let ``an item derives AT MOST ONE chore PER FIELD — two writes to one field is a contradiction`` () =
        // The invariant behind the two defects above, stated over every combination rather than by example.
        //
        // **IT SAYS "PER FIELD" NOW, AND THAT IS A RESTATEMENT, NOT A NARROWING.** It used to say "at most
        // one chore", full stop, and justified the shorter sentence like this: *"Every one of the five kinds
        // has a remedy that writes `Status`, so 'at most one chore that writes the column' and 'at most one
        // chore' are the same sentence — and the shorter one cannot be quietly narrowed the way the longer
        // one was."* The two sentences were the same only because of that coincidence. `CLASS-PROJECTION-LAG`
        // (.github#1588) writes `Class`, so they have come apart, and a `Status` repair alongside a `Class`
        // projection on one item is two independent repairs rather than a contradiction — both land, in
        // either order, with the same result.
        //
        // **AND THE PER-FIELD GROUPING ALONE WOULD HAVE LOST THE ORIGINAL DEFECT — SO IT IS NOT ALONE.**
        // This is the trap, and it was nearly walked into: the exclusion this test once carried was for
        // STALE-CLAIM, "on the grounds that it restores a column rather than setting one". Restoring IS
        // writing — its remedy is `reap`, and `reap` restores `PreviousStatus` (#481) — and that exclusion
        // is exactly what hid a real contradiction for as long as it stood: on a CLOSED issue carrying a
        // STALE marker, STALE-CLAIM ("restore the column it overwrote") and CLOSED-ISSUE-NOT-DONE ("set
        // Done") both fired, writing opposite columns to one item.
        //
        // `Kind.Write` answers "what field does `reconcile` send", and for STALE-CLAIM that is honestly
        // `None` — its remedy is a marker collection. But `None` and `Some "Status"` are DISTINCT GROUPING
        // KEYS, so grouping by field puts that historical pair in two groups of one and passes. MEASURED:
        // reintroducing the old CLOSED-ISSUE-NOT-DONE exception verbatim leaves the per-field sweep GREEN
        // and reds only two by-example tests. The generalised guard — whose entire purpose is to catch the
        // contradiction nobody thought to write an example for — would have stopped covering the one
        // contradiction this repo actually shipped.
        //
        // So the field partition is the RELAXATION, and the assertion below it is the compensation: a chore
        // that writes NO field cannot coexist with any other chore at all. That is the true statement about
        // STALE-CLAIM — it does not reconcile a column, it ends the reservation that owns every column, so
        // nothing may be derived alongside it — and it restores exactly the coverage the grouping gave up.
        //
        // Every axis is DERIVED from its union (see `everyCaseOf`), so a case added tomorrow widens this
        // sweep instead of silently escaping it. Blocker sets go to every ORDERED PAIR, not a hand-picked
        // few: `List.forall` is what BLOCKER-CLEARED turns on, and a single-element set cannot tell `forall`
        // from `exists`. The two class fields are swept independently, because the projection rule fires on
        // their DISAGREEMENT and a sweep that moved them together would never generate one.
        let claims = None :: (everyLiveness |> List.map (fun l -> Some(claim other, l)))

        let blockerSets =
            [ yield []
              for a in everyBlockerState do
                  yield [ blocker 2 a ]
                  for b in everyBlockerState do
                      yield [ blocker 2 a; blocker 3 b ] ]

        let mutable derivedSomething = 0
        let mutable sawClassChore = 0

        for st in everyStatus do
            for cl in claims do
                for bs in blockerSets do
                    for state in [ Open; Closed ] do
                        for declared in everyClassValue do
                            for board in everyClassValue do
                                let i =
                                    { item 1 with
                                        Status = st
                                        State = state
                                        Claim = cl
                                        Blockers = bs
                                        Class = declared
                                        BoardClass = board
                                        Severity = Unset
                                        Phase = None
                                        AgeDays = None }

                                let derived = derive [ i ]
                                derivedSomething <- derivedSomething + derived.Length

                                sawClassChore <-
                                    sawClassChore
                                    + (derived
                                       |> List.filter (fun c -> c.Kind.RuleId = "CLASS-PROJECTION-LAG")
                                       |> List.length)

                                let axes =
                                    $"status=%A{st} state=%A{state} claim=%A{cl |> Option.map snd} blockers=%A{bs |> List.map (fun b -> b.State)} class=%A{declared} boardClass=%A{board}"

                                for field, group in derived |> List.groupBy (fun c -> c.Kind.Write |> Option.map fst) do
                                    Assert.True(
                                        List.length group <= 1,
                                        $"%s{axes} derived %d{List.length group} chores writing %A{field}: %A{group |> List.map (fun c -> c.Kind.RuleId)}"
                                    )

                                // A FIELDLESS CHORE IS EXCLUSIVE. `STALE-CLAIM` is the only one: it does not
                                // reconcile a column, it collects the marker that RESERVES the item, and
                                // while that reservation stands every column belongs to its holder (#331).
                                // So nothing may be derived beside it — and this is the assertion that keeps
                                // the CLOSED-issue-plus-stale-marker pair failing, which grouping by field
                                // no longer does on its own.
                                if derived |> List.exists (fun c -> c.Kind.Write.IsNone) then
                                    Assert.True(
                                        List.length derived = 1,
                                        $"%s{axes} derived a fieldless chore ALONGSIDE others: %A{derived |> List.map (fun c -> c.Kind.RuleId)}"
                                    )

        // NON-VACUITY, and it is not a formality: everything above is an UPPER bound, so `derive = fun _ -> []`
        // satisfies every assertion in this test. That is the same shape as the two touch-set tests below —
        // an emptiness this suite would have read as proof — and #266 is the epic about a check reporting
        // green over a subject it never saw. A sweep that asserts "never two" must also show it ever saw one.
        Assert.True(derivedSomething > 0, "the sweep derived NO chores at all — `at most one` proved nothing")

        // AND IT MUST HAVE SEEN THE NEW KIND SPECIFICALLY. The counter above would stay satisfied by the
        // five Status rules alone, so widening the sweep with two class axes could have added 16× the
        // combinations and exercised the rule they were added for exactly zero times — a sweep that grew
        // and proved nothing new. This is the axis's own non-vacuity guard.
        Assert.True(sawClassChore > 0, "the sweep never derived a CLASS-PROJECTION-LAG — the class axes proved nothing")

    [<Fact>]
    let ``#1588 a Status repair and a Class projection COEXIST — they are two repairs, not a contradiction`` () =
        // The positive statement of what "per field" bought, pinned by example so the restatement above is
        // not merely a weaker assertion nobody checked. A closed issue still carrying `Ready` needs its
        // column set to Done AND its declared class projected; neither write is the other's business, and
        // suppressing one to preserve "at most one chore" would leave a real repair underived forever.
        //
        // Note the item is OPEN: `CLASS-PROJECTION-LAG` is scoped to open rows, so the coexistence has to be
        // demonstrated against a rule that fires on one — `STATUS-NOT-BLOCKED` here.
        let i =
            { item 1 with
                Status = Ready
                State = Open
                Blockers = [ blocker 2 BlockerOpen ]
                Class = Some Defect
                BoardClass = None
                Severity = Unset
                Phase = None
                AgeDays = None }

        let derived = derive [ i ]

        Assert.Equal<string list>(
            [ "STATUS-NOT-BLOCKED"; "CLASS-PROJECTION-LAG" ],
            derived |> List.map (fun c -> c.Kind.RuleId) |> List.sort |> List.rev
        )

        // They write DIFFERENT fields — the property the invariant is actually about.
        Assert.Equal<string list>(
            [ "Class"; "Status" ],
            derived |> List.choose (fun c -> c.Kind.Write |> Option.map fst) |> List.sort
        )

    [<Fact>]
    let ``#1588 an item whose text declares NO class derives no projection — never a default`` () =
        // AC3, as an invariant. `Class = None` is the common case today (a board where nobody has written a
        // `Class:` line yet), and the one thing this rule must never do is stamp such a row with a class the
        // engine made up. `lint`'s CLASS-UNSET reports it to a human instead.
        for board in everyClassValue do
            let i = { item 1 with Class = None; BoardClass = board }
            Assert.Empty(derive [ i ] |> List.filter (fun c -> c.Kind.RuleId = "CLASS-PROJECTION-LAG"))

    [<Fact>]
    let ``#1588 the projection RETIRES once the column agrees — or reconcile could never come clean`` () =
        // `Chore.isRetired` is what tells a caller a chore is discharged, and it is answered by re-deriving.
        // An unconditional write would re-derive forever against a write that landed, so `reconcile` would
        // report the same finding on every pass and never converge — the board permanently "dirty" for a
        // reason nobody could clear.
        for c in everyCaseOf<ItemClass> noFields do
            let agreeing = { item 1 with Class = Some c; BoardClass = Some c }
            Assert.Empty(derive [ agreeing ] |> List.filter (fun x -> x.Kind.RuleId = "CLASS-PROJECTION-LAG"))

            let lagging = { item 1 with Class = Some c; BoardClass = None }
            let chores = derive [ lagging ] |> List.filter (fun x -> x.Kind.RuleId = "CLASS-PROJECTION-LAG")
            Assert.Single chores |> ignore
            Assert.True(isRetired (List.head chores) [ agreeing ], "the chore did not retire against an agreeing board")

    [<Fact>]
    let ``#1588 a DISAGREEING column is rewritten - a wrong projection is not left standing`` () =
        // The case a naive `BoardClass.IsNone` guard would miss: the column says one thing, the item's text
        // says another. That is not "already projected" — it is the board asserting something the item does
        // not, which is worse than an empty column because a reader would believe it.
        let i = { item 1 with Class = Some Defect; BoardClass = Some Hardening }

        Assert.Equal<string list>(
            [ "CLASS-PROJECTION-LAG" ],
            derive [ i ] |> List.map (fun c -> c.Kind.RuleId)
        )

        Assert.Equal<(string * string) option>(Some("Class", "defect"), (List.head (derive [ i ])).Kind.Write)

    // ---- what is NOT a chore: fixes only ever write to the BOARD -------------------------------------

    [<Fact>]
    let ``UNCLAIMED-IN-PROGRESS is never a chore — someone working outside the protocol needs a human`` () =
        let i = { item 1 with Status = InProgress; Claim = None }
        Assert.Empty(derive [ i ])

    [<Fact>]
    let ``DONE-STATUS-OPEN-ISSUE is never a chore — was the flip premature? is a judgement call`` () =
        let i = { item 1 with State = Open; Status = Done }
        Assert.Empty(derive [ i ])

    [<Fact>]
    let ``every rule but BLOCKER-CLEARED ignores the touch-set — Undeclared, none, chore and unread alike`` () =
        // Stated as INVARIANCE over a DEFECTIVE item, not as emptiness over a healthy one. This replaces two
        // tests (`UNDECLARED-PATHS is never a chore`, `an unreadable touch-set is never a chore`) that each
        // set one touch-set on an item with nothing else wrong and asserted `Assert.Empty`. Both passed
        // against `derive = fun _ -> []` — the empty list they asserted is the empty list a healthy item
        // yields whatever the touch-set is, so neither could tell "the rules deliberately ignore this field"
        // from "the rules did nothing at all". The claim in their names was never under test.
        //
        // Varying the touch-set across an item that DOES derive a chore is the same claim, made falsifiable:
        // the derivation must be identical for every case, and it goes red the day a rule starts reading the
        // field — which is exactly when somebody needs to be told, since the fix for a bad touch-set is an
        // ISSUE edit and chores only ever write to the BOARD.
        //
        // **AND THAT DAY CAME (.github#1644), SO THE TITLE OF THIS TEST CHANGED WITH THE RULE.** It read
        // "NO rule reads the touch-set", which the guard above was built to falsify — and it would NOT have
        // gone red, because the subject here is a CLOSED issue and the new reader (`BLOCKER-CLEARED`) needs
        // an OPEN one. A green test carrying a claim the code had stopped honouring is this repo's own named
        // defect class, so the claim is now the true one: `BLOCKER-CLEARED` reads `Unreadable` as a
        // BODY-READ RECEIPT (see the #1644 block above, which pins that half); every other rule ignores the
        // field, which is what this pins.
        let defective = { item 1 with State = Closed; Status = Ready }

        Assert.Equal<string list>([ "CLOSED-ISSUE-NOT-DONE" ], rules [ defective ])

        for ts in [ Undeclared; DeclaredNone; DeclaredChore; Declared [ Matchable "src/" ]; Unreadable "rate limited" ] do
            Assert.Equal<string list>([ "CLOSED-ISSUE-NOT-DONE" ], rules [ { defective with TouchSet = ts } ])

        // STATUS-NOT-BLOCKED too, over an OPEN item — the population `BLOCKER-CLEARED`'s new read actually
        // reaches. Pinning invariance only on a closed issue would leave "every OTHER rule" asserted about
        // the one state where no other rule could have read the field anyway.
        let advertised = { item 1 with State = Open; Status = Ready; Blockers = [ blocker 2 BlockerOpen ] }

        for ts in [ Undeclared; DeclaredNone; DeclaredChore; Declared [ Matchable "src/" ]; Unreadable "rate limited" ] do
            Assert.Equal<string list>([ "STATUS-NOT-BLOCKED" ], rules [ { advertised with TouchSet = ts } ])

    [<Fact>]
    let ``a healthy board yields no chores at all`` () =
        Assert.Empty(derive [ item 1; item 2; item 3 ])

    // ---- condition 3: the safe point --------------------------------------------------------------

    [<Fact>]
    let ``a worker mid-lease is NOT at a safe point — never hand a live touch-set an unbounded side-quest`` () =
        let mine = { item 1 with Status = InProgress; Claim = Some(claim worker, LeaseHeld) }
        Assert.True((safePoint AtNext worker (Whole [ mine ]) [ mine ]).IsNone)

    [<Fact>]
    let ``an idle worker IS at a safe point`` () =
        Assert.True((safePoint AtNext worker (Whole [ item 1 ]) [ item 1 ]).IsSome)

    [<Fact>]
    let ``ANOTHER worker's live claim does not stop US being idle`` () =
        let theirs = { item 1 with Status = InProgress; Claim = Some(claim other, LeaseHeld) }
        Assert.True((safePoint AtNext worker (Whole [ theirs ]) [ theirs ]).IsSome)

    [<Fact>]
    let ``our own STALE claim still holds us mid-item — the lock breaks on reap, not on the clock (#461, #581)`` () =
        // The lease is a clock; the lock is a marker. Until it is collected our touch-set is still reserved,
        // so we are not idle — however loudly the clock says otherwise.
        let mine = { item 1 with Claim = Some(claim worker, LeaseExpiredNoPr) }
        Assert.True((safePoint AtNext worker (Whole [ mine ]) [ mine ]).IsNone)

    [<Fact>]
    let ``our claim whose liveness we could not read still holds us — could-not-look is not idle (#266)`` () =
        let mine = { item 1 with Claim = Some(claim worker, LivenessUnknown) }
        Assert.True((safePoint AtNext worker (Whole [ mine ]) [ mine ]).IsNone)

    [<Fact>]
    let ``AfterDone is a safe point once the claim is dropped (#533)`` () =
        // `done --flip` drops the marker, so the worker is idle by the time it is offered anything.
        Assert.True((safePoint AfterDone worker (Whole [ item 1 ]) [ item 1 ]).IsSome)

    // ---- condition 3: bounded, and condition 1's other half: agreement -------------------------------

    // ---- #1086: a board that cannot SEE our claims cannot report us idle ---------------------------
    //
    // The guard was right and its EVIDENCE was forgeable. `safePoint` answered honestly about whatever list
    // it was handed, and `next --repo <r>` handed it a list `Scan.scope` had already filtered — in which our
    // claim in another repo does not appear. Invisible read as absent, and condition 3's own guard handed a
    // mid-item worker the side-quest it exists to withhold. These legs pin the type that ended it.

    [<Fact>]
    let ``a FILTERED board never reports us idle — invisible is not absent (#1086/#266)`` () =
        // The exact shape of the bug: our claim is live, and the slice cannot see it. Before the scope rode
        // in the type this was `Some` — the honest question, put to a board that could not answer it.
        let mine = { item 1 with Status = InProgress; Claim = Some(claim worker, LeaseHeld) }
        let ours = [ item 2 ]
        Assert.True((safePoint AtNext worker (Filtered ours) ours).IsNone)
        // ...and it is the FILTERING that refuses, not the claim: the same slice, with no claim of ours
        // anywhere in sight, is still refused. "I could not tell" cannot become "yes" by the subject
        // happening to look clean.
        Assert.True((safePoint AtNext worker (Filtered [ item 2 ]) [ item 2 ]).IsNone)
        // The whole board, holding that same claim, refuses for the RIGHT reason — it can see it.
        Assert.True((safePoint AtNext worker (Whole [ mine; item 2 ]) ours).IsNone)

    [<Fact>]
    let ``idleness is asked of the WHOLE board, the chore derived over the SUBJECT (#1086)`` () =
        // Two sets, and they always were. A claim of ours in ANOTHER repo makes us busy even though the
        // subject — the lock's own repo — is spotless. This is the case `next --repo <r>` got wrong.
        let elsewhere =
            { item 9 with Ref = { (item 9).Ref with Repo = "FS.GG.SDD" }
                          Status = InProgress
                          Claim = Some(claim worker, LeaseHeld) }

        let subject = [ { item 2 with State = Closed } ]     // a real chore, in the lock's repo
        Assert.True((safePoint AtNext worker (Whole (elsewhere :: subject)) subject).IsNone)

        // With that claim gone we are idle, and the SafePoint carries the subject — so `offer` still derives
        // over the lock's repo only, never over the board it took its evidence from.
        let at = (safePoint AtNext worker (Whole subject) subject).Value
        Assert.True((offer at).IsSome)

    [<Fact>]
    let ``offer hands back AT MOST ONE chore, however much is wrong`` () =
        let board =
            [ { item 1 with Claim = Some(claim other, LeaseExpiredNoPr) }
              { item 2 with State = Closed }
              { item 3 with Status = Blocked; Blockers = [ blocker 9 BlockerClosed ] } ]

        let at = (safePoint AtNext worker (Whole board) board).Value
        Assert.Equal(3, (derive board).Length)
        Assert.True((offer at).IsSome)

    [<Fact>]
    let ``the unlucky caller does not pay for everybody's garbage collection — 40 findings, one offer`` () =
        let board = [ for n in 1..40 -> { item n with State = Closed } ]
        let at = (safePoint AtNext worker (Whole board) board).Value
        Assert.Equal(40, (derive board).Length)
        Assert.Equal<string list>([ ".github#1" ], [ (offer at).Value.Subject.Short ])

    [<Fact>]
    let ``the offer is scoped to the board the idleness was OBSERVED on — evidence and subject are one value`` () =
        // `offer` takes no `items` argument, so a `SafePoint` minted from one board cannot be spent on
        // another. If it could, a caller could prove it was idle on an empty board and then be handed chores
        // from a board it holds a live lease on — condition 3 defeated by an argument list.
        let board = [ { item 1 with State = Closed } ]
        let at = (safePoint AtNext worker (Whole board) board).Value
        Assert.Equal(".github#1", (offer at).Value.Subject.Short)

    [<Fact>]
    let ``STALE-CLAIM is offered first — an unused reservation holds startable work off the board (#601)`` () =
        let board =
            [ { item 1 with State = Closed }
              { item 2 with Claim = Some(claim other, LeaseExpiredNoPr) } ]

        let at = (safePoint AtNext worker (Whole board) board).Value
        Assert.Equal("STALE-CLAIM", (offer at).Value.Kind.RuleId)

    [<Fact>]
    let ``two callers deriving the same board agree about what is next — the order is TOTAL (#464)`` () =
        // Not cosmetic. Under a fan-out the offer order IS the contention pattern: if two callers disagree
        // about what is next they take different locks and the queue drains in a different order every pass.
        let board = [ for n in [ 7; 3; 9; 1 ] -> { item n with State = Closed } ]
        let at = (safePoint AtNext worker (Whole board) board).Value
        let reversed = List.rev board
        let atR = (safePoint AtNext worker (Whole reversed) reversed).Value
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
