namespace FS.GG.Coord.GitHub

/// Fixed read-only composition. Inputs select an immutable reviewed binding, never executable code.
module V2ProjectionSource =
    open System
    open System.Text.Json
    open Errors
    open Transport
    open V2Projection

    type NativeObservation =
        { Issue: IssueRef; NodeId: string; UpdatedAt: string; State: string }

    type ItemReport =
        { Issue: IssueRef
          Native: NativeObservation option
          DependencyObservations: NativeObservation list
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
        let document = "query($owner: String!, $repo: String!, $number: Int!) { repository(owner: $owner, name: $repo) { nameWithOwner issue(number: $number) { id number updatedAt state repository { nameWithOwner } } } rateLimit { cost remaining } }"
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
                           && (state = "OPEN" || state = "CLOSED") ->
                Ok { Issue = issue; NodeId = nodeId; UpdatedAt = updated; State = state }
            | _ -> Error(Malformed(subject, "native identity or revision is unreadable/mismatched")))

    let private readProtectedBlob (transport: IGitHubTransport) (revision: string) (path: string) =
        let subject = $"protected recipe {revision}:{path}"
        if revision.Length <> 40 || revision |> Seq.exists (fun character -> not (Uri.IsHexDigit character)) then
            Error(Malformed(subject, "reviewed recipe must be an immutable Git commit SHA"))
        elif not (path.StartsWith("docs/", StringComparison.Ordinal)) || path.Contains("..") || path.Contains(":") || path.Contains("\\") then
            Error(Malformed(subject, "reviewed source path must remain within canonical docs"))
        else
            let document = "query($expression: String!) { repository(owner: \"FS-GG\", name: \".github\") { nameWithOwner ref(qualifiedName: \"refs/heads/main\") { target { oid } } object(expression: $expression) { ... on Blob { text } } } rateLimit { cost remaining } }"
            GraphQl.read transport (query document [ "expression", VString $"{revision}:{path}" ] subject) (fun data ->
                let repository = data.GetProperty "repository"
                let text = repository.GetProperty("object").GetProperty("text").GetString()
                if repository.GetProperty("nameWithOwner").GetString() <> "FS-GG/.github"
                   || repository.GetProperty("ref").GetProperty("target").GetProperty("oid").GetString() <> revision
                   || String.IsNullOrWhiteSpace text then
                    Error(Malformed(subject, "canonical source blob is unavailable"))
                else Ok text)

    let private parseIssue (value: string) =
        let hash = value.LastIndexOf '#'
        let slash = value.IndexOf '/'
        let mutable number = 0
        if slash <= 0 || hash <= slash + 1 || not (Int32.TryParse(value.Substring(hash + 1), &number)) || number <= 0 then
            Error(Malformed("reviewed native issue", "canonical OWNER/REPO#NUMBER required"))
        else Ok { Owner = value.Substring(0, slash); Repository = value.Substring(slash + 1, hash - slash - 1); Number = number }

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
               || admission.GetProperty("recipeRevision").GetString() <> binding.RecipeRevision
               || admission.GetProperty("artifactSha256").GetString() <> binding.ArtifactSha256
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
                                if dependencies.Length > 50 || row.GetProperty("observedState").GetString() <> "open" || not (binding.Repositories.Contains repo) || String.IsNullOrWhiteSpace nodeId || String.IsNullOrWhiteSpace updated || String.IsNullOrWhiteSpace roadmap then
                                    Error(Malformed("reviewed population", "missing canonical identity, revision, roadmap or repository scope"))
                                else Ok { Issue = issue; NodeId = nodeId; UpdatedAt = updated; Roadmap = roadmap; Dependencies = dependencies |> List.choose (function Ok value -> Some value | _ -> None) })
                    match parsed |> List.tryPick (function Error error -> Some error | _ -> None) with
                    | Some error -> Error error
                    | None ->
                        let selected = parsed |> List.choose (function Ok value -> Some value | _ -> None)
                        if selected |> List.map (fun row -> row.NodeId) |> Set.ofList |> Set.count <> selected.Length then
                            Error(Malformed("reviewed population", "duplicate native issue identities"))
                        else Ok selected
        with
        | :? JsonException
        | :? Collections.Generic.KeyNotFoundException
        | :? InvalidOperationException -> Error(Malformed("reviewed population", "manifest is incomplete or nonexecutable"))

    /// Previous reports preserve historical successful observations only; they never license writes.
    let runFixed (transport: IGitHubTransport) (binding: Binding) (previous: BatchReport option) =
        let failed error =
            { ProjectId = binding.ProjectId; RecipeRevision = binding.RecipeRevision; PopulationRevision = binding.PopulationRevision; Selected = None; Attempted = 0; Verified = 0
              Items = previous |> Option.filter (fun batch -> batch.ProjectId = binding.ProjectId) |> Option.map (fun batch -> batch.Items |> List.map (fun item -> { item with Native = None; DependencyObservations = []; Health = "Unknown"; Reads = 0; MutationAttempts = 0; MembershipPages = None; Projection = None; Gap = Some error })) |> Option.defaultValue []
              PopulationGap = Some error; Cleanup = "No resources, claims or background work created" }
        let artifactCheck =
            try
                let location = typeof<Binding>.Assembly.Location
                if String.IsNullOrWhiteSpace location then Error(Malformed("reviewed executable", "loaded assembly has no verifiable file identity"))
                else
                    use stream = System.IO.File.OpenRead location
                    let actual = System.Security.Cryptography.SHA256.HashData stream |> Convert.ToHexStringLower
                    if not (String.Equals(actual, binding.ArtifactSha256, StringComparison.OrdinalIgnoreCase)) then Error(Malformed("reviewed executable", "loaded assembly bytes differ from selected artifact SHA-256"))
                    else Ok()
            with :? System.IO.IOException -> Error(Malformed("reviewed executable", "loaded assembly bytes unavailable"))
        match validateBinding binding |> Result.bind (fun () -> artifactCheck) with
        | Error error -> failed error
        | Ok() ->
            match readProtectedBlob transport binding.PopulationRevision "docs/coordination/board-v2-import-manifest.json" |> Result.bind (population binding) with
            | Error error -> failed error
            | Ok selected ->
                let items = selected |> List.map (fun row ->
                    let mutable native = None
                    let mutable dependencies = []
                    let mutable reads = 0
                    let mutable mutations = 0
                    let counted =
                        { new IGitHubTransport with
                            member _.Send request = reads <- reads + 1; transport.Send request
                            member _.SendMutation intent = mutations <- mutations + 1; transport.SendMutation intent
                            member _.RetryMutation effectId = mutations <- mutations + 1; transport.RetryMutation effectId }
                    let verifier: SourceVerifier = fun _ ->
                        match readIssue counted row.Issue with
                        | Error error -> Error error
                        | Ok observation ->
                            native <- Some observation
                            if observation.NodeId <> row.NodeId then Ok(Refused "native issue node identity changed")
                            elif observation.UpdatedAt <> row.UpdatedAt then Ok(Stale None)
                            else
                                let reads = row.Dependencies |> List.map (readIssue counted)
                                dependencies <- reads |> List.choose (function Ok value -> Some value | _ -> None)
                                match reads |> List.tryPick (function Error error -> Some error | _ -> None) with
                                | Some error -> Error error
                                | None ->
                                    match readProtectedBlob counted binding.PopulationRevision row.Roadmap with
                                    | Error error -> Error error
                                    | Ok _ ->
                                        let dependencyRevision = dependencies |> List.map (fun dependency -> $"{dependency.NodeId}@{dependency.UpdatedAt}:{dependency.State}") |> String.concat ";"
                                        Ok(Current $"{observation.NodeId}@{observation.UpdatedAt};dependencies={dependencyRevision};recipe={binding.RecipeRevision}")
                    let result = runOneShot verifier counted binding { Issue = row.Issue; ExpectedNodeId = row.NodeId }
                    let projection = match result with Ok report -> Some report | _ -> None
                    let historical = previous |> Option.bind (fun batch ->
                        if batch.ProjectId <> binding.ProjectId then None
                        else batch.Items |> List.tryFind (fun item -> item.Issue = row.Issue) |> Option.bind (fun item -> item.LastVerified))
                    { Issue = row.Issue; Native = native; DependencyObservations = dependencies
                      Delivery = "Unknown: no source-delivery acceptance reader selected"
                      Publication = "Unknown: no publication acceptance reader selected"
                      NativeAcceptance = "Unknown: no operation acceptance reader selected"
                      Health = match result with Ok _ -> "Verified" | Error(Http(409, _)) -> "Stale" | _ -> "Unknown"
                      Reads = reads; MutationAttempts = mutations
                      MembershipPages = projection |> Option.map (fun report -> report.ProjectReads - (if report.Mutations = 0 then 3 else 4))
                      Projection = projection; Gap = match result with Error error -> Some error | _ -> None
                      LastVerified = projection |> Option.orElse historical })
                { ProjectId = binding.ProjectId; RecipeRevision = binding.RecipeRevision; PopulationRevision = binding.PopulationRevision; Selected = Some selected.Length
                  Attempted = items.Length; Verified = items |> List.filter (fun item -> item.Health = "Verified") |> List.length
                  Items = items; PopulationGap = None; Cleanup = "No resources, claims or background work created" }
