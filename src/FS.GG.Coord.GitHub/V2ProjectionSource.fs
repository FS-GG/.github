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

    let private readIssueDetails (transport: IGitHubTransport) (issue: IssueRef) =
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
                Ok({ Issue = issue; NodeId = nodeId; UpdatedAt = updated; State = state; Url = native.GetProperty("url").GetString()
                     BodySha256 = native.GetProperty("body").GetString() |> System.Text.Encoding.UTF8.GetBytes |> System.Security.Cryptography.SHA256.HashData |> Convert.ToHexStringLower }, native.GetProperty("body").GetString())
            | _ -> Error(Malformed(subject, "native identity or revision is unreadable/mismatched")))

    let private readIssue transport issue = readIssueDetails transport issue |> Result.map fst

    // Closed maintained six-field headings; prose remains observation, never authority.
    let private recordFields (body: string) =
        let lines = FS.GG.Coord.Markdown.unfenced body |> List.toArray
        let names = Set.ofList [ "Owner"; "Done when"; "Current deliverable"; "Next action"; "Blocked on"; "Acceptance evidence" ]
        let headings =
            lines |> Array.indexed |> Array.choose (fun (index, line) ->
                let value = line.Trim()
                if value.StartsWith("##", StringComparison.Ordinal) then Some(index, value) else None)
        let parsed = headings |> Array.map (fun (_, value) -> if value.StartsWith("## ", StringComparison.Ordinal) then value.Substring(3).Trim() else "")
        if parsed.Length <> names.Count || Set.ofArray parsed <> names then None
        else
            parsed |> Array.mapi (fun index name ->
                let start, _ = headings[index]
                let finish = if index + 1 < headings.Length then fst headings[index + 1] else lines.Length
                name, (lines[(start + 1)..(finish - 1)] |> String.concat "\n" |> _.Trim()))
            |> Map.ofArray |> Some

    let private ownerField (body: string) =
        match recordFields body with
        | Some fields -> Map.tryFind "Owner" fields |> Option.filter (String.IsNullOrWhiteSpace >> not)
        | None when body.Contains("##", StringComparison.Ordinal) -> None
        | None ->
            let values =
                FS.GG.Coord.Markdown.unfenced body |> List.choose (fun line ->
                    let text = line.Trim().TrimStart('*').Trim()
                    if text.StartsWith("Owner:", StringComparison.Ordinal) then Some(text.Substring(6).Trim().Trim('*').Trim()) else None)
            match values with [ owner ] when not (String.IsNullOrWhiteSpace owner) -> Some owner | _ -> None

    let private deliverableField (binding: Binding) (body: string) =
        match recordFields body |> Option.bind (Map.tryFind "Current deliverable") with
        | None -> [], Some "Native Current deliverable field is missing or malformed; absence of closing references does not establish no existing work"
        | Some "None" -> [], None
        | Some value ->
            let urls =
                System.Text.RegularExpressions.Regex.Matches(value, "https?://[^\\s<>()\\[\\]]+")
                |> Seq.cast<System.Text.RegularExpressions.Match> |> Seq.map _.Value |> Seq.distinct |> Seq.toList
            let canonical (url: string) =
                let mutable uri = Unchecked.defaultof<Uri>
                if not (url.StartsWith("https://github.com/", StringComparison.Ordinal)) || not (Uri.TryCreate(url, UriKind.Absolute, &uri)) then None
                else
                    let parts = uri.AbsolutePath.Trim('/').Split('/')
                    let mutable number = 0
                    if uri.Scheme <> "https" || uri.Host <> "github.com" || not uri.IsDefaultPort || uri.UserInfo <> "" || uri.Query <> "" || uri.Fragment <> ""
                       || parts.Length <> 4 || parts[2] <> "pull" || not (Int32.TryParse(parts[3], &number)) || number <= 0
                       || url <> $"https://github.com/{parts[0]}/{parts[1]}/pull/{number}" then None
                    else
                        binding.Repositories |> Seq.tryFind (fun repository -> String.Equals(repository, parts[0] + "/" + parts[1], StringComparison.OrdinalIgnoreCase))
                        |> Option.map (fun repository -> $"https://github.com/{repository}/pull/{number}")
            let normalized = urls |> List.choose canonical |> List.distinct
            if urls.IsEmpty || (urls |> List.exists (canonical >> Option.isNone)) then
                [], Some "Native Current deliverable must explicitly say None or contain canonical PR links in selected repositories"
            else normalized, None

    let private sourceRepository (binding: Binding) =
        if binding.BindingVersion = 3 then Set.minElement binding.Repositories else "FS-GG/.github"

    let private populationPath = "docs/coordination/board-v2-import-manifest.json"

    let private readProtectedBlob (transport: IGitHubTransport) (binding: Binding) (revision: string) (path: string) =
        let subject = $"protected recipe {revision}:{path}"
        if revision.Length <> 40 || revision |> Seq.exists (fun character -> not (Uri.IsHexDigit character)) then
            Error(Malformed(subject, "reviewed recipe must be an immutable Git commit SHA"))
        elif not (path.StartsWith("docs/", StringComparison.Ordinal)) || path.Contains("..") || path.Contains(":") || path.Contains("\\") then
            Error(Malformed(subject, "reviewed source path must remain within canonical docs"))
        else
            let expectedRepository = sourceRepository binding
            let document, ownerVariables =
                if binding.BindingVersion = 3 then
                    "query($owner: String!, $repo: String!, $expression: String!, $currentExpression: String!) { repository(owner: $owner, name: $repo) { nameWithOwner ref(qualifiedName: \"refs/heads/main\") { target { oid } } object(expression: $expression) { ... on Blob { oid text } } current: object(expression: $currentExpression) { ... on Blob { oid text } } } rateLimit { cost remaining } }",
                    [ "owner", VString binding.Owner; "repo", VString (expectedRepository.Split('/')[1]) ]
                else
                    "query($expression: String!, $currentExpression: String!) { repository(owner: \"FS-GG\", name: \".github\") { nameWithOwner ref(qualifiedName: \"refs/heads/main\") { target { oid } } object(expression: $expression) { ... on Blob { oid text } } current: object(expression: $currentExpression) { ... on Blob { oid text } } } rateLimit { cost remaining } }", []
            GraphQl.read transport (query document (ownerVariables @ [ "expression", VString (if binding.BindingVersion = 4 && path <> populationPath then $"main:{path}" else $"{revision}:{path}"); "currentExpression", VString $"main:{path}" ]) subject) (fun data ->
                let repository = data.GetProperty "repository"
                let text = repository.GetProperty("object").GetProperty("text").GetString()
                if repository.GetProperty("nameWithOwner").GetString() <> expectedRepository
                   || repository.GetProperty("object").GetProperty("oid").GetString() <> repository.GetProperty("current").GetProperty("oid").GetString()
                   || text <> repository.GetProperty("current").GetProperty("text").GetString()
                   || (let oid = repository.GetProperty("object").GetProperty("oid").GetString() in String.IsNullOrWhiteSpace oid || oid.Length <> 40 || oid |> Seq.exists (Uri.IsHexDigit >> not))
                   || (let oid = repository.GetProperty("ref").GetProperty("target").GetProperty("oid").GetString() in String.IsNullOrWhiteSpace oid || oid.Length <> 40 || oid |> Seq.exists (Uri.IsHexDigit >> not))
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

    let private readBlockedBy (transport: IGitHubTransport) (binding: Binding) (issue: IssueRef) =
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
                        if uri.Scheme <> "https" || uri.Host <> "github.com" || uri.UserInfo <> "" || uri.Query <> "" || uri.Fragment <> "" || parts.Length <> 4 || parts[0] <> (if binding.BindingVersion = 3 then binding.Owner else "FS-GG") || parts[2] <> "issues" || (node.TryGetProperty("pull_request") |> fst)
                           || (binding.BindingVersion = 3 && not (binding.Repositories.Contains $"{parts[0]}/{parts[1]}")) then
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
        if binding.BindingVersion = 3 && not (path.StartsWith("docs/", StringComparison.Ordinal)) then
            let prefix = $"https://github.com/{sourceRepository binding}/issues/"
            let mutable number = 0
            if not (path.StartsWith(prefix, StringComparison.Ordinal)) || not (Int32.TryParse(path.Substring(prefix.Length), &number)) || number <= 0 || path <> prefix + string number then
                Error(Malformed("owning plan", "product plan must be canonical docs or a native issue in the selected repository"))
            else
                let repository = (sourceRepository binding).Split('/')[1]
                readIssue transport { Owner = binding.Owner; Repository = repository; Number = number } |> Result.bind (fun native ->
                    let emptyDigest = System.Security.Cryptography.SHA256.HashData(Array.empty<byte>) |> Convert.ToHexStringLower
                    if native.BodySha256 = emptyDigest then Error(Malformed("owning plan", "selected native plan body is empty"))
                    else Ok(Some native, $"{native.NodeId}@{native.UpdatedAt}:{native.Url}:{native.BodySha256}"))
        elif path = "https://github.com/FS-GG/.github/issues/3008" && binding.Repositories.Contains "FS-GG/.github" then
            readIssue transport { Owner = "FS-GG"; Repository = ".github"; Number = 3008 } |> Result.bind (fun native ->
                let emptyDigest = System.Security.Cryptography.SHA256.HashData(Array.empty<byte>) |> Convert.ToHexStringLower
                if native.Url <> path || native.BodySha256 = emptyDigest then Error(Malformed("programme issue", "native URL/body is unreadable or mismatched"))
                else Ok(Some native, $"{native.NodeId}@{native.UpdatedAt}:{native.Url}:{native.BodySha256}"))
        elif path.StartsWith("docs/", StringComparison.Ordinal) then
            readProtectedBlob transport binding binding.PopulationRevision path |> Result.map (fun (_, revision, blob) -> None, $"{path}@{revision}:{blob}")
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
            elif binding.BindingVersion = 4 then
                Error(Malformed("reviewed population", "programme binding requires enrollment/v2, never pilot fallback"))
            else
                let rows = root.GetProperty("items").EnumerateArray() |> Seq.filter (fun row -> row.GetProperty("pilot").GetBoolean() && (row.GetProperty("decision").GetString() = "import" || row.GetProperty("decision").GetString() = "follow-up") && row.GetProperty("adjudication").GetString() = "verified-remaining") |> Seq.toList
                if rows.Length < (if binding.BindingVersion = 3 then 1 else 3) || rows.Length > 5 then Error(Malformed("reviewed population", "admitted population is outside the selected bounded cohort"))
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

    let private programmePopulation (binding: Binding) (text: string) =
        try
            use document = JsonDocument.Parse text
            let root = document.RootElement
            let closed (names: string list) (value: JsonElement) =
                let actual = value.EnumerateObject() |> Seq.map _.Name |> Seq.toList
                if actual.Length <> names.Length || Set.ofList actual <> Set.ofList names then invalidArg "enrollment" "closed schema required"
            closed [ "schema"; "target"; "binding"; "items" ] root
            if root.GetProperty("schema").GetString() <> "fsgg.coordination-board-v2-enrollment/v2" then invalidArg "schema" "programme enrollment/v2 required"
            let target = root.GetProperty "target"
            closed [ "owner"; "organizationId"; "projectId"; "projectNumber"; "schemaVersion" ] target
            let admission = root.GetProperty "binding"
            closed [ "recipeRevision"; "artifactSha256"; "repositories" ] admission
            let repositories = admission.GetProperty("repositories").EnumerateArray() |> Seq.map _.GetString() |> Seq.toList
            if target.GetProperty("owner").GetString() <> binding.Owner || target.GetProperty("organizationId").GetString() <> binding.OrganizationId
               || target.GetProperty("projectId").GetString() <> binding.ProjectId || target.GetProperty("projectNumber").GetInt32() <> binding.ProjectNumber
               || target.GetProperty("schemaVersion").GetInt32() <> binding.SchemaVersion
               || admission.GetProperty("recipeRevision").GetString() <> binding.ImportRecipeRevision
               || admission.GetProperty("artifactSha256").GetString() <> binding.ImportArtifactSha256
               || Set.ofList repositories <> binding.Repositories || repositories.Length <> binding.Repositories.Count then
                invalidArg "enrollment" "target/provenance/repository mismatch"
            let rows = root.GetProperty("items").EnumerateArray() |> Seq.map (fun row ->
                closed [ "issue"; "nodeId"; "roadmap" ] row
                let issue = parseIssue (row.GetProperty("issue").GetString()) |> Result.defaultWith (Errors.explain >> failwith)
                let node = row.GetProperty("nodeId").GetString()
                let roadmap = row.GetProperty("roadmap").GetString()
                if Map.tryFind node binding.SelectedIssues <> Some $"{issue.Owner}/{issue.Repository}#{issue.Number}" || String.IsNullOrWhiteSpace roadmap then
                    invalidArg "enrollment" "issue identity/owning plan differs"
                { Issue = issue; NodeId = node; UpdatedAt = ""; Roadmap = roadmap; Dependencies = [] }) |> Seq.toList
            if rows.Length <> binding.SelectedIssues.Count || (rows |> List.map _.NodeId |> Set.ofList |> Set.count) <> rows.Length then
                invalidArg "enrollment" "exact selected population required"
            Ok rows
        with error -> Error(Malformed("programme enrollment", error.Message))

    let private selectedPopulation binding text =
        if binding.BindingVersion = 4 then programmePopulation binding text else population binding text

    type private SourceCollection =
        { Verification: IoResult<SourceVerification>; Native: NativeObservation option
          Dependencies: NativeObservation list; DependenciesComplete: bool; Plan: NativeObservation option; Owner: string option
          Deliverables: string list; DeliverableGap: string option }

    let private collectSource (transport: IGitHubTransport) (binding: Binding) (row: Selected) =
        let mutable native = None
        let mutable dependencies = []
        let mutable dependenciesComplete = false
        let mutable plan = None
        let mutable owner = None
        let mutable deliverables = []
        let mutable deliverableGap = Some "Native issue body was not collected"
        let verification =
            match readIssueDetails transport row.Issue with
            | Error error -> Error error
            | Ok(observation, body) ->
                owner <- ownerField body
                let references, gap = deliverableField binding body
                deliverables <- references
                deliverableGap <- gap
                native <- Some observation
                if observation.NodeId <> row.NodeId then Ok(Refused "native issue node identity changed")
                elif binding.BindingVersion <> 4 && (observation.UpdatedAt <> row.UpdatedAt || observation.State <> "OPEN") then Ok(Stale None)
                else
                    match readBlockedBy transport binding row.Issue with
                    | Error error -> Error error
                    | Ok observed ->
                        dependencies <- observed
                        dependenciesComplete <- true
                        let actual = observed |> List.map _.Issue |> Set.ofList
                        if binding.BindingVersion <> 4 && actual <> Set.ofList row.Dependencies then Ok(Stale None)
                        else
                            match readPlan transport binding row.Roadmap with
                            | Error error -> Error error
                            | Ok(planObservation, planRevision) ->
                                plan <- planObservation
                                let dependencyRevision = dependencies |> List.map (fun dependency -> $"{dependency.NodeId}@{dependency.UpdatedAt}:{dependency.State}") |> List.sort |> String.concat ";"
                                Ok(Current $"{observation.NodeId}@{observation.UpdatedAt}:{observation.BodySha256};dependencies=complete:{dependencyRevision};plan={planRevision};recipe={binding.RecipeRevision}")
        { Verification = verification; Native = native; Dependencies = dependencies; DependenciesComplete = dependenciesComplete; Plan = plan; Owner = owner; Deliverables = deliverables; DeliverableGap = deliverableGap }

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
            match readProtectedBlob transport binding binding.PopulationRevision populationPath |> Result.bind (fun (text, current, blob) -> selectedPopulation binding text |> Result.map (fun selected -> selected, current, blob)) with
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
                            else readProtectedBlob counted binding binding.PopulationRevision populationPath |> Result.bind (fun (_, _, blob) ->
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
                { ProjectId = binding.ProjectId; RecipeRevision = binding.RecipeRevision; PopulationRevision = binding.PopulationRevision; ImportRecipeRevision = binding.ImportRecipeRevision; ImportArtifactSha256 = binding.ImportArtifactSha256; ArtifactSha256 = binding.ArtifactSha256; NativeDispatch = []; ProtectedInputs = [ (if binding.BindingVersion = 3 then sourceRepository binding + ":" + populationPath else populationPath), protectedRevision, populationBlob ]; Selected = Some selected.Length
                  Attempted = items.Length; Verified = items |> List.filter (fun item -> item.Health = "Verified") |> List.length
                  Items = items; PopulationGap = None; Cleanup = "No resources, claims or background work created" }

    // Native head-bound observations; none of these facts establishes deployment acceptance.
    type PullRequestFacts =
        { Id: string; Repository: string; Number: int; Url: string; State: string; Head: string; UpdatedAt: string
          Author: string option; Issues: string list; Files: string list; ScopeCurrent: bool; ReviewDecision: string option
          Reviews: string list; Checks: string list; Gaps: string list }

    type NativeIntegratorFacts =
        { PullRequests: PullRequestFacts list; RepositoryGaps: Map<string, string>; ObservedAt: DateTimeOffset }

    // Trusted caller inputs for one current slice, not a parent outcome reservation or authority.
    type Action = Source | Activation
    type Operation = ObserveExisting | Implement | ReviewWork | QualifyWork | ActivateWork
    type AttributedObservation =
        { Supplier: string; ObservedAt: DateTimeOffset; ValidUntil: DateTimeOffset; Evidence: string list }
    type SliceJoin =
        { NativeId: string; UpdatedAt: string; BodySha256: string; SourceRevision: string }
    type Prerequisite = { Action: Action; Reference: string; Satisfied: bool option; SupplyingOwner: string }
    type CurrentSlice =
        { Id: string; Attempt: string; Operation: Operation; Issue: IssueRef
          AccountableOwner: string; Owner: string; Repository: string; Paths: string list; Action: Action
          Join: SliceJoin; Observation: AttributedObservation; PullRequest: string option; PullRequestId: string option
          PullRequestUpdatedAt: string option; Head: string option
          Prerequisites: Prerequisite list; PrerequisitesComplete: bool; IntakeAuthorized: bool option }
    type Reservation =
        { SliceId: string; Attempt: string; Operation: Operation; Owner: string; Repository: string; Paths: string list
          PullRequest: string option; Head: string option; CustodyReference: string }
    type SelectionContext =
        { Invocation: string; ProjectId: string; SchemaVersion: int; RecipeRevision: string; ArtifactSha256: string
          PopulationRevision: string; EnrollmentBlob: string; InputSha256: string
          Slices: CurrentSlice list; ImplementationSlots: int option; ReviewSlots: int option; HostedCiSlots: int option
          CapacityMeaning: string; CapacityObservation: AttributedObservation option
          Reservations: Reservation list option; ReservationObservation: AttributedObservation option
          ObservedAt: DateTimeOffset; ValidUntil: DateTimeOffset; Evidence: string }
    type Selection =
        { SliceId: string option; Attempt: string option; Operation: Operation option
          Issue: IssueRef; Action: Action; Decision: string; Owner: string option; PullRequest: string option
          Head: string option; Reasons: string list; Evidence: string list }

    type NativeIntakeObservation =
        { Admission: BoardIntake.Admission option; Allowed: bool option; ObservedAt: DateTimeOffset; Gap: IoError option }

    type InspectionItem =
        { Evidence: ItemReport; ExpectedNodeId: string; SourceCurrentness: string
          Planning: PlanningObservation option; PlanningGap: IoError option; Discrepancies: string list; Owner: string option; SourceRevision: string option
          Deliverables: string list; DeliverableGap: string option; Intake: NativeIntakeObservation }

    type InspectionReport =
        { Binding: Binding; Evidence: BatchReport; Items: InspectionItem list
          Memberships: Membership list; MembershipGap: IoError option; NativeIntegrator: NativeIntegratorFacts }

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
                | "POST", "graphql", Query(document, _) when request.Budget = GraphQl && request.Query.IsEmpty && request.IfNoneMatch.IsNone && (document.StartsWith("query(", StringComparison.Ordinal) || document.StartsWith("query IntakeIdentity(", StringComparison.Ordinal)) -> transport.Send request
                | "GET", path, NoBody when paths.Contains path && request.Budget = Rest && request.Query = [ "per_page", "50" ] && request.IfNoneMatch.IsNone ->
                    match box transport with
                    | :? ISinglePageGitHubTransport as single -> single.SendSingle request
                    | _ -> transport.Send request
                | "GET", path, NoBody when request.Budget = Rest && request.Query.IsEmpty && request.IfNoneMatch.IsNone ->
                    let parts = path.Split('/')
                    let safeLogin (login: string) =
                        not (String.IsNullOrWhiteSpace login) && login.Length <= 39
                        && (login |> Seq.forall (fun character -> Char.IsAsciiLetterOrDigit character || character = '-'))
                    if parts.Length = 6 && parts[0] = "repos" && binding.Repositories.Contains(parts[1] + "/" + parts[2])
                       && parts[3] = "collaborators" && safeLogin parts[4] && parts[5] = "permission" then transport.Send request
                    else Error(Malformed("read-only V2 inspection", "permission read escaped selected repository/canonical login scope"))
                | _ -> Error(Malformed("read-only V2 inspection", "request is not a fixed canonical read"))
            member _.SendMutation _ = Error(Malformed("read-only V2 inspection", "mutation is unavailable"))
            member _.RetryMutation _ = Error(Malformed("read-only V2 inspection", "durable replay is unavailable")) }

    let createInspectionTransport binding token : IoResult<IGitHubTransport * IDisposable> =
        validateBinding binding |> Result.bind (fun () ->
            if String.IsNullOrWhiteSpace token then Error(Unauthorized "read-only V2 inspection token")
            else
                let transport = new HttpTransport("https://api.github.com", token)
                Ok(inspectionReads transport binding, transport :> IDisposable))

    let private nativeIntake (gate: BoardIntake.Gate) (observedAt: DateTimeOffset) (binding: Binding) (expected: NativeObservation option) =
        let absent = { Admission = None; Allowed = None; ObservedAt = DateTimeOffset.MinValue; Gap = None }
        if binding.BindingVersion <> 4 || expected.IsNone then absent
        else
            let native = expected.Value
            let issue = native.Issue
            match gate.Authorize(issue.Owner, issue.Repository, issue.Number) with
            | Ok admission when admission.IssueId = native.NodeId && admission.UpdatedAt = native.UpdatedAt ->
                { Admission = Some admission; Allowed = Some true; ObservedAt = observedAt; Gap = None }
            | Ok _ ->
                { Admission = None; Allowed = None; ObservedAt = observedAt
                  Gap = Some(Malformed("native intake", "native issue ID/revision changed since body read")) }
            | Error error -> { Admission = None; Allowed = None; ObservedAt = observedAt; Gap = Some error }

    let private nativeIntegrator (transport: IGitHubTransport) (binding: Binding) (references: string list) =
        // Charge freshness from the earliest determining inventory read, never collection completion.
        let observedAt = DateTimeOffset.UtcNow
        let mutable repositoryGaps = Map.empty
        let mutable all = []
        let sha (value: string) = not (String.IsNullOrWhiteSpace value) && value.Length = 40 && value |> Seq.forall Uri.IsHexDigit
        for repository in binding.Repositories do
            let parts = repository.Split('/')
            let subject = repository + " open PR inventory"
            let fetch after =
                let document = "query($owner: String!, $repo: String!, $after: String) { repository(owner: $owner, name: $repo) { nameWithOwner pullRequests(first: 50, after: $after, states: OPEN) { totalCount nodes { id number url headRefOid updatedAt author { login } reviewDecision } pageInfo { hasNextPage endCursor } } } rateLimit { cost remaining } }"
                let variables = [ "owner", VString parts[0]; "repo", VString parts[1] ] @ (after |> Option.map (fun cursor -> [ "after", VString cursor ]) |> Option.defaultValue [])
                GraphQl.read transport (query document variables subject) (fun data ->
                    let native = data.GetProperty "repository"
                    if native.GetProperty("nameWithOwner").GetString() <> repository then Error(Malformed(subject, "repository differs"))
                    else
                        let decode (node: JsonElement) =
                            let id = node.GetProperty("id").GetString()
                            let number = node.GetProperty("number").GetInt32()
                            let head = node.GetProperty("headRefOid").GetString()
                            let url = node.GetProperty("url").GetString()
                            let updated = node.GetProperty("updatedAt").GetString()
                            if String.IsNullOrWhiteSpace id || number <= 0 || not (sha head) || not (fst (DateTimeOffset.TryParse updated)) || url <> $"https://github.com/{repository}/pull/{number}" then
                                Error(Malformed(subject, "PR identity/head/revision differs"))
                            else
                                let optional (name: string) (value: JsonElement) = let property = value.GetProperty name in if property.ValueKind = JsonValueKind.Null then None else Some(property.GetString())
                                let author = node.GetProperty "author"
                                Ok { Id = id; Repository = repository; Number = number; Url = url; State = "OPEN"; Head = head; UpdatedAt = updated
                                     Author = if author.ValueKind = JsonValueKind.Null then None else optional "login" author
                                     ReviewDecision = optional "reviewDecision" node; Issues = []; Files = []; ScopeCurrent = false; Reviews = []; Checks = []; Gaps = [] }
                        GraphQl.page subject "open PRs" (fun (value: PullRequestFacts) -> value.Id) decode (native.GetProperty "pullRequests"))
            match GraphQl.drain subject "open PRs" { MaxPages = 10; MaxItems = 500 } fetch with
            | Error error -> repositoryGaps <- repositoryGaps.Add(repository, Errors.explain error)
            | Ok pulls ->
                for pull in pulls do
                    let mutable gaps = []
                    let mutable scopeCurrent = true
                    let connection field selection (decode: JsonElement -> IoResult<'a>) (key: 'a -> string) =
                        let fetch after =
                            let document = "query($id: ID!, $after: String) { node(id: $id) { ... on PullRequest { id number state headRefOid updatedAt repository { nameWithOwner } " + field + "(first: 50, after: $after) { totalCount nodes { " + selection + " } pageInfo { hasNextPage endCursor } } } } rateLimit { cost remaining } }"
                            let variables = [ "id", VId pull.Id ] @ (after |> Option.map (fun cursor -> [ "after", VString cursor ]) |> Option.defaultValue [])
                            GraphQl.read transport (query document variables (pull.Url + " " + field)) (fun data ->
                                let node = data.GetProperty "node"
                                if node.GetProperty("id").GetString() <> pull.Id || node.GetProperty("number").GetInt32() <> pull.Number
                                   || node.GetProperty("repository").GetProperty("nameWithOwner").GetString() <> repository
                                   || node.GetProperty("state").GetString() <> "OPEN" || node.GetProperty("headRefOid").GetString() <> pull.Head
                                   || node.GetProperty("updatedAt").GetString() <> pull.UpdatedAt then
                                    scopeCurrent <- false
                                    Error(Malformed(pull.Url, "PR/head/revision changed; recompute affected selection"))
                                else GraphQl.page pull.Url field key decode (node.GetProperty field))
                        match GraphQl.drain pull.Url field { MaxPages = 10; MaxItems = 500 } fetch with
                        | Ok values -> values
                        | Error error ->
                            if field = "files" || field = "closingIssuesReferences" then scopeCurrent <- false
                            gaps <- gaps @ [ Errors.explain error ]
                            []
                    let issues = connection "closingIssuesReferences" "id number repository { nameWithOwner }" (fun node ->
                        let issueRepository = node.GetProperty("repository").GetProperty("nameWithOwner").GetString()
                        let number = node.GetProperty("number").GetInt32()
                        let id = node.GetProperty("id").GetString()
                        if number <= 0 || String.IsNullOrWhiteSpace id || not (issueRepository.StartsWith(binding.Owner + "/", StringComparison.Ordinal)) then Error(Malformed(pull.Url, "closing issue identity outside owner scope"))
                        else Ok(id, $"{issueRepository}#{number}")) fst
                    let files = connection "files" "path" (fun node ->
                        let path = node.GetProperty("path").GetString()
                        if String.IsNullOrWhiteSpace path then Error(Malformed(pull.Url, "empty changed file")) else Ok path) id
                    let reviews = connection "reviews" "id state commit { oid } author { login }" (fun node ->
                        let id = node.GetProperty("id").GetString()
                        let state = node.GetProperty("state").GetString()
                        let commit = node.GetProperty "commit"
                        let head = if commit.ValueKind = JsonValueKind.Null then "unknown" else commit.GetProperty("oid").GetString()
                        if String.IsNullOrWhiteSpace id || String.IsNullOrWhiteSpace state then Error(Malformed(pull.Url, "review identity/state unavailable"))
                        else Ok(id, state + "@" + head)) fst
                    let checks =
                        let fetch after =
                            let document = "query($owner: String!, $repo: String!, $head: String!, $after: String) { repository(owner: $owner, name: $repo) { nameWithOwner object(expression: $head) { ... on Commit { oid statusCheckRollup { contexts(first: 50, after: $after) { totalCount nodes { __typename ... on CheckRun { id name status conclusion } ... on StatusContext { id context state } } pageInfo { hasNextPage endCursor } } } } } } rateLimit { cost remaining } }"
                            let variables = [ "owner", VString parts[0]; "repo", VString parts[1]; "head", VString pull.Head ] @ (after |> Option.map (fun cursor -> [ "after", VString cursor ]) |> Option.defaultValue [])
                            GraphQl.read transport (query document variables (pull.Url + " head checks")) (fun data ->
                                let native = data.GetProperty "repository"
                                let commit = native.GetProperty "object"
                                let rollup = commit.GetProperty "statusCheckRollup"
                                if native.GetProperty("nameWithOwner").GetString() <> repository || commit.GetProperty("oid").GetString() <> pull.Head || rollup.ValueKind = JsonValueKind.Null then
                                    Error(Malformed(pull.Url, "head checks unavailable/mismatched; no required-check acceptance inferred"))
                                else
                                    let decode (node: JsonElement) =
                                        let kind = node.GetProperty("__typename").GetString()
                                        let id = node.GetProperty("id").GetString()
                                        let value =
                                            if kind = "CheckRun" then
                                                let conclusion = node.GetProperty "conclusion"
                                                node.GetProperty("name").GetString() + ":" + node.GetProperty("status").GetString() + ":" + (if conclusion.ValueKind = JsonValueKind.Null then "pending" else conclusion.GetString())
                                            elif kind = "StatusContext" then node.GetProperty("context").GetString() + ":" + node.GetProperty("state").GetString()
                                            else ""
                                        if String.IsNullOrWhiteSpace id || String.IsNullOrWhiteSpace value then Error(Malformed(pull.Url, "unreadable check context")) else Ok(id, value)
                                    GraphQl.page pull.Url "head checks" fst decode (rollup.GetProperty "contexts"))
                        match GraphQl.drain pull.Url "head checks" { MaxPages = 10; MaxItems = 500 } fetch with
                        | Ok values -> values |> List.map snd
                        | Error error -> gaps <- gaps @ [ Errors.explain error ]; []
                    // Final barrier prevents a stale inventory head from licensing continuation.
                    let document = "query($id: ID!) { node(id: $id) { ... on PullRequest { id state headRefOid updatedAt repository { nameWithOwner } } } rateLimit { cost remaining } }"
                    let barrier = GraphQl.read transport (query document [ "id", VId pull.Id ] (pull.Url + " final head barrier")) (fun data ->
                        let node = data.GetProperty "node"
                        if node.GetProperty("id").GetString() = pull.Id && node.GetProperty("state").GetString() = "OPEN" && node.GetProperty("headRefOid").GetString() = pull.Head
                           && node.GetProperty("updatedAt").GetString() = pull.UpdatedAt && node.GetProperty("repository").GetProperty("nameWithOwner").GetString() = repository then Ok()
                        else Error(Malformed(pull.Url, "PR/head/revision changed; recompute affected selection")))
                    match barrier with
                    | Error error -> scopeCurrent <- false; gaps <- gaps @ [ Errors.explain error ]
                    | _ -> ()
                    all <- all @ [ { pull with Issues = issues |> List.map snd; Files = files; ScopeCurrent = scopeCurrent; Reviews = reviews |> List.map snd; Checks = checks; Gaps = gaps } ]
        // Current deliverable URLs are discovery hints. Re-read their native identity/state/head,
        // even when an open-inventory association agrees; prose never supplies PR facts.
        for url in references |> List.distinct do
            let parts = (Uri url).AbsolutePath.Trim('/').Split('/')
            let repository = parts[0] + "/" + parts[1]
            let number = Int32.Parse parts[3]
            let document = "query($owner: String!, $repo: String!, $number: Int!) { repository(owner: $owner, name: $repo) { nameWithOwner pullRequest(number: $number) { id number url state headRefOid updatedAt repository { nameWithOwner } } } rateLimit { cost remaining } }"
            let variables = [ "owner", VString parts[0]; "repo", VString parts[1]; "number", VNumber(double number) ]
            let observed =
                GraphQl.read transport (query document variables (url + " Current deliverable native join")) (fun data ->
                    let native = data.GetProperty "repository"
                    let pull = native.GetProperty "pullRequest"
                    let id = pull.GetProperty("id").GetString()
                    let state = pull.GetProperty("state").GetString()
                    let head = pull.GetProperty("headRefOid").GetString()
                    let updated = pull.GetProperty("updatedAt").GetString()
                    if native.GetProperty("nameWithOwner").GetString() <> repository || pull.GetProperty("repository").GetProperty("nameWithOwner").GetString() <> repository
                       || pull.GetProperty("number").GetInt32() <> number || pull.GetProperty("url").GetString() <> url
                       || String.IsNullOrWhiteSpace id || not (sha head) || not (fst (DateTimeOffset.TryParse updated)) || not (List.contains state [ "OPEN"; "CLOSED"; "MERGED" ]) then
                        Error(Malformed(url, "Current deliverable native identity/state/head is unavailable or mismatched"))
                    else
                        Ok { Id = id; Repository = repository; Number = number; Url = url; State = state; Head = head; UpdatedAt = updated
                             Author = None; Issues = []; Files = []; ScopeCurrent = false; ReviewDecision = None; Reviews = []; Checks = []; Gaps = [] })
            match observed with
            | Error error -> repositoryGaps <- repositoryGaps.Add(repository, Errors.explain error)
            | Ok pull ->
                match all |> List.tryFind (fun current -> current.Url = url) with
                | Some current when current.Id = pull.Id && current.State = pull.State && current.Head = pull.Head && current.UpdatedAt = pull.UpdatedAt -> ()
                | Some _ -> repositoryGaps <- repositoryGaps.Add(repository, "Current deliverable changed since open PR inventory; re-observe affected slices")
                | None when pull.State = "OPEN" ->
                    let gap = "Current deliverable open PR was absent from complete inventory; files/reviews/checks are incomplete; re-observe affected slices"
                    repositoryGaps <- repositoryGaps.Add(repository, gap)
                    all <- all @ [ { pull with Gaps = [ gap ] } ]
                | None -> all <- all @ [ pull ]
        { PullRequests = all; RepositoryGaps = repositoryGaps; ObservedAt = observedAt }

    let inspectFixed (transport: IGitHubTransport) (binding: Binding) (previous: BatchReport option) =
        let reads = inspectionReads transport binding
        let intakeObservedAt = DateTimeOffset.UtcNow
        let intakeGate = BoardIntake.Gate(reads, persistCache = false)
        let metadata (items: ItemReport list) (selected: int option) (protectedInputs: (string * string * string) list) (gap: IoError option) : BatchReport =
            { ProjectId = binding.ProjectId; RecipeRevision = binding.RecipeRevision; PopulationRevision = binding.PopulationRevision
              ImportRecipeRevision = binding.ImportRecipeRevision; ImportArtifactSha256 = binding.ImportArtifactSha256; ArtifactSha256 = binding.ArtifactSha256
              NativeDispatch = []; ProtectedInputs = protectedInputs; Selected = selected; Attempted = items |> List.length
              Verified = items |> List.filter (fun item -> item.Health = "Verified") |> List.length; Items = items
              PopulationGap = gap; Cleanup = "Read-only inspection; no project lock, claims, mutation, durable effect or background work" }
        let emptyNative =
            { PullRequests = []; ObservedAt = DateTimeOffset.MinValue
              RepositoryGaps = if binding.BindingVersion = 4 then binding.Repositories |> Seq.map (fun repository -> repository, "Native integrator not collected because global enrollment/project/schema/membership admission is incomplete") |> Map.ofSeq else Map.empty }
        let failed error = { Binding = binding; Evidence = metadata [] None [] (Some error); Items = []; Memberships = []; MembershipGap = Some error; NativeIntegrator = emptyNative }
        match validateBinding binding |> Result.bind (fun () -> artifactCheck binding) with
        | Error error -> failed error
        | Ok() ->
            match readProtectedBlob reads binding binding.PopulationRevision populationPath |> Result.bind (fun (text, current, blob) -> selectedPopulation binding text |> Result.map (fun selected -> selected, current, blob)) with
            | Error error -> failed error
            | Ok(selected, current, blob) ->
                let membership = if binding.BindingVersion = 4 then readMemberships reads binding else Ok []
                let members, membershipGap = match membership with Ok values -> values, None | Error error -> [], Some error
                let items: InspectionItem list = selected |> List.map (fun (row: Selected) ->
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
                    { Evidence = evidence; ExpectedNodeId = row.NodeId; SourceCurrentness = currentness; Planning = observed; PlanningGap = planningGap; Discrepancies = discrepancies; Owner = collection.Owner; Deliverables = collection.Deliverables; DeliverableGap = collection.DeliverableGap; Intake = nativeIntake intakeGate intakeObservedAt binding collection.Native
                      SourceRevision = match collection.Verification with Ok(Current revision) -> Some revision | _ -> None })
                let integration = if binding.BindingVersion = 4 && membershipGap.IsNone then nativeIntegrator reads binding (items |> List.collect _.Deliverables) else emptyNative
                let items =
                    if binding.BindingVersion <> 4 then items
                    else List.map2 (fun (row: Selected) (item: InspectionItem) ->
                        let mutable additionalReads = 0
                        let counted =
                            { new IGitHubTransport with
                                member _.Send request = additionalReads <- additionalReads + 1; reads.Send request
                                member _.SendMutation _ = Error(Malformed("final inspection barrier", "mutation is unavailable"))
                                member _.RetryMutation _ = Error(Malformed("final inspection barrier", "replay is unavailable")) }
                        let barrier = collectSource counted binding row
                        let planning = readPlanning counted binding { Issue = row.Issue; ExpectedNodeId = row.NodeId }
                        let finalIntake = nativeIntake intakeGate intakeObservedAt binding barrier.Native
                        let intake =
                            if finalIntake.Admission = item.Intake.Admission && finalIntake.Allowed = item.Intake.Allowed && finalIntake.Gap = item.Intake.Gap then item.Intake
                            else { item.Intake with Allowed = None; Gap = Some(Malformed("final native intake barrier", "repository policy/author/permission changed or became unavailable")) }
                        let item = { item with Intake = intake }
                        let evidence: ItemReport = { item.Evidence with Reads = item.Evidence.Reads + additionalReads }
                        match barrier.Verification, planning with
                        | Ok(Current revision), Ok intent when item.SourceRevision = Some revision && barrier.Owner = item.Owner && barrier.Deliverables = item.Deliverables && barrier.DeliverableGap = item.DeliverableGap && item.Planning = Some intent -> { item with Evidence = evidence }
                        | _ -> { item with SourceCurrentness = "Unknown"; Evidence = { evidence with Health = "Unknown"; Gap = Some(Malformed("final source/planning barrier", "issue/dependency/plan or human planning changed/became unavailable; refresh affected selection")) } }) selected items
                let membershipGap =
                    if binding.BindingVersion <> 4 || membershipGap.IsSome then membershipGap
                    else
                        match readMemberships reads binding with
                        | Ok final when List.sortBy (fun (value: Membership) -> value.ItemId) final = List.sortBy (fun (value: Membership) -> value.ItemId) members -> None
                        | Ok _ -> Some(Malformed("final membership barrier", "project membership changed; reconcile complete population before selection"))
                        | Error error -> Some error
                { Binding = binding; Memberships = members; MembershipGap = membershipGap; NativeIntegrator = integration
                  Evidence = metadata (items |> List.map _.Evidence) (Some selected.Length) [ (if binding.BindingVersion = 3 then sourceRepository binding + ":" + populationPath else populationPath), current, blob ] membershipGap; Items = items }

    let planningCandidates (report: InspectionReport) (facts: IntegratorFacts) =
        if report.Evidence.Selected.IsNone || report.Evidence.PopulationGap.IsSome then []
        else report.Items |> List.map (fun (item: InspectionItem) ->
            { Issue = item.Evidence.Issue; HumanStatus = item.Planning |> Option.bind _.Status; Track = item.Planning |> Option.bind _.Track; Roadmap = item.Planning |> Option.bind _.Roadmap
              SourceCurrentness = item.SourceCurrentness; Observation = item.Planning |> Option.bind _.Observation; Integrator = facts
              UnmetOrUnknown =
                  item.Discrepancies @
                  [ if item.SourceCurrentness <> "Current" then yield "Current source/native coverage is unavailable or stale"
                    if item.Evidence.Gap.IsSome || item.PlanningGap.IsSome then yield "One or more selected reads failed"
                    if report.Binding.BindingVersion <> 4 then
                        yield item.Evidence.Delivery; yield item.Evidence.Publication; yield item.Evidence.NativeAcceptance
                    if facts.OpenPullRequests.IsNone then yield "Open PR facts are unknown"
                    if facts.TouchSets.IsNone then yield "Touch-set facts are unknown"
                    if facts.AvailableSlots.IsNone then yield "Current capacity is unknown"
                    yield "Intake authorization and scheduling selection remain the current integrator's responsibility" ] })

    // Advisory fold in explicit integrator order. Selected proposals consume only this invocation's
    // remaining capacity; no allocation, reservation, dispatch or original-effect receipt is created.
    let selectActions (report: InspectionReport) (context: SelectionContext option) (now: DateTimeOffset) =
        let fresh (observed: DateTimeOffset) =
            observed <> DateTimeOffset.MinValue && observed <= now && now - observed <= TimeSpan.FromMinutes 5.0
        let attributed (value: AttributedObservation) =
            fresh value.ObservedAt && value.ObservedAt <= value.ValidUntil && now <= value.ValidUntil
            && value.ValidUntil - value.ObservedAt <= TimeSpan.FromMinutes 5.0
            && not (String.IsNullOrWhiteSpace value.Supplier) && not value.Evidence.IsEmpty
            && (value.Evidence |> List.forall (String.IsNullOrWhiteSpace >> not))
        let paths (values: string list) = FS.GG.Coord.TouchSet.parse ("Paths: " + String.concat " " values)
        let usable (values: string list) =
            match paths values with
            | FS.GG.Coord.Types.Declared tokens when not values.IsEmpty ->
                let roundTrip = tokens |> List.choose (function FS.GG.Coord.Types.Matchable value -> Some value | _ -> None)
                roundTrip = values && FS.GG.Coord.TouchSet.usability (paths values) = FS.GG.Coord.TouchSet.Usable
            | _ -> false
        let canonicalScope (repository: string) =
            if String.IsNullOrWhiteSpace repository then false
            else
                let parts = repository.Split('/')
                parts.Length = 2 && (parts |> Array.forall (fun value -> not (String.IsNullOrWhiteSpace value) && value = value.Trim()))
        let overlaps (leftRepo: string) (leftPaths: string list) (rightRepo: string) (rightPaths: string list) =
            let left = leftRepo.Split('/')
            let right = rightRepo.Split('/')
            canonicalScope leftRepo && canonicalScope rightRepo
            && not (FS.GG.Coord.TouchSet.scopedConflicts left[0] left[1] right[0] right[1] (paths leftPaths) (paths rightPaths) |> List.isEmpty)
        let identity (issue: IssueRef) = $"{issue.Owner}/{issue.Repository}#{issue.Number}"
        let currentContext =
            context |> Option.filter (fun (value: SelectionContext) ->
                fresh value.ObservedAt && value.ObservedAt <= value.ValidUntil && now <= value.ValidUntil
                && value.ValidUntil - value.ObservedAt <= TimeSpan.FromMinutes 5.0
                && not (String.IsNullOrWhiteSpace value.Invocation) && not (String.IsNullOrWhiteSpace value.Evidence)
                && value.ProjectId = report.Binding.ProjectId && value.SchemaVersion = report.Binding.SchemaVersion
                && value.RecipeRevision = report.Binding.RecipeRevision && value.ArtifactSha256 = report.Binding.ArtifactSha256
                && value.PopulationRevision = report.Binding.PopulationRevision
                && (report.Evidence.ProtectedInputs |> List.exists (fun (_, _, blob) -> blob = value.EnrollmentBlob)))
        let proposals: (InspectionItem option * CurrentSlice option * IssueRef) list =
            match context with
            | Some value when not value.Slices.IsEmpty ->
                value.Slices |> List.map (fun (slice: CurrentSlice) ->
                    report.Items |> List.tryFind (fun item -> item.Evidence.Issue = slice.Issue), Some slice, slice.Issue)
            | _ -> report.Items |> List.map (fun item -> Some item, None, item.Evidence.Issue)
        let mutable chosen: CurrentSlice list = []
        let mutable implementation = currentContext |> Option.bind _.ImplementationSlots
        let mutable review = currentContext |> Option.bind _.ReviewSlots
        let mutable ci = currentContext |> Option.bind _.HostedCiSlots
        let evaluate (itemOption: InspectionItem option, slice: CurrentSlice option, issue: IssueRef) : Selection =
            let value = currentContext
            let action = slice |> Option.map _.Action |> Option.defaultValue Source
            let owner = slice |> Option.map _.Owner |> Option.orElseWith (fun () -> itemOption |> Option.bind _.Owner)
            let associatedAll =
                match itemOption with
                | None -> []
                | Some item ->
                    report.NativeIntegrator.PullRequests |> List.filter (fun pull ->
                        List.contains (identity issue) pull.Issues || List.contains pull.Url item.Deliverables)
            let associations = associatedAll |> List.filter (fun candidate -> candidate.State = "OPEN")
            let pull =
                match slice |> Option.bind _.PullRequest with
                | Some url -> associatedAll |> List.tryFind (fun current -> current.Url = url)
                | None when slice.IsNone && associations.Length = 1 -> Some associations.Head
                | _ -> None
            let result (decision: string) (reasons: string list) : Selection =
                let nativeEvidence =
                    match itemOption |> Option.bind (fun item -> item.Evidence.Native) with
                    | Some native -> [ "native-observed: " + native.NodeId + "@" + native.UpdatedAt + ":" + native.BodySha256 ]
                    | None -> [ "missing: native issue" ]
                let pullEvidence =
                    match pull with
                    | Some current ->
                        [ "native-observed: " + current.Url + "@" + current.Head ]
                        @ ((current.Reviews @ current.Checks) |> List.map (fun text -> "native-observed: " + text))
                    | None -> []
                let suppliedEvidence =
                    match slice with
                    | Some current ->
                        [ "integrator-reported: " + current.Observation.Supplier ]
                        @ (current.Observation.Evidence |> List.map (fun text -> "integrator-reported: " + text))
                    | None -> [ "missing: attributed integrator slice/action/prerequisite/intake observations" ]
                let invocationEvidence =
                    match value with
                    | Some current -> [ "integrator-reported: invocation " + current.Invocation + ": " + current.Evidence ]
                    | None -> [ "missing: current bound integrator invocation" ]
                { SliceId = slice |> Option.map _.Id; Attempt = slice |> Option.map _.Attempt; Operation = slice |> Option.map _.Operation
                  Issue = issue; Action = action; Decision = decision; Owner = owner
                  PullRequest = pull |> Option.map _.Url; Head = pull |> Option.map _.Head; Reasons = reasons
                  Evidence = [ "enrolled: " + identity issue ] @ nativeEvidence @ pullEvidence @ suppliedEvidence @ invocationEvidence }
            let globalGap = report.Binding.BindingVersion <> 4 || report.Evidence.PopulationGap.IsSome || report.MembershipGap.IsSome
            if globalGap then result "unknown" [ "Global enrollment/project/schema/membership read is incomplete or historical/product mode does not select programme actions" ]
            elif not (fresh report.NativeIntegrator.ObservedAt) then result "unknown" [ "Native integrator snapshot expired or is from the future; refresh before action" ]
            elif slice.IsSome && currentContext.IsNone then
                result "unknown" [ "Bound integrator invocation is missing, expired, from the future or disagrees with the exact enrollment/binding; retain each proposed slice identity for recovery" ]
            elif itemOption.IsNone then result "unknown" [ "Proposed slice issue is outside the exact native enrollment" ]
            else
                let item = itemOption.Value
                if item.SourceCurrentness <> "Current" || item.Evidence.Gap.IsSome || item.PlanningGap.IsSome then
                    result "unknown" [ "Current issue/dependency/plan/planning read is unavailable or changed" ]
                elif item.Owner |> Option.forall String.IsNullOrWhiteSpace then
                    result "needs-decision" [ "Current accountable owner is missing or ambiguous" ]
                elif item.Discrepancies |> List.contains "Human Roadmap differs from the admitted owning plan" then
                    result "needs-decision" [ "Human Roadmap differs from the enrolled owning plan; reconcile the selected action scope" ]
                elif item.Evidence.Native |> Option.exists (fun native -> native.State = "CLOSED") then
                    result "needs-decision" [ "Native issue is closed; reconcile remaining outcome acceptance" ]
                elif item.Planning |> Option.exists (fun planning -> planning.Status = Some "Done") then
                    result "needs-decision" [ "Human Done requires outcome acceptance reconciliation" ]
                elif slice.IsNone then
                    result "unknown" [ "Exact attributed slice/action/prerequisite/intake observations are missing; recovered PR evidence grants no continuation or assignment authority" ]
                else
                    let current = slice.Value
                    let allSlices = value.Value.Slices
                    let duplicate = allSlices |> List.filter (fun other -> other.Id = current.Id) |> List.length > 1
                    let scopeInvalid =
                        allSlices |> List.exists (fun other -> not (canonicalScope other.Repository) || not (usable other.Paths) || String.IsNullOrWhiteSpace other.Owner)
                    let native = item.Evidence.Native
                    let joinMatches =
                        native |> Option.exists (fun observed ->
                            observed.NodeId = current.Join.NativeId && observed.UpdatedAt = current.Join.UpdatedAt
                            && observed.BodySha256 = current.Join.BodySha256 && item.SourceRevision = Some current.Join.SourceRevision)
                    let referenceRepositories (references: string list) =
                        references |> List.choose (fun reference ->
                            let mutable uri = Unchecked.defaultof<Uri>
                            if Uri.TryCreate(reference, UriKind.Absolute, &uri) && uri.Scheme = "https" && uri.Host = "github.com" then
                                let parts = uri.AbsolutePath.Trim('/').Split('/')
                                if parts.Length >= 2 then Some(parts[0] + "/" + parts[1]) else None
                            else None)
                    let relevantRepos =
                        [ current.Repository ]
                        @ (item.Evidence.DependencyObservations |> List.map (fun dependency -> dependency.Issue.Owner + "/" + dependency.Issue.Repository))
                        @ (referenceRepositories ((current.PullRequest |> Option.toList) @ (current.Prerequisites |> List.map _.Reference)))
                        |> Set.ofList
                    let repositoryGaps = report.NativeIntegrator.RepositoryGaps |> Map.toList |> List.filter (fun (repository, _) -> relevantRepos.Contains repository)
                    let relevantPulls = report.NativeIntegrator.PullRequests |> List.filter (fun candidate -> candidate.State = "OPEN" && relevantRepos.Contains candidate.Repository)
                    let unreadablePulls =
                        relevantPulls |> List.filter (fun candidate ->
                            not candidate.ScopeCurrent || not (usable candidate.Files)
                            || (Some candidate.Url = current.PullRequest && not candidate.Gaps.IsEmpty))
                    let unassignedAssociations =
                        associations |> List.filter (fun candidate ->
                            candidate.Repository = current.Repository
                            && not (allSlices |> List.exists (fun other -> other.Issue = issue && other.PullRequest = Some candidate.Url)))
                    let prerequisites = current.Prerequisites |> List.filter (fun prerequisite -> prerequisite.Action = action)
                    let unsatisfied = prerequisites |> List.filter (fun prerequisite -> prerequisite.Satisfied = Some false)
                    let unknown = prerequisites |> List.filter (fun prerequisite -> prerequisite.Satisfied.IsNone || String.IsNullOrWhiteSpace prerequisite.SupplyingOwner)
                    let openEdges = item.Evidence.DependencyObservations |> List.filter (fun edge -> edge.State <> "CLOSED")
                    let unclassified =
                        openEdges |> List.exists (fun edge ->
                            let matches = current.Prerequisites |> List.filter (fun prerequisite -> prerequisite.Reference = edge.Url || prerequisite.Reference = identity edge.Issue)
                            matches.IsEmpty || (matches |> List.exists (fun prerequisite -> prerequisite.Action = action && prerequisite.Satisfied = Some true)))
                    let operationMatches =
                        match current.Operation, action with
                        | (ObserveExisting | Implement | ReviewWork | QualifyWork), Source -> true
                        | ActivateWork, Activation -> true
                        | _ -> false
                    if duplicate then result "needs-decision" [ "Duplicate stable slice identity; reconcile the explicit integrator order" ]
                    elif scopeInvalid then result "unknown" [ "Proposed slice scope is malformed; no disjointness conclusion is possible" ]
                    elif String.IsNullOrWhiteSpace current.Id || String.IsNullOrWhiteSpace current.Attempt || current.AccountableOwner <> item.Owner.Value
                         || not (report.Binding.Repositories.Contains current.Repository) then
                        result "needs-decision" [ "Current slice identity/accountable owner/repository conflicts with native enrollment" ]
                    elif not (attributed current.Observation) || not joinMatches then
                        result "unknown" [ "Integrator-reported action/prerequisite/intake evidence is expired, missing or bound to a different native issue/body/owning-plan revision" ]
                    elif item.Intake.Allowed = Some false then result "blocked" [ "Native repository policy/current issue-author permission refuses intake; parent operation authority is separate" ]
                    elif item.Intake.Allowed.IsNone || item.Intake.Gap.IsSome || not (fresh item.Intake.ObservedAt) then
                        result "unknown" [ "Native repository policy/current issue-author allowance is missing, changed or expired" ]
                    elif current.IntakeAuthorized = Some false then result "blocked" [ "The current integrator intake decision refuses this action" ]
                    elif not unsatisfied.IsEmpty then result "blocked" (unsatisfied |> List.map (fun prerequisite -> "Unsatisfied action prerequisite: " + prerequisite.Reference))
                    elif not current.PrerequisitesComplete || unclassified || not unknown.IsEmpty || current.IntakeAuthorized.IsNone then
                        result "unknown" [ "Action-specific native and owning-plan prerequisite/intake coverage is incomplete; supplying owner is the integrator" ]
                    elif not operationMatches then result "unknown" [ "Proposed action and operation resource demand do not agree" ]
                    elif not repositoryGaps.IsEmpty then result "unknown" (repositoryGaps |> List.map (fun (repository, gap) -> repository + ": " + gap))
                    elif not unreadablePulls.IsEmpty then result "unknown" [ "Relevant in-flight PR identity/head/file scope or review/check evidence is unreadable; no disjointness conclusion is possible" ]
                    elif item.DeliverableGap.IsSome then result "unknown" [ item.DeliverableGap.Value ]
                    elif current.PullRequest.IsNone && (associatedAll |> List.exists (fun candidate -> candidate.Repository = current.Repository && candidate.State <> "OPEN")) && not (associations |> List.exists (fun candidate -> candidate.Repository = current.Repository)) then
                        result "needs-decision" [ "Only historical closed/merged Current deliverable references remain; reconcile the current action without inferring acceptance" ]
                    elif not unassignedAssociations.IsEmpty then result "needs-decision" [ "Native Current deliverable/closing associations are not mapped unambiguously to declared slices; withhold duplicate work" ]
                    elif current.PullRequest.IsSome && pull.IsNone then result "needs-decision" [ "Declared existing PR is not a current native owning-issue association" ]
                    elif pull |> Option.exists (fun candidate -> candidate.State <> "OPEN") then result "needs-decision" [ "Native Current deliverable is closed or merged; reconcile the superseded deliverable before action" ]
                    elif pull |> Option.exists (fun candidate -> current.PullRequestId <> Some candidate.Id || current.PullRequestUpdatedAt <> Some candidate.UpdatedAt) then
                        result "unknown" [ "Declared existing PR native ID/revision differs from the current native observation" ]
                    elif pull |> Option.exists (fun candidate -> current.Head <> Some candidate.Head) then result "unknown" [ "Declared existing PR head differs from the current native head" ]
                    elif current.PullRequest.IsNone && (current.Head.IsSome || current.PullRequestId.IsSome || current.PullRequestUpdatedAt.IsSome) then result "unknown" [ "A head without a native PR association cannot bind an action" ]
                    elif (current.Operation = ReviewWork || current.Operation = QualifyWork) && pull.IsNone then
                        result "unknown" [ "Review/qualification demand requires an existing native PR/head; future workload capacity is not source-edit demand" ]
                    elif current.Operation = ObserveExisting then
                        if pull.IsNone then result "unknown" [ "Read-only continuation requires an existing exact PR/head" ]
                        else result "continue-existing" [ "Observe the established slice/owner/attempt and exact native PR/head; no new implementation/review/CI capacity is consumed; no dispatch authority" ]
                    elif current.Operation = ActivateWork then result "unknown" [ "Activation remains under its operation-specific admitted executor; this source selection cannot infer its resource demand or acceptance" ]
                    else
                        let reservations = value.Value.Reservations
                        let invalidReservations =
                            reservations |> Option.exists (List.exists (fun reservation ->
                                not (canonicalScope reservation.Repository) || not (usable reservation.Paths) || String.IsNullOrWhiteSpace reservation.Owner
                                || String.IsNullOrWhiteSpace reservation.CustodyReference || String.IsNullOrWhiteSpace reservation.SliceId || String.IsNullOrWhiteSpace reservation.Attempt))
                        let self (reservation: Reservation) =
                            reservation.SliceId = current.Id && reservation.Attempt = current.Attempt && reservation.Operation = current.Operation
                            && reservation.Owner = current.Owner && reservation.Repository = current.Repository && reservation.Paths = current.Paths
                            && reservation.PullRequest = current.PullRequest && reservation.Head = current.Head && current.PullRequest.IsSome
                        let holds = reservations |> Option.defaultValue [] |> List.filter (fun reservation -> not (self reservation) && overlaps current.Repository current.Paths reservation.Repository reservation.Paths)
                        let conflicts = relevantPulls |> List.filter (fun candidate -> Some candidate.Url <> current.PullRequest && overlaps current.Repository current.Paths candidate.Repository candidate.Files)
                        let chosenConflicts = chosen |> List.filter (fun earlier -> overlaps current.Repository current.Paths earlier.Repository earlier.Paths)
                        if invalidReservations then result "unknown" [ "Reservation scope/custody is unreadable; existing uncertain reservations remain held" ]
                        elif not holds.IsEmpty || not conflicts.IsEmpty then result "blocked" [ "Current slice overlaps in-flight native work or an original reservation; expiry/owner-name equality cannot release a hold" ]
                        elif not chosenConflicts.IsEmpty then result "deferred" [ "Current slice conflicts with an earlier selected slice in this explicit invocation order" ]
                        elif reservations.IsNone || not (value.Value.ReservationObservation |> Option.exists attributed) then
                            result "unknown" [ "Fresh attributed relevant reservation coverage is unavailable; expired or uncertain holds remain held" ]
                        elif value.Value.CapacityMeaning <> "remaining-after-active" || not (value.Value.CapacityObservation |> Option.exists attributed) then
                            result "unknown" [ "Resource counts must be freshly attributed remaining-after-active capacity; no collaboration-slot inference" ]
                        else
                            let available, consume =
                                match current.Operation with
                                | Implement -> implementation, (fun () -> implementation <- implementation |> Option.map (fun count -> count - 1))
                                | ReviewWork -> review, (fun () -> review <- review |> Option.map (fun count -> count - 1))
                                | QualifyWork -> ci, (fun () -> ci <- ci |> Option.map (fun count -> count - 1))
                                | _ -> None, (fun () -> ())
                            let negative = [ implementation; review; ci ] |> List.exists (Option.exists (fun count -> count < 0))
                            if negative then result "unknown" [ "Negative resource observations cannot establish remaining capacity" ]
                            else
                                match available with
                                | None -> result "unknown" [ "The supplying owner has not observed capacity for this operation's actual demand" ]
                                | Some count when count <= 0 -> result "deferred" [ "This operation's remaining capacity is exhausted by active/earlier selected work" ]
                                | Some _ ->
                                    consume ()
                                    chosen <- chosen @ [ current ]
                                    result "ready"
                                        [ "Selected in explicit integrator order within action-specific remaining capacity, native in-flight scope and original reservations";
                                          "Integrator-reported observations grant no allocation or dispatch authority; recheck determining facts beside owner execution" ]
        proposals |> List.map evaluate
