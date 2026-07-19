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

        member this.RuleId =
            match this with
            | StaleClaim _ -> "STALE-CLAIM"
            | ClaimStatusLag _ -> "CLAIM-STATUS-LAG"
            | ClosedIssueNotDone _ -> "CLOSED-ISSUE-NOT-DONE"
            | BlockerCleared _ -> "BLOCKER-CLEARED"
            | StatusNotBlocked _ -> "STATUS-NOT-BLOCKED"

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
              if
                  item.State = Open
                  && item.Status = Blocked
                  && not item.Blockers.IsEmpty
                  && item.Blockers |> List.forall Blockers.isResolved
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
