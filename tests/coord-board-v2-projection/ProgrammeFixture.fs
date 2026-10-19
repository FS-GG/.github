namespace FS.GG.Coord.GitHub.Tests

/// Inert native transport shared by source and actual CLI composition controls.
module ProgrammeFixture =
    open System
    open System.IO
    open System.Security.Cryptography
    open System.Text.Json
    open FS.GG.Coord.GitHub
    open FS.GG.Coord.GitHub.Transport
    open FS.GG.Coord.GitHub.V2Projection

    let head = String.replicate 40 "d"
    let revision = "2026-10-17T12:00:00Z"
    let project = "PVT_kwDOEYAWY84Bldpa"
    let issue: IssueRef = { Owner = "FS-GG"; Repository = ".github"; Number = 4634 }
    let enroll (value: Binding) (rows: (string * IssueRef * string) list) : Binding =
        let payload =
            JsonSerializer.Serialize(
                {| schema = "fsgg.coordination-board-v2-enrollment-payload/v1";
                   target = {| owner = value.Owner; organizationId = value.OrganizationId; projectId = value.ProjectId; projectNumber = value.ProjectNumber; schemaVersion = value.SchemaVersion |};
                   binding = {| recipeRevision = value.ImportRecipeRevision; artifactSha256 = value.ImportArtifactSha256; repositories = value.Repositories |> Set.toList |};
                   items = (rows |> List.map (fun (id, issue, roadmap) ->
                       {| issue = $"{issue.Owner}/{issue.Repository}#{issue.Number}"; nodeId = id; roadmap = roadmap |})) |})
        { value with ProgrammeEnrollment = Some { NonceHex = String.replicate 64 "1"; PayloadBase64 = Convert.ToBase64String(Text.Encoding.UTF8.GetBytes payload) } }

    let publicEnrollment (value: Binding) =
        let commitment = value.ProgrammeEnrollment |> Option.get |> programmeCommitment |> Result.defaultWith (Errors.explain >> failwith)
        JsonSerializer.Serialize({| schema = "fsgg.coordination-board-v2-enrollment/v3"; commitmentSha256 = commitment |})

    let binding () : Binding =
        use stream = File.OpenRead typeof<Binding>.Assembly.Location
        let value: Binding =
          { WorkLevel = None; ProgrammeEnrollment = None; BindingVersion = 4; SchemaVersion = 2; ImportRecipeRevision = String.replicate 40 "a"; ImportArtifactSha256 = String.replicate 64 "b"
            SelectedIssues = Map.ofList [ ("I_outcome", "FS-GG/.github#4634") ]; RecipeRevision = String.replicate 40 "c"; PopulationRevision = String.replicate 40 "e"
            OrganizationId = "O_kgDOEYAWYw"; ArtifactSha256 = SHA256.HashData stream |> Convert.ToHexStringLower
            OwnerKind = Org; Owner = "FS-GG"; ProjectNumber = 3; ProjectTitle = "Coordination V2"; ProjectId = project
            Status = { Id = "status"; Options = [ "Backlog"; "Ready"; "In progress"; "In review"; "Blocked"; "Done" ] |> List.map (fun name -> name, name) |> Map.ofList }
            RoadmapFieldId = "roadmap"; Track = { Id = "track"; Options = Map.ofList [ ("Active delivery", "active"); ("Follow-up", "follow") ] }
            Observation = { Id = "observation"; Options = Map.ofList [ ("Verified", "verified"); ("Unknown", "unknown"); ("Stale", "stale") ] }
            Repositories = Set.singleton "FS-GG/.github" }
        enroll value [ "I_outcome", issue, "docs/coordination/programme.md" ]

    let bindingJsonFor (value: Binding) =
        let json = JsonSerializer.Serialize(
            {| bindingVersion = value.BindingVersion; schemaVersion = value.SchemaVersion; importRecipeRevision = value.ImportRecipeRevision
               importArtifactSha256 = value.ImportArtifactSha256; selectedIssues = value.SelectedIssues; recipeRevision = value.RecipeRevision
               populationRevision = value.PopulationRevision; organizationId = value.OrganizationId; artifactSha256 = value.ArtifactSha256
               ownerKind = "organization"; owner = value.Owner; projectNumber = value.ProjectNumber; projectTitle = value.ProjectTitle; projectId = value.ProjectId
               status = {| id = value.Status.Id; options = value.Status.Options |}; roadmapFieldId = value.RoadmapFieldId
               track = {| id = value.Track.Id; options = value.Track.Options |}; observation = {| id = value.Observation.Id; options = value.Observation.Options |}
               repositories = value.Repositories |> Set.toList |})
        let node = Text.Json.Nodes.JsonNode.Parse json
        value.WorkLevel |> Option.iter (fun field -> node.["workLevel"] <- JsonSerializer.SerializeToNode {| id = field.Id; options = field.Options |})
        let json = node.ToJsonString()
        match value.ProgrammeEnrollment with
        | None -> json
        | Some enrollment ->
            let root = Text.Json.Nodes.JsonNode.Parse json
            let privateNode = Text.Json.Nodes.JsonObject()
            privateNode.Add("nonceHex", Text.Json.Nodes.JsonValue.Create enrollment.NonceHex)
            privateNode.Add("payloadBase64", Text.Json.Nodes.JsonValue.Create enrollment.PayloadBase64)
            root.AsObject().Add("programmeEnrollment", privateNode)
            root.ToJsonString()

    let bindingJson () = bindingJsonFor (binding ())

    let private response body = Ok { Status = 200; Body = body; ETag = None; NextLink = None; Headers = Map.empty }
    let private wire value = response (JsonSerializer.Serialize ({| data = value |}))
    let private connection (nodes: 'a list) = {| totalCount = List.length nodes; nodes = nodes; pageInfo = {| hasNextPage = false; endCursor = (None: string option) |} |}
    let private blob text = wire ({| repository = {| nameWithOwner = "FS-GG/.github"; ref = {| target = {| oid = head |} |}; object = {| oid = head; text = text |}; current = {| oid = head; text = text |} |} |})
    let private enrollment () = publicEnrollment (binding ())

    let ownerBody = "## Owner\n\nworker\n\n## Done when\n\nAccepted outcome\n\n## Current deliverable\n\nhttps://github.com/FS-GG/.github/pull/42\n\n## Next action\n\nCurrent source\n\n## Blocked on\n\nSee owning plan\n\n## Acceptance evidence\n\nNative links"
    let inlineOwnerBody =
        [ "Owner"; "Done when"; "Current deliverable"; "Next action"; "Blocked on"; "Acceptance evidence" ]
        |> List.fold (fun (body: string) name -> body.Replace("## " + name + "\n\n", name + ": ")) ownerBody


    let private content = {| id = "I_outcome"; number = 4634; repository = {| nameWithOwner = "FS-GG/.github" |} |}
    let private prNode (variables: (string * Var) list) (field: string) (value: 'a) =
        let id = variables |> List.pick (function "id", VId value -> Some value | _ -> None)
        let number = if id = "PR_second" then 43 else 42
        let fields =
            Map.ofList(
                [ ("id", box id); ("number", box number); ("state", box "OPEN");
                  ("headRefOid", box head); ("updatedAt", box revision);
                  ("repository", box {| nameWithOwner = "FS-GG/.github" |}); (field, box value) ])
        wire ({| node = fields |})

    let transport (change: Transport.Request -> Errors.IoResult<Transport.Response> -> Errors.IoResult<Transport.Response>) =
        Fake.Recorder(fun request ->
            let result =
                match request.Body with
                | Query(document, _) when document.Contains("currentExpression") ->
                    if request.Subject.EndsWith("board-v2-import-manifest.json", StringComparison.Ordinal) then blob (enrollment ()) else blob "Current owning plan; later deployment acceptance is separate"
                | Transport.Query(document, variables) when request.Subject = "enrolled native owning plan" ->
                    let repository = variables |> List.pick (function "repo", Transport.VString repo -> Some repo | _ -> None)
                    if document.Contains("qualifiedName:") then
                        wire ({| repository = {| nameWithOwner = "FS-GG/" + repository; ref = {| target = {| oid = head |} |} |} |})
                    else
                        wire ({| repository = {| nameWithOwner = "FS-GG/" + repository; object = {| oid = head; text = "Synthetic current owning plan" |} |} |})
                | Query(document, _) when document.Contains("issueCreationPolicy") ->
                    wire ({| repository = {| id = "R_repository"; issueCreationPolicy = "COLLABORATORS_ONLY"; hasIssuesEnabled = true; mergeCommitAllowed = true; squashMergeAllowed = true; rebaseMergeAllowed = false |} |})
                | Query(document, _) when document.StartsWith("query IntakeIdentity(", StringComparison.Ordinal) ->
                    wire ({| repository = {| issue = {| id = "I_outcome"; updatedAt = revision; author = {| id = "U_author"; login = "author" |} |} |} |})
                | NoBody when request.Path.EndsWith("/collaborators/author/permission", StringComparison.Ordinal) ->
                    response """{"permission":"write","user":{"node_id":"U_author"}}"""
                | Query(document, variables) when document.Contains("pullRequest(number:") ->
                    let number = variables |> List.pick (function "number", VNumber value -> Some(int value) | _ -> None)
                    let native =
                        {| id = if number = 42 then "PR_current" elif number = 43 then "PR_second" else "PR_historical"
                           number = number; url = $"https://github.com/FS-GG/.github/pull/{number}"
                           state = if number = 42 || number = 43 then "OPEN" else "MERGED"
                           headRefOid = head; updatedAt = revision; repository = {| nameWithOwner = "FS-GG/.github" |} |}
                    wire ({| repository = {| nameWithOwner = "FS-GG/.github"; pullRequest = native |} |})
                | Query(document, _) when document.Contains("issue(number:") && document.Contains("updatedAt state") ->
                    wire ({| repository = {| nameWithOwner = "FS-GG/.github"; issue = {| id = "I_outcome"; number = 4634; updatedAt = revision; state = "OPEN"; url = "https://github.com/FS-GG/.github/issues/4634"; body = ownerBody; repository = {| nameWithOwner = "FS-GG/.github" |} |} |} |})
                | NoBody when request.Path.EndsWith("/dependencies/blocked_by", StringComparison.Ordinal) -> response "[]"
                | Query(document, _) when document.Contains("organization(login:") && not (document.Contains("projectV2")) -> wire ({| organization = {| id = "O_kgDOEYAWYw"; login = "FS-GG" |} |})
                | Query(document, _) when document.Contains("projectV2(number:") ->
                    let value = binding ()
                    let select name (field: SelectFieldBinding) = box {| id = field.Id; name = name; dataType = "SINGLE_SELECT"; options = field.Options |> Map.toList |> List.map (fun (name, id) -> {| id = id; name = name |}) |}
                    wire ({| organization = {| login = "FS-GG"; projectV2 = {| id = project; number = 3; title = "Coordination V2"; fields = connection [ select "Status" value.Status; box {| id = "roadmap"; name = "Roadmap"; dataType = "TEXT" |}; select "Track" value.Track; select "Observation" value.Observation ] |} |} |})
                | Query(document, _) when document.Contains("items(first: 50") ->
                    wire ({| node = {| id = project; items = connection [ {| id = "PVTI_current"; content = {| __typename = "Issue"; id = "I_outcome"; number = 4634; repository = {| nameWithOwner = "FS-GG/.github" |} |} |}; {| id = "PVTI_extra"; content = {| __typename = "Issue"; id = "I_extra"; number = 5000; repository = {| nameWithOwner = "FS-GG/.github" |} |} |} ] |} |})
                | Query(document, _) when document.Contains("projectItems(first:") -> wire ({| repository = {| nameWithOwner = "FS-GG/.github"; issue = {| id = "I_outcome"; number = 4634; projectItems = connection [ {| id = "PVTI_current"; project = {| id = project |}; content = content |} ] |} |} |})
                | Query(_, _) when request.Subject = "fresh V2 Observation dispatch identity" ->
                    wire ({| node = {| id = "PVTI_current"; project = {| id = project |}; content = content |} |})
                | Query(document, _) when document.Contains("fieldValueByName(name: \"Observation\")") && not (document.Contains("status: fieldValueByName")) ->
                    wire ({| node = {| id = "PVTI_current"; project = {| id = project |}; content = content; fieldValueByName = {| name = "Unknown" |} |} |})
                | Query(document, _) when document.Contains("status: fieldValueByName") ->
                    let select id name option = {| __typename = "ProjectV2ItemFieldSingleSelectValue"; name = name; optionId = option; field = {| id = id |} |}
                    wire (
                        {| node =
                            {| id = "PVTI_current"; project = {| id = project |}; content = content
                               status = select "status" "In review" "In review"; track = select "track" "Active delivery" "active"; observation = select "observation" "Unknown" "unknown"
                               roadmap = {| __typename = "ProjectV2ItemFieldTextValue"; text = "docs/coordination/programme.md"; field = {| id = "roadmap" |} |} |} |})
                | Query(document, _) when document.Contains("pullRequests(first:") ->
                    wire ({| repository = {| nameWithOwner = "FS-GG/.github"; pullRequests = connection [ {| id = "PR_current"; number = 42; url = "https://github.com/FS-GG/.github/pull/42"; headRefOid = head; updatedAt = revision; author = {| login = "author" |}; reviewDecision = "APPROVED" |} ] |} |})
                | Query(document, variables) when document.Contains("closingIssuesReferences(") -> prNode variables "closingIssuesReferences" (connection [ content ])
                | Query(document, variables) when document.Contains("files(first:") -> prNode variables "files" (connection [ {| path = "src/current.fs" |} ])
                | Query(document, variables) when document.Contains("reviews(first:") -> prNode variables "reviews" (connection [ {| id = "review"; state = "APPROVED"; commit = {| oid = head |}; author = {| login = "reviewer" |} |} ])
                | Query(document, _) when document.Contains("statusCheckRollup") -> wire ({| repository = {| nameWithOwner = "FS-GG/.github"; object = {| oid = head; statusCheckRollup = {| contexts = connection [ {| __typename = "CheckRun"; id = "check"; name = "source"; status = "COMPLETED"; conclusion = "SUCCESS" |} ] |} |} |} |})
                | Query(_, variables) when request.Subject.EndsWith("final head barrier", StringComparison.Ordinal) -> prNode variables "unused" "unused"
                | _ -> failwith ("Unmatched inert native request: " + request.Subject)
            change request result)

    // Synthetic native population for the production CLI's protected-enrollment/plan join.
    // It uses the same recorder and read composition; no private population IDs are retained.
    let populationTransport (selected: Binding) (rows: (string * IssueRef * string) list)
                            (change: Transport.Request -> Errors.IoResult<Transport.Response> -> Errors.IoResult<Transport.Response>) =
        let current (variables: (string * Var) list) =
            let repo = variables |> List.tryPick (function ("repo" | "name"), Transport.VString repo -> Some repo | _ -> None)
            let number = variables |> List.tryPick (function "number", Transport.VNumber number -> Some(int number) | _ -> None)
            match repo, number with
            | Some repo, Some number -> rows |> List.tryFind (fun (_, issue, _) -> issue.Repository = repo && issue.Number = number)
            | _ ->
                let item = variables |> List.tryPick (function "itemId", Transport.VId id -> Some id | _ -> None)
                rows |> List.tryFind (fun (id, _, _) -> item = Some("synthetic-member-" + id))
        transport (fun (request: Transport.Request) result ->
            let result =
                match request.Body with
                | Transport.Query(document, _) when document.Contains("currentExpression") && request.Subject.EndsWith("board-v2-import-manifest.json", StringComparison.Ordinal) ->
                    blob (publicEnrollment selected)
                | Transport.Query(document, variables) when document.Contains("issue(number:") && document.Contains("updatedAt state") ->
                    let id, issue, _ = current variables |> Option.get
                    let repository = issue.Owner + "/" + issue.Repository
                    wire ({| repository = {| nameWithOwner = repository;
                                             issue = {| id = id; number = issue.Number; updatedAt = revision; state = "OPEN";
                                                        url = $"https://github.com/{repository}/issues/{issue.Number}";
                                                        body = ownerBody.Replace("https://github.com/FS-GG/.github/pull/42", "None");
                                                        repository = {| nameWithOwner = repository |} |} |} |})
                | Transport.Query(document, variables) when document.Contains("issueCreationPolicy") ->
                    let repository = variables |> List.pick (function "name", Transport.VString name -> Some name | _ -> None)
                    wire ({| repository = {| id = "synthetic-repository-" + repository; issueCreationPolicy = "COLLABORATORS_ONLY";
                                             hasIssuesEnabled = true; mergeCommitAllowed = true; squashMergeAllowed = true; rebaseMergeAllowed = false |} |})
                | Transport.Query(document, variables) when document.StartsWith("query IntakeIdentity(", StringComparison.Ordinal) ->
                    let id, _, _ = current variables |> Option.get
                    wire ({| repository = {| issue = {| id = id; updatedAt = revision; author = {| id = "U_author"; login = "author" |} |} |} |})
                | Transport.Query(document, variables) when document.Contains("pullRequests(first:") ->
                    let repository = variables |> List.pick (function "repo", Transport.VString repo -> Some repo | _ -> None)
                    wire ({| repository = {| nameWithOwner = "FS-GG/" + repository; pullRequests = connection ([]: obj list) |} |})
                | Transport.Query(document, _) when document.Contains("items(first: 50") ->
                    let nodes = rows |> List.map (fun (id, issue, _) ->
                        {| id = "synthetic-member-" + id;
                           content = {| __typename = "Issue"; id = id; number = issue.Number; repository = {| nameWithOwner = issue.Owner + "/" + issue.Repository |} |} |})
                    wire ({| node = {| id = project; items = connection nodes |} |})
                | Transport.Query(document, variables) when document.Contains("projectItems(first:") ->
                    let id, issue, _ = current variables |> Option.get
                    let repository = issue.Owner + "/" + issue.Repository
                    let content = {| id = id; number = issue.Number; repository = {| nameWithOwner = repository |} |}
                    wire ({| repository = {| nameWithOwner = repository;
                                             issue = {| id = id; number = issue.Number;
                                                        projectItems = connection [ {| id = "synthetic-member-" + id; project = {| id = project |}; content = content |} ] |} |} |})
                | Transport.Query(document, variables) when document.Contains("status: fieldValueByName") ->
                    let id, issue, roadmap = current variables |> Option.get
                    let select fieldId name option =
                        {| __typename = "ProjectV2ItemFieldSingleSelectValue"; name = name; optionId = option; field = {| id = fieldId |} |}
                    wire ({| node = {| id = "synthetic-member-" + id; project = {| id = project |};
                                       content = {| id = id; number = issue.Number; repository = {| nameWithOwner = issue.Owner + "/" + issue.Repository |} |};
                                       status = select "status" "Blocked" "Blocked";
                                       track = select "track" "Active delivery" "active";
                                       observation = select "observation" "Unknown" "unknown";
                                       roadmap = {| __typename = "ProjectV2ItemFieldTextValue"; text = roadmap; field = {| id = "roadmap" |} |} |} |})
                | _ -> result
            change request result)

    let unchanged (_: Transport.Request) (response: Errors.IoResult<Transport.Response>) = response

    let context (report: V2ProjectionSource.InspectionReport) : V2ProjectionSource.SelectionContext =
        let item = report.Items.Head
        let native: V2ProjectionSource.NativeObservation =
            item.Evidence.Native
            |> Option.defaultValue(
                { Issue = issue; NodeId = "I_outcome"; UpdatedAt = revision; State = "OPEN"; Url = "https://github.com/FS-GG/.github/issues/4634"; BodySha256 = String.replicate 64 "0" })
        let _, _, blob = report.Evidence.ProtectedInputs.Head
        let observation: V2ProjectionSource.AttributedObservation =
            { Supplier = "/root/programme"; ObservedAt = report.NativeIntegrator.ObservedAt
              ValidUntil = report.NativeIntegrator.ObservedAt.AddMinutes 1.0; Evidence = [ "runtime/current-owner-observation" ] }
        let pull =
            report.NativeIntegrator.PullRequests |> List.tryFind (fun candidate ->
                candidate.State = "OPEN" && ((candidate.Issues |> List.contains "FS-GG/.github#4634") || (item.Deliverables |> List.contains candidate.Url)))
        { Invocation = "inert-invocation"; ProjectId = report.Binding.ProjectId; SchemaVersion = report.Binding.SchemaVersion
          RecipeRevision = report.Binding.RecipeRevision; ArtifactSha256 = report.Binding.ArtifactSha256
          PopulationRevision = report.Binding.PopulationRevision; EnrollmentBlob = blob; EnrollmentCommitment = (report.Binding.ProgrammeEnrollment |> Option.get |> programmeCommitment |> Result.defaultWith (Errors.explain >> failwith)); InputSha256 = String.replicate 64 "a"
          Slices =
            [ { Id = "source-slice"; Attempt = "owner-attempt"; Operation = if pull.IsSome then V2ProjectionSource.ObserveExisting else V2ProjectionSource.Implement
                Issue = item.Evidence.Issue; AccountableOwner = "worker"; Owner = "worker"; Repository = item.Evidence.Issue.Owner + "/" + item.Evidence.Issue.Repository; Paths = [ "src/new.fs" ]; Action = V2ProjectionSource.Source
                Join = { NativeId = native.NodeId; UpdatedAt = native.UpdatedAt; BodySha256 = native.BodySha256; SourceRevision = item.SourceRevision |> Option.defaultValue "missing source revision"; HierarchySha256 = item.WorkItem |> Option.bind _.Sha256 }
                Observation = observation; PullRequest = pull |> Option.map _.Url; PullRequestId = pull |> Option.map _.Id
                PullRequestUpdatedAt = pull |> Option.map _.UpdatedAt; Head = pull |> Option.map _.Head
                Prerequisites =
                    (if item.CurrentDeliverable |> Option.exists (fun value -> value <> "None") then
                        [ { Action = V2ProjectionSource.Source; Reference = "current-deliverable"; Satisfied = Some true; SupplyingOwner = "/root/programme" } ]
                     else [])
                PrerequisitesComplete = true; IntakeAuthorized = Some true } ]
          ImplementationSlots = Some 1; ReviewSlots = Some 1; HostedCiSlots = Some 1; CapacityMeaning = "remaining-after-active"
          CapacityObservation = Some observation; Reservations = Some []; ReservationObservation = Some observation
          ObservedAt = observation.ObservedAt; ValidUntil = observation.ValidUntil; Evidence = "inert actual parent caller observation" }

    let integratorJson (context: V2ProjectionSource.SelectionContext) =
        let action = function V2ProjectionSource.Source -> "source" | V2ProjectionSource.Activation -> "activation"
        let operation = function
            | V2ProjectionSource.ObserveExisting -> "observe-existing" | V2ProjectionSource.Implement -> "implement"
            | V2ProjectionSource.ReviewWork -> "review" | V2ProjectionSource.QualifyWork -> "qualify" | V2ProjectionSource.ActivateWork -> "activate"
        let observation (value: V2ProjectionSource.AttributedObservation) =
            {| supplier = value.Supplier; observedAt = value.ObservedAt; validUntil = value.ValidUntil; evidence = value.Evidence |}
        JsonSerializer.Serialize(
            {| schema = "fsgg.coord.board-v2-integrator-observations/1"; invocation = context.Invocation; projectId = context.ProjectId; schemaVersion = context.SchemaVersion
               recipeRevision = context.RecipeRevision; artifactSha256 = context.ArtifactSha256; populationRevision = context.PopulationRevision; enrollmentBlob = context.EnrollmentBlob; enrollmentCommitmentSha256 = context.EnrollmentCommitment
               observedAt = context.ObservedAt; validUntil = context.ValidUntil; evidence = context.Evidence
               slices = context.Slices |> List.map (fun slice ->
                   {| id = slice.Id; attempt = slice.Attempt; operation = operation slice.Operation
                      issue = {| owner = slice.Issue.Owner; repository = slice.Issue.Repository; number = slice.Issue.Number |}
                      accountableOwner = slice.AccountableOwner; owner = slice.Owner; repository = slice.Repository; paths = slice.Paths; action = action slice.Action
                      join = {| nativeId = slice.Join.NativeId; updatedAt = slice.Join.UpdatedAt; bodySha256 = slice.Join.BodySha256; sourceRevision = slice.Join.SourceRevision |}
                      observation = observation slice.Observation; pullRequest = slice.PullRequest; pullRequestId = slice.PullRequestId
                      pullRequestUpdatedAt = slice.PullRequestUpdatedAt; head = slice.Head
                      prerequisites = slice.Prerequisites |> List.map (fun prerequisite -> {| action = action prerequisite.Action; reference = prerequisite.Reference; satisfied = prerequisite.Satisfied; supplyingOwner = prerequisite.SupplyingOwner |})
                      prerequisitesComplete = slice.PrerequisitesComplete; intakeAuthorized = slice.IntakeAuthorized |})
               capacity = context.CapacityObservation |> Option.map (fun observed ->
                   {| meaning = context.CapacityMeaning; implementation = context.ImplementationSlots; review = context.ReviewSlots; hostedCi = context.HostedCiSlots; observation = observation observed |})
               reservations = (context.Reservations, context.ReservationObservation) ||> Option.map2 (fun (items: V2ProjectionSource.Reservation list) (observed: V2ProjectionSource.AttributedObservation) ->
                   {| observation = observation observed
                      items = items |> List.map (fun reservation ->
                          {| sliceId = reservation.SliceId; attempt = reservation.Attempt; operation = operation reservation.Operation; owner = reservation.Owner
                             repository = reservation.Repository; paths = reservation.Paths; pullRequest = reservation.PullRequest; head = reservation.Head; custodyReference = reservation.CustodyReference |}) |}) |})

    let newAssignment (request: Transport.Request) (result: Errors.IoResult<Transport.Response>) =
        result |> Result.map (fun value ->
            if request.Subject.EndsWith("native observation", StringComparison.Ordinal) then
                { value with Body = value.Body.Replace("https://github.com/FS-GG/.github/pull/42", "None") }
            elif request.Subject.EndsWith("closingIssuesReferences", StringComparison.Ordinal) then
                { value with Body = value.Body.Replace("I_outcome", "I_other").Replace("\"number\":4634", "\"number\":999") }
            else value)

    let observation (current: string) (request: Transport.Request) (result: Errors.IoResult<Transport.Response>) =
        if request.Subject = "board item PVTI_current Observation" then
            result |> Result.map (fun value -> { value with Body = value.Body.Replace("\"Unknown\"", JsonSerializer.Serialize current) })
        else result

    let oldSchema (request: Transport.Request) (result: Errors.IoResult<Transport.Response>) =
        match request.Body with
        | Query(document, _) when document.Contains("projectV2(number:") ->
            result |> Result.map (fun value ->
                let root = Text.Json.Nodes.JsonNode.Parse value.Body
                let project = root.["data"].["organization"].["projectV2"]
                let nodes = project.["fields"].["nodes"].AsArray()
                let status = nodes |> Seq.find (fun field -> field.["name"].GetValue<string>() = "Status")
                let options = status.["options"].AsArray()
                for index in options.Count - 1 .. -1 .. 0 do
                    if options.[index].["name"].GetValue<string>() = "In review" then options.RemoveAt index
                { value with Body = root.ToJsonString() })
        | _ -> result

    let successorBinding () =
        let value = { binding () with BindingVersion = 5; SchemaVersion = 3
                                      WorkLevel = Some { Id = "work-level"; Options = Map.ofList ["Outcome", "outcome-option"; "Delivery", "delivery-option"] } }
        enroll value ["I_outcome", issue, "docs/coordination/programme.md"]

    /// Actual fixed reader over inert complete native connections, with one extra unenrolled child.
    let successorTransportFor (selected: Binding) (rows: (string * IssueRef * string) list) (level: string option) change =
        let original = populationTransport selected rows unchanged
        Fake.Recorder(fun request ->
            let result =
                match request.Body with
                | Query(_, variables) when request.Subject = "bound V2 successor planning memberships" ->
                    let number = variables |> List.pick (function "number", VNumber value -> Some(int value) | _ -> None)
                    let repository = variables |> List.pick (function "repo", VString value -> Some value | _ -> None)
                    let id, issue, roadmap = rows |> List.find (fun (_, issue, _) -> issue.Number = number && issue.Repository = repository)
                    let repository = issue.Owner + "/" + issue.Repository
                    let select fieldId name option =
                        {| __typename = "ProjectV2ItemFieldSingleSelectValue"; name = name; optionId = option; field = {| id = fieldId |} |}
                    let memberItem =
                        {| id = "synthetic-member-" + id; project = {| id = project |}
                           content = {| id = id; number = issue.Number; repository = {| nameWithOwner = repository |} |}
                           status = select "status" "Blocked" "Blocked"; track = select "track" "Active delivery" "active"
                           observation = select "observation" "Unknown" "unknown"
                           roadmap = {| __typename = "ProjectV2ItemFieldTextValue"; text = roadmap; field = {| id = "roadmap" |} |} |}
                    wire {| organization = {| id = selected.OrganizationId; login = selected.Owner |}
                            repository = {| nameWithOwner = repository; issue = {| id = id; number = number; projectItems = connection [ memberItem ] |} |} |}
                | Query(_, variables) when request.Subject = "selected work classification" ->
                    let itemId = variables |> List.pick (function "itemId", VId id -> Some id | _ -> None)
                    let id, _, _ = rows |> List.find (fun (id, _, _) -> itemId = "synthetic-member-" + id)
                    let value = level |> Option.map (fun name ->
                        {| __typename = "ProjectV2ItemFieldSingleSelectValue"; name = name; optionId = selected.WorkLevel.Value.Options[name]; field = {| id = "work-level" |} |})
                    wire {| node = {| id = itemId; project = {| id = project |}; content = {| id = id |}; workLevel = value |} |}
                | Query(_, variables) when request.Subject = "selected native work hierarchy" ->
                    let number = variables |> List.pick (function "number", VNumber number -> Some(int number) | _ -> None)
                    let id, selectedIssue, _ = rows |> List.find (fun (_, issue, _) -> issue.Number = number)
                    let child = {| id = if rows.Length = 1 then "I_unselected-child" else "I_unselected-child-" + id
                                   number = if rows.Length = 1 then 9999 else 10000 + selectedIssue.Number
                                   state = "OPEN"; stateReason = (None: string option); closedAt = (None: string option); repository = {| nameWithOwner = "FS-GG/.github" |} |}
                    let parent = {| id = "I_parent"; number = 9998; repository = {| nameWithOwner = "FS-GG/.github" |} |}
                    let repository = selectedIssue.Owner + "/" + selectedIssue.Repository
                    wire {| repository = {| nameWithOwner = repository; issue =
                                {| id = id; number = selectedIssue.Number; updatedAt = revision; state = "OPEN"; stateReason = (None: string option); closedAt = (None: string option)
                                   body = ownerBody.Replace("https://github.com/FS-GG/.github/pull/42", "None"); repository = {| nameWithOwner = repository |}
                                   parent = parent; subIssues = connection [child] |} |} |}
                | Query(document, _) when document.Contains("projectV2(number:") ->
                    (original :> IGitHubTransport).Send request |> Result.map (fun response ->
                        let root = Text.Json.Nodes.JsonNode.Parse response.Body
                        let fields = root.["data"].["organization"].["projectV2"].["fields"]
                        fields.["nodes"].AsArray().Add(JsonSerializer.SerializeToNode {| id = "work-level"; name = "Work level"; dataType = "SINGLE_SELECT"; options = [ {| id = "outcome-option"; name = "Outcome" |}; {| id = "delivery-option"; name = "Delivery" |} ] |})
                        fields.["totalCount"] <- Text.Json.Nodes.JsonValue.Create(fields.["nodes"].AsArray().Count)
                        { response with Body = root.ToJsonString() })
                | _ -> (original :> IGitHubTransport).Send request
            change request result)

    let successorTransport selected level change =
        successorTransportFor selected ["I_outcome", issue, "docs/coordination/programme.md"] level change

    let successorIntegratorJson (context: V2ProjectionSource.SelectionContext) =
        let root = Text.Json.Nodes.JsonNode.Parse(integratorJson context)
        root.["schema"] <- Text.Json.Nodes.JsonValue.Create "fsgg.coord.board-v2-integrator-observations/2"
        context.Slices |> List.iteri (fun index slice ->
            root.["slices"].[index].["join"].["hierarchySha256"] <- JsonSerializer.SerializeToNode slice.Join.HierarchySha256)
        root.ToJsonString()
