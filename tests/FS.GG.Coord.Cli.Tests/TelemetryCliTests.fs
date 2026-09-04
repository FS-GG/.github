namespace FS.GG.Coord.Cli.Tests

open System
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json
open Xunit
open FS.GG.Coord
open FS.GG.Coord.Cli

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
    let ``#3208 CSV append preserves one header and deduplicates response identity`` () =
        let sessionDisposable, sessionPath = temporary (codex 15)
        let outputDisposable, outputPath = temporary ""
        use _session = sessionDisposable
        use _output = outputDisposable
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
