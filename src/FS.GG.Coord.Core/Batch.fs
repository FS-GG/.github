namespace FS.GG.Coord

module Batch =

    open System
    open System.Text.RegularExpressions
    open Types
    open Schedulability

    type Holder =
        | LiveClaim of worker: WorkerId * item: Ref * ageSeconds: int * livePr: int option
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
          CollidedWith: Holder option
          /// The derived priority this candidate was ORDERED by (.github#1598). Carried on the decision
          /// rather than recomputed by the renderer: `--explain` must print the rank the fold actually
          /// used, and a second derivation is a second answer waiting to disagree with the first.
          Rank: Rank.Rank }

    type BatchResult =
        { Chosen: Item list
          Decisions: Decision list
          Truncated: bool }

    type WaveModel =
        { Waves: int
          ImplementerSlotsPerWave: int
          ReviewSlots: int
          ConsolidationThreshold: int }

    type WaveOccupancy =
        { ActiveItems: int
          WaveCapacity: int
          OpenSlots: int }

    let private waveModelPattern =
        Regex(
            @"<!--\s*fsgg:wave-model:v1\s+waves=(\d+)\s+implementer-slots-per-wave=(\d+)\s+review-slots=(\d+)\s+consolidation-threshold=(\d+)\s*-->",
            RegexOptions.CultureInvariant
        )

    let parseWaveModel (document: string) : Result<WaveModel, string> =
        let matches = waveModelPattern.Matches(document)

        if matches.Count <> 1 then
            Error $"expected exactly one fsgg:wave-model:v1 declaration, found %d{matches.Count}"
        else
            let m = matches[0]

            let values =
                [ for i in 1..4 -> Int32.TryParse(m.Groups[i].Value) ]

            match values with
            | [ (true, waves); (true, implementers); (true, reviewers); (true, consolidation) ]
                when waves = Protocol.wavePolicy.Waves
                     && implementers = Protocol.wavePolicy.ImplementerSlotsPerWave
                     && reviewers = Protocol.wavePolicy.ReviewSlots
                     && consolidation = Protocol.wavePolicy.ConsolidationThreshold ->
                Ok
                    { Waves = waves
                      ImplementerSlotsPerWave = implementers
                      ReviewSlots = reviewers
                      ConsolidationThreshold = consolidation }
            | _ -> Error "fsgg:wave-model:v1 must match the typed Protocol.wavePolicy"

    let waveOccupancy (model: WaveModel) (activeItems: Ref list) : WaveOccupancy =
        let active = activeItems |> List.distinct |> List.length
        let capacity = model.Waves * model.ImplementerSlotsPerWave

        { ActiveItems = active
          WaveCapacity = capacity
          OpenSlots = max 0 (capacity - active) }

    let renderWaveOccupancy (occupancy: WaveOccupancy) : string =
        $"wave occupancy: {{\"activeItems\":%d{occupancy.ActiveItems},\"waveCapacity\":%d{occupancy.WaveCapacity},\"openSlots\":%d{occupancy.OpenSlots}}}"

    let waveShortfallHeadline (schedulableItems: int) (occupancy: WaveOccupancy) : string option =
        if occupancy.OpenSlots > 0 && schedulableItems > 0 then
            Some
                $"WAVE SHORTFALL — %d{occupancy.OpenSlots} implementer slot(s) are open while %d{schedulableItems} schedulable item(s) exist; dispatch the next wave now."
        else
            None

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
            // A lapsed lease kept alive by an open PR (#581) has NO lease window to quote — the work is
            // demonstrably alive and the claim is NOT reapable, so `leaseWindow` would print the phantom
            // "lease EXPIRED — reapable" that #712 is about: advice `reap` will decline, one nudge away
            // from closing a live PR to clear the lane. Name the PR and point at the worker instead.
            | LiveClaim(WorkerId w, item, _, Some pr) ->
                $"%s{id} — overlaps in-flight work held by %s{w} on %s{item.Short} (lease lapsed, but PR #%d{pr} is open: the work is alive and this is NOT reapable — #581; talk to %s{w}, there is no lease window to wait out): %s{where}"

            | LiveClaim(WorkerId w, item, age, None) ->
                $"%s{id} — overlaps in-flight work held by %s{w} on %s{item.Short} (%s{Schedulability.leaseWindow leaseMinutes age}): %s{where}"

            | BatchMember item -> $"%s{id} — overlaps batch member %s{item.Short} (%s{where})"

            // IN PROGRESS, NO MARKER. Something is evidently editing those files, so the reservation is
            // real and must be honoured — but there is no worker to name, no lease to wait out, and
            // nobody to `say` to. Dressing it up as a holder ("held by — (lease unknown)") would invite a
            // worker to wait for a marker that is never coming.
            | Unowned item ->
                $"%s{id} — overlaps %s{item.Short}, which the board says is In progress with NO claim marker (someone is working outside the protocol — there is no lease to wait out; see: scripts/fsgg-coord who): %s{where}"

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
            | OverlapsInFlight _, Some(LiveClaim(w, holder, age, livePr)) ->
                Some
                    { Worker = w
                      Holder = holder
                      AgeSeconds = age
                      // DERIVED, not hardcoded (#712). This used to be `false` because the collided
                      // `LiveClaim` had thrown its liveness away at the reserve site, so there was
                      // nothing to set it from — which silently defeated the `leaseExpired` guard below
                      // for the one shape it was written for, letting the phantom "lease EXPIRED —
                      // reapable" reach the batch summary too. `livePr` restores the fact: `Some` proof
                      // of life is exactly the #581 claim that is over its lease yet not reapable.
                      KnownLiveWork = Option.isSome livePr }
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
                  $"  triage them to Ready, or schedule from Backlog as-is: scripts/fsgg-coord take --repo %s{one} --include-backlog" ]
            | many ->
                (count :: "  triage them to Ready, or schedule from Backlog as-is — one repo at a time:" :: [
                    for r in many -> $"    scripts/fsgg-coord take --repo %s{r} --include-backlog"
                ])

    /// THE DEADLOCK SECTION (#1092) — the one starved-queue cause that WAITING CANNOT FIX.
    ///
    /// Every other line this banner prints describes a queue that DRAINS: a lease frees, a column gets
    /// triaged, a worker lands their PR. A `Blocked by` ring drains never — each item waits on the next
    /// around a circle — and it used to print as `Status is Blocked`, per item, in the same five words
    /// `Schedulability.explain` gives a block that clears in ten minutes. Three such items sat on this board
    /// for two hours while `take` said `this queue is BUSY, not empty` over them, which was the most
    /// confident wrong impression available: **BUSY implies it drains.**
    ///
    /// This is the AGGREGATE `explainDecision` cannot give, which is this banner's whole charter. The blocker
    /// graph's four previous repairs (#343/#476/#602/#620) are every one of them per-item, and a ring passes
    /// all four because each item on it is individually well-formed. `Blockers.cycles` owns the graph
    /// question — including which edges are live — and this only puts its answer into English.
    ///
    /// It names each member WITH its in-ring blockers, because the remedy is to break ONE edge and a worker
    /// has to pick which: the edges are the actionable part, not the membership. Blockers pointing OUT of the
    /// ring are omitted — they are not what makes it a ring, and cutting one would free nothing.
    let private cycleSection (result: BatchResult) : string list =
        let items =
            result.Decisions
            |> List.map (fun d -> d.Item)
            |> List.fold
                (fun acc (i: Item) ->
                    if List.exists (fun (x: Item) -> x.Ref = i.Ref) acc then
                        acc
                    else
                        i :: acc)
                []
            |> List.rev

        match Blockers.cycles (items |> List.map (fun i -> i.Ref, i.Blockers)) with
        | [] -> []
        | rings ->
            let byRef = items |> List.map (fun i -> i.Ref, i) |> Map.ofList

            [ for ring in rings do
                  let inRing = Set.ofList ring

                  $"DEADLOCKED: %d{List.length ring} item(s) form a `Blocked by` CYCLE — NONE of them can EVER be startable, and no lease frees them:"

                  for r in ring do
                      match Map.tryFind r byRef with
                      | Some item ->
                          let holds =
                              item.Blockers
                              |> List.filter (Blockers.isResolved >> not)
                              |> List.choose (fun b -> b.Ref)
                              |> List.filter inRing.Contains
                              |> List.map (fun x -> x.Short)
                              |> List.distinct
                              |> String.concat ", "

                          $"  %s{r.Short} — Blocked by %s{holds}"
                      | None -> ()

                  "  This is a BUG, not a wait. Break ONE edge: re-read each item's `Blocked by` premise — the"
                  "  edge to cut is the one whose premise is spent (the overlap was retracted, the work merged)."
                  "  scripts/fsgg-coord set-field <ref> 'Blocked by' ''" ]

    /// Returns [] whenever the queue is NOT starved: work WAS handed out (a `chosen` batch is not starved),
    /// or nothing is queued behind a live claim AND nothing was withheld at the column AND no `Blocked by`
    /// RING was found. A queue starved by ordinary blockers is still #440's business, told per-item — but a
    /// ring is not ordinary: no per-item verdict can see it, and it never clears (#1092), so it leads the
    /// banner rather than staying silent. A banner on a healthy queue is noise that trains workers to skip
    /// stderr — the very habit #440 was closed to break.
    let starvedBanner (leaseMinutes: int) (result: BatchResult) : string list =
        if not (List.isEmpty result.Chosen) then
            []
        else
            let backlog = backlogSection result
            // The ring leads, and nothing folds it into a line about waiting. It is the only cause here that
            // no lease and no triage ever clears, and a worker who reads "BUSY — soonest lease frees in ~89m"
            // stops reading. When a ring is the ONLY reason nothing is startable, this section is the whole
            // banner: that queue is held by no claim and withheld at no column, so every line below is
            // silent, and `take` printed "nothing schedulable right now." over a permanent deadlock with no
            // banner at all. That is the worst case, and it is the one that happened (#1092).
            let deadlock = cycleSection result

            let drainable =
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
                            [ $"  %d{expiredLeases} of those lease(s) have EXPIRED — collect them: scripts/fsgg-coord reap --repo %s{one} --apply" ]
                        | many ->
                            ($"  %d{expiredLeases} of those lease(s) have EXPIRED — collect them, one repo at a time:"
                             :: [ for r in many -> $"    scripts/fsgg-coord reap --repo %s{r} --apply" ])

                    // #636 — BUSY and UNTRIAGED are independent facts about one queue, and the live board that
                    // prompted this had both: two items behind live claims (soonest ~120m) and seven withheld at
                    // the column. Reporting only the first told the worker to wait two hours over work that was
                    // one flag away. So the sections compose; neither suppresses the other.
                    banner @ reapAdvice @ backlog

            // #636's composition rule, extended to the ring — with the one asymmetry that matters. BUSY and
            // UNTRIAGED are both true of a queue that DRAINS; a ring is true of one that does not. So it
            // leads, and it is the entire banner when it is the only cause.
            deadlock @ drainable

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
            // A chore reserves nothing, so it holds no token either — same as the sentinels below.
            | DeclaredChore
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

    let scheduleWith
        (boardCounts: Map<Ref, int>)
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
                    | LiveClaim(WorkerId w, item, _, _) -> $"worker %s{w} on %s{item.Short}"
                    | BatchMember item -> $"batch member %s{item.Short}"
                    | Unowned item -> $"%s{item.Short} (in progress, no claim marker)"
                    | UnknownHolder -> "an unnameable holder"

                let toks = String.concat ", " bad

                $"in-flight work held by %s{who} declares unmatchable touch-set token(s): %s{toks} — it therefore reserves NOTHING, and scheduling against an unknown touch-set would hand its files to a second worker. Fix with: scripts/fsgg-coord widen <issue> --paths '<paths>'")
            |> Red

        | [] ->

        // PRIORITY-GREEDY, not merely maximal (.github#1598). The disjointness guarantee is unchanged —
        // this still admits a candidate only if it clears everything already reserved — but the ORDER the
        // fold walks is now the derived rank, so the highest-ranked schedulable item is ALWAYS admitted
        // rather than losing its lane to whichever lower-value item happened to have a smaller number.
        // That is the exact failure this replaces: on 2026-07-27 three P0 items were refused as
        // "overlaps batch member #1560" while #1560 was hardening, and the only remedy available was to
        // park nineteen rows in `Backlog`.
        //
        // DETERMINISM IS STILL THE POINT, and it survives: every rank term is a fact about the board, and
        // the issue NUMBER is the final term, so two workers reading one cached window (#418) still
        // compute one batch. What is NOT stable is the batch across DIFFERENT windows — rank moves as the
        // board moves, so a caller must not assume two `batch` calls return the same set (a risk this
        // item states rather than leaves to be discovered).
        //
        // The ranks are computed ONCE, here, and travel on each `Decision` so the renderer prints the rank
        // the fold used. The ranks travel WITH their items through the fold — never re-looked-up by ref. A
        // `Map` keyed on `Ref` was the obvious spelling for THAT and it is the wrong one: it would
        // silently collapse two board cards pointing at one issue into a single entry, and it forced an
        // unreachable "rank not found" default, which is a branch nothing can ever test.
        //
        // `boardCounts` IS keyed on `Ref`, and that is a different question with a different answer: a
        // COUNT is a property of the blocked-upon ISSUE, so two cards pointing at one issue genuinely
        // share one count. It is a lookup table, not the carrier.
        //
        // **THE COUNTS COME FROM THE CALLER, NOT FROM `candidates`** (.github#1628). They used to be
        // derived here, from the candidate set, and `Scan.snapshot` scopes that set with `--repo` — so an
        // item three items in another repo were `Blocked by` counted 0 under `batch --repo <its repo>`
        // and 3 under a bare `batch`. Same board, same instant, two ranks, and the scoped spelling is the
        // one every worker runs. The blocking GRAPH is a whole-board fact; the candidate LIST is a scoped
        // projection of it, and deriving the former from the latter truncated it silently — no error, the
        // batch still well-formed, still disjoint, still deterministic, and ordered by a count that was
        // wrong in the direction that matters most.
        let ordered = Rank.ofItemsWith boardCounts candidates |> List.sortBy (snd >> Rank.key)

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
                      Repo = item.PathRepo
                      Paths = item.TouchSet
                      Holder = holder } ]

        let ageOf (item: Item) =
            match item.Claim with
            | Some(c, _) -> c.AgeSeconds
            | None -> 0

        let step (item: Item, rank: Rank.Rank) =
            let owner, repo = item.Ref.Owner, item.PathRepo

            // Only this repo's reservations. The ORDER is part of the contract: `inFlight` precedes
            // the batch members appended below, so a candidate colliding with both reports the LIVE
            // CLAIM — the collision that has a lease and a worker behind it, and therefore the only
            // one an operator can actually act on.
            let visible = reserved |> inRepo owner repo |> List.map (fun r -> r.Paths)

            let result = schedulable allowBacklog visible item

            let collidedWith, reportedResult =
                match result with
                | OverlapsInFlight((_, reservedToken) :: _ as hits) ->
                    // `conflicts` yields (candidateToken, reservedToken), and the RESERVED side is
                    // the key that joins this collision back to its owner (#428). Name the holder,
                    // not just the files: nobody can wait for, or talk to, a pair of paths.
                    let holder = holderOf reserved owner repo reservedToken

                    // `schedulable` aggregates every collision so the fold can still make its one
                    // safety decision. The passed-over sentence, however, names one holder: keep its
                    // evidence to that holder's tokens rather than attributing another reservation to
                    // the first worker in the aggregate (#2229).
                    let holderHits =
                        holder
                        |> Option.map (fun h ->
                            hits
                            |> List.filter (fun (_, token) -> holderOf reserved owner repo token = Some h))
                        |> Option.defaultValue hits

                    holder |> Option.orElse (Some UnknownHolder), OverlapsInFlight holderHits
                | _ -> None, result

            decisions <-
                { Item = item
                  Result = reportedResult
                  CollidedWith = collidedWith
                  Rank = rank }
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
            | HeldBy worker -> reserve item (LiveClaim(worker, item.Ref, ageOf item, None))
            // #712's root cause was HERE: the PR that proves this claim is alive (#581) was dropped
            // (`_`), so every later item that collided with this reservation saw a liveness-less
            // `LiveClaim` and was told to wait out a lease that will never free. Carry it through.
            | HeldByLiveWork(worker, pr) -> reserve item (LiveClaim(worker, item.Ref, ageOf item, Some pr))

            | WrongStatus _
            | IssueClosed
            | NoTouchSet
            | DeliberatelyNoTouchSet
            | UnusableTouchSet _
            | BlockedBy _
            | AwaitingHuman _
            | ItemPrOpen _
            | OverlapsInFlight _
            | Undetermined _ ->
                // Not startable, and reserving nothing. `ItemPrOpen` is the #651 leg: an open `item/<n>-*`
                // PR with no marker means someone is implementing it, but there is no claim to name and no
                // lease to wait out — so, like a markerless Ready row, it reserves nothing here; it is simply
                // not handed out (which is what stops the duplicate implementation).
                ()

        for ranked in ordered do
            if not stop then
                step ranked

        Green
            { Chosen = List.rev chosen
              Decisions = List.rev decisions
              Truncated = truncated }

    /// `scheduleWith` when the candidate list IS the whole board — the counts are derived from it.
    ///
    /// This is the honest answer for a caller holding no wider view, and there is one: `decide --snapshot`
    /// is handed a document and nothing else, exactly as it is already handed no `Phase` and no age
    /// (.github#1598). It is `Rank`'s no-priority-data direction — an undercount can only sort an item
    /// LATER, never promote one — which is the same fail-open-downward posture every other unread rank
    /// input takes.
    ///
    /// **THE WORKER LOOP MUST NOT USE THIS** (.github#1628). `batch`/`next`/`take` scope their candidates
    /// with `--repo`, and a scoped list is a projection of the graph rather than the graph — see
    /// `Rank.blockingCounts`. Those paths hold the unscoped scan rows already and pass whole-board counts
    /// to `scheduleWith`.
    let schedule
        (allowBacklog: bool)
        (limit: int option)
        (inFlight: Reservation list)
        (candidates: Item list)
        : Verdict<BatchResult> =
        scheduleWith (Rank.blockingCounts candidates) allowBacklog limit inFlight candidates

    /// How many candidates this batch member DISPLACED — passed over because they collided with IT, and
    /// with it specifically.
    ///
    /// This is the number `--explain` owes a reader, and it is the cost side of priority-greedy packing:
    /// one wide high-rank item can exclude several narrow ones that a maximal pack would have run
    /// together. That is the correct trade — priority means the important thing runs — but a trade nobody
    /// is told about is indistinguishable from a regression in parallelism, so it is REPORTED.
    ///
    /// A refused candidate names exactly one holder (`schedule` resolves the FIRST colliding reserved
    /// token), so nothing is double counted.
    let displacedBy (result: BatchResult) (member': Ref) : int =
        result.Decisions
        |> List.filter (fun d ->
            match d.Result, d.CollidedWith with
            | OverlapsInFlight _, Some(BatchMember m) -> m = member'
            | _ -> false)
        |> List.length

    /// `batch --explain` (.github#1598 AC5) — the ordering, made INSPECTABLE.
    ///
    /// Before this the only way to learn why a P0 item was absent from a batch was to read refusal prose
    /// and infer; there was no way at all to learn what ORDER the candidates were considered in, because
    /// the order was the issue number and the issue number is not a priority. A scheduler whose ordering
    /// cannot be inspected is one nobody trusts, and the driver that filed this item spent an afternoon
    /// proving exactly that.
    ///
    /// One line per candidate IN DECISION ORDER — which is rank order, so the list itself is the ranking
    /// — carrying the verdict, every rank input that produced its position, and for an admitted item the
    /// number of lanes it displaced. Deliberately not a table: these lines are read on a terminal beside
    /// stderr refusal prose, and a column layout that wraps is worse than a sentence that does not.
    let explainRanking (result: BatchResult) : string list =
        let header =
            [ "RANKING (.github#1598) — candidates in the order the scheduler considered them."
              "  rank inputs, in lexicographic order: starvation escalation, blocking count, Class, Phase, age, issue number." ]

        let lines =
            result.Decisions
            |> List.map (fun d ->
                let id = d.Item.Ref.Short
                let inputs = Rank.explain d.Rank

                // `OverlapsInFlight` tells the reader THAT a lane was unavailable; `CollidedWith`
                // tells them WHO made it unavailable. Keep those facts together here rather than
                // asking the operator to reconstruct the reservation from a rank-only refusal.
                // `UnknownHolder` and an absent join remain explicit absences: inventing a worker or
                // item would turn a conservative scheduler answer into a false instruction.
                let overlapHolder =
                    match d.Result, d.CollidedWith with
                    | OverlapsInFlight _, Some(LiveClaim(WorkerId worker, holder, _, _)) ->
                        $"held by %s{worker} on %s{holder.Short}"
                    | OverlapsInFlight _, Some(BatchMember holder) -> $"blocked by batch member %s{holder.Short}"
                    | OverlapsInFlight _, Some(Unowned holder) ->
                        $"reserved by markerless In-progress item %s{holder.Short}"
                    | OverlapsInFlight _, Some UnknownHolder -> "holder unknown"
                    | OverlapsInFlight _, None -> "holder unavailable"
                    | _ -> ""

                match d.Result with
                | Startable ->
                    match displacedBy result d.Item.Ref with
                    | 0 -> $"  ADMITTED %s{id} — %s{inputs}"
                    | n -> $"  ADMITTED %s{id} — %s{inputs}; displaced %d{n} lower-ranked candidate(s) from a lane"
                | OverlapsInFlight _ when Rank.isUnranked d.Rank ->
                    // SAID OUT LOUD, because it is the safety property AC1 turns on: an item with no
                    // priority evidence is not being punished, it simply has nothing to sort on, and a
                    // board of such items schedules exactly as it did before this existed.
                    $"  refused  %s{id} — %s{inputs} (no priority evidence: sorts last); %s{overlapHolder}"
                | _ when Rank.isUnranked d.Rank ->
                    $"  refused  %s{id} — %s{inputs} (no priority evidence: sorts last)"
                | OverlapsInFlight _ -> $"  refused  %s{id} — %s{inputs}; %s{overlapHolder}"
                | _ -> $"  refused  %s{id} — %s{inputs}")

        // Silent on an empty candidate set rather than printing a header over nothing.
        match lines with
        | [] -> []
        | _ -> header @ lines
