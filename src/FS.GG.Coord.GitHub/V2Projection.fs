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
            SchemaVersion: int
            RecipeRevision: string
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

    type SourceEvidence =
        | Fresh of observedRevision: string
        | Stale of lastVerifiedRevision: string option
        | Unknown of reason: string

    type Request =
        {
            Issue: IssueRef
            Source: SourceEvidence
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
            Reads: int
            Mutations: int
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

    let validateBinding (binding: Binding) =
        let subject = "the Coordination V2 binding"
        let allFieldIds = [ binding.Status.Id; binding.RoadmapFieldId; binding.Track.Id; binding.Observation.Id ]

        if binding.SchemaVersion <> 1 then
            invalid subject "only schema version 1 is supported"
        elif not (nonBlank binding.RecipeRevision) then
            invalid subject "the reviewed recipe revision is missing"
        elif binding.OwnerKind <> OwnerKind.Org then
            invalid subject "this organization-board adapter requires an explicit organization owner kind"
        elif not (nonBlank binding.Owner) || binding.ProjectNumber <= 0 || not (nonBlank binding.ProjectTitle) || not (nonBlank binding.ProjectId) then
            invalid subject "the exact organization project identity is incomplete"
        elif
            String.Equals(binding.Owner, "FS-GG", StringComparison.OrdinalIgnoreCase)
            && binding.ProjectNumber = 1
            && binding.ProjectTitle = "Coordination"
            && binding.ProjectId = "PVT_kwDOEYAWY84Bb08W"
        then
            invalid subject "the legacy Coordination Project 1 identity is not a V2 projection target"
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

    let private fieldValueByItemId (transport: IGitHubTransport) itemId =
        let subject = $"board item %s{itemId} Observation"
        let document =
            "query($itemId: ID!) { node(id: $itemId) { ... on ProjectV2Item { fieldValueByName(name: \"Observation\") { ... on ProjectV2ItemFieldSingleSelectValue { name } } } } rateLimit { cost remaining } }"

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

    let private repositoryAllowed (binding: Binding) (issue: IssueRef) =
        let candidate = $"%s{issue.Owner}/%s{issue.Repository}"
        binding.Repositories |> Seq.exists (fun allowed -> String.Equals(allowed, candidate, StringComparison.OrdinalIgnoreCase))

    let runOneShot (transport: IGitHubTransport) (binding: Binding) (request: Request) =
        match validateBinding binding with
        | Error error -> Error error
        | Ok() when request.Issue.Number <= 0 || not (nonBlank request.Issue.Owner) || not (nonBlank request.Issue.Repository) ->
            invalid "the projection request" "the issue identity is incomplete"
        | Ok() when not (repositoryAllowed binding request.Issue) ->
            Error(Http(403, $"repository %s{request.Issue.Owner}/%s{request.Issue.Repository} is outside the reviewed V2 projection allowlist"))
        | Ok() ->
            match request.Source with
            | Stale last ->
                let suffix = last |> Option.map (fun revision -> $"; last verified revision %s{revision}") |> Option.defaultValue ""
                Error(Http(409, $"source observation is stale%s{suffix}; refusing to write Verified"))
            | Unknown reason ->
                let detail = if nonBlank reason then reason else "no reason supplied"
                Error(Http(409, $"source observation is unknown (%s{detail}); refusing to write Verified"))
            | Fresh revision when not (nonBlank revision) ->
                invalid "the projection request" "the fresh source revision is missing"
            | Fresh revision ->
                let expected: ExactProject =
                    {
                        Owner = binding.Owner
                        Number = binding.ProjectNumber
                        Title = binding.ProjectTitle
                        Id = binding.ProjectId
                    }

                match bootstrapExactProject transport expected with
                | Error error -> Error error
                | Ok board ->
                    match verifyFieldSchema binding board with
                    | Error error -> Error error
                    | Ok() ->
                        match itemId transport board request.Issue.Owner request.Issue.Repository request.Issue.Number with
                        | Error error -> Error error
                        | Ok None -> Error(NotFound $"%s{request.Issue.Owner}/%s{request.Issue.Repository}#%d{request.Issue.Number} on exact project %s{binding.ProjectId}")
                        | Ok(Some itemId) ->
                            match fieldValueByItemId transport itemId with
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
                                        Reads = 3
                                        Mutations = 0
                                    }
                            | Ok(Some value) when not (binding.Observation.Options.ContainsKey value) ->
                                Error(Malformed("the Observation value", $"unknown option '%s{value}'"))
                            | Ok _ ->
                                match setField transport board itemId "Observation" (Set "Verified") with
                                | Error error -> Error error
                                | Ok() ->
                                    Ok
                                        {
                                            ProjectId = binding.ProjectId
                                            RecipeRevision = binding.RecipeRevision
                                            Issue = request.Issue
                                            ObservedRevision = revision
                                            Observation = "Verified"
                                            Outcome = Updated itemId
                                            Reads = 3
                                            Mutations = 1
                                        }
