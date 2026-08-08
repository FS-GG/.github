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

    type Result =
        | Project of status: BoardStatus * observedAt: int64
        | Withheld of reason: string

    /// Persisted ordering receipt for lifecycle projections.
    type Watermark = { ObservedAt: int64; Status: BoardStatus }

    /// Computes the newest coherent lifecycle status. Facts older than the newest observation are
    /// deliberately withheld, so delayed webhook delivery cannot regress the Project row.
    val project: Observation -> Result

    /// Rejects stale or contradictory event observations against a persisted projection receipt.
    val advance: Watermark option -> Observation -> Result
