namespace FS.GG.Coord.GitHub

module CiReads =
    type Page = { Resource: string; Index: int; Count: int; Total: int }
    type Run =
        { Id: int64; Attempt: int; Workflow: string; Event: string; HeadSha: string
          Status: string; Conclusion: string option; CreatedAt: string option
          RunStartedAt: string option; UpdatedAt: string option; PullRequests: int list }
    type Step =
        { Number: int; Name: string; Status: string; Conclusion: string option
          StartedAt: string option; CompletedAt: string option }
    type Job =
        { RunId: int64; Attempt: int; Id: int64; Name: string; Status: string
          Conclusion: string option; CreatedAt: string option; StartedAt: string option
          CompletedAt: string option; RunnerName: string option; CheckRunId: int64 option; Steps: Step list }
    type Check =
        { Id: int64; Name: string; AppSlug: string option; Status: string
          Conclusion: string option; StartedAt: string option; CompletedAt: string option }
    type Snapshot =
        { Repository: string; Head: string; PullRequest: int; Workflow: string
          Binding: string; Pages: Page list; Runs: Run list; Jobs: Job list
          InventoryCoverage: string; AttemptCoverage: string; JobPageCoverage: string
          TerminalCoverage: string; TimestampCoverage: string; LineageCoverage: string; Diagnostic: string option }

    type PopulationSnapshot =
        { Snapshot: Snapshot
          BaseRef: string option
          BaseSha: string option
          Checks: Check list
          CheckCoverage: string
          ExternalChecks: int
          AdmissionWitness: bool
          Revision: int64
          Pending: string list
          Gaps: string list }

    val collect:
        transport: Transport.ISinglePageGitHubTransport -> apiBase: string -> owner: string -> repo: string ->
        pr: int -> head: string -> workflow: string -> Errors.IoResult<Snapshot>

    /// Discover every Actions workflow run and native check on one candidate head. `admitted` permits
    /// a previously witnessed head to finish reconciling after the PR has moved; it never admits a head.
    val discoverPopulation:
        transport: Transport.ISinglePageGitHubTransport -> apiBase: string -> owner: string -> repo: string ->
        pr: int -> head: string -> baseRef: string -> baseSha: string -> admitted: bool -> Errors.IoResult<PopulationSnapshot>
