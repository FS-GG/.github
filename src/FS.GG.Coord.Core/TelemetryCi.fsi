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
