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

    let parseBinding json = V2Projection.decodeBinding json

    // JSON is an attributed caller report, never authentication or native acceptance. All groups
    // retain their original observation interval; decoding/serialization cannot renew a fact.
    let parseIntegratorObservations (binding: Binding) (json: string) : Result<SelectionContext, string> =
        try
            let utf8 = System.Text.UTF8Encoding(false, true)
            if not (isProgrammeBinding binding) || utf8.GetByteCount json > 262144 then
                invalidArg "input" "organization-only bounded input"
            let options = JsonDocumentOptions(MaxDepth = 12)
            use document = JsonDocument.Parse(json, options)
            let root = document.RootElement
            closed [ "schema"; "invocation"; "projectId"; "schemaVersion"; "recipeRevision"; "artifactSha256"; "populationRevision"; "enrollmentBlob"; "enrollmentCommitmentSha256";
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
                closed ([ "nativeId"; "updatedAt"; "bodySha256"; "sourceRevision" ] @ (if binding.BindingVersion = 5 then ["hierarchySha256"] else [])) joined
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
                let hierarchySha256 =
                    if binding.BindingVersion = 5 then
                        let hash = boundedText "hierarchySha256" 64 joined
                        if hash.Length <> 64 || hash |> Seq.exists (Char.IsAsciiHexDigitLower >> not) then
                            invalidArg "join" "invalid hierarchy digest"
                        Some hash
                    else None
                { Id = boundedText "id" 128 value; Attempt = boundedText "attempt" 128 value; Operation = operation (text "operation" value)
                  Issue = issue; AccountableOwner = boundedText "accountableOwner" 4096 value; Owner = boundedText "owner" 512 value
                  Repository = repository value; Paths = strings 128 1024 (value.GetProperty "paths"); Action = action (text "action" value)
                  Join = { NativeId = nativeId; UpdatedAt = boundedText "updatedAt" 128 joined; BodySha256 = boundedText "bodySha256" 64 joined; SourceRevision = boundedText "sourceRevision" 16384 joined
                           HierarchySha256 = hierarchySha256 }
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
                  PopulationRevision = boundedText "populationRevision" 40 root; EnrollmentBlob = boundedText "enrollmentBlob" 40 root; EnrollmentCommitment = boundedText "enrollmentCommitmentSha256" 64 root
                  InputSha256 = SHA256.HashData(utf8.GetBytes json) |> Convert.ToHexStringLower
                  Slices = slices; CapacityMeaning = capacity |> Option.map (fun (meaning, _, _, _, _) -> meaning) |> Option.defaultValue "missing"
                  ImplementationSlots = capacity |> Option.bind (fun (_, count, _, _, _) -> count); ReviewSlots = capacity |> Option.bind (fun (_, _, count, _, _) -> count)
                  HostedCiSlots = capacity |> Option.bind (fun (_, _, _, count, _) -> count); CapacityObservation = capacity |> Option.map (fun (_, _, _, _, observed) -> observed)
                  Reservations = reservations |> Option.map fst; ReservationObservation = reservations |> Option.map snd
                  ObservedAt = timestamp "observedAt" root; ValidUntil = timestamp "validUntil" root; Evidence = boundedText "evidence" 2048 root }
            if text "schema" root <> (if binding.BindingVersion = 5 then "fsgg.coord.board-v2-integrator-observations/2" else "fsgg.coord.board-v2-integrator-observations/1") || context.ProjectId <> binding.ProjectId
               || context.SchemaVersion <> binding.SchemaVersion || context.RecipeRevision <> binding.RecipeRevision || context.ArtifactSha256 <> binding.ArtifactSha256
               || context.PopulationRevision <> binding.PopulationRevision
               || (binding.ProgrammeEnrollment |> Option.bind (fun value -> V2Projection.programmeCommitment value |> Result.toOption)) <> Some context.EnrollmentCommitment then invalidArg "input" "exact organization binding required"
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
        let complete = isProgrammeBinding report.Binding && report.Evidence.PopulationGap.IsNone && report.MembershipGap.IsNone && report.NativeIntegrator.RepositoryGaps.IsEmpty && (report.NativeIntegrator.PullRequests |> List.forall (fun pull -> pull.Gaps.IsEmpty && pull.GapDetails.IsEmpty))
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

        if not (isProgrammeBinding report.Binding) then JsonSerializer.Serialize(common)
        else
            let successor =
                {| enrollmentCommitmentSha256 = report.Evidence.ProtectedInputs |> List.tryPick (fun (path, _, digest) -> if path = "programme-enrollment/v3-commitment" then Some digest else None)
                   membershipGap = report.MembershipGap |> Option.map Errors.explain
                   memberships = report.Memberships |> List.map (fun memberItem ->
                       {| itemId = memberItem.ItemId; contentId = memberItem.ContentId; kind = memberItem.Kind
                          issue = memberItem.Issue |> Option.map issueWire; enrolled = memberItem.Enrolled; archived = memberItem.Archived
                          discrepancy =
                            if memberItem.Archived then Some V2ProjectionSource.archivedCardDiscrepancy
                            elif memberItem.Enrolled then None
                            else Some "Unexpected/unselected membership; no automatic enrollment" |})
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
            wire.["schema"] <- JsonSerializer.SerializeToNode(if report.Binding.BindingVersion = 5 then "fsgg.coord.board-v2-inspection/3" else "fsgg.coord.board-v2-inspection/2")
            let extension = JsonSerializer.SerializeToNode(successor)
            for name in [ "enrollmentCommitmentSha256"; "membershipGap"; "memberships"; "nativeIntegrator"; "integratorObservation"; "selectionCoverage"; "selection" ] do
                let value = extension.[name]
                wire.[name] <- if isNull value then null else value.DeepClone()
            let itemNodes = wire.["items"].AsArray()
            report.Items |> List.iteri (fun index (item: InspectionItem) ->
                let itemNode = itemNodes.[index]
                if report.Binding.BindingVersion = 5 then
                    itemNode.["workItem"] <- JsonSerializer.SerializeToNode(item.WorkItem |> Option.map (fun facts ->
                        {| workLevel = facts.WorkLevel; workLevelOptionId = facts.WorkLevelOptionId
                           hierarchy = {| parent = facts.Parent |> Option.map (fun value -> {| issue = issueWire value.Issue; nodeId = value.NodeId |})
                                          children = facts.Children |> List.map (fun value -> {| issue = issueWire value.Issue; nodeId = value.NodeId; state = value.State; stateReason = value.StateReason; closedAt = value.ClosedAt |})
                                          complete = facts.Complete; gap = facts.Gap; observedAt = facts.ObservedAt; sha256 = facts.Sha256 |}
                           closure = {| stateReason = facts.StateReason; closedAt = facts.ClosedAt; doneWhen = facts.DoneWhen; blockedOn = facts.BlockedOn; acceptanceEvidence = facts.AcceptanceEvidence; acceptance = "Unverified"; gaps = facts.ClosureGaps |} |}))
                itemNode.["sourceRevision"] <- JsonSerializer.SerializeToNode(item.SourceRevision)
                itemNode.["accountableOwner"] <- JsonSerializer.SerializeToNode(item.Owner)
                itemNode.["currentDeliverable"] <- JsonSerializer.SerializeToNode({| provenance = "native-observed"; declaration = item.CurrentDeliverable; references = item.Deliverables; gap = item.DeliverableGap; reconciliationRequired = (item.CurrentDeliverable |> Option.exists (fun value -> value <> "None")) |})
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

    type private MigrationCommand =
        | MigrationPrepare of scopePath: string * reportPath: string
        | MigrationApply of preparedPath: string * expectedSha256: string * accountableOwner: string * selectionReference: string * windowUntil: DateTimeOffset
        | MigrationReadback of preparedPath: string

    let private migrationSyntax (arguments: string list) : Result<MigrationCommand, string> =
        let rec flags (values: Map<string, string>) (remaining: string list) =
            match remaining with
            | [] -> Ok values
            | flag :: value :: rest when not (values.ContainsKey flag) && not (value.StartsWith("--", StringComparison.Ordinal)) ->
                flags (values.Add(flag, value)) rest
            | _ -> Error "migration requires unique explicit named arguments"
        match arguments with
        | command :: rest ->
            flags Map.empty rest |> Result.bind (fun values ->
                let exact names = Set.ofList names = (values |> Map.keys |> Set.ofSeq)
                let path name = Path.GetFullPath values[name]
                match command with
                | "migration-prepare" when exact [ "--scope-file"; "--report-file" ] -> Ok(MigrationPrepare(path "--scope-file", path "--report-file"))
                | "migration-readback" when exact [ "--prepared-file" ] -> Ok(MigrationReadback(path "--prepared-file"))
                | "migration-apply" when exact [ "--prepared-file"; "--prepared-sha256"; "--accountable-owner"; "--selection-reference"; "--planning-window-until" ] ->
                    let sha = values["--prepared-sha256"]
                    let owner = values["--accountable-owner"]
                    let reference = values["--selection-reference"]
                    let mutable expiry = DateTimeOffset.MinValue
                    if sha.Length <> 64 || not (Seq.forall Uri.IsHexDigit sha)
                       || String.IsNullOrWhiteSpace owner || owner.Length > 512 || (owner |> Seq.exists Char.IsControl)
                       || String.IsNullOrWhiteSpace reference || reference.Length > 2048 || (reference |> Seq.exists Char.IsControl)
                       || not (DateTimeOffset.TryParse(values["--planning-window-until"], Globalization.CultureInfo.InvariantCulture, Globalization.DateTimeStyles.None, &expiry)) then
                        Error "migration requires exact prepared digest, accountable selection and bounded planning window"
                    else Ok(MigrationApply(path "--prepared-file", sha.ToLowerInvariant(), owner, reference, expiry))
                | _ -> Error "unsupported migration command or argument; no effects admitted")
        | [] -> Error "missing migration command"

    // Private sibling that retains a refused migration-prepare's typed, bounded reason.
    let private migrationPrepareFailurePath (reportPath: string) = reportPath + ".prepare-failure.json"
    let private migrationDigest (bytes: byte array) = SHA256.HashData bytes |> Convert.ToHexString |> _.ToLowerInvariant()
    let private privatePath (path: string) =
        if OperatingSystem.IsWindows() then invalidArg "path" "this private evidence route requires Unix permission verification"
        let mutable current = Path.GetFullPath path
        while not (String.IsNullOrEmpty current) do
            if File.Exists current || Directory.Exists current then
                if File.GetAttributes(current).HasFlag FileAttributes.ReparsePoint then invalidArg "path" "symlink/reparse evidence path refused"
            current <- Path.GetDirectoryName current
        let parent = Path.GetDirectoryName(Path.GetFullPath path)
        if not (Directory.Exists parent) then invalidArg "path" "existing private parent directory required"
        let mode = File.GetUnixFileMode parent
        let others = UnixFileMode.GroupRead ||| UnixFileMode.GroupWrite ||| UnixFileMode.GroupExecute ||| UnixFileMode.OtherRead ||| UnixFileMode.OtherWrite ||| UnixFileMode.OtherExecute
        if mode &&& others <> enum<UnixFileMode> 0 then invalidArg "path" "evidence parent directory must be private"
    let private privateRead (limit: int64) (path: string) =
        privatePath path
        let info = FileInfo path
        if not info.Exists || info.Attributes.HasFlag FileAttributes.Directory || info.Attributes.HasFlag FileAttributes.Device
           || info.Length <= 0L || info.Length > limit then invalidArg "path" "bounded regular input required"
        use stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)
        use captured = new MemoryStream()
        let buffer = Array.zeroCreate<byte> 8192
        let mutable count = stream.Read(buffer, 0, buffer.Length)
        while count > 0 do
            if captured.Length + int64 count > limit then invalidArg "input" "bounded input exceeded"
            captured.Write(buffer, 0, count)
            count <- stream.Read(buffer, 0, buffer.Length)
        captured.ToArray()
    module private MigrationDirectoryNative =
        [<System.Runtime.InteropServices.DllImport("libc", EntryPoint = "open", SetLastError = true)>]
        extern int openDirectory(string path, int flags)
        [<System.Runtime.InteropServices.DllImport("libc", SetLastError = true)>]
        extern int fsync(int descriptor)
        [<System.Runtime.InteropServices.DllImport("libc", SetLastError = true)>]
        extern int close(int descriptor)

    let private syncMigrationDirectory (path: string) =
        if not (OperatingSystem.IsLinux())
           || System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture <> System.Runtime.InteropServices.Architecture.X64 then
            raise (IOException "supported directory durability barrier unavailable")
        // Linux O_DIRECTORY | O_CLOEXEC | O_NOFOLLOW; no fallback claims durability.
        let descriptor = MigrationDirectoryNative.openDirectory(path, 0x10000 ||| 0x80000 ||| 0x20000)
        if descriptor < 0 then raise (IOException "directory durability barrier unavailable")
        try
            if MigrationDirectoryNative.fsync descriptor <> 0 then raise (IOException "directory durability barrier unavailable")
        finally
            MigrationDirectoryNative.close descriptor |> ignore

    let private privateWrite (syncDirectory: string -> unit) (path: string) (bytes: byte array) =
        privatePath path
        let options = FileStreamOptions(Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None,
                                        Options = FileOptions.WriteThrough, UnixCreateMode = Nullable(UnixFileMode.UserRead ||| UnixFileMode.UserWrite))
        use stream = new FileStream(path, options)
        stream.Write(bytes, 0, bytes.Length)
        stream.Flush true
        syncDirectory (Path.GetDirectoryName path)
    let private privateJson (syncDirectory: string -> unit) (path: string) (json: string) = privateWrite syncDirectory path (System.Text.UTF8Encoding(false, true).GetBytes json)
    let private migrationIdentity (scope: V2Migration.Scope) =
        let adapter = typeof<V2Migration.Scope>.Assembly
        let cli = System.Reflection.Assembly.GetExecutingAssembly()
        let sha (assembly: System.Reflection.Assembly) =
            use stream = File.OpenRead assembly.Location
            SHA256.HashData stream |> Convert.ToHexString |> _.ToLowerInvariant()
        let revision (assembly: System.Reflection.Assembly) =
            let attribute = assembly.GetCustomAttributes(typeof<System.Reflection.AssemblyInformationalVersionAttribute>, false)
            if attribute.Length <> 1 then None
            else
                let value = (attribute[0] :?> System.Reflection.AssemblyInformationalVersionAttribute).InformationalVersion
                let suffix = value.Split('+') |> Array.tryLast
                suffix |> Option.filter (fun text -> text.Length = 40 && Seq.forall Uri.IsHexDigit text)
        if sha adapter <> scope.ArtifactSha256 || sha cli <> scope.CliArtifactSha256
           || revision adapter <> Some scope.SourceRevision || revision cli <> Some scope.SourceRevision then
            Error "selected source/adapter/CLI identity differs from the actual executing assemblies"
        else Ok()
    let private migrationFresh (connection: V2MigrationTransport.IConnection) (scope: V2Migration.Scope) =
        V2Migration.collect connection.Reads scope |> Result.bind (V2Migration.prepare scope)
    let private migrationStable (connection: V2MigrationTransport.IConnection) (scope: V2Migration.Scope) =
        migrationFresh connection scope |> Result.bind (fun first ->
            migrationFresh connection scope |> Result.bind (fun final ->
                if first.Digest <> final.Digest then Error(Errors.Malformed("migration", "determining native facts changed during preparation"))
                else Ok final))
    let private migrationErrorClass (error: Errors.IoError) =
        match error with
        | Errors.RateLimited _ -> "rate-limited"
        | Errors.Unauthorized _ -> "unauthorized"
        | Errors.NotFound _ -> "not-found"
        | Errors.Malformed _ -> "malformed-or-refused"
        | Errors.GraphQlErrors _ -> "graphql-errors"
        | Errors.Partial _ -> "partial"
        | Errors.Transport _ -> "transport"
        | Errors.Http _ -> "http"
    let private migrationFailureReason (error: Errors.IoError) =
        match error with
        | Errors.Malformed("Project 3 migration transport", detail) ->
            let known =
                Set.ofList [ "response acquisition exceeded 4 MiB after possible send; Unknown; readback only";
                             "shared remaining deadline expired after possible send; Unknown; readback only";
                             "bounded response acquisition failed after possible send; Unknown; readback only";
                             "shared request/deadline ceiling reached; no dispatch";
                             "request exceeds 256 KiB; not sent" ]
            if known.Contains detail then detail else "transport-refusal"
        | _ -> migrationErrorClass error
    let private migrationResponse (change: V2Migration.Change) (request: Transport.Request) (response: Transport.Response) =
        let name =
            match change with
            | V2Migration.CreateWorkLevel -> "createProjectV2Field"
            | V2Migration.AddInReview _ -> "updateProjectV2Field"
            | V2Migration.AddMembership _ -> "addProjectV2ItemById"
            | V2Migration.InitializePlanningValue _ -> "updateProjectV2ItemFieldValue"
            | V2Migration.CreateView _ -> "createProjectV2View"
            | V2Migration.UpdateView _ -> "updateProjectV2View"
        let expected =
            match request.Body with
            | Json json ->
                use document = JsonDocument.Parse json
                document.RootElement.GetProperty("variables").GetProperty("input").GetProperty("clientMutationId").GetString()
            | _ -> invalidOp "reconstructed migration request has no typed JSON body"
        if response.Status <> 200 then Error(Errors.Malformed("migration", "non-success native response; Unknown"))
        else
            V2MigrationTransport.validateClearEnvelope response.Body |> Result.bind (fun () ->
                GraphQl.decode "migration response" response.Body (fun data ->
                    try
                        let payload = data.GetProperty name
                        let identityName =
                            match change with
                            | V2Migration.CreateWorkLevel -> "projectV2Field"
                            | V2Migration.AddInReview _ -> "projectV2Field"
                            | V2Migration.AddMembership _ -> "item"
                            | V2Migration.InitializePlanningValue _ -> "projectV2Item"
                            | V2Migration.CreateView _ | V2Migration.UpdateView _ -> "projectV2View"
                        let identity = payload.GetProperty(identityName).GetProperty("id").GetString()
                        if payload.GetProperty("clientMutationId").GetString() = expected
                           && not (String.IsNullOrWhiteSpace identity) && identity.Length <= 256 then Ok identity
                        else Error(Errors.Malformed("migration", "mutation correlation or native result identity differs; Unknown"))
                    with _ -> Error(Errors.Malformed("migration", "mutation response is incomplete; Unknown"))))

    // This capability is private to the explicit manual CLI boundary. Owner/selection strings are
    // accountability bindings, not authentication. Actual authorized invocation supplies approval.
    type private MigrationAdmission =
        { Prepared: V2Migration.Prepared; Directory: string; WindowUntil: DateTimeOffset }

    // Resolves the shared writer lock for a project and a legacy lock name (board-reconciler design §8, #5126).
    // Production resolves V2WriterLock.production; every test seam injects a private directory instead.
    type private WriterLock = string -> string -> V2WriterLock.Location

    let private productionWriterLock : WriterLock =
        fun projectId legacyName ->
            match V2WriterLock.production projectId legacyName with
            | Ok location -> location
            | Error reason -> raise (IOException reason)

    let private injectedWriterLock (directory: string) : WriterLock = V2WriterLock.inDirectory directory

    let private executeMigration (writerLock: WriterLock) (connection: V2MigrationTransport.IConnection) (syncDirectory: string -> unit) (clock: TimeProvider) (command: MigrationCommand) =
        let privateWrite = privateWrite syncDirectory
        let privateJson = privateJson syncDirectory
        let utf8 = System.Text.UTF8Encoding(false, true)
        let mutable ownsInvocation = false
        // Prepare is read-only. Once it owns its new output identity, any refusal leaves a bounded
        // private failure record beside the absent or partial report instead of only an exit code.
        let mutable ownsPreparedOutput = false
        let mutable prepareStage = "output-preconditions"
        let mutable prepareError: Errors.IoError option = None
        let fail () =
            eprintfn "fsgg-coord-engine: board-v2 migration refused or incomplete; inspect private original invocation evidence; no automatic retry"
            3
        let invoke () =
            match command with
            | MigrationPrepare(scopePath, reportPath) ->
                let failurePath = migrationPrepareFailurePath reportPath
                if scopePath = reportPath || File.Exists reportPath || Directory.Exists reportPath || Directory.Exists(reportPath + ".invocation")
                   || File.Exists failurePath || Directory.Exists failurePath then invalidArg "path" "new distinct prepared output required"
                ownsPreparedOutput <- true
                let classify (error: Errors.IoError) =
                    prepareError <- Some error
                    migrationErrorClass error
                prepareStage <- "scope-read"
                let scopeJson = utf8.GetString(privateRead 262144L scopePath)
                prepareStage <- "scope-parse"
                V2Migration.parseScope scopeJson |> Result.mapError classify
                |> Result.bind (fun scope ->
                    prepareStage <- "executing-identity"
                    migrationIdentity scope |> Result.bind (fun () ->
                        prepareStage <- "native-preparation"
                        migrationStable connection scope |> Result.mapError classify))
                |> Result.map (fun prepared ->
                    prepareStage <- "prepared-report-write"
                    privateJson reportPath (V2Migration.encodePrepared ({ prepared with PreparedPath = Some reportPath }))
                    0)
            | MigrationApply(preparedPath, expectedHash, owner, selection, windowUntil) ->
                let raw = privateRead (4L * 1024L * 1024L) preparedPath
                if migrationDigest raw <> expectedHash then Error "selected-prepared-byte-digest-differs"
                else
                    V2Migration.decodePrepared (utf8.GetString raw) |> Result.mapError migrationErrorClass
                    |> Result.bind (fun (scope, digest, observedAt, boundPath) ->
                        let now = clock.GetUtcNow()
                        if not (String.Equals(boundPath, preparedPath, StringComparison.Ordinal)) then Error "original-prepared-custody-path-differs"
                        elif observedAt = DateTimeOffset.MinValue || observedAt > now || now - observedAt > TimeSpan.FromMinutes 30.0
                           || windowUntil <= now || windowUntil - now > TimeSpan.FromMinutes 5.0 then Error "prepared-or-planning-window-expired"
                        else
                            migrationIdentity scope |> Result.bind (fun () ->
                                let directory = preparedPath + ".invocation"
                                privatePath directory
                                // Exclusive marker, never removed. Reopening this identity is readback-only.
                                let reservationMetadata =
                                    {| preparedSha256 = expectedHash;
                                       accountableOwner = owner;
                                       selectionReference = selection |}
                                privateJson (preparedPath + ".invocation-reserved") (JsonSerializer.Serialize(reservationMetadata))
                                ownsInvocation <- true
                                if Directory.Exists directory || File.Exists directory then invalidArg "evidence" "retained invocation permits readback only"
                                Directory.CreateDirectory(directory, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute) |> ignore
                                syncDirectory directory
                                syncDirectory (Path.GetDirectoryName directory)
                                privateWrite (Path.Combine(directory, "prepared.json")) raw
                                let invocationMetadata =
                                    {| accountableOwner = owner;
                                       selectionReference = selection;
                                       preparedSha256 = expectedHash;
                                       sourceRevision = scope.SourceRevision;
                                       artifactSha256 = scope.ArtifactSha256;
                                       cliArtifactSha256 = scope.CliArtifactSha256;
                                       planningWindowUntil = windowUntil;
                                       requestCeiling = 500;
                                       secondsCeiling = 270 |}
                                privateJson (Path.Combine(directory, "invocation.json")) (JsonSerializer.Serialize(invocationMetadata))
                                migrationStable connection scope |> Result.mapError migrationErrorClass
                                |> Result.bind (fun fresh ->
                                    if fresh.Digest <> digest then Error "fresh-preconditions-differ-from-selected-preparation"
                                    elif not fresh.Snapshot.CanUpdate then Error "native-project-update-capability-absent"
                                    else
                                        let admission: MigrationAdmission = { Prepared = { fresh with PreparedPath = Some boundPath }; Directory = directory; WindowUntil = windowUntil }
                                        use lease = V2WriterLock.acquire (writerLock "PVT_kwDOEYAWY84Bldpa" V2WriterLock.migrationLegacyName)
                                        let rec effects (sequence: int) (expected: V2Migration.Prepared) =
                                            if clock.GetUtcNow() >= admission.WindowUntil then Error "planning-window-expired"
                                            else
                                                migrationFresh connection expected.Scope |> Result.mapError migrationErrorClass
                                                |> Result.bind (fun observed ->
                                                    let current = { observed with PreparedPath = expected.PreparedPath }
                                                    if current.Digest <> expected.Digest then Error "determining-facts-changed-before-effect"
                                                    else
                                                        match current.Changes with
                                                        | [] ->
                                                            privateJson (Path.Combine(directory, "completed.json")) (V2Migration.encodePrepared current)
                                                            Ok 0
                                                        | change :: _ ->
                                                            V2Migration.request current change |> Result.mapError migrationErrorClass
                                                            |> Result.bind (fun request ->
                                                                let prefix = Path.Combine(directory, sequence.ToString("D3", Globalization.CultureInfo.InvariantCulture))
                                                                let exact = match request.Body with Json json -> json | _ -> invalidOp "typed JSON request expected"
                                                                privateJson (prefix + "-precondition.json") (V2Migration.encodePrepared current)
                                                                if clock.GetUtcNow() >= admission.WindowUntil then invalidOp "planning window expired before send"
                                                                privateJson (prefix + "-intent.json") exact
                                                                let intentMetadata =
                                                                    {| method = request.Method;
                                                                       path = request.Path;
                                                                       budget = "GraphQl";
                                                                       requestSha256 = migrationDigest (Transport.canonicalMutationBytes request);
                                                                       bodySha256 = migrationDigest (utf8.GetBytes exact);
                                                                       bodyBytes = utf8.GetByteCount exact;
                                                                       originalPreparedSha256 = expectedHash;
                                                                       sequence = sequence |}
                                                                privateJson (prefix + "-intent-metadata.json") (JsonSerializer.Serialize(intentMetadata))
                                                                // Once this durable intent exists, every later exception remains uncertain.
                                                                if clock.GetUtcNow() >= admission.WindowUntil then invalidOp "planning window expired after custody before dispatch"
                                                                let outcome = connection.Dispatch(current, change, admission.WindowUntil)
                                                                let accepted =
                                                                    match outcome with
                                                                    | Ok response ->
                                                                        privateJson (prefix + "-response.json") response.Body
                                                                        let receiptMetadata =
                                                                            {| httpStatus = response.Status;
                                                                               responseSha256 = migrationDigest (utf8.GetBytes response.Body);
                                                                               responseBytes = utf8.GetByteCount response.Body |}
                                                                        privateJson (prefix + "-receipt.json") (JsonSerializer.Serialize(receiptMetadata))
                                                                        migrationResponse change request response
                                                                    | Error error ->
                                                                        let errorMetadata =
                                                                            {| state = "Unknown";
                                                                               errorClass = migrationErrorClass error;
                                                                               reason = migrationFailureReason error |}
                                                                        privateJson (prefix + "-error.json") (JsonSerializer.Serialize(errorMetadata))
                                                                        Error error
                                                                let rec observe (attempt: int) : Result<V2Migration.Prepared, string> =
                                                                    if clock.GetUtcNow() >= admission.WindowUntil || connection.RemainingTime <= TimeSpan.Zero then Error "Unknown-after-possible-send-observation-window-expired"
                                                                    else
                                                                        let observationReads = connection.ObservationReads admission.WindowUntil
                                                                        let observation =
                                                                            match accepted with
                                                                            | Ok returnedId -> V2Migration.collectEffectReadback observationReads current.Scope current.Snapshot change returnedId
                                                                            | Error _ -> V2Migration.collect observationReads current.Scope
                                                                        match observation with
                                                                        | Error _ -> Error "Unknown-after-possible-send-readback-unavailable"
                                                                        | Ok snapshot ->
                                                                            let observationPrefix =
                                                                                if attempt = 1 then prefix
                                                                                else prefix + "-visibility-" + attempt.ToString("D3", Globalization.CultureInfo.InvariantCulture)
                                                                            privateJson (observationPrefix + "-native-readback.json") (V2Migration.encodeReadback current.Scope snapshot)
                                                                            let prepared =
                                                                                V2Migration.resolveReadback current.Scope current.Snapshot snapshot change
                                                                                |> Result.bind (fun resolved -> V2Migration.prepare resolved snapshot)
                                                                                |> Result.map (fun (observed: V2Migration.Prepared) -> { observed with PreparedPath = current.PreparedPath })
                                                                            match prepared with
                                                                            | Ok after -> privateJson (observationPrefix + "-readback.json") (V2Migration.encodePrepared after)
                                                                            | Error _ -> ()
                                                                            if clock.GetUtcNow() >= admission.WindowUntil || connection.RemainingTime <= TimeSpan.Zero then Error "Unknown-after-possible-send-observation-window-expired"
                                                                            else
                                                                                match accepted with
                                                                                | Error _ -> Error "Unknown-original-outcome-retained-readback-only"
                                                                                | Ok returnedId ->
                                                                                    match V2Migration.observeTransition current.Scope current.Snapshot snapshot change returnedId with
                                                                                    | Ok(V2Migration.ObservedEffect resolved) ->
                                                                                        match prepared with
                                                                                        | Ok after when after.Scope = resolved -> Ok after
                                                                                        | _ -> Error "Unknown-original-outcome-retained-readback-only"
                                                                                    | Ok V2Migration.PendingVisibility when attempt < 7 ->
                                                                                        let delay = TimeSpan.FromSeconds([| 1.0; 1.0; 2.0; 4.0; 8.0; 14.0 |].[attempt - 1])
                                                                                        if min (admission.WindowUntil - clock.GetUtcNow()) connection.RemainingTime <= delay then
                                                                                            Error "Unknown-after-possible-send-observation-window-expired"
                                                                                        else
                                                                                            System.Threading.Tasks.Task.Delay(delay, clock, System.Threading.CancellationToken.None).GetAwaiter().GetResult()
                                                                                            observe (attempt + 1)
                                                                                    | Ok V2Migration.PendingVisibility -> Error "Unknown-visibility-observation-exhausted-original-intent-retained"
                                                                                    | Error _ -> Error "Unknown-original-outcome-retained-readback-only"
                                                                observe 1 |> Result.bind (effects (sequence + 1))))
                                        effects 1 admission.Prepared)))
            | MigrationReadback preparedPath ->
                let original = Path.Combine(preparedPath + ".invocation", "prepared.json")
                let raw = privateRead (4L * 1024L * 1024L) original
                V2Migration.decodePrepared (utf8.GetString raw) |> Result.mapError migrationErrorClass
                |> Result.bind (fun (scope, _, _, boundPath) ->
                    if not (String.Equals(boundPath, preparedPath, StringComparison.Ordinal)) then Error "original-readback-custody-path-differs"
                    else
                        migrationIdentity scope |> Result.bind (fun () ->
                            V2Migration.collect connection.Reads scope |> Result.mapError migrationErrorClass
                            |> Result.bind (fun first ->
                                V2Migration.collect connection.Reads scope |> Result.mapError migrationErrorClass
                                |> Result.bind (fun final ->
                                    let destination = Path.Combine(preparedPath + ".invocation", "readback-" + Guid.NewGuid().ToString("N") + ".json")
                                    privateJson destination (V2Migration.encodeReadback scope final)
                                    if V2Migration.snapshotDigest first <> V2Migration.snapshotDigest final then Error "readback-changed"
                                    else
                                        // Current state does not rewrite original response/unknown attribution.
                                        match V2Migration.prepare scope final with
                                        | Ok observed when observed.Changes.IsEmpty -> Ok 0
                                        | _ -> Ok 3))))
        let terminal (failureCode: string) (exceptionType: string option) =
            match command with
            | MigrationPrepare(_, reportPath) when ownsPreparedOutput ->
                try
                    let bounded (text: string) = if text.Length <= 256 then text else text.Substring(0, 256)
                    let failureMetadata =
                        {| schema = "fs.gg.board-v2.migration-prepare-failure/1";
                           state = "RefusedReadOnlyPreparation";
                           nativeMutationDispatched = false;
                           automaticRetry = false;
                           stage = prepareStage;
                           failureCode = bounded failureCode;
                           errorClass = (prepareError |> Option.map migrationErrorClass |> Option.toObj);
                           reason = (prepareError |> Option.map migrationFailureReason |> Option.toObj);
                           exceptionType = (exceptionType |> Option.map bounded |> Option.toObj);
                           preparedReportPresent = File.Exists reportPath;
                           failedAt = clock.GetUtcNow() |}
                    privateJson (migrationPrepareFailurePath reportPath) (JsonSerializer.Serialize(failureMetadata))
                with _ -> () // The exit code and stderr remain; no retry or rewrite follows.
            | MigrationApply(preparedPath, _, _, _, _) ->
                let directory = preparedPath + ".invocation"
                if ownsInvocation && Directory.Exists directory then
                    try
                        let possibleSend = Directory.EnumerateFiles(directory, "*-intent.json") |> Seq.isEmpty |> not
                        let terminalMetadata =
                            {| state = (if possibleSend then "Unknown" else "RefusedBeforeSend");
                               automaticRetry = false;
                               originalEvidenceRetained = true;
                               failureCode = failureCode |}
                        privateJson (Path.Combine(directory, "terminal.json")) (JsonSerializer.Serialize(terminalMetadata))
                    with _ -> () // Existing durable original intent remains the recovery anchor.
            | _ -> ()
            fail()
        try
            match invoke() with
            | Ok code -> code
            | Error code -> terminal code None
        with error -> terminal "invocation-io-or-unexpected-failure" (Some(error.GetType().FullName))

    let runMigrationWithCustody (lockDirectory: string) (connection: V2MigrationTransport.IConnection) (syncDirectory: string -> unit) (clock: TimeProvider) (arguments: string list) =
        try
            match migrationSyntax arguments with
            | Ok command -> executeMigration (injectedWriterLock lockDirectory) connection syncDirectory clock command
            | Error _ -> 1
        with _ -> 1

    let runMigrationWithConnection (lockDirectory: string) (connection: V2MigrationTransport.IConnection) (arguments: string list) =
        runMigrationWithCustody lockDirectory connection syncMigrationDirectory TimeProvider.System arguments

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
        | "board-v2" :: ((command :: _) as rest) when command.StartsWith("migration-", StringComparison.Ordinal) ->
            Some(try migrationSyntax rest |> Result.map ignore with _ -> Error "invalid migration argument")
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

    let private readBindingFile (path: string) =
        // One opened handle; bound before allocating from provider-reported file length.
        use stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)
        if stream.Length <= 0L || stream.Length > 262144L then invalidArg "binding" "binding-byte-bound"
        let buffer = Array.zeroCreate<byte> 262145
        let mutable count = 0
        let mutable finished = false
        while not finished && count < buffer.Length do
            let read = stream.Read(buffer, count, buffer.Length - count)
            if read = 0 then finished <- true else count <- count + read
        if count > 262144 then invalidArg "binding" "binding-byte-bound"
        System.Text.UTF8Encoding(false, true).GetString(buffer, 0, count)

    let private prepare (reader: TextReader) arguments =
        (try parseArguments arguments with error -> Error("invalid input path: " + error.Message)) |> Result.bind (fun (bindingPath, previousPath, reportPath, integratorInput) ->
            try
                parseBinding (readBindingFile bindingPath) |> Result.bind (fun binding ->
                    if isProgrammeBinding binding then
                        privatePath bindingPath
                        privatePath reportPath
                    use stream = File.OpenRead typeof<Binding>.Assembly.Location
                    let actual = SHA256.HashData stream |> Convert.ToHexString |> _.ToLowerInvariant()
                    if not (String.Equals(actual, binding.ArtifactSha256, StringComparison.OrdinalIgnoreCase)) then Error "selected binding does not match the loaded adapter assembly SHA-256"
                    else
                        let previous = match previousPath with None -> Ok None | Some path -> decodePreviousReport (File.ReadAllText path) |> Result.map Some
                        previous |> Result.bind (fun history ->
                            let context =
                                if integratorInput && not (isProgrammeBinding binding) then Error "integrator stdin is restricted to organization binding 4 inspection"
                                elif integratorInput then readIntegratorInput reader binding |> Result.map Some
                                else Ok None
                            context |> Result.map (fun supplied -> binding, history, reportPath, supplied)))
            with _ -> Error "could not read refresh input: bounded binding/history input unavailable")

    // Leave enough time for the existing 30 second transport timeout. Stop before dispatch,
    // without cancelling an in-flight writer; serialization is owned by the fixed job.
    // The request ceiling and the deadline scale with the selected population (#5119).
    let boundedWith (elapsed: unit -> TimeSpan) (binding: Binding) (transport: IGitHubTransport) =
        let rows = binding.SelectedIssues.Count
        let ceiling = V2Projection.refreshRequestCeiling rows
        let deadline = TimeSpan.FromSeconds(V2Projection.refreshDeadlineSeconds rows)
        let mutable calls = 0
        let dispatch subject action =
            if elapsed () >= deadline || calls >= ceiling then Error(Errors.Malformed(subject, "bounded refresh deadline/request ceiling reached; coverage Unknown"))
            else
                calls <- calls + 1
                action ()
        { new IGitHubTransport with
            member _.Send request = dispatch request.Subject (fun () -> transport.Send request)
            member _.SendMutation mutation = dispatch mutation.Request.Subject (fun () -> transport.SendMutation mutation)
            member _.RetryMutation effectId = Error(Errors.Malformed(effectId, "fixed refresh cannot replay durable mutation recipes")) }

    let private bounded (binding: Binding) (transport: IGitHubTransport) =
        let clock = Diagnostics.Stopwatch.StartNew()
        boundedWith (fun () -> clock.Elapsed) binding transport

    let private refuse message =
        eprintfn "fsgg-coord-engine: board-v2 refresh refused: %s" message
        1
    let private execute (writerLock: WriterLock) (transport: IGitHubTransport) ((binding: Binding), previous, reportPath, _) =
        try
            let report =
                use lease = V2WriterLock.acquire (writerLock binding.ProjectId (V2WriterLock.observationLegacyName binding.ProjectId))
                V2ProjectionSource.runFixed (bounded binding transport) binding previous
            let evidence = match box transport with :? V2ObservationTransport.IDispatchEvidence as evidence -> evidence.DispatchReceipts | _ -> []
            let report = { report with NativeDispatch = evidence; Cleanup = "Root-local project exclusive OS handle released; no distributed lease or background work" }
            let json = encodeReport report + Environment.NewLine
            if isProgrammeBinding binding then privateJson syncMigrationDirectory reportPath json
            else File.WriteAllText(reportPath, json)
            if report.PopulationGap.IsSome || report.Selected <> Some report.Verified then 3 else 0
        with error -> refuse (if isProgrammeBinding binding then "private refresh/report failed" else "refresh/report failed: " + error.Message)
    let private executeInspection transport (binding, previous, reportPath, context) =
        try
            let report = V2ProjectionSource.inspectFixed (bounded binding transport) binding previous
            let json = encodeInspectionWithContext report context DateTimeOffset.UtcNow + Environment.NewLine
            if isProgrammeBinding binding then privateJson syncMigrationDirectory reportPath json
            else File.WriteAllText(reportPath, json)
            if report.Evidence.PopulationGap.IsSome || report.MembershipGap.IsSome || (report.Items |> List.exists (fun item -> item.SourceCurrentness <> "Current" || item.PlanningGap.IsSome)) then 3 else 0
        with error -> refuse (if isProgrammeBinding binding then "private inspection/report failed" else "inspection/report failed: " + error.Message)

    let runWithTransportAndInput (lockDirectory: string) transport (reader: TextReader) arguments =
        match prepare reader arguments with
        | Error message -> refuse message
        | Ok input ->
            match arguments with
            | "inspect" :: _ -> executeInspection transport input
            | _ -> execute (injectedWriterLock lockDirectory) transport input
    let runWithTransport (lockDirectory: string) transport arguments = runWithTransportAndInput lockDirectory transport Console.In arguments

    let tryRun arguments =
        match arguments with
        | "board-v2" :: ((command :: _) as rest) when command.StartsWith("migration-", StringComparison.Ordinal) ->
            Some(
                try
                    match migrationSyntax rest with
                    | Error _ -> 1
                    | Ok selected ->
                        let primary = Environment.GetEnvironmentVariable "GITHUB_TOKEN"
                        let token = if String.IsNullOrWhiteSpace primary then Environment.GetEnvironmentVariable "GH_TOKEN" else primary
                        match V2MigrationTransport.createLive token with
                        | Error _ -> 1
                        | Ok connection ->
                            use connection = connection
                            executeMigration productionWriterLock connection syncMigrationDirectory TimeProvider.System selected
                with _ -> 1)
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
                         match rest with "inspect" :: _ -> executeInspection transport input | _ -> execute productionWriterLock transport input)
        | _ -> None
