namespace FS.GG.Coord.GitHub

module OwnerContinuationReads =
    open System
    open System.Text
    open System.Text.Json
    open FS.GG.Coord
    open FS.GG.Coord.OwnerContinuation
    open V2ProjectionSource
    open Transport

    let private text name (value: JsonElement) = value.GetProperty(name: string).GetString()
    let private strict (names: string list) (value: JsonElement) =
        let fields = value.EnumerateObject() |> Seq.map _.Name |> Seq.toList
        if fields.Length <> names.Length || Set.ofList fields <> Set.ofList names then invalidArg "input" "closed schema required"
    let private parse (json: string) =
        if Encoding.UTF8.GetByteCount json > 262144 then invalidArg "input" "bounded JSON required"
        let rec unique (value: JsonElement) =
            match value.ValueKind with
            | JsonValueKind.Object ->
                let fields = value.EnumerateObject() |> Seq.toList
                if fields.Length <> (fields |> List.map _.Name |> Set.ofList |> Set.count) then invalidArg "input" "duplicate field"
                fields |> List.iter (fun f -> unique f.Value)
            | JsonValueKind.Array -> value.EnumerateArray() |> Seq.iter unique
            | _ -> ()
        use document = JsonDocument.Parse(json, JsonDocumentOptions(MaxDepth = 16))
        unique document.RootElement
        document.RootElement.Clone()

    let collect (transport: IGitHubTransport) (report: InspectionReport) (context: SelectionContext) (now: DateTimeOffset) (request: OwnerContinuation.Request) =
        let fact predicate value evidence proof : OwnerContinuation.Fact =
            { Id = "native-envelope-" + predicateName predicate; Subject = request.Subject; Predicate = predicate; Value = value
              Support = NativeObservation("OwnerContinuationReads/1", proof); ObservedAt = now; ValidUntil = now.AddMinutes 5.0; Evidence = evidence; Supersedes = []; ArtifactRevision = None }
        match request.Envelope with
        | None -> []
        | Some reference ->
            let gap () = [fact EnvelopeCurrent (Unknown "native-envelope-policy-or-identity-gap") [reference.NodeId] reference.BodySha256]
            try
                let slice = context.Slices |> List.find (fun s -> continuationIdentity s = request.Subject)
                let immutableIdentity = continuationEnvelopeIdentity report slice
                let parts = slice.Repository.Split('/')
                // Source policy is always the canonical hub policy, including for receiver subjects.
                let document = "query($id: ID!, $subjectId: ID!, $owner: String!, $repo: String!) { viewer { id } subject: node(id: $subjectId) { ... on Issue { id parent { id number repository { nameWithOwner } } } } node(id: $id) { ... on IssueComment { id body updatedAt lastEditedAt author { login } editor { login } issue { number repository { nameWithOwner } } } } repository(owner: $owner, name: $repo) { nameWithOwner policy: object(expression: \"main:scripts/root-identity-policy.json\") { ... on Blob { text } } } rateLimit { cost remaining } }"
                let nativeRequest = { Method = "POST"; Path = "graphql"; Query = []; Body = Query(document, ["id", VId reference.NodeId; "subjectId", VId slice.Join.NativeId; "owner", VString report.Binding.Owner; "repo", VString ".github"]); Budget = GraphQl; IfNoneMatch = None; Subject = "owner-continuation-native-envelope" }
                let native = GraphQl.read transport nativeRequest (fun data ->
                    try Ok(parse (data.GetRawText())) with _ -> Error(Errors.Malformed("owner envelope", "bounded native envelope unavailable")))
                match native, RootIdentityPolicy.policy with
                | Ok data, Ok policy ->
                    let node = data.GetProperty "node"
                    let body = text "body" node
                    let issue = node.GetProperty "issue"
                    let author = node.GetProperty "author" |> text "login"
                    let editor = node.GetProperty "editor"
                    let edited = node.GetProperty("lastEditedAt").ValueKind <> JsonValueKind.Null
                    let root login = policy.RootLogins |> Set.exists (fun value -> String.Equals(value, login, StringComparison.OrdinalIgnoreCase))
                    let currentPolicy = data.GetProperty("repository").GetProperty("policy") |> text "text"
                    let marker = "<!-- fsgg:owner-continuation-envelope/v1 -->\n"
                    let issueUrl (native: JsonElement) = "https://github.com/" + text "nameWithOwner" (native.GetProperty "repository") + "/issues/" + string (native.GetProperty("number").GetInt32())
                    let authorityUrl = issueUrl issue
                    let subject = data.GetProperty "subject"
                    let parent = subject.GetProperty "parent"
                    let nativeParent = text "id" subject = slice.Join.NativeId && parent.ValueKind <> JsonValueKind.Null && issueUrl parent = authorityUrl
                    let nativePlan = report.Items |> List.exists (fun item -> item.Evidence.Issue = slice.Issue && item.Evidence.PlanObservation |> Option.exists (fun plan -> plan.Url = authorityUrl))
                    if text "id" node <> reference.NodeId || text "updatedAt" node <> reference.UpdatedAt || digest body <> reference.BodySha256
                       || not (root author) || (edited && (editor.ValueKind = JsonValueKind.Null || not (root (text "login" editor))))
                       || not (nativeParent || nativePlan)
                       || digest currentPolicy <> policy.Sha256 || not (body.StartsWith(marker, StringComparison.Ordinal)) then gap ()
                    else
                        let envelope = parse (body.Substring marker.Length)
                        strict ["schema";"policy";"rootPolicySha256";"subject";"owner";"sliceId";"attempt";"revision";"scopeDigest";"head";"issuedAt";"expiresAt";"principalId";"invocation";"writeDomains";"original";"runId";"runAttempt";"steps";"immutableInputsSha256"] envelope
                        let issued = envelope.GetProperty("issuedAt").GetDateTimeOffset()
                        let expires = envelope.GetProperty("expiresAt").GetDateTimeOffset()
                        let strings field = envelope.GetProperty(field: string).EnumerateArray() |> Seq.map _.GetString() |> Seq.toList
                        let domains = slice.Paths |> List.map (fun path -> slice.Repository + ":" + path) |> List.sort
                        let original = text "original" envelope
                        let run = envelope.GetProperty("runId").GetInt64()
                        let attempt = envelope.GetProperty("runAttempt").GetInt32()
                        let bound =
                            text "schema" envelope = "fsgg.coord.owner-envelope/1" && text "policy" envelope = "adr0096-finite-sequence"
                            && text "rootPolicySha256" envelope = policy.Sha256 && text "subject" envelope = request.Subject.Subject
                            && text "owner" envelope = request.Subject.Owner && text "sliceId" envelope = request.Subject.Slice
                            && text "attempt" envelope = request.Subject.Attempt && text "revision" envelope = immutableIdentity.Revision
                            && text "scopeDigest" envelope = immutableIdentity.ScopeDigest && Some(text "head" envelope) = slice.Head
                            && text "principalId" envelope = (data.GetProperty "viewer" |> text "id")
                            && text "invocation" envelope = context.Invocation && text "immutableInputsSha256" envelope = continuationEnvelopeInputDigest report context slice
                            && List.sort (strings "writeDomains") = domains && strings "steps" = ["observe-ci"; "merge-pr"]
                            && Some original = request.Original && run > 0L && attempt > 0 && issued <= now && issued < expires
                        let valid = bound && now <= expires
                        let envelopeFact = fact EnvelopeCurrent (Known valid) [reference.NodeId; original] reference.BodySha256
                        let originalBound = text "subject" envelope = request.Subject.Subject && text "owner" envelope = request.Subject.Owner
                                            && text "sliceId" envelope = request.Subject.Slice && text "attempt" envelope = request.Subject.Attempt
                                            && Some(text "head" envelope) = slice.Head && Some original = request.Original && run > 0L && attempt > 0
                        // Current execution authority can lapse without erasing readback of the exact original.
                        if not originalBound then [envelopeFact]
                        else
                            let path = $"repos/{parts[0]}/{parts[1]}/actions/runs/{run}/attempts/{attempt}"
                            let read = { Method = "GET"; Path = path; Query = []; Body = NoBody; Budget = Rest; IfNoneMatch = None; Subject = "owner-continuation-original-readback" }
                            match transport.Send read with
                            | Ok response when response.Status = 200 && response.NextLink.IsNone ->
                                let native = parse response.Body
                                let exact = native.GetProperty("id").GetInt64() = run && native.GetProperty("run_attempt").GetInt32() = attempt
                                            && Some(text "head_sha" native) = slice.Head && text "full_name" (native.GetProperty "repository") = slice.Repository
                                let terminal = exact && text "status" native = "completed"
                                let success = terminal && text "conclusion" native = "success"
                                let failure = terminal && not success
                                let proof = digest response.Body
                                [envelopeFact; fact OriginalSucceeded (Known success) [original] proof; fact OriginalFailed (Known failure) [original] proof]
                            | _ -> [envelopeFact; fact OriginalSucceeded (Unknown "original-native-readback-unavailable") [original] reference.BodySha256]
                | _ -> gap ()
            with _ -> gap ()
