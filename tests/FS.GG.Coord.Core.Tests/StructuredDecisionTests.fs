namespace FS.GG.Coord.Tests

open System
open System.Text.Json
open System.IO
open Xunit
open FS.GG.Coord

module StructuredDecisionTests =
    let route revision previous =
        let draft : StructuredDecision.RouteRecord =
            { Schema = StructuredDecision.RouteSchema
              Subject = "FS-GG/.github#42"
              Revision = revision
              PreviousDigest = previous
              Scope = [ "coordinate structured authorization" ]
              Dependencies = [ "FS-GG/.github#41" ]
              TouchSet = [ "src/FS.GG.Coord.Core/StructuredDecision.fs" ]
              PolicyVersion = StructuredDecision.PolicyVersion
              Route = Some DeliveryRoute.Lightweight
              Agent = "m4-worker"
              Timestamp = "2026-08-14T09:00:00Z"
              ReasonCodes = [ "structured-inputs" ]
              Rationale = "body prose is not authorization"
              SddWorkId = None
              SpecHome = None
              RequiredGates = []
              Digest = "" }
        { draft with Digest = StructuredDecision.routeDigest draft }

    let review revision previous kind verdict round initial preceding =
        let draft : StructuredDecision.ReviewRecord =
            { Schema = StructuredDecision.ReviewSchema
              Subject = "FS-GG/.github#42/pr/77"
              Revision = revision
              PreviousDigest = previous
              HeadSha = String.replicate 40 "a"
              Critic = "tern-42"
              Verdict = verdict
              AcceptedExceptions = []
              RouteApplicability = "not-meaningful"
              RouteEvidence = [ "coordination engine has no runtime route comparison" ]
              PolicyVersion = StructuredDecision.PolicyVersion
              Kind = kind
              Round = round
              InitialReview = initial
              PrecedingReview = preceding
              Timestamp = "2026-08-14T09:00:00Z"
              Digest = "" }
        { draft with Digest = StructuredDecision.reviewDigest draft }

    [<Fact>]
    let ``M4 route authorization is bound to structured scope dependencies touch-set policy and revision`` () =
        let record = route 1 None
        let changed = { record with Scope = [ "silently broader scope" ] }
        Assert.False(record.Digest = StructuredDecision.routeDigest changed)
        Assert.Equal(Ok record, StructuredDecision.validateRouteLedger record.Subject [ record ])

    [<Fact>]
    let ``M4 tampering with a persisted route record fails closed`` () =
        let record = route 1 None
        match StructuredDecision.validateRouteLedger record.Subject [ { record with TouchSet = [ "src/**" ] } ] with
        | Error errors -> Assert.Contains(errors, fun error -> error.Contains "digest does not match")
        | Ok _ -> failwith "tampered record was accepted"

    [<Fact>]
    let ``M4 append requires the exact previous digest and next revision`` () =
        let first = route 1 None
        let valid = route 2 (Some first.Digest)
        Assert.True(StructuredDecision.validateRouteLedger first.Subject [ first; valid ] |> Result.isOk)
        let stale = route 2 (Some(String.replicate 64 "0"))
        Assert.True(StructuredDecision.validateRouteLedger first.Subject [ first; stale ] |> Result.isError)
        let gap = route 3 (Some first.Digest)
        Assert.True(StructuredDecision.validateRouteLedger first.Subject [ first; gap ] |> Result.isError)

    [<Fact>]
    let ``M4 narrative body edits cannot affect a structured route`` () =
        let record = route 1 None
        let beforeBody = "Implement the bounded contract.\nPaths: src/A.fs"
        let afterBody = beforeBody + "\n\nEditorial clarification only."
        Assert.False((beforeBody = afterBody))
        Assert.Equal(Ok record, StructuredDecision.validateRouteLedger record.Subject [ record ])

    [<Fact>]
    let ``M4 checked-in active-item migration replay preserves authority across a body edit`` () =
        let fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "structured-decisions", "active-migration.json")
        use fixture = JsonDocument.Parse(File.ReadAllText fixturePath)
        let root = fixture.RootElement
        let structured = root.GetProperty "structured"
        let strings (name: string) =
            structured.GetProperty(name).EnumerateArray()
            |> Seq.map _.GetString()
            |> Seq.toList
        let draft =
            { route 1 None with
                Subject = root.GetProperty("subject").GetString()
                Scope = strings "scope"
                Dependencies = strings "dependencies"
                TouchSet = strings "touchSet"
                PolicyVersion = structured.GetProperty("policyVersion").GetString()
                Digest = "" }
        let migrated = { draft with Digest = StructuredDecision.routeDigest draft }
        let legacy = StructuredDecision.toLegacyReceipt migrated
        let classification =
            StructuredDecision.classifyRoute (Some legacy) (Some migrated)
            |> StructuredDecision.routeClassificationName
        Assert.Equal(root.GetProperty("expectedClassification").GetString(), classification)
        Assert.False(root.GetProperty("bodyBefore").GetString() = root.GetProperty("bodyAfter").GetString())
        Assert.Equal("none", root.GetProperty("expectedBodyEditEffect").GetString())
        Assert.Equal(Ok migrated, StructuredDecision.validateRouteLedger migrated.Subject [ migrated ])

    [<Fact>]
    let ``M4 legacy and structured route differences are explicitly classified`` () =
        let fresh = route 1 None
        let equivalent = StructuredDecision.toLegacyReceipt fresh
        Assert.Equal("legacy-only", StructuredDecision.classifyRoute (Some equivalent) None |> StructuredDecision.routeClassificationName)
        Assert.Equal("structured-only", StructuredDecision.classifyRoute None (Some fresh) |> StructuredDecision.routeClassificationName)
        Assert.Equal("equivalent", StructuredDecision.classifyRoute (Some equivalent) (Some fresh) |> StructuredDecision.routeClassificationName)
        let divergent = { equivalent with Route = Some DeliveryRoute.SddRequired }
        Assert.Equal("divergent", StructuredDecision.classifyRoute (Some divergent) (Some fresh) |> StructuredDecision.routeClassificationName)

    [<Fact>]
    let ``M4 review digest binds exact head critic verdict exceptions policy and revision`` () =
        let record = review 1 None StructuredDecision.Initial StructuredDecision.Pass 0 None None
        for changed in
            [ { record with HeadSha = String.replicate 40 "b" }
              { record with Critic = "another-critic" }
              { record with Verdict = StructuredDecision.ChangesRequired }
              { record with AcceptedExceptions = [ "EX-1" ] }
              { record with PolicyVersion = "other-policy" }
              { record with Revision = 2 } ] do
            Assert.False(record.Digest = StructuredDecision.reviewDigest changed)

    [<Fact>]
    let ``M4 review ledger rejects tamper stale links and non exact SHAs`` () =
        let first = review 1 None StructuredDecision.Initial StructuredDecision.Pass 0 None None
        let second = review 2 (Some first.Digest) StructuredDecision.Acceptance StructuredDecision.Accepted 0 (Some "https://review/1") (Some "https://review/1")
        Assert.True(StructuredDecision.validateReviewLedger first.Subject [ first; second ] |> Result.isOk)
        Assert.True(StructuredDecision.validateReviewLedger first.Subject [ first; { second with Digest = String.replicate 64 "0" } ] |> Result.isError)
        let shortHead = { first with HeadSha = "abc" }
        let shortHead = { shortHead with Digest = StructuredDecision.reviewDigest shortHead }
        Assert.True(StructuredDecision.validateReviewLedger first.Subject [ shortHead ] |> Result.isError)

    let reviewJson (record: StructuredDecision.ReviewRecord) =
        let kind = match record.Kind with StructuredDecision.Initial -> "initial" | StructuredDecision.Confirmation -> "confirmation" | StructuredDecision.Acceptance -> "acceptance"
        let verdict = match record.Verdict with StructuredDecision.Pass -> "pass" | StructuredDecision.ChangesRequired -> "changes-required" | StructuredDecision.Accepted -> "accepted"
        JsonSerializer.Serialize
            {| schema = record.Schema; subject = record.Subject; revision = record.Revision
               previousDigest = record.PreviousDigest; headSha = record.HeadSha; critic = record.Critic
               verdict = verdict; acceptedExceptions = record.AcceptedExceptions
               routeApplicability = record.RouteApplicability; routeEvidence = record.RouteEvidence
               policyVersion = record.PolicyVersion; kind = kind; round = record.Round
               initialReview = record.InitialReview; precedingReview = record.PrecedingReview
               timestamp = record.Timestamp; digest = record.Digest |}

    [<Fact>]
    let ``M4 representative active review migration projects a v2 record through the existing protocol`` () =
        let record = review 1 None StructuredDecision.Initial StructuredDecision.Pass 0 None None
        let comment : Driver.ReviewComment =
            { Id = 1L; Url = "https://review/1"; Body = "<!-- fsgg:review-decision/v2 -->\n" + reviewJson record }
        let facts = Driver.reviewPhaseFacts [ comment ]
        Assert.Empty facts.StructuredErrors
        Assert.True facts.InitialPresent
        Assert.Equal(Some record.HeadSha, facts.InitialHeadSha)
        Assert.Equal(Some record.Critic, facts.CriticIdentity)
        Assert.Equal("structured-only", (Driver.liveReviewComments record.HeadSha [ comment ]).EvidenceClassification)

    [<Fact>]
    let ``M4 legacy and structured review decisions classify equivalent and divergent fields`` () =
        let record = review 1 None StructuredDecision.Initial StructuredDecision.Pass 0 None None
        let structured : Driver.ReviewComment =
            { Id = 2L; Url = "https://review/v2"; Body = "<!-- fsgg:review-decision/v2 -->\n" + reviewJson record }
        let legacyBody verdict =
            $"<!-- fsgg:independent-review:v1 -->\ncritic: %s{record.Critic}\nreviewed-head: %s{record.HeadSha}\nverdict: %s{verdict}\nroute-applicability: not-meaningful\nroute-not-meaningful-reason: coordination engine has no runtime route comparison"
        let legacy verdict : Driver.ReviewComment =
            { Id = 1L; Url = "https://review/v1"; Body = legacyBody verdict }
        Assert.Equal("equivalent", (Driver.liveReviewComments record.HeadSha [ legacy "pass"; structured ]).EvidenceClassification)
        Assert.Equal("divergent", (Driver.liveReviewComments record.HeadSha [ legacy "changes-required"; structured ]).EvidenceClassification)

    [<Fact>]
    let ``M4 malformed v2 review never falls back to a passing legacy marker`` () =
        let legacy : Driver.ReviewComment =
            { Id = 1L; Url = "https://review/legacy"; Body = "<!-- fsgg:independent-review:v1 -->\ncritic: legacy\nreviewed-head: " + String.replicate 40 "a" + "\nverdict: pass" }
        let malformed : Driver.ReviewComment =
            { Id = 2L; Url = "https://review/v2"; Body = "<!-- fsgg:review-decision/v2 -->\n{}" }
        Assert.True(Driver.parseReviewComments [ legacy; malformed ] |> Result.isError)
        Assert.NotEmpty((Driver.reviewPhaseFacts [ legacy; malformed ]).StructuredErrors)
