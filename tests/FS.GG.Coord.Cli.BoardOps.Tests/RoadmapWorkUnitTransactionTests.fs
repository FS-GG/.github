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
    let private makePlan () =
        let roadmap, catalog = preparationSources ()
        let request: RoadmapWorkUnit.PreparationRequest =
            { Schema = RoadmapWorkUnit.PreparationInputSchema
              RoadmapRevision = String.replicate 40 "a"
              AuthorityIssue = "https://github.com/FS-GG/.github/issues/3210"
              SddWorkId = "3210-roadmap-work-unit-compiler"
              RegistrationOwner = "FS-GG"
              RegistrationRepository = ".github"
              RegistrationPaths = [ "src/FS.GG.Coord.Core" ] }
        RoadmapWorkUnit.compilePreparation (Encoding.UTF8.GetBytes roadmap) (Encoding.UTF8.GetBytes catalog) request |> unwrap

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

    let private fullLifecycle candidate number =
        let draft order phase event at actual usage =
            let sourceRevision =
                match phase with
                | "merge" -> String.replicate 40 "b"
                | "acceptance" -> String.replicate 40 "d"
                | _ -> candidate
            $"""{{"schema_version":1,"run_id":"roadmap-unit-gs2-07.3","unit_id":"GS2-07.3","item":{{"repo":"FS-GG/.github","number":%d{number},"url":"https://github.com/FS-GG/.github/issues/%d{number}"}},"phase_order":%d{order},"phase":"%s{phase}","event":"%s{event}","at":"%s{at}","actor":"worker-1","model":{{"status":"recorded","provider":"OpenAI","name":"gpt","effort":"medium","source":"test"}},"source":{{"repository":"FS-GG/.github","revision":"%s{sourceRevision}"}},"evidence":["test:evidence"],"actual_minutes":%s{actual},"historical_durations_minutes":[],"historical_average_minutes":null,"token_usage":%s{usage},"tooling":{{"ledger_schema":1,"runtime":{{"status":"recorded","name":"codex","version":"1","source":"test"}},"coordination":{{"status":"recorded","name":"coord","version":"1","source":"test"}},"sdd":{{"status":"recorded","name":"sdd","version":"1","source":"test"}},"contracts":{{"status":"recorded","name":"contracts","version":"1","source":"test"}}}},"authority":{{"kind":"github_issue_comment","subject":"FS-GG/.github#%d{number}","claim_generation":"1"}}}}"""
        [ "intake"; "claim"; "sdd-analyze"; "implementation"; "sdd-verify"; "sdd-ship"
          "qualification"; "review"; "host-acceptance"; "merge"; "acceptance" ]
        |> List.mapi (fun index phase -> index + 1, phase)
        |> List.fold (fun log (order, phase) ->
            let first = LifecycleTelemetry.sealSuccessor "roadmap-unit-gs2-07.3" "GS2-07.3" log (draft order phase "started" "2026-09-05T06:00:00Z" "null" "{\"status\":\"pending\"}") |> unwrap
            let current = log + first
            current + (LifecycleTelemetry.sealSuccessor "roadmap-unit-gs2-07.3" "GS2-07.3" current (draft order phase "completed" "2026-09-05T06:01:00Z" "1" "{\"status\":\"unavailable\",\"reason\":\"post-completion runtime usage lookup failed: test fixture has no source\",\"source\":\"test\"}") |> unwrap)) ""

    let private authorityRouteFixture () =
        let plan = makePlan ()
        let candidate = String.replicate 40 "a"
        let applied =
            plan.Registrations
            |> List.mapi (fun index registration ->
                let number = 500 + index
                ({ Id = registration.Id; Kind = registration.Kind; DraftSha256 = IntakeReceipt.digest registration.Draft
                   Issue = $"FS-GG/.github#%d{number}"; IssueUrl = $"https://github.com/FS-GG/.github/issues/%d{number}" }
                 : RoadmapWorkUnit.AppliedRegistration))
        let application = RoadmapWorkUnit.sealPreparationApplication plan applied |> unwrap
        let unit = application.Registrations |> List.find (fun value -> value.Kind = "unit")
        let number = Int32.Parse(unit.Issue.Split('#')[1])
        let observation stage status : RoadmapWorkUnit.SddObservation =
            { Stage = stage; SubjectRevision = candidate
              ArtifactJson = $"""{{"schemaVersion":1,"viewVersion":"1.0","generator":"FS.GG.SDD.Artifacts/1.5.0","sources":[{{"path":"readiness/500-roadmap-gs2-07-3/work-model.json"}}],"findings":[],"diagnostics":[],"stage":"%s{stage}","status":"%s{status}","readiness":"%s{status}","workId":"500-roadmap-gs2-07-3"}}""" }
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
                   obligations = {| headSha = candidate; declarations = [ {| id = (None: string option); kind = "none" |} ]; readbacks = [ {| commentId = 1L; url = "https://github.com/FS-GG/.github/pull/1#issuecomment-1"; author = "bot" |} ] |}
                   semanticReview = {| subjectRevision = candidate; accepted = true; evidence = "https://github.com/FS-GG/.github/pull/1#issuecomment-2" |} |}
        let qualificationInput = Qualification.parseInput (Encoding.UTF8.GetBytes qualificationJson) |> unwrap
        let qualification = Qualification.validate qualificationInput |> unwrap
        let identities: RoadmapWorkUnit.RevisionIdentities =
            { ImplementationPullRequest = 1; ImplementationCandidate = candidate; ImplementationMerge = String.replicate 40 "b"
              AcceptancePullRequest = 2; AcceptanceCandidate = String.replicate 40 "c"; AcceptanceMerge = String.replicate 40 "d"; ProtectedMain = String.replicate 40 "d" }
        let binding candidateHead merge tree = RoadmapWorkUnit.sealRevisionBinding "FS-GG/.github" candidateHead merge tree tree 0
        let critique = $"""{{"schema_version":3,"cycle_id":"GS2-07.3","milestone":"GS2-07.3","critic":"critic-1","initial_reviewed_commit":"%s{candidate}","scope":["requirements","diff","tests","architecture","roadmap-evidence"],"initial_verdict":"pass","game_functionality":false,"entry_point_not_test_ownable":false,"entry_point_not_test_ownable_reason":null,"player_journeys":[],"uncovered_functionality":[],"repair_rounds":0,"reviewed_commits":["%s{candidate}"],"findings":[],"confirmation":{{"reviewed_commit":"%s{candidate}","verdict":"pass","unresolved_blocker_major":[]}},"human_escalation":null}}"""
        let input: RoadmapWorkUnit.AcceptanceInput =
            { Schema = RoadmapWorkUnit.AcceptanceInputSchema; Plan = plan; PreparationApplication = application; Qualification = qualification
              LifecycleRunId = "roadmap-unit-gs2-07.3"; LifecycleUnitId = "GS2-07.3"; LifecycleLog = fullLifecycle candidate number
              RequiredLifecyclePhases = [ "intake"; "claim"; "sdd-analyze"; "implementation"; "sdd-verify"; "sdd-ship"; "qualification"; "review"; "host-acceptance"; "merge"; "acceptance" ]
              LifecycleUsageReceipts = []; LifecycleHistoryReport = "phase,tooling_fingerprint,actual_minutes,source\n"
              ReviewEvidence = "https://github.com/FS-GG/.github/pull/1#issuecomment-2"; StructuredReviewEvidence = "https://github.com/FS-GG/.github/pull/1#issuecomment-2"
              ReviewCycleId = "GS2-07.3"; ReviewReceipt = critique; SddWorkId = "500-roadmap-gs2-07-3"; SddObservations = observations
              Identities = identities; ImplementationBinding = binding identities.ImplementationCandidate identities.ImplementationMerge (String.replicate 40 "e")
              AcceptanceBinding = binding identities.AcceptanceCandidate identities.AcceptanceMerge (String.replicate 40 "f"); AcceptedAt = "2026-09-05T06:02:00Z" }
        input, qualificationJson

    let private invokeAuthorityRouteUsing productionAuthorities observeSdd observeAuthorities =
        let directory = Path.Combine(Path.GetTempPath(), "fsgg-3210-authority-route-" + Guid.NewGuid().ToString("n"))
        Directory.CreateDirectory directory |> ignore
        try
            let input, qualificationJson = authorityRouteFixture ()
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
            let transport =
                Fake.Recorder(fun _ ->
                    Error(NotFound(if productionAuthorities then "production authority observer reached" else "injected observers must prevent live transport")))
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

    let private invokeAuthorityRoute observeSdd observeAuthorities =
        invokeAuthorityRouteUsing false observeSdd observeAuthorities

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
    let ``#3210 production composition keeps the live authority observer active`` () =
        let code, output, error =
            invokeAuthorityRouteUsing true (fun _ -> Ok()) (fun _ -> failwith "production authority observer was replaced")
        Assert.Equal(ExitError, code)
        Assert.Equal("", output)
        Assert.Contains("production authority observer reached", error)

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
    let ``#3210 production immutable preparation observer has a green route and refuses revision drift`` () =
        let input, _ = authorityRouteFixture ()
        let roadmap, catalog = preparationSources ()
        Handlers.validateImmutablePreparation input roadmap catalog |> unwrap |> ignore
        let stale = catalog.Replace(String.replicate 40 "a", String.replicate 40 "9")
        let errors =
            Handlers.validateImmutablePreparation input roadmap stale
            |> function Error values -> values | Ok () -> failwith "stale catalog revision reached the acceptance seal"
        Assert.Contains("immutable preparation authority: RoadmapIdentityMismatch \"catalog.roadmap.revision\"", errors)
