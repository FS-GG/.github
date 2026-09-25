#r "../../src/FS.GG.V2.Progress/bin/Release/net10.0/FS.GG.V2.Progress.dll"

open System
open System.IO
open System.Text.Json
open FS.GG.V2.Progress

let fail message = invalidArg "snapshot" message
let prop (value: JsonElement) (name: string) =
    match value.TryGetProperty name with
    | true, found -> found
    | _ -> fail ("missing " + name)
let optional (value: JsonElement) (name: string) =
    match value.TryGetProperty name with
    | true, found when found.ValueKind <> JsonValueKind.Null -> Some found
    | _ -> None
let str (value: JsonElement) = value.GetString()
let string (value: JsonElement) (name: string) = prop value name |> str
let boolean (value: JsonElement) (name: string) = (prop value name).GetBoolean()
let number (value: JsonElement) (name: string) = (prop value name).GetInt32()
let integer (value: JsonElement) (name: string) = (prop value name).GetInt64()
let instant (value: JsonElement) = DateTimeOffset.Parse(str value).ToUniversalTime()
let time (value: JsonElement) (name: string) = prop value name |> instant
let items (value: JsonElement) = value.EnumerateArray() |> Seq.toList
let strings (value: JsonElement) (name: string) = prop value name |> items |> List.map str
let state = function
    | "active" -> ActiveHealthy | "pending" -> Pending
    | "blocked" -> BlockedIncompleteEvidence | "failed" -> FailedUnsafe
    | "completed" -> CompletedInfo | "unknown" -> Unknown
    | value -> fail ("unknown state " + value)
let readLane (value: JsonElement) : Lane =
    let role = match string value "role" with "orchestrator" -> Orchestrator | "worker" -> Worker | _ -> fail "role"
    let reservation = match string value "reservation" with "direct-v2" -> DirectV2 | "general" -> General | _ -> fail "reservation"
    let launchSource = if role = Orchestrator then ExplicitUserInstruction else ExplicitOrchestratorSpawn
    { Id = string value "id"; Role = role; Activity = Running; Model = Gpt6Sol; Effort = High
      Reservation = reservation; State = state (string value "state")
      Launch = Some { Model = Gpt6Sol; Effort = High; Source = launchSource
                      EvidenceId = string value "launchEvidenceId" } }
let readWorkstream (value: JsonElement) : Workstream =
    { Name = string value "name"; State = state (string value "state")
      Detail = string value "detail"; PrCount = number value "prCount"
      EvidenceCount = number value "evidenceCount" }
let readNamed (value: JsonElement) : NamedState =
    { Name = string value "name"; State = state (string value "state")
      Detail = string value "detail" }
let readCompletion (value: JsonElement) : Completion =
    { CompletedAt = time value "completedAt"; Item = string value "item"
      Workstream = string value "workstream"; Result = state (string value "result")
      Link = Some { Label = string value "evidenceLabel"; Url = string value "evidenceUrl" }
      RecordedRoadmapHead = string value "roadmapHead" }
let readEvent (value: JsonElement) : NativeTokenCountEvent =
    let counters = prop value "counters"
    let primary = optional value "primaryRate" |> Option.map (fun rate ->
        { AccountScopeId = string rate "accountScopeId"; LimitId = string rate "limitId"
          WindowMinutes = number rate "windowMinutes"
          UsedPercent = (prop rate "usedPercent").GetDecimal()
          ResetsAt = time rate "resetsAt" })
    { ObservedAt = time value "observedAt"; Ordinal = integer value "ordinal"
      Counters = { InputTokens = integer counters "inputTokens"
                   CachedInputTokens = integer counters "cachedInputTokens"
                   OutputTokens = integer counters "outputTokens"
                   TotalTokens = integer counters "totalTokens" }
      PrimaryRate = primary }
let readSession asOf (value: JsonElement) : NativeSessionCounters =
    { SessionId = string value "sessionId"
      ParentSessionId = optional value "parentSessionId" |> Option.map str
      StartedAt = time value "startedAt"; CompleteThrough = asOf
      HistoryComplete = boolean value "historyComplete"
      CollectorVerified = false // This CLI accepts local diagnostics only.
      EvidenceId = string value "evidenceId"
      Events = prop value "events" |> items |> List.map readEvent }
let readSnapshot (root: JsonElement) : ProgressSnapshot =
    let asOf = time root "asOf"
    let lanes = prop root "lanes" |> items |> List.map readLane
    let streams = prop root "workstreams" |> items |> List.map readWorkstream
    let telemetry = prop root "telemetry"
    let health = prop telemetry "health"
    let workspace = prop telemetry "workspace"
    let period = prop root "localCounterWindow"
    let counterWindow : TeamCounterWindow =
        { RootSessionId = string period "rootSessionId"
          WindowStart = time period "windowStart"; WindowEnd = time period "windowEnd"
          DeclaredSessionCount = number period "declaredSessionCount"
          Sessions = prop period "sessions" |> items |> List.map (readSession asOf) }
    let declared = prop root "declaredCounts"
    let byModel = [ (Gpt6Sol, number declared "gpt6Sol") ]
    { AsOf = asOf; RoadmapHead = string root "roadmapHead"; Lanes = lanes
      DeclaredLaneCounts = { Total = number declared "totalLanes"
                             Active = number declared "activeLanes"
                             ReservedDirectV2 = number declared "reservedDirectV2"
                             ActiveReservedDirectV2 = number declared "activeReservedDirectV2"
                             ByModel = byModel }
      Workstreams = streams
      DeclaredEvidenceCounts = { Prs = number declared "prs"; Evidence = number declared "evidence" }
      Telemetry = { WorkspaceId = string telemetry "workspaceId"; Readiness = ActiveHealthy
                    HealthObservation = Some { Authenticated = boolean health "authenticated"
                                               Ready = boolean health "ready"
                                               CollectorVerified = boolean health "collectorVerified"
                                               ObservedAt = time health "observedAt"
                                               EvidenceId = string health "evidenceId" }
                    WorkspaceObservation = Some { Configured = boolean workspace "configured"
                                                  CollectorVerified = boolean workspace "collectorVerified"
                                                  WorkspaceId = string telemetry "workspaceId"
                                                  Pending = number workspace "pending"
                                                  PendingUnacknowledged = number workspace "pendingUnacknowledged"
                                                  UnacknowledgedLossy = boolean workspace "unacknowledgedLossy"
                                                  ObservedAt = time workspace "observedAt"
                                                  EvidenceId = string workspace "evidenceId" }
                    Pending = number workspace "pending"
                    PendingUnacknowledged = number workspace "pendingUnacknowledged"
                    UnacknowledgedLossy = boolean workspace "unacknowledgedLossy"
                    Capture = NoAcceptedCaptureClaim }
      CliStatus = None // The live /status panel lacks authenticated collector provenance.
      PeriodUsage = LocalCounterDiagnostic counterWindow
      ProtectedHolds = strings root "protectedHolds"
      Checks = prop root "checks" |> items |> List.map readNamed
      Risks = prop root "risks" |> items |> List.map readNamed
      NextActions = strings root "nextActions"
      CompletionHistory = prop root "completions" |> items |> List.map readCompletion }

if fsi.CommandLineArgs.Length <> 2 then
    eprintfn "usage: dotnet fsi scripts/v2-progress/render.fsx SNAPSHOT.json"
    exit 2
try
    use document = JsonDocument.Parse(File.ReadAllText fsi.CommandLineArgs.[1])
    match ProgressRenderer.renderSnapshot (readSnapshot document.RootElement) with
    | Ok markdown -> printf "%s" markdown
    | Error findings ->
        findings |> List.iter (eprintfn "refused: %s")
        exit 2
with error ->
    eprintfn "refused: %s" error.Message
    exit 2
