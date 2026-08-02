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

    [<Literal>]
    let ExitGreen = 0

    [<Literal>]
    let ExitError = 1

    [<Literal>]
    let ExitRed = 3

    [<Literal>]
    let ExitNoVerdict = 4

    // #585 — `take`'s exit code must tell "I claimed you an item" (0) apart from the ways it can claim
    // NOTHING, so a worker loop (`take && work_it`) never proceeds on nothing. EX_NONE (5) is "looked,
    // nothing startable" (empty or all-blocked); EX_CONTENDED (6) is "lost every race — the board is
    // contended, back off and retry"; a read failure keeps its own non-zero (`fail`, never EX_NONE, so
    // "could not look" ≠ "empty" — #266); EX_RATE (75) is the budget. The values dodge the engine's
    // reserved codes (1 error, 2 defect, 3 red, 4 no-verdict). This reverses #480's "the empty queue
    // exits cleanly (0)", by decision on #585.
    [<Literal>]
    let ExitNone = 5

    [<Literal>]
    let ExitContended = 6

    // #720/#724 — `landable`'s exit code is a POLL-LOOP CONTRACT: a recipe reads it to tell "keep waiting"
    // from "stop" WITHOUT parsing the verdict word (/pnext-item §5, #724). PENDING is the ONE verdict worth
    // retrying, so it gets its OWN code, distinct from every "stop" outcome — a red/conflicted verdict is the
    // engine's ExitRed (do NOT wait), and an unknown is ExitNoVerdict (fail-closed, never a retry). Its value
    // dodges the reserved codes (0 green, 1 error, 2 defect, 3 red, 4 no-verdict, 5/6 take, 75 EX_RATE).
    // Disposed on the record (ADR-0040 §5): bash's `landable` numbers the poll loop 0/3/1 (green/pending/red),
    // where the engine keeps 3 == red across every verdict command (`done`/`decide`/`adopt`) and gives pending
    // its own 7 — the LITERALS differ, the PROPERTY does not (green is 0; pending is a distinct, retryable,
    // non-zero code; red is a distinct do-not-wait code). #724's `--wait` recipe is rewritten against these.
    [<Literal>]
    let ExitPending = 7

    /// 10 — the PR is NOT OPEN: merged, or closed without merging (#1680). A VERDICT, and a terminal one.
    ///
    /// It exists because the four codes above have no word for "there is nothing left to gate", so a merged
    /// PR was answered with `ExitPending` — the one code the contract defines as worth retrying — and
    /// `--wait` spent its whole 600s budget on a fact settled before it started. Its own number rather than
    /// a share of `ExitRed`: both mean stop, but 3's documented remedy is "a red check is a finding", and
    /// the caller that meets a merged PR is usually a successor recovering an item whose worker died
    /// between merge and stamp — who must be told to STAMP, not to investigate a failure.
    [<Literal>]
    let ExitNotOpen = 10

    /// Board status → its name, qualified against BoardStatus (bare `Ready` would resolve to the
    /// `Command.Ready` opened below). One place, so every render agrees.
    let private eprint (s: string) = Console.Error.WriteLine(s: string)

    /// An IO failure → its exit code and a printed reason. `RateLimited` becomes EX_RATE (75), the back-off
    /// signal; a caller that saw a generic 1 would treat a temporary condition as permanent.
    let private failWith (render: Options.Render) (e: Errors.IoError) : int =
        let message = Errors.explain e

        match render with
        | Json -> eprint (Render.renderFailureJson (Errors.exitCode e) message (Errors.rateLimitKind e))
        | Text -> eprint $"fsgg-coord-engine: %s{message}"

        Errors.exitCode e

    let private fail (e: Errors.IoError) : int = failWith Text e

    /// #1151: a NON-fatal board write's outcome, turned into the stderr note it warrants — SURFACING what
    /// `|> ignore` used to swallow. A `Written` is silent; the other three each say what did NOT land and
    /// how to finish it. `Deferred` is the one that bites: an exhausted budget QUEUES the write and NOTHING
    /// replays it on its own (#510/#878), so a silent defer is a column that never lands under a command
    /// that reported green. This is the sibling `release` handler's rule (`unclaimColumn`, #331/#867),
    /// factored so `done`'s Status=Done stamp and `claim`'s In-progress move cannot regrow the same
    /// promise-under-`ignore` (#1151). It does NOT touch the verdict: the caller keeps its exit code and
    /// merely emits this note, because the WORK is done (or the lock is held) whatever the column says.
    let private boardWriteNote (ref: Ref) (field: string) (value: string) (outcome: Result<Board.WriteOutcome, Errors.IoError>) : unit =
        match outcome with
        | Ok Board.Written -> ()
        | Ok Board.Deferred ->
            eprint
                $"fsgg-coord-engine: the %s{field}=%s{value} board write for %s{ref.Short} is DEFERRED — the budget is exhausted, so it is QUEUED, not lost, and NOTHING replays it on its own:  scripts/fsgg-coord flush"
        | Ok Board.NotOnBoard ->
            eprint
                $"fsgg-coord-engine: %s{ref.Short} is not an item on this board — the %s{field} column was NOT set to '%s{value}'."
        | Error e ->
            eprint
                $"fsgg-coord-engine: the %s{field}=%s{value} board write for %s{ref.Short} FAILED (%s{Errors.explain e}) — the column is UNCHANGED:  scripts/fsgg-coord set-field %s{ref.Short} %s{field} '%s{value}'"

    // ---- shared context --------------------------------------------------------------------------------

    let private env name fallback =
        match Environment.GetEnvironmentVariable(name: string) with
        | null
        | "" -> fallback
        | v -> v

    type Context =
        { Transport: IGitHubTransport
          Owner: string
          Title: string
          /// The board's default repo scope, for a bare `repo#n` ref and the candidate filter.
          DefaultRepo: string option
          /// The per-deployment chore-lock roster a VENDORED tenant injects by env
          /// (`FSGG_COORD_CHORE_LOCKS`, parsed by `parseChoreLocks`). Matched on (owner, repo) under ANY
          /// owner and consulted BEFORE the engine's embedded FS-GG table — empty for the default FS-GG
          /// deployment, so its behaviour is unchanged. See `Options.choreLockRef`.
          ChoreLocks: Ref list }

    /// Parse a `<ref>` — a URL, `owner/repo#n`, `repo#n` (owner defaulting to the board owner), or a bare
    /// `n`/`#n` (repo defaulting to `defaultRepo`, the checkout you are standing in).
    ///
    /// PUBLIC PURELY TO BE TESTED — `parseRef` below is the real entry point, and it takes a `Context`
    /// carrying a live transport, which no unit test can build. Same idiom as `parseGitHubSlug` (#480).
    ///
    /// #548: the bare form is the one EVERY recipe hands a worker — `claim <issue>`, `widen <issue>`,
    /// `heartbeat <issue>`, `done <issue> --flip` — immediately after `take` has printed the item and the
    /// worker has been thinking in bare numbers all session. Rejecting it was not a papercut. On
    /// `heartbeat` the refusal runs unattended, so it ends 120 minutes later as an expired lease and TWO
    /// WORKERS ON ONE ITEM — the exact failure the protocol exists to prevent. On `done --flip` it fires
    /// after the merge, stranding green, merged work with the board un-flipped and the touch-set still
    /// reserved. On `widen` it is worse still: the recipe teaches a worker to read a non-zero exit as a
    /// COLLISION, so a usage error is indistinguishable from one, and the documented response to a
    /// collision is to stop editing — or to route around the tool with `gh issue edit`, which is the
    /// silent last-write-wins body clobber `widen` exists to prevent.
    ///
    /// `defaultRepo` is `None` when there is NO FS-GG repo to infer from — outside a checkout, or in one
    /// whose owner is not the board's — and a bare number then stays a hard error. That is the ask's one
    /// ambiguity criterion: `506` must never silently address another org's issue #506.
    ///
    /// The bare form matches `Blockers.canonToken`, which has always accepted `#n` by adopting the item's
    /// own owner/repo. `#?` here so `548` and `#548` both parse and the two ref readers in this codebase
    /// stop disagreeing about what a ref is.
    let parseRefIn (owner: string) (defaultRepo: string option) (raw: string) : Result<Ref, string> =
        RefParsing.parse owner defaultRepo raw

    /// Parse a `<ref>` against the ambient context. See `parseRefIn` — this only supplies the defaults.
    let private parseRef (ctx: Context) (raw: string) : Result<Ref, string> =
        parseRefIn ctx.Owner ctx.DefaultRepo raw

    /// Resolve the worker, printing the shared-session warning to stderr but proceeding — the id is still
    /// this worker's in the common single-worker case; the warning is for the fan-out that needs to know.
    let private worker (opts: Options) : Result<Identity.Worker, int> =
        match Identity.resolve opts.Worker with
        | Error msg ->
            eprint $"fsgg-coord-engine: %s{msg}"
            Result.Error ExitError
        | Ok w ->
            match w.Provenance with
            | Identity.FromSharedSession(_, _, why) ->
                // #419: point at the mint COMMAND, not a literal — the same remedy `whoami` gives, so the
                // command path agents actually run most does not re-introduce the copy-a-literal attractor.
                // The id is named as DIAGNOSIS (which id you are using now), never as one to invent.
                eprint $"fsgg-coord-engine: WARNING — worker id '%s{w.Id}' was derived from a session where %s{why}. Give EACH worker a unique id (do NOT invent one):  eval \"$(scripts/fsgg-coord whoami --mint)\""
            | _ -> ()

            Ok w

    let private oneArg (opts: Options) (what: string) : Result<string, int> =
        match opts.Args with
        | [ a ] -> Ok a
        | [] ->
            eprint $"fsgg-coord-engine: %s{what} required."
            Result.Error ExitError
        | _ ->
            eprint $"fsgg-coord-engine: %s{what} takes exactly one argument (got %d{List.length opts.Args})."
            Result.Error ExitError

    /// OUR SESSION, for the twin predicate — the same one `claim` rides into the marker it posts.
    ///
    /// `None` means this harness exports no session. That is not a failure and must not fail closed: a
    /// caller with no session of its own has nothing to compare, so it can never conclude "twin", and
    /// `verifyHeld` keeps the pre-#1031 behaviour for it (Writes.twinSession).
    let private sessionOf (w: Identity.Worker) : SessionId option = w.Session |> Option.map SessionId

    /// WHO THIS PROCESS IS, as opposed to who `--worker` said it was (#1646) — the third fact the lock
    /// boundary asks, after the id and the session, and the only one the flag cannot restate.
    ///
    /// `DerivesNothing` is a caller that resolves no identity of its own (a human, a harness exporting no
    /// session and setting no `$FSGG_WORKER`). It must NOT fail closed, for `sessionOf`'s reason exactly:
    /// `--worker` is the only way such a caller can say who it is, so refusing it would lock out the operator
    /// the flag exists for. What it costs is stated in `Writes.SelfIdentity` rather than hidden here.
    let private selfOf (w: Identity.Worker) : Writes.SelfIdentity =
        match w.Derived with
        | Some d -> Writes.Derives(WorkerId d)
        | None -> Writes.DerivesNothing

    /// THE REMEDY FOR A SHARED ID, and it is a MINT — never `--force` (#1031).
    ///
    /// A twin is a broken IDENTITY, not a contested item: forcing would delete a lock our twin is working
    /// behind, which is the double-claim ADR-0027 exists to prevent, so `claim` refuses a twin even under
    /// `--force` (#419) and nothing here may offer it as a way out. And the remedy is a COMMAND, not a
    /// literal — an id an agent copies is an id agents collide on (#551), which is why no id appears here to
    /// copy.
    let private mintRemedy () =
        eprint "  Mint a fresh, unique id in THIS shell (do NOT invent one):  eval \"$(scripts/fsgg-coord whoami --mint)\""

    /// THE TWIN REFUSAL, shared by the three verbs that REFUSE over a twin's marker — `release`, `heartbeat`,
    /// `widen` (#1031).
    ///
    /// ONE MESSAGE, and `claim`'s wording deliberately: a worker who meets this from `release` and then from
    /// `heartbeat` is in ONE situation — a broken identity — and must not have to work out that two
    /// differently-worded refusals mean the same thing.
    ///
    /// `done`'s lock-drop is the fourth `verifyHeld` caller and deliberately does NOT use this: it is not a
    /// refusal. The stamp is earned by the MERGE, which owes nothing to whose session holds the lock, so
    /// `done` reports the twin and still exits green. Same hazard, different verdict — so it says a different
    /// thing, and shares only the remedy (`mintRemedy`).
    ///
    /// ExitRed, matching `claim`'s twin refusal: a broken identity is a stop, not a retry.
    let private twinRefusal (verb: string) (workerId: string) (ref: Ref) (theirs: SessionId) : int =
        eprint
            $"fsgg-coord-engine: %s{ref.Short} carries a live marker with YOUR worker id '%s{workerId}' but a DIFFERENT session (%s{theirs.Value}) — two workers share one id (#419). %s{verb} would act on your TWIN's lock, not yours."

        // A different session can also be a long-lived worker whose harness rotated its ambient
        // session id (#1857). We cannot prove that here, so preserve #419's refusal; but minting
        // would strand that worker's live claim. Name the safe recovery which pins the marker fact.
        eprint $"  This may be a rotated session, not a twin. If this is your existing claim, retry with CLAUDE_CODE_SESSION_ID=%s{theirs.Value} FSGG_WORKER=%s{workerId}; do NOT mint a new id for this live claim."
        ExitRed

    /// THE IMPERSONATION REFUSAL (#1646), shared by the verbs that REFUSE over a worker we are not — `claim`,
    /// `release`, `heartbeat`, `widen` — for `twinRefusal`'s reason: one situation, one message.
    ///
    /// `done`'s lock-drop is the fifth site and deliberately does NOT use this, exactly as it declines
    /// `twinRefusal`: the stamp is earned by the MERGE, which owes nothing to whose lock sits on the item, so
    /// `done` reports the foreign claim, leaves it alone, and still exits green. Same hazard, different
    /// verdict, different sentence.
    ///
    /// IT SAYS A DIFFERENT THING FROM THE TWIN REFUSAL, AND MUST. A twin is a broken IDENTITY — two workers
    /// arrived at one id by accident — and its remedy is a new id. This is not an accident and a new id is not
    /// the remedy: the caller HAS an id, and typed somebody else's. Sending them to `whoami --mint` would be
    /// advice for a collision they do not have, so `mintRemedy` is deliberately not called here.
    ///
    /// THE REMEDY IS THE SANCTIONED STEAL, and naming it is the whole point of the change. #1620 built
    /// `claim --force` precisely so a worker recovering a dead holder had a route that RECORDS the theft on
    /// the item and makes the displaced worker's next `heartbeat` fail loudly — and then the impersonation
    /// route stayed open, one flag shorter and silent. A refusal that did not point at `--force` would leave
    /// the recovering worker exactly where #1596's did: at a documented dead end, inventing a way round.
    ///
    /// **AND THE REMEDY IS SPELLED WITHOUT `--worker`, WHICH IS NOT A DETAIL.** Review caught this: a caller
    /// who copies the line back with the flag they already had — `claim <ref> --force --worker <them>` — is
    /// refused by this very function, because a steal under a foreign id is the sharper half of what is being
    /// closed (it evicts a live holder AND signs the #1620 notice with a third party's name). A remedy that
    /// loops back to its own refusal is worse than none: it reads as the tool malfunctioning.
    ///
    /// It does NOT offer "export $FSGG_WORKER=<them>", which would work. That is the residue #1646 records
    /// (this is not a proof of identity, and cannot be), not a workaround to publish: a tool that prints its
    /// own bypass has closed nothing.
    ///
    /// ExitRed, matching the twin refusal: acting as another worker is a stop, not a retry.
    let private impersonationRefusal
        (verb: string)
        (ref: Ref)
        (derived: WorkerId)
        (named: WorkerId)
        : int =
        eprint
            $"fsgg-coord-engine: refusing to %s{verb} %s{ref.Short} as '%s{named.Value}' — this process's OWN worker id is '%s{derived.Value}'. `--worker` ASSERTS an identity; it does not prove one, so acting on %s{ref.Short} under another worker's id would take, renew or destroy their lock with nothing recorded and nobody told (#1646)."

        // WITHOUT `--worker`, and the omission is load-bearing — see the doc above. The line a caller pastes
        // back has to be one that RUNS.
        eprint
            $"  If you are RECOVERING a holder that is really gone, use the sanctioned route AS YOURSELF — it posts the theft on the item and makes their next heartbeat fail loudly:  FSGG_WORKER=%s{derived.Value} scripts/fsgg-coord claim %s{ref.Short} --force"

        eprint
            $"  If you meant to act as YOURSELF, drop `--worker %s{named.Value}` — this process is '%s{derived.Value}'."

        // THE ID WE JUST TOLD THEM TO USE MAY ITSELF BE SHARED, AND NOTHING ELSE WOULD SAY SO (#419).
        //
        // `worker opts` prints the shared-session warning off the PROVENANCE, and with `--worker` present the
        // provenance is `FromFlag` — so a caller in exactly this state has never been warned that the id this
        // refusal is sending them back to is one every sibling of the fan-out also derives. Pointing a worker
        // at a shared id without saying so would be this refusal re-creating #419 while closing #1646.
        match Identity.derivedProvenance () with
        | Some(Identity.FromSharedSession(_, _, why)) ->
            eprint
                $"  NOTE — '%s{derived.Value}' was derived from a session where %s{why}, so it is not unique to this worker either. Mint one first (do NOT invent one):  eval \"$(scripts/fsgg-coord whoami --mint)\""
        | _ -> ()

        ExitRed

    /// THE TYPO NOTE (#1646 AC 3), and it is NOT the impersonation refusal.
    ///
    /// By far the commonest way to name an id that is not yours is to mistype it, and the remedy for that is
    /// "check the flag", not an accusation. So the accusation is reserved for the one case where the named id
    /// holds the LIVE lock (`Writes.ImpersonatesHolder`), and every other disagreement lands here: appended to
    /// whatever "you do not hold this" the verb already prints, saying which id this process actually is.
    ///
    /// Silent when the ids agree — which is every ordinary invocation — so the common path gains no noise.
    let private noteWorkerDisagreement (w: Identity.Worker) =
        match w.Derived with
        | Some d when d <> w.Id ->
            eprint
                $"  (note: '%s{w.Id}' is not this process's own worker id — that is '%s{d}'. If you did not mean to act as another worker, check `--worker`.)"
        | _ -> ()

    // ---- the read / schedule commands ------------------------------------------------------------------

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
                            PathRepo = Options.resolveRepo row.PathRepo
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
    let renderDecision (opts: Options) (rows: Scan.Row list) (doc: string) : Result<Batch.BatchResult, int> =
        match Snapshot.parse doc with
        | Error errors ->
            for e in errors do
                eprint $"fsgg-coord-engine: %s{e.Path}: %s{e.Message}"

            Result.Error ExitError
        | Ok parsed ->
            let request = enrichBoardFacts rows parsed

            match
                Batch.scheduleWith
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

    /// The candidates the scheduler LOOKED AT and refused. One spelling, because two call sites print this
    /// list and a third reports its COUNT on the wire (`take --json`'s `passedOver`, .github#1525) — a
    /// receipt whose number disagreed with the reasons printed beside it would be worse than no number.
    let private passedOver (result: Batch.BatchResult) =
        result.Decisions |> List.filter (fun d -> d.Result <> Schedulability.Startable)

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

    let private activeItemRefs (doc: string) : Result<Ref list, string> =
        match Snapshot.parse doc with
        | Error errors ->
            errors
            |> List.map (fun e -> $"%s{e.Path}: %s{e.Message}")
            |> String.concat "; "
            |> Error
        | Ok request ->
            let candidates =
                request.Candidates
                |> List.choose (fun c ->
                    if c.Item.Claim.IsSome || c.Item.ItemPr.IsSome then Some c.Item.Ref else None)

            let reservations =
                request.InFlight
                |> List.choose (fun r ->
                    match r.Holder with
                    | Batch.LiveClaim(_, item, _, _) -> Some item
                    | Batch.BatchMember item
                    | Batch.Unowned item -> Some item
                    | _ -> None)

            Ok(candidates @ reservations |> List.distinct)

    /// Occupancy is advisory, not enforcing: refusing `batch` on an open slot would prevent the dispatch
    /// that closes it, and a draining queue legitimately has spare capacity. It is nevertheless loud and
    /// typed at the decision point, where a host can act on the measured deficit instead of remembering
    /// prose from the start of a long run. STDERR preserves `batch`'s stdout machine contract byte-for-byte.
    let private sayWaveOccupancy (doc: string) (result: Batch.BatchResult) : unit =
        match readWaveModel (), activeItemRefs doc with
        | Ok model, Ok active ->
            let occupancy = Batch.waveOccupancy model active
            eprint (Batch.renderWaveOccupancy occupancy)

            Batch.waveShortfallHeadline (List.length result.Chosen) occupancy
            |> Option.iter eprint
        | model, active ->
            let explain = function
                | Ok _ -> None
                | Error e -> Some e

            [ explain model; explain active ]
            |> List.choose id
            |> String.concat "; "
            |> fun reason -> eprint $"wave occupancy: unavailable (%s{reason})"

    let batch (ctx: Context) (opts: Options) : int =
        match scanAndDecide ctx opts Cache.Scheduling with
        | Error e -> fail e
        | Ok(rows, doc, receipt) ->
            sayRepoAdvisory receipt

            match renderDecision opts rows doc with
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
            let schemaField, observedAtField, sourceShaField, completeField, consolidationApprovedField, observationsField =
                match receiptFields with
                | [ schema; observedAt; sourceSha; complete; consolidationApproved; observations ] ->
                    schema, observedAt, sourceSha, complete, consolidationApproved, observations
                | _ -> failwith "the ledger receipt field policy is malformed"
            let kindField, observationObservedAtField, observationSourceShaField, outcomeField, receiptIdField =
                match observationFields with
                | [ kind; observedAt; sourceSha; outcome; receiptId ] -> kind, observedAt, sourceSha, outcome, receiptId
                | _ -> failwith "the ledger observation field policy is malformed"
            let requireFields (fields: string list) (node: JsonElement) =
                fields |> List.iter (fun name ->
                    let mutable value = Unchecked.defaultof<JsonElement>
                    if not (node.TryGetProperty(name, &value)) then failwith $"missing ledger field {name}")
            requireFields receiptFields root
            if root.GetProperty(schemaField).GetString() <> Protocol.ledgerPolicy.Schema then
                failwith "the ledger receipt schema is unsupported"
            let bool (name: string) (node: JsonElement) = node.GetProperty(name).GetBoolean()
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
                    |> Seq.toList }
            Ok receipt
        with error -> Error $"the driver receipt is malformed: %s{error.Message}"

    /// Live inspection derives occupancy from the same board snapshot as `batch`, never caller input.
    let driver (ctx: Context) (opts: Options) : int =
        match scanAndDecide ctx opts Cache.Scheduling, readWaveModel () with
        | Error e, _ -> eprint (Errors.explain e); ExitError
        | _, Error e -> eprint e; ExitError
        | Ok(_, doc, _), Ok model ->
            match activeItemRefs doc with
            | Error e -> eprint e; ExitError
            | Ok active ->
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
                                    let comments = comments |> List.map (fun c -> ({ Id = c.Id; Url = c.Url; Body = c.Body }: Driver.ReviewComment))
                                    match Driver.parseReviewComments comments with
                                    | Ok review when review.HeadSha = Some head ->
                                        Some(pr, head, { review with ChecksGreen = true }, scan.Markers.Length)
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
                            match Reads.winner opts.LeaseMinutes markers with
                            | Some marker when marker.Worker.Value = w.Id -> Ok(Some marker)
                            | Some marker -> Error(Errors.Malformed(target.Short, $"live claim belongs to worker '%s{marker.Worker.Value}', not '%s{w.Id}'"))
                            | None when terminalBoardState -> Ok None
                            | None -> Error(Errors.Malformed(target.Short, "no live claim marker can authorize delivery")))

                    match liveClaim, Cache.pending () with
                    | Error error, _ -> fail error
                    | _, Error error -> fail error
                    | Ok marker, Ok pending ->
                        let declaredPaths =
                            match candidate.Item.TouchSet with
                            | Declared tokens ->
                                tokens
                                |> List.map (function | Matchable value | Unmatchable value -> value)
                            | DeclaredChore -> [ "any" ]
                            | DeclaredNone
                            | Undeclared
                            | Unreadable _ -> []

                        let branchAndPr: Result<string * int option * string * bool * bool * bool * Driver.ReviewChain option * bool * bool * bool * Delivery.Obligation list, Errors.IoError> =
                            match opts.Pr with
                            | None -> Ok(Directory.GetCurrentDirectory(), None, "", false, false, false, None, false, false, false, [])
                            | Some pr ->
                                match Reads.prHeadRef ctx.Transport target.Owner target.Repo pr,
                                      Reads.prHeadSha ctx.Transport target.Owner target.Repo pr,
                                      Reads.prLandable ctx.Transport target.Owner target.Repo pr,
                                      Reads.prClosingRef ctx.Transport target.Owner target.Repo pr,
                                      Reads.prFiles ctx.Transport target.Owner target.Repo pr,
                                      Reads.commentsWithIdentity ctx.Transport target.Owner target.Repo pr with
                                | Ok branch, Ok head, landable, Ok closing, Ok files, Ok comments ->
                                    let review =
                                        comments
                                        |> List.map (fun comment -> ({ Id = comment.Id; Url = comment.Url; Body = comment.Body }: Driver.ReviewComment))
                                        |> Driver.parseReviewComments
                                        |> Result.map (fun parsed -> { parsed with ChecksGreen = landable = PrGreen })
                                        |> Result.toOption
                                    let itemBranchCanonical = branch.StartsWith($"item/%d{target.Number}-", StringComparison.Ordinal)
                                    let linkageCanonical = closing |> Option.exists ((=) target)
                                    let pathsVerified =
                                        match candidate.Item.TouchSet with
                                        | Declared tokens -> files |> List.forall (fun file -> tokens |> List.exists (fun token -> TouchSet.covers token file))
                                        | _ -> false
                                    let reviewComments =
                                        comments
                                        |> List.map (fun comment -> ({ Id = comment.Id; Url = comment.Url; Body = comment.Body }: Driver.ReviewComment))
                                    let obligations = DeliveryApplication.obligationsFromComments head reviewComments
                                    let obligationsDeclared = Result.isOk obligations
                                    let obligations = obligations |> Result.defaultValue []
                                    Ok(branch, Some pr, head, itemBranchCanonical, linkageCanonical, pathsVerified, review, (landable = PrGreen), (landable = PrMerged), obligationsDeclared, obligations)
                                | Error error, _, _, _, _, _
                                | _, Error error, _, _, _, _
                                | _, _, _, Error error, _, _
                                | _, _, _, _, Error error, _
                                | _, _, _, _, _, Error error -> Error error

                        match branchAndPr with
                        | Error error -> fail error
                        | Ok(branch, pr, head, itemBranchCanonical, closingLinkageCanonical, pathsVerified, review, landable, merged, obligationsDeclared, obligations) ->
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
                                match currentClaimGeneration with
                                | Error error -> fail error
                                | Ok generation ->
                                    match DeliveryApplication.guardedLanding transition.FreshnessToken transition.ActionKey facts generation (fun () -> Writes.mergeAtHead ctx.Transport target pr.Value head) with
                                    | Error reason ->
                                        eprint $"fsgg-coord-engine: delivery --apply is refused: %s{reason}"
                                        ExitNoVerdict
                                    | Ok merge ->
                                        match merge with
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
            Chores.offer ctx.Transport boundary (WorkerId w.Id) (selfOf w) session ctx.ChoreLocks ctx.Owner repo observed
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

            match renderDecision opts rows doc with
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
    let reconcile (ctx: Context) (opts: Options) : int =
        match scanAndDecide ctx { opts with Limit = None } Cache.Reconciling with
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

                let chores = Chore.derive items

                // .github#2079: BLOCKER-CLEARED must not promote a row whose BODY's `Blocked by:` line
                // names ref(s) the FIELD does not carry — the `FS.GG.Templates#348` shape, where the
                // field's all-resolved read is not trustworthy because the park's real edge landed only
                // in the body. `Chore.derive` above is untouched and correct on the FIELD alone, which is
                // the only fact `Item` carries; the body's RAW text is the one additional fact only this
                // CLI layer has (read straight off the snapshot document, since `Item` has nowhere to put
                // it), so the withholding happens here — one predicate, `blockedByBodyDivergence`, shared
                // with `lint`'s `BLOCKED-BY-INERT` above, never a second copy that could disagree with it.
                let bodyByRef: Map<Ref, string> =
                    try
                        use parsed = JsonDocument.Parse(doc)

                        match parsed.RootElement.TryGetProperty "items" with
                        | false, _ -> Map.empty
                        | true, elItems ->
                            elItems.EnumerateArray()
                            |> Seq.choose (fun el ->
                                match el.TryGetProperty "body" with
                                | true, bodyEl when bodyEl.ValueKind = JsonValueKind.String ->
                                    let owner = el.GetProperty("owner").GetString()
                                    let repo = el.GetProperty("repo").GetString()
                                    let number = el.GetProperty("number").GetInt32()

                                    Some({ Owner = owner; Repo = repo; Number = number }, bodyEl.GetString())
                                | _ -> None)
                            |> Map.ofSeq
                    with _ ->
                        // FAIL OPEN INTO NO EVIDENCE, NOT INTO A CRASH (#266's other edge): a document this
                        // read cannot make is a document this rule cannot act on, and the rule it defers to
                        // is `Chore.derive`'s own — already computed above, already correct on the field.
                        // This block only ever WITHHOLDS a promotion; it never grants one, so an empty map
                        // here costs nothing but the withholding this issue exists to add.
                        Map.empty

                let blockedByRawByRef: Map<Ref, string> =
                    rows |> List.map (fun r -> r.Ref, r.BlockedByRaw) |> Map.ofList

                let divergenceOf (c: Chore.Chore) : string list =
                    let fieldRaw = Map.tryFind c.Subject blockedByRawByRef |> Option.defaultValue ""
                    let body = Map.tryFind c.Subject bodyByRef |> Option.defaultValue ""
                    blockedByBodyDivergence c.Subject.Owner c.Subject.Repo fieldRaw body

                let chores, blockerClearedWithheld =
                    chores
                    |> List.partition (fun c ->
                        match c.Kind with
                        | Chore.BlockerCleared _ -> List.isEmpty (divergenceOf c)
                        | _ -> true)

                for c in blockerClearedWithheld do
                    let named = divergenceOf c |> String.concat ", "

                    eprint
                        $"fsgg-coord-engine: reconcile: withheld BLOCKER-CLEARED for %s{c.Subject.Short} — its body's `Blocked by:` line names %s{named}, which the FIELD does not carry, so the field's all-resolved read is not trustworthy (.github#2079). Run `lint` for the divergence (`BLOCKED-BY-INERT`), reconcile the FIELD to match, then re-run."

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

                // A cleared dependency is a two-field projection.  Keeping the old `Blocked by` text
                // beside a Ready status creates the half-converged shape this reconciler is meant to
                // remove, so send both values in one aliased mutation.
                let writesFor (chore: Chore.Chore) =
                    match chore.Kind with
                    | Chore.BlockerCleared _ -> [ "Status", Board.Set(statusWireName FS.GG.Coord.Types.Ready); "Blocked by", Board.Clear ]
                    | _ ->
                        match write chore with
                        | Some(field, value) -> [ field, Board.Set value ]
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
                        // fresh scan that proves the projected row contains every requested value.
                        let verifyWrites (chore: Chore.Chore) (writes: (string * Board.FieldWrite) list) =
                            match Scan.board ctx.Transport Cache.Reconciling ctx.Owner ctx.Title board.Number with
                            | Error e -> Error(None, Errors.explain e)
                            | Ok freshRows ->
                                match freshRows |> List.tryFind (fun row -> row.Ref = chore.Subject) with
                                | None -> Error(None, "the item left the board before fresh verification")
                                | Some row ->
                                    let observed field =
                                        match field with
                                        | "Status" -> statusWireName row.Status
                                        | "Blocked by" -> row.BlockedByRaw
                                        | "Class" -> row.BoardClass |> Option.map itemClassWireName |> Option.defaultValue ""
                                        | _ -> ""

                                    let observedValues =
                                        writes |> List.map (fun (field, _) -> field, observed field)

                                    let mismatches =
                                        writes
                                        |> List.choose (fun (field, requested) ->
                                            let intended = match requested with | Board.Set value -> value | Board.Clear -> ""
                                            let actual = observed field
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

                        let withheldClassProjections =
                            chores
                            |> List.filter (fun c ->
                                match c.Kind with
                                | Chore.ClassProjectionLag _ -> true
                                | _ -> false)

                        if classFieldMissing && not (List.isEmpty withheldClassProjections) then
                            eprint
                                "fsgg-coord-engine: reconcile: board has no Class field; withheld Class projections. Create it with createProjectV2Field before writing the first Class: line (docs/coordination/board-schema.md)."

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

                                      let outcome =
                                          match chore.Kind with
                                          | Chore.BlockerCleared _ ->
                                              Board.boardWriteBatch ctx.Transport board chore.Subject.Owner chore.Subject.Repo chore.Subject.Number writes w.Id
                                          | _ ->
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

                let candidates =
                    if failure.IsSome then
                        []
                    else
                        Seq.append offBoard.Keys inProgressRefs
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
                                    match markers |> List.sortBy (fun m -> m.Id) |> List.tryHead with
                                    | Some m -> Some(Stale m)
                                    // NO MARKER WE COULD READ. Before this may be reported as an ABSENCE,
                                    // the read has to have been COMPLETE (.github#1668). If any comment on
                                    // this issue could not be classified, one of them may have been the
                                    // claim, and the honest answer is that we cannot tell.
                                    //
                                    // It fires on BOTH arms, deliberately. On arm A it replaces the
                                    // `UNCLAIMED` accusation. On arm B — where a markerless issue is
                                    // normally not in flight at all and is simply dropped — it makes the row
                                    // APPEAR, because an off-board issue hiding an unreadable marker is
                                    // precisely the held item this verb must not omit.
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
                            match markers |> List.sortBy (fun m -> m.Id) |> List.tryHead with
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
                           restReported = false
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

                printfn
                    "REST requests: NOT REPORTED here — /rate_limit's `core` figure disagrees with the counter real requests are billed against on this account, and a SECONDARY (abuse-detection) limit never appears in it. The claim lock lives on REST (ADR-0034 §3), so a healthy line above is not evidence that `claim`/`take`/`who` will run."

                match pending with
                | Some 0 -> printfn "pending board writes: 0"
                | Some n -> printfn "pending board writes: %d — replay them with `flush` (#862)" n
                | None -> printfn "pending board writes: UNKNOWN — the deferral queue could not be read"

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
                |> List.map (fun r -> r.Ref, Options.resolveRepo r.PathRepo)
                |> Map.ofList
                |> Ok

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
                    if opts.Force then Ok [] else heldElsewhere ctx opts.LeaseMinutes w.Id ref

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
                    let emitClaimReceipt (kind: string) (held: Writes.Held) statusOutcome =
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

                        let statusWrite =
                            match statusOutcome with
                            | Ok Board.Written -> "written"
                            | Ok Board.Deferred -> "deferred"
                            | Ok Board.NotOnBoard -> "not-on-board"
                            | Error _ -> "failed"

                        let pending =
                            match Cache.pending () with
                            | Ok entries -> Some(List.length entries)
                            | Error _ -> None

                        let converged = markerObserved && status = Some "In progress"

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
                                printfn "%sboard confirmed: marker=%d, Status=In progress)" humanPrefix held.MarkerId
                            else
                                let shownStatus = status |> Option.defaultValue "UNREADABLE/UNSET"
                                printfn "%slock held; board NOT confirmed: marker=%b, Status=%s, write=%s)" humanPrefix markerObserved shownStatus statusWrite
                                eprint $"fsgg-coord-engine: do NOT announce or implement %s{ref.Short} yet — re-run `claim %s{ref.Short} --json` and require `.converged == true`; reconciliation retains CLAIM-STATUS-LAG repair."

                        boardWriteNote ref "Status" "In progress" statusOutcome
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

                    // Move the board column to In progress — the ONE board write, through the queue-aware
                    // path so an exhausted budget defers rather than drops (#510).
                    let setInProgress () =
                        match board.Force() with
                        | Error e -> Error e
                        | Ok b -> Board.boardWrite ctx.Transport b ref.Owner ref.Repo ref.Number "Status" (Board.Set "In progress") w.Id

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
                        displaced <- victims

                        for victim in victims do
                            match opts.Render with
                            | Json -> eprint $"STOLE %s{ref.Short} from worker '%s{victim.Value}' (--force)"
                            | Text -> printfn "STOLE %s from worker '%s' (--force)" ref.Short victim.Value

                            Writes.say
                                ctx.Transport
                                (WorkerId w.Id)
                                victim
                                ref
                                $"worker '%s{w.Id}' has TAKEN %s{ref.Short} from you with `claim --force` — your claim was live and its marker has been deleted. STOP working this item: you no longer hold it, `heartbeat` will refuse, and anything you push against it now races a second worker. If this was wrong, say so here."
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
                    with
                    | Error e -> failWith opts.Render e
                    | Ok(Writes.Won(held, collected)) ->
                        announceCollected collected
                        emitClaimReceipt "claimed" held (setInProgress ())
                    | Ok(Writes.Stolen(held, _, collected)) ->
                        // `announceTheft` has already run — it fired from inside the CAS, the moment the
                        // holder's marker went. All that is left here is the receipt, which reports the
                        // steal as a steal so a scripted caller can tell it from an ordinary win.
                        announceCollected collected
                        emitClaimReceipt "stolen" held (setInProgress ())
                    | Ok(Writes.Renewed(held, collected)) ->
                        // A live marker already ours — the claim RENEWED it in place rather than posting a
                        // second (a `take` retry, or a worker beating its own lease). Any stale debris it
                        // claimed over was still collected, so tell the evicted workers exactly as a fresh
                        // win does.
                        announceCollected collected
                        let outcome = setInProgress ()

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

                        emitClaimReceipt "renewed" held outcome
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
                            eprint $"fsgg-coord-engine: %s{ref.Short} is already held by %s{holder.Value}. Pick another, or wait for the lease."

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
                        // 2. There must be an EXPIRED claim to adopt — the lowest-id marker (`reap`'s rule).
                        match markers |> List.sortBy (fun m -> m.Id) |> List.tryHead with
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
                    let required = opts.Require
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
    let requireCoherentParkIfBlocked (ctx: Context) (ref: Ref) (requested: BoardStatus option) : Result<unit, int> =
        if requested <> Some BoardStatus.Blocked then
            Ok()
        else
            match Board.bootstrapCached ctx.Transport ctx.Owner ctx.Title with
            | Error e ->
                eprint
                    $"fsgg-coord-engine: release --status Blocked: the board could not be resolved (%s{Errors.explain e}) — nothing released."

                Error ExitError
            | Ok board ->
                match Board.itemBlockedBy ctx.Transport board ref.Owner ref.Repo ref.Number with
                | Error e ->
                    eprint
                        $"fsgg-coord-engine: release --status Blocked: the live 'Blocked by' field could not be read (%s{Errors.explain e}) — a field this call could not read is not one it may assume is set (#266). Nothing released."

                    Error ExitError
                | Ok(Some fieldValue) when not (String.IsNullOrWhiteSpace fieldValue) -> Ok()
                | Ok _ ->
                    match Reads.issueBody ctx.Transport ref.Owner ref.Repo ref.Number with
                    | Error e ->
                        eprint
                            $"fsgg-coord-engine: release --status Blocked: the body could not be read to check for a 'Blocked on:' sentinel (%s{Errors.explain e}) — nothing released."

                        Error ExitError
                    | Ok body ->
                        match HumanBlock.parse body with
                        | Some _ -> Ok()
                        | None ->
                            eprint
                                $"fsgg-coord-engine: release --status Blocked refuses: the row would land with an EMPTY 'Blocked by' field and no 'Blocked on: human/...' sentinel — a park is two writes and nothing else binds them (.github#2079). Write the edge:  scripts/fsgg-coord set-field %s{ref.Short} 'Blocked by' '<refs>'  (or pass --blocked-by on this call), or record a human park: add a body line 'Blocked on: human/decision' or 'Blocked on: human/action'. Nothing released."

                            Error ExitError

    /// The authoritative inventory of every `Status=Blocked` writer (#2109).  A writer is either a
    /// deliberate park and MUST pass `requireCoherentParkIfBlocked`, or a restore of a marker's
    /// recorded previous column. Restores never choose `Blocked`: `unclaimColumn` copies only the
    /// value captured by `claim`, and their safety boundary is the claim record rather than a new
    /// park decision. Keep this executable list beside the gate and pin it in BlockerLintTests; adding
    /// a writer without classifying it is a test failure, not a prose omission.
    type BlockedStatusWriter =
        | DeliberatePark of string
        | RecordedRestore of string

    let blockedStatusWriterCoverage : BlockedStatusWriter list =
        [ DeliberatePark "release --status Blocked"
          DeliberatePark "set-field Status Blocked"
          DeliberatePark "set-field --batch Status=Blocked"
          DeliberatePark "add --status Blocked"
          RecordedRestore "release (recorded previous Status=Blocked)"
          RecordedRestore "reap (recorded previous Status=Blocked)" ]

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
                                match
                                    Board.boardWrite ctx.Transport board ref.Owner ref.Repo ref.Number "Status" (Board.Set name) w.Id
                                with
                                | Ok Board.Written -> true
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

    let take (ctx: Context) (opts: Options) : int =
        match worker opts with
        | Error c -> c
        | Ok w ->
            match scanAndDecide ctx { opts with Limit = Some 1 } Cache.Scheduling with
            // #585: a board we could not read is NOT an empty queue — but that distinction is already
            // carried by the code `fail` returns (EX_RATE for a budget, a non-zero read error otherwise),
            // and it is never EX_NONE, so "I could not look" and "I looked, and it is empty" keep
            // different codes (#266). bash's hard board-read failure exits the same way (#344's fatal
            // die), so the two engines agree.
            | Error e -> failWith opts.Render e
            | Ok(rows, doc, receipt) ->
                sayRepoAdvisory receipt

                match renderDecision { opts with Limit = Some 1 } rows doc with
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
                    | item :: _ ->
                        // Claim the chosen item. `claim` re-reads and runs the CAS, so a stale scan cannot
                        // cost a double-claim: the loser backs off and the caller retries.
                        // #585: translate the claim's verdict into `take`'s contract — a win is 0, an
                        // exhausted budget passes through as EX_RATE (back off until reset), and any other
                        // failure is a LOST RACE (EX_CONTENDED): the item was startable when we picked it,
                        // so a failure to take it means someone else got there first.
                        // Pass the selected typed identity through the mutating path.  `Short` is a display
                        // projection and parsing it here used `ctx.Owner` as a new default, turning an
                        // offered external row into an attempt to claim an unrelated default-owner twin.
                        match claim ctx { opts with Args = claimArgsForSelected item } with
                        | code when code = ExitGreen -> ExitGreen
                        | code when code = Errors.ExRate -> code
                        | _ -> ExitContended

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

    /// `set-field --batch <ref> Field=Value ...` — N fields in ONE aliased mutation (#448).
    ///
    /// The whole point is the call count: three separate writes are three GraphQL points; the same three
    /// aliased into one document is one. So EVERYTHING is resolved before a single mutation is emitted (a
    /// pair that fails validation costs zero — a refused value must not spend the budget that dies first),
    /// and the two failure arms are told apart because they carry opposite promises: a rate limit refused
    /// the whole document, so every pair is QUEUED; a per-alias failure means the board is half-written, so
    /// nothing is queued and the caller is told, field by field, what landed and what did not.
    let private setFieldBatchCmd (ctx: Context) (opts: Options) : int =
        match opts.Args with
        | refArg :: (_ :: _ as pairs) ->
            match parseRef ctx refArg, worker opts with
            | Error msg, _ ->
                eprint $"fsgg-coord-engine: %s{msg}"
                ExitError
            | _, Error c -> c
            | Ok ref, Ok w ->
                // Split each pair on the FIRST '=' — a value may legitimately contain one (`Contract=a=b`),
                // and an empty value clears, exactly as the single path's empty `<value>` does.
                let parsePair (s: string) : Result<string * Board.FieldWrite, string> =
                    match s.IndexOf '=' with
                    | -1 -> Error s
                    | i ->
                        let field = s.Substring(0, i)
                        let value = s.Substring(i + 1)

                        if field = "" then Error s
                        else Ok(field, (if value = "" then Board.Clear else Board.Set value))

                let parsed = pairs |> List.map parsePair

                match parsed |> List.tryPick (function | Error s -> Some s | Ok _ -> None) with
                | Some bad ->
                    eprint
                        $"fsgg-coord-engine: set-field --batch takes Field=Value pairs (an empty value clears); '%s{bad}' is not one."
                    ExitError
                | None ->
                    let rawWrites = parsed |> List.choose (function | Ok p -> Some p | Error _ -> None)

                    // The `Blocked by` gate applies to `--batch` too — the same one home the single write
                    // uses — so a prose dependency cannot slip in through the aliased document. It runs
                    // BEFORE bootstrap, so a refused pair spends no GraphQL and queues nothing.
                    let gated =
                        (Ok [], rawWrites)
                        ||> List.fold (fun acc (field, write) ->
                            match acc with
                            | Error _ -> acc
                            | Ok done' ->
                                let raw =
                                    match write with
                                    | Board.Set v -> v
                                    | Board.Clear -> ""

                                match gateField ref field raw with
                                | Error rc -> Error rc
                                | Ok w -> Ok((field, w) :: done'))
                        |> Result.map List.rev

                    match gated with
                    | Error rc -> rc
                    | Ok writes ->

                    // #2098 AC1: the SAME coherent-park invariant `release`/single `set-field` already
                    // enforce, reached through the batch door — a `Status=Blocked` write must not land
                    // with an empty `Blocked by` field and no `Blocked on: human/...` sentinel. Judged
                    // against THIS batch's own pending writes (`requireCoherentParkIfBlockedForBatch`),
                    // so a call pairing `Status=Blocked` with a non-empty `Blocked by=<ref>` in the SAME
                    // document is coherent without a live read racing its own not-yet-emitted mutation.
                    // Runs BEFORE any alias is emitted, same as `gateField` above — a refused batch must
                    // cost nothing.
                    let requestedStatus =
                        writes
                        |> List.tryPick (fun (field, write) ->
                            if field = "Status" then
                                match write with
                                | Board.Set v -> Reads.statusOfName v
                                | Board.Clear -> None
                            else
                                None)

                    let pendingBlockedBy =
                        writes
                        |> List.tryPick (fun (field, write) -> if field = "Blocked by" then Some write else None)

                    match requireCoherentParkIfBlockedForBatch ctx ref requestedStatus pendingBlockedBy with
                    | Error rc -> rc
                    | Ok() ->

                    match Board.bootstrapCached ctx.Transport ctx.Owner ctx.Title with
                    | Error e -> fail e
                    | Ok board ->
                        // Map an alias ("f2") back to the pair it wrote, so a partial write can be reported
                        // in the caller's OWN vocabulary — `Field='value'` — not "f2".
                        let describe (alias: string) : string =
                            let idx =
                                match Int32.TryParse(alias.TrimStart 'f') with
                                | true, n -> Some n
                                | _ -> None

                            match idx with
                            | Some i when i >= 0 && i < List.length writes ->
                                match List.item i writes with
                                | field, Board.Set v -> $"%s{field}='%s{v}'"
                                | field, Board.Clear -> $"%s{field}=<cleared>"
                            | _ -> alias

                        match Board.boardWriteBatch ctx.Transport board ref.Owner ref.Repo ref.Number writes w.Id with
                        // THE PARTIAL ARM IS ITS OWN ANSWER — matched BEFORE the generic failure. Some aliases
                        // landed; reporting nothing happened would be a lie, and reporting success is the bug
                        // #448 forbade by name. EX_PARTIAL (4), and the board is half-written on the record.
                        | Error(Errors.Partial(applied, failed)) ->
                            eprint
                                "fsgg-coord-engine: PARTIALLY APPLIED — the board is now half-written. This is NOT queued: replaying the document would rewrite the aliases that already landed."

                            for alias in applied do
                                eprint $"  APPLIED  %s{describe alias}"

                            for alias, msg in failed do
                                eprint $"  FAILED   %s{describe alias} — %s{msg}"

                            Errors.ExPartial
                        | Error e -> fail e
                        | Ok Board.Written ->
                            printfn "set %d field(s) on %s in one aliased mutation:" (List.length writes) ref.Canonical

                            for field, write in writes do
                                printfn "  %s = %s" field (match write with | Board.Set v -> v | Board.Clear -> "<cleared>")

                            ExitGreen
                        | Ok Board.Deferred ->
                            printfn
                                "set-field --batch %s — QUEUED all %d field(s) (budget exhausted; flush replays the batch)"
                                ref.Canonical
                                (List.length writes)

                            Errors.ExRate
                        | Ok Board.NotOnBoard ->
                            eprint $"fsgg-coord-engine: %s{ref.Canonical} is not an item on this board — nothing written."
                            ExitError
        | _ ->
            eprint "fsgg-coord-engine: set-field --batch takes <ref> followed by one or more Field=Value pairs."
            ExitError

    let setField (ctx: Context) (opts: Options) : int =
        if opts.Batch then
            setFieldBatchCmd ctx opts
        else

        match opts.Args with
        | [ refArg; field; value ] ->
            match parseRef ctx refArg, worker opts with
            | Error msg, _ ->
                eprint $"fsgg-coord-engine: %s{msg}"
                ExitError
            | _, Error c -> c
            | Ok ref, Ok w ->
                // The `Blocked by` gate runs FIRST — before any board read — so a refused value spends no
                // GraphQL, and it produces the canonical value (or `Clear`) the write below emits.
                match gateField ref field value with
                | Error rc -> rc
                | Ok write ->

                // AC1 (.github#2079): `set-field <ref> Status Blocked` is `release --status Blocked`'s
                // other door onto the same park invariant — refused BEFORE any write when the row would
                // land with neither a non-empty `Blocked by` field nor a `Blocked on: human/...` sentinel.
                // A no-op for every other field/value pair (`requireCoherentParkIfBlocked` itself is a
                // no-op unless the resolved status is `Blocked`).
                match requireCoherentParkIfBlocked ctx ref (if field = "Status" then Reads.statusOfName value else None) with
                | Error rc -> rc
                | Ok() ->

                match Board.bootstrapCached ctx.Transport ctx.Owner ctx.Title with
                | Error e -> fail e
                | Ok board ->
                    match Board.boardWrite ctx.Transport board ref.Owner ref.Repo ref.Number field write w.Id with
                    | Error e -> fail e
                    | Ok Board.Written ->
                        printfn
                            "set %s %s = %s"
                            ref.Canonical
                            field
                            (match write with
                             | Board.Set v -> v
                             | Board.Clear -> "<cleared>")
                        ExitGreen
                    | Ok Board.Deferred ->
                        printfn
                            "set %s %s = %s — QUEUED (budget exhausted; flush replays it)"
                            ref.Canonical
                            field
                            (match write with
                             | Board.Set v -> v
                             | Board.Clear -> "<cleared>")

                        Errors.ExRate
                    | Ok Board.NotOnBoard ->
                        eprint $"fsgg-coord-engine: %s{ref.Canonical} is not an item on this board — nothing written."
                        ExitError
        | _ ->
            eprint "fsgg-coord-engine: set-field takes <ref> <field> <value> (an empty value clears)."
            ExitError

    let child (ctx: Context) (opts: Options) : int =
        match opts.Args with
        | [ parentArg; childArg ] ->
            match parseRef ctx parentArg, parseRef ctx childArg with
            | Error msg, _
            | _, Error msg ->
                eprint $"fsgg-coord-engine: %s{msg}"
                ExitError
            | Ok parent, Ok childRef ->
                match Reads.restId ctx.Transport childRef.Owner childRef.Repo childRef.Number with
                | Error e -> fail e
                | Ok childId ->
                    // #320: the existing-links read must FAIL CLOSED. Swallowing it would make "I could not
                    // reach the API" look exactly like "the edge is not there" — `child` would POST, collect
                    // a 422, and blame the token. Idempotency is BY ID, not by number: two repos can each
                    // have an issue #7, so a re-run of a worker's close-out never has to reason about the edge.
                    match Reads.subIssueIds ctx.Transport parent.Owner parent.Repo parent.Number with
                    | Error e ->
                        eprint
                            $"fsgg-coord-engine: child: cannot read %s{parent.Short}'s sub-issues (%s{Errors.explain e}) — refusing to guess whether %s{childRef.Short} is already linked."
                        ExitError
                    | Ok existing when List.contains childId existing ->
                        printfn "%s is already a sub-issue of %s — nothing to do" childRef.Short parent.Short
                        ExitGreen
                    | Ok _ ->
                        match Writes.child ctx.Transport parent childId with
                        // A failed link surfaces the API's OWN diagnosis — a 422 (already linked, or a
                        // cross-repo link GitHub refuses) and a 403 (no `issues: write`) are different
                        // problems with different fixes, and guessing one would send the worker at the wrong.
                        | Error e -> fail e
                        | Ok() ->
                            printfn "linked %s as a sub-issue of %s" childRef.Short parent.Short
                            ExitGreen
        | _ ->
            eprint "fsgg-coord-engine: child takes <parent-ref> <child-ref>."
            ExitError

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
    /// - A claim on a **CLOSED** issue. `openIssues` is open-only — and so is the scheduler: `Scan.snapshot`
    ///   SWEEPS a closed candidate with no marker read at all (#520). The two agree, deliberately.
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
                    |> Option.defaultValue ref.Repo)

        match targetPathRepo, Reads.openIssues ctx.Transport ref.Owner ref.Repo with
        | Error e, _ -> Error e
        | _, Error e -> Error e
        | Ok targetPathRepo, Ok issues ->
            // THE TOKEN FILTER RUNS FIRST, AND IT IS WHAT MAKES THE MARKER READS AFFORDABLE. It is pure:
            // the bodies arrived on the list read above, `TouchSet.parse`/`conflicts` touch no network, and
            // a row whose declaration cannot collide is discarded before anything is spent on it.
            //
            // Sorted by number so the collision list is deterministic. The old order was the board's row
            // order, which is the project's, which no caller can predict — and `widen --json`'s
            // `collisions` array is a machine contract.
            let colliding =
                issues
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
                            match TouchSet.conflicts ts (TouchSet.parse body) with
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
                            let otherPathRepo = m.PathRepo |> Option.defaultValue other.Repo
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
                                        activeCollisions
                                            ctx
                                            opts
                                            ref
                                            (Some(held.PathRepo |> Option.defaultValue ref.Repo))
                                            (TouchSet.parse rewritten.Body)
                                    with
                                    | Error e -> fail e
                                    | Ok collisions ->
                                        match Writes.widen ctx.Transport held rewritten with
                                        | Error e -> fail e
                                        | Ok() ->
                                            let paths = String.Join(", ", proposed.Tokens)

                                            // #1517 — THE RENDER MODE IS HONOURED HERE, and it was not before. `--json`
                                            // is `Global` in `scopeOf` and `command-contract` advertises it on both
                                            // verbs, so the parser accepted it, the residue rule had nothing to refuse,
                                            // and this renderer then printed human prose and exited 0 — #867/#991's
                                            // "accepted and ignored" defect, arriving through the one door that rule
                                            // cannot watch. A driver that widens a touch-set had to scrape
                                            // `widened <ref> → Paths: a, b` out of stdout and the overlap verdict out
                                            // of STDERR to learn what it had just done.
                                            //
                                            // The TEXT projection below is byte-identical to what it has always been,
                                            // deliberately: every existing recipe reads it. This is an addition.
                                            //
                                            // The JSON object cannot be written HERE, though the human receipt is: it
                                            // carries the collision list, and whether each colliding holder was
                                            // successfully NOTIFIED is not known until the notify loop has run. So the
                                            // Json arm emits ONE object at the end of whichever branch it takes, and
                                            // never a partial receipt first — a machine consumer gets one document per
                                            // invocation or none.
                                            match opts.Render with
                                            | Json -> ()
                                            | Text -> printfn "%s %s → Paths: %s" past ref.Short paths

                                            // Declaration time is the cheap moment to learn that editing a kit source
                                            // obliges a re-digest (#469); OBSERVED off the tree, advisory, never fatal.
                                            // It is stderr-only, so it cannot corrupt the JSON projection.
                                            KitDigest.digestWarn ()

                                            let receipt (collisions: PathCollision list) : string =
                                                renderPathUpdateJson
                                                    { Ref = ref
                                                      Worker = w.Id
                                                      Kind = past
                                                      Paths = proposed.Tokens
                                                      Collisions = collisions }

                                            match collisions with
                                            | [] ->
                                                match opts.Render with
                                                | Json -> printfn "%s" (receipt [])
                                                | Text ->
                                                    printfn "DISJOINT — the updated touch-set clears every live claim in %s/%s (#353)." ref.Owner ref.Repo

                                                ExitGreen
                                            | collisions ->
                                                // The notify is the part a worker cannot do alone. A post that fails is
                                                // reported, but the collision still stands — it does not become DISJOINT.

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

                                                            $"heads up: I %s{past} %s{ref.Short} to `Paths: %s{paths}`, which overlaps your touch-set here (%s{toksText}). %s{origin}. This is a TRANSIENT overlap — the scheduler already sequences us, and it clears the moment one claim drops, so you may not need to do anything. To unblock the board sooner: narrow with `set-paths`, or split one touch-set so we are disjoint. Only add a `Blocked by` edge if there is a real DEPENDENCY — my work must be authored against your LANDED result, not merely the same files — because that edge is durable and nothing re-checks it once the overlap is gone. Reply here."

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
                                                | Json -> printfn "%s" (receipt notified)
                                                | Text -> ()

                                                // A real same-repo collision exits non-zero (engine ExitContended=6;
                                                // bash's literal 1 disposed on the record, ADR-0040 §5). UNCHANGED by
                                                // #1517: the renderer was the bug, the verbs' semantics were not.
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
                    let pathRepoOf (r: Ref) = Map.tryFind r scopes |> Option.defaultValue r.Repo
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
                                match TouchSet.scopedConflicts ra.Owner (pathRepoOf ra) rb.Owner (pathRepoOf rb) tsa tsb with
                                | [] ->
                                    printfn "DISJOINT — %s and %s share no touch-set token; they may run in parallel." ra.Short rb.Short
                                    ExitGreen
                                | pairs ->
                                    printfn "OVERLAP — %s and %s share %s" ra.Short rb.Short (sharedTokenText (sharedTokens pairs))
                                    ExitContended

        | _ ->
            eprint "fsgg-coord-engine: overlap needs <ref> --active, or two refs: overlap <ref-a> <ref-b>."
            ExitError

    let say (ctx: Context) (opts: Options) : int =
        match oneArg opts "say: an issue ref", worker opts with
        | Error c, _
        | _, Error c -> c
        | Ok arg, Ok w ->
            match opts.ToWorker, opts.Message with
            | None, _ ->
                eprint "fsgg-coord-engine: say needs --to <worker>."
                ExitError
            | _, None ->
                eprint "fsgg-coord-engine: say needs --message <text>."
                ExitError
            | Some toW, Some msg ->
                // NORMALIZE the target to a worker id. Ids are slug()'d at creation and `inbox` matches
                // `.to` by EXACT string, so an unslugged `--to Heron-B71` posts a message its recipient
                // (heron-b71) can never see. `*` — anyone holding the item — is the one literal that is
                // not a worker id. Slug via Identity.slug, the SAME normalization that creates ids (#485).
                let normalizedTo =
                    if toW = "*" then Ok "*"
                    else
                        match Identity.slug toW with
                        | "" -> Error $"say: --to '%s{toW}' is not a usable worker id."
                        | s -> Ok s

                match normalizedTo with
                | Error e ->
                    eprint $"fsgg-coord-engine: %s{e}"
                    ExitError
                | Ok toSlug ->
                    match parseRef ctx arg with
                    | Error m ->
                        eprint $"fsgg-coord-engine: %s{m}"
                        ExitError
                    | Ok ref ->
                        // An unslugged target reached the wrong id silently; say we changed it.
                        if toSlug <> toW then
                            eprint $"fsgg-coord-engine: addressing worker '%s{toSlug}' (normalized from '%s{toW}')."
                        // No lock required — the worker who most needs to speak is the one who just lost a race.
                        match Writes.say ctx.Transport (WorkerId w.Id) (WorkerId toSlug) ref msg with
                        | Error e -> fail e
                        | Ok() ->
                            printfn "said to %s on %s" toSlug ref.Short
                            ExitGreen


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
    let inbox (ctx: Context) (opts: Options) : int =
        match worker opts with
        | Error c -> c
        | Ok w ->
            match Board.bootstrapCached ctx.Transport ctx.Owner ctx.Title with
            | Error e -> fail e
            | Ok board ->
                match Scan.board ctx.Transport Cache.Reconciling ctx.Owner ctx.Title board.Number with
                | Error e -> fail e
                | Ok rows ->
                    let scoped =
                        rows |> List.filter (fun r -> not r.IsPullRequest) |> Scan.scope opts.Repo

                    // #979 — a mailbox that reports "no new messages" over a repo it never found is the
                    // one failure this verb must not have. Like `who`, the fallback below fails closed on
                    // a repo that does not exist; the advisory is what names the cause as a spelling
                    // rather than an outage.
                    scoped.Advisory |> Option.iter eprint

                    let boardRows = scoped.Rows

                    // The repos an off-board claim could live in — derived from the board scan in hand, with
                    // the same `--repo`-names-no-board-item fallback `who` uses, so a message on an issue that
                    // never reached the board is still found.
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

                    // Off-board open issues, WITH their bodies (as `who` keeps them): one list read serves both
                    // the candidate scan and the `Rooms:` extraction below, so widening the subject set to
                    // coordination rooms (ADR-0051) costs no extra body read — only the message read per room.
                    let offBoard =
                        System.Collections.Generic.Dictionary<string * string * int, Reads.IssueBodyRead>()

                    for (o, r) in repos do
                        if failure.IsNone then
                            // FAILS CLOSED (#461): an unreadable scan is never an empty one. A mailbox that
                            // swallowed a failed issue-list read would report "no new mail" over a claim whose
                            // messages it never looked for.
                            match Reads.openIssues ctx.Transport o r with
                            | Error e -> failure <- Some e
                            | Ok issues ->
                                for issue in issues do
                                    offBoard.[(o, r, issue.Number)] <- issue.Body

                    let inProgressRefs =
                        boardRows
                        |> List.filter (fun r -> r.Status = BoardStatus.InProgress)
                        |> List.map (fun r -> r.Ref.Owner, r.Ref.Repo, r.Ref.Number)
                        |> Set.ofList

                    // ADR-0051 — the subject set WIDENS to the coordination ROOMS the in-flight items
                    // reference. A worker is in a room by holding a claim on an item that carries `Rooms: #R`,
                    // and folding #R in here — behind the SAME per-worker cursor — is the whole mechanism the
                    // ADR calls "the one real code delta". Derived from the bodies already in hand, so the
                    // added cost is one message read per DISTINCT room, the bound ADR-0051 sets. A room may
                    // live in a repo outside `--repo` scope; `Reads.messages` takes owner/repo/number
                    // directly, so a cross-repo room is still reached. The recipient filter below is unchanged,
                    // so a worker still sees only messages addressed to it or broadcast (`*`) — exactly as for
                    // items, which is why this widens the SUBJECTS without broadening delivery.
                    let rooms =
                        offBoard
                        |> Seq.collect (fun kv ->
                            let (o, r, _) = kv.Key

                            match kv.Value with
                            // .github#1794: a body we could not read names no rooms we know of — which is
                            // not the claim that it names none. It costs a subject, never a delivery: the
                            // ITEM itself stays in `candidates` (its key is in `offBoard.Keys` regardless),
                            // so no message on the item is lost; only a room it might have referenced is
                            // unreachable through this row, and any OTHER item referencing that room still
                            // folds it in.
                            | Reads.BodyUnread _ -> []
                            | Reads.BodyRead body -> Rooms.parse o r body)
                        |> Seq.map (fun rf -> rf.Owner, rf.Repo, rf.Number)
                        |> Set.ofSeq

                    let candidates =
                        if failure.IsSome then
                            []
                        else
                            Seq.append (Seq.append offBoard.Keys inProgressRefs) rooms
                            |> Seq.distinct
                            |> Seq.sortBy (fun (o, r, n) -> o, r, n)
                            |> List.ofSeq

                    // The cursor is the high-water mark. `maxId` advances past EVERY message seen — even one
                    // not addressed to us, even a broadcast we sent — so mail already read never re-surfaces;
                    // `delivered` is the subset the cursor's advance does NOT gate on: new (> the old cursor),
                    // not our own, and addressed to us or to everyone.
                    let since = Cache.inboxCursor w.Id
                    let mutable maxId = since
                    let delivered = ResizeArray<string * Reads.Message>()

                    for (o, r, n) in candidates do
                        if failure.IsNone then
                            // A FAILED READ IS A LOST MESSAGE, not an empty mailbox — fail closed over the
                            // in-flight set exactly as `who` does, so an unread warning is never reported as
                            // "no new mail".
                            match Reads.messages ctx.Transport o r n with
                            | Error e -> failure <- Some e
                            | Ok msgs ->
                                for m in msgs do
                                    if m.Id > maxId then
                                        maxId <- m.Id

                                    if m.Id > since && m.From <> w.Id && (m.To = w.Id || m.To = "*") then
                                        delivered.Add($"%s{r}#%d{n}", m)

                    match failure with
                    | Some e -> fail e
                    | None ->
                        // The default consumes what it shows (advance the cursor); `--peek` shows it and
                        // leaves the cursor, so the same mail is still new next time.
                        if not opts.Peek then
                            Cache.putInboxCursor w.Id maxId

                        let delivered = List.ofSeq delivered

                        match opts.Render with
                        | Json -> printfn "%s" (renderInboxJson delivered)
                        | Text ->
                            if List.isEmpty delivered then
                                printfn "no new messages for worker %s." w.Id
                            else
                                for (item, m) in delivered do
                                    printfn "── %s  %s → %s  (%s)" item m.From m.To m.At
                                    printfn "%s" m.Text
                                    printfn ""

                                // Say the cursor was NOT advanced, on stderr, so a `--peek` piped to a machine
                                // consumer still shouts that this read did not consume the mail it showed.
                                if opts.Peek then
                                    eprint "fsgg-coord-engine: --peek — cursor not advanced."

                        ExitGreen

    /// `room open --over N,M` — open a coordination room over a contended cluster (ADR-0051). Creates the
    /// room ISSUE (off the board — coordination scaffolding, not deliverable work) and writes a `Rooms:`
    /// back-reference onto each named item, so their holders share the room's channel via `say`/`inbox`.
    /// No lock is taken or required: a room is opened over other workers' items, exactly as `say` speaks
    /// to them.
    let roomOpen (ctx: Context) (opts: Options) : int =
        match worker opts with
        | Error c -> c
        | Ok w ->
            if List.isEmpty opts.Over then
                eprint "fsgg-coord-engine: room open needs --over N,M (the items to open the room over)"
                ExitError
            else
                let parsed = opts.Over |> List.map (fun t -> t, parseRef ctx t)
                let bad = parsed |> List.choose (fun (t, r) -> match r with Error m -> Some(t, m) | Ok _ -> None)

                match bad with
                | _ :: _ ->
                    for (t, msg) in bad do
                        eprint $"fsgg-coord-engine: --over '%s{t}': %s{msg}"

                    ExitError
                | [] ->
                    let members =
                        parsed
                        |> List.choose (fun (_, r) ->
                            match r with
                            | Ok ref -> Some ref
                            | Error _ -> None)
                        |> List.distinct

                    match members with
                    | [] ->
                        eprint "fsgg-coord-engine: room open needs at least one item"
                        ExitError
                    | first :: _ ->
                        // A room is INTRA-REPO (ADR-0027 §5, which ADR-0051 amends): every member shares one
                        // repo, and the room lives there with them. This is not a mere convenience — it is what
                        // makes the derived close (ADR-0051 §4) SOUND. That close scans the room's own repo for
                        // surviving referrers; a member in another repo would be invisible to it, so completing
                        // the last same-repo member would close the room while the cross-repo member is still
                        // open (the one error §4 must never make). Refusing a cross-repo `--over` here is what
                        // keeps every tool-created room within a single repo, so the repo-local scan is exact.
                        // A cross-repo knot is cross-repo-coordination's domain (ADR-0001), not a room's.
                        let owner, repo = first.Owner, first.Repo

                        let strangers =
                            members |> List.filter (fun m -> m.Owner <> owner || m.Repo <> repo)

                        if not (List.isEmpty strangers) then
                            let named = strangers |> List.map (fun m -> $"%s{m.Owner}/%s{m.Repo}#%d{m.Number}") |> String.concat ", "

                            eprint
                                $"fsgg-coord-engine: room open is intra-repo (ADR-0027 §5) — these members are outside %s{owner}/%s{repo}: %s{named}. A cross-repo knot is cross-repo-coordination's domain (ADR-0001), not a room's."

                            ExitError
                        else

                        let memberList = members |> List.map (fun r -> r.Short) |> String.concat ", "
                        let title = $"coordination room over %s{memberList}"

                        let bodyText =
                            $"Coordination room (ADR-0051), opened by worker %s{w.Id} over %s{memberList}.\n\n"
                            + "Workers holding these items share this room's channel — reach it with `say` on this "
                            + "issue and read it with `inbox`. Membership and lifecycle are DERIVED from the "
                            + "`Rooms:` back-references on the items: this room closes itself when every referenced "
                            + "item is done. Record any touch-set agreement as a `widen` on the real items, not here.\n\n"
                            + "Paths: none"

                        match Writes.createRoom ctx.Transport owner repo title bodyText with
                        | Error e -> fail e
                        | Ok room ->
                            // Every member shares the room's repo (enforced above), so the back-reference is a
                            // bare `#n` — which `Rooms.parse`, defaulting a bare ref to the member's own repo,
                            // resolves to exactly this room.
                            let roomToken = $"#%d{room.Number}"

                            let mutable failure: Errors.IoError option = None

                            for m in members do
                                if failure.IsNone then
                                    match Reads.issueBody ctx.Transport m.Owner m.Repo m.Number with
                                    | Error e -> failure <- Some e
                                    | Ok mbody ->
                                        // Idempotent: a member already referencing the room keeps its one line
                                        // rather than growing a duplicate (the union in `Rooms.parse` would
                                        // collapse it anyway, but a clean body is worth the read).
                                        if Rooms.parse m.Owner m.Repo mbody |> List.contains room then
                                            ()
                                        else
                                            match Writes.writeRoomRef ctx.Transport m mbody roomToken with
                                            | Error e -> failure <- Some e
                                            | Ok() -> ()

                            match failure with
                            | Some e ->
                                eprint
                                    $"fsgg-coord-engine: room %s{room.Short} was created, but a `Rooms:` back-reference could not be written: %s{Errors.explain e}. The room exists; wire the remaining members by hand or re-run."

                                Errors.exitCode e
                            | None ->
                                printfn "opened room %s over %s" room.Short memberList
                                ExitGreen

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

                        match verdict with
                        | Verdict.NoVerdict _ -> ExitNoVerdict
                        | Red _ -> ExitRed
                        | Green _ ->
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

    /// resolve_repo (bash): a `--repo` value is a registry short-id (`sdd`), an `owner/repo`, or a literal
    /// A repo token → the repo NAME board rows carry. THE MAP NOW LIVES IN THE PARSER (`Options.resolveRepo`,
    /// #962), because that is the one funnel every verb reaches; this alias keeps the call sites that resolve
    /// something the parser never saw — a GIT REMOTE (`scopedRepo`, `defaultRepoScope`) or a POSITIONAL repo
    /// arg (`issues <repo>`) — reading in the local vocabulary.
    ///
    /// An explicit `--repo` is ALREADY resolved by the time it reaches this module, so it needs no call here.
    /// The remaining ones are idempotent and harmless: they resolve a name that may already be resolved.
    let private resolveRepo (raw: string) : string = Options.resolveRepo raw

    // ---- #480/#430: the repo a command scopes to — the checkout you are STANDING IN ------------------
    // Defined ABOVE verify-paths (and the worker-command `scopedRepo` below) because BOTH read the same
    // signal: the git remote of the checkout. verify-paths' #430 default (neither `--repo` nor `--issue`)
    // resolves the repo exactly the way `next`/`take`/`batch`/`who` do — one shared reader, one behaviour.

    /// A GitHub remote URL → its `owner/repo`, or `None` when the URL is not a GitHub remote naming
    /// exactly one owner/repo. Handles every form `git config remote.origin.url` yields:
    /// `https://github.com/FS-GG/x(.git)`, `git@github.com:FS-GG/x(.git)`, `ssh://…/FS-GG/x.git`. A bare
    /// host or a nested path is NOT a scope — bash's `*/*/*|*/` guard, held here as an exact one-slash
    /// requirement — so a malformed remote can never be silently read as a repo.
    let parseGitHubSlug (url: string) : string option =
        match url.IndexOf("github.com", StringComparison.OrdinalIgnoreCase) with
        | -1 -> None
        | idx ->
            let mutable s = url.Substring(idx + "github.com".Length).TrimStart(':', '/')

            if s.EndsWith(".git", StringComparison.OrdinalIgnoreCase) then
                s <- s.Substring(0, s.Length - 4)

            s <- s.TrimEnd('/')
            // owner/repo EXACTLY — one slash, both sides non-empty.
            let parts = s.Split('/')

            if parts.Length = 2 && parts.[0] <> "" && parts.[1] <> "" then
                Some s
            else
                None

    /// scope_repo (#480): the current checkout's `owner/repo`, read from `git config --get
    /// remote.origin.url`. FREE and offline — deliberately NOT `gh repo view` — so resolving the scope
    /// cannot burn GraphQL budget, and an exhausted budget can never be misreported as "you are not in a
    /// checkout" (#430). `None` when there is no readable `origin` that parses as `owner/repo`.
    let private gitRemoteRepo () : string option =
        try
            let psi = ProcessStartInfo("git", "config --get remote.origin.url")
            psi.RedirectStandardOutput <- true
            psi.RedirectStandardError <- true
            psi.UseShellExecute <- false
            use p = Process.Start psi
            let url = p.StandardOutput.ReadToEnd().Trim()
            p.WaitForExit()

            if p.ExitCode <> 0 then None else parseGitHubSlug url
        with _ ->
            None

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
                            let regenerated, undeclared =
                                if List.isEmpty drift then
                                    [], []
                                else

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

                                drift |> List.partition (fun f -> Set.contains f subtractable)

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

                            if List.isEmpty undeclared then
                                printfn "FSGG-PATHS OK — PR #%d stays inside the touch-set declared by %s." pr issue.Short
                                reportRegenerated ()
                                combine ExitGreen
                            else
                                printfn "FSGG-PATHS DRIFT — PR #%d changes files outside the touch-set declared by %s:" pr issue.Short

                                printfn "  undeclared (review):"

                                for f in undeclared do
                                    printfn "    %s" f

                                reportRegenerated ()
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

    /// `FSGG_COORD_CHORE_LOCKS` → the injected chore-lock roster a vendored tenant runs against a non-FS-GG
    /// board. Comma-separated fully-qualified refs `owner/repo#n`; a token that does not parse is DROPPED,
    /// never thrown — a dropped lock costs a chore not offered (fail-closed, condition 1), the same answer an
    /// absent lock already gives, so a typo degrades to the default rather than crashing the caller's real
    /// command. Parsing lives HERE, not in the pure `Options.choreLockRef`, because env is IO and ADR-0042's
    /// constraint is only that no `repos.yml` READER ship to a receiver — env DOES reach the receiver, so a
    /// per-deployment roster injected by env is exactly the seam a file reader could not be. The repo is
    /// canonicalised on the way in (`resolveRepo`), so the stored ref is already in the spelling the CAS
    /// compares. The pattern is `parseRef`'s own fully-qualified arm (line ~166), kept in step by eye.
    let parseChoreLocks (raw: string) : Ref list =
        raw.Split(',')
        |> Array.choose (fun tok ->
            let m = Text.RegularExpressions.Regex.Match(tok.Trim(), @"^([\w.-]+)/([\w.-]+)#(\d+)$")

            if m.Success then
                Some(
                    { Owner = m.Groups.[1].Value
                      Repo = resolveRepo m.Groups.[2].Value
                      Number = int m.Groups.[3].Value }
                    : Ref
                )
            else
                None)
        |> Array.toList

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

    // ---- #480: the repo a WORKER command scopes to — the one you are STANDING IN --------------------
    // `parseGitHubSlug` + `gitRemoteRepo` are defined above verify-paths (its #430 default reads the same
    // remote); only the worker-command wrapper lives here.

    /// The DEFAULT scope of a worker command (`next`/`take`/`batch`/`who`) when no `--repo` spells one out:
    /// the checkout you are standing in (#480). Reconcilers (`ready`) do NOT call this — /check-board runs a
    /// bare `ready --all` to reconcile the WHOLE board, so narrowing it to the checkout would silently shrink
    /// the reconciler to one repo, trading this scope bug for a strictly worse one in the very tool that
    /// exists to catch it.
    ///
    /// DEFAULTING IS ALL THIS DOES NOW, AND THE SPLIT IS THE FIX (#962). It used to also RESOLVE an explicit
    /// `--repo`, and those are two different questions:
    ///
    ///   1. "no `--repo` — which repo do I mean?"  → per-verb. A reconciler must answer "the whole org".
    ///   2. "`--repo sdd` — which repo is that?"   → universal. `sdd` is `FS.GG.SDD` for every verb alive.
    ///
    /// Conflating them made the ONLY available choice both-or-neither, so excluding `ready` to get (1) right
    /// silently dropped (2) as well: `ready --repo governance` compared `"governance"` against the row's
    /// `"FS.GG.Governance"` verbatim, matched nothing, and printed `[]` with exit 0 over a 50-item board —
    /// indistinguishable from an empty queue, in the read `/pnext-item` §1 makes the truth-check for a park.
    /// (2) is now the PARSER's, for every verb at once, so this answers (1) alone.
    let private scopedRepo (opts: Options) : string option =
        match opts.Repo with
        // Already resolved by `parse`. Resolving again would be idempotent, but it would also re-plant the
        // habit that grew this bug: resolution living at each site that remembers to ask for it.
        | Some r -> Some r
        // A git remote is not an argument, so the parser never saw it — this one is genuinely ours.
        | None -> gitRemoteRepo () |> Option.map resolveRepo

    /// #548: the repo a BARE `<n>` ref resolves against — `Context.DefaultRepo`. An explicit `--repo` wins
    /// (resolved through the same short-id map, so `--repo rendering` names one queue in both argument
    /// positions); otherwise the checkout you are standing in, exactly as `take`/`batch` default.
    ///
    /// THE OWNER CHECK IS THE WHOLE AMBIGUITY CRITERION, and it is why this is not just `scopedRepo`.
    /// `resolveRepo` throws the owner away, so in a NON-FS-GG checkout `scopedRepo` happily yields
    /// `acme/thing` → `thing`, and a bare `506` would then silently address `FS-GG/thing#506` — an issue in
    /// a repo the caller is not standing in and may not have meant to exist. Comparing the remote's owner
    /// against the board's and yielding `None` on a mismatch is what keeps a bare number a hard error
    /// outside the org, which the issue names as the one thing to get right.
    /// The checkout's `owner/repo` → the repo a bare `<n>` resolves against, or `None` when that slug's
    /// owner is not the board's. PUBLIC PURELY TO BE TESTED — `defaultRepoScope` below wraps it with the
    /// process call that reads the remote, which a unit test cannot drive. Same idiom as `parseGitHubSlug`.
    let defaultRepoForOwner (owner: string) (slug: string) : string option =
        match slug.Split('/') with
        | [| o; r |] when String.Equals(o, owner, StringComparison.OrdinalIgnoreCase) -> Some(resolveRepo r)
        | _ -> None

    let private defaultRepoScope (owner: string) (opts: Options) : string option =
        match opts.Repo with
        | Some r -> Some(resolveRepo r)
        | None -> gitRemoteRepo () |> Option.bind (defaultRepoForOwner owner)

    /// Run an IO command. Every one goes through here so the token check, the transport lifetime, and the
    /// defect boundary are in one place.
    // ---- the plumbing commands (#418 board/item cache, case 10) ---------------------------------------
    //
    // These expose the resolver cache the corpus counts: `bootstrap` pays the two GraphQL points once and
    // day-caches the field/option id map; `board`/`field-id`/`option-id` read it back for ZERO; `item-id`
    // resolves an issue's board item id in ONE call and then keeps it forever. They are board-global — no
    // #480 repo scoping — because the board is one board and its ids are the same from any checkout.

    let bootstrapCmd (ctx: Context) (opts: Options) : int =
        // `--refresh` drops the day-cache so the next resolve is a real one — the remedy Snapshot.fs points
        // a worker at when the board schema changed under a warm cache.
        if opts.Fresh then
            Cache.dropBoardMap ctx.Owner ctx.Title

        match Board.bootstrapCached ctx.Transport ctx.Owner ctx.Title with
        | Error e -> fail e
        | Ok board ->
            printfn "bootstrapped board #%d '%s' in %s (%d fields)" board.Number board.Title board.Owner (Map.count board.Fields)
            ExitGreen

    let boardCmd (ctx: Context) : int =
        match Board.bootstrapCached ctx.Transport ctx.Owner ctx.Title with
        | Error e -> fail e
        | Ok board ->
            printfn "%s" (Board.boardToJson board)
            ExitGreen

    let fieldId (ctx: Context) (opts: Options) : int =
        match opts.Args with
        | [ field ] ->
            match Board.bootstrapCached ctx.Transport ctx.Owner ctx.Title with
            | Error e -> fail e
            | Ok board ->
                match Map.tryFind field board.Fields with
                | Some f ->
                    printfn "%s" f.Id
                    ExitGreen
                | None ->
                    eprint $"fsgg-coord-engine: no board field named '%s{field}'."
                    ExitError
        | _ ->
            eprint "fsgg-coord-engine: field-id takes <field>."
            ExitError

    let optionId (ctx: Context) (opts: Options) : int =
        match opts.Args with
        | [ field; option ] ->
            match Board.bootstrapCached ctx.Transport ctx.Owner ctx.Title with
            | Error e -> fail e
            | Ok board ->
                match Map.tryFind field board.Fields with
                | Some f ->
                    match f.Type with
                    | Board.SingleSelect options ->
                        match Map.tryFind option options with
                        | Some id ->
                            printfn "%s" id
                            ExitGreen
                        | None ->
                            eprint $"fsgg-coord-engine: field '%s{field}' has no option '%s{option}'."
                            ExitError
                    | _ ->
                        eprint $"fsgg-coord-engine: field '%s{field}' is not a single-select field."
                        ExitError
                | None ->
                    eprint $"fsgg-coord-engine: no board field named '%s{field}'."
                    ExitError
        | _ ->
            eprint "fsgg-coord-engine: option-id takes <field> <option>."
            ExitError

    let itemIdCmd (ctx: Context) (opts: Options) : int =
        match opts.Args with
        | [ refArg ] ->
            match parseRef ctx refArg with
            | Error msg ->
                eprint $"fsgg-coord-engine: %s{msg}"
                ExitError
            | Ok ref ->
                match Board.bootstrapCached ctx.Transport ctx.Owner ctx.Title with
                | Error e -> fail e
                | Ok board ->
                    match Board.itemIdCached ctx.Transport board ref.Owner ref.Repo ref.Number with
                    | Error e -> fail e
                    | Ok(Some id) ->
                        printfn "%s" id
                        ExitGreen
                    // #421: a SUCCESSFUL read that found nothing is the only path that says "not on board".
                    // It is a definite answer, not a failure — but it is not an id, so it exits non-zero.
                    | Ok None ->
                        eprint $"fsgg-coord-engine: %s{ref.Short} is not an item on this board."
                        ExitError
        | _ ->
            eprint "fsgg-coord-engine: item-id takes <ref>."
            ExitError

    /// The column `add` writes when the caller names none (.github#1823).
    ///
    /// **`Backlog`, and not `Ready`.** `Backlog` is visible to triage and NOT startable without a
    /// deliberate promotion, which is `drive-board`'s existing backlog-triage contract — it promotes only
    /// evidenced actionable work to `Ready`. Defaulting to `Ready` would auto-schedule work nobody has
    /// read; defaulting to NOTHING was the defect (14 rows in one day, every one found by accident).
    [<Literal>]
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
    let addCmd (ctx: Context) (opts: Options) : int =
        match opts.Args with
        | [ refArg ] ->
            match parseRef ctx refArg with
            | Error msg ->
                eprint $"fsgg-coord-engine: %s{msg}"
                ExitError
            | Ok ref ->
                // THE CLASS GATE, BEFORE THE ROW EXISTS (.github#1651 AC1).
                //
                // `add` is the engine's FIRST sight of an item: the issue itself is written with `gh`, so
                // there is no earlier place a closed vocabulary can be enforced. Two workers in one run,
                // in two repos, wrote `Class: docs` and `Class: enhancement` — both believed they were
                // classing the row, nothing told them the set was closed, and each row then sat on the
                // board reading as UNCLASSED, which ADR-0066 counts as a POSSIBLE defect and which
                // therefore blocked a clean termination read until a human read the body by hand.
                //
                // IT REFUSES; IT DOES NOT CORRECT. Mapping `docs` onto the nearest of three would be the
                // guess #1588's AC3 forbids, wearing a writer's authority instead of a parser's.
                //
                // AND IT FAILS CLOSED (#266): a body we could not READ is not a body we may declare
                // clean. `add` costs one REST read it did not cost before, which is the price of the
                // check being at the only moment the author is still standing there. Nothing has been
                // written when this refuses, so the remedy is: fix the line, re-run `add`. `add` is
                // idempotent (already-on-board is a success), so a retry is free.
                //
                // IT DOES NOT REQUIRE A CLASS. An item with no `Class:` line boards, and `lint`'s
                // `CLASS-UNSET` reports it — refusing THAT would turn "filed but never boarded" into a
                // routine outcome, and an unboarded row is invisible to every scheduler rather than
                // merely untriaged.
                match Reads.issueBody ctx.Transport ref.Owner ref.Repo ref.Number with
                | Error e -> fail e
                | Ok body ->

                match outOfVocabularyClass body with
                | Some detail ->
                    eprint $"fsgg-coord-engine: refusing to board %s{ref.Short} — %s{detail} Fix the body line and re-run `add`."
                    ExitError
                | None ->

                let laneOfOneWarning =
                    match Board.bootstrapCached ctx.Transport ctx.Owner ctx.Title with
                    | Error e -> Some($"could not inspect sibling declarations ({Errors.explain e})")
                    | Ok board ->
                        match Scan.board ctx.Transport Cache.Reconciling ctx.Owner ctx.Title board.Number with
                        | Error e -> Some($"could not inspect sibling declarations ({Errors.explain e})")
                        | Ok rows ->
                            match Scan.snapshot ctx.Transport rows (Some ref.Repo) true None opts.LeaseMinutes with
                            | Error e -> Some($"could not inspect sibling declarations ({Errors.explain e})")
                            | Ok(doc, _) ->
                                match Snapshot.parse doc with
                                | Error errors ->
                                    let detail = sprintf "%A" errors
                                    Some($"could not inspect sibling declarations ({detail})")
                                | Ok request ->
                                    let siblings = filingLaneOfOne ref (TouchSet.parse body) (request.Candidates |> List.map _.Item)
                                    if List.isEmpty siblings then None
                                    else
                                        let names = siblings |> List.map _.Short |> String.concat ", "
                                        Some($"its Paths: declaration strictly contains {names}. A directory token reserves future files beneath it too, so this is a lane of one; narrow the holding declaration or sequence the work.")

                laneOfOneWarning |> Option.iter (fun warning -> eprint $"fsgg-coord-engine: filing advisory for %s{ref.Short} — %s{warning}")

                match Board.bootstrapCached ctx.Transport ctx.Owner ctx.Title with
                | Error e -> fail e
                | Ok board ->

                // AN EXPLICIT `--status` IS CHECKED BEFORE ANYTHING IS WRITTEN, and it costs zero
                // GraphQL: `bootstrapCached` has already resolved the column's options, so the check is
                // free and it precedes the add.
                //
                // Checked here, and not left to the write, because of what the write does with a refusal.
                // The Status write is deliberately NON-FATAL — the row is boarded, so a red would send a
                // filer back to re-run `add` rather than to the field write actually owed — and that is
                // right for a DEFAULT nobody asked for. It is wrong for an instruction: `add --status Redy`
                // would board the row, print the id, note the refusal, and exit 0, leaving a row with NO
                // column at all. The flag added to close "a boarded row invisible to every scheduler"
                // would itself be a way to produce one, on a green exit. `set-field` exits non-zero for
                // the same value, and this must not be the weaker verb it says it is.
                //
                // NOTHING HAS BEEN WRITTEN when this refuses, and the remedy is: fix the value, re-run.
                // `add` is idempotent, so the retry is free — `outOfVocabularyClass` above, exactly.
                let explicitStatus =
                    match opts.Status with
                    | None -> Ok None
                    | Some name ->
                        match Map.tryFind "Status" board.Fields with
                        | Some { Type = Board.SingleSelect options } when Map.containsKey name options -> Ok(Some name)
                        | Some { Type = Board.SingleSelect options } ->
                            let known = options |> Map.keys |> String.concat ", "

                            Error
                                $"'%s{name}' is not a column on this board's `Status` field. Known columns: %s{known}. Nothing was written — fix the value and re-run `add` (it is idempotent, so the retry is free)."
                        | Some _ -> Error "this board's `Status` field is not a single-select, so `--status` cannot name a column on it."
                        | None ->
                            let known = board.Fields |> Map.keys |> String.concat ", "

                            Error $"this board has no `Status` field at all, so `--status` names nothing. Known fields: %s{known}."

                match explicitStatus with
                | Error detail ->
                    eprint $"fsgg-coord-engine: refusing to board %s{ref.Short} — %s{detail}"
                    ExitError
                | Ok explicitStatus ->

                // #2109: `add --status Blocked` is a Status writer, not merely an add with a
                // convenient flag. Establish the coherent-park invariant BEFORE item-add: after it,
                // the otherwise-invalid board row already exists. Reuse the shared gate so the live
                // `Blocked by` field and the human sentinel keep exactly the same meaning as the
                // other explicit Status=Blocked doors.
                match requireCoherentParkIfBlocked ctx ref (if explicitStatus = Some "Blocked" then Some BoardStatus.Blocked else None) with
                | Error c -> c
                | Ok() ->

                match Board.addItem ctx.Transport board ref.Owner ref.Repo ref.Number with
                | Error e -> fail e
                | Ok outcome ->

                // ALREADY THERE IS A SUCCESS, and it exits 0. `add` is the second line of the recipe's
                // filing procedure, so a close-out pass, a retry, or two workers racing the same
                // follow-up all reach it — and none of them is an error. It says so on stderr and puts
                // the id on stdout, so a caller piping it gets an id either way.
                let itemId =
                    match outcome with
                    | Board.AlreadyOnBoard id ->
                        eprint $"fsgg-coord-engine: %s{ref.Short} is already on board '%s{ctx.Title}'."
                        id
                    | Board.AddedToBoard id ->
                        eprint $"added %s{ref.Short} to board '%s{ctx.Title}'."
                        id

                // THE ID GOES OUT BEFORE THE COLUMN IS SETTLED, unconditionally. `add`'s promise is
                // "this issue is on the board, here is its item id", and that promise is already kept
                // by the time we get here. Every Status outcome below is a note ABOUT a row that is
                // boarded; none of them may swallow the id a caller is piping.
                printfn "%s" itemId

                // THE IDENTITY IS RESOLVED HERE, AFTER THE ROW IS BOARDED, AND NEVER BEFORE IT.
                //
                // The deferral queue is keyed on the worker id, so a board write needs one — but #1823
                // explicitly REFUSED to make `add` refuse: *"`add` is called mid-item by a worker who
                // has just found something outside its touch-set, and a refusal at that moment is how a
                // finding ends up in a report instead of on the board."* Resolving before the add would
                // turn a working `add` into a refusal for any caller with no identity ladder, which is
                // that same failure wearing this change's badge. So the column is what degrades, loudly
                // and by name, and `add`'s own promise is untouched.
                //
                // Through `worker`, not `Identity.resolve`, so the #419 shared-session WARNING fires here
                // as it does on every other verb that writes. `add` is now one of them, and a write verb
                // that alone stays silent about a shared id is the one place the warning is missing.
                let write (value: string) (why: string) : int =
                    match worker opts with
                    | Error _ ->
                        eprint
                            $"fsgg-coord-engine: %s{ref.Short} IS on the board (its id is on stdout), but Status was NOT set to '%s{value}' — a board write is queued against a worker id and this process could not derive one. The refusal above says how to fix it; then:  scripts/fsgg-coord set-field %s{ref.Short} Status %s{value}"

                        ExitGreen

                    | Ok w ->

                    let writeOutcome =
                        Board.boardWrite ctx.Transport board ref.Owner ref.Repo ref.Number "Status" (Board.Set value) w.Id

                    match writeOutcome with
                    | Ok Board.Written ->
                        eprint $"fsgg-coord-engine: Status=%s{value} on %s{ref.Short} — %s{why}"
                        ExitGreen
                    | _ ->
                        // Deferred / NotOnBoard / a failed mutation. `boardWriteNote` names what did
                        // NOT land and the exact command that finishes it. The verdict stays green:
                        // the ROW IS BOARDED, which is what `add` was asked to do and what its stdout
                        // now says — and reporting a red here would send a filer back to re-run `add`
                        // rather than to the one-field write that is actually owed. (An explicit
                        // `--status` cannot reach here on a bad VALUE — that was refused above, before
                        // the add — so what remains is genuinely a transport or budget condition.)
                        boardWriteNote ref "Status" value writeOutcome
                        ExitGreen

                match explicitStatus with
                // AC2 — AN EXPLICIT STATUS STILL WINS. `--status` is the caller naming the column, so
                // it is written whatever is there: this is `set-field <ref> Status <S>` reached from
                // `add`, and #1823 makes only the DEFAULT conditional. A flag accepted and then
                // silently declined would be #867's defect, on the flag #867 is about.
                | Some explicit ->
                    write explicit "you named it with --status (an explicit column always wins over the #1823 default)."

                | None ->

                // AC4 — THE IDEMPOTENCE ARM, AND IT IS THE ONLY ARM. Read the column, then prefer
                // whatever is already there. This is what a "just set Status on add" change gets wrong.
                //
                // THE FRESHLY-ADDED CASE USED TO SKIP THIS READ and it was wrong to. The justification was
                // "a new project item has no field values, and #421's guard means `AddedToBoard` only
                // follows a definite not-on-board read" — but `Board.addItem`'s own docstring records the
                // opposite about the mutation: `addProjectV2ItemById` is idempotent SERVER-side and
                // returns the EXISTING item's id for an issue already on the board. So `AddedToBoard`
                // means "the lookup did not find it", never "the item is new" — and that lookup is
                // `projectItems(first: 20)` with no pagination, so a successful read can miss a row that
                // is on the board carrying a live column. One unpaginated miss and the default would have
                // overwritten it. That is the ONE direction this change destroys information, asserted to
                // be impossible rather than made so. It costs one GraphQL point on a once-per-filing verb
                // (#418) to stop asserting it.
                match Board.itemStatus ctx.Transport board ref.Owner ref.Repo ref.Number with
                | Error e ->
                    // #266. NOT MEASURED is not `Ok None`. We could not read the column, so we may
                    // not assert it is empty, and defaulting on an unread column is exactly how
                    // this change would destroy information instead of adding it.
                    eprint
                        $"fsgg-coord-engine: %s{ref.Short} is on the board, but its Status could NOT BE READ (%s{Errors.explain e}) — so the #1823 default was NOT applied. That is a read that did not happen, not an empty column, and defaulting over one would overwrite whatever is really there. Check it:  scripts/fsgg-coord ready --all --repo %s{ref.Repo}"

                    ExitGreen

                | Ok(Some existing) ->
                    eprint
                        $"fsgg-coord-engine: %s{ref.Short} already has Status='%s{statusWireName existing}' — LEFT AS IT IS. The #1823 default only ever fills an EMPTY column, so re-running `add` never walks a row somebody set back to Backlog."

                    ExitGreen

                | Ok None ->
                    write
                        AddDefaultStatus
                        $"the #1823 default, because you named none. The row is ON the board and VISIBLE to triage, but NOT startable: a scheduler takes `Ready`, and promoting it there is a deliberate act. Use `--status <column>` to choose, or `set-field %s{ref.Short} Status Ready` once it is triaged."
        | _ ->
            eprint "fsgg-coord-engine: add takes <ref> (a URL, owner/repo#n, or repo#n)."
            ExitError

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

    let flushCmd (ctx: Context) (opts: Options) : int =
        // READ THE QUEUE FIRST, AND BEFORE ANY BOARD READ. An empty queue needs no board map, and
        // `bootstrapCached` can spend GraphQL — which is precisely what we do not have when a flush is
        // called for. The overwhelmingly common case (nothing pending) must cost nothing.
        match Cache.pending () with
        | Error e -> fail e

        // AN EMPTY QUEUE IS A SUCCESS, and it exits 0. `flush` is a recovery verb: a close-out pass, a
        // retry, or a worker following the `EX_RATE` advice on a budget that recovered before they got
        // here all reach it, and none of them is an error.
        | Ok [] ->
            printfn "flush — nothing pending."
            ExitGreen

        | Ok entries when opts.DryRun ->
            // WHICH BOARD IS THIS OWED TO? A dry run is the read that must work when NO board read can — an
            // exhausted budget is the only reason a queue exists — so "which board?" has to be answerable
            // exactly here (#966). It still costs zero GraphQL: the target is recorded ON the entry (#882),
            // and `ctx.Owner`/`ctx.Title` are the environment's, so this compares two things already in hand.
            let here = ctx.Owner, ctx.Title

            let elsewhere (e: Cache.Deferred) =
                match e.Board with
                | Some b when not (Cache.sameBoard b here) -> Some b
                | _ -> None

            printfn
                "flush --dry-run — %d write(s) queued, NOT replayed. A flush here writes %s/%s:"
                (List.length entries)
                ctx.Owner
                ctx.Title

            for e in entries do
                // A SKIP AND A REPLAY MUST NOT RENDER ALIKE, which is the whole of this item: the engine has
                // told a skip from a drop since #963, and every entry printed here looked identical anyway.
                let note =
                    match elsewhere e with
                    | Some(o, t) -> sprintf "  -> queued against %s/%s; a flush HERE would SKIP it" o t
                    | None ->
                        match e.Board with
                        // Pre-#882: no board recorded, replayed against the current one — the behaviour it
                        // was queued under. Said out loud, because it is the one entry whose target is a
                        // default rather than a fact.
                        | None -> "  -> no board recorded (pre-#882); would replay against this board"
                        | Some _ -> ""

                printfn
                    "  %s %s = %s  (queued %s by %s)%s"
                    e.Ref
                    e.Field
                    (if e.Value = "" then "<cleared>" else e.Value)
                    e.At
                    e.Worker
                    note

            // The remedy that WORKS, next to the count it explains. Re-running flush here can never land
            // another board's entry, so "re-run after the reset" would be advice that does not fix the
            // number it names.
            match entries |> List.choose elsewhere |> List.distinct with
            | [] -> ()
            | boards ->
                let n = entries |> List.filter (elsewhere >> Option.isSome) |> List.length

                eprint (
                    sprintf
                        "fsgg-coord-engine: %d of these write(s) are queued against another board (%s) and CANNOT land here — they are still owed, not lost. Point FSGG_COORD_OWNER/FSGG_COORD_PROJECT at that board and flush again."
                        n
                        (boards |> List.map (fun (o, t) -> $"%s{o}/%s{t}") |> String.concat ", ")
                )

            ExitGreen

        | Ok _ ->
            match Board.bootstrapCached ctx.Transport ctx.Owner ctx.Title with
            | Error e -> fail e
            | Ok board ->

                // EVERY COUNT BELOW COMES FROM THE PASS ITSELF. An earlier cut of this read the queue here
                // and again afterwards to infer what had landed — and the deferral queue is ONE file shared
                // by every worker on the machine, so a concurrent `defer` in that window made the inference
                // wrong in the worst direction: "nothing replayed" over writes that HAD landed. Nothing is
                // reconstructed now.
                match Board.flush ctx.Transport board with
                | Error e -> fail e
                | Ok r ->
                    printfn "flush — replayed %d of %d queued write(s)." r.Written r.Queued

                    // A DROP IS NOT A WRITE, AND IT IS NOT AN ERROR EITHER. `Board.flush` drops what it can
                    // never land — an unparseable ref, an item no longer on the board — rather than
                    // retrying it forever. Saying so is the difference between a queue that drained and a
                    // queue that gave up quietly.
                    if r.Dropped > 0 then
                        eprint
                            $"fsgg-coord-engine: %d{r.Dropped} queued write(s) were DROPPED, not replayed — permanently un-writable (an unparseable ref, or an item no longer on this board)."

                    // A SKIP IS THE OPPOSITE OF A DROP, and until now the CLI told the worker neither (#966).
                    // `replayed 0 of 1` and nothing else is exactly the "my write did not replay and nothing
                    // said why" that #882 felt like from the outside — the impression this count exists to
                    // end. A dropped write is gone; a skipped one is still owed and still landable, just not
                    // by this pass against this board, so it gets its own line and its own remedy.
                    if r.Skipped > 0 then
                        eprint
                            $"fsgg-coord-engine: %d{r.Skipped} queued write(s) were SKIPPED, not replayed — they are queued against a DIFFERENT board, so this pass left them alone. They are still owed and still landable: point FSGG_COORD_OWNER/FSGG_COORD_PROJECT at that board and flush again (`flush --dry-run` names it)."

                    match r.Stopped with
                    | None -> ExitGreen

                    // A PARTIAL IS REPORTED AS A PARTIAL. A bare `fail e` here would say "budget exhausted"
                    // over a flush that DID land writes, and the worker could not tell "nothing landed"
                    // from "most landed" — the same could-not-tell that #862 is about.
                    | Some e ->
                        // SUBTRACT THE SKIPS (#966). `Queued - Written - Dropped` stays TRUE — a skipped
                        // entry does remain queued — but it is the wrong number to attach to "re-run flush
                        // after the reset", because a re-run HERE can never land another board's entry. The
                        // advice would not fix the count it names. The skips are reported above, with the
                        // remedy that does work; this number is the one the reset actually clears.
                        let remaining = r.Queued - r.Written - r.Dropped - r.Skipped

                        eprint
                            $"fsgg-coord-engine: the budget ran out mid-flush; %d{remaining} write(s) REMAIN QUEUED — re-run flush after the reset."

                        fail e

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
                                          LintApplication.ConsolidationRow.Repo = Options.resolveRepo r.PathRepo
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
    let private issues (ctx: Context) (opts: Options) : int =
        match opts.Args with
        | [] ->
            eprint "fsgg-coord-engine: issues: a repo is required (a registry short-id, owner/repo, or a repo name)."
            ExitError
        | raw :: _ ->
            // bash's `issues` split: a slashed token is an explicit owner/repo (kept whole), a bare token is
            // a short-id resolved against the board owner. Note this does NOT run an owner/repo back through
            // resolveRepo — an explicit owner is authoritative, exactly as bash's `${1%%/*}`/`${1#*/}` reads.
            let owner, repo =
                match raw.IndexOf('/') with
                | -1 -> ctx.Owner, resolveRepo raw
                | i -> raw.Substring(0, i), raw.Substring(i + 1)

            let state = opts.IssueState |> Option.defaultValue "open"

            match Reads.issues ctx.Transport owner repo state opts.Label opts.Fresh with
            | Ok body ->
                printfn "%s" (body.TrimEnd())
                ExitGreen
            // `fail`, NOT a hand-rolled `explain` + `ExitError`. This arm was the latter, and the two are
            // not the same: `fail` returns the ERROR'S OWN code, so a rate limit exits EX_RATE (75) — the
            // back-off signal — while `ExitError` flattened it to a generic 1, the code a caller reads as a
            // PERMANENT protocol error. `fail`'s own docstring names the failure exactly: "a caller that
            // saw a generic 1 would treat a temporary condition as permanent."
            //
            // It bit hardest here of all places. `issues` is the REST-only read `/pnext-item` §4 sends
            // every worker to for its dedupe step *because* REST survives a GraphQL outage — so it is the
            // one command still standing when GraphQL dies, and the one that dies alone when REST goes
            // instead (2026-07-16: REST core 0/5000, GraphQL 3,639 spare). Either way it reported the one
            // condition worth retrying as the one condition never worth retrying.
            | Error e -> fail e

    let run (opts: Options) : int =
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
            | Inbox
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

            match opts.Command with
            | Next -> next ctx opts
            | BatchCmd -> batch ctx opts
            | DriverCmd -> driver ctx opts
            | DeliveryCmd -> delivery ctx opts
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
            | SetField -> setField ctx opts
            | Child -> child ctx opts
            | Widen -> widen ctx opts
            | SetPaths -> setPaths ctx opts
            | Overlap -> overlapCmd ctx opts
            | Say -> say ctx opts
            | Inbox -> inbox ctx opts
            | RoomOpen -> roomOpen ctx opts
            | DoneCmd -> doneCmd ctx opts
            | VerifyPaths -> verifyPaths ctx opts
            | Bootstrap -> bootstrapCmd ctx opts
            | BoardCmd -> boardCmd ctx
            | FieldId -> fieldId ctx opts
            | OptionId -> optionId ctx opts
            | ItemId -> itemIdCmd ctx opts
            | Add -> addCmd ctx opts
            | Flush -> flushCmd ctx opts
            | LintCmd -> lint ctx opts
            | Issues -> issues ctx opts
            | other -> failwith $"Client.run received a non-IO command: %A{other}"
