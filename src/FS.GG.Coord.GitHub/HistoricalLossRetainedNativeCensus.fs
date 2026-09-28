namespace FS.GG.Coord.GitHub

module HistoricalLossRetainedNativeCensus =

    open System
    open System.Collections.Generic
    open System.Globalization
    open System.IO
    open System.Runtime.InteropServices
    open Microsoft.Win32.SafeHandles
    open System.Security.Cryptography
    open System.Text
    open System.Text.Json
    open System.Text.RegularExpressions
    open Errors
    open Transport

    [<Literal>]
    let private ApiVersion = "2026-03-10"

    type RepositoryIdentity =
        { FullName: string
          DatabaseId: int64
          NodeId: string }

    type Stream =
        | Identity
        | Issues
        | Pulls
        | IssueComments
        | IssueEvents
        | Timeline of issueNumber: int

    type RawPage =
        { Pass: int
          Repository: RepositoryIdentity
          Stream: Stream
          Index: int
          Method: string
          ApiVersionRequested: string
          ApiVersionSelected: string
          Path: string
          Query: (string * string) list
          Status: int
          Resource: string
          Body: string
          RawSha256: string
          LinkHeader: string option
          ObservedAt: string
          NextLink: string option
          ItemCount: int
          Terminal: bool }

    type Family =
        | DeliveryReceipt
        | IntakeReceipt
        | LegacyDoneReceipt

    type Origin =
        | IssueBody
        | IssueComment

    type DraftSubject =
        { Repository: RepositoryIdentity
          SubjectNumber: int
          SubjectIsPullRequest: bool
          NativeId: string
          Family: Family
          Origin: Origin
          CreatedAt: string
          PayloadSha256: string
          PayloadBlobSha: string
          SessionOperationId: string option }

    type UntrustedDraft =
        { ObservationHorizon: string
          Repositories: RepositoryIdentity list
          Subjects: DraftSubject list
          EvidenceFingerprint: string
          EligibleInventoryDigest: string }

    type PassCapture =
        { Number: int
          Pages: RawPage list
          Draft: UntrustedDraft
          RawEvidenceDigest: string }

    type Capture =
        { First: PassCapture
          Second: PassCapture }

    type PrivateCaptureSummary =
        { ObservationHorizon: string
          FirstPageCount: int
          FirstRawEvidenceDigest: string
          SecondPageCount: int
          SecondRawEvidenceDigest: string }

    type CollectorError =
        | TransportFailure of subject: string * detail: string
        | Unauthorized of subject: string
        | Forbidden of subject: string
        | NotFound of subject: string
        | ProviderStatus of subject: string * status: int
        | InvalidResponse of subject: string * detail: string
        | UnsafeContinuation of subject: string * detail: string
        | RosterDrift of detail: string
        | PassDrift of detail: string
        | DuplicateNativeId of nativeId: string
        | MalformedCandidate of subject: string * detail: string

    // Frozen from the accepted GS2-06.8 baseline. Names are deliberately not discovered from search,
    // organization enumeration, or a caller-supplied list.
    let repositories =
        [ { FullName = "FS-GG/.github"; DatabaseId = 1269292704L; NodeId = "R_kgDOS6feoA" }
          { FullName = "FS-GG/FS.GG.Audio"; DatabaseId = 1292226968L; NodeId = "R_kgDOTQXRmA" }
          { FullName = "FS-GG/FS.GG.Coordination"; DatabaseId = 1346720714L; NodeId = "R_kgDOUEVTyg" }
          { FullName = "FS-GG/FS.GG.Game"; DatabaseId = 1290990429L; NodeId = "R_kgDOTPLzXQ" }
          { FullName = "FS-GG/FS.GG.Governance"; DatabaseId = 1273065119L; NodeId = "R_kgDOS-Funw" }
          { FullName = "FS-GG/FS.GG.Net"; DatabaseId = 1305845505L; NodeId = "R_kgDOTdWfAQ" }
          { FullName = "FS-GG/FS.GG.Rendering"; DatabaseId = 1269292235L; NodeId = "R_kgDOS6fcyw" }
          { FullName = "FS-GG/FS.GG.SDD"; DatabaseId = 1274272672L; NodeId = "R_kgDOS_PboA" }
          { FullName = "FS-GG/FS.GG.Templates"; DatabaseId = 1281961814L; NodeId = "R_kgDOTGkvVg" } ]

    let private sha256 (value: string) =
        value
        |> Encoding.UTF8.GetBytes
        |> SHA256.HashData
        |> Convert.ToHexString
        |> _.ToLowerInvariant()

    let private blobSha (value: string) =
        let bytes = Encoding.UTF8.GetBytes value
        Array.append (Encoding.ASCII.GetBytes($"blob {bytes.LongLength}\u0000")) bytes
        |> SHA1.HashData
        |> Convert.ToHexString
        |> _.ToLowerInvariant()

    let private frame (value: string) = $"%d{Encoding.UTF8.GetByteCount value}:%s{value}"

    let private streamName =
        function
        | Identity -> "identity"
        | Issues -> "issues"
        | Pulls -> "pulls"
        | IssueComments -> "issue-comments"
        | IssueEvents -> "issue-events"
        | Timeline number -> $"timeline:%d{number}"

    let private parseInstant (subject: string) (value: string) =
        let mutable parsed = DateTimeOffset.MinValue

        if DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, &parsed) then
            Ok parsed
        else
            Error(InvalidResponse(subject, $"timestamp '%s{value}' is not an exact round-trip instant"))

    let private requestSubject (repository: RepositoryIdentity) (stream: Stream) (page: int) =
        $"%s{repository.FullName} %s{streamName stream} page %d{page}"

    let private ioDetail (error: IoError) = Errors.explain error

    let private validLinkHeader (value: string option) (next: string option) =
        match value with
        | None -> next.IsNone
        | Some raw ->
            let parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries) |> Array.map _.Trim()
            let pattern = Regex(@"\A<(?<url>https?://[^<>\s]+)>;\s*rel=""(?<rel>next|prev|first|last)""\z", RegexOptions.CultureInvariant)
            let matches = parts |> Array.map pattern.Match
            if parts.Length = 0 || matches |> Array.exists (fun item -> not item.Success) then false
            else
                let nextValues =
                    matches |> Array.choose (fun item -> if item.Groups.["rel"].Value = "next" then Some item.Groups.["url"].Value else None)
                match next, nextValues with
                | None, [||] -> true
                | Some expected, [| observed |] -> expected = observed
                | _ -> false

    let private send (transport: IVersionedSinglePageGitHubTransport) (repository: RepositoryIdentity) (stream: Stream) (index: int) (path: string) (query: (string * string) list) =
        let subject = requestSubject repository stream index
        let request =
            { Method = "GET"
              Path = path
              Query = query
              Body = NoBody
              Budget = Rest
              IfNoneMatch = None
              Subject = subject }

        match transport.SendSingleVersioned(ApiVersion, request) with
        | Error(Errors.NotFound _) -> Error(NotFound subject)
        | Error(Errors.Unauthorized _) -> Error(Unauthorized subject)
        | Error(Errors.RateLimited _) -> Error(Forbidden subject)
        | Error error -> Error(TransportFailure(subject, ioDetail error))
        | Ok response ->
            match response.Status with
            | 401 -> Error(Unauthorized subject)
            | 403 -> Error(Forbidden subject)
            | 404 -> Error(NotFound subject)
            | status when status <> 200 -> Error(ProviderStatus(subject, status))
            | _ when not (validLinkHeader (header "Link" response) response.NextLink) ->
                Error(UnsafeContinuation(subject, "Link header is missing, malformed, or disagrees with parsed next link"))
            | _ when header "X-GitHub-Api-Version-Selected" response <> Some ApiVersion ->
                Error(InvalidResponse(subject, "selected GitHub API version is missing or differs from the pinned request version"))
            | _ ->
                match header "X-RateLimit-Resource" response with
                | None -> Error(InvalidResponse(subject, "X-RateLimit-Resource is missing"))
                | Some resource when String.IsNullOrWhiteSpace resource ->
                    Error(InvalidResponse(subject, "X-RateLimit-Resource is empty"))
                | Some resource -> Ok(request, response, resource)

    let private requiredString (subject: string) (name: string) (value: JsonElement) =
        match value.TryGetProperty name with
        | true, item when item.ValueKind = JsonValueKind.String && not (String.IsNullOrWhiteSpace(item.GetString())) ->
            Ok(item.GetString())
        | _ -> Error(InvalidResponse(subject, $"%s{name} must be a non-empty string"))

    let private requiredInt (subject: string) (name: string) (value: JsonElement) =
        match value.TryGetProperty name with
        | true, item when item.ValueKind = JsonValueKind.Number ->
            let mutable parsed = 0
            if item.TryGetInt32(&parsed) then Ok parsed else Error(InvalidResponse(subject, $"%s{name} must be an integer"))
        | _ -> Error(InvalidResponse(subject, $"%s{name} must be an integer"))

    let private requiredInt64 (subject: string) (name: string) (value: JsonElement) =
        match value.TryGetProperty name with
        | true, item when item.ValueKind = JsonValueKind.Number ->
            let mutable parsed = 0L
            if item.TryGetInt64(&parsed) then Ok parsed else Error(InvalidResponse(subject, $"%s{name} must be an integer"))
        | _ -> Error(InvalidResponse(subject, $"%s{name} must be an integer"))

    let private optionalBody (subject: string) (value: JsonElement) =
        match value.TryGetProperty "body" with
        | true, item when item.ValueKind = JsonValueKind.String -> Ok(item.GetString())
        | true, item when item.ValueKind = JsonValueKind.Null -> Ok ""
        | _ -> Error(InvalidResponse(subject, "body must be a string or null"))

    let private parseArray (subject: string) (body: string) =
        try
            use document = JsonDocument.Parse body
            if document.RootElement.ValueKind <> JsonValueKind.Array then
                Error(InvalidResponse(subject, "response body must be a JSON array"))
            else
                Ok(document.RootElement.EnumerateArray() |> Seq.map _.Clone() |> Seq.toList)
        with :? JsonException as error ->
            Error(InvalidResponse(subject, $"invalid JSON: %s{error.Message}"))

    let private queryValue (key: string) (query: (string * string) list) = query |> List.tryPick (fun (name, value) -> if name = key then Some value else None)

    let private continuation (apiBase: string) (repository: RepositoryIdentity) (stream: Stream) (currentIndex: int) (currentPath: string) (currentQuery: (string * string) list) (nextLink: string) =
        let subject = requestSubject repository stream currentIndex
        let baseUri = Uri(apiBase.TrimEnd('/') + "/", UriKind.Absolute)
        let expectedPage = currentIndex + 1

        match Uri.TryCreate(nextLink, UriKind.Absolute) with
        | false, _ -> Error(UnsafeContinuation(subject, "next link is not absolute"))
        | true, uri when uri.Scheme <> baseUri.Scheme || uri.Authority <> baseUri.Authority ->
            Error(UnsafeContinuation(subject, "next link escaped the configured API origin"))
        | true, uri ->
            let expectedPath = "/" + currentPath.TrimStart('/')
            if uri.AbsolutePath <> expectedPath then
                Error(UnsafeContinuation(subject, "next link changed the endpoint path"))
            else
                let pairs =
                    uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
                    |> Array.map (fun pair ->
                        let parts = pair.Split('=', 2)
                        Uri.UnescapeDataString parts.[0], Uri.UnescapeDataString(if parts.Length = 2 then parts.[1] else ""))
                    |> Array.toList

                let expected =
                    currentQuery
                    |> List.map (fun (key, value) -> if key = "page" then key, string expectedPage else key, value)

                if pairs <> expected || queryValue "page" pairs <> Some(string expectedPage) then
                    Error(UnsafeContinuation(subject, "next link changed the query or skipped a page"))
                else
                    Ok(currentPath, pairs)

    let private rowId subject (row: JsonElement) =
        match row.TryGetProperty "id" with
        | true, value when value.ValueKind = JsonValueKind.Number -> Ok(value.GetRawText())
        | true, value when value.ValueKind = JsonValueKind.String && not (String.IsNullOrWhiteSpace(value.GetString())) -> Ok(value.GetString())
        | _ -> Error(InvalidResponse(subject, "native row id is missing or invalid"))

    let private collectArrayStream (transport: IVersionedSinglePageGitHubTransport) (apiBase: string) (pass: int) (repository: RepositoryIdentity) (stream: Stream) (path: string) (baseQuery: (string * string) list) =
        let rec loop index path query seen resourceSeen acc =
            let subject = requestSubject repository stream index
            if index > 10000 then
                Error(InvalidResponse(subject, "pagination did not terminate within 10000 pages"))
            else
              send transport repository stream index path query
              |> Result.bind (fun (request, response, resource) ->
                if resourceSeen |> Option.exists ((<>) resource) then
                    Error(InvalidResponse(subject, "X-RateLimit-Resource changed during the stream"))
                else
                parseArray subject response.Body
                |> Result.bind (fun rows ->
                    let ids = rows |> List.map (rowId subject)
                    let idError = ids |> List.tryPick (function Error error -> Some error | Ok _ -> None)
                    let pageIds = ids |> List.choose (function Ok value -> Some value | Error _ -> None)
                    let duplicate = pageIds |> List.tryFind (fun id -> Set.contains id seen)
                    let duplicateInPage = pageIds |> List.countBy id |> List.tryFind (fun (_, count) -> count > 1) |> Option.map fst
                    let count = rows.Length
                    let explicitNext = response.NextLink

                    let next =
                        match explicitNext with
                        | Some link -> continuation apiBase repository stream index request.Path request.Query link |> Result.map Some
                        | None when count = 100 ->
                            Ok(Some(request.Path, request.Query |> List.map (fun (key, value) -> if key = "page" then key, string (index + 1) else key, value)))
                        | None -> Ok None

                    match idError, duplicate |> Option.orElse duplicateInPage with
                    | Some error, _ -> Error error
                    | None, Some id -> Error(DuplicateNativeId($"%s{repository.FullName}/%s{streamName stream}/%s{id}"))
                    | None, None ->
                      next
                      |> Result.bind (fun nextRequest ->
                        let page =
                            { Pass = pass
                              Repository = repository
                              Stream = stream
                              Index = index
                              Method = request.Method
                              ApiVersionRequested = ApiVersion
                              ApiVersionSelected = (header "X-GitHub-Api-Version-Selected" response).Value
                              Path = request.Path
                              Query = request.Query
                              Status = response.Status
                              Resource = resource
                              Body = response.Body
                              RawSha256 = sha256 response.Body
                              LinkHeader = header "Link" response
                              ObservedAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
                              NextLink = explicitNext
                              ItemCount = count
                              Terminal = nextRequest.IsNone }

                        match nextRequest with
                        | None -> Ok(List.rev (page :: acc))
                        | Some(nextPath, nextQuery) -> loop (index + 1) nextPath nextQuery (Set.union seen (Set.ofList pageIds)) (Some resource) (page :: acc))))

        loop 1 path (baseQuery @ [ "per_page", "100"; "page", "1" ]) Set.empty None []

    let private collectIdentity (transport: IVersionedSinglePageGitHubTransport) (pass: int) (expected: RepositoryIdentity) =
        let owner, repo =
            let parts = expected.FullName.Split('/', 2)
            parts.[0], parts.[1]
        let path = $"repos/%s{owner}/%s{repo}"
        let subject = requestSubject expected Identity 1

        send transport expected Identity 1 path []
        |> Result.bind (fun (request, response, resource) ->
            try
                use document = JsonDocument.Parse response.Body
                let root = document.RootElement

                match requiredString subject "full_name" root, requiredInt64 subject "id" root, requiredString subject "node_id" root with
                | Ok fullName, Ok databaseId, Ok nodeId ->
                    let observed = { FullName = fullName; DatabaseId = databaseId; NodeId = nodeId }
                    if observed <> expected then
                        Error(RosterDrift($"%s{expected.FullName} resolved as %A{observed}"))
                    elif response.NextLink.IsSome then
                        Error(UnsafeContinuation(subject, "repository identity unexpectedly paginated"))
                    else
                        Ok
                            { Pass = pass
                              Repository = expected
                              Stream = Identity
                              Index = 1
                              Method = request.Method
                              ApiVersionRequested = ApiVersion
                              ApiVersionSelected = (header "X-GitHub-Api-Version-Selected" response).Value
                              Path = request.Path
                              Query = request.Query
                              Status = response.Status
                              Resource = resource
                              Body = response.Body
                              RawSha256 = sha256 response.Body
                              LinkHeader = header "Link" response
                              ObservedAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
                              NextLink = None
                              ItemCount = 1
                              Terminal = true }
                | Error error, _, _
                | _, Error error, _
                | _, _, Error error -> Error error
            with :? JsonException as error ->
                Error(InvalidResponse(subject, $"invalid JSON: %s{error.Message}")))

    type private NativeSubject =
        { Number: int
          NodeId: string
          CreatedAt: string
          Body: string
          IsPullRequest: bool }

    type private NativeComment =
        { NodeId: string
          CreatedAt: string
          Body: string
          IssueUrl: string }

    let private rows (pages: RawPage list) =
        pages
        |> List.collect (fun page ->
            match parseArray (requestSubject page.Repository page.Stream page.Index) page.Body with
            | Ok values -> values
            | Error _ -> failwith "collector retained a page it could not parse")

    let private distinctNativeIds getId subject rows =
        let duplicate = rows |> List.countBy getId |> List.tryFind (fun (_, count) -> count > 1)
        match duplicate with
        | Some(id, _) -> Error(DuplicateNativeId id)
        | None -> Ok rows

    let private parseSubjects (repository: RepositoryIdentity) (pages: RawPage list) =
        let subject = $"%s{repository.FullName} issue roster"
        let parsed =
            rows pages
            |> List.fold (fun state row ->
                state
                |> Result.bind (fun values ->
                    match requiredInt subject "number" row, requiredString subject "node_id" row, requiredString subject "created_at" row, optionalBody subject row with
                    | Ok number, Ok nodeId, Ok createdAt, Ok body ->
                        let isPr = match row.TryGetProperty "pull_request" with | true, _ -> true | _ -> false
                        Ok({ Number = number; NodeId = nodeId; CreatedAt = createdAt; Body = body; IsPullRequest = isPr } :: values)
                    | Error error, _, _, _
                    | _, Error error, _, _
                    | _, _, Error error, _
                    | _, _, _, Error error -> Error error)) (Ok [])
            |> Result.map List.rev
        parsed |> Result.bind (distinctNativeIds (fun (item: NativeSubject) -> item.NodeId) subject)

    let private parsePullNumbers (repository: RepositoryIdentity) (pages: RawPage list) =
        let subject = $"%s{repository.FullName} pull roster"
        rows pages
        |> List.fold (fun state row ->
            state |> Result.bind (fun values -> requiredInt subject "number" row |> Result.map (fun number -> number :: values))) (Ok [])
        |> Result.bind (fun numbers ->
            match numbers |> List.countBy id |> List.tryFind (fun (_, count) -> count > 1) with
            | Some(number, _) -> Error(DuplicateNativeId($"%s{repository.FullName}#%d{number}"))
            | None -> Ok(Set.ofList numbers))

    let private parseComments (repository: RepositoryIdentity) (pages: RawPage list) =
        let subject = $"%s{repository.FullName} issue comments"
        let parsed =
            rows pages
            |> List.fold (fun state row ->
                state
                |> Result.bind (fun values ->
                    match requiredString subject "node_id" row, requiredString subject "created_at" row, optionalBody subject row, requiredString subject "issue_url" row with
                    | Ok nodeId, Ok createdAt, Ok body, Ok issueUrl ->
                        Ok({ NodeId = nodeId; CreatedAt = createdAt; Body = body; IssueUrl = issueUrl } :: values)
                    | Error error, _, _, _
                    | _, Error error, _, _
                    | _, _, Error error, _
                    | _, _, _, Error error -> Error error)) (Ok [])
            |> Result.map List.rev
        parsed |> Result.bind (distinctNativeIds (fun (item: NativeComment) -> item.NodeId) subject)

    let private leadingLine (text: string) =
        let first = text.Replace("\r\n", "\n").Split('\n') |> Array.tryFind (fun line -> line.Trim() <> "")
        match first with
        | Some line ->
            let indent = line |> Seq.takeWhile (fun c -> c = ' ' || c = '\t') |> Seq.toArray |> String
            if indent.Contains '\t' || indent.Length >= 4 then line
            else
                let trimmed = text.Trim().Replace("\r\n", "\n")
                match trimmed.IndexOf '\n' with | -1 -> trimmed | index -> trimmed.Substring(0, index)
        | None -> ""

    let private delivery =
        Regex(@"\A<!-- fsgg:delivery-receipt id=(?<id>[a-z0-9][a-z0-9_.-]*) head=(?<head>[0-9a-f]{40}) evidence=(?<evidence>[^\s<>]+) -->\z", RegexOptions.Compiled ||| RegexOptions.CultureInvariant)

    let private intake =
        Regex(@"\A<!-- fsgg:intake:v1 id=(?<id>[A-Za-z0-9][A-Za-z0-9_.-]*) digest=(?<digest>[0-9a-f]{64}) -->", RegexOptions.Compiled ||| RegexOptions.CultureInvariant)

    let private commentNumber (apiBase: string) (repository: RepositoryIdentity) (issueUrl: string) =
        let baseUri = Uri(apiBase.TrimEnd('/') + "/", UriKind.Absolute)
        match Uri.TryCreate(issueUrl, UriKind.Absolute) with
        | true, uri when uri.Scheme = baseUri.Scheme && uri.Authority = baseUri.Authority ->
            let prefix = $"/repos/%s{repository.FullName}/issues/"
            if not (uri.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal)) then None
            else
                let suffix = uri.AbsolutePath.Substring(prefix.Length)
                let mutable number = 0
                if Int32.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, &number) && number > 0 then Some number else None
        | _ -> None

    let private classifyBody (horizon: DateTimeOffset) (repository: RepositoryIdentity) (subject: NativeSubject) =
        parseInstant ($"%s{repository.FullName}#%d{subject.Number}") subject.CreatedAt
        |> Result.bind (fun created ->
            if created > horizon then Ok None
            else
                let line = leadingLine subject.Body
                if line.StartsWith("<!-- fsgg:intake", StringComparison.Ordinal) then
                    let matched = intake.Match subject.Body
                    if not matched.Success || subject.IsPullRequest then
                        Error(MalformedCandidate($"%s{repository.FullName}#%d{subject.Number}", "intake receipt is malformed or bound to a pull request"))
                    else
                        Ok(Some
                            { Repository = repository; SubjectNumber = subject.Number; SubjectIsPullRequest = false
                              NativeId = subject.NodeId; Family = IntakeReceipt; Origin = IssueBody
                              CreatedAt = created.ToString("O", CultureInfo.InvariantCulture); PayloadSha256 = sha256 subject.Body; PayloadBlobSha = blobSha subject.Body
                              SessionOperationId = Some matched.Groups.["id"].Value })
                elif line.StartsWith("<!-- fsgg:delivery-receipt", StringComparison.Ordinal)
                     || subject.Body.StartsWith("<!-- fsgg:done-receipt", StringComparison.Ordinal) then
                    Error(MalformedCandidate($"%s{repository.FullName}#%d{subject.Number}", "receipt marker is bound to the wrong native origin"))
                else Ok None)

    let private classifyComment (apiBase: string) (horizon: DateTimeOffset) (repository: RepositoryIdentity) (roster: Map<int, NativeSubject>) (comment: NativeComment) =
        let label = $"%s{repository.FullName} comment %s{comment.NodeId}"
        parseInstant label comment.CreatedAt
        |> Result.bind (fun created ->
            match commentNumber apiBase repository comment.IssueUrl with
            | None -> Error(InvalidResponse(label, "issue_url escaped or did not name a roster subject"))
            | Some number ->
                match Map.tryFind number roster with
                | None -> Error(InvalidResponse(label, "issue_url did not join to the complete issue roster"))
                | Some subject when created > horizon -> Ok None
                | Some subject ->
                    let line = leadingLine comment.Body
                    if line.StartsWith("<!-- fsgg:delivery-receipt", StringComparison.Ordinal) then
                        let matched = delivery.Match line
                        let evidence = if matched.Success then matched.Groups.["evidence"].Value else ""
                        let mutable uri = Unchecked.defaultof<Uri>
                        if not subject.IsPullRequest || not matched.Success || not (Uri.TryCreate(evidence, UriKind.Absolute, &uri)) || uri.Scheme <> Uri.UriSchemeHttps || String.IsNullOrWhiteSpace uri.Host then
                            Error(MalformedCandidate(label, "delivery receipt is malformed or not bound to a pull request"))
                        else
                            Ok(Some
                                { Repository = repository; SubjectNumber = number; SubjectIsPullRequest = true
                                  NativeId = comment.NodeId; Family = DeliveryReceipt; Origin = IssueComment
                                  CreatedAt = created.ToString("O", CultureInfo.InvariantCulture); PayloadSha256 = sha256 comment.Body; PayloadBlobSha = blobSha comment.Body
                                  SessionOperationId = Some matched.Groups.["id"].Value })
                    elif comment.Body.StartsWith("<!-- fsgg:done-receipt v=1 -->", StringComparison.Ordinal) then
                        if subject.IsPullRequest then
                            Error(MalformedCandidate(label, "legacy done receipt is bound to a pull request"))
                        else
                            Ok(Some
                                { Repository = repository; SubjectNumber = number; SubjectIsPullRequest = false
                                  NativeId = comment.NodeId; Family = LegacyDoneReceipt; Origin = IssueComment
                                  CreatedAt = created.ToString("O", CultureInfo.InvariantCulture); PayloadSha256 = sha256 comment.Body; PayloadBlobSha = blobSha comment.Body
                                  SessionOperationId = None })
                    elif line.StartsWith("<!-- fsgg:intake", StringComparison.Ordinal) then
                        Error(MalformedCandidate(label, "intake receipt is bound to the wrong native origin"))
                    elif comment.Body.StartsWith("<!-- fsgg:done-receipt", StringComparison.Ordinal) then
                        Error(MalformedCandidate(label, "legacy done receipt marker is malformed"))
                    else Ok None)

    let private appendFingerprintFrame (hash: IncrementalHash) (value: string) =
        let bytes = Encoding.UTF8.GetBytes value
        hash.AppendData(Encoding.ASCII.GetBytes($"%d{bytes.Length}:"))
        hash.AppendData bytes

    let private appendPageFingerprint (hash: IncrementalHash) (page: RawPage) =
        [ page.Repository.FullName; string page.Repository.DatabaseId; page.Repository.NodeId
          string page.Pass; streamName page.Stream; string page.Index; page.Method
          page.ApiVersionRequested; page.ApiVersionSelected; page.Path
          page.Query |> List.collect (fun (key, value) -> [ key; value ]) |> String.concat "\u001f"
          string page.Status; page.Resource; page.Body; page.RawSha256
          defaultArg page.LinkHeader ""; page.ObservedAt; defaultArg page.NextLink ""
          string page.ItemCount; string page.Terminal ]
        |> List.iter (appendFingerprintFrame hash)

    let private rawFingerprint (pages: RawPage list) =
        use hash = IncrementalHash.CreateHash HashAlgorithmName.SHA256
        pages |> List.iter (appendPageFingerprint hash)
        hash.GetHashAndReset() |> Convert.ToHexString |> _.ToLowerInvariant()

    let private typedFingerprint (subjects: DraftSubject list) (inventoryDigest: string) =
        inventoryDigest ::
        (subjects
         |> List.collect (fun item ->
             [ item.Repository.FullName; string item.Repository.DatabaseId; item.Repository.NodeId
               string item.SubjectNumber; string item.SubjectIsPullRequest; item.NativeId
               string item.Family; string item.Origin; item.CreatedAt; item.PayloadSha256
               item.PayloadBlobSha; defaultArg item.SessionOperationId "" ]))
        |> List.map frame |> String.concat "" |> sha256

    let private eligibleInventory (apiBase: string) (horizon: DateTimeOffset) (pages: RawPage list) =
        repositories
        |> List.fold (fun state repository ->
            state |> Result.bind (fun accumulated ->
                let issues = pages |> List.filter (fun page -> page.Repository = repository && page.Stream = Issues)
                let comments = pages |> List.filter (fun page -> page.Repository = repository && page.Stream = IssueComments)
                parseSubjects repository issues
                |> Result.bind (fun subjects ->
                    parseComments repository comments
                    |> Result.bind (fun commentRows ->
                        let subjectRows =
                            subjects |> List.map (fun item ->
                                parseInstant repository.FullName item.CreatedAt
                                |> Result.map (fun created ->
                                    if created <= horizon then
                                        Some [ repository.FullName; "issue"; string item.Number; item.NodeId
                                               string item.IsPullRequest; created.ToString("O", CultureInfo.InvariantCulture) ]
                                    else None))
                        let commentInventory =
                            commentRows |> List.map (fun item ->
                                parseInstant repository.FullName item.CreatedAt
                                |> Result.bind (fun created ->
                                    match commentNumber apiBase repository item.IssueUrl with
                                    | None -> Error(InvalidResponse(repository.FullName, "comment parent escaped repository"))
                                    | Some number ->
                                        Ok(if created <= horizon then
                                               Some [ repository.FullName; "comment"; string number; item.NodeId
                                                      created.ToString("O", CultureInfo.InvariantCulture) ]
                                           else None)))
                        (subjectRows @ commentInventory)
                        |> List.fold (fun result row ->
                            result |> Result.bind (fun values -> row |> Result.map (fun value -> value :: values))) (Ok accumulated))))) (Ok [])
        |> Result.map (fun values ->
            values |> List.choose id |> List.sort |> List.collect id |> List.map frame |> String.concat "" |> sha256)

    let private collectRepository (transport: IVersionedSinglePageGitHubTransport) (apiBase: string) (pass: int) (horizon: DateTimeOffset) (repository: RepositoryIdentity) =
        let owner, repo = let parts = repository.FullName.Split('/', 2) in parts.[0], parts.[1]
        let root = $"repos/%s{owner}/%s{repo}"
        let issueQuery = [ "state", "all"; "sort", "created"; "direction", "asc" ]
        let eventQuery = []

        collectIdentity transport pass repository
        |> Result.bind (fun identity ->
            collectArrayStream transport apiBase pass repository Issues ($"%s{root}/issues") issueQuery
            |> Result.bind (fun issuePages ->
                collectArrayStream transport apiBase pass repository Pulls ($"%s{root}/pulls") issueQuery
                |> Result.bind (fun pullPages ->
                    collectArrayStream transport apiBase pass repository IssueComments ($"%s{root}/issues/comments") [ "sort", "created"; "direction", "asc" ]
                    |> Result.bind (fun commentPages ->
                        collectArrayStream transport apiBase pass repository IssueEvents ($"%s{root}/issues/events") eventQuery
                        |> Result.bind (fun eventPages ->
                            parseSubjects repository issuePages
                            |> Result.bind (fun subjects ->
                                parsePullNumbers repository pullPages
                                |> Result.bind (fun pulls ->
                                    let issuePulls = subjects |> List.filter _.IsPullRequest |> List.map _.Number |> Set.ofList
                                    if pulls <> issuePulls then
                                        Error(InvalidResponse(repository.FullName, "pull roster does not match pull rows in the complete issue roster"))
                                    else
                                        parseComments repository commentPages
                                        |> Result.bind (fun comments ->
                                            let roster = subjects |> List.map (fun item -> item.Number, item) |> Map.ofList
                                            let bodyDrafts = subjects |> List.map (classifyBody horizon repository)
                                            let commentDrafts = comments |> List.map (classifyComment apiBase horizon repository roster)
                                            let errors =
                                                bodyDrafts @ commentDrafts
                                                |> List.choose (function Error error -> Some error | Ok _ -> None)
                                            match errors with
                                            | error :: _ -> Error error
                                            | [] ->
                                                let drafts =
                                                    bodyDrafts @ commentDrafts
                                                    |> List.choose (function Ok(Some item) -> Some item | _ -> None)
                                                let duplicate = drafts |> List.countBy _.NativeId |> List.tryFind (fun (_, count) -> count > 1)
                                                match duplicate with
                                                | Some(nativeId, _) -> Error(DuplicateNativeId nativeId)
                                                | None ->
                                                    let timelineSubjects = drafts |> List.map _.SubjectNumber |> List.distinct |> List.sort
                                                    let timelineResult =
                                                        timelineSubjects
                                                        |> List.fold (fun state number ->
                                                            state
                                                            |> Result.bind (fun pages ->
                                                                collectArrayStream transport apiBase pass repository (Timeline number) ($"%s{root}/issues/%d{number}/timeline") []
                                                                |> Result.map (fun captured -> pages @ captured))) (Ok [])
                                                    timelineResult
                                                    |> Result.map (fun timelines ->
                                                        identity :: (issuePages @ pullPages @ commentPages @ eventPages @ timelines), drafts)))))))))

    let private collectPass transport apiBase pass horizonText horizon =
        repositories
        |> List.fold (fun state repository ->
            state
            |> Result.bind (fun (allPages, allSubjects) ->
                collectRepository transport apiBase pass horizon repository
                |> Result.map (fun (pages, subjects) -> allPages @ pages, allSubjects @ subjects))) (Ok([], []))
        |> Result.bind (fun (pages, subjects) ->
            match subjects |> List.countBy _.NativeId |> List.tryFind (fun (_, count) -> count > 1) with
            | Some(nativeId, _) -> Error(DuplicateNativeId nativeId)
            | None when pages |> List.exists (fun page -> page.Resource <> "core") ->
                Error(InvalidResponse("collector", "native repository stream changed X-RateLimit-Resource from core"))
            | None ->
                eligibleInventory apiBase horizon pages
                |> Result.map (fun inventoryDigest ->
                    let ordered =
                        subjects
                        |> List.sortBy (fun item -> item.Repository.FullName, item.SubjectNumber, item.CreatedAt, item.NativeId)
                    let draft =
                        { ObservationHorizon = horizonText
                          Repositories = repositories
                          Subjects = ordered
                          EligibleInventoryDigest = inventoryDigest
                          EvidenceFingerprint = typedFingerprint ordered inventoryDigest }
                    { Number = pass; Pages = pages; Draft = draft; RawEvidenceDigest = rawFingerprint pages }))

    let collectTwoPass transport apiBase observationHorizon =
        match Uri.TryCreate(apiBase, UriKind.Absolute) with
        | false, _ -> Error(InvalidResponse("collector", "apiBase must be an absolute URI"))
        | true, apiUri when apiUri.Scheme <> Uri.UriSchemeHttps && apiUri.Host <> "localhost" && apiUri.Host <> "127.0.0.1" ->
            Error(InvalidResponse("collector", "apiBase must use HTTPS outside local fixtures"))
        | true, _ ->
            parseInstant "observation horizon" observationHorizon
            |> Result.bind (fun horizon ->
                if horizon > DateTimeOffset.UtcNow then
                    Error(InvalidResponse("observation horizon", "horizon is later than the native observation time"))
                else
                collectPass transport apiBase 1 observationHorizon horizon
                |> Result.bind (fun first ->
                    collectPass transport apiBase 2 observationHorizon horizon
                    |> Result.bind (fun second ->
                        if first.Draft.Repositories <> second.Draft.Repositories then
                            Error(RosterDrift "repository identity changed between passes")
                        elif first.Draft.Subjects <> second.Draft.Subjects then
                            Error(PassDrift "derived receipt subjects changed between passes")
                        elif first.Draft.EligibleInventoryDigest <> second.Draft.EligibleInventoryDigest then
                            Error(PassDrift "eligible native issue/comment inventory changed between passes")
                        elif first.Draft.EvidenceFingerprint <> second.Draft.EvidenceFingerprint then
                            Error(PassDrift "typed receipt population changed between passes")
                        else Ok { First = first; Second = second })))

    let private registryRepository (repository: RepositoryIdentity) : FS.GG.Coord.HistoricalLossRegistry.RepositoryIdentityV3 =
        { FullName = repository.FullName; DatabaseId = repository.DatabaseId; NodeId = repository.NodeId }

    let private registryFamily =
        function
        | DeliveryReceipt -> "delivery-receipt"
        | IntakeReceipt -> "intake-receipt"
        | LegacyDoneReceipt -> "legacy-done-receipt"

    let private projectedPages (pages: RawPage list) =
        pages
        |> List.groupBy _.Repository
        |> List.collect (fun (_, repositoryPages) ->
            repositoryPages
            |> List.mapi (fun index page ->
                ({ Repository = registryRepository page.Repository
                   Index = index + 1
                   ItemCount = page.ItemCount
                   RawSha256 = page.RawSha256
                   Terminal = index = repositoryPages.Length - 1 }: FS.GG.Coord.HistoricalLossRegistry.RetainedPageV3)))

    let private projectedSubjects family (subjects: DraftSubject list) =
        subjects
        |> List.filter (fun subject -> registryFamily subject.Family = family)
        |> List.map (fun subject ->
            ({ Repository = registryRepository subject.Repository
               NativeId = subject.NativeId
               Family = registryFamily subject.Family
               CreatedAt = subject.CreatedAt
               PayloadBlobSha = subject.PayloadBlobSha
               SessionOperationId = subject.SessionOperationId
               LiveClaim = false }: FS.GG.Coord.HistoricalLossRegistry.RetainedSubjectV3))

    let private bindValidatedCapture (capture: Capture) expectedFamily expectedScope expectedObservationHorizon registryBytes
        (entry: FS.GG.Coord.HistoricalLossRegistry.EntryV3)
        (approval: FS.GG.Coord.HistoricalLossRegistry.NativeApprovalReadbackV2)
        : Result<FS.GG.Coord.HistoricalLossRegistry.BoundLossV3, string list> =
        let expectedRepositories = repositories |> List.map registryRepository
        if capture.First.Draft.Subjects <> capture.Second.Draft.Subjects
           || capture.First.Draft.EligibleInventoryDigest <> capture.Second.Draft.EligibleInventoryDigest then
            Error [ "historical-loss-native-pass-drift" ]
        else
            let firstPages = projectedPages capture.First.Pages
            let secondPages = projectedPages capture.Second.Pages
            let firstSubjects = projectedSubjects expectedFamily capture.First.Draft.Subjects
            let secondSubjects = projectedSubjects expectedFamily capture.Second.Draft.Subjects
            let errors = ResizeArray<string>()
            let check condition message = if not condition then errors.Add message
            check (entry.CensusFirst.SelectedRepositories = expectedRepositories && entry.CensusSecond.SelectedRepositories = expectedRepositories)
                "historical-loss-native-repository-roster"
            check (entry.CensusFirst.ObservationHorizon = expectedObservationHorizon && entry.CensusSecond.ObservationHorizon = expectedObservationHorizon)
                "historical-loss-native-observation-horizon"
            let sourceRevisions = entry.RecoverySources |> List.map _.Revision |> Set.ofList
            check (Set.contains entry.CensusFirst.Revision sourceRevisions
                   && Set.contains entry.CensusSecond.Revision sourceRevisions)
                "historical-loss-native-source-revision"
            check (entry.CensusFirst.Pages = firstPages && entry.CensusSecond.Pages = secondPages)
                "historical-loss-native-pages"
            check (entry.CensusFirst.RawEvidenceDigest = capture.First.RawEvidenceDigest && entry.CensusSecond.RawEvidenceDigest = capture.Second.RawEvidenceDigest)
                "historical-loss-native-raw-digests"
            check (entry.CensusFirst.TypedPopulationDigest = capture.First.Draft.EvidenceFingerprint
                   && entry.CensusSecond.TypedPopulationDigest = capture.Second.Draft.EvidenceFingerprint
                   && entry.CensusFirst.EligibleInventoryDigest = capture.First.Draft.EligibleInventoryDigest
                   && entry.CensusSecond.EligibleInventoryDigest = capture.Second.Draft.EligibleInventoryDigest)
                "historical-loss-native-typed-digests"
            check (entry.CensusFirst.Subjects = firstSubjects && entry.CensusSecond.Subjects = secondSubjects)
                "historical-loss-native-subjects"
            check (entry.CensusFirst.DeclaredCount = firstSubjects.Length && entry.CensusSecond.DeclaredCount = secondSubjects.Length)
                "historical-loss-native-receipt-count"
            if errors.Count > 0 then Error(List.ofSeq errors)
            else
                match FS.GG.Coord.HistoricalLossRegistry.bindV3 expectedFamily expectedScope expectedObservationHorizon expectedRepositories registryBytes entry approval with
                | Error [ "historical-loss-native-census-proof-unavailable" ] ->
                    Ok
                        { Family = entry.Family
                          Scope = entry.Scope
                          ObservationHorizon = entry.ObservationHorizon
                          SelectedRepositories = expectedRepositories
                          RetainedCount = firstSubjects.Length
                          CensusDigest = entry.CensusFirst.Digest
                          ApprovalDigest = approval.ApprovalEnvelopeFirst.BodySha256
                          Consequence = entry.Consequence }
                | Error errors -> Error errors
                | Ok _ -> Error [ "historical-loss-native-census-proof-contract" ]

    let bindV3Captured apiBase (capture: Capture) expectedFamily expectedScope expectedObservationHorizon registryBytes entry approval =
        let mutable requiredHorizon = DateTimeOffset.MinValue
        let horizonValid =
            DateTimeOffset.TryParseExact(expectedObservationHorizon, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, &requiredHorizon)
        let pageObservedAfterHorizon (page: RawPage) =
            let mutable observed = DateTimeOffset.MinValue
            DateTimeOffset.TryParseExact(page.ObservedAt, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, &observed)
            && horizonValid && observed >= requiredHorizon
        if not horizonValid || (capture.First.Pages @ capture.Second.Pages |> List.exists (pageObservedAfterHorizon >> not)) then
            Error [ "historical-loss-retained-page-before-horizon" ]
        else
        let originals = capture.First.Pages @ capture.Second.Pages
        let queue = Queue<RawPage>(originals)
        let replay =
            { new IVersionedSinglePageGitHubTransport with
                member _.SendSingleVersioned(version, request) =
                    if queue.Count = 0 then Error(Errors.Transport "retained raw capture ended before native enumeration")
                    else
                        let page = queue.Dequeue()
                        if version <> page.ApiVersionRequested || request.Method <> page.Method || request.Path <> page.Path || request.Query <> page.Query then
                            Error(Errors.Transport "retained raw capture API version, path, method or query drifted")
                        else
                            let headers =
                                Map [ "X-RateLimit-Resource", page.Resource
                                      "X-GitHub-Api-Version-Selected", page.ApiVersionSelected ]
                                |> fun values -> match page.LinkHeader with Some value -> values.Add("Link", value) | None -> values
                            Ok { Status = page.Status; Body = page.Body; Headers = headers; ETag = None; NextLink = page.NextLink } }
        let exactRaw (original: PassCapture) (recomputed: PassCapture) =
            original.Number = recomputed.Number
            && original.RawEvidenceDigest = rawFingerprint original.Pages
            && original.Pages.Length = recomputed.Pages.Length
            && List.forall2 (fun stored decoded ->
                stored = { decoded with ObservedAt = stored.ObservedAt }) original.Pages recomputed.Pages
        match collectTwoPass replay apiBase expectedObservationHorizon with
        | Error error -> Error [ $"historical-loss-retained-raw-replay: %A{error}" ]
        | Ok decoded when queue.Count <> 0 || not (exactRaw capture.First decoded.First) || not (exactRaw capture.Second decoded.Second) ->
            Error [ "historical-loss-retained-raw-integrity" ]
        | Ok decoded ->
            let sealedCapture =
                { First = { decoded.First with Pages = capture.First.Pages; RawEvidenceDigest = capture.First.RawEvidenceDigest }
                  Second = { decoded.Second with Pages = capture.Second.Pages; RawEvidenceDigest = capture.Second.RawEvidenceDigest } }
            bindValidatedCapture sealedCapture expectedFamily expectedScope expectedObservationHorizon registryBytes entry approval

    // A private local artifact for the interval between direct collection and the independent
    // postmerge approval. The binary format stores only raw pages and their digests. Typed Draft
    // fields are never deserialized as evidence; bindV3Captured reconstructs them by replay.
    let private privateMode = UnixFileMode.UserRead ||| UnixFileMode.UserWrite
    let private captureMagic = Encoding.ASCII.GetBytes("FSGG-HLCAP-v1\u0000")
    let private strictUtf8 = UTF8Encoding(false, true)
    let private maximumPages = 200000
    let private maximumPageBodyBytes = 4 * 1024 * 1024

    [<DllImport("libc", SetLastError = true, EntryPoint = "open")>]
    extern int private unixOpen(string path, int flags)

    [<DllImport("libc", SetLastError = true, EntryPoint = "fsync")>]
    extern int private unixFsync(int fd)

    [<DllImport("libc", SetLastError = true, EntryPoint = "statx")>]
    extern int private unixStatx(int dirfd, string path, int flags, uint32 mask, nativeint buffer)

    // Linux flags. The store intentionally refuses platforms without these descriptor guarantees.
    let private oNoFollow = 0x20000
    let private oCloseOnExec = 0x80000
    let private oNonBlock = 0x800
    let private oDirectory = 0x10000

    let private openedRegularPrivateFile fd =
        let buffer = Marshal.AllocHGlobal 256
        try
            // AT_EMPTY_PATH pins statx to the opened descriptor. statx mode is at offset 28,
            // size at 40 in the Linux UAPI layout, independent of libc's struct stat layout.
            if unixStatx(fd, "", 0x1000, 0x203u, buffer) <> 0 then false
            else
                let mode = int (uint16 (Marshal.ReadInt16(buffer, 28)))
                let size = Marshal.ReadInt64(buffer, 40)
                mode &&& 0xF000 = 0x8000
                && mode &&& 0x1FF = 0x180
                && size >= 0L
        finally
            Marshal.FreeHGlobal buffer

    let private writeText (writer: BinaryWriter) (value: string) =
        let bytes = strictUtf8.GetBytes value
        if bytes.Length > maximumPageBodyBytes then
            raise (InvalidDataException "private capture write exceeds bounds")
        writer.Write bytes.Length
        writer.Write bytes

    let private writeOptional (writer: BinaryWriter) =
        function
        | None -> writer.Write false
        | Some value -> writer.Write true; writeText writer value

    let private readText (reader: BinaryReader) maximum =
        let length = reader.ReadInt32()
        if length < 0 || length > maximum || int64 length > reader.BaseStream.Length - reader.BaseStream.Position then
            raise (InvalidDataException "invalid private capture field length")
        strictUtf8.GetString(reader.ReadBytes length)

    let private readOptional (reader: BinaryReader) maximum =
        if reader.ReadBoolean() then Some(readText reader maximum) else None

    let private writePage (writer: BinaryWriter) (page: RawPage) =
        writer.Write page.Pass
        writeText writer page.Repository.FullName
        writer.Write page.Repository.DatabaseId
        writeText writer page.Repository.NodeId
        match page.Stream with
        | Identity -> writer.Write 0
        | Issues -> writer.Write 1
        | Pulls -> writer.Write 2
        | IssueComments -> writer.Write 3
        | IssueEvents -> writer.Write 4
        | Timeline number -> writer.Write 5; writer.Write number
        writer.Write page.Index
        writeText writer page.Method
        writeText writer page.ApiVersionRequested
        writeText writer page.ApiVersionSelected
        writeText writer page.Path
        writer.Write page.Query.Length
        for name, value in page.Query do writeText writer name; writeText writer value
        writer.Write page.Status
        writeText writer page.Resource
        writeText writer page.Body
        writeText writer page.RawSha256
        writeOptional writer page.LinkHeader
        writeText writer page.ObservedAt
        writeOptional writer page.NextLink
        writer.Write page.ItemCount
        writer.Write page.Terminal

    let private readPage (reader: BinaryReader) =
        let pass = reader.ReadInt32()
        let repository =
            { FullName = readText reader 512
              DatabaseId = reader.ReadInt64()
              NodeId = readText reader 512 }
        let stream =
            match reader.ReadInt32() with
            | 0 -> Identity
            | 1 -> Issues
            | 2 -> Pulls
            | 3 -> IssueComments
            | 4 -> IssueEvents
            | 5 ->
                let number = reader.ReadInt32()
                if number <= 0 then raise (InvalidDataException "invalid timeline number")
                Timeline number
            | _ -> raise (InvalidDataException "invalid native stream")
        let index = reader.ReadInt32()
        let method = readText reader 16
        let requested = readText reader 32
        let selected = readText reader 32
        let path = readText reader 4096
        let queryCount = reader.ReadInt32()
        if queryCount < 0 || queryCount > 32 then raise (InvalidDataException "invalid query count")
        let query = [ for _ in 1 .. queryCount -> readText reader 256, readText reader 4096 ]
        { Pass = pass
          Repository = repository
          Stream = stream
          Index = index
          Method = method
          ApiVersionRequested = requested
          ApiVersionSelected = selected
          Path = path
          Query = query
          Status = reader.ReadInt32()
          Resource = readText reader 256
          Body = readText reader (4 * 1024 * 1024)
          RawSha256 = readText reader 64
          LinkHeader = readOptional reader 16384
          ObservedAt = readText reader 128
          NextLink = readOptional reader 8192
          ItemCount = reader.ReadInt32()
          Terminal = reader.ReadBoolean() }

    let private rawPassValid number (pass: PassCapture) =
        pass.Number = number
        && pass.Pages.Length <= maximumPages
        && pass.Pages |> List.forall (fun page ->
            page.Pass = number && Set.contains page.Repository (Set.ofList repositories)
            && Encoding.UTF8.GetByteCount page.Body <= maximumPageBodyBytes
            && page.RawSha256 = sha256 page.Body)
        && pass.RawEvidenceDigest = rawFingerprint pass.Pages

    let savePrivate path (capture: Capture) =
        if not (OperatingSystem.IsLinux()) then Error "private native capture requires Linux descriptor checks"
        elif String.IsNullOrWhiteSpace path
             || not (rawPassValid 1 capture.First && rawPassValid 2 capture.Second)
             || capture.First.Draft.ObservationHorizon <> capture.Second.Draft.ObservationHorizon then
            Error "private native capture is incomplete or inconsistent"
        else
            let mutable temporary = ""
            try
                let target = Path.GetFullPath path
                temporary <- target + ".tmp-" + Guid.NewGuid().ToString("N")
                let options = FileStreamOptions()
                options.Mode <- FileMode.CreateNew
                options.Access <- FileAccess.Write
                options.Share <- FileShare.None
                options.UnixCreateMode <- privateMode
                use stream = new FileStream(temporary, options)
                use writer = new BinaryWriter(stream, strictUtf8, true)
                writer.Write captureMagic
                writeText writer capture.First.Draft.ObservationHorizon
                for pass in [ capture.First; capture.Second ] do
                    writer.Write pass.Number
                    writeText writer pass.RawEvidenceDigest
                    writer.Write pass.Pages.Length
                    pass.Pages |> List.iter (writePage writer)
                writer.Flush()
                if File.GetUnixFileMode temporary <> privateMode then
                    raise (InvalidDataException "private capture exceeds limits")
                stream.Flush true
                File.Move(temporary, target, false)
                let directory = Path.GetDirectoryName target
                let directoryFd = unixOpen(directory, oDirectory ||| oNoFollow ||| oCloseOnExec)
                if directoryFd < 0 then raise (IOException "private capture directory could not be opened")
                use directoryHandle = new SafeFileHandle(nativeint directoryFd, true)
                if unixFsync directoryFd <> 0 then raise (IOException "private capture directory could not be synced")
                Ok()
            with _ ->
                if temporary <> "" then
                    try File.Delete temporary with _ -> ()
                Error "private native capture could not be saved"

    let loadPrivate path =
        if not (OperatingSystem.IsLinux()) then Error "private native capture requires Linux descriptor checks"
        elif String.IsNullOrWhiteSpace path then Error "private native capture path is invalid"
        else
            try
                let descriptor = unixOpen(Path.GetFullPath path, oNoFollow ||| oCloseOnExec ||| oNonBlock)
                if descriptor < 0 then
                    Error "private native capture is missing, non-private, or too large"
                else
                    use handle = new SafeFileHandle(nativeint descriptor, true)
                    if not (openedRegularPrivateFile descriptor) then
                        Error "private native capture is missing, non-private, or too large"
                    else
                      use stream = new FileStream(handle, FileAccess.Read)
                      use reader = new BinaryReader(stream, strictUtf8, true)
                      if reader.ReadBytes(captureMagic.Length) <> captureMagic then
                        Error "private native capture format is invalid"
                      else
                        let horizon = readText reader 128
                        let draft =
                            { ObservationHorizon = horizon; Repositories = repositories; Subjects = []
                              EvidenceFingerprint = ""; EligibleInventoryDigest = "" }
                        let readPass expected =
                            let number = reader.ReadInt32()
                            let digest = readText reader 64
                            let count = reader.ReadInt32()
                            if number <> expected || count < 0 || count > maximumPages then
                                raise (InvalidDataException "invalid native pass")
                            let pages = [ for _ in 1 .. count -> readPage reader ]
                            { Number = number; Pages = pages; Draft = draft; RawEvidenceDigest = digest }
                        let first, second = readPass 1, readPass 2
                        if stream.Position <> stream.Length || not (rawPassValid 1 first && rawPassValid 2 second) then
                            Error "private native capture failed raw integrity checks"
                        else Ok { First = first; Second = second }
            with _ -> Error "private native capture could not be loaded"

    // The command-line capture uses this path rather than collectTwoPass + savePrivate. Only one
    // response page and compact issue/comment roster rows are retained in memory at a time; raw
    // bodies are written once to the private descriptor and then discarded.
    let private appendRawPage (hash: IncrementalHash) (page: RawPage) =
        appendPageFingerprint hash page

    let private collectArrayProjected
        (transport: IVersionedSinglePageGitHubTransport)
        (apiBase: string)
        (pass: int)
        (repository: RepositoryIdentity)
        (streamKind: Stream)
        (path: string)
        (baseQuery: (string * string) list)
        (project: string -> JsonElement list -> Result<'a list, CollectorError>)
        (retain: RawPage -> Result<unit, CollectorError>) =
        let rec loop index currentPath query seen resourceSeen acc =
            let subject = requestSubject repository streamKind index
            if index > 10000 then
                Error(InvalidResponse(subject, "pagination did not terminate within 10000 pages"))
            else
                send transport repository streamKind index currentPath query
                |> Result.bind (fun (request, response, resource) ->
                    if resourceSeen |> Option.exists ((<>) resource) then
                        Error(InvalidResponse(subject, "X-RateLimit-Resource changed during the stream"))
                    else
                        parseArray subject response.Body
                        |> Result.bind (fun rows ->
                            let ids = rows |> List.map (rowId subject)
                            let idError = ids |> List.tryPick (function Error error -> Some error | Ok _ -> None)
                            let pageIds = ids |> List.choose (function Ok value -> Some value | Error _ -> None)
                            let duplicate = pageIds |> List.tryFind (fun id -> Set.contains id seen)
                            let duplicateInPage = pageIds |> List.countBy id |> List.tryFind (fun (_, count) -> count > 1) |> Option.map fst
                            let count = rows.Length
                            let explicitNext = response.NextLink
                            let next =
                                match explicitNext with
                                | Some link -> continuation apiBase repository streamKind index request.Path request.Query link |> Result.map Some
                                | None when count = 100 ->
                                    Ok(Some(request.Path, request.Query |> List.map (fun (key, value) -> if key = "page" then key, string (index + 1) else key, value)))
                                | None -> Ok None
                            match idError, duplicate |> Option.orElse duplicateInPage with
                            | Some error, _ -> Error error
                            | None, Some id -> Error(DuplicateNativeId($"%s{repository.FullName}/%s{streamName streamKind}/%s{id}"))
                            | None, None ->
                                next
                                |> Result.bind (fun nextRequest ->
                                    let page =
                                        { Pass = pass; Repository = repository; Stream = streamKind; Index = index
                                          Method = request.Method; ApiVersionRequested = ApiVersion
                                          ApiVersionSelected = (header "X-GitHub-Api-Version-Selected" response).Value
                                          Path = request.Path; Query = request.Query; Status = response.Status
                                          Resource = resource; Body = response.Body; RawSha256 = sha256 response.Body
                                          LinkHeader = header "Link" response
                                          ObservedAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
                                          NextLink = explicitNext; ItemCount = count; Terminal = nextRequest.IsNone }
                                    retain page
                                    |> Result.bind (fun () -> project subject rows)
                                    |> Result.bind (fun projected ->
                                        let accumulated = List.rev projected @ acc
                                        match nextRequest with
                                        | None -> Ok(List.rev accumulated)
                                        | Some(nextPath, nextQuery) ->
                                            loop (index + 1) nextPath nextQuery (Set.union seen (Set.ofList pageIds)) (Some resource) accumulated))))
        loop 1 path (baseQuery @ [ "per_page", "100"; "page", "1" ]) Set.empty None []

    let private markerBody (body: string) =
        let line = leadingLine body
        if line.StartsWith("<!-- fsgg:", StringComparison.Ordinal) then body else ""

    let private projectedNativeSubjects repository subject rows =
        rows
        |> List.fold (fun state row ->
            state |> Result.bind (fun values ->
                match requiredInt subject "number" row, requiredString subject "node_id" row,
                      requiredString subject "created_at" row, optionalBody subject row with
                | Ok number, Ok nodeId, Ok createdAt, Ok body ->
                    let isPr = match row.TryGetProperty "pull_request" with | true, _ -> true | _ -> false
                    Ok({ Number = number; NodeId = nodeId; CreatedAt = createdAt; Body = markerBody body; IsPullRequest = isPr } :: values)
                | Error error, _, _, _ | _, Error error, _, _ | _, _, Error error, _ | _, _, _, Error error -> Error error)) (Ok [])
        |> Result.map List.rev

    let private projectedPulls _subject rows =
        rows
        |> List.fold (fun state row -> state |> Result.bind (fun values -> requiredInt "pull roster" "number" row |> Result.map (fun number -> number :: values))) (Ok [])
        |> Result.map List.rev

    let private projectedComments repository subject rows =
        rows
        |> List.fold (fun state row ->
            state |> Result.bind (fun values ->
                match requiredString subject "node_id" row, requiredString subject "created_at" row,
                      optionalBody subject row, requiredString subject "issue_url" row with
                | Ok nodeId, Ok createdAt, Ok body, Ok issueUrl ->
                    Ok({ NodeId = nodeId; CreatedAt = createdAt; Body = markerBody body; IssueUrl = issueUrl } :: values)
                | Error error, _, _, _ | _, Error error, _, _ | _, _, Error error, _ | _, _, _, Error error -> Error error)) (Ok [])
        |> Result.map List.rev

    let private inventoryRows apiBase horizon repository (subjects: NativeSubject list) (comments: NativeComment list) =
        let subjectRows =
            subjects |> List.map (fun item ->
                parseInstant repository.FullName item.CreatedAt
                |> Result.map (fun created ->
                    if created <= horizon then
                        Some [ repository.FullName; "issue"; string item.Number; item.NodeId
                               string item.IsPullRequest; created.ToString("O", CultureInfo.InvariantCulture) ]
                    else None))
        let commentRows =
            comments |> List.map (fun item ->
                parseInstant repository.FullName item.CreatedAt
                |> Result.bind (fun created ->
                    match commentNumber apiBase repository item.IssueUrl with
                    | None -> Error(InvalidResponse(repository.FullName, "comment parent escaped repository"))
                    | Some number ->
                        Ok(if created <= horizon then
                               Some [ repository.FullName; "comment"; string number; item.NodeId
                                      created.ToString("O", CultureInfo.InvariantCulture) ]
                           else None)))
        subjectRows @ commentRows
        |> List.fold (fun state row -> state |> Result.bind (fun values -> row |> Result.map (fun value -> value :: values))) (Ok [])

    type private StreamedPass =
        { Draft: UntrustedDraft; PageCount: int; RawEvidenceDigest: string }

    let private collectPassStreamed transport apiBase pass horizonText horizon (writer: BinaryWriter) =
        use rawHash = IncrementalHash.CreateHash HashAlgorithmName.SHA256
        let mutable pageCount = 0
        let retain page =
            if pageCount >= maximumPages then Error(InvalidResponse("collector", "native capture exceeded the bounded page count"))
            elif page.Resource <> "core" then Error(InvalidResponse("collector", "native repository stream changed X-RateLimit-Resource from core"))
            else
                writePage writer page
                appendRawPage rawHash page
                pageCount <- pageCount + 1
                Ok()
        let collectRepositoryStreamed repository =
            let owner, repo = let parts = repository.FullName.Split('/', 2) in parts.[0], parts.[1]
            let root = $"repos/%s{owner}/%s{repo}"
            let issueQuery = [ "state", "all"; "sort", "created"; "direction", "asc" ]
            collectIdentity transport pass repository
            |> Result.bind (fun identity -> retain identity)
            |> Result.bind (fun () -> collectArrayProjected transport apiBase pass repository Issues ($"%s{root}/issues") issueQuery (projectedNativeSubjects repository) retain)
            |> Result.bind (fun subjects ->
                collectArrayProjected transport apiBase pass repository Pulls ($"%s{root}/pulls") issueQuery projectedPulls retain
                |> Result.bind (fun pulls ->
                    let pullSet = Set.ofList pulls
                    if pullSet.Count <> pulls.Length then Error(DuplicateNativeId($"%s{repository.FullName} pull roster"))
                    elif pullSet <> (subjects |> List.filter _.IsPullRequest |> List.map _.Number |> Set.ofList) then
                        Error(InvalidResponse(repository.FullName, "pull roster does not match pull rows in the complete issue roster"))
                    else
                        collectArrayProjected transport apiBase pass repository IssueComments ($"%s{root}/issues/comments") [ "sort", "created"; "direction", "asc" ] (projectedComments repository) retain
                        |> Result.bind (fun comments ->
                            collectArrayProjected transport apiBase pass repository IssueEvents ($"%s{root}/issues/events") [] (fun _ _ -> Ok []) retain
                            |> Result.bind (fun (_: unit list) ->
                                let roster = subjects |> List.map (fun item -> item.Number, item) |> Map.ofList
                                let bodyDrafts = subjects |> List.map (classifyBody horizon repository)
                                let commentDrafts = comments |> List.map (classifyComment apiBase horizon repository roster)
                                match bodyDrafts @ commentDrafts |> List.tryPick (function Error error -> Some error | _ -> None) with
                                | Some error -> Error error
                                | None ->
                                    let drafts = bodyDrafts @ commentDrafts |> List.choose (function Ok(Some item) -> Some item | _ -> None)
                                    match drafts |> List.countBy _.NativeId |> List.tryFind (fun (_, count) -> count > 1) with
                                    | Some(nativeId, _) -> Error(DuplicateNativeId nativeId)
                                    | None ->
                                        drafts |> List.map _.SubjectNumber |> List.distinct |> List.sort
                                        |> List.fold (fun state number ->
                                            state |> Result.bind (fun () ->
                                                collectArrayProjected transport apiBase pass repository (Timeline number) ($"%s{root}/issues/%d{number}/timeline") [] (fun _ _ -> Ok []) retain
                                                |> Result.map (fun (_: unit list) -> ()))) (Ok())
                                        |> Result.bind (fun () -> inventoryRows apiBase horizon repository subjects comments)
                                        |> Result.map (fun inventory -> drafts, inventory)))))
        repositories
        |> List.fold (fun state repository ->
            state |> Result.bind (fun (drafts, inventory) ->
                collectRepositoryStreamed repository
                |> Result.map (fun (moreDrafts, moreInventory) -> moreDrafts @ drafts, moreInventory @ inventory))) (Ok([], []))
        |> Result.bind (fun (subjects, inventory) ->
            match subjects |> List.countBy _.NativeId |> List.tryFind (fun (_, count) -> count > 1) with
            | Some(nativeId, _) -> Error(DuplicateNativeId nativeId)
            | None ->
                let ordered = subjects |> List.sortBy (fun item -> item.Repository.FullName, item.SubjectNumber, item.CreatedAt, item.NativeId)
                let inventoryDigest = inventory |> List.choose id |> List.sort |> List.collect id |> List.map frame |> String.concat "" |> sha256
                let draft =
                    { ObservationHorizon = horizonText; Repositories = repositories; Subjects = ordered
                      EligibleInventoryDigest = inventoryDigest; EvidenceFingerprint = typedFingerprint ordered inventoryDigest }
                let rawDigest = rawHash.GetHashAndReset() |> Convert.ToHexString |> _.ToLowerInvariant()
                Ok { Draft = draft; PageCount = pageCount; RawEvidenceDigest = rawDigest })

    let private patchPassHeader (writer: BinaryWriter) (digestPosition: int64) (countPosition: int64) (digest: string) (count: int) =
        let endPosition = writer.BaseStream.Position
        writer.BaseStream.Position <- digestPosition
        writeText writer digest
        writer.BaseStream.Position <- countPosition
        writer.Write count
        writer.BaseStream.Position <- endPosition

    let private validatePrivateStream (stream: FileStream) =
        stream.Position <- 0L
        use reader = new BinaryReader(stream, strictUtf8, true)
        if reader.ReadBytes(captureMagic.Length) <> captureMagic then raise (InvalidDataException "invalid private capture magic")
        let horizon = readText reader 128
        let readPass expected =
            let number = reader.ReadInt32()
            let expectedDigest = readText reader 64
            let count = reader.ReadInt32()
            if number <> expected || count < 0 || count > maximumPages then raise (InvalidDataException "invalid native pass")
            use hash = IncrementalHash.CreateHash HashAlgorithmName.SHA256
            for _ in 1 .. count do
                let page = readPage reader
                if page.Pass <> expected || not (Set.contains page.Repository (Set.ofList repositories)) || page.RawSha256 <> sha256 page.Body then
                    raise (InvalidDataException "private capture failed raw integrity checks")
                appendRawPage hash page
            let actualDigest = hash.GetHashAndReset() |> Convert.ToHexString |> _.ToLowerInvariant()
            if actualDigest <> expectedDigest then raise (InvalidDataException "private capture digest mismatch")
            count, actualDigest
        let firstCount, firstDigest = readPass 1
        let secondCount, secondDigest = readPass 2
        if stream.Position <> stream.Length then raise (InvalidDataException "private capture has trailing bytes")
        { ObservationHorizon = horizon; FirstPageCount = firstCount; FirstRawEvidenceDigest = firstDigest
          SecondPageCount = secondCount; SecondRawEvidenceDigest = secondDigest }

    let collectTwoPassPrivate transport apiBase observationHorizon path =
        let mutable temporary = ""
        try
            if not (OperatingSystem.IsLinux()) then Error "private native capture requires Linux descriptor checks"
            elif String.IsNullOrWhiteSpace path || File.Exists path || Directory.Exists path then Error "private native capture path is invalid"
            else
                match Uri.TryCreate(apiBase, UriKind.Absolute) with
                | false, _ -> Error "native census refused"
                | true, apiUri when apiUri.Scheme <> Uri.UriSchemeHttps && apiUri.Host <> "localhost" && apiUri.Host <> "127.0.0.1" -> Error "native census refused"
                | true, _ ->
                    match parseInstant "observation horizon" observationHorizon with
                    | Error _ -> Error "native census refused"
                    | Ok horizon when horizon > DateTimeOffset.UtcNow -> Error "native census refused"
                    | Ok horizon ->
                        let target = Path.GetFullPath path
                        temporary <- target + ".tmp-" + Guid.NewGuid().ToString("N")
                        let options = FileStreamOptions()
                        options.Mode <- FileMode.CreateNew
                        options.Access <- FileAccess.ReadWrite
                        options.Share <- FileShare.None
                        options.UnixCreateMode <- privateMode
                        use stream = new FileStream(temporary, options)
                        use writer = new BinaryWriter(stream, strictUtf8, true)
                        writer.Write captureMagic
                        writeText writer observationHorizon
                        let collectPassNumber (number: int) =
                            writer.Write number
                            let digestPosition = stream.Position
                            writeText writer (String.replicate 64 "0")
                            let countPosition = stream.Position
                            writer.Write 0
                            collectPassStreamed transport apiBase number observationHorizon horizon writer
                            |> Result.map (fun captured ->
                                patchPassHeader writer digestPosition countPosition captured.RawEvidenceDigest captured.PageCount
                                captured)
                        match collectPassNumber 1 with
                        | Error _ -> Error "native census refused"
                        | Ok first ->
                            match collectPassNumber 2 with
                            | Error _ -> Error "native census refused"
                            | Ok second when first.Draft.Repositories <> second.Draft.Repositories
                                             || first.Draft.Subjects <> second.Draft.Subjects
                                             || first.Draft.EligibleInventoryDigest <> second.Draft.EligibleInventoryDigest
                                             || first.Draft.EvidenceFingerprint <> second.Draft.EvidenceFingerprint -> Error "native census pass drift"
                            | Ok second ->
                                writer.Flush()
                                if File.GetUnixFileMode temporary <> privateMode then raise (InvalidDataException "private capture permissions changed")
                                stream.Flush true
                                let verified = validatePrivateStream stream
                                if verified.FirstPageCount <> first.PageCount || verified.SecondPageCount <> second.PageCount
                                   || verified.FirstRawEvidenceDigest <> first.RawEvidenceDigest
                                   || verified.SecondRawEvidenceDigest <> second.RawEvidenceDigest then
                                    raise (InvalidDataException "private capture readback mismatch")
                                File.Move(temporary, target, false)
                                temporary <- ""
                                let directoryFd = unixOpen(Path.GetDirectoryName target, oDirectory ||| oNoFollow ||| oCloseOnExec)
                                if directoryFd < 0 then raise (IOException "private capture directory could not be opened")
                                use directoryHandle = new SafeFileHandle(nativeint directoryFd, true)
                                if unixFsync directoryFd <> 0 then raise (IOException "private capture directory could not be synced")
                                Ok verified
        with _ -> Error "private native capture could not be saved"
        |> fun result ->
            if Result.isError result && temporary <> "" then
                try File.Delete temporary with _ -> ()
            result
