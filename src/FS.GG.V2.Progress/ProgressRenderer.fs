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

type LaneRole = Orchestrator | Worker
type LaneActivity = Running | Idle | Finished
type LaunchSource = ExplicitUserInstruction | ExplicitOrchestratorSpawn | RuntimeSelfIntrospection

type LaunchEvidence = {
    Model: LaneModel
    Effort: Effort
    Source: LaunchSource
    EvidenceId: string
}

type Lane = {
    Id: string
    Role: LaneRole
    Activity: LaneActivity
    Model: LaneModel
    Effort: Effort
    Reservation: Reservation
    State: Status
    Launch: LaunchEvidence option
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
    Authenticated: bool
    CollectorVerified: bool
    EvidenceId: string
    Pending: int
    PendingUnacknowledged: int
    UnacknowledgedLossy: bool
}

type CaptureClaim =
    | NoAcceptedCaptureClaim
    | AcceptedCaptureClaim of RunnerItem * HostReceipt * QueueObservation

type AuthenticatedHealthObservation = {
    Authenticated: bool
    Ready: bool
    CollectorVerified: bool
    ObservedAt: DateTimeOffset
    EvidenceId: string
}

type ConfiguredWorkspaceObservation = {
    Configured: bool
    CollectorVerified: bool
    WorkspaceId: string
    Pending: int
    PendingUnacknowledged: int
    UnacknowledgedLossy: bool
    ObservedAt: DateTimeOffset
    EvidenceId: string
}

type Telemetry = {
    WorkspaceId: string
    Readiness: Status
    HealthObservation: AuthenticatedHealthObservation option
    WorkspaceObservation: ConfiguredWorkspaceObservation option
    Pending: int
    PendingUnacknowledged: int
    UnacknowledgedLossy: bool
    Capture: CaptureClaim
}

/// A collector-verified observation of the Codex CLI /status display.
/// Context occupancy is a window size, not cumulative or period token usage.
type CliStatusObservation = {
    Authenticated: bool
    CollectorVerified: bool
    EvidenceId: string
    ObservedAt: DateTimeOffset
    WeeklyRemainingPercent: int
    WeeklyResetLocal: DateTimeOffset
    WeeklyResetTimeZone: string
    ContextUsedTokens: int64
    ContextCapacityTokens: int64
}

type NativePeriodUsage = {
    WindowStart: DateTimeOffset
    WindowEnd: DateTimeOffset
    Runner: RunnerItem
    CollectorVerified: bool
}

/// Native JSONL token_count totals are cumulative within one session.
/// Cached input is a subset of input; total equals input plus output.
type NativeTokenCounters = {
    InputTokens: int64
    CachedInputTokens: int64
    OutputTokens: int64
    TotalTokens: int64
}

type NativePrimaryRate = {
    AccountScopeId: string
    LimitId: string
    WindowMinutes: int
    UsedPercent: decimal
    ResetsAt: DateTimeOffset
}

type NativeTokenCountEvent = {
    ObservedAt: DateTimeOffset
    Ordinal: int64
    Counters: NativeTokenCounters
    PrimaryRate: NativePrimaryRate option
}

type NativeSessionCounters = {
    SessionId: string
    ParentSessionId: string option
    StartedAt: DateTimeOffset
    CompleteThrough: DateTimeOffset
    HistoryComplete: bool
    CollectorVerified: bool
    EvidenceId: string
    Events: NativeTokenCountEvent list
}

type TeamCounterWindow = {
    RootSessionId: string
    WindowStart: DateTimeOffset
    WindowEnd: DateTimeOffset
    DeclaredSessionCount: int
    Sessions: NativeSessionCounters list
}

type PeriodUsage =
    | UnknownPeriodUsage
    | NativeTurnPeriodUsage of NativePeriodUsage
    | NativeCounterPeriodUsage of TeamCounterWindow
    | LocalCounterDiagnostic of TeamCounterWindow

type CounterTotals = {
    Input: decimal
    CachedInput: decimal
    Output: decimal
    Total: decimal
}

type NativeRatePoint = {
    ObservedAt: DateTimeOffset
    Ordinal: int64
    SessionId: string
    EvidenceId: string
    Rate: NativePrimaryRate
}

type ExhaustionEstimate =
    | NoRateSlope
    | EstimatedAt of DateTimeOffset
    | NotBeforeReset
    | AlreadyAtLimit

type TeamCounterSummary = {
    RootStartedAt: DateTimeOffset
    WindowStart: DateTimeOffset
    WindowEnd: DateTimeOffset
    CompletedPeriodCount: int64
    SessionCount: int
    LatestPeriodDelta: CounterTotals
    AllPeriodsTotal: CounterTotals
    AllPeriodMean: CounterTotals
    LatestRate: NativeRatePoint option
    Exhaustion: ExhaustionEstimate
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
    CliStatus: CliStatusObservation option
    PeriodUsage: PeriodUsage
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
    CounterSummary: TeamCounterSummary option
}

module ProgressRenderer =
    let private maxTelemetryObservationAge = TimeSpan.FromMinutes 5.0

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

    let private roleText = function Orchestrator -> "Orchestrator" | Worker -> "Worker"
    let private activityText = function Running -> "Running" | Idle -> "Idle" | Finished -> "Finished"
    let private launchSourceText = function
        | ExplicitUserInstruction -> "Explicit user instruction"
        | ExplicitOrchestratorSpawn -> "Explicit orchestrator spawn"
        | RuntimeSelfIntrospection -> "Runtime self-introspection"

    let private utc (value: DateTimeOffset) = value.Offset = TimeSpan.Zero
    let private timeText (value: DateTimeOffset) =
        value.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)
    let private decimalText (value: decimal) = value.ToString("0.00", CultureInfo.InvariantCulture)
    let private integerText (value: decimal) = value.ToString("0", CultureInfo.InvariantCulture)

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

    let private zeroCounters =
        { InputTokens = 0L; CachedInputTokens = 0L; OutputTokens = 0L; TotalTokens = 0L }

    let private counterDelta after before =
        { Input = decimal after.InputTokens - decimal before.InputTokens
          CachedInput = decimal after.CachedInputTokens - decimal before.CachedInputTokens
          Output = decimal after.OutputTokens - decimal before.OutputTokens
          Total = decimal after.TotalTokens - decimal before.TotalTokens }

    let private sumTotals left right =
        { Input = left.Input + right.Input
          CachedInput = left.CachedInput + right.CachedInput
          Output = left.Output + right.Output
          Total = left.Total + right.Total }

    let private zeroTotals = { Input = 0M; CachedInput = 0M; Output = 0M; Total = 0M }

    let private deriveCounterWindow (asOf: DateTimeOffset) (localDiagnostic: bool) (window: TeamCounterWindow)
        : Result<TeamCounterSummary, string list> =
        let errors = ResizeArray<string>()
        let require condition message = if not condition then errors.Add message
        let period = TimeSpan.FromMinutes 10.0
        require (utc window.WindowStart && utc window.WindowEnd
                 && window.WindowEnd - window.WindowStart = period
                 && window.WindowEnd <= asOf && asOf - window.WindowEnd < period)
            "native counter period must be the latest completed ten-minute UTC window"
        require (nonblank window.RootSessionId) "native counter root session must be supplied"
        require (window.DeclaredSessionCount > 0 && window.DeclaredSessionCount = window.Sessions.Length)
            "declared native session count disagrees with supplied sessions"
        let ids = window.Sessions |> List.map _.SessionId
        require (ids.Length = (ids |> List.distinct).Length) "native session IDs must be unique"
        require (window.Sessions |> List.exists (fun session ->
            session.SessionId = window.RootSessionId && session.ParentSessionId.IsNone))
            "native counter root must have no parent"
        let rootStartedAt =
            window.Sessions |> List.tryFind (fun session -> session.SessionId = window.RootSessionId)
            |> Option.map _.StartedAt |> Option.defaultValue window.WindowStart
        let elapsed = window.WindowEnd - rootStartedAt
        require (utc rootStartedAt && elapsed.Ticks >= period.Ticks
                 && elapsed.Ticks % period.Ticks = 0L
                 && window.WindowStart = window.WindowEnd - period)
            "native completed periods must be anchored at root session start"
        let mutable reachable = Set.singleton window.RootSessionId
        for _ in 1 .. window.Sessions.Length do
            for session in window.Sessions do
                if session.ParentSessionId |> Option.exists reachable.Contains then
                    reachable <- reachable.Add session.SessionId
        require (reachable.Count = window.Sessions.Length)
            "native session lineage must close under the selected root"
        for session in window.Sessions do
            require (nonblank session.SessionId && nonblank session.EvidenceId
                     && session.CollectorVerified <> localDiagnostic && session.HistoryComplete)
                (if localDiagnostic then
                     "local diagnostic requires explicitly unverified complete-history provenance"
                 else "native session requires collector-verified complete-history provenance")
            require (utc session.StartedAt && utc session.CompleteThrough
                     && session.StartedAt >= rootStartedAt
                     && session.StartedAt <= asOf
                     && session.CompleteThrough = asOf)
                "native session history must cover the report window"
            for event in session.Events do
                require (utc event.ObservedAt && event.ObservedAt >= session.StartedAt
                         && event.ObservedAt <= session.CompleteThrough && event.Ordinal >= 0L)
                    "native token_count event time or ordinal is invalid"
                let c = event.Counters
                require (c.InputTokens >= 0L && c.CachedInputTokens >= 0L
                         && c.OutputTokens >= 0L && c.TotalTokens >= 0L
                         && c.CachedInputTokens <= c.InputTokens
                         && decimal c.InputTokens + decimal c.OutputTokens = decimal c.TotalTokens)
                    "native token_count components disagree"
                event.PrimaryRate |> Option.iter (fun rate ->
                    require (nonblank rate.AccountScopeId && nonblank rate.LimitId
                             && rate.WindowMinutes = 10080
                             && rate.UsedPercent >= 0M && rate.UsedPercent <= 100M
                             && utc rate.ResetsAt && rate.ResetsAt > event.ObservedAt)
                        "native primary weekly rate limit is invalid")
            for previous, current in session.Events |> List.pairwise do
                require (previous.Ordinal < current.Ordinal
                         && previous.ObservedAt <= current.ObservedAt)
                    "native token_count events must follow ordinal and time order"
                let a, b = previous.Counters, current.Counters
                require (a.InputTokens <= b.InputTokens
                         && a.CachedInputTokens <= b.CachedInputTokens
                         && a.OutputTokens <= b.OutputTokens
                         && a.TotalTokens <= b.TotalTokens)
                    "native cumulative counters decreased"
                require (decimal b.CachedInputTokens - decimal a.CachedInputTokens
                         <= decimal b.InputTokens - decimal a.InputTokens)
                    "native cached input delta exceeds input delta"
        if errors.Count > 0 then Error(errors |> Seq.distinct |> Seq.sort |> Seq.toList)
        else
            let lastAt cutoff (events: NativeTokenCountEvent list) =
                events |> List.filter (fun event -> event.ObservedAt <= cutoff)
                       |> List.tryLast |> Option.map _.Counters |> Option.defaultValue zeroCounters
            let latestDelta =
                window.Sessions
                |> List.map (fun session ->
                    counterDelta (lastAt window.WindowEnd session.Events)
                                 (lastAt window.WindowStart session.Events))
                |> List.fold sumTotals zeroTotals
            let allTotal =
                window.Sessions
                |> List.map (fun session -> counterDelta (lastAt window.WindowEnd session.Events) zeroCounters)
                |> List.fold sumTotals zeroTotals
            let periodCount = elapsed.Ticks / period.Ticks
            let count = decimal periodCount
            let mean =
                { Input = allTotal.Input / count; CachedInput = allTotal.CachedInput / count
                  Output = allTotal.Output / count; Total = allTotal.Total / count }
            let ratePoints =
                window.Sessions
                |> List.collect (fun session ->
                    session.Events |> List.choose (fun event ->
                        event.PrimaryRate |> Option.map (fun rate ->
                            { ObservedAt = event.ObservedAt; Ordinal = event.Ordinal
                              SessionId = session.SessionId; EvidenceId = session.EvidenceId
                              Rate = rate })))
                |> List.sortWith (fun a b ->
                    let byTime = compare a.ObservedAt b.ObservedAt
                    if byTime <> 0 then byTime else
                    let bySession = ordinal.Compare(a.SessionId, b.SessionId)
                    if bySession <> 0 then bySession else compare a.Ordinal b.Ordinal)
            let latest =
                ratePoints
                |> List.filter (fun point ->
                    point.ObservedAt >= asOf - period && point.ObservedAt <= asOf)
                |> List.tryLast
            let exhaustion =
                match latest with
                | None -> NoRateSlope
                | Some current when current.Rate.UsedPercent >= 100M -> AlreadyAtLimit
                | Some current ->
                    let earliest =
                        ratePoints
                        |> List.filter (fun point ->
                            point.Rate.AccountScopeId = current.Rate.AccountScopeId
                            && point.Rate.LimitId = current.Rate.LimitId
                            && point.Rate.ResetsAt = current.Rate.ResetsAt
                            && point.ObservedAt <= current.ObservedAt - TimeSpan.FromMinutes 10.0)
                        |> List.tryHead
                    match earliest with
                    | None -> NoRateSlope
                    | Some before ->
                        let rise = current.Rate.UsedPercent - before.Rate.UsedPercent
                        if rise <= 0M then NoRateSlope else
                        let observedTicks = decimal (current.ObservedAt - before.ObservedAt).Ticks
                        let remainingTicks =
                            Decimal.Truncate((100M - current.Rate.UsedPercent) * observedTicks / rise)
                        let untilReset = decimal (current.Rate.ResetsAt - current.ObservedAt).Ticks
                        if remainingTicks > untilReset then NotBeforeReset
                        else EstimatedAt(current.ObservedAt.AddTicks(int64 remainingTicks))
            Ok { RootStartedAt = rootStartedAt
                 WindowStart = window.WindowStart; WindowEnd = window.WindowEnd
                 CompletedPeriodCount = periodCount; SessionCount = window.Sessions.Length
                 LatestPeriodDelta = latestDelta; AllPeriodsTotal = allTotal
                 AllPeriodMean = mean; LatestRate = latest; Exhaustion = exhaustion }

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
        let orchestrators = snapshot.Lanes |> List.filter (fun lane -> lane.Role = Orchestrator)
        require (orchestrators.Length = 1 && orchestrators.Head.Activity = Running)
            "active progress snapshot requires exactly one orchestrator"
        for lane in snapshot.Lanes do
            requireText "lane ID" lane.Id
            if lane.Reservation = DirectV2 then
                require (lane.Role = Worker) "direct V2 reservation requires a worker role"
            match lane.Launch with
            | Some launch ->
                requireText "launch evidence ID" launch.EvidenceId
                require (launch.Model = lane.Model && launch.Effort = lane.Effort)
                    "lane model/effort disagrees with explicit launch settings"
                require (launch.Source <> RuntimeSelfIntrospection)
                    "runtime self-introspection is not launch settings provenance"
            | None -> ()
            if lane.Activity = Running then
                require lane.Launch.IsSome "running lane requires explicit launch settings evidence"
            if lane.Role = Worker && lane.Activity = Running then
                require (lane.Model = Gpt6Sol && lane.Effort = High
                         && (match lane.Launch with
                             | Some launch ->
                                 launch.Model = Gpt6Sol && launch.Effort = High
                                 && launch.Source = ExplicitOrchestratorSpawn
                                 && nonblank launch.EvidenceId
                             | None -> false))
                    "active V2 worker requires explicit gpt-6-sol/high spawn evidence"
            if lane.Role = Orchestrator && lane.Activity = Running then
                require (lane.Model = Gpt6Sol && lane.Effort = High
                         && (match lane.Launch with
                             | Some launch ->
                                 launch.Model = Gpt6Sol && launch.Effort = High
                                 && launch.Source = ExplicitUserInstruction
                                 && nonblank launch.EvidenceId
                             | None -> false))
                    "running orchestrator requires explicit gpt-6-sol/high visible-profile evidence"
        let lanes = snapshot.Lanes |> List.sortWith (fun a b -> ordinal.Compare(a.Id, b.Id))
        let byModel =
            lanes
            |> List.countBy _.Model
            |> List.sortWith (fun (a, _) (b, _) -> ordinal.Compare(modelText a, modelText b))
        let laneCounts = {
            Total = lanes.Length
            Active = lanes |> List.filter (fun x -> x.Activity = Running) |> List.length
            ReservedDirectV2 = lanes |> List.filter (fun x -> x.Reservation = DirectV2) |> List.length
            ActiveReservedDirectV2 =
                lanes |> List.filter (fun x -> x.Role = Worker && x.Reservation = DirectV2 && x.Activity = Running) |> List.length
            ByModel = byModel
        }
        require (snapshot.DeclaredLaneCounts.Total = laneCounts.Total) "declared total lane count disagrees with lanes"
        require (snapshot.DeclaredLaneCounts.Active = laneCounts.Active) "declared active lane count disagrees with lanes"
        require (snapshot.DeclaredLaneCounts.ReservedDirectV2 = laneCounts.ReservedDirectV2)
            "declared direct V2 reservation count disagrees with lanes"
        require (snapshot.DeclaredLaneCounts.ActiveReservedDirectV2 = laneCounts.ActiveReservedDirectV2)
            "declared active direct V2 reservation count disagrees with lanes"
        require (laneCounts.ActiveReservedDirectV2 > 0) "active reserved direct V2 worker is required"
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
        telemetry.HealthObservation |> Option.iter (fun health ->
            requireUtc "health observation" health.ObservedAt
            require (health.ObservedAt <= snapshot.AsOf) "health observation exceeds report time"
            requireText "health evidence ID" health.EvidenceId)
        telemetry.WorkspaceObservation |> Option.iter (fun workspace ->
            requireUtc "workspace observation" workspace.ObservedAt
            require (workspace.ObservedAt <= snapshot.AsOf) "workspace observation exceeds report time"
            requireText "workspace evidence ID" workspace.EvidenceId
            require (workspace.WorkspaceId = telemetry.WorkspaceId) "workspace observation ID disagrees with telemetry workspace"
            require (workspace.Pending = telemetry.Pending
                     && workspace.PendingUnacknowledged = telemetry.PendingUnacknowledged
                     && workspace.UnacknowledgedLossy = telemetry.UnacknowledgedLossy)
                "workspace observation queue disagrees with telemetry queue")
        if telemetry.Readiness = ActiveHealthy then
            require (match telemetry.HealthObservation, telemetry.WorkspaceObservation with
                     | Some health, Some workspace ->
                         health.Authenticated && health.Ready && health.CollectorVerified
                         && workspace.Configured && workspace.CollectorVerified
                     | _ -> false)
                "healthy telemetry requires authenticated health and configured workspace observations"
            telemetry.HealthObservation |> Option.iter (fun health ->
                require (snapshot.AsOf - health.ObservedAt <= maxTelemetryObservationAge)
                    "healthy telemetry requires fresh authenticated health observation")
            telemetry.WorkspaceObservation |> Option.iter (fun workspace ->
                require (snapshot.AsOf - workspace.ObservedAt <= maxTelemetryObservationAge)
                    "healthy telemetry requires fresh configured workspace observation")
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
            require (later.Authenticated && later.CollectorVerified && nonblank later.EvidenceId)
                "capture requires authenticated collector-verified later queue evidence"
            require (later.ObservedAt > host.ObservedAt) "zero queue observation must be later than Host receipt"
            require (later.Pending = 0 && later.PendingUnacknowledged = 0
                     && not later.UnacknowledgedLossy) "capture requires a later zero, lossless queue"
            require (later.ObservedAt <= snapshot.AsOf) "later queue observation exceeds report time"

        snapshot.CliStatus |> Option.iter (fun status ->
            require (status.Authenticated && status.CollectorVerified && nonblank status.EvidenceId)
                "CLI /status requires authenticated collector-verified provenance"
            requireUtc "CLI /status observation" status.ObservedAt
            require (status.ObservedAt <= snapshot.AsOf) "CLI /status observation exceeds report time"
            require (status.WeeklyRemainingPercent >= 0 && status.WeeklyRemainingPercent <= 100)
                "CLI /status weekly remaining percent must be between 0 and 100"
            require (nonblank status.WeeklyResetTimeZone)
                "CLI /status weekly reset requires a local time zone"
            require (status.WeeklyResetLocal > snapshot.AsOf)
                "CLI /status weekly reset must follow report time"
            require (status.ContextCapacityTokens > 0L && status.ContextUsedTokens >= 0L
                     && status.ContextUsedTokens <= status.ContextCapacityTokens)
                "CLI /status context occupancy must fit its capacity")
        let mutable counterSummary: TeamCounterSummary option = None
        match snapshot.PeriodUsage with
        | UnknownPeriodUsage -> ()
        | NativeTurnPeriodUsage usage ->
            requireUtc "period start" usage.WindowStart
            requireUtc "period end" usage.WindowEnd
            require (usage.WindowStart < usage.WindowEnd && usage.WindowEnd <= snapshot.AsOf)
                "native period usage requires a past nonempty UTC window"
            require (usage.CollectorVerified && usage.Runner.Origin = NativeRunnerItem)
                "period usage requires collector-verified native runner provenance"
            require (usage.Runner.ObservedAt >= usage.WindowStart
                     && usage.Runner.ObservedAt <= usage.WindowEnd)
                "native runner observation must fall within period window"
            requireUtc "period runner observation" usage.Runner.ObservedAt
            requireLink "period runner item" usage.Runner.Evidence
            requireText "period runner workspace" usage.Runner.WorkspaceId
            require (usage.Runner.WorkspaceId = telemetry.WorkspaceId)
                "period runner workspace must match telemetry workspace"
            requireText "period runner item" usage.Runner.ItemId
            require (not usage.Runner.NativeTurns.IsEmpty)
                "period usage requires genuine native turn IDs and usage"
            let ids = usage.Runner.NativeTurns |> List.map _.TurnId
            require (ids.Length = (ids |> List.distinct).Length)
                "period usage native turn IDs must be unique"
            for turn in usage.Runner.NativeTurns do
                requireText "period native turn ID" turn.TurnId
                require (turn.InputTokens >= 0 && turn.OutputTokens >= 0
                         && int64 turn.InputTokens + int64 turn.OutputTokens > 0L)
                    "period usage requires positive native turn token counts"
        | NativeCounterPeriodUsage window ->
            match deriveCounterWindow snapshot.AsOf false window with
            | Ok summary -> counterSummary <- Some summary
            | Error findings -> for finding in findings do errors.Add finding
        | LocalCounterDiagnostic window ->
            match deriveCounterWindow snapshot.AsOf true window with
            | Ok summary -> counterSummary <- Some summary
            | Error findings -> for finding in findings do errors.Add finding

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
            require ((completion.Result = CompletedInfo || completion.Result = FailedUnsafe)
                     && completion.Link.IsSome)
                "completion requires a terminal result and evidence link"
            completion.Link |> Option.iter (requireLink "completion")
        let recent = snapshot.CompletionHistory |> List.sortWith completionCompare |> List.truncate 5
        if errors.Count > 0 then Error(errors |> Seq.distinct |> Seq.sort |> Seq.toList)
        else Ok { Snapshot = snapshot; LaneCounts = laneCounts; EvidenceCounts = counts
                  RecentCompletions = recent; CounterSummary = counterSummary }

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
                let launch =
                    lane.Launch
                    |> Option.map (fun value -> $"{launchSourceText value.Source} ({escape value.EvidenceId})")
                    |> Option.defaultValue "—"
                $"| {escape lane.Id} | {roleText lane.Role} | {activityText lane.Activity} | {modelText lane.Model} | {effortText lane.Effort} | {reservationText lane.Reservation} | {statusText lane.State} | {launch} |")
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
        let healthText =
            snapshot.Telemetry.HealthObservation
            |> Option.map (fun value ->
                $"authenticated={value.Authenticated}, ready={value.Ready}, collector verified={value.CollectorVerified}, observed={timeText value.ObservedAt}, evidence={escape value.EvidenceId}")
            |> Option.defaultValue "none supplied"
        let workspaceText =
            snapshot.Telemetry.WorkspaceObservation
            |> Option.map (fun value ->
                $"configured={value.Configured}, collector verified={value.CollectorVerified}, observed={timeText value.ObservedAt}, evidence={escape value.EvidenceId}")
            |> Option.defaultValue "none supplied"
        let statusRows =
            match snapshot.CliStatus with
            | None -> [ "- Authenticated CLI /status weekly evidence: 🔘 Unknown — none supplied"
                        "- Context occupancy: 🔘 Unknown — none supplied" ]
            | Some status ->
                let reset = status.WeeklyResetLocal.ToString("yyyy-MM-dd HH:mm zzz", CultureInfo.InvariantCulture)
                let percent = string status.WeeklyRemainingPercent + "%"
                [ $"- Authenticated CLI /status weekly evidence: 🔵 Completed/Info — {percent} left; reset {reset} ({escape status.WeeklyResetTimeZone}); observed {timeText status.ObservedAt}; provenance {escape status.EvidenceId}"
                  $"- Context occupancy: 🔵 Completed/Info — {status.ContextUsedTokens}/{status.ContextCapacityTokens} tokens in current context window; not cumulative usage" ]
        let periodUsageText =
            match snapshot.PeriodUsage with
            | UnknownPeriodUsage -> "🔘 Unknown — genuine native turn usage has not been supplied"
            | NativeTurnPeriodUsage usage ->
                let input = usage.Runner.NativeTurns |> List.sumBy (fun turn -> int64 turn.InputTokens)
                let output = usage.Runner.NativeTurns |> List.sumBy (fun turn -> int64 turn.OutputTokens)
                $"🔵 Completed/Info — input={input}, output={output} native turn tokens; window {timeText usage.WindowStart} to {timeText usage.WindowEnd}; {renderLink usage.Runner.Evidence}"
            | NativeCounterPeriodUsage _ | LocalCounterDiagnostic _ ->
                match progress.CounterSummary with
                | None -> "🔘 Unknown — native counter window has not qualified"
                | Some summary ->
                    let delta = summary.LatestPeriodDelta
                    let noncached = delta.Input - delta.CachedInput
                    let provenance =
                        match snapshot.PeriodUsage with
                        | LocalCounterDiagnostic _ -> "🟠 Blocked/Incomplete evidence — local JSONL diagnostic only; no authenticated collector or Host receipt"
                        | _ -> "🔵 Completed/Info — collector-verified"
                    $"{provenance}; team-wide 10-minute native token_count delta {timeText summary.WindowStart} to {timeText summary.WindowEnd}: input={integerText delta.Input}, cached input={integerText delta.CachedInput}, noncached input={integerText noncached}, output={integerText delta.Output}, total={integerText delta.Total}; sessions={summary.SessionCount}; cached input is included in input"
        let periodsText, allTotalText, meanText, nativeRateText, exhaustionText =
            match progress.CounterSummary with
            | None ->
                "🔘 Unknown — no complete native team counter history",
                "🔘 Unknown — no complete native team counter history",
                "🔘 Unknown — no complete native team counter window",
                "🔘 Unknown — no fresh native primary.used_percent observation",
                "🔘 Unknown — no qualified weekly percent slope"
            | Some summary ->
                let total = summary.AllPeriodsTotal
                let mean = summary.AllPeriodMean
                let counterState =
                    match snapshot.PeriodUsage with
                    | LocalCounterDiagnostic _ ->
                        "🟠 Blocked/Incomplete evidence — local JSONL diagnostic only; account and collector scope unverified"
                    | _ -> "🔵 Completed/Info"
                let periodsText =
                    $"{counterState} — {summary.CompletedPeriodCount} completed 10-minute periods from {timeText summary.RootStartedAt} through {timeText summary.WindowEnd}; zero-use periods included"
                let totalText =
                    $"{counterState} — input={integerText total.Input}, cached input={integerText total.CachedInput}, noncached input={integerText (total.Input - total.CachedInput)}, output={integerText total.Output}, total={integerText total.Total} tokens across all completed periods"
                let meanText =
                    $"{counterState} — input={decimalText mean.Input}, cached input={decimalText mean.CachedInput}, noncached input={decimalText (mean.Input - mean.CachedInput)}, output={decimalText mean.Output}, total={decimalText mean.Total} team tokens/period"
                let rateText =
                    match summary.LatestRate with
                    | None -> "🔘 Unknown — no fresh native primary.used_percent observation"
                    | Some point ->
                        let used = decimalText point.Rate.UsedPercent + "%"
                        let remaining = decimalText (100M - point.Rate.UsedPercent) + "%"
                        $"{counterState} — used={used}, remaining={remaining}; observed={timeText point.ObservedAt}; reset={timeText point.Rate.ResetsAt}; account scope={escape point.Rate.AccountScopeId}; limit={escape point.Rate.LimitId}; session={escape point.SessionId}; ordinal={point.Ordinal}; provenance={escape point.EvidenceId}"
                let estimateText =
                    match summary.Exhaustion with
                    | NoRateSlope -> "🔘 Unknown — need a positive earliest-to-latest same-account, same-reset weekly percent slope across this root session"
                    | EstimatedAt instant ->
                        let qualification =
                            match snapshot.PeriodUsage with
                            | LocalCounterDiagnostic _ -> "; local JSONL diagnostic with unverified account scope"
                            | _ -> ""
                        $"🟡 Pending — approximately {timeText instant} at the continuous-use, account-wide weekly percent slope{qualification}"
                    | NotBeforeReset -> "🔵 Completed/Info — continuous-use, account-wide weekly percent slope projects no exhaustion before reset"
                    | AlreadyAtLimit -> "🔴 Failed/Unsafe — native weekly used percent reached 100%"
                periodsText, totalText, meanText, rateText, estimateText
        [
            "# V2 progress update"
            ""
            $"- Snapshot: {timeText snapshot.AsOf}"
            $"- Recorded roadmap head: `{snapshot.RoadmapHead}`"
            ""
            "## Lanes"
            ""
            "| Lane | Role | Activity | Model | Effort | Reservation | State | Launch settings evidence |"
            "| --- | --- | --- | --- | --- | --- | --- | --- |"
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
            $"- Authenticated health observation: {healthText}"
            $"- Configured workspace observation: {workspaceText}"
            $"- Current queue: pending={snapshot.Telemetry.Pending}, pending unacknowledged={snapshot.Telemetry.PendingUnacknowledged}, unacknowledged lossy={string snapshot.Telemetry.UnacknowledgedLossy |> _.ToLowerInvariant()}"
            $"- End-to-end capture acceptance: {captureText}"
            ""
            "## CLI status and token usage"
            ""
            yield! statusRows
            $"- Period usage: {periodUsageText}"
            $"- Completed periods: {periodsText}"
            $"- All-period team total: {allTotalText}"
            $"- Team mean per completed period: {meanText}"
            $"- Native weekly allowance: {nativeRateText}"
            $"- Weekly exhaustion estimate: {exhaustionText}"
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
