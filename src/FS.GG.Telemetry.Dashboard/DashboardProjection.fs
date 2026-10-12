namespace FS.GG.Telemetry.Dashboard

open System
open System.Collections.Generic
open System.IO
open System.IO.Compression
open System.Reflection
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open System.Text.RegularExpressions

type ProjectionError =
    | EnvelopeTooLarge
    | InvalidEnvelope
    | UnsupportedSchema
    | InvalidRevision
    | SnapshotTooLarge
    | InvalidSnapshot
    | IncompleteSnapshot
    | IncompatibleStore
    | UnauthorizedWorkspace

module DashboardProjection =
    [<Literal>]
    let Schema = "fsgg.telemetry.private-dashboard/2"

    let private envelopeLimit = 1024 * 1024
    let private snapshotLimit = 4 * envelopeLimit

    let private idPattern =
        Regex(@"\A[A-Za-z0-9][A-Za-z0-9._-]{0,127}\z", RegexOptions.CultureInvariant)

    let private tokenPattern =
        Regex(@"\A[A-Za-z0-9][A-Za-z0-9._-]{0,63}\z", RegexOptions.CultureInvariant)

    let private ciClassifications =
        set [ "useful-validation"; "admin"; "necessary-setup"; "mixed"; "unclassified" ]

    let private validItem (value: string) =
        not (String.IsNullOrWhiteSpace value)
        && value.Length <= 256
        && value |> Seq.forall (fun character -> not (Char.IsControl character))

    let private exactNames (value: JsonElement) expected =
        if value.ValueKind <> JsonValueKind.Object then
            false
        else
            let names = value.EnumerateObject() |> Seq.map _.Name |> Seq.toArray

            names.Length = (Array.distinct names |> Array.length)
            && Set.ofArray names = expected

    let private property (name: string) (value: JsonElement) =
        if value.ValueKind <> JsonValueKind.Object then
            None
        else
            match value.TryGetProperty name with
            | true, result -> Some result
            | _ -> None

    let private text name value =
        property name value
        |> Option.bind (fun v ->
            if v.ValueKind = JsonValueKind.String then
                Some(v.GetString())
            else
                None)

    let private number name value =
        property name value
        |> Option.bind (fun v ->
            if v.ValueKind <> JsonValueKind.Number then
                None
            else
                match v.TryGetInt64() with
                | true, n when n >= 0L -> Some n
                | _ -> None)

    let private signedExitCode name value =
        property name value
        |> Option.bind (fun v ->
            if v.ValueKind <> JsonValueKind.Number then None
            else
                match v.TryGetInt32() with
                | true, exitCode -> Some exitCode
                | _ -> None)

    let private nullableNumber name value =
        match property name value with
        | Some v when v.ValueKind = JsonValueKind.Null -> Some None
        | Some v when v.ValueKind = JsonValueKind.Number ->
            match v.TryGetInt64() with
            | true, n when n >= 0L -> Some(Some n)
            | _ -> None
        | Some _ -> None
        | None -> None

    let private safeToken (fallback: string) (value: string option) =
        match value with
        | Some v when tokenPattern.IsMatch v -> v
        | _ -> fallback

    let private safeTime (value: string option) =
        match value with
        | Some text ->
            match DateTimeOffset.TryParse text with
            | true, time -> Some(time.ToUniversalTime().ToString("O"))
            | _ -> None
        | None -> None

    // These references are private source-native identifiers, never display ordinals.
    let private reference name row =
        text name row |> Option.filter (fun value -> validItem value)
        |> Option.map JsonValue.Create |> Option.toObj

    let private sourceFields (node: JsonObject) row =
        node["factIdentity"] <- reference "identity" row
        node["factRevision"] <- number "fact_revision" row |> Option.map JsonValue.Create |> Option.toObj
        node["contentDigest"] <- null // This scoped reader does not supply retained canonical digests.

    let private itemSteps (perKindLimit: int) item activities attributions ciSteps admissions terminals times usage lineage =
        let activityRows = activities |> Array.filter (fun row -> text "item_id" row = Some item)
        let ciRows = ciSteps |> Array.filter (fun row -> text "item_id" row = Some item)
        let runtimeRows = admissions |> Array.filter (fun row -> text "item_id" row = Some item)
        let result = JsonObject()
        result["schema"] <- "fsgg.telemetry.private-item-steps/1"
        result["limitPerKind"] <- JsonValue.Create perKindLimit
        result["activityCount"] <- activityRows.Length
        result["ciStepCount"] <- ciRows.Length
        result["runtimeCount"] <- runtimeRows.Length
        let steps = JsonArray()

        for index, row in runtimeRows |> Array.truncate perKindLimit |> Array.indexed do
            let invocation = text "invocation_id" row
            let events =
                times
                |> Array.filter (fun event ->
                    invocation.IsSome && text "item_id" event = Some item && text "invocation_id" event = invocation)
            let latest eventName = events |> Array.tryFind (fun event -> text "event" event = Some eventName)
            let start = latest "start" |> Option.bind (text "occurred_at") |> safeTime
            let finish = latest "terminal" |> Option.bind (text "occurred_at") |> safeTime
            let startClock = latest "start" |> Option.bind (text "occurred_clock_provenance")
            let finishClock = latest "terminal" |> Option.bind (text "occurred_clock_provenance")
            let sameClock = startClock.IsSome && startClock = finishClock
            let invocationUsage =
                usage
                |> Array.filter (fun entry ->
                    invocation.IsSome && text "item_id" entry = Some item && text "invocation_id" entry = invocation)
            let bases =
                invocationUsage
                |> Array.choose (fun entry ->
                    text "accounting_scope" entry
                    |> Option.map (fun scope -> text "provider" entry, scope))
                |> Array.distinct
            let totals = invocationUsage |> Array.choose (number "total")
            let observedTokens =
                if totals.Length = 0 || totals.Length <> invocationUsage.Length
                   || bases.Length <> 1
                   || (invocationUsage |> Array.choose (text "accounting_scope")).Length <> invocationUsage.Length then None
                else
                    let total = totals |> Array.sumBy bigint
                    if total > bigint Int64.MaxValue then None else Some(int64 total)
            let relation =
                lineage
                |> Array.tryFind (fun entry ->
                    invocation.IsSome && text "item_id" entry = Some item && text "invocation_id" entry = invocation)
                |> Option.bind (text "relation")
                |> safeToken "unclassified"
            let node = JsonObject()
            sourceFields node row
            node["id"] <- reference "invocation_id" row
            node["attemptId"] <- reference "attempt_id" row
            node["parentAttemptId"] <- reference "parent_attempt_id" row
            let terminal = terminals |> Array.tryFind (fun entry -> invocation.IsSome && text "item_id" entry = Some item && text "invocation_id" entry = invocation)
            node["outcome"] <- terminal |> Option.bind (text "outcome") |> safeToken "unknown"
            node["terminalIdentity"] <- terminal |> Option.map (reference "identity") |> Option.toObj
            node["exitCode"] <- terminal |> Option.bind (signedExitCode "exit_code") |> Option.map JsonValue.Create |> Option.toObj
            let lineageRow = lineage |> Array.tryFind (fun entry -> invocation.IsSome && text "item_id" entry = Some item && text "invocation_id" entry = invocation)
            node["parentId"] <- lineageRow |> Option.map (reference "parent_invocation_id") |> Option.toObj
            node["rootId"] <- lineageRow |> Option.map (reference "root_invocation_id") |> Option.toObj
            node["lineageIdentity"] <- lineageRow |> Option.map (reference "identity") |> Option.toObj
            node["kind"] <- "runtime"
            node["label"] <- $"Runtime invocation {index + 1}"
            node["classification"] <- relation
            node["clock"] <- if sameClock then safeToken "unknown" startClock else "unknown"
            node["startedAt"] <- if sameClock then start |> Option.map JsonValue.Create |> Option.toObj else null
            node["endedAt"] <- if sameClock then finish |> Option.map JsonValue.Create |> Option.toObj else null
            node["tokens"] <- observedTokens |> Option.map JsonValue.Create |> Option.toObj
            node["tokenBasis"] <- if observedTokens.IsNone then "unknown" else "observed-native-partial"
            steps.Add node

        for row in activityRows |> Array.truncate perKindLimit do
            let category = safeToken "unclassified" (text "category" row)
            let start = text "started_at" row |> safeTime
            let finish = text "ended_at" row |> safeTime
            let matching =
                match text "activity_id" row with
                | None -> [||]
                | Some activityId ->
                    attributions
                    |> Array.filter (fun attribution ->
                        text "item_id" attribution = Some item
                        && text "classification" attribution = Some "direct"
                        && text "activity_id" attribution = Some activityId)
            let attributed =
                matching
                |> Array.choose (fun attribution ->
                    match text "usage_identity" attribution with
                    | None -> None
                    | Some identity ->
                        usage
                        |> Array.tryFind (fun entry ->
                            text "item_id" entry = Some item
                            && text "identity" entry = Some identity)
                        |> Option.bind (fun entry ->
                            match number "total" attribution, number "total" entry, text "accounting_scope" entry with
                            | Some attributedTotal, Some nativeTotal, Some scope when attributedTotal = nativeTotal ->
                                Some(attributedTotal, text "provider" entry, scope)
                            | _ -> None))
            let attributedTotal =
                if matching.Length = 0 || attributed.Length <> matching.Length
                   || (attributed |> Array.map (fun (_, provider, scope) -> provider, scope) |> Array.distinct).Length <> 1 then None
                else
                    let total = attributed |> Array.sumBy (fun (value, _, _) -> bigint value)
                    if total > bigint Int64.MaxValue then None else Some(int64 total)
            let node = JsonObject()
            sourceFields node row
            node["id"] <- reference "activity_id" row
            node["invocationId"] <- reference "invocation_id" row
            node["attemptId"] <- reference "attempt_id" row
            node["parentId"] <- null
            node["rootId"] <- null
            node["kind"] <- "activity"
            node["label"] <- category
            node["classification"] <- category
            node["clock"] <- safeToken "unknown" (text "clock_provenance" row)
            node["startedAt"] <- start |> Option.map JsonValue.Create |> Option.toObj
            node["endedAt"] <- finish |> Option.map JsonValue.Create |> Option.toObj
            node["tokens"] <- attributedTotal |> Option.map JsonValue.Create |> Option.toObj
            node["tokenBasis"] <- if attributedTotal.IsNone then "unknown" else "direct-attribution-partial"
            steps.Add node

        for index, row in ciRows |> Array.truncate perKindLimit |> Array.indexed do
            let node = JsonObject()
            sourceFields node row
            node["id"] <- reference "identity" row
            node["parentId"] <- null
            node["rootId"] <- null
            for name in [ "run_id"; "job_id"; "attempt"; "number" ] do
                node[name] <- number name row |> Option.map JsonValue.Create |> Option.toObj
            node["status"] <- safeToken "unknown" (text "status" row)
            node["conclusion"] <- safeToken "unknown" (text "conclusion" row)
            node["kind"] <- "ci"
            node["label"] <- $"CI step {index + 1}"
            node["classification"] <-
                match text "classification" row with
                | Some value when ciClassifications.Contains value -> value
                | _ -> "unclassified"
            node["clock"] <- "github-actions"
            node["startedAt"] <- text "started_at" row |> safeTime |> Option.map JsonValue.Create |> Option.toObj
            node["endedAt"] <- text "completed_at" row |> safeTime |> Option.map JsonValue.Create |> Option.toObj
            node["tokens"] <- null
            node["tokenBasis"] <- "not-applicable"
            steps.Add node

        result["rows"] <- steps
        result["truncated"] <-
            activityRows.Length > perKindLimit
            || ciRows.Length > perKindLimit
            || runtimeRows.Length > perKindLimit
        result

    let private array name root =
        match property name root with
        | Some value when value.ValueKind = JsonValueKind.Array -> Some(value.EnumerateArray() |> Seq.toArray)
        | _ -> None

    let private latestByItem item rows =
        rows |> Array.tryFind (fun row -> text "item_id" row = Some item)

    let private readBoundedGzip compressed =
        try
            use input = new MemoryStream(compressed, false)
            use gzip = new GZipStream(input, CompressionMode.Decompress, false)
            use output = new MemoryStream()
            let buffer = Array.zeroCreate<byte> 8192
            let mutable total = 0
            let mutable finished = false

            while not finished && total <= snapshotLimit do
                let count = gzip.Read(buffer, 0, buffer.Length)

                if count = 0 then
                    finished <- true
                else
                    output.Write(buffer, 0, count)
                    total <- total + count

            if total > snapshotLimit then
                Error SnapshotTooLarge
            else
                Ok(output.ToArray())
        with _ ->
            Error InvalidRevision

    let private projectItem
        summaries
        populations
        dirty
        outcomes
        gaps
        ciCoverage
        populationCoverage
        times
        activities
        attributions
        ciSteps
        admissions
        terminals
        usageRows
        lineage
        perKindLimit
        item
        =
        match summaries |> Array.tryFind (fun summary -> text "item" summary = Some item) with
        | None -> Error InvalidSnapshot
        | Some summary ->
            let required =
                set
                    [
                        "schema"
                        "item"
                        "factCount"
                        "usageObservations"
                        "deliveryObservations"
                        "usage"
                        "launcherPopulation"
                        "recordValidity"
                        "joinIntegrity"
                        "populationCoverage"
                        "qualification"
                    ]

            match property "usage" summary, property "launcherPopulation" summary with
            | Some usage, Some launcher when
                exactNames summary required
                && text "schema" summary = Some "fsgg.telemetry.public-summary/1"
                ->
                let usageNames =
                    set["input"
                        "cachedInput"
                        "cacheWriteInput"
                        "output"
                        "reasoning"
                        "total"]

                let launcherNames =
                    set["admitted"
                        "started"
                        "terminal"
                        "usage"
                        "missingAdmission"
                        "missingStart"
                        "missingTerminal"
                        "missingUsage"]

                let usageValues =
                    [ "input"; "cachedInput"; "cacheWriteInput"; "output"; "total" ]
                    |> List.map (fun n -> nullableNumber n usage)

                let launcherValues =
                    [
                        "admitted"
                        "started"
                        "terminal"
                        "usage"
                        "missingAdmission"
                        "missingStart"
                        "missingTerminal"
                        "missingUsage"
                    ]
                    |> List.map (fun n -> number n launcher)

                let aggregateValues =
                    [ "factCount"; "usageObservations"; "deliveryObservations" ]
                    |> List.map (fun n -> number n summary)

                if
                    not (exactNames usage usageNames && exactNames launcher launcherNames)
                    || aggregateValues |> List.exists Option.isNone
                    || usageValues |> List.exists Option.isNone
                    || launcherValues |> List.exists Option.isNone
                    || nullableNumber "reasoning" usage |> Option.isNone
                then
                    Error InvalidSnapshot
                else
                    let populationFact = latestByItem item populations

                    let population =
                        populationFact
                        |> Option.bind (text "state")
                        |> safeToken "missing"

                    let originalItemId =
                        populationFact
                        |> Option.bind (text "original_item_id")
                        |> Option.filter validItem

                    let outcome = latestByItem item outcomes
                    let ci = latestByItem item ciCoverage
                    let pci = latestByItem item populationCoverage

                    let clocks =
                        Array.append
                            (times
                             |> Array.choose (fun row ->
                                 if text "item_id" row = Some item then
                                     text "occurred_clock_provenance" row |> Option.filter tokenPattern.IsMatch
                                 else
                                     None))
                            (activities
                             |> Array.choose (fun row ->
                                 if text "item_id" row = Some item then
                                     text "clock_provenance" row |> Option.filter tokenPattern.IsMatch
                                 else
                                     None))
                        |> Array.distinct
                        |> Array.sort

                    let gapCodes =
                        gaps
                        |> Array.choose (fun row ->
                            if text "item_id" row = Some item then
                                text "code" row |> Option.filter tokenPattern.IsMatch
                            else
                                None)
                        |> Array.distinct
                        |> Array.sort

                    let node = JsonObject()
                    node["id"] <- item
                    node["unit"] <- "canonical-item"
                    node["factCount"] <- number "factCount" summary |> Option.get
                    node["usageObservations"] <- number "usageObservations" summary |> Option.get
                    node["deliveryObservations"] <- number "deliveryObservations" summary |> Option.get
                    let usageNode = JsonObject()

                    for name in [ "input"; "cachedInput"; "cacheWriteInput"; "output"; "total" ] do
                        usageNode[name] <-
                            match nullableNumber name usage |> Option.get with
                            | Some n -> JsonValue.Create n
                            | None -> null

                    usageNode["reasoning"] <-
                        match nullableNumber "reasoning" usage |> Option.get with
                        | Some n -> JsonValue.Create n
                        | None -> null

                    usageNode["nativeUsage"] <-
                        if gapCodes |> Array.contains "native-usage-unsupported" then
                            "unsupported"
                        elif gapCodes.Length > 0 then
                            "incomplete"
                        elif (number "usageObservations" summary |> Option.get) > 0L then
                            "observed"
                        else
                            "missing"

                    node["usage"] <- usageNode
                    let runtime = JsonObject()

                    for name in
                        [
                            "admitted"
                            "started"
                            "terminal"
                            "usage"
                            "missingAdmission"
                            "missingStart"
                            "missingTerminal"
                            "missingUsage"
                        ] do
                        runtime[name] <- number name launcher |> Option.get

                    runtime["gapCodes"] <- JsonArray(gapCodes |> Array.map (fun v -> JsonValue.Create(v) :> JsonNode))
                    node["runtime"] <- runtime
                    let state = JsonObject()
                    state["population"] <- population
                    state["originalItemId"] <-
                        match originalItemId with
                        | Some original -> JsonValue.Create original
                        | None -> null

                    state["memberRelation"] <-
                        match originalItemId with
                        | Some original when original = item -> "original"
                        | Some _ -> "member"
                        | None -> "unknown"
                    state["dirty"] <- dirty |> Array.exists (fun row -> text "item_id" row = Some item)
                    state["outcome"] <- safeToken "missing" (outcome |> Option.bind (text "outcome"))
                    state["codeDelivery"] <- safeToken "unknown" (outcome |> Option.bind (text "code_delivery"))

                    state["observedAt"] <-
                        match outcome |> Option.bind (text "observed_at") |> safeTime with
                        | Some v -> JsonValue.Create v
                        | None -> null

                    node["state"] <- state
                    let coverage = JsonObject()

                    for name in [ "recordValidity"; "joinIntegrity"; "populationCoverage"; "qualification" ] do
                        coverage[name] <- safeToken "unknown" (text name summary)

                    coverage["ciInventory"] <-
                        safeToken
                            "unknown"
                            (pci
                             |> Option.bind (text "actions")
                             |> Option.orElseWith (fun () -> ci |> Option.bind (text "inventory")))

                    coverage["ciChecks"] <- safeToken "unknown" (pci |> Option.bind (text "checks"))

                    coverage["ciAttempts"] <-
                        safeToken
                            "unknown"
                            (pci
                             |> Option.bind (text "attempts")
                             |> Option.orElseWith (fun () -> ci |> Option.bind (text "attempts")))

                    coverage["ciJobs"] <-
                        safeToken
                            "unknown"
                            (pci
                             |> Option.bind (text "jobs")
                             |> Option.orElseWith (fun () -> ci |> Option.bind (text "job_pages")))

                    coverage["ciTerminal"] <-
                        safeToken
                            "unknown"
                            (pci
                             |> Option.bind (text "terminal")
                             |> Option.orElseWith (fun () -> ci |> Option.bind (text "terminal")))

                    coverage["ciTimestamps"] <-
                        safeToken
                            "unknown"
                            (pci
                             |> Option.bind (text "timestamps")
                             |> Option.orElseWith (fun () -> ci |> Option.bind (text "timestamps")))

                    coverage["ciContinuation"] <- safeToken "none" (pci |> Option.bind (text "continuation"))

                    coverage["externalChecks"] <-
                        match pci |> Option.bind (number "external_checks") with
                        | Some value -> JsonValue.Create value
                        | None -> null

                    node["coverage"] <- coverage
                    node["clockProvenance"] <- JsonArray(clocks |> Array.map (fun v -> JsonValue.Create(v) :> JsonNode))
                    let steps = itemSteps perKindLimit item activities attributions ciSteps admissions terminals times usageRows lineage
                    node["steps"] <- steps
                    let health = JsonObject()
                    health["schema"] <- "fsgg.telemetry.process-health/1"
                    health["verdict"] <- "insufficient-evidence"
                    health["confidence"] <- "insufficient"
                    health["reason"] <- "Diagnostic assessment, dependency coverage, active effort and retained history are not supplied by this snapshot."
                    health["dimensions"] <- JsonArray([| "delivery"; "rework"; "coordination"; "cost"; "reliability" |] |> Array.map (fun dimension ->
                        let value = JsonObject()
                        value["dimension"] <- dimension
                        value["verdict"] <- "insufficient-evidence"
                        value :> JsonNode))
                    health["effortCategories"] <- JsonArray([| "product-implementation"; "necessary-validation"; "tooling-infrastructure-repair"; "coordination-approvals"; "repeated-work"; "external-waiting"; "unknown-unattributed" |] |> Array.map (fun category ->
                        let value = JsonObject()
                        value["category"] <- category
                        value["amount"] <- null
                        value["unit"] <- null
                        value["coverage"] <- "unknown"
                        value :> JsonNode))
                    let stages = JsonObject()
                    stages["source"] <- state["codeDelivery"].DeepClone()
                    for stage in [ "publication"; "installation"; "adoption"; "verifiedOperation" ] do stages[stage] <- "unknown"
                    health["stages"] <- stages
                    let diagnosticCoverage = JsonObject()
                    diagnosticCoverage["population"] <- coverage["populationCoverage"].DeepClone()
                    diagnosticCoverage["usage"] <- usageNode["nativeUsage"].DeepClone()
                    diagnosticCoverage["lineage"] <- if lineage |> Array.exists (fun row -> text "item_id" row = Some item) then "observed-partial" else "unknown"
                    for relation in [ "classification"; "dependencies"; "history"; "operationalStages" ] do diagnosticCoverage[relation] <- "unknown"
                    health["coverage"] <- diagnosticCoverage
                    let evidence = JsonObject()
                    match outcome with
                    | Some row ->
                        sourceFields evidence row
                        evidence["sourceReference"] <- reference "source_ref" row
                        evidence["repository"] <- reference "repository" row
                        evidence["pullRequest"] <- number "pr_number" row |> Option.map JsonValue.Create |> Option.toObj
                        evidence["head"] <- reference "head" row
                        evidence["mergeCommit"] <- reference "merge_commit" row
                    | None ->
                        for name in [ "factIdentity"; "factRevision"; "contentDigest"; "sourceReference"; "repository"; "pullRequest"; "head"; "mergeCommit" ] do evidence[name] <- null
                    health["sourceEvidence"] <- evidence
                    let edges = JsonArray()
                    let rows = steps["rows"].AsArray()
                    for row in rows do
                        if row["kind"].GetValue<string>() = "runtime" && not (isNull row["parentId"]) then
                            let edge = JsonObject()
                            edge["from"] <- row["parentId"].DeepClone()
                            edge["to"] <- if isNull row["id"] then null else row["id"].DeepClone()
                            edge["relation"] <- row["classification"].DeepClone()
                            edge["evidenceIdentity"] <- if isNull row["lineageIdentity"] then null else row["lineageIdentity"].DeepClone()
                            edge["targetCoverage"] <- if rows |> Seq.exists (fun candidate -> not (isNull candidate["id"]) && candidate["kind"].GetValue<string>() = "runtime" && candidate["id"].ToJsonString() = row["parentId"].ToJsonString()) then "displayed" else "unresolved"
                            edges.Add edge
                    health["edges"] <- edges
                    health["gaps"] <- JsonArray(Array.append gapCodes [| "diagnostic-assessment-unavailable"; "active-effort-unavailable"; "seven-category-allocation-unavailable"; "dependency-coverage-unknown"; "historical-knowledge-time-unavailable"; "operational-stage-evidence-unavailable"; "canonical-digests-unavailable"; "source-freshness-coverage-unknown" |] |> Array.distinct |> Array.map (fun value -> JsonValue.Create(value) :> JsonNode))
                    health["anomalies"] <- null
                    health["interventions"] <- null
                    node["processHealth"] <- health
                    Ok(node :> JsonNode)
            | _ -> Error InvalidSnapshot

    let project (authorizedWorkspace: string) (canonicalSnapshotEnvelopeBytes: byte array) =
        let bytes = canonicalSnapshotEnvelopeBytes

        if isNull authorizedWorkspace || not (idPattern.IsMatch authorizedWorkspace) then
            Error UnauthorizedWorkspace
        elif isNull bytes || bytes.Length > envelopeLimit then
            Error EnvelopeTooLarge
        else
            try
                use envelopeDocument =
                    JsonDocument.Parse(
                        ReadOnlyMemory<byte>(bytes),
                        JsonDocumentOptions(
                            AllowTrailingCommas = false,
                            CommentHandling = JsonCommentHandling.Disallow,
                            MaxDepth = 8
                        )
                    )

                let envelope = envelopeDocument.RootElement

                let names =
                    set["schema"
                        "workspaceId"
                        "observedAt"
                        "revision"
                        "canonicalSnapshotGzip"
                        "operational"]

                if not (exactNames envelope names) then
                    Error InvalidEnvelope
                elif text "schema" envelope <> Some "fsgg.telemetry.item-detail/2" then
                    Error UnsupportedSchema
                elif text "workspaceId" envelope <> Some authorizedWorkspace then
                    Error UnauthorizedWorkspace
                else
                    match
                        text "revision" envelope, text "canonicalSnapshotGzip" envelope, property "operational" envelope
                    with
                    | Some revision, Some encoded, Some operational when Regex.IsMatch(revision, @"\A[0-9a-f]{64}\z") ->
                        let observedAt = text "observedAt" envelope |> safeTime
                        let pendingBatches = number "pendingBatches" operational
                        let consistency = text "consistency" operational

                        let scopedOperational =
                            exactNames
                                operational
                                (set["pendingBatches"
                                     "appliedReceipts"
                                     "rejectedReceipts"
                                     "consistency"])
                            && consistency = Some "database-transaction"

                        let appliedReceipts =
                            if scopedOperational then
                                number "appliedReceipts" operational
                            else
                                None

                        let rejectedReceipts =
                            if scopedOperational then
                                number "rejectedReceipts" operational
                            else
                                None

                        if
                            observedAt.IsNone
                            || pendingBatches.IsNone
                            || not scopedOperational || appliedReceipts.IsNone || rejectedReceipts.IsNone
                        then
                            Error InvalidEnvelope
                        else
                            let compressed =
                                try
                                    Ok(Convert.FromBase64String encoded)
                                with _ ->
                                    Error InvalidRevision

                            match compressed |> Result.bind readBoundedGzip with
                            | Error error -> Error error
                            | Ok canonical when
                                Convert.ToHexString(SHA256.HashData canonical).ToLowerInvariant() <> revision
                                ->
                                Error InvalidRevision
                            | Ok canonical ->
                                try
                                    use snapshotDocument =
                                        JsonDocument.Parse(
                                            ReadOnlyMemory<byte>(canonical),
                                            JsonDocumentOptions(
                                                AllowTrailingCommas = false,
                                                CommentHandling = JsonCommentHandling.Disallow,
                                                MaxDepth = 32
                                            )
                                        )

                                    let snapshot = snapshotDocument.RootElement

                                    let expected =
                                        set["workspaceId"
                                            "learningSnapshotSchema"
                                            "selection"
                                            "store"
                                            "items"
                                            "summaries"
                                            "populations"
                                            "dirtyItems"
                                            "outcomes"
                                            "admissions"
                                            "starts"
                                            "terminals"
                                            "expectedDispatches"
                                            "lineage"
                                            "times"
                                            "usage"
                                            "runtimeGaps"
                                            "ciRuns"
                                            "ciJobs"
                                            "ciSteps"
                                            "ciCoverage"
                                            "ciPopulationCoverage"
                                            "budgetAssessments"
                                            "budgetMembership"
                                            "budgetEpochs"
                                            "budgetBreaches"
                                            "budgetInterventions"
                                            "activities"
                                            "activityUsageAttributions"
                                            "complications"
                                            "reviews"
                                            "learningObservations"]

                                    let supportedShape =
                                        exactNames snapshot (Set.add "responseUsage" expected)
                                        && text "learningSnapshotSchema" snapshot = Some "fsgg.telemetry.learn-item-detail/4"
                                        && text "workspaceId" snapshot = Some authorizedWorkspace

                                    match property "selection" snapshot, property "store" snapshot with
                                    | Some selection, Some store when
                                        supportedShape
                                        && (text "mode" selection
                                            |> Option.exists (fun mode -> mode = "all" || mode = "item"))
                                        && property "complete" selection
                                           |> Option.exists (fun v -> v.ValueKind = JsonValueKind.True)
                                        ->
                                        match number "schemaVersion" store, text "journalMode" store with
                                        | Some 14L, Some "wal" ->
                                            let arrayNames =
                                                [
                                                    "items"
                                                    "summaries"
                                                    "populations"
                                                    "dirtyItems"
                                                    "outcomes"
                                                    "admissions"
                                                    "starts"
                                                    "terminals"
                                                    "expectedDispatches"
                                                    "lineage"
                                                    "times"
                                                    "usage"
                                                    "runtimeGaps"
                                                    "ciRuns"
                                                    "ciJobs"
                                                    "ciSteps"
                                                    "ciCoverage"
                                                    "ciPopulationCoverage"
                                                    "budgetAssessments"
                                                    "budgetMembership"
                                                    "budgetEpochs"
                                                    "budgetBreaches"
                                                    "budgetInterventions"
                                                    "activities"
                                                    "activityUsageAttributions"
                                                    "complications"
                                                    "reviews"
                                                ]

                                            let arrayNames = "responseUsage" :: "learningObservations" :: arrayNames

                                            let allArrays = arrayNames |> List.map (fun n -> array n snapshot)

                                            let requiredArrays =
                                                [
                                                    "items"
                                                    "summaries"
                                                    "populations"
                                                    "dirtyItems"
                                                    "outcomes"
                                                    "runtimeGaps"
                                                    "ciCoverage"
                                                    "ciPopulationCoverage"
                                                    "times"
                                                    "activities"
                                                ]
                                                |> List.map (fun n -> array n snapshot)

                                            if allArrays |> List.exists Option.isNone then
                                                Error InvalidSnapshot
                                            else
                                                let values = requiredArrays |> List.map Option.get

                                                if
                                                    allArrays
                                                    |> List.map Option.get
                                                    |> List.exists (fun rows -> rows.Length > 10000)
                                                then
                                                    Error InvalidSnapshot
                                                else
                                                    let itemElements = values[0]

                                                    let items =
                                                        itemElements
                                                        |> Array.choose (fun v ->
                                                            if
                                                                v.ValueKind = JsonValueKind.String
                                                                && validItem (v.GetString())
                                                            then
                                                                Some(v.GetString())
                                                            else
                                                                None)

                                                    if
                                                        items.Length <> itemElements.Length
                                                        || items.Length > 200
                                                        || items.Length <> (Array.distinct items |> Array.length)
                                                        || values[1].Length <> items.Length
                                                    then
                                                        Error InvalidSnapshot
                                                    else
                                                        let projected =
                                                            items
                                                            |> Array.map (
                                                                projectItem
                                                                    values[1]
                                                                    values[2]
                                                                    values[3]
                                                                    values[4]
                                                                    values[5]
                                                                    values[6]
                                                                    values[7]
                                                                    values[8]
                                                                    values[9]
                                                                    (array "activityUsageAttributions" snapshot |> Option.get)
                                                                    (array "ciSteps" snapshot |> Option.get)
                                                                    (array "admissions" snapshot |> Option.get)
                                                                    (array "terminals" snapshot |> Option.get)
                                                                    (array "usage" snapshot |> Option.get)
                                                                    (array "lineage" snapshot |> Option.get)
                                                                    (min 64 (max 1 (128 / max 1 items.Length)))
                                                            )

                                                        match
                                                            projected
                                                            |> Array.tryPick (function
                                                                | Error e -> Some e
                                                                | _ -> None)
                                                        with
                                                        | Some error -> Error error
                                                        | None ->
                                                            let result = JsonObject()
                                                            result["schema"] <- Schema
                                                            result["workspaceId"] <- authorizedWorkspace
                                                            result["observedAt"] <- observedAt.Value
                                                            result["revision"] <- revision
                                                            let provenance = JsonObject()
                                                            provenance["schema"] <- "fsgg.telemetry.process-health/1"
                                                            provenance["verdict"] <- "insufficient-evidence"
                                                            provenance["selectionMode"] <- text "mode" selection |> Option.get
                                                            provenance["requestedItem"] <- reference "requestedItem" selection
                                                            provenance["sourceObservedAt"] <- null
                                                            provenance["snapshotObservedAt"] <- observedAt.Value
                                                            provenance["ingestedAt"] <- null
                                                            provenance["projectionAt"] <- DateTimeOffset.UtcNow.ToString("O")
                                                            provenance["cutoff"] <- null
                                                            provenance["timezone"] <- null
                                                            provenance["sessionId"] <- null
                                                            provenance["configVersion"] <- null
                                                            provenance["ruleVersion"] <- null
                                                            provenance["classificationVersion"] <- null
                                                            result["processHealth"] <- provenance
                                                            let op = JsonObject()
                                                            op["pendingBatches"] <- pendingBatches.Value

                                                            op["appliedReceipts"] <-
                                                                match appliedReceipts with
                                                                | Some value -> JsonValue.Create value
                                                                | None -> null

                                                            op["rejectedReceipts"] <-
                                                                match rejectedReceipts with
                                                                | Some value -> JsonValue.Create value
                                                                | None -> null

                                                            op["consistency"] <- consistency.Value
                                                            result["operational"] <- op

                                                            result["items"] <-
                                                                JsonArray(
                                                                    projected
                                                                    |> Array.choose (function
                                                                        | Ok n -> Some n
                                                                        | _ -> None)
                                                                )

                                                            let output =
                                                                Encoding.UTF8.GetBytes(
                                                                    result.ToJsonString(
                                                                        JsonSerializerOptions(WriteIndented = false)
                                                                    )
                                                                )

                                                            if output.Length > envelopeLimit then
                                                                Error SnapshotTooLarge
                                                            else
                                                                Ok output
                                        | _ -> Error IncompatibleStore
                                    | _ -> Error IncompleteSnapshot
                                with _ ->
                                    Error InvalidSnapshot
                    | _ -> Error InvalidEnvelope
            with _ ->
                Error InvalidEnvelope

type Asset =
    {
        ContentType: string
        Bytes: byte array
    }

module DashboardAssets =
    let private assembly = Assembly.GetExecutingAssembly()

    let private read name =
        use stream =
            assembly.GetManifestResourceStream("FS.GG.Telemetry.Dashboard.Assets." + name)

        use memory = new MemoryStream()
        stream.CopyTo memory
        memory.ToArray()

    let private assets =
        Map.ofList
            [
                "/private/dashboard/",
                {
                    ContentType = "text/html; charset=utf-8"
                    Bytes = read "index.html"
                }
                "/private/dashboard/app.js",
                {
                    ContentType = "text/javascript; charset=utf-8"
                    Bytes = read "app.js"
                }
                "/private/dashboard/styles.css",
                {
                    ContentType = "text/css; charset=utf-8"
                    Bytes = read "styles.css"
                }
            ]

    let tryGet route =
        assets
        |> Map.tryFind route
        |> Option.map (fun asset ->
            { asset with
                Bytes = Array.copy asset.Bytes
            })

    let private localIndex =
        let html = read "index.html" |> Encoding.UTF8.GetString

        html
            .Replace(
                "<meta name=\"referrer\" content=\"no-referrer\">",
                "<meta name=\"referrer\" content=\"no-referrer\"><meta name=\"fsgg-dashboard-mode\" content=\"local\">"
            )
            .Replace("/private/dashboard/styles.css", "/styles.css")
            .Replace("/private/dashboard/app.js", "/app.js")
        |> Encoding.UTF8.GetBytes

    let private localAssets =
        Map.ofList
            [
                "/",
                {
                    ContentType = "text/html; charset=utf-8"
                    Bytes = localIndex
                }
                "/index.html",
                {
                    ContentType = "text/html; charset=utf-8"
                    Bytes = localIndex
                }
                "/app.js",
                {
                    ContentType = "text/javascript; charset=utf-8"
                    Bytes = read "app.js"
                }
                "/styles.css",
                {
                    ContentType = "text/css; charset=utf-8"
                    Bytes = read "styles.css"
                }
            ]

    let tryGetLocal route =
        localAssets
        |> Map.tryFind route
        |> Option.map (fun asset ->
            { asset with
                Bytes = Array.copy asset.Bytes
            })
