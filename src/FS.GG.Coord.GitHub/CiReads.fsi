namespace FS.GG.Coord.GitHub

module CiReads =
    type Page =
        {
            Resource: string
            Index: int
            Count: int
            Total: int
        }

    type TargetAssociation =
        | Exact
        | Missing
        | Conflicting
        | Unsupported

    type Run =
        {
            Id: int64
            Attempt: int
            Workflow: string
            Event: string
            HeadSha: string
            Status: string
            Conclusion: string option
            CreatedAt: string option
            RunStartedAt: string option
            UpdatedAt: string option
            PullRequests: int list
            TargetAssociation: TargetAssociation
        }

    type Step =
        {
            Number: int
            Name: string
            Status: string
            Conclusion: string option
            StartedAt: string option
            CompletedAt: string option
        }

    type Job =
        {
            RunId: int64
            Attempt: int
            Id: int64
            Name: string
            Status: string
            Conclusion: string option
            CreatedAt: string option
            StartedAt: string option
            CompletedAt: string option
            RunnerName: string option
            CheckRunId: int64 option
            Steps: Step list
        }

    type Check =
        {
            Id: int64
            Name: string
            AppSlug: string option
            Status: string
            Conclusion: string option
            StartedAt: string option
            CompletedAt: string option
        }

    type Snapshot =
        {
            Repository: string
            Head: string
            PullRequest: int
            Workflow: string
            Binding: string
            Pages: Page list
            Runs: Run list
            Jobs: Job list
            InventoryCoverage: string
            AttemptCoverage: string
            JobPageCoverage: string
            TerminalCoverage: string
            TimestampCoverage: string
            LineageCoverage: string
            Diagnostic: string option
        }

    type PopulationSnapshot =
        {
            Snapshot: Snapshot
            BaseRef: string option
            BaseSha: string option
            Checks: Check list
            CheckCoverage: string
            ExternalChecks: int
            AdmissionWitness: bool
            Revision: int64
            Pending: string list
            Gaps: string list
        }

    /// Classify a native target relation; no heuristic association is attempted.
    val targetAssociation:
        repository: string -> pr: int -> head: string -> baseSha: string option ->
        item: System.Text.Json.JsonElement -> TargetAssociation

    /// Read one bounded profile file at the exact selected source revision.
    val readAttributionProfile:
        transport: Transport.ISinglePageGitHubTransport -> repository: string -> head: string ->
            Errors.IoResult<byte array option>

    val collect:
        transport: Transport.ISinglePageGitHubTransport ->
        apiBase: string ->
        owner: string ->
        repo: string ->
        pr: int ->
        head: string ->
        workflow: string ->
            Errors.IoResult<Snapshot>

    /// Discover every Actions workflow run and native check on one candidate head. `admitted` permits
    /// a previously witnessed head to finish reconciling after the PR has moved; it never admits a head.
    val discoverPopulation:
        transport: Transport.ISinglePageGitHubTransport ->
        apiBase: string ->
        owner: string ->
        repo: string ->
        pr: int ->
        head: string ->
        baseRef: string ->
        baseSha: string ->
        admitted: bool ->
            Errors.IoResult<PopulationSnapshot>

    /// Private provider progress, separate from receiver facts and ingest cursor. A population is
    /// present only after two complete resource sweeps and the fresh complete inventory barrier.
    type ContinuationResult =
        { Checkpoint: FS.GG.Coord.TelemetryCi.ProviderCheckpoint.Checkpoint
          Population: PopulationSnapshot option
          Requests: int
          Gap: string option }

    /// Resume one unchanged admitted identity under a total40 GET/30-second allowance, including the
    /// pinned profile. All retained bodies are parsed again. PublicationPending refuses until C3's
    /// exact receiver readback; this function performs no file/database writes or retries.
    /// `admitted` requires authentic original receiver admission from the caller, never a cache flag;
    /// it preserves original-head reconciliation after supersession and reports actual moved-base gaps.
    val resumePopulation:
        transport: Transport.IDeadlineSinglePageGitHubTransport ->
        expected: FS.GG.Coord.TelemetryCi.ProviderCheckpoint.Identity ->
        clock: (unit -> System.DateTimeOffset) ->
        admitted: bool ->
        storeRevisionFloor: int64 ->
        original: FS.GG.Coord.TelemetryCi.ProviderCheckpoint.Checkpoint option ->
            Errors.IoResult<ContinuationResult>

    /// Read-only DTOs from the existing retained native graph parser; not authority or freshness receipts.
    type RetainedSuiteWitness =
        { SuiteId: int64; AppId: int64; AppSlug: string option; Status: string; Conclusion: string option }

    type RetainedCheckMembershipWitness =
        { CheckId: int64; SuiteId: int64; AppId: int64; AppSlug: string option }

    type RetainedSuiteGraphPage =
        { Key: FS.GG.Coord.TelemetryCi.ProviderCheckpoint.ResourceKey
          BodyDigest: string
          NextPage: int option
          ObservedAt: System.DateTimeOffset
          ValidatedAt: System.DateTimeOffset
          Page: Page
          Suites: RetainedSuiteWitness list
          Checks: RetainedCheckMembershipWitness list
          CheckObservations: Check list }

    /// Exact retained-byte/full native graph projection only; never provider freshness or receiver authority.
    val retainedSuiteGraph:
        expected: FS.GG.Coord.TelemetryCi.ProviderCheckpoint.Identity -> now: System.DateTimeOffset ->
        admitted: bool -> checkpoint: FS.GG.Coord.TelemetryCi.ProviderCheckpoint.Checkpoint ->
            Errors.IoResult<RetainedSuiteGraphPage list>
