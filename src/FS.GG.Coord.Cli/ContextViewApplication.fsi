namespace FS.GG.Coord.Cli

/// `context-view`: the optional, read-only scoped context view (#4940, Design B of #4730).
///
///   fsgg-coord-engine context-view --issue OWNER/REPO#N --report-file NEW_PATH
///       [--pr N] [--action implement|repair|review|merge|accept|resume]
///       [--cap-bytes N] [--custody CURSOR_FILE] [--workspace DIR]
///
/// Reads the owning issue (and the pull request and its check runs when `--pr` is given) with GET requests
/// only, optionally the Design A operation cursor and a local workspace, builds the typed view with the pure
/// `FS.GG.Coord.ContextView` core and writes it to the NEW file `--report-file` (exclusive create, owner-only
/// permissions). It writes nothing to GitHub, Status or any ledger. One compact result line is printed.
module ContextViewApplication =
    open FS.GG.Coord.ContextView

    type Arguments =
        { Issue: IssueRef
          PullRequest: int option
          Action: Action
          CapBytes: int
          Custody: string option
          Workspace: string option
          ReportFile: string }

    /// Closed flag vocabulary; unknown, repeated or missing-value flags are refused.
    val parseArguments: string list -> Result<Arguments, string>

    /// Read the Design A cursor file as the custody facts. Never rewrites it.
    val readCustody: path: string -> Read<CustodyFact>

    /// Verify the requested repository-relative files and read the Atlas archive under a workspace directory.
    val readNavigation: root: string -> paths: string list -> Read<NavigationFacts>

    /// Fetch the observations through the transport (GET only) and the local sources.
    val observe:
        transport: FS.GG.Coord.GitHub.Transport.IDeadlineSinglePageGitHubTransport ->
        arguments: Arguments -> Observations

    /// Build, then create the report file exclusively. Returns the compact result line and exit code.
    val execute:
        transport: FS.GG.Coord.GitHub.Transport.IDeadlineSinglePageGitHubTransport ->
        arguments: Arguments -> string * int

    /// The help text printed by `context-view --help`.
    val usage: string

    val tryRun: string list -> int option
