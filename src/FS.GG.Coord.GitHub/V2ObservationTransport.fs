namespace FS.GG.Coord.GitHub

module V2ObservationTransport =
    open System
    open System.Text.Json
    open System.Net.Http
    open Errors
    open Transport
    open V2Projection

    type DispatchReceipt =
        { HttpStatus: int option; Headers: Map<string,string>; ResponseSha256: string option; Error: string option }

    type IDispatchEvidence =
        abstract DispatchReceipts: DispatchReceipt list

    let private project = "PVT_kwDOEYAWY84Bldpa"
    let private field = "PVTSSF_lADOEYAWY84BldpazhkKrUw"
    let private verified = "c0e289aa"
    let private cohort =
        Map.ofList [ "I_kwDOS_PboM8AAAABOUVZAw", "FS-GG/FS.GG.SDD#928"
                     "I_kwDOTGkvVs8AAAABOUV4AA", "FS-GG/FS.GG.Templates#441"
                     "I_kwDOS6feoM8AAAABOUWpiA", "FS-GG/.github#3010" ]
    let private successorCohort = cohort.Add("I_kwDOS6feoM8AAAABOUU1ew", "FS-GG/.github#3009")
    let private document = "mutation($projectId: ID!, $itemId: ID!, $fieldId: ID!, $optionId: String!) { updateProjectV2ItemFieldValue(input: {projectId: $projectId, itemId: $itemId, fieldId: $fieldId, value: {singleSelectOptionId: $optionId}}) { clientMutationId } }"

    let validateScope (binding: Binding) =
        match validateBinding binding with
        | Error error -> Error error
        | Ok() when binding.Owner <> "FS-GG" || binding.OrganizationId <> "O_kgDOEYAWYw" || binding.ProjectNumber <> 3 || binding.ProjectId <> project
                    || binding.Observation.Id <> field || Map.tryFind "Verified" binding.Observation.Options <> Some verified || (binding.SelectedIssues <> cohort && binding.SelectedIssues <> successorCohort) ->
            Error(Malformed("V2 Observation authority", "selected root-local project/field/native cohort differs from the reviewed three-target pilot or four-target successor"))
        | Ok() -> Ok()

    let authorize binding (intent: MutationIntent) =
        match validateScope binding with
        | Error error -> Error error
        | Ok() ->
            let request = intent.Request
            match request.Body with
            | Query(actual, variables) when actual = document && request.Method = "POST" && request.Path = "graphql" && request.Budget = GraphQl && request.Query.IsEmpty && request.IfNoneMatch.IsNone ->
                match variables with
                | [ "projectId", VId p; "itemId", VId item; "fieldId", VId f; "optionId", VString option ] when p = project && f = field && option = verified && not (String.IsNullOrWhiteSpace item) ->
                    Ok { request with Body = Query(document, [ "projectId", VId project; "itemId", VId item; "fieldId", VId field; "optionId", VString verified ]) }
                | _ -> Error(Malformed("V2 Observation authority", "unknown variables, field, item, project or option"))
            | _ -> Error(Malformed("V2 Observation authority", "only the fixed Observation single-select update is admitted"))

    let compose binding (reads: IGitHubTransport) (nativeOnce: Transport.Request -> IoResult<Response>) : IoResult<IGitHubTransport> =
        validateScope binding |> Result.map (fun () ->
            { new IGitHubTransport with
                member _.Send request = reads.Send request
                member _.RetryMutation _ = Error(Malformed("V2 Observation authority", "durable replay and mutation retry are not supported"))
                member _.SendMutation intent =
                    authorize binding intent |> Result.bind (fun request ->
                        let item = match request.Body with Query(_, variables) -> variables |> List.pick (function "itemId", VId value -> Some value | _ -> None) | _ -> failwith "checked request"
                        let query = "query($itemId: ID!) { node(id: $itemId) { ... on ProjectV2Item { id project { id } content { ... on Issue { id number repository { nameWithOwner } } } } } rateLimit { cost remaining } }"
                        let identityRequest: Transport.Request = { request with Body = Query(query, [ "itemId", VId item ]); Subject = "fresh V2 Observation dispatch identity" }
                        GraphQl.read reads identityRequest (fun data ->
                            let node = data.GetProperty "node"
                            let content = node.GetProperty "content"
                            let nativeId = content.GetProperty("id").GetString()
                            let repository = content.GetProperty("repository").GetProperty("nameWithOwner").GetString()
                            let number = content.GetProperty("number").GetInt32()
                            let issue = $"{repository}#{number}"
                            if node.GetProperty("id").GetString() <> item || node.GetProperty("project").GetProperty("id").GetString() <> project || Map.tryFind nativeId binding.SelectedIssues <> Some issue then
                                Error(Malformed("V2 Observation authority", "fresh item project/content is not an admitted native pilot issue"))
                            else Ok()) |> Result.bind (fun () -> nativeOnce request)) })

    let createLive binding token : IoResult<IGitHubTransport * IDisposable> =
        if String.IsNullOrWhiteSpace token then Error(Unauthorized "root-local V2 Observation token")
        else
            validateScope binding |> Result.bind (fun () ->
                let reads = new HttpTransport("https://api.github.com", token)
                let handler = new HttpClientHandler(AllowAutoRedirect = false)
                let http = new HttpClient(handler)
                http.Timeout <- TimeSpan.FromSeconds 30.0
                http.DefaultRequestHeaders.UserAgent.ParseAdd "fsgg-coord-v2-observation"
                http.DefaultRequestHeaders.Authorization <- Headers.AuthenticationHeaderValue("Bearer", token)
                let readOnly =
                    { new IGitHubTransport with
                        member _.Send request =
                            if request.Method = "GET" && request.Path.EndsWith("/dependencies/blocked_by", StringComparison.Ordinal) then (reads :> ISinglePageGitHubTransport).SendSingle request
                            else (reads :> IGitHubTransport).Send request
                        member _.SendMutation _ = Error(Unauthorized "read-only V2 source transport")
                        member _.RetryMutation _ = Error(Unauthorized "read-only V2 source transport") }
                let mutable receipts = []
                let nativeOnce (request: Transport.Request) =
                    try
                        let variables = match request.Body with Query(_, variables) -> variables |> List.map (fun (key, value) -> key, match value with VId value | VString value -> value | _ -> failwith "checked variables") |> Map.ofList | _ -> failwith "checked body"
                        let body = JsonSerializer.Serialize {| query = document; variables = variables |}
                        use content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
                        use response = http.PostAsync("https://api.github.com/graphql", content).GetAwaiter().GetResult()
                        let body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                        let headers = response.Headers |> Seq.map (fun header -> header.Key, String.concat "," header.Value) |> Map.ofSeq
                        let result =
                            if not response.IsSuccessStatusCode then Error(Http(int response.StatusCode, "V2 Observation native dispatch refused"))
                            else GraphQl.decode request.Subject body (fun data ->
                                let payload = data.GetProperty "updateProjectV2ItemFieldValue"
                                if payload.ValueKind <> JsonValueKind.Object || not (payload.TryGetProperty("clientMutationId") |> fst) then
                                    Error(Malformed(request.Subject, "native mutation payload missing; effect unknown, no retry"))
                                else
                                    Ok { Status = int response.StatusCode; Body = body; ETag = None; NextLink = None
                                         Headers = headers })
                        receipts <- receipts @ [ { HttpStatus = Some(int response.StatusCode); Headers = headers
                                                   ResponseSha256 = Some(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes body) |> Convert.ToHexStringLower)
                                                   Error = (match result with Ok _ -> None | Error error -> Some(Errors.explain error)) } ]
                        result
                    with
                    | :? HttpRequestException
                    | :? Threading.Tasks.TaskCanceledException ->
                        receipts <- receipts @ [ { HttpStatus = None; Headers = Map.empty; ResponseSha256 = None; Error = Some "Native response unavailable; no retry" } ]
                        Error(Transport "V2 Observation native response unavailable; no retry")
                match compose binding readOnly nativeOnce with
                | Error error -> (reads :> IDisposable).Dispose(); http.Dispose(); Error error
                | Ok transport ->
                    let lifetime = { new IDisposable with member _.Dispose() = (reads :> IDisposable).Dispose(); http.Dispose() }
                    let observed =
                        { new IGitHubTransport with
                            member _.Send request = transport.Send request
                            member _.SendMutation intent = transport.SendMutation intent
                            member _.RetryMutation id = transport.RetryMutation id
                          interface IDispatchEvidence with
                            member _.DispatchReceipts = receipts }
                    Ok(observed, lifetime))
