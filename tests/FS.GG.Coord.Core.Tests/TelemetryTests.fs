namespace FS.GG.Coord.Tests

open System
open System.Security.Cryptography
open System.Text
open System.Text.Json
open Xunit
open FS.GG.Coord

module TelemetryTests =
    let private bytes (value: string) = Encoding.UTF8.GetBytes value
    let private sha (value: byte array) = SHA256.HashData value |> Convert.ToHexString |> _.ToLowerInvariant()
    let private unwrap = function Ok value -> value | Error errors -> failwithf "%A" errors

    [<Fact>]
    let ``Codex collection preserves stable schema and exact counter arithmetic`` () =
        let counts = """{"input_tokens":10,"cached_input_tokens":4,"cache_write_input_tokens":0,"output_tokens":5,"reasoning_output_tokens":2,"total_tokens":15}"""
        let session =
            [ """{"timestamp":"2026-01-01T00:00:00Z","type":"session_meta","payload":{"cli_version":"1.2.3"}}"""
              """{"timestamp":"2026-01-01T00:00:00Z","type":"turn_context","payload":{"turn_id":"turn-1","model":"gpt-test-sol","effort":"high"}}"""
              $"""{{"timestamp":"2026-01-01T00:01:00Z","type":"token_usage_record","payload":{{"thread_id":"thread-1","turn_id":"turn-1","session_id":"session-1","response_id":"response-1","usage":%s{counts},"turn_token_usage":%s{counts},"thread_token_usage":%s{counts}}}}}""" ]
            |> String.concat "\n"
        let result = RuntimeUsage.collectCodex "repo#1/claim" None false None None "4.5.6" "7.8.9" "10.0.0" (bytes session) |> unwrap
        Assert.Single(result.Rows) |> ignore
        Assert.Equal("gpt-test-sol", result.Rows.Head.Model)
        Assert.Equal(15L, result.Rows.Head.Response.Total)
        Assert.Equal(2L, result.Rows.Head.Response.Reasoning.Value)
        Assert.StartsWith("codex-session-jsonl:sha256:", result.SourceDigest)
        Assert.StartsWith("timestamp,task,session_id,thread_id,turn_id,response_id,provider,model,effort,runtime_version,coordination_version,sdd_version,contracts_version,ledger_schema", RuntimeUsage.renderCsv result.Rows)

    [<Fact>]
    let ``Codex collection rejects malformed arithmetic and never exports source paths`` () =
        let invalid =
            [ """{"type":"session_meta","payload":{"cli_version":"1.2.3"}}"""
              """{"type":"turn_context","payload":{"turn_id":"turn-1","model":"gpt-test-sol"}}"""
              """{"timestamp":"2026-01-01T00:01:00Z","type":"token_usage_record","payload":{"thread_id":"thread-1","turn_id":"turn-1","session_id":"session-1","response_id":"response-1","usage":{"input_tokens":10,"cached_input_tokens":0,"cache_write_input_tokens":0,"output_tokens":5,"reasoning_output_tokens":0,"total_tokens":99},"turn_token_usage":{},"thread_token_usage":{}}}""" ] |> String.concat "\n"
        let result = RuntimeUsage.collectCodex "task" None false None None "1" "1" "1" (bytes invalid)
        Assert.True(Result.isError result)
        Assert.DoesNotContain("/home/", sprintf "%A" result)

    [<Fact>]
    let ``Claude collection binds model runtime and deterministic response identity`` () =
        let snapshot = """{"timestamp":"2026-01-01T00:02:00Z","session_id":"claude-session","prompt_id":"prompt-1","version":"2.3.4","model":{"id":"claude-test"},"effort":{"level":"high"},"context_window":{"current_usage":{"input_tokens":7,"cache_read_input_tokens":2,"cache_creation_input_tokens":1,"output_tokens":3}}}"""
        let first = RuntimeUsage.collectClaude "task" "1" "2" "3" (bytes snapshot) |> unwrap
        let second = RuntimeUsage.collectClaude "task" "1" "2" "3" (bytes snapshot) |> unwrap
        Assert.Equal(first.Rows.Head.ResponseId, second.Rows.Head.ResponseId)
        Assert.Equal("Anthropic", first.Rows.Head.Provider)
        Assert.Equal(13L, first.Rows.Head.Response.Total)
        Assert.Equal(None, first.Rows.Head.Response.Reasoning)

    [<Fact>]
    let ``lifecycle sealing is canonical and terminal pending usage is refused`` () =
        let common event at actual tokens =
            $"""{{"schema_version":1,"run_id":"run","unit_id":"unit","item":{{"repo":"FS-GG/.github","number":42,"url":"https://github.com/FS-GG/.github/issues/42"}},"phase_order":1,"phase":"claim","event":"%s{event}","at":"%s{at}","actor":"worker-1","model":{{"status":"recorded","provider":"OpenAI","name":"gpt-test","effort":"high","source":"runtime receipt"}},"source":{{"repository":"FS-GG/.github","revision":"%s{String.replicate 40 "a"}"}},"evidence":["receipt"],"actual_minutes":%s{actual},"historical_durations_minutes":[],"historical_average_minutes":null,"token_usage":%s{tokens},"tooling":{{"ledger_schema":1,"runtime":{{"status":"recorded","name":"codex","version":"1.2.3","source":"session"}},"coordination":{{"status":"recorded","name":"fsgg-coord","version":"4.5.6","source":"cli"}},"sdd":{{"status":"recorded","name":"fsgg-sdd","version":"7.8.9","source":"cli"}},"contracts":{{"status":"recorded","name":"fsgg-contracts","version":"10.0.0","source":"registry"}}}},"authority":{{"kind":"github_issue_comment","subject":"FS-GG/.github#42","claim_generation":"claim-1"}}}}"""
        let started = common "started" "2026-09-04T08:00:00Z" "null" "{\"status\":\"pending\"}"
        let sealedStarted = LifecycleTelemetry.sealSuccessor "run" "unit" "" started |> unwrap
        let repeat = LifecycleTelemetry.sealSuccessor "run" "unit" "" started |> unwrap
        Assert.Equal(sealedStarted, repeat)
        Assert.True(LifecycleTelemetry.validate "run" "unit" false [] sealedStarted |> Result.isOk)
        let pendingCompletion = common "completed" "2026-09-04T08:01:00Z" "1" "{\"status\":\"pending\"}"
        Assert.True(LifecycleTelemetry.sealSuccessor "run" "unit" sealedStarted pendingCompletion |> Result.isError)

    [<Fact>]
    let ``critique and feedback receipts bind current evidence`` () =
        let head = String.replicate 40 "a"
        let critique = $"""{{"schema_version":3,"cycle_id":"cycle-1","milestone":"GS2-01.1","critic":"critic-1","initial_reviewed_commit":"%s{head}","scope":["requirements","diff","tests","architecture","roadmap-evidence"],"initial_verdict":"pass","game_functionality":false,"entry_point_not_test_ownable":false,"entry_point_not_test_ownable_reason":null,"player_journeys":[],"uncovered_functionality":[],"repair_rounds":0,"reviewed_commits":["%s{head}"],"findings":[],"confirmation":{{"reviewed_commit":"%s{head}","verdict":"pass","unresolved_blocker_major":[]}},"human_escalation":null}}"""
        let reviewed = CritiqueReceipt.validate "cycle-1" (Some head) (bytes critique) |> unwrap
        Assert.Equal(0, reviewed.RepairRounds)
        Assert.True(CritiqueReceipt.validate "cycle-2" (Some head) (bytes critique) |> Result.isError)

        let report = """---
feedbackSchema: 2
cycle: cycle-1
---
## §1 Provenance and confidence
- **activation:** active
- **phases:** claim, implementation
- **material events:** 0
- **zero-event reason:** all exercised surfaces behaved as expected
## §2 Findings
None.
"""
        let digest = sha (bytes report)
        let audit = $"""{{"auditSchema":1,"report":"feedback/report.md","reportSha256":"%s{digest}","findings":[]}}"""
        let receipt = FeedbackReceipt.validate "cycle-1" [ "claim"; "implementation" ] "feedback/report.md" (bytes report) (bytes audit) None |> unwrap
        Assert.Equal(0, receipt.MaterialEvents)

    let private closure sourceDigest =
        RoadmapClosure.inspect
            { UnitId = "GS2-01.1"; Title = "Typed thing"; RoadmapSourceDigest = sourceDigest
              AcceptedReceiptDigest = "sha256:receipt"; CandidateHead = "candidate"; ImplementationMergeHead = "implementation"
              AcceptanceMergeHead = "acceptance"; ReviewHead = "candidate"; FeedbackHead = "acceptance"
              CycleId = "cycle-1"; CycleUpdateDigest = "sha256:update"; CritiqueVerdict = "pass"; RepairRounds = 1
              IssueUrl = "https://github.com/FS-GG/repo/issues/1"; PullRequestUrl = "https://github.com/FS-GG/repo/pull/2"
              ClaimsRemaining = 0
              Checks = [ { Name = "required"; Required = true; Passed = true; Owner = None }
                         { Name = "unrelated"; Required = false; Passed = false; Owner = Some "FS-GG/other#3" } ] }
        |> unwrap

    [<Fact>]
    let ``roadmap rendering is bounded deterministic and keeps unrelated failures external`` () =
        let original = "before\n<!-- fsgg:roadmap-unit/GS2-01.1 -->\nold\n<!-- /fsgg:roadmap-unit/GS2-01.1 -->\nafter\n"
        let sourceDigest = "sha256:" + sha (bytes original)
        let accepted = closure sourceDigest
        Assert.Single(accepted.ExternalObligations) |> ignore
        let rendered = RoadmapProjection.render sourceDigest (bytes original) accepted |> unwrap
        let renderedAgain = RoadmapProjection.render sourceDigest (bytes original) accepted |> unwrap
        Assert.Equal(rendered, renderedAgain)
        Assert.StartsWith("before\n", rendered)
        Assert.EndsWith("\nafter\n", rendered)
        Assert.Contains("- [x] **GS2-01.1 — Typed thing**", rendered)

    [<Fact>]
    let ``roadmap closure refuses required failure and ambiguous marker authority`` () =
        let failed =
            RoadmapClosure.inspect
                { (closure "sha256:x").Evidence with Checks = [ { Name = "required"; Required = true; Passed = false; Owner = None } ] }
        Assert.True(Result.isError failed)
        let ambiguous = "<!-- fsgg:roadmap-unit/GS2-01.1 -->\n<!-- fsgg:roadmap-unit/GS2-01.1 -->\n<!-- /fsgg:roadmap-unit/GS2-01.1 -->"
        let digest = "sha256:" + sha (bytes ambiguous)
        Assert.True(RoadmapProjection.render digest (bytes ambiguous) (closure digest) |> Result.isError)
