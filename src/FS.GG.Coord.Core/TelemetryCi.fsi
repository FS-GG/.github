namespace FS.GG.Coord

module TelemetryCi =
    [<Literal>]
    val AssignmentSchema: string = "fsgg.telemetry.ci-assignment/1"

    type Assignment =
        {
            FeatureId: string
            ItemId: string
            AttemptId: string
            ParentAttemptId: string option
            ProducerStream: string
        }

    /// Operator evidence plus exact delivery truth; never an observation or token record.
    type CorrectionRequest =
        {
            CorrectionId: string
            ExpectedPredecessor: string option
            Repository: string
            PullRequest: int64
            BaseRef: string
            BaseSha: string
            Head: string
            MergeCommit: string
            Prior: Assignment
            Effective: Assignment
            EvidenceSha256: string
            Reason: string
            OperatorSource: string
            ObservedAt: string
        }

    type CorrectionTarget =
        {
            Table: string
            Identity: string
            Revision: int64
            Digest: string
        }

    type CorrectionPlan =
        {
            StoreId: string
            Request: CorrectionRequest
            Targets: CorrectionTarget list
        }

    val parseCorrectionRequest: bytes: byte array -> Result<CorrectionRequest, string list>
    val parseCorrectionPlan: bytes: byte array -> Result<CorrectionPlan, string list>
    val correctionPlanJson: plan: CorrectionPlan -> string

    type Interval =
        {
            StartUtc: System.DateTimeOffset
            EndUtc: System.DateTimeOffset
        }

    type Timing =
        {
            RunnerSeconds: int64 option
            WallSeconds: int64 option
            AdministrativeSeconds: int64 option
            CriticalPathSeconds: int64 option
        }

    val parseAssignment: bytes: byte array -> Result<Assignment, string list>
    val interval: startUtc: string option -> endUtc: string option -> Interval option
    val unionSeconds: intervals: Interval list -> int64 option
    val subtractSeconds: source: Interval list -> excluded: Interval list -> int64 option

    /// Bounded producer-private provider progress; never receiver facts or ingest cursor.
    module ProviderCheckpoint =
        type Identity =
            { Assignment: Assignment
              AssignmentDigest: string
              StoreId: string
              Collection: string
              ApiOrigin: string
              Repository: string
              RepositoryId: int64
              PullRequest: int64
              BaseRef: string
              BaseSha: string
              Head: string
              ProfileDigest: string
              ParserContract: string }

        type ResourceKind =
            | Admission
            | Runs
            | Attempt
            | Jobs
            | Checks
            | Suites
            | SuiteChecks

        type ResourceKey =
            { Kind: ResourceKind
              RunId: int64
              SuiteId: int64
              Attempt: int
              Page: int }

        type Resource =
            { Key: ResourceKey
              Body: string
              BodyDigest: string
              Validator: string
              Next: string
              ProjectionDigest: string
              ObservedAt: System.DateTimeOffset
              ValidatedAt: System.DateTimeOffset }

        type Phase =
            | Acquiring
            | FirstSweep
            | SecondSweep
            | FinalBarrier
            | Stable
            | PublicationPending

        type FactReference =
            { Kind: string
              Identity: string
              Revision: int64
              Digest: string }

        type Checkpoint =
            { Identity: Identity
              Generation: int64
              CreatedAt: System.DateTimeOffset
              ObservedThrough: System.DateTimeOffset
              PriorCheckpointDigest: string
              CandidateDigest: string
              Phase: Phase
              Cursor: int
              Resources: Resource list
              FirstSweepDigest: string
              SecondSweepDigest: string
              Revision: int64
              PendingBatchDigest: string
              PendingBatchBase64: string
              FactReferences: FactReference list }

        val maximumResources: int
        val maximumBodyBytes: int
        val maximumAggregateBodyBytes: int
        val maximumCheckpointBytes: int
        val maximumAge: System.TimeSpan
        val digest: bytes: byte array -> string
        val identityDigest: identity: Identity -> string
        val populationDigest: resources: Resource list -> string
        val validate: expected: Identity -> now: System.DateTimeOffset -> checkpoint: Checkpoint -> Result<Checkpoint, string list>
        val beginSweep: checkpoint: Checkpoint -> Result<Checkpoint, string list>
        val advanceSweep: resourceKey: ResourceKey -> changed: bool -> checkpoint: Checkpoint -> Result<Checkpoint, string list>
        val finishSweep: checkpoint: Checkpoint -> Result<Checkpoint, string list>
        val nextRevision: prior: int64 -> storeFloor: int64 -> Result<int64, string list>
        val parse: expected: Identity -> now: System.DateTimeOffset -> bytes: byte array -> Result<Checkpoint, string list>
        val encode: expected: Identity -> now: System.DateTimeOffset -> checkpoint: Checkpoint -> Result<byte array, string list>
