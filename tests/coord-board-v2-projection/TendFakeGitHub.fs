/// A stateful fake of the GitHub GraphQL surface the tend writer uses (FS-GG/.github#5136 phase 2 PR 4).
module FS.GG.Coord.GitHub.Tests.TendFakeGitHub

open System
open System.Text.Json.Nodes
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Transport

type FakeItem =
    { ContentId: string
      Ref: string
      mutable Open: bool
      mutable StateReason: string option
      Parent: string option
      mutable Values: Map<string, string>
      mutable Archived: bool }

type FakePr =
    { mutable State: string
      mutable Draft: bool
      MergedAt: string option
      Body: string
      MergeOid: string
      Comments: (string * string) list }

let projectId = "PVT_kwDOEYAWY84Bldpa"

let statusOptions = [ "Backlog"; "Ready"; "In progress"; "In review"; "Blocked"; "Done" ]
let fieldDefinitions =
    [ "Status", "SINGLE_SELECT", statusOptions
      "Work level", "SINGLE_SELECT", [ "Outcome"; "Delivery" ]
      "Track", "SINGLE_SELECT", [ "Active delivery"; "Follow-up" ]
      "Roadmap", "TEXT", [] ]

let fieldId (name: string) = "F_" + name.Replace(" ", "_")
let optionId (field: string) (value: string) = "O_" + field.Replace(" ", "_") + "_" + value.Replace(" ", "_")

type Call = { Kind: string; Subject: string; Document: string }

type Board() =
    let items = Collections.Generic.Dictionary<string, FakeItem>()
    let prs = Collections.Generic.Dictionary<string, FakePr>()
    let comments = Collections.Generic.Dictionary<string, Collections.Generic.List<string * string * string>>()
    let timelines = Collections.Generic.Dictionary<string, string list>()
    let calls = Collections.Generic.List<Call>()
    let mutable commentCounter = 0

    member _.Items = items
    member _.Prs = prs
    member _.Comments = comments
    member _.Timelines = timelines
    member _.Calls = List.ofSeq calls
    member _.Mutations = calls |> Seq.filter (fun c -> c.Kind = "mutation") |> List.ofSeq
    /// Runs before a mutation takes effect (a concurrent writer); returning false loses the response.
    member val BeforeMutation: (Request -> unit) = ignore with get, set
    member val FailMutation = false with get, set
    member val LoseResponse = false with get, set

    member _.AddItem(itemId: string, item: FakeItem) = items.[itemId] <- item
    member _.AddPr(reference: string, pr: FakePr) = prs.[reference] <- pr

    member private this.ItemNode(itemId: string) : JsonNode =
        match items.TryGetValue itemId with
        | false, _ -> null
        | true, item ->
            let node = JsonObject()
            node["__typename"] <- "ProjectV2Item"
            node["id"] <- itemId
            node["isArchived"] <- item.Archived
            let fields = JsonArray()
            for name, kind, options in fieldDefinitions do
                let f = JsonObject()
                f["__typename"] <- (if kind = "TEXT" then "ProjectV2Field" else "ProjectV2SingleSelectField")
                f["id"] <- fieldId name
                f["name"] <- name
                f["dataType"] <- kind
                if kind <> "TEXT" then
                    f["options"] <- JsonArray([| for o in options -> JsonObject(dict [ "id", JsonValue.Create(optionId name o) :> JsonNode; "name", JsonValue.Create o :> JsonNode ]) :> JsonNode |])
                fields.Add f
            node["project"] <- JsonObject(dict [ "id", JsonValue.Create projectId :> JsonNode; "fields", JsonObject(dict [ "totalCount", JsonValue.Create fields.Count :> JsonNode; "nodes", fields :> JsonNode ]) :> JsonNode ])
            let repo, number = item.Ref.Substring(0, item.Ref.LastIndexOf '#'), int (item.Ref.Substring(item.Ref.LastIndexOf '#' + 1))
            let content = JsonObject()
            content["__typename"] <- "Issue"
            content["id"] <- item.ContentId
            content["number"] <- number
            content["state"] <- (if item.Open then "OPEN" else "CLOSED")
            content["repository"] <- JsonObject(dict [ "nameWithOwner", JsonValue.Create repo :> JsonNode ])
            content["parent"] <-
                (match item.Parent with
                 | Some p -> JsonObject(dict [ "number", JsonValue.Create(int (p.Substring(p.LastIndexOf '#' + 1))) :> JsonNode; "repository", JsonObject(dict [ "nameWithOwner", JsonValue.Create(p.Substring(0, p.LastIndexOf '#')) :> JsonNode ]) :> JsonNode ]) :> JsonNode
                 | None -> null)
            node["content"] <- content
            let values = JsonArray()
            for KeyValue(name, value) in item.Values do
                let v = JsonObject()
                let field = JsonObject(dict [ "__typename", JsonValue.Create "ProjectV2SingleSelectField" :> JsonNode; "id", JsonValue.Create(fieldId name) :> JsonNode; "name", JsonValue.Create name :> JsonNode ])
                if name = "Roadmap" then
                    v["__typename"] <- "ProjectV2ItemFieldTextValue"
                    v["text"] <- value
                else
                    v["__typename"] <- "ProjectV2ItemFieldSingleSelectValue"
                    v["name"] <- value
                    v["optionId"] <- optionId name value
                v["field"] <- field
                values.Add v
            // An unrelated system value, which must survive every readback unchanged.
            values.Add(JsonObject(dict [ "__typename", JsonValue.Create "ProjectV2ItemFieldTextValue" :> JsonNode; "text", JsonValue.Create item.Ref :> JsonNode; "field", JsonObject(dict [ "id", JsonValue.Create "F_Title" :> JsonNode; "name", JsonValue.Create "Title" :> JsonNode ]) :> JsonNode ]))
            node["fieldValues"] <- JsonObject(dict [ "totalCount", JsonValue.Create values.Count :> JsonNode; "nodes", values :> JsonNode ])
            node :> JsonNode

    member private this.Respond(data: JsonObject) =
        Ok { Status = 200; ETag = None; NextLink = None; Headers = Map.empty; Body = JsonObject(dict [ "data", data :> JsonNode ]).ToJsonString() }

    member private this.Variable(variables: (string * Var) list, name: string) =
        variables |> List.tryPick (fun (n, v) -> if n = name then Some v else None)

    member private this.Text(variables, name) =
        match this.Variable(variables, name) with
        | Some(VId s) | Some(VString s) -> s
        | Some(VNumber n) -> string (int n)
        | _ -> ""

    member private this.IssueByContent(issueId: string) = items.Values |> Seq.tryFind (fun i -> i.ContentId = issueId)

    member this.Query(request: Request) : Errors.IoResult<Response> =
        let document, variables = match request.Body with Query(d, v) -> d, v | _ -> failwith "query expected"
        calls.Add { Kind = "query"; Subject = request.Subject; Document = document }
        let data = JsonObject()
        if document.Contains "node(id: $item)" then
            data["node"] <- this.ItemNode(this.Text(variables, "item"))
            let mutable i = 0
            while document.Contains(sprintf "pr%d:" i) do
                let reference = sprintf "%s/%s#%s" (this.Text(variables, sprintf "o%d" i)) (this.Text(variables, sprintf "n%d" i)) (this.Text(variables, sprintf "k%d" i))
                match prs.TryGetValue reference with
                | true, pr ->
                    data[sprintf "pr%d" i] <- JsonObject(dict [ "pullRequest", JsonObject(dict [ "state", JsonValue.Create pr.State :> JsonNode; "isDraft", JsonValue.Create pr.Draft :> JsonNode; "mergedAt", (match pr.MergedAt with Some m -> JsonValue.Create m :> JsonNode | None -> null) ]) :> JsonNode ])
                | _ -> data[sprintf "pr%d" i] <- null
                i <- i + 1
        elif document.Contains "issue: node(id: $issue)" then
            let issue = this.IssueByContent(this.Text(variables, "issue"))
            data["issue"] <-
                (match issue with
                 | Some i -> JsonObject(dict [ "state", JsonValue.Create(if i.Open then "OPEN" else "CLOSED") :> JsonNode; "stateReason", (match i.StateReason with Some r -> JsonValue.Create r :> JsonNode | None -> null) ]) :> JsonNode
                 | None -> null)
            let commentId = this.Text(variables, "comment")
            let body = comments.Values |> Seq.collect id |> Seq.tryFind (fun (id, _, _) -> id = commentId) |> Option.map (fun (_, b, _) -> b)
            data["comment"] <- (match body with Some b -> JsonObject(dict [ "body", JsonValue.Create b :> JsonNode ]) :> JsonNode | None -> null)
        elif document.Contains "timelineItems" then
            let issueId = this.Text(variables, "issue")
            let refs = match timelines.TryGetValue issueId with | true, r -> r | _ -> []
            let nodes =
                [| for index, reference in List.indexed refs ->
                    let pr = prs.[reference]
                    let repo, number = reference.Substring(0, reference.LastIndexOf '#'), int (reference.Substring(reference.LastIndexOf '#' + 1))
                    JsonObject(dict [ "id", JsonValue.Create(sprintf "CRE_%d" index) :> JsonNode
                                      "source", JsonObject(dict [ "__typename", JsonValue.Create "PullRequest" :> JsonNode; "number", JsonValue.Create number :> JsonNode; "state", JsonValue.Create pr.State :> JsonNode; "isDraft", JsonValue.Create pr.Draft :> JsonNode; "body", JsonValue.Create pr.Body :> JsonNode; "repository", JsonObject(dict [ "nameWithOwner", JsonValue.Create repo :> JsonNode ]) :> JsonNode ]) :> JsonNode ]) :> JsonNode |]
            data["node"] <- JsonObject(dict [ "__typename", JsonValue.Create "Issue" :> JsonNode; "timelineItems", JsonObject(dict [ "totalCount", JsonValue.Create nodes.Length :> JsonNode; "pageInfo", JsonObject(dict [ "hasNextPage", JsonValue.Create false :> JsonNode; "endCursor", null ]) :> JsonNode; "nodes", JsonArray(nodes) :> JsonNode ]) :> JsonNode ])
        elif document.Contains "comments(first: 100, after: $cursor)" && document.Contains "node(id: $issue)" then
            let issueId = this.Text(variables, "issue")
            let list = match comments.TryGetValue issueId with | true, l -> List.ofSeq l | _ -> []
            let nodes = [| for id, body, author in list -> JsonObject(dict [ "id", JsonValue.Create id :> JsonNode; "body", JsonValue.Create body :> JsonNode; "author", JsonObject(dict [ "login", JsonValue.Create author :> JsonNode ]) :> JsonNode ]) :> JsonNode |]
            data["node"] <- JsonObject(dict [ "__typename", JsonValue.Create "Issue" :> JsonNode; "comments", JsonObject(dict [ "totalCount", JsonValue.Create nodes.Length :> JsonNode; "pageInfo", JsonObject(dict [ "hasNextPage", JsonValue.Create false :> JsonNode; "endCursor", null ]) :> JsonNode; "nodes", JsonArray(nodes) :> JsonNode ]) :> JsonNode ])
        elif document.Contains "node(id: $issue)" then
            match this.IssueByContent(this.Text(variables, "issue")) with
            | Some i ->
                let repo, number = i.Ref.Substring(0, i.Ref.LastIndexOf '#'), int (i.Ref.Substring(i.Ref.LastIndexOf '#' + 1))
                data["node"] <- JsonObject(dict [ "__typename", JsonValue.Create "Issue" :> JsonNode; "id", JsonValue.Create i.ContentId :> JsonNode; "number", JsonValue.Create number :> JsonNode; "state", JsonValue.Create(if i.Open then "OPEN" else "CLOSED") :> JsonNode; "repository", JsonObject(dict [ "nameWithOwner", JsonValue.Create repo :> JsonNode ]) :> JsonNode ])
            | None -> data["node"] <- null
        elif document.Contains "mergeCommit" then
            let reference = sprintf "%s/%s#%s" (this.Text(variables, "o")) (this.Text(variables, "n")) (this.Text(variables, "k"))
            let pr = prs.[reference]
            let nodes = [| for index, (url, body) in List.indexed pr.Comments -> JsonObject(dict [ "id", JsonValue.Create(sprintf "PRC_%d" index) :> JsonNode; "url", JsonValue.Create url :> JsonNode; "body", JsonValue.Create body :> JsonNode ]) :> JsonNode |]
            data["repository"] <- JsonObject(dict [ "pullRequest", JsonObject(dict [ "mergeCommit", JsonObject(dict [ "oid", JsonValue.Create pr.MergeOid :> JsonNode ]) :> JsonNode; "comments", JsonObject(dict [ "totalCount", JsonValue.Create nodes.Length :> JsonNode; "pageInfo", JsonObject(dict [ "hasNextPage", JsonValue.Create false :> JsonNode; "endCursor", null ]) :> JsonNode; "nodes", JsonArray(nodes) :> JsonNode ]) :> JsonNode ]) :> JsonNode ])
        else failwithf "unexpected query: %s" document
        this.Respond data

    member this.Mutate(request: Request) : Errors.IoResult<Response> =
        let document, variables = match request.Body with Query(d, v) -> d, v | _ -> failwith "query expected"
        calls.Add { Kind = "mutation"; Subject = request.Subject; Document = document }
        this.BeforeMutation request
        if this.FailMutation then Error(Errors.Malformed(request.Subject, "network failure after send"))
        else
            let data = JsonObject()
            if document.Contains "updateProjectV2ItemFieldValue" then
                let item = items.[this.Text(variables, "item")]
                let mutable i = 0
                while document.Contains(sprintf "e%d:" i) do
                    let field = this.Text(variables, sprintf "f%d" i)
                    let name, _, _ = fieldDefinitions |> List.find (fun (n, _, _) -> fieldId n = field)
                    let raw = this.Text(variables, sprintf "v%d" i)
                    let value = if name = "Roadmap" then raw else (fieldDefinitions |> List.pick (fun (n, _, options) -> if n = name then options |> List.tryFind (fun o -> optionId n o = raw) else None))
                    item.Values <- item.Values.Add(name, value)
                    data[sprintf "e%d" i] <- JsonObject(dict [ "clientMutationId", JsonValue.Create(this.Text(variables, sprintf "c%d" i)) :> JsonNode ])
                    i <- i + 1
            elif document.Contains "addComment" then
                let issueId = this.Text(variables, "issue")
                commentCounter <- commentCounter + 1
                let id = sprintf "IC_%d" commentCounter
                if not (comments.ContainsKey issueId) then comments.[issueId] <- Collections.Generic.List()
                comments.[issueId].Add((id, this.Text(variables, "body"), "EHotwagner"))
                data["e0"] <- JsonObject(dict [ "clientMutationId", JsonValue.Create(this.Text(variables, "c0")) :> JsonNode; "commentEdge", JsonObject(dict [ "node", JsonObject(dict [ "id", JsonValue.Create id :> JsonNode ]) :> JsonNode ]) :> JsonNode ])
            elif document.Contains "closeIssue" then
                let issue = (this.IssueByContent(this.Text(variables, "issue"))).Value
                issue.Open <- false
                issue.StateReason <- Some "COMPLETED"
                data["e0"] <- JsonObject(dict [ "clientMutationId", JsonValue.Create(this.Text(variables, "c0")) :> JsonNode; "issue", JsonObject(dict [ "id", JsonValue.Create issue.ContentId :> JsonNode; "state", JsonValue.Create "CLOSED" :> JsonNode ]) :> JsonNode ])
            else failwithf "unexpected mutation: %s" document
            if this.LoseResponse then Error(Errors.Malformed(request.Subject, "response lost after the send"))
            else this.Respond data

    member this.Transport =
        { new IGitHubTransport with
            member _.Send request = this.Query request
            member _.SendMutation _ = failwith "tend never uses SendMutation"
            member _.RetryMutation _ = failwith "tend never retries" }
