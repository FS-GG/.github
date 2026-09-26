namespace FS.GG.V2.Progress

open System

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

type Effort =
    | Low
    | Medium
    | High
    | XHigh
    | Max
    | Ultra

type Reservation =
    | General
    | DirectV2

type LaneRole =
    | Orchestrator
    | Worker

type LaneActivity =
    | Running
    | Idle
    | Finished

type LaunchSource =
    | ExplicitUserInstruction
    | ExplicitOrchestratorSpawn
    | RuntimeSelfIntrospection

type LaunchEvidence = {
    Model: LaneModel
    Effort: Effort
    Source: LaunchSource
    EvidenceId: string
}

type Lane = {
    Id: string
    CurrentWork: string
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

type EvidenceCounts = {
    Prs: int
    Evidence: int
}

type EvidenceLink = {
    Label: string
    Url: string
}

type NativeTurn = {
    TurnId: string
    InputTokens: int
    OutputTokens: int
}

type RunnerOrigin =
    | NativeRunnerItem
    | SyntheticOrUnknown

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

type NamedState = {
    Name: string
    State: Status
    Detail: string
}

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
    val statusText: Status -> string
    val derive: ProgressSnapshot -> Result<DerivedProgress, string list>
    val renderSnapshot: ProgressSnapshot -> Result<string, string list>
