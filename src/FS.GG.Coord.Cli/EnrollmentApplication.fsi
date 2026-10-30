namespace FS.GG.Coord.Cli

/// `enrollment` and `route-preflight`: the batched programme-enrollment commands and the supported-route preflight
/// (#4941), registered in the engine so that they reach the installed tool as `fsgg-coord`.
///
///   fsgg-coord enrollment plan|prepare|bind-runtime|render-carrier|resolve-issues  NAMED-ARGUMENTS
///   fsgg-coord route-preflight --route ROUTE --stage STAGE --repository CHECKOUT [route arguments]
///
/// Both are dispatch arms ahead of the typed handler table, like `context-view`; they are not in the command catalogue and are classified in the V1 writer census
/// (`outOfCatalogueRoots`, `writes: never`). The code is the shared
/// `FS.GG.Coord.GitHub.V2EnrollmentCommands`, the same code the `BoardV2Import` tool runs, so the two surfaces cannot drift.
///
/// Every command prints one closed refusal code on stderr and never a payload, nonce, native identity, path or input text.
/// Exit codes: 0 done; 2 refused; for `route-preflight` additionally 10 (unsupported) and 11 (unknown), and 0 only when every
/// check is supported. The only effects are exclusive owner-only output files, local `git` reads, the maintained gate scripts
/// (run from the base blobs) and read-only `GET`s; none writes to GitHub or grants enrollment, admission or implementation authority.
module EnrollmentApplication =

    /// The two root words this module answers.
    val roots: string list

    /// The `enrollment` verbs and the library command each maps to.
    val enrollmentVerbs: (string * string) list

    /// The help text printed by `enrollment --help` and `route-preflight --help`.
    val usage: string

    /// `Some exit code` when the first word is one of `roots`, otherwise `None`.
    val tryRun: string list -> int option
