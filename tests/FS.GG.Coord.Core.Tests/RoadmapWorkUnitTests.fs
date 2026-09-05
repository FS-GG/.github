namespace FS.GG.Coord.Tests

open System
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open System.Security.Cryptography
open Xunit
open FS.GG.Coord

module RoadmapWorkUnitTests =
    let private bytes (value: string) = Encoding.UTF8.GetBytes value
    let private sha c = String(c, 64)
    let private head c = String(c, 40)
    let private unwrap = function Ok value -> value | Error values -> failwithf "%A" values
    let private shaText (value: string) = SHA256.HashData(bytes value) |> Convert.ToHexString |> _.ToLowerInvariant()
    let private sealedReceipt (payload: string) = payload[..payload.Length - 2] + $",\"digest\":\"%s{shaText payload}\"}}"

    let private preparationInput () : RoadmapWorkUnit.PreparationInput =
        let obligations = [ "sdd:analyze"; "sdd:verify"; "sdd:ship"; "qualification"; "lifecycle"; "review" ]
        let previous: RoadmapWorkUnit.CatalogRow =
            { UnitId = "GS2-07.2"; Title = "Previous"; State = RoadmapWorkUnit.Accepted
              Prerequisite = Some "GS2-07.1"; Gates = [ "previous" ]; EvidenceObligations = obligations; ContractSha256 = sha 'e' }
        let selected: RoadmapWorkUnit.CatalogRow =
            { UnitId = "GS2-07.3"; Title = "Compile roadmap units"; State = RoadmapWorkUnit.Unchecked
              Prerequisite = Some previous.UnitId; Gates = [ "implementation"; "acceptance" ]; EvidenceObligations = obligations; ContractSha256 = sha 'f' }
        { Schema = RoadmapWorkUnit.PreparationInputSchema
          RoadmapRevision = head '1'
          RoadmapSourceDigest = "sha256:" + sha 'a'
          CatalogSourceDigest = "sha256:" + sha 'b'
          Catalog = [ previous; selected ]
          RoadmapRow = { UnitId = selected.UnitId; Title = selected.Title; Prerequisite = selected.Prerequisite; Gates = selected.Gates }
          AuthorityIssue = "https://github.com/FS-GG/.github/issues/3210"
          SddWorkId = "3210-roadmap-work-unit-compiler"
          RegistrationOwner = "FS-GG"; RegistrationRepository = ".github"
          RegistrationPaths = [ "src/FS.GG.Coord.Core" ] }

    let private sourcePreparation () =
        let roadmap = "- [x] **GS2-07.2 — Previous.** done\n- [ ] **GS2-07.3 — Compile roadmap units.** next\n"
        let unit id title prerequisites qGates commands =
            let unsigned = $"""{{"exitGate":"test","gateCommands":%s{commands},"id":"%s{id}","owner":"FS.GG.Coordination","permissionCeiling":["local"],"prerequisites":%s{prerequisites},"qGates":%s{qGates},"title":"%s{title}"}}"""
            unsigned[..unsigned.Length - 2] + $",\"contractSha256\":\"%s{shaText unsigned}\"}}"
        let roadmapHash = shaText roadmap
        let catalog =
            $"""{{"schema":"fsgg.coordination.roadmap-index/1","roadmap":{{"repository":"FS-GG/.github","revision":"%s{head '1'}","path":"docs/github-substrate-v2-roadmap.md","sha256":"%s{roadmapHash}"}},"units":[%s{unit "GS2-07.2" "Previous" "[\"GS2-07.1\"]" "[]" "[\"previous\"]"},%s{unit "GS2-07.3" "Compile roadmap units" "[\"GS2-07.2\"]" "[\"Q3\"]" "[\"acceptance\"]"}]}}"""
        let request: RoadmapWorkUnit.PreparationRequest =
            { Schema = RoadmapWorkUnit.PreparationInputSchema; RoadmapRevision = head '1'; AuthorityIssue = "https://github.com/FS-GG/.github/issues/3210"
              SddWorkId = "3210-roadmap-work-unit-compiler"
              RegistrationOwner = "FS-GG"; RegistrationRepository = ".github"; RegistrationPaths = [ "src" ] }
        roadmap, catalog, request

    let private qualification subject candidate =
        let tool: Qualification.ToolIdentity = { Id = "dotnet"; Version = "10.0"; Sha256 = sha '1' }
        let executor: Qualification.ExecutorIdentity = { Id = "implementer"; Role = "implementer"; ImplementationSha256 = sha '2' }
        let operation id kind : Qualification.OperationEvidence =
            { Id = id; Kind = kind; SubjectRevision = candidate; Tool = tool; Executor = executor
              CommandSha256 = sha '3'; ArtifactSha256 = [ sha '4' ]; ResultSha256 = sha '5'
              ReplayResultSha256 = if kind = Qualification.FixedPoint then Some(sha '5') else None
              ExitCode = if kind = Qualification.Mutation then 3 else 0
              Refusal = if kind = Qualification.Mutation then Some "REFUSED inverted" else None }
        let operations =
            [ operation "analyze" Qualification.Analyze; operation "verify" Qualification.Verify
              operation "ship" Qualification.Ship; operation "hosted" Qualification.Hosted
              operation "fixed" Qualification.FixedPoint; operation "mutation" Qualification.Mutation ]
        let checks: Qualification.HostedCheck list = [ { Scope = "check"; Id = "1"; SubjectRevision = candidate; State = "completed"; Conclusion = "success" } ]
        let input: Qualification.Input =
            { Schema = Qualification.InputSchema
              Subject = subject
              SubjectRevision = candidate
              CheckoutClean = true
              ToolManifest = [ tool ]
              Executor = executor
              Operations = operations
              Claims =
                [ { Id = "all"; SubjectRevision = candidate
                    RequiredKinds = [ Qualification.Analyze; Qualification.Verify; Qualification.Ship; Qualification.Hosted; Qualification.FixedPoint ]
                    EvidenceIds = [ "analyze"; "verify"; "ship"; "hosted"; "fixed" ] } ]
              Mutations =
                [ { Id = "inverted"; OperationId = "mutation"; ExpectedRefusal = "REFUSED inverted"
                    ObservedRefusal = "REFUSED inverted"; ProductionImplementationSha256 = sha '2'
                    FixtureImplementationSha256 = sha '6'; FixtureExecutorId = "fixture"; FixtureExecutorRole = "mutation-fixture" } ]
              HostedObservations = [ { Complete = true; Checks = checks }; { Complete = true; Checks = checks } ]
              Obligations =
                { HeadSha = candidate; Declarations = [ Qualification.NoObligations ]
                  Readbacks = [ { CommentId = 1L; Url = "https://github.com/FS-GG/.github/pull/1#issuecomment-1"; Author = "bot" } ] }
              SemanticReview = { SubjectRevision = candidate; Accepted = true; Evidence = "https://github.com/FS-GG/.github/pull/1#issuecomment-2" } }
        input |> Qualification.validate |> unwrap

    let private lifecycle () =
        let common order phase event at actual tokens =
            let sourceRevision =
                match phase with
                | "merge" -> head 'b'
                | "acceptance" -> head 'd'
                | _ -> head 'a'
            $"""{{"schema_version":1,"run_id":"roadmap-unit-gs2-07.3","unit_id":"GS2-07.3","item":{{"repo":"FS-GG/.github","number":400,"url":"https://github.com/FS-GG/.github/issues/400"}},"phase_order":%d{order},"phase":"%s{phase}","event":"%s{event}","at":"%s{at}","actor":"worker-1","model":{{"status":"recorded","provider":"OpenAI","name":"gpt","effort":"medium","source":"test"}},"source":{{"repository":"FS-GG/.github","revision":"%s{sourceRevision}"}},"evidence":["test:evidence"],"actual_minutes":%s{actual},"historical_durations_minutes":[],"historical_average_minutes":null,"token_usage":%s{tokens},"tooling":{{"ledger_schema":1,"runtime":{{"status":"recorded","name":"codex","version":"1","source":"test"}},"coordination":{{"status":"recorded","name":"coord","version":"1","source":"test"}},"sdd":{{"status":"recorded","name":"sdd","version":"1","source":"test"}},"contracts":{{"status":"recorded","name":"contracts","version":"1","source":"test"}}}},"authority":{{"kind":"github_issue_comment","subject":"FS-GG/.github#400","claim_generation":"1"}}}}"""
        [ "intake"; "claim"; "sdd-analyze"; "implementation"; "sdd-verify"; "sdd-ship"
          "qualification"; "review"; "host-acceptance"; "merge"; "acceptance" ]
        |> List.mapi (fun index phase -> index + 1, phase)
        |> List.fold (fun log (order, phase) ->
            let started = common order phase "started" "2026-09-05T06:00:00Z" "null" "{\"status\":\"pending\"}"
            let first = LifecycleTelemetry.sealSuccessor "roadmap-unit-gs2-07.3" "GS2-07.3" log started |> unwrap
            let current = log + first
            let completed = common order phase "completed" "2026-09-05T06:01:00Z" "1" "{\"status\":\"unavailable\",\"reason\":\"post-completion runtime usage lookup failed: test fixture has no source\",\"source\":\"test fixture\"}"
            current + (LifecycleTelemetry.sealSuccessor "roadmap-unit-gs2-07.3" "GS2-07.3" current completed |> unwrap)) ""

    let private critique candidate =
        $"""{{"schema_version":3,"cycle_id":"GS2-07.3","milestone":"GS2-07.3","critic":"critic-1","initial_reviewed_commit":"%s{candidate}","scope":["requirements","diff","tests","architecture","roadmap-evidence"],"initial_verdict":"pass","game_functionality":false,"entry_point_not_test_ownable":false,"entry_point_not_test_ownable_reason":null,"player_journeys":[],"uncovered_functionality":[],"repair_rounds":0,"reviewed_commits":["%s{candidate}"],"findings":[],"confirmation":{{"reviewed_commit":"%s{candidate}","verdict":"pass","unresolved_blocker_major":[]}},"human_escalation":null}}"""

    let private acceptanceInput () =
        let candidate = head 'a'
        let plan = preparationInput () |> RoadmapWorkUnit.inspectPreparation |> unwrap
        let applied =
            plan.Registrations
            |> List.mapi (fun index registration ->
                let number = 400 + index
                ({ Id = registration.Id; Kind = registration.Kind; DraftSha256 = IntakeReceipt.digest registration.Draft
                   Issue = $"FS-GG/.github#%d{number}"; IssueUrl = $"https://github.com/FS-GG/.github/issues/%d{number}" }
                 : RoadmapWorkUnit.AppliedRegistration))
        let application = RoadmapWorkUnit.sealPreparationApplication plan applied |> unwrap
        let unitIssue = application.Registrations |> List.find (fun value -> value.Kind = "unit") |> _.Issue
        let observation stage status : RoadmapWorkUnit.SddObservation =
            { Stage = stage; SubjectRevision = candidate
              ArtifactJson = $"""{{"schemaVersion":1,"viewVersion":"1.0","generator":"FS.GG.SDD.Artifacts/1.5.0","sources":[{{"path":"readiness/400-roadmap-gs2-07-3/work-model.json"}}],"findings":[],"diagnostics":[],"stage":"%s{stage}","status":"%s{status}","readiness":"%s{status}","workId":"400-roadmap-gs2-07-3"}}""" }
        let structuredReview = "https://github.com/FS-GG/.github/pull/1#issuecomment-2"
        let acceptanceEnvelope = "https://github.com/FS-GG/.github/pull/1#issuecomment-3"
        let binding candidate merge tree =
            RoadmapWorkUnit.sealRevisionBinding "FS-GG/.github" candidate merge tree tree 0
        let identities: RoadmapWorkUnit.RevisionIdentities =
            { ImplementationPullRequest = 1; ImplementationCandidate = candidate; ImplementationMerge = head 'b'
              AcceptancePullRequest = 2; AcceptanceCandidate = head 'c'; AcceptanceMerge = head 'd'; ProtectedMain = head 'd' }
        { Schema = RoadmapWorkUnit.AcceptanceInputSchema
          Plan = plan; PreparationApplication = application; Qualification = qualification unitIssue candidate; LifecycleRunId = "roadmap-unit-gs2-07.3"; LifecycleUnitId = "GS2-07.3"
          LifecycleLog = lifecycle ()
          RequiredLifecyclePhases =
            [ "intake"; "claim"; "sdd-analyze"; "implementation"; "sdd-verify"; "sdd-ship"
              "qualification"; "review"; "host-acceptance"; "merge"; "acceptance" ]
          LifecycleUsageReceipts = []
          LifecycleHistoryReport = "phase,tooling_fingerprint,actual_minutes,source\n"
          ReviewEvidence = acceptanceEnvelope; StructuredReviewEvidence = structuredReview
          ReviewCycleId = "GS2-07.3"; ReviewReceipt = critique candidate; SddWorkId = "400-roadmap-gs2-07-3"
          SddObservations = [ observation "analyze" "implementationReady"; observation "verify" "verificationReady"; observation "ship" "shipReady" ]
          Identities = identities
          ImplementationBinding = binding identities.ImplementationCandidate identities.ImplementationMerge (head 'e')
          AcceptanceBinding = binding identities.AcceptanceCandidate identities.AcceptanceMerge (head 'f')
          AcceptedAt = "2026-09-05T06:02:00Z" } : RoadmapWorkUnit.AcceptanceInput

    [<Fact>]
    let ``#3210 selects exactly next row and derives staged intake registrations`` () =
        let plan = preparationInput () |> RoadmapWorkUnit.inspectPreparation |> unwrap
        Assert.Equal("GS2-07.3", plan.Unit.UnitId)
        Assert.Equal("GS2-07.2", plan.AcceptedPrerequisite)
        Assert.Single(plan.Registrations) |> ignore
        Assert.Equal(2, plan.GateRegistrations.Length)
        plan.Registrations |> List.iter (fun registration -> Assert.True(Intake.validate registration.Draft |> Result.isOk))
        let replay = preparationInput () |> RoadmapWorkUnit.inspectPreparation |> unwrap
        Assert.Equal(RoadmapWorkUnit.canonicalPlan plan, RoadmapWorkUnit.canonicalPlan replay)

    [<Fact>]
    let ``#3210 authoritative roadmap and catalog compile while stale drift and misordered frontier refuse`` () =
        let roadmap, catalog, request = sourcePreparation ()
        let plan = RoadmapWorkUnit.compilePreparation (bytes roadmap) (bytes catalog) request |> unwrap
        Assert.Equal("GS2-07.3", plan.Unit.UnitId)
        Assert.Equal(shaText roadmap, plan.Authority.RoadmapDigest.Substring("sha256:".Length))
        Assert.True(
            RoadmapWorkUnit.compilePreparation (bytes roadmap) (bytes (catalog.Replace(head '1', head '9'))) request
            |> Result.isError)
        Assert.True(RoadmapWorkUnit.compilePreparation (bytes (roadmap + "drift")) (bytes catalog) request |> Result.isError)
        Assert.True(RoadmapWorkUnit.compilePreparation (bytes roadmap) (bytes (catalog.Replace(plan.Unit.ContractSha256, sha '0'))) request |> Result.isError)
        let misordered = roadmap.Replace("- [ ] **GS2-07.3", "- [x] **GS2-07.3")
        Assert.True(RoadmapWorkUnit.compilePreparation (bytes misordered) (bytes catalog) request |> Result.isError)

    [<Fact>]
    let ``#3210 catalog cannot omit the canonical first unchecked roadmap row`` () =
        let roadmap, catalog, request = sourcePreparation ()
        let omittedRoadmap =
            roadmap.Replace(
                "- [ ] **GS2-07.3 — Compile roadmap units.** next",
                "- [ ] **GS2-07.4 — Omitted canonical frontier.** next\n- [ ] **GS2-07.3 — Compile roadmap units.** later")
        let pinned = catalog.Replace(shaText roadmap, shaText omittedRoadmap)
        let findings =
            RoadmapWorkUnit.compilePreparation (bytes omittedRoadmap) (bytes pinned) request
            |> function Error values -> values | Ok value -> failwithf "unsafe later unit selected: %s" value.Unit.UnitId
        Assert.Contains(RoadmapWorkUnit.RoadmapIdentityMismatch "catalog omits or reorders the canonical first unchecked roadmap row", findings)

    [<Fact>]
    let ``#3210 catalog accepted prefix must preserve canonical roadmap order`` () =
        let roadmap =
            "- [x] **GS2-07.1 — First.** done\n- [x] **GS2-07.2 — Second.** done\n- [ ] **GS2-07.3 — Compile roadmap units.** next\n"
        let unit id title prerequisites =
            let unsigned =
                $"""{{"exitGate":"test","gateCommands":["acceptance"],"id":"%s{id}","owner":"FS.GG.Coordination","permissionCeiling":["local"],"prerequisites":%s{prerequisites},"qGates":[],"title":"%s{title}"}}"""
            unsigned[..unsigned.Length - 2] + $",\"contractSha256\":\"%s{shaText unsigned}\"}}"
        let catalog =
            $"""{{"schema":"fsgg.coordination.roadmap-index/1","roadmap":{{"repository":"FS-GG/.github","revision":"%s{head '1'}","path":"docs/github-substrate-v2-roadmap.md","sha256":"%s{shaText roadmap}"}},"units":[%s{unit "GS2-07.2" "Second" "[\"GS2-07.1\"]"},%s{unit "GS2-07.1" "First" "[\"GS2-07.0\"]"},%s{unit "GS2-07.3" "Compile roadmap units" "[\"GS2-07.1\"]"}]}}"""
        let _, _, request = sourcePreparation ()
        let findings =
            RoadmapWorkUnit.compilePreparation (bytes roadmap) (bytes catalog) request
            |> function Error values -> values | Ok value -> failwithf "misordered prefix accepted: %s" value.Unit.UnitId
        Assert.Contains(
            RoadmapWorkUnit.RoadmapIdentityMismatch "catalog prefix does not match canonical roadmap order through the first unchecked row",
            findings)

    [<Fact>]
    let ``#3233 ordered partial catalog admits omitted accepted roadmap history`` () =
        let roadmap, catalog, request = sourcePreparation ()
        let partialRoadmap =
            "- [x] **GS2-00.0 — Historical roadmap preamble.** accepted\n"
            + roadmap.Replace("- [ ] **GS2-07.3", "- [x] **GS2-03.10 — Accepted row outside the executable catalog.** accepted\n- [ ] **GS2-07.3")
        let pinnedCatalog = catalog.Replace(shaText roadmap, shaText partialRoadmap)
        let plan = RoadmapWorkUnit.compilePreparation (bytes partialRoadmap) (bytes pinnedCatalog) request |> unwrap
        Assert.Equal("GS2-07.3", plan.Unit.UnitId)
        Assert.Equal("GS2-07.2", plan.AcceptedPrerequisite)

    [<Fact>]
    let ``#3210 selection refuses ambiguous authority prerequisite and catalog drift`` () =
        let input = preparationInput ()
        let extra = { input.Catalog[1] with UnitId = "GS2-07.4" }
        let ambiguous = { input with Catalog = input.Catalog @ [ extra ] }
        Assert.Contains(RoadmapWorkUnit.MultipleNextUnits [ "GS2-07.3"; "GS2-07.4" ], ambiguous |> RoadmapWorkUnit.inspectPreparation |> function Error values -> values | Ok _ -> [])
        let wrong = { input with RoadmapRow = { input.RoadmapRow with Title = "drift" } }
        Assert.Contains(RoadmapWorkUnit.RoadmapIdentityMismatch "title", wrong |> RoadmapWorkUnit.inspectPreparation |> function Error values -> values | Ok _ -> [])
        let zero = { input with Catalog = input.Catalog |> List.map (fun row -> { row with State = RoadmapWorkUnit.Accepted }) }
        Assert.Contains(RoadmapWorkUnit.NextUnitMissing, zero |> RoadmapWorkUnit.inspectPreparation |> function Error values -> values | Ok _ -> [])
        let blocked = { input with Catalog = [ input.Catalog[0]; { input.Catalog[1] with Prerequisite = Some "GS2-99.9" } ] }
        Assert.Contains(RoadmapWorkUnit.PrerequisiteNotAccepted("GS2-07.3", "GS2-99.9"), blocked |> RoadmapWorkUnit.inspectPreparation |> function Error values -> values | Ok _ -> [])
        let duplicate = { input with Catalog = input.Catalog @ [ input.Catalog[1] ] }
        Assert.Contains(RoadmapWorkUnit.DuplicateCatalogUnit "GS2-07.3", duplicate |> RoadmapWorkUnit.inspectPreparation |> function Error values -> values | Ok _ -> [])
        let missingEvidence = { input with Catalog = [ input.Catalog[0]; { input.Catalog[1] with EvidenceObligations = [ "qualification" ] } ] }
        Assert.Contains(RoadmapWorkUnit.EvidenceObligationMissing "sdd:analyze", missingEvidence |> RoadmapWorkUnit.inspectPreparation |> function Error values -> values | Ok _ -> [])

    [<Fact>]
    let ``#3210 preparation rendering is bounded deterministic and verifies replay`` () =
        let plan = preparationInput () |> RoadmapWorkUnit.inspectPreparation |> unwrap
        let source = bytes "before\n<!-- fsgg:roadmap-registration/GS2-07.3 -->\nold\n<!-- /fsgg:roadmap-registration/GS2-07.3 -->\nafter\n"
        let rendered = RoadmapWorkUnit.renderPreparation source plan |> unwrap
        Assert.Equal(rendered, RoadmapWorkUnit.renderPreparation source plan |> unwrap)
        Assert.True(RoadmapWorkUnit.verifyPreparation source (bytes rendered) plan |> Result.isOk)
        Assert.True(RoadmapWorkUnit.verifyPreparation source (bytes (rendered.Replace("before", "changed"))) plan |> Result.isError)

    [<Fact>]
    let ``#3210 acceptance consumes qualified lifecycle and observed SDD evidence atomically`` () =
        let input = acceptanceInput ()
        let candidateEnvelope = RoadmapWorkUnit.inspectAcceptanceCandidate input |> unwrap
        let observed = RoadmapWorkUnit.observeAcceptance candidateEnvelope
        let accepted = RoadmapWorkUnit.sealObservedAcceptance observed
        let bundle = RoadmapWorkUnit.acceptedBundle accepted
        Assert.Contains("\"evidenceIndex\"", bundle)
        Assert.Contains("\"receipt\"", bundle)
        Assert.True(RoadmapWorkUnit.verifyObservedAcceptance observed (bytes bundle) |> Result.isOk)

        let candidate, implementation, acceptance = input.Identities.ImplementationCandidate, input.Identities.ImplementationMerge, input.Identities.AcceptanceMerge
        let report = "---\nfeedbackSchema: 2\ncycle: GS2-07.3\n---\n## §1 Provenance and confidence\n- **activation:** active\n- **phases:** implementation, acceptance\n- **material events:** 0\n- **zero-event reason:** compiled pilot produced no material feedback\n## §2 Findings\nNone.\n"
        let audit = $"""{{"auditSchema":1,"report":"feedback/report.md","reportSha256":"%s{shaText report}","findings":[]}}"""
        let delivery = sealedReceipt $"""{{"acceptanceMergeHead":"%s{acceptance}","candidateHead":"%s{candidate}","claimsRemaining":0,"implementationMergeHead":"%s{implementation}","issueUrl":"https://github.com/FS-GG/.github/issues/3210","pullRequestUrl":"https://github.com/FS-GG/.github/pull/1","schema":"fsgg.roadmap.delivery/1","unitId":"GS2-07.3"}}"""
        let feedback = sealedReceipt $"""{{"auditSha256":"%s{shaText audit}","cycleId":"GS2-07.3","head":"%s{acceptance}","reportSha256":"%s{shaText report}","schema":"fsgg.roadmap.feedback-binding/1","unitId":"GS2-07.3"}}"""
        let cycle = sealedReceipt $"""{{"cycleId":"GS2-07.3","head":"%s{acceptance}","schema":"fsgg.roadmap.cycle-update/1","unitId":"GS2-07.3"}}"""
        let check = sealedReceipt $"""{{"head":"%s{acceptance}","name":"required","owner":null,"passed":true,"required":true,"schema":"fsgg.roadmap.check/1","unitId":"GS2-07.3"}}"""
        let closed =
            RoadmapClosure.inspect
                { UnitId = "GS2-07.3"; Title = "Compile roadmap units"; RoadmapSourceDigest = input.Plan.Authority.RoadmapDigest
                  AcceptedReceipt = RoadmapWorkUnit.acceptedReceipt accepted; DeliveryReceipt = bytes delivery; Critique = bytes (critique candidate)
                  FeedbackReportPath = "feedback/report.md"; FeedbackReport = bytes report; FeedbackAudit = bytes audit
                  FeedbackPhases = [ "implementation"; "acceptance" ]; FeedbackCheckpoint = None; FeedbackBinding = bytes feedback
                  CycleUpdate = bytes cycle; CheckReceipts = [ bytes check ] }
            |> unwrap
        Assert.Equal("GS2-07.3", closed.Evidence.UnitId)
        Assert.Equal(RoadmapWorkUnit.acceptedDigest accepted, closed.Evidence.AcceptedReceiptDigest.Substring("sha256:".Length))

    [<Fact>]
    let ``#3247 semantic review binds structured authority independently of acceptance envelope`` () =
        let input = acceptanceInput ()
        Assert.False(input.ReviewEvidence = input.StructuredReviewEvidence)
        RoadmapWorkUnit.inspectAcceptanceCandidate input |> unwrap |> ignore
        let mismatched =
            { input with
                StructuredReviewEvidence = "https://github.com/FS-GG/.github/pull/1#issuecomment-4" }
        let findings =
            RoadmapWorkUnit.inspectAcceptanceCandidate mismatched
            |> function Error values -> values | Ok _ -> failwith "mismatched structured review reached acceptance"
        Assert.Contains(
            RoadmapWorkUnit.QualificationMismatch "semantic review evidence locator differs from the structured review authority",
            findings)

    [<Fact>]
    let ``#3210 manually flipped SDD state identity collapse and bundle tamper refuse`` () =
        let input = acceptanceInput ()
        let forged = { input.SddObservations.Head with ArtifactJson = input.SddObservations.Head.ArtifactJson.Replace("implementationReady", "authoredReady") }
        Assert.True(RoadmapWorkUnit.inspectAcceptanceCandidate { input with SddObservations = forged :: input.SddObservations.Tail } |> Result.isError)
        let minimal = { input.SddObservations.Head with ArtifactJson = "{\"stage\":\"analyze\",\"status\":\"implementationReady\",\"workId\":\"GS2-07.3\"}" }
        Assert.True(RoadmapWorkUnit.inspectAcceptanceCandidate { input with SddObservations = minimal :: input.SddObservations.Tail } |> Result.isError)
        let wrongSubject = { input.Qualification with Subject = "FS-GG/.github#9999" }
        Assert.True(RoadmapWorkUnit.inspectAcceptanceCandidate { input with Qualification = wrongSubject } |> Result.isError)
        let forgedApplication = { input.PreparationApplication with PlanDigest = sha '0' }
        Assert.True(RoadmapWorkUnit.inspectAcceptanceCandidate { input with PreparationApplication = forgedApplication } |> Result.isError)
        let collapsed = { input.Identities with ImplementationMerge = input.Identities.ImplementationCandidate }
        Assert.True(RoadmapWorkUnit.inspectAcceptanceCandidate { input with Identities = collapsed } |> Result.isError)
        let wrongTree = { input.ImplementationBinding with MergeTree = head '0' }
        Assert.True(RoadmapWorkUnit.inspectAcceptanceCandidate { input with ImplementationBinding = wrongTree } |> Result.isError)
        let wrongCommand = { input.ImplementationBinding with CommandSha256 = sha '0' }
        Assert.True(RoadmapWorkUnit.inspectAcceptanceCandidate { input with ImplementationBinding = wrongCommand } |> Result.isError)
        let wrongLifecycleIssue = input.LifecycleLog.Replace("FS-GG/.github#400", "FS-GG/.github#3210").Replace("/issues/400", "/issues/3210").Replace("\"number\":400", "\"number\":3210")
        Assert.True(RoadmapWorkUnit.inspectAcceptanceCandidate { input with LifecycleLog = wrongLifecycleIssue } |> Result.isError)
        let wrongLifecycleRevision = input.LifecycleLog.Replace(head 'a', head '9')
        Assert.True(RoadmapWorkUnit.inspectAcceptanceCandidate { input with LifecycleLog = wrongLifecycleRevision } |> Result.isError)
        Assert.True(RoadmapWorkUnit.inspectAcceptanceCandidate { input with RequiredLifecyclePhases = input.RequiredLifecyclePhases.Tail } |> Result.isError)
        Assert.True(RoadmapWorkUnit.inspectAcceptanceCandidate { input with LifecycleHistoryReport = "not,csv\n" } |> Result.isError)
        Assert.True(RoadmapWorkUnit.inspectAcceptanceCandidate { input with ReviewReceipt = input.ReviewReceipt.Replace("\"verdict\":\"pass\"", "\"verdict\":\"red\"") } |> Result.isError)
        let candidateEnvelope = RoadmapWorkUnit.inspectAcceptanceCandidate input |> unwrap
        let observed = RoadmapWorkUnit.observeAcceptance candidateEnvelope
        let accepted = RoadmapWorkUnit.sealObservedAcceptance observed
        let bundle = RoadmapWorkUnit.acceptedBundle accepted
        Assert.True(RoadmapWorkUnit.verifyObservedAcceptance observed (bytes (bundle.Replace("accepted", "tampered"))) |> Result.isError)
        Assert.True(RoadmapWorkUnit.verifyObservedAcceptance observed (RoadmapWorkUnit.acceptedReceipt accepted) |> Result.isError)

    [<Fact>]
    let ``#3210 closed acceptance wire input parses and replays the same receipt bytes`` () =
        let input = acceptanceInput ()
        let parsed = RoadmapWorkUnit.parseAcceptanceInput (bytes (RoadmapWorkUnit.canonicalAcceptanceInput input)) |> unwrap
        let direct = RoadmapWorkUnit.inspectAcceptanceCandidate input |> unwrap
        let roundTrip = RoadmapWorkUnit.inspectAcceptanceCandidate parsed |> unwrap
        Assert.Equal(RoadmapWorkUnit.candidateDigest direct, RoadmapWorkUnit.candidateDigest roundTrip)
