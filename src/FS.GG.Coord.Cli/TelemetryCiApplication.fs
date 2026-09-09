namespace FS.GG.Coord.Cli

#nowarn "3391"

open System
open System.IO
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open System.Text.RegularExpressions
open FS.GG.Coord
open FS.GG.Coord.GitHub

module TelemetryCiApplication =
    let canCreateAdmission (outcome: string) (codeDelivery: string) (observedHead: string option) (expectedHead: string) =
        outcome = "ready" && codeDelivery = "not-delivered" && observedHead = Some expectedHead
    let private option name args = args |> List.indexed |> List.tryPick (fun (index,value) -> if value = name then List.tryItem (index + 1) args else None)
    let private legacyRoot args = option "--store-root" args |> Option.orElseWith (fun () -> Environment.GetEnvironmentVariable("FSGG_TELEMETRY_STORE") |> Option.ofObj |> Option.filter (String.IsNullOrWhiteSpace >> not))
    let private fail reasons = reasons |> List.iter (fun reason -> Console.Error.WriteLine("fsgg-coord-engine: telemetry ci: " + reason)); 1
    let private addOptional (node: JsonObject) (name: string) (value: string option) = node[name] <- match value with Some text -> JsonValue.Create text | None -> null
    let private identity (prefix: string) (parts: string list) = prefix + CanonicalJson.sha256(Encoding.UTF8.GetBytes(String.concat "\u001f" parts)).Substring(0, 40)
    let private common (kind: string) (nativeIdentity: string) (item: string) =
        let node = JsonObject()
        node["kind"] <- kind
        node["identity"] <- nativeIdentity
        node["itemId"] <- item
        node["revision"] <- 0
        node
    let private stateRevision status = match status with "completed" -> 2 | "in_progress" -> 1 | _ -> 0
    let private eventBatch (assignment: TelemetryCi.Assignment) (collection: string) (index: int) (events: JsonObject list) =
        let root = JsonObject()
        let values = JsonArray()
        events |> List.iter (fun event -> values.Add(event.DeepClone()))
        let contentKey = CanonicalJson.sha256(Encoding.UTF8.GetBytes(values.ToJsonString())).Substring(0, 16)
        root["schema"] <- TelemetryStore.BatchSchema; root["ingestId"] <- $"ci-%s{collection.Substring(0, 24)}-%04d{index}-%s{contentKey}"; root["sourceIdentity"] <- assignment.ProducerStream
        root["generation"] <- collection; root["cursor"] <- $"%04d{index}-%s{contentKey}"; root["eventCount"] <- events.Length
        root["events"] <- values
        Encoding.UTF8.GetBytes(root.ToJsonString(JsonSerializerOptions(WriteIndented = false)))

    let private boundedBatches (assignment: TelemetryCi.Assignment) (collection: string) (events: JsonObject list) =
        let rec loop (index: int) (current: JsonObject list) (remaining: JsonObject list) (output: byte array list) =
            match remaining with
            | [] ->
                if current.IsEmpty then Ok(List.rev output)
                else Ok(List.rev (eventBatch assignment collection index (List.rev current) :: output))
            | event :: tail ->
                let candidate = event :: current
                let bytes = eventBatch assignment collection index (List.rev candidate)
                if candidate.Length <= TelemetryStore.MaxEvents && bytes.Length <= TelemetryStore.MaxBatchBytes then loop index candidate tail output
                elif current.IsEmpty then Error [ "projected CI observation exceeds the immutable event bound" ]
                else loop (index + 1) [] remaining (eventBatch assignment collection index (List.rev current) :: output)
        loop 1 [] events []

    type private Rule = { Workflow: string; Job: string; Step: string; Classification: string; Rationale: string }
    type private DeliveryBinding =
        { Repository: string; PullRequest: int; BaseRef: string; BaseSha: string; Head: string
          Outcome: string; CodeDelivery: string; ObservedHead: string option; MergeCommit: string option
          OutcomeAt: string option; ObservedAt: string }
    let private readRules (path: string) =
        try
            use document = JsonDocument.Parse(File.ReadAllBytes path)
            let root = document.RootElement
            let allowed = Set [ "schema"; "rules" ]
            let closed = root.EnumerateObject() |> Seq.forall (fun field -> Set.contains field.Name allowed)
            if not closed || root.GetProperty("schema").GetString() <> "fsgg.telemetry.ci-attribution/1" then Error [ "attribution profile is malformed" ] else
            let rules =
                root.GetProperty("rules").EnumerateArray()
                |> Seq.map (fun value -> { Workflow = value.GetProperty("workflow").GetString(); Job = value.GetProperty("job").GetString(); Step = value.GetProperty("step").GetString(); Classification = value.GetProperty("classification").GetString(); Rationale = value.GetProperty("rationale").GetString() })
                |> Seq.toList
            if rules |> List.exists (fun rule -> not (Set.contains rule.Classification (Set [ "useful-validation"; "admin"; "necessary-setup"; "mixed"; "unclassified" ]))) then Error [ "attribution profile contains unsupported classification" ] else Ok rules
        with error -> Error [ "attribution profile is unavailable: " + error.Message ]

    let private project (assignment: TelemetryCi.Assignment) (rules: Rule list) (snapshot: CiReads.Snapshot) =
        let collection = CanonicalJson.sha256(Encoding.UTF8.GetBytes($"%s{snapshot.Repository}\u001f%s{snapshot.Head}\u001f%d{snapshot.PullRequest}\u001f%s{snapshot.Workflow}\u001f%s{assignment.ItemId}"))
        let binding = common "ci-binding" (identity "ci-binding-" [collection]) assignment.ItemId
        binding["revision"] <- if snapshot.Binding = "exact" then 1 else 0
        binding["collectionId"] <- collection; binding["repository"] <- snapshot.Repository; binding["head"] <- snapshot.Head; binding["prNumber"] <- snapshot.PullRequest; binding["workflow"] <- snapshot.Workflow
        binding["featureId"] <- assignment.FeatureId; binding["attemptId"] <- assignment.AttemptId; addOptional binding "parentAttemptId" assignment.ParentAttemptId; binding["producerStream"] <- assignment.ProducerStream; binding["binding"] <- snapshot.Binding
        let pages = snapshot.Pages |> List.map (fun page -> let node = common "ci-page" (identity "ci-page-" [collection;page.Resource;string page.Index]) assignment.ItemId in node["collectionId"] <- collection; node["resource"] <- page.Resource; node["page"] <- page.Index; node["count"] <- page.Count; node["total"] <- page.Total; node)
        let runs = snapshot.Runs |> List.map (fun run -> let node = common "ci-run" (identity "ci-run-" [snapshot.Repository;string run.Id;string run.Attempt]) assignment.ItemId in node["revision"] <- stateRevision run.Status; node["repository"] <- snapshot.Repository; node["runId"] <- run.Id; node["attempt"] <- run.Attempt; node["workflow"] <- run.Workflow; node["event"] <- run.Event; node["head"] <- run.HeadSha; node["status"] <- run.Status; addOptional node "conclusion" run.Conclusion; addOptional node "createdAt" run.CreatedAt; addOptional node "startedAt" run.RunStartedAt; addOptional node "updatedAt" run.UpdatedAt; node)
        let jobs = snapshot.Jobs |> List.map (fun job -> let node = common "ci-job" (identity "ci-job-" [snapshot.Repository;string job.RunId;string job.Attempt;string job.Id]) assignment.ItemId in node["revision"] <- stateRevision job.Status; node["repository"] <- snapshot.Repository; node["runId"] <- job.RunId; node["attempt"] <- job.Attempt; node["jobId"] <- job.Id; node["name"] <- job.Name; node["status"] <- job.Status; addOptional node "conclusion" job.Conclusion; addOptional node "createdAt" job.CreatedAt; addOptional node "startedAt" job.StartedAt; addOptional node "completedAt" job.CompletedAt; node)
        let steps = snapshot.Jobs |> List.collect (fun job -> job.Steps |> List.map (fun step ->
            let workflowIdentity = snapshot.Runs |> List.tryFind (fun run -> run.Id = job.RunId) |> Option.map _.Workflow |> Option.defaultValue snapshot.Workflow
            let matched = rules |> List.tryFind (fun rule -> (rule.Workflow = workflowIdentity || rule.Workflow = snapshot.Workflow) && rule.Job = job.Name && rule.Step = step.Name)
            let classification,rationale = matched |> Option.map (fun rule -> rule.Classification,rule.Rationale) |> Option.defaultValue ("unclassified","no exact attribution rule")
            let node = common "ci-step" (identity "ci-step-" [snapshot.Repository;string job.RunId;string job.Attempt;string job.Id;string step.Number]) assignment.ItemId
            node["revision"] <- stateRevision step.Status; node["repository"] <- snapshot.Repository; node["runId"] <- job.RunId; node["attempt"] <- job.Attempt; node["jobId"] <- job.Id; node["number"] <- step.Number; node["name"] <- step.Name; node["status"] <- step.Status; addOptional node "conclusion" step.Conclusion; addOptional node "startedAt" step.StartedAt; addOptional node "completedAt" step.CompletedAt; node["classification"] <- classification; node["rationale"] <- rationale; node))
        let classificationCoverage = if steps.IsEmpty || steps |> List.exists (fun step -> step["classification"].GetValue<string>() = "unclassified") then "unknown" else "complete"
        let coverageKey = String.concat "\u001f" [ snapshot.InventoryCoverage; snapshot.AttemptCoverage; snapshot.JobPageCoverage; snapshot.TerminalCoverage; snapshot.TimestampCoverage; snapshot.LineageCoverage; classificationCoverage; "unknown"; string snapshot.Pages.Length ]
        let coverage = common "ci-coverage" (identity "ci-coverage-" [collection;coverageKey]) assignment.ItemId
        coverage["collectionId"] <- collection; coverage["inventory"] <- snapshot.InventoryCoverage; coverage["attempts"] <- snapshot.AttemptCoverage; coverage["jobPages"] <- snapshot.JobPageCoverage; coverage["terminal"] <- snapshot.TerminalCoverage; coverage["timestamps"] <- snapshot.TimestampCoverage; coverage["lineage"] <- snapshot.LineageCoverage; coverage["classification"] <- classificationCoverage; coverage["criticalPath"] <- "unknown"
        let diagnostics = snapshot.Diagnostic |> Option.map (fun code -> let node = common "diagnostic" (identity "ci-diagnostic-" [collection;code]) assignment.ItemId in node["code"] <- code; node["severity"] <- "warning"; node) |> Option.toList
        collection, binding :: (pages @ runs @ jobs @ steps @ [coverage] @ diagnostics)

    let private readAssignment path =
        try
            let full = Path.GetFullPath path
            let info = FileInfo full
            if not (Path.IsPathFullyQualified path) then Error [ "assignment path must be absolute" ]
            elif not info.Exists || not (isNull info.LinkTarget) then Error [ "assignment must be a regular non-symlink file" ]
            elif info.Length > 8192L then Error [ "assignment exceeds 8 KiB" ]
            elif not (OperatingSystem.IsWindows()) && File.GetUnixFileMode(full) <> (UnixFileMode.UserRead ||| UnixFileMode.UserWrite) then Error [ "assignment permissions must be 0600" ]
            else File.ReadAllBytes full |> TelemetryCi.parseAssignment
        with error -> Error [ "assignment is unavailable: " + error.Message ]

    let private readDelivery path =
        try
            let full = Path.GetFullPath path
            let info = FileInfo full
            if not (Path.IsPathFullyQualified path) then Error [ "delivery path must be absolute" ]
            elif not info.Exists || not (isNull info.LinkTarget) then Error [ "delivery must be a regular non-symlink file" ]
            elif info.Length > 16384L then Error [ "delivery exceeds 16 KiB" ]
            else
                use document = JsonDocument.Parse(File.ReadAllBytes full)
                let value = document.RootElement
                let text (name: string) = match value.TryGetProperty name with true, field when field.ValueKind = JsonValueKind.String -> Some(field.GetString()) | _ -> None
                let number (name: string) =
                    match value.TryGetProperty name with
                    | true, field when field.ValueKind = JsonValueKind.Number -> match field.TryGetInt32() with true, result -> Some result | _ -> None
                    | _ -> None
                let observed = text "observedHead"
                let mergeCommit = text "mergeCommit"
                let outcomeAt = text "outcomeAt"
                let observedAt = text "observedAt"
                match text "schema", text "repo", number "pr", text "baseRef", text "baseSha", text "expectedHead", text "outcome", text "codeDelivery", observedAt with
                | Some "fsgg.routine-delivery/v1", Some repository, Some pr, Some baseRef, Some baseSha, Some head, Some outcome, Some codeDelivery, Some observedAt
                    when Regex.IsMatch(repository, "^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$") && pr > 0 && Regex.IsMatch(head, "^[0-9a-f]{40}$")
                         && not (String.IsNullOrWhiteSpace baseRef) && Regex.IsMatch(baseSha, "^[0-9a-f]{40}$")
                         && mergeCommit |> Option.forall (fun value -> Regex.IsMatch(value, "^[0-9a-f]{40}$"))
                         && [ Some observedAt; outcomeAt ] |> List.choose id |> List.forall (fun value -> DateTimeOffset.TryParse(value) |> fst)
                         && Set.contains outcome (Set [ "ready"; "refused"; "delivered"; "delivered-after-readback"; "delivered-disputed"; "indeterminate" ])
                         && Set.contains codeDelivery (Set [ "delivered"; "not-delivered"; "unknown" ]) -> Ok { Repository = repository; PullRequest = pr; BaseRef = baseRef; BaseSha = baseSha; Head = head; Outcome = outcome; CodeDelivery = codeDelivery; ObservedHead = observed; MergeCommit = mergeCommit; OutcomeAt = outcomeAt; ObservedAt = observedAt }
                | _ -> Error [ "delivery is not a valid exact-head fsgg.routine-delivery/v1 candidate binding" ]
        with error -> Error [ "delivery is unavailable: " + error.Message ]

    let private projectPopulation (assignment: TelemetryCi.Assignment) (rules: Rule list) (population: CiReads.PopulationSnapshot) =
        let snapshot = population.Snapshot
        let collection = CanonicalJson.sha256(Encoding.UTF8.GetBytes($"%s{snapshot.Repository}\u001f%s{snapshot.Head}\u001f%d{snapshot.PullRequest}\u001f%s{assignment.ItemId}\u001fall-workflows"))
        let revision = population.Revision
        let revised kind nativeIdentity = let node = common kind nativeIdentity assignment.ItemId in node["revision"] <- revision; node
        let admission =
            if population.AdmissionWitness then
                let node = common "ci-population-admission" (identity "ci-population-admission-" [collection]) assignment.ItemId
                node["revision"] <- 1; node["collectionId"] <- collection; node["repository"] <- snapshot.Repository; node["prNumber"] <- snapshot.PullRequest; node["baseRef"] <- population.BaseRef.Value; node["baseSha"] <- population.BaseSha.Value; node["head"] <- snapshot.Head; node["witness"] <- "native-pr-head"
                [node]
            else []
        let binding = revised "ci-binding" (identity "ci-binding-" [collection])
        binding["collectionId"] <- collection; binding["repository"] <- snapshot.Repository; binding["head"] <- snapshot.Head; binding["prNumber"] <- snapshot.PullRequest; binding["workflow"] <- "*"
        binding["featureId"] <- assignment.FeatureId; binding["attemptId"] <- assignment.AttemptId; addOptional binding "parentAttemptId" assignment.ParentAttemptId; binding["producerStream"] <- assignment.ProducerStream; binding["binding"] <- snapshot.Binding
        let pages = snapshot.Pages |> List.map (fun page -> let node = revised "ci-page" (identity "ci-page-" [collection;page.Resource;string page.Index]) in node["collectionId"] <- collection; node["resource"] <- page.Resource; node["page"] <- page.Index; node["count"] <- page.Count; node["total"] <- page.Total; node)
        let runs = snapshot.Runs |> List.map (fun run -> let node = revised "ci-run" (identity "ci-run-" [snapshot.Repository;string run.Id;string run.Attempt]) in node["repository"] <- snapshot.Repository; node["runId"] <- run.Id; node["attempt"] <- run.Attempt; node["workflow"] <- run.Workflow; node["event"] <- run.Event; node["head"] <- run.HeadSha; node["status"] <- run.Status; addOptional node "conclusion" run.Conclusion; addOptional node "createdAt" run.CreatedAt; addOptional node "startedAt" run.RunStartedAt; addOptional node "updatedAt" run.UpdatedAt; node)
        let jobs = snapshot.Jobs |> List.map (fun job -> let node = revised "ci-job" (identity "ci-job-" [snapshot.Repository;string job.RunId;string job.Attempt;string job.Id]) in node["repository"] <- snapshot.Repository; node["runId"] <- job.RunId; node["attempt"] <- job.Attempt; node["jobId"] <- job.Id; node["name"] <- job.Name; node["status"] <- job.Status; addOptional node "conclusion" job.Conclusion; addOptional node "createdAt" job.CreatedAt; addOptional node "startedAt" job.StartedAt; addOptional node "completedAt" job.CompletedAt; node)
        let steps = snapshot.Jobs |> List.collect (fun job -> job.Steps |> List.map (fun step ->
            let workflow = snapshot.Runs |> List.tryFind (fun run -> run.Id = job.RunId && run.Attempt = job.Attempt) |> Option.map _.Workflow |> Option.defaultValue "*"
            let matched = rules |> List.tryFind (fun rule -> rule.Workflow = workflow && rule.Job = job.Name && rule.Step = step.Name)
            let classification,rationale = matched |> Option.map (fun rule -> rule.Classification,rule.Rationale) |> Option.defaultValue ("unclassified","no exact attribution rule")
            let node = revised "ci-step" (identity "ci-step-" [snapshot.Repository;string job.RunId;string job.Attempt;string job.Id;string step.Number])
            node["repository"] <- snapshot.Repository; node["runId"] <- job.RunId; node["attempt"] <- job.Attempt; node["jobId"] <- job.Id; node["number"] <- step.Number; node["name"] <- step.Name; node["status"] <- step.Status; addOptional node "conclusion" step.Conclusion; addOptional node "startedAt" step.StartedAt; addOptional node "completedAt" step.CompletedAt; node["classification"] <- classification; node["rationale"] <- rationale; node))
        let checks = population.Checks |> List.map (fun check -> let node = revised "ci-check" (identity "ci-check-" [snapshot.Repository;string check.Id]) in node["repository"] <- snapshot.Repository; node["checkId"] <- check.Id; node["name"] <- check.Name; addOptional node "appSlug" check.AppSlug; node["status"] <- check.Status; addOptional node "conclusion" check.Conclusion; addOptional node "startedAt" check.StartedAt; addOptional node "completedAt" check.CompletedAt; node)
        let gaps = JsonSerializer.Serialize(Array.ofList population.Gaps)
        let coverage = revised "ci-population-coverage" (identity "ci-population-coverage-" [collection])
        coverage["collectionId"] <- collection; coverage["actions"] <- snapshot.InventoryCoverage; coverage["checks"] <- population.CheckCoverage; coverage["attempts"] <- snapshot.AttemptCoverage; coverage["jobs"] <- snapshot.JobPageCoverage; coverage["terminal"] <- snapshot.TerminalCoverage; coverage["timestamps"] <- snapshot.TimestampCoverage; coverage["continuation"] <- (if population.Pending.IsEmpty then "none" else "pending"); coverage["externalChecks"] <- population.ExternalChecks; coverage["gaps"] <- gaps
        let oldCoverage = revised "ci-coverage" (identity "ci-coverage-" [collection])
        let classification = if steps.IsEmpty || steps |> List.exists (fun step -> step["classification"].GetValue<string>() = "unclassified") then "unknown" else "complete"
        oldCoverage["collectionId"] <- collection; oldCoverage["inventory"] <- snapshot.InventoryCoverage; oldCoverage["attempts"] <- snapshot.AttemptCoverage; oldCoverage["jobPages"] <- snapshot.JobPageCoverage; oldCoverage["terminal"] <- snapshot.TerminalCoverage; oldCoverage["timestamps"] <- snapshot.TimestampCoverage; oldCoverage["lineage"] <- snapshot.LineageCoverage; oldCoverage["classification"] <- classification; oldCoverage["criticalPath"] <- "unknown"
        let diagnostics = (population.Pending @ population.Gaps) |> List.distinct |> List.map (fun code -> let node = revised "diagnostic" (identity "ci-diagnostic-" [collection;code]) in node["code"] <- code; node["severity"] <- "warning"; node)
        collection, admission @ [binding] @ pages @ runs @ jobs @ steps @ checks @ [oldCoverage;coverage] @ diagnostics

    let private projectOutcome (assignment: TelemetryCi.Assignment) (delivery: DeliveryBinding) =
        let candidate = String.concat "\u001f" [ assignment.ItemId; delivery.Repository; string delivery.PullRequest; delivery.BaseRef; delivery.BaseSha; delivery.Head ]
        let nativeIdentity = identity "native-item-outcome-" [ candidate ]
        let event = common "native-item-outcome" nativeIdentity assignment.ItemId
        event["revision"] <- DateTimeOffset.Parse(delivery.ObservedAt).UtcTicks
        event["repository"] <- delivery.Repository; event["prNumber"] <- delivery.PullRequest
        event["baseRef"] <- delivery.BaseRef; event["baseSha"] <- delivery.BaseSha; event["head"] <- delivery.Head
        event["outcome"] <- delivery.Outcome; event["codeDelivery"] <- delivery.CodeDelivery
        addOptional event "mergeCommit" delivery.MergeCommit; addOptional event "occurredAt" delivery.OutcomeAt
        event["observedAt"] <- delivery.ObservedAt; event["sourceKind"] <- "routine-delivery"
        event["sourceRef"] <- "routine-delivery:" + CanonicalJson.sha256(Encoding.UTF8.GetBytes candidate)
        let collection = CanonicalJson.sha256(Encoding.UTF8.GetBytes(candidate + "\u001foutcome"))
        collection,event

    type private Target<'binding> =
        | Legacy of root:string * assessment:TelemetryStore.DurabilityAssessment
        | Workspace of binding:'binding * localRoot:string option

    let private target
        (assess:string -> TelemetryStore.DurabilityAssessment)
        (resolve:string option -> string option -> Result<'binding,string list>)
        (localStoreRoot:'binding -> Result<string option,string list>)
        args =
        let config, repository = option "--config" args, option "--repository" args
        let selectedConfigExists = File.Exists(WorkspaceTelemetryApplication.configuredPath config)
        match option "--store-root" args, config.IsSome || selectedConfigExists with
        | Some root, _ -> Ok(Legacy(root, assess root))
        | None, true ->
            resolve config repository
            |> Result.bind (fun binding -> localStoreRoot binding |> Result.map (fun root -> Workspace(binding, root)))
        | None, false ->
            match Environment.GetEnvironmentVariable("FSGG_TELEMETRY_STORE") |> Option.ofObj |> Option.filter (String.IsNullOrWhiteSpace >> not) with
            | Some root -> Ok(Legacy(root, assess root))
            | None -> resolve config repository |> Result.bind (fun binding -> localStoreRoot binding |> Result.map (fun root -> Workspace(binding, root)))

    let private publish
        (publishWorkspace:'binding -> byte array -> Result<string,string list>)
        target bytes =
        match target with
        | Legacy(root, assessment) -> TelemetryStoreApplication.publish root assessment bytes |> Result.map (fun _ -> "queued")
        | Workspace(binding, _) ->
            match publishWorkspace binding bytes with
            | Ok status -> Ok status
            | Error [ "unacknowledged-lossy" ] -> Ok "queued"
            | Error errors -> Error errors

    let private drain
        (drainWorkspace:'binding -> Result<string,string list>)
        target =
        match target with
        | Legacy(root, assessment) -> TelemetryStoreApplication.drain root assessment |> Result.map ignore
        | Workspace(binding, _) -> drainWorkspace binding |> Result.map ignore

    let private admission target (assignment:TelemetryCi.Assignment) (delivery:DeliveryBinding) =
        match target with
        | Legacy(root, assessment) -> TelemetryStoreApplication.ciPopulationAdmissionExists root assessment assignment.ItemId delivery.Repository delivery.PullRequest delivery.BaseRef delivery.BaseSha delivery.Head |> Result.map Some
        | Workspace(_, Some root) ->
            let assessment = TelemetryStoreApplication.assessProductionRoot root
            TelemetryStoreApplication.ciPopulationAdmissionExists root assessment assignment.ItemId delivery.Repository delivery.PullRequest delivery.BaseRef delivery.BaseSha delivery.Head |> Result.map Some
        | Workspace(_, None) -> Ok None

    let private health target (assignment:TelemetryCi.Assignment) =
        match target with
        | Legacy(root, assessment) ->
            TelemetryStoreApplication.budgetHealth root assessment assignment.ItemId
            |> Result.bind (fun json ->
                try use document = JsonDocument.Parse json in Ok(document.RootElement.GetProperty("status").GetString())
                with _ -> Error [ "health-unavailable" ])
        | Workspace(_, Some root) ->
            TelemetryStoreApplication.budgetHealth root (TelemetryStoreApplication.assessProductionRoot root) assignment.ItemId
            |> Result.bind (fun json ->
                try use document = JsonDocument.Parse json in Ok(document.RootElement.GetProperty("status").GetString())
                with _ -> Error [ "health-unavailable" ])
        | Workspace(_, None) -> Ok "pending"

    let private remainsQueued target drainResult =
        match target with
        | Workspace(_, None) -> true
        | _ -> Result.isError drainResult

    let private runUsing assess resolve publishWorkspace drainWorkspace localStoreRoot action args =
        match action with
        | "summary" ->
            match legacyRoot args, option "--item" args with
            | Some path, Some item -> TelemetryStoreApplication.ciSummary path (assess path) item |> function Ok json -> Console.Out.Write json; 0 | Error errors -> fail errors
            | None, _ -> fail [ "store root is unconfigured" ]
            | _, None -> fail [ "--item is required" ]
        | "collect" ->
            match option "--assignment" args, option "--repo" args, option "--pr" args, option "--head" args, option "--workflow" args, target assess resolve localStoreRoot args with
            | Some assignmentPath, Some repository, Some prText, Some head, Some workflow, Ok destination ->
                match repository.Split('/') with
                | [| owner; repo |] when Regex.IsMatch(repository, "^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$") && Regex.IsMatch(head, "^[0-9a-f]{40}$") ->
                    match Int32.TryParse prText, readAssignment assignmentPath, readRules (Path.Combine(Directory.GetCurrentDirectory(), ".fsgg", "telemetry-ci-attribution.json")) with
                    | (true, pr), Ok assignment, Ok rules when pr > 0 ->
                        let token = Environment.GetEnvironmentVariable("GITHUB_TOKEN") |> Option.ofObj |> Option.orElseWith (fun () -> Environment.GetEnvironmentVariable("GH_TOKEN") |> Option.ofObj)
                        match token with
                        | None -> fail [ "GITHUB_TOKEN or GH_TOKEN is required" ]
                        | Some token ->
                            use transport = new Transport.HttpTransport(Transport.apiBaseFromEnv(), token)
                            match CiReads.collect (transport :> Transport.ISinglePageGitHubTransport) (Transport.apiBaseFromEnv()) owner repo pr head workflow with
                            | Error error -> fail [ string error ]
                            | Ok snapshot ->
                                let collection, events = project assignment rules snapshot
                                match boundedBatches assignment collection events with
                                | Error errors -> fail errors
                                | Ok batches ->
                                    let results = batches |> List.map (publish publishWorkspace destination)
                                    match results |> List.tryPick (function Error errors -> Some errors | _ -> None) with
                                    | Some errors -> fail errors
                                    | None -> Console.Out.WriteLine(JsonSerializer.Serialize {| schema = "fsgg.telemetry.ci-collect/1"; status = "queued"; batches = results.Length; observations = events.Length; coverage = snapshot.InventoryCoverage |}); 0
                    | (false,_), _, _ -> fail [ "--pr must be a positive integer" ]
                    | _, Error errors, _ | _, _, Error errors -> fail errors
                    | _ -> fail [ "--pr must be a positive integer" ]
                | _ -> fail [ "--repo must be owner/name and --head must be 40 lowercase hexadecimal characters" ]
            | _, _, _, _, _, Error errors -> fail errors
            | _ -> fail [ "collect requires --assignment, --repo, --pr, --head, --workflow, and an explicitly selected telemetry destination" ]
        | "reconcile" ->
            match option "--assignment" args, option "--delivery" args, target assess resolve localStoreRoot args with
            | Some assignmentPath, Some deliveryPath, Ok destination ->
                match readAssignment assignmentPath, readDelivery deliveryPath with
                | Ok assignment, Ok delivery ->
                    let values = delivery.Repository.Split('/')
                    let outcomeCollection,outcomeEvent = projectOutcome assignment delivery
                    let outcomeResult =
                        boundedBatches assignment outcomeCollection [outcomeEvent]
                        |> Result.bind (fun batches ->
                            batches
                            |> List.map (publish publishWorkspace destination)
                            |> List.tryPick (function Error errors -> Some(Error errors) | _ -> None)
                            |> Option.defaultValue (Ok())
                            |> Result.bind (fun () -> drain drainWorkspace destination))
                    match outcomeResult |> Result.bind (fun () -> admission destination assignment delivery) with
                    | Error errors -> fail errors
                    | Ok None when delivery.CodeDelivery = "delivered" ->
                        Console.Out.WriteLine(JsonSerializer.Serialize {| schema = "fsgg.telemetry.ci-reconciliation/1"; status = "pending"; driverHealth = "pending"; binding = "remote-admission-unavailable"; queued = true; population = "unknown"; pending = [| "remote-admission-query-unavailable" |]; unsupportedSources = [| "merge_group"; "base"; "pull_request_target"; "push" |] |}); 0
                    | Ok admittedOption when admittedOption = Some false || admittedOption = None ->
                        let admitted = false
                        if not (canCreateAdmission delivery.Outcome delivery.CodeDelivery delivery.ObservedHead delivery.Head) then fail [ "first CI population admission requires ready, not-delivered, and an explicit matching observed head" ] else
                        match readRules (Path.Combine(Directory.GetCurrentDirectory(), ".fsgg", "telemetry-ci-attribution.json")) with
                        | Error errors -> fail errors
                        | Ok rules ->
                            let token = Environment.GetEnvironmentVariable("GITHUB_TOKEN") |> Option.ofObj |> Option.orElseWith (fun () -> Environment.GetEnvironmentVariable("GH_TOKEN") |> Option.ofObj)
                            match token with
                            | None -> fail [ "GITHUB_TOKEN or GH_TOKEN is required" ]
                            | Some token ->
                                use transport = new Transport.HttpTransport(Transport.apiBaseFromEnv(), token)
                                match CiReads.discoverPopulation (transport :> Transport.ISinglePageGitHubTransport) (Transport.apiBaseFromEnv()) values[0] values[1] delivery.PullRequest delivery.Head delivery.BaseRef delivery.BaseSha admitted with
                                | Error error -> fail [ string error ]
                                | Ok population when not population.AdmissionWitness && not admitted ->
                                    Console.Out.WriteLine(JsonSerializer.Serialize {| schema = "fsgg.telemetry.ci-reconciliation/1"; status = "incomplete"; binding = "unadmitted-head"; queued = false; population = "unknown"; pending = population.Pending |> List.toArray; unsupportedSources = [| "merge_group"; "base"; "pull_request_target"; "push" |] |}); 0
                                | Ok population ->
                                    let collection, events = projectPopulation assignment rules population
                                    match boundedBatches assignment collection events with
                                    | Error errors -> fail errors
                                    | Ok batches ->
                                        let results = batches |> List.map (publish publishWorkspace destination)
                                        match results |> List.tryPick (function Error errors -> Some errors | _ -> None) with
                                        | Some errors -> fail errors
                                        | None ->
                                            let drainResult = drain drainWorkspace destination
                                            let health = match drainResult |> Result.bind (fun () -> health destination assignment) with Ok value -> value | Error _ -> "pending"
                                            let status = if population.Pending.IsEmpty && population.Gaps.IsEmpty && population.Snapshot.InventoryCoverage = "complete" && population.CheckCoverage = "complete" then "complete" else "partial"
                                            let workflows = population.Snapshot.Runs |> List.map _.Workflow |> List.distinct |> List.sort |> List.toArray
                                            Console.Out.WriteLine(JsonSerializer.Serialize {| schema = "fsgg.telemetry.ci-reconciliation/1"; status = status; driverHealth = health; binding = population.Snapshot.Binding; queued = remainsQueued destination drainResult; batches = batches.Length; observations = events.Length; workflows = workflows; attempts = population.Snapshot.Runs.Length; checks = population.Checks.Length; externalChecks = population.ExternalChecks; pending = population.Pending |> List.toArray; gaps = population.Gaps |> List.toArray; unsupportedSources = [| "merge_group"; "base"; "pull_request_target"; "push" |] |}); 0
                    | Ok(Some true) ->
                        match readRules (Path.Combine(Directory.GetCurrentDirectory(), ".fsgg", "telemetry-ci-attribution.json")) with
                        | Error errors -> fail errors
                        | Ok rules ->
                            let token = Environment.GetEnvironmentVariable("GITHUB_TOKEN") |> Option.ofObj |> Option.orElseWith (fun () -> Environment.GetEnvironmentVariable("GH_TOKEN") |> Option.ofObj)
                            match token with
                            | None -> fail [ "GITHUB_TOKEN or GH_TOKEN is required" ]
                            | Some token ->
                                use transport = new Transport.HttpTransport(Transport.apiBaseFromEnv(), token)
                                match CiReads.discoverPopulation (transport :> Transport.ISinglePageGitHubTransport) (Transport.apiBaseFromEnv()) values[0] values[1] delivery.PullRequest delivery.Head delivery.BaseRef delivery.BaseSha true with
                                | Error error -> fail [ string error ]
                                | Ok population ->
                                    let collection, events = projectPopulation assignment rules population
                                    match boundedBatches assignment collection events with
                                    | Error errors -> fail errors
                                    | Ok batches ->
                                        let results = batches |> List.map (publish publishWorkspace destination)
                                        match results |> List.tryPick (function Error errors -> Some errors | _ -> None) with
                                        | Some errors -> fail errors
                                        | None ->
                                            let drainResult = drain drainWorkspace destination
                                            let driverHealth = match drainResult |> Result.bind (fun () -> health destination assignment) with Ok value -> value | Error _ -> "pending"
                                            let status = if population.Pending.IsEmpty && population.Gaps.IsEmpty && population.Snapshot.InventoryCoverage = "complete" && population.CheckCoverage = "complete" then "complete" else "partial"
                                            Console.Out.WriteLine(JsonSerializer.Serialize {| schema = "fsgg.telemetry.ci-reconciliation/1"; status = status; driverHealth = driverHealth; binding = population.Snapshot.Binding; queued = remainsQueued destination drainResult; batches = batches.Length; observations = events.Length; workflows = population.Snapshot.Runs |> List.map _.Workflow |> List.distinct |> List.sort |> List.toArray; attempts = population.Snapshot.Runs.Length; checks = population.Checks.Length; externalChecks = population.ExternalChecks; pending = population.Pending |> List.toArray; gaps = population.Gaps |> List.toArray; unsupportedSources = [| "merge_group"; "base"; "pull_request_target"; "push" |] |}); 0
                    | Ok _ -> fail [ "CI population admission state is invalid" ]
                | Error errors, _ | _, Error errors -> fail errors
            | _, _, Error errors -> fail errors
            | _ -> fail [ "reconcile requires --assignment, --delivery, and an explicitly selected telemetry destination" ]
        | _ -> fail [ "action must be collect, reconcile, or summary" ]

    let runWithAssessment assessment action args =
        runUsing (fun _ -> assessment) WorkspaceTelemetryApplication.resolveBinding WorkspaceTelemetryApplication.tryPublishBinding WorkspaceTelemetryApplication.tryDrainBinding WorkspaceTelemetryApplication.tryLocalStoreRootBound action args
    let runWithWorkspaceForTesting resolve publish drain localStoreRoot action args =
        runUsing TelemetryStoreApplication.assessProductionRoot resolve publish drain localStoreRoot action args
    let run action args =
        runUsing TelemetryStoreApplication.assessProductionRoot WorkspaceTelemetryApplication.resolveBinding WorkspaceTelemetryApplication.tryPublishBinding WorkspaceTelemetryApplication.tryDrainBinding WorkspaceTelemetryApplication.tryLocalStoreRootBound action args
