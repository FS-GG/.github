namespace FS.GG.Coord.Cli.Tests

open System
open System.Diagnostics
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json
open Xunit
open FS.GG.Coord
open FS.GG.Coord.Cli
open FS.GG.Coord.GitHub

module TelemetryCliTests =
    let private invoke args =
        let previousOut, previousError = Console.Out, Console.Error
        use stdout = new StringWriter()
        use stderr = new StringWriter()
        try
            Console.SetOut stdout
            Console.SetError stderr
            let code = Program.main (List.toArray args)
            code, stdout.ToString(), stderr.ToString()
        finally
            Console.SetOut previousOut
            Console.SetError previousError

    let private temporary (content: string) =
        let path = Path.Combine(Path.GetTempPath(), "fsgg-3208-" + Guid.NewGuid().ToString("n"))
        File.WriteAllText(path, content, UTF8Encoding(false))
        { new IDisposable with member _.Dispose() = File.Delete path }, path

    let private shaText (content: string) = SHA256.HashData(Encoding.UTF8.GetBytes content) |> Convert.ToHexString |> _.ToLowerInvariant()
    let private shaFile (path: string) = SHA256.HashData(File.ReadAllBytes path) |> Convert.ToHexString |> _.ToLowerInvariant()
    let private command (workingDirectory: string) (executable: string) (arguments: string list) =
        let start = ProcessStartInfo(executable, WorkingDirectory = workingDirectory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true)
        arguments |> List.iter start.ArgumentList.Add
        use childProcess = Process.Start start
        let stdout, stderr = childProcess.StandardOutput.ReadToEnd(), childProcess.StandardError.ReadToEnd()
        childProcess.WaitForExit()
        if childProcess.ExitCode <> 0 then failwithf "%s %A failed: %s" executable arguments stderr
        stdout.Trim()
    let private sealedReceipt (payload: string) = payload[..payload.Length - 2] + $",\"digest\":\"%s{shaText payload}\"}}"
    let private roadmapEvidence digest =
        let directory = Path.Combine(Path.GetTempPath(), "fsgg-3208-evidence-" + Guid.NewGuid().ToString("n"))
        Directory.CreateDirectory directory |> ignore
        let write (name: string) (content: string) = File.WriteAllText(Path.Combine(directory, name), content, UTF8Encoding(false))
        let candidate, implementation, acceptance = String.replicate 40 "a", String.replicate 40 "b", String.replicate 40 "c"
        let report = "---\nfeedbackSchema: 2\ncycle: cycle-1\n---\n## §1 Provenance and confidence\n- **activation:** active\n- **phases:** claim, implementation\n- **material events:** 0\n- **zero-event reason:** exercised surfaces produced no material event\n## §2 Findings\nNone.\n"
        let audit = $"""{{"auditSchema":1,"report":"report.md","reportSha256":"%s{shaText report}","findings":[]}}"""
        let critique = $"""{{"schema_version":3,"cycle_id":"cycle-1","milestone":"GS2-01.1","critic":"critic-1","initial_reviewed_commit":"%s{candidate}","scope":["requirements","diff","tests","architecture","roadmap-evidence"],"initial_verdict":"pass","game_functionality":false,"entry_point_not_test_ownable":false,"entry_point_not_test_ownable_reason":null,"player_journeys":[],"uncovered_functionality":[],"repair_rounds":0,"reviewed_commits":["%s{candidate}"],"findings":[],"confirmation":{{"reviewed_commit":"%s{candidate}","verdict":"pass","unresolved_blocker_major":[]}},"human_escalation":null}}"""
        let filler = String.replicate 64 "d"
        write "accepted.json" (sealedReceipt $"""{{"acceptedAt":"2026-01-01T00:00:00Z","artifacts":[{{"name":"implementation-candidate-%s{candidate}","sha256":"%s{filler}"}}],"schema":"fsgg.coordination.unit-acceptance/1","sourceRevision":"%s{candidate}","state":"accepted","unitContractSha256":"%s{filler}","unitId":"GS2-01.1"}}""")
        write "delivery.json" (sealedReceipt $"""{{"acceptanceMergeHead":"%s{acceptance}","candidateHead":"%s{candidate}","claimsRemaining":0,"implementationMergeHead":"%s{implementation}","issueUrl":"https://github.com/FS-GG/repo/issues/1","pullRequestUrl":"https://github.com/FS-GG/repo/pull/2","schema":"fsgg.roadmap.delivery/1","unitId":"GS2-01.1"}}""")
        write "critique.json" critique
        write "report.md" report
        write "audit.json" audit
        write "feedback.json" (sealedReceipt $"""{{"auditSha256":"%s{shaText audit}","cycleId":"cycle-1","head":"%s{acceptance}","reportSha256":"%s{shaText report}","schema":"fsgg.roadmap.feedback-binding/1","unitId":"GS2-01.1"}}""")
        write "cycle.json" (sealedReceipt $"""{{"cycleId":"cycle-1","head":"%s{acceptance}","schema":"fsgg.roadmap.cycle-update/1","unitId":"GS2-01.1"}}""")
        write "check.json" (sealedReceipt $"""{{"head":"%s{acceptance}","name":"required","owner":null,"passed":true,"required":true,"schema":"fsgg.roadmap.check/1","unitId":"GS2-01.1"}}""")
        let manifest = $"""{{"unitId":"GS2-01.1","title":"Typed thing","roadmapSourceDigest":"%s{digest}","acceptedReceiptPath":"accepted.json","deliveryReceiptPath":"delivery.json","critiquePath":"critique.json","feedbackReportPath":"report.md","feedbackAuditPath":"audit.json","feedbackPhases":["claim","implementation"],"feedbackBindingPath":"feedback.json","cycleUpdatePath":"cycle.json","checkReceiptPaths":["check.json"]}}"""
        write "manifest.json" manifest
        { new IDisposable with member _.Dispose() = Directory.Delete(directory, true) }, Path.Combine(directory, "manifest.json")

    let private codex total =
        let counts = $"""{{"input_tokens":10,"cached_input_tokens":4,"cache_write_input_tokens":0,"output_tokens":5,"reasoning_output_tokens":2,"total_tokens":%d{total}}}"""
        [ """{"timestamp":"2026-01-01T00:00:00Z","type":"session_meta","payload":{"cli_version":"1.2.3"}}"""
          """{"timestamp":"2026-01-01T00:00:00Z","type":"turn_context","payload":{"turn_id":"turn-1","model":"gpt-test-sol","effort":"high"}}"""
          $"""{{"timestamp":"2026-01-01T00:01:00Z","type":"token_usage_record","payload":{{"thread_id":"thread-1","turn_id":"turn-1","session_id":"session-1","response_id":"response-1","usage":%s{counts},"turn_token_usage":%s{counts},"thread_token_usage":%s{counts}}}}}""" ] |> String.concat "\n"

    let private qualificationInput checkoutClean =
        let digest c = String.replicate 64 (string c)
        let revision = String.replicate 40 "1"
        let tool = {| id = "dotnet"; version = "10.0.0"; sha256 = digest 'a' |}
        let executor = {| id = "executor-primary"; role = "implementer"; implementationSha256 = digest 'b' |}
        let operation id kind exitCode refusal replay =
            {| id = id; kind = kind; subjectRevision = revision; tool = tool; executor = executor
               commandSha256 = digest 'c'; artifactSha256 = [ digest 'd' ]; resultSha256 = digest 'e'
               replayResultSha256 = replay; exitCode = exitCode; refusal = refusal |}
        JsonSerializer.Serialize
            {| schema = Qualification.InputSchema; subject = "FS-GG/.github#3209"; subjectRevision = revision
               checkoutClean = checkoutClean; toolManifest = [ tool ]; executor = executor
               operations =
                 [ operation "analyze" "analyze" 0 None None
                   operation "verify" "verify" 0 None None
                   operation "ship" "ship" 0 None None
                   operation "hosted" "hosted" 0 None None
                   operation "fixed" "fixed-point" 0 None (Some(digest 'e'))
                   operation "mutation" "mutation" 3 (Some "REFUSED wrong subject") None ]
               claims = [ {| id = "all"; subjectRevision = revision; requiredKinds = [ "analyze"; "verify"; "ship"; "hosted"; "fixed-point" ]; evidenceIds = [ "analyze"; "verify"; "ship"; "hosted"; "fixed" ] |} ]
               mutations = [ {| id = "wrong-subject"; operationId = "mutation"; expectedRefusal = "REFUSED wrong subject"; observedRefusal = "REFUSED wrong subject"; productionImplementationSha256 = digest 'b'; fixtureImplementationSha256 = digest 'f'; fixtureExecutorId = "fixture-executor"; fixtureExecutorRole = "mutation-fixture" |} ]
               hostedObservations =
                 [ {| complete = true; checks = [ {| scope = "check"; id = "1"; subjectRevision = revision; state = "completed"; conclusion = "success" |} ] |}
                   {| complete = true; checks = [ {| scope = "check"; id = "1"; subjectRevision = revision; state = "completed"; conclusion = "success" |} ] |} ]
               obligations =
                 {| headSha = revision
                    declarations = [ {| kind = "none"; ids = List.empty<string> |} ]
                    readback = Some {| commentId = 1L; url = "https://github.com/FS-GG/.github/pull/1#issuecomment-1"; author = "github-actions[bot]" |} |}
               semanticReview = {| subjectRevision = revision; accepted = true; evidence = "https://github.com/FS-GG/.github/pull/1#issuecomment-1" |} |}

    let private qualificationRunFixture () =
        let directory = Path.Combine(Path.GetTempPath(), "fsgg-3209-run-" + Guid.NewGuid().ToString("n"))
        Directory.CreateDirectory directory |> ignore
        Directory.CreateDirectory(Path.Combine(directory, "evidence")) |> ignore
        File.WriteAllText(Path.Combine(directory, ".gitignore"), "evidence/\ninput.json\nexecution.json\n", UTF8Encoding(false))
        let toolPath = Path.Combine(directory, "qualification-tool.sh")
        File.WriteAllText(
            toolPath,
            "#!/bin/sh\nif [ \"$1\" = \"--version\" ]; then printf 'fixture-tool 1.0\\n'; exit 0; fi\nif [ \"$1\" = \"mutation\" ]; then printf 'REFUSED wrong subject\\n' >&2; exit 3; fi\nif [ \"$1\" = \"hosted\" ]; then printf '%s' \"$3\" > \"$2\"; printf '%s' \"$3\" > \"$4\"; printf '%s' \"$6\" > \"$5\"; printf 'hosted\\n'; exit 0; fi\nif [ \"$1\" = \"fixed-unstable\" ]; then if [ -f \"$2\" ]; then printf 'changed' > \"$2\"; else printf 'first' > \"$2\"; fi; printf 'stable\\n'; exit 0; fi\nprintf '%s\\n' \"$1\"\nprintf '%s' \"$1\" > \"$2\"\n",
            UTF8Encoding(false))
        File.SetUnixFileMode(toolPath, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute)
        let fixturePath = Path.Combine(directory, "mutation-fixture.sh")
        File.WriteAllText(fixturePath, "#!/bin/sh\nprintf 'REFUSED wrong subject\\n' >&2\nexit 7\n", UTF8Encoding(false))
        File.SetUnixFileMode(fixturePath, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute)
        command directory "/usr/bin/git" [ "init"; "--quiet" ] |> ignore
        command directory "/usr/bin/git" [ "add"; ".gitignore"; "qualification-tool.sh"; "mutation-fixture.sh" ] |> ignore
        command directory "/usr/bin/git" [ "-c"; "user.name=Fixture"; "-c"; "user.email=fixture@example.invalid"; "commit"; "--quiet"; "-m"; "fixture" ] |> ignore
        let revision = command directory "/usr/bin/git" [ "rev-parse"; "HEAD" ]
        let digest c = String.replicate 64 (string c)
        let fixtureTool = {| id = "fixture"; version = "fixture-tool 1.0"; sha256 = shaFile toolPath |}
        let gitTool = {| id = "git"; version = command directory "/usr/bin/git" [ "--version" ]; sha256 = shaFile "/usr/bin/git" |}
        let executor = {| id = "executor-primary"; role = "implementer"; implementationSha256 = digest 'b' |}
        let operation id kind exitCode refusal replay =
            {| id = id; kind = kind; subjectRevision = revision; tool = fixtureTool; executor = executor
               commandSha256 = digest 'c'; artifactSha256 = [ digest 'd' ]; resultSha256 = digest 'e'
               replayResultSha256 = replay; exitCode = exitCode; refusal = refusal |}
        let input =
            JsonSerializer.Serialize
                {| schema = Qualification.InputSchema; subject = "FS-GG/.github#3209"; subjectRevision = revision
                   checkoutClean = true; toolManifest = [ fixtureTool; gitTool ]; executor = executor
                   operations =
                     [ operation "analyze" "analyze" 0 None None
                       operation "verify" "verify" 0 None None
                       operation "ship" "ship" 0 None None
                       operation "hosted" "hosted" 0 None None
                       operation "fixed" "fixed-point" 0 None (Some(digest 'e'))
                       operation "mutation" "mutation" 3 (Some "REFUSED wrong subject") None ]
                   claims = [ {| id = "all"; subjectRevision = revision; requiredKinds = [ "analyze"; "verify"; "ship"; "hosted"; "fixed-point" ]; evidenceIds = [ "analyze"; "verify"; "ship"; "hosted"; "fixed" ] |} ]
                   mutations = [ {| id = "wrong-subject"; operationId = "mutation"; expectedRefusal = "REFUSED wrong subject"; observedRefusal = "pending"; productionImplementationSha256 = digest 'b'; fixtureImplementationSha256 = digest 'f'; fixtureExecutorId = "fixture-executor"; fixtureExecutorRole = "mutation-fixture" |} ]
                   hostedObservations =
                     [ {| complete = true; checks = [ {| scope = "check"; id = "1"; subjectRevision = revision; state = "completed"; conclusion = "success" |} ] |}
                       {| complete = true; checks = [ {| scope = "check"; id = "1"; subjectRevision = revision; state = "completed"; conclusion = "success" |} ] |} ]
                   obligations =
                     {| headSha = revision
                        declarations = [ {| kind = "none"; ids = List.empty<string> |} ]
                        readback = Some {| commentId = 1L; url = "https://github.com/FS-GG/.github/pull/1#issuecomment-1"; author = "template" |} |}
                   semanticReview = {| subjectRevision = revision; accepted = true; evidence = "https://github.com/FS-GG/.github/pull/1#issuecomment-1" |} |}
        let operation id artifact =
            {| id = id
               arguments = (if id = "mutation" then [ "mutation" ] else [ id; artifact ])
               artifacts = (if id = "mutation" then [] else [ artifact ]) |}
        let hostedPath1, hostedPath2, obligationPath = "evidence/hosted-1.json", "evidence/hosted-2.json", "evidence/obligation.txt"
        let hostedJson =
            JsonSerializer.Serialize
                {| schema = QualificationEvidence.HostedSchema; complete = true
                   items = [ {| scope = "check"; id = "1"; headSha = revision; state = "completed"; conclusion = Some "success" |} ] |}
        let obligationComment = QualificationEvidence.renderObligationComment revision Qualification.NoObligations
        let obligationReadback =
            JsonSerializer.Serialize
                {| schema = QualificationEvidence.ObligationReadbackSchema
                   commentId = 123L
                   url = "https://github.com/FS-GG/.github/pull/3221#issuecomment-123"
                   author = "github-actions[bot]"
                   body = obligationComment |}
        let hostedOperation =
            {| id = "hosted"; arguments = [ "hosted"; hostedPath1; hostedJson; hostedPath2; obligationPath; obligationReadback ]
               artifacts = [ hostedPath1; hostedPath2; obligationPath ] |}
        let execution =
            JsonSerializer.Serialize
                {| schema = "fsgg.qualification.execution/1"; checkout = directory; timeoutSeconds = 10
                   environment = [ {| name = "HOME"; value = directory |}; {| name = "LANG"; value = "C" |} ]
                   executor = {| id = "executor-primary"; role = "implementer" |}
                   tools =
                     [ {| id = "fixture"; path = toolPath; versionArguments = [ "--version" ] |}
                       {| id = "git"; path = "/usr/bin/git"; versionArguments = [ "--version" ] |} ]
                   operations =
                     [ operation "analyze" "evidence/analyze.txt"
                       operation "verify" "evidence/verify.txt"
                       operation "ship" "evidence/ship.txt"
                       hostedOperation
                       operation "fixed" "evidence/fixed.txt"
                       operation "mutation" "" ]
                   fixtures = [ {| mutationId = "wrong-subject"; executorId = "fixture-executor"; executorRole = "mutation-fixture"; path = fixturePath; arguments = List.empty<string> |} ]
                   hostedObservationPaths = [ hostedPath1; hostedPath2 ]
                   obligationCommentPaths = [ obligationPath ] |}
        let inputPath, executionPath = Path.Combine(directory, "input.json"), Path.Combine(directory, "execution.json")
        File.WriteAllText(inputPath, input, UTF8Encoding(false))
        File.WriteAllText(executionPath, execution, UTF8Encoding(false))
        { new IDisposable with member _.Dispose() = Directory.Delete(directory, true) }, inputPath, executionPath

    [<Fact>]
    let ``#3208 packaged Codex collector emits the frozen CSV contract`` () =
        let disposable, path = temporary (codex 15)
        use _ = disposable
        let code, stdout, stderr =
            invoke [ "telemetry"; "usage"; "collect"; "codex"; "--session-file"; path; "--task"; "repo#1/claim"; "--coord-version"; "4.5.6"; "--sdd-version"; "7.8.9"; "--contracts-version"; "10.0.0" ]
        Assert.Equal(ExitCode.toInt ExitCode.Green, code)
        Assert.Equal("", stderr)
        Assert.StartsWith("timestamp,task,session_id,thread_id,turn_id,response_id,provider,model,effort,runtime_version,coordination_version,sdd_version,contracts_version,ledger_schema", stdout)
        Assert.Contains(",10,4,0,5,2,15,", stdout)
        Assert.DoesNotContain(path, stdout)

    [<Fact>]
    let ``#3208 malformed runtime arithmetic is a typed non-green refusal`` () =
        let disposable, path = temporary (codex 99)
        use _ = disposable
        let code, stdout, stderr =
            invoke [ "telemetry"; "usage"; "collect"; "codex"; "--session-file"; path; "--task"; "repo#1/claim"; "--coord-version"; "1"; "--sdd-version"; "1"; "--contracts-version"; "1" ]
        Assert.Equal("", stdout)
        Assert.NotEqual(ExitCode.toInt ExitCode.Green, code)
        Assert.Contains("total_tokens must equal", stderr)

    [<Fact>]
    let ``#3208 telemetry commands reject unknown missing-value and positional arguments`` () =
        let disposable, path = temporary (codex 15)
        use _ = disposable
        let common = [ "telemetry"; "usage"; "collect"; "codex"; "--session-file"; path; "--task"; "repo#1/claim"; "--coord-version"; "1"; "--sdd-version"; "1"; "--contracts-version"; "1" ]
        let cases =
            [ common @ [ "--unknown-flag" ], "unrecognized argument '--unknown-flag'"
              common @ [ "--format" ], "--format requires a value"
              common @ [ "unexpected" ], "unexpected positional argument 'unexpected'" ]
        for args, expected in cases do
            let code, stdout, stderr = invoke args
            Assert.NotEqual(ExitCode.toInt ExitCode.Green, code)
            Assert.Equal("", stdout)
            Assert.Contains(expected, stderr)

    [<Fact>]
    let ``#3208 CSV append preserves one header and deduplicates response identity`` () =
        let sessionDisposable, sessionPath = temporary (codex 15)
        let outputDirectory = Path.Combine(Path.GetTempPath(), "fsgg-3208-append-" + Guid.NewGuid().ToString("n"))
        let outputPath = Path.Combine(outputDirectory, "missing", "usage.csv")
        use _session = sessionDisposable
        use _output = { new IDisposable with member _.Dispose() = Directory.Delete(outputDirectory, true) }
        let args = [ "telemetry"; "usage"; "collect"; "codex"; "--session-file"; sessionPath; "--task"; "repo#1/claim"; "--coord-version"; "1"; "--sdd-version"; "1"; "--contracts-version"; "1"; "--append"; outputPath ]
        Assert.Equal(0, invoke args |> fun (code, _, _) -> code)
        Assert.Equal(0, invoke args |> fun (code, _, _) -> code)
        let lines = File.ReadAllLines outputPath
        Assert.Equal(2, lines.Length)
        Assert.StartsWith("timestamp,task,session_id", lines[0])

    [<Fact>]
    let ``#3208 roadmap render is bounded deterministic and does not mutate its input`` () =
        let roadmap = "before\n<!-- fsgg:roadmap-unit/GS2-01.1 -->\nold\n<!-- /fsgg:roadmap-unit/GS2-01.1 -->\nafter\n"
        let digest = "sha256:" + (SHA256.HashData(Encoding.UTF8.GetBytes roadmap) |> Convert.ToHexString |> _.ToLowerInvariant())
        let roadmapDisposable, roadmapPath = temporary roadmap
        let evidenceDisposable, evidencePath = roadmapEvidence digest
        use _roadmap = roadmapDisposable
        use _evidence = evidenceDisposable
        let args = [ "roadmap"; "close"; "render"; "--roadmap"; roadmapPath; "--source-digest"; digest; "--evidence"; evidencePath ]
        let firstCode, first, firstError = invoke args
        let secondCode, second, secondError = invoke args
        Assert.Equal(0, firstCode)
        Assert.Equal(firstCode, secondCode)
        Assert.Equal(first, second)
        Assert.Equal("", firstError + secondError)
        Assert.Equal(roadmap, File.ReadAllText roadmapPath)
        Assert.StartsWith("before\n", first)
        Assert.EndsWith("\nafter\n", first)

    [<Fact>]
    let ``#3208 duplicated roadmap markers refuse without output`` () =
        let roadmap = "<!-- fsgg:roadmap-unit/GS2-01.1 -->\n<!-- fsgg:roadmap-unit/GS2-01.1 -->\n<!-- /fsgg:roadmap-unit/GS2-01.1 -->"
        let digest = "sha256:" + (SHA256.HashData(Encoding.UTF8.GetBytes roadmap) |> Convert.ToHexString |> _.ToLowerInvariant())
        let roadmapDisposable, roadmapPath = temporary roadmap
        let evidenceDisposable, evidencePath = roadmapEvidence digest
        use _roadmap = roadmapDisposable
        use _evidence = evidenceDisposable
        let code, stdout, stderr = invoke [ "roadmap"; "close"; "render"; "--roadmap"; roadmapPath; "--source-digest"; digest; "--evidence"; evidencePath ]
        Assert.NotEqual(0, code)
        Assert.Equal("", stdout)
        Assert.Contains("markers", stderr)

    [<Fact>]
    let ``#3209 qualification CLI emits deterministic canonical acceptance`` () =
        let disposable, path = temporary (qualificationInput true)
        use _ = disposable
        let args = [ "telemetry"; "qualification"; "validate"; "--input"; path ]
        let firstCode, first, firstError = invoke args
        let secondCode, second, secondError = invoke args
        Assert.Equal(0, firstCode)
        Assert.Equal(firstCode, secondCode)
        Assert.Equal(first, second)
        Assert.Equal("", firstError + secondError)
        Assert.Contains("\"schema\":\"fsgg.qualification.result/1\"", first)
        Assert.Contains("\"operationCount\":6", first)
        Assert.DoesNotContain(path, first)

    [<Fact>]
    let ``#3209 qualification CLI rejects dirty checkout and unknown fields`` () =
        let dirtyDisposable, dirtyPath = temporary (qualificationInput false)
        use _dirty = dirtyDisposable
        let dirtyCode, dirtyOutput, dirtyError = invoke [ "telemetry"; "qualification"; "validate"; "--input"; dirtyPath ]
        Assert.NotEqual(0, dirtyCode)
        Assert.Equal("", dirtyOutput)
        Assert.Contains("DirtyCheckout", dirtyError)
        let unknown = (qualificationInput true).Replace("\"schema\":", "\"unknown\":true,\"schema\":")
        let unknownDisposable, unknownPath = temporary unknown
        use _unknown = unknownDisposable
        let unknownCode, unknownOutput, unknownError = invoke [ "telemetry"; "qualification"; "validate"; "--input"; unknownPath ]
        Assert.NotEqual(0, unknownCode)
        Assert.Equal("", unknownOutput)
        Assert.Contains("unknown fields: unknown", unknownError)

    [<Fact>]
    let ``#3209 qualification command contract rejects loose argv`` () =
        for args, expected in
            [ [ "telemetry"; "qualification"; "validate" ], "--input is required"
              [ "telemetry"; "qualification"; "validate"; "--wat" ], "unrecognized argument '--wat'"
              [ "telemetry"; "qualification"; "validate"; "loose" ], "unexpected positional argument 'loose'" ] do
            let code, stdout, stderr = invoke args
            Assert.NotEqual(0, code)
            Assert.Equal("", stdout)
            Assert.Contains(expected, stderr)

    [<Fact>]
    let ``#3209 qualification obligation commands render intent and verify authoritative readback`` () =
        let head = String.replicate 40 "1"
        let renderArgs = [ "telemetry"; "qualification"; "obligation"; "render"; "--head"; head; "--kind"; "none" ]
        let renderCode, body, renderError = invoke renderArgs
        Assert.Equal(0, renderCode)
        Assert.Equal("", renderError)
        Assert.Contains("fsgg:qualification-obligations/v1", body)
        let receipt =
            JsonSerializer.Serialize
                {| schema = QualificationEvidence.ObligationReadbackSchema
                   commentId = 77L
                   url = "https://github.com/FS-GG/.github/pull/3221#issuecomment-77"
                   author = "github-actions[bot]"
                   body = body |}
        let disposable, receiptPath = temporary receipt
        use _ = disposable
        let code, stdout, stderr =
            invoke [ "telemetry"; "qualification"; "obligation"; "verify"; "--head"; head; "--kind"; "none"; "--readback"; receiptPath ]
        Assert.Equal(0, code)
        Assert.Equal("", stderr)
        Assert.Contains("\"schema\":\"fsgg.qualification.obligation-verification/1\"", stdout)
        Assert.Contains("\"commentId\":77", stdout)

    [<Fact>]
    let ``#3209 qualification runner executes exact clean checkout and fixed point`` () =
        let disposable, inputPath, executionPath = qualificationRunFixture ()
        use _ = disposable
        let args = [ "telemetry"; "qualification"; "run"; "--input"; inputPath; "--execution"; executionPath ]
        let firstCode, first, firstError = invoke args
        Directory.Delete(Path.Combine(Path.GetDirectoryName(inputPath), "evidence"), true)
        Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(inputPath), "evidence")) |> ignore
        let secondCode, second, secondError = invoke args
        Assert.Equal(0, firstCode)
        Assert.Equal(firstCode, secondCode)
        Assert.Equal(first, second)
        Assert.Equal("", firstError + secondError)
        Assert.Contains("\"operationCount\":6", first)
        Assert.DoesNotContain(inputPath, first)

    [<Fact>]
    let ``#3209 qualification runner refuses dirty checkout before execution`` () =
        let disposable, inputPath, executionPath = qualificationRunFixture ()
        use _ = disposable
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(inputPath), "dirty.txt"), "dirty")
        let code, stdout, stderr = invoke [ "telemetry"; "qualification"; "run"; "--input"; inputPath; "--execution"; executionPath ]
        Assert.NotEqual(0, code)
        Assert.Equal("", stdout)
        Assert.Contains("DirtyCheckout", stderr)

    [<Fact>]
    let ``#3209 qualification runner refuses preexisting and reused artifacts`` () =
        let disposable, inputPath, executionPath = qualificationRunFixture ()
        use _ = disposable
        let execution = File.ReadAllText executionPath
        File.WriteAllText(executionPath, execution.Replace("evidence/verify.txt", "evidence/analyze.txt"))
        let code, stdout, stderr = invoke [ "telemetry"; "qualification"; "run"; "--input"; inputPath; "--execution"; executionPath ]
        Assert.NotEqual(0, code)
        Assert.Equal("", stdout)
        Assert.Contains("artifact is reused by unrelated operations", stderr)

    [<Fact>]
    let ``#3209 qualification runner requires two distinct hosted observation artifacts`` () =
        let disposable, inputPath, executionPath = qualificationRunFixture ()
        use _ = disposable
        let execution = File.ReadAllText executionPath
        File.WriteAllText(executionPath, execution.Replace("\"hostedObservationPaths\":[\"evidence/hosted-1.json\",\"evidence/hosted-2.json\"]", "\"hostedObservationPaths\":[\"evidence/hosted-1.json\",\"evidence/hosted-1.json\"]"))
        let code, stdout, stderr = invoke [ "telemetry"; "qualification"; "run"; "--input"; inputPath; "--execution"; executionPath ]
        Assert.NotEqual(0, code)
        Assert.Equal("", stdout)
        Assert.Contains("duplicate hosted observation path", stderr)

    [<Fact>]
    let ``#3209 qualification runner independently executes exact mutation fixture refusal`` () =
        let disposable, inputPath, executionPath = qualificationRunFixture ()
        use _ = disposable
        let execution = File.ReadAllText executionPath
        let fixturePath = Path.Combine(Path.GetDirectoryName(inputPath), "mutation-fixture.sh")
        File.WriteAllText(executionPath, execution.Replace(fixturePath, "/bin/false"))
        let code, stdout, stderr = invoke [ "telemetry"; "qualification"; "run"; "--input"; inputPath; "--execution"; executionPath ]
        Assert.NotEqual(0, code)
        Assert.Equal("", stdout)
        Assert.Contains("fixture 'wrong-subject' refusal did not match exactly", stderr)

    [<Fact>]
    let ``#3209 qualification runner requires a distinct mutation fixture role`` () =
        let disposable, inputPath, executionPath = qualificationRunFixture ()
        use _ = disposable
        let execution = File.ReadAllText executionPath
        File.WriteAllText(executionPath, execution.Replace("\"executorRole\":\"mutation-fixture\"", "\"executorRole\":\"implementer\""))
        let code, stdout, stderr = invoke [ "telemetry"; "qualification"; "run"; "--input"; inputPath; "--execution"; executionPath ]
        Assert.NotEqual(0, code)
        Assert.Equal("", stdout)
        Assert.Contains("reuses the production executor role", stderr)

    [<Fact>]
    let ``#3209 qualification runner refuses non-authoritative obligation readback receipts`` () =
        let disposable, inputPath, executionPath = qualificationRunFixture ()
        use _ = disposable
        let execution = File.ReadAllText executionPath
        File.WriteAllText(executionPath, execution.Replace("https://github.com/FS-GG/.github/pull/3221#issuecomment-123", "file:///tmp/asserted"))
        let code, stdout, stderr = invoke [ "telemetry"; "qualification"; "run"; "--input"; inputPath; "--execution"; executionPath ]
        Assert.NotEqual(0, code)
        Assert.Equal("", stdout)
        Assert.Contains("readback url must be an exact GitHub PR issuecomment URL", stderr)

    [<Fact>]
    let ``#3209 fixed point refuses replay that changes only artifact bytes`` () =
        let disposable, inputPath, executionPath = qualificationRunFixture ()
        use _ = disposable
        let execution = File.ReadAllText executionPath
        File.WriteAllText(executionPath, execution.Replace("[\"fixed\",\"evidence/fixed.txt\"]", "[\"fixed-unstable\",\"evidence/fixed.txt\"]"))
        let code, stdout, stderr = invoke [ "telemetry"; "qualification"; "run"; "--input"; inputPath; "--execution"; executionPath ]
        Assert.NotEqual(0, code)
        Assert.Equal("", stdout)
        Assert.Contains("changed artifact bytes", stderr)
