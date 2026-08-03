namespace FS.GG.Coord

/// Pure, fail-closed transitions for the two-wave coordination driver.
module Driver =
    open Batch

    type Housekeeping =
        { HasHostIdentity: bool; StaleClaim: bool; EngineCurrent: bool; PendingWrites: int
          ReconcileDryRunFresh: bool; ReconcileApplied: bool; ReconcileFresh: bool
          TriageFresh: bool; CurrencyScoped: bool }

    /// Machine-validated evidence decision carried by the latest passing independent-review marker.
    type RuntimeRouteEvidence =
        | Meaningful of builtArtifact: string * executedCommand: string * comparedRoutes: string * observedResult: string
        | NotMeaningful of reason: string

    type ReviewChain =
        { MarkerValid: bool; CriticIdentity: string option; HeadSha: string option
          Rounds: int list; RepairPhase: bool; ChecksGreen: bool; HostAccepted: bool
          RuntimeRouteEvidence: RuntimeRouteEvidence option }

    type ReviewComment = { Id: int64; Url: string; Body: string }

    val parseReviewComments: comments: ReviewComment list -> Result<ReviewChain, string list>

    type Receipt =
        { ObservedAt: int64; SourceSha: string; Complete: bool; Review: ReviewChain option }

    val receiptFresh: now: int64 -> maxAgeSeconds: int64 -> Receipt -> bool

    type WorkerReturn =
        { ClaimLive: bool; ReviewReady: bool; ParkedOrDone: bool }

    /// A content-addressed result emitted by one deterministic housekeeping read/command.  Every
    /// constituent is bound to the full live fact source; a same-occupancy state change invalidates it.
    type PlanningObservation =
        { Kind: string; ObservedAt: int64; SourceSha: string; Outcome: string; ReceiptId: string }

    type PlanningReceipt =
        { ObservedAt: int64; SourceSha: string; Complete: bool; ConsolidationApproved: bool
          Observations: PlanningObservation list }

    val observationReceiptId: kind: string -> observedAt: int64 -> sourceSha: string -> outcome: string -> string

    val planningReceiptFresh: now: int64 -> maxAgeSeconds: int64 -> sourceSha: string -> PlanningReceipt -> bool

    type Action =
        | RequestHostIdentity | ReapStaleClaims | RepairEngineCurrency | FlushPendingWrites
        | ReconcileBoard | RefreshTriage | Consolidate | ResumeSameWorker
        | DispatchWave of slots: int | ContinueCurrentWave

    val validateReviewChain: maxRounds: int -> ReviewChain -> string list
    val nextAction: model: WaveModel -> activeItems: int -> consolidationApproved: bool -> housekeeping: Housekeeping -> workerReturns: WorkerReturn list -> Action
