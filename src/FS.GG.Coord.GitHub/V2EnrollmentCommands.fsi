namespace FS.GG.Coord.GitHub

/// The batched programme-enrollment commands, shared by the `BoardV2Import` tool and the coordination engine so
/// that both reach the same code: `plan-successor`, `prepare-successor`, `bind-runtime`, `render-carrier`,
/// `resolve-issues` and `preflight`.
///
/// Each command prints one closed refusal code on stderr and never a payload, nonce, native identity, path or input
/// text; exit 2 is a refusal. `preflight` exits 0 (supported), 10 (unsupported) or 11 (unknown). The only effects are
/// exclusive owner-only output files, local `git` reads, the maintained gate scripts, and read-only `GET`s through
/// the GitHub CLI. None grants enrollment, admission or implementation authority.
module V2EnrollmentCommands =

    /// The command vocabulary, in the order documented.
    val commands: string list

    /// Run one command. `args.[0]` is the command name; the rest are its named arguments. Never throws.
    val run: command: string -> args: string array -> int
