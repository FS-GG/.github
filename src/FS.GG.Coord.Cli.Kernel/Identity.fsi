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
          Provenance: Provenance

          /// **WHO THIS PROCESS IS, AS OPPOSED TO WHO IT WAS TOLD TO BE** (#1646) — rules 2-4 above with rule
          /// 1 removed. `Id` is what the caller asked to be; this is what it is, and the two differ on
          /// exactly one shape: `--worker <somebody else>`.
          ///
          /// That shape was a working impersonation. `verifyHeld` opened the door to `Held` when the live
          /// marker's id matched ours AND `twinSession` did not say twin — and `twinSession` says twin only
          /// when both sessions are known and DIFFER. Every subagent of one Claude Code session shares one
          /// session id, so a caller passing another worker's id matched on BOTH legs: the id because it was
          /// copied off the board, the session because a sibling's session is also ours. Both of #419's and
          /// #1031's repairs made the protocol better at telling ids apart; neither can help when the second
          /// factor is shared too. This is the third fact, and the only one `--worker` cannot restate.
          ///
          /// `None` means this process resolves NOTHING of its own — a human operator, a harness that exports
          /// no session and sets no `$FSGG_WORKER`. The impersonation question is then UNASKABLE rather than
          /// answered "no"; see `Writes.SelfIdentity`, which is what this becomes at the lock boundary.
          ///
          /// It is NOT a proof of identity, and the refusal built on it must not be sold as one:
          /// `$FSGG_WORKER` is an assertion too. What it removes is the property #1646 names as the harm —
          /// that impersonating was QUIETER and one flag shorter than `claim --force`, the sanctioned steal.
          Derived: string option }

    /// A filename/id-safe slug, matching the bash client's `slug` so an id round-trips identically. Worker
    /// ids are slug()'d at creation, so any surface that ADDRESSES a worker (e.g. `say --to`) must run its
    /// target through the SAME slug or it names an id nobody holds. The one home for that normalization.
    val slug: s: string -> string

    /// Resolve the worker id. `worker` is the `--worker` flag value, if any.
    ///
    /// `Error` when no id can be derived without inventing a shared one — with a message telling the caller
    /// to pass `--worker` or `eval "$(scripts/fsgg-coord whoami --mint)"`. The remedy names the RESOLVER
    /// (`scripts/fsgg-coord`), never this engine's own binary: `fsgg-coord-engine` is not on PATH, so a
    /// worker who pastes it gets `command not found` (#569).
    ///
    /// **POST-CONDITION: a returned `Worker` has a NON-EMPTY `Id`** — and rule 4 is why it has to be stated
    /// here rather than left to the caller. The id is the `slug` of the input, and `slug` annihilates any
    /// all-punctuation string (`.`, `///`, `..` → `""`), so a guard on the INPUT could not see it: `.` is
    /// not whitespace, so it passed, and this function answered `Ok` with an empty id (#1070). An empty id
    /// is one that EVERY caller whose input annihilates shares — rule 4's shared id, manufactured by the
    /// resolver instead of invented by an agent, and #419's collapse either way.
    ///
    /// The refusal names the OFFENDING INPUT, because a diagnostic that names the wrong cause sends the
    /// reader somewhere there is nothing to find (#611): `$FSGG_WORKER='.'` is a variable that IS set, so
    /// "could not derive a worker id" would be true and useless. It does NOT fall back to the session id —
    /// on a harness that shares one across subagents that trades one shared id for another, and `whoami`
    /// would still report success over an identity that cannot hold a lock (#266).
    val resolve: worker: string option -> Result<Worker, string>

    /// WHERE `Worker.Derived` CAME FROM — rules 2-4's provenance, with `--worker` taken away (#1646).
    ///
    /// It exists for one caller and one sentence: the impersonation refusal sends a worker back to its OWN
    /// id, and has to be able to say when that id is itself a shared one. Nothing else would say it — the
    /// `#419` shared-session warning is printed off `Worker.Provenance`, which is `FromFlag` whenever
    /// `--worker` is present, so a caller in exactly the impersonation state has never been warned. A refusal
    /// that quietly recommends a shared id would re-create #419 in the act of closing #1646.
    ///
    /// `None` for a process that derives no identity of its own, exactly as `Worker.Derived` is.
    val derivedProvenance: unit -> Provenance option

    /// Mint a fresh, genuinely-random id (`whoami --mint`). One `word-hhhh` line, so
    /// `eval "$(… whoami --mint)"` is the whole ritual. It does NOT consult the current identity — minting
    /// is for making a NEW worker, and reading the old one would defeat the point.
    val mint: unit -> string

    /// True only for an already-resolved canonical id in the exact shape emitted by `mint`:
    /// one word from the shared mint table, `-`, and four lowercase hexadecimal digits.
    val isMintedWorkerId: id: string -> bool

    /// The `whoami` report: the id, the session, and the rule that fired — the last because an id nobody
    /// can explain is one nobody can trust.
    ///
    /// It adds a `self:` line ONLY when `Derived` disagrees with `Id` (#1646) — the impersonation shape, and
    /// the only case where that field says something the other lines do not. `whoami` is where a worker
    /// checks its identity before a lock verb refuses it, so a refusal this report cannot reproduce is one
    /// the worker has to guess at.
    val explain: worker: Worker -> string list
