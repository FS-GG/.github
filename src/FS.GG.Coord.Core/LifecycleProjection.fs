namespace FS.GG.Coord

open System
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

    /// The durable portion of a projection receipt.  Callers persist this beside the status write and
    /// feed it back on the next event.  Keeping the water-mark in the typed boundary makes an event that
    /// arrived late a no-op rather than an opportunity to re-derive an older column value.
    type Watermark = { ObservedAt: int64; Status: BoardStatus }

    /// The comment-shaped receipt is deliberately small and append-only.  Project fields can be
    /// deferred and later repaired; this receipt is the durable ordering fact which says which
    /// lifecycle observation was actually verified on the row.
    let watermarkMarker watermark =
        $"<!-- fsgg:lifecycle-watermark v=1 observedAt=%d{watermark.ObservedAt} status=%s{statusWireName watermark.Status} -->"

    let tryWatermark (comments: string list) =
        let status = function
            | "Backlog" -> Some Backlog
            | "Ready" -> Some Ready
            | "In progress" -> Some InProgress
            | "Blocked" -> Some Blocked
            | "In review" -> Some InReview
            | "Done" -> Some Done
            | _ -> None

        comments
        |> List.choose (fun body ->
            let marker = "<!-- fsgg:lifecycle-watermark v=1 observedAt="
            let start = body.IndexOf(marker, StringComparison.Ordinal)
            if start < 0 then None
            else
                let tail = body.Substring(start + marker.Length)
                let split = tail.IndexOf(" status=", StringComparison.Ordinal)
                let close = tail.IndexOf(" -->", StringComparison.Ordinal)
                if split < 1 || close < split then None
                else
                    match Int64.TryParse(tail.Substring(0, split)), status (tail.Substring(split + 8, close - split - 8)) with
                    | (true, observedAt), Some value -> Some { ObservedAt = observedAt; Status = value }
                    | _ -> None)
        |> List.sortByDescending (fun receipt -> receipt.ObservedAt)
        |> List.tryHead

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
        // Closure alone is not an instruction to erase the board's lifecycle state.  `Done` is earned
        // only by its immutable receipt; without it a scheduled pass must leave the terminal row for the
        // normal delivery path rather than projecting it back to Ready.
        elif observation.Issue.Value = Closed then
            Withheld "closed issue has no verified done receipt"
        elif observation.Delivery.Value.Outstanding then
            Project(InReview, observedAt)
        elif observation.PullRequest.Value |> Option.exists (fun pr -> pr.Open || pr.ReviewOrCiActive) then
            Project(InReview, observedAt)
        elif observation.Claim.Value |> Option.exists (fun (_, liveness) -> match liveness with LeaseHeld | LeaseExpiredPrOpen _ | LeaseExpiredBranchPushed -> true | _ -> false) then
            Project(InProgress, observedAt)
        elif observation.Claim.Value |> Option.exists (fun (_, liveness) -> match liveness with LivenessUnknown -> true | _ -> false) then
            Withheld "claim liveness could not be observed"
        else
            Project(Ready, observedAt)

    /// Accept a newly projected lifecycle result only when it is newer than the last applied receipt.
    /// Equal timestamps are idempotent only when they agree; different values at the same timestamp are
    /// withheld because the ordering source was not strong enough to decide which event won.
    let advance watermark observation =
        match project observation with
        | Withheld reason -> Withheld reason
        | Project(status, observedAt) ->
            match watermark with
            | Some previous when observedAt < previous.ObservedAt ->
                Withheld "lifecycle observation predates the persisted projection watermark"
            | Some previous when observedAt = previous.ObservedAt && status <> previous.Status ->
                Withheld "lifecycle observation conflicts with the persisted projection watermark"
            | _ -> Project(status, observedAt)
