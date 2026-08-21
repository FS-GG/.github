namespace FS.GG.Coord.Cli

/// THE CLIENT COMMAND SURFACE — the bash client's commands, re-expressed over the typed IO layer.
///
/// This is what the ADR-0034 §4.4 shim execs in place of `scripts/fsgg-coord`. Each command composes the
/// already-built, already-tested pieces — `Scan` (the board read), `Batch`/`Schedulability` (the pure
/// decision), `Writes` (the claim CAS and the capability-typed writes), `Board` (the field writes), `Done`
/// (the done-stamp) — into a CLI verb with the fail-closed exit contract the recipes and the corpus depend
/// on.
///
/// THE ONE RULE THIS FILE ADDS TO THE ONES BELOW IT: every command that touches a lock takes a WORKER (via
/// `Identity`), because the lock is keyed on the worker, not the account (ADR-0027). And every command
/// fails CLOSED — a read it could not make is never an empty answer, and an exhausted budget is EX_RATE
/// (75), the back-off signal, never a generic error.
module Client =

    open System
    open System.Diagnostics
    open System.IO
    open System.Text.Json
    open FS.GG.Coord
    open FS.GG.Coord.Types
    open FS.GG.Coord.GitHub
    open FS.GG.Coord.GitHub.Transport
    open FS.GG.Coord.Cli.Options
    open FS.GG.Coord.Cli.Render
    open FS.GG.Coord.Cli.BoardOps
    // .github#2725 — the shared base now lives in `FS.GG.Coord.Cli.Kernel`, and this `open` is what keeps
    // its extraction a MOVE rather than a rewrite: `eprint`, `fail`, the exit literals, `Context`,
    // `worker`, `parseRef` and the checkout-scope readers keep the unqualified spelling they had inside
    // this module, at every one of their ~600 call sites here. The re-spelling that DOES happen is
    // external — the test projects name `Kernel.` explicitly — which is the point: a consumer outside this
    // module can no longer reach the shared base THROUGH `Client`, so the four family extractions
    // (.github#2726–#2729) depend on the Kernel rather than on the module they are being cut out of.
    open FS.GG.Coord.Cli.Kernel

    /// lint's BAD-TOUCH-SET sentence for a declaration `TouchSet.usability` has judged, or `None` when
    /// there is nothing to say. `status` is the already-rendered wire name.
    ///
    /// MODULE-LEVEL, AND THAT IS THE POINT (#945). lint's rule used to live inside the command handler,
    /// deciding usability with its own `List.exists`/`List.choose`/`List.forall`. Nothing could reach it,
    /// so "lint agrees with the core" was a claim no test could make — which is precisely how
    /// `Schedulability` and `Lanes` agreed right up until they didn't (#864). The VERDICT is the core's
    /// and arrives as an argument; only the WORDS are lint's. Taking `Usability` rather than a touch-set
    /// is what makes that true by construction: there is no threshold left in here to get wrong, and the
    /// test can drive every case the core can produce.
    let badTouchSetDetail = LintApplication.badTouchSetDetail

    let blockedNoReasonVerdict = LintApplication.blockedNoReasonVerdict

    let humanParkResolvedVerdict = LintApplication.humanParkResolvedVerdict

    let blockerCycleVerdicts = LintApplication.blockerCycleVerdicts

    /// The `Blocked by` BODY-VS-FIELD divergence (.github#2079) — the refs a body's `Blocked by:` line(s)
    /// name that the FIELD does not, both sides canonicalized on `Blockers.canonicalizeBlockedBy`'s terms
    /// so `#8`, `FS-GG/FS.GG.SDD#8` and the field's own rendering of the same ref compare equal.
    ///
    /// `[]` means coherent: the body declares no `Blocked by:` line, or everything it names is already in
    /// the field. A non-empty result is the `FS.GG.Templates#348` shape — a park whose edge landed in the
    /// wrong medium, leaving a field that satisfies `BLOCKED-NO-REASON` (it is non-empty) while naming
    /// refs the reader cannot see. `lint`'s `BLOCKED-BY-INERT` and `reconcile`'s `BLOCKER-CLEARED`
    /// withholding are the same predicate, asked twice for two different reasons — never two copies.
    ///
    /// MODULE-LEVEL, ABOVE `reconcile` (.github#1225-ish): both `reconcile` and `lint` need it, and F#
    /// compiles top to bottom — a copy inside either command would be the second-copy shape #945/#972
    /// argue against everywhere else in this file.
    let blockedByBodyDivergence (owner: string) (repo: string) (fieldRaw: string) (body: string) : string list =
        let canonRefs (raw: string) : Set<string> =
            match Blockers.canonicalizeBlockedBy owner repo raw with
            | Ok(Some canonical) -> canonical.Split(',') |> Array.map (fun s -> s.Trim()) |> Set.ofArray
            | Ok None
            | Error _ -> Set.empty

        let fieldRefs = canonRefs fieldRaw

        let bodyRefs =
            HumanBlock.parseBlockedByLines body
            |> List.collect (fun raw -> canonRefs raw |> Set.toList)
            |> Set.ofList

        Set.difference bodyRefs fieldRefs |> Set.toList |> List.sort

    /// lint's CLASS verdict (`CLASS-INVALID` / `CLASS-UNSET` / nothing), on `badTouchSetDetail`'s terms:
    /// module-level so a test can drive every shape the grammar can produce (.github#1651).
    let classVerdict = LintApplication.classVerdict

    /// The out-of-vocabulary-`Class:` refusal `add` renders before it boards a row (.github#1651 AC1).
    let outOfVocabularyClass = LintApplication.outOfVocabularyClass

    /// Existing same-tree rows that the new declaration strictly contains.  This is advisory at the
    /// filing boundary: the issue is already real, and refusing to board it would make the warning hide
    /// work from every later board view.  It instead names the lane-of-one while the filer can still
    /// narrow or sequence the pair (#1843).
    let filingLaneOfOne (candidate: Ref) (paths: TouchSet) (items: Item list) : Ref list =
        items
        |> List.filter (fun item ->
            item.Ref <> candidate
            && String.Equals(item.Ref.Owner, candidate.Owner, StringComparison.OrdinalIgnoreCase)
            // repo-filter-monopoly: exempt — REF-to-REF identity comparison, not a `--repo` filter.
            && String.Equals(item.Ref.Repo, candidate.Repo, StringComparison.OrdinalIgnoreCase)
            && TouchSet.strictlyContains paths item.TouchSet)
        |> List.map _.Ref

    // ---- the read / schedule commands ------------------------------------------------------------------

    [<Literal>]
    let private StructuredRouteMarker = "<!-- fsgg:route-decision/v2 -->"

    [<Literal>]
    let private StructuredReviewMarker = "<!-- fsgg:review-decision/v2 -->"

    /// Static receipt validation proves that an SDD route says which work package it governs.  Whether
    /// that package's files currently exist and are `implementationReady` is a SEPARATE, ADVISORY fact
    /// (#2298): requiring it to be true before the receipt records or the item schedules made `sdd-required`
    /// permanently unreachable for any item that does not already carry a package, because the only actor
    /// positioned to author that package — a CLAIMED WORKER, inside a worktree, via `fsgg-sdd` — could never
    /// get claimed to do so. `sddEvidenceErrors` is reported (by `record` and `show`) but never refuses.
    /// Decode the current SDD analysis fact for the named work.  The route boundary owns this one
    /// interpretation so a `workId` substitution and an unready analysis cannot be accepted on one
    /// command path while another merely checks that JSON was present.
    let sddReadinessEvidenceErrors workId (raw: string) =
        try
            use document = JsonDocument.Parse raw
            let root = document.RootElement
            [ match root.TryGetProperty "workId" with
              | true, value when value.ValueKind = JsonValueKind.String && value.GetString() = workId -> ()
              | _ -> yield $"sdd readiness workId does not match '%s{workId}'"
              match root.TryGetProperty "status" with
              | true, value when value.ValueKind = JsonValueKind.String && value.GetString() = "implementationReady" -> ()
              | _ -> yield "sdd readiness status is not implementationReady" ]
        with error -> [ $"sdd readiness evidence is unreadable: %s{error.Message}" ]

    /// Exposed for the command-boundary test: this is the sole filesystem-backed SDD proof surfaced by
    /// route reads and route recording, so the test can pin both the current and missing-work inversions.
    /// ADVISORY ONLY (#2298) — see the doc comment above. Never fed back into a `DeliveryRoute.Verdict`.
    let sddEvidenceErrors (receipt: DeliveryRoute.Receipt) =
        match receipt.Route, receipt.SddWorkId, receipt.SpecHome with
        | Some DeliveryRoute.SddRequired, Some workId, Some specHome ->
            let rec findRoot (directory: DirectoryInfo) =
                let hasEvidence =
                    Directory.Exists(Path.Combine(directory.FullName, "work"))
                    && Directory.Exists(Path.Combine(directory.FullName, "readiness"))

                if hasEvidence then Some directory.FullName
                elif isNull directory.Parent then None
                else findRoot directory.Parent

            let root =
                match env "FSGG_COORD_SDD_ROOT" "" with
                | "" -> findRoot (DirectoryInfo(Directory.GetCurrentDirectory()))
                | value -> Some value
            let atRoot relative = root |> Option.map (fun value -> Path.Combine(value, relative)) |> Option.defaultValue relative
            let specPath = atRoot specHome
            let readiness = atRoot (Path.Combine("readiness", workId, "analysis.json"))
            [ if not (File.Exists specPath) then
                  yield $"sdd spec does not exist: %s{specHome}"
              if not (File.Exists readiness) then
                  yield $"sdd readiness evidence does not exist: %s{readiness}"
              elif File.Exists readiness then
                  yield! sddReadinessEvidenceErrors workId (File.ReadAllText readiness) ]
        | _ -> []

    /// Structured decisions are append-only ledgers, so every effective read must see revision 1 and
    /// every predecessor. A bounded tail is unsafe: a buried record could otherwise disappear. Use the complete, paginated identity read
    /// for reads and writes; the pagination guard fails closed rather than truncating authorization.
    let private completeDeliveryRouteComments (ctx: Context) (target: Ref) =
        Reads.commentBodies ctx.Transport target.Owner target.Repo target.Number

    let private readDeliveryRouteComments (ctx: Context) (target: Ref) =
        completeDeliveryRouteComments ctx target

    /// The validated delivery-route decision in the complete comment ledger — one search, in one place.
    ///
    /// It was written out three times (scheduling, the claim/take mutation boundary, and
    /// `delivery-route show`/`record`) and .github#2324 needed a fourth caller in `verifyPaths`. A rule
    /// copied a fourth time is #485's shape arriving by addition rather than by drift, so the copies are
    /// collapsed here first. Every caller still owns what it does with `None` — the three existing ones
    /// deliberately differ (`Unreadable`/`Stale` vs. a raw IO error), and that judgement is theirs, not
    /// this function's.
    ///
    /// Read every structured record in comment order. Missing, malformed, gapped, or tampered evidence
    /// is an explicit refusal; there is no alternate authority or prose fallback.
    let private structuredRouteLedger (subject: string) (comments: string list) =
        let marked =
            comments
            |> List.choose (fun comment ->
                if comment.StartsWith(StructuredRouteMarker + "\n", StringComparison.Ordinal) then
                    Some(comment.Substring(StructuredRouteMarker.Length).Trim())
                else None)

        if List.isEmpty marked then Ok None
        else
            let decoded = marked |> List.map DeliveryRouteApplication.decodeStructured
            let failures = decoded |> List.choose (function Error error -> Some error | Ok _ -> None)
            if not (List.isEmpty failures) then Error failures
            else
                let records = decoded |> List.choose Result.toOption
                StructuredDecision.validateRouteLedger subject records |> Result.map (fun latest -> Some(records, latest))

    let private routeEvidence (subject: string) (comments: string list) : DeliveryRoute.Verdict =
        match structuredRouteLedger subject comments with
        | Error errors -> DeliveryRoute.Stale errors
        | Ok(Some(_, latest)) ->
            DeliveryRoute.Current(StructuredDecision.toEffectiveRoute latest)
        | Ok None ->
            DeliveryRoute.Stale [ "structured route ledger is missing" ]

    /// The route decision is an impure receipt: both the source item and its append-only receipt ledger
    /// are read immediately before the pure scheduler sees the item.  An unreadable read stays typed as
    /// unreadable, rather than collapsing into a missing/lightweight decision.
    let private readDeliveryRouteVerdict (ctx: Context) (target: Ref) =
        match readDeliveryRouteComments ctx target with
        | Error error -> DeliveryRoute.Unreadable [ Errors.explain error ]
        | Ok comments -> routeEvidence target.Canonical comments

    /// Mutation boundaries need the underlying IO error as well as the fail-closed route verdict.  In
    /// particular, a rate-limited receipt read must remain EX_RATE for a JSON worker, not be flattened into
    /// a malformed/missing route and accidentally lose its back-off contract.
    let private requireCurrentDeliveryRoute (ctx: Context) (target: Ref) =
        match readDeliveryRouteComments ctx target with
        | Error error -> Error error
        | Ok comments ->
            // .github#2583 repair 2: THIS is where the DEC-001 trade is spent — a worker claiming the row
            // is committing to a route decision that may have had content added beside what it judged.
            // `show` reporting it is not enough if the boundary that ACTS on the row says nothing, so the
            // same note is emitted here, from the one shared spelling. The verdict is untouched: this
            // reports, it does not refuse.
            match routeEvidence target.Canonical comments with
            | DeliveryRoute.Current route -> Ok route
            | DeliveryRoute.Stale reasons
            | DeliveryRoute.Unreadable reasons ->
                Error(Errors.Malformed(target.Canonical, "delivery route is not current: " + String.concat "; " reasons))

    let private offlineDeliveryRoute =
        DeliveryRoute.Current
            { Schema = DeliveryRoute.Schema; Subject = "offline"; SubjectRevision = "offline"; Route = Some DeliveryRoute.Lightweight
              Agent = "offline"; Timestamp = "1970-01-01T00:00:00Z"; ReasonCodes = [ "offline" ]; Rationale = "offline diagnostic"
              DeclaredImpacts = [ "offline" ]; ObservedFacts = [ "offline" ]; SddWorkId = None; SpecHome = None; RequiredGates = [] }

    /// .github#2300 AC1/AC2/AC4: is this candidate's schedulability verdict ALREADY DECIDED by steps
    /// that run strictly before the route check, so the real delivery-route receipt could not change it?
    ///
    /// `Schedulability.schedulable`'s own ordering comment (step "3c. DELIVERY ROUTE") places the route
    /// match arm AFTER issue state (1), board column (2), blockers (3), and the human hold (3b) — every
    /// one of which is already a known, free fact by the time a candidate reaches here (`Snapshot.parse`
    /// plus the board scan `enrichBoardFacts` already ran). So this previews `schedulable` against a
    /// NEUTRAL placeholder route: `offlineDeliveryRoute` is already `Current`, so it can never itself be
    /// the reason the preview stops, and the four cases matched below are exactly the verdicts steps
    /// 1-3b can produce on their own. `inFlight = []` for the same reason — every step this preview can
    /// reach (1 through 3b) reads neither `item.TouchSet` nor the lock/overlap facts.
    ///
    /// A preview verdict OUTSIDE those four cases means steps 1-3b did NOT already decide the item —
    /// touch-set, lock, or the route itself still could — so the real receipt is read for real (AC3: the
    /// gate still fails closed for a genuinely schedulable row with a missing/stale/unreadable receipt).
    let private routeCannotChangeVerdict (allowBacklog: bool) (item: Item) : bool =
        // .github#2305 — `inFlight = []` here (see the doc above: this preview never reaches step 6), so
        // no disjointness hit can ever occur and `generated` cannot change the answer either way. `Set.empty`.
        match Schedulability.schedulable Set.empty allowBacklog [] { item with DeliveryRoute = offlineDeliveryRoute } with
        | Schedulability.IssueClosed
        | Schedulability.WrongStatus _
        | Schedulability.BlockedBy _
        | Schedulability.AwaitingHuman _ -> true
        | _ -> false

    /// THE FIX FOR .github#2300. Before this, every candidate — closed, `Blocked`, `Backlog`-without-opt-
    /// in, all of them — paid `readDeliveryRouteVerdict`'s two REST reads (`issueBody` plus a
    /// `commentBodies` that PAGINATES with how much protocol traffic the issue has accumulated, comment
    /// 4) whether or not the eventual decision would ever consult the answer. On an 887-candidate board
    /// where the overwhelming majority are exactly those already-decided rows, that is the whole of the
    /// measured cost.
    ///
    /// A candidate `routeCannotChangeVerdict` clears is left UNENRICHED, deliberately: its
    /// `Item.DeliveryRoute` keeps whatever `Snapshot.parse` defaulted it to (`Unreadable [...]`, read by
    /// nobody), because `Batch.scheduleWith`'s real `Schedulability.schedulable` call on this same item
    /// hits the identical early exit and never reaches its own route match arm either. The two engines
    /// cannot disagree because they are the SAME function, run twice on the same inputs at two different
    /// times — a preview now, the real decision in `renderLiveDecision` — never two rules that could
    /// drift (#485).
    let private enrichDeliveryRoutes (ctx: Context) (request: Snapshot.Request) =
        { request with
            Candidates =
                request.Candidates
                |> List.map (fun candidate ->
                    if routeCannotChangeVerdict request.AllowBacklog candidate.Item then
                        candidate
                    else
                        let route = readDeliveryRouteVerdict ctx candidate.Item.Ref
                        { candidate with Item = { candidate.Item with DeliveryRoute = route } }) }

    /// Scan the board and decide. The shared body of `next`/`batch`/`take` — one board read, one decision,
    /// so the three can never disagree about which items exist (#485).
    let private scanAndDecide (ctx: Context) (opts: Options) (intent: Cache.ReadIntent) =
        Board.bootstrapCached ctx.Transport ctx.Owner ctx.Title
        |> Result.bind (fun board -> Scan.board ctx.Transport intent ctx.Owner ctx.Title board.Number)
        |> Result.bind (fun rows ->
            Scan.snapshot ctx.Transport rows opts.Repo opts.AllowBacklog opts.Limit opts.LeaseMinutes
            |> Result.map (fun (doc, receipt) -> rows, doc, receipt))

    /// `snapshot` already scoped by `--repo`; this is the SAYING half (#979).
    ///
    /// `next`/`batch`/`take` each report an empty queue in their own words — `nothingSchedulable`, on
    /// stdout from `batch --text` and `take --text` and on stderr from `next` (.github#1562), and `take`'s
    /// EX_NONE in every projection — and every one of those sentences is TRUE of a `--repo` that named
    /// nothing, which is exactly what makes it invisible. This is the verb family
    /// where that costs the most: `--repo <short-id>` is the documented spelling, a typo is the single
    /// likeliest thing a worker types, and `take` is the one command in a worker's loop. So the reason
    /// rides out with the verdict rather than being dropped with the receipt.
    let private sayRepoAdvisory (receipt: Scan.Receipt) =
        receipt.RepoAdvisory |> Option.iter eprint

    /// Populate `Item.Class` with the TITLE half of the derivation, `Item.BoardClass` with what the scan
    /// observed in the board column (.github#1588, ADR-0066), and `Item.Phase`/`Item.AgeDays` with the two
    /// remaining rank inputs the snapshot document cannot carry (.github#1598).
    ///
    /// `Phase` and the age join here for exactly the reason `BoardClass` does: both are SCAN facts. `Phase`
    /// is a board COLUMN, and the age comes off the issue's `createdAt` — neither is derivable from an item
    /// body, so neither can live on the pure snapshot. A `decide --snapshot` run therefore ranks on
    /// blocking-count alone, which is `Rank`'s no-priority-data case and behaves exactly as it did before.
    ///
    /// **THE CLOCK IS READ HERE, ONCE.** `Scan.Row` carries the INSTANT so the cache cannot serve a stale
    /// age; this is the only place it becomes a number of days, so every candidate in one batch is aged
    /// against one `now` and a rank cannot shift underneath its own comparison. A `createdAt` in the
    /// FUTURE (clock skew) floors at 0 rather than going negative — a negative age would sort as the
    /// OLDEST item on the board.
    ///
    /// THE IMPURE HALF, on `enrichPredicates`' terms exactly. `Snapshot.parse` already lifted the pure,
    /// body-only `Class:` declaration off each item; two facts are not on that document and cannot be:
    ///
    /// - the TITLE, which AC3 names as evidence via the `[decision]` prefix the board already uses. It is
    ///   a scan fact, not a snapshot one.
    /// - the board's `Class` COLUMN, which is the projection this engine writes and must therefore READ
    ///   before it writes — `CLASS-PROJECTION-LAG` fires on disagreement, and a chore that could not see
    ///   the current value could never retire.
    ///
    /// The body WINS over the title, so this never overwrites a `Class:` line somebody wrote: it fills in
    /// only where the pure parse found nothing. That ordering is `Class.derive`'s and it is asserted there
    /// rather than re-decided here.
    ///
    /// A row the scan did not carry keeps what the parser gave it. That is the fail-closed direction: an
    /// item we could not join is an item whose column we do not claim to know, and `BoardClass = None`
    /// costs at most one idempotent re-write rather than suppressing a projection that is genuinely owed.
    let private enrichBoardFacts (rows: Scan.Row list) (request: Snapshot.Request) : Snapshot.Request =
        let byRef =
            rows |> List.map (fun r -> r.Ref, r) |> Map.ofList

        // ONE `now` FOR THE WHOLE BATCH. Reading the clock per item would let two candidates a millisecond
        // apart be aged against two different instants, which is a comparison whose inputs move while it
        // runs — and the batch's determinism (#418) is the property that cannot be given up.
        let now = System.DateTimeOffset.UtcNow

        let ageDaysOf (row: Scan.Row) =
            row.CreatedAt
            |> Option.map (fun created ->
                // FLOORED AT ZERO. A `createdAt` ahead of `now` is clock skew, not a negative age, and a
                // negative age sorts as the oldest work on the board — the one direction a skewed clock
                // must not be able to push priority.
                let days = (now - created).TotalDays
                if days <= 0.0 then 0 else int (floor days))

        // WAS THE BODY READ AT ALL? `Snapshot.parse` renders an unreadable body as `Class = None` — the
        // same value as "the body declares no class" — because `ItemClass option` has nowhere to put
        // "I could not look". That collapse is safe there and lethal HERE, at the one place a SECOND source
        // is consulted: an item whose body says `Class: defect` and whose read failed would fall through to
        // a `[decision]` title prefix and be projected as `decision`, a SEVERITY DOWNGRADE PRODUCED BY A
        // FAILED READ — on the one value that means "no driver may ever schedule this". That is #266
        // exactly, and #496's `Undeclared`-vs-`Unreadable` collapse one axis over.
        //
        // The engine already records the fact: `TouchSet.Unreadable` IS "we did not read the body" (see
        // `Types.fsi`), parsed off the same body on the same terms. So this asks rather than adding a
        // second flag that could disagree with it.
        let bodyWasRead (c: Snapshot.Candidate) =
            match c.Item.TouchSet with
            | Unreadable _ -> false
            | _ -> true

        let enrich (c: Snapshot.Candidate) : Snapshot.Candidate =
            match Map.tryFind c.Item.Ref byRef with
            | None -> c
            | Some row ->
                { c with
                    Item =
                        { c.Item with
                            // `Repo Scope` is the authority for WHERE `Paths:` live; `Ref` remains the
                            // issue identity for every GitHub mutation.  The resolver is shared with
                            // `--repo`, so roster short-ids and canonical names cannot split a lane.
                            //
                            // `RepoScope.orFallback` (#2398): a `Repo Scope` of `cross-repo` — the
                            // board's one deliberate non-roster value — names no repository, so it
                            // behaves exactly like an absent one and falls back to the item's own
                            // hosting repository, same as `.github#2351`'s `pathRepoOrFallback` policy
                            // for `overlap`/`activeCollisions`. Before this, the sentinel flowed into
                            // `Item.PathRepo` UNCHANGED and untagged, so two items in the SAME
                            // repository whose Repo Scope merely disagreed (one `cross-repo`, one
                            // rostered) compared unequal downstream and split a lane on the strength of
                            // the sentinel alone, without either touch-set ever being read
                            // (`.github#2386`).
                            PathRepo = FS.GG.Coord.RepoScope.orFallback c.Item.Ref.Repo (Options.resolveRepo row.PathRepo)
                            Class =
                                match c.Item.Class with
                                | Some _ as declared -> declared
                                // No title fallback over an unread body: `None` there is "unknown", and
                                // deriving a WEAKER class from a title we could read, to stand in for a
                                // body we could not, is a confident answer built on a failed read. `None`
                                // yields no chore, which is the fail-closed direction.
                                | None when bodyWasRead c -> Class.fromTitle row.Title
                                | None -> None
                            BoardClass = row.BoardClass
                            // The `Kind:` body line joins here on `Class`'s terms, MINUS the title
                            // fallback — there is no `[register]` title convention and inventing one would
                            // derive a reducer exemption from a naming habit. `c.Item.Kind` is already the
                            // pure body parse (`Snapshot.parse`), so this arm exists only so the join
                            // reads symmetrically with `Class` above; an unread body leaves it `None`,
                            // which `Kind.govern` reads as `Work` — the row behaves exactly as today.
                            Kind = c.Item.Kind
                            BoardKind = row.BoardKind
                            CommentCount = row.CommentCount
                            Severity = row.Severity
                            Phase = row.Phase
                            AgeDays = ageDaysOf row } }

        { request with
            Candidates = request.Candidates |> List.map enrich }


    /// THE WHOLE BOARD'S BLOCKING COUNTS, from the scan rows the offer path already holds (.github#1628).
    ///
    /// **THE GRAPH IS A WHOLE-BOARD FACT; THE CANDIDATE LIST IS A SCOPED PROJECTION OF IT.** `Rank`'s
    /// primary term used to be derived from the candidates, and `Scan.snapshot` scopes those with
    /// `--repo` — so an item in `.github` that three open items in `FS.GG.SDD` are `Blocked by` counted 0
    /// under `take --repo .github` and 3 under a bare org-wide `batch`. Same board, same instant, two
    /// ranks. The repo-scoped spelling is the documented worker loop, so the wrong one was the one that
    /// actually ran, and the item it under-ranked was by construction a cross-repo hub — the thing most
    /// worth scheduling first.
    ///
    /// **IT COSTS NOTHING.** `Scan.blockerGraph` is pure and reads no transport (#1090): a board item's
    /// OPEN/CLOSED state is already in `rows`, so an on-board blocker's resolution is free. `rows` is the
    /// UNSCOPED scan the offer path already paid for — the same list `enrichBoardFacts` joins against.
    ///
    /// THE SOURCE SET IS THE SAME ONE `Scan.snapshot` DERIVES CANDIDATES FROM, minus the scope: OPEN,
    /// non-PR rows. Both filters are load-bearing rather than tidy.
    ///
    /// - **OPEN** because a closed item is not waiting on anything; `Rank.blockingCounts` has always said
    ///   "how many OPEN items name this one", and dropping that filter would keep promoting a hub whose
    ///   dependents all shipped.
    /// - **NON-PR** because `Scan.snapshot` drops PRs before it scopes, so counting a PR's `Blocked by`
    ///   here would credit a dependent the candidate-set spelling never could — a NEW disagreement
    ///   introduced by the fix for a disagreement.
    ///
    /// **THE GRAPH IS BUILT OVER ALL ROWS AND FILTERED AFTER, NEVER BEFORE.** `blockerGraph` resolves a
    /// blocker's state by looking the target up in the rows it was handed, and treats a miss as
    /// `BlockerUnknown` — which BLOCKS. Filtering the rows first would therefore turn every CLOSED target
    /// into an unknown one and count edges that have long since resolved: the closed-blocker case would
    /// silently start inflating exactly the term this item exists to make honest.
    ///
    /// Off-board and unparseable edges keep .github#1598's treatment, unchanged and for free —
    /// `blockerGraph` marks an off-board ref `BlockerUnknown` and prose gets no `Ref` at all, and
    /// `Rank.blockingCountsOf` credits only edges that carry a ref. An off-board ref that IS credited
    /// names a node no candidate can be, so its entry is never read.
    /// Compatibility forward for existing callers; the board-fact seam lives in BoardFactsApplication.
    let boardBlockingCounts = BoardFactsApplication.blockingCounts

    /// THE OFFER PATH'S DECISION — and as of .github#1598 it ENRICHES before it schedules.
    ///
    /// `rows` is no longer discarded here. The scheduler's ordering reads `Class`, `Phase` and the issue's
    /// age, and two of those three are SCAN facts that the snapshot document structurally cannot carry (a
    /// board column and `createdAt`). Scheduling straight off `Snapshot.parse` therefore ranked every item
    /// as having no phase and no age — the exact "no priority data" case — so the rewrite would have
    /// compiled, passed its unit tests, and changed nothing about the live board.
    ///
    /// It is the SAME `enrichBoardFacts` `reconcile` already ran, not a second join: one function, one set
    /// of rules for how a scan row reaches an item, so the ordering `batch` prints and the projection
    /// `reconcile` writes can never come from different readings of one board.
    /// It also carries the WHOLE BOARD's blocking counts (.github#1628) — `rows` is unscoped and the
    /// snapshot's candidates are not, so the counts must come from the wider list or the graph is
    /// truncated. Not `private`, because AC2 asks for a fixture that ranks a cross-repo hub from a
    /// `--repo`-scoped batch, and only this composition — real `Scan.snapshot` bytes, the real enrichment,
    /// the real fold — can answer that. A fixture built on the pieces would pass while the wiring rotted.

    // `renderLiveDecision`, `verifyPaths`, and `delivery` all ask the same question: which changed paths
    // are generated and therefore intentionally absent from an item's authored touch-set / reservable
    // surface (ADR-0044, #498, .github#2305)? The collector is initialized below, beside its bounded
    // process implementation (`generatedPaths`), before this module can serve a command — this forward
    // declaration is what lets every earlier consumer in file order (this one is now the EARLIEST) reuse
    // that one fail-closed collector instead of acquiring a second, weaker list of generated paths.
    let mutable private generatedPathCollector: string -> Set<string> = fun _ -> Set.empty

    let renderLiveDecision (ctx: Context) (opts: Options) (rows: Scan.Row list) (doc: string) : Result<Batch.BatchResult, int> =
        match Snapshot.parse doc with
        | Error errors ->
            for e in errors do
                eprint $"fsgg-coord-engine: %s{e.Path}: %s{e.Message}"

            Result.Error ExitError
        | Ok parsed ->
            let request = parsed |> enrichBoardFacts rows |> enrichDeliveryRoutes ctx

            // .github#2305/ADR-0044 — THIS is the live `take`/`batch`/`next` path (see this function's
            // doc), and it DOES have IO: the same `generatedPathCollector`/`KitDigest.kitRoot()` seam
            // `updateTouchSet`/`activeCollisions`/`overlapCmd` already use. Wiring the real roster HERE is
            // what closes the practical gap the pure `Set.empty` call sites (`Program.fs`'s `lanes`/
            // `decide`, `renderDecision` below) cannot: a live `take`/`batch` no longer refuses two items
            // whose real subjects never collided, only a generated artifact neither of them authors.
            let generated =
                match KitDigest.kitRoot () with
                | Some root -> generatedPathCollector root
                | None -> Set.empty

            match
                Batch.scheduleWith
                    generated
                    (boardBlockingCounts rows)
                    request.AllowBacklog
                    request.Limit
                    request.InFlight
                    (request.Candidates |> List.map (fun c -> c.Item))
            with
            | Green result -> Ok result
            | Red reasons ->
                eprint "REFUSED — the batch cannot be scheduled:"

                for r in reasons do
                    eprint $"  %s{r}"

                Result.Error ExitRed
            | Verdict.NoVerdict reason ->
                eprint $"UNDETERMINED — %s{reason}"
                Result.Error ExitNoVerdict

    /// Pure snapshot projection retained for offline diagnostics. Live board commands use
    /// `renderLiveDecision`, which always reads the mandatory route ledger immediately before scheduling.
    let renderDecision (opts: Options) (rows: Scan.Row list) (doc: string) : Result<Batch.BatchResult, int> =
        match Snapshot.parse doc with
        | Error errors ->
            for e in errors do eprint $"fsgg-coord-engine: %s{e.Path}: %s{e.Message}"
            Result.Error ExitError
        | Ok parsed ->
            let request =
                parsed |> enrichBoardFacts rows
                |> fun r -> { r with Candidates = r.Candidates |> List.map (fun c -> { c with Item = { c.Item with DeliveryRoute = offlineDeliveryRoute } }) }
            // .github#2305 — offline/pure, same reasoning as `Program.fs`'s `lanes`/`decide`: `Set.empty`.
            match Batch.scheduleWith Set.empty (boardBlockingCounts rows) request.AllowBacklog request.Limit request.InFlight (request.Candidates |> List.map _.Item) with
            | Green result -> Ok result
            | Red reasons -> reasons |> List.iter (fun r -> eprint $"  %s{r}"); Result.Error ExitRed
            | Verdict.NoVerdict reason -> eprint $"UNDETERMINED — %s{reason}"; Result.Error ExitNoVerdict

    /// The candidates the scheduler LOOKED AT and refused. One spelling, because two call sites print this
    /// list and a third reports its COUNT on the wire (`take --json`'s `passedOver`, .github#1525) — a
    /// receipt whose number disagreed with the reasons printed beside it would be worse than no number.
    let private passedOver (result: Batch.BatchResult) =
        result.Decisions |> List.filter (fun d -> d.Result <> Schedulability.Startable)

    /// "NOTHING SCHEDULABLE" MUST MEAN *MEASURED* NOTHING (.github#2525 acceptance #2).
    ///
    /// `Decisions` is one entry per candidate the scheduler actually looked at, so its length IS the
    /// measurement — no parallel counter on `BatchResult`, which is how two numbers start disagreeing
    /// (#485). It matters because every other explanatory surface here (`sayPassedOver`, `starvedBanner`,
    /// `explainRanking`) is keyed on `Decisions` and so says nothing AT ALL when it is empty: the
    /// empty-candidate case printed one bare sentence on stdout, nothing on stderr, and exit 0 —
    /// indistinguishable from a board that was fully read and had nothing startable in it. Stating the
    /// count separates "I considered 40 and refused them all" from "I considered nothing".
    ///
    /// STDERR, AND THAT IS THE WHOLE POINT (.github#1562). The stdout headline is a pinned byte-for-byte
    /// contract on both `take` and `batch --text`, and #1562 exists because a change that moved BOTH
    /// streams at once would have been green everywhere. This is an explanation, explanations already all
    /// live on stderr here, and adding a word to the parsed stream to carry one would repeat the mistake
    /// that item was filed about.
    ///
    /// THE EXIT-CODE HALF HOLDS ON THE FRESH-SCAN ROUTE, AND ONLY THERE — stated narrowly on purpose
    /// (.github#2525 repair 2), because the wider claim is the one that is easy to write and false.
    ///
    /// On a fresh scan, `Scan.scanFresh` now refuses a page-set it cannot prove complete, so a truncated
    /// read carries its own non-zero error code and never reaches this function as a green empty ranking.
    ///
    /// On a CACHE HIT it does not, and cannot. `Scan.board` returns `Ok rows` from a parseable cache entry
    /// before `scanFresh` is ever called (`Scan.fs:663-670`), so whatever that entry holds arrives here as
    /// a complete board and exits 0. That is not a gap a completeness guard can close: the failure mode it
    /// matters for — a cache carrying rows that were never on the board — produces an entry that is
    /// complete, well-formed and internally consistent. There is nothing partial about it to detect. It is
    /// prevented at the write instead, by never letting a test-fixture board reach `putScan` at all
    /// (`tests/FS.GG.Coord.Cli.Tests/CacheSandbox.fs`), which is a different repair for a different cause.
    let private sayHowManyConsidered (result: Batch.BatchResult) =
        if List.isEmpty result.Chosen then
            eprint $"considered %d{List.length result.Decisions} candidate(s) — this is a measured count, not an assumption."

    /// THE `--json` STDERR SPLIT, shared by `batch --json` and `take --json` (.github#1525).
    ///
    /// stdout is the machine document; the "why nothing / why less" is stderr, exactly as bash splits them.
    /// `take`'s empty arm needed the identical split, and a second copy of it is the thing that drifts
    /// (#485) — the per-item reasons and #428's banner are ONE answer to "why did I get nothing", so the
    /// two verbs must not be able to start giving different halves of it.
    ///
    /// NO `passed over:` HEADER. That header belongs to the human projection below; the JSON arm has never
    /// printed it, and this is the extraction of what `batch --json` already emitted, not a reformat of it.
    let private sayWhyNothing (leaseMinutes: int) (result: Batch.BatchResult) =
        sayHowManyConsidered result

        for d in passedOver result do
            eprint $"  %s{Batch.explainDecision leaseMinutes d}"

        // The starved-queue banner rides on stderr too (#428).
        for line in Batch.starvedBanner leaseMinutes result do
            eprint line

    /// #440's honest headline, in ONE spelling (.github#1562).
    ///
    /// Three verbs emit it and they do NOT agree on the stream — `batch --text` and `take` put it on
    /// stdout (both through `printChosen` below), `next` on stderr — so the words had to stop being a
    /// literal typed twice. The stream is each verb's own stdout contract: `next`'s is a bare ref read
    /// with `$(…)`, the other two are prose for a human. The SENTENCE is #440's, and a second copy of it
    /// is the thing that drifts (#485).
    let private nothingSchedulable = "nothing schedulable right now."

    /// The human "why nothing / why less" tail of every TEXT projection: the `passed over:` header, the
    /// per-item reasons, and #428's starved banner. ALL STDERR ALREADY — nothing here moved.
    ///
    /// Extracted from `printChosen` by .github#1562 so `next`'s empty arm can keep the whole of it while
    /// putting its headline somewhere else. It is deliberately NOT `sayWhyNothing` above: that one is the
    /// `--json` split and has never printed the `passed over:` header, which belongs to this projection.
    let private sayPassedOver (leaseMinutes: int) (result: Batch.BatchResult) =
        sayHowManyConsidered result

        let passed = passedOver result

        if not (List.isEmpty passed) then
            eprint "passed over:"

            for d in passed do
                eprint $"  %s{Batch.explainDecision leaseMinutes d}"

        // #428 — a queue that hands out NOTHING but is full of items queued behind live claims is BUSY, not
        // empty. Say so, or the honest "nothing schedulable" reads as an empty backlog and sends a worker
        // home from a repo with work in it. Silent on a healthy queue (the banner is []).
        for line in Batch.starvedBanner leaseMinutes result do
            eprint line

    let private printChosen (leaseMinutes: int) (result: Batch.BatchResult) =
        if List.isEmpty result.Chosen then
            printfn "%s" nothingSchedulable
        else
            for item in result.Chosen do
                printfn "  → %s" item.Ref.Short

        sayPassedOver leaseMinutes result

    /// `batch --explain` (.github#1598 AC5) — STDERR, on `sayWhyNothing`'s terms and for its reason.
    ///
    /// `batch`'s stdout is a machine contract (`["FS-GG/FS.GG.SDD#70",…]`) that `take` parses, so an explanation
    /// printed there would corrupt the answer it explains. It rides beside the per-item refusal prose and
    /// #428's banner, which is where every other "why" this verb produces already goes — and it means
    /// `batch --json --explain` is a legitimate spelling rather than a contradiction.
    ///
    /// Silent without the flag: this is a wall of lines proportional to the board, and a driver who did not
    /// ask for the ranking is a driver reading refusal prose it would bury.
    let private sayRanking (opts: Options) (result: Batch.BatchResult) =
        if opts.Explain then
            for line in Batch.explainRanking result do
                eprint line

    /// Read the dispatch model from BOTH host loops. The documents govern the numbers; the engine only
    /// parses them and refuses disagreement. Reading one copy and ignoring the other would let
    /// `drive-board` and `work-board` silently size different fleets again.
    let private readWaveModel () : Result<Batch.WaveModel, string> =
        match KitDigest.kitRoot () with
        | None -> Error "the kit root is unavailable"
        | Some root ->
            let paths =
                [ for skillRoot in [ ".claude/skills"; ".agents/skills" ] do
                      for driver in [ "drive-board"; "work-board" ] do
                          $"%s{skillRoot}/%s{driver}/references/host-loop.md" ]

            let read (relative: string) =
                let path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar))

                if not (File.Exists path) then
                    Error $"%s{relative} is missing"
                else
                    try
                        File.ReadAllText path
                        |> Batch.parseWaveModel
                        |> Result.mapError (fun e -> $"%s{relative}: %s{e}")
                    with e ->
                        Error $"%s{relative} could not be read: %s{e.Message}"

            let existing =
                paths |> List.filter (fun p -> File.Exists(Path.Combine(root, p)))

            match existing with
            | [] -> Error "no drive-board or work-board host-loop document exists"
            | documents ->
                // Receiver topology is intentionally asymmetric: `work-board` materializes always while
                // operator-only `drive-board` never does. Validate every copy that EXISTS and require them
                // to agree, without turning an intentionally absent operator skill into an unavailable
                // signal for the product driver.
                let results = documents |> List.map read
                let errors =
                    results
                    |> List.choose (function
                        | Error e -> Some e
                        | Ok _ -> None)

                let models =
                    results
                    |> List.choose (function
                        | Ok model -> Some model
                        | Error _ -> None)
                    |> List.distinct

                match errors, models with
                | [], [ model ] -> Ok model
                | [], _ -> Error "drive-board and work-board declare different fsgg:wave-model:v1 values"
                | _, _ -> errors |> String.concat "; " |> Error

    /// `batch`'s implementer-slot projection over the very snapshot it scheduled from (.github#2678).
    ///
    /// NAMED, PUBLIC, AND TYPED, WHERE IT USED TO BE AN INLINE FLAT REF LIST. The list was where the two
    /// over-broad clauses lived — `c.Item.ItemPr.IsSome` admitted a row whose PR is open and whose claim
    /// is absent, and `Batch.Unowned item` admitted a reservation that by name has no owner — so `batch`
    /// reported six occupied slots on a board carrying exactly three live claims, while `who` and
    /// `driver --events` read the same board and both answered three. The predicate now lives in
    /// `Batch.implementerSlots`, where it is pure and pinned; this function's remaining job is parsing.
    ///
    /// Public because a projection nobody could call is a projection nobody could pin AGAINST the other
    /// two readings of the same board, which is the disagreement this item is about.
    let slotOccupancyOf (doc: string) : Result<Batch.SlotOccupancy, string> =
        match Snapshot.parse doc with
        | Error errors ->
            errors
            |> List.map (fun e -> $"%s{e.Path}: %s{e.Message}")
            |> String.concat "; "
            |> Error
        | Ok request ->
            Ok(Batch.implementerSlots (request.Candidates |> List.map (fun c -> c.Item)) request.InFlight)

    /// Occupancy is advisory, not enforcing: refusing `batch` on an open slot would prevent the dispatch
    /// that closes it, and a draining queue legitimately has spare capacity. It is nevertheless loud and
    /// typed at the decision point, where a host can act on the measured deficit instead of remembering
    /// prose from the start of a long run. STDERR preserves `batch`'s stdout machine contract byte-for-byte.
    let private sayWaveOccupancy (doc: string) (result: Batch.BatchResult) : unit =
        match readWaveModel (), slotOccupancyOf doc with
        | Ok model, Ok slots ->
            let occupancy = Batch.waveOccupancy model slots.Occupying
            eprint (Batch.renderWaveOccupancy occupancy)

            // .github#2678 acceptance 5 — a PR-bearing, claim-free row is REAL, and it is now said out
            // loud under its own name instead of being folded into implementer occupancy, where the only
            // trace it left was a slot count nobody could reconcile against `who` or `driver --events`.
            // Between the occupancy object and the shortfall headline, because it is the fact that
            // explains why a board can be busy and its slots still open.
            Batch.renderWorkWithoutClaim slots |> Option.iter eprint

            Batch.waveShortfallHeadline (List.length result.Chosen) occupancy
            |> Option.iter eprint
        | model, slots ->
            let explain = function
                | Ok _ -> None
                | Error e -> Some e

            [ explain model; explain slots ]
            |> List.choose id
            |> String.concat "; "
            |> fun reason -> eprint $"wave occupancy: unavailable (%s{reason})"

    let batch (ctx: Context) (opts: Options) : int =
        match scanAndDecide ctx opts Cache.Scheduling with
        | Error e -> fail e
        | Ok(rows, doc, receipt) ->
            sayRepoAdvisory receipt

            match renderLiveDecision ctx opts rows doc with
            | Error code -> code
            | Ok result ->
                match opts.Render with
                | Json ->
                    // THE MACHINE CONTRACT — canonical owner/repo/number refs, sorted as the scheduler
                    // chose them. The array-of-strings shape stays compatible with ref consumers, while the
                    // strings no longer collapse same-repo/number rows owned by different accounts (#2155).
                    // Human text continues to use `Short`; machine decisions must be unambiguous.
                    let ids =
                        result.Chosen
                        |> List.map (fun item -> "\"" + item.Ref.Canonical + "\"")
                        |> String.concat ","

                    printfn "[%s]" ids
                    // The skip reasons and #428's banner go to stderr — a caller reads the array on stdout,
                    // the "why nothing / why less" on stderr, exactly as bash does. `take --json` emits the
                    // same split from the same helper (.github#1525).
                    sayWhyNothing opts.LeaseMinutes result
                | Text ->
                    if not (List.isEmpty result.Chosen) then
                        printfn "schedulable in parallel (%d):" (List.length result.Chosen)

                    printChosen opts.LeaseMinutes result

                // AFTER the verdict, in BOTH renderings — the ranking explains the answer above it, and a
                // flag that worked in one projection and silently did nothing in the other is the #1523
                // defect this repo already paid for once.
                sayRanking opts result
                sayWaveOccupancy doc result

                ExitGreen

    let private planningSourceSha (facts: string list) =
        // This is deliberately wider than occupancy.  The prior digest stayed identical when a pending
        // write, triage fact, claim liveness or review result changed, so an old clean receipt could be
        // replayed over a materially different board.  Every live read consumed by the transition is now
        // one constituent.  Callers can carry the content-addressed receipts; they cannot choose the source
        // against which this invocation validates them.
        facts |> String.concat "\n\u001e\n"
        |> Text.Encoding.UTF8.GetBytes
        |> Security.Cryptography.SHA256.HashData
        |> Convert.ToHexString
        |> fun value -> value.ToLowerInvariant()

    let private parsePlanningReceipt (path: string) : Result<Driver.PlanningReceipt, string> =
        try
            use document = JsonDocument.Parse(File.ReadAllText path)
            let root = document.RootElement
            let receiptFields = Protocol.ledgerPolicy.ReceiptFields
            let observationFields = Protocol.ledgerPolicy.ObservationFields
            let contentIntakeFields = Protocol.ledgerPolicy.ContentIntakeFields
            let contentDispositionFields = Protocol.ledgerPolicy.ContentDispositionFields
            let schemaField, observedAtField, sourceShaField, completeField, consolidationApprovedField, observationsField, contentIntakesField, contentDispositionsField =
                match receiptFields with
                | [ schema; observedAt; sourceSha; complete; consolidationApproved; observations; contentIntakes; contentDispositions ] ->
                    schema, observedAt, sourceSha, complete, consolidationApproved, observations, contentIntakes, contentDispositions
                | _ -> failwith "the ledger receipt field policy is malformed"
            let kindField, observationObservedAtField, observationSourceShaField, outcomeField, receiptIdField =
                match observationFields with
                | [ kind; observedAt; sourceSha; outcome; receiptId ] -> kind, observedAt, sourceSha, outcome, receiptId
                | _ -> failwith "the ledger observation field policy is malformed"
            let sourceFindingField, dispositionField, consumerPathsField, decisionMakerField, rationaleField, evidenceField, dispositionObservedAtField, dispositionSourceShaField, dispositionReceiptIdField =
                match contentDispositionFields with
                | [ sourceFinding; disposition; consumerPaths; decisionMaker; rationale; evidence; observedAt; sourceSha; receiptId ] ->
                    sourceFinding, disposition, consumerPaths, decisionMaker, rationale, evidence, observedAt, sourceSha, receiptId
                | _ -> failwith "the ledger content disposition field policy is malformed"
            let requireFields (fields: string list) (node: JsonElement) =
                fields |> List.iter (fun name ->
                    let mutable value = Unchecked.defaultof<JsonElement>
                    if not (node.TryGetProperty(name, &value)) then failwith $"missing ledger field {name}")
            requireFields receiptFields root
            if root.GetProperty(schemaField).GetString() <> Protocol.ledgerPolicy.Schema then
                failwith "the ledger receipt schema is unsupported"
            let bool (name: string) (node: JsonElement) = node.GetProperty(name).GetBoolean()
            let sourceFindingField =
                match contentIntakeFields with
                | [ sourceFinding ] -> sourceFinding
                | _ -> failwith "the ledger content intake field policy is malformed"
            let contentDisposition (item: JsonElement) : Driver.ContentDispositionReceipt =
                requireFields contentDispositionFields item
                let disposition =
                    match item.GetProperty(dispositionField).GetString() with
                    | "not-reusable" -> Driver.NotReusable
                    | "skill" -> Driver.Skill
                    | "example/fixture" -> Driver.ExampleFixture
                    | "skill+example/fixture" -> Driver.SkillAndExampleFixture
                    | _ -> failwith "the ledger content disposition is unsupported"
                let evidence =
                    match item.GetProperty(evidenceField).GetString() with
                    | null | "" -> None
                    | value when value.StartsWith "url:" -> Some(Driver.EvidenceUrl(value.Substring 4))
                    | value when value.StartsWith "path:" -> Some(Driver.EvidencePath(value.Substring 5))
                    | _ -> failwith "the ledger content evidence is unsupported"
                { SourceFinding = item.GetProperty(sourceFindingField).GetString() |> Option.ofObj |> Option.defaultValue ""
                  Disposition = disposition
                  ConsumerPaths = item.GetProperty(consumerPathsField).EnumerateArray() |> Seq.map (fun path -> path.GetString() |> Option.ofObj |> Option.defaultValue "") |> Seq.toList
                  DecisionMaker = item.GetProperty(decisionMakerField).GetString() |> Option.ofObj |> Option.defaultValue ""
                  Rationale = item.GetProperty(rationaleField).GetString() |> Option.ofObj |> Option.defaultValue ""
                  Evidence = evidence
                  ObservedAt = item.GetProperty(dispositionObservedAtField).GetInt64()
                  SourceSha = item.GetProperty(dispositionSourceShaField).GetString() |> Option.ofObj |> Option.defaultValue ""
                  ReceiptId = item.GetProperty(dispositionReceiptIdField).GetString() |> Option.ofObj |> Option.defaultValue "" }
            let receipt: Driver.PlanningReceipt =
                { ObservedAt = root.GetProperty(observedAtField).GetInt64()
                  SourceSha = root.GetProperty(sourceShaField).GetString() |> Option.ofObj |> Option.defaultValue ""
                  Complete = bool completeField root
                  ConsolidationApproved = bool consolidationApprovedField root
                  Observations =
                    root.GetProperty(observationsField).EnumerateArray()
                    |> Seq.map (fun item ->
                        requireFields observationFields item
                        ({ Kind = item.GetProperty(kindField).GetString() |> Option.ofObj |> Option.defaultValue ""
                           ObservedAt = item.GetProperty(observationObservedAtField).GetInt64()
                           SourceSha = item.GetProperty(observationSourceShaField).GetString() |> Option.ofObj |> Option.defaultValue ""
                           Outcome = item.GetProperty(outcomeField).GetString() |> Option.ofObj |> Option.defaultValue ""
                           ReceiptId = item.GetProperty(receiptIdField).GetString() |> Option.ofObj |> Option.defaultValue "" }: Driver.PlanningObservation))
                    |> Seq.toList
                  ContentDispositions =
                    root.GetProperty(contentDispositionsField).EnumerateArray()
                    |> Seq.map contentDisposition
                    |> Seq.toList
                  ContentIntakes =
                    root.GetProperty(contentIntakesField).EnumerateArray()
                    |> Seq.map (fun item ->
                        requireFields contentIntakeFields item
                        item.GetProperty(sourceFindingField).GetString() |> Option.ofObj |> Option.defaultValue "")
                    |> Seq.toList }
            Ok receipt
        with error -> Error $"the driver receipt is malformed: %s{error.Message}"

    /// One candidate's live board/claim/PR/review/delivery facts, projected into the shape
    /// `DriverEvents.classify` consumes (.github#2135). Named and pure over its inputs — no `ctx`, no
    /// IO — precisely so it is unit-testable without a live board scan
    /// (independent review round 1, finding 3: "the entire CLI execution path ... has zero
    /// test coverage"). `reviewByPr`/`mergedFactsByRef` are pre-computed by the caller from the SAME
    /// live reads `driver`'s existing planning path already performs.
    let candidateToItemFacts
        (reviewByPr: Map<int, Driver.ReviewChain>)
        (mergedFactsByRef: Map<string, int * bool * Delivery.Obligation list>)
        (now: int64)
        (sourceSha: string)
        (candidate: Snapshot.Candidate)
        : DriverEvents.ItemFacts =
        let refText = candidate.Item.Ref.Canonical
        let claimWorker = candidate.Item.Claim |> Option.map (fun (claim, _) -> claim.Worker.Value)
        let review = candidate.Item.ItemPr |> Option.bind (fun pr -> Map.tryFind pr reviewByPr)

        let merged, obligationsDeclared, obligations, pr =
            match Map.tryFind refText mergedFactsByRef with
            | Some(pr, declared, obligations) -> true, declared, obligations, Some pr
            | None -> false, false, [], candidate.Item.ItemPr

        let evidence =
            match claimWorker, pr with
            | Some worker, Some pr -> $"claim:worker=%s{worker};pr=%d{pr}"
            | Some worker, None -> $"claim:worker=%s{worker}"
            | None, Some pr -> $"pr:%d{pr}"
            | None, None -> $"board-status:%A{candidate.Item.Status}"

        { Ref = refText
          ReadOk = not candidate.Item.ItemPrUnreadable
          UnreadableReason =
            if candidate.Item.ItemPrUnreadable then
                Some "the markerless item-PR probe was unreadable"
            else
                None
          BoardStatus = Some candidate.Item.Status
          IssueState = Some candidate.Item.State
          ClaimWorker = claimWorker
          HumanBlock = candidate.Item.HumanBlock
          Pr = pr
          Review = review
          Merged = merged
          ObligationsDeclared = obligationsDeclared
          Obligations = obligations
          Evidence = evidence
          ObservedAt = now
          SourceSha = sourceSha }

    /// Read the durable `driver --events` cursor (.github#2135). No `--cursor` and a `--cursor` path
    /// that has never been written both read as an empty cursor — a legitimate first run. A path that
    /// EXISTS but cannot be parsed is a DISTINCT, refused case
    /// (independent review round 1, finding 2): "never observed" and "observed and corrupt"
    /// must not collapse into the same silent `Map.empty`, or a cursor file truncated by a killed
    /// process reads back exactly as though nothing had ever run — the same fail-open shape this
    /// module refuses everywhere else (a failed read masquerading as a legitimate "no").
    ///
    /// A DIRECTORY at the cursor path is the SAME root cause wearing a different shape
    /// (independent review round 2): `File.Exists` returns false for a directory exactly as
    /// it does for a missing path, so without this check a directory silently read as "never
    /// written" and fell through into `writeEventsCursorAtomic`, whose `File.Move` cannot rename onto
    /// an existing directory and threw uncaught — a caller input error misreported as an internal
    /// engine defect, plus a leaked temp file on the crash. Checked BEFORE `File.Exists` so it is
    /// refused before either kind of "absent" reasoning is reached.
    let readEventsCursor (path: string option) : Result<DriverEvents.Cursor, string> =
        match path with
        | None -> Ok Map.empty
        | Some path when Directory.Exists path -> Error $"cursor path '%s{path}' is a directory, not a file"
        | Some path when not (File.Exists path) -> Ok Map.empty
        | Some path ->
            try
                use document = JsonDocument.Parse(File.ReadAllText path)

                let decoded =
                    document.RootElement.EnumerateObject()
                    |> Seq.map (fun prop ->
                        let raw = prop.Value.GetString()

                        match DriverEvents.decodeState raw with
                        | Some state -> Ok(prop.Name, state)
                        | None -> Error $"entry '%s{prop.Name}' has an unrecognized state encoding '%s{raw}'")
                    |> Seq.toList

                match decoded |> List.tryPick (function Error e -> Some e | Ok _ -> None) with
                | Some error -> Error $"cursor file '%s{path}' is corrupt: %s{error}"
                | None -> decoded |> List.choose (function Ok pair -> Some pair | Error _ -> None) |> Map.ofList |> Ok
            with ex ->
                Error $"cursor file '%s{path}' could not be parsed: %s{ex.Message}"

    /// Persist the cursor ATOMICALLY (.github#2135 repair round 1, finding 2's second half). A bare
    /// `File.WriteAllText` truncates the target before writing the new bytes — a process killed
    /// mid-write leaves a PARTIAL file, which is precisely the corrupt input `readEventsCursor` above
    /// exists to refuse rather than silently treat as absent. Writing to a sibling temp file in the
    /// SAME directory, then renaming it over the target, means a crash leaves either the complete OLD
    /// file or nothing at that path — never a half-written one. `File.Move` with `overwrite: true` is
    /// a single filesystem rename on the common case (temp and target on the same volume), which is
    /// guaranteed by placing the temp file beside its target rather than under a system temp root.
    let writeEventsCursorAtomic (path: string) (cursor: DriverEvents.Cursor) : unit =
        let json =
            cursor
            |> Map.toList
            |> List.map (fun (ref, state) -> ref, DriverEvents.encodeState state)
            |> dict
            |> JsonSerializer.Serialize

        let directory =
            match Path.GetDirectoryName path with
            | null | "" -> "."
            | value -> value

        let tempPath = Path.Combine(directory, $".{Path.GetFileName path}.tmp-{Guid.NewGuid():N}")
        File.WriteAllText(tempPath, json)
        File.Move(tempPath, path, true)

    /// The `fsgg.coord.driver-events/1` JSON projection (.github#2135) — named so it is directly
    /// testable against a hand-built `DriverEvents.Projection` without a live board scan.
    let renderEventsJson (sourceSha: string) (projection: DriverEvents.Projection) : string =
        let transitions =
            projection.Transitions
            |> List.map (fun e ->
                {| ref = e.Ref
                   previous = e.Previous |> Option.map DriverEvents.encodeState |> Option.toObj
                   state = DriverEvents.encodeState e.New
                   reason = e.Reason
                   evidence = e.Evidence
                   observedAt = e.ObservedAt
                   sourceSha = e.SourceSha |})

        let activeItems =
            projection.Active
            |> List.map (fun c ->
                {| ref = c.Ref
                   state = DriverEvents.encodeState c.State
                   reason = c.Reason
                   evidence = c.Evidence |})

        // THE MACHINE PROJECTION NEEDS THE SAME COMPLETENESS FACT AS THE TEXT ONE (.github#2525).
        // A reader that only saw `active: []` could not tell a measured-empty inventory from one this read
        // never finished, which is exactly the collapse the text renderer was fixed for. `activeComplete`
        // is the single boolean a driver can branch on; `unreadable` carries the detail. Additive keys on
        // an existing schema — no consumer of `transitions`/`active` changes.
        let unreadableItems =
            projection.Unreadable
            |> List.map (fun c ->
                {| ref = c.Ref
                   state = DriverEvents.encodeState c.State
                   reason = c.Reason
                   evidence = c.Evidence |})

        JsonSerializer.Serialize
            {| schema = "fsgg.coord.driver-events/1"
               sourceSha = sourceSha
               renderedAt = projection.RenderedAt
               transitions = transitions
               active = activeItems
               activeComplete = List.isEmpty projection.Unreadable
               unreadable = unreadableItems |}

    /// Live inspection derives occupancy from the same board snapshot as `batch`, never caller input.
    let driver (ctx: Context) (opts: Options) : int =
        /// EVERY receipt the chain carries, not the one it is allowed to carry.
        ///
        /// A receipt names ONE rename pair, so a diff with two distinct renames cannot be covered by a
        /// single one — and this used to return `Some` only for exactly one receipt in exactly one
        /// comment, which made a covering submission impossible to author (.github#2144 repair-phase
        /// round 2). Malformed encodings are dropped HERE and re-read by `Driver`, which is the layer
        /// that reports them; this function's job is to supply the recomputations, not to grade.
        let auditReceipts (comments: Driver.ReviewComment list) =
            comments
            |> List.collect (fun comment ->
                comment.Body.Split '\n'
                |> Array.choose (fun line ->
                    let prefix = "diff-audit-receipt-v1:"
                    let line = line.Trim()
                    if line.StartsWith(prefix, StringComparison.Ordinal) then Some(line.Substring(prefix.Length).Trim()) else None)
                |> Array.toList)
            |> List.choose (SemanticDiff.ofBase64 >> Result.toOption)

        let collect rows =
            rows
            |> List.fold (fun state next -> Result.bind (fun all -> Result.map (fun row -> all @ [ row ]) next) state) (Ok [])

        /// The blob pair a rename is visible in.  A 404 is the SERVER saying the path is absent at that
        /// ref — a file this PR added or deleted — which is empty content and a readable fact. Every
        /// other failure stays an error, because "I could not read it" and "there is nothing there" are
        /// opposite answers and #421 is what merging them costs.
        let blobPair owner repo path baseSha headSha =
            let at gitRef =
                match Reads.fileAtRef ctx.Transport owner repo path gitRef with
                | Ok content -> Ok content
                | Error(Errors.NotFound _) -> Ok ""
                | Error e -> Error e
            match at baseSha, at headSha with
            | Ok before, Ok after -> Ok(path, before, after)
            | Error e, _
            | _, Error e -> Error e

        // Recomputation reads blobs through the SAME `blobPair` rule as discovery.  When it used a bare
        // `fileAtRef`, a declared path this PR ADDED 404'd at the base and the whole candidate was
        // dropped — so a correct receipt over a new file was discarded rather than validated, and the two
        // arms disagreed about what "absent at that ref" means.
        let recomputeAudit owner repo baseSha headSha (submitted: SemanticDiff.Receipt) =
            submitted.DeclaredPaths
            |> List.map (fun path ->
                blobPair owner repo path baseSha headSha
                |> Result.map (fun (path, before, after) ->
                    SemanticDiff.inventory path before after submitted.OldToken submitted.NewToken))
            |> List.fold (fun state next -> Result.bind (fun all -> Result.map (fun rows -> all @ rows) next) state) (Ok [])
            |> Result.map (fun rows ->
                SemanticDiff.receipt submitted.Repository baseSha headSha submitted.OldToken submitted.NewToken submitted.DeclaredPaths true rows)

        match scanAndDecide ctx opts Cache.Scheduling, readWaveModel () with
        | Error e, _ -> eprint (Errors.explain e); ExitError
        | _, Error e -> eprint e; ExitError
        | Ok(_, doc, _), Ok model ->
            // THE SAME PROJECTION `batch` REPORTS, AND THE SECOND CONSUMER THE MISCOUNT REACHED
            // (.github#2678). `Driver.nextAction` sizes the next wave as
            // `min slotsPerWave (capacity - activeItems)`, so every claim-free row with an open
            // `item/<n>-*` PR shrank the wave this planner offered by one, exactly as it shrank the
            // `openSlots` `batch` printed. One derivation now answers both, so the planning verb and the
            // scheduling verb cannot drift from each other or from `driver --events`.
            match slotOccupancyOf doc with
            | Error e -> eprint e; ExitError
            | Ok slots ->
                let active = slots.Occupying
                match Snapshot.parse doc with
                | Error errors ->
                    errors |> List.iter (fun error -> eprint $"fsgg-coord-engine: %s{error.Path}: %s{error.Message}")
                    ExitError
                | Ok snapshot ->
                    let reviewEvidence =
                        snapshot.Candidates
                        |> List.choose (fun candidate ->
                            match candidate.Item.ItemPr with
                            | Some pr ->
                                match Reads.markerScan ctx.Transport candidate.Item.Ref.Owner candidate.Item.Ref.Repo candidate.Item.Ref.Number,
                                      Reads.prLandable ctx.Transport candidate.Item.Ref.Owner candidate.Item.Ref.Repo pr,
                                      Reads.prHeadSha ctx.Transport candidate.Item.Ref.Owner candidate.Item.Ref.Repo pr,
                                      Reads.commentsWithIdentity ctx.Transport candidate.Item.Ref.Owner candidate.Item.Ref.Repo pr with
                                | Ok scan, PrGreen, Ok head, Ok comments when List.isEmpty scan.Unreadable ->
                                    let owner = candidate.Item.Ref.Owner
                                    let repo = candidate.Item.Ref.Repo
                                    let comments = comments |> List.map (fun c -> ({ Id = c.Id; Url = c.Url; Body = c.Body }: Driver.ReviewComment))
                                    let threshold =
                                        match Environment.GetEnvironmentVariable "FSGG_DIFF_AUDIT_THRESHOLD" with
                                        | null | "" -> Some 5
                                        | value ->
                                            match Int32.TryParse value with
                                            | true, number when number >= 0 -> Some number
                                            | _ -> None
                                    match threshold,
                                          Reads.issueBody ctx.Transport owner repo candidate.Item.Ref.Number,
                                          Reads.commitMessage ctx.Transport owner repo head,
                                          Reads.prFiles ctx.Transport owner repo pr with
                                    | Some threshold, Ok itemBody, Ok commitMessage, Ok changedPaths ->
                                        let finish required trusted =
                                            match Driver.parseReviewCommentsWithFacts required trusted comments with
                                            | Ok review when review.HeadSha = Some head ->
                                                Some(pr, head, { review with ChecksGreen = true }, scan.Markers.Length)
                                            | _ -> None
                                        // THE THRESHOLD COUNTS OCCURRENCES, SO IT MUST BE MEASURED IN
                                        // OCCURRENCES (.github#2144, the finding this repair phase exists for).
                                        // The no-receipt arm used to pass `changedPaths.Length` — the changed-FILE
                                        // count — into a parameter documented and named as an occurrence count. They
                                        // are different quantities and the file count is always the smaller one, so a
                                        // one-file rename with six quoted occurrences supplied `1`, computed
                                        // `mechanicallyRequired = false` against the default threshold of 5, and let a
                                        // `diff-audit-required:false` chain merge with no receipt at all. Omitting the
                                        // receipt therefore ANSWERED the question the receipt exists to answer, which
                                        // is the agent-memory failure this item removes.
                                        //
                                        // AND REQUIREDNESS IS MEASURED FROM THE SAME POPULATION IN BOTH ARMS
                                        // (round 1 finding 2). The receipt arm used to count only
                                        // `trusted.Occurrences.Length` — the recomputation of the ONE token pair the
                                        // author chose to submit. On an identical diff that made "no receipt" report
                                        // 7 occurrences and require the audit, while a receipt naming the narrowest
                                        // rename in the same diff reported 1 and turned the gate OFF. That is the
                                        // escalated defect wearing different clothes: the author's choice of what
                                        // evidence to supply must never decide whether the gate applies. So the
                                        // threshold input below is always what the ENGINE discovers over the whole
                                        // diff, and the submitted receipt is evidence to be CHECKED, never the
                                        // population to be checked against.
                                        //
                                        // Evidence we could not read stays UNKNOWN throughout: it requires the
                                        // receipt rather than disproving the threshold. A negative fact is never
                                        // manufactured from a missing one.
                                        let declared = SemanticDiff.activationRequired threshold 0 commitMessage (Some itemBody)

                                        /// Every occurrence the engine can discover over the whole diff, read from the
                                        /// live base/head blobs of every changed path. An `Error` means the reads
                                        /// failed, which is emphatically not an empty inventory.
                                        let liveOccurrences baseSha =
                                            if List.isEmpty changedPaths then
                                                Ok []
                                            else
                                                changedPaths
                                                |> List.map (fun path -> blobPair owner repo path baseSha head)
                                                |> collect
                                                |> Result.map SemanticDiff.discoveredOccurrences

                                        let requiredFrom baseSha =
                                            match liveOccurrences baseSha with
                                            // Threshold satisfaction could not be DISPROVED. Fail closed.
                                            | Error _ -> true
                                            | Ok occurrences ->
                                                SemanticDiff.activationRequired threshold occurrences.Length commitMessage (Some itemBody)

                                        match auditReceipts comments with
                                        | _ :: _ as submitted ->
                                            match Reads.prBaseSha ctx.Transport owner repo pr with
                                            | Error _ -> None
                                            | Ok baseSha ->
                                                // One recomputation per submitted receipt proves each HONEST about the
                                                // pair it names; the whole-diff discovery proves them COMPLETE. A
                                                // receipt arm that carried only the first accepted a receipt for 6 of
                                                // 12 discovered occurrences (round 2 of this repair phase).
                                                let expected =
                                                    submitted
                                                    |> List.map (recomputeAudit owner repo baseSha head)
                                                    |> collect

                                                match expected, liveOccurrences baseSha with
                                                | Ok expected, Ok discovered ->
                                                    let required =
                                                        declared
                                                        || SemanticDiff.activationRequired threshold discovered.Length commitMessage (Some itemBody)

                                                    finish required (Some({ Expected = expected; Discovered = discovered }: SemanticDiff.TrustedAudit))
                                                | _ ->
                                                    // The live facts could not be established, so the receipts cannot be
                                                    // checked against anything. Requiring the audit while supplying NO
                                                    // trusted facts makes the gate refuse — an unreadable diff must not
                                                    // let unverified receipts through, which is the same fail-closed
                                                    // rule the no-receipt arm applies to the threshold itself.
                                                    finish true None
                                        | [] ->
                                            if declared then
                                                // An item/commit declaration already decides it; the blob reads
                                                // cannot change the answer and are not worth the REST budget.
                                                finish true None
                                            elif List.isEmpty changedPaths then
                                                // `prFiles` SUCCEEDED and returned nothing, so "no occurrences" is
                                                // a read fact here rather than an unread one — there is no diff for
                                                // a rename to hide in. This is the one shape where zero is honest.
                                                finish false None
                                            else
                                                match Reads.prBaseSha ctx.Transport owner repo pr with
                                                | Error _ -> None
                                                | Ok baseSha -> finish (requiredFrom baseSha) None
                                    | _ -> None
                                | _ -> None
                            | None -> None)
                    let pending = Cache.pending ()
                    let identity = Identity.resolve opts.Worker
                    let staleClaim =
                        snapshot.Candidates
                        |> List.exists (fun candidate ->
                            match candidate.Item.Claim with
                            | Some(_, Types.LeaseExpiredNoPr) -> true
                            | _ -> false)
                    // Preserve full board/body/claim provenance while removing only the continuously
                    // increasing age.  The derived stale/non-stale boundary is added separately, so the
                    // receipt changes exactly when that age becomes actionable, not every second.
                    let stableBoard =
                        Text.RegularExpressions.Regex.Replace(doc, "\"ageSeconds\":-?[0-9]+", "\"ageSeconds\":0")
                    let sourceSha =
                        planningSourceSha
                            [ "board:" + stableBoard
                              "stale-claim:" + string staleClaim
                              "pending:" + (pending |> Result.map (sprintf "%A") |> Result.defaultValue "UNREADABLE")
                              "identity:" + (identity |> Result.map (fun value -> sprintf "%A" value) |> Result.defaultValue "UNRESOLVED")
                              "review:" + sprintf "%A" reviewEvidence
                              "engine:" + string (Reflection.Assembly.GetExecutingAssembly().ManifestModule.ModuleVersionId) ]
                    let now = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                    let suppliedReceipt = opts.SnapshotFile |> Option.bind (parsePlanningReceipt >> Result.toOption)
                    let receiptValid = suppliedReceipt |> Option.exists (Driver.planningReceiptFresh now 300L sourceSha)
                    let pendingWrites = pending |> Result.map List.length |> Result.defaultValue 1
                    let hasIdentity = Result.isOk identity
                    let housekeeping: Driver.Housekeeping =
                        { HasHostIdentity = hasIdentity
                          StaleClaim = staleClaim
                          EngineCurrent = receiptValid
                          PendingWrites = pendingWrites
                          ReconcileDryRunFresh = receiptValid
                          ReconcileApplied = receiptValid
                          ReconcileFresh = receiptValid
                          TriageFresh = receiptValid
                          CurrencyScoped = receiptValid }
                    if opts.Events then
                        // The material-transition/active-inventory projection (.github#2135), layered
                        // over the SAME live board scan and review evidence this command already reads —
                        // no new REST surface for the review sub-states.
                        let reviewByPr =
                            reviewEvidence |> List.map (fun (pr, _, chain, _) -> pr, chain) |> Map.ofList

                        // Obligation/merge facts (clarify DEC-002) are read on the SAME `ItemPr` boundary
                        // `reviewEvidence` already uses — the markerless-duplicate probe. A claim still
                        // held by a live marker has no board-recorded PR number to look up by, so a merge
                        // that happens while the marker is still live classifies `Claimed` rather than
                        // `MergedAwaitingObligations`/`Released` until the claim releases. That is an
                        // honest reflection of what a board scan can discover today, not a shortcut this
                        // projection invents — no other command in this CLI discovers an arbitrary claimed
                        // item's PR number without the caller supplying it (`delivery --pr N`).
                        let mergedFactsByRef =
                            snapshot.Candidates
                            |> List.choose (fun candidate ->
                                match candidate.Item.ItemPr with
                                | Some pr ->
                                    let owner = candidate.Item.Ref.Owner
                                    let repo = candidate.Item.Ref.Repo
                                    match Reads.prLandable ctx.Transport owner repo pr, Reads.prHeadSha ctx.Transport owner repo pr, Reads.commentsWithIdentity ctx.Transport owner repo pr with
                                    | PrMerged, Ok head, Ok comments ->
                                        let comments = comments |> List.map (fun c -> ({ Id = c.Id; Url = c.Url; Body = c.Body }: Driver.ReviewComment))
                                        match DeliveryApplication.obligationsFromComments head comments with
                                        | Ok obligations -> Some(candidate.Item.Ref.Canonical, (pr, true, obligations))
                                        | Error _ -> Some(candidate.Item.Ref.Canonical, (pr, false, []))
                                    | _ -> None
                                | None -> None)
                            |> Map.ofList

                        let facts =
                            snapshot.Candidates
                            |> List.map (candidateToItemFacts reviewByPr mergedFactsByRef now sourceSha)

                        match readEventsCursor opts.CursorFile with
                        | Error message ->
                            // FAIL CLOSED (.github#2135 repair round 1): a cursor file that exists but
                            // cannot be parsed is a corrupt read, never a silent "first run". Refusing
                            // here — rather than falling back to an empty cursor — is what stops a
                            // truncated file from being read back as though nothing had ever happened.
                            eprint $"fsgg-coord-engine: driver --events: %s{message}"
                            ExitError
                        | Ok priorCursor ->
                            let projection = DriverEvents.project priorCursor facts now

                            opts.CursorFile
                            |> Option.iter (fun path -> writeEventsCursorAtomic path projection.Cursor)

                            match opts.Render with
                            | Json -> printfn "%s" (renderEventsJson sourceSha projection)
                            | Text -> printfn "%s" (DriverEvents.renderText projection)

                            ExitGreen
                    else

                    let reviewedPrs = reviewEvidence |> List.map (fun (pr, _, _, _) -> pr) |> Set.ofList
                    let workerReturns =
                        snapshot.Candidates
                        |> List.choose (fun candidate ->
                            match candidate.Item.Claim with
                            | Some(_, Types.LeaseHeld) ->
                                Some
                                    ({ ClaimLive = true
                                       ReviewReady = candidate.Item.ItemPr |> Option.exists reviewedPrs.Contains
                                       ParkedOrDone = candidate.Item.Status = Types.Blocked || candidate.Item.Status = Types.Done }: Driver.WorkerReturn)
                            | _ -> None)
                    let consolidationApproved = suppliedReceipt |> Option.exists (fun receipt -> receiptValid && receipt.ConsolidationApproved)
                    let action = Driver.nextAction model (List.length active) consolidationApproved housekeeping workerReturns
                    match opts.Render with
                    | Json -> printfn "{\"schema\":\"fsgg.coord.driver-live/1\",\"sourceSha\":\"%s\",\"receiptValid\":%s,\"activeItems\":%d,\"reviewSlotsReserved\":%d,\"reviewEvidence\":%d,\"action\":\"%A\"}" sourceSha (if receiptValid then "true" else "false") (List.length active) model.ReviewSlots (List.length reviewEvidence) action
                    | Text -> printfn "%A" action
                    ExitGreen

    // Bound after `doneCmd` is defined. The delivery adapter precedes the established completion
    // transaction in this file, while F# binds values top-to-bottom.
    let mutable private completeDelivery: Context -> Options -> int = fun _ _ -> failwith "delivery completion is not initialized"

    /// The delivery receipt and `verify-paths` both exclude generated, CI-gated artifacts from the
    /// authored touch-set boundary.  The collector fails closed, so an unreadable generator can only
    /// make this false (and block landing); it cannot turn an undeclared authored file into a pass.
    let deliveryPathsVerified (touchSet: TouchSet) (files: string list) =
        let generated =
            match KitDigest.kitRoot () with
            | Some root -> generatedPathCollector root
            | None -> Set.empty

        match touchSet with
        | Declared tokens ->
            files
            |> List.forall (fun file ->
                (tokens |> List.exists (fun token -> TouchSet.covers token file))
                || Set.contains file generated)
        | _ -> false

    /// Preserve the parser's exact malformed-chain diagnostic at the live delivery boundary.  Keeping
    /// this small adapter named and directly testable prevents a future `Result.toOption` from turning
    /// an attempted-but-invalid review into the distinct fact that no review was posted.
    let deliveryReviewEvidence (landable: bool) (comments: Driver.ReviewComment list) : Driver.ReviewChain option * string option =
        match Driver.parseReviewComments comments with
        | Ok parsed -> Some { parsed with ChecksGreen = landable }, None
        | Error errors -> None, Some(String.concat "; " errors)

    /// True when a claimed PR has at least one declared, unverified delivery obligation — the sole
    /// signal the lifecycle reducer needs to keep a row `In review` rather than advance it
    /// (round-1 review repair, .github#2264 PR #2271). Pure over already-read facts, precisely so this
    /// is testable directly rather than only through `reconcile`'s live GitHub wiring — the gap the
    /// critic named: the prior inline `.Contains` scan lived only inside that IO fold, and NO test drove
    /// it. REUSES `DeliveryApplication.obligationsFromComments` rather than a second parser: its ANCHORED,
    /// whole-line match means a comment that merely QUOTES an obligation or receipt marker in prose — the
    /// org's ordinary review-comment style — can never satisfy it, so it cannot be mistaken for a real
    /// declaration or a real receipt the way unanchored `.Contains` could.
    ///
    /// An unreadable head or comment thread, or a declaration the parser refuses (malformed, stale,
    /// undeclared), withholds trust rather than granting it: `true`, the same "stay in review" answer a
    /// genuinely outstanding obligation gives. Only a HEAD that reads AND parses AND is fully verified
    /// clears it.
    let outstandingObligations (headFact: Errors.IoResult<string>) (commentsFact: Errors.IoResult<Reads.CommentBody list>) : bool =
        match headFact, commentsFact with
        | Ok head, Ok comments ->
            let comments = comments |> List.map (fun c -> ({ Id = c.Id; Url = c.Url; Body = c.Body }: Driver.ReviewComment))
            match DeliveryApplication.obligationsFromComments head comments with
            | Ok obligations -> obligations |> List.exists (fun o -> not o.Verified)
            | Error _ -> true
        | _ -> true

    /// THE MARKER THAT AUTHORIZES ACTING ON A CLAIM — `review`'s and `delivery`'s shared extension of
    /// `Reads.winner`'s live-lease-only answer with the SAME proof-of-life the scheduler already trusts
    /// for a `HeldByLiveWork` item (#581, `Schedulability.leaseAuthorizes`): an expired lease backed by
    /// an OPEN `item/<n>-*` PR is not abandoned, it is a worker paused at the review handoff — stopped
    /// between turns, unable to heartbeat because it is not running (#2378). One function, so the two
    /// callers cannot silently disagree about what "live" means for the identical fact (#485's lesson).
    ///
    /// `liveness` is a THUNK, not an eager read: `Reads.winner` already answers most calls (a live lease
    /// needs no PR probe), so the one extra REST call this adds is paid only on the path that actually
    /// needs it — `Reads.prAlive`'s own doc comment states the same cost discipline ("only on the ONE
    /// item about to be … reaped, never a scan").
    ///
    /// `Ok None` is a REAL, distinct answer — "no marker authorizes this claim" — kept apart from
    /// `Error`, a read that could not be made. Callers choose their own refusal wording for `None`;
    /// `review` and `delivery` worded it differently before this function existed and still do.
    let authorizedMarker
        (leaseMinutes: int)
        (markers: Reads.Marker list)
        (liveness: unit -> Errors.IoResult<Liveness>)
        : Errors.IoResult<Reads.Marker option> =
        match Reads.winner leaseMinutes markers with
        | Some marker -> Ok(Some marker)
        | None ->
            match Reads.reserver leaseMinutes markers with
            | None -> Ok None
            | Some stale ->
                match liveness () with
                | Ok l when Schedulability.leaseAuthorizes l -> Ok(Some stale)
                | Ok _ -> Ok None
                | Error e -> Error e

    /// `delivery`'s write-side counterpart to `scripts/check-claim-generation.py`'s read side
    /// (.github#2395, design slice 3 of .github#1858 — BOTH acts of §11.2 row 3 now, the merge
    /// election and the authorization that names it; the first landing carried only the second and
    /// said so in this comment's own text).
    ///
    /// UNLIKE the delivery-obligation/receipt markers `DeliveryApplication.obligationsFromComments`
    /// parses, this one is matched the SAME PERMISSIVE way `check-claim-generation.py`'s `AUTH_MARKER_RE`
    /// matches it: DOTALL, anywhere in the body, not anchored to a comment's leading line. That gate is
    /// this marker's only other reader, so this mirrors its tolerance rather than inventing a stricter
    /// one of its own — a marker this function judges "present and current" must be one the gate would
    /// also accept.
    let private authorizationMarkerPattern =
        System.Text.RegularExpressions.Regex(
            @"<!--\s*fsgg:pr-authorization\s.*?-->",
            System.Text.RegularExpressions.RegexOptions.Compiled
            ||| System.Text.RegularExpressions.RegexOptions.Singleline
        )

    /// The exact marker text `delivery` writes: `v=1 item=<owner/repo>#<n> gen=<claim marker comment
    /// id> opkey=<64 lowercase hex> grant=<election comment id> head=<40-hex sha>` — ALL SIX fields
    /// `scripts/check-claim-fence.py`'s `REQUIRED_AUTH_FIELDS` names, in its own order. The two
    /// narrower readers accept supersets, so widening it needed no cutover; `Client.fsi` has the rest.
    //
    // WHY SIX AND NOT FOUR, RECORDED WHERE THE TEMPLATE IS. Until this row's second landing this
    // wrote four fields, and the consequence was not a narrower pass: the fence returns at CHECK 1 on
    // a marker missing `opkey`/`grant`, so its check 4 — the only one of the six a forger cannot
    // satisfy by typing — was never evaluated on any real pull request, while
    // `.github/workflows/fsgg-claim-fence.yml` told operators check 4 "is expected to fail on every
    // real pull request today" for a known reason. A documented expectation about an unreachable
    // branch is `.github#266`'s class one level up.
    //
    // THE TWO NARROWER READERS ARE UNAFFECTED, WHICH IS WHY THIS NEEDS NO CUTOVER.
    // `scripts/check-claim-generation.py` — the only one of the three readers that is a REQUIRED
    // status context on `main` — requires `v`/`item`/`gen`/`head` and commits in its own docstring to
    // "silently accept (never reject on) any ADDITIONAL `key=value` pairs — including a future
    // `opkey=`/`grant=`". The receiver-side validation job in `.github/workflows/kit-materialize.yml`
    // requires the same four with the same tolerance. So a six-field marker passes both, and an
    // existing FOUR-field marker on an open pull request keeps passing both until
    // `rebindAuthorization` replaces it on that pull request's next `delivery` call.
    let authorizationMarker (item: string) (gen: string) (opkey: string) (grant: string) (head: string) : string =
        $"<!-- fsgg:pr-authorization v=1 item=%s{item} gen=%s{gen} opkey=%s{opkey} grant=%s{grant} head=%s{head} -->"

    /// The one write-worthy fact about a PR body: is its authorization already exactly what the live
    /// claim/head demand, or does it need rebinding? `AuthorizationCurrent` lets the caller skip a PATCH
    /// entirely — the common case on every `delivery --apply` call after the first — while
    /// `AuthorizationRebound` carries the WHOLE new body, never a diff, so the caller cannot
    /// accidentally PATCH a partial rewrite.
    type AuthorizationRebind =
        | AuthorizationCurrent
        | AuthorizationRebound of body: string

    /// Rewrite `body` so it carries EXACTLY ONE `fsgg:pr-authorization` marker, bound to the current
    /// `item`/`gen`/`head`. Satisfies the item's owed properties directly, by construction rather than
    /// by a separate check:
    ///   - "replaces rather than duplicates" — every existing match (zero, one, or many) is stripped
    ///     before the fresh marker is appended, so the result never carries more than one;
    ///   - "rebinds on head change" — a single existing match whose text is not byte-identical to the
    ///     freshly rendered marker (a stale head, a stale gen, or any other drift) is treated exactly
    ///     like zero matches: stripped and replaced, never left in place or duplicated alongside a
    ///     second, corrected one.
    //
    // THAT SECOND RULE IS ALSO THE WHOLE MIGRATION FOR THE SIX-FIELD MARKER, and it needed no new
    // code. A four-field marker written before this row's second landing is not byte-identical to a
    // six-field one, so it takes the `AuthorizationRebound` arm and is replaced in place on the next
    // `delivery` call — one marker in, one marker out. There is no cutover, no dual-shape acceptance
    // and no rebinding campaign, because the only marker reader that is a required status context
    // accepts both shapes (see `authorizationMarker`).
    let rebindAuthorization (body: string) (item: string) (gen: string) (opkey: string) (grant: string) (head: string) : AuthorizationRebind =
        let desired = authorizationMarker item gen opkey grant head
        let matches = authorizationMarkerPattern.Matches body
        if matches.Count = 1 && matches.[0].Value.Trim() = desired then
            AuthorizationCurrent
        else
            let stripped = authorizationMarkerPattern.Replace(body, "").TrimEnd()
            if String.IsNullOrWhiteSpace stripped then
                AuthorizationRebound desired
            else
                AuthorizationRebound(stripped + "\n\n" + desired)

    // THE ORDERING RULE, ASKED RATHER THAN RE-IMPLEMENTED (.github#2395, design §4.2).
    //
    // "Lowest id wins, lease-free" is exported exactly once, as `Reads.lowestId` — slice 2 added it
    // (`.github#2312`) for precisely this read, and §4.2 forbids a second copy: *"That ordering rule
    // must not be written twice"*. `Reads` owns no election record and this row does not declare
    // `Reads.fs`, so the candidates are PROJECTED onto `Reads.Marker` to ask the question and the
    // winner is mapped back by id.
    //
    // EVERY FIELD BUT `Id` IS A PLACEHOLDER THIS CALL NEVER READS BACK, and that is safe by the
    // exported contract rather than by inspection of the implementation: `Reads.lowestId`'s own
    // signature comment says it *"filters nothing, decides no arm, and is not a lock"* and answers
    // *"only which marker is first"*. It is `winner` WITHOUT the staleness filter, so no placeholder
    // here can reach a lease, a worker comparison or a column restore. `Worker` is spelled empty
    // rather than plausibly, so a placeholder can never be mistaken for a real identity if one of
    // these values ever escapes into a diagnostic.
    let private lowestElection (elections: DeliveryApplication.Election list) : DeliveryApplication.Election option =
        elections
        |> List.map (fun election ->
            ({ Id = election.Id
               Worker = WorkerId ""
               Session = None
               AgeSeconds = -1
               PreviousStatus = None
               PathRepo = None
               AgentContract = None
               Raw = "" }: Reads.Marker))
        |> Reads.lowestId
        |> Option.bind (fun winner -> elections |> List.tryFind (fun election -> election.Id = winner.Id))

    // The first of §11.2 row 3's two acts: obtain the merge election this pull request's
    // authorization will be GROUNDED IN, posting one only when this delivery target does not
    // already own it. Returns `(opkey, grant)`.
    //
    // THE TWO ACTS CANNOT BE ATOMIC, SO THE ORDER IS THE DESIGN. GitHub offers no multi-write
    // transaction, and this pair is deliberately ordered election-then-authorization because an
    // election is *append-only, never deleted, no lease, no renewal* (§4.2). A failure after the
    // election and before the authorization therefore leaves a DURABLE, REUSABLE fact that the next
    // `delivery` call finds and completes; the reverse order has no such property, because an
    // authorization naming an election that does not exist is check-4 RED and its `grant=` names an
    // id that may later belong to an unrelated comment. This is the opposite shape to the
    // `claim --force` non-atomicity measured on 2026-08-16, where a 503 between a DESTRUCTIVE
    // eviction and the replacement write left the row with no holder at all: both acts here are
    // non-destructive — an append, then an idempotent replace-in-place — so the intermediate state
    // is weaker than the final state rather than worse than the initial one.
    //
    // AND IT REFUSES RATHER THAN DEGRADES. A `compose` refusal, an unreadable comment list, or a
    // failed POST propagates, and `ensureAuthorization` below therefore never reaches its PATCH. No
    // four-field fallback is written: a marker the fence calls ungrounded is exactly the decorative
    // case §6.3 names, and a failed read must not be able to masquerade as a legitimate answer
    // (`#266`). Nothing is made worse by refusing — the previous marker, if any, is left exactly as
    // it was, and `delivery` is safe to re-run.
    let electionGrounding
        (ctx: Context)
        (target: Ref)
        (gen: string)
        (pr: int)
        : Errors.IoResult<string * string> =
        let receiver = $"%s{target.Owner}/%s{target.Repo}"

        // `Operation.compose` is slice 1's key (`.github#2311`), and it is CALLED rather than
        // re-expressed so that this producer and the fence's check 5 cannot disagree about a key.
        // Its refusals are real reachable paths — an item in the board's `<repo>#N` shorthand lands
        // in `ItemNotFullyQualified`, and the `released` claim sentinel in `GenerationNotServerAssigned`
        // — so they are reported in full rather than collapsed into one word.
        match Operation.compose target.Canonical gen receiver Operation.Merge with
        | Error refusals ->
            Error(
                Errors.Malformed(
                    target.Short,
                    "the merge election's operation key could not be composed: "
                    + (refusals |> List.map Operation.describe |> String.concat "; ")
                )
            )
        | Ok key ->
            let opkey = key.Value

            Reads.commentsWithIdentity ctx.Transport target.Owner target.Repo target.Number
            |> Result.bind (fun comments ->
                let owned =
                    comments
                    |> List.map (fun comment ->
                        ({ Id = comment.Id; Url = comment.Url; Body = comment.Body }: Driver.ReviewComment))
                    |> DeliveryApplication.electionsFromComments
                    |> DeliveryApplication.electionsOwnedBy opkey pr

                match lowestElection owned with
                // ALREADY ELECTED — the ordinary case on every call after the first, and the reason a
                // repeated `delivery` neither costs a write nor denies its own pull request. The
                // LOWEST of this target's own elections is named, not the first one read: a duplicate
                // that a lost POST response could have created must not change which id is granted.
                | Some election -> Ok(opkey, string election.Id)
                | None ->
                    // The marker is the comment's FIRST BYTE, because the fence anchors its match at
                    // position 0 of the raw body and never trims. The prose belongs after it.
                    let body =
                        DeliveryApplication.electionMarker opkey target.Canonical gen receiver pr
                        + "\n\nMerge election for this item's current claim generation, posted by `delivery` "
                        + $"for pull request #%d{pr} (`.github#2395`, design §4.2). Append-only: it carries no "
                        + "lease, is never deleted, and the lowest-id election bearing this operation key is the "
                        + "one whose merge this generation admits."

                    Writes.postIssueComment ctx.Transport target body
                    |> Result.map (fun id -> (opkey, string id)))

    /// `delivery`'s automatic write-side counterpart to `scripts/check-claim-generation.py`'s read side
    /// (.github#2395). A no-op whenever there is nothing yet to authorize: no PR (`pr = None`), no LIVE
    /// claim held by this worker (`marker = None` — the same fact `delivery` already refuses to act on
    /// elsewhere), or the PR has already merged (nothing further to authorize).
    /// `rebindAuthorization`'s `AuthorizationCurrent` answer skips the PATCH entirely, so a routine
    /// status check that changed nothing spends one read and zero writes — the common case on every call
    /// after the first.
    ///
    /// NO LONGER GATED ON `--apply` (.github#2488). It used to be — `apply, pr, marker, merged` all had
    /// to line up — on the theory that this was one of `delivery`'s writes and `--apply` gates every
    /// write the command makes. Measured against the fleet's real PRs, that gating made the write
    /// UNREACHABLE: five real `item/<n>-*` PRs merged after this function landed on `main` carried NO
    /// `fsgg:pr-authorization` marker at all, because nothing in the fleet's real flow ever calls
    /// `delivery --apply --pr N` — the documented merge step is a direct `gh api -X PUT …/pulls/<pr>/
    /// merge` (`deep-detail.md`'s "MERGE over REST"), which never reaches this function, and even a
    /// worker who DID pass `--apply` reached it too late: `--apply` immediately attempts the
    /// `GuardedLand`/`Complete` transition in the SAME call, so by the time any CI re-evaluation could
    /// see the freshly-PATCHed body the PR was often already merged or closed. Dropping the gate makes
    /// every LIVE `delivery <ref> --pr N` call — including a plain, non-`--apply` status read — refresh
    /// the marker as a side effect wherever a caller with a live GitHub credential DOES make that call
    /// (a worker's own shell; `pnext-item` §5's own documented step routes to `--snapshot FILE`, an
    /// IO-free path this change does not touch — nothing in that skill's CURRENT text calls the live
    /// form, a distinct reachability gap tracked separately, not fixed by this signature change alone).
    /// This is safe precisely because it is the ONLY thing this function does: unlike `delivery`'s
    /// `GuardedLand`/`Complete` transitions (still exclusively `--apply`-gated, untouched here), a
    /// PR-body PATCH of an HTML-comment marker takes no board-affecting action, so a read-only
    /// inspection performing it carries none of the "did this quietly merge something" risk that gating
    /// write-capable commands behind `--apply` exists to prevent. It ALSO cannot run from this repo's
    /// own CI: the live form's first action is a Coordination Projects (v2) board bootstrap
    /// (`Board.bootstrapCached`), a read this org's CI credential inventory does not carry (ADR-0019
    /// §1, `.github#2332`) — see `.github/workflows/coherence.yml`'s `claim-generation` job comment for
    /// the measured failure and why a CI-side self-heal was tried and deliberately dropped.
    ///
    /// PATCHes `pulls/{n}`, not `issues/{n}`: this is a pull-request-specific field, and the PR-scoped
    /// endpoint is the one GitHub documents for it.
    ///
    /// Not `private` — `tests/FS.GG.Coord.Cli.Tests/DeliveryApplicationTests.fs` drives this directly
    /// against a `Fake.Recorder`, the same "reuse the internal seam rather than restate the whole
    /// `delivery` command's board-scan/PR-facts machinery" idiom `AuthorizedMarkerTests.fs` already uses
    /// for `authorizedMarker`, above. This is what makes the LIVE wired IO (`Reads.prBody` then a
    /// conditional `ctx.Transport.Send` PATCH) hermetically testable, not merely the pure
    /// `rebindAuthorization` decision it wraps.
    let ensureAuthorization
        (ctx: Context)
        (target: Ref)
        (marker: Reads.Marker option)
        (pr: int option)
        (head: string)
        (merged: bool)
        : Errors.IoResult<unit> =
        match pr, marker, merged with
        | Some prNumber, Some heldMarker, false ->
            let gen = string heldMarker.Id

            electionGrounding ctx target gen prNumber
            |> Result.bind (fun (opkey, grant) ->
                Reads.prBody ctx.Transport target.Owner target.Repo prNumber
                |> Result.bind (fun body ->
                    match rebindAuthorization body target.Canonical gen opkey grant head with
                    | AuthorizationCurrent -> Ok()
                    | AuthorizationRebound rebound ->
                        let payload =
                            let o = System.Text.Json.Nodes.JsonObject()
                            o.["body"] <- System.Text.Json.Nodes.JsonValue.Create rebound
                            o.ToJsonString()

                        let request: Request =
                            { Method = "PATCH"
                              Path = $"repos/%s{target.Owner}/%s{target.Repo}/pulls/%d{prNumber}"
                              Query = []
                              Body = Transport.Json payload
                              Budget = Rest
                              IfNoneMatch = None
                              Subject = target.Short }

                        ctx.Transport.Send request |> Result.map ignore))
        | _ -> Ok()

    /// Read a claimed item's delivery facts again immediately before producing the next lifecycle action.
    /// The board scan gives the status/touch-set projection; the marker scan is deliberately repeated over
    /// REST because a cached or earlier scheduler observation cannot authorize a claim-bound transition.
    let private delivery (ctx: Context) (opts: Options) : int =
        match oneArg opts "delivery: an item ref", worker opts with
        | Error code, _ -> code
        | _, Error code -> code
        | Ok raw, Ok w ->
            match parseRef ctx raw with
            | Error message ->
                eprint $"fsgg-coord-engine: delivery: %s{message}"
                ExitError
            | Ok target ->
                let candidate =
                    scanAndDecide ctx { opts with Repo = Some target.Repo; Limit = None } Cache.Scheduling
                    |> Result.mapError Errors.explain
                    |> Result.bind (fun (_, doc, _) ->
                        Snapshot.parse doc
                        |> Result.mapError (fun errors ->
                            errors
                            |> List.map (fun error -> $"%s{error.Path}: %s{error.Message}")
                            |> String.concat "; ")
                        |> Result.bind (fun snapshot ->
                            match snapshot.Candidates |> List.tryFind (fun item -> item.Item.Ref = target) with
                            | Some item -> Ok item
                            | None -> Error $"%s{target.Short} is not present in the fresh board scan"))

                match candidate with
                | Error message ->
                    eprint $"fsgg-coord-engine: delivery cannot establish board facts: %s{message}"
                    ExitError
                | Ok candidate ->
                    let terminalBoardState = candidate.Item.Status = Done && candidate.Item.State = Closed
                    let liveClaim =
                        Reads.markerScan ctx.Transport target.Owner target.Repo target.Number
                        |> Result.bind (Reads.requireCompleteMarkerScan target.Short)
                        |> Result.bind (fun markers ->
                            authorizedMarker opts.LeaseMinutes markers (fun () ->
                                Reads.prAlive ctx.Transport target.Owner target.Repo target.Number)
                            |> Result.bind (function
                                | Some marker when marker.Worker.Value = w.Id -> Ok(Some marker)
                                | Some marker -> Error(Errors.Malformed(target.Short, $"live claim belongs to worker '%s{marker.Worker.Value}', not '%s{w.Id}'"))
                                | None when terminalBoardState -> Ok None
                                | None -> Error(Errors.Malformed(target.Short, "no live claim marker can authorize delivery"))))

                    match liveClaim, Cache.pending () with
                    | Error error, _ -> fail error
                    | _, Error error -> fail error
                    | Ok marker, Ok pending ->
                        // Preserves the SAME three-way distinction `Schedulability` already draws for
                        // scheduling (`Undeclared -> NoTouchSet`, `DeclaredNone -> DeliberatelyNoTouchSet`)
                        // rather than collapsing every empty-ish case into one `Delivery.Known []`, so a
                        // worker reading `delivery`'s own `noVerdict` can tell a deliberate `Paths: none`
                        // from a forgotten declaration from an unread body without opening the issue body
                        // (.github#2233 acceptance 4). `Unreadable` is never posed as any of the three read
                        // cases: a body nobody read becomes `Delivery.Unread`, so `Delivery.validate` can
                        // name the read, not the item, as the failure.
                        let declaredPaths =
                            match candidate.Item.TouchSet with
                            | Declared tokens ->
                                Delivery.Known(
                                    tokens
                                    |> List.map (function | Matchable value | Unmatchable value -> value)
                                )
                            | DeclaredChore -> Delivery.Known [ "any" ]
                            | TouchSet.DeclaredNone -> Delivery.DeclaredNone
                            | TouchSet.Undeclared -> Delivery.Undeclared
                            | Unreadable reason -> Delivery.Unread reason

                        let branchAndPr: Result<string * int option * string * bool * bool * bool * Driver.ReviewChain option * string option * bool * bool * bool * Delivery.Obligation list, Errors.IoError> =
                            match opts.Pr with
                            | None -> Ok(Directory.GetCurrentDirectory(), None, "", false, false, false, None, None, false, false, false, [])
                            | Some pr ->
                                match Reads.prHeadRef ctx.Transport target.Owner target.Repo pr,
                                      Reads.prHeadSha ctx.Transport target.Owner target.Repo pr,
                                      Reads.prLandable ctx.Transport target.Owner target.Repo pr,
                                      Reads.prClosingRef ctx.Transport target.Owner target.Repo pr,
                                      Reads.prFiles ctx.Transport target.Owner target.Repo pr,
                                      Reads.commentsWithIdentity ctx.Transport target.Owner target.Repo pr with
                                | Ok branch, Ok head, landable, Ok closing, Ok files, Ok comments ->
                                    let review, reviewProblem =
                                        comments
                                        |> List.map (fun comment -> ({ Id = comment.Id; Url = comment.Url; Body = comment.Body }: Driver.ReviewComment))
                                        |> deliveryReviewEvidence (landable = PrGreen)
                                    let itemBranchCanonical = branch.StartsWith($"item/%d{target.Number}-", StringComparison.Ordinal)
                                    let linkageCanonical = closing |> Option.exists ((=) target)
                                    let pathsVerified = deliveryPathsVerified candidate.Item.TouchSet files
                                    let reviewComments =
                                        comments
                                        |> List.map (fun comment -> ({ Id = comment.Id; Url = comment.Url; Body = comment.Body }: Driver.ReviewComment))
                                    let obligations = DeliveryApplication.obligationsFromComments head reviewComments
                                    let obligationsDeclared = Result.isOk obligations
                                    let obligations = obligations |> Result.defaultValue []
                                    Ok(branch, Some pr, head, itemBranchCanonical, linkageCanonical, pathsVerified, review, reviewProblem, (landable = PrGreen), (landable = PrMerged), obligationsDeclared, obligations)
                                | Error error, _, _, _, _, _
                                | _, Error error, _, _, _, _
                                | _, _, _, Error error, _, _
                                | _, _, _, _, Error error, _
                                | _, _, _, _, _, Error error -> Error error

                        // Ensure the PR's `fsgg:pr-authorization` marker is current BEFORE deriving the
                        // lifecycle facts below — a write to the PR body that no fact in
                        // `Delivery.Snapshot` reads, so folding it into the same `Result` here cannot
                        // change what `Delivery.inspect` decides; it only keeps the gate's read side
                        // (`scripts/check-claim-generation.py`) current.
                        //
                        // UNCONDITIONAL on `opts.Apply` (.github#2488, was gated on it — see
                        // `ensureAuthorization`'s own doc comment for the measured reachability failure
                        // that gating caused, and for what still has to call this LIVE form for the
                        // write to actually happen). Every LIVE `delivery <ref> --pr N` call reaches
                        // this now, apply or not — a plain status read is enough, not only a
                        // `--apply` invocation.
                        let branchAndPr =
                            branchAndPr
                            |> Result.bind (fun (branch, pr, head, itemBranchCanonical, closingLinkageCanonical, pathsVerified, review, reviewProblem, landable, merged, obligationsDeclared, obligations) ->
                                ensureAuthorization ctx target marker pr head merged
                                |> Result.map (fun () ->
                                    (branch, pr, head, itemBranchCanonical, closingLinkageCanonical, pathsVerified, review, reviewProblem, landable, merged, obligationsDeclared, obligations)))

                        match branchAndPr with
                        | Error error -> fail error
                        | Ok(branch, pr, head, itemBranchCanonical, closingLinkageCanonical, pathsVerified, review, reviewProblem, landable, merged, obligationsDeclared, obligations) ->
                            let facts: Delivery.Snapshot =
                                { Freshness =
                                    { ItemRef = target.Short
                                      ClaimGeneration = marker |> Option.map (fun held -> string held.Id) |> Option.defaultValue "released"
                                      Executor = marker |> Option.map (fun held -> held.Worker.Value) |> Option.defaultValue w.Id
                                      Branch = branch
                                      Worktree = Directory.GetCurrentDirectory()
                                      PullRequest = pr
                                      HeadSha = if pr.IsSome then head else "unpublished"
                                      DeclaredPaths = declaredPaths
                                      BoardState = statusWireName candidate.Item.Status }
                                  ItemBranchCanonical = if pr.IsSome then itemBranchCanonical else true
                                  ClosingLinkageCanonical = if pr.IsSome then closingLinkageCanonical else false
                                  PathsVerified = if pr.IsSome then pathsVerified else false
                                  InReview = pr.IsSome
                                  Review = review
                                  ReviewProblem = reviewProblem
                                  Landable = landable
                                  Merged = merged
                                  // `done` independently verifies GitHub's merged closing record before it
                                  // closes or stamps anything.  This only permits routing to that transaction;
                                  // it never authorizes a write by itself.
                                  MergeReachable = merged
                                  IssueClosed = candidate.Item.State = Closed
                                  BoardDone = candidate.Item.Status = Done
                                  ClaimReleased = marker.IsNone
                                  PendingWrites = List.length pending
                                  CleanupEligible = terminalBoardState && marker.IsNone && List.isEmpty pending
                                  ObligationsDeclared = obligationsDeclared
                                  Obligations = obligations
                                  ParkedReason = None }
                            match Delivery.inspect facts, opts.Apply with
                            | Delivery.Next transition, true when transition.Action = Delivery.GuardedLand ->
                                // A delivery receipt authorizes only the exact claim generation that was
                                // inspected.  Re-read the winning marker immediately before the REST
                                // write so a released, replaced, or stolen claim cannot merge on stale
                                // authority.
                                let currentClaimGeneration =
                                    Reads.markerScan ctx.Transport target.Owner target.Repo target.Number
                                    |> Result.bind (Reads.requireCompleteMarkerScan target.Short)
                                    |> Result.map (fun markers ->
                                        match Reads.winner opts.LeaseMinutes markers with
                                        | Some held when held.Worker.Value = w.Id -> Some(string held.Id)
                                        | _ -> None)
                                match currentClaimGeneration,
                                      Reads.prHeadSha ctx.Transport target.Owner target.Repo pr.Value,
                                      Reads.prBaseTipSha ctx.Transport target.Owner target.Repo pr.Value with
                                | Error error, _, _
                                | _, Error error, _
                                | _, _, Error error -> fail error
                                | Ok generation, Ok currentHead, Ok currentBase ->
                                    match DeliveryApplication.guardedLanding transition.FreshnessToken transition.ActionKey facts generation (Some currentHead) (Some currentBase) (fun () -> Writes.mergeAtHead ctx.Transport target pr.Value head) with
                                    | Error reason ->
                                        eprint $"fsgg-coord-engine: delivery --apply is refused: %s{reason}"
                                        ExitNoVerdict
                                    | Ok receipt ->
                                        eprint $"fsgg-coord-engine: guarded landing receipt: head=%s{receipt.HeadSha} base=%s{receipt.BaseSha}"
                                        match receipt.Result with
                                        | Error error -> fail error
                                        | Ok false ->
                                            eprint "fsgg-coord-engine: delivery merge was refused because the PR is no longer at the inspected head. Re-inspect before attempting another action."
                                            ExitNoVerdict
                                        | Ok true ->
                                            match opts.Render with
                                            | Json ->
                                                printfn "{\"schema\":\"fsgg.coord.delivery/1\",\"verdict\":\"applied\",\"action\":\"guardedLand\",\"freshnessToken\":\"%s\",\"actionKey\":\"%s\"}" transition.FreshnessToken transition.ActionKey
                                            | Text -> printfn "merged %s at the inspected head" target.Short
                                            ExitGreen
                            | Delivery.Next transition, true when transition.Action = Delivery.Complete ->
                                // Delegate the coupled close / board-Done / own-claim-release sequence to
                                // the existing `done` transaction.  Its `Done.verify` re-reads the merged
                                // closer and refuses a stale or unrelated PR before any write.
                                let code = completeDelivery ctx { opts with Args = [ target.Canonical ]; Pr = pr; Apply = false }
                                if code <> ExitGreen then code
                                else
                                    match Cache.pending () with
                                    | Ok [] -> ExitGreen
                                    | Ok pending ->
                                        eprint $"fsgg-coord-engine: delivery completion left %d{List.length pending} queued board write(s); run `flush` and re-inspect before cleanup."
                                        ExitNoVerdict
                                    | Error error -> fail error
                            | Delivery.Next transition, true ->
                                eprint $"fsgg-coord-engine: delivery --apply is refused: the sole fresh action is %A{transition.Action}, not guarded landing."
                                ExitNoVerdict
                            | _ -> DeliveryApplication.render opts facts

    /// The live `review <ref> --pr N` adapter (.github#2175) — matches `delivery <ref> [--pr N]`'s shape
    /// rather than inventing a parallel spelling, and reuses the SAME live reads that function already
    /// makes (`Reads.markerScan`, `Reads.commentsWithIdentity`, `Reads.prLandable`) rather than a second
    /// derivation of "what is the current PR/claim state". `--pr` is REQUIRED here, unlike `delivery`:
    /// there is no review protocol before a PR exists, so a bare `review <ref>` would either fabricate
    /// meaningless facts or silently answer a question nobody asked.
    ///
    /// TWO FACTS THIS READ CANNOT ESTABLISH LIVE, DELIBERATELY DEFAULTED RATHER THAN LEFT UNHANDLED:
    ///   - `RepairPhaseGranted` is always `None`. A granted repair phase binds a NEW claim/branch/PR/
    ///     critic that lives on a DIFFERENT item than the one this command reads, and resolving that
    ///     binding live is future work, not a silent wrong answer today — a caller mid-repair-phase-setup
    ///     sees `RepairPhaseSetup`/`Park` guidance directing it to the host, which is honest.
    ///   - `RepairRouteAvailable` defaults to `true`. Whether a fresh worker/critic slot exists is a
    ///     scheduler fact, not something one PR read can observe. Both outcomes of this default route to
    ///     `Park` (human/host action) in `Review.classify` either way — the default only changes the
    ///     STATE label (`OrdinaryExhaustion` vs `TerminalHumanPark`), never whether the action is safe.
    let private inspectReview (ctx: Context) (opts: Options) : int =
        match oneArg opts "review: an item ref", worker opts with
        | Error code, _ -> code
        | _, Error code -> code
        | Ok raw, Ok w ->
            match parseRef ctx raw with
            | Error message ->
                eprint $"fsgg-coord-engine: review: %s{message}"
                ExitError
            | Ok target ->
                match opts.Pr with
                | None ->
                    eprint "fsgg-coord-engine: review: --pr is required (there is no review protocol before a PR exists)."
                    ExitError
                | Some pr ->
                    let liveClaim =
                        Reads.markerScan ctx.Transport target.Owner target.Repo target.Number
                        |> Result.bind (Reads.requireCompleteMarkerScan target.Short)
                        |> Result.bind (fun markers ->
                            authorizedMarker opts.LeaseMinutes markers (fun () ->
                                Reads.prAlive ctx.Transport target.Owner target.Repo target.Number)
                            |> Result.bind (function
                                | Some marker -> Ok marker
                                | None -> Error(Errors.Malformed(target.Short, "no live claim marker can authorize a review inspection"))))

                    match liveClaim with
                    | Error error -> fail error
                    | Ok marker ->
                        match Reads.prHeadSha ctx.Transport target.Owner target.Repo pr,
                              Reads.prLandable ctx.Transport target.Owner target.Repo pr,
                              Reads.commentsWithIdentity ctx.Transport target.Owner target.Repo pr with
                        | Ok head, checks, Ok comments ->
                            let reviewComments =
                                comments
                                |> List.map (fun comment -> ({ Id = comment.Id; Url = comment.Url; Body = comment.Body }: Driver.ReviewComment))

                            // Derived, not asserted: a live `repair-phase` marker in the comment thread is
                            // the same structural fact `Driver.reviewPhaseFacts` already exposes, so the
                            // caller does not have to pass `--repair` by hand for the common case.
                            let phaseFacts = Driver.reviewPhaseFacts reviewComments

                            let binding: Review.Binding =
                                { ItemRef = target.Canonical
                                  Pr = pr
                                  HeadSha = head
                                  ClaimGeneration = string marker.Id
                                  ImplementerIdentity = marker.Worker.Value
                                  Phase = if phaseFacts.RepairPhasePresent then Review.Repair else Review.Ordinary
                                  Round = 1 }

                            let facts: Review.Facts =
                                { Comments = reviewComments
                                  Checks = checks
                                  RepairPhaseGranted = None
                                  RepairRouteAvailable = true
                                  DiffAuditTrusted = None }

                            let waitResults =
                                comments
                                |> List.sortBy _.Id
                                |> List.map (fun comment -> ReviewWait.tryDecode comment.Body)
                            let waitErrors = waitResults |> List.choose (function Error error -> Some error | _ -> None)
                            let waitEvents = waitResults |> List.choose (function Ok (Some event) -> Some event | _ -> None)
                            let prOpen = checks <> Types.PrMerged && checks <> Types.PrClosed
                            let waitState =
                                if List.isEmpty waitErrors then
                                    ReviewWait.project target.Canonical (Some(string marker.Id)) prOpen DateTimeOffset.UtcNow waitEvents
                                else ReviewWait.Invalid waitErrors
                            ReviewApplication.renderWithWait opts binding facts waitState
                        | Error error, _, _
                        | _, _, Error error -> fail error

    // Append one validated durable review-wait event. Queue entry is a write, never an in-memory host
    // promise: the marker is posted to the reviewed PR and is therefore available to a later host after
    // the worker/critic process exits. Transition writes require an existing entry for the same review
    // generation. Entry is fenced to the current claim generation; resumption still has to reacquire or
    // revalidate that generation before any tree mutation (.github#2756).
    let private recordReviewWait (ctx: Context) (opts: Options) (rawRef: string) (path: string) : int =
        match parseRef ctx rawRef, opts.Pr with
        | Error message, _ -> eprint $"fsgg-coord-engine: review wait: %s{message}"; ExitError
        | _, None -> eprint "fsgg-coord-engine: review wait: --pr is required."; ExitError
        | Ok target, Some pr ->
            try
                let raw = File.ReadAllText path
                let markerBody = if raw.StartsWith(ReviewWait.Marker, StringComparison.Ordinal) then raw else ReviewWait.Marker + "\n" + raw
                match ReviewWait.tryDecode markerBody with
                | Error reason -> eprint $"fsgg-coord-engine: review wait: malformed event: %s{reason}"; ExitError
                | Ok None -> eprint "fsgg-coord-engine: review wait: the draft is not a review-wait event."; ExitError
                | Ok (Some event) ->
                    match Reads.markerScan ctx.Transport target.Owner target.Repo target.Number
                          |> Result.bind (Reads.requireCompleteMarkerScan target.Short),
                          Reads.commentsWithIdentity ctx.Transport target.Owner target.Repo pr with
                    | Error error, _
                    | _, Error error -> fail error
                    | Ok markers, Ok comments ->
                        match Reads.winner opts.LeaseMinutes markers with
                        | None -> eprint "fsgg-coord-engine: review wait: no current claim generation can authorize this write."; ExitNoVerdict
                        | Some claim ->
                            let ordered = comments |> List.sortBy _.Id
                            let parsed = ordered |> List.map (fun comment -> ReviewWait.tryDecode comment.Body)
                            let parseErrors = parsed |> List.choose (function Error error -> Some error | _ -> None)
                            let prior =
                                parsed |> List.choose (function Ok (Some prior) -> Some prior | _ -> None)
                            let terminalGenerations =
                                prior
                                |> List.choose (function
                                    | ReviewWait.Complete (generation, _, _)
                                    | ReviewWait.Cancel (generation, _, _)
                                    | ReviewWait.Timeout (generation, _, _) -> Some generation
                                    | _ -> None)
                                |> Set.ofList
                            let unconsumedEntries =
                                prior
                                |> List.choose (function ReviewWait.Enter receipt -> Some receipt | _ -> None)
                                |> List.filter (fun receipt -> not (Set.contains receipt.ReviewGeneration terminalGenerations))
                            let permitted =
                                if not (List.isEmpty parseErrors) then
                                    let detail = String.concat "; " parseErrors
                                    Error($"existing review-wait ledger is malformed: %s{detail}")
                                else
                                    match event with
                                    | ReviewWait.Enter receipt ->
                                        let now = DateTimeOffset.UtcNow
                                        if receipt.Item <> target.Canonical then Error "receipt item does not match the requested item"
                                        elif receipt.ClaimGeneration <> string claim.Id then Error "receipt claimGeneration is not current"
                                        elif receipt.EnteredAt > now.AddMinutes 5.0 then Error "enteredAt is implausibly in the future"
                                        elif receipt.ExpiresAt <= now then Error "receipt is already expired"
                                        elif prior |> List.exists (function ReviewWait.Enter old when old.ReviewGeneration = receipt.ReviewGeneration -> true | _ -> false) then Error "reviewGeneration already has an entry receipt"
                                        elif not (List.isEmpty unconsumedEntries) then Error "a preceding reviewGeneration is still unconsumed"
                                        else Ok ()
                                    | ReviewWait.Complete (generation, at, _)
                                    | ReviewWait.Cancel (generation, at, _)
                                    | ReviewWait.Timeout (generation, at, _) ->
                                        let entry = prior |> List.tryPick (function ReviewWait.Enter old when old.ReviewGeneration = generation -> Some old | _ -> None)
                                        if entry.IsNone then
                                            Error "transition has no durable entry receipt for this reviewGeneration"
                                        elif prior |> List.exists (function
                                            | ReviewWait.Complete (old, _, _)
                                            | ReviewWait.Cancel (old, _, _)
                                            | ReviewWait.Timeout (old, _, _) -> old = generation
                                            | _ -> false) then
                                            Error "reviewGeneration already has a terminal transition"
                                        else
                                            let receipt = entry.Value
                                            let now = DateTimeOffset.UtcNow
                                            match event with
                                            | _ when receipt.ClaimGeneration <> string claim.Id -> Error "entry receipt claimGeneration is not current"
                                            | ReviewWait.Complete _ when at > now.AddMinutes 5.0 -> Error "completion timestamp is implausibly in the future"
                                            | ReviewWait.Complete _ when at < receipt.EnteredAt -> Error "completion predates queue entry"
                                            | ReviewWait.Complete _ when at > receipt.ExpiresAt -> Error "completion is after bounded review wait expiry"
                                            | ReviewWait.Complete _ when now >= receipt.ExpiresAt -> Error "completion was not durable before bounded review wait expiry"
                                            | ReviewWait.Cancel _ when at > now.AddMinutes 5.0 -> Error "cancellation timestamp is implausibly in the future"
                                            | ReviewWait.Cancel _ when at < receipt.EnteredAt -> Error "cancellation predates queue entry"
                                            | ReviewWait.Cancel _ when now >= receipt.ExpiresAt -> Error "cancellation was not durable before bounded review wait expiry"
                                            | ReviewWait.Timeout _ when at > now.AddMinutes 5.0 -> Error "timeout timestamp is implausibly in the future"
                                            | ReviewWait.Timeout _ when at < receipt.ExpiresAt -> Error "timeout predates expiresAt"
                                            | ReviewWait.Timeout _ when now < receipt.ExpiresAt -> Error "timeout cannot be made durable before expiresAt"
                                            | _ -> Ok ()
                            match permitted with
                            | Error reason -> eprint $"fsgg-coord-engine: review wait: refused: %s{reason}"; ExitNoVerdict
                            | Ok () ->
                                let prTarget = { target with Number = pr }
                                match Writes.postIssueComment ctx.Transport prTarget markerBody with
                                | Error error -> fail error
                                | Ok commentId ->
                                    // Re-read the append-only thread and project in GitHub comment-id
                                    // order. Concurrent terminal writers can both pass the pre-read,
                                    // but only the first durable comment wins; every caller observes
                                    // that same answer and a loser never reports its write as success.
                                    match Reads.commentsWithIdentity ctx.Transport target.Owner target.Repo pr with
                                    | Error error -> fail error
                                    | Ok current ->
                                        let currentParsed =
                                            current
                                            |> List.sortBy _.Id
                                            |> List.map (fun comment -> ReviewWait.tryDecode comment.Body)
                                        let currentErrors = currentParsed |> List.choose (function Error error -> Some error | _ -> None)
                                        let currentEvents = currentParsed |> List.choose (function Ok (Some transition) -> Some transition | _ -> None)
                                        let state =
                                            if List.isEmpty currentErrors then
                                                ReviewWait.project target.Canonical (Some(string claim.Id)) true DateTimeOffset.UtcNow currentEvents
                                            else ReviewWait.Invalid currentErrors
                                        let ownsWinner =
                                            match event, state with
                                            | ReviewWait.Enter expected, ReviewWait.Waiting actual
                                            | ReviewWait.Enter expected, ReviewWait.Completed (actual, _)
                                            | ReviewWait.Enter expected, ReviewWait.Cancelled (actual, _)
                                            | ReviewWait.Enter expected, ReviewWait.Recoverable (actual, _) -> expected = actual
                                            | ReviewWait.Complete (generation, _, evidence), ReviewWait.Completed (actual, winner) -> generation = actual.ReviewGeneration && evidence = winner
                                            | ReviewWait.Cancel (generation, _, evidence), ReviewWait.Cancelled (actual, winner) -> generation = actual.ReviewGeneration && evidence = winner
                                            | ReviewWait.Timeout (generation, _, evidence), ReviewWait.Recoverable (actual, winner) -> generation = actual.ReviewGeneration && evidence = winner
                                            | _ -> false
                                        if not ownsWinner then
                                            eprint "fsgg-coord-engine: review wait: the posted event lost a concurrent durable transition race; re-read review state."
                                            ExitNoVerdict
                                        else
                                            printfn "%s" (JsonSerializer.Serialize {| schema = "fsgg.coord.review-wait-result/v1"; item = target.Canonical; pr = pr; commentId = commentId |})
                                            ExitGreen
            with error -> eprint $"fsgg-coord-engine: review wait: %s{error.Message}"; ExitError

    let private authorizeReviewRecordWait
        (ctx: Context)
        (opts: Options)
        (target: Ref)
        (pr: int)
        (comments: Reads.CommentBody list)
        (draft: StructuredDecision.ReviewRecord)
        =
        match Reads.markerScan ctx.Transport target.Owner target.Repo target.Number
              |> Result.bind (Reads.requireCompleteMarkerScan target.Short) with
        | Error error -> Error(sprintf "the current claim generation could not be read: %A" error)
        | Ok markers ->
            match Reads.winner opts.LeaseMinutes markers with
            | None -> Error "no current claim generation can authorize the structured review write"
            | Some claim ->
                let parsed =
                    comments
                    |> List.sortBy _.Id
                    |> List.map (fun comment -> ReviewWait.tryDecode comment.Body)
                let parseErrors = parsed |> List.choose (function Error error -> Some error | _ -> None)
                let events = parsed |> List.choose (function Ok (Some event) -> Some event | _ -> None)
                let state =
                    if List.isEmpty parseErrors then
                        ReviewWait.project target.Canonical (Some(string claim.Id)) true DateTimeOffset.UtcNow events
                    else ReviewWait.Invalid parseErrors
                let generationMatches kind round (receipt: ReviewWait.WaitReceipt) =
                    receipt.Kind = kind
                    && receipt.ReviewGeneration = ReviewWait.generationToken draft.HeadSha kind round
                match draft.Kind, state with
                | StructuredDecision.Acceptance, ReviewWait.Completed (_, evidence)
                    when draft.PrecedingReview = Some evidence -> Ok ()
                | StructuredDecision.Acceptance, _ ->
                    Error "host acceptance requires the immediately preceding critic record's durable review wait to be completed"
                | StructuredDecision.Initial, ReviewWait.Waiting receipt
                    when generationMatches ReviewWait.InitialReview 0 receipt -> Ok ()
                | (StructuredDecision.Confirmation | StructuredDecision.Escalation | StructuredDecision.RepairPhase),
                  ReviewWait.Waiting receipt
                    when generationMatches ReviewWait.RepairConfirmation draft.Round receipt -> Ok ()
                | _, ReviewWait.Invalid errors ->
                    let detail = String.concat "; " errors
                    Error($"the review-wait ledger is invalid: %s{detail}")
                | _, ReviewWait.Recoverable (_, reason) -> Error($"the review-wait ledger is recoverable, not authoritative: %s{reason}")
                | _, ReviewWait.Waiting receipt ->
                    Error($"waiting receipt generation/kind does not authorize this review record: %s{receipt.ReviewGeneration} / %A{receipt.Kind}")
                | _, _ -> Error "a matching durable review-wait entry is required before a critic record"

    let private recordReview (ctx: Context) (opts: Options) (rawRef: string) (path: string) : int =
        match parseRef ctx rawRef, opts.Pr with
        | Error message, _ -> eprint $"fsgg-coord-engine: review record: %s{message}"; ExitError
        | _, None -> eprint "fsgg-coord-engine: review record: --pr is required."; ExitError
        | Ok target, Some pr ->
            try
                let raw = File.ReadAllText path
                let mutable waitRefusal = None
                match Driver.decodeStructuredReview raw,
                      Reads.commentsWithIdentity ctx.Transport target.Owner target.Repo pr with
                | Error reason, _ ->
                    eprint $"fsgg-coord-engine: review record: only structured v2 drafts may be written: %s{reason}"
                    ExitError
                | _, Error error -> fail error
                | Ok draft, Ok comments
                    when
                        (authorizeReviewRecordWait ctx opts target pr comments draft
                         |> Result.mapError (fun reason -> waitRefusal <- Some reason)
                         |> Result.isOk) ->
                    let expectedSubject = $"%s{target.Canonical}/pr/%d{pr}"
                    let marked =
                        comments
                        |> List.choose (fun comment ->
                            if comment.Body.StartsWith(StructuredReviewMarker + "\n", StringComparison.Ordinal) then
                                Some(comment, comment.Body.Substring(StructuredReviewMarker.Length).Trim())
                            else None)
                    let decoded = marked |> List.map (fun (comment, payload) -> comment, Driver.decodeStructuredReview payload)
                    let decodeErrors = decoded |> List.choose (fun (_, result) -> match result with Error error -> Some error | Ok _ -> None)
                    if not (List.isEmpty decodeErrors) then
                        let detail = String.concat "; " decodeErrors
                        eprint $"fsgg-coord-engine: review record: existing structured ledger is unreadable: %s{detail}"
                        ExitError
                    else
                        let pairs =
                            decoded
                            |> List.choose (fun (comment, result) ->
                                result |> Result.toOption |> Option.map (fun record -> comment, record))
                        let existing = pairs |> List.map snd
                        let existingValidation =
                            if List.isEmpty existing then Ok []
                            else StructuredDecision.validateReviewLedger expectedSubject existing
                        match existingValidation with
                        | Error errors ->
                            let detail = String.concat "; " errors
                            eprint $"fsgg-coord-engine: review record: existing structured ledger is invalid: %s{detail}"
                            ExitError
                        | Ok _ when draft.Subject <> expectedSubject ->
                            eprint $"fsgg-coord-engine: review record: subject must be '%s{expectedSubject}'."
                            ExitError
                        | Ok _ ->
                            let latestInitialUrl =
                                pairs
                                |> List.rev
                                |> List.tryPick (fun (comment, record) ->
                                    if record.Kind = StructuredDecision.Initial then Some comment.Url else None)
                            let precedingUrl = pairs |> List.tryLast |> Option.map (fun (comment, _) -> comment.Url)
                            let backlinkErrors =
                                match draft.Kind with
                                | StructuredDecision.Initial ->
                                    [ if draft.InitialReview.IsSome then yield "initial review records cannot name initialReview"
                                      if draft.PrecedingReview.IsSome then yield "initial review records cannot name precedingReview" ]
                                | StructuredDecision.Confirmation
                                | StructuredDecision.Escalation
                                | StructuredDecision.RepairPhase
                                | StructuredDecision.Acceptance ->
                                    [ if draft.InitialReview <> latestInitialUrl then
                                          yield "initialReview must equal the actual current generation's initial comment URL"
                                      if draft.PrecedingReview <> precedingUrl then
                                          yield "precedingReview must equal the actual immediately preceding structured comment URL" ]
                            if not (List.isEmpty backlinkErrors) then
                                let detail = String.concat "; " backlinkErrors
                                eprint $"fsgg-coord-engine: review record: %s{detail}"
                                ExitError
                            else
                                let previous = existing |> List.tryLast |> Option.map _.Digest
                                // The acceptance record is the authorization receipt, so its producer —
                                // not a hand-authored draft — binds the two live coordination revisions
                                // that are not already carried by subject/head. Existing non-acceptance
                                // records stay byte-compatible; only the terminal host act gains fields.
                                let liveAcceptanceBinding =
                                    if draft.Kind <> StructuredDecision.Acceptance then
                                        Ok(None, None)
                                    else
                                        match Reads.markerScan ctx.Transport target.Owner target.Repo target.Number
                                              |> Result.bind (Reads.requireCompleteMarkerScan target.Short),
                                              Reads.prHeadSha ctx.Transport target.Owner target.Repo pr,
                                              Reads.prBaseTipSha ctx.Transport target.Owner target.Repo pr with
                                        | Ok markers, Ok liveHead, Ok liveBase ->
                                            match Reads.winner opts.LeaseMinutes markers with
                                            | None -> Error(Errors.Malformed(target.Short, "host acceptance requires a live claim generation"))
                                            | Some held when liveHead <> draft.HeadSha ->
                                                Error(Errors.Malformed(target.Short, $"host acceptance draft names head %s{draft.HeadSha}, but PR #%d{pr} is at %s{liveHead}"))
                                            | Some held -> Ok(Some(string held.Id), Some liveBase)
                                        | Error error, _, _
                                        | _, Error error, _
                                        | _, _, Error error -> Error error

                                match liveAcceptanceBinding with
                                | Error error -> fail error
                                | Ok(claimGeneration, baseSha) ->
                                    let unsigned =
                                        { draft with
                                            Revision = existing.Length + 1
                                            PreviousDigest = previous
                                            ClaimGeneration = claimGeneration
                                            BaseSha = baseSha
                                            Digest = "" }
                                    let candidate = { unsigned with Digest = StructuredDecision.reviewDigest unsigned }
                                    match StructuredDecision.validateReviewLedger expectedSubject (existing @ [ candidate ]) with
                                    | Error errors ->
                                        let detail = String.concat "; " errors
                                        eprint $"fsgg-coord-engine: review record: %s{detail}"
                                        ExitError
                                    | Ok _ ->
                                        let body = StructuredReviewMarker + "\n" + Driver.encodeStructuredReview candidate
                                        let pendingUrl = $"https://github.com/%s{target.Owner}/%s{target.Repo}/pull/%d{pr}#issuecomment-pending"
                                        let pendingId = comments |> List.map _.Id |> List.fold max 0L |> (+) 1L
                                        let projected =
                                            comments
                                            |> List.map (fun comment -> ({ Id = comment.Id; Url = comment.Url; Body = comment.Body }: Driver.ReviewComment))
                                        let effectiveValidation =
                                            if candidate.Kind <> StructuredDecision.Acceptance then Ok None
                                            else
                                                Driver.parseEffectiveReviewComments candidate.HeadSha
                                                    (projected @ [ { Id = pendingId; Url = pendingUrl; Body = body } ])
                                                |> Result.map Some
                                        match effectiveValidation with
                                        | Error errors ->
                                            let detail = String.concat "; " errors
                                            eprint $"fsgg-coord-engine: review record: resulting accepted chain is invalid: %s{detail}"
                                            ExitError
                                        | Ok _ ->
                                            let prTarget = { target with Number = pr }
                                            match Writes.postIssueComment ctx.Transport prTarget body with
                                            | Error error -> fail error
                                            | Ok commentId ->
                                                let commentUrl = $"https://github.com/%s{target.Owner}/%s{target.Repo}/pull/%d{pr}#issuecomment-%d{commentId}"
                                                printfn "%s" (JsonSerializer.Serialize {| schema = "fsgg.coord.review-record-result/v2"; subject = expectedSubject; revision = candidate.Revision; digest = candidate.Digest; commentId = commentId; commentUrl = commentUrl; effectiveChainValidated = (candidate.Kind = StructuredDecision.Acceptance) |})
                                                ExitGreen
                | Ok _, Ok _ ->
                    let detail = waitRefusal |> Option.defaultValue "durable review-wait authorization failed"
                    eprint $"fsgg-coord-engine: review record: refused: %s{detail}"
                    ExitNoVerdict
            with error -> eprint $"fsgg-coord-engine: review record: %s{error.Message}"; ExitError

    let review (ctx: Context) (opts: Options) : int =
        match opts.Args with
        | [ "wait"; rawRef; path ] -> recordReviewWait ctx opts rawRef path
        | [ "record"; rawRef; path ] -> recordReview ctx opts rawRef path
        | _ -> inspectReview ctx opts

    /// THE CHORE OFFER, at whichever of condition 3's safe points the caller is standing on — `AtNext` (the
    /// worker is idle and about to pick up work anyway) or `AfterDone` (the item is stamped and the claim is
    /// already dropped, #533).
    ///
    /// BOTH BOUNDARIES GO THROUGH HERE, and that is the point rather than tidiness. `AfterDone` was a
    /// `Boundary` case Core declared and NOTHING minted, so the offer reached exactly one verb — and
    /// #733's own §4.6 names both. A second spelling of "how do I offer a chore" is the thing that drifts
    /// (#485); the boundary is a parameter because it is the only thing that actually differs.
    ///
    /// SILENT ON EVERY REFUSAL, which is `Chores.offer`'s whole contract — an offer is a COURTESY to a
    /// worker who asked for something else, so nothing here may change the caller's answer or its exit code.
    ///
    /// STDERR, NEVER STDOUT. `next`'s stdout is a machine contract: one line, the chosen item's ref, which a
    /// caller reads with `$(…)`. A chore printed there would be read as an ITEM REF by every script that
    /// already parses this command — the offer would corrupt the answer it is attached to. It is a message
    /// to a human, and it goes where `sayRepoAdvisory` and `printChosen` already put messages to humans.
    ///
    /// It re-parses the snapshot rather than threading the `Request` out of `renderDecision`, because the
    /// alternative changes that function's shape for `batch` and `take` as well — to avoid a pure re-parse
    /// of a document already in hand. It is the SAME `Snapshot.parse` either way, so there is no second
    /// spelling to drift (#485).
    /// THE UNFILTERED BOARD, read for the idleness question and nothing else — the ONLY place `Chore.Whole`
    /// is constructed, so the label is produced by the read that earns it rather than asserted by a caller.
    ///
    /// `Repo = None` is the whole of it: `Scan.scope None` is a pass-through, so the snapshot carries every
    /// repo's rows and a live claim of ours anywhere is visible. `None` on any failure — an unreadable board
    /// cannot report anybody idle (#266), and the offer is a courtesy that may do nothing.
    ///
    /// WHAT IT COSTS, MEASURED RATHER THAN FEARED (#1086, 2026-07-17). The obvious objection is that an
    /// unscoped scan reads the whole org board — 1,192 rows. It does not: `Scan.snapshot` SWEEPS a closed
    /// row without reading it (#520), and 1,156 of those rows are closed. Only the 36 OPEN ones cost the two
    /// REST reads apiece, and 31 of the 36 are in `.github` already — so scoping saves about 22 requests of
    /// 5,000/hr. The scoped read was never buying what its cost estimate claimed, and that estimate was what
    /// made a fail-open guard look like a reasonable trade.
    // ---- the ADR-0050 registry-predicate oracle, resolved off local files (.github#1202/#1203) ---------
    //
    // The IMPURE edge of `RegistryPredicate` (Core, pure): these read `registry/skills.yml` and the OWNING
    // producer's manifest off disk. TWO callers consume them — the `predicate` command (call-site A,
    // filing-time) and the flip-time enrichment below (call-site B) — so they live ONCE, here, ABOVE both,
    // because a module reads top-down and the offer path is above the command (#485). Everything fails
    // CLOSED to `Unreadable`/`Silent` → `Unknown`, so a context without `registry/` (a receiver, ADR-0042)
    // never gets a verdict it could not prove (#266).

    /// Producer skill-manifest locations, in the order the Python reconcile probes them
    /// (`fsgg-skill-registry-check` MANIFEST_CANDIDATES) — the first hit wins. Kept in step by eye; a
    /// producer that moves its manifest changes both.
    let private manifestCandidates =
        [ ".agents/skills/skill-manifest.json"
          "template/skill-manifest/skill-manifest.json" ]

    let private envOr (name: string) (fallback: string) : string =
        match Environment.GetEnvironmentVariable name with
        | null
        | "" -> fallback
        | v -> v

    /// The value of a manifest ENTRY's field, honoring `mirror_of` EXACTLY. `mirrored` is a BOOL, so a
    /// JSON bool renders `true`/`false` and ANY other kind — a STRING `"true"`, a number, an array,
    /// null, or an absent field — is `Silent`: an unparseable verdict is UNKNOWN, never believed
    /// (.github#658; the Python `mirror_of` is `value if isinstance(value, bool) else None`,
    /// `scripts/fsgg-skill-registry-check:333`). Believing a string `"true"` here was a fail-OPEN — a
    /// bad merge or hand-edit would forge a false Agrees/Contradicts, the exact refutation the filing
    /// check auto-comments. Only `mirrored` reaches here today (the Cli gates on `supportsField`); a
    /// future string-typed field must add its OWN typed arm rather than fall through this bool rule.
    ///
    /// The lookup normalises the field FIRST (`supportsField` already accepted `Mirrored`/` mirrored `),
    /// because `TryGetProperty` is case-SENSITIVE against the manifest's canonical lower-kebab key — an
    /// un-normalised `Mirrored` would miss and downgrade a real #1194 refutation to `Unknown`.
    let private manifestFieldValue (entry: JsonElement) (field: string) : RegistryPredicate.OwnerDeclaration =
        let mutable el = Unchecked.defaultof<JsonElement>
        let key = field.Trim().ToLowerInvariant()

        if not (entry.TryGetProperty(key, &el)) then
            RegistryPredicate.Silent
        else
            match el.ValueKind with
            | JsonValueKind.True -> RegistryPredicate.Declares "true"
            | JsonValueKind.False -> RegistryPredicate.Declares "false"
            | _ -> RegistryPredicate.Silent

    /// What the OWNING producer's manifest declares for `(row.Id, field)`, resolved off local producer
    /// checkouts under `reposRoot`. Fails closed to `Unreadable`/`Silent` — never throws — so a missing
    /// checkout, a missing/unparseable manifest, or an absent field all become `Unknown` at the oracle,
    /// never a refutation it could not prove (ADR-0050 / #266).
    let private resolveOwnerDeclaration
        (reposRoot: string)
        (row: RegistryPredicate.Row)
        (field: string)
        : RegistryPredicate.OwnerDeclaration =
        match row.Source with
        | None ->
            RegistryPredicate.Unreadable(
                sprintf "row `%s` declares no `source:`, so its owning producer cannot be located" row.Id
            )
        | Some source ->
            let repoDir = source.Split('/').[0]

            let manifestPath =
                manifestCandidates
                |> List.map (fun c -> Path.Combine(reposRoot, repoDir, c))
                |> List.tryFind File.Exists

            match manifestPath with
            | None ->
                RegistryPredicate.Unreadable(
                    sprintf
                        "no producer manifest for `%s` under `%s` (looked for %s) — check the producer repos out, or the oracle fails closed"
                        repoDir
                        reposRoot
                        (String.concat ", " manifestCandidates)
                )
            | Some path ->
                try
                    use doc = JsonDocument.Parse(File.ReadAllText path)
                    let root = doc.RootElement
                    let mutable skills = Unchecked.defaultof<JsonElement>

                    if
                        root.ValueKind <> JsonValueKind.Object
                        || not (root.TryGetProperty("skills", &skills))
                        || skills.ValueKind <> JsonValueKind.Array
                    then
                        RegistryPredicate.Unreadable(sprintf "manifest `%s` has no `skills` array" path)
                    else
                        let entry =
                            skills.EnumerateArray()
                            |> Seq.tryFind (fun e ->
                                let mutable idEl = Unchecked.defaultof<JsonElement>

                                e.ValueKind = JsonValueKind.Object
                                && e.TryGetProperty("id", &idEl)
                                && idEl.ValueKind = JsonValueKind.String
                                && idEl.GetString() = row.Id)

                        match entry with
                        // The owning manifest does not declare this id at all — SILENT, not a refutation.
                        | None -> RegistryPredicate.Silent
                        | Some e -> manifestFieldValue e field
                with e ->
                    RegistryPredicate.Unreadable(sprintf "manifest `%s` could not be read: %s" path e.Message)

    /// Resolve ONE declared assertion to a verdict against the owning manifest (ADR-0050 call-site B,
    /// .github#1213). This is the Cli half of the split the `predicate` command already runs
    /// (findRow → supportsField → resolveOwnerDeclaration → classify); the two agree because they are the
    /// same calls. `rows` is `registry/skills.yml` parsed ONCE by the caller — a per-item re-parse would
    /// read the file once per blocked item.
    let private resolveAssertion
        (reposRoot: string)
        (rows: RegistryPredicate.Row list)
        (a: RegistryPredicate.Assertion)
        : RegistryPredicate.Verdict =
        let owner =
            match RegistryPredicate.findRow rows a.Id with
            | Some row when RegistryPredicate.supportsField a.Field -> resolveOwnerDeclaration reposRoot row a.Field
            | _ -> RegistryPredicate.Silent

        RegistryPredicate.classify rows owner a

    /// Populate `Item.Predicate` for the BLOCKED items whose body DECLARED a registry predicate — ADR-0050
    /// call-site B firing (.github#1213). `Snapshot.parse` already lifted the pure `DeclaredPredicate`
    /// assertion off each body; this is the impure half — resolving it against the owning manifest — so it
    /// runs here at the offer path's edge, never in the pure parser.
    ///
    /// SCOPED, so the manifest reads are bounded to the handful of registry items that could ever be gated:
    /// only `Blocked` items with a `DeclaredPredicate` are resolved — the gate reads the field only in
    /// `BLOCKER-CLEARED`, and everything else keeps `None`. `registry/` absent ⇒ `parseRows ""` = `[]` ⇒
    /// every declared predicate resolves to `Unknown` and NO manifest is read: receiver-safe and cheap by
    /// construction (ADR-0042), and the gate then HOLDS the item, which is the fail-closed answer (#266).
    let private enrichPredicates (request: Snapshot.Request) : Snapshot.Request =
        let needsResolving (c: Snapshot.Candidate) =
            c.Item.Status = Blocked && c.DeclaredPredicate.IsSome

        if not (request.Candidates |> List.exists needsResolving) then
            request
        else
            let registryPath = envOr "FSGG_REGISTRY" "registry/skills.yml"
            let reposRoot = envOr "FSGG_REPOS_ROOT" ".repos"

            let rows =
                if File.Exists registryPath then
                    RegistryPredicate.parseRows(File.ReadAllText registryPath)
                else
                    []

            let enrich (c: Snapshot.Candidate) : Snapshot.Candidate =
                match c.DeclaredPredicate with
                | Some a when c.Item.Status = Blocked ->
                    { c with
                        Item =
                            { c.Item with
                                Predicate = Some(resolveAssertion reposRoot rows a) } }
                | _ -> c

            { request with
                Candidates = request.Candidates |> List.map enrich }

    /// A parsed snapshot's rows as a `Chore.Whole` — the ONE construction of that case, so the label is
    /// never spelled twice (#485) and never asserted by a caller who only believes the board is whole. The
    /// argument is a `Request` the caller must have parsed from an UNFILTERED read; that obligation is the
    /// reason both call sites below funnel through here rather than building `Chore.Whole` themselves.
    let private wholeOf (request: Snapshot.Request) : Chore.Board =
        Chore.Whole(request.Candidates |> List.map (fun c -> c.Item))

    /// Explicit bootstrap policy for the single lifecycle reducer. Mutable Project Status is deliberately
    /// absent: callers at next/claim/reconcile cannot silently turn a stale column back into intent.
    let private lifecyclePolicyIntent observedAt (item: Item) =
        match item.HumanBlock, item.Class, item.TouchSet with
        | Some human, _, _ ->
            LifecycleProjection.HumanPark(
                human,
                { Revision = observedAt; Reason = "explicit human scheduling hold" })
        | None, Some Decision, _ ->
            LifecycleProjection.HumanPark(
                AwaitingHumanDecision,
                { Revision = observedAt; Reason = "decision-class work requires a human decision" })
        | None, _, (Undeclared | DeclaredNone) ->
            LifecycleProjection.Backlog
                { Revision = observedAt; Reason = "touch-set policy is not schedulable" }
        | None, _, (DeclaredChore | Declared _) -> LifecycleProjection.Auto
        | None, _, Unreadable reason ->
            LifecycleProjection.Deferred($"touch-set unreadable: %s{reason}", None, observedAt)

    let private lifecycleObservation observedAt (item: Item) delivery =
        let fact value : LifecycleProjection.Fact<_> = { ObservedAt = observedAt; Value = value }
        let pullRequest =
            item.ItemPr
            |> Option.map (fun number ->
                ({ Number = number; Open = true; ReviewOrCiActive = true }: LifecycleProjection.PullRequest))
        ({ Claim = fact item.Claim
           PullRequest = fact pullRequest
           Blockers = fact item.Blockers
           Delivery = fact delivery
           Issue = fact item.State }: LifecycleProjection.Observation)

    let private lifecycleSelection
        observedAt
        (item: Item)
        (delivery: LifecycleProjection.Delivery)
        (watermark: LifecycleProjection.Watermark option) =
        let intent = watermark |> Option.map _.Intent |> Option.defaultValue (lifecyclePolicyIntent observedAt item)
        intent,
        // `item.Kind` — THE ITEM'S OWN `Kind:` LINE, RE-READ ON THIS PASS (.github#2712), and pointedly
        // NOT anything the watermark carries. The line above is the freeze: a watermark's mere existence
        // makes `lifecyclePolicyIntent` unreachable, so any exemption expressed as an intent would be
        // frozen by whatever receipt the row already has — which, since .github#2690, is every `add`-filed
        // row. The kind is a separate argument sourced from the body, so no receipt in this system can
        // suppress it. `Kind.govern` owns the `None`-means-`Work` reading, so a row declaring no kind
        // reaches an unchanged reducer with an unchanged answer.
        LifecycleProjection.advance
            LifecycleProjection.IntentStatusV1
            (Kind.govern item.Kind)
            intent
            watermark
            (lifecycleObservation observedAt item delivery)

    /// The live `Blocked by` edges for ONE item — `Scan.blockersOf`'s per-board question asked at a write
    /// boundary instead, exactly as `requireCoherentBlockedWrite` below asks the emptiness half of it.
    ///
    /// THE BOARD FIELD IS THE SOURCE, NOT THE BODY'S `Blocked by:` LINE. ADR-0045 makes the COLUMN the
    /// typed dependency edge and `Scan` resolves precisely that (`row.BlockedByRaw`), so reading the body
    /// here would be a second, disagreeing spelling of one edge — `.github#2079`'s divergence is a finding
    /// `lint` REPORTS (`blockedByBodyDivergence` above), never a licence to prefer the other side of it.
    ///
    /// IT FAILS CLOSED IN BOTH DIRECTIONS, on `Scan.resolveBlocker`'s own terms: a token that is not a ref
    /// is `BlockerUnparseable`, a lookup that could not be made is `BlockerUnknown` (`Reads.blockerState`'s
    /// documented answer), and `Blockers.isResolved` clears NEITHER — "I could not look" is not "I looked
    /// and it is fine" (#266). A transport failure that is not a per-ref verdict propagates, so the caller
    /// can withhold rather than project a column over a board it only half read.
    ///
    /// THE TOKEN GRAMMAR IS ASKED FOR, NOT RESTATED — `Blockers.canonicalizeBlockedBy`, PER TOKEN. It is
    /// the Core's one definition of what a `Blocked by` token is, and it accepts exactly what
    /// `Scan.parseBlockerRef` accepts (a URL, `owner/repo#n`, `repo#n`, `#n`) — so this reader and the
    /// board scan cannot drift about which edges hold. `parseRefIn` alone would have been the drift: it
    /// also accepts a BARE `123`, which `Scan` calls `BlockerUnparseable` and therefore BLOCKS on, and a
    /// reader that resolved a token the scan refuses is a fail-OPEN dressed as tolerance. Both refusal
    /// shapes (`Placeholder`, `NotIssueRefs`) land on `BlockerUnparseable`, which is Scan's answer too.
    ///
    /// ZERO REST IS THE COMMON CASE: an item with no `Blocked by` value spends one resolver read (against
    /// a board the caller has already bootstrapped) and no REST at all.
    let private liveBlockers (ctx: Context) (board: Board.BoardMap) (ref: Ref) : Errors.IoResult<Blocker list> =
        let unparseable token = { Ref = None; Raw = token; State = BlockerUnparseable }

        let resolve (token: string) : Errors.IoResult<Blocker> =
            match Blockers.canonicalizeBlockedBy ref.Owner ref.Repo token with
            | Error _
            | Ok None -> Ok(unparseable token)
            | Ok(Some canonical) ->
                match parseRefIn ref.Owner (Some ref.Repo) canonical with
                // Unreachable: `canonicalizeBlockedBy` emits `owner/repo#n`, which `RefParsing.parse`
                // accepts by construction. Fail closed anyway rather than assert it away (#266).
                | Error _ -> Ok(unparseable token)
                | Ok target ->
                    Reads.blockerState ctx.Transport target.Owner target.Repo target.Number
                    |> Result.map (fun state -> { Ref = Some target; Raw = target.Short; State = state })

        match Board.itemBlockedBy ctx.Transport board ref.Owner ref.Repo ref.Number with
        | Error e -> Error e
        | Ok None -> Ok []
        | Ok(Some raw) when String.IsNullOrWhiteSpace raw -> Ok []
        | Ok(Some raw) ->
            raw.Split ','
            |> Array.map (fun token -> token.Trim())
            |> Array.filter (fun token -> token <> "")
            |> Array.fold
                (fun acc token ->
                    match acc with
                    | Error e -> Error e
                    | Ok blockers -> resolve token |> Result.map (fun blocker -> blockers @ [ blocker ]))
                (Ok [])

    /// A successful lock is an observed lifecycle fact, not permission for the claim command to invent a
    /// column value. Route it through the same reducer as next/reconcile and fail closed if that reducer can
    /// no longer establish a destination.
    ///
    /// **.github#2645 — ROUTING THROUGH THE REDUCER WAS DONE; NOT INVENTING THE VALUE WAS NOT.** The
    /// invention had simply moved one level down, from the column to the reducer's INPUTS:
    ///
    /// ```
    /// PullRequest = fact None            // "this item has no PR"
    /// Blockers    = fact []              // "nothing is blocking it"
    /// Delivery    = fact { false; false }
    /// Issue       = fact Open
    /// ```
    ///
    /// `PullRequest = fact None` is the one that bit. It asserts *"there is no PR"* to a reducer whose whole
    /// job is deciding a column from exactly that kind of fact, so the reducer correctly answered
    /// `In progress` — it had been told the review does not exist. A worker holding an item at `In review`
    /// who renewed its own lock with a bare `claim` therefore had the column silently reverted to
    /// `In progress`, and `converged` (which required exactly `In progress`) reported that overwrite as
    /// SUCCESS. Measured live on `.github#2546`. A reducer fed a fiction is not a projection of live state.
    ///
    /// So every fact is now READ, from the same places `lifecycleObservation` above reads them for
    /// `next`/`reconcile`, and an unreadable one WITHHOLDS the write (`Error`) instead of being defaulted —
    /// which is what the comment this function has always carried already instructed.
    ///
    /// **WHY `Delivery.Outstanding` IS THE ONE FACT STILL PASSED AS `false`, AND WHY THAT IS A PROOF RATHER
    /// THAN THE OLD FABRICATION.** `lifecycleOfferChores` passes the same constant for the same reason.
    /// `Outstanding` reaches `project`'s cascade only BELOW the closed-issue arms and ABOVE the PR arm, and
    /// its own producer (`reconcile`'s `outstandingObligations`) is reached only through `item.ItemPr` —
    /// which is `Reads.prAlive`'s OPEN-PR answer, the identical read below. So on every path where
    /// `Outstanding` could be `true` here, an open PR exists, and the PR arm two lines later already
    /// projects `In review`: the read cannot change this function's answer, and buying it would spend a
    /// `prHeadSha` plus a whole PR comment thread per claim to re-derive a column already decided. That is
    /// a reachability proof about THIS caller, not a default about the world — which is exactly the
    /// distinction `PullRequest = fact None` failed to make.
    ///
    /// **THE `intent` ARGUMENT IS NOT AN OBSERVATION AND IS DELIBERATELY NOT DERIVED HERE.** It is the
    /// human/policy scheduling input, whose one authority is the persisted watermark (reconcile's own
    /// attributable `IntentRecord`) with `lifecyclePolicyIntent` as the fallback for callers holding a
    /// scanned `Item`. `claim` holds no `Item` and no title, and re-deriving policy intent from a body
    /// alone would be a SECOND, WEAKER spelling of that rule — `Class.fromBody` cannot see the `[decision]`
    /// TITLE prefix `Class.derive` can — i.e. the rule-spelled-twice failure this codebase measures
    /// everywhere else. So the watermark's intent is used when the row has one, and `Auto` (the value this
    /// function already used) otherwise; parking a row whose only evidence is its title stays reconcile's.
    ///
    /// `advance`, not `reduce`: the persisted watermark is read here anyway (it rides in the same comment
    /// thread as the done receipt), and honouring it is what stops a claim landing a column that an OLDER
    /// observation would re-derive — the ordering guarantee `next`/`reconcile` already get for free.
    let private claimLifecycleDestination
        (ctx: Context)
        (board: Board.BoardMap)
        observedAt
        (ref: Ref)
        (held: Writes.Held)
        : Result<BoardStatus, string> =

        let claim : Claim =
            { Worker = held.Worker
              Session = held.Session
              AgeSeconds = 0
              PreviousStatus = held.PreviousStatus }
        let fact value : LifecycleProjection.Fact<_> = { ObservedAt = observedAt; Value = value }

        // The item's OWN open `item/<n>-*` PR, constructed exactly as `lifecycleObservation` constructs it
        // from `item.ItemPr`. `LivenessUnknown` and a propagated transport/rate-limit error are the two
        // shapes that must NEVER collapse to "no PR" (Reads.prAlive's own contract, and .github#1924's
        // open fail-open at Scan's call site): here they withhold.
        let pullRequest =
            match Reads.prAlive ctx.Transport ref.Owner ref.Repo ref.Number with
            | Ok(LeaseExpiredPrOpen pr) ->
                Ok(Some({ Number = pr; Open = true; ReviewOrCiActive = true }: LifecycleProjection.PullRequest))
            // No open PR: `LeaseExpiredNoPr` and `LeaseExpiredBranchPushed` are both DEFINITE negatives
            // about a PULL REQUEST (a pushed branch is proof of life, but it is not a PR, and `ItemPr`
            // carries the same reading at Scan.fs's own probe).
            | Ok LeaseExpiredNoPr
            | Ok LeaseExpiredBranchPushed -> Ok None
            | Ok LivenessUnknown -> Error "the item's open-PR probe could not be completed"
            // UNREACHABLE — `Reads.prAlive` answers about a PR, never about a lease, so it cannot return
            // `LeaseHeld`. An answer we did not expect is a read we could not make (#266), never a
            // confident "no PR": the impossible case fails CLOSED rather than being folded into the
            // negative arm above, where a future change to `prAlive`'s vocabulary would land silently.
            | Ok LeaseHeld ->
                Error "the item's open-PR probe answered `LeaseHeld`, which is not an answer about a pull request"
            | Error e -> Error $"the item's open-PR probe failed: %s{Errors.explain e}"

        let issue =
            match Reads.issueState ctx.Transport ref.Owner ref.Repo ref.Number with
            | Ok state -> Ok state
            | Error e -> Error $"the issue's OPEN/CLOSED state could not be read: %s{Errors.explain e}"

        // ONE read, TWO facts — the done receipt and the projection watermark are both ISSUE comments, and
        // `reconcile` reads them off this same thread for the same pair of uses.
        let comments =
            match Reads.commentBodies ctx.Transport ref.Owner ref.Repo ref.Number with
            | Ok bodies -> Ok bodies
            | Error e -> Error $"the item's comment thread could not be read: %s{Errors.explain e}"

        let blockers =
            match liveBlockers ctx board ref with
            | Ok values -> Ok values
            | Error e -> Error $"the item's `Blocked by` edges could not be resolved: %s{Errors.explain e}"

        // A FIFTH READ, ADDED DELIBERATELY (.github#2712), for the ONE fact this function could not
        // otherwise have: whether the row it is about to project a column onto is a unit of work at all.
        //
        // THE KIND CANNOT COME FROM ANYWHERE ELSE HERE. It is a `Kind:` BODY line — the sole authority,
        // because a lagging or hand-edited board column must not be able to decide this — and this
        // function holds no `Item` and no scanned row. Taking it from the persisted watermark instead is
        // exactly the mistake `advance`'s own comment refutes: a receipt cannot carry an exemption without
        // freezing it.
        //
        // AND THE COST IS PAID ONCE PER CLAIM, NOT PER ROW SCANNED. `take` is the dearest verb on this
        // board — measured at ~190 REST requests for one run on `FS-GG/.github` — and it reaches here
        // exactly once, for the single item it claims, so this is one request against that. Contrast the
        // collision scan a few hundred lines up, which `take` skips precisely because it would be paid per
        // candidate. The alternative to paying it is writing a `Status` column onto a register, which is
        // the defect this row exists to remove.
        //
        // FAIL-CLOSED, on this function's own stated terms: an unreadable body is not an absent `Kind:`
        // line, so it refuses rather than defaulting to `Work` and projecting.
        let kind =
            match Reads.issueBody ctx.Transport ref.Owner ref.Repo ref.Number with
            | Ok body -> Ok(Kind.govern (Kind.fromBody body))
            | Error e -> Error $"the item's body could not be read, so its `Kind:` is UNKNOWN — not absent: %s{Errors.explain e}"

        // ALL FOUR READS ARE MADE, then judged — deliberately, not as an oversight. Short-circuiting on the
        // first failure would make this path's cost depend on which read failed, and a claim whose spend
        // varies with the weather is one no budget assertion can pin (`SchedulingCostTests`' standard, and
        // the read-cost leg in `ClaimLifecycleDestinationTests`). The reads are independent and none of
        // them writes, so making all four and reporting the first refusal costs at most three reads on the
        // rare failing path and keeps the common one exactly measurable.
        match pullRequest, issue, comments, blockers, kind with
        | Error reason, _, _, _, _
        | _, Error reason, _, _, _
        | _, _, Error reason, _, _
        | _, _, _, Error reason, _
        | _, _, _, _, Error reason -> Error reason
        | Ok pullRequest, Ok issue, Ok comments, Ok blockers, Ok kind ->
            let watermark = LifecycleProjection.tryWatermark comments

            let observation : LifecycleProjection.Observation =
                { Claim = fact (Some(claim, LeaseHeld))
                  PullRequest = fact pullRequest
                  Blockers = fact blockers
                  Delivery =
                    fact ({ Outstanding = false; DoneStamped = Done.hasReceipt comments }: LifecycleProjection.Delivery)
                  Issue = fact issue }

            let intent =
                watermark |> Option.map _.Intent |> Option.defaultValue LifecycleProjection.Auto

            match LifecycleProjection.advance LifecycleProjection.IntentStatusV1 kind intent watermark observation with
            | LifecycleProjection.Project(destination, _) -> Ok destination
            // NOTHING IS WRITTEN, and the caller's existing `Error reason` arm is exactly the right
            // vocabulary for it: it already means "a fact said do not write this column", and it already
            // reports rather than swallows. A standing row claimed by hand still gets its claim marker and
            // its lease; what it does not get is a lifecycle column it has no lifecycle to justify.
            | LifecycleProjection.Exempt kind ->
                Error
                    $"%s{ref.Short} is a %s{itemKindWireName kind} (`Kind: %s{itemKindWireName kind}`), not a unit of work, so it has no lifecycle Status to project (.github#2712)"
            | LifecycleProjection.Withheld reason -> Error $"the lifecycle projection was withheld: %s{reason}"

    /// Next/AfterDone consume the same reducer as reconcile. Only its verified destination is admitted to
    /// the chore queue; Chore.derive remains unable to manufacture a Status repair.
    let private lifecycleOfferChores (ctx: Context) (observed: Chore.Board) =
        let items = match observed with | Chore.Whole values | Chore.Filtered values -> values
        items
        |> List.choose (fun item ->
            if item.Status = Done then None
            else
                match Reads.commentBodies ctx.Transport item.Ref.Owner item.Ref.Repo item.Ref.Number with
                | Error _ -> None
                | Ok comments ->
                    let observedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    let delivery : LifecycleProjection.Delivery =
                        { Outstanding = false; DoneStamped = Done.hasReceipt comments }
                    let _, selected =
                        lifecycleSelection observedAt item delivery (LifecycleProjection.tryWatermark comments)
                    match selected with
                    | LifecycleProjection.Project(destination, _) -> Chore.lifecycleProjection item destination
                    // NO CHORE for a standing row — no park, no promote, no `Done` (.github#2712 AC2).
                    // Distinguished from `Withheld` even though both answer `None` here, because they are
                    // different facts and this file is where the difference would rot if it were folded.
                    | LifecycleProjection.Exempt _ -> None
                    | LifecycleProjection.Withheld _ -> None)

    /// THE OFFER PATH'S BOARD — the scan's bytes AND the scan's rows, joined the way `reconcile` joins them
    /// (.github#1649).
    ///
    /// **THE DEFECT THIS FIXES.** This composition was `Snapshot.parse doc |> Option.map (enrichPredicates >>
    /// wholeOf)`, and the `rows` the scan had already paid for were DISCARDED at the match. `Snapshot.parse`
    /// says what that costs, in the field's own comment: `BoardClass = None` means *"this parser did not
    /// look"*, not *"the column is unset"* — the board's `Class` column is a SCAN fact and the pure document
    /// structurally cannot carry it. So every item reaching `Chore.derive` through the OFFER path carried
    /// `BoardClass = None`, and `CLASS-PROJECTION-LAG`'s guard (`board <> Some declared`) was therefore
    /// UNCONDITIONALLY TRUE for every open item whose body declares a class. The offer named a disagreement
    /// it had never read either side of.
    ///
    /// **WHY THE CHEAPER EXPLANATIONS WERE ALL REFUTED, AND WHY THIS ONE ISN'T.** #1649 accumulated eight
    /// offers across three repos and three measurements that each rule out a suspect. Every one of them is
    /// this defect and only this defect:
    ///
    /// - *"It is the 90s scan cache."* Refuted by measurement, and correctly: the scan reaching here is
    ///   fresh. A fresh `ready` contradicting the offer at the same instant is the EXPECTED reading, because
    ///   `ready` renders `Scan.Row.BoardClass` — the value this function was discarding.
    /// - *"The caller reuses an old snapshot; make `AtNext` re-read."* Refuted: the offers arrived from
    ///   `AfterDone`, which buys a dedicated fresh scan for exactly this purpose. The staleness was never in
    ///   the read. `offerChoreAfterDone` paid for the scan and this join threw away the half it paid for.
    /// - *"It is a duplicate offer; dedupe it, or hold the lock harder."* Refuted: one measured offer's
    ///   predicate was independently false, not duplicated. With `BoardClass` pinned at `None` an item whose
    ///   body and column AGREE still derives the chore, so no dedupe or lock fix could have suppressed it.
    ///
    /// Two further observations fall out of the same line. It never RETIRED across repeated `done --flip`s
    /// because `Chore.isRetired` re-derives, and a re-derivation over an unjoined board answers "still owed"
    /// against a write that landed; and because `CLASS-PROJECTION-LAG` is `rank` 5, LAST, a repo whose queue
    /// is otherwise drained hands out the same lowest-`Id` item again and again — the observed shape exactly.
    /// Finally `reconcile` proposed NOTHING for the same item at the same moment: the two paths run the SAME
    /// `Chore.derive` over DIFFERENT joins, `reconcile` having always run `enrichBoardFacts` and this never.
    /// That divergence was the defect, visible from outside as the engine contradicting itself.
    ///
    /// It is the same `enrichBoardFacts` in the same order `reconcile` uses — `enrichPredicates` first, then
    /// the board join — so there is now ONE reading of a board behind the chore the queue OFFERS and the
    /// chore `reconcile --apply` WRITES, and they cannot drift apart again.
    ///
    /// `Phase` and `AgeDays` ride along and derive nothing here; they are `Rank`'s inputs and the offer sorts
    /// by `Chore.rank`. Joining them costs nothing and keeps this the SAME function `reconcile` calls rather
    /// than a second, narrower one that could disagree — which is the whole failure being repaired.
    ///
    /// NOT PRIVATE, deliberately, on `badTouchSetDetail`'s terms: this join IS the correctness argument, and
    /// a defect whose entire content was "one call site skipped it" must be assertable by a test rather than
    /// re-argued in a comment. `ChoresTests` drives it from the REAL `Scan.snapshot` bytes.
    let offerBoardOf (rows: Scan.Row list) (doc: string) : Chore.Board option =
        // Resolve the flip-time predicate before building the board (ADR-0050 call-site B, .github#1213):
        // `Snapshot.parse` lifted the declared assertion off each body, `enrichPredicates` resolves it to
        // `Item.Predicate` against the owning manifest, and the gate in `Chore.derive` reads that. A parse
        // failure keeps the board `None`, exactly as before.
        Snapshot.parse doc
        |> Result.toOption
        |> Option.map (enrichPredicates >> enrichBoardFacts rows >> wholeOf)

    /// THE OFFER PATH'S BOARD READ — the ONE door to `offerBoardOf`, and the place its FRESHNESS is decided.
    ///
    /// **THE DEFECT THIS FIXES** (.github#1679). This read `Cache.Scheduling`, so for up to ninety seconds
    /// after any `Class` column write the offer derived `CLASS-PROJECTION-LAG` against the column as it was
    /// BEFORE the write. That is not #1649: #1649 was a JOIN — the rows were discarded, so `BoardClass` was
    /// pinned at `None` and the guard was unconditionally true — and its fix is present and working. This is
    /// a CLOCK. The offer was not wrong about the board; it was right about a ninety-second-old board, which
    /// is indistinguishable from wrong to the worker who acts on it.
    ///
    /// **AND `Chore.isRetired` CANNOT RESCUE IT**, which is what makes this more than a stale read.
    /// Retirement works by RE-DERIVING, and a re-derivation over the same cached scan answers "still owed"
    /// against a write that has already landed. So the offer survives the very write that discharges it,
    /// for the whole TTL, and re-arrives at the next boundary inside the window.
    ///
    /// **FOUR MEASUREMENTS SEPARATE THIS CAUSE FROM #1649's**, and every one of them is a fact about the
    /// clock rather than the join: `reconcile` proposed no `CLASS-PROJECTION-LAG` at the same instant (same
    /// `Chore.derive`, same `enrichBoardFacts`, opposite answer); a cold `FSGG_COORD_CACHE` made the offer
    /// vanish; the warm run stopped offering once the TTL elapsed with NO board write in between; and under
    /// #1649's defect the offer was independent of cache age — which is exactly why that investigation
    /// refuted the cache, correctly, for the offers it measured.
    ///
    /// **WHY `Cache.Offering` AND NOT MERELY DELETING THE CACHE FROM THIS PATH'S REACH.** The cache is right
    /// and #418 is why: the fleet shares 5,000 GraphQL pt/hr and five workers looping `take` drained it in
    /// fifteen minutes. `Scheduling` is untouched — the scheduling poll still serves N workers from one
    /// scan. The offer is not in that loop (gated on a chore lock existing at all; once per `done --flip`;
    /// on `next` only after `take` found nothing), the REST half of this read was never cached and is paid
    /// either way, and a fresh scan WRITES the cache — so this path becomes a producer of the shared scan
    /// rather than a consumer of it. `Cache.fsi` carries the full argument and names this consumer.
    ///
    /// NOT PRIVATE, deliberately, on `offerBoardOf`'s terms exactly: the freshness IS the correctness
    /// argument, and a defect whose entire content was "one call site named the wrong intent" must be
    /// assertable by a test rather than re-argued in a comment. `ChoresTests` drives THIS function over a
    /// warm cache, so a mutation back to `Scheduling` reds a leg instead of passing one.
    let wholeBoard (ctx: Context) (opts: Options) : Chore.Board option =
        match scanAndDecide ctx { opts with Repo = None } Cache.Offering with
        | Error _ -> None
        // The scan ROWS, no longer discarded — see `offerBoardOf` for what discarding them cost. The shape of
        // the bug is worth keeping in view AT the call site: the `_` that used to sit here was not an
        // omission a compiler could ever have flagged, it was a board fact silently defaulted to "did not
        // look" and then compared as though it had been read.
        | Ok(rows, doc, _) -> offerBoardOf rows doc

    let private offerChoreAt (ctx: Context) (opts: Options) (boundary: Chore.Boundary) (repo: string) (observed: Chore.Board) : unit =
        // No worker id resolves ⇒ no lock is possible ⇒ no offer. `next` itself needs no worker, and that
        // asymmetry is deliberate: the ANSWER does not touch a lock, the OFFER is nothing but one. Note this
        // uses `Identity.resolve` directly rather than `worker`, which PRINTS a #419 warning and would make
        // an idle `next` scold every worker on a shared session for a lock it is not going to take.
        match Identity.resolve opts.Worker with
        | Error _ -> ()
        | Ok w ->
            let session = w.Session |> Option.map SessionId

            // The board reaches `offer` as a `Chore.Board`, so what it is scoped to travels WITH it and the
            // idleness question can refuse a board that cannot answer (#1086). `offer` derives `ours` from
            // its rows: the candidates are unfiltered by COLUMN — `Scan.snapshot` drops only PRs and
            // out-of-scope repos — which is what makes the rules reachable at all. `BLOCKER-CLEARED` needs a
            // `Blocked` row and `CLOSED-ISSUE-NOT-DONE` a closed one, and bash filtered on
            // `Status ∈ {Ready, Backlog}` before the engine was asked, so under it neither could ever fire.
            let lifecycle = lifecycleOfferChores ctx observed
            Chores.offerWithLifecycle lifecycle ctx.Transport boundary (WorkerId w.Id) (selfOf w) session ctx.ChoreLocks ctx.Owner repo observed
            |> Option.iter (fun (chore, lockRef) -> eprint (Chores.render chore lockRef))

    /// `next`'s call site. The repo the offer is FOR: `--repo` when given, else the checkout we are standing
    /// in — the same default `parseRef` uses for a bare ref. `choreLockRef` canonicalises it (`Governance` →
    /// `FS.GG.Governance`) and answers `None` for anything it does not know, so a typo cannot lock the wrong
    /// repo: it offers nothing, which is #979's lesson applied where a WRITE is at stake.
    ///
    /// THIS IS A WRITE, AND IT IS NOW DECLARED ONE — .github#1535, DECIDED. `next` was documented, used and
    /// tested as a READ (`/pnext-item` §1 and the exit-code table both name it as the diagnostic to run when
    /// nothing is schedulable), and after printing that answer it reaches `Chores.offer` → `Writes.claim`,
    /// POSTing a claim marker that takes the repo's chore lock. #1535 put two shapes on the record: DECLARE
    /// the write and keep the conscription point, or MOVE the offer to a boundary that already writes so
    /// `next` becomes a genuine read again. The first was chosen, on two facts about this code rather than a
    /// preference:
    ///
    /// 1. `AtNext` IS THE ONLY BOUNDARY THAT REACHES A DRAINED BOARD. `AfterDone` fires from `done`, which
    ///    requires an item somebody claimed and finished. On a board where every row sits `Blocked` behind
    ///    blockers that have all resolved, `take` answers EX_NONE for everyone, nobody works, nobody stamps,
    ///    and no `done` ever runs — so the ONLY caller left is a `next`. That is the deadlock .github#1047
    ///    records verbatim: #733 sat `Blocked` on a condition only #733 could clear, invisible to every
    ///    `take`, until a human reconciled it by hand. `BLOCKER-CLEARED` is the rule written to end it, and
    ///    moving the offer off `next` would delete the one boundary that can ever fire it on a stalled board
    ///    — spending the mechanism to preserve a sentence in a doc.
    ///
    /// 2. THE REFUSAL #1528 BUYS COSTS A SPELLING, NOT A DIAGNOSTIC. #1528 accepted refusing `next` on a
    ///    stale engine while recording that it "costs the fleet its diagnostic verb whenever anyone is
    ///    mid-edit on `src/`". It does not. `batch` is this same decision uncapped, makes NO offer, and sits
    ///    in the shim's `BOARD_READS`. `batch --text -n 1` is `next` minus the write, and prints the
    ///    identical answer: "nothing schedulable right now." in the one `nothingSchedulable` spelling above,
    ///    and the per-item passed-over reasons and #428's starved banner out of the one `sayPassedOver`, so
    ///    the two cannot drift. They differ in ONE thing and .github#1562 is why: `batch --text` puts that
    ///    headline on STDOUT, `next` on STDERR, because `next`'s stdout is a bare ref read with `$(…)` and
    ///    `batch --text`'s is prose for a human. Same words, same streams for every other line — so a leg
    ///    that captures them MERGED sees one output, and one that captures them apart sees the contract.
    ///    `--text` belongs in that spelling — `batch` defaults to `Json` (`renderSupport`, `Both Json`), and
    ///    the JSON arm prints `[]` with the reasons on stderr through `sayWhyNothing`, which is the same
    ///    information and NOT the same sentence. `tests/coord-engine-e2e/writes.sh` pins BOTH halves against
    ///    one board and one chore: `batch` leaves `.github#1033`'s comment thread empty, `next` posts a
    ///    marker naming its worker to it.
    ///
    /// THE THIRD SHAPE — gate the offer so that a `next` which is not going to lock is PROVABLY a read — is
    /// unreachable at the only level that could act on it. The shim classifies by VERB and refuses to be
    /// argument-aware (its own recorded reasoning), and `next`'s write is gated on BOARD STATE rather than on
    /// argv at all: is there a lock, is the caller idle, is there a chore. A flag would not change what the
    /// shim can see. A DEDICATED VERB would work mechanically and would destroy the mechanism: a conscription
    /// somebody has to remember to invoke is #570's rule-enforced-by-whoever-remembers, which is the decay
    /// this queue exists to stop.
    ///
    /// So `next` writes, and every place a caller MEETS it says so rather than only this file: the engine's
    /// own `--help`, the emitted `command-contract` (`writes: conditional`, .github#1534), the shim's
    /// `BOARD_WRITES_CONDITIONAL` — where it stays, and #1528's refusal is simply correct — and a note
    /// BESIDE `/pnext-item`'s exit-code table, which is the row that sends an idling worker to `next`.
    ///
    /// THE NOTE SITS BESIDE THAT TABLE RATHER THAN IN IT, DELIBERATELY. The table is a GENERATED region
    /// emitted from `Protocol.fs` by `scripts/generate-projections`, and `Protocol.fs` is the engine's WIRE
    /// SURFACE: `check-engine-freshness.py` reds main whenever it has drifted unreleased, so editing that
    /// cell would have turned an already-owed engine release into a red gate for a documentation fix. The
    /// fact belongs to the verb, not to an exit code, so it is stated in hand-authored prose under the same
    /// heading — where the reader who just read that row is standing — and the generated cell stays the
    /// engine's own answer.
    ///
    /// WHICH BOARD THE IDLENESS QUESTION GETS. Idleness is a fact about the whole board, so `wholeBoard`
    /// reads it UNFILTERED regardless of `--repo` (#1086). With no `--repo` that is the same board `next`
    /// just decided from, so this re-reads it — a second scan on the DIAGNOSTIC command (`/pnext-item` calls
    /// `next` only when `take` found nothing), and only once a lock is known to exist.
    ///
    /// AND SINCE .github#1679 THAT SECOND SCAN IS REAL, where it used to be a cache hit off the read `next`
    /// had made milliseconds earlier. That is the cost of the fix, stated here rather than left for someone
    /// to discover in a budget: the re-read bought a type-level guarantee (#1086) and now buys freshness
    /// too, which is what it was always documented as being. It lands on the verb that fires only on a board
    /// with no work — and a fresh scan REFRESHES the shared cache, so the points come back to the next
    /// `take`.
    ///
    /// IT DOES NOT REUSE `next`'s `doc`, deliberately, and that is the #1086 fix keeping its own rule. The
    /// reuse would build a `Chore.Whole` from a board proven whole only by "`next` with no `--repo` scans
    /// unscoped" — caller reasoning, exactly the forgeable label the type exists to abolish. If `next` ever
    /// default-scoped, that reuse would silently relabel a slice `Whole` and the fail-open returns with no
    /// compile error. `wholeBoard` is the only door, so the re-read buys the guarantee back.
    let private offerChoreAtNext (ctx: Context) (opts: Options) : unit =
        let repo =
            opts.Repo |> Option.orElse ctx.DefaultRepo |> Option.defaultValue ""

        // The free question first, exactly as `Chores.offer` does it and for the same reason: a repo with no
        // chore lock must not buy a board read to hear so.
        //
        // THE RULE IS UNCHANGED AND THE ARITHMETIC UNDER IT IS NOT. This read "six of the org's seven repos
        // have no chore lock" until #1087 gave all seven one, at which point the sentence was simply false
        // and the saving it claimed no longer existed in the common case. What is saved now is the read on a
        // repo the map does NOT know — an unrostered one (`FS.GG.Legacy`, which the e2e fixture drives as
        // exactly this control). Cheapest question first is right either way; the count was the part that
        // rotted, and a stale count is how a correct rule acquires a false justification.
        match Options.choreLockRef ctx.ChoreLocks ctx.Owner repo with
        | None -> ()
        | Some _ -> wholeBoard ctx opts |> Option.iter (offerChoreAt ctx opts Chore.AtNext repo)

    /// `done --flip`'s call site — condition 3's OTHER safe point, and the one a working fleet reaches.
    ///
    /// IT PAYS A BOARD READ THAT `next` DOES NOT, AND THAT IS THE WHOLE COST OF THIS BOUNDARY. `next` offers
    /// for free: it is already holding the snapshot it just decided from. `done` holds no snapshot — it reads
    /// one item's facts and stamps it — so the offer costs a scan. Condition 3 justifies `next` by "the worker
    /// is about to pick up work anyway"; nothing says that of `done`, so the read is real and is spent here
    /// deliberately.
    ///
    /// It is worth it because of WHERE THE TWO BOUNDARIES SIT IN THE RECIPE. `/pnext-item` takes (§1), works,
    /// stamps (§5), and loops back to `take` (§6). It calls `next` in exactly one place: the "if `take` finds
    /// nothing" diagnostic. So an offer that only fires at `next` fires only when the board has NO work — and
    /// a board with no work is a board with no fleet to conscript. `done --flip` runs on every completed item,
    /// which is the only moment a busy board is ever handed a thread.
    ///
    /// THE BOARD IS READ UNSCOPED, and the SUBJECT is still the stamped item's repo — #1086's decision.
    ///
    /// This shipped scoped to the item's repo, on a cost estimate that was WRONG BY AN ORDER OF MAGNITUDE and
    /// was the entire argument for it: "an unscoped scan would spend hundreds of requests of the budget the
    /// claim lock lives on". That number came from the board's ROW count (1,192). But `Scan.snapshot` sweeps
    /// a closed row WITHOUT READING IT (#520), and 1,156 of those rows are closed — only the 36 OPEN ones
    /// cost the two REST reads (body + markers, markers uncached: a lock may not be read from a cache).
    /// Measured 2026-07-17: unscoped ~85 REST, scoped-to-`.github` ~63, because 31 of the 36 open rows are in
    /// `.github` anyway. Scoping bought ~22 requests of 5,000/hr — and paid for them with a guard that failed
    /// open. The estimate is what made that look like a trade instead of a mistake.
    ///
    /// SILENT ON EVERY FAILURE, and this is the one that matters most. The stamp is EARNED — the merge
    /// happened, the column is set, the claim is dropped. A board read that fails, a budget that is gone, an
    /// engine that cannot parse its own snapshot: none of them may touch a verdict this function already
    /// printed. The offer is a courtesy appended to finished work, so it is allowed to do exactly nothing.
    let private offerChoreAfterDone (ctx: Context) (opts: Options) (ref: Ref) : unit =
        // IS THERE A LOCK AT ALL? — asked FIRST, and asked HERE rather than left to `Chores.offer`, which
        // asks it as its own step 1 "because it is a pure string match that spends nothing".
        //
        // That reasoning is exactly right and it does not survive being reached through a scan. `next` may
        // call `offer` unconditionally because `next` scans regardless — the board is already in its hand.
        // `done` does not, so ordering the question after the read would spend the scan to learn something a
        // string match answers for free. The cheapest question first is the module's own rule; this is the
        // call site keeping it.
        //
        // AND THE ARITHMETIC THAT USED TO CARRY THIS IS GONE, WHICH IS WORTH SAYING RATHER THAN DELETING.
        // This clause read: "`choreLockRef` knows one repo (`.github#1033`, ADR-0041), so SIX of the org's
        // seven receivers answer `None` — most `done` calls would buy a board scan for a guaranteed refusal."
        // #1087 gave all seven repos a lock, so the majority it invoked stopped existing and the ordering's
        // stated reason became false while the ordering stayed right. The rule survives on its own terms: a
        // string match costs nothing and a scan costs the budget the claim lock lives on, so asking the free
        // question first needs no majority to justify it. The saving is now real only for a repo the map does
        // not know — which is still a case that reaches here, and is still one nobody should pay a scan for.
        match Options.choreLockRef ctx.ChoreLocks ctx.Owner ref.Repo with
        | None -> ()
        | Some _ -> wholeBoard ctx opts |> Option.iter (offerChoreAt ctx opts Chore.AfterDone ref.Repo)

    let next (ctx: Context) (opts: Options) : int =
        // `next` is `batch` capped at one. The cap is the ONLY difference — the decision is identical, so
        // they cannot disagree.
        let opts = { opts with Limit = Some 1 }

        match scanAndDecide ctx opts Cache.Scheduling with
        | Error e -> fail e
        | Ok(rows, doc, receipt) ->
            sayRepoAdvisory receipt

            match renderLiveDecision ctx opts rows doc with
            | Error code -> code
            | Ok result ->
                match result.Chosen with
                | item :: _ -> printfn "%s" item.Ref.Short
                | [] ->
                    // THE EMPTY ARM SAYS NOTHING ON STDOUT — .github#1562, and it is the ONE thing this arm
                    // owes. `next`'s stdout is a machine contract stated three lines up and in the chore
                    // comment below: one line, the chosen item's ref, read with `$(…)`. This arm went
                    // through `printChosen`, which writes #440's headline to STDOUT, so
                    // `ref="$(fsgg-coord next --repo X)"` yielded the STRING "nothing schedulable right
                    // now." AT EXIT 0 — a value no caller can tell from a ref, over the very contract the
                    // offer below is kept off stdout to protect. An EMPTY stdout is the honest answer:
                    // `$(…)` reads it as no ref, which is what happened.
                    //
                    // #440 IS UNCHANGED, only re-routed. The headline is the same sentence in the same one
                    // spelling and the per-item passed-over reasons follow it — never a GUESSED list of
                    // causes `next` did not observe (case 41). Every line of it was already on stderr
                    // except the headline, so this moves ONE line and drops none.
                    //
                    // THE EXIT CODE STAYS 0, DELIBERATELY, and this is the second of the three shapes the
                    // issue put on the record (`take`'s EX_NONE) declined with a reason rather than
                    // skipped. `next` is `batch` CAPPED AT ONE — that is the only difference, asserted at
                    // the top of this function — and `batch` answers an empty queue with `[]`/the headline
                    // at exit 0. #1535 then made `batch --text -n 1` the DOCUMENTED substitute for `next`
                    // when a caller wants the decision without the chore offer, in the engine's `--help`,
                    // in `/pnext-item`'s command contracts, and in `writes.sh`. An EX_NONE here would make
                    // that substitution change a caller's exit status — inventing a disagreement between
                    // two verbs this file works to keep identical (#485), to replace a stdout signal that
                    // is now honest without it. `take`'s EX_NONE means "I CLAIMED NOTHING", which is a fact
                    // about a write `next` never attempts.
                    eprint nothingSchedulable
                    sayPassedOver opts.LeaseMinutes result

                // AFTER the answer, and outside every failure path above: a chore is offered to a worker who
                // already has what it came for. This is the conscription point (#733/§4.6) — the tool has no
                // thread, so the next caller is it.
                offerChoreAtNext ctx opts

                ExitGreen


    let ready (ctx: Context) (opts: Options) : int =
        // A RECONCILER read — always fresh, never the cache. Its whole job is to say what is true right now.
        match Board.bootstrapCached ctx.Transport ctx.Owner ctx.Title with
        | Error e -> fail e
        | Ok board ->
            match Scan.board ctx.Transport Cache.Reconciling ctx.Owner ctx.Title board.Number with
            | Error e -> fail e
            | Ok rows ->
                // #520 — `ready` is a TRUTH read: it shows the BOARD, including items the SCHEDULER refuses
                // (a closed-but-Ready issue, an item whose only touch-set is unmatchable). So it filters on
                // the board Status COLUMN, and NEVER on the issue's OPEN/CLOSED state — the column is the
                // projection the reconciler exists to reconcile, and hiding a closed-but-Ready row by its
                // state is exactly the disagreement `/check-board` is run to find. A PR is not an item of
                // work (#641), so it is the one thing dropped unconditionally.
                let scoped = ReadyApplication.select opts.Repo opts.Status opts.All rows

                // #979 — SAY IT, do not imply it. `ready --repo <typo>` used to print `[]` and exit 0,
                // which is indistinguishable from a repo that genuinely has no items. The exit is
                // deliberately unchanged: `ready` is a truth read, and an empty board is a real answer.
                scoped.Advisory |> Option.iter eprint

                match opts.Render with
                | Json -> printfn "%s" (renderReadyJson scoped.Rows)
                | Text ->
                    for row in scoped.Rows do
                        let status =
                            match statusWireName row.Status with
                            | "" -> "(no status)"
                            | s -> s

                        printfn "  %-14s %s  %s" status row.Ref.Short row.Title

                ExitGreen

    /// Reconcile the board projection from the same typed mechanical rules used by the deferred chore
    /// queue. The bare command is a dry run. `--apply` may perform only remedies represented by
    /// `ChoreKind`; findings that require judgement remain report-only in `lint`.
    /// Shared write-time boundary for every resolved `Status=Blocked` mutation, including the
    /// generic reconcile dispatcher.  A scan's earlier blocker observation is not a substitute: the
    /// field can change before this mutation is emitted.
    let private requireCoherentBlockedWrite (ctx: Context) (ref: Ref) (status: BoardStatus option) : Result<unit, int> =
        if status <> Some BoardStatus.Blocked then Ok()
        else
            match Board.bootstrapCached ctx.Transport ctx.Owner ctx.Title with
            | Error e -> eprint $"fsgg-coord-engine: Status=Blocked: board unreadable ({Errors.explain e})"; Error ExitError
            | Ok board ->
                match Board.itemBlockedBy ctx.Transport board ref.Owner ref.Repo ref.Number with
                | Ok(Some value) when not (String.IsNullOrWhiteSpace value) -> Ok()
                | Error e -> eprint $"fsgg-coord-engine: Status=Blocked: Blocked by unreadable ({Errors.explain e})"; Error ExitError
                | Ok _ ->
                    match Reads.issueBody ctx.Transport ref.Owner ref.Repo ref.Number with
                    | Ok body when HumanBlock.parse body |> Option.isSome -> Ok()
                    | Ok _ -> eprint "fsgg-coord-engine: Status=Blocked refuses an incoherent park (.github#2079)."; Error ExitError
                    | Error e -> eprint $"fsgg-coord-engine: Status=Blocked: body unreadable ({Errors.explain e})"; Error ExitError

    /// THE ONE RESOLVED-STATUS BOUNDARY FOR A `Ready` WRITE (.github#2698) — the deliberate mirror of
    /// `requireCoherentBlockedWrite` directly above, and placed beside it so the two lifecycle columns
    /// that carry a precondition carry it in one shape rather than two.
    ///
    /// A row boarded `Ready` with no current delivery-route receipt is UNSCHEDULABLE FROM BIRTH and says
    /// nothing about it. `Schedulability.schedulable` maps a `Stale`/`Unreadable` route to
    /// `AwaitingDeliveryRouteDecision`, `Batch.scheduleWith` then skips the row and reserves no lane —
    /// while every board projection keeps reporting it as available work. That is the same shape
    /// `.github#2220` closed for `Paths: none`, reached through a different missing precondition.
    ///
    /// IT IS A REFUSAL, NOT A CORRECTION, and never an authoring path. `DeliveryRoute.fs`'s own validator
    /// says the route *"is required and must be agent-authored"*: an engine that picked `lightweight`
    /// here to keep the promotion moving would be minting the one judgement the receipt exists to record.
    /// So this converts a SILENTLY unschedulable row into a LOUD refusal at the moment the promoting
    /// agent still holds the context a route decision needs, and names the command that authors one.
    ///
    /// IT IS SHARED BECAUSE ONE SEAM WAS NEVER THE WHOLE OF IT. The filed acceptance criterion named
    /// `add --status Ready` alone; a host then measured seven rows reaching `Ready` on 2026-08-16 and NOT
    /// ONE of them went through that seam (`.github#2698#issuecomment-5309155317`). `add` with no
    /// `--status` defaults to `Backlog` (#1823), so the real paths were `set-field Status Ready` and —
    /// for five of the seven — `reconcile --apply`, which derived `Ready` from policy and promoted rows
    /// their operator had deliberately parked in `Backlog`, with no operator action at all. A rule
    /// enforced only where a human types it is a rule a scheduled job walks around, so every caller that
    /// RESOLVES a `Status=Ready` mutation passes through here, the reducer included.
    ///
    /// IT FAILS CLOSED ON AN UNREAD LEDGER (#266): a receipt we could not READ is not a receipt we may
    /// declare absent — nor one we may declare present. The underlying IO error's own exit code is
    /// preserved rather than flattened to 1, so a rate-limited read stays EX_RATE and keeps its back-off
    /// contract instead of reading to a JSON worker as a permanent refusal.
    let private requireCurrentRouteIfReady (ctx: Context) (ref: Ref) (status: BoardStatus option) : Result<unit, int> =
        if status <> Some BoardStatus.Ready then Ok()
        else
            match readDeliveryRouteComments ctx ref with
            | Error e ->
                eprint
                    $"fsgg-coord-engine: refusing Status=Ready on %s{ref.Short} — its delivery-route receipt ledger could NOT BE READ (%s{Errors.explain e}). That is a failed read, not an absent receipt, and it is not a present one either (#266). Nothing was written."

                Error(Errors.exitCode e)
            | Ok comments ->
                match routeEvidence ref.Canonical comments with
                | DeliveryRoute.Current _ -> Ok()
                | DeliveryRoute.Stale reasons
                | DeliveryRoute.Unreadable reasons ->
                    let why = String.concat "; " reasons

                    eprint
                        $"fsgg-coord-engine: refusing Status=Ready on %s{ref.Short} — no current delivery-route receipt (%s{why}). Nothing was written."

                    eprint
                        "fsgg-coord-engine:   A row boarded Ready without one is UNSCHEDULABLE FROM BIRTH: `batch`/`take` pass it over as `awaiting an explicit current delivery-route decision`, while `ready` and every other projection keep reporting it as available work (.github#2698)."

                    eprint
                        $"fsgg-coord-engine:   The route is an agent judgement and this engine may not mint one. Author it, then re-run:  scripts/fsgg-coord delivery-route record %s{ref.Short} <receipt.json>"

                    Error ExitError

    /// True only for the one shape `Board.bootstrap` emits when the configured Projects v2 board itself
    /// could not be resolved — a credential/visibility gap, not a real reconcile finding (round-2 review
    /// repair, .github#2264 PR #2271; the org-level remedy is tracked at .github#2332, not here).
    ///
    /// SCOPED TO `reconcile` ALONE, deliberately not a change to `Errors.exitCode` — that shared table
    /// already explains, in its own comment, why `NotFound` stays a real exit-1 finding for every other
    /// caller (a mistyped `--repo`, a renamed board a human is debugging locally, ...): downgrading it
    /// there would blunt the diagnostic for the callers who NEED it to stay loud. What is different here
    /// is the CALLER — `coord-board-reconcile.yml` runs unattended, on a schedule, forever, against a
    /// board this repo's default `GITHUB_TOKEN` cannot see at all today, and `.github#1611`/`#1582`
    /// already established the org's position on an always-red unattended gate: it trains a reader to
    /// ignore red as effectively as a gate that silently never runs. The message text this matches is
    /// constructed in exactly one place (`Board.fs`'s `bootstrap`), not user input, so matching its
    /// prefix is a real structural distinction, not a fragile string sniff of an incidental error.
    let boardUnreachable (error: Errors.IoError) : bool =
        match error with
        | Errors.NotFound subject -> subject.StartsWith("no Projects v2 board titled ", StringComparison.Ordinal)
        | _ -> false

    let reconcile (ctx: Context) (opts: Options) : int =
        match scanAndDecide ctx { opts with Limit = None } Cache.Reconciling with
        | Error e when boardUnreachable e ->
            eprint
                $"fsgg-coord-engine: reconcile: %s{Errors.explain e} — NO VERDICT, not a pass: the board \
itself could not be resolved (a credential/visibility gap), never mistake this for \"reached the board, \
nothing to reconcile\". The remedy is org-level (grant this token Projects v2 read/write, or provision a \
scoped credential) and is tracked at .github#2332, not fixable from this repo's tree."
            ExitNoVerdict
        | Error e -> fail e
        // The scan ROWS, no longer discarded: `enrichBoardFacts` joins the board's `Class` column and each
        // item's title onto the parsed candidates (.github#1588). Both are scan facts that the snapshot
        // document does not carry, and the projection chore needs the column it is about to write.
        | Ok(rows, doc, receipt) ->
            receipt.RepoAdvisory |> Option.iter eprint

            match Snapshot.parse doc with
            | Error errors ->
                for e in errors do
                    eprint $"fsgg-coord-engine: %s{e.Path}: %s{e.Message}"

                ExitError
            | Ok request ->
                let items =
                    request
                    |> enrichPredicates
                    |> enrichBoardFacts rows
                    |> fun r -> r.Candidates
                    |> List.map (fun c -> c.Item)

                // `CLOSED-ISSUE-NOT-DONE` predates the auditable done receipt and used closure as a
                // terminal proxy.  Keep its established mechanical writer, but feed it only the terminal
                // fact it is entitled to project: a freshly read immutable receipt.  An unreadable comment
                // collection is deliberately equivalent to no receipt here, so a transient REST failure
                // can withhold a repair but can never manufacture Done.
                let lifecycleItems =
                    items
                    |> List.map (fun item ->
                        if item.State = Closed && item.Status <> Done then
                            match Reads.commentBodies ctx.Transport item.Ref.Owner item.Ref.Repo item.Ref.Number with
                            | Ok comments when Done.hasReceipt comments -> item
                            | Ok _
                            | Error _ -> { item with State = Open }
                        else
                            item)

                // #2264: the board column is a projection, not the source of lifecycle truth.  Gather the
                // facts again at the reconciliation boundary and send every coherent observation through
                // the typed projector before handing its one repair to the existing verified write path.
                // Comments are read for terminal rows and every live derived column. Besides delivery evidence
                // they carry the verified projection watermark. Reading live columns carries explicit intent across
                // claim/review and machine-block transitions. Settled Done remains the only historical
                // population and is still skipped below; these columns are the small live queue.
                //
                // TWO comment threads, not one (round-1 review repair). The watermark and the done receipt
                // are ISSUE facts — `Writes.lifecycleWatermark`/`Writes.doneReceipt` both post to `ref`,
                // the item itself — so `item.Ref.Number`'s thread is the right and only place to read them.
                // A delivery obligation/receipt is a PR fact: every other caller of
                // `DeliveryApplication.obligationsFromComments` in this file (`driver --events`'s
                // `mergedFactsByRef` above) reads it from `item.ItemPr`'s thread via `commentsWithIdentity`,
                // never the issue's. Reading obligations from `item.Ref.Number` — the prior shape here —
                // read a thread the worker's `<!-- fsgg:delivery-obligation -->` declaration is never
                // actually posted to, so `Outstanding` measured an always-empty set and stayed `false`
                // regardless of what was truly owed: silently reproducing the exact `.github#2135`/
                // `.github#2333` failure this projector exists to prevent, parsing precision aside.
                let resultLabel = function
                    | LifecycleProjection.Project(status, _) -> statusWireName status
                    // RENDERED DISTINCTLY, never as a `withheld:` reason. This label is what the reconcile
                    // health row reports as `intended`, and it is read by a human deciding whether the
                    // board disagrees with itself. "exempt: register" says the row has no lifecycle;
                    // "withheld: …" says we could not decide one this pass. Collapsing them would put a
                    // permanent fact in a vocabulary that means "try again".
                    | LifecycleProjection.Exempt kind -> $"exempt: %s{itemKindWireName kind}"
                    | LifecycleProjection.Withheld reason -> $"withheld: %s{reason}"
                let intentLabel = function
                    | LifecycleProjection.Auto -> "auto"
                    | LifecycleProjection.Backlog _ -> "backlog"
                    | LifecycleProjection.HumanPark(AwaitingHumanDecision, _) -> "human-decision"
                    | LifecycleProjection.HumanPark(AwaitingHumanAction, _) -> "human-action"
                    | LifecycleProjection.Deferred _ -> "deferred"
                let lifecycleHealthRows = ResizeArray<_>()
                let lifecycleChores, lifecycleWatermarks =
                    lifecycleItems
                    |> List.fold (fun (chores, watermarks) item ->
                        // A SETTLED ROW IS SWEPT, NOT READ — and this is `.github#2300` again, arriving
                        // through the projector rather than the route search. `State = Closed` reads as a
                        // narrow condition and is the OPPOSITE of one: a closed row is the only kind this
                        // board accumulates, so this gate names ~99% of it and grows by one row for every
                        // item the fleet ever completes. Measured on the live board (2026-08-11, one
                        // `reconcile --json` dry run behind a logging proxy): 2,159 of 2,181 rows are
                        // closed, and the pass spent 2,050+ billed REST requests, 1,847 of them exactly
                        // here, against a 5,000/hr budget — so a `check-board` pass (dry-run, apply, fresh,
                        // lint) could not finish inside one hour and the board driver exhausted the fleet's
                        // budget before dispatching a single worker. `Reads.commentBodies` is unconditional
                        // (`IfNoneMatch = None`) and paginates with the whole thread, so none of it is
                        // recoverable by caching.
                        //
                        // THE BOUND IS A PROOF, NOT A BUDGET HEURISTIC — the same standard `memoisable`
                        // holds. For `Closed` + `Done`, the lifecycle reducer has exactly three
                        // reachable answers, and the read cannot change ANY of them:
                        //   * an unresolved blocker → `Project(Blocked)`. Decided by `observation.Blockers`,
                        //     a free scan fact, ABOVE the closure arm — so it still fires here, unread.
                        //   * `DoneStamped` → `Project(Done)`, and `Chore.lifecycleProjection` returns
                        //     `None` on `item.Status = destination`. No chore, and — because the watermark
                        //     is added only on the `Some chore` arm below — no watermark either.
                        //   * no receipt → `Withheld "closed issue has no verified done receipt"`. Also no
                        //     chore, also no watermark. Closure is never an instruction to demote a row
                        //     (`project`'s own comment), so nothing is lost by not distinguishing these two.
                        // Both read-dependent answers are already no-ops, so skipping the read is
                        // BEHAVIOUR-PRESERVING, not a trade — identical chores, identical watermarks.
                        //
                        // WHAT IS DELIBERATELY STILL READ: a closed row that is NOT `Done`. That is
                        // `.github#2225`'s post-merge window — closed, claim live, obligations outstanding —
                        // and it is a first-class in-flight state whose receipt this projector must see. It
                        // is also small and bounded by the fleet's own concurrency, which is the difference
                        // that matters: it does not grow with the board's history.
                        let settledDone = item.State = Closed && item.Status = Done

                        // `Backlog` IS ON THIS LIST, AND ITS ABSENCE WAS THE OTHER HALF OF .github#2690.
                        //
                        // This read is the ONLY place a row's lifecycle watermark is recovered — the
                        // delivery facts and `tryWatermark` come out of the same `commentBodies` call, two
                        // lines below — so a column excluded here is a column whose recorded intent the
                        // reducer never sees. `lifecycleSelection` then falls through to
                        // `lifecyclePolicyIntent`, which answers `Auto` for any row with declared paths, and
                        // `Auto` projects `Ready`. That is a deliberate park promoted by a pass that did not
                        // read the park, and no operator-writable intent channel could have survived it: the
                        // receipt was written, and nothing ever looked.
                        //
                        // IT IS ALSO WHY .github#2690's DIRECTION C NEEDED NO OPERATOR AT ALL. `add` files a
                        // row into an empty column at `Backlog` (#1823) precisely so promotion stays "a
                        // deliberate act"; the very next pass skipped the read and promoted it anyway.
                        // `#2678`, `#2679`, `#2683`, `#2684` and `#2688` all read `Ready` within the hour of
                        // being filed.
                        //
                        // THE .github#2300 BOUND IS UNTOUCHED, and this is not a quiet reopening of it. That
                        // skip's measured subject is CLOSED history — 2,159 of 2,181 rows, 1,847 billed REST
                        // requests in one pass — and `settledDone` above still names exactly it. `Backlog` is
                        // not history: it is the live triage queue, bounded by intake rather than by
                        // everything the fleet has ever completed, and it is the same population as the
                        // `Ready` rows one line down whose comments this pass already reads unconditionally.
                        // So the increment is at most the order of the `Ready` cost already being paid, not
                        // the order of the cost `#2300` removed. (The live Backlog row count is `unverified`
                        // here: reading it needs a board scan, and a claimed worker gets one scan, spent on
                        // `take`.)
                        let needsDeliveryRead =
                            not settledDone
                            && (item.State = Closed
                                || item.Status = InReview
                                || item.Status = InProgress
                                || item.Status = Blocked
                                || item.Status = BoardStatus.Backlog
                                || item.Status = BoardStatus.Ready)

                        let delivery =
                            if not needsDeliveryRead then Some (({ Outstanding = false; DoneStamped = false }: LifecycleProjection.Delivery), None)
                            else
                                match Reads.commentBodies ctx.Transport item.Ref.Owner item.Ref.Repo item.Ref.Number with
                                | Error _ -> None
                                | Ok issueComments ->
                                    // No PR yet ⇒ nothing has been merged to owe a release obligation, so
                                    // `Outstanding = false` — the same "nothing to check" reading
                                    // `needsDeliveryRead` already gives a row with no PR at all.
                                    // `outstandingObligations` above is the ANCHORED, ID-MATCHED, REUSED
                                    // check for every other case — see its doc comment for why a quoted
                                    // marker can never pass it the way the prior bulk `.Contains` scan let
                                    // one through.
                                    let outstanding =
                                        match item.ItemPr with
                                        | None -> false
                                        | Some pr ->
                                            outstandingObligations
                                                (Reads.prHeadSha ctx.Transport item.Ref.Owner item.Ref.Repo pr)
                                                (Reads.commentsWithIdentity ctx.Transport item.Ref.Owner item.Ref.Repo pr)
                                    Some
                                        (({ Outstanding = outstanding
                                            DoneStamped = Done.hasReceipt issueComments }: LifecycleProjection.Delivery),
                                         LifecycleProjection.tryWatermark issueComments)

                        match delivery with
                        | None -> chores, watermarks // an unreadable fact withholds its write; scheduled reconciliation retries.
                        | Some(delivery, watermark) ->
                            let observedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                            let intent, selected = lifecycleSelection observedAt item delivery watermark
                            lifecycleHealthRows.Add(
                                {| current = statusWireName item.Status
                                   intended = resultLabel selected
                                   intent = intentLabel intent
                                   readComplete = true
                                   subject = item.Ref.Canonical |})
                            match selected with
                            | LifecycleProjection.Project(destination, timestamp) ->
                                match Chore.lifecycleProjection item destination with
                                | Some chore ->
                                    chore :: chores,
                                    Map.add item.Ref
                                        ({ ObservedAt = timestamp
                                           Status = destination
                                           Intent = intent }: LifecycleProjection.Watermark)
                                        watermarks
                                | None -> chores, watermarks
                            // NEITHER A CHORE NOR A WATERMARK (.github#2712 AC2 — "no park, no promote, no
                            // `Done`, no watermark"). This arm is the second half of the exemption and it
                            // is not redundant with the reducer's: `advance` decides that no STATUS is
                            // projected, and this decides that no RECEIPT is persisted either. A watermark
                            // written here would be a durable ordering fact about a lifecycle the row does
                            // not have, and `tryWatermark` would keep re-asserting it with a fresh
                            // `ObservedAt` on every pass.
                            | LifecycleProjection.Exempt _ -> chores, watermarks
                            | LifecycleProjection.Withheld _ -> chores, watermarks) ([], Map.empty)

                // Scheduling Status has one authority: the intent reducer above. `Chore.derive` remains
                // responsible only for non-lifecycle maintenance such as stale-claim cleanup and Class.
                let maintenanceChores =
                    Chore.derive lifecycleItems
                    |> List.filter (fun chore ->
                        match chore.Kind.Write with
                        | Some("Status", _) -> false
                        | _ -> true)
                let chores = maintenanceChores @ List.rev lifecycleChores

                // The field write a chore implies — the SINGLE source for the write this phase performs,
                // the `field`/`value` the receipt reports, AND the `remedy`/human-table prose below.
                //
                // IT IS THE CORE'S NOW, ASKED RATHER THAN RESTATED (.github#1588). This was a local `match`
                // over `ChoreKind` here, correct and single-sourced for as long as every kind wrote
                // `Status`. `CLASS-PROJECTION-LAG` writes `Class`, so the partition by FIELD became a fact
                // a Core invariant has to state — `ChoreTests`' "at most one chore" rests on all kinds
                // writing one column — and a Core test cannot state it against a mapping that lives here.
                // The mapping moved; this stayed the single source by ASKING for it. Note the values are
                // now `statusWireName`'s output rather than four more string literals, which is the same
                // #983 argument one call deeper.
                let write (chore: Chore.Chore) = chore.Kind.Write

                let lifecycleByRef =
                    lifecycleItems |> List.map (fun item -> item.Ref, item) |> Map.ofList

                let writesFor (chore: Chore.Chore) =
                    match write chore with
                    | Some(field, value) ->
                        let primary = [ field, Board.Set value ]
                        match chore.Kind, Map.tryFind chore.Subject lifecycleByRef with
                        | Chore.LifecycleProjectionLag destination, Some item
                            when destination <> Blocked
                                 && not (List.isEmpty item.Blockers)
                                 && Blockers.cleared item.Blockers ->
                            primary @ [ "Blocked by", Board.Clear ]
                        | _ -> primary
                    | None -> []

                // DERIVED from `write`, not matched a second time. These are the same fact in two
                // renderings — the `remedy` key and the `field`/`value` pair of the SAME JSON object, plus
                // the human table's third column — and a second hand-maintained `match` over `ChoreKind`
                // is how one object comes to describe two different writes. That is the failure this whole
                // change is about, and it does not get a pass for being prose.
                let target (chore: Chore.Chore) =
                    match write chore with
                    | Some(field, value) -> $"%s{field}=%s{value}"
                    | None -> "reap expired claim and restore its previous Status"

                let reconcileRow (chore: Chore.Chore) (outcome: ReconcileOutcome option) : ReconcileRow =
                    { Id = chore.Id
                      Rule = chore.Kind.RuleId
                      Subject = chore.Subject
                      Size = chore.Size.Label
                      Remedy = target chore
                      Statement = chore.Statement
                      Write = write chore
                      Writes = writesFor chore |> List.map (fun (field, write) -> field, match write with | Board.Set value -> value | Board.Clear -> "")
                      Observed = None
                      Outcome = outcome }

                /// Emit the machine document, ONCE, and only under `--json`.
                ///
                /// .github#1524: this used to run HERE, before the apply phase — which is why the phase
                /// below then printed its `applied`/`queued` lines past a document that had already ended.
                /// The outcome is PART of the document, so the emit has to happen after the outcome exists.
                ///
                /// EVERY EXIT FROM THIS POINT ON goes through it exactly once — but note what that does
                /// NOT say. The two exits ABOVE it emit nothing at all: a `scanAndDecide` that failed and
                /// a snapshot that would not parse. That is deliberate, and it is the honest answer rather
                /// than a convenient one. Both mean the board was never read, so there is no findings list
                /// to describe — and `[]` would be exactly the #266 fail-open this file argues against
                /// everywhere else, a read that did not happen rendered as a board with nothing wrong.
                /// A `--json` caller gets a diagnostic on stderr and a non-zero exit, which is a refusal
                /// it can tell apart from an answer; an empty array would not be.
                let emitJson (rows: ReconcileRow list) (includeOutcome: bool) =
                    match opts.Render with
                    | Json -> printfn "%s" (renderReconcileJson includeOutcome rows)
                    | Text -> ()

                let emitHealth (applicationMode: string) (applied: ReconcileRow list) =
                    let verifiedStatusWrites =
                        applied
                        |> List.choose (fun row ->
                            match row.Write, row.Outcome, row.Observed with
                            | Some("Status", value), Some Written, Some _ -> Some(row.Subject.Canonical, value)
                            | _ -> None)
                        |> Map.ofList
                    Environment.GetEnvironmentVariable "FSGG_COORD_HEALTH_REPORT"
                    |> Option.ofObj
                    |> Option.filter (String.IsNullOrWhiteSpace >> not)
                    |> Option.iter (fun path ->
                        let subjects =
                            lifecycleHealthRows
                            |> Seq.map (fun row ->
                                let finalApplied = Map.tryFind row.subject verifiedStatusWrites |> Option.defaultValue row.current
                                let reversal =
                                    row.intent <> "auto"
                                    && not (row.intended.StartsWith("withheld:", StringComparison.Ordinal))
                                    && finalApplied <> row.intended
                                {| applied = finalApplied
                                   current = row.current
                                   intended = row.intended
                                   intent = row.intent
                                   readComplete = row.readComplete
                                   reversed = reversal
                                   subject = row.subject |})
                            |> Seq.sortBy (fun row -> row.subject)
                            |> Seq.toArray
                        let report =
                            {| applicationMode = applicationMode
                               completeReadBoundary = "typed-complete-success/1"
                               schemaVersion = 1
                               subjectCount = lifecycleItems.Length
                               subjects = subjects |}
                        File.WriteAllText(path, JsonSerializer.Serialize(report, JsonSerializerOptions(WriteIndented = true)) + Environment.NewLine))

                match opts.Render with
                | Json -> ()
                | Text ->
                    if List.isEmpty chores then
                        printfn "clean — no mechanical board repairs"
                    else
                        printfn "%s (%d mechanical finding(s))" (if opts.Apply then "applying" else "dry-run") chores.Length

                        for chore in chores do
                            printfn "  %-24s %-24s %s" chore.Kind.RuleId chore.Subject.Short (target chore)

                    printfn "judgement findings are report-only: scripts/fsgg-coord lint%s" (if opts.Repo.IsSome then " --repo " + opts.Repo.Value else "")

                if not opts.Apply || List.isEmpty chores then
                    // The DRY RUN, and the nothing-to-do apply. No outcome exists, so none is claimed: the
                    // six-key rows here are byte-identical to what this verb has always emitted.
                    emitHealth (if opts.Apply then "verified-apply" else "dry-run") []
                    emitJson (chores |> List.map (fun c -> reconcileRow c None)) false
                    ExitGreen
                else
                    // The apply phase could not START. A `--json` caller still gets a document — an empty
                    // stream is not a parseable answer, and "found these, attempted none" is a real state
                    // rather than a gap (#266: never let an unmade observation read as a clean one).
                    let notAttempted (reason: string) =
                        emitJson (chores |> List.map (fun c -> reconcileRow c (Some(NotAttempted reason)))) true

                    match worker opts, Board.bootstrapCached ctx.Transport ctx.Owner ctx.Title with
                    | Error code, _ ->
                        notAttempted "no worker id resolved, so no board write could be attributed"
                        code
                    | _, Error e ->
                        let code = fail e
                        notAttempted $"the board could not be resolved: %s{Errors.explain e}"
                        code
                    | Ok w, Ok board ->
                        let mutable failed = false
                        let reapExit = Collections.Generic.Dictionary<string, int>()

                        // A mutation acknowledgement is not a board observation.  Reconciliation is a
                        // truth operation, therefore each accepted field mutation must be followed by a
                        // fresh resolver read that proves every requested field. Re-scanning the whole
                        // board after each repair re-runs the expensive closed-row census N times.
                        let verifyWrites (chore: Chore.Chore) (writes: (string * Board.FieldWrite) list) =
                            let read field =
                                Board.itemFieldValue ctx.Transport board chore.Subject.Owner chore.Subject.Repo chore.Subject.Number field
                                |> Result.map (Option.defaultValue "")

                            let rec readAll remaining observed =
                                match remaining with
                                | [] -> Ok(List.rev observed)
                                | (field, _) :: tail ->
                                    match read field with
                                    | Ok value -> readAll tail ((field, value) :: observed)
                                    | Error e -> Error e

                            match readAll writes [] with
                            // `Board.itemFieldValue` says an erased ProjectV2 item is `NotFound "board
                            // item ..."`; that is the targeted-read equivalent of the old scan not finding
                            // the row. Preserve the established receipt/diagnostic without treating other
                            // NotFound, malformed, or transport failures as an absence.
                            | Error(Errors.NotFound subject) when subject.StartsWith("board item ", StringComparison.Ordinal) ->
                                Error(None, "the item left the board before fresh verification")
                            | Error e -> Error(None, Errors.explain e)
                            | Ok observedValues ->
                                let observed = observedValues |> Map.ofList

                                let mismatches =
                                    writes
                                    |> List.choose (fun (field, requested) ->
                                        let intended = match requested with | Board.Set value -> value | Board.Clear -> ""
                                        let actual = observed[field]
                                        if actual = intended then None else Some $"%s{field}: intended '%s{intended}', observed '%s{actual}'")

                                if List.isEmpty mismatches then
                                    Ok observedValues
                                else
                                    // A failed comparison is still a successful fresh observation. Keep
                                    // every actual field/value pair in the receipt so an operator can see
                                    // which half of a coupled repair projected and which half stayed stale.
                                    Error(Some observedValues, String.concat "; " mismatches)

                        // `BoardClass = None` has two different meanings at two different boundaries:
                        // this scan's row may have an UNSET `Class` value, while this map can prove that
                        // the project declares NO `Class` field at all. `Chore.derive` deliberately cannot
                        // collapse those facts: it is pure and sees rows, not a board capability. Here we
                        // have paid for that capability, so do not turn the second fact into one 422 per
                        // classed row. This is not a fail-open: `lint` still reads the item's body and
                        // reports `CLASS-UNSET`; only a projection whose destination demonstrably does not
                        // exist is withheld. Create it first with `createProjectV2Field` as documented in
                        // docs/coordination/board-schema.md, then the next reconcile projects every row.
                        let classFieldMissing = not (Map.containsKey "Class" board.Fields)

                        // The IDENTICAL fact for `Kind` (.github#2712), and it is the COMMON case rather
                        // than the exotic one: no board in this org has a `Kind` field yet, because
                        // creating it is `createProjectV2Field` and an operator action (the `Class`
                        // precedent in docs/coordination/board-schema.md). This is why the projection is
                        // withheld behind ONE diagnostic instead of failing a write per declared row —
                        // and it is why landing this change is safe against today's board.
                        let kindFieldMissing = not (Map.containsKey "Kind" board.Fields)

                        let withheldClassProjections =
                            chores
                            |> List.filter (fun c ->
                                match c.Kind with
                                | Chore.ClassProjectionLag _ -> true
                                | _ -> false)

                        let withheldKindProjections =
                            chores
                            |> List.filter (fun c ->
                                match c.Kind with
                                | Chore.KindProjectionLag _ -> true
                                | _ -> false)

                        if classFieldMissing && not (List.isEmpty withheldClassProjections) then
                            eprint
                                "fsgg-coord-engine: reconcile: board has no Class field; withheld Class projections. Create it with createProjectV2Field before writing the first Class: line (docs/coordination/board-schema.md)."

                        if kindFieldMissing && not (List.isEmpty withheldKindProjections) then
                            eprint
                                "fsgg-coord-engine: reconcile: board has no Kind field; withheld Kind projections. Create it with createProjectV2Field before writing the first Kind: line (docs/coordination/board-schema.md)."

                        // `reap` owns the marker CAS and its column-restore rule. Run it once per affected
                        // repo; the same fresh derivation means every additional stale marker it safely
                        // collects there is another typed STALE-CLAIM remedy, never a broader judgement.
                        chores
                        |> List.choose (fun c ->
                            match c.Kind with
                            | Chore.StaleClaim _ -> Some c.Subject.Repo
                            | _ -> None)
                        |> List.distinct
                        |> List.iter (fun repo ->
                            // Re-enter the executable's typed `reap` verb rather than duplicating its
                            // marker CAS, renewed-since-scan check, and PreviousStatus restore here.
                            // `Environment.ProcessPath` is this exact packaged client, so this does not
                            // depend on a checkout wrapper or PATH.
                            let processPath =
                                Environment.ProcessPath
                                |> Option.ofObj
                                |> Option.defaultWith (fun () -> invalidOp "reconcile: current executable path is unavailable")

                            let psi = ProcessStartInfo(processPath)
                            psi.UseShellExecute <- false

                            // A packaged apphost is directly executable. Under `dotnet <assembly>.dll`
                            // (including a framework-dependent tool install), ProcessPath is `dotnet`;
                            // preserve that host and name the entry assembly before the typed verb.
                            if
                                String.Equals(
                                    Path.GetFileNameWithoutExtension processPath,
                                    "dotnet",
                                    StringComparison.OrdinalIgnoreCase
                                )
                            then
                                let entryAssembly = Reflection.Assembly.GetEntryAssembly()

                                if isNull entryAssembly || String.IsNullOrWhiteSpace entryAssembly.Location then
                                    invalidOp "reconcile: current entry assembly path is unavailable"

                                psi.ArgumentList.Add entryAssembly.Location

                            psi.ArgumentList.Add "reap"
                            psi.ArgumentList.Add "--repo"
                            psi.ArgumentList.Add repo
                            psi.ArgumentList.Add "--apply"
                            psi.ArgumentList.Add "--worker"
                            psi.ArgumentList.Add w.Id

                            // .github#1524, THE THIRD LEAK — and the one the filed root cause did not name.
                            //
                            // This child INHERITS our stdout, and `reap --apply` is not quiet on it: it
                            // prints `reaped <ref> worker <w>` and its column-restore lines there. So under
                            // `--json` a STALE-CLAIM finding put a whole second program's prose on the same
                            // stream as our document.
                            //
                            // BE PRECISE ABOUT WHERE. Under the OLD ordering the array was written before
                            // this phase ran, so all three leaks were TRAILING garbage after a complete
                            // `]` — never interleaved inside the array. Under the NEW ordering the document
                            // is written last, so an unredirected child would put its prose IN FRONT of the
                            // array instead. Both break a parser reading the whole stream; neither is
                            // "inside" it. What makes this one distinct is not position but PROVENANCE: it
                            // is another process's output, unbounded, arriving at a moment this handler
                            // does not control — so it cannot be fixed by choosing when we print.
                            //
                            // Under `--json` we capture it and forward it to STDERR, where this CLI already
                            // puts diagnostics: the operator detail is not lost, and stdout stays one
                            // document. Under `--text` the child keeps writing straight through to our
                            // stdout, which is what makes the human projection byte-identical.
                            psi.RedirectStandardOutput <- (opts.Render = Json)

                            use child = Process.Start psi

                            // Drain BEFORE waiting. A redirected pipe that fills while we block in
                            // `WaitForExit` deadlocks both processes.
                            if opts.Render = Json then
                                let inherited = child.StandardOutput.ReadToEnd()

                                for line in inherited.Split('\n') do
                                    let line = line.TrimEnd '\r'

                                    if not (String.IsNullOrWhiteSpace line) then
                                        eprint $"fsgg-coord-engine: reconcile: reap: %s{line}"

                            child.WaitForExit()
                            reapExit[repo] <- child.ExitCode

                            if child.ExitCode <> ExitGreen then failed <- true)

                        let applied =
                            [ for chore in chores do
                                  match write chore with
                                  | Some("Kind", _) when kindFieldMissing ->
                                      // One map-level diagnostic above names the remedy, on the `Class`
                                      // arm's exact terms.
                                      reconcileRow
                                          chore
                                          (Some(
                                              NotAttempted
                                                  "the board declares no Kind field; create it with createProjectV2Field before projecting Kind"
                                          ))
                                  | Some("Class", _) when classFieldMissing ->
                                      // One map-level diagnostic above names the remedy. Repeating it for
                                      // every row would turn a board configuration fact into N failures.
                                      reconcileRow
                                          chore
                                          (Some(
                                              NotAttempted
                                                  "the board declares no Class field; create it with createProjectV2Field before projecting Class"
                                          ))
                                  | None ->
                                      // `STALE-CLAIM` — no field write; the `reap` pass above owns it. Its
                                      // outcome is that pass's exit code, per REPO, and it is reported as
                                      // `reaped` rather than `written` precisely because it is the weaker
                                      // observation. A repo with no recorded pass never happens (the pass
                                      // is driven off these same chores), so an absent entry is honestly
                                      // "not attempted" rather than a guess at success.
                                      match reapExit.TryGetValue chore.Subject.Repo with
                                      | true, code when code = ExitGreen -> reconcileRow chore (Some Reaped)
                                      | true, code ->
                                          reconcileRow
                                              chore
                                              (Some(
                                                  Failed
                                                      $"`reap --repo %s{chore.Subject.Repo} --apply` exited %d{code}"
                                              ))
                                      | _ ->
                                          reconcileRow chore (Some(NotAttempted "no reap pass ran for this repo"))
                                  | Some(field, value) ->
                                      let writes = writesFor chore

                                      // `ChoreKind.Write` deliberately carries a variable field/value so
                                      // reconciliation can project more than Status.  When that resolved
                                      // pair is `Status=Blocked`, re-check coherence immediately before the
                                      // transport mutation: the scan that derived this chore is stale by
                                      // definition once another actor can clear `Blocked by`.
                                      let resolvedStatus = if field = "Status" then Reads.statusOfName value else None

                                      let blockedGate =
                                          if field = "Status" then
                                              match resolvedStatus, Map.tryFind chore.Subject lifecycleWatermarks with
                                              // A typed HumanPark intent is itself the durable reason for
                                              // this lifecycle write. Requiring the old prose sentinel as
                                              // well would make the new-only reducer compute Blocked and
                                              // then let a retired authority veto its own projection.
                                              // Blocker-derived Auto writes still pass through the live
                                              // Blocked-by/body coherence boundary below.
                                              | Some Blocked, Some watermark when LifecycleProjection.isHumanPark watermark.Intent -> Ok()
                                              | _ -> requireCoherentBlockedWrite ctx chore.Subject resolvedStatus
                                          else
                                              Ok()

                                      let gate =
                                          if field = "Status" then
                                              // .github#2698 — THE SEAM WITH NO OPERATOR IN IT, and the one
                                              // the filed acceptance criterion did not name. A host measured
                                              // `reconcile --apply` reporting `LIFECYCLE-PROJECTION-LAG …
                                              // Status=Ready` for `.github#2721`-`#2723` and PROMOTING all
                                              // three — rows deliberately set to `Backlog` to honour a
                                              // design's ordering — with no `add`, no `set-field`, and no
                                              // human in the loop. Every one landed `Ready` with no receipt
                                              // and was then found unschedulable by `batch --explain`.
                                              //
                                              // So the reducer is gated exactly as the operator doors are.
                                              // This does NOT stop the reducer DERIVING `Ready` — that
                                              // projection has a purpose this row did not study — it stops
                                              // the derived value being WRITTEN onto a row that cannot be
                                              // scheduled once it lands.
                                              //
                                              // The receipt read is paid only on a row this pass is ALREADY
                                              // about to write, never per board row — `enrichDeliveryRoutes`'
                                              // own #2300 lesson, kept by placing the gate at the mutation
                                              // rather than at the scan.
                                              match blockedGate with
                                              | Error rc -> Error rc
                                              | Ok() -> requireCurrentRouteIfReady ctx chore.Subject resolvedStatus
                                          else
                                              Ok()

                                      // WHICH CLASS OF OUTCOME IS A ROUTE REFUSAL? .github#2698 REPAIR 1,
                                      // AND THE CHANGE MUST DECIDE IT RATHER THAN INHERIT IT.
                                      //
                                      // `coord-board-reconcile.yml` runs this pass on a SCHEDULE and ends
                                      // `exit "$rc"` (`:347`, `:362`). It maps two conditions to
                                      // `::warning:: + exit 0` — an unresolvable board (exit 4) and an
                                      // exhausted budget (EX_RATE) — under a rule it states in its own
                                      // words: those are "NO VERDICT, not a pass", and the mapping is
                                      // "never for a genuine finding". Left in the `Failed` arm below, a
                                      // route refusal exits 1 and REDS that scheduled workflow; and since
                                      // nothing recurring authors a receipt, the red cannot self-clear —
                                      // it would sit red until a human authored receipts by hand, on a
                                      // `main` this item's own body already describes as wedged.
                                      //
                                      // IT IS NEITHER OF THOSE TWO CLASSES, AND IT IS NOT A FAILURE. The
                                      // pass ran to completion and the board is not wrong; ONE derived
                                      // remedy was declined because performing it needs a judgement this
                                      // pass may not make. `reconcile`'s own contract already draws that
                                      // line — "`--apply` may perform only remedies represented by
                                      // `ChoreKind`; findings that require judgement remain report-only" —
                                      // and the vocabulary for it already exists and is already used for a
                                      // mechanically identical case: `NotAttempted`, which is what the
                                      // `classFieldMissing` arm above emits for a remedy whose
                                      // precondition lies outside this pass, WITHOUT failing it.
                                      //
                                      // So it is reported, loudly, and not failed. The refusal text (row,
                                      // reason, and the command that authors a receipt) is already on
                                      // stderr from the gate itself, the row carries `not-attempted` and
                                      // its reason in the `--json` receipt, and `$rc` stays 0 for a pass
                                      // whose only finding is "these rows owe a route decision".
                                      //
                                      // THE BLOCKED GATE'S CLASSIFICATION IS UNTOUCHED. It is a different
                                      // judgement — an incoherent park is the board being wrong — and it
                                      // keeps its `Failed` arm and its non-zero exit. That is also why
                                      // these two are told apart here rather than through `gate`, whose
                                      // `Result.isError` cannot say WHICH boundary refused: before this,
                                      // a route refusal was reported to the operator as a
                                      // "Status=Blocked coherence gate" refusal, naming a gate that had
                                      // returned `Ok`.
                                      let routeRefused = Result.isError gate && Result.isOk blockedGate

                                      let outcome =
                                          match gate with
                                          | Error _ -> Ok Board.NotOnBoard
                                          | Ok() when List.length writes > 1 ->
                                              Board.boardWriteBatch ctx.Transport board chore.Subject.Owner chore.Subject.Repo chore.Subject.Number writes w.Id
                                          | Ok() ->
                                              Board.boardWrite ctx.Transport board chore.Subject.Owner chore.Subject.Repo chore.Subject.Number field (Board.Set value) w.Id

                                      match outcome with
                                      // The two lines .github#1524 is about. They are the HUMAN projection
                                      // and every recipe reads them.
                                      //
                                      // THEY NAME `field` NOW, NOT THE LITERAL "Status" (.github#1588). Both
                                      // lines hardcoded the word while `field` — the name of the column
                                      // actually being written, already bound right here — sat unused two
                                      // lines above. That was invisible for as long as every chore wrote
                                      // `Status`, and it is the same defect `write`'s own comment warns
                                      // about: "one object comes to describe two different writes". MEASURED
                                      // on the live board: `CLASS-PROJECTION-LAG` applied cleanly and
                                      // reported `applied .github#1547 Status=decision` — a receipt naming a
                                      // column that was never touched, for a value `Status` has no option
                                      // for. A reader checking that receipt would go looking for a corrupt
                                      // Status column, and `--json`'s `write` object said `Class` the whole
                                      // time, so the two projections of one fact disagreed.
                                      | Ok Board.Written ->
                                          match verifyWrites chore writes with
                                          | Ok observed ->
                                              // The write acknowledgement is not the ordering receipt.  Store
                                              // the watermark only after the fresh scan above proved the row
                                              // contains the projected status; otherwise a late event could
                                              // be suppressed by a receipt for a mutation that never landed.
                                              match chore.Kind, Map.tryFind chore.Subject lifecycleWatermarks with
                                              | Chore.LifecycleProjectionLag _, Some watermark ->
                                                  match Writes.lifecycleWatermark ctx.Transport chore.Subject (LifecycleProjection.watermarkMarker watermark) with
                                                  | Error e ->
                                                      failed <- true
                                                      eprint $"fsgg-coord-engine: reconcile: %s{chore.Subject.Short} Status=%s{value} was verified but its lifecycle watermark could not be persisted: %s{Errors.explain e}"
                                                      { reconcileRow chore (Some(Failed "verified status has no durable lifecycle watermark")) with Observed = Some observed }
                                                  | Ok () ->
                                                      match opts.Render with
                                                      | Text -> printfn "applied  %s  %s=%s" chore.Subject.Short field value
                                                      | Json -> ()
                                                      { reconcileRow chore (Some Written) with Observed = Some observed }
                                              | _ ->
                                                  match opts.Render with
                                                  | Text -> printfn "applied  %s  %s=%s" chore.Subject.Short field value
                                                  | Json -> ()
                                                  { reconcileRow chore (Some Written) with Observed = Some observed }
                                          | Error(observed, reason) ->
                                              eprint $"fsgg-coord-engine: reconcile: %s{chore.Subject.Short} mutation was accepted but fresh verification failed: %s{reason}"
                                              failed <- true
                                              { reconcileRow chore (Some(Failed reason)) with Observed = observed }
                                      | Ok Board.Deferred ->
                                          match opts.Render with
                                          | Text ->
                                              printfn
                                                  "queued   %s  %s=%s (run scripts/fsgg-coord flush)"
                                                  chore.Subject.Short
                                                  field
                                                  value
                                          | Json -> ()

                                          reconcileRow chore (Some Deferred)
                                      // .github#2698 — THE ROUTE REFUSAL, REPORTED AND NOT FAILED. Matched
                                      // BEFORE the `Result.isError gate` arm below, which is the
                                      // `Status=Blocked` coherence refusal and keeps its non-zero exit.
                                      // The reason travels in the receipt so a `--json` reader gets the
                                      // row, the rule, and what is owed; the gate itself has already put
                                      // the full refusal and the authoring command on stderr.
                                      | Ok Board.NotOnBoard when routeRefused ->
                                          reconcileRow
                                              chore
                                              (Some(
                                                  NotAttempted
                                                      $"%s{chore.Subject.Short} has no current delivery-route receipt, so Status=Ready was NOT written — a row promoted without one is unschedulable. The route is an agent judgement this pass may not make: scripts/fsgg-coord delivery-route record %s{chore.Subject.Short} <receipt.json>"
                                              ))
                                      | Ok Board.NotOnBoard when Result.isError gate ->
                                          failed <- true
                                          reconcileRow chore (Some(Failed "Status=Blocked coherence gate refused the stale reconcile write"))
                                      | Ok Board.NotOnBoard ->
                                          eprint
                                              $"fsgg-coord-engine: reconcile: %s{chore.Subject.Short} left the board before apply."

                                          failed <- true
                                          reconcileRow chore (Some NotOnBoard)
                                      | Error e ->
                                          eprint
                                              // `field`, not the literal "Status" — the third of the three
                                              // lines .github#1588 caught. This one is the worst of them:
                                              // it is the DIAGNOSTIC, read by whoever is working out why a
                                              // write failed, and naming the wrong column sends them to
                                              // audit a field nothing touched.
                                              $"fsgg-coord-engine: reconcile: %s{chore.Subject.Short} %s{field}=%s{value} failed: %s{Errors.explain e}"

                                          failed <- true
                                          reconcileRow chore (Some(Failed(Errors.explain e))) ]

                        emitHealth "verified-apply" applied
                        emitJson applied true

                        // The DEFERRED writes, said out loud ONCE, on stderr, under `--json` only.
                        //
                        // The human form carries `(run scripts/fsgg-coord flush)` on each queued line; the
                        // machine form carries the FACT as `outcome:"deferred"`, and a remedy sentence is
                        // not a fact, so it does not belong in the document. But it must not simply
                        // vanish: a queued write is not lost, and `flush` DOES replay it — nothing
                        // replays it ON ITS OWN, which is the whole reason the remedy has to reach a
                        // human. An operator who piped stdout into a parser would otherwise be told
                        // nothing at all. stderr is where this CLI already puts operator diagnostics (and
                        // where `repos-audit.sh` and friends put theirs), so that is where it goes. The
                        // wording is the one this file already uses at the other three deferral sites.
                        //
                        // DERIVED from the rows just emitted, not counted alongside them — the #1517
                        // lesson. The advisory and the document cannot disagree about how many writes
                        // queued, because there is only one place that number is computed.
                        match opts.Render with
                        | Json ->
                            let deferred =
                                applied
                                |> List.filter (fun r -> r.Outcome = Some Deferred)
                                |> List.map (fun r -> r.Subject.Short)

                            if not (List.isEmpty deferred) then
                                let refs = String.concat ", " deferred

                                eprint
                                    $"fsgg-coord-engine: reconcile: %d{List.length deferred} board write(s) DEFERRED — the budget is exhausted, so they are QUEUED, not lost, and NOTHING replays them on its own (%s{refs}):  scripts/fsgg-coord flush"
                        | Text -> ()

                        if failed then ExitError else ExitGreen



    /// The touch-set a claim reserves (or an In-progress item declares) — the `paths` a consumer keys on
    /// (case 25). Read from the issue body; an undeclared or unreadable one is an empty list, because `who`
    /// reports what is reserved, and nothing is reserved on a surface nobody declared.
    ///
    /// `Unreadable _ -> []` is an ADVICE consumer, safe as written (.github#2233 scope item 5 audit):
    /// `who` is purely informational (case 25 above; its production call site's own comment,
    /// `.github#1794`, says "the reserved touch-set is informational, so a body we could not read is an
    /// empty list, not a failed `who` — a body is not a lock"). This function never reaches a
    /// scheduling or verdict decision on the strength of an unread body — it only ever reports what a
    /// worker may safely treat as reserved, and an unread body reserves nothing to REPORT, whatever it
    /// may turn out to reserve once read.
    let private pathNames (ts: TouchSet) : string list =
        match ts with
        | Declared tokens ->
            tokens
            |> List.map (fun t ->
                match t with
                | Matchable s -> s
                | Unmatchable s -> s)
        | Undeclared
        | DeclaredNone
        // A chore reserves nothing, so `who` reports no reserved paths for it.
        | DeclaredChore
        | Unreadable _ -> []

    /// LOCAL GIT WORKTREES, as (item-number, path) — the join `who --local` needs (#959). Every worker runs
    /// in a per-item worktree branched `item/<n>-<slug>` (pnext-item §2), so the item number in the branch
    /// name is the key that ties a `../<repo>-<n>` directory back to its claim. Read straight off
    /// `git worktree list --porcelain`; nothing here reaches the network.
    ///
    /// bash joined on this same key (`item/<n>` → `.number`), NOT on the `fsgg.worker` git-config value it
    /// also read — a worktree's identity is which item it is for, and that survives even when the id stamp is
    /// missing or stale. Outside a checkout `git worktree list` fails; that is an empty join, never an error
    /// (`who --local` outside a repo is a `who` with no local column, not a failure).
    let private localWorktrees () : (int * string) list =
        try
            let psi = ProcessStartInfo("git", "worktree list --porcelain")
            psi.RedirectStandardOutput <- true
            // stderr is NOT redirected: the output is a few bounded lines, so there is nothing to drain, and
            // draining one pipe while the other fills is the deadlock #498 just removed one command over.
            // git's own stderr on the inherited channel is fine — this is a read that may fail silently.
            psi.UseShellExecute <- false
            use p = Process.Start psi
            let out = p.StandardOutput.ReadToEnd()
            p.WaitForExit()

            if p.ExitCode <> 0 then
                []
            else
                // Porcelain blocks are `worktree <path>` … `branch refs/heads/<b>`, blank-line separated. Pair
                // a path with the item number of its branch when that branch is an `item/<n>-…`; a worktree on
                // any other branch (a shared checkout on `main`) has no item and is dropped from the join.
                let mutable currentPath = ""
                let acc = ResizeArray<int * string>()

                for line in out.Split('\n') do
                    let line = line.TrimEnd('\r')

                    if line.StartsWith "worktree " then
                        currentPath <- line.Substring 9
                    elif line.StartsWith "branch " then
                        let branch = line.Substring(7).Replace("refs/heads/", "")
                        let m = Text.RegularExpressions.Regex.Match(branch, @"^item/(\d+)-")

                        if m.Success && currentPath <> "" then
                            acc.Add(int m.Groups.[1].Value, currentPath)

                List.ofSeq acc
        with _ ->
            []



    let who (ctx: Context) (opts: Options) : int =
        let scopeText =
            match opts.Repo with
            | Some repo -> $"repository %s{ctx.Owner}/%s{repo}"
            | None -> "all repositories represented on the Coordination board"

        // Scope is part of the answer, including the empty answer. Without it, `[]` from the hub checkout
        // was routinely reported as the org-wide claim set (#1369). A non-empty JSON row already names its
        // repository; for an empty JSON answer the explicit scope rides stderr, preserving the array wire
        // contract and callers that deliberately combine streams. The human table always carries a header.
        match opts.Render with
        | Json -> ()
        | Text -> printfn "scope: %s" scopeText

        // A truth read — fresh, and it reads the LOCK, which is never cached.
        match Board.bootstrapCached ctx.Transport ctx.Owner ctx.Title with
        | Error e -> fail e
        | Ok board ->
            match Scan.board ctx.Transport Cache.Reconciling ctx.Owner ctx.Title board.Number with
            | Error e -> fail e
            | Ok rows ->
                // The board rows in scope, no PRs (#641). #480 — `--repo` scopes to the checkout. These
                // carry the ONE thing the off-board scan cannot: the In-progress COLUMN, which is the only
                // fact that licenses an `unclaimed` verdict on a markerless item (work outside the protocol).
                let scoped = rows |> List.filter (fun r -> not r.IsPullRequest) |> Scan.scope opts.Repo

                // #979. `who` does not fail OPEN on an unrostered `--repo` the way `ready` did — the
                // off-board fallback below scans `<owner>/<name>` directly, so a repo that does not
                // exist fails the read and takes the verb with it (exit 1, measured). The advisory still
                // earns its place: it names the MISSPELLING, where the failed read only reports that
                // some HTTP call died — which reads as an outage, and sends the worker down that axis.
                scoped.Advisory |> Option.iter eprint

                let boardRows = scoped.Rows

                // A CLAIM LIVES OFF THE BOARD TOO (#461/#581, case 25). The board's In-progress column is not
                // the claim set: a marker sits on the ISSUE, and the issue's board Status may be Ready (a
                // failed column flip), or the item may never have reached the board at all. So the candidate
                // set is the board's In-progress rows (arm A — the only thing that can be `unclaimed`) UNION
                // every open issue in the repos in scope (arm B — the off-board scan, PAGINATED and never
                // conditional, because a lock has no hundred-issue limit and a 304 can hide a fresh marker).
                //
                // The repos an off-board claim could live in are derived from the board scan already in hand
                // (bash's `active_claims`). A `--repo` that names no board item at all still resolves against
                // the default owner — so a claim on an issue that never reached the board is still found.
                let repos =
                    let fromBoard =
                        boardRows |> List.map (fun r -> r.Ref.Owner, r.Ref.Repo) |> List.distinct

                    match fromBoard, opts.Repo with
                    | [], Some name when name.Contains "/" ->
                        let parts = name.Split('/')
                        [ parts.[0], parts.[1] ]
                    | [], Some name -> [ ctx.Owner, name ]
                    | rs, _ -> rs

                let mutable failure: Errors.IoError option = None

                // arm B — the off-board scan. Each open issue's BODY rides along for free (one list read
                // serves both the marker scan and the touch-set extraction), keyed by ref.
                let offBoard =
                    System.Collections.Generic.Dictionary<string * string * int, Reads.IssueBodyRead>()

                for (o, r) in repos do
                    if failure.IsNone then
                        // FAILS CLOSED (#461): an unreadable scan is never an empty one — `who` would report
                        // a held item as free, which is exactly the fail-open the lock exists to prevent.
                        match Reads.openIssues ctx.Transport o r with
                        | Error e -> failure <- Some e
                        | Ok issues ->
                            for issue in issues do
                                // .github#1794: an unreadable BODY is stored as unreadable, not as `""`. The
                                // ROW is still a candidate either way — its number was read, so its marker
                                // will be — which is the fact `who` actually reports.
                                offBoard.[(o, r, issue.Number)] <- issue.Body

                // arm A ∪ arm B, deduped by ref and ordered (repo, number) so the output is deterministic.
                // A board `In progress` row is a candidate even with no marker (it may be `unclaimed`); an
                // arm-B issue is a candidate only if it carries one (below) — a chatty issue is not in flight.
                let inProgressRefs =
                    boardRows
                    |> List.filter (fun r -> r.Status = BoardStatus.InProgress)
                    |> List.map (fun r -> r.Ref.Owner, r.Ref.Repo, r.Ref.Number)
                    |> Set.ofList

                // ARM C — THE POST-MERGE WINDOW (.github#2225). Arm B is `Reads.openIssues`, so it cannot
                // see a CLOSED issue; arm A only carries the `In progress` COLUMN, and an item whose PR has
                // merged has usually already moved past it. A claim held on a closed, unstamped item was
                // therefore in NEITHER set, so its marker was never even requested — and `who` answered
                // EMPTY, which reads as "nothing in flight" rather than "I cannot see this". The hardened
                // per-candidate read below (#461/#1668) could not save it: a read nobody issues has no
                // unreadable state to report.
                //
                // Closing is the MIDDLE of an item here, not its end — the terminal fact is the done STAMP —
                // so a closed row that is not yet `Done` is exactly as in-flight as an `In progress` one.
                let closedUnstampedRefs =
                    boardRows
                    |> List.filter (fun r -> r.State = Closed && r.Status <> BoardStatus.Done)
                    |> List.map (fun r -> r.Ref.Owner, r.Ref.Repo, r.Ref.Number)
                    |> Set.ofList

                let candidates =
                    if failure.IsSome then
                        []
                    else
                        Seq.append offBoard.Keys (Set.union inProgressRefs closedUnstampedRefs)
                        |> Seq.distinct
                        |> Seq.sortBy (fun (o, r, n) -> o, r, n)
                        |> List.ofSeq

                let isInProgress ref =
                    inProgressRefs |> Set.contains ref

                let results = ResizeArray<WhoRow>()

                for (o, r, n) in candidates do
                    if failure.IsNone then
                        let ref = { Owner = o; Repo = r; Number = n }

                        // A FAILED MARKER READ IS FATAL (#461): a claim set we could not read is never an
                        // empty one, so `who` fails closed rather than reporting a live lock as absent.
                        // .github#1668: `markerScan` because `who` REPORTS and can carry an honest
                        // `Undetermined` result. Decision and write callers pass this same scan through
                        // `requireCompleteMarkerScan` and refuse; no projection may discard completeness
                        // anymore (.github#1896).
                        match Reads.markerScan ctx.Transport o r n with
                        | Error e -> failure <- Some e
                        | Ok scan ->
                            let markers = scan.Markers

                            // Classify by the LOCK. The lowest-id marker in the CAS's own total order names
                            // the holder; the lease decides live (`Held`) vs past its window (`Stale`). No
                            // marker at all is in flight ONLY when the board column says In progress — an
                            // arm-B issue someone merely commented on is not a claim.
                            let state =
                                match Reads.winner opts.LeaseMinutes markers with
                                | Some m -> Some(Held m)
                                | None ->
                                    // `Reads.lowestId`, NOT `Reads.reserver`: this arm has already
                                    // established there is no live winner, and it needs the lease-free
                                    // ordering alone. `reserver` would re-ask the liveness question this
                                    // match just answered. The Held/Stale distinction is `who`'s own and
                                    // stays here (design §4.2's second constraint).
                                    match Reads.lowestId markers with
                                    | Some m -> Some(Stale m)
                                    // NO MARKER WE COULD READ. Before this may be reported as an ABSENCE,
                                    // the read has to have been COMPLETE (.github#1668). If any comment on
                                    // this issue could not be classified, one of them may have been the
                                    // claim, and the honest answer is that we cannot tell.
                                    //
                                    // It fires on EVERY arm, deliberately. On arm A it replaces the
                                    // `UNCLAIMED` accusation. On arms B and C — where a markerless issue is
                                    // normally not in flight at all and is simply dropped — it makes the row
                                    // APPEAR, because an off-board or post-merge issue hiding an unreadable
                                    // marker is precisely the held item this verb must not omit.
                                    | None when not (List.isEmpty scan.Unreadable) -> Some Undetermined
                                    | None -> if isInProgress (o, r, n) then Some Unclaimed else None

                            match state with
                            | None -> ()
                            | Some st ->
                                // The reserved touch-set is informational, so a body we could not read is an
                                // empty list, not a failed `who` — a body is not a lock. Only `--json` reports
                                // paths, so the human table pays no body read. An arm-B body is already in
                                // hand (free); a board-only item (In progress but closed/unlisted) pays one.
                                let paths =
                                    match opts.Render with
                                    | Text -> []
                                    | Json ->
                                        let body =
                                            match offBoard.TryGetValue((o, r, n)) with
                                            // .github#1794: an unreadable arm-B body is `[]` for the same
                                            // reason a failed `issueBody` is — informational, and a body is
                                            // not a lock. It is NOT re-read as `""`, which would have
                                            // printed an empty `paths` array as though we had looked.
                                            | true, Reads.BodyUnread _ -> Error()
                                            | true, Reads.BodyRead b -> Ok b
                                            | _ ->
                                                match Reads.issueBody ctx.Transport o r n with
                                                | Ok text -> Ok text
                                                | Error _ -> Error()

                                        match body with
                                        | Ok text -> pathNames (TouchSet.parse text)
                                        | Error() -> []

                                // #581 — a bare `STALE` and `STALE (#NNN OPEN)` are not the same fact, and
                                // `who` is what a human reads immediately before deciding to reap. Probe ONLY
                                // a stale row (a held claim needs no proof of life, an unclaimed item has
                                // nothing to prove): does the item's own `item/<n>-*` PR exist, and on which
                                // branch? REST, and rare. The lease-vs-life decision that DELETES lives in
                                // `reap` (which re-probes and fails closed); here a probe we could not make is
                                // simply a bare `STALE` — advisory, not the gate.
                                //
                                // Two reads (the open-PR scan, then that PR's head ref) rather than bash's
                                // one: the head ref `prAlive` matched on is not surfaced through `Liveness`,
                                // so `who` reads it back with `prHeadRef`. Disposed as acceptable — the path
                                // is a stale claim WITH an open PR, which is rare, and both reads are REST.
                                // Probe proof of life ONCE for the row (it is a REST call), then read both
                                // facts off it: the open PR (#581), and — #1055 — whether a pushed
                                // `item/<n>-*` branch exists with no PR yet, so a human sees WHY the item is
                                // withheld rather than a bare `STALE` that reads as reapable.
                                let liveness =
                                    match st with
                                    | Stale _ -> Some(Reads.prAlive ctx.Transport o r n)
                                    | Held _
                                    | Unclaimed
                                    | Undetermined -> None

                                let livePr =
                                    match liveness with
                                    | Some(Ok(LeaseExpiredPrOpen pr)) ->
                                        match Reads.prHeadRef ctx.Transport o r pr with
                                        | Ok headRef -> Some(pr, headRef)
                                        | Error _ -> None
                                    | _ -> None

                                let branchPushed =
                                    match liveness with
                                    | Some(Ok LeaseExpiredBranchPushed) -> true
                                    | _ -> false

                                // #697: a bare `STALE (#NNN OPEN)` reads as an abandoned branch, and the
                                // reader reaches for `reap` — the destructive verb — on FINISHED work. So
                                // when there IS a live PR, read WHAT IT SAYS: `who` is what a human reads
                                // immediately before deciding to reap, so it is exactly where "GREEN: LAND
                                // IT" has to appear. Advisory (a `PrUnknown` just falls back to the bare
                                // flag), and only on the rare stale-with-open-PR row, so the extra reads ride
                                // the same cost budget as the #581 probe above.
                                let prState =
                                    match livePr with
                                    | Some(pr, _) -> Some(Reads.prLandable ctx.Transport o r pr)
                                    | None -> None

                                results.Add(
                                    { Ref = ref
                                      State = st
                                      Paths = paths
                                      LivePr = livePr
                                      BranchPushed = branchPushed
                                      PrState = prState
                                      // Filled in below, once the worktree read has run once for the whole
                                      // set rather than per row — a claim's worktree is a local fact, not one
                                      // of the network reads this loop is spending.
                                      Worktree = None

                                      // .github#1668: ON EVERY ROW, not just the markerless one. The
                                      // `Undetermined` STATE is only reachable when the short read left no
                                      // marker at all; this field is the READ's own completeness, and a
                                      // `Held`/`Stale` row needs it just as badly — a hidden marker with a
                                      // lower id means the holder named above is the wrong holder, and a
                                      // hidden LIVE marker behind a lapsed one means the `STALE` a human is
                                      // reading before reaping is not free.
                                      Incomplete = scan.Unreadable }
                                )

                match failure with
                | Some e -> fail e
                | None ->
                    // #959: `--local` joins each claim to its local worktree by ITEM NUMBER — the key the
                    // branch name carries (`item/<n>-…`). Read ONCE for the whole set, off the local git; a
                    // held item with no worktree here is normal (its holder is elsewhere), so it stays `None`.
                    let localByItem =
                        if opts.Local then
                            // KEEP-FIRST on a duplicate item number, matching bash's `| first`. `Map.ofList`
                            // keeps LAST, so it is built over the reversed list — two worktrees on one item is
                            // rare (a leftover retry tree), but the two engines must name the same one.
                            localWorktrees () |> List.rev |> Map.ofList
                        else
                            Map.empty

                    let inFlight =
                        results
                        |> List.ofSeq
                        |> List.map (fun row ->
                            match Map.tryFind row.Ref.Number localByItem with
                            | Some path -> { row with Worktree = Some path }
                            | None -> row)

                    match opts.Render with
                    | Json ->
                        if List.isEmpty inFlight then
                            eprint $"fsgg-coord-engine: who scope: %s{scopeText} (empty result)"

                        printfn "%s" (renderWhoJson opts.Local inFlight)
                    | Text ->
                        if List.isEmpty inFlight then
                            printfn "nothing is in flight."
                        else
                            for row in inFlight do
                                // #959: under `--local`, each row names the worktree it is checked out in — the
                                // whole point of the flag. A row with no local worktree says so explicitly
                                // (`no local worktree`), because "held elsewhere" and "the join silently found
                                // nothing" are different facts to a human deciding whose tree to look in.
                                let wt =
                                    if opts.Local then
                                        match row.Worktree with
                                        | Some path -> $"  [worktree: %s{path}]"
                                        | None -> "  [no local worktree]"
                                    else
                                        ""

                                match row.State with
                                | Held m ->
                                    printfn
                                        "  %-16s held by %s  (%s)%s"
                                        row.Ref.Short
                                        m.Worker.Value
                                        (Schedulability.leaseWindow opts.LeaseMinutes m.AgeSeconds)
                                        wt
                                | Stale m ->
                                    // #581/#697: `STALE (#NNN OPEN — <what the PR says>)` when the work is
                                    // demonstrably alive, a bare `STALE` (which a reaper may collect)
                                    // otherwise. GREEN work says LAND IT — it is not an abandoned branch.
                                    let flag =
                                        match row.LivePr with
                                        | Some(pr, _) ->
                                            match row.PrState with
                                            | Some PrGreen -> $"STALE (#%d{pr} OPEN — GREEN: LAND IT)"
                                            | Some PrConflicted -> $"STALE (#%d{pr} OPEN — conflicted)"
                                            | Some PrPending -> $"STALE (#%d{pr} OPEN — checks running)"
                                            | Some PrRed -> $"STALE (#%d{pr} OPEN — not green)"
                                            // `LivePr` comes from the OPEN-PR read, so these two are all
                                            // but unreachable here — NOT structurally, though: the liveness
                                            // read and the landable read are two separate REST calls, and a
                                            // PR that merges between them lands right here. Sharing the
                                            // `STALE (#N OPEN)` wording is deliberate rather than ideal —
                                            // this is one cell of a human table, the row is stale either
                                            // way, and `who` is advisory. `landable` is where the merged
                                            // verdict is spoken precisely.
                                            | Some PrMerged
                                            | Some PrClosed
                                            | Some PrUnknown
                                            | None -> $"STALE (#%d{pr} OPEN)"
                                        // #1055: no PR, but a pushed `item/<n>-*` branch is proof of life
                                        // during §3 — say so, so a human sees WHY it is withheld rather than
                                        // a bare `STALE` that reads as reapable. `reap` refuses it, `who`
                                        // shows why. A truly dead claim (no branch) stays a bare `STALE`.
                                        | None when row.BranchPushed ->
                                            $"STALE (item/%d{row.Ref.Number}-* pushed — no PR yet)"
                                        | None -> "STALE"

                                    printfn
                                        "  %-16s held by %s  %s (%s)%s"
                                        row.Ref.Short
                                        m.Worker.Value
                                        flag
                                        (Schedulability.leaseWindow opts.LeaseMinutes m.AgeSeconds)
                                        wt
                                | Unclaimed ->
                                    printfn
                                        "  %-16s UNCLAIMED — In progress with NO claim marker%s"
                                        row.Ref.Short
                                        wt
                                // .github#1668. NOT a variant spelling of UNCLAIMED: this row is the verb
                                // declining to answer. The count goes in the line because "1 comment" and
                                // "all 40 comments" are very different situations to walk into, and the
                                // reasons follow on stderr where the rest of `who`'s diagnosis lives.
                                | Undetermined ->
                                    printfn
                                        "  %-16s UNDETERMINED — the marker read was INCOMPLETE (%d comment(s) unreadable); this item may be HELD%s"
                                        row.Ref.Short
                                        (List.length row.Incomplete)
                                        wt

                    // #697: FINISHED work behind a dead worker gets its OWN stderr lines, and they come
                    // FIRST — folding a green, mergeable PR into the generic "reap these" hint is how the
                    // best work on the board ends up in the bin. `reap` is a destructive verb, and pointing
                    // it at finished work is precisely the advice this change exists to delete: land it.
                    // .github#1668: AND ITS READ MUST HAVE BEEN COMPLETE. This block tells a human the claim
                    // is DEAD and the work is finished — `adopt` it. On a row whose marker read was short,
                    // "the claim is DEAD" is precisely the thing not established: the hidden comment may be
                    // a live marker sitting behind the lapsed one. The row still prints, and the warning
                    // below still names it; what is withheld is the instruction to take it over.
                    let orphans =
                        inFlight
                        |> List.filter (fun r ->
                            match r.State, r.PrState with
                            | Stale _, Some PrGreen -> List.isEmpty r.Incomplete
                            | _ -> false)

                    if not (List.isEmpty orphans) then
                        let refs = orphans |> List.map (fun r -> r.Ref.Short) |> String.concat ", "

                        eprint
                            $"fsgg-coord-engine: %s{refs} — the claim is DEAD but the PR is GREEN and MERGEABLE: that work is FINISHED."

                        eprint "fsgg-coord-engine:   Do NOT reap or close it. Land it:"

                        for r in orphans do
                            eprint $"fsgg-coord-engine:     scripts/fsgg-coord adopt %s{r.Ref.Short}"

                    // A markerless In-progress item is work happening OUTSIDE the protocol — warned on
                    // stderr (where case 20 looks for it) regardless of the stdout format, so a `who` piped
                    // to a machine consumer still shouts about the one thing no reconciler can fix by itself.
                    for row in inFlight do
                        match row.State with
                        | Unclaimed ->
                            eprint
                                $"fsgg-coord-engine: WARNING — %s{row.Ref.Short} is In progress with NO claim marker (someone is working outside the protocol)."
                        // .github#1668: THE ACCUSATION IS WITHHELD HERE, AND THAT IS THE POINT. "Someone is
                        // working outside the protocol" is a charge against a person, and on an incomplete
                        // read it was levelled at a worker who held a perfectly valid marker. The
                        // replacement is emitted by the loop BELOW, which is keyed on the read rather than
                        // on this verdict.
                        | Undetermined
                        | Held _
                        | Stale _ -> ()

                    // .github#1668 — THE INCOMPLETE-READ CAVEAT, KEYED ON THE READ AND NOT ON THE VERDICT.
                    //
                    // It fires on EVERY row whose marker read was short, in every state, because every state
                    // is compromised by a hidden marker and two of them are compromised in the direction
                    // that destroys work:
                    //
                    //   * `held`  — the CAS winner is the LOWEST id, so a marker we could not order may be
                    //               the real holder. The name we printed would then be the wrong one.
                    //   * `stale` — a hidden LIVE marker behind the lapsed one means this is not a dead
                    //               claim, and `stale` is the row a human reads immediately before `reap`.
                    //   * `undetermined` — no marker at all survived the read.
                    for row in inFlight do
                        if not (List.isEmpty row.Incomplete) then
                            // The state WORD, so the warning names the very verdict it is qualifying. Spelled
                            // here rather than reaching for `Render.whoStateName`, which is the JSON wire
                            // contract's vocabulary and not this stream's — cases 20/25 certify that one, and
                            // a human sentence must not be able to change it by needing a different word.
                            let state =
                                match row.State with
                                | Held _ -> "held"
                                | Stale _ -> "stale"
                                | Unclaimed -> "unclaimed"
                                | Undetermined -> "undetermined"

                            eprint
                                $"fsgg-coord-engine: WARNING — %s{row.Ref.Short}: the claim-marker read was INCOMPLETE, so its lock state (%s{state}) is a LOWER BOUND, not a fact."

                            for reason in row.Incomplete do
                                eprint $"fsgg-coord-engine:   - %s{reason}"

                            eprint
                                "fsgg-coord-engine:   Do NOT dispatch a worker, `reap`, or `adopt` on this row. Re-run `who`, and if it persists, read the issue's comments directly."


                    ExitGreen

    /// What undoing a claim does to the board column — the ONE question `release` and `reap` both ask (#331).
    ///
    /// Two verbs, one question, deliberately: they are the only two ways a claim goes away, and bash closed
    /// this split by making them share `unclaim_status`. The port re-opened it by giving each verb its own
    /// copy that consulted the marker alone. A fix that lands in one and not the other re-creates it.
    type private UnclaimColumn =
        /// The claim's own footprint is being undone — write the column it overwrote.
        | ResetTo of BoardStatus
        /// The column was chosen DURING the lease. Leave it exactly as it is, with NO write at all.
        | Preserve of BoardStatus option

    /// Decide from the item's LIVE column and the marker's recorded one. PURE — the read is the caller's, so
    /// the decision is testable without a board, and the failed-read case never reaches here at all.
    ///
    /// `claim` writes `In progress` (`CLAIM_STATUS`). Undoing a claim resets THAT — the claim's own footprint
    /// — and only that. **Any other column was chosen during the lease, deliberately**: a worker who hits a
    /// blocker and parks the item `Blocked` is following pnext-item §4's own prescribed sequence, and
    /// reverting it is #331 — the defect where `release` silently undid the column the protocol had just told
    /// the worker to set.
    ///
    /// The preserve arm writes **NOTHING**, rather than a redundant write of the column already there. That is
    /// not a micro-optimisation: a matching write would land the same end state while spending a GraphQL point
    /// on the budget that dies first under fan-out (#418), and — the part that actually bites — it would make
    /// "preserved" and "restored" indistinguishable to every test and every reader of the board's history. The
    /// absence of the write IS the observable that says the column was nobody's to change.
    let private unclaimColumn (live: BoardStatus option) (recorded: BoardStatus option) : UnclaimColumn =
        match live with
        | Some InProgress ->
            match recorded with
            // A recorded `In progress` is that same footprint written twice — still a column nobody chose, so
            // it falls back exactly as an unrecorded one does (#481).
            | Some InProgress
            | None -> ResetTo BoardStatus.Ready
            | Some s -> ResetTo s
        // ANY other column — including NO column at all, which has no footprint to reset either.
        | other -> Preserve other

    /// The one resolved-Status boundary for a `Blocked` write.  Callers may arrive through an
    /// explicit park, a recorded claim restore, or reconciliation; none may write the column until
    /// this verifies that the resulting row carries either a machine edge or a human sentinel.
    let requireCoherentParkIfBlocked (ctx: Context) (ref: Ref) (requested: BoardStatus option) : Result<unit, int> =
        requireCoherentBlockedWrite ctx ref requested

    /// #581 — collect expired claims whose WORK is dead, and REFUSE any whose work is alive.
    ///
    /// A lease is EVIDENCE of abandonment, never PROOF. Its false positive is SYSTEMATIC — work that simply
    /// outlasts its lease — and the reaper that breaks a lock on expiry alone collects the claims of workers
    /// who are visibly, demonstrably still working. So reap does not stop at "the lease lapsed": it looks for
    /// the item's own `item/<n>-*` PR, the worktree protocol's own server-side artifact, and REFUSES when one
    /// is open. That refusal is not an `if` here to forget — `Writes.reapable` is the only constructor of the
    /// capability `Writes.reap` consumes, so a live (or unreadable) claim cannot reach the delete at all.
    ///
    /// It scans the repo's OPEN ISSUES, not just the board's In-progress column: an abandoned claim's board
    /// Status is wherever the dead worker last left it — or nowhere, for a claim that never made the board
    /// (#461/#581). The scan fails CLOSED at every read (a marker set we could not read is not an empty one).
    ///
    /// `--apply` gates the destructive delete. The bare form is a DRY RUN that only reports what it WOULD
    /// collect, so breaking a lock is never the default — the operator opts into it.
    let reap (ctx: Context) (opts: Options) : int =
        match opts.Repo with
        | None ->
            eprint
                "fsgg-coord-engine: reap: --repo required (no git remote here, so the repo to reap is undefined)."

            ExitError
        | Some repoName ->

        // The board map, for the post-reap column restore. BEST-EFFORT: reap has already broken the lock by
        // the time it restores, so a board it cannot resolve leaves the column alone and reports it, rather
        // than a failure that would strand the freed item.
        //
        // LAZY, and #418 is why: a DRY RUN performs no restore, so it must not pay `bootstrap`'s two GraphQL
        // points on the budget that dies first for a board it will never write. Resolving on first actual
        // reset keeps the dry run — the form an operator runs to LOOK before deciding — free.
        let board = lazy (Board.bootstrapCached ctx.Transport ctx.Owner ctx.Title)

        match Reads.openIssues ctx.Transport ctx.Owner repoName with
        | Error e -> fail e
        | Ok issues ->
            let mutable failure: Errors.IoError option = None

            // `reap` needs the NUMBER only: its subject is the lock, and the lock is a comment. The body
            // rides along free and is not consulted, so its readability cannot change a reap decision.
            for { Reads.OpenIssue.Number = number } in issues do
                if failure.IsNone then
                    let ref =
                        { Owner = ctx.Owner
                          Repo = repoName
                          Number = number }

                    // FAIL CLOSED (#461): a claim set we could not read is never an empty one.
                    match
                        Reads.markerScan ctx.Transport ref.Owner ref.Repo ref.Number
                        |> Result.bind (Reads.requireCompleteMarkerScan ref.Short)
                    with
                    | Error e -> failure <- Some e
                    | Ok markers ->
                        // A live winner is a claim reap may not touch. Only when NO winner is live but a
                        // marker exists is the lowest-id marker a stale lock — reap's one candidate per item.
                        match Reads.winner opts.LeaseMinutes markers with
                        | Some _ -> ()
                        | None ->
                            // `Reads.lowestId`, NOT `Reads.reserver`. `reap` acts ONLY when no live winner
                            // exists — the `Some _` arm above does nothing — so substituting `reserver`
                            // here would hand `reap` the live holder and break a lock somebody is standing
                            // in. This is design §4.2's second constraint, in the one path where getting it
                            // wrong is worst.
                            match Reads.lowestId markers with
                            | None -> ()
                            | Some marker ->
                                // #581: the lease lapsed — now ask whether the WORK did.
                                match Reads.prAlive ctx.Transport ref.Owner ref.Repo ref.Number with
                                | Error e -> failure <- Some e
                                | Ok liveness ->
                                    match Writes.reapable ref marker liveness with
                                    | Error(Writes.WorkAlive pr) ->
                                        // #697: refusing on the PR's mere EXISTENCE is right (#581), but the
                                        // remedy that used to follow — "close it, then reap" — is a loaded
                                        // gun pointed at the best work on the board. Read WHAT the PR says
                                        // and tell the states apart: only ever advise closing the one that is
                                        // genuinely abandoned (red/conflicted). The verdict is advisory — a
                                        // `PrUnknown` chooses the "look yourself" wording, never a delete.
                                        let idleM = marker.AgeSeconds / 60
                                        let w = marker.Worker.Value

                                        match Reads.prLandable ctx.Transport ref.Owner ref.Repo pr with
                                        | PrGreen ->
                                            eprint
                                                $"fsgg-coord-engine: REFUSING to reap %s{ref.Short} — worker %s{w} (idle %d{idleM}m), PR #%d{pr} is OPEN, GREEN and MERGEABLE."

                                            eprint
                                                "fsgg-coord-engine:   This work is FINISHED — the worker died between \"green\" and \"merge\", the window this protocol leaves open on every item. Do NOT close it: that destroys a reviewed, passing fix. LAND IT:"

                                            eprint $"fsgg-coord-engine:       scripts/fsgg-coord adopt %s{ref.Short}"
                                        | PrPending ->
                                            eprint
                                                $"fsgg-coord-engine: REFUSING to reap %s{ref.Short} — worker %s{w} (idle %d{idleM}m), PR #%d{pr} is OPEN (checks running). The lease lapsed; the WORK did not."

                                            eprint
                                                "fsgg-coord-engine:   Its checks are STILL RUNNING — it is UNFINISHED, not abandoned, and may be minutes from green. Do NOT close it. Let CI settle and look again:"

                                            eprint
                                                $"fsgg-coord-engine:       scripts/fsgg-coord who --repo %s{repoName}        # green? then: scripts/fsgg-coord adopt %s{ref.Short}"
                                        | PrUnknown ->
                                            eprint
                                                $"fsgg-coord-engine: REFUSING to reap %s{ref.Short} — worker %s{w} (idle %d{idleM}m), PR #%d{pr} is OPEN (state unknown). The lease lapsed; the WORK did not."

                                            eprint
                                                $"fsgg-coord-engine:   Its state could NOT be determined (rate limit? network?). Do NOT close it on a guess — look at PR #%d{pr} yourself before deciding anything."
                                        | (PrMerged | PrClosed) as verdict ->
                                            // Structurally unreachable: this arm is reached through
                                            // `Writes.WorkAlivePr`, which names an OPEN PR. Handled anyway,
                                            // and handled SAFELY — a merged PR is finished work, so the one
                                            // thing this must never do is advise closing it.
                                            eprint
                                                $"fsgg-coord-engine: REFUSING to reap %s{ref.Short} — worker %s{w} (idle %d{idleM}m), PR #%d{pr} is %s{Landable.name verdict}, not open."

                                            eprint
                                                $"fsgg-coord-engine:   The claim outlived its PR. Do NOT close anything — look at PR #%d{pr} and, if it MERGED, stamp the item: scripts/fsgg-coord done %s{ref.Short} --flip --pr %d{pr}"
                                        | (PrRed | PrConflicted) as verdict ->
                                            // The one genuinely-abandoned case — and the ONLY one that may
                                            // advise closing. A conflicted or red PR is not finished work.
                                            eprint
                                                $"fsgg-coord-engine: REFUSING to reap %s{ref.Short} — worker %s{w} (idle %d{idleM}m), PR #%d{pr} is OPEN (%s{Landable.name verdict}). The lease lapsed; the WORK did not."

                                            eprint
                                                $"fsgg-coord-engine:   It is %s{Landable.name verdict}, so there is nothing to land as it stands (`adopt` only lands green, mergeable work). If the PR really is abandoned, close it, then reap."
                                    | Error Writes.WorkAliveBranch ->
                                        // #1055: no PR yet, but a pushed `item/<n>-*` branch — proof of life
                                        // during §3, before §5 opens the PR. There is nothing to `adopt` (a
                                        // branch is not a landable PR), so this refuses without the land/close
                                        // advice: the worker is likely still writing, or a REST outage expired
                                        // the lease mid-work and they have not re-claimed yet.
                                        let idleM = marker.AgeSeconds / 60
                                        let w = marker.Worker.Value

                                        eprint
                                            $"fsgg-coord-engine: REFUSING to reap %s{ref.Short} — worker %s{w} (idle %d{idleM}m), a pushed item/%d{ref.Number}-* branch has NO PR yet. The lease lapsed; the WORK did not (#1055/#581)."

                                        eprint
                                            "fsgg-coord-engine:   A branch with no PR is work IN PROGRESS, not an abandoned one — the worker may be mid-build, or a REST outage expired the lease before they opened the PR. Nothing to adopt (there is no PR to land). Leave it: they re-claim, or push the PR."
                                    | Error(Writes.Undetermined why) ->
                                        eprint
                                            $"fsgg-coord-engine: NOT reaping %s{ref.Short} — %s{why}; a lock we cannot rule dead we may not break."
                                    | Ok reapable ->
                                        if not opts.Apply then
                                            // DRY RUN — say what --apply would collect, and touch nothing.
                                            printfn "would reap  %s  worker %s" ref.Short marker.Worker.Value
                                        else
                                            match Writes.reap ctx.Transport opts.LeaseMinutes reapable with
                                            | Error e ->
                                                // A FAILED DELETE IS REPORTED, NOT SWALLOWED, and the scan
                                                // moves on to the next item. The marker is still there, so the
                                                // item is still HELD — the board is left untouched and the
                                                // worker is NOT told it was released. `reap` deletes BEFORE it
                                                // would ever notify (and this engine's reap posts no notify at
                                                // all): a notify ahead of a failed delete would tell a worker
                                                // to stop while its marker still holds the item for a full
                                                // lease — released to its owner, held against everyone else,
                                                // and nothing clears it. One failed collect is not fatal to
                                                // the whole reap; the other items still collect.
                                                eprint
                                                    $"fsgg-coord-engine: FAILED  %s{ref.Short}  worker %s{marker.Worker.Value}  — could not remove the marker (%s{Errors.explain e}); board left untouched, worker not notified."
                                            | Ok(Writes.RenewedSinceScan ageSeconds) ->
                                                // The holder HEARTBEATED between the scan and this delete: the
                                                // lock is live again, so reap SKIPS it rather than break a
                                                // lease that was renewed under it — the one way reap could
                                                // itself cause the double-hold it exists to clean up.
                                                printfn
                                                    "skipped  %s  worker %s  — renewed since the scan (%dm), still alive"
                                                    ref.Short
                                                    marker.Worker.Value
                                                    (ageSeconds / 60)
                                            | Ok Writes.AlreadyGone ->
                                                // A peer collected the same stale marker first — nothing left
                                                // to break, which is a collector's goal state, not a failure.
                                                printfn
                                                    "skipped  %s  worker %s  — marker already gone"
                                                    ref.Short
                                                    marker.Worker.Value
                                            | Ok Writes.Reaped ->
                                                printfn "reaped  %s  worker %s" ref.Short marker.Worker.Value

                                                // Restore the freed column — best-effort, the lock is already
                                                // gone. An OFF-BOARD claim has no board item to reset, and reap
                                                // must not claim a reset it never performed (case 25).
                                                match board.Value with
                                                | Ok bm ->
                                                    match
                                                        Board.itemIdCached ctx.Transport bm ref.Owner ref.Repo ref.Number
                                                    with
                                                    | Ok(Some _) ->
                                                        // #331's read, in `reap`'s copy — because the reaper
                                                        // collects a LEASE and knows nothing about whether the
                                                        // item became startable. A worker whose lease lapsed on
                                                        // an item it had deliberately marked `Blocked` had that
                                                        // column reset on its way out, which is #331 with a
                                                        // dead worker instead of a live one. bash asked ONE
                                                        // question here (`unclaim_status`); so does this.
                                                        match
                                                            Board.itemStatus ctx.Transport bm ref.Owner ref.Repo ref.Number
                                                        with
                                                        // A column we could not read is not one we may
                                                        // overwrite (#266, aimed at a writer). Never fatal —
                                                        // the lock is already gone.
                                                        | Error e ->
                                                            printfn
                                                                "  column UNREADABLE (%s) — marker cleared, column left ALONE:  scripts/fsgg-coord set-field %s Status '<column>'"
                                                                (Errors.explain e)
                                                                ref.Short
                                                        | Ok live ->

                                                        match unclaimColumn live reapable.PreviousStatus with
                                                        | Preserve(Some s) ->
                                                            printfn
                                                                "  column left at %s (chosen during the lease — reap collects a lease, not a decision)"
                                                                (statusWireName s)
                                                        | Preserve None -> printfn "  no column set (nothing to reset)"
                                                        | ResetTo restoreTo ->

                                                        let name = statusWireName restoreTo

                                                        if name <> "" then
                                                            // #867: `release`'s defect, in `reap`'s copy —
                                                            // the outcome was discarded, so "best-effort"
                                                            // meant "unmentioned". Case 25's own rule is that
                                                            // reap must not claim a reset it never performed;
                                                            // a silent `Deferred` or failure claims exactly
                                                            // that, by saying nothing. Still never fatal: the
                                                            // lock is already gone.
                                                            match requireCoherentParkIfBlocked ctx ref (Some restoreTo) with
                                                            | Error _ ->
                                                                printfn "  reset to %s REFUSED — the restored Blocked column has no coherent reason" name
                                                            | Ok() ->
                                                                match
                                                                    Board.boardWrite
                                                                        ctx.Transport
                                                                        bm
                                                                        ref.Owner
                                                                        ref.Repo
                                                                        ref.Number
                                                                        "Status"
                                                                        (Board.Set name)
                                                                        marker.Worker.Value
                                                                with
                                                                | Ok Board.Written -> printfn "  reset to %s" name
                                                                | Ok Board.Deferred ->
                                                                    printfn
                                                                        "  reset to %s DEFERRED (budget exhausted) — queued, not lost; nothing replays it on its own:  scripts/fsgg-coord flush"
                                                                        name
                                                                | Ok Board.NotOnBoard ->
                                                                    printfn "  not on board (marker cleared; nothing to reset)"
                                                                | Error e ->
                                                                    printfn
                                                                        "  reset to %s FAILED (%s) — marker cleared, column UNCHANGED:  scripts/fsgg-coord set-field %s Status '%s'"
                                                                        name
                                                                        (Errors.explain e)
                                                                        ref.Short
                                                                        name
                                                    | Ok None ->
                                                        printfn "  not on board (marker cleared; nothing to reset)"
                                                    | Error _ -> ()
                                                | Error _ -> ()

            match failure with
            | Some e -> fail e
            | None -> ExitGreen

    /// `pendingBoardWrites` is the DEPTH OF THE DEFERRAL QUEUE — the writes `boardWrite` took on an
    /// exhausted budget and `flush` will replay (#862). It reads a local file and spends nothing, which is
    /// the point: the moment you most need to ask "did my stamp land?" is the moment you have no budget to
    /// ask with.
    ///
    /// A queue we could not READ is `None`, never `0`. They are opposite answers — "nothing is waiting" vs
    /// "I cannot tell you what is waiting" — and rendering the second as the first is how a worker concludes
    /// their `done --flip` was dropped when it is sitting in the queue, or the reverse (#266).
    let budget (ctx: Context) (opts: Options) : int =
        match Reads.rateLimit ctx.Transport with
        | Error e -> fail e
        | Ok meter ->
            let rests =
                match Environment.GetEnvironmentVariable "GITHUB_TOKEN" with
                | null
                | "" -> []
                | token -> Budget.readRestObservations token

            let fleetState = Budget.fleetState rests |> Budget.fleetStateText

            let pending =
                match Cache.pending () with
                | Ok entries -> Some(List.length entries)
                | Error _ -> None

            match opts.Render with
            | Json ->
                // `source` and `restReported` are ADDITIVE (#1666). The object carried a `graphql` figure
                // and nothing saying where it came from or what was missing, so a JSON consumer had exactly
                // the ambiguity the text line had: no way to tell this is one bucket of GitHub's own
                // accounting rather than the account's whole picture. `restReported: false` is the
                // machine-readable form of "do not conclude REST is healthy from this".
                let doc =
                    JsonSerializer.Serialize(
                        {| graphql =
                            {| remaining = meter.Remaining
                               limit = meter.Limit
                               source = "github:/rate_limit" |}
                           restReported = not rests.IsEmpty
                           rest = rests
                           fleetState = fleetState
                           pendingBoardWrites = pending |}
                    )

                printfn "%s" doc
            | Text ->
                // SAY WHOSE BUDGET THIS IS, AND WHAT IT DOES NOT COVER (#1666). This line used to read
                // "GraphQL budget: N / 5000 remaining", and a board driver reasonably took it for the
                // account's whole rate-limit picture — then, holding a REST refusal in one hand and this
                // healthy-looking number in the other, concluded the engine was tripping a counter of its
                // own. It reports ONE bucket, read from GitHub's own free `/rate_limit`, and the claim lock
                // does not live in it. Neither does a secondary limit, which never appears there at all.
                printfn "GitHub GraphQL points (from GitHub's own /rate_limit): %d / %d remaining" meter.Remaining meter.Limit

                match rests with
                | [] ->
                    printfn "REST resource telemetry: unknown (no real-resource header observation yet); fleet unknown for new dispatch."
                | observations ->
                    for observation in observations |> List.sortBy _.Resource do
                        let remaining = observation.Remaining |> Option.map string |> Option.defaultValue "unknown"
                        let limit = observation.Limit |> Option.map string |> Option.defaultValue "unknown"
                        let used = observation.Used |> Option.map string |> Option.defaultValue "unknown"
                        let reset = observation.ResetAt |> Option.map (fun instant -> instant.ToString "o") |> Option.defaultValue "unknown"
                        printfn "REST %s (real response headers): %s / %s remaining; used %s; reset %s; observed %s; source %s; fleet %s" observation.Resource remaining limit used reset (observation.ObservedAt.ToString "o") observation.Source fleetState

                printfn
                    "REST requests: NOT REPORTED here — /rate_limit's `core` figure disagrees with the counter real requests are billed against on this account, and a SECONDARY (abuse-detection) limit never appears in it. The claim lock lives on REST (ADR-0034 §3), so a healthy line above is not evidence that `claim`/`take`/`who` will run."

                match pending with
                | Some 0 -> printfn "pending board writes: 0"
                | Some n -> printfn "pending board writes: %d — replay them with `flush` (#862)" n
                | None -> printfn "pending board writes: UNKNOWN — the deferral queue could not be read"

                // WHO SPENT IT (#2418). The meter above says how much is left; it has never said what took
                // it. When the budget died twice inside one board run, the drain could not be attributed —
                // not from the meter, and not from reading the engine either, because the one function that
                // parsed `rateLimit { cost }` had no caller. This is that answer, and it is a MEASUREMENT:
                // every row is GitHub's own `cost`, summed per command, never our estimate of one.
                let window = TimeSpan.FromHours 1.0

                match Budget.recentSpend window with
                | [] ->
                    printfn
                        "GraphQL spend (last hour): no attribution recorded — no invocation has billed a GraphQL call since the ledger was last written. This is 'nothing measured', NOT 'nothing spent': a fleet whose engine predates #2418 records nothing at all."
                | records ->
                    let byCommand = Budget.spendByCommand records
                    let total = records |> List.sumBy (fun r -> r.Points)
                    let calls = records |> List.sumBy (fun r -> r.Calls)

                    printfn
                        "GraphQL spend (last hour): %d point(s) over %d billed call(s), %d invocation(s) — dearest first:"
                        total
                        calls
                        (List.length records)

                    for command, points, callCount in byCommand |> List.truncate 10 do
                        printfn "  %-16s %5d pt  %4d call(s)" command points callCount

                    // MUTATIONS ARE MISSING FROM THIS TOTAL, and saying so is the point. `rateLimit` is a
                    // field of the query root, so a mutation cannot carry it: every `set-field` write is
                    // billed the 1-point floor by GitHub and reported here as nothing. A total presented as
                    // complete would be a confident number with a known hole in it.
                    printfn
                        "  (queries only — a mutation carries no `rateLimit`, so board WRITES are billed the 1-pt floor and are not counted above)"

            if meter.Remaining < Budget.WarnBelow then
                eprint $"fsgg-coord-engine: WARNING — only %d{meter.Remaining} GraphQL points remain (< %d{Budget.WarnBelow}); the fleet shares one 5,000/hr budget (#418)."

            // A QUEUE WITH ENTRIES IN IT IS NOT AN ERROR — it is the state `flush` exists for — so this
            // stays green and merely says so. Exiting non-zero here would make `budget`, the one free
            // pre-flight read the recipes tell you to START with, fail on a board that is merely mid-repair.
            match pending with
            | Some n when n > 0 ->
                eprint $"fsgg-coord-engine: NOTE — %d{n} board write(s) are queued and have NOT landed; `flush` replays them (#862)."
            | _ -> ()

            ExitGreen

    // ---- the lock lifecycle ----------------------------------------------------------------------------

    /// #516 — at most ONE item per worker. The CAS is keyed on the ITEM, so it guarantees at most one
    /// worker per item; NOTHING guaranteed the converse, and the cost model assumes it. A second,
    /// unattended claim RESERVES A TOUCH-SET on files nobody is editing for the whole lease, and `batch`
    /// then refuses every item that overlaps it. This scans the TARGET repo's in-flight items for a live
    /// claim held by THIS worker on a DIFFERENT item, returning the ones they already hold.
    ///
    /// It rides the 90s scan cache (`Cache.Scheduling`), exactly as bash's guard rides `CACHED=1`: under
    /// `take` the board scan is already paid, and a bare `claim` rides the window like `next` — paying a
    /// fresh board scan per claim is the burn #418 exists to stop. A stale-by-90s set cannot cause a
    /// double-hold: it can only miss OUR OWN very recent second claim, and the item's own CAS still holds.
    /// A held item's markers are read fresh and complete (`markerScan` plus
    /// `requireCompleteMarkerScan` — the lock is never cached), and `winner` applies the lease, so a
    /// lapsed claim of ours does not count.
    let private heldElsewhere (ctx: Context) (leaseMinutes: int) (workerId: string) (ref: Ref) =
        match Board.bootstrapCached ctx.Transport ctx.Owner ctx.Title with
        | Error e -> Error e
        | Ok board ->
            match Scan.board ctx.Transport Cache.Scheduling ctx.Owner ctx.Title board.Number with
            | Error e -> Error e
            | Ok rows ->
                let inFlight =
                    rows
                    |> List.filter (fun r ->
                        not r.IsPullRequest
                        && r.Status = InProgress
                        && r.Ref.Number <> ref.Number
                        // repo-filter-monopoly: exempt — REF-to-REF, not a `--repo` filter. This asks
                        // "which other in-flight items live in THIS ref's repo?", a question `--repo`
                        // has no part in: `opts.Repo` is not read here and scoping it would be wrong.
                        && String.Equals(r.Ref.Repo, ref.Repo, StringComparison.OrdinalIgnoreCase)
                        && String.Equals(r.Ref.Owner, ref.Owner, StringComparison.OrdinalIgnoreCase))

                let rec scan acc rows =
                    match rows with
                    | [] -> Ok(List.rev acc)
                    | (row: Scan.Row) :: rest ->
                        match
                            Reads.markerScan ctx.Transport row.Ref.Owner row.Ref.Repo row.Ref.Number
                            |> Result.bind (Reads.requireCompleteMarkerScan row.Ref.Short)
                        with
                        | Error e -> Error e
                        | Ok markers ->
                            match Reads.winner leaseMinutes markers with
                            | Some m when m.Worker.Value = workerId -> scan (row.Ref.Short :: acc) rest
                            | _ -> scan acc rest

                scan [] inFlight

    /// `cross-repo` IS NOT A REPOSITORY (#2351). `docs/coordination/board-schema.md` names it as the
    /// **one deliberate non-roster value** the `Repo Scope` field carries — every other option is a
    /// same-named `registry/repos.yml` row (`Options.resolveRepo`'s embedded roster). A caller that
    /// takes a `Repo Scope` string and hands it straight to `Option.defaultValue` cannot tell those two
    /// cases apart: `Option.defaultValue` only ever declines a `None`, and a `Some "cross-repo"` is a
    /// `Some`. That is exactly how two items in the SAME repository, differing only in which of them
    /// carries the sentinel, went DISJOINT-by-construction — the sentinel string substituted for a real
    /// repository on one side of the comparison and could never equal the other side's genuine repo
    /// name. This is the ONE place that gets settled (AC5): a scope that does not name a repository
    /// behaves exactly like an ABSENT scope and falls back to `fallback` — normally the ref's own
    /// hosting repository, the same repository its `Paths:` tokens are read against by
    /// `TouchSet.parse`/`Reads.issueBody`. Every caller that turns a raw `Repo Scope`/marker `PathRepo`
    /// value into the repository used for touch-set comparison MUST route through this, never through a
    /// bare `Option.defaultValue`.
    let private pathRepoOrFallback (fallback: string) (scope: string option) : string =
        match scope with
        | Some s when not (String.Equals(s, "cross-repo", StringComparison.OrdinalIgnoreCase)) -> s
        | _ -> fallback

    /// Resolve the repository a board item's paths name without changing the issue ref used for reads
    /// and mutations. Off-board items have no `Repo Scope` projection and therefore retain their own
    /// repository as the only truthful scope.
    let private boardPathScopes (ctx: Context) : Errors.IoResult<Map<Ref, string>> =
        match Board.bootstrapCached ctx.Transport ctx.Owner ctx.Title with
        | Error e -> Error e
        | Ok board ->
            match Scan.board ctx.Transport Cache.Scheduling ctx.Owner ctx.Title board.Number with
            | Error e -> Error e
            | Ok rows ->
                rows
                // `RepoScope.orFallback` (#2398): a `cross-repo` Repo Scope names no repository, so it
                // falls back to the row's own hosting repository — the doc above's "retain their own
                // repository as the only truthful scope," now the same fallback `enrich`/`Lanes.partition`
                // apply, rather than a raw resolve that could hand back the sentinel itself.
                |> List.map (fun r -> r.Ref, FS.GG.Coord.RepoScope.orFallback r.Ref.Repo (Options.resolveRepo r.PathRepo))
                |> Map.ofList
                |> Ok

    // #353 — two refs name the same repo. `Paths:` tokens are repo-relative, so a touch-set comparison
    // is only meaningful within a repo; both `overlap` and `widen`'s re-check narrow the live set to this.
    let private sameRepo (a: Ref) (b: Ref) =
        String.Equals(a.Owner, b.Owner, StringComparison.OrdinalIgnoreCase)
        && String.Equals(a.Repo, b.Repo, StringComparison.OrdinalIgnoreCase)

    // The tokens a collision is named IN — the stem (`src/Scene/**` and `src/Scene` are one subtree, so
    // the raw suffix beside a reservation that has none reads as two different things), deduped.
    //
    // #1517 — this returns the LIST and the human sites join it, rather than returning the joined string
    // and leaving a machine consumer to split it back apart. `widen --json` emits these stems, and a
    // comma-joined string beside a sibling `paths` ARRAY in the same object is a shape formatted for the
    // stderr line it used to have only one caller for.
    let private sharedTokens (pairs: (string * string) list) : string list =
        pairs
        |> List.collect (fun (a, b) -> [ TouchSet.stem a; TouchSet.stem b ])
        |> List.distinct

    let private sharedTokenText (tokens: string list) = String.Join(", ", tokens)

    /// The #353 collision scan, shared by `overlap --active` and `widen`'s re-check: given an item's ref
    /// and touch-set, every LIVE claim in the SAME repo whose touch-set collides with it, named with its
    /// holder and the shared token stems. The repo scope IS the fix — a same-named repo-relative token in
    /// another repo names a different file, so it is never a collision and its holder is never named. A
    /// claim we could not read propagates as an Error: a claim we could not check is never a silent DISJOINT.
    ///
    /// ---- THE CANDIDATE SET IS KEYED ON THE **LOCK**, NEVER ON A PROJECTION OF IT (.github#1779) --------
    ///
    /// A touch-set reservation is carried by a CLAIM MARKER — a comment, posted by `claim`'s CAS. The
    /// board's `Status` column is a PROJECTION of that marker, written afterwards, over a different
    /// transport, on a different budget. Every way those two can disagree is a way this scan can print
    /// `DISJOINT` over a live reservation, and **there is no CAS on a file**: nothing downstream re-decides
    /// a false `DISJOINT`, nothing detects it, and the cost is two workers editing one file for as long as
    /// they both work. `heldElsewhere`'s warrant — *"staleness costs a retry, not a double-claim"* — is
    /// about the ITEM CAS and does not reach this consumer; #1740 is what borrowing it cost.
    ///
    /// `claim`'s own receipt enumerates the disagreements, and it exits GREEN on all four —
    /// `converged:false` is a report, not a refusal. A column-derived candidate set misses the last three:
    ///
    /// | `statusWrite` | the column says | closed by a column-derived scan? |
    /// |---|---|---|
    /// | `written`, read fresh   | `In progress`     | yes |
    /// | `written`, read stale   | the pre-claim column | only by a freshness tier (#1740 cause 1) |
    /// | `deferred`              | the pre-claim column, until somebody runs `flush` | only by reading the deferral queue (#1740 cause 2) |
    /// | `failed`                | the pre-claim column, **forever** — #510 never queues a permanent failure, because a write replayed forever is a promise nobody can keep | **no** |
    /// | `not-on-board`          | nothing: there is no row | **no, by construction** |
    ///
    /// So the candidate set is not derived from the board at all. `Reads.openIssues` lists the repo's open
    /// issues WITH their bodies in one paginated, unconditional call; the `Paths:` tokens are compared
    /// PURELY; and a marker is read only for a row whose tokens actually collide. The column is never
    /// consulted, so none of the four rows above can hide behind it, and the two `#1740` closed are closed
    /// again here by a mechanism that does not depend on a cache tier or on a local queue file.
    ///
    /// **THE SWEEP IS THE SCHEDULER'S OWN, NOT A NEW ONE.** `Scan.snapshot` takes
    /// `candidates = scoped.Rows` — every row, with NO column filter — reads body and markers for each OPEN
    /// one, and then sweeps `Reads.openIssues` for *"a claim on an issue whose column flip failed (the board
    /// says Ready, the lock says held), or on one that never reached the board at all"*. That is
    /// `take`/`next`/`batch`, on every scheduling poll. So this reads what the scheduler already reads, on
    /// a verb run far less often, which is what #353 was reaching for when it repo-scoped this call: a
    /// `widen` that disagrees with `take` about who holds what is incoherent whichever way it errs.
    ///
    /// **THAT IS NOT THE SAME AS "THE SAME UNIVERSE", AND AN EARLIER DRAFT OF THIS COMMENT SAID IT WAS.**
    /// Two differences survive, both listed under #266 below: `Scan.snapshot` reserves a stale-but-unreaped
    /// marker (`reserver`, not `winner`) and it reserves a MARKERLESS `In progress` row as `RUnowned`. The
    /// second is column-derived by construction, so a scan that never reads the column cannot reproduce it,
    /// and no amount of care here would. Both predate this change; neither is fixed by it.
    ///
    /// **AND IT IS CHEAPER THAN THE COLUMN-DERIVED SCAN IT REPLACES — MEASURED, NOT ESTIMATED** (#1086 got
    /// this same trade wrong by an order of magnitude by estimating; the first draft of `.github#1779` got
    /// it wrong the other way, filing "~74 REST marker reads per `widen`" and declining the work over it).
    /// Measured on the live Coordination board, 2026-07-28 — 74 open issues in `FS-GG/.github`, five rows
    /// `In progress` — by reading the GraphQL budget either side of one `overlap .github#1688 --active`
    /// (`budget` itself moves the counter by 0, so the whole delta is the scan):
    ///
    ///   | | GraphQL points, of 5,000/hr | REST calls |
    ///   |---|---|---|
    ///   | BEFORE — board scan + column filter | **24, 27, 31** | 1 issue body + 1 marker per `In progress` row |
    ///   | AFTER  — `openIssues` + token filter | **0** | **2**: one issue body, one open-issue list |
    ///
    /// The GraphQL cost goes to **zero**: the board query, and `Board.bootstrapCached` behind it, are gone
    /// from this path entirely. What replaces them is one REST list read whose bodies `Reads.openIssues`
    /// states are *"free here"*, plus one marker read PER COLLIDING ROW — in the incident that produced
    /// #1740, exactly one; on the measurement above, zero, because nothing collided. The old scan's marker
    /// reads were per `In progress` row **whether or not its tokens could ever collide**, so on a busy
    /// board this strictly shrinks the REST cost too.
    ///
    /// The upper bound is the repo's open-issue count, reached when a declaration collides with every other
    /// one — the same number `Scan.snapshot` already pays on every poll. **It is NOT reached by a
    /// `Paths: any` chore lock, though an earlier draft of this comment said so.** `TouchSet.parse` maps
    /// `any` to `DeclaredChore` and `TouchSet.conflicts` answers `[]` for it in either direction (#1103 leg
    /// 8: *"a chore reserves nothing, so it conflicts with nothing"*), so a chore lock is the CHEAPEST row
    /// here, not the most expensive. Worth stating, because the wrong version of that sentence also implies
    /// `widen` would report a collision against an `any` chore, and it never does.
    ///
    /// **WHAT THIS DOES NOT REACH, NAMED RATHER THAN GLOSSED (#266).**
    ///
    /// - ~~A claim on a CLOSED issue.~~ **CLOSED by `.github#2250`.** `openIssues` remains open-only, but
    ///   `activeCollisions` now unions its token-filtered candidates with same-repo board rows that are
    ///   CLOSED and not `Done`, then reads each such body's declared paths before it reads a matching
    ///   marker. A closed-but-unstamped post-merge holder therefore reserves on both this gate and
    ///   `Scan.snapshot`; `overlap --active`, `widen`, `batch`, and `take` name the same holder rather than
    ///   disagreeing.
    ///
    ///   The cost is deliberate and measured: a cold gate pays the cached board universe (bootstrap's two
    ///   resolver queries plus one board page) and one REST body read per closed, unstamped same-repo row.
    ///   It still token-shortlists before every neighbour marker read, so it does not reintroduce the old
    ///   per-In-progress marker sweep. `ApplicationServiceTests` and `coord-engine-e2e/writes.sh` pin both
    ///   the cross-route result and this 3-GraphQL cold cost.
    ///
    ///   The old citation to `#520` stays absent: `#2225` moved that fixture from `Ready` to `Done`, which
    ///   falsified the licence that had cited it. The replacement assertion drives one CLOSED, unstamped
    ///   holder across BOTH production surfaces, so a future candidate-universe drift turns red.
    /// - A marker that is **not yet visible** to a reader that just posted it. That is `.github#1668`, it
    ///   would defeat any marker-keyed scan, and it is explicitly not absorbed here.
    /// - A **stale-but-unreaped** claim. `winner` applies the lease, so a lapsed claim reserves nothing
    ///   here, while `Scan.snapshot` reserves it via `reserver` (*"a lease is a clock; a lock is broken
    ///   only by `reap`"*). So the scheduler and this gate can give opposite answers about one marker.
    ///   That divergence predates this change and is neither introduced nor fixed by it — it is about
    ///   which MARKERS count, where this is about which ROWS are looked at. Filed as `.github#1792`.
    /// - A **MARKERLESS row the board calls `In progress`**. `Scan.snapshot` reserves it as `RUnowned` —
    ///   *"something is evidently editing those files"* — and that reservation is read off the COLUMN, so
    ///   a scan that never reads the column cannot reproduce it by construction. `take` will therefore
    ///   refuse to schedule against a surface this gate calls DISJOINT. The old scan here did not reserve
    ///   it either (it required a `winner`), so this is not a regression; it is the same "which markers
    ///   count" question as the row above, and it is on `.github#1792`.
    /// - ~~An issue whose list entry is **malformed**.~~ **CLOSED by `.github#1794`**, and it is the one
    ///   residual on this list that was a fail-OPEN rather than a divergence. `Reads.openIssues` used to
    ///   drop an element with no numeric `number` and read an absent/ill-typed `body` as `""` — which
    ///   parses to `Undeclared`, a confident *"declares nothing"* about a row nobody could read. It now
    ///   refuses the whole read for the first and returns `BodyUnread` for the second, and this scan gates
    ///   on it below.
    let private activeCollisions
        (ctx: Context)
        (opts: Options)
        (ref: Ref)
        (knownTargetPathRepo: string option)
        (ts: TouchSet)
        : Errors.IoResult<(Ref * string * string list) list> =
        // THE REPO SCOPE IS STRUCTURAL NOW, NOT A FILTER (#353). `openIssues` is keyed on this item's own
        // owner/repo, so a token in another repo is never even read — where the old scan pulled the whole
        // board and filtered with `sameRepo`, one edit away from the phantom collisions #353 removed.
        // PRs are dropped inside `openIssues` (#641), so `IsPullRequest` needs no analogue here.
        let targetPathRepo =
            match knownTargetPathRepo with
            | Some pathRepo -> Ok pathRepo
            | None ->
                Reads.markerScan ctx.Transport ref.Owner ref.Repo ref.Number
                |> Result.bind (Reads.requireCompleteMarkerScan ref.Short)
                |> Result.map (fun markers ->
                    Reads.reserver opts.LeaseMinutes markers
                    |> Option.bind (fun marker -> marker.PathRepo)
                    // #2351 — `cross-repo` is not a repository; see `pathRepoOrFallback`.
                    |> pathRepoOrFallback ref.Repo)

        // .github#2250 — `openIssues` is necessarily OPEN-only, but a CLOSED item is not terminal until
        // its green Done stamp exists.  The scheduler already reads a closed-but-unstamped board row and
        // lets its marker reserve paths during that post-merge window.  This gate must use that same
        // candidate universe: a successful `widen` / `overlap --active` is final evidence workers act on,
        // not an advisory that may disagree with `take`.
        //
        // COST, MADE EXPLICIT.  This adds the scheduler's cached Projects scan (normally one GraphQL page;
        // the map bootstrap is day-cached) plus one REST body read per closed, unstamped row in THIS repo.
        // It deliberately does not reinstate a per-In-progress-row marker sweep: tokens still shortlist
        // candidates before their marker is read.  The post-merge set is normally tiny and bounded by
        // unfinished delivery, while a false DISJOINT has no later CAS to repair it.
        let closedUnstampedIssues =
            Board.bootstrapCached ctx.Transport ctx.Owner ctx.Title
            |> Result.bind (fun board ->
                Scan.board ctx.Transport Cache.Scheduling ctx.Owner ctx.Title board.Number)
            |> Result.bind (fun rows ->
                rows
                |> List.filter (fun row ->
                    row.Ref.Owner = ref.Owner
                    // repo-filter-monopoly: exempt — REF-to-REF identity comparison, not a `--repo` filter.
                    && row.Ref.Repo = ref.Repo
                    && not row.IsPullRequest
                    && row.State = Closed
                    && row.Status <> BoardStatus.Done)
                |> List.fold
                    (fun state row ->
                        state
                        |> Result.bind (fun issues ->
                            Reads.issueBody ctx.Transport row.Ref.Owner row.Ref.Repo row.Ref.Number
                            |> Result.map (fun body ->
                                ({ Number = row.Ref.Number
                                   Body = Reads.BodyRead body }: Reads.OpenIssue)
                                :: issues)))
                    (Ok []))

        match targetPathRepo, Reads.openIssues ctx.Transport ref.Owner ref.Repo, closedUnstampedIssues with
        | Error e, _, _ -> Error e
        | _, Error e, _ -> Error e
        | _, _, Error e -> Error e
        | Ok targetPathRepo, Ok openIssues, Ok closedIssues ->
            // .github#2305/ADR-0044 — resolved ONCE for the whole scan, reusing the same
            // `generatedPathCollector` seam `deliveryPathsVerified`/`updateTouchSet` already pay for (one
            // local process invocation, no extra REST/GraphQL). A conflict pair attributable solely to a
            // shared generated, CI-gated artifact is cleared below — see `TouchSet.excludeGenerated`'s doc
            // for the exact-stem-only rule that keeps a directory-prefix declaration (the ADR-0044 #309
            // parent-directory trap) colliding exactly as before.
            let generated =
                match KitDigest.kitRoot () with
                | Some root -> generatedPathCollector root
                | None -> Set.empty

            // THE TOKEN FILTER RUNS FIRST, AND IT IS WHAT MAKES THE MARKER READS AFFORDABLE. It is pure:
            // the bodies arrived on the list read above, `TouchSet.parse`/`conflicts` touch no network, and
            // a row whose declaration cannot collide is discarded before anything is spent on it.
            //
            // Sorted by number so the collision list is deterministic. The old order was the board's row
            // order, which is the project's, which no caller can predict — and `widen --json`'s
            // `collisions` array is a machine contract.
            let colliding =
                List.append openIssues closedIssues
                |> List.sortBy (fun i -> i.Number)
                |> List.choose (fun issue ->
                    if issue.Number = ref.Number then
                        // AN ITEM NEVER COLLIDES WITH ITSELF. `widen` re-checks the touch-set it is about
                        // to write, so without this every widen would report itself.
                        None
                    else
                        let other =
                            { Owner = ref.Owner
                              Repo = ref.Repo
                              Number = issue.Number }

                        match issue.Body with
                        // .github#1794 — A ROW WE COULD NOT READ SURVIVES THE FILTER. It cannot be
                        // compared, so it cannot be CLEARED: discarding it here is the fail-open, because
                        // the only thing downstream of this function is a `DISJOINT` nothing re-decides.
                        // It is carried as UNDETERMINED, not as a collision — see the lock phase below for
                        // why that distinction is what keeps the cost where #1779 measured it.
                        | Reads.BodyUnread reason -> Some(other, Choice2Of2 reason)
                        | Reads.BodyRead body ->
                            // Scope is carried by the reservation marker, which is deliberately read only
                            // after this cheap token shortlist. The unscoped comparison cannot create a
                            // collision by itself; the marker-backed scope check below is authoritative.
                            //
                            // .github#2305 — a pair attributable SOLELY to a shared generated artifact is
                            // excluded here, before the marker-backed scope check below ever runs: neither
                            // side authors that file, so it is not a real reservation to defend.
                            match TouchSet.conflicts ts (TouchSet.parse body) |> TouchSet.excludeGenerated generated with
                            | [] -> None
                            | pairs -> Some(other, Choice1Of2 pairs))

            // ...THEN THE LOCK, for the survivors only. Colliding TOKENS are not a reservation: an
            // unclaimed issue that declares the same files is work nobody is doing, and reporting it would
            // stop a worker who has nothing to stop for. A marker read that FAILS propagates — a claim we
            // could not check is never a silent DISJOINT (#461).
            //
            // IT IS `reserver`, NOT `winner` (.github#1792). This asks "who has RESERVED these files", and
            // `Reads.fsi` already assigns that question its function: `winner` decides IDENTITY (only a live
            // marker answers a heartbeat or loses a CAS), `reserver` decides RESERVATION. This call site read
            // the IDENTITY function to answer the RESERVATION question, and that is the whole of the defect —
            // one marker, two answers. Every OTHER `Reads.winner` in this file is an identity question and
            // stays: `who` classifying Held vs Stale, `reap` refusing to touch a live claim, `adopt` refusing
            // to call a live worker an orphan, `heartbeat` and `done` asking whether the lock is still ours.
            // `adopt` is where "a lapsed lease is adoptable" is DELIBERATE, and it is deliberate about
            // IDENTITY — which is why this change does not disturb it, and why both functions keep their
            // callers rather than one being collapsed into the other.
            //
            // WHAT A MISS COSTS ON THIS CONSUMER (the #1740 AC4 warrant standard). This verdict is the ONLY
            // thing standing between two workers and one file — there is no CAS on a file, so a wrong
            // `DISJOINT` here is not retried, not detected, and not recoverable; it is discovered as two
            // divergent edits to one tree. `winner` failed OPEN into exactly that: a lapsed lease is a MISSED
            // HEARTBEAT far more often than a dead worker (a long build, a slow review, an item that simply
            // outran its lease), and this scan told the next worker those files were free. Measured on
            // 2026-07-28: `.github#1779`'s own worker had its lease lapse at 120m mid-verification and this
            // scan returned `DISJOINT` for its own still-live work — the fail-open, observed on the engine
            // that shipped it.
            //
            // `reserver` fails CLOSED instead, and the failure it can cause is BOUNDED AND ESCAPABLE: a
            // genuinely dead claim reports `OVERLAP … held by W` until somebody collects it, and `reap` is
            // that somebody — the protocol exit exists, it is one command, and the report NAMES the worker
            // and the item to run it against. A lease is a clock; a lock is broken only by `reap`
            // (#461/#581), and #581 has `reap` REFUSE a claim with an open `item/<n>-*` PR precisely because
            // a lapsed lease is not proof the work is dead. If that reasoning is right — and it is the
            // reasoning the SCHEDULER already runs on — it is right at both call sites, which is all this
            // change says.
            //
            // ONE DIVERGENCE SURVIVES ON PURPOSE, AND IT IS NAMED HERE RATHER THAN LEFT IMPLIED
            // (.github#1792, the `RUnowned` case). `Scan.snapshot` reserves one thing this scan does not: a
            // board row sitting in `In progress` with NO marker at all. That reservation is COLUMN-derived,
            // and #1779 removed the column from this path entirely — the candidate set here is
            // `Reads.openIssues`, which carries numbers and bodies and no board state — so it is not merely
            // unimplemented here, it is unreachable by construction. It is declined for two reasons, both
            // about THIS consumer:
            //
            //   • COST. Reaching it means reading the board, which is the GraphQL half #1779 drove to ZERO
            //     points. `overlap --active`/`widen` run in a worker's loop (#418), so paying board points
            //     per call is the trade `.github#1666` and `.github#1086` are both about.
            //   • NO EXIT. The two verbs need different things from a stop. `batch` passing over a candidate
            //     costs a wait and nothing else. `widen` refusing costs a worker who cannot proceed — and a
            //     markerless row offers them NOTHING to act on: no worker to `say` to, no marker to `reap`,
            //     no lease to wait out. `Scan.snapshot` says this itself where it mints `RUnowned` ("no
            //     worker to name and no lease to wait out"). A scheduler can absorb an unactionable stop by
            //     waiting; a gate a worker is told to believe cannot, because the only remedy left is to
            //     ignore it.
            //
            // So the rule, stated so it can be CHECKED rather than rediscovered: THE TWO SURFACES AGREE ON
            // EVERY MARKER, LIVE OR LAPSED, AND DIVERGE ONLY WHERE THERE IS NO MARKER. `Scan.snapshot`'s
            // `RUnowned` arm carries the same sentence, and the `#1792` legs in `ApplicationServiceTests`
            // pin both halves — the divergence one so that closing it is a decision somebody makes with
            // these two costs in front of them, rather than a patch that silently reopens the question.
            //
            // AND THE SAME MARKER READ SETTLES THE UNREADABLE ROWS, WHICH IS WHY THEY ARE CHEAP
            // (.github#1794). An unreadable body is only dangerous if that row RESERVES something, and the
            // paragraphs above are precisely the argument about what reserves. So an unreadable row asks the
            // one question a colliding row already asks — its marker — and the answer settles it:
            //
            //   - no reserver → it reserves nothing whatever its body said. Skipped, provably safely, and
            //     `widen` is NOT reddened for an anomaly on an issue nobody is holding.
            //   - a reserver  → we cannot say whether it collides, and we may not answer DISJOINT over a
            //     live reservation. Refuse: #266's *"I could not look"*, never *"I looked and it was
            //     fine"*. The caller gets a `Malformed` naming the row, and `overlap --active`/`widen`/
            //     `set-paths` exit non-zero on it.
            //
            // NOTE THE INTERACTION WITH `.github#1792`, WHICH IS NOT NEUTRAL AND IS ASSERTED RATHER THAN
            // INHERITED: because this is `reserver` and not `winner`, a LAPSED claim on a row whose body
            // could not be read now refuses too. That is the same bounded, escapable failure `reserver`
            // takes everywhere else — `OVERLAP`/refusal until somebody `reap`s — and it is the consistent
            // reading: if a lapsed lease still reserves, then a lapsed lease over an UNKNOWN surface
            // reserves an unknown surface, which is exactly what may not be cleared.
            //
            // The cost is therefore 1 marker read per unreadable row — the same unit as a colliding row,
            // and zero when the list reads cleanly, which is `.github#1779`'s measured steady state.
            let rec scan acc rows =
                match rows with
                | [] -> Ok(List.rev acc)
                | ((other: Ref), verdict) :: rest ->
                    match
                        Reads.markerScan ctx.Transport other.Owner other.Repo other.Number
                        |> Result.bind (Reads.requireCompleteMarkerScan other.Short)
                    with
                    | Error e -> Error e
                    | Ok markers ->
                        // NO EXTRA READ. `reserver` is a pure function over the marker list already fetched
                        // on the line above, so agreeing with the scheduler costs ZERO additional API calls —
                        // #1779's measured cost (0 GraphQL points; REST at 1 issue-list plus 1 marker read
                        // per colliding row) is unchanged, and that was verified either side of one
                        // `overlap --active` call rather than estimated (#1086).
                        match Reads.reserver opts.LeaseMinutes markers with
                        | None -> scan acc rest
                        | Some m ->
                            // #2351 — `cross-repo` is not a repository; see `pathRepoOrFallback`.
                            let otherPathRepo = m.PathRepo |> pathRepoOrFallback other.Repo
                            let samePathRepo =
                                String.Equals(ref.Owner, other.Owner, StringComparison.OrdinalIgnoreCase)
                                && String.Equals(targetPathRepo, otherPathRepo, StringComparison.OrdinalIgnoreCase)

                            if not samePathRepo then
                                scan acc rest
                            else
                                match verdict with
                                | Choice1Of2 pairs -> scan ((other, m.Worker.Value, sharedTokens pairs) :: acc) rest
                                | Choice2Of2 reason ->
                                    Error(
                                        Errors.Malformed(
                                            $"%s{ref.Short}'s collision scan",
                                            $"%s{other.Short} is held by %s{m.Worker.Value} and its touch-set could not be read (%s{reason}) — refusing to report DISJOINT over a live reservation whose surface is unknown (#1794/#266)"
                                        )
                                    )

            scan [] colliding

    let claim (ctx: Context) (opts: Options) : int =
        match oneArg opts "claim: an issue ref", worker opts with
        | Error c, _
        | _, Error c -> c
        | Ok arg, Ok w ->
            match parseRef ctx arg with
            | Error msg ->
                eprint $"fsgg-coord-engine: %s{msg}"
                ExitError
            | Ok ref ->
                let session = w.Session |> Option.map SessionId

                // #516: refuse a SECOND live hold before the CAS. `--force` is the deliberate override — a
                // rule with no escape hatch gets worked around, not obeyed. Re-claiming the SAME item is not
                // caught (the scan excludes `ref` itself), so `take` retries stay idempotent.
                let heldCheck =
                    // Re-read the source body and receipt ledger immediately before EVERY claim path —
                    // including --force and idempotent renewal.  A scheduler snapshot is advisory once
                    // a CAS/post/status mutation is about to occur; this is the mutation boundary that
                    // closes the scan-to-claim race and prevents an implicit route after scope changes.
                    match requireCurrentDeliveryRoute ctx ref with
                    | Ok _ ->
                        // The bounded existing claim scan is the first real-resource observation for a
                        // fresh session.  Check admission only after it, still before the claim CAS/post.
                        if opts.Force then Ok [] else heldElsewhere ctx opts.LeaseMinutes w.Id ref
                    | Error error -> Error error

                match heldCheck with
                | Error e -> failWith opts.Render e
                | Ok(_ :: _ as heldRefs) ->
                    let names = String.Join(", ", heldRefs)

                    eprint
                        $"fsgg-coord-engine: worker '%s{w.Id}' ALREADY HOLDS %s{names}. A claim reserves a touch-set, so a second one locks files nobody is editing for the rest of the lease (%d{opts.LeaseMinutes}m) — and `batch` will refuse every item that overlaps it (#516)."

                    eprint "  Finish or drop the item you hold:  scripts/fsgg-coord done <issue> --flip   (or: release <issue>)"

                    // #1620: `--force` now carries a SECOND, destructive power — it STEALS a live claim.
                    // This line points a worker at the flag for the #516 override alone, so it has to say
                    // what else it will do, or it sends somebody to delete a lock they never meant to touch.
                    // That is exactly the message-vs-behaviour disagreement #1620 exists to close, and it
                    // would have been re-created here, in the one place that actively recommends the flag.
                    eprint
                        $"  If you genuinely mean to hold two, say so:  scripts/fsgg-coord claim <issue> --force"

                    eprint
                        $"  NOTE: --force ALSO STEALS a live claim — against an item another worker is holding it will DELETE their lock (#1620). On a FREE item it does nothing but lift this refusal."
                    ExitRed
                | Ok [] ->
                    // #2459 — `claim` reaches items the scheduler's OWN overlap-avoidance never sees:
                    // `take`/`batch` pre-filter every candidate through the #353 collision scan below
                    // BEFORE it is ever claimed (`activeCollisions`, shared with `widen`/`overlap
                    // --active`), but `claim` is the SAME lock without that upstream filter — which is
                    // exactly right for the orphan/recovery paths `claim` exists to serve (see the #516
                    // refusal two cases up, whose own sentence describes the touch-set reservation THIS
                    // scan is what actually checks: "a claim reserves a touch-set ... and `batch` will
                    // refuse every item that overlaps it"). What was missing is that reaching an item
                    // this way silently gave up a check a caller had no reason to expect it lost: measured
                    // live on 2026-08-12, two workers ended up on intersecting touch-sets with nothing
                    // warning either of them until a merge conflict surfaced after a full review round had
                    // already been spent (#2459).
                    //
                    // So: run the SAME `activeCollisions` scan against THIS item's own declared touch-set.
                    // Default is a WARNING that still claims — refusing by default would break the very
                    // recovery paths `claim` exists for. `--refuse-overlap` is the explicit opt-in for a
                    // caller that wants the scheduler's own guarantee without the scheduler; it refuses
                    // instead, exit `ExitContended` (6) — the same code `take`/`overlap --active` already
                    // use for a live collision, so a scripted caller reads one number either way.
                    //
                    // A CLAIM THAT WINS DESPITE A REPORTED COLLISION STILL EXITS GREEN (0): the lock is
                    // real and the caller asked for exactly this item, so "claimed, and here is what you
                    // now share with whom" is the true outcome, not a failure dressed as one — the OVERLAP
                    // detail lives on stderr and in `--json`'s `collisions` array, never in the exit code.
                    //
                    // AN UNREADABLE SCAN IS NOT A COLLISION. `--refuse-overlap` cannot guarantee
                    // disjointness over a scan it never completed, so it refuses (#523's doctrine, applied
                    // here too). The default path cannot make that guarantee either, but its whole point is
                    // to keep `claim` working through exactly this kind of degradation — an exhausted
                    // budget must not silently turn every `claim` into a refusal — so it warns and proceeds.
                    // `knownTargetPathRepo` is supplied EXPLICITLY, from the board's OWN declared Repo
                    // Scope for `ref` (`boardPathScopes`, the same resolver `overlap`'s two-ref form and
                    // `readPathRepo` below both use) — never left `None` to let `activeCollisions` derive
                    // it from a live marker READ on `ref` itself. Two reasons, not one:
                    //   1. CORRECTNESS. `ref` is, by construction, in the `Ok []` (not-already-held-by-us)
                    //      arm — a FRESH claim has no marker of its own yet, so a marker-derived read would
                    //      answer `None` anyway and fall back to `ref.Repo`, which `boardPathScopes` also
                    //      does when the item is undeclared. A RENEWAL is the one case where `ref` might
                    //      carry a `pathRepo=` on an existing marker — and the board's CURRENT Repo Scope is
                    //      the more current answer of the two; a marker only ever recorded scope AT claim
                    //      time (#2351's own reasoning for preferring the declared scope over a stale one).
                    //   2. COST, MEASURABLY. A live marker read is one REST call PER CLAIM, against `ref`'s
                    //      own `/comments` — on the hottest path in the org. `boardPathScopes` is a BOARD
                    //      scan already cached day-to-day and (for a fresh, non-renewal claim) is the SAME
                    //      read `readPathRepo` below performs moments later for the CAS itself — so this
                    //      adds no new network shape, only reuses the cache sooner. Measured live: without
                    //      this, `case24(g)`'s parity fixture (a transient re-read fault AFTER the CAS post)
                    //      shifted onto THIS scan's marker read instead, changing which operation the fault
                    //      landed on and breaking the withdraw-on-failed-reread assertion — a concrete
                    //      demonstration that an extra per-item REST read here is not free.
                    let collisionScan () =
                        let refPathRepo =
                            match boardPathScopes ctx with
                            | Ok scopes -> Map.tryFind ref scopes |> pathRepoOrFallback ref.Repo
                            | Error _ -> ref.Repo

                        Reads.issueBody ctx.Transport ref.Owner ref.Repo ref.Number
                        |> Result.map TouchSet.parse
                        |> Result.bind (fun ts -> activeCollisions ctx opts ref (Some refPathRepo) ts)

                    // The courtesy notice `widen`'s own overlap refusal already sends its colliding
                    // holders — posted ONLY on the path where this claim actually lands, because that is
                    // the one outcome that gives the other holder something true to react to. A REFUSED
                    // attempt (`--refuse-overlap`) changed nothing on either item, so there is nothing yet
                    // for them to coordinate around.
                    let notifyOverlap (collisions: (Ref * string * string list) list) : PathCollision list =
                        [ for other, holder, toks in collisions do
                              let msg =
                                  $"heads up: worker '%s{w.Id}' just claimed %s{ref.Short} via `claim` (not the scheduler), which overlaps your touch-set here (%s{sharedTokenText toks}). I do not know which of us declared these paths first. This is NOT a race the scheduler is sequencing for us — `claim` skips that upstream filter (#2459) — so please coordinate directly: merge-sequence by hand (whoever lands second rebases), or one of us narrows with `set-paths`. Reply here."

                              match Writes.say ctx.Transport (WorkerId w.Id) (WorkerId holder) other msg with
                              | Error e ->
                                  eprint $"  could NOT notify worker %s{holder} on %s{other.Short}: %s{Errors.explain e}"

                                  yield
                                      { Ref = other
                                        Worker = holder
                                        SharedTokens = toks
                                        Notified = false
                                        NotifyError = Some(Errors.explain e) }
                              | Ok() ->
                                  eprint $"  notified worker %s{holder} on %s{other.Short}"

                                  yield
                                      { Ref = other
                                        Worker = holder
                                        SharedTokens = toks
                                        Notified = true
                                        NotifyError = None } ]

                    let earlyExit, overlapCollisions =
                        // `opts.Command` names the VERB the caller actually typed, not the function about
                        // to run — `take`'s success path below (`match claim ctx { opts with Args = ... }
                        // with ...`) delegates to this same `claim`, but never rewrites `Command`, so it is
                        // still `Take` here. `adopt` and the bare `claim` dispatch are the same fact about
                        // themselves (`opts.Command = Options.Adopt` is exactly what gates the budget-probe
                        // skip a few lines below this one, in the existing `Writes.claimScoped` call).
                        //
                        // `take`/`batch` ALREADY ran their own overlap-avoidance moments before reaching
                        // here — a candidate whose declared touch-set collides with a live claim is never
                        // even offered (`Schedulability`) — so re-running the identical #353 scan on this,
                        // the hottest and most budget-sensitive write path in the whole engine (#1666/
                        // #1086; `budget`'s own spend table shows `take` as the dearest call by a wide
                        // margin), would buy zero new information at a real, paid cost. `claim` (typed
                        // directly) and `adopt` (transferring a STALE claim the scheduler never re-filters)
                        // both reach items the scheduler's pre-filter never saw, which is the actual gap
                        // #2459 is about — so both keep the scan; only `take`'s internal delegation skips it.
                        if opts.Command = Options.Take then
                            None, []
                        else
                            match collisionScan () with
                            | Error e when opts.RefuseOverlap ->
                                let code = failWith opts.Render e
                                Some code, ([]: PathCollision list)
                            | Error e ->
                                eprint
                                    $"fsgg-coord-engine: the #353 collision scan for %s{ref.Short} could not run (%s{Errors.explain e}) — claiming %s{ref.Short} anyway (best-effort; pass --refuse-overlap to require a clean scan first)."

                                None, []
                            | Ok [] -> None, []
                            | Ok collisions ->
                                for other, holder, toks in collisions do
                                    let toksText = sharedTokenText toks
                                    eprint $"OVERLAP — %s{ref.Short} would collide with %s{other.Short} (worker %s{holder})"
                                    eprint $"  %s{toksText}"

                                    eprint
                                        "  These are TWO LIVE CLAIMS over intersecting files, not one — the scheduler is not sequencing them for you here (that protection is `take`/`batch`'s; `claim` skips it, #2459). MERGE-SEQUENCE them by hand (whoever lands second rebases); do not race them. `claim --refuse-overlap` would have refused instead of warning."

                                if opts.RefuseOverlap then
                                    eprint
                                        $"fsgg-coord-engine: refusing to claim %s{ref.Short} (--refuse-overlap): it overlaps %d{List.length collisions} live claim(s) (see OVERLAP lines above)."

                                    Some ExitContended, []
                                else
                                    None, notifyOverlap collisions

                    match earlyExit with
                    | Some exitCode -> exitCode
                    | None ->
                        // #481: the claim records the column it OVERWRITES, so `release` (and `reap`) can put it
                        // back rather than guess `Ready`. The pre-claim column is knowable at exactly one instant
                        // — before the `In progress` write below overwrites it — so it rides into the marker here.
                        //
                        // The board is read at most ONCE: `board` is a `lazy` shared between the pre-claim column
                        // read and the `In progress` write, so a winning claim bootstraps a single time. And the
                        // pre-claim read is spent ONLY when we win and post: `readPreviousStatus` is the CAS's
                        // post-path thunk, never called on a lost race or an idempotent re-claim. That is what
                        // keeps this off the losing side of the hottest path in the org, on the budget that dies
                        // first under fan-out (#418).
                        let board = lazy (Board.bootstrapCached ctx.Transport ctx.Owner ctx.Title)

                        let readPreviousStatus () =
                            // BEST-EFFORT. A pre-claim column we cannot read is recorded as NONE — the same as a
                            // marker minted before #481 — and it NEVER blocks the lock: the lease matters more
                            // than the courtesy of a restorable column, and `release` falling back to `Ready` is
                            // the safe, pre-existing behaviour for "nothing recorded".
                            match board.Force() with
                            | Ok b ->
                                match Board.itemStatus ctx.Transport b ref.Owner ref.Repo ref.Number with
                                | Ok s -> s
                                | Error _ -> None
                            | Error _ -> None

                        // Marker-carried scope keeps active collision checks on REST. This is best-effort
                        // for the same reason as `prev`: failure to read a projection must not prevent the
                        // lock, and an absent field has a conservative legacy fallback to the issue repo.
                        let readPathRepo () =
                            match boardPathScopes ctx with
                            | Ok scopes -> Map.tryFind ref scopes
                            | Error _ -> None

                        // A CLAIM IS TWO FACTS, NOT ONE: the REST marker is the lock; the Projects Status is
                        // the user-visible ledger. A green CAS cannot prove the latter. This receipt re-reads
                        // both AFTER the mutation and keeps every failure/lag explicit, so a worker can gate
                        // implementation on `.converged` rather than parsing an optimistic sentence (#1369).
                        let receiptCensuses (value: Writes.ForcedClaimCensuses) : ForcedClaimCensusesReceipt =
                            let mapCensus (census: Writes.ClaimMarkerCensus) : ClaimMarkerCensusReceipt =
                                { WinnerMarkerId = census.WinnerMarkerId
                                  Markers =
                                    census.Markers
                                    |> List.map (fun marker ->
                                        { MarkerId = marker.MarkerId
                                          Worker = marker.Worker.Value
                                          Live = marker.Live }) }
                            { Before = mapCensus value.Before
                              After = value.After |> Option.map mapCensus }

                        let emitClaimReceipt
                            (kind: string)
                            (held: Writes.Held)
                            (forcedClaimCensuses: Writes.ForcedClaimCensuses option)
                            (projection: Result<BoardStatus * Result<Board.WriteOutcome, Errors.IoError>, string>) =
                            let markerObserved, markerId =
                                match Writes.verifyHeld ctx.Transport opts.LeaseMinutes (WorkerId w.Id) (selfOf w) session ref with
                                | Ok(Writes.Holds fresh) when fresh.MarkerId = held.MarkerId -> true, Some fresh.MarkerId
                                | Ok(Writes.Holds fresh) -> false, Some fresh.MarkerId
                                | Ok Writes.DoesNotHold
                                // #1646. This is a READBACK, so it REPORTS rather than decides: `markerObserved
                                // = false` is the honest receipt for a marker this process may not verify, and
                                // `converged` then says so.
                                //
                                // It is not reachable from a successful claim any more, and the history is worth
                                // keeping: while the refusal sat on `claim`'s re-claim arm alone, `claim --worker
                                // <them>` on a FREE item won the CAS and then failed its own readback here — a
                                // green claim whose receipt said the marker was not ours, because it was not.
                                // `claim` refuses that argv outright now, so the two agree.
                                | Ok(Writes.ImpersonatesHolder _)
                                | Ok(Writes.TwinHolds _) -> false, None
                                | Error e ->
                                    eprint $"fsgg-coord-engine: post-claim marker readback FAILED for %s{ref.Short}: %s{Errors.explain e}"
                                    false, None

                            let status, statusRead =
                                match board.Force() with
                                | Error e ->
                                    eprint $"fsgg-coord-engine: post-claim Status readback FAILED for %s{ref.Short}: %s{Errors.explain e}"
                                    None, "failed"
                                | Ok b ->
                                    match Board.itemStatus ctx.Transport b ref.Owner ref.Repo ref.Number with
                                    | Ok s -> s |> Option.map statusWireName, "observed"
                                    | Error e ->
                                        eprint $"fsgg-coord-engine: post-claim Status readback FAILED for %s{ref.Short}: %s{Errors.explain e}"
                                        None, "failed"

                            // The column the lifecycle reducer established for THIS claim, or `None` when it
                            // withheld — .github#2645. Everything below that used to spell `In progress`
                            // reads it from here, because a hard-coded destination in the receipt, the human
                            // line or the write note is the same invention as a hard-coded observation: it
                            // reports a column that was never the one at stake.
                            let destination =
                                match projection with
                                | Ok(destination, _) -> Some(statusWireName destination)
                                | Error _ -> None

                            let statusWrite =
                                match projection with
                                // FAIL CLOSED, AND SAY SO. Nothing was written — this is neither a failed
                                // mutation (`failed`, which asserts one was attempted) nor a queued one
                                // (`deferred`, which promises `flush` will land it). The lock is unaffected.
                                | Error _ -> "withheld"
                                | Ok(_, Ok Board.Written) -> "written"
                                | Ok(_, Ok Board.Deferred) -> "deferred"
                                | Ok(_, Ok Board.NotOnBoard) -> "not-on-board"
                                | Ok(_, Error _) -> "failed"

                            let pending =
                                match Cache.pending () with
                                | Ok entries -> Some(List.length entries)
                                | Error _ -> None

                            // .github#2645 AC3 — CONVERGENCE IS AGREEMENT WITH THE PROJECTION, NOT WITH ONE
                            // LITERAL. `status = Some "In progress"` made the only convergent column the one
                            // the fabricated observation always produced, so a mid-review re-affirm that
                            // CORRECTLY leaves the row at `In review` would have reported drift, and the
                            // revert this issue is about reported success. A withheld projection is never
                            // convergent: there is no destination for the board to agree with.
                            let converged = markerObserved && destination.IsSome && status = destination

                            let receipt: ClaimReceipt =
                                { Ref = ref
                                  Worker = w.Id
                                  Kind = kind
                                  MarkerObserved = markerObserved
                                  MarkerId = markerId
                                  // The assignee is account-level decoration, never the worker lock. This
                                  // client does not mutate it; null is the honest observation, not a success.
                                  AssigneeObserved = None
                                  Status = status
                                  StatusRead = statusRead
                                  StatusWrite = statusWrite
                                  PendingBoardWrites = pending
                                  // #2459 — every live claim this item's declared touch-set collides with,
                                  // exactly as computed and reported above; empty when the scan found none
                                  // (or, best-effort, when the scan itself could not run). This is purely
                                  // informational and never affects `Converged`: the LOCK and BOARD facts
                                  // above are the postcondition of the mutation, while this is a courtesy
                                  // report about OTHER items that this claim, once won, does not change.
                                  Collisions = overlapCollisions
                                  ForcedClaimCensuses = forcedClaimCensuses |> Option.map receiptCensuses
                                  Converged = converged }

                            match opts.Render with
                            | Json -> printfn "%s" (renderClaimReceiptJson receipt)
                            | Text ->
                                let humanPrefix =
                                    match kind with
                                    | "renewed" -> $"held %s{ref.Short} by worker %s{w.Id} (lease renewed;"
                                    // #1620: a steal reads as a steal on stdout too. The stderr lines above
                                    // named the displaced worker; this is the line a human skims.
                                    | "stolen" -> $"STOLE %s{ref.Short} for worker %s{w.Id} (--force; "
                                    | _ -> $"claimed %s{ref.Short} by worker %s{w.Id} ("

                                if converged then
                                    printfn "%sboard confirmed: marker=%d, Status=%s)" humanPrefix held.MarkerId (destination |> Option.defaultValue "")
                                else
                                    let shownStatus = status |> Option.defaultValue "UNREADABLE/UNSET"
                                    printfn "%slock held; board NOT confirmed: marker=%b, Status=%s, write=%s)" humanPrefix markerObserved shownStatus statusWrite
                                    eprint $"fsgg-coord-engine: do NOT announce or implement %s{ref.Short} yet — re-run `claim %s{ref.Short} --json` and require `.converged == true`; reconciliation retains CLAIM-STATUS-LAG repair."

                                // #2459 — the human line stays a one-word summary; the detailed OVERLAP/who/
                                // shared-tokens lines already went to stderr above, once, at scan time.
                                if not (List.isEmpty overlapCollisions) then
                                    eprint
                                        $"fsgg-coord-engine: NOTE — this claim overlaps %d{List.length overlapCollisions} live claim(s); see OVERLAP lines above (or `overlap %s{ref.Short} --active`)."

                            match projection with
                            | Ok(destination, outcome) -> boardWriteNote ref "Status" (statusWireName destination) outcome
                            // .github#2645 — a WITHHELD projection is reported here rather than swallowed: the
                            // LOCK is held and the exit code is unaffected (`boardWriteNote`'s own rule for a
                            // non-fatal board write), but the column was deliberately not moved, and a worker
                            // must be able to tell that from a write that landed.
                            | Error reason ->
                                eprint
                                    $"fsgg-coord-engine: the Status board write for %s{ref.Short} was WITHHELD — %s{reason}. The lock IS held; the column is UNCHANGED, because a column derived from a fact this process could not read would be an invention. Re-run `claim %s{ref.Short} --json` once the read recovers, or repair the column with `reconcile --apply`."

                            KitDigest.declaredWarn ctx.Transport ref
                            ExitGreen

                        // A stale marker we claimed over was COLLECTED (deleted) by the CAS, never merely
                        // out-ordered — an ignored stale marker is what `heartbeat` resurrects underneath us,
                        // two live markers on one item. TELL each evicted worker, on their own item, that their
                        // expired claim was collected and the item is taken: a silent eviction is how a worker
                        // keeps building against a lock it no longer holds.
                        //
                        // ONE COPY, THREE CALLERS (`Won`, `Renewed`, `Stolen`). It was two identical copies
                        // before #1620, and adding the third is exactly when a duplicated announcement starts
                        // drifting — this is the courtesy that stops a displaced worker corrupting an item, so
                        // the version that gets forgotten is the one that matters.
                        let announceCollected (collected: WorkerId list) =
                            for evicted in collected do
                                match opts.Render with
                                | Json -> eprint $"collected worker '%s{evicted.Value}' expired claim"
                                | Text -> printfn "collected worker '%s' expired claim" evicted.Value

                                Writes.say
                                    ctx.Transport
                                    (WorkerId w.Id)
                                    evicted
                                    ref
                                    $"your expired claim on %s{ref.Short} was collected — worker '%s{w.Id}' has taken the item. Stop working it."
                                |> ignore

                        // Move the board column to the destination the lifecycle reducer derives from this
                        // item's LIVE facts — the ONE board write, through the queue-aware path so an
                        // exhausted budget defers rather than drops (#510).
                        //
                        // .github#2645: the destination is no longer `In progress` by construction, so it is
                        // carried OUT of here beside the write's outcome. Every consumer below — the receipt,
                        // its `converged` predicate, the human line, the write note — reports the column that
                        // was actually at stake instead of a literal that was only ever true by accident.
                        // `Error reason` is the fail-closed answer the function's own contract demands: a
                        // fact could not be read, so NOTHING is written and the reason is reported.
                        let setClaimLifecycle (held: Writes.Held) : Result<BoardStatus * Result<Board.WriteOutcome, Errors.IoError>, string> =
                            match board.Force() with
                            | Error e -> Error $"the board could not be read: %s{Errors.explain e}"
                            | Ok b ->
                                claimLifecycleDestination ctx b (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) ref held
                                |> Result.map (fun destination ->
                                    destination,
                                    Board.boardWrite
                                        ctx.Transport
                                        b
                                        ref.Owner
                                        ref.Repo
                                        ref.Number
                                        "Status"
                                        (Board.Set(statusWireName destination))
                                        w.Id)

                        let force =
                            if opts.Force then
                                Writes.StealLiveHolder
                            else
                                Writes.RefuseLiveHolder

                        // #1620 — THE THEFT NOTICE, POSTED THE MOMENT THE LOCK IS DESTROYED.
                        //
                        // Everything here is the "loud accounting" half of the decision, and it is not optional:
                        // a silent transfer is worse than a refusal, because the displaced worker is still
                        // RUNNING. It matters more than the courtesy on a stale collection — an expired lease
                        // means its holder probably stopped; a stolen live claim means it probably did not.
                        //
                        // The `say` is the load-bearing part. It posts a comment ON THE ITEM naming the prior
                        // holder, the worker that took it, and (by the comment's own timestamp) when — so a
                        // reader of the issue afterwards can see the theft, AND the displaced worker finds it in
                        // `inbox`. Both obligations, one comment. The evicted marker is DELETED, so this comment
                        // is the only surviving trace of the claim that was taken.
                        //
                        // IT RUNS FROM THE EVICTION CALLBACK, NOT FROM THE `Stolen` ARM, because a lock can be
                        // destroyed on paths that never produce a `Stolen`: the post can fail, the re-read can
                        // fail, a newcomer can win the open race. Best-effort — a failed comment does not
                        // un-take the lock — but it is ATTEMPTED on every execution that deleted a live marker.
                        let mutable displaced: WorkerId list = []

                        let announceTheft (victims: WorkerId list) =
                            displaced <- displaced @ victims

                            for victim in victims do
                                match opts.Render with
                                | Json -> eprint $"STOLE %s{ref.Short} from worker '%s{victim.Value}' (--force)"
                                | Text -> printfn "STOLE %s from worker '%s' (--force)" ref.Short victim.Value

                                Writes.say
                                    ctx.Transport
                                    (WorkerId w.Id)
                                    victim
                                    ref
                                    $"worker '%s{w.Id}' has TAKEN %s{ref.Short} through an interruption-safe `claim --force` transition — your live marker was deleted only after a replacement marker was posted. STOP working this item: you no longer hold it, and the replacement is being resolved by the existing comment-order election. If this was wrong, say so here."
                                |> ignore

                        match
                            Writes.claimScoped
                                ctx.Transport
                                opts.LeaseMinutes
                                force
                                announceTheft
                                (WorkerId w.Id)
                                (selfOf w)
                                session
                                ref
                                readPreviousStatus
                                readPathRepo
                                (fun () ->
                                    if opts.Command = Options.Adopt || not (usesLiveHttp ctx) then Ok()
                                    else
                                        match Environment.GetEnvironmentVariable "GITHUB_TOKEN" with
                                        | null | "" -> Error(Errors.RateLimited(Errors.UnknownBudget, None))
                                        | token ->
                                            let establish =
                                                if Budget.fleetState (Budget.readRestObservations token) = Budget.Unknown then
                                                    Reads.issueBody ctx.Transport ref.Owner ref.Repo ref.Number |> Result.map ignore
                                                else Ok()
                                            establish |> Result.bind (fun () ->
                                                match Budget.fleetState (Budget.readRestObservations token) with
                                                | Budget.Healthy -> Ok()
                                                | _ -> Error(Errors.RateLimited(Errors.UnknownBudget, None))) )
                        with
                        | Error e -> failWith opts.Render e
                        | Ok(Writes.Won(held, collected)) ->
                            announceCollected collected
                            emitClaimReceipt "claimed" held None (setClaimLifecycle held)
                        | Ok(Writes.Stolen(held, _, collected, censuses)) ->
                            // `announceTheft` has already run — it fired from inside the CAS, the moment the
                            // holder's marker went. All that is left here is the receipt, which reports the
                            // steal as a steal so a scripted caller can tell it from an ordinary win.
                            announceCollected collected
                            emitClaimReceipt "stolen" held (Some censuses) (setClaimLifecycle held)
                        | Ok(Writes.ReplacementPostFailed(holder, holderMarkerId, reason, _)) ->
                            eprint
                                $"fsgg-coord-engine: %s{ref.Short} forced-claim replacement POST FAILED before any incumbent deletion (%s{reason}). The complete post-state census proves worker '%s{holder.Value}' marker %d{holderMarkerId} remains authoritative: the OLD HOLDER STANDS and nothing was taken."
                            eprint "  Retry is authorized only after a fresh complete marker census; the non-zero exit alone authorizes nothing."
                            ExitRed
                        | Ok(Writes.CleanupRequired(replacement, removed, failed, failedMarkerId, reason, _)) ->
                            eprint
                                $"fsgg-coord-engine: %s{ref.Short} forced-claim cleanup is INCOMPLETE: replacement marker %d{replacement.MarkerId} was posted before eviction; %d{List.length removed} live marker(s) were removed, but worker '%s{failed.Value}' marker %d{failedMarkerId} still stands (%s{reason})."
                            eprint
                                $"  The item is not unclaimed: comment-order still makes the older surviving marker authoritative. Re-run this same `claim %s{ref.Short} --force` as worker '%s{w.Id}' to reuse replacement marker %d{replacement.MarkerId} and reconcile cleanup; do not infer retry authority from the exit code alone."
                            ExitRed
                        | Ok(Writes.PostStateUnreadable(replacement, removed, reason, _)) ->
                            let replacementText = replacement |> Option.map (fun held -> string held.MarkerId) |> Option.defaultValue "not established"
                            eprint
                                $"fsgg-coord-engine: %s{ref.Short} forced-claim post-state is UNREADABLE; replacement marker=%s{replacementText}, observed removals=%d{List.length removed} (%s{reason}). No empty or ownership postcondition is inferred from this failed read."
                            eprint
                                $"  Re-run this same `claim %s{ref.Short} --force` as worker '%s{w.Id}' only after restoring the census read; no ownership verdict was reached."
                            ExitNoVerdict
                        | Ok(Writes.OldHolderStands(replacementMarkerId, holder, holderMarkerId, removed, _)) ->
                            eprint
                                $"fsgg-coord-engine: %s{ref.Short} forced-claim replacement marker %d{replacementMarkerId} is absent in the complete post-state census; worker '%s{holder.Value}' marker %d{holderMarkerId} remains authoritative after %d{List.length removed} observed removal(s). The OLD HOLDER STANDS."
                            eprint "  Nothing in this result authorizes retry; inspect the live marker census before another mutation."
                            ExitRed
                        | Ok(Writes.NoHolderRemaining(replacementMarkerId, removed, _)) ->
                            let replacementText = replacementMarkerId |> Option.map string |> Option.defaultValue "not established"
                            eprint
                                $"fsgg-coord-engine: %s{ref.Short} forced-claim post-state was readable but NO live marker remained: replacement marker=%s{replacementText} after %d{List.length removed} incumbent marker(s) were removed. This is not an ordinary loss and retry is not authorized by this result."
                            ExitRed
                        | Ok(Writes.ForcedClaimLost(winner, _)) ->
                            eprint
                                $"fsgg-coord-engine: %s{ref.Short} forced-claim cleanup completed, but the complete post-state census names worker '%s{winner.Value}' as the comment-order winner. The replacement did not win and was withdrawn; retry is not authorized by this result."
                            ExitRed
                        | Ok(Writes.Renewed(held, collected)) ->
                            // A live marker already ours — the claim RENEWED it in place rather than posting a
                            // second (a `take` retry, or a worker beating its own lease). Any stale debris it
                            // claimed over was still collected, so tell the evicted workers exactly as a fresh
                            // win does.
                            announceCollected collected
                            let outcome = setClaimLifecycle held

                            // THE SHARED-ID HAZARD, WARNED WHERE IT ACTUALLY BITES. This path bypassed the CAS —
                            // it renewed a marker on the strength of its worker id alone — and a marker bearing
                            // our id is not proof it is ours: rules 4/5 (#419) can hand one id to several workers.
                            // The fresh-claim CAS never runs here, so its shared-id refusal is never reached; this
                            // is the one place to say a same-id sibling may have just had its lock adopted.
                            match w.Provenance with
                            | Identity.FromSharedSession _ ->
                                eprint
                                    $"fsgg-coord-engine: NOTE — this renewed an EXISTING marker for worker '%s{w.Id}' without running the claim CAS. If another worker shares this id, you have just adopted ITS lock."

                                eprint
                                    $"fsgg-coord-engine: WARNING — worker id '%s{w.Id}' may not be unique to this worker. Give EACH worker its own id (do NOT invent one):  eval \"$(scripts/fsgg-coord whoami --mint)\""
                            | _ -> ()

                            emitClaimReceipt "renewed" held None outcome
                        | Ok(Writes.Lost holder) ->
                            // TWO DIFFERENT FACTS, ONE OUTCOME (#1620). Ordinarily the holder refused us before
                            // we posted anything. But a `--force` that EVICTED somebody and then lost the fresh
                            // race to a worker arriving after the eviction is a different event: "wait for the
                            // lease" is wrong advice (the lease we were waiting on is gone), and the worker we
                            // set out to displace HAS been displaced even though we did not get the item.
                            //
                            // IT KEYS ON `displaced`, NOT ON `opts.Force`, and the difference is reachable:
                            // `--force` against an item that turns out to be FREE evicts nobody and then loses
                            // an ordinary race to a concurrent claimant. Keying on the flag would report a
                            // displacement that never happened — and "you displaced someone" is not a sentence
                            // to print on a guess.
                            if not (List.isEmpty displaced) then
                                eprint
                                    $"fsgg-coord-engine: %s{ref.Short} was CLEARED by --force, and then %s{holder.Value} won the open race for it — the steal displaced the previous holder but did NOT give you the item."

                                eprint "  Retry to race for it, or leave it: a fresh holder is a working worker, not the dead one you came to recover."
                            else
                                // .github#2683 — THIS SENTENCE USED TO END "Pick another, or wait for the
                                // lease.", AND ONE HALF OF THAT WAS AN INSTRUCTION NOBODY MAY FOLLOW.
                                // `claim` is reached two ways: directly, by a caller who named this item,
                                // and from inside `take`'s own ranked walk. To the second, "pick another"
                                // reads as "run `take` again" — the one act every worker brief in this org
                                // forbids, because a second `take` against a live one is how two workers
                                // land on one item. So the line states the FACT and the remedies that are
                                // actually available, and names who does the picking: `take` walks its own
                                // ranking, so a lost race never needs a caller to re-issue anything.
                                eprint
                                    $"fsgg-coord-engine: %s{ref.Short} is already held by %s{holder.Value} — you did not get it. Wait for the lease to lapse, or work whatever `take` offers instead; `take` advances through its own ranked candidates on a lost race, so no caller re-runs it to retry (.github#2683)."

                            ExitRed
                        | Ok(Writes.Twin theirs) ->
                            // #419: the marker is ours by id but a DIFFERENT session — two workers share one id.
                            // This is a broken IDENTITY, not a contested item, and the fix for a broken identity
                            // is a NEW identity, so `--force` (which steals contested items) must NOT override it:
                            // forcing here would delete a lock our twin is working behind. The remedy is a command,
                            // not a literal — an id an agent copies is an id agents collide on.
                            eprint
                                $"fsgg-coord-engine: %s{ref.Short} carries a live marker with YOUR worker id '%s{w.Id}' but a DIFFERENT session (%s{theirs.Value}) — two workers share one id (#419). Adopting it would put both of you on this item, which is the double-claim ADR-0027 exists to prevent."

                            eprint "  Mint a fresh, unique id in THIS shell (do NOT invent one):  eval \"$(scripts/fsgg-coord whoami --mint)\""
                            ExitRed
                        | Ok(Writes.Impersonates(derived, named)) ->
                            // #1646: `claim` is `Held`'s OTHER door, and the re-claim arm walks through it on the
                            // id alone. Under one harness session the `Twin` arm above cannot catch this — the
                            // impersonator's session IS the holder's — so `claim --worker <them>` renewed a live
                            // holder's lease and reported the item held. Same refusal as the other four verbs.
                            impersonationRefusal "claim" ref derived named
                        | Ok(Writes.Undecided reason) ->
                            eprint $"fsgg-coord-engine: could not take %s{ref.Short}: %s{reason}. This is a LOSS, not a win — retry."
                            ExitRed
                        | Ok Writes.BlockedByUnparseableMarker ->
                            eprint $"fsgg-coord-engine: %s{ref.Short} carries a marker held by nobody (an unparseable lock). It blocks until reaped."
                            ExitRed

    /// #697 — take over an ORPHAN (a stale claim whose PR is FINISHED) and land it.
    ///
    /// `reap` refuses a stale claim whose PR is open (#581, correct) and then offers exactly one exit,
    /// "close it, then reap". For a PR that is green, reviewed and mergeable that exit DESTROYS the best work
    /// on the board — and it is the path of least resistance. `adopt` lets a worker land another worker's
    /// orphaned PR through ONE verified command that cannot be talked into landing anything else.
    ///
    /// WHAT MAKES THIS SAFE IS THE GATE, NOT THE TRANSFER. Each refusal below is a state in which "adopt"
    /// would mean something other than *finish somebody's finished work*: a LIVE claim is not an orphan
    /// (taking it is a steal); no open PR is nothing to land (the claim is merely dead — `reap` it); a PR
    /// that is not GREEN AND MERGEABLE is not finished (rebasing a conflict or fixing a red is AUTHORING);
    /// and `unknown` refuses too, because adopting on a guess is how a "verified" command launders the
    /// unverified, destructive act it exists to replace.
    ///
    /// THE TRANSFER ITSELF IS `claim`'s, ON PURPOSE. `claim` already runs the comment-id CAS, carries the
    /// original `prev` across (#481), and refuses a lost race or a #516 second hold. Re-implementing it here
    /// would be a SECOND lock, and the second one is the one with the bug. `adopt` is a GATE IN FRONT OF
    /// `claim`, not a rival to it — so the "GREEN and MERGEABLE" line is a PRECONDITION report, not a success
    /// banner: the `claim` below can still refuse, and the ADOPTED epilogue prints only when it truly won.
    let adopt (ctx: Context) (opts: Options) : int =
        match oneArg opts "adopt: an issue ref", worker opts with
        | Error c, _
        | _, Error c -> c
        | Ok arg, Ok w ->
            match parseRef ctx arg with
            | Error msg ->
                eprint $"fsgg-coord-engine: %s{msg}"
                ExitError
            | Ok ref ->
                // Read the LOCK off the item (fresh — the lock is never cached).
                match
                    Reads.markerScan ctx.Transport ref.Owner ref.Repo ref.Number
                    |> Result.bind (Reads.requireCompleteMarkerScan ref.Short)
                with
                | Error e -> fail e
                | Ok markers ->
                    match Reads.winner opts.LeaseMinutes markers with
                    // 1. A LIVE claim is not an orphan. Its worker is alive, and taking their item is a STEAL.
                    | Some live ->
                        eprint
                            $"fsgg-coord-engine: %s{ref.Short} is held by a LIVE claim — worker '%s{live.Worker.Value}', renewed %d{live.AgeSeconds / 60}m ago (lease %d{opts.LeaseMinutes}m). A worker that is alive is not an orphan, and taking their item is a steal, not an adoption."

                        eprint
                            $"  Talk to them:  scripts/fsgg-coord say %s{ref.Short} --to %s{live.Worker.Value} --message '<message>'"

                        eprint
                            $"  If you genuinely mean to take it anyway, that is a steal, and the flag says so:  scripts/fsgg-coord claim %s{ref.Short} --force"

                        ExitRed
                    | None ->
                        // 2. There must be an EXPIRED claim to adopt — the lowest-id marker (`reap`'s rule,
                        //    and now literally the same function rather than a second copy of it).
                        //    `Reads.lowestId`, NOT `Reads.reserver`: arm 1 above has already refused a live
                        //    claim as a steal, so this arm needs the ordering without the liveness question.
                        match Reads.lowestId markers with
                        | None ->
                            eprint
                                $"fsgg-coord-engine: %s{ref.Short} carries no expired claim — there is no orphan here to adopt."

                            eprint
                                $"  If it is simply unclaimed, take it the ordinary way:  scripts/fsgg-coord claim %s{ref.Short}"

                            ExitRed
                        | Some stale ->
                            let ow = stale.Worker.Value
                            let oage = stale.AgeSeconds / 60

                            // 3. There must be FINISHED WORK: an open PR on the item's `item/<n>-*` branch.
                            match Reads.prAlive ctx.Transport ref.Owner ref.Repo ref.Number with
                            | Error e -> fail e
                            | Ok LeaseExpiredNoPr ->
                                eprint
                                    $"fsgg-coord-engine: worker '%s{ow}' claim on %s{ref.Short} is expired (idle %d{oage}m), but there is NO open PR on 'item/%d{ref.Number}-*' — so there is no finished work to adopt. That claim is simply dead."

                                eprint
                                    $"  Collect it and take the item normally:  scripts/fsgg-coord reap --repo %s{ref.Repo} --apply && scripts/fsgg-coord claim %s{ref.Short}"

                                ExitRed
                            | Ok LeaseExpiredBranchPushed ->
                                // #1055: a pushed `item/<n>-*` branch with NO PR is work IN PROGRESS, not
                                // finished work — `adopt` lands green PRs and there is none. Refuse: the
                                // worker is likely mid-build, or a REST outage expired the lease before they
                                // opened the PR and they will re-claim. Adopting would race a live worker.
                                eprint
                                    $"fsgg-coord-engine: worker '%s{ow}' claim on %s{ref.Short} is expired (idle %d{oage}m), and a pushed 'item/%d{ref.Number}-*' branch exists but has NO open PR — so there is no FINISHED work to adopt (#1055). A branch with no PR is work in progress, not a landable PR."

                                eprint
                                    "  Do NOT adopt: the worker may still be writing, or a REST outage expired the lease mid-work and they will re-claim. Leave it for proof of life (a PR), or coordinate on the item."

                                ExitRed
                            | Ok LeaseHeld
                            | Ok LivenessUnknown ->
                                // We could not establish the PR's existence — fail closed. Adopting on a read
                                // we could not make is the guess this command exists to refuse.
                                eprint
                                    $"fsgg-coord-engine: could NOT determine whether %s{ref.Short} has an open PR (rate limit? network?). REFUSING to adopt on a guess — look at the item yourself."

                                ExitRed
                            | Ok(LeaseExpiredPrOpen pnum) ->
                                // 4. THE GATE. `adopt` lands FINISHED work and nothing else.
                                match Reads.prLandable ctx.Transport ref.Owner ref.Repo pnum with
                                | PrGreen ->
                                    // A PRECONDITION report, not a success banner — `claim` below can still
                                    // refuse (a lost CAS, #516, a twin), and announcing success before it wins
                                    // would leave the operator unable to tell whether the lock was taken.
                                    eprint
                                        $"fsgg-coord-engine: PR #%d{pnum} on 'item/%d{ref.Number}-*' is GREEN and MERGEABLE — worker '%s{ow}' FINISHED this work and died before landing it (idle %d{oage}m). Taking the claim..."

                                    // 5. Take the lock. `claim` does the transfer under the same CAS as every
                                    //    other lock; it DIES on a lost race, so the epilogue runs only on a win.
                                    let rc = claim ctx opts

                                    if rc = ExitGreen then
                                        eprint
                                            $"fsgg-coord-engine: ADOPTED %s{ref.Short} from worker '%s{ow}' — the claim is now yours."

                                        eprint
                                            $"  The work is FINISHED. Do NOT rebuild it, and do NOT close PR #%d{pnum}. Land it:"

                                        eprint
                                            $"    gh api -X PUT repos/%s{ref.Owner}/%s{ref.Repo}/pulls/%d{pnum}/merge -f merge_method=squash"

                                        eprint $"    scripts/fsgg-coord done %s{ref.Short} --flip"
                                        ExitGreen
                                    else
                                        rc
                                | PrConflicted ->
                                    eprint
                                        $"fsgg-coord-engine: PR #%d{pnum} on %s{ref.Short} is OPEN but CONFLICTED with its base — so it is not landable as it stands, and it is not finished work. Rebasing it is AUTHORING, not landing; and GitHub gives a conflicted PR no CI at all (it cannot build refs/pull/%d{pnum}/merge), so nothing about it has been verified since the conflict appeared."

                                    eprint
                                        $"  Take the item the ordinary way and finish the job:  scripts/fsgg-coord reap --repo %s{ref.Repo} --apply && scripts/fsgg-coord claim %s{ref.Short}"

                                    ExitRed
                                | PrPending ->
                                    eprint
                                        $"fsgg-coord-engine: PR #%d{pnum} on %s{ref.Short} still has checks RUNNING — it is not finished yet, and a pending check is not a passing one. Let CI settle, then adopt."

                                    ExitRed
                                | PrRed ->
                                    eprint
                                        $"fsgg-coord-engine: PR #%d{pnum} on %s{ref.Short} is NOT green — either a check failed, or it has NO check runs at all. Both are one verdict here: a missing subject is a finding, not a pass (#606), and CI that never started has proved nothing. A red PR is not finished work."

                                    eprint
                                        $"  If it is genuinely abandoned, close the PR and reap the claim. If it is salvageable, take the item and finish it:  scripts/fsgg-coord reap --repo %s{ref.Repo} --apply && scripts/fsgg-coord claim %s{ref.Short}"

                                    ExitRed
                                | PrMerged ->
                                    // `adopt` lands FINISHED work; a merged PR is finished AND landed, so
                                    // there is nothing to land and the honest instruction is to stamp it.
                                    // Reached only if the liveness read and this one straddle a merge.
                                    eprint
                                        $"fsgg-coord-engine: PR #%d{pnum} on %s{ref.Short} is ALREADY MERGED — there is nothing to adopt, and nothing to land. The work is done; what is missing is the STAMP."

                                    eprint
                                        $"  scripts/fsgg-coord done %s{ref.Short} --flip --pr %d{pnum}"

                                    ExitRed
                                | PrClosed ->
                                    eprint
                                        $"fsgg-coord-engine: PR #%d{pnum} on %s{ref.Short} is CLOSED WITHOUT MERGING — nothing landed, so there is nothing to adopt and the item must NOT be stamped done."

                                    eprint
                                        $"  Take the item the ordinary way and finish the job:  scripts/fsgg-coord reap --repo %s{ref.Repo} --apply && scripts/fsgg-coord claim %s{ref.Short}"

                                    ExitRed
                                | PrUnknown ->
                                    eprint
                                        $"fsgg-coord-engine: could NOT determine the state of PR #%d{pnum} on %s{ref.Short} (rate limit? network? GitHub computes mergeability lazily and may not have done so yet). REFUSING to adopt on a guess — an 'adopt' that lands an unverified PR is exactly the destructive act this command exists to replace. Look at the PR, or re-run in a moment."

                                    ExitRed

    /// `landable <pr> --repo NAME` — is this OPEN PR finished work? The #697/#720 verdict as a first-class
    /// QUERY: the ONE word (`green`/`conflicted`/`pending`/`red`/`unknown` — or `merged`/`closed` when the
    /// PR is not open at all, #1680) on stdout, the DECISION in the exit code so a poll loop reads "keep
    /// waiting" from "stop" without parsing prose (#724).
    ///
    /// IT IS THE READ `who`/`reap`/`adopt` ALREADY MAKE (`Reads.prLandable` → `Landable.score`), surfaced on
    /// its own so the verdict has ONE home. §5 of the worker recipes used to re-derive it in ~40 lines of jq,
    /// wrong four times (#547/#606/#698/#720) and fixed in a COPY each time because nothing executes a recipe.
    /// A command a test can hold makes a fifth copy unwritable — this is that command.
    ///
    /// FAIL-CLOSED BY CONSTRUCTION. `Reads.prLandable`'s failure IS its answer — a read it could not make is
    /// `PrUnknown`, an honest no-verdict, never a masqueraded green — so there is no separate error path here:
    /// a rate limit, a 404, a `null` mergeability GitHub has not resolved all resolve to `unknown` (exit 4),
    /// the fail-closed verdict on which the poll loop advises nothing.
    ///
    /// `--wait` (#724) turns the single read into a POLL that never believes an early green — it waits for
    /// the run set to STOP GROWING and keeps waiting while zero runs have registered — so a recipe can NAME
    /// this gate instead of embedding the ~40 lines of jq the four recipe copies drifted on. The
    /// break-vs-wait decision is `Landable.settled` (pure, unit-tested); the loop here only threads the read.
    ///
    /// `--require NAME` and `--sha SHA` (#737) are what let the LAST hand-rolled copy — the skill-registry
    /// autofix BOT, which merges unattended — call this command rather than carry its own rollup. Both are
    /// assertions that can only REFUSE: `--require` names a check that must have reported (the bot's
    /// `registry-coherence` is not required by branch protection, so nothing else would ever look at it),
    /// and `--sha` names the head the caller MEANS (the bot force-pushes, and `pulls/{n}` lags). Each
    /// unsatisfied assertion is `pending`, so `--wait` rides out the transient case and refuses the
    /// permanent one; neither can produce a `green`.
    ///
    /// Review acceptance is the DEFAULT final assertion (.github#2360). The earlier opt-in shape let a
    /// worker omit one token and receive the same green word as a command that evaluated the host's review
    /// record. `--require fsgg:review-decision/v2` remains a compatible, explicit spelling, but plain
    /// `landable` now asks the same question.
    ///
    /// One known caller deliberately has no critic: skill-registry-autofix merges its own mechanical PRs
    /// under `--require registry-coherence --sha <head>`. That already-named assertion is the narrow
    /// exemption. It is not a broad "some --require was supplied" escape, and explicitly adding the review
    /// token wins over it. Both the evaluated and exempt paths announce their provenance on stderr, while
    /// stdout remains the one-word verdict contract.
    ///
    /// THE TOKEN IS NAMESPACED (`fsgg:review-decision/v2`, the structured schema id) rather than a bare word
    /// like `review-accepted`, because AC4 (.github#2360) is exactly `.github#2354`: a required check
    /// satisfied by an UNRELATED job of the same name. A literal check run or workflow job cannot collide
    /// with this token without also being spelled like an `fsgg:` protocol marker, which nothing in this
    /// org's CI vocabulary is.
    ///
    /// FAIL CLOSED. An unreadable head, an unreadable comment thread, an absent or malformed review chain,
    /// or a marker bound to a DIFFERENT sha than the one just read are all `Unmet` — AC2 requires the last
    /// one explicitly: a stale-sha marker is treated as ABSENT, never as satisfaction, because a reader who
    /// sees a marker at all is the reader most likely to stop looking.
    let private reviewAcceptedRequireToken = "fsgg:review-decision/v2"

    let private registryCoherenceRequireToken = "registry-coherence"

    /// The one extra assertion `landable --require fsgg:review-decision/v2` folds into the rollup — see the
    /// doc comment above `landable` for why this exists. The accepted receipt authorizes exactly one
    /// issue/claim/head/effective-base tuple; every component is recomputed on this final green path.
    let private reviewAcceptedUnmet (ctx: Context) (leaseMinutes: int) (repoName: string) (pr: int) : Reads.Unmet list =
        match Reads.prHeadSha ctx.Transport ctx.Owner repoName pr with
        | Error _ ->
            [ Reads.Asserted
                  $"PR #%d{pr}'s current head SHA could not be read, so the host review-acceptance marker (`%s{reviewAcceptedRequireToken}`) cannot be bound to it" ]
        | Ok liveHead ->
            match Reads.commentsWithIdentity ctx.Transport ctx.Owner repoName pr with
            | Error _ ->
                [ Reads.Asserted
                      $"PR #%d{pr}'s comments could not be read, so the host review-acceptance marker (`%s{reviewAcceptedRequireToken}`) cannot be found" ]
            | Ok comments ->
                let reviewComments =
                    comments
                    |> List.map (fun c -> ({ Id = c.Id; Url = c.Url; Body = c.Body }: Driver.ReviewComment))

                match Driver.parseReviewComments reviewComments with
                | Error _ ->
                    [ Reads.Asserted
                          $"PR #%d{pr} carries no valid host review-acceptance marker (`%s{reviewAcceptedRequireToken}`) — the review chain is absent, incomplete, or malformed" ]
                | Ok chain ->
                    let problems = ResizeArray<Reads.Unmet>()
                    match chain.HeadSha with
                    | Some acceptedHead when acceptedHead = liveHead -> ()
                    | Some staleHead ->
                        problems.Add(
                            Reads.Asserted
                                $"PR #%d{pr}'s host review-acceptance marker is bound to head `%s{staleHead}`, not the current head `%s{liveHead}` — a stale-sha marker is treated as ABSENT, never as satisfaction")
                    | None ->
                        problems.Add(
                            Reads.Asserted
                                $"PR #%d{pr}'s host review-acceptance marker did not resolve to a bound head SHA")

                    let suffix = $"/pr/%d{pr}"
                    let boundItem =
                        chain.Subject
                        |> Option.bind (fun subject ->
                            if subject.EndsWith(suffix, StringComparison.Ordinal) then
                                subject.Substring(0, subject.Length - suffix.Length) |> parseRef ctx |> Result.toOption
                            else None)

                    match boundItem with
                    | None ->
                        problems.Add(
                            Reads.Asserted
                                $"PR #%d{pr}'s host review-acceptance receipt does not bind this pull request to a readable coordination item")
                    | Some item when item.Owner <> ctx.Owner || item.Repo <> repoName ->
                        problems.Add(
                            Reads.Asserted
                                $"PR #%d{pr}'s host review-acceptance receipt is bound to `%s{item.Canonical}`, not `%s{ctx.Owner}/%s{repoName}`")
                    | Some item ->
                        match Reads.markerScan ctx.Transport item.Owner item.Repo item.Number
                              |> Result.bind (Reads.requireCompleteMarkerScan item.Short) with
                        | Error _ ->
                            problems.Add(
                                Reads.Asserted
                                    $"PR #%d{pr}'s bound item `%s{item.Canonical}` has no readable current claim generation")
                        | Ok markers ->
                            let actualGeneration =
                                Reads.winner leaseMinutes markers |> Option.map (fun marker -> string marker.Id)
                            if chain.ClaimGeneration <> actualGeneration then
                                let expectedGeneration = chain.ClaimGeneration |> Option.defaultValue "absent"
                                let liveGeneration = actualGeneration |> Option.defaultValue "released"
                                problems.Add(
                                    Reads.Asserted
                                        $"PR #%d{pr}'s host review-acceptance receipt is bound to claim generation `%s{expectedGeneration}`, not the current generation `%s{liveGeneration}`")

                    match Reads.prBaseTipSha ctx.Transport ctx.Owner repoName pr with
                    | Error _ ->
                        problems.Add(
                            Reads.Asserted
                                $"PR #%d{pr}'s effective base SHA could not be read, so review acceptance cannot be authorized")
                    | Ok liveBase when chain.BaseSha = Some liveBase -> ()
                    | Ok liveBase ->
                        let expectedBase = chain.BaseSha |> Option.defaultValue "absent"
                        problems.Add(
                            Reads.Asserted
                                $"PR #%d{pr}'s host review-acceptance receipt is STALE: expected effective base `%s{expectedBase}`, actual `%s{liveBase}`; rebase or revalidate the merged tree")

                    List.ofSeq problems

    let landable (ctx: Context) (opts: Options) : int =
        match opts.Repo with
        | None ->
            eprint
                "fsgg-coord-engine: landable: --repo required (no git remote here, so which repo the PR is in is undefined)."

            ExitError
        | Some repoName ->
            match oneArg opts "landable: a PR number" with
            | Error c -> c
            | Ok arg ->
                match Int32.TryParse arg with
                | false, _ ->
                    eprint
                        $"fsgg-coord-engine: landable: '%s{arg}' is not a PR number (landable takes a PR, e.g. 'landable 801 --repo FS.GG.SDD')."

                    ExitError
                | true, pr ->
                    // `--require`/`--sha` (#737): assertions the caller adds to the rollup, both of which can
                    // only ever REFUSE. A required check that has not reported and a PR object that still
                    // names the previous commit are both `pending` — "the evidence is not here yet" — so
                    // `--wait` rides out the transient case (registration, a superseded suite's replacement,
                    // GitHub catching up with a force-push) and refuses when the tries run out.
                    //
                    // `fsgg:review-decision/v2` (.github#2360) is stripped out of `required` here: it is
                    // NOT a check-run/workflow-run name for `Reads.prLandableRequire` to look for (it would
                    // never report and this PR would poll forever for the wrong reason) — it is handled
                    // below, after CI has settled, by `reviewAcceptedUnmet`.
                    let explicitlyRequireReview = opts.Require |> List.contains reviewAcceptedRequireToken
                    let registryCoherenceExemption =
                        not explicitlyRequireReview
                        && (opts.Require |> List.contains registryCoherenceRequireToken)
                    let requireReviewAccepted = not registryCoherenceExemption
                    let required = opts.Require |> List.filter (fun r -> r <> reviewAcceptedRequireToken)
                    let expected = opts.Sha

                    let read () =
                        Reads.prLandableRequire ctx.Transport ctx.Owner repoName pr required expected

                    // The verdict, over the head SHA's workflow runs UNIONED with its check-runs, a superseded
                    // suite dropped (#720). One word on stdout; the exit code carries the decision.
                    let state, missing =
                        if not opts.Wait then
                            let v, _, missing = read ()
                            v, missing
                        else
                            // --wait: poll until the verdict SETTLES (#724). A single-shot verdict cannot do
                            // the ONE thing the recipe's loop did — refuse to believe an EARLY green. GitHub
                            // registers a PR's runs over 20-60s, so the subject set is empty at first (a
                            // `red` that is really "not started yet") and then GROWS (an early all-green is a
                            // PARTIAL rollup). `Landable.settled` decides break-vs-wait from the verdict and
                            // whether the count has stopped growing; the loop threads the previous count and
                            // keeps the LAST verdict for when the tries run out (the honest #606 red if the
                            // runs never registered, the still-growing verdict otherwise).
                            let tries = defaultArg opts.Tries 30
                            let interval = defaultArg opts.Interval 20

                            let rec poll (i: int) (prev: int) : PrState * Reads.Unmet list =
                                let v, n, missing = read ()

                                if Landable.settled v n prev then v, missing
                                elif i >= tries then v, missing
                                else
                                    if interval > 0 then
                                        System.Threading.Thread.Sleep(interval * 1000)

                                    poll (i + 1) n

                            poll 1 -1

                    // The review-acceptance assertion (.github#2360): checked ONCE, after CI has settled,
                    // and ONLY when it settled green — an unmet CI check is already the reason for a
                    // non-green verdict, and there is no reason to spend the two extra reads (head sha,
                    // comment thread) on a PR that is not otherwise ready to land. An unmet assertion can
                    // only ever REFUSE (same rule as `--require`/`--sha` above): it downgrades GREEN to
                    // PENDING, never anything to RED, so it can never be confused with a failing check.
                    let reviewUnmet =
                        if requireReviewAccepted && state = PrGreen then
                            eprint
                                $"fsgg-coord-engine: landable: evaluated the host review-acceptance assertion (`%s{reviewAcceptedRequireToken}`) for PR #%d{pr}."
                            reviewAcceptedUnmet ctx opts.LeaseMinutes repoName pr
                        else
                            if registryCoherenceExemption && state = PrGreen then
                                eprint
                                    $"fsgg-coord-engine: landable: host review-acceptance assertion (`%s{reviewAcceptedRequireToken}`) EXEMPTED for PR #%d{pr} by the explicit `%s{registryCoherenceRequireToken}` unattended-caller gate."
                            []

                    let missing = missing @ reviewUnmet

                    let state =
                        if state = PrGreen && not (List.isEmpty reviewUnmet) then PrPending else state

                    printfn "%s" (Landable.name state)

                    // WHY the verdict is not green, when the reason is not a red check. `pending` alone is
                    // honest but useless on the case that does not resolve: a required check absent because
                    // its job was RENAMED polls for the whole budget and then refuses, leaving the operator
                    // one word and no thread to pull. stdout — the verdict — is untouched; this is stderr.
                    //
                    // THE THREE REASONS ARE SPOKEN APART (#1575). An assertion the caller added, a context
                    // the BASE BRANCH requires, and a required set we could not read at all have opposite
                    // remedies, and the old single banner ("These are assertions you asked for") is simply
                    // FALSE of the second and third — it would send an operator to look at a flag they did
                    // not pass instead of at the branch policy that is holding their PR.
                    let asserted =
                        missing
                        |> List.choose (function
                            | Reads.Asserted reason -> Some reason
                            | _ -> None)

                    let refused =
                        missing
                        |> List.choose (function
                            | Reads.Refused(state, baseRef) -> Some(state, baseRef)
                            | _ -> None)

                    let notReported =
                        missing
                        |> List.choose (function
                            | Reads.NotReported(context, baseRef) -> Some(context, baseRef)
                            | _ -> None)

                    let unreadable =
                        missing
                        |> List.choose (function
                            | Reads.PolicyUnreadable why -> Some why
                            | _ -> None)

                    // Gated on `pending` exactly as before: a RED verdict already names a check that failed,
                    // and listing an absent one beneath it buries the finding under a "not yet".
                    if state = PrPending && not asserted.IsEmpty then
                        for reason in asserted do
                            eprint $"fsgg-coord-engine: landable: PR #%d{pr} is not landable — %s{reason}."

                        if not explicitlyRequireReview && not reviewUnmet.IsEmpty then
                            eprint
                                "fsgg-coord-engine:   Review acceptance is asserted by DEFAULT; an unmet record is `pending`, never `green`. Obtain a current host acceptance record, or use the narrowly named registry-coherence unattended route when that is genuinely the caller."
                        else
                            eprint
                                "fsgg-coord-engine:   These are assertions you asked for, and an unmet one is `pending`, never `green` — an ABSENT check reads exactly like a passing one to any 'is anything red?' rollup (#606). Usually transient (registration, a superseded suite's replacement, GitHub catching up with a force-push). If it never resolves: the job was RENAMED, its workflow's `paths:` filter no longer matches, or --sha named the wrong commit."

                    // AN UNMET `--sha` ON A MERGED PR GETS ITS OWN SENTENCES (#1680), and must NOT borrow
                    // the block above. Every clause up there is FALSE here: this PR is not "not landable"
                    // (it landed), the verdict is not `pending`, nothing is transient, and no amount of
                    // waiting is implied. Printing a true reason under a false banner is precisely the
                    // fault this issue is about, and #1575 split the refusal arms for the same reason.
                    //
                    // The verdict is untouched — the PR IS merged and stdout says so. What this adds is the
                    // one fact that changes the caller's next act: on the recovery path, "your work landed,
                    // go stamp it" and "something landed here and it was not what you asked about" call for
                    // opposite responses, and only the asserted SHA can tell them apart.
                    if state = PrMerged && not asserted.IsEmpty then
                        for reason in asserted do
                            eprint
                                $"fsgg-coord-engine: landable: PR #%d{pr} MERGED, but NOT the commit you named — %s{reason}."

                        eprint
                            "fsgg-coord-engine:   The verdict stands: this PR is merged and there is nothing to gate. But you asserted a commit with --sha and it is not the one that landed, so do NOT read this as a receipt for YOUR work. Someone else's push, a force-push, or a bot commit landed here. Check what merged before you stamp anything."

                    if not refused.IsEmpty then
                        for state, baseRef in refused do
                            eprint
                                $"fsgg-coord-engine: landable: PR #%d{pr} is not landable — GitHub reports it as `%s{state}` against `%s{baseRef}`, so it will REFUSE this merge (`the base branch policy prohibits the merge`)."

                        // NOT "a check failed" (#1575 AC2). The operator must not be sent to look at a red
                        // check that does not exist: every check that REPORTED here is green, and the gap
                        // is a requirement that has not reported — a different fact with a different
                        // remedy. Only the named contexts below, if we could read them, say which.
                        for context, baseRef in notReported do
                            eprint
                                $"fsgg-coord-engine:   `%s{baseRef}` REQUIRES the status check `%s{context}`, and it has NO CHECK RUN on this head — it has not reported, and it did not fail."

                        for why in unreadable do
                            // The policy read is DIAGNOSIS. Say plainly that the verdict stands without it,
                            // so nobody reads a missing sentence as a missing verdict.
                            eprint
                                $"fsgg-coord-engine:   (could not say WHICH requirement is unmet: %s{why}. The refusal above is GitHub's own and stands regardless.)"

                        // AC5: say when waiting cannot help. A context whose producing workflow does not
                        // exist on this branch, or whose `paths:` filter excludes this PR, can never report
                        // without a NEW event — no amount of polling creates a check run.
                        eprint
                            "fsgg-coord-engine:   Waiting fixes this ONLY if the missing run has yet to register. If the producing workflow does not exist on this branch (it was armed on the base AFTER this head was pushed), or a `paths:`/`branches:` filter excludes this PR, then no check run will EVER be created and --wait cannot help: rebase the branch onto the current base (or push a fresh commit) so the event fires, or stop requiring the context. `behind` needs a rebase; `draft` needs the PR marked ready."

                    // #1680 AC2/AC4: SAY IT, not merely code it. The verdict word is on stdout and the
                    // decision is in the exit code, but the caller meeting this is usually a successor
                    // recovering an item whose worker died between merge and stamp — the one reader who
                    // most needs to be told, in words, that the correct next act is not to wait.
                    if state = PrMerged then
                        eprint
                            $"fsgg-coord-engine: landable: PR #%d{pr} is ALREADY MERGED — there is nothing to gate, and --wait does not poll it."

                        eprint
                            $"fsgg-coord-engine:   This is a TERMINAL verdict, not a retry. If you are recovering an item whose worker died between merge and stamp, the work LANDED — go stamp it: scripts/fsgg-coord done <ref> --flip --pr %d{pr}"

                    if state = PrClosed then
                        eprint
                            $"fsgg-coord-engine: landable: PR #%d{pr} is CLOSED WITHOUT MERGING — nothing landed, so there is nothing to gate."

                        eprint
                            "fsgg-coord-engine:   Terminal, and NOT the merged case: do NOT stamp the item done. The branch was abandoned or the PR rejected, so the item needs re-working or releasing."

                    match state with
                    | PrGreen -> ExitGreen
                    | PrPending -> ExitPending
                    | PrRed
                    | PrConflicted -> ExitRed
                    // #1680. Terminal, and its own code: `merged` is a SUCCESS whose next act is to STAMP,
                    // which is the opposite of what 3 tells a caller to do and the opposite of what 7 told
                    // it before. The WORD on stdout separates the two cases; the code says only "stop".
                    | PrMerged
                    | PrClosed -> ExitNotOpen
                    | PrUnknown -> ExitNoVerdict

    /// `release --status S` — the column the caller DELIBERATELY names (#867/#331), resolved to a
    /// `BoardStatus` before anything is written.
    ///
    /// The timing is the point: an unknown column is refused BEFORE the marker is dropped. Validate it
    /// afterwards and a typo costs the caller their lock AND the column they asked for — released, un-landed,
    /// and non-zero. A refused write spends no GraphQL and drops no lease.
    let private requestedStatus (opts: Options) : Result<BoardStatus option, int> =
        match opts.Status with
        | None -> Ok None
        | Some raw ->
            match Reads.statusOfName raw with
            | Some s -> Ok(Some s)
            | None ->
                eprint
                    $"fsgg-coord-engine: release --status: unknown column '%s{raw}' (Backlog, Ready, In progress, Blocked, In review, Done) — nothing released."

                Error ExitError

    /// `release --blocked-by <ref>` (.github#2079): write the `Blocked by` FIELD FIRST — before the lock
    /// drops, before the Status write, and before the coherence gate below even runs. An invalid ref
    /// refuses the WHOLE call, at zero GraphQL spent on the lock (`gateField`'s own ordering, one call
    /// earlier). Writing the field here rather than folding it into the Status-restore branch below means
    /// the coherence gate that follows simply finds a row already coherent — one write path, not two that
    /// could disagree about what landed.
    let private writeBlockedByIfRequested
        (ctx: Context)
        (w: Identity.Worker)
        (ref: Ref)
        (blockedByArg: string option)
        : Result<unit, int> =
        match blockedByArg with
        | None -> Ok()
        | Some raw ->
            match Blockers.canonicalizeBlockedBy ref.Owner ref.Repo raw with
            | Error Blockers.Placeholder ->
                eprint
                    $"fsgg-coord-engine: release --blocked-by: '%s{raw.Trim()}' is a placeholder, not a ref — a Blocked park needs a real one. Nothing released."

                Error ExitError
            | Error Blockers.NotIssueRefs ->
                eprint
                    $"fsgg-coord-engine: release --blocked-by takes issue refs (owner/repo#n), not prose: '%s{raw}'. Nothing released."

                Error ExitError
            | Ok None ->
                eprint
                    "fsgg-coord-engine: release --blocked-by '' clears rather than parks — a Blocked park needs a real ref, not an empty one. Nothing released."

                Error ExitError
            | Ok(Some canonical) ->
                match Board.bootstrapCached ctx.Transport ctx.Owner ctx.Title with
                | Error e ->
                    eprint
                        $"fsgg-coord-engine: release --blocked-by: the board could not be resolved (%s{Errors.explain e}) — nothing released."

                    Error ExitError
                | Ok board ->
                    match
                        Board.boardWrite ctx.Transport board ref.Owner ref.Repo ref.Number "Blocked by" (Board.Set canonical) w.Id
                    with
                    | Ok Board.Written -> Ok()
                    | Ok Board.Deferred ->
                        eprint
                            "fsgg-coord-engine: release --blocked-by: the 'Blocked by' write is DEFERRED — the budget is exhausted, so it is QUEUED, not lost: scripts/fsgg-coord flush. Nothing released — retry once it has landed."

                        Error Errors.ExRate
                    | Ok Board.NotOnBoard ->
                        eprint $"fsgg-coord-engine: release --blocked-by: %s{ref.Short} is not an item on this board. Nothing released."
                        Error ExitError
                    | Error e ->
                        eprint
                            $"fsgg-coord-engine: release --blocked-by: the 'Blocked by' write FAILED (%s{Errors.explain e}). Nothing released."

                        Error ExitError

    /// AC1 (.github#2079): **a `Blocked` park is coherent ONLY if the row will end with a non-empty
    /// readable `Blocked by` field or a `Blocked on: human/...` sentinel.** A park is two independent
    /// writes — the `Status` column and the `Blocked by` field — and nothing else bound them at write
    /// time: `BLOCKED-NO-REASON` only reds the EMPTY-field case, so a stale-but-non-empty field (the
    /// `FS.GG.Templates#348` shape — an edge that landed as a body line instead) satisfied every existing
    /// check while naming refs that had all resolved, and `BLOCKER-CLEARED` promoted the row.
    ///
    /// Refuses BEFORE the lock drops — a refused write must not cost the caller their lock, `requestedStatus`'s
    /// own timing argument, one clause over. A no-op for every OTHER `--status`, including none: this is
    /// ADR-0045's park invariant, not a general field-write gate, and it never fires on a row this call is
    /// not about to park.
    ///
    /// Called AFTER `writeBlockedByIfRequested`, so `--blocked-by` on this same call already landed and
    /// the live read below sees it.
    /// The authoritative inventory of every `Status=Blocked` writer (#2109).  Every writer, including
    /// a recorded restore, passes the same resolved-Status gate before emitting its mutation.
    type BlockedStatusWriter =
        | CannotWriteBlocked of string
        | DeliberatePark of string
        | GuardedRestore of string

    let blockedStatusWriterCoverage : BlockedStatusWriter list =
        [ CannotWriteBlocked "claim (Status=In progress)"
          CannotWriteBlocked "done (Status=Done)"
          DeliberatePark "release --status Blocked"
          DeliberatePark "set-field Status Blocked"
          DeliberatePark "set-field --batch Status=Blocked"
          DeliberatePark "add --status Blocked"
          DeliberatePark "intake apply Status=Blocked"
          GuardedRestore "release (recorded previous Status=Blocked)"
          GuardedRestore "reap (recorded previous Status=Blocked)" ]

    /// #2098 round 1 (independent review): a pending `Blocked by` CLEAR in the SAME batch is
    /// AUTHORITATIVE, not a cue to fall back on the live field. The live value is about to be overwritten
    /// to empty by THIS call, so reading it first reads state the batch's own write already makes
    /// obsolete — the round-1 defect let `set-field --batch <ref> Status=Blocked "Blocked by="` succeed
    /// whenever the live field happened to still hold a stale non-empty value, landing exactly the
    /// `Status=Blocked`-with-empty-field-and-no-sentinel shape `.github#2079` exists to prevent.
    ///
    /// `release --blocked-by ''` refuses this shape outright (`writeBlockedByIfRequested`'s `Ok None` arm,
    /// above) — a park never clears. `set-field --batch` cannot refuse a `Blocked by` CLEAR
    /// unconditionally the way `release` does: clearing the field is a legitimate write on its own (e.g.
    /// paired with `Status=Ready` once a blocker resolves), and only becomes incoherent when paired with
    /// `Status=Blocked` in the SAME call — which is exactly the one case this function ever runs for. So
    /// it checks the one thing that stays true regardless of what the live field currently holds: the
    /// `Blocked on: human/...` sentinel.
    let private requireSentinelIfBlockedByCleared (ctx: Context) (ref: Ref) : Result<unit, int> =
        match Reads.issueBody ctx.Transport ref.Owner ref.Repo ref.Number with
        | Error e ->
            eprint
                $"fsgg-coord-engine: set-field --batch: the body could not be read to check for a 'Blocked on:' sentinel (%s{Errors.explain e}) — nothing written."

            Error ExitError
        | Ok body ->
            match HumanBlock.parse body with
            | Some _ -> Ok()
            | None ->
                eprint
                    $"fsgg-coord-engine: set-field --batch refuses: this call clears 'Blocked by' in the SAME batch as 'Status=Blocked' — the row would land with an EMPTY field and no 'Blocked on: human/...' sentinel (.github#2079). Pair 'Status=Blocked' with a non-empty 'Blocked by=<refs>' instead, or record a human park first: add a body line 'Blocked on: human/decision' or 'Blocked on: human/action'. Nothing written."

                Error ExitError

    /// #2098: `requireCoherentParkIfBlocked` alone is the wrong gate for `set-field --batch`. `release`
    /// and single `set-field` write the `Blocked by` field as a SEPARATE step BEFORE calling it
    /// (`writeBlockedByIfRequested`, `--blocked-by` above; the single-field write itself for the other
    /// door), so by the time the gate's live read runs, a same-call edge has already landed and the read
    /// sees it. `--batch` cannot borrow that trick: AC1 requires the WHOLE document to validate before
    /// ANY alias is emitted (`setFieldBatchCmd`'s "one aliased mutation" contract — a pair that fails
    /// validation must cost nothing), so there is no live write to read back. Calling the live-only gate
    /// as-is would refuse the exact call the docs steer callers toward: `Status=Blocked` paired with a
    /// non-empty `Blocked by=<ref>` in the SAME batch, because the live board has not seen that pair yet.
    ///
    /// So this batch-only wrapper is handed the batch's OWN pending `Blocked by` write and judges it
    /// BEFORE ever touching the live field: a non-empty pending `Set` makes the park coherent on its own
    /// (no live read needed); a pending `Clear` is judged on the sentinel ALONE (the live field is about
    /// to be obsolete — see `requireSentinelIfBlockedByCleared` above); only the ABSENCE of a `Blocked by`
    /// pair in this batch defers to the live-read gate, so a batch that never touches `Blocked by` behaves
    /// exactly as it did before this issue.
    let requireCoherentParkIfBlockedForBatch
        (ctx: Context)
        (ref: Ref)
        (requested: BoardStatus option)
        (pendingBlockedBy: Board.FieldWrite option)
        : Result<unit, int> =
        if requested <> Some BoardStatus.Blocked then
            Ok()
        else
            match pendingBlockedBy with
            | Some(Board.Set v) when not (String.IsNullOrWhiteSpace v) -> Ok()
            | Some Board.Clear -> requireSentinelIfBlockedByCleared ctx ref
            | _ -> requireCoherentParkIfBlocked ctx ref requested

    /// THE OPERATOR-WRITABLE INTENT CHANNEL (.github#2690) — the WRITE half, shared by every verb that
    /// lands a Status column somebody explicitly named.
    ///
    /// `LifecycleProjection.explicitStatusWatermark` owns the rule (which columns record an intent, and why
    /// the rest deliberately do not); this owns the IO and the failure vocabulary. Callers pass the column
    /// they actually landed, never the one they hoped to.
    ///
    /// CALL IT ONLY AFTER THE COLUMN WRITE LANDED — `Ok Board.Written`, never `Deferred`. That is
    /// `Writes.lifecycleWatermark`'s own stated contract (*"Persist the ordering receipt only after the
    /// caller has freshly verified its board mutation"*): a deferred write has not happened, and `flush`
    /// replays the column, not this.
    ///
    /// IT DOES NOT ADD A VERIFICATION READ, and that is a decision rather than an omission. `reconcile`
    /// re-reads the row before persisting its watermark because the receipt it is about to write could
    /// SUPPRESS a later event under `advance`'s ordering rule. Here the failure runs the other way and is
    /// self-correcting: a watermark for a column write that somehow did not land makes the very next
    /// reconcile pass compute the operator's intended column and WRITE it, which is the outcome the
    /// operator asked for. Spending a GraphQL point per `set-field` to prevent a self-healing outcome is
    /// not the trade `#418` asks for.
    ///
    /// A FAILURE HERE IS A FAILURE, and the caller must say so rather than exiting green. `reconcile`
    /// already settled this vocabulary — a verified status whose watermark could not be persisted is
    /// `Failed "verified status has no durable lifecycle watermark"`, not a warning — and the reason is the
    /// whole of this row: a column with no intent behind it is reverted on the next pass, silently. The
    /// caller that swallows this reproduces the defect it is fixing.
    let private recordExplicitStatusIntent
        (ctx: Context)
        (ref: Ref)
        (landed: BoardStatus)
        (reason: string)
        : Result<unit, string> =
        let observedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()

        match LifecycleProjection.explicitStatusWatermark observedAt reason landed with
        | None -> Ok()
        | Some watermark ->
            Writes.lifecycleWatermark ctx.Transport ref (LifecycleProjection.watermarkMarker watermark)
            |> Result.mapError Errors.explain

    /// The one sentence every caller of the above prints when it fails, so three verbs cannot drift into
    /// three different accounts of the same consequence.
    let private explicitStatusIntentFailure (ref: Ref) (landed: BoardStatus) (why: string) : string =
        $"fsgg-coord-engine: %s{ref.Short} Status=%s{statusWireName landed} LANDED on the board, but its scheduling intent could NOT be recorded (%s{why}) — so the next `reconcile --apply` pass will recompute this row from inputs you never touched and REVERT it (.github#2690). Nothing is queued and nothing replays it. Re-run this command once the transport recovers."

    let release (ctx: Context) (opts: Options) : int =
        match oneArg opts "release: an issue ref", worker opts with
        | Error c, _
        | _, Error c -> c
        | Ok arg, Ok w ->

        match requestedStatus opts with
        | Error c -> c
        | Ok requested ->
            match parseRef ctx arg with
            | Error msg ->
                eprint $"fsgg-coord-engine: %s{msg}"
                ExitError
            | Ok ref ->

                match Writes.verifyHeld ctx.Transport opts.LeaseMinutes (WorkerId w.Id) (selfOf w) (sessionOf w) ref with
                | Error e -> fail e
                | Ok Writes.DoesNotHold ->
                    eprint $"fsgg-coord-engine: %s{w.Id} does not hold %s{ref.Short} — nothing to release."
                    noteWorkerDisagreement w
                    ExitError
                // #1031: our id, another session. `release` DELETES the marker, so adopting a twin's would drop
                // a lock they are working behind — the one outcome this verb must never produce.
                | Ok(Writes.TwinHolds theirs) -> twinRefusal "release" w.Id ref theirs
                // #1646: the marker really is the NAMED worker's, and we are not them. `release` is the most
                // destructive of the four — it DELETES the lock — and it is the verb #1620's decision named as
                // the impersonation route that had to be closed.
                | Ok(Writes.ImpersonatesHolder(derived, named)) -> impersonationRefusal "release" ref derived named
                | Ok(Writes.Holds held) ->

                // AC1 (.github#2079): `--blocked-by` lands the field FIRST, then the coherence gate — both
                // AFTER the holder check above and BEFORE the lock drops below.
                //
                // THE ORDERING RELATIVE TO THE HOLDER CHECK IS LOAD-BEARING (round-1 review). A caller who
                // does NOT hold this item still reaches `release <ref> --blocked-by <x>` on argv — `release`
                // takes no lock to attempt the write, `--blocked-by` doesn't gate on holding — so a write
                // BEFORE `Writes.verifyHeld` would land a live board mutation from a non-holder even though
                // the release itself then correctly refuses with "does not hold". `release`'s whole contract
                // is that it only touches rows it holds; that is worth more than the field write landing a
                // few lines earlier. So both go HERE, inside `Ok(Writes.Holds held)`, after the ONLY check
                // that establishes we may touch this row at all — never ahead of it.
                match writeBlockedByIfRequested ctx w ref opts.BlockedBy with
                | Error c -> c
                | Ok() ->

                match requireCoherentParkIfBlocked ctx ref requested with
                | Error c -> c
                | Ok() ->

                // .github#2698 — `release <ref> --status Ready` is `set-field <ref> Status Ready`'s third
                // door, and it is gated HERE, BEFORE `Writes.release` drops the lock, for the reason the
                // #2079 gate directly above is: a refusal that arrives after the marker is deleted leaves
                // the holder with no lock and no way to retry, which is strictly worse than the row it was
                // protecting. Refused here, the lease is untouched — author the receipt and re-run.
                //
                // ONLY THE EXPLICIT FLAG. The claim-footprint restore below (`unclaimColumn`'s `ResetTo`,
                // which can restore `Ready`) is deliberately NOT gated, and the reason is upstream rather
                // than merely pragmatic: `claim` already runs `requireCurrentDeliveryRoute` on EVERY claim
                // path including `--force`, so a lock cannot be held on a row without a current receipt in
                // the first place. Restoring that claim's own footprint therefore promotes nothing that
                // was not already routed — and the residual window (a receipt invalidated DURING the
                // lease) is a stated, recorded hole rather than a reason to make lock-release refusable.
                match requireCurrentRouteIfReady ctx ref requested with
                | Error c -> c
                | Ok() ->

                    match Writes.release ctx.Transport held with
                    | Error e -> fail e
                    | Ok previousStatus ->
                        // THE LEASE IS ALREADY DROPPED, and everything below runs in that shadow. The marker is
                        // the lock, so a board we cannot read or write from here leaves a column wrong — never a
                        // claim stranded. That ordering is why the live read below may fail without being fatal.
                        //
                        // The board is resolved ONCE and shared by the live read and the write. `bootstrapCached`
                        // is the same call both would make; resolving it twice would spend #418's budget twice
                        // for one answer.
                        let board = Board.bootstrapCached ctx.Transport ctx.Owner ctx.Title

                        // WHAT THE COLUMN BECOMES.
                        //
                        // #867: an explicit `--status` IS the caller naming the deliberate column, so it beats
                        // both the recorded restore and the `Ready` fallback — that is #331/#481's precedence,
                        // and the skill has documented it since. The port dropped the flag on the floor:
                        // `opts.Status` was never consulted, so the documented way to abandon an item into a
                        // column was a no-op that exited 0. It is how #732 kept coming back — four workers
                        // correctly parked it `Blocked`, and the board kept saying `Ready` (#888).
                        //
                        // It also spends NO live read: the caller stated the end state, so there is no default
                        // left to derive, and the read exists only to derive the default.
                        let decision =
                            match requested with
                            | Some s -> Ok(ResetTo s)
                            | None ->
                                // #331's read. The recorded column answers "what did the claim overwrite?"; it
                                // CANNOT answer "did somebody choose a column since?" — the marker was written
                                // at claim time and never updated. Only the live column knows, so `release`
                                // asks it rather than reverting a `Blocked` the protocol itself told the worker
                                // to set.
                                // The two ways the answer can be missing are REPORTED APART, because they send
                                // the reader somewhere different: an unresolvable board is an auth/plumbing
                                // problem, an unreadable column is this item's own read.
                                match board with
                                | Error e -> Error $"the board could not be resolved (%s{Errors.explain e})"
                                | Ok bm ->
                                    match Board.itemStatus ctx.Transport bm ref.Owner ref.Repo ref.Number with
                                    | Ok live -> Ok(unclaimColumn live previousStatus)
                                    // A COLUMN WE COULD NOT READ IS NOT A COLUMN WE MAY OVERWRITE. #266's
                                    // fail-closed rule, aimed at a WRITER: the obvious read-compare-write fails
                                    // OPEN here — treat an unreadable column as "not In progress" and you
                                    // preserve blindly; treat it as "In progress" and you revert a deliberate
                                    // column on a transient 502. Neither is knowledge. So the column is left
                                    // alone and SAID SO, naming the repair.
                                    | Error e -> Error $"its current column could not be read (%s{Errors.explain e})"

                        match decision with
                        | Error why ->
                            eprint
                                $"fsgg-coord-engine: %s{ref.Short}: %s{why} — the lock is dropped, but the column is UNCHANGED. A column we cannot read is not one we may overwrite (#331). Set it yourself if it needs setting:  scripts/fsgg-coord set-field %s{ref.Short} Status '<column>'"

                            printfn "released %s" ref.Short
                            ExitGreen
                        | Ok(Preserve live) ->
                            // NO WRITE. The column was chosen during the lease, so there is nothing to undo —
                            // and stdout must not imply `release` put it there.
                            match live with
                            | Some s -> printfn "released %s (column left at %s)" ref.Short (statusWireName s)
                            // NO COLUMN TO RESET — the item is off this board, or on it with no `Status` set.
                            // SAY THAT. A bare `released <ref>` is this recipe's documented tell for "the
                            // column did NOT land, and stderr says why", so printing one here would raise that
                            // alarm with nothing behind it — and it would lose the plain "not an item on this
                            // board" that the pre-#331 write path reported. `itemStatus` cannot tell the two
                            // apart (`Ok None` is both), so this states what is TRUE of both rather than
                            // guessing which.
                            | None -> printfn "released %s (no column to reset — not on this board, or no Status set)" ref.Short

                            ExitGreen
                        | Ok(ResetTo restoreTo) ->

                        let name = statusWireName restoreTo

                        // #867: the restore's result is REPORTED, never fatal. The lock really is gone, so a
                        // failed column must not red the command — but "not fatal" and "not mentioned" are
                        // different things, and only the second shipped: `|> ignore` discarded all four
                        // outcomes directly beneath a comment promising they were reported. `Deferred` is the
                        // one that bites hardest — an exhausted budget QUEUES the write and nothing replays it
                        // on its own (#510/#878), so a silent defer is a column that never lands.
                        let landed =
                            match board with
                            | Ok board when name <> "" ->
                                match requireCoherentParkIfBlocked ctx ref (Some restoreTo) with
                                | Error _ -> false
                                | Ok() ->
                                    match
                                        Board.boardWrite ctx.Transport board ref.Owner ref.Repo ref.Number "Status" (Board.Set name) w.Id
                                    with
                                    | Ok Board.Written ->
                                        // .github#2690: `release --status <column>` is #867's OTHER door onto
                                        // the deliberate column — the skill has documented it as the way to
                                        // abandon an item into one since #331 — so it carries the same intent
                                        // channel `set-field` does. Only the EXPLICIT flag does: the `None`
                                        // branch above restores a column off the claim marker, which is this
                                        // verb undoing its own claim, not an operator choosing anything.
                                        //
                                        // REPORTED, NEVER FATAL — #867's rule directly above, and it governs
                                        // here a fortiori. The lock is already dropped, so this verb may not
                                        // red; and a missing intent is strictly less damaging than the missing
                                        // column that rule was written about. stderr carries the consequence.
                                        match requested with
                                        | None -> ()
                                        | Some _ ->
                                            match
                                                recordExplicitStatusIntent ctx ref restoreTo $"explicit release --status by %s{w.Id}"
                                            with
                                            | Ok() -> ()
                                            | Error why -> eprint (explicitStatusIntentFailure ref restoreTo why)

                                        true
                                    | Ok Board.Deferred ->
                                        eprint
                                            $"fsgg-coord-engine: the Status restore to '%s{name}' is DEFERRED — the budget is exhausted, so it is QUEUED, not lost, and NOTHING replays it on its own:  scripts/fsgg-coord flush"

                                        false
                                    | Ok Board.NotOnBoard ->
                                        eprint
                                            $"fsgg-coord-engine: %s{ref.Short} is not an item on this board — the lock is dropped, but the column was NOT set to '%s{name}'."

                                        false
                                    | Error e ->
                                        eprint
                                            $"fsgg-coord-engine: the Status restore to '%s{name}' FAILED (%s{Errors.explain e}) — the lock is dropped, but the column is UNCHANGED:  scripts/fsgg-coord set-field %s{ref.Short} Status '%s{name}'"

                                        false
                            | Error e ->
                                eprint
                                    $"fsgg-coord-engine: could not resolve the board (%s{Errors.explain e}) — the lock is dropped, but the column was NOT set to '%s{name}'."

                                false
                            | Ok _ -> false

                        // NAME THE COLUMN ONLY IF IT LANDED. `release` reporting a bare "released <ref>" is
                        // what let the ignored `--status` look like it had worked — but a line that names the
                        // column unconditionally is the SAME defect wearing the fix's clothes: on a deferred
                        // or failed write it asserts, on stdout and with a green exit, a column the board does
                        // not hold. stderr already said otherwise, and a caller that reads one of the two
                        // reads stdout. So stdout states only what is true; the reason it is not true is on
                        // stderr, immediately above.
                        if landed then
                            printfn "released %s → %s" ref.Short name
                        else
                            printfn "released %s" ref.Short

                        ExitGreen

    let heartbeat (ctx: Context) (opts: Options) : int =
        match oneArg opts "heartbeat: an issue ref", worker opts with
        | Error c, _
        | _, Error c -> c
        | Ok arg, Ok w ->
            match parseRef ctx arg with
            | Error msg ->
                eprint $"fsgg-coord-engine: %s{msg}"
                ExitError
            | Ok ref ->
                match Writes.verifyHeld ctx.Transport opts.LeaseMinutes (WorkerId w.Id) (selfOf w) (sessionOf w) ref with
                | Error e -> fail e
                // #1646: `heartbeat` is the quiet one — it RENEWS another worker's lease, so an impersonation
                // here keeps their item alive under our control while they are told nothing. It is refused
                // ahead of the twin arm for the same reason that arm exists: the diagnosis below keys on the
                // worker id, and the named id IS the live winner, so a fall-through would report success.
                | Ok(Writes.ImpersonatesHolder(derived, named)) -> impersonationRefusal "heartbeat" ref derived named
                // #1031: our id, another session. This arm is why `verifyHeld` returns a TWIN rather than a
                // bare non-hold. The diagnosis below keys on the WORKER ID — which a twin shares — so a twin
                // reaching it finds our own id on the live winner, falls to the `_` arm, and is told its lease
                // EXPIRED and to `claim --force`: advice to go take a lock a twin is working behind. The one
                // remedy that cannot be recovered from an id is the one an id-keyed branch would print.
                | Ok(Writes.TwinHolds theirs) -> twinRefusal "heartbeat" w.Id ref theirs
                | Ok Writes.DoesNotHold ->
                    // Either someone else holds it, or the lease expired. Read the markers to say which —
                    // "a non-holder cannot renew" and "the lease expired" are different remedies.
                    match
                        Reads.markerScan ctx.Transport ref.Owner ref.Repo ref.Number
                        |> Result.bind (Reads.requireCompleteMarkerScan ref.Short)
                    with
                    | Ok markers ->
                        match Reads.winner opts.LeaseMinutes markers with
                        | Some m when m.Worker <> WorkerId w.Id ->
                            // #1620: this is also the arm a worker lands in after its live claim was STOLEN
                            // — the thief deleted our marker, so the live winner is now somebody else. It
                            // must stay LOUD and non-zero for exactly that reason: a displaced worker that
                            // heartbeats successfully is a worker that never learns it was displaced. Name
                            // the possibility, because "held by someone else" reads as a mistake of ours,
                            // and a steal is not.
                            eprint $"fsgg-coord-engine: %s{ref.Short} is held by %s{m.Worker.Value}, not %s{w.Id} — STOP working it, or reap it."

                            eprint
                                $"  If you DID hold it, your claim was taken (`claim --force`) — check `inbox` for the notice, and do not push against %s{ref.Short}."
                        | _ ->
                            // An EXPIRED lease needs no `--force`: a plain `claim` COLLECTS the stale marker
                            // it claims over. `--force` steals a LIVE claim (#1620), which this is not, and
                            // advertising it here taught workers to reach for the steal by default.
                            eprint $"fsgg-coord-engine: %s{w.Id}'s lease on %s{ref.Short} has EXPIRED and cannot be renewed in place — re-claim it (a plain `claim` collects the expired marker)."

                        // #1646: BOTH arms above key on the id the caller NAMED, so both are wrong in the same
                        // way when that id is not this process's own — "your lease expired" about somebody
                        // else's lease reads as a fact about us. The named id holds no live lock here (that is
                        // `ImpersonatesHolder`, refused above), so this is the far commoner mistake: a typo.
                        noteWorkerDisagreement w
                        ExitError
                    | Error e -> fail e
                | Ok(Writes.Holds held) ->
                    match Writes.heartbeat ctx.Transport opts.LeaseMinutes held with
                    | Error e -> fail e
                    | Ok _ ->
                        printfn "heartbeat %s by worker %s" ref.Short w.Id
                        ExitGreen

    /// The claim argv derived from a selected scheduler item.  The scheduler has already resolved this
    /// identity; this boundary must preserve it rather than reinterpret a display ref in the board owner's
    /// context (#2155).
    let claimArgsForSelected (item: Item) = [ item.Ref.Canonical ]

    /// HOW MANY RANKED CANDIDATES ONE `take` MAY ATTEMPT BEFORE IT REPORTS `EX_CONTENDED` (.github#2683).
    ///
    /// `take` used to ask the scheduler for exactly ONE candidate and, on a lost claim CAS, return
    /// `EX_CONTENDED` with no item. The remedy both the engine's own stderr and the code comment named was
    /// a retry BY THE CALLER — and the caller is a disposable worker whose brief forbids exactly that: a
    /// second `take` against a live one is how two workers land on one item. So the retry the engine relied
    /// on had no owner, and every lost race permanently idled an implementer slot. Measured three times in
    /// one 2026-08-15/16 `drive-board-best` session, the third time with SIX free disjoint lanes and only
    /// two workers — so the loss rate does not improve as the board gets richer, because every worker ranks
    /// identically and every worker picks the head.
    ///
    /// THE BOUND IS EXPLICIT AND IT IS A CONSTANT, not a loop that runs until the board yields. An
    /// unbounded fallthrough against a contended board would spend a claim attempt per candidate on every
    /// worker in the fan-out, which is the opposite of the scarcity this scheduler exists to respect. Three
    /// is the number the measured waves needed (3 lanes/3 workers, 2/2, 6/2) and it costs at most three
    /// `claim` CAS attempts — set against the alternative, which is the host paying a fresh ~1,900-request
    /// board scan plus a fresh agent for a lane that was free the whole time.
    ///
    /// **THE FALLBACKS ARE THE SCHEDULER'S OWN DISJOINT LANES, AND THAT IS THE SAFETY PROPERTY, NOT A
    /// COMPROMISE.** Raising the limit makes `Batch.scheduleWith` walk further down ONE ranking, reserving
    /// each chosen item's touch-set as it goes — so candidate 2 is disjoint from candidate 1. That is
    /// exactly what a lost race requires: losing candidate 1 means another worker now holds candidate 1's
    /// files, and any item overlapping them is unsafe to claim. A "next item by rank, disjointness ignored"
    /// fallthrough would hand us files a live worker is standing in.
    ///
    /// It costs NOTHING extra to read. The limit is applied by the pure fold in `Batch.scheduleWith`; the
    /// board scan, the body reads, the marker reads and `enrichDeliveryRoutes`' route reads are all made
    /// over the whole candidate set before the limit is ever consulted (`Scan.snapshot` merely writes the
    /// number into the document). One `take` is still one board scan.
    let private takeCandidateBound = 3

    let take (ctx: Context) (opts: Options) : int =
        match worker opts with
        | Error c -> c
        | Ok w ->
            // A first-ever process has no cached header observation.  Its normal bounded scan is the
            // one real-resource read that establishes one; admission remains enforced by `claim`
            // immediately before its mutation.  Blocking here on an empty cache would deadlock every
            // fresh session (budget -> take could never produce the observation it requires).
            match scanAndDecide ctx { opts with Limit = Some takeCandidateBound } Cache.Scheduling with
            // #585: a board we could not read is NOT an empty queue — but that distinction is already
            // carried by the code `fail` returns (EX_RATE for a budget, a non-zero read error otherwise),
            // and it is never EX_NONE, so "I could not look" and "I looked, and it is empty" keep
            // different codes (#266). bash's hard board-read failure exits the same way (#344's fatal
            // die), so the two engines agree.
            | Error e -> failWith opts.Render e
            | Ok(rows, doc, receipt) ->
                sayRepoAdvisory receipt

                match renderLiveDecision ctx { opts with Limit = Some takeCandidateBound } rows doc with
                | Error code -> code
                | Ok result ->
                    match result.Chosen with
                    | [] ->
                        // #585: looked, nothing startable — NOT a claim. Exit EX_NONE so `take && work_it`
                        // does not proceed on nothing.
                        // #440: name the OBSERVED reason, never a GUESSED list of causes. `printChosen` prints
                        // the honest "nothing schedulable right now." to stdout and the per-item passed-over
                        // reasons to stderr — the same shape `batch`/`decide` already use. Reciting "every
                        // candidate is blocked, claimed, overlapping, or undeclared" asserts causes we did not
                        // observe (case 41); over a starved queue half of them are false, which is the #440
                        // defect wearing a headline.
                        //
                        // .github#1525 — AND IT HONOURS `--json`, WHICH IS THE ONE ARM THIS VERB OWNS.
                        // `take`'s success path delegates to `claim`, which has honoured `opts.Render`
                        // since it was written; so `take --json` INHERITED a machine projection rather
                        // than choosing one, and the arm nobody inherited escaped. The result was a
                        // `--json` document that was JSON or prose depending on the fact the caller was
                        // asking about — a projection that cannot describe its own outcome without the
                        // exit code held beside it, which no other `--json` verb requires.
                        //
                        // EX_NONE IS NOT AN ERROR. It is a look that succeeded and found nothing, and the
                        // recipe's documented response is to DIAGNOSE before idling. A driver deciding
                        // that must be able to read the answer, so the empty outcome gets a receipt of its
                        // own rather than an unparseable stream.
                        match opts.Render with
                        | Json ->
                            printfn
                                "%s"
                                (Render.renderNoItemJson
                                    { Worker = w.Id
                                      PassedOver = List.length (passedOver result)
                                      // #979's advisory rides IN the document too. `sayRepoAdvisory`
                                      // above still prints it for the human; to a PARSER, a misspelt
                                      // `--repo` and an empty board are the same `passedOver:0`, and
                                      // this is the only place that can tell them apart.
                                      RepoAdvisory = receipt.RepoAdvisory })

                            // The reasons and #428's banner stay on stderr, from the same helper
                            // `batch --json` uses — stdout is the document, stderr is the "why nothing".
                            sayWhyNothing opts.LeaseMinutes result
                        | Text -> printChosen opts.LeaseMinutes result

                        ExitNone
                    | candidates ->
                        // Claim a chosen item. `claim` re-reads and runs the CAS, so a stale scan cannot
                        // cost a double-claim: the loser holds nothing, and THIS FUNCTION — never its
                        // caller — advances to the next ranked candidate (.github#2683). The old comment
                        // here said "the loser backs off and the caller retries", and that retry was one no
                        // compliant worker may issue; see `takeCandidateBound` for the whole measurement.
                        // #585: translate the claim's verdict into `take`'s contract — a win is 0, an
                        // exhausted budget passes through as EX_RATE (back off until reset), and any other
                        // failure is a LOST RACE: the item was startable when we picked it, so a failure to
                        // take it means someone else got there first.
                        //
                        // EX_CONTENDED NOW FIRES ONLY WHEN EVERY CANDIDATE IN THE BOUNDED RANKING HAS BEEN
                        // ATTEMPTED AND LOST, which narrows WHEN the code fires without touching WHAT it
                        // asserts: #585's contract that 0 means "claimed an item" is unchanged, and a
                        // caller that reads 6 as "lost the race, hold nothing, stop" reads it identically.
                        // EX_RATE still short-circuits on the spot — an exhausted budget is systemic, so
                        // spending it against further candidates would turn one refusal into three.
                        //
                        // Pass the selected typed identity through the mutating path.  `Short` is a display
                        // projection and parsing it here used `ctx.Owner` as a new default, turning an
                        // offered external row into an attempt to claim an unrelated default-owner twin.
                        //
                        // The walk is over a LIST, so a candidate already lost is never re-attempted: each
                        // one is dropped from `remaining` before the next attempt is made.
                        let rec attempt (remaining: Item list) (attempted: int) =
                            match remaining with
                            | [] ->
                                eprint
                                    $"fsgg-coord-engine: every one of the %d{attempted} ranked candidate(s) this scan offered was lost to another worker — nothing was claimed (EX_CONTENDED)."

                                eprint
                                    "  Do NOT re-run `take`: this scan has already walked its own ranking (.github#2683). Report the contention to whoever dispatched you, or wait for a lease to lapse."

                                ExitContended
                            | item :: rest ->
                                match claim ctx { opts with Args = claimArgsForSelected item } with
                                | code when code = ExitGreen -> ExitGreen
                                | code when code = Errors.ExRate -> code
                                | _ ->
                                    if not (List.isEmpty rest) then
                                        eprint
                                            $"fsgg-coord-engine: %s{item.Ref.Short} was not won — advancing to the next ranked candidate this scan already offered (.github#2683)."

                                    attempt rest (attempted + 1)

                        attempt candidates 0

    // ---- the writes ------------------------------------------------------------------------------------

    /// The `Blocked by` gate (case 13). The field is a TYPED dependency edge (Projects v2 has no dependency
    /// field, so it is TEXT — nothing but this gate stops it drifting back into the resolution log it was in
    /// bash), so its value canonicalizes to `owner/repo#n` and prose is refused. It applies to BOTH set-field
    /// surfaces — the single write and `--batch` — so the two cannot disagree about what the field accepts,
    /// and it runs BEFORE any board read, so a refused write spends no GraphQL (the budget that dies first).
    ///
    /// `Ok write` is the value to write (`Set canonical` or `Clear`); `Error rc` is a refusal already
    /// reported, with the exit code to return. A non-`Blocked by` field passes through unchanged — the gate
    /// is scoped to the one field (Contract and every other TEXT field stay free-form).
    let private gateField (ref: Ref) (field: string) (raw: string) : Result<Board.FieldWrite, int> =
        if field <> "Blocked by" then
            Ok(if raw = "" then Board.Clear else Board.Set raw)
        else
            match Blockers.canonicalizeBlockedBy ref.Owner ref.Repo raw with
            | Ok None -> Ok Board.Clear
            | Ok(Some canonical) -> Ok(Board.Set canonical)
            | Error Blockers.Placeholder ->
                // A placeholder is not a value — it is the caller trying to say "no blocker" with a token
                // rather than by clearing. Point at the clear (`'Blocked by' ''`), not at Status.
                eprint
                    $"fsgg-coord-engine: 'Blocked by' does not take a placeholder ('%s{raw.Trim()}'). To say there is no blocker, CLEAR the field:  set-field <issue> 'Blocked by' ''"

                Error ExitError
            | Error Blockers.NotIssueRefs ->
                // Prose in a dependency field. If the caller means the item ITSELF is blocked, that is a
                // Status — name it, so the refusal is a redirection, not just a rejection.
                eprint
                    $"fsgg-coord-engine: 'Blocked by' takes issue refs (owner/repo#n), not prose: '%s{raw}'. If the item ITSELF is blocked, that is a Status:  set-field <issue> Status Blocked"

                Error ExitError

    /// Compatibility entry point for the family-owned `set-field` handler.
    ///
    /// BoardOps owns parsing, validation, aliased batch mutation, and output behavior; this binding
    /// preserves the existing public Client surface for callers that have not migrated to the family
    /// module. Program dispatch does not use this seam: it composes the BoardOps registration table
    /// directly at the executable edge.
    ///
    /// The forwarding body is intentionally the whole implementation here.
    let setField (ctx: Context) (opts: Options) : int = Handlers.setField ctx opts

    let child (ctx: Context) (opts: Options) : int = Handlers.child ctx opts


    type private PathUpdate =
        | Union
        | Replace

    let private declaredPathTokens (touchSet: TouchSet) : string list =
        match touchSet with
        | Undeclared
        | Unreadable _ -> []
        | DeclaredNone -> [ "none" ]
        | DeclaredChore -> [ "any" ]
        | Declared tokens ->
            tokens
            |> List.map (function
                | Matchable token
                | Unmatchable token -> token)

    let private normalizePathTokens (tokens: string list) : string list =
        // Round-trip through the one grammar the issue-body reader uses. This normalizes comma-separated
        // arguments, a leading `./`, backticks and duplicates before the union is formed, so two spellings
        // of one path cannot make a repeated `widen` grow the declaration (#1377).
        // `TouchSet.parse` reads physical lines: it must, because an issue body can carry ordinary prose
        // after its declaration. An argv token is not an issue-body line, though. Shell substitutions and
        // file-backed variables commonly pass a newline-separated path list as ONE token; feeding that
        // directly into the synthetic body made every line after the first disappear before validation.
        // Split that transport shape before constructing the one-line declaration, so every supplied path
        // reaches the grammar, validation, collision scan, and receipt (#2104).
        let physicalTokens =
            tokens
            |> List.collect (fun token ->
                token.Split([| '\r'; '\n' |], StringSplitOptions.RemoveEmptyEntries)
                |> Array.toList)

        "Paths: " + String.Join(" ", physicalTokens)
        |> TouchSet.parse
        |> declaredPathTokens

    let private updateTouchSet (update: PathUpdate) (ctx: Context) (opts: Options) : int =
        let verb, past, action =
            match update with
            | Union -> "widen", "widened", "add paths to"
            | Replace -> "set-paths", "set", "replace"

        match oneArg opts $"%s{verb}: an issue ref", worker opts with
        | Error c, _
        | _, Error c -> c
        | Ok arg, Ok w ->
            if List.isEmpty opts.Paths then
                eprint $"fsgg-coord-engine: %s{verb} needs --paths <token>..."
                ExitError
            else
                match parseRef ctx arg with
                | Error msg ->
                    eprint $"fsgg-coord-engine: %s{msg}"
                    ExitError
                | Ok ref ->
                    // #706 — widen takes the HELD claim. verifyHeld is the only door to it that this command
                    // has, and it fails closed: no capability from a failed read.
                    match Writes.verifyHeld ctx.Transport opts.LeaseMinutes (WorkerId w.Id) (selfOf w) (sessionOf w) ref with
                    | Error e -> fail e
                    | Ok Writes.DoesNotHold ->
                        eprint $"fsgg-coord-engine: %s{w.Id} does not hold %s{ref.Short} — %s{verb} can only %s{action} the touch-set of a lock you hold (#706)."
                        noteWorkerDisagreement w
                        ExitError
                    // #1031: our id, another session. A path update PATCHes the issue BODY, so a twin's touch-set
                    // would be rewritten under them — re-reserving files they are editing, or handing away
                    // files they are.
                    | Ok(Writes.TwinHolds theirs) -> twinRefusal verb w.Id ref theirs
                    // #1646: the same write, aimed at a marker we NAMED rather than hold. It rewrites the
                    // reservation protecting the files the real holder is standing in — #706's defect, reached
                    // deliberately instead of by accident.
                    | Ok(Writes.ImpersonatesHolder(derived, named)) -> impersonationRefusal verb ref derived named
                    | Ok(Writes.Holds held) ->
                        // #523 — validate BEFORE the read of the body, and rewrite BEFORE the PATCH. A bad
                        // token cannot reach the write, because it cannot produce the value the write takes.
                        match opts.Paths |> normalizePathTokens |> Writes.validate with
                        | Error msg ->
                            eprint $"fsgg-coord-engine: %s{msg}"
                            ExitError
                        | Ok validated ->
                            // .github#2305/ADR-0044 — a requested token that EXACTLY names a generated,
                            // CI-gated artifact is refused here, before the body is even read and before
                            // any collision scan runs: nobody authors such a file (a checked-in generator
                            // emits it and CI reds on any diff), so declaring it is not input this command
                            // accepts — the same all-or-nothing input refusal `Writes.validate` just gave a
                            // flag-shaped or sentinel-mixed token, never a silent per-token drop (a drop
                            // would leave the caller believing they declared something they did not).
                            // `TouchSet.generatedTokens` compares STEMS and requires an EXACT match, so a
                            // directory-prefix request (`registry/**`) is NOT caught here — see its doc for
                            // why the ADR-0044 #309 parent-directory trap must keep colliding.
                            let generated =
                                match KitDigest.kitRoot () with
                                | Some root -> generatedPathCollector root
                                | None -> Set.empty

                            let notDeclarable = TouchSet.generatedTokens generated validated.Tokens

                            if not (List.isEmpty notDeclarable) then
                                let joined = String.Join(", ", notDeclarable)

                                eprint
                                    $"fsgg-coord-engine: %s{verb} refuses %s{joined} — generated, CI-gated artifact(s) are not declarable (ADR-0044): nobody authors them, a checked-in generator emits them, and reserving one only serialises a worker who does not need its content. Regenerate and commit it instead; verify-paths already exempts the resulting drift."

                                ExitError
                            else

                            match Reads.issueBody ctx.Transport ref.Owner ref.Repo ref.Number with
                            | Error e -> fail e
                            | Ok body ->
                                // #1377 — `widen` means union. Its name is now its behaviour: every existing
                                // token survives and repeated additions are idempotent. Replacement remains
                                // available only through the deliberately named `set-paths` command, which is
                                // also the operation used to narrow an over-reservation.
                                let proposed =
                                    match update with
                                    | Replace -> Ok validated
                                    | Union ->
                                        declaredPathTokens (TouchSet.parse body)
                                        @ validated.Tokens
                                        |> List.distinct
                                        |> Writes.validate

                                match proposed with
                                | Error msg ->
                                    eprint $"fsgg-coord-engine: %s{msg}"
                                    ExitError
                                | Ok proposed ->
                                    let rewritten = Writes.rewrite body proposed

                                    // #1740 AC5 — IS THIS UPDATE A NARROWING? A token-subset of the prior
                                    // declaration can only ever name FEWER files, so it is provably incapable
                                    // of introducing a collision: whatever the scan below finds was ALREADY
                                    // there before this command ran. (The implication runs one way only — a
                                    // narrowing need not be a token-subset, e.g. `src/**` → `src/A.fs`. So
                                    // this is a sound test for "provably pre-existing", never a claim that
                                    // anything else INTRODUCED one.)
                                    //
                                    // It exists because the sentence below used to assert causation it had
                                    // not established, and that cost real time: the worker who filed #1740
                                    // narrowed a touch-set back to its original declaration, was told the
                                    // update "introduced a collision", and went looking for a mistake in the
                                    // narrowing — when the collision belonged to a claim that predated it.
                                    // A PROPER subset — strictly fewer tokens, all of them already there. The
                                    // length test is not decoration: without it an update that changes NOTHING
                                    // satisfies `forall`, and `widen` (a UNION) reaches that arm on every
                                    // idempotent re-run. It would then announce that it "NARROWED the
                                    // touch-set" over an identity, which is a different false sentence in
                                    // place of the one this is removing. Both lists are deduped (`validate` /
                                    // `List.distinct`), so subset + shorter IS proper.
                                    let priorTokens = declaredPathTokens (TouchSet.parse body)

                                    let isNarrowing =
                                        List.length proposed.Tokens < List.length priorTokens
                                        && proposed.Tokens |> List.forall (fun t -> List.contains t priorTokens)

                                    // #523/#353 — RE-CHECK BEFORE THE PATCH, and let its verdict GATE the write.
                                    // ADR-0021's "re-declare AND re-check overlap before continuing" is the half a
                                    // worker cannot do alone. The overlap scan runs against the PROPOSED touch-set
                                    // (`rewritten.Body`, computed in memory above — `activeCollisions` takes it as an
                                    // argument and never re-reads THIS item's body, so it needs no landed PATCH) and
                                    // compares it to the live claims in THIS item's repo. If the scan is UNREADABLE —
                                    // an exhausted GraphQL budget, a malformed claim — we REFUSE, and the body is left
                                    // untouched: that is #523. Landing the declaration first and re-checking afterwards
                                    // (as bash did) meant that on an exhausted budget the touch-set landed UNVERIFIED
                                    // and the workers it now collided with were never told. Only once we HOLD a verdict
                                    // do we PATCH. A scan that SUCCEEDS and finds a collision still lands the update
                                    // and then notifies each colliding worker on their own issue; only an unreadable
                                    // scan refuses. Same-repo scope: a cross-repo namesake is a phantom (#353).
                                    match
                                        // #2351 — `cross-repo` is not a repository; see `pathRepoOrFallback`.
                                        activeCollisions
                                            ctx
                                            opts
                                            ref
                                            (Some(held.PathRepo |> pathRepoOrFallback ref.Repo))
                                            (TouchSet.parse rewritten.Body)
                                    with
                                    | Error e -> fail e
                                    | Ok collisions ->
                                        let paths = String.Join(", ", proposed.Tokens)

                                        // #2323 round 1 — THE JSON PROJECTION MUST NAME WHAT THE ITEM ACTUALLY DECLARES,
                                        // NOT WHAT WAS REQUESTED. `Render.fsi`'s own doc for `PathUpdateReceipt.Paths` is
                                        // "the tokens the item now declares" — on a REFUSED update (`committed = false`)
                                        // that is `priorTokens`, byte-identical to before the call, never
                                        // `proposed.Tokens`. The Text projection above already gates on `committed`;
                                        // this closure takes the SAME flag so the two projections cannot disagree about
                                        // one call's outcome — a caller gating on the exit code and then trusting this
                                        // field is exactly the false belief #2306's AC1 exists to rule out, and it is
                                        // truer of the machine surface than of the human one, since a program reads it
                                        // unattended.
                                        let receipt (committed: bool) (collisions: PathCollision list) : string =
                                            renderPathUpdateJson
                                                { Ref = ref
                                                  Worker = w.Id
                                                  Kind = past
                                                  Paths = (if committed then proposed.Tokens else priorTokens)
                                                  Collisions = collisions }

                                        // #2306 — REFUSE THE WRITE ONLY WHEN *THIS CALL'S OWN NEW TOKENS* COLLIDE, NOT
                                        // WHENEVER THE FULL PROPOSED DECLARATION STILL SHOWS ANY COLLISION AT ALL.
                                        //
                                        // `collisions` above is unchanged — the #353 scan over the WHOLE proposed body,
                                        // exactly as it always ran, and it still drives the DISJOINT/OVERLAP verdict and
                                        // exit code below exactly as before. What #2306 fixes is narrower: `Writes.widen`
                                        // used to run unconditionally once that scan merely COMPLETED (`Ok _`), so a scan
                                        // that found a real collision still landed the PATCH — #2248's shape, where a
                                        // widen's newly REQUESTED path itself overlapped a live claim and got written
                                        // anyway. But a NARROWING (or an addition of a genuinely disjoint token) can
                                        // surface a collision that predates the command and that the command's own tokens
                                        // had no part in — the #1740 AC5 reasoning below — and refusing to write THAT
                                        // would block the very narrowing this protocol recommends as the remedy (the
                                        // courtesy notice below says exactly "narrow with `set-paths`"). So the write is
                                        // gated on whether a NEW token — one `priorTokens` did not already carry — is
                                        // itself part of a reported collision, not on whether the declaration merely
                                        // still shows one. `TouchSet.decideUpdate` is the one place the ALL-OR-NOTHING
                                        // refusal rule for a collision so attributed is stated (see its `.fsi` doc); this
                                        // call site supplies the attribution, not the threshold.
                                        let newTokenStems =
                                            proposed.Tokens
                                            |> List.filter (fun t -> not (List.contains t priorTokens))
                                            |> List.map TouchSet.stem
                                            |> Set.ofList

                                        let introducedCollision =
                                            collisions
                                            |> List.exists (fun (_, _, toks) -> toks |> List.exists newTokenStems.Contains)

                                        let write =
                                            match TouchSet.decideUpdate introducedCollision with
                                            | TouchSet.CommitUpdate ->
                                                Writes.widen ctx.Transport held rewritten |> Result.map (fun () -> true)
                                            // NO PATCH IS ISSUED ON THIS PATH — that is the whole fix. `held`/`rewritten`
                                            // are never handed to `Writes.widen`, so the body `Reads.issueBody` read
                                            // above is exactly what remains: a widen/set-paths refused because ITS OWN
                                            // requested paths collide leaves `Paths:` byte-identical (#2306 AC1/AC2), for
                                            // a full collision or a partial one alike, since this command already
                                            // computes ONE merged/replaced declaration and gates that ONE write — there
                                            // is no per-token partial commit to leave behind.
                                            | TouchSet.RefuseUpdate -> Ok false

                                        match write with
                                        | Error e -> fail e
                                        | Ok committed ->
                                            // #1517 — THE RENDER MODE IS HONOURED HERE, and it was not before. `--json`
                                            // is `Global` in `scopeOf` and `command-contract` advertises it on both
                                            // verbs, so the parser accepted it, the residue rule had nothing to refuse,
                                            // and this renderer then printed human prose and exited 0 — #867/#991's
                                            // "accepted and ignored" defect, arriving through the one door that rule
                                            // cannot watch. A driver that widens a touch-set had to scrape
                                            // `widened <ref> → Paths: a, b` out of stdout and the overlap verdict out
                                            // of STDERR to learn what it had just done.
                                            //
                                            // The TEXT projection is byte-identical to what it has always been WHEN THE
                                            // WRITE LANDED (`committed`), deliberately: every existing recipe reads it.
                                            // #2306 — when it did NOT land, the line must not claim it did.
                                            match opts.Render with
                                            | Json -> ()
                                            | Text ->
                                                if committed then
                                                    printfn "%s %s → Paths: %s" past ref.Short paths
                                                else
                                                    printfn
                                                        "refused to %s %s's touch-set → Paths: unchanged (%s would overlap a live claim)"
                                                        action
                                                        ref.Short
                                                        paths

                                            // Declaration time is the cheap moment to learn that editing a kit source
                                            // obliges a re-digest (#469); OBSERVED off the tree, advisory, never fatal.
                                            // It is stderr-only, so it cannot corrupt the JSON projection.
                                            KitDigest.digestWarn ()

                                            match collisions with
                                            | [] ->
                                                // `collisions = []` implies `committed = true`: an empty scan can never
                                                // report an introduced collision, so `decideUpdate` always commits here.
                                                match opts.Render with
                                                | Json -> printfn "%s" (receipt true [])
                                                | Text ->
                                                    printfn "DISJOINT — the updated touch-set clears every live claim in %s/%s (#353)." ref.Owner ref.Repo

                                                ExitGreen
                                            | collisions ->
                                                // The notify is the part a worker cannot do alone. A post that fails is
                                                // reported, but the collision still stands — it does not become DISJOINT.
                                                // This runs whether or not `committed` — #2306 does not withhold the
                                                // courtesy notice from a REFUSED attempt: the other holder still benefits
                                                // from knowing an overlapping request was made, even though nothing
                                                // landed on this item, and #353's guarantee holds either way.
                                                //
                                                // #1517 — the notify OUTCOME is collected as it is printed, because the
                                                // JSON receipt carries it. The stderr lines below are unchanged and are
                                                // emitted in BOTH projections: they are operator diagnostics, not the
                                                // machine contract, and stdout is the only stream `--json` speaks on.
                                                let notified =
                                                    [ for other, holder, toks in collisions do
                                                        let toksText = sharedTokenText toks

                                                        eprint $"OVERLAP — now collides with %s{other.Short} (worker %s{holder})"
                                                        eprint $"  %s{toksText}"

                                                        // DO NOT RECOMMEND `Blocked by` FOR A BARE OVERLAP (#1090). An
                                                        // overlap is TRANSIENT — the scheduler already sequences it and
                                                        // it self-clears the moment a claim drops — whereas `Blocked by`
                                                        // is a DURABLE edge nothing ever recomputes. Offering the durable
                                                        // remedy for the transient condition is how a ring got drawn on a
                                                        // premise withdrawn 60 seconds later and held #1059 hostage: a
                                                        // category error the tool used to recommend first. `Blocked by` is
                                                        // correct ONLY for a real logical dependency (this work must be
                                                        // authored against the other's LANDED result), which outlives any
                                                        // claim — and that distinction is the thing the worker has to
                                                        // decide, so the message names it instead of defaulting to the
                                                        // edge that closes rings.
                                                        let msg =
                                                            // #1740 AC5, ON THE MESSAGE THE OTHER WORKER READS. Taking
                                                            // "introduced" off stderr and leaving "which NOW overlaps"
                                                            // here would move the false causal claim rather than remove
                                                            // it — and this is the copy the innocent party reads, so it
                                                            // is the one that misdirects someone who did nothing.
                                                            let origin =
                                                                if isNarrowing then
                                                                    "That is a NARROWING, so it cannot have caused this — the overlap already existed and predates my command"
                                                                else
                                                                    "I do not know which of us declared these paths first, so this may or may not be new"

                                                            // #2306 — NEVER CLAIM A COMPLETED WRITE THAT DID NOT HAPPEN.
                                                            // When `committed` this is byte-identical to the pre-#2306
                                                            // copy; when not, it names the ATTEMPT and the REFUSAL instead
                                                            // of asserting a mutation that never landed.
                                                            if committed then
                                                                $"heads up: I %s{past} %s{ref.Short} to `Paths: %s{paths}`, which overlaps your touch-set here (%s{toksText}). %s{origin}. This is a TRANSIENT overlap — the scheduler already sequences us, and it clears the moment one claim drops, so you may not need to do anything. To unblock the board sooner: narrow with `set-paths`, or split one touch-set so we are disjoint. Only add a `Blocked by` edge if there is a real DEPENDENCY — my work must be authored against your LANDED result, not merely the same files — because that edge is durable and nothing re-checks it once the overlap is gone. Reply here."
                                                            else
                                                                $"heads up: I attempted to %s{action} %s{ref.Short} (to `Paths: %s{paths}`), which would overlap your touch-set here (%s{toksText}). The request was REFUSED, and nothing was changed on my item. %s{origin}. This is a TRANSIENT overlap — the scheduler already sequences us, and it clears the moment one claim drops, so you may not need to do anything. To unblock the board sooner: narrow with `set-paths`, or split one touch-set so we are disjoint. Only add a `Blocked by` edge if there is a real DEPENDENCY — my work must be authored against your LANDED result, not merely the same files — because that edge is durable and nothing re-checks it once the overlap is gone. Reply here."

                                                        match Writes.say ctx.Transport (WorkerId w.Id) (WorkerId holder) other msg with
                                                        | Error e ->
                                                            eprint $"  could NOT notify worker %s{holder} on %s{other.Short}: %s{Errors.explain e}"

                                                            yield
                                                                { Ref = other
                                                                  Worker = holder
                                                                  SharedTokens = toks
                                                                  Notified = false
                                                                  NotifyError = Some(Errors.explain e) }
                                                        | Ok() ->
                                                            eprint $"  notified worker %s{holder} on %s{other.Short}"

                                                            yield
                                                                { Ref = other
                                                                  Worker = holder
                                                                  SharedTokens = toks
                                                                  Notified = true
                                                                  NotifyError = None } ]

                                                // #1740 AC5 — NAME WHAT WE KNOW, AND NOTHING MORE. Neither
                                                // sentence says "introduced" unless that has been shown; on a
                                                // narrowing we can prove the opposite, so we say THAT.
                                                if isNarrowing then
                                                    eprint $"fsgg-coord-engine: this %s{verb} NARROWED the touch-set, so it cannot have introduced the collision — a subset names fewer files. The overlap was ALREADY there, and belongs to a claim that predates this command. Do NOT keep editing the shared paths until it is resolved."
                                                else
                                                    eprint "fsgg-coord-engine: the updated touch-set COLLIDES with a live claim (this command may or may not be what introduced it) — do NOT keep editing the shared paths until it is resolved."

                                                // The OVERLAP detail is IN the object, not beside it on stderr — that
                                                // split is the half of this defect a machine consumer could not work
                                                // around at all (#1517 AC2).
                                                match opts.Render with
                                                | Json -> printfn "%s" (receipt committed notified)
                                                | Text -> ()

                                                // A real same-repo collision exits non-zero (engine ExitContended=6;
                                                // bash's literal 1 disposed on the record, ADR-0040 §5). UNCHANGED by
                                                // #1517/#2306: the renderer and the write gate were the bugs, the exit
                                                // code semantics were not.
                                                ExitContended

    let widen (ctx: Context) (opts: Options) : int = updateTouchSet Union ctx opts

    let setPaths (ctx: Context) (opts: Options) : int = updateTouchSet Replace ctx opts

    /// `paths_of` FAILS CLOSED (#494 leg k). A touch-set read FOR SCHEDULING that we could not complete is
    /// NOT an empty touch-set: an empty set reads as "disjoint from everything", so a failed body read would
    /// let the scheduler hand out work overlapping a held item (#266's fail-open, one subtree down). So a
    /// failed body read is surfaced as a scheduler-specific refusal — "refusing to schedule against an
    /// unknown touch-set" — never diagnosed as "the issue declared nothing"; only a SUCCESSFUL read with no
    /// `Paths:` is the honest empty DISJOINT. The IoError is carried so the exit code stays the read's own (a
    /// rate limit is still ExRate), the way `fail` does — this only swaps the SENTENCE for the one the corpus
    /// greps at the scheduling surface, distinct from a claim we could not read (`claims_of`'s refusal).
    let private failSchedule (ref: Ref) (e: Errors.IoError) : int =
        eprint
            $"fsgg-coord-engine: cannot read the touch-set on %s{ref.Owner}/%s{ref.Repo}#%d{ref.Number} (rate limit? network?) — refusing to schedule against an unknown touch-set."

        Errors.exitCode e

    /// #353 — DOES THIS ITEM'S TOUCH-SET COLLIDE WITH ANOTHER'S, and NOTHING outside its own repo counts.
    ///
    /// `Paths:` tokens are repo-relative: `scripts/fsgg-coord` names a file in whichever repo the item lives.
    /// So comparing two items' token lists is only meaningful WITHIN a repo — `TouchSet.conflicts` says so in
    /// its own contract. `overlap` used to hand `active_claims` no repo, so a token collided with the same
    /// string in every OTHER repo — a phantom that never hands two workers one file (the dangerous
    /// direction), but stops a worker who has nothing to stop for and is INCOHERENT with the scheduler, which
    /// would run the pair in parallel. Two shapes, both repo-scoped:
    ///   `overlap <ref> --active`     — the item vs the LIVE claims in its own repo.
    ///   `overlap <ref-a> <ref-b>`    — the two items, or DISJOINT-by-construction if they are in different repos.
    let overlapCmd (ctx: Context) (opts: Options) : int =
        let touchSetOf (ref: Ref) =
            Reads.issueBody ctx.Transport ref.Owner ref.Repo ref.Number
            |> Result.map TouchSet.parse

        match opts.Args with
        | [ a ] when opts.Active ->
            match parseRef ctx a with
            | Error m ->
                eprint $"fsgg-coord-engine: %s{m}"
                ExitError
            | Ok ref ->
                match touchSetOf ref with
                | Error e -> failSchedule ref e
                | Ok ts ->
                    match activeCollisions ctx opts ref None ts with
                    | Error e -> fail e
                    | Ok [] ->
                        printfn "DISJOINT — %s overlaps no live claim in %s/%s (#353)." ref.Short ref.Owner ref.Repo
                        ExitGreen
                    | Ok collisions ->
                        for other, holder, toks in collisions do
                            printfn "OVERLAP — %s collides with %s held by %s on %s" ref.Short other.Short holder (sharedTokenText toks)

                        ExitContended

        | [ a; b ] when not opts.Active ->
            match parseRef ctx a, parseRef ctx b with
            | Error m, _
            | _, Error m ->
                eprint $"fsgg-coord-engine: %s{m}"
                ExitError
            | Ok ra, Ok rb ->
                match boardPathScopes ctx with
                | Error e -> fail e
                | Ok scopes ->
                    // #2351 — `cross-repo` is not a repository; see `pathRepoOrFallback`. A board Repo
                    // Scope of `cross-repo` used to substitute the literal string for a real repo name
                    // here, so a genuinely same-repo pair whose scopes merely disagreed (one `cross-repo`,
                    // one a rostered value) compared unequal and was reported DISJOINT BY CONSTRUCTION
                    // without ever reading either touch-set — authorizing exactly the collision #353's
                    // short-circuit exists to rule out.
                    let pathRepoOf (r: Ref) = Map.tryFind r scopes |> pathRepoOrFallback r.Repo
                    let samePathRepo =
                        String.Equals(pathRepoOf ra, pathRepoOf rb, StringComparison.OrdinalIgnoreCase)

                    if not samePathRepo then
                        // #353 — DISJOINT BY CONSTRUCTION. Repo-relative tokens in two different repos can never
                        // name the same file, so this needs no body read at all.
                        printfn
                            "DISJOINT — %s and %s are in different repos; repo-relative touch-sets can never name the same file (#353)."
                            ra.Short
                            rb.Short

                        ExitGreen
                    else
                        match touchSetOf ra with
                        | Error e -> failSchedule ra e
                        | Ok tsa ->
                            match touchSetOf rb with
                            | Error e -> failSchedule rb e
                            | Ok tsb ->
                                // .github#2305/ADR-0044 — the same exact-stem exclusion `activeCollisions`
                                // applies: a pair attributable solely to a shared generated, CI-gated
                                // artifact is not a real reservation either side needs defended.
                                let generated =
                                    match KitDigest.kitRoot () with
                                    | Some root -> generatedPathCollector root
                                    | None -> Set.empty

                                match
                                    TouchSet.scopedConflicts ra.Owner (pathRepoOf ra) rb.Owner (pathRepoOf rb) tsa tsb
                                    |> TouchSet.excludeGenerated generated
                                with
                                | [] ->
                                    printfn "DISJOINT — %s and %s share no touch-set token; they may run in parallel." ra.Short rb.Short
                                    ExitGreen
                                | pairs ->
                                    printfn "OVERLAP — %s and %s share %s" ra.Short rb.Short (sharedTokenText (sharedTokens pairs))
                                    ExitContended

        | _ ->
            eprint "fsgg-coord-engine: overlap needs <ref> --active, or two refs: overlap <ref-a> <ref-b>."
            ExitError

    let say (ctx: Context) (opts: Options) : int = Handlers.say ctx opts


    /// This worker's mailbox: every message addressed to it (or broadcast) across the in-flight claims.
    ///
    /// `say` posts a message on the ITEM it concerns, and both parties to a collision are In progress, so the
    /// in-flight set is exactly where cross-work talk lives. A per-worker cursor makes `inbox` show only
    /// what is new; `--peek` leaves the cursor alone.
    ///
    /// IT RUNS THE SAME OFF-BOARD SCAN `who`/`reap`/`batch` run (case 25). A claim — and the message riding
    /// it — can sit on an issue the board never listed (a failed column flip, or an item that never reached
    /// the board), so a mailbox that read only the board's In-progress column would silently drop a message
    /// posted on an off-board claim. The candidate set is the board's In-progress rows (arm A) UNION every
    /// open issue in the repos in scope (arm B — paginated, and never conditional, so a 304 cannot hide a
    /// message the way it must never hide a lock).
    let inbox (ctx: Context) (opts: Options) : int = Handlers.inbox ctx opts

    /// `room open --over N,M` — open a coordination room over a contended cluster (ADR-0051). Creates the
    /// room ISSUE (off the board — coordination scaffolding, not deliverable work) and writes a `Rooms:`
    /// back-reference onto each named item, so their holders share the room's channel via `say`/`inbox`.
    /// No lock is taken or required: a room is opened over other workers' items, exactly as `say` speaks
    /// to them.
    let roomOpen (ctx: Context) (opts: Options) : int = Handlers.roomOpen ctx opts

    let doneCmd (ctx: Context) (opts: Options) : int =
        match oneArg opts "done: an issue ref", worker opts with
        | Error c, _
        | _, Error c -> c
        | Ok arg, Ok w ->
            match parseRef ctx arg with
            | Error msg ->
                eprint $"fsgg-coord-engine: %s{msg}"
                ExitError
            | Ok ref ->
                match Board.bootstrapCached ctx.Transport ctx.Owner ctx.Title with
                | Error e -> fail e
                | Ok board ->
                    match Done.facts ctx.Transport board ref with
                    | Error e -> fail e
                    | Ok facts ->
                        let verdict = Done.verify opts.Pr opts.Evidence facts
                        printfn "%s" (Done.render ref verdict)

                        // .github#2444 — `.github#2427`'s own acceptance criterion, and #733's precedent
                        // (a candidate that existed but was not chosen rides stderr, never the stdout
                        // verdict): printed exactly ONCE here, regardless of how many times `Done.render`/
                        // `Done.renderReceipt` are called below for this SAME verdict.
                        Done.passedOverForeignNote ref verdict
                        |> Option.iter (fun note -> eprint $"fsgg-coord-engine: %s{note}")

                        match verdict with
                        | Verdict.NoVerdict _ -> ExitNoVerdict
                        | Red _ -> ExitRed
                        | Green _ ->
                            // Write durable evidence before the mutable Project projection.  If the latter
                            // is deferred, the scheduled lifecycle reconciler can later prove that this was
                            // a verified terminal transition rather than guessing from issue closure.
                            // `renderReceipt`, not `render`: the durable comment deliberately keeps the
                            // passed-over-foreign-closer note for provenance even though stdout no longer
                            // does (.github#2444).
                            match Writes.doneReceipt ctx.Transport ref (Done.renderReceipt ref verdict) with
                            | Error e ->
                                eprint $"fsgg-coord-engine: verified done but could not record its lifecycle receipt: %s{Errors.explain e}"
                            | Ok() -> ()
                            // Stamp the board Done. A board-write failure leaves the stamp GREEN (the work
                            // IS done) and reports the note — the same rule as the bash client. #1151: the
                            // outcome was `|> ignore`d directly under the "reports the note" comment, so a
                            // `Deferred` (queued, nothing auto-replays it) printed green with no flush remedy
                            // and the board silently drifted un-stamped. Surface it, keeping the verdict green.
                            Board.boardWrite ctx.Transport board ref.Owner ref.Repo ref.Number "Status" (Board.Set "Done") w.Id
                            |> boardWriteNote ref "Status" "Done"

                            // --flip: roll the parent up. Whether this child DISCHARGES its parent is a fact
                            // only the author knows and no board read recovers (#614), so it is the caller's
                            // to state: `--partial "why"` makes it a `Partial` that leaves the parent OPEN
                            // (naming why), and its absence asserts `Completes`. A partial fix that once had
                            // to be run as a plain `done` (no climb at all) can now flip its own status AND
                            // record on the parent that it did not complete it.
                            if opts.Flip then
                                let discharge =
                                    match opts.Partial with
                                    | Some why -> Done.Partial why
                                    | None -> Done.Completes

                                match facts.Parent with
                                | Some parent ->
                                    match Done.rollUp ctx.Transport board w.Id parent discharge with
                                    | Error e ->
                                        eprint $"fsgg-coord-engine: the stamp is GREEN, but the roll-up to %s{parent.Short} did not complete: %s{Errors.explain e}"
                                    | Ok results ->
                                        for r in results do
                                            match r with
                                            | Done.ParentClosed p -> printfn "  ↑ %s stamped Done and closed" p.Short
                                            | Done.ParentLeftOpen(p, reasons) ->
                                                eprint $"  ↑ %s{p.Short} left OPEN:"

                                                for reason in reasons do
                                                    eprint $"      %s{reason}"
                                            | Done.NoParent -> ()
                                | None -> ()

                            // ADR-0051 §4 — A ROOM DIES WITH ITS WORK. This item just completed, so any
                            // coordination room it referenced may now be empty. For each room the item's
                            // `Rooms:` line names, scan the ROOM'S repo for an OPEN issue that still references
                            // it; finding none, close the room. Derived lifecycle: no manual close, no
                            // room-lease, no litter.
                            //
                            // FAIL-CLOSED and ADVISORY, in that order. Every read here can fail, and a failure
                            // leaves the room OPEN and never touches the GREEN stamp — closing a room while a
                            // member is still open is the one error to avoid, and the stamp is a fact about the
                            // MERGE that owes nothing to a room. The completed item is already CLOSED (the stamp
                            // required State=Closed), so it is absent from `openIssues` and cannot keep its own
                            // room alive.
                            //
                            // SAME-REPO ONLY, and that guard is what makes the repo-local scan SOUND. A room in
                            // the completed item's own repo has all its members there too (`room open` is
                            // intra-repo, ADR-0027 §5), so the scan below sees every possible referrer. A room
                            // in ANOTHER repo — only reachable via a cross-repo `Rooms:` reference, which
                            // `room open` refuses to create — may have referrers this scan cannot see, so it is
                            // LEFT OPEN rather than closed on a partial view. Without this guard, finishing the
                            // last same-repo referrer would close a room a cross-repo member still needs.
                            if opts.Flip then
                                match Reads.issueBody ctx.Transport ref.Owner ref.Repo ref.Number with
                                | Error _ -> ()
                                | Ok body ->
                                    for room in Rooms.parse ref.Owner ref.Repo body do
                                        if room.Owner <> ref.Owner || room.Repo <> ref.Repo then
                                            // A cross-repo room reference — not ours to auto-close (above).
                                            ()
                                        else
                                            match Reads.openIssues ctx.Transport room.Owner room.Repo with
                                            | Error _ -> ()
                                            | Ok issues ->
                                                // .github#1794 — AN UNREADABLE BODY COUNTS AS A REFERENCE.
                                                // This predicate gates a WRITE that closes the room, so its
                                                // fail-open direction is "nobody references it any more"
                                                // over a row nobody read: the room shuts under the workers
                                                // still talking in it. `true` here costs a room that stays
                                                // open one `done` longer; `false` costs a channel that
                                                // vanishes mid-conversation.
                                                let stillReferenced =
                                                    issues
                                                    |> List.exists (fun issue ->
                                                        match issue.Body with
                                                        | Reads.BodyUnread _ -> true
                                                        | Reads.BodyRead b ->
                                                            Rooms.parse room.Owner room.Repo b |> List.contains room)

                                                if not stillReferenced then
                                                    match Writes.closeRoom ctx.Transport room with
                                                    | Ok() ->
                                                        printfn "  ⋄ %s closed — every referenced item is done (ADR-0051)" room.Short
                                                    | Error e ->
                                                        eprint
                                                            $"fsgg-coord-engine: the stamp is GREEN, but room %s{room.Short} could not be closed: %s{Errors.explain e}"

                            // #533 — A FINISHED ITEM MUST NOT KEEP ITS LOCK. `done` verified the merge, set
                            // the column Done, and rolled the parent up — and, until here, left the claim
                            // marker live for the rest of the 120m lease. A live marker's `Paths:` keep
                            // reserving its touch-set, so the item most likely to overlap a just-finished one
                            // — its own follow-up findings, filed BECAUSE you were standing in those files —
                            // is the one its own author is locked out of. This is the port's half of #533:
                            // `done --flip` set Status and never touched the marker, and `release` was the
                            // only path that dropped it — but `release` REWRITES Status, so running it on an
                            // item you just stamped clobbers the stamp you just earned.
                            //
                            // Drop OUR OWN lock, and only ours. A `Held` is obtainable only by confirming the
                            // live winner is us (`verifyHeld`), so `release` here CANNOT touch another
                            // worker's marker — deleting a claim that is not ours is `reap`'s job, and the
                            // "only your own" rule is the capability type, not a forgettable `if`. And unlike
                            // the `release` command, we do NOT restore the column: the item is Done, and Done
                            // is what stands.
                            match Writes.verifyHeld ctx.Transport opts.LeaseMinutes (WorkerId w.Id) (selfOf w) (sessionOf w) ref with
                            | Ok(Writes.Holds held) ->
                                match Writes.release ctx.Transport held with
                                | Ok _ -> ()
                                | Error e ->
                                    eprint
                                        $"fsgg-coord-engine: the stamp is GREEN, but %s{w.Id}'s claim on %s{ref.Short} could not be dropped: %s{Errors.explain e}. Run `release` (or `reap`) so it stops reserving its touch-set (#533)."
                            // #1031: our id, another session. The stamp stands — `done` verified the MERGE, which
                            // is a fact about the PR and owes nothing to whose session holds the lock. But the
                            // claim is our TWIN's, and dropping it here would delete a lock they are working
                            // behind. Say so instead: this is the shared-id hazard, not a stranded claim, and
                            // `reap` (the remedy for somebody else's marker) is the wrong tool for an id that is
                            // nominally ours.
                            | Ok(Writes.TwinHolds theirs) ->
                                eprint
                                    $"fsgg-coord-engine: %s{ref.Short} is stamped Done, but its claim carries YOUR worker id '%s{w.Id}' in a DIFFERENT session (%s{theirs.Value}) — two workers share one id (#419). Left alone: dropping it would delete a lock your twin is working behind."

                                mintRemedy ()
                            // #1646: the claim is the NAMED worker's, and we are not them. The stamp stands for
                            // the twin arm's reason — the merge is a fact about the PR — but the lock is not
                            // ours to drop, and `done` is exactly where a `--worker <them>` would look most
                            // innocent: a tidy-up at the end of somebody else's item. Left alone, and named.
                            | Ok(Writes.ImpersonatesHolder(derived, named)) ->
                                eprint
                                    $"fsgg-coord-engine: %s{ref.Short} is stamped Done, but its claim is '%s{named.Value}'s and this process is '%s{derived.Value}' — `done` drops only your OWN lock (#1646). Left alone: `%s{named.Value}` was never asked, and `--worker` does not make you them."
                            | Ok Writes.DoesNotHold ->
                                // We do not hold it. If ANOTHER worker's lock is live, this engine leaves it
                                // alone and says so — it never silently deletes a claim that is not ours.
                                match
                                    Reads.markerScan ctx.Transport ref.Owner ref.Repo ref.Number
                                    |> Result.bind (Reads.requireCompleteMarkerScan ref.Short)
                                with
                                | Ok markers ->
                                    match Reads.winner opts.LeaseMinutes markers with
                                    | Some m when m.Worker <> WorkerId w.Id ->
                                        eprint
                                            $"fsgg-coord-engine: %s{ref.Short} is stamped Done, but %s{m.Worker.Value} still holds its claim — `done` drops only your own lock; run `reap` to clear another worker's (#533)."
                                    | _ -> ()
                                | Error _ -> ()
                            | Error e ->
                                eprint
                                    $"fsgg-coord-engine: the stamp is GREEN, but %s{w.Id}'s claim on %s{ref.Short} could not be checked: %s{Errors.explain e}. If it is still held, run `release` so it stops reserving its touch-set (#533)."

                            // A follow-up is a LOCAL promise, keyed to THIS resolved worker. The irreversible
                            // work facts above must stand even if this audit cannot read a local file: a queue
                            // failure cannot unstamp a merged PR. But it must not be quiet — `Empty` means we
                            // looked and found nothing, whereas `Unreadable` means a promise may still exist.
                            // Never pop here: `done` reports the obligation; the worker drains its OWN queue
                            // sequentially after this stamp, one claimed item at a time.
                            let mutable followupsDisposed = true

                            match Followups.apply w Followups.List with
                            | Followups.Empty -> ()
                            | Followups.Listed refs ->
                                // The queue itself is local and keyed to a worker who may now disappear.
                                // Before we tell that worker to end, put the deferral on EACH owed issue.
                                // This is deliberately before any queue rewrite (there is none at this
                                // boundary): a failed issue mutation leaves the original promise intact,
                                // visible, and retryable instead of converting it into an untraceable file
                                // edit. The comment is an observation of the worker's explicit queue, not a
                                // claim that a driver has scheduled the work.
                                let mutable recorded = true

                                for owed in refs do
                                    match
                                        Writes.followupDisposition
                                            ctx.Transport
                                            owed
                                            (WorkerId w.Id)
                                            $"deferred after completing %s{ref.Short}: this worker's follow-up queue still contains this open item. Drain it sequentially, or explicitly abandon it with a reason before this worker ends."
                                    with
                                    | Ok () -> ()
                                    | Error e ->
                                        recorded <- false
                                        followupsDisposed <- false
                                        eprint
                                            $"fsgg-coord-engine: could not durably record the follow-up disposition for %s{owed.Short}: %s{Errors.explain e}. The queue was NOT rewritten; retry `done` or record an explicit disposition before ending worker %s{w.Id}."

                                if recorded then
                                    eprint
                                        $"fsgg-coord-engine: %d{List.length refs} follow-up(s) remain for worker %s{w.Id}; each is now durably recorded as deferred on its issue. Drain only your OWN queue sequentially: `scripts/fsgg-coord followup pop`; claim and complete one item before considering the next."
                            | Followups.Unreadable why
                            | Followups.Refused why ->
                                followupsDisposed <- false
                                eprint
                                    $"fsgg-coord-engine: %s{why} The done stamp stands, but do not treat this as an empty queue. Retry `scripts/fsgg-coord followup list` before ending this worker."
                            | other ->
                                followupsDisposed <- false
                                eprint $"fsgg-coord-engine: unexpected follow-up audit result %A{other}; the queue was not rewritten."

                            // #733/§4.6 — THE OTHER SAFE POINT, and the one the fleet actually reaches.
                            //
                            // Condition 3 names two boundaries: "offer after `done`, or at `next` when the
                            // worker is idle". #1056 wired `next`. But `/pnext-item` — the recipe every worker
                            // runs — calls `take` on its happy path, NOT `next`: §1 takes, §5 stamps, §6 loops
                            // back to `take`. `next` appears in that recipe exactly once, inside the "if `take`
                            // finds nothing" DIAGNOSTIC. So the conscription point was wired to the one verb a
                            // working fleet does not call, and the queue drained only when the board had no work
                            // — never on a busy board, which is precisely when drift accumulates. `done --flip`
                            // runs on EVERY completed item.
                            //
                            // It goes here, AFTER the claim is dropped (#533) rather than merely after the
                            // stamp, because that ordering is what makes the offer legal: `safePoint` refuses a
                            // worker holding a live claim, so offering before the release would offer to a
                            // worker this very function is about to make idle — and be refused, silently,
                            // forever. Condition 3's guard and #533's drop are the same instant, in this order.
                            if followupsDisposed then
                                offerChoreAfterDone ctx opts ref
                                ExitGreen
                            else
                                // The merge/stamp is irreversible and stands, but a non-green result is
                                // the terminal guard: this worker must not be recycled while its queue
                                // has no readable/durable disposition.
                                ExitRed

    do completeDelivery <- doneCmd

    // ---- #498/ADR-0044: the generated artifacts drift is subtracted against -------------------------

    /// The repo's GENERATED, CI-GATED artifacts, asked of `scripts/generated-paths` — the set a PR may
    /// change without it being drift, because nobody AUTHORS them (#309's authorship test). §1 tells a
    /// worker not to reserve these in their touch-set; `verifyPaths` then reported them as drift anyway,
    /// forever. A signal that fires on the behaviour the protocol MANDATES is one workers learn to skip
    /// past, and the one time it means a real overrun nobody reads it (#498).
    ///
    /// FAILS CLOSED, ALWAYS: an absent, unrunnable, failing, or silent `generated-paths` yields the EMPTY
    /// set, which subtracts NOTHING and leaves drift reported exactly as it is today. Never the reverse —
    /// "I could not ask what is generated" and "nothing is generated" are opposite facts, and only one of
    /// them is safe to act on (#266). The script has its own fail-closed rule per generator; this is the
    /// same rule one level up, for the script as a whole.
    ///
    /// ITS STDERR IS FORWARDED, NOT SWALLOWED, AND THAT IS THE HALF THAT MAKES FAILING CLOSED USABLE.
    /// `generated-paths` says on stderr exactly WHICH generator broke and why — it goes out of its way to
    /// ("a warning nobody can act on is a warning nobody reads"). Dropping that would leave the safe
    /// behaviour and remove the only pointer to the cause: the artifact reappears under `undeclared`, the
    /// worker is told to go look at the generator, and nothing says which one. A mute fail-closed is
    /// #266's shape one level down — right verdict, unreadable reason.
    let private generatedPaths (root: string) : Set<string> =
        let script = Path.Combine(root, "scripts", "generated-paths")

        if not (File.Exists script) then
            Set.empty
        else
            try
                let psi = ProcessStartInfo(script)
                psi.WorkingDirectory <- root
                psi.RedirectStandardOutput <- true
                psi.RedirectStandardError <- true
                psi.UseShellExecute <- false
                use p = Process.Start psi

                // BOTH pipes async, and the ordering here is the whole point rather than a style choice.
                //
                // A blocking `StandardOutput.ReadToEnd()` cannot be bounded by a later `WaitForExit t`: a
                // stuck generator never closes stdout, so the read never returns and the timeout below is
                // simply never reached. (Measured, on the first cut of this function: a `sleep 600`
                // generator hung `verify-paths` for as long as it was allowed to run, and left the child
                // alive — the timeout was decoration, and a comment promising a bound that does not exist
                // is worse than no bound, because it invites the trust it cannot earn.)
                //
                // Async also removes the symmetric deadlock: draining one pipe while the child fills the
                // other's buffer blocks them both.
                let stdout = Text.StringBuilder()
                let sync = obj ()

                p.OutputDataReceived.Add(fun e ->
                    if not (isNull e.Data) then
                        lock sync (fun () -> stdout.AppendLine e.Data |> ignore))

                // The generator's OWN reason, forwarded rather than swallowed — see the doc comment.
                p.ErrorDataReceived.Add(fun e ->
                    if not (isNull e.Data) then
                        eprint $"  | %s{e.Data}")

                p.BeginOutputReadLine()
                p.BeginErrorReadLine()

                // BOUNDED, because a HANG is the one failure this design otherwise had no answer for.
                // Everything else fails closed — absent, broken, silent all subtract nothing and say so. A
                // generator that blocks (waits on stdin, a hung git, a stalled network mount) instead hangs
                // `verify-paths` itself, which IS the merge gate: no verdict, no diagnostic, a job burning
                // to its workflow timeout. That is not fail-closed, it is fail-SILENT. Nothing else in the
                // chain bounds it — `generated-paths` does not time its own generators out — so the bound
                // goes at the edge that has a verdict to protect. 30s is three orders of magnitude past the
                // measured cost of the whole roster (~40ms): only a genuinely stuck generator reaches it.
                // `Kill true` takes the process TREE — killing the script and orphaning the generator it is
                // blocked on would leak the actual hang.
                //
                // Tunable ONLY so the gate can prove the bound without costing CI 30 idle seconds — the same
                // reason `FSGG_COORD_SCAN_TTL_SEC` exists, and the e2e suite already sets that. An untested
                // safety net is one that rots quietly (#724); a knob nothing but the test turns is cheaper
                // than not testing it. A malformed or non-positive value falls back to the default rather
                // than disabling the bound: "0" must not silently mean "wait forever".
                let timeoutMs =
                    match Environment.GetEnvironmentVariable "FSGG_GENERATED_PATHS_TIMEOUT_MS" with
                    | null
                    | "" -> 30_000
                    | v ->
                        match Int32.TryParse v with
                        | true, n when n > 0 -> n
                        | _ -> 30_000

                if not (p.WaitForExit timeoutMs) then
                    (try p.Kill true with _ -> ())

                    eprint
                        $"fsgg-coord-engine: scripts/generated-paths did not finish within %d{timeoutMs}ms and was killed — NOTHING is subtracted, so a regenerated artifact will be reported as drift below."

                    Set.empty
                else

                // The child is gone; this second, unbounded wait is the documented way to let the async
                // handlers flush what it wrote before exiting. It cannot hang — the process has exited.
                p.WaitForExit()
                let out = lock sync (fun () -> stdout.ToString())

                if p.ExitCode <> 0 then
                    eprint
                        $"fsgg-coord-engine: scripts/generated-paths exited %d{p.ExitCode} — NOTHING is subtracted, so a regenerated artifact will be reported as drift below."

                    Set.empty
                else
                    out.Split('\n')
                    |> Array.map (fun l -> l.Trim())
                    |> Array.filter (fun l -> l <> "")
                    |> Set.ofArray
            with ex ->
                eprint
                    $"fsgg-coord-engine: could not run scripts/generated-paths (%s{ex.Message}) — NOTHING is subtracted, so a regenerated artifact will be reported as drift below."

                Set.empty

    do generatedPathCollector <- generatedPaths

    // ---- .github#2324: the sdd-required route's own mandatory output is subtracted against -------------

    /// The package directories the item this PR implements is OBLIGED to produce, as `PathToken`s ready
    /// for `TouchSet.covers` — or the EMPTY list, which subtracts nothing.
    ///
    /// `work/<workId>/` and `readiness/<workId>/` are mandatory output of the `sdd-required` route itself
    /// (#2298 makes the CLAIMED WORKER author them), and nothing in filing, routing, or claiming ever puts
    /// them in the item's `Paths:` — the paths cannot be named before the item exists, and nothing
    /// revisits the declaration once they do. So `verify-paths` reported DRIFT on the behaviour the
    /// protocol MANDATES, on every single `sdd-required` item, and each one paid a `widen` to silence it.
    /// That is #498's lesson exactly — a signal that fires on the instruction it enforces is one workers
    /// learn to skip past — reached through a second door. See `DeliveryRoute.mandatorySddPaths`' own doc
    /// for why the remedy is an exemption rather than an auto-declaration, and for why declaring them
    /// nonetheless stays legal (this is NOT ADR-0044's refusal).
    ///
    /// BOUND TO THIS ITEM'S OWN RECEIPT, never to `work/`/`readiness/` as roots: another item's package is
    /// ordinary drift, and stays so.
    ///
    /// FAILS CLOSED, ALWAYS, AND SAYS SO. An unreadable comment ledger or a receipt that is not `Current`
    /// yields the empty list — "I could not ask what this route obliges" and "this route obliges nothing"
    /// are opposite facts (#266) — and the reason goes to stderr rather than being swallowed, for the same
    /// reason `generatedPaths` forwards its child's stderr: a mute fail-closed leaves the right verdict and
    /// removes the only pointer to its cause. It takes the body the caller ALREADY read and one complete,
    /// paginated REST ledger read, with no second `issueBody`.
    let private sddPackageTokens (ctx: Context) (issue: Ref) (body: string) : PathToken list =
        let nothingSubtracted (why: string) =
            eprint
                $"fsgg-coord-engine: could not establish %s{issue.Short}'s delivery route (%s{why}) — NOTHING is subtracted for the sdd-required route's mandatory work/<id> + readiness/<id> output, so it will be reported as drift below."

            []

        match readDeliveryRouteComments ctx issue with
        | Error e -> nothingSubtracted (Errors.explain e)
        | Ok comments ->
            match routeEvidence issue.Canonical comments with
            // A `lightweight` route reaches here too and answers the empty list from
            // `mandatorySddPaths` — correctly, and SILENTLY: it has no mandatory package, so there is
            // nothing we failed to read and nothing to warn about.
            | DeliveryRoute.Current receipt -> DeliveryRoute.mandatorySddPaths receipt |> List.map TouchSet.classify
            | DeliveryRoute.Stale reasons
            | DeliveryRoute.Unreadable reasons -> nothingSubtracted (String.concat "; " reasons)

    // ---- verify-paths ----------------------------------------------------------------------------------

    /// Check a PR's changed files against the touch-set declared by the issue it implements.
    ///
    /// THE VERDICT VOCABULARY IS THE BASH CLIENT'S, because the shim will run one where the other ran:
    ///   OK      — every changed file is inside the declared touch-set.
    ///   DRIFT   — a file falls outside it (named), and the PR should widen or split.
    ///   SKIP    — nothing to verify against (no touch-set, or the issue can't be identified). Green.
    ///   INVALID — the declared touch-set has only unmatchable tokens (#273).
    ///
    /// "I COULD NOT CHECK" IS NEVER A VERDICT (#322). An unreadable head ref, body, or file list is an
    /// ERROR — even under --warn, which downgrades a real DRIFT/INVALID to advisory but cannot downgrade a
    /// read that never happened. Stamping "stays inside its touch-set" on a subject nobody looked at is the
    /// exact fail-open this command exists to prevent.
    let verifyPaths (ctx: Context) (opts: Options) : int =
        match opts.Pr with
        | None ->
            eprint "fsgg-coord-engine: verify-paths needs --pr <n>."
            ExitError
        | Some pr ->
            let owner = ctx.Owner

            // An explicit `--issue` (owner/repo#n, repo#n, or a URL) names the issue the PR implements —
            // no branch or closing-ref resolution needed. It is parsed up front because its repo is
            // authoritative: it decides the repo when `--repo` is absent, and a `--issue` in a DIFFERENT
            // repo than `--repo` is a straddle the tool refuses (#479).
            let issueRef =
                match opts.Issue with
                | None -> Ok None
                | Some raw ->
                    match parseRef ctx raw with
                    | Ok r -> Ok(Some r)
                    | Error m -> Result.Error m

            match issueRef with
            | Result.Error m ->
                eprint $"fsgg-coord-engine: verify-paths --issue: %s{m}"
                ExitError
            | Ok issueRef ->

            // The repo the PR is in: `--repo` (a registry short-id / owner/repo / literal name, reduced the
            // way every worker command reduces it — case 13's resolve_repo), else the `--issue`'s repo (the
            // issue decides when no `--repo` is given), else #430's git-remote default — the repo of the
            // checkout you are standing in, read FREE and offline from `git config remote.origin.url`, the
            // same signal `next`/`take`/`batch`/`who` scope to (#480). Deliberately NOT `gh repo view`
            // (bash's fallback): repo resolution must never spend GraphQL, so an exhausted budget can never
            // be dressed up as "not inside a checkout" — the exact fail this whole command guards against.
            // With no remote either, there is no subject to check, so it refuses (an earned verdict, since
            // `git config` failing is not a rate limit dressed up as one).
            let repo =
                match opts.Repo with
                | Some r -> Ok(resolveRepo r)
                | None ->
                    match issueRef with
                    | Some ir -> Ok ir.Repo
                    | None ->
                        match gitRemoteRepo () with
                        | Some slug -> Ok(resolveRepo slug)
                        | None ->
                            eprint
                                "fsgg-coord-engine: verify-paths is not inside a GitHub checkout (no git remote), and neither --repo nor --issue names the repo the PR is in. Name it with --repo FS-GG/<repo>, or the issue with --issue <ref>."

                            Result.Error ExitError

            match repo with
            | Result.Error rc -> rc
            | Ok repo ->

            // .github#2107 — the org's own board shorthand `<repo>#<n>` (RefParsing's OWN 'short' form,
            // the one `take`/`claim`/`widen`/every recipe teaches) is NOT GitHub's closing-keyword
            // grammar: it wants a bare `#<n>` for a same-repo issue, or `owner/repo#<n>` for a cross-repo
            // one. Written next to a closing verb it renders as plain text — GitHub never links it, the
            // merge never closes the issue, and there is no repair once the PR has merged (editing the
            // body does not replay the close). Checked HERE, independently of the touch-set verdict below,
            // because this is the one moment fixing it is free — the PR is still open.
            //
            // A prBody READ FAILURE never contaminates the touch-set verdict: this is an ADDITIONAL check
            // bolted onto an existing command, and a network hiccup on this one extra call must not turn an
            // otherwise-healthy touch-set run red.
            let closingFindings =
                match Reads.prBody ctx.Transport owner repo pr with
                | Error e ->
                    eprint
                        $"fsgg-coord-engine: verify-paths: could not read PR #%d{pr}'s body to check for board-shorthand closing keywords (%s{Errors.explain e}) — skipping that check."

                    []
                | Ok body -> RefParsing.boardShorthandCloses body

            // Applied to every leaf that would otherwise report GREEN or RED: a closing-keyword defect is
            // worth failing on even when the touch-set itself is clean, or when there is no touch-set to
            // check at all. Left UNCHANGED on every other code (NO-VERDICT, ERROR, the straddle refusal) —
            // those already mean "no confident verdict was reached", and this is not the check that gets to
            // override that.
            let combine (rc: int) : int =
                if List.isEmpty closingFindings || not (rc = ExitGreen || rc = ExitRed) then
                    rc
                else
                    printfn
                        "FSGG-CLOSES DEFECT — PR #%d's body writes a closing keyword next to the board's OWN '<repo>#<n>' shorthand, which GitHub's closing-keyword grammar does not parse:"
                        pr

                    for f in closingFindings do
                        printfn "    `%s`" f.Matched

                        printfn
                            "      GitHub will NOT close %s from this. Use a bare '#%s' (same-repo) or 'owner/repo#%s' (cross-repo)."
                            f.Ref
                            f.Number
                            f.Number

                    eprint
                        "  Fix the PR body now — this is unrecoverable once the PR is merged (editing a merged PR's body does not replay the close, .github#2107)."

                    ExitRed

            // #479: `--repo` and `--issue` naming DIFFERENT repos is a straddle — a touch-set in one repo
            // says nothing about the files changed in the other, and printing a verdict on the wrong subject
            // is the exact fail-open this command exists to prevent (#266). It fails CLOSED both by default
            // AND under --warn: --warn downgrades a real DRIFT to advisory, but it cannot license a verdict on
            // a subject that was never compared. (Only reachable when BOTH flags are present — with `--repo`
            // absent, `repo` IS the issue's repo and they agree by construction.)
            match issueRef with
            | Some ir when opts.Repo.IsSome && not (String.Equals(ir.Repo, repo, StringComparison.OrdinalIgnoreCase)) ->
                // No FSGG-PATHS verdict — the touch-set drift gate greps stdout for one, and a straddle
                // produces none; it exits non-zero and the gate reads that as the failure it is.
                eprint (
                    sprintf
                        "fsgg-coord-engine: verify-paths refuses to straddle a repo boundary — PR #%d in %s/%s vs the touch-set of %s/%s#%d, in another repo. The touch-set was NOT checked (a touch-set there says nothing about the files changed here). Name the PR's own issue with --issue, or drop --issue to resolve it from the branch."
                        pr
                        owner
                        repo
                        ir.Owner
                        ir.Repo
                        ir.Number
                )

                ExitNoVerdict
            | _ ->

            // The issue a PR implements: an explicit `--issue` (which bypasses the head-ref read entirely —
            // #322, an unreadable head ref must not drag down a run that named its issue), else its
            // `item/<n>-*` branch, else what it declares it closes.
            let resolveIssue () : Result<Ref option, Errors.IoError> =
                match issueRef with
                | Some ir -> Ok(Some ir)
                | None ->
                    match Reads.prHeadRef ctx.Transport owner repo pr with
                    | Error e -> Result.Error e
                    | Ok head ->
                        let m = Text.RegularExpressions.Regex.Match(head, @"^item/(\d+)-")

                        if m.Success then
                            Ok(Some { Owner = owner; Repo = repo; Number = int m.Groups.[1].Value })
                        else
                            // Not an item branch — ask what it closes.
                            Reads.prClosingRef ctx.Transport owner repo pr

            match resolveIssue () with
            | Error e -> fail e
            | Ok None ->
                // Can't tell which issue this PR implements. SKIP — not a verdict, and green: a PR that
                // implements no tracked item has no touch-set to drift from.
                printfn
                    "FSGG-PATHS SKIP — cannot tell which issue PR #%d implements (branch is not item/<n>-…, and it closes no issue)."
                    pr

                combine ExitGreen
            | Ok(Some issue) ->
                // Repo-relative touch-sets: a PR in repo A that closes an issue in repo B cannot be checked
                // against B's paths — those say nothing about A's files (#353).
                if not (String.Equals(issue.Repo, repo, StringComparison.OrdinalIgnoreCase)) then
                    printfn
                        "FSGG-PATHS SKIP — PR #%d is in %s/%s but implements %s/%s#%d, in another repo — a touch-set there says nothing about the files changed here."
                        pr
                        owner
                        repo
                        issue.Owner
                        issue.Repo
                        issue.Number

                    combine ExitGreen
                else

                match Reads.issueBody ctx.Transport issue.Owner issue.Repo issue.Number with
                | Error e -> fail e
                | Ok body ->
                    match TouchSet.parse body with
                    | Undeclared
                    | DeclaredNone
                    // `Paths: any` reserves nothing and permits any file, so there is no boundary a PR
                    // could stray outside of — nothing to verify against (#1103 leg 8).
                    | DeclaredChore ->
                        printfn "FSGG-PATHS SKIP — %s declares no 'Paths:' touch-set; nothing to verify against." issue.Short
                        combine ExitGreen
                    | Unreadable reason ->
                        // Should not happen (we just read the body), but the type demands it be handled, and
                        // "I could not read the body" is an error, never a SKIP.
                        eprint $"fsgg-coord-engine: could not read %s{issue.Short}'s touch-set: %s{reason}"
                        ExitError
                    | Declared tokens ->
                        let unmatchable =
                            tokens
                            |> List.choose (function
                                | Unmatchable u -> Some u
                                | Matchable _ -> None)

                        if List.length unmatchable = List.length tokens then
                            // EVERY token is unmatchable — the declaration reserves nothing (#273). That is
                            // INVALID, not "everything drifts": the touch-set is the broken thing.
                            let bad = String.Join(", ", unmatchable)
                            printfn "FSGG-PATHS INVALID — %s declares only unmatchable tokens: %s" issue.Short bad
                            eprint $"  %s{Schedulability.TouchSetGrammar}"
                            combine (if opts.Warn then ExitGreen else ExitRed)
                        else

                        match Reads.prFiles ctx.Transport owner repo pr with
                        | Error e -> fail e
                        | Ok files ->
                            let drift =
                                files
                                |> List.filter (fun f -> not (tokens |> List.exists (fun t -> TouchSet.covers t f)))

                            // #498/ADR-0044: the generated, CI-gated artifacts this PR REGENERATED are drift
                            // by the letter of the touch-set and are not a finding — §1 forbids declaring
                            // them, so reporting them is the gate firing on its own instruction.
                            //
                            // SUBTRACT ONLY WHEN THE CHECKOUT IS THE PR'S OWN REPO. `verify-paths --pr N
                            // --repo <other>` is a legal call, and the local generators say NOTHING about
                            // another repo's artifacts: subtracting this repo's set there would suppress real
                            // drift in a repo we never asked. That is the fail-open this change exists to
                            // avoid, reached from the one direction the roster cannot see. Owner included —
                            // `otherorg/.github` is not `FS-GG/.github`, and only the slug knows that.
                            // ASK ONLY WHEN THERE IS DRIFT TO SUBTRACT FROM. Not merely to save the three
                            // generator forks on the commonest verdict — the reason is the DIAGNOSTIC. A
                            // failing `generated-paths` reports that nothing was subtracted "so a regenerated
                            // artifact will be reported as drift below"; on a PR with no drift, that sentence
                            // is FALSE and it lands in the sticky comment of a GREEN PR (the workflow merges
                            // our stderr into the file it publishes). A gate that cries wolf on the happy path
                            // teaches one lesson — that its output is noise — and the next warning will be
                            // real (#698). With no drift there is nothing to subtract and nothing to say.
                            //
                            // .github#2324 shares this guard and this reason. The route receipt read is a
                            // bounded GraphQL call, and its own fail-closed diagnostic ("NOTHING is
                            // subtracted … so it will be reported as drift below") is FALSE on a PR with no
                            // drift — the same sentence in the same sticky comment on the same green PR.
                            // Both subtractions therefore hang off one `List.isEmpty drift` gate rather
                            // than two, so neither can be re-armed on the happy path by accident.
                            let sddPackage, regenerated, undeclared =
                                if List.isEmpty drift then
                                    [], [], []
                                else

                                // SUBTRACTED FIRST, and the order is not arbitrary: an SDD package file is
                                // never a generated, CI-gated artifact, so the two sets are disjoint in
                                // practice — but partitioning the sdd bucket out first means a file can
                                // only ever land in ONE reported bucket, and a reader is never asked to
                                // reconcile the same path appearing twice under two different reasons.
                                let sddPackage, rest =
                                    let tokens = sddPackageTokens ctx issue body
                                    drift |> List.partition (fun f -> tokens |> List.exists (fun t -> TouchSet.covers t f))

                                let subtractable =
                                    let checkoutIsSubject =
                                        match gitRemoteRepo () with
                                        | Some slug ->
                                            String.Equals(slug, $"%s{owner}/%s{repo}", StringComparison.OrdinalIgnoreCase)
                                        | None -> false

                                    if not checkoutIsSubject then
                                        Set.empty
                                    else
                                        match KitDigest.kitRoot () with
                                        | Some root -> generatedPaths root
                                        | None -> Set.empty

                                let regenerated, undeclared =
                                    rest |> List.partition (fun f -> Set.contains f subtractable)

                                sddPackage, regenerated, undeclared

                            // BEFORE THE VERDICT, SO IT FIRES ON `OK` TOO — and `OK` is the case that needs it.
                            // The kit obligation is about what the PR CHANGED, not what it declared: a PR that
                            // edits a kit source and never relocks reds `main` whether or not it drifted, so an
                            // `OK` verdict must not read as "safe to merge" (#469). That is exactly #509's
                            // complaint — the worker's own pre-merge check is green while `main` is about to go
                            // red — and it is the reason bash armed this here (`kit_digest_warn "$changed" "PR
                            // #$pr"`). THE PORT DROPPED IT: the D-phase swap carried the `widen` call site over
                            // and left this one behind, so the warning stopped firing on the one command §5
                            // tells every worker to run. A gate that silently stops running is #266's whole
                            // shape, which is why it is restored here rather than left to the merge.
                            KitDigest.digestWarn ()

                            // The regenerated set is reported on BOTH verdicts and decides NEITHER — it is
                            // context, not a finding. Printed after the verdict line so the first line of
                            // output stays the answer, and named `regenerated (expected)` so a reader can
                            // tell at a glance which list they are being asked to act on.
                            let reportRegenerated () =
                                if not (List.isEmpty regenerated) then
                                    printfn "  regenerated (expected) — generated + CI-gated, so not declarable (ADR-0044):"

                                    for f in regenerated do
                                        printfn "    %s" f

                            // .github#2324 — REPORTED, NEVER SILENTLY SUBTRACTED, and on BOTH verdicts for
                            // the same reason `regenerated` is: an invisible subtraction is indistinguishable
                            // from a gate that stopped looking. Naming the bucket and its reason is what lets
                            // a reviewer check the exemption instead of trusting it.
                            let reportSddPackage () =
                                if not (List.isEmpty sddPackage) then
                                    printfn
                                        "  sdd package (expected) — mandatory output of %s's sdd-required delivery route, so not required in Paths: (.github#2324):"
                                        issue.Short

                                    for f in sddPackage do
                                        printfn "    %s" f

                            if List.isEmpty undeclared then
                                printfn "FSGG-PATHS OK — PR #%d stays inside the touch-set declared by %s." pr issue.Short
                                reportRegenerated ()
                                reportSddPackage ()
                                combine ExitGreen
                            else
                                printfn "FSGG-PATHS DRIFT — PR #%d changes files outside the touch-set declared by %s:" pr issue.Short

                                printfn "  undeclared (review):"

                                for f in undeclared do
                                    printfn "    %s" f

                                reportRegenerated ()
                                reportSddPackage ()
                                eprint "  Widen the touch-set (scripts/fsgg-coord widen), or split the PR."
                                combine (if opts.Warn then ExitGreen else ExitRed)

    // ---- identity --------------------------------------------------------------------------------------

    let whoami (opts: Options) : int =
        if opts.Mint then
            printfn "export FSGG_WORKER=%s" (Identity.mint ())
            eprint "fsgg-coord-engine: minted a worker id — eval this line, or export it, in EACH worker's shell."
            ExitGreen
        else
            match Identity.resolve opts.Worker with
            | Error msg ->
                eprint $"fsgg-coord-engine: %s{msg}"
                ExitError
            | Ok w ->
                for line in Identity.explain w do
                    printfn "%s" line

                // #419: when the id was DERIVED from a session that shares one id across every subagent, a
                // fan-out would hand N workers the same id — the exact collision ADR-0027 moved the lock off
                // the shared account to avoid. Warn to STDERR (stdout is the id itself), point at the mint
                // COMMAND, and offer NO literal to copy: a warning that named an example id is one agents
                // pattern-match on and paste, which is how #419 happened.
                match w.Provenance with
                | Identity.FromSharedSession _ ->
                    eprint
                        "fsgg-coord-engine: WARNING — this id was derived from a session that shares one id across every subagent, so a fan-out of workers would all draw it and collide on each other's locks (#419)."

                    eprint "  Give EACH worker a unique id (do NOT invent one):  eval \"$(scripts/fsgg-coord whoami --mint)\""
                | _ -> ()

                ExitGreen

    // ---- predicate: the ADR-0050 registry oracle (call-site A, local files, .github#1202) --------------
    //
    // The impure readers this command shares with the flip-time enrichment (`resolveOwnerDeclaration` &c.)
    // live UP beside `wholeOf` — the flip gate (call-site B, .github#1213) needs them before the offer path,
    // and a module reads top-down, so ONE copy sits above both callers rather than a second here (#485).

    /// `predicate` — the oracle as a query: one verdict word on stdout, the decision in the exit code
    /// (0 agrees / 3 contradicts / 4 unknown, the `landable` shape). `--json` emits the structured
    /// verdict the filing-time workflow reads to build its auto-comment (`.ownerValue`, `.note`).
    let predicate (opts: Options) : int =
        let registryPath = envOr "FSGG_REGISTRY" "registry/skills.yml"
        let reposRoot = envOr "FSGG_REPOS_ROOT" ".repos"

        let assertion: Result<RegistryPredicate.Assertion option, string> =
            match opts.Args with
            | [ id; field; value ] -> Ok(Some { Id = id; Field = field; Value = value })
            | [] ->
                let body = Console.In.ReadToEnd()
                if body.Trim() = "" then Ok None else Ok(RegistryPredicate.parseAssertion body)
            | _ -> Error "predicate: give `<id> <field> <value>`, or a cross-repo-request body on stdin"

        match assertion with
        | Error msg ->
            eprint $"fsgg-coord-engine: %s{msg}"
            ExitError
        | Ok None ->
            // No structured assertion in the request — nothing to refute. A no-op, exit green.
            match opts.Render with
            | Json -> printfn "{\"verdict\":\"none\"}"
            | Text -> eprint "fsgg-coord-engine: no registry assertion in this request — nothing to check."

            ExitGreen
        | Ok(Some a) ->
            let verdict =
                if not (File.Exists registryPath) then
                    RegistryPredicate.Unknown(
                        sprintf
                            "no registry at `%s` — the oracle is authority-scoped and fails closed where `registry/` is absent (ADR-0042/ADR-0050)"
                            registryPath
                    )
                else
                    let rows = RegistryPredicate.parseRows(File.ReadAllText registryPath)

                    // classify short-circuits on a missing row / unsupported field before it reads `owner`,
                    // so resolve the manifest only when it will actually be consulted.
                    let owner =
                        match RegistryPredicate.findRow rows a.Id with
                        | Some row when RegistryPredicate.supportsField a.Field ->
                            resolveOwnerDeclaration reposRoot row a.Field
                        | _ -> RegistryPredicate.Silent

                    RegistryPredicate.classify rows owner a

            match opts.Render with
            | Json ->
                let result: Render.PredicateResult =
                    { Verdict = RegistryPredicate.name verdict
                      Id = a.Id
                      Field = a.Field
                      Value = a.Value
                      OwnerValue =
                        match verdict with
                        | RegistryPredicate.Contradicts(ov, _) -> Some ov
                        | _ -> None
                      Note =
                        match verdict with
                        | RegistryPredicate.Contradicts(_, n) -> Some n
                        | _ -> None
                      Reason =
                        match verdict with
                        | RegistryPredicate.Unknown r -> Some r
                        | _ -> None }

                printfn "%s" (Render.renderPredicateJson result)
            | Text ->
                printfn "%s" (RegistryPredicate.name verdict)

                match verdict with
                | RegistryPredicate.Agrees -> ()
                | RegistryPredicate.Contradicts(ov, note) -> eprint (sprintf "  owner declares `%s: %s` — %s" a.Field ov note)
                | RegistryPredicate.Unknown reason -> eprint (sprintf "  %s" reason)

            match verdict with
            | RegistryPredicate.Agrees -> ExitGreen
            | RegistryPredicate.Contradicts _ -> ExitRed
            | RegistryPredicate.Unknown _ -> ExitNoVerdict

    // ---- the dispatcher for the IO commands ------------------------------------------------------------

    /// THE PER-RECEIVER OPERATION LOCK — the item CAS, unchanged, on a third subject.
    ///
    /// This is the `op=dispatch:*` mechanism of the GitHub-native executor fencing design §4.1, filed as
    /// `.github#2312` under `.github#1858`. It answers ONE question — *"is anyone dispatching against this
    /// receiver right now?"* — which is per receiver, concurrent, and asked and answered inside one
    /// synchronous window by the caller that holds the lock. Merge does NOT use it: a lease-based lock is
    /// verifiable only by a reader running inside the lease, and the merge verifier is a queued CI job, so
    /// merges are fenced by the lease-free election (§4.2) instead.
    ///
    /// **THE CAS WRITE PATH GAINS NO CODE, NO PREFIX, NO FIELD AND NO PARAMETER, AND THAT IS CHECKABLE
    /// RATHER THAN ASSERTED.** Everything below is composition: `Writes.claimScoped` is already a general
    /// comment-order CAS over an arbitrary issue ref — "not item-specific; it is *item-configured*, by its
    /// caller, through a callback" (ADR-0041) — so a new caller supplies a lock ref and stub callbacks and
    /// is done. This module adds no function to `Writes` and no field to the marker. Mutual exclusion is
    /// answered by the SUBJECT (one lock issue per receiver), never by anything written in the marker, so
    /// the operation key is deliberately absent from it: the opkey answers idempotence, which is a
    /// different question asked at a different time (`Operation`, slice 1).
    ///
    /// **`#516`'s ONE-ITEM-PER-WORKER REFUSAL IS NOT TRIPPED BY TAKING A GRANT WHILE HOLDING AN ITEM.**
    /// That check (`heldElsewhere`) scans the target repo's in-flight **board** items for a live claim held
    /// by this worker on a different item, and the lock issue is off-board — closed, never added to the
    /// project, exactly as the chore lock is (ADR-0041's own "`who` and `reap` do not see the chore lock").
    /// A grant is not a board item, so it is not in the set that check examines.
    ///
    /// **WHAT THE TEST ACTUALLY PROVES, STATED NARROWLY BECAUSE THE OBVIOUS CLAIM OVERREACHES IT.**
    /// `OpLockTests` asserts that `acquire` bills the GraphQL meter ZERO times, and Projects v2 is
    /// reachable only over GraphQL — so what is pinned is *"acquiring the lock never reads the board"*.
    /// That is NOT the same proposition as *"the lock issue is not on the board"*: board membership is a
    /// fact about GitHub, and no scripted-transport unit test can establish it, because the fixture answers
    /// whatever it is told to. The membership half was verified out-of-band instead, by reading all eight
    /// lock issues back from the API (`projectItems` empty on every one), and is maintained by the rule in
    /// each issue's body that it stays closed and off the board. Two halves, two different kinds of
    /// evidence; conflating them would let a unit test appear to certify something it cannot see.
    module OpLock =

        /// TEN MINUTES, matching the chore lock's, and for the same argument. This bounds how long a DEAD
        /// executor stalls one receiver's dispatch queue — not how long a live one may take, because a live
        /// holder heartbeats. A dispatch is a handful of API calls; a lease long enough to cover a hung
        /// process is a lease that makes every other executor wait out a corpse.
        let LeaseMinutes = 10

        /// Why the operation lock was not obtained. A REFUSAL in every arm — there is no "proceed anyway",
        /// because a fence that cannot establish it holds the lock must not act (#266, #421).
        ///
        /// Each arm is separate so a caller can report WHICH fact stopped it rather than re-deriving it from
        /// the input it already had. Collapsing them into one `None` is what makes an unroutable receiver
        /// indistinguishable from a busy one, and those two need opposite responses: the first is a
        /// configuration defect somebody must fix, the second is the lock working correctly.
        type Refusal =
            /// NO LOCK REF FOR THIS RECEIVER — design §4.1's "absent ref ⇒ refuse", the fail-closed arm.
            /// An unrostered repository, or a caller under an owner whose locks the table does not know.
            /// This is a REFUSAL and never a licence to dispatch unfenced, which is precisely what the
            /// `.github#1858` executor did.
            | NoLockRef of owner: string * receiver: string
            /// ANOTHER EXECUTOR HOLDS THIS RECEIVER'S LOCK, and their lease is live. Their worker id, so the
            /// caller can address them. This arm is the lock WORKING.
            | HeldByAnother of holder: WorkerId
            /// THE MARKER CARRIES OUR WORKER ID UNDER A DIFFERENT SESSION (#419). An id two executors share
            /// is not a lock, and adopting their live grant as our own is the exact defect `.github#1858`
            /// measured — two executors, one id, one claim, both acting.
            | Twin of theirs: SessionId
            /// WE ASKED TO ACT AS A WORKER WE ARE NOT (#1646).
            | Impersonates of derived: WorkerId * named: WorkerId
            // WE DO NOT HOLD THIS RECEIVER'S LOCK — the RELEASE path's arm, and it has no acquire-path
            // counterpart because acquiring is how you come to hold one. Distinct from `HeldByAnother`,
            // which says somebody else's live marker is there: this arm also covers the case where the
            // issue carries no live marker at all, so there is nothing to drop. Both are refusals, and
            // keeping them apart is what lets a caller tell "my grant already lapsed" from "another
            // executor took the receiver while I was dispatching" — two facts that need opposite
            // responses, exactly as `NoLockRef` and `HeldByAnother` do.
            | NotHeld of owner: string * receiver: string
            /// WE COULD NOT TELL. A failed read, an unparseable marker, or a lost re-read. Never a yes.
            | Undetermined of detail: string

        /// One human-readable line per refusal, for a caller that must report why it did not dispatch.
        /// Carries no judgement and decides nothing.
        let describe (refusal: Refusal) : string =
            match refusal with
            | NoLockRef(owner, receiver) ->
                $"no operation-lock issue is known for %s{owner}/%s{receiver} — refusing to dispatch unfenced"
            | HeldByAnother holder ->
                $"worker '%s{holder.Value}' holds this receiver's operation lock and their lease is live"
            | Twin theirs ->
                $"the live grant carries our worker id under a different session (%s{theirs.Value}) — two executors sharing one id is not a lock"
            | Impersonates(derived, named) ->
                $"refusing to take this receiver's operation lock as '%s{named.Value}' while acting as '%s{derived.Value}'"
            | NotHeld(owner, receiver) ->
                $"we do not hold %s{owner}/%s{receiver}'s operation lock — there is no grant of ours to drop"
            | Undetermined detail -> $"could not establish who holds this receiver's operation lock: %s{detail}"

        /// ACQUIRE the operation lock for one receiver, or say exactly why not.
        ///
        /// `Writes.claimScoped`, unchanged, with the two board callbacks stubbed and nothing admitted — and
        /// those stubs ARE the configuration (§4.1). `readPreviousStatus` is `None` because `claim` reads a
        /// previous column only to restore it on release and this issue has no column; `readPathRepo` is
        /// `None` because an off-board issue has no board path-scope projection; `admitNew` is `Ok()`
        /// because there is no intake policy on a lock subject.
        ///
        /// `RefuseLiveHolder`, with no flag that changes it. The steal is a RECOVERY route for an item whose
        /// holder died with written work stranded on it; an operation lock holds no work, so a live holder
        /// simply means another executor is already dispatching against this receiver — which is the one
        /// thing this lock exists to prevent. Forcing it would put two executors on one receiver, and that
        /// is the incident, not the remedy.
        let acquire
            (transport: Transport.IGitHubTransport)
            (worker: WorkerId)
            (self: Writes.SelfIdentity)
            (session: SessionId option)
            (extra: Ref list)
            (owner: string)
            (receiver: string)
            : Result<Writes.Held, Refusal> =
            // 1. WHOSE LOCK? — first, because it is a pure string match that spends nothing, and because a
            //    receiver with no lock is a guaranteed refusal that must not cost a network round trip.
            match Options.opLockRef extra owner receiver with
            | None -> Result.Error(NoLockRef(owner, receiver))
            | Some lockRef ->
                match
                    Writes.claimScoped
                        transport
                        LeaseMinutes
                        Writes.RefuseLiveHolder
                        ignore
                        worker
                        self
                        session
                        lockRef
                        (fun () -> None)
                        (fun () -> None)
                        (fun () -> Ok())
                with
                // A fresh win and a re-claim are both "we hold it now". They differ in whether the CAS ran,
                // which matters to a caller whose id may be shared — but not to whether the lock is ours.
                | Ok(Writes.Won(held, _))
                | Ok(Writes.Renewed(held, _)) -> Ok held
                // `Stolen` is unreachable under `RefuseLiveHolder` and is NOT folded into the success arm:
                // if it ever arrives, the force policy above has changed and this composition's argument for
                // refusing a live holder has gone with it. Failing closed on it is the honest answer.
                | Ok(Writes.Stolen _) ->
                    Result.Error(
                        Undetermined "the CAS reported a steal under RefuseLiveHolder — the force policy and this fence disagree"
                    )
                | Ok(Writes.Lost holder) -> Result.Error(HeldByAnother holder)
                | Ok(Writes.Twin theirs) -> Result.Error(Twin theirs)
                | Ok(Writes.Impersonates(derived, named)) -> Result.Error(Impersonates(derived, named))
                | Ok other -> Result.Error(Undetermined(string other))
                | Result.Error e -> Result.Error(Undetermined(Errors.explain e))

        /// RELEASE the operation lock. Takes the capability, exactly as every other release does, so a
        /// marker nobody holds cannot be dropped by naming it.
        ///
        /// The board column this hands back is always `None` — there is no column, because the lock issue is
        /// off-board — and that is discarded rather than reported: a caller who reads a restored column here
        /// is reading a fact about an item this subject is not.
        // RE-OBTAIN the capability for a grant this PROCESS did not take — `Writes.verifyHeld` on the
        // receiver's lock ref, with `acquire`'s refusal vocabulary.
        //
        // **IT EXISTS BECAUSE ACQUIRE AND RELEASE ARE NECESSARILY DIFFERENT PROCESSES, AND THAT IS THE
        // DESIGN RATHER THAN AN AWKWARDNESS.** The grant is a capability the *broker* verifies, and the
        // broker is a queued CI job: design §4.1 requires the lock to be taken "immediately before a
        // dispatch and released after it", and the dispatch happens between two invocations of this
        // engine. A `Held` cannot survive that gap — it is an in-process value with no public constructor,
        // deliberately (`Writes.Held`) — so the release path has to re-establish it from GitHub. This is
        // the same shape `release <ref>` already uses for an ordinary item claim, and it is reached
        // through the same function, so "is this marker ours?" keeps ONE answer.
        //
        // **IT IS NOT `Reads.lowestId` AND IT IS NOT `Reads.reserver`.** Dropping a lock is the most
        // destructive act in this module, and `verifyHeld` is the only door to `Held` that applies the
        // impersonation and twin predicates `claim` applies — the #1031/#1646 chain. Selecting the marker
        // by lowest id and deleting it would delete whatever marker happened to be first, which under a
        // shared worker id is precisely #550.
        let held
            (transport: Transport.IGitHubTransport)
            (worker: WorkerId)
            (self: Writes.SelfIdentity)
            (session: SessionId option)
            (extra: Ref list)
            (owner: string)
            (receiver: string)
            : Result<Writes.Held, Refusal> =
            // The free question first, for `acquire`'s reason verbatim: a receiver with no lock issue is a
            // guaranteed refusal and must not cost a network round trip.
            match Options.opLockRef extra owner receiver with
            | None -> Result.Error(NoLockRef(owner, receiver))
            | Some lockRef ->
                match Writes.verifyHeld transport LeaseMinutes worker self session lockRef with
                | Ok(Writes.Holds h) -> Ok h
                | Ok Writes.DoesNotHold -> Result.Error(NotHeld(owner, receiver))
                | Ok(Writes.TwinHolds theirs) -> Result.Error(Twin theirs)
                | Ok(Writes.ImpersonatesHolder(derived, named)) -> Result.Error(Impersonates(derived, named))
                | Result.Error e -> Result.Error(Undetermined(Errors.explain e))

        let release (transport: Transport.IGitHubTransport) (held: Writes.Held) : Errors.IoResult<unit> =
            Writes.release transport held |> Result.map ignore

        // THE PER-DEPLOYMENT OP-LOCK ROSTER, read from `FSGG_COORD_OP_LOCKS`.
        //
        // This is what makes `opLockRef`'s `extra` parameter reachable from production at all. Without it
        // the only production caller would pass `[]` for ever, and a documented injection point no code
        // path can reach is the same defect this whole row is reopened for — a mechanism with no writer,
        // green under its own tests.
        //
        // `parseChoreLocks` is CALLED rather than copied, and the name is the only thing chore-shaped
        // about it: it is a comma-separated `owner/repo#n` → `Ref list` reader that drops an unparseable
        // token instead of throwing, which is exactly this roster's grammar and exactly its fail-closed
        // polarity — a dropped token degrades to "no lock for that receiver", and `NoLockRef` is a
        // refusal, so a typo costs a dispatch that does not happen rather than one that happens unfenced.
        // Writing a second parser here to get a better name would be the copy `.github#485` is about.
        //
        // A SEPARATE VARIABLE FROM `FSGG_COORD_CHORE_LOCKS`, because they name different subjects: the
        // chore lock and the operation lock are two locks on two issues answering two questions, and a
        // tenant that repointed one would otherwise silently repoint the other (design §4.1 — "sharing the
        // chore lock's issue would make a chore drain and a dispatch operation serialise against each
        // other, which is two questions answered in one colour").
        let roster () : Ref list = parseChoreLocks (env "FSGG_COORD_OP_LOCKS" "")

        // `owner/repo` → `(owner, repo)`, for the two arguments `opLockRef` takes.
        //
        // A PROJECTION, NOT THE VALIDATOR, and the difference matters. `Operation.compose` owns whether a
        // receiver is well formed on the wire — its `ReceiverNotFullyQualified` arm is the authority, it
        // is the same rule the broker recomputes, and `acquire` below runs it BEFORE this. What is left
        // here is only splitting an already-accepted string into the pair a lookup wants.
        //
        // It cannot fail open, which is why `release` (which composes no key and so has no compose arm in
        // front of it) may lean on it alone: anything this returns is fed to `opLockRef`, and a repo that
        // is not on the embedded roster or in `FSGG_COORD_OP_LOCKS` comes back `None` → `NoLockRef` →
        // refuse. The worst a lax split can do is turn a malformed receiver into a refusal with a slightly
        // less specific message.
        let splitReceiver (receiver: string) : Result<string * string, string> =
            match receiver.Split('/') with
            | [| owner; repo |] when owner.Trim() <> "" && repo.Trim() <> "" -> Ok(owner.Trim(), repo.Trim())
            | _ ->
                Result.Error
                    $"receiver '%s{receiver}' is not owner/repo — the board's <repo>#N shorthand is not GitHub grammar (.github#2107), and a receiver this engine cannot name is a receiver it must not dispatch to"

        // `dispatch:<event-type>` → `Operation.Dispatch <event-type>`, or a refusal naming what was given.
        //
        // **THE PREFIX IS DERIVED FROM `Operation.wire`, NEVER SPELLED HERE.** `wire (Dispatch payload)`
        // is `"dispatch:" + payload`, so `wire (Dispatch "")` IS the prefix, computed by the one function
        // that defines it. Typing `"dispatch:"` into this file would be a second copy of the wire
        // vocabulary in the CLI layer — the same "forbidden second copy" §12.5 refuses and slice 3
        // declined to write for the ordering rule. If the spelling ever changes, this follows it.
        //
        // **AND IT ROUND-TRIPS RATHER THAN TRUSTING THE SPLIT.** The parsed operation is re-rendered and
        // required to equal the input, so a payload that `wire` would normalise, or a prefix that only
        // looks right, is refused instead of being dispatched under a key the broker will recompute
        // differently. A recomputed-key mismatch at the broker is a refusal too — but one that has already
        // cost a round trip and reads as a fence failure rather than as a typo.
        //
        // THIS BROKER BROKERS `dispatch:*` ONLY, and that is the design's boundary, not a shortcut:
        // `Operation.Merge` is fenced by the lease-free election (§4.2) because its verifier is a queued
        // CI job, and a lease-based lock is verifiable only by a reader running inside the lease.
        let parseDispatch (op: string) : Result<Operation.Op, string> =
            let prefix = Operation.wire (Operation.Dispatch "")

            if op.StartsWith(prefix, StringComparison.Ordinal) then
                let parsed = Operation.Dispatch(op.Substring prefix.Length)

                if Operation.wire parsed = op then
                    Ok parsed
                else
                    Result.Error
                        $"operation '%s{op}' does not survive a round trip through the engine's own wire spelling — the broker recomputes the key from this string and would disagree"
            else
                Result.Error
                    $"operation '%s{op}' is not a dispatch operation: this verb fences `%s{prefix}<event-type>` and nothing else, because a lease-based lock is verifiable only by a reader running inside the lease and every other operation's verifier is a queued CI job (design §4.2)"

    // `op-lock acquire <item> <generation> <receiver> <op>` — TAKE THE DISPATCH GRANT, AND PRINT THE
    // AUTHORIZATION TUPLE THE BROKER DEMANDS.
    //
    // **THIS IS THE PRODUCTION CALLER `.github#2312` LANDED WITHOUT.** `Client.OpLock.acquire` and
    // `Options.opLockRef` landed correct and complete in `d1632c4e` and were reachable from nothing but
    // their own unit tests, so no `fsgg:claim` marker could ever appear on an op-lock issue, so
    // `fsgg-dispatch-broker.yml`'s `grant` input had no non-empty value any caller could supply and its
    // step-5 refusal — *"no live grant holds this receiver's operation lock"* — was unreachable by
    // construction rather than by policy. A lock nobody can take is not a fence.
    //
    // **IT PRINTS THE WHOLE TUPLE, NOT JUST THE GRANT, AND THAT IS A CORRECTNESS DECISION.** The broker
    // recomputes `opkey` from `(item, generation, receiver, op)` and refuses a mismatch. The opkey is a
    // SHA-256 no operator can compute by hand, so a verb that emitted only the grant would produce
    // something structurally unusable — the caller would still have to derive the key some second way, and
    // a second way to compute a key is how the two answers come to disagree. Emitting both from one
    // `Operation.compose` call is what makes the pair guaranteed consistent.
    //
    // **CHEAP QUESTIONS FIRST, NETWORK LAST**, which is the broker's own ordering comment and `acquire`'s:
    // a malformed request is a guaranteed refusal and must not cost a round trip, and — the half that
    // matters more — it must not cost a LOCK. Composing the key after taking the grant would leave a live
    // marker on a receiver for a dispatch that was never going to be authorized, stalling that receiver
    // for the whole ten-minute lease on a typo.
    let opLockAcquire (ctx: Context) (opts: Options) : int =
        match opts.Args with
        | [ item; generation; receiver; op ] ->
            match worker opts with
            | Error c -> c
            | Ok w ->

            match OpLock.parseDispatch op with
            | Result.Error msg ->
                eprint $"fsgg-coord-engine: op-lock acquire: %s{msg}"
                ExitError
            | Ok parsedOp ->

            // PURE, AND BEFORE THE WRITE. `Operation.compose` is slice 1's key (`.github#2311`), CALLED
            // rather than re-derived — it is the same function whose domain the broker transcribed, so the
            // key printed below is the key the broker recomputes, by construction rather than by agreement.
            // Its refusals ACCUMULATE, so a caller who fixes one component does not resubmit to find a
            // second.
            match Operation.compose item generation receiver parsedOp with
            | Result.Error refusals ->
                let why = refusals |> List.map Operation.describe |> String.concat "; "
                eprint $"fsgg-coord-engine: op-lock acquire: %s{why}"
                ExitError
            | Ok(Operation.OpKey opkey) ->

            match OpLock.splitReceiver receiver with
            | Result.Error msg ->
                eprint $"fsgg-coord-engine: op-lock acquire: %s{msg}"
                ExitError
            | Ok(receiverOwner, receiverRepo) ->

            match
                OpLock.acquire
                    ctx.Transport
                    (WorkerId w.Id)
                    (selfOf w)
                    (sessionOf w)
                    (OpLock.roster ())
                    receiverOwner
                    receiverRepo
            with
            | Result.Error refusal ->
                eprint $"fsgg-coord-engine: op-lock acquire refused: %s{OpLock.describe refusal}"

                // A CONTENDED LOCK IS NOT A MISCONFIGURED ONE, and the exit codes say so because the
                // remedies are opposite. `HeldByAnother` is the fence WORKING — another executor is
                // dispatching against this receiver right now — and the caller should back off and retry,
                // which is exactly what `ExitContended` documents. Every other arm is a fact somebody must
                // change before a retry can differ, so retrying on them is a loop.
                match refusal with
                | OpLock.HeldByAnother _ -> ExitContended
                | _ -> ExitError
            | Ok held ->
                // THE GRANT IS THE COMMENT ID, AND NOTHING ELSE IS. Nobody can mint one locally, nobody can
                // choose its value, and nobody can forge its ordering (design §3.2) — which is the whole
                // reason the broker's step 5 is the one check a requester cannot satisfy by typing.
                let grant = string held.MarkerId

                match opts.Render with
                | Options.Json ->
                    printfn
                        "%s"
                        (JsonSerializer.Serialize
                            {| item = item
                               generation = generation
                               receiver = receiver
                               op = Operation.wire parsedOp
                               opkey = opkey
                               grant = grant
                               worker = w.Id
                               leaseMinutes = OpLock.LeaseMinutes |})
                | Options.Text ->
                    printfn "grant=%s" grant
                    printfn "opkey=%s" opkey
                    printfn "item=%s" item
                    printfn "generation=%s" generation
                    printfn "receiver=%s" receiver
                    printfn "op=%s" (Operation.wire parsedOp)

                    eprint
                        $"fsgg-coord-engine: %s{w.Id} holds %s{receiver}'s operation lock for %d{OpLock.LeaseMinutes} minutes. Dispatch now, then `op-lock release %s{receiver}` — a grant held across an item's lifetime serialises the fleet on this receiver."

                ExitGreen
        | args ->
            eprint
                $"fsgg-coord-engine: op-lock acquire needs exactly four arguments — <item> <generation> <receiver> <op> — and got %d{List.length args}. They are `Operation.compose`'s own components, in its own order: item as owner/repo#N, generation as the winning claim marker's comment id, receiver as owner/repo, op as dispatch:<event-type>."

            ExitError

    // `op-lock release <receiver>` — DROP THE GRANT, after the dispatch it fenced.
    //
    // The other half of design §4.1's "taken immediately before a dispatch and released after it, never
    // held across an item's lifetime". It refuses unless we are the live winner, through the same
    // `Writes.verifyHeld` door every other release in this engine goes through, so a lock nobody holds
    // cannot be dropped by naming it and a twin's grant cannot be dropped at all.
    //
    // AN UNRELEASED GRANT IS NOT AN OUTAGE, and saying so is what keeps this verb honest about its own
    // importance: the lease is ten minutes, and `claim`'s stale collection takes the dead marker on the
    // next acquire (ADR-0041). The cost of skipping it is one receiver serialised for up to ten minutes,
    // not a wedged fleet — which is exactly why the lease is minutes rather than the claim's 120.
    let opLockRelease (ctx: Context) (opts: Options) : int =
        match oneArg opts "op-lock release: a receiver, spelled owner/repo", worker opts with
        | Error c, _
        | _, Error c -> c
        | Ok arg, Ok w ->

        match OpLock.splitReceiver arg with
        | Result.Error msg ->
            eprint $"fsgg-coord-engine: op-lock release: %s{msg}"
            ExitError
        | Ok(receiverOwner, receiverRepo) ->

        match
            OpLock.held ctx.Transport (WorkerId w.Id) (selfOf w) (sessionOf w) (OpLock.roster ()) receiverOwner receiverRepo
        with
        | Result.Error refusal ->
            eprint $"fsgg-coord-engine: op-lock release refused: %s{OpLock.describe refusal}"
            ExitError
        | Ok held ->
            let grant = string held.MarkerId

            match OpLock.release ctx.Transport held with
            | Result.Error e -> fail e
            | Ok() ->
                match opts.Render with
                | Options.Json ->
                    printfn
                        "%s"
                        (JsonSerializer.Serialize
                            {| receiver = arg
                               grant = grant
                               worker = w.Id
                               released = true |})
                | Options.Text -> printfn "released %s grant=%s" arg grant

                ExitGreen

    /// Build the context — the transport, the board coordinates, the token check. `Error` is a printed
    /// message and an exit code (a missing token is a refusal, never an empty board).
    let context () : Result<Context * IDisposable, int> =
        let token =
            match env "GITHUB_TOKEN" (env "GH_TOKEN" "") with
            | "" -> None
            | t -> Some t

        match token with
        | None ->
            eprint
                "fsgg-coord-engine: this command needs a GitHub token ($GITHUB_TOKEN or $GH_TOKEN). An unauthenticated read returns an empty organization, and an empty board is exactly the answer this engine refuses to invent."

            Result.Error ExitError
        | Some token ->
            let transport = new Transport.HttpTransport(Transport.apiBaseFromEnv (), token)

            // The board owner LABEL (subject text, cache key, board JSON). The queries pick org/user/viewer
            // from `OwnerKind.fromEnv` in the GitHub layer; this is only the human-facing name. `user` with no
            // explicit `FSGG_COORD_OWNER` is viewer-scoped (#1349) — no login in config — so it is labelled
            // `@me` rather than mislabelled as the org default.
            let owner =
                match env "FSGG_COORD_OWNER" "" with
                | "" when (env "FSGG_COORD_OWNER_TYPE" "").Trim().ToLowerInvariant() = "user" -> "@me"
                | "" -> "FS-GG"
                | v -> v

            Ok(
                { Transport = transport
                  Owner = owner
                  Title = env "FSGG_COORD_PROJECT" "Coordination"
                  DefaultRepo = None
                  ChoreLocks = parseChoreLocks (env "FSGG_COORD_CHORE_LOCKS" "") },
                transport :> IDisposable
            )

    /// `followup audit` is intentionally a read-only reconciliation PREVIEW. A queue's mtime is a
    /// candidate selector, not evidence that its worker died: each queued issue is re-read from GitHub,
    /// then its marker scan is required to be complete before we say it has no live claim.
    // The application fixture supplies the same typed context every other Client handler receives. The
    // override is scoped and restored even when its assertion throws; production never installs one.
    let mutable private followupAuditContextOverride: (Context * IDisposable) option = None

    let withFollowupAuditContextForTest (ctx: Context) (f: unit -> 'a) : 'a =
        let prior = followupAuditContextOverride
        followupAuditContextOverride <- Some(ctx, { new IDisposable with member _.Dispose() = () })

        try f ()
        finally followupAuditContextOverride <- prior

    let followupAudit (opts: Options) : int =
        let supplied =
            match followupAuditContextOverride with
            | Some value -> Ok value
            | None -> context ()

        match supplied with
        | Error code -> code
        | Ok(ctx, disposable) ->
            use _ = disposable
            let local = Followups.audit DateTimeOffset.UtcNow
            let mutable failed = not (List.isEmpty local.Unreadable)

            for (worker, why) in local.Unreadable do
                eprint $"UNREADABLE-QUEUE: worker %s{worker}: %s{why}"

            let repos =
                (local.Stale @ local.Fresh)
                |> List.collect (fun q -> q.Refs)
                |> List.map (fun r -> r.Owner, r.Repo)
                |> Set.ofList

            // `openIssues` is a convenient per-repo candidate read, but absence from it is not a state:
            // it can also be an off-board row, a PR, or a visibility mismatch. The fresh board scan owns
            // the issue-state authority, and anything it cannot name remains UNKNOWN.
            let boardRows =
                Board.bootstrapCached ctx.Transport ctx.Owner ctx.Title
                |> Result.bind (fun board -> Scan.board ctx.Transport Cache.Reconciling ctx.Owner ctx.Title board.Number)

            // AC1 is about the QUEUE OWNER, not merely the owed issue. Mirror `who`'s A ∪ B sweep: board
            // In-progress rows (A) union every open issue in every board repository (B). B is what finds
            // an off-board claim after a failed status flip; every list and marker read must complete.
            let liveClaims =
                boardRows
                |> Result.bind (fun rows ->
                    let boardRepos =
                        rows
                        |> List.filter (fun row -> not row.IsPullRequest)
                        |> List.map (fun row -> row.Ref.Owner, row.Ref.Repo)
                        |> Set.ofList

                    let allRepos = Set.union boardRepos repos

                    let candidates =
                        allRepos
                        |> Seq.fold (fun state (owner, repo) ->
                            state
                            |> Result.bind (fun refs ->
                                Reads.openIssues ctx.Transport owner repo
                                |> Result.map (fun issues ->
                                    issues
                                    |> List.map (fun issue -> { Owner = owner; Repo = repo; Number = issue.Number })
                                    |> List.append refs))) (Ok [])
                        |> Result.map (fun openRefs ->
                            let inProgress =
                                rows
                                |> List.filter (fun row -> not row.IsPullRequest && row.Status = BoardStatus.InProgress)
                                |> List.map (fun row -> row.Ref)

                            Set.union (Set.ofList openRefs) (Set.ofList inProgress) |> Set.toList)

                    candidates
                    |> Result.bind (List.fold (fun state (row: Ref) ->
                        state
                        |> Result.bind (fun claims ->
                            Reads.markerScan ctx.Transport row.Owner row.Repo row.Number
                            |> Result.bind (Reads.requireCompleteMarkerScan row.Short)
                            |> Result.map (fun markers ->
                                match Reads.winner opts.LeaseMinutes markers with
                                | Some marker -> (marker.Worker, row) :: claims
                                | None -> claims))) (Ok [])))

            let mutable closedByWorker: Map<string, Ref list> = Map.empty

            let reportQueue (label: string) (queue: Followups.AuditedQueue) =
                let ownerIsLive =
                    match liveClaims with
                    | Error e ->
                        failed <- true
                        eprint $"UNKNOWN: worker %s{queue.Worker}: complete fleet claim scan failed: %s{Errors.explain e}. Queue retained."
                        Error ()
                    | Ok claims ->
                        Ok(claims |> List.tryFind (fun (worker, _) -> worker.Value = queue.Worker))

                let abandonedOwner =
                    match label, ownerIsLive with
                    | "ABANDONED", Ok None -> true
                    | _ -> false

                for (owed: Ref) in queue.Refs do
                    match Reads.issueState ctx.Transport owed.Owner owed.Repo owed.Number with
                    | Error e ->
                        failed <- true
                        eprint $"UNKNOWN: worker %s{queue.Worker}, %s{owed.Short}: could not read authoritative issue state: %s{Errors.explain e}. Queue retained."
                    | Ok IssueState.Closed ->
                        if abandonedOwner then
                            closedByWorker <-
                                closedByWorker
                                |> Map.change queue.Worker (fun prior -> Some(owed :: Option.defaultValue [] prior))
                        eprint $"CLOSED: worker %s{queue.Worker}, %s{owed.Short}; eligible for reconciliation, but queue retained (preview)."
                    | Ok IssueState.Open ->
                            match
                                Reads.markerScan ctx.Transport owed.Owner owed.Repo owed.Number
                                |> Result.bind (Reads.requireCompleteMarkerScan owed.Short)
                            with
                            | Error e ->
                                failed <- true
                                eprint $"UNKNOWN: worker %s{queue.Worker}, %s{owed.Short}: claim scan incomplete: %s{Errors.explain e}. Queue retained."
                            | Ok markers ->
                                match Reads.winner opts.LeaseMinutes markers with
                                | Some marker ->
                                    if abandonedOwner then
                                        closedByWorker <-
                                            closedByWorker
                                            |> Map.change queue.Worker (fun prior -> Some(owed :: Option.defaultValue [] prior))
                                    eprint $"LIVE-CLAIM: worker %s{queue.Worker}, %s{owed.Short} is open and claimed by %s{marker.Worker.Value}; queue retained."
                                | None ->
                                    match ownerIsLive with
                                    | Ok(Some(_, held)) ->
                                        eprint $"ACTIVE-WORKER: worker %s{queue.Worker} holds %s{held.Short}; %s{owed.Short} is open and unclaimed. Queue retained."
                                    | Ok None ->
                                        if abandonedOwner then
                                            closedByWorker <-
                                                closedByWorker
                                                |> Map.change queue.Worker (fun prior -> Some(owed :: Option.defaultValue [] prior))
                                        eprint $"%s{label}: worker %s{queue.Worker} holds no live claim; %s{owed.Short} is open and unclaimed. Queue retained pending durable disposition."
                                    | Error () ->
                                        eprint $"UNKNOWN: worker %s{queue.Worker}, %s{owed.Short}: owner liveness is unreadable. Queue retained."

            for queue in local.Stale do reportQueue "ABANDONED" queue
            for queue in local.Fresh do reportQueue "ACTIVE" queue

            // The apply phase is deliberately after ALL reads: an unknown anywhere keeps every queue
            // intact. For each abandoned ref (open is re-surfaced, closed is cleared), comment first; only a fully acknowledged batch may rewrite its
            // worker's queue. A failed comment therefore leaves the original promise recoverable.
            if opts.Apply && not failed then
                for KeyValue(workerId, refs) in closedByWorker do
                    match Identity.resolve (Some workerId) with
                    | Error why ->
                        failed <- true
                        eprint $"UNKNOWN: cannot resolve queued worker %s{workerId}: %s{why}. Queue retained."
                    | Ok worker ->
                        let mutable durable = true
                        for owed in refs do
                            match Writes.followupDisposition ctx.Transport owed (WorkerId worker.Id) "reconciled from an abandoned worker queue: this issue has been re-surfaced for the board; the durable queue promise is now cleared." with
                            | Ok () -> ()
                            | Error e ->
                                durable <- false
                                failed <- true
                                eprint $"UNKNOWN: could not record disposition for %s{owed.Short}: %s{Errors.explain e}. Queue retained."

                        if durable then
                            match Followups.remove worker (Set.ofList refs) with
                            | Ok removed -> eprint $"RECONCILED: worker %s{worker.Id}, removed %d{removed} re-surfaced follow-up(s)."
                            | Error why ->
                                failed <- true
                                eprint $"UNKNOWN: dispositions landed but queue %s{worker.Id} could not be rewritten: %s{why}. Queue retained."

            if failed then ExitRed else ExitGreen

    /// Run an IO command. Every one goes through here so the token check, the transport lifetime, and the
    /// defect boundary are in one place.
    // ---- the plumbing commands (#418 board/item cache, case 10) ---------------------------------------
    //
    // These expose the resolver cache the corpus counts: `bootstrap` pays the two GraphQL points once and
    // day-caches the field/option id map; `board`/`field-id`/`option-id` read it back for ZERO; `item-id`
    // resolves an issue's board item id in ONE call and then keeps it forever. They are board-global — no
    // #480 repo scoping — because the board is one board and its ids are the same from any checkout.

    let bootstrapCmd (ctx: Context) (opts: Options) : int = Handlers.bootstrapCmd ctx opts

    let boardCmd (ctx: Context) : int = Handlers.boardCmd ctx

    let fieldId (ctx: Context) (opts: Options) : int = Handlers.fieldId ctx opts

    let optionId (ctx: Context) (opts: Options) : int = Handlers.optionId ctx opts

    let itemIdCmd (ctx: Context) (opts: Options) : int = Handlers.itemIdCmd ctx opts

    /// `body-edits <ref>` — "has this issue/PR body changed since X" (`.github#2477`).
    ///
    /// `.github#2456`'s independent-review contract names GraphQL's `userContentEdits` connection as the
    /// authoritative source for this question — REST's timeline carries no body-edit event at all, only
    /// `renamed` for titles — and warns a critic off a hand-built `gh api graphql` call, which
    /// `graphql-monopoly` refuses as an unmetered principal on the shared budget. This is the sanctioned,
    /// metered way to ask the contract's own question: one `Reads.contentEditProvenance` call through the
    /// existing client path, so it is budget-attributed exactly like every other read.
    ///
    /// FAILS CLOSED ON BOTH PROJECTIONS. `Reads.contentEditProvenance` never degrades a read it could not
    /// complete into an empty connection; this handler carries that all the way to the exit code and both
    /// renderers via `failWith opts.Render`, so a rate-limited or unauthorized read prints as a FAILED
    /// READ — non-zero exit, an error on stderr (`--text`) or in the failure document (`--json`) — and
    /// NEVER as "0 edits". Silently reporting a failed read as a negative is exactly the false negative
    /// `.github#2456` was written to prevent; this command exists so a critic never has to choose between
    /// asking the authoritative question and obeying `graphql-monopoly`.
    let bodyEditsCmd (ctx: Context) (opts: Options) : int = Handlers.bodyEditsCmd ctx opts
    let private AddDefaultStatus = "Backlog"

    /// `add <ref>` — put an issue on the board (#861).
    ///
    /// The verb `check-graphql-monopoly` (#586) names as the compliant alternative to `gh project
    /// item-add`, and the one the port dropped — so the rule spent its life with no path that obeyed it.
    /// The raw `gh` call spends the shared 5,000 pt/hr fleet budget with nothing to meter, cache or refuse
    /// it; this goes through the one transport, which does all three.
    ///
    /// Idempotent by #421's rule and not by a `try`: see `Board.addItem`. Re-running it is free of a write.
    ///
    /// ---- THE STATUS DEFAULT (.github#1823) --------------------------------------------------------
    ///
    /// `add` used to leave `Status` UNSET, and a row with no `Status` is invisible to every scheduler —
    /// `Schedulability` says so in as many words: *"no Status on the board: invisible to every scheduler,
    /// and nobody set it."* Fourteen rows were filed that way on 2026-07-28, in three batches, and EVERY
    /// instance was found by accident by a driver reading `batch` output for an unrelated reason. Nothing
    /// reported any of them. Each was filed in good faith by a worker discharging a real item and
    /// following the documented flow — file the finding, `add` it to the board. The step that made the row
    /// schedulable was undocumented at the point of use and silently optional, which is #1644's subject
    /// arriving one layer down: no scheduler reads prose, and an unscheduled row is prose.
    ///
    /// **THE DEFAULT ONLY EVER FILLS AN EMPTY COLUMN, AND THAT IS THE WHOLE RISK OF THIS CHANGE.** `add`
    /// is idempotent (#861) — a close-out pass, a retry, or two workers racing the same follow-up all
    /// reach it — so a naive "set Status on add" would walk a live `In progress` row back to `Backlog` and
    /// DESTROY information rather than add it. That is the one direction this can be wrong, and it is why
    /// the already-on-board arm READS the column before it decides, and why an unreadable column is left
    /// alone rather than defaulted (#266: a column we could not read is not a column we may call empty).
    ///
    /// **THERE IS ONE ARM, NOT TWO, AND THAT IS DELIBERATE.** A freshly-added row looks like it needs no
    /// read — but `AddedToBoard` does not mean "the item is new". `Board.addItem`'s own docstring records
    /// that `addProjectV2ItemById` is idempotent SERVER-side and returns the EXISTING item's id for an
    /// issue already on the board, so the case really means "the lookup did not find it" — and that
    /// lookup is `projectItems(first: 20)`, unpaginated. A successful read can therefore miss a row that
    /// IS on the board carrying a live column, and the skipped read would have overwritten it. One
    /// GraphQL point on a once-per-filing verb (#418) buys that away.
    ///
    /// **AND IT SAYS SO.** Silence is how the defect worked: a filer who is told nothing assumes the row
    /// is schedulable. The stderr note names the column and states that the row is NOT startable yet.
    ///
    /// **AN EXPLICIT `--status` IS VALIDATED BEFORE THE ADD**, against the options `bootstrapCached` has
    /// already resolved — zero GraphQL, and no mutation spent on a value that cannot land. The Status
    /// write is non-fatal (the row is boarded, so a red would send a filer back to re-run `add`), which
    /// is right for a default nobody asked for and wrong for an instruction: a bad `--status` would
    /// otherwise board the row, exit 0, and leave it with no column at all — this change's own flag
    /// producing the very row it exists to prevent.
    ///
    /// COST, over `Board.addItem`'s own (3 GraphQL on a real add, 1 when it is already there): **+3
    /// measured**, on every path — the `itemStatus` read, the item-id lookup `boardWrite` re-resolves for
    /// itself, and the mutation. (`boardWrite` takes owner/repo/number, not the id `addItem` just
    /// returned, so that lookup is redundant and is charged anyway; retiring it is a change to
    /// `Board.boardWrite`'s signature and belongs to whoever owns that file.) Two of the three are not
    /// spent when the column is already set — the read answers, and nothing is written.
    /// Compatibility entry for the BoardOps `add` handler.
    ///
    /// The family assembly owns the implementation and its `Backlog` default; this forwarding seam remains
    /// public only for callers that already drive the handler over a recording transport. Program dispatch
    /// composes `Handlers.handlers` directly and does not register this seam as a second owner.
    /// Observable output, exit codes, and board writes therefore come from the family implementation.
    let addCmd (ctx: Context) (opts: Options) : int = Handlers.addCmd ctx opts

    // ---- flush: the replay every deferral already promised (#862) --------------------------------------
    //
    // `Board.boardWrite` QUEUES a field write that meets an exhausted budget and returns `Deferred`; the
    // verb then prints "QUEUED — flush replays it" and exits `EX_RATE`. That message was true of the
    // LIBRARY and false of this CLI. The queue (`Cache.defer`/`pending`/`dropPending`) and the replay
    // (`Board.flush`) were both here, complete and tested — nothing exposed the verb, so `flush` was an
    // unknown command and the promise could not be kept by anyone.
    //
    // THAT IS THE WORST DIRECTION FOR IT TO BE WRONG, and it is why this is a bug and not a missing
    // feature. `EX_RATE` means back off and retry; "QUEUED, flush replays it" means the retry is already
    // arranged. A worker who reads both correctly concludes there is NOTHING TO DO — and the write is
    // gone. The board is a projection of issue state, so a dropped `set-field`/`done --flip` leaves the
    // projection lying, and the cost lands on a later reconcile pass rather than on the write (#510).
    //
    // The rendering rule (#266): "3 queued" and "3 replayed" are DIFFERENT SENTENCES. A dry run must never
    // be readable as a flush, which is the whole reason `--dry-run` says NOT replayed in those words.

    let flushCmd (ctx: Context) (opts: Options) : int = Handlers.flushCmd ctx opts

    // ---- lint: the board-health gate (#496) -----------------------------------------------------------
    //
    // The rule whose absence let `lint` report `0 error(s)` over a DEAD queue. A Ready/Backlog item that
    // declares no schedulable touch-set is refused by `batch`/`take` — correctly, an undeclared touch-set
    // cannot be proven disjoint — and then sits on the board looking like work no worker can ever pick up.
    // NO-TOUCH-SET names the omission; BAD-TOUCH-SET names the item that DECLARED a touch-set the scheduler
    // cannot use (every token unmatchable). Both are the same condition — "no worker can ever pick this up"
    // — so both are errors. `Paths: none` is the deliberate out (an epic, a decision item), and it is the
    // whole point: "deliberately undeclared" must be sayable, or no gate can tell it from "somebody forgot".
    // A RECONCILER read: always fresh, never the cache.
    //
    // The epic ROLL-UP-graph rules (EPIC-*, DONE-STATUS-OPEN-ISSUE, the PR-probing EPIC-UNLINKED-CHILD) SHIP —
    // they read the sub-issue graph and parse body child-refs, which the touch-set rules do not.
    //
    // They divide into two kinds, and the split is the thing to hold onto. The `error` rules name an epic that
    // CANNOT roll up (no children, truncated graph, an unlinked declared child, acceptance delegated to nobody
    // or stated nowhere) — each is a defect with a mechanical remedy. `EPIC-ROLLUP-READY` is a `note` naming
    // the opposite: every mechanical precondition holds. It is still not a verdict that the epic is done,
    // because that verdict is `Discharge` (#614) and no read can reach it.
    // Compatibility seams retained for existing callers while the lint command delegates its
    // deterministic verdicts to the extracted application service.
    type EpicFinding = LintApplication.EpicFinding

    let epicVerdict = LintApplication.epicVerdict

    let lint (ctx: Context) (opts: Options) : int =
        match Board.bootstrapCached ctx.Transport ctx.Owner ctx.Title with
        | Error e -> fail e
        | Ok board ->
            match Scan.board ctx.Transport Cache.Reconciling ctx.Owner ctx.Title board.Number with
            | Error e -> fail e
            | Ok rows ->
                // A PR is not an item of work (#641), and `--repo` scopes the pass.
                //
                // The local `Option.map resolveRepo` that stood here is gone (#979). Resolution is the
                // PARSER's job since #962/#978 — `Options.parse` has already resolved `opts.Repo`, and
                // `resolveRepo` is idempotent, so the second call was a no-op. It was worth deleting
                // anyway: it was the last thing in the tree implying a verb still resolves for itself,
                // which is the habit that made the filter five copies in the first place.
                let scoped = rows |> List.filter (fun r -> not r.IsPullRequest) |> Scan.scope opts.Repo

                scoped.Advisory |> Option.iter eprint

                let items = scoped.Rows
                let blockersByRef = Scan.blockerGraph rows |> Map.ofList

                // "A WORKER COULD BE HANDED THIS ROW" — the population two rules share, spelled once.
                //
                // `NO-TOUCH-SET`/`BAD-TOUCH-SET` ask whether such a row is SCHEDULABLE; `CLASS-UNSET` asks
                // whether it is TRIAGED (.github#1588). Same set, two questions, and it also decides
                // `bodyNeeded` below — so a copy that drifted would leave one rule reading a body the pass
                // never fetched.
                let isSchedulableCandidate (r: Scan.Row) =
                    r.State = IssueState.Open && (r.Status = BoardStatus.Ready || r.Status = BoardStatus.Backlog)

                let mk code severity (r: Scan.Row) detail =
                    { Code = code
                      Severity = severity
                      Id = $"%s{r.Ref.Owner}/%s{r.Ref.Repo}#%d{r.Ref.Number}"
                      Short = r.Ref.Short
                      Status = statusWireName r.Status
                      Url = $"https://github.com/%s{r.Ref.Owner}/%s{r.Ref.Repo}/issues/%d{r.Ref.Number}"
                      Detail = detail }

                // The schedulability rules (#496): a Ready/Backlog OPEN item no worker can pick up.
                let touchSetFindings (r: Scan.Row) (body: string) : LintFinding list =
                    if isSchedulableCandidate r then
                        match TouchSet.parse body with
                        | Undeclared ->
                            [ mk
                                  "NO-TOUCH-SET"
                                  "error"
                                  r
                                  $"%s{statusWireName r.Status} but declares no `Paths:` — `batch`/`take` cannot schedule it, so no worker can ever pick it up. Declare a touch-set, or `Paths: none` if it genuinely has none (an epic, a decision item)." ]
                        // ASK, do not decide (#945). This rule used to reach the verdict itself — its own
                        // `List.exists` for the threshold, its own `List.choose` for the offending
                        // tokens, its own `List.forall` for the every/some split. It AGREED with
                        // `TouchSet.usability`, because #646 had taught it the partial case by hand. That
                        // is exactly what `Schedulability` and `Lanes` looked like before they drifted
                        // into opposite verdicts on the same item (#864): a copy that agrees is still a
                        // copy, and `unmatchable` handing back a LIST left every caller free to pick its
                        // own threshold.
                        //
                        // Only the two SENTENCES are lint's own — the rule, the threshold and the split
                        // are the core's. That is the whole distinction this change is about: rendering a
                        // verdict differently is not the same as computing a different verdict.
                        // Name ONLY the offending subset — the matchable tokens are fine and pointing at
                        // them would send the author to spell out a path that already works. Which subset
                        // that is, and whether there is one at all, is `TouchSet.usability`'s answer.
                        | Declared _ as ts ->
                            match TouchSet.usability ts |> badTouchSetDetail (statusWireName r.Status) with
                            | None -> []
                            | Some detail -> [ mk "BAD-TOUCH-SET" "error" r detail ]
                        | _ -> []
                    else
                        []

                // BLOCKED-NO-REASON (#1103 leg 2). A `Blocked` item with an EMPTY `Blocked by` records
                // nothing about WHAT holds it — and `Blocked by` is ref-typed, so it structurally cannot
                // say "blocked on a human". That empty field reads identically whether the park was
                // deliberate (blocked on a person's decision or action) or a filing mistake, which is
                // exactly the collapse #1103 breaks. The sentinel is the deliberate out: `Blocked on:
                // human/decision` or `human/action`. So this reds ONLY when NEITHER is present — an author
                // who had a machine-readable way to say what they meant and used neither. An item with a
                // real `Blocked by` ref, or one carrying the sentinel, is silent here.
                let humanBlockFindings (r: Scan.Row) (body: string) : LintFinding list =
                    blockedNoReasonVerdict r.State r.Status r.BlockedByRaw body
                    |> Option.map (mk "BLOCKED-NO-REASON" "error" r)
                    |> Option.toList

                let humanParkFindings (r: Scan.Row) (body: string) : LintFinding list =
                    Map.tryFind r.Ref blockersByRef
                    |> Option.bind (fun blockers -> humanParkResolvedVerdict r.State r.Status blockers body)
                    |> Option.map (mk "HUMAN-PARK-MACHINE-CLEARED" "note" r)
                    |> Option.toList

                // BLOCKED-BY-INERT (.github#2079). `BLOCKED-NO-REASON` above reds ONLY when the `Blocked
                // by` FIELD is empty — its own comment says an item with a real `Blocked by` ref "is
                // silent here." This is what is not silent: a `Blocked` row whose BODY carries a
                // `Blocked by:` line naming ref(s) the field does not — the `FS.GG.Templates#348` shape,
                // where a park's real edge landed as a body line and the field kept a stale, unrelated
                // (here, fully-resolved) set. A body line is never read by anything that clears a
                // blocker — `Blocked by` is a board FIELD (ADR-0045/`.github#1933`) — so the declaration
                // is INERT, and `reconcile` withholds `BLOCKER-CLEARED` on exactly this same predicate
                // (see below): this finding is what makes that withholding legible rather than silent.
                //
                // Same population as `BLOCKED-NO-REASON` — an open `Blocked` row, whose body this pass
                // already read for the sentinel check — because this shape is dangerous precisely where
                // that one is silent, and reading every row's body for it would spend the budget that
                // dies first (#418) on rows where the divergence cannot mislead `BLOCKER-CLEARED` at all.
                let blockedByInertFindings (r: Scan.Row) (body: string) : LintFinding list =
                    if r.State = IssueState.Open && r.Status = BoardStatus.Blocked then
                        match blockedByBodyDivergence r.Ref.Owner r.Ref.Repo r.BlockedByRaw body with
                        | [] -> []
                        | extra ->
                            let named = String.concat ", " extra

                            [ mk
                                  "BLOCKED-BY-INERT"
                                  "error"
                                  r
                                  $"body declares a `Blocked by:` line naming %s{named}, which the FIELD does not carry. A body line is never read by anything that clears a blocker — `Blocked by` is a board FIELD (ADR-0045/.github#1933) — so this declaration is INERT: `BLOCKER-CLEARED` cannot see it, and `reconcile` withholds the promotion on this same divergence. If %s{named} really blocks this item, write it into the field:  scripts/fsgg-coord set-field %s{r.Ref.Short} 'Blocked by' '<refs>'" ]
                    else
                        []

                // The CLASS axis (.github#1588 AC2/AC3, .github#1651). A `Ready`/`Backlog` OPEN item whose
                // own text does not class it — either because it says nothing about HOW BAD it is (no
                // `Class:` line, no `[decision]` title prefix, no ADR-0045 `Blocked on: human/decision`
                // sentinel), or because it DID write a `Class:` line and the word is not one of the three.
                //
                // THOSE ARE TWO FAULTS AND THEY USED TO RENDER AS ONE. `Class.derive` answers `None` for
                // both, so a row carrying `Class: docs` was reported as recording no `Class:` — a
                // diagnostic naming a fault the row did not have, measured twice in one run on two repos
                // with two different invented words. The verdict is `LintApplication.classVerdict`'s now,
                // for `badTouchSetDetail`'s reason: a rule nothing can call is a rule no test can drive.
                //
                // UNTRIAGED SEVERITY IS EXACTLY AS INVISIBLE AS AN UNTRIAGED STATUS, which is why this is
                // an `error` on `NO-TOUCH-SET`'s terms rather than a note. The board went from 5 non-Done
                // rows to 34 in one burn-down and the driver could not tell a RED `main` from a stale
                // comment in a test file, because `batch`, `ready` and the stopping rule see the same row
                // for both. A human sorted the same titles in seconds; the fact was knowable and nowhere.
                //
                // IT REPORTS; IT NEVER DEFAULTS. The remedy is a body line a human writes, and `reconcile`
                // then projects it onto the board column — the field is never the input. That direction is
                // ADR-0066, and it is what keeps ADR-0045 (which rejected a board field for exactly this
                // kind of fact) intact.
                //
                // AND IT DOES NOT ASK WHETHER THE BOARD HAS A `Class` FIELD. Gating the rule on the field
                // existing would make it no-op against any project without one — convenient, and the third
                // instance of the shape this repo has already been bitten by twice: `landable` greening a
                // required context that never reported (#1575), and #266's rule that a gate which could
                // not read an item must never report it clean. `lint` FAILS CLOSED. The subject of this
                // rule is the ITEM's text, which is present on every board.
                let classFindings (r: Scan.Row) (body: string) : LintFinding list =
                    // `isSchedulableCandidate`, NOT a third spelling of `Open && (Ready || Backlog)`. The
                    // two rules genuinely share this population, and a restated copy drifts in one quiet
                    // direction: narrowing the shared predicate would make `bodyNeeded` false while this
                    // rule kept firing over `body = ""`, reporting rows as untriaged whose bodies nobody
                    // read. That is the #266 shape again — a finding produced by a read that did not happen.
                    if isSchedulableCandidate r then
                        match classVerdict (statusWireName r.Status) r.Title body with
                        | Some(code, detail) -> [ mk code "error" r detail ]
                        | None -> []
                    else
                        []

                // The epic ROLL-UP-graph rules. Only epics pay the sub-issue read; the VERDICT over what
                // it reads is the pure `epicVerdict` (#1050, on #945's `badTouchSetDetail` precedent).
                let epicFindings (r: Scan.Row) (body: string) : Errors.IoResult<LintFinding list> =
                    match Reads.subIssues ctx.Transport r.Ref.Owner r.Ref.Repo r.Ref.Number with
                    | Error e -> Error e
                    | Ok graph ->
                        // The EPIC-UNLINKED-CHILD set is read (and PR-pruned, #346/#266) ONLY when the
                        // graph is whole — a truncated graph makes "declared child X is unlinked" a claim
                        // about a set already known short (#266), which EPIC-CHILDREN-TRUNCATED covers
                        // instead. This read is the caller's; the gate on it is mirrored in `epicVerdict`.
                        let unlinkedResult =
                            if graph.Total = List.length graph.Children then
                                Done.bodyUnlinkedChildren
                                    ctx.Transport
                                    r.Ref.Owner
                                    r.Ref.Repo
                                    body
                                    (graph.Children |> List.map (fun c -> c.Ref))
                            else
                                Ok []

                        match unlinkedResult with
                        | Error e -> Error e
                        | Ok unlinked ->
                            epicVerdict r.State r.Status body graph unlinked
                            |> List.map (fun ef -> mk ef.Code ef.Severity r ef.Detail)
                            |> Ok

                // Per item, in rule order: touch-set rules, then a Done-but-open NOTE, then the epic rules
                // (only epics read their graph/body-child-refs). FAIL CLOSED (#266): an unreadable body or
                // graph fails the whole pass — a gate that could not read an item must not report it clean.
                let rec classify
                    (acc: LintFinding list)
                    (touchSets: LintApplication.ConsolidationRow list)
                    (rows: Scan.Row list)
                    : Errors.IoResult<LintFinding list * LintApplication.ConsolidationRow list> =
                    match rows with
                    | [] -> Ok(acc, touchSets)
                    | r :: rest ->
                        let isEpic =
                            r.Title.IndexOf("[epic]", StringComparison.OrdinalIgnoreCase) >= 0

                        let isTouchSetCandidate = isSchedulableCandidate r

                        // A Blocked item with an empty `Blocked by` needs its body read too — the sentinel
                        // that would make the park deliberate lives there (#1103 leg 2).
                        let isHumanBlockCandidate =
                            r.State = IssueState.Open
                            && r.Status = BoardStatus.Blocked
                            && r.Status = BoardStatus.Blocked

                        let doneOpenNote =
                            if r.Status = BoardStatus.Done && r.State = IssueState.Open then
                                [ mk "DONE-STATUS-OPEN-ISSUE" "note" r "board Status is Done but the issue is still open" ]
                            else
                                []

                        // STATUS-UNSET (.github#1823 AC5) — `CLASS-UNSET`'s sibling, and an `error` on
                        // `NO-TOUCH-SET`'s terms: "no worker can ever pick this up" is exactly what an
                        // unset column means, reached one step earlier than an unusable touch-set. It
                        // costs NO read — the column is already in the scan — so it is not gated on
                        // `bodyNeeded` and cannot be the #266 shape `classFindings` warns about.
                        let statusUnsetFindings =
                            match LintApplication.statusVerdict r.State r.Status with
                            | Some detail -> [ mk "STATUS-UNSET" "error" r detail ]
                            | None -> []

                        // SEVERITY-UNSET (.github#1901). `Unset` is a real vocabulary value so the
                        // absence stays representable in JSON and ranking, but it is not a completed
                        // triage decision. Report every open, non-Done row until a human rates it.
                        let severityUnsetFindings =
                            match LintApplication.severityVerdict r.State r.Status r.Severity with
                            | Some detail -> [ mk "SEVERITY-UNSET" "error" r detail ]
                            | None -> []

                        // One body read serves the touch-set rules, the human-block rule, and the epic
                        // body-child-refs.
                        let bodyNeeded = isTouchSetCandidate || isEpic || isHumanBlockCandidate

                        let bodyResult =
                            if bodyNeeded then
                                Reads.issueBody ctx.Transport r.Ref.Owner r.Ref.Repo r.Ref.Number
                            else
                                Ok ""

                        match bodyResult with
                        | Error e -> Error e
                        | Ok body ->
                            let tsFindings = touchSetFindings r body

                            let hbFindings = humanBlockFindings r body
                            let humanPark = humanParkFindings r body
                            let blockedByInert = blockedByInertFindings r body

                            let clsFindings = classFindings r body

                            let epicResult =
                                if isEpic then epicFindings r body else Ok []

                            match epicResult with
                            | Error e -> Error e
                            | Ok epic ->
                                // The CONSOLIDATION population, collected on the body read the
                                // touch-set rules already paid for (.github#1914). It is
                                // `isSchedulableCandidate`'s set — the same rows `NO-TOUCH-SET` and
                                // `CLASS-UNSET` speak about — because a row nobody can be handed is not
                                // a row anybody would merge into another.
                                //
                                // The `Unreadable` case cannot arise HERE: `bodyResult` above is `fail`
                                // on error, so an unread body aborts the whole pass (#266) rather than
                                // reaching the rule. The rule handles it anyway, and is tested on it,
                                // because it is a pure function with other possible callers and "the
                                // caller is careful" is the assumption fail-open defects are built on.
                                let touchSets =
                                    if isTouchSetCandidate then
                                        { LintApplication.ConsolidationRow.Ref = r.Ref.Short
                                          // The board issue may live in `.github` while its declaration
                                          // reserves a receiver worktree. Consolidation is evidence about
                                          // overlapping files, so it partitions on `Repo Scope`, not the
                                          // repository that happens to host the coordination issue (#1732).
                                          //
                                          // `RepoScope.orFallback` (#2398): a `cross-repo` Repo Scope
                                          // names no repository, so consolidation falls back to the
                                          // issue's own hosting repository rather than grouping on the
                                          // sentinel itself — the same policy `enrich`/`Lanes.partition`
                                          // apply.
                                          LintApplication.ConsolidationRow.Repo =
                                            FS.GG.Coord.RepoScope.orFallback r.Ref.Repo (Options.resolveRepo r.PathRepo)
                                          LintApplication.ConsolidationRow.TouchSet = TouchSet.parse body }
                                        :: touchSets
                                    else
                                        touchSets

                                classify
                                    (acc
                                     @ tsFindings
                                     @ hbFindings
                                     @ humanPark
                                     @ blockedByInert
                                     @ clsFindings
                                     @ statusUnsetFindings
                                     @ severityUnsetFindings
                                     @ doneOpenNote
                                     @ epic)
                                    touchSets
                                    rest

                // The BLOCKER-CYCLE rule (#1090) — the one lint rule that is NOT per-item, and could not be.
                // A `Blocked by` ring passes every per-item blocker check (#343/#476/#602/#620), because
                // every item on it is individually well-formed: a non-empty blocker list, every blocker
                // OPEN, every ref a real issue, correctly never handed out. The defect lives in the GRAPH,
                // which no per-item rule has to look at, and no worker can see — each edge is drawn by a
                // different worker from locally correct information, and the ring is visible only from
                // above. `Blockers.cycles` owns that graph (#1092); `Scan.blockerGraph` builds it from the
                // rows already scanned, with no extra read. Error severity: a ring can NEVER clear on its
                // own — no lease, no merge frees it — so it is not a note a human might adjudicate but a
                // deadlock a human must break.
                let cycleFindings =
                    let byRef = items |> List.map (fun row -> row.Ref, row) |> Map.ofList
                    Scan.blockerGraph items
                    |> blockerCycleVerdicts
                    |> List.choose (fun (ref, detail) ->
                        Map.tryFind ref byRef |> Option.map (fun row -> mk "BLOCKER-CYCLE" "error" row detail))

                match classify [] [] items with
                | Error e -> fail e
                | Ok(perItemFindings, touchSets) ->
                    // CONSOLIDATION-CANDIDATE (.github#1914) — the SECOND lint rule that is not
                    // per-item and could not be, for `BLOCKER-CYCLE`'s reason one axis over. Every row
                    // in a consolidation group is individually well-formed; what is visible only from
                    // above is that two of them may be the SAME piece of work. No worker can see it —
                    // each declared its touch-set from locally correct information — and the board pays
                    // for that three times in a single run (#1626): a finished item that could not merge
                    // because its file was held continuously, an edit rehearsed in a scratch worktree,
                    // a unit test that could not go in the file it belonged in.
                    //
                    // ONE FINDING PER GROUP, anchored on its lowest-numbered member — and that is the
                    // deliberate difference from `BLOCKER-CYCLE`, which emits one per ring member. A
                    // cycle is a PER-ROW defect: every row on the ring is individually stuck and each
                    // must be flagged where a reader will meet it. A consolidation candidate is a
                    // property of the SET, and repeating one proposal once per member would treble the
                    // report without adding a fact.
                    //
                    // NOTE severity, on `EPIC-ROLLUP-READY`'s terms. This reports a state only the
                    // runner can adjudicate — the rule cannot tell "the same operation split in two"
                    // from "two unrelated objectives that happen to edit one file", and it says so in
                    // the finding. Reddening a gate on a question nobody has been asked is how a gate
                    // becomes noise (#698). `--strict` is for the caller who wants to be stopped.
                    let consolidation = LintApplication.consolidationVerdict touchSets

                    let rowByShort = items |> List.map (fun r -> r.Ref.Short, r) |> Map.ofList

                    let anchored (short: string) (build: Scan.Row -> LintFinding) =
                        Map.tryFind short rowByShort |> Option.map build |> Option.toList

                    let consolidationFindings =
                        // UNREADABLE FIRST, and it is an `error`. A board this rule could not read in
                        // full is a NO-VERDICT, not "no clusters" (#266) — the absence of groups proves
                        // nothing when a row was never compared, so the pass must not go green on it.
                        (consolidation.Unreadable
                         |> List.collect (fun (short, reason) ->
                             anchored short (fun row ->
                                 mk
                                     "CONSOLIDATION-UNREADABLE"
                                     "error"
                                     row
                                     (LintApplication.consolidationUnreadableDetail reason))))
                        @ (consolidation.Groups
                           |> List.collect (fun group ->
                               match group.Members with
                               | anchor :: _ ->
                                   anchored anchor (fun row ->
                                       mk
                                           "CONSOLIDATION-CANDIDATE"
                                           "note"
                                           row
                                           (LintApplication.consolidationDetail group))
                               | [] -> []))

                    let findings = perItemFindings @ cycleFindings @ consolidationFindings
                    let summary =
                        findings
                        |> List.map (fun finding -> finding.Severity)
                        |> LintApplication.summarize opts.Strict

                    match opts.Render with
                    | Json -> printfn "%s" (renderLintJson findings)
                    | Text ->
                        for f in findings do
                            printfn "FSGG-LINT %s  %s  %s  — %s" (f.Severity.ToUpperInvariant()) f.Code f.Short f.Detail

                        // `opts.Repo` is the RESOLVED name — the parser did it (#962/#978), so this prints
                        // exactly what the deleted local `resolveRepo` used to produce.
                        let repoSuffix =
                            match opts.Repo with
                            | Some r -> $" for repo '%s{r}'"
                            | None -> ""

                        eprint
                            $"fsgg-coord-engine: %d{summary.Errors} error(s), %d{summary.Notes} note(s)%s{repoSuffix}"

                    // A gate: any error fails it; `--strict` makes a note fatal too.
                    //
                    // NOTES ARE NOT FATAL BY DEFAULT, AND `EPIC-ROLLUP-READY` IS WHY THAT MATTERS NOW. Both note
                    // rules report a state only a human can adjudicate — `DONE-STATUS-OPEN-ISSUE` cannot tell a
                    // premature flip from "merged, left open for the release note", and `EPIC-ROLLUP-READY` cannot
                    // tell a discharged epic from #614's partial fix. Reddening a gate on a question nobody has
                    // been asked yet teaches the lesson #698 names: the gate is noise, merge anyway. `--strict` is
                    // for the caller who wants to be stopped by one.
                    if summary.Fails then
                        ExitError
                    else
                        ExitGreen

    /// `issues <repo> [--label L] [--state S] [--refresh]` — list a repo's issues over REST, ETag-revalidated
    /// (#446/#418). The repo is resolved like every OTHER repo-taking command: an `owner/repo` splits and
    /// passes through, a bare short-id maps through `resolveRepo` to `owner/<repo-name>`. This was the ONE
    /// command that took the bare token verbatim — so `issues game` asked for `repos/FS-GG/game` and 404'd
    /// while `--repo game` worked everywhere else (#446). A 404 there is worse than a typo: `issues` is
    /// advertised as THE way to read issues without spending GraphQL, so the natural recovery is `gh issue
    /// list` — the exact budget the command exists to save. Emits the raw JSON array; the caller jq's it.
    let issues (ctx: Context) (opts: Options) : int = Handlers.issues ctx opts

    /// #2134's receipt-first intake transaction. The receipt is persisted immediately after the only
    /// REST create, so a retry repairs the same issue's projection rather than issuing a second POST.
    /// Execute the live receipt-first intake transaction.  This is public so the transaction can be
    /// driven over a recording transport: the tests must count the issue-create POST, not infer it
    /// from a later board result.
    let intakeCmd (ctx: Context) (opts: Options) : int = Handlers.intakeCmd ctx opts

    /// Read the full evidence pair from GitHub.  The issue body is the source-bound subject and comments
    /// are the append-only receipt ledger: a failure in either direction is not a missing decision.
    /// `show` renders only the current receipt (`kind = "current"`, never a history), but validates the
    /// complete append-only ledger before selecting it. This is the same fail-closed read used at the
    /// scheduling and mutation boundaries.
    let private deliveryRouteFact (ctx: Context) (target: Ref) =
        match readDeliveryRouteComments ctx target with
        | Error error -> Error error
        | Ok comments ->
            match routeEvidence target.Canonical comments with
            | DeliveryRoute.Current valid ->
                let structuredCurrent =
                    match structuredRouteLedger target.Canonical comments with
                    | Ok(Some(_, current)) -> Some current
                    | _ -> None
                Ok(valid, structuredCurrent)
            | DeliveryRoute.Stale errors -> Error(Errors.Malformed(target.Canonical, String.concat "; " errors))
            | DeliveryRoute.Unreadable errors -> Error(Errors.Malformed(target.Canonical, String.concat "; " errors))

    /// Not `private`: the command-boundary test (`DeliveryRouteCliTests`) drives `record`/`show` directly
    /// against a scripted transport, the same way `Client.claim` already is by `ForceStealTests`.
    let deliveryRouteCmd (ctx: Context) (opts: Options) : int =
        let target arg = parseRef ctx arg
        match opts.Args with
        | [ "show"; arg ] ->
            match target arg with
            | Error message -> eprint $"fsgg-coord-engine: delivery-route: %s{message}"; ExitError
            | Ok ref ->
                match deliveryRouteFact ctx ref with
                | Error error -> fail error
                | Ok(receipt, structuredCurrent) ->
                    let route =
                        match receipt.Route with
                        | Some DeliveryRoute.Lightweight -> "lightweight"
                        | Some DeliveryRoute.SddRequired -> "sdd-required"
                        | None -> ""
                    // #2298: the SDD package's on-disk readiness is reported, not enforced here — the
                    // claimed worker is the actor who completes it, and this command must stay readable
                    // (and postable, below) before that worker exists.
                    let sddNotes = sddEvidenceErrors receipt
                    let revision = structuredCurrent |> Option.map _.Revision
                    let digest = structuredCurrent |> Option.map _.Digest |> Option.defaultValue receipt.SubjectRevision
                    printfn "%s" (JsonSerializer.Serialize {| schema = "fsgg.coord.delivery-route-result/v2"; kind = "current"; subject = receipt.Subject; decisionRevision = revision; revision = revision; digest = digest; route = route; reasonCodes = receipt.ReasonCodes; sddPackageReady = List.isEmpty sddNotes; sddPackageNotes = sddNotes |})
                    ExitGreen
        | [ "record"; arg; path ] ->
            match target arg with
            | Error message -> eprint $"fsgg-coord-engine: delivery-route: %s{message}"; ExitError
            | Ok ref ->
                try
                    let raw = File.ReadAllText path
                    match completeDeliveryRouteComments ctx ref,
                          DeliveryRouteApplication.decodeStructured raw with
                    | Error error, _ -> fail error
                    | _, Error reason -> eprint $"fsgg-coord-engine: delivery-route: only structured v2 records may be written: %s{reason}"; ExitError
                    | Ok comments, Ok candidate ->
                        let existing =
                            match structuredRouteLedger ref.Canonical comments with
                            | Ok(Some(records, _)) -> Ok records
                            | Ok None -> Ok []
                            | Error errors -> Error errors
                        match existing |> Result.bind (fun records -> StructuredDecision.validateRouteLedger ref.Canonical (records @ [ candidate ])) with
                        | Error errors ->
                            let detail = String.concat "; " errors
                            eprint $"fsgg-coord-engine: delivery-route: %s{detail}"
                            ExitError
                        | Ok validRecord ->
                            let valid = StructuredDecision.toEffectiveRoute validRecord
                            // #2298: an `sdd-required` decision records on the strength of the AGENT'S
                            // explicit, structurally valid receipt alone.  The coordinator authoring it
                            // holds no worktree and cannot produce `work/<id>/spec.md` or the readiness
                            // analysis (SB-002 of work/2137-delivery-route/spec.md: fsgg-coord does not
                            // author SDD specifications). Requiring that package to exist here made
                            // `sdd-required` permanently unrecordable for any item that did not already
                            // carry one — the deadlock this fix removes. The evidence is still read and
                            // reported so the ledger carries the honest state at record time; it never
                            // refuses the write. The claimed worker owns completing it, before touching
                            // the item's declared `Paths:` (see `.claude/skills/pnext-item` step 1).
                            let sddNotes = sddEvidenceErrors valid
                            if not (List.isEmpty sddNotes) then
                                let detail = String.concat "; " sddNotes
                                eprint $"fsgg-coord-engine: delivery-route: recording sdd-required ahead of its SDD package (%s{detail}) — the claimed worker owns producing it before touching Paths."
                            let marker = StructuredRouteMarker + "\n" + raw.Trim()
                            match Writes.postIssueComment ctx.Transport ref marker with
                            | Error error -> fail error
                            | Ok commentId ->
                                printfn "%s" (JsonSerializer.Serialize {| schema = "fsgg.coord.delivery-route-result/v2"; kind = "recorded"; subject = valid.Subject; decisionRevision = validRecord.Revision; digest = validRecord.Digest; commentId = commentId; sddPackageReady = List.isEmpty sddNotes; sddPackageNotes = sddNotes |})
                                ExitGreen
                with error -> eprint $"fsgg-coord-engine: delivery-route: %s{error.Message}"; ExitError
        | _ -> eprint "fsgg-coord-engine: delivery-route: usage delivery-route <show REF|record REF receipt.json>"; ExitError

    let private graphQlOps (ctx: Context) (opts: Options) : int =
        let print value = printfn "%s" (JsonSerializer.Serialize value); ExitGreen
        let result value project = match value with Ok resolved -> print (project resolved) | Error error -> fail error

        match opts.Args with
        | [ "project-visibility"; owner; title ] ->
            result (OperationalGraphQl.projectVisibility ctx.Transport owner title) (fun publicValue -> {| isPublic = publicValue |})
        | [ "project-id"; owner; number ] ->
            match Int32.TryParse number with
            | true, value when value > 0 -> result (OperationalGraphQl.projectId ctx.Transport owner value) (fun id -> {| id = id |})
            | _ -> eprint "fsgg-coord-engine: graphql project-id requires a positive integer project number"; ExitError
        | [ "repository-policy"; owner; name ] ->
            result (OperationalGraphQl.repositoryPolicy ctx.Transport owner name) (fun policy -> {| issueCreationPolicy = policy.IssueCreationPolicy; hasIssuesEnabled = policy.HasIssuesEnabled |})
        | [ "meter" ] ->
            result (OperationalGraphQl.meterRemaining ctx.Transport) (fun remaining -> {| remaining = remaining |})
        | [ "archive-scan"; projectId ] ->
            result (OperationalGraphQl.archiveScan ctx.Transport projectId) (fun scan ->
                {| pages = scan.Pages; spent = scan.Spent
                   items = scan.Items |> List.map (fun row -> {| itemId = row.ItemId; status = row.Status; blockedBy = row.BlockedBy; number = row.Number; state = row.State; closedAt = row.ClosedAt; repo = row.Repo |}) |})
        | "archive-items" :: projectId :: itemIds when not itemIds.IsEmpty ->
            result (OperationalGraphQl.archiveItems ctx.Transport projectId itemIds) (fun () -> {| archived = itemIds |})
        | [ "roster-board"; owner; title ] ->
            result (OperationalGraphQl.rosterBoard ctx.Transport owner title) (fun rows ->
                rows |> List.map (fun row -> {| owner = row.Owner; repo = row.Repo; number = row.Number; status = row.Status |}))
        | _ ->
            eprint "fsgg-coord-engine: graphql: expected project-visibility OWNER TITLE | project-id OWNER NUMBER | repository-policy OWNER NAME | meter | archive-scan PROJECT-ID | archive-items PROJECT-ID ID... | roster-board OWNER TITLE"
            ExitError

    let run (boardOpsHandlers: Map<Options.Command, HandlerRegistration.Handler>) (opts: Options) : int =
        // #548: the bare-`<n>` default is resolved from what the CALLER actually passed, so it must be read
        // BEFORE the #480 rewrite below replaces `Repo` with the git-remote scope. That rewrite goes through
        // `scopedRepo`, which drops the owner — so reading `opts.Repo` after it would launder a non-FS-GG
        // remote into an "explicit --repo" and defeat the owner check that keeps a bare number a hard error
        // outside the org.
        //
        // #962's parse-time resolution does not disturb this. What is guarded against here is the git-remote
        // FILL-IN — `None` becoming `Some` — and the parser only ever rewrites a `Some` in place. A caller
        // who passed no `--repo` still arrives with `None` and still reaches the owner check.
        let callerOpts = opts

        // #480: a WORKER command DEFAULTS to the repo you are standing in when no `--repo` spells it out; a
        // reconciler stays org-wide. `take` ACTS — it claims an item and prints a worktree command against
        // THIS checkout's origin — so an undetectable scope is a hard error, not a quiet fall-back to the
        // whole org, which is what once handed a `.github` worker another repo's item and a worktree
        // command that would have built it in the wrong repository.
        //
        // This list is about DEFAULTING ONLY (#962) — resolution happened above, for everything. Membership
        // here is a real per-verb judgement ("is this a worker command or a reconciler?"), and a verb left
        // out of it fails LOUDLY rather than silently: it simply has no `--repo`, which `take`/`landable`/
        // `reap` already refuse by name. That is the whole difference from the resolution list this replaced,
        // where being left out bought you a verbatim string compare that matched nothing and exited 0.
        let opts =
            match opts.Command with
            | Who ->
                if opts.AllRepos then opts else { opts with Repo = scopedRepo opts }
            | Next
            | BatchCmd
            | Reap
            | Landable
            | Take -> { opts with Repo = scopedRepo opts }
            | _ -> opts

        match opts.Command with
        | Take when Option.isNone opts.Repo ->
            eprint
                "fsgg-coord-engine: take: --repo required (no git remote here, so 'the repo you are standing in' is undefined)."

            ExitError
        | _ ->

        match context () with
        | Error code -> code
        | Ok(ctx, disposable) ->
            use _ = disposable

            // #548: populate the ONE field every `<ref>` parse defaults against, here, so accepting a bare
            // `<n>` reaches all 15 `parseRef` call sites through a single edit rather than 15.
            let ctx =
                { ctx with
                    DefaultRepo = defaultRepoScope ctx.Owner callerOpts }

            match Map.tryFind opts.Command boardOpsHandlers with
            | Some handler -> handler ctx opts
            | None ->
                match opts.Command with
                | Next -> next ctx opts
                | BatchCmd -> batch ctx opts
                | DriverCmd -> driver ctx opts
                | DeliveryCmd -> delivery ctx opts
                | ReviewCmd -> review ctx opts
                | Ready -> ready ctx opts
                | Reconcile -> reconcile ctx opts
                | Who -> who ctx opts
                | Reap -> reap ctx opts
                | Budget -> budget ctx opts
                | Claim -> claim ctx opts
                | Adopt -> adopt ctx opts
                | Landable -> landable ctx opts
                | Take -> take ctx opts
                | Release -> release ctx opts
                | Heartbeat -> heartbeat ctx opts
                | Widen -> widen ctx opts
                | SetPaths -> setPaths ctx opts
                | Overlap -> overlapCmd ctx opts
                | OpLockAcquire -> opLockAcquire ctx opts
                | OpLockRelease -> opLockRelease ctx opts
                | DoneCmd -> doneCmd ctx opts
                | VerifyPaths -> verifyPaths ctx opts
                | GraphQlOps -> graphQlOps ctx opts
                | LintCmd -> lint ctx opts
                | RouteCmd -> deliveryRouteCmd ctx opts
                | other -> failwith $"Client.run received a non-IO command: %A{other}"
