namespace FS.GG.Coord.Tests

module MigrationFixture =
    open System
    open System.Text.Json
    open System.Text.Json.Nodes
    open FS.GG.Coord.GitHub
    open FS.GG.Coord.GitHub.Errors
    open FS.GG.Coord.GitHub.Transport
    open FS.GG.Coord.GitHub.V2Migration

    let private value (item: 'a) : JsonNode = JsonSerializer.SerializeToNode item
    let private objectNode (pairs: (string * JsonNode) list) : JsonNode =
        let node = JsonObject()
        for name, item in pairs do node.Add(name, item)
        node
    let private arrayNode (items: JsonNode list) : JsonNode =
        let node = JsonArray()
        for item in items do node.Add item
        node
    let private page (nodes: JsonNode list) =
        objectNode [ "totalCount", value nodes.Length; "nodes", arrayNode nodes;
                     "pageInfo", objectNode [ "hasNextPage", value false; "endCursor", null ] ]
    let private response (data: JsonNode) : IoResult<Transport.Response> =
        Ok(
            { Status = 200; Body = (objectNode [ "data", data ]).ToJsonString();
              Headers = Map.empty; ETag = None; NextLink = None })
    let private options: SelectOption list =
        [ "Backlog"; "Ready"; "In progress"; "Blocked"; "Done" ] |> List.mapi (fun index name ->
            { Id = "synthetic-option-" + string index; Name = name; Color = "GRAY"; Description = "synthetic retained option" })
    let scope : Scope =
        { SourceRevision = String.replicate 40 "a"; ArtifactSha256 = String.replicate 64 "b"; CliArtifactSha256 = String.replicate 64 "c";
          PrincipalId = "synthetic-principal"; PrincipalLogin = "synthetic-operator";
          SelectedIssues = [ { Id = "synthetic-issue"; Repository = "FS-GG/.github"; Number = 42; Slice = "synthetic-parent" } ];
          StatusFieldId = "synthetic-status"; RoadmapFieldId = "synthetic-roadmap"; TrackFieldId = "synthetic-track";
          ObservationFieldId = "synthetic-observation"; InReviewColor = "BLUE"; InReviewDescription = "synthetic review";
          Views = []; InitialPlanning = []; WorkLevel = None }
    let snapshot : Snapshot =
        { ObservedAt = DateTimeOffset.UtcNow; PrincipalId = scope.PrincipalId; PrincipalLogin = scope.PrincipalLogin;
          OrganizationId = "O_kgDOEYAWYw"; ProjectId = "PVT_kwDOEYAWY84Bldpa"; ProjectTitle = "Coordination V2";
          ProjectNumber = 3; Private = true; CanUpdate = true; UpdatedAt = "2026-10-17T12:00:00Z";
          Fields =
            [ { Id = scope.StatusFieldId; Name = "Status"; DataType = "SINGLE_SELECT"; Options = options };
              { Id = scope.RoadmapFieldId; Name = "Roadmap"; DataType = "TEXT"; Options = [] };
              { Id = scope.TrackFieldId; Name = "Track"; DataType = "SINGLE_SELECT"; Options = [] };
              { Id = scope.ObservationFieldId; Name = "Observation"; DataType = "SINGLE_SELECT"; Options = [] } ];
          Items = []; Views = [];
          Issues = [ { Id = "synthetic-issue"; Repository = "FS-GG/.github"; Number = 42; UpdatedAt = "2026-10-17T12:00:00Z"; State = "OPEN" } ] }

    // Sanitized structural replay of retained original002: 16 fields, five items
    // (four seven-value/one two-value), five five-column views, 20 selected issues.
    // All identifiers/content below are synthetic; the stable token shape is436.
    let original002 () : Scope * Snapshot =
        let repositories: string list =
            [ "FS-GG/.github"; "FS-GG/FS.GG.Coordination"; "FS-GG/FS.GG.Game";
              "FS-GG/FS.GG.SDD"; "FS-GG/FS.GG.Templates"; "FS-GG/FS.GG.Unity.Client" ]
        let selected: SelectedIssue list =
            [ 0 .. 19 ] |> List.map (fun index ->
                { Id = "synthetic-selected-" + string index; Repository = repositories.[index % repositories.Length];
                  Number = index + 1; Slice = "synthetic-slice-" + string index })
        let systemFields: Field list =
            [ ("Repository", "REPOSITORY"); ("Reviewers", "REVIEWERS"); ("Title", "TITLE");
              ("Assignees", "ASSIGNEES"); ("Labels", "LABELS"); ("Linked pull requests", "LINKED_PULL_REQUESTS");
              ("Milestone", "MILESTONE"); ("Parent issue", "PARENT_ISSUE"); ("Sub-issues progress", "SUB_ISSUES_PROGRESS");
              ("Created", "CREATED"); ("Updated", "UPDATED"); ("Closed", "CLOSED") ]
            |> List.mapi (fun index (name, kind) ->
                { Id = "synthetic-system-" + string index; Name = name; DataType = kind; Options = [] })
        let fields: Field list =
            snapshot.Fields |> List.map (fun (field: Field) ->
                let options: SelectOption list =
                    if field.Id = scope.StatusFieldId then
                        field.Options @ [ { Id = "synthetic-review-option"; Name = "In review"; Color = scope.InReviewColor; Description = scope.InReviewDescription } ]
                    elif field.Id = scope.TrackFieldId || field.Id = scope.ObservationFieldId then
                        [ 0 .. (if field.Id = scope.TrackFieldId then 1 else 2) ] |> List.map (fun index ->
                            { Id = field.Id + "-option-" + string index; Name = "synthetic-option-" + string index;
                              Color = "GRAY"; Description = "synthetic retained descriptor" })
                    else field.Options
                { field with Options = options })
        let scalar (index: int) (fieldId: string) (kind: string) (payload: string list) : ItemValue =
            { Id = fieldId; FieldId = fieldId; Kind = kind;
              Value = JsonSerializer.Serialize([ "id"; JsonSerializer.Serialize("synthetic-value-" + string index + "-" + fieldId);
                                                "updatedAt"; "\"2026-10-04T13:04:40Z\"" ] @ payload) }
        let items: Item list =
            [ 0 .. 4 ] |> List.map (fun index ->
                let issue: SelectedIssue =
                    if index < 3 then selected.[17 + index]
                    else
                        { Id = "synthetic-unselected-" + string index; Repository = "FS-GG/.github";
                          Number = 100 + index; Slice = "synthetic-unselected" }
                let system: ItemValue list =
                    [ { Id = systemFields.[0].Id; FieldId = systemFields.[0].Id; Kind = "ProjectV2ItemFieldRepositoryValue"; Value = "[]" };
                      scalar index systemFields.[2].Id "ProjectV2ItemFieldTextValue" [ "text"; "\"synthetic title\"" ] ]
                let values: ItemValue list =
                    if index = 4 then system
                    else
                        system @
                        [ { Id = systemFields.[4].Id; FieldId = systemFields.[4].Id; Kind = "ProjectV2ItemFieldLabelValue"; Value = "[]" };
                          scalar index scope.RoadmapFieldId "ProjectV2ItemFieldTextValue" [ "text"; "\"synthetic retained roadmap\"" ] ]
                        @ ([ scope.StatusFieldId; scope.TrackFieldId; scope.ObservationFieldId ] |> List.map (fun fieldId ->
                            let option = fields |> List.find (fun (field: Field) -> field.Id = fieldId) |> _.Options.Head
                            scalar index fieldId "ProjectV2ItemFieldSingleSelectValue"
                                [ "optionId"; JsonSerializer.Serialize(option.Id); "name"; JsonSerializer.Serialize(option.Name) ]))
                { Id = "synthetic-retained-item-" + string index; ContentId = issue.Id; ContentKind = "Issue";
                  Repository = Some issue.Repository; Number = Some issue.Number; UpdatedAt = "2026-10-04T13:04:40Z";
                  Archived = false; Values = values })
        let views: View list =
            [ 0 .. 4 ] |> List.map (fun index ->
                { Id = "synthetic-retained-view-" + string index; Name = "synthetic view " + string index;
                  Layout = "TABLE_LAYOUT"; Filter = "";
                  VisibleFieldIds = [ systemFields.[2].Id; systemFields.[0].Id; scope.StatusFieldId; scope.TrackFieldId; scope.RoadmapFieldId ];
                  UnsupportedConfiguration = "[[\"\"],[],[],[]]" })
        let issues: NativeIssue list =
            selected |> List.map (fun (issue: SelectedIssue) ->
                { Id = issue.Id; Repository = issue.Repository; Number = issue.Number;
                  UpdatedAt = "2026-10-04T13:04:40Z"; State = "OPEN" })
        ({ scope with SelectedIssues = selected },
         { snapshot with UpdatedAt = "2026-10-04T13:04:40Z"; Fields = systemFields @ fields;
                         Items = items; Views = views; Issues = issues })

    type Native(initial: Snapshot) =
        let mutable snapshot = initial
        let requests = ResizeArray<Transport.Request>()
        let mutations = ResizeArray<Transport.Request>()
        let mutable visibilityDelay = 0
        let mutable pendingVisibility: (Snapshot * int) option = None
        let mutable visibilitySnapshot: Snapshot option = None
        let mutable failAfterEffect = false
        let mutable beforeRead: Transport.Request -> unit = ignore
        let mutable beforeDispatch: Transport.Request -> unit = ignore
        let mutable transformDispatch: Transport.Request -> Transport.Response -> IoResult<Transport.Response> = fun _ response -> Ok response
        let mutable transform: Transport.Request -> Transport.Response -> IoResult<Transport.Response> = fun _ response -> Ok response
        let fieldNode (field: Field) : JsonNode =
            let options: JsonNode list =
                field.Options
                |> List.map (fun (option: SelectOption) ->
                    objectNode [ "id", value option.Id; "name", value option.Name; "color", value option.Color; "description", value option.Description ])
            objectNode [ "id", value field.Id; "name", value field.Name; "dataType", value field.DataType;
                         "__typename", value (if field.DataType = "SINGLE_SELECT" then "ProjectV2SingleSelectField" else "ProjectV2Field");
                         "options", arrayNode options ]
        let valueNode (item: ItemValue) : JsonNode =
            let node = JsonObject()
            node.Add("__typename", value item.Kind)
            node.Add("field", objectNode [ "id", value item.FieldId ])
            let tokens = JsonSerializer.Deserialize<string array> item.Value
            for pair in tokens |> Array.chunkBySize 2 do node.Add(pair.[0], JsonNode.Parse pair.[1])
            node :> JsonNode
        let viewNode (view: View) : JsonNode =
            let visible = view.VisibleFieldIds |> List.map (fun id -> objectNode [ "id", value id ])
            objectNode [ "id", value view.Id; "name", value view.Name; "layout", value view.Layout; "filter", value view.Filter;
                         "configuration", objectNode [ "visibleFields", page visible ]; "groupByFields", page [];
                         "verticalGroupByFields", page []; "sortByFields", page [] ]
        let itemNode (item: Item) : JsonNode =
            let content =
                objectNode [ "id", value item.ContentId; "__typename", value item.ContentKind;
                             "number", value (item.Number |> Option.defaultValue 0);
                             "repository", objectNode [ "nameWithOwner", value (item.Repository |> Option.defaultValue "") ] ]
            objectNode [ "id", value item.Id; "updatedAt", value item.UpdatedAt; "isArchived", value item.Archived; "content", content;
                         "fieldValues", page (item.Values |> List.map valueNode) ]
        let read (request: Transport.Request) : IoResult<Transport.Response> =
            requests.Add request
            beforeRead request
            match request.Body with
            | Query(document, variables) ->
                if document.Contains("viewer{id login}") && document.Contains("fields(first:") then
                    match pendingVisibility with
                    | Some(before, remaining) when remaining > 0 ->
                        visibilitySnapshot <- Some({ before with UpdatedAt = snapshot.UpdatedAt })
                        pendingVisibility <- Some(before, remaining - 1)
                    | _ ->
                        visibilitySnapshot <- None
                        pendingVisibility <- None
                let snapshot = visibilitySnapshot |> Option.defaultValue snapshot
                let id = variables |> List.tryPick (function "id", VId id -> Some id | _ -> None) |> Option.defaultValue snapshot.ProjectId
                let parent connection values = response (objectNode [ "node", objectNode [ "id", value id; connection, page values ] ])
                if document.Contains "viewer{id login}" then
                    let extra: (string * JsonNode) list =
                        if document.Contains "fields(first:" then
                            [ ("fields", page (snapshot.Fields |> List.map fieldNode));
                              ("items", page (snapshot.Items |> List.map itemNode));
                              ("views", page (snapshot.Views |> List.map viewNode)) ]
                        else []
                    let project: JsonNode =
                        objectNode ([ "id", value snapshot.ProjectId; "number", value snapshot.ProjectNumber;
                                      "title", value snapshot.ProjectTitle; "public", value (not snapshot.Private);
                                      "viewerCanUpdate", value snapshot.CanUpdate; "updatedAt", value snapshot.UpdatedAt ] @ extra)
                    let issues: (string * JsonNode) list =
                        variables |> List.choose (fun (name, variable) ->
                            if not (name.StartsWith("issue", StringComparison.Ordinal)) then None
                            else
                                let id = match variable with Transport.VId id -> id | _ -> failwith "native issue ID expected"
                                let issue = snapshot.Issues |> List.find (fun (issue: NativeIssue) -> issue.Id = id)
                                Some(name, objectNode [ "__typename", value "Issue"; "id", value issue.Id; "number", value issue.Number;
                                                        "updatedAt", value issue.UpdatedAt; "state", value issue.State;
                                                        "repository", objectNode [ "nameWithOwner", value issue.Repository ] ]))
                    response (objectNode ([ "viewer", objectNode [ "id", value snapshot.PrincipalId; "login", value snapshot.PrincipalLogin ];
                                            "organization", objectNode [ "id", value snapshot.OrganizationId; "login", value "FS-GG";
                                                                        "projectV2", project ] ] @ issues))
                elif document.Contains "fields(first:" then parent "fields" (snapshot.Fields |> List.map fieldNode)
                elif document.Contains "items(first:" then parent "items" (snapshot.Items |> List.map itemNode)
                elif document.Contains "isArchived content{" && not (document.Contains "items(first:") then
                    // An item-scoped read (#5123): a pending visibility serves the pre-effect item.
                    let source =
                        match pendingVisibility with
                        | Some(before, remaining) when remaining > 0 ->
                            pendingVisibility <- Some(before, remaining - 1)
                            before
                        | Some _ ->
                            pendingVisibility <- None
                            snapshot
                        | None -> snapshot
                    match source.Items |> List.tryFind (fun (item: Item) -> item.Id = id) with
                    | Some item -> response (objectNode [ "node", itemNode item ])
                    | None -> response (objectNode [ "node", null ])
                elif document.Contains "fieldValues(first:" then
                    let item = snapshot.Items |> List.find (fun (item: Item) -> item.Id = id)
                    parent "fieldValues" (item.Values |> List.map valueNode)
                elif document.Contains "views(first:" then parent "views" (snapshot.Views |> List.map viewNode)
                else
                    let view = snapshot.Views |> List.find (fun (view: View) -> view.Id = id)
                    let visible = view.VisibleFieldIds |> List.map (fun id -> objectNode [ "id", value id ])
                    if document.Contains "visibleFields(first:" then
                        response (objectNode [ "node", objectNode [ "id", value id; "configuration", objectNode [ "visibleFields", page visible ] ] ])
                    else
                        let name = [ "groupByFields"; "verticalGroupByFields"; "sortByFields" ] |> List.find (fun name -> document.Contains(name + "(first:"))
                        parent name []
            | _ -> Error(Malformed("synthetic migration", "unexpected read"))
        let dispatch (request: Transport.Request) : IoResult<Transport.Response> =
            mutations.Add request
            let before = snapshot
            beforeDispatch request
            match request.Body with
            | Json json ->
                use document = JsonDocument.Parse json
                let query = document.RootElement.GetProperty("query").GetString()
                let input = document.RootElement.GetProperty("variables").GetProperty "input"
                let text (name: string) = input.GetProperty(name).GetString()
                let name =
                    if query.Contains "createProjectV2Field(" then
                        let options: SelectOption list =
                            input.GetProperty("singleSelectOptions").EnumerateArray() |> Seq.mapi (fun index option ->
                                { Id = "synthetic-level-option-" + string index; Name = option.GetProperty("name").GetString()
                                  Color = option.GetProperty("color").GetString(); Description = option.GetProperty("description").GetString() }) |> Seq.toList
                        let field: Field = { Id = "synthetic-work-level"; Name = text "name"; DataType = text "dataType"; Options = options }
                        snapshot <- { snapshot with Fields = snapshot.Fields @ [ field ] }
                        "createProjectV2Field"
                    elif query.Contains "updateProjectV2Field(" then
                        let options: SelectOption list =
                            input.GetProperty("singleSelectOptions").EnumerateArray() |> Seq.map (fun option ->
                                let mutable id = Unchecked.defaultof<JsonElement>
                                { Id = (if option.TryGetProperty("id", &id) then id.GetString() else "synthetic-new-option");
                                  Name = option.GetProperty("name").GetString(); Color = option.GetProperty("color").GetString(); Description = option.GetProperty("description").GetString() }) |> Seq.toList
                        let fields: Field list =
                            snapshot.Fields
                            |> List.map (fun (field: Field) ->
                                if field.Id = text "fieldId" then { field with Options = options } else field)
                        snapshot <- { snapshot with Fields = fields }
                        "updateProjectV2Field"
                    elif query.Contains "addProjectV2ItemById(" then
                        let issue = snapshot.Issues |> List.find (fun (issue: NativeIssue) -> issue.Id = text "contentId")
                        let item: Item =
                            { Id = "synthetic-item-" + issue.Id; ContentId = issue.Id; ContentKind = "Issue"; Repository = Some issue.Repository;
                              Number = Some issue.Number; UpdatedAt = issue.UpdatedAt; Archived = false; Values = [] }
                        snapshot <- { snapshot with Items = snapshot.Items @ [ item ] }
                        "addProjectV2ItemById"
                    elif query.Contains "updateProjectV2ItemFieldValue(" then
                        let itemId, fieldId = text "itemId", text "fieldId"
                        let selected = input.GetProperty "value"
                        let mutable textValue = Unchecked.defaultof<JsonElement>
                        let kind, tokens =
                            if selected.TryGetProperty("text", &textValue) then
                                "ProjectV2ItemFieldTextValue", [ "text"; textValue.GetRawText() ]
                            else
                                let optionId = selected.GetProperty("singleSelectOptionId").GetString()
                                let field = snapshot.Fields |> List.find (fun (field: Field) -> field.Id = fieldId)
                                let option = field.Options |> List.find (fun (option: SelectOption) -> option.Id = optionId)
                                "ProjectV2ItemFieldSingleSelectValue", [ "optionId"; JsonSerializer.Serialize(option.Id); "name"; JsonSerializer.Serialize(option.Name) ]
                        let initialized: ItemValue =
                            { Id = fieldId; FieldId = fieldId; Kind = kind;
                              Value = JsonSerializer.Serialize([ "id"; JsonSerializer.Serialize("synthetic-value-" + itemId + "-" + fieldId);
                                                               "updatedAt"; JsonSerializer.Serialize("2026-10-17T13:00:00Z") ] @ tokens) }
                        let items: Item list =
                            snapshot.Items |> List.map (fun (item: Item) ->
                                if item.Id = itemId then
                                    { item with UpdatedAt = "2026-10-17T13:00:00Z";
                                                Values = (item.Values |> List.filter (fun (value: ItemValue) -> value.FieldId <> fieldId)) @ [ initialized ] }
                                else item)
                        snapshot <- { snapshot with Items = items }
                        "updateProjectV2ItemFieldValue"
                    else
                        let visible = input.GetProperty("configuration").GetProperty("visibleFieldIds").EnumerateArray() |> Seq.map _.GetString() |> Seq.toList
                        let create = query.Contains "createProjectV2View("
                        let view: View =
                            { Id = (if create then "synthetic-new-view-" + string snapshot.Views.Length else text "viewId"); Name = text "name"; Layout = text "layout";
                              Filter = (if create then "" else text "filter"); VisibleFieldIds = visible; UnsupportedConfiguration = "[[],[],[]]" }
                        let views: View list =
                            if create then snapshot.Views @ [ view ]
                            else
                                snapshot.Views
                                |> List.map (fun (existing: View) ->
                                    if existing.Id = view.Id then view else existing)
                        snapshot <- { snapshot with Views = views }
                        if create then "createProjectV2View" else "updateProjectV2View"
                if visibilityDelay > 0 then
                    snapshot <- { snapshot with UpdatedAt = "2026-10-18T00:32:24Z" }
                    pendingVisibility <- Some(before, visibilityDelay)
                if failAfterEffect then Error(Transport "synthetic lost response")
                else
                    let payloadName, nativeId =
                        match name with
                        | "createProjectV2Field" -> "projectV2Field", "synthetic-work-level"
                        | "updateProjectV2Field" -> "projectV2Field", text "fieldId"
                        | "addProjectV2ItemById" -> "item", "synthetic-item-" + text "contentId"
                        | "updateProjectV2ItemFieldValue" -> "projectV2Item", text "itemId"
                        | "createProjectV2View" -> "projectV2View", (snapshot.Views |> List.last |> _.Id)
                        | _ -> "projectV2View", text "viewId"
                    response (objectNode [ name, objectNode [ "clientMutationId", value (text "clientMutationId"); payloadName, objectNode [ "id", value nativeId ] ] ])
            | _ -> Error(Malformed("synthetic migration", "unexpected effect"))
        let reads =
            { new IGitHubTransport with
                member _.Send(request: Transport.Request) = read request |> Result.bind (transform request)
                member _.SendMutation(_: MutationIntent) = Error(Malformed("synthetic", "raw effects refused"))
                member _.RetryMutation(_: string) = Error(Malformed("synthetic", "replay refused")) }
        member _.VisibilityDelay with get() = visibilityDelay and set value = visibilityDelay <- value
        member _.Snapshot with get() = snapshot and set value = snapshot <- value
        member _.Requests = requests |> Seq.toList
        member _.Mutations = mutations |> Seq.toList
        member _.FailAfterEffect with get() = failAfterEffect and set value = failAfterEffect <- value
        member _.BeforeRead with set value = beforeRead <- value
        member _.BeforeDispatch with set value = beforeDispatch <- value
        member _.Transform with set value = transform <- value
        member _.TransformDispatch with set value = transformDispatch <- value
        member _.Connection() = V2MigrationTransport.compose reads (fun request -> dispatch request |> Result.bind (transformDispatch request))
