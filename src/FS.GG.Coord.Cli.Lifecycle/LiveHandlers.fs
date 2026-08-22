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
    let delivery
        (completeDelivery: Context -> Options -> int)
        (deliveryPathsVerified: TouchSet -> string list -> bool)
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
                            let unresolved =
                                [ initial; roundOne; roundTwo; roundThree ]
                                |> List.forall (fun record -> record.Verdict = StructuredDecision.ChangesRequired)
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
                                && receipt.Kind = ReviewWait.RepairConfirmation
                                && receipt.ReviewGeneration =
                                    ReviewWait.generationToken draft.HeadSha ReviewWait.RepairConfirmation Protocol.reviewPolicy.MaxAutomatedRepairRounds
                                && receipt.ClaimGeneration <> string claim.Id
                                && evidence = roundThreeComment.Url
                            let freshClaim = claim.Id > legacy.Id
                            match Reads.prHeadSha ctx.Transport target.Owner target.Repo pr with
                            | Error error -> Error($"the exhausted pull request head could not be read: %A{error}")
                            | Ok liveHead when liveHead <> draft.HeadSha ->
                                Error($"the escalation head is stale: draft %s{draft.HeadSha}, pull request %s{liveHead}")
                            | Ok _ when not expectedKinds -> Error "ordinary exhaustion requires exactly initial plus confirmation rounds 1, 2, and 3"
                            | Ok _ when not unresolved -> Error "ordinary exhaustion requires a changes-required verdict through confirmation round 3"
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
    let doneCmd (offerChoreAfterDone: Context -> Options -> Ref -> unit) (ctx: Context) (opts: Options) : int =
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
