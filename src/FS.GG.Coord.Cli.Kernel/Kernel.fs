namespace FS.GG.Coord.Cli

/// THE CLI KERNEL — the shared base every command family needs, and the one module in this project that
/// was EXTRACTED rather than relocated (.github#2725).
///
/// Everything here was `Client.fs` on 2026-08-17, and it is the part of `Client.fs` that is not about any
/// particular command: the exit-code vocabulary, the ambient `Context`, the stderr and refusal helpers,
/// worker resolution, ref parsing's context-bound entry points, and the readers that answer "which repo is
/// this checkout". `.github#2724` measured why that mattered — 87 public bindings on one 11,090-line
/// module, of which four were required by production code — and this project is where the shared quarter
/// of them goes so that .github#2726–#2729 have something to depend on that is not `Client`.
///
/// WHAT MOVED IS THE CODE, NOT THE BEHAVIOUR. Every binding below is byte-identical to the one it replaces
/// except for its accessibility: bindings that were `private` to `Client` are public here, because an
/// assembly boundary has no `private`-to-a-friend. `Client` reaches them by `open FS.GG.Coord.Cli.Kernel`,
/// so every one of its ~350 `eprint`, ~143 `fail` and ~100 exit-literal call sites keeps the spelling it
/// had. That is deliberate: AC3 asks for no behaviour change, and a move that also re-spells 600 call
/// sites is a move whose diff nobody can read.
module Kernel =

    open System
    open System.Diagnostics
    open FS.GG.Coord
    open FS.GG.Coord.Types
    open FS.GG.Coord.GitHub
    open FS.GG.Coord.GitHub.Transport
    open FS.GG.Coord.Cli.Options

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
    let eprint (s: string) = Console.Error.WriteLine(s: string)

    /// An IO failure → its exit code and a printed reason. `RateLimited` becomes EX_RATE (75), the back-off
    /// signal; a caller that saw a generic 1 would treat a temporary condition as permanent.
    let failWith (render: Options.Render) (e: Errors.IoError) : int =
        let message = Errors.explain e

        match render with
        | Json -> eprint (Render.renderFailureJson (Errors.exitCode e) message (Errors.rateLimitKind e))
        | Text -> eprint $"fsgg-coord-engine: %s{message}"

        Errors.exitCode e

    let fail (e: Errors.IoError) : int = failWith Text e

    /// #1151: a NON-fatal board write's outcome, turned into the stderr note it warrants — SURFACING what
    /// `|> ignore` used to swallow. A `Written` is silent; the other three each say what did NOT land and
    /// how to finish it. `Deferred` is the one that bites: an exhausted budget QUEUES the write and NOTHING
    /// replays it on its own (#510/#878), so a silent defer is a column that never lands under a command
    /// that reported green. This is the sibling `release` handler's rule (`unclaimColumn`, #331/#867),
    /// factored so `done`'s Status=Done stamp and `claim`'s In-progress move cannot regrow the same
    /// promise-under-`ignore` (#1151). It does NOT touch the verdict: the caller keeps its exit code and
    /// merely emits this note, because the WORK is done (or the lock is held) whatever the column says.
    let boardWriteNote (ref: Ref) (field: string) (value: string) (outcome: Result<Board.WriteOutcome, Errors.IoError>) : unit =
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

    let env name fallback =
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

    /// Fake transports are deterministic in-process fixtures: they do not receive HTTP response
    /// headers and therefore cannot populate the credential ledger. Production admission is applied
    /// only at the real HTTP boundary; unit fixtures exercise the command's own pre-existing CAS paths.
    let usesLiveHttp (ctx: Context) =
        match box ctx.Transport with
        | :? Transport.HttpTransport -> true
        | _ -> false

    let parseRefIn (owner: string) (defaultRepo: string option) (raw: string) : Result<Ref, string> =
        RefParsing.parse owner defaultRepo raw

    /// Parse a `<ref>` against the ambient context. See `parseRefIn` — this only supplies the defaults.
    let parseRef (ctx: Context) (raw: string) : Result<Ref, string> =
        parseRefIn ctx.Owner ctx.DefaultRepo raw

    /// Resolve the worker, printing the shared-session warning to stderr but proceeding — the id is still
    /// this worker's in the common single-worker case; the warning is for the fan-out that needs to know.
    let worker (opts: Options) : Result<Identity.Worker, int> =
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

    let oneArg (opts: Options) (what: string) : Result<string, int> =
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
    let sessionOf (w: Identity.Worker) : SessionId option = w.Session |> Option.map SessionId

    /// WHO THIS PROCESS IS, as opposed to who `--worker` said it was (#1646) — the third fact the lock
    /// boundary asks, after the id and the session, and the only one the flag cannot restate.
    ///
    /// `DerivesNothing` is a caller that resolves no identity of its own (a human, a harness exporting no
    /// session and setting no `$FSGG_WORKER`). It must NOT fail closed, for `sessionOf`'s reason exactly:
    /// `--worker` is the only way such a caller can say who it is, so refusing it would lock out the operator
    /// the flag exists for. What it costs is stated in `Writes.SelfIdentity` rather than hidden here.
    let selfOf (w: Identity.Worker) : Writes.SelfIdentity =
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
    let mintRemedy () =
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
    let twinRefusal (verb: string) (workerId: string) (ref: Ref) (theirs: SessionId) : int =
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
    let impersonationRefusal
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
    let noteWorkerDisagreement (w: Identity.Worker) =
        match w.Derived with
        | Some d when d <> w.Id ->
            eprint
                $"  (note: '%s{w.Id}' is not this process's own worker id — that is '%s{d}'. If you did not mean to act as another worker, check `--worker`.)"
        | _ -> ()

    // ---- the repo a command scopes to ------------------------------------------------------------

    /// resolve_repo (bash): a `--repo` value is a registry short-id (`sdd`), an `owner/repo`, or a literal
    /// A repo token → the repo NAME board rows carry. THE MAP NOW LIVES IN THE PARSER (`Options.resolveRepo`,
    /// #962), because that is the one funnel every verb reaches; this alias keeps the call sites that resolve
    /// something the parser never saw — a GIT REMOTE (`scopedRepo`, `defaultRepoScope`) or a POSITIONAL repo
    /// arg (`issues <repo>`) — reading in the local vocabulary.
    ///
    /// An explicit `--repo` is ALREADY resolved by the time it reaches this module, so it needs no call here.
    /// The remaining ones are idempotent and harmless: they resolve a name that may already be resolved.
    ///
    /// ECHOES, same as `Options.resolveRepoName` (#2398): every caller of this alias resolves a git
    /// remote, a positional repo argument, or an already-resolved name — none of them a board `Repo
    /// Scope` read, so none can genuinely be the `cross-repo` sentinel in practice. Stated by an
    /// exhaustive two-arm match rather than assumed, so the byte-identical prior behaviour ("a literal
    /// name passes through") is preserved for whichever arm actually arrives.
    let resolveRepo (raw: string) : string =
        match Options.resolveRepo raw with
        | FS.GG.Coord.RepoScope.Repository name -> name
        | FS.GG.Coord.RepoScope.NonRepository token -> token

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
    let gitRemoteRepo () : string option =
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
    let scopedRepo (opts: Options) : string option =
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

    let defaultRepoScope (owner: string) (opts: Options) : string option =
        match opts.Repo with
        | Some r -> Some(resolveRepo r)
        | None -> gitRemoteRepo () |> Option.bind (defaultRepoForOwner owner)
