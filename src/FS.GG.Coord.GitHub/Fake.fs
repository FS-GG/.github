namespace FS.GG.Coord.GitHub

module Fake =

    open System
    open System.Text.RegularExpressions
    open Errors
    open Transport

    type Route = Request -> IoResult<Response>

    /// `repos/{owner}/{repo}/…` → `owner/repo`, and whatever trailing numbers the path carries.
    ///
    /// The stub logs `<verb> <owner/repo> <number>`, and the CROSS-REPO assertions depend on it: #494 was
    /// an issue read addressed to the wrong repository, and the only way the corpus can see that is if the
    /// log names the repo the request was actually sent to.
    let private restParts (path: string) =
        let m = Regex.Match(path, @"^repos/([^/]+)/([^/]+)(?:/(.*))?$")

        if not m.Success then
            None
        else
            let nwo = m.Groups.[1].Value + "/" + m.Groups.[2].Value
            let rest = if m.Groups.[3].Success then m.Groups.[3].Value else ""
            Some(nwo, rest)

    /// The `gh project item-edit` line, synthesised from a Projects v2 field mutation.
    ///
    /// THE ONE PIECE OF DELIBERATE ARCHAEOLOGY IN THIS FILE. Under bash, a board write was
    /// `gh project item-edit --id … --project-id … --field-id … --single-select-option-id …`, and the
    /// corpus asserts on those exact flag strings. Under the port it is a GraphQL mutation and those bytes
    /// are gone. Re-emitting them here is not nostalgia: it is what keeps ~20 assertions — every one of
    /// which encodes a real defect about routing a value to the right field type — TRUE STATEMENTS about
    /// the new code instead of dead greps that pass over anything.
    ///
    /// An empty value is `--clear`, never `--text ""`. That is a real trap and a real bug: `item-edit
    /// --text ''` is a NO-OP ("no changes to make"), so an empty write silently left the old value in
    /// place, and the board went on showing a `Blocked by` that had been cleared.
    let private itemEditLine (document: string) (variables: (string * string) list) =
        let v name =
            variables |> List.tryFind (fun (k, _) -> k = name) |> Option.map snd

        let itemId = v "itemId" |> Option.defaultValue ""
        let projectId = v "projectId" |> Option.defaultValue ""
        let fieldId = v "fieldId" |> Option.defaultValue ""

        let valueFlag =
            if document.Contains "clearProjectV2ItemFieldValue" then
                "--clear"
            else
                match v "optionId", v "text", v "number", v "date", v "iterationId" with
                | Some o, _, _, _, _ -> $"--single-select-option-id %s{o}"
                | _, Some t, _, _, _ -> $"--text %s{t}"
                | _, _, Some n, _, _ -> $"--number %s{n}"
                | _, _, _, Some d, _ -> $"--date %s{d}"
                | _, _, _, _, Some i -> $"--iteration-id %s{i}"
                | _ -> "--clear"

        $"item-edit --id %s{itemId} --project-id %s{projectId} --field-id %s{fieldId} %s{valueFlag}"

    let describe (request: Request) =
        match request.Body with
        | Query(document, variables) ->
            // The stub discriminates GraphQL on markers INSIDE the document text, and so does this. The
            // aliased batch document is logged whole (`batch-mutation mutation { f0: … }`) because the
            // corpus asserts on its bytes: one document, aliases in order, `clear…` for an empty value.
            if document.Contains "f0:" || Regex.IsMatch(document, @"\bf\d+:") then
                "batch-mutation " + document
            elif
                document.Contains "updateProjectV2ItemFieldValue"
                || document.Contains "clearProjectV2ItemFieldValue"
            then
                itemEditLine document variables
            elif document.Contains "addProjectV2ItemById" then
                let url =
                    variables |> List.tryFind (fun (k, _) -> k = "contentId") |> Option.map snd |> Option.defaultValue ""

                $"item-add %s{url}"
            else
                "graphql " + request.Subject

        | _ ->

        let method = request.Method.ToUpperInvariant()

        match restParts request.Path with
        | None ->
            if request.Path.StartsWith "rate_limit" then "rate-limit" else $"%s{method.ToLowerInvariant()} %s{request.Path}"

        | Some(nwo, rest) ->

        // The verb names are the stub's, exactly. A log that renamed them would break every `grep -c
        // '^item-edit'` in the corpus while looking, to a reader, like it still worked.
        match rest with
        | r when Regex.IsMatch(r, @"^issues/\d+/comments$") ->
            let n = Regex.Match(r, @"^issues/(\d+)/comments$").Groups.[1].Value

            if method = "POST" then
                $"comment-post %s{nwo} %s{n}"
            else
                $"comment-list %s{nwo} %s{n}"

        | r when Regex.IsMatch(r, @"^issues/comments/\d+$") ->
            let id = Regex.Match(r, @"^issues/comments/(\d+)$").Groups.[1].Value

            match method with
            | "DELETE" -> $"comment-delete %s{nwo} %s{id}"
            | "PATCH" -> $"comment-patch %s{nwo} %s{id}"
            | m -> $"comment-%s{m.ToLowerInvariant()} %s{nwo} %s{id}"

        | r when Regex.IsMatch(r, @"^issues/\d+/assignees$") ->
            let n = Regex.Match(r, @"^issues/(\d+)/assignees$").Groups.[1].Value
            $"assignee-%s{method.ToLowerInvariant()} %s{nwo} %s{n}"

        | r when Regex.IsMatch(r, @"^issues/\d+/sub_issues$") ->
            let n = Regex.Match(r, @"^issues/(\d+)/sub_issues$").Groups.[1].Value

            if method = "POST" then
                // THE `-F` ASSERTION, RESTATED ONE LAYER DOWN. Under `gh`, `-f` sent the child's id as a
                // JSON *string* and collected a 422; `-F` sent it as a number. The corpus asserts the typed
                // form. Under HTTP the same defect is "the JSON body sends `sub_issue_id` as a NUMBER, not
                // a string", so the log carries the id and the test asserts the body's type.
                let id =
                    match request.Body with
                    | Json body ->
                        let m = Regex.Match(body, @"""sub_issue_id""\s*:\s*(\d+)")
                        if m.Success then m.Groups.[1].Value else ""
                    | _ -> ""

                $"sub-issue-add %s{nwo} %s{n} -F sub_issue_id=%s{id}"
            else
                $"sub-issue-%s{method.ToLowerInvariant()} %s{nwo} %s{n}"

        | r when Regex.IsMatch(r, @"^issues/\d+$") ->
            let n = Regex.Match(r, @"^issues/(\d+)$").Groups.[1].Value

            if method = "PATCH" then
                $"issue-patch %s{nwo} %s{n}"
            else
                $"issue-get %s{nwo} %s{n}"

        | "issues" ->
            // `paginate` and `inm` are the two facts the corpus reads off this line, and both are
            // correctness properties of the LOCK, not ergonomics. A claim scan that does not paginate
            // cannot see the markers past the first hundred issues; a claim scan sent with an
            // `If-None-Match` can be served a 304 from before the marker was posted, and would report
            // `comments: 0` over a live lock.
            let paginate = "1"

            let inm =
                match request.IfNoneMatch with
                | Some e -> e
                | None -> "none"

            $"issue-list %s{nwo} paginate=%s{paginate} inm=%s{inm}"

        | r when Regex.IsMatch(r, @"^pulls/\d+/files$") ->
            let n = Regex.Match(r, @"^pulls/(\d+)/files$").Groups.[1].Value
            $"pr-files %s{nwo} %s{n}"

        | r when Regex.IsMatch(r, @"^pulls/\d+$") ->
            let n = Regex.Match(r, @"^pulls/(\d+)$").Groups.[1].Value
            $"pr-get %s{nwo} %s{n}"

        | "pulls" -> $"pulls-list %s{nwo}"

        | r when Regex.IsMatch(r, @"^commits/[^/]+/check-runs$") ->
            let sha = Regex.Match(r, @"^commits/([^/]+)/check-runs$").Groups.[1].Value
            $"check-runs %s{nwo} %s{sha}"

        | "actions/runs" ->
            let sha =
                request.Query |> List.tryFind (fun (k, _) -> k = "head_sha") |> Option.map snd |> Option.defaultValue ""

            $"wf-runs %s{nwo} %s{sha}"

        | r -> $"%s{method.ToLowerInvariant()} %s{nwo} %s{r}"

    type Recorder(route: Route) =

        let log = ResizeArray<string>()
        let mutable graphQlCalls = 0
        let mutable restCalls = 0

        member _.GraphQlCalls = graphQlCalls
        member _.RestCalls = restCalls
        member _.Log = List.ofSeq log

        member _.Logged(needle: string) =
            log |> Seq.exists (fun l -> l.Contains needle)

        member _.Count(needle: string) =
            log |> Seq.filter (fun l -> l.Contains needle) |> Seq.length

        interface IGitHubTransport with
            member _.Send(request: Request) : IoResult<Response> =
                // COUNT FIRST, AND COUNT THE FAILURES TOO. The stub increments before it injects its 403,
                // and it is right to: a call that came back rate-limited is a call you made and were
                // charged for. Counting only successes would report a budget you did not spend, and the
                // one thing the meter exists to do is tell you what you spent.
                match request.Budget with
                | GraphQl -> graphQlCalls <- graphQlCalls + 1
                | Rest -> restCalls <- restCalls + 1
                // THE METER READ IS FREE, and the corpus depends on it being free. `GET /rate_limit` does
                // not consume the budget it reports — which is exactly what makes "back off until the
                // reset" a strategy and not a guess. Bill it here and `bootstrap: 2 GraphQL calls`, plus
                // every count delta the corpus asserts after it, shifts by one.
                | Free -> ()

                log.Add(describe request)
                route request
