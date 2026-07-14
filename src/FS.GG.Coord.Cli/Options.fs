namespace FS.GG.Coord.Cli

module Options =

    type Command =
        | Decide
        /// THE ONE COMMAND THAT PERFORMS IO. See Options.fsi.
        | Scan
        | FleetVerdict
        | LanesView
        /// Emit the PROTOCOL — the rules the typed core enforces, as data (ADR-0034 §4.5). The docs and
        /// the SKILL.md bodies are generated FROM this, so a rule cannot land in the engine and not in
        /// the prose that tells a worker about it.
        | Facts
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
          LeaseMinutes: int }

    /// The documented default (`FSGG_CLAIM_LEASE_MIN`).
    [<Literal>]
    let DefaultLeaseMinutes = 120

    let usage =
        """fsgg-coord-engine — the typed coordination engine (ADR-0034).

NOT A USER-FACING TOOL. `scripts/fsgg-coord` is the user-facing client; this is the engine it shells
out to.

EVERY COMMAND BELOW IS PURE — reads NOTHING, no board, no network, no token — WITH ONE EXCEPTION, and
it is named: `scan`. The DECISION commands take state on stdin, which is what makes shadow mode free:
two engines, one board read, one budget (#418). `scan` is the command that goes and GETS that state,
so that the engine no longer needs bash in order to see the board it decides about (ADR-0034 §4.1,
built by ADR-0040).

Usage:
  fsgg-coord-engine scan  [--repo NAME] [--fresh] [--include-backlog] [-n N] [--lease MIN] [--json|--text]
                                    # THE ONLY COMMAND THAT PERFORMS IO. Scan the board, resolve the
                                    # blockers, read each candidate's touch-set and claim markers, and
                                    # emit the snapshot `decide` consumes.
                                    #
                                    #   fsgg-coord-engine scan | fsgg-coord-engine decide
                                    #
                                    # is a complete scheduling pass with no bash anywhere in it.
                                    #
                                    # Board: $FSGG_COORD_OWNER / $FSGG_COORD_PROJECT.
                                    # Token: $GITHUB_TOKEN (or $GH_TOKEN).
                                    # API:   $FSGG_GITHUB_API_BASE (default https://api.github.com).

  fsgg-coord-engine decide [--snapshot FILE] [--json|--text]
                                    # read a board-state snapshot (stdin, or FILE) and print the
                                    # scheduling decision for every candidate in it

  fsgg-coord-engine fleet  [--snapshot FILE] [--json|--text]
                                    # read the fleet divergence LEDGER (stdin, or FILE) and print
                                    # ADR-0034 §5's cut-over verdict: has the shadow agreed with bash
                                    # across the live fleet, on THIS engine build, for N consecutive
                                    # days? (#634)

  fsgg-coord-engine lanes  [--snapshot FILE] [--json|--text]
                                    # partition the board into LANES — connected components of the
                                    # item-overlap graph. Items in different lanes can NEVER collide,
                                    # so they are provably safe to run concurrently. Prints the
                                    # CEILING (how many workers this board can actually absorb) and
                                    # the items that cannot be laned at all.

  fsgg-coord-engine facts  [--json|--text]
                                    # emit the PROTOCOL the engine enforces — the touch-set grammar, the
                                    # check order, the blocker rule, the claim lock, the lease, and the
                                    # verdict a worker can be handed. `scripts/generate-projections`
                                    # renders the docs and the SKILL.md bodies FROM this, and a gate
                                    # regenerates and fails on any diff. A rule stated twice is a rule
                                    # that will disagree with itself (#485).

  fsgg-coord-engine --help
  fsgg-coord-engine --version

EXIT CODES — the engine's own, NOT the client's (the client translates them):
  0   green      a batch was computed / the cut-over criterion is met
  1   error      bad arguments, or a malformed snapshot
  2   defect     the engine itself broke — a bug, and never the caller's fault
  3   red        the batch is REFUSED. A reservation whose touch-set is unmatchable reserves NOTHING,
                 so scheduling against it would hand a second worker files somebody is standing in.
                 Unschedulable beats mis-scheduled.
                 For `fleet`: the engines DISAGREED. The flip is blocked.
  4   no-verdict the engine could not reach an answer. NEVER zero, and never silently a "no" —
                 an unreachable answer is not a negative one (#266).
                 For `fleet`: the evidence is absent, thin, single-worker, or from another build.
                 An empty ledger is zero EVIDENCE, and it is never zero divergence.
"""

    let parse (args: string list) : Result<Options, string> =
        let rec flags acc rest =
            match rest with
            | [] -> Ok acc

            // A flag whose value is another flag is a MISSING value, not a value that happens to
            // start with a dash. Swallowing the next flag here is how an option silently goes unset
            // while the command reports success.
            | "--snapshot" :: value :: _ when value.StartsWith "-" ->
                Error $"--snapshot needs a value (got flag '%s{value}')"
            | "--snapshot" :: value :: t -> flags { acc with SnapshotFile = Some value } t
            | [ "--snapshot" ] -> Error "--snapshot needs a value"

            | "--repo" :: value :: _ when value.StartsWith "-" -> Error $"--repo needs a value (got flag '%s{value}')"
            | "--repo" :: value :: t -> flags { acc with Repo = Some value } t
            | [ "--repo" ] -> Error "--repo needs a value"

            | "--fresh" :: t -> flags { acc with Fresh = true } t
            | "--include-backlog" :: t -> flags { acc with AllowBacklog = true } t

            | "-n" :: value :: _ when value.StartsWith "-" -> Error $"-n needs a value (got flag '%s{value}')"
            | "-n" :: value :: t ->
                match System.Int32.TryParse value with
                | true, n when n > 0 -> flags { acc with Limit = Some n } t
                // A LIMIT OF ZERO IS NOT "no limit" — it is a batch of nothing, and it would report an empty
                // queue over a full board. Refuse it rather than guess which the caller meant.
                | true, n -> Error $"-n must be a positive count (got %d{n})"
                | _ -> Error $"-n needs a number (got '%s{value}')"
            | [ "-n" ] -> Error "-n needs a value"

            | "--lease" :: value :: _ when value.StartsWith "-" -> Error $"--lease needs a value (got flag '%s{value}')"
            | "--lease" :: value :: t ->
                match System.Int32.TryParse value with
                | true, n when n > 0 -> flags { acc with LeaseMinutes = n } t
                // A LEASE OF ZERO WOULD MAKE EVERY CLAIM INSTANTLY REAPABLE. That is never what anyone meant,
                // and it would turn the lock into a no-op under exactly the fan-out it exists for.
                | true, n -> Error $"--lease must be a positive number of minutes (got %d{n})"
                | _ -> Error $"--lease needs a number of minutes (got '%s{value}')"
            | [ "--lease" ] -> Error "--lease needs a value"

            | "--json" :: t -> flags { acc with Render = Json } t
            | "--text" :: t -> flags { acc with Render = Text } t

            | other :: _ -> Error $"unknown argument: %s{other}"

        let defaults =
            { Command = Decide
              Render = Json
              SnapshotFile = None
              Repo = None
              Fresh = false
              AllowBacklog = false
              Limit = None
              LeaseMinutes = DefaultLeaseMinutes }

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

        | other :: _ -> Error $"unknown command: %s{other}"
