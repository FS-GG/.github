namespace FS.GG.Coord

open FS.GG.Coord.Types

/// One pure status projection shared by webhook and scheduled-reconciliation callers.
module LifecycleProjection =
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

    let private latest observation =
        [ observation.Claim.ObservedAt; observation.PullRequest.ObservedAt; observation.Blockers.ObservedAt
          observation.Delivery.ObservedAt; observation.Issue.ObservedAt ]
        |> List.max

    let private coherent observation timestamp =
        [ observation.Claim.ObservedAt; observation.PullRequest.ObservedAt; observation.Blockers.ObservedAt
          observation.Delivery.ObservedAt; observation.Issue.ObservedAt ]
        |> List.forall ((=) timestamp)

    let project observation =
        let observedAt = latest observation
        if not (coherent observation observedAt) then
            Withheld "lifecycle facts have different observation timestamps"
        elif observation.Blockers.Value |> List.exists (fun blocker -> blocker.State <> BlockerClosed && blocker.State <> BlockerMerged) then
            Project(Blocked, observedAt)
        elif observation.Delivery.Value.DoneStamped && observation.Issue.Value = Closed then
            Project(Done, observedAt)
        elif observation.Delivery.Value.Outstanding then
            Project(InReview, observedAt)
        elif observation.PullRequest.Value |> Option.exists (fun pr -> pr.Open) then
            Project(InReview, observedAt)
        elif observation.Claim.Value |> Option.isSome then
            Project(InProgress, observedAt)
        else
            Project(Ready, observedAt)
