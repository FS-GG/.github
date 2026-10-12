namespace FS.GG.Coord.Cli

#nowarn "3391"

open System
open System.IO
open System.Runtime.InteropServices
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open System.Text.RegularExpressions
open FS.GG.Coord
open FS.GG.Coord.GitHub

module TelemetryCiApplication =
    module private ContinuationNative =
        [<StructLayout(LayoutKind.Sequential)>]
        type Stat =
            struct
                val mutable Device: uint64
                val mutable Inode: uint64
                val mutable Links: uint64
                val mutable Mode: uint32
                val mutable User: uint32
                val mutable Group: uint32
                val mutable Padding: int32
                val mutable Rdev: uint64
                val mutable Size: int64
                val mutable BlockSize: int64
                val mutable Blocks: int64
                val mutable AccessSeconds: int64
                val mutable AccessNanoseconds: int64
                val mutable ModifySeconds: int64
                val mutable ModifyNanoseconds: int64
                val mutable ChangeSeconds: int64
                val mutable ChangeNanoseconds: int64
                val mutable Reserved1: int64
                val mutable Reserved2: int64
                val mutable Reserved3: int64
            end

        [<Literal>]
        let LockExclusive = 2

        [<Literal>]
        let LockNonBlocking = 4

        [<DllImport("libc", SetLastError = true)>]
        extern int flock(int descriptor, int operation)

        [<DllImport("libc", SetLastError = true)>]
        extern int fsync(int descriptor)

        [<DllImport("libc", EntryPoint = "open", SetLastError = true)>]
        extern int openDirectory(string path, int flags)

        [<DllImport("libc", EntryPoint = "open", SetLastError = true)>]
        extern int openPrivateFile(string path, int flags, uint32 mode)

        [<DllImport("libc", SetLastError = true)>]
        extern int close(int descriptor)

        [<DllImport("libc", EntryPoint = "stat", SetLastError = true)>]
        extern int statPath(string path, Stat& value)

        [<DllImport("libc", SetLastError = true)>]
        extern int fstat(int descriptor, Stat& value)

        [<DllImport("libc")>]
        extern uint32 geteuid()


    // Linux x64 stat ABI matches the maintained workspace reader; refuse unsupported hosts.
    let private continuationStat path =
        let mutable value = Unchecked.defaultof<ContinuationNative.Stat>
        if ContinuationNative.statPath(path, &value) <> 0 then invalidOp "ci-continuation-path-unavailable"
        value

    let private continuationReadAncestry (directory: string) =
        if not (OperatingSystem.IsLinux()) || RuntimeInformation.ProcessArchitecture <> Architecture.X64 then
            invalidOp "ci-continuation-private-custody-platform-unsupported"
        if not (Path.IsPathFullyQualified directory) || Path.GetFullPath directory <> directory then
            invalidOp "ci-continuation-private-path-must-be-canonical-absolute"
        let owner = ContinuationNative.geteuid()
        let rec collect (info: DirectoryInfo) retained =
            if not info.Exists || not (isNull info.LinkTarget) then invalidOp "ci-continuation-unsafe-ancestor"
            let value = continuationStat info.FullName
            if value.Mode &&& 0o170000u <> 0o040000u || value.Mode &&& 0o022u <> 0u
               || (value.User <> owner && value.User <> 0u) then invalidOp "ci-continuation-unsafe-ancestor"
            let row = info.FullName, value.Device, value.Inode, value.User, value.Mode
            if isNull info.Parent then row :: retained else collect info.Parent (row :: retained)
        collect (DirectoryInfo directory) []

    let private continuationAncestry (directory: string) =
        let anchors = continuationReadAncestry directory
        let leaf = continuationStat directory
        if leaf.User <> ContinuationNative.geteuid() || leaf.Mode &&& 0o777u <> 0o700u then invalidOp "ci-continuation-private-directory-owner-mode"
        anchors

    let private withContinuationLock directory action =
        let anchors = continuationAncestry directory
        let lockPath = Path.Combine(directory, "publication.lock")
        // O_RDWR | O_CREAT | O_NONBLOCK | O_NOFOLLOW | O_CLOEXEC. Never truncate/unlink lock.
        let descriptor = ContinuationNative.openPrivateFile(lockPath, 0xA0842, 0o600u)
        if descriptor < 0 then invalidOp "ci-continuation-private-lock-open-refused"
        use handle = new Microsoft.Win32.SafeHandles.SafeFileHandle(nativeint descriptor, true)
        let mutable locked = false
        let mutable birth = Unchecked.defaultof<ContinuationNative.Stat>
        let valid (value: ContinuationNative.Stat) =
            value.Mode &&& 0o170000u = 0o100000u && value.Mode &&& 0o777u = 0o600u
            && value.User = ContinuationNative.geteuid() && value.Links = 1UL
        let guard () =
            if continuationAncestry directory <> anchors || not (isNull (FileInfo(lockPath).LinkTarget)) then
                invalidOp "ci-continuation-private-ancestry-drift"
            let mutable opened = Unchecked.defaultof<ContinuationNative.Stat>
            let named = continuationStat lockPath
            if ContinuationNative.fstat(descriptor, &opened) <> 0 || not (valid opened) || not (valid named)
               || opened.Device <> birth.Device || opened.Inode <> birth.Inode
               || named.Device <> birth.Device || named.Inode <> birth.Inode then
                invalidOp "ci-continuation-private-lock-custody-lost"
        try
            if ContinuationNative.fstat(descriptor, &birth) <> 0 || not (valid birth) then
                invalidOp "ci-continuation-private-lock-shape"
            guard ()
            if ContinuationNative.flock(descriptor, ContinuationNative.LockExclusive ||| ContinuationNative.LockNonBlocking) <> 0 then
                invalidOp "ci-continuation-publication-busy"
            locked <- true
            guard ()
            let result = action guard
            guard ()
            result
        finally
            if locked then ContinuationNative.flock(descriptor, 8) |> ignore

    let continuationPrivateCustodyForTesting directory action =
        try withContinuationLock directory action with error -> Error [ error.Message ]

    let canCreateAdmission
        (outcome: string)
        (codeDelivery: string)
        (observedHead: string option)
        (expectedHead: string)
        =
        outcome = "ready"
        && codeDelivery = "not-delivered"
        && observedHead = Some expectedHead

    let private option name args =
        args
        |> List.indexed
        |> List.tryPick (fun (index, value) -> if value = name then List.tryItem (index + 1) args else None)

    let private legacyRoot args =
        option "--store-root" args
        |> Option.orElseWith (fun () ->
            Environment.GetEnvironmentVariable("FSGG_TELEMETRY_STORE")
            |> Option.ofObj
            |> Option.filter (String.IsNullOrWhiteSpace >> not))

    let private fail reasons =
        reasons
        |> List.iter (fun reason -> Console.Error.WriteLine("fsgg-coord-engine: telemetry ci: " + reason))

        1

    let private addOptional (node: JsonObject) (name: string) (value: string option) =
        node[name] <-
            match value with
            | Some text -> JsonValue.Create text
            | None -> null

    let private identity (prefix: string) (parts: string list) =
        prefix
        + CanonicalJson.sha256(Encoding.UTF8.GetBytes(String.concat "\u001f" parts)).Substring(0, 40)

    let private common (kind: string) (nativeIdentity: string) (item: string) =
        let node = JsonObject()
        node["kind"] <- kind
        node["identity"] <- nativeIdentity
        node["itemId"] <- item
        node["revision"] <- 0
        node

    let private stateRevision status =
        match status with
        | "completed" -> 2
        | "in_progress" -> 1
        | _ -> 0

    let private eventBatch
        (assignment: TelemetryCi.Assignment)
        (collection: string)
        (index: int)
        (events: JsonObject list)
        =
        let root = JsonObject()
        let values = JsonArray()
        events |> List.iter (fun event -> values.Add(event.DeepClone()))

        let contentKey =
            CanonicalJson.sha256(Encoding.UTF8.GetBytes(values.ToJsonString())).Substring(0, 16)

        root["schema"] <- TelemetryStore.BatchSchema
        root["ingestId"] <- $"ci-%s{collection.Substring(0, 24)}-%04d{index}-%s{contentKey}"
        root["sourceIdentity"] <- assignment.ProducerStream
        root["generation"] <- collection
        root["cursor"] <- $"%04d{index}-%s{contentKey}"
        root["eventCount"] <- events.Length
        root["events"] <- values
        Encoding.UTF8.GetBytes(root.ToJsonString(JsonSerializerOptions(WriteIndented = false)))

    let private boundedBatchesUnder maximumBytes (assignment: TelemetryCi.Assignment) (collection: string) (events: JsonObject list) =
        let rec loop (index: int) (current: JsonObject list) (remaining: JsonObject list) (output: byte array list) =
            match remaining with
            | [] ->
                if current.IsEmpty then
                    Ok(List.rev output)
                else
                    Ok(List.rev (eventBatch assignment collection index (List.rev current) :: output))
            | event :: tail ->
                let candidate = event :: current
                let bytes = eventBatch assignment collection index (List.rev candidate)

                if
                    candidate.Length <= TelemetryStore.MaxEvents
                    && bytes.Length <= maximumBytes
                then
                    loop index candidate tail output
                elif current.IsEmpty then
                    Error [ "projected CI observation exceeds the immutable event bound" ]
                else
                    loop (index + 1) [] remaining (eventBatch assignment collection index (List.rev current) :: output)

        loop 1 [] events []

    let private boundedBatches assignment collection events =
        boundedBatchesUnder TelemetryStore.MaxBatchBytes assignment collection events

    let normalizePendingBytesForTesting (bytes: byte array) =
        if isNull bytes then invalidArg "bytes" "pending bytes are unavailable"
        elif bytes.Length = 0 || bytes[bytes.Length - 1] <> byte '\n' then Array.append bytes [| byte '\n' |]
        else Array.copy bytes

    type private Rule =
        {
            Workflow: string
            Job: string
            Step: string
            Classification: string
            Rationale: string
        }

    type private DeliveryBinding =
        {
            Repository: string
            PullRequest: int
            BaseRef: string
            BaseSha: string
            Head: string
            Outcome: string
            CodeDelivery: string
            ObservedHead: string option
            MergeCommit: string option
            OutcomeAt: string option
            ObservedAt: string
        }

    type private Profile =
        { Rules: Rule list; Available: bool; Evidence: string }

    let private parseRules (bytes: byte array) =
        try
            use document = JsonDocument.Parse bytes
            let closed (allowed: Set<string>) (node: JsonElement) =
                node.ValueKind = JsonValueKind.Object
                && (let names = node.EnumerateObject() |> Seq.map _.Name |> Seq.toList
                    names.Length = (names |> Set.ofList).Count
                    && Set.ofList names = allowed)
            let root = document.RootElement
            if not (closed (Set [ "schema"; "rules" ]) root)
               || root.GetProperty("schema").GetString() <> "fsgg.telemetry.ci-attribution/1"
               || root.GetProperty("rules").ValueKind <> JsonValueKind.Array then
                Error [ "attribution profile is malformed" ]
            else
                let rules =
                    root.GetProperty("rules").EnumerateArray()
                    |> Seq.map (fun value ->
                        if not (closed (Set [ "workflow"; "job"; "step"; "classification"; "rationale" ]) value) then
                            invalidOp "attribution rule fields are malformed"
                        { Workflow = value.GetProperty("workflow").GetString()
                          Job = value.GetProperty("job").GetString()
                          Step = value.GetProperty("step").GetString()
                          Classification = value.GetProperty("classification").GetString()
                          Rationale = value.GetProperty("rationale").GetString() })
                    |> Seq.toList
                if rules.Length > 1000 then Error [ "attribution profile exceeds rule bound" ]
                elif rules |> List.exists (fun rule ->
                    [ rule.Workflow; rule.Job; rule.Step; rule.Rationale ] |> List.exists String.IsNullOrWhiteSpace
                    || not (Regex.IsMatch(rule.Workflow, "^\\.github/workflows/[A-Za-z0-9_.-]+\\.ya?ml$"))
                    || not (Set.contains rule.Classification (Set [ "useful-validation"; "admin"; "necessary-setup"; "mixed"; "unclassified" ]))) then
                    Error [ "attribution profile contains an invalid literal rule" ]
                elif (rules |> List.map (fun rule -> rule.Workflow, rule.Job, rule.Step) |> Set.ofList).Count <> rules.Length then
                    Error [ "attribution profile contains duplicate or conflicting tuples" ]
                else Ok rules
        with _ -> Error [ "attribution profile is malformed" ]

    let private classify rules workflow job step =
        rules
        |> List.tryFind (fun rule -> rule.Workflow = workflow && rule.Job = job && rule.Step = step)
        |> Option.map (fun rule -> rule.Classification, rule.Rationale)
        |> Option.defaultValue ("unclassified", "no exact attribution rule")

    let classifyProfileForTesting bytes workflow job step =
        parseRules bytes |> Result.map (fun rules -> classify rules workflow job step)

    let private profileFromBytes repository head bytes =
        let evidence = "ci-profile:" + repository + "@" + head
        match bytes with
        | None -> { Rules = []; Available = false; Evidence = evidence + ":unavailable" }
        | Some bytes ->
            match parseRules bytes with
            | Error _ -> { Rules = []; Available = false; Evidence = evidence + ":malformed" }
            | Ok rules -> { Rules = rules; Available = true; Evidence = evidence + ":sha256:" + CanonicalJson.sha256 bytes }

    let private readRules (repository: string) (head: string) =
        let token =
            Environment.GetEnvironmentVariable("GITHUB_TOKEN")
            |> Option.ofObj
            |> Option.orElseWith (fun () -> Environment.GetEnvironmentVariable("GH_TOKEN") |> Option.ofObj)
        let bytes =
            match token with
            | None -> None
            | Some token ->
                use transport = new Transport.HttpTransport(Transport.apiBaseFromEnv (), token)
                match CiReads.readAttributionProfile (transport :> Transport.ISinglePageGitHubTransport) repository head with
                | Error _ -> None
                | Ok bytes -> bytes
        Ok(profileFromBytes repository head bytes)

    let private project (assignment: TelemetryCi.Assignment) (profile: Profile) (snapshot: CiReads.Snapshot) =
        let collection =
            CanonicalJson.sha256 (
                Encoding.UTF8.GetBytes(
                    $"%s{snapshot.Repository}\u001f%s{snapshot.Head}\u001f%d{snapshot.PullRequest}\u001f%s{snapshot.Workflow}\u001f%s{assignment.ItemId}"
                )
            )

        let binding =
            common "ci-binding" (identity "ci-binding-" [ collection ]) assignment.ItemId

        binding["revision"] <- if snapshot.Binding = "exact" then 1 else 0
        binding["collectionId"] <- collection
        binding["repository"] <- snapshot.Repository
        binding["head"] <- snapshot.Head
        binding["prNumber"] <- snapshot.PullRequest
        binding["workflow"] <- snapshot.Workflow
        binding["featureId"] <- assignment.FeatureId
        binding["attemptId"] <- assignment.AttemptId
        addOptional binding "parentAttemptId" assignment.ParentAttemptId
        binding["producerStream"] <- assignment.ProducerStream
        binding["binding"] <- snapshot.Binding

        let pages =
            snapshot.Pages
            |> List.map (fun page ->
                let node =
                    common
                        "ci-page"
                        (identity "ci-page-" [ collection; page.Resource; string page.Index ])
                        assignment.ItemId in

                node["collectionId"] <- collection
                node["resource"] <- page.Resource
                node["page"] <- page.Index
                node["count"] <- page.Count
                node["total"] <- page.Total
                node)

        let runs =
            snapshot.Runs
            |> List.filter (fun run -> run.Event <> "pull_request_target" || run.TargetAssociation = CiReads.Exact)
            |> List.map (fun run ->
                let node =
                    common
                        "ci-run"
                        (identity "ci-run-" [ snapshot.Repository; string run.Id; string run.Attempt ])
                        assignment.ItemId in

                node["revision"] <- stateRevision run.Status
                node["repository"] <- snapshot.Repository
                node["runId"] <- run.Id
                node["attempt"] <- run.Attempt
                node["workflow"] <- run.Workflow
                node["event"] <- run.Event
                node["head"] <- run.HeadSha
                node["status"] <- run.Status
                addOptional node "conclusion" run.Conclusion
                addOptional node "createdAt" run.CreatedAt
                addOptional node "startedAt" run.RunStartedAt
                addOptional node "updatedAt" run.UpdatedAt
                node)

        let jobs =
            snapshot.Jobs
            |> List.map (fun job ->
                let node =
                    common
                        "ci-job"
                        (identity
                            "ci-job-"
                            [ snapshot.Repository; string job.RunId; string job.Attempt; string job.Id ])
                        assignment.ItemId in

                node["revision"] <- stateRevision job.Status
                node["repository"] <- snapshot.Repository
                node["runId"] <- job.RunId
                node["attempt"] <- job.Attempt
                node["jobId"] <- job.Id
                node["name"] <- job.Name
                node["status"] <- job.Status
                addOptional node "conclusion" job.Conclusion
                addOptional node "createdAt" job.CreatedAt
                addOptional node "startedAt" job.StartedAt
                addOptional node "completedAt" job.CompletedAt
                node)

        let steps =
            snapshot.Jobs
            |> List.collect (fun job ->
                (if profile.Available then job.Steps else [])
                |> List.map (fun step ->
                    let workflowIdentity =
                        snapshot.Runs
                        |> List.tryFind (fun run -> run.Id = job.RunId && run.Attempt = job.Attempt)
                        |> Option.map _.Workflow
                        |> Option.defaultValue "*"

                    let classification, rationale = classify profile.Rules workflowIdentity job.Name step.Name

                    let node =
                        common
                            "ci-step"
                            (identity
                                "ci-step-"
                                [
                                    snapshot.Repository
                                    string job.RunId
                                    string job.Attempt
                                    string job.Id
                                    string step.Number
                                ])
                            assignment.ItemId

                    node["revision"] <- stateRevision step.Status
                    node["repository"] <- snapshot.Repository
                    node["runId"] <- job.RunId
                    node["attempt"] <- job.Attempt
                    node["jobId"] <- job.Id
                    node["number"] <- step.Number
                    node["name"] <- step.Name
                    node["status"] <- step.Status
                    addOptional node "conclusion" step.Conclusion
                    addOptional node "startedAt" step.StartedAt
                    addOptional node "completedAt" step.CompletedAt
                    node["classification"] <- classification
                    node["rationale"] <- rationale
                    node))

        let classificationCoverage =
            if
                steps.IsEmpty
                || steps
                   |> List.exists (fun step -> step["classification"].GetValue<string>() = "unclassified")
            then
                "unknown"
            else
                "complete"

        let coverageKey =
            String.concat
                "\u001f"
                [
                    snapshot.InventoryCoverage
                    snapshot.AttemptCoverage
                    snapshot.JobPageCoverage
                    snapshot.TerminalCoverage
                    snapshot.TimestampCoverage
                    snapshot.LineageCoverage
                    classificationCoverage
                    "unknown"
                    string snapshot.Pages.Length
                ]

        let coverage =
            common "ci-coverage" (identity "ci-coverage-" [ collection; coverageKey ]) assignment.ItemId

        coverage["collectionId"] <- collection
        coverage["inventory"] <- snapshot.InventoryCoverage
        coverage["attempts"] <- snapshot.AttemptCoverage
        coverage["jobPages"] <- snapshot.JobPageCoverage
        coverage["terminal"] <- snapshot.TerminalCoverage
        coverage["timestamps"] <- snapshot.TimestampCoverage
        coverage["lineage"] <- snapshot.LineageCoverage
        coverage["classification"] <- classificationCoverage
        coverage["criticalPath"] <- "unknown"

        let diagnostics =
            (Some profile.Evidence :: [ snapshot.Diagnostic ])
            |> List.choose id
            |> List.map (fun code ->
                let node =
                    common "diagnostic" (identity "ci-diagnostic-" [ collection; code ]) assignment.ItemId in

                node["code"] <- code
                node["severity"] <- "warning"
                node)


        collection, binding :: (pages @ runs @ jobs @ steps @ [ coverage ] @ diagnostics)

    let private readAssignment path =
        try
            let full = Path.GetFullPath path
            let info = FileInfo full

            if not (Path.IsPathFullyQualified path) then
                Error [ "assignment path must be absolute" ]
            elif not info.Exists || not (isNull info.LinkTarget) then
                Error [ "assignment must be a regular non-symlink file" ]
            elif info.Length > 8192L then
                Error [ "assignment exceeds 8 KiB" ]
            elif
                not (OperatingSystem.IsWindows())
                && File.GetUnixFileMode(full) <> (UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
            then
                Error [ "assignment permissions must be 0600" ]
            else
                File.ReadAllBytes full |> TelemetryCi.parseAssignment
        with error ->
            Error [ "assignment is unavailable: " + error.Message ]

    let private parseDeliveryBytes (bytes: byte array) =
        try
            use document = JsonDocument.Parse bytes
            let value = document.RootElement

            let text (name: string) =
                match value.TryGetProperty name with
                | true, field when field.ValueKind = JsonValueKind.String -> Some(field.GetString())
                | _ -> None

            let number (name: string) =
                match value.TryGetProperty name with
                | true, field when field.ValueKind = JsonValueKind.Number ->
                    match field.TryGetInt32() with
                    | true, result -> Some result
                    | _ -> None
                | _ -> None

            let observed = text "observedHead"
            let mergeCommit = text "mergeCommit"
            let outcomeAt = text "outcomeAt"
            let observedAt = text "observedAt"

            match
                text "schema",
                text "repo",
                number "pr",
                text "baseRef",
                text "baseSha",
                text "expectedHead",
                text "outcome",
                text "codeDelivery",
                observedAt
            with
            | Some "fsgg.routine-delivery/v1",
              Some repository,
              Some pr,
              Some baseRef,
              Some baseSha,
              Some head,
              Some outcome,
              Some codeDelivery,
              Some observedAt when
                Regex.IsMatch(repository, "^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")
                && pr > 0
                && Regex.IsMatch(head, "^[0-9a-f]{40}$")
                && not (String.IsNullOrWhiteSpace baseRef)
                && Regex.IsMatch(baseSha, "^[0-9a-f]{40}$")
                && mergeCommit
                   |> Option.forall (fun value -> Regex.IsMatch(value, "^[0-9a-f]{40}$"))
                && [ Some observedAt; outcomeAt ]
                   |> List.choose id
                   |> List.forall (fun value -> DateTimeOffset.TryParse(value) |> fst)
                && Set.contains
                    outcome
                    (Set
                        [
                            "ready"
                            "refused"
                            "delivered"
                            "delivered-after-readback"
                            "delivered-disputed"
                            "indeterminate"
                        ])
                && Set.contains codeDelivery (Set [ "delivered"; "not-delivered"; "unknown" ])
                ->
                Ok
                    {
                        Repository = repository
                        PullRequest = pr
                        BaseRef = baseRef
                        BaseSha = baseSha
                        Head = head
                        Outcome = outcome
                        CodeDelivery = codeDelivery
                        ObservedHead = observed
                        MergeCommit = mergeCommit
                        OutcomeAt = outcomeAt
                        ObservedAt = observedAt
                    }
            | _ ->
                Error
                    [
                        "delivery is not a valid exact-head fsgg.routine-delivery/v1 candidate binding"
                    ]
        with error -> Error [ "delivery is unavailable: " + error.Message ]

    let private readDelivery path =
        try
            let full = Path.GetFullPath path
            let info = FileInfo full

            if not (Path.IsPathFullyQualified path) then
                Error [ "delivery path must be absolute" ]
            elif not info.Exists || not (isNull info.LinkTarget) then
                Error [ "delivery must be a regular non-symlink file" ]
            elif info.Length > 16384L then
                Error [ "delivery exceeds 16 KiB" ]
            else
                File.ReadAllBytes full |> parseDeliveryBytes
        with error ->
            Error [ "delivery is unavailable: " + error.Message ]

    let private projectPopulation
        (assignment: TelemetryCi.Assignment)
        (profile: Profile)
        (population: CiReads.PopulationSnapshot)
        =
        let snapshot = population.Snapshot

        let collection =
            CanonicalJson.sha256 (
                Encoding.UTF8.GetBytes(
                    $"%s{snapshot.Repository}\u001f%s{snapshot.Head}\u001f%d{snapshot.PullRequest}\u001f%s{assignment.ItemId}\u001fall-workflows"
                )
            )

        let revision = population.Revision

        let revised kind nativeIdentity =
            let node = common kind nativeIdentity assignment.ItemId in
            node["revision"] <- revision
            node

        let admission =
            if population.AdmissionWitness then
                let node =
                    common
                        "ci-population-admission"
                        (identity "ci-population-admission-" [ collection ])
                        assignment.ItemId

                node["revision"] <- 1
                node["collectionId"] <- collection
                node["repository"] <- snapshot.Repository
                node["prNumber"] <- snapshot.PullRequest
                node["baseRef"] <- population.BaseRef.Value
                node["baseSha"] <- population.BaseSha.Value
                node["head"] <- snapshot.Head
                node["witness"] <- "native-pr-head"
                [ node ]
            else
                []

        let binding = revised "ci-binding" (identity "ci-binding-" [ collection ])
        binding["collectionId"] <- collection
        binding["repository"] <- snapshot.Repository
        binding["head"] <- snapshot.Head
        binding["prNumber"] <- snapshot.PullRequest
        binding["workflow"] <- "*"
        binding["featureId"] <- assignment.FeatureId
        binding["attemptId"] <- assignment.AttemptId
        addOptional binding "parentAttemptId" assignment.ParentAttemptId
        binding["producerStream"] <- assignment.ProducerStream
        binding["binding"] <- snapshot.Binding

        let pages =
            snapshot.Pages
            |> List.map (fun page ->
                let node =
                    revised "ci-page" (identity "ci-page-" [ collection; page.Resource; string page.Index ]) in

                node["collectionId"] <- collection
                node["resource"] <- page.Resource
                node["page"] <- page.Index
                node["count"] <- page.Count
                node["total"] <- page.Total
                node)

        let runs =
            snapshot.Runs
            |> List.filter (fun run -> run.Event <> "pull_request_target" || run.TargetAssociation = CiReads.Exact)
            |> List.map (fun run ->
                let node =
                    revised "ci-run" (identity "ci-run-" [ snapshot.Repository; string run.Id; string run.Attempt ]) in

                node["repository"] <- snapshot.Repository
                node["runId"] <- run.Id
                node["attempt"] <- run.Attempt
                node["workflow"] <- run.Workflow
                node["event"] <- run.Event
                node["head"] <- run.HeadSha
                node["status"] <- run.Status
                addOptional node "conclusion" run.Conclusion
                addOptional node "createdAt" run.CreatedAt
                addOptional node "startedAt" run.RunStartedAt
                addOptional node "updatedAt" run.UpdatedAt
                node)

        let jobs =
            snapshot.Jobs
            |> List.map (fun job ->
                let node =
                    revised
                        "ci-job"
                        (identity
                            "ci-job-"
                            [ snapshot.Repository; string job.RunId; string job.Attempt; string job.Id ]) in

                node["repository"] <- snapshot.Repository
                node["runId"] <- job.RunId
                node["attempt"] <- job.Attempt
                node["jobId"] <- job.Id
                node["name"] <- job.Name
                node["status"] <- job.Status
                addOptional node "conclusion" job.Conclusion
                addOptional node "createdAt" job.CreatedAt
                addOptional node "startedAt" job.StartedAt
                addOptional node "completedAt" job.CompletedAt
                node)

        let steps =
            snapshot.Jobs
            |> List.collect (fun job ->
                (if profile.Available then job.Steps else [])
                |> List.map (fun step ->
                    let workflow =
                        snapshot.Runs
                        |> List.tryFind (fun run -> run.Id = job.RunId && run.Attempt = job.Attempt)
                        |> Option.map _.Workflow
                        |> Option.defaultValue "*"

                    let classification, rationale = classify profile.Rules workflow job.Name step.Name

                    let node =
                        revised
                            "ci-step"
                            (identity
                                "ci-step-"
                                [
                                    snapshot.Repository
                                    string job.RunId
                                    string job.Attempt
                                    string job.Id
                                    string step.Number
                                ])

                    node["repository"] <- snapshot.Repository
                    node["runId"] <- job.RunId
                    node["attempt"] <- job.Attempt
                    node["jobId"] <- job.Id
                    node["number"] <- step.Number
                    node["name"] <- step.Name
                    node["status"] <- step.Status
                    addOptional node "conclusion" step.Conclusion
                    addOptional node "startedAt" step.StartedAt
                    addOptional node "completedAt" step.CompletedAt
                    node["classification"] <- classification
                    node["rationale"] <- rationale
                    node))

        let checks =
            population.Checks
            |> List.map (fun check ->
                let node =
                    revised "ci-check" (identity "ci-check-" [ snapshot.Repository; string check.Id ]) in

                node["repository"] <- snapshot.Repository
                node["checkId"] <- check.Id
                node["name"] <- check.Name
                addOptional node "appSlug" check.AppSlug
                node["status"] <- check.Status
                addOptional node "conclusion" check.Conclusion
                addOptional node "startedAt" check.StartedAt
                addOptional node "completedAt" check.CompletedAt
                node)

        let gaps = JsonSerializer.Serialize(Array.ofList population.Gaps)

        let coverage =
            revised "ci-population-coverage" (identity "ci-population-coverage-" [ collection ])

        coverage["collectionId"] <- collection
        coverage["actions"] <- snapshot.InventoryCoverage
        coverage["checks"] <- population.CheckCoverage
        coverage["attempts"] <- snapshot.AttemptCoverage
        coverage["jobs"] <- snapshot.JobPageCoverage
        coverage["terminal"] <- snapshot.TerminalCoverage
        coverage["timestamps"] <- snapshot.TimestampCoverage
        coverage["continuation"] <- (if population.Pending.IsEmpty then "none" else "pending")
        coverage["externalChecks"] <- population.ExternalChecks
        coverage["gaps"] <- gaps
        let oldCoverage = revised "ci-coverage" (identity "ci-coverage-" [ collection ])

        let classification =
            if
                steps.IsEmpty
                || steps
                   |> List.exists (fun step -> step["classification"].GetValue<string>() = "unclassified")
            then
                "unknown"
            else
                "complete"

        oldCoverage["collectionId"] <- collection
        oldCoverage["inventory"] <- snapshot.InventoryCoverage
        oldCoverage["attempts"] <- snapshot.AttemptCoverage
        oldCoverage["jobPages"] <- snapshot.JobPageCoverage
        oldCoverage["terminal"] <- snapshot.TerminalCoverage
        oldCoverage["timestamps"] <- snapshot.TimestampCoverage
        oldCoverage["lineage"] <- snapshot.LineageCoverage
        oldCoverage["classification"] <- classification
        oldCoverage["criticalPath"] <- "unknown"

        let diagnostics =
            (profile.Evidence :: (population.Pending @ population.Gaps))
            |> List.distinct
            |> List.map (fun code ->
                let node = revised "diagnostic" (identity "ci-diagnostic-" [ collection; code ]) in
                node["code"] <- code
                node["severity"] <- "warning"
                node)

        collection,
        admission
        @ [ binding ]
        @ pages
        @ runs
        @ jobs
        @ steps
        @ checks
        @ (if profile.Available then [ oldCoverage; coverage ] else [ coverage ])
        @ diagnostics

    let projectPopulationForTesting profileBytes assignment (population: CiReads.PopulationSnapshot) =
        let profile = profileFromBytes population.Snapshot.Repository population.Snapshot.Head profileBytes
        let collection, events = projectPopulation assignment profile population
        boundedBatches assignment collection events


    let private readContinuationInput (path: string) (maximum: int) publicRead (guard: unit -> unit) =
        guard ()
        if not (Path.IsPathFullyQualified path) || Path.GetFullPath path <> path then invalidOp "ci-continuation-input-path-shape"
        let descriptor = ContinuationNative.openPrivateFile(path, 0xA0800, 0u)
        if descriptor < 0 then invalidOp "ci-continuation-private-intent-open-refused"
        use handle = new Microsoft.Win32.SafeHandles.SafeFileHandle(nativeint descriptor, true)
        let valid (value: ContinuationNative.Stat) =
            value.Mode &&& 0o170000u = 0o100000u && (value.Mode &&& 0o777u = 0o600u || (publicRead && value.Mode &&& 0o777u = 0o644u))
            && value.User = ContinuationNative.geteuid() && value.Links = 1UL
            && value.Size > 0L && value.Size <= int64 maximum
        let stable (value: ContinuationNative.Stat) =
            value.Device, value.Inode, value.Mode, value.User, value.Group, value.Links, value.Size,
            value.ModifySeconds, value.ModifyNanoseconds, value.ChangeSeconds, value.ChangeNanoseconds
        let mutable before = Unchecked.defaultof<ContinuationNative.Stat>
        if ContinuationNative.fstat(descriptor, &before) <> 0 || not (valid before) then
            invalidOp "ci-continuation-private-intent-shape-or-bound"
        use stream = new FileStream(handle, FileAccess.Read)
        let bytes = Array.zeroCreate<byte> (int before.Size)
        stream.ReadExactly bytes
        let extra = stream.ReadByte()
        let mutable after = Unchecked.defaultof<ContinuationNative.Stat>
        if extra <> -1 || ContinuationNative.fstat(descriptor, &after) <> 0 || not (valid after) then
            invalidOp "ci-continuation-private-intent-custody-or-byte-drift"
        let named = continuationStat path
        guard ()
        if not (isNull (FileInfo(path).LinkTarget)) || not (valid named)
           || stable before <> stable after || stable before <> stable named then
            invalidOp "ci-continuation-private-intent-custody-or-byte-drift"
        // Nonwritable ancestry excludes other-user substitution. These barriers detect observed
        // same-user namespace drift; they do not atomically exclude a concurrent same-user rename.
        bytes

    let private readContinuationIntent (directory: string) (name: string) (maximum: int) (guard: unit -> unit) =
        if not (Regex.IsMatch(name, "^continuation-[a-f0-9]{64}$")) then invalidOp "ci-continuation-private-name-shape"
        readContinuationInput (Path.Combine(directory, name + ".json")) maximum false guard

    let private persistContinuationIntent (directory: string) (name: string) (maximum: int) (guard: unit -> unit) (bytes: byte array) =
        guard ()
        if not (Regex.IsMatch(name, "^continuation-[a-f0-9]{64}$")) then invalidOp "ci-continuation-private-name-shape"
        let normalized = normalizePendingBytesForTesting bytes
        if normalized.Length > maximum then invalidOp "ci-continuation-private-intent-bound"
        let prior = FileInfo(Path.Combine(directory, name + ".json"))
        if not (isNull prior.LinkTarget) then invalidOp "ci-continuation-private-intent-symlink"
        if prior.Exists then readContinuationIntent directory name maximum guard |> ignore
        SkillTelemetryAdapter.writePrivateBytes directory name normalized |> ignore
        guard ()
        let actual = readContinuationIntent directory name maximum guard
        if actual <> normalized then invalidOp "ci-continuation-private-intent-exact-byte-mismatch"
        actual

    let continuationExactIntentForTesting directory name bytes =
        try
            withContinuationLock directory (fun guard ->
                persistContinuationIntent directory name (8 * 1024 * 1024) guard bytes |> Ok)
        with error -> Error [ error.Message ]

    // Pure immutable phase preparation. The caller supplies independent receiver admission/custody
    // before these bytes may be persisted or sent; private provider state is never that authority.
    let private projectContinuation
        (expected: TelemetryCi.ProviderCheckpoint.Identity)
        (now: DateTimeOffset)
        admitted
        receiverRevisionFloor
        profileBytes
        (existingReceiverFacts: TelemetryStore.Fact list)
        (checkpoint: TelemetryCi.ProviderCheckpoint.Checkpoint)
        (population: CiReads.PopulationSnapshot)
        =
        try
            let require condition reason = if not condition then invalidOp reason
            let pages =
                match CiReads.retainedSuiteGraph expected now admitted checkpoint with
                | Ok value -> value
                | Error error -> invalidOp (string error)
            require (checkpoint.Phase = TelemetryCi.ProviderCheckpoint.Stable) "ci-suite-stable-source-preparation-required"
            require (checkpoint.FirstSweepDigest = TelemetryCi.ProviderCheckpoint.populationDigest checkpoint.Resources
                     && checkpoint.SecondSweepDigest = checkpoint.FirstSweepDigest) "ci-suite-current-final-barrier-binding"
            require (population.Revision = checkpoint.Revision && population.Snapshot.Repository = expected.Repository
                     && population.Snapshot.Head = expected.Head && int64 population.Snapshot.PullRequest = expected.PullRequest
                     && population.BaseRef = Some expected.BaseRef && population.BaseSha = Some expected.BaseSha) "ci-suite-population-scope"
            require (TelemetryCi.ProviderCheckpoint.digest (profileBytes |> Option.defaultValue Array.empty) = expected.ProfileDigest) "ci-suite-profile-byte-binding"
            let observedChecks = pages |> List.collect _.CheckObservations |> List.sortBy _.Id
            require (observedChecks = (population.Checks |> List.sortBy _.Id)) "ci-suite-native-check-observation-correspondence"
            let profile = profileFromBytes expected.Repository expected.Head profileBytes
            require profile.Available "ci-suite-valid-attribution-profile-required"
            let compatible =
                { population with
                    Snapshot =
                        { population.Snapshot with
                            Pages =
                                population.Snapshot.Pages |> List.filter (fun page ->
                                    not (page.Resource.StartsWith("Suites:", StringComparison.Ordinal)
                                         || page.Resource.StartsWith("SuiteChecks:", StringComparison.Ordinal))) } }
            let collection, originals = projectPopulation expected.Assignment profile compatible
            require (collection = expected.Collection) "ci-suite-collection-identity-binding"
            let next prior =
                match TelemetryCi.ProviderCheckpoint.nextRevision prior receiverRevisionFloor with
                | Ok value -> value
                | Error errors -> invalidOp (String.concat ";" errors)
            let fenceRevision = next checkpoint.Revision
            let graphRevision = next fenceRevision
            let sealRevision = next graphRevision
            let fenceKinds = set [ "ci-population-admission"; "ci-binding"; "ci-coverage"; "ci-population-coverage" ]
            let fence, observations =
                originals |> List.map (fun event ->
                    let copy = event.DeepClone().AsObject()
                    let kind = copy["kind"].GetValue<string>()
                    if kind <> "ci-population-admission" then
                        copy["revision"] <- (if fenceKinds.Contains kind then fenceRevision else graphRevision)
                    if kind = "ci-population-coverage" then
                        copy["checks"] <- "partial"
                        copy["continuation"] <- "pending"
                    copy)
                |> List.partition (fun event -> fenceKinds.Contains(event["kind"].GetValue<string>()))
            let parseEvent (event: JsonObject) =
                match TelemetryStore.parseBatch (eventBatch expected.Assignment collection 0 [ event ]) with
                | Ok parsed -> parsed.Facts.Head
                | Error errors -> invalidOp (String.concat ";" errors)
            let reference (fact: TelemetryStore.Fact) =
                JsonNode.Parse(JsonSerializer.Serialize {| identity = fact.Identity; revision = fact.Revision; digest = fact.ContentDigest |})
            let dependency kind =
                match fence |> List.filter (fun event -> event["kind"].GetValue<string>() = kind) with
                | [ event ] -> parseEvent event
                | [] when kind = "ci-population-admission" && admitted ->
                    match existingReceiverFacts |> List.filter (fun fact ->
                        fact.ItemId = Some expected.Assignment.ItemId &&
                        match fact.Payload with
                        | TelemetryStore.CiPopulationAdmission(collectionId, repository, pr, baseRef, baseSha, head, witness) ->
                            collectionId = collection && repository = expected.Repository && pr = expected.PullRequest
                            && baseRef = expected.BaseRef && baseSha = expected.BaseSha && head = expected.Head && witness = "native-pr-head"
                        | _ -> false) with
                    | [ fact ] -> fact
                    | _ -> invalidOp "ci-suite-existing-authenticated-admission-required"
                | _ -> invalidOp "ci-suite-exact-fence-dependency-required"
            let checkFacts =
                observations |> List.map parseEvent |> List.choose (fun fact ->
                    match fact.Payload with
                    | TelemetryStore.CiCheck(_, id, _, _, _, _, _, _) -> Some(id, fact)
                    | _ -> None) |> Map.ofList
            let scope =
                JsonNode.Parse(JsonSerializer.Serialize
                    {| collectionId = collection; generation = checkpoint.Generation; repository = expected.Repository
                       repositoryId = expected.RepositoryId; pullRequest = expected.PullRequest; baseRef = expected.BaseRef
                       baseSha = expected.BaseSha; head = expected.Head; assignmentDigest = expected.AssignmentDigest
                       profileDigest = expected.ProfileDigest; parserContract = expected.ParserContract
                       producerStream = expected.Assignment.ProducerStream |})
            let parts = ResizeArray<JsonObject>()
            for page in pages do
                let inventory = page.Key.Kind = TelemetryCi.ProviderCheckpoint.Suites
                let rows: JsonNode list =
                    if inventory then
                        page.Suites |> List.map (fun row ->
                            JsonNode.Parse(JsonSerializer.Serialize {| suiteId = row.SuiteId; appId = row.AppId; appSlug = row.AppSlug |> Option.toObj; status = row.Status; conclusion = row.Conclusion |> Option.toObj |}))
                    else
                        page.Checks |> List.map (fun row ->
                            let fact = Map.tryFind row.CheckId checkFacts |> Option.defaultWith (fun () -> invalidOp "ci-suite-check-reference-missing")
                            let node = JsonObject()
                            node["checkId"] <- row.CheckId
                            node["appId"] <- row.AppId
                            addOptional node "appSlug" row.AppSlug
                            node["checkRef"] <- reference fact
                            node :> JsonNode)
                require (rows.Length = page.Page.Count) "ci-suite-native-page-row-count"
                let makePage (fragment: int) (count: int) (offset: int) (selected: JsonNode list) =
                    let event = common "ci-suite-page/1"
                                    (identity "ci-suite-page-" [ collection; string checkpoint.Generation; string page.Key.Kind; string page.Key.SuiteId; string page.Page.Index; string fragment ])
                                    expected.Assignment.ItemId
                    event["revision"] <- graphRevision
                    event["scope"] <- scope.DeepClone()
                    event["resourceKind"] <- (if inventory then "suites" else "suite-checks")
                    event["suiteId"] <- (if inventory then null else JsonValue.Create page.Key.SuiteId :> JsonNode)
                    event["page"] <- page.Page.Index
                    event["nativeCount"] <- page.Page.Count
                    event["nativeTotal"] <- page.Page.Total
                    event["nextPage"] <- (match page.NextPage with None -> null | Some nextPage -> JsonValue.Create nextPage :> JsonNode)
                    event["bodyDigest"] <- page.BodyDigest
                    event["observedAt"] <- page.ObservedAt.ToString("O")
                    event["validatedAt"] <- page.ValidatedAt.ToString("O")
                    event["fragmentIndex"] <- fragment
                    event["fragmentCount"] <- count
                    event["rowOffset"] <- offset
                    let values = JsonArray()
                    selected |> List.iter (fun (row: JsonNode) -> values.Add(row.DeepClone()))
                    event["rows"] <- values
                    event
                let fits index offset selected =
                    let event = makePage index 100 offset selected
                    Encoding.UTF8.GetByteCount(event.ToJsonString()) <= TelemetryStore.MaxBatchBytes - 1024
                let rec partition index offset current remaining output =
                    match remaining with
                    | [] -> List.rev (List.rev current :: output)
                    | row :: tail ->
                        let candidate = List.rev (row :: current)
                        if fits index offset candidate then partition index offset (row :: current) tail output
                        elif current.IsEmpty then invalidOp "ci-suite-single-row-unrepresentable"
                        else partition (index + 1) (offset + current.Length) [] remaining (List.rev current :: output)
                let fragments = if rows.IsEmpty then [ [] ] else partition 0 0 [] rows []
                let mutable offset = 0
                for index, fragment in List.indexed fragments do
                    let event = makePage index fragments.Length offset fragment
                    parseEvent event |> ignore
                    parts.Add event
                    offset <- offset + fragment.Length
            let partFacts = parts |> Seq.map parseEvent |> Seq.toList
            let first = checkpoint.Resources |> List.minBy _.ObservedAt |> fun resource -> resource.ObservedAt
            let seal = common "ci-suite-seal/1" (identity "ci-suite-seal-" [ collection; string checkpoint.Generation ]) expected.Assignment.ItemId
            seal["revision"] <- sealRevision
            seal["scope"] <- scope.DeepClone()
            seal["admissionRef"] <- reference (dependency "ci-population-admission")
            seal["bindingRef"] <- reference (dependency "ci-binding")
            seal["coverageRef"] <- reference (dependency "ci-coverage")
            seal["populationCoverageRef"] <- reference (dependency "ci-population-coverage")
            seal["graphDigest"] <- TelemetryStore.ciSuiteFactGraphDigest partFacts
            seal["resourceCount"] <- pages.Length
            seal["fragmentCount"] <- parts.Count
            seal["suiteCount"] <- pages |> List.sumBy (fun page -> page.Suites.Length)
            seal["checkCount"] <- observedChecks.Length
            seal["firstSweepDigest"] <- checkpoint.FirstSweepDigest
            seal["secondSweepDigest"] <- checkpoint.SecondSweepDigest
            seal["barrierDigest"] <- TelemetryCi.ProviderCheckpoint.populationDigest checkpoint.Resources
            seal["observedFrom"] <- first.ToString("O")
            seal["observedThrough"] <- checkpoint.ObservedThrough.ToString("O")
            let sealFact = parseEvent seal
            match sealFact.Payload with
            | TelemetryStore.CiSuiteSeal proof ->
                let prepared = (fence @ observations |> List.map parseEvent) @ partFacts
                let replaced = prepared |> List.map _.Identity |> Set.ofList
                let retained = existingReceiverFacts |> List.filter (fun fact -> not (replaced.Contains fact.Identity))
                match TelemetryStore.validateCiSuiteGraph expected.Assignment.ItemId proof (prepared @ retained) with
                | Ok() -> ()
                | Error errors -> invalidOp (String.concat ";" errors)
            | _ -> invalidOp "ci-suite-seal-codec-required"
            let batch phase events =
                match boundedBatchesUnder (TelemetryStore.MaxBatchBytes - 1) expected.Assignment (collection + "-" + string checkpoint.Generation + "-" + phase) events with
                | Ok batches -> batches |> List.map normalizePendingBytesForTesting
                | Error errors -> invalidOp (String.concat ";" errors)
            let fenceBytes = batch "fence" fence
            let graphBytes = batch "graph" (observations @ List.ofSeq parts)
            let sealBytes = batch "seal" [ seal ]
            let manifest =
                JsonSerializer.SerializeToUtf8Bytes
                    {| schema = "fsgg.telemetry.ci-publication-phases/1"; storeId = expected.StoreId
                       collectionId = collection; generation = checkpoint.Generation
                       revisions = [| fenceRevision; graphRevision; sealRevision |]
                       phases = [| List.toArray fenceBytes; List.toArray graphBytes; List.toArray sealBytes |] |}
                |> normalizePendingBytesForTesting
            require (manifest.Length <= 8 * 1024 * 1024) "ci-suite-existing-pending-bundle-bound"
            Ok(fenceBytes, graphBytes, sealBytes)
        with error -> Error [ error.Message ]

    let projectContinuationForTesting expected now admitted receiverRevisionFloor profileBytes existingReceiverFacts checkpoint population =
        projectContinuation expected now admitted receiverRevisionFloor profileBytes existingReceiverFacts checkpoint population


    type private ContinuationPendingState =
        { Phase: int; Batch: int; Payloads: byte array list list }

    let private validateContinuationPending
        (expected: TelemetryCi.ProviderCheckpoint.Identity)
        (checkpoint: TelemetryCi.ProviderCheckpoint.Checkpoint)
        (existingReceiverFacts: TelemetryStore.Fact list)
        (state: ContinuationPendingState) =
        let require condition reason = if not condition then invalidOp reason
        require (state.Payloads.Length = 3 && (state.Payloads |> List.forall (List.isEmpty >> not))
                 && (state.Payloads |> List.sumBy List.length) <= 4096) "ci-continuation-phase-count"
        require (state.Phase >= 0 && state.Phase < 3 && state.Batch >= 0
                 && state.Batch < state.Payloads[state.Phase].Length) "ci-continuation-phase-cursor"
        let parsed =
            state.Payloads |> List.map (List.map (fun (bytes: byte array) ->
                require (bytes.Length > 0 && bytes.Length <= TelemetryStore.MaxBatchBytes && bytes[bytes.Length - 1] = byte '\n') "ci-continuation-exact-normalized-batch-bound"
                match TelemetryStore.parseBatch bytes with
                | Error errors -> invalidOp (String.concat ";" errors)
                | Ok batch ->
                    require (not batch.Facts.IsEmpty) "ci-continuation-nonempty-batch-required"
                    require (batch.SourceIdentity = expected.Assignment.ProducerStream
                             && (batch.Facts |> List.forall (fun fact -> fact.ItemId = Some expected.Assignment.ItemId))) "ci-continuation-phase-source-scope"
                    batch))
        let factsByPhase = parsed |> List.map (List.collect _.Facts)
        let fenceKinds = set [ "ci-population-admission"; "ci-binding"; "ci-coverage"; "ci-population-coverage" ]
        require (factsByPhase[0] |> List.forall (fun fact -> fenceKinds.Contains fact.Kind)) "ci-continuation-fence-only"
        for kind in [ "ci-binding"; "ci-coverage"; "ci-population-coverage" ] do
            require ((factsByPhase[0] |> List.filter (fun fact -> fact.Kind = kind)).Length = 1) "ci-continuation-exact-fence"
        require ((factsByPhase[0] |> List.filter (fun fact -> fact.Kind = "ci-population-admission")).Length <= 1) "ci-continuation-single-admission"
        require (factsByPhase[1] |> List.forall (fun fact -> not (fenceKinds.Contains fact.Kind) && fact.Kind <> "ci-suite-seal/1")) "ci-continuation-graph-phase-only"
        require (state.Payloads[2].Length = 1 && factsByPhase[2].Length = 1) "ci-continuation-separate-single-seal"
        let facts = factsByPhase |> List.collect id
        require (facts.Length <= 4096 && (facts |> List.map _.Identity |> Set.ofList).Count = facts.Length) "ci-continuation-reference-bound-or-duplicate"
        let proof =
            match factsByPhase[2].Head.Payload with
            | TelemetryStore.CiSuiteSeal seal -> seal
            | _ -> invalidOp "ci-continuation-final-seal-required"
        let scope = proof.Scope
        require (scope.CollectionId = expected.Collection && scope.Generation = checkpoint.Generation
                 && scope.Repository = expected.Repository && scope.RepositoryId = expected.RepositoryId
                 && scope.PullRequest = expected.PullRequest && scope.BaseRef = expected.BaseRef && scope.BaseSha = expected.BaseSha
                 && scope.Head = expected.Head && scope.AssignmentDigest = expected.AssignmentDigest
                 && scope.ProfileDigest = expected.ProfileDigest && scope.ParserContract = expected.ParserContract
                 && scope.ProducerStream = expected.Assignment.ProducerStream) "ci-continuation-seal-exact-candidate"
        require (proof.FirstSweepDigest = checkpoint.FirstSweepDigest && proof.SecondSweepDigest = checkpoint.SecondSweepDigest
                 && proof.BarrierDigest = TelemetryCi.ProviderCheckpoint.populationDigest checkpoint.Resources) "ci-continuation-retained-source-proof"
        let replaced = facts |> List.map _.Identity |> Set.ofList
        let retained = existingReceiverFacts |> List.filter (fun fact -> not (replaced.Contains fact.Identity))
        match TelemetryStore.validateCiSuiteGraph expected.Assignment.ItemId proof (facts @ retained) with
        | Error errors -> invalidOp (String.concat ";" errors)
        | Ok() -> facts

    let private encodeContinuationPending
        (expected: TelemetryCi.ProviderCheckpoint.Identity)
        (checkpoint: TelemetryCi.ProviderCheckpoint.Checkpoint)
        (state: ContinuationPendingState) =
        let bytes =
            JsonSerializer.SerializeToUtf8Bytes
                {| schema = "fsgg.telemetry.ci-publication-phases/1"; storeId = expected.StoreId
                   collectionId = expected.Collection; generation = checkpoint.Generation
                   phase = state.Phase; batch = state.Batch
                   phases = state.Payloads |> List.map List.toArray |> List.toArray |}
            |> normalizePendingBytesForTesting
        if bytes.Length > 8 * 1024 * 1024 then invalidOp "ci-continuation-pending-bundle-bound"
        bytes

    let private parseContinuationPending
        (expected: TelemetryCi.ProviderCheckpoint.Identity)
        (checkpoint: TelemetryCi.ProviderCheckpoint.Checkpoint)
        (bytes: byte array) =
        if isNull bytes || bytes.Length = 0 || bytes.Length > 8 * 1024 * 1024 then invalidOp "ci-continuation-pending-bundle-bound"
        use doc = JsonDocument.Parse bytes
        let root = doc.RootElement
        let fields = root.EnumerateObject() |> Seq.map _.Name |> Seq.toList
        let closed = [ "schema"; "storeId"; "collectionId"; "generation"; "phase"; "batch"; "phases" ]
        if fields.Length <> closed.Length || Set.ofList fields <> Set.ofList closed
           || root.GetProperty("schema").GetString() <> "fsgg.telemetry.ci-publication-phases/1"
           || root.GetProperty("storeId").GetString() <> expected.StoreId
           || root.GetProperty("collectionId").GetString() <> expected.Collection
           || root.GetProperty("generation").GetInt64() <> checkpoint.Generation then invalidOp "ci-continuation-pending-closed-candidate"
        let payloads =
            root.GetProperty("phases").EnumerateArray() |> Seq.map (fun phase ->
                phase.EnumerateArray() |> Seq.map (fun payload ->
                    let encoded = payload.GetString()
                    if isNull encoded || encoded.Length > 87384 then invalidOp "ci-continuation-pending-payload-encoded-bound"
                    Convert.FromBase64String encoded) |> Seq.toList) |> Seq.toList
        { Phase = root.GetProperty("phase").GetInt32(); Batch = root.GetProperty("batch").GetInt32(); Payloads = payloads }

    let prepareContinuationPendingForTesting expected now existingReceiverFacts checkpoint phases =
        let fence, graph, seal = phases
        try
            TelemetryCi.ProviderCheckpoint.validate expected now checkpoint |> Result.defaultWith (String.concat ";" >> invalidOp) |> ignore
            if checkpoint.Phase <> TelemetryCi.ProviderCheckpoint.Stable || checkpoint.PendingBatchDigest <> "" then invalidOp "ci-continuation-stable-preparation-required"
            let state = { Phase = 0; Batch = 0; Payloads = [ fence; graph; seal ] }
            let facts = validateContinuationPending expected checkpoint existingReceiverFacts state
            let bytes = encodeContinuationPending expected checkpoint state
            let refs: TelemetryCi.ProviderCheckpoint.FactReference list =
                facts |> List.map (fun fact -> ({ Kind = fact.Kind; Identity = fact.Identity; Revision = fact.Revision; Digest = fact.ContentDigest }: TelemetryCi.ProviderCheckpoint.FactReference)) |> List.sortBy _.Identity
            let pending =
                { checkpoint with
                    Phase = TelemetryCi.ProviderCheckpoint.PublicationPending
                    Revision = facts |> List.map _.Revision |> List.fold max checkpoint.Revision
                    PendingBatchDigest = TelemetryCi.ProviderCheckpoint.digest bytes
                    PendingBatchBase64 = Convert.ToBase64String bytes
                    FactReferences = refs }
            TelemetryCi.ProviderCheckpoint.encode expected now pending |> Result.map normalizePendingBytesForTesting
        with error -> Error [ error.Message ]

    let recoverContinuationPendingForTesting expected now existingReceiverFacts checkpointBytes =
        try
            let checkpoint = TelemetryCi.ProviderCheckpoint.parse expected now checkpointBytes |> Result.defaultWith (String.concat ";" >> invalidOp)
            if checkpoint.Phase <> TelemetryCi.ProviderCheckpoint.PublicationPending then invalidOp "ci-continuation-pending-checkpoint-required"
            let bytes = Convert.FromBase64String checkpoint.PendingBatchBase64
            let state = parseContinuationPending expected checkpoint bytes
            let facts = validateContinuationPending expected checkpoint existingReceiverFacts state
            let actualRefs: TelemetryCi.ProviderCheckpoint.FactReference list =
                facts |> List.map (fun fact -> ({ Kind = fact.Kind; Identity = fact.Identity; Revision = fact.Revision; Digest = fact.ContentDigest }: TelemetryCi.ProviderCheckpoint.FactReference)) |> List.sortBy _.Identity
            if actualRefs <> (checkpoint.FactReferences |> List.sortBy _.Identity) then invalidOp "ci-continuation-pending-current-reference-join"
            Ok(Some(Array.copy state.Payloads.[state.Phase].[state.Batch]), state.Phase, state.Batch)
        with error -> Error [ error.Message ]


    let private continuationExpectedEnvelope (scope: TelemetryReceipt.Scope) (bytes: byte array) =
        let payload = JsonNode.Parse(bytes).AsObject()
        let batchId = payload["ingestId"].GetValue<string>()
        // Prepared batches already carry native valid IDs. Refuse an unexpected adapter rewrite.
        if not (TelemetryReceipt.validId batchId) then invalidOp "ci-continuation-native-batch-id-required"
        let node = JsonObject()
        node["schema"] <- TelemetryReceipt.Schema
        node["workspaceId"] <- scope.Workspace
        node["producerId"] <- scope.Producer
        node["streamId"] <- scope.Stream
        node["batchId"] <- batchId
        node["payload"] <- payload
        TelemetryReceipt.parse (Encoding.UTF8.GetBytes(node.ToJsonString()))
        |> Result.defaultWith (String.concat ";" >> invalidOp)

    let private continuationReceiptMatches (expected: TelemetryReceipt.Envelope) (receiptJson: string) =
        if isNull receiptJson || Encoding.UTF8.GetByteCount receiptJson > 65536 then invalidOp "ci-continuation-receipt-bound"
        use doc = JsonDocument.Parse receiptJson
        let root = doc.RootElement
        let fields = root.EnumerateObject() |> Seq.map _.Name |> Seq.toList
        let closed = [ "schema"; "workspaceId"; "producerId"; "streamId"; "batchId"; "digest"; "status"; "code" ]
        if fields.Length <> closed.Length || Set.ofList fields <> Set.ofList closed
           || root.GetProperty("schema").GetString() <> "fsgg.telemetry.receipt/1" then invalidOp "ci-continuation-receipt-closed-shape"
        root.GetProperty("workspaceId").GetString() = expected.Scope.Workspace
        && root.GetProperty("producerId").GetString() = expected.Scope.Producer
        && root.GetProperty("streamId").GetString() = expected.Scope.Stream
        && root.GetProperty("batchId").GetString() = expected.BatchId
        && root.GetProperty("digest").GetString() = expected.Digest
        && root.GetProperty("status").GetString() = "applied"
        && root.GetProperty("code").ValueKind = JsonValueKind.Null

    let advanceContinuationPendingForTesting
        (expected: TelemetryCi.ProviderCheckpoint.Identity) now (scope: TelemetryReceipt.Scope)
        existingReceiverFacts checkpointBytes receiptJson =
        try
            if scope.Stream <> expected.Assignment.ProducerStream then invalidOp "ci-continuation-principal-stream-binding"
            let checkpoint = TelemetryCi.ProviderCheckpoint.parse expected now checkpointBytes |> Result.defaultWith (String.concat ";" >> invalidOp)
            if checkpoint.Phase <> TelemetryCi.ProviderCheckpoint.PublicationPending then invalidOp "ci-continuation-pending-checkpoint-required"
            let state = parseContinuationPending expected checkpoint (Convert.FromBase64String checkpoint.PendingBatchBase64)
            let facts = validateContinuationPending expected checkpoint existingReceiverFacts state
            let references: TelemetryCi.ProviderCheckpoint.FactReference list =
                facts |> List.map (fun fact -> ({ Kind = fact.Kind; Identity = fact.Identity; Revision = fact.Revision; Digest = fact.ContentDigest }: TelemetryCi.ProviderCheckpoint.FactReference)) |> List.sortBy _.Identity
            if references <> (checkpoint.FactReferences |> List.sortBy _.Identity) then invalidOp "ci-continuation-pending-current-reference-join"
            let payload = state.Payloads.[state.Phase].[state.Batch]
            let envelope = continuationExpectedEnvelope scope payload
            let currentExact =
                envelope.Batch.Facts |> List.forall (fun expectedFact ->
                    existingReceiverFacts |> List.filter (fun actual ->
                        actual.Identity = expectedFact.Identity && actual.Kind = expectedFact.Kind
                        && actual.ItemId = expectedFact.ItemId && actual.Revision = expectedFact.Revision
                        && actual.ContentDigest = expectedFact.ContentDigest) |> List.length = 1)
            // This is a pure testing seam. Production supplies independently authenticated receiver
            // snapshot facts and lookupReceipt output for the sealed current producer/principal scope.
            if not (continuationReceiptMatches envelope receiptJson) || not currentExact then
                Ok(Array.copy checkpointBytes, false)
            else
                let nextPhase, nextBatch =
                    if state.Batch + 1 < state.Payloads.[state.Phase].Length then state.Phase, state.Batch + 1
                    else state.Phase + 1, 0
                let next =
                    if nextPhase = 3 then
                        { checkpoint with
                            Phase = TelemetryCi.ProviderCheckpoint.Stable
                            PendingBatchDigest = ""
                            PendingBatchBase64 = ""
                            FactReferences = [] }
                    else
                        let bundle = encodeContinuationPending expected checkpoint { state with Phase = nextPhase; Batch = nextBatch }
                        { checkpoint with
                            PendingBatchDigest = TelemetryCi.ProviderCheckpoint.digest bundle
                            PendingBatchBase64 = Convert.ToBase64String bundle }
                TelemetryCi.ProviderCheckpoint.encode expected now next
                |> Result.map (fun bytes -> normalizePendingBytesForTesting bytes, nextPhase = 3)
        with error -> Error [ error.Message ]

    let receiverContinuationReadbackForTesting path assessment itemId (scope: TelemetryReceipt.Scope) =
        TelemetryStoreApplication.ciSuiteReceiverReadback path assessment itemId scope.Producer scope.Stream
        |> Result.map (fun snapshot -> snapshot.StoreId, snapshot.ReceiverContract, snapshot.RevisionFloor, snapshot.AuthenticatedFacts)


    let private acquireContinuation
        (transport: Transport.IDeadlineSinglePageGitHubTransport)
        (expected: TelemetryCi.ProviderCheckpoint.Identity) (clock: unit -> DateTimeOffset)
        admitted receiverRevisionFloor original (guard: unit -> unit) =
        try
            let timer = System.Diagnostics.Stopwatch.StartNew()
            let mutable requests = 0
            let metered =
                { new Transport.IDeadlineSinglePageGitHubTransport with
                    member _.SendSingleWithin(request, requestedRemaining) =
                        guard ()
                        let remaining = min requestedRemaining (TimeSpan.FromSeconds 30.0 - timer.Elapsed)
                        if request.Method <> "GET" || requests >= 40 || remaining <= TimeSpan.Zero then
                            Error(FS.GG.Coord.GitHub.Errors.Malformed(request.Subject, "provider-continuation-original-budget-exhausted"))
                        else
                            requests <- requests + 1
                            let response = transport.SendSingleWithin(request, remaining)
                            guard ()
                            response }
            let single =
                { new Transport.ISinglePageGitHubTransport with
                    member _.SendSingle request = metered.SendSingleWithin(request, TimeSpan.FromSeconds 30.0) }
            // Repository and profile discovery consume this same invocation's allowance.
            // The retained checkpoint never establishes either native witness.
            let response =
                single.SendSingle
                    { Method = "GET"; Path = "repos/" + expected.Repository; Query = []; Body = Transport.NoBody
                      Budget = Transport.Rest; IfNoneMatch = None; Subject = expected.Repository + " native repository identity" }
                |> Result.defaultWith (fun error -> invalidOp (string error))
            if response.Status <> 200 || response.NextLink.IsSome || response.Body.Length > 131072 then
                invalidOp "provider-native-repository-witness-required"
            use repository = JsonDocument.Parse response.Body
            let rec unique (value: JsonElement) =
                match value.ValueKind with
                | JsonValueKind.Object ->
                    let fields = value.EnumerateObject() |> Seq.toList
                    (fields |> List.map _.Name |> List.distinct).Length = fields.Length
                    && fields |> List.forall (fun field -> unique field.Value)
                | JsonValueKind.Array -> value.EnumerateArray() |> Seq.forall unique
                | _ -> true
            let repo = repository.RootElement
            if not (unique repo) || repo.GetProperty("full_name").GetString() <> expected.Repository then
                invalidOp "provider-native-repository-witness-required"
            let repositoryId = repo.GetProperty("id").GetInt64()
            if repositoryId <= 0L || (expected.RepositoryId > 0L && expected.RepositoryId <> repositoryId) then
                invalidOp "provider-native-repository-identity-drift"
            let profileBytes =
                CiReads.readAttributionProfile single expected.Repository expected.Head
                |> Result.defaultWith (fun error -> invalidOp (string error))
                |> Option.defaultWith (fun () -> invalidOp "provider-attribution-profile-unvalidated")
            parseRules profileBytes |> Result.defaultWith (String.concat ";" >> invalidOp) |> ignore
            let profileDigest = TelemetryCi.ProviderCheckpoint.digest profileBytes
            if expected.ProfileDigest <> "" && expected.ProfileDigest <> profileDigest then
                invalidOp "provider-attribution-profile-drift"
            let actual = { expected with RepositoryId = repositoryId; ProfileDigest = profileDigest }
            guard ()
            let result =
                CiReads.resumePopulation metered actual clock admitted receiverRevisionFloor original
                |> Result.defaultWith (fun error -> invalidOp (string error))
            guard ()
            Ok(actual, profileBytes, result, requests)
        with error -> Error [ error.Message ]

    let acquireContinuationForTesting transport expected clock admitted receiverRevisionFloor original =
        acquireContinuation transport expected clock admitted receiverRevisionFloor original ignore

    let private driveContinuationPending
        directory name path assessment (expected: TelemetryCi.ProviderCheckpoint.Identity)
        (clock: unit -> DateTimeOffset) (scope: TelemetryReceipt.Scope)
        (publishPayload: byte array -> Result<string, string list>)
        (drainPending: unit -> Result<unit, string list>) (hook: string -> unit) (guard: unit -> unit) =
        let require condition reason = if not condition then invalidOp reason
        require (scope.Stream = expected.Assignment.ProducerStream) "ci-continuation-principal-stream-binding"
        let readback () =
            guard ()
            let snapshot = TelemetryStoreApplication.ciSuiteReceiverReadback path assessment expected.Assignment.ItemId scope.Producer scope.Stream
                           |> Result.defaultWith (String.concat ";" >> invalidOp)
            require (snapshot.StoreId = expected.StoreId && snapshot.ReceiverContract = "fsgg.telemetry.ci-suite-receiver/1"
                     && snapshot.ItemId = expected.Assignment.ItemId && snapshot.Producer = scope.Producer && snapshot.Stream = scope.Stream) "ci-continuation-current-receiver-binding"
            guard ()
            snapshot.AuthenticatedFacts
        let lookup (envelope: TelemetryReceipt.Envelope) =
            guard ()
            let receipt = TelemetryStoreApplication.lookupReceipt path assessment scope envelope.BatchId
            guard ()
            receipt
        let mutable bytes = readContinuationIntent directory name (96 * 1024 * 1024) guard
        let mutable result = "pending"
        let mutable stopped = false
        let mutable completedBatches = 0
        while not stopped do
            require (completedBatches < 4096) "ci-continuation-publication-batch-bound"
            let facts = readback ()
            let payload, _, _ = recoverContinuationPendingForTesting expected (clock ()) facts bytes
                                |> Result.defaultWith (String.concat ";" >> invalidOp)
            let payload = payload |> Option.defaultWith (fun () -> invalidOp "ci-continuation-pending-payload-required")
            let envelope = continuationExpectedEnvelope scope payload
            let initial = lookup envelope
            let mutable receipt = initial
            match initial with
            | Error [ "receipt-unavailable" ] ->
                guard ()
                publishPayload payload |> Result.defaultWith (String.concat ";" >> invalidOp) |> ignore
                guard ()
                hook "after-publication"
                drainPending () |> Result.defaultWith (String.concat ";" >> invalidOp)
                guard ()
                hook "after-drain"
                receipt <- lookup envelope
            | Ok json ->
                use doc = JsonDocument.Parse json
                if doc.RootElement.GetProperty("status").GetString() = "durably-received" then
                    // Existing durable custody is consumed once; do not resubmit the envelope.
                    drainPending () |> Result.defaultWith (String.concat ";" >> invalidOp)
                    guard ()
                    hook "after-drain"
                    receipt <- lookup envelope
            | Error errors -> invalidOp (String.concat ";" errors)
            match receipt with
            | Error [ "receipt-unavailable" ] -> stopped <- true
            | Error errors -> invalidOp (String.concat ";" errors)
            | Ok json ->
                let current = readback ()
                let next, promoted = advanceContinuationPendingForTesting expected (clock ()) scope current bytes json
                                     |> Result.defaultWith (String.concat ";" >> invalidOp)
                hook "after-readback"
                if next = bytes then stopped <- true
                else
                    guard ()
                    hook "before-checkpoint-write"
                    bytes <- persistContinuationIntent directory name (96 * 1024 * 1024) guard next
                    guard ()
                    hook "after-checkpoint-write"
                    completedBatches <- completedBatches + 1
                    if promoted then result <- "complete"; stopped <- true
        bytes, result

    let publishContinuationPendingForTesting
        directory name path assessment (expected: TelemetryCi.ProviderCheckpoint.Identity)
        (clock: unit -> DateTimeOffset) (scope: TelemetryReceipt.Scope) checkpointBytes publishPayload drainPending hook =
        try
            withContinuationLock directory (fun guard ->
                let snapshot = TelemetryStoreApplication.ciSuiteReceiverReadback path assessment expected.Assignment.ItemId scope.Producer scope.Stream
                               |> Result.defaultWith (String.concat ";" >> invalidOp)
                if snapshot.StoreId <> expected.StoreId || snapshot.ReceiverContract <> "fsgg.telemetry.ci-suite-receiver/1"
                   || scope.Stream <> expected.Assignment.ProducerStream then invalidOp "ci-continuation-current-receiver-binding"
                guard ()
                let file = FileInfo(Path.Combine(directory, name + ".json"))
                if not (isNull file.LinkTarget) then invalidOp "ci-continuation-private-intent-symlink"
                if not file.Exists then
                    recoverContinuationPendingForTesting expected (clock ()) snapshot.AuthenticatedFacts checkpointBytes
                    |> Result.defaultWith (String.concat ";" >> invalidOp) |> ignore
                    persistContinuationIntent directory name (96 * 1024 * 1024) guard checkpointBytes |> ignore
                    hook "initial-intent-stored"
                // The file supplies only retained bytes. A stable cache is not completion/freshness;
                // the production caller must perform native provider revalidation before new work.
                driveContinuationPending directory name path assessment expected clock scope publishPayload drainPending hook guard |> Ok)
        with error -> Error [ error.Message ]

    let private continueCollection
        directory name path assessment (expected: TelemetryCi.ProviderCheckpoint.Identity)
        clock (scope: TelemetryReceipt.Scope) transport publishPayload drainPending hook (beforeAcquisition: unit -> unit) (guard: unit -> unit) =
        let require condition reason = if not condition then invalidOp reason
        require (scope.Stream = expected.Assignment.ProducerStream) "ci-continuation-principal-stream-binding"
        let readback () =
            guard ()
            let snapshot = TelemetryStoreApplication.ciSuiteReceiverReadback path assessment expected.Assignment.ItemId scope.Producer scope.Stream
                           |> Result.defaultWith (String.concat ";" >> invalidOp)
            require (snapshot.StoreId = expected.StoreId && snapshot.ReceiverContract = "fsgg.telemetry.ci-suite-receiver/1") "ci-continuation-current-receiver-binding"
            guard ()
            snapshot
        let mutable actual = expected
        let mutable original = None
        let file = FileInfo(Path.Combine(directory, name + ".json"))
        require (isNull file.LinkTarget) "ci-continuation-private-intent-symlink"
        if file.Exists then
            let bytes = readContinuationIntent directory name (96 * 1024 * 1024) guard
            // Only these retained witness values are read before the complete strict parser.
            // All authority-bearing current bindings come from the caller/receiver. Acquisition
            // rechecks repository/profile natively; publication still requires exact current facts.
            use document = JsonDocument.Parse bytes
            let identity = document.RootElement.GetProperty("identity")
            let retainedRepositoryId = identity.GetProperty("repositoryId").GetInt64()
            let retainedProfileDigest = identity.GetProperty("profileDigest").GetString()
            require ((expected.RepositoryId = 0L || expected.RepositoryId = retainedRepositoryId)
                     && (expected.ProfileDigest = "" || expected.ProfileDigest = retainedProfileDigest)) "ci-continuation-retained-witness-binding"
            actual <- { expected with RepositoryId = retainedRepositoryId; ProfileDigest = retainedProfileDigest }
            let checkpoint = TelemetryCi.ProviderCheckpoint.parse actual (clock ()) bytes
                             |> Result.defaultWith (String.concat ";" >> invalidOp)
            original <- Some checkpoint
        let mutable publicationPending = false
        match original with
        | Some checkpoint when checkpoint.Phase = TelemetryCi.ProviderCheckpoint.PublicationPending ->
            let bytes, status = driveContinuationPending directory name path assessment actual clock scope publishPayload drainPending hook guard
            publicationPending <- status <> "complete"
            original <- Some(TelemetryCi.ProviderCheckpoint.parse actual (clock ()) bytes |> Result.defaultWith (String.concat ";" >> invalidOp))
        | _ -> ()
        if publicationPending then "pending", 0, original.Value
        else
            guard ()
            beforeAcquisition ()
            guard ()
            let before = readback ()
            let admissions =
                before.AuthenticatedFacts |> List.filter (fun fact ->
                    match fact.Payload with
                    | TelemetryStore.CiPopulationAdmission(collection, repository, pr, baseRef, baseSha, head, witness) ->
                        collection = actual.Collection && repository = actual.Repository && pr = actual.PullRequest
                        && baseRef = actual.BaseRef && baseSha = actual.BaseSha && head = actual.Head && witness = "native-pr-head"
                    | _ -> false)
            require (admissions.Length <= 1) "ci-continuation-exact-authenticated-admission-required"
            let admitted = not admissions.IsEmpty
            let identity, profileBytes, result, requests =
                acquireContinuation transport actual clock admitted before.RevisionFloor original guard
                |> Result.defaultWith (String.concat ";" >> invalidOp)
            actual <- identity
            let checkpointBytes = TelemetryCi.ProviderCheckpoint.encode actual (clock ()) result.Checkpoint
                                  |> Result.defaultWith (String.concat ";" >> invalidOp)
            persistContinuationIntent directory name (96 * 1024 * 1024) guard checkpointBytes |> ignore
            match result.Population with
            | None -> "pending", requests, result.Checkpoint
            | Some population ->
                let current = readback ()
                let phases = projectContinuation actual (clock ()) admitted current.RevisionFloor (Some profileBytes) current.AuthenticatedFacts result.Checkpoint population
                             |> Result.defaultWith (String.concat ";" >> invalidOp)
                let pending = prepareContinuationPendingForTesting actual (clock ()) current.AuthenticatedFacts result.Checkpoint phases
                              |> Result.defaultWith (String.concat ";" >> invalidOp)
                persistContinuationIntent directory name (96 * 1024 * 1024) guard pending |> ignore
                hook "initial-intent-stored"
                let bytes, status = driveContinuationPending directory name path assessment actual clock scope publishPayload drainPending hook guard
                status, requests, TelemetryCi.ProviderCheckpoint.parse actual (clock ()) bytes |> Result.defaultWith (String.concat ";" >> invalidOp)

    let continueCollectionForTesting directory name path assessment expected clock scope transport publishPayload drainPending hook =
        try
            withContinuationLock directory (fun guard ->
                continueCollection directory name path assessment expected clock scope transport publishPayload drainPending hook ignore guard |> Ok)
        with error -> Error [ error.Message ]

    let private projectOutcome (assignment: TelemetryCi.Assignment) (delivery: DeliveryBinding) =
        let candidate =
            String.concat
                "\u001f"
                [
                    assignment.ItemId
                    delivery.Repository
                    string delivery.PullRequest
                    delivery.BaseRef
                    delivery.BaseSha
                    delivery.Head
                ]

        let nativeIdentity = identity "native-item-outcome-" [ candidate ]
        let event = common "native-item-outcome" nativeIdentity assignment.ItemId
        event["revision"] <- DateTimeOffset.Parse(delivery.ObservedAt).UtcTicks
        event["repository"] <- delivery.Repository
        event["prNumber"] <- delivery.PullRequest
        event["baseRef"] <- delivery.BaseRef
        event["baseSha"] <- delivery.BaseSha
        event["head"] <- delivery.Head
        event["outcome"] <- delivery.Outcome
        event["codeDelivery"] <- delivery.CodeDelivery
        addOptional event "mergeCommit" delivery.MergeCommit
        addOptional event "occurredAt" delivery.OutcomeAt
        event["observedAt"] <- delivery.ObservedAt
        event["sourceKind"] <- "routine-delivery"
        event["sourceRef"] <- "routine-delivery:" + CanonicalJson.sha256 (Encoding.UTF8.GetBytes candidate)

        let collection =
            CanonicalJson.sha256 (Encoding.UTF8.GetBytes(candidate + "\u001foutcome"))

        collection, event

    type private Target<'binding> =
        | Legacy of root: string * assessment: TelemetryStore.DurabilityAssessment
        | Workspace of binding: 'binding * localRoot: string option

    // Only a validated legacy host default may coexist with explicit legacy storage.
    // Explicit selections still belong to the workspace resolver and never fall back.
    let private implicitWorkspaceSelected () =
        try
            let path = WorkspaceTelemetryApplication.configuredPath None
            let info = FileInfo path
            if not (isNull info.LinkTarget) || Directory.Exists path then
                Error [ "configuration-unsafe" ]
            elif not info.Exists then
                Ok false
            elif
                info.Length > 65536L
                || (not (OperatingSystem.IsWindows())
                    && File.GetUnixFileMode(path) <> (UnixFileMode.UserRead ||| UnixFileMode.UserWrite))
            then
                Error [ "configuration-unsafe" ]
            else
                use stream = File.OpenRead path
                let bytes = Array.zeroCreate<byte> 65537
                let count = stream.ReadAtLeast(bytes.AsSpan(), 65537, false)
                if count > 65536 then
                    Error [ "configuration-unsafe" ]
                else
                    use document = JsonDocument.Parse(ReadOnlyMemory<byte>(bytes, 0, count))
                    let rec unique (value: JsonElement) =
                        match value.ValueKind with
                        | JsonValueKind.Object ->
                            let fields = value.EnumerateObject() |> Seq.toList
                            (fields |> List.map _.Name |> List.distinct).Length = fields.Length
                            && fields |> List.forall (fun field -> unique field.Value)
                        | JsonValueKind.Array -> value.EnumerateArray() |> Seq.forall unique
                        | _ -> true

                    let root = document.RootElement
                    if not (unique root) then
                        Error [ "configuration-duplicate-property" ]
                    else
                        match root.GetProperty("schema").GetString() with
                        | "fsgg.telemetry.workspace-config/1" -> Ok true
                        | "fsgg.telemetry.host-config/1" ->
                            let fields = root.EnumerateObject() |> Seq.map _.Name |> Set.ofSeq
                            if fields <> Set [ "schema"; "storeRoot"; "engine" ] then
                                Error [ "configuration-shape" ]
                            else
                                let storeRoot = root.GetProperty("storeRoot").GetString()
                                let engine = root.GetProperty("engine").GetString()
                                if
                                    not (String.IsNullOrEmpty storeRoot)
                                    && Path.IsPathFullyQualified storeRoot
                                    && not (String.IsNullOrEmpty engine)
                                    && not (engine.Contains('/') || engine.Contains('\\'))
                                then
                                    Ok false
                                else
                                    Error [ "configuration-shape" ]
                        | _ -> Error [ "configuration-version" ]
        with _ ->
            Error [ "configuration-unreadable" ]

    let private target
        (assess: string -> TelemetryStore.DurabilityAssessment)
        (resolve: string option -> string option -> Result<'binding, string list>)
        (localStoreRoot: 'binding -> Result<string option, string list>)
        args
        =
        let config, repository = option "--config" args, option "--repository" args

        let environmentConfigSelected =
            Environment.GetEnvironmentVariable("FSGG_TELEMETRY_CONFIG")
            |> Option.ofObj
            |> Option.exists (fun value -> value <> "")

        let workspaceSelected =
            if List.contains "--config" args && config.IsNone then
                Error [ "configuration-path-required" ]
            elif config.IsSome || environmentConfigSelected then
                Ok true
            else
                implicitWorkspaceSelected ()

        match option "--store-root" args, workspaceSelected with
        | _, Error errors -> Error errors
        | Some _, Ok true -> Error [ "workspace config and legacy store root cannot both be selected" ]
        | Some root, Ok false -> Ok(Legacy(root, assess root))
        | None, Ok true ->
            resolve config repository
            |> Result.bind (fun binding -> localStoreRoot binding |> Result.map (fun root -> Workspace(binding, root)))
        | None, Ok false ->
            match
                Environment.GetEnvironmentVariable("FSGG_TELEMETRY_STORE")
                |> Option.ofObj
                |> Option.filter (String.IsNullOrWhiteSpace >> not)
            with
            | Some root -> Ok(Legacy(root, assess root))
            | None ->
                resolve config repository
                |> Result.bind (fun binding ->
                    localStoreRoot binding |> Result.map (fun root -> Workspace(binding, root)))

    let private publish (publishWorkspace: 'binding -> byte array -> Result<string, string list>) target bytes =
        match target with
        | Legacy(root, assessment) ->
            TelemetryStoreApplication.publish root assessment bytes
            |> Result.map (fun _ -> "queued")
        | Workspace(binding, _) ->
            match publishWorkspace binding bytes with
            | Ok status -> Ok status
            | Error [ "unacknowledged-lossy" ] -> Ok "queued"
            | Error errors -> Error errors

    let private drain (drainWorkspace: 'binding -> Result<string, string list>) target =
        match target with
        | Legacy(root, assessment) -> TelemetryStoreApplication.drain root assessment |> Result.map ignore
        | Workspace(binding, _) -> drainWorkspace binding |> Result.map ignore

    let private admission target (assignment: TelemetryCi.Assignment) (delivery: DeliveryBinding) =
        match target with
        | Legacy(root, assessment) ->
            TelemetryStoreApplication.ciPopulationAdmissionExists
                root
                assessment
                assignment.ItemId
                delivery.Repository
                delivery.PullRequest
                delivery.BaseRef
                delivery.BaseSha
                delivery.Head
            |> Result.map Some
        | Workspace(_, Some root) ->
            let assessment = TelemetryStoreApplication.assessProductionRoot root

            TelemetryStoreApplication.ciPopulationAdmissionExists
                root
                assessment
                assignment.ItemId
                delivery.Repository
                delivery.PullRequest
                delivery.BaseRef
                delivery.BaseSha
                delivery.Head
            |> Result.map Some
        | Workspace(_, None) -> Ok None

    let private health target (assignment: TelemetryCi.Assignment) =
        match target with
        | Legacy(root, assessment) ->
            TelemetryStoreApplication.budgetHealth root assessment assignment.ItemId
            |> Result.bind (fun json ->
                try
                    use document = JsonDocument.Parse json in
                    Ok(document.RootElement.GetProperty("status").GetString())
                with _ ->
                    Error [ "health-unavailable" ])
        | Workspace(_, Some root) ->
            TelemetryStoreApplication.budgetHealth
                root
                (TelemetryStoreApplication.assessProductionRoot root)
                assignment.ItemId
            |> Result.bind (fun json ->
                try
                    use document = JsonDocument.Parse json in
                    Ok(document.RootElement.GetProperty("status").GetString())
                with _ ->
                    Error [ "health-unavailable" ])
        | Workspace(_, None) -> Ok "pending"

    let private remainsQueued target drainResult =
        match target with
        | Workspace(_, None) -> true
        | _ -> Result.isError drainResult

    let private correctionPrivatePath (path: string) =
        if not (Path.IsPathFullyQualified path) then invalidOp "correction path must be absolute"
        let full = Path.GetFullPath path
        let mutable parent = DirectoryInfo(Path.GetDirectoryName full)
        while not (isNull parent) do
            if not parent.Exists || not (isNull parent.LinkTarget) then
                invalidOp "correction path requires existing non-symlink parents"
            parent <- parent.Parent
        full

    let private readCorrectionFile path =
        try
            let full = correctionPrivatePath path
            let file = FileInfo full
            if not file.Exists || not (isNull file.LinkTarget) then invalidOp "correction input must be a regular non-symlink file"
            if file.Length <= 0L then invalidOp "correction input must contain JSON bytes"
            if file.Length > 1048576L then invalidOp "correction input exceeds 1 MiB"
            if not (OperatingSystem.IsWindows()) && File.GetUnixFileMode(full) <> (UnixFileMode.UserRead ||| UnixFileMode.UserWrite) then
                invalidOp "correction input permissions must be 0600"
            use stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read)
            if stream.Length > 1048576L then invalidOp "correction input exceeds 1 MiB"
            let bytes = Array.zeroCreate<byte> (int stream.Length)
            stream.ReadExactly bytes
            Ok bytes
        with error -> Error [ error.Message ]

    let private writeCorrectionFile path (json: string) =
        try
            let full = correctionPrivatePath path
            let bytes = Encoding.UTF8.GetBytes json
            if bytes.Length > 1048576 then invalidOp "correction plan exceeds 1 MiB"
            let options = FileStreamOptions(Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, Options = FileOptions.WriteThrough)
            if not (OperatingSystem.IsWindows()) then options.UnixCreateMode <- Nullable(UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
            use stream = new FileStream(full, options)
            stream.Write bytes
            stream.Flush true
            Ok "{\"schema\":\"fsgg.telemetry.ci-correction-planned/1\",\"status\":\"planned\"}\n"
        with error -> Error [ error.Message ]

    let private runCorrection assess resolve localStoreRoot action args =
        let selected =
            target assess resolve localStoreRoot args
            |> Result.bind (function
                | Legacy(root, assessment) -> Ok(root, assessment)
                | Workspace(_, Some root) when option "--repository" args |> Option.isSome -> Ok(root, assess root)
                | Workspace(_, Some _) -> Error [ "correction workspace selection requires --repository" ]
                | Workspace(_, None) -> Error [ "ci-attribution-correction-remote-unsupported" ])
        let result =
            selected |> Result.bind (fun (root, assessment) ->
                match action with
                | "correction-plan" ->
                    match option "--input" args, option "--output" args with
                    | Some input, Some output ->
                        readCorrectionFile input
                        |> Result.bind TelemetryCi.parseCorrectionRequest
                        |> Result.bind (fun request ->
                            if option "--repository" args |> Option.exists ((<>) request.Repository) then
                                Error [ "ci-correction-repository-selection-conflict" ]
                            else TelemetryStoreApplication.ciCorrectionPlan root assessment request)
                        |> Result.bind (writeCorrectionFile output)
                    | _ -> Error [ "correction-plan requires --input and --output" ]
                | "correct" ->
                    match option "--plan" args with
                    | None -> Error [ "correct requires --plan" ]
                    | Some path ->
                        readCorrectionFile path |> Result.bind (fun bytes ->
                            TelemetryCi.parseCorrectionPlan bytes |> Result.bind (fun plan ->
                                if option "--repository" args |> Option.exists ((<>) plan.Request.Repository) then
                                    Error [ "ci-correction-repository-selection-conflict" ]
                                else TelemetryStoreApplication.ciCorrect root assessment bytes))
                | _ ->
                    match option "--correction-id" args with
                    | None -> Error [ "correction-history requires --correction-id" ]
                    | Some identity -> TelemetryStoreApplication.ciCorrectionHistory root assessment identity)
        match result with
        | Ok json -> Console.Out.Write json; 0
        | Error errors -> fail errors

    let private runUsing assess resolve publishWorkspace drainWorkspace localStoreRoot action args =
        match action with
        | "correction-plan" | "correct" | "correction-history" ->
            runCorrection assess resolve localStoreRoot action args
        | "summary" ->
            match legacyRoot args, option "--item" args with
            | Some path, Some item ->
                TelemetryStoreApplication.ciSummary path (assess path) item
                |> function
                    | Ok json ->
                        Console.Out.Write json
                        0
                    | Error errors -> fail errors
            | None, _ -> fail [ "store root is unconfigured" ]
            | _, None -> fail [ "--item is required" ]
        | "collect" ->
            match
                option "--assignment" args,
                option "--repo" args,
                option "--pr" args,
                option "--head" args,
                option "--workflow" args,
                target assess resolve localStoreRoot args
            with
            | Some assignmentPath, Some repository, Some prText, Some head, Some workflow, Ok destination ->
                match repository.Split('/') with
                | [| owner; repo |] when
                    Regex.IsMatch(repository, "^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")
                    && Regex.IsMatch(head, "^[0-9a-f]{40}$")
                    ->
                    match
                        Int32.TryParse prText,
                        readAssignment assignmentPath,
                        readRules repository head
                    with
                    | (true, pr), Ok assignment, Ok rules when pr > 0 ->
                        let token =
                            Environment.GetEnvironmentVariable("GITHUB_TOKEN")
                            |> Option.ofObj
                            |> Option.orElseWith (fun () ->
                                Environment.GetEnvironmentVariable("GH_TOKEN") |> Option.ofObj)

                        match token with
                        | None -> fail [ "GITHUB_TOKEN or GH_TOKEN is required" ]
                        | Some token ->
                            use transport = new Transport.HttpTransport(Transport.apiBaseFromEnv (), token)

                            match
                                CiReads.collect
                                    (transport :> Transport.ISinglePageGitHubTransport)
                                    (Transport.apiBaseFromEnv ())
                                    owner
                                    repo
                                    pr
                                    head
                                    workflow
                            with
                            | Error error -> fail [ string error ]
                            | Ok snapshot ->
                                let collection, events = project assignment rules snapshot

                                match boundedBatches assignment collection events with
                                | Error errors -> fail errors
                                | Ok batches ->
                                    let results = batches |> List.map (publish publishWorkspace destination)

                                    match
                                        results
                                        |> List.tryPick (function
                                            | Error errors -> Some errors
                                            | _ -> None)
                                    with
                                    | Some errors -> fail errors
                                    | None ->
                                        Console.Out.WriteLine(
                                            JsonSerializer.Serialize
                                                {|
                                                    schema = "fsgg.telemetry.ci-collect/1"
                                                    status = "queued"
                                                    batches = results.Length
                                                    observations = events.Length
                                                    coverage = snapshot.InventoryCoverage
                                                |}
                                        )

                                        0
                    | (false, _), _, _ -> fail [ "--pr must be a positive integer" ]
                    | _, Error errors, _
                    | _, _, Error errors -> fail errors
                    | _ -> fail [ "--pr must be a positive integer" ]
                | _ ->
                    fail
                        [
                            "--repo must be owner/name and --head must be 40 lowercase hexadecimal characters"
                        ]
            | _, _, _, _, _, Error errors -> fail errors
            | _ ->
                fail
                    [
                        "collect requires --assignment, --repo, --pr, --head, --workflow, and an explicitly selected telemetry destination"
                    ]
        | "reconcile" ->
            match option "--assignment" args, option "--delivery" args, target assess resolve localStoreRoot args with
            | Some assignmentPath, Some deliveryPath, Ok destination ->
                match readAssignment assignmentPath, readDelivery deliveryPath with
                | Ok assignment, Ok delivery ->
                    let values = delivery.Repository.Split('/')
                    let outcomeCollection, outcomeEvent = projectOutcome assignment delivery

                    let outcomeResult =
                        boundedBatches assignment outcomeCollection [ outcomeEvent ]
                        |> Result.bind (fun batches ->
                            batches
                            |> List.map (publish publishWorkspace destination)
                            |> List.tryPick (function
                                | Error errors -> Some(Error errors)
                                | _ -> None)
                            |> Option.defaultValue (Ok())
                            |> Result.bind (fun () -> drain drainWorkspace destination))

                    match
                        outcomeResult
                        |> Result.bind (fun () -> admission destination assignment delivery)
                    with
                    | Error errors -> fail errors
                    | Ok None when delivery.CodeDelivery = "delivered" ->
                        Console.Out.WriteLine(
                            JsonSerializer.Serialize
                                {|
                                    schema = "fsgg.telemetry.ci-reconciliation/1"
                                    status = "pending"
                                    driverHealth = "pending"
                                    binding = "remote-admission-unavailable"
                                    queued = true
                                    population = "unknown"
                                    pending = [| "remote-admission-query-unavailable" |]
                                    unsupportedSources = [| "merge_group"; "base"; "push" |]
                                |}
                        )

                        0
                    | Ok admittedOption when admittedOption = Some false || admittedOption = None ->
                        let admitted = false

                        if
                            not (
                                canCreateAdmission
                                    delivery.Outcome
                                    delivery.CodeDelivery
                                    delivery.ObservedHead
                                    delivery.Head
                            )
                        then
                            fail
                                [
                                    "first CI population admission requires ready, not-delivered, and an explicit matching observed head"
                                ]
                        else
                            match
                                readRules delivery.Repository delivery.Head
                            with
                            | Error errors -> fail errors
                            | Ok rules ->
                                let token =
                                    Environment.GetEnvironmentVariable("GITHUB_TOKEN")
                                    |> Option.ofObj
                                    |> Option.orElseWith (fun () ->
                                        Environment.GetEnvironmentVariable("GH_TOKEN") |> Option.ofObj)

                                match token with
                                | None -> fail [ "GITHUB_TOKEN or GH_TOKEN is required" ]
                                | Some token ->
                                    use transport = new Transport.HttpTransport(Transport.apiBaseFromEnv (), token)

                                    match
                                        CiReads.discoverPopulation
                                            (transport :> Transport.ISinglePageGitHubTransport)
                                            (Transport.apiBaseFromEnv ())
                                            values[0]
                                            values[1]
                                            delivery.PullRequest
                                            delivery.Head
                                            delivery.BaseRef
                                            delivery.BaseSha
                                            admitted
                                    with
                                    | Error error -> fail [ string error ]
                                    | Ok population when not population.AdmissionWitness && not admitted ->
                                        Console.Out.WriteLine(
                                            JsonSerializer.Serialize
                                                {|
                                                    schema = "fsgg.telemetry.ci-reconciliation/1"
                                                    status = "incomplete"
                                                    binding = "unadmitted-head"
                                                    queued = false
                                                    population = "unknown"
                                                    pending = population.Pending |> List.toArray
                                                    unsupportedSources =
                                                        [| "merge_group"; "base"; "push" |]
                                                |}
                                        )

                                        0
                                    | Ok population ->
                                        let collection, events = projectPopulation assignment rules population

                                        match boundedBatches assignment collection events with
                                        | Error errors -> fail errors
                                        | Ok batches ->
                                            let results = batches |> List.map (publish publishWorkspace destination)

                                            match
                                                results
                                                |> List.tryPick (function
                                                    | Error errors -> Some errors
                                                    | _ -> None)
                                            with
                                            | Some errors -> fail errors
                                            | None ->
                                                let drainResult = drain drainWorkspace destination

                                                let health =
                                                    match
                                                        drainResult
                                                        |> Result.bind (fun () -> health destination assignment)
                                                    with
                                                    | Ok value -> value
                                                    | Error _ -> "pending"

                                                let status =
                                                    if
                                                        population.Pending.IsEmpty
                                                        && population.Gaps.IsEmpty
                                                        && population.Snapshot.InventoryCoverage = "complete"
                                                        && population.CheckCoverage = "complete"
                                                    then
                                                        "complete"
                                                    else
                                                        "partial"

                                                let workflows =
                                                    population.Snapshot.Runs
                                                    |> List.map _.Workflow
                                                    |> List.distinct
                                                    |> List.sort
                                                    |> List.toArray

                                                Console.Out.WriteLine(
                                                    JsonSerializer.Serialize
                                                        {|
                                                            schema = "fsgg.telemetry.ci-reconciliation/1"
                                                            status = status
                                                            driverHealth = health
                                                            binding = population.Snapshot.Binding
                                                            queued = remainsQueued destination drainResult
                                                            batches = batches.Length
                                                            observations = events.Length
                                                            workflows = workflows
                                                            attempts = population.Snapshot.Runs.Length
                                                            checks = population.Checks.Length
                                                            externalChecks = population.ExternalChecks
                                                            pending = population.Pending |> List.toArray
                                                            gaps = population.Gaps |> List.toArray
                                                            unsupportedSources =
                                                                [|
                                                                    "merge_group"
                                                                    "base"
                                                                    "push"
                                                                |]
                                                        |}
                                                )

                                                0
                    | Ok(Some true) ->
                        match
                            readRules delivery.Repository delivery.Head
                        with
                        | Error errors -> fail errors
                        | Ok rules ->
                            let token =
                                Environment.GetEnvironmentVariable("GITHUB_TOKEN")
                                |> Option.ofObj
                                |> Option.orElseWith (fun () ->
                                    Environment.GetEnvironmentVariable("GH_TOKEN") |> Option.ofObj)

                            match token with
                            | None -> fail [ "GITHUB_TOKEN or GH_TOKEN is required" ]
                            | Some token ->
                                use transport = new Transport.HttpTransport(Transport.apiBaseFromEnv (), token)

                                match
                                    CiReads.discoverPopulation
                                        (transport :> Transport.ISinglePageGitHubTransport)
                                        (Transport.apiBaseFromEnv ())
                                        values[0]
                                        values[1]
                                        delivery.PullRequest
                                        delivery.Head
                                        delivery.BaseRef
                                        delivery.BaseSha
                                        true
                                with
                                | Error error -> fail [ string error ]
                                | Ok population ->
                                    let collection, events = projectPopulation assignment rules population

                                    match boundedBatches assignment collection events with
                                    | Error errors -> fail errors
                                    | Ok batches ->
                                        let results = batches |> List.map (publish publishWorkspace destination)

                                        match
                                            results
                                            |> List.tryPick (function
                                                | Error errors -> Some errors
                                                | _ -> None)
                                        with
                                        | Some errors -> fail errors
                                        | None ->
                                            let drainResult = drain drainWorkspace destination

                                            let driverHealth =
                                                match
                                                    drainResult |> Result.bind (fun () -> health destination assignment)
                                                with
                                                | Ok value -> value
                                                | Error _ -> "pending"

                                            let status =
                                                if
                                                    population.Pending.IsEmpty
                                                    && population.Gaps.IsEmpty
                                                    && population.Snapshot.InventoryCoverage = "complete"
                                                    && population.CheckCoverage = "complete"
                                                then
                                                    "complete"
                                                else
                                                    "partial"

                                            Console.Out.WriteLine(
                                                JsonSerializer.Serialize
                                                    {|
                                                        schema = "fsgg.telemetry.ci-reconciliation/1"
                                                        status = status
                                                        driverHealth = driverHealth
                                                        binding = population.Snapshot.Binding
                                                        queued = remainsQueued destination drainResult
                                                        batches = batches.Length
                                                        observations = events.Length
                                                        workflows =
                                                            population.Snapshot.Runs
                                                            |> List.map _.Workflow
                                                            |> List.distinct
                                                            |> List.sort
                                                            |> List.toArray
                                                        attempts = population.Snapshot.Runs.Length
                                                        checks = population.Checks.Length
                                                        externalChecks = population.ExternalChecks
                                                        pending = population.Pending |> List.toArray
                                                        gaps = population.Gaps |> List.toArray
                                                        unsupportedSources =
                                                            [| "merge_group"; "base"; "push" |]
                                                    |}
                                            )

                                            0
                    | Ok _ -> fail [ "CI population admission state is invalid" ]
                | Error errors, _
                | _, Error errors -> fail errors
            | _, _, Error errors -> fail errors
            | _ ->
                fail
                    [
                        "reconcile requires --assignment, --delivery, and an explicitly selected telemetry destination"
                    ]
        | _ -> fail [ "action must be collect, reconcile, summary, correction-plan, correct, or correction-history" ]

    let private runConfiguredContinuationUsing assess (clock: unit -> DateTimeOffset) (transportOverride: Transport.IDeadlineSinglePageGitHubTransport option) (testingAssessment: TelemetryStore.DurabilityAssessment option) args =
        try
            let require condition reason = if not condition then invalidOp reason
            let config, repository = option "--config" args, option "--repository" args
            let assignmentPath = option "--assignment" args |> Option.defaultWith (fun () -> invalidOp "--assignment is required")
            let deliveryPath = option "--delivery" args |> Option.defaultWith (fun () -> invalidOp "--delivery is required")
            let binding = WorkspaceTelemetryApplication.resolveBinding config repository |> Result.defaultWith (String.concat ";" >> invalidOp)
            let configPath, boundRepository, bindingDigest =
                WorkspaceTelemetryApplication.tryBinding config repository
                |> Option.defaultWith (fun () -> invalidOp "ci-continuation-current-configuration-binding")
            let root, principal, associationDigest =
                WorkspaceTelemetryApplication.resolveEfficiencyProducer config repository
                |> Result.defaultWith (fun errors ->
                    if errors = [ "local-destination-required" ] then invalidOp "ci-continuation-remote-receiver-unsupported"
                    else invalidOp (String.concat ";" errors))
            let scope = principal.Scope
            let assessment = assess root
            withContinuationLock root (fun physicalGuard ->
                let assignmentAnchors = continuationReadAncestry (Path.GetDirectoryName assignmentPath)
                let deliveryAnchors = continuationReadAncestry (Path.GetDirectoryName deliveryPath)
                let inputGuard () =
                    physicalGuard ()
                    require (continuationReadAncestry (Path.GetDirectoryName assignmentPath) = assignmentAnchors
                             && continuationReadAncestry (Path.GetDirectoryName deliveryPath) = deliveryAnchors) "ci-continuation-input-ancestry-drift"
                let assignmentBytes = readContinuationInput assignmentPath 8192 false inputGuard
                let deliveryBytes = readContinuationInput deliveryPath 16384 true inputGuard
                let assignment = TelemetryCi.parseAssignment assignmentBytes |> Result.defaultWith (String.concat ";" >> invalidOp)
                let delivery = parseDeliveryBytes deliveryBytes |> Result.defaultWith (String.concat ";" >> invalidOp)
                require (boundRepository = delivery.Repository) "ci-continuation-current-repository-binding"
                require (scope.Stream = assignment.ProducerStream) "ci-continuation-principal-stream-binding"
                let guard () =
                    inputGuard ()
                    require (readContinuationInput assignmentPath 8192 false inputGuard = assignmentBytes
                             && readContinuationInput deliveryPath 16384 true inputGuard = deliveryBytes) "ci-continuation-current-input-drift"
                    let currentRoot, currentPrincipal, currentDigest =
                        WorkspaceTelemetryApplication.resolveEfficiencyProducer (Some configPath) (Some boundRepository)
                        |> Result.defaultWith (String.concat ";" >> invalidOp)
                    require (currentRoot = root && currentPrincipal = principal && currentDigest = associationDigest
                             && currentDigest = bindingDigest) "ci-continuation-current-configuration-binding"
                    physicalGuard ()
                guard ()
                let receiver = TelemetryStoreApplication.ciSuiteReceiverReadback root assessment assignment.ItemId scope.Producer scope.Stream
                               |> Result.defaultWith (String.concat ";" >> invalidOp)
                require (receiver.ReceiverContract = "fsgg.telemetry.ci-suite-receiver/1") "ci-continuation-capable-receiver-required"
                let collection = CanonicalJson.sha256 (Encoding.UTF8.GetBytes($"{delivery.Repository}\u001f{delivery.Head}\u001f{delivery.PullRequest}\u001f{assignment.ItemId}\u001fall-workflows"))
                let expected: TelemetryCi.ProviderCheckpoint.Identity =
                    { Assignment = assignment; AssignmentDigest = TelemetryCi.ProviderCheckpoint.digest assignmentBytes
                      StoreId = receiver.StoreId; Collection = collection; ApiOrigin = Transport.apiBaseFromEnv ()
                      Repository = delivery.Repository; RepositoryId = 0L; PullRequest = int64 delivery.PullRequest
                      BaseRef = delivery.BaseRef; BaseSha = delivery.BaseSha; Head = delivery.Head
                      ProfileDigest = ""; ParserContract = "provider-checkpoint/2" }
                let authenticatedAdmission =
                    receiver.AuthenticatedFacts |> List.exists (fun fact ->
                        match fact.Payload with
                        | TelemetryStore.CiPopulationAdmission(id, repo, pr, baseRef, baseSha, head, witness) ->
                            id = collection && repo = delivery.Repository && pr = int64 delivery.PullRequest
                            && baseRef = delivery.BaseRef && baseSha = delivery.BaseSha && head = delivery.Head && witness = "native-pr-head"
                        | _ -> false)
                require (authenticatedAdmission || canCreateAdmission delivery.Outcome delivery.CodeDelivery delivery.ObservedHead delivery.Head) "ci-continuation-original-native-admission-required"
                let token =
                    Environment.GetEnvironmentVariable("GITHUB_TOKEN") |> Option.ofObj
                    |> Option.orElseWith (fun () -> Environment.GetEnvironmentVariable("GH_TOKEN") |> Option.ofObj)
                    |> Option.filter (String.IsNullOrWhiteSpace >> not)
                use http = new Transport.HttpTransport(expected.ApiOrigin, token |> Option.defaultValue "")
                let transport =
                    transportOverride |> Option.defaultWith (fun () ->
                        { new Transport.IDeadlineSinglePageGitHubTransport with
                            member _.SendSingleWithin(request, remaining) =
                                if token.IsNone then Error(FS.GG.Coord.GitHub.Errors.Malformed(request.Subject, "GITHUB_TOKEN or GH_TOKEN is required"))
                                else (http :> Transport.IDeadlineSinglePageGitHubTransport).SendSingleWithin(request, remaining) })
                let publish payload =
                    guard ()
                    let result =
                        match testingAssessment with
                        | None -> WorkspaceTelemetryApplication.tryPublishBinding binding payload
                        | Some accepted ->
                            let envelope = continuationExpectedEnvelope scope payload
                            TelemetryStoreApplication.submitReceipt root accepted scope (Encoding.UTF8.GetBytes envelope.Canonical)
                    guard ()
                    result
                let drain () =
                    guard ()
                    let result =
                        match testingAssessment with
                        | None -> WorkspaceTelemetryApplication.tryDrainBinding binding |> Result.map ignore
                        | Some accepted -> TelemetryStoreApplication.drainReceipts root accepted scope.Workspace |> Result.map ignore
                    guard ()
                    result
                let beforeAcquisition () =
                    // Preserve the established routine outcome producer. This occurs after any
                    // retained pending publication has obtained exact receiver readback.
                    let outcomeCollection, event = projectOutcome assignment delivery
                    boundedBatches assignment outcomeCollection [ event ]
                    |> Result.defaultWith (String.concat ";" >> invalidOp)
                    |> List.iter (fun payload -> publish payload |> Result.defaultWith (String.concat ";" >> invalidOp) |> ignore)
                    drain () |> Result.defaultWith (String.concat ";" >> invalidOp)
                let status, requests, checkpoint =
                    continueCollection root ("continuation-" + collection) root assessment expected clock
                        scope transport publish drain ignore beforeAcquisition guard
                Console.Out.WriteLine(JsonSerializer.Serialize
                    {| schema = "fsgg.telemetry.ci-reconciliation/1"; status = status; driverHealth = "pending"
                       binding = "exact-authenticated-receiver"; queued = status <> "complete"
                       population = if status = "complete" then "complete" else "partial"
                       pending = if status = "complete" then [||] else [| "provider-continuation-or-receiver-readback" |]
                       nativeRequests = requests; generation = checkpoint.Generation
                       unsupportedSources = [| "merge_group"; "base"; "push" |] |}))
            0
        with error -> fail [ error.Message ]

    let private runConfiguredContinuation assess args =
        runConfiguredContinuationUsing assess (fun () -> DateTimeOffset.UtcNow) None None args

    let runConfiguredContinuationForTesting assessment clock transport args =
        runConfiguredContinuationUsing (fun _ -> assessment) clock (Some transport) (Some assessment) args

    let runWithAssessment assessment action args =
        runUsing
            (fun _ -> assessment)
            WorkspaceTelemetryApplication.resolveBinding
            WorkspaceTelemetryApplication.tryPublishBinding
            WorkspaceTelemetryApplication.tryDrainBinding
            WorkspaceTelemetryApplication.tryLocalStoreRootBound
            action
            args

    let runWithWorkspaceForTesting resolve publish drain localStoreRoot action args =
        runUsing TelemetryStoreApplication.assessProductionRoot resolve publish drain localStoreRoot action args

    let run action args =
        if action = "reconcile" && legacyRoot args = None then runConfiguredContinuation TelemetryStoreApplication.assessProductionRoot args
        else
            runUsing
                TelemetryStoreApplication.assessProductionRoot
                WorkspaceTelemetryApplication.resolveBinding
                WorkspaceTelemetryApplication.tryPublishBinding
                WorkspaceTelemetryApplication.tryDrainBinding
                WorkspaceTelemetryApplication.tryLocalStoreRootBound
                action
                args
