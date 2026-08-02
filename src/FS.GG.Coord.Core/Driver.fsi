namespace FS.GG.Coord

/// Pure, fail-closed transitions for the two-wave coordination driver.
module Driver =
    open Batch

    type Housekeeping =
        { HasHostIdentity: bool; StaleClaim: bool; EngineCurrent: bool; PendingWrites: int
          ReconcileDryRunFresh: bool; ReconcileApplied: bool; ReconcileFresh: bool
          TriageFresh: bool; CurrencyScoped: bool }

    type ReviewChain =
        { MarkerValid: bool; CriticIdentity: string option; HeadSha: string option
          Rounds: int list; ChecksGreen: bool; HostAccepted: bool }

    type WorkerReturn =
        { ClaimLive: bool; ReviewReady: bool; ParkedOrDone: bool }

    type Action =
        | RequestHostIdentity | ReapStaleClaims | RepairEngineCurrency | FlushPendingWrites
        | ReconcileBoard | RefreshTriage | Consolidate | ResumeSameWorker
        | DispatchWave of slots: int | ContinueCurrentWave

    val validateReviewChain: maxRounds: int -> ReviewChain -> string list
    val nextAction: model: WaveModel -> activeItems: int -> consolidationApproved: bool -> housekeeping: Housekeeping -> workerReturns: WorkerReturn list -> Action
