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
              DiffAuditRequired = false
              DiffAuditReceipts = []
              Timestamp = "2026-08-14T09:00:00Z"
              Digest = "" }
        { draft with Digest = StructuredDecision.reviewDigest draft }

    [<Fact>]
    let ``M4 route authorization is bound to structured scope dependencies touch-set policy and revision`` () =
        let record = route 1 None
        let changed = { record with Scope = [ "silently broader scope" ] }
        Assert.False(record.Digest = StructuredDecision.routeDigest changed)
        Assert.Equal(Ok record, StructuredDecision.validateRouteLedger record.Subject [ record ])
        let missingDependencies = { record with Dependencies = []; Digest = "" }
        let missingDependencies = { missingDependencies with Digest = StructuredDecision.routeDigest missingDependencies }
        Assert.True(StructuredDecision.validateRouteLedger record.Subject [ missingDependencies ] |> Result.isError)

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
        Assert.False(root.GetProperty("bodyBefore").GetString() = root.GetProperty("bodyAfter").GetString())
        Assert.Equal("none", root.GetProperty("expectedBodyEditEffect").GetString())
        Assert.Equal(Ok migrated, StructuredDecision.validateRouteLedger migrated.Subject [ migrated ])

    [<Fact>]
    let ``M6 effective route is derived only from the validated structured record`` () =
        let fresh = route 1 None
        let effective = StructuredDecision.toEffectiveRoute fresh
        Assert.Equal(fresh.Digest, effective.SubjectRevision)
        Assert.Equal(fresh.Route, effective.Route)
        Assert.Equal(DeliveryRoute.Schema, effective.Schema)

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
        let kind =
            match record.Kind with
            | StructuredDecision.Initial -> "initial"
            | StructuredDecision.Confirmation -> "confirmation"
            | StructuredDecision.Escalation -> "escalation"
            | StructuredDecision.RepairPhase -> "repair-phase"
            | StructuredDecision.Acceptance -> "acceptance"
        let verdict = match record.Verdict with StructuredDecision.Pass -> "pass" | StructuredDecision.ChangesRequired -> "changes-required" | StructuredDecision.Accepted -> "accepted"
        JsonSerializer.Serialize
            {| schema = record.Schema; subject = record.Subject; revision = record.Revision
               previousDigest = record.PreviousDigest; headSha = record.HeadSha; critic = record.Critic
               verdict = verdict; acceptedExceptions = record.AcceptedExceptions
               routeApplicability = record.RouteApplicability; routeEvidence = record.RouteEvidence
               policyVersion = record.PolicyVersion; kind = kind; round = record.Round
               initialReview = record.InitialReview; precedingReview = record.PrecedingReview
               diffAuditRequired = record.DiffAuditRequired; diffAuditReceipts = record.DiffAuditReceipts
               timestamp = record.Timestamp; digest = record.Digest |}

    let reseal (record: StructuredDecision.ReviewRecord) =
        let draft = { record with Digest = "" }
        { draft with Digest = StructuredDecision.reviewDigest draft }

    let reviewComment id (record: StructuredDecision.ReviewRecord) : Driver.ReviewComment =
        { Id = id
          Url = $"https://review/%d{id}"
          Body = "<!-- fsgg:review-decision/v2 -->\n" + reviewJson record }

    let acceptedChain () =
        let initial = review 1 None StructuredDecision.Initial StructuredDecision.Pass 0 None None
        let acceptance =
            review 2 (Some initial.Digest) StructuredDecision.Acceptance StructuredDecision.Accepted 0
                (Some "https://review/1") (Some "https://review/1")
        initial, acceptance, [ reviewComment 1L initial; reviewComment 2L acceptance ]

    let expectOk = function
        | Ok value -> value
        | Error errors -> failwithf "expected success, got %A" errors

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
        Assert.Empty((Driver.liveReviewComments record.HeadSha [ comment ]).StructuredErrors)

    [<Fact>]
    let ``M6 prose-only review comments never become authority beside structured evidence`` () =
        let record = review 1 None StructuredDecision.Initial StructuredDecision.Pass 0 None None
        let structured : Driver.ReviewComment =
            { Id = 2L; Url = "https://review/v2"; Body = "<!-- fsgg:review-decision/v2 -->\n" + reviewJson record }
        let retiredMarker = "<!-- fsgg:independent-review" + ":v1 -->"
        let legacyBody verdict =
            $"%s{retiredMarker}\ncritic: %s{record.Critic}\nreviewed-head: %s{record.HeadSha}\nverdict: %s{verdict}\nroute-applicability: not-meaningful\nroute-not-meaningful-reason: coordination engine has no runtime route comparison"
        let legacy verdict : Driver.ReviewComment =
            { Id = 1L; Url = "https://review/v1"; Body = legacyBody verdict }
        let pass = Driver.liveReviewComments record.HeadSha [ legacy "pass"; structured ]
        let conflict = Driver.liveReviewComments record.HeadSha [ legacy "changes-required"; structured ]
        Assert.Empty pass.StructuredErrors
        Assert.Empty conflict.StructuredErrors
        Assert.Equal(1, pass.Live.Length)
        Assert.Equal(1, conflict.Live.Length)

    [<Fact>]
    let ``M4 malformed v2 review never falls back to a passing legacy marker`` () =
        let retiredMarker = "<!-- fsgg:independent-review" + ":v1 -->"
        let legacy : Driver.ReviewComment =
            { Id = 1L; Url = "https://review/legacy"; Body = retiredMarker + "\ncritic: legacy\nreviewed-head: " + String.replicate 40 "a" + "\nverdict: pass" }
        let malformed : Driver.ReviewComment =
            { Id = 2L; Url = "https://review/v2"; Body = "<!-- fsgg:review-decision/v2 -->\n{}" }
        Assert.True(Driver.parseReviewComments [ legacy; malformed ] |> Result.isError)
        Assert.NotEmpty((Driver.reviewPhaseFacts [ legacy; malformed ]).StructuredErrors)

    [<Fact>]
    let ``M4 accepted structured generation is retired after head movement and fresh review`` () =
        let seal (record: StructuredDecision.ReviewRecord) =
            { record with Digest = StructuredDecision.reviewDigest { record with Digest = "" } }
        let initialA = review 1 None StructuredDecision.Initial StructuredDecision.Pass 0 None None
        let acceptedA =
            review 2 (Some initialA.Digest) StructuredDecision.Acceptance StructuredDecision.Accepted 0
                (Some "https://review/1") (Some "https://review/1")
        let initialB =
            { review 3 (Some acceptedA.Digest) StructuredDecision.Initial StructuredDecision.Pass 0 None None with
                HeadSha = String.replicate 40 "b"; Critic = "tern-43"; Digest = "" }
            |> seal
        let acceptedB =
            { review 4 (Some initialB.Digest) StructuredDecision.Acceptance StructuredDecision.Accepted 0
                (Some "https://review/3") (Some "https://review/3") with
                HeadSha = initialB.HeadSha; Critic = initialB.Critic; Digest = "" }
            |> seal
        let records = [ initialA; acceptedA; initialB; acceptedB ]
        Assert.True(StructuredDecision.validateReviewLedger initialA.Subject records |> Result.isOk)
        let comments =
            records
            |> List.mapi (fun index record ->
                let id = int64 (index + 1)
                ({ Id = id; Url = $"https://review/%d{index + 1}"
                   Body = "<!-- fsgg:review-decision/v2 -->\n" + reviewJson record }: Driver.ReviewComment))
        let live = Driver.liveReviewComments initialB.HeadSha comments
        Assert.Single live.Retired |> ignore
        Assert.Equal(2, live.Live.Length)
        Assert.True(Driver.parseEffectiveReviewComments initialB.HeadSha comments |> Result.isOk)
        let unmoved = { initialB with HeadSha = initialA.HeadSha; Digest = "" } |> seal
        Assert.True(StructuredDecision.validateReviewLedger initialA.Subject [ initialA; acceptedA; unmoved ] |> Result.isError)

    [<Fact>]
    let m6_structured_chain_drives_effective_state () =
        let initial = review 1 None StructuredDecision.Initial StructuredDecision.ChangesRequired 0 None None
        let confirmation =
            review 2 (Some initial.Digest) StructuredDecision.Confirmation StructuredDecision.Pass 1
                (Some "https://review/1") (Some "https://review/1")
        let acceptance =
            review 3 (Some confirmation.Digest) StructuredDecision.Acceptance StructuredDecision.Accepted 0
                (Some "https://review/1") (Some "https://review/2")
        let comments = [ reviewComment 1L initial; reviewComment 2L confirmation; reviewComment 3L acceptance ]
        let parsed = Driver.parseEffectiveReviewComments confirmation.HeadSha comments |> expectOk
        Assert.Equal(Some initial.Critic, parsed.CriticIdentity)
        Assert.Equal(Some confirmation.HeadSha, parsed.HeadSha)
        Assert.Equal<int list>([ 1 ], parsed.Rounds)
        Assert.True parsed.HostAccepted

    [<Fact>]
    let m6_v1_prose_chain_is_inert () =
        let retiredReview = "<!-- fsgg:independent-review" + ":v1 -->"
        let retiredAcceptance = "<!-- fsgg:review-accepted" + ":v1 -->"
        let prose : Driver.ReviewComment =
            { Id = 1L
              Url = "https://review/legacy"
              Body = retiredReview + "\ncritic: minted\nreviewed-head: 0123456789012345678901234567890123456789\nverdict: pass\n" + retiredAcceptance }
        let facts = Driver.reviewPhaseFacts [ prose ]
        Assert.False facts.InitialPresent
        Assert.False facts.AcceptancePresent
        Assert.NotEmpty facts.StructuredErrors
        Assert.True(Driver.parseEffectiveReviewComments (String.replicate 40 "0") [ prose ] |> Result.isError)

    [<Fact>]
    let m6_structured_fields_are_content_addressed () =
        let original = review 1 None StructuredDecision.Initial StructuredDecision.Pass 0 None None
        let mutations =
            [ { original with Subject = "FS-GG/.github#42/pr/78" }
              { original with RouteApplicability = "meaningful" }
              { original with RouteEvidence = [ "changed" ] }
              { original with Kind = StructuredDecision.Escalation }
              { original with Round = 1 }
              { original with InitialReview = Some "https://review/0" }
              { original with PrecedingReview = Some "https://review/0" }
              { original with DiffAuditRequired = true }
              { original with DiffAuditReceipts = [ "receipt" ] }
              { original with Timestamp = "2026-08-14T09:00:01Z" } ]
        mutations |> List.iter (fun changed -> Assert.NotEqual<string>(original.Digest, StructuredDecision.reviewDigest changed))

    [<Fact>]
    let m6_ledger_refuses_gaps_stale_links_subjects_and_critics () =
        let initial = review 1 None StructuredDecision.Initial StructuredDecision.ChangesRequired 0 None None
        let confirmation =
            review 2 (Some initial.Digest) StructuredDecision.Confirmation StructuredDecision.Pass 1
                (Some "https://review/1") (Some "https://review/1")
        let invalid =
            [ { confirmation with Revision = 3 } |> reseal
              { confirmation with PreviousDigest = Some(String.replicate 64 "0") } |> reseal
              { confirmation with Subject = "FS-GG/.github#42/pr/78" } |> reseal
              { confirmation with Critic = "different-critic" } |> reseal
              { confirmation with Round = 2 } |> reseal
              { confirmation with InitialReview = None } |> reseal
              { confirmation with PrecedingReview = None } |> reseal ]
        invalid
        |> List.iter (fun record ->
            Assert.True(StructuredDecision.validateReviewLedger initial.Subject [ initial; record ] |> Result.isError))

    [<Fact>]
    let m6_generic_critic_and_wrong_acceptance_links_fail () =
        let initial, acceptance, _ = acceptedChain ()
        let generic = { initial with Critic = "fsgg-critic-review"; Digest = "" } |> reseal
        let genericAcceptance =
            { acceptance with PreviousDigest = Some generic.Digest; Critic = generic.Critic; Digest = "" } |> reseal
        Assert.True(Driver.parseReviewComments [ reviewComment 1L generic; reviewComment 2L genericAcceptance ] |> Result.isError)
        for changed in
            [ { acceptance with HeadSha = String.replicate 40 "b" }
              { acceptance with InitialReview = Some "https://review/wrong" }
              { acceptance with PrecedingReview = Some "https://review/wrong" } ] do
            let changed = { changed with Digest = "" } |> reseal
            Assert.True(Driver.parseReviewComments [ reviewComment 1L initial; reviewComment 2L changed ] |> Result.isError)

    [<Fact>]
    let m6_host_acceptance_must_follow_critic () =
        let initial, acceptance, _ = acceptedChain ()
        match Driver.parseReviewComments [ reviewComment 2L initial; reviewComment 1L acceptance ] with
        | Error errors -> Assert.Contains(errors, fun error -> error.Contains "must follow")
        | Ok _ -> failwith "out-of-order host acceptance was authorized"

    [<Fact>]
    let m6_escalation_requires_typed_repair_phase () =
        let initial = review 1 None StructuredDecision.Initial StructuredDecision.ChangesRequired 0 None None
        let escalation =
            review 2 (Some initial.Digest) StructuredDecision.Escalation StructuredDecision.ChangesRequired 0
                (Some "https://review/1") (Some "https://review/1")
        let acceptance =
            review 3 (Some escalation.Digest) StructuredDecision.Acceptance StructuredDecision.Accepted 0
                (Some "https://review/1") (Some "https://review/1")
        Assert.True(
            Driver.parseReviewComments
                [ reviewComment 1L initial; reviewComment 2L escalation; reviewComment 3L acceptance ]
            |> Result.isError)

    [<Fact>]
    let m6_typed_escalation_and_repair_drive_state () =
        let initial = review 1 None StructuredDecision.Initial StructuredDecision.ChangesRequired 0 None None
        let escalation =
            review 2 (Some initial.Digest) StructuredDecision.Escalation StructuredDecision.ChangesRequired 0
                (Some "https://review/1") (Some "https://review/1")
        let repair =
            review 3 (Some escalation.Digest) StructuredDecision.RepairPhase StructuredDecision.ChangesRequired 0
                (Some "https://review/1") (Some "https://review/2")
        let confirmation =
            review 4 (Some repair.Digest) StructuredDecision.Confirmation StructuredDecision.Pass 1
                (Some "https://review/1") (Some "https://review/3")
        let acceptance =
            review 5 (Some confirmation.Digest) StructuredDecision.Acceptance StructuredDecision.Accepted 0
                (Some "https://review/1") (Some "https://review/4")
        let comments =
            [ reviewComment 1L initial; reviewComment 2L escalation; reviewComment 3L repair
              reviewComment 4L confirmation; reviewComment 5L acceptance ]
        let facts = Driver.reviewPhaseFacts comments
        Assert.True facts.EscalationPresent
        Assert.True facts.RepairPhasePresent
        let parsed = Driver.parseReviewComments comments |> expectOk
        Assert.True parsed.RepairPhase
        Assert.True parsed.HostAccepted

    [<Fact>]
    let m6_missing_malformed_and_misplaced_audit_fails () =
        let initial, acceptance, _ = acceptedChain ()
        let required = { initial with DiffAuditRequired = true; Digest = "" } |> reseal
        let accepted = { acceptance with PreviousDigest = Some required.Digest; Digest = "" } |> reseal
        Assert.True(Driver.parseReviewComments [ reviewComment 1L required; reviewComment 2L accepted ] |> Result.isError)
        let malformed = { accepted with DiffAuditReceipts = [ "not-base64" ]; Digest = "" } |> reseal
        Assert.True(Driver.parseReviewComments [ reviewComment 1L required; reviewComment 2L malformed ] |> Result.isError)
        let misplaced = { required with DiffAuditReceipts = [ "not-base64" ]; Digest = "" } |> reseal
        Assert.True(StructuredDecision.validateReviewLedger required.Subject [ misplaced ] |> Result.isError)

    [<Fact>]
    let m6_moved_head_parses_only_new_live_generation () =
        let initialA, acceptanceA, _ = acceptedChain ()
        let initialB =
            { review 3 (Some acceptanceA.Digest) StructuredDecision.Initial StructuredDecision.Pass 0 None None with
                HeadSha = String.replicate 40 "b"; Critic = "tern-43"; Digest = "" }
            |> reseal
        let acceptanceB =
            { review 4 (Some initialB.Digest) StructuredDecision.Acceptance StructuredDecision.Accepted 0
                (Some "https://review/3") (Some "https://review/3") with
                HeadSha = initialB.HeadSha; Critic = initialB.Critic; Digest = "" }
            |> reseal
        let comments =
            [ reviewComment 1L initialA; reviewComment 2L acceptanceA
              reviewComment 3L initialB; reviewComment 4L acceptanceB ]
        let parsed = Driver.parseEffectiveReviewComments initialB.HeadSha comments |> expectOk
        Assert.Equal(Some initialB.HeadSha, parsed.HeadSha)
        Assert.Equal(Some initialB.Critic, parsed.CriticIdentity)
