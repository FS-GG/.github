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

    /// True only for a typed human scheduling hold; never derived from mutable Status or prose.
    val isHumanPark: SchedulingIntent -> bool

    type PolicyVersion =
        | IntentStatusV1

    type Result =
        | Project of status: BoardStatus * observedAt: int64
        | Withheld of reason: string

    /// Persisted ordering receipt for lifecycle projections.
    type Watermark =
        { ObservedAt: int64
          Status: BoardStatus
          Intent: SchedulingIntent }

    /// Stable, append-only receipt written only after a fresh board verification.
    val watermarkMarker: Watermark -> string

    /// Reads the newest valid durable receipt from issue comments.
    val tryWatermark: string list -> Watermark option

    /// The operator-writable intent channel (.github#2690): the fresh intent an EXPLICIT status write
    /// records, so the write survives the next reducer pass instead of being silently recomputed away.
    /// `None` for every column intent does not decide — `Blocked` (already re-derived from its own durable
    /// park, and a frozen `HumanPark` here could never be lifted), the three observation-projected columns,
    /// and `NoStatus`. `observedAt` must be the reducer's own Unix-millisecond clock: `tryWatermark` orders
    /// by it, and that ordering is what makes a decision recorded now outrank one frozen hours ago.
    val explicitStatusWatermark: observedAt: int64 -> reason: string -> BoardStatus -> Watermark option

    /// Pure lifecycle reducer: observed facts, scheduling intent, and policy version are separate inputs.
    val reduce: PolicyVersion -> SchedulingIntent -> Observation -> Result

    /// Rejects stale or contradictory event observations against a persisted projection receipt.
    val advance: PolicyVersion -> SchedulingIntent -> Watermark option -> Observation -> Result
