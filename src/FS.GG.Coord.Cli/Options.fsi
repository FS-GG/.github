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

    /// What the engine was asked to do. The DECISION commands (`Decide`, `Fleet`, `Lanes`, `Facts`) read
    /// state on stdin and touch no network; the CLIENT commands below `Scan` perform IO through the GitHub
    /// adapter — they are the surface the shim (ADR-0034 §4.4) execs in place of the bash client.
    type Command =
        | Decide
        | Scan
        | FleetVerdict
        | LanesView
        | Facts

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
        /// Who holds what, right now — held/stale/unclaimed (`who [--repo] [--json]`).
        | Who
        /// Collect expired claims whose work is dead — refusing any with an open `item/<n>-*` PR
        /// (`reap [--repo] [--apply]`, #581). A DRY RUN without `--apply`.
        | Reap
        /// Take an item's lock (`claim <ref> [--worker W] [--force]`).
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
        /// Board-health gate: flag Ready/Backlog items no worker can pick up (`lint [--repo] [--strict]`, #496).
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

          /// Positional arguments in order — the ref, the field, the value, the child ref. Each command
          /// reads what it needs and refuses the wrong count.
          Args: string list

          /// `--worker <id>` — the lock's identity (ADR-0027; NOT the GitHub account).
          Worker: string option
          /// `--force` — steal a live lock (`claim`/`release`). A broken IDENTITY is never stealable; this
          /// is for a lock a human means to break.
          Force: bool
          /// `--mint` (`whoami`) — print one fresh id line for `eval`.
          Mint: bool
          /// `--flip` (`done`) — roll the parent up when this child completes it.
          Flip: bool
          /// `--evidence <text>` (`done`) — assert the item is finished with NO PR (#600). Required for the
          /// no-PR green path; a green path with no argument would be a way of switching the stamp off.
          Evidence: string option
          /// `--to <worker>` (`say`).
          ToWorker: string option
          /// `--message <text>` (`say`).
          Message: string option
          /// `--paths <token>...` (`widen`) — the new touch-set.
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
          /// `--all` (`ready`) — widen past the "not Done" default without naming a column. `ready` is a
          /// TRUTH read (#520), so `--all` shows the whole board — Done, and closed-but-still-columned rows.
          All: bool

          /// `--batch` (`set-field`) — write the remaining `Field=Value` args in ONE aliased mutation
          /// document (#448): N fields, one GraphQL request, one point at the floor.
          Batch: bool

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

          /// `--wait` (`landable`) — poll until the verdict SETTLES rather than reading it once (#724). The
          /// poll never believes an early `green`: it waits for the run set to STOP GROWING, and it keeps
          /// waiting while zero runs have registered (the registration race). Conflicted/unknown return at
          /// once — no amount of waiting fixes either.
          Wait: bool
          /// `--tries N` (`landable --wait`) — the maximum number of polls (positive). Default 30.
          Tries: int option
          /// `--interval S` (`landable --wait`) — seconds to sleep between polls (0 permitted, for the test
          /// harness). Default 20.
          Interval: int option }

    /// The documented default (`FSGG_CLAIM_LEASE_MIN`).
    [<Literal>]
    val DefaultLeaseMinutes: int = 120

    /// Parse argv. `Error` carries a message already fit to print.
    val parse: args: string list -> Result<Options, string>

    val usage: string
