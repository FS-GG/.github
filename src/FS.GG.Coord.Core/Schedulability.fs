namespace FS.GG.Coord

module Schedulability =

    open Types

    type Schedulability =
        | Startable
        | WrongStatus of BoardStatus
        | IssueClosed
        | NoTouchSet
        | DeliberatelyNoTouchSet
        | UnusableTouchSet of tokens: string list
        | BlockedBy of Blocker list
        | HeldBy of WorkerId
        | HeldByLiveWork of WorkerId * pr: int
        | OverlapsInFlight of (string * string) list
        | Undetermined of reason: string

    let schedulable (allowBacklog: bool) (inFlight: TouchSet list) (item: Item) : Schedulability =

        // ORDER IS PART OF THE SPEC, not an implementation detail. Each check must come before every
        // check whose answer it would make meaningless — and it must come AFTER any check that
        // produces a stronger, more actionable statement about the same item.

        // 1. THE ISSUE BEFORE THE COLUMN (#520). The board column is a projection; the issue is the
        //    work. Asking "is the column Ready?" of a CLOSED issue is asking the wrong question of the
        //    wrong record — and answering it is how a closed item got handed out twice.
        match item.State with
        | Closed -> IssueClosed
        | Open ->

        // 2. The column. `NoStatus` is its own case and must not read as `Backlog` (#437).
        match item.Status with
        | NoStatus
        | InProgress
        | Blocked
        | InReview
        | Done -> WrongStatus item.Status
        | Backlog when not allowBacklog -> WrongStatus Backlog
        | Backlog
        | Ready ->

        // 3. THE TOUCH-SET, BEFORE THE LOCK. "Nobody can claim this item" is a stronger and cheaper
        //    statement than "somebody already has", and a worker told the second when the first is
        //    also true fixes the wrong thing.
        match item.TouchSet with
        | Undeclared -> NoTouchSet
        | DeclaredNone -> DeliberatelyNoTouchSet
        | Declared _ ->

        match TouchSet.unmatchable item.TouchSet with
        | _ :: _ as bad when List.length bad = (match item.TouchSet with
                                                | Declared ts -> List.length ts
                                                | _ -> 0) ->
            // EVERY token is unmatchable: the declaration reserves nothing at all, so it is as dead as
            // no declaration — but for a different reason, and the linter must say which (#496).
            UnusableTouchSet bad
        | _ :: _ as bad ->
            // SOME tokens are unmatchable. This is worse than all of them being so: the item looks
            // declared, and the unmatchable tokens silently reserve nothing — so the files they name
            // are invisible to every other worker's overlap check. Refuse, do not partially schedule.
            UnusableTouchSet bad
        | [] ->

        // 4. THE LOCK — and what "held" actually means.
        match item.Claim with
        | Some(claim, LeaseHeld) -> HeldBy claim.Worker

        | Some(claim, LeaseExpiredPrOpen pr) ->
            // #581. The lease lapsed; the WORK did not. An open PR on the item's own branch is the
            // worktree protocol's own artifact, and it outranks a timer. `take` handed out an item
            // exactly like this one while its worker was on it, because a loaded box stretched one
            // build past the lease — and it later reaped the claim on #485 while that worker was
            // fixing #485.
            HeldByLiveWork(claim.Worker, pr)

        | Some(_, LivenessUnknown) ->
            // We could not ask whether the work is alive. That is NOT the same as "no PR", and
            // treating it as such is what destroyed uncommitted work. An unverifiable claim is not a
            // free item.
            Undetermined "the claim's lease has expired and we could not check for an open item/<n> PR — an unverifiable claim is not an abandoned one (#581)"

        | Some(_, LeaseExpiredNoPr)
        | None ->

        // 5. THE BLOCKERS. Resolved = CLOSED or MERGED (#476). Unknown and Unparseable BLOCK.
        match Blockers.unresolved item.Blockers with
        | _ :: _ as holding -> BlockedBy holding
        | [] ->

        // 6. DISJOINTNESS, last: it is the only check that depends on other items.
        let hits = inFlight |> List.collect (TouchSet.conflicts item.TouchSet)

        match hits with
        | _ :: _ -> OverlapsInFlight hits
        | [] -> Startable

    let explain (item: Item) (result: Schedulability) : string =
        let id = item.Ref.Short

        match result with
        | Startable -> $"%s{id} — startable"
        | IssueClosed ->
            $"%s{id} — the issue is CLOSED (the board column says %A{item.Status}; /check-board reconciles it)"
        | WrongStatus NoStatus ->
            $"%s{id} — no Status on the board: invisible to every scheduler, and nobody set it"
        | WrongStatus s -> $"%s{id} — Status is %A{s}"
        | NoTouchSet ->
            $"%s{id} — no 'Paths:' declared (an OMISSION; declare one, or 'Paths: none' if it truly has none)"
        | DeliberatelyNoTouchSet -> $"%s{id} — 'Paths: none' (deliberately has no touch-set; not schedulable by design)"
        | UnusableTouchSet tokens ->
            let toks = String.concat ", " tokens
            $"%s{id} — unmatchable 'Paths:' token(s): %s{toks}. Not a glob language: exact paths, directory prefixes, and a TRAILING /** or /*"
        | BlockedBy holding ->
            let names =
                holding
                |> List.map (fun b -> $"%s{b.Ref.Short} (%A{b.State})")
                |> String.concat ", "

            $"%s{id} — blocked by %s{names}"
        | HeldBy(WorkerId w) -> $"%s{id} — already claimed by worker %s{w}"
        | HeldByLiveWork(WorkerId w, pr) ->
            $"%s{id} — lease EXPIRED, but PR #%d{pr} is open: worker %s{w} is demonstrably still working it. Not offering it; its touch-set stays reserved"
        | OverlapsInFlight hits ->
            let pairs =
                hits |> List.map (fun (a, b) -> $"%s{a} ⇄ %s{b}") |> String.concat ", "

            $"%s{id} — overlaps in-flight work: %s{pairs}"
        | Undetermined reason -> $"%s{id} — UNDETERMINED: %s{reason}"
