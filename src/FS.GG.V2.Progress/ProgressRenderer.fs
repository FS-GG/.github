namespace FS.GG.V2.Progress

open System
open System.Globalization
open System.Text.RegularExpressions

/// A presentation state. The words remain visible when emoji are unavailable.
type Status =
    | ActiveHealthy
    | Pending
    | BlockedIncompleteEvidence
    | FailedUnsafe
    | CompletedInfo
    | Unknown

type LaneModel =
    | Gpt6Astra
    | Gpt6Sol
    | Gpt6Luna
    | Gpt56Sol
    | Gpt56Terra

type Effort = Low | Medium | High | XHigh | Max | Ultra

type Reservation = General | DirectV2

type Lane = {
    Id: string
    Model: LaneModel
    Effort: Effort
    Reservation: Reservation
    State: Status
}

type LaneCounts = {
    Total: int
    Active: int
    ReservedDirectV2: int
    ActiveReservedDirectV2: int
    ByModel: (LaneModel * int) list
}

type Workstream = {
    Name: string
    State: Status
    Detail: string
    PrCount: int
    EvidenceCount: int
}

type EvidenceCounts = { Prs: int; Evidence: int }

type EvidenceLink = { Label: string; Url: string }

type NativeTurn = { TurnId: string; InputTokens: int; OutputTokens: int }

type RunnerOrigin = NativeRunnerItem | SyntheticOrUnknown

type RunnerItem = {
    Origin: RunnerOrigin
    WorkspaceId: string
    ItemId: string
    ObservedAt: DateTimeOffset
    Evidence: EvidenceLink
    NativeTurns: NativeTurn list
}

type HostReceipt = {
    WorkspaceId: string
    ItemId: string
    Applied: bool
    ObservedAt: DateTimeOffset
    Evidence: EvidenceLink
}

type QueueObservation = {
    WorkspaceId: string
    ObservedAt: DateTimeOffset
    Pending: int
    PendingUnacknowledged: int
    UnacknowledgedLossy: bool
}

type CaptureClaim =
    | NoAcceptedCaptureClaim
    | AcceptedCaptureClaim of RunnerItem * HostReceipt * QueueObservation

type Telemetry = {
    WorkspaceId: string
    Readiness: Status
    Pending: int
    PendingUnacknowledged: int
    UnacknowledgedLossy: bool
    Capture: CaptureClaim
}

type NamedState = { Name: string; State: Status; Detail: string }

type Completion = {
    CompletedAt: DateTimeOffset
    Item: string
    Workstream: string
    Result: Status
    Link: EvidenceLink option
    RecordedRoadmapHead: string
}

type ProgressSnapshot = {
    AsOf: DateTimeOffset
    RoadmapHead: string
    Lanes: Lane list
    DeclaredLaneCounts: LaneCounts
    Workstreams: Workstream list
    DeclaredEvidenceCounts: EvidenceCounts
    Telemetry: Telemetry
    ProtectedHolds: string list
    Checks: NamedState list
    Risks: NamedState list
    NextActions: string list
    CompletionHistory: Completion list
}

type DerivedProgress = {
    Snapshot: ProgressSnapshot
    LaneCounts: LaneCounts
    EvidenceCounts: EvidenceCounts
    RecentCompletions: Completion list
}

module ProgressRenderer =
    let statusText = function
        | ActiveHealthy -> "🟢 Active/Healthy"
        | Pending -> "🟡 Pending"
        | BlockedIncompleteEvidence -> "🟠 Blocked/Incomplete evidence"
        | FailedUnsafe -> "🔴 Failed/Unsafe"
        | CompletedInfo -> "🔵 Completed/Info"
        | Unknown -> "🔘 Unknown"

    let private modelText = function
        | Gpt6Astra -> "gpt-6-astra"
        | Gpt6Sol -> "gpt-6-sol"
        | Gpt6Luna -> "gpt-6-luna"
        | Gpt56Sol -> "gpt-5.6-sol"
        | Gpt56Terra -> "gpt-5.6-terra"

    let private effortText = function
        | Low -> "low"
        | Medium -> "medium"
        | High -> "high"
        | XHigh -> "xhigh"
        | Max -> "max"
        | Ultra -> "ultra"

    let private reservationText = function
        | General -> "General"
        | DirectV2 -> "Reserved direct V2"

    let private utc (value: DateTimeOffset) = value.Offset = TimeSpan.Zero
    let private timeText (value: DateTimeOffset) =
        value.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)

    let private sha40 (value: string) =
        not (isNull value) && Regex.IsMatch(value, "\\A[0-9a-f]{40}\\z", RegexOptions.CultureInvariant)

    let private nonblank (value: string) = not (String.IsNullOrWhiteSpace value)
    let private ordinal = StringComparer.Ordinal

    let private linkValid (link: EvidenceLink) =
        let mutable uri = Unchecked.defaultof<Uri>
        nonblank link.Label
        && Uri.TryCreate(link.Url, UriKind.Absolute, &uri)
        && uri.Scheme = Uri.UriSchemeHttps
        && not (String.IsNullOrEmpty uri.Host)
        && String.IsNullOrEmpty uri.UserInfo

    let private completionCompare (left: Completion) (right: Completion) =
        let first = compare right.CompletedAt left.CompletedAt
        if first <> 0 then first else
        let second = ordinal.Compare(left.Item, right.Item)
        if second <> 0 then second else
        let third = ordinal.Compare(left.Workstream, right.Workstream)
        if third <> 0 then third else
        let fourth = ordinal.Compare(left.RecordedRoadmapHead, right.RecordedRoadmapHead)
        if fourth <> 0 then fourth else
        let fifth = compare left.Result right.Result
        if fifth <> 0 then fifth else
        let sixth = ordinal.Compare(left.Link |> Option.map _.Url |> Option.defaultValue "",
                                    right.Link |> Option.map _.Url |> Option.defaultValue "")
        if sixth <> 0 then sixth else
        ordinal.Compare(left.Link |> Option.map _.Label |> Option.defaultValue "",
                        right.Link |> Option.map _.Label |> Option.defaultValue "")

    /// Validates caller-supplied facts and derives only counts and display order.
    /// The collector must verify provenance of runner items and Host receipts before constructing a claim.
    let derive (snapshot: ProgressSnapshot) : Result<DerivedProgress, string list> =
        let errors = ResizeArray<string>()
        let require condition message = if not condition then errors.Add message
        let requireText label value = require (nonblank value) (label + " must be nonblank")
        let requireUtc label value = require (utc value) (label + " must be UTC")
        let requireLink label value = require (linkValid value) (label + " must be an HTTPS evidence link")

        requireUtc "as-of time" snapshot.AsOf
        require (sha40 snapshot.RoadmapHead) "roadmap head must be a lowercase 40-hex commit"
        let laneIds = snapshot.Lanes |> List.map _.Id
        require (laneIds.Length = (laneIds |> List.distinct).Length) "lane IDs must be unique"
        for lane in snapshot.Lanes do
            requireText "lane ID" lane.Id
        let lanes = snapshot.Lanes |> List.sortWith (fun a b -> ordinal.Compare(a.Id, b.Id))
        let byModel =
            lanes
            |> List.countBy _.Model
            |> List.sortWith (fun (a, _) (b, _) -> ordinal.Compare(modelText a, modelText b))
        let laneCounts = {
            Total = lanes.Length
            Active = lanes |> List.filter (fun x -> x.State = ActiveHealthy) |> List.length
            ReservedDirectV2 = lanes |> List.filter (fun x -> x.Reservation = DirectV2) |> List.length
            ActiveReservedDirectV2 =
                lanes |> List.filter (fun x -> x.Reservation = DirectV2 && x.State = ActiveHealthy) |> List.length
            ByModel = byModel
        }
        require (snapshot.DeclaredLaneCounts.Total = laneCounts.Total) "declared total lane count disagrees with lanes"
        require (snapshot.DeclaredLaneCounts.Active = laneCounts.Active) "declared active lane count disagrees with lanes"
        require (snapshot.DeclaredLaneCounts.ReservedDirectV2 = laneCounts.ReservedDirectV2)
            "declared direct V2 reservation count disagrees with lanes"
        require (snapshot.DeclaredLaneCounts.ActiveReservedDirectV2 = laneCounts.ActiveReservedDirectV2)
            "declared active direct V2 reservation count disagrees with lanes"
        require (snapshot.DeclaredLaneCounts.ByModel = laneCounts.ByModel)
            "declared model counts disagree with lanes"

        let workstreamNames = snapshot.Workstreams |> List.map _.Name
        require (workstreamNames.Length = (workstreamNames |> List.distinct).Length) "workstream names must be unique"
        for workstream in snapshot.Workstreams do
            requireText "workstream name" workstream.Name
            requireText "workstream detail" workstream.Detail
            require (workstream.PrCount >= 0 && workstream.EvidenceCount >= 0)
                "workstream counts must be nonnegative"
        let prs = snapshot.Workstreams |> List.sumBy (fun x -> int64 x.PrCount)
        let evidence = snapshot.Workstreams |> List.sumBy (fun x -> int64 x.EvidenceCount)
        require (prs <= int64 Int32.MaxValue && evidence <= int64 Int32.MaxValue)
            "PR/evidence totals must fit a 32-bit count"
        let counts = { Prs = int prs; Evidence = int evidence }
        require (snapshot.DeclaredEvidenceCounts = counts) "declared PR/evidence counts disagree with workstreams"

        let telemetry = snapshot.Telemetry
        requireText "telemetry workspace" telemetry.WorkspaceId
        require (not (isNull telemetry.WorkspaceId)
                 && Regex.IsMatch(telemetry.WorkspaceId, "\\A[A-Za-z0-9._-]+\\z", RegexOptions.CultureInvariant))
            "telemetry workspace must be a plain identifier"
        require (telemetry.Pending >= 0 && telemetry.PendingUnacknowledged >= 0)
            "telemetry queue counts must be nonnegative"
        if telemetry.Readiness = ActiveHealthy then
            require (telemetry.Pending = 0 && telemetry.PendingUnacknowledged = 0
                     && not telemetry.UnacknowledgedLossy)
                "healthy telemetry readiness conflicts with queue status"
        match telemetry.Capture with
        | NoAcceptedCaptureClaim -> ()
        | AcceptedCaptureClaim (runner, host, later) ->
            require (runner.Origin = NativeRunnerItem) "capture requires a genuine native runner item"
            requireText "runner workspace" runner.WorkspaceId
            requireText "runner item" runner.ItemId
            require (runner.WorkspaceId = telemetry.WorkspaceId) "runner workspace does not match telemetry workspace"
            requireUtc "runner observation" runner.ObservedAt
            requireLink "runner item" runner.Evidence
            require (not runner.NativeTurns.IsEmpty) "capture requires native turn IDs and usage"
            let turnIds = runner.NativeTurns |> List.map _.TurnId
            require (turnIds.Length = (turnIds |> List.distinct).Length) "native turn IDs must be unique"
            for turn in runner.NativeTurns do
                requireText "native turn ID" turn.TurnId
                require (turn.InputTokens >= 0 && turn.OutputTokens >= 0
                         && turn.InputTokens + turn.OutputTokens > 0)
                    "native turn usage must be present and positive"
            require (host.Applied) "capture requires an applied Host receipt"
            require (host.WorkspaceId = runner.WorkspaceId && host.ItemId = runner.ItemId)
                "Host receipt does not match exact runner workspace/item"
            requireUtc "Host receipt observation" host.ObservedAt
            requireLink "Host receipt" host.Evidence
            require (host.ObservedAt >= runner.ObservedAt) "Host receipt precedes runner item"
            require (later.WorkspaceId = runner.WorkspaceId) "later queue workspace does not match runner"
            requireUtc "later queue observation" later.ObservedAt
            require (later.ObservedAt > host.ObservedAt) "zero queue observation must be later than Host receipt"
            require (later.Pending = 0 && later.PendingUnacknowledged = 0
                     && not later.UnacknowledgedLossy) "capture requires a later zero, lossless queue"
            require (later.ObservedAt <= snapshot.AsOf) "later queue observation exceeds report time"

        for hold in snapshot.ProtectedHolds do requireText "protected hold" hold
        for check in snapshot.Checks do
            requireText "check name" check.Name
            requireText "check detail" check.Detail
        require (snapshot.Checks |> List.map _.Name |> List.distinct |> List.length = snapshot.Checks.Length)
            "check names must be unique"
        for risk in snapshot.Risks do
            requireText "risk name" risk.Name
            requireText "risk detail" risk.Detail
        require (snapshot.Risks |> List.map _.Name |> List.distinct |> List.length = snapshot.Risks.Length)
            "risk names must be unique"
        for action in snapshot.NextActions do requireText "next action" action
        for completion in snapshot.CompletionHistory do
            requireUtc "completion time" completion.CompletedAt
            require (completion.CompletedAt <= snapshot.AsOf) "completion time exceeds report time"
            requireText "completion item" completion.Item
            requireText "completion workstream" completion.Workstream
            require (sha40 completion.RecordedRoadmapHead) "completion roadmap head must be lowercase 40-hex"
            completion.Link |> Option.iter (requireLink "completion")
        let recent = snapshot.CompletionHistory |> List.sortWith completionCompare |> List.truncate 5
        if errors.Count > 0 then Error(errors |> Seq.distinct |> Seq.sort |> Seq.toList)
        else Ok { Snapshot = snapshot; LaneCounts = laneCounts; EvidenceCounts = counts; RecentCompletions = recent }

    let private escape (value: string) =
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
             .Replace("\\", "\\\\").Replace("|", "\\|").Replace("[", "\\[").Replace("]", "\\]")
             .Replace("`", "\\`").Replace("\r", " ").Replace("\n", " ")

    let private renderLink (link: EvidenceLink) =
        let uri = Uri(link.Url, UriKind.Absolute)
        $"[{escape link.Label}](<{uri.AbsoluteUri}>)"

    let private rowsOrNone rows = if List.isEmpty rows then [ "_None supplied._" ] else rows

    /// Pure Markdown projection. No collection, file writes, or authority updates occur here.
    let private render (progress: DerivedProgress) : string =
        let snapshot = progress.Snapshot
        let lanes = snapshot.Lanes |> List.sortWith (fun a b -> ordinal.Compare(a.Id, b.Id))
        let workstreams = snapshot.Workstreams |> List.sortWith (fun a b -> ordinal.Compare(a.Name, b.Name))
        let namedRows values =
            values
            |> List.sortWith (fun a b -> ordinal.Compare(a.Name, b.Name))
            |> List.map (fun x -> $"| {escape x.Name} | {statusText x.State} | {escape x.Detail} |")
        let laneRows =
            lanes |> List.map (fun lane ->
                $"| {escape lane.Id} | {modelText lane.Model} | {effortText lane.Effort} | {reservationText lane.Reservation} | {statusText lane.State} |")
        let modelSummary =
            progress.LaneCounts.ByModel
            |> List.map (fun (model, count) -> $"{modelText model}: {count}")
            |> String.concat ", "
        let workstreamRows =
            workstreams |> List.map (fun row ->
                $"| {escape row.Name} | {statusText row.State} | {row.PrCount} | {row.EvidenceCount} | {escape row.Detail} |")
        let completionRows =
            progress.RecentCompletions |> List.map (fun row ->
                let link = row.Link |> Option.map renderLink |> Option.defaultValue "—"
                $"| {timeText row.CompletedAt} | {escape row.Item} / {escape row.Workstream} | {statusText row.Result} | {link} | `{row.RecordedRoadmapHead}` |")
        let captureText =
            match snapshot.Telemetry.Capture with
            | NoAcceptedCaptureClaim -> "🟡 Pending — no end-to-end capture acceptance claimed"
            | AcceptedCaptureClaim _ -> "🔵 Completed/Info — end-to-end capture accepted from supplied evidence"
        [
            "# V2 progress update"
            ""
            $"- Snapshot: {timeText snapshot.AsOf}"
            $"- Recorded roadmap head: `{snapshot.RoadmapHead}`"
            ""
            "## Lanes"
            ""
            "| Lane | Model | Effort | Reservation | State |"
            "| --- | --- | --- | --- | --- |"
            yield! laneRows
            ""
            $"Total: {progress.LaneCounts.Total}; active: {progress.LaneCounts.Active}; reserved direct V2: {progress.LaneCounts.ReservedDirectV2}; active reserved direct V2: {progress.LaneCounts.ActiveReservedDirectV2}."
            $"Models: {modelSummary}."
            ""
            "## Workstreams"
            ""
            "| Workstream | State | PRs | Evidence | Detail |"
            "| --- | --- | ---: | ---: | --- |"
            yield! workstreamRows
            ""
            $"PRs: {progress.EvidenceCounts.Prs}; evidence links: {progress.EvidenceCounts.Evidence}."
            ""
            "## Telemetry and capture"
            ""
            $"- Telemetry readiness: {statusText snapshot.Telemetry.Readiness}"
            $"- Workspace: `{escape snapshot.Telemetry.WorkspaceId}`"
            $"- Current queue: pending={snapshot.Telemetry.Pending}, pending unacknowledged={snapshot.Telemetry.PendingUnacknowledged}, unacknowledged lossy={string snapshot.Telemetry.UnacknowledgedLossy |> _.ToLowerInvariant()}"
            $"- End-to-end capture acceptance: {captureText}"
            ""
            "## Protected holds"
            ""
            yield! snapshot.ProtectedHolds |> List.sortWith (fun a b -> ordinal.Compare(a, b)) |> List.map (fun hold -> $"- 🟠 Blocked/Incomplete evidence — {escape hold}") |> rowsOrNone
            ""
            "## Checks"
            ""
            "| Check | State | Detail |"
            "| --- | --- | --- |"
            yield! namedRows snapshot.Checks |> rowsOrNone
            ""
            "## Risks"
            ""
            "| Risk | State | Detail |"
            "| --- | --- | --- |"
            yield! namedRows snapshot.Risks |> rowsOrNone
            ""
            "## Next actions"
            ""
            yield! snapshot.NextActions |> List.map (fun action -> "- " + escape action) |> rowsOrNone
            ""
            "## Last 5 completions"
            ""
            "| Time (UTC) | Item / workstream | Result | PR / evidence | Recorded roadmap head |"
            "| --- | --- | --- | --- | --- |"
            yield! completionRows
        ] |> String.concat "\n" |> fun value -> value + "\n"

    let renderSnapshot snapshot = derive snapshot |> Result.map render
