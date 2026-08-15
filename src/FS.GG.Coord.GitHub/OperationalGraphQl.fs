namespace FS.GG.Coord.GitHub

module OperationalGraphQl =
    open System
    open System.Text.Json
    open Errors
    open Transport

    type RepositoryPolicy =
        { IssueCreationPolicy: string
          HasIssuesEnabled: bool }

    type ArchiveRow =
        { ItemId: string
          Status: string option
          BlockedBy: string option
          Number: int option
          State: string option
          ClosedAt: string option
          Repo: string option }

    type ArchiveScan =
        { Items: ArchiveRow list
          Pages: int
          Spent: int }

    type RosterRow =
        { Owner: string
          Repo: string
          Number: int option
          Status: string }

    let private malformed (subject: string) (detail: string) = Error(Malformed(subject, detail))
    let private requiredObject (subject: string) (name: string) (parent: JsonElement) =
        match parent.TryGetProperty name with
        | true, value when value.ValueKind = JsonValueKind.Object -> Ok value
        | _ -> malformed subject $"the response omitted object `%s{name}`"

    let private stringOption (name: string) (parent: JsonElement) =
        match parent.TryGetProperty name with
        | true, value when value.ValueKind = JsonValueKind.String -> Some(value.GetString())
        | _ -> None

    let private request (subject: string) (document: string) (variables: (string * Var) list) : Request =
        { Method = "POST"; Path = "graphql"; Query = []; Body = Query(document, variables)
          Budget = GraphQl; IfNoneMatch = None; Subject = subject }

    let private projectsDocument = "query($owner:String!,$cursor:String){organization(login:$owner){projectsV2(first:100,after:$cursor){totalCount pageInfo{hasNextPage endCursor} nodes{id title public number}}} rateLimit{cost remaining}}"

    type private Project = { Id: string; Title: string; Public: bool; Number: int }

    let private projects (transport: IGitHubTransport) (owner: string) =
        let subject = $"projects under %s{owner}"
        let fetch cursor =
            let variables = [ "owner", VString owner ] @ (cursor |> Option.map (fun c -> [ "cursor", VString c ]) |> Option.defaultValue [])
            GraphQl.read transport (request subject projectsDocument variables) (fun data ->
                match requiredObject subject "organization" data with
                | Error error -> Error error
                | Ok org ->
                    match requiredObject subject "projectsV2" org with
                    | Error error -> Error error
                    | Ok connection ->
                        GraphQl.page subject "the project list" (fun p -> p.Id) (fun node ->
                            try
                                Ok { Id = node.GetProperty("id").GetString(); Title = node.GetProperty("title").GetString(); Public = node.GetProperty("public").GetBoolean(); Number = node.GetProperty("number").GetInt32() }
                            with _ -> malformed subject "a project node omitted id/title/public/number") connection)
        GraphQl.drain subject "the project list" { MaxPages = 100; MaxItems = 10000 } fetch

    let projectVisibility (transport: IGitHubTransport) (owner: string) (title: string) =
        match projects transport owner with
        | Error error -> Error error
        | Ok values ->
            match values |> List.filter (fun p -> p.Title = title) with
            | [] -> Ok None
            | [ value ] -> Ok(Some value.Public)
            | matches -> malformed title $"expected at most one project named `%s{title}`, found %d{matches.Length}"

    let projectId (transport: IGitHubTransport) (owner: string) (number: int) =
        let subject = $"%s{owner} project %d{number}"
        let document = "query($owner:String!,$number:Int!){organization(login:$owner){projectV2(number:$number){id title}} rateLimit{cost remaining}}"
        GraphQl.read transport (request subject document [ "owner", VString owner; "number", VNumber(float number) ]) (fun data ->
            match requiredObject subject "organization" data with
            | Error error -> Error error
            | Ok org ->
                match requiredObject subject "projectV2" org with
                | Error error -> Error error
                | Ok project ->
                    match stringOption "id" project with Some id when not(String.IsNullOrWhiteSpace id) -> Ok id | _ -> malformed subject "project id was missing")

    let repositoryPolicy (transport: IGitHubTransport) (owner: string) (name: string) =
        let subject = $"%s{owner}/%s{name} repository policy"
        let document = "query($owner:String!,$name:String!){repository(owner:$owner,name:$name){issueCreationPolicy hasIssuesEnabled} rateLimit{cost remaining}}"
        GraphQl.read transport (request subject document [ "owner", VString owner; "name", VString name ]) (fun data ->
            match requiredObject subject "repository" data with
            | Error error -> Error error
            | Ok repo ->
                try Ok { IssueCreationPolicy = repo.GetProperty("issueCreationPolicy").GetString(); HasIssuesEnabled = repo.GetProperty("hasIssuesEnabled").GetBoolean() }
                with _ -> malformed subject "repository policy fields were incomplete")

    let meterRemaining (transport: IGitHubTransport) =
        let subject = "GraphQL rate meter"
        GraphQl.read transport (request subject "query { rateLimit { remaining cost } }" []) (fun data ->
            match requiredObject subject "rateLimit" data with
            | Error error -> Error error
            | Ok meter -> try Ok(meter.GetProperty("remaining").GetInt32()) with _ -> malformed subject "remaining was missing or not an integer")

    let private archiveDocument = "query($project:ID!,$cursor:String){node(id:$project){... on ProjectV2{items(first:100,after:$cursor){totalCount pageInfo{hasNextPage endCursor} nodes{id status:fieldValueByName(name:\"Status\"){... on ProjectV2ItemFieldSingleSelectValue{name}} blockedBy:fieldValueByName(name:\"Blocked by\"){... on ProjectV2ItemFieldTextValue{text}} content{__typename ... on Issue{number state closedAt repository{nameWithOwner}} ... on PullRequest{number state closedAt repository{nameWithOwner}}}}}}} rateLimit{cost remaining}}"

    let archiveScan (transport: IGitHubTransport) (projectId: string) =
        let subject = $"project %s{projectId} archive scan"
        let before = Budget.graphQlSpend ()
        let fetch cursor =
            let variables = [ "project", VId projectId ] @ (cursor |> Option.map (fun c -> [ "cursor", VString c ]) |> Option.defaultValue [])
            GraphQl.read transport (request subject archiveDocument variables) (fun data ->
                match requiredObject subject "node" data with
                | Error error -> Error error
                | Ok node ->
                    match requiredObject subject "items" node with
                    | Error error -> Error error
                    | Ok connection ->
                        GraphQl.page subject "the archive scan" (fun row -> row.ItemId) (fun raw ->
                            try
                                let content = match raw.TryGetProperty "content" with true, v when v.ValueKind = JsonValueKind.Object -> Some v | _ -> None
                                let nested (name: string) (field: string) = match raw.TryGetProperty name with true, v when v.ValueKind = JsonValueKind.Object -> stringOption field v | _ -> None
                                Ok { ItemId = raw.GetProperty("id").GetString(); Status = nested "status" "name"; BlockedBy = nested "blockedBy" "text"; Number = content |> Option.bind (fun c -> match c.TryGetProperty "number" with true,v when v.ValueKind=JsonValueKind.Number -> Some(v.GetInt32()) | _ -> None); State = content |> Option.bind (stringOption "state"); ClosedAt = content |> Option.bind (stringOption "closedAt"); Repo = content |> Option.bind (fun c -> match c.TryGetProperty "repository" with true,r when r.ValueKind=JsonValueKind.Object -> stringOption "nameWithOwner" r | _ -> None) }
                            with _ -> malformed subject "an archive item node was malformed") connection)
        match GraphQl.drain subject "the archive scan" { MaxPages = 100; MaxItems = 10000 } fetch with
        | Error error -> Error error
        | Ok items ->
            let after = Budget.graphQlSpend ()
            Ok { Items = items; Pages = after.Calls - before.Calls; Spent = after.Points - before.Points }

    let archiveItems (transport: IGitHubTransport) (projectId: string) (itemIds: string list) =
        if List.isEmpty itemIds then malformed projectId "archive mutation requires at least one item id" else
        let subject = $"archive %d{itemIds.Length} project item(s)"
        let declarations = [ "$p:ID!" ] @ [ for i in 0 .. itemIds.Length-1 -> $"$i%d{i}:ID!" ] |> String.concat ","
        let aliases = [ for i in 0 .. itemIds.Length-1 -> $"a%d{i}:archiveProjectV2Item(input:{{projectId:$p,itemId:$i%d{i}}}){{item{{id}}}}" ] |> String.concat " "
        let variables = [ "p", VId projectId ] @ (itemIds |> List.mapi (fun i id -> $"i%d{i}", VId id))
        let req = request subject $"mutation(%s{declarations}){{%s{aliases}}}" variables
        match transport.Send req with
        | Error error -> Error error
        | Ok response ->
            match GraphQl.decode subject response.Body (fun _ -> Ok()) with
            | Ok () -> Ok ()
            | Error(RateLimited _ as error) -> Error error
            | Error(GraphQlErrors _ as graphQlError) ->
                match GraphQl.partialMutation subject response.Body with
                | Error error -> Error error
                | Ok(applied, failed) when applied.IsEmpty -> Error graphQlError
                | Ok(applied, failed) -> Error(Partial(applied, failed))
            | Error error -> Error error

    let rosterBoard (transport: IGitHubTransport) (owner: string) (title: string) =
        match projects transport owner with
        | Error error -> Error error
        | Ok values ->
            match values |> List.filter (fun p -> p.Title = title) with
            | [ project ] ->
                let subject = $"%s{owner}/%s{title} roster board"
                let document = "query($owner:String!,$number:Int!,$cursor:String){organization(login:$owner){projectV2(number:$number){items(first:100,after:$cursor){totalCount pageInfo{hasNextPage endCursor} nodes{id status:fieldValueByName(name:\"Status\"){... on ProjectV2ItemFieldSingleSelectValue{name}} content{__typename ... on Issue{number repository{owner{login} name}} ... on PullRequest{number repository{owner{login} name}}}}}}} rateLimit{cost remaining}}"
                let fetch cursor =
                    let vars = [ "owner",VString owner; "number",VNumber(float project.Number) ] @ (cursor |> Option.map(fun c -> ["cursor",VString c]) |> Option.defaultValue [])
                    GraphQl.read transport (request subject document vars) (fun data ->
                        match requiredObject subject "organization" data with
                        | Error error -> Error error
                        | Ok org -> match requiredObject subject "projectV2" org with
                                    | Error error -> Error error
                                    | Ok projectNode -> match requiredObject subject "items" projectNode with
                                                        | Error error -> Error error
                                                        | Ok connection -> GraphQl.page subject "the roster board" (fun (id,_) -> id) (fun raw ->
                                                            try
                                                                let id = raw.GetProperty("id").GetString()
                                                                let content = raw.GetProperty("content")
                                                                let kind = content.GetProperty("__typename").GetString()
                                                                if kind <> "Issue" && kind <> "PullRequest" then Ok(id,None) else
                                                                let repo = content.GetProperty("repository")
                                                                let number = match content.TryGetProperty "number" with true,n when n.ValueKind=JsonValueKind.Number -> Some(n.GetInt32()) | _ -> None
                                                                let status = match raw.TryGetProperty "status" with true,s when s.ValueKind=JsonValueKind.Object -> stringOption "name" s |> Option.defaultValue "" | _ -> ""
                                                                let row = { Owner = repo.GetProperty("owner").GetProperty("login").GetString(); Repo = repo.GetProperty("name").GetString(); Number = number; Status = status }
                                                                Ok(id,Some row)
                                                            with _ -> malformed subject "a roster board node was malformed") connection)
                GraphQl.drain subject "the roster board" { MaxPages=100; MaxItems=10000 } fetch |> Result.map (List.choose snd)
            | matches -> malformed title $"expected exactly one project named `%s{title}`, found %d{matches.Length}"
