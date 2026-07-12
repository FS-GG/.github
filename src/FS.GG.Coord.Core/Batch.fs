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
            | DeclaredNone -> false)
        |> Option.map (fun r -> r.Holder)

    /// A reservation that reserves nothing is the one thing this scheduler may not tolerate — see the
    /// `Red` leg on `schedule`. Returns the offending tokens, empty if the reservation is sound.
    let private unusableReservation (r: Reservation) = TouchSet.unmatchable r.Paths

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
            | OverlapsInFlight _
            | Undetermined _ ->
                // Not startable, and reserving nothing — precisely BECAUSE nobody is working it,
                // which is what separates this leg from the two above.
                ()

        for item in ordered do
            if not stop then
                step item

        Green
            { Chosen = List.rev chosen
              Decisions = List.rev decisions
              Truncated = truncated }
