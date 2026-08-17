namespace FS.GG.Coord.Cli

/// THE CLI KERNEL'S CONTRACT — the shared base `.github#2726`–`#2729` will each depend on, held by the
/// compiler rather than by what an implementation happens to expose.
///
/// `.github#2724` measured what happens without one: 186 module-level bindings in `Client.fs`, 87 of them
/// public, and exactly FOUR required by production code outside the module. THE METHOD BEHIND 87, because
/// a count stated without one is not a measurement: 186 lines matching `^    let ` in that file, less the
/// 96 matching `^    let private `, less the 3 `let mutable private` forward declarations — the arithmetic
/// `.github#2724` recorded at `src/FS.GG.Coord.Cli/Client.fsi:29-30`, reproduced there rather than
/// restated here. Two neighbouring counts are NOT this one and do not reproduce it: 90 is the same
/// subtraction with the three forward declarations left in, and 81 is `^    val ` plus `^    type ` in the
/// base `Client.fsi`, which counts the reviewed surface after `.github#2724` had already made ten
/// internal-only bindings private. This file exists so the same
/// thing cannot happen here as command families are cut out one at a time. A binding absent from this file
/// is private to the module whatever `Kernel.fs` says, so the shared surface can widen only by an edit
/// somebody reviews.
///
/// ── WHERE THE PROSE BELOW CAME FROM, WHICH IS A DECISION AND NOT AN ACCIDENT ─────────────────────────
///
/// An F# signature file DISCARDS every `///` comment in the implementation file it fronts. `Client.fsi`
/// exists as of `.github#2724`, so 1,714 doc-comment lines in `Client.fs` reach nobody today; `.github#2725`
/// is the first change to move code out from under it, and it therefore decides for every relocated block
/// whether that block's prose reaches a consumer again. The rule applied, stated once in
/// `work/2725-cli-kernel-extraction/spec.md` and applied uniformly:
///
///   1. Prose that documents a binding TODAY — that is, `Client.fsi`'s — moves here byte-for-byte. Nothing
///      that reached a consumer stops reaching one.
///   2. Prose that is discarded today — `Client.fs`'s — moves into `Kernel.fs` unchanged and stays
///      discarded. Promoting it wholesale would be the general sweep `.github#2730` owns.
///   3. Where the two are byte-identical, only this copy survives; two copies of one paragraph in two
///      files are two texts that will drift.
///   4. A binding whose VISIBILITY this change altered gets a signature entry authored here from its own
///      previously-discarded prose. That is the one place prose starts reaching consumers, it is confined
///      to the bindings that were `private` to `Client` and must be public across an assembly boundary,
///      and every such entry says so in its own first line.
module Kernel =

    open FS.GG.Coord.Types
    open FS.GG.Coord.GitHub

    /// 0 — the verb did what it was asked, or its answer is YES.
    ///
    /// THE EXIT VOCABULARY BELOW IS A CONTRACT, not an implementation detail. The recipes and the shell corpus
    /// read the NUMBER and never parse the words, so these values are as public as any function here and move
    /// only by decision. Two invariants hold across every command in this module. First, they fail CLOSED: a
    /// read that could not be made is a distinct non-zero code, never a green empty answer, because "looked and
    /// found nothing" and "could not look" need opposite responses (#266). Second, an exhausted budget is
    /// EX_RATE (75) rather than a generic 1, so a temporary condition is never reported as a permanent one.
    [<Literal>]
    val ExitGreen: int = 0

    /// 1 — a usage error, or an operation that did not complete.
    ///
    /// The catch-all STOP, and it carries no verdict about the subject: it says the COMMAND failed, never that
    /// the answer was no. A caller that needs "the gate ran and refused" wants `ExitRed`.
    [<Literal>]
    val ExitError: int = 1

    /// 3 — a VERDICT, and it is no. A gate that ran to completion and refused.
    ///
    /// Held at 3 across every verdict-bearing command (`done`, `landable`, `verify-paths`, `predicate`,
    /// `adopt`, `claim`'s refusals) so a caller can key on the number without knowing which verb produced it.
    /// Its documented remedy is "a red is a finding": investigate the subject, do not retry the command.
    [<Literal>]
    val ExitRed: int = 3

    /// 4 — the gate ran and could NOT decide. Fail-closed, and deliberately not a share of `ExitRed`.
    ///
    /// The two mean different things to whoever reads them: 3's remedy is "a red check is a finding" and points
    /// at the subject; 4's is "go and get the fact this gate could not read" and points at the environment —
    /// an unresolvable board, an unreadable body, an unknown PR state. Collapsing them is how a credential gap
    /// gets reported as a clean board. No poll loop retries it.
    [<Literal>]
    val ExitNoVerdict: int = 4

    /// 5 — looked, and there is nothing startable: an empty queue, or every candidate blocked.
    ///
    /// #585 — `take`'s exit code must tell "I claimed you an item" (0) apart from the ways it can claim
    /// NOTHING, so a worker loop (`take && work_it`) never proceeds on nothing. A read failure keeps its own
    /// non-zero code and is NEVER this one, so "could not look" is never reported as "empty" (#266). This
    /// reverses #480's "the empty queue exits cleanly (0)", by decision on #585.
    [<Literal>]
    val ExitNone: int = 5

    /// 6 — lost every race. The board is contended; back off and retry.
    ///
    /// The caller holds NOTHING when it sees this, which is what makes a bounded retry safe here and unsafe on
    /// a code that might have taken a lock. Distinct from `ExitNone` because the two have opposite remedies: 5
    /// means there is no work, 6 means there is work and somebody else got it.
    [<Literal>]
    val ExitContended: int = 6

    /// 7 — not settled yet. The ONE verdict worth retrying, and it has its own number for that reason.
    ///
    /// #720/#724 — `landable`'s exit code is a POLL-LOOP CONTRACT: a recipe reads it to tell "keep waiting"
    /// from "stop" WITHOUT parsing the verdict word. Every stop outcome is a different code — a red or
    /// conflicted verdict is `ExitRed` (do NOT wait), an unknown is `ExitNoVerdict` (fail-closed, never a
    /// retry), and a PR that is not open at all is `ExitNotOpen`. Disposed on the record (ADR-0040 §5): bash's
    /// `landable` numbered its poll loop 0/3/1, where this engine keeps 3 == red across every verdict command
    /// and gives pending its own 7. The LITERALS differ; the PROPERTY does not.
    [<Literal>]
    val ExitPending: int = 7

    /// 10 — the PR is NOT OPEN: merged, or closed without merging (#1680). A VERDICT, and a terminal one.
    ///
    /// It exists because the four codes above have no word for "there is nothing left to gate", so a merged
    /// PR was answered with `ExitPending` — the one code the contract defines as worth retrying — and
    /// `--wait` spent its whole 600s budget on a fact settled before it started. Its own number rather than
    /// a share of `ExitRed`: both mean stop, but 3's documented remedy is "a red check is a finding", and
    /// the caller that meets a merged PR is usually a successor recovering an item whose worker died
    /// between merge and stamp — who must be told to STAMP, not to investigate a failure.
    [<Literal>]
    val ExitNotOpen: int = 10

    /// Write one line to stderr.
    ///
    /// PUBLIC BECAUSE THE ASSEMBLY BOUNDARY HAS NO `private`-TO-A-FRIEND (.github#2725, rule 4). It was
    /// `let private eprint` inside `Client`, which reaches it at ~350 sites; those sites are unchanged and
    /// still spell it `eprint`, because `Client.fs` opens this module.
    ///
    /// Its `Client.fs` doc comment was NOT promoted into this signature: it described a `Board status →
    /// its name` helper that exists nowhere in the tree, so carrying it here — where it would reach
    /// consumers for the first time — would have published a paragraph about a different binding. It does
    /// still stand beside the implementation in `Kernel.fs`, demoted from `///` to `//` and labelled as
    /// inherited-stale, so nothing was deleted and nothing is published. Recorded rather than dropped.
    val eprint: s: string -> unit

    /// An IO failure → its exit code and a printed reason, rendered in the caller's chosen mode.
    /// `RateLimited` becomes EX_RATE (75), the back-off signal; a caller that saw a generic 1 would treat
    /// a temporary condition as permanent.
    ///
    /// Public under rule 4: `let private failWith` in `Client`.
    val failWith: render: Options.Render -> e: Errors.IoError -> int

    /// `failWith` in `Text` mode — the failure path every command that has not been handed a render mode
    /// takes. Public under rule 4: `let private fail` in `Client`, reached at ~143 sites there.
    val fail: e: Errors.IoError -> int

    /// A NON-fatal board write's outcome, turned into the stderr note it warrants — surfacing what
    /// `|> ignore` used to swallow (#1151). A `Written` is silent; the other three each say what did NOT
    /// land and how to finish it. `Deferred` is the one that bites: an exhausted budget QUEUES the write
    /// and nothing replays it on its own (#510/#878), so a silent defer is a column that never lands under
    /// a command that reported green. It does NOT touch the verdict — the caller keeps its exit code and
    /// merely emits this note, because the WORK is done (or the lock is held) whatever the column says.
    ///
    /// Public under rule 4: `let private boardWriteNote` in `Client`.
    val boardWriteNote:
      ref: Ref ->
        field: string ->
        value: string ->
        outcome: Result<Board.WriteOutcome,Errors.IoError> -> unit

    /// An environment variable, or the supplied fallback when it is unset OR empty. The empty case is the
    /// point: an exported-but-blank variable is an absent value, not a value of `""`.
    ///
    /// Public under rule 4: `let private env` in `Client`.
    val env: name: string -> fallback: string -> string

    /// The ambient facts every IO command in this module is handed: the transport it may reach GitHub through,
    /// the board owner and title it is scoped to, the default repo a bare `<n>` ref resolves against, and the
    /// chore-lock roster a vendored tenant injects by environment.
    ///
    /// BUILT ONCE PER PROCESS, at the composition edge in `run`, for every command that TAKES one. That is what
    /// makes each such command below a function of its arguments rather than of the environment, and it is the
    /// only reason the test project can drive them at all: no unit test can construct a live transport, so a
    /// command that reached for one itself would be untestable by construction. This type is exported for that
    /// reason and for no other — it is a parameter, not a service locator.
    ///
    /// `followupAudit` IS THE EXCEPTION, and it is evidence rather than a pattern to copy. It takes no `Context`,
    /// so it builds one INSIDE the command from the environment — which is precisely why
    /// `withFollowupAuditContextForTest` has to exist at all, and why both bindings below are annotated as
    /// arguments for the extraction programme (.github#2725 onward) rather than as designs.
    type Context =
        {
          Transport: FS.GG.Coord.GitHub.Transport.IGitHubTransport
          Owner: string
          Title: string

          /// The board's default repo scope, for a bare `repo#n` ref and the candidate filter.
          DefaultRepo: string option

          /// The per-deployment chore-lock roster a VENDORED tenant injects by env
          /// (`FSGG_COORD_CHORE_LOCKS`, parsed by `parseChoreLocks`). Matched on (owner, repo) under ANY
          /// owner and consulted BEFORE the engine's embedded FS-GG table — empty for the default FS-GG
          /// deployment, so its behaviour is unchanged. See `Options.choreLockRef`.
          ChoreLocks: FS.GG.Coord.Types.Ref list
        }

    /// Whether this context's transport is the real HTTP one.
    ///
    /// Fake transports are deterministic in-process fixtures: they do not receive HTTP response headers
    /// and therefore cannot populate the credential ledger. Production admission is applied only at the
    /// real HTTP boundary; unit fixtures exercise the command's own pre-existing CAS paths.
    ///
    /// Public under rule 4: `let private usesLiveHttp` in `Client`.
    val usesLiveHttp: ctx: Context -> bool

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
    val parseRefIn:
      owner: string ->
        defaultRepo: string option ->
        raw: string -> Result<FS.GG.Coord.Types.Ref,string>

    /// Parse a `<ref>` against the ambient context. See `parseRefIn` — this only supplies the defaults,
    /// and it is the entry point every IO command actually uses.
    ///
    /// Public under rule 4: `let private parseRef` in `Client`.
    val parseRef: ctx: Context -> raw: string -> Result<Ref,string>

    /// Resolve the worker, printing the shared-session warning to stderr but PROCEEDING — the id is still
    /// this worker's in the common single-worker case, and the warning is for the fan-out that needs to
    /// know. #419: the remedy it prints is the mint COMMAND, never a literal, because an id an agent
    /// copies is an id agents collide on (#551).
    ///
    /// Public under rule 4: `let private worker` in `Client`.
    val worker: opts: Options.Options -> Result<Identity.Worker,int>

    /// Exactly one positional argument, or the usage refusal that names what was expected and how many
    /// arrived. Public under rule 4: `let private oneArg` in `Client`.
    val oneArg: opts: Options.Options -> what: string -> Result<string,int>

    /// OUR SESSION, for the twin predicate — the same one `claim` rides into the marker it posts.
    ///
    /// `None` means this harness exports no session. That is not a failure and must not fail closed: a
    /// caller with no session of its own has nothing to compare, so it can never conclude "twin", and
    /// `verifyHeld` keeps the pre-#1031 behaviour for it (`Writes.twinSession`).
    ///
    /// Public under rule 4: `let private sessionOf` in `Client`.
    val sessionOf: w: Identity.Worker -> SessionId option

    /// WHO THIS PROCESS IS, as opposed to who `--worker` said it was (#1646) — the third fact the lock
    /// boundary asks, after the id and the session, and the only one the flag cannot restate.
    ///
    /// `DerivesNothing` is a caller that resolves no identity of its own. It must NOT fail closed, for
    /// `sessionOf`'s reason exactly: `--worker` is the only way such a caller can say who it is, so
    /// refusing it would lock out the operator the flag exists for.
    ///
    /// Public under rule 4: `let private selfOf` in `Client`.
    val selfOf: w: Identity.Worker -> Writes.SelfIdentity

    /// THE REMEDY FOR A SHARED ID, and it is a MINT — never `--force` (#1031). A twin is a broken
    /// IDENTITY, not a contested item: forcing would delete a lock our twin is working behind. The remedy
    /// is a COMMAND, not a literal, which is why no id appears in it to copy.
    ///
    /// Public under rule 4: `let private mintRemedy` in `Client`.
    val mintRemedy: unit -> unit

    /// THE TWIN REFUSAL, shared by the three verbs that REFUSE over a twin's marker — `release`,
    /// `heartbeat`, `widen` (#1031). ONE MESSAGE, deliberately: a worker who meets this from `release`
    /// and then from `heartbeat` is in ONE situation and must not have to work out that two differently
    /// worded refusals mean the same thing. `done`'s lock-drop is the fourth `verifyHeld` caller and
    /// deliberately does NOT use this — the stamp is earned by the MERGE, so `done` reports the twin and
    /// still exits green. Returns `ExitRed`: a broken identity is a stop, not a retry.
    ///
    /// Public under rule 4: `let private twinRefusal` in `Client`.
    val twinRefusal: verb: string -> workerId: string -> ref: Ref -> theirs: SessionId -> int

    /// THE IMPERSONATION REFUSAL (#1646), shared by `claim`, `release`, `heartbeat` and `widen`. It says a
    /// DIFFERENT thing from the twin refusal and must: a twin is an accident whose remedy is a new id;
    /// this caller HAS an id and typed somebody else's, so `mintRemedy` is deliberately not called. The
    /// remedy it names is the sanctioned steal (`claim --force`, #1620) spelled WITHOUT `--worker` —
    /// a remedy that loops back into its own refusal reads as the tool malfunctioning. Returns `ExitRed`.
    ///
    /// Public under rule 4: `let private impersonationRefusal` in `Client`.
    val impersonationRefusal: verb: string -> ref: Ref -> derived: WorkerId -> named: WorkerId -> int

    /// THE TYPO NOTE (#1646 AC 3), and it is NOT the impersonation refusal. The commonest way to name an
    /// id that is not yours is to mistype it, and the remedy for that is "check the flag", not an
    /// accusation. Silent when the ids agree, which is every ordinary invocation.
    ///
    /// Public under rule 4: `let private noteWorkerDisagreement` in `Client`.
    val noteWorkerDisagreement: w: Identity.Worker -> unit

    /// A repo token → the repo NAME board rows carry. THE MAP LIVES IN THE PARSER (`Options.resolveRepo`,
    /// #962), because that is the one funnel every verb reaches; this alias serves the call sites that
    /// resolve something the parser never saw — a GIT REMOTE (`scopedRepo`, `defaultRepoScope`) or a
    /// positional repo argument. An explicit `--repo` is already resolved before it arrives, so the
    /// remaining calls are idempotent and harmless.
    ///
    /// Public under rule 4: `let private resolveRepo` in `Client`.
    val resolveRepo: raw: string -> string

    /// A GitHub remote URL → its `owner/repo`, or `None` when the URL is not a GitHub remote naming
    /// exactly one owner/repo. Handles every form `git config remote.origin.url` yields:
    /// `https://github.com/FS-GG/x(.git)`, `git@github.com:FS-GG/x(.git)`, `ssh://…/FS-GG/x.git`. A bare
    /// host or a nested path is NOT a scope — bash's `*/*/*|*/` guard, held here as an exact one-slash
    /// requirement — so a malformed remote can never be silently read as a repo.
    val parseGitHubSlug: url: string -> string option

    /// scope_repo (#480): the current checkout's `owner/repo`, read from `git config --get
    /// remote.origin.url`. FREE and offline — deliberately NOT `gh repo view` — so resolving the scope
    /// cannot burn GraphQL budget, and an exhausted budget can never be misreported as "you are not in a
    /// checkout" (#430). `None` when there is no readable `origin` that parses as `owner/repo`.
    ///
    /// Public under rule 4: `let private gitRemoteRepo` in `Client`.
    val gitRemoteRepo: unit -> string option

    /// `FSGG_COORD_CHORE_LOCKS` → the injected chore-lock roster a vendored tenant runs against a non-FS-GG
    /// board. Comma-separated fully-qualified refs `owner/repo#n`; a token that does not parse is DROPPED,
    /// never thrown — a dropped lock costs a chore not offered (fail-closed, condition 1), the same answer an
    /// absent lock already gives, so a typo degrades to the default rather than crashing the caller's real
    /// command. Parsing lives HERE, not in the pure `Options.choreLockRef`, because env is IO and ADR-0042's
    /// constraint is only that no `repos.yml` READER ship to a receiver — env DOES reach the receiver, so a
    /// per-deployment roster injected by env is exactly the seam a file reader could not be. The repo is
    /// canonicalised on the way in (`resolveRepo`), so the stored ref is already in the spelling the CAS
    /// compares. The pattern is byte-identical to `parseRef`'s own fully-qualified arm — which no longer lives
    /// in this module at all, but in `RefParsing.parse` (`src/FS.GG.Coord.Cli.Kernel/RefParsing.fs`, the `full` match)
    /// that `parseRefIn` delegates to — and the two are kept in step BY EYE, which is the standing hazard here
    /// rather than a note: nothing compiles the copy against its original.
    val parseChoreLocks: raw: string -> FS.GG.Coord.Types.Ref list

    /// The DEFAULT scope of a worker command (`next`/`take`/`batch`/`who`) when no `--repo` spells one
    /// out: the checkout you are standing in (#480). Reconcilers (`ready`) do NOT call this — /check-board
    /// runs a bare `ready --all` to reconcile the WHOLE board, and narrowing that to the checkout would
    /// shrink the reconciler to one repo, trading this scope bug for a strictly worse one in the very tool
    /// that exists to catch it. DEFAULTING is all it does; resolving an explicit `--repo` is the parser's,
    /// for every verb at once (#962).
    ///
    /// Public under rule 4: `let private scopedRepo` in `Client`.
    val scopedRepo: opts: Options.Options -> string option

    /// The checkout's `owner/repo` → the repo a bare `<n>` resolves against, or `None` when that slug's
    /// owner is not the board's. PUBLIC PURELY TO BE TESTED — the PRIVATE `defaultRepoScope` wraps it with
    /// the process call that reads the git remote (which a unit test cannot drive) and with `--repo`'s
    /// precedence: an explicit `--repo` wins, resolved through the same short-id map, so `--repo rendering`
    /// names one queue in both argument positions; otherwise the checkout you are standing in, exactly as
    /// `take`/`batch` default. That wrapper's result is what `run` installs as `Context.DefaultRepo`. Same
    /// idiom as `parseGitHubSlug`.
    ///
    /// THE OWNER CHECK THIS FUNCTION PERFORMS IS THE WHOLE #548 AMBIGUITY CRITERION, and it is why the
    /// wrapper is not just `scopedRepo`. `resolveRepo` throws the owner away, so in a NON-FS-GG checkout
    /// `scopedRepo` happily yields `acme/thing` → `thing`, and a bare `506` would then silently address
    /// `FS-GG/thing#506` — an issue in a repo the caller is not standing in and may not have meant to
    /// exist. Comparing the remote's owner against the board's and yielding `None` on a mismatch is what
    /// keeps a bare number a hard error outside the org, which the issue names as the one thing to get
    /// right.
    val defaultRepoForOwner: owner: string -> slug: string -> string option

    /// `defaultRepoForOwner` wrapped with the process call that reads the git remote — which a unit test
    /// cannot drive — and with `--repo`'s precedence. Its result is what `run` installs as
    /// `Context.DefaultRepo`.
    ///
    /// Public under rule 4: `let private defaultRepoScope` in `Client`.
    val defaultRepoScope: owner: string -> opts: Options.Options -> string option
