namespace FS.GG.Coord.Cli

module Chores =

    open System
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
        //
        // `scripts/fsgg-coord`, NEVER a bare `fsgg-coord` (#931, #569): the bare name is not on PATH, so a
        // worker pasting exactly what the tool told them gets `command not found`. The resolver is what
        // runs, and it resolves from an item worktree too — which is where pnext-item §2 puts every reader
        // of this line. The `worker-id-attractor` gate holds this, and it caught this line.
        String.concat
            "\n"
            [ $"chore [%s{chore.Size.Label}] %s{chore.Statement}"
              $"  you hold %s{lockRef.Short}, the chore lock for this repo (%d{LeaseMinutes}m)."
              $"  do it, or hand it back now:  scripts/fsgg-coord release %s{lockRef.Short}" ]

    let offer
        (transport: Transport.IGitHubTransport)
        (boundary: Chore.Boundary)
        (worker: WorkerId)
        (session: SessionId option)
        (extra: Ref list)
        (owner: string)
        (repo: string)
        (observed: Chore.Board)
        : (Chore.Chore * Ref) option =
        // 1. WHOSE LOCK? — FIRST, because it is what defines the SCOPE everything below reasons over, and
        //    because it is a pure string match that spends nothing. `None` is every repo without a lock
        //    issue (the six receivers today): ADR-0041 verbatim, a chore queue that cannot find its lock
        //    offers nothing.
        match Options.choreLockRef extra owner repo with
        | None -> None
        | Some lockRef ->
            // 2. THE LOCK IS PER-REPO, SO THE BOARD IT IS SPENT ON MUST BE TOO.
            //
            //    `items` is NOT reliably one repo's: the FS-GG board is org-wide (1,170 rows across 7 repos
            //    when measured), and `Scan.scope None` — which is what a bare `next`, with no `--repo`,
            //    asks for — returns every one of them. Deriving over that and locking `.github#1033` would
            //    hand a worker an `FS.GG.Rendering` chore under `.github`'s lock: the subject and the lock
            //    that serialises it would name different repos, which is not what a PER-REPO lock means
            //    (ADR-0041). Two workers could then hold two different repos' locks and be handed the SAME
            //    chore — condition 1 defeated by the very mechanism meant to enforce it.
            //
            //    Scoped on `lockRef.Repo`, never the `repo` argument: `choreLockRef` canonicalises
            //    (`governance` → `FS.GG.Governance`), so the lock's own ref is the only spelling that is
            //    certainly canonical. Comparing against the raw argument would drop every row on a caller
            //    who typed a short id — offering nothing, silently, on a board full of chores.
            let items =
                match observed with
                | Chore.Whole xs
                | Chore.Filtered xs -> xs

            let ours =
                items
                |> List.filter (fun i ->
                    // repo-filter-monopoly: exempt — REF-to-REF, not a `--repo` filter. This asks "which
                    // items live in THIS LOCK's repo?" — the same shape as `widen`'s, and a question
                    // `--repo` has no part in. `repo` reaches this function only to RESOLVE the lock, and
                    // by here it is spent: the scope is `lockRef`'s, so a caller who named no repo at all
                    // is still scoped, which `Scan.scope opts.Repo` (correctly) would not do. Routing this
                    // through the funnel is also not possible in the type: `Scan.scope` takes `Scan.Row`s
                    // and these are `Types.Item`s — the funnel sits upstream of the snapshot these came
                    // out of, and it already ran there.
                    String.Equals(i.Ref.Repo, lockRef.Repo, StringComparison.OrdinalIgnoreCase))

            // 3. IDLE? — over the WHOLE board (`observed`), deriving over `ours`. ONE mint, two arguments.
            //
            //    Idleness is a fact about the WORKER; a chore is a fact about the REPO. A worker holding a
            //    live claim in FS.GG.SDD is mid-item with a live touch-set, and is exactly who must not be
            //    handed a side-quest — asking `ours` would not SEE that claim and would call them idle.
            //
            //    THIS USED TO MINT TWICE and say so: once over `items` for idleness, once over `ours` to
            //    spend, reasoning that the second could not refuse because `ours` is a subset of the first.
            //    The reasoning was sound and the PREMISE was not — `items` was only ever "every row we were
            //    GIVEN", and `next --repo <r>` gives a board `Scan.scope` already filtered, so the honest
            //    question was put to a board that could not answer it (#1086). The scope now rides in the
            //    type: `observed` is `Chore.Whole` or `Chore.Filtered`, `safePoint` refuses the latter, and
            //    the subject is a second argument rather than a subset argument to get right.
            match Chore.safePoint boundary worker observed ours with
            | None -> None
            | Some at ->
                // 4. IS THERE ANYTHING TO DO? — pure and free, and asked BEFORE spending a REST
                //    request: on a healthy board "nothing" is the common case, and the lock lives on
                //    the budget the item CAS itself lives on (ADR-0034 §3).
                match Chore.offer at with
                | None -> None
                | Some chore ->
                    // 5. `Writes.claim`, unchanged, on another subject (ADR-0041). The board callback
                    //    is `None` because the lock issue is not ON the board and must never be:
                    //    `claim` reads a previous column only to restore it on release, and there is no
                    //    column here to restore. That stub IS the chore-lock configuration — and
                    //    `WriteTests` has driven `claim` this way, against an arbitrary ref, for the
                    //    whole of its life.
                    //
                    //    `RefuseLiveHolder`, and there is no flag that changes it (#1620). The steal is a
                    //    RECOVERY route for an item whose holder died with written work stranded on it; a
                    //    chore lock holds no work, so there is nothing to recover and a live holder simply
                    //    means somebody else is already draining this repo. Forcing it would put two
                    //    reconcilers on one board — the one thing this lock exists to prevent.
                    match
                        Writes.claim transport LeaseMinutes Writes.RefuseLiveHolder ignore worker session lockRef (fun () ->
                            None)
                    with
                    | Ok(Writes.Won _)
                    | Ok(Writes.Renewed _) -> Some(chore, lockRef)

                    // EVERY OTHER OUTCOME IS "NOT OURS", and they are one branch on purpose. `Lost` is
                    // the lock WORKING — somebody else is draining this repo. `Twin` is #419, and a
                    // lock that cannot tell two workers apart is not one. `Undecided` and an
                    // unparseable marker are "I could not tell", which is never a yes (#266). None of
                    // them is an error the caller asked about: it asked for `next`.
                    | Ok _
                    | Error _ -> None
