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
          CompletedAt: string option; RunnerName: string option; Steps: Step list }
    type Snapshot =
        { Repository: string; Head: string; PullRequest: int; Workflow: string
          Binding: string; Pages: Page list; Runs: Run list; Jobs: Job list
          InventoryCoverage: string; AttemptCoverage: string; JobPageCoverage: string
          TerminalCoverage: string; TimestampCoverage: string; LineageCoverage: string; Diagnostic: string option }

    val collect:
        transport: Transport.ISinglePageGitHubTransport -> apiBase: string -> owner: string -> repo: string ->
        pr: int -> head: string -> workflow: string -> Errors.IoResult<Snapshot>
