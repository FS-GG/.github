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
///
/// ── WHAT THIS SIGNATURE IS, AND WHAT IT MEASURED (.github#2724) ─────────────────────────────────────
///
/// `Client.fs` is 11,090 lines — 59% of this project's 18,897 lines of `.fs`, and 3.7× the next-largest
/// source file in the repository (`FS.GG.Coord.GitHub/Reads.fs`, 2,982). Until this file existed nothing
/// distinguished its CONTRACT from the bindings that were merely reachable.
/// This signature is that distinction, and the compiler now holds it: a binding absent from here is
/// private to the module whatever the implementation says, so the surface can widen only by an edit to
/// THIS file, in a diff somebody reviews.
///
/// The question "what is actually public here" was DISCOVERED rather than designed. Every module-level
/// binding was made private, the solution and all three test suites were built, and the compiler named
/// the residue. What it named:
///
///   * 186 module-level bindings. 96 were already `private` and 3 more are the `let mutable private`
///     forward declarations described below; 87 were public.
///   * 10 of those 87 were reachable only from inside this module — `renderLiveDecision`, `child`,
///     `say`, `roomOpen`, `context`, `bootstrapCmd`, `fieldId`, `optionId`, `itemIdCmd`, `flushCmd`
///     (each a verb `run` dispatches to itself). They are absent from this file and are now private.
///     `OpLock.LeaseMinutes` and `OpLock.release` went the same way.
///   * 77 remain, and this is the finding the extraction programme (.github#2725–#2729) consumes: of
///     those 77, exactly FOUR are required by production code outside this module. `Program.fs` needs
///     `run`, `whoami`, `followupAudit` and `predicate`, and nothing else in `src/` names this module at
///     all. The other 73 are held open by the TEST PROJECT ALONE.
///
/// So this module's real external contract is four functions wide. Everything else exported below is a
/// testability seam, and each one marks a place where a unit of behaviour is welded to a file that
/// cannot be constructed in a test — which is the same force that produced the three mutable cells. The
/// consumer-by-consumer table is in the pull request that added this file.
///
/// WHAT THIS FILE DELIBERATELY DID NOT DO. `generatedPathCollector`, `completeDelivery` and
/// `followupAuditContextOverride` are `let mutable private` forward declarations back-patched thousands
/// of lines later, and `completeDelivery`'s initial value is a reachable `failwith`. They are the
/// EVIDENCE motivating the extraction, not this file's work to remove: across two files each is an
/// ordinary dependency inversion, and inside one module there is no way to express it except a mutable
/// cell. Removing them here would turn a compiler-checked change into a design change. They stay, and
/// they stay private.
module Client =

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
    val badTouchSetDetail:
      (string -> FS.GG.Coord.TouchSet.Usability -> string option)

    /// `lint`'s BLOCKED-NO-REASON sentence for one row, or `None` when the row is coherent. An ALIAS of
    /// `LintApplication.blockedNoReasonVerdict`; the rule itself is stated once, there.
    ///
    /// `Some detail` for exactly one shape: an OPEN row whose column is `Blocked`, whose `Blocked by` field is
    /// empty or whitespace, and whose body carries no `Blocked on: human/...` sentinel. Such a row is parked
    /// with no recoverable reason — nothing on it says what would unblock it — which is the `.github#2079`
    /// shape `release --status Blocked` now refuses at the write. This is the READ half of that pair: it finds
    /// the rows parked before the refusal existed, or by a route that never passes through it.
    ///
    /// Re-exported at this level rather than reached through `LintApplication` so that `lint` and the tests
    /// bind the SAME function. That is #945's rule for every lint rule in this module: the VERDICT is computed
    /// once and arrives as an argument; only the WORDS are lint's.
    val blockedNoReasonVerdict:
      (FS.GG.Coord.Types.IssueState ->
         FS.GG.Coord.Types.BoardStatus -> string -> string -> string option)

    /// `lint`'s HUMAN-PARK-RESOLVED sentence for one row, or `None`. An ALIAS of
    /// `LintApplication.humanParkResolvedVerdict`.
    ///
    /// `Some detail` when a row is Open and `Blocked`, its body declares `Blocked on: human/decision` or
    /// `Blocked on: human/action`, and its machine blocker list is NON-EMPTY and every member of it is
    /// resolved. It PROMISES not to unpark: the sentence it returns says to keep the row `Blocked` until the
    /// human step happens. A resolved machine edge is not evidence a person decided, and an automatic
    /// promotion on that evidence hands a worker the one item whose whole point is that nobody has chosen yet.
    ///
    /// An EMPTY blocker list is `None`, deliberately — a human park that never had a machine edge was never
    /// waiting on one, so there is nothing here to report as newly resolved. The verdict exists so that a
    /// reader (and `reconcile`'s BLOCKER-CLEARED chore) can tell this state apart from a row that is genuinely
    /// ready to move.
    val humanParkResolvedVerdict:
      (FS.GG.Coord.Types.IssueState ->
         FS.GG.Coord.Types.BoardStatus ->
         FS.GG.Coord.Types.Blocker list -> string -> string option)

    /// One sentence per row that sits on a `Blocked by` CYCLE — a ring of rows each blocked by the next, which
    /// can never become startable by itself. An ALIAS of `LintApplication.blockerCycleVerdicts`.
    ///
    /// It takes the WHOLE graph rather than one row because a cycle is not a property any single row carries:
    /// the finding is reachable only from the edge set, which is why no per-row lint rule can ever produce it.
    /// Each returned pair names the full ring in its detail, so the reader sees which edge they could break
    /// rather than only that they are stuck.
    ///
    /// `[]` means no cycle, and it is an honest empty: a graph the caller could not read never reaches here.
    val blockerCycleVerdicts:
      ((FS.GG.Coord.Types.Ref * FS.GG.Coord.Types.Blocker list) list ->
         (FS.GG.Coord.Types.Ref * string) list)

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
    val blockedByBodyDivergence:
      owner: string ->
        repo: string -> fieldRaw: string -> body: string -> string list

    /// lint's CLASS verdict (`CLASS-INVALID` / `CLASS-UNSET` / nothing), on `badTouchSetDetail`'s terms:
    /// module-level so a test can drive every shape the grammar can produce (.github#1651).
    val classVerdict:
      (string -> string -> string -> (string * string) option)

    /// The out-of-vocabulary-`Class:` refusal `add` renders before it boards a row (.github#1651 AC1).
    val outOfVocabularyClass: (string -> string option)

    /// Existing same-tree rows that the new declaration strictly contains.  This is advisory at the
    /// filing boundary: the issue is already real, and refusing to board it would make the warning hide
    /// work from every later board view.  It instead names the lane-of-one while the filer can still
    /// narrow or sequence the pair (#1843).
    val filingLaneOfOne:
      candidate: FS.GG.Coord.Types.Ref ->
        paths: FS.GG.Coord.Types.TouchSet ->
        items: FS.GG.Coord.Types.Item list -> FS.GG.Coord.Types.Ref list

    /// Decode the current SDD analysis fact for the named work — the errors in one `sdd readiness` JSON
    /// document, or `[]`. The route boundary owns this one interpretation so a `workId` substitution and an
    /// unready analysis cannot be accepted on one command path while another merely checks that JSON was
    /// present. An unreadable document is an ERROR in the returned list, never a silent `[]`.
    val sddReadinessEvidenceErrors:
      workId: string -> raw: string -> string list

    /// Exposed for the command-boundary test: this is the sole filesystem-backed SDD proof surfaced by
    /// route reads and route recording, so the test can pin both the current and missing-work inversions.
    ///
    /// ADVISORY ONLY (#2298), and never fed back into a `DeliveryRoute.Verdict`. Static receipt validation
    /// proves that an SDD route says WHICH work package it governs; whether that package's files currently
    /// exist and are `implementationReady` is a SEPARATE fact. Requiring it to be true before the receipt
    /// records or the item schedules made `sdd-required` permanently unreachable for any item that does not
    /// already carry a package, because the only actor positioned to author that package — a CLAIMED
    /// WORKER, inside a worktree, via `fsgg-sdd` — could never get claimed to do so. So this is REPORTED
    /// (by `record` and `show`) and never refuses.
    val sddEvidenceErrors:
      receipt: FS.GG.Coord.DeliveryRoute.Receipt -> string list

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
    val boardBlockingCounts:
      (FS.GG.Coord.GitHub.Scan.Row list -> Map<FS.GG.Coord.Types.Ref,int>)

    /// Pure snapshot projection retained for offline diagnostics. Live board commands use
    /// `renderLiveDecision`, which always reads the mandatory route ledger immediately before scheduling.
    val renderDecision:
      opts: Options.Options ->
        rows: FS.GG.Coord.GitHub.Scan.Row list ->
        doc: string -> Result<FS.GG.Coord.Batch.BatchResult,int>

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
    val slotOccupancyOf:
      doc: string -> Result<FS.GG.Coord.Batch.SlotOccupancy,string>

    /// `batch` — every item schedulable in parallel right now; `next` uncapped, and the READ half of that pair.
    ///
    /// It makes NO chore offer and takes NO lock (#1535). That is the whole difference from `next`, and it is
    /// why this is the verb to call for the decision alone — including from a stale engine, which still permits
    /// it. Candidates are packed priority-greedily by a derived rank (blocking count, Class, Phase, age —
    /// #1598), so the highest-ranked schedulable item is always admitted.
    ///
    /// Exits 0 on a completed answer, INCLUDING the empty one. An IO failure keeps the read's own exit code
    /// through `fail`, so "nothing schedulable" and "could not read the board" are never the same number.
    val batch: ctx: Kernel.Context -> opts: Options.Options -> int

    /// One candidate's live board/claim/PR/review/delivery facts, projected into the shape
    /// `DriverEvents.classify` consumes (.github#2135). Named and pure over its inputs — no `ctx`, no
    /// IO — precisely so it is unit-testable without a live board scan
    /// (independent review round 1, finding 3: "the entire CLI execution path ... has zero
    /// test coverage"). `reviewByPr`/`mergedFactsByRef` are pre-computed by the caller from the SAME
    /// live reads `driver`'s existing planning path already performs.
    val candidateToItemFacts:
      reviewByPr: Map<int,FS.GG.Coord.Driver.ReviewChain> ->
        mergedFactsByRef: Map<string,
                              (int * bool *
                               FS.GG.Coord.Delivery.Obligation list)> ->
        now: int64 ->
        sourceSha: string ->
        candidate: Snapshot.Candidate -> FS.GG.Coord.DriverEvents.ItemFacts

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
    val readEventsCursor:
      path: string option -> Result<FS.GG.Coord.DriverEvents.Cursor,string>

    /// Persist the cursor ATOMICALLY (.github#2135 repair round 1, finding 2's second half). A bare
    /// `File.WriteAllText` truncates the target before writing the new bytes — a process killed
    /// mid-write leaves a PARTIAL file, which is precisely the corrupt input `readEventsCursor` above
    /// exists to refuse rather than silently treat as absent. Writing to a sibling temp file in the
    /// SAME directory, then renaming it over the target, means a crash leaves either the complete OLD
    /// file or nothing at that path — never a half-written one. `File.Move` with `overwrite: true` is
    /// a single filesystem rename on the common case (temp and target on the same volume), which is
    /// guaranteed by placing the temp file beside its target rather than under a system temp root.
    val writeEventsCursorAtomic:
      path: string -> cursor: FS.GG.Coord.DriverEvents.Cursor -> unit

    /// The `fsgg.coord.driver-events/1` JSON projection (.github#2135) — named so it is directly
    /// testable against a hand-built `DriverEvents.Projection` without a live board scan.
    val renderEventsJson:
      sourceSha: string ->
        projection: FS.GG.Coord.DriverEvents.Projection -> string

    /// Live inspection derives occupancy from the same board snapshot as `batch`, never caller input.
    val driver: ctx: Kernel.Context -> opts: Options.Options -> int

    /// The delivery receipt and `verify-paths` both exclude generated, CI-gated artifacts from the
    /// authored touch-set boundary.  The collector fails closed, so an unreadable generator can only
    /// make this false (and block landing); it cannot turn an undeclared authored file into a pass.
    val deliveryPathsVerified:
      touchSet: FS.GG.Coord.Types.TouchSet -> files: string list -> bool

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
    val offerBoardOf:
      rows: FS.GG.Coord.GitHub.Scan.Row list ->
        doc: string -> FS.GG.Coord.Chore.Board option

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
    val wholeBoard:
      ctx: Kernel.Context ->
        opts: Options.Options -> FS.GG.Coord.Chore.Board option

    /// `next` — the single next schedulable item: the ref alone on stdout, every reason on stderr.
    ///
    /// That split is the contract (.github#1562). The "nothing schedulable" headline and the per-item reasons
    /// are STDERR, so `ref="$(… next)"` reads an empty string when there is none instead of capturing a
    /// sentence. `batch --text` prints the same answer with all of it on stdout, for a human.
    ///
    /// AND IT WRITES (#1535). After printing the answer it offers a #733 chore, which POSTs a claim marker
    /// TAKING this repo's chore lock. A caller that wants the decision without the offer wants `batch` — that
    /// is the read, and the one a stale engine still permits. Exits 0 whether or not there was an item; an IO
    /// failure keeps the read's own code.
    val next: ctx: Kernel.Context -> opts: Options.Options -> int

    /// `ready` — the board as a RECONCILER sees it: always fresh, not-`Done` by default, `--status`/`--all` to
    /// widen past that.
    ///
    /// A TRUTH read, not a scheduling read, and the difference is deliberate: it SHOWS rows the scheduler will
    /// refuse, because a reconciler's question is "what is on the board" and a scheduler's is "what may I
    /// start". Defaults to JSON; `--text` gives the human-readable table. Exits 0 on a completed read; an IO
    /// failure keeps the read's own code.
    val ready: ctx: Kernel.Context -> opts: Options.Options -> int

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
    val boardUnreachable: error: FS.GG.Coord.GitHub.Errors.IoError -> bool

    /// `reconcile` — the mechanically safe board repairs derivable from a fresh scan. A DRY RUN unless
    /// `--apply`.
    ///
    /// `--apply` performs ONLY typed chore remedies. Judgement findings stay report-only in `lint`, because a
    /// repair that needs somebody to decide something is not mechanical and this verb has no way to ask.
    ///
    /// It distinguishes FOUR outcomes rather than two. A completed reconciliation is 0. A board that could not
    /// be RESOLVED AT ALL — a credential or visibility gap — is `ExitNoVerdict`, with a sentence saying so in
    /// as many words, precisely so it is never mistaken for "reached the board, nothing to reconcile"; that
    /// remedy is org-level (.github#2332) and not fixable from this repo's tree. `ExitError` is a run that
    /// REACHED the board and did not complete — a snapshot that would not parse, or an applied remedy that
    /// failed — which is a different fact from both of those and must not be collapsed into either. Any other
    /// IO failure keeps the read's own code.
    val reconcile: ctx: Kernel.Context -> opts: Options.Options -> int

    /// `who` — who holds what, right now: held, stale, or unclaimed.
    ///
    /// `--local` joins the claims to local git worktrees; `--all-repos` widens past the repo scope; `--json`
    /// gives the machine contract and anything else gives a human table.
    ///
    /// SCOPE IS PART OF THE ANSWER, INCLUDING THE EMPTY ANSWER (#1369 — a `[]` from the hub checkout was
    /// routinely read as the org-wide claim set), but WHERE it is said depends on the renderer, and the
    /// narrower rule is the true one. The human table always opens with a `scope:` line on stdout. Under
    /// `--json` the array wire contract is preserved instead: nothing is added to stdout, a non-empty row
    /// already names its own repository, and only for an EMPTY result does the explicit scope ride STDERR. So
    /// a JSON caller reading stdout alone gets no scope line — by design, and it is exactly why the empty case
    /// needs one at all.
    ///
    /// Exits 0 on a completed read; an IO failure keeps the read's own code.
    val who: ctx: Kernel.Context -> opts: Options.Options -> int

    /// The one resolved-Status boundary for a `Blocked` write.  Callers may arrive through an
    /// explicit park, a recorded claim restore, or reconciliation; none may write the column until
    /// this verifies that the resulting row carries either a machine edge or a human sentinel.
    ///
    /// AC1 (.github#2079): **a `Blocked` park is coherent ONLY if the row will end with a non-empty
    /// readable `Blocked by` field or a `Blocked on: human/...` sentinel.** A park is two independent
    /// writes — the `Status` column and the `Blocked by` field — and nothing else bound them at write
    /// time: `BLOCKED-NO-REASON` only reds the EMPTY-field case, so a stale-but-non-empty field (the
    /// `FS.GG.Templates#348` shape — an edge that landed as a body line instead) satisfied every existing
    /// check while naming refs that had all resolved, and `BLOCKER-CLEARED` promoted the row.
    ///
    /// Its callers order it BEFORE the lock drops — a refused write must not cost the caller their lock —
    /// and AFTER the same call's own `Blocked by` write, so `release --blocked-by` has already landed and
    /// the live read this makes sees it. A no-op for every OTHER requested status, including none: this is
    /// ADR-0045's park invariant, not a general field-write gate, and it never fires on a row the call is
    /// not about to park. `Error` carries `ExitError` in every arm, including an unreadable board, field or
    /// body — a fact it could not read is a refusal, never a pass. `set-field --batch` cannot use this
    /// as-is; that is what `requireCoherentParkIfBlockedForBatch` below exists for.
    val requireCoherentParkIfBlocked:
      ctx: Kernel.Context ->
        ref: FS.GG.Coord.Types.Ref ->
        requested: FS.GG.Coord.Types.BoardStatus option -> Result<unit,int>

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
    val reap: ctx: Kernel.Context -> opts: Options.Options -> int

    /// `budget` — what is left, and what is stuck: the GraphQL meter, the REST telemetry, and the deferral
    /// queue.
    ///
    /// `pendingBoardWrites` is the DEPTH OF THE DEFERRAL QUEUE — the writes `boardWrite` took on an
    /// exhausted budget and `flush` will replay (#862). It and the REST observations beside it come from
    /// LOCAL files, which is the point: the moment you most need to ask "did my stamp land?" is the moment
    /// you have no budget to ask with.
    ///
    /// THE COMMAND ITSELF IS NOT OFFLINE, and must not be described as though it were: it opens by reading
    /// GitHub's own `/rate_limit`, so it needs a live transport and a token, and a failure there keeps that
    /// read's own code rather than reporting an unknown budget as a healthy one. What it reports about REST
    /// is deliberately NOT that figure — `/rate_limit`'s `core` count disagrees with the counter real
    /// requests are billed against on this account, and a secondary limit never appears in it at all.
    ///
    /// A queue we could not READ is `None`, never `0`. They are opposite answers — "nothing is waiting" vs
    /// "I cannot tell you what is waiting" — and rendering the second as the first is how a worker concludes
    /// their `done --flip` was dropped when it is sitting in the queue, or the reverse (#266).
    val budget: ctx: Kernel.Context -> opts: Options.Options -> int

    /// `claim` — take the lock on ONE named item, without the scheduler's overlap-avoidance in front of it.
    ///
    /// Because it reaches items `take` would never offer, it runs the same #353 collision scan that
    /// `widen`/`overlap --active` use and WARNS on a live overlap while still claiming (#2459);
    /// `--refuse-overlap` turns that warning into a refusal.
    ///
    /// `--force` STEALS through an interruption-safe transition: post a replacement capability, delete the
    /// prior live marker, then apply the existing comment-order election (#1620/#2772). Ambiguous cleanup is
    /// classified only after a complete marker census, and retry reuses a retained replacement. It also lifts
    /// #516, but does not override twin or unparseable markers.
    ///
    /// Exit contract: 0 on a converged claim; `ExitRed` for every LOSS that is not a race the caller should
    /// re-run — held by another worker, a twin marker, an impersonation refusal, an undecided CAS, a marker
    /// held by nobody; `ExitContended` when the caller genuinely lost a race; `ExitError` for a usage fault. A
    /// lost race never asks the caller to re-issue anything: `take` advances through its own ranked candidates
    /// (.github#2683).
    val claim: ctx: Kernel.Context -> opts: Options.Options -> int

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
    val adopt: ctx: Kernel.Context -> opts: Options.Options -> int

    /// `landable` — is this OPEN PR finished work? One verdict word on stdout, the decision in the exit code.
    ///
    /// The #697/#720 gate as a query (#724). `--wait` polls until the verdict SETTLES: it never believes an
    /// early green (it waits for the run set to STOP GROWING) and keeps waiting while zero runs have
    /// registered. `--require NAME` (repeatable) demands that a named check have REPORTED — for the one branch
    /// protection does not require but which decides the PR, and which absent reads like a passing one (#606).
    /// `--sha SHA` names the head you MEAN to gate, for a caller that just force-pushed and whose PR object
    /// lags. Neither of those can turn a verdict green; both can hold it pending (#737).
    ///
    /// This is the widest exit vocabulary in the module and every arm is load-bearing: 0 green, `ExitPending`
    /// (7) not settled and worth retrying, `ExitRed` (3) red or conflicted — stop, do not wait,
    /// `ExitNoVerdict` (4) unknown, fail-closed, `ExitNotOpen` (10) the PR is merged or closed so there is
    /// nothing left to gate (#1680), and `ExitError` (1) for a usage fault.
    val landable: ctx: Kernel.Context -> opts: Options.Options -> int

    /// The authoritative inventory of every `Status=Blocked` writer (#2109).  Every writer, including
    /// a recorded restore, passes the same resolved-Status gate before emitting its mutation — the gate
    /// being `requireCoherentParkIfBlocked` above, whose AC1 invariant this list exists to grade the code
    /// against.
    type BlockedStatusWriter =
        | CannotWriteBlocked of string
        | DeliberatePark of string
        | GuardedRestore of string

    /// The authoritative inventory of every code path in this engine that can write `Status=Blocked`, tagged
    /// with which of the three kinds it is (#2109).
    ///
    /// IT IS EXPORTED SO A TEST CAN GRADE THE LIST AGAINST THE CODE. The invariant it exists to protect is that
    /// every writer — including a RECORDED RESTORE, which is the one people forget, because a `release` or
    /// `reap` putting back a column it did not choose is still a writer — passes the same resolved-Status gate
    /// before emitting its mutation. A list that silently fell behind the code would assert that invariant
    /// about a set that no longer matches the writers, which is the shape of a check that cannot fail (#266).
    val blockedStatusWriterCoverage: BlockedStatusWriter list

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
    val requireCoherentParkIfBlockedForBatch:
      ctx: Kernel.Context ->
        ref: FS.GG.Coord.Types.Ref ->
        requested: FS.GG.Coord.Types.BoardStatus option ->
        pendingBlockedBy: FS.GG.Coord.GitHub.Board.FieldWrite option ->
        Result<unit,int>

    /// `release` — drop the lock, restoring the column it overwrote.
    ///
    /// `--status S` lands the row in S instead of back on `Ready` (#867: name the column you mean). `--status
    /// Blocked` REFUSES BEFORE THE LOCK DROPS unless the row will end with a non-empty `Blocked by` field or a
    /// `Blocked on: human/...` sentinel (.github#2079) — the refusal is ordered ahead of the drop so a failed
    /// park never costs the caller its claim. `--blocked-by REF` writes that field in this same call, so a
    /// coherent park is one command rather than two.
    ///
    /// Exit contract, and it has FOUR arms rather than two. 0 once the lock is dropped. `ExitError` on a usage
    /// fault, on a caller that does not hold the item, and on a refused park. `ExitRed` on a BROKEN IDENTITY —
    /// a twin marker (#419) or an impersonation refusal (#1646) — which is a stop and never a retry, and which
    /// matters most on this verb precisely because `release` DELETES the marker: adopting a twin's would drop a
    /// lock somebody is working behind. And a read it could not make keeps THAT read's own code (75 on an
    /// exhausted budget), never a green and never a generic 1.
    ///
    /// A non-fatal board write that could not land is SURFACED as a stderr note rather than swallowed (#1151)
    /// and does not change the code: the lock is genuinely released whatever the column says.
    val release: ctx: Kernel.Context -> opts: Options.Options -> int

    /// `heartbeat` — renew the lease on a held item.
    ///
    /// Runs UNATTENDED in the recipes, which is why its refusals matter more than its successes: a refusal here
    /// is not seen by anyone until the lease lapses 120 minutes later and a second worker is handed the same
    /// item (#548). So the refusals are enumerated in full below rather than summarised — a verb whose whole
    /// argument is that its refusals are the part nobody watches must not describe them loosely.
    ///
    /// Exits 0 on a renewed lease. `ExitError` on a usage fault and on a caller that does not hold the item —
    /// which splits into two remedies the command distinguishes on stderr: somebody ELSE holds it (including
    /// after a `claim --force` steal, #1620, which this is the arm a displaced worker lands in) or the lease
    /// EXPIRED and must be re-claimed. `ExitRed` on a BROKEN IDENTITY, a stop rather than a retry: an
    /// impersonation refusal (#1646 — `heartbeat` is the quiet one, since renewing another worker's lease keeps
    /// their item alive under our control while they are told nothing) or a twin marker (#419). And a read it
    /// could not make keeps THAT read's own code — 75 on an exhausted budget — never a green and never a
    /// generic 1.
    val heartbeat: ctx: Kernel.Context -> opts: Options.Options -> int

    /// The claim argv derived from a selected scheduler item.  The scheduler has already resolved this
    /// identity; this boundary must preserve it rather than reinterpret a display ref in the board owner's
    /// context (#2155).
    val claimArgsForSelected: item: FS.GG.Coord.Types.Item -> string list

    /// `take` — schedule AND claim the next item, in one step.
    ///
    /// READY ONLY: a `Backlog` row is passed over AT THE COLUMN unless `--include-backlog` asks for it (#636).
    /// This is the dearest verb in the module, because it spends REST on per-row claim markers rather than on
    /// the Projects v2 read, so its cost tracks the ROWS IT EXAMINES and is not fixed.
    ///
    /// Its three codes are the whole reason #585 exists — a worker loop (`take && work_it`) must never proceed
    /// on nothing. 0 means an item was claimed and only that. `ExitNone` (5) means it looked and there was
    /// nothing startable. `ExitContended` (6) means it lost every race and holds nothing, which is the one of
    /// the three a caller may safely retry. A read failure keeps its own code and is never any of these.
    val take: ctx: Kernel.Context -> opts: Options.Options -> int

    /// `set-field` — write one board field, or, with `--batch`, N fields in ONE aliased mutation (#448). An
    /// empty value clears the field.
    ///
    /// `--batch` promises ALL-OR-NOTHING: the whole document validates before any alias is emitted, so a pair
    /// that fails validation costs nothing. That promise is why it cannot borrow the single-field door's trick
    /// of writing first and reading back, and why a `Status=Blocked` paired with a `Blocked by` CLEAR in the
    /// same call is refused on the body sentinel instead (#2098) — the one fact that stays true regardless of
    /// what the live field currently holds.
    ///
    /// Exits 0 when the write landed, `ExitError` on a usage fault or a refused incoherent write.
    val setField: ctx: Kernel.Context -> opts: Options.Options -> int

    /// `widen` — add paths to a HELD item's touch-set. A UNION, and idempotent.
    ///
    /// It runs the #353 collision scan and reports its verdict. AN OVERLAP VERDICT IS A HARD STOP, and reading
    /// it as anything softer is a measured hazard: a `widen` that refuses with OVERLAP still writes the
    /// requested paths into the item's declaration, so a caller's declared `Paths:` can be WIDER than what it
    /// legitimately holds. Declaration breadth is therefore never permission.
    ///
    /// Widening does not stale the item's delivery-route receipt. That receipt's `subjectRevision` is checked
    /// against a route RECORD's digest, and `StructuredDecision.routeDigest` hashes that record's own fields —
    /// including the touch-set SNAPSHOT the record carries. It never re-reads the live issue body, so no edit
    /// to the body moves it.
    ///
    /// Exit codes come from `updateTouchSet`, which this and `set-paths` share.
    val widen: ctx: Kernel.Context -> opts: Options.Options -> int

    /// `set-paths` — replace a HELD item's touch-set explicitly. The only door that NARROWS one.
    ///
    /// `widen`'s sibling through the same `updateTouchSet` implementation, differing in exactly one argument:
    /// this one replaces where `widen` unions. `--json` returns the resulting declaration and the #353 verdict
    /// as one object rather than as prose split across two streams (#1517).
    val setPaths: ctx: Kernel.Context -> opts: Options.Options -> int

    /// A durable wait edge bound to both current claim generations and the exact shared reservations.
    type OverlapWaitReceipt =
        { Waiter: FS.GG.Coord.Types.Ref
          WaiterGeneration: string
          Predecessor: FS.GG.Coord.Types.Ref
          PredecessorGeneration: string
          SharedTokens: string list
          Host: string
          Digest: string }

    type OverlapClaimFact =
        { Item: FS.GG.Coord.Types.Ref
          Generation: string
          Live: bool }

    type OverlapRelation =
        { Left: FS.GG.Coord.Types.Ref
          Right: FS.GG.Coord.Types.Ref
          SharedTokens: string list }

    type MutualOverlapSnapshot =
        { Readable: bool
          Claims: OverlapClaimFact list
          Relations: OverlapRelation list
          Waits: OverlapWaitReceipt list
          DurableDependencies: (FS.GG.Coord.Types.Ref * FS.GG.Coord.Types.Ref) list
          RelatedRoomCycleDigests: string list }

    type MutualOverlapCycle =
        { First: FS.GG.Coord.Types.Ref
          Second: FS.GG.Coord.Types.Ref
          FirstGeneration: string
          SecondGeneration: string
          SharedTokens: string list
          Digest: string }

    type MutualOverlapVerdict =
        | NoMutualOverlapCycle
        | MutualOverlapCycle of MutualOverlapCycle
        | MutualOverlapRefused of reason: string

    type OverlapPrecedenceReceipt =
        { CycleDigest: string
          Revision: int
          PreviousDigest: string option
          Winner: FS.GG.Coord.Types.Ref
          Loser: FS.GG.Coord.Types.Ref
          Host: string
          Reason: string option
          Digest: string }

    type LoserResumeFacts =
        { WinnerLanded: bool
          LoserClaimGenerationCurrent: bool
          FetchedWinnerBase: bool
          RebasedHead: bool
          OverlapClear: bool
          ExplicitlyRewidened: bool
          ReviewRequired: bool
          ExactHeadReviewed: bool }

    type BoardOrchestratorLease =
        { Board: string
          HolderRepo: string
          Holder: string
          Generation: int64
          ExpiresAtUnix: int64
          CommentId: int64
          Digest: string }

    type BoardOrchestratorRequest =
        { Board: string
          RequestingRepo: string
          RequestKey: string
          CoordinationRef: FS.GG.Coord.Types.Ref
          LeaseGeneration: int64
          CommentId: int64
          Digest: string }

    type BoardOrchestratorSnapshot =
        { Readable: bool
          NowUnix: int64
          Board: string
          Leases: BoardOrchestratorLease list
          Requests: BoardOrchestratorRequest list }

    type BoardOrchestratorDecision =
        | RouteRequestTo of BoardOrchestratorLease
        | RunBoardOrchestrator of lease: BoardOrchestratorLease * highestPriority: BoardOrchestratorRequest option
        | AcquireBoardOrchestrator of generation: int64
        | BoardOrchestratorRefused of reason: string

    val waitReceiptDigest: receipt: OverlapWaitReceipt -> string
    val detectMutualOverlap: snapshot: MutualOverlapSnapshot -> MutualOverlapVerdict
    val precedenceReceiptDigest: receipt: OverlapPrecedenceReceipt -> string
    val validateOverlapPrecedence: cycle: MutualOverlapCycle -> receipts: OverlapPrecedenceReceipt list -> Result<OverlapPrecedenceReceipt, string>
    val validateLoserResume: facts: LoserResumeFacts -> string list
    val boardOrchestratorLeaseDigest: lease: BoardOrchestratorLease -> string
    val boardOrchestratorRequestDigest: request: BoardOrchestratorRequest -> string
    val decideBoardOrchestrator: requesterRepo: string -> holder: string -> snapshot: BoardOrchestratorSnapshot -> BoardOrchestratorDecision

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
    val overlapCmd: ctx: Kernel.Context -> opts: Options.Options -> int

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
    val inbox: ctx: Kernel.Context -> opts: Options.Options -> int

    /// `done` — the done stamp, and the gate in front of it.
    ///
    /// `--flip` moves the row to `Done`. The stamp is not a formality: it audits residual promises before it
    /// lands, so an item with an undischarged follow-up cannot be stamped green and forgotten.
    ///
    /// Four outcomes, and the middle two are the point. 0 is a green stamp. `ExitRed` (3) is a gate that ran
    /// and refused — the work is not done. `ExitNoVerdict` (4) is a gate that could not read a fact it needed,
    /// which must never be reported as either of the other two. `ExitError` (1) is a usage fault.
    val doneCmd: ctx: Kernel.Context -> opts: Options.Options -> int

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
    val verifyPaths: ctx: Kernel.Context -> opts: Options.Options -> int

    /// `whoami` — resolve, explain, or MINT this shell's worker identity. The only sanctioned source of a
    /// worker id.
    ///
    /// `--mint` prints `export FSGG_WORKER=<id>` on stdout and the instruction to eval it on stderr, so
    /// `eval "$(… whoami --mint)"` works and a plain call still shows a human what happened. Without `--mint`
    /// it resolves the id the caller already has and explains where it came from — which is the half that
    /// matters, because an id DERIVED from the agent harness's session is shared by every subagent of that
    /// session and cannot separate one worker from another (#419, .github#2684).
    ///
    /// There is no second way to obtain an id, and improvising one is the defect this command exists to
    /// prevent. Exits 0 with the id, `ExitError` when no identity could be resolved.
    ///
    /// PURE — it takes no `Context` and touches no board, which is why `Program.fs` reaches it directly instead
    /// of through `run`.
    val whoami: opts: Options.Options -> int

    /// `predicate` — the oracle as a query: one verdict word on stdout, the decision in the exit code
    /// (0 agrees / 3 contradicts / 4 unknown, the `landable` shape). `--json` emits the structured
    /// verdict the filing-time workflow reads to build its auto-comment (`.ownerValue`, `.note`).
    val predicate: opts: Options.Options -> int

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
            | HeldByAnother of holder: FS.GG.Coord.Types.WorkerId

            /// THE MARKER CARRIES OUR WORKER ID UNDER A DIFFERENT SESSION (#419). An id two executors share
            /// is not a lock, and adopting their live grant as our own is the exact defect `.github#1858`
            /// measured — two executors, one id, one claim, both acting.
            | Twin of theirs: FS.GG.Coord.Types.SessionId

            /// WE ASKED TO ACT AS A WORKER WE ARE NOT (#1646).
            | Impersonates of
              derived: FS.GG.Coord.Types.WorkerId *
              named: FS.GG.Coord.Types.WorkerId

            /// WE DO NOT HOLD THIS RECEIVER'S LOCK — the RELEASE path's arm, and it has no acquire-path
            /// counterpart because acquiring is how you come to hold one. Distinct from `HeldByAnother`,
            /// which says somebody else's live marker is there: this arm also covers an issue carrying no
            /// live marker at all, so there is nothing to drop. Keeping them apart is what lets a caller
            /// tell "my grant already lapsed" from "another executor took the receiver while I was
            /// dispatching" — two facts that need opposite responses.
            | NotHeld of owner: string * receiver: string

            /// WE COULD NOT TELL. A failed read, an unparseable marker, or a lost re-read. Never a yes.
            | Undetermined of detail: string

        /// One human-readable line per refusal, for a caller that must report why it did not dispatch.
        /// Carries no judgement and decides nothing.
        val describe: refusal: Refusal -> string

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
        val acquire:
          transport: FS.GG.Coord.GitHub.Transport.IGitHubTransport ->
            worker: FS.GG.Coord.Types.WorkerId ->
            self: FS.GG.Coord.GitHub.Writes.SelfIdentity ->
            session: FS.GG.Coord.Types.SessionId option ->
            extra: FS.GG.Coord.Types.Ref list ->
            owner: string ->
            receiver: string ->
            Result<FS.GG.Coord.GitHub.Writes.Held,Refusal>

        /// TEN MINUTES, matching the chore lock's, and for the same argument. This bounds how long a DEAD
        /// executor stalls one receiver's dispatch queue — not how long a live one may take, because a live
        /// holder heartbeats. A dispatch is a handful of API calls; a lease long enough to cover a hung
        /// process is a lease that makes every other executor wait out a corpse.
        val LeaseMinutes: int

        /// RE-OBTAIN the capability for a grant this PROCESS did not take — `Writes.verifyHeld` on the
        /// receiver's lock ref, with `acquire`'s refusal vocabulary.
        ///
        /// Acquire and release are NECESSARILY different processes, by design rather than by awkwardness:
        /// the grant is verified by the broker, the broker is a queued CI job, and design §4.1 requires the
        /// lock to be taken immediately before the dispatch and released after it. A `Held` cannot survive
        /// that gap — it is an in-process value with no public constructor, deliberately — so the release
        /// path re-establishes it from GitHub, through the same door `release <ref>` uses for an ordinary
        /// item claim. That door applies `claim`'s own impersonation and twin predicates (#1031, #1646);
        /// selecting the marker by lowest id and deleting it would delete whichever marker happened to be
        /// first, which under a shared worker id is #550.
        val held:
          transport: FS.GG.Coord.GitHub.Transport.IGitHubTransport ->
            worker: FS.GG.Coord.Types.WorkerId ->
            self: FS.GG.Coord.GitHub.Writes.SelfIdentity ->
            session: FS.GG.Coord.Types.SessionId option ->
            extra: FS.GG.Coord.Types.Ref list ->
            owner: string ->
            receiver: string ->
            Result<FS.GG.Coord.GitHub.Writes.Held,Refusal>

        /// RELEASE the operation lock. Takes the capability, exactly as every other release does, so a
        /// marker nobody holds cannot be dropped by naming it.
        ///
        /// IT WAS UNREACHABLE UNTIL NOW, AND NOT ONLY UNCALLED: `d1632c4e` defined it in `Client.fs` and
        /// left it out of this signature file, so F# made it private and no caller — production or test —
        /// could have reached it even by trying. Exporting it is part of giving the lock a release half at
        /// all.
        val release:
          transport: FS.GG.Coord.GitHub.Transport.IGitHubTransport ->
            held: FS.GG.Coord.GitHub.Writes.Held ->
            FS.GG.Coord.GitHub.Errors.IoResult<unit>

        /// The per-deployment op-lock roster, read from `FSGG_COORD_OP_LOCKS` — what makes `opLockRef`'s
        /// `extra` parameter reachable from production at all. A SEPARATE variable from
        /// `FSGG_COORD_CHORE_LOCKS`: the two name different subjects, and a tenant repointing one must not
        /// silently repoint the other.
        val roster: unit -> FS.GG.Coord.Types.Ref list

    /// `op-lock acquire <item> <generation> <receiver> <op>` — take the dispatch grant and print the
    /// authorization tuple `fsgg-dispatch-broker.yml` demands.
    ///
    /// THE PRODUCTION CALLER `.github#2312` LANDED WITHOUT. `OpLock.acquire` and `Options.opLockRef` landed
    /// correct and complete and were reachable from nothing but their own unit tests, so no `fsgg:claim`
    /// marker could ever appear on an op-lock issue and the broker's step-5 refusal was unreachable by
    /// construction. It prints the `opkey` beside the `grant` because the broker recomputes the key and
    /// refuses a mismatch, and a SHA-256 no operator can compute by hand makes a grant-only verb unusable.
    ///
    /// Exit `6` (contended) when another executor holds the receiver — the fence WORKING, and a back-off is
    /// the remedy. Exit `1` for every other refusal, all of which need a fact changed before a retry can
    /// differ.
    val opLockAcquire: ctx: Kernel.Context -> opts: Options.Options -> int

    /// `op-lock release <receiver>` — drop that grant after the dispatch it fenced. Refuses unless we are
    /// the live winner.
    val opLockRelease: ctx: Kernel.Context -> opts: Options.Options -> int

    /// Run `f` with a supplied `Context` installed as `followupAudit`'s ambient context, then restore whatever
    /// was there before — INCLUDING when `f` throws.
    ///
    /// A TEST SEAM, and it is named one so nobody has to guess. `followupAudit` builds its own context from the
    /// environment, which a unit test cannot do; this lets the application fixture hand it the same typed
    /// context every other handler receives as an argument. The override is scoped and restored on both exits,
    /// and production never installs one.
    ///
    /// It is exported because the alternative is worse, not because it is good: the honest fix is
    /// `followupAudit` taking its context as a parameter like every other command here, and that is a
    /// dependency inversion this file cannot express while it is one module. Read this binding as evidence for
    /// the extraction programme (.github#2725 onward), not as a pattern to copy.
    val withFollowupAuditContextForTest:
      ctx: Kernel.Context -> f: (unit -> 'a) -> 'a

    /// `followup audit` — the read-only reconciliation PREVIEW over the local follow-up queues.
    ///
    /// INTENTIONALLY READ-ONLY, and its evidence rule is the reason it can be trusted: a queue's mtime is a
    /// CANDIDATE SELECTOR, never evidence that its worker died. Each queued issue is re-read from GitHub, and
    /// its marker scan is required to be COMPLETE before this says the item has no live claim. An incomplete
    /// scan is not an absence of claims (#1949).
    ///
    /// Exits 0 when the audit completed with nothing to report, `ExitRed` when it found unreadable queues or
    /// residual promises. Like `whoami`, it is reached directly from `Program.fs` — it builds its own context
    /// rather than receiving one, which is what `withFollowupAuditContextForTest` exists to work around.
    val followupAudit: opts: Options.Options -> int

    /// `board` — the bootstrapped Coordination board as JSON: its number, title, owner and field map.
    ///
    /// The one read that resolves the board itself, and the operation every other IO command implicitly depends
    /// on: a token that cannot perform THIS cannot perform any of them, which makes it the diagnostic to reach
    /// for when a board-scoped verb reports a gap. Exits 0 with the JSON; an IO failure keeps the read's own
    /// code through `fail`.
    val boardCmd: ctx: Kernel.Context -> int

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
    val bodyEditsCmd: ctx: Kernel.Context -> opts: Options.Options -> int

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
    val addCmd: ctx: Kernel.Context -> opts: Options.Options -> int

    /// An alias of `LintApplication.EpicFinding` — one epic-rollup finding as a code, a severity and a detail.
    ///
    /// Exported here only because `epicVerdict` is: a caller that can obtain the findings must be able to name
    /// their type.
    type EpicFinding = LintApplication.EpicFinding

    /// `lint`'s whole EPIC-ROLLUP verdict for one row: every finding its sub-issue graph and unlinked-child
    /// list support. An ALIAS of `LintApplication.epicVerdict`.
    ///
    /// Its findings are NOTES by default rather than errors, and that is a decision rather than an oversight:
    /// `EPIC-ROLLUP-READY` reports a state only a human can adjudicate — it cannot tell a discharged epic from
    /// a partial fix — and reddening a gate on a question nobody has been asked yet teaches the one lesson #698
    /// names, that the gate is noise and you merge anyway. `--strict` is for the caller who wants to be stopped
    /// by one.
    val epicVerdict:
      (FS.GG.Coord.Types.IssueState ->
         FS.GG.Coord.Types.BoardStatus ->
         string ->
         FS.GG.Coord.GitHub.Reads.SubIssueSet ->
         string list -> LintApplication.EpicFinding list)

    /// `lint` — the judgement half of the board's health read, and the half `reconcile --apply` deliberately
    /// will not touch.
    ///
    /// Every rule here reports; none repairs. The split is the point: a finding that needs somebody to decide
    /// something cannot be a mechanical remedy, and moving it into `reconcile` would be a repair applied
    /// without the decision. Notes are NOT fatal by default (see `epicVerdict`); `--strict` makes them so.
    ///
    /// Exits 0 when nothing fatal was found and `ExitError` when something was. It has no `ExitRed` arm: the
    /// distinction lint draws is between a run that produced fatal findings and one that did not, not between
    /// a subject that passed a gate and one that failed it.
    val lint: ctx: Kernel.Context -> opts: Options.Options -> int

    /// #2134's receipt-first intake transaction. The receipt is persisted immediately after the only
    /// REST create, so a retry repairs the same issue's projection rather than issuing a second POST.
    /// Execute the live receipt-first intake transaction.  This is public so the transaction can be
    /// driven over a recording transport: the tests must count the issue-create POST, not infer it
    /// from a later board result.
    val intakeCmd: ctx: Kernel.Context -> opts: Options.Options -> int

    /// Not `private`: the command-boundary test (`DeliveryRouteCliTests`) drives `record`/`show` directly
    /// against a scripted transport, the same way `Client.claim` already is by `ForceStealTests`.
    val deliveryRouteCmd: ctx: Kernel.Context -> opts: Options.Options -> int

    /// THE COMPOSITION EDGE: build the one `Context` for this process, resolve the repo scope, and dispatch to
    /// the IO command `opts` names.
    ///
    /// This is `Program.fs`'s single door into every board-touching verb; the only commands it reaches directly
    /// are the three that take no `Context` at all (`whoami`, `followupAudit`, `predicate`). Two things happen here that no command may do for itself. The repo
    /// DEFAULT is resolved from what the caller actually passed, before the #480 git-remote rewrite, so a
    /// non-FS-GG remote can never be laundered into an "explicit --repo" and defeat the owner check that keeps
    /// a bare `<n>` a hard error outside the org (#548/#962). And `Context.DefaultRepo` is populated once, here,
    /// so accepting a bare `<n>` reached every `parseRef` call site through a single edit rather than through
    /// one edit each — twenty-one such lines at the time of writing, carrying twenty-three invocations, two
    /// lines parsing a pair. The COUNT drifts with the file and is quoted as a measurement, not a contract:
    /// `grep -c 'parseRef ctx' src/FS.GG.Coord.Cli/Client.fs` re-derives it. The PROPERTY — one edit, not N —
    /// is what this clause is about.
    ///
    /// `take` with no resolvable repo is a hard ERROR rather than a quiet fall-back to the whole org: it ACTS,
    /// claiming an item and printing a worktree command against THIS checkout's origin, and an undetectable
    /// scope once handed a `.github` worker another repo's item.
    ///
    /// Returns the process exit code. A PURE command routed here by mistake is a `failwith`, not a verdict —
    /// this door is for IO commands and the mistake is a defect in the caller, not a condition to report.
    val run:
      boardOpsHandlers: Map<Options.Command, BoardOps.HandlerRegistration.Handler> ->
      opts: Options.Options -> int
