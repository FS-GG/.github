// Temporary, repository-owned roadmap adapter. No GitHub, model or workload effects.
open System
open System.IO
open System.Text
open System.Text.Json
open System.Text.Json.Serialization
open System.Security.Cryptography
open System.Diagnostics

let options = JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                                    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)
let encode value = JsonSerializer.Serialize(value, options)
let fail message = invalidOp message
let require condition message = if not condition then fail message
let digest (bytes: byte array) = SHA256.HashData bytes |> Convert.ToHexString |> _.ToLowerInvariant()
// Read at most limit+1 actual bytes, including concurrent growth. The extra
// byte detects overflow without reading to EOF or growing memory unboundedly.
let readBoundedStream limit (stream: Stream) =
    require (limit >= 0 && limit < Int32.MaxValue) "invalid-stream-bound"
    use memory = new MemoryStream()
    let buffer = Array.zeroCreate<byte> (min 65536 (limit+1))
    let mutable remaining = limit+1
    let mutable ended = false
    while remaining > 0 && not ended do
        let count = stream.Read(buffer,0,min buffer.Length remaining)
        if count = 0 then ended <- true
        else
            memory.Write(buffer,0,count)
            remaining <- remaining-count
    require (memory.Length <= int64 limit) "input-grew-too-large"
    memory.ToArray()
let boundedFile limit path =
    let full = Path.GetFullPath path
    let mutable current = full
    while not (String.IsNullOrEmpty current) do
        require ((File.GetAttributes current &&& FileAttributes.ReparsePoint) = enum 0) "symlink-refused"
        current <- Path.GetDirectoryName current
    use stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read)
    require (stream.Length <= int64 limit) "input-too-large"
    readBoundedStream limit stream
let read<'a> path =
    let bytes = boundedFile (2 * 1024 * 1024) path
    use document = JsonDocument.Parse bytes
    let rec unique (node: JsonElement) =
        if node.ValueKind = JsonValueKind.Object then
            let names = Collections.Generic.HashSet<string>()
            for property in node.EnumerateObject() do
                require (names.Add property.Name) "duplicate-json-key"
                unique property.Value
        elif node.ValueKind = JsonValueKind.Array then
            for item in node.EnumerateArray() do unique item
    unique document.RootElement
    let value = JsonSerializer.Deserialize<'a>(bytes, options)
    require (not (obj.ReferenceEquals(value, null))) "empty-input"
    value, digest bytes
let nonempty (value: string) = not (String.IsNullOrWhiteSpace value)
let isDigest (value: string) = nonempty value && value.Length = 64 && Seq.forall Uri.IsHexDigit value
let id (value: string) = nonempty value && value.Length <= 128 && Seq.forall (fun c -> Char.IsAsciiLetterOrDigit c || "._-".Contains c) value
let oneOf value choices = require (List.contains value choices) ("unsupported-state:" + value)
let array name (value: 'a array) = require (not (isNull value)) ("missing-array:" + name)

type Dependency = { Lane: string; Boundary: string }
type Lane = {
    Id: string; Feature: string; Item: string; OriginalItem: string; Attempt: string
    Repository: string; Chain: string; Owner: string; Worktree: string; Head: string
    Priority: int; Readable: bool; ObservedAt: DateTimeOffset; Evidence: string array
    State: string; InFlight: bool; TouchSet: string array; Dependencies: Dependency array
    Source: string; Validation: string; Publication: string; Installed: string; Native: string; Projection: string
}
type Pull = { Repository: string; Campaign: string; Chain: string; Head: string }
type Snapshot = {
    Schema: string; Campaign: string; Capacity: int; CapacityKnown: bool; MaxAgeSeconds: int
    Lanes: Lane array; OpenPulls: Pull array
}
type Action = { Lane: string; Action: string; Reason: string }
type Frontier = { Schema: string; Advisory: bool; InputSha256: string; ObservedAt: DateTimeOffset; Active: string array; Actions: Action array }

let touch value =
    require (nonempty value && not (Path.IsPathRooted value) && not (value.Contains "\\")) "invalid-touch-set"
    let prefix = if value.EndsWith "/**" then value[..value.Length-4] else value.TrimEnd('/')
    require (nonempty prefix && not (prefix.Contains '*') && not (prefix.Contains '?') &&
             (prefix.Split('/') |> Array.forall (fun part -> nonempty part && part <> "." && part <> ".."))) "unsupported-touch-pattern"
    prefix
let overlaps (left: Lane) (right: Lane) =
    left.Repository.Equals(right.Repository, StringComparison.OrdinalIgnoreCase) &&
    (left.TouchSet |> Array.exists (fun a -> right.TouchSet |> Array.exists (fun b ->
        let a, b = touch a, touch b
        a = b || a.StartsWith(b + "/", StringComparison.Ordinal) || b.StartsWith(a + "/", StringComparison.Ordinal))))
let validateSnapshot (snapshot: Snapshot) =
    require (snapshot.Schema = "fsgg.programme.snapshot/1" && id snapshot.Campaign) "invalid-snapshot"
    require (snapshot.Capacity >= 1 && snapshot.Capacity <= 10 && snapshot.MaxAgeSeconds >= 1 && snapshot.MaxAgeSeconds <= 3600) "invalid-capacity-or-freshness"
    array "lanes" snapshot.Lanes; array "openPulls" snapshot.OpenPulls
    require (snapshot.Lanes.Length > 0 && snapshot.Lanes.Length <= 64) "invalid-lane-population"
    let names = Collections.Generic.HashSet<string>()
    for lane in snapshot.Lanes do
        require (id lane.Id && names.Add lane.Id && id lane.Feature && id lane.Item && id lane.OriginalItem && id lane.Attempt && id lane.Chain) "invalid-or-duplicate-lane"
        require (nonempty lane.Repository && lane.Repository.Split('/').Length = 2 && nonempty lane.Owner && Path.IsPathRooted lane.Worktree) "invalid-lane-location"
        array "evidence" lane.Evidence; array "touchSet" lane.TouchSet; array "dependencies" lane.Dependencies
        require (lane.Evidence.Length > 0 && Array.forall nonempty lane.Evidence && lane.TouchSet.Length > 0) "missing-evidence-or-touch-set"
        lane.TouchSet |> Array.iter (touch >> ignore)
        oneOf lane.State ["ready"; "active"; "review"; "checks"; "repair"; "operation"; "unknown"; "closed"; "blocked"]
        oneOf lane.Source ["none"; "local"; "pr"; "merged"]
        oneOf lane.Validation ["not-required"; "pending"; "passed"; "disputed"; "unknown"]
        oneOf lane.Publication ["not-required"; "pending"; "published"]
        oneOf lane.Installed ["not-required"; "pending"; "qualified"]
        oneOf lane.Native ["not-required"; "pending"; "succeeded"; "failed"; "unknown"]
        oneOf lane.Projection ["not-required"; "pending"; "landed"]
    for lane in snapshot.Lanes do
        for dependency in lane.Dependencies do
            require (names.Contains dependency.Lane && dependency.Lane <> lane.Id) "invalid-dependency"
            oneOf dependency.Boundary ["source"; "qualified"; "published"; "installed"; "native"; "closed"]
    for pull in snapshot.OpenPulls do
        require (id pull.Campaign && id pull.Chain && nonempty pull.Repository && nonempty pull.Head) "invalid-pull-fact"
    let rec visit seen path name =
        require (not (Set.contains name path)) "dependency-cycle"
        if not (Set.contains name seen) then
            let lane = snapshot.Lanes |> Array.find (fun lane -> lane.Id = name)
            lane.Dependencies |> Array.fold (fun acc edge -> visit acc (Set.add name path) edge.Lane) (Set.add name seen)
        else seen
    snapshot.Lanes |> Array.fold (fun seen lane -> visit seen Set.empty lane.Id) Set.empty |> ignore

let frontier now inputDigest snapshot =
    validateSnapshot snapshot
    let fresh lane = lane.Readable && lane.ObservedAt <= now && (now - lane.ObservedAt).TotalSeconds <= float snapshot.MaxAgeSeconds
    let satisfied (dependency: Dependency) =
        let lane = snapshot.Lanes |> Array.find (fun lane -> lane.Id = dependency.Lane)
        fresh lane && lane.Validation <> "disputed" &&
        match dependency.Boundary with
        | "source" -> lane.Source = "merged"
        | "qualified" -> lane.Validation = "passed"
        | "published" -> lane.Publication = "published" && List.contains lane.Validation ["passed"; "not-required"]
        | "installed" -> lane.Installed = "qualified" && List.contains lane.Validation ["passed"; "not-required"]
        | "native" -> lane.Native = "succeeded" && List.contains lane.Validation ["passed"; "not-required"]
        | _ -> lane.State = "closed" && List.contains lane.Validation ["passed"; "not-required"]
    let reserved = ResizeArray<Lane>(snapshot.Lanes |> Array.filter _.InFlight)
    let actions = ResizeArray<Action>()
    let add lane action reason = actions.Add { Lane = lane.Id; Action = action; Reason = reason }
    for lane in snapshot.Lanes |> Array.sortBy (fun lane -> lane.Priority, lane.Id) do
        if not (fresh lane) then add lane "refresh" "missing, unreadable, future or stale observation; retain reservations"
        elif lane.Validation = "disputed" then add lane "repair" "late validation dispute; dependent acceptance and effects fenced"
        elif lane.Native = "unknown" || lane.State = "unknown" then add lane "reconcile" "observe original operation before retry"
        elif lane.Native = "failed" then add lane "repair" "retain failed attempt; repair before separately admitting a new operation"
        elif lane.State = "closed" && lane.Projection = "pending" then add lane "project" "land section 0 closure before advancing this chain"
        elif lane.State = "closed" && List.contains lane.Validation ["pending"; "unknown"] then add lane "watch" "source history remains delivered; outstanding validation needs machine observation"
        elif lane.State = "closed" then add lane "retain" "window complete; broader feature completion is a separate judgment"
        elif lane.State = "blocked" then add lane "decision" "recorded boundary needs an owner decision"
        elif lane.InFlight then add lane "continue" "retain accountable owner; inspect only changes"
        elif not (lane.Dependencies |> Array.forall satisfied) then add lane "wait" "required dependency boundary is not established"
        elif snapshot.Lanes |> Array.exists (fun prior -> prior.Chain = lane.Chain && prior.Repository.Equals(lane.Repository, StringComparison.OrdinalIgnoreCase) && prior.State = "closed" && prior.Projection = "pending") then
            add lane "wait" "same-chain closure projection pending"
        elif not snapshot.CapacityKnown then add lane "wait" "capacity observation unavailable"
        elif snapshot.OpenPulls |> Array.exists (fun pull -> pull.Campaign = snapshot.Campaign && pull.Chain = lane.Chain && pull.Repository.Equals(lane.Repository, StringComparison.OrdinalIgnoreCase)) then
            add lane "resume-pr" "continue existing chain PR; do not create another"
        elif (snapshot.OpenPulls |> Array.filter (fun pull -> pull.Repository.Equals(lane.Repository, StringComparison.OrdinalIgnoreCase))).Length >= 2 then
            add lane "prepare-local" "repository managed PR queue full"
        elif reserved |> Seq.exists (fun other -> other.Repository.Equals(lane.Repository, StringComparison.OrdinalIgnoreCase) && other.Chain = lane.Chain) then
            add lane "wait" "same dependency chain already has an owner"
        elif reserved.Count >= snapshot.Capacity then add lane "wait" "worker capacity full"
        else
            match reserved |> Seq.tryFind (overlaps lane) with
            | Some holder -> add lane "wait" ("touch-set reserved by " + holder.Id)
            | None -> reserved.Add lane; add lane "assign" "independently ready bounded lane; native dispatch and resource admission remain external"
    { Schema = "fsgg.programme.frontier/1"; Advisory = true; InputSha256 = inputDigest; ObservedAt = now
      Active = snapshot.Lanes |> Array.filter _.InFlight |> Array.map _.Id; Actions = actions.ToArray() }

// Additive offline return/delta projection. Snapshot facts remain advisory;
// neither owner reports nor this projection mint acceptance or effect authority.
type ReturnBoundaries = {
    Source: string; Validation: string; Publication: string; Installed: string; Native: string; Projection: string
}
type ReturnEvidence = { Kind: string; Reference: string; Sha256: string; Scope: string }
type LaneReturn = {
    Schema: string; Campaign: string; Lane: string; Feature: string; Item: string; OriginalItem: string
    OriginalAttempt: string; Attempt: string; Candidate: string; Owner: string
    Revision: int64; Supersedes: int64; SourceRevision: string; InputPacketSha256: string
    ObservedAt: DateTimeOffset; Outcome: string; Boundaries: ReturnBoundaries
    Evidence: ReturnEvidence array; Unknowns: string array; Exception: string; Continuation: string; Narrative: string
}
type DeltaInput = {
    Schema: string; EvaluationTime: DateTimeOffset; EvaluatorIdentity: string; PolicyIdentity: string; UserScopeRevision: string
    BaseRevision: string; CurrentRevision: string
    BaseEvaluatorIdentity: string; BasePolicyIdentity: string; BaseUserScopeRevision: string
    BaseReturns: LaneReturn array; CurrentReturns: LaneReturn array; Snapshot: Snapshot
}
type DeltaNotice = { Lane: string; Kind: string }
type DeltaResult = {
    Schema: string; Advisory: bool; InputSha256: string; EvaluationTime: DateTimeOffset
    EvaluatorIdentity: string; PolicyIdentity: string; UserScopeRevision: string
    BaseRevision: string; CurrentRevision: string; Resynchronize: bool
    Changed: LaneReturn array; ActiveReservations: Lane array; Notices: DeltaNotice array
    Coverage: string; EffectAuthority: string
}
let returnBoundaries (lane: Lane) : ReturnBoundaries =
    { Source=lane.Source; Validation=lane.Validation; Publication=lane.Publication
      Installed=lane.Installed; Native=lane.Native; Projection=lane.Projection }
let validateReturn (row: LaneReturn) =
    require (row.Schema = "fsgg.programme.lane-return/1") "invalid-return-schema"
    [row.Campaign;row.Lane;row.Feature;row.Item;row.OriginalItem;row.OriginalAttempt;row.Attempt;row.Owner]
    |> List.iter (fun value -> require (id value) "invalid-return-identity")
    require (nonempty row.Candidate && row.Candidate.Length <= 256 && nonempty row.SourceRevision && row.SourceRevision.Length <= 256) "missing-return-candidate-source"
    require (row.Revision > 0L && row.Supersedes >= 0L && row.Supersedes < row.Revision) "invalid-return-revision"
    require (isDigest row.InputPacketSha256 && row.ObservedAt <> DateTimeOffset.MinValue) "missing-return-input-time"
    oneOf row.Outcome ["acknowledgment"; "window-reported"]
    require (not (obj.ReferenceEquals(row.Boundaries,null))) "missing-return-boundaries"
    let b = row.Boundaries
    oneOf b.Source ["none";"local";"pr";"merged"]
    oneOf b.Validation ["not-required";"pending";"passed";"disputed";"unknown"]
    oneOf b.Publication ["not-required";"pending";"published"]
    oneOf b.Installed ["not-required";"pending";"qualified"]
    oneOf b.Native ["not-required";"pending";"succeeded";"failed";"unknown"]
    oneOf b.Projection ["not-required";"pending";"landed"]
    array "return-evidence" row.Evidence; array "return-unknowns" row.Unknowns
    require (row.Evidence.Length > 0 && row.Evidence.Length <= 8 && row.Unknowns.Length <= 8) "return-evidence-bound"
    for evidence in row.Evidence do
        oneOf evidence.Kind ["source";"validation";"publication";"installed";"native";"projection";"mechanical"]
        require (nonempty evidence.Reference && evidence.Reference.Length <= 1024 && isDigest evidence.Sha256 &&
                 nonempty evidence.Scope && evidence.Scope.Length <= 256) "invalid-return-evidence"
    for unknown in row.Unknowns do require (nonempty unknown && unknown.Length <= 256) "invalid-return-unknown"
    require (nonempty row.Exception && row.Exception.Length <= 1024 && nonempty row.Continuation && row.Continuation.Length <= 1024 &&
             nonempty row.Narrative && Encoding.UTF8.GetByteCount row.Narrative <= 2048) "return-text-bound"
let returnStream (row: LaneReturn) = row.Campaign,row.Lane,row.Owner,row.OriginalAttempt,row.Attempt,row.Candidate
let returnContent (row: LaneReturn) = encode row
let contextDelta inputDigest (spec: DeltaInput) =
    require (spec.Schema = "fsgg.programme.delta-input/1") "invalid-delta-schema"
    require (spec.EvaluationTime <> DateTimeOffset.MinValue && id spec.EvaluatorIdentity && id spec.PolicyIdentity && id spec.UserScopeRevision &&
             id spec.CurrentRevision && (spec.BaseRevision = "" || id spec.BaseRevision)) "invalid-delta-identity-time"
    array "base-returns" spec.BaseReturns; array "current-returns" spec.CurrentReturns
    require (spec.BaseReturns.Length <= 128 && spec.CurrentReturns.Length <= 128) "delta-return-population-bound"
    require (Encoding.UTF8.GetByteCount(encode spec) <= 262144) "delta-input-payload-bound"
    validateSnapshot spec.Snapshot
    Array.append spec.BaseReturns spec.CurrentReturns |> Array.iter (fun row ->
        validateReturn row
        require (row.Campaign = spec.Snapshot.Campaign) "return-campaign-mismatch")
    let notices = ResizeArray<DeltaNotice>()
    let notice lane kind = notices.Add {Lane=lane;Kind=kind}
    let missingBase = spec.BaseRevision = ""
    require (not missingBase || spec.BaseReturns.Length = 0) "missing-base-with-content"
    if not missingBase then
        require (id spec.BaseEvaluatorIdentity && id spec.BasePolicyIdentity && id spec.BaseUserScopeRevision) "missing-base-identities"
    let invalidated = not missingBase && (spec.BaseEvaluatorIdentity <> spec.EvaluatorIdentity ||
                                          spec.BasePolicyIdentity <> spec.PolicyIdentity || spec.BaseUserScopeRevision <> spec.UserScopeRevision)
    if missingBase then notice "all" "missing-base-resynchronize"
    if invalidated then notice "all" "scope-policy-evaluator-invalidated"
    let reduce (rows: LaneReturn array) =
        rows |> Array.groupBy returnStream |> Array.sortBy fst |> Array.choose (fun (_, group) ->
            let conflict = group |> Array.groupBy _.Revision |> Array.exists (fun (_, values) -> values |> Array.map returnContent |> Array.distinct |> Array.length |> fun count -> count > 1)
            if conflict then notice group[0].Lane "equal-revision-conflict-reconcile"; None
            else Some (group |> Array.maxBy _.Revision))
    let baseline = reduce spec.BaseReturns
    let incoming = reduce spec.CurrentReturns
    // A newer row cannot erase equal-revision disagreement in the supplied
    // base/current closure; original attempts are identity, not parallel streams.
    let closure = Array.append spec.BaseReturns spec.CurrentReturns
    reduce closure |> ignore
    closure |> Array.groupBy (fun row -> row.Campaign,row.Lane,row.Owner,row.Attempt,row.Candidate)
    |> Array.iter (fun (_, rows) ->
        if (rows |> Array.map _.OriginalAttempt |> Array.distinct |> Array.length) > 1 then
            notice rows[0].Lane "original-lineage-conflict-reconcile")
    let candidates = ResizeArray<LaneReturn>()
    for row in incoming do
        require (row.Campaign = spec.Snapshot.Campaign) "return-campaign-mismatch"
        match spec.Snapshot.Lanes |> Array.tryFind (fun lane -> lane.Id = row.Lane) with
        | None -> notice row.Lane "unknown-lane-reconcile"
        | Some lane ->
            require (row.Feature=lane.Feature && row.Item=lane.Item && row.OriginalItem=lane.OriginalItem) "return-item-lineage-mismatch"
            let matched = row.Owner=lane.Owner && row.Attempt=lane.Attempt && row.Candidate=lane.Head && row.SourceRevision=lane.Head
            if not matched then notice row.Lane "superseded-or-incomparable-owner-candidate"
            else
                let prior = baseline |> Array.tryFind (fun old -> returnStream old = returnStream row)
                let lanePrior = baseline |> Array.filter (fun old -> old.Lane = row.Lane)
                if notices |> Seq.exists (fun n -> n.Lane = row.Lane && n.Kind = "equal-revision-conflict-reconcile") then
                    notice row.Lane "conflicted-base-or-return-reconcile"
                elif notices |> Seq.exists (fun n -> n.Lane = row.Lane && n.Kind = "original-lineage-conflict-reconcile") then
                    notice row.Lane "original-lineage-conflict-reconcile"
                elif prior.IsNone && (row.Revision <> 1L || row.Supersedes <> 0L) then
                    notice row.Lane "missing-return-base-resynchronize"
                elif lanePrior |> Array.exists (fun old -> old.OriginalItem <> row.OriginalItem || old.OriginalAttempt <> row.OriginalAttempt) then
                    notice row.Lane "original-lineage-conflict-reconcile"
                elif prior |> Option.exists (fun old -> row.Revision < old.Revision) then notice row.Lane "older-return-retained-as-history"
                elif prior |> Option.exists (fun old -> row.Revision = old.Revision && returnContent row <> returnContent old) then notice row.Lane "equal-revision-conflict-reconcile"
                elif prior |> Option.exists (fun old -> row.Revision > old.Revision && row.Supersedes <> old.Revision) then notice row.Lane "missing-return-base-resynchronize"
                elif not (lane.Readable && lane.ObservedAt <= spec.EvaluationTime &&
                          (spec.EvaluationTime-lane.ObservedAt).TotalSeconds <= float spec.Snapshot.MaxAgeSeconds &&
                          row.ObservedAt <= spec.EvaluationTime && (spec.EvaluationTime-row.ObservedAt).TotalSeconds <= float spec.Snapshot.MaxAgeSeconds) then
                    notice row.Lane "stale-or-unreadable-fact-refresh"
                elif row.Boundaries <> returnBoundaries lane then notice row.Lane "boundary-correspondence-reconcile"
                elif row.Outcome = "acknowledgment" then notice row.Lane "acknowledgment-not-completion"
                elif (prior |> Option.exists (fun old -> returnContent row = returnContent old)) && not invalidated then ()
                else candidates.Add row
    // Snapshot reservation inventory is independent of return freshness, arrival
    // order, acknowledgment, conflicts, missing base and owner reports.
    for lane in spec.Snapshot.Lanes do
        if lane.Validation = "disputed" || lane.Native = "failed" then notice lane.Id "late-failure-dependent-acceptance-fenced"
    let resync = missingBase || invalidated || (notices |> Seq.exists (fun n -> n.Kind = "missing-return-base-resynchronize"))
    let changed = candidates.ToArray() |> Array.sortBy (fun row -> row.Lane,returnStream row)
    if spec.BaseRevision = spec.CurrentRevision && changed.Length > 0 then notice "all" "view-revision-content-conflict-reconcile"
    let result : DeltaResult = {
        Schema="fsgg.programme.delta/1";Advisory=true;InputSha256=inputDigest;EvaluationTime=spec.EvaluationTime
        EvaluatorIdentity=spec.EvaluatorIdentity;PolicyIdentity=spec.PolicyIdentity;UserScopeRevision=spec.UserScopeRevision
        BaseRevision=spec.BaseRevision;CurrentRevision=spec.CurrentRevision;Resynchronize=resync
        Changed=(if resync || spec.BaseRevision=spec.CurrentRevision then [||] else changed)
        ActiveReservations=spec.Snapshot.Lanes |> Array.filter _.InFlight |> Array.sortBy _.Id
        Notices=notices |> Seq.distinct |> Seq.sortBy (fun n -> n.Lane,n.Kind) |> Seq.toArray
        Coverage="caller-declared-complete-snapshot; returns-may-be-partial; native-usage-unknown"
        EffectAuthority="none; external acceptance and current native admission required" }
    require (Encoding.UTF8.GetByteCount(encode result) <= 262144) "delta-output-payload-bound"
    result

// Pure routine decision domain; wire DTOs retain closed strings at the boundary.
type OperationClass = Ordinary | Observation | Integration | Protected | UnknownOperation
type BoundMode = Soft | Enforced | UnknownBound
type RoutineEvent = Failed | CleanupObserved | CurrentObservation | UnknownEffect | AdmissionRequired | EffectReconciled | ScopeChanged
type RoutineState = Running | RepairNeeded | CleanupUnconfirmed | CleanupConfirmed | EffectUnknown | AdmissionPending | Waiting | Accepted
type RoutineAction = ReturnExistingOwner | ObserveOriginalIdentity | RetainAdmission | RefreshFacts | WaitForResources | EscalateConflictOrScope | RetainCurrent
type RoutineReason = MissingBase | OperationRevisionGap | OrdinaryRepair | CleanupMissing | EffectUncertain | ProtectedBoundary | FactsMissing | RevisionConflict | ScopeInvalidated | DeadlineExpired | SoftTargetExceeded | ResourceUnavailable | ResourceUnknown | OwnerMissing | SharedConflict | DependencyPending | TerminalAccepted | ExistingOwner | AdmissionExternal
let operationClass = function
    | "ordinary" -> Ordinary | "observation" -> Observation | "integration" -> Integration
    | "protected" -> Protected | "unknown" -> UnknownOperation | _ -> fail "unknown-operation-class"
let boundMode = function | "soft" -> Soft | "enforced" -> Enforced | "unknown" -> UnknownBound | _ -> fail "unknown-bound-mode"
let routineEvent = function
    | "failed" -> Failed | "cleanup-observed" -> CleanupObserved | "current-observation" -> CurrentObservation
    | "unknown-effect" -> UnknownEffect | "admission-required" -> AdmissionRequired
    | "effect-reconciled" -> EffectReconciled | "scope-changed" -> ScopeChanged | _ -> fail "unknown-routine-event"
let routineState = function
    | "running" -> Running | "repair-needed" -> RepairNeeded | "cleanup-unconfirmed" -> CleanupUnconfirmed
    | "cleanup-observed" -> CleanupConfirmed | "unknown-effect" -> EffectUnknown
    | "protected-admission" -> AdmissionPending | "waiting" -> Waiting | "accepted" -> Accepted | _ -> fail "unknown-routine-state"
let actionCode = function
    | ReturnExistingOwner -> "return-existing-owner" | ObserveOriginalIdentity -> "observe-original-identity"
    | RetainAdmission -> "retain-admission" | RefreshFacts -> "refresh-facts" | WaitForResources -> "wait-for-resources"
    | EscalateConflictOrScope -> "escalate-conflict-or-scope" | RetainCurrent -> "retain-current"
let reasonCode = function
    | MissingBase -> "missing-delta-base" | OperationRevisionGap -> "operation-revision-gap"
    | OrdinaryRepair -> "ordinary-repair" | CleanupMissing -> "cleanup-unconfirmed" | EffectUncertain -> "unknown-effect"
    | ProtectedBoundary -> "protected-admission" | FactsMissing -> "missing-stale-or-future-facts"
    | RevisionConflict -> "revision-conflict" | ScopeInvalidated -> "scope-policy-evaluator-changed"
    | DeadlineExpired -> "execution-deadline-expired" | SoftTargetExceeded -> "soft-target-exceeded"
    | ResourceUnavailable -> "resources-occupied-or-enforced-limit" | ResourceUnknown -> "resource-observation-unknown"
    | OwnerMissing -> "accountable-owner-missing" | SharedConflict -> "shared-surface-conflict"
    | DependencyPending -> "dependency-pending" | TerminalAccepted -> "terminal-observed"
    | ExistingOwner -> "existing-owner-no-new-slot" | AdmissionExternal -> "integration-admission-external"
type RoutineResources = {
    Known: bool; ObservedAt: DateTimeOffset; Cpu: int; MemoryMiB: int; OccupiedCpu: int; OccupiedMemoryMiB: int
    Evidence: string; AgentMode: string; CpuMemoryMode: string
}
type RoutineOperation = {
    Lane: string; Operation: string; OriginalAttempt: string; Attempt: string; Owner: string; Candidate: string
    Revision: int64; Supersedes: int64; Event: string; Class: string; State: string
    ObservedAt: DateTimeOffset; Cleanup: string; ExecutionDeadline: DateTimeOffset; ExecutionMode: string
    ObservationDeadline: DateTimeOffset; Cpu: int; MemoryMiB: int; AllocationCpu: int; AllocationMemoryMiB: int
    AllocationMode: string; Evidence: string; Sha256: string; History: string
}
type RoutineDecision = {
    Lane: string; Owner: string; Candidate: string; Attempt: string; State: string; Action: string
    Reasons: string array; Evidence: string array; Operations: RoutineOperation array; Unresolved: string array
}
type RoutineInput = {
    Schema: string; Delta: DeltaInput; Resources: RoutineResources; Events: RoutineOperation array
    Previous: RoutineDecision array; PreviousPolicyIdentity: string; PreviousEvaluatorIdentity: string; PreviousUserScopeRevision: string
}
type RoutineResult = {
    Schema: string; Advisory: bool; InputSha256: string; EvaluationTime: DateTimeOffset
    PolicyIdentity: string; EvaluatorIdentity: string; UserScopeRevision: string
    Current: RoutineDecision array; Changed: RoutineDecision array; ActiveReservations: Lane array
    Resynchronize: bool; EffectAuthority: string; Coverage: string
}
let validateOperation (o: RoutineOperation) =
    [o.Lane;o.OriginalAttempt;o.Attempt] |> List.iter (fun v -> require (id v) "invalid-operation-identity")
    require (nonempty o.Operation && o.Operation.Length<=1024 && nonempty o.Owner && o.Owner.Length<=128 && nonempty o.Candidate && o.Candidate.Length<=256 && o.Revision>0L && o.Supersedes>=0L && o.Supersedes<o.Revision) "invalid-operation-revision"
    operationClass o.Class |> ignore; routineEvent o.Event |> ignore; routineState o.State |> ignore
    boundMode o.ExecutionMode |> ignore; boundMode o.AllocationMode |> ignore
    oneOf o.Cleanup ["observed";"unconfirmed"]
    require (o.ObservedAt<>DateTimeOffset.MinValue && (o.ExecutionDeadline<>DateTimeOffset.MinValue || o.ExecutionMode="unknown") &&
             o.Cpu>=0 && o.MemoryMiB>=0 && o.AllocationCpu>=0 && o.AllocationMemoryMiB>=0 &&
             nonempty o.Evidence && o.Evidence.Length<=1024 && isDigest o.Sha256 && nonempty o.History && o.History.Length<=1024) "invalid-operation-facts"
let operationStream (o: RoutineOperation) = o.Lane,o.Operation,o.OriginalAttempt,o.Attempt,o.Owner,o.Candidate
let semanticDecision (d: RoutineDecision) = d.Lane,d.Owner,d.Candidate,d.Attempt,d.State,d.Action,d.Reasons,d.Evidence,d.Unresolved
// One stream join supplies both reservation and routing predicates. Current views
// carry unresolved obligations separately from the latest observation narrative.
type TemporalEligibility = WithinBudget | ExecutionExpired | ObservationExpired | ExecutionUnknown
let temporalEligibility now repairNeeded (o: RoutineOperation) =
    let requiresBudget=o.State<>"accepted" || repairNeeded
    if not requiresBudget then WithinBudget
    elif operationClass o.Class=Observation then
        if now>=o.ObservationDeadline then ObservationExpired else WithinBudget
    else
        match boundMode o.ExecutionMode with
        | UnknownBound -> ExecutionUnknown
        | Enforced when now>=o.ExecutionDeadline -> ExecutionExpired
        | _ -> WithinBudget

type RoutineStream = {
    Observation: RoutineOperation; Conflict: bool; Missing: bool; Unknown: bool
    Protected: bool; Scope: bool; Held: bool
}
let reduceRoutineStream fresh priorGap (retained: RoutineOperation array) (rows: RoutineOperation array) =
    let sequence=rows |> Array.sortBy (fun o -> o.Revision,encode o)
    let latest=Array.last sequence
    let original=Array.head sequence
    let immutable (o: RoutineOperation) =
        o.Class,o.ExecutionDeadline,o.ExecutionMode,o.ObservationDeadline,o.AllocationCpu,o.AllocationMemoryMiB,o.AllocationMode,o.Cpu,o.MemoryMiB,o.History
    let payloadConflict=sequence |> Array.groupBy _.Revision |> Array.exists (fun (_, values) -> (values |> Array.map encode |> Array.distinct).Length>1)
    let conflict=payloadConflict || (sequence |> Array.exists (fun o -> immutable o<>immutable original))
    let prior=if priorGap then None else retained |> Array.tryFind (fun o -> operationStream o=operationStream latest)
    let mutable revision=prior |> Option.map _.Revision |> Option.defaultValue 0L
    let mutable missing=false
    for row in sequence |> Array.distinctBy _.Revision do
        if row.Revision>revision then
            if row.Supersedes<>revision || row.Revision<>revision+1L then missing<-true
            revision<-row.Revision
    let reconciled=routineEvent latest.Event=EffectReconciled && fresh latest.ObservedAt && not conflict && not missing
    let unknownClass=sequence |> Array.exists (fun o -> operationClass o.Class=UnknownOperation)
    let unknown=unknownClass || ((sequence |> Array.exists (fun o -> routineEvent o.Event=UnknownEffect || routineState o.State=EffectUnknown)) && not reconciled)
    let protectedEffect=sequence |> Array.exists (fun o -> operationClass o.Class=Protected || routineEvent o.Event=AdmissionRequired)
    let scope=sequence |> Array.exists (fun o -> routineEvent o.Event=ScopeChanged)
    // Clean terminal evidence releases nothing unless its stream joins are valid/fresh.
    let held=latest.Cleanup<>"observed" || unknown || conflict || missing || not (fresh latest.ObservedAt)
    { Observation={latest with Class=original.Class;ExecutionDeadline=original.ExecutionDeadline;ExecutionMode=original.ExecutionMode
                               ObservationDeadline=original.ObservationDeadline;AllocationCpu=original.AllocationCpu
                               AllocationMemoryMiB=original.AllocationMemoryMiB;AllocationMode=original.AllocationMode
                               Cpu=original.Cpu;MemoryMiB=original.MemoryMiB;History=original.History}
      Conflict=conflict;Missing=missing;Unknown=unknown;Protected=protectedEffect;Scope=scope;Held=held }

let routineDecisions inputDigest (spec: RoutineInput) =
    require (spec.Schema="fsgg.programme.routine-decisions-input/1") "invalid-routine-schema"
    require (not (obj.ReferenceEquals(spec.Delta,null)) && not (obj.ReferenceEquals(spec.Resources,null))) "missing-routine-input"
    array "events" spec.Events; array "previous" spec.Previous
    require (spec.Events.Length<=128 && spec.Previous.Length<=64 && Encoding.UTF8.GetByteCount(encode spec)<=262144) "routine-input-bound"
    let delta=contextDelta inputDigest spec.Delta
    let snapshot=spec.Delta.Snapshot
    let now=spec.Delta.EvaluationTime
    let fresh time = time<>DateTimeOffset.MinValue && time<=now && (now-time).TotalSeconds<=float snapshot.MaxAgeSeconds
    let resources=spec.Resources
    boundMode resources.AgentMode |> ignore; boundMode resources.CpuMemoryMode |> ignore
    require (resources.Cpu>=0 && resources.MemoryMiB>=0 && resources.OccupiedCpu>=0 && resources.OccupiedMemoryMiB>=0 &&
             nonempty resources.Evidence && resources.Evidence.Length<=1024) "invalid-resource-facts"
    let seen=Collections.Generic.HashSet<string>()
    for prior in spec.Previous do
        require (id prior.Lane && seen.Add prior.Lane && nonempty prior.Owner && nonempty prior.Candidate && id prior.Attempt) "invalid-previous-identity"
        require (snapshot.Lanes |> Array.exists (fun lane -> lane.Id=prior.Lane)) "previous-reservation-missing-from-snapshot"
        routineState prior.State |> ignore
        oneOf prior.Action ["return-existing-owner";"observe-original-identity";"retain-admission";"refresh-facts";"wait-for-resources";"escalate-conflict-or-scope";"retain-current"]
        array "previous-operations" prior.Operations; array "previous-unresolved" prior.Unresolved
        array "previous-reasons" prior.Reasons; array "previous-evidence" prior.Evidence
        require (prior.Operations.Length<=128 && prior.Unresolved.Length<=8 && prior.Reasons.Length<=16 && prior.Evidence.Length<=128) "previous-population-bound"
        prior.Unresolved |> Array.iter (fun value -> oneOf value ["revision-conflict";"unknown-effect";"scope-policy-evaluator-changed";"protected-admission";"operation-revision-gap";"missing-delta-base"])
        prior.Operations |> Array.iter validateOperation
        require (prior.Operations |> Array.forall (fun o -> o.Lane=prior.Lane)) "previous-operation-lane-mismatch"
    spec.Events |> Array.iter (fun o ->
        validateOperation o
        require (snapshot.Lanes |> Array.exists (fun lane -> lane.Id=o.Lane && lane.Attempt=o.Attempt && lane.Owner=o.Owner && lane.Head=o.Candidate)) "operation-snapshot-identity-mismatch")
    let retained=spec.Previous |> Array.collect _.Operations
    require (retained.Length<=128) "retained-operation-bound"
    let closure=Array.append retained spec.Events
    let gapLanes=spec.Previous |> Array.filter (fun d -> Array.contains "operation-revision-gap" d.Unresolved) |> Array.map _.Lane |> Set.ofArray
    let streams=closure |> Array.groupBy operationStream |> Array.sortBy fst
                |> Array.map (fun (_, rows) -> reduceRoutineStream fresh (Set.contains rows[0].Lane gapLanes) retained rows)
    // Original unresolved obligations, not latest narratives or snapshot booleans,
    // determine retained holds. Only externally reconciled current facts clear them.
    let priorUnknownResolved name =
        let selected=streams |> Array.filter (fun stream -> stream.Observation.Lane=name)
        selected.Length>0 && (selected |> Array.forall (fun stream ->
            routineEvent stream.Observation.Event=EffectReconciled && fresh stream.Observation.ObservedAt &&
            not stream.Conflict && not stream.Missing && not stream.Unknown))
    let priorHeld =
        spec.Previous
        |> Array.filter (fun d -> d.Unresolved |> Array.exists (fun reason ->
            match reason with
            | "unknown-effect" -> not (priorUnknownResolved d.Lane)
            | "operation-revision-gap" ->
                let selected=streams |> Array.filter (fun stream -> stream.Observation.Lane=d.Lane)
                selected.Length=0 || (selected |> Array.exists _.Missing)
            | "missing-delta-base" -> delta.Resynchronize
            | _ -> true))
        |> Array.map _.Lane |> Set.ofArray
    let heldLanes=streams |> Array.filter _.Held |> Array.map (fun stream -> stream.Observation.Lane) |> Set.ofArray |> Set.union priorHeld
    let reservations=snapshot.Lanes |> Array.filter (fun lane -> lane.InFlight || Set.contains lane.Id heldLanes)
                     |> Array.map (fun lane -> {lane with InFlight=true}) |> Array.sortBy _.Id
    let invalidated=spec.Previous.Length>0 && (spec.PreviousPolicyIdentity<>spec.Delta.PolicyIdentity ||
                    spec.PreviousEvaluatorIdentity<>spec.Delta.EvaluatorIdentity || spec.PreviousUserScopeRevision<>spec.Delta.UserScopeRevision)
    let decisions=snapshot.Lanes |> Array.sortBy _.Id |> Array.map (fun lane ->
        let prior=spec.Previous |> Array.tryFind (fun d -> d.Lane=lane.Id)
        let unresolved=ResizeArray<RoutineReason>()
        if invalidated then unresolved.Add ScopeInvalidated
        if spec.Delta.BaseRevision="" then unresolved.Add MissingBase
        if prior |> Option.exists (fun d -> d.Unresolved |> Array.contains "revision-conflict") then unresolved.Add RevisionConflict
        if prior |> Option.exists (fun d -> d.Unresolved |> Array.contains "scope-policy-evaluator-changed") then unresolved.Add ScopeInvalidated
        if prior |> Option.exists (fun d -> d.Unresolved |> Array.contains "protected-admission") then unresolved.Add ProtectedBoundary
        let laneStreams=streams |> Array.filter (fun stream -> stream.Observation.Lane=lane.Id)
        let operations=laneStreams |> Array.map _.Observation
        let missing=(laneStreams |> Array.exists _.Missing) ||
                    (Set.contains lane.Id gapLanes && laneStreams.Length=0)
        if missing then unresolved.Add OperationRevisionGap
        for stream in laneStreams do
            if stream.Conflict then unresolved.Add RevisionConflict
            if stream.Unknown then unresolved.Add EffectUncertain
            if stream.Protected then unresolved.Add ProtectedBoundary
            if stream.Scope then unresolved.Add ScopeInvalidated
        let lineage=operations |> Array.groupBy (fun o -> o.Operation,o.Attempt,o.Owner,o.Candidate)
        if lineage |> Array.exists (fun (_, rows) -> (rows |> Array.map _.OriginalAttempt |> Array.distinct).Length>1) then unresolved.Add RevisionConflict
        if prior |> Option.exists (fun d -> d.Unresolved |> Array.contains "unknown-effect") then
            if not (priorUnknownResolved lane.Id) then unresolved.Add EffectUncertain
        let reasons=ResizeArray<RoutineReason>()
        reasons.AddRange unresolved
        let mutable action=RetainCurrent
        let mutable state=lane.State |> function "closed" -> "accepted" | "repair" -> "repair-needed" | "unknown" -> "unknown-effect" | _ -> "running"
        let choose a r s = action<-a; reasons.Add r; state<-s
        let repairNeeded=lane.State="repair" || operations |> Array.exists (fun o -> routineEvent o.Event=Failed || o.State="repair-needed" || o.State="cleanup-observed")
        let temporal=operations |> Array.map (temporalEligibility now repairNeeded)
        let badFact=not lane.Readable || not (fresh lane.ObservedAt) || missing || delta.Resynchronize ||
                    (operations |> Array.exists (fun o -> not (fresh o.ObservedAt))) ||
                    (delta.Notices |> Array.exists (fun n -> (n.Lane=lane.Id || n.Lane="all") && n.Kind<>"older-return-retained-as-history"))
        let conflict=reservations |> Array.exists (fun other -> other.Id<>lane.Id && overlaps lane other)
        let dependency=lane.Dependencies |> Array.exists (fun edge ->
            let other=snapshot.Lanes |> Array.find (fun value -> value.Id=edge.Lane)
            let qualified=List.contains other.Validation ["passed";"not-required"]
            let boundary=match edge.Boundary with
                         | "source" -> other.Source="merged"
                         | "qualified" -> other.Validation="passed"
                         | "published" -> other.Publication="published" && qualified
                         | "installed" -> other.Installed="qualified" && qualified
                         | "native" -> other.Native="succeeded" && qualified
                         | _ -> other.State="closed" && qualified
            not other.Readable || not (fresh other.ObservedAt) || other.Validation="disputed" || other.Native="failed" || not boundary)
        if unresolved.Contains EffectUncertain || lane.Native="unknown" || lane.State="unknown" then choose ObserveOriginalIdentity EffectUncertain "unknown-effect"
        elif unresolved.Contains ProtectedBoundary then choose RetainAdmission ProtectedBoundary "protected-admission"
        elif unresolved.Contains RevisionConflict then choose EscalateConflictOrScope RevisionConflict "waiting"
        elif unresolved.Contains ScopeInvalidated || (operations |> Array.exists (fun o -> routineEvent o.Event=ScopeChanged)) then choose EscalateConflictOrScope ScopeInvalidated "waiting"
        elif badFact then
            if spec.Delta.BaseRevision="" then reasons.Add MissingBase
            if missing then reasons.Add OperationRevisionGap
            choose RefreshFacts FactsMissing "waiting"
        elif conflict then choose EscalateConflictOrScope SharedConflict "waiting"
        elif dependency then choose RetainCurrent DependencyPending "waiting"
        elif operations |> Array.exists (fun o -> o.Owner<>lane.Owner || o.Attempt<>lane.Attempt || o.Candidate<>lane.Head) then choose EscalateConflictOrScope OwnerMissing "waiting"
        elif (repairNeeded && operations.Length=0) ||
             (operations |> Array.exists (fun o -> o.Cleanup<>"observed" && (o.State<>"running" || repairNeeded))) then choose ObserveOriginalIdentity CleanupMissing "cleanup-unconfirmed"
        elif Array.contains ExecutionExpired temporal then choose RetainAdmission DeadlineExpired "waiting"
        elif Array.contains ObservationExpired temporal then choose RefreshFacts DeadlineExpired "waiting"
        elif Array.contains ExecutionUnknown temporal then choose RetainAdmission FactsMissing "waiting"
        elif (not repairNeeded && lane.State="closed" && lane.Validation<>"pending" && lane.Validation<>"unknown" && lane.Projection<>"pending" &&
              (operations |> Array.forall (fun o -> o.State="accepted" && o.Cleanup="observed"))) then choose RetainCurrent TerminalAccepted "accepted"
        elif not resources.Known || not (fresh resources.ObservedAt) || boundMode resources.CpuMemoryMode=UnknownBound then choose WaitForResources ResourceUnknown "waiting"
        elif operations |> Array.exists (fun o ->
            o.Cpu>resources.Cpu-resources.OccupiedCpu || o.MemoryMiB>resources.MemoryMiB-resources.OccupiedMemoryMiB ||
            (boundMode o.AllocationMode=Enforced && (o.Cpu>o.AllocationCpu || o.MemoryMiB>o.AllocationMemoryMiB))) then choose WaitForResources ResourceUnavailable "waiting"
        elif operations |> Array.exists (fun o -> operationClass o.Class=Integration) then choose ReturnExistingOwner AdmissionExternal "running"
        elif not lane.InFlight && operations.Length=0 then choose RetainAdmission OwnerMissing "waiting"
        else
            if operations |> Array.exists (fun o ->
                (boundMode o.ExecutionMode=Soft && now>=o.ExecutionDeadline) ||
                (boundMode o.AllocationMode=Soft && (o.Cpu>o.AllocationCpu || o.MemoryMiB>o.AllocationMemoryMiB))) then reasons.Add SoftTargetExceeded
            choose ReturnExistingOwner (if repairNeeded then OrdinaryRepair else ExistingOwner) (if repairNeeded then "repair-needed" else "running")
        { Lane=lane.Id;Owner=lane.Owner;Candidate=lane.Head;Attempt=lane.Attempt;State=state;Action=actionCode action
          Reasons=reasons |> Seq.map reasonCode |> Seq.distinct |> Seq.sort |> Seq.toArray
          Evidence=operations |> Array.map _.Evidence |> Array.append lane.Evidence |> Array.distinct |> Array.sort
          Operations=operations;Unresolved=unresolved |> Seq.map reasonCode |> Seq.distinct |> Seq.sort |> Seq.toArray })
    let changed=decisions |> Array.filter (fun d -> spec.Previous |> Array.tryFind (fun old -> old.Lane=d.Lane) |> Option.forall (fun old -> semanticDecision old<>semanticDecision d))
    let result={ Schema="fsgg.programme.routine-decisions/1";Advisory=true;InputSha256=inputDigest;EvaluationTime=now
                 PolicyIdentity=spec.Delta.PolicyIdentity;EvaluatorIdentity=spec.Delta.EvaluatorIdentity;UserScopeRevision=spec.Delta.UserScopeRevision
                 Current=decisions;Changed=changed;ActiveReservations=reservations;Resynchronize=delta.Resynchronize || invalidated || (streams |> Array.exists _.Missing) || (Set.count gapLanes>0 && streams.Length=0)
                 EffectAuthority="none; existing owner authority and fresh native admission remain external"
                 Coverage="caller-declared snapshot and resource observations; no native reads, acceptance or effect execution" }
    require (Encoding.UTF8.GetByteCount(encode result)<=262144) "routine-output-bound"
    result

// Bound complete UTF-8 JSON including its emitted newline; overflow is a pointer,
// never a sliced decision or partial evidence. Retention uses the existing helper.
let routineStdout artifactPath artifactSha256 (observation: RoutineResult) =
    let compact = encode {| schema="fsgg.programme.routine-decisions-summary/1"; advisory=true
                            artifact=artifactPath; sha256=artifactSha256; effectAuthority="none"
                            changed=observation.Changed |> Array.map (fun d ->
                                {| lane=d.Lane;owner=d.Owner;candidate=d.Candidate;attempt=d.Attempt;state=d.State
                                   action=d.Action;reasons=d.Reasons;evidence=d.Evidence |}) |}
    let fits (value: string) = Encoding.UTF8.GetByteCount(value)+1<=2048
    if fits compact then compact
    else
        let pointer=encode {| schema="fsgg.programme.routine-decisions-summary/1";advisory=true
                              status="overflow-retrieve-artifact";artifact=artifactPath;sha256=artifactSha256
                              changedCount=observation.Changed.Length;effectAuthority="none" |}
        require (fits pointer) "routine-artifact-reference-exceeds-bound"
        pointer

type Reference = { Path: string; Sha256: string; StartLine: int; EndLine: int; Mandatory: bool; Trust: string; Reason: string }
type Packet = { Schema: string; Lane: string; Objective: string; Stop: string; MandatoryPaths: string array; MaximumBytes: int; References: Reference array }
let packet (spec: Packet) =
    require (spec.Schema = "fsgg.programme.packet/1" && id spec.Lane && nonempty spec.Objective && nonempty spec.Stop) "invalid-packet"
    array "references" spec.References; array "mandatoryPaths" spec.MandatoryPaths
    require (spec.References.Length > 0 && spec.References.Length <= 64 && spec.MandatoryPaths.Length > 0 && spec.MaximumBytes >= 1024 && spec.MaximumBytes <= 262144) "invalid-packet-bounds"
    let builder = StringBuilder($"Objective: {spec.Objective}\nStop: {spec.Stop}\n")
    let selected, omitted = ResizeArray<string>(), ResizeArray<string>()
    let identities = Collections.Generic.HashSet<string>()
    for reference in spec.References do
        require (Path.IsPathRooted reference.Path && identities.Add reference.Path && isDigest reference.Sha256 && nonempty reference.Reason) "invalid-reference"
        oneOf reference.Trust ["instruction"; "plan"; "data"]
        require (reference.Trust <> "instruction" || reference.Mandatory) "instruction-cannot-be-optional"
    for path in spec.MandatoryPaths do
        require (spec.References |> Array.exists (fun reference -> reference.Path = path && reference.Mandatory)) "mandatory-reference-missing"
    for reference in spec.References |> Array.sortBy (fun reference -> not reference.Mandatory) do
        let bytes = boundedFile (2 * 1024 * 1024) reference.Path
        require (digest bytes = reference.Sha256.ToLowerInvariant()) "reference-drift"
        let text = UTF8Encoding(false, true).GetString bytes
        let lines = text.Split('\n')
        let content =
            if reference.StartLine = 0 && reference.EndLine = 0 then text
            else
                require (reference.Trust <> "instruction") "governing-instruction-must-be-complete"
                require (reference.StartLine >= 1 && reference.EndLine >= reference.StartLine && reference.EndLine <= lines.Length) "invalid-excerpt"
                String.Join("\n", lines[reference.StartLine-1 .. reference.EndLine-1])
        let part = $"\n--- {reference.Trust}: {reference.Path} ({reference.Sha256}; {reference.Reason}) ---\n{content}\n"
        if Encoding.UTF8.GetByteCount(builder.ToString() + part) > spec.MaximumBytes then
            require (not reference.Mandatory) "mandatory-context-exceeds-bound"
            omitted.Add reference.Path
        else builder.Append part |> ignore; selected.Add reference.Path
    require (Encoding.UTF8.GetByteCount(builder.ToString()) <= spec.MaximumBytes) "packet-too-large"
    builder.ToString(), selected.ToArray(), omitted.ToArray()

type Artifact = { Path: string; Sha256: string; Bytes: int64 }
type Verification = { Schema: string; Claim: string; RequiredPaths: string array; Artifacts: Artifact array }
type VerifyResult = { Schema: string; MechanicalOnly: bool; Claim: string; Checked: int; Bytes: int64; Failures: string array }
let verify (spec: Verification) =
    require (spec.Schema = "fsgg.programme.verification/1" && nonempty spec.Claim) "invalid-verification"
    array "artifacts" spec.Artifacts; array "requiredPaths" spec.RequiredPaths
    require (spec.Artifacts.Length > 0 && spec.Artifacts.Length <= 20000 && spec.RequiredPaths.Length > 0) "invalid-verification-population"
    let paths = Collections.Generic.HashSet<string>()
    for artifact in spec.Artifacts do
        require (Path.IsPathRooted artifact.Path && paths.Add artifact.Path && isDigest artifact.Sha256 && artifact.Bytes >= 0L && artifact.Bytes <= 536870912L) "invalid-artifact"
    for path in spec.RequiredPaths do require (paths.Contains path) "required-artifact-missing"
    let failures = ResizeArray<string>()
    let mutable total = 0L
    for artifact in spec.Artifacts do
        try
            // Stream large retained artifacts; never load a package or process capture into model context.
            let mutable current = artifact.Path
            while not (String.IsNullOrEmpty current) do
                require ((File.GetAttributes current &&& FileAttributes.ReparsePoint) = enum 0) "artifact-symlink"
                current <- Path.GetDirectoryName current
            use stream = new FileStream(artifact.Path, FileMode.Open, FileAccess.Read, FileShare.Read)
            require (stream.Length = artifact.Bytes) "artifact-size-drift"
            use hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256)
            let buffer = Array.zeroCreate<byte> 65536
            let mutable count = stream.Read buffer
            let mutable consumed = 0L
            while count > 0 do
                consumed <- consumed + int64 count
                require (consumed <= artifact.Bytes) "artifact-grew"
                hash.AppendData(buffer, 0, count)
                count <- stream.Read buffer
            total <- total + consumed
            let observed = hash.GetHashAndReset() |> Convert.ToHexString |> _.ToLowerInvariant()
            if consumed <> artifact.Bytes || observed <> artifact.Sha256.ToLowerInvariant() then failures.Add artifact.Path
        with _ -> failures.Add artifact.Path
    { Schema = "fsgg.programme.verification-result/1"; MechanicalOnly = true; Claim = spec.Claim
      Checked = spec.Artifacts.Length; Bytes = total; Failures = failures.ToArray() }

// Explicit bounded views. Caller-declared access/obligations are observations,
// never OS permission grants, universal instruction discovery or authenticity.
type EvidenceViewInput = {
    Schema: string; Path: string; Sha256: string; Bytes: int64; StartLine: int; EndLine: int
    MaximumBytes: int; Trust: string; Mandatory: bool; ObligationsComplete: bool; Access: string; Provenance: string
}
type EvidenceViewResult = {
    Schema: string; Status: string; Path: string; Sha256: string; IdentityVerified: bool
    StartLine: int; EndLine: int; Trust: string; Mandatory: bool; Provenance: string
    Text: string; Bytes: int; DeclarationScope: string; InstructionAuthority: string
}
let evidenceView (spec: EvidenceViewInput) =
    let result status verified text : EvidenceViewResult = {
        Schema="fsgg.programme.evidence-view/1";Status=status;Path=spec.Path;Sha256=spec.Sha256
        IdentityVerified=verified;StartLine=spec.StartLine;EndLine=spec.EndLine;Trust=spec.Trust
        Mandatory=spec.Mandatory;Provenance=spec.Provenance;Text=text;Bytes=Encoding.UTF8.GetByteCount text
        DeclarationScope="access and obligation completeness caller-declared; equality not authenticity"
        InstructionAuthority="none minted; data never overrides governing instructions" }
    require (Encoding.UTF8.GetByteCount(encode spec)<=262144) "view-input-payload-bound"
    require (spec.Schema="fsgg.programme.evidence-view-input/1" && nonempty spec.Path && spec.Path.Length<=4096 && Path.IsPathRooted spec.Path &&
             isDigest spec.Sha256 && spec.Bytes >= 0L &&
             spec.MaximumBytes > 0 && spec.MaximumBytes <= 65536 && nonempty spec.Provenance && spec.Provenance.Length <= 1024) "invalid-view-input"
    oneOf spec.Trust ["instruction";"plan";"data"]
    oneOf spec.Access ["allowed";"denied";"unknown"]
    if spec.Access <> "allowed" then result "permission-unestablished" false ""
    elif spec.Bytes>2097152L then result "oversize" false ""
    elif spec.Trust="instruction" && (not spec.Mandatory || spec.StartLine<>0 || spec.EndLine<>0) then result "instruction-must-be-mandatory-whole-file" false ""
    elif spec.Trust="instruction" && not spec.ObligationsComplete then result "obligations-incomplete" false ""
    else
        try
            let raw = boundedFile 2097152 spec.Path
            if int64 raw.Length <> spec.Bytes || digest raw <> spec.Sha256.ToLowerInvariant() then result "drift" false ""
            else
                // Full raw identity precedes UTF-8 decoding, range selection and exposure.
                let text = UTF8Encoding(false,true).GetString raw
                let lines = text.Split('\n')
                let validRange = (spec.StartLine=0 && spec.EndLine=0) ||
                                 (spec.StartLine>=1 && spec.EndLine>=spec.StartLine && spec.EndLine<=lines.Length)
                if not validRange then result "invalid-range" true ""
                else
                    let selected = if spec.StartLine=0 then text else String.Join("\n",lines[spec.StartLine-1..spec.EndLine-1])
                    if Encoding.UTF8.GetByteCount selected > spec.MaximumBytes then result "oversize" true ""
                    else
                        let candidate = result "passed" true selected
                        if Encoding.UTF8.GetByteCount(encode candidate) > 262144 then result "oversize" true "" else candidate
        with
        | :? FileNotFoundException | :? DirectoryNotFoundException -> result "missing" false ""
        | :? UnauthorizedAccessException -> result "permission-denied" false ""
        | :? InvalidOperationException as error when error.Message="symlink-refused" -> result "linked" false ""
        | :? InvalidOperationException as error when error.Message="input-too-large" || error.Message="input-grew-too-large" -> result "oversize" false ""
        | :? IOException | :? DecoderFallbackException -> result "unreadable" false ""

// This is a pure reuse suggestion for one byte-check receipt. The caller must
// supply a trustworthy expected receipt digest and complete transitive closure.
// No semantic/native finding, permission, signature or completion is cached.
type MechanicalIdentity = { PolicySha256: string; ProfileSha256: string; EvaluatorSha256: string; SourceConfigSha256: string }
type MechanicalInputFact = {
    Id: string; Kind: string; Reference: string; Sha256: string; Bytes: int64; ObservedAt: DateTimeOffset; Access: string
}
type MechanicalReceipt = {
    Schema: string; Kind: string; Identity: MechanicalIdentity; InputClosureSha256: string
    EvaluatedAt: DateTimeOffset; Result: string
}
type ReuseInput = {
    Schema: string; EvaluationTime: DateTimeOffset; MaxAgeSeconds: int; Identity: MechanicalIdentity
    ClosureComplete: bool; RequiredInputIds: string array; Inputs: MechanicalInputFact array
    PriorReceipt: MechanicalReceipt; PriorReceiptSha256: string
}
type ReuseResult = {
    Schema: string; Decision: string; Reasons: string array; EvaluationTime: DateTimeOffset
    InputClosureSha256: string; MechanicalOnly: bool; Authority: string; Coverage: string
}
let mechanicalClosure (identity: MechanicalIdentity) (required: string array) (inputs: MechanicalInputFact array) =
    encode {| identity=identity; requiredInputIds=required |> Array.sort
              inputs=inputs |> Array.sortBy _.Id |} |> Encoding.UTF8.GetBytes |> digest
let validateMechanicalIdentity (identity: MechanicalIdentity) =
    require (not (obj.ReferenceEquals(identity,null))) "missing-mechanical-identity"
    [identity.PolicySha256;identity.ProfileSha256;identity.EvaluatorSha256;identity.SourceConfigSha256]
    |> List.iter (fun value -> require (isDigest value) "invalid-mechanical-identity")
let mechanicalReuse (spec: ReuseInput) =
    require (spec.Schema="fsgg.programme.reuse-input/1" && spec.EvaluationTime<>DateTimeOffset.MinValue &&
             spec.MaxAgeSeconds>=1 && spec.MaxAgeSeconds<=3600) "invalid-reuse-input"
    require (Encoding.UTF8.GetByteCount(encode spec)<=262144) "reuse-input-payload-bound"
    validateMechanicalIdentity spec.Identity
    array "required-inputs" spec.RequiredInputIds;array "mechanical-inputs" spec.Inputs
    require (spec.RequiredInputIds.Length>0 && spec.RequiredInputIds.Length<=32 && spec.Inputs.Length<=32) "reuse-population-bound"
    require (spec.RequiredInputIds |> Array.forall id) "invalid-required-input-id"
    require ((spec.RequiredInputIds |> Array.distinct |> Array.length) = spec.RequiredInputIds.Length) "duplicate-required-input"
    let seen=Collections.Generic.HashSet<string>()
    for fact in spec.Inputs do
        require (id fact.Id && seen.Add fact.Id && nonempty fact.Reference && fact.Reference.Length<=1024 &&
                 isDigest fact.Sha256 && fact.Bytes>=0L && fact.ObservedAt<>DateTimeOffset.MinValue) "invalid-or-duplicate-mechanical-input"
        oneOf fact.Kind ["evidence";"source";"configuration";"policy";"profile";"evaluator"]
        oneOf fact.Access ["allowed";"denied";"unknown"]
    let reasons=ResizeArray<string>()
    if not spec.ClosureComplete then reasons.Add "closure-unknown-request-missing"
    for required in spec.RequiredInputIds do if not (seen.Contains required) then reasons.Add ("missing-input:"+required)
    for fact in spec.Inputs do
        if not (Array.contains fact.Id spec.RequiredInputIds) then reasons.Add ("undeclared-input:"+fact.Id)
    let populationMissing=reasons.Count>0
    let closure=mechanicalClosure spec.Identity spec.RequiredInputIds spec.Inputs
    let fresh (time: DateTimeOffset) = time<=spec.EvaluationTime && (spec.EvaluationTime-time).TotalSeconds<=float spec.MaxAgeSeconds
    for fact in spec.Inputs do
        if fact.Access<>"allowed" then reasons.Add ("access-unestablished:"+fact.Id)
        if not (fresh fact.ObservedAt) then reasons.Add ("stale-or-future-input:"+fact.Id)
    if obj.ReferenceEquals(spec.PriorReceipt,null) then reasons.Add "receipt-missing-recompute"
    else
        let receipt=spec.PriorReceipt
        validateMechanicalIdentity receipt.Identity
        if receipt.Schema<>"fsgg.programme.mechanical-receipt/1" || receipt.Kind<>"declared-byte-equality" then reasons.Add "receipt-scope-not-reusable"
        if not (isDigest spec.PriorReceiptSha256) || digest(Encoding.UTF8.GetBytes(encode receipt))<>spec.PriorReceiptSha256.ToLowerInvariant() then reasons.Add "receipt-pin-mismatch"
        if receipt.Result<>"passed" then reasons.Add "failed-or-unknown-receipt-recompute"
        if receipt.Identity<>spec.Identity then reasons.Add "policy-profile-evaluator-source-config-changed"
        if not (isDigest receipt.InputClosureSha256) || receipt.InputClosureSha256<>closure then reasons.Add "input-closure-changed"
        if receipt.EvaluatedAt=DateTimeOffset.MinValue || not (fresh receipt.EvaluatedAt) then reasons.Add "receipt-stale-or-future"
    let result : ReuseResult = {
        Schema="fsgg.programme.reuse/1"
        Decision=(if populationMissing then "request-missing" elif reasons.Count>0 then "recompute" else "reuse")
        Reasons=reasons |> Seq.distinct |> Seq.sort |> Seq.toArray
        EvaluationTime=spec.EvaluationTime
        InputClosureSha256=closure
        MechanicalOnly=true
        Authority="none; no semantic/native acceptance, permission or completion reused"
        Coverage="caller-declared complete closure, access and trustworthy receipt pin; no authentication or live readback"
    }
    require (Encoding.UTF8.GetByteCount(encode result)<=262144) "reuse-output-payload-bound"
    result

let privateRoot (path: string) =
    require (Path.IsPathRooted path) "private-root-must-be-absolute"
    let full = Path.GetFullPath path
    let mutable current = full
    while not (String.IsNullOrEmpty current) do
        if Directory.Exists current then require ((File.GetAttributes current &&& FileAttributes.ReparsePoint) = enum 0) "private-root-symlink"
        current <- Path.GetDirectoryName current
    Directory.CreateDirectory full |> ignore
    if OperatingSystem.IsLinux() then
        let mode = File.GetUnixFileMode full
        // A freshly created directory has the user's umask; tighten only this dedicated root.
        if Directory.GetFileSystemEntries(full).Length = 0 then File.SetUnixFileMode(full, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute)
        else require ((int mode &&& 63) = 0) "private-root-permissions"
    full
let writePrivate (path: string) (bytes: byte array) =
    use stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)
    if OperatingSystem.IsLinux() then File.SetUnixFileMode(path, UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
    stream.Write bytes; stream.Flush(true)
type Measurement = { Schema: string; Operation: string; InputSha256: string; InputBytes: int; ElapsedMs: int64; OutputBytes: int; ArtifactBytes: int; NativeTokens: string; Result: string; Artifact: string; Input: string }
let run (command: string) (input: string) (root: string) =
    let root = privateRoot root
    let watch = Stopwatch.StartNew()
    let name = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffffff") + "-" + Guid.NewGuid().ToString("N")
    let mutable inputDigest = ""
    let mutable result = "passed"
    let mutable output = ""
    let artifactPath = Path.Combine(root, name + ".json")
    let inputPath = Path.Combine(root, name + ".input.json")
    let mutable inputBytes = 0
    let mutable routineObservation : RoutineResult option = None
    try
        let bytes = boundedFile (2 * 1024 * 1024) input
        inputBytes <- bytes.Length
        inputDigest <- digest bytes
        writePrivate inputPath bytes
        // Read the retained immutable input so the result and recovery source consume the same bytes.
        match command with
        | "frontier" ->
            let spec, hash = read<Snapshot> inputPath
            inputDigest <- hash
            output <- frontier DateTimeOffset.UtcNow hash spec |> encode
        | "delta" ->
            let spec, hash = read<DeltaInput> inputPath
            inputDigest <- hash
            output <- contextDelta hash spec |> encode
        | "routine-decisions" ->
            let spec, hash = read<RoutineInput> inputPath
            inputDigest <- hash
            let observation = routineDecisions hash spec
            routineObservation <- Some observation
            output <- encode observation
        | "packet" ->
            let spec, hash = read<Packet> inputPath
            inputDigest <- hash
            let body, selected, omitted = packet spec
            let path = Path.Combine(root, name + ".packet.txt")
            let bytes = Encoding.UTF8.GetBytes body
            writePrivate path bytes
            output <- encode {| schema = "fsgg.programme.packet-result/1"; lane = spec.Lane; packetPath = path
                                sha256 = digest bytes; bytes = bytes.Length; selected = selected; omitted = omitted |}
        | "view" ->
            let spec, hash = read<EvidenceViewInput> inputPath
            inputDigest <- hash
            let observation = evidenceView spec
            if observation.Status <> "passed" then result <- "refused"
            output <- encode observation
        | "reuse" ->
            let spec, hash = read<ReuseInput> inputPath
            inputDigest <- hash
            output <- mechanicalReuse spec |> encode
        | "verify" ->
            let spec, hash = read<Verification> inputPath
            inputDigest <- hash
            let observation = verify spec
            if observation.Failures.Length > 0 then result <- "failed"
            output <- encode observation
        | _ -> fail "unsupported-command"
    with error ->
        result <- "refused"
        output <- encode {| schema = "fsgg.programme.refusal/1"; reason = error.Message |}
    let bytes = Encoding.UTF8.GetBytes output
    writePrivate artifactPath bytes
    let stdout =
        if command="routine-decisions" then
            try
                let value = match routineObservation with
                            | Some observation -> routineStdout artifactPath (digest bytes) observation
                            | None -> output
                require (Encoding.UTF8.GetByteCount(value)+1<=2048) "routine-refusal-exceeds-bound"
                value
            with _ ->
                result <- "refused"
                encode {| schema="fsgg.programme.refusal/1"; reason="routine-output-reference-or-refusal-exceeds-bound" |}
        elif command = "verify" || bytes.Length > 16384 then
            encode {| schema = "fsgg.programme.result-summary/1"; result = result; artifact = artifactPath; bytes = bytes.Length |}
        else output
    // Immutable one-file-per-operation logging avoids a shared writer and interleaved JSONL.
    let measurement = { Schema = "fsgg.programme.measurement/1"; Operation = command; InputSha256 = inputDigest
                        InputBytes = inputBytes; ElapsedMs = watch.ElapsedMilliseconds; OutputBytes = Encoding.UTF8.GetByteCount stdout
                        ArtifactBytes = bytes.Length; NativeTokens = "unknown"; Result = result; Artifact = artifactPath; Input = inputPath }
    writePrivate (Path.Combine(root, name + ".measure.json")) (Encoding.UTF8.GetBytes(encode measurement))
    printfn "%s" stdout
    if result = "passed" then 0 else 3
let report (root: string) =
    let root = privateRoot root
    let paths = Directory.GetFiles(root, "*.measure.json") |> Array.sort
    require (paths.Length <= 10000) "measurement-window-too-large"
    let rows = paths |> Array.map (read<Measurement> >> fst)
    let latest = rows |> Array.tryFindBack (fun row -> row.Operation = "frontier" && row.Result = "passed")
    printfn "%s" (encode {| schema = "fsgg.programme.measurement-summary/1"; operations = rows.Length
                            elapsedMs = rows |> Array.sumBy _.ElapsedMs; outputBytes = rows |> Array.sumBy _.OutputBytes
                            inputBytes = rows |> Array.sumBy _.InputBytes; artifactBytes = rows |> Array.sumBy _.ArtifactBytes
                            latestFrontier = latest |> Option.map _.Artifact |> Option.defaultValue ""
                            latestSnapshot = latest |> Option.map _.Input |> Option.defaultValue ""
                            failures = rows |> Array.filter (fun row -> row.Result <> "passed") |> Array.length
                            nativeTokens = "unknown"; compactions = "not-observed" |})
    0

// Loaded by the focused acceptance harness without executing the CLI.
if fsi.CommandLineArgs.Length > 1 then
    try
        let arguments = fsi.CommandLineArgs |> Array.skip 1
        let code =
            match arguments with
            | [| "report"; root |] -> report root
            | [| command; input; root |] -> run command input root
            | _ -> fail "usage: programme.fsx <frontier|packet|verify|delta|view|reuse|routine-decisions> INPUT PRIVATE_ROOT, or report PRIVATE_ROOT"
        exit code
    with error ->
        if fsi.CommandLineArgs |> Array.contains "routine-decisions" then
            eprintfn "routine-decisions initialization refused; inspect the existing private input and root"
        else eprintfn "%s" error.Message
        exit 3
