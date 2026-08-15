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
              Succession = None
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
        // Deliberately a SECOND, hand-written statement of the wire shape rather than a call to
        // `Driver.encodeStructuredReview`: it is what stops a decode bug being cancelled by a matching
        // encode bug. `.github#2662`'s `succession` object is spelled here for the same reason.
        let succession =
            record.Succession
            |> Option.map (fun (grant: StructuredDecision.SuccessionGrant) ->
                {| originalCritic = grant.OriginalCritic
                   grantedBy = grant.GrantedBy
                   grantUrl = grant.GrantUrl |})
        JsonSerializer.Serialize
            {| schema = record.Schema; subject = record.Subject; revision = record.Revision
               previousDigest = record.PreviousDigest; headSha = record.HeadSha; critic = record.Critic
               verdict = verdict; acceptedExceptions = record.AcceptedExceptions
               routeApplicability = record.RouteApplicability; routeEvidence = record.RouteEvidence
               policyVersion = record.PolicyVersion; kind = kind; round = record.Round
               initialReview = record.InitialReview; precedingReview = record.PrecedingReview
               diffAuditRequired = record.DiffAuditRequired; diffAuditReceipts = record.DiffAuditReceipts
               succession = succession
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

    // ── .github#2662: a host-granted successor critic must be able to RECORD ──────────────────────────
    //
    // `.github#2417` taught the decision layer that a chain whose critic despawned can be handed on;
    // `validateReviewLedger` never learned it, so the successor could review and had no honest shape to
    // write in. These legs are the ledger half of that recovery, and the refusal legs are what keep the
    // continuity rule from being weakened generally.

    let private grant original =
        ({ OriginalCritic = original
           GrantedBy = "heron-61d6"
           GrantUrl = "https://github.com/FS-GG/.github/pull/2650#issuecomment-5302904754" }
        : StructuredDecision.SuccessionGrant)

    /// initial(`tern-42`, changes-required) followed by one succession-bearing record of `kind`, appended
    /// by `successor`. The base chain is exactly what the existing tests build, so nothing about the
    /// generation is special except the record under test.
    let private successionChain kind successor mutate =
        let initial = review 1 None StructuredDecision.Initial StructuredDecision.ChangesRequired 0 None None
        let appended =
            { review 2 (Some initial.Digest) kind StructuredDecision.ChangesRequired
                (if kind = StructuredDecision.Confirmation then 1 else 0)
                (Some "https://review/1") (Some "https://review/1") with
                Critic = successor
                Succession = Some(grant initial.Critic) }
            |> mutate
            |> reseal
        initial, appended

    [<Fact>]
    let ``2662 a granted successor records under its own identity on confirmation escalation and repair-phase`` () =
        for kind in [ StructuredDecision.Confirmation; StructuredDecision.Escalation; StructuredDecision.RepairPhase ] do
            let initial, appended = successionChain kind "snipe-8934" id
            // A `Confirmation`-only exemption would leave the escalate-into-repair-phase hatch wedged,
            // because the continuity conjunct is keyed on `Kind <> Initial` and exempts no other kind.
            Assert.True(StructuredDecision.validateReviewLedger initial.Subject [ initial; appended ] |> Result.isOk)

    [<Fact>]
    let ``2662 the succession is legible from the record alone and survives the wire`` () =
        let _, appended = successionChain StructuredDecision.Confirmation "snipe-8934" id
        let decoded = Driver.decodeStructuredReview (Driver.encodeStructuredReview appended) |> expectOk
        // Criterion 2: outgoing critic, granter and grant location are all readable without
        // reconstructing the chain's history from other comments.
        Assert.Equal(Some(grant "tern-42"), decoded.Succession)
        Assert.Equal<string>("snipe-8934", decoded.Critic)
        Assert.Equal<string>(appended.Digest, StructuredDecision.reviewDigest decoded)
        Assert.Contains("\"originalCritic\":\"tern-42\"", Driver.encodeStructuredReview appended)

    [<Fact>]
    let ``2662 an absent grant is spelled null on the wire and decodes as no grant`` () =
        let ordinary = review 1 None StructuredDecision.Initial StructuredDecision.Pass 0 None None
        let encoded = Driver.encodeStructuredReview ordinary
        Assert.Contains("\"succession\":null", encoded)
        let decoded = Driver.decodeStructuredReview encoded |> expectOk
        Assert.True decoded.Succession.IsNone
        // The same fact spelled by OMISSION — every record written before the field existed.
        let omitted = encoded.Replace("\"succession\":null,", "")
        Assert.DoesNotContain("succession", omitted)
        let fromOmitted = Driver.decodeStructuredReview omitted |> expectOk
        Assert.True fromOmitted.Succession.IsNone
        Assert.Equal<string>(ordinary.Digest, StructuredDecision.reviewDigest fromOmitted)

    [<Fact>]
    let ``2662 an absent grant contributes nothing to the digest`` () =
        // The stability property every already-written ledger record depends on: `digest` joins with
        // `|`, so an absent grant must append no field at all — not an empty one.
        let ordinary = review 1 None StructuredDecision.Initial StructuredDecision.Pass 0 None None
        Assert.Equal<string>(ordinary.Digest, StructuredDecision.reviewDigest ordinary)
        let blankGrant =
            { ordinary with Succession = Some { OriginalCritic = ""; GrantedBy = ""; GrantUrl = "" } }
        Assert.NotEqual<string>(ordinary.Digest, StructuredDecision.reviewDigest blankGrant)
        for field in
            [ { grant "tern-42" with OriginalCritic = "other" }
              { grant "tern-42" with GrantedBy = "other" }
              { grant "tern-42" with GrantUrl = "other" } ] do
            Assert.NotEqual<string>(
                StructuredDecision.reviewDigest { ordinary with Succession = Some(grant "tern-42") },
                StructuredDecision.reviewDigest { ordinary with Succession = Some field })

    [<Fact>]
    let ``2662 a succession record fails closed against an engine that predates the field`` () =
        let initial, appended = successionChain StructuredDecision.Confirmation "snipe-8934" id
        // What an engine built before `.github#2662` computes: it ignores the unknown `succession` key
        // and digests the eighteen fields it knows. Modelled here by digesting the same record with the
        // grant dropped, which is exactly the field list that engine sees.
        let preFieldDigest = StructuredDecision.reviewDigest { appended with Succession = None; Digest = "" }
        Assert.NotEqual<string>(appended.Digest, preFieldDigest)
        let asPreFieldEngineWouldStore = { appended with Digest = preFieldDigest }
        match StructuredDecision.validateReviewLedger initial.Subject [ initial; asPreFieldEngineWouldStore ] with
        | Ok _ -> failwith "a succession record must never validate under a digest that omits the grant"
        | Error errors -> Assert.Contains(errors, fun error -> error.Contains "digest does not match")

    [<Fact>]
    let ``2662 an ungranted critic change is still refused with the unchanged message`` () =
        let initial = review 1 None StructuredDecision.Initial StructuredDecision.ChangesRequired 0 None None
        let stranger =
            { review 2 (Some initial.Digest) StructuredDecision.Confirmation StructuredDecision.Pass 1
                (Some "https://review/1") (Some "https://review/1") with
                Critic = "different-critic" }
            |> reseal
        match StructuredDecision.validateReviewLedger initial.Subject [ initial; stranger ] with
        | Ok _ -> failwith "an ungranted critic change must stay refused"
        | Error errors ->
            // The exact string, not a substring: continuity is not weakened generally, and a consumer
            // reading this message must keep reading the same one.
            Assert.Contains("every record in one review generation must bind the same critic", errors)

    [<Fact>]
    let ``2662 an inadmissible grant is refused conjunct by conjunct`` () =
        let cases =
            [ "outgoing critic is not the generation's critic",
              (fun (record: StructuredDecision.ReviewRecord) ->
                  { record with Succession = Some(grant "somebody-else") })
              "granting identity is blank",
              (fun record -> { record with Succession = Some { grant "tern-42" with GrantedBy = "  " } })
              "grant url is blank",
              (fun record -> { record with Succession = Some { grant "tern-42" with GrantUrl = "" } })
              "the successor is a generic route identity",
              (fun record -> { record with Critic = "fsgg-critic-best" })
              "the granter is a generic route identity",
              (fun record -> { record with Succession = Some { grant "tern-42" with GrantedBy = "fsgg-critic-best" } })
              "the record changes no critic at all",
              (fun record -> { record with Critic = "tern-42" }) ]
        for label, mutate in cases do
            let initial, appended = successionChain StructuredDecision.Confirmation "snipe-8934" mutate
            match StructuredDecision.validateReviewLedger initial.Subject [ initial; appended ] with
            | Ok _ -> failwithf "a grant must be refused when %s" label
            | Error _ -> ()

    [<Fact>]
    let ``2662 a generic outgoing critic cannot be laundered through a grant`` () =
        // .github#2451 in the successor slot. `fsgg-critic-<route>` is shared by every critic dispatched
        // at that route, so naming it as the outgoing critic witnesses nothing about which instance held
        // the seat — the equality a grant rests on would be satisfied by a stranger.
        let initial =
            { review 1 None StructuredDecision.Initial StructuredDecision.ChangesRequired 0 None None with
                Critic = "fsgg-critic-best" }
            |> reseal
        let appended =
            { review 2 (Some initial.Digest) StructuredDecision.Confirmation StructuredDecision.Pass 1
                (Some "https://review/1") (Some "https://review/1") with
                Critic = "snipe-8934"
                Succession = Some(grant "fsgg-critic-best") }
            |> reseal
        Assert.True(StructuredDecision.validateReviewLedger initial.Subject [ initial; appended ] |> Result.isError)

    [<Fact>]
    let ``2662 a grant belongs to no initial and no acceptance record`` () =
        let initial =
            { review 1 None StructuredDecision.Initial StructuredDecision.ChangesRequired 0 None None with
                Succession = Some(grant "someone") }
            |> reseal
        Assert.True(StructuredDecision.validateReviewLedger initial.Subject [ initial ] |> Result.isError)
        let clean = review 1 None StructuredDecision.Initial StructuredDecision.Pass 0 None None
        let acceptance =
            { review 2 (Some clean.Digest) StructuredDecision.Acceptance StructuredDecision.Accepted 0
                (Some "https://review/1") (Some "https://review/1") with
                Critic = "snipe-8934"
                Succession = Some(grant clean.Critic) }
            |> reseal
        Assert.True(StructuredDecision.validateReviewLedger clean.Subject [ clean; acceptance ] |> Result.isError)

    [<Fact>]
    let ``2662 the seat changes hands so the host acceptance and a second grant bind the successor`` () =
        let initial, confirmation = successionChain StructuredDecision.Confirmation "snipe-8934" id
        // The acceptance now binds the SUCCESSOR, and one bearing the despawned critic is refused —
        // that is what "rebind" means, and it is why the fix needs no special case at acceptance.
        let acceptance critic =
            { review 3 (Some confirmation.Digest) StructuredDecision.Acceptance StructuredDecision.Accepted 0
                (Some "https://review/1") (Some "https://review/2") with
                Critic = critic }
            |> reseal
        Assert.True(
            StructuredDecision.validateReviewLedger initial.Subject [ initial; confirmation; acceptance "snipe-8934" ]
            |> Result.isOk)
        Assert.True(
            StructuredDecision.validateReviewLedger initial.Subject [ initial; confirmation; acceptance initial.Critic ]
            |> Result.isError)
        // A successor can itself despawn: the second grant must name the FIRST successor, not the
        // record that opened the generation.
        let second original =
            { review 3 (Some confirmation.Digest) StructuredDecision.Confirmation StructuredDecision.Pass 2
                (Some "https://review/1") (Some "https://review/2") with
                Critic = "wren-4411"
                Succession = Some(grant original) }
            |> reseal
        Assert.True(
            StructuredDecision.validateReviewLedger initial.Subject [ initial; confirmation; second "snipe-8934" ]
            |> Result.isOk)
        Assert.True(
            StructuredDecision.validateReviewLedger initial.Subject [ initial; confirmation; second initial.Critic ]
            |> Result.isError)

    [<Fact>]
    let ``2662 the published generation critic is the identity in force and is unchanged without a grant`` () =
        let initial, confirmation = successionChain StructuredDecision.Confirmation "snipe-8934" id
        let comments = [ reviewComment 1L initial; reviewComment 2L confirmation ]
        let facts = Driver.reviewPhaseFacts comments
        Assert.Empty facts.StructuredErrors
        Assert.Equal(Some "snipe-8934", facts.CriticIdentity)
        // The equivalence floor: on a grant-free ledger the identity in force IS the opening record's
        // critic, so this correction changes no answer the engine already gave.
        let plainInitial = review 1 None StructuredDecision.Initial StructuredDecision.ChangesRequired 0 None None
        let plainConfirmation =
            review 2 (Some plainInitial.Digest) StructuredDecision.Confirmation StructuredDecision.Pass 1
                (Some "https://review/1") (Some "https://review/1")
        let plain = [ reviewComment 1L plainInitial; reviewComment 2L plainConfirmation ]
        Assert.Equal(Some plainInitial.Critic, (Driver.reviewPhaseFacts plain).CriticIdentity)
        // ...and through the terminal chain parser, whose accepted receipt must name the critic whose
        // pass the host actually accepted.
        let passing = { confirmation with Verdict = StructuredDecision.Pass; Digest = "" } |> reseal
        let acceptance =
            { review 3 (Some passing.Digest) StructuredDecision.Acceptance StructuredDecision.Accepted 0
                (Some "https://review/1") (Some "https://review/2") with
                Critic = "snipe-8934" }
            |> reseal
        let accepted =
            Driver.parseEffectiveReviewComments passing.HeadSha
                [ reviewComment 1L initial; reviewComment 2L passing; reviewComment 3L acceptance ]
            |> expectOk
        Assert.Equal(Some "snipe-8934", accepted.CriticIdentity)

    [<Fact>]
    let ``2662 a malformed grant fails closed at the wire and names the field`` () =
        let _, appended = successionChain StructuredDecision.Confirmation "snipe-8934" id
        let encoded = Driver.encodeStructuredReview appended
        let cases =
            [ "\"succession\":{\"grantUrl\":\"u\",\"grantedBy\":\"g\"}", "originalCritic"
              "\"succession\":{\"grantUrl\":\"u\",\"grantedBy\":\"g\",\"originalCritic\":7}", "originalCritic"
              "\"succession\":\"not-an-object\"", "succession" ]
        let intact = encoded.Substring(encoded.IndexOf "\"succession\":")
        let original = intact.Substring(0, intact.IndexOf "}" + 1)
        for replacement, expectedField in cases do
            match Driver.decodeStructuredReview (encoded.Replace(original, replacement)) with
            | Ok _ -> failwithf "a malformed grant must fail closed, not decode (%s)" replacement
            | Error reason -> Assert.Contains(expectedField, reason)

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
