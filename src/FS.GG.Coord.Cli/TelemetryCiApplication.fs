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
    let private option name args = args |> List.indexed |> List.tryPick (fun (index,value) -> if value = name then List.tryItem (index + 1) args else None)
    let private root args = option "--store-root" args |> Option.orElseWith (fun () -> Environment.GetEnvironmentVariable("FSGG_TELEMETRY_STORE") |> Option.ofObj |> Option.filter (String.IsNullOrWhiteSpace >> not))
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
        events |> List.iter values.Add
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

    let run action args =
        match action with
        | "summary" ->
            match root args, option "--item" args with
            | Some path, Some item -> TelemetryStoreApplication.ciSummary path (TelemetryStoreApplication.assessProductionRoot path) item |> function Ok json -> Console.Out.Write json; 0 | Error errors -> fail errors
            | None, _ -> fail [ "store root is unconfigured" ]
            | _, None -> fail [ "--item is required" ]
        | "collect" ->
            match option "--assignment" args, option "--repo" args, option "--pr" args, option "--head" args, option "--workflow" args, root args with
            | Some assignmentPath, Some repository, Some prText, Some head, Some workflow, Some storeRoot ->
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
                                    let results = batches |> List.map (TelemetryStoreApplication.publish storeRoot (TelemetryStoreApplication.assessProductionRoot storeRoot))
                                    match results |> List.tryPick (function Error errors -> Some errors | _ -> None) with
                                    | Some errors -> fail errors
                                    | None -> Console.Out.WriteLine(JsonSerializer.Serialize {| schema = "fsgg.telemetry.ci-collect/1"; status = "queued"; batches = results.Length; observations = events.Length; coverage = snapshot.InventoryCoverage |}); 0
                    | (false,_), _, _ -> fail [ "--pr must be a positive integer" ]
                    | _, Error errors, _ | _, _, Error errors -> fail errors
                    | _ -> fail [ "--pr must be a positive integer" ]
                | _ -> fail [ "--repo must be owner/name and --head must be 40 lowercase hexadecimal characters" ]
            | _ -> fail [ "collect requires --assignment, --repo, --pr, --head, --workflow, and a configured store root" ]
        | _ -> fail [ "action must be collect or summary" ]
