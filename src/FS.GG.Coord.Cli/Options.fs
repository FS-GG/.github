namespace FS.GG.Coord.Cli

module Options =

    type Command =
        | Decide
        | DeliveryCmd
        /// .github#2175: inspect the resumable review/repair protocol (`FS.GG.Coord.Review`) —
        /// live against `<ref> --pr N`, or from a supplied `--snapshot`; the typed surface `pnext-item`
        /// and the #2135 event projection consume.
        | ReviewCmd
        | DriverCmd
        | CycleCmd
        | Scan
        | LanesView
        | Facts
        | CommandContractCmd
        | IntakeCmd
        | RouteCmd
        | WhoAmI
        | Budget
        | Next
        | BatchCmd
        | Ready
        | Reconcile
        | Who
        | Reap
        | Claim
        | Adopt
        | Landable
        | Take
        | Release
        | Heartbeat
        | SetField
        | Child
        | Widen
        | SetPaths
        | Overlap
        | Say
        | Inbox
        | DoneCmd
        | VerifyPaths
        | Bootstrap
        | BoardCmd
        | FieldId
        | OptionId
        | ItemId
        | Add
        | Flush
        | LintCmd
        | Issues
        | Followup
        | Predicate
        | DiffAudit
        | RoomOpen
        | Help
        | Version

    type Render =
        | Json
        | Text

    /// WHICH STDOUT PROJECTIONS A COMMAND ACTUALLY HAS (#1523).
    ///
    /// This is the ONE hand-written fact about the renderers, and everything else about `--json`/`--text`
    /// is derived from it: which commands may be GIVEN the flag (`scopeOf FJson`/`scopeOf FText`), what
    /// `command-contract` advertises, and — the part that matters most — what `Render` a BARE invocation
    /// is left at. Before #1523 those were three independent copies: `scopeOf` said `Global` (all 40),
    /// `renderCommandContract` hard-coded `--json` onto every row, and each parse arm chose its own default
    /// or forgot to. Fourteen commands honoured the flag.
    ///
    /// It is a TOTAL function over `Command` and this project sets `TreatWarningsAsErrors`, so a new verb
    /// with no row here is a BUILD ERROR, not a silently-unguarded flag — the same structural property #991
    /// bought for the other 38 flags.
    type RenderSupport =
        /// BOTH projections exist: the handler branches on `opts.Render`, so `--json` and `--text` each
        /// mean something. Carries the mode a BARE invocation renders in — the default is part of the same
        /// declaration as the capability, because honouring a flag means honouring its ABSENCE too (#1517).
        | Both of ``default``: Render
        /// stdout is ALWAYS a machine document. `--json` keeps its promise; `--text` cannot be kept, so it
        /// is refused rather than accepted and ignored.
        | JsonOnly
        /// stdout is ALWAYS human text. `--text` keeps its promise; `--json` cannot be kept, so it is
        /// refused. This is the bucket that carried the #1523 defect: 20 verbs advertised `--json` and
        /// printed prose regardless.
        | TextOnly

    type Options =
        { Command: Command
          Render: Render
          /// EVERY render flag GIVEN — empty when neither `--json` nor `--text` appeared on argv.
          ///
          /// `Render` alone cannot answer "was the flag given?", because it has a non-optional default:
          /// "given" and "defaulted" are the same state, which is the exemption that kept `--json` `Global`
          /// and unguardable (#991's own note, quoted at `scopeOf`). This field is the missing bit, and it
          /// is what lets `flagsGiven` report `--json` so the residue rule can refuse it (#1523).
          ///
          /// A SET, NOT THE LAST ONE. `Render` keeps last-wins, and a field that only remembered the
          /// winner would leave the loser invisible: `done --json --text` would resolve to `Text`, report
          /// only `--text` — which `done` legitimately takes — and let the `--json` it cannot honour
          /// through unnamed. That is the accepted-and-ignored silence this whole change exists to end,
          /// rebuilt inside the guard against it. Every spelling that was typed is refusable.
          RenderGiven: Set<Render>
          SnapshotFile: string option
          /// `driver --events` — derive the material-transition/active-inventory projection instead of
          /// the single next planning `Action` (.github#2135). A separate flag rather than a separate
          /// command: both read the same live board/claim/PR/review facts `driver` already gathers, and
          /// the projection is layered over the SAME source, not a second one.
          Events: bool
          /// `driver --events --cursor <path>` — the durable per-item cursor file read before, and
          /// written after, deriving the projection (.github#2135). Absent ⇒ an empty cursor (every
          /// classified state reads as a transition) and no persistence — a stateless single-shot render.
          CursorFile: string option
          Repo: string option
          Fresh: bool
          AllowBacklog: bool
          Limit: int option
          LeaseMinutes: int
          LeaseGiven: bool
          Args: string list
          Worker: string option
          Force: bool
          /// `claim --refuse-overlap` (.github#2459) — see the `.fsi` doc.
          RefuseOverlap: bool
          Mint: bool
          Flip: bool
          Evidence: string option
          /// `done --flip --partial "<why>"` — this child is a PARTIAL fix and does NOT discharge its
          /// parent, so the roll-up must leave the parent OPEN (#614). Absent means the child completes it.
          Partial: string option
          ToWorker: string option
          Message: string option
          Paths: string list
          Pr: int option
          Warn: bool
          /// `verify-paths --issue REF` — the issue whose touch-set to check the PR against, named
          /// explicitly (bypasses branch/closing-ref resolution). Its repo is authoritative (#479).
          Issue: string option
          /// `ready --status S` — the board Status column to show, matched by NAME (case-insensitive), the
          /// way bash's `board_filter` matches it. Present ⇒ the default "not Done" filter is off: asking for
          /// a column is asking to SEE it, Done included.
          ///
          /// `release --status S` — the column to LAND the item in, matched the same way (#867/#331). It is
          /// the caller stating the deliberate column, so it beats both the restore and the `Ready` fallback.
          /// No other command reads this field; `statusCommands` refuses it everywhere else.
          Status: string option
          /// `release --status Blocked --blocked-by <ref>` — the edge to write into the `Blocked by`
          /// FIELD in the SAME call (.github#2079), so a coherent park is one call rather than two the
          /// caller must remember to pair. Canonicalized on `Blockers.canonicalizeBlockedBy`'s terms,
          /// exactly as `set-field <ref> 'Blocked by' <value>` already is — a comma list, a bare `#n`, a
          /// `repo#n`, or an issue URL, and prose or a placeholder is refused before anything is written.
          /// Read ONLY by `release`; every other command refuses it.
          BlockedBy: string option
          /// `ready --all` — widen past the "not Done" default without naming a column (#520: `ready` is a
          /// TRUTH read, so `--all` shows the whole board, Done and closed items and all).
          All: bool
          /// `set-field --batch` — the remaining `Field=Value` args are written in ONE aliased mutation
          /// document (#448): N fields, one GraphQL request, one point at the floor.
          Batch: bool
          /// `batch --explain` — print the DERIVED RANKING beside the batch (.github#1598): every candidate
          /// in the order the scheduler considered it, the rank inputs that produced that order, and how
          /// many lanes each admitted item displaced. Stderr, like every other "why" this verb prints, so
          /// `batch --json --explain` keeps stdout a clean machine document.
          Explain: bool
          /// `lint --strict` — a NOTE (not just an error) is fatal. Off, a note is advisory and lint still
          /// exits 0; on, any note fails the gate too (the pedantic board-health pass).
          Strict: bool
          /// `overlap <ref> --active` — check the item's touch-set against the LIVE claims in its own repo,
          /// rather than against a second named item. Repo-scoped: a same-named token in another repo is not
          /// a collision (#353).
          Active: bool

          /// `reap --apply` — actually DELETE the expired markers; without it, reap is a DRY RUN (#581).
          Apply: bool

          /// `inbox --peek` — show new messages WITHOUT advancing the per-worker cursor, so the same mail
          /// is still "new" on the next read. Off, `inbox` consumes what it shows.
          Peek: bool

          /// `flush --dry-run` — LIST the queued writes without replaying them.
          ///
          /// The polarity is deliberate, and it is the OPPOSITE of `reap --apply` (#862). `reap` collects
          /// another worker's claim, so its bare form must be safe. `flush` replays writes THIS worker was
          /// already told were queued — the recovery action the `EX_RATE` message names — so a dry run by
          /// default would rebuild the exact trap this issue was filed for: a worker runs the command the
          /// engine told them to run, reads "3 pending", concludes the board is repaired, and walks away
          /// from three writes that never landed.
          DryRun: bool

          /// `landable --wait` — poll until the verdict SETTLES instead of reading it once (#724). A `green`
          /// is believed only once the subject count has stopped growing; a `red` at zero subjects is the
          /// registration race and keeps waiting; conflicted/unknown return at once.
          Wait: bool
          /// `--tries N` (`landable --wait`) — the maximum number of polls. Default 30.
          Tries: int option
          /// `--interval N` (`landable --wait`) — seconds to sleep between polls. Default 20.
          Interval: int option
          /// `landable --require NAME` (repeatable) — check-run names that must have REPORTED. For a check
          /// branch protection does NOT require but which is the reason the PR exists; absent, it reads
          /// exactly like a passing one (#606/#737). Missing ⇒ `pending`, never `green`.
          Require: string list
          /// `landable --sha SHA` — the head SHA the caller believes it is gating. `pulls/{n}` is eventually
          /// consistent after a force-push, so a caller that just pushed says which commit it means; a
          /// disagreement is `pending`, never a verdict about the previous commit (#737).
          Sha: string option

          /// `issues --label L` — restrict the REST listing to issues carrying this label (absent ⇒ all).
          Label: string option
          /// `issues --state open|closed|all` — which issue state to list. Default `open` (bash's default).
          IssueState: string option

          /// `who --local` — join the live claims to the LOCAL git worktrees, so "what is that worktree
          /// doing?" needs no forensics (#959). bash shipped and documented it; the port dropped it. Every
          /// worker runs in a per-item worktree (pnext-item §2), so a fan-out IS a pile of `../<repo>-<n>`
          /// directories, and this is the verb that names which is which — the remedy #419's own warning
          /// points at when N agents collide on one id. `who` is the only reader.
          Local: bool
          /// `who --all-repos` — suppress the checkout-repository default and read the whole board.
          AllRepos: bool

          /// `room open --over N,M` — the item refs the room is opened over (ADR-0051). A comma-separated
          /// list resolved to `Ref`s by the handler, each of which gets a `Rooms: #room` back-reference
          /// written onto its body. `RoomOpen` is the only reader; `scopeOf FOver` refuses it everywhere else.
          Over: string list }

    /// The CLIENT's lease default, and the value `FSGG_CLAIM_LEASE_MIN` overrides.
    ///
    /// **`Snapshot.DefaultLeaseMinutes` IS THE SAME NUMBER AND IS DELIBERATELY NOT THE SAME FACT**
    /// (`.github#1677` AC5, which proposed reconciling them to one source). They are two different
    /// defaults that agree today: this one is what THIS process uses when nothing configured it, and
    /// Snapshot's is the WIRE default — what the engine assumes when a shim too old to send
    /// `leaseMinutes` omits it from the snapshot (`Snapshot.fs`, `Snapshot.fsi`).
    ///
    /// Collapsing them would COUPLE the wire default to this process's configured value, which is
    /// exactly what Snapshot's own comment forbids: *"It is CONFIGURABLE in the client
    /// (`FSGG_CLAIM_LEASE_MIN`, default 120), so the engine may not assume it."* Under an
    /// `FSGG_CLAIM_LEASE_MIN=30` export, a single shared source would make the engine read an old
    /// shim's silence as "30" when that shim meant 120 — inventing a shortened lease for a client that
    /// never asked for one, and reaping live claims on the strength of it. The duplication is the
    /// decoupling, and it is recorded here rather than removed. They are also not reachable from one
    /// another as a `[<Literal>]`: `Snapshot.fs` compiles BEFORE `Options.fs`.
    [<Literal>]
    let DefaultLeaseMinutes = 120

    /// `FSGG_CLAIM_LEASE_MIN` — the claim lease, in minutes, when no `--lease` is given.
    ///
    /// **THIS FUNCTION IS THE WHOLE OF `.github#1677`, AND ITS ABSENCE WAS THE DEFECT.** Seventeen
    /// places in this repo — ADR-0027, ADR-0038, `docs/coordination/parallel-work.md`, the generated
    /// `Protocol.fs` region and the KIT-MIRRORED skill copies it emits into every receiver, plus
    /// `Snapshot.fsi` and `Options.fsi` right here — told every worker in the fleet that this variable
    /// configures the lease. Nothing read it. An ignored environment variable is INDISTINGUISHABLE from
    /// an honoured one that happened to match the default, so the claim was unfalsifiable from where a
    /// worker stands, and it survived for as long as it did precisely because it could not be caught.
    ///
    /// The load-bearing copy is `Snapshot.fs`'s: it justifies why the engine keeps its OWN wire default
    /// rather than assuming 120 — *"a repo that shortened its lease and an engine that hard-coded 120
    /// would together tell every worker to wait out a window that already closed."* That rationale
    /// asserts this function exists. Deleting the variable instead would have had to rewrite the reason
    /// the two defaults are deliberately separate, which is the eighth site the audit did not count.
    ///
    /// **A MALFORMED VALUE IS REFUSED, NOT SILENTLY DROPPED, AND THAT IS THE POINT.** This is the same
    /// input as `--lease` arriving down a different channel, so it gets `--lease`'s contract exactly:
    /// a positive number of minutes, or an error naming what was read. Falling back to 120 on garbage —
    /// the soft direction `FSGG_COORD_SCAN_TTL_SEC` takes, correctly, because a cache is optional — would
    /// rebuild the very unfalsifiability this issue is about: a worker who typos the value would see no
    /// error and no change, which is the state that already cost the fleet seventeen lying documents.
    ///
    /// UNSET and EMPTY are not malformed. They are "I did not configure this", and they take the default.
    let private leaseMinutesFromEnv () : Result<int, string> =
        match System.Environment.GetEnvironmentVariable "FSGG_CLAIM_LEASE_MIN" with
        // WHITESPACE-ONLY IS UNSET, NOT MALFORMED, AND THE `Trim` BELOW IS WHY THIS ARM SAYS SO. Matching
        // only `null | ""` here would refuse `export FSGG_CLAIM_LEASE_MIN="$SOMETHING_UNSET "` — empty in
        // intent, fatal in effect, and refused by a function whose own contract says empty is fine.
        | v when System.String.IsNullOrWhiteSpace v -> Ok DefaultLeaseMinutes
        | v ->
            match System.Int32.TryParse(v.Trim()) with
            | true, n when n > 0 -> Ok n
            | true, n -> Error $"FSGG_CLAIM_LEASE_MIN must be a positive number of minutes (got %d{n})"
            | _ -> Error $"FSGG_CLAIM_LEASE_MIN needs a number of minutes (got '%s{v}')"

    let usage =
        """fsgg-coord-engine — the typed coordination engine (ADR-0034), and the client it becomes (ADR-0040).

`scripts/fsgg-coord` is the client you run today; this is the engine it shells out to, and — at the
Phase D shim — the engine it BECOMES. The DECISION commands read state on stdin and touch nothing. The
CLIENT commands read and write GitHub through the typed IO layer.

DECISION (pure — no board, no network):
  decide [--snapshot FILE] [--json|--text]   decide a batch from a board-state snapshot on stdin
  delivery <ref> [--pr N] [--apply] [--json|--text]
                                             re-read one claimed item's delivery facts and emit its sole
                                             freshness-bound action; --apply performs only guarded landing
  delivery --snapshot FILE [--json|--text]   inspect a supplied lifecycle snapshot without IO
  review <ref> --pr N [--json|--text]        inspect the resumable review/repair protocol (.github#2175):
                                             one typed state and next action — dispatch critic, resume
                                             implementer, resume the same critic, await checks, request
                                             host acceptance, enter the one fresh repair phase, accept, or
                                             park for human action — bound to a freshness token a changed
                                             head invalidates, or a fail-closed no-verdict
  review --snapshot FILE [--json|--text]     inspect a supplied review-protocol snapshot without IO
  driver [--snapshot FILE] [--json|--text]   plan from the live board plus a source-bound receipt
  driver --events [--cursor FILE] [--json|--text]
                                             derive material transitions and the complete active-item
                                             inventory from live board/claim/PR/review/delivery facts
                                             (.github#2135); --cursor persists the idempotency cursor
  cycle <inspect|register|advance|update|complete> [--snapshot FILE] [--json|--text]
                                             inspect or advance one source-bound roadmap/workspace cycle ledger
  lanes  [--snapshot FILE] [--json|--text]   partition a snapshot's items into non-contending lanes
  facts  [--json|--text]                     emit the protocol the engine enforces (projections read this)
  command-contract [--json]                  emit the parser's command/flag contract for tooling
  intake <validate|apply> <draft.json> [--json]
                                             validate or atomically project one receipt-bound filing draft
  delivery-route <show REF|record REF receipt.json> [--json]
                                             inspect or append a source-bound agent delivery-route receipt

IO (read and write the board — $FSGG_COORD_OWNER / $FSGG_COORD_PROJECT, $GITHUB_TOKEN, $FSGG_GITHUB_API_BASE):
  scan   [--repo NAME] [--fresh] [-n N] [--include-backlog] [--lease MIN]
                                             read the board and emit the snapshot `decide` consumes
  next   [--repo NAME]                       the next single schedulable item — one line, the ref, on
                                             stdout, and NOTHING on stdout when there is none
                                             (.github#1562: the "nothing schedulable" headline and the
                                             per-item reasons are stderr, so `ref="$(… next)"` reads an
                                             empty ref instead of that sentence). AND IT WRITES (#1535):
                                             after printing that answer it offers a #733 chore, which
                                             POSTs a claim marker TAKING this repo's chore lock. For the
                                             same decision WITHOUT the offer use `batch --text -n 1` —
                                             that is the read, and what a STALE engine still permits
  batch  [--repo NAME] [-n N] [--include-backlog] [--explain] [--json|--text]
                                             every item schedulable in parallel right now — `next`
                                             uncapped, and the READ half of that pair: it makes no
                                             chore offer and takes no lock (#1535). Defaults to JSON;
                                             --text gives `next`'s ANSWER without the offer — the same
                                             words in the same order, but ALL on stdout, including the
                                             "nothing schedulable" headline `next` keeps off it
                                             (.github#1562). This is prose for a human; `next`'s stdout
                                             is a ref for `$(…)`, so capture accordingly.
                                             Candidates are packed PRIORITY-GREEDILY by a derived rank
                                             (blocking count, Class, Phase, age — #1598), so the
                                             highest-ranked schedulable item is always admitted;
                                             --explain prints that ranking and why each item won or
                                             lost its lane
  ready  [--repo NAME] [--status S] [--all] [--json|--text]
                                             the board as a reconciler sees it (always fresh; not-Done
                                             by default — a TRUTH read, so it shows items the scheduler
                                             will refuse; --status/--all widen past the default). Defaults
                                             to JSON; --text gives the human-readable board table
  reconcile [--repo NAME] [--apply] [--json]  derive mechanically safe board repairs from a fresh scan;
                                             dry-run by default. Judgement findings remain report-only in
                                             `lint`; --apply performs only typed chore remedies
  who    [--repo NAME|--all-repos] [--local] [--json]
                                             who holds what, right now (held/stale/unclaimed;
                                             --local joins claims to local git worktrees;
                                             output always names its effective scope;
                                             --json for the machine contract, else a human table)
  reap   [--repo NAME] [--apply]             collect expired claims whose work is dead — REFUSING any with
                                             an open item/<n>-* PR (#581); a DRY RUN without --apply
  landable <pr> --repo NAME                  is this OPEN PR finished work? one verdict word on stdout
    [--wait [--tries N] [--interval S]]      (green/conflicted/pending/red/unknown, or merged/closed when
                                             the PR is not open at all — .github#1680), the decision in the
                                             exit code — the #697/#720 gate as a query (#724). --wait polls
                                             until the verdict SETTLES: it never believes an early green (it
                                             waits for the run set to STOP GROWING), and keeps waiting while
                                             zero runs have registered (default --tries 30, --interval 20s).
    [--require NAME]... [--sha SHA]           --require NAME (repeatable): this check must have REPORTED —
                                             for one branch protection does NOT require but that decides the
                                             PR; absent, it reads like a passing one (#606). --sha SHA: the
                                             head you MEAN to gate, for a caller that just force-pushed (the
                                             PR object lags). Neither can green; both are pending (#737)

  claim  <ref> [--worker W] [--force]        take the lock; --json emits a fresh marker/Status receipt.
         [--refuse-overlap] [--json]         --force STEALS: it takes an item another worker holds RIGHT
                                             NOW, deleting their marker and posting the theft on the item
                                             so they and a later reader both see it (#1620 — the recovery
                                             route for a holder that died with an open PR and hours of
                                             lease left, which `reap` and `adopt` correctly refuse). It
                                             also lifts the #516 one-item-per-worker refusal. It does NOT
                                             override a twin marker (#419) or an unparseable one: those
                                             are a broken identity, not a contested item. `claim` reaches
                                             items WITHOUT the scheduler's own overlap-avoidance, so it
                                             runs the same #353 collision scan `widen`/`overlap --active`
                                             use and WARNS (still claiming) on a live overlap (#2459);
                                             --refuse-overlap turns that warning into a refusal instead
  take   [--repo NAME] [--worker W] [--json]
                                             schedule AND claim the next item, in one step. Ready only:
         [--include-backlog]                 a Backlog row is passed over AT THE COLUMN unless you ask for
                                             it (#636 — the flag has always worked here; only this line
                                             was missing, so the remedy for a Backlog-starved queue was
                                             undiscoverable from the tool that refused)
  release <ref> [--worker W]                 drop the lock, restoring the column it overwrote;
          [--status S]                       --status lands it in S instead (#867: name the column you
          [--blocked-by REF]                  mean, e.g. `--status Blocked`, or it goes back to Ready).
                                             `--status Blocked` refuses BEFORE the lock drops unless the
                                             row will end with a `Blocked by` field or a `Blocked on:
                                             human/...` sentinel (.github#2079); `--blocked-by REF` writes
                                             the field in this same call so a coherent park is one call
  heartbeat <ref> [--worker W]               renew the lease
  adopt  <ref> [--worker W] [--json|--text]  take over an ORPHAN — a stale claim whose PR is FINISHED —
                                             and land it (#697/#720); reports the preconditions it checked,
                                             then transfers the claim. Defaults to text; --json gives the
                                             transferred claim's machine receipt

  add    <ref> [--status S]                  put an issue ON the board, idempotently (#861) — the metered
                                             verb the GraphQL monopoly rule names (#586); prints the item id.
                                             Status DEFAULTS TO `Backlog` (#1823): a row with no Status is
                                             invisible to EVERY scheduler, and 14 were filed that way in one
                                             day before anything said so. Backlog is visible to triage and
                                             NOT startable — promotion to Ready stays a deliberate act. The
                                             default only ever fills an EMPTY column: a row that already
                                             carries a Status keeps it, so re-running `add` cannot overwrite
                                             one somebody set. `--status S` names the column instead
  set-field <ref> <field> <value>            write one board field (empty value clears)
  set-field --batch <ref> Field=Value ...    write N fields in ONE aliased mutation (#448)
  flush  [--dry-run]                         REPLAY the board writes an exhausted budget queued — the verb
                                             every "QUEUED; flush replays it" message names (#862). Replays
                                             by DEFAULT; --dry-run lists the queue and writes nothing
  child  <parent-ref> <child-ref>            attach a child issue to a parent
  widen  <ref> --paths T... [--json]         add paths to a HELD item's touch-set (union; idempotent)
  set-paths <ref> --paths T... [--json]      replace a HELD item's touch-set explicitly (also narrows)
                                             --json: the resulting declaration and the #353 verdict as
                                             one object, rather than prose over two streams (#1517)
  overlap <ref> --active | <a> <b>           does an item's touch-set collide? (repo-scoped, #353)
  say    <ref> [--to W] <message>            message another worker; --to defaults to `*` (anyone
               <ref> --to W --message M      holding the item). The message is POSITIONAL — the form
                                             every skill prescribes — and --message is its alias
  inbox  [--repo NAME] [--peek] [--json]     messages addressed to this worker across every in-flight
                                             claim (ON the board and off it, #461/case 25), AND the
                                             coordination rooms those items reference (ADR-0051); --peek
                                             does not advance the cursor
  room open --over N,M [--repo NAME]         open a coordination room over a contended cluster (ADR-0051):
                                             create the room issue and write a `Rooms: #room` back-reference
                                             onto each named item, so their holders share its channel. The
                                             room is off-board and closes itself when every referenced item
                                             is done
  done   <ref> [--flip] [--evidence E]       stamp the item done; --flip rolls the parent up (add
               [--partial "why"]             --partial "why" if this child does NOT complete its parent, #614)
  verify-paths --pr N [--repo NAME]          did the PR stay inside its issue's touch-set? (OK/DRIFT/
               [--issue REF] [--warn]        SKIP; --issue names the issue explicitly; --warn advisory)

  whoami [--mint]                            this worker's id and how it was derived
  budget [--json]                            the GraphQL budget, and the depth of the deferral queue
                                             (`pendingBoardWrites`) — free, and 0 GraphQL
  followup add <ref> | peek | pop | list | audit
                                             `audit` inspects every local worker queue without consuming it;
                                             the other verbs operate on this worker's follow-up queue — the "I can fix this, just not
                                             in THIS PR" promise, kept where something can test it (#1063).
                                             A FILE: no board, no token, so it survives the exhausted budget
                                             that strands the worker who made the promise. Keyed on the
                                             resolved worker id, so a fan-out cannot race it. `add` refuses a
                                             BARE ref — the queue outlives the checkout that wrote it, where
                                             a bare number silently resolves onto an unrelated row. `peek`/
                                             `pop`/`list` exit 5 (EX_NONE) on an empty queue: "nothing owed"
                                             is a LOOK THAT SUCCEEDED, and never a failed read (#266/#585)

  bootstrap [--refresh]                      resolve the board + field/option ids (2 GraphQL, then
                                             day-cached; --refresh drops the cache and re-resolves)
  board                                      the cached board map as JSON (0 GraphQL when warm)
  field-id <field>                           the resolved id of a board field (from cache)
  option-id <field> <option>                 the resolved id of a single-select option (from cache)
  item-id <ref>                              the board item id for an issue (1 GraphQL, then cached)

  lint   [--repo NAME] [--json] [--strict]   board-health gate: a Ready/Backlog item that no worker can
                                             ever pick up (no `Paths:`, or every token unmatchable) is an
                                             error (#496); --strict makes notes fatal too
  issues <repo> [--label L] [--state S]      list a repo's issues over REST, ETag-revalidated — a 304 costs
         [--refresh]                         nothing (#446/#418). <repo> is a short-id, owner/repo, or a
                                             repo name; emits the raw JSON array (project it with jq)
  predicate <id> <field> <value> [--json|--text]
                                             the ADR-0050 registry oracle: does the row exist AND does the
  predicate  (cross-repo-request on stdin)   OWNING producer's manifest agree? One word — agrees/contradicts/
                                             unknown — decision in the exit code (0/3/4, the `landable` shape).
                                             Owner is authoritative (`owner:`), an absent value is UNKNOWN
                                             not false (.github#658), and a missing registry/manifest is
                                             UNKNOWN — fail closed. Reads registry/skills.yml ($FSGG_REGISTRY)
                                             and producer checkouts under $FSGG_REPOS_ROOT (default .repos).
                                             Local: no board, no token. Defaults to text; --json gives the
                                             machine verdict. Only `mirrored` compared today.
  diff-audit <base> <head> <old> <new> [receipt.json|-] [item-body.md] --paths P... [--repo ROOT] [--json]
                                             inventory exact git-object changes; unresolved required receipts
                                             exit red. A supplied receipt is rejected when stale, incomplete,
                                             duplicated, malformed, or outside the declared paths.

  --help    --version

A <ref> is a URL, owner/repo#n, or repo#n (owner/repo default to $FSGG_COORD_OWNER / --repo).

EXIT CODES — the engine's own (the shim translates them for a caller that still speaks bash):
  0 green   ·   1 error (bad args / malformed input)   ·   2 defect (the engine broke)
  3 red     ·   4 no-verdict   ·   75 EX_RATE (budget exhausted — back off, try again later)
  `take` (#585): 0 ONLY when it claimed an item · 5 EX_NONE (nothing startable) · 6 EX_CONTENDED
  (lost every race) · 75 EX_RATE · any other non-zero, could not read (never EX_NONE, #266)
  `next`/`batch` (.github#1562): 0 whether or not anything was schedulable — DELIBERATELY not `take`'s
  EX_NONE. `next` is `batch` capped at one and `batch --text -n 1` is its documented substitute (#1535),
  so an EX_NONE here would make that substitution change a caller's exit status; `take`'s 5 means "I
  CLAIMED NOTHING", a fact about a write neither of these attempts. So read the ANSWER — `next`'s stdout
  is the ref and is EMPTY when there is none, `batch --json`'s is `[]` — but read it ONLY AT 0. Every
  other code above is "could not look", and it empties stdout too; an empty ref at non-zero is no answer,
  never an empty queue (#266)
  `landable` (#720/#724): 0 green · 7 pending (the ONE verdict worth retrying) · 3 red or conflicted
  (do NOT wait) · 10 the PR is NOT OPEN — stdout says which: `merged` (it LANDED; do not merge it
  again, and if you are recovering a half-finished item, go STAMP it) or `closed` (nothing landed, so
  do NOT stamp it done). Terminal, and deliberately not 7: GitHub nulls `mergeable` on merge, so this
  used to answer `pending` and `--wait` burned its whole 600s budget on a settled fact (.github#1680)
  · 4 unknown (could not reach a verdict — fail-closed, never a retry)
"""

    /// Which stdout projections each command HAS — see `RenderSupport`. Derived by tracing every
    /// `opts.Render` read in `Client.fs`/`Program.fs` to the handler that reaches it, exactly as `scopeOf`
    /// below was derived, and NOT from the usage prose.
    ///
    /// The `Both` default of each row is the mode that command renders in TODAY with no flag. It is not a
    /// preference and it is not adjustable in passing: `#1517` shipped `widen` honouring `opts.Render`
    /// while its parse arm still inherited the module default of `Json`, which would have flipped the BARE
    /// `widen` — the form every recipe runs — from its human receipt to a JSON object. Changing a `Both`
    /// default here changes what an existing caller's un-flagged invocation prints.
    let renderSupport (c: Command) : RenderSupport =
        match c with
        // ---- BOTH projections: the handler branches on `opts.Render` -----------------------------------
        | Decide -> Both Json // Program.fs `decide`
        | DeliveryCmd -> Both Json // Program.fs `delivery`
        | ReviewCmd -> Both Json // Program.fs `review` (ReviewApplication)
        | DriverCmd -> Both Json // Program.fs `driver`
        | CycleCmd -> Both Json // Program.fs `CycleLedgerApplication`
        | LanesView -> Both Json // Program.fs `lanes`
        | Facts -> Both Json // Program.fs `facts` — `generate-projections` reads the JSON arm
        | BatchCmd -> Both Json // Client.fs `batch`
        | Ready -> Both Json // Client.fs `ready`
        // Client.fs `reconcile` — and `--apply --json` is a REAL combination (.github#1541), which is the
        // statement this row now has to carry, because the `parse` funnel used to contradict it.
        //
        // #1429 (897b83d, a skills refactor whose subject said nothing about the options surface) added a
        // `validate` arm refusing `--apply` with `--json` on this verb. That was a WORKAROUND for a
        // handler defect, never a contract, and the thing it removed was the machine projection of the
        // org's one MUTATING reconciliation verb: a driver applying a reconciliation could not learn which
        // writes landed and which QUEUED against an exhausted budget except by scraping `applied`/`queued`
        // prose off stdout. A queued write is not lost — `flush` replays it — but nothing replays it ON ITS
        // OWN, so a caller that never learns the write deferred is a caller that never runs `flush`. The
        // refusal's own remedy ("inspect the JSON dry run, then apply in human-readable mode") is two
        // passes over a board that can change between them, and the second is the unreadable one.
        //
        // WHAT MADE IT LIFTABLE. Until PR #1537 the `--apply` phase printed its per-write outcome to
        // stdout PAST a document that had already ended, so opening the gate would have shipped an
        // unparseable stream the moment it opened. #1537 moved the outcome INTO the document
        // (`field`/`value`/`outcome`/`error`), put the deferred-write remedy on stderr, and stopped the
        // `reap` child's stdout leaking in. The handler was correct first; only the gate remained.
        //
        // AND THIS ROW IS WHY THERE WAS NOTHING ELSE TO CHANGE. `Both` puts `reconcile` in `jsonReaders`,
        // so `--json` has been in its scoped flag set — and therefore in the emitted `command-contract`
        // row and the `--help` usage line (`reconcile [--repo NAME] [--apply] [--json]`) — the whole time
        // the parser was refusing it. `Text` stays the bare default; nothing about `--apply` changes it.
        | Reconcile -> Both Text
        // `who`, `budget` and `inbox` share one polarity, and it is the mirror of `ready`/`batch`: the
        // human table is what an operator or a worker reads, and `--json` opts into the machine contract
        // (case 20/25's rows, `pendingBoardWrites` — #862).
        | Who -> Both Text
        | Budget -> Both Text
        | Claim -> Both Text
        // `adopt` produces no stdout of its own — it delegates to `claim`, passing `opts` through, so
        // `claim`'s receipt is its receipt.
        //
        // `take` delegated the same way and INHERITED the row rather than earning it: the one arm it owns,
        // the empty queue, printed prose regardless of `Render`, so `take --json` was honoured only when it
        // actually claimed something. `.github#1525` closed that in `Client.fs` — the empty arm now emits
        // its own `kind:"none"` receipt — so the row is true of BOTH arms rather than of one, and this
        // comment records a caveat that was real and is now discharged, not one still standing.
        | Adopt -> Both Text
        | Take -> Both Text
        | Widen -> Both Text // both reach the shared `updateTouchSet` (#1517)
        | SetPaths -> Both Text
        | Inbox -> Both Text
        | Predicate -> Both Text
        | DiffAudit -> JsonOnly
        | LintCmd -> Both Text

        // ---- JSON ONLY: stdout is a machine document whatever the flag says ----------------------------
        // `scan` DOES match on `opts.Render` (`Program.fs`), and both arms print the same document —
        // deliberately, because the snapshot IS the contract and `scan | decide` must keep working. So the
        // match decides nothing on stdout, and `--text` was a no-op rather than a rendering: there is no
        // human projection of a snapshot to ask for. The PIPELINE is unaffected either way (`scan` bare and
        // `scan --text` printed identical bytes); what is refused is only the flag that never did anything.
        //
        // The now-unreachable `Text` arm and its comment live in `Program.fs`, which is outside this item's
        // touch-set. Filed as a follow-up rather than reached into.
        | Scan -> JsonOnly
        | CommandContractCmd -> JsonOnly
        | IntakeCmd -> JsonOnly
        | RouteCmd -> JsonOnly
        | BoardCmd -> JsonOnly
        | Issues -> JsonOnly // the raw REST array; the caller projects it with jq

        // ---- TEXT ONLY: prose, an id, or one verdict word — there is no machine projection to ask for ---
        // THIS IS THE #1523 BUCKET. Every row here advertised `--json` on all 40 commands and printed the
        // same prose with it as without. Two are load-bearing for the fleet and say why refusal beats
        // "make the handler honour it": `whoami --mint` prints `export FSGG_WORKER=…` for `eval`, and
        // `done` prints the `FSGG-DONE` stamp every driver greps. A JSON projection nobody consumes, on
        // either of those, is a fleet outage bought for nothing.
        | WhoAmI -> TextOnly
        | Next -> TextOnly
        // `reap` reports what it WOULD collect; the destructive collect is gated behind `--apply`, so the
        // bare form is a DRY RUN and the thing an operator reads before deciding is prose.
        | Reap -> TextOnly
        | Landable -> TextOnly // one verdict word; the decision is the exit code
        | Release -> TextOnly
        | Heartbeat -> TextOnly
        | SetField -> TextOnly
        | Child -> TextOnly
        | Overlap -> TextOnly
        | Say -> TextOnly
        | RoomOpen -> TextOnly
        | DoneCmd -> TextOnly
        | VerifyPaths -> TextOnly
        | Followup -> TextOnly
        | Bootstrap -> TextOnly
        | FieldId -> TextOnly // a bare id
        | OptionId -> TextOnly
        | ItemId -> TextOnly
        | Add -> TextOnly
        // `flush` REPLAYS by default (the opposite polarity to `reap --apply`, see `DryRun`), and what it
        // prints is a report an operator reads after the `EX_RATE` back-off.
        | Flush -> TextOnly
        | Help -> TextOnly
        | Version -> TextOnly

    /// The render mode a BARE invocation of a command is left at — derived from the same declaration that
    /// decides whether the flag may be given at all, so the two can no longer disagree.
    ///
    /// For a `JsonOnly`/`TextOnly` command this is simply what it already prints, which makes the declared
    /// default TRUE of the handler rather than an accident of the module `defaults` record. That is what
    /// disarms the trap: a future edit that teaches one of those handlers to read `opts.Render` finds the
    /// field already set to the mode it has always printed in.
    let private defaultRender (c: Command) : Render =
        match renderSupport c with
        | Both d -> d
        | JsonOnly -> Json
        | TextOnly -> Text

    /// Every nullary `Command` case, by reflection — the one enumeration that cannot drift from the DU.
    let private allCommands: Command list =
        Microsoft.FSharp.Reflection.FSharpType.GetUnionCases typeof<Command>
        |> Array.toList
        |> List.choose (fun case ->
            if case.GetFields().Length <> 0 then
                None
            else
                Some(Microsoft.FSharp.Reflection.FSharpValue.MakeUnion(case, [||]) :?> Command))

    /// The commands each render flag may be given to, DERIVED from `renderSupport` and computed ONCE.
    ///
    /// A hand-written pair of lists here would be the same defect one level along — a second copy of
    /// `renderSupport`, free to drift from it — and this board has spent five items on exactly that shape.
    /// Module-level `let` rather than a filter inside `scopeOf`, because `renderCommandContract` calls
    /// `scopeOf` once per (command, flag) pair and would otherwise rebuild both lists ~1,400 times.
    let private jsonReaders = allCommands |> List.filter (fun c -> renderSupport c <> TextOnly)

    let private textReaders = allCommands |> List.filter (fun c -> renderSupport c <> JsonOnly)

    /// THE FLAG SURFACE (#991) — every global flag, and the commands that READ it.
    ///
    /// The parser is one flat pass, so EVERY command accepts EVERY flag, and `unknown argument` — the
    /// refusal that catches a typo instantly — never fires on a flag the parser knows. A flag accepted and
    /// ignored is worse than one refused: the caller is told, by a green exit, that a thing happened which
    /// did not. `OptionsTests` calls that THE RESIDUE RULE and states it generally.
    ///
    /// Enforcement used to be one `match` arm on one field (`o.Status`, added by #867 for `release --status`).
    /// The rule was general; its enforcement was a special case — so every OTHER flag stayed unguarded, and
    /// every NEW flag was born unguarded. Measured when this replaced it: 38 flags, one of them checked.
    /// `release --force` was documented in the usage block above and read by NOTHING — #867's exact defect,
    /// in the very command #867 repaired.
    ///
    /// So the table is a TOTAL function over a closed `Flag`. That is the whole repair, and it is structural
    /// rather than diligent: this project sets `TreatWarningsAsErrors`, so FS0025 (incomplete match) is a
    /// BUILD ERROR — a new `Flag` case with no `scopeOf` row does not warn, it fails the build. Instance five
    /// is unwritable rather than merely discouraged. `Global` is a real answer — a flag every command honours
    /// — but it is an answer somebody gives, not the silence a missing row used to be.
    type private Flag =
        | FSnapshot
        /// `driver --events` (.github#2135) — see the `Events` field doc comment.
        | FEvents
        /// `driver --events --cursor <path>` (.github#2135) — see the `CursorFile` field doc comment.
        | FCursor
        | FLease
        | FRepo
        | FWorker
        | FEvidence
        | FPartial
        | FTo
        | FMessage
        | FPaths
        | FPr
        | FWarn
        | FIssue
        | FStatus
        | FBlockedBy
        | FAll
        | FBatch
        | FStrict
        | FActive
        | FApply
        | FPeek
        | FDryRun
        | FWait
        | FTries
        | FInterval
        | FRequire
        | FSha
        | FLabel
        | FState
        | FFresh
        | FIncludeBacklog
        | FForce
        /// `claim --refuse-overlap` (.github#2459) — turn the #353 collision report into a refusal.
        | FRefuseOverlap
        | FMint
        | FFlip
        | FLimit
        | FExplain
        | FLocal
        | FAllRepos
        | FOver
        /// `--json` and `--text` are TWO flags, not one spelling of one (#1523). They make DIFFERENT
        /// promises — "give me a machine document" and "give me a human one" — and a command can keep one
        /// without the other, which is exactly what `issues` (always JSON) and `done` (always prose) do.
        /// Collapsing them into a single row would force one of those two commands to advertise a promise
        /// it cannot keep, which is the defect being repaired.
        | FJson
        | FText

    type private FlagScope =
        /// Every command honours it. Named deliberately — the flags here are the ones whose readers really
        /// are the whole surface (`--repo`), or whose "was it given?" cannot be observed at all because the
        /// field has a non-optional default (`--lease` lands in `LeaseMinutes`): an unset flag is
        /// indistinguishable from its default, so there is nothing here to refuse.
        ///
        /// `--json`/`--text` USED TO BE HERE for that second reason, and #1523 measured what it cost: the
        /// exemption was true of the PARSER and silent about the RENDERER, so `command-contract` advertised
        /// `--json` on all 40 commands while 20 of them printed the same prose with it as without. The
        /// remedy is not a new checker — it is `RenderGiven`, which makes "given" observable, so the flag
        /// can leave `Global` and `scopeOf` can be the gate it already is for the other 38.
        | Global
        /// Only these commands READ it. Every other command refuses it rather than swallowing it.
        | Only of Command list

    /// Which commands read each flag. Derived by tracing each `opts.<Field>` read to its handler, NOT from
    /// the usage prose — the two disagreed, and where they disagreed the prose was wrong (`release --force`).
    let private scopeOf (f: Flag) : FlagScope =
        match f with
        | FRepo -> Global
        | FWorker -> Global

        // DERIVED, never listed (#1523) — see `jsonReaders`/`textReaders`.
        | FJson -> Only jsonReaders
        | FText -> Only textReaders

        | FSnapshot -> Only [ Decide; DeliveryCmd; ReviewCmd; DriverCmd; CycleCmd; LanesView ]

        // `driver --events`/`--cursor` (.github#2135) — the projection mode of `driver`. `DriverCmd`
        // only; every other command refuses them exactly as it refuses `--snapshot`.
        | FEvents -> Only [ DriverCmd ]
        | FCursor -> Only [ DriverCmd ]

        | FLease -> Only [ Scan; Claim; Take; Adopt ]

        // `--status`: #867's original row, now one of many rather than the only one.
        //
        // `Add` joined it for .github#1823. `add` DEFAULTS the column to `Backlog`, and "when the caller
        // gives none" is only a meaningful clause if there is a way to give one — so the flag is the
        // other half of that default, not decoration. Scoped here rather than left `Global` for #867's
        // own reason: a `--status` accepted and ignored is a green exit telling the caller something
        // happened which did not.
        | FStatus -> Only [ Ready; Release; Add ]

        // `release --blocked-by` (.github#2079) — the ONE call that pairs the `Blocked by` field write
        // with `--status Blocked`, so `release` is its only reader; every other command refuses it.
        | FBlockedBy -> Only [ Release ]

        | FMint -> Only [ WhoAmI ]
        | FLocal -> Only [ Who ]
        | FAllRepos -> Only [ Who ]
        | FAll -> Only [ Ready ]
        | FActive -> Only [ Overlap ]
        | FApply -> Only [ DeliveryCmd; Reap; Reconcile; Followup ]
        | FPeek -> Only [ Inbox ]
        | FDryRun -> Only [ Flush ]
        | FStrict -> Only [ LintCmd ]
        | FBatch -> Only [ SetField ]
        | FPaths -> Only [ Widen; SetPaths; DiffAudit ]
        | FTo -> Only [ Say ]
        | FMessage -> Only [ Say ]
        | FEvidence -> Only [ DoneCmd ]
        | FFlip -> Only [ DoneCmd ]
        | FPartial -> Only [ DoneCmd ]
        | FPr -> Only [ DeliveryCmd; ReviewCmd; DoneCmd; VerifyPaths ]
        | FIssue -> Only [ VerifyPaths ]
        | FWarn -> Only [ VerifyPaths ]
        | FWait -> Only [ Landable ]
        | FTries -> Only [ Landable ]
        | FInterval -> Only [ Landable ]
        | FRequire -> Only [ Landable ]
        | FSha -> Only [ Landable ]
        | FLabel -> Only [ Issues ]
        | FState -> Only [ Issues ]

        | FOver -> Only [ RoomOpen ]

        // `--force` STEALS A LIVE CLAIM, and secondarily bypasses the #516 one-item-per-worker check.
        // `claim` is the ONLY reader. The usage block advertised `release [--force]` for the whole life of
        // the port and `release` never read it — a documented no-op, found by building this table (#991).
        // The usage line lost it; refusing it here breaks no working behaviour, because there was none.
        //
        // #1620 IS #991'S OTHER HALF, and it lived in this very row. `--force` was scoped correctly — one
        // reader, `claim`, exactly as written — but that reader consulted it only for the #516 pre-check,
        // while `adopt`'s refusal and the usage line above both advertised it as the way to take another
        // worker's item. A flag can be documented as read by the right command and still be wired to the
        // wrong DECISION inside it, which is the shape a scope table cannot see. The steal in
        // `Writes.claim`'s `Lost` arm is what made the advertisement true; `ForceStealTests` is what pins
        // the two meanings together, by driving ONE board through argv twice — with the flag and without —
        // and requiring different outcomes. (Not this table, and not `CommandSurfaceTests`: both were
        // green throughout the defect's whole life.)
        | FForce -> Only [ Claim ]

        // .github#2459 — `claim` ONLY. `take`/`adopt` both delegate to `Client.claim` internally, but
        // NEITHER accepts this flag on its own argv: `take` never even reaches the collision scan this
        // gates (it skips it entirely, having already run the scheduler's own overlap-avoidance), and
        // `adopt` transfers a claim under the same permissive default `claim` itself uses. A caller that
        // wants the strict guarantee for an adopted item runs `claim --refuse-overlap` directly once the
        // orphan is identified, exactly as `claim --force` is already the steal primitive `adopt` does not
        // expose on its own argv either.
        | FRefuseOverlap -> Only [ Claim ]

        // Scheduling reads take their freshness from a `Cache.ReadIntent`, not from this flag, so
        // `batch --fresh` / `next --fresh` / `take --fresh` never did anything. Only these three read it.
        | FFresh -> Only [ Scan; Bootstrap; Issues ]

        // `next` and `take` OVERRIDE the cap to 1 (they are `batch` capped at one), so `-n` is dead on both.
        | FLimit -> Only [ Scan; BatchCmd ]

        | FIncludeBacklog -> Only [ Scan; Next; BatchCmd; Take ]

        // `batch` ONLY (.github#1598). `--explain` prints the derived RANKING — every candidate in the
        // order the scheduler considered it, with the rank inputs that put it there. `next` and `take`
        // are `batch` capped at one and print a single ref; a ranking of one candidate answers nothing,
        // and `decide` has no board columns to rank on (its snapshot carries no `Phase` and no age).
        | FExplain -> Only [ BatchCmd ]

    /// EVERY flag's argv spelling — the CANONICAL one a machine consumer matches on, and its aliases.
    ///
    /// Lifted out of `renderCommandContract` by #1534, which needed the same Flag → spelling map to name
    /// the flag a CONDITIONAL write is gated on (`writeSurface` below). A second copy inside the emitter
    /// would be free to spell `--apply` one way in a command's `flags` array and another in its
    /// `writesWhen`, which is the two-copies-of-one-fact shape #1507/#1510/#1515/#1523/#1528 have each
    /// cost this board an item.
    ///
    /// A TOTAL MATCH RATHER THAN A LOOKUP LIST, and the difference is a crash path that does not exist.
    /// The list form this replaced could not answer for a `Flag` nobody had added to it, so it needed a
    /// runtime `failwith` — and a comment claiming the gap was unreachable. It was: the list was in fact
    /// total over `Flag`, so the comment justified a fallback against data that contradicted it. Written
    /// as a match, FS0025 under `TreatWarningsAsErrors` makes a new `Flag` with no spelling a BUILD ERROR,
    /// exactly as `scopeOf` above and `writeSurface` below are — and there is nothing left to fail at.
    ///
    /// The CANONICAL spelling is returned separately from the aliases because the two are read for
    /// different jobs: `writesWhen` names one flag a consumer must look for on argv, while the emitted
    /// `flags` array advertises every spelling the parser accepts. `--fresh`/`--refresh` is the one flag
    /// where they differ, and flattening them would make the contract advertise a canonical spelling it
    /// picked arbitrarily.
    let private spellingsOf (f: Flag) : string * string list =
        match f with
        | FSnapshot -> "--snapshot", []
        | FEvents -> "--events", []
        | FCursor -> "--cursor", []
        | FLease -> "--lease", []
        | FRepo -> "--repo", []
        | FWorker -> "--worker", []
        | FEvidence -> "--evidence", []
        | FPartial -> "--partial", []
        | FTo -> "--to", []
        | FMessage -> "--message", []
        | FPaths -> "--paths", []
        | FPr -> "--pr", []
        | FWarn -> "--warn", []
        | FIssue -> "--issue", []
        | FStatus -> "--status", []
        | FBlockedBy -> "--blocked-by", []
        | FAll -> "--all", []
        | FBatch -> "--batch", []
        | FExplain -> "--explain", []
        | FStrict -> "--strict", []
        | FActive -> "--active", []
        | FApply -> "--apply", []
        | FPeek -> "--peek", []
        | FDryRun -> "--dry-run", []
        | FWait -> "--wait", []
        | FTries -> "--tries", []
        | FInterval -> "--interval", []
        | FRequire -> "--require", []
        | FSha -> "--sha", []
        | FLabel -> "--label", []
        | FState -> "--state", []
        | FFresh -> "--fresh", [ "--refresh" ]
        | FIncludeBacklog -> "--include-backlog", []
        | FForce -> "--force", []
        | FRefuseOverlap -> "--refuse-overlap", []
        | FMint -> "--mint", []
        | FFlip -> "--flip", []
        | FLimit -> "-n", []
        | FLocal -> "--local", []
        | FAllRepos -> "--all-repos", []
        | FOver -> "--over", []
        // #1523 — these two used to be spliced onto every emitted row unconditionally, which is how the
        // contract came to promise `--json` on 20 commands that print prose. They are ordinary scoped
        // flags now, so the contract and `scopeOf` agree BY CONSTRUCTION rather than by a test noticing.
        | FJson -> "--json", []
        | FText -> "--text", []

    /// The one spelling a consumer matches on argv. Total, because `spellingsOf` is. This is also the
    /// spelling `flagsGiven` names in a refusal (#1573), so the emitted contract and the residue rule
    /// cannot silently disagree.
    let private spellingOf (f: Flag) : string = fst (spellingsOf f)

    /// Every `Flag` case, by reflection — the same move `allCommands` makes for the `Command` union, and
    /// for the same reason: an enumeration written by hand is one a new case can be left out of. Paired
    /// with `spellingsOf`'s totality this makes the emitted flag surface unable to omit a flag silently.
    ///
    /// THE BINDING FLAGS ARE LOAD-BEARING, unlike `allCommands`'s. `Command` is public and reflects with
    /// the defaults; `Flag` is `private`, and without `NonPublic` both calls below refuse — `GetUnionCases`
    /// returns nothing and `MakeUnion` throws, inside a module-level `let`, which surfaces as a
    /// `TypeInitializationException` on the FIRST parse of ANY argv. Measured while writing this: every
    /// verb exited 2 with a stack trace. Copying `allCommands`'s spelling here looks right and is not.
    let private allFlags: Flag list =
        let binding =
            System.Reflection.BindingFlags.NonPublic ||| System.Reflection.BindingFlags.Public

        Microsoft.FSharp.Reflection.FSharpType.GetUnionCases(typeof<Flag>, binding)
        |> Array.toList
        |> List.map (fun case ->
            Microsoft.FSharp.Reflection.FSharpValue.MakeUnion(case, [||], binding) :?> Flag)

    /// EVERY flag and all its spellings — what `renderCommandContract` advertises per row, derived rather
    /// than listed. The emitter sorts each row's flags, so this list's order is not observable.
    let private scopedFlags: (Flag * string list) list =
        allFlags
        |> List.map (fun f ->
            let canonical, aliases = spellingsOf f
            f, canonical :: aliases)

    /// WHEN A CONDITIONAL WRITE IS ON — carried as a typed `Flag`, never a string, so the spelling emitted
    /// in the contract is the SAME spelling the command's `flags` array advertises (`spellingOf`), and a
    /// gate naming a flag the command cannot even be given is catchable rather than plausible-looking.
    type private WriteGate =
        /// Writing is OFF until the flag is GIVEN. `reap --apply`, `reconcile --apply` — the bare form is a
        /// DRY RUN, which is the case #1534 asked not to be flattened away.
        | OnlyWhenGiven of Flag
        /// Writing is ON until the flag is given — the OPPOSITE polarity, and it is real: `flush` REPLAYS
        /// the deferred board writes by default and `--dry-run` only lists them. Modelling this as
        /// `OnlyWhenGiven` inverted, or as an unconditional write, are both lies in a field whose whole
        /// purpose is that a guard can trust it.
        | UnlessGiven of Flag
        /// The condition is not on argv AT ALL, so no parse of the command line can decide it. Carries the
        /// reason, because a consumer that cannot see the condition needs to know it is not looking in the
        /// wrong place.
        ///
        /// RESERVED FOR A WRITE THAT IS NOT WHAT THE VERB IS FOR, and that line has to be drawn or this
        /// case swallows the surface: `take` on an empty queue, `add` on an already-boarded item and
        /// `child` on an existing sub-issue all decline to write on runtime state too, and all three are
        /// `Writes` — mutation is their PURPOSE, and a consumer must treat them as writers. `next` is the
        /// one verb where it is not: it is a scheduling READ that, after printing its answer, makes the
        /// #733 chore OFFER and POSTs a claim marker — to the repo's chore-LOCK issue, which is not even
        /// the issue it just named. That is why the shim's #1528 note calls names "not evidence".
        | NotOnArgv of because: string

    /// DOES THIS COMMAND MUTATE STATE THE FLEET SHARES? (#1534)
    ///
    /// The engine has always known this and has never SAID it. `command-contract` emits a name and a flag
    /// list per verb, derived from the type system, and nothing about write-ness — so the one consumer that
    /// needs the fact, the stale-engine guard in `scripts/fsgg-coord`, kept its own hand-written copy in
    /// bash. #1528 measured what that cost: `set-paths` reached the same `Writes.widen` PATCH as `widen`
    /// through the same helper and was absent; `room` and `reconcile` were absent; and `bootstrap`, believed
    /// to create the project, is a pair of GraphQL QUERIES that adding would have refused for nothing.
    ///
    /// TOTAL, WITH NO WILDCARD ARM, AND THAT IS THE STRUCTURAL PART. This project sets
    /// `TreatWarningsAsErrors`, so FS0025 (incomplete match) is a BUILD ERROR — the same property
    /// `renderSupport` and `scopeOf` already buy for the renderers and the flag surface. A verb added to the
    /// `Command` union does not default to `Reads`; it does not compile. A `| _ -> Reads` arm added here
    /// would silently give that guarantee back, which is the single most damaging edit this file accepts.
    ///
    /// WHAT IT IS AND IS NOT DERIVED FROM, stated plainly because the honest limit matters more than the
    /// claim. The GATE — which flag turns a conditional write on — is typed and cross-checked: it is a
    /// `Flag`, spelled through `scopedFlags`, and `CommandSurfaceTests` asserts `scopeOf` really gives that
    /// flag to that command. The BASE read/write split is not derivable here at all: it is a property of
    /// `Client.fs` control flow, `Options.fs` compiles BEFORE `Client.fs` and cannot reference it, and a
    /// text analysis over-approximates badly (advice strings in `who` and `lint` name `claim`, `reap` and
    /// `done`). So the rows below are a person's reading of `Client.fs`, held total by the compiler and
    /// carrying their evidence in the comment beside them. That is strictly better than a bash string with
    /// nothing comparing it to anything — it is not the same thing as proof, and the follow-on that would
    /// make it executable (drive every verb at a fake GitHub and fail on any mutating request) is filed.
    ///
    /// DELIBERATELY NOT READ AT RUN TIME BY THE SHIM, and the issue that asked for this field said so first.
    /// The engine that would answer "am I stale?" is the stale one; an engine built before this field
    /// existed emits no field, a shim deriving its write-set from that derives an EMPTY one, and every board
    /// write is permitted on precisely the oldest artifacts. The consumer is the parity GATE, where a
    /// freshly built engine and the shim's text are both present and neither is suspect.
    ///
    /// LOCAL WRITES ARE NOT WRITES HERE, AND "LOCAL" DOES NOT MEAN "PRIVATE". `inbox` advances a cursor
    /// (`Cache.putInboxCursor`), `board`/`bootstrap`/`field-id`/`option-id`/`item-id` write a board-map or
    /// item-id cache, and `followup` is a queue file — all under `$XDG_CACHE_HOME`. Only the inbox cursor
    /// is keyed on the WORKER; the board map and item-id caches are keyed on owner/repo, so every worker on
    /// one machine shares them (`Cache.fs` says so, and #881 is filed against the sibling queue file for
    /// exactly that). The line this field draws is not "nobody else sees it" — it is THE BOARD: a cache is
    /// re-derivable from a read, and what a stale engine corrupts there costs a re-fetch, not a claim.
    ///
    /// A DRY RUN THAT MISREPORTS IS NOT WHAT THIS FIELD GUARDS. `reap` bare is `Reads`-shaped in its
    /// effects and is still classified `OnlyWhenGiven FApply` rather than `Reads`, because the QUESTION is
    /// "could this verb, as invoked, mutate?" — and a consumer that wants to permit the bare form must
    /// decide that for itself, from a condition this field now hands it.
    type private WriteSurface =
        /// Every invocation may mutate shared state. A no-op because there was nothing to do — `take` on an
        /// empty queue, `add` on an item already boarded — is still this: mutation is what the verb is FOR,
        /// and no argv shape avoids it.
        | Writes
        /// No invocation mutates shared state. Local files and GraphQL/REST QUERIES are not mutation.
        | Reads
        /// Some invocations mutate it and some do not; the gate says which.
        | WritesIf of WriteGate

    let private writeSurface (c: Command) : WriteSurface =
        match c with
        // ---- PURE DECISION — no board, no network at all (ADR-0034) ------------------------------------
        | Decide -> Reads
        | DeliveryCmd -> WritesIf(OnlyWhenGiven FApply)
        // .github#2175: `review` (snapshot or live) only ever inspects — unlike `delivery` it has no
        // `--apply`/mutating arm at all, so it is unconditionally `Reads`, beside `DriverCmd`/`CycleCmd`.
        | ReviewCmd -> Reads
        | DriverCmd -> Reads
        | CycleCmd -> Reads
        | LanesView -> Reads
        | Facts -> Reads
        | CommandContractCmd -> Reads
        | IntakeCmd -> Writes // `intake apply` creates/reuses and projects the receipt-bound issue
        | RouteCmd -> Writes // `record` appends the validated durable decision receipt

        // ---- LOCAL — a file, an identity, a registry read; no token, no board --------------------------
        | WhoAmI -> Reads // derives/mints an id; `--mint` prints one, it does not register it
        | Followup -> Reads // the #1063 queue is a FILE, which is the whole reason it survives EX_RATE
        | Predicate -> Reads // ADR-0050 oracle: registry + producer manifests off disk
        | DiffAudit -> Reads // immutable git-object inspection; no board or network write

        // ---- READ THE BOARD — GraphQL/REST QUERIES, plus a per-user cache file -------------------------
        | Scan -> Reads // POSTs to `graphql`, which is how a GraphQL READ is spelled
        | BatchCmd -> Reads // `next` uncapped; makes NO chore offer, which is what separates the two
        | Ready -> Reads
        | Who -> Reads
        | Budget -> Reads
        | Landable -> Reads
        | Overlap -> Reads
        | VerifyPaths -> Reads
        | LintCmd -> Reads
        | Issues -> Reads
        | Inbox -> Reads // the cursor it advances is `Cache.putInboxCursor` — per-worker, local
        | Bootstrap -> Reads // `Board.bootstrap`: two GraphQL queries, then day-cached
        | BoardCmd -> Reads
        | FieldId -> Reads
        | OptionId -> Reads
        | ItemId -> Reads

        // ---- WRITE THE BOARD, EVERY INVOCATION ---------------------------------------------------------
        | Claim -> Writes // POSTs the `fsgg:claim` marker comment and writes Status
        | Take -> Writes // schedules, then delegates to `claim`
        | Adopt -> Writes // transfers a claim, also via `claim`
        | Release -> Writes
        | Heartbeat -> Writes
        | Add -> Writes // idempotent — `AlreadyOnBoard` skips the mutation, which is state, not argv
        | SetField -> Writes
        | Child -> Writes // idempotent the same way; an existing sub-issue is not POSTed twice
        | Say -> Writes // no lock required, and every invocation POSTs
        // Up to FIVE independent writes: board Status, the `--flip` ancestor roll-up (which also CLOSES
        // issues over REST), the room close, its own marker release (#533) — and the #733 chore offer, the
        // SECOND `Chores.offer` site after `next`. None of that changes the row; it is why the row is here.
        | DoneCmd -> Writes
        | RoomOpen -> Writes // `Writes.createRoom` + a `Rooms: #room` back-reference onto each named item
        // Both reach the same `Writes.widen` PATCH through the same `updateTouchSet` helper. `set-paths`
        // being the RECOVERY verb is exactly why its absence from the shim's list mattered (#1528).
        | Widen -> Writes
        | SetPaths -> Writes

        // ---- WRITE UNDER A CONDITION -------------------------------------------------------------------
        | Reap -> WritesIf(OnlyWhenGiven FApply)
        | Reconcile -> WritesIf(OnlyWhenGiven FApply)
        // AND THIS ROW IS NOT THE SHIM'S ROW — read before wiring the #1534 follow-on gate. The shim puts
        // `flush` in BOARD_WRITES (unconditional) and this says `conditional`, and BOTH are right about
        // different questions: the shim's two write sets are REFUSED IDENTICALLY under staleness, so its
        // split records "is this flag-dependent?" for the reader, while this records "could this invocation
        // mutate?" for a consumer. A gate asserting `BOARD_WRITES == {always}` set-for-set therefore reds on
        // `flush` over no defect at all. The sound assertion is over the UNION —
        // `BOARD_WRITES ∪ BOARD_WRITES_CONDITIONAL == {always} ∪ {conditional}` and
        // `BOARD_READS == {never}` — which is exactly the fact the guard acts on.
        | Flush -> WritesIf(UnlessGiven FDryRun)
        | Next ->
            WritesIf(
                NotOnArgv "after printing its answer it makes the #733 chore offer, which POSTs a claim marker"
            )

        // ---- NOT VERBS — reached by flag, and excluded from the emitted contract entirely ---------------
        | Help -> Reads
        | Version -> Reads

    /// The flags actually GIVEN, with the spelling to name in a refusal. A flag whose field has a
    /// non-optional default and no record of having been given (`LeaseMinutes`) cannot appear: "given" and
    /// "defaulted" are the same state, so it is `Global` above and there is nothing to detect.
    ///
    /// `--json`/`--text` were in that sentence until #1523. They are detectable now because `RenderGiven`
    /// records the ACT of giving them separately from the `Render` they set — the smallest change that
    /// turns an unguardable flag into a guarded one.
    let private flagsGiven (o: Options) : (Flag * string) list =
        [ if o.SnapshotFile.IsSome then FSnapshot
          if o.Events then FEvents
          if o.CursorFile.IsSome then FCursor
          if o.Repo.IsSome then FRepo
          if o.Worker.IsSome then FWorker
          if o.Evidence.IsSome then FEvidence
          if o.Partial.IsSome then FPartial
          if o.ToWorker.IsSome then FTo
          if o.Message.IsSome then FMessage
          if not (List.isEmpty o.Paths) then FPaths
          if o.Pr.IsSome then FPr
          if o.Warn then FWarn
          if o.Issue.IsSome then FIssue
          if o.Status.IsSome then FStatus
          if o.BlockedBy.IsSome then FBlockedBy
          if o.All then FAll
          if o.Batch then FBatch
          if o.Strict then FStrict
          if o.Active then FActive
          if o.Apply then FApply
          if o.Peek then FPeek
          if o.Local then FLocal
          if o.AllRepos then FAllRepos
          if o.DryRun then FDryRun
          if o.Wait then FWait
          if o.Tries.IsSome then FTries
          if o.Interval.IsSome then FInterval
          if not (List.isEmpty o.Require) then FRequire
          if o.Sha.IsSome then FSha
          if o.Label.IsSome then FLabel
          if o.IssueState.IsSome then FState
          if o.Fresh then FFresh
          if o.AllowBacklog then FIncludeBacklog
          if o.Force then FForce
          if o.RefuseOverlap then FRefuseOverlap
          if o.Mint then FMint
          if o.Flip then FFlip
          if not (List.isEmpty o.Over) then FOver
          if o.Limit.IsSome then FLimit
          if o.Explain then FExplain
          if o.LeaseGiven then FLease

          // LAST, deliberately. `validate` reports the FIRST residue it finds, and putting these at the
          // head changed the message for inputs that were already wrong for another reason:
          // `next --snapshot x --json` used to name `--snapshot` and point at `decide`/`lanes`, which is
          // the more useful answer. Adding a guard should not re-word an existing refusal.
          //
          // BOTH spellings are reported when both were typed — see `RenderGiven`. One command can hold a
          // legal `--text` and an illegal `--json` at the same time, and only one of them is a finding.
          if o.RenderGiven.Contains Json then FJson
          if o.RenderGiven.Contains Text then FText ]
        |> List.map (fun flag -> flag, spellingOf flag)

    /// The argv spelling of a command — the word a refusal must name, because it is the word the caller
    /// typed. Total over `Command`, so a new verb cannot be named `%A` by accident.
    let commandName (c: Command) : string =
        match c with
        | Decide -> "decide"
        | DeliveryCmd -> "delivery"
        | ReviewCmd -> "review"
        | DriverCmd -> "driver"
        | CycleCmd -> "cycle"
        | Scan -> "scan"
        | LanesView -> "lanes"
        | Facts -> "facts"
        | CommandContractCmd -> "command-contract"
        | IntakeCmd -> "intake"
        | RouteCmd -> "delivery-route"
        | WhoAmI -> "whoami"
        | Budget -> "budget"
        | Next -> "next"
        | BatchCmd -> "batch"
        | Ready -> "ready"
        | Reconcile -> "reconcile"
        | Who -> "who"
        | Reap -> "reap"
        | Claim -> "claim"
        | Adopt -> "adopt"
        | Landable -> "landable"
        | Take -> "take"
        | Release -> "release"
        | Heartbeat -> "heartbeat"
        | SetField -> "set-field"
        | Child -> "child"
        | Widen -> "widen"
        | SetPaths -> "set-paths"
        | Overlap -> "overlap"
        | Say -> "say"
        | Inbox -> "inbox"
        | DoneCmd -> "done"
        | VerifyPaths -> "verify-paths"
        | Bootstrap -> "bootstrap"
        | BoardCmd -> "board"
        | FieldId -> "field-id"
        | OptionId -> "option-id"
        | ItemId -> "item-id"
        | Add -> "add"
        | Flush -> "flush"
        | LintCmd -> "lint"
        | Issues -> "issues"
        | Followup -> "followup"
        | Predicate -> "predicate"
        | DiffAudit -> "diff-audit"
        | RoomOpen -> "room open"
        | Help -> "--help"
        | Version -> "--version"

    let renderCommandContract () =
        let commands =
            Microsoft.FSharp.Reflection.FSharpType.GetUnionCases typeof<Command>
            |> Array.choose (fun case ->
                if case.GetFields().Length <> 0 then
                    None
                else
                    let command =
                        Microsoft.FSharp.Reflection.FSharpValue.MakeUnion(case, [||]) :?> Command

                    match command with
                    | Help
                    | Version -> None
                    | _ -> Some command)
            |> Array.sortBy commandName

        use stream = new System.IO.MemoryStream()
        use writer =
            new System.Text.Json.Utf8JsonWriter(
                stream,
                System.Text.Json.JsonWriterOptions(Indented = true, SkipValidation = false)
            )

        writer.WriteStartObject()
        writer.WriteString("schema", "fsgg.coord.commands/1")
        writer.WriteStartArray("commands")

        for command in commands do
            writer.WriteStartObject()
            writer.WriteString("name", commandName command)
            writer.WriteStartArray("flags")

            let flags =
                // Every emitted flag is derived from `scopeOf`; `LeaseGiven` makes `--lease` observable,
                // so it needs no special-case injection (#1544).
                scopedFlags
                 |> List.collect (fun (flag, spellings) ->
                     match scopeOf flag with
                     | Global -> spellings
                     | Only readers when List.contains command readers -> spellings
                     | Only _ -> [])
                |> List.distinct
                |> List.sort

            for flag in flags do
                writer.WriteStringValue flag

            writer.WriteEndArray()

            // WRITE-NESS (#1534) — the fact the shim's stale-engine guard duplicated by hand in bash, said
            // out loud by the engine that owns it. Three values, and the third is not a hedge: a consumer
            // that flattened `reap` into "writes" would refuse a dry run it could have permitted, and one
            // that flattened it into "reads" would let a stale engine collect live claims.
            //
            // ADDITIVE, AND THE SCHEMA ID DOES NOT MOVE. `fsgg.coord.commands/1` promises the keys it names;
            // it does not promise their absence. The three consumers in the tree all read by key and are
            // unaffected by a new one: `scripts/check-skill-quality.py` (`name` + `flags`),
            // `tests/coord-engine-parity/shim.sh` §3b (`.commands[].name` via `jq`, and nothing else), and
            // `CommandSurfaceTests` in this repo's own suite.
            //
            // A BUMP TO `/2` IS LOUD ON EVERY READER THAT CHECKS — worth stating because it was not
            // always. Two of the three compare the id for equality, and since #1574 they agree on what
            // a mismatch means: `check-skill-quality.py` fails in BOTH of its readers, with distinct
            // actionable diagnostics — `validate_invocations` reports the unsupported schema, and
            // `validate_semantics` names the `--paths`/`--apply`/`--dry-run` polarity assertions it did
            // NOT make — and `CommandSurfaceTests` asserts the id outright. The disarming case is gone:
            // that second reader used to return BARE, dropping every semantic assertion without
            // reporting one, and the id is now stated once as `CONTRACT_SCHEMA` rather than twice, so
            // two readers of one document can no longer disagree about whether it is supported. So the
            // cost of a bump is now LOUD rather than silent — but it is not one edit, and the third of
            // the three id literals that move with it is a TRAP. `CONTRACT_SCHEMA` and
            // `CommandSurfaceTests` are the obvious two. The third is the `/2` MUTANT in
            // `tests/skill-quality/run.sh`, which `expect_rejection` feeds the gate to prove an
            // unsupported schema still fails: make `/2` the SUPPORTED id and that mutant becomes a
            // supported document, the gate exits 0, and the fixture goes red asserting the opposite of
            // what it means.
            let writes, gate =
                match writeSurface command with
                | Writes -> "always", None
                | Reads -> "never", None
                | WritesIf gate -> "conditional", Some gate

            writer.WriteString("writes", writes)

            match gate with
            | None -> ()
            | Some gate ->
                // Emitted ONLY for `conditional`, so `has("writesWhen")` and `writes == "conditional"` are
                // the same question asked twice rather than two facts that can disagree.
                writer.WriteStartObject "writesWhen"

                match gate with
                | OnlyWhenGiven flag -> writer.WriteString("flagGiven", spellingOf flag)
                | UnlessGiven flag -> writer.WriteString("flagAbsent", spellingOf flag)
                | NotOnArgv because -> writer.WriteString("argvCannotSay", because)

                writer.WriteEndObject()

            writer.WriteEndObject()

        writer.WriteEndArray()
        writer.WriteEndObject()
        writer.Flush()
        System.Text.Encoding.UTF8.GetString(stream.ToArray())

    /// A `--repo` token → the repo NAME board rows carry. A registry short-id maps, an `owner/repo` keeps its
    /// repo part, a literal name passes through — so `--repo sdd`, `--repo FS-GG/FS.GG.SDD` and
    /// `--repo FS.GG.SDD` all name one queue, which is `--repo`'s documented contract in the skill's Setup
    /// section. The roster map is EMBEDDED rather than read from `registry/repos.yml` because the shim ships
    /// as a `kind: client` kit item WITHOUT the roster (case 13 §6c / #381).
    ///
    /// IT LIVES IN THE PARSER BECAUSE THAT IS THE ONLY PLACE EVERY VERB REACHES (#962). It used to live in
    /// `Client`, applied per-verb at a dispatch site, and being left out of that list is a silent fail-open:
    /// the raw token reaches a verbatim `String.Equals` against the row's repo, matches nothing, and reports
    /// an EMPTY QUEUE with exit 0 over a full board. That has now happened three times — #381 (`resolve_repo`
    /// hard-coded 4 short-ids, so `--repo game` matched nothing), #446 (`issues`, the one verb that never
    /// called it), #962 (`ready`, the same, in the F# port) — and each repair added the missing verb, which
    /// fixes the instance and not the thing that makes instances.
    ///
    /// `Client.run` is NOT that funnel and cannot be: `scan` is dispatched straight from `Program` and reads
    /// `opts.Repo` itself, so a resolution living in `run` leaves `scan --repo sdd` reporting `0 candidate(s)`
    /// over a full board — the same bug, in the fix for the same bug. `parse` is the one gate with no way
    /// around it, which is the argument `normalizeSay` below already makes: argument shape is the parser's job.
    ///
    /// Idempotent, so a caller that resolves again for its own reasons gets the same answer: a resolved name
    /// has no slash and is not a short-id, so it maps to itself.
    let resolveRepo (raw: string) : FS.GG.Coord.RepoScope.Scope =
        FS.GG.Coord.RepoScope.resolve raw

    /// The display-string ECHO policy: every caller below wants the resolved token back as a plain
    /// string regardless of which arm it is — a `--repo` filter value, or a chore-lock repo comparison
    /// — the same behaviour `resolveRepo` gave before it was tagged `Scope` (#2398). A `NonRepository`
    /// token round-trips unchanged (`RepoScope.resolve`'s own doc), matching the pre-fix "literal name
    /// passes through" contract byte-for-byte. An exhaustive two-arm match, not `Option.defaultValue`
    /// or a wildcard, so a third `Scope` arm added later fails THIS build (FS0025) rather than silently
    /// picking a string for it.
    let private resolveRepoName (raw: string) : string =
        match resolveRepo raw with
        | FS.GG.Coord.RepoScope.Repository name -> name
        | FS.GG.Coord.RepoScope.NonRepository token -> token

    /// An owner + repo → the CLOSED issue whose comments are that repo's CHORE-LOCK CAS subject (ADR-0041).
    /// `None` ⇒ `offer` refuses, which is the whole contract: a chore queue that cannot find its lock must
    /// offer nothing and never broadcast — condition 1 fails CLOSED, like every other "could not look" in
    /// this engine (#266, #421).
    ///
    /// EMBEDDED, BESIDE THE ROSTER, FOR THE ROSTER'S OWN REASON (ADR-0042, .github#1026). ADR-0041 recorded
    /// this number in `registry/repos.yml`, and the engine has no YAML reader — deliberately: the shim ships
    /// as a `kind: client` kit item WITHOUT the roster (case 13 §6c / #381), so a `repos.yml` reader would be
    /// absent exactly where receivers run and `offer` would refuse there forever. That inverts the mechanism
    /// #733 is for — a queue that "amortises maintenance across a fleet already calling the tool constantly"
    /// would amortise across the one repo that has the file. So the ref lives where the roster lives, and
    /// pays the roster's price: growth is a code edit here rather than a data change (ADR-0019).
    ///
    /// KEYED ON OWNER AS WELL AS REPO, AND THAT IS THE FAIL-CLOSED PART. The owner is configurable
    /// (`FSGG_COORD_OWNER`, default `FS-GG` — `Client.fs`/`Program.fs`), and these numbers are FS-GG's
    /// issues. Keyed on the repo alone, a caller under another owner would be handed `<their-owner>/.github`
    /// number 1033 — a real ref naming an unrelated issue, which is a lock that protects nothing while
    /// reporting that it does. An owner this map does not know has no lock, so it gets `None` and `offer`
    /// stays shut.
    ///
    /// ALL SEVEN REPOS HAVE A LOCK (#1087). `.github#1033` was the first (#733); the six receivers' locks were
    /// created and wired here as the #1087 rollout, so the chore queue drains in every repo rather than only
    /// the one that used to have a lock issue. A repo NOT in this table is still `None` — the fail-closed
    /// default is unchanged, it just now has six fewer members.
    ///
    /// THE NUMBERS ARE THE CLOSED `[chore-lock]` ISSUES, one per repo (each says so in its own body). The map
    /// is the ONLY record the engine reads — ADR-0042: no YAML reader, because the shim ships to receivers
    /// without the roster (above). So a lock issue's number lives here and nowhere the engine consults at
    /// runtime; the issue's body names this file as the place it is embedded, and that pairing is the whole
    /// coherence contract (change one, change the other).
    let private choreLockNumbers: (string * int) list =
        // (canonical repo, its closed chore-lock issue number). Canonical spellings only — the lookup below
        // canonicalises the caller's input through `resolveRepo` before matching, so `Governance`, `governance`
        // and `FS.GG.Governance` all find this row, and the Ref that comes back is built from THIS spelling.
        [ ".github", 1033
          "FS.GG.SDD", 518
          "FS.GG.Rendering", 878
          "FS.GG.Governance", 268
          "FS.GG.Templates", 252
          "FS.GG.Game", 406
          "FS.GG.Audio", 183 ]

    let choreLockRef
        (extra: FS.GG.Coord.Types.Ref list)
        (owner: string)
        (repo: string)
        : FS.GG.Coord.Types.Ref option =
        // KEYED ON OWNER TOO, and that is the fail-closed part (see the doc above): a repo neither the
        // embedded table nor `extra` knows gets `None`, so `offer` refuses rather than broadcasts.
        // `.ToLowerInvariant()` throughout, matching `resolveRepo` — the resolver opens no `System`.
        //
        // `extra` is the per-deployment roster a VENDORED tenant injects by env (`FSGG_COORD_CHORE_LOCKS`,
        // parsed in `Client.fs`, ADR-0042). It is matched on (owner, repo) so it works under ANY owner, and
        // it is consulted FIRST so a deployment can repoint a lock without a code change. The EMBEDDED table
        // below stays gated to `FS-GG`: its numbers are FS-GG's issues, so a caller under another owner is
        // still never handed one — the invariant that kept a foreign owner from a real-but-unrelated ref is
        // unchanged; the vendored tenant now brings its OWN refs rather than borrowing FS-GG's.
        let ownerLc = owner.ToLowerInvariant()
        let repoLc = (resolveRepoName repo).ToLowerInvariant()

        let fromExtra =
            extra
            |> List.tryFind (fun r ->
                r.Owner.ToLowerInvariant() = ownerLc
                && (resolveRepoName r.Repo).ToLowerInvariant() = repoLc)

        match fromExtra with
        // The injected ref is already canonical (its repo was resolved when parsed) and carries its own
        // owner, so it is returned verbatim — the CAS compares the same value the deployment declared.
        | Some _ as hit -> hit
        | None when ownerLc <> "fs-gg" -> None
        | None ->
            choreLockNumbers
            |> List.tryFind (fun (r, _) -> r.ToLowerInvariant() = repoLc)
            |> Option.map (fun (r, n) ->
                // CANONICAL on the way out — the Ref is built from the TABLE's spelling (`r`), never the
                // caller's casing. Echoing the caller back would mint a Ref structurally UNEQUAL to the
                // canonical one while `Short` renders both alike — two locks that compare different and print
                // the same, the split the CAS cannot survive and no log would show.
                { FS.GG.Coord.Types.Owner = "FS-GG"
                  FS.GG.Coord.Types.Repo = r
                  FS.GG.Coord.Types.Number = n })

    /// `say`'s message is POSITIONAL, and `--to` is OPTIONAL — the shape bash shipped, the shape all seven
    /// prescribing sites document, and the shape the port dropped (#919).
    ///
    /// The port re-typed the verb with a required `--message` and a required `--to`, so the documented form
    /// collected `say: an issue ref takes exactly one argument (got 2)` — a refusal that blames the REF, i.e.
    /// the one part of the line that was right. It fires exactly when a `widen` returns OVERLAP and §3 sends
    /// the worker to `say` the holder, which is the one moment the protocol depends on the channel. A worker
    /// who reads that as "I typed the ref wrong" goes looking down the wrong axis; one who reads it as "the
    /// channel is broken" edits the shared paths anyway, which is the double-edit the protocol exists to
    /// prevent.
    ///
    /// This is the third of its family — #861 (`add` never ported, verb absent), #867 (`release --status`
    /// parsed and ignored, flag unread), this (arguments re-typed) — so the fix is DocumentedInvocationTests,
    /// which parses every prescribed invocation in the corpus. This function is merely instance 3.
    ///
    /// Normalizing HERE, not in `Client.say`, is deliberate: argument shape is the parser's job, and it lets
    /// `Client.say` keep receiving exactly the `[ref]` + `Message` + `ToWorker` it already expects.
    let private normalizeSay (o: Options) : Result<Options, string> =
        if o.Command <> Say then
            Ok o
        else
            // bash joined the trailing positionals with a space, so an unquoted `say #1 hello world` said
            // "hello world" rather than dropping "world" on the floor. Keep that: the alternative refuses a
            // line that reads as obviously correct.
            let withMessage =
                match o.Args, o.Message with
                | _ :: _ :: _, Some _ ->
                    Error
                        "say: the message was given BOTH positionally and with --message — pass it once (say <ref> [--to W] <message>)"
                | ref :: (_ :: _ as rest), None -> Ok { o with Args = [ ref ]; Message = Some(String.concat " " rest) }
                | _ -> Ok o

            // `--to` defaults to `*` — anyone holding the item — as bash's `local to="*"` did. `Client.say`
            // already implements the `*` target, so this restores a documented affordance rather than adding
            // machinery: `say <issue> 'Anyone else in here?'` is prescribed by parallel-work.md and had no
            // form in the port.
            withMessage
            |> Result.map (fun o ->
                match o.ToWorker with
                | None -> { o with ToWorker = Some "*" }
                | Some _ -> o)

    let parse (args: string list) : Result<Options, string> =
        /// Runs at the parse's ONE funnel — `flags`'s terminal case, which every verb reaches — so a new
        /// command cannot quietly re-open the swallow. (`--help`/`--version` short-circuit before `flags`
        /// and carry no flags to check.)
        let validate (o: Options) : Result<Options, string> =
            let residue =
                flagsGiven o
                |> List.tryPick (fun (f, spelling) ->
                    match scopeOf f with
                    | Global -> None
                    | Only readers when List.contains o.Command readers -> None
                    | Only readers -> Some(f, spelling, readers))

            // A `reconcile --apply --json` refusal used to stand HERE, ahead of this arm, and .github#1541
            // removed it. The reasoning lives on `renderSupport`'s `Reconcile` row, next to the `Both` that
            // makes the pair legal, rather than at a funnel it no longer has an arm in.
            //
            // ITS REMOVAL IS WHY THIS MESSAGE NAMES THE COMMAND (.github#1541). `--all-repos` is
            // `Only [ Who ]`, so this arm is the ONE combination check that can be reached by a verb it
            // does not describe — and it runs BEFORE the residue rule, so it shadows the "not a flag of"
            // sentence that would otherwise name the real culprit. That was invisible while the deleted
            // arm answered `reconcile --apply --json --all-repos --repo X` first; with it gone, that line
            // reached a diagnostic prefixed `who:` about a `reconcile` command. Hardcoding one verb's name
            // into a refusal every verb can trigger is the #611 defect this file argues against elsewhere,
            // so the name is DERIVED — `who` still says `who`, and nothing else claims to be `who`.
            if o.AllRepos && o.Repo.IsSome then
                Error
                    $"%s{commandName o.Command}: --repo and --all-repos are mutually exclusive — choose the repository slice or the whole board."
            else
                match residue with
                // `--json`/`--text` get their own sentence, and it is not stylistic (#1523). Naming the
                // readers is the right answer for `--status` (two commands) and useless for these two
                // (twenty), and the caller's real question is not "who else takes this?" but "why can I not
                // have it here?" — which has a one-line answer that the list would bury.
                | Some(FJson, _, _) ->
                    Error
                        $"--json is not a flag of `%s{commandName o.Command}` — it has no machine projection: its stdout is human text whatever you ask for. It would have been ACCEPTED and IGNORED before #1523; this refusal is the flag telling you the truth. Run `command-contract` for the commands that do emit JSON."
                | Some(FText, _, _) ->
                    Error
                        $"--text is not a flag of `%s{commandName o.Command}` — it has no human projection: its stdout is a machine document whatever you ask for, so asking for text would change nothing (#1523). Drop the flag, or project the document yourself."
                | Some(_, spelling, readers) ->
                    // NAME THE READERS, not just the refusal. The caller reached for this flag because they
                    // wanted something; the useful answer is where that something lives, which is why #867's
                    // original message named `ready` and `release` rather than only saying no.
                    let who = readers |> List.map (fun c -> $"`%s{commandName c}`") |> String.concat ", "

                    Error
                        $"%s{spelling} is not a flag of `%s{commandName o.Command}` — only %s{who} read it. It would have been ACCEPTED and IGNORED before #991; this refusal is the flag telling you the truth, not a new restriction."
                | None -> normalizeSay o

        let rec flags acc rest =
            match rest with
            | [] -> validate { acc with Args = List.rev acc.Args }

            | "--snapshot" :: value :: _ when value.StartsWith "-" ->
                Error $"--snapshot needs a value (got flag '%s{value}')"
            | "--snapshot" :: value :: t -> flags { acc with SnapshotFile = Some value } t
            | [ "--snapshot" ] -> Error "--snapshot needs a value"

            | "--events" :: t -> flags { acc with Events = true } t

            | "--cursor" :: value :: _ when value.StartsWith "-" -> Error $"--cursor needs a value (got flag '%s{value}')"
            | "--cursor" :: value :: t -> flags { acc with CursorFile = Some value } t
            | [ "--cursor" ] -> Error "--cursor needs a value"

            | "--repo" :: value :: _ when value.StartsWith "-" -> Error $"--repo needs a value (got flag '%s{value}')"
            // RESOLVED HERE (#962) — see `resolveRepo`. Every verb that takes `--repo` reaches this arm, and
            // there is no list to be left out of, which is the whole repair: the three instances of this bug
            // were each a verb that never got resolution, not a resolution that was wrong.
            | "--repo" :: value :: t ->
                flags
                    { acc with
                        // `Options.Repo` stays a plain string — it is `--repo`'s FILTER value, always a
                        // repository token, never the `cross-repo` sentinel a `--repo` argument could
                        // not meaningfully name (#2398).
                        Repo = Some(resolveRepoName value) }
                    t
            | [ "--repo" ] -> Error "--repo needs a value"

            | "--worker" :: value :: _ when value.StartsWith "-" -> Error $"--worker needs a value (got flag '%s{value}')"
            | "--worker" :: value :: t -> flags { acc with Worker = Some value } t
            | [ "--worker" ] -> Error "--worker needs a value"

            | "--evidence" :: value :: t -> flags { acc with Evidence = Some value } t
            | [ "--evidence" ] -> Error "--evidence needs a value"
            | "--partial" :: value :: t -> flags { acc with Partial = Some value } t
            | [ "--partial" ] -> Error "--partial needs a value — say why this child does NOT complete its parent (#614)"

            | "--to" :: value :: _ when value.StartsWith "-" -> Error $"--to needs a value (got flag '%s{value}')"
            | "--to" :: value :: t -> flags { acc with ToWorker = Some value } t
            | [ "--to" ] -> Error "--to needs a value"

            | "--message" :: value :: t -> flags { acc with Message = Some value } t
            | [ "--message" ] -> Error "--message needs a value"

            // `--paths` is the one CONSUME-THE-REST flag, and it used to mean that literally: it took every
            // remaining argument as a touch-set token and recursed on `[]`, so nothing after it could ever be
            // read as a flag. The comment justifying that said "a `Paths:` token can begin with anything".
            // It cannot (#1507) — `TouchSet.isFlagShaped` is the grammar saying so — and the price of the
            // claim was `widen <ref> --paths <tokens> --json` writing `--json` into the touch-set of a live
            // claim, at exit 0, under a receipt whose `DISJOINT` line read like success.
            //
            // Note WHICH flag it was: `--json` is in `widen`'s own advertised surface (`command-contract`
            // lists it). This was never an unknown flag being tolerated — it was a DECLARED flag of the
            // command being eaten as a value of the preceding one. Every other multi-token flag in this file
            // already guards the same way (`--snapshot`, `--repo`, `--sha`, `-n` … all refuse a value that
            // `StartsWith "-"`); `--paths` was the single arm that opted out, and it opted out of the check
            // for the argument that lands in the one declaration the scheduler reserves files by.
            //
            // So: take tokens up to the first FLAG-SHAPED one, then keep parsing from there. Two properties
            // matter and neither is "recognise `--json`":
            //
            //  * The stop rule asks the GRAMMAR, not a list of flags. A rule keyed on "is it a known flag of
            //    this command?" would need a copy of the flag table here, and a hand-maintained duplicate of
            //    a flag list is the same defect one level along — the next flag added is swallowed exactly
            //    like `--json`, silently, in the same place. `isFlagShaped` cannot fall out of date because
            //    there is no list in it.
            //  * A flag-shaped token that is NOT a flag of this command is now REFUSED rather than declared.
            //    It falls through to the arms below and reaches either its own handler (where the #991
            //    residue rule names it: "--status is not a flag of `widen`") or `unknown argument`. Both are
            //    loud; being written into a `Paths:` line is not.
            //
            // Fully qualified deliberately: `open FS.GG.Coord` here would bring the core's `Landable` module
            // into scope beside this module's `Landable` COMMAND case, and the name a `--paths` guard
            // resolves to is not a thing to leave to resolution order. It also keeps the call site honest —
            // the rule is the core grammar's, and it reads that way.
            | "--paths" :: t ->
                let isPathToken (tok: string) = not (FS.GG.Coord.TouchSet.isFlagShaped tok)
                let tokens = t |> List.takeWhile isPathToken
                let rest = t |> List.skipWhile isPathToken

                // The EMPTY case is the one that must not be "silently satisfied by the next argument".
                // `--paths --json` has no tokens at all, and the failure to say so is what #1507's third
                // acceptance criterion is about: absent and empty are misconfigurations, not defaults.
                // Name the offending token, so the caller sees the flag it typed rather than a bare refusal.
                if List.isEmpty tokens then
                    match rest with
                    | flag :: _ -> Error $"--paths needs at least one token (got flag '%s{flag}')"
                    | [] -> Error "--paths needs at least one token"
                else
                    flags { acc with Paths = tokens } rest

            | "--pr" :: value :: _ when value.StartsWith "-" -> Error $"--pr needs a value (got flag '%s{value}')"
            | "--pr" :: value :: t ->
                match System.Int32.TryParse value with
                | true, n when n > 0 -> flags { acc with Pr = Some n } t
                | _ -> Error $"--pr needs a positive PR number (got '%s{value}')"
            | [ "--pr" ] -> Error "--pr needs a value"

            | "--warn" :: t -> flags { acc with Warn = true } t

            | "--issue" :: value :: _ when value.StartsWith "-" -> Error $"--issue needs a value (got flag '%s{value}')"
            | "--issue" :: value :: t -> flags { acc with Issue = Some value } t
            | [ "--issue" ] -> Error "--issue needs a value"

            | "--status" :: value :: _ when value.StartsWith "-" ->
                Error $"--status needs a value (got flag '%s{value}')"
            | "--status" :: value :: t -> flags { acc with Status = Some value } t
            | [ "--status" ] -> Error "--status needs a value"

            | "--blocked-by" :: value :: _ when value.StartsWith "-" ->
                Error $"--blocked-by needs a value (got flag '%s{value}')"
            | "--blocked-by" :: value :: t -> flags { acc with BlockedBy = Some value } t
            | [ "--blocked-by" ] -> Error "--blocked-by needs a value"

            // `issues --label` / `--state` — a label may legitimately begin with a hyphen, but a bare
            // trailing `--label` with nothing after it is still an error, so the empty-tail guard stays.
            | "--label" :: value :: t -> flags { acc with Label = Some value } t
            | [ "--label" ] -> Error "--label needs a value"

            | "--state" :: value :: _ when value.StartsWith "-" -> Error $"--state needs a value (got flag '%s{value}')"
            | "--state" :: value :: t ->
                match value with
                | "open"
                | "closed"
                | "all" -> flags { acc with IssueState = Some value } t
                | other -> Error $"--state must be open, closed, or all (got '%s{other}')"
            | [ "--state" ] -> Error "--state needs a value"

            // `room open --over N,M` — a comma-separated list of item refs the room is opened over. Split on
            // the comma and trim, so `--over 12, 13` and `--over 12,13` are one form; empties are dropped, so
            // a trailing comma is not a phantom ref. A single VALUE (not consume-the-rest like `--paths`), so
            // a stray flag after it is still caught by `unknown argument`.
            | "--over" :: value :: _ when value.StartsWith "-" -> Error $"--over needs a value (got flag '%s{value}')"
            | "--over" :: value :: t ->
                let refs =
                    value.Split(',')
                    |> Array.map (fun s -> s.Trim())
                    |> Array.filter (fun s -> s <> "")
                    |> List.ofArray

                if List.isEmpty refs then
                    Error "--over needs at least one item ref (e.g. --over 12,13)"
                else
                    flags { acc with Over = refs } t
            | [ "--over" ] -> Error "--over needs a value"

            | "--all" :: t -> flags { acc with All = true } t
            | "--batch" :: t -> flags { acc with Batch = true } t
            | "--explain" :: t -> flags { acc with Explain = true } t
            | "--strict" :: t -> flags { acc with Strict = true } t
            | "--active" :: t -> flags { acc with Active = true } t
            | "--apply" :: t -> flags { acc with Apply = true } t
            | "--peek" :: t -> flags { acc with Peek = true } t
            | "--local" :: t -> flags { acc with Local = true } t
            | "--all-repos" :: t -> flags { acc with AllRepos = true } t
            | "--dry-run" :: t -> flags { acc with DryRun = true } t

            | "--wait" :: t -> flags { acc with Wait = true } t

            | "--tries" :: value :: _ when value.StartsWith "-" ->
                Error $"--tries needs a value (got flag '%s{value}')"
            | "--tries" :: value :: t ->
                match System.Int32.TryParse value with
                | true, n when n > 0 -> flags { acc with Tries = Some n } t
                | true, n -> Error $"--tries must be a positive count (got %d{n})"
                | _ -> Error $"--tries needs a number (got '%s{value}')"
            | [ "--tries" ] -> Error "--tries needs a value"

            // `--interval` permits 0 (the test harness drives the poll with no wall-clock); it is a delay,
            // not a count, so zero is meaningful where `-n 0` would not be.
            | "--interval" :: value :: _ when value.StartsWith "-" ->
                Error $"--interval needs a value (got flag '%s{value}')"
            | "--interval" :: value :: t ->
                match System.Int32.TryParse value with
                | true, n when n >= 0 -> flags { acc with Interval = Some n } t
                | true, n -> Error $"--interval must be a non-negative number of seconds (got %d{n})"
                | _ -> Error $"--interval needs a number of seconds (got '%s{value}')"
            | [ "--interval" ] -> Error "--interval needs a value"

            // REPEATABLE: each `--require` APPENDS. A check-set is a set, and a caller naming two checks
            // means both, not the last one — a last-wins parse would silently drop a required check, which
            // is the fail-open direction this whole command exists to close (#737).
            | "--require" :: value :: _ when value.StartsWith "-" ->
                Error $"--require needs a check name (got flag '%s{value}')"
            | "--require" :: value :: t when System.String.IsNullOrWhiteSpace value ->
                Error "--require needs a check name (got an empty one)"
            | "--require" :: value :: t -> flags { acc with Require = acc.Require @ [ value ] } t
            | [ "--require" ] -> Error "--require needs a check name"

            | "--sha" :: value :: _ when value.StartsWith "-" -> Error $"--sha needs a value (got flag '%s{value}')"
            | "--sha" :: value :: t when System.String.IsNullOrWhiteSpace value ->
                Error "--sha needs a commit SHA (got an empty one)"
            | "--sha" :: value :: t -> flags { acc with Sha = Some value } t
            | [ "--sha" ] -> Error "--sha needs a value"

            | "--fresh" :: t -> flags { acc with Fresh = true } t
            // `bootstrap --refresh` — drop the day-cached board map and re-resolve. An alias of `--fresh`
            // (both mean "ignore the cache, re-read"); the remediation text elsewhere names `--refresh`.
            | "--refresh" :: t -> flags { acc with Fresh = true } t
            | "--include-backlog" :: t -> flags { acc with AllowBacklog = true } t
            | "--force" :: t -> flags { acc with Force = true } t
            | "--refuse-overlap" :: t -> flags { acc with RefuseOverlap = true } t
            | "--mint" :: t -> flags { acc with Mint = true } t
            | "--flip" :: t -> flags { acc with Flip = true } t

            | "-n" :: value :: _ when value.StartsWith "-" -> Error $"-n needs a value (got flag '%s{value}')"
            | "-n" :: value :: t ->
                match System.Int32.TryParse value with
                | true, n when n > 0 -> flags { acc with Limit = Some n } t
                | true, n -> Error $"-n must be a positive count (got %d{n})"
                | _ -> Error $"-n needs a number (got '%s{value}')"
            | [ "-n" ] -> Error "-n needs a value"

            | "--lease" :: value :: _ when value.StartsWith "-" -> Error $"--lease needs a value (got flag '%s{value}')"
            | "--lease" :: value :: t ->
                match System.Int32.TryParse value with
                | true, n when n > 0 -> flags { acc with LeaseMinutes = n; LeaseGiven = true } t
                | true, n -> Error $"--lease must be a positive number of minutes (got %d{n})"
                | _ -> Error $"--lease needs a number of minutes (got '%s{value}')"
            | [ "--lease" ] -> Error "--lease needs a value"

            // Both the EFFECT (`Render`) and the ACT (`RenderGiven`) are recorded. The second is what the
            // residue rule reads: without it, a `--json` on a command with no JSON projection is
            // indistinguishable from that command's default and cannot be refused (#1523).
            | "--json" :: t ->
                flags
                    { acc with
                        Render = Json
                        RenderGiven = acc.RenderGiven.Add Json }
                    t
            | "--text" :: t ->
                flags
                    { acc with
                        Render = Text
                        RenderGiven = acc.RenderGiven.Add Text }
                    t

            | "-" :: t when acc.Command = DiffAudit -> flags { acc with Args = "-" :: acc.Args } t
            | other :: _ when other.StartsWith "-" -> Error $"unknown argument: %s{other}"
            | other :: t -> flags { acc with Args = other :: acc.Args } t

        let defaults =
            { Command = Decide
              // Overwritten by `start` for every verb — see below. Kept as `Json` only so the record is
              // constructible; NOTHING should read this field's value here.
              Render = Json
              RenderGiven = Set.empty
              SnapshotFile = None
              Repo = None
              Fresh = false
              AllowBacklog = false
              Limit = None
              Events = false
              CursorFile = None
              LeaseMinutes = DefaultLeaseMinutes
              LeaseGiven = false
              Args = []
              Worker = None
              Force = false
              RefuseOverlap = false
              Mint = false
              Flip = false
              Evidence = None
              Partial = None
              ToWorker = None
              Message = None
              Paths = []
              Pr = None
              Warn = false
              Issue = None
              Status = None
              BlockedBy = None
              All = false
              Batch = false
              Explain = false
              Strict = false
              Active = false
              Apply = false
              Peek = false
              DryRun = false
              Wait = false
              Tries = None
              Interval = None
              Require = []
              Sha = None
              Label = None
              IssueState = None
              Local = false
              AllRepos = false
              Over = [] }

        /// THE DEFAULT COMES FROM THE DECLARATION, NOT FROM THE ARM (#1523).
        ///
        /// Every verb starts here, and its bare-form render mode is `defaultRender` of the command — the
        /// same `renderSupport` row that decides whether `--json` may be given at all. That is the whole
        /// structural repair on this side of the file.
        ///
        /// It used to be per-arm, and the arms disagreed with themselves: seventeen left `Render` at the
        /// module default of `Json` while what they printed was prose (fifteen verbs, plus `--help` and
        /// `--version`, which are reached by flag). The DECLARED default and the PRINTED one were
        /// opposites on two-fifths of the surface, in the exact configuration #1517 described. `widen` is
        /// the proof that this matters and not the exception: honouring `opts.Render` in the renderer
        /// while the arm still said `Json` would have flipped the bare `widen` — the form every recipe,
        /// skill and driver in the corpus runs — from its human receipt to a JSON object. With one
        /// derivation there is no arm left to forget, and all seventeen traps are disarmed at once.
        ///
        /// IT TAKES THE PARTIALLY-BUILT RECORD, not the `Command`, so each arm still spells
        /// `Command = Landable` at its own call site. That is not style. `recipe-landable.yml` and
        /// `recipe-followup.yml` gate this file with `grep -qE 'Command[[:space:]]*=[[:space:]]*Landable'`
        /// — a source-level assertion that the verb every recipe names is still ROUTED — and a `start
        /// Landable` form passes the token through a helper where that grep cannot see it. The gate is
        /// right to look: a recipe naming a verb the parser dropped is a lie told to every worker. Reading
        /// the default off `o.Command` keeps the derivation total either way — an arm cannot name one
        /// command and get another's default.
        let start (o: Options) =
            { o with Render = defaultRender o.Command }

        // `--help`, `--version` and a bare invocation answer BEFORE the environment is consulted, and
        // that exemption is load-bearing rather than tidiness.
        //
        // A malformed `FSGG_CLAIM_LEASE_MIN` is a MISCONFIGURED SHELL, and these three are the verbs an
        // operator reaches for to diagnose one. Gating them would make `--help` unreachable to exactly
        // the person told to go read it — and `usage` does not name this variable, so the refusal would
        // point at a page that cannot explain it.
        //
        // `--version` is worse, and it is not hypothetical: `scripts/fsgg-coord` probes
        // `dotnet tool run fsgg-coord-engine -- --version` to decide whether the tool is restored. A
        // typo'd export in one shell profile would fail that probe on EVERY invocation, forcing a full
        // `dotnet tool restore` each time and — when that restore failed for any unrelated reason —
        // handing the worker a manifest/feed misdiagnosis for a problem that is a typo. A lease nobody
        // reads must not be able to break the command that reports which binary you are running.
        match args with
        | []
        | "--help" :: _
        | "-h" :: _ -> Ok(start { defaults with Command = Help })

        | "--version" :: _ -> Ok(start { defaults with Command = Version })

        | _ ->

        // PRECEDENCE: `--lease` beats `FSGG_CLAIM_LEASE_MIN` beats `DefaultLeaseMinutes`, and it falls
        // out of WHERE this sits rather than from a rule anybody has to remember — the env only re-seeds
        // `defaults`, and `flags`' `--lease` arm overwrites `LeaseMinutes` afterwards. There is no
        // givenness record for this field (see the residue rule above: `LeaseMinutes` is the field with a
        // non-optional default and no record of having been given), so seeding the default is the ONLY
        // place an env fallback can go without inventing one.
        //
        // The refusal is returned from `parse`, so it reaches the operator through the channel every
        // other bad argument already uses, and no command that could ACT on a lease runs on one nobody
        // could read.
        match leaseMinutesFromEnv () with
        | Error e -> Error e
        | Ok envLeaseMinutes ->

        let defaults =
            { defaults with
                LeaseMinutes = envLeaseMinutes }

        match args with
        | "scan" :: rest -> flags (start { defaults with Command = Scan }) rest
        | "decide" :: rest -> flags (start { defaults with Command = Decide }) rest
        | "delivery" :: rest -> flags (start { defaults with Command = DeliveryCmd }) rest
        | "review" :: rest -> flags (start { defaults with Command = ReviewCmd }) rest
        | "driver" :: rest -> flags (start { defaults with Command = DriverCmd }) rest
        | "cycle" :: rest -> flags (start { defaults with Command = CycleCmd }) rest
        | "lanes" :: rest -> flags (start { defaults with Command = LanesView }) rest
        | "facts" :: rest -> flags (start { defaults with Command = Facts }) rest
        | "command-contract" :: rest -> flags (start { defaults with Command = CommandContractCmd }) rest
        | "intake" :: rest -> flags (start { defaults with Command = IntakeCmd }) rest
        | "delivery-route" :: rest -> flags (start { defaults with Command = RouteCmd }) rest

        | "whoami" :: rest -> flags (start { defaults with Command = WhoAmI }) rest
        | "budget" :: rest -> flags (start { defaults with Command = Budget }) rest
        | "next" :: rest -> flags (start { defaults with Command = Next }) rest
        | "batch" :: rest -> flags (start { defaults with Command = BatchCmd }) rest
        | "ready" :: rest -> flags (start { defaults with Command = Ready }) rest
        | "reconcile" :: rest -> flags (start { defaults with Command = Reconcile }) rest
        | "who" :: rest -> flags (start { defaults with Command = Who }) rest
        | "reap" :: rest -> flags (start { defaults with Command = Reap }) rest
        | "claim" :: rest -> flags (start { defaults with Command = Claim }) rest
        | "adopt" :: rest -> flags (start { defaults with Command = Adopt }) rest
        | "landable" :: rest -> flags (start { defaults with Command = Landable }) rest
        | "take" :: rest -> flags (start { defaults with Command = Take }) rest
        | "release" :: rest -> flags (start { defaults with Command = Release }) rest
        | "heartbeat" :: rest -> flags (start { defaults with Command = Heartbeat }) rest
        | "set-field" :: rest -> flags (start { defaults with Command = SetField }) rest
        | "child" :: rest -> flags (start { defaults with Command = Child }) rest
        | "widen" :: rest -> flags (start { defaults with Command = Widen }) rest
        | "set-paths" :: rest -> flags (start { defaults with Command = SetPaths }) rest
        | "overlap" :: rest -> flags (start { defaults with Command = Overlap }) rest
        | "say" :: rest -> flags (start { defaults with Command = Say }) rest
        | "inbox" :: rest -> flags (start { defaults with Command = Inbox }) rest
        | "done" :: rest -> flags (start { defaults with Command = DoneCmd }) rest
        | "verify-paths" :: rest -> flags (start { defaults with Command = VerifyPaths }) rest
        | "bootstrap" :: rest -> flags (start { defaults with Command = Bootstrap }) rest
        | "board" :: rest -> flags (start { defaults with Command = BoardCmd }) rest
        | "field-id" :: rest -> flags (start { defaults with Command = FieldId }) rest
        | "option-id" :: rest -> flags (start { defaults with Command = OptionId }) rest
        | "item-id" :: rest -> flags (start { defaults with Command = ItemId }) rest
        | "add" :: rest -> flags (start { defaults with Command = Add }) rest
        | "flush" :: rest -> flags (start { defaults with Command = Flush }) rest
        | "lint" :: rest -> flags (start { defaults with Command = LintCmd }) rest
        | "issues" :: rest -> flags (start { defaults with Command = Issues }) rest
        | "followup" :: rest -> flags (start { defaults with Command = Followup }) rest
        | "predicate" :: rest -> flags (start { defaults with Command = Predicate }) rest
        | "diff-audit" :: rest -> flags (start { defaults with Command = DiffAudit }) rest

        // `room open` — the ONLY two-word verb (ADR-0051). A `room` namespace, so `room close`/`room list`
        // have a home if they ever land; today `open` is the one subcommand, and anything else under `room`
        // is named and refused rather than swallowed.
        | "room" :: "open" :: rest -> flags (start { defaults with Command = RoomOpen }) rest
        | "room" :: sub :: _ -> Error $"unknown room subcommand: '%s{sub}' (expected: open)"
        | [ "room" ] -> Error "room needs a subcommand (open)"

        | other :: _ -> Error $"unknown command: %s{other}"

        // UNREACHABLE: a bare invocation is answered above, before the environment is consulted. It is
        // spelled anyway because F# checks each `match` independently, and it is routed to the SAME
        // answer as the exemption arm rather than to an error — so if that arm's list is ever edited,
        // the two cannot come to disagree about what a bare `fsgg-coord-engine` means.
        | [] -> Ok(start { defaults with Command = Help })
