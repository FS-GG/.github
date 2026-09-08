namespace FS.GG.Coord.GitHub

open System
open System.Collections.Generic
open System.Diagnostics
open System.Text.Json
open FS.GG.Coord.GitHub.Errors
open FS.GG.Coord.GitHub.Transport

module CiReads =
    type Page = { Resource: string; Index: int; Count: int; Total: int }
    type Run =
        { Id: int64; Attempt: int; Workflow: string; Event: string; HeadSha: string
          Status: string; Conclusion: string option; CreatedAt: string option
          RunStartedAt: string option; UpdatedAt: string option; PullRequests: int list }
    type Step =
        { Number: int; Name: string; Status: string; Conclusion: string option
          StartedAt: string option; CompletedAt: string option }
    type Job =
        { RunId: int64; Attempt: int; Id: int64; Name: string; Status: string
          Conclusion: string option; CreatedAt: string option; StartedAt: string option
          CompletedAt: string option; RunnerName: string option; CheckRunId: int64 option; Steps: Step list }
    type Check =
        { Id: int64; Name: string; AppSlug: string option; Status: string
          Conclusion: string option; StartedAt: string option; CompletedAt: string option }
    type Snapshot =
        { Repository: string; Head: string; PullRequest: int; Workflow: string
          Binding: string; Pages: Page list; Runs: Run list; Jobs: Job list
          InventoryCoverage: string; AttemptCoverage: string; JobPageCoverage: string
          TerminalCoverage: string; TimestampCoverage: string; LineageCoverage: string; Diagnostic: string option }
    type PopulationSnapshot =
        { Snapshot: Snapshot
          BaseRef: string option
          BaseSha: string option
          Checks: Check list
          CheckCoverage: string
          ExternalChecks: int
          AdmissionWitness: bool
          Revision: int64
          Pending: string list
          Gaps: string list }

    let private str (node: JsonElement) (name: string) =
        match node.TryGetProperty name with
        | true, value when value.ValueKind = JsonValueKind.String -> Some(value.GetString())
        | _ -> None
    let private int64Value (node: JsonElement) (name: string) =
        match node.TryGetProperty name with
        | true, value when value.ValueKind = JsonValueKind.Number -> match value.TryGetInt64() with true, result -> Some result | _ -> None
        | _ -> None
    let private int32 (node: JsonElement) (name: string) = int64Value node name |> Option.bind (fun value -> if value >= 0L && value <= int64 Int32.MaxValue then Some(int value) else None)
    let private parse (subject: string) (body: string) =
        try Ok(JsonDocument.Parse body) with :? JsonException as error -> Error(Malformed(subject, error.Message))

    let collect (transport: ISinglePageGitHubTransport) (apiBase: string) (owner: string) (repo: string) (pr: int) (head: string) (workflow: string) =
        let timer = Stopwatch.StartNew()
        let mutable calls = 0
        let baseUri = Uri(apiBase.TrimEnd('/') + "/")
        let prefix = $"/repos/%s{owner}/%s{repo}/"
        let send subject path query =
            if calls >= 20 then Error(Malformed(subject, "20-request collection budget exhausted"))
            elif timer.Elapsed > TimeSpan.FromSeconds 30.0 then Error(Transport "30-second collection deadline exceeded")
            else
                calls <- calls + 1
                transport.SendSingle
                    { Method = "GET"; Path = path; Query = query; Body = NoBody; Budget = Rest
                      IfNoneMatch = None; Subject = subject }
        let continuation (subject: string) (link: string) =
            try
                let uri = Uri link
                if uri.Scheme <> baseUri.Scheme || uri.Host <> baseUri.Host || uri.Port <> baseUri.Port || not (uri.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal)) then
                    Error(Malformed(subject, "pagination continuation escaped the configured origin or repository"))
                else
                    let query =
                        uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
                        |> Array.map (fun pair -> let parts = pair.Split('=', 2) in Uri.UnescapeDataString(parts[0]), Uri.UnescapeDataString(if parts.Length = 2 then parts[1] else ""))
                        |> Array.toList
                    Ok(uri.AbsolutePath.TrimStart('/'), query)
            with :? UriFormatException -> Error(Malformed(subject, "pagination continuation is not a URI"))
        let readObject subject path query = send subject path query |> Result.bind (fun response -> parse subject response.Body |> Result.map (fun doc -> response,doc))
        let repository = $"%s{owner}/%s{repo}"
        let prSubject = $"%s{repository} PR #%d{pr} CI binding"
        match readObject prSubject $"repos/%s{owner}/%s{repo}/pulls/%d{pr}" [] with
        | Error error -> Error error
        | Ok(_, prDoc) ->
            use prDoc = prDoc
            let prHead =
                match prDoc.RootElement.TryGetProperty "head" with
                | true, value when value.ValueKind = JsonValueKind.Object -> str value "sha"
                | _ -> None
            let isMergeGroup =
                match prDoc.RootElement.TryGetProperty "merge_commit_sha" with
                | true, value when value.ValueKind = JsonValueKind.String -> value.GetString() = head && prHead <> Some head
                | _ -> false
            if prHead <> Some head || isMergeGroup then
                Ok { Repository = repository; Head = head; PullRequest = pr; Workflow = workflow; Binding = if isMergeGroup then "merge-group-unsupported" else "unresolved"
                     Pages = []; Runs = []; Jobs = []; InventoryCoverage = "unknown"; AttemptCoverage = "unknown"; JobPageCoverage = "unknown"
                     TerminalCoverage = "unknown"; TimestampCoverage = "unknown"; LineageCoverage = "unknown"; Diagnostic = Some(if isMergeGroup then "merge-group-unsupported" else "binding-unresolved") }
            else
                let pages = ResizeArray<Page>()
                let runs = Dictionary<int64 * int, Run>()
                let jobs = Dictionary<int64 * int * int64, Job>()
                let seenLinks = HashSet<string>(StringComparer.Ordinal)
                let rec readRunPage page path query =
                    let subject = $"%s{repository} workflow %s{workflow} runs @ %s{head} page %d{page}"
                    readObject subject path query
                    |> Result.bind (fun (response,document) ->
                        use document = document
                        let root = document.RootElement
                        match int32 root "total_count", root.TryGetProperty "workflow_runs" with
                        | Some total, (true, array) when array.ValueKind = JsonValueKind.Array && array.GetArrayLength() <= 100 && total <= 1000 ->
                            pages.Add { Resource = "runs"; Index = page; Count = array.GetArrayLength(); Total = total }
                            let mutable problem: IoError option = None
                            for item in array.EnumerateArray() do
                                match int64Value item "id", int32 item "run_attempt", str item "path", str item "event", str item "head_sha", str item "status" with
                                | Some id, Some attempt, Some path, Some event, Some sha, Some status when sha = head && (event = "pull_request" || event = "pull_request_target" || event = "workflow_dispatch") ->
                                    let prs =
                                        match item.TryGetProperty "pull_requests" with
                                        | true, values when values.ValueKind = JsonValueKind.Array -> values.EnumerateArray() |> Seq.choose (fun value -> int32 value "number") |> Seq.toList
                                        | _ -> []
                                    if not prs.IsEmpty && not (List.contains pr prs) then problem <- Some(Malformed(subject, "run lineage points at a different pull request"))
                                    else
                                        let row = { Id = id; Attempt = attempt; Workflow = path; Event = event; HeadSha = sha; Status = status; Conclusion = str item "conclusion"; CreatedAt = str item "created_at"; RunStartedAt = str item "run_started_at"; UpdatedAt = str item "updated_at"; PullRequests = prs }
                                        let key = id,attempt
                                        match runs.TryGetValue key with
                                        | true, existing when existing <> row -> problem <- Some(Malformed(subject, "conflicting duplicate run identity"))
                                        | _ -> runs[key] <- row
                                | _ -> problem <- Some(Malformed(subject, "run row is malformed, wrong-head, or unsupported"))
                            match problem with
                            | Some error -> Error error
                            | None ->
                                match response.NextLink with
                                | None when runs.Count = total -> Ok()
                                | None -> Error(Malformed(subject, $"run inventory reports %d{total} but collected %d{runs.Count}"))
                                | Some link when not (seenLinks.Add link) -> Error(Malformed(subject, "pagination continuation loop"))
                                | Some link -> continuation subject link |> Result.bind (fun (nextPath,nextQuery) -> readRunPage (page + 1) nextPath nextQuery)
                        | _ -> Error(Malformed(subject, "run page count/shape is invalid or exceeds 1000")))
                let readRuns = readRunPage 1 $"repos/%s{owner}/%s{repo}/actions/workflows/%s{Uri.EscapeDataString workflow}/runs" [ "head_sha",head; "per_page","100"; "page","1" ]
                let rec readJobPage (run: Run) page path query =
                    let subject = $"%s{repository} run %d{run.Id} attempt %d{run.Attempt} jobs page %d{page}"
                    readObject subject path query
                    |> Result.bind (fun (response,document) ->
                        use document = document
                        let root = document.RootElement
                        match int32 root "total_count", root.TryGetProperty "jobs" with
                        | Some total, (true,array) when array.ValueKind = JsonValueKind.Array && array.GetArrayLength() <= 100 && total <= 1000 ->
                            pages.Add { Resource = $"jobs:%d{run.Id}:%d{run.Attempt}"; Index = page; Count = array.GetArrayLength(); Total = total }
                            let mutable problem: IoError option = None
                            for item in array.EnumerateArray() do
                                match int64Value item "id", str item "name", str item "status" with
                                | Some id, Some name, Some status ->
                                    let steps =
                                        match item.TryGetProperty "steps" with
                                        | true, values when values.ValueKind = JsonValueKind.Array ->
                                            values.EnumerateArray() |> Seq.choose (fun step ->
                                                match int32 step "number", str step "name", str step "status" with
                                                | Some number, Some stepName, Some stepStatus -> Some { Number = number; Name = stepName; Status = stepStatus; Conclusion = str step "conclusion"; StartedAt = str step "started_at"; CompletedAt = str step "completed_at" }
                                                | _ -> None) |> Seq.toList
                                        | _ -> []
                                    let checkRunId =
                                        str item "check_run_url"
                                        |> Option.bind (fun value -> match Int64.TryParse(value.TrimEnd('/').Split('/') |> Array.last) with true, id -> Some id | _ -> None)
                                    let row = { RunId = run.Id; Attempt = run.Attempt; Id = id; Name = name; Status = status; Conclusion = str item "conclusion"; CreatedAt = str item "created_at"; StartedAt = str item "started_at"; CompletedAt = str item "completed_at"; RunnerName = str item "runner_name"; CheckRunId = checkRunId; Steps = steps }
                                    let key = run.Id,run.Attempt,id
                                    match jobs.TryGetValue key with
                                    | true, existing when existing <> row -> problem <- Some(Malformed(subject, "conflicting duplicate job identity"))
                                    | _ -> jobs[key] <- row
                                | _ -> problem <- Some(Malformed(subject, "job row is malformed"))
                            match problem with
                            | Some error -> Error error
                            | None ->
                                let collected = jobs.Values |> Seq.filter (fun job -> job.RunId = run.Id && job.Attempt = run.Attempt) |> Seq.length
                                match response.NextLink with
                                | None when collected = total -> Ok()
                                | None -> Error(Malformed(subject, $"job inventory reports %d{total} but collected %d{collected}"))
                                | Some link when not (seenLinks.Add link) -> Error(Malformed(subject, "pagination continuation loop"))
                                | Some link -> continuation subject link |> Result.bind (fun (nextPath,nextQuery) -> readJobPage run (page + 1) nextPath nextQuery)
                        | _ -> Error(Malformed(subject, "job page count/shape is invalid or exceeds 1000")))
                let finish complete diagnostic =
                    let runRows = runs.Values |> Seq.sortBy (fun run -> run.Id,run.Attempt) |> Seq.toList
                    let jobRows = jobs.Values |> Seq.sortBy (fun job -> job.RunId,job.Attempt,job.Id) |> Seq.toList
                    let terminal = runRows |> List.forall (fun run -> run.Status = "completed" && run.Conclusion.IsSome)
                    let timestamps = jobRows |> List.forall (fun job -> job.Status <> "completed" || (job.StartedAt.IsSome && job.CompletedAt.IsSome))
                    { Repository = repository; Head = head; PullRequest = pr; Workflow = workflow; Binding = "exact"; Pages = pages |> Seq.toList; Runs = runRows; Jobs = jobRows
                      InventoryCoverage = (if complete then "complete" else "partial")
                      AttemptCoverage = (if complete then "complete" else "partial")
                      JobPageCoverage = (if complete then "complete" else "partial")
                      TerminalCoverage = (if terminal then "complete" else "partial")
                      TimestampCoverage = (if timestamps then "complete" else "partial")
                      LineageCoverage = (if complete then "complete" else "partial")
                      Diagnostic = diagnostic }
                match readRuns with
                | Error error when pages.Count = 0 -> Error error
                | Error(Malformed(_, detail) as error) when detail.Contains("conflicting duplicate", StringComparison.Ordinal) -> Error error
                | Error _ -> Ok(finish false (Some "run-pagination-incomplete"))
                | Ok () ->
                    let jobsResult =
                        runs.Values
                        |> Seq.groupBy _.Id
                        |> Seq.collect (fun (_, versions) ->
                            let latest = versions |> Seq.maxBy _.Attempt
                            seq { for attempt in 1 .. latest.Attempt -> { latest with Attempt = attempt } })
                        |> Seq.sortBy (fun run -> run.Id,run.Attempt)
                        |> Seq.fold (fun state run -> state |> Result.bind (fun () -> readJobPage run 1 $"repos/%s{owner}/%s{repo}/actions/runs/%d{run.Id}/attempts/%d{run.Attempt}/jobs" [ "per_page","100"; "page","1" ])) (Ok())
                    match jobsResult with
                    | Ok () ->
                        let verifyAttempts =
                            runs.Values
                            |> Seq.groupBy _.Id
                            |> Seq.map (fun (_, versions) -> versions |> Seq.maxBy _.Attempt)
                            |> Seq.fold (fun state expected ->
                                state |> Result.bind (fun () ->
                                    let subject = $"%s{repository} run %d{expected.Id} attempt stability"
                                    readObject subject $"repos/%s{owner}/%s{repo}/actions/runs/%d{expected.Id}" []
                                    |> Result.bind (fun (_, document) ->
                                        use document = document
                                        match int32 document.RootElement "run_attempt" with
                                        | Some observed when observed = expected.Attempt -> Ok()
                                        | Some _ -> Error(Malformed(subject, "run attempt increased during collection"))
                                        | None -> Error(Malformed(subject, "run attempt verification is malformed"))))) (Ok())
                        match verifyAttempts with
                        | Ok () -> Ok(finish true None)
                        | Error _ -> Ok(finish false (Some "attempt-changed-or-unverified"))
                    | Error(Malformed(_, detail) as error) when detail.Contains("conflicting duplicate", StringComparison.Ordinal) -> Error error
                    | Error _ -> Ok(finish false (Some "job-pagination-incomplete"))

    let discoverPopulation (transport: ISinglePageGitHubTransport) (apiBase: string) (owner: string) (repo: string) (pr: int) (head: string) (baseRef: string) (baseSha: string) (admitted: bool) =
        let timer = Stopwatch.StartNew()
        let mutable calls = 0
        let callLimit = 40
        let baseUri = Uri(apiBase.TrimEnd('/') + "/")
        let prefix = $"/repos/%s{owner}/%s{repo}/"
        let send subject path query =
            if calls >= callLimit then Error(Malformed(subject, "request-budget-exhausted"))
            elif timer.Elapsed > TimeSpan.FromSeconds 30.0 then Error(Transport "collection-deadline-exceeded")
            else
                calls <- calls + 1
                transport.SendSingle { Method = "GET"; Path = path; Query = query; Body = NoBody; Budget = Rest; IfNoneMatch = None; Subject = subject }
        let read subject path query =
            send subject path query
            |> Result.bind (fun response -> parse subject response.Body |> Result.map (fun document -> response,document))
        let continuation subject link =
            try
                let uri = Uri link
                if uri.Scheme <> baseUri.Scheme || uri.Host <> baseUri.Host || uri.Port <> baseUri.Port || not (uri.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal)) then
                    Error(Malformed(subject, "pagination continuation escaped the configured origin or repository"))
                else
                    let query =
                        uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
                        |> Array.map (fun pair -> let values = pair.Split('=', 2) in Uri.UnescapeDataString(values[0]), Uri.UnescapeDataString(if values.Length = 2 then values[1] else ""))
                        |> Array.toList
                    Ok(uri.AbsolutePath.TrimStart('/'), query)
            with :? UriFormatException -> Error(Malformed(subject, "pagination continuation is not a URI"))
        let repository = $"%s{owner}/%s{repo}"
        let pages = ResizeArray<Page>()
        let runs = Dictionary<int64 * int, Run>()
        let jobs = Dictionary<int64 * int * int64, Job>()
        let checks = Dictionary<int64, Check>()
        let pending = ResizeArray<string>()
        let gaps = ResizeArray<string>()
        let seen = HashSet<string>(StringComparer.Ordinal)
        let mutable inventoryComplete = true
        let mutable attemptsComplete = true
        let mutable jobsComplete = true
        let mutable checksComplete = true
        let parseRun subject (item: JsonElement) =
            match int64Value item "id", int32 item "run_attempt", str item "path", str item "event", str item "head_sha", str item "status" with
            | Some id, Some attempt, Some path, Some event, Some sha, Some status when sha = head ->
                let prs =
                    match item.TryGetProperty "pull_requests" with
                    | true, values when values.ValueKind = JsonValueKind.Array -> values.EnumerateArray() |> Seq.choose (fun value -> int32 value "number") |> Seq.toList
                    | _ -> []
                if not prs.IsEmpty && not (List.contains pr prs) then Error(Malformed(subject, "run lineage points at a different pull request"))
                else
                    if event <> "pull_request" then gaps.Add("unsupported-event:" + event)
                    Ok { Id = id; Attempt = attempt; Workflow = path; Event = event; HeadSha = sha; Status = status; Conclusion = str item "conclusion"; CreatedAt = str item "created_at"; RunStartedAt = str item "run_started_at"; UpdatedAt = str item "updated_at"; PullRequests = prs }
            | _ -> Error(Malformed(subject, "run row is malformed or wrong-head"))
        let parseJob subject runId attempt (item: JsonElement) =
            match int64Value item "id", str item "name", str item "status" with
            | Some id, Some name, Some status ->
                let steps =
                    match item.TryGetProperty "steps" with
                    | true, values when values.ValueKind = JsonValueKind.Array ->
                        values.EnumerateArray() |> Seq.choose (fun step ->
                            match int32 step "number", str step "name", str step "status" with
                            | Some number, Some stepName, Some stepStatus -> Some { Number = number; Name = stepName; Status = stepStatus; Conclusion = str step "conclusion"; StartedAt = str step "started_at"; CompletedAt = str step "completed_at" }
                            | _ -> None) |> Seq.toList
                    | _ -> []
                let checkRunId = str item "check_run_url" |> Option.bind (fun value -> match Int64.TryParse(value.TrimEnd('/').Split('/') |> Array.last) with true, id -> Some id | _ -> None)
                Ok { RunId = runId; Attempt = attempt; Id = id; Name = name; Status = status; Conclusion = str item "conclusion"; CreatedAt = str item "created_at"; StartedAt = str item "started_at"; CompletedAt = str item "completed_at"; RunnerName = str item "runner_name"; CheckRunId = checkRunId; Steps = steps }
            | _ -> Error(Malformed(subject, "job row is malformed"))
        let rec runPages page path query =
            let subject = $"%s{repository} all workflow runs @ %s{head} page %d{page}"
            read subject path query |> Result.bind (fun (response,document) ->
                use document = document
                match int32 document.RootElement "total_count", document.RootElement.TryGetProperty "workflow_runs" with
                | Some total, (true,array) when array.ValueKind = JsonValueKind.Array && array.GetArrayLength() <= 100 && total <= 1000 ->
                    pages.Add { Resource = "runs:all-workflows"; Index = page; Count = array.GetArrayLength(); Total = total }
                    let results = array.EnumerateArray() |> Seq.map (parseRun subject) |> Seq.toList
                    match results |> List.tryPick (function Error error -> Some error | _ -> None) with
                    | Some error -> Error error
                    | None ->
                        for row in results |> List.choose (function Ok row -> Some row | _ -> None) do
                            let key = row.Id,row.Attempt
                            match runs.TryGetValue key with
                            | true, existing when existing <> row -> gaps.Add("conflicting-run-identity")
                            | _ -> runs[key] <- row
                        match response.NextLink with
                        | None when runs.Count = total -> Ok()
                        | None -> Error(Malformed(subject, "run inventory count mismatch"))
                        | Some link when not (seen.Add link) -> Error(Malformed(subject, "pagination continuation loop"))
                        | Some link -> continuation subject link |> Result.bind (fun (nextPath,nextQuery) -> runPages (page + 1) nextPath nextQuery)
                | _ -> Error(Malformed(subject, "run page count/shape is invalid or exceeds 1000")))
        let rec jobPages (run: Run) page path query =
            let subject = $"%s{repository} run %d{run.Id} attempt %d{run.Attempt} jobs page %d{page}"
            read subject path query |> Result.bind (fun (response,document) ->
                use document = document
                match int32 document.RootElement "total_count", document.RootElement.TryGetProperty "jobs" with
                | Some total, (true,array) when array.ValueKind = JsonValueKind.Array && array.GetArrayLength() <= 100 && total <= 1000 ->
                    pages.Add { Resource = $"jobs:%d{run.Id}:%d{run.Attempt}"; Index = page; Count = array.GetArrayLength(); Total = total }
                    let results = array.EnumerateArray() |> Seq.map (parseJob subject run.Id run.Attempt) |> Seq.toList
                    match results |> List.tryPick (function Error error -> Some error | _ -> None) with
                    | Some error -> Error error
                    | None ->
                        for row in results |> List.choose (function Ok row -> Some row | _ -> None) do jobs[(row.RunId,row.Attempt,row.Id)] <- row
                        let count = jobs.Values |> Seq.filter (fun row -> row.RunId = run.Id && row.Attempt = run.Attempt) |> Seq.length
                        match response.NextLink with
                        | None when count = total -> Ok()
                        | None -> Error(Malformed(subject, "job inventory count mismatch"))
                        | Some link when not (seen.Add link) -> Error(Malformed(subject, "pagination continuation loop"))
                        | Some link -> continuation subject link |> Result.bind (fun (nextPath,nextQuery) -> jobPages run (page + 1) nextPath nextQuery)
                | _ -> Error(Malformed(subject, "job page count/shape is invalid or exceeds 1000")))
        let rec checkPages page path query =
            let subject = $"%s{repository} native checks @ %s{head} page %d{page}"
            read subject path query |> Result.bind (fun (response,document) ->
                use document = document
                match int32 document.RootElement "total_count", document.RootElement.TryGetProperty "check_runs" with
                | Some total, (true,array) when array.ValueKind = JsonValueKind.Array && array.GetArrayLength() <= 100 && total <= 1000 ->
                    pages.Add { Resource = "checks"; Index = page; Count = array.GetArrayLength(); Total = total }
                    for item in array.EnumerateArray() do
                        match int64Value item "id", str item "name", str item "status" with
                        | Some id, Some name, Some status ->
                            let slug = match item.TryGetProperty "app" with true, app when app.ValueKind = JsonValueKind.Object -> str app "slug" | _ -> None
                            let row = { Id = id; Name = name; AppSlug = slug; Status = status; Conclusion = str item "conclusion"; StartedAt = str item "started_at"; CompletedAt = str item "completed_at" }
                            match checks.TryGetValue id with true, existing when existing <> row -> gaps.Add("conflicting-check-identity") | _ -> checks[id] <- row
                        | _ -> gaps.Add("malformed-check-row")
                    match response.NextLink with
                    | None when checks.Count = total -> Ok()
                    | None -> Error(Malformed(subject, "check inventory count mismatch"))
                    | Some link when not (seen.Add link) -> Error(Malformed(subject, "pagination continuation loop"))
                    | Some link -> continuation subject link |> Result.bind (fun (nextPath,nextQuery) -> checkPages (page + 1) nextPath nextQuery)
                | _ -> Error(Malformed(subject, "check page count/shape is invalid or exceeds 1000")))
        let prSubject = $"%s{repository} PR #%d{pr} population admission"
        match read prSubject $"repos/%s{owner}/%s{repo}/pulls/%d{pr}" [] with
        | Error error -> Error error
        | Ok(_,document) ->
            use document = document
            let currentHead = match document.RootElement.TryGetProperty "head" with true, value when value.ValueKind = JsonValueKind.Object -> str value "sha" | _ -> None
            let currentBaseRef,currentBaseSha =
                match document.RootElement.TryGetProperty "base" with
                | true, value when value.ValueKind = JsonValueKind.Object -> str value "ref", str value "sha"
                | _ -> None,None
            let witnessed = currentHead = Some head && currentBaseRef = Some baseRef && currentBaseSha = Some baseSha
            if not witnessed && not admitted then
                let code = if currentHead <> Some head then "head-not-admitted" elif currentBaseRef <> Some baseRef then "base-ref-mismatch" else "base-sha-mismatch"
                let snapshot = { Repository = repository; Head = head; PullRequest = pr; Workflow = "*"; Binding = "unresolved"; Pages = []; Runs = []; Jobs = []; InventoryCoverage = "unknown"; AttemptCoverage = "unknown"; JobPageCoverage = "unknown"; TerminalCoverage = "unknown"; TimestampCoverage = "unknown"; LineageCoverage = "unknown"; Diagnostic = Some code }
                Ok { Snapshot = snapshot; BaseRef = currentBaseRef; BaseSha = currentBaseSha; Checks = []; CheckCoverage = "unknown"; ExternalChecks = 0; AdmissionWitness = false; Revision = 0L; Pending = []; Gaps = [ code ] }
            else
                if admitted && (currentBaseRef <> Some baseRef || currentBaseSha <> Some baseSha) then gaps.Add "admitted-base-moved"
                match runPages 1 $"repos/%s{owner}/%s{repo}/actions/runs" [ "head_sha",head; "per_page","100"; "page","1" ] with
                | Error _ -> inventoryComplete <- false; pending.Add "actions-runs"
                | Ok () -> ()
                let listed = runs.Values |> Seq.toList
                for latest in listed |> Seq.groupBy _.Id |> Seq.map (snd >> Seq.maxBy _.Attempt) |> Seq.sortBy _.Id do
                    for attempt in 1 .. latest.Attempt do
                        let runKey = latest.Id,attempt
                        let run =
                            match runs.TryGetValue runKey with
                            | true, row -> Some row
                            | _ ->
                                let subject = $"%s{repository} run %d{latest.Id} attempt %d{attempt}"
                                match read subject $"repos/%s{owner}/%s{repo}/actions/runs/%d{latest.Id}/attempts/%d{attempt}" [] with
                                | Ok(_,doc) -> use doc = doc in match parseRun subject doc.RootElement with Ok row -> runs[(row.Id,row.Attempt)] <- row; Some row | Error _ -> attemptsComplete <- false; pending.Add($"run:%d{latest.Id}:%d{attempt}"); None
                                | Error _ -> attemptsComplete <- false; pending.Add($"run:%d{latest.Id}:%d{attempt}"); None
                        match run with
                        | Some row -> match jobPages row 1 $"repos/%s{owner}/%s{repo}/actions/runs/%d{row.Id}/attempts/%d{row.Attempt}/jobs" [ "per_page","100"; "page","1" ] with Ok () -> () | Error _ -> jobsComplete <- false; pending.Add($"jobs:%d{row.Id}:%d{row.Attempt}")
                        | None -> ()
                match checkPages 1 $"repos/%s{owner}/%s{repo}/commits/%s{head}/check-runs" [ "per_page","100"; "page","1" ] with Ok () -> () | Error _ -> checksComplete <- false; pending.Add "check-runs"
                // A final native inventory read detects a run arriving while this bounded pass was collecting.
                if inventoryComplete then
                    let subject = $"%s{repository} workflow inventory stability @ %s{head}"
                    match read subject $"repos/%s{owner}/%s{repo}/actions/runs" [ "head_sha",head; "per_page","100"; "page","1" ] with
                    | Ok(_,doc) ->
                        use doc = doc
                        match int32 doc.RootElement "total_count" with Some count when count = listed.Length -> () | _ -> inventoryComplete <- false; pending.Add "actions-runs-changed"
                    | Error _ -> inventoryComplete <- false; pending.Add "actions-runs-unverified"
                // Check-runs are independently mutable; do not call one page a complete population
                // unless the native total still agrees at the end of this bounded pass.
                if checksComplete then
                    let subject = $"%s{repository} check inventory stability @ %s{head}"
                    match read subject $"repos/%s{owner}/%s{repo}/commits/%s{head}/check-runs" [ "per_page","1"; "page","1" ] with
                    | Ok(_,doc) ->
                        use doc = doc
                        match int32 doc.RootElement "total_count" with Some count when count = checks.Count -> () | _ -> checksComplete <- false; pending.Add "check-runs-changed"
                    | Error _ -> checksComplete <- false; pending.Add "check-runs-unverified"
                let actionCheckIds = checks.Values |> Seq.filter (fun row -> row.AppSlug = Some "github-actions") |> Seq.map _.Id |> Set.ofSeq
                let jobCheckIds = jobs.Values |> Seq.choose _.CheckRunId |> Set.ofSeq
                let external = checks.Values |> Seq.filter (fun row -> row.AppSlug <> Some "github-actions") |> Seq.length
                if external > 0 then gaps.Add "external-checks-not-attributed"
                if checksComplete && (actionCheckIds <> jobCheckIds || jobs.Values |> Seq.exists _.CheckRunId.IsNone) then checksComplete <- false; pending.Add "workflow-check-inventory-mismatch"
                let runRows = runs.Values |> Seq.sortBy (fun row -> row.Id,row.Attempt) |> Seq.toList
                let jobRows = jobs.Values |> Seq.sortBy (fun row -> row.RunId,row.Attempt,row.Id) |> Seq.toList
                let terminal = runRows |> List.forall (fun row -> row.Status = "completed" && row.Conclusion.IsSome)
                let timestamps = jobRows |> List.forall (fun row -> row.Status <> "completed" || (row.StartedAt.IsSome && row.CompletedAt.IsSome))
                let complete = inventoryComplete && attemptsComplete && jobsComplete && checksComplete && (gaps |> Seq.forall (fun code -> code = "external-checks-not-attributed"))
                let latestTimestamp = runRows |> List.choose _.UpdatedAt |> List.choose (fun value -> match DateTimeOffset.TryParse value with true, parsed -> Some(parsed.ToUnixTimeMilliseconds()) | _ -> None) |> List.fold max 0L
                let revision = max 1L latestTimestamp
                let diagnostic = if complete then None else pending |> Seq.tryHead |> Option.orElseWith (fun () -> gaps |> Seq.tryHead)
                let snapshot =
                    { Repository = repository
                      Head = head
                      PullRequest = pr
                      Workflow = "*"
                      Binding = (if witnessed then "exact" else "admitted-superseded")
                      Pages = List.ofSeq pages
                      Runs = runRows
                      Jobs = jobRows
                      InventoryCoverage = (if inventoryComplete && checksComplete then "complete" else "partial")
                      AttemptCoverage = (if attemptsComplete then "complete" else "partial")
                      JobPageCoverage = (if jobsComplete then "complete" else "partial")
                      TerminalCoverage = (if terminal then "complete" else "partial")
                      TimestampCoverage = (if timestamps then "complete" else "partial")
                      LineageCoverage = (if inventoryComplete then "complete" else "partial")
                      Diagnostic = diagnostic }
                Ok
                    { Snapshot = snapshot
                      BaseRef = currentBaseRef
                      BaseSha = currentBaseSha
                      Checks = checks.Values |> Seq.sortBy _.Id |> Seq.toList
                      CheckCoverage = (if checksComplete then "complete" else "partial")
                      ExternalChecks = external
                      AdmissionWitness = witnessed
                      Revision = revision
                      Pending = List.ofSeq pending |> List.distinct
                      Gaps = List.ofSeq gaps |> List.distinct }
