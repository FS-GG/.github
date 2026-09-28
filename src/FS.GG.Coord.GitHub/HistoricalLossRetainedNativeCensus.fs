namespace FS.GG.Coord.GitHub

module HistoricalLossRetainedNativeCensus =

    open System
    open System.Collections.Generic
    open System.Globalization
    open System.Security.Cryptography
    open System.Text
    open System.Text.Json
    open System.Text.RegularExpressions
    open Errors
    open Transport

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
          Path: string
          Query: (string * string) list
          Status: int
          Resource: string
          Body: string
          RawSha256: string
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
          EvidenceFingerprint: string }

    type PassCapture =
        { Number: int
          Pages: RawPage list
          Draft: UntrustedDraft }

    type Capture =
        { First: PassCapture
          Second: PassCapture }

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
          { FullName = "FS-GG/Audio"; DatabaseId = 1292226968L; NodeId = "R_kgDOTQXRmA" }
          { FullName = "FS-GG/Coordination"; DatabaseId = 1346720714L; NodeId = "R_kgDOUEVTyg" }
          { FullName = "FS-GG/Game"; DatabaseId = 1290990429L; NodeId = "R_kgDOTPLzXQ" }
          { FullName = "FS-GG/Governance"; DatabaseId = 1273065119L; NodeId = "R_kgDOS-Funw" }
          { FullName = "FS-GG/Net"; DatabaseId = 1305845505L; NodeId = "R_kgDOTdWfAQ" }
          { FullName = "FS-GG/Rendering"; DatabaseId = 1269292235L; NodeId = "R_kgDOS6fcyw" }
          { FullName = "FS-GG/SDD"; DatabaseId = 1274272672L; NodeId = "R_kgDOS_PboA" }
          { FullName = "FS-GG/Templates"; DatabaseId = 1281961814L; NodeId = "R_kgDOTGkvVg" } ]

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

    let private send (transport: ISinglePageGitHubTransport) (repository: RepositoryIdentity) (stream: Stream) (index: int) (path: string) (query: (string * string) list) =
        let subject = requestSubject repository stream index
        let request =
            { Method = "GET"
              Path = path
              Query = query
              Body = NoBody
              Budget = Rest
              IfNoneMatch = None
              Subject = subject }

        match transport.SendSingle request with
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

    let private collectArrayStream (transport: ISinglePageGitHubTransport) (apiBase: string) (pass: int) (repository: RepositoryIdentity) (stream: Stream) (path: string) (baseQuery: (string * string) list) =
        let rec loop index path query seen acc =
            let subject = requestSubject repository stream index
            if index > 10000 then
                Error(InvalidResponse(subject, "pagination did not terminate within 10000 pages"))
            else
              send transport repository stream index path query
              |> Result.bind (fun (request, response, resource) ->
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
                              Path = request.Path
                              Query = request.Query
                              Status = response.Status
                              Resource = resource
                              Body = response.Body
                              RawSha256 = sha256 response.Body
                              NextLink = explicitNext
                              ItemCount = count
                              Terminal = nextRequest.IsNone }

                        match nextRequest with
                        | None -> Ok(List.rev (page :: acc))
                        | Some(nextPath, nextQuery) -> loop (index + 1) nextPath nextQuery (Set.union seen (Set.ofList pageIds)) (page :: acc))))

        loop 1 path (baseQuery @ [ "per_page", "100"; "page", "1" ]) Set.empty []

    let private collectIdentity (transport: ISinglePageGitHubTransport) (pass: int) (expected: RepositoryIdentity) =
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
                              Path = request.Path
                              Query = request.Query
                              Status = response.Status
                              Resource = resource
                              Body = response.Body
                              RawSha256 = sha256 response.Body
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
                if line.StartsWith("<!-- fsgg:intake:v1", StringComparison.Ordinal) then
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
                    elif line.StartsWith("<!-- fsgg:intake:v1", StringComparison.Ordinal) then
                        Error(MalformedCandidate(label, "intake receipt is bound to the wrong native origin"))
                    elif comment.Body.StartsWith("<!-- fsgg:done-receipt", StringComparison.Ordinal) then
                        Error(MalformedCandidate(label, "legacy done receipt marker is malformed"))
                    else Ok None)

    let private fingerprint (pages: RawPage list) (subjects: DraftSubject list) =
        [ yield!
              pages
              |> List.collect (fun page ->
                  [ page.Repository.FullName; string page.Repository.DatabaseId; page.Repository.NodeId
                    streamName page.Stream; string page.Index; page.Method; page.Path
                    page.Query |> List.collect (fun (key, value) -> [ key; value ]) |> String.concat "\u001f"
                    string page.Status; page.Resource; page.RawSha256; defaultArg page.NextLink ""
                    string page.ItemCount; string page.Terminal ])
          yield!
              subjects
              |> List.collect (fun item ->
                  [ item.Repository.FullName; string item.SubjectNumber; string item.SubjectIsPullRequest
                    item.NativeId; string item.Family; string item.Origin; item.CreatedAt; item.PayloadSha256; item.PayloadBlobSha
                    defaultArg item.SessionOperationId "" ]) ]
        |> List.map frame
        |> String.concat ""
        |> sha256

    let private collectRepository (transport: ISinglePageGitHubTransport) (apiBase: string) (pass: int) (horizon: DateTimeOffset) (repository: RepositoryIdentity) =
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
            | None ->
                let ordered =
                    subjects
                    |> List.sortBy (fun item -> item.Repository.FullName, item.SubjectNumber, item.CreatedAt, item.NativeId)
                let draft =
                    { ObservationHorizon = horizonText
                      Repositories = repositories
                      Subjects = ordered
                      EvidenceFingerprint = fingerprint pages ordered }
                Ok { Number = pass; Pages = pages; Draft = draft })

    let collectTwoPass transport apiBase observationHorizon =
        match Uri.TryCreate(apiBase, UriKind.Absolute) with
        | false, _ -> Error(InvalidResponse("collector", "apiBase must be an absolute URI"))
        | true, apiUri when apiUri.Scheme <> Uri.UriSchemeHttps && apiUri.Host <> "localhost" && apiUri.Host <> "127.0.0.1" ->
            Error(InvalidResponse("collector", "apiBase must use HTTPS outside local fixtures"))
        | true, _ ->
            parseInstant "observation horizon" observationHorizon
            |> Result.bind (fun horizon ->
                collectPass transport apiBase 1 observationHorizon horizon
                |> Result.bind (fun first ->
                    collectPass transport apiBase 2 observationHorizon horizon
                    |> Result.bind (fun second ->
                        if first.Draft.Repositories <> second.Draft.Repositories then
                            Error(RosterDrift "repository identity changed between passes")
                        elif first.Draft.Subjects <> second.Draft.Subjects then
                            Error(PassDrift "derived receipt subjects changed between passes")
                        elif first.Draft.EvidenceFingerprint <> second.Draft.EvidenceFingerprint then
                            Error(PassDrift "raw page evidence changed between passes")
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

    let bindV3Native transport apiBase expectedFamily expectedScope expectedObservationHorizon registryBytes
        (entry: FS.GG.Coord.HistoricalLossRegistry.EntryV3)
        (approval: FS.GG.Coord.HistoricalLossRegistry.NativeApprovalReadbackV2)
        : Result<FS.GG.Coord.HistoricalLossRegistry.BoundLossV3, string list> =
        let expectedRepositories = repositories |> List.map registryRepository
        // The collector owns both passes. No typed draft, page list, repository subset or
        // completeness claim is accepted from this method's caller.
        match collectTwoPass transport apiBase expectedObservationHorizon with
        | Error error -> Error [ $"historical-loss-native-census: %A{error}" ]
        | Ok capture ->
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
            check (entry.CensusFirst.Revision = approval.File.Revision && entry.CensusSecond.Revision = approval.File.Revision)
                "historical-loss-native-revision"
            check (entry.CensusFirst.Pages = firstPages && entry.CensusSecond.Pages = secondPages)
                "historical-loss-native-pages"
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
