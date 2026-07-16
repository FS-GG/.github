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
        | ItemPrOpen of pr: int
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

        // 3. THE BLOCKERS, BEFORE THE TOUCH-SET — and this is the ORDERING DECISION that ADR-0034 left
        //    open for the flip to make. The two engines checked in different orders, both were right
        //    about the item, and only one of them can be the sentence a worker reads.
        //
        //    Bash's order wins, for two reasons that point the same way:
        //
        //    SEMANTICS. A blocked item cannot be started whatever its touch-set says. "No 'Paths:'
        //    declared" is an OMISSION the worker can fix in ten seconds — and telling them that about an
        //    item they still could not start afterwards sends them to fix the wrong thing, then come
        //    back to the same queue. "Blocked by #999" is the fact that actually governs.
        //
        //    COST, which is the one that settles it. Blockers are BOARD facts: the scan already has
        //    them, and they are free. A touch-set lives in the issue BODY — one REST read per item. So
        //    bash never reads the body of a blocked candidate, and never needed to. Checking the
        //    touch-set first would force the client to fetch a body for every blocked item on the board
        //    just to reach a verdict that the board could already have given — paying the budget that
        //    dies first (#418) to answer a question that was already answered.
        match Blockers.unresolved item.Blockers with
        | _ :: _ as holding -> BlockedBy holding
        | [] ->

        // 4. THE TOUCH-SET, BEFORE THE LOCK. "Nobody can claim this item" is a stronger and cheaper
        //    statement than "somebody already has", and a worker told the second when the first is
        //    also true fixes the wrong thing.
        match item.TouchSet with
        | Unreadable reason ->
            // WE DID NOT READ THE BODY, SO WE DO NOT KNOW THE TOUCH-SET — and an unknown touch-set is
            // not an absent one. Coercing this to `Undeclared` would report a confident OMISSION about
            // an item nobody looked at, and then schedule everything else against a surface we cannot
            // see. It is `Undetermined`: not startable, and SAID SO. (A BLOCKED item never reaches
            // here — that is the whole point of the order above, and it is why one unreadable body
            // cannot starve a worker.)
            Undetermined $"the issue body could not be read, so its touch-set is UNKNOWN — not absent (%s{reason})"
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

        // 5. THE LOCK — and what "held" actually means.
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

        // 5b. THE MARKERLESS PR (#581 one leg over — #651). No live-held claim governs this item, but an
        //     open `item/<n>-*` PR on its own branch is an implementation ALREADY IN FLIGHT — a worker who
        //     opened a PR without a claim marker, or whose marker lapsed and was cleaned. Offering it costs a
        //     DUPLICATE implementation of work that is already written. The open-PR probe is proof of life
        //     whether or not a marker points at it; #581 read it only through the marker, so a markerless
        //     item slipped straight through to `Startable`. `ItemPr` is `None` unless the scan found such a
        //     PR AND no marker claimed the item, so a claimed item never double-counts here.
        match item.ItemPr with
        | Some pr -> ItemPrOpen pr
        | None ->

        // 6. DISJOINTNESS, last: it is the only check that depends on other items.
        let hits = inFlight |> List.collect (TouchSet.conflicts item.TouchSet)

        match hits with
        | _ :: _ -> OverlapsInFlight hits
        | [] -> Startable

    /// THE OPERATOR'S WORDS, NOT THE COMPILER'S. `%A` on a union prints its CASE NAME — `BlockerOpen`,
    /// `InProgress` — and after the flip (ADR-0034 Phase 3b) this prose is what every worker reads when
    /// a scheduler hands them nothing. `InProgress` is not even what the board says; the column is
    /// literally "In progress", so a worker who greps for what they were told finds nothing. These
    /// renderings are the projection edge: the DU is the model, and this is the only place it is put
    /// into English.
    let private statusText (s: BoardStatus) =
        match s with
        | NoStatus -> "no Status"
        | Backlog -> "Backlog"
        | Ready -> "Ready"
        | InProgress -> "In progress"
        | Blocked -> "Blocked"
        | InReview -> "In review"
        | Done -> "Done"

    let private blockerText (s: BlockerState) =
        match s with
        | BlockerOpen -> "open"
        | BlockerClosed -> "closed"
        | BlockerMerged -> "merged"
        | BlockerUnknown -> "unknown"
        | BlockerUnparseable -> "unparseable"

    /// The grammar, stated once. A refusal that does not say what WOULD have been accepted just moves
    /// the worker's confusion one step later.
    let TouchSetGrammar =
        "supported: an exact path ('src/Foo.fs'), or a directory prefix ('src/Foo', 'src/Foo/*', 'src/Foo/**'). There is no glob matcher: a leading '**/' or an interior '*' matches nothing — spell the paths out."

    /// THE OPERATOR'S QUESTION IS NOT "IS IT HELD?" BUT "SHOULD I WAIT?" — AND THAT IS A NUMBER (#428).
    ///
    /// "nothing schedulable" and "queued behind a claim held by <w>, lease frees in ~96m" are the same
    /// fact and two completely different instructions: the first reads as an empty queue and sends a
    /// worker home. Bash has always answered this (`lease_window`). The typed core must too, or the
    /// flip is an information REGRESSION on the one line a starved worker actually reads.
    ///
    /// A NEGATIVE age means the age is UNKNOWN and says so, rather than guessing. `claim` has always
    /// recorded a timestamp, but a hand-written or truncated marker may not — and inventing
    /// "frees in ~120m" out of a missing field is the confident-but-unfounded sentence that #440 and
    /// #488 were both closed for.
    let leaseWindow (leaseMinutes: int) (ageSeconds: int) : string =
        if ageSeconds < 0 then
            "lease unknown"
        else
            let left = leaseMinutes - ageSeconds / 60

            // An expired lease is not a WAIT, it is a REAP. Printing "frees in ~-180m" — or rounding
            // that to zero and implying "any moment now" — sends a worker off to wait for a holder who
            // is very likely dead. The touch-set stays reserved regardless: only `reap` may break a lock.
            if left <= 0 then
                "lease EXPIRED — reapable"
            else
                $"lease frees in ~%d{left}m"

    /// The path-collision pair, as bash renders it — STEMMED, so `src/Off/Sub/**` and `src/Off/Sub` do
    /// not read as two different things when they are one subtree. The wide arrow is not decoration:
    /// these lines are read beside a wall of issue refs, and the collision is what the eye must land on.
    let collisionText (hits: (string * string) list) =
        hits
        |> List.map (fun (a, b) -> $"%s{TouchSet.stem a}  ⇄  %s{TouchSet.stem b}")
        |> String.concat ", "

    let explain (leaseMinutes: int) (item: Item) (result: Schedulability) : string =
        let id = item.Ref.Short

        let age =
            match item.Claim with
            | Some(c, _) -> c.AgeSeconds
            | None -> -1

        match result with
        | Startable -> $"%s{id} — startable"
        | IssueClosed ->
            $"%s{id} — the issue is closed (the board column says %s{statusText item.Status}; /check-board reconciles it)"
        | WrongStatus NoStatus ->
            $"%s{id} — no Status on the board: invisible to every scheduler, and nobody set it"
        | WrongStatus s -> $"%s{id} — Status is %s{statusText s}"
        | NoTouchSet ->
            $"%s{id} — no 'Paths:' declared (cannot schedule — this is an OMISSION; declare one, or 'Paths: none' if it truly has no touch-set)"
        | DeliberatelyNoTouchSet -> $"%s{id} — 'Paths: none' (deliberately has no touch-set; not schedulable by design)"
        | UnusableTouchSet tokens ->
            let toks = String.concat ", " tokens
            $"%s{id} — unmatchable 'Paths:' token(s): %s{toks} (cannot schedule; %s{TouchSetGrammar})"
        | BlockedBy holding ->
            let names =
                holding
                |> List.map (fun b -> $"%s{b.Display} (%s{blockerText b.State})")
                |> String.concat ", "

            $"%s{id} — blocked by %s{names}"
        | HeldBy(WorkerId w) -> $"%s{id} — already claimed by worker %s{w} (%s{leaseWindow leaseMinutes age})"
        | HeldByLiveWork(WorkerId w, pr) ->
            $"%s{id} — lease EXPIRED, but PR #%d{pr} is open: worker %s{w} is demonstrably still working it (#581). Not offering it; its touch-set stays reserved."
        | ItemPrOpen pr ->
            $"%s{id} — no claim marker, but PR #%d{pr} is open on its `item/%d{item.Ref.Number}-*` branch: an implementation is already in flight. Not offering it — claiming it now would duplicate work that is already written (#651)."
        | OverlapsInFlight hits ->
            // The HOLDER-BLIND rendering. `Batch.explainDecision` overrides this with the holder and its
            // lease window whenever the decision carries one — which, in a batch, is always. This is the
            // last resort, for a collision whose holder genuinely could not be named.
            $"%s{id} — overlaps in-flight work: %s{collisionText hits}"
        | Undetermined reason -> $"%s{id} — UNDETERMINED: %s{reason}"
