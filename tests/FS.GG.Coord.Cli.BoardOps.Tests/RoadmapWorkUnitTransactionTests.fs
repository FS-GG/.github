namespace FS.GG.Coord.Cli.BoardOps.Tests

open System
open System.IO
open System.Text
open System.Text.Json
open System.Security.Cryptography
open Xunit
open FS.GG.Coord
open FS.GG.Coord.Cli
open FS.GG.Coord.Cli.BoardOps
open FS.GG.Coord.Cli.Kernel
open FS.GG.Coord.Cli.Lifecycle
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Errors
open FS.GG.Coord.GitHub.Transport

module RoadmapWorkUnitTransactionTests =
    let private unwrap = function Ok value -> value | Error values -> failwithf "%A" values
    let private shaText (value: string) = SHA256.HashData(Encoding.UTF8.GetBytes value) |> Convert.ToHexString |> _.ToLowerInvariant()
    let private preparationSources () =
        let roadmap = "- [x] **GS2-07.2 — previous.** done\n- [ ] **GS2-07.3 — compiler.** next\n"
        let unit id title prerequisites gates =
            let unsigned =
                $"""{{"exitGate":"test","gateCommands":%s{gates},"id":"%s{id}","owner":"FS.GG.Coordination","permissionCeiling":["local"],"prerequisites":%s{prerequisites},"qGates":[],"title":"%s{title}"}}"""
            unsigned[..unsigned.Length - 2] + $",\"contractSha256\":\"%s{shaText unsigned}\"}}"
        let catalog =
            $"""{{"schema":"fsgg.coordination.roadmap-index/1","roadmap":{{"repository":"FS-GG/.github","revision":"%s{String.replicate 40 "a"}","path":"docs/github-substrate-v2-roadmap.md","sha256":"%s{shaText roadmap}"}},"units":[%s{unit "GS2-07.2" "previous" "[\"GS2-07.1\"]" "[]"},%s{unit "GS2-07.3" "compiler" "[\"GS2-07.2\"]" "[\"implementation\",\"acceptance\"]"}]}}"""
        roadmap, catalog
    let private makePlanFor registrationRepository =
        let roadmap, catalog = preparationSources ()
        let request: RoadmapWorkUnit.PreparationRequest =
            { Schema = RoadmapWorkUnit.PreparationInputSchema
              RoadmapRevision = String.replicate 40 "a"
              AuthorityIssue = "https://github.com/FS-GG/.github/issues/3210"
              SddWorkId = "3210-roadmap-work-unit-compiler"
              RegistrationOwner = "FS-GG"
              RegistrationRepository = registrationRepository
              RegistrationPaths = [ "src/FS.GG.Coord.Core" ] }
        RoadmapWorkUnit.compilePreparation (Encoding.UTF8.GetBytes roadmap) (Encoding.UTF8.GetBytes catalog) request |> unwrap
    let private makePlan () = makePlanFor ".github"

    [<Fact>]
    let ``#3210 compiler registrations are byte-stable inputs to the sole staged-intake transaction`` () =
        let plan = makePlan ()
        let identities = plan.Registrations |> List.map (fun registration -> registration.Id, IntakeReceipt.digest registration.Draft)
        let replay = makePlan () |> _.Registrations |> List.map (fun registration -> registration.Id, IntakeReceipt.digest registration.Draft)
        Assert.Equal<(string * string) list>(identities, replay)
        for registration in plan.Registrations do
            let path = Path.Combine(Path.GetTempPath(), "fsgg-3210-intake-" + Guid.NewGuid().ToString("n") + ".json")
            try
                File.WriteAllText(path, RoadmapWorkUnit.canonicalIntakeDraft registration, UTF8Encoding(false))
                let decoded = IntakeApplication.readDraft path |> unwrap
                Assert.Equal(registration.Draft, decoded)
                Assert.Equal(IntakeReceipt.digest registration.Draft, IntakeReceipt.digest decoded)
            finally File.Delete path

    let private lifecycleLine claim phase event revision =
        $"""{{"item":{{"repo":"FS-GG/.github","number":500,"url":"https://github.com/FS-GG/.github/issues/500"}},"phase":"%s{phase}","event":"%s{event}","source":{{"repository":"FS-GG/.github","revision":"%s{revision}"}},"authority":{{"subject":"FS-GG/.github#500","claim_generation":"%s{claim}"}}}}"""

    [<Fact>]
    let ``#3210 lifecycle authority preserves historical claim generations and binds the terminal winner`` () =
        let expectation: Handlers.LifecycleAuthorityExpectation =
            { Repository = "FS-GG/.github"; Number = 500
              Url = "https://github.com/FS-GG/.github/issues/500"; Subject = "FS-GG/.github#500"
              CurrentClaimGeneration = "200"; ImplementationRepository = "FS-GG/.github"
              ImplementationCandidate = String.replicate 40 "a"
              ImplementationMerge = String.replicate 40 "b"
              AcceptanceCandidate = String.replicate 40 "c"
              AcceptanceMerge = String.replicate 40 "d"
              ProtectedMain = String.replicate 40 "d" }
        let turnover =
            lifecycleLine "100" "implementation" "completed" expectation.ImplementationCandidate
            + "\n"
            + lifecycleLine "200" "merge" "completed" expectation.ImplementationMerge
            + "\n"
            + lifecycleLine "200" "telemetry-reconciliation-merge" "completed" expectation.ImplementationMerge
            + "\n"
            + lifecycleLine "200" "acceptance" "completed" expectation.AcceptanceMerge
            + "\n"
        Assert.Empty(Handlers.validateLifecycleAuthority expectation turnover)
        let staleTerminal = turnover.Replace("\"claim_generation\":\"200\"", "\"claim_generation\":\"100\"")
        Assert.Contains("terminal lifecycle event claim generation is not the live winning claim", Handlers.validateLifecycleAuthority expectation staleTerminal)
        let staleMerge = turnover.Replace(expectation.ImplementationMerge, expectation.ImplementationCandidate)
        Assert.Contains(Handlers.validateLifecycleAuthority expectation staleMerge, fun error -> error.Contains("source revision is invalid for phase merge", StringComparison.Ordinal))
        let staleReconciliation =
            turnover.Replace(
                lifecycleLine "200" "telemetry-reconciliation-merge" "completed" expectation.ImplementationMerge,
                lifecycleLine "200" "telemetry-reconciliation-merge" "completed" expectation.ImplementationCandidate)
        Assert.Contains(Handlers.validateLifecycleAuthority expectation staleReconciliation, fun error -> error.Contains("source revision is invalid for phase telemetry-reconciliation-merge", StringComparison.Ordinal))

    [<Fact>]
    let ``#3210 SDD work model refuses any pending task`` () =
        let complete = """{"workId":"500-roadmap-gs2-07-3","tasks":[{"id":"T001","status":"done"},{"id":"T002","status":"done"}]}"""
        Assert.Empty(Handlers.validateCompleteSddWorkModel "500-roadmap-gs2-07-3" complete)
        let pending = complete.Replace("\"status\":\"done\"}", "\"status\":\"pending\"}", StringComparison.Ordinal)
        let findings = Handlers.validateCompleteSddWorkModel "500-roadmap-gs2-07-3" pending
        Assert.Contains(findings, fun finding -> finding.Contains("expected done", StringComparison.Ordinal))

    let private fullLifecycle repository candidate implementationMerge acceptanceMerge number =
        let draft order phase event at actual usage =
            let sourceRevision =
                match phase with
                | "merge" -> implementationMerge
                | "acceptance" -> acceptanceMerge
                | _ -> candidate
            $"""{{"schema_version":1,"run_id":"roadmap-unit-gs2-07.3","unit_id":"GS2-07.3","item":{{"repo":"%s{repository}","number":%d{number},"url":"https://github.com/%s{repository}/issues/%d{number}"}},"phase_order":%d{order},"phase":"%s{phase}","event":"%s{event}","at":"%s{at}","actor":"worker-1","model":{{"status":"recorded","provider":"OpenAI","name":"gpt","effort":"medium","source":"test"}},"source":{{"repository":"%s{repository}","revision":"%s{sourceRevision}"}},"evidence":["test:evidence"],"actual_minutes":%s{actual},"historical_durations_minutes":[],"historical_average_minutes":null,"token_usage":%s{usage},"tooling":{{"ledger_schema":1,"runtime":{{"status":"recorded","name":"codex","version":"1","source":"test"}},"coordination":{{"status":"recorded","name":"coord","version":"1","source":"test"}},"sdd":{{"status":"recorded","name":"sdd","version":"1","source":"test"}},"contracts":{{"status":"recorded","name":"contracts","version":"1","source":"test"}}}},"authority":{{"kind":"github_issue_comment","subject":"%s{repository}#%d{number}","claim_generation":"1"}}}}"""
        [ "intake"; "claim"; "sdd-analyze"; "implementation"; "sdd-verify"; "sdd-ship"
          "qualification"; "review"; "host-acceptance"; "merge"; "acceptance" ]
        |> List.mapi (fun index phase -> index + 1, phase)
        |> List.fold (fun log (order, phase) ->
            let first = LifecycleTelemetry.sealSuccessor "roadmap-unit-gs2-07.3" "GS2-07.3" log (draft order phase "started" "2026-09-05T06:00:00Z" "null" "{\"status\":\"pending\"}") |> unwrap
            let current = log + first
            current + (LifecycleTelemetry.sealSuccessor "roadmap-unit-gs2-07.3" "GS2-07.3" current (draft order phase "completed" "2026-09-05T06:01:00Z" "1" "{\"status\":\"unavailable\",\"reason\":\"post-completion runtime usage lookup failed: test fixture has no source\",\"source\":\"test\"}") |> unwrap)) ""

    let private authorityRouteFixtureFor
        registrationRepository
        issueNumber
        implementationPr
        candidate
        implementationMerge
        acceptancePr
        acceptanceCandidate
        acceptanceMerge
        sddWorkId
        critiqueCycle
        reviewedCommit
        =
        let plan = makePlanFor registrationRepository
        let applied =
            plan.Registrations
            |> List.mapi (fun index registration ->
                let number = issueNumber + index
                ({ Id = registration.Id; Kind = registration.Kind; DraftSha256 = IntakeReceipt.digest registration.Draft
                   Issue = $"%s{registration.Draft.Owner}/%s{registration.Draft.Repository}#%d{number}"; IssueUrl = $"https://github.com/%s{registration.Draft.Owner}/%s{registration.Draft.Repository}/issues/%d{number}" }
                 : RoadmapWorkUnit.AppliedRegistration))
        let application = RoadmapWorkUnit.sealPreparationApplication plan applied |> unwrap
        let unit = application.Registrations |> List.find (fun value -> value.Kind = "unit")
        let number = Int32.Parse(unit.Issue.Split('#')[1])
        let observation stage status : RoadmapWorkUnit.SddObservation =
            { Stage = stage; SubjectRevision = candidate
              ArtifactJson = $"""{{"schemaVersion":1,"viewVersion":"1.0","generator":"FS.GG.SDD.Artifacts/1.5.0","sources":[{{"path":"readiness/%s{sddWorkId}/work-model.json"}}],"findings":[],"diagnostics":[],"stage":"%s{stage}","status":"%s{status}","readiness":"%s{status}","workId":"%s{sddWorkId}"}}""" }
        let observations = [ observation "analyze" "implementationReady"; observation "verify" "verificationReady"; observation "ship" "shipReady" ]
        let digest c = String.replicate 64 (string c)
        let tool = {| id = "dotnet"; version = "10.0"; sha256 = digest '1' |}
        let executor = {| id = "implementer"; role = "implementer"; implementationSha256 = digest '2' |}
        let artifact stage = observations |> List.find (fun value -> value.Stage = stage) |> _.ArtifactJson |> Encoding.UTF8.GetBytes |> CanonicalJson.sha256
        let operation id kind artifactSha exitCode refusal replay =
            {| id = id; kind = kind; subjectRevision = candidate; tool = tool; executor = executor
               commandSha256 = digest '3'; artifactSha256 = [ artifactSha ]; resultSha256 = digest '5'
               replayResultSha256 = replay; exitCode = exitCode; refusal = refusal |}
        let qualificationJson =
            JsonSerializer.Serialize
                {| schema = Qualification.InputSchema; subject = unit.Issue; subjectRevision = candidate; checkoutClean = true
                   toolManifest = [ tool ]; executor = executor
                   operations =
                     [ operation "analyze" "analyze" (artifact "analyze") 0 None None
                       operation "verify" "verify" (artifact "verify") 0 None None
                       operation "ship" "ship" (artifact "ship") 0 None None
                       operation "hosted" "hosted" (digest '4') 0 None None
                       operation "fixed" "fixed-point" (digest '4') 0 None (Some(digest '5'))
                       operation "mutation" "mutation" (digest '4') 3 (Some "REFUSED inverted") None ]
                   claims = [ {| id = "all"; subjectRevision = candidate; requiredKinds = [ "analyze"; "verify"; "ship"; "hosted"; "fixed-point" ]; evidenceIds = [ "analyze"; "verify"; "ship"; "hosted"; "fixed" ] |} ]
                   mutations = [ {| id = "inverted"; operationId = "mutation"; expectedRefusal = "REFUSED inverted"; observedRefusal = "REFUSED inverted"; productionImplementationSha256 = digest '2'; fixtureImplementationSha256 = digest '6'; fixtureExecutorId = "fixture"; fixtureExecutorRole = "mutation-fixture" |} ]
                   hostedObservations =
                     [ {| complete = true; checks = [ {| scope = "check"; id = "1"; subjectRevision = candidate; state = "completed"; conclusion = "success" |} ] |}
                       {| complete = true; checks = [ {| scope = "check"; id = "1"; subjectRevision = candidate; state = "completed"; conclusion = "success" |} ] |} ]
                   obligations = {| headSha = candidate; declarations = [ {| id = (None: string option); kind = "none" |} ]; readbacks = [ {| commentId = 1L; url = $"https://github.com/FS-GG/%s{registrationRepository}/pull/%d{implementationPr}#issuecomment-1"; author = "bot" |} ] |}
                   semanticReview = {| subjectRevision = candidate; accepted = true; evidence = $"https://github.com/FS-GG/%s{registrationRepository}/pull/%d{implementationPr}#issuecomment-2" |} |}
        let qualificationInput = Qualification.parseInput (Encoding.UTF8.GetBytes qualificationJson) |> unwrap
        let qualification = Qualification.validate qualificationInput |> unwrap
        let identities: RoadmapWorkUnit.RevisionIdentities =
            { ImplementationPullRequest = implementationPr; ImplementationCandidate = candidate; ImplementationMerge = implementationMerge
              AcceptancePullRequest = acceptancePr; AcceptanceCandidate = acceptanceCandidate; AcceptanceMerge = acceptanceMerge; ProtectedMain = acceptanceMerge }
        let binding candidateHead merge tree = RoadmapWorkUnit.sealRevisionBinding ($"FS-GG/%s{registrationRepository}") candidateHead merge tree tree 0
        let critique = $"""{{"schema_version":3,"cycle_id":"%s{critiqueCycle}","milestone":"GS2-07.3","critic":"critic-1","initial_reviewed_commit":"%s{reviewedCommit}","scope":["requirements","diff","tests","architecture","roadmap-evidence"],"initial_verdict":"pass","game_functionality":false,"entry_point_not_test_ownable":false,"entry_point_not_test_ownable_reason":null,"player_journeys":[],"uncovered_functionality":[],"repair_rounds":0,"reviewed_commits":["%s{reviewedCommit}"],"findings":[],"confirmation":{{"reviewed_commit":"%s{reviewedCommit}","verdict":"pass","unresolved_blocker_major":[]}},"human_escalation":null}}"""
        let input: RoadmapWorkUnit.AcceptanceInput =
            { Schema = RoadmapWorkUnit.AcceptanceInputSchema; Plan = plan; PreparationApplication = application; Qualification = qualification
              LifecycleRunId = "roadmap-unit-gs2-07.3"; LifecycleUnitId = "GS2-07.3"; LifecycleLog = fullLifecycle ($"FS-GG/%s{registrationRepository}") candidate implementationMerge acceptanceMerge number
              RequiredLifecyclePhases = [ "intake"; "claim"; "sdd-analyze"; "implementation"; "sdd-verify"; "sdd-ship"; "qualification"; "review"; "host-acceptance"; "merge"; "acceptance" ]
              LifecycleUsageReceipts = []; LifecycleHistoryReport = "phase,tooling_fingerprint,actual_minutes,source\n"
              ReviewEvidence = $"https://github.com/FS-GG/%s{registrationRepository}/pull/%d{implementationPr}#issuecomment-3"; StructuredReviewEvidence = $"https://github.com/FS-GG/%s{registrationRepository}/pull/%d{implementationPr}#issuecomment-2"
              ReviewCycleId = "GS2-07.3"; ReviewReceipt = critique; SddWorkId = sddWorkId; SddObservations = observations
              Identities = identities; ImplementationBinding = binding identities.ImplementationCandidate identities.ImplementationMerge (String.replicate 40 "e")
              AcceptanceBinding = binding identities.AcceptanceCandidate identities.AcceptanceMerge (String.replicate 40 "f"); AcceptedAt = "2026-09-05T06:02:00Z" }
        input, qualificationJson

    let private authorityRouteFixture () =
        let candidate = String.replicate 40 "a"
        authorityRouteFixtureFor
            ".github" 500 1 candidate (String.replicate 40 "b") 2 (String.replicate 40 "c")
            (String.replicate 40 "d") "500-roadmap-gs2-07-3" "GS2-07.3" candidate

    let private immutableSinglePrAuthorityRouteFixture () =
        authorityRouteFixtureFor
            "FS.GG.Coordination" 304 305
            "0e07d58820c5480efb0c20901f361cf0ddba5dc9"
            "3e81b5df1ffc87c9dcad42e30733bb36073c3a57"
            305
            "0e07d58820c5480efb0c20901f361cf0ddba5dc9"
            "3e81b5df1ffc87c9dcad42e30733bb36073c3a57"
            "304-gs2-07-3-audit-repair"
            "roadmap-github-substrate-v2-m7-gs2-07-3-audit-repair"
            "46fb77ababbd0abc85f5d32d51578487523ec32d"

    let private invokeAuthorityRouteFixtureUsingResponder
        (fixture: unit -> RoadmapWorkUnit.AcceptanceInput * string)
        productionAuthorities
        observeSdd
        observeAuthorities
        respond
        =
        let directory = Path.Combine(Path.GetTempPath(), "fsgg-3210-authority-route-" + Guid.NewGuid().ToString("n"))
        Directory.CreateDirectory directory |> ignore
        try
            let input, qualificationJson = fixture ()
            let inputPath = Path.Combine(directory, "acceptance.json")
            let qualificationPath = Path.Combine(directory, "qualification.json")
            let executionPath = Path.Combine(directory, "execution.json")
            File.WriteAllText(inputPath, RoadmapWorkUnit.canonicalAcceptanceInput input, UTF8Encoding(false))
            File.WriteAllText(qualificationPath, qualificationJson, UTF8Encoding(false))
            File.WriteAllText(executionPath, "{}", UTF8Encoding(false))
            let baseOptions = Options.parse [ "intake"; "apply"; inputPath ] |> Result.defaultWith failwith
            let parsed =
                { baseOptions with
                    Args = [ "seal"; "--input"; inputPath; "--qualification-input"; qualificationPath; "--qualification-execution"; executionPath ] }
            let transport = Fake.Recorder respond
            let context: Context = { Transport = transport; Owner = "FS-GG"; Title = "Coordination"; DefaultRepo = Some ".github"; ChoreLocks = [] }
            let previousOut, previousError = Console.Out, Console.Error
            use stdout = new StringWriter()
            use stderr = new StringWriter()
            try
                Console.SetOut stdout; Console.SetError stderr
                let runQualification _ _ _ = Ok input.Qualification
                let code =
                    if productionAuthorities then
                        Handlers.roadmapUnitAcceptWithSddObserver runQualification observeSdd context parsed
                    else
                        Handlers.roadmapUnitAcceptWithObservers runQualification observeSdd observeAuthorities context parsed
                code, stdout.ToString(), stderr.ToString()
            finally Console.SetOut previousOut; Console.SetError previousError
        finally Directory.Delete(directory, true)

    let private invokeAuthorityRouteUsingResponder productionAuthorities observeSdd observeAuthorities respond =
        invokeAuthorityRouteFixtureUsingResponder authorityRouteFixture productionAuthorities observeSdd observeAuthorities respond

    let private invokeAuthorityRouteUsing productionAuthorities observeSdd observeAuthorities =
        invokeAuthorityRouteUsingResponder
            productionAuthorities
            observeSdd
            observeAuthorities
            (fun _ ->
                Error(NotFound(if productionAuthorities then "production authority observer reached" else "injected observers must prevent live transport")))

    let private invokeAuthorityRoute observeSdd observeAuthorities =
        invokeAuthorityRouteUsing false observeSdd observeAuthorities

    let private invokeImmutableSinglePrAuthorityRoute observeSdd observeAuthorities =
        invokeAuthorityRouteFixtureUsingResponder
            immutableSinglePrAuthorityRouteFixture
            false
            observeSdd
            observeAuthorities
            (fun _ -> Error(NotFound "injected observers must prevent live transport"))

    [<Fact>]
    let ``#3210 production acceptance route seals only after both live observer boundaries pass`` () =
        let mutable sddObserved, authoritiesObserved = false, false
        let code, output, error =
            invokeAuthorityRoute
                (fun _ -> sddObserved <- true; Ok())
                (fun _ -> authoritiesObserved <- true; Ok())
        Assert.Equal(ExitGreen, code)
        Assert.True(sddObserved && authoritiesObserved)
        Assert.Contains("fsgg.roadmap-unit.acceptance-bundle/1", output)
        Assert.Equal("", error)

        let refused, refusedOutput, refusedError =
            invokeAuthorityRoute (fun _ -> Ok()) (fun _ -> Error [ "inverted live authority" ])
        Assert.Equal(ExitError, refused)
        Assert.Equal("", refusedOutput)
        Assert.Contains("inverted live authority", refusedError)

    [<Fact>]
    let ``#3251 complete seal route accepts immutable Coordination issue 304 and single PR 305`` () =
        let code, output, error =
            invokeImmutableSinglePrAuthorityRoute (fun _ -> Ok()) (fun _ -> Ok())
        Assert.Equal(ExitGreen, code)
        Assert.Equal("", error)
        Assert.Contains("fsgg.roadmap-unit.acceptance-bundle/1", output)
        Assert.Contains("0e07d58820c5480efb0c20901f361cf0ddba5dc9", output)
        Assert.Contains("3e81b5df1ffc87c9dcad42e30733bb36073c3a57", output)

    [<Fact>]
    let ``#3210 production composition keeps the live authority observer active`` () =
        let code, output, error =
            invokeAuthorityRouteUsing true (fun _ -> Ok()) (fun _ -> failwith "production authority observer was replaced")
        Assert.Equal(ExitError, code)
        Assert.Equal("", output)
        Assert.Contains("production authority observer reached", error)

    [<Fact>]
    let ``#3247 production composition executes route and immutable envelope authority`` () =
        let ok body =
            Ok ({ Status = 200; Body = body; ETag = None; NextLink = None; Headers = Map.empty }: Response)
        let response (request: Request) =
            match request.Path with
            | "repos/FS-GG/.github/issues/500/comments" -> ok "[]"
            | "repos/FS-GG/.github/issues/1/comments" ->
                ok """[{"id":3,"html_url":"https://github.com/FS-GG/.github/pull/1#issuecomment-3","body":"envelope","created_at":"2026-09-05T06:02:00Z","updated_at":"2026-09-05T06:03:00Z"}]"""
            | _ -> Error(NotFound "unrelated live authority fixture")
        let code, output, error =
            invokeAuthorityRouteUsingResponder true (fun _ -> Ok()) (fun _ -> failwith "production observer was replaced") response
        Assert.Equal(ExitError, code)
        Assert.Equal("", output)
        Assert.Contains("acceptance SDD route authority is missing", error)
        Assert.Contains("review evidence comment was edited", error)

    [<Fact>]
    let ``#3242 live acceptance does not require ignored generated SDD files from the candidate`` () =
        let accepted, output, error =
            invokeAuthorityRoute (fun _ -> Ok()) (fun _ -> Ok())
        Assert.Equal(ExitGreen, accepted)
        Assert.Contains("fsgg.roadmap-unit.acceptance-bundle/1", output)
        Assert.Equal("", error)

        let refused, refusedOutput, refusedError =
            invokeAuthorityRoute
                (fun _ -> Error [ "independent SDD observation refused" ])
                (fun _ -> Ok())
        Assert.Equal(ExitError, refused)
        Assert.Equal("", refusedOutput)
        Assert.Contains("independent SDD observation refused", refusedError)

    [<Fact>]
    let ``#3247 immutable acceptance envelope has its own post-create identity`` () =
        let input, _ = authorityRouteFixture ()
        let canonical (text: string) =
            CanonicalJson.canonicalize (Encoding.UTF8.GetBytes text) |> unwrap
        let body =
            $"<!-- fsgg:roadmap-unit-acceptance-evidence/v1 -->\n```json\n%s{canonical (Qualification.canonicalResult input.Qualification)}\n```\n```json\n%s{canonical input.ReviewReceipt}\n```"
        let comment: Reads.AuthorityComment =
            { Id = 3L; Url = input.ReviewEvidence; Body = body
              CreatedAt = "2026-09-05T06:02:00Z"; UpdatedAt = "2026-09-05T06:02:00Z" }
        Assert.Empty(Handlers.validateAcceptanceEvidenceComment input comment)
        Assert.Contains(
            "review evidence comment was edited",
            Handlers.validateAcceptanceEvidenceComment input { comment with UpdatedAt = "2026-09-05T06:03:00Z" })

    [<Fact>]
    let ``#3247 acceptance SDD identity comes from current structured route authority`` () =
        let input, _ = authorityRouteFixture ()
        let unit = input.PreparationApplication.Registrations |> List.find (fun value -> value.Kind = "unit")
        let route: StructuredDecision.RouteRecord =
            { Schema = StructuredDecision.RouteSchema; Subject = unit.Issue; Revision = 1; PreviousDigest = None
              Scope = [ "roadmap acceptance" ]; Dependencies = [ "none" ]; TouchSet = [ "src" ]
              PolicyVersion = StructuredDecision.PolicyVersion; Route = Some DeliveryRoute.SddRequired
              Agent = "router"; Timestamp = "2026-09-05T06:00:00Z"; ReasonCodes = [ "public-contract" ]
              Rationale = "acceptance uses SDD"; SddWorkId = Some input.SddWorkId
              SpecHome = Some("work/" + input.SddWorkId + "/spec.md")
              RequiredGates = [ "implementationReady"; "analyze"; "verify"; "ship" ]; Digest = "" }
        let route = { route with Digest = StructuredDecision.routeDigest route }
        let json =
            JsonSerializer.Serialize
                {| schema = route.Schema; subject = route.Subject; revision = route.Revision
                   previousDigest = route.PreviousDigest; scope = route.Scope; dependencies = route.Dependencies
                   touchSet = route.TouchSet; policyVersion = route.PolicyVersion; route = "sdd-required"
                   agent = route.Agent; timestamp = route.Timestamp; reasonCodes = route.ReasonCodes
                   rationale = route.Rationale; sddWorkId = route.SddWorkId; specHome = route.SpecHome
                   requiredGates = route.RequiredGates; digest = route.Digest |}
        let ledger = [ "<!-- fsgg:route-decision/v2 -->\n" + json ]
        Assert.Empty(Handlers.validateAcceptanceSddRoute unit.Issue input.SddWorkId ledger)
        Assert.Contains(
            "acceptance SDD work id wrong-work differs from current route work id 500-roadmap-gs2-07-3",
            Handlers.validateAcceptanceSddRoute unit.Issue "wrong-work" ledger)
        Assert.Contains("acceptance SDD route authority is missing", Handlers.validateAcceptanceSddRoute unit.Issue input.SddWorkId [])

    [<Fact>]
    let ``#3210 production immutable preparation observer has a green route and refuses revision drift`` () =
        let input, _ = authorityRouteFixture ()
        let roadmap, catalog = preparationSources ()
        Handlers.validateImmutablePreparation input roadmap catalog |> unwrap |> ignore
        let stale = catalog.Replace(String.replicate 40 "a", String.replicate 40 "9")
        let errors =
            Handlers.validateImmutablePreparation input roadmap stale
            |> function Error values -> values | Ok () -> failwith "stale catalog revision reached the acceptance seal"
        Assert.Contains("immutable preparation authority: RoadmapIdentityMismatch \"catalog.roadmap.revision\"", errors)

    [<Fact>]
    let ``#3251 production critique route proves the real single-PR artifact-only ancestry`` () =
        let reviewed = "46fb77ababbd0abc85f5d32d51578487523ec32d"
        let candidate = "0e07d58820c5480efb0c20901f361cf0ddba5dc9"
        let cycle = "roadmap-github-substrate-v2-m7-gs2-07-3-audit-repair"
        let comparisonPath = $"repos/FS-GG/FS.GG.Coordination/compare/%s{reviewed}...%s{candidate}"
        let transport =
            Fake.Recorder(fun request ->
                if request.Path = comparisonPath then
                    Ok
                        { Status = 200
                          Body = $"""{{"status":"ahead","ahead_by":1,"merge_base_commit":{{"sha":"%s{reviewed}"}},"files":[{{"filename":"reviews/roadmap/%s{cycle}.json","status":"modified"}}]}}"""
                          ETag = None
                          NextLink = None
                          Headers = Map.empty }
                else Error(NotFound request.Path))
        let compare ancestor descendant =
            Reads.compareCommits transport "FS-GG" "FS.GG.Coordination" ancestor descendant
            |> Result.mapError Errors.explain
        let errors =
            Handlers.validateCritiqueCommitRelation
                compare
                "GS2-07.3"
                "304-gs2-07-3-audit-repair"
                candidate
                cycle
                reviewed
                "pass"
        Assert.Empty errors
        Assert.Equal(1, transport.RestCalls)
        Assert.True(transport.Logged($"%s{reviewed}...%s{candidate}"))

        let unrelated _ _ =
            Ok
                ({ Status = "diverged"
                   MergeBase = String.replicate 40 "9"
                   AheadBy = 2
                   Files = [ "src/unreviewed-change.fs", "added" ] }
                 : Reads.CommitComparison)
        let refused =
            Handlers.validateCritiqueCommitRelation
                unrelated
                "GS2-07.3"
                "304-gs2-07-3-audit-repair"
                candidate
                cycle
                reviewed
                "pass"
        Assert.Contains(
            refused,
            fun error ->
                error.Contains(
                    "not the exact one-commit ancestor of a modified-artifact-only final candidate",
                    StringComparison.Ordinal))
