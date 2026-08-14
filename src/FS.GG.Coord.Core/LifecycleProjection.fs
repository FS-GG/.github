namespace FS.GG.Coord

open System
open System.Text.RegularExpressions
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

    type IntentRecord =
        { Revision: int64
          Reason: string }

    type SchedulingIntent =
        | Auto
        | Backlog of IntentRecord
        | HumanPark of HumanBlock * IntentRecord
        | Deferred of reason: string * until: int64 option * revision: int64

    type PolicyVersion =
        | IntentStatusV1

    type ProjectionMode =
        | Legacy
        | Intent

    type Result =
        | Project of status: BoardStatus * observedAt: int64
        | Withheld of reason: string

    type Difference =
        | Same
        | DeliberateParkPreserved of legacy: BoardStatus * intended: BoardStatus
        | Unexpected of legacy: Result * intended: Result

    type Shadow =
        { Legacy: Result
          Intended: Result
          Difference: Difference }

    /// The durable portion of a projection receipt.  Callers persist this beside the status write and
    /// feed it back on the next event.  Keeping the water-mark in the typed boundary makes an event that
    /// arrived late a no-op rather than an opportunity to re-derive an older column value.
    type Watermark = { ObservedAt: int64; Status: BoardStatus }

    /// The comment-shaped receipt is deliberately small and append-only.  Project fields can be
    /// deferred and later repaired; this receipt is the durable ordering fact which says which
    /// lifecycle observation was actually verified on the row.
    let watermarkMarker watermark =
        $"<!-- fsgg:lifecycle-watermark v=1 observedAt=%d{watermark.ObservedAt} status=%s{statusWireName watermark.Status} -->"

    // ANCHORED, NOT SUBSTRING (round-1 review repair, .github#2264 PR #2271). `body.IndexOf(marker)`
    // found the sentinel wherever it sat in a comment — including a documentation-style comment that
    // merely QUOTES an illustrative marker in prose. A quoted marker carrying a large `observedAt` then
    // silently outranked the real persisted watermark under the `List.sortByDescending` below, corrupting
    // AC-4's guarantee that an older event can never overwrite a newer observed state. This is the same
    // class of defect `.github#2221` fixed for review markers: a marker is evidence only when it is the
    // comment's ENTIRE (trimmed) body, matching `DeliveryApplication.obligationsFromComments`'s own
    // anchored, whole-line semantics exactly (`^<!-- ... -->$` against `Trim()`). It cannot be reused
    // directly — it lives in FS.GG.Coord.Cli, which depends on this project, not the other way around,
    // and its grammar is the unrelated `id=/kind=/head=` obligation shape rather than this marker's
    // `observedAt=/status=` — so this is a parallel implementation of that one rule, not a fork of its
    // logic. `Writes.lifecycleWatermark` posts nothing but this bare line (`watermarkMarker` above), so a
    // genuine receipt always satisfies a whole-body match; only a quotation can fail it, and a quotation
    // is exactly what must fail it.
    let private watermarkLine =
        Regex(
            @"^<!-- fsgg:lifecycle-watermark v=1 observedAt=(?<observedAt>-?[0-9]+) status=(?<status>[A-Za-z ]+) -->$",
            RegexOptions.Compiled
        )

    let tryWatermark (comments: string list) =
        let status = function
            | "Backlog" -> Some BoardStatus.Backlog
            | "Ready" -> Some BoardStatus.Ready
            | "In progress" -> Some BoardStatus.InProgress
            | "Blocked" -> Some BoardStatus.Blocked
            | "In review" -> Some BoardStatus.InReview
            | "Done" -> Some BoardStatus.Done
            | _ -> None

        comments
        |> List.choose (fun body ->
            let matched = watermarkLine.Match(body.Trim())
            if not matched.Success then None
            else
                match Int64.TryParse(matched.Groups.["observedAt"].Value), status matched.Groups.["status"].Value with
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

    let private projectWithIntent intent observation =
        let observedAt = latest observation
        if not (coherent observation observedAt) then
            Withheld "lifecycle facts have different observation timestamps"
        elif observation.Delivery.Value.DoneStamped && observation.Issue.Value = Closed then
            Project(BoardStatus.Done, observedAt)
        // Closure alone is not an instruction to erase the board's lifecycle state.  `Done` is earned
        // only by its immutable receipt; without it a scheduled pass must leave the terminal row for the
        // normal delivery path rather than projecting it back to Ready.
        elif observation.Issue.Value = Closed then
            Withheld "closed issue has no verified done receipt"
        // HumanPark is scheduling intent, not a blocker inferred from mutable observations.  It survives
        // active-looking facts until its own revision is changed: a worker/PR cannot silently answer the
        // human question which parked the item.  A real blocker naturally projects the same column.
        elif (match intent with HumanPark _ -> true | _ -> false) then
            Project(BoardStatus.Blocked, observedAt)
        elif observation.Blockers.Value |> List.exists (fun blocker -> blocker.State <> BlockerClosed && blocker.State <> BlockerMerged) then
            Project(BoardStatus.Blocked, observedAt)
        elif observation.Delivery.Value.Outstanding then
            Project(BoardStatus.InReview, observedAt)
        elif observation.PullRequest.Value |> Option.exists (fun pr -> pr.Open || pr.ReviewOrCiActive) then
            Project(BoardStatus.InReview, observedAt)
        elif observation.Claim.Value |> Option.exists (fun (_, liveness) -> match liveness with LeaseHeld | LeaseExpiredPrOpen _ | LeaseExpiredBranchPushed -> true | _ -> false) then
            Project(BoardStatus.InProgress, observedAt)
        elif observation.Claim.Value |> Option.exists (fun (_, liveness) -> match liveness with LivenessUnknown -> true | _ -> false) then
            Withheld "claim liveness could not be observed"
        else
            match intent with
            | Auto -> Project(BoardStatus.Ready, observedAt)
            | Backlog _
            | Deferred _ -> Project(BoardStatus.Backlog, observedAt)
            | HumanPark _ -> Project(BoardStatus.Blocked, observedAt) // handled above; exhaustive and stable

    let project observation = projectWithIntent Auto observation

    let reduce policy intent observation =
        match policy with
        | IntentStatusV1 -> projectWithIntent intent observation

    let migrateIntent revision status humanBlock =
        match humanBlock with
        | Some human ->
            HumanPark(
                human,
                { Revision = revision
                  Reason = "migrated from the explicit human-park sentinel" }
            )
        | None when status = BoardStatus.Backlog ->
            Backlog
                { Revision = revision
                  Reason = "migrated from the deliberate Backlog projection" }
        | None -> Auto

    let private classify intent legacy intended =
        if legacy = intended then Same
        else
            match intent, legacy, intended with
            | Backlog _, Project(BoardStatus.Ready, _), Project(BoardStatus.Backlog, _) ->
                DeliberateParkPreserved(BoardStatus.Ready, BoardStatus.Backlog)
            | Deferred _, Project(BoardStatus.Ready, _), Project(BoardStatus.Backlog, _) ->
                DeliberateParkPreserved(BoardStatus.Ready, BoardStatus.Backlog)
            | HumanPark _, Project(oldStatus, _), Project(BoardStatus.Blocked, _) ->
                DeliberateParkPreserved(oldStatus, BoardStatus.Blocked)
            | _ -> Unexpected(legacy, intended)

    let shadow policy intent observation =
        let legacy = project observation
        let intended = reduce policy intent observation
        { Legacy = legacy
          Intended = intended
          Difference = classify intent legacy intended }

    let projectionMode raw =
        match raw |> Option.map (fun (value: string) -> value.Trim().ToLowerInvariant()) with
        | None
        | Some ""
        | Some "intent"
        | Some "intent-v1" -> Ok Intent
        | Some "legacy" -> Ok Legacy
        | Some value -> Error $"unknown lifecycle projection mode '%s{value}' (expected intent-v1 or legacy)"

    let select mode shadow =
        match mode with
        | Legacy -> shadow.Legacy
        | Intent -> shadow.Intended

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

    let private advanceResult watermark result =
        match result with
        | Withheld reason -> Withheld reason
        | Project(status, observedAt) ->
            match watermark with
            | Some previous when observedAt < previous.ObservedAt ->
                Withheld "lifecycle observation predates the persisted projection watermark"
            | Some previous when observedAt = previous.ObservedAt && status <> previous.Status ->
                Withheld "lifecycle observation conflicts with the persisted projection watermark"
            | _ -> result

    let shadowAdvance policy intent watermark observation =
        let projections = shadow policy intent observation
        let legacy = advanceResult watermark projections.Legacy
        let intended = advanceResult watermark projections.Intended
        { Legacy = legacy
          Intended = intended
          Difference = classify intent legacy intended }
