namespace FS.GG.Coord.GitHub

module ContextViewReads =
    open System
    open System.Text
    open System.Text.Json
    open FS.GG.Coord.ContextView
    open Transport

    exception private ReadFailure of string

    // One accepted page. Issue bodies are at most 65536 characters; 1 MiB is generous and still bounded.
    let private pageLimit = 1048576
    let private perRequest = TimeSpan.FromSeconds 30.0

    let private truncated (text: string) = if text.Length > 200 then text.Substring(0, 200) + "..." else text

    let private fetch (transport: IDeadlineSinglePageGitHubTransport) (path: string) (query: (string * string) list) =
        let request =
            { Method = "GET"; Path = path; Query = query; Body = NoBody; Budget = Rest; IfNoneMatch = None
              Subject = "context-view" }
        match transport.SendSingleWithin(request, perRequest) with
        | Error error -> raise (ReadFailure(truncated (Errors.explain error)))
        | Ok response ->
            if response.Status <> 200 then raise (ReadFailure $"native read returned {response.Status}")
            if Encoding.UTF8.GetByteCount response.Body > pageLimit then raise (ReadFailure "native page exceeds the accepted 1 MiB bound")
            if Option.isSome response.NextLink then raise (ReadFailure "unexpected native continuation")
            let rec unique (node: JsonElement) =
                match node.ValueKind with
                | JsonValueKind.Object ->
                    let fields = node.EnumerateObject() |> Seq.toList
                    Set.count (fields |> List.map _.Name |> Set.ofList) = fields.Length && fields |> List.forall (fun f -> unique f.Value)
                | JsonValueKind.Array -> node.EnumerateArray() |> Seq.forall unique
                | _ -> true
            use document = JsonDocument.Parse(response.Body, JsonDocumentOptions(MaxDepth = 24))
            if not (unique document.RootElement) then raise (ReadFailure "duplicate native JSON fields")
            document.RootElement.Clone()

    let private property name (node: JsonElement) =
        if node.ValueKind <> JsonValueKind.Object then raise (ReadFailure "native object missing")
        match node.TryGetProperty(name: string) with
        | true, value -> value
        | _ -> raise (ReadFailure $"native field {name} missing")

    let private text name node =
        let value = property name node
        if value.ValueKind <> JsonValueKind.String then raise (ReadFailure $"native field {name} is not text")
        value.GetString()

    let private nullableText name node =
        let value = property name node
        match value.ValueKind with
        | JsonValueKind.Null -> ""
        | JsonValueKind.String -> value.GetString()
        | _ -> raise (ReadFailure $"native field {name} is not text")

    let private flag name node =
        match (property name node).ValueKind with
        | JsonValueKind.True -> true
        | JsonValueKind.False -> false
        | _ -> raise (ReadFailure $"native field {name} is not boolean")

    let private count name node =
        match (property name node).TryGetInt32() with
        | true, n when n >= 0 -> n
        | _ -> raise (ReadFailure $"native field {name} is not a count")

    let private guarded (read: unit -> 'a) =
        try Supplied(read ())
        with
        | ReadFailure reason -> Unavailable reason
        | :? JsonException -> Unavailable "malformed native JSON"
        | :? InvalidOperationException -> Unavailable "malformed native field kind"
        | :? FormatException -> Unavailable "malformed native value"

    let readIssue (transport: IDeadlineSinglePageGitHubTransport) (issue: IssueRef) : Read<IssueFact> =
        guarded (fun () ->
            let node = fetch transport $"repos/{issue.Repository}/issues/{issue.Number}" []
            if count "number" node <> issue.Number then raise (ReadFailure "native issue number differs from the request")
            match node.TryGetProperty "pull_request" with
            | true, _ -> raise (ReadFailure "the number names a pull request, not an owning issue")
            | _ -> ()
            { Title = text "title" node; State = text "state" node; UpdatedAt = text "updated_at" node; Body = nullableText "body" node })

    let private readChecks (transport: IDeadlineSinglePageGitHubTransport) (repository: string) (head: string) : Read<CheckPopulation> =
        guarded (fun () ->
            let node = fetch transport $"repos/{repository}/commits/{head}/check-runs" [ "per_page", "100" ]
            let total = count "total_count" node
            let runs = property "check_runs" node
            if runs.ValueKind <> JsonValueKind.Array then raise (ReadFailure "native check_runs missing")
            let checks =
                runs.EnumerateArray()
                |> Seq.map (fun run ->
                    let conclusion = property "conclusion" run
                    { Name = text "name" run
                      Status = text "status" run
                      Conclusion = (if conclusion.ValueKind = JsonValueKind.String then Some(conclusion.GetString()) else None) })
                |> List.ofSeq
            if checks.Length > 100 then raise (ReadFailure "native page exceeds the requested population")
            { Checks = checks; Complete = total <= checks.Length })

    let readPullRequest (transport: IDeadlineSinglePageGitHubTransport) (repository: string) (number: int) : Read<PullRequestFact> =
        guarded (fun () ->
            let node = fetch transport $"repos/{repository}/pulls/{number}" []
            if count "number" node <> number then raise (ReadFailure "native pull request number differs from the request")
            let head = property "head" node
            let headSha = text "sha" head
            { Number = number
              Url = text "html_url" node
              State = text "state" node
              Draft = flag "draft" node
              Merged = flag "merged" node
              Head = headSha
              HeadRef = text "ref" head
              BaseRef = property "base" node |> text "ref"
              ChangedFiles = count "changed_files" node
              Body = nullableText "body" node
              Checks = readChecks transport repository headSha })
