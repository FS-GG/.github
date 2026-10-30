namespace FS.GG.Coord.GitHub

/// Read-only native reads for the scoped context view (#4940). Each is one bounded GET over the
/// single-page deadline transport: no mutation interface, retry, redirect or implicit pagination. A
/// failed or malformed read becomes `Unavailable reason`, never an empty success.
module ContextViewReads =
    open FS.GG.Coord.ContextView

    /// `GET repos/{repository}/issues/{number}`. A number that names a pull request is refused.
    val readIssue: transport: Transport.IDeadlineSinglePageGitHubTransport -> issue: IssueRef -> Read<IssueFact>

    /// `GET repos/{repository}/pulls/{number}` followed by one `GET .../commits/{head}/check-runs?per_page=100`.
    /// A failed check read leaves the pull request supplied with `Checks = Unavailable`.
    val readPullRequest: transport: Transport.IDeadlineSinglePageGitHubTransport -> repository: string -> number: int -> Read<PullRequestFact>
