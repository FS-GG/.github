namespace FS.GG.Coord.Cli.Lifecycle

module LiveHandlers =

    open FS.GG.Coord.Cli

    /// Preserve the parser's exact malformed-chain diagnostic at the live delivery boundary.  Keeping
    /// this small adapter named and directly testable prevents a future `Result.toOption` from turning
    /// an attempted-but-invalid review into the distinct fact that no review was posted.
    val deliveryReviewEvidence:
      landable: bool ->
        comments: FS.GG.Coord.Driver.ReviewComment list ->
        FS.GG.Coord.Driver.ReviewChain option * string option

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
    val outstandingObligations:
      headFact: FS.GG.Coord.GitHub.Errors.IoResult<string> ->
        commentsFact: FS.GG.Coord.GitHub.Errors.IoResult<FS.GG.Coord.GitHub.Reads.CommentBody list> ->
        bool

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
    val authorizedMarker:
      leaseMinutes: int ->
        markers: FS.GG.Coord.GitHub.Reads.Marker list ->
        liveness: (unit ->
                     FS.GG.Coord.GitHub.Errors.IoResult<FS.GG.Coord.Types.Liveness>) ->
        FS.GG.Coord.GitHub.Errors.IoResult<FS.GG.Coord.GitHub.Reads.Marker option>

    /// The exact marker text `delivery` writes: `v=1 item=<owner/repo>#<n> gen=<claim marker comment
    /// id> opkey=<64 lowercase hex> grant=<election comment id> head=<40-hex sha>`.
    ///
    /// ALL SIX of `scripts/check-claim-fence.py`'s `REQUIRED_AUTH_FIELDS`. Writing only four — which
    /// is what landed the first time this row was worked — did not make that gate pass NARROWLY: it
    /// stopped the gate at CHECK 1, so check 4, the only one of the six a forger cannot satisfy by
    /// typing, was never evaluated on any real pull request.
    ///
    /// The two narrower readers are unaffected and need no cutover: `check-claim-generation.py` (the
    /// only marker reader among `main`'s required contexts) and the receiver-side validation job in
    /// `.github/workflows/kit-materialize.yml` each require four fields and accept additional pairs.
    val authorizationMarker:
      item: string ->
        gen: string -> opkey: string -> grant: string -> head: string -> string

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
    ///
    /// That second rule is also the WHOLE migration for the six-field marker: a four-field marker
    /// left on an open pull request is not byte-identical to the freshly rendered one, so it is
    /// replaced in place on the next `delivery` call, one marker in and one marker out.
    val rebindAuthorization:
      body: string ->
        item: string ->
        gen: string ->
        opkey: string -> grant: string -> head: string -> AuthorizationRebind

    /// The merge election this pull request's authorization is GROUNDED IN, as `(opkey, grant)` —
    /// posting one only when this delivery target does not already own it (.github#2395, design
    /// §4.2, §11.2 row 3's first act).
    ///
    /// `opkey` is `Operation.compose item gen receiver Merge`, CALLED rather than re-expressed so
    /// this producer and the fence's check 5 cannot disagree about a key. `grant` is the election
    /// comment's server-assigned id, which is what a forger cannot choose and therefore what makes
    /// the authorization grounded rather than decorative.
    ///
    /// IDEMPOTENT, AND THAT IS A CORRECTNESS PROPERTY RATHER THAN A SAVING. An election bearing this
    /// operation key AND this pull request is reused — the LOWEST of them, so a duplicate a lost POST
    /// response could have created cannot change which id is granted. Posting unconditionally would
    /// deny this pull request for the rest of its claim generation, because a second election carries
    /// a strictly higher comment id and the fence grants only the lowest.
    ///
    /// It REFUSES rather than degrades: a `compose` refusal, an unreadable comment list or a failed
    /// POST propagates, and `ensureAuthorization` therefore writes nothing at all — never a
    /// four-field fallback, which is the decorative case design §6.3 names.
    ///
    /// Not `private` — `tests/FS.GG.Coord.Cli.Tests` drives it directly against a `Fake.Recorder`,
    /// the same internal-seam idiom `ensureAuthorization` and `authorizedMarker` already use.
    val electionGrounding:
      ctx: Kernel.Context ->
        target: FS.GG.Coord.Types.Ref ->
        gen: string ->
        pr: int -> FS.GG.Coord.GitHub.Errors.IoResult<string * string>

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
    val ensureAuthorization:
      ctx: Kernel.Context ->
        target: FS.GG.Coord.Types.Ref ->
        marker: FS.GG.Coord.GitHub.Reads.Marker option ->
        pr: int option ->
        head: string ->
        merged: bool -> FS.GG.Coord.GitHub.Errors.IoResult<unit>

    /// `review` — the resumable review/repair protocol (.github#2175) as one typed answer, and `review record`
    /// as its only writer.
    ///
    /// Two shapes reach here and they are not symmetric. `review record REF draft.json --pr N` seals and
    /// APPENDS the next structured v2 decision to the PR; every other argument shape inspects and writes
    /// nothing, returning exactly one closed protocol state plus the single next action that follows from it,
    /// bound to a freshness token that a changed head invalidates. It decides ordering only: materiality,
    /// same-critic continuity and repair-phase provenance stay authored by the agents, and this verb never
    /// substitutes for them.
    ///
    /// Exit codes come from the delegated application service rather than from this dispatcher.
    val review: ctx: Kernel.Context -> opts: Options.Options -> int


    val delivery:
      completeDelivery: (Kernel.Context -> Options.Options -> int) ->
        deliveryPathsVerified: (FS.GG.Coord.Types.TouchSet -> string list -> bool) ->
        requireCurrentDeliveryRoute: (Kernel.Context -> FS.GG.Coord.Types.Ref -> Result<FS.GG.Coord.DeliveryRoute.Receipt, FS.GG.Coord.GitHub.Errors.IoError>) ->
        scanAndDecide: (Kernel.Context -> Options.Options -> FS.GG.Coord.GitHub.Cache.ReadIntent -> FS.GG.Coord.GitHub.Errors.IoResult<FS.GG.Coord.GitHub.Scan.Row list * string * FS.GG.Coord.GitHub.Scan.Receipt>) ->
        ctx: Kernel.Context -> opts: Options.Options -> int

    val landable: ctx: Kernel.Context -> opts: Options.Options -> int

    val doneCmd:
      offerChoreAfterDone: (Kernel.Context -> Options.Options -> FS.GG.Coord.Types.Ref -> unit) ->
        ctx: Kernel.Context -> opts: Options.Options -> int

    val sddReadinessEvidenceErrors: workId: string -> raw: string -> string list

    val sddEvidenceErrors: receipt: FS.GG.Coord.DeliveryRoute.Receipt -> string list

    val readDeliveryRouteComments:
      ctx: Kernel.Context ->
        target: FS.GG.Coord.Types.Ref ->
        FS.GG.Coord.GitHub.Errors.IoResult<string list>

    val routeEvidence:
      subject: string -> comments: string list -> FS.GG.Coord.DeliveryRoute.Verdict

    val readDeliveryRouteVerdict:
      ctx: Kernel.Context -> target: FS.GG.Coord.Types.Ref -> FS.GG.Coord.DeliveryRoute.Verdict

    val requireCurrentDeliveryRoute:
      ctx: Kernel.Context ->
        target: FS.GG.Coord.Types.Ref ->
        Result<FS.GG.Coord.DeliveryRoute.Receipt, FS.GG.Coord.GitHub.Errors.IoError>

    val verifyPaths:
      generatedPaths: (string -> Set<string>) ->
        kitRoot: (unit -> string option) ->
        digestWarn: (unit -> unit) ->
        ctx: Kernel.Context -> opts: Options.Options -> int

    val followupAudit:
      ctx: Kernel.Context -> opts: Options.Options -> int

    val deliveryRouteCmd: ctx: Kernel.Context -> opts: Options.Options -> int
