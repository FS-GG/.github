namespace FS.GG.Coord.Cli

/// Argument parsing, and the residue rule.
///
/// EVERY CONSUMED OPTION IS DECLARED HERE, so that anything else can be REJECTED. The org learned this
/// the expensive way in SDD, where `init --project-root /tmp/b` silently seeded the current directory
/// and then reported success: an argument that is ignored is indistinguishable, from the caller's side,
/// from an argument that was honoured. A parser that shrugs at an unknown token is the same fail-open
/// shape as a gate that reports green over a subject it never read (#266).
///
/// So: an unknown FLAG is NAMED and refused. A flag given without its value is refused rather than
/// swallowing the next flag as its argument. Positional arguments (an issue ref, a field, a value) are
/// collected in order into `Args`, and each command validates its own arity.
module Options =

    /// What the engine was asked to do. The DECISION commands (`Decide`, `Lanes`, `Facts`) read
    /// state on stdin and touch no network; the CLIENT commands below `Scan` perform IO through the GitHub
    /// adapter — they are the surface the shim (ADR-0034 §4.4) execs in place of the bash client.
    type Command =
        | Decide
        | DeliveryCmd
        | SelfHostCmd
        /// .github#2175: inspect the resumable review/repair protocol (`FS.GG.Coord.Review`) —
        /// live against `<ref> --pr N`, or from a supplied `--snapshot`; the typed surface `pnext-item`
        /// and the #2135 event projection consume.
        | ReviewCmd
        | DriverCmd
        | CycleCmd
        | Scan
        | LanesView
        | Facts
        /// Emit the parser's command/flag surface as JSON for documentation and tooling gates.
        | CommandContractCmd
        /// Validate an intake draft, or report that its live transaction is not yet available.
        | IntakeCmd
        /// Validate one `fsgg.coord.finding-packet/v1` document (.github#2737). PURE — a finder gates
        /// its own draft before posting, and nothing here can refuse a post.
        | PacketCmd
        /// Record, inspect, or validate an explicit delivery-route receipt.
        | RouteCmd

        // ---- the client command surface (ADR-0040 Phase D — wired to the IO layer) --------------------

        /// This worker's id and the rule that derived it (`whoami [--mint]`).
        | WhoAmI
        /// The GraphQL/REST budget (`budget`).
        | Budget
        /// The next single schedulable item (`next [--repo]`).
        | Next
        /// Every item schedulable in parallel right now (`batch [--repo] [-n N] [--include-backlog]`).
        | BatchCmd
        /// The board, as a reconciler sees it — always fresh (`ready [--repo]`).
        | Ready
        /// Derive typed mechanical repairs from a fresh board read; dry-run unless `--apply`.
        | Reconcile
        /// Who holds what, right now — held/stale/unclaimed (`who [--repo] [--json]`).
        | Who
        /// Collect expired claims whose work is dead — refusing any with an open `item/<n>-*` PR
        /// (`reap [--repo] [--apply]`, #581). A DRY RUN without `--apply`.
        | Reap
        /// Take an item's lock (`claim <ref> [--worker W] [--force] [--refuse-overlap]`).
        | Claim
        /// Take over an ORPHAN — a stale claim whose PR is FINISHED — and land it (`adopt <ref> [--worker W]`, #697).
        | Adopt
        /// Is an OPEN PR finished work? The #697/#720 verdict as a first-class QUERY
        /// (`landable <pr> --repo NAME`) — one word on stdout, the decision in the exit code.
        | Landable
        /// Schedule AND claim the next item in one step (`take [--repo] [--worker W]`).
        | Take
        /// Drop a lock, restoring the column it overwrote (`release <ref> [--worker W]`).
        | Release
        /// Renew a lease (`heartbeat <ref> [--worker W]`).
        | Heartbeat
        /// Write one board field (`set-field <ref> <field> <value>`).
        | SetField
        /// Attach a child issue to a parent (`child <parent-ref> <child-ref>`).
        | Child
        /// Widen a held item's touch-set (`widen <ref> --paths T...`).
        | Widen
        /// Replace a held item's touch-set explicitly (`set-paths <ref> --paths T...`).
        | SetPaths
        /// Report whether an item's touch-set overlaps another's, or the repo's live claims
        /// (`overlap <ref> --active` | `overlap <ref-a> <ref-b>`). Repo-scoped (#353).
        | Overlap
        /// Message another worker (`say <ref> --to W --message M`).
        | Say
        /// This worker's mailbox — messages across every in-flight claim, on the board and off it
        /// (`inbox [--repo] [--peek] [--json]`, #461/case 25).
        | Inbox
        /// Stamp an item done, optionally rolling the parent up (`done <ref> [--flip] [--evidence E]`).
        | DoneCmd
        /// Check a PR's changed files against the touch-set its issue declared (`verify-paths --pr N [--warn]`).
        | VerifyPaths
        /// Resolve the board + field/option ids and cache them for a day (`bootstrap [--refresh]`, #418).
        | Bootstrap
        /// Print the cached board map as JSON (`board`) — 0 GraphQL when the day-cache is warm.
        | BoardCmd
        /// The resolved id of a board field, from the cache (`field-id <field>`).
        | FieldId
        /// The resolved id of a single-select option, from the cache (`option-id <field> <option>`).
        | OptionId
        /// The board item id for an issue (`item-id <ref>`) — 1 GraphQL, then cached forever.
        | ItemId
        /// "Has this issue/PR body changed since X" (`body-edits <ref>`) — the metered GraphQL
        /// `userContentEdits` read `.github#2456`'s independent-review contract names as authoritative,
        /// reached through the one metered transport in place of a hand-built `gh api graphql` call
        /// (`.github#2477`).
        | BodyEdits
        /// Typed operational GraphQL boundary for audit/archive automation.
        | GraphQlOps
        /// Put an issue on the board, idempotently (`add <ref>`) — the metered verb the GraphQL monopoly
        /// rule (#586) names in place of `gh project item-add`, restored in #861.
        | Add
        /// Replay the board writes an exhausted budget queued (`flush [--dry-run]`, #862) — the verb every
        /// `Deferred` write's "QUEUED; flush replays it" message names, and which nothing exposed. The queue
        /// (`Cache.defer`) and the replay (`Board.flush`) were always here; only this arm was missing, so
        /// the promise could not be kept by anyone.
        | Flush
        /// Board-health gate: flag Ready/Backlog items no worker can pick up (`lint [--repo] [--strict]`, #496).
        | LintCmd
        /// List a repo's issues over REST, ETag-revalidated — the GraphQL-budget-free read
        /// (`issues <repo> [--label L] [--state S] [--refresh]`, #446/#418).
        | Issues
        /// This worker's follow-up queue — the §4-case-2 promise, at an altitude something tests
        /// (`followup add <ref> | peek | pop | list`, #1063). LOCAL: a file, no board, no token, so the
        /// promise survives the exhausted budget that strands the worker who made it.
        | Followup

        /// The ADR-0050 registry-predicate oracle `P(id, field, value)`, run over LOCAL files (no board,
        /// no token). Prints one verdict word — `agrees`/`contradicts`/`unknown` — with the decision in
        /// the exit code (the `landable` shape). Assertion is positional or read from a filed
        /// `cross-repo-request` body on stdin. Call-site A of ADR-0050 (.github#1202).
        | Predicate
        | DiffAudit
        | RoomOpen
        | CommentCmd

        /// TAKE this receiver's per-receiver operation lock and print the authorization tuple the dispatch
        /// broker demands (executor-fencing design §4.1/§5.2, `.github#2312`). This is the production caller
        /// `Client.OpLock.acquire` was landed without: until it existed, no `fsgg:claim` marker could ever
        /// appear on an op-lock issue, so `fsgg-dispatch-broker.yml`'s `grant` input had no non-empty value
        /// and its "no live grant holds this receiver's operation lock" refusal could not be passed by any
        /// caller. It composes the operation key through `Operation.compose` (slice 1, `.github#2311`)
        /// BEFORE it spends a network round trip, exactly as the broker's own ordering comment does.
        | OpLockAcquire

        /// DROP this receiver's operation lock, after the dispatch it fenced. The other half of §4.1's
        /// "taken immediately before a dispatch and released after it, never held across an item's
        /// lifetime" — a grant held for an item's lifetime converts a parallel fleet into a serial one.
        | OpLockRelease

        | Help
        | Version

    type Render =
        | Json
        | Text

    /// WHICH STDOUT PROJECTIONS A COMMAND ACTUALLY HAS (#1523) — the one hand-written fact about the
    /// renderers. `scopeOf`'s `--json`/`--text` rows, what `command-contract` advertises, and the `Render`
    /// a BARE invocation is left at are all DERIVED from it, so they cannot disagree.
    ///
    /// PUBLIC so the surface tests can assert the emitted contract against it rather than against a second
    /// copy of the same list. Total over `Command` under `TreatWarningsAsErrors`, so a new verb cannot be
    /// born advertising a projection it does not have.
    type RenderSupport =
        /// Both projections exist — the handler branches on `opts.Render`. Carries the mode a BARE
        /// invocation renders in, because honouring a flag means honouring its ABSENCE too (#1517).
        | Both of ``default``: Render
        /// stdout is ALWAYS a machine document: `--json` is kept, `--text` is refused.
        | JsonOnly
        /// stdout is ALWAYS human text: `--text` is kept, `--json` is refused. The #1523 bucket.
        | TextOnly

    type HandlerOwner =
        | KernelProgram
        | BoardOps

    type MutationKind =
        | ReadOnly
        | WritesRemoteState

    type CommandDescriptor =
        { Command: Command
          Verb: string
          Render: RenderSupport
          Mutation: MutationKind
          HandlerOwner: HandlerOwner
          Documented: bool }

    /// Every nullary command case, derived from the union rather than maintained as a second list.
    val allCommands: Command list

    type Options =
        { Command: Command
          Render: Render
          /// EVERY render flag GIVEN — empty when neither appeared on argv. `Render` alone cannot say,
          /// because it has a non-optional default; this is what makes the flag guardable at all (#1523).
          ///
          /// A SET rather than the winner: `Render` is last-wins, and remembering only the winner would
          /// let `done --json --text` slip the `--json` it cannot honour past the residue rule unnamed.
          RenderGiven: Set<Render>
          SnapshotFile: string option
          /// `driver --events` (.github#2135) — derive the material-transition/active-inventory
          /// projection instead of the single next planning `Action`.
          Events: bool
          /// `driver --events --cursor <path>` (.github#2135) — the durable per-item cursor file.
          CursorFile: string option
          Repo: string option
          Fresh: bool
          AllowBacklog: bool
          Limit: int option
          LeaseMinutes: int
          LeaseGiven: bool

          /// Positional arguments in order — the ref, the field, the value, the child ref. Each command
          /// reads what it needs and refuses the wrong count.
          Args: string list

          /// `--worker <id>` — the lock's identity (ADR-0027; NOT the GitHub account).
          Worker: string option
          /// `--force` — steal a live lock (`claim`/`release`). A broken IDENTITY is never stealable; this
          /// is for a lock a human means to break.
          Force: bool
          /// `--refuse-overlap` (`claim`, .github#2459) — turn `claim`'s own #353 collision report from a
          /// WARNING that still claims into a REFUSAL. `claim` is the same lock as `take`/`batch` but
          /// without their upstream overlap-avoidance (that is exactly why the orphan/recovery paths it
          /// exists for can reach it), so the default stays permissive — this is the opt-in for a caller
          /// that wants the scheduler's own guarantee without going through the scheduler.
          RefuseOverlap: bool
          /// `--mint` (`whoami`) — print one fresh id line for `eval`.
          Mint: bool
          /// `--flip` (`done`) — roll the parent up when this child completes it.
          Flip: bool
          /// `--evidence <text>` (`done`) — assert the item is finished with NO PR (#600). Required for the
          /// no-PR green path; a green path with no argument would be a way of switching the stamp off.
          Evidence: string option
          /// `--partial <why>` (`done --flip`) — this child is a PARTIAL fix and does NOT discharge its
          /// parent, so the roll-up leaves the parent OPEN (#614). Absent means the child completes it.
          Partial: string option
          /// `--to <worker>` (`say`).
          ToWorker: string option
          /// `--message <text>` (`say`).
          Message: string option
          /// `--paths <token>...` (`widen`/`set-paths`) — paths to add, or the explicit replacement.
          ///
          /// Consumes the following arguments up to the first FLAG-SHAPED one (`TouchSet.isFlagShaped`),
          /// NOT to the end of argv. It read to the end until #1507, which made `--json` after `--paths` a
          /// declared path token rather than the flag it is. Empty is REFUSED, so `--paths --json` is a
          /// misconfiguration rather than a `--paths` silently satisfied by the next flag.
          Paths: string list

          /// `--pr <n>` (`verify-paths`) — the pull request to check.
          Pr: int option
          /// `--warn` (`verify-paths`) — downgrade a DRIFT/INVALID verdict to advisory (exit 0). "I could
          /// not check" is never downgraded — only a real verdict is.
          Warn: bool
          /// `--issue <ref>` (`verify-paths`) — check the PR against an EXPLICITLY named issue's touch-set,
          /// bypassing the branch/closing-ref resolution (#479). Its repo is authoritative: a `--issue` in a
          /// different repo than the PR's is a straddle the tool refuses (a touch-set there says nothing about
          /// the files changed here), and when `--repo` is absent the issue decides the repo.
          Issue: string option

          /// `--status <name>` (`ready`) — show only that board Status column, matched by NAME the way
          /// bash's `board_filter` matches it. Present ⇒ the default "not Done" filter is OFF: asking to
          /// see a column is asking to see it, Done included.
          Status: string option
          /// `--blocked-by <ref>` (`release --status Blocked`) — the edge to write into the `Blocked by`
          /// FIELD in the SAME call (.github#2079), so a coherent park is one call. Canonicalized exactly
          /// as `set-field <ref> 'Blocked by' <value>` already canonicalizes it.
          BlockedBy: string option
          /// `--all` (`ready`) — widen past the "not Done" default without naming a column. `ready` is a
          /// TRUTH read (#520), so `--all` shows the whole board — Done, and closed-but-still-columned rows.
          All: bool

          /// `--batch` (`set-field`) — write the remaining `Field=Value` args in ONE aliased mutation
          /// document (#448): N fields, one GraphQL request, one point at the floor.
          Batch: bool
          /// `batch --explain` — print the DERIVED RANKING beside the batch (.github#1598): every candidate
          /// in the order the scheduler considered it, the rank inputs that produced that order, and how
          /// many lanes each admitted item displaced. Stderr, like every other "why" this verb prints, so
          /// `batch --json --explain` keeps stdout a clean machine document.
          Explain: bool

          /// `--strict` (`lint`) — a NOTE is fatal too, not just an error (the pedantic board-health pass).
          Strict: bool

          /// `--active` (`overlap`) — check the item's touch-set against the LIVE claims in its own repo,
          /// rather than against a second named item. Repo-scoped (#353).
          Active: bool

          /// `--apply` (`reap`) — actually DELETE the expired markers. Without it, `reap` is a DRY RUN that
          /// only reports what it WOULD collect (`would reap …`), so a destructive lock-break is never the
          /// default — the operator opts into it. #581.
          Apply: bool

          /// `--peek` (`inbox`) — show new messages WITHOUT advancing the per-worker cursor, so the same
          /// mail is still "new" on the next read. Off, `inbox` consumes what it shows.
          Peek: bool

          /// `--dry-run` (`flush`) — LIST the queued board writes without replaying them.
          ///
          /// The polarity is the OPPOSITE of `reap --apply`, deliberately (#862). `reap` breaks another
          /// worker's lock, so its bare form must be safe. `flush` replays writes THIS worker was already
          /// told were queued — the recovery the `EX_RATE` message names — so defaulting to a dry run would
          /// rebuild the very trap #862 was filed for: the worker runs the command the engine named, reads
          /// a queue depth, concludes the board is repaired, and walks away from writes that never landed.
          DryRun: bool

          /// `--wait` (`landable`) — poll until the verdict SETTLES rather than reading it once (#724). The
          /// poll never believes an early `green`: it waits for the run set to STOP GROWING, and it keeps
          /// waiting while zero runs have registered (the registration race). Conflicted/unknown return at
          /// once — no amount of waiting fixes either.
          Wait: bool
          /// `--tries N` (`landable --wait`) — the maximum number of polls (positive). Default 30.
          Tries: int option
          /// `--interval S` (`landable --wait`) — seconds to sleep between polls (0 permitted, for the test
          /// harness). Default 20.
          Interval: int option
          /// `--require NAME` (`landable`, REPEATABLE — each occurrence APPENDS) — a check-run name that must
          /// have REPORTED before this PR is `green`. The rollup asks "is anything red?", which is blind to a
          /// check that is ABSENT; branch protection covers the REQUIRED set, so this is for a NON-required
          /// check that nonetheless decides the PR (#737). A missing one is `pending`, never `green`.
          Require: string list
          /// `--sha SHA` (`landable`) — the head SHA the caller believes it is gating. `pulls/{n}` is
          /// eventually consistent after a force-push, so a caller that just pushed can name the commit it
          /// MEANS; a disagreement is `pending` rather than a verdict about the previous commit (#737).
          /// Absent ⇒ the PR's own head SHA is taken on trust, right for every caller that did not push.
          Sha: string option

          /// `--label L` (`issues`) — restrict the REST listing to issues carrying this label. Absent ⇒ every
          /// issue in the state.
          Label: string option
          /// `--state open|closed|all` (`issues`) — which issue state to list. Default `open`, exactly as
          /// bash's `issues`.
          IssueState: string option
          /// `--local` (`who`) — join the live claims to the local git worktrees (#959). `who` is the only
          /// reader.
          Local: bool
          /// `who --all-repos` — read every repository represented on the Coordination board instead of
          /// silently defaulting to the current checkout's repository.
          AllRepos: bool

          /// `--over N,M` (`room open`) — the item refs the coordination room is opened over (ADR-0051),
          /// comma-separated. `RoomOpen` is the only reader.
          Over: string list }

    /// The documented default (`FSGG_CLAIM_LEASE_MIN`).
    [<Literal>]
    val DefaultLeaseMinutes: int = 120

    /// Which stdout projections a command HAS. Derived by tracing every `opts.Render` read in
    /// `Client.fs`/`Program.fs` to the handler that reaches it — NOT from the usage prose, which
    /// under-advertised four of the fourteen honouring commands while `scopeOf` over-advertised twenty.
    val renderSupport: command: Command -> RenderSupport

    /// One descriptor for every nullary command. During phase-1 coexistence it closes declarative
    /// projections over the existing parser/render/write behavior and owns handler-family assignment.
    val commandCatalogue: CommandDescriptor list

    /// Structural closure for catalogue consumers. Reports every duplicate, missing, unexpected,
    /// blank, or undocumented row together so a command addition gets one actionable failure.
    val validateCommandCatalogue:
        expectedCommands: Command list ->
        descriptors: CommandDescriptor list ->
        Result<unit, string list>

    /// A `--repo` token → the `RepoScope.Scope` board rows resolve to: a registry short-id maps
    /// (`sdd` → `FS.GG.SDD`), an `owner/repo` keeps its repo part, a literal name passes through, and
    /// the board's one non-roster value (`cross-repo`) comes back tagged `NonRepository` rather than a
    /// bare string a caller could mistake for a repository (#2398). `parse` applies this to `--repo`
    /// (flattening explicitly, by arm, to the plain string `Options.Repo` still carries — a `--repo`
    /// filter value is always a repository token, never the sentinel), so `Options.Repo` is ALWAYS
    /// resolved and no verb can be left out of it (#962).
    ///
    /// PUBLIC so the git-remote scope can resolve through the SAME map (`Client.scopedRepo` reads a remote,
    /// not an argument, so the parser never sees it), and to be tested directly.
    val resolveRepo: raw: string -> FS.GG.Coord.RepoScope.Scope

    /// An owner + repo → the CLOSED issue whose comments are that repo's CHORE-LOCK CAS subject (ADR-0041),
    /// resolved through the SAME map as `--repo` (so `sdd`, `FS-GG/FS.GG.SDD` and `FS.GG.SDD` agree).
    ///
    /// `None` is the FAIL-CLOSED answer and the caller must treat it as one: no lock ⇒ `offer` refuses, never
    /// broadcasts (#266). The embedded FS-GG table stays gated to owner `FS-GG` — the numbers are FS-GG's
    /// issues, so a foreign owner is never handed a real-but-unrelated ref. Embedded rather than read from
    /// `registry/repos.yml`: ADR-0042 / .github#1026.
    ///
    /// `extra` is the per-deployment roster a VENDORED tenant injects by env (`FSGG_COORD_CHORE_LOCKS`,
    /// parsed in `Client.fs`): matched on (owner, repo) under ANY owner, consulted BEFORE the embedded table
    /// so a deployment can bring its own locks (or repoint one) without a code change (.github#1140,
    /// successor to #1087). Pass `[]` for the default FS-GG deployment — behaviour is then unchanged.
    val choreLockRef:
        extra: FS.GG.Coord.Types.Ref list -> owner: string -> repo: string -> FS.GG.Coord.Types.Ref option

    /// An owner + repo → the CLOSED issue whose comments are that repo's per-receiver OPERATION-LOCK CAS
    /// subject (executor-fencing design §4.1, extending ADR-0041 onto a third subject), resolved through the
    /// SAME map as `--repo` and `choreLockRef`.
    ///
    /// A DIFFERENT SUBJECT FROM THE CHORE LOCK, DELIBERATELY. Mutual exclusion is answered by the subject —
    /// one lock issue per receiver — so a chore drain and a dispatch operation do not serialise against each
    /// other, and no key needs to appear in the marker at all. The CAS write path gains nothing from this:
    /// callers pass this ref to `Writes.claimScoped` unchanged, with stub callbacks, exactly as the chore
    /// lock does.
    ///
    /// `None` is the FAIL-CLOSED answer and callers must treat it as one: a fence that cannot find its lock
    /// REFUSES and never proceeds (#266, #421). The embedded FS-GG table stays gated to owner `FS-GG`, so a
    /// foreign owner is never handed a real-but-unrelated ref.
    ///
    /// UNLIKE `choreLockRef`, THIS COVERS ALL EIGHT ROSTER REPOSITORIES — `FS.GG.Net` included, which the
    /// chore-lock table omits and which is one of the repositories the `.github#1858` incident reached.
    /// `OpLockTests` proves that completeness against `registry/repos.yml` rather than against a
    /// hand-checked list.
    ///
    /// `extra` is the per-deployment roster a vendored tenant may inject, matched on (owner, repo) under ANY
    /// owner and consulted BEFORE the embedded table. Pass `[]` for the default FS-GG deployment.
    val opLockRef:
        extra: FS.GG.Coord.Types.Ref list -> owner: string -> repo: string -> FS.GG.Coord.Types.Ref option

    /// Parse argv. `Error` carries a message already fit to print.
    val parse: args: string list -> Result<Options, string>

    /// The command's wire name — the same spelling `usage` and the command contract use.
    ///
    /// Public since #2418, which keys the GraphQL spend ledger by it. Attribution must name the command
    /// an operator actually typed, so this stays the one spelling rather than a second table beside it.
    val commandName: command: Command -> string

    /// The parser's accepted command/flag surface, emitted from the same `scopeOf` table that enforces
    /// the residue rule. This is machine input; documentation validators must not scrape `usage`.
    val renderCommandContract: unit -> string

    val usage: string
