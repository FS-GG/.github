namespace FS.GG.Coord.Tests

open System
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json
open Xunit
open FS.GG.Coord

module TelemetryTests =
    let private bytes (value: string) = Encoding.UTF8.GetBytes value
    let private sha (value: byte array) = SHA256.HashData value |> Convert.ToHexString |> _.ToLowerInvariant()
    let private unwrap = function Ok value -> value | Error errors -> failwithf "%A" errors
    let private sealedReceipt (payload: string) =
        let digest = sha (bytes payload)
        bytes (payload[..payload.Length - 2] + $",\"digest\":\"%s{digest}\"}}")

    let private usageRow task : RuntimeUsage.UsageRow =
        let counts: RuntimeUsage.TokenCounts =
            { Input = 10L; CachedInput = 4L; CacheWriteInput = 0L; Output = 5L; Reasoning = Some 2L; Total = 15L }
        { Timestamp = "2026-09-04T08:01:00Z"; Task = task; SessionId = "session-1"; ThreadId = "thread-1"
          TurnId = "turn-1"; ResponseId = "response-1"; Provider = "OpenAI"; Model = "gpt-test"; Effort = "high"
          RuntimeVersion = "1.2.3"; CoordinationVersion = "4.5.6"; SddVersion = "7.8.9"; ContractsVersion = "10.0.0"
          LedgerSchema = 1; Response = counts; Turn = counts; Thread = Some counts
          Source = "codex-session-jsonl:sha256:" + String.replicate 64 "f" }

    [<Fact>]
    let ``#3259 usage receipts archive idempotently and corrupted canonical bytes fail closed`` () =
        let store = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "fsgg-3259-tests", Guid.NewGuid().ToString("n"))
        use _cleanup = { new IDisposable with member _.Dispose() = if Directory.Exists store then Directory.Delete(store, true) }
        let receiptBytes = RuntimeUsage.renderCsv [ usageRow "FS-GG/.github#42/claim" ] |> bytes
        let first = UsageReceiptStore.archive (Some store) receiptBytes |> unwrap
        let second = UsageReceiptStore.archive (Some store) receiptBytes |> unwrap
        Assert.Equal(first, second)
        Assert.True(receiptBytes.AsSpan().SequenceEqual((UsageReceiptStore.resolve (Some store) first.Source |> unwrap).AsSpan()))
        if not (OperatingSystem.IsWindows()) then
            Assert.Equal(UnixFileMode.UserRead ||| UnixFileMode.UserWrite, File.GetUnixFileMode first.Path)
        File.WriteAllText(first.Path, "tampered", UTF8Encoding(false))
        let corrupted = UsageReceiptStore.resolve (Some store) first.Source
        Assert.True(Result.isError corrupted)
        Assert.Contains("corrupted or collides", sprintf "%A" corrupted)
        File.WriteAllBytes(first.Path, receiptBytes)
        if not (OperatingSystem.IsWindows()) then
            File.SetUnixFileMode(first.Path, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.GroupRead)
            Assert.Contains("permissions are not owner-only", sprintf "%A" (UsageReceiptStore.resolve (Some store) first.Source))
        Assert.True(UsageReceiptStore.archive (Some(Path.GetTempPath())) receiptBytes |> Result.isError)
        Assert.True(UsageReceiptStore.archive (Some(Path.GetPathRoot store)) receiptBytes |> Result.isError)
        if not (OperatingSystem.IsWindows()) then
            let linkTarget, link = store + "-target", store + "-link"
            Directory.CreateDirectory linkTarget |> ignore
            Directory.CreateSymbolicLink(link, linkTarget) |> ignore
            Assert.Contains("symbolic link", sprintf "%A" (UsageReceiptStore.archive (Some(Path.Combine(link, "nested"))) receiptBytes))
            Directory.Delete link
            Directory.Delete(linkTarget, true)

    [<Fact>]
    let ``#3259 reviewed legacy proof excludes a missing measured receipt without reconstructing counts`` () =
        let tooling = """{"ledger_schema":1,"runtime":{"status":"recorded","name":"codex","version":"1.2.3","source":"session"},"coordination":{"status":"recorded","name":"fsgg-coord","version":"4.5.6","source":"cli"},"sdd":{"status":"recorded","name":"fsgg-sdd","version":"7.8.9","source":"cli"},"contracts":{"status":"recorded","name":"fsgg-contracts","version":"10.0.0","source":"registry"}}"""
        let model = """{"status":"recorded","provider":"OpenAI","name":"gpt-test","effort":"high","source":"runtime receipt"}"""
        let draft order phase event at actual usage evidence =
            $"""{{"schema_version":1,"run_id":"run","unit_id":"unit","item":{{"repo":"FS-GG/.github","number":42,"url":"https://github.com/FS-GG/.github/issues/42"}},"phase_order":%d{order},"phase":"%s{phase}","event":"%s{event}","at":"%s{at}","actor":"worker-1","model":%s{model},"source":{{"repository":"FS-GG/.github","revision":"%s{String.replicate 40 "a"}"}},"evidence":%s{JsonSerializer.Serialize evidence},"actual_minutes":%s{actual},"historical_durations_minutes":[],"historical_average_minutes":null,"token_usage":%s{usage},"tooling":%s{tooling},"authority":{{"kind":"github_issue_comment","subject":"FS-GG/.github#42","claim_generation":"1"}}}}"""
        let receipt = RuntimeUsage.renderCsv [ usageRow "FS-GG/.github#42/claim" ] |> bytes |> RuntimeUsage.parseCsvReceipt |> unwrap
        let source, _ = receipt
        let started = LifecycleTelemetry.sealSuccessor "run" "unit" "" (draft 1 "claim" "started" "2026-09-04T08:00:00Z" "null" "{\"status\":\"pending\"}" [ "claim" ]) |> unwrap
        let measured = $"""{{"status":"measured","input":10,"cached_input":4,"cache_write_input":0,"output":5,"reasoning":2,"total":15,"source":"%s{source}","session_ids":["session-1"],"turn_ids":["turn-1"]}}"""
        let completed = LifecycleTelemetry.sealSuccessorWithEvidence "run" "unit" [ receipt ] [] started (draft 1 "claim" "completed" "2026-09-04T08:01:00Z" "1" measured [ "usage" ]) |> unwrap
        use completedJson = JsonDocument.Parse completed
        let eventDigest = completedJson.RootElement.GetProperty("digest").GetString()
        let proofWithoutDigest = $"""{{"schema":"%s{LegacyReceiptProof.Schema}","original_event_digest":"%s{eventDigest}","missing_receipt_source":"%s{source}","authority":{{"subject":"FS-GG/.github#42","comment_id":123}},"lookup_evidence":["canonical-store:absent","source-session:absent"],"author":"recovery-1a2b","reviewer":"critic-2c3d","review_evidence":["https://github.com/FS-GG/.github/issues/42#issuecomment-124"],"decision":"irrecoverable-exclude-usage"}}"""
        let proofDigest = CanonicalJson.canonicalize (bytes proofWithoutDigest) |> unwrap |> bytes |> sha
        let proofJson = proofWithoutDigest[..proofWithoutDigest.Length - 2] + $",\"digest\":\"%s{proofDigest}\"}}"
        let proof = LegacyReceiptProof.parse (bytes proofJson) |> unwrap
        let existing = started + completed
        Assert.True(LifecycleTelemetry.validateWithEvidenceAndLegacy "run" "unit" false false [] [] [] [] existing |> Result.isError)
        let recovery =
            LifecycleTelemetry.sealSuccessorWithEvidenceAndLegacy "run" "unit" [] [ proof ] [] existing
                (draft 2 "legacy-receipt-recovery-claim" "started" "2026-09-04T08:02:00Z" "null" "{\"status\":\"pending\"}" [ "legacy-receipt-proof:sha256:" + proofDigest ])
            |> unwrap
        LifecycleTelemetry.validateWithEvidenceAndLegacy "run" "unit" false false [] [] [ proof ] [] (existing + recovery) |> unwrap |> ignore
        Assert.True(LegacyReceiptProof.parse (bytes (proofJson.Replace("\"critic-2c3d\"", "\"recovery-1a2b\""))) |> Result.isError)

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
        let duplicated = RuntimeUsage.renderJsonLines [ result.Rows.Head; result.Rows.Head ]
        Assert.True(RuntimeUsage.parseJsonLines duplicated |> Result.isError)

    [<Fact>]
    let ``Codex collection rejects malformed arithmetic and never exports source paths`` () =
        let invalid =
            [ """{"type":"session_meta","payload":{"cli_version":"1.2.3"}}"""
              """{"type":"turn_context","payload":{"turn_id":"turn-1","model":"gpt-test-sol"}}"""
              """{"timestamp":"2026-01-01T00:01:00Z","type":"token_usage_record","payload":{"thread_id":"thread-1","turn_id":"turn-1","session_id":"session-1","response_id":"response-1","usage":{"input_tokens":10,"cached_input_tokens":0,"cache_write_input_tokens":0,"output_tokens":5,"reasoning_output_tokens":0,"total_tokens":99},"turn_token_usage":{},"thread_token_usage":{}}}""" ] |> String.concat "\n"
        let result = RuntimeUsage.collectCodex "task" None false None None "1" "1" "1" (bytes invalid)
        Assert.True(Result.isError result)
        Assert.DoesNotContain("/home/", sprintf "%A" result)
        let valid = invalid.Replace("\"total_tokens\":99", "\"total_tokens\":15")
        let latestWithoutTimestamp = valid.Split('\n').[2].Replace("\"timestamp\":\"2026-01-01T00:01:00Z\",", "").Replace("response-1", "response-latest")
        let latestResult = RuntimeUsage.collectCodex "task" None false None None "1" "1" "1" (bytes (valid + "\n" + latestWithoutTimestamp))
        Assert.True(Result.isError latestResult)

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
        let invented = common "completed" "2026-09-04T08:01:00Z" "1" "{\"status\":\"measured\",\"input\":1,\"cached_input\":0,\"cache_write_input\":0,\"output\":1,\"reasoning\":0,\"total\":2,\"source\":\"runtime-usage-csv:sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"session_ids\":[\"s\"],\"turn_ids\":[\"t\"]}"
        Assert.True(LifecycleTelemetry.sealSuccessor "run" "unit" sealedStarted invented |> Result.isError)
        Assert.True(LifecycleTelemetry.sealSuccessor "run" "unit" "" sealedStarted |> Result.isError)

    [<Fact>]
    let ``#3210 reconciled validation requires an exact-digest measured recovery for timing placeholders`` () =
        let tooling = """{"ledger_schema":1,"runtime":{"status":"recorded","name":"codex","version":"1.2.3","source":"session"},"coordination":{"status":"recorded","name":"fsgg-coord","version":"4.5.6","source":"cli"},"sdd":{"status":"recorded","name":"fsgg-sdd","version":"7.8.9","source":"cli"},"contracts":{"status":"recorded","name":"fsgg-contracts","version":"10.0.0","source":"registry"}}"""
        let model = """{"status":"recorded","provider":"OpenAI","name":"gpt-test","effort":"high","source":"runtime receipt"}"""
        let draft order phase event at actual usage evidence =
            $"""{{"schema_version":1,"run_id":"run","unit_id":"unit","item":{{"repo":"FS-GG/.github","number":42,"url":"https://github.com/FS-GG/.github/issues/42"}},"phase_order":%d{order},"phase":"%s{phase}","event":"%s{event}","at":"%s{at}","actor":"worker-1","model":%s{model},"source":{{"repository":"FS-GG/.github","revision":"%s{String.replicate 40 "a"}"}},"evidence":%s{JsonSerializer.Serialize evidence},"actual_minutes":%s{actual},"historical_durations_minutes":[],"historical_average_minutes":null,"token_usage":%s{usage},"tooling":%s{tooling},"authority":{{"kind":"github_issue_comment","subject":"FS-GG/.github#42","claim_generation":"1"}}}}"""
        let started = LifecycleTelemetry.sealSuccessor "run" "unit" "" (draft 1 "claim" "started" "2026-09-04T08:00:00Z" "null" "{\"status\":\"pending\"}" [ "claim" ]) |> unwrap
        let legacy =
            LifecycleTelemetry.sealSuccessor "run" "unit" started
                (draft 1 "claim" "completed" "2026-09-04T08:01:00Z" "1" "{\"status\":\"unavailable\",\"reason\":\"final usage is written after this response\",\"source\":\"legacy child\"}" [ "legacy" ])
            |> unwrap
        let unreconciled = started + legacy
        Assert.True(LifecycleTelemetry.validateWithEvidence "run" "unit" true [] [] [] unreconciled |> Result.isOk)
        Assert.True(LifecycleTelemetry.validateReconciledWithEvidence "run" "unit" true [] [] [] unreconciled |> Result.isError)
        use legacyJson = JsonDocument.Parse legacy
        let legacyDigest = legacyJson.RootElement.GetProperty("digest").GetString()
        let counts: RuntimeUsage.TokenCounts =
            { Input = 10L; CachedInput = 4L; CacheWriteInput = 0L; Output = 5L; Reasoning = Some 2L; Total = 15L }
        let row: RuntimeUsage.UsageRow =
            { Timestamp = "2026-09-04T08:03:00Z"; Task = "FS-GG/.github#42/telemetry-reconciliation-claim"
              SessionId = "session-1"; ThreadId = "thread-1"; TurnId = "turn-1"; ResponseId = "response-1"
              Provider = "OpenAI"; Model = "gpt-test"; Effort = "high"; RuntimeVersion = "1.2.3"
              CoordinationVersion = "4.5.6"; SddVersion = "7.8.9"; ContractsVersion = "10.0.0"
              LedgerSchema = 1; Response = counts; Turn = counts; Thread = Some counts
              Source = "codex-session-jsonl:sha256:" + String.replicate 64 "f" }
        let usageReceipt = RuntimeUsage.renderCsv [ row ] |> bytes |> RuntimeUsage.parseCsvReceipt |> unwrap
        let usageDigest, _ = usageReceipt
        let recoveryStarted =
            LifecycleTelemetry.sealSuccessor "run" "unit" unreconciled
                (draft 2 "telemetry-reconciliation-claim" "started" "2026-09-04T08:02:00Z" "null" "{\"status\":\"pending\"}" [ "recovery" ])
            |> unwrap
        let withRecoveryStarted = unreconciled + recoveryStarted
        let measured = $"""{{"status":"measured","input":10,"cached_input":4,"cache_write_input":0,"output":5,"reasoning":2,"total":15,"source":"%s{usageDigest}","session_ids":["session-1"],"turn_ids":["turn-1"]}}"""
        let recovered =
            LifecycleTelemetry.sealSuccessorWithEvidence "run" "unit" [ usageReceipt ] [] withRecoveryStarted
                (draft 2 "telemetry-reconciliation-claim" "completed" "2026-09-04T08:03:00Z" "1" measured [ "supersedes-lifecycle-digest:" + legacyDigest ])
            |> unwrap
        let complete = withRecoveryStarted + recovered
        LifecycleTelemetry.validateReconciledWithEvidence "run" "unit" true [] [ usageReceipt ] [] complete |> unwrap |> ignore
        let recoveredWithExtraEvidence =
            LifecycleTelemetry.sealSuccessorWithEvidence "run" "unit" [ usageReceipt ] [] withRecoveryStarted
                (draft 2 "telemetry-reconciliation-claim" "completed" "2026-09-04T08:03:00Z" "1" measured
                    [ "supersedes-lifecycle-digest:" + legacyDigest; "unrelated:evidence" ])
            |> unwrap
        Assert.True(
            LifecycleTelemetry.validateReconciledWithEvidence
                "run" "unit" true [] [ usageReceipt ] [] (withRecoveryStarted + recoveredWithExtraEvidence)
            |> Result.isError)
        let paraphrasedLegacy =
            LifecycleTelemetry.sealSuccessor "run" "unit" started
                (draft 1 "claim" "completed" "2026-09-04T08:01:00Z" "1"
                    "{\"status\":\"unavailable\",\"reason\":\"usage unavailable because the child response is still running\",\"source\":\"legacy child\"}"
                    [ "legacy" ])
            |> unwrap
        Assert.True(
            LifecycleTelemetry.validateReconciledWithEvidence "run" "unit" true [] [] [] (started + paraphrasedLegacy)
            |> Result.isError)
        let misleadingMissing =
            LifecycleTelemetry.sealSuccessor "run" "unit" started
                (draft 1 "claim" "completed" "2026-09-04T08:01:00Z" "1"
                    "{\"status\":\"unavailable\",\"reason\":\"usage missing because the child response is still running\",\"source\":\"legacy child\"}"
                    [ "legacy" ])
            |> unwrap
        Assert.True(
            LifecycleTelemetry.validateReconciledWithEvidence "run" "unit" true [] [] [] (started + misleadingMissing)
            |> Result.isError)
        let genuineFailure =
            LifecycleTelemetry.sealSuccessor "run" "unit" started
                (draft 1 "claim" "completed" "2026-09-04T08:01:00Z" "1"
                    "{\"status\":\"unavailable\",\"reason\":\"post-completion collector schema validation failed: total field missing\",\"source\":\"collector\"}"
                    [ "failure" ])
            |> unwrap
        LifecycleTelemetry.validateReconciledWithEvidence "run" "unit" true [] [] [] (started + genuineFailure) |> unwrap |> ignore

    [<Fact>]
    let ``critique and feedback receipts bind current evidence`` () =
        let head = String.replicate 40 "a"
        let critique = $"""{{"schema_version":3,"cycle_id":"cycle-1","milestone":"GS2-01.1","critic":"critic-1","initial_reviewed_commit":"%s{head}","scope":["requirements","diff","tests","architecture","roadmap-evidence"],"initial_verdict":"pass","game_functionality":false,"entry_point_not_test_ownable":false,"entry_point_not_test_ownable_reason":null,"player_journeys":[],"uncovered_functionality":[],"repair_rounds":0,"reviewed_commits":["%s{head}"],"findings":[],"confirmation":{{"reviewed_commit":"%s{head}","verdict":"pass","unresolved_blocker_major":[]}},"human_escalation":null}}"""
        let reviewed = CritiqueReceipt.validate "cycle-1" (Some head) (bytes critique) |> unwrap
        Assert.Equal(0, reviewed.RepairRounds)
        Assert.True(CritiqueReceipt.validate "cycle-2" (Some head) (bytes critique) |> Result.isError)
        let emptyFindingEvidence = critique.Replace("\"findings\":[]", "\"findings\":[{\"id\":\"minor-1\",\"severity\":\"minor\",\"summary\":\"gap\",\"evidence\":[],\"disposition\":\"follow-up\",\"resolution_evidence\":[]}]")
        Assert.True(CritiqueReceipt.validate "cycle-1" (Some head) (bytes emptyFindingEvidence) |> Result.isError)

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
        Assert.True(FeedbackReceipt.validate "cycle-1" [ "claim"; "implementation" ] "feedback/report.md" (bytes report) (bytes audit) (Some "not-json-and-wrong-cycle") |> Result.isError)

    let private closureInputs sourceDigest =
        let candidate, implementation, acceptance = String.replicate 40 "a", String.replicate 40 "b", String.replicate 40 "c"
        let critique = $"""{{"schema_version":3,"cycle_id":"cycle-1","milestone":"GS2-01.1","critic":"critic-1","initial_reviewed_commit":"%s{candidate}","scope":["requirements","diff","tests","architecture","roadmap-evidence"],"initial_verdict":"pass","game_functionality":false,"entry_point_not_test_ownable":false,"entry_point_not_test_ownable_reason":null,"player_journeys":[],"uncovered_functionality":[],"repair_rounds":0,"reviewed_commits":["%s{candidate}"],"findings":[],"confirmation":{{"reviewed_commit":"%s{candidate}","verdict":"pass","unresolved_blocker_major":[]}},"human_escalation":null}}""" |> bytes
        let report = "---\nfeedbackSchema: 2\ncycle: cycle-1\n---\n## §1 Provenance and confidence\n- **activation:** active\n- **phases:** claim, implementation\n- **material events:** 0\n- **zero-event reason:** all exercised surfaces behaved as expected\n## §2 Findings\nNone.\n" |> bytes
        let reportHash, auditArtifactHash, contractHash = sha report, String.replicate 64 "d", String.replicate 64 "e"
        let audit = $"""{{"auditSchema":1,"report":"feedback/report.md","reportSha256":"%s{reportHash}","findings":[]}}""" |> bytes
        let auditHash = sha audit
        let accepted = sealedReceipt $"""{{"acceptedAt":"2026-01-01T00:00:00Z","artifacts":[{{"name":"implementation-candidate-%s{candidate}","sha256":"%s{auditArtifactHash}"}}],"schema":"fsgg.coordination.unit-acceptance/1","sourceRevision":"%s{candidate}","state":"accepted","unitContractSha256":"%s{contractHash}","unitId":"GS2-01.1"}}"""
        let delivery = sealedReceipt $"""{{"acceptanceMergeHead":"%s{acceptance}","candidateHead":"%s{candidate}","claimsRemaining":0,"implementationMergeHead":"%s{implementation}","issueUrl":"https://github.com/FS-GG/repo/issues/1","pullRequestUrl":"https://github.com/FS-GG/repo/pull/2","schema":"fsgg.roadmap.delivery/1","unitId":"GS2-01.1"}}"""
        let feedbackBinding = sealedReceipt $"""{{"auditSha256":"%s{auditHash}","cycleId":"cycle-1","head":"%s{acceptance}","reportSha256":"%s{reportHash}","schema":"fsgg.roadmap.feedback-binding/1","unitId":"GS2-01.1"}}"""
        let cycle = sealedReceipt $"""{{"cycleId":"cycle-1","head":"%s{acceptance}","schema":"fsgg.roadmap.cycle-update/1","unitId":"GS2-01.1"}}"""
        let check = sealedReceipt $"""{{"head":"%s{acceptance}","name":"required","owner":null,"passed":true,"required":true,"schema":"fsgg.roadmap.check/1","unitId":"GS2-01.1"}}"""
        { UnitId = "GS2-01.1"; Title = "Typed thing"; RoadmapSourceDigest = sourceDigest
          AcceptedReceipt = accepted; DeliveryReceipt = delivery; Critique = critique
          FeedbackReportPath = "feedback/report.md"; FeedbackReport = report; FeedbackAudit = audit
          FeedbackPhases = [ "claim"; "implementation" ]; FeedbackCheckpoint = None; FeedbackBinding = feedbackBinding
          CycleUpdate = cycle; CheckReceipts = [ check ] } : RoadmapClosure.Inputs

    let private closure sourceDigest = closureInputs sourceDigest |> RoadmapClosure.inspect |> unwrap

    [<Fact>]
    let ``roadmap rendering is bounded deterministic and keeps unrelated failures external`` () =
        let original = "before\n<!-- fsgg:roadmap-unit/GS2-01.1 -->\nold\n<!-- /fsgg:roadmap-unit/GS2-01.1 -->\nafter\n"
        let sourceDigest = "sha256:" + sha (bytes original)
        let accepted = closure sourceDigest
        Assert.Empty(accepted.ExternalObligations)
        let rendered = RoadmapProjection.render sourceDigest (bytes original) accepted |> unwrap
        let renderedAgain = RoadmapProjection.render sourceDigest (bytes original) accepted |> unwrap
        Assert.Equal(rendered, renderedAgain)
        Assert.StartsWith("before\n", rendered)
        Assert.EndsWith("\nafter\n", rendered)
        Assert.Contains("- [x] **GS2-01.1 — Typed thing**", rendered)
        let tamperedOutside = rendered.Replace("before", "tampered")
        Assert.True(RoadmapProjection.verify sourceDigest (bytes original) (bytes tamperedOutside) accepted |> Result.isError)
        Assert.True(RoadmapProjection.verify sourceDigest (bytes original) (bytes rendered) accepted |> Result.isOk)
        Assert.True(RoadmapProjection.render ("sha256:" + sha (bytes rendered)) (bytes rendered) accepted |> Result.isError)

    [<Fact>]
    let ``roadmap closure refuses required failure and ambiguous marker authority`` () =
        let inputs = closureInputs ("sha256:" + String.replicate 64 "a")
        let tampered = { inputs with AcceptedReceipt = inputs.AcceptedReceipt |> Array.copy }
        tampered.AcceptedReceipt[10] <- byte 'X'
        Assert.True(RoadmapClosure.inspect tampered |> Result.isError)
        let acceptance = String.replicate 40 "c"
        let ownerless = sealedReceipt $"""{{"head":"%s{acceptance}","name":"unrelated","owner":null,"passed":false,"required":false,"schema":"fsgg.roadmap.check/1","unitId":"GS2-01.1"}}"""
        Assert.True(RoadmapClosure.inspect { inputs with CheckReceipts = [ ownerless ] } |> Result.isError)
        let ambiguous = "<!-- fsgg:roadmap-unit/GS2-01.1 -->\n<!-- fsgg:roadmap-unit/GS2-01.1 -->\n<!-- /fsgg:roadmap-unit/GS2-01.1 -->"
        let digest = "sha256:" + sha (bytes ambiguous)
        Assert.True(RoadmapProjection.render digest (bytes ambiguous) (closure digest) |> Result.isError)
