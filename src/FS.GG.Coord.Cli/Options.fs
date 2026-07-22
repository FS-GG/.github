namespace FS.GG.Coord.Cli

module Options =

    type Command =
        | Decide
        | Scan
        | LanesView
        | Facts
        | WhoAmI
        | Budget
        | Next
        | BatchCmd
        | Ready
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
        | RoomOpen
        | Help
        | Version

    type Render =
        | Json
        | Text

    type Options =
        { Command: Command
          Render: Render
          SnapshotFile: string option
          Repo: string option
          Fresh: bool
          AllowBacklog: bool
          Limit: int option
          LeaseMinutes: int
          Args: string list
          Worker: string option
          Force: bool
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
          /// `ready --all` — widen past the "not Done" default without naming a column (#520: `ready` is a
          /// TRUTH read, so `--all` shows the whole board, Done and closed items and all).
          All: bool
          /// `set-field --batch` — the remaining `Field=Value` args are written in ONE aliased mutation
          /// document (#448): N fields, one GraphQL request, one point at the floor.
          Batch: bool
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

    [<Literal>]
    let DefaultLeaseMinutes = 120

    let usage =
        """fsgg-coord-engine — the typed coordination engine (ADR-0034), and the client it becomes (ADR-0040).

`scripts/fsgg-coord` is the client you run today; this is the engine it shells out to, and — at the
Phase D shim — the engine it BECOMES. The DECISION commands read state on stdin and touch nothing. The
CLIENT commands read and write GitHub through the typed IO layer.

DECISION (pure — no board, no network):
  decide [--snapshot FILE] [--json|--text]   decide a batch from a board-state snapshot on stdin
  lanes  [--snapshot FILE] [--json|--text]   partition a snapshot's items into non-contending lanes
  facts  [--json|--text]                     emit the protocol the engine enforces (projections read this)

IO (read and write the board — $FSGG_COORD_OWNER / $FSGG_COORD_PROJECT, $GITHUB_TOKEN, $FSGG_GITHUB_API_BASE):
  scan   [--repo NAME] [--fresh] [-n N] [--include-backlog] [--lease MIN]
                                             read the board and emit the snapshot `decide` consumes
  next   [--repo NAME]                       the next single schedulable item
  batch  [--repo NAME] [-n N] [--include-backlog]
                                             every item schedulable in parallel right now
  ready  [--repo NAME] [--status S] [--all]  the board as a reconciler sees it (always fresh; not-Done
                                             by default — a TRUTH read, so it shows items the scheduler
                                             will refuse; --status/--all widen past the default)
  who    [--repo NAME|--all-repos] [--local] [--json]
                                             who holds what, right now (held/stale/unclaimed;
                                             --local joins claims to local git worktrees;
                                             output always names its effective scope;
                                             --json for the machine contract, else a human table)
  reap   [--repo NAME] [--apply]             collect expired claims whose work is dead — REFUSING any with
                                             an open item/<n>-* PR (#581); a DRY RUN without --apply
  landable <pr> --repo NAME                  is this OPEN PR finished work? one verdict word on stdout
    [--wait [--tries N] [--interval S]]      (green/conflicted/pending/red/unknown), the decision in the
                                             exit code — the #697/#720 gate as a query (#724). --wait polls
                                             until the verdict SETTLES: it never believes an early green (it
                                             waits for the run set to STOP GROWING), and keeps waiting while
                                             zero runs have registered (default --tries 30, --interval 20s).
    [--require NAME]... [--sha SHA]           --require NAME (repeatable): this check must have REPORTED —
                                             for one branch protection does NOT require but that decides the
                                             PR; absent, it reads like a passing one (#606). --sha SHA: the
                                             head you MEAN to gate, for a caller that just force-pushed (the
                                             PR object lags). Neither can green; both are pending (#737)

  claim  <ref> [--worker W] [--force] [--json]
                                             take the lock; --json emits a fresh marker/Status receipt
  take   [--repo NAME] [--worker W] [--json]
                                             schedule AND claim the next item, in one step. Ready only:
         [--include-backlog]                 a Backlog row is passed over AT THE COLUMN unless you ask for
                                             it (#636 — the flag has always worked here; only this line
                                             was missing, so the remedy for a Backlog-starved queue was
                                             undiscoverable from the tool that refused)
  release <ref> [--worker W]                 drop the lock, restoring the column it overwrote;
          [--status S]                       --status lands it in S instead (#867: name the column you
                                             mean, e.g. `--status Blocked`, or it goes back to Ready)
  heartbeat <ref> [--worker W]               renew the lease
  adopt  <ref> [--worker W]                  take over an ORPHAN — a stale claim whose PR is FINISHED —
                                             and land it (#697/#720); reports the preconditions it checked,
                                             then transfers the claim

  add    <ref>                               put an issue ON the board, idempotently (#861) — the metered
                                             verb the GraphQL monopoly rule names (#586); prints the item id
  set-field <ref> <field> <value>            write one board field (empty value clears)
  set-field --batch <ref> Field=Value ...    write N fields in ONE aliased mutation (#448)
  flush  [--dry-run]                         REPLAY the board writes an exhausted budget queued — the verb
                                             every "QUEUED; flush replays it" message names (#862). Replays
                                             by DEFAULT; --dry-run lists the queue and writes nothing
  child  <parent-ref> <child-ref>            attach a child issue to a parent
  widen  <ref> --paths T...                  add paths to a HELD item's touch-set (union; idempotent)
  set-paths <ref> --paths T...               replace a HELD item's touch-set explicitly (also narrows)
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
  followup add <ref> | peek | pop | list     this worker's follow-up queue — the "I can fix this, just not
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
  predicate <id> <field> <value>             the ADR-0050 registry oracle: does the row exist AND does the
  predicate  (cross-repo-request on stdin)   OWNING producer's manifest agree? One word — agrees/contradicts/
                                             unknown — decision in the exit code (0/3/4, the `landable` shape).
                                             Owner is authoritative (`owner:`), an absent value is UNKNOWN
                                             not false (.github#658), and a missing registry/manifest is
                                             UNKNOWN — fail closed. Reads registry/skills.yml ($FSGG_REGISTRY)
                                             and producer checkouts under $FSGG_REPOS_ROOT (default .repos).
                                             Local: no board, no token. Only `mirrored` compared today.

  --help    --version

A <ref> is a URL, owner/repo#n, or repo#n (owner/repo default to $FSGG_COORD_OWNER / --repo).

EXIT CODES — the engine's own (the shim translates them for a caller that still speaks bash):
  0 green   ·   1 error (bad args / malformed input)   ·   2 defect (the engine broke)
  3 red     ·   4 no-verdict   ·   75 EX_RATE (budget exhausted — back off, try again later)
  `take` (#585): 0 ONLY when it claimed an item · 5 EX_NONE (nothing startable) · 6 EX_CONTENDED
  (lost every race) · 75 EX_RATE · any other non-zero, could not read (never EX_NONE, #266)
  `landable` (#720/#724): 0 green · 7 pending (the ONE verdict worth retrying) · 3 red or conflicted
  (do NOT wait) · 4 unknown (could not reach a verdict — fail-closed, never a retry)
"""

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
        | FMint
        | FFlip
        | FLimit
        | FLocal
        | FAllRepos
        | FOver

    type private FlagScope =
        /// Every command honours it. Named deliberately — the flags here are the ones whose readers really
        /// are the whole surface (`--repo`), or whose "was it given?" cannot be observed at all because the
        /// field has a non-optional default (`--json`/`--text` land in `Render`, `--lease` in `LeaseMinutes`):
        /// an unset flag is indistinguishable from its default, so there is nothing here to refuse.
        | Global
        /// Only these commands READ it. Every other command refuses it rather than swallowing it.
        | Only of Command list

    /// Which commands read each flag. Derived by tracing each `opts.<Field>` read to its handler, NOT from
    /// the usage prose — the two disagreed, and where they disagreed the prose was wrong (`release --force`).
    let private scopeOf (f: Flag) : FlagScope =
        match f with
        | FRepo -> Global
        | FWorker -> Global
        | FSnapshot -> Only [ Decide; LanesView ]

        // `--status`: #867's original row, now one of many rather than the only one.
        | FStatus -> Only [ Ready; Release ]

        | FMint -> Only [ WhoAmI ]
        | FLocal -> Only [ Who ]
        | FAllRepos -> Only [ Who ]
        | FAll -> Only [ Ready ]
        | FActive -> Only [ Overlap ]
        | FApply -> Only [ Reap ]
        | FPeek -> Only [ Inbox ]
        | FDryRun -> Only [ Flush ]
        | FStrict -> Only [ LintCmd ]
        | FBatch -> Only [ SetField ]
        | FPaths -> Only [ Widen; SetPaths ]
        | FTo -> Only [ Say ]
        | FMessage -> Only [ Say ]
        | FEvidence -> Only [ DoneCmd ]
        | FFlip -> Only [ DoneCmd ]
        | FPartial -> Only [ DoneCmd ]
        | FPr -> Only [ DoneCmd; VerifyPaths ]
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

        // `--force` bypasses the #516 one-item-per-worker check, and `claim` is the ONLY reader. The usage
        // block advertised `release [--force]` for the whole life of the port and `release` never read it —
        // a documented no-op, found by building this table (#991). The usage line lost it; refusing it here
        // breaks no working behaviour, because there was none to break.
        | FForce -> Only [ Claim ]

        // Scheduling reads take their freshness from a `Cache.ReadIntent`, not from this flag, so
        // `batch --fresh` / `next --fresh` / `take --fresh` never did anything. Only these three read it.
        | FFresh -> Only [ Scan; Bootstrap; Issues ]

        // `next` and `take` OVERRIDE the cap to 1 (they are `batch` capped at one), so `-n` is dead on both.
        | FLimit -> Only [ Scan; BatchCmd ]

        | FIncludeBacklog -> Only [ Scan; Next; BatchCmd; Take ]

    /// The flags actually GIVEN, with the spelling to name in a refusal. A flag whose field has a
    /// non-optional default (`Render`, `LeaseMinutes`) cannot appear: "given" and "defaulted" are the same
    /// state, so it is `Global` above and there is nothing to detect.
    let private flagsGiven (o: Options) : (Flag * string) list =
        [ if o.SnapshotFile.IsSome then FSnapshot, "--snapshot"
          if o.Repo.IsSome then FRepo, "--repo"
          if o.Worker.IsSome then FWorker, "--worker"
          if o.Evidence.IsSome then FEvidence, "--evidence"
          if o.Partial.IsSome then FPartial, "--partial"
          if o.ToWorker.IsSome then FTo, "--to"
          if o.Message.IsSome then FMessage, "--message"
          if not (List.isEmpty o.Paths) then FPaths, "--paths"
          if o.Pr.IsSome then FPr, "--pr"
          if o.Warn then FWarn, "--warn"
          if o.Issue.IsSome then FIssue, "--issue"
          if o.Status.IsSome then FStatus, "--status"
          if o.All then FAll, "--all"
          if o.Batch then FBatch, "--batch"
          if o.Strict then FStrict, "--strict"
          if o.Active then FActive, "--active"
          if o.Apply then FApply, "--apply"
          if o.Peek then FPeek, "--peek"
          if o.Local then FLocal, "--local"
          if o.AllRepos then FAllRepos, "--all-repos"
          if o.DryRun then FDryRun, "--dry-run"
          if o.Wait then FWait, "--wait"
          if o.Tries.IsSome then FTries, "--tries"
          if o.Interval.IsSome then FInterval, "--interval"
          if not (List.isEmpty o.Require) then FRequire, "--require"
          if o.Sha.IsSome then FSha, "--sha"
          if o.Label.IsSome then FLabel, "--label"
          if o.IssueState.IsSome then FState, "--state"
          if o.Fresh then FFresh, "--fresh"
          if o.AllowBacklog then FIncludeBacklog, "--include-backlog"
          if o.Force then FForce, "--force"
          if o.Mint then FMint, "--mint"
          if o.Flip then FFlip, "--flip"
          if not (List.isEmpty o.Over) then FOver, "--over"
          if o.Limit.IsSome then FLimit, "-n" ]

    /// The argv spelling of a command — the word a refusal must name, because it is the word the caller
    /// typed. Total over `Command`, so a new verb cannot be named `%A` by accident.
    let private commandName (c: Command) : string =
        match c with
        | Decide -> "decide"
        | Scan -> "scan"
        | LanesView -> "lanes"
        | Facts -> "facts"
        | WhoAmI -> "whoami"
        | Budget -> "budget"
        | Next -> "next"
        | BatchCmd -> "batch"
        | Ready -> "ready"
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
        | RoomOpen -> "room open"
        | Help -> "--help"
        | Version -> "--version"

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
    let resolveRepo (raw: string) : string =
        match raw.ToLowerInvariant() with
        | "sdd" -> "FS.GG.SDD"
        | "rendering" -> "FS.GG.Rendering"
        | "governance" -> "FS.GG.Governance"
        | "templates" -> "FS.GG.Templates"
        | "game" -> "FS.GG.Game"
        | "audio" -> "FS.GG.Audio"
        | "net" -> "FS.GG.Net"
        | _ ->
            // owner/repo -> the repo part (bash's `${1#*/}`); a literal name -> itself.
            match raw.IndexOf('/') with
            | -1 -> raw
            | i -> raw.Substring(i + 1)

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
        let repoLc = (resolveRepo repo).ToLowerInvariant()

        let fromExtra =
            extra
            |> List.tryFind (fun r ->
                r.Owner.ToLowerInvariant() = ownerLc
                && (resolveRepo r.Repo).ToLowerInvariant() = repoLc)

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
                    | Only readers -> Some(spelling, readers))

            if o.AllRepos && o.Repo.IsSome then
                Error "who: --repo and --all-repos are mutually exclusive — choose the repository slice or the whole board."
            else
                match residue with
                | Some(spelling, readers) ->
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

            | "--repo" :: value :: _ when value.StartsWith "-" -> Error $"--repo needs a value (got flag '%s{value}')"
            // RESOLVED HERE (#962) — see `resolveRepo`. Every verb that takes `--repo` reaches this arm, and
            // there is no list to be left out of, which is the whole repair: the three instances of this bug
            // were each a verb that never got resolution, not a resolution that was wrong.
            | "--repo" :: value :: t ->
                flags
                    { acc with
                        Repo = Some(resolveRepo value) }
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

            // `--paths` consumes the REST of the arguments as touch-set tokens — a `Paths:` token can begin
            // with anything, so nothing after it is treated as a flag.
            | "--paths" :: t ->
                if List.isEmpty t then
                    Error "--paths needs at least one token"
                else
                    flags { acc with Paths = t } []

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
                | true, n when n > 0 -> flags { acc with LeaseMinutes = n } t
                | true, n -> Error $"--lease must be a positive number of minutes (got %d{n})"
                | _ -> Error $"--lease needs a number of minutes (got '%s{value}')"
            | [ "--lease" ] -> Error "--lease needs a value"

            | "--json" :: t -> flags { acc with Render = Json } t
            | "--text" :: t -> flags { acc with Render = Text } t

            | other :: _ when other.StartsWith "-" -> Error $"unknown argument: %s{other}"
            | other :: t -> flags { acc with Args = other :: acc.Args } t

        let defaults =
            { Command = Decide
              Render = Json
              SnapshotFile = None
              Repo = None
              Fresh = false
              AllowBacklog = false
              Limit = None
              LeaseMinutes = DefaultLeaseMinutes
              Args = []
              Worker = None
              Force = false
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
              All = false
              Batch = false
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

        match args with
        | []
        | "--help" :: _
        | "-h" :: _ -> Ok { defaults with Command = Help }

        | "--version" :: _ -> Ok { defaults with Command = Version }

        | "scan" :: rest -> flags { defaults with Command = Scan } rest
        | "decide" :: rest -> flags { defaults with Command = Decide } rest
        | "lanes" :: rest -> flags { defaults with Command = LanesView } rest
        | "facts" :: rest -> flags { defaults with Command = Facts } rest

        | "whoami" :: rest -> flags { defaults with Command = WhoAmI } rest
        // `budget` reports as text — the operator's free pre-flight read — and `--json` opts into the
        // machine contract (`pendingBoardWrites`, #862), the same polarity as `who`/`inbox`. It ALWAYS
        // rendered text; pinning `Render` here is what makes that the DECLARED default rather than a
        // record field the command quietly ignored, so `--json` now means something.
        | "budget" :: rest -> flags { defaults with Command = Budget; Render = Text } rest
        | "next" :: rest -> flags { defaults with Command = Next } rest
        | "batch" :: rest -> flags { defaults with Command = BatchCmd } rest
        | "ready" :: rest -> flags { defaults with Command = Ready } rest
        // `who` is a HUMAN truth read by default (the table case 20 asserts), and `--json` opts into the
        // machine contract cases 20/25 consume — the mirror of `ready`/`batch`, where JSON is the default.
        | "who" :: rest -> flags { defaults with Command = Who; Render = Text } rest
        // `reap` reports as text (the operator reads it before deciding); its collect is gated behind
        // `--apply`, so the bare form is a DRY RUN.
        | "reap" :: rest -> flags { defaults with Command = Reap; Render = Text } rest
        | "claim" :: rest -> flags { defaults with Command = Claim; Render = Text } rest
        // `adopt` reports as text (a precondition report the operator reads); it gates the `claim` transfer.
        | "adopt" :: rest -> flags { defaults with Command = Adopt; Render = Text } rest
        // `landable` prints ONE verdict word on stdout and puts the decision in the exit code — a query, not
        // a table, so no `Render` flip.
        | "landable" :: rest -> flags { defaults with Command = Landable } rest
        | "take" :: rest -> flags { defaults with Command = Take; Render = Text } rest
        | "release" :: rest -> flags { defaults with Command = Release } rest
        | "heartbeat" :: rest -> flags { defaults with Command = Heartbeat } rest
        | "set-field" :: rest -> flags { defaults with Command = SetField } rest
        | "child" :: rest -> flags { defaults with Command = Child } rest
        | "widen" :: rest -> flags { defaults with Command = Widen } rest
        | "set-paths" :: rest -> flags { defaults with Command = SetPaths } rest
        | "overlap" :: rest -> flags { defaults with Command = Overlap; Render = Text } rest
        | "say" :: rest -> flags { defaults with Command = Say } rest
        // `inbox` reports as a human table by default (a worker reads it), `--json` for a machine consumer —
        // the mirror of `who`.
        | "inbox" :: rest -> flags { defaults with Command = Inbox; Render = Text } rest
        | "done" :: rest -> flags { defaults with Command = DoneCmd } rest
        | "verify-paths" :: rest -> flags { defaults with Command = VerifyPaths } rest
        | "bootstrap" :: rest -> flags { defaults with Command = Bootstrap } rest
        | "board" :: rest -> flags { defaults with Command = BoardCmd } rest
        | "field-id" :: rest -> flags { defaults with Command = FieldId } rest
        | "option-id" :: rest -> flags { defaults with Command = OptionId } rest
        | "item-id" :: rest -> flags { defaults with Command = ItemId } rest
        | "add" :: rest -> flags { defaults with Command = Add } rest
        // `flush` REPLAYS by default — see `DryRun`. It reports as text: an operator reads it after the
        // back-off `EX_RATE` told them to take.
        | "flush" :: rest -> flags { defaults with Command = Flush; Render = Text } rest
        | "lint" :: rest -> flags { defaults with Command = LintCmd; Render = Text } rest
        // `issues` emits the raw JSON array (bash's `issues` prints the REST body); the caller projects it
        // with jq, so the default Json render stands.
        | "issues" :: rest -> flags { defaults with Command = Issues } rest
        // `followup` prints the REF on stdout and nothing else — a query whose caller is `widen`/`claim`,
        // not a reader. Text, for `whoami`'s reason: there is no board document here to be the contract.
        | "followup" :: rest -> flags { defaults with Command = Followup; Render = Text } rest
        // `predicate` runs the ADR-0050 registry oracle over LOCAL files (no board, no token) and prints
        // one verdict word — `agrees`/`contradicts`/`unknown` — with the decision in the exit code, the
        // `landable` shape. Text by default; `--json` opts into the structured verdict. Assertion is
        // POSITIONAL (`predicate <id> <field> <value>`) or read from a `cross-repo-request` body on stdin.
        | "predicate" :: rest -> flags { defaults with Command = Predicate; Render = Text } rest

        // `room open` — the ONLY two-word verb (ADR-0051). A `room` namespace, so `room close`/`room list`
        // have a home if they ever land; today `open` is the one subcommand, and anything else under `room`
        // is named and refused rather than swallowed. Text, like `add`: it reports the room it created.
        | "room" :: "open" :: rest -> flags { defaults with Command = RoomOpen; Render = Text } rest
        | "room" :: sub :: _ -> Error $"unknown room subcommand: '%s{sub}' (expected: open)"
        | [ "room" ] -> Error "room needs a subcommand (open)"

        | other :: _ -> Error $"unknown command: %s{other}"
