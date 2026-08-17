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
        /// **THE ROW IS A STANDING ITEM AND HAS NO LIFECYCLE TO PROJECT (.github#2712).**
        ///
        /// A DISTINCT case, deliberately not folded into `Withheld of reason: string`. `Withheld` says
        /// *not on this observation* — a caller retries it next pass, and rightly. `Exempt` says *never*,
        /// and the difference has to be one the compiler can enforce: a string reason is something a
        /// caller may only log, whereas a union case makes every `match` in the repository fail the build
        /// until its author has decided what an exempt row means there. That is what turns "the reducer
        /// wrote no Status and no watermark" from an assertion into a checked property.
        | Exempt of kind: ItemKind

    /// Persisted ordering receipt for lifecycle projections. Callers persist this beside the status
    /// write and feed it back on the next event; keeping the watermark in the typed boundary is what
    /// makes an event that arrived late a no-op rather than an opportunity to re-derive an older
    /// column value.
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
    ///
    /// **`kind` IS A REQUIRED POSITIONAL ARGUMENT, AND THAT IS THE POINT (.github#2712).** A standing kind
    /// answers `Exempt` before `intent` or `observation` is consulted at all. It is a PARAMETER rather
    /// than a `SchedulingIntent` case or an arm of the caller's policy function because both of those live
    /// downstream of the persisted watermark, and a watermark's mere existence suppresses policy
    /// re-derivation (`Client.fs:2492`) — so an exemption expressed either way could be frozen by a
    /// receipt the row already carries, which since .github#2690 is every `add`-filed row. A parameter has
    /// no such channel: the watermark carries `Intent`, never `Kind`.
    ///
    /// Being required is the other half. No caller can adopt the new signature by accident, forget the
    /// argument, or default it; `Kind.govern` is the one place the `None`-means-`Work` reading is spelled.
    val reduce: PolicyVersion -> ItemKind -> SchedulingIntent -> Observation -> Result

    /// Rejects stale or contradictory event observations against a persisted projection receipt: a newly
    /// projected lifecycle result is accepted only when it is newer than the last applied one. EQUAL
    /// timestamps are idempotent only when the two agree; different values at the same timestamp are
    /// WITHHELD, because an ordering source that cannot separate them is not strong enough to decide
    /// which event won.
    ///
    /// `kind` is tested BEFORE the watermark comparison, not merely before the projection: a standing row
    /// must not be able to reach a verdict *about* a watermark either, because the ordering refusals read
    /// as "we could not decide yet" where the right answer is "there is nothing to decide, ever".
    val advance: PolicyVersion -> ItemKind -> SchedulingIntent -> Watermark option -> Observation -> Result
