namespace FS.GG.Coord.Cli.Tests

open System
open System.Diagnostics
open System.IO
open System.Text
open System.Security.Cryptography
open Xunit
open FS.GG.Coord
open FS.GG.Coord.Cli

module RoadmapWorkUnitCliTests =
    let private sha c = String(c, 64)
    let private head c = String(c, 40)
    let private unwrap = function Ok value -> value | Error values -> failwithf "%A" values
    let private invoke args =
        let previousOut, previousError = Console.Out, Console.Error
        use stdout = new StringWriter()
        use stderr = new StringWriter()
        try
            Console.SetOut stdout; Console.SetError stderr
            let code = Program.main (List.toArray args)
            code, stdout.ToString(), stderr.ToString()
        finally
            Console.SetOut previousOut; Console.SetError previousError

    let private git repository args =
        let info = ProcessStartInfo("git")
        info.WorkingDirectory <- repository
        info.RedirectStandardOutput <- true
        info.UseShellExecute <- false
        args |> List.iter info.ArgumentList.Add
        use gitProcess = Process.Start info
        let output = gitProcess.StandardOutput.ReadToEnd().Trim()
        gitProcess.WaitForExit()
        Assert.Equal(0, gitProcess.ExitCode)
        output

    let private fixture () =
        let directory = Path.Combine(Path.GetTempPath(), "fsgg-3210-cli-" + Guid.NewGuid().ToString("n"))
        Directory.CreateDirectory directory |> ignore
        let input = $"""{{"schema":"%s{RoadmapWorkUnit.PreparationInputSchema}","authorityIssue":"https://github.com/FS-GG/.github/issues/3210","sddWorkId":"3210-roadmap-work-unit-compiler","registrationOwner":"FS-GG","registrationRepository":".github","registrationPaths":["src/FS.GG.Coord.Core"]}}"""
        let roadmap = "- [x] **GS2-07.2 — Previous.** done\n- [ ] **GS2-07.3 — Compiler.** next\n"
        let roadmapDigest = SHA256.HashData(Encoding.UTF8.GetBytes roadmap) |> Convert.ToHexString |> _.ToLowerInvariant()
        let unit id title prerequisites qGates commands =
            let unsigned = $"""{{"exitGate":"test","gateCommands":%s{commands},"id":"%s{id}","owner":"FS.GG.Coordination","permissionCeiling":["test"],"prerequisites":%s{prerequisites},"qGates":%s{qGates},"title":"%s{title}"}}"""
            let contractDigest = SHA256.HashData(Encoding.UTF8.GetBytes unsigned) |> Convert.ToHexString |> _.ToLowerInvariant()
            unsigned[..unsigned.Length - 2] + $",\"contractSha256\":\"%s{contractDigest}\"}}"
        let catalog =
            $"""{{"schema":"fsgg.coordination.roadmap-index/1","roadmap":{{"repository":"FS-GG/.github","revision":"%s{String.replicate 40 "a"}","path":"docs/github-substrate-v2-roadmap.md","sha256":"%s{roadmapDigest}"}},"units":[%s{unit "GS2-07.2" "Previous" "[\"GS2-07.1\"]" "[]" "[\"previous\"]"},%s{unit "GS2-07.3" "Compiler" "[\"GS2-07.2\"]" "[\"implementation\"]" "[\"acceptance\"]"}]}}"""
        let registry = "before\n<!-- fsgg:roadmap-registration/GS2-07.3 -->\nold\n<!-- /fsgg:roadmap-registration/GS2-07.3 -->\nafter\n"
        let inputPath, roadmapPath, catalogPath = Path.Combine(directory, "input.json"), Path.Combine(directory, "roadmap.md"), Path.Combine(directory, "catalog.json")
        let sourcePath, candidatePath = Path.Combine(directory, "registry.md"), Path.Combine(directory, "candidate.md")
        File.WriteAllText(inputPath, input, UTF8Encoding(false)); File.WriteAllText(roadmapPath, roadmap, UTF8Encoding(false))
        File.WriteAllText(catalogPath, catalog, UTF8Encoding(false)); File.WriteAllText(sourcePath, registry, UTF8Encoding(false))
        { new IDisposable with member _.Dispose() = Directory.Delete(directory, true) }, inputPath, roadmapPath, catalogPath, sourcePath, candidatePath

    let private qualification subject candidate =
        let tool: Qualification.ToolIdentity = { Id = "dotnet"; Version = "10.0"; Sha256 = sha '1' }
        let executor: Qualification.ExecutorIdentity = { Id = "implementer"; Role = "implementer"; ImplementationSha256 = sha '2' }
        let operation id kind : Qualification.OperationEvidence =
            { Id = id; Kind = kind; SubjectRevision = candidate; Tool = tool; Executor = executor
              CommandSha256 = sha '3'; ArtifactSha256 = [ sha '4' ]; ResultSha256 = sha '5'
              ReplayResultSha256 = if kind = Qualification.FixedPoint then Some(sha '5') else None
              ExitCode = if kind = Qualification.Mutation then 3 else 0
              Refusal = if kind = Qualification.Mutation then Some "REFUSED inverted" else None }
        let checks: Qualification.HostedCheck list = [ { Scope = "check"; Id = "1"; SubjectRevision = candidate; State = "completed"; Conclusion = "success" } ]
        ({ Schema = Qualification.InputSchema; Subject = subject; SubjectRevision = candidate; CheckoutClean = true
           ToolManifest = [ tool ]; Executor = executor
           Operations = [ operation "analyze" Qualification.Analyze; operation "verify" Qualification.Verify; operation "ship" Qualification.Ship; operation "hosted" Qualification.Hosted; operation "fixed" Qualification.FixedPoint; operation "mutation" Qualification.Mutation ]
           Claims = [ { Id = "all"; SubjectRevision = candidate; RequiredKinds = [ Qualification.Analyze; Qualification.Verify; Qualification.Ship; Qualification.Hosted; Qualification.FixedPoint ]; EvidenceIds = [ "analyze"; "verify"; "ship"; "hosted"; "fixed" ] } ]
           Mutations = [ { Id = "inverted"; OperationId = "mutation"; ExpectedRefusal = "REFUSED inverted"; ObservedRefusal = "REFUSED inverted"; ProductionImplementationSha256 = sha '2'; FixtureImplementationSha256 = sha '6'; FixtureExecutorId = "fixture"; FixtureExecutorRole = "mutation-fixture" } ]
           HostedObservations = [ { Complete = true; Checks = checks }; { Complete = true; Checks = checks } ]
           Obligations = { HeadSha = candidate; Declarations = [ Qualification.NoObligations ]; Readbacks = [ { CommentId = 1L; Url = "https://github.com/FS-GG/.github/pull/1#issuecomment-1"; Author = "bot" } ] }
           SemanticReview = { SubjectRevision = candidate; Accepted = true; Evidence = "https://github.com/FS-GG/.github/pull/1#issuecomment-2" } } : Qualification.Input)
        |> Qualification.validate |> unwrap

    let private lifecycle candidate =
        let draft order phase event at actual usage =
            let sourceRevision =
                match phase with
                | "merge" -> head 'b'
                | "acceptance" -> head 'd'
                | _ -> candidate
            $"""{{"schema_version":1,"run_id":"roadmap-unit-gs2-07.3","unit_id":"GS2-07.3","item":{{"repo":"FS-GG/.github","number":500,"url":"https://github.com/FS-GG/.github/issues/500"}},"phase_order":%d{order},"phase":"%s{phase}","event":"%s{event}","at":"%s{at}","actor":"worker-1","model":{{"status":"recorded","provider":"OpenAI","name":"gpt","effort":"medium","source":"test"}},"source":{{"repository":"FS-GG/.github","revision":"%s{sourceRevision}"}},"evidence":["test:evidence"],"actual_minutes":%s{actual},"historical_durations_minutes":[],"historical_average_minutes":null,"token_usage":%s{usage},"tooling":{{"ledger_schema":1,"runtime":{{"status":"recorded","name":"codex","version":"1","source":"test"}},"coordination":{{"status":"recorded","name":"coord","version":"1","source":"test"}},"sdd":{{"status":"recorded","name":"sdd","version":"1","source":"test"}},"contracts":{{"status":"recorded","name":"contracts","version":"1","source":"test"}}}},"authority":{{"kind":"github_issue_comment","subject":"FS-GG/.github#500","claim_generation":"1"}}}}"""
        [ "intake"; "claim"; "sdd-analyze"; "implementation"; "sdd-verify"; "sdd-ship"
          "qualification"; "review"; "host-acceptance"; "merge"; "acceptance" ]
        |> List.mapi (fun index phase -> index + 1, phase)
        |> List.fold (fun log (order, phase) ->
            let first = LifecycleTelemetry.sealSuccessor "roadmap-unit-gs2-07.3" "GS2-07.3" log (draft order phase "started" "2026-09-05T06:00:00Z" "null" "{\"status\":\"pending\"}") |> unwrap
            let current = log + first
            current + (LifecycleTelemetry.sealSuccessor "roadmap-unit-gs2-07.3" "GS2-07.3" current (draft order phase "completed" "2026-09-05T06:01:00Z" "1" "{\"status\":\"unavailable\",\"reason\":\"test fixture\",\"source\":\"test\"}") |> unwrap)) ""

    let private acceptanceInput (plan: RoadmapWorkUnit.PreparationPlan) =
        let candidate = head 'a'
        let applied =
            plan.Registrations
            |> List.mapi (fun index registration ->
                let number = 500 + index
                ({ Id = registration.Id; Kind = registration.Kind; DraftSha256 = IntakeReceipt.digest registration.Draft
                   Issue = $"FS-GG/.github#%d{number}"; IssueUrl = $"https://github.com/FS-GG/.github/issues/%d{number}" }
                 : RoadmapWorkUnit.AppliedRegistration))
        let application = RoadmapWorkUnit.sealPreparationApplication plan applied |> unwrap
        let unitIssue = application.Registrations |> List.find (fun value -> value.Kind = "unit") |> _.Issue
        let identities: RoadmapWorkUnit.RevisionIdentities =
            { ImplementationPullRequest = 1; ImplementationCandidate = candidate; ImplementationMerge = head 'b'
              AcceptancePullRequest = 2; AcceptanceCandidate = head 'c'; AcceptanceMerge = head 'd'; ProtectedMain = head 'd' }
        let binding candidate merge tree = RoadmapWorkUnit.sealRevisionBinding "FS-GG/.github" candidate merge tree tree 0
        let observation stage status : RoadmapWorkUnit.SddObservation = { Stage = stage; SubjectRevision = candidate; ArtifactJson = $"""{{"schemaVersion":1,"viewVersion":"1.0","generator":"FS.GG.SDD.Artifacts/1.5.0","sources":[{{"path":"readiness/500-roadmap-gs2-07-3/work-model.json"}}],"findings":[],"diagnostics":[],"stage":"%s{stage}","status":"%s{status}","readiness":"%s{status}","workId":"500-roadmap-gs2-07-3"}}""" }
        let critique = $"""{{"schema_version":3,"cycle_id":"GS2-07.3","milestone":"GS2-07.3","critic":"critic-1","initial_reviewed_commit":"%s{candidate}","scope":["requirements","diff","tests","architecture","roadmap-evidence"],"initial_verdict":"pass","game_functionality":false,"entry_point_not_test_ownable":false,"entry_point_not_test_ownable_reason":null,"player_journeys":[],"uncovered_functionality":[],"repair_rounds":0,"reviewed_commits":["%s{candidate}"],"findings":[],"confirmation":{{"reviewed_commit":"%s{candidate}","verdict":"pass","unresolved_blocker_major":[]}},"human_escalation":null}}"""
        { Schema = RoadmapWorkUnit.AcceptanceInputSchema; Plan = plan; PreparationApplication = application; Qualification = qualification unitIssue candidate
          LifecycleRunId = "roadmap-unit-gs2-07.3"; LifecycleUnitId = "GS2-07.3"; LifecycleLog = lifecycle candidate
          RequiredLifecyclePhases =
            [ "intake"; "claim"; "sdd-analyze"; "implementation"; "sdd-verify"; "sdd-ship"
              "qualification"; "review"; "host-acceptance"; "merge"; "acceptance" ]
          LifecycleUsageReceipts = []
          LifecycleHistoryReport = "phase,tooling_fingerprint,actual_minutes,source\n"
          ReviewEvidence = "https://github.com/FS-GG/.github/pull/1#issuecomment-2"
          StructuredReviewEvidence = "https://github.com/FS-GG/.github/pull/1#issuecomment-2"
          ReviewCycleId = "GS2-07.3"; ReviewReceipt = critique
          SddWorkId = "500-roadmap-gs2-07-3"; SddObservations = [ observation "analyze" "implementationReady"; observation "verify" "verificationReady"; observation "ship" "shipReady" ]
          Identities = identities; ImplementationBinding = binding identities.ImplementationCandidate identities.ImplementationMerge (head 'e')
          AcceptanceBinding = binding identities.AcceptanceCandidate identities.AcceptanceMerge (head 'f'); AcceptedAt = "2026-09-05T06:02:00Z" } : RoadmapWorkUnit.AcceptanceInput

    [<Fact>]
    let ``#3210 prepare inspect render verify is a deterministic CLI round trip`` () =
        let disposable, input, roadmap, catalog, source, candidate = fixture ()
        use _ = disposable
        let common = [ "--input"; input; "--roadmap"; roadmap; "--catalog"; catalog ]
        let inspectCode, plan, inspectError = invoke ([ "roadmap"; "unit"; "prepare"; "inspect" ] @ common)
        Assert.Equal(ExitCode.toInt ExitCode.Green, inspectCode); Assert.Equal("", inspectError)
        Assert.Contains("\"schema\":\"fsgg.roadmap-unit.preparation-plan/1\"", plan)
        let renderCode, _, renderError = invoke ([ "roadmap"; "unit"; "prepare"; "render" ] @ common @ [ "--registry"; source; "--output"; candidate ])
        Assert.Equal(ExitCode.toInt ExitCode.Green, renderCode); Assert.Equal("", renderError)
        let verifyCode, stdout, verifyError = invoke ([ "roadmap"; "unit"; "prepare"; "verify" ] @ common @ [ "--source-registry"; source; "--registry"; candidate ])
        Assert.Equal(ExitCode.toInt ExitCode.Green, verifyCode); Assert.Equal("", verifyError)
        Assert.Contains("FSGG-ROADMAP-UNIT-PREPARATION-VERIFIED GS2-07.3", stdout)

    [<Fact>]
    let ``#3210 pure acceptance actions emit only an internally coherent candidate`` () =
        let disposable, request, roadmap, catalog, _, _ = fixture ()
        use _ = disposable
        let _, planJson, _ = invoke [ "roadmap"; "unit"; "prepare"; "inspect"; "--input"; request; "--roadmap"; roadmap; "--catalog"; catalog ]
        let plan = RoadmapWorkUnit.parsePlan (Encoding.UTF8.GetBytes planJson) |> unwrap
        let input = acceptanceInput plan
        let directory = Path.GetDirectoryName request
        let inputPath, candidatePath = Path.Combine(directory, "acceptance.json"), Path.Combine(directory, "candidate.json")
        File.WriteAllText(inputPath, RoadmapWorkUnit.canonicalAcceptanceInput input, UTF8Encoding(false))
        let inspectCode, stdout, inspectError = invoke [ "roadmap"; "unit"; "accept"; "inspect"; "--input"; inputPath ]
        Assert.Equal(ExitCode.toInt ExitCode.Green, inspectCode)
        Assert.Equal("", inspectError)
        Assert.Contains("\"verdict\":\"internally-coherent-candidate\"", stdout)
        Assert.DoesNotContain("\"verdict\":\"accepted\"", stdout)
        let renderCode, _, renderError = invoke [ "roadmap"; "unit"; "accept"; "render"; "--input"; inputPath; "--output"; candidatePath ]
        Assert.Equal(ExitCode.toInt ExitCode.Green, renderCode); Assert.Equal("", renderError)
        let verifyCode, verified, verifyError = invoke [ "roadmap"; "unit"; "accept"; "verify"; "--input"; inputPath; "--bundle"; candidatePath ]
        Assert.Equal(ExitCode.toInt ExitCode.Green, verifyCode); Assert.Equal("", verifyError)
        Assert.Contains("FSGG-ROADMAP-UNIT-CANDIDATE-VERIFIED GS2-07.3", verified)
        Assert.DoesNotContain("ACCEPTANCE-VERIFIED", verified)

    [<Fact>]
    let ``#3210 prepare refuses unrecognized arguments before file IO`` () =
        let code, stdout, stderr = invoke [ "roadmap"; "unit"; "prepare"; "inspect"; "--input"; "missing"; "--invented" ]
        Assert.NotEqual(ExitCode.toInt ExitCode.Green, code); Assert.Equal("", stdout)
        Assert.Contains("unrecognized argument '--invented'", stderr)

    [<Fact>]
    let ``#3210 revision inspect emits a sealed binding from observed Git identities`` () =
        let repository = Directory.GetCurrentDirectory()
        let merge = git repository [ "rev-parse"; "HEAD" ]
        // A squash merge is accepted by tree identity, not ancestry.  Using the same immutable commit
        // for both sides isolates this command's observation/sealing contract from this test checkout's
        // unrelated parent-tree contents.
        let candidate = merge
        let code, stdout, stderr =
            invoke
                [ "roadmap"; "unit"; "revision"; "inspect"
                  "--repository"; repository; "--repository-id"; "FS-GG/.github"
                  "--candidate"; candidate; "--merge"; merge ]
        Assert.Equal(ExitCode.toInt ExitCode.Green, code)
        Assert.Equal("", stderr)
        let binding = RoadmapWorkUnit.parseRevisionBinding (Encoding.UTF8.GetBytes stdout) |> unwrap
        Assert.Equal(candidate, binding.Candidate)
        Assert.Equal(merge, binding.Merge)
        Assert.Equal(0, binding.ExitCode)
