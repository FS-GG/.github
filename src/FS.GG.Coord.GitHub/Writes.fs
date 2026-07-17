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
        internal (ref: Ref, worker: WorkerId, markerId: int64, previousStatus: BoardStatus option) =
        member _.Ref = ref
        member _.Worker = worker
        member _.MarkerId = markerId
        member _.PreviousStatus = previousStatus

    type ClaimOutcome =
        | Won of held: Held * collected: WorkerId list
        | Renewed of held: Held * collected: WorkerId list
        | Lost of WorkerId
        | Twin of theirs: SessionId
        | Undecided of reason: string
        | BlockedByUnparseableMarker

    [<Sealed>]
    type Reapable
        internal (ref: Ref, worker: WorkerId, markerId: int64, previousStatus: BoardStatus option) =
        member _.Ref = ref
        member _.Worker = worker
        member _.MarkerId = markerId
        member _.PreviousStatus = previousStatus

    type ReapRefusal =
        | WorkAlive of pr: int
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

        $"<!-- fsgg:claim worker=%s{worker.Value} lease=%d{leaseMinutes}%s{sessionPart}%s{prevPart} -->"

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

    let claim
        (transport: IGitHubTransport)
        (leaseMinutes: int)
        (worker: WorkerId)
        (session: SessionId option)
        (ref: Ref)
        (readPreviousStatus: unit -> BoardStatus option)
        : IoResult<ClaimOutcome> =

        // 1. READ THE LIVE MARKERS. A failed read here is fatal and we have posted nothing, so there is no
        //    marker to clean up — this is the only cheap place to fail, and it is why the read comes first.
        match Reads.markers transport ref.Owner ref.Repo ref.Number with
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

        match liveBefore with
        // A MARKER HELD BY NOBODY BLOCKS. A half-written lock fails CLOSED — if it vanished, the item would
        // read as free and a second worker would be handed files somebody may be standing in.
        | Some m when m.Worker = WorkerId UnparsedMarker -> Ok BlockedByUnparseableMarker

        // Somebody else holds a LIVE lock. Refuse before we post anything: a marker we post and then
        // withdraw is a comment somebody has to read, and the item is not ours regardless.
        | Some m when m.Worker <> worker -> Ok(Lost m.Worker)

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
            match session, m.Session with
            | Some(SessionId ours), Some(SessionId theirs) when ours <> theirs -> Ok(Twin(SessionId theirs))
            | _ ->
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
                let renewed = markerBody worker session leaseMinutes m.PreviousStatus

                // THE RENEWAL IS BEST-EFFORT, and it must be: we ALREADY hold this lock — our marker is the
                // live CAS winner — so a failed renewal PATCH does not un-hold us, and failing the command
                // here would turn an idempotent re-claim (a `take` retry) into an error on a transient 5xx,
                // reporting a loss on an item we demonstrably hold. Renew the lease if we can; hold either
                // way. This is bash's own re-claim (its `heartbeat_comment` result is not checked), and it
                // matches how the fresh-CAS `Won` path treats its follow-on board write (best-effort, #510).
                patchComment transport ref m.Id renewed |> ignore
                Ok(Renewed(Held(ref, worker, m.Id, m.PreviousStatus), collected))

        | None ->

        // 2. POST OUR MARKER.
        //
        // THIS is the linearisation point, and the only place the pre-claim column is worth a point (#481):
        // we have decided to post, no live marker stood in the way, and one line further on the board will
        // say `In progress` and the answer will be gone. A lost race or a re-claim never reaches here, so
        // neither pays the read.
        let previousStatus = readPreviousStatus ()
        let body = markerBody worker session leaseMinutes previousStatus

        match postComment transport ref body with
        | Error e -> Error e
        | Ok myId ->

        // 3. RE-READ, AND TAKE THE LOWEST LIVE ID AS THE WINNER.
        //
        // FROM HERE ON, OUR MARKER IS POSTED. Every exit below must either KEEP it (we won) or REMOVE it
        // (we lost, or we cannot tell) — never abort in between and leave it orphaned. An orphaned marker
        // is a lock held by a worker who does not know they hold it, and nothing will ever release it.
        let withdraw (reason: string) =
            match deleteComment transport ref myId with
            | Ok() -> Ok(Undecided reason)
            | Error e ->
                // WE CANNOT WIN AND WE CANNOT WITHDRAW. This is the one genuinely bad outcome, and it must
                // be reported as itself: the marker is on the issue, we do not hold the item, and a human
                // has to reap it.
                Error(
                    Transport
                        $"%s{reason} — AND our own marker (comment %d{myId}) could not be removed: %s{explain e}. It is orphaned on %s{ref.Short} and must be reaped."
                )

        match Reads.markers transport ref.Owner ref.Repo ref.Number with
        | Error e -> withdraw $"the re-read failed (%s{explain e})"
        | Ok after ->

        match Reads.winner leaseMinutes after with
        // OUR MARKER IS NOT IN THE RE-READ AT ALL. We cannot tell who holds this, and **"we cannot tell" is
        // a LOSS**. Reading it as a win would be a lock granted on the strength of an observation we did
        // not make.
        | None -> withdraw "our marker vanished from the re-read"

        | Some w when w.Id = myId ->
            // WE WON. The lowest live marker id is ours, and every racer computing the same total order
            // reaches the same conclusion. Now collect the stale debris this win claimed over — including
            // our OWN just-superseded stale marker, so a renew ends with exactly one marker, not two.
            Ok(Won(Held(ref, worker, myId, previousStatus), collectStale myId after))

        | Some w ->
            // We lost the race — somebody's marker has a lower id. Back off CLEANLY.
            match deleteComment transport ref myId with
            | Ok() -> Ok(Lost w.Worker)
            | Error e ->
                Error(
                    Transport
                        $"lost the claim race on %s{ref.Short} to %s{w.Worker.Value}, AND could not remove our own marker (comment %d{myId}): %s{explain e}. It is orphaned and must be reaped."
                )

    let verifyHeld
        (transport: IGitHubTransport)
        (leaseMinutes: int)
        (worker: WorkerId)
        (ref: Ref)
        : IoResult<Held option> =

        // FAILS CLOSED. An unreadable marker set yields an ERROR, never a `Held` and never `None` — because
        // `None` says "we looked, and this worker does not hold it", which is a claim a failed read is not
        // entitled to make. Manufacturing a capability from a failed read would be the fail-open this whole
        // type exists to prevent, sitting inside its own constructor.
        match Reads.markers transport ref.Owner ref.Repo ref.Number with
        | Error e -> Error e
        | Ok markers ->
            match Reads.winner leaseMinutes markers with
            | Some m when m.Worker = worker -> Ok(Some(Held(ref, worker, m.Id, m.PreviousStatus)))
            | _ -> Ok None

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
            if List.isEmpty realPaths then
                // ALL sentinel. CANONICALISE to a single `none`: `rewrite` joins the tokens verbatim, so
                // passing `["none"; "none"]` through would emit `Paths: none none` — which is #863's own
                // input, written by the tool that exists to repair it.
                Ok(Validated [ "none" ])
            else
                // A CONTRADICTION: "I touch nothing" and "I touch src/A" cannot both hold. Refuse it here
                // rather than write it, and say which of the two the worker has to pick — the unmatchable
                // message below would report `none` as a typo'd path and send them looking for the wrong
                // mistake.
                let named = String.Join(", ", realPaths)

                Error
                    $"`none` is the sentinel for 'this item touches nothing, deliberately' — it cannot be declared alongside real paths (%s{named}). Declare the paths, or declare `none`, not both."
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
        // did not carry `prev=` forward and nothing had kept it.
        let body =
            markerBody held.Worker None leaseMinutes held.PreviousStatus

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
        match Reads.markers transport reapable.Ref.Owner reapable.Ref.Repo reapable.Ref.Number with
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
