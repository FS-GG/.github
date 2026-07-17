namespace FS.GG.Coord.Cli

module Chores =

    open FS.GG.Coord
    open FS.GG.Coord.Types
    open FS.GG.Coord.GitHub

    // TEN MINUTES. See the .fsi for why this is short and what it actually governs — it bounds how long a
    // DEAD worker stalls one repo's drain, not how long a live one may take. A chore is one board write.
    let LeaseMinutes = 10

    let render (chore: Chore.Chore) (lockRef: Ref) : string =
        // The chore's OWN sentence (#485: do not spell the condition twice), then the two things that are
        // true only because we took the lock: nobody else in this repo is being offered a chore, and the
        // lock is ours until we drop it or it lapses. Naming the release command matters — a worker who
        // declines must be able to hand the drain back NOW rather than make the fleet wait out the lease,
        // and condition 3's "carry an explicit size so the worker can decline" is not real if declining has
        // no verb.
        String.concat
            "\n"
            [ $"chore [%s{chore.Size.Label}] %s{chore.Statement}"
              $"  you hold %s{lockRef.Short}, the chore lock for this repo (%d{LeaseMinutes}m)."
              $"  do it, or hand it back now:  fsgg-coord release %s{lockRef.Short}" ]

    let offer
        (transport: Transport.IGitHubTransport)
        (boundary: Chore.Boundary)
        (worker: WorkerId)
        (session: SessionId option)
        (owner: string)
        (repo: string)
        (items: Item list)
        : (Chore.Chore * Ref) option =
        // 1. IDLE? — the evidence is derived from the board we already read, never asserted by us.
        //    `safePoint` is the only constructor, so "never offer to a worker holding a live lease" is the
        //    argument `offer` cannot be called without, rather than an `if` a refactor can drop.
        match Chore.safePoint boundary worker items with
        | None -> None
        | Some at ->
            // 2. IS THERE ANYTHING TO DO? — pure, free, and taken FROM the SafePoint so the idleness and
            //    the board it is spent on are one value. Ask BEFORE spending a REST request: on a healthy
            //    board this is the common case, and the lock lives on the budget the item CAS lives on
            //    (ADR-0034 §3).
            match Chore.offer at with
            | None -> None
            | Some chore ->
                // 3. WHOSE TURN? — `None` is every repo without a lock issue (the six receivers today).
                //    ADR-0041 verbatim: a chore queue that cannot find its lock offers nothing.
                match Options.choreLockRef owner repo with
                | None -> None
                | Some lockRef ->
                    // `Writes.claim`, unchanged, on another subject (ADR-0041). The board callback is
                    // `None` because the lock issue is not ON the board and must never be: `claim` reads a
                    // previous column only to restore it on release, and there is no column here to
                    // restore. That stub IS the chore-lock configuration — `WriteTests` has driven
                    // `claim` this way, against an arbitrary ref, for the whole of its life.
                    match Writes.claim transport LeaseMinutes worker session lockRef (fun () -> None) with
                    | Ok(Writes.Won _)
                    | Ok(Writes.Renewed _) -> Some(chore, lockRef)

                    // EVERY OTHER OUTCOME IS "NOT OURS", and they are one branch on purpose. `Lost` is the
                    // lock working — somebody else is draining this repo. `Twin` is #419, and a lock that
                    // cannot tell two workers apart is not one. `Undecided` and an unparseable marker are
                    // "I could not tell", which is never a yes (#266). None of them is an error the caller
                    // asked about: it asked for `next`.
                    | Ok _
                    | Error _ -> None
