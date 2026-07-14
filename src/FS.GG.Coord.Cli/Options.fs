namespace FS.GG.Coord.Cli

module Options =

    type Command =
        | Decide
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
          SnapshotFile: string option }

    let usage =
        """fsgg-coord-engine — the typed coordination engine (ADR-0034).

NOT A USER-FACING TOOL. `scripts/fsgg-coord` is the user-facing client; this is the engine it shells
out to. It is pure: it reads NOTHING — no board, no issues, no network, no GitHub token. The client
has already paid for the board scan by the time it decides, so it hands that state over on stdin and
this decides from it. That is what makes shadow mode free: two engines, one board read, one budget
(#418).

Usage:
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

            | "--json" :: t -> flags { acc with Render = Json } t
            | "--text" :: t -> flags { acc with Render = Text } t

            | other :: _ -> Error $"unknown argument: %s{other}"

        let defaults =
            { Command = Decide
              Render = Json
              SnapshotFile = None }

        match args with
        | []
        | "--help" :: _
        | "-h" :: _ -> Ok { defaults with Command = Help }

        | "--version" :: _ -> Ok { defaults with Command = Version }

        | "decide" :: rest -> flags { defaults with Command = Decide } rest
        | "fleet" :: rest -> flags { defaults with Command = FleetVerdict } rest
        | "lanes" :: rest -> flags { defaults with Command = LanesView } rest
        | "facts" :: rest -> flags { defaults with Command = Facts } rest

        | other :: _ -> Error $"unknown command: %s{other}"
