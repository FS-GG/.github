namespace FS.GG.Coord.GitHub.Tests

open System
open System.Collections.Generic
open System.Security.Cryptography
open System.Text
open Xunit
open FS.GG.Coord
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Transport
open FS.GG.Coord.GitHub.HistoricalLossRetainedNativeCensus

module HistoricalLossRetainedNativeCensusTests =

    let private api = "https://api.github.test"
    let private at = "2026-09-27T00:00:00Z"
    let private horizon = "2026-09-28T00:00:00Z"
    let private head = String.replicate 40 "a"
    let private digest = String.replicate 64 "b"

    let private ok body next =
        { Status = 200
          Body = body
          Headers = Map [ "X-RateLimit-Resource", "core" ]
          ETag = None
          NextLink = next }

    let private query name (request: Request) =
        request.Query |> List.find (fst >> (=) name) |> snd

    let private repoForPath (path: string) =
        repositories
        |> List.find (fun repository -> path.StartsWith("repos/" + repository.FullName, StringComparison.Ordinal))

    let private issue id node number body isPull =
        let pull = if isPull then ",\"pull_request\":{}" else ""
        $"{{\"id\":{id},\"node_id\":\"{node}\",\"number\":{number},\"created_at\":\"{at}\",\"body\":{System.Text.Json.JsonSerializer.Serialize body}{pull}}}"

    let private comment id node number body repo =
        $"{{\"id\":{id},\"node_id\":\"{node}\",\"created_at\":\"{at}\",\"body\":{System.Text.Json.JsonSerializer.Serialize body},\"issue_url\":\"{api}/repos/{repo}/issues/{number}\"}}"

    type Fixture(?statusFor: Request -> int option, ?nextFor: Request -> string option option, ?bodyFor: (Request * int) -> string -> string) =
        let requests = ResizeArray<Request>()
        let calls = Dictionary<string, int>()
        let statusFor = defaultArg statusFor (fun _ -> None)
        let nextFor = defaultArg nextFor (fun _ -> None)
        let bodyFor = defaultArg bodyFor (fun _ body -> body)

        let defaultBody (request: Request) =
            let repository = repoForPath request.Path
            let page = request.Query |> List.tryFind (fst >> (=) "page") |> Option.map snd |> Option.defaultValue "1"
            let root = "repos/" + repository.FullName

            if request.Path = root then
                $"{{\"id\":{repository.DatabaseId},\"node_id\":\"{repository.NodeId}\",\"full_name\":\"{repository.FullName}\"}}"
            elif request.Path.EndsWith("/timeline", StringComparison.Ordinal) then
                "[{\"id\":9001}]"
            elif request.Path = root + "/issues" then
                if repository.FullName = "FS-GG/.github" then
                    "[" + issue 1001 "I_gh1" 1 "ordinary PR body" true + "]"
                elif repository.FullName = "FS-GG/Audio" then
                    "[" + issue 1501 "I_audio3" 3 "audio PR" true + "]"
                elif repository.FullName = "FS-GG/Rendering" then
                    let marker = $"<!-- fsgg:intake:v1 id=rendering-2 digest={digest} -->\nwork item"
                    "[" + issue 2001 "I_rendering2" 2 marker false + "]"
                else "[]"
            elif request.Path = root + "/pulls" then
                if repository.FullName = "FS-GG/.github" then "[{\"id\":3001,\"number\":1}]"
                elif repository.FullName = "FS-GG/Audio" then "[{\"id\":3501,\"number\":3}]"
                else "[]"
            elif request.Path = root + "/issues/comments" then
                if repository.FullName = "FS-GG/.github" then
                    let receipt = $"<!-- fsgg:delivery-receipt id=kit-1 head={head} evidence=https://example.test/a -->\n\nproof"
                    "[" + comment 4001 "IC_delivery" 1 receipt repository.FullName + "," + comment 4002 "IC_nonreceipt" 1 "quoted receipt example" repository.FullName + "]"
                elif repository.FullName = "FS-GG/Audio" then
                    let receipt = $"<!-- fsgg:delivery-receipt id=audio-3 head={head} evidence=https://example.test/audio -->\nproof"
                    "[" + comment 4501 "IC_audio_delivery" 3 receipt repository.FullName + "]"
                elif repository.FullName = "FS-GG/Rendering" then
                    "[" + comment 5001 "IC_done" 2 "<!-- fsgg:done-receipt v=1 -->\nverified" repository.FullName + "]"
                else "[]"
            elif request.Path = root + "/issues/events" && repository.FullName = "FS-GG/.github" && page = "1" then
                [ 1 .. 100 ] |> List.map (fun id -> $"{{\"id\":{6000 + id}}}") |> String.concat "," |> fun rows -> "[" + rows + "]"
            else "[]"

        interface ISinglePageGitHubTransport with
            member _.SendSingle request =
                requests.Add request
                let count = calls.GetValueOrDefault(request.Path, 0) + 1
                calls.[request.Path] <- count
                let body = bodyFor (request, count) (defaultBody request)

                match statusFor request with
                | Some status ->
                    Ok { (ok "{}" None) with Status = status }
                | None ->
                    let defaultNext =
                        if request.Path = "repos/FS-GG/.github/issues/events" && query "page" request = "1" then
                            Some $"{api}/repos/FS-GG/.github/issues/events?per_page=100&page=2"
                        else None
                    let next = defaultArg (nextFor request) defaultNext
                    Ok(ok body next)

        member _.Requests = List.ofSeq requests

    [<Fact>]
    let ``two stable native passes retain exact pages and derive only source-bound receipts`` () =
        let fixture = Fixture()

        match collectTwoPass fixture api horizon with
        | Error error -> failwithf "%A" error
        | Ok capture ->
            Assert.Equal(9, capture.First.Draft.Repositories.Length)
            Assert.Equal(4, capture.First.Draft.Subjects.Length)
            Assert.True(capture.First.Draft.Subjects = capture.Second.Draft.Subjects)
            Assert.Equal(capture.First.Draft.EvidenceFingerprint, capture.Second.Draft.EvidenceFingerprint)
            Assert.Contains(capture.First.Draft.Subjects, fun item -> item.Family = DeliveryReceipt && item.NativeId = "IC_delivery")
            Assert.Contains(capture.First.Draft.Subjects, fun item -> item.Family = IntakeReceipt && item.Origin = IssueBody)
            Assert.Contains(capture.First.Draft.Subjects, fun item -> item.Family = LegacyDoneReceipt && item.NativeId = "IC_done")
            Assert.DoesNotContain(capture.First.Draft.Subjects, fun item -> item.NativeId = "IC_nonreceipt")
            Assert.Contains(capture.First.Pages, fun page -> page.Stream = IssueEvents && page.Index = 2 && page.ItemCount = 0 && page.Terminal)
            Assert.Equal(3, capture.First.Pages |> List.filter (fun page -> match page.Stream with Timeline _ -> true | _ -> false) |> List.length)
            Assert.All(fixture.Requests, fun request -> Assert.Equal("GET", request.Method); Assert.Equal(NoBody, request.Body))

    [<Theory>]
    [<InlineData(401)>]
    [<InlineData(403)>]
    [<InlineData(404)>]
    let ``bounded authorization and absence statuses refuse instead of becoming empty streams`` status =
        let fixture = Fixture(statusFor = (fun request -> if request.Path.EndsWith("/issues", StringComparison.Ordinal) then Some status else None))
        match collectTwoPass fixture api horizon, status with
        | Error(Unauthorized _), 401
        | Error(Forbidden _), 403
        | Error(NotFound _), 404 -> ()
        | actual -> failwithf "unexpected result %A" actual

    [<Fact>]
    let ``foreign continuation refuses before a second page request`` () =
        let fixture = Fixture(nextFor = (fun request ->
            if request.Path = "repos/FS-GG/.github/issues/events" then Some(Some "https://evil.invalid/repos/FS-GG/.github/issues/events?per_page=100&page=2")
            else None))
        match collectTwoPass fixture api horizon with
        | Error(UnsafeContinuation _) -> ()
        | actual -> failwithf "unexpected result %A" actual

    [<Fact>]
    let ``marker-shaped malformed comment is rejected and never becomes a draft row`` () =
        let fixture = Fixture(bodyFor = (fun (request, _) body ->
            if request.Path = "repos/FS-GG/.github/issues/comments" then
                "[" + comment 4001 "IC_bad" 1 "<!-- fsgg:delivery-receipt id=kit head=NOT-A-SHA evidence=https://example.test/a -->" "FS-GG/.github" + "]"
            else body))
        match collectTwoPass fixture api horizon with
        | Error(MalformedCandidate _) -> ()
        | actual -> failwithf "unexpected result %A" actual

    [<Fact>]
    let ``duplicate native ids and changed second pass both fail closed`` () =
        let duplicate = Fixture(bodyFor = (fun (request, _) body ->
            if request.Path = "repos/FS-GG/Audio/issues/events" then "[{\"id\":1},{\"id\":1}]" else body))
        match collectTwoPass duplicate api horizon with
        | Error(DuplicateNativeId _) -> ()
        | actual -> failwithf "duplicate was not refused: %A" actual

        let drifting = Fixture(bodyFor = (fun (request, count) body ->
            if request.Path = "repos/FS-GG/.github/issues/comments" && count = 2 then body.Replace("proof", "changed proof") else body))
        match collectTwoPass drifting api horizon with
        | Error(PassDrift _) -> ()
        | actual -> failwithf "drift was not refused: %A" actual

    let private sha (bytes: byte array) = bytes |> SHA256.HashData |> Convert.ToHexString |> _.ToLowerInvariant()
    let private shaText (value: string) = value |> Encoding.UTF8.GetBytes |> sha
    let private blobSha (bytes: byte array) =
        Array.append (Encoding.ASCII.GetBytes($"blob {bytes.LongLength}\u0000")) bytes
        |> SHA1.HashData |> Convert.ToHexString |> _.ToLowerInvariant()
    let private quote (value: string) = System.Text.Json.JsonSerializer.Serialize value
    let private approvalSubject = "FS-GG/.github#4000/pr/4001"
    let private baseSha = String.replicate 40 "a"
    let private headSha = String.replicate 40 "b"
    let private mergeSha = String.replicate 40 "c"
    let private approvedHorizon = "2026-09-28T00:00:00.0000000+00:00"

    let private seal (record: StructuredDecision.ReviewRecord) =
        let draft = { record with Digest = "" }
        { draft with Digest = StructuredDecision.reviewDigest draft }

    let private reviews registryBlob =
        let initial: StructuredDecision.ReviewRecord =
            ({ Schema = StructuredDecision.ReviewSchema; Subject = approvalSubject; Revision = 1;
              PreviousDigest = None; HeadSha = headSha; ClaimGeneration = None; BaseSha = None;
              Critic = "loss-reviewer-1"; Verdict = StructuredDecision.Pass; AcceptedExceptions = [];
              RouteApplicability = "not-meaningful"; RouteEvidence = [ "pure historical-loss evidence review" ];
              PolicyVersion = StructuredDecision.PolicyVersion; Kind = StructuredDecision.Initial; Round = 0;
              InitialReview = None; PrecedingReview = None; DiffAuditRequired = false; DiffAuditReceipts = [];
              Succession = None; RepairPhaseReceipt = None; Timestamp = "2026-09-27T01:00:00.0000000+00:00";
              Digest = "" }: StructuredDecision.ReviewRecord)
            |> seal
        let accepted =
            { initial with Revision = 2; PreviousDigest = Some initial.Digest
                           ClaimGeneration = Some "loss-approval-session-1"; BaseSha = Some baseSha
                           Verdict = StructuredDecision.Accepted; Kind = StructuredDecision.Acceptance
                           InitialReview = Some "https://github.com/FS-GG/.github/pull/4001#issuecomment-1"
                           PrecedingReview = Some "https://github.com/FS-GG/.github/pull/4001#issuecomment-1"
                           DiffAuditReceipts = [ "registry-blob:" + registryBlob ]
                           Timestamp = "2026-09-27T01:01:00.0000000+00:00"; Digest = "" }
            |> seal
        let comment index record: HistoricalLossRegistry.NativeReviewComment =
            let body = "<!-- fsgg:review-decision/v2 -->\n" + Driver.encodeStructuredReview record
            { DatabaseId = int64 index; NodeId = $"IC_{index}"
              Url = $"https://github.com/FS-GG/.github/pull/4001#issuecomment-{index}"
              Body = body; BodySha256 = shaText body }
        [ comment 1 initial; comment 2 accepted ], accepted.Digest

    let private renderRegistry (entry: HistoricalLossRegistry.EntryV3) =
        let repo (r: HistoricalLossRegistry.RepositoryIdentityV3) =
            $"{{\"fullName\":{quote r.FullName},\"databaseId\":{r.DatabaseId},\"nodeId\":{quote r.NodeId}}}"
        let page (p: HistoricalLossRegistry.RetainedPageV3) =
            $"{{\"repository\":{repo p.Repository},\"index\":{p.Index},\"itemCount\":{p.ItemCount},\"rawSha256\":{quote p.RawSha256},\"terminal\":{p.Terminal.ToString().ToLowerInvariant()}}}"
        let subject (s: HistoricalLossRegistry.RetainedSubjectV3) =
            let operation = s.SessionOperationId |> Option.map quote |> Option.defaultValue "null"
            $"{{\"repository\":{repo s.Repository},\"nativeId\":{quote s.NativeId},\"family\":{quote s.Family},\"createdAt\":{quote s.CreatedAt},\"payloadBlobSha\":{quote s.PayloadBlobSha},\"sessionOperationId\":{operation},\"liveClaim\":{s.LiveClaim.ToString().ToLowerInvariant()}}}"
        let census (c: HistoricalLossRegistry.RetainedNativeCensusV3) =
            let rs = c.SelectedRepositories |> List.map repo |> String.concat ","
            let ps = c.Pages |> List.map page |> String.concat ","
            let ss = c.Subjects |> List.map subject |> String.concat ","
            $"{{\"selectedRepositories\":[{rs}],\"observationHorizon\":{quote c.ObservationHorizon},\"revision\":{quote c.Revision},\"enumeration\":\"direct-repository-enumeration\",\"complete\":true,\"declaredCount\":{c.DeclaredCount},\"pages\":[{ps}],\"subjects\":[{ss}],\"historicalEmissions\":\"unknown\",\"historicalDeletions\":\"unknown\",\"lostCount\":\"unknown\",\"producerDeploymentEnd\":\"unknown\",\"digest\":{quote c.Digest}}}"
        let source (s: HistoricalLossRegistry.RecoverySource) =
            let role = if s.Role = HistoricalLossRegistry.RecoveredWriterSource then "recovered-writer-source" else "protocol-authoring-source"
            $"{{\"producerId\":{quote s.ProducerId},\"revision\":{quote s.Revision},\"path\":{quote s.Path},\"blobSha\":{quote s.BlobSha},\"bytesSha256\":{quote s.BytesSha256},\"role\":{quote role}}}"
        let sources = entry.RecoverySources |> List.map source |> String.concat ","
        let known = entry.KnownSurvivorIds |> List.map quote |> String.concat ","
        $"{{\"schema\":{quote HistoricalLossRegistry.SchemaV3},\"entries\":[{{\"family\":{quote entry.Family},\"scope\":{quote entry.Scope},\"observationHorizon\":{quote entry.ObservationHorizon},\"recoverySources\":[{sources}],\"knownSurvivorIds\":[{known}],\"censusFirst\":{census entry.CensusFirst},\"censusSecond\":{census entry.CensusSecond},\"exclusionAppliesToLiveClaims\":false,\"consequence\":{quote entry.Consequence},\"approval\":{{\"subject\":{quote entry.Approval.Subject},\"pullRequest\":4001,\"baseSha\":{quote entry.Approval.BaseSha},\"registryPath\":{quote entry.Approval.RegistryPath}}}}}]}}"

    let private approvedFixture (entry: HistoricalLossRegistry.EntryV3) =
        let bytes = renderRegistry entry |> Encoding.UTF8.GetBytes
        let registryBlob = blobSha bytes
        let comments, acceptedDigest = reviews registryBlob
        let envelopeBody =
            "<!-- fsgg:historical-loss-approval/v2 -->\n"
            + $"{{\"schema\":{quote HistoricalLossRegistry.ApprovalEnvelopeSchemaV2},\"subject\":{quote approvalSubject},\"pullRequest\":4001,\"baseSha\":{quote baseSha},\"reviewedHeadSha\":{quote headSha},\"mergeCommitSha\":{quote mergeSha},\"registryPath\":{quote entry.Approval.RegistryPath},\"registryBlobSha\":{quote registryBlob},\"registryBytesSha256\":{quote (sha bytes)},\"acceptedReviewDigest\":{quote acceptedDigest}}}"
        let envelope: HistoricalLossRegistry.NativeApprovalEnvelopeCommentV2 =
            { DatabaseId = 3L; NodeId = "IC_3"; Url = "https://github.com/FS-GG/.github/pull/4001#issuecomment-3"
              CreatedAt = "2026-09-27T01:03:00.0000000+00:00"; Body = envelopeBody; BodySha256 = shaText envelopeBody }
        let pr: HistoricalLossRegistry.NativePullRequest =
            { Repository = "FS-GG/.github"; PullRequest = 4001; State = "closed"; Merged = true
              BaseSha = baseSha; HeadSha = headSha; MergeCommitSha = mergeSha }
        let merged: HistoricalLossRegistry.NativeMergedPullRequestV2 =
            { PullRequest = pr; MergedAt = "2026-09-27T01:02:00.0000000+00:00" }
        let native: HistoricalLossRegistry.NativeApprovalReadbackV2 =
            { PullRequestFirst = merged; PullRequestSecond = merged; ReviewCommentsFirst = comments; ReviewCommentsSecond = comments
              ReviewCommentsComplete = true; ReviewCommentsTerminal = true; ApprovalEnvelopeFirst = envelope; ApprovalEnvelopeSecond = envelope
              File = { Repository = "FS-GG/.github"; Path = entry.Approval.RegistryPath; Revision = mergeSha; BlobSha = registryBlob; Bytes = bytes; BytesSha256 = sha bytes }
              Blob = { Repository = "FS-GG/.github"; BlobSha = registryBlob; Bytes = bytes; BytesSha256 = sha bytes } }
        bytes, native

    let private censusFromCapture (capture: PassCapture) subjects =
        let selected: HistoricalLossRegistry.RepositoryIdentityV3 list =
            repositories |> List.map (fun r -> { FullName = r.FullName; DatabaseId = r.DatabaseId; NodeId = r.NodeId })
        let pages =
            capture.Pages |> List.groupBy _.Repository
            |> List.collect (fun (_, ps) -> ps |> List.mapi (fun index p ->
                ({ Repository = { FullName = p.Repository.FullName; DatabaseId = p.Repository.DatabaseId; NodeId = p.Repository.NodeId }
                   Index = index + 1; ItemCount = p.ItemCount; RawSha256 = p.RawSha256; Terminal = index = ps.Length - 1 }: HistoricalLossRegistry.RetainedPageV3)))
        let rows =
            subjects |> List.map (fun (s: DraftSubject) ->
                ({ Repository = { FullName = s.Repository.FullName; DatabaseId = s.Repository.DatabaseId; NodeId = s.Repository.NodeId }
                   NativeId = s.NativeId; Family = "delivery-receipt"; CreatedAt = s.CreatedAt; PayloadBlobSha = s.PayloadBlobSha
                   SessionOperationId = s.SessionOperationId; LiveClaim = false }: HistoricalLossRegistry.RetainedSubjectV3))
        let draft: HistoricalLossRegistry.RetainedNativeCensusV3 =
            { SelectedRepositories = selected; ObservationHorizon = approvedHorizon; Revision = mergeSha
              Enumeration = HistoricalLossRegistry.DirectRepositoryEnumeration; Complete = true; DeclaredCount = rows.Length
              Pages = pages; Subjects = rows; HistoricalEmissions = "unknown"; HistoricalDeletions = "unknown"
              LostCount = "unknown"; ProducerDeploymentEnd = "unknown"; Digest = "" }
        { draft with Digest = HistoricalLossRegistry.retainedCensusDigestV3 draft }

    let private nativeEntry (census: HistoricalLossRegistry.RetainedNativeCensusV3) =
        let source role producer: HistoricalLossRegistry.RecoverySource =
            { ProducerId = producer; Revision = String.replicate 40 "d"; Path = "docs/receipt-protocol.md"
              BlobSha = String.replicate 40 "e"; BytesSha256 = String.replicate 64 "f"; Role = role }
        ({ Family = "delivery-receipt"; Scope = "FS-GG/FS.GG.Coordination"; ObservationHorizon = approvedHorizon
           RecoverySources = [ source HistoricalLossRegistry.RecoveredWriterSource "writer"; source HistoricalLossRegistry.ProtocolAuthoringSource "protocol" ]
           KnownSurvivorIds = census.Subjects |> List.map _.NativeId; CensusFirst = census; CensusSecond = census
           ExclusionAppliesToLiveClaims = false; Consequence = HistoricalLossRegistry.RequiredConsequence
           Approval = { Subject = approvalSubject; PullRequest = 4001; BaseSha = baseSha; RegistryPath = "policy/historical-loss-registry.json" } }: HistoricalLossRegistry.EntryV3)

    [<Fact>]
    let ``verified native multi-repo census and detached approval bind while fabricated empty rows refuse`` () =
        let fixture = Fixture()
        let capture = match collectTwoPass fixture api approvedHorizon with Ok value -> value | Error error -> failwithf "%A" error
        let delivery = capture.First.Draft.Subjects |> List.filter (fun s -> s.Family = DeliveryReceipt)
        let census = censusFromCapture capture.First delivery
        let entry = nativeEntry census
        let bytes, approval = approvedFixture entry
        match bindV3Native fixture api entry.Family entry.Scope approvedHorizon bytes entry approval with
        | Ok bound -> Assert.Equal(2, bound.RetainedCount)
        | Error errors -> failwithf "valid native proof refused: %A" errors
        let emptyDraft = { census with Subjects = []; DeclaredCount = 0; Digest = "" }
        let empty = { emptyDraft with Digest = HistoricalLossRegistry.retainedCensusDigestV3 emptyDraft }
        let fabricated = { entry with CensusFirst = empty; CensusSecond = empty; KnownSurvivorIds = [] }
        let fabricatedBytes, fabricatedApproval = approvedFixture fabricated
        match bindV3Native fixture api fabricated.Family fabricated.Scope approvedHorizon fabricatedBytes fabricated fabricatedApproval with
        | Error errors -> Assert.Contains("historical-loss-native-subjects", errors)
        | Ok _ -> failwith "caller-supplied empty receipt census gained authority"

        let changedPageDraft =
            { census with Pages = { census.Pages.Head with RawSha256 = String.replicate 64 "0" } :: census.Pages.Tail; Digest = "" }
        let changedPage = { changedPageDraft with Digest = HistoricalLossRegistry.retainedCensusDigestV3 changedPageDraft }
        let alteredPageEntry = { entry with CensusFirst = changedPage; CensusSecond = changedPage }
        let changedPageBytes, changedPageApproval = approvedFixture alteredPageEntry
        match bindV3Native fixture api alteredPageEntry.Family alteredPageEntry.Scope approvedHorizon changedPageBytes alteredPageEntry changedPageApproval with
        | Error errors -> Assert.Contains("historical-loss-native-pages", errors)
        | Ok _ -> failwith "altered raw page hash gained authority"

        let changedSubjectDraft =
            { census with Subjects = { census.Subjects.Head with PayloadBlobSha = String.replicate 40 "0" } :: census.Subjects.Tail; Digest = "" }
        let changedSubject = { changedSubjectDraft with Digest = HistoricalLossRegistry.retainedCensusDigestV3 changedSubjectDraft }
        let alteredSubjectEntry = { entry with CensusFirst = changedSubject; CensusSecond = changedSubject }
        let changedSubjectBytes, changedSubjectApproval = approvedFixture alteredSubjectEntry
        match bindV3Native fixture api alteredSubjectEntry.Family alteredSubjectEntry.Scope approvedHorizon changedSubjectBytes alteredSubjectEntry changedSubjectApproval with
        | Error errors -> Assert.Contains("historical-loss-native-subjects", errors)
        | Ok _ -> failwith "substituted typed payload hash gained authority"
