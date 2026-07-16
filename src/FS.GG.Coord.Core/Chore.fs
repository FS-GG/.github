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

    let safePoint (boundary: Boundary) (worker: WorkerId) (items: Item list) : SafePoint option =
        if items |> List.exists (holdsLock worker) then
            None
        else
            Some(SafePoint(boundary, worker, items))

    let private resolved (b: Blocker) =
        match b.State with
        | BlockerClosed
        | BlockerMerged -> true
        | BlockerOpen
        | BlockerUnknown
        | BlockerUnparseable -> false

    /// IS A CLAIM MARKER RESERVING THIS ITEM? — and therefore: is its SCHEDULING COLUMN somebody else's?
    ///
    /// The rule this expresses, once, rather than per-rule: **while a marker reserves an item, the column
    /// belongs to whoever holds it.** A worker who hits a blocker and sets `Blocked` made a DECISION, and a
    /// column set deliberately during a lease still wins (#331) — so a chore that "reconciles" it overwrites
    /// somebody's judgement with a default, which is this mechanism running backwards.
    ///
    /// It is the RESERVER, not the live winner (`Reads.reserver`, not `Reads.winner`): a lease is a clock
    /// but a lock is broken only by `reap` (#461/#581), so a stale-but-uncollected marker still owns the
    /// column. That costs nothing — STALE-CLAIM collects it and restores the column (#481), and the rules
    /// below then fire on the next pass. It converges, and it never races the holder.
    ///
    /// Without this the rules disagree with each other: an item that is `Ready`, claimed, and blocked derived
    /// BOTH `CLAIM-STATUS-LAG` ("set In progress") and `STATUS-NOT-BLOCKED` ("set Blocked") — two chores
    /// writing opposite columns to one item, with the winner decided by whichever caller drained first.
    let private reserved (item: Item) = item.Claim.IsSome

    /// The chores ONE item's observed state implies.
    ///
    /// Every rule fails CLOSED. The asymmetry is deliberate and it is what makes the whole mechanism safe to
    /// run unattended: a chore we decline to derive costs one round-trip — the next caller derives it from
    /// the next scan — while a chore we derive wrongly is a board write nobody asked for, on somebody else's
    /// item. So where a fact was not observed, no chore is produced.
    let private choresFor (item: Item) : Chore list =
        [
          // STALE-CLAIM. `LeaseExpiredNoPr` and nothing else — the lease lapsed AND we LOOKED for the item's
          // own `item/<n>-*` PR and found none. `LeaseExpiredPrOpen` is a worker demonstrably still working
          // (#581) and `LivenessUnknown` is a probe that failed; offering either would hand the reaper a
          // chore `Writes.reapable` must then refuse, which is a queue that never drains. Fires on a CLOSED
          // issue too: an abandoned lease reserves its touch-set either way (#601), and freeing it is the
          // whole point.
          match item.Claim with
          | Some(claim, LeaseExpiredNoPr) -> Chore(item.Ref, StaleClaim claim.Worker, Involved)
          | _ -> ()

          // CLAIM-STATUS-LAG. Only a LIVE lease, and only over the columns a claim should have overwritten.
          // `Blocked`/`In review` during a lease are the HOLDER's decisions and they still win (#331) —
          // reconciling those would overwrite somebody's judgement with a default, which is this mechanism
          // running backwards. A stale lease is left to STALE-CLAIM above rather than double-reported: its
          // remedy restores the column anyway (#481).
          match item.Claim with
          | Some(_, LeaseHeld) when
              item.State = Open
              && (match item.Status with
                  | Ready
                  | Backlog
                  | NoStatus -> true
                  | _ -> false)
              ->
              Chore(item.Ref, ClaimStatusLag item.Status, Quick)
          | _ -> ()

          // CLOSED-ISSUE-NOT-DONE. The column is a projection of the work; the issue IS the work, and when
          // they disagree the issue wins (#520). This is NOT the done-stamp — that is `done --flip`'s, it is
          // earned against a merged PR, and faking it is how the board starts lying. This only stops a
          // closed issue wearing a column that says it is still live.
          if item.State = Closed && item.Status <> Done then
              Chore(item.Ref, ClosedIssueNotDone item.Status, Quick)

          // BLOCKER-CLEARED. EVERY blocker resolved — CLOSED **or MERGED** (#476) — and at least one to
          // resolve. One `BlockerUnknown` or `BlockerUnparseable` and this does not fire: a blocker we could
          // not resolve is not a blocker we cleared (#266, #421), and unblocking on a failed read is the
          // fail-open this codebase exists to make unwritable.
          //
          // NOT while a marker reserves it: that `Blocked` is very likely the HOLDER's own — they hit the
          // blocker, set the column, and have not released yet — and #331 says their column wins. They will
          // release, `release` restores it, and this fires on the next pass.
          if
              item.State = Open
              && not (reserved item)
              && item.Status = Blocked
              && not item.Blockers.IsEmpty
              && item.Blockers |> List.forall resolved
          then
              Chore(item.Ref, BlockerCleared(item.Blockers |> List.map (fun b -> b.Display)), Quick)

          // STATUS-NOT-BLOCKED. A blocker observed OPEN while the board advertises the item as startable —
          // the scheduler is handing out work that is not startable. `BlockerUnknown` blocks too, but
          // writing `Blocked` off a read we FAILED to make would stamp a column from a failure, so it stays
          // report-only in `lint`.
          //
          // NOT while a marker reserves it, and here that is not merely deference: a reserved item is not
          // being ADVERTISED to anyone — the claim reserves its touch-set, so the scheduler will not hand it
          // out whatever the column says. The premise of this rule is already false, and firing anyway would
          // contradict CLAIM-STATUS-LAG on the very same item.
          match item.Blockers |> List.filter (fun b -> b.State = BlockerOpen) with
          | [] -> ()
          | openOnes when
              item.State = Open
              && not (reserved item)
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
