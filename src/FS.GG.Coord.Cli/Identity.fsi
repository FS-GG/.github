namespace FS.GG.Coord.Cli

/// WHO IS ASKING — the worker id, and where it came from.
///
/// ADR-0027 (superseding ADR-0021 §1): the lock is keyed on a WORKER, not on the GitHub account. N agents
/// in a fan-out authenticate as the SAME account, so `@me` is one principal for all of them and an
/// assignee-keyed lock is a no-op under exactly the conditions it exists for. So every lock command needs a
/// worker id, and where that id comes from is a lock-safety question, not a convenience.
///
/// THE RULE, IN PRECEDENCE ORDER (the bash client's, preserved):
///   1. `--worker <id>`      — an explicit instruction; honoured verbatim.
///   2. `$FSGG_WORKER`       — the exported id a fan-out sets once per worker.
///   3. the harness session  — derived deterministically from the agent session id, BUT only trustworthy
///                             where the harness gives each subagent its own id. Claude Code shares one id
///                             across every subagent it spawns, so a session-derived id there names the
///                             SESSION, not the worker — and #419 is what happens when N agents collapse
///                             onto one id.
///   4. refuse               — rather than invent an id that several workers would share. An invented
///                             shared id is a lock that succeeds under exactly the fan-out it must prevent.
module Identity =

    /// Which rule produced the id — because a surprising id is a lock bug waiting to happen, and `whoami`
    /// exists to make the derivation legible before it costs a double-claim.
    type Provenance =
        | FromFlag
        | FromEnv of var: string
        | FromSession of harness: string * sessionId: string
        /// Derived from a session id on a harness that SHARES it across subagents — carried with the reason
        /// it is unsafe, so the caller can warn rather than lock silently.
        | FromSharedSession of harness: string * sessionId: string * why: string

    type Worker =
        { Id: string
          Session: string option
          Provenance: Provenance }

    /// Resolve the worker id. `worker` is the `--worker` flag value, if any.
    ///
    /// `Error` when no id can be derived without inventing a shared one — with a message telling the caller
    /// to pass `--worker` or `eval "$(fsgg-coord-engine whoami --mint)"`, exactly as the bash client does.
    val resolve: worker: string option -> Result<Worker, string>

    /// Mint a fresh, genuinely-random id (`whoami --mint`). One `word-hhhh` line, so
    /// `eval "$(… whoami --mint)"` is the whole ritual. It does NOT consult the current identity — minting
    /// is for making a NEW worker, and reading the old one would defeat the point.
    val mint: unit -> string

    /// The `whoami` report: the id, the session, and the rule that fired — the last because an id nobody
    /// can explain is one nobody can trust.
    val explain: worker: Worker -> string list
