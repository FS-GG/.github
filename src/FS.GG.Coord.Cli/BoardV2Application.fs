namespace FS.GG.Coord.Cli

module BoardV2Application =
    open System
    open System.IO
    open System.Text.Json
    open System.Security.Cryptography
    open FS.GG.Coord.GitHub
    open FS.GG.Coord.GitHub.Transport
    open FS.GG.Coord.GitHub.V2Projection
    open FS.GG.Coord.GitHub.V2ProjectionSource

    let private text (name: string) (element: JsonElement) = element.GetProperty(name).GetString()
    let private number (name: string) (element: JsonElement) = element.GetProperty(name).GetInt32()
    let private closed names (element: JsonElement) =
        if element.ValueKind <> JsonValueKind.Object then invalidArg "json" "expected an object"
        let properties = element.EnumerateObject() |> Seq.map _.Name |> Seq.toList
        if Set.ofList properties <> Set.ofList names || properties.Length <> names.Length then
            invalidArg "json" "unknown, missing or duplicate properties"

    let private parseIssue (element: JsonElement) : IssueRef =
        closed [ "owner"; "repository"; "number" ] element
        { Owner = text "owner" element; Repository = text "repository" element; Number = number "number" element }

    let parseBinding json =
        try
            use document = JsonDocument.Parse(json: string)
            let root = document.RootElement
            closed [ "bindingVersion"; "importRecipeRevision"; "importArtifactSha256"; "selectedIssues"; "schemaVersion"; "recipeRevision"; "populationRevision"; "organizationId"; "artifactSha256"; "ownerKind"; "owner"; "projectNumber"; "projectTitle"; "projectId"; "status"; "roadmapFieldId"; "track"; "observation"; "repositories" ] root
            if text "ownerKind" root <> "organization" then invalidArg "ownerKind" "only organization is admitted"
            let field (name: string) =
                let value = root.GetProperty(name)
                closed [ "id"; "options" ] value
                let options = value.GetProperty("options").EnumerateObject() |> Seq.map (fun item -> item.Name, item.Value.GetString()) |> Seq.toList
                if options.Length <> (options |> Map.ofList |> Map.count) then invalidArg "options" "duplicate options"
                { Id = text "id" value; Options = Map.ofList options }
            let repositories = root.GetProperty("repositories").EnumerateArray() |> Seq.map _.GetString() |> Seq.toList
            if repositories.Length <> (repositories |> Set.ofList |> Set.count) then invalidArg "repositories" "duplicate repositories"
            let selected = root.GetProperty("selectedIssues").EnumerateObject() |> Seq.map (fun entry -> entry.Name, entry.Value.GetString()) |> Seq.toList
            if selected.Length <> (selected |> Map.ofList |> Map.count) then invalidArg "selectedIssues" "duplicate native issue IDs"
            let binding =
                { BindingVersion = number "bindingVersion" root; ImportRecipeRevision = text "importRecipeRevision" root
                  ImportArtifactSha256 = text "importArtifactSha256" root
                  SelectedIssues = Map.ofList selected
                  SchemaVersion = number "schemaVersion" root; RecipeRevision = text "recipeRevision" root
                  PopulationRevision = text "populationRevision" root; OrganizationId = text "organizationId" root
                  ArtifactSha256 = text "artifactSha256" root; OwnerKind = Org; Owner = text "owner" root
                  ProjectNumber = number "projectNumber" root; ProjectTitle = text "projectTitle" root; ProjectId = text "projectId" root
                  Status = field "status"; RoadmapFieldId = text "roadmapFieldId" root; Track = field "track"
                  Observation = field "observation"; Repositories = Set.ofList repositories }
            V2Projection.validateBinding binding |> Result.map (fun () -> binding) |> Result.mapError Errors.explain
        with error -> Error("invalid binding: " + error.Message)

    let private issueWire (issue: IssueRef) = {| owner = issue.Owner; repository = issue.Repository; number = issue.Number |}
    let private projectionWire (report: Report) =
        let outcome, itemId = match report.Outcome with AlreadyCurrent id -> "already-current", id | Updated id -> "updated", id
        {| projectId = report.ProjectId; recipeRevision = report.RecipeRevision; issue = issueWire report.Issue
           observedRevision = report.ObservedRevision; observation = report.Observation; outcome = outcome; itemId = itemId
           sourceChecks = report.SourceChecks; projectReads = report.ProjectReads; mutations = report.Mutations; verifiedAt = report.VerifiedAt |}
    let private nativeWire (native: NativeObservation) =
        {| issue = issueWire native.Issue; nodeId = native.NodeId; updatedAt = native.UpdatedAt; state = native.State; url = native.Url; bodySha256 = native.BodySha256 |}
    let encodeReport (report: BatchReport) =
        JsonSerializer.Serialize
            {| schema = "fsgg.coord.board-v2-refresh/2"; projectId = report.ProjectId; recipeRevision = report.RecipeRevision
               populationRevision = report.PopulationRevision; importRecipeRevision = report.ImportRecipeRevision
               importArtifactSha256 = report.ImportArtifactSha256; artifactSha256 = report.ArtifactSha256; protectedInputs = report.ProtectedInputs; nativeDispatch = report.NativeDispatch |> List.map (fun receipt -> {| httpStatus = receipt.HttpStatus; headers = receipt.Headers; responseSha256 = receipt.ResponseSha256; error = receipt.Error |}); readAccounting = "Logical source/project calls; dispatch identity reads are additional"; selected = report.Selected; attempted = report.Attempted; verified = report.Verified
               populationGap = report.PopulationGap |> Option.map Errors.explain; cleanup = report.Cleanup
               items = report.Items |> List.map (fun item ->
                   {| issue = issueWire item.Issue; native = item.Native |> Option.map nativeWire
                      dependencyObservations = item.DependencyObservations |> List.map nativeWire
                      dependencyReadComplete = item.DependencyReadComplete; planObservation = item.PlanObservation |> Option.map nativeWire
                      delivery = item.Delivery; publication = item.Publication; nativeAcceptance = item.NativeAcceptance
                      health = item.Health; reads = item.Reads; mutationAttempts = item.MutationAttempts; membershipPages = item.MembershipPages
                      projection = item.Projection |> Option.map projectionWire; gap = item.Gap |> Option.map Errors.explain
                      lastVerified = item.LastVerified |> Option.map projectionWire |}) |}

    let decodePreviousReport json =
        try
            use document = JsonDocument.Parse(json: string)
            let root = document.RootElement
            closed [ "schema"; "projectId"; "recipeRevision"; "populationRevision"; "importRecipeRevision"; "importArtifactSha256"; "artifactSha256"; "protectedInputs"; "nativeDispatch"; "readAccounting"; "selected"; "attempted"; "verified"; "populationGap"; "cleanup"; "items" ] root
            if text "schema" root <> "fsgg.coord.board-v2-refresh/2" then invalidArg "schema" "unsupported report schema"
            let optional (name: string) decode (value: JsonElement) =
                let property = value.GetProperty(name)
                if property.ValueKind = JsonValueKind.Null then None else Some(decode property)
            let projection (value: JsonElement) : Report =
                closed [ "projectId"; "recipeRevision"; "issue"; "observedRevision"; "observation"; "outcome"; "itemId"; "sourceChecks"; "projectReads"; "mutations"; "verifiedAt" ] value
                let recipeRevision = text "recipeRevision" value
                if String.IsNullOrWhiteSpace recipeRevision || recipeRevision.Length <> 40 || recipeRevision |> Seq.exists (Uri.IsHexDigit >> not) then
                    invalidArg "recipeRevision" "historical recipe revision must be an immutable 40 hexadecimal revision"
                let outcome = match text "outcome" value with "already-current" -> AlreadyCurrent(text "itemId" value) | "updated" -> Updated(text "itemId" value) | _ -> invalidArg "outcome" "unknown historical outcome"
                if text "observation" value <> "Verified" then invalidArg "observation" "historical successful observation must be Verified"
                { ProjectId = text "projectId" value; RecipeRevision = text "recipeRevision" value; Issue = parseIssue (value.GetProperty("issue"))
                  ObservedRevision = text "observedRevision" value; Observation = "Verified"; Outcome = outcome
                  SourceChecks = number "sourceChecks" value; ProjectReads = number "projectReads" value; Mutations = number "mutations" value
                  VerifiedAt = value.GetProperty("verifiedAt").GetDateTimeOffset() }
            let items = root.GetProperty("items").EnumerateArray() |> Seq.map (fun value ->
                closed [ "issue"; "native"; "dependencyObservations"; "dependencyReadComplete"; "planObservation"; "delivery"; "publication"; "nativeAcceptance"; "health"; "reads"; "mutationAttempts"; "membershipPages"; "projection"; "gap"; "lastVerified" ] value
                let issue = parseIssue (value.GetProperty("issue"))
                let history = optional "lastVerified" projection value
                if history |> Option.exists (fun report -> report.Issue <> issue || report.ProjectId <> text "projectId" root) then
                    invalidArg "lastVerified" "historical identity differs from report"
                { Issue = issue; Native = None; DependencyObservations = []; DependencyReadComplete = false; PlanObservation = None; Delivery = "Unknown"; Publication = "Unknown"; NativeAcceptance = "Unknown"
                  Health = "Unknown"; Reads = 0; MutationAttempts = 0; MembershipPages = None; Projection = None; Gap = None; LastVerified = history }) |> Seq.toList
            Ok { ProjectId = text "projectId" root; RecipeRevision = text "recipeRevision" root; PopulationRevision = text "populationRevision" root
                 ImportRecipeRevision = text "importRecipeRevision" root; ImportArtifactSha256 = text "importArtifactSha256" root; ArtifactSha256 = text "artifactSha256" root; NativeDispatch = []; ProtectedInputs = []
                 Selected = None; Attempted = 0; Verified = 0; Items = items; PopulationGap = None; Cleanup = "historical-input" }
        with error -> Error("invalid previous report: " + error.Message)

    let private parseArguments arguments =
        let rec flags values = function
            | [] ->
                match Map.tryFind "--binding-file" values, Map.tryFind "--report-file" values with
                | Some binding, Some report when binding <> report && Map.tryFind "--previous-report-file" values <> Some report ->
                    let bindingPath = Path.GetFullPath binding
                    let reportPath = Path.GetFullPath report
                    let previousPath = Map.tryFind "--previous-report-file" values |> Option.map Path.GetFullPath
                    if bindingPath = reportPath || previousPath = Some reportPath || File.Exists reportPath then
                        Error "report output must be a new path distinct from all inputs"
                    else Ok(bindingPath, previousPath, reportPath)
                | _ -> Error "refresh requires distinct --binding-file PATH and --report-file PATH"
            | flag :: value :: rest when List.contains flag [ "--binding-file"; "--previous-report-file"; "--report-file" ] && not (Map.containsKey flag values) && not (String.IsNullOrWhiteSpace value) && not (value.StartsWith("--", StringComparison.Ordinal)) -> flags (Map.add flag value values) rest
            | _ -> Error "unknown, duplicate or incomplete refresh argument"
        match arguments with "refresh" :: rest -> flags Map.empty rest | _ -> Error "expected board-v2 refresh"

    let private prepare arguments =
        (try parseArguments arguments with error -> Error("invalid input path: " + error.Message)) |> Result.bind (fun (bindingPath, previousPath, reportPath) ->
            try
                parseBinding (File.ReadAllText bindingPath) |> Result.bind (fun binding ->
                    use stream = File.OpenRead typeof<Binding>.Assembly.Location
                    let actual = SHA256.HashData stream |> Convert.ToHexString |> _.ToLowerInvariant()
                    if not (String.Equals(actual, binding.ArtifactSha256, StringComparison.OrdinalIgnoreCase)) then Error "selected binding does not match the loaded adapter assembly SHA-256"
                    else
                        let previous = match previousPath with None -> Ok None | Some path -> decodePreviousReport (File.ReadAllText path) |> Result.map Some
                        previous |> Result.map (fun history -> binding, history, reportPath))
            with error -> Error("could not read refresh input: " + error.Message))

    // Leave enough time for the existing 30 second transport timeout. Stop before dispatch,
    // without cancelling an in-flight writer; serialization is owned by the fixed job.
    let private bounded (transport: IGitHubTransport) =
        let elapsed = Diagnostics.Stopwatch.StartNew()
        let mutable calls = 0
        let dispatch subject action =
            if elapsed.Elapsed >= TimeSpan.FromSeconds 270.0 || calls >= 500 then Error(Errors.Malformed(subject, "bounded refresh deadline/request ceiling reached; coverage Unknown"))
            else
                calls <- calls + 1
                action ()
        { new IGitHubTransport with
            member _.Send request = dispatch request.Subject (fun () -> transport.Send request)
            member _.SendMutation mutation = dispatch mutation.Request.Subject (fun () -> transport.SendMutation mutation)
            member _.RetryMutation effectId = Error(Errors.Malformed(effectId, "fixed refresh cannot replay durable mutation recipes")) }

    let private refuse message =
        eprintfn "fsgg-coord-engine: board-v2 refresh refused: %s" message
        1
    let private execute (transport: IGitHubTransport) ((binding: Binding), previous, reportPath) =
        try
            let report =
                let lockPath = Path.Combine(Path.GetTempPath(), "fsgg-v2-observation-" + binding.ProjectId + ".lock")
                use lease = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)
                V2ProjectionSource.runFixed (bounded transport) binding previous
            let evidence = match box transport with :? V2ObservationTransport.IDispatchEvidence as evidence -> evidence.DispatchReceipts | _ -> []
            let report = { report with NativeDispatch = evidence; Cleanup = "Root-local project exclusive OS handle released; no distributed lease or background work" }
            File.WriteAllText(reportPath, encodeReport report + Environment.NewLine)
            if report.PopulationGap.IsSome || report.Selected <> Some report.Verified then 3 else 0
        with error -> refuse ("refresh/report failed: " + error.Message)
    let runWithTransport transport arguments =
        match prepare arguments with Error message -> refuse message | Ok input -> execute transport input
    let tryRun arguments =
        match arguments with
        | "board-v2" :: rest ->
            Some(match prepare rest with
                 | Error message -> refuse message
                 | Ok input ->
                     let binding, _, _ = input
                     let token =
                         let primary = Environment.GetEnvironmentVariable "GITHUB_TOKEN"
                         if String.IsNullOrWhiteSpace primary then Environment.GetEnvironmentVariable "GH_TOKEN" else primary
                     match V2ObservationTransport.createLive binding token with
                     | Error error -> refuse (Errors.explain error)
                     | Ok(transport, lifetime) ->
                         use lifetime = lifetime
                         execute transport input)
        | _ -> None
