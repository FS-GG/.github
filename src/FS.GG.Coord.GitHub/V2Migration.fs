namespace FS.GG.Coord.GitHub

module V2Migration =
    open System
    open Errors
    open Transport

    type SelectedIssue =
        { Id: string; Repository: string; Number: int; Slice: string }

    type DesiredView =
        { Id: string option; Name: string; Layout: string; Filter: string;
          VisibleFieldIds: string list }

    type PlanningOption =
        { Id: string; Name: string }

    type InitialPlanning =
        { IssueId: string; Track: PlanningOption; Roadmap: string; Status: PlanningOption option }

    type PlanningValue =
        | PlanningText of string
        | PlanningSelect of PlanningOption

    // Explicit selected classification only; no title, leaf, Status or Track inference.
    type WorkLevelAssignment = { IssueId: string; Name: string }

    // None FieldId permits one create only when no same-name field exists. Exact independently
    // read-back field/option identities then bind subsequent absent-only initialization.
    type WorkLevelPlan = { FieldId: string option; Assignments: WorkLevelAssignment list }

    type Scope =
        { SourceRevision: string; ArtifactSha256: string; CliArtifactSha256: string; PrincipalId: string;
          PrincipalLogin: string; SelectedIssues: SelectedIssue list;
          StatusFieldId: string; RoadmapFieldId: string; TrackFieldId: string;
          ObservationFieldId: string; InReviewColor: string; InReviewDescription: string;
          Views: DesiredView list; InitialPlanning: InitialPlanning list; WorkLevel: WorkLevelPlan option }

    type SelectOption =
        { Id: string; Name: string; Color: string; Description: string }

    type Field =
        { Id: string; Name: string; DataType: string; Options: SelectOption list }

    type ItemValue =
        { Id: string; FieldId: string; Kind: string; Value: string }

    type Item =
        { Id: string; ContentId: string; ContentKind: string; Repository: string option;
          Number: int option; UpdatedAt: string; Archived: bool; Values: ItemValue list }

    type NativeIssue =
        { Id: string; Repository: string; Number: int; UpdatedAt: string; State: string }

    type View =
        { Id: string; Name: string; Layout: string; Filter: string;
          VisibleFieldIds: string list; UnsupportedConfiguration: string }

    type Snapshot =
        { ObservedAt: DateTimeOffset; PrincipalId: string; PrincipalLogin: string;
          OrganizationId: string; ProjectId: string; ProjectTitle: string;
          ProjectNumber: int; Private: bool; CanUpdate: bool; UpdatedAt: string;
          Fields: Field list; Items: Item list; Views: View list; Issues: NativeIssue list }

    type Change =
        | CreateWorkLevel
        | AddInReview of fieldId: string * retained: SelectOption list * color: string * description: string
        | AddMembership of issueId: string
        | InitializePlanningValue of issueId: string * itemId: string * fieldId: string * value: PlanningValue
        | CreateView of DesiredView
        | UpdateView of viewId: string * DesiredView

    type ReadbackObservation =
        | ObservedEffect of Scope
        | PendingVisibility

    type Prepared =
        { Scope: Scope; Snapshot: Snapshot; Changes: Change list; OwnerActions: string list;
          Digest: string; PreparedPath: string option }

    open System.Collections.Generic
    open System.IO
    open System.Text
    open System.Text.Json
    open System.Text.Json.Nodes
    open System.Security.Cryptography

    let private subject = "Project 3 migration"
    let private invalid detail = Error(Malformed(subject, detail))
    let private bounded limit (value: string) =
        not (String.IsNullOrWhiteSpace value) && value.Length <= limit
        && value.Trim() = value && not (value |> Seq.exists Char.IsControl)
    let private unique (values: string list) = Set.count (Set.ofList values) = values.Length
    let private hex length (value: string) =
        bounded length value && value.Length = length && Seq.forall Uri.IsHexDigit value
    let private repository (value: string) =
        let parts = value.Split('/')
        parts.Length = 2 && parts.[0] = "FS-GG"
        && bounded 100 parts.[1] && parts.[1] <> "." && parts.[1] <> ".."
        && Seq.forall (fun c -> Char.IsAsciiLetterOrDigit c || c = '.' || c = '_' || c = '-') parts.[1]

    let validateScope (scope: Scope) : IoResult<unit> =
        let fieldIds = [ scope.StatusFieldId; scope.TrackFieldId; scope.RoadmapFieldId; scope.ObservationFieldId ]
        let colors = Set.ofList [ "GRAY"; "BLUE"; "GREEN"; "YELLOW"; "ORANGE"; "RED"; "PINK"; "PURPLE" ]
        let invalidIssues =
            scope.SelectedIssues |> List.exists (fun (issue: SelectedIssue) ->
                not (bounded 256 issue.Id && bounded 256 issue.Repository && repository issue.Repository
                     && issue.Number > 0 && bounded 128 issue.Slice))
        let existingViewIds = scope.Views |> List.choose (fun (view: DesiredView) -> view.Id)
        let invalidViews =
            scope.Views |> List.exists (fun (view: DesiredView) ->
                not (bounded 100 view.Name)
                || not (Set.contains view.Layout (Set.ofList [ "TABLE_LAYOUT"; "BOARD_LAYOUT"; "ROADMAP_LAYOUT" ]))
                || isNull view.Filter || view.Filter.Length > 2048 || (view.Filter |> Seq.exists Char.IsControl)
                || view.VisibleFieldIds.IsEmpty || view.VisibleFieldIds.Length > 50
                || not (unique view.VisibleFieldIds) || (view.VisibleFieldIds |> List.exists (bounded 256 >> not))
                || (view.Id |> Option.exists (bounded 256 >> not))
                || (scope.WorkLevel.IsSome && view.Name.Equals("All items", StringComparison.OrdinalIgnoreCase) && view.Filter <> ""))
        let selectedIds = scope.SelectedIssues |> List.map (fun (issue: SelectedIssue) -> issue.Id) |> Set.ofList
        let optionValid (value: PlanningOption) = bounded 256 value.Id && bounded 100 value.Name
        let invalidPlanning =
            scope.InitialPlanning.Length > 100
            || not (unique (scope.InitialPlanning |> List.map (fun (initial: InitialPlanning) -> initial.IssueId)))
            || (scope.InitialPlanning |> List.exists (fun (initial: InitialPlanning) ->
                not (selectedIds.Contains initial.IssueId) || not (optionValid initial.Track)
                || not (bounded 4096 initial.Roadmap) || (initial.Status |> Option.exists (optionValid >> not))))
        let invalidWorkLevel =
            scope.WorkLevel |> Option.exists (fun plan ->
                (plan.FieldId |> Option.exists (fun id -> not (bounded 256 id) || List.contains id fieldIds))
                || plan.Assignments.Length > 100
                || not (unique (plan.Assignments |> List.map _.IssueId))
                || (plan.Assignments |> List.exists (fun value ->
                    not (selectedIds.Contains value.IssueId) || (value.Name <> "Outcome" && value.Name <> "Delivery"))))
        if not (hex 40 scope.SourceRevision && hex 64 scope.ArtifactSha256 && hex 64 scope.CliArtifactSha256)
           || not (bounded 256 scope.PrincipalId && bounded 100 scope.PrincipalLogin) then
            invalid "reviewed source/package or expected principal identity is incomplete"
        elif (fieldIds |> List.exists (bounded 256 >> not)) || not (unique fieldIds) then
            invalid "four distinct native planning/Observation field identities are required"
        elif scope.SelectedIssues.IsEmpty || scope.SelectedIssues.Length > 100 || invalidIssues
             || not (unique (scope.SelectedIssues |> List.map (fun (issue: SelectedIssue) -> issue.Id)))
             || not (unique (scope.SelectedIssues |> List.map (fun (issue: SelectedIssue) -> $"{issue.Repository}#{issue.Number}")))
             || not (unique (scope.SelectedIssues |> List.map (fun (issue: SelectedIssue) -> issue.Slice))) then
            invalid "explicit distinct native issue/slice identities are required; membership never selects enrollment"
        elif not (colors.Contains scope.InReviewColor) || isNull scope.InReviewDescription || scope.InReviewDescription.Length > 1024 then
            invalid "bounded native In review option descriptor is required"
        elif invalidWorkLevel then
            invalid "Work level requires exact selected issues, Outcome/Delivery values and a distinct explicit field identity"
        elif invalidPlanning then
            invalid "planning initialization requires one exact selected issue declaration and bounded explicit values"
        elif scope.Views.Length > 10 || invalidViews || not (unique existingViewIds) || not (unique (scope.Views |> List.map (fun (view: DesiredView) -> view.Name))) then
            invalid "view requests must use distinct bounded names, native IDs and supported API configuration only"
        else Ok()

    let private propertyString (name: string) (node: JsonElement) =
        let value = node.GetProperty(name).GetString()
        if isNull value then invalidArg name "native string is null"
        value
    let private nonempty (name: string) (node: JsonElement) =
        let value = propertyString name node
        if not (bounded 4096 value) then invalidArg name "native identity is malformed"
        value
    let private decode (read: unit -> 'a) : IoResult<'a> =
        try Ok(read())
        with :? InvalidOperationException | :? KeyNotFoundException | :? ArgumentException | :? FormatException | :? JsonException ->
            invalid "native response is missing or malformed; no incomplete snapshot is accepted"
    let private query (document: string) (variables: (string * Var) list) : Transport.Request =
        { Method = "POST"; Path = "graphql"; Query = []; Body = Query(document, variables);
          Budget = GraphQl; IfNoneMatch = None; Subject = subject }
    let private cursor (after: string option) =
        match after with Some value -> [ "after", VString value ] | None -> []
    let private rootNode (data: JsonElement) = data.GetProperty "node"
    let private limits: GraphQl.DrainLimits = { MaxPages = 100; MaxItems = 5000 }
    let private drainCached (first: JsonElement option) (transport: IGitHubTransport) (id: string) (what: string) (document: string)
                      (key: 'a -> string) (node: JsonElement -> IoResult<'a>) : IoResult<'a list> =
        GraphQl.drain subject what limits (fun after ->
            match after, first with
            | None, Some connection -> GraphQl.page subject what key node connection
            | _ ->
                GraphQl.read transport (query document ([ "id", VId id ] @ cursor after)) (fun data ->
                    match decode (fun () -> rootNode data) with
                    | Error error -> Error error
                    | Ok parent ->
                        match decode (fun () -> nonempty "id" parent, parent.GetProperty what) with
                        | Error error -> Error error
                        | Ok(actual, connection) when actual = id -> GraphQl.page subject what key node connection
                        | Ok _ -> invalid "connection returned a different parent identity"))

    let private option (node: JsonElement) : IoResult<SelectOption> =
        decode (fun () ->
            { Id = nonempty "id" node; Name = nonempty "name" node;
              Color = nonempty "color" node; Description = propertyString "description" node })
    let private sequence (values: IoResult<'a> list) : IoResult<'a list> =
        List.foldBack (fun value rest ->
            match value, rest with
            | Ok item, Ok items -> Ok(item :: items)
            | Error error, _ | _, Error error -> Error error) values (Ok [])
    let private field (node: JsonElement) : IoResult<Field> =
        match decode (fun () ->
            let kind = nonempty "__typename" node
            let options =
                if kind = "ProjectV2SingleSelectField" then node.GetProperty("options").EnumerateArray() |> Seq.map option |> Seq.toList
                else []
            nonempty "id" node, nonempty "name" node, nonempty "dataType" node, options) with
        | Error error -> Error error
        | Ok(id, name, dataType, options) ->
            sequence options |> Result.bind (fun options ->
                if not (unique (List.map (fun (value: SelectOption) -> value.Id) options))
                   || not (unique (List.map (fun (value: SelectOption) -> value.Name) options)) then
                    invalid "duplicate native option identities or names"
                else Ok ({ Id = id; Name = name; DataType = dataType; Options = options }))

    let private fieldsDocument =
        "query($id:ID!,$after:String){node(id:$id){... on ProjectV2{id fields(first:50,after:$after){totalCount pageInfo{hasNextPage endCursor} nodes{__typename ... on ProjectV2FieldCommon{id name dataType} ... on ProjectV2SingleSelectField{options{id name color description}}}}}}}"
    let private itemsDocument =
        "query($id:ID!,$after:String){node(id:$id){... on ProjectV2{id items(first:50,after:$after){totalCount pageInfo{hasNextPage endCursor} nodes{id updatedAt isArchived content{__typename ... on Issue{id number repository{nameWithOwner}} ... on PullRequest{id number repository{nameWithOwner}} ... on DraftIssue{id}} fieldValues(first:50){pageInfo{hasNextPage endCursor} nodes{__typename ... on ProjectV2ItemFieldValueCommon{id updatedAt field{... on ProjectV2FieldCommon{id}}} ... on ProjectV2ItemFieldTextValue{text} ... on ProjectV2ItemFieldSingleSelectValue{optionId name} ... on ProjectV2ItemFieldDateValue{date} ... on ProjectV2ItemFieldNumberValue{number} ... on ProjectV2ItemFieldIterationValue{iterationId title startDate duration} ... on ProjectV2ItemFieldMultiSelectValue{field{... on ProjectV2FieldCommon{id}}} ... on ProjectV2ItemFieldRepositoryValue{field{... on ProjectV2FieldCommon{id}}} ... on ProjectV2ItemFieldLabelValue{field{... on ProjectV2FieldCommon{id}}} ... on ProjectV2ItemFieldMilestoneValue{field{... on ProjectV2FieldCommon{id}}} ... on ProjectV2ItemFieldPullRequestValue{field{... on ProjectV2FieldCommon{id}}} ... on ProjectV2ItemFieldReviewerValue{field{... on ProjectV2FieldCommon{id}}} ... on ProjectV2ItemFieldUserValue{field{... on ProjectV2FieldCommon{id}}} ... on ProjectV2ItemIssueFieldValue{field{... on ProjectV2FieldCommon{id}}}}}}}}}}"
    let private valuesDocument =
        "query($id:ID!,$after:String){node(id:$id){... on ProjectV2Item{id fieldValues(first:50,after:$after){pageInfo{hasNextPage endCursor} nodes{__typename ... on ProjectV2ItemFieldValueCommon{id updatedAt field{... on ProjectV2FieldCommon{id}}} ... on ProjectV2ItemFieldTextValue{text} ... on ProjectV2ItemFieldSingleSelectValue{optionId name} ... on ProjectV2ItemFieldDateValue{date} ... on ProjectV2ItemFieldNumberValue{number} ... on ProjectV2ItemFieldIterationValue{iterationId title startDate duration} ... on ProjectV2ItemFieldMultiSelectValue{field{... on ProjectV2FieldCommon{id}}} ... on ProjectV2ItemFieldRepositoryValue{field{... on ProjectV2FieldCommon{id}}} ... on ProjectV2ItemFieldLabelValue{field{... on ProjectV2FieldCommon{id}}} ... on ProjectV2ItemFieldMilestoneValue{field{... on ProjectV2FieldCommon{id}}} ... on ProjectV2ItemFieldPullRequestValue{field{... on ProjectV2FieldCommon{id}}} ... on ProjectV2ItemFieldReviewerValue{field{... on ProjectV2FieldCommon{id}}} ... on ProjectV2ItemFieldUserValue{field{... on ProjectV2FieldCommon{id}}} ... on ProjectV2ItemIssueFieldValue{field{... on ProjectV2FieldCommon{id}}}}}}}}"
    let private item (node: JsonElement) : IoResult<Item> =
        decode (fun () ->
            let content = node.GetProperty "content"
            let kind = nonempty "__typename" content
            let repository, number =
                match kind with
                | "Issue" | "PullRequest" -> Some(nonempty "nameWithOwner" (content.GetProperty "repository")), Some(content.GetProperty("number").GetInt32())
                | "DraftIssue" -> None, None
                | _ -> invalidArg "content" "unsupported native content"
            { Id = nonempty "id" node; ContentId = nonempty "id" content; ContentKind = kind;
              Repository = repository; Number = number; UpdatedAt = nonempty "updatedAt" node;
              Archived = node.GetProperty("isArchived").GetBoolean(); Values = [] })
    // Match the maintained native-provider UTC timestamp rule in CiReads. Scalar native
    // value identity is additional evidence; the drained connection key remains item+field.
    let private planningScalarIdentity (nativeId: string) (updatedAt: string) =
        if not (bounded 4096 nativeId && bounded 128 updatedAt) then false
        else
            match DateTimeOffset.TryParse(updatedAt, Globalization.CultureInfo.InvariantCulture, Globalization.DateTimeStyles.RoundtripKind) with
            | true, parsed ->
                parsed.Offset = TimeSpan.Zero
                && System.Text.RegularExpressions.Regex.IsMatch(updatedAt, @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?(Z|\+00:00)$")
            | _ -> false

    let private value (node: JsonElement) : IoResult<ItemValue> =
        decode (fun () ->
            let kind = nonempty "__typename" node
            let names =
                match kind with
                | "ProjectV2ItemFieldTextValue" -> [ "id"; "updatedAt"; "text" ]
                | "ProjectV2ItemFieldSingleSelectValue" -> [ "id"; "updatedAt"; "optionId"; "name" ]
                | "ProjectV2ItemFieldDateValue" -> [ "id"; "updatedAt"; "date" ]
                | "ProjectV2ItemFieldNumberValue" -> [ "id"; "updatedAt"; "number" ]
                | "ProjectV2ItemFieldIterationValue" -> [ "id"; "updatedAt"; "iterationId"; "title"; "startDate"; "duration" ]
                | "ProjectV2ItemFieldMultiSelectValue" -> [ "id"; "updatedAt" ]
                | "ProjectV2ItemFieldRepositoryValue" | "ProjectV2ItemFieldLabelValue"
                | "ProjectV2ItemFieldMilestoneValue" | "ProjectV2ItemFieldPullRequestValue"
                | "ProjectV2ItemFieldReviewerValue" | "ProjectV2ItemFieldUserValue"
                | "ProjectV2ItemIssueFieldValue" -> []
                | _ -> invalidArg "value" "unknown native field-value identity"
            if kind = "ProjectV2ItemFieldTextValue" || kind = "ProjectV2ItemFieldSingleSelectValue" then
                if not (planningScalarIdentity (nonempty "id" node) (nonempty "updatedAt" node)) then
                    invalidArg "value" "native scalar planning identity/timestamp is unavailable or malformed"
            // System-field contents are not reconstructed or written. Their native field identity
            // is retained; complete selected planning values are retained above.
            let fieldId = nonempty "id" (node.GetProperty "field")
            let values = names |> List.collect (fun name -> [ name; node.GetProperty(name).GetRawText() ]);
            { Id = fieldId; Kind = kind; FieldId = fieldId; Value = JsonSerializer.Serialize values })
    let private projectDocument =
        "query{viewer{id login} organization(login:\"FS-GG\"){id login projectV2(number:3){id number title public viewerCanUpdate updatedAt}}}"
    let private projectObservation (observedAt: DateTimeOffset) (data: JsonElement) : IoResult<Snapshot> =
        decode (fun () ->
            let principal = data.GetProperty "viewer"
            let owner = data.GetProperty "organization"
            if nonempty "login" owner <> "FS-GG" then invalidArg "owner" "different native owner"
            let project = owner.GetProperty "projectV2"
            { ObservedAt = observedAt; PrincipalId = nonempty "id" principal; PrincipalLogin = nonempty "login" principal;
              OrganizationId = nonempty "id" owner; ProjectId = nonempty "id" project;
              ProjectTitle = nonempty "title" project; ProjectNumber = project.GetProperty("number").GetInt32();
              Private = not (project.GetProperty("public").GetBoolean()); CanUpdate = project.GetProperty("viewerCanUpdate").GetBoolean();
              UpdatedAt = nonempty "updatedAt" project; Fields = []; Items = []; Views = []; Issues = [] })
    let private project (transport: IGitHubTransport) (observedAt: DateTimeOffset) : IoResult<Snapshot> =
        GraphQl.read transport (query projectDocument []) (projectObservation observedAt)

    let private issueBatch (selected: SelectedIssue list) =
        let indexed: (string * SelectedIssue) list = selected |> List.mapi (fun index issue -> "issue" + string index, issue)
        let declaration = indexed |> List.map (fun (name, _) -> "$" + name + ":ID!") |> String.concat ","
        let nodes =
            indexed |> List.map (fun (name, _) ->
                name + ":node(id:$" + name + "){__typename ... on Issue{id number updatedAt state repository{nameWithOwner}}}") |> String.concat " "
        let variables: (string * Var) list = indexed |> List.map (fun (name, issue) -> name, VId issue.Id)
        indexed, declaration, nodes, variables
    let private issueObservations (indexed: (string * SelectedIssue) list) (data: JsonElement) : IoResult<NativeIssue list> =
        indexed |> List.map (fun (name, expected) ->
            match decode (fun () ->
                let issue = data.GetProperty name
                if nonempty "__typename" issue <> "Issue" then invalidArg "issue" "selected native node is not an issue"
                { Id = nonempty "id" issue; Repository = nonempty "nameWithOwner" (issue.GetProperty "repository");
                  Number = issue.GetProperty("number").GetInt32(); UpdatedAt = nonempty "updatedAt" issue; State = nonempty "state" issue }) with
            | Ok actual when actual.Id = expected.Id && actual.Repository = expected.Repository && actual.Number = expected.Number -> Ok actual
            | Ok _ -> invalid "selected native issue identity differs"
            | Error error -> Error error) |> sequence
    let private viewsDocument =
        "query($id:ID!,$after:String){node(id:$id){... on ProjectV2{id views(first:50,after:$after){pageInfo{hasNextPage endCursor} nodes{id name layout filter configuration{visibleFields(first:50){pageInfo{hasNextPage endCursor} nodes{... on ProjectV2FieldCommon{id name dataType}}}} groupByFields(first:50){pageInfo{hasNextPage endCursor} nodes{... on ProjectV2FieldCommon{id name dataType}}} verticalGroupByFields(first:50){pageInfo{hasNextPage endCursor} nodes{... on ProjectV2FieldCommon{id name dataType}}} sortByFields(first:50){pageInfo{hasNextPage endCursor} nodes{direction field{... on ProjectV2FieldCommon{id name dataType}}}}}}}}}"
    let private viewNode (node: JsonElement) : IoResult<View> =
        decode (fun () ->
            { Id = nonempty "id" node; Name = nonempty "name" node; Layout = nonempty "layout" node;
              Filter = (if node.GetProperty("filter").ValueKind = JsonValueKind.Null then "" else propertyString "filter" node);
              VisibleFieldIds = []; UnsupportedConfiguration = node.GetProperty("filter").GetRawText() })
    let private viewConfiguration (first: JsonElement option) (transport: IGitHubTransport) (viewId: string) (connection: string) : IoResult<string list> =
        let fieldSelection = "... on ProjectV2FieldCommon{id name dataType}"
        let selection =
            if connection = "sortByFields" then "direction field{" + fieldSelection + "}"
            else fieldSelection
        let page = connection + "(first:50,after:$after){pageInfo{hasNextPage endCursor} nodes{" + selection + "}}"
        let selection = if connection = "visibleFields" then "configuration{" + page + "}" else page
        let document = "query($id:ID!,$after:String){node(id:$id){... on ProjectV2View{id " + selection + "}}}"
        let decodeNode (node: JsonElement) =
            decode (fun () ->
                if connection = "sortByFields" then
                    let field = node.GetProperty "field"
                    let direction = nonempty "direction" node
                    if direction <> "ASC" && direction <> "DESC" then invalidArg "direction" "unknown native ordering"
                    nonempty "id" field + ":" + direction
                else nonempty "id" node)
        GraphQl.drain subject connection limits (fun after ->
            match after, first with
            | None, Some values -> GraphQl.page subject connection (fun (entry: string) -> if connection = "sortByFields" then entry.Substring(0, entry.LastIndexOf ':') else entry) decodeNode values
            | _ ->
                GraphQl.read transport (query document ([ "id", VId viewId ] @ cursor after)) (fun data ->
                    match decode (fun () ->
                        let parent = rootNode data
                        if nonempty "id" parent <> viewId then invalidArg "view" "different native view"
                        if connection = "visibleFields" then parent.GetProperty("configuration").GetProperty connection
                        else parent.GetProperty connection) with
                    | Error error -> Error error
                    | Ok values -> GraphQl.page subject connection (fun (entry: string) -> if connection = "sortByFields" then entry.Substring(0, entry.LastIndexOf ':') else entry) decodeNode values))
    let private collectViews (first: JsonElement option) (transport: IGitHubTransport) (projectId: string) : IoResult<View list> =
        let firstPages = Dictionary<string, JsonElement>()
        let node (element: JsonElement) =
            viewNode element |> Result.bind (fun view ->
                decode (fun () ->
                    firstPages.Add(view.Id, element.Clone())
                    view))
        drainCached first transport projectId "views" viewsDocument (fun (view: View) -> view.Id) node
        |> Result.bind (fun views ->
            views |> List.map (fun (view: View) ->
                let groups = [ "visibleFields"; "groupByFields"; "verticalGroupByFields"; "sortByFields" ]
                let read connection =
                    match decode (fun () ->
                        let parent = firstPages.[view.Id]
                        (if connection = "visibleFields" then parent.GetProperty("configuration").GetProperty connection
                         else parent.GetProperty connection).Clone()) with
                    | Error error -> Error error
                    | Ok page -> viewConfiguration (Some page) transport view.Id connection
                groups |> List.map read |> sequence
                |> Result.map (function
                    | [ visible; grouped; vertical; sorted ] ->
                        { view with VisibleFieldIds = visible;
                                    UnsupportedConfiguration = JsonSerializer.Serialize([ [ view.UnsupportedConfiguration ]; grouped; vertical; sorted ]) }
                    | _ -> invalidOp "fixed view configuration shape")) |> sequence)

    type private FirstSnapshot =
        { Project: Snapshot; Fields: JsonElement; Items: JsonElement; Views: JsonElement; Issues: NativeIssue list }

    let private firstSnapshot (transport: IGitHubTransport) (observedAt: DateTimeOffset) (scope: Scope) : IoResult<FirstSnapshot> =
        let indexed, declaration, nodes, variables = issueBatch scope.SelectedIssues
        let document =
            "query(" + declaration + "){viewer{id login} organization(login:\"FS-GG\"){id login projectV2(number:3){id number title public viewerCanUpdate updatedAt fields(first:50){totalCount pageInfo{hasNextPage endCursor} nodes{__typename ... on ProjectV2FieldCommon{id name dataType} ... on ProjectV2SingleSelectField{options{id name color description}}}} items(first:50){totalCount pageInfo{hasNextPage endCursor} nodes{id updatedAt isArchived content{__typename ... on Issue{id number repository{nameWithOwner}} ... on PullRequest{id number repository{nameWithOwner}} ... on DraftIssue{id}} fieldValues(first:50){pageInfo{hasNextPage endCursor} nodes{__typename ... on ProjectV2ItemFieldValueCommon{id updatedAt field{... on ProjectV2FieldCommon{id}}} ... on ProjectV2ItemFieldTextValue{text} ... on ProjectV2ItemFieldSingleSelectValue{optionId name} ... on ProjectV2ItemFieldDateValue{date} ... on ProjectV2ItemFieldNumberValue{number} ... on ProjectV2ItemFieldIterationValue{iterationId title startDate duration} ... on ProjectV2ItemFieldMultiSelectValue{field{... on ProjectV2FieldCommon{id}}} ... on ProjectV2ItemFieldRepositoryValue{field{... on ProjectV2FieldCommon{id}}} ... on ProjectV2ItemFieldLabelValue{field{... on ProjectV2FieldCommon{id}}} ... on ProjectV2ItemFieldMilestoneValue{field{... on ProjectV2FieldCommon{id}}} ... on ProjectV2ItemFieldPullRequestValue{field{... on ProjectV2FieldCommon{id}}} ... on ProjectV2ItemFieldReviewerValue{field{... on ProjectV2FieldCommon{id}}} ... on ProjectV2ItemFieldUserValue{field{... on ProjectV2FieldCommon{id}}} ... on ProjectV2ItemIssueFieldValue{field{... on ProjectV2FieldCommon{id}}}}}}} views(first:50){pageInfo{hasNextPage endCursor} nodes{id name layout filter configuration{visibleFields(first:50){pageInfo{hasNextPage endCursor} nodes{... on ProjectV2FieldCommon{id name dataType}}}} groupByFields(first:50){pageInfo{hasNextPage endCursor} nodes{... on ProjectV2FieldCommon{id name dataType}}} verticalGroupByFields(first:50){pageInfo{hasNextPage endCursor} nodes{... on ProjectV2FieldCommon{id name dataType}}} sortByFields(first:50){pageInfo{hasNextPage endCursor} nodes{direction field{... on ProjectV2FieldCommon{id name dataType}}}}}}}} " + nodes + "}"
        GraphQl.read transport (query document variables) (fun data ->
            projectObservation observedAt data |> Result.bind (fun project ->
                issueObservations indexed data |> Result.bind (fun issues ->
                    decode (fun () ->
                        let node = data.GetProperty("organization").GetProperty "projectV2"
                        { Project = project; Fields = node.GetProperty("fields").Clone();
                          Items = node.GetProperty("items").Clone(); Views = node.GetProperty("views").Clone(); Issues = issues }))))

    let private collectComplete (confirmIdentity: bool) (transport: IGitHubTransport) (scope: Scope) : IoResult<Snapshot> =
        let observedAt = DateTimeOffset.UtcNow
        match validateScope scope with
        | Error error -> Error error
        | Ok() ->
            firstSnapshot transport observedAt scope |> Result.bind (fun first ->
                let initial = first.Project
                if initial.OrganizationId <> "O_kgDOEYAWYw" || initial.ProjectId <> "PVT_kwDOEYAWY84Bldpa"
                   || initial.ProjectNumber <> 3 || initial.ProjectTitle <> "Coordination V2" || not initial.Private
                   || initial.PrincipalId <> scope.PrincipalId || initial.PrincipalLogin <> scope.PrincipalLogin then
                    invalid "native principal/private project identity differs before snapshot collection"
                else
                    drainCached (Some first.Fields) transport initial.ProjectId "fields" fieldsDocument (fun (field: Field) -> field.Id) field
                    |> Result.bind (fun fields ->
                        let firstPages = Dictionary<string, JsonElement>()
                        let node (element: JsonElement) : IoResult<Item> =
                            item element |> Result.bind (fun item ->
                                decode (fun () ->
                                    firstPages.Add(item.Id, element.GetProperty("fieldValues").Clone())
                                    item))
                        drainCached (Some first.Items) transport initial.ProjectId "items" itemsDocument (fun (item: Item) -> item.Id) node
                        |> Result.bind (fun items ->
                            items |> List.map (fun (item: Item) ->
                                drainCached (Some firstPages.[item.Id]) transport item.Id "fieldValues" valuesDocument (fun (value: ItemValue) -> value.Id) value
                                |> Result.map (fun values -> { item with Values = values })) |> sequence
                            |> Result.bind (fun items ->
                                collectViews (Some first.Views) transport initial.ProjectId |> Result.bind (fun views ->
                                    let snapshot: Snapshot =
                                        { initial with Fields = fields; Items = items; Views = views; Issues = first.Issues }
                                    if not confirmIdentity then Ok snapshot
                                    else
                                        project transport observedAt |> Result.bind (fun final ->
                                            if final <> initial then invalid "project/principal changed during complete snapshot collection"
                                            else Ok snapshot))))))

    let collect (transport: IGitHubTransport) (scope: Scope) : IoResult<Snapshot> =
        collectComplete true transport scope

    let private desiredTokens (view: DesiredView) =
        [ view.Id |> Option.defaultValue ""; view.Name; view.Layout; view.Filter;
          string view.VisibleFieldIds.Length ] @ view.VisibleFieldIds
    let private optionTokens (value: SelectOption) = [ value.Id; value.Name; value.Color; value.Description ]
    let private planningOptionTokens (value: PlanningOption) = [ value.Id; value.Name ]
    let private planningValueTokens (value: PlanningValue) =
        match value with
        | PlanningText text -> [ "text"; text ]
        | PlanningSelect option -> "select" :: planningOptionTokens option
    let private scopeTokens (scope: Scope) =
        [ scope.SourceRevision; scope.ArtifactSha256; scope.CliArtifactSha256; scope.PrincipalId; scope.PrincipalLogin;
          scope.StatusFieldId; scope.RoadmapFieldId; scope.TrackFieldId; scope.ObservationFieldId;
          scope.InReviewColor; scope.InReviewDescription; string scope.SelectedIssues.Length ]
        @ (scope.SelectedIssues |> List.collect (fun (issue: SelectedIssue) -> [ issue.Id; issue.Repository; string issue.Number; issue.Slice ]))
        @ [ string scope.Views.Length ] @ (scope.Views |> List.collect desiredTokens)
        @ [ string scope.InitialPlanning.Length ]
        @ (scope.InitialPlanning |> List.collect (fun (initial: InitialPlanning) ->
            [ initial.IssueId; initial.Roadmap ] @ planningOptionTokens initial.Track
            @ (match initial.Status with None -> [ "status-omitted" ] | Some status -> "status-selected" :: planningOptionTokens status)))
        @ (match scope.WorkLevel with
           | None -> []
           | Some plan ->
               [ "work-level/1"; plan.FieldId |> Option.defaultValue ""; string plan.Assignments.Length ]
               @ (plan.Assignments |> List.collect (fun value -> [ value.IssueId; value.Name ])))
    let private snapshotTokens (snapshot: Snapshot) =
        [ snapshot.PrincipalId; snapshot.PrincipalLogin; snapshot.OrganizationId; snapshot.ProjectId;
          snapshot.ProjectTitle; string snapshot.ProjectNumber; string snapshot.Private;
          string snapshot.CanUpdate; snapshot.UpdatedAt; string snapshot.Fields.Length ]
        @ (snapshot.Fields |> List.sortBy (fun (field: Field) -> field.Id) |> List.collect (fun (field: Field) ->
            [ field.Id; field.Name; field.DataType; string field.Options.Length ] @ (field.Options |> List.collect optionTokens)))
        @ [ string snapshot.Items.Length ]
        @ (snapshot.Items |> List.sortBy (fun (item: Item) -> item.Id) |> List.collect (fun (item: Item) ->
            [ item.Id; item.ContentId; item.ContentKind; item.Repository |> Option.defaultValue "";
              item.Number |> Option.map string |> Option.defaultValue ""; item.UpdatedAt; string item.Archived; string item.Values.Length ]
            @ (item.Values |> List.sortBy (fun (value: ItemValue) -> value.Id) |> List.collect (fun (value: ItemValue) ->
                [ value.Id; value.FieldId; value.Kind; value.Value ]))))
        @ [ string snapshot.Views.Length ]
        @ (snapshot.Views |> List.sortBy (fun (view: View) -> view.Id) |> List.collect (fun (view: View) ->
            [ view.Id; view.Name; view.Layout; view.Filter; view.UnsupportedConfiguration; string view.VisibleFieldIds.Length ] @ view.VisibleFieldIds))
        @ [ string snapshot.Issues.Length ]
        @ (snapshot.Issues |> List.sortBy (fun (issue: NativeIssue) -> issue.Id) |> List.collect (fun (issue: NativeIssue) ->
            [ issue.Id; issue.Repository; string issue.Number; issue.UpdatedAt; issue.State ]))
    let private changeTokens (change: Change) =
        match change with
        | CreateWorkLevel -> [ "create-work-level/1" ]
        | AddInReview(id, retained, color, description) ->
            [ "status"; id; color; description; string retained.Length ] @ (retained |> List.collect optionTokens)
        | AddMembership id -> [ "membership"; id ]
        | InitializePlanningValue(issueId, itemId, fieldId, value) ->
            [ "initialize-absent"; issueId; itemId; fieldId ] @ planningValueTokens value
        | CreateView view -> "create-view" :: desiredTokens view
        | UpdateView(id, view) -> [ "update-view"; id ] @ desiredTokens view
    let private hash (tokens: string list) =
        SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize tokens)) |> Convert.ToHexString |> _.ToLowerInvariant()

    let private samePlanningValue (expected: PlanningValue) (actual: ItemValue) =
        try
            let tokens = JsonSerializer.Deserialize<string array>(actual.Value)
            if isNull tokens || tokens.Length % 2 <> 0 then false
            else
                let fields = tokens |> Array.chunkBySize 2 |> Array.map (fun (pair: string array) -> pair.[0], pair.[1]) |> Map.ofArray
                let text (name: string) =
                    use document = JsonDocument.Parse(fields.[name])
                    document.RootElement.GetString()
                planningScalarIdentity (text "id") (text "updatedAt")
                && (match expected with
                    | PlanningText value -> actual.Kind = "ProjectV2ItemFieldTextValue" && text "text" = value
                    | PlanningSelect value ->
                        actual.Kind = "ProjectV2ItemFieldSingleSelectValue" && text "optionId" = value.Id && text "name" = value.Name)
        with _ -> false

    let private initialChanges (scope: Scope) (snapshot: Snapshot) (fields: Map<string, Field>) : IoResult<Change list> =
        scope.InitialPlanning
        |> List.map (fun (initial: InitialPlanning) ->
            let declarations: (string * PlanningValue) list =
                [ (scope.TrackFieldId, PlanningSelect initial.Track);
                  (scope.RoadmapFieldId, PlanningText initial.Roadmap) ]
                @ (initial.Status |> Option.map (fun status -> [ (scope.StatusFieldId, PlanningSelect status) ]) |> Option.defaultValue [])
            let selected = scope.SelectedIssues |> List.find (fun (issue: SelectedIssue) -> issue.Id = initial.IssueId)
            let values =
                declarations |> List.map (fun (fieldId, desired) ->
                    let field = fields.[fieldId]
                    let declared =
                        match desired with
                        | PlanningText _ -> field.DataType = "TEXT"
                        | PlanningSelect selected ->
                            field.DataType = "SINGLE_SELECT"
                            && (field.Options |> List.filter (fun (option: SelectOption) -> option.Id = selected.Id && option.Name = selected.Name) |> List.length) = 1
                    if not declared then invalid "declared planning field type or exact option ID/name differs from native schema"
                    else
                        match snapshot.Items |> List.filter (fun (item: Item) -> item.ContentId = initial.IssueId) with
                        | [] -> Ok [] // Membership must first correlate and independently read back its native ID.
                        | [ item ] when item.ContentKind = "Issue" && not item.Archived
                                      && item.Repository = Some selected.Repository && item.Number = Some selected.Number ->
                            match item.Values |> List.filter (fun (value: ItemValue) -> value.FieldId = fieldId) with
                            | [] -> Ok [ InitializePlanningValue(initial.IssueId, item.Id, fieldId, desired) ]
                            | [ existing ] when samePlanningValue desired existing -> Ok []
                            | _ -> invalid "existing planning value conflicts with initialization; preserve human/automation value and stop"
                        | _ -> invalid "initial planning membership identity is ambiguous or archived")
            sequence values |> Result.map List.concat)
        |> sequence
        |> Result.map List.concat

    let private workLevelOptions =
        [ ("Outcome", "PURPLE", "Full owning outcome promise; not a schedulable delivery")
          ("Delivery", "BLUE", "Independently meaningful six-field delivery; enrollment and action admission remain required") ]

    let private workLevelField (scope: Scope) (snapshot: Snapshot) : IoResult<Field option> =
        match scope.WorkLevel with
        | None -> Ok None
        | Some plan ->
            let named = snapshot.Fields |> List.filter (fun field -> field.Name = "Work level")
            match plan.FieldId, named with
            | None, [] -> Ok None
            | Some id, [ field ] when field.Id = id && field.DataType = "SINGLE_SELECT"
                                      && field.Options.Length = 2
                                      && unique (field.Options |> List.map _.Id)
                                      && (field.Options |> List.forall (fun option ->
                                          bounded 256 option.Id && List.contains (option.Name, option.Color, option.Description) workLevelOptions))
                                      && (field.Options |> List.map _.Name |> Set.ofList) = Set.ofList [ "Outcome"; "Delivery" ] -> Ok(Some field)
            | _ -> invalid "Work level native identity/type/options differ or same-name field already exists; preserve and reconcile explicitly"

    let private workLevelChanges (scope: Scope) (snapshot: Snapshot) : IoResult<Change list> =
        workLevelField scope snapshot |> Result.bind (fun field ->
            match scope.WorkLevel, field with
            | None, _ -> Ok []
            | Some _, None -> Ok [ CreateWorkLevel ]
            | Some plan, Some field ->
                plan.Assignments |> List.map (fun assignment ->
                    let selected = scope.SelectedIssues |> List.find (fun issue -> issue.Id = assignment.IssueId)
                    let option = field.Options |> List.find (fun value -> value.Name = assignment.Name)
                    let desired = PlanningSelect { Id = option.Id; Name = option.Name }
                    match snapshot.Items |> List.filter (fun item -> item.ContentId = assignment.IssueId) with
                    | [] -> Ok [] // Correlated membership readback precedes initialization.
                    | [ item ] when item.ContentKind = "Issue" && not item.Archived
                                    && item.Repository = Some selected.Repository && item.Number = Some selected.Number ->
                        match item.Values |> List.filter (fun value -> value.FieldId = field.Id) with
                        | [] -> Ok [ InitializePlanningValue(assignment.IssueId, item.Id, field.Id, desired) ]
                        | [ existing ] when samePlanningValue desired existing -> Ok []
                        | _ -> invalid "existing Work level conflicts or is unreadable; never overwrite intent"
                    | _ -> invalid "Work level initialization membership is ambiguous, archived or mismatched")
                |> sequence |> Result.map List.concat)

    let prepare (scope: Scope) (snapshot: Snapshot) : IoResult<Prepared> =
        match validateScope scope with
        | Error error -> Error error
        | Ok() ->
            let fields = snapshot.Fields |> List.map (fun (field: Field) -> field.Id, field) |> Map.ofList
            let expected : (string * string * string) list =
                [ (scope.StatusFieldId, "Status", "SINGLE_SELECT");
                  (scope.TrackFieldId, "Track", "SINGLE_SELECT");
                  (scope.RoadmapFieldId, "Roadmap", "TEXT");
                  (scope.ObservationFieldId, "Observation", "SINGLE_SELECT") ]
            let fieldMismatch =
                expected |> List.exists (fun (id, name, kind) ->
                    (fields |> Map.tryFind id |> Option.forall (fun (field: Field) -> field.Name <> name || field.DataType <> kind))
                    || (snapshot.Fields |> List.filter (fun (field: Field) -> field.Name = name) |> List.length) <> 1)
            let issueMismatch =
                scope.SelectedIssues |> List.exists (fun (selected: SelectedIssue) ->
                    snapshot.Issues |> List.filter (fun (issue: NativeIssue) -> issue.Id = selected.Id)
                    |> function
                       | [ issue ] -> issue.Repository <> selected.Repository || issue.Number <> selected.Number
                       | _ -> true)
            if snapshot.OrganizationId <> "O_kgDOEYAWYw" || snapshot.ProjectId <> "PVT_kwDOEYAWY84Bldpa"
               || snapshot.ProjectNumber <> 3 || snapshot.ProjectTitle <> "Coordination V2" || not snapshot.Private then
                invalid "native owner/private Project 3 identity differs"
            elif snapshot.PrincipalId <> scope.PrincipalId || snapshot.PrincipalLogin <> scope.PrincipalLogin then
                invalid "native principal differs from the explicitly selected invocation"
            elif fieldMismatch || not (unique (snapshot.Fields |> List.map (fun (field: Field) -> field.Id))) then
                invalid "predecessor field identity/name/type differs"
            elif issueMismatch || snapshot.Issues.Length <> scope.SelectedIssues.Length then
                invalid "selected native population identity is incomplete or changed"
            elif not (unique (snapshot.Items |> List.map (fun (item: Item) -> item.Id)))
                 || not (unique (snapshot.Items |> List.map (fun (item: Item) -> item.ContentId))) then
                invalid "duplicate native membership/content identities"
            elif snapshot.Items |> List.exists (fun (item: Item) ->
                    not (unique (item.Values |> List.map (fun (value: ItemValue) -> value.FieldId)))
                    || item.Values |> List.exists (fun (value: ItemValue) -> not (fields.ContainsKey value.FieldId))) then
                invalid "native item values contain duplicate or unknown field identities"
            else
                let status = fields.[scope.StatusFieldId]
                let names = status.Options |> List.map (fun (value: SelectOption) -> value.Name) |> Set.ofList
                let old = Set.ofList [ "Backlog"; "Ready"; "In progress"; "Blocked"; "Done" ]
                if not (unique (status.Options |> List.map (fun (value: SelectOption) -> value.Id)))
                   || not (unique (status.Options |> List.map (fun (value: SelectOption) -> value.Name)))
                   || (names <> old && names <> old.Add "In review") then
                    invalid "Status is neither the exact retained predecessor nor its six-option successor"
                elif status.Options |> List.exists (fun (option: SelectOption) ->
                        option.Name = "In review" && (option.Color <> scope.InReviewColor || option.Description <> scope.InReviewDescription)) then
                    invalid "existing In review descriptor differs; reconcile with its owner without rewriting existing options"
                else
                    let statusChange =
                        if names.Contains "In review" then []
                        else [ AddInReview(status.Id, status.Options, scope.InReviewColor, scope.InReviewDescription) ]
                    let membership =
                        scope.SelectedIssues |> List.choose (fun (issue: SelectedIssue) ->
                            if snapshot.Items |> List.exists (fun (item: Item) -> item.ContentId = issue.Id) then None
                            else Some(AddMembership issue.Id))
                    let viewChanges =
                        scope.Views |> List.map (fun (desired: DesiredView) ->
                            let candidates =
                                snapshot.Views |> List.filter (fun (view: View) ->
                                    match desired.Id with Some id -> view.Id = id | None -> view.Name = desired.Name)
                            if (snapshot.Views |> List.exists (fun (view: View) -> view.Name = desired.Name && desired.Id.IsSome && desired.Id <> Some view.Id)) then
                                invalid "requested view name collides with another native identity"
                            elif desired.VisibleFieldIds |> List.exists (fields.ContainsKey >> not) then
                                invalid "requested view references an unknown native field"
                            else
                                match desired.Id, candidates with
                                | Some _, [] -> invalid "explicit existing view is absent"
                                | None, [] -> Ok [ CreateView desired ]
                                | _, [ current ] ->
                                    let preservedColumns =
                                        desired.VisibleFieldIds = current.VisibleFieldIds
                                        || (scope.WorkLevel |> Option.bind _.FieldId |> Option.exists (fun id ->
                                            not (List.contains id current.VisibleFieldIds)
                                            && desired.VisibleFieldIds = current.VisibleFieldIds @ [ id ]))
                                    let repurposedPopulationView =
                                        current.Name.Equals("All items", StringComparison.OrdinalIgnoreCase)
                                        && (desired.Name <> current.Name || desired.Filter <> "")
                                    if scope.WorkLevel.IsSome && (current.Layout <> desired.Layout || not preservedColumns
                                       || repurposedPopulationView
                                       || (desired.Name.Equals("All items", StringComparison.OrdinalIgnoreCase) && desired.Filter <> "")) then
                                        invalid "successor view change must preserve layout/columns and unfiltered All items; only explicit Work level append is allowed"
                                    elif current.Name = desired.Name && current.Layout = desired.Layout
                                       && current.Filter = desired.Filter && current.VisibleFieldIds = desired.VisibleFieldIds then Ok []
                                    elif desired.Id.IsNone then invalid "same-name existing view differs; select its exact native ID before updating"
                                    else Ok [ UpdateView(current.Id, desired) ]
                                | _ -> invalid "view identity/name discovery is ambiguous")
                    sequence viewChanges |> Result.bind (fun changes ->
                        initialChanges scope snapshot fields |> Result.bind (fun initializations ->
                          workLevelChanges scope snapshot |> Result.map (fun classifications ->
                            let all = classifications @ statusChange @ membership @ initializations @ List.concat changes
                            let ownerActions =
                                [ "Only explicitly declared absent Status, Track and Roadmap values may be initialized; existing values are never overwritten.";
                                  "Initialization assumes a cooperative owner-controlled no-edit window; no provider if-absent/CAS exists, and final read-to-send races cannot be prevented.";
                                  "Grouping, sorting, column order and visual verification require supported authenticated UI actions.";
                                  "Readback IDs must be independently finalized into protected enrollment before v4 consumer activation.";
                                  "Compare-before-write is not CAS; apply requires a bounded participating single-writer planning window." ]
                                @ (if snapshot.CanUpdate then [] else [ "Native principal lacks project update capability; this read-only preparation grants no effect permission." ])
                                @ (scope.InitialPlanning |> List.choose (fun (initial: InitialPlanning) ->
                                    if initial.Status.IsNone then Some("Initial Status is intentionally unresolved for " + initial.IssueId) else None))
                            let ownerActions =
                                if scope.WorkLevel.IsNone then ownerActions
                                else
                                    (ownerActions |> List.map (fun value ->
                                        if value = "Readback IDs must be independently finalized into protected enrollment before v4 consumer activation." then
                                            "Independently read-back Work level/option IDs and selected enrollment require binding5/schema3 acceptance; existing binding4 is unchanged."
                                        else value))
                                    @ [ "Only explicitly selected absent Work level values may be initialized; conflicts remain held and no outcome closure is inferred." ]
                            { Scope = scope; Snapshot = snapshot; Changes = all; OwnerActions = ownerActions; PreparedPath = None;
                              Digest = hash (scopeTokens scope @ snapshotTokens snapshot @ [ string all.Length ] @ (all |> List.collect changeTokens)) })))

    let private jsonString (value: string) : JsonNode = JsonValue.Create value
    let private objectNode (values: (string * JsonNode) list) : JsonObject =
        let result = JsonObject()
        for name, value in values do result.Add(name, value)
        result
    let private arrayNode (values: JsonNode list) : JsonArray =
        let result = JsonArray()
        for value in values do result.Add value
        result
    let request (prepared: Prepared) (change: Change) : IoResult<Transport.Request> =
        match prepare prepared.Scope prepared.Snapshot with
        | Ok _ when not prepared.Snapshot.CanUpdate -> invalid "native project update capability is absent; no effect reconstructed"
        | Error error -> Error error
        | Ok current when current.Digest <> prepared.Digest || not (List.contains change current.Changes) ->
            invalid "effect is absent from the exact freshly reconstructed prepared delta"
        | Ok _ ->
            let id (name: string) (value: string) = name, jsonString value
            let visible (view: DesiredView) =
                objectNode [ "visibleFieldIds", arrayNode (view.VisibleFieldIds |> List.map jsonString) ] :> JsonNode
            let selection (name: string) (operation: string) (identity: string) =
                $"mutation($input:{name}!){{{operation}(input:$input){{clientMutationId {identity}}}}}"
            let document, input =
                match change with
                | CreateWorkLevel ->
                    let options = workLevelOptions |> List.map (fun (name, color, description) ->
                        objectNode [ id "name" name; id "color" color; id "description" description ] :> JsonNode)
                    (selection "CreateProjectV2FieldInput" "createProjectV2Field" "projectV2Field{... on ProjectV2FieldCommon{id}}",
                     objectNode [ id "projectId" prepared.Snapshot.ProjectId; id "dataType" "SINGLE_SELECT";
                                  id "name" "Work level"; "singleSelectOptions", arrayNode options ])
                | AddInReview(fieldId, retained, color, description) ->
                    let options =
                        (retained |> List.map (fun (option: SelectOption) ->
                            objectNode [ id "id" option.Id; id "name" option.Name; id "color" option.Color; id "description" option.Description ] :> JsonNode))
                        @ [ objectNode [ id "name" "In review"; id "color" color; id "description" description ] :> JsonNode ]
                    (selection "UpdateProjectV2FieldInput" "updateProjectV2Field" "projectV2Field{... on ProjectV2FieldCommon{id}}",
                     objectNode [ id "fieldId" fieldId; "singleSelectOptions", arrayNode options ])
                | AddMembership issueId ->
                    (selection "AddProjectV2ItemByIdInput" "addProjectV2ItemById" "item{id}",
                     objectNode [ id "projectId" prepared.Snapshot.ProjectId; id "contentId" issueId ])
                | InitializePlanningValue(_, itemId, fieldId, desired) ->
                    let selectedValue: JsonNode =
                        match desired with
                        | PlanningText text -> objectNode [ id "text" text ] :> JsonNode
                        | PlanningSelect option -> objectNode [ id "singleSelectOptionId" option.Id ] :> JsonNode
                    (selection "UpdateProjectV2ItemFieldValueInput" "updateProjectV2ItemFieldValue" "projectV2Item{id}",
                     objectNode [ id "projectId" prepared.Snapshot.ProjectId; id "itemId" itemId;
                                  id "fieldId" fieldId; "value", selectedValue ])
                | CreateView view ->
                    (selection "CreateProjectV2ViewInput" "createProjectV2View" "projectV2View{id}",
                     objectNode [ id "projectId" prepared.Snapshot.ProjectId; id "name" view.Name; id "layout" view.Layout; "configuration", visible view ])
                | UpdateView(viewId, view) ->
                    (selection "UpdateProjectV2ViewInput" "updateProjectV2View" "projectV2View{id}",
                     objectNode [ id "viewId" viewId; id "name" view.Name; id "layout" view.Layout; id "filter" view.Filter; "configuration", visible view ])
            input.Add("clientMutationId", jsonString (hash (prepared.Digest :: changeTokens change)))
            let variables = objectNode [ "input", input ]
            let body = objectNode [ id "query" document; "variables", variables ]
            Ok(
                { Method = "POST"; Path = "graphql"; Query = []; Body = Json(body.ToJsonString());
                  Budget = GraphQl; IfNoneMatch = None; Subject = subject })
    let private closed (names: string list) (node: JsonElement) =
        if node.ValueKind <> JsonValueKind.Object then invalidArg "json" "expected closed object"
        let actual = node.EnumerateObject() |> Seq.map (fun entry -> entry.Name) |> Seq.toList
        if actual.Length <> names.Length || Set.ofList actual <> Set.ofList names then
            invalidArg "json" "unknown, missing or duplicate property"
    let private array (limit: int) (node: JsonElement) =
        if node.ValueKind <> JsonValueKind.Array || node.GetArrayLength() > limit then invalidArg "json" "bounded array required"
        node.EnumerateArray() |> Seq.toList
    let private parseScopeNode (node: JsonElement) : Scope =
        let hasWorkLevel = node.TryGetProperty("workLevel") |> fst
        [ "sourceRevision"; "artifactSha256"; "cliArtifactSha256"; "principalId"; "principalLogin";
                 "selectedIssues"; "statusFieldId"; "roadmapFieldId"; "trackFieldId"; "observationFieldId";
                 "inReviewColor"; "inReviewDescription"; "views"; "initialPlanning" ]
                @ (if hasWorkLevel then [ "workLevel" ] else []) |> fun names -> closed names node
        let issues: SelectedIssue list =
            array 100 (node.GetProperty "selectedIssues") |> List.map (fun (issue: JsonElement) ->
                closed [ "id"; "repository"; "number"; "slice" ] issue
                { Id = nonempty "id" issue; Repository = nonempty "repository" issue;
                  Number = issue.GetProperty("number").GetInt32(); Slice = nonempty "slice" issue })
        let views: DesiredView list =
            array 10 (node.GetProperty "views") |> List.map (fun (view: JsonElement) ->
                closed [ "id"; "name"; "layout"; "filter"; "visibleFieldIds" ] view
                let id = view.GetProperty "id"
                { Id = (if id.ValueKind = JsonValueKind.Null then None else Some(nonempty "id" view));
                  Name = nonempty "name" view; Layout = nonempty "layout" view; Filter = propertyString "filter" view;
                  VisibleFieldIds = (array 50 (view.GetProperty "visibleFieldIds") |> List.map (fun field -> field.GetString())) })
        let planningOption (value: JsonElement) : PlanningOption =
            closed [ "id"; "name" ] value
            { Id = nonempty "id" value; Name = nonempty "name" value }
        let initialPlanning: InitialPlanning list =
            array 100 (node.GetProperty "initialPlanning")
            |> List.map (fun (initial: JsonElement) ->
                closed [ "issueId"; "track"; "roadmap"; "status" ] initial
                let status = initial.GetProperty "status"
                { IssueId = nonempty "issueId" initial;
                  Track = planningOption (initial.GetProperty "track");
                  Roadmap = propertyString "roadmap" initial;
                  Status = (if status.ValueKind = JsonValueKind.Null then None else Some(planningOption status)) })
        let workLevel =
            if not hasWorkLevel then None
            else
                let value = node.GetProperty "workLevel"
                closed [ "schema"; "fieldId"; "assignments" ] value
                if propertyString "schema" value <> "fs.gg.board-v2.work-level-migration/1" then invalidArg "schema" "different Work level migration schema"
                let field = value.GetProperty "fieldId"
                Some { FieldId = (if field.ValueKind = JsonValueKind.Null then None else Some(nonempty "fieldId" value))
                       Assignments = array 100 (value.GetProperty "assignments") |> List.map (fun assignment ->
                           closed [ "issueId"; "name" ] assignment
                           { IssueId = nonempty "issueId" assignment; Name = nonempty "name" assignment }) }
        { SourceRevision = nonempty "sourceRevision" node; ArtifactSha256 = nonempty "artifactSha256" node;
          CliArtifactSha256 = nonempty "cliArtifactSha256" node;
          PrincipalId = nonempty "principalId" node; PrincipalLogin = nonempty "principalLogin" node;
          SelectedIssues = issues; StatusFieldId = nonempty "statusFieldId" node;
          RoadmapFieldId = nonempty "roadmapFieldId" node; TrackFieldId = nonempty "trackFieldId" node;
          ObservationFieldId = nonempty "observationFieldId" node; InReviewColor = nonempty "inReviewColor" node;
          InReviewDescription = propertyString "inReviewDescription" node; Views = views; InitialPlanning = initialPlanning; WorkLevel = workLevel }
    let parseScope (json: string) : IoResult<Scope> =
        if Encoding.UTF8.GetByteCount json > 262144 then invalid "scope input exceeds 256 KiB"
        else
            decode (fun () ->
                use document = JsonDocument.Parse(json, JsonDocumentOptions(MaxDepth = 12))
                parseScopeNode document.RootElement)
            |> Result.bind (fun scope -> validateScope scope |> Result.map (fun () -> scope))

    let private stringsNode (values: string list) = arrayNode (values |> List.map jsonString) :> JsonNode
    let private desiredNode (view: DesiredView) : JsonNode =
        objectNode [ "id", (view.Id |> Option.map jsonString |> Option.defaultValue null);
                     "name", jsonString view.Name; "layout", jsonString view.Layout;
                     "filter", jsonString view.Filter; "visibleFieldIds", stringsNode view.VisibleFieldIds ] :> JsonNode
    let private scopeNode (scope: Scope) : JsonNode =
        let issues =
            scope.SelectedIssues |> List.map (fun (issue: SelectedIssue) ->
                objectNode [ "id", jsonString issue.Id; "repository", jsonString issue.Repository;
                             "number", JsonValue.Create issue.Number; "slice", jsonString issue.Slice ] :> JsonNode)
        let optionNode (option: PlanningOption) : JsonNode =
            objectNode [ "id", jsonString option.Id; "name", jsonString option.Name ] :> JsonNode
        let initialPlanning: JsonNode list =
            scope.InitialPlanning
            |> List.map (fun (initial: InitialPlanning) ->
                objectNode [ "issueId", jsonString initial.IssueId;
                             "track", optionNode initial.Track;
                             "roadmap", jsonString initial.Roadmap;
                             "status", (initial.Status |> Option.map optionNode |> Option.defaultValue null) ] :> JsonNode)
        let result = objectNode [ "sourceRevision", jsonString scope.SourceRevision; "artifactSha256", jsonString scope.ArtifactSha256;
                     "cliArtifactSha256", jsonString scope.CliArtifactSha256;
                     "principalId", jsonString scope.PrincipalId; "principalLogin", jsonString scope.PrincipalLogin;
                     "selectedIssues", arrayNode issues; "statusFieldId", jsonString scope.StatusFieldId;
                     "roadmapFieldId", jsonString scope.RoadmapFieldId; "trackFieldId", jsonString scope.TrackFieldId;
                     "observationFieldId", jsonString scope.ObservationFieldId; "inReviewColor", jsonString scope.InReviewColor;
                     "inReviewDescription", jsonString scope.InReviewDescription;
                     "views", arrayNode (scope.Views |> List.map desiredNode); "initialPlanning", arrayNode initialPlanning ]
        match scope.WorkLevel with
        | None -> ()
        | Some plan ->
            let assignments = plan.Assignments |> List.map (fun assignment ->
                objectNode [ "issueId", jsonString assignment.IssueId; "name", jsonString assignment.Name ] :> JsonNode)
            result.Add("workLevel", objectNode [ "schema", jsonString "fs.gg.board-v2.work-level-migration/1";
                       "fieldId", (plan.FieldId |> Option.map jsonString |> Option.defaultValue null);
                       "assignments", arrayNode assignments ])
        result :> JsonNode
    let encodePrepared (prepared: Prepared) : string =
        let preparedPath =
            match prepared.PreparedPath with
            | Some path when bounded 4096 path && Path.IsPathFullyQualified path && Path.GetFullPath path = path -> path
            | _ -> invalidArg "prepared" "canonical original invocation path must be bound before encoding apply input"
        // Exact stable token arrays retain every determining native value without union/option
        // serializer defaults. This is an operation report, never a normal v4 binding.
        let root =
            objectNode [ "schema", jsonString (if prepared.Scope.WorkLevel.IsSome then "fs.gg.board-v2.migration-prepare/2" else "fs.gg.board-v2.migration-prepare/1");
                         "preparedPath", jsonString preparedPath;
                         "observedAt", jsonString (prepared.Snapshot.ObservedAt.ToString "O");
                         "scope", scopeNode prepared.Scope; "snapshot", stringsNode (snapshotTokens prepared.Snapshot);
                         "changes", arrayNode (prepared.Changes |> List.map (changeTokens >> stringsNode));
                         "ownerActions", stringsNode prepared.OwnerActions; "digest", jsonString prepared.Digest ]
        let json = root.ToJsonString(JsonSerializerOptions(WriteIndented = true))
        if Encoding.UTF8.GetByteCount json > 4 * 1024 * 1024 then invalidOp "bounded private migration report exceeds 4 MiB"
        json
    let decodePrepared (json: string) : IoResult<Scope * string * DateTimeOffset * string> =
        if Encoding.UTF8.GetByteCount json > 4 * 1024 * 1024 then invalid "prepared input exceeds 4 MiB"
        else
            decode (fun () ->
                use document = JsonDocument.Parse(json, JsonDocumentOptions(MaxDepth = 16))
                let root = document.RootElement
                closed [ "schema"; "preparedPath"; "observedAt"; "scope"; "snapshot"; "changes"; "ownerActions"; "digest" ] root
                let schema = propertyString "schema" root
                if schema <> "fs.gg.board-v2.migration-prepare/1" && schema <> "fs.gg.board-v2.migration-prepare/2" then invalidArg "schema" "different prepared schema"
                let preparedPath = nonempty "preparedPath" root
                if not (Path.IsPathFullyQualified preparedPath) || Path.GetFullPath preparedPath <> preparedPath then
                    invalidArg "preparedPath" "canonical original invocation path required"
                let scope = parseScopeNode (root.GetProperty "scope")
                if (schema = "fs.gg.board-v2.migration-prepare/2") <> scope.WorkLevel.IsSome then
                    invalidArg "schema" "Work level scope and prepared version differ"
                let digest = nonempty "digest" root
                if not (hex 64 digest) then invalidArg "digest" "malformed prepared digest"
                // Report payload remains retained verbatim; no JSON assertion supplies native facts.
                // The complete fresh native planner must reproduce this separately selected digest.
                scope, digest, root.GetProperty("observedAt").GetDateTimeOffset(), preparedPath)
            |> Result.bind (fun (scope, digest, observedAt, preparedPath) ->
                validateScope scope |> Result.map (fun () -> scope, digest, observedAt, preparedPath))
    let resolveReadback (scope: Scope) (before: Snapshot) (after: Snapshot) (change: Change) : IoResult<Scope> =
        match change with
        | CreateWorkLevel ->
            match scope.WorkLevel with
            | Some plan when plan.FieldId.IsNone && not (before.Fields |> List.exists (fun field -> field.Name = "Work level")) ->
                match after.Fields |> List.filter (fun field -> field.Name = "Work level") with
                | [ created ] when not (before.Fields |> List.exists (fun field -> field.Id = created.Id)) ->
                    let resolved = { scope with WorkLevel = Some { plan with FieldId = Some created.Id } }
                    workLevelField resolved after |> Result.map (fun _ -> resolved)
                | _ -> invalid "created Work level field identity is ambiguous; original invocation remains readback-only"
            | _ -> invalid "Work level creation was not explicitly selected from an absent preimage"
        | CreateView desired ->
            let prior = before.Views |> List.filter (fun (view: View) -> view.Name = desired.Name)
            let candidates = after.Views |> List.filter (fun (view: View) -> view.Name = desired.Name)
            match prior, candidates with
            | [], [ created ] when created.Layout = desired.Layout && created.Filter = ""
                                   && created.VisibleFieldIds = desired.VisibleFieldIds
                                   && not (before.Views |> List.exists (fun (view: View) -> view.Id = created.Id)) ->
                let resolvedViews: DesiredView list =
                    scope.Views
                    |> List.map (fun (view: DesiredView) ->
                        if view = desired then { view with Id = Some created.Id } else view)
                Ok({ scope with Views = resolvedViews })
            | _ -> invalid "created view identity/configuration is ambiguous; original invocation is readback-only"
        | _ -> Ok scope

    let validateTransition (scope: Scope) (before: Snapshot) (after: Snapshot) (change: Change) : IoResult<unit> =
        let normalize (snapshot: Snapshot) = { snapshot with ObservedAt = before.ObservedAt; UpdatedAt = before.UpdatedAt }
        let unchanged (snapshot: Snapshot) = snapshotTokens (normalize snapshot) = snapshotTokens (normalize before)
        let orthogonal (view: View) : IoResult<string list list> =
            decode (fun () ->
                let parts = JsonSerializer.Deserialize<string array array> view.UnsupportedConfiguration
                if isNull parts || parts.Length <> 4 || (parts |> Array.exists isNull) || parts.[0].Length <> 1 then
                    invalidArg "view" "complete native orthogonal configuration required"
                parts |> Array.skip 1 |> Array.map Array.toList |> Array.toList)
        let sameView (desired: DesiredView) (actual: View) =
            desired.Name = actual.Name && desired.Layout = actual.Layout && desired.Filter = actual.Filter
            && desired.VisibleFieldIds = actual.VisibleFieldIds
        match resolveReadback scope before after change |> Result.bind (fun resolved -> prepare resolved after) with
        | Error error -> Error error
        | Ok _ ->
            let reconciled =
                match change with
                | CreateWorkLevel ->
                    match after.Fields |> List.filter (fun field -> field.Name = "Work level") with
                    | [ added ] -> unchanged { after with Fields = after.Fields |> List.filter (fun field -> field.Id <> added.Id) }
                    | _ -> false
                | AddInReview(fieldId, retained, color, description) ->
                    match after.Fields |> List.filter (fun (field: Field) -> field.Id = fieldId) with
                    | [ field ] ->
                        match field.Options |> List.filter (fun (option: SelectOption) -> option.Name = "In review") with
                        | [ added ] when added.Color = color && added.Description = description
                                         && (retained |> List.forall (fun (option: SelectOption) -> option.Id <> added.Id))
                                         && field.Options = retained @ [ added ] ->
                            let fields = after.Fields |> List.map (fun (value: Field) -> if value.Id = fieldId then { value with Options = retained } else value)
                            unchanged ({ after with Fields = fields })
                        | _ -> false
                    | _ -> false
                | AddMembership issueId ->
                    let added = after.Items |> List.filter (fun (item: Item) -> item.ContentId = issueId)
                    match added, scope.SelectedIssues |> List.tryFind (fun (issue: SelectedIssue) -> issue.Id = issueId) with
                    | [ item ], Some issue when item.ContentKind = "Issue" && item.Repository = Some issue.Repository
                                               && item.Number = Some issue.Number && not item.Archived
                                               && (before.Items |> List.forall (fun (existing: Item) -> existing.Id <> item.Id && existing.ContentId <> issueId)) ->
                        unchanged ({ after with Items = (after.Items |> List.filter (fun (existing: Item) -> existing.Id <> item.Id)) })
                    | _ -> false
                | InitializePlanningValue(issueId, itemId, fieldId, desired) ->
                    let prior = before.Items |> List.filter (fun (item: Item) -> item.Id = itemId && item.ContentId = issueId)
                    let current = after.Items |> List.filter (fun (item: Item) -> item.Id = itemId && item.ContentId = issueId)
                    match prior, current with
                    | [ original ], [ observed ] when not (original.Values |> List.exists (fun (value: ItemValue) -> value.FieldId = fieldId)) ->
                        match observed.Values |> List.filter (fun (value: ItemValue) -> value.FieldId = fieldId) with
                        | [ initialized ] when samePlanningValue desired initialized ->
                            let retained: ItemValue list = observed.Values |> List.filter (fun (value: ItemValue) -> value.FieldId <> fieldId)
                            let normalized: Item = { observed with UpdatedAt = original.UpdatedAt; Values = retained }
                            let items: Item list = after.Items |> List.map (fun (item: Item) -> if item.Id = itemId then normalized else item)
                            unchanged ({ after with Items = items })
                        | _ -> false
                    | _ -> false
                | CreateView desired ->
                    match after.Views |> List.filter (fun (view: View) -> view.Name = desired.Name) with
                    | [ added ] when sameView ({ desired with Filter = "" }) added && (before.Views |> List.forall (fun (view: View) -> view.Id <> added.Id)) ->
                        unchanged ({ after with Views = (after.Views |> List.filter (fun (view: View) -> view.Id <> added.Id)) })
                    | _ -> false
                | UpdateView(viewId, desired) ->
                    match before.Views |> List.filter (fun (view: View) -> view.Id = viewId), after.Views |> List.filter (fun (view: View) -> view.Id = viewId) with
                    | [ original ], [ current ] when sameView desired current
                                                     && orthogonal original = orthogonal current
                                                     && Result.isOk (orthogonal original) ->
                        unchanged ({ after with Views = (after.Views |> List.map (fun (view: View) -> if view.Id = viewId then original else view)) })
                    | _ -> false
            if reconciled then Ok()
            else invalid "readback differs beyond the one exact effect; preserve original intent and stop writes for reconciliation"
    let observeTransition (scope: Scope) (before: Snapshot) (after: Snapshot) (change: Change) (returnedId: string) : IoResult<ReadbackObservation> =
        let identityPossible =
            bounded 256 returnedId
            && (match change with
                | CreateWorkLevel -> before.Fields |> List.forall (fun field -> field.Id <> returnedId)
                | AddInReview(fieldId, _, _, _) -> returnedId = fieldId
                | InitializePlanningValue(_, itemId, _, _) -> returnedId = itemId
                | UpdateView(viewId, _) -> returnedId = viewId
                | AddMembership _ -> before.Items |> List.forall (fun (item: Item) -> item.Id <> returnedId)
                | CreateView _ -> before.Views |> List.forall (fun (view: View) -> view.Id <> returnedId))
        let observedIdentity =
            match change with
            | CreateWorkLevel -> after.Fields |> List.filter (fun field -> field.Name = "Work level") |> function [ field ] -> Some field.Id | _ -> None
            | AddInReview(fieldId, _, _, _) -> Some fieldId
            | InitializePlanningValue(_, itemId, _, _) -> Some itemId
            | UpdateView(viewId, _) -> Some viewId
            | AddMembership issueId ->
                after.Items |> List.filter (fun (item: Item) -> item.ContentId = issueId)
                |> function [ item ] -> Some item.Id | _ -> None
            | CreateView desired ->
                after.Views |> List.filter (fun (view: View) -> view.Name = desired.Name)
                |> function [ view ] -> Some view.Id | _ -> None
        // The only pending state is the exact complete pre-effect observation.
        // Reuse the already allowed target item timestamp change for initialization;
        // no issue facts, other item timestamps, values or orthogonal view facts move.
        let pending =
            let items: Item list =
                match change with
                | InitializePlanningValue(issueId, itemId, _, _) ->
                    after.Items |> List.map (fun (item: Item) ->
                        match before.Items |> List.tryFind (fun (original: Item) -> original.Id = itemId && original.ContentId = issueId) with
                        | Some original when item.Id = itemId && item.ContentId = issueId -> { item with UpdatedAt = original.UpdatedAt }
                        | _ -> item)
                | _ -> after.Items
            let normalized: Snapshot =
                { after with ObservedAt = before.ObservedAt; UpdatedAt = before.UpdatedAt; Items = items }
            snapshotTokens normalized = snapshotTokens before
        prepare scope before |> Result.bind (fun prepared ->
            if not identityPossible || not (List.contains change prepared.Changes) then
                invalid "correlated native result identity or original selected effect differs"
            else
                match validateTransition scope before after change with
                | Ok() when observedIdentity = Some returnedId ->
                    resolveReadback scope before after change |> Result.map ObservedEffect
                | Ok() -> invalid "independent effect identity differs from correlated native result"
                | Error _ when pending -> Ok PendingVisibility
                | Error error -> Error error)
    let collectEffectReadback (transport: IGitHubTransport) (scope: Scope) (before: Snapshot) (change: Change) (returnedId: string) : IoResult<Snapshot> =
        collectComplete false transport scope |> Result.bind (fun snapshot ->
            match observeTransition scope before snapshot change returnedId with
            | Ok PendingVisibility -> Ok snapshot
            | _ ->
                // Only a complete exact pre-effect witness may omit this redundant read.
                // Any candidate settlement or contradiction keeps the strict final identity barrier.
                project transport snapshot.ObservedAt |> Result.bind (fun final ->
                    let initial: Snapshot = { snapshot with Fields = []; Items = []; Views = []; Issues = [] }
                    if final <> initial then invalid "project/principal changed during complete snapshot collection"
                    else Ok snapshot))

    // ---- item-scoped planning initialization (#5123) ----

    type ItemObservation =
        | ItemEffect of Item
        | ItemPending

    let private itemDocument =
        valuesDocument
            .Replace("query($id:ID!,$after:String)", "query($id:ID!)")
            .Replace("... on ProjectV2Item{id fieldValues(first:50,after:$after)",
                     "... on ProjectV2Item{id updatedAt isArchived content{__typename ... on Issue{id number repository{nameWithOwner}} ... on PullRequest{id number repository{nameWithOwner}} ... on DraftIssue{id}} fieldValues(first:50)")

    let collectItem (transport: IGitHubTransport) (itemId: string) : IoResult<Item> =
        GraphQl.read transport (query itemDocument [ "id", VId itemId ]) (fun data ->
            match decode (fun () -> rootNode data) with
            | Error error -> Error error
            | Ok node ->
                item node |> Result.bind (fun observed ->
                    if observed.Id <> itemId then invalid "item read returned a different identity"
                    else decode (fun () -> observed, node.GetProperty("fieldValues").Clone())))
        |> Result.bind (fun (observed, first) ->
            drainCached (Some first) transport itemId "fieldValues" valuesDocument (fun (value: ItemValue) -> value.Id) value
            |> Result.map (fun values -> { observed with Values = values }))

    let private sortedValues (values: ItemValue list) = values |> List.sortBy (fun (value: ItemValue) -> value.Id)

    let sameItem (expected: Item) (actual: Item) =
        { expected with Values = sortedValues expected.Values } = { actual with Values = sortedValues actual.Values }

    let itemTransition (before: Item) (after: Item) (change: Change) : IoResult<ItemObservation> =
        match change with
        | InitializePlanningValue(issueId, itemId, fieldId, desired) ->
            let identity (item: Item) = item.Id, item.ContentId, item.ContentKind, item.Repository, item.Number, item.Archived
            if before.Id <> itemId || before.ContentId <> issueId || identity before <> identity after then
                invalid "item identity differs across the item-scoped effect"
            elif before.Values |> List.exists (fun (value: ItemValue) -> value.FieldId = fieldId) then
                invalid "the selected planning value was not absent before the effect"
            else
                let others = after.Values |> List.filter (fun (value: ItemValue) -> value.FieldId <> fieldId)
                match after.Values |> List.filter (fun (value: ItemValue) -> value.FieldId = fieldId) with
                | [ initialized ] when samePlanningValue desired initialized && sortedValues others = sortedValues before.Values ->
                    Ok(ItemEffect after)
                | [] when sortedValues after.Values = sortedValues before.Values -> Ok ItemPending
                | _ -> invalid "item readback differs beyond the one exact effect; preserve original intent and stop writes for reconciliation"
        | _ -> invalid "only planning-value initialization is item-scoped"

    let validateGroup (scope: Scope) (before: Snapshot) (after: Snapshot) (applied: Change list) : IoResult<unit> =
        match prepare scope before with
        | Error error -> Error error
        | Ok prepared when applied.IsEmpty || applied |> List.exists (fun change -> not (List.contains change prepared.Changes)) ->
            invalid "a group effect is absent from its base delta"
        | Ok _ ->
            let targets =
                applied |> List.choose (function
                    | InitializePlanningValue(issueId, itemId, fieldId, desired) -> Some(issueId, itemId, fieldId, desired)
                    | _ -> None)
            if targets.Length <> applied.Length then invalid "only planning-value initialization is grouped"
            elif (targets |> List.map (fun (_, itemId, fieldId, _) -> itemId, fieldId) |> List.distinct |> List.length) <> targets.Length then
                invalid "a group initializes one value twice"
            else
                let present =
                    targets |> List.forall (fun (issueId, itemId, fieldId, desired) ->
                        match after.Items |> List.filter (fun (item: Item) -> item.Id = itemId && item.ContentId = issueId) with
                        | [ item ] ->
                            match item.Values |> List.filter (fun (value: ItemValue) -> value.FieldId = fieldId) with
                            | [ initialized ] -> samePlanningValue desired initialized
                            | _ -> false
                        | _ -> false)
                let touched = targets |> List.map (fun (_, itemId, _, _) -> itemId) |> Set.ofList
                let initialized = targets |> List.map (fun (_, itemId, fieldId, _) -> itemId, fieldId) |> Set.ofList
                // Only the group's own values and their items' timestamps may differ from the base.
                let items: Item list =
                    after.Items |> List.map (fun (item: Item) ->
                        match before.Items |> List.tryFind (fun (original: Item) -> original.Id = item.Id) with
                        | Some original when touched.Contains item.Id ->
                            { item with
                                UpdatedAt = original.UpdatedAt
                                Values = item.Values |> List.filter (fun (value: ItemValue) -> not (initialized.Contains(item.Id, value.FieldId))) }
                        | _ -> item)
                let normalized: Snapshot = { after with ObservedAt = before.ObservedAt; UpdatedAt = before.UpdatedAt; Items = items }
                if present && snapshotTokens normalized = snapshotTokens before then Ok()
                else invalid "group readback differs beyond its exact effects; preserve original intent and stop writes for reconciliation"

    let snapshotDigest (snapshot: Snapshot) = hash (snapshotTokens snapshot)
    let encodeReadback (scope: Scope) (snapshot: Snapshot) =
        let root =
            objectNode [ "schema", jsonString (if scope.WorkLevel.IsSome then "fs.gg.board-v2.migration-readback/2" else "fs.gg.board-v2.migration-readback/1");
                         "observedAt", jsonString (snapshot.ObservedAt.ToString "O");
                         "scope", scopeNode scope; "snapshot", stringsNode (snapshotTokens snapshot);
                         "snapshotDigest", jsonString (snapshotDigest snapshot);
                         "admission", jsonString "readback-only; original outcome and uncertainty retained" ]
        let json = root.ToJsonString(JsonSerializerOptions(WriteIndented = true))
        if Encoding.UTF8.GetByteCount json > 4 * 1024 * 1024 then invalidOp "bounded private migration report exceeds 4 MiB"
        json
