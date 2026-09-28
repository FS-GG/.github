namespace FS.GG.Coord.GitHub

open System
open System.Collections.Generic
open System.Security.Cryptography
open System.Text
open System.Text.Json
open FS.GG.Coord
open FS.GG.Coord.GitHub.Errors
open FS.GG.Coord.GitHub.Transport

module HistoricalLossApprovalRead =
    type RawResponse =
        {
            Resource: string
            Path: string
            Query: (string * string) list
            Status: int
            Body: string
            BodySha256: string
            NextLink: string option
        }

    type Capture =
        {
            BoundLoss: HistoricalLossRegistry.BoundLoss
            Readback: HistoricalLossRegistry.NativeApprovalReadbackV2
            FirstPass: RawResponse list
            SecondPass: RawResponse list
            Fingerprint: string
        }

    type V3CensusCapture =
        {
            First: HistoricalLossRegistry.RetainedNativeCensusV3
            Second: HistoricalLossRegistry.RetainedNativeCensusV3
            FirstPass: RawResponse list
            SecondPass: RawResponse list
        }

    type private Pass =
        {
            PullRequest: HistoricalLossRegistry.NativeMergedPullRequestV2
            Comments: HistoricalLossRegistry.NativeReviewComment list
            Envelope: HistoricalLossRegistry.NativeApprovalEnvelopeCommentV2
            File: HistoricalLossRegistry.NativeFileReadback
            Blob: HistoricalLossRegistry.NativeBlobReadback
            Raw: RawResponse list
        }

    let private shaBytes (bytes: byte array) =
        bytes |> SHA256.HashData |> Convert.ToHexString |> _.ToLowerInvariant()

    let private shaText (value: string) = value |> Encoding.UTF8.GetBytes |> shaBytes

    let validateV3CensusCapture
        (expectedRepositories: HistoricalLossRegistry.RepositoryIdentityV3 list)
        (capture: V3CensusCapture)
        : IoResult<HistoricalLossRegistry.RetainedNativeCensusV3> =
        let subject = "historical-loss v3 retained native census"
        let rawStable =
            capture.FirstPass.Length = capture.SecondPass.Length
            && List.forall2
                (fun first second ->
                    first.Path = second.Path
                    && first.Resource = second.Resource
                    && first.Query = second.Query
                    && first.Status = second.Status
                    && first.Body = second.Body
                    && first.BodySha256 = second.BodySha256
                    && first.NextLink = second.NextLink)
                capture.FirstPass
                capture.SecondPass

        let pageBound
            (census: HistoricalLossRegistry.RetainedNativeCensusV3)
            (raw: RawResponse list)
            =
            let endpoint (page: HistoricalLossRegistry.RetainedPageV3) =
                match page.Repository.FullName.Split('/', 2) with
                | [| owner; repository |] ->
                    Some(
                        $"retained-subjects:%s{page.Repository.FullName}:%d{page.Index}",
                        $"repos/%s{owner}/%s{repository}/issues/comments",
                        [ "sort", "created"; "direction", "asc"; "per_page", "100"; "page", string page.Index ]
                    )
                | _ -> None

            not census.Pages.IsEmpty
            && census.Pages.Length = raw.Length
            && List.forall2
                (fun (page: HistoricalLossRegistry.RetainedPageV3) (response: RawResponse) ->
                    match endpoint page with
                    | None -> false
                    | Some(resource, path, query) ->
                        response.Resource = resource
                        && response.Path = path
                        && response.Query = query
                        && response.Status = 200
                        && response.BodySha256 = shaText response.Body
                        && response.BodySha256 = page.RawSha256
                        && response.NextLink.IsNone = page.Terminal)
                census.Pages
                raw

        let pagesComplete (census: HistoricalLossRegistry.RetainedNativeCensusV3) =
            census.SelectedRepositories
            |> List.forall (fun repository ->
                let pages = census.Pages |> List.filter (fun page -> page.Repository = repository)
                not pages.IsEmpty
                && pages
                   |> List.mapi (fun index page ->
                       page.Index = index + 1 && page.Terminal = (index = pages.Length - 1))
                   |> List.forall id)
            && (census.Pages
                |> List.forall (fun page -> List.contains page.Repository census.SelectedRepositories))

        if capture.First.Enumeration <> HistoricalLossRegistry.DirectRepositoryEnumeration then
            Error(Malformed(subject, "search or audit-absence census cannot establish retained population completeness"))
        elif capture.First <> capture.Second || not rawStable then
            Error(Malformed(subject, "retained census drifted between complete passes"))
        elif capture.First.SelectedRepositories <> expectedRepositories then
            Error(Malformed(subject, "retained census repository roster differs from the frozen scope"))
        elif capture.FirstPass.IsEmpty
             || not (pagesComplete capture.First)
             || not (pagesComplete capture.Second)
             || not (pageBound capture.First capture.FirstPass)
             || not (pageBound capture.Second capture.SecondPass) then
            Error(Malformed(subject, "retained census raw pages are incomplete, nonterminal, or unbound"))
        else
            Error(Malformed(subject, "native raw-to-typed census proof is unavailable"))

    let private text (node: JsonElement) (name: string) =
        match node.TryGetProperty name with
        | true, value when value.ValueKind = JsonValueKind.String -> Some(value.GetString())
        | _ -> None

    let private integer64 (node: JsonElement) (name: string) =
        match node.TryGetProperty name with
        | true, value when value.ValueKind = JsonValueKind.Number ->
            match value.TryGetInt64() with
            | true, result -> Some result
            | _ -> None
        | _ -> None

    let private boolean (node: JsonElement) (name: string) =
        match node.TryGetProperty name with
        | true, value when value.ValueKind = JsonValueKind.True -> Some true
        | true, value when value.ValueKind = JsonValueKind.False -> Some false
        | _ -> None

    let private child (node: JsonElement) (name: string) =
        match node.TryGetProperty name with
        | true, value when value.ValueKind = JsonValueKind.Object -> Some value
        | _ -> None

    let private parse (subject: string) (body: string) =
        try
            Ok(JsonDocument.Parse body)
        with :? JsonException as error ->
            Error(Malformed(subject, error.Message))

    let private decodeBase64 (subject: string) (value: string) =
        try
            value.Replace("\n", "").Replace("\r", "") |> Convert.FromBase64String |> Ok
        with :? FormatException as error ->
            Error(Malformed(subject, error.Message))

    let private escapePath (path: string) =
        path.Split('/', StringSplitOptions.RemoveEmptyEntries)
        |> Array.map Uri.EscapeDataString
        |> String.concat "/"

    let collectAndBind
        (transport: ISinglePageGitHubTransport)
        (apiBase: string)
        (owner: string)
        (repositoryName: string)
        (expectedFamily: string)
        (expectedScope: string)
        (expectedCutoff: string)
        (registryBytes: byte array)
        (entry: HistoricalLossRegistry.EntryV2)
        =
        let repository = $"%s{owner}/%s{repositoryName}"
        let prNumber = entry.Approval.PullRequest
        let subject = $"%s{repository} historical-loss approval PR #%d{prNumber}"
        let baseUri = Uri(apiBase.TrimEnd('/') + "/")
        let commentsPath = $"repos/%s{owner}/%s{repositoryName}/issues/%d{prNumber}/comments"
        let mutable calls = 0

        let send (resource: string) (path: string) (query: (string * string) list) =
            if calls >= 208 then
                Error(Malformed(subject, "historical-loss approval read exceeded its request bound"))
            else
                calls <- calls + 1

                transport.SendSingle
                    {
                        Method = "GET"
                        Path = path
                        Query = query
                        Body = NoBody
                        Budget = Rest
                        IfNoneMatch = None
                        Subject = subject + " " + resource
                    }
                |> Result.bind (fun response ->
                    match response.Status with
                    | 200 ->
                        Ok(
                            response,
                            {
                                Resource = resource
                                Path = path
                                Query = query
                                Status = response.Status
                                Body = response.Body
                                BodySha256 = shaText response.Body
                                NextLink = response.NextLink
                            }
                        )
                    | 401
                    | 403 -> Error(Unauthorized(subject + " " + resource))
                    | 404 -> Error(NotFound(subject + " " + resource))
                    | status -> Error(Http(status, response.Body)))

        let continuation (expectedPage: int) (link: string) =
            try
                let uri = Uri link
                let expectedPath = "/" + commentsPath
                let pairs =
                    uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
                    |> Array.map (fun pair ->
                        let parts = pair.Split('=', 2)
                        Uri.UnescapeDataString parts[0], Uri.UnescapeDataString(if parts.Length = 2 then parts[1] else ""))
                    |> Array.toList

                if uri.Scheme <> baseUri.Scheme || uri.Host <> baseUri.Host || uri.Port <> baseUri.Port then
                    Error(Malformed(subject, "comment continuation escaped the configured API origin"))
                elif uri.AbsolutePath <> expectedPath then
                    Error(Malformed(subject, "comment continuation escaped the exact pull request population"))
                elif (pairs |> List.sort) <> ([ "direction", "asc"; "page", string expectedPage; "per_page", "100"; "sort", "created" ] |> List.sort) then
                    Error(Malformed(subject, "comment continuation query is incomplete or ambiguous"))
                else
                    Ok(uri.AbsolutePath.TrimStart('/'), pairs)
            with :? UriFormatException ->
                Error(Malformed(subject, "comment continuation is not an absolute URI"))

        let readPull (pass: int) =
            let path = $"repos/%s{owner}/%s{repositoryName}/pulls/%d{prNumber}"
            send ($"pass-%d{pass}:pull") path []
            |> Result.bind (fun (_, raw) ->
                parse subject raw.Body
                |> Result.bind (fun document ->
                    use document = document
                    let root = document.RootElement
                    let baseNode = child root "base"
                    let headNode = child root "head"
                    let baseRepository = baseNode |> Option.bind (fun value -> child value "repo") |> Option.bind (fun value -> text value "full_name")

                    match integer64 root "number", text root "state", boolean root "merged", baseNode |> Option.bind (fun value -> text value "sha"), headNode |> Option.bind (fun value -> text value "sha"), text root "merge_commit_sha", text root "merged_at", baseRepository with
                    | Some number, Some state, Some merged, Some baseSha, Some headSha, Some mergeSha, Some mergedAt, Some baseRepo
                        when number = int64 prNumber && baseRepo = repository ->
                        let nativePull: HistoricalLossRegistry.NativePullRequest =
                            {
                                Repository = repository
                                PullRequest = prNumber
                                State = state
                                Merged = merged
                                BaseSha = baseSha
                                HeadSha = headSha
                                MergeCommitSha = mergeSha
                            }

                        let mergedPull: HistoricalLossRegistry.NativeMergedPullRequestV2 =
                            { PullRequest = nativePull; MergedAt = mergedAt }

                        Ok(mergedPull, raw)
                    | _ -> Error(Malformed(subject, "pull request identity or required merge fields are absent"))))

        let readComments (pass: int) =
            let comments = ResizeArray<HistoricalLossRegistry.NativeReviewComment>()
            let envelopes = ResizeArray<HistoricalLossRegistry.NativeApprovalEnvelopeCommentV2>()
            let rawPages = ResizeArray<RawResponse>()
            let identities = HashSet<int64>()
            let seenLinks = HashSet<string>(StringComparer.Ordinal)
            let marker = "<!-- fsgg:historical-loss-approval/v2 -->\n"

            let rec page (index: int) (path: string) (query: (string * string) list) =
                if index > 100 then
                    Error(Malformed(subject, "comment population exceeds 100 pages"))
                else
                    send ($"pass-%d{pass}:comments:%d{index}") path query
                    |> Result.bind (fun (response, raw) ->
                        rawPages.Add raw
                        parse subject response.Body
                        |> Result.bind (fun document ->
                            use document = document
                            let root = document.RootElement

                            if root.ValueKind <> JsonValueKind.Array || root.GetArrayLength() > 100 then
                                Error(Malformed(subject, "comment page is not a bounded JSON array"))
                            else
                                let mutable problem: IoError option = None

                                for item in root.EnumerateArray() do
                                    match integer64 item "id", text item "node_id", text item "html_url", text item "created_at", text item "body" with
                                    | Some id, Some nodeId, Some url, Some createdAt, Some body when id > 0L && identities.Add id ->
                                        comments.Add
                                            {
                                                DatabaseId = id
                                                NodeId = nodeId
                                                Url = url
                                                Body = body
                                                BodySha256 = shaText body
                                            }

                                        if body.StartsWith(marker, StringComparison.Ordinal) then
                                            envelopes.Add
                                                {
                                                    DatabaseId = id
                                                    NodeId = nodeId
                                                    Url = url
                                                    CreatedAt = createdAt
                                                    Body = body
                                                    BodySha256 = shaText body
                                                }
                                        elif body.Contains("fsgg:historical-loss-approval", StringComparison.Ordinal) then
                                            problem <- Some(Malformed(subject, "unknown historical-loss approval envelope marker"))
                                    | _ -> problem <- Some(Malformed(subject, "comment identity/body is missing or duplicated"))

                                match problem with
                                | Some error -> Error error
                                | None ->
                                    match response.NextLink with
                                    | None -> Ok()
                                    | Some link when not (seenLinks.Add link) -> Error(Malformed(subject, "comment continuation loop"))
                                    | Some link -> continuation (index + 1) link |> Result.bind (fun (nextPath, nextQuery) -> page (index + 1) nextPath nextQuery)))

            page 1 commentsPath [ "sort", "created"; "direction", "asc"; "per_page", "100"; "page", "1" ]
            |> Result.bind (fun () ->
                if envelopes.Count <> 1 then
                    Error(Malformed(subject, $"expected exactly one v2 approval envelope, found %d{envelopes.Count}"))
                else
                    Ok(List.ofSeq comments, envelopes[0], List.ofSeq rawPages))

        let readFile (pass: int) (mergeSha: string) =
            let path = $"repos/%s{owner}/%s{repositoryName}/contents/%s{escapePath entry.Approval.RegistryPath}"
            send ($"pass-%d{pass}:contents") path [ "ref", mergeSha ]
            |> Result.bind (fun (_, raw) ->
                parse subject raw.Body
                |> Result.bind (fun document ->
                    use document = document
                    let root = document.RootElement

                    match text root "type", text root "path", text root "sha", text root "encoding", text root "content", integer64 root "size" with
                    | Some "file", Some filePath, Some blobSha, Some "base64", Some encoded, Some size when filePath = entry.Approval.RegistryPath ->
                        decodeBase64 subject encoded
                        |> Result.bind (fun bytes ->
                            if int64 bytes.LongLength <> size then
                                Error(Malformed(subject, "contents byte count does not match provider size"))
                            else
                                let file: HistoricalLossRegistry.NativeFileReadback =
                                    {
                                        Repository = repository
                                        Path = filePath
                                        Revision = mergeSha
                                        BlobSha = blobSha
                                        Bytes = bytes
                                        BytesSha256 = shaBytes bytes
                                    }

                                Ok(file, raw))
                    | _ -> Error(Malformed(subject, "contents response is not the exact base64 file"))))

        let readBlob (pass: int) (blobSha: string) =
            let path = $"repos/%s{owner}/%s{repositoryName}/git/blobs/%s{blobSha}"
            send ($"pass-%d{pass}:blob") path []
            |> Result.bind (fun (_, raw) ->
                parse subject raw.Body
                |> Result.bind (fun document ->
                    use document = document
                    let root = document.RootElement

                    match text root "sha", text root "encoding", text root "content", integer64 root "size" with
                    | Some actualSha, Some "base64", Some encoded, Some size when actualSha = blobSha ->
                        decodeBase64 subject encoded
                        |> Result.bind (fun bytes ->
                            if int64 bytes.LongLength <> size then
                                Error(Malformed(subject, "blob byte count does not match provider size"))
                            else
                                let blob: HistoricalLossRegistry.NativeBlobReadback =
                                    {
                                        Repository = repository
                                        BlobSha = blobSha
                                        Bytes = bytes
                                        BytesSha256 = shaBytes bytes
                                    }

                                Ok(blob, raw))
                    | _ -> Error(Malformed(subject, "blob response identity or encoding is invalid"))))

        let readPass (pass: int) =
            readPull pass
            |> Result.bind (fun (pull, pullRaw) ->
                readComments pass
                |> Result.bind (fun (comments, envelope, commentRaw) ->
                    readFile pass pull.PullRequest.MergeCommitSha
                    |> Result.bind (fun (file, fileRaw) ->
                        readBlob pass file.BlobSha
                        |> Result.map (fun (blob, blobRaw) ->
                            ({
                                PullRequest = pull
                                Comments = comments
                                Envelope = envelope
                                File = file
                                Blob = blob
                                Raw = pullRaw :: commentRaw @ [ fileRaw; blobRaw ]
                            }: Pass)))))

        let stablePasses (first: Pass) (second: Pass) =
            let rawStable =
                List.length first.Raw = List.length second.Raw
                && List.forall2
                    (fun left right ->
                        left.Path = right.Path
                        && left.Query = right.Query
                        && left.Status = right.Status
                        && left.Body = right.Body
                        && left.NextLink = right.NextLink)
                    first.Raw
                    second.Raw

            first.PullRequest = second.PullRequest
            && first.Comments = second.Comments
            && first.Envelope = second.Envelope
            && first.File = second.File
            && first.Blob = second.Blob
            && rawStable

        if owner <> "FS-GG" || repositoryName <> ".github" || prNumber <= 0 || String.IsNullOrWhiteSpace entry.Approval.RegistryPath then
            Error(Malformed(subject, "v2 approval collector is bound to one valid FS-GG/.github approval"))
        else
            readPass 1
            |> Result.bind (fun first ->
                readPass 2
                |> Result.bind (fun second ->
                    if not (stablePasses first second) then
                        Error(Malformed(subject, "native approval evidence drifted between complete passes"))
                    else
                        let readback: HistoricalLossRegistry.NativeApprovalReadbackV2 =
                            {
                                PullRequestFirst = first.PullRequest
                                PullRequestSecond = second.PullRequest
                                ReviewCommentsFirst = first.Comments
                                ReviewCommentsSecond = second.Comments
                                ReviewCommentsComplete = true
                                ReviewCommentsTerminal = true
                                ApprovalEnvelopeFirst = first.Envelope
                                ApprovalEnvelopeSecond = second.Envelope
                                File = first.File
                                Blob = first.Blob
                            }

                        HistoricalLossRegistry.bindV2 expectedFamily expectedScope expectedCutoff registryBytes entry readback
                        |> Result.mapError (fun reasons -> Malformed(subject, String.concat "; " reasons))
                        |> Result.map (fun bound ->
                            let fingerprintMaterial =
                                first.Raw
                                |> List.collect (fun raw ->
                                    [ raw.Resource; raw.Path; sprintf "%A" raw.Query; raw.BodySha256; defaultArg raw.NextLink "terminal" ])
                                |> String.concat "\n"

                            {
                                BoundLoss = bound
                                Readback = readback
                                FirstPass = first.Raw
                                SecondPass = second.Raw
                                Fingerprint = shaText (repository + "\n" + fingerprintMaterial)
                            })))
