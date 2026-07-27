namespace FS.GG.Coord.Cli

module Snapshot =

    open System
    open System.IO
    open System.Text.Json
    open FS.GG.Coord
    open FS.GG.Coord.Types
    open FS.GG.Coord.Schedulability
    open FS.GG.Coord.Cli.Json

    [<Literal>]
    let private SnapshotSchema = "fsgg.coord.snapshot/1"

    [<Literal>]
    let private DecisionSchema = "fsgg.coord.decision/1"

    /// The generic JSON accessors live in `Json` — this codec and the fleet-ledger codec obey the same
    /// no-fail-open rule, and a second copy of it would be a second place for it to rot.
    type Error = Json.Error

    type Candidate =
        { Item: Item
          BashPaths: string list option
          /// The registry-predicate ASSERTION this item's body declares (ADR-0050 call-site B), or `None`.
          /// The PURE id/field/value triple; the offer path resolves it to `Item.Predicate` (.github#1213).
          DeclaredPredicate: RegistryPredicate.Assertion option }

    /// The claim lease, in minutes. It is CONFIGURABLE in the client (`FSGG_CLAIM_LEASE_MIN`, default
    /// 120), so the engine may not assume it — a repo that shortened its lease and an engine that hard-
    /// coded 120 would together tell every worker to wait out a window that already closed.
    let DefaultLeaseMinutes = 120

    type Request =
        { AllowBacklog: bool
          Limit: int option
          LeaseMinutes: int
          InFlight: Batch.Reservation list
          Candidates: Candidate list }

    // ---- the domain vocabulary ---------------------------------------------------------------------
    // Every mapping below is TOTAL and CLOSED. An unrecognised value is refused, never coerced: if the
    // board grows a Status column nobody taught the engine about, that must surface as a loud snapshot
    // error, not as a silent `NoStatus` that happens to read as "not startable" and hides the change.

    let private boardStatus (path: string) (s: string) : Result<BoardStatus, Error list> =
        match s.Trim().ToLowerInvariant() with
        | "" -> Ok NoStatus
        | "backlog" -> Ok Backlog
        | "ready" -> Ok Ready
        | "in progress" -> Ok InProgress
        | "blocked" -> Ok Blocked
        | "in review" -> Ok InReview
        | "done" -> Ok Done
        | other ->
            err
                path
                $"unknown board Status '%s{other}'. The engine refuses a column it was never taught, rather than coercing it to NoStatus and quietly reporting the item unschedulable — a board-schema change must be LOUD (run: scripts/fsgg-coord bootstrap --refresh)"

    let private issueState (path: string) (s: string) : Result<IssueState, Error list> =
        match s.Trim().ToUpperInvariant() with
        | "OPEN" -> Ok Open
        | "CLOSED" -> Ok Closed
        | other -> err path $"unknown issue state '%s{other}' (expected OPEN or CLOSED)"

    // ASK the vocabulary rather than restating it (#1012). This hand-listed the five strings a second
    // time, in the opposite direction from `Scan`'s renderer, in a different project — and nothing
    // asserted the two were inverse, so a typo in either left the engine unable to read what it wrote.
    // The error stays HERE, because how to fail is this parser's business: `Core` answers "is this a
    // blocker state?" and `Snapshot` says which JSON path asked.
    let private blockerState (path: string) (s: string) : Result<BlockerState, Error list> =
        match Types.blockerStateOfWireName s with
        | Some b -> Ok b
        | None -> err path $"unknown blocker state '%s{s.Trim().ToLowerInvariant()}'"

    let private refOf (path: string) (el: JsonElement) : Result<Ref, Error list> =
        match stringField path "owner" el, stringField path "repo" el, intField path "number" el with
        | Ok owner, Ok repo, Ok number ->
            Ok
                { Owner = owner
                  Repo = repo
                  Number = number }
        | a, b, c ->
            [ a |> Result.map ignore; b |> Result.map ignore; c |> Result.map ignore ]
            |> collect
            |> Result.map (fun _ -> Unchecked.defaultof<Ref>)

    /// A BLOCKER'S REF IS OPTIONAL, BECAUSE PROSE BLOCKS (#421, and the fail-open the corpus caught).
    ///
    /// `BlockerUnparseable` means the `Blocked by` text is not a ref at all — "RESOLVED: shipped last week"
    /// — so demanding owner/repo/number here would refuse the very case the state exists to name. The
    /// client used to sidestep that by dropping such blockers entirely (its `jq capture` matched nothing,
    /// produced no object, and the whole array collapsed to `[]`), so an item the client called BLOCKED
    /// arrived here UNBLOCKED — and the engine's answer is the one a worker acts on, so that is a worker
    /// being handed blocked work.
    ///
    /// So: take the ref if it is there, keep the raw text always, and let the STATE carry the meaning.
    let private blocker (path: string) (el: JsonElement) : Result<Blocker, Error list> =
        let state =
            stringField path "state" el
            |> Result.bind (blockerState $"%s{path}.state")

        let raw =
            match optProp "raw" el with
            | Some v -> asString $"%s{path}.raw" v |> Result.map Some
            | None -> Ok None

        // A ref is present only if it is COMPLETE. A half-ref is not a ref, and coercing one would put a
        // fabricated `owner/repo#0` in front of a human.
        let parsedRef =
            match optProp "owner" el, optProp "repo" el, optProp "number" el with
            | Some _, Some _, Some _ ->
                match refOf path el with
                | Ok r when r.Number > 0 && r.Owner <> "" && r.Repo <> "" -> Some r
                | _ -> None
            | _ -> None

        match state, raw with
        | Ok s, Ok rawOpt ->
            let display =
                match rawOpt, parsedRef with
                | Some t, _ when t <> "" -> t
                | _, Some r -> r.Short
                | _ -> "an unnamed blocker"

            // AN UNNAMEABLE BLOCKER STILL BLOCKS. If neither a ref nor any text survived, we know only
            // that something was declared and we cannot read it — which is `BlockerUnparseable`'s whole
            // point, and it is emphatically not "nothing is blocking".
            Ok
                { Ref = parsedRef
                  Raw = display
                  State = s }
        | a, b ->
            [ a |> Result.map ignore; b |> Result.map ignore ]
            |> collect
            |> Result.map (fun _ -> Unchecked.defaultof<Blocker>)

    let private liveness (path: string) (el: JsonElement) : Result<Liveness, Error list> =
        stringField path "kind" el
        |> Result.bind (fun kind ->
            match kind with
            | "lease-held" -> Ok LeaseHeld
            | "lease-expired-no-pr" -> Ok LeaseExpiredNoPr
            | "lease-expired-branch-pushed" -> Ok LeaseExpiredBranchPushed
            | "lease-expired-pr-open" ->
                // The PR number is the PROOF. A `lease-expired-pr-open` with no number is a claim of
                // proof without the proof, and accepting it would resurrect exactly the reasoning
                // #581 was filed on.
                intField path "pr" el |> Result.map LeaseExpiredPrOpen
            | "unknown" -> Ok LivenessUnknown
            | other -> err $"%s{path}.kind" $"unknown liveness '%s{other}'")

    let private claim (path: string) (el: JsonElement) : Result<Claim * Liveness, Error list> =
        let worker = stringField path "worker" el |> Result.map WorkerId
        let age = intField path "ageSeconds" el

        let session =
            optProp "session" el
            |> Option.map (fun v -> asString $"%s{path}.session" v |> Result.map SessionId)

        let prev =
            optProp "prevStatus" el
            |> Option.map (fun v ->
                asString $"%s{path}.prevStatus" v
                |> Result.bind (boardStatus $"%s{path}.prevStatus"))

        let live = prop path "liveness" el |> Result.bind (liveness $"%s{path}.liveness")

        let sessionR =
            match session with
            | None -> Ok None
            | Some(Ok s) -> Ok(Some s)
            | Some(Error e) -> Error e

        let prevR =
            match prev with
            | None -> Ok None
            | Some(Ok s) -> Ok(Some s)
            | Some(Error e) -> Error e

        match worker, age, sessionR, prevR, live with
        | Ok w, Ok a, Ok s, Ok p, Ok l ->
            Ok(
                { Worker = w
                  Session = s
                  AgeSeconds = a
                  PreviousStatus = p },
                l
            )
        | w, a, s, p, l ->
            [ w |> Result.map ignore
              a |> Result.map ignore
              s |> Result.map ignore
              p |> Result.map ignore
              l |> Result.map ignore ]
            |> collect
            |> Result.map (fun _ -> Unchecked.defaultof<Claim * Liveness>)

    let private candidate (i: int) (el: JsonElement) : Result<Candidate, Error list> =
        let path = $"items[%d{i}]"
        let r = refOf path el

        let status =
            // An item with NO Status is a modelled FACT (#437), and it is the one the board produces
            // most often by accident — so `status: null` maps to `NoStatus`, while a MISSING `status`
            // key is a malformed snapshot. The difference is whether bash looked.
            match optProp "status" el with
            | None ->
                match el.TryGetProperty "status" with
                | true, _ -> Ok NoStatus // present and explicitly null
                | _ -> err $"%s{path}.status" "required field is missing (use null for 'no Status')"
            | Some v ->
                asString $"%s{path}.status" v
                |> Result.bind (boardStatus $"%s{path}.status")

        let state =
            stringField path "state" el
            |> Result.bind (issueState $"%s{path}.state")

        // THE ENGINE PARSES THE BODY ITSELF. This is the whole reason the raw body is on the wire: the
        // touch-set grammar is its own family of incidents (#273, #277, #435, #496), and a shadow that
        // re-used bash's parse would compare two schedulers over one parser and call the parser proven.
        //
        // `bodyUnreadable` is the client saying "I could not fetch this one". The body is then ABSENT,
        // not empty — and `TouchSet.parse ""` would answer `Undeclared`, a confident OMISSION about an
        // item nobody looked at. The client used to WITHHOLD such items entirely instead, which is worse:
        // an item bash had classified BLOCKED simply vanished from the engine's world, so it could not be
        // offered and could not even be passed over with a reason. Say what is true: UNREADABLE.
        let touchSet =
            match optProp "bodyUnreadable" el with
            | Some v ->
                asString $"%s{path}.bodyUnreadable" v
                |> Result.map (fun reason -> Unreadable reason)
            | None ->
                match optProp "body" el, state with
                | None, Ok Closed ->
                    // A CLOSED item is SWEPT: the client short-circuits it off the board scan and never
                    // reads its body, exactly as bash's own scheduler does. Its touch-set is never
                    // consulted — `Schedulability` answers `Closed -> IssueClosed` as its FIRST question,
                    // before the touch-set — so an absent body here is a fact, not a malformed item. (A
                    // shadow that pays to read the closed body still sends one; that parses too.)
                    Ok(TouchSet.parse "")
                | _ -> stringField path "body" el |> Result.map TouchSet.parse

        // #1103 leg 2 — the `Blocked on: human/...` sentinel, parsed off the same body the touch-set is.
        // Absent when the body was unreadable (we cannot know the sentinel we did not read) or swept off a
        // closed item (`Schedulability` answers `IssueClosed` first, so it is never consulted).
        let humanBlock =
            match optProp "bodyUnreadable" el with
            | Some _ -> Ok None
            | None ->
                match optProp "body" el, state with
                | None, Ok Closed -> Ok None
                | _ -> stringField path "body" el |> Result.map HumanBlock.parse

        // The registry-predicate ASSERTION the body declares, if any (ADR-0050 call-site B, .github#1213) —
        // parsed off the SAME body, on the same terms as `humanBlock`. This is the PURE half: only the
        // id/field/value triple the form renders, never the owner VERDICT, which needs the owning manifest
        // off disk and is resolved at the offer path's impure edge (`Client.enrichPredicates`). Absent on an
        // unreadable or swept-closed body — a predicate we did not read is one the flip gate holds on anyway.
        let declaredPredicate =
            match optProp "bodyUnreadable" el with
            | Some _ -> Ok None
            | None ->
                match optProp "body" el, state with
                | None, Ok Closed -> Ok None
                | _ -> stringField path "body" el |> Result.map RegistryPredicate.parseAssertion

        // The `Class:` the body declares (.github#1588, ADR-0066) — parsed off the SAME body as the
        // touch-set and the human-block sentinel, on identical terms. PURE, and BODY-ONLY: the `[decision]`
        // title convention is also evidence (AC3) but the snapshot document carries no `title`, so that
        // half is added at the impure edge by `Client.enrichClasses` — exactly the split `DeclaredPredicate`
        // above already runs. Body-only here is the fail-SAFE half: a missed derivation yields no chore,
        // never a wrong one.
        let itemClass =
            match optProp "bodyUnreadable" el with
            | Some _ -> Ok None
            | None ->
                match optProp "body" el, state with
                | None, Ok Closed -> Ok None
                | _ -> stringField path "body" el |> Result.map Class.fromBody

        let blockers =
            match optProp "blockers" el with
            | None -> Ok []
            | Some v ->
                asArray $"%s{path}.blockers" v
                |> Result.bind (fun els ->
                    els
                    |> List.mapi (fun j b -> blocker $"%s{path}.blockers[%d{j}]" b)
                    |> collect)

        let claimR =
            match optProp "claim" el with
            | None -> Ok None
            | Some v -> claim $"%s{path}.claim" v |> Result.map Some

        let bashPaths =
            match optProp "bashPaths" el with
            | None -> Ok None
            | Some v ->
                asArray $"%s{path}.bashPaths" v
                |> Result.bind (fun els ->
                    els
                    |> List.mapi (fun j t -> asString $"%s{path}.bashPaths[%d{j}]" t)
                    |> collect)
                |> Result.map Some

        // #651 — the open `item/<n>-*` PR the scan found on a MARKERLESS item. Absent for the common case
        // (no such PR, or a claim marker already carries its own liveness), so it is optional.
        let itemPr =
            match optProp "itemPr" el with
            | None -> Ok None
            | Some _ -> intField path "itemPr" el |> Result.map Some

        match r, status, state, touchSet, blockers, claimR, bashPaths, itemPr, humanBlock, declaredPredicate, itemClass with
        | Ok r, Ok st, Ok state, Ok ts, Ok bl, Ok cl, Ok bp, Ok ip, Ok hb, Ok dp, Ok ic ->
            Ok
                { Item =
                    { Ref = r
                      Status = st
                      State = state
                      TouchSet = ts
                      Blockers = bl
                      Claim = cl
                      ItemPr = ip
                      HumanBlock = hb
                      // The registry predicate VERDICT is RESOLVED impurely — it needs the owning producer's
                      // manifest off disk — so it is never set here: `parse` is pure (ADR-0050 call-site B,
                      // .github#1203). `None` is the ungated common case; the offer path resolves it from the
                      // `DeclaredPredicate` assertion below (`Client.enrichPredicates`, .github#1213), and a
                      // context that never resolves one (a plain `parse`, a receiver) flips as today.
                      Predicate = None
                      Class = ic
                      // The board's `Class` COLUMN is not on this document and is never inferred from the
                      // body: it is what the scan OBSERVED, resolved at the impure edge like `Predicate`
                      // (.github#1588). `None` here means "this parser did not look", and the projection
                      // chore reads it as a disagreement — so a context that never enriches writes the
                      // column it already holds, an idempotent write, rather than suppressing a real one.
                      BoardClass = None
                      // The board's `Phase` COLUMN and the issue's `createdAt` are not on this document
                      // either, and for `BoardClass`'s exact reason: both are SCAN facts, and this parser
                      // is pure (.github#1598). `Client.enrichBoardFacts` joins them on the offer path.
                      //
                      // `None` here is what makes a bare `decide --snapshot` rank on blocking-count alone
                      // — `Rank`'s documented no-priority-data case, which orders by issue number exactly
                      // as this engine did before #1598. A snapshot-only context therefore loses priority,
                      // never correctness, and never invents a phase it did not read.
                      Phase = None
                      AgeDays = None }
                  BashPaths = bp
                  DeclaredPredicate = dp }
        | a, b, c, d, e, f, g, h, i, j, k ->
            [ a |> Result.map ignore
              b |> Result.map ignore
              c |> Result.map ignore
              d |> Result.map ignore
              e |> Result.map ignore
              f |> Result.map ignore
              g |> Result.map ignore
              h |> Result.map ignore
              i |> Result.map ignore
              j |> Result.map ignore
              k |> Result.map ignore ]
            |> collect
            |> Result.map (fun _ -> Unchecked.defaultof<Candidate>)

    let private holder (path: string) (el: JsonElement) : Result<Batch.Holder, Error list> =
        stringField path "kind" el
        |> Result.bind (fun kind ->
            match kind with
            | "live-claim" ->
                let w = stringField path "worker" el |> Result.map WorkerId
                let r = refOf path el
                let age = intField path "ageSeconds" el
                // #712/#581: absent means "no proof of life" (a claim within its lease), NOT "unknown".
                let livePr =
                    match optProp "livePr" el with
                    | None -> Ok None
                    | Some _ -> intField path "livePr" el |> Result.map Some

                match w, r, age, livePr with
                | Ok w, Ok r, Ok a, Ok pr -> Ok(Batch.LiveClaim(w, r, a, pr))
                | a, b, c, d ->
                    [ a |> Result.map ignore
                      b |> Result.map ignore
                      c |> Result.map ignore
                      d |> Result.map ignore ]
                    |> collect
                    |> Result.map (fun _ -> Batch.UnknownHolder)
            | "batch-member" -> refOf path el |> Result.map Batch.BatchMember
            | "unowned" -> refOf path el |> Result.map Batch.Unowned
            | "unknown" -> Ok Batch.UnknownHolder
            | other -> err $"%s{path}.kind" $"unknown holder '%s{other}'")

    let private reservation (i: int) (el: JsonElement) : Result<Batch.Reservation, Error list> =
        let path = $"inFlight[%d{i}]"
        let owner = stringField path "owner" el
        let repo = stringField path "repo" el

        // Pre-parsed tokens, but still CLASSIFIED here — so an unmatchable reserved token is caught by
        // the same rule that catches it anywhere else (#273). It is the extraction, not the grammar,
        // that this side of the wire skips.
        // #1150: `pathsUnreadable` is the writer saying "I hold a lock over this item but could not read
        // its body", exactly as `bodyUnreadable` says it for a CANDIDATE. Its touch-set is UNKNOWN, not
        // empty — `Unreadable`, which `Batch.schedule` reds the batch on, rather than `Undeclared`, a
        // confident claim of no files that would let every candidate clear the lock (#496). Keyed on the
        // field's PRESENCE, so a within-lease claim with real paths round-trips through the `paths` arm.
        let paths =
            match optProp "pathsUnreadable" el with
            | Some v ->
                asString $"%s{path}.pathsUnreadable" v
                |> Result.map (fun reason -> Unreadable reason)
            | None ->
                prop path "paths" el
                |> Result.bind (asArray $"%s{path}.paths")
                |> Result.bind (fun els ->
                    els
                    |> List.mapi (fun j t -> asString $"%s{path}.paths[%d{j}]" t)
                    |> collect)
                |> Result.map (fun tokens ->
                    match tokens with
                    | [] -> Undeclared
                    | ts -> Declared(ts |> List.map TouchSet.classify))

        let h = prop path "holder" el |> Result.bind (holder $"%s{path}.holder")

        match owner, repo, paths, h with
        | Ok o, Ok rp, Ok p, Ok h ->
            Ok
                { Owner = o
                  Repo = rp
                  Paths = p
                  Holder = h }
        | a, b, c, d ->
            [ a |> Result.map ignore
              b |> Result.map ignore
              c |> Result.map ignore
              d |> Result.map ignore ]
            |> collect
            |> Result.map (fun _ -> Unchecked.defaultof<Batch.Reservation>)

    let parse (json: string) : Result<Request, Error list> =
        let doc =
            try
                Ok(JsonDocument.Parse(json))
            with :? JsonException as e ->
                err "$" $"not valid JSON: %s{e.Message}"

        match doc with
        | Error e -> Error e
        | Ok doc ->

        use doc = doc
        let root = doc.RootElement

        if root.ValueKind <> JsonValueKind.Object then
            err "$" $"expected an object, got %A{root.ValueKind}"
        else

        // The schema tag is a REFUSAL, not decoration. A shim that outlives its engine — or an engine
        // that outlives its shim — must fail loudly rather than decide from a shape it half-recognises.
        let schema =
            stringField "$" "schema" root
            |> Result.bind (fun s ->
                if s = SnapshotSchema then
                    Ok s
                else
                    err "$.schema" $"unsupported snapshot schema '%s{s}' (this engine speaks '%s{SnapshotSchema}')")

        let allowBacklog =
            prop "$" "allowBacklog" root |> Result.bind (asBool "$.allowBacklog")

        let limit =
            match optProp "limit" root with
            | None -> Ok None
            | Some v ->
                asInt "$.limit" v
                // `batch -n 0` is bash's "unlimited", and it must not read as "choose nothing".
                |> Result.map (fun n -> if n > 0 then Some n else None)

        // OPTIONAL, and defaulted — an older shim that does not send it still gets the documented 120,
        // which is what it was silently assuming anyway. A NON-POSITIVE lease is refused rather than
        // coerced: "every claim is instantly reapable" is never what anyone meant, and it would turn the
        // whole lock into a no-op on a typo.
        let leaseMinutes =
            match optProp "leaseMinutes" root with
            | None -> Ok DefaultLeaseMinutes
            | Some v ->
                asInt "$.leaseMinutes" v
                |> Result.bind (fun n ->
                    if n > 0 then
                        Ok n
                    else
                        err "$.leaseMinutes" $"a lease of %d{n} minute(s) would make every claim instantly reapable")

        let inFlight =
            match optProp "inFlight" root with
            | None -> Ok []
            | Some v ->
                asArray "$.inFlight" v
                |> Result.bind (fun els -> els |> List.mapi reservation |> collect)

        let candidates =
            prop "$" "items" root
            |> Result.bind (asArray "$.items")
            |> Result.bind (fun els -> els |> List.mapi candidate |> collect)

        match schema, allowBacklog, limit, leaseMinutes, inFlight, candidates with
        | Ok _, Ok ab, Ok lim, Ok lease, Ok inf, Ok cands ->
            Ok
                { AllowBacklog = ab
                  Limit = lim
                  LeaseMinutes = lease
                  InFlight = inf
                  Candidates = cands }
        | a, b, c, d, e, f ->
            [ a |> Result.map ignore
              b |> Result.map ignore
              c |> Result.map ignore
              d |> Result.map ignore
              e |> Result.map ignore
              f |> Result.map ignore ]
            |> collect
            |> Result.map (fun _ -> Unchecked.defaultof<Request>)

    // ================================================================================================
    // WRITING. The verdict TOKEN is the contract — the prose is not.
    // ================================================================================================
    // `touch-set-drift.yml` greps this client's stdout for verdict tokens, and the org has already paid
    // once for a consumer that pattern-matched on a human sentence. The `kind` strings are the
    // comparable surface: stable, lower-kebab, one per `Schedulability` case. The `explain` string
    // beside them is for a human and carries no contract at all.
    //
    // THEY ARE NO LONGER SPELLED HERE. `Schedulability.kind` is the only place they exist (#865) — this
    // module held a second hand-typed copy, `Protocol.verdicts` a third, and they disagreed: the wire
    // shipped `item-pr-open` and `held-by` while the docs had no `item-pr-open` at all and explained a
    // `held` nothing emits. A token is a contract, so it is emitted from the union, once.

    let private blockerStateName =
        function
        | BlockerOpen -> "open"
        | BlockerClosed -> "closed"
        | BlockerMerged -> "merged"
        | BlockerUnknown -> "unknown"
        | BlockerUnparseable -> "unparseable"

    let private writeRef (w: Utf8JsonWriter) (r: Ref) =
        w.WriteString("owner", r.Owner)
        w.WriteString("repo", r.Repo)
        w.WriteNumber("number", r.Number)
        w.WriteString("short", r.Short)

    let private writeHolder (w: Utf8JsonWriter) (h: Batch.Holder) =
        w.WriteStartObject()

        match h with
        | Batch.LiveClaim(WorkerId worker, item, age, livePr) ->
            w.WriteString("kind", "live-claim")
            w.WriteString("worker", worker)
            w.WriteNumber("ageSeconds", age)
            // #712: the #581 proof of life must survive the cache, or a later reader reconstructs a
            // liveness-less claim and the bug returns one layer down. Optional, and written ONLY when
            // present, so a claim within its lease round-trips byte-identically (the common case).
            match livePr with
            | Some pr -> w.WriteNumber("livePr", pr)
            | None -> ()
            writeRef w item
        | Batch.BatchMember item ->
            w.WriteString("kind", "batch-member")
            writeRef w item
        | Batch.Unowned item ->
            w.WriteString("kind", "unowned")
            writeRef w item
        | Batch.UnknownHolder -> w.WriteString("kind", "unknown")

        w.WriteEndObject()

    let private writeDetail (w: Utf8JsonWriter) (result: Schedulability) =
        match result with
        | WrongStatus s -> w.WriteString("status", statusWireName s)
        | UnusableTouchSet tokens ->
            w.WriteStartArray("tokens")
            tokens |> List.iter w.WriteStringValue
            w.WriteEndArray()
        | BlockedBy blockers ->
            w.WriteStartArray("blockers")

            for b in blockers do
                w.WriteStartObject()

                // The ref only when there IS one — a prose blocker has none, and emitting `owner: ""`,
                // `number: 0` would put a fabricated ref in the divergence log, which is the one document
                // that has to be readable when two engines disagree about exactly this.
                match b.Ref with
                | Some r -> writeRef w r
                | None -> ()

                w.WriteString("raw", b.Raw)
                w.WriteString("state", blockerStateName b.State)
                w.WriteEndObject()

            w.WriteEndArray()
        | HeldBy(WorkerId worker) -> w.WriteString("worker", worker)
        | HeldByLiveWork(WorkerId worker, pr) ->
            w.WriteString("worker", worker)
            w.WriteNumber("pr", pr)
        | ItemPrOpen pr -> w.WriteNumber("pr", pr)
        | OverlapsInFlight hits ->
            w.WriteStartArray("hits")

            for (candidateToken, reservedToken) in hits do
                w.WriteStartObject()
                w.WriteString("candidate", candidateToken)
                w.WriteString("reserved", reservedToken)
                w.WriteEndObject()

            w.WriteEndArray()
        | Undetermined reason -> w.WriteString("reason", reason)
        // The single `awaiting-human` kind carries WHICH sentinel as detail — the same edge `wrong-status`
        // uses for its column. `human/decision` and `human/action` are opposite instructions to a worker
        // (one never becomes startable without a human choice; the other becomes startable the moment an
        // action lands), and a reader of the divergence log must be able to tell them apart.
        | AwaitingHuman AwaitingHumanDecision -> w.WriteString("humanBlock", "decision")
        | AwaitingHuman AwaitingHumanAction -> w.WriteString("humanBlock", "action")
        | Startable
        | IssueClosed
        | NoTouchSet
        | DeliberatelyNoTouchSet -> ()

    let private tokensOf (ts: TouchSet) =
        match ts with
        | Declared tokens ->
            tokens
            |> List.map (function
                | Matchable t -> t
                | Unmatchable t -> t)
        | Undeclared
        | DeclaredNone
        // A chore reserves nothing, so it names no token.
        | DeclaredChore
        // No tokens are KNOWN. Not the same as none existing — see `touchSetKind`, which says so out loud
        // in the divergence log, because "the engines disagree" and "one of them never saw the body" are
        // the first two questions a reader of that log has to be able to tell apart.
        | Unreadable _ -> []

    let private touchSetKind (ts: TouchSet) =
        match ts with
        | Undeclared -> "undeclared"
        | DeclaredNone -> "none"
        | DeclaredChore -> "any"
        | Declared _ -> "declared"
        | Unreadable _ -> "unreadable"

    let render (leaseMinutes: int) (candidates: Candidate list) (decision: Verdict<Batch.BatchResult>) : string =
        let bashPathsOf (r: Ref) =
            candidates
            |> List.tryFind (fun c -> c.Item.Ref = r)
            |> Option.bind (fun c -> c.BashPaths)

        use stream = new MemoryStream()

        use w =
            new Utf8JsonWriter(stream, JsonWriterOptions(Indented = false, SkipValidation = false))

        w.WriteStartObject()
        w.WriteString("schema", DecisionSchema)

        match decision with
        | Red reasons ->
            // FAIL CLOSED, AND SAY SO. The batch was refused outright — a reservation whose surface we
            // cannot see makes every later comparison a lie. There is no partial answer to give.
            w.WriteString("verdict", "red")
            w.WriteStartArray("reasons")
            reasons |> List.iter w.WriteStringValue
            w.WriteEndArray()

        | NoVerdict reason ->
            w.WriteString("verdict", "no-verdict")
            w.WriteString("reason", reason)

        | Green result ->
            w.WriteString("verdict", "green")
            w.WriteBoolean("truncated", result.Truncated)

            w.WriteStartArray("chosen")

            for item in result.Chosen do
                w.WriteStartObject()
                writeRef w item.Ref
                w.WriteEndObject()

            w.WriteEndArray()

            w.WriteStartArray("decisions")

            for d in result.Decisions do
                w.WriteStartObject()
                writeRef w d.Item.Ref
                // `Schedulability.kind`, not a spelling of our own: this field and `Protocol.verdicts`
                // are two projections of ONE match, so the token a reader greps out of this log is the
                // token the doc explains. They were two hand-typed lists, and `item-pr-open` shipped
                // here while the doc had never heard of it (#865).
                w.WriteString("verdict", Schedulability.kind d.Result)

                w.WriteStartObject("detail")
                writeDetail w d.Result
                w.WriteEndObject()

                match d.CollidedWith with
                | Some h ->
                    w.WritePropertyName("collidedWith")
                    writeHolder w h
                | None -> ()

                // Both parses, side by side. When the two engines disagree about an item, the first
                // question is always "did they even read the same touch-set out of the same body?" —
                // and a divergence log that cannot answer it sends a reader back to the API.
                w.WriteString("touchSet", touchSetKind d.Item.TouchSet)

                w.WriteStartArray("paths")
                tokensOf d.Item.TouchSet |> List.iter w.WriteStringValue
                w.WriteEndArray()

                match bashPathsOf d.Item.Ref with
                | Some bp ->
                    w.WriteStartArray("bashPaths")
                    bp |> List.iter w.WriteStringValue
                    w.WriteEndArray()
                | None -> ()

                // The HOLDER-AWARE rendering (see Batch.explainDecision). This string is what the client
                // relays to the worker verbatim, so it is the one place the reason is worded — bash may
                // not restate it, and a holder-blind version of it is a regression on #428.
                w.WriteString("explain", Batch.explainDecision leaseMinutes d)
                w.WriteEndObject()

            w.WriteEndArray()

        w.WriteEndObject()
        w.Flush()

        Text.Encoding.UTF8.GetString(stream.ToArray())

    // ================================================================================================
    // THE PROTOCOL (ADR-0034 §4.5) — the rules, as a document nobody authors.
    // ================================================================================================

    // WHAT THIS WRITER KNOWS, AND WHAT IT DOES NOT (#1027). It knows how each fact SHAPE renders — which
    // members a rule has, that an exit code's `code` is a number. It does not know which keys the document
    // states, in what order, or that `filingRules` exists at all: `Protocol.factsDocument` states the
    // inventory, and this folds it.
    //
    // The two halves used to be split the other way round, and the split is what made `Snapshot.fs` a
    // chokepoint: `renderFacts` took one positional parameter per fact kind, so the INVENTORY — which keys
    // exist, and in what order — was hand-maintained here, in the Cli, for facts `Protocol` owns outright.
    // Adding one Core-owned key cost five edits across four files, four of them carrying no information
    // the fact had not already stated in `Protocol.fs`; and because one of them landed in THIS file, every
    // remaining #889 slice declared it and serialised behind whoever held it.
    //
    // THIS FILE ALREADY MADE THAT ARGUMENT — in the very block the inventory replaced. The schema note
    // that stood here said a `jq` filter listing rule ids in the shell would be *"a second copy of the
    // membership, hand-maintained, in the file whose whole purpose is to end hand-maintained copies"*. It
    // simply never turned the sentence on itself: `rules` was emitted rather than authored, and the list
    // of what got emitted was authored, four lines above the words. The argument still stands where it is
    // load-bearing — `render_filing_rules` in `scripts/generate-projections` — and now it holds here too.
    //
    // `FactsSchema` moved with the inventory, for the same reason: the schema version describes the
    // document's SHAPE, and the document's shape is `Protocol.factsDocument`. It is `Protocol.factsSchema`.

    let renderFacts (document: Protocol.FactSection list) : string =
        use stream = new MemoryStream()
        use w = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true, SkipValidation = false))

        w.WriteStartObject()
        w.WriteString("schema", Protocol.factsSchema)

        // ONE writer per SHAPE, and the keys come from the caller. A second hand-written copy of a shape's
        // members is exactly how `rules` and `filingRules` would come to spell `because` differently under
        // the generator that reads both — and how two exit-code tables' key names would drift apart.
        let writeRules (key: string) (rs: Protocol.Rule list) =
            w.WriteStartArray(key)

            for r in rs do
                w.WriteStartObject()
                w.WriteString("id", r.Id)
                w.WriteString("title", r.Title)
                w.WriteString("statement", r.Statement)
                w.WriteString("because", r.Because)
                w.WriteEndObject()

            w.WriteEndArray()

        let writeVerdicts (key: string) (verdicts: Protocol.VerdictDoc list) =
            w.WriteStartArray(key)

            for v in verdicts do
                w.WriteStartObject()
                w.WriteString("kind", v.Kind)
                w.WriteString("meaning", v.Meaning)
                w.WriteEndObject()

            w.WriteEndArray()

        let writeBlockerStates (key: string) (states: Protocol.BlockerStateDoc list) =
            w.WriteStartArray(key)

            for b in states do
                w.WriteStartObject()
                w.WriteString("wire", b.Wire)
                w.WriteBoolean("holds", b.Holds)
                w.WriteString("meaning", b.Meaning)
                w.WriteEndObject()

            w.WriteEndArray()

        // The ONE section that writes an OBJECT rather than an array, because the fact has a scalar in it:
        // the snapshot's shape is a schema string PLUS its keys. Flattening the schema into `keys` would
        // make it look like a key of the document it describes, which it is not.
        //
        // THE SCHEMA STRING IS NOT THIS FILE'S, and that is the whole point of #1058's ownership call:
        // `Scan.snapshot` writes `fsgg.coord.snapshot/1` and THIS module's parser refuses a document
        // without it, and neither reads it from `Protocol`. So three copies exist and `ProtocolTests` pins
        // them together — which is what makes the call payable rather than a doc that rots.
        let writeSnapshotShape (key: string) (schema: string) (keys: Protocol.SnapshotKeyDoc list) =
            w.WriteStartObject(key)
            w.WriteString("schema", schema)
            w.WriteStartArray("keys")

            for k in keys do
                w.WriteStartObject()
                w.WriteString("key", k.Key)
                w.WriteBoolean("reconciled", k.Reconciled)
                w.WriteString("meaning", k.Meaning)
                w.WriteEndObject()

            w.WriteEndArray()
            w.WriteEndObject()

        // `startable` is a STRING, not a bool, because the truth has three states: `Backlog` is startable
        // IFF the caller passed `--include-backlog`. Rendering that as `false` would publish the board's
        // most common park as plain unstartable and hide the flag that starts it; as `true`, it would
        // promise a queue a bare `take` never reads.
        //
        // THE SPELLING IS NOT THIS FILE'S. `s.Startable` is already `Schedulability.columnStartabilityWireName`'s
        // answer, exactly as `v.Kind` above is `Schedulability.kind`'s. The first draft of #1057 matched the
        // union HERE and spelled the three strings in this writer — a wire vocabulary owned by a projection
        // instead of by `Core`, which is the defect the whole change is about. It compiled clean when renamed.
        let writeBoardStatuses (key: string) (statuses: Protocol.BoardStatusDoc list) =
            w.WriteStartArray(key)

            for s in statuses do
                w.WriteStartObject()
                w.WriteString("wire", s.Wire)
                w.WriteString("startable", s.Startable)
                w.WriteString("meaning", s.Meaning)
                w.WriteEndObject()

            w.WriteEndArray()

        let writeExitCodes (key: string) (codes: Protocol.ExitCodeDoc list) =
            w.WriteStartArray(key)

            for c in codes do
                w.WriteStartObject()
                w.WriteNumber("code", c.Code)
                w.WriteString("name", c.Name)
                w.WriteString("meaning", c.Meaning)
                w.WriteString("action", c.Action)
                w.WriteEndObject()

            w.WriteEndArray()

        // `writes` is a BOOL, not folded into the prose: the absence of a write is the #331 observable
        // — it is what tells "preserved" from "restored" in the board's history — so a projection that
        // rendered it as a phrase inside `endState` would put a fact a reader selects on back into text a
        // reader parses. Same reason `blockerStates.holds` and `boardStatuses.startable` are their own
        // members and not baked into `meaning`.
        let writeReleaseColumns (key: string) (columns: Protocol.ReleaseColumnDoc list) =
            w.WriteStartArray(key)

            for c in columns do
                w.WriteStartObject()
                w.WriteString("condition", c.Condition)
                w.WriteString("endState", c.EndState)
                w.WriteBoolean("writes", c.Writes)
                w.WriteString("stdout", c.Stdout)
                w.WriteEndObject()

            w.WriteEndArray()

        // THE FOLD IS THE DOCUMENT. The list's order is the JSON's key order, so document order is stated
        // once — in `Protocol.factsDocument` — rather than here as a sequence of calls that has to be kept
        // in step with it. An EXHAUSTIVE match, so a new fact shape cannot reach the payload unrendered:
        // the compiler refuses a case with no writer, which is the coupling that makes the Cli's half of
        // this contract un-forgettable.
        for section in document do
            match section with
            | Protocol.Rules(key, rs) -> writeRules key rs
            | Protocol.Verdicts(key, vs) -> writeVerdicts key vs
            | Protocol.BlockerStates(key, bs) -> writeBlockerStates key bs
            | Protocol.BoardStatuses(key, ss) -> writeBoardStatuses key ss
            | Protocol.ExitCodes(key, cs) -> writeExitCodes key cs
            | Protocol.ReleaseColumns(key, cs) -> writeReleaseColumns key cs
            | Protocol.SnapshotShape(key, schema, keys) -> writeSnapshotShape key schema keys

        w.WriteEndObject()
        w.Flush()

        Text.Encoding.UTF8.GetString(stream.ToArray())

    // ================================================================================================
    // LANES (#428) — the partition, as the chore agent's input document.
    // ================================================================================================

    [<Literal>]
    let private LanesSchema = "fsgg.coord.lanes/1"

    let renderLanes (startable: Item -> bool) (partition: Lanes.Partition) : string =
        use stream = new MemoryStream()

        use w =
            new Utf8JsonWriter(stream, JsonWriterOptions(Indented = false, SkipValidation = false))

        let free = Lanes.free startable partition

        let isFree (lane: Lanes.Lane) =
            free |> List.exists (fun f -> f.Id = lane.Id)

        w.WriteStartObject()
        w.WriteString("schema", LanesSchema)

        // THE CEILING IS THE HEADLINE. Fan out wider than this and the extra workers are handed nothing
        // — and `take` reports an empty queue over a board that is full of work (#440's shape).
        w.WriteNumber("ceiling", List.length free)
        w.WriteNumber("lanes", List.length partition.Lanes)

        w.WritePropertyName "partition"
        w.WriteStartArray()

        for lane in partition.Lanes do
            w.WriteStartObject()
            w.WriteString("id", lane.Id.Short)
            w.WriteString("repo", $"%s{lane.Owner}/%s{lane.Repo}")
            w.WriteBoolean("free", isFree lane)

            w.WritePropertyName "heldBy"
            w.WriteStartArray()

            for wk in lane.HeldBy do
                w.WriteStringValue wk.Value

            w.WriteEndArray()

            w.WritePropertyName "tokens"
            w.WriteStartArray()

            for t in lane.Tokens do
                w.WriteStringValue t

            w.WriteEndArray()

            w.WritePropertyName "items"
            w.WriteStartArray()

            for item in lane.Items do
                w.WriteStartObject()
                w.WriteString("ref", item.Ref.Short)
                w.WriteBoolean("startable", startable item)
                w.WriteEndObject()

            w.WriteEndArray()

            // THE GLUE — the chore's target list, ranked by what narrowing each token would actually
            // BUY. Only tokens whose removal splits the lane are emitted: one that splits it into 1 is
            // load-bearing, the work really is coupled, and an agent that "narrowed" it would reserve
            // less than the work touches and hand those files to somebody else (#273).
            w.WritePropertyName "glue"
            w.WriteStartArray()

            for g in Lanes.glue lane |> List.filter (fun g -> g.SplitsInto > 1) do
                w.WriteStartObject()
                w.WriteString("token", g.Token)
                w.WriteNumber("splitsInto", g.SplitsInto)

                w.WritePropertyName "declaredBy"
                w.WriteStartArray()

                for r in g.DeclaredBy do
                    w.WriteStringValue r.Short

                w.WriteEndArray()
                w.WriteEndObject()

            w.WriteEndArray()
            w.WriteEndObject()

        w.WriteEndArray()

        // THE THREE UNLANABLE STATES, NAMED SEPARATELY. An agent that cannot tell a forgotten `Paths:`
        // from a deliberate `Paths: none` will "helpfully" declare a touch-set for an epic — making the
        // board worse while reporting that it improved it (#496).
        w.WritePropertyName "unlanable"
        w.WriteStartArray()

        for u in partition.Unlanable do
            w.WriteStartObject()
            w.WriteString("ref", u.Item.Ref.Short)

            match u with
            | Lanes.Unread(_, reason) ->
                // NOT a chore. A chore is work an agent can do; nobody can declare a touch-set for a body
                // that was never read, and telling them to would put a `Paths:` line on an item that may
                // already have one.
                w.WriteString("reason", "body-unreadable")
                w.WriteBoolean("chore", false)
                w.WriteString("detail", $"its body could not be READ, so its touch-set is unknown — not absent ({reason}). Retry; do not declare one for it.")

            | Lanes.NoTouchSet _ ->
                w.WriteString("reason", "no-touch-set")
                w.WriteBoolean("chore", true)
                w.WriteString("detail", "no `Paths:` line at all — somebody forgot. Real work, and nobody can pick it up.")

            | Lanes.DeliberatelyNone _ ->
                w.WriteString("reason", "declared-none")
                w.WriteBoolean("chore", false)
                w.WriteString("detail", "`Paths: none` — an epic or a decision. Unschedulable BY DESIGN. Do NOT propose a touch-set.")

            | Lanes.UnusableTokens(_, tokens) ->
                w.WriteString("reason", "unusable-tokens")
                w.WriteBoolean("chore", true)

                w.WriteString(
                    "detail",
                    "declares tokens that match NOTHING, so it reserves nothing and reads as disjoint from everybody. Worse than undeclared (#273)."
                )

                w.WritePropertyName "tokens"
                w.WriteStartArray()

                for t in tokens do
                    w.WriteStringValue t

                w.WriteEndArray()

            w.WriteEndObject()

        w.WriteEndArray()
        w.WriteEndObject()
        w.Flush()

        Text.Encoding.UTF8.GetString(stream.ToArray())
