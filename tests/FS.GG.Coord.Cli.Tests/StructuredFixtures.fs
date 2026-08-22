namespace FS.GG.Coord.Cli.Tests

open System.Text.Json
open FS.GG.Coord

module StructuredFixtures =
    let routeJson subject route agent workId =
        let specHome, gates =
            match route, workId with
            | Some DeliveryRoute.SddRequired, Some work ->
                Some $"work/%s{work}/spec.md", [ "implementationReady"; "analyze"; "verify"; "ship" ]
            | _ -> None, []

        let draft: StructuredDecision.RouteRecord =
            { Schema = StructuredDecision.RouteSchema
              Subject = subject
              Revision = 1
              PreviousDigest = None
              Scope = [ "fixture scope" ]
              Dependencies = [ "none" ]
              TouchSet = [ "src/**" ]
              PolicyVersion = StructuredDecision.PolicyVersion
              Route = route
              Agent = agent
              Timestamp = "2026-08-15T00:00:00Z"
              ReasonCodes = [ "fixture" ]
              Rationale = "structured fixture route"
              SddWorkId = workId
              SpecHome = specHome
              RequiredGates = gates
              Digest = "" }

        let record = { draft with Digest = StructuredDecision.routeDigest draft }
        JsonSerializer.Serialize
            {| schema = record.Schema
               subject = record.Subject
               revision = record.Revision
               previousDigest = record.PreviousDigest
               scope = record.Scope
               dependencies = record.Dependencies
               touchSet = record.TouchSet
               policyVersion = record.PolicyVersion
               route =
                   match record.Route with
                   | Some DeliveryRoute.Lightweight -> "lightweight"
                   | Some DeliveryRoute.SddRequired -> "sdd-required"
                   | None -> null
               agent = record.Agent
               timestamp = record.Timestamp
               reasonCodes = record.ReasonCodes
               rationale = record.Rationale
               sddWorkId = record.SddWorkId
               specHome = record.SpecHome
               requiredGates = record.RequiredGates
               digest = record.Digest |}

    let routeComment subject route agent workId =
        "<!-- fsgg:route-decision/v2 -->\n" + routeJson subject route agent workId

    let private reviewJson (record: StructuredDecision.ReviewRecord) =
        Driver.encodeStructuredReview record

    let acceptedReviewComments subject head critic =
        let initialDraft: StructuredDecision.ReviewRecord =
            { Schema = StructuredDecision.ReviewSchema
              Subject = subject
              Revision = 1
              PreviousDigest = None
              HeadSha = head
              ClaimGeneration = None
              BaseSha = None
              Critic = critic
              Verdict = StructuredDecision.Pass
              AcceptedExceptions = []
              RouteApplicability = "not-meaningful"
              RouteEvidence = [ "fixture has no runtime route comparison" ]
              PolicyVersion = StructuredDecision.PolicyVersion
              Kind = StructuredDecision.Initial
              Round = 0
              InitialReview = None
              PrecedingReview = None
              DiffAuditRequired = false
              DiffAuditReceipts = []
              Succession = None
              Timestamp = "2026-08-15T00:00:00Z"
              Digest = "" }
        let initial = { initialDraft with Digest = StructuredDecision.reviewDigest initialDraft }
        let acceptedDraft =
            { initial with
                Revision = 2
                PreviousDigest = Some initial.Digest
                Kind = StructuredDecision.Acceptance
                Verdict = StructuredDecision.Accepted
                ClaimGeneration = Some "10"
                BaseSha = Some(String.replicate 40 "b")
                InitialReview = Some "https://reviews/1"
                PrecedingReview = Some "https://reviews/1"
                Digest = "" }
        let accepted = { acceptedDraft with Digest = StructuredDecision.reviewDigest acceptedDraft }
        [ 1L, "https://reviews/1", "<!-- fsgg:review-decision/v2 -->\n" + reviewJson initial
          2L, "https://reviews/2", "<!-- fsgg:review-decision/v2 -->\n" + reviewJson accepted ]

    let movedHeadRepairComments subject reviewedHead critic =
        let initialDraft: StructuredDecision.ReviewRecord =
            { Schema = StructuredDecision.ReviewSchema
              Subject = subject
              Revision = 1
              PreviousDigest = None
              HeadSha = reviewedHead
              ClaimGeneration = None
              BaseSha = None
              Critic = critic
              Verdict = StructuredDecision.ChangesRequired
              AcceptedExceptions = []
              RouteApplicability = "not-meaningful"
              RouteEvidence = [ "fixture has no runtime route comparison" ]
              PolicyVersion = StructuredDecision.PolicyVersion
              Kind = StructuredDecision.Initial
              Round = 0
              InitialReview = None
              PrecedingReview = None
              DiffAuditRequired = false
              DiffAuditReceipts = []
              Succession = None
              Timestamp = "2026-08-15T00:00:00Z"
              Digest = "" }
        let initial = { initialDraft with Digest = StructuredDecision.reviewDigest initialDraft }
        let confirmationDraft =
            { initial with
                Revision = 2
                PreviousDigest = Some initial.Digest
                Kind = StructuredDecision.Confirmation
                Verdict = StructuredDecision.Pass
                Round = 1
                InitialReview = Some "https://reviews/1"
                PrecedingReview = Some "https://reviews/1"
                Digest = "" }
        let confirmation = { confirmationDraft with Digest = StructuredDecision.reviewDigest confirmationDraft }
        [ 1L, "https://reviews/1", "<!-- fsgg:review-decision/v2 -->\n" + reviewJson initial
          2L, "https://reviews/2", "<!-- fsgg:review-decision/v2 -->\n" + reviewJson confirmation ]

    let ordinaryRoundThreePassCommentsWithInitialVerdict subject terminalHead critic initialVerdict =
        let head round = String.replicate 40 (string round)
        let initialDraft: StructuredDecision.ReviewRecord =
            { Schema = StructuredDecision.ReviewSchema
              Subject = subject
              Revision = 1
              PreviousDigest = None
              HeadSha = head 0
              ClaimGeneration = None
              BaseSha = None
              Critic = critic
              Verdict = initialVerdict
              AcceptedExceptions = []
              RouteApplicability = "not-meaningful"
              RouteEvidence = [ "fixture has no runtime route comparison" ]
              PolicyVersion = StructuredDecision.PolicyVersion
              Kind = StructuredDecision.Initial
              Round = 0
              InitialReview = None
              PrecedingReview = None
              DiffAuditRequired = false
              DiffAuditReceipts = []
              Succession = None
              Timestamp = "2026-08-22T00:00:00Z"
              Digest = "" }
        let initial = { initialDraft with Digest = StructuredDecision.reviewDigest initialDraft }
        let next revision previous round reviewedHead preceding verdict =
            let draft =
                { initial with
                    Revision = revision
                    PreviousDigest = Some previous
                    HeadSha = reviewedHead
                    Verdict = verdict
                    Kind = StructuredDecision.Confirmation
                    Round = round
                    InitialReview = Some "https://reviews/1"
                    PrecedingReview = Some preceding
                    Digest = "" }
            { draft with Digest = StructuredDecision.reviewDigest draft }
        let round1 = next 2 initial.Digest 1 (head 1) "https://reviews/1" StructuredDecision.ChangesRequired
        let round2 = next 3 round1.Digest 2 (head 2) "https://reviews/2" StructuredDecision.ChangesRequired
        let round3 = next 4 round2.Digest 3 terminalHead "https://reviews/3" StructuredDecision.Pass
        [ 1L, "https://reviews/1", "<!-- fsgg:review-decision/v2 -->\n" + reviewJson initial
          2L, "https://reviews/2", "<!-- fsgg:review-decision/v2 -->\n" + reviewJson round1
          3L, "https://reviews/3", "<!-- fsgg:review-decision/v2 -->\n" + reviewJson round2
          4L, "https://reviews/4", "<!-- fsgg:review-decision/v2 -->\n" + reviewJson round3 ]

    let ordinaryRoundThreePassComments subject terminalHead critic =
        ordinaryRoundThreePassCommentsWithInitialVerdict
            subject terminalHead critic StructuredDecision.ChangesRequired
