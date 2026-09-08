namespace FS.GG.Coord

open System
open System.IO
open System.Globalization
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open System.Text.RegularExpressions

module TelemetryStore =
    [<Literal>]
    let BatchSchema = "fsgg.telemetry.ingest/1"
    [<Literal>]
    let MaxBatchBytes = 64 * 1024
    [<Literal>]
    let MaxEventBytes = 64 * 1024
    [<Literal>]
    let MaxEvents = 64

    type Coverage =
        { RecordValidity: string; JoinIntegrity: string; PopulationCoverage: string; Qualification: string
          Eligible: int64 option; Observed: int64 option }
    type Payload =
        | Item of featureId: string option
        | Feature of name: string
        | Attempt of parentItemId: string * status: string
        | ParentChild of parentId: string * childId: string
        | PullRequestHead of repository: string * pullRequest: int64 * head: string
        | Source of sourceIdentity: string * generation: string * cursor: string
        | Usage of provider: string * model: string * effort: string * input: int64 * cachedInput: int64 * cacheWriteInput: int64 * output: int64 * reasoning: int64 option * total: int64 * responses: int64 * sessions: int64 * turns: int64
        | Delivery of state: string * expectedHead: string option * observedHead: string option * pullRequest: int64 option
        | Evidence of digest: string * availability: string
        | Coverage of Coverage
        | Diagnostic of code: string * severity: string
        | Correction of targetIdentity: string * reason: string
        | RuntimeAdmission of invocationId: string * featureId: string * attemptId: string * parentAttemptId: string option * producerStream: string * requestedModel: string option * requestedEffort: string option * backend: string option
        | RuntimeStart of invocationId: string * threadId: string option * turnId: string option * turnSequence: int64 option * processId: int64 * phase: string
        | RuntimeTurnUsage of invocationId: string * threadId: string * turnId: string option * turnSequence: int64 * provider: string option * requestedModel: string option * observedModel: string option * requestedEffort: string option * observedEffort: string option * backend: string option * scope: string * provenance: string * input: int64 * cachedInput: int64 * output: int64 * reasoning: int64 option * total: int64
        | RuntimeTerminal of invocationId: string * threadId: string option * outcome: string * exitCode: int64
        | RuntimeGap of invocationId: string * code: string
        | CiBinding of collectionId: string * repository: string * head: string * pullRequest: int64 * workflow: string * featureId: string * attemptId: string * parentAttemptId: string option * producerStream: string * binding: string
        | CiPage of collectionId: string * resource: string * page: int64 * count: int64 * total: int64
        | CiRun of repository: string * runId: int64 * attempt: int64 * workflow: string * event: string * head: string * status: string * conclusion: string option * createdAt: string option * startedAt: string option * updatedAt: string option
        | CiJob of repository: string * runId: int64 * attempt: int64 * jobId: int64 * name: string * status: string * conclusion: string option * createdAt: string option * startedAt: string option * completedAt: string option
        | CiStep of repository: string * runId: int64 * attempt: int64 * jobId: int64 * number: int64 * name: string * status: string * conclusion: string option * startedAt: string option * completedAt: string option * classification: string * rationale: string
        | CiCoverage of collectionId: string * inventory: string * attempts: string * jobPages: string * terminal: string * timestamps: string * lineage: string * classification: string * criticalPath: string
        | CiPopulationAdmission of collectionId: string * repository: string * pullRequest: int64 * baseRef: string * baseSha: string * head: string * witness: string
        | CiCheck of repository: string * checkId: int64 * name: string * appSlug: string option * status: string * conclusion: string option * startedAt: string option * completedAt: string option
        | CiPopulationCoverage of collectionId: string * actions: string * checks: string * attempts: string * jobs: string * terminal: string * timestamps: string * continuation: string * externalChecks: int64 * gaps: string
        | NativeItemOutcome of repository: string * pullRequest: int64 * baseRef: string * baseSha: string * head: string * outcome: string * codeDelivery: string * mergeCommit: string option * occurredAt: string option * observedAt: string * sourceKind: string * sourceRef: string
        | BudgetPopulation of originalItemId: string * state: string * sourceKind: string * sourceRef: string
        | BudgetAttribution of dimension: string * provider: string * accountingScope: string * numerator: int64 option * denominator: int64 option * coverage: string * attribution: string * sourceKind: string * sourceRef: string
        | BudgetInterval of dimension: string * classification: string * startNanoseconds: int64 * endNanoseconds: int64 * witnessed: bool * sourceKind: string * sourceRef: string
        | BudgetIntervention of interventionId: string * transition: string * sequence: int64 * result: string * coverage: string * sourceRef: string
        | OperationalActivation of activationId: string * scope: string * runtime: string * activatedAt: string * clockProvenance: string * lateAfterSeconds: int64
        | ExpectedDispatch of dispatchId: string * activationId: string * relation: string * parentDispatchId: string option * runtime: string * expectedAt: string * clockProvenance: string
        | InvocationLineage of dispatchId: string * invocationId: string * relation: string * parentInvocationId: string option * rootInvocationId: string * runtime: string
        | EventTime of invocationId: string * event: string * occurredAt: string option * occurredClockProvenance: string option * observedAt: string option * observedClockProvenance: string option
    type Fact =
        { Identity: string; ItemId: string option; Revision: int64; Kind: string; Payload: Payload
          Canonical: string; ContentDigest: string }
    type Batch =
        { IngestId: string; SourceIdentity: string; Generation: string; Cursor: string; Facts: Fact list; ContentDigest: string }
    type Aggregate =
        { ItemId: string; FactCount: int64; UsageObservations: int64; DeliveryObservations: int64
          Input: int64; CachedInput: int64; CacheWriteInput: int64; Output: int64; Reasoning: int64 option; Total: int64
          Admitted: int64; Started: int64; Terminal: int64; RuntimeUsage: int64; MissingAdmission: int64; MissingStart: int64; MissingTerminal: int64; MissingUsage: int64
          RecordValidity: string; JoinIntegrity: string; PopulationCoverage: string; Qualification: string }
    type DurabilityAssessment = ApprovedLocalDurable | Unsafe of reason: string | DurabilityUnverified of reason: string

    let private nonEmpty label (value: string) =
        if String.IsNullOrWhiteSpace value then Error $"%s{label} must be a non-empty string" else Ok value
    let private requiredText (label: string) (node: JsonElement) (name: string) =
        match node.TryGetProperty name with
        | true, value when value.ValueKind = JsonValueKind.String -> nonEmpty $"%s{label}.%s{name}" (value.GetString())
        | _ -> Error $"%s{label}.%s{name} must be a non-empty string"
    let private optionalText (label: string) (node: JsonElement) (name: string) =
        match node.TryGetProperty name with
        | false, _ -> Ok None
        | true, value when value.ValueKind = JsonValueKind.Null -> Ok None
        | true, value when value.ValueKind = JsonValueKind.String -> nonEmpty $"%s{label}.%s{name}" (value.GetString()) |> Result.map Some
        | _ -> Error $"%s{label}.%s{name} must be a non-empty string or null"
    let private requiredInt (label: string) (node: JsonElement) (name: string) =
        match node.TryGetProperty name with
        | true, value when value.ValueKind = JsonValueKind.Number ->
            match value.TryGetInt64() with true, number when number >= 0L -> Ok number | _ -> Error $"%s{label}.%s{name} must be a non-negative integer"
        | _ -> Error $"%s{label}.%s{name} must be a non-negative integer"
    let private optionalInt (label: string) (node: JsonElement) (name: string) =
        match node.TryGetProperty name with
        | false, _ -> Ok None
        | true, value when value.ValueKind = JsonValueKind.Null -> Ok None
        | true, value when value.ValueKind = JsonValueKind.Number ->
            match value.TryGetInt64() with true, number when number >= 0L -> Ok(Some number) | _ -> Error $"%s{label}.%s{name} must be a non-negative integer or null"
        | _ -> Error $"%s{label}.%s{name} must be a non-negative integer or null"
    let private requiredBool (label: string) (node: JsonElement) (name: string) =
        match node.TryGetProperty name with
        | true, value when value.ValueKind = JsonValueKind.True -> Ok true
        | true, value when value.ValueKind = JsonValueKind.False -> Ok false
        | _ -> Error $"%s{label}.%s{name} must be a boolean"
    let private validTimestamp (value: string) =
        Regex.IsMatch(value, "^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\\.[0-9]+)?(?:Z|[+-][0-9]{2}:[0-9]{2})$")
        && (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            |> function true, _ -> true | _ -> false)
    let private requiredTimestamp label node name =
        requiredText label node name
        |> Result.bind (fun value -> if validTimestamp value then Ok value else Error $"%s{label}.%s{name} must be an RFC 3339 timestamp")
    let private optionalTimestamp label node name =
        optionalText label node name
        |> Result.bind (function None -> Ok None | Some value when validTimestamp value -> Ok(Some value) | Some _ -> Error $"%s{label}.%s{name} must be an RFC 3339 timestamp or null")
    let private validClock = function "host-wall" | "provider-native" | "github-native" -> true | _ -> false
    let private optionalClock label node name =
        optionalText label node name
        |> Result.bind (function None -> Ok None | Some value when validClock value -> Ok(Some value) | Some _ -> Error $"%s{label}.%s{name} is unsupported")
    let private closed label allowed (node: JsonElement) =
        let unknown = node.EnumerateObject() |> Seq.map _.Name |> Seq.filter (fun name -> not (Set.contains name allowed)) |> Seq.toList
        let names = String.concat "," unknown
        if unknown.IsEmpty then Ok () else Error $"%s{label} contains unknown field(s): %s{names}"
    let private sequence results =
        let errors = results |> List.choose (function Error e -> Some e | _ -> None)
        if errors.IsEmpty then Ok(results |> List.choose (function Ok value -> Some value | _ -> None)) else Error errors
    let private checkedAdd label left right =
        try Ok(Checked.(+) left right) with :? OverflowException -> Error $"%s{label} overflows int64"

    let private parseEvent index (node: JsonElement) =
        let label = $"events[%d{index}]"
        let kindResult = requiredText label node "kind"
        let identityResult = requiredText label node "identity"
        let itemResult = optionalText label node "itemId"
        let revisionResult = optionalInt label node "revision" |> Result.map (Option.defaultValue 0L)
        match kindResult, identityResult, itemResult, revisionResult with
        | Ok kind, Ok identity, Ok itemId, Ok revision ->
            let common = Set.ofList [ "kind"; "identity"; "itemId"; "revision" ]
            let make allowed payload =
                closed label (Set.union common (Set.ofList allowed)) node
                |> Result.map (fun () ->
                    let canonical = JsonNode.Parse(node.GetRawText()) |> fun value -> CanonicalJson.canonicalize (Encoding.UTF8.GetBytes(value.ToJsonString())) |> Result.defaultWith invalidOp
                    { Identity = identity; ItemId = itemId; Revision = revision; Kind = kind; Payload = payload; Canonical = canonical; ContentDigest = CanonicalJson.sha256(Encoding.UTF8.GetBytes canonical) })
            match kind with
            | "item" -> optionalText label node "featureId" |> Result.bind (Item >> make [ "featureId" ])
            | "feature" -> requiredText label node "name" |> Result.bind (Feature >> make [ "name" ])
            | "attempt" ->
                match requiredText label node "parentItemId", requiredText label node "status" with
                | Ok parent, Ok status -> make [ "parentItemId"; "status" ] (Attempt(parent, status))
                | values -> Error(sprintf "%A" values)
            | "parent-child" ->
                match requiredText label node "parentId", requiredText label node "childId" with
                | Ok parent, Ok child -> make [ "parentId"; "childId" ] (ParentChild(parent, child))
                | values -> Error(sprintf "%A" values)
            | "pr-head" ->
                match requiredText label node "repository", requiredInt label node "prNumber", requiredText label node "head" with
                | Ok repo, Ok pr, Ok head when head.Length = 40 && head |> Seq.forall Char.IsAsciiHexDigitLower -> make [ "repository"; "prNumber"; "head" ] (PullRequestHead(repo, pr, head))
                | Ok _, Ok _, Ok _ -> Error $"%s{label}.head must be 40 lowercase hexadecimal characters"
                | values -> Error(sprintf "%A" values)
            | "source" ->
                match requiredText label node "sourceIdentity", requiredText label node "generation", requiredText label node "cursor" with
                | Ok source, Ok generation, Ok cursor -> make [ "sourceIdentity"; "generation"; "cursor" ] (Source(source, generation, cursor))
                | values -> Error(sprintf "%A" values)
            | "usage" ->
                match requiredText label node "provider", requiredText label node "model", requiredText label node "effort",
                      requiredInt label node "input", requiredInt label node "cachedInput", requiredInt label node "cacheWriteInput",
                      requiredInt label node "output", optionalInt label node "reasoning", requiredInt label node "total",
                      requiredInt label node "responses", requiredInt label node "sessions", requiredInt label node "turns" with
                | Ok provider, Ok model, Ok effort, Ok input, Ok cached, Ok write, Ok output, Ok reasoning, Ok total, Ok responses, Ok sessions, Ok turns ->
                    match checkedAdd "usage.total" input output with
                    | Error reason -> Error reason
                    | Ok expected when expected <> total -> Error $"%s{label}.total must equal input + output"
                    | Ok _ when cached > input || write > input - cached -> Error $"%s{label} cachedInput + cacheWriteInput exceeds input"
                    | Ok _ when reasoning |> Option.exists (fun count -> count > output) -> Error $"%s{label}.reasoning exceeds output"
                    | Ok _ -> make [ "provider"; "model"; "effort"; "input"; "cachedInput"; "cacheWriteInput"; "output"; "reasoning"; "total"; "responses"; "sessions"; "turns" ] (Usage(provider, model, effort, input, cached, write, output, reasoning, total, responses, sessions, turns))
                | values -> Error(sprintf "%A" values)
            | "delivery" ->
                match requiredText label node "state", optionalText label node "expectedHead", optionalText label node "observedHead", optionalInt label node "prNumber" with
                | Ok state, Ok expected, Ok observed, Ok pr -> make [ "state"; "expectedHead"; "observedHead"; "prNumber" ] (Delivery(state, expected, observed, pr))
                | values -> Error(sprintf "%A" values)
            | "evidence" ->
                match requiredText label node "digest", requiredText label node "availability" with
                | Ok digest, Ok availability -> make [ "digest"; "availability" ] (Evidence(digest, availability))
                | values -> Error(sprintf "%A" values)
            | "coverage" ->
                match requiredText label node "recordValidity", requiredText label node "joinIntegrity", requiredText label node "populationCoverage", requiredText label node "qualification", optionalInt label node "eligible", optionalInt label node "observed" with
                | Ok validity, Ok join, Ok coverage, Ok qualification, Ok eligible, Ok observed ->
                    let allowedCoverage = Set.ofList [ "unknown"; "partial"; "complete"; "not-evaluated" ]
                    if not (Set.contains coverage allowedCoverage) then Error $"%s{label}.populationCoverage has an unsupported value"
                    else make [ "recordValidity"; "joinIntegrity"; "populationCoverage"; "qualification"; "eligible"; "observed" ] (Coverage { RecordValidity = validity; JoinIntegrity = join; PopulationCoverage = coverage; Qualification = qualification; Eligible = eligible; Observed = observed })
                | values -> Error(sprintf "%A" values)
            | "diagnostic" ->
                match requiredText label node "code", requiredText label node "severity" with
                | Ok code, Ok severity -> make [ "code"; "severity" ] (Diagnostic(code, severity))
                | values -> Error(sprintf "%A" values)
            | "correction" ->
                match requiredText label node "targetIdentity", requiredText label node "reason" with
                | Ok target, Ok reason -> make [ "targetIdentity"; "reason" ] (Correction(target, reason))
                | values -> Error(sprintf "%A" values)
            | "runtime-admission" ->
                match requiredText label node "invocationId", requiredText label node "featureId", requiredText label node "attemptId", optionalText label node "parentAttemptId", requiredText label node "producerStream", optionalText label node "requestedModel", optionalText label node "requestedEffort", optionalText label node "backend" with
                | Ok invocation, Ok feature, Ok attempt, Ok parent, Ok producer, Ok model, Ok effort, Ok backend ->
                    make [ "invocationId"; "featureId"; "attemptId"; "parentAttemptId"; "producerStream"; "requestedModel"; "requestedEffort"; "backend" ] (RuntimeAdmission(invocation,feature,attempt,parent,producer,model,effort,backend))
                | values -> Error(sprintf "%A" values)
            | "runtime-start" ->
                match requiredText label node "invocationId", optionalText label node "threadId", optionalText label node "turnId", optionalInt label node "turnSequence", requiredInt label node "processId", requiredText label node "phase" with
                | Ok invocation, Ok threadId, Ok turnId, Ok turnSequence, Ok processId, Ok phase when phase = "process" || phase = "thread" || (phase = "turn" && threadId.IsSome && turnSequence.IsSome) -> make [ "invocationId"; "threadId"; "turnId"; "turnSequence"; "processId"; "phase" ] (RuntimeStart(invocation,threadId,turnId,turnSequence,processId,phase))
                | Ok _, Ok _, Ok _, Ok _, Ok _, Ok _ -> Error $"%s{label}.phase must be process, thread, or a qualified turn"
                | values -> Error(sprintf "%A" values)
            | "runtime-turn-usage" ->
                match requiredText label node "invocationId", requiredText label node "threadId", optionalText label node "turnId", requiredInt label node "turnSequence",
                      optionalText label node "provider", optionalText label node "requestedModel", optionalText label node "observedModel", optionalText label node "requestedEffort", optionalText label node "observedEffort", optionalText label node "backend",
                      requiredText label node "scope", requiredText label node "provenance", requiredInt label node "input", requiredInt label node "cachedInput", requiredInt label node "output", optionalInt label node "reasoning", requiredInt label node "total" with
                | Ok invocation, Ok threadId, Ok turnId, Ok sequence, Ok provider, Ok requestedModel, Ok observedModel, Ok requestedEffort, Ok observedEffort, Ok backend, Ok scope, Ok provenance, Ok input, Ok cached, Ok output, Ok reasoning, Ok total ->
                    match checkedAdd "runtime-turn-usage.total" input output with
                    | Error reason -> Error reason
                    | Ok expected when expected <> total -> Error $"%s{label}.total must equal input + output"
                    | Ok _ when cached > input -> Error $"%s{label}.cachedInput exceeds input"
                    | Ok _ when reasoning |> Option.exists (fun count -> count > output) -> Error $"%s{label}.reasoning exceeds output"
                    | Ok _ -> make [ "invocationId"; "threadId"; "turnId"; "turnSequence"; "provider"; "requestedModel"; "observedModel"; "requestedEffort"; "observedEffort"; "backend"; "scope"; "provenance"; "input"; "cachedInput"; "output"; "reasoning"; "total" ] (RuntimeTurnUsage(invocation,threadId,turnId,sequence,provider,requestedModel,observedModel,requestedEffort,observedEffort,backend,scope,provenance,input,cached,output,reasoning,total))
                | values -> Error(sprintf "%A" values)
            | "runtime-terminal" ->
                match requiredText label node "invocationId", optionalText label node "threadId", requiredText label node "outcome", requiredInt label node "exitCode" with
                | Ok invocation, Ok threadId, Ok outcome, Ok exitCode -> make [ "invocationId"; "threadId"; "outcome"; "exitCode" ] (RuntimeTerminal(invocation,threadId,outcome,exitCode))
                | values -> Error(sprintf "%A" values)
            | "runtime-gap" ->
                match requiredText label node "invocationId", requiredText label node "code" with
                | Ok invocation, Ok code -> make [ "invocationId"; "code" ] (RuntimeGap(invocation,code))
                | values -> Error(sprintf "%A" values)
            | "ci-binding" ->
                match requiredText label node "collectionId", requiredText label node "repository", requiredText label node "head", requiredInt label node "prNumber", requiredText label node "workflow", requiredText label node "featureId", requiredText label node "attemptId", optionalText label node "parentAttemptId", requiredText label node "producerStream", requiredText label node "binding" with
                | Ok collection, Ok repository, Ok head, Ok pr, Ok workflow, Ok feature, Ok attempt, Ok parent, Ok producer, Ok binding when head.Length = 40 && head |> Seq.forall Char.IsAsciiHexDigitLower -> make [ "collectionId"; "repository"; "head"; "prNumber"; "workflow"; "featureId"; "attemptId"; "parentAttemptId"; "producerStream"; "binding" ] (CiBinding(collection,repository,head,pr,workflow,feature,attempt,parent,producer,binding))
                | Ok _, Ok _, Ok _, Ok _, Ok _, Ok _, Ok _, Ok _, Ok _, Ok _ -> Error $"%s{label}.head must be 40 lowercase hexadecimal characters"
                | values -> Error(sprintf "%A" values)
            | "ci-page" ->
                match requiredText label node "collectionId", requiredText label node "resource", requiredInt label node "page", requiredInt label node "count", requiredInt label node "total" with
                | Ok collection, Ok resource, Ok page, Ok count, Ok total when page > 0L && count <= 100L && total <= 1000L -> make [ "collectionId"; "resource"; "page"; "count"; "total" ] (CiPage(collection,resource,page,count,total))
                | Ok _, Ok _, Ok _, Ok _, Ok _ -> Error $"%s{label} page/count/total exceeds the bounded collection contract"
                | values -> Error(sprintf "%A" values)
            | "ci-run" ->
                match requiredText label node "repository", requiredInt label node "runId", requiredInt label node "attempt", requiredText label node "workflow", requiredText label node "event", requiredText label node "head", requiredText label node "status", optionalText label node "conclusion", optionalText label node "createdAt", optionalText label node "startedAt", optionalText label node "updatedAt" with
                | Ok repository, Ok runId, Ok attempt, Ok workflow, Ok event, Ok head, Ok status, Ok conclusion, Ok created, Ok started, Ok updated -> make [ "repository"; "runId"; "attempt"; "workflow"; "event"; "head"; "status"; "conclusion"; "createdAt"; "startedAt"; "updatedAt" ] (CiRun(repository,runId,attempt,workflow,event,head,status,conclusion,created,started,updated))
                | values -> Error(sprintf "%A" values)
            | "ci-job" ->
                match requiredText label node "repository", requiredInt label node "runId", requiredInt label node "attempt", requiredInt label node "jobId", requiredText label node "name", requiredText label node "status", optionalText label node "conclusion", optionalText label node "createdAt", optionalText label node "startedAt", optionalText label node "completedAt" with
                | Ok repository, Ok runId, Ok attempt, Ok jobId, Ok name, Ok status, Ok conclusion, Ok created, Ok started, Ok completed -> make [ "repository"; "runId"; "attempt"; "jobId"; "name"; "status"; "conclusion"; "createdAt"; "startedAt"; "completedAt" ] (CiJob(repository,runId,attempt,jobId,name,status,conclusion,created,started,completed))
                | values -> Error(sprintf "%A" values)
            | "ci-step" ->
                match requiredText label node "repository", requiredInt label node "runId", requiredInt label node "attempt", requiredInt label node "jobId", requiredInt label node "number", requiredText label node "name", requiredText label node "status", optionalText label node "conclusion", optionalText label node "startedAt", optionalText label node "completedAt", requiredText label node "classification", requiredText label node "rationale" with
                | Ok repository, Ok runId, Ok attempt, Ok jobId, Ok number, Ok name, Ok status, Ok conclusion, Ok started, Ok completed, Ok classification, Ok rationale -> make [ "repository"; "runId"; "attempt"; "jobId"; "number"; "name"; "status"; "conclusion"; "startedAt"; "completedAt"; "classification"; "rationale" ] (CiStep(repository,runId,attempt,jobId,number,name,status,conclusion,started,completed,classification,rationale))
                | values -> Error(sprintf "%A" values)
            | "ci-coverage" ->
                match requiredText label node "collectionId", requiredText label node "inventory", requiredText label node "attempts", requiredText label node "jobPages", requiredText label node "terminal", requiredText label node "timestamps", requiredText label node "lineage", requiredText label node "classification", requiredText label node "criticalPath" with
                | Ok collection, Ok inventory, Ok attempts, Ok jobPages, Ok terminal, Ok timestamps, Ok lineage, Ok classification, Ok criticalPath -> make [ "collectionId"; "inventory"; "attempts"; "jobPages"; "terminal"; "timestamps"; "lineage"; "classification"; "criticalPath" ] (CiCoverage(collection,inventory,attempts,jobPages,terminal,timestamps,lineage,classification,criticalPath))
                | values -> Error(sprintf "%A" values)
            | "ci-population-admission" ->
                match requiredText label node "collectionId", requiredText label node "repository", requiredInt label node "prNumber", requiredText label node "baseRef", requiredText label node "baseSha", requiredText label node "head", requiredText label node "witness" with
                | Ok collection, Ok repository, Ok pr, Ok baseRef, Ok baseSha, Ok head, Ok witness when pr > 0L && baseSha.Length = 40 && baseSha |> Seq.forall Char.IsAsciiHexDigitLower && head.Length = 40 && head |> Seq.forall Char.IsAsciiHexDigitLower && witness = "native-pr-head" ->
                    make [ "collectionId"; "repository"; "prNumber"; "baseRef"; "baseSha"; "head"; "witness" ] (CiPopulationAdmission(collection,repository,pr,baseRef,baseSha,head,witness))
                | Ok _, Ok _, Ok _, Ok _, Ok _, Ok _, Ok _ -> Error $"%s{label} requires a positive PR, base ref, lowercase base/head SHAs, and native-pr-head witness"
                | values -> Error(sprintf "%A" values)
            | "ci-check" ->
                match requiredText label node "repository", requiredInt label node "checkId", requiredText label node "name", optionalText label node "appSlug", requiredText label node "status", optionalText label node "conclusion", optionalTimestamp label node "startedAt", optionalTimestamp label node "completedAt" with
                | Ok repository, Ok checkId, Ok name, Ok app, Ok status, Ok conclusion, Ok started, Ok completed ->
                    make [ "repository"; "checkId"; "name"; "appSlug"; "status"; "conclusion"; "startedAt"; "completedAt" ] (CiCheck(repository,checkId,name,app,status,conclusion,started,completed))
                | values -> Error(sprintf "%A" values)
            | "ci-population-coverage" ->
                match requiredText label node "collectionId", requiredText label node "actions", requiredText label node "checks", requiredText label node "attempts", requiredText label node "jobs", requiredText label node "terminal", requiredText label node "timestamps", requiredText label node "continuation", requiredInt label node "externalChecks", requiredText label node "gaps" with
                | Ok collection, Ok actions, Ok checks, Ok attempts, Ok jobs, Ok terminal, Ok timestamps, Ok continuation, Ok externalChecks, Ok gaps
                    when [ actions; checks; attempts; jobs; terminal; timestamps ] |> List.forall (fun value -> Set.contains value (Set [ "complete"; "partial"; "unknown" ])) && Set.contains continuation (Set [ "none"; "pending" ]) ->
                    make [ "collectionId"; "actions"; "checks"; "attempts"; "jobs"; "terminal"; "timestamps"; "continuation"; "externalChecks"; "gaps" ] (CiPopulationCoverage(collection,actions,checks,attempts,jobs,terminal,timestamps,continuation,externalChecks,gaps))
                | Ok _, Ok _, Ok _, Ok _, Ok _, Ok _, Ok _, Ok _, Ok _, Ok _ -> Error $"%s{label} has unsupported population coverage"
                | values -> Error(sprintf "%A" values)
            | "native-item-outcome" ->
                match requiredText label node "repository", requiredInt label node "prNumber", requiredText label node "baseRef", requiredText label node "baseSha", requiredText label node "head", requiredText label node "outcome", requiredText label node "codeDelivery", optionalText label node "mergeCommit", optionalTimestamp label node "occurredAt", requiredTimestamp label node "observedAt", requiredText label node "sourceKind", requiredText label node "sourceRef" with
                | Ok repository, Ok pullRequest, Ok baseRef, Ok baseSha, Ok head, Ok outcome, Ok codeDelivery, Ok mergeCommit, Ok occurredAt, Ok observedAt, Ok sourceKind, Ok sourceRef
                    when pullRequest > 0L && Regex.IsMatch(repository, "^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")
                         && [ baseSha; head ] |> List.forall (fun sha -> sha.Length = 40 && sha |> Seq.forall Char.IsAsciiHexDigitLower)
                         && mergeCommit |> Option.forall (fun sha -> sha.Length = 40 && sha |> Seq.forall Char.IsAsciiHexDigitLower)
                         && Set.contains outcome (Set [ "ready"; "refused"; "delivered"; "delivered-after-readback"; "delivered-disputed"; "indeterminate" ])
                         && Set.contains codeDelivery (Set [ "delivered"; "not-delivered"; "unknown" ])
                         && ((Set.contains outcome (Set [ "delivered"; "delivered-after-readback"; "delivered-disputed" ]) && codeDelivery = "delivered" && mergeCommit.IsSome && occurredAt.IsSome)
                             || (outcome = "ready" && codeDelivery = "not-delivered" && mergeCommit.IsNone)
                             || (outcome = "refused" && codeDelivery = "not-delivered" && mergeCommit.IsNone)
                             || (outcome = "indeterminate" && codeDelivery = "unknown" && mergeCommit.IsNone))
                         && (occurredAt |> Option.forall (fun value -> DateTimeOffset.Parse(value, CultureInfo.InvariantCulture) <= DateTimeOffset.Parse(observedAt, CultureInfo.InvariantCulture)))
                         && sourceKind = "routine-delivery" ->
                    make [ "repository"; "prNumber"; "baseRef"; "baseSha"; "head"; "outcome"; "codeDelivery"; "mergeCommit"; "occurredAt"; "observedAt"; "sourceKind"; "sourceRef" ] (NativeItemOutcome(repository,pullRequest,baseRef,baseSha,head,outcome,codeDelivery,mergeCommit,occurredAt,observedAt,sourceKind,sourceRef))
                | Ok _, Ok _, Ok _, Ok _, Ok _, Ok _, Ok _, Ok _, Ok _, Ok _, Ok _, Ok _ -> Error $"%s{label} is not a valid machine-authored native item outcome"
                | values -> Error(sprintf "%A" values)
            | "budget-population" ->
                match requiredText label node "originalItemId", requiredText label node "state", requiredText label node "sourceKind", requiredText label node "sourceRef" with
                | Ok original, Ok state, Ok sourceKind, Ok sourceRef when (state = "open" || state = "completed") && sourceKind = "native-item" ->
                    make [ "originalItemId"; "state"; "sourceKind"; "sourceRef" ] (BudgetPopulation(original,state,sourceKind,sourceRef))
                | Ok _, Ok _, Ok _, Ok _ -> Error $"%s{label} requires open/completed state from native-item"
                | values -> Error(sprintf "%A" values)
            | "budget-attribution" ->
                match requiredText label node "dimension", requiredText label node "provider", requiredText label node "accountingScope", optionalInt label node "numerator", optionalInt label node "denominator", requiredText label node "coverage", requiredText label node "attribution", requiredText label node "sourceKind", requiredText label node "sourceRef" with
                | Ok dimension, Ok provider, Ok scope, Ok numerator, Ok denominator, Ok coverage, Ok attribution, Ok sourceKind, Ok sourceRef
                    when Set.contains coverage (Set [ "complete"; "partial"; "unknown"; "not-applicable" ]) && Set.contains attribution (Set [ "classified"; "mixed"; "unclassified" ]) && Set.contains sourceKind (Set [ "runtime"; "ci"; "legacy"; "native-item" ]) ->
                    make [ "dimension"; "provider"; "accountingScope"; "numerator"; "denominator"; "coverage"; "attribution"; "sourceKind"; "sourceRef" ] (BudgetAttribution(dimension,provider,scope,numerator,denominator,coverage,attribution,sourceKind,sourceRef))
                | Ok _, Ok _, Ok _, Ok _, Ok _, Ok _, Ok _, Ok _, Ok _ -> Error $"%s{label} has unsupported coverage or attribution"
                | values -> Error(sprintf "%A" values)
            | "budget-interval" ->
                match requiredText label node "dimension", requiredText label node "classification", requiredInt label node "startNanoseconds", requiredInt label node "endNanoseconds", requiredBool label node "witnessed", requiredText label node "sourceKind", requiredText label node "sourceRef" with
                | Ok dimension, Ok classification, Ok startAt, Ok endAt, Ok witnessed, Ok sourceKind, Ok sourceRef
                    when endAt >= startAt && Set.contains classification (Set [ "administrative"; "useful"; "productive" ]) && Set.contains sourceKind (Set [ "runtime"; "ci"; "legacy"; "native-item" ]) ->
                    make [ "dimension"; "classification"; "startNanoseconds"; "endNanoseconds"; "witnessed"; "sourceKind"; "sourceRef" ] (BudgetInterval(dimension,classification,startAt,endAt,witnessed,sourceKind,sourceRef))
                | Ok _, Ok _, Ok _, Ok _, Ok _, Ok _, Ok _ -> Error $"%s{label} has invalid interval or classification"
                | values -> Error(sprintf "%A" values)
            | "budget-intervention" ->
                match requiredText label node "interventionId", requiredText label node "transition", requiredInt label node "sequence", requiredText label node "result", requiredText label node "coverage", requiredText label node "sourceRef" with
                | Ok intervention, Ok transition, Ok sequence, Ok result, Ok coverage, Ok sourceRef
                    when ((transition = "deployed" && result = "not-evaluated") || (transition = "verified" && Set.contains result (Set [ "improved"; "failed"; "unknown" ]))) && Set.contains coverage (Set [ "complete"; "partial"; "unknown" ]) ->
                    make [ "interventionId"; "transition"; "sequence"; "result"; "coverage"; "sourceRef" ] (BudgetIntervention(intervention,transition,sequence,result,coverage,sourceRef))
                | Ok _, Ok _, Ok _, Ok _, Ok _, Ok _ -> Error $"%s{label} has unsupported intervention evidence"
                | values -> Error(sprintf "%A" values)
            | "operational-activation" ->
                match requiredText label node "activationId", requiredText label node "scope", requiredText label node "runtime", requiredTimestamp label node "activatedAt", requiredText label node "clockProvenance", requiredInt label node "lateAfterSeconds" with
                | Ok activation, Ok scope, Ok runtime, Ok activatedAt, Ok clock, Ok lateAfter
                    when scope = "explicit-future-dispatches" && validClock clock ->
                    make [ "activationId"; "scope"; "runtime"; "activatedAt"; "clockProvenance"; "lateAfterSeconds" ] (OperationalActivation(activation,scope,runtime,activatedAt,clock,lateAfter))
                | Ok _, Ok _, Ok _, Ok _, Ok _, Ok _ -> Error $"%s{label} requires explicit-future-dispatches scope and supported clock provenance"
                | values -> Error(sprintf "%A" values)
            | "expected-dispatch" ->
                match requiredText label node "dispatchId", requiredText label node "activationId", requiredText label node "relation", optionalText label node "parentDispatchId", requiredText label node "runtime", requiredTimestamp label node "expectedAt", requiredText label node "clockProvenance" with
                | Ok dispatch, Ok activation, Ok relation, Ok parent, Ok runtime, Ok expectedAt, Ok clock
                    when validClock clock && ((relation = "root" && parent.IsNone) || ((relation = "child" || relation = "follow-up") && parent.IsSome)) ->
                    make [ "dispatchId"; "activationId"; "relation"; "parentDispatchId"; "runtime"; "expectedAt"; "clockProvenance" ] (ExpectedDispatch(dispatch,activation,relation,parent,runtime,expectedAt,clock))
                | Ok _, Ok _, Ok _, Ok _, Ok _, Ok _, Ok _ -> Error $"%s{label} has an invalid relation, parent dispatch, or clock provenance"
                | values -> Error(sprintf "%A" values)
            | "invocation-lineage" ->
                match requiredText label node "dispatchId", requiredText label node "invocationId", requiredText label node "relation", optionalText label node "parentInvocationId", requiredText label node "rootInvocationId", requiredText label node "runtime" with
                | Ok dispatch, Ok invocation, Ok relation, Ok parent, Ok root, Ok runtime
                    when ((relation = "root" && parent.IsNone && root = invocation) || ((relation = "child" || relation = "follow-up") && parent.IsSome)) ->
                    make [ "dispatchId"; "invocationId"; "relation"; "parentInvocationId"; "rootInvocationId"; "runtime" ] (InvocationLineage(dispatch,invocation,relation,parent,root,runtime))
                | Ok _, Ok _, Ok _, Ok _, Ok _, Ok _ -> Error $"%s{label} has an invalid relation, parent invocation, or root identity"
                | values -> Error(sprintf "%A" values)
            | "event-time" ->
                match requiredText label node "invocationId", requiredText label node "event", optionalTimestamp label node "occurredAt", optionalClock label node "occurredClockProvenance", optionalTimestamp label node "observedAt", optionalClock label node "observedClockProvenance" with
                | Ok invocation, Ok event, Ok occurred, Ok occurredClock, Ok observed, Ok observedClock
                    when event = "admission" || event = "start" || event = "terminal" ->
                    make [ "invocationId"; "event"; "occurredAt"; "occurredClockProvenance"; "observedAt"; "observedClockProvenance" ] (EventTime(invocation,event,occurred,occurredClock,observed,observedClock))
                | Ok _, Ok _, Ok _, Ok _, Ok _, Ok _ -> Error $"%s{label}.event must be admission, start, or terminal"
                | values -> Error(sprintf "%A" values)
            | _ -> Error $"%s{label}.kind is unsupported"
        | values -> Error(sprintf "%A" values)

    let parseBatch (bytes: byte array) =
        if bytes.Length > MaxBatchBytes then Error [ $"batch exceeds %d{MaxBatchBytes} bytes" ] else
        try
            use document = JsonDocument.Parse bytes
            let root = document.RootElement
            if root.ValueKind <> JsonValueKind.Object then Error [ "batch must be a JSON object" ] else
            match closed "batch" (Set.ofList [ "schema"; "ingestId"; "sourceIdentity"; "generation"; "cursor"; "eventCount"; "events" ]) root,
                  requiredText "batch" root "schema", requiredText "batch" root "ingestId", requiredText "batch" root "sourceIdentity",
                  requiredText "batch" root "generation", requiredText "batch" root "cursor", requiredInt "batch" root "eventCount" with
            | Ok (), Ok schema, Ok ingestId, Ok source, Ok generation, Ok cursor, Ok declared when schema = BatchSchema ->
                if not (Regex.IsMatch(ingestId, "^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$")) then Error [ "batch.ingestId is not a safe immutable-batch identifier" ]
                elif not (Regex.IsMatch(source, "^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$")) then Error [ "batch.sourceIdentity is not a safe producer-stream identifier" ]
                else
                    match root.TryGetProperty "events" with
                    | true, events when events.ValueKind = JsonValueKind.Array && events.GetArrayLength() <= MaxEvents ->
                        let values = events.EnumerateArray() |> Seq.toList
                        if int64 values.Length <> declared then Error [ "batch.eventCount does not match events length" ] else
                        let oversize = values |> List.tryFindIndex (fun event -> Encoding.UTF8.GetByteCount(event.GetRawText()) > MaxEventBytes)
                        match oversize with
                        | Some index -> Error [ $"events[%d{index}] exceeds %d{MaxEventBytes} bytes" ]
                        | None ->
                            match values |> List.mapi parseEvent |> sequence with
                            | Error errors -> Error errors
                            | Ok facts ->
                                let duplicates = facts |> List.countBy _.Identity |> List.filter (fun (_, count) -> count > 1)
                                if not duplicates.IsEmpty then Error [ "batch contains duplicate native fact identities" ] else
                                match CanonicalJson.canonicalize bytes with
                                | Error reason -> Error [ reason ]
                                | Ok canonical -> Ok { IngestId = ingestId; SourceIdentity = source; Generation = generation; Cursor = cursor; Facts = facts; ContentDigest = CanonicalJson.sha256(Encoding.UTF8.GetBytes canonical) }
                    | true, events when events.ValueKind = JsonValueKind.Array -> Error [ $"batch exceeds %d{MaxEvents} events" ]
                    | _ -> Error [ "batch.events must be an array" ]
            | Ok (), Ok schema, _, _, _, _, _ -> Error [ $"unsupported batch schema '%s{schema}'" ]
            | values -> Error [ sprintf "%A" values ]
        with :? JsonException as error -> Error [ $"invalid JSON: %s{error.Message}" ]

    let reduce itemId (facts: Fact list) =
        let selected = facts |> List.filter (fun fact -> fact.ItemId = Some itemId || (fact.Kind = "item" && fact.Identity = itemId))
        let usage = selected |> List.choose (fun fact -> match fact.Payload with Usage(_,_,_,i,c,w,o,r,t,_,_,_) -> Some(i,c,w,o,r,t) | _ -> None)
        let add label selector = usage |> List.map selector |> List.fold (fun state value -> state |> Result.bind (fun current -> checkedAdd label current value)) (Ok 0L)
        match add "input" (fun (v,_,_,_,_,_) -> v), add "cachedInput" (fun (_,v,_,_,_,_) -> v), add "cacheWriteInput" (fun (_,_,v,_,_,_) -> v), add "output" (fun (_,_,_,v,_,_) -> v), add "total" (fun (_,_,_,_,_,v) -> v) with
        | Ok input, Ok cached, Ok write, Ok output, Ok total ->
            let reasoningValues = usage |> List.map (fun (_,_,_,_,v,_) -> v)
            let reasoning = if reasoningValues |> List.exists Option.isNone then None else reasoningValues |> List.choose id |> List.fold (fun state value -> state |> Result.bind (fun current -> checkedAdd "reasoning" current value)) (Ok 0L) |> Result.toOption
            let coverage = selected |> List.choose (fun fact -> match fact.Payload with Coverage value -> Some value | _ -> None) |> List.tryLast
            let value name getter = coverage |> Option.map getter |> Option.defaultValue name
            Ok { ItemId = itemId; FactCount = int64 selected.Length; UsageObservations = int64 usage.Length; DeliveryObservations = selected |> List.filter (fun fact -> match fact.Payload with Delivery _ -> true | _ -> false) |> List.length |> int64
                 Input = input; CachedInput = cached; CacheWriteInput = write; Output = output; Reasoning = reasoning; Total = total
                 Admitted = 0L; Started = 0L; Terminal = 0L; RuntimeUsage = 0L; MissingAdmission = 0L; MissingStart = 0L; MissingTerminal = 0L; MissingUsage = 0L
                 RecordValidity = value "unknown" _.RecordValidity; JoinIntegrity = value "unknown" _.JoinIntegrity
                 PopulationCoverage = value "unknown" _.PopulationCoverage; Qualification = value "not-evaluated" _.Qualification }
        | values -> Error [ sprintf "%A" values ]

    let publicJson aggregate =
        JsonSerializer.Serialize
            {| schema = "fsgg.telemetry.public-summary/1"; item = aggregate.ItemId; factCount = aggregate.FactCount
               usageObservations = aggregate.UsageObservations; deliveryObservations = aggregate.DeliveryObservations
               usage = {| input = aggregate.Input; cachedInput = aggregate.CachedInput; cacheWriteInput = aggregate.CacheWriteInput; output = aggregate.Output; reasoning = aggregate.Reasoning; total = aggregate.Total |}
               launcherPopulation = {| admitted = aggregate.Admitted; started = aggregate.Started; terminal = aggregate.Terminal; usage = aggregate.RuntimeUsage; missingAdmission = aggregate.MissingAdmission; missingStart = aggregate.MissingStart; missingTerminal = aggregate.MissingTerminal; missingUsage = aggregate.MissingUsage |}
               recordValidity = aggregate.RecordValidity; joinIntegrity = aggregate.JoinIntegrity
               populationCoverage = aggregate.PopulationCoverage; qualification = aggregate.Qualification |} + "\n"

    let validateStoreRoot path assessment =
        if String.IsNullOrWhiteSpace path then Error [ "store root is not configured" ]
        elif not (Path.IsPathFullyQualified path) then Error [ "store root must be an absolute path" ]
        else
            match assessment with
            | ApprovedLocalDurable -> Ok(Path.GetFullPath path)
            | Unsafe reason -> Error [ "unsafe store root: " + reason ]
            | DurabilityUnverified reason -> Error [ "store root durability-unverified: " + reason ]
