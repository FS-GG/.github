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
    let ``#3208 roadmap render is bounded deterministic and does not mutate its input`` () =
        let roadmap = "before\n<!-- fsgg:roadmap-unit/GS2-01.1 -->\nold\n<!-- /fsgg:roadmap-unit/GS2-01.1 -->\nafter\n"
        let digest = "sha256:" + (SHA256.HashData(Encoding.UTF8.GetBytes roadmap) |> Convert.ToHexString |> _.ToLowerInvariant())
        let evidence = $"""{{"unitId":"GS2-01.1","title":"Typed thing","roadmapSourceDigest":"%s{digest}","acceptedReceiptDigest":"sha256:receipt","candidateHead":"candidate","implementationMergeHead":"implementation","acceptanceMergeHead":"acceptance","reviewHead":"candidate","feedbackHead":"acceptance","cycleId":"cycle-1","cycleUpdateDigest":"sha256:update","critiqueVerdict":"pass","repairRounds":0,"issueUrl":"https://github.com/FS-GG/repo/issues/1","pullRequestUrl":"https://github.com/FS-GG/repo/pull/2","claimsRemaining":0,"checks":[{{"name":"required","required":true,"passed":true,"owner":null}}]}}"""
        let roadmapDisposable, roadmapPath = temporary roadmap
        let evidenceDisposable, evidencePath = temporary evidence
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
        let evidence = $"""{{"unitId":"GS2-01.1","title":"Typed thing","roadmapSourceDigest":"%s{digest}","acceptedReceiptDigest":"r","candidateHead":"c","implementationMergeHead":"i","acceptanceMergeHead":"a","reviewHead":"c","feedbackHead":"a","cycleId":"cycle","cycleUpdateDigest":"u","critiqueVerdict":"pass","repairRounds":0,"issueUrl":"https://github.com/FS-GG/repo/issues/1","pullRequestUrl":"https://github.com/FS-GG/repo/pull/2","claimsRemaining":0,"checks":[]}}"""
        let roadmapDisposable, roadmapPath = temporary roadmap
        let evidenceDisposable, evidencePath = temporary evidence
        use _roadmap = roadmapDisposable
        use _evidence = evidenceDisposable
        let code, stdout, stderr = invoke [ "roadmap"; "close"; "render"; "--roadmap"; roadmapPath; "--source-digest"; digest; "--evidence"; evidencePath ]
        Assert.NotEqual(0, code)
        Assert.Equal("", stdout)
        Assert.Contains("markers", stderr)
