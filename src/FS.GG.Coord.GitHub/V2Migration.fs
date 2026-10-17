namespace FS.GG.Coord.GitHub

module V2Migration =
    open System
    open Errors
    open Transport

    type SelectedIssue =
        { Id: string; Repository: string; Number: int; Slice: string }

    type DesiredView =
        { Id: string option; Name: string; Layout: string; Filter: string
          VisibleFieldIds: string list }

    type Scope =
        { SourceRevision: string; ArtifactSha256: string; CliArtifactSha256: string; PrincipalId: string
          PrincipalLogin: string; SelectedIssues: SelectedIssue list
          StatusFieldId: string; RoadmapFieldId: string; TrackFieldId: string
          ObservationFieldId: string; InReviewColor: string; InReviewDescription: string
          Views: DesiredView list }

    type SelectOption =
        { Id: string; Name: string; Color: string; Description: string }

    type Field =
        { Id: string; Name: string; DataType: string; Options: SelectOption list }

    type ItemValue =
        { Id: string; FieldId: string; Kind: string; Value: string }

    type Item =
        { Id: string; ContentId: string; ContentKind: string; Repository: string option
          Number: int option; UpdatedAt: string; Archived: bool; Values: ItemValue list }

    type NativeIssue =
        { Id: string; Repository: string; Number: int; UpdatedAt: string; State: string }

    type View =
        { Id: string; Name: string; Layout: string; Filter: string
          VisibleFieldIds: string list; UnsupportedConfiguration: string }

    type Snapshot =
        { ObservedAt: DateTimeOffset; PrincipalId: string; PrincipalLogin: string
          OrganizationId: string; ProjectId: string; ProjectTitle: string
          ProjectNumber: int; Private: bool; CanUpdate: bool; UpdatedAt: string
          Fields: Field list; Items: Item list; Views: View list; Issues: NativeIssue list }

    type Change =
        | AddInReview of fieldId: string * retained: SelectOption list * color: string * description: string
        | AddMembership of issueId: string
        | CreateView of DesiredView
        | UpdateView of viewId: string * DesiredView

    type Prepared =
        { Scope: Scope; Snapshot: Snapshot; Changes: Change list; OwnerActions: string list
          Digest: string }

    open System.Collections.Generic
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
        let invalidViews =
            scope.Views |> List.exists (fun (view: DesiredView) ->
                not (bounded 100 view.Name)
                || not (Set.contains view.Layout (Set.ofList [ "TABLE_LAYOUT"; "BOARD_LAYOUT"; "ROADMAP_LAYOUT" ]))
                || isNull view.Filter || view.Filter.Length > 2048 || (view.Filter |> Seq.exists Char.IsControl)
                || view.VisibleFieldIds.IsEmpty || view.VisibleFieldIds.Length > 50
                || not (unique view.VisibleFieldIds) || (view.VisibleFieldIds |> List.exists (bounded 256 >> not))
                || (view.Id |> Option.exists (bounded 256 >> not)))
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
        elif scope.Views.Length > 10 || invalidViews || not (unique (scope.Views |> List.map (fun (view: DesiredView) -> view.Name))) then
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
        { Method = "POST"; Path = "graphql"; Query = []; Body = Query(document, variables)
          Budget = GraphQl; IfNoneMatch = None; Subject = subject }
    let private cursor (after: string option) =
        match after with Some value -> [ "after", VString value ] | None -> []
    let private rootNode (data: JsonElement) = data.GetProperty "node"
    let private limits = { GraphQl.DrainLimits.MaxPages = 100; MaxItems = 5000 }
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

    let private drain transport id what document key node = drainCached None transport id what document key node

    let private option (node: JsonElement) : IoResult<SelectOption> =
        decode (fun () ->
            { Id = nonempty "id" node; Name = nonempty "name" node
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
                else Ok { Id = id; Name = name; DataType = dataType; Options = options })

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
            { Id = nonempty "id" node; ContentId = nonempty "id" content; ContentKind = kind
              Repository = repository; Number = number; UpdatedAt = nonempty "updatedAt" node
              Archived = node.GetProperty("isArchived").GetBoolean(); Values = [] })
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
            // System-field contents are not reconstructed or written. Their native field identity
            // is retained; complete selected planning values are retained above.
            let fieldId = nonempty "id" (node.GetProperty "field")
            let values = names |> List.collect (fun name -> [ name; node.GetProperty(name).GetRawText() ]);
            { Id = fieldId; Kind = kind; FieldId = fieldId; Value = JsonSerializer.Serialize values })
    let private projectDocument =
        "query{viewer{id login} organization(login:\"FS-GG\"){id login projectV2(number:3){id number title public viewerCanUpdate updatedAt}}}"
    let private project (transport: IGitHubTransport) (observedAt: DateTimeOffset) : IoResult<Snapshot> =
        GraphQl.read transport (query projectDocument []) (fun data ->
            decode (fun () ->
                let principal = data.GetProperty "viewer"
                let owner = data.GetProperty "organization"
                if nonempty "login" owner <> "FS-GG" then invalidArg "owner" "different native owner"
                let project = owner.GetProperty "projectV2"
                { ObservedAt = observedAt; PrincipalId = nonempty "id" principal; PrincipalLogin = nonempty "login" principal
                  OrganizationId = nonempty "id" owner; ProjectId = nonempty "id" project
                  ProjectTitle = nonempty "title" project; ProjectNumber = project.GetProperty("number").GetInt32()
                  Private = not (project.GetProperty("public").GetBoolean()); CanUpdate = project.GetProperty("viewerCanUpdate").GetBoolean()
                  UpdatedAt = nonempty "updatedAt" project; Fields = []; Items = []; Views = []; Issues = [] }))
    let private nativeIssues (transport: IGitHubTransport) (selected: SelectedIssue list) : IoResult<NativeIssue list> =
        let indexed = selected |> List.mapi (fun index issue -> "issue" + string index, issue)
        let declaration = indexed |> List.map (fun (name, _) -> "$" + name + ":ID!") |> String.concat ","
        let nodes =
            indexed |> List.map (fun (name, _) ->
                name + ":node(id:$" + name + "){__typename ... on Issue{id number updatedAt state repository{nameWithOwner}}}") |> String.concat " "
        let variables = indexed |> List.map (fun (name, issue) -> name, VId issue.Id)
        GraphQl.read transport (query ("query(" + declaration + "){" + nodes + "}") variables) (fun data ->
            indexed |> List.map (fun (name, expected) ->
                match decode (fun () ->
                    let issue = data.GetProperty name
                    if nonempty "__typename" issue <> "Issue" then invalidArg "issue" "selected native node is not an issue"
                    { Id = nonempty "id" issue; Repository = nonempty "nameWithOwner" (issue.GetProperty "repository")
                      Number = issue.GetProperty("number").GetInt32(); UpdatedAt = nonempty "updatedAt" issue; State = nonempty "state" issue }) with
                | Ok actual when actual.Id = expected.Id && actual.Repository = expected.Repository && actual.Number = expected.Number -> Ok actual
                | Ok _ -> invalid "selected native issue identity differs"
                | Error error -> Error error) |> sequence)

    let private viewsDocument =
        "query($id:ID!,$after:String){node(id:$id){... on ProjectV2{id views(first:50,after:$after){pageInfo{hasNextPage endCursor} nodes{id name layout filter configuration{visibleFields(first:50){pageInfo{hasNextPage endCursor} nodes{... on ProjectV2FieldCommon{id name dataType}}}} groupByFields(first:50){pageInfo{hasNextPage endCursor} nodes{... on ProjectV2FieldCommon{id name dataType}}} verticalGroupByFields(first:50){pageInfo{hasNextPage endCursor} nodes{... on ProjectV2FieldCommon{id name dataType}}} sortByFields(first:50){pageInfo{hasNextPage endCursor} nodes{direction field{... on ProjectV2FieldCommon{id name dataType}}}}}}}}}"
    let private viewNode (node: JsonElement) : IoResult<View> =
        decode (fun () ->
            { Id = nonempty "id" node; Name = nonempty "name" node; Layout = nonempty "layout" node
              Filter = (if node.GetProperty("filter").ValueKind = JsonValueKind.Null then "" else propertyString "filter" node)
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
    let private collectViews (transport: IGitHubTransport) (projectId: string) : IoResult<View list> =
        let firstPages = Dictionary<string, JsonElement>()
        let node (element: JsonElement) =
            viewNode element |> Result.bind (fun view ->
                decode (fun () ->
                    firstPages.Add(view.Id, element.Clone())
                    view))
        drain transport projectId "views" viewsDocument (fun (view: View) -> view.Id) node
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
                        { view with VisibleFieldIds = visible
                                    UnsupportedConfiguration = JsonSerializer.Serialize [ [ view.UnsupportedConfiguration ]; grouped; vertical; sorted ] }
                    | _ -> invalidOp "fixed view configuration shape")) |> sequence)

    let collect (transport: IGitHubTransport) (scope: Scope) : IoResult<Snapshot> =
        let observedAt = DateTimeOffset.UtcNow
        match validateScope scope with
        | Error error -> Error error
        | Ok() ->
            project transport observedAt |> Result.bind (fun initial ->
                if initial.OrganizationId <> "O_kgDOEYAWYw" || initial.ProjectId <> "PVT_kwDOEYAWY84Bldpa"
                   || initial.ProjectNumber <> 3 || initial.ProjectTitle <> "Coordination V2" || not initial.Private
                   || initial.PrincipalId <> scope.PrincipalId || initial.PrincipalLogin <> scope.PrincipalLogin then
                    invalid "native principal/private project identity differs before snapshot collection"
                else
                    drain transport initial.ProjectId "fields" fieldsDocument (fun (field: Field) -> field.Id) field
                    |> Result.bind (fun fields ->
                        let firstPages = Dictionary<string, JsonElement>()
                        let node (element: JsonElement) : IoResult<Item> =
                            item element |> Result.bind (fun item ->
                                decode (fun () ->
                                    firstPages.Add(item.Id, element.GetProperty("fieldValues").Clone())
                                    item))
                        drain transport initial.ProjectId "items" itemsDocument (fun (item: Item) -> item.Id) node
                        |> Result.bind (fun items ->
                            items |> List.map (fun (item: Item) ->
                                drainCached (Some firstPages.[item.Id]) transport item.Id "fieldValues" valuesDocument (fun (value: ItemValue) -> value.Id) value
                                |> Result.map (fun values -> { item with Values = values })) |> sequence
                            |> Result.bind (fun items ->
                                collectViews transport initial.ProjectId |> Result.bind (fun views ->
                                    nativeIssues transport scope.SelectedIssues
                                    |> Result.bind (fun issues ->
                                        project transport observedAt |> Result.bind (fun final ->
                                            if final <> initial then invalid "project/principal changed during complete snapshot collection"
                                            else Ok { initial with Fields = fields; Items = items; Views = views; Issues = issues })))))))

    let private desiredTokens (view: DesiredView) =
        [ view.Id |> Option.defaultValue ""; view.Name; view.Layout; view.Filter;
          string view.VisibleFieldIds.Length ] @ view.VisibleFieldIds
    let private optionTokens (value: SelectOption) = [ value.Id; value.Name; value.Color; value.Description ]
    let private scopeTokens (scope: Scope) =
        [ scope.SourceRevision; scope.ArtifactSha256; scope.CliArtifactSha256; scope.PrincipalId; scope.PrincipalLogin;
          scope.StatusFieldId; scope.RoadmapFieldId; scope.TrackFieldId; scope.ObservationFieldId;
          scope.InReviewColor; scope.InReviewDescription; string scope.SelectedIssues.Length ]
        @ (scope.SelectedIssues |> List.collect (fun (issue: SelectedIssue) -> [ issue.Id; issue.Repository; string issue.Number; issue.Slice ]))
        @ [ string scope.Views.Length ] @ (scope.Views |> List.collect desiredTokens)
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
        | AddInReview(id, retained, color, description) ->
            [ "status"; id; color; description; string retained.Length ] @ (retained |> List.collect optionTokens)
        | AddMembership id -> [ "membership"; id ]
        | CreateView view -> "create-view" :: desiredTokens view
        | UpdateView(id, view) -> [ "update-view"; id ] @ desiredTokens view
    let private hash (tokens: string list) =
        SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize tokens)) |> Convert.ToHexString |> _.ToLowerInvariant()

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
                                    if current.Name = desired.Name && current.Layout = desired.Layout
                                       && current.Filter = desired.Filter && current.VisibleFieldIds = desired.VisibleFieldIds then Ok []
                                    elif desired.Id.IsNone then invalid "same-name existing view differs; select its exact native ID before updating"
                                    else Ok [ UpdateView(current.Id, desired) ]
                                | _ -> invalid "view identity/name discovery is ambiguous")
                    sequence viewChanges |> Result.map (fun changes ->
                        let all = statusChange @ membership @ List.concat changes
                        let ownerActions =
                            [ "No Status, Track or Roadmap values are seeded or overwritten; reconcile desired planning values with their owners.";
                              "Grouping, sorting, column order and visual verification require supported authenticated UI actions.";
                              "Readback IDs must be independently finalized into protected enrollment before v4 consumer activation.";
                              "Compare-before-write is not CAS; apply requires a bounded participating single-writer planning window." ]
                            @ (if snapshot.CanUpdate then [] else [ "Native principal lacks project update capability; this read-only preparation grants no effect permission." ])
                        { Scope = scope; Snapshot = snapshot; Changes = all; OwnerActions = ownerActions
                          Digest = hash (scopeTokens scope @ snapshotTokens snapshot @ [ string all.Length ] @ (all |> List.collect changeTokens)) })

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
                | CreateView view ->
                    (selection "CreateProjectV2ViewInput" "createProjectV2View" "projectV2View{id}",
                     objectNode [ id "projectId" prepared.Snapshot.ProjectId; id "name" view.Name; id "layout" view.Layout; "configuration", visible view ])
                | UpdateView(viewId, view) ->
                    (selection "UpdateProjectV2ViewInput" "updateProjectV2View" "projectV2View{id}",
                     objectNode [ id "viewId" viewId; id "name" view.Name; id "layout" view.Layout; id "filter" view.Filter; "configuration", visible view ])
            input.Add("clientMutationId", jsonString (hash (prepared.Digest :: changeTokens change)))
            let variables = objectNode [ "input", input ]
            let body = objectNode [ id "query" document; "variables", variables ]
            Ok { Method = "POST"; Path = "graphql"; Query = []; Body = Json(body.ToJsonString())
                 Budget = GraphQl; IfNoneMatch = None; Subject = subject }
    let private closed (names: string list) (node: JsonElement) =
        if node.ValueKind <> JsonValueKind.Object then invalidArg "json" "expected closed object"
        let actual = node.EnumerateObject() |> Seq.map (fun entry -> entry.Name) |> Seq.toList
        if actual.Length <> names.Length || Set.ofList actual <> Set.ofList names then
            invalidArg "json" "unknown, missing or duplicate property"
    let private array (limit: int) (node: JsonElement) =
        if node.ValueKind <> JsonValueKind.Array || node.GetArrayLength() > limit then invalidArg "json" "bounded array required"
        node.EnumerateArray() |> Seq.toList
    let private parseScopeNode (node: JsonElement) : Scope =
        closed [ "sourceRevision"; "artifactSha256"; "cliArtifactSha256"; "principalId"; "principalLogin";
                 "selectedIssues"; "statusFieldId"; "roadmapFieldId"; "trackFieldId"; "observationFieldId";
                 "inReviewColor"; "inReviewDescription"; "views" ] node
        let issues =
            array 100 (node.GetProperty "selectedIssues") |> List.map (fun issue ->
                closed [ "id"; "repository"; "number"; "slice" ] issue
                { Id = nonempty "id" issue; Repository = nonempty "repository" issue
                  Number = issue.GetProperty("number").GetInt32(); Slice = nonempty "slice" issue })
        let views =
            array 10 (node.GetProperty "views") |> List.map (fun view ->
                closed [ "id"; "name"; "layout"; "filter"; "visibleFieldIds" ] view
                let id = view.GetProperty "id"
                { Id = if id.ValueKind = JsonValueKind.Null then None else Some(nonempty "id" view)
                  Name = nonempty "name" view; Layout = nonempty "layout" view; Filter = propertyString "filter" view
                  VisibleFieldIds = array 50 (view.GetProperty "visibleFieldIds") |> List.map (fun field -> field.GetString()) })
        { SourceRevision = nonempty "sourceRevision" node; ArtifactSha256 = nonempty "artifactSha256" node
          CliArtifactSha256 = nonempty "cliArtifactSha256" node
          PrincipalId = nonempty "principalId" node; PrincipalLogin = nonempty "principalLogin" node
          SelectedIssues = issues; StatusFieldId = nonempty "statusFieldId" node
          RoadmapFieldId = nonempty "roadmapFieldId" node; TrackFieldId = nonempty "trackFieldId" node
          ObservationFieldId = nonempty "observationFieldId" node; InReviewColor = nonempty "inReviewColor" node
          InReviewDescription = propertyString "inReviewDescription" node; Views = views }
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
        objectNode [ "sourceRevision", jsonString scope.SourceRevision; "artifactSha256", jsonString scope.ArtifactSha256;
                     "cliArtifactSha256", jsonString scope.CliArtifactSha256;
                     "principalId", jsonString scope.PrincipalId; "principalLogin", jsonString scope.PrincipalLogin;
                     "selectedIssues", arrayNode issues; "statusFieldId", jsonString scope.StatusFieldId;
                     "roadmapFieldId", jsonString scope.RoadmapFieldId; "trackFieldId", jsonString scope.TrackFieldId;
                     "observationFieldId", jsonString scope.ObservationFieldId; "inReviewColor", jsonString scope.InReviewColor;
                     "inReviewDescription", jsonString scope.InReviewDescription; "views", arrayNode (scope.Views |> List.map desiredNode) ] :> JsonNode
    let encodePrepared (prepared: Prepared) : string =
        // Exact stable token arrays retain every determining native value without union/option
        // serializer defaults. This is an operation report, never a normal v4 binding.
        let root =
            objectNode [ "schema", jsonString "fs.gg.board-v2.migration-prepare/1";
                         "observedAt", jsonString (prepared.Snapshot.ObservedAt.ToString "O");
                         "scope", scopeNode prepared.Scope; "snapshot", stringsNode (snapshotTokens prepared.Snapshot);
                         "changes", arrayNode (prepared.Changes |> List.map (changeTokens >> stringsNode));
                         "ownerActions", stringsNode prepared.OwnerActions; "digest", jsonString prepared.Digest ]
        let json = root.ToJsonString(JsonSerializerOptions(WriteIndented = true))
        if Encoding.UTF8.GetByteCount json > 4 * 1024 * 1024 then invalidOp "bounded private migration report exceeds 4 MiB"
        json
    let decodePrepared (json: string) : IoResult<Scope * string * DateTimeOffset> =
        if Encoding.UTF8.GetByteCount json > 4 * 1024 * 1024 then invalid "prepared input exceeds 4 MiB"
        else
            decode (fun () ->
                use document = JsonDocument.Parse(json, JsonDocumentOptions(MaxDepth = 16))
                let root = document.RootElement
                closed [ "schema"; "observedAt"; "scope"; "snapshot"; "changes"; "ownerActions"; "digest" ] root
                if propertyString "schema" root <> "fs.gg.board-v2.migration-prepare/1" then invalidArg "schema" "different prepared schema"
                let scope = parseScopeNode (root.GetProperty "scope")
                let digest = nonempty "digest" root
                if not (hex 64 digest) then invalidArg "digest" "malformed prepared digest"
                // Report payload remains retained verbatim; no JSON assertion supplies native facts.
                // The complete fresh native planner must reproduce this separately selected digest.
                scope, digest, root.GetProperty("observedAt").GetDateTimeOffset())
            |> Result.bind (fun (scope, digest, observedAt) ->
                validateScope scope |> Result.map (fun () -> scope, digest, observedAt))
    let resolveReadback (scope: Scope) (before: Snapshot) (after: Snapshot) (change: Change) : IoResult<Scope> =
        match change with
        | CreateView desired ->
            let prior = before.Views |> List.filter (fun (view: View) -> view.Name = desired.Name)
            let candidates = after.Views |> List.filter (fun (view: View) -> view.Name = desired.Name)
            match prior, candidates with
            | [], [ created ] when created.Layout = desired.Layout && created.Filter = ""
                                   && created.VisibleFieldIds = desired.VisibleFieldIds
                                   && not (before.Views |> List.exists (fun (view: View) -> view.Id = created.Id)) ->
                Ok { scope with Views = scope.Views |> List.map (fun (view: DesiredView) ->
                        if view = desired then { view with Id = Some created.Id } else view) }
            | _ -> invalid "created view identity/configuration is ambiguous; original invocation is readback-only"
        | _ -> Ok scope

    let validateTransition (scope: Scope) (before: Snapshot) (after: Snapshot) (change: Change) : IoResult<unit> =
        let normalize (snapshot: Snapshot) = { snapshot with ObservedAt = before.ObservedAt; UpdatedAt = before.UpdatedAt }
        let unchanged (snapshot: Snapshot) = snapshotTokens (normalize snapshot) = snapshotTokens (normalize before)
        let sameView (desired: DesiredView) (actual: View) =
            desired.Name = actual.Name && desired.Layout = actual.Layout && desired.Filter = actual.Filter
            && desired.VisibleFieldIds = actual.VisibleFieldIds
        match resolveReadback scope before after change |> Result.bind (fun resolved -> prepare resolved after) with
        | Error error -> Error error
        | Ok _ ->
            let reconciled =
                match change with
                | AddInReview(fieldId, retained, color, description) ->
                    match after.Fields |> List.filter (fun (field: Field) -> field.Id = fieldId) with
                    | [ field ] ->
                        match field.Options |> List.filter (fun (option: SelectOption) -> option.Name = "In review") with
                        | [ added ] when added.Color = color && added.Description = description
                                         && (retained |> List.forall (fun (option: SelectOption) -> option.Id <> added.Id))
                                         && field.Options = retained @ [ added ] ->
                            let fields = after.Fields |> List.map (fun (value: Field) -> if value.Id = fieldId then { value with Options = retained } else value)
                            unchanged { after with Fields = fields }
                        | _ -> false
                    | _ -> false
                | AddMembership issueId ->
                    let added = after.Items |> List.filter (fun (item: Item) -> item.ContentId = issueId)
                    match added, scope.SelectedIssues |> List.tryFind (fun (issue: SelectedIssue) -> issue.Id = issueId) with
                    | [ item ], Some issue when item.ContentKind = "Issue" && item.Repository = Some issue.Repository
                                               && item.Number = Some issue.Number && not item.Archived
                                               && (before.Items |> List.forall (fun (existing: Item) -> existing.Id <> item.Id && existing.ContentId <> issueId)) ->
                        unchanged { after with Items = after.Items |> List.filter (fun (existing: Item) -> existing.Id <> item.Id) }
                    | _ -> false
                | CreateView desired ->
                    match after.Views |> List.filter (fun (view: View) -> view.Name = desired.Name) with
                    | [ added ] when sameView { desired with Filter = "" } added && (before.Views |> List.forall (fun (view: View) -> view.Id <> added.Id)) ->
                        unchanged { after with Views = after.Views |> List.filter (fun (view: View) -> view.Id <> added.Id) }
                    | _ -> false
                | UpdateView(viewId, desired) ->
                    match before.Views |> List.filter (fun (view: View) -> view.Id = viewId), after.Views |> List.filter (fun (view: View) -> view.Id = viewId) with
                    | [ original ], [ current ] when sameView desired current ->
                        unchanged { after with Views = after.Views |> List.map (fun (view: View) -> if view.Id = viewId then original else view) }
                    | _ -> false
            if reconciled then Ok()
            else invalid "readback differs beyond the one exact effect; preserve original intent and stop writes for reconciliation"
    let snapshotDigest (snapshot: Snapshot) = hash (snapshotTokens snapshot)
    let encodeReadback (scope: Scope) (snapshot: Snapshot) =
        let root =
            objectNode [ "schema", jsonString "fs.gg.board-v2.migration-readback/1";
                         "observedAt", jsonString (snapshot.ObservedAt.ToString "O");
                         "scope", scopeNode scope; "snapshot", stringsNode (snapshotTokens snapshot);
                         "snapshotDigest", jsonString (snapshotDigest snapshot);
                         "admission", jsonString "readback-only; original outcome and uncertainty retained" ]
        let json = root.ToJsonString(JsonSerializerOptions(WriteIndented = true))
        if Encoding.UTF8.GetByteCount json > 4 * 1024 * 1024 then invalidOp "bounded private migration report exceeds 4 MiB"
        json
