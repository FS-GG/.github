namespace FS.GG.Coord.Cli.Tests

open System
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json.Nodes
open Microsoft.Data.Sqlite
open Xunit
open FS.GG.Coord
open FS.GG.Coord.Cli

/// Claude Code subagent dispatch attribution (#5171). Every transcript, metadata and launch record below is a
/// synthetic fixture in the observed native shape; no private transcript content is used. Fixture clock: the fixed
/// literal timestamps in the records, except the retirement case, which ages its files relative to the host wall clock.
module ClaudeSubagentDispatchTests =
    let private supported () =
        OperatingSystem.IsLinux() && Runtime.InteropServices.RuntimeInformation.ProcessArchitecture = Runtime.InteropServices.Architecture.X64

    let private session = "7d3c1a2b-4e5f-4a6b-9c8d-0e1f2a3b4c5d"
    let private agent = "a1111111111111111"
    let private nestedAgent = "a2222222222222222"
    let private toolUse = "toolu_01SyntheticLaunchAaaaaaaa"
    let private nestedToolUse = "toolu_01SyntheticLaunchBbbbbbbb"
    let private promptA = "0b1c2d3e-4f5a-4b6c-8d7e-9f0a1b2c3d4e"
    let private promptB = "1c2d3e4f-5a6b-4c7d-9e8f-0a1b2c3d4e5f"
    let private promptNested = "2d3e4f5a-6b7c-4d8e-8f9a-1b2c3d4e5f6a"
    let private marker = "fsgg-dispatch: item=FS-GG/.github#5171 role=worker"

    // Independent recomputation of the documented identity derivations; the test does not call the producer's helpers.
    let private hex32 (label: string) (values: string list) =
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(String.Join("\u001f", label :: values)))).Substring(0, 32).ToLowerInvariant()

    let private dispatchOf tool = hex32 "claude-code-subagent-dispatch/1" [ session; tool ]
    let private sessionDispatch = hex32 "claude-code-session-dispatch/1" [ session ]
    let private threadOf (agentId: string) = RuntimeUsage.ClaudeTranscript.subagentThreadId session agentId

    let private temp () =
        let root = Path.Combine(Path.GetTempPath(), "fsgg-5171-" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory root |> ignore
        File.SetUnixFileMode(root, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute)
        { new IDisposable with
            member _.Dispose() = if Directory.Exists root then Directory.Delete(root, true) }, root

    type private Layout =
        { Root: string
          Home: string
          Parent: string
          Subagents: string }
        member this.Transcript(id: string) = Path.Combine(this.Subagents, $"agent-{id}.jsonl")
        member this.Meta(id: string) = Path.Combine(this.Subagents, $"agent-{id}.meta.json")

    let private layout (root: string) =
        let home = Path.Combine(root, "claude")
        let project = Path.Combine(home, "projects", "-work")
        let subagents = Path.Combine(project, session, "subagents")
        Directory.CreateDirectory subagents |> ignore
        { Root = root; Home = home; Parent = Path.Combine(project, session + ".jsonl"); Subagents = subagents }

    let private opening (agentId: string) (prompt: string) (text: string) (at: string) =
        let content = Json.JsonSerializer.Serialize text
        $"""{{"parentUuid":null,"isSidechain":true,"promptId":"{prompt}","agentId":"{agentId}","type":"user","message":{{"role":"user","content":{content}}},"uuid":"{Guid.NewGuid()}","timestamp":"{at}","cwd":"/work","sessionId":"{session}","version":"2.1.309"}}""" + "\n"

    let private response (agentId: string) (messageId: string) (input: int64) (cacheRead: int64) (cacheWrite: int64) (output: int64) (at: string) =
        $"""{{"parentUuid":null,"isSidechain":true,"agentId":"{agentId}","message":{{"model":"claude-sonnet-5-5","id":"{messageId}","type":"message","role":"assistant","content":[{{"type":"text","text":"synthetic"}}],"stop_reason":"end_turn","usage":{{"input_tokens":{input},"cache_creation_input_tokens":{cacheWrite},"cache_read_input_tokens":{cacheRead},"output_tokens":{output}}}}},"requestId":"req_{messageId}","type":"assistant","uuid":"{Guid.NewGuid()}","timestamp":"{at}","cwd":"/work","sessionId":"{session}","version":"2.1.309"}}""" + "\n"

    let private meta (tool: string option) (depth: int) (parent: string option) =
        let fields =
            [ yield "\"agentType\":\"fsgg-worker-normal\""
              yield "\"isBuiltIn\":false"
              yield "\"description\":\"synthetic\""
              match tool with
              | Some value -> yield $"\"toolUseId\":\"{value}\""
              | None -> ()
              match parent with
              | Some value -> yield $"\"parentAgentId\":\"{value}\""
              | None -> ()
              yield $"\"spawnDepth\":{depth}"
              yield "\"requestShape\":\"background\"" ]
        "{" + String.Join(",", fields) + "}"

    let private launch (agentId: string) (tool: string) (model: string) =
        $"""{{"parentUuid":null,"isSidechain":false,"promptId":"{promptA}","type":"user","message":{{"role":"user","content":[{{"tool_use_id":"{tool}","type":"tool_result","content":[{{"type":"text","text":"synthetic"}}]}}]}},"uuid":"{Guid.NewGuid()}","timestamp":"2026-11-01T09:00:00.500Z","toolUseResult":{{"isAsync":true,"status":"async_launched","agentId":"{agentId}","description":"synthetic","resolvedModel":"{model}","prompt":"synthetic"}},"sessionId":"{session}","version":"2.1.309"}}""" + "\n"

    let private mainPrompt =
        $"""{{"parentUuid":null,"isSidechain":false,"promptId":"{promptA}","type":"user","message":{{"role":"user","content":"synthetic"}},"uuid":"{Guid.NewGuid()}","timestamp":"2026-11-01T08:59:00.000Z","sessionId":"{session}","version":"2.1.309"}}""" + "\n"

    /// A standard attributed dispatch: marker prompt, two completed responses, metadata and launch result.
    let private writeAttributed (fixture: Layout) =
        File.WriteAllText(fixture.Parent, mainPrompt + launch agent toolUse "claude-sonnet-5-5")
        File.WriteAllText(fixture.Meta agent, meta (Some toolUse) 1 None)
        File.WriteAllText(
            fixture.Transcript agent,
            opening agent promptA (marker + "\nsynthetic brief body") "2026-11-01T09:00:01.250Z"
            + response agent "msg_01a" 3L 4000L 300L 50L "2026-11-01T09:00:05.000Z"
            + response agent "msg_01b" 2L 4300L 120L 70L "2026-11-01T09:00:09.000Z")

    let private capture (fixture: Layout) (batches: ResizeArray<byte array>) (paths: string list) =
        let result =
            SkillTelemetryAdapter.capturePassiveFrom fixture.Root "owner" "passive-stream" (SkillTelemetryReaders.ClaudeHome fixture.Home) paths
                (fun bytes -> batches.Add(Array.copy bytes); Ok "locally-retained")
        Assert.True((result.ExitCode = 0), Encoding.UTF8.GetString result.Stderr)
        JsonNode.Parse result.Stdout

    let private facts (bytes: byte array) =
        (TelemetryStore.parseBatch bytes |> Result.defaultWith (sprintf "%A" >> failwith)).Facts

    let private ofKind kind (batch: TelemetryStore.Fact list) = batch |> List.filter (fun fact -> fact.Kind = kind)

    let private field (fact: TelemetryStore.Fact) (name: string) =
        let value = (JsonNode.Parse fact.Canonical)[name]
        if isNull value then None else Some(value.ToJsonString().Trim('"'))

    let private dispatchState (root: string) =
        let state = JsonNode.Parse(File.ReadAllBytes(Path.Combine(root, "orchestrator-passive", "producer.json")))
        let pick (entries: JsonNode) (name: string) =
            if isNull entries then []
            else entries.AsObject() |> Seq.choose (fun pair -> if isNull pair.Value[name] then None else Some(pair.Value[name].GetValue<string>())) |> Seq.toList
        pick (state["sources"]) "dispatch", pick (state["retired"]) "d"

    // The read-only join the PR body names for the installed proof. It is executed verbatim against the store here.
    let JoinSql =
        """SELECT d.item_id, d.dispatch_id, l.invocation_id, d.parent_dispatch_id, d.expected_at,
       a.producer_stream, a.requested_model,
       count(u.identity) AS responses,
       group_concat(DISTINCT json_extract(u.canonical,'$.model')) AS observed_models,
       sum(json_extract(u.canonical,'$.input')) AS input,
       sum(json_extract(u.canonical,'$.cachedInput')) AS cached_input,
       sum(json_extract(u.canonical,'$.cacheWriteInput')) AS cache_write_input,
       sum(json_extract(u.canonical,'$.output')) AS output,
       sum(json_extract(u.canonical,'$.total')) AS total
FROM expected_dispatches d
JOIN invocation_lineage l ON l.item_id=d.item_id AND l.dispatch_id=d.dispatch_id
LEFT JOIN runtime_admissions a ON a.item_id=l.item_id AND a.invocation_id=l.invocation_id
LEFT JOIN current_ingest_facts u ON u.kind='provider-usage-observation/1' AND json_extract(u.canonical,'$.threadId')=l.invocation_id
WHERE d.runtime='claude-code-subagent'
GROUP BY d.item_id, d.dispatch_id, l.invocation_id
ORDER BY d.expected_at, d.dispatch_id;"""

    module private Store =
        let approved = TelemetryStore.ApprovedLocalDurable
        let unwrap = function Ok value -> value | Error errors -> failwithf "%A" errors
        let scope: TelemetryReceipt.Scope = { Workspace = "claude-dispatch"; Producer = "owner"; Stream = "passive-stream" }

        let create (root: string) =
            let path = Path.Combine(root, "store")
            Directory.CreateDirectory path |> ignore
            TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
            TelemetryStoreApplication.provisionReceiptWorkspace path approved scope.Workspace |> unwrap |> ignore
            TelemetryStoreApplication.enrollReceiptPrincipal path approved (TelemetryReceipt.genericPrincipal scope) |> unwrap |> ignore
            path

        /// Submits one exact producer batch through an authenticated generic receipt and drains it.
        let submit (path: string) (name: string) (batch: byte array) =
            let payload = Encoding.UTF8.GetString batch
            let envelope =
                Encoding.UTF8.GetBytes(
                    $"""{{"schema":"{TelemetryReceipt.Schema}","workspaceId":"{scope.Workspace}","producerId":"{scope.Producer}","streamId":"{scope.Stream}","batchId":"{name}","payload":{payload}}}""")
            TelemetryStoreApplication.submitReceiptPrincipal path approved (TelemetryReceipt.genericPrincipal scope) envelope |> unwrap |> ignore
            TelemetryStoreApplication.drainReceipts path approved scope.Workspace |> unwrap

        let rows (path: string) (sql: string) =
            use connection = new SqliteConnection($"Data Source={Path.Combine(path, TelemetryStoreApplication.databaseFileName)};Mode=ReadOnly;Pooling=False")
            connection.Open()
            use command = connection.CreateCommand()
            command.CommandText <- sql
            use reader = command.ExecuteReader()
            [ while reader.Read() do
                  yield [ for index in 0 .. reader.FieldCount - 1 -> if reader.IsDBNull index then null else string (reader.GetValue index) ] ]

        let scalar path sql = (rows path sql |> List.exactlyOne |> List.exactlyOne)

    /// Re-serializes chosen facts from a producer batch as a new batch, for replay controls.
    let private rebatch (name: string) (source: byte array) (keep: string -> bool) =
        let node = JsonNode.Parse source
        let events = node["events"].AsArray() |> Seq.filter (fun event -> keep (event["kind"].GetValue<string>())) |> Seq.map (fun event -> event.DeepClone()) |> Seq.toArray
        let batch = JsonObject()
        batch["schema"] <- JsonValue.Create TelemetryStore.BatchSchema
        batch["ingestId"] <- JsonValue.Create name
        batch["sourceIdentity"] <- JsonValue.Create "replay-control"
        batch["generation"] <- JsonValue.Create "g1"
        batch["cursor"] <- JsonValue.Create "1"
        batch["eventCount"] <- JsonValue.Create events.Length
        batch["events"] <- JsonArray(events)
        Encoding.UTF8.GetBytes(batch.ToJsonString())

    [<Fact>]
    let ``attributed subagent dispatch joins its Delivery and per-response usage in the store`` () =
        if supported () then
            let cleanup, root = temp ()
            use cleanup = cleanup
            let fixture = layout root
            writeAttributed fixture
            let batches = ResizeArray<byte array>()
            capture fixture batches [ fixture.Transcript agent ] |> ignore
            let batch = facts (Assert.Single batches)
            let dispatch = Assert.Single(ofKind "expected-dispatch" batch)
            let lineage = Assert.Single(ofKind "invocation-lineage" batch)
            let admission = Assert.Single(ofKind "runtime-admission" batch)
            Assert.Equal(2, (ofKind "provider-usage-observation/1" batch).Length)
            Assert.Empty(ofKind "diagnostic" batch)
            Assert.Empty(ofKind "runtime-gap" batch)
            let invocation = threadOf agent
            Assert.Equal("expected-dispatch-" + dispatchOf toolUse, dispatch.Identity)
            Assert.Equal(Some "FS-GG/.github#5171", dispatch.ItemId)
            Assert.Equal(Some "child", field dispatch "relation")
            Assert.Equal(Some sessionDispatch, field dispatch "parentDispatchId")
            Assert.Equal(Some "claude-code-subagent", field dispatch "runtime")
            Assert.Equal(Some "2026-11-01T09:00:01.2500000Z", field dispatch "expectedAt")
            Assert.Equal(Some(dispatchOf toolUse), field lineage "dispatchId")
            Assert.Equal(Some invocation, field lineage "invocationId")
            Assert.Equal(Some session, field lineage "parentInvocationId")
            Assert.Equal(Some session, field lineage "rootInvocationId")
            Assert.Equal(Some "claude-sonnet-5-5", field admission "requestedModel")
            Assert.Equal(Some "claude-code-subagent:worker", field admission "producerStream")
            Assert.Equal(Some(dispatchOf toolUse), field admission "attemptId")
            // Per-response usage already carries the invocation as its thread identity.
            for observation in ofKind "provider-usage-observation/1" batch do
                Assert.Equal(Some invocation, field observation "threadId")
            Assert.Equal<string list>([ "complete" ], fst (dispatchState root))
            let store = Store.create root
            Assert.Contains("\"rejected\":0", Store.submit store "first" batches[0])
            let row = Assert.Single(Store.rows store JoinSql)
            Assert.Equal<string list>(
                [ "FS-GG/.github#5171"; dispatchOf toolUse; invocation; sessionDispatch; "2026-11-01T09:00:01.2500000Z"
                  "claude-code-subagent:worker"; "claude-sonnet-5-5"; "2"; "claude-sonnet-5-5"
                  // Input is uncached + cache read + cache write per response, summed over both responses.
                  string (3L + 4000L + 300L + 2L + 4300L + 120L); string (4000L + 4300L); string (300L + 120L)
                  string (50L + 70L); string (3L + 4000L + 300L + 50L + 2L + 4300L + 120L + 70L) ],
                row)

    [<Fact>]
    let ``a re-read replays identical dispatch facts and a resumed agent adds turns to the same dispatch`` () =
        if supported () then
            let cleanup, root = temp ()
            use cleanup = cleanup
            let fixture = layout root
            writeAttributed fixture
            let batches = ResizeArray<byte array>()
            capture fixture batches [ fixture.Transcript agent ] |> ignore
            let store = Store.create root
            Assert.Contains("\"rejected\":0", Store.submit store "first" batches[0])
            // Control: an unchanged transcript produces no batch at all.
            capture fixture batches [ fixture.Transcript agent ] |> ignore
            Assert.Equal(1, batches.Count)
            // A fresh producer state re-reading the same files derives byte-identical dispatch facts.
            let rereadRoot = Path.Combine(root, "reread")
            Directory.CreateDirectory rereadRoot |> ignore
            File.SetUnixFileMode(rereadRoot, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute)
            let reread = ResizeArray<byte array>()
            let result =
                SkillTelemetryAdapter.capturePassiveFrom rereadRoot "owner" "passive-stream" (SkillTelemetryReaders.ClaudeHome fixture.Home)
                    [ fixture.Transcript agent ] (fun bytes -> reread.Add(Array.copy bytes); Ok "locally-retained")
            Assert.Equal(0, result.ExitCode)
            let dispatchKinds = set [ "expected-dispatch"; "invocation-lineage"; "runtime-admission" ]
            let digests (bytes: byte array) =
                facts bytes |> List.filter (fun fact -> dispatchKinds.Contains fact.Kind) |> List.map (fun fact -> fact.Identity, fact.ContentDigest) |> List.sort
            Assert.Equal<(string * string) list>(digests batches[0], digests (Assert.Single reread))
            Assert.Equal(3, (digests batches[0]).Length)
            // The receiver replays them: still one dispatch, nothing rejected.
            let replay = Store.submit store "replay" (rebatch "replay" reread[0] dispatchKinds.Contains)
            Assert.Contains("\"rejected\":0", replay)
            Assert.Equal("1", Store.scalar store "SELECT count(*) FROM expected_dispatches;")
            Assert.Equal("1", Store.scalar store "SELECT count(*) FROM ingest_batches WHERE accepted_count=0 AND replay_count=3;")
            // SendMessage resumes the same agent transcript with a new prompt: more turns, the same dispatch.
            File.AppendAllText(
                fixture.Transcript agent,
                opening agent promptB "synthetic follow-up" "2026-11-01T09:10:00.000Z"
                + response agent "msg_02a" 4L 5000L 60L 30L "2026-11-01T09:10:04.000Z")
            capture fixture batches [ fixture.Transcript agent ] |> ignore
            Assert.Equal(2, batches.Count)
            let resumed = facts batches[1]
            Assert.Empty(resumed |> List.filter (fun fact -> dispatchKinds.Contains fact.Kind))
            let observation = Assert.Single(ofKind "provider-usage-observation/1" resumed)
            Assert.Equal(Some(threadOf agent), field observation "threadId")
            Assert.Equal(Some promptB, field observation "turnId")
            Assert.Contains("\"rejected\":0", Store.submit store "resumed" batches[1])
            let row = Assert.Single(Store.rows store JoinSql)
            Assert.Equal(dispatchOf toolUse, row[1])
            Assert.Equal("3", row[7])
            Assert.Equal(string (3L + 4000L + 300L + 50L + 2L + 4300L + 120L + 70L + 4L + 5000L + 60L + 30L), row[13])

    [<Fact>]
    let ``missing metadata or launch result and unmarked briefs are explicit gaps and never guessed`` () =
        if supported () then
            let cleanup, root = temp ()
            use cleanup = cleanup
            let fixture = layout root
            writeAttributed fixture
            // Missing metadata: no dispatch can be derived, the usage still flows, and the gap names the invocation.
            File.Delete(fixture.Meta agent)
            let batches = ResizeArray<byte array>()
            let snapshot = capture fixture batches [ fixture.Transcript agent ]
            let noMeta = facts (Assert.Single batches)
            Assert.Empty(ofKind "expected-dispatch" noMeta)
            Assert.Equal(2, (ofKind "provider-usage-observation/1" noMeta).Length)
            let gap = Assert.Single(ofKind "diagnostic" noMeta)
            Assert.Equal("claude-subagent-dispatch-unavailable-" + threadOf agent, gap.Identity)
            Assert.Equal(Some "dispatch-identity-unavailable", field gap "code")
            Assert.Equal(None, gap.ItemId)
            Assert.Contains("claude-dispatch-identity-unavailable", snapshot["gaps"].ToJsonString())
            let store = Store.create root
            Assert.Contains("\"rejected\":0", Store.submit store "no-meta" batches[0])
            Assert.Equal("0", Store.scalar store "SELECT count(*) FROM expected_dispatches;")
            Assert.Equal("1", Store.scalar store "SELECT count(*) FROM health_diagnostics WHERE code='dispatch-identity-unavailable' AND item_id IS NULL;")

    let private unattributedCase (text: string) (reason: string) =
        let cleanup, root = temp ()
        use cleanup = cleanup
        let fixture = layout root
        writeAttributed fixture
        File.WriteAllText(
            fixture.Transcript agent,
            opening agent promptA text "2026-11-01T09:00:01.250Z" + response agent "msg_01a" 3L 4000L 300L 50L "2026-11-01T09:00:05.000Z")
        let batches = ResizeArray<byte array>()
        capture fixture batches [ fixture.Transcript agent ] |> ignore
        let batch = facts (Assert.Single batches)
        Assert.Empty(ofKind "expected-dispatch" batch)
        Assert.Empty(ofKind "invocation-lineage" batch)
        Assert.Empty(ofKind "runtime-admission" batch)
        let gap = Assert.Single(ofKind "diagnostic" batch)
        Assert.Equal($"claude-subagent-item-unattributed-{dispatchOf toolUse}-{threadOf agent}", gap.Identity)
        Assert.Equal(Some("item-unattributed:" + reason), field gap "code")
        Assert.Equal(None, gap.ItemId)
        let store = Store.create root
        Assert.Contains("\"rejected\":0", Store.submit store "unattributed" batches[0])
        Assert.Empty(Store.rows store JoinSql)
        Assert.Equal("1", Store.scalar store $"SELECT count(*) FROM health_diagnostics WHERE code='item-unattributed:{reason}' AND item_id IS NULL;")

    [<Fact>]
    let ``a missing marker is an item-unattributed gap even when the item appears later in prose`` () =
        if supported () then
            unattributedCase ("Please work on FS-GG/.github#5171.\n" + marker) "missing-marker"

    [<Fact>]
    let ``a malformed marker is an item-unattributed gap`` () =
        if supported () then
            unattributedCase "fsgg-dispatch: item=FS-GG/.github#5171 role=boss" "malformed-marker"
            unattributedCase "fsgg-dispatch: item=FS-GG/.github role=worker" "malformed-marker"

    [<Fact>]
    let ``a missing launch result waits, then joins later or becomes a gap when the transcript retires`` () =
        if supported () then
            let cleanup, root = temp ()
            use cleanup = cleanup
            let fixture = layout root
            writeAttributed fixture
            File.WriteAllText(fixture.Parent, mainPrompt)
            let batches = ResizeArray<byte array>()
            capture fixture batches [ fixture.Transcript agent ] |> ignore
            let first = facts (Assert.Single batches)
            Assert.Single(ofKind "expected-dispatch" first) |> ignore
            Assert.Single(ofKind "invocation-lineage" first) |> ignore
            Assert.Empty(ofKind "runtime-admission" first)
            Assert.Empty(ofKind "runtime-gap" first)
            Assert.Equal<string list>([ "awaiting-launch" ], fst (dispatchState root))
            // The launch result appears in the spawning transcript: only the admission is added, once.
            File.AppendAllText(fixture.Parent, launch agent toolUse "claude-sonnet-5-5")
            capture fixture batches [ fixture.Transcript agent ] |> ignore
            let second = facts batches[1]
            let admission = Assert.Single second
            Assert.Equal("runtime-admission", admission.Kind)
            Assert.Equal(Some "claude-sonnet-5-5", field admission "requestedModel")
            Assert.Equal<string list>([ "complete" ], fst (dispatchState root))
            capture fixture batches [ fixture.Transcript agent ] |> ignore
            Assert.Equal(2, batches.Count)
            let store = Store.create root
            for index, batch in Seq.indexed batches do
                Assert.Contains("\"rejected\":0", Store.submit store $"b{index}" batch)
            Assert.Equal("claude-sonnet-5-5", (Assert.Single(Store.rows store JoinSql))[6])

    [<Fact>]
    let ``a transcript that retires without its launch result records the launch gap and keeps the dispatch`` () =
        if supported () then
            let cleanup, root = temp ()
            use cleanup = cleanup
            let fixture = layout root
            writeAttributed fixture
            File.WriteAllText(fixture.Parent, mainPrompt)
            File.SetLastWriteTimeUtc(fixture.Transcript agent, DateTime.UtcNow.AddHours -2.0)
            let batches = ResizeArray<byte array>()
            let snapshot = capture fixture batches [ fixture.Transcript agent ]
            let batch = facts (Assert.Single batches)
            Assert.Single(ofKind "expected-dispatch" batch) |> ignore
            Assert.Empty(ofKind "runtime-admission" batch)
            let gap = Assert.Single(ofKind "runtime-gap" batch)
            Assert.Equal(Some "claude-launch-result-unavailable", field gap "code")
            Assert.Equal(1, snapshot["closedSources"].GetValue<int>())
            Assert.Equal<string list>([ "complete" ], snd (dispatchState root))
            // Control: a later append to the retired transcript resumes it without re-emitting dispatch facts.
            File.AppendAllText(
                fixture.Transcript agent,
                opening agent promptB "synthetic follow-up" "2026-11-01T09:10:00.000Z"
                + response agent "msg_02a" 4L 5000L 60L 30L "2026-11-01T09:10:04.000Z")
            capture fixture batches [ fixture.Transcript agent ] |> ignore
            let resumed = facts batches[1]
            Assert.Equal<string list>([ "provider-usage-observation/1" ], resumed |> List.map _.Kind |> List.distinct)
            let store = Store.create root
            for index, batch in Seq.indexed batches do
                Assert.Contains("\"rejected\":0", Store.submit store $"b{index}" batch)
            Assert.Equal("1", Store.scalar store "SELECT count(*) FROM runtime_gaps WHERE code='claude-launch-result-unavailable';")
            let row = Assert.Single(Store.rows store JoinSql)
            Assert.Null(row[6])
            Assert.Equal("3", row[7])

    [<Fact>]
    let ``a nested subagent is a child of its spawning subagent's dispatch and invocation`` () =
        if supported () then
            let cleanup, root = temp ()
            use cleanup = cleanup
            let fixture = layout root
            writeAttributed fixture
            File.WriteAllText(fixture.Meta nestedAgent, meta (Some nestedToolUse) 2 (Some agent))
            // The nested launch result lives in the spawning subagent's own transcript.
            let nestedLaunch =
                (launch nestedAgent nestedToolUse "claude-haiku-4-5").Replace("\"isSidechain\":false", $"\"isSidechain\":true,\"agentId\":\"{agent}\"")
            File.AppendAllText(fixture.Transcript agent, nestedLaunch)
            File.WriteAllText(
                fixture.Transcript nestedAgent,
                opening nestedAgent promptNested "fsgg-dispatch: item=FS-GG/FS.GG.SDD#1148 role=analyst\nsynthetic" "2026-11-01T09:02:00.000Z"
                + response nestedAgent "msg_03a" 1L 900L 40L 12L "2026-11-01T09:02:03.000Z")
            let batches = ResizeArray<byte array>()
            capture fixture batches [ fixture.Transcript nestedAgent ] |> ignore
            let batch = facts (Assert.Single batches)
            let dispatch = Assert.Single(ofKind "expected-dispatch" batch)
            let lineage = Assert.Single(ofKind "invocation-lineage" batch)
            let admission = Assert.Single(ofKind "runtime-admission" batch)
            Assert.Equal("expected-dispatch-" + dispatchOf nestedToolUse, dispatch.Identity)
            Assert.Equal(Some "FS-GG/FS.GG.SDD#1148", dispatch.ItemId)
            Assert.Equal(Some(dispatchOf toolUse), field dispatch "parentDispatchId")
            Assert.Equal(Some(threadOf agent), field lineage "parentInvocationId")
            Assert.Equal(Some session, field lineage "rootInvocationId")
            Assert.Equal(Some(dispatchOf toolUse), field admission "parentAttemptId")
            Assert.Equal(Some "claude-haiku-4-5", field admission "requestedModel")
            Assert.Equal(Some "claude-code-subagent:analyst", field admission "producerStream")
            // Control: without the spawning subagent's metadata the parent cannot be named, so nothing is guessed.
            let cleanup2, root2 = temp ()
            use cleanup2 = cleanup2
            let orphan = layout root2
            File.WriteAllText(orphan.Meta nestedAgent, meta (Some nestedToolUse) 2 (Some agent))
            File.WriteAllText(orphan.Transcript nestedAgent, File.ReadAllText(fixture.Transcript nestedAgent))
            let orphanBatches = ResizeArray<byte array>()
            capture orphan orphanBatches [ orphan.Transcript nestedAgent ] |> ignore
            let orphaned = facts (Assert.Single orphanBatches)
            Assert.Empty(ofKind "expected-dispatch" orphaned)
            Assert.Equal(Some "dispatch-identity-unavailable", field (Assert.Single(ofKind "diagnostic" orphaned)) "code")

    [<Fact>]
    let ``a main-session transcript and a Codex rollout carry no dispatch status`` () =
        if supported () then
            let cleanup, root = temp ()
            use cleanup = cleanup
            let fixture = layout root
            writeAttributed fixture
            let mainResponse =
                (response agent "msg_main" 5L 100L 10L 9L "2026-11-01T08:59:30.000Z").Replace($"\"isSidechain\":true,\"agentId\":\"{agent}\",", "\"isSidechain\":false,")
            File.WriteAllText(fixture.Parent, mainPrompt + mainResponse)
            let batches = ResizeArray<byte array>()
            capture fixture batches [ fixture.Parent ] |> ignore
            let observation = Assert.Single(ofKind "provider-usage-observation/1" (facts (Assert.Single batches)))
            Assert.Equal(Some session, field observation "threadId")
            for batch in batches do
                Assert.Empty(facts batch |> List.filter (fun fact -> fact.Kind = "expected-dispatch" || fact.Kind = "diagnostic"))
            Assert.Empty(fst (dispatchState root))
            Assert.Equal(None, SkillTelemetryReaders.NativeUsage.claudeSubagentSource fixture.Home fixture.Parent)
            Assert.True((SkillTelemetryReaders.NativeUsage.claudeSubagentSource fixture.Home (fixture.Transcript agent)).IsSome)
            // A subagent-shaped path outside the selected projects root is not a subagent source.
            Assert.Equal(None, SkillTelemetryReaders.NativeUsage.claudeSubagentSource (Path.Combine(root, "elsewhere")) (fixture.Transcript agent))
