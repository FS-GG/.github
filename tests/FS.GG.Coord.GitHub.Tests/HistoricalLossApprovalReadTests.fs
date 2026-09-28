module FS.GG.Coord.GitHub.Tests.HistoricalLossApprovalReadTests

open System
open System.Collections.Generic
open System.Security.Cryptography
open System.Text
open System.Text.Json
open Xunit
open FS.GG.Coord
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Errors
open FS.GG.Coord.GitHub.Transport

type private Fake(responses: IoResult<Response> list) =
    let queue = Queue<IoResult<Response>>(responses)
    let requests = ResizeArray<Request>()
    member _.Requests = List.ofSeq requests

    interface ISinglePageGitHubTransport with
        member _.SendSingle request =
            requests.Add request
            queue.Dequeue()

let private hex character count = String.replicate count (string character)
let private sha256 (bytes: byte array) = bytes |> SHA256.HashData |> Convert.ToHexString |> _.ToLowerInvariant()
let private shaText (value: string) = value |> Encoding.UTF8.GetBytes |> sha256

let private gitBlobSha (bytes: byte array) =
    Array.append (Encoding.ASCII.GetBytes($"blob {bytes.LongLength}\u0000")) bytes
    |> SHA1.HashData
    |> Convert.ToHexString
    |> _.ToLowerInvariant()

let private quoted value = JsonSerializer.Serialize(value: string)
let private baseSha = hex 'a' 40
let private headSha = hex 'b' 40
let private mergeSha = hex 'c' 40
let private cutoff = "2026-09-27T00:00:00.0000000+00:00"
let private scope = "FS-GG/FS.GG.Coordination"
let private subject = "FS-GG/.github#4000/pr/4001"

let private census =
    let draft: HistoricalLossRegistry.NativeCensus =
        {
            Scope = scope
            ObservedThrough = cutoff
            Revision = hex 'd' 40
            Complete = true
            Terminal = true
            DeclaredCount = 0
            RawBlobShas = [ hex 'e' 40 ]
            Survivors = []
            Digest = ""
        }

    { draft with Digest = HistoricalLossRegistry.censusDigest draft }

let private entry: HistoricalLossRegistry.EntryV2 =
    {
        Family = "delivery-receipt"
        Scope = scope
        Cutoff = cutoff
        AuditedSources =
            [
                {
                    ProducerId = "coordination-source-audit"
                    Revision = hex 'd' 40
                    Path = "src/parser.fs"
                    BlobSha = hex 'f' 40
                    BytesSha256 = hex '1' 64
                    Role = HistoricalLossRegistry.ProtectedParserOnly
                }
            ]
        CensusFirst = census
        CensusSecond = census
        Consequence = HistoricalLossRegistry.RequiredConsequence
        Approval =
            {
                Subject = subject
                PullRequest = 4001
                BaseSha = baseSha
                RegistryPath = "policy/historical-loss-registry.json"
            }
    }

let private roleText = function HistoricalLossRegistry.ProtectedParserOnly -> "protected-parser-only" | HistoricalLossRegistry.LocalCacheOnly -> "local-cache-only"

let private registryJson () =
    let source = entry.AuditedSources.Head
    let renderCensus (value: HistoricalLossRegistry.NativeCensus) =
        let blobs = value.RawBlobShas |> List.map quoted |> String.concat ","
        $"{{\"scope\":{quoted value.Scope},\"observedThrough\":{quoted value.ObservedThrough},\"revision\":{quoted value.Revision},\"complete\":true,\"terminal\":true,\"declaredCount\":0,\"rawBlobShas\":[{blobs}],\"survivors\":[],\"digest\":{quoted value.Digest}}}"

    $"{{\"schema\":{quoted HistoricalLossRegistry.SchemaV2},\"entries\":[{{\"family\":{quoted entry.Family},\"scope\":{quoted entry.Scope},\"cutoff\":{quoted entry.Cutoff},\"auditedSources\":[{{\"producerId\":{quoted source.ProducerId},\"revision\":{quoted source.Revision},\"path\":{quoted source.Path},\"blobSha\":{quoted source.BlobSha},\"bytesSha256\":{quoted source.BytesSha256},\"role\":{quoted (roleText source.Role)}}}],\"censusFirst\":{renderCensus entry.CensusFirst},\"censusSecond\":{renderCensus entry.CensusSecond},\"consequence\":{quoted entry.Consequence},\"approval\":{{\"subject\":{quoted subject},\"pullRequest\":4001,\"baseSha\":{quoted baseSha},\"registryPath\":{quoted entry.Approval.RegistryPath}}}}}]}}"

let private reseal record =
    let draft = { record with StructuredDecision.ReviewRecord.Digest = "" }
    { draft with Digest = StructuredDecision.reviewDigest draft }

let private reviews blobSha =
    let initial =
        ({
            Schema = StructuredDecision.ReviewSchema
            Subject = subject
            Revision = 1
            PreviousDigest = None
            HeadSha = headSha
            ClaimGeneration = None
            BaseSha = None
            Critic = "loss-reviewer-1"
            Verdict = StructuredDecision.Pass
            AcceptedExceptions = []
            RouteApplicability = "not-meaningful"
            RouteEvidence = [ "pure historical-loss evidence review" ]
            PolicyVersion = StructuredDecision.PolicyVersion
            Kind = StructuredDecision.Initial
            Round = 0
            InitialReview = None
            PrecedingReview = None
            DiffAuditRequired = false
            DiffAuditReceipts = []
            Succession = None
            RepairPhaseReceipt = None
            Timestamp = "2026-09-27T01:00:00.0000000+00:00"
            Digest = ""
        }: StructuredDecision.ReviewRecord)
        |> reseal

    let accepted =
        { initial with
            Revision = 2
            PreviousDigest = Some initial.Digest
            ClaimGeneration = Some "loss-approval-session-1"
            BaseSha = Some baseSha
            Verdict = StructuredDecision.Accepted
            Kind = StructuredDecision.Acceptance
            InitialReview = Some "https://github.com/FS-GG/.github/pull/4001#issuecomment-1"
            PrecedingReview = Some "https://github.com/FS-GG/.github/pull/4001#issuecomment-1"
            DiffAuditReceipts = [ "registry-blob:" + blobSha ]
            Timestamp = "2026-09-27T01:01:00.0000000+00:00"
            Digest = ""
        }
        |> reseal

    [ initial; accepted ]

let private issueComment id created body =
    $"{{\"id\":{id},\"node_id\":\"IC_{id}\",\"html_url\":\"https://github.com/FS-GG/.github/pull/4001#issuecomment-{id}\",\"created_at\":\"{created}\",\"body\":{quoted body}}}"

let private response body next =
    Ok { Status = 200; Body = body; Headers = Map.empty; ETag = None; NextLink = next }

type private Fixture =
    {
        Bytes: byte array
        Pull: IoResult<Response>
        ReviewPage: IoResult<Response>
        EnvelopePage: IoResult<Response>
        Contents: IoResult<Response>
        Blob: IoResult<Response>
    }

let private fixture () =
    let bytes = registryJson () |> Encoding.UTF8.GetBytes
    let blobSha = gitBlobSha bytes
    let decisions = reviews blobSha
    let reviewBodies =
        decisions
        |> List.mapi (fun index record ->
            let body = "<!-- fsgg:review-decision/v2 -->\n" + Driver.encodeStructuredReview record
            issueComment (index + 1) "2026-09-27T01:01:00.0000000+00:00" body)
    let acceptedDigest = decisions |> List.last |> _.Digest
    let envelopeBody =
        "<!-- fsgg:historical-loss-approval/v2 -->\n"
        + $"{{\"schema\":{quoted HistoricalLossRegistry.ApprovalEnvelopeSchemaV2},\"subject\":{quoted subject},\"pullRequest\":4001,\"baseSha\":{quoted baseSha},\"reviewedHeadSha\":{quoted headSha},\"mergeCommitSha\":{quoted mergeSha},\"registryPath\":{quoted entry.Approval.RegistryPath},\"registryBlobSha\":{quoted blobSha},\"registryBytesSha256\":{quoted (sha256 bytes)},\"acceptedReviewDigest\":{quoted acceptedDigest}}}"
    let next = "https://api.github.com/repos/FS-GG/.github/issues/4001/comments?sort=created&direction=asc&per_page=100&page=2"

    {
        Bytes = bytes
        Pull = response ($"{{\"number\":4001,\"state\":\"closed\",\"merged\":true,\"merged_at\":\"2026-09-27T01:02:00.0000000+00:00\",\"merge_commit_sha\":\"{mergeSha}\",\"head\":{{\"sha\":\"{headSha}\"}},\"base\":{{\"sha\":\"{baseSha}\",\"repo\":{{\"full_name\":\"FS-GG/.github\"}}}}}}") None
        ReviewPage = response ("[" + String.concat "," reviewBodies + "]") (Some next)
        EnvelopePage = response ("[" + issueComment 3 "2026-09-27T01:03:00.0000000+00:00" envelopeBody + "]") None
        Contents = response ($"{{\"type\":\"file\",\"path\":{quoted entry.Approval.RegistryPath},\"sha\":\"{blobSha}\",\"encoding\":\"base64\",\"size\":{bytes.Length},\"content\":\"{Convert.ToBase64String bytes}\"}}") None
        Blob = response ($"{{\"sha\":\"{blobSha}\",\"encoding\":\"base64\",\"size\":{bytes.Length},\"content\":\"{Convert.ToBase64String bytes}\"}}") None
    }

let private collect fake bytes =
    HistoricalLossApprovalRead.collectAndBind
        (fake :> ISinglePageGitHubTransport)
        "https://api.github.com"
        "FS-GG"
        ".github"
        entry.Family
        entry.Scope
        entry.Cutoff
        bytes
        entry

[<Fact>]
let ``v2 collector binds two terminal raw and typed passes`` () =
    let item = fixture ()
    let pass = [ item.Pull; item.ReviewPage; item.EnvelopePage; item.Contents; item.Blob ]
    let fake = Fake(pass @ pass)

    match collect fake item.Bytes with
    | Error error -> failwithf "%A" error
    | Ok capture ->
        Assert.Equal(5, capture.FirstPass.Length)
        Assert.Equal(5, capture.SecondPass.Length)
        Assert.Equal(64, capture.Fingerprint.Length)
        Assert.Equal(entry.Family, capture.BoundLoss.Family)
        Assert.Equal(10, fake.Requests.Length)
        Assert.All(fake.Requests, fun request -> Assert.Equal("GET", request.Method))
        Assert.All(fake.Requests, fun request -> Assert.Equal(NoBody, request.Body))

[<Fact>]
let ``v2 collector refuses incomplete or escaped comment pagination`` () =
    let item = fixture ()
    let escaped = response "[]" (Some "https://evil.invalid/repos/FS-GG/.github/issues/4001/comments?sort=created&direction=asc&per_page=100&page=2")
    let fake = Fake [ item.Pull; escaped ]

    match collect fake item.Bytes with
    | Error(Malformed(_, detail)) -> Assert.Contains("origin", detail)
    | result -> failwithf "expected pagination refusal, got %A" result

[<Fact>]
let ``v2 collector refuses duplicate native envelopes`` () =
    let item = fixture ()
    let envelope =
        match item.EnvelopePage with
        | Ok response -> response.Body.Trim('[', ']')
        | Error error -> failwithf "%A" error
    let secondEnvelope =
        envelope.Replace("\"id\":3", "\"id\":4").Replace("IC_3", "IC_4").Replace("issuecomment-3", "issuecomment-4")
    let fake = Fake [ item.Pull; response ($"[{envelope},{secondEnvelope}]") None ]

    match collect fake item.Bytes with
    | Error(Malformed(_, detail)) -> Assert.Contains("exactly one", detail)
    | result -> failwithf "expected uniqueness refusal, got %A" result

[<Fact>]
let ``v2 collector refuses permissions and ambiguous absence`` () =
    for status in [ 401; 403; 404 ] do
        let fake = Fake [ Ok { Status = status; Body = "denied"; Headers = Map.empty; ETag = None; NextLink = None } ]
        match collect fake (fixture ()).Bytes with
        | Error(Unauthorized _) when status = 401 || status = 403 -> ()
        | Error(NotFound _) when status = 404 -> ()
        | result -> failwithf "expected status %d refusal, got %A" status result

[<Fact>]
let ``v2 collector refuses raw or typed drift after a complete first pass`` () =
    let item = fixture ()
    let first = [ item.Pull; item.ReviewPage; item.EnvelopePage; item.Contents; item.Blob ]
    let changedPull =
        response ($"{{\"number\":4001,\"state\":\"closed\",\"merged\":true,\"merged_at\":\"2026-09-27T01:02:01.0000000+00:00\",\"merge_commit_sha\":\"{mergeSha}\",\"head\":{{\"sha\":\"{headSha}\"}},\"base\":{{\"sha\":\"{baseSha}\",\"repo\":{{\"full_name\":\"FS-GG/.github\"}}}}}}") None
    let second = [ changedPull; item.ReviewPage; item.EnvelopePage; item.Contents; item.Blob ]
    let fake = Fake(first @ second)

    match collect fake item.Bytes with
    | Error(Malformed(_, detail)) -> Assert.Contains("drifted", detail)
    | result -> failwithf "expected two-pass refusal, got %A" result
