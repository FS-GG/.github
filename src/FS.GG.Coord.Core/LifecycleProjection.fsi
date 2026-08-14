namespace FS.GG.Coord

/// Pure, freshness-bound projection of observed coordination facts onto Project Status.
module LifecycleProjection =
    open FS.GG.Coord.Types

    type Fact<'a> = { ObservedAt: int64; Value: 'a }
    type PullRequest = { Number: int; Open: bool; ReviewOrCiActive: bool }
    type Delivery = { Outstanding: bool; DoneStamped: bool }
    type Observation =
        { Claim: Fact<(Claim * Liveness) option>
          PullRequest: Fact<PullRequest option>
          Blockers: Fact<Blocker list>
          Delivery: Fact<Delivery>
          Issue: Fact<IssueState> }

    /// An attributable, revisioned scheduling decision.  Status is deliberately absent: this is an
    /// input to the lifecycle reducer, never another spelling of its output.
    type IntentRecord =
        { Revision: int64
          Reason: string }

    /// Human/policy scheduling intent, independent of observed lifecycle facts and Project Status.
    type SchedulingIntent =
        | Auto
        | Backlog of IntentRecord
        | HumanPark of HumanBlock * IntentRecord
        | Deferred of reason: string * until: int64 option * revision: int64

    type PolicyVersion =
        | IntentStatusV1

    /// Compatibility switch. `Intent` is the normal path; `Legacy` is the bounded rollback path.
    type ProjectionMode =
        | Legacy
        | Intent

    type Result =
        | Project of status: BoardStatus * observedAt: int64
        | Withheld of reason: string

    /// Classification emitted by the old/new shadow comparison.  There is no untyped "different" case.
    type Difference =
        | Same
        | DeliberateParkPreserved of legacy: BoardStatus * intended: BoardStatus
        | Unexpected of legacy: Result * intended: Result

    type Shadow =
        { Legacy: Result
          Intended: Result
          Difference: Difference }

    /// Persisted ordering receipt for lifecycle projections.
    type Watermark =
        { ObservedAt: int64
          Status: BoardStatus
          /// The scheduling input used for this projection. `None` is a readable legacy v1 receipt.
          Intent: SchedulingIntent option }

    /// Stable, append-only receipt written only after a fresh board verification.
    val watermarkMarker: Watermark -> string

    /// Reads the newest valid durable receipt from issue comments.
    val tryWatermark: string list -> Watermark option

    /// Computes the newest coherent lifecycle status. Facts older than the newest observation are
    /// deliberately withheld, so delayed webhook delivery cannot regress the Project row.
    val project: Observation -> Result

    /// Pure lifecycle reducer: observed facts, scheduling intent, and policy version are separate inputs.
    val reduce: PolicyVersion -> SchedulingIntent -> Observation -> Result

    /// Migrate deliberate legacy parks into explicit intent.  Automatic lifecycle columns remain Auto.
    val migrateIntent:
        revision: int64 -> status: BoardStatus -> humanBlock: HumanBlock option -> SchedulingIntent

    /// Compute both projections and classify their difference before any projection is selected.
    val shadow: PolicyVersion -> SchedulingIntent -> Observation -> Shadow

    /// Parse the rollback switch. Missing means the intent reducer; only `legacy` rolls back.
    val projectionMode: string option -> Result<ProjectionMode, string>

    /// Select one already-computed shadow result; selection never re-runs either reducer.
    val select: ProjectionMode -> Shadow -> Result

    /// Rejects stale or contradictory event observations against a persisted projection receipt.
    val advance: Watermark option -> Observation -> Result

    /// Watermark-aware old/new shadow comparison used by reconciliation.
    val shadowAdvance: PolicyVersion -> SchedulingIntent -> Watermark option -> Observation -> Shadow
