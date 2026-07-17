namespace FS.GG.Coord

module Batch =

    open Types
    open Schedulability

    type Holder =
        | LiveClaim of worker: WorkerId * item: Ref * ageSeconds: int
        | BatchMember of item: Ref
        | Unowned of item: Ref
        | UnknownHolder

    type Reservation =
        { Owner: string
          Repo: string
          Paths: TouchSet
          Holder: Holder }

    type Decision =
        { Item: Item
          Result: Schedulability
          CollidedWith: Holder option }

    type BatchResult =
        { Chosen: Item list
          Decisions: Decision list
          Truncated: bool }

    /// THE DECISION'S OWN WORDS — the only place a passed-over item is put into English.
    ///
    /// `Schedulability.explain` cannot do this alone: it sees the ITEM and the VERDICT, but a collision's
    /// holder lives on the DECISION (`CollidedWith`), because "who reserved this path" is a fact about the
    /// batch, not about the item. Rendering the verdict without it produces "overlaps in-flight work: a ⇄ b"
    /// — true, useless, and a REGRESSION on bash, which has named the holder and its lease since #428.
    ///
    /// The distinction between a LIVE CLAIM and a BATCH MEMBER is the one that matters most and is the one
    /// a holder-blind renderer destroys. They are the same verdict and two completely different instructions:
    /// a batch member frees at the end of THIS run and has no lease; a live claim means go and talk to a
    /// worker, or wait out a window. Collapsing them tells a worker to wait ~12m for a colleague who does
    /// not exist.
    let explainDecision (leaseMinutes: int) (d: Decision) : string =
        let id = d.Item.Ref.Short

        match d.Result, d.CollidedWith with
        | Schedulability.OverlapsInFlight hits, Some holder ->
            let where = Schedulability.collisionText hits

            match holder with
            | LiveClaim(WorkerId w, item, age) ->
                $"%s{id} — overlaps in-flight work held by %s{w} on %s{item.Short} (%s{Schedulability.leaseWindow leaseMinutes age}): %s{where}"

            | BatchMember item -> $"%s{id} — overlaps batch member %s{item.Short} (%s{where})"

            // IN PROGRESS, NO MARKER. Something is evidently editing those files, so the reservation is
            // real and must be honoured — but there is no worker to name, no lease to wait out, and
            // nobody to `say` to. Dressing it up as a holder ("held by — (lease unknown)") would invite a
            // worker to wait for a marker that is never coming.
            | Unowned item ->
                $"%s{id} — overlaps %s{item.Short}, which the board says is In progress with NO claim marker (someone is working outside the protocol — there is no lease to wait out; see: fsgg-coord who): %s{where}"

            | UnknownHolder -> $"%s{id} — overlaps in-flight work: %s{where}"

        | result, _ -> Schedulability.explain leaseMinutes d.Item result

    /// A passed-over candidate that is QUEUED BEHIND A LIVE CLAIM — a real marker holds it, or it overlaps
    /// one. The age is the HOLDER's, so its lease window can be computed; `KnownLiveWork` is true only for a
    /// claim whose own PR proves the work outlived its lease (#581), because a lapsed-but-alive lease is
    /// evidence to talk, never a lease to reap.
    type private QueuedClaim =
        { Worker: WorkerId
          Holder: Ref
          AgeSeconds: int
          KnownLiveWork: bool }

    let private itemAge (item: Item) =
        match item.Claim with
        | Some(c, _) -> c.AgeSeconds
        | None -> -1

    /// The passed-over items queued behind a LIVE CLAIM, in decision order — the ones for which "wait for a
    /// worker" or "reap a dead lease" is a real instruction. A candidate qualifies when its own lock holds
    /// it (`HeldBy`/`HeldByLiveWork`) or it overlaps one (`OverlapsInFlight` whose holder is a `LiveClaim`).
    /// A markerless In-progress reserver (`Unowned`), a batch-member peer, or an unnameable holder is NOT
    /// one: none has a worker to talk to or a lease to wait out, so folding them in would advertise a wait
    /// that nothing ends (#428).
    let private queuedBehindClaims (result: BatchResult) : QueuedClaim list =
        result.Decisions
        |> List.choose (fun d ->
            match d.Result, d.CollidedWith with
            | HeldBy w, _ ->
                Some
                    { Worker = w
                      Holder = d.Item.Ref
                      AgeSeconds = itemAge d.Item
                      KnownLiveWork = false }
            | HeldByLiveWork(w, _), _ ->
                Some
                    { Worker = w
                      Holder = d.Item.Ref
                      AgeSeconds = itemAge d.Item
                      KnownLiveWork = true }
            | OverlapsInFlight _, Some(LiveClaim(w, holder, age)) ->
                Some
                    { Worker = w
                      Holder = holder
                      AgeSeconds = age
                      KnownLiveWork = false }
            | _ -> None)

    /// A lease a `reap` would collect: expired by the clock, and NOT one whose PR proves the work is alive.
    let private leaseExpired (leaseMinutes: int) (q: QueuedClaim) =
        not q.KnownLiveWork && q.AgeSeconds >= 0 && q.AgeSeconds > leaseMinutes * 60

    /// The candidates withheld AT THE COLUMN because the caller did not pass `--include-backlog` (#636).
    ///
    /// This is an OBSERVED fact, not an inference: `Schedulability.schedulable` yields `WrongStatus Backlog`
    /// on one leg only — `Backlog when not allowBacklog`. WITH the flag a Backlog row falls straight through
    /// to the blocker/touch-set/overlap checks like any Ready one, so this verdict cannot appear. Its presence
    /// therefore means exactly "the flag was absent, and the scheduler stopped here".
    ///
    /// What it deliberately does NOT mean is "these are startable". The scheduler stopped at the column and
    /// never asked whether they are blocked, declared, or disjoint — so the banner may not promise they would
    /// be handed out. Asserting a cause we did not observe is the #440 defect, and this is the shape of queue
    /// (starved, with a plausible story to tell) where it is most tempting.
    let private withheldAtBacklog (result: BatchResult) : Item list =
        result.Decisions
        |> List.choose (fun d ->
            match d.Result with
            | WrongStatus Backlog -> Some d.Item
            | _ -> None)

    /// THE STARVED-QUEUE BANNER (#428). A batch that hands out NOTHING over a queue full of items QUEUED
    /// BEHIND LIVE CLAIMS reads exactly like an empty backlog — and a worker who reads "nothing schedulable"
    /// goes home from a repo with work in it. So when that is what happened, say the queue is BUSY, name
    /// every holder (who to talk to), give the SOONEST lease (whether the wait is worth it at all), and —
    /// for a lease that has already EXPIRED — point at `reap`, because a dead lease is the one blocker a
    /// worker clears themselves, a collection rather than a wait.
    ///
    /// THE UNTRIAGED HALF (#636). The banner above answers "who do I wait for". It could not answer the other
    /// way a full queue hands out nothing: every candidate sits in `Backlog`, and the caller did not ask for
    /// Backlog. That queue is not busy — it is UNTRIAGED, and the difference is the remedy. A claim is a wait;
    /// an untriaged column is a decision somebody can make right now.
    ///
    /// It had to be its own line rather than a wider `queuedBehindClaims`, because "BUSY" over an untriaged
    /// queue would be false in the direction that matters: it would tell a worker to wait out a lease that
    /// does not exist. That is the judgement the #428 tests already fixed for blockers ("a BUSY banner over
    /// it would be a lie"), and it holds here — what changes is that a Backlog column, unlike a blocker, has
    /// a remedy the worker can act on ALONE, and nothing named it.
    ///
    /// Silent when nothing was withheld at the column, so a genuinely Ready-starved queue gains no noise.
    let private backlogSection (result: BatchResult) : string list =
        match withheldAtBacklog result with
        | [] -> []
        | withheld ->
            // A BATCH IS NOT REPO-SCOPED — `--repo` is OPTIONAL, and without it `Scan.snapshot` keeps every
            // row on the org board (`repo: string option`, `| None -> true`). So the remedy must name every
            // repo it actually applies to: naming `List.head`'s would print ONE command under a count that
            // spans several, and a worker who ran it would silently address a subset of the items just
            // counted — a remedy that appears to cover a subject it does not (#266).
            let repos = withheld |> List.map (fun i -> i.Ref.Repo) |> List.distinct |> List.sort

            let count =
                $"%d{List.length withheld} item(s) are in BACKLOG, and were passed over AT THE COLUMN — whether they are startable is UNASKED, not no."

            match repos with
            | [ one ] ->
                [ count
                  $"  triage them to Ready, or schedule from Backlog as-is: fsgg-coord take --repo %s{one} --include-backlog" ]
            | many ->
                (count :: "  triage them to Ready, or schedule from Backlog as-is — one repo at a time:" :: [
                    for r in many -> $"    fsgg-coord take --repo %s{r} --include-backlog"
                ])

    /// Returns [] whenever the queue is NOT starved: work WAS handed out (a `chosen` batch is not starved),
    /// or nothing is queued behind a live claim AND nothing was withheld at the column (a queue starved by
    /// blockers is #440's business, and it is told per-item). A banner on a healthy queue is noise that
    /// trains workers to skip stderr — the very habit #440 was closed to break.
    let starvedBanner (leaseMinutes: int) (result: BatchResult) : string list =
        if not (List.isEmpty result.Chosen) then
            []
        else
            let backlog = backlogSection result

            match queuedBehindClaims result with
            // Nothing is held, but the column withheld candidates — UNTRIAGED, not busy, not empty either.
            | [] ->
                if List.isEmpty backlog then
                    []
                else
                    "this queue is UNTRIAGED, not empty." :: backlog
            | queued ->
                let holders =
                    queued
                    |> List.map (fun q -> q.Worker.Value)
                    |> List.distinct
                    |> List.sort
                    |> String.concat ", "

                let expired = queued |> List.filter (leaseExpired leaseMinutes)

                // The soonest lease to free. A reapable EXPIRED lease frees NOW, so it is the soonest of all.
                // Otherwise the window that closes first is the one with the LARGEST age — but only among
                // claims whose lease is still LIVE: a `HeldByLiveWork` claim is over its lease by the clock
                // yet is NOT reapable (#581), so letting its age drive `leaseWindow` would print a phantom
                // "lease EXPIRED — reapable" here with no reap advice beside it. If no live lease remains to
                // wait on (every queued claim is a live-work over-run, or ageless), we cannot name a window.
                let soonest =
                    if not (List.isEmpty expired) then
                        "lease EXPIRED — reapable"
                    else
                        match
                            queued
                            |> List.map (fun q -> q.AgeSeconds)
                            |> List.filter (fun a -> a >= 0 && a <= leaseMinutes * 60)
                        with
                        | [] -> "lease unknown"
                        | ages -> Schedulability.leaseWindow leaseMinutes (List.max ages)

                // The repos the expired leases are actually IN. This used to read "repo-scoped by
                // construction: every queued claim is in the batch's one repo" and take `List.head`'s — but
                // the batch is not repo-scoped (`--repo` is optional; see `backlogSection`), so on an
                // org-wide batch that printed ONE `reap --repo X` under a count spanning several, and the
                // leases in every other repo stayed uncollected while the line said they were collectable.
                let reapRepos =
                    expired |> List.map (fun q -> q.Holder.Repo) |> List.distinct |> List.sort

                let banner =
                    [ "this queue is BUSY, not empty."
                      $"%d{List.length queued} item(s) are QUEUED BEHIND LIVE CLAIMS held by: %s{holders}"
                      $"  soonest: %s{soonest}" ]

                // The one blocker a worker can clear ALONE is a dead lease — so if any has expired, do not
                // let it read as a wait: name how many, and the exact `reap` that collects them. `reap`
                // re-probes and refuses a claim whose PR is still open, so this advice can never break a
                // lock over live work (#581).
                let expiredLeases =
                    expired |> List.map (fun q -> q.Holder) |> List.distinct |> List.length

                let reapAdvice =
                    match reapRepos with
                    | [] -> []
                    | [ one ] ->
                        [ $"  %d{expiredLeases} of those lease(s) have EXPIRED — collect them: fsgg-coord reap --repo %s{one} --apply" ]
                    | many ->
                        ($"  %d{expiredLeases} of those lease(s) have EXPIRED — collect them, one repo at a time:"
                         :: [ for r in many -> $"    fsgg-coord reap --repo %s{r} --apply" ])

                // #636 — BUSY and UNTRIAGED are independent facts about one queue, and the live board that
                // prompted this had both: two items behind live claims (soonest ~120m) and seven withheld at
                // the column. Reporting only the first told the worker to wait two hours over work that was
                // one flag away. So the sections compose; neither suppresses the other.
                banner @ reapAdvice @ backlog

    /// Reservations that name files in THIS repo. Tokens are repo-relative (#312, #353).
    let private inRepo (owner: string) (repo: string) (reservations: Reservation list) =
        reservations |> List.filter (fun r -> r.Owner = owner && r.Repo = repo)

    let holderOf (reservations: Reservation list) (owner: string) (repo: string) (token: string) : Holder option =
        reservations
        |> inRepo owner repo
        |> List.tryFind (fun r ->
            match r.Paths with
            | Declared tokens ->
                tokens
                |> List.exists (function
                    | Matchable t -> t = token
                    | Unmatchable _ -> false)
            | Undeclared
            | DeclaredNone
            // An unread reservation names no token, so it can never be the holder OF a token. It never
            // reaches here: `schedule` reds the batch on one before any candidate is compared.
            | Unreadable _ -> false)
        |> Option.map (fun r -> r.Holder)

    /// A reservation that reserves nothing is the one thing this scheduler may not tolerate — see the
    /// `Red` leg on `schedule`. Returns the offending tokens, empty if the reservation is sound.
    ///
    /// AN UNREAD SURFACE RESERVES NOTHING, AND THAT IS EXACTLY THE FAIL-OPEN THIS FUNCTION EXISTS TO
    /// CATCH. `TouchSet.unmatchable` answers `[]` for `Unreadable` — truthfully, because we know of no
    /// bad tokens for the simple reason that we know of no tokens at all. Reading that `[]` as "the
    /// reservation is sound" would let every candidate clear a lock over files nobody ever looked at,
    /// and hand a second worker the tree its holder is standing in. Same shape as #273; different cause.
    let private unusableReservation (r: Reservation) =
        match r.Paths with
        | Unreadable reason ->
            [ $"the holder's issue body could not be read, so its touch-set is UNKNOWN (%s{reason})" ]
        | _ -> TouchSet.unmatchable r.Paths

    let schedule
        (allowBacklog: bool)
        (limit: int option)
        (inFlight: Reservation list)
        (candidates: Item list)
        : Verdict<BatchResult> =

        // FAIL CLOSED, BEFORE ANYTHING IS SCHEDULED. A reservation we cannot see the surface of makes
        // every later comparison a lie — the candidate would clear it and be handed files its holder
        // is standing in. Refuse the whole batch, exactly as the bash client does, rather than drop
        // one item and schedule the rest against a hole.
        let broken =
            inFlight
            |> List.choose (fun r ->
                match unusableReservation r with
                | [] -> None
                | bad -> Some(r, bad))

        match broken with
        | _ :: _ ->
            broken
            |> List.map (fun (r, bad) ->
                let who =
                    match r.Holder with
                    | LiveClaim(WorkerId w, item, _) -> $"worker %s{w} on %s{item.Short}"
                    | BatchMember item -> $"batch member %s{item.Short}"
                    | Unowned item -> $"%s{item.Short} (in progress, no claim marker)"
                    | UnknownHolder -> "an unnameable holder"

                let toks = String.concat ", " bad

                $"in-flight work held by %s{who} declares unmatchable touch-set token(s): %s{toks} — it therefore reserves NOTHING, and scheduling against an unknown touch-set would hand its files to a second worker. Fix with: fsgg-coord widen <issue> --paths '<paths>'")
            |> Red

        | [] ->

        // Greedy by issue number. DETERMINISM IS THE POINT: the scan is cached and shared across the
        // fleet (#418), so two workers reading the same window must compute the same batch — an
        // order-dependent scheduler would hand them different answers from identical input.
        let ordered = candidates |> List.sortBy (fun i -> i.Ref.Number)

        let mutable reserved = inFlight
        let mutable chosen = []
        let mutable decisions = []
        let mutable truncated = false
        let mutable stop = false

        let atLimit () =
            match limit with
            | Some n when n > 0 -> List.length chosen >= n
            | _ -> false

        // A candidate whose files are OCCUPIED does not merely drop out of the batch — it reserves.
        // The lock, not the board column, is the truth: a claim whose `Status` flip failed still owns
        // the item, and its files with it. Skipping it WITHOUT reserving would hand a later candidate
        // the very files another worker is standing in — which is the accident this whole scheduler
        // exists to prevent.
        let reserve (item: Item) (holder: Holder) =
            reserved <-
                reserved
                @ [ { Owner = item.Ref.Owner
                      Repo = item.Ref.Repo
                      Paths = item.TouchSet
                      Holder = holder } ]

        let ageOf (item: Item) =
            match item.Claim with
            | Some(c, _) -> c.AgeSeconds
            | None -> 0

        let step (item: Item) =
            let owner, repo = item.Ref.Owner, item.Ref.Repo

            // Only this repo's reservations. The ORDER is part of the contract: `inFlight` precedes
            // the batch members appended below, so a candidate colliding with both reports the LIVE
            // CLAIM — the collision that has a lease and a worker behind it, and therefore the only
            // one an operator can actually act on.
            let visible = reserved |> inRepo owner repo |> List.map (fun r -> r.Paths)

            let result = schedulable allowBacklog visible item

            let collidedWith =
                match result with
                | OverlapsInFlight((_, reservedToken) :: _) ->
                    // `conflicts` yields (candidateToken, reservedToken), and the RESERVED side is
                    // the key that joins this collision back to its owner (#428). Name the holder,
                    // not just the files: nobody can wait for, or talk to, a pair of paths.
                    holderOf reserved owner repo reservedToken |> Option.orElse (Some UnknownHolder)
                | _ -> None

            decisions <-
                { Item = item
                  Result = result
                  CollidedWith = collidedWith }
                :: decisions

            match result with
            | Startable ->
                chosen <- item :: chosen
                reserve item (BatchMember item.Ref)

                if atLimit () then
                    stop <- true
                    // TRUNCATED ONLY IF SOMETHING WAS LEFT UNSEEN. Hitting the cap ON the last
                    // candidate evaluated everything; reporting a cap that did not bite would be its
                    // own small lie.
                    truncated <- List.length decisions < List.length ordered

            // Held — by a live lease, or by a lapsed one whose `item/<n>-*` PR proves the work is
            // still alive (#581). Either way its worker is IN those files.
            | HeldBy worker -> reserve item (LiveClaim(worker, item.Ref, ageOf item))
            | HeldByLiveWork(worker, _) -> reserve item (LiveClaim(worker, item.Ref, ageOf item))

            | WrongStatus _
            | IssueClosed
            | NoTouchSet
            | DeliberatelyNoTouchSet
            | UnusableTouchSet _
            | BlockedBy _
            | ItemPrOpen _
            | OverlapsInFlight _
            | Undetermined _ ->
                // Not startable, and reserving nothing. `ItemPrOpen` is the #651 leg: an open `item/<n>-*`
                // PR with no marker means someone is implementing it, but there is no claim to name and no
                // lease to wait out — so, like a markerless Ready row, it reserves nothing here; it is simply
                // not handed out (which is what stops the duplicate implementation).
                ()

        for item in ordered do
            if not stop then
                step item

        Green
            { Chosen = List.rev chosen
              Decisions = List.rev decisions
              Truncated = truncated }
