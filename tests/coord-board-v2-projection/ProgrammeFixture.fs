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
    let binding () : Binding =
        use stream = File.OpenRead typeof<Binding>.Assembly.Location
        { BindingVersion = 4; SchemaVersion = 2; ImportRecipeRevision = String.replicate 40 "a"; ImportArtifactSha256 = String.replicate 64 "b"
          SelectedIssues = Map.ofList [ ("I_outcome", "FS-GG/.github#4634") ]; RecipeRevision = String.replicate 40 "c"; PopulationRevision = String.replicate 40 "e"
          OrganizationId = "O_kgDOEYAWYw"; ArtifactSha256 = SHA256.HashData stream |> Convert.ToHexStringLower
          OwnerKind = Org; Owner = "FS-GG"; ProjectNumber = 3; ProjectTitle = "Coordination V2"; ProjectId = project
          Status = { Id = "status"; Options = [ "Backlog"; "Ready"; "In progress"; "In review"; "Blocked"; "Done" ] |> List.map (fun name -> name, name) |> Map.ofList }
          RoadmapFieldId = "roadmap"; Track = { Id = "track"; Options = Map.ofList [ ("Active delivery", "active"); ("Follow-up", "follow") ] }
          Observation = { Id = "observation"; Options = Map.ofList [ ("Verified", "verified"); ("Unknown", "unknown"); ("Stale", "stale") ] }
          Repositories = Set.singleton "FS-GG/.github" }

    let bindingJson () =
        let value = binding ()
        JsonSerializer.Serialize(
            {| bindingVersion = value.BindingVersion; schemaVersion = value.SchemaVersion; importRecipeRevision = value.ImportRecipeRevision
               importArtifactSha256 = value.ImportArtifactSha256; selectedIssues = value.SelectedIssues; recipeRevision = value.RecipeRevision
               populationRevision = value.PopulationRevision; organizationId = value.OrganizationId; artifactSha256 = value.ArtifactSha256
               ownerKind = "organization"; owner = value.Owner; projectNumber = value.ProjectNumber; projectTitle = value.ProjectTitle; projectId = value.ProjectId
               status = {| id = value.Status.Id; options = value.Status.Options |}; roadmapFieldId = value.RoadmapFieldId
               track = {| id = value.Track.Id; options = value.Track.Options |}; observation = {| id = value.Observation.Id; options = value.Observation.Options |}
               repositories = value.Repositories |> Set.toList |})

    let private response body = Ok { Status = 200; Body = body; ETag = None; NextLink = None; Headers = Map.empty }
    let private wire value = response (JsonSerializer.Serialize ({| data = value |}))
    let private connection (nodes: 'a list) = {| totalCount = List.length nodes; nodes = nodes; pageInfo = {| hasNextPage = false; endCursor = (None: string option) |} |}
    let private blob text = wire ({| repository = {| nameWithOwner = "FS-GG/.github"; ref = {| target = {| oid = head |} |}; object = {| oid = head; text = text |}; current = {| oid = head; text = text |} |} |})
    let private enrollment () =
        let value = binding ()
        JsonSerializer.Serialize(
            {| schema = "fsgg.coordination-board-v2-enrollment/v2"
               target = {| owner = value.Owner; organizationId = value.OrganizationId; projectId = project; projectNumber = 3; schemaVersion = 2 |}
               binding = {| recipeRevision = value.ImportRecipeRevision; artifactSha256 = value.ImportArtifactSha256; repositories = [ "FS-GG/.github" ] |}
               items = [ {| issue = "FS-GG/.github#4634"; nodeId = "I_outcome"; roadmap = "docs/coordination/programme.md" |} ] |})

    let ownerBody = "## Owner\n\nworker\n\n## Done when\n\nAccepted outcome\n\n## Current deliverable\n\nExisting PR\n\n## Next action\n\nCurrent source\n\n## Blocked on\n\nSee owning plan\n\n## Acceptance evidence\n\nNative links"

    let private content = {| id = "I_outcome"; number = 4634; repository = {| nameWithOwner = "FS-GG/.github" |} |}
    let private prNode (field: string) (value: 'a) =
        let fields =
            Map.ofList(
                [ ("id", box "PR_current"); ("number", box 42); ("state", box "OPEN");
                  ("headRefOid", box head); ("updatedAt", box revision);
                  ("repository", box {| nameWithOwner = "FS-GG/.github" |}); (field, box value) ])
        wire ({| node = fields |})

    let transport (change: Transport.Request -> Errors.IoResult<Response> -> Errors.IoResult<Response>) =
        Fake.Recorder(fun request ->
            let result =
                match request.Body with
                | Query(document, _) when document.Contains("currentExpression") ->
                    if request.Subject.EndsWith("board-v2-import-manifest.json", StringComparison.Ordinal) then blob (enrollment ()) else blob "Current owning plan; later deployment acceptance is separate"
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
                | Query(document, _) when document.Contains("status: fieldValueByName") ->
                    let select id name option = {| __typename = "ProjectV2ItemFieldSingleSelectValue"; name = name; optionId = option; field = {| id = id |} |}
                    wire (
                        {| node =
                            {| id = "PVTI_current"; project = {| id = project |}; content = content
                               status = select "status" "In review" "In review"; track = select "track" "Active delivery" "active"; observation = select "observation" "Unknown" "unknown"
                               roadmap = {| __typename = "ProjectV2ItemFieldTextValue"; text = "docs/coordination/programme.md"; field = {| id = "roadmap" |} |} |} |})
                | Query(document, _) when document.Contains("pullRequests(first:") ->
                    wire ({| repository = {| nameWithOwner = "FS-GG/.github"; pullRequests = connection [ {| id = "PR_current"; number = 42; url = "https://github.com/FS-GG/.github/pull/42"; headRefOid = head; updatedAt = revision; author = {| login = "author" |}; reviewDecision = "APPROVED" |} ] |} |})
                | Query(document, _) when document.Contains("closingIssuesReferences(") -> prNode "closingIssuesReferences" (connection [ content ])
                | Query(document, _) when document.Contains("files(first:") -> prNode "files" (connection [ {| path = "src/current.fs" |} ])
                | Query(document, _) when document.Contains("reviews(first:") -> prNode "reviews" (connection [ {| id = "review"; state = "APPROVED"; commit = {| oid = head |}; author = {| login = "reviewer" |} |} ])
                | Query(document, _) when document.Contains("statusCheckRollup") -> wire ({| repository = {| nameWithOwner = "FS-GG/.github"; object = {| oid = head; statusCheckRollup = {| contexts = connection [ {| __typename = "CheckRun"; id = "check"; name = "source"; status = "COMPLETED"; conclusion = "SUCCESS" |} ] |} |} |} |})
                | Query(_, _) when request.Subject.EndsWith("final head barrier", StringComparison.Ordinal) -> prNode "unused" "unused"
                | _ -> failwith ("Unmatched inert native request: " + request.Subject)
            change request result)

    let unchanged _ response = response
