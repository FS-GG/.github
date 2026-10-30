namespace FS.GG.Coord.GitHub

module V2NativeResolve =

    open System
    open System.Text.Json
    open System.Text.Json.Nodes
    open System.Text.RegularExpressions

    type GetResult =
        | Body of string
        | NotFound
        | Failed

    type Get = string -> bool -> GetResult

    type ItemContent =
        | IssueContent of reference: string * nodeId: string
        | PullRequestContent of reference: string * nodeId: string
        | OtherContent of kind: string

    type ProjectItem =
        { ItemId: string
          Content: ItemContent
          HasStatus: bool
          HasTrack: bool
          HasWorkLevel: bool
          RoadmapRaw: string option
          UpdatedAt: DateTimeOffset }

    type Resolved =
        { Facts: V2Enrollment.NativeFacts
          Items: ProjectItem list
          // Repositories in which a plan document read as 404 and the repository itself could not be read:
          // the 404 may be invisibility, not absence.
          HiddenRepositories: string list }

    type PullRequest =
        { Number: int
          State: string
          Title: string
          Body: string
          BaseRef: string
          BaseSha: string
          HeadRef: string
          HeadSha: string }

    // ---- the closed GET grammar: a per-endpoint allowlist of exactly the endpoints this module uses ----

    let private segmentPattern = Regex(@"\A[A-Za-z0-9_.-]{1,200}\z", RegexOptions.CultureInvariant)
    let private numberPattern = Regex(@"\A[1-9][0-9]{0,9}\z", RegexOptions.CultureInvariant)
    let private itemsQuery = Regex(@"\Aper_page=100&fields=[0-9]{1,12}(,[0-9]{1,12}){0,15}\z", RegexOptions.CultureInvariant)

    // A segment is a name, never a traversal: "." and ".." as a whole segment are refused, while "a..b.md" is a name.
    let private isName (segment: string) = segmentPattern.IsMatch segment && segment <> "." && segment <> ".."

    // Whether `path` is exactly one of the allowlisted read endpoints, and `paginated` is what that endpoint needs.
    let private allowedEndpoint (path: string) (paginated: bool) : bool =
        if isNull path || path.Length > 1000 || not (path.StartsWith "/") then false
        else
            let pathPart, query =
                match path.IndexOf '?' with
                | -1 -> path, None
                | at -> path.Substring(0, at), Some(path.Substring(at + 1))
            let segments = pathPart.Substring(1).Split('/') |> Array.toList
            let noQuery = query.IsNone
            match segments with
            // Project metadata, fields and items.
            | [ "orgs"; owner; "projectsV2"; number ] -> isName owner && numberPattern.IsMatch number && noQuery && not paginated
            | [ "orgs"; owner; "projectsV2"; number; "fields" ] -> isName owner && numberPattern.IsMatch number && query = Some "per_page=100" && paginated
            | [ "orgs"; owner; "projectsV2"; number; "items" ] -> isName owner && numberPattern.IsMatch number && (query |> Option.exists itemsQuery.IsMatch) && paginated
            // Repository visibility, one issue, one pull request, the open pull requests and the main head.
            | [ "repos"; owner; repository ] -> isName owner && isName repository && noQuery && not paginated
            | [ "repos"; owner; repository; "issues"; number ] -> isName owner && isName repository && numberPattern.IsMatch number && noQuery && not paginated
            | [ "repos"; owner; repository; "pulls"; number ] -> isName owner && isName repository && numberPattern.IsMatch number && noQuery && not paginated
            | [ "repos"; owner; repository; "pulls" ] -> isName owner && isName repository && query = Some "state=open&per_page=100" && paginated
            | [ "repos"; owner; repository; "git"; "ref"; "heads"; "main" ] -> isName owner && isName repository && noQuery && not paginated
            // A document on main: one to twelve path segments.
            | "repos" :: owner :: repository :: "contents" :: (_ :: _ as document) ->
                isName owner && isName repository && document.Length <= 12 && document |> List.forall isName && query = Some "ref=main" && not paginated
            | _ -> false

    let getArguments (path: string) (paginated: bool) : string list option =
        if not (allowedEndpoint path paginated) then None
        else
            Some(
                [ "api"; "--method"; "GET"; "-H"; "Accept: application/vnd.github+json"; "-H"; "X-GitHub-Api-Version: 2022-11-28" ]
                @ (if paginated then [ "--paginate"; "--slurp" ] else [])
                @ [ path ])

    // ---- bounded strict reading; nothing read here is echoed ----

    let private maxPages = 50
    let private maxItems = 5000

    let private parse (body: string) : JsonDocument =
        if isNull body || body.Length = 0 || body.Length > 33554432 then raise (FormatException "body")
        JsonDocument.Parse(body, JsonDocumentOptions(MaxDepth = 32))

    let private str (name: string) (element: JsonElement) =
        let mutable value = Unchecked.defaultof<JsonElement>
        if element.ValueKind = JsonValueKind.Object && element.TryGetProperty(name, &value) && value.ValueKind = JsonValueKind.String then value.GetString()
        else raise (FormatException "string")

    let private tryProperty (name: string) (element: JsonElement) =
        let mutable value = Unchecked.defaultof<JsonElement>
        if element.ValueKind = JsonValueKind.Object && element.TryGetProperty(name, &value) && value.ValueKind <> JsonValueKind.Null then Some value else None

    let private repositoryFromUrl (url: string) =
        let m = Regex.Match(url, @"\Ahttps://api\.github\.com/repos/([A-Za-z0-9_.-]{1,100}/[A-Za-z0-9_.-]{1,100})\z", RegexOptions.CultureInvariant)
        if m.Success then m.Groups.[1].Value else raise (FormatException "repository")

    let private flatten (document: JsonDocument) =
        let root = document.RootElement
        if root.ValueKind <> JsonValueKind.Array then raise (FormatException "pages")
        let pages = root.EnumerateArray() |> Seq.toList
        if pages.Length > maxPages then raise (FormatException "pages")
        pages |> List.collect (fun page ->
            if page.ValueKind <> JsonValueKind.Array then raise (FormatException "page")
            page.EnumerateArray() |> Seq.toList)

    let private fieldNames = [ "Status"; "Roadmap"; "Track"; "Work level" ]

    let private fail () : 'a = raise (FormatException "native")

    let private get' (get: Get) (path: string) (paginated: bool) =
        match getArguments path paginated with
        | None -> fail ()
        | Some _ -> get path paginated

    let private projectPath (binding: V2Projection.Binding) =
        if binding.OwnerKind <> Transport.OwnerKind.Org then fail ()
        $"/orgs/{binding.Owner}/projectsV2/{binding.ProjectNumber}"

    let private fieldPresent (item: JsonElement) (name: string) =
        match tryProperty "fields" item with
        | None -> false
        | Some fields when fields.ValueKind = JsonValueKind.Array ->
            fields.EnumerateArray()
            |> Seq.exists (fun field ->
                str "name" field = name
                && (match tryProperty "value" field with
                    | None -> false
                    | Some value ->
                        match value.ValueKind with
                        | JsonValueKind.Object ->
                            (match tryProperty "name" value with
                             | Some label when label.ValueKind = JsonValueKind.Object -> (match tryProperty "raw" label with Some raw -> raw.ValueKind = JsonValueKind.String && raw.GetString().Length > 0 | None -> false)
                             | Some label when label.ValueKind = JsonValueKind.String -> label.GetString().Length > 0
                             | _ -> (match tryProperty "raw" value with Some raw -> raw.ValueKind = JsonValueKind.String && raw.GetString().Length > 0 | None -> false))
                        | JsonValueKind.String -> value.GetString().Length > 0
                        | _ -> false))
        | _ -> fail ()

    let private roadmapRaw (item: JsonElement) =
        match tryProperty "fields" item with
        | Some fields when fields.ValueKind = JsonValueKind.Array ->
            fields.EnumerateArray()
            |> Seq.tryPick (fun field ->
                if str "name" field = "Roadmap" then
                    match tryProperty "value" field with
                    | Some value when value.ValueKind = JsonValueKind.Object ->
                        (match tryProperty "raw" value with Some raw when raw.ValueKind = JsonValueKind.String && raw.GetString().Length > 0 -> Some(raw.GetString()) | _ -> None)
                    | Some value when value.ValueKind = JsonValueKind.String && value.GetString().Length > 0 -> Some(value.GetString())
                    | _ -> None
                else None)
        | _ -> None

    let private itemOf (element: JsonElement) : ProjectItem =
        let content =
            match str "content_type" element with
            | "Issue" | "PullRequest" as kind ->
                let body = element.GetProperty "content"
                let reference = repositoryFromUrl (str "repository_url" body) + "#" + string (body.GetProperty("number").GetInt32())
                let node = str "node_id" body
                if kind = "Issue" then IssueContent(reference, node) else PullRequestContent(reference, node)
            | other -> OtherContent(if Regex.IsMatch(other, @"\A[A-Za-z]{1,40}\z") then other else "Unknown")
        { ItemId = str "node_id" element
          Content = content
          HasStatus = fieldPresent element "Status"
          HasTrack = fieldPresent element "Track"
          HasWorkLevel = fieldPresent element "Work level"
          RoadmapRaw = roadmapRaw element
          UpdatedAt = DateTimeOffset.Parse(str "updated_at" element, Globalization.CultureInfo.InvariantCulture) }

    let readProject (get: Get) (binding: V2Projection.Binding) : Result<string * ProjectItem list, V2Enrollment.Refusal> =
        try
            let project = projectPath binding
            let projectId =
                match get' get project false with
                | Body body ->
                    use document = parse body
                    if document.RootElement.GetProperty("number").GetInt32() <> binding.ProjectNumber then fail ()
                    str "node_id" document.RootElement
                | _ -> fail ()
            let fieldIds =
                match get' get (project + "/fields?per_page=100") true with
                | Body body ->
                    use document = parse body
                    let fields = flatten document
                    fieldNames |> List.map (fun name ->
                        match fields |> List.filter (fun field -> str "name" field = name) with
                        | [ field ] -> string (field.GetProperty("id").GetInt64())
                        | _ -> fail ())
                | _ -> fail ()
            let items =
                match get' get (project + "/items?per_page=100&fields=" + String.Join(",", fieldIds)) true with
                | Body body ->
                    use document = parse body
                    let elements = flatten document
                    if elements.Length > maxItems then fail ()
                    elements |> List.map itemOf
                | _ -> fail ()
            if (items |> List.map _.ItemId |> Set.ofList).Count <> items.Length then fail ()
            Ok(projectId, items)
        with _ -> Error V2Enrollment.NativeReadFailed

    type private IssueRead =
        | Read of nodeId: string * isPullRequest: bool * repository: string
        | Absent

    let private readIssue (get: Get) (reference: string) : IssueRead =
        let hash = reference.IndexOf '#'
        let repository, number = reference.Substring(0, hash), reference.Substring(hash + 1)
        match get' get ($"/repos/{repository}/issues/{number}") false with
        | NotFound -> Absent
        | Body body ->
            use document = parse body
            let root = document.RootElement
            Read(str "node_id" root, (tryProperty "pull_request" root).IsSome, repositoryFromUrl (str "repository_url" root))
        | Failed -> fail ()

    // (on main, repository hidden). A 404 is absence only if the repository itself is readable; otherwise
    // it may be invisibility, which is a different fact from absence.
    let private blobOnMain (get: Get) (repository: string) (path: string) : bool * bool =
        match get' get ($"/repos/{repository}/contents/{path}?ref=main") false with
        | NotFound ->
            match get' get ($"/repos/{repository}") false with
            | Body _ -> false, false
            | NotFound -> false, true
            | Failed -> fail ()
        | Body body ->
            use document = parse body
            let root = document.RootElement
            (root.ValueKind = JsonValueKind.Object && str "type" root = "file"), false
        | Failed -> fail ()

    let resolve (get: Get) (binding: V2Projection.Binding) (request: V2Enrollment.Request) : Result<Resolved, V2Enrollment.Refusal> =
        match readProject get binding with
        | Error refusal -> Error refusal
        | Ok(projectId, items) ->
            try
                let membersByNode =
                    items |> List.choose (fun item -> match item.Content with IssueContent(_, node) -> Some(node, item) | _ -> None) |> Map.ofList
                let issues =
                    request.Issues |> List.map (fun issue ->
                        match readIssue get issue.Ref with
                        | Absent ->
                            ({ Ref = issue.Ref; NodeId = "I_unresolved"; Kind = V2Enrollment.NativeIssue
                               Repository = issue.Ref.Substring(0, issue.Ref.IndexOf '#'); ProjectMember = false; Complete = false; HumanRoadmap = None }: V2Enrollment.NativeIssueFact)
                        | Read(node, isPullRequest, repository) ->
                            let item = Map.tryFind node membersByNode
                            ({ Ref = issue.Ref; NodeId = node
                               Kind = (if isPullRequest then V2Enrollment.NativePullRequest else V2Enrollment.NativeIssue)
                               Repository = repository; ProjectMember = item.IsSome; Complete = true
                               HumanRoadmap = item |> Option.bind _.RoadmapRaw }: V2Enrollment.NativeIssueFact))
                let reads =
                    request.Issues
                    |> List.choose (fun issue -> match issue.Plan with V2Enrollment.ProtectedDoc(repository, path) -> Some(repository, path) | _ -> None)
                    |> List.distinct
                    |> List.map (fun (repository, path) -> repository, path, blobOnMain get repository path)
                let blobs =
                    reads |> List.map (fun (repository, path, (onMain, _)) ->
                        ({ Repository = repository; Path = path; OnMain = onMain }: V2Enrollment.PlanBlobFact))
                let hidden = reads |> List.choose (fun (repository, _, (_, isHidden)) -> if isHidden then Some repository else None) |> List.distinct
                Ok { Facts = { ProjectId = projectId; Issues = issues; PlanBlobs = blobs }; Items = items; HiddenRepositories = hidden }
            with _ -> Error V2Enrollment.NativeReadFailed

    let encodeNativeFacts (facts: V2Enrollment.NativeFacts) : string =
        let root = JsonObject()
        root.["schema"] <- JsonValue.Create "fsgg.board-v2-native-facts/1"
        root.["projectId"] <- JsonValue.Create facts.ProjectId
        let issues = JsonArray()
        for fact in facts.Issues do
            let row = JsonObject()
            row.["ref"] <- JsonValue.Create fact.Ref
            row.["nodeId"] <- JsonValue.Create fact.NodeId
            row.["kind"] <- JsonValue.Create(match fact.Kind with V2Enrollment.NativeIssue -> "Issue" | V2Enrollment.NativePullRequest -> "PullRequest")
            row.["repository"] <- JsonValue.Create fact.Repository
            row.["projectMember"] <- JsonValue.Create fact.ProjectMember
            row.["visibility"] <- JsonValue.Create(if fact.Complete then "complete" else "partial")
            row.["humanRoadmap"] <- (match fact.HumanRoadmap with Some value -> JsonValue.Create value :> JsonNode | None -> null)
            issues.Add row
        root.["issues"] <- issues
        let blobs = JsonArray()
        for blob in facts.PlanBlobs do
            let row = JsonObject()
            row.["repository"] <- JsonValue.Create blob.Repository
            row.["path"] <- JsonValue.Create blob.Path
            row.["onMain"] <- JsonValue.Create blob.OnMain
            blobs.Add row
        root.["planBlobs"] <- blobs
        root.ToJsonString()

    // ---- the protected main head and pull requests (read-only) ----

    let private validRepository (repository: string) =
        Regex.IsMatch(repository, @"\A[A-Za-z0-9_.-]{1,100}/[A-Za-z0-9_.-]{1,100}\z", RegexOptions.CultureInvariant)

    let readMainHead (get: Get) (repository: string) : Result<string, V2Enrollment.Refusal> =
        try
            if not (validRepository repository) then fail ()
            match get' get ($"/repos/{repository}/git/ref/heads/main") false with
            | Body body ->
                use document = parse body
                let sha = str "sha" (document.RootElement.GetProperty "object")
                if Regex.IsMatch(sha, @"\A[0-9a-f]{40}\z") then Ok sha else fail ()
            | _ -> fail ()
        with _ -> Error V2Enrollment.NativeReadFailed

    let private pullRequestOf (element: JsonElement) : PullRequest =
        let side (name: string) = element.GetProperty name
        { Number = element.GetProperty("number").GetInt32()
          State = str "state" element
          Title = str "title" element
          Body = (match tryProperty "body" element with Some value when value.ValueKind = JsonValueKind.String -> value.GetString() | _ -> "")
          BaseRef = str "ref" (side "base")
          BaseSha = str "sha" (side "base")
          HeadRef = str "ref" (side "head")
          HeadSha = str "sha" (side "head") }

    let readPullRequest (get: Get) (repository: string) (number: int) : Result<PullRequest, V2Enrollment.Refusal> =
        try
            if not (validRepository repository) || number < 1 then fail ()
            match get' get ($"/repos/{repository}/pulls/{number}") false with
            | Body body ->
                use document = parse body
                Ok(pullRequestOf document.RootElement)
            | _ -> fail ()
        with _ -> Error V2Enrollment.NativeReadFailed

    let openPullRequestsFor (get: Get) (repository: string) (headRef: string) : Result<PullRequest list, V2Enrollment.Refusal> =
        try
            if not (validRepository repository) then fail ()
            match get' get ($"/repos/{repository}/pulls?state=open&per_page=100") true with
            | Body body ->
                use document = parse body
                Ok(flatten document |> List.map pullRequestOf |> List.filter (fun pr -> pr.HeadRef = headRef))
            | _ -> fail ()
        with _ -> Error V2Enrollment.NativeReadFailed
