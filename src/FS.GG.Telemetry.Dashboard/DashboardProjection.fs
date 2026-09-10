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
    let Schema = "fsgg.telemetry.private-dashboard/1"

    let private envelopeLimit = 1024 * 1024
    let private snapshotLimit = 4 * envelopeLimit
    let private idPattern = Regex(@"\A[A-Za-z0-9][A-Za-z0-9._-]{0,127}\z", RegexOptions.CultureInvariant)
    let private tokenPattern = Regex(@"\A[A-Za-z0-9][A-Za-z0-9._-]{0,63}\z", RegexOptions.CultureInvariant)
    let private validItem (value:string) =
        not(String.IsNullOrWhiteSpace value) && value.Length<=256 && value |> Seq.forall(fun character->not(Char.IsControl character))
    let private exactNames (value:JsonElement) expected =
        if value.ValueKind <> JsonValueKind.Object then false
        else
            let names = value.EnumerateObject() |> Seq.map _.Name |> Seq.toArray
            names.Length = (Array.distinct names |> Array.length) && Set.ofArray names = expected
    let private property (name:string) (value:JsonElement) =
        if value.ValueKind<>JsonValueKind.Object then None
        else match value.TryGetProperty name with true,result -> Some result | _ -> None
    let private text name value = property name value |> Option.bind (fun v -> if v.ValueKind=JsonValueKind.String then Some(v.GetString()) else None)
    let private number name value = property name value |> Option.bind (fun v -> if v.ValueKind<>JsonValueKind.Number then None else match v.TryGetInt64() with true,n when n>=0L -> Some n | _ -> None)
    let private nullableNumber name value =
        match property name value with
        | Some v when v.ValueKind=JsonValueKind.Null -> Some None
        | Some v when v.ValueKind=JsonValueKind.Number -> match v.TryGetInt64() with true,n when n>=0L -> Some(Some n) | _ -> None
        | Some _ -> None
        | None -> None
    let private safeToken (fallback:string) (value:string option) = match value with Some v when tokenPattern.IsMatch v -> v | _ -> fallback
    let private safeTime (value:string option) =
        match value with
        | Some text -> match DateTimeOffset.TryParse text with true,time -> Some(time.ToUniversalTime().ToString("O")) | _ -> None
        | None -> None
    let private array name root =
        match property name root with Some value when value.ValueKind=JsonValueKind.Array -> Some(value.EnumerateArray() |> Seq.toArray) | _ -> None
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
                let count = gzip.Read(buffer,0,buffer.Length)
                if count = 0 then finished <- true else output.Write(buffer,0,count); total <- total + count
            if total > snapshotLimit then Error SnapshotTooLarge else Ok(output.ToArray())
        with _ -> Error InvalidRevision

    let private projectItem summaries populations dirty outcomes gaps ciCoverage populationCoverage times activities item =
        match summaries |> Array.tryFind (fun summary -> text "item" summary = Some item) with
        | None -> Error InvalidSnapshot
        | Some summary ->
            let required = set ["schema";"item";"factCount";"usageObservations";"deliveryObservations";"usage";"launcherPopulation";"recordValidity";"joinIntegrity";"populationCoverage";"qualification"]
            match property "usage" summary, property "launcherPopulation" summary with
            | Some usage, Some launcher when exactNames summary required && text "schema" summary=Some "fsgg.telemetry.public-summary/1" ->
                let usageNames=set["input";"cachedInput";"cacheWriteInput";"output";"reasoning";"total"]
                let launcherNames=set["admitted";"started";"terminal";"usage";"missingAdmission";"missingStart";"missingTerminal";"missingUsage"]
                let usageValues=["input";"cachedInput";"cacheWriteInput";"output";"total"] |> List.map(fun n->number n usage)
                let launcherValues=["admitted";"started";"terminal";"usage";"missingAdmission";"missingStart";"missingTerminal";"missingUsage"] |> List.map(fun n->number n launcher)
                let aggregateValues=["factCount";"usageObservations";"deliveryObservations"] |> List.map(fun n->number n summary)
                if not(exactNames usage usageNames && exactNames launcher launcherNames) || aggregateValues |> List.exists Option.isNone || usageValues |> List.exists Option.isNone || launcherValues |> List.exists Option.isNone || nullableNumber "reasoning" usage |> Option.isNone then Error InvalidSnapshot else
                let population = latestByItem item populations |> Option.bind(text "state") |> safeToken "missing"
                let outcome = latestByItem item outcomes
                let ci = latestByItem item ciCoverage
                let pci = latestByItem item populationCoverage
                let clocks =
                    Array.append
                        (times |> Array.choose(fun row -> if text "item_id" row=Some item then text "clock" row |> Option.filter tokenPattern.IsMatch else None))
                        (activities |> Array.choose(fun row -> if text "item_id" row=Some item then text "clock_provenance" row |> Option.filter tokenPattern.IsMatch else None))
                    |> Array.distinct |> Array.sort
                let gapCodes =
                    gaps |> Array.choose(fun row -> if text "item_id" row=Some item then text "code" row |> Option.filter tokenPattern.IsMatch else None) |> Array.distinct |> Array.sort
                let node=JsonObject()
                node["id"]<-item
                node["factCount"]<-number "factCount" summary |> Option.get
                node["usageObservations"]<-number "usageObservations" summary |> Option.get
                node["deliveryObservations"]<-number "deliveryObservations" summary |> Option.get
                let usageNode=JsonObject()
                for name in ["input";"cachedInput";"cacheWriteInput";"output";"total"] do usageNode[name]<-number name usage |> Option.get
                usageNode["reasoning"] <- match nullableNumber "reasoning" usage |> Option.get with Some n -> JsonValue.Create n | None -> null
                usageNode["nativeUsage"] <-
                    if gapCodes |> Array.contains "native-usage-unsupported" then "unsupported"
                    elif gapCodes.Length>0 then "incomplete"
                    elif (number "usageObservations" summary |> Option.get)>0L then "observed"
                    else "missing"
                node["usage"]<-usageNode
                let runtime=JsonObject()
                for name in ["admitted";"started";"terminal";"usage";"missingAdmission";"missingStart";"missingTerminal";"missingUsage"] do runtime[name]<-number name launcher |> Option.get
                runtime["gapCodes"]<-JsonArray(gapCodes |> Array.map(fun v->JsonValue.Create(v):>JsonNode))
                node["runtime"]<-runtime
                let state=JsonObject()
                state["population"]<-population
                state["dirty"]<-dirty |> Array.exists(fun row->text "item_id" row=Some item)
                state["outcome"]<-safeToken "missing" (outcome |> Option.bind(text "outcome"))
                state["codeDelivery"]<-safeToken "unknown" (outcome |> Option.bind(text "code_delivery"))
                state["observedAt"]<-match outcome |> Option.bind(text "observed_at") |> safeTime with Some v->JsonValue.Create v|None->null
                node["state"]<-state
                let coverage=JsonObject()
                for name in ["recordValidity";"joinIntegrity";"populationCoverage";"qualification"] do coverage[name]<-safeToken "unknown" (text name summary)
                coverage["ciInventory"]<-safeToken "unknown" (pci |> Option.bind(text "actions") |> Option.orElseWith(fun()->ci |> Option.bind(text "inventory")))
                coverage["ciChecks"]<-safeToken "unknown" (pci |> Option.bind(text "checks"))
                coverage["ciAttempts"]<-safeToken "unknown" (pci |> Option.bind(text "attempts") |> Option.orElseWith(fun()->ci |> Option.bind(text "attempts")))
                coverage["ciJobs"]<-safeToken "unknown" (pci |> Option.bind(text "jobs") |> Option.orElseWith(fun()->ci |> Option.bind(text "job_pages")))
                coverage["ciTerminal"]<-safeToken "unknown" (pci |> Option.bind(text "terminal") |> Option.orElseWith(fun()->ci |> Option.bind(text "terminal")))
                coverage["ciTimestamps"]<-safeToken "unknown" (pci |> Option.bind(text "timestamps") |> Option.orElseWith(fun()->ci |> Option.bind(text "timestamps")))
                coverage["ciContinuation"]<-safeToken "none" (pci |> Option.bind(text "continuation"))
                coverage["externalChecks"]<-match pci |> Option.bind(number "external_checks") with Some value->JsonValue.Create value|None->null
                node["coverage"]<-coverage
                node["clockProvenance"]<-JsonArray(clocks |> Array.map(fun v->JsonValue.Create(v):>JsonNode))
                Ok(node:>JsonNode)
            | _ -> Error InvalidSnapshot

    let project (authorizedWorkspace:string) (canonicalSnapshotEnvelopeBytes:byte array) =
        let bytes = canonicalSnapshotEnvelopeBytes
        if isNull authorizedWorkspace || not(idPattern.IsMatch authorizedWorkspace) then Error UnauthorizedWorkspace
        elif isNull bytes || bytes.Length > envelopeLimit then Error EnvelopeTooLarge
        else
            try
                use envelopeDocument=JsonDocument.Parse(ReadOnlyMemory<byte>(bytes),JsonDocumentOptions(AllowTrailingCommas=false,CommentHandling=JsonCommentHandling.Disallow,MaxDepth=8))
                let envelope=envelopeDocument.RootElement
                let names=set["schema";"observedAt";"revision";"canonicalSnapshotGzip";"operational"]
                if not(exactNames envelope names) then Error InvalidEnvelope
                elif text "schema" envelope<>Some "fsgg.telemetry.item-detail/2" then Error UnsupportedSchema
                else
                    match text "revision" envelope,text "canonicalSnapshotGzip" envelope,property "operational" envelope with
                    | Some revision,Some encoded,Some operational when Regex.IsMatch(revision,@"\A[0-9a-f]{64}\z") && exactNames operational (set["pendingBatches";"consistency"]) ->
                        let observedAt = text "observedAt" envelope |> safeTime
                        let pendingBatches = number "pendingBatches" operational
                        let consistency = text "consistency" operational
                        if observedAt.IsNone || pendingBatches.IsNone || consistency <> Some "observed-outside-database-transaction" then Error InvalidEnvelope else
                        let compressed = try Ok(Convert.FromBase64String encoded) with _ -> Error InvalidRevision
                        match compressed |> Result.bind readBoundedGzip with
                        | Error error -> Error error
                        | Ok canonical when Convert.ToHexString(SHA256.HashData canonical).ToLowerInvariant()<>revision -> Error InvalidRevision
                        | Ok canonical ->
                            try
                                use snapshotDocument=JsonDocument.Parse(ReadOnlyMemory<byte>(canonical),JsonDocumentOptions(AllowTrailingCommas=false,CommentHandling=JsonCommentHandling.Disallow,MaxDepth=32))
                                let snapshot=snapshotDocument.RootElement
                                let expected=set["selection";"store";"items";"summaries";"populations";"dirtyItems";"outcomes";"admissions";"starts";"terminals";"expectedDispatches";"lineage";"times";"usage";"runtimeGaps";"ciRuns";"ciJobs";"ciSteps";"ciCoverage";"ciPopulationCoverage";"budgetAssessments";"budgetMembership";"budgetEpochs";"budgetBreaches";"budgetInterventions";"activities";"activityUsageAttributions";"complications";"reviews"]
                                match property "selection" snapshot,property "store" snapshot with
                                | Some selection,Some store when exactNames snapshot expected && (text "mode" selection |> Option.exists(fun mode->mode="all" || mode="item")) && property "complete" selection |> Option.exists(fun v->v.ValueKind=JsonValueKind.True) ->
                                    match number "schemaVersion" store,text "journalMode" store with
                                    | Some version,Some "wal" when version=8L || version=9L ->
                                        let arrayNames=["items";"summaries";"populations";"dirtyItems";"outcomes";"admissions";"starts";"terminals";"expectedDispatches";"lineage";"times";"usage";"runtimeGaps";"ciRuns";"ciJobs";"ciSteps";"ciCoverage";"ciPopulationCoverage";"budgetAssessments";"budgetMembership";"budgetEpochs";"budgetBreaches";"budgetInterventions";"activities";"activityUsageAttributions";"complications";"reviews"]
                                        let allArrays=arrayNames |> List.map(fun n->array n snapshot)
                                        let requiredArrays=["items";"summaries";"populations";"dirtyItems";"outcomes";"runtimeGaps";"ciCoverage";"ciPopulationCoverage";"times";"activities"] |> List.map(fun n->array n snapshot)
                                        if allArrays |> List.exists Option.isNone then Error InvalidSnapshot else
                                        let values=requiredArrays |> List.map Option.get
                                        if allArrays |> List.map Option.get |> List.exists(fun rows->rows.Length>10000) then Error InvalidSnapshot else
                                        let itemElements=values[0]
                                        let items=itemElements |> Array.choose(fun v->if v.ValueKind=JsonValueKind.String && validItem(v.GetString()) then Some(v.GetString()) else None)
                                        if items.Length<>itemElements.Length || items.Length>200 || items.Length<>(Array.distinct items|>Array.length) || values[1].Length<>items.Length then Error InvalidSnapshot else
                                        let projected=items |> Array.map(projectItem values[1] values[2] values[3] values[4] values[5] values[6] values[7] values[8] values[9])
                                        match projected |> Array.tryPick(function Error e->Some e|_->None) with
                                        | Some error->Error error
                                        | None ->
                                            let result=JsonObject()
                                            result["schema"]<-Schema
                                            result["workspaceId"]<-authorizedWorkspace
                                            result["observedAt"]<-observedAt.Value
                                            result["revision"]<-revision
                                            let op=JsonObject()
                                            op["pendingBatches"]<-pendingBatches.Value
                                            op["consistency"]<-consistency.Value
                                            result["operational"]<-op
                                            result["items"]<-JsonArray(projected |> Array.choose(function Ok n->Some n|_->None))
                                            let output=Encoding.UTF8.GetBytes(result.ToJsonString(JsonSerializerOptions(WriteIndented=false)))
                                            if output.Length>envelopeLimit then Error SnapshotTooLarge else Ok output
                                    | _ -> Error IncompatibleStore
                                | _ -> Error IncompleteSnapshot
                            with _ -> Error InvalidSnapshot
                    | _ -> Error InvalidEnvelope
            with _ -> Error InvalidEnvelope

type Asset = { ContentType:string; Bytes:byte array }

module DashboardAssets =
    let private assembly=Assembly.GetExecutingAssembly()
    let private read name =
        use stream=assembly.GetManifestResourceStream("FS.GG.Telemetry.Dashboard.Assets."+name)
        use memory=new MemoryStream()
        stream.CopyTo memory
        memory.ToArray()
    let private assets =
        Map.ofList [ "/private/dashboard/", {ContentType="text/html; charset=utf-8";Bytes=read "index.html"}
                     "/private/dashboard/app.js", {ContentType="text/javascript; charset=utf-8";Bytes=read "app.js"}
                     "/private/dashboard/styles.css", {ContentType="text/css; charset=utf-8";Bytes=read "styles.css"} ]
    let tryGet route = assets |> Map.tryFind route |> Option.map(fun asset->{asset with Bytes=Array.copy asset.Bytes})
