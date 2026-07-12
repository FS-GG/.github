namespace FS.GG.Coord.Cli

module Snapshot =

    open System
    open System.IO
    open System.Text.Json
    open FS.GG.Coord
    open FS.GG.Coord.Types
    open FS.GG.Coord.Schedulability

    [<Literal>]
    let private SnapshotSchema = "fsgg.coord.snapshot/1"

    [<Literal>]
    let private DecisionSchema = "fsgg.coord.decision/1"

    type Error = { Path: string; Message: string }

    type Candidate =
        { Item: Item
          BashPaths: string list option }

    type Request =
        { AllowBacklog: bool
          Limit: int option
          InFlight: Batch.Reservation list
          Candidates: Candidate list }

    // ================================================================================================
    // READING. Every accessor below is REQUIRED-by-default and returns a Result.
    // ================================================================================================
    // The one rule: absence is an error unless absence is a modelled fact. `jq -r .foo` on a missing
    // key yields the string "null", and bash then compares it to something — which is how "I could not
    // read it" and "it is not set" became the same value in the first place. Not here.

    let private err path message = Error [ { Path = path; Message = message } ]

    let private prop (path: string) (name: string) (el: JsonElement) : Result<JsonElement, Error list> =
        match el.TryGetProperty name with
        | true, v -> Ok v
        | _ -> err $"%s{path}.%s{name}" "required field is missing"

    /// A field that may be legitimately absent OR explicitly null — both mean "not set", which is a
    /// FACT here (no claim, no limit), not a failure to read one.
    let private optProp (name: string) (el: JsonElement) : JsonElement option =
        match el.TryGetProperty name with
        | true, v when v.ValueKind <> JsonValueKind.Null -> Some v
        | _ -> None

    let private asString (path: string) (el: JsonElement) : Result<string, Error list> =
        if el.ValueKind = JsonValueKind.String then
            Ok(el.GetString())
        else
            err path $"expected a string, got %A{el.ValueKind}"

    let private asInt (path: string) (el: JsonElement) : Result<int, Error list> =
        match el.ValueKind with
        | JsonValueKind.Number ->
            match el.TryGetInt32() with
            | true, n -> Ok n
            | _ -> err path "expected a 32-bit integer"
        | k -> err path $"expected a number, got %A{k}"

    let private asBool (path: string) (el: JsonElement) : Result<bool, Error list> =
        match el.ValueKind with
        | JsonValueKind.True -> Ok true
        | JsonValueKind.False -> Ok false
        | k -> err path $"expected a boolean, got %A{k}"

    let private asArray (path: string) (el: JsonElement) : Result<JsonElement list, Error list> =
        if el.ValueKind = JsonValueKind.Array then
            Ok(el.EnumerateArray() |> List.ofSeq)
        else
            err path $"expected an array, got %A{el.ValueKind}"

    let private stringField path name el =
        prop path name el |> Result.bind (asString $"%s{path}.%s{name}")

    let private intField path name el =
        prop path name el |> Result.bind (asInt $"%s{path}.%s{name}")

    /// Collect EVERY error, not just the first. A shadow that has to be debugged one field per
    /// round-trip, across six repos, does not get debugged.
    let private collect (results: Result<'a, Error list> list) : Result<'a list, Error list> =
        let errors = results |> List.collect (function Error e -> e | Ok _ -> [])

        if List.isEmpty errors then
            Ok(results |> List.choose (function Ok v -> Some v | Error _ -> None))
        else
            Error errors

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
                $"unknown board Status '%s{other}'. The engine refuses a column it was never taught, rather than coercing it to NoStatus and quietly reporting the item unschedulable — a board-schema change must be LOUD (run: fsgg-coord bootstrap --refresh)"

    let private issueState (path: string) (s: string) : Result<IssueState, Error list> =
        match s.Trim().ToUpperInvariant() with
        | "OPEN" -> Ok Open
        | "CLOSED" -> Ok Closed
        | other -> err path $"unknown issue state '%s{other}' (expected OPEN or CLOSED)"

    let private blockerState (path: string) (s: string) : Result<BlockerState, Error list> =
        match s.Trim().ToLowerInvariant() with
        | "open" -> Ok BlockerOpen
        | "closed" -> Ok BlockerClosed
        | "merged" -> Ok BlockerMerged
        | "unknown" -> Ok BlockerUnknown
        | "unparseable" -> Ok BlockerUnparseable
        | other -> err path $"unknown blocker state '%s{other}'"

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

    let private blocker (path: string) (el: JsonElement) : Result<Blocker, Error list> =
        let r = refOf path el

        let state =
            stringField path "state" el
            |> Result.bind (blockerState $"%s{path}.state")

        match r, state with
        | Ok r, Ok s -> Ok { Ref = r; State = s }
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
        let touchSet =
            stringField path "body" el |> Result.map TouchSet.parse

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

        match r, status, state, touchSet, blockers, claimR, bashPaths with
        | Ok r, Ok st, Ok state, Ok ts, Ok bl, Ok cl, Ok bp ->
            Ok
                { Item =
                    { Ref = r
                      Status = st
                      State = state
                      TouchSet = ts
                      Blockers = bl
                      Claim = cl }
                  BashPaths = bp }
        | a, b, c, d, e, f, g ->
            [ a |> Result.map ignore
              b |> Result.map ignore
              c |> Result.map ignore
              d |> Result.map ignore
              e |> Result.map ignore
              f |> Result.map ignore
              g |> Result.map ignore ]
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

                match w, r, age with
                | Ok w, Ok r, Ok a -> Ok(Batch.LiveClaim(w, r, a))
                | a, b, c ->
                    [ a |> Result.map ignore; b |> Result.map ignore; c |> Result.map ignore ]
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
        let paths =
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

        match schema, allowBacklog, limit, inFlight, candidates with
        | Ok _, Ok ab, Ok lim, Ok inf, Ok cands ->
            Ok
                { AllowBacklog = ab
                  Limit = lim
                  InFlight = inf
                  Candidates = cands }
        | a, b, c, d, e ->
            [ a |> Result.map ignore
              b |> Result.map ignore
              c |> Result.map ignore
              d |> Result.map ignore
              e |> Result.map ignore ]
            |> collect
            |> Result.map (fun _ -> Unchecked.defaultof<Request>)

    // ================================================================================================
    // WRITING. The verdict TOKEN is the contract — the prose is not.
    // ================================================================================================
    // `touch-set-drift.yml` greps this client's stdout for verdict tokens, and the org has already paid
    // once for a consumer that pattern-matched on a human sentence. The `kind` strings below are the
    // comparable surface: stable, lower-kebab, one per `Schedulability` case. The `explain` string
    // beside them is for a human and carries no contract at all.

    let private verdictKind =
        function
        | Startable -> "startable"
        | WrongStatus _ -> "wrong-status"
        | IssueClosed -> "issue-closed"
        | NoTouchSet -> "no-touch-set"
        | DeliberatelyNoTouchSet -> "deliberately-no-touch-set"
        | UnusableTouchSet _ -> "unusable-touch-set"
        | BlockedBy _ -> "blocked-by"
        | HeldBy _ -> "held-by"
        | HeldByLiveWork _ -> "held-by-live-work"
        | OverlapsInFlight _ -> "overlaps-in-flight"
        | Undetermined _ -> "undetermined"

    let private statusName =
        function
        | NoStatus -> ""
        | Backlog -> "Backlog"
        | Ready -> "Ready"
        | InProgress -> "In progress"
        | Blocked -> "Blocked"
        | InReview -> "In review"
        | Done -> "Done"

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
        | Batch.LiveClaim(WorkerId worker, item, age) ->
            w.WriteString("kind", "live-claim")
            w.WriteString("worker", worker)
            w.WriteNumber("ageSeconds", age)
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
        | WrongStatus s -> w.WriteString("status", statusName s)
        | UnusableTouchSet tokens ->
            w.WriteStartArray("tokens")
            tokens |> List.iter w.WriteStringValue
            w.WriteEndArray()
        | BlockedBy blockers ->
            w.WriteStartArray("blockers")

            for b in blockers do
                w.WriteStartObject()
                writeRef w b.Ref
                w.WriteString("state", blockerStateName b.State)
                w.WriteEndObject()

            w.WriteEndArray()
        | HeldBy(WorkerId worker) -> w.WriteString("worker", worker)
        | HeldByLiveWork(WorkerId worker, pr) ->
            w.WriteString("worker", worker)
            w.WriteNumber("pr", pr)
        | OverlapsInFlight hits ->
            w.WriteStartArray("hits")

            for (candidateToken, reservedToken) in hits do
                w.WriteStartObject()
                w.WriteString("candidate", candidateToken)
                w.WriteString("reserved", reservedToken)
                w.WriteEndObject()

            w.WriteEndArray()
        | Undetermined reason -> w.WriteString("reason", reason)
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
        | DeclaredNone -> []

    let private touchSetKind (ts: TouchSet) =
        match ts with
        | Undeclared -> "undeclared"
        | DeclaredNone -> "none"
        | Declared _ -> "declared"

    let render (candidates: Candidate list) (decision: Verdict<Batch.BatchResult>) : string =
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
                w.WriteString("verdict", verdictKind d.Result)

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

                w.WriteString("explain", Schedulability.explain d.Item d.Result)
                w.WriteEndObject()

            w.WriteEndArray()

        w.WriteEndObject()
        w.Flush()

        Text.Encoding.UTF8.GetString(stream.ToArray())
