namespace FS.GG.Coord

/// The engine's process exit codes, as ONE declaration site (#918, ADR-0046).
///
/// Before this, the codes were `[<Literal>] let` ints in THREE modules — `GitHub/Errors.fs`,
/// `Cli/Client.fs`, `Cli/Program.fs` — and two numbers carried two meanings each: `3` was both
/// `EX_OFFBOARD` and a RED verdict, `4` was both `EX_PARTIAL` and a NO-VERDICT. Nothing in the type
/// system could see the collision, and nothing could enumerate the set of codes a command returns, so
/// the generated `take`/`landable` exit-code tables were hand-derived and their completeness could only
/// be proof-read (#585/#889/#900/#916).
///
/// One union, one `toInt`, one shared number space. A collision now means two cases mapping to one
/// number in `toInt`, which is a diff a reviewer reads in one place; and `takeCodes`/`landableCodes`
/// make a command's return set a value a test can check the projection against for completeness.
module ExitCode =

    /// One exit code the engine can hand the OS. The cases are grouped by the layer that owns them,
    /// but they share one number space (`toInt`) — which is what makes the historical collision
    /// impossible to reintroduce silently.
    type ExitCode =
        /// 0 — success.
        | Green
        /// 1 — the engine refused the INPUT before it looked, or a read failed. No verdict, not retryable
        /// as-is.
        | Error
        /// 2 — an unhandled engine defect (`Program.main`'s top-level handler).
        | Defect
        /// 3 — a verdict of RED: a failed check (`landable`), or a batch `take` refused to schedule.
        | Red
        /// 4 — NO verdict could be reached; the FAIL-CLOSED code (#266).
        | NoVerdict
        /// 5 — EX_NONE (`take`): looked, and nothing was startable.
        | NoneStartable
        /// 6 — EX_CONTENDED (`take`): the claim CAS lost every race.
        | Contended
        /// 7 — PENDING (`landable`): the verdict has not settled; the one retryable code.
        | Pending
        /// 8 — EX_OFFBOARD: the issue is not an item on the board. Was `3`, which collided with `Red`
        /// (#918): off-board is a fact found on a SUCCESSFUL read, not a verdict.
        | Offboard
        /// 9 — EX_PARTIAL: a `set-field --batch` write half-landed. Was `4`, which collided with
        /// `NoVerdict` (#918): a half-written board is an OUTCOME, not the absence of an answer.
        | Partial
        /// 75 — EX_RATE: a rate budget is exhausted.
        | Rate

    /// The ONLY place an `ExitCode` becomes a number. Exhaustive by construction: a new case with no
    /// number does not compile, so a code can never reach the OS undefined.
    val toInt: code: ExitCode -> int

    /// The codes `take` can return (#585), as the domain `Protocol.takeExitCodes` is checked complete
    /// against. `take` never returns `Offboard`/`Partial` (those are `Errors`' write/read facts) nor
    /// `Pending`/`NoVerdict` (it has no PR verdict to settle).
    val takeCodes: ExitCode list

    /// The codes `landable` can return (#900). No `Rate`: `landable` is fail-closed by construction and
    /// folds a budget failure into `NoVerdict`, having no error channel of its own. No `NoneStartable`/
    /// `Contended`: it has no queue and takes no lock.
    val landableCodes: ExitCode list
