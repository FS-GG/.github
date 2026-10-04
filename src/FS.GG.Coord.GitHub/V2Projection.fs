namespace FS.GG.Coord.GitHub

module V2Projection =

    open System
    open System.Text.Json
    open Errors
    open Transport
    open Board

    type SelectFieldBinding =
        {
            Id: string
            Options: Map<string, string>
        }

    type Binding =
        {
            BindingVersion: int
            ImportRecipeRevision: string
            ImportArtifactSha256: string
            SelectedIssues: Map<string, string>
            SchemaVersion: int
            RecipeRevision: string
            PopulationRevision: string
            OrganizationId: string
            ArtifactSha256: string
            OwnerKind: OwnerKind
            Owner: string
            ProjectNumber: int
            ProjectTitle: string
            ProjectId: string
            Status: SelectFieldBinding
            RoadmapFieldId: string
            Track: SelectFieldBinding
            Observation: SelectFieldBinding
            Repositories: Set<string>
        }

    type IssueRef =
        {
            Owner: string
            Repository: string
            Number: int
        }

    type SourceVerification =
        | Current of observedRevision: string
        | Stale of lastVerifiedRevision: string option
        | Refused of reason: string

    type SourceVerifier = IssueRef -> IoResult<SourceVerification>

    type Request =
        {
            Issue: IssueRef
            ExpectedNodeId: string
        }

    type Outcome =
        | AlreadyCurrent of itemId: string
        | Updated of itemId: string

    type Report =
        {
            ProjectId: string
            RecipeRevision: string
            Issue: IssueRef
            ObservedRevision: string
            Observation: string
            Outcome: Outcome
            SourceChecks: int
            ProjectReads: int
            Mutations: int
            VerifiedAt: DateTimeOffset
        }

    let private invalid subject message = Error(Http(422, $"%s{subject}: %s{message}"))

    let private nonBlank value = not (String.IsNullOrWhiteSpace value)

    let private canonicalRepository (value: string) =
        let parts = value.Split('/', StringSplitOptions.None)

        parts.Length = 2
        && Array.forall nonBlank parts
        && Array.forall (fun (part: string) -> part.Trim() = part) parts

    let private expectedStatus = Set.ofList [ "Backlog"; "Ready"; "In progress"; "Blocked"; "Done" ]
    let private expectedTrack = Set.ofList [ "Active delivery"; "Follow-up" ]
    let private expectedObservation = Set.ofList [ "Verified"; "Stale"; "Unknown" ]

    let private validateSelect subject expected (field: SelectFieldBinding) =
        if not (nonBlank field.Id) then
            invalid subject "field id is missing"
        elif (field.Options |> Map.keys |> Set.ofSeq) <> expected then
            invalid subject "option names do not match schema v1"
        elif field.Options |> Map.values |> Seq.exists (nonBlank >> not) then
            invalid subject "an option id is missing"
        elif (field.Options |> Map.values |> Set.ofSeq |> Set.count) <> field.Options.Count then
            invalid subject "option ids are duplicated"
        else
            Ok()

    let private productPopulation (binding: Binding) =
        if binding.Repositories.Count <> 1 then false
        else
            let repository = Set.minElement binding.Repositories
            let prefix = repository + "#"
            repository.StartsWith(binding.Owner + "/", StringComparison.Ordinal)
            && canonicalRepository repository
            && (binding.SelectedIssues |> Map.values |> Set.ofSeq |> Set.count) = binding.SelectedIssues.Count
            && (binding.SelectedIssues |> Map.forall (fun node issue ->
                let mutable number = 0
                nonBlank node && nonBlank issue && issue.StartsWith(prefix, StringComparison.Ordinal)
                && Int32.TryParse(issue.Substring(prefix.Length), &number) && number > 0
                && issue = prefix + string number))

    let validateBinding (binding: Binding) =
        let subject = "the Coordination V2 binding"
        let allFieldIds = [ binding.Status.Id; binding.RoadmapFieldId; binding.Track.Id; binding.Observation.Id ]

        if binding.BindingVersion <> 2 && binding.BindingVersion <> 3 then
            invalid subject "only organization binding version 2 or repository binding version 3 is supported"
        elif String.IsNullOrWhiteSpace binding.ImportRecipeRevision || binding.ImportRecipeRevision.Length <> 40 || binding.ImportRecipeRevision |> Seq.exists (Uri.IsHexDigit >> not)
             || String.IsNullOrWhiteSpace binding.ImportArtifactSha256 || binding.ImportArtifactSha256.Length <> 64 || binding.ImportArtifactSha256 |> Seq.exists (Uri.IsHexDigit >> not) then
            invalid subject "distinct reviewed import constructor provenance is incomplete"
        elif binding.SelectedIssues.Count < (if binding.BindingVersion = 3 then 1 else 3) || binding.SelectedIssues.Count > 5 || binding.SelectedIssues |> Map.exists (fun node issue -> String.IsNullOrWhiteSpace node || String.IsNullOrWhiteSpace issue) then
            invalid subject "explicit selected native cohort is incomplete"
        elif binding.SchemaVersion <> 1 then
            invalid subject "only schema version 1 is supported"
        elif not (nonBlank binding.RecipeRevision) || binding.RecipeRevision.Length <> 40 || binding.RecipeRevision |> Seq.exists (Uri.IsHexDigit >> not) then
            invalid subject "the reviewed recipe revision must be an immutable Git SHA"
        elif not (nonBlank binding.PopulationRevision) || binding.PopulationRevision.Length <> 40 || binding.PopulationRevision |> Seq.exists (Uri.IsHexDigit >> not) then
            invalid subject "the reviewed population revision must be an immutable Git SHA"
        elif not (nonBlank binding.OrganizationId) || not (nonBlank binding.ArtifactSha256) || binding.ArtifactSha256.Length <> 64 || binding.ArtifactSha256 |> Seq.exists (Uri.IsHexDigit >> not) then
            invalid subject "immutable organization or reviewed artifact identity is missing"
        elif binding.OwnerKind <> OwnerKind.Org then
            invalid subject "this organization-board adapter requires an explicit organization owner kind"
        elif not (nonBlank binding.Owner) || binding.ProjectNumber <= 0 || not (nonBlank binding.ProjectTitle) || not (nonBlank binding.ProjectId) then
            invalid subject "the exact organization project identity is incomplete"
        elif
            (String.Equals(binding.Owner, "FS-GG", StringComparison.OrdinalIgnoreCase)
             && (binding.ProjectNumber = 1 || binding.ProjectId = "PVT_kwDOEYAWY84Bb08W"))
            || (binding.BindingVersion = 3 && binding.ProjectId = "PVT_kwDOEYAWY84Bb08W")
        then
            invalid subject "the legacy Coordination Project 1 identity is not a V2 projection target"
        elif binding.BindingVersion = 2 && (binding.Owner <> "FS-GG" || binding.ProjectTitle <> "Coordination V2") then
            invalid subject "this restricted organization pilot requires the FS-GG Coordination V2 target"
        elif binding.BindingVersion = 3 && (binding.ProjectId = "PVT_kwDOEYAWY84Bldpa" || (binding.Owner = "FS-GG" && binding.ProjectNumber = 3)) then
            invalid subject "a repository binding cannot select the organization Coordination V2 target"
        elif binding.BindingVersion = 3 && not (productPopulation binding) then
            invalid subject "a repository binding requires one owner-local repository and an exact bounded native cohort"
        elif not (nonBlank binding.RoadmapFieldId) then
            invalid subject "the Roadmap field id is missing"
        elif List.distinct allFieldIds |> List.length <> allFieldIds.Length then
            invalid subject "field ids are duplicated"
        elif Set.isEmpty binding.Repositories || binding.Repositories |> Seq.exists (canonicalRepository >> not) then
            invalid subject "the repository allowlist is empty or non-canonical"
        else
            match validateSelect "Status" expectedStatus binding.Status with
            | Error error -> Error error
            | Ok() ->
                match validateSelect "Track" expectedTrack binding.Track with
                | Error error -> Error error
                | Ok() -> validateSelect "Observation" expectedObservation binding.Observation

    let private sameOptions expected actual = expected = actual

    let private verifyFieldSchema (binding: Binding) (board: BoardMap) =
        let select name expected =
            match Map.tryFind name board.Fields with
            | Some { Id = id; Type = SingleSelect options } when id = expected.Id && sameOptions options expected.Options -> Ok()
            | _ -> Error(Malformed($"the Coordination V2 field '%s{name}'", "field identity, type or options drifted from the reviewed binding"))

        match select "Status" binding.Status with
        | Error error -> Error error
        | Ok() ->
            match Map.tryFind "Roadmap" board.Fields with
            | Some { Id = id; Type = Text } when id = binding.RoadmapFieldId ->
                match select "Track" binding.Track with
                | Error error -> Error error
                | Ok() -> select "Observation" binding.Observation
            | _ -> Error(Malformed("the Coordination V2 field 'Roadmap'", "field identity or type drifted from the reviewed binding"))

    let private graphRequest document variables subject : Transport.Request =
        { Method = "POST"; Path = "graphql"; Query = []; Body = Query(document, variables)
          Budget = GraphQl; IfNoneMatch = None; Subject = subject }

    let private verifyOrganization transport (binding: Binding) =
        let document = "query($owner: String!) { organization(login: $owner) { id login } rateLimit { cost remaining } }"
        GraphQl.read transport (graphRequest document [ "owner", VString binding.Owner ] "immutable project owner") (fun data ->
            let organization = data.GetProperty "organization"
            if organization.GetProperty("id").GetString() <> binding.OrganizationId || organization.GetProperty("login").GetString() <> binding.Owner then
                Error(Malformed("immutable project owner", "organization identity differs from the reviewed binding"))
            else Ok())

    let private exactMembership (transport: IGitHubTransport) (binding: Binding) (issue: IssueRef) expectedNativeId =
        let subject = $"{issue.Owner}/{issue.Repository}#{issue.Number} exact membership"
        let document =
            "query($owner: String!, $repo: String!, $number: Int!, $after: String) { repository(owner: $owner, name: $repo) { nameWithOwner issue(number: $number) { id number projectItems(first: 50, after: $after) { totalCount pageInfo { hasNextPage endCursor } nodes { id project { id } content { ... on Issue { id number repository { nameWithOwner } } } } } } } rateLimit { cost remaining } }"
        let mutable pages = 0
        let fetch after =
            let variables =
                [ "owner", VString issue.Owner; "repo", VString issue.Repository; "number", VNumber(double issue.Number) ]
                @ (after |> Option.map (fun cursor -> [ "after", VString cursor ]) |> Option.defaultValue [])
            GraphQl.read transport (graphRequest document variables subject) (fun data ->
                try
                    let repository = data.GetProperty "repository"
                    let native = repository.GetProperty "issue"
                    let repo = $"{issue.Owner}/{issue.Repository}"
                    let nativeId = native.GetProperty("id").GetString()
                    if repository.GetProperty("nameWithOwner").GetString() <> repo || native.GetProperty("number").GetInt32() <> issue.Number || nativeId <> expectedNativeId || not (nonBlank nativeId) then
                        Error(Malformed(subject, "native issue identity differs"))
                    else
                        let decode (node: JsonElement) =
                            let itemId = node.GetProperty("id").GetString()
                            let projectId = node.GetProperty("project").GetProperty("id").GetString()
                            let content = node.GetProperty "content"
                            if not (nonBlank itemId) || not (nonBlank projectId) || content.GetProperty("id").GetString() <> nativeId || content.GetProperty("number").GetInt32() <> issue.Number || content.GetProperty("repository").GetProperty("nameWithOwner").GetString() <> repo then
                                Error(Malformed(subject, "membership content identity differs"))
                            else Ok(itemId, projectId, nativeId)
                        let connection = native.GetProperty "projectItems"
                        if connection.GetProperty("totalCount").GetInt32() < 0 || connection.GetProperty("nodes").GetArrayLength() > 50 then
                            Error(Malformed(subject, "membership count/window is invalid"))
                        else
                            let page = GraphQl.page subject "native issue project memberships" (fun (id, _, _) -> id) decode connection
                            match page with
                            | Ok _ -> pages <- pages + 1; page
                            | Error _ -> page
                with
                | :? Collections.Generic.KeyNotFoundException
                | :? InvalidOperationException -> Error(Malformed(subject, "unreadable native membership identity")))
        match GraphQl.drain subject "native issue project memberships" { MaxPages = 10; MaxItems = 500 } fetch with
        | Error error -> Error error
        | Ok memberships ->
            match memberships |> List.filter (fun (_, projectId, _) -> projectId = binding.ProjectId) with
            | [ (itemId, _, nativeId) ] -> Ok(itemId, nativeId, pages)
            | [] -> Error(NotFound subject)
            | _ -> Error(Malformed(subject, "duplicate exact-project membership"))

    let private fieldValueByItemId (transport: IGitHubTransport) (binding: Binding) (issue: IssueRef) nativeId itemId =
        let subject = $"board item %s{itemId} Observation"
        let document =
            "query($itemId: ID!) { node(id: $itemId) { ... on ProjectV2Item { id project { id } content { ... on Issue { id number repository { nameWithOwner } } } fieldValueByName(name: \"Observation\") { ... on ProjectV2ItemFieldSingleSelectValue { name } } } } rateLimit { cost remaining } }"

        let request =
            {
                Method = "POST"
                Path = "graphql"
                Query = []
                Body = Query(document, [ "itemId", VId itemId ])
                Budget = GraphQl
                IfNoneMatch = None
                Subject = subject
            }

        GraphQl.read transport request (fun data ->
            try
                let node = data.GetProperty "node"

                if node.ValueKind = JsonValueKind.Null then
                    Error(NotFound subject)
                elif node.GetProperty("id").GetString() <> itemId
                     || node.GetProperty("project").GetProperty("id").GetString() <> binding.ProjectId
                     || node.GetProperty("content").GetProperty("id").GetString() <> nativeId
                     || node.GetProperty("content").GetProperty("number").GetInt32() <> issue.Number
                     || node.GetProperty("content").GetProperty("repository").GetProperty("nameWithOwner").GetString() <> $"{issue.Owner}/{issue.Repository}" then
                    Error(Malformed(subject, "immutable item project or content identity differs"))
                else
                    match node.TryGetProperty "fieldValueByName" with
                    | true, value when value.ValueKind = JsonValueKind.Null -> Ok None
                    | true, value when value.ValueKind = JsonValueKind.Object ->
                        match value.TryGetProperty "name" with
                        | true, name when name.ValueKind = JsonValueKind.String && nonBlank (name.GetString()) -> Ok(Some(name.GetString()))
                        | _ -> Error(Malformed(subject, "the Observation value has no readable option name"))
                    | _ -> Error(Malformed(subject, "the response has no readable Observation value"))
            with :? Collections.Generic.KeyNotFoundException ->
                Error(Malformed(subject, "the response is missing data.node")))

    type PlanningObservation =
        { ItemId: string; Status: string option; Roadmap: string option; Track: string option
          Observation: string option; MembershipPages: int }

    let readPlanning (transport: IGitHubTransport) (binding: Binding) (request: Request) =
        let admitted = validateBinding binding |> Result.bind (fun () ->
            if request.Issue.Number <= 0 || not (binding.Repositories.Contains $"{request.Issue.Owner}/{request.Issue.Repository}") || Map.tryFind request.ExpectedNodeId binding.SelectedIssues <> Some $"{request.Issue.Owner}/{request.Issue.Repository}#{request.Issue.Number}" then
                Error(Malformed("bound V2 planning fields", "request is outside the explicitly selected native cohort"))
            else Ok())
        admitted |> Result.bind (fun () ->
            verifyOrganization transport binding |> Result.bind (fun () ->
                let expected: ExactProject = { Owner = binding.Owner; Number = binding.ProjectNumber; Title = binding.ProjectTitle; Id = binding.ProjectId }
                bootstrapExactProject transport expected |> Result.bind (fun board ->
                    verifyFieldSchema binding board |> Result.bind (fun () ->
                        exactMembership transport binding request.Issue request.ExpectedNodeId |> Result.bind (fun (itemId, nativeId, pages) ->
                            let document = "query($itemId: ID!) { node(id: $itemId) { ... on ProjectV2Item { id project { id } content { ... on Issue { id number repository { nameWithOwner } } } status: fieldValueByName(name: \"Status\") { __typename ... on ProjectV2ItemFieldSingleSelectValue { name optionId field { ... on ProjectV2SingleSelectField { id } } } } roadmap: fieldValueByName(name: \"Roadmap\") { __typename ... on ProjectV2ItemFieldTextValue { text field { ... on ProjectV2Field { id } } } } track: fieldValueByName(name: \"Track\") { __typename ... on ProjectV2ItemFieldSingleSelectValue { name optionId field { ... on ProjectV2SingleSelectField { id } } } } observation: fieldValueByName(name: \"Observation\") { __typename ... on ProjectV2ItemFieldSingleSelectValue { name optionId field { ... on ProjectV2SingleSelectField { id } } } } } } rateLimit { cost remaining } }"
                            GraphQl.read transport (graphRequest document [ "itemId", VId itemId ] "bound V2 planning fields") (fun data ->
                                let node = data.GetProperty "node"
                                let content = node.GetProperty "content"
                                if node.GetProperty("id").GetString() <> itemId || node.GetProperty("project").GetProperty("id").GetString() <> binding.ProjectId
                                   || content.GetProperty("id").GetString() <> nativeId || content.GetProperty("number").GetInt32() <> request.Issue.Number
                                   || content.GetProperty("repository").GetProperty("nameWithOwner").GetString() <> $"{request.Issue.Owner}/{request.Issue.Repository}" then
                                    Error(Malformed("bound V2 planning fields", "immutable item/project/native identity differs"))
                                else
                                    let select (alias: string) (field: SelectFieldBinding) =
                                        let value = node.GetProperty alias
                                        if value.ValueKind = JsonValueKind.Null then Ok None
                                        else
                                            let name = value.GetProperty("name").GetString()
                                            if value.GetProperty("__typename").GetString() <> "ProjectV2ItemFieldSingleSelectValue"
                                               || value.GetProperty("field").GetProperty("id").GetString() <> field.Id
                                               || Map.tryFind name field.Options <> Some(value.GetProperty("optionId").GetString()) then
                                                Error(Malformed("bound V2 planning fields", "single-select field/value differs from bound schema"))
                                            else Ok(Some name)
                                    let roadmap =
                                        let value = node.GetProperty "roadmap"
                                        if value.ValueKind = JsonValueKind.Null then Ok None
                                        elif value.GetProperty("__typename").GetString() <> "ProjectV2ItemFieldTextValue" || value.GetProperty("field").GetProperty("id").GetString() <> binding.RoadmapFieldId then
                                            Error(Malformed("bound V2 planning fields", "Roadmap field identity/type differs"))
                                        else Ok(Some(value.GetProperty("text").GetString()))
                                    select "status" binding.Status |> Result.bind (fun status -> roadmap |> Result.bind (fun roadmap ->
                                        select "track" binding.Track |> Result.bind (fun track -> select "observation" binding.Observation |> Result.map (fun observation ->
                                            { ItemId = itemId; Status = status; Roadmap = roadmap; Track = track; Observation = observation; MembershipPages = pages }))))))))))

    let private repositoryAllowed (binding: Binding) (issue: IssueRef) =
        let candidate = $"%s{issue.Owner}/%s{issue.Repository}"
        binding.Repositories |> Seq.exists (fun allowed -> String.Equals(allowed, candidate, StringComparison.OrdinalIgnoreCase))

    let runOneShot (verifySource: SourceVerifier) (transport: IGitHubTransport) (binding: Binding) (request: Request) =
        match validateBinding binding with
        | Error error -> Error error
        | Ok() when request.Issue.Number <= 0 || not (nonBlank request.Issue.Owner) || not (nonBlank request.Issue.Repository) || not (nonBlank request.ExpectedNodeId) ->
            invalid "the projection request" "the issue identity is incomplete"
        | Ok() when not (repositoryAllowed binding request.Issue) ->
            Error(Http(403, $"repository %s{request.Issue.Owner}/%s{request.Issue.Repository} is outside the reviewed V2 projection allowlist"))
        | Ok() when binding.BindingVersion = 3 && Map.tryFind request.ExpectedNodeId binding.SelectedIssues <> Some $"{request.Issue.Owner}/{request.Issue.Repository}#{request.Issue.Number}" ->
            invalid "the projection request" "the native issue is outside the selected receiver cohort"
        | Ok() ->
            match verifySource request.Issue with
            | Error error -> Error error
            | Ok(Stale last) ->
                let suffix = last |> Option.map (fun revision -> $"; last verified revision %s{revision}") |> Option.defaultValue ""
                Error(Http(409, $"source observation is stale%s{suffix}; refusing to write Verified"))
            | Ok(Refused reason) ->
                let detail = if nonBlank reason then reason else "no reason supplied"
                Error(Http(403, $"authoritative source verification refused (%s{detail}); refusing to write Verified"))
            | Ok(Current revision) when not (nonBlank revision) ->
                invalid "the source verification" "the current source revision is missing"
            | Ok(Current revision) ->
                let expected: ExactProject =
                    {
                        Owner = binding.Owner
                        Number = binding.ProjectNumber
                        Title = binding.ProjectTitle
                        Id = binding.ProjectId
                    }

                let bootstrap = verifyOrganization transport binding |> Result.bind (fun () -> bootstrapExactProject transport expected)
                match bootstrap with
                | Error error -> Error error
                | Ok board ->
                    match verifyFieldSchema binding board with
                    | Error error -> Error error
                    | Ok() ->
                        match exactMembership transport binding request.Issue request.ExpectedNodeId with
                        | Error error -> Error error
                        | Ok(itemId, nativeId, pages) ->
                            match fieldValueByItemId transport binding request.Issue nativeId itemId with
                            | Error error -> Error error
                            | Ok(Some "Verified") ->
                                Ok
                                    {
                                        ProjectId = binding.ProjectId
                                        RecipeRevision = binding.RecipeRevision
                                        Issue = request.Issue
                                        ObservedRevision = revision
                                        Observation = "Verified"
                                        Outcome = AlreadyCurrent itemId
                                        SourceChecks = 1
                                        ProjectReads = 3 + pages
                                        Mutations = 0
                                        VerifiedAt = DateTimeOffset.UtcNow
                                    }
                            | Ok(Some value) when not (binding.Observation.Options.ContainsKey value) ->
                                Error(Malformed("the Observation value", $"unknown option '%s{value}'"))
                            | Ok _ ->
                                let write =
                                    match verifySource request.Issue with
                                    | Ok(Current fresh) when fresh = revision -> setField transport board itemId "Observation" (Set "Verified")
                                    | Ok _ -> Error(Http(409, "source changed before mutation; retaining current project value"))
                                    | Error error -> Error error
                                match write with
                                | Error error -> Error error
                                | Ok() ->
                                    match fieldValueByItemId transport binding request.Issue nativeId itemId with
                                    | Ok(Some "Verified") ->
                                        Ok
                                            { ProjectId = binding.ProjectId; RecipeRevision = binding.RecipeRevision
                                              Issue = request.Issue; ObservedRevision = revision; Observation = "Verified"
                                              Outcome = Updated itemId; SourceChecks = 2; ProjectReads = 4 + pages; Mutations = 1; VerifiedAt = DateTimeOffset.UtcNow }
                                    | Ok _ -> Error(Malformed("projection readback", "owned Observation did not read back Verified"))
                                    | Error error -> Error error
