namespace FS.GG.Coord.GitHub

module OperationObservationReads =
    open System
    open System.Text
    open System.Text.Json
    open System.Security.Cryptography
    open FS.GG.Coord.OperationObservation
    open Transport

    exception private ReadFailure of string

    let collect (transport: IDeadlineSinglePageGitHubTransport) (identity: Identity) clock workDeadline (beforeRequest: unit -> Result<unit, string>) =
        let refuse message = raise (ReadFailure message)
        let property name (node: JsonElement) =
            if node.ValueKind <> JsonValueKind.Object then refuse "native object missing"
            match node.TryGetProperty(name: string) with
            | true, value -> value
            | _ -> refuse "native field missing"
        let str name node =
            let value = property name node
            if value.ValueKind <> JsonValueKind.String then refuse "native text missing"
            value.GetString()
        let integer name node =
            let value = property name node
            match value.TryGetInt64() with true, number when number > 0L -> number | _ -> refuse "native positive identity missing"
        let optional name node =
            let value = property name node
            match value.ValueKind with
            | JsonValueKind.Null -> None
            | JsonValueKind.String -> Some(value.GetString())
            | _ -> refuse "native nullable text malformed"
        let boolean name node =
            let value = property name node
            match value.ValueKind with JsonValueKind.True -> true | JsonValueKind.False -> false | _ -> refuse "native boolean missing"
        let rec unique (node: JsonElement) =
            match node.ValueKind with
            | JsonValueKind.Object ->
                let fields = node.EnumerateObject() |> Seq.toList
                Set.count (fields |> List.map _.Name |> Set.ofList) = fields.Length && fields |> List.forall (fun field -> unique field.Value)
            | JsonValueKind.Array -> node.EnumerateArray() |> Seq.forall unique
            | _ -> true
        let request path query =
            if clock() >= workDeadline then refuse "original work deadline reached"
            match beforeRequest() with Error _ -> refuse "original request allowance unavailable" | Ok () -> ()
            let remaining = min 30000L (workDeadline - clock())
            if remaining <= 0L then refuse "original work deadline reached"
            let query = { Method = "GET"; Path = path; Query = query; Body = NoBody; Budget = Rest
                          IfNoneMatch = None; Subject = "original-operation-observation" }
            match transport.SendSingleWithin(query, TimeSpan.FromMilliseconds(float remaining)) with
            | Error _ -> refuse "native read unavailable; original effects remain Unknown"
            | Ok response ->
                if clock() >= workDeadline then refuse "native response after original work deadline"
                if response.Status <> 200 then refuse "native read did not return200"
                if Encoding.UTF8.GetByteCount response.Body > 262144 then refuse "native page exceeds accepted256KiB bound"
                use document = JsonDocument.Parse(response.Body, JsonDocumentOptions(MaxDepth = 24))
                if not (unique document.RootElement) then refuse "duplicate native JSON fields"
                document.RootElement.Clone(), response.NextLink
        let noNext (value, next) =
            if Option.isSome next then refuse "unexpected native continuation"
            value
        try
            match validateIdentity identity with Error _ -> refuse "invalid original operation identity" | Ok _ -> ()
            let root = "repos/" + identity.Repository
            let prPath = root + "/pulls/" + string identity.PullRequest
            let issueParts = identity.Issue.Split '#'
            let issuePath = "repos/" + issueParts[0] + "/issues/" + issueParts[1]
            let checkIssue () =
                let issue = request issuePath [] |> noNext
                let bodyDigest = SHA256.HashData(Encoding.UTF8.GetBytes(str "body" issue)) |> Convert.ToHexString |> _.ToLowerInvariant()
                if str "updated_at" issue <> identity.IssueUpdatedAt || bodyDigest <> identity.IssueBodySha256
                   || str "state" issue <> "open" || string (integer "number" issue) <> issueParts[1] then
                    refuse "owning issue revision or original ownership changed"
            checkIssue()
            let before = request prPath [] |> noNext
            let prHead node = property "head" node |> str "sha"
            let head = prHead before
            let empty observedHead runId attempt : Sample =
                { Head = observedHead; RunId = runId; RunAttempt = attempt; RunStatus = "unknown"
                  RunConclusion = None; PullRequestState = "unknown"; Merged = false; Draft = false
                  Checks = []; Complete = false; Gaps = [ "native identity changed" ] }
            if head <> identity.Head then Ok(empty head identity.RunId identity.RunAttempt)
            else
                let runPath = root + "/actions/runs/" + string identity.RunId
                let run = request runPath [] |> noNext
                let runId = integer "id" run
                let attempt = integer "run_attempt" run
                let runHead = str "head_sha" run
                if attempt > int64 Int32.MaxValue then refuse "unsupported native attempt"
                if runId <> identity.RunId || int attempt <> identity.RunAttempt then Ok(empty runHead runId (int attempt))
                elif runHead <> identity.Head then Ok(empty runHead runId (int attempt))
                else
                    // Native run relation must name this exact PR, including repo, head and source branch.
                    let relations = property "pull_requests" run
                    if relations.ValueKind <> JsonValueKind.Array then refuse "native PR association unavailable"
                    let matches = relations.EnumerateArray() |> Seq.filter (fun relation -> integer "number" relation = int64 identity.PullRequest) |> Seq.toList
                    if matches.Length <> 1 then refuse "native run/PR association missing or ambiguous"
                    let relationHead = property "head" matches.Head
                    if str "sha" relationHead <> identity.Head then refuse "native run/PR head association differs"
                    let currentHead = property "head" before
                    if (property "repo" relationHead |> integer "id") <> (property "repo" currentHead |> integer "id")
                       || (property "repository" run |> str "full_name") <> identity.Repository then
                        refuse "native run/PR repository association differs"
                    let jobsPath = runPath + "/attempts/" + string identity.RunAttempt + "/jobs"
                    let parseJobs page =
                        let totalValue = property "total_count" page
                        let total = match totalValue.TryGetInt32() with true, count when count >= 0 && count <= 128 -> count | _ -> refuse "native jobs population exceeds bound or is missing"
                        let values = property "jobs" page
                        if values.ValueKind <> JsonValueKind.Array then refuse "native jobs missing"
                        let jobs = values.EnumerateArray() |> Seq.toList
                        if jobs.Length > 100 then refuse "native page exceeds requested population"
                        let checks = jobs |> List.map (fun job ->
                            if integer "run_id" job <> identity.RunId || integer "run_attempt" job <> int64 identity.RunAttempt
                               || str "head_sha" job <> identity.Head then refuse "native job identity differs"
                            { Id = integer "id" job; Name = str "name" job; Status = str "status" job
                              Conclusion = optional "conclusion" job; StartedAt = optional "started_at" job
                              CompletedAt = optional "completed_at" job })
                        total, checks
                    let readJobs () =
                        let first, firstNext = request jobsPath [ "per_page", "100"; "page", "1" ]
                        let total, firstJobs = parseJobs first
                        let checks =
                            if total > 100 then
                                if firstJobs.Length <> 100 || firstNext.IsNone then refuse "native jobs first page incomplete"
                                // Construct the sole allowed second page; never follow a provider-supplied URL.
                                let second, next = request jobsPath [ "per_page", "100"; "page", "2" ]
                                let secondTotal, secondJobs = parseJobs second
                                if next.IsSome || secondTotal <> total || firstJobs.Length + secondJobs.Length <> total then refuse "native jobs continuation incomplete or changed"
                                firstJobs @ secondJobs
                            else
                                if firstNext.IsSome || firstJobs.Length <> total then refuse "native jobs page incomplete"
                                firstJobs
                        if checks |> List.map _.Id |> Set.ofList |> Set.count <> checks.Length then refuse "duplicate native job identities"
                        checks
                    let firstChecks = readJobs()
                    let checks = readJobs()
                    if (firstChecks |> List.map _.Id |> Set.ofList) <> (checks |> List.map _.Id |> Set.ofList) then
                        refuse "native jobs population changed between complete sweeps"
                    // Re-read run metadata to fence reruns that race jobs acquisition, then PR head.
                    let afterRun = request runPath [] |> noNext
                    let after = request prPath [] |> noNext
                    checkIssue()
                    if integer "run_attempt" afterRun > int64 Int32.MaxValue then refuse "unsupported native final attempt"
                    if integer "run_attempt" afterRun <> int64 identity.RunAttempt then
                        Ok(empty (prHead after) identity.RunId (int (integer "run_attempt" afterRun)))
                    elif integer "id" afterRun <> identity.RunId || str "head_sha" afterRun <> identity.Head then
                        refuse "native run identity changed at final barrier"
                    else
                        Ok { Head = prHead after; RunId = identity.RunId; RunAttempt = identity.RunAttempt
                             RunStatus = str "status" afterRun; RunConclusion = optional "conclusion" afterRun
                             PullRequestState = str "state" after; Merged = boolean "merged" after; Draft = boolean "draft" after
                             Checks = checks
                             Complete = str "status" afterRun <> "completed" || str "status" run = "completed"
                             Gaps = if str "status" afterRun = "completed" && str "status" run <> "completed"
                                    then [ "native run completed across jobs barrier; root reconciliation required" ] else [] }
        with
        | ReadFailure message -> Error message
        | :? JsonException -> Error "malformed native JSON"
        | :? InvalidOperationException -> Error "malformed native field kind"
        | :? ArgumentException -> Error "malformed native value"
