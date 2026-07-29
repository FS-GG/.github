namespace FS.GG.Coord

module Chore =

    open Types

    type ChoreSize =
        | Quick
        | Involved

        member this.Label =
            match this with
            | Quick -> "quick"
            | Involved -> "involved"

    type ChoreKind =
        | StaleClaim of holder: WorkerId
        | ClaimStatusLag of column: BoardStatus
        | ClosedIssueNotDone of column: BoardStatus
        | BlockerCleared of resolved: string list
        | StatusNotBlocked of blockers: string list
        | ClassProjectionLag of declared: ItemClass

        member this.RuleId =
            match this with
            | StaleClaim _ -> "STALE-CLAIM"
            | ClaimStatusLag _ -> "CLAIM-STATUS-LAG"
            | ClosedIssueNotDone _ -> "CLOSED-ISSUE-NOT-DONE"
            | BlockerCleared _ -> "BLOCKER-CLEARED"
            | StatusNotBlocked _ -> "STATUS-NOT-BLOCKED"
            | ClassProjectionLag _ -> "CLASS-PROJECTION-LAG"

        /// The board write this kind's remedy performs — see Chore.fsi for why it lives HERE.
        member this.Write: (string * string) option =
            match this with
            // STALE-CLAIM writes no field: its remedy is a marker collection, delegated to `reap`, which
            // owns the CAS and restores `PreviousStatus` (#481). `None` is "there is no field write",
            // never "we could not work one out".
            | StaleClaim _ -> None
            | ClaimStatusLag _ -> Some("Status", statusWireName InProgress)
            | ClosedIssueNotDone _ -> Some("Status", statusWireName Done)
            | BlockerCleared _ -> Some("Status", statusWireName Ready)
            | StatusNotBlocked _ -> Some("Status", statusWireName Blocked)
            // The ONLY kind that writes a field other than `Status` (.github#1588), which is exactly why
            // `Write` had to move into this type — see the .fsi.
            | ClassProjectionLag c -> Some("Class", itemClassWireName c)

    /// The column, for PROSE. Deliberately not the four private `statusName`s in `Scan`/`Writes`/`Client`/
    /// `Snapshot`: those render the Projects v2 OPTION NAME, where an unset column is legitimately the empty
    /// string. That is the wire, and it is right there. Here it would be a sentence reading "the board says
    /// ", so `NoStatus` — a case, never a null, and its own bug (#437) — is NAMED.
    let private columnLabel (s: BoardStatus) =
        match s with
        | NoStatus -> "no Status at all"
        | Backlog -> "Backlog"
        | Ready -> "Ready"
        | InProgress -> "In progress"
        | Blocked -> "Blocked"
        | InReview -> "In review"
        | Done -> "Done"

    /// Both call sites are guarded non-empty (`BlockerCleared` requires blockers to exist; `StatusNotBlocked`
    /// matches `[]` away), so there is no empty case to render — and inventing a word for one would imply a
    /// state the callers already exclude.
    let private nameList (xs: string list) = String.concat ", " xs

    /// THE FLIP-TIME PREDICATE GATE — ADR-0050 call-site B (.github#1199, .github#1203).
    ///
    /// `BLOCKER-CLEARED` clears an item `Blocked → Ready` when its recorded blockers resolve. But a
    /// blocker can be a PROXY for the item's real acceptance predicate — FS.GG.Rendering#923's "WI-2
    /// (Game publishes the skill)" closing would flip it to `Ready` even though the semantic dependency
    /// (the registry row exists AND the owning manifest agrees) is not satisfied. So an item that
    /// DECLARES a machine-checkable registry predicate does not leave `Blocked` on blockers-cleared alone:
    /// the resolved verdict must also `Agrees`.
    ///
    /// `true` — the flip may proceed — in exactly two cases: the item declares NO such predicate (`None`,
    /// the common case, ungated by ADR-0050 boundary decision 5), or its predicate `Agrees`. A
    /// `Contradicts` or an `Unknown` returns `false` and HOLDS the item — fail closed, on the same terms a
    /// `BlockerUnknown` already holds `BLOCKER-CLEARED` (#266, #421): "could not evaluate the predicate" is
    /// not "the predicate holds", and a proxy blocker closing can no longer fake readiness.
    let private predicateAllowsFlip (item: Item) : bool =
        match item.Predicate with
        | None
        | Some RegistryPredicate.Agrees -> true
        | Some(RegistryPredicate.Contradicts _)
        | Some(RegistryPredicate.Unknown _) -> false

    /// THE HUMAN-PARK GATE — ADR-0045's sentinel, respected by the one chore that would overwrite it
    /// (.github#1644).
    ///
    /// `BLOCKER-CLEARED` clears an item `Blocked → Ready` when its recorded blockers resolve. An item whose
    /// body carries `Blocked on: human/decision` (or `human/action`) is unstartable until a HUMAN acts,
    /// whatever its concrete edges say — `Schedulability` step 3b refuses it outright, and ADR-0045's whole
    /// consequence is that such an item is "refused by construction". Promoting it writes the board a column
    /// that CONTRADICTS the scheduler, and the promotion is not self-correcting: `Ready` is what advertises
    /// the row to `ready`/`batch --include-backlog` and to every human reading the board, `lint`'s
    /// `BLOCKED-NO-REASON` only watches a `Blocked` row so it stops watching the park, and
    /// `STATUS-NOT-BLOCKED` cannot push the row back because its blockers are all resolved. One write, and
    /// the parking record is a body line nothing on the board agrees with any more.
    ///
    /// `true` — the flip may proceed — in exactly one case: the item declares NO sentinel **and we read the
    /// body that would have carried one**.
    ///
    /// THE SECOND CLAUSE IS THE FAIL-CLOSED, AND IT IS NOT DECORATION (#266). `HumanBlock = None` means BOTH
    /// "the body declares no sentinel" and "nobody read the body": `HumanBlock option` has nowhere to put
    /// "I could not look", and `Snapshot.parse` renders an unreadable body as `None` for exactly that reason.
    /// Gating on `HumanBlock.IsSome` alone would therefore promote a parked row whose body read FAILED —
    /// the fail-open dressed as the fix. The engine already records the missing fact one field over:
    /// `TouchSet.Unreadable` IS "we did not read the body" (`Types.fsi`), lifted off the SAME body by the
    /// SAME parse on the SAME terms, so this asks it rather than adding a second flag that could disagree
    /// with it. `Client.enrichBoardFacts` solved the identical collapse for `Class` from the identical
    /// source. And the gate is not a no-op: `Scan` reads every OPEN candidate's body unconditionally, so a
    /// failed read reaches here as `Unreadable`, not as a missing field.
    ///
    /// THIS IS THE ONE RULE THAT READS `TouchSet`, and it reads it as a BODY-READ RECEIPT, never as a
    /// touch-set: no path token is inspected and no declaration is judged. The match is TOTAL so a sixth
    /// `TouchSet` case must be classified here rather than defaulting to "the body was read" — which is the
    /// fail-open direction, and the only direction this function exists to make unwritable.
    ///
    /// **#620-SAFE, and that is the half the fixture pair pins.** An item with no parking record over a body
    /// we DID read is untouched: `None` + a readable touch-set flips exactly as it did before. That is the
    /// entire population #620's remedy was built for, and this narrows the flip condition rather than
    /// adding a second way to trigger it.
    let private humanBlockAllowsFlip (item: Item) : bool =
        match item.HumanBlock with
        | Some _ -> false
        | None ->
            match item.TouchSet with
            | Unreadable _ -> false
            | Undeclared
            | DeclaredNone
            | DeclaredChore
            | Declared _ -> true

    /// THE IN-FLIGHT-IMPLEMENTATION GATE — #651's refusal, respected by the one chore that would write the
    /// column it refuses on (.github#1738).
    ///
    /// `Schedulability` step 5b refuses a markerless item carrying an open `item/<n>-*` PR: *"an
    /// implementation is already in flight … claiming it now would duplicate work that is already written"*
    /// (#651). `BLOCKER-CLEARED`'s remedy is `Status = Ready` — the ONE column `columnStartability` calls
    /// `AlwaysStartable`, and therefore the column that ADVERTISES the row to every reader. So the two
    /// mechanisms disagreed about one item and the WRITE won, which is #1644's shape exactly, one field over.
    ///
    /// MEASURED, three instances in one board event: `FS.GG.Rendering#1094` merging fired `BLOCKER-CLEARED`
    /// on `#1086`, `#1089` and `#1092`, each of which had a complete open PR. `Ready` was wrong for all three.
    ///
    /// **IT READS EXACTLY WHAT STEP 5b READS, AND THAT IS THE POINT RATHER THAN A CONVENIENCE.** A gate that
    /// consulted a second source, or read this one more strictly, would be a THIRD opinion about one row and
    /// could disagree with the scheduler in the other direction. `Item.ItemPr` is the field step 5b refuses
    /// on; asking it the same question is what makes "the chore never writes a column the scheduler refuses
    /// on" structural instead of remembered.
    ///
    /// **IT DOES NOT FAIL CLOSED ON A PROBE THAT FAILED, AND THAT IS A KNOWN RESIDUAL — .github#1924.**
    /// This gate is NOT the fail-closed shape `humanBlockAllowsFlip` is, and saying it were would be the
    /// fail-open wearing the fix's clothes. `Reads.prAlive : IoResult&lt;Liveness&gt;` has FIVE outcomes;
    /// `Item.ItemPr` is an `int option` and carries ONE, so THREE arrive here as `None` = "no PR":
    /// `LeaseExpiredBranchPushed` (#1055's pushed branch, work in flight before its PR exists),
    /// `LivenessUnknown` (we could not ask), and `Error _` — INCLUDING `RateLimited`, which `Reads.prAlive`
    /// propagates on purpose and `Scan`'s probe then swallows. The third is the expensive one, because rate
    /// limiting is SYSTEMIC: one exhausted scan answers "no PR" for every row it probes.
    /// #651 chose that collapse deliberately, and it was sound while the only consumer was step 5b: that
    /// consumer fails open into OFFERING a row — read-only, and re-decided by the next scan. THIS consumer
    /// fails open into a board WRITE on somebody else's item, which `choresFor`'s own header names as the
    /// asymmetry that makes this mechanism safe to run unattended. Closing it needs a receipt `int option`
    /// cannot carry, which is a change to a shared wire fact and its four readers — filed as .github#1924
    /// rather than bodged in here. Until it lands, a rate-limited scan can still promote a cleared row.
    ///
    /// What #1738 DID change is the population the probe covers: `Scan` probed only the columns a scheduler
    /// offers TODAY (`Ready`/`Backlog`), and this rule writes the column that makes a row offerable TOMORROW —
    /// so a `Blocked` row, the only population this rule acts on, was never probed and this gate would have
    /// been dead on arrival. `Scan` now probes the `BLOCKER-CLEARED` candidate set as well, keyed on the
    /// shared `Blockers.cleared` so the probed population cannot drift narrower than the firing one.
    ///
    /// `true` — the flip may proceed — in exactly one case: no open `item/<n>-*` PR was RECORDED for this
    /// row. Which, per the paragraph above, is not yet the same sentence as "none was found".
    let private itemPrAllowsFlip (item: Item) : bool = item.ItemPr.IsNone

    [<Sealed>]
    type Chore internal (subject: Ref, kind: ChoreKind, size: ChoreSize) =
        member _.Subject = subject
        member _.Kind = kind
        member _.Size = size

        member _.Id = $"%s{kind.RuleId}:%s{subject.Owner}/%s{subject.Repo}#%d{subject.Number}"

        member _.Statement =
            match kind with
            | StaleClaim holder ->
                $"%s{subject.Short}: %s{holder.Value}'s lease has lapsed and the item has no open `item/` PR — collect the marker and restore the column it overwrote."
            | ClaimStatusLag column ->
                $"%s{subject.Short}: a live claim holds it, but the board says %s{columnLabel column} — set Status to In progress."
            | ClosedIssueNotDone column ->
                $"%s{subject.Short}: the issue is CLOSED but the board says %s{columnLabel column} — set Status to Done."
            | BlockerCleared resolved ->
                $"%s{subject.Short}: every blocker is resolved (%s{nameList resolved}) but the board still says Blocked — set Status to Ready."
            | StatusNotBlocked blockers ->
                $"%s{subject.Short}: %s{nameList blockers} is still open, but the board advertises it as startable — set Status to Blocked."
            | ClassProjectionLag c ->
                $"%s{subject.Short}: the item's own text declares `Class: %s{itemClassWireName c}` but the board's Class column does not say so — set Class to %s{itemClassWireName c}."

    type Boundary =
        | AtNext
        | AfterDone

        member this.Label =
            match this with
            | AtNext -> "next"
            | AfterDone -> "done"

    /// See the .fsi: the scope rides in the type because `Item list` could not say whether it was the whole
    /// board or a slice, and `safePoint` answered the idleness question honestly about a board that could
    /// not answer it (#1086).
    type Board =
        | Whole of Item list
        | Filtered of Item list

    [<Sealed>]
    type SafePoint internal (boundary: Boundary, worker: WorkerId, items: Item list) =
        member _.Boundary = boundary
        member _.Worker = worker
        member internal _.Items = items

    /// Does THIS worker hold a lock on this item RIGHT NOW?
    ///
    /// A stale-but-unreaped claim counts. The lease is a clock; the LOCK is broken only by `reap`
    /// (#461/#581), and until it is, the touch-set is still reserved and its holder is still mid-item. A
    /// `LivenessUnknown` counts too, and for the sharper reason: we could not tell whether that work is
    /// alive, and "I could not look" is not "I am idle" (#266). Both fail CLOSED — toward not offering.
    let private holdsLock (worker: WorkerId) (item: Item) =
        match item.Claim with
        | Some(claim, _) -> claim.Worker = worker
        | None -> false

    let safePoint (boundary: Boundary) (worker: WorkerId) (observed: Board) (subject: Item list) : SafePoint option =
        match observed with
        // #1086 — a slice cannot report us idle. A live claim of ours OUTSIDE the filter is invisible to
        // this list, and `List.exists` over it would answer "no claim found" for a worker who is mid-item
        // somewhere else. That is "I could not tell" wearing the costume of "no" (#266), and it is the one
        // answer this function must never give.
        | Filtered _ -> None
        | Whole items ->
            if items |> List.exists (holdsLock worker) then
                None
            else
                Some(SafePoint(boundary, worker, subject))

    /// The chores ONE item's observed state implies.
    ///
    /// **THE RESERVER OWNS THE SCHEDULING COLUMN, and this `match` is where that is decided — once, for
    /// every rule, instead of by each rule.** While a marker reserves an item, its column belongs to whoever
    /// holds it: a worker who hit a blocker and set `Blocked` made a DECISION, and a column set deliberately
    /// during a lease still wins (#331), so a chore that "reconciles" it overwrites somebody's judgement with
    /// a default — this mechanism running backwards. It is the RESERVER, not the live winner (`Reads.reserver`,
    /// not `Reads.winner`): a lease is a clock but a lock is broken only by `reap` (#461/#581), so a
    /// stale-but-uncollected marker still owns its column.
    ///
    /// The two branches are what makes that structural rather than remembered. A `reserved` PREDICATE is what
    /// this used to be, and a predicate has to be CALLED: three of the four column rules called it, the fourth
    /// (`CLOSED-ISSUE-NOT-DONE`) did not, and on a closed issue carrying a stale marker it derived `Done`
    /// while `STALE-CLAIM` derived the restore of `PreviousStatus` — two chores writing opposite columns to
    /// one item, the winner decided by whichever caller drained first. Here there is no guard to forget: a
    /// rule placed in the `None` branch cannot fire on a reserved item, because the branch is the guard.
    ///
    /// Deferring costs nothing, which is why it is safe to make absolute: `STALE-CLAIM` collects an abandoned
    /// marker, `reap` restores the column it overwrote (#481), and the deferred rules fire on the next pass.
    /// Deference DEFERS; it does not suppress.
    ///
    /// Every rule fails CLOSED. The asymmetry is deliberate and it is what makes the whole mechanism safe to
    /// run unattended: a chore we decline to derive costs one round-trip — the next caller derives it from
    /// the next scan — while a chore we derive wrongly is a board write nobody asked for, on somebody else's
    /// item. So where a fact was not observed, no chore is produced.
    let private choresFor (item: Item) : Chore list =
        match item.Claim with

        // RESERVED — only the rules that act on the MARKER itself. Neither writes a column the reserver did
        // not already own: STALE-CLAIM ends the reservation and restores what the claim overwrote, and
        // CLAIM-STATUS-LAG finishes the write the claim should have made. They are mutually exclusive on the
        // lease state, which is what holds "at most one chore per item" up on this side.
        | Some(claim, liveness) ->
            [
              // STALE-CLAIM. `LeaseExpiredNoPr` and nothing else — the lease lapsed AND we LOOKED for the
              // item's own `item/<n>-*` PR and found none. `LeaseExpiredPrOpen` is a worker demonstrably
              // still working (#581), `LeaseExpiredBranchPushed` is a pushed branch that proves work in
              // progress before its PR is opened (#1055), and `LivenessUnknown` is a probe that failed;
              // offering any of the three would hand the reaper a chore `Writes.reapable` must then refuse,
              // which is a queue that never drains. Fires on a CLOSED issue too: an abandoned lease reserves
              // its touch-set either way (#601), and freeing it is the whole point.
              match liveness with
              | LeaseExpiredNoPr -> Chore(item.Ref, StaleClaim claim.Worker, Involved)

              // CLAIM-STATUS-LAG. Only a LIVE lease, and only over the columns a claim should have
              // overwritten. `Blocked`/`In review` during a lease are the HOLDER's decisions and they still
              // win (#331) — reconciling those would overwrite somebody's judgement with a default.
              | LeaseHeld when
                  item.State = Open
                  && (match item.Status with
                      | Ready
                      | Backlog
                      | NoStatus -> true
                      | _ -> false)
                  ->
                  Chore(item.Ref, ClaimStatusLag item.Status, Quick)

              | _ -> () ]

        // UNRESERVED — the rules that write the column DIRECTLY. Nothing here needs to ask about a claim:
        // there is not one. They are pairwise disjoint on facts the compiler can see — `CLOSED-ISSUE-NOT-DONE`
        // needs `Closed` and the other two need `Open`; `BLOCKER-CLEARED` needs `Blocked` with every blocker
        // resolved where `STATUS-NOT-BLOCKED` needs `Ready`/`Backlog` with one OPEN — so at most one fires.
        | None ->
            [
              // CLOSED-ISSUE-NOT-DONE. The column is a projection of the work; the issue IS the work, and
              // when they disagree the issue wins (#520). This is NOT the done-stamp — that is
              // `done --flip`'s, it is earned against a merged PR, and faking it is how the board starts
              // lying. This only stops a closed issue wearing a column that says it is still live.
              //
              // It used to be an EXCEPTION to the reserver rule, justified as "no lease outranks a closed
              // issue". That answers a LIVE lease and was never true of a stale one, where STALE-CLAIM's own
              // remedy writes the column straight back. It does not need to be an exception in either case:
              // `rank` ALREADY puts STALE-CLAIM (0) ahead of this (3), so deferring changes no order that
              // was ever observable — it only stops the pair being derived at once. And against a live
              // lease, the holder who just closed the issue is about to `done --flip` it; racing them to
              // write `Done` is exactly what #331 forbids.
              if item.State = Closed && item.Status <> Done then
                  Chore(item.Ref, ClosedIssueNotDone item.Status, Quick)

              // BLOCKER-CLEARED. EVERY blocker resolved — CLOSED **or MERGED** (#476) — and at least one to
              // resolve. One `BlockerUnknown` or `BlockerUnparseable` and this does not fire: a blocker we
              // could not resolve is not a blocker we cleared (#266, #421), and unblocking on a failed read
              // is the fail-open this codebase exists to make unwritable.
              //
              // ...AND the item's declared registry predicate must agree — ADR-0050 call-site B. This is
              // the SAME fail-closed shape one step further out: `predicateAllowsFlip` holds the item on a
              // `Contradicts`/`Unknown` exactly as a `BlockerUnknown` above holds it, so a PROXY blocker
              // closing can no longer fake readiness for an item whose real acceptance is a registry fact
              // (FS.GG.Rendering#923). An item that declares no predicate (`None`) is ungated and flips as
              // today (ADR-0050 decision 5).
              //
              // ...AND the item must not be PARKED ON A HUMAN — .github#1644. `Blocked on: human/decision`
              // is ADR-0045's sentinel for "a person must choose before this is startable", and this is the
              // one mechanical rule that could overwrite it: promoting such a row to `Ready` converts "a
              // human must decide this" into "a worker may pick this up". `humanBlockAllowsFlip` also fails
              // closed on a body we did not READ, because `HumanBlock = None` cannot tell that apart from
              // "declares no sentinel".
              //
              // ...AND the item must not already carry an IMPLEMENTATION IN FLIGHT — .github#1738. An open
              // `item/<n>-*` PR on a markerless row is exactly what `Schedulability` step 5b refuses on
              // (#651), and `Ready` is precisely the column that invites the duplicate implementation it
              // refused. Same shape as the park one field over, and `itemPrAllowsFlip` asks the SAME field
              // step 5b asks — so the two mechanisms cannot reach opposite answers about one row.
              //
              // The gate order mirrors `Schedulability`'s: concrete blockers (step 3), the human park (3b),
              // the markerless in-flight PR (5b) — then the machine predicate, for which `Schedulability`
              // has no step at all and which is therefore last.
              if
                  item.State = Open
                  && item.Status = Blocked
                  // `Blockers.cleared`, not a `not IsEmpty && forall` spelled here: `Scan` must probe
                  // exactly this population for `Item.ItemPr`, so the two projects ask ONE function
                  // rather than agreeing by inspection (.github#1738, #1012).
                  && Blockers.cleared item.Blockers
                  && humanBlockAllowsFlip item
                  && itemPrAllowsFlip item
                  && predicateAllowsFlip item
              then
                  Chore(item.Ref, BlockerCleared(item.Blockers |> List.map (fun b -> b.Display)), Quick)

              // STATUS-NOT-BLOCKED. A blocker observed OPEN while the board advertises the item as
              // startable — the scheduler is handing out work that is not startable. `BlockerUnknown` blocks
              // too, but writing `Blocked` off a read we FAILED to make would stamp a column from a failure,
              // so it stays report-only in `lint`.
              match item.Blockers |> List.filter (fun b -> b.State = BlockerOpen) with
              | [] -> ()
              | openOnes when
                  item.State = Open
                  && (match item.Status with
                      | Ready
                      | Backlog -> true
                      | _ -> false)
                  ->
                  Chore(item.Ref, StatusNotBlocked(openOnes |> List.map (fun b -> b.Display)), Quick)
              | _ -> ()

              // CLASS-PROJECTION-LAG (.github#1588). The item's own text declares a class and the board's
              // `Class` column does not agree — so the projection is stale and this writes it.
              //
              // DERIVED, NEVER GUESSED, and the direction is the whole item: the body line is the
              // AUTHORITY (ADR-0066, keeping ADR-0045 intact) and the column is a rendering of it. An item
              // whose text declares nothing derives NOTHING here — `Item.Class = None` produces no chore,
              // so a row nobody has triaged is never stamped with a class this engine made up. `lint`'s
              // `CLASS-UNSET` reports that gap to a human instead, which is #1588's AC3 exactly.
              //
              // It fires only where the two DISAGREE, which is what lets it retire: once the write lands,
              // the next scan reads `BoardClass = Class` and derives nothing. An unconditional write would
              // leave `Chore.isRetired` answering "still owed" forever against a write that succeeded.
              // `BoardClass = None` on a board with no `Class` field therefore reads as a real
              // disagreement and the write is attempted — deliberately: the field is the thing #1588 adds,
              // and a projection that silently no-opped when its column was missing is the fail-open shape
              // #1575 and #266 already cost this repo twice.
              //
              // THAT CHOICE PUTS AN OBLIGATION ON THE CALLER, AND .github#1649 IS WHAT IT COSTS WHEN THE
              // CALLER SKIPS IT. `None` here means "no such column"; in `Snapshot.parse` the SAME value
              // means "this parser did not look", because the column is a scan fact no pure document can
              // carry. A caller that derives over parsed candidates WITHOUT joining the scan rows therefore
              // hands this rule a permanent, unretirable disagreement for every open classed item — eight
              // offers across three repos before it was found, one of them naming a discrepancy that
              // measurably did not exist. The rule is right and is unchanged; `Client.offerBoardOf` is the
              // join that owes it a real answer, and `ChoresTests`' #1649 legs pin it from the real writer.
              //
              // OPEN items only. A closed row is out of the burn-down's scope, and classing it would spend
              // a board write on the one population no stopping rule consults.
              match item.State, item.Class, item.BoardClass with
              | Open, Some declared, board when board <> Some declared -> Chore(item.Ref, ClassProjectionLag declared, Quick)
              | _ -> () ]

    /// How much a chore unwedges the QUEUE. Lower sorts first.
    ///
    /// Not age. A `STALE-CLAIM` reserves a touch-set, and an unused reservation holds startable work off the
    /// board for the rest of the lease — that is #601, and it is strictly the most expensive thing on this
    /// list. Then the two rules that make the board's answer to "what can I start?" wrong in either
    /// direction. Then the two that merely make it untidy.
    let private rank (c: Chore) =
        match c.Kind with
        | StaleClaim _ -> 0
        | BlockerCleared _ -> 1
        | StatusNotBlocked _ -> 2
        | ClosedIssueNotDone _ -> 3
        | ClaimStatusLag _ -> 4
        // LAST, and it is the one kind that unwedges nothing: every rule above changes the board's answer
        // to "what can I start?", where this one changes the board's answer to "how bad is it". A worker
        // offered a chore at a safe point should be handed the queue-freeing one first — and #1588's own
        // argument is that severity is a stopping-rule input, consulted after the queue is drained rather
        // than to drain it.
        | ClassProjectionLag _ -> 5

    let derive (items: Item list) : Chore list = items |> List.collect choresFor

    let offer (at: SafePoint) : Chore option =
        // The board comes from the capability, never from a second argument: a `SafePoint` minted from one
        // board and spent on another would prove idleness about a board nobody asked about. Condition 4
        // rides on the same value — the chore-execution path mints no `SafePoint`, so a chore cannot offer
        // a chore, and the recursion is unwritable rather than merely bounded.
        derive at.Items
        // Total, so two callers deriving the same board agree about what is next; `Id` breaks the tie
        // because it is the one field that is unique per chore and stable across passes.
        |> List.sortBy (fun c -> rank c, c.Id)
        |> List.tryHead

    let isRetired (chore: Chore) (items: Item list) : bool =
        derive items |> List.forall (fun c -> c.Id <> chore.Id)
