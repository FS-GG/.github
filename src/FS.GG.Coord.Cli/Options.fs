namespace FS.GG.Coord.Cli

module Options =

    type Command =
        | Decide
        | Scan
        | FleetVerdict
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
        | LintCmd
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

          /// `landable --wait` — poll until the verdict SETTLES instead of reading it once (#724). A `green`
          /// is believed only once the subject count has stopped growing; a `red` at zero subjects is the
          /// registration race and keeps waiting; conflicted/unknown return at once.
          Wait: bool
          /// `--tries N` (`landable --wait`) — the maximum number of polls. Default 30.
          Tries: int option
          /// `--interval N` (`landable --wait`) — seconds to sleep between polls. Default 20.
          Interval: int option }

    [<Literal>]
    let DefaultLeaseMinutes = 120

    let usage =
        """fsgg-coord-engine — the typed coordination engine (ADR-0034), and the client it becomes (ADR-0040).

`scripts/fsgg-coord` is the client you run today; this is the engine it shells out to, and — at the
Phase D shim — the engine it BECOMES. The DECISION commands read state on stdin and touch nothing. The
CLIENT commands read and write GitHub through the typed IO layer.

DECISION (pure — no board, no network):
  decide [--snapshot FILE] [--json|--text]   decide a batch from a board-state snapshot on stdin
  fleet  [--snapshot FILE] [--json|--text]   fold the fleet divergence ledger into the cut-over verdict
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
  who    [--repo NAME] [--json]              who holds what, right now (held/stale/unclaimed;
                                             --json for the machine contract, else a human table)
  reap   [--repo NAME] [--apply]             collect expired claims whose work is dead — REFUSING any with
                                             an open item/<n>-* PR (#581); a DRY RUN without --apply
  landable <pr> --repo NAME                  is this OPEN PR finished work? one verdict word on stdout
    [--wait [--tries N] [--interval S]]      (green/conflicted/pending/red/unknown), the decision in the
                                             exit code — the #697/#720 gate as a query (#724). --wait polls
                                             until the verdict SETTLES: it never believes an early green (it
                                             waits for the run set to STOP GROWING), and keeps waiting while
                                             zero runs have registered (default --tries 30, --interval 20s)

  claim  <ref> [--worker W] [--force]        take the item's lock (comment-order CAS)
  take   [--repo NAME] [--worker W]          schedule AND claim the next item, in one step
  release <ref> [--worker W] [--force]       drop the lock, restoring the column it overwrote
  heartbeat <ref> [--worker W]               renew the lease

  set-field <ref> <field> <value>            write one board field (empty value clears)
  set-field --batch <ref> Field=Value ...    write N fields in ONE aliased mutation (#448)
  child  <parent-ref> <child-ref>            attach a child issue to a parent
  widen  <ref> --paths T...                  widen a HELD item's touch-set
  overlap <ref> --active | <a> <b>           does an item's touch-set collide? (repo-scoped, #353)
  say    <ref> --to W --message M            message another worker
  inbox  [--repo NAME] [--peek] [--json]     messages addressed to this worker across every in-flight
                                             claim (ON the board and off it, #461/case 25); --peek does
                                             not advance the cursor
  done   <ref> [--flip] [--evidence E]       stamp the item done; --flip rolls the parent up
  verify-paths --pr N [--repo NAME]          did the PR stay inside its issue's touch-set? (OK/DRIFT/
               [--issue REF] [--warn]        SKIP; --issue names the issue explicitly; --warn advisory)

  whoami [--mint]                            this worker's id and how it was derived
  budget                                     the GraphQL/REST budget

  bootstrap [--refresh]                      resolve the board + field/option ids (2 GraphQL, then
                                             day-cached; --refresh drops the cache and re-resolves)
  board                                      the cached board map as JSON (0 GraphQL when warm)
  field-id <field>                           the resolved id of a board field (from cache)
  option-id <field> <option>                 the resolved id of a single-select option (from cache)
  item-id <ref>                              the board item id for an issue (1 GraphQL, then cached)

  lint   [--repo NAME] [--json] [--strict]   board-health gate: a Ready/Backlog item that no worker can
                                             ever pick up (no `Paths:`, or every token unmatchable) is an
                                             error (#496); --strict makes notes fatal too

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

    let parse (args: string list) : Result<Options, string> =
        let rec flags acc rest =
            match rest with
            | [] -> Ok { acc with Args = List.rev acc.Args }

            | "--snapshot" :: value :: _ when value.StartsWith "-" ->
                Error $"--snapshot needs a value (got flag '%s{value}')"
            | "--snapshot" :: value :: t -> flags { acc with SnapshotFile = Some value } t
            | [ "--snapshot" ] -> Error "--snapshot needs a value"

            | "--repo" :: value :: _ when value.StartsWith "-" -> Error $"--repo needs a value (got flag '%s{value}')"
            | "--repo" :: value :: t -> flags { acc with Repo = Some value } t
            | [ "--repo" ] -> Error "--repo needs a value"

            | "--worker" :: value :: _ when value.StartsWith "-" -> Error $"--worker needs a value (got flag '%s{value}')"
            | "--worker" :: value :: t -> flags { acc with Worker = Some value } t
            | [ "--worker" ] -> Error "--worker needs a value"

            | "--evidence" :: value :: t -> flags { acc with Evidence = Some value } t
            | [ "--evidence" ] -> Error "--evidence needs a value"

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

            | "--all" :: t -> flags { acc with All = true } t
            | "--batch" :: t -> flags { acc with Batch = true } t
            | "--strict" :: t -> flags { acc with Strict = true } t
            | "--active" :: t -> flags { acc with Active = true } t
            | "--apply" :: t -> flags { acc with Apply = true } t
            | "--peek" :: t -> flags { acc with Peek = true } t

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
              Wait = false
              Tries = None
              Interval = None }

        match args with
        | []
        | "--help" :: _
        | "-h" :: _ -> Ok { defaults with Command = Help }

        | "--version" :: _ -> Ok { defaults with Command = Version }

        | "scan" :: rest -> flags { defaults with Command = Scan } rest
        | "decide" :: rest -> flags { defaults with Command = Decide } rest
        | "fleet" :: rest -> flags { defaults with Command = FleetVerdict } rest
        | "lanes" :: rest -> flags { defaults with Command = LanesView } rest
        | "facts" :: rest -> flags { defaults with Command = Facts } rest

        | "whoami" :: rest -> flags { defaults with Command = WhoAmI } rest
        | "budget" :: rest -> flags { defaults with Command = Budget } rest
        | "next" :: rest -> flags { defaults with Command = Next } rest
        | "batch" :: rest -> flags { defaults with Command = BatchCmd } rest
        | "ready" :: rest -> flags { defaults with Command = Ready } rest
        // `who` is a HUMAN truth read by default (the table case 20 asserts), and `--json` opts into the
        // machine contract cases 20/25 consume — the mirror of `ready`/`batch`, where JSON is the default.
        | "who" :: rest -> flags { defaults with Command = Who; Render = Text } rest
        // `reap` reports as text (the operator reads it before deciding); its collect is gated behind
        // `--apply`, so the bare form is a DRY RUN.
        | "reap" :: rest -> flags { defaults with Command = Reap; Render = Text } rest
        | "claim" :: rest -> flags { defaults with Command = Claim } rest
        // `adopt` reports as text (a precondition report the operator reads); it gates the `claim` transfer.
        | "adopt" :: rest -> flags { defaults with Command = Adopt; Render = Text } rest
        // `landable` prints ONE verdict word on stdout and puts the decision in the exit code — a query, not
        // a table, so no `Render` flip.
        | "landable" :: rest -> flags { defaults with Command = Landable } rest
        | "take" :: rest -> flags { defaults with Command = Take } rest
        | "release" :: rest -> flags { defaults with Command = Release } rest
        | "heartbeat" :: rest -> flags { defaults with Command = Heartbeat } rest
        | "set-field" :: rest -> flags { defaults with Command = SetField } rest
        | "child" :: rest -> flags { defaults with Command = Child } rest
        | "widen" :: rest -> flags { defaults with Command = Widen } rest
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
        | "lint" :: rest -> flags { defaults with Command = LintCmd; Render = Text } rest

        | other :: _ -> Error $"unknown command: %s{other}"
