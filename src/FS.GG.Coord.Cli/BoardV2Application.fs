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
            // Version 3 uses this same closed wire shape for a selected receiver repository.
            // Local-only workspaces select no board command; no implicit binding is constructed.
            V2Projection.validateBinding binding |> Result.map (fun () -> binding) |> Result.mapError Errors.explain
        with error -> Error("invalid binding: " + error.Message)

    // JSON is an attributed caller report, never authentication or native acceptance. All groups
    // retain their original observation interval; decoding/serialization cannot renew a fact.
    let parseIntegratorObservations (binding: Binding) (json: string) : Result<SelectionContext, string> =
        try
            let utf8 = System.Text.UTF8Encoding(false, true)
            if binding.BindingVersion <> 4 || utf8.GetByteCount json > 262144 then
                invalidArg "input" "organization-only bounded input"
            let options = JsonDocumentOptions(MaxDepth = 12)
            use document = JsonDocument.Parse(json, options)
            let root = document.RootElement
            closed [ "schema"; "invocation"; "projectId"; "schemaVersion"; "recipeRevision"; "artifactSha256"; "populationRevision"; "enrollmentBlob";
                     "observedAt"; "validUntil"; "evidence"; "slices"; "capacity"; "reservations" ] root
            let boundedText (name: string) (limit: int) (value: JsonElement) =
                let result = text name value
                if String.IsNullOrWhiteSpace result || result.Length > limit || result.Contains('\u0000') then invalidArg "input" "bounded nonempty text required"
                result
            let values (limit: int) (value: JsonElement) =
                if value.ValueKind <> JsonValueKind.Array || value.GetArrayLength() > limit then invalidArg "input" "bounded array required"
                value.EnumerateArray() |> Seq.toList
            let strings (limit: int) (width: int) (value: JsonElement) =
                values limit value |> List.map (fun entry ->
                    if entry.ValueKind <> JsonValueKind.String then invalidArg "input" "text array required"
                    let result = entry.GetString()
                    if String.IsNullOrWhiteSpace result || result.Length > width || result.Contains('\u0000') then invalidArg "input" "bounded nonempty text required"
                    result)
            let timestamp (name: string) (value: JsonElement) = value.GetProperty(name).GetDateTimeOffset()
            let observation (value: JsonElement) : AttributedObservation =
                closed [ "supplier"; "observedAt"; "validUntil"; "evidence" ] value
                { Supplier = boundedText "supplier" 512 value; ObservedAt = timestamp "observedAt" value
                  ValidUntil = timestamp "validUntil" value; Evidence = strings 16 2048 (value.GetProperty "evidence") }
            let optional (decode: JsonElement -> 'a) (value: JsonElement) =
                if value.ValueKind = JsonValueKind.Null then None else Some(decode value)
            let optionalText (value: JsonElement) =
                optional (fun entry ->
                    if entry.ValueKind <> JsonValueKind.String || String.IsNullOrWhiteSpace(entry.GetString()) || entry.GetString().Length > 2048 then
                        invalidArg "input" "bounded optional text required"
                    entry.GetString()) value
            let action (value: string) =
                match value with "source" -> Source | "activation" -> Activation | _ -> invalidArg "input" "unsupported action"
            let operation (value: string) =
                match value with
                | "observe-existing" -> ObserveExisting | "implement" -> Implement | "review" -> ReviewWork
                | "qualify" -> QualifyWork | "activate" -> ActivateWork | _ -> invalidArg "input" "unsupported operation"
            let repository (value: JsonElement) =
                let result = boundedText "repository" 256 value
                if not (binding.Repositories.Contains result) then invalidArg "input" "repository outside selected native scope"
                result
            let slice (value: JsonElement) : CurrentSlice =
                closed [ "id"; "attempt"; "operation"; "issue"; "accountableOwner"; "owner"; "repository"; "paths"; "action"; "join"; "observation";
                         "pullRequest"; "pullRequestId"; "pullRequestUpdatedAt"; "head"; "prerequisites"; "prerequisitesComplete"; "intakeAuthorized" ] value
                let joined = value.GetProperty "join"
                closed [ "nativeId"; "updatedAt"; "bodySha256"; "sourceRevision" ] joined
                let issue = parseIssue (value.GetProperty "issue")
                let nativeId = boundedText "nativeId" 256 joined
                if (binding.SelectedIssues |> Map.tryFind nativeId) <> Some $"{issue.Owner}/{issue.Repository}#{issue.Number}" then
                    invalidArg "input" "slice is outside exact enrollment"
                let prerequisites =
                    values 128 (value.GetProperty "prerequisites") |> List.map (fun prerequisite ->
                        closed [ "action"; "reference"; "satisfied"; "supplyingOwner" ] prerequisite
                        { Action = action (text "action" prerequisite); Reference = boundedText "reference" 2048 prerequisite
                          Satisfied = optional (fun entry -> entry.GetBoolean()) (prerequisite.GetProperty "satisfied")
                          SupplyingOwner = boundedText "supplyingOwner" 512 prerequisite })
                { Id = boundedText "id" 128 value; Attempt = boundedText "attempt" 128 value; Operation = operation (text "operation" value)
                  Issue = issue; AccountableOwner = boundedText "accountableOwner" 4096 value; Owner = boundedText "owner" 512 value
                  Repository = repository value; Paths = strings 128 1024 (value.GetProperty "paths"); Action = action (text "action" value)
                  Join = { NativeId = nativeId; UpdatedAt = boundedText "updatedAt" 128 joined; BodySha256 = boundedText "bodySha256" 64 joined; SourceRevision = boundedText "sourceRevision" 16384 joined }
                  Observation = observation (value.GetProperty "observation"); PullRequest = optionalText (value.GetProperty "pullRequest")
                  PullRequestId = optionalText (value.GetProperty "pullRequestId"); PullRequestUpdatedAt = optionalText (value.GetProperty "pullRequestUpdatedAt"); Head = optionalText (value.GetProperty "head")
                  Prerequisites = prerequisites; PrerequisitesComplete = value.GetProperty("prerequisitesComplete").GetBoolean()
                  IntakeAuthorized = optional (fun entry -> entry.GetBoolean()) (value.GetProperty "intakeAuthorized") }
            let slices = values 64 (root.GetProperty "slices") |> List.map slice
            if slices.IsEmpty || (slices |> List.map _.Id |> Set.ofList |> Set.count) <> slices.Length then invalidArg "input" "unique nonempty slice identities required"
            let capacity =
                optional (fun value ->
                    closed [ "meaning"; "implementation"; "review"; "hostedCi"; "observation" ] value
                    let count (name: string) =
                        optional (fun entry ->
                            let result = entry.GetInt32()
                            if result < 0 || result > 10000 then invalidArg "input" "bounded nonnegative capacity required"
                            result) (value.GetProperty name)
                    boundedText "meaning" 128 value, count "implementation", count "review", count "hostedCi", observation (value.GetProperty "observation")) (root.GetProperty "capacity")
            let reservations =
                optional (fun value ->
                    closed [ "items"; "observation" ] value
                    let items =
                        values 256 (value.GetProperty "items") |> List.map (fun reservation ->
                            closed [ "sliceId"; "attempt"; "operation"; "owner"; "repository"; "paths"; "pullRequest"; "head"; "custodyReference" ] reservation
                            { SliceId = boundedText "sliceId" 128 reservation; Attempt = boundedText "attempt" 128 reservation
                              Operation = operation (text "operation" reservation); Owner = boundedText "owner" 512 reservation
                              Repository = repository reservation; Paths = strings 128 1024 (reservation.GetProperty "paths")
                              PullRequest = optionalText (reservation.GetProperty "pullRequest"); Head = optionalText (reservation.GetProperty "head")
                              CustodyReference = boundedText "custodyReference" 2048 reservation })
                    items, observation (value.GetProperty "observation")) (root.GetProperty "reservations")
            let context: SelectionContext =
                { Invocation = boundedText "invocation" 128 root; ProjectId = boundedText "projectId" 256 root; SchemaVersion = number "schemaVersion" root
                  RecipeRevision = boundedText "recipeRevision" 40 root; ArtifactSha256 = boundedText "artifactSha256" 64 root
                  PopulationRevision = boundedText "populationRevision" 40 root; EnrollmentBlob = boundedText "enrollmentBlob" 40 root
                  InputSha256 = SHA256.HashData(utf8.GetBytes json) |> Convert.ToHexStringLower
                  Slices = slices; CapacityMeaning = capacity |> Option.map (fun (meaning, _, _, _, _) -> meaning) |> Option.defaultValue "missing"
                  ImplementationSlots = capacity |> Option.bind (fun (_, count, _, _, _) -> count); ReviewSlots = capacity |> Option.bind (fun (_, _, count, _, _) -> count)
                  HostedCiSlots = capacity |> Option.bind (fun (_, _, _, count, _) -> count); CapacityObservation = capacity |> Option.map (fun (_, _, _, _, observed) -> observed)
                  Reservations = reservations |> Option.map fst; ReservationObservation = reservations |> Option.map snd
                  ObservedAt = timestamp "observedAt" root; ValidUntil = timestamp "validUntil" root; Evidence = boundedText "evidence" 2048 root }
            if text "schema" root <> "fsgg.coord.board-v2-integrator-observations/1" || context.ProjectId <> binding.ProjectId
               || context.SchemaVersion <> binding.SchemaVersion || context.RecipeRevision <> binding.RecipeRevision || context.ArtifactSha256 <> binding.ArtifactSha256
               || context.PopulationRevision <> binding.PopulationRevision then invalidArg "input" "exact organization binding required"
            Ok context
        with _ -> Error "invalid integrator observations: organization-only closed bounded schema and exact binding required; no native authority inferred"

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
               items = report.Items |> List.map (fun (item: ItemReport) ->
                   {| issue = issueWire item.Issue; native = item.Native |> Option.map nativeWire
                      dependencyObservations = item.DependencyObservations |> List.map nativeWire
                      dependencyReadComplete = item.DependencyReadComplete; planObservation = item.PlanObservation |> Option.map nativeWire
                      delivery = item.Delivery; publication = item.Publication; nativeAcceptance = item.NativeAcceptance
                      health = item.Health; reads = item.Reads; mutationAttempts = item.MutationAttempts; membershipPages = item.MembershipPages
                      projection = item.Projection |> Option.map projectionWire; gap = item.Gap |> Option.map Errors.explain
                      lastVerified = item.LastVerified |> Option.map projectionWire |}) |}

    let encodeInspectionWithContext (report: InspectionReport) (context: SelectionContext option) (now: DateTimeOffset) =
        let complete = report.Binding.BindingVersion = 4 && report.Evidence.PopulationGap.IsNone && report.MembershipGap.IsNone && report.NativeIntegrator.RepositoryGaps.IsEmpty && (report.NativeIntegrator.PullRequests |> List.forall (fun pull -> pull.Gaps.IsEmpty))
        let facts: IntegratorFacts =
            { OpenPullRequests = if complete then Some(report.NativeIntegrator.PullRequests |> List.filter (fun pull -> pull.State = "OPEN") |> List.map _.Url) else None
              TouchSets = if complete then Some(report.NativeIntegrator.PullRequests |> List.filter (fun pull -> pull.State = "OPEN") |> List.map (fun pull -> pull.Url, pull.Files) |> Map.ofList) else None
              AvailableSlots = None }
        let actionName = function Source -> "source" | Activation -> "activation"
        let operationName = function
            | ObserveExisting -> "observe-existing" | Implement -> "implement" | ReviewWork -> "review"
            | QualifyWork -> "qualify" | ActivateWork -> "activate"
        let observationWire (value: AttributedObservation) =
            {| provenance = "integrator-reported"; supplier = value.Supplier; observedAt = value.ObservedAt; validUntil = value.ValidUntil; evidence = value.Evidence |}
        let planningWire (value: PlanningObservation) =
            {| itemId = value.ItemId; status = value.Status; roadmap = value.Roadmap; track = value.Track; observation = value.Observation; membershipPages = value.MembershipPages |}
        let common =
            {| schema = "fsgg.coord.board-v2-inspection/1"; bindingVersion = report.Binding.BindingVersion; organizationId = report.Binding.OrganizationId
               owner = report.Binding.Owner; projectNumber = report.Binding.ProjectNumber; projectId = report.Binding.ProjectId; projectTitle = report.Binding.ProjectTitle
               schemaVersion = report.Binding.SchemaVersion; ownerKind = "organization"; repositories = report.Binding.Repositories |> Set.toList
               status = {| id = report.Binding.Status.Id; options = report.Binding.Status.Options |}; roadmapFieldId = report.Binding.RoadmapFieldId
               track = {| id = report.Binding.Track.Id; options = report.Binding.Track.Options |}; observation = {| id = report.Binding.Observation.Id; options = report.Binding.Observation.Options |}
               selectedIssues = report.Binding.SelectedIssues; recipeRevision = report.Binding.RecipeRevision; artifactSha256 = report.Binding.ArtifactSha256
               populationRevision = report.Binding.PopulationRevision; importRecipeRevision = report.Binding.ImportRecipeRevision; importArtifactSha256 = report.Binding.ImportArtifactSha256
               protectedInputs = report.Evidence.ProtectedInputs; selected = report.Evidence.Selected; attempted = report.Evidence.Attempted; verified = report.Evidence.Verified
               mutationAttempts = 0; populationGap = report.Evidence.PopulationGap |> Option.map Errors.explain; cleanup = report.Evidence.Cleanup
               items = report.Items |> List.map (fun (item: InspectionItem) ->
                   {| issue = issueWire item.Evidence.Issue; expectedNodeId = item.ExpectedNodeId; native = item.Evidence.Native |> Option.map nativeWire
                      dependencyObservations = item.Evidence.DependencyObservations |> List.map nativeWire; dependencyReadComplete = item.Evidence.DependencyReadComplete
                      planObservation = item.Evidence.PlanObservation |> Option.map nativeWire; sourceCurrentness = item.SourceCurrentness
                      planning = item.Planning |> Option.map planningWire; planningGap = item.PlanningGap |> Option.map Errors.explain; discrepancies = item.Discrepancies
                      health = item.Evidence.Health; delivery = item.Evidence.Delivery; publication = item.Evidence.Publication; nativeAcceptance = item.Evidence.NativeAcceptance
                      reads = item.Evidence.Reads; mutationAttempts = 0; gap = item.Evidence.Gap |> Option.map Errors.explain; lastVerified = item.Evidence.LastVerified |> Option.map projectionWire |})
               candidates = planningCandidates report facts |> List.map (fun candidate ->
                   {| issue = issueWire candidate.Issue; humanStatus = candidate.HumanStatus; track = candidate.Track; roadmap = candidate.Roadmap
                      sourceCurrentness = candidate.SourceCurrentness; observation = candidate.Observation; unmetOrUnknown = candidate.UnmetOrUnknown
                      openPullRequests = candidate.Integrator.OpenPullRequests; touchSets = candidate.Integrator.TouchSets; availableSlots = candidate.Integrator.AvailableSlots |}) |}

        if report.Binding.BindingVersion <> 4 then JsonSerializer.Serialize(common)
        else
            let successor =
                {| membershipGap = report.MembershipGap |> Option.map Errors.explain
                   memberships = report.Memberships |> List.map (fun memberItem ->
                       {| itemId = memberItem.ItemId; contentId = memberItem.ContentId; kind = memberItem.Kind
                          issue = memberItem.Issue |> Option.map issueWire; enrolled = memberItem.Enrolled
                          discrepancy = if memberItem.Enrolled then None else Some "Unexpected/unselected membership; no automatic enrollment" |})
                   nativeIntegrator = {| observedAt = (if report.NativeIntegrator.ObservedAt = DateTimeOffset.MinValue then None else Some report.NativeIntegrator.ObservedAt); repositoryGaps = report.NativeIntegrator.RepositoryGaps
                                         pullRequests = report.NativeIntegrator.PullRequests
                                         resourceGap = if context.IsNone then Some "Missing runtime/integrator observations; no capacity inferred from collaboration slots" else None |}
                   integratorObservation = context |> Option.map (fun value ->
                       {| provenance = "integrator-reported"; invocation = value.Invocation; inputSha256 = value.InputSha256
                          observedAt = value.ObservedAt; validUntil = value.ValidUntil; evidence = value.Evidence
                          capacityMeaning = value.CapacityMeaning; capacityObservation = value.CapacityObservation |> Option.map observationWire
                          reservationObservation = value.ReservationObservation |> Option.map observationWire
                          capacity = {| implementation = value.ImplementationSlots; review = value.ReviewSlots; hostedCi = value.HostedCiSlots |}
                          reservations = value.Reservations |> Option.map (List.map (fun hold ->
                              {| sliceId = hold.SliceId; attempt = hold.Attempt; operation = operationName hold.Operation; owner = hold.Owner
                                 repository = hold.Repository; paths = hold.Paths; pullRequest = hold.PullRequest; head = hold.Head; custodyReference = hold.CustodyReference |}))
                          slices = value.Slices |> List.map (fun slice -> {| id = slice.Id; attempt = slice.Attempt; accountableOwner = slice.AccountableOwner; observation = observationWire slice.Observation |}) |})
                   selectionCoverage = if context.IsNone then "Partial .8: native observations available; attributed action/prerequisite/intake/resource/reservation suppliers are missing" else "Attributed ephemeral integrator observations supplied; per-slice results retain native join failures and explicit ordered advisory selection; no allocation/dispatch authority"
                   selection = selectActions report context now |> List.map (fun selected ->
                       {| sliceId = selected.SliceId; attempt = selected.Attempt; operation = selected.Operation |> Option.map operationName
                          issue = issueWire selected.Issue; action = actionName selected.Action; decision = selected.Decision
                          owner = selected.Owner; pullRequest = selected.PullRequest; head = selected.Head; reasons = selected.Reasons; evidence = selected.Evidence
                          resourceDemand = selected.Operation |> Option.bind (function
                              | ObserveExisting -> Some {| implementation = 0; review = 0; hostedCi = 0 |}
                              | Implement -> Some {| implementation = 1; review = 0; hostedCi = 0 |}
                              | ReviewWork -> Some {| implementation = 0; review = 1; hostedCi = 0 |}
                              | QualifyWork -> Some {| implementation = 0; review = 0; hostedCi = 1 |}
                              | ActivateWork -> None)
                          dispatchAuthority = false |}) |}
            let wire = JsonSerializer.SerializeToNode(common)
            wire.["schema"] <- JsonSerializer.SerializeToNode("fsgg.coord.board-v2-inspection/2")
            let extension = JsonSerializer.SerializeToNode(successor)
            for name in [ "membershipGap"; "memberships"; "nativeIntegrator"; "integratorObservation"; "selectionCoverage"; "selection" ] do
                let value = extension.[name]
                wire.[name] <- if isNull value then null else value.DeepClone()
            let itemNodes = wire.["items"].AsArray()
            report.Items |> List.iteri (fun index (item: InspectionItem) ->
                let itemNode = itemNodes.[index]
                itemNode.["sourceRevision"] <- JsonSerializer.SerializeToNode(item.SourceRevision)
                itemNode.["accountableOwner"] <- JsonSerializer.SerializeToNode(item.Owner)
                itemNode.["currentDeliverable"] <- JsonSerializer.SerializeToNode({| provenance = "native-observed"; references = item.Deliverables; gap = item.DeliverableGap |})
                itemNode.["nativeIntake"] <- JsonSerializer.SerializeToNode(
                    {| provenance = if item.Intake.ObservedAt = DateTimeOffset.MinValue then "missing" else "native-observed"
                       observedAt = if item.Intake.ObservedAt = DateTimeOffset.MinValue then None else Some item.Intake.ObservedAt
                       admission = item.Intake.Admission; allowed = item.Intake.Allowed; gap = item.Intake.Gap |> Option.map Errors.explain |}))
            wire.ToJsonString()

    let encodeInspection report = encodeInspectionWithContext report None DateTimeOffset.UtcNow

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

    let private parseSyntax arguments =
        let rec flags values = function
            | [] ->
                match Map.tryFind "--binding-file" values, Map.tryFind "--report-file" values with
                | Some binding, Some report when binding <> report && Map.tryFind "--previous-report-file" values <> Some report ->
                    Ok(binding, Map.tryFind "--previous-report-file" values, report, Map.containsKey "--integrator-stdin" values)
                | _ -> Error "refresh requires distinct --binding-file PATH and --report-file PATH"
            | "--integrator-stdin" :: rest when not (Map.containsKey "--integrator-stdin" values) -> flags (Map.add "--integrator-stdin" "true" values) rest
            | flag :: value :: rest when List.contains flag [ "--binding-file"; "--previous-report-file"; "--report-file" ] && not (Map.containsKey flag values) && not (String.IsNullOrWhiteSpace value) && not (value.StartsWith("--", StringComparison.Ordinal)) -> flags (Map.add flag value values) rest
            | _ -> Error "unknown, duplicate or incomplete refresh argument"
        match arguments with
        | "refresh" :: rest when rest |> List.contains "--integrator-stdin" -> Error "integrator observations are read-only organization inspect input"
        | ("refresh" | "inspect") :: rest -> flags Map.empty rest
        | _ -> Error "expected board-v2 refresh or inspect"

    let validateInvocation arguments =
        match arguments with
        | "board-v2" :: rest -> parseSyntax rest |> Result.map ignore |> Some
        | _ -> None

    let private parseArguments arguments =
        parseSyntax arguments |> Result.bind (fun (binding, previous, report, integratorInput) ->
            let bindingPath = Path.GetFullPath binding
            let reportPath = Path.GetFullPath report
            let previousPath = previous |> Option.map Path.GetFullPath
            if bindingPath = reportPath || previousPath = Some reportPath || File.Exists reportPath then
                Error "report output must be a new path distinct from all inputs"
            else Ok(bindingPath, previousPath, reportPath, integratorInput))

    let private readIntegratorInput (reader: TextReader) (binding: Binding) =
        try
            let buffer = Array.zeroCreate<char> 262145
            let count = reader.ReadBlock(buffer, 0, buffer.Length)
            if count = buffer.Length then Error "integrator stdin exceeds its bounded single-document limit"
            else parseIntegratorObservations binding (String(buffer, 0, count))
        with _ -> Error "could not read bounded integrator stdin; no native authority inferred"

    let private prepare (reader: TextReader) arguments =
        (try parseArguments arguments with error -> Error("invalid input path: " + error.Message)) |> Result.bind (fun (bindingPath, previousPath, reportPath, integratorInput) ->
            try
                parseBinding (File.ReadAllText bindingPath) |> Result.bind (fun binding ->
                    use stream = File.OpenRead typeof<Binding>.Assembly.Location
                    let actual = SHA256.HashData stream |> Convert.ToHexString |> _.ToLowerInvariant()
                    if not (String.Equals(actual, binding.ArtifactSha256, StringComparison.OrdinalIgnoreCase)) then Error "selected binding does not match the loaded adapter assembly SHA-256"
                    else
                        let previous = match previousPath with None -> Ok None | Some path -> decodePreviousReport (File.ReadAllText path) |> Result.map Some
                        previous |> Result.bind (fun history ->
                            let context =
                                if integratorInput && binding.BindingVersion <> 4 then Error "integrator stdin is restricted to organization binding 4 inspection"
                                elif integratorInput then readIntegratorInput reader binding |> Result.map Some
                                else Ok None
                            context |> Result.map (fun supplied -> binding, history, reportPath, supplied)))
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
    let private execute (transport: IGitHubTransport) ((binding: Binding), previous, reportPath, _) =
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
    let private executeInspection transport (binding, previous, reportPath, context) =
        try
            let report = V2ProjectionSource.inspectFixed (bounded transport) binding previous
            File.WriteAllText(reportPath, encodeInspectionWithContext report context DateTimeOffset.UtcNow + Environment.NewLine)
            if report.Evidence.PopulationGap.IsSome || report.MembershipGap.IsSome || (report.Items |> List.exists (fun item -> item.SourceCurrentness <> "Current" || item.PlanningGap.IsSome)) then 3 else 0
        with error -> refuse ("inspection/report failed: " + error.Message)

    let runWithTransportAndInput transport (reader: TextReader) arguments =
        match prepare reader arguments with
        | Error message -> refuse message
        | Ok input ->
            match arguments with
            | "inspect" :: _ -> executeInspection transport input
            | _ -> execute transport input
    let runWithTransport transport arguments = runWithTransportAndInput transport Console.In arguments

    let tryRun arguments =
        match arguments with
        | "board-v2" :: rest ->
            Some(match prepare Console.In rest with
                 | Error message -> refuse message
                 | Ok input ->
                     let binding, _, _, _ = input
                     let token =
                         let primary = Environment.GetEnvironmentVariable "GITHUB_TOKEN"
                         if String.IsNullOrWhiteSpace primary then Environment.GetEnvironmentVariable "GH_TOKEN" else primary
                     let live =
                         match rest with
                         | "inspect" :: _ -> V2ProjectionSource.createInspectionTransport binding token
                         | _ -> V2ObservationTransport.createLive binding token
                     match live with
                     | Error error -> refuse (Errors.explain error)
                     | Ok(transport, lifetime) ->
                         use lifetime = lifetime
                         match rest with "inspect" :: _ -> executeInspection transport input | _ -> execute transport input)
        | _ -> None
