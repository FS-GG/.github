namespace FS.GG.Coord.GitHub

/// The batched programme-enrollment commands, shared by the `BoardV2Import` tool and the coordination engine so
/// that both reach the same code: `plan-successor`, `prepare-successor`, `bind-runtime`, `rebind-reader`,
/// `render-carrier`, `resolve-issues` and `preflight`.
///
/// Each command prints one closed refusal code on stderr and never a payload, nonce, native identity, path or input
/// text; exit 2 is a refusal. `preflight` exits 0 (supported), 10 (unsupported) or 11 (unknown). The only effects are
/// exclusive owner-only output files, local `git` reads, the maintained gate scripts, and read-only `GET`s through
/// the GitHub CLI. None grants enrollment, admission or implementation authority.
module V2EnrollmentCommands =

    /// The command vocabulary, in the order documented.
    val commands: string list

    /// The `ConstructorIdentity` content evidence for one exact pair of revisions, read from the checkout's own objects
    /// (#5113): whether `main` descends from `built`, and how many changed paths between them are inside or outside the
    /// constructor closure derived at `built`. A failed read, an underivable closure or an unparseable change listing
    /// is `NotObserved`, never evidence.
    val observeConstructorContent: repository: string -> built: string -> main: string -> V2Preflight.Observed<V2Preflight.ConstructorContent>

    /// Run one command. `args.[0]` is the command name; the rest are its named arguments. Never throws.
    val run: command: string -> args: string array -> int

    /// `run` as the named host assembly: the coordination engine, whose own identity `rebind-reader` binds a migration
    /// scope's `cliArtifactSha256` to. Without a host (`run`), a scope rebind refuses `ReaderNotVerified`.
    val runHosted: host: System.Reflection.Assembly -> command: string -> args: string array -> int

    /// The pure splice behind `rebind-reader` (#5120): each `(name, old, new)` names a top-level string field, its current
    /// value and a 40- or 64-hex replacement. Only that value's bytes change; every other byte is carried over. `Error
    /// MalformedInput` unless each old value is the top-level value, its `"name": "old"` occurrence is unique in the text,
    /// and, with the named fields removed, the two documents are equal.
    val rebindFields: json: string -> fields: (string * string * string) list -> Result<string, V2Enrollment.Refusal>
