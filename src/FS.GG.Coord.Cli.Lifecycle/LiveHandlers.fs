namespace FS.GG.Coord.Cli.Lifecycle

module LiveHandlers =

    open System
    open System.IO
    open System.Text.Json
    open FS.GG.Coord
    open FS.GG.Coord.Types
    open FS.GG.Coord.GitHub
    open FS.GG.Coord.GitHub.Transport
    open FS.GG.Coord.Cli
    open FS.GG.Coord.Cli.Options
    open FS.GG.Coord.Cli.Render
    open FS.GG.Coord.Cli.Kernel

    [<Literal>]
    let private StructuredReviewMarker = "<!-- fsgg:review-decision/v2 -->"

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
    /// Not `private` — `tests/FS.GG.Coord.Cli.Lifecycle.Tests/DeliveryApplicationTests.fs` drives this directly
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
    let delivery
        (completeDelivery: Delivery.Snapshot -> Delivery.Transition -> Context -> Options -> int)
        (deliveryPathClassifier: Context -> Ref -> TouchSet -> string list -> Delivery.PathClassification list)
        (projectPathVerdict: Delivery.PathClassification list -> bool)
        (requireCurrentDeliveryRoute: Context -> Ref -> Result<DeliveryRoute.Receipt, Errors.IoError>)
        (scanAndDecide: Context -> Options -> Cache.ReadIntent -> Errors.IoResult<Scan.Row list * string * Scan.Receipt>)
        (ctx: Context)
        (opts: Options)
        : int =
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
                                    let pathsVerified =
                                        deliveryPathClassifier ctx target candidate.Item.TouchSet files
                                        |> projectPathVerdict
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
                            let completionDecision =
                                facts |> Delivery.completionFacts |> Delivery.decideCompletion
                            match Delivery.inspect facts, completionDecision, opts.Apply with
                            | Delivery.Next transition, _, true when transition.Action = Delivery.GuardedLand ->
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
                            | Delivery.Next transition, Delivery.CompletionDecision.ProjectCompletion, true
                                when transition.Action = Delivery.Complete ->
                                // Delegate the coupled close / board-Done / own-claim-release sequence to
                                // the existing `done` transaction.  Its `Done.verify` re-reads the merged
                                // closer and refuses a stale or unrelated PR before any write.
                                let code =
                                    completeDelivery
                                        facts
                                        transition
                                        ctx
                                        { opts with Args = [ target.Canonical ]; Pr = pr; Apply = false }
                                if code <> ExitGreen then code
                                else
                                    match Cache.pending () with
                                    | Ok [] -> ExitGreen
                                    | Ok pending ->
                                        eprint $"fsgg-coord-engine: delivery completion left %d{List.length pending} queued board write(s); run `flush` and re-inspect before cleanup."
                                        ExitNoVerdict
                                    | Error error -> fail error
                            | Delivery.Next transition, _, true ->
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
    /// `RepairRouteAvailable` defaults to `true`. Whether a fresh worker/critic slot exists is a
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
                                  // The production writer validates this seven-field receipt against the
                                  // exhausted predecessor PR and the current claim/branch/head before it
                                  // can enter the structured ledger. Reuse that durable fact here instead
                                  // of dropping it at the live adapter boundary.
                                  RepairPhaseGranted = phaseFacts.RepairPhaseReceipt
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
    let private reviewRecordGeneration (record: StructuredDecision.ReviewRecord) =
        match record.Kind with
        | StructuredDecision.Initial ->
            Some(ReviewWait.generationToken record.HeadSha ReviewWait.InitialReview 0)
        | StructuredDecision.Confirmation
        | StructuredDecision.Escalation
        | StructuredDecision.RepairPhase ->
            Some(ReviewWait.generationToken record.HeadSha ReviewWait.RepairConfirmation record.Round)
        | StructuredDecision.Acceptance -> None

    let private isCanonicalReviewGeneration (generation: string) =
        let initialSuffix = ":initial-review:0"
        let confirmationPrefix = ":repair-confirmation:"
        let hasCanonicalHead =
            generation.Length >= 40
            && (generation.Substring(0, 40) |> Seq.forall Uri.IsHexDigit)
        hasCanonicalHead
        && ((generation.Length = 40 + initialSuffix.Length
             && generation.EndsWith(initialSuffix, StringComparison.Ordinal))
            || (generation.Substring(40).StartsWith(confirmationPrefix, StringComparison.Ordinal)
                && match Int32.TryParse(generation.Substring(40 + confirmationPrefix.Length)) with
                   | true, round when round >= 0 -> true
                   | _ -> false))

    let private normalizeCompletionEvidence
        (comments: Reads.CommentBody list)
        (receipt: ReviewWait.WaitReceipt)
        (evidence: string)
        : Result<string, string> =
        if not (isCanonicalReviewGeneration receipt.ReviewGeneration) then
            // Explicit event files predate engine-authored generations and remain readable during the
            // additive migration. They cannot authorize a structured review record because that writer
            // independently requires the canonical token, so preserving their terminal event is safe.
            Ok evidence
        else
            let decoded =
                comments
                |> List.choose (fun comment ->
                    if comment.Body.StartsWith(StructuredReviewMarker + "\n", StringComparison.Ordinal) then
                        Some(comment, Driver.decodeStructuredReview (comment.Body.Substring(StructuredReviewMarker.Length).Trim()))
                    else None)
            let malformed =
                decoded
                |> List.choose (function _, Error reason -> Some reason | _ -> None)
            if not (List.isEmpty malformed) then
                let detail = String.concat "; " malformed
                Error($"the structured review ledger is malformed: %s{detail}")
            else
                let expected =
                    decoded
                    |> List.choose (function
                        | comment, Ok record when reviewRecordGeneration record = Some receipt.ReviewGeneration ->
                            Some(comment, record)
                        | _ -> None)
                match expected with
                | [] ->
                    Error($"completion requires the structured review-decision record for generation '%s{receipt.ReviewGeneration}', but no such record is present")
                | [ requiredComment, requiredRecord ] ->
                    let normalized = evidence.Trim()
                    let digest = requiredRecord.Digest
                    let digestRef = $"sha256:%s{digest}"
                    if normalized = requiredComment.Url
                       || normalized = string requiredComment.Id
                       || normalized.Equals(digest, StringComparison.OrdinalIgnoreCase)
                       || normalized.Equals(digestRef, StringComparison.OrdinalIgnoreCase) then
                        Ok requiredComment.Url
                    else
                        Error(
                            $"completion evidenceRef must identify structured review-decision record %s{requiredComment.Url} "
                            + $"(comment %d{requiredComment.Id}, digest sha256:%s{digest}); the supplied reference '%s{evidence}' does not"
                        )
                | matches ->
                    let urls = matches |> List.map (fst >> _.Url) |> String.concat ", "
                    Error($"completion evidence is ambiguous: generation '%s{receipt.ReviewGeneration}' matches multiple structured review-decision records: %s{urls}")

    let private appendReviewWait
        (ctx: Context)
        (opts: Options)
        (target: Ref)
        (pr: int)
        (requestedEvent: ReviewWait.Transition)
        : int =
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
                let prior = parsed |> List.choose (function Ok (Some prior) -> Some prior | _ -> None)
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
                let entryFor generation =
                    prior
                    |> List.tryPick (function ReviewWait.Enter old when old.ReviewGeneration = generation -> Some old | _ -> None)
                let normalizedEvent =
                    match requestedEvent with
                    | ReviewWait.Complete (generation, at, evidence) ->
                        match entryFor generation with
                        | None -> Ok requestedEvent
                        | Some receipt ->
                            normalizeCompletionEvidence comments receipt evidence
                            |> Result.map (fun normalized -> ReviewWait.Complete(generation, at, normalized))
                    | _ -> Ok requestedEvent
                match normalizedEvent with
                | Error reason -> eprint $"fsgg-coord-engine: review wait: refused: %s{reason}"; ExitNoVerdict
                | Ok event ->
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
                                let entry = entryFor generation
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
                        let markerBody = ReviewWait.encode event
                        let prTarget = { target with Number = pr }
                        match Writes.postIssueComment ctx.Transport prTarget markerBody with
                        | Error error -> fail error
                        | Ok commentId ->
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
                | Ok (Some event) -> appendReviewWait ctx opts target pr event
            with error -> eprint $"fsgg-coord-engine: review wait: %s{error.Message}"; ExitError

    let private enterReviewWait (ctx: Context) (opts: Options) (rawRef: string) : int =
        match parseRef ctx rawRef, opts.Pr with
        | Error message, _ -> eprint $"fsgg-coord-engine: review wait enter: %s{message}"; ExitError
        | _, None -> eprint "fsgg-coord-engine: review wait enter: --pr is required."; ExitError
        | Ok target, Some pr ->
            match Reads.markerScan ctx.Transport target.Owner target.Repo target.Number
                  |> Result.bind (Reads.requireCompleteMarkerScan target.Short),
                  Reads.prHeadSha ctx.Transport target.Owner target.Repo pr,
                  Reads.prLandable ctx.Transport target.Owner target.Repo pr,
                  Reads.commentsWithIdentity ctx.Transport target.Owner target.Repo pr with
            | Error error, _, _, _
            | _, Error error, _, _
            | _, _, _, Error error -> fail error
            | Ok markers, Ok head, checks, Ok comments ->
                match Reads.winner opts.LeaseMinutes markers with
                | None -> eprint "fsgg-coord-engine: review wait enter: no current claim generation can authorize this write."; ExitNoVerdict
                | Some claim ->
                    let reviewComments =
                        comments
                        |> List.map (fun comment -> ({ Id = comment.Id; Url = comment.Url; Body = comment.Body }: Driver.ReviewComment))
                    let phaseFacts = Driver.reviewPhaseFacts reviewComments
                    let round = phaseFacts.ConfirmationCount + 1
                    let binding: Review.Binding =
                        { ItemRef = target.Canonical
                          Pr = pr
                          HeadSha = head
                          ClaimGeneration = string claim.Id
                          ImplementerIdentity = claim.Worker.Value
                          Phase = if phaseFacts.RepairPhasePresent then Review.Repair else Review.Ordinary
                          Round = round }
                    let facts: Review.Facts =
                        { Comments = reviewComments
                          Checks = checks
                          RepairPhaseGranted = phaseFacts.RepairPhaseReceipt
                          RepairRouteAvailable = true
                          DiffAuditTrusted = None }
                    match Review.inspect binding facts None None with
                    | Error reasons ->
                        let detail = String.concat "; " reasons
                        eprint $"fsgg-coord-engine: review wait enter: refused: %s{detail}"
                        ExitNoVerdict
                    | Ok verdict ->
                        let authority =
                            match verdict.NextAction, verdict.State with
                            | Review.DispatchCritic, _ -> Ok(ReviewWait.InitialReview, 0)
                            | Review.DispatchSuccessor _, Review.AwaitingSuccessorReview nextRound
                            | Review.DispatchSuccessor _, Review.RepairPhaseActive nextRound ->
                                Ok(ReviewWait.RepairConfirmation, nextRound)
                            | _ -> Error "the current review state does not authorize a critic dispatch"
                        match authority with
                        | Error reason -> eprint $"fsgg-coord-engine: review wait enter: refused: %s{reason}"; ExitNoVerdict
                        | Ok(kind, requiredRound) ->
                            // Durable terminal events are commonly authored at whole-second precision.
                            // Emit the entry on the same precision so an immediate completion/cancel at
                            // the current second cannot appear to predate an entry by sub-second ticks.
                            let observed = DateTimeOffset.UtcNow
                            let now = observed.AddTicks(-(observed.Ticks % TimeSpan.TicksPerSecond))
                            let event =
                                ReviewWait.Enter
                                    { Item = target.Canonical
                                      ClaimGeneration = string claim.Id
                                      ReviewGeneration = ReviewWait.generationToken head kind requiredRound
                                      Kind = kind
                                      EnteredAt = now
                                      ExpiresAt = now.AddHours 4.0
                                      EvidenceRef = $"https://github.com/%s{target.Owner}/%s{target.Repo}/pull/%d{pr}" }
                            appendReviewWait ctx opts target pr event

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

                let authorizeRepairPhaseEntry () =
                    match draft.RepairPhaseReceipt with
                    | None -> Error "a repair-phase record requires the seven-field repairPhaseReceipt"
                    | Some entry when entry.ExhaustedPr = pr -> Error "repairPhaseReceipt.exhaustedPr must name the closed predecessor PR"
                    | Some entry when entry.NewClaimGeneration <> string claim.Id -> Error "repairPhaseReceipt.newClaimGeneration is not current"
                    | Some entry when entry.NewImplementerIdentity <> claim.Worker.Value -> Error "repairPhaseReceipt.newImplementerIdentity is not the current claimant"
                    | Some entry when entry.NewCriticIdentity <> draft.Critic -> Error "repairPhaseReceipt.newCriticIdentity does not match the repair-phase critic"
                    | Some entry when entry.CandidateHeadSha <> draft.HeadSha -> Error "repairPhaseReceipt.candidateHeadSha does not match the repair-phase record head"
                    | Some entry ->
                        match Reads.prHeadRef ctx.Transport target.Owner target.Repo pr,
                              Reads.prHeadSha ctx.Transport target.Owner target.Repo pr,
                              Reads.commentsWithIdentity ctx.Transport target.Owner target.Repo entry.ExhaustedPr with
                        | Error error, _, _
                        | _, Error error, _
                        | _, _, Error error -> Error(sprintf "repair-phase entry provenance could not be read: %A" error)
                        | Ok branch, Ok head, Ok exhaustedComments ->
                            let prBinding = string pr
                            let branchOrPrMatches =
                                entry.NewBranchOrPr = branch
                                || entry.NewBranchOrPr = prBinding
                                || entry.NewBranchOrPr = $"#%d{pr}"
                                || entry.NewBranchOrPr = $"pr/%d{pr}"
                            if not branchOrPrMatches then
                                Error "repairPhaseReceipt.newBranchOrPr does not match the current branch or PR"
                            elif head <> entry.CandidateHeadSha then
                                Error "repairPhaseReceipt.candidateHeadSha is stale for the current PR"
                            elif claim.Id <= entry.EscalationCommentId then
                                Error "repairPhaseReceipt.newClaimGeneration must be newer than the exhausted escalation comment"
                            elif Reads.prLandable ctx.Transport target.Owner target.Repo entry.ExhaustedPr <> Types.PrClosed then
                                Error "repairPhaseReceipt.exhaustedPr must be closed without merging"
                            else
                                let reviewComments =
                                    exhaustedComments
                                    |> List.map (fun comment -> ({ Id = comment.Id; Url = comment.Url; Body = comment.Body }: Driver.ReviewComment))
                                let phaseFacts = Driver.reviewPhaseFacts reviewComments
                                let exactEscalation =
                                    exhaustedComments
                                    |> List.tryFind (fun comment -> comment.Id = entry.EscalationCommentId)
                                    |> Option.bind (fun comment ->
                                        if comment.Body.StartsWith(StructuredReviewMarker + "\n", StringComparison.Ordinal) then
                                            Driver.decodeStructuredReview (comment.Body.Substring(StructuredReviewMarker.Length).Trim())
                                            |> Result.toOption
                                        else None)
                                match phaseFacts.StructuredErrors, exactEscalation with
                                | errors, _ when not (List.isEmpty errors) ->
                                    let detail = String.concat "; " errors
                                    Error($"the exhausted repair-phase provenance ledger is invalid: %s{detail}")
                                | _, Some escalation
                                    when escalation.Kind = StructuredDecision.Escalation
                                         && escalation.Subject = $"%s{target.Canonical}/pr/%d{entry.ExhaustedPr}" -> Ok ()
                                | _ -> Error "repairPhaseReceipt.escalationCommentId does not name the exhausted PR's structured escalation record"

                let authorizeExhaustedClaimTurnover (receipt: ReviewWait.WaitReceipt) evidence =
                    let expectedSubject = $"%s{target.Canonical}/pr/%d{pr}"
                    let ordered = comments |> List.sortBy _.Id
                    let structured =
                        ordered
                        |> List.choose (fun comment ->
                            if comment.Body.StartsWith(StructuredReviewMarker + "\n", StringComparison.Ordinal) then
                                Some(comment, Driver.decodeStructuredReview (comment.Body.Substring(StructuredReviewMarker.Length).Trim()))
                            else None)
                    let decodeErrors =
                        structured
                        |> List.choose (fun (_, decoded) -> match decoded with Error error -> Some error | Ok _ -> None)
                    let pairs =
                        structured
                        |> List.choose (fun (comment, decoded) ->
                            decoded |> Result.toOption |> Option.map (fun record -> comment, record))
                    let records = pairs |> List.map snd
                    let generationPairs =
                        pairs
                        |> List.indexed
                        |> List.choose (fun (index, (_, record)) ->
                            if record.Kind = StructuredDecision.Initial then Some index else None)
                        |> List.tryLast
                        |> Option.map (fun start -> pairs[start..])
                        |> Option.defaultValue []
                    let legacyMarker = "<!-- fsgg:independent-review-escalation:v1 -->"
                    let lines (body: string) = body.Replace("\r\n", "\n").Split '\n' |> Array.toList
                    let legacyComments =
                        ordered
                        |> List.filter (fun comment -> lines comment.Body |> List.tryHead = Some legacyMarker)
                    let legacyMarkerCount =
                        ordered
                        |> List.sumBy (fun comment -> lines comment.Body |> List.filter ((=) legacyMarker) |> List.length)
                    let legacyField name body =
                        let prefix = name + ": "
                        lines body
                        |> List.choose (fun line ->
                            if line.StartsWith(prefix, StringComparison.Ordinal) then
                                Some(line.Substring(prefix.Length).Trim())
                            else None)
                        |> List.tryExactlyOne
                    let completionIds =
                        ordered
                        |> List.choose (fun comment ->
                            match ReviewWait.tryDecode comment.Body with
                            | Ok (Some (ReviewWait.Complete (generation, _, completedEvidence)))
                                when generation = receipt.ReviewGeneration && completedEvidence = evidence -> Some comment.Id
                            | _ -> None)
                    let terminalIds =
                        ordered
                        |> List.choose (fun comment ->
                            match ReviewWait.tryDecode comment.Body with
                            | Ok (Some (ReviewWait.Complete (generation, _, _)))
                            | Ok (Some (ReviewWait.Cancel (generation, _, _)))
                            | Ok (Some (ReviewWait.Timeout (generation, _, _)))
                                when generation = receipt.ReviewGeneration -> Some comment.Id
                            | _ -> None)

                    match StructuredDecision.validateReviewLedger expectedSubject records with
                    | Error errors ->
                        let detail = String.concat "; " errors
                        Error($"the exhausted structured review ledger is invalid: %s{detail}")
                    | Ok _ when not (List.isEmpty decodeErrors) ->
                        let detail = String.concat "; " decodeErrors
                        Error($"the exhausted structured review ledger is unreadable: %s{detail}")
                    | Ok _ ->
                        match generationPairs, legacyComments, completionIds, terminalIds with
                        | [ (initialComment, initial)
                            (roundOneComment, roundOne)
                            (roundTwoComment, roundTwo)
                            (roundThreeComment, roundThree) ],
                          [ legacy ],
                          [ completedCommentId ],
                          [ terminalCommentId ] when completedCommentId = terminalCommentId ->
                            let expectedKinds =
                                initial.Kind = StructuredDecision.Initial && initial.Round = 0
                                && roundOne.Kind = StructuredDecision.Confirmation && roundOne.Round = 1
                                && roundTwo.Kind = StructuredDecision.Confirmation && roundTwo.Round = 2
                                && roundThree.Kind = StructuredDecision.Confirmation && roundThree.Round = 3
                            // `.github#2807`: repairs advance the pull-request head. The structured-ledger
                            // validator above already exact-binds every record to its own 40-hex head and
                            // preserves the ordered revision/digest/backlink/round chain. Requiring every
                            // historical head to equal the terminal draft therefore rejects the ordinary
                            // multi-round shape. Terminal authority remains exact below: round three, the
                            // escalation draft, completed wait, legacy marker, and live PR all bind one head.
                            let reviewComments =
                                ordered
                                |> List.map (fun comment ->
                                    ({ Id = comment.Id; Url = comment.Url; Body = comment.Body }: Driver.ReviewComment))
                            let terminalChecks =
                                if roundThree.Verdict = StructuredDecision.Pass then
                                    Reads.prLandable ctx.Transport target.Owner target.Repo pr
                                else
                                    Types.PrUnknown
                            let terminalDecision =
                                Review.decideOrdinaryExhaustion
                                    { Phase = Review.Ordinary
                                      HeadSha = draft.HeadSha
                                      CurrentClaimGeneration = string claim.Id
                                      Checks = terminalChecks
                                      Comments = reviewComments
                                      WaitState = Some state }
                            let legacyBody = legacy.Body
                            let legacyMatches =
                                legacyMarkerCount = 1
                                && legacy.Id > completedCommentId
                                && legacyField "exhausted-head" legacyBody = Some draft.HeadSha
                                && legacyField "initial-review" legacyBody = Some initialComment.Url
                                && legacyField "confirmation-1" legacyBody = Some roundOneComment.Url
                                && legacyField "confirmation-2" legacyBody = Some roundTwoComment.Url
                                && legacyField "confirmation-3" legacyBody = Some roundThreeComment.Url
                                && legacyField "critic" legacyBody = Some roundThree.Critic
                                && legacyField "verdict" legacyBody = Some "ordinary-chain-exhausted"
                            let exactDraft =
                                draft.Round = Protocol.reviewPolicy.MaxAutomatedRepairRounds
                                && draft.Verdict = StructuredDecision.ChangesRequired
                                && draft.HeadSha = roundThree.HeadSha
                                && draft.PreviousDigest = Some roundThree.Digest
                                && draft.InitialReview = Some initialComment.Url
                                && draft.PrecedingReview = Some roundThreeComment.Url
                                && draft.Critic = roundThree.Critic
                                && draft.Succession.IsNone
                            let exactWait =
                                receipt.Item = target.Canonical
                                && evidence = roundThreeComment.Url
                            let freshClaim = claim.Id > legacy.Id
                            match Reads.prHeadSha ctx.Transport target.Owner target.Repo pr with
                            | Error error -> Error($"the exhausted pull request head could not be read: %A{error}")
                            | Ok liveHead when liveHead <> draft.HeadSha ->
                                Error($"the escalation head is stale: draft %s{draft.HeadSha}, pull request %s{liveHead}")
                            | Ok _ when not expectedKinds -> Error "ordinary exhaustion requires exactly initial plus confirmation rounds 1, 2, and 3"
                            | Ok _ when terminalDecision <> Review.OrdinaryExhaustionDecision.CompletedOrdinaryExhaustion ->
                                Error "ordinary exhaustion requires changes-required through round 2 and either round-3 changes-required or an exact-head round-3 pass with settled red checks"
                            | Ok _ when not legacyMatches -> Error "legacy ordinary-exhaustion evidence is missing, duplicated, stale, or malformed"
                            | Ok _ when not exactDraft -> Error "the escalation draft does not bind the exact exhausted head, round, digest, critic, and backlinks"
                            | Ok _ when not exactWait -> Error "the completed wait does not bind the exact old-claim ordinary round-3 generation"
                            | Ok _ when not freshClaim -> Error "the current claimant is not a fresh post-exhaustion claim generation"
                            | Ok _ -> Ok ()
                        | _ -> Error "ordinary exhaustion requires one exact initial+confirmation1/2/3 chain, one completed round-3 wait, and one legacy escalation"
                match draft.Kind, state with
                | StructuredDecision.Escalation, ReviewWait.Completed (receipt, evidence) ->
                    authorizeExhaustedClaimTurnover receipt evidence
                | StructuredDecision.Acceptance, ReviewWait.Completed (receipt, evidence)
                    when receipt.ClaimGeneration = string claim.Id && draft.PrecedingReview = Some evidence -> Ok ()
                | StructuredDecision.Acceptance, _ ->
                    Error "host acceptance requires the immediately preceding critic record's durable review wait to be completed"
                | StructuredDecision.Initial, ReviewWait.Waiting receipt
                    when generationMatches ReviewWait.InitialReview 0 receipt -> Ok ()
                | StructuredDecision.RepairPhase, ReviewWait.Waiting receipt
                    when generationMatches ReviewWait.RepairConfirmation draft.Round receipt ->
                    authorizeRepairPhaseEntry ()
                | (StructuredDecision.Confirmation | StructuredDecision.Escalation),
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
        | [ "wait"; "enter"; rawRef ] -> enterReviewWait ctx opts rawRef
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
                          $"PR #%d{pr} carries no valid host review-acceptance marker (`%s{reviewAcceptedRequireToken}`) — the review chain is absent, incomplete, or malformed; enter the bounded critic queue with `scripts/fsgg-coord review wait`, then append its completed decision with `scripts/fsgg-coord review record`" ]
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
                            $"fsgg-coord-engine:   This is a TERMINAL verdict, not a retry. If you are recovering an item whose worker died between merge and completion projection, the work LANDED — complete delivery: scripts/fsgg-coord delivery <ref> --pr %d{pr} --flip --apply"

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
    let private runDone
        (completionAuthority: (Delivery.Snapshot * Delivery.Transition) option)
        (offerChoreAfterDone: Context -> Options -> Ref -> unit)
        (ctx: Context)
        (opts: Options)
        : int =
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
                        // A correction receipt proves this issue was previously observed CLOSED without
                        // completion authority and was reopened by reconciliation. Delivery completion may
                        // therefore verify the historical closing merge while the mutable issue projection
                        // is OPEN; standalone `done` retains the ordinary CLOSED precondition.
                        let factsForVerdict =
                            match completionAuthority, facts.State with
                            | Some _, Open ->
                                match Reads.commentBodies ctx.Transport ref.Owner ref.Repo ref.Number with
                                | Error error ->
                                    failwith(
                                        "delivery completion could not read its correction evidence: "
                                        + Errors.explain error
                                    )
                                | Ok comments ->
                                    match Done.completionCorrectionStateFor ref comments with
                                    | Done.VerifiedCompletionCorrection _ -> { facts with State = Closed }
                                    | Done.InvalidCompletionCorrection errors ->
                                        failwith(
                                            "delivery completion found invalid correction evidence: "
                                            + String.concat "; " errors
                                        )
                                    | Done.NoCompletionCorrection -> facts
                            | _ -> facts
                        let verdict = Done.verify opts.Pr opts.Evidence factsForVerdict
                        let verdict =
                            match completionAuthority, verdict with
                            | None, Green(Done.ClosedByPullRequest(actualPr, mergeSha, _, _)) ->
                                match Reads.commentBodies ctx.Transport ref.Owner ref.Repo ref.Number with
                                | Error error ->
                                    Red
                                        [ "standalone done could not read typed completion authority: "
                                          + Errors.explain error ]
                                | Ok comments ->
                                    match Done.receiptStateFor ref comments with
                                    | Done.VerifiedCompletionReceipt receipt
                                        when receipt.PullRequest = actualPr && receipt.MergeSha = mergeSha ->
                                        verdict
                                    | Done.VerifiedCompletionReceipt receipt ->
                                        Red
                                            [ $"standalone done found typed completion authority for PR #%d{receipt.PullRequest} @ %s{receipt.MergeSha}, not the verified closer PR #%d{actualPr} @ %s{mergeSha}" ]
                                    | Done.LegacyReceipt ->
                                        Red
                                            [ "legacy done evidence is not completion authority; run delivery --pr <pr> --flip --apply to verify the exact merge and mint a typed receipt" ]
                                    | Done.NoReceipt ->
                                        Red
                                            [ "standalone done cannot mint completion authority; run delivery --pr <pr> --flip --apply first" ]
                                    | Done.InvalidCompletionReceipt errors ->
                                        Red
                                            [ "typed completion authority is invalid: " + String.concat "; " errors ]
                            | None, Green _ ->
                                Red
                                    [ "standalone done requires typed pull-request completion authority; non-PR completion cannot be projected terminal" ]
                            | _ -> verdict
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
                        | Green closure ->
                            // Write durable evidence before the mutable Project projection.  If the latter
                            // is deferred, the scheduled lifecycle reconciler can later prove that this was
                            // a verified terminal transition rather than guessing from issue closure.
                            // `renderReceipt`, not `render`: the durable comment deliberately keeps the
                            // passed-over-foreign-closer note for provenance even though stdout no longer
                            // does (.github#2444).
                            match completionAuthority, closure with
                            | Some(authorityFacts, authorityTransition), Done.ClosedByPullRequest(actualPr, mergeSha, _, _)
                                when authorityFacts.Freshness.PullRequest = Some actualPr ->
                                match Reads.commentBodies ctx.Transport ref.Owner ref.Repo ref.Number with
                                | Error error ->
                                    failwith(
                                        "delivery completion could not establish self-host replay authority: "
                                        + Errors.explain error
                                    )
                                | Ok comments ->
                                    match Done.selfHostReplayState comments with
                                    | SelfHost.NoBootstrap
                                    | SelfHost.VerifiedReplay _ -> ()
                                    | SelfHost.ReplayRequired bootstrap ->
                                        failwith(
                                            $"delivery completion is blocked until the shared engine records post-merge replay for self-host bootstrap %s{bootstrap.Digest}"
                                        )
                                    | SelfHost.InvalidReplay errors ->
                                        failwith(
                                            "delivery completion found invalid self-host replay evidence: "
                                            + String.concat "; " errors
                                        )
                                match
                                    Delivery.advance
                                        authorityTransition.FreshnessToken
                                        authorityTransition.ActionKey
                                        authorityFacts
                                with
                                | Delivery.Next current when current.Action = Delivery.Complete ->
                                    match
                                        Delivery.createCompletionReceipt
                                            ref.Canonical
                                            actualPr
                                            mergeSha
                                            DateTimeOffset.UtcNow
                                            current.FreshnessToken
                                            current.ActionKey
                                            (Delivery.completionFacts authorityFacts)
                                    with
                                    | Error errors ->
                                        failwith(
                                            "delivery completion receipt was refused after merge verification: "
                                            + String.concat "; " errors
                                        )
                                    | Ok receipt ->
                                        match Writes.deliveryCompletionReceipt ctx.Transport ref receipt with
                                        | Ok () ->
                                            if facts.State = Open then
                                                match Writes.closeIssueCompleted ctx.Transport ref with
                                                | Ok () -> ()
                                                | Error error ->
                                                    failwith(
                                                        "delivery completion receipt landed but issue closure could not be freshly verified: "
                                                        + Errors.explain error
                                                    )
                                        | Error error ->
                                            failwith(
                                                "verified delivery completion could not append its receipt: "
                                                + Errors.explain error
                                            )
                                | decision ->
                                    failwithf "delivery completion transition changed after merge verification: %A" decision
                            | Some _, Done.ClosedByPullRequest(actualPr, _, _, _) ->
                                failwithf "delivery completion pull request changed after inspection: %A" actualPr
                            | Some _, _ ->
                                failwith "delivery completion requires a verified pull-request merge"
                            | None, _ ->
                                // Standalone `done` is now a replay-only projection over a matching typed
                                // completion receipt. The admission above makes this branch write-free.
                                ()
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

    let doneCmd
        (offerChoreAfterDone: Context -> Options -> Ref -> unit)
        (ctx: Context)
        (opts: Options)
        : int =
        runDone None offerChoreAfterDone ctx opts

    let completeDelivery
        (offerChoreAfterDone: Context -> Options -> Ref -> unit)
        (facts: Delivery.Snapshot)
        (transition: Delivery.Transition)
        (ctx: Context)
        (opts: Options)
        : int =
        runDone (Some(facts, transition)) offerChoreAfterDone ctx opts


    [<Literal>]
    let private StructuredRouteMarker = "<!-- fsgg:route-decision/v2 -->"

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

    let readDeliveryRouteComments (ctx: Context) (target: Ref) =
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

    let routeEvidence (subject: string) (comments: string list) : DeliveryRoute.Verdict =
        match structuredRouteLedger subject comments with
        | Error errors -> DeliveryRoute.Stale errors
        | Ok(Some(_, latest)) ->
            DeliveryRoute.Current(StructuredDecision.toEffectiveRoute latest)
        | Ok None ->
            DeliveryRoute.Stale [ "structured route ledger is missing" ]

    /// The route decision is an impure receipt: both the source item and its append-only receipt ledger
    /// are read immediately before the pure scheduler sees the item.  An unreadable read stays typed as
    /// unreadable, rather than collapsing into a missing/lightweight decision.
    let readDeliveryRouteVerdict (ctx: Context) (target: Ref) =
        match readDeliveryRouteComments ctx target with
        | Error error -> DeliveryRoute.Unreadable [ Errors.explain error ]
        | Ok comments -> routeEvidence target.Canonical comments

    /// Mutation boundaries need the underlying IO error as well as the fail-closed route verdict.  In
    /// particular, a rate-limited receipt read must remain EX_RATE for a JSON worker, not be flattened into
    /// a malformed/missing route and accidentally lose its back-off contract.
    let requireCurrentDeliveryRoute (ctx: Context) (target: Ref) =
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
    /// removes the only pointer to its cause. It takes one complete, paginated REST ledger read and no
    /// second issue-body read.
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
    let verifyPaths
        (deliveryPathClassifier: Context -> Ref -> TouchSet -> string list -> Delivery.PathClassification list)
        (projectPathVerdict: Delivery.PathClassification list -> bool)
        (digestWarn: unit -> unit)
        (ctx: Context)
        (opts: Options)
        : int =
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
                            let classifications = deliveryPathClassifier ctx issue (Declared tokens) files

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
                            // Both authorities hang off the classifier's one preliminary all-declared
                            // verdict, so neither can be re-armed independently on the happy path.
                            let pathsWith admission =
                                classifications
                                |> List.choose (fun classification ->
                                    if classification.Admission = admission then Some classification.Path else None)

                            let sddPackage = pathsWith Delivery.MandatorySddPath
                            let regenerated = pathsWith Delivery.GeneratedPath
                            let undeclared =
                                classifications
                                |> List.choose (fun classification ->
                                    match classification.Admission with
                                    | Delivery.UndeclaredAuthoredPath
                                    | Delivery.UnknownPath -> Some classification.Path
                                    | _ -> None)

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
                            digestWarn ()

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

                            if projectPathVerdict classifications then
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

    /// `followup audit` is intentionally a read-only reconciliation PREVIEW. A queue's mtime is a
    /// candidate selector, not evidence that its worker died: each queued issue is re-read from GitHub,
    /// then its marker scan is required to be complete before we say it has no live claim.
    let followupAudit (ctx: Context) (opts: Options) : int =
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
