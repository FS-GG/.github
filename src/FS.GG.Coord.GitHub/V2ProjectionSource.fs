namespace FS.GG.Coord.GitHub

module V2ProjectionSource =
    open System
    open System.Text.Json
    open Errors
    open Transport
    open V2Projection

    type NativeObservation =
        { Issue: IssueRef; NodeId: string; UpdatedAt: string; State: string; Url: string; BodySha256: string }

    type ItemReport =
        { Issue: IssueRef
          Native: NativeObservation option
          DependencyObservations: NativeObservation list
          DependencyReadComplete: bool
          PlanObservation: NativeObservation option
          Delivery: string
          Publication: string
          NativeAcceptance: string
          Health: string
          Reads: int
          MutationAttempts: int
          MembershipPages: int option
          Projection: Report option
          Gap: IoError option
          LastVerified: Report option }

    type BatchReport =
        { ProjectId: string
          RecipeRevision: string
          PopulationRevision: string
          ImportRecipeRevision: string
          ImportArtifactSha256: string
          ArtifactSha256: string
          NativeDispatch: V2ObservationTransport.DispatchReceipt list
          ProtectedInputs: (string * string * string) list
          Selected: int option
          Attempted: int
          Verified: int
          Items: ItemReport list
          PopulationGap: IoError option
          Cleanup: string }

    let private query document variables subject : Transport.Request =
        { Method = "POST"; Path = "graphql"; Query = []; Body = Query(document, variables)
          Budget = GraphQl; IfNoneMatch = None; Subject = subject }

    let private readIssue (transport: IGitHubTransport) (issue: IssueRef) =
        let subject = $"{issue.Owner}/{issue.Repository}#{issue.Number} native observation"
        let document = "query($owner: String!, $repo: String!, $number: Int!) { repository(owner: $owner, name: $repo) { nameWithOwner issue(number: $number) { id number updatedAt state url body repository { nameWithOwner } } } rateLimit { cost remaining } }"
        GraphQl.read transport (query document [ "owner", VString issue.Owner; "repo", VString issue.Repository; "number", VNumber(double issue.Number) ] subject) (fun data ->
            let repository = data.GetProperty "repository"
            let native = repository.GetProperty "issue"
            let expected = $"{issue.Owner}/{issue.Repository}"
            let nodeId = native.GetProperty("id").GetString()
            let updated = native.GetProperty("updatedAt").GetString()
            let state = native.GetProperty("state").GetString()
            match DateTimeOffset.TryParse updated with
            | true, _ when repository.GetProperty("nameWithOwner").GetString() = expected
                           && native.GetProperty("repository").GetProperty("nameWithOwner").GetString() = expected
                           && native.GetProperty("number").GetInt32() = issue.Number
                           && not (String.IsNullOrWhiteSpace nodeId)
                           && (state = "OPEN" || state = "CLOSED")
                           && native.GetProperty("body").ValueKind = JsonValueKind.String
                           && native.GetProperty("url").GetString() = $"https://github.com/{expected}/issues/{issue.Number}" ->
                Ok { Issue = issue; NodeId = nodeId; UpdatedAt = updated; State = state; Url = native.GetProperty("url").GetString()
                     BodySha256 = native.GetProperty("body").GetString() |> System.Text.Encoding.UTF8.GetBytes |> System.Security.Cryptography.SHA256.HashData |> Convert.ToHexStringLower }
            | _ -> Error(Malformed(subject, "native identity or revision is unreadable/mismatched")))

    let private readProtectedBlob (transport: IGitHubTransport) (revision: string) (path: string) =
        let subject = $"protected recipe {revision}:{path}"
        if revision.Length <> 40 || revision |> Seq.exists (fun character -> not (Uri.IsHexDigit character)) then
            Error(Malformed(subject, "reviewed recipe must be an immutable Git commit SHA"))
        elif not (path.StartsWith("docs/", StringComparison.Ordinal)) || path.Contains("..") || path.Contains(":") || path.Contains("\\") then
            Error(Malformed(subject, "reviewed source path must remain within canonical docs"))
        else
            let document = "query($expression: String!, $currentExpression: String!) { repository(owner: \"FS-GG\", name: \".github\") { nameWithOwner ref(qualifiedName: \"refs/heads/main\") { target { oid } } object(expression: $expression) { ... on Blob { oid text } } current: object(expression: $currentExpression) { ... on Blob { oid text } } } rateLimit { cost remaining } }"
            GraphQl.read transport (query document [ "expression", VString $"{revision}:{path}"; "currentExpression", VString $"main:{path}" ] subject) (fun data ->
                let repository = data.GetProperty "repository"
                let text = repository.GetProperty("object").GetProperty("text").GetString()
                if repository.GetProperty("nameWithOwner").GetString() <> "FS-GG/.github"
                   || repository.GetProperty("object").GetProperty("oid").GetString() <> repository.GetProperty("current").GetProperty("oid").GetString()
                   || text <> repository.GetProperty("current").GetProperty("text").GetString()
                   || String.IsNullOrWhiteSpace(repository.GetProperty("object").GetProperty("oid").GetString())
                   || String.IsNullOrWhiteSpace text then
                    Error(Malformed(subject, "canonical source blob is unavailable"))
                else Ok(text, repository.GetProperty("ref").GetProperty("target").GetProperty("oid").GetString(), repository.GetProperty("object").GetProperty("oid").GetString()))

    let private parseIssue (value: string) =
        let hash = value.LastIndexOf '#'
        let slash = value.IndexOf '/'
        let mutable number = 0
        if slash <= 0 || hash <= slash + 1 || not (Int32.TryParse(value.Substring(hash + 1), &number)) || number <= 0 then
            Error(Malformed("reviewed native issue", "canonical OWNER/REPO#NUMBER required"))
        else Ok { Owner = value.Substring(0, slash); Repository = value.Substring(slash + 1, hash - slash - 1); Number = number }

    let private readBlockedBy (transport: IGitHubTransport) (issue: IssueRef) =
        let subject = $"{issue.Owner}/{issue.Repository}#{issue.Number} native blocked_by"
        let request: Transport.Request =
            { Method = "GET"; Path = $"repos/{issue.Owner}/{issue.Repository}/issues/{issue.Number}/dependencies/blocked_by"
              Query = [ "per_page", "50" ]; Body = NoBody; Budget = Rest; IfNoneMatch = None; Subject = subject }
        match transport.Send request with
        | Error error -> Error error
        | Ok response when response.Status <> 200 || response.NextLink.IsSome -> Error(Malformed(subject, "native dependency snapshot incomplete or denied"))
        | Ok response ->
            try
                use document = JsonDocument.Parse response.Body
                let nodes = document.RootElement
                if nodes.ValueKind <> JsonValueKind.Array || nodes.GetArrayLength() >= 50 then Error(Malformed(subject, "native dependency bound reached; completeness unknown"))
                else
                    let parsed = nodes.EnumerateArray() |> Seq.map (fun node ->
                        let url = node.GetProperty("html_url").GetString()
                        let uri = Uri url
                        let parts = uri.AbsolutePath.Trim('/').Split('/')
                        if uri.Scheme <> "https" || uri.Host <> "github.com" || uri.UserInfo <> "" || uri.Query <> "" || uri.Fragment <> "" || parts.Length <> 4 || parts[0] <> "FS-GG" || parts[2] <> "issues" || (node.TryGetProperty("pull_request") |> fst) then
                            Error(Malformed(subject, "native blocker identity is not an FS-GG issue"))
                        else
                            let native: NativeObservation =
                                { Issue = { Owner = parts[0]; Repository = parts[1]; Number = node.GetProperty("number").GetInt32() }
                                  NodeId = node.GetProperty("node_id").GetString(); UpdatedAt = node.GetProperty("updated_at").GetString()
                                  State = node.GetProperty("state").GetString().ToUpperInvariant(); Url = url; BodySha256 = "" }
                            if string native.Issue.Number <> parts[3] || String.IsNullOrWhiteSpace native.NodeId
                               || not (fst (DateTimeOffset.TryParse native.UpdatedAt)) || (native.State <> "OPEN" && native.State <> "CLOSED") then Error(Malformed(subject, "native blocker node identity missing/mismatched"))
                            else Ok native) |> Seq.toList
                    match parsed |> List.tryPick (function Error error -> Some error | _ -> None) with
                    | Some error -> Error error
                    | None -> Ok(parsed |> List.choose (function Ok native -> Some native | _ -> None))
            with
            | :? JsonException
            | :? Collections.Generic.KeyNotFoundException
            | :? InvalidOperationException
            | :? UriFormatException
            | :? ArgumentException -> Error(Malformed(subject, "unreadable native dependency snapshot"))

    let private readPlan (transport: IGitHubTransport) (binding: Binding) (path: string) =
        if path = "https://github.com/FS-GG/.github/issues/3008" && binding.Repositories.Contains "FS-GG/.github" then
            readIssue transport { Owner = "FS-GG"; Repository = ".github"; Number = 3008 } |> Result.bind (fun native ->
                let emptyDigest = System.Security.Cryptography.SHA256.HashData(Array.empty<byte>) |> Convert.ToHexStringLower
                if native.Url <> path || native.BodySha256 = emptyDigest then Error(Malformed("programme issue", "native URL/body is unreadable or mismatched"))
                else Ok(Some native, $"{native.NodeId}@{native.UpdatedAt}:{native.Url}:{native.BodySha256}"))
        elif path.StartsWith("docs/", StringComparison.Ordinal) then
            readProtectedBlob transport binding.PopulationRevision path |> Result.map (fun (_, _, blob) -> None, $"{path}:{blob}")
        else Error(Malformed("owning plan", "only canonical protected docs or the selected programme issue URL is admitted"))

    type private Selected =
        { Issue: IssueRef; NodeId: string; UpdatedAt: string; Roadmap: string; Dependencies: IssueRef list }

    let private population (binding: Binding) (text: string) =
        try
            use document = JsonDocument.Parse(text: string)
            let root = document.RootElement
            let target = root.GetProperty "target"
            let admission = root.GetProperty "binding"
            let repositories = admission.GetProperty("repositories").EnumerateArray() |> Seq.map (fun value -> value.GetString()) |> Set.ofSeq
            if admission.GetProperty("organizationId").GetString() <> binding.OrganizationId
               || admission.GetProperty("visibility").GetString() <> "complete"
               || admission.GetProperty("authorization").GetString() <> "root-selected"
               || admission.GetProperty("recipeRevision").GetString() <> binding.ImportRecipeRevision
               || admission.GetProperty("artifactSha256").GetString() <> binding.ImportArtifactSha256
               || repositories <> binding.Repositories
               || target.GetProperty("creationState").GetString() <> "created-and-read-back"
               || root.GetProperty("schema").GetString() <> "fsgg.coordination-board-v2-import/v1"
               || target.GetProperty("ownerKind").GetString() <> "organization"
               || target.GetProperty("owner").GetString() <> binding.Owner
               || target.GetProperty("title").GetString() <> binding.ProjectTitle
               || target.GetProperty("id").GetString() <> binding.ProjectId
               || target.GetProperty("number").GetInt32() <> binding.ProjectNumber then
                Error(Malformed("reviewed population", "manifest is not bound to the exact admitted project"))
            else
                let rows = root.GetProperty("items").EnumerateArray() |> Seq.filter (fun row -> row.GetProperty("pilot").GetBoolean() && (row.GetProperty("decision").GetString() = "import" || row.GetProperty("decision").GetString() = "follow-up") && row.GetProperty("adjudication").GetString() = "verified-remaining") |> Seq.toList
                if rows.Length < 3 || rows.Length > 5 then Error(Malformed("reviewed population", "admitted pilot requires three to five native issues"))
                else
                    let parsed = rows |> List.map (fun row ->
                        match parseIssue (row.GetProperty("issue").GetString()) with
                        | Error error -> Error error
                        | Ok issue ->
                            let dependencies = row.GetProperty("dependencies").EnumerateArray() |> Seq.map (fun dependency -> parseIssue (dependency.GetProperty("issue").GetString())) |> Seq.toList
                            match dependencies |> List.tryPick (function Error error -> Some error | _ -> None) with
                            | Some error -> Error error
                            | None ->
                                let repo = $"{issue.Owner}/{issue.Repository}"
                                let nodeId = row.GetProperty("nodeId").GetString()
                                let updated = row.GetProperty("observedUpdatedAt").GetString()
                                let roadmap = row.GetProperty("roadmap").GetString()
                                if dependencies.Length <> 0 || row.GetProperty("observedState").GetString() <> "open" || not (binding.Repositories.Contains repo) || String.IsNullOrWhiteSpace nodeId || String.IsNullOrWhiteSpace updated || String.IsNullOrWhiteSpace roadmap then
                                    Error(Malformed("reviewed population", "missing canonical identity/revision/plan/scope or nonempty dependency snapshot outside this reviewed empty pilot"))
                                else Ok { Issue = issue; NodeId = nodeId; UpdatedAt = updated; Roadmap = roadmap; Dependencies = dependencies |> List.choose (function Ok value -> Some value | _ -> None) })
                    match parsed |> List.tryPick (function Error error -> Some error | _ -> None) with
                    | Some error -> Error error
                    | None ->
                        let selected = parsed |> List.choose (function Ok value -> Some value | _ -> None)
                        if selected |> List.map (fun row -> row.NodeId) |> Set.ofList |> Set.count <> selected.Length then
                            Error(Malformed("reviewed population", "duplicate native issue identities"))
                        elif selected |> List.map (fun row -> row.NodeId, $"{row.Issue.Owner}/{row.Issue.Repository}#{row.Issue.Number}") |> Map.ofList <> binding.SelectedIssues then
                            Error(Malformed("reviewed population", "selected native cohort differs from explicit root binding"))
                        else Ok selected
        with
        | :? JsonException
        | :? Collections.Generic.KeyNotFoundException
        | :? InvalidOperationException -> Error(Malformed("reviewed population", "manifest is incomplete or nonexecutable"))

    type private SourceCollection =
        { Verification: IoResult<SourceVerification>; Native: NativeObservation option
          Dependencies: NativeObservation list; DependenciesComplete: bool; Plan: NativeObservation option }

    let private collectSource (transport: IGitHubTransport) (binding: Binding) (row: Selected) =
        let mutable native = None
        let mutable dependencies = []
        let mutable dependenciesComplete = false
        let mutable plan = None
        let verification =
            match readIssue transport row.Issue with
            | Error error -> Error error
            | Ok observation ->
                native <- Some observation
                if observation.NodeId <> row.NodeId then Ok(Refused "native issue node identity changed")
                elif observation.UpdatedAt <> row.UpdatedAt || observation.State <> "OPEN" then Ok(Stale None)
                else
                    match readBlockedBy transport row.Issue with
                    | Error error -> Error error
                    | Ok observed ->
                        dependencies <- observed
                        dependenciesComplete <- true
                        let actual = observed |> List.map _.Issue |> Set.ofList
                        if actual <> Set.ofList row.Dependencies then Ok(Stale None)
                        else
                            match readPlan transport binding row.Roadmap with
                            | Error error -> Error error
                            | Ok(planObservation, planRevision) ->
                                plan <- planObservation
                                let dependencyRevision = dependencies |> List.map (fun dependency -> $"{dependency.NodeId}@{dependency.UpdatedAt}:{dependency.State}") |> List.sort |> String.concat ";"
                                Ok(Current $"{observation.NodeId}@{observation.UpdatedAt};dependencies=complete:{dependencyRevision};plan={planRevision};recipe={binding.RecipeRevision}")
        { Verification = verification; Native = native; Dependencies = dependencies; DependenciesComplete = dependenciesComplete; Plan = plan }

    let private artifactCheck (binding: Binding) =
        try
            let location = typeof<Binding>.Assembly.Location
            if String.IsNullOrWhiteSpace location then Error(Malformed("reviewed executable", "loaded assembly has no verifiable file identity"))
            else
                use stream = System.IO.File.OpenRead location
                let actual = System.Security.Cryptography.SHA256.HashData stream |> Convert.ToHexStringLower
                if not (String.Equals(actual, binding.ArtifactSha256, StringComparison.OrdinalIgnoreCase)) then Error(Malformed("reviewed executable", "loaded assembly bytes differ from selected artifact SHA-256"))
                else Ok()
        with :? System.IO.IOException -> Error(Malformed("reviewed executable", "loaded assembly bytes unavailable"))

    let runFixed (transport: IGitHubTransport) (binding: Binding) (previous: BatchReport option) =
        let failed error =
            { ProjectId = binding.ProjectId; RecipeRevision = binding.RecipeRevision; PopulationRevision = binding.PopulationRevision; ImportRecipeRevision = binding.ImportRecipeRevision; ImportArtifactSha256 = binding.ImportArtifactSha256; ArtifactSha256 = binding.ArtifactSha256; NativeDispatch = []; ProtectedInputs = []; Selected = None; Attempted = 0; Verified = 0
              Items = previous |> Option.filter (fun batch -> batch.ProjectId = binding.ProjectId) |> Option.map (fun batch -> batch.Items |> List.map (fun item -> { item with Native = None; DependencyObservations = []; DependencyReadComplete = false; PlanObservation = None; Health = "Unknown"; Reads = 0; MutationAttempts = 0; MembershipPages = None; Projection = None; Gap = Some error })) |> Option.defaultValue []
              PopulationGap = Some error; Cleanup = "No resources, claims or background work created" }
        match validateBinding binding |> Result.bind (fun () -> artifactCheck binding) with
        | Error error -> failed error
        | Ok() ->
            match readProtectedBlob transport binding.PopulationRevision "docs/coordination/board-v2-import-manifest.json" |> Result.bind (fun (text, current, blob) -> population binding text |> Result.map (fun selected -> selected, current, blob)) with
            | Error error -> failed error
            | Ok(selected, protectedRevision, populationBlob) ->
                let items = selected |> List.map (fun row ->
                    let mutable native = None
                    let mutable dependencies = []
                    let mutable dependenciesComplete = false
                    let mutable plan = None
                    let mutable reads = 0
                    let mutable mutations = 0
                    let counted =
                        { new IGitHubTransport with
                            member _.Send request = reads <- reads + 1; transport.Send request
                            member _.SendMutation intent = mutations <- mutations + 1; transport.SendMutation intent
                            member _.RetryMutation effectId = mutations <- mutations + 1; transport.RetryMutation effectId }
                    let mutable sourceChecks = 0
                    let verifier: SourceVerifier = fun _ ->
                        let populationCurrent =
                            if sourceChecks = 0 then Ok()
                            else readProtectedBlob counted binding.PopulationRevision "docs/coordination/board-v2-import-manifest.json" |> Result.bind (fun (_, _, blob) ->
                                if blob = populationBlob then Ok() else Error(Malformed("reviewed population", "population changed before mutation")))
                        sourceChecks <- sourceChecks + 1
                        match populationCurrent with
                        | Error error -> Error error
                        | Ok() ->
                            let collection = collectSource counted binding row
                            native <- collection.Native
                            dependencies <- collection.Dependencies
                            dependenciesComplete <- collection.DependenciesComplete
                            plan <- collection.Plan
                            collection.Verification
                    let result = runOneShot verifier counted binding { Issue = row.Issue; ExpectedNodeId = row.NodeId }
                    let projection = match result with Ok report -> Some report | _ -> None
                    let historical = previous |> Option.bind (fun batch ->
                        if batch.ProjectId <> binding.ProjectId then None
                        else batch.Items |> List.tryFind (fun item -> item.Issue = row.Issue) |> Option.bind (fun item -> item.LastVerified))
                    { Issue = row.Issue; Native = native; DependencyObservations = dependencies; DependencyReadComplete = dependenciesComplete; PlanObservation = plan
                      Delivery = "Unknown: no source-delivery acceptance reader selected"
                      Publication = "Unknown: no publication acceptance reader selected"
                      NativeAcceptance = "Unknown: no operation acceptance reader selected"
                      Health = match result with Ok _ -> "Verified" | Error(Http(409, _)) -> "Stale" | _ -> "Unknown"
                      Reads = reads; MutationAttempts = mutations
                      MembershipPages = projection |> Option.map (fun report -> report.ProjectReads - (if report.Mutations = 0 then 3 else 4))
                      Projection = projection; Gap = match result with Error error -> Some error | _ -> None
                      LastVerified = projection |> Option.orElse historical })
                { ProjectId = binding.ProjectId; RecipeRevision = binding.RecipeRevision; PopulationRevision = binding.PopulationRevision; ImportRecipeRevision = binding.ImportRecipeRevision; ImportArtifactSha256 = binding.ImportArtifactSha256; ArtifactSha256 = binding.ArtifactSha256; NativeDispatch = []; ProtectedInputs = [ "docs/coordination/board-v2-import-manifest.json", protectedRevision, populationBlob ]; Selected = Some selected.Length
                  Attempted = items.Length; Verified = items |> List.filter (fun item -> item.Health = "Verified") |> List.length
                  Items = items; PopulationGap = None; Cleanup = "No resources, claims or background work created" }

    type InspectionItem =
        { Evidence: ItemReport; ExpectedNodeId: string; SourceCurrentness: string
          Planning: PlanningObservation option; PlanningGap: IoError option; Discrepancies: string list }

    type InspectionReport =
        { Binding: Binding; Evidence: BatchReport; Items: InspectionItem list }

    type IntegratorFacts =
        { OpenPullRequests: string list option; TouchSets: Map<string, string list> option; AvailableSlots: int option }

    type PlanningCandidate =
        { Issue: IssueRef; HumanStatus: string option; Track: string option; Roadmap: string option
          SourceCurrentness: string; Observation: string option; UnmetOrUnknown: string list; Integrator: IntegratorFacts }

    let private inspectionReads (transport: IGitHubTransport) (binding: Binding) =
        let paths = binding.SelectedIssues |> Map.values |> Seq.choose (fun value ->
            match parseIssue value with Ok issue -> Some $"repos/{issue.Owner}/{issue.Repository}/issues/{issue.Number}/dependencies/blocked_by" | _ -> None) |> Set.ofSeq
        { new IGitHubTransport with
            member _.Send request =
                match request.Method, request.Path, request.Body with
                | "POST", "graphql", Query(document, _) when request.Budget = GraphQl && request.Query.IsEmpty && request.IfNoneMatch.IsNone && document.StartsWith("query(", StringComparison.Ordinal) -> transport.Send request
                | "GET", path, NoBody when paths.Contains path && request.Budget = Rest && request.Query = [ "per_page", "50" ] && request.IfNoneMatch.IsNone ->
                    match box transport with
                    | :? ISinglePageGitHubTransport as single -> single.SendSingle request
                    | _ -> transport.Send request
                | _ -> Error(Malformed("read-only V2 inspection", "request is not a fixed canonical read"))
            member _.SendMutation _ = Error(Malformed("read-only V2 inspection", "mutation is unavailable"))
            member _.RetryMutation _ = Error(Malformed("read-only V2 inspection", "durable replay is unavailable")) }

    let createInspectionTransport binding token : IoResult<IGitHubTransport * IDisposable> =
        validateBinding binding |> Result.bind (fun () ->
            if String.IsNullOrWhiteSpace token then Error(Unauthorized "read-only V2 inspection token")
            else
                let transport = new HttpTransport("https://api.github.com", token)
                Ok(inspectionReads transport binding, transport :> IDisposable))

    let inspectFixed (transport: IGitHubTransport) (binding: Binding) (previous: BatchReport option) =
        let reads = inspectionReads transport binding
        let metadata items selected protectedInputs gap : BatchReport =
            { ProjectId = binding.ProjectId; RecipeRevision = binding.RecipeRevision; PopulationRevision = binding.PopulationRevision
              ImportRecipeRevision = binding.ImportRecipeRevision; ImportArtifactSha256 = binding.ImportArtifactSha256; ArtifactSha256 = binding.ArtifactSha256
              NativeDispatch = []; ProtectedInputs = protectedInputs; Selected = selected; Attempted = items |> List.length
              Verified = items |> List.filter (fun item -> item.Health = "Verified") |> List.length; Items = items
              PopulationGap = gap; Cleanup = "Read-only inspection; no project lock, claims, mutation, durable effect or background work" }
        let failed error = { Binding = binding; Evidence = metadata [] None [] (Some error); Items = [] }
        match validateBinding binding |> Result.bind (fun () -> artifactCheck binding) with
        | Error error -> failed error
        | Ok() ->
            match readProtectedBlob reads binding.PopulationRevision "docs/coordination/board-v2-import-manifest.json" |> Result.bind (fun (text, current, blob) -> population binding text |> Result.map (fun selected -> selected, current, blob)) with
            | Error error -> failed error
            | Ok(selected, current, blob) ->
                let items = selected |> List.map (fun row ->
                    let mutable calls = 0
                    let counted =
                        { new IGitHubTransport with
                            member _.Send request = calls <- calls + 1; reads.Send request
                            member _.SendMutation _ = Error(Malformed("read-only V2 inspection", "mutation is unavailable"))
                            member _.RetryMutation _ = Error(Malformed("read-only V2 inspection", "durable replay is unavailable")) }
                    let collection = collectSource counted binding row
                    let currentness, sourceGap =
                        match collection.Verification with
                        | Ok(Current _) -> "Current", None
                        | Ok(Stale _) -> "Stale", Some(Http(409, "native issue or dependency differs from admitted population"))
                        | Ok(Refused reason) -> "Unknown", Some(Http(403, reason))
                        | Error error -> "Unknown", Some error
                    let planning = readPlanning counted binding { Issue = row.Issue; ExpectedNodeId = row.NodeId }
                    let observed, planningGap = match planning with Ok value -> Some value, None | Error error -> None, Some error
                    let historical = previous |> Option.filter (fun batch -> batch.ProjectId = binding.ProjectId) |> Option.bind (fun batch ->
                        batch.Items |> List.tryFind (fun item -> item.Issue = row.Issue) |> Option.bind _.LastVerified)
                    let discrepancies =
                        [ if collection.Native |> Option.exists (fun native -> native.State = "CLOSED") then "Native issue is closed; remaining outcome acceptance is unverified"
                          match observed with
                          | Some value ->
                              if value.Roadmap <> Some row.Roadmap then "Human Roadmap differs from the admitted owning plan"
                              if value.Status = Some "Done" then "Human Status is Done; delivery/publication/native outcome acceptance is unverified"
                              if value.Observation <> Some "Verified" then "Observation is missing or not Verified; inspection retains its value"
                          | None -> "Current planning fields are unavailable" ]
                    let evidence =
                        { Issue = row.Issue; Native = collection.Native; DependencyObservations = collection.Dependencies; DependencyReadComplete = collection.DependenciesComplete; PlanObservation = collection.Plan
                          Delivery = "Unknown: no source-delivery acceptance reader selected"; Publication = "Unknown: no publication acceptance reader selected"; NativeAcceptance = "Unknown: no operation acceptance reader selected"
                          Health = if planningGap.IsSome then "Unknown" elif currentness = "Stale" then "Stale" elif currentness = "Current" && (observed |> Option.exists (fun value -> value.Observation = Some "Verified")) then "Verified" else "Unknown"
                          Reads = calls; MutationAttempts = 0; MembershipPages = observed |> Option.map _.MembershipPages
                          Projection = None; Gap = sourceGap; LastVerified = historical }
                    { Evidence = evidence; ExpectedNodeId = row.NodeId; SourceCurrentness = currentness; Planning = observed; PlanningGap = planningGap; Discrepancies = discrepancies })
                { Binding = binding; Evidence = metadata (items |> List.map _.Evidence) (Some selected.Length) [ "docs/coordination/board-v2-import-manifest.json", current, blob ] None; Items = items }

    let planningCandidates (report: InspectionReport) (facts: IntegratorFacts) =
        if report.Evidence.Selected.IsNone || report.Evidence.PopulationGap.IsSome then []
        else report.Items |> List.map (fun item ->
            { Issue = item.Evidence.Issue; HumanStatus = item.Planning |> Option.bind _.Status; Track = item.Planning |> Option.bind _.Track; Roadmap = item.Planning |> Option.bind _.Roadmap
              SourceCurrentness = item.SourceCurrentness; Observation = item.Planning |> Option.bind _.Observation; Integrator = facts
              UnmetOrUnknown =
                  item.Discrepancies @
                  [ if item.SourceCurrentness <> "Current" then yield "Current source/native coverage is unavailable or stale"
                    if item.Evidence.Gap.IsSome || item.PlanningGap.IsSome then yield "One or more selected reads failed"
                    yield item.Evidence.Delivery; yield item.Evidence.Publication; yield item.Evidence.NativeAcceptance
                    if facts.OpenPullRequests.IsNone then yield "Open PR facts are unknown"
                    if facts.TouchSets.IsNone then yield "Touch-set facts are unknown"
                    if facts.AvailableSlots.IsNone then yield "Current capacity is unknown"
                    yield "Intake authorization and scheduling selection remain the current integrator's responsibility" ] })
