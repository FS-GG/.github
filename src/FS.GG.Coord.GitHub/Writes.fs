namespace FS.GG.Coord.GitHub

module Writes =

    open System
    open System.Text.Json
    open System.Text.RegularExpressions
    open FS.GG.Coord
    open FS.GG.Coord.Types
    open Errors
    open Transport

    [<Sealed>]
    type Held
        internal
        (
            ref: Ref,
            worker: WorkerId,
            markerId: int64,
            session: SessionId option,
            previousStatus: BoardStatus option,
            pathRepo: string option
        ) =
        member _.Ref = ref
        member _.Worker = worker
        member _.MarkerId = markerId
        member _.Session = session
        member _.PreviousStatus = previousStatus
        member _.PathRepo = pathRepo

    type ClaimForce =
        | RefuseLiveHolder
        | StealLiveHolder

    type SelfIdentity =
        | Derives of WorkerId
        | DerivesNothing

    type ClaimOutcome =
        | Won of held: Held * collected: WorkerId list
        | Renewed of held: Held * collected: WorkerId list
        | Stolen of held: Held * from: WorkerId list * collected: WorkerId list
        | Lost of WorkerId
        | Twin of theirs: SessionId
        | Impersonates of derived: WorkerId * named: WorkerId
        | Undecided of reason: string
        | BlockedByUnparseableMarker

    type HeldOutcome =
        | Holds of Held
        | DoesNotHold
        | TwinHolds of theirs: SessionId
        // `ImpersonatesHolder`, not a second `Impersonates`: `ClaimOutcome` already has that name, and two
        // cases spelled alike in one module shadow each other at every unqualified construction. This is the
        // `Twin`/`TwinHolds` pairing's reason, and its spelling.
        | ImpersonatesHolder of derived: WorkerId * named: WorkerId

    [<Sealed>]
    type Reapable
        internal (ref: Ref, worker: WorkerId, markerId: int64, previousStatus: BoardStatus option) =
        member _.Ref = ref
        member _.Worker = worker
        member _.MarkerId = markerId
        member _.PreviousStatus = previousStatus

    type ReapRefusal =
        | WorkAlive of pr: int
        | WorkAliveBranch
        | Undetermined of reason: string

    [<Sealed>]
    type Validated internal (tokens: string list) =
        member _.Tokens = tokens

    [<Sealed>]
    type Rewritten internal (body: string) =
        member _.Body = body

    /// The worker id a marker carries when we could not parse one out of it. A claim held by NOBODY, which
    /// BLOCKS — see `Reads.parseMarker`.
    [<Literal>]
    let private UnparsedMarker = "unparsed-marker"

    // ---- the marker body -------------------------------------------------------------------------

    /// `%` is encoded FIRST, so that decoding can take it LAST. Get this order wrong and a status
    /// containing a literal `%20` round-trips into a space that was never in it.
    let private encodeStatus (s: string) =
        s.Replace("%", "%25").Replace(" ", "%20")

    /// THE MARKER. `worker=` MUST stay the first key — the parser anchors on it, and the anchor is what
    /// stops a `say` message that merely QUOTES a marker from forging a lock.
    let private markerBody
        (worker: WorkerId)
        (session: SessionId option)
        (leaseMinutes: int)
        (previousStatus: BoardStatus option)
        (pathRepo: string option)
        =
        let sessionPart =
            match session with
            | Some(SessionId s) -> $" session=%s{s}"
            | None -> ""

        let prevPart =
            match previousStatus with
            | Some s -> $" prev=%s{encodeStatus (statusWireName s)}"
            // A COLUMN NOBODY RECORDED CANNOT BE RESTORED (#481). Emitting `prev=` with an empty value
            // would be a recorded decision to restore nothing, which is a different claim from having made
            // no observation at all.
            | None -> ""

        let pathRepoPart = pathRepo |> Option.map (fun p -> $" pathRepo=%s{p}") |> Option.defaultValue ""

        $"<!-- fsgg:claim worker=%s{worker.Value} lease=%d{leaseMinutes}%s{sessionPart}%s{prevPart}%s{pathRepoPart} -->"

    /// IS A MARKER BEARING OUR WORKER ID ACTUALLY A TWIN'S? Returns the OTHER session when it is.
    ///
    /// ONE PREDICATE, TWO CALLERS — `claim` (which refuses to adopt a twin's lock) and `verifyHeld` (which
    /// refuses to hand out the capability over it). It is factored here because the two must agree BY
    /// CONSTRUCTION: a `claim` that calls a marker a twin and a `verifyHeld` that calls the same marker ours
    /// would mean the tool refuses you the lock and then authorises you to delete it. Two implementations of
    /// one question is #485's shape (one question, five implementations, agreeing in none), and this is the
    /// question the protocol can least afford to answer twice.
    ///
    /// TWIN ONLY WHEN BOTH SESSIONS ARE KNOWN. A sessionless marker — a human, a harness that exports none,
    /// any marker minted before #419 — is genuinely indistinguishable from ours, and failing closed on it
    /// would lock workers out of items they really hold. Our own session is never a twin, or a worker could
    /// neither renew nor verify its own lease.
    let private twinSession (ours: SessionId option) (theirs: SessionId option) : SessionId option =
        match ours, theirs with
        | Some(SessionId o), Some(SessionId t) when o <> t -> Some(SessionId t)
        | _ -> None

    /// IS THE CALLER ASKING TO ACT AS SOMEBODY ELSE? Returns the id it derived for ITSELF when it is (#1646).
    ///
    /// ONE PREDICATE, TWO CALLERS — `claim` and `verifyHeld` — for `twinSession`'s reason, verbatim: these are
    /// `Held`'s only two doors, and a `claim` that takes a lock `verifyHeld` then refuses to verify (or the
    /// reverse) would mean the tool hands you the lock in one verb and denies it in another.
    ///
    /// AND EACH CALLER ASKS IT ONCE, FOR ITS WHOLE FUNCTION. `claim` asked it on the re-claim arm alone at
    /// first, on the reasoning that its other arms CREATE a marker rather than adopt one. Review found that
    /// `--force` breaks the reasoning — it deletes the holder's live marker on the way, and then signs
    /// #1620's theft notice with the named worker's name — and the fresh-CAS arm plants a lock whose creator
    /// cannot then drop it. Two of the three exceptions were the hole again. So: no arms.
    ///
    /// It compares the named `worker` against what this PROCESS resolves for itself with `--worker` taken
    /// away. That is the only fact in the whole exchange the flag cannot restate: the id came off the board,
    /// and under a shared harness session the session came from a sibling, so both of the older legs match on
    /// facts the impersonator legitimately holds.
    ///
    /// UNASKABLE IS NOT "NO". `DerivesNothing` — a human, a harness exporting no session and no
    /// `$FSGG_WORKER` — has nothing to compare, exactly as a caller with no session of its own can never
    /// conclude "twin". It returns `None` there because the question cannot be put, not because the answer is
    /// clean, and #1646 records that residue rather than dressing it up: a caller that unsets its own identity
    /// before impersonating arrives here and is indistinguishable from the operator this arm exists for.
    let private impersonated (self: SelfIdentity) (worker: WorkerId) : WorkerId option =
        match self with
        | Derives d when d <> worker -> Some d
        | Derives _
        | DerivesNothing -> None

    // ---- comment primitives ----------------------------------------------------------------------

    let private postComment (transport: IGitHubTransport) (ref: Ref) (body: string) : IoResult<int64> =
        let payload =
            let o = Nodes.JsonObject()
            o.["body"] <- Nodes.JsonValue.Create body
            o.ToJsonString()

        let request =
            { Method = "POST"
              Path = $"repos/%s{ref.Owner}/%s{ref.Repo}/issues/%d{ref.Number}/comments"
              Query = []
              Body = Json payload
              Budget = Rest
              IfNoneMatch = None
              Subject = ref.Short }

        match transport.Send request with
        | Error e -> Error e
        | Ok response ->
            try
                use doc = JsonDocument.Parse response.Body

                match doc.RootElement.TryGetProperty "id" with
                | true, v when v.ValueKind = JsonValueKind.Number -> Ok(v.GetInt64())
                | _ ->
                    // WE POSTED SOMETHING AND CANNOT NAME IT. This is the worst shape available to the CAS:
                    // a marker exists on the issue and we do not know its id, so we can neither win with it
                    // nor delete it. It must be loud.
                    Error(
                        Malformed(
                            ref.Short,
                            "the comment was posted but the response carried no id — we cannot identify our own marker, and therefore cannot win or withdraw it"
                        )
                    )
            with :? JsonException as e ->
                Error(Malformed(ref.Short, $"the comment-post response is not JSON: %s{e.Message}"))

    /// Delete a comment. **A 404 IS SUCCESS.**
    ///
    /// "Already gone" is the goal state. Two workers collecting the same expired marker must not turn the
    /// loser's benign 404 into a hard error — and, more sharply, a CAS backing off must be able to withdraw
    /// its own marker even if somebody else got there first.
    ///
    /// Non-zero means the comment IS STILL THERE, and that is the only thing a caller cares about.
    let private deleteComment (transport: IGitHubTransport) (ref: Ref) (commentId: int64) : IoResult<unit> =
        let request =
            { Method = "DELETE"
              Path = $"repos/%s{ref.Owner}/%s{ref.Repo}/issues/comments/%d{commentId}"
              Query = []
              Body = NoBody
              Budget = Rest
              IfNoneMatch = None
              Subject = ref.Short }

        match transport.Send request with
        | Ok _ -> Ok()
        | Error(NotFound _) -> Ok()
        | Error e -> Error e

    let private patchComment
        (transport: IGitHubTransport)
        (ref: Ref)
        (commentId: int64)
        (body: string)
        : IoResult<unit> =
        let payload =
            let o = Nodes.JsonObject()
            o.["body"] <- Nodes.JsonValue.Create body
            o.ToJsonString()

        let request =
            { Method = "PATCH"
              Path = $"repos/%s{ref.Owner}/%s{ref.Repo}/issues/comments/%d{commentId}"
              Query = []
              Body = Json payload
              Budget = Rest
              IfNoneMatch = None
              Subject = ref.Short }

        transport.Send request |> Result.map ignore

    // ---- THE CAS ---------------------------------------------------------------------------------

    let claimScoped
        (transport: IGitHubTransport)
        (leaseMinutes: int)
        (force: ClaimForce)
        (onEvict: WorkerId list -> unit)
        (worker: WorkerId)
        (self: SelfIdentity)
        (session: SessionId option)
        (ref: Ref)
        (readPreviousStatus: unit -> BoardStatus option)
        (readPathRepo: unit -> string option)
        : IoResult<ClaimOutcome> =

        // 0. ARE WE THE WORKER WE SAY WE ARE? (#1646) — BEFORE THE READ, AND BEFORE ANY ARM.
        //
        //    THE CHECK IS NOT ON AN ARM, BECAUSE THE HOLE WAS NOT ON ONE. This first went in on the re-claim
        //    arm alone, on the reasoning that the OTHER arms create a marker rather than adopt one, and that
        //    creating a lock under somebody else's name is a different act from taking theirs. Review found
        //    the reasoning does not survive contact with `--force`, and the measurement is unambiguous:
        //
        //      $ FSGG_WORKER=kite-461 fsgg-coord-engine claim FS.GG.SDD#42 --force --worker smew-f31
        //      STOLE FS.GG.SDD#42 from worker 'vole-418' (--force)
        //      STOLE FS.GG.SDD#42 for worker smew-f31 (--force; lock held; ...)      rc=0
        //      <!-- fsgg:msg from=smew-f31 to=vole-418 -->
        //
        //    `kite-461` destroyed `vole-418`'s live lock, and the notice #1620 requires — the one that makes
        //    a steal accountable — was posted over `smew-f31`'s name. `smew-f31` did nothing. So the steal
        //    does not merely BYPASS #1620's accounting, which is what #1646 says `release --worker` does; it
        //    FALSIFIES it, writing a false attribution into the only surviving record of a destroyed lock.
        //
        //    And the fresh-CAS arm is the same code path (`postAndResolve`) reached with nothing to evict: it
        //    plants a marker under a name its own creator then cannot heartbeat or release, because every
        //    verb that would operate it goes through `verifyHeld` and is refused. A lock nobody can drop.
        //
        //    ONE QUESTION, ASKED ONCE, FOR THE WHOLE FUNCTION. A rule applied to three of four arms is the
        //    shape this item is about — and here it would leave `claim` and `verifyHeld` disagreeing, which
        //    is precisely what `impersonated` is factored to make impossible.
        //
        //    IT COSTS NOTHING AND SPENDS NOTHING. It is pure, it precedes the marker read, and a refusal
        //    therefore touches neither the network nor the item. `DerivesNothing` — the human operator, the
        //    harness that exports no session — is unaffected, as everywhere else.
        match impersonated self worker with
        | Some derived -> Ok(Impersonates(derived, worker))
        | None ->

        // 1. READ THE LIVE MARKERS. A failed read here is fatal and we have posted nothing, so there is no
        //    marker to clean up — this is the only cheap place to fail, and it is why the read comes first.
        match
            Reads.markerScan transport ref.Owner ref.Repo ref.Number
            |> Result.bind (Reads.requireCompleteMarkerScan ref.Short)
        with
        | Error e -> Error e
        | Ok before ->

        let liveBefore = Reads.winner leaseMinutes before

        // COLLECT THE STALE DEBRIS ON AN ITEM WE HAVE WON. A stale marker is a lapsed lease, and the next
        // claimant must COLLECT it, never merely out-order it: an ignored stale marker is exactly what
        // `heartbeat` resurrects underneath the new holder — two live markers, one item. So once our live
        // marker is the winner, delete every OTHER stale marker on the item (a 404 is success — a peer may
        // have collected the same one, the concurrent-GC race), and hand back the workers we evicted so the
        // caller can TELL them. Our OWN stale marker (a claim of ours that went stale) is deleted too, so
        // exactly one marker survives, but is not returned: you do not message yourself. Best-effort — a
        // stale marker we could not delete is left for `reap`, never a reason to fail a claim already won.
        let collectStale (winnerId: int64) (markers: Reads.Marker list) : WorkerId list =
            markers
            |> List.filter (fun m -> m.Id <> winnerId && Reads.isStale leaseMinutes m)
            |> List.choose (fun m ->
                match deleteComment transport ref m.Id with
                | Ok() -> Some m.Worker
                | Error _ -> None)
            // Our OWN stale marker is deleted but is not a notification (you do not message yourself); and an
            // unparseable marker that is merely STALE is debris worth deleting, but its `worker` is a sentinel
            // — `say`ing to "unparsed-marker" addresses no worker and posts a comment nobody reads.
            |> List.filter (fun w -> w <> worker && w <> WorkerId UnparsedMarker)

        // STEPS 2 AND 3 OF THE CAS — POST OUR MARKER, RE-READ, TAKE THE LOWEST LIVE ID AS THE WINNER.
        //
        // A FUNCTION because TWO paths reach it: an item with no live holder, and the #1620 steal, which
        // EVICTS the live holder first and then takes the item. The steal is deliberately not a second,
        // parallel lock protocol — it clears the way and then races exactly like every other claimant, so a
        // third worker arriving mid-steal is still resolved by comment order rather than by who forced.
        // `evicted` is empty on the ordinary path, and it is the only thing that separates `Won` from
        // `Stolen`: the outcome names the theft because a silent transfer is worse than a refusal.
        let postAndResolve (evicted: WorkerId list) : IoResult<ClaimOutcome> =
            // THIS is the linearisation point, and the only place the pre-claim column is worth a point
            // (#481): we have decided to post, no live marker stands in the way, and one line further on
            // the board will say `In progress` and the answer will be gone. A lost race or a re-claim
            // never reaches here, so neither pays the read.
            let previousStatus = readPreviousStatus ()
            let pathRepo = readPathRepo ()
            let body = markerBody worker session leaseMinutes previousStatus pathRepo

            match postComment transport ref body with
            | Error e -> Error e
            | Ok myId ->

            // FROM HERE ON, OUR MARKER IS POSTED. Every exit below must either KEEP it (we won) or REMOVE
            // it (we lost, or we cannot tell) — never abort in between and leave it orphaned. An orphaned
            // marker is a lock held by a worker who does not know they hold it, and nothing will ever
            // release it.
            let withdraw (reason: string) =
                match deleteComment transport ref myId with
                | Ok() -> Ok(Undecided reason)
                | Error e ->
                    // WE CANNOT WIN AND WE CANNOT WITHDRAW. This is the one genuinely bad outcome, and it
                    // must be reported as itself: the marker is on the issue, we do not hold the item, and
                    // a human has to reap it.
                    Error(
                        Transport
                            $"%s{reason} — AND our own marker (comment %d{myId}) could not be removed: %s{explain e}. It is orphaned on %s{ref.Short} and must be reaped."
                    )

            match
                Reads.markerScan transport ref.Owner ref.Repo ref.Number
                |> Result.bind (Reads.requireCompleteMarkerScan ref.Short)
            with
            | Error e -> withdraw $"the re-read failed (%s{explain e})"
            | Ok after ->

            match Reads.winner leaseMinutes after with
            // OUR MARKER IS NOT IN THE RE-READ AT ALL. We cannot tell who holds this, and **"we cannot
            // tell" is a LOSS**. Reading it as a win would be a lock granted on the strength of an
            // observation we did not make.
            | None -> withdraw "our marker vanished from the re-read"

            | Some w when w.Id = myId ->
                // WE WON. The lowest live marker id is ours, and every racer computing the same total
                // order reaches the same conclusion. Now collect the stale debris this win claimed over —
                // including our OWN just-superseded stale marker, so a renew ends with exactly one marker,
                // not two. `session` is what we posted into the marker (`body`, above), so the `Held`
                // re-emits it on every heartbeat and twin-detection survives the lease (#1149).
                let held = Held(ref, worker, myId, session, previousStatus, pathRepo)
                let collected = collectStale myId after

                match evicted with
                | [] -> Ok(Won(held, collected))
                | _ -> Ok(Stolen(held, evicted, collected))

            | Some w ->
                // We lost the race — somebody's marker has a lower id. Back off CLEANLY.
                match deleteComment transport ref myId with
                | Ok() -> Ok(Lost w.Worker)
                | Error e ->
                    Error(
                        Transport
                            $"lost the claim race on %s{ref.Short} to %s{w.Worker.Value}, AND could not remove our own marker (comment %d{myId}): %s{explain e}. It is orphaned and must be reaped."
                    )

        // THE #1620 STEAL — EVICT EVERY LIVE MARKER HELD BY ANOTHER WORKER, so the CAS above runs on a
        // clear item. Only `claim --force` reaches this.
        //
        // EVERY live foreign marker goes, not merely the winning one. A live marker left behind has a LOWER
        // id than the one we are about to post, so it would win the re-read and we would withdraw — having
        // already deleted the real holder's lock and taken nothing, which is the one outcome strictly worse
        // than refusing. Clearing them all is what makes "the way is clear" true. A racer whose in-flight
        // marker we delete is not harmed: its own re-read finds the marker gone, which this CAS already
        // reads as a LOSS and retries (`Undecided`) — the #950/#266 fail-closed path, reached honestly.
        //
        // A FAILED DELETE IS NOT A STEAL. Their lock stands, so take nothing and say so: posting our marker
        // over a marker we could not remove is two live locks on one item, which is the whole thing the CAS
        // exists to prevent. `deleteComment` treats 404 as success, so a holder who released concurrently
        // is not an error — "already gone" is the goal state.
        let evictLive (markers: Reads.Marker list) : IoResult<WorkerId list> =
            let rec go acc rest =
                match rest with
                | [] -> Ok(List.rev acc)
                | (m: Reads.Marker) :: tail ->
                    match deleteComment transport ref m.Id with
                    | Ok() -> go (m.Worker :: acc) tail
                    | Error e ->
                        Error(
                            Transport
                                $"could not evict worker %s{m.Worker.Value}'s live marker (comment %d{m.Id}) from %s{ref.Short}: %s{explain e}. Their lock STANDS and nothing was taken."
                        )

            markers
            |> List.filter (fun m -> not (Reads.isStale leaseMinutes m) && m.Worker <> worker)
            |> go []

        match liveBefore with
        // A MARKER HELD BY NOBODY BLOCKS. A half-written lock fails CLOSED — if it vanished, the item would
        // read as free and a second worker would be handed files somebody may be standing in.
        | Some m when m.Worker = WorkerId UnparsedMarker -> Ok BlockedByUnparseableMarker

        // Somebody else holds a LIVE lock. Refuse before we post anything: a marker we post and then
        // withdraw is a comment somebody has to read, and the item is not ours regardless.
        //
        // UNLESS THIS IS A STEAL (#1620). `--force` is the org's only sanctioned recovery route for a holder
        // that died mid-item with hours of lease left: `reap` refuses an item with an open `item/<n>-*` PR
        // (#581, correct), `adopt` refuses a claim that is not stale (correct — a live claim is not an
        // orphan), and both of them point HERE. This arm is what makes that instruction true. It was not
        // before: `--force` was read in exactly one place, the caller's #516 one-item-per-worker pre-check,
        // so it refused identically with and without the flag while every message promised otherwise.
        | Some m when m.Worker <> worker ->
            match force with
            | RefuseLiveHolder -> Ok(Lost m.Worker)
            | StealLiveHolder ->
                // THE REFUSALS BEHIND THE HOLDER. The arms above catch an unparseable or same-id marker
                // when it is the CAS WINNER; a steal has to look PAST the winner too, because it is about
                // to delete the winner and promote whatever was queued behind it.
                //
                //   * UNPARSEABLE — a lock held by nobody is not a contested item, and evicting the holder
                //     would promote a marker we cannot attribute to anybody. `reap` owns that.
                //   * OUR OWN WORKER ID — the `Twin` refusal (#419) has to cover this position or it only
                //     covers half its own rule. Reachable: an orphaned marker from a failed withdraw (this
                //     function names that state), or a hand-written one. Left unguarded, the eviction would
                //     delete the real holder's live lock and then LOSE the re-read to our own twin's
                //     surviving marker — deleting a live lock and taking nothing, the one outcome the
                //     eviction comment below calls strictly worse than refusing.
                let liveOthers = before |> List.filter (fun x -> not (Reads.isStale leaseMinutes x))

                if liveOthers |> List.exists (fun x -> x.Worker = WorkerId UnparsedMarker) then
                    Ok BlockedByUnparseableMarker
                else
                    match liveOthers |> List.tryFind (fun x -> x.Worker = worker) with
                    // Sessions known and different: a twin, named as one.
                    | Some ours when (twinSession session ours.Session).IsSome ->
                        Ok(Twin (twinSession session ours.Session).Value)
                    // OUR ID, AND WE CANNOT CALL IT A TWIN — a sessionless marker, or our own session.
                    // Either way it is a marker of ours sitting BEHIND somebody else's live lock, which the
                    // CAS never produces and cannot resolve: evicting past it would delete the holder and
                    // then lose the re-read to it. `Undecided` is the honest answer — retryable, and it
                    // destroys nothing while an anomalous marker set is what it is.
                    | Some _ ->
                        Ok(
                            Undecided
                                $"a live marker for worker %s{worker.Value} sits BEHIND %s{m.Worker.Value}'s lock on %s{ref.Short} — a state the CAS does not produce. Refusing to force past it: the eviction would delete the live holder and then lose to this marker. Reap the item, or resolve the duplicate marker by hand"
                        )
                    | None ->
                        match evictLive before with
                        | Error e -> Error e
                        | Ok evicted ->
                            // ANNOUNCE THE EVICTION THE INSTANT IT HAPPENS, NOT WHEN IT PAYS OFF.
                            //
                            // A CALLBACK for `readPreviousStatus`'s reason — the caller owns the courtesy,
                            // this function owns the lock — and it is invoked HERE, between the delete and
                            // the post, deliberately.
                            //
                            // Reporting the theft only through the `Stolen` outcome would report it only on
                            // the HAPPY PATH. Every exit below can follow a successful eviction: the post
                            // can fail, the re-read can fail or come back without our marker, a newcomer can
                            // win the open race. In each of those the holder's live lock is already DELETED
                            // and no `Stolen` is ever returned — so the worker whose lock we destroyed would
                            // be told nothing, and their next `heartbeat` would read the empty item and
                            // report an EXPIRED LEASE. That is the silent transfer #1620's decision calls
                            // worse than a refusal, wearing the fix's clothes.
                            //
                            // The residue this does NOT fix, stated honestly: a steal that evicts and then
                            // fails to post leaves the item with NO live marker. That is recoverable — the
                            // item reads as free and the next claimant takes it — and the displaced worker
                            // has been told. A destroyed lock nobody knows about is not recoverable, which
                            // is why the notice, not the marker, is what this guarantees.
                            onEvict evicted
                            postAndResolve evicted

        // A live marker that is ALREADY OURS by worker id. Re-claiming is a no-op, and running the CAS again
        // would post a SECOND marker of ours with a higher id — which we would then lose to our own first one.
        //
        // BUT an id is not a lock if two workers share it (#419). If this marker carries a DIFFERENT session
        // from ours — and BOTH sessions are known — the holder is a TWIN, not us: another worker who derived
        // or was handed the same id. Adopting their live lock as a heartbeat is exactly the double-claim
        // ADR-0027 exists to prevent. So refuse, and hand back the other session to name.
        //
        // We conclude "twin" ONLY when both sessions are known. A sessionless marker (a human, a harness that
        // exports none, any pre-#419 marker) is genuinely indistinguishable from ours — failing closed on it
        // would lock workers out of items they really hold — so it heartbeats. And our OWN session re-claiming
        // its own marker is a heartbeat, never a twin, or a worker could never renew its own lease.
        | Some m ->
            // THE IMPERSONATION QUESTION IS ALREADY ANSWERED — step 0 asked it for the whole function, so by
            // here `worker` is this process's own id (or it derives none). That matters most on THIS arm:
            // it hands back a `Renewed` over a marker it did not create, on the strength of the id alone,
            // and under one harness session `twinSession` below cannot call that marker somebody else's,
            // because the impersonator's session IS theirs. It was the first door found; it was not the only.
            match twinSession session m.Session with
            | Some theirs -> Ok(Twin theirs)
            | None ->
                // RE-CLAIM / HEARTBEAT. The marker is already ours — same session, sessionless (a human or a
                // pre-#419 marker, indistinguishable from ours), or our own session re-claiming. RENEW THE
                // LEASE IN PLACE: a PATCH of the one marker we have, never a second POST that we would then
                // lose to our own first one and withdraw, reporting a loss on an item we hold. So a slow
                // worker re-claiming ends with ONE marker, and `take` retries stay idempotent.
                //
                // This bypasses the CAS entirely, which is why it is a SEPARATE outcome the caller must warn
                // about on a shared id (#419): a marker bearing our id is not proof it is ours, and adopting
                // it without the CAS is exactly where a same-id sibling silently takes another worker's lock.
                //
                // Collect the stale debris first (as the fresh-CAS win does), then renew — a stale OTHER
                // marker on this item is still what `heartbeat` would resurrect underneath us.
                let collected = collectStale m.Id before
                let renewed = markerBody worker session leaseMinutes m.PreviousStatus m.PathRepo

                // THE RENEWAL IS BEST-EFFORT, and it must be: we ALREADY hold this lock — our marker is the
                // live CAS winner — so a failed renewal PATCH does not un-hold us, and failing the command
                // here would turn an idempotent re-claim (a `take` retry) into an error on a transient 5xx,
                // reporting a loss on an item we demonstrably hold. Renew the lease if we can; hold either
                // way. This is bash's own re-claim (its `heartbeat_comment` result is not checked), and it
                // matches how the fresh-CAS `Won` path treats its follow-on board write (best-effort, #510).
                patchComment transport ref m.Id renewed |> ignore
                // `session` (our own) is what `renewed` just wrote into the marker (line above), so that is
                // what the `Held` carries forward — a re-claim UPGRADES a sessionless marker to bear our
                // session, exactly as it refreshes the lease.
                Ok(Renewed(Held(ref, worker, m.Id, session, m.PreviousStatus, m.PathRepo), collected))

        // Nobody holds it. Post and race, evicting nothing.
        | None -> postAndResolve []

    /// Compatibility entry point for callers that do not have a board path scope (notably chore
    /// locks and focused CAS tests). Its marker is intentionally legacy-shaped.
    let claim
        (transport: IGitHubTransport)
        (leaseMinutes: int)
        (force: ClaimForce)
        (onEvict: WorkerId list -> unit)
        (worker: WorkerId)
        (self: SelfIdentity)
        (session: SessionId option)
        (ref: Ref)
        (readPreviousStatus: unit -> BoardStatus option)
        : IoResult<ClaimOutcome> =
        claimScoped transport leaseMinutes force onEvict worker self session ref readPreviousStatus (fun () -> None)

    let mergeAtHead (transport: IGitHubTransport) (ref: Ref) (pr: int) (headSha: string) : IoResult<bool> =
        let payload =
            let body = Nodes.JsonObject()
            body["sha"] <- Nodes.JsonValue.Create headSha
            body.ToJsonString()
        let request =
            { Method = "PUT"
              Path = $"repos/%s{ref.Owner}/%s{ref.Repo}/pulls/%d{pr}/merge"
              Query = []
              Body = Json payload
              Budget = Rest
              IfNoneMatch = None
              Subject = ref.Short }
        match transport.Send request with
        | Error error -> Error error
        | Ok response ->
            try
                use document = JsonDocument.Parse response.Body
                match document.RootElement.TryGetProperty "merged" with
                | true, value when value.ValueKind = JsonValueKind.True -> Ok true
                | true, value when value.ValueKind = JsonValueKind.False -> Ok false
                | _ -> Error(Malformed(ref.Short, "merge response has no boolean merged field"))
            with :? JsonException as error ->
                Error(Malformed(ref.Short, $"merge response is not JSON: %s{error.Message}"))

    let verifyHeld
        (transport: IGitHubTransport)
        (leaseMinutes: int)
        (worker: WorkerId)
        (self: SelfIdentity)
        (session: SessionId option)
        (ref: Ref)
        : IoResult<HeldOutcome> =

        // FAILS CLOSED. An unreadable marker set yields an ERROR, never a `Holds` and never `DoesNotHold` —
        // because `DoesNotHold` says "we looked, and this worker does not hold it", which is a claim a failed
        // read is not entitled to make. Manufacturing a capability from a failed read would be the fail-open
        // this whole type exists to prevent, sitting inside its own constructor.
        match
            Reads.markerScan transport ref.Owner ref.Repo ref.Number
            |> Result.bind (Reads.requireCompleteMarkerScan ref.Short)
        with
        | Error e -> Error e
        | Ok markers ->
            match Reads.winner leaseMinutes markers with
            | Some m when m.Worker = worker ->
                // THE ID MATCHED. That is NOT the same question as "is this ours", and #419 is the whole
                // reason: an id two workers share is an id this protocol cannot separate. Ask the session
                // predicate — `claim`'s own — before opening the door to the capability.
                //
                // AND ASK WHO WE ARE FIRST (#1646). The id matching proves nothing on its own — it was
                // COPIED off this very marker set — and under one harness session the session leg matches for
                // the same reason, because every sibling of the fan-out holds it. So the question that
                // survives a shared session is asked BEFORE the twin one: does this process's OWN identity
                // agree with the id it was told to act as? A caller that derived `kite-461` and named
                // `vole-418` is definitively not `vole-418`, whatever the sessions say — and calling that a
                // TWIN would send them to `whoami --mint` over an identity collision they do not have.
                //
                // This arm is reached only when the named worker holds the LIVE lock, which is what makes it
                // safe to accuse: a mis-typed `--worker` names an id that holds nothing and falls through to
                // `DoesNotHold` below, where the caller can note the disagreement without the accusation.
                match impersonated self worker with
                | Some derived -> Ok(ImpersonatesHolder(derived, worker))
                | None ->

                match twinSession session m.Session with
                | Some theirs -> Ok(TwinHolds theirs)
                // `m.Session` is the session ALREADY in the live marker — carry it, unchanged, so a later
                // `heartbeat` re-emits it rather than stripping it (#1149). verifyHeld does not write, so the
                // marker's own value is the truth here.
                | None -> Ok(Holds(Held(ref, worker, m.Id, m.Session, m.PreviousStatus, m.PathRepo)))
            | _ -> Ok DoesNotHold

    // ---- the touch-set -----------------------------------------------------------------------------

    let validate (tokens: string list) : Result<Validated, string> =
        if List.isEmpty tokens then
            Error
                "a touch-set with no tokens reserves nothing. Declare `Paths: none` if that is the decision, or name the files."
        else

        // THE SENTINEL IS A DECISION, AND `widen` IS HOW YOU DECLARE IT (#863).
        //
        // `Paths: none` says "this item touches nothing, deliberately" — an epic, a decision item (#496).
        // It is not a path, so `TouchSet.classify` calls it `Unmatchable`, and the check below would
        // therefore REFUSE it as a token that "can never match a file". That refusal is technically true
        // and entirely beside the point: never matching a file is what the sentinel is FOR. It would also
        // make the tool contradict itself — the empty-token refusal directly above tells the worker to
        // "Declare `Paths: none` if that is the decision", and `widen --paths none` is how they would do
        // it. So the sentinel is decided FIRST, over the whole token set, exactly as `TouchSet.parse`
        // decides it — the two must agree, or `widen` writes a body its own parser reads differently.
        let sentinels, realPaths = tokens |> List.partition TouchSet.isSentinel

        if not (List.isEmpty sentinels) then
            if not (List.isEmpty realPaths) then
                // A CONTRADICTION: "I touch nothing/anything" and "I touch src/A" cannot both hold. Refuse
                // it here rather than write it, and say which of the two the worker has to pick — the
                // unmatchable message below would report the sentinel as a typo'd path and send them
                // looking for the wrong mistake.
                let named = String.Join(", ", realPaths)

                Error
                    $"a `Paths:` sentinel ('none' or 'any') declares that this item reserves no files — it cannot be declared alongside real paths (%s{named}). Declare the paths, or declare the sentinel, not both."
            else
                // ALL sentinel — but WHICH one? There are two (#1103 leg 8), they mean OPPOSITE things
                // (`none` unschedulable, `any` a schedulable chore), and canonicalising both to `none` —
                // as this did while there was only one — would silently turn a chore into an epic. So
                // decide over the distinct sentinel WORDS, exactly as `TouchSet.parse` does, and refuse a
                // mix (`none any` is as contradictory as `none src/A`). Canonicalise to a SINGLE token:
                // `rewrite` joins verbatim, so `["none"; "none"]` would emit `Paths: none none`, #863's
                // own input.
                match sentinels |> List.choose TouchSet.sentinelToken |> List.distinct with
                | [ "none" ] -> Ok(Validated [ "none" ])
                | [ "any" ] -> Ok(Validated [ "any" ])
                | _ ->
                    Error
                        "the touch-set sentinels 'none' (unschedulable — an epic/decision) and 'any' (a schedulable file-less chore) mean opposite things and cannot be declared together. Pick one."
        else

        // THE GRAMMAR LIVES IN THE CORE, AND THERE IS ONE OF IT. Re-implementing `classify` here would be a
        // second place for the touch-set rule to rot — which is #485's shape (one question, five
        // implementations, agreeing in none) reproduced inside its own remedy.
        let unmatchable =
            tokens
            |> List.choose (fun t ->
                match TouchSet.classify t with
                | Unmatchable u -> Some u
                | Matchable _ -> None)

        if not (List.isEmpty unmatchable) then
            // AN UNMATCHABLE TOKEN RESERVES NOTHING, so it conflicts with nothing, so it reads as DISJOINT
            // against every other worker (#273) — a lock that succeeds under exactly the conditions it
            // exists to prevent. It may not be written to an issue body.
            //
            // The refusal names what WOULD have been accepted. A refusal that does not only moves the
            // worker's confusion one step later.
            let bad = String.Join(", ", unmatchable)

            Error
                $"these tokens can never match a file, so they would reserve NOTHING and read as disjoint against every other worker: %s{bad}. %s{Schedulability.TouchSetGrammar}"
        else
            Ok(Validated tokens)

    /// A `Paths:` line, at the start of a line, with up to three leading spaces (CommonMark's limit).
    let private pathsLine = Regex(@"^ {0,3}[Pp]aths:", RegexOptions.Compiled)

    let rewrite (body: string) (paths: Validated) : Rewritten =
        let declaration = "Paths: " + String.Join(" ", paths.Tokens)

        // FENCE-AWARE, and the fence rule is ASKED, not decided (#972). A `Paths:` inside a fenced code
        // block is PROSE — an example, a quoted marker, a snippet of somebody else's issue — and rewriting
        // it would corrupt documentation into a reservation. This file used to carry its own `^\s*` toggle
        // while `TouchSet.parse` — the reader that decides whether the declaration we write is SEEN — used
        // `^ {0,3}`. Two rules over one body: `widen` wrote under one and `take` scheduled under the other.
        let mutable replaced = false
        let out = ResizeArray<string>()

        for line, kind in Markdown.classify body do
            if kind = Markdown.Text && pathsLine.IsMatch line then
                // THE FIRST DECLARATION IS REPLACED; THE REST ARE DROPPED. Two `Paths:` lines are an
                // ambiguity, and an ambiguity in a reservation is two workers each reading the one that
                // suits them.
                if not replaced then
                    out.Add declaration
                    replaced <- true
            else
                out.Add line

        // AN UNTERMINATED FENCE IS CLOSED, AND IT IS CLOSED *BEFORE* ANYTHING IS APPENDED BELOW IT.
        //
        // The order is the whole repair, and getting it wrong is not a style point — it silently voided the
        // write (#972). This close has always been here, with a comment naming the exact hazard: "a body
        // whose fence we opened and never shut would swallow every heading below it — including, on the
        // next pass, the declaration we just wrote." It ran AFTER the append, so the declaration landed
        // INSIDE the code block and the closer went underneath it. Measured, on a body ending in an
        // unterminated fence: `widen` returned success, and `TouchSet.parse` of the body it wrote returned
        // `Undeclared`. The scheduler could not see the touch-set, the item never started, and nothing
        // anywhere reported a failure. The comment described the bug it was standing next to.
        //
        // The closer is the OPENER's, too: a `~~~~` fence is closed by `~~~~`. Appending a literal "```"
        // to a tilde fence — which is what this did — repairs nothing and adds a line of noise.
        match Markdown.unterminatedFenceCloser body with
        | Some closer -> out.Add closer
        | None -> ()

        if not replaced then
            // No declaration to replace — append one. An issue that never declared a touch-set is exactly
            // the item `widen` exists to repair (#496: an OMISSION, not a decision).
            if out.Count > 0 && not (String.IsNullOrWhiteSpace(out.[out.Count - 1])) then
                out.Add ""

            out.Add declaration

        Rewritten(String.Join("\n", out))

    // ---- the writes that need the lock ---------------------------------------------------------------

    let private patchBody (transport: IGitHubTransport) (ref: Ref) (rewritten: Rewritten) : IoResult<unit> =
        let payload =
            let o = Nodes.JsonObject()
            o.["body"] <- Nodes.JsonValue.Create rewritten.Body
            o.ToJsonString()

        let request =
            { Method = "PATCH"
              Path = $"repos/%s{ref.Owner}/%s{ref.Repo}/issues/%d{ref.Number}"
              Query = []
              Body = Json payload
              Budget = Rest
              IfNoneMatch = None
              Subject = ref.Short }

        transport.Send request |> Result.map ignore

    let widen (transport: IGitHubTransport) (held: Held) (rewritten: Rewritten) : IoResult<unit> =
        // #706 AND #523, BOTH GONE, AND NEITHER BY A CHECK.
        //
        // The ownership test is the FIRST ARGUMENT: there is no path into this function that does not
        // already carry proof the caller holds the lock, so "widen never checks that the caller HOLDS the
        // claim" is not a bug that was fixed — it is a sentence that no longer parses.
        //
        // The validation is the SECOND: a `Rewritten` can only come from `rewrite`, which can only take a
        // `Validated`, which can only come from `validate`. So the PATCH cannot precede its own re-check,
        // which is #523 — and on an exhausted budget the declaration is not already rewritten when the
        // refusal arrives, because the refusal happens before the write is even representable.
        patchBody transport held.Ref rewritten

    let heartbeat (transport: IGitHubTransport) (leaseMinutes: int) (held: Held) : IoResult<Held> =
        // THE MARKER IS ADDRESSED BY ITS COMMENT ID, never by the worker string. #550 is what happens
        // otherwise: `release` and `heartbeat` picked a marker by WORKER STRING alone, so a twin — the same
        // worker id in a different session — could delete a lock it did not hold. The id is the lock.
        //
        // A PATCH rewrites the WHOLE body, so every field must be re-emitted from the capability. A claim
        // that had been beating for two hours used to forget the column it overwrote, because the rewrite
        // did not carry `prev=` forward and nothing had kept it (#550). It forgot `session=` the same way,
        // and the same way was worse: a sessionless marker is indistinguishable from a human's, so after the
        // first heartbeat `twinSession` could no longer catch a same-id twin, and two workers ended on one
        // item — the double-hold the CAS exists to prevent (#1149). The capability now HOLDS the session, so
        // the rewrite can re-emit it.
        let body =
            markerBody held.Worker held.Session leaseMinutes held.PreviousStatus held.PathRepo

        match patchComment transport held.Ref held.MarkerId body with
        | Error e -> Error e
        | Ok() -> Ok held

    let release (transport: IGitHubTransport) (held: Held) : IoResult<BoardStatus option> =
        // DELETE THE MARKER FIRST. Once it is gone the lease is dropped, and NOTHING below may abort the
        // release: a board we cannot read or write must leave the column alone and REPORT it, rather than
        // failing and leaving a lock behind that nobody now owns.
        //
        // A failed delete, on the other hand, IS fatal — the marker is still there, so the item is still
        // held, and reporting a release that did not happen would strand the item forever.
        match deleteComment transport held.Ref held.MarkerId with
        | Error e -> Error e
        | Ok() ->
            // WHAT THE COLUMN BECOMES IS THE CALLER'S DECISION, and this returns the fact it needs rather
            // than making it. `Some s` is "this claim overwrote column s, and it can be put back";
            // `None` is "this claim recorded no column" — and a column nobody recorded cannot be restored,
            // so the caller says so instead of inventing one (#481).
            Ok held.PreviousStatus

    // ---- reap: break a lock its own holder abandoned (#581) ------------------------------------------

    let reapable (ref: Ref) (marker: Reads.Marker) (liveness: Liveness) : Result<Reapable, ReapRefusal> =
        // The ONLY green case: the lease lapsed and we LOOKED for the item's PR and found none. `reapable`
        // is the whole of #581 — a reaper cannot reach `reap` any other way, so the proof-of-life gate is
        // structural, not a checklist item.
        match liveness with
        | LeaseExpiredNoPr -> Ok(Reapable(ref, marker.Worker, marker.Id, marker.PreviousStatus))
        | LeaseExpiredPrOpen pr -> Error(WorkAlive pr)
        // A pushed `item/<n>-*` branch with no PR is proof of life BEFORE §5 opens the PR (#1055). Refuse —
        // and refuse with its OWN reason, not `Undetermined`: `Undetermined` means "we could not tell", and
        // collapsing "the work is alive" into "could not tell" is the exact #581 mistake this whole gate
        // exists to prevent. We DID tell: a branch is pushed.
        | LeaseExpiredBranchPushed -> Error WorkAliveBranch
        // "We could not ask" is NOT "there is no PR" — the distinction that stops a transient failure from
        // reaping live work. A lease that is not even expired should never have reached here; treat it, too,
        // as a refusal rather than manufacturing a capability, because a `reap` from that state is a bug.
        | LivenessUnknown -> Error(Undetermined "the item's proof-of-life (its open PRs) could not be read")
        | LeaseHeld -> Error(Undetermined "the lease has not expired")

    type ReapResult =
        | Reaped
        | RenewedSinceScan of ageSeconds: int
        | AlreadyGone

    let reap (transport: IGitHubTransport) (leaseMinutes: int) (reapable: Reapable) : IoResult<ReapResult> =
        // RE-VERIFY AGAINST A FRESH READ, IMMEDIATELY BEFORE THE DELETE. `Reapable` was proven against the
        // SCAN, and the scan is a snapshot — between it and now the holder may have heartbeated. Deleting a
        // marker because it USED TO BE stale evicts a worker that is alive and believes its lease was
        // renewed, which is the double-hold `reap` exists to CLEAN UP, caused BY `reap`. So the marker's
        // freshness is the last thing checked before it is broken.
        match
            Reads.markerScan transport reapable.Ref.Owner reapable.Ref.Repo reapable.Ref.Number
            |> Result.bind (Reads.requireCompleteMarkerScan reapable.Ref.Short)
        with
        | Error e -> Error e
        | Ok markers ->
            match markers |> List.tryFind (fun m -> m.Id = reapable.MarkerId) with
            // A peer collected it between the scan and now — "already gone" is a collector's goal state.
            | None -> Ok AlreadyGone
            // Renewed since the scan: the lease is live again, so the lock stands. Leave it.
            | Some m when not (Reads.isStale leaseMinutes m) -> Ok(RenewedSinceScan m.AgeSeconds)
            // Still stale on the fresh read — break the lock. The lease self-heals the instant the marker is
            // gone. Nothing here restores the board: that is the caller's decision (`PreviousStatus`), taken
            // after the lock is already dropped, so a board write that fails cannot strand a lock.
            | Some _ ->
                match deleteComment transport reapable.Ref reapable.MarkerId with
                | Error e -> Error e
                | Ok() -> Ok Reaped

    // ---- the writes that do NOT need the lock ---------------------------------------------------------

    let say
        (transport: IGitHubTransport)
        (from: WorkerId)
        (toWorker: WorkerId)
        (ref: Ref)
        (text: string)
        : IoResult<unit> =

        // NO `Held` REQUIRED, AND THAT IS DELIBERATE. The worker who most needs to speak is often the one
        // who just LOST the race, or who is warning the holder that their touch-sets overlap. Gating a
        // message on the lock would silence exactly the worker with something urgent to say.
        let body =
            $"<!-- fsgg:msg from=%s{from.Value} to=%s{toWorker.Value} -->\n**%s{from.Value} → %s{toWorker.Value}**\n\n%s{text}"

        postComment transport ref body |> Result.map ignore

    let followupDisposition (transport: IGitHubTransport) (ref: Ref) (worker: WorkerId) (text: string) : IoResult<unit> =
        let body =
            $"<!-- fsgg:followup-disposition worker=%s{worker.Value} -->\n"
            + $"**Follow-up disposition for %s{worker.Value}**\n\n%s{text}"

        postComment transport ref body |> Result.map ignore

    let child (transport: IGitHubTransport) (parent: Ref) (childId: int64) : IoResult<unit> =
        // A JSON NUMBER, NOT A STRING. `gh api -f sub_issue_id=1047` sent it as a quoted string and
        // collected a 422; `-F` sent it as a number. `JsonValue.Create` on an int64 emits a number, and the
        // test asserts the body's TYPE rather than merely its text — the defect is one layer down now, but
        // it is the same defect.
        //
        // And it is the child's REST INTEGER ID, never its number: two repos can each have an issue #7, and
        // posting a number where an id belongs attaches the wrong issue silently.
        let payload =
            let o = Nodes.JsonObject()
            o.["sub_issue_id"] <- Nodes.JsonValue.Create childId
            o.ToJsonString()

        let request =
            { Method = "POST"
              Path = $"repos/%s{parent.Owner}/%s{parent.Repo}/issues/%d{parent.Number}/sub_issues"
              Query = []
              Body = Json payload
              Budget = Rest
              IfNoneMatch = None
              Subject = parent.Short }

        transport.Send request |> Result.map ignore

    // ---- coordination rooms (ADR-0051) ---------------------------------------------------------------

    /// Append a `Rooms: <roomRef>` line to a body, fence-safely. PURE, so `room open`'s write can be
    /// validated in a test with no network.
    ///
    /// UNLIKE `rewrite`, this APPENDS rather than replaces: a room membership is additive (`Rooms.parse`
    /// unions every line), so a second room adds a second line and never clobbers the first. The fence is
    /// closed BEFORE the append for `rewrite`'s exact reason (#972): a line appended under an unterminated
    /// fence lands inside the code block and `Rooms.parse` — which reads only `Markdown.unfenced` lines —
    /// would never see it, so the write would silently vanish.
    let appendRoomLine (body: string) (roomRef: string) : string =
        let declaration = $"Rooms: %s{roomRef}"
        let out = ResizeArray<string>()

        for line, _ in Markdown.classify body do
            out.Add line

        match Markdown.unterminatedFenceCloser body with
        | Some closer -> out.Add closer
        | None -> ()

        if out.Count > 0 && not (String.IsNullOrWhiteSpace(out.[out.Count - 1])) then
            out.Add ""

        out.Add declaration
        String.Join("\n", out)

    /// Write a `Rooms: <roomRef>` back-reference onto an item's body (ADR-0051). Does NOT take a `Held`:
    /// `room open` writes onto the items of a contended cluster it need not itself hold, exactly as `say`
    /// and `child` write to items without the lock. The CURRENT body is read by the caller and passed in,
    /// so the append is pure and the PATCH is the only IO.
    let writeRoomRef (transport: IGitHubTransport) (ref: Ref) (currentBody: string) (roomRef: string) : IoResult<unit> =
        patchBody transport ref (Rewritten(appendRoomLine currentBody roomRef))

    /// Create the room ISSUE (ADR-0051). A net-new write — no other verb POSTs an issue — returning the new
    /// item's `Ref` so the caller can write each member's `Rooms:` back-reference to it. The room is created
    /// OFF the board (nothing calls `add`): it is coordination scaffolding, not deliverable work.
    let createRoom (transport: IGitHubTransport) (owner: string) (repo: string) (title: string) (body: string) : IoResult<Ref> =
        let payload =
            let o = Nodes.JsonObject()
            o.["title"] <- Nodes.JsonValue.Create title
            o.["body"] <- Nodes.JsonValue.Create body
            o.ToJsonString()

        let request =
            { Method = "POST"
              Path = $"repos/%s{owner}/%s{repo}/issues"
              Query = []
              Body = Json payload
              Budget = Rest
              IfNoneMatch = None
              Subject = $"%s{owner}/%s{repo} room" }

        match transport.Send request with
        | Error e -> Error e
        | Ok response ->
            try
                use doc = JsonDocument.Parse response.Body

                match doc.RootElement.TryGetProperty "number" with
                | true, v when v.ValueKind = JsonValueKind.Number ->
                    Ok
                        { Owner = owner
                          Repo = repo
                          Number = v.GetInt32() }
                | _ ->
                    Error(
                        Malformed(
                            $"%s{owner}/%s{repo}",
                            "the room issue was created but the response carried no number — we cannot reference a room we cannot name"
                        )
                    )
            with :? JsonException as e ->
                Error(Malformed($"%s{owner}/%s{repo}", $"the issue-create response is not JSON: %s{e.Message}"))

    /// Close the room ISSUE (ADR-0051 §4). Its lifecycle is DERIVED: a room dies when every item that
    /// currently references it is done, and the caller (`done --flip`'s roll-up) has already established
    /// that. This just PATCHes the issue closed — a room carries no lock and no lease, so there is nothing
    /// else to unwind.
    let closeRoom (transport: IGitHubTransport) (ref: Ref) : IoResult<unit> =
        let payload =
            let o = Nodes.JsonObject()
            o.["state"] <- Nodes.JsonValue.Create "closed"
            o.ToJsonString()

        let request =
            { Method = "PATCH"
              Path = $"repos/%s{ref.Owner}/%s{ref.Repo}/issues/%d{ref.Number}"
              Query = []
              Body = Json payload
              Budget = Rest
              IfNoneMatch = None
              Subject = ref.Short }

        transport.Send request |> Result.map ignore
