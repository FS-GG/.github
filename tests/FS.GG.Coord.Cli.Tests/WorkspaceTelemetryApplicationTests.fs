namespace FS.GG.Coord.Cli.Tests

open System
open System.IO
open System.Text
open System.Diagnostics
open Xunit
open FS.GG.Coord.Cli
open FS.GG.Coord

module WorkspaceTelemetryApplicationTests =
    let private temp () =
        let root =
            Path.Combine(Path.GetTempPath(), "fsgg-l1-" + Guid.NewGuid().ToString("N"))

        Directory.CreateDirectory root |> ignore

        if not (OperatingSystem.IsWindows()) then
            File.SetUnixFileMode(root, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute)

        { new IDisposable with
            member _.Dispose() =
                if Directory.Exists root then
                    Directory.Delete(root, true)
        },
        root

    let private invoke action args =
        let priorOut, priorError = Console.Out, Console.Error
        use stdout = new StringWriter()
        use stderr = new StringWriter()

        try
            Console.SetOut stdout
            Console.SetError stderr
            let code = WorkspaceTelemetryApplication.run action args
            code, stdout.ToString(), stderr.ToString()
        finally
            Console.SetOut priorOut
            Console.SetError priorError

    let private payload item =
        Encoding.UTF8.GetBytes
            $"""{{"schema":"{TelemetryStore.BatchSchema}","ingestId":"bad id","sourceIdentity":"source","generation":"g1","cursor":"1","eventCount":1,"events":[{{"kind":"item","identity":"item-1","itemId":"{item}","revision":0}}]}}"""

    let private remoteArgs config spool producer =
        [
            "--config"
            config
            "--workspace"
            "workspace-a"
            "--producer"
            producer
            "--stream"
            "runtime"
            "--repository"
            "FS-GG/.github"
            "--endpoint"
            "https://127.0.0.1:1/"
            "--credential-reference"
            "main"
            "--spool-root"
            spool
        ]

    [<Fact>]
    let ``unconfigured status is read only`` () =
        let cleanup, root = temp ()
        use cleanup = cleanup
        let config = Path.Combine(root, "missing.json")

        let code, stdout, _ =
            invoke "status" [ "--config"; config; "--repository"; "FS-GG/.github" ]

        Assert.Equal(0, code)
        Assert.Contains("unconfigured", stdout)
        Assert.False(File.Exists(config + ".lock"))

    [<Fact>]
    let ``remote association spools one identity and refuses changed content`` () =
        if
            OperatingSystem.IsLinux()
            && Runtime.InteropServices.RuntimeInformation.ProcessArchitecture = Runtime.InteropServices.Architecture.X64
        then
            let cleanup, root = temp ()
            use cleanup = cleanup
            let config = Path.Combine(root, "telemetry.json")
            let spool = Path.Combine(root, "spool")
            Environment.SetEnvironmentVariable("FSGG_TELEMETRY_CREDENTIAL_MAIN", String('x', 32))

            try
                let code, _, error =
                    invoke
                        "activate-remote"
                        [
                            "--config"
                            config
                            "--workspace"
                            "workspace-a"
                            "--producer"
                            "producer-a"
                            "--stream"
                            "runtime"
                            "--repository"
                            "FS-GG/.github"
                            "--endpoint"
                            "https://127.0.0.1:1/"
                            "--credential-reference"
                            "main"
                            "--spool-root"
                            spool
                        ]

                Assert.True((code = 0), error)

                let first =
                    WorkspaceTelemetryApplication.tryPublish (Some config) (Some "FS-GG/.github") (payload "item-a")

                Assert.True((first = Error [ "unacknowledged-lossy" ]), sprintf "unexpected first result: %A" first)
                Assert.Single(Directory.EnumerateFiles(spool, "*.ready")) |> ignore

                let second =
                    WorkspaceTelemetryApplication.tryPublish (Some config) (Some "FS-GG/.github") (payload "item-b")

                Assert.True((second = Error [ "identity-conflict" ]), sprintf "unexpected second result: %A" second)
                Assert.Single(Directory.EnumerateFiles(spool, "*.ready")) |> ignore

                let code, status, _ =
                    invoke "status" [ "--config"; config; "--repository"; "FS-GG/.github" ]

                Assert.Equal(0, code)
                Assert.Contains("\"pending\":1", status)
                Assert.Contains("\"pendingDurablyReceived\":0", status)
                Assert.Contains("\"pendingUnacknowledged\":1", status)
                Assert.Contains("\"unacknowledgedLossy\":true", status)
            finally
                Environment.SetEnvironmentVariable("FSGG_TELEMETRY_CREDENTIAL_MAIN", null)

    [<Fact>]
    let ``closed config rejects duplicate properties and old host config`` () =
        let cleanup, root = temp ()
        use cleanup = cleanup

        for index, json in
            [
                0,
                "{\"schema\":\"fsgg.telemetry.workspace-config/1\",\"schema\":\"fsgg.telemetry.workspace-config/1\",\"engine\":\"fsgg-coord-engine\",\"associations\":[],\"retiredAssociations\":[]}"
                1,
                "{\"schema\":\"fsgg.telemetry.host-config/1\",\"storeRoot\":\"/private\",\"engine\":\"fsgg-coord-engine\"}"
            ] do
            let path = Path.Combine(root, string index + ".json")
            File.WriteAllText(path, json)
            File.SetUnixFileMode(path, UnixFileMode.UserRead ||| UnixFileMode.UserWrite)

            let code, _, _ =
                invoke "status" [ "--config"; path; "--repository"; "FS-GG/.github" ]

            Assert.NotEqual(0, code)

    [<Fact>]
    let ``default XDG association is selected and preserves repository spelling`` () =
        if
            OperatingSystem.IsLinux()
            && Runtime.InteropServices.RuntimeInformation.ProcessArchitecture = Runtime.InteropServices.Architecture.X64
        then
            let cleanup, root = temp ()
            use cleanup = cleanup
            let xdg = Path.Combine(root, "xdg")
            Directory.CreateDirectory xdg |> ignore
            File.SetUnixFileMode(xdg, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute)
            let directory = Path.Combine(xdg, "fs-gg")
            let config = Path.Combine(directory, "telemetry.json")
            let spool = Path.Combine(root, "spool")
            let oldXdg = Environment.GetEnvironmentVariable "XDG_CONFIG_HOME"
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", xdg)
            Environment.SetEnvironmentVariable("FSGG_TELEMETRY_REPOSITORY", "FS-GG/.github")
            Environment.SetEnvironmentVariable("FSGG_TELEMETRY_CREDENTIAL_MAIN", String('x', 32))

            try
                let code, _, error =
                    invoke "activate-remote" (remoteArgs config spool "producer-a" |> List.tail |> List.tail)

                Assert.True((code = 0), error)

                match WorkspaceTelemetryApplication.tryBinding None None with
                | Some(path, repository, digest) ->
                    Assert.Equal(config, path)
                    Assert.Equal("FS-GG/.github", repository)
                    Assert.Equal(64, digest.Length)
                | None -> failwith "default association was not selected"
            finally
                Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", oldXdg)
                Environment.SetEnvironmentVariable("FSGG_TELEMETRY_REPOSITORY", null)
                Environment.SetEnvironmentVariable("FSGG_TELEMETRY_CREDENTIAL_MAIN", null)

    [<Fact>]
    let ``cutover archives identity and refuses A to B to A reuse`` () =
        if
            OperatingSystem.IsLinux()
            && Runtime.InteropServices.RuntimeInformation.ProcessArchitecture = Runtime.InteropServices.Architecture.X64
        then
            let cleanup, root = temp ()
            use cleanup = cleanup

            let config, spoolA, spoolB =
                Path.Combine(root, "telemetry.json"), Path.Combine(root, "spool-a"), Path.Combine(root, "spool-b")

            Environment.SetEnvironmentVariable("FSGG_TELEMETRY_CREDENTIAL_MAIN", String('x', 32))

            try
                Assert.Equal(
                    0,
                    let code, _, _ = invoke "activate-remote" (remoteArgs config spoolA "producer-a") in code
                )

                let common producer spool =
                    [
                        "--config"
                        config
                        "--workspace"
                        "workspace-a"
                        "--producer"
                        producer
                        "--stream"
                        "runtime"
                        "--to"
                        "remote"
                        "--endpoint"
                        "https://127.0.0.1:1/"
                        "--credential-reference"
                        "main"
                        "--spool-root"
                        spool
                    ]

                Assert.Equal(0, let code, _, _ = invoke "cutover" (common "producer-b" spoolB) in code)

                Assert.Equal(
                    Error [ "association-stale" ],
                    WorkspaceTelemetryApplication.tryPublishExpected
                        (Some config)
                        (Some "FS-GG/.github")
                        (Some "producer-a")
                        (payload "item-a")
                )

                Assert.NotEqual(0, let code, _, _ = invoke "cutover" (common "producer-a" spoolA) in code)
            finally
                Environment.SetEnvironmentVariable("FSGG_TELEMETRY_CREDENTIAL_MAIN", null)

    [<Fact>]
    let ``remote spool capacity refuses before writing a new input`` () =
        if
            OperatingSystem.IsLinux()
            && Runtime.InteropServices.RuntimeInformation.ProcessArchitecture = Runtime.InteropServices.Architecture.X64
        then
            let cleanup, root = temp ()
            use cleanup = cleanup

            let config, spool =
                Path.Combine(root, "telemetry.json"), Path.Combine(root, "spool")

            Environment.SetEnvironmentVariable("FSGG_TELEMETRY_CREDENTIAL_MAIN", String('x', 32))

            try
                Assert.Equal(
                    0,
                    let code, _, _ = invoke "activate-remote" (remoteArgs config spool "producer-a") in code
                )

                for index in 1..128 do
                    let file = Path.Combine(spool, $"occupied-{index}.ready")
                    File.WriteAllText(file, "x")
                    File.SetUnixFileMode(file, UnixFileMode.UserRead ||| UnixFileMode.UserWrite)

                Assert.Equal(
                    Error [ "spool-overload" ],
                    WorkspaceTelemetryApplication.tryPublish (Some config) (Some "FS-GG/.github") (payload "item-a")
                )

                Assert.Equal(128, Directory.EnumerateFiles(spool, "*.ready") |> Seq.length)
            finally
                Environment.SetEnvironmentVariable("FSGG_TELEMETRY_CREDENTIAL_MAIN", null)

    [<Fact>]
    let ``production local activation refuses the unqualified overlay without residue`` () =
        if
            OperatingSystem.IsLinux()
            && Runtime.InteropServices.RuntimeInformation.ProcessArchitecture = Runtime.InteropServices.Architecture.X64
        then
            let cleanup, root = temp ()
            use cleanup = cleanup

            let config, store =
                Path.Combine(root, "telemetry.json"), Path.Combine(root, "store")

            let code, _, _ =
                invoke
                    "activate-local"
                    [
                        "--config"
                        config
                        "--workspace"
                        "workspace-a"
                        "--producer"
                        "producer-a"
                        "--stream"
                        "runtime"
                        "--repository"
                        "FS-GG/.github"
                        "--store-root"
                        store
                    ]

            Assert.NotEqual(0, code)
            Assert.False(File.Exists config)

    [<Fact>]
    let ``configuration lock excludes a second process and symlink config is refused`` () =
        if
            OperatingSystem.IsLinux()
            && Runtime.InteropServices.RuntimeInformation.ProcessArchitecture = Runtime.InteropServices.Architecture.X64
        then
            let cleanup, root = temp ()
            use cleanup = cleanup

            let config, spool =
                Path.Combine(root, "telemetry.json"), Path.Combine(root, "spool")

            Environment.SetEnvironmentVariable("FSGG_TELEMETRY_CREDENTIAL_MAIN", String('x', 32))

            try
                Assert.Equal(
                    0,
                    let code, _, _ = invoke "activate-remote" (remoteArgs config spool "producer-a") in code
                )

                let start = ProcessStartInfo("flock")
                start.UseShellExecute <- false
                start.RedirectStandardOutput <- true

                for value in [ "-n"; config + ".lock"; "sh"; "-c"; "echo ready; sleep 5" ] do
                    start.ArgumentList.Add value

                use holder = Process.Start start
                Assert.Equal("ready", holder.StandardOutput.ReadLine())

                let code, _, error =
                    invoke
                        "associate-repository"
                        [
                            "--config"
                            config
                            "--workspace"
                            "workspace-a"
                            "--repository"
                            "FS-GG/second"
                        ]

                Assert.NotEqual(0, code)
                Assert.Contains("configuration-busy", error)
                holder.Kill(true)
                let link = Path.Combine(root, "linked.json")
                File.CreateSymbolicLink(link, config) |> ignore

                let linkedCode, _, _ =
                    invoke "status" [ "--config"; link; "--repository"; "FS-GG/.github" ]

                Assert.NotEqual(0, linkedCode)
                let ancestor = Path.Combine(root, "alias")
                Directory.CreateSymbolicLink(ancestor, root) |> ignore

                let ancestorCode, _, _ =
                    invoke
                        "status"
                        [
                            "--config"
                            Path.Combine(ancestor, "telemetry.json")
                            "--repository"
                            "FS-GG/.github"
                        ]

                Assert.NotEqual(0, ancestorCode)
            finally
                Environment.SetEnvironmentVariable("FSGG_TELEMETRY_CREDENTIAL_MAIN", null)

    [<Fact>]
    let ``activation refuses duplicate producers and repository case aliases before serialization`` () =
        if
            OperatingSystem.IsLinux()
            && Runtime.InteropServices.RuntimeInformation.ProcessArchitecture = Runtime.InteropServices.Architecture.X64
        then
            let cleanup, root = temp ()
            use cleanup = cleanup
            let config = Path.Combine(root, "telemetry.json")
            Environment.SetEnvironmentVariable("FSGG_TELEMETRY_CREDENTIAL_MAIN", String('x', 32))

            try
                Assert.Equal(
                    0,
                    let code, _, _ =
                        invoke "activate-remote" (remoteArgs config (Path.Combine(root, "spool-a")) "producer-a") in

                    code
                )

                let duplicateProducer =
                    [
                        "--config"
                        config
                        "--workspace"
                        "workspace-b"
                        "--producer"
                        "producer-a"
                        "--stream"
                        "runtime"
                        "--repository"
                        "FS-GG/other"
                        "--endpoint"
                        "https://127.0.0.1:1/"
                        "--credential-reference"
                        "main"
                        "--spool-root"
                        Path.Combine(root, "spool-b")
                    ]

                Assert.NotEqual(0, let code, _, _ = invoke "activate-remote" duplicateProducer in code)

                let aliases =
                    [
                        "--config"
                        config
                        "--workspace"
                        "workspace-c"
                        "--producer"
                        "producer-c"
                        "--stream"
                        "runtime"
                        "--repository"
                        "FS-GG/Third"
                        "--repository"
                        "fs-gg/third"
                        "--endpoint"
                        "https://127.0.0.1:1/"
                        "--credential-reference"
                        "main"
                        "--spool-root"
                        Path.Combine(root, "spool-c")
                    ]

                Assert.NotEqual(0, let code, _, _ = invoke "activate-remote" aliases in code)

                Assert.Equal(
                    0,
                    let code, _, _ =
                        invoke "status" [ "--config"; config; "--repository"; "FS-GG/.github" ] in

                    code
                )
            finally
                Environment.SetEnvironmentVariable("FSGG_TELEMETRY_CREDENTIAL_MAIN", null)
    let private passiveSource (root: string) (name: string) =
        let home = Path.Combine(root, "codex")
        let sessions = Path.Combine(home, "sessions")
        Directory.CreateDirectory sessions |> ignore
        home, Path.Combine(sessions, name + ".jsonl")

    // Inert native shape, using one real observed counter tuple with pseudonymous native IDs.
    let private nativeRow (response: string) (input: int64) (cacheWrite: int64) =
        $"""{{"timestamp":"2026-10-18T06:41:48.322Z","type":"token_usage_record","payload":{{"thread_id":"7f4b2770-a3ce-54b8-be37-7992c3c9091b","turn_id":"f8097483-03a3-5055-88a4-eb7e2ee32446","response_id":"{response}","usage":{{"input_tokens":{input},"cached_input_tokens":0,"cache_write_input_tokens":{cacheWrite},"output_tokens":72,"reasoning_output_tokens":0,"total_tokens":{input + 72L}}},"turn_token_usage":{{"input_tokens":999999,"cached_input_tokens":0,"cache_write_input_tokens":0,"output_tokens":1,"reasoning_output_tokens":0,"total_tokens":1000000}}}}}}""" + "\n"

    let private parseCapture (value: SkillTelemetryAdapter.CommandResult) =
        Assert.True((value.ExitCode = 0), Encoding.UTF8.GetString value.Stderr)
        System.Text.Json.Nodes.JsonNode.Parse value.Stdout

    [<Fact>]
    let ``passive modern reader defers tails rotation and oversized lines without provider calls`` () =
        if OperatingSystem.IsLinux() && Runtime.InteropServices.RuntimeInformation.ProcessArchitecture = Runtime.InteropServices.Architecture.X64 then
            let cleanup, root = temp ()
            use cleanup = cleanup
            let home, path = passiveSource root "selected"
            let row = nativeRow "response-a" 22810L 0L
            File.WriteAllText(path, row.TrimEnd('\n'))
            let scan cursor budget = SkillTelemetryReaders.NativeUsage.scanPassive home path cursor budget 64 |> Result.defaultWith (fun error -> failwith error.Message)
            let tail = scan None 4194304
            Assert.Empty tail.Records
            Assert.Equal(0L, tail.Cursor.Offset)
            Assert.Contains("partial-tail-deferred", tail.Diagnostics)
            File.AppendAllText(path, "\n")
            let complete = scan (Some tail.Cursor) 4194304
            Assert.Single complete.Records |> ignore
            Assert.Equal(Some 22810L, complete.Records.Head.Usage.Input)
            Assert.Equal(Some 0L, complete.Records.Head.Usage.CacheWriteInput)
            Assert.Equal(None, complete.Records.Head.Model)
            Assert.Equal(0L, complete.Records.Head.Position)
            Assert.Empty((scan (Some complete.Cursor) 4194304).Records)
            File.Move(path, path + ".old")
            File.WriteAllText(path, row)
            let rotated = scan (Some complete.Cursor) 4194304
            Assert.Contains("source-continuity-gap", rotated.Diagnostics)
            Assert.NotEqual<string>(complete.Cursor.SourceInstance, rotated.Cursor.SourceInstance)
            Assert.NotEqual<string>(complete.Cursor.Generation, rotated.Cursor.Generation)
            File.WriteAllText(path, String('x', 2 * 1024 * 1024) + "\n" + row)
            let oversized = scan (Some rotated.Cursor) 1200000
            Assert.True(oversized.Cursor.SkippingOversized)
            Assert.True(oversized.ScannedBytes <= 1200000)
            Assert.Contains("oversized-line-skipped", oversized.Diagnostics)
            let resumed = scan (Some oversized.Cursor) 4194304
            Assert.Single resumed.Records |> ignore
            Assert.False(resumed.Cursor.SkippingOversized)
            File.WriteAllText(path, row + row)
            let boundary = scan (Some resumed.Cursor) 4194304
            Assert.Single boundary.Records |> ignore
            Assert.Equal(int64(Encoding.UTF8.GetByteCount row), boundary.Cursor.Offset)
            Assert.Contains("response-boundary-deferred", boundary.Diagnostics)

    // Synthetic Claude Code transcript records in the observed native shape; no private transcript content.
    let private claudeSession = "5f0c2a8e-3b1d-4e6f-9a7b-1c2d3e4f5a6b"
    let private claudePromptA = "0a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c4d"
    let private claudePromptB = "1b2c3d4e-5f6a-4b7c-9d8e-0f1a2b3c4d5e"

    let private claudeSource (root: string) (name: string) =
        let home = Path.Combine(root, "claude")
        let project = Path.Combine(home, "projects", "-work")
        Directory.CreateDirectory project |> ignore
        home, Path.Combine(project, name + ".jsonl")

    let private claudePrompt (prompt: string) =
        $"""{{"parentUuid":null,"isSidechain":false,"promptId":"{prompt}","type":"user","message":{{"role":"user","content":"synthetic"}},"uuid":"00000000-0000-4000-8000-000000000001","timestamp":"2026-10-22T05:00:00.000Z","cwd":"/work","sessionId":"{claudeSession}","version":"2.1.299"}}""" + "\n"

    let private claudeResponse (messageId: string) (second: int) (stop: string) (output: int64) =
        $"""{{"parentUuid":"00000000-0000-4000-8000-000000000001","isSidechain":false,"message":{{"model":"claude-opus-5-5","id":"{messageId}","type":"message","role":"assistant","content":[{{"type":"text","text":"synthetic"}}],"stop_reason":{stop},"usage":{{"input_tokens":2,"cache_creation_input_tokens":300,"cache_read_input_tokens":4000,"output_tokens":{output},"output_tokens_details":{{"thinking_tokens":7}}}}}},"requestId":"req_{messageId}","type":"assistant","uuid":"00000000-0000-4000-9000-0000000000{second:D2}","timestamp":"2026-10-22T05:01:{second:D2}.000Z","cwd":"/work","sessionId":"{claudeSession}","version":"2.1.299","effort":"high"}}""" + "\n"

    [<Fact>]
    let ``passive Claude reader deduplicates per-block records and carries prompt context across passes`` () =
        if OperatingSystem.IsLinux() && Runtime.InteropServices.RuntimeInformation.ProcessArchitecture = Runtime.InteropServices.Architecture.X64 then
            let cleanup, root = temp ()
            use cleanup = cleanup
            let home, path = claudeSource root "session"
            let source = SkillTelemetryReaders.ClaudeHome home
            let scan cursor = SkillTelemetryReaders.NativeUsage.scanPassiveFrom source path cursor 4194304 64 |> Result.defaultWith (fun error -> failwith error.Message)
            // A streaming record before stop_reason never becomes an observation.
            File.WriteAllText(path, claudePrompt claudePromptA + claudeResponse "msg_a" 1 "null" 8L)
            let streaming = scan None
            Assert.Empty streaming.Records
            Assert.Equal(Some claudePromptA, streaming.Cursor.Context)
            File.AppendAllText(path, claudeResponse "msg_a" 2 "\"tool_use\"" 50L + claudeResponse "msg_a" 3 "\"tool_use\"" 50L)
            let completed = scan (Some streaming.Cursor)
            let record = Assert.Single completed.Records
            Assert.Equal("msg_a", record.ResponseId)
            Assert.Equal(Guid.Parse claudeSession, record.ThreadId)
            Assert.Equal(Guid.Parse claudePromptA, record.TurnId)
            Assert.Equal(SkillTelemetryReaders.NativeUsage.ClaudePassiveProvenance, record.Provenance)
            Assert.Equal(Some 4302L, record.Usage.Input)
            Assert.Equal(Some 4000L, record.Usage.CachedInput)
            Assert.Equal(Some 300L, record.Usage.CacheWriteInput)
            Assert.Equal(Some 50L, record.Usage.Output)
            Assert.Equal(Some 7L, record.Usage.Reasoning)
            Assert.Equal(Some 4352L, record.Usage.Total)
            Assert.Equal(Some "claude-opus-5-5", record.Model)
            Assert.Equal(Some "high", record.Effort)
            Assert.DoesNotContain("response-boundary-deferred", completed.Diagnostics)
            Assert.Equal(
                SkillTelemetryReaders.NativeUsage.passiveClaudeIdentity record.ThreadId record.TurnId record.ResponseId,
                SkillTelemetryReaders.NativeUsage.passiveRecordIdentity record)
            Assert.NotEqual<string>(
                SkillTelemetryReaders.NativeUsage.passiveIdentity record.ThreadId record.TurnId record.ResponseId,
                SkillTelemetryReaders.NativeUsage.passiveRecordIdentity record)
            // A repeat in a later pass has the same allowlisted digest despite its different block timestamp.
            File.AppendAllText(path, claudeResponse "msg_a" 4 "\"tool_use\"" 50L)
            let repeated = Assert.Single((scan (Some completed.Cursor)).Records)
            Assert.Equal(record.SourceDigest, repeated.SourceDigest)
            // The resumed cursor attributes a later prompt exactly as a full read does.
            let beforeB = scan (Some completed.Cursor)
            File.AppendAllText(path, claudePrompt claudePromptB + claudeResponse "msg_b" 5 "\"end_turn\"" 9L)
            let resumed = scan (Some beforeB.Cursor)
            let fromStart = scan None
            let resumedB = resumed.Records |> List.find (fun row -> row.ResponseId = "msg_b")
            let fullB = fromStart.Records |> List.find (fun row -> row.ResponseId = "msg_b")
            Assert.Equal(Guid.Parse claudePromptB, resumedB.TurnId)
            Assert.Equal(fullB.TurnId, resumedB.TurnId)
            Assert.Equal(fullB.SourceDigest, resumedB.SourceDigest)
            // A disagreeing repeat within one pass is a correction boundary, never a silent overwrite.
            File.AppendAllText(path, claudeResponse "msg_b" 6 "\"end_turn\"" 10L)
            let boundary = scan (Some resumed.Cursor)
            Assert.Single boundary.Records |> ignore
            File.WriteAllText(path, claudePrompt claudePromptA + claudeResponse "msg_c" 1 "\"end_turn\"" 9L + claudeResponse "msg_c" 2 "\"end_turn\"" 10L)
            let disagreeing = scan None
            Assert.Single disagreeing.Records |> ignore
            Assert.Contains("response-boundary-deferred", disagreeing.Diagnostics)

    [<Fact>]
    let ``passive Claude reader withholds unattributable malformed partial and outside sources`` () =
        if OperatingSystem.IsLinux() && Runtime.InteropServices.RuntimeInformation.ProcessArchitecture = Runtime.InteropServices.Architecture.X64 then
            let cleanup, root = temp ()
            use cleanup = cleanup
            let home, path = claudeSource root "session"
            let source = SkillTelemetryReaders.ClaudeHome home
            let scan cursor = SkillTelemetryReaders.NativeUsage.scanPassiveFrom source path cursor 4194304 64 |> Result.defaultWith (fun error -> failwith error.Message)
            // Control: an attributable completed response is observed.
            File.WriteAllText(path, claudePrompt claudePromptA + claudeResponse "msg_a" 1 "\"end_turn\"" 9L)
            Assert.Single((scan None).Records) |> ignore
            // No preceding prompt: the turn is unknown, so no observation is invented.
            File.WriteAllText(path, claudeResponse "msg_a" 1 "\"end_turn\"" 9L)
            let orphan = scan None
            Assert.Empty orphan.Records
            Assert.Contains("turn-identity-unavailable", orphan.Diagnostics)
            // A malformed line may have been a prompt; later responses stay unattributed until the next prompt.
            File.WriteAllText(path, claudePrompt claudePromptA + "{\"type\":\"user\",\"promptId\":\n" + claudeResponse "msg_a" 1 "\"end_turn\"" 9L)
            let malformed = scan None
            Assert.Empty malformed.Records
            Assert.Contains("malformed-row-skipped", malformed.Diagnostics)
            Assert.Contains("turn-identity-unavailable", malformed.Diagnostics)
            // Missing counters are malformed, never zero.
            File.WriteAllText(path, claudePrompt claudePromptA + (claudeResponse "msg_a" 1 "\"end_turn\"" 9L).Replace("\"cache_read_input_tokens\":4000,", ""))
            let missing = scan None
            Assert.Empty missing.Records
            Assert.Contains("malformed-row-skipped", missing.Diagnostics)
            // A partial tail is deferred without advancing past it.
            let complete = claudePrompt claudePromptA
            File.WriteAllText(path, complete + (claudeResponse "msg_a" 1 "\"end_turn\"" 9L).TrimEnd('\n'))
            let tail = scan None
            Assert.Empty tail.Records
            Assert.Contains("partial-tail-deferred", tail.Diagnostics)
            Assert.Equal(int64 (Encoding.UTF8.GetByteCount complete), tail.Cursor.Offset)
            File.AppendAllText(path, "\n")
            let completedTail = scan (Some tail.Cursor)
            Assert.Single completedTail.Records |> ignore
            // A rotated source is a continuity gap: a new generation rereads from the start with no carried
            // prompt context, so the replacement's responses are attributed only by its own prompts.
            File.Move(path, path + ".old")
            File.WriteAllText(path, claudeResponse "msg_r" 2 "\"end_turn\"" 9L)
            let rotated = scan (Some completedTail.Cursor)
            Assert.Contains("source-continuity-gap", rotated.Diagnostics)
            Assert.NotEqual<string>(completedTail.Cursor.Generation, rotated.Cursor.Generation)
            Assert.Empty rotated.Records
            Assert.Contains("turn-identity-unavailable", rotated.Diagnostics)
            // Only files below the selected Claude projects root are readable.
            let outside = Path.Combine(home, "session.jsonl")
            File.WriteAllText(outside, complete)
            Assert.True(SkillTelemetryReaders.NativeUsage.scanPassiveFrom source outside None 4194304 64 |> Result.isError)
            Assert.True(SkillTelemetryReaders.NativeUsage.scanPassiveFrom (SkillTelemetryReaders.CodexHome home) path None 4194304 64 |> Result.isError)

    [<Fact>]
    let ``passive Claude capture enqueues admitted provider usage with its own provenance and corrections`` () =
        if OperatingSystem.IsLinux() && Runtime.InteropServices.RuntimeInformation.ProcessArchitecture = Runtime.InteropServices.Architecture.X64 then
            let cleanup, root = temp ()
            use cleanup = cleanup
            let home, path = claudeSource root "session"
            let source = SkillTelemetryReaders.ClaudeHome home
            File.WriteAllText(path, claudePrompt claudePromptA + claudeResponse "msg_a" 1 "\"tool_use\"" 50L)
            let batches = ResizeArray<byte array>()
            let capture () =
                SkillTelemetryAdapter.capturePassiveFrom root "owner" "passive-stream" source [ path ] (fun bytes -> batches.Add(Array.copy bytes); Ok "locally-retained")
                |> parseCapture
            let first = capture ()
            let row = Assert.Single(first["observations"].AsArray())
            Assert.Equal("claude-local-transcript/1", row["provenance"].GetValue<string>())
            Assert.Equal("partial", row["populationCoverage"].GetValue<string>())
            Assert.Equal("unknown", row["attributionCoverage"].GetValue<string>())
            Assert.Null(row["itemId"])
            Assert.Equal(7L, row["reasoning"].GetValue<int64>())
            let parsed = TelemetryStore.parseBatch batches[0] |> Result.defaultWith (sprintf "%A" >> failwith)
            let fact = Assert.Single(parsed.Facts |> List.filter (fun value -> value.Kind = "provider-usage-observation/1"))
            Assert.Equal(row["identity"].GetValue<string>(), fact.Identity)
            let state = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllBytes(Path.Combine(root, "orchestrator-passive", "producer.json")))
            let cursor = state["sources"].AsObject() |> Seq.exactlyOne
            Assert.Equal(claudePromptA, cursor.Value["context"].GetValue<string>())
            // An identical per-block repeat is not a new observation.
            File.AppendAllText(path, claudeResponse "msg_a" 2 "\"tool_use\"" 50L)
            capture () |> ignore
            Assert.Equal(1, batches.Count)
            // A disagreeing later record is a numbered correction under the same identity.
            File.AppendAllText(path, claudeResponse "msg_a" 3 "\"tool_use\"" 60L)
            let corrected = capture ()
            Assert.Equal(2, batches.Count)
            let current = Assert.Single(corrected["observations"].AsArray())
            Assert.Equal(2L, current["revision"].GetValue<int64>())
            Assert.Equal(60L, current["output"].GetValue<int64>())
            let correction = TelemetryStore.parseBatch batches[1] |> Result.defaultWith (sprintf "%A" >> failwith)
            Assert.Equal(fact.Identity, (Assert.Single correction.Facts).Identity)

    [<Fact>]
    let ``passive capture persists intent before enqueue and replays exact identity after uncertain enqueue`` () =
        if OperatingSystem.IsLinux() && Runtime.InteropServices.RuntimeInformation.ProcessArchitecture = Runtime.InteropServices.Architecture.X64 then
            let cleanup, root = temp ()
            use cleanup = cleanup
            let home, path = passiveSource root "selected"
            File.WriteAllText(path, nativeRow "response-a" 22810L 0L)
            let calls = ResizeArray<byte array>()
            let statePath = Path.Combine(root, "orchestrator-passive", "producer.json")
            let mutable reject = true
            let enqueue (bytes: byte array) =
                let state = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllBytes statePath)
                Assert.Equal(Convert.ToBase64String bytes, (state["pending"]["batchBase64"]).GetValue<string>())
                Assert.Empty(state["sources"].AsObject())
                calls.Add(Array.copy bytes)
                if reject then Error [ "spool-overload" ] else Ok "locally-retained"
            let capture enqueue = SkillTelemetryAdapter.capturePassive root "owner" "passive-stream" home [path] enqueue |> parseCapture
            let first = capture enqueue
            Assert.Empty(first["observations"].AsArray())
            Assert.True(first["pendingIntent"].GetValue<bool>())
            let report = SkillTelemetryAdapter.reportPassive root "owner" "passive-stream" |> parseCapture
            Assert.True(report["pendingIntent"].GetValue<bool>())
            reject <- false
            let second = capture enqueue
            Assert.Equal<byte>(calls[0], calls[1])
            let row = Assert.Single(second["observations"].AsArray())
            Assert.Equal(1L, row["revision"].GetValue<int64>())
            Assert.Null(row["itemId"])
            Assert.Null(row["model"])
            Assert.False(row.AsObject().ContainsKey "cost")
            Assert.Equal(22882L, row["total"].GetValue<int64>())
            Assert.Equal("unknown", second["attribution"].GetValue<string>())
            Assert.Equal("unknown", second["delivery"].GetValue<string>())
            Assert.Equal(2, calls.Count)
            let mutable duplicateEnqueues = 0
            let noNew = capture (fun _ -> duplicateEnqueues <- duplicateEnqueues + 1; Ok "locally-retained")
            Assert.Equal(0, duplicateEnqueues)
            Assert.Single(noNew["observations"].AsArray()) |> ignore
            File.AppendAllText(path, nativeRow "response-a" 23000L 50L)
            let mutable correction = Array.empty<byte>
            let corrected = capture (fun bytes -> correction <- bytes; Ok "locally-retained")
            let current = Assert.Single(corrected["observations"].AsArray())
            Assert.Equal(2L, current["revision"].GetValue<int64>())
            Assert.Equal(23072L, current["total"].GetValue<int64>())
            Assert.Equal(50L, current["cacheWriteInput"].GetValue<int64>())
            let parsed = TelemetryStore.parseBatch correction |> Result.defaultWith (sprintf "%A" >> failwith)
            Assert.Single parsed.Facts |> ignore
            Assert.Equal(row["identity"].GetValue<string>(), parsed.Facts.Head.Identity)
            let before = File.ReadAllBytes statePath
            let mutable ownerConflictEnqueues = 0
            let refused = SkillTelemetryAdapter.capturePassive root "different-owner" "passive-stream" home [path] (fun _ -> ownerConflictEnqueues <- ownerConflictEnqueues + 1; Ok "locally-retained")
            Assert.Equal(0, ownerConflictEnqueues)
            Assert.Equal(1, refused.ExitCode)
            Assert.Equal<byte>(before, File.ReadAllBytes statePath)

    [<Fact>]
    let ``passive uncertain post-enqueue crash retains same intent and never replays workload`` () =
        if OperatingSystem.IsLinux() && Runtime.InteropServices.RuntimeInformation.ProcessArchitecture = Runtime.InteropServices.Architecture.X64 then
            let cleanup, root = temp ()
            use cleanup = cleanup
            let home, path = passiveSource root "crash"
            File.WriteAllText(path, nativeRow "response-a" 10L 0L)
            let mutable retained = Array.empty<byte>
            let interrupted = SkillTelemetryAdapter.capturePassive root "owner" "passive-stream" home [path] (fun bytes -> retained <- bytes; raise (IOException "crash after enqueue"))
            Assert.Equal(1, interrupted.ExitCode)
            File.Delete path // Recovery must reconcile intent before reopening a source that no longer exists.
            let mutable replay = Array.empty<byte>
            let recovered = SkillTelemetryAdapter.capturePassive root "owner" "passive-stream" home [path] (fun bytes -> replay <- bytes; Ok "locally-retained") |> parseCapture
            Assert.Equal<byte>(retained, replay)
            Assert.Single(recovered["observations"].AsArray()) |> ignore
            Assert.Contains("source-unavailable-cursor-retained", recovered["gaps"].ToJsonString())

    [<Fact>]
    let ``passive finite response index refuses additional identities without advancing their source`` () =
        if OperatingSystem.IsLinux() && Runtime.InteropServices.RuntimeInformation.ProcessArchitecture = Runtime.InteropServices.Architecture.X64 then
            let cleanup, root = temp ()
            use cleanup = cleanup
            let home, path = passiveSource root "full-index"
            File.WriteAllText(path, [0..255] |> List.map (fun index -> nativeRow ("response-" + string index) 10L 0L) |> String.concat "")
            for _ in 1..11 do
                SkillTelemetryAdapter.capturePassive root "owner" "passive-stream" home [path] (fun _ -> Ok "locally-retained") |> parseCapture |> ignore
            let statePath = Path.Combine(root, "orchestrator-passive", "producer.json")
            let before = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllBytes statePath)
            Assert.Equal(256, before["responses"].AsObject().Count)
            let cursor = before["sources"].ToJsonString()
            File.AppendAllText(path, nativeRow "overflow" 10L 0L)
            let mutable overflowEnqueues = 0
            let after = SkillTelemetryAdapter.capturePassive root "owner" "passive-stream" home [path] (fun _ -> overflowEnqueues <- overflowEnqueues + 1; Ok "locally-retained") |> parseCapture
            Assert.Equal(0, overflowEnqueues)
            Assert.Equal(256, after["observations"].AsArray().Count)
            Assert.Contains("response-index-full-capture-deferred", after["gaps"].ToJsonString())
            let state = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllBytes statePath)
            Assert.Equal(cursor, state["sources"].ToJsonString())

    let private passiveRemoteConfig (root: string) =
        let config = Path.Combine(root, "passive-config.json")
        let spool = Path.Combine(root, "spool")
        let node =
            System.Text.Json.Nodes.JsonNode.Parse
                $"""{{"schema":"fsgg.telemetry.workspace-config/1","engine":"absent-fixture-engine","associations":[{{"workspaceId":"workspace-a","producerId":"passive-a","streamId":"native-observations","repositories":["FS-GG/.github"],"destination":{{"kind":"remote","endpoint":"https://127.0.0.1:1/","credentialReference":"absent-passive-fixture","spoolRoot":"unused"}}}}],"retiredAssociations":[]}}"""
        node["associations"].[0].["destination"].["spoolRoot"] <- System.Text.Json.Nodes.JsonValue.Create spool
        File.WriteAllText(config, node.ToJsonString())
        if not (OperatingSystem.IsWindows()) then File.SetUnixFileMode(config, enum<UnixFileMode> 0o600)
        config, spool

    [<Fact>]
    let ``passive enqueue uses immutable existing spool offline and overload preserves intent`` () =
        if OperatingSystem.IsLinux() && Runtime.InteropServices.RuntimeInformation.ProcessArchitecture = Runtime.InteropServices.Architecture.X64 then
            let cleanup, root = temp ()
            use cleanup = cleanup
            let config, spool = passiveRemoteConfig root
            let stateRoot, scope, binding = WorkspaceTelemetryApplication.passiveProducer (Some config) (Some "FS-GG/.github") |> Result.defaultWith (sprintf "%A" >> failwith)
            let home, path = passiveSource root "spool"
            File.WriteAllText(path, nativeRow "response-a" 22810L 0L)
            let enqueue = WorkspaceTelemetryApplication.tryEnqueueBound (Some config) (Some "FS-GG/.github") (Some scope.Producer) (Some binding)
            let captured = SkillTelemetryAdapter.capturePassive stateRoot "owner" scope.Stream home [path] enqueue |> parseCapture
            Assert.Single(captured["observations"].AsArray()) |> ignore
            let file = Assert.Single(Directory.GetFiles(spool, "*.ready"))
            let original = File.ReadAllBytes file
            Assert.Equal("locally-retained", enqueue (Encoding.UTF8.GetBytes(((System.Text.Json.Nodes.JsonNode.Parse original)["payload"]).ToJsonString())) |> Result.defaultWith (sprintf "%A" >> failwith))
            Assert.Equal<byte>(original, File.ReadAllBytes file)
            // Existing spool bound, including uncertain ready entries, refuses rather than overwrites.
            for index in 1..127 do
                File.WriteAllBytes(Path.Combine(spool, "retained-" + string index + ".ready"), [|0uy|])
            File.AppendAllText(path, nativeRow "response-b" 20L 0L)
            let deferred = SkillTelemetryAdapter.capturePassive stateRoot "owner" scope.Stream home [path] enqueue |> parseCapture
            Assert.Single(deferred["observations"].AsArray()) |> ignore
            Assert.True(deferred["pendingIntent"].GetValue<bool>())
            Assert.Contains("spool-overload", deferred["firstEnqueueFailure"].ToJsonString())
            Assert.Equal(128, Directory.GetFiles(spool, "*.ready").Length)
            Assert.Equal<byte>(original, File.ReadAllBytes file)
            let before = File.ReadAllBytes(Path.Combine(spool, "orchestrator-passive", "producer.json"))
            let again = SkillTelemetryAdapter.capturePassive stateRoot "owner" scope.Stream home [path] enqueue |> parseCapture
            Assert.True(again["pendingIntent"].GetValue<bool>())
            Assert.Equal<byte>(before, File.ReadAllBytes(Path.Combine(spool, "orchestrator-passive", "producer.json")))

    [<Fact>]
    let ``capture-only remote setup needs no credential and never asserts receiver readiness`` () =
        if OperatingSystem.IsLinux() && Runtime.InteropServices.RuntimeInformation.ProcessArchitecture = Runtime.InteropServices.Architecture.X64 then
            let cleanup, root = temp ()
            use cleanup = cleanup
            let config = Path.Combine(root, "setup.json")
            let spool = Path.Combine(root, "spool")
            let args = [ "--config"; config; "--workspace"; "passive-workspace"; "--producer"; "passive-setup"
                         "--stream"; "native-observations"; "--repository"; "FS-GG/.github"
                         "--endpoint"; "https://127.0.0.1:1/"; "--credential-reference"; "absent-passive-fixture"; "--spool-root"; spool ]
            let refused, _, defaultError = invoke "activate-remote" args
            Assert.Equal(1, refused)
            Assert.Contains("credential-unavailable", defaultError)
            Assert.False(File.Exists config)
            let code, configured, error = invoke "activate-remote" ("--capture-only" :: args)
            Assert.True((code = 0), error)
            Assert.Equal("capture-configured;receiver-unverified", configured)
            let statusCode, status, _ = invoke "status" [ "--config"; config; "--repository"; "FS-GG/.github" ]
            Assert.Equal(0, statusCode)
            Assert.Contains("\"status\":\"configured\"", status)
            Assert.DoesNotContain("authenticated", status)
            Assert.DoesNotContain("healthy", status)
            let before = File.ReadAllBytes config
            let localCode, _, _ = invoke "activate-local" ("--capture-only" :: args)
            Assert.Equal(1, localCode)
            let cutoverCode, _, _ = invoke "cutover" ("--capture-only" :: args)
            Assert.Equal(1, cutoverCode)
            Assert.Equal<byte>(before, File.ReadAllBytes config)

    [<Fact>]
    let ``passive delivery serializes numeric corrections behind durable and uncertain predecessors`` () =
        if OperatingSystem.IsLinux() && Runtime.InteropServices.RuntimeInformation.ProcessArchitecture = Runtime.InteropServices.Architecture.X64 then
            let cleanup, root = temp ()
            use cleanup = cleanup
            let config, spool = passiveRemoteConfig root
            let stateRoot, scope, binding = WorkspaceTelemetryApplication.passiveProducer (Some config) (Some "FS-GG/.github") |> Result.defaultWith (sprintf "%A" >> failwith)
            let home, path = passiveSource root "ordered"
            File.WriteAllText(path, "")
            let enqueue = WorkspaceTelemetryApplication.tryEnqueueBound (Some config) (Some "FS-GG/.github") (Some scope.Producer) (Some binding)
            for index in 1..32 do
                File.AppendAllText(path, nativeRow "response-a" (int64 index) 0L)
                SkillTelemetryAdapter.capturePassive stateRoot "owner" scope.Stream home [path] enqueue |> parseCapture |> ignore
            let envelopes =
                Directory.GetFiles(spool, "*.ready") |> Array.map (fun file ->
                    let value = TelemetryReceipt.parse(File.ReadAllBytes file) |> Result.defaultWith (sprintf "%A" >> failwith)
                    value.BatchId, value) |> Map.ofArray
            // Force the earliest predecessor outside an arbitrary first-16 inventory prefix.
            let scrambled = Directory.GetFiles(spool, "*.ready") |> Array.sortByDescending (fun file ->
                (TelemetryReceipt.parse(File.ReadAllBytes file) |> Result.defaultWith (sprintf "%A" >> failwith)).Batch.Cursor |> Int32.Parse)
            let selected = WorkspaceTelemetryApplication.selectReadyFilesForTesting scrambled |> Result.defaultWith (sprintf "%A" >> failwith)
            let cursors = selected |> List.map (fun file ->
                (TelemetryReceipt.parse(File.ReadAllBytes file) |> Result.defaultWith (sprintf "%A" >> failwith)).Batch.Cursor |> Int32.Parse)
            Assert.Equal<int>([1], cursors)
            let submitted = ResizeArray<int>()
            let received = System.Collections.Generic.HashSet<string>()
            let mutable phase = "durable"
            let receipt (batchId: string) (status: string) (code: string option) : FS.GG.Telemetry.RemoteContract.Receipt =
                { Scope = scope; BatchId = batchId; Digest = envelopes[batchId].Digest; Status = status; Code = code }
            let operations: WorkspaceTelemetryApplication.RemoteOperations =
                { Lookup = fun _ batchId _ _ ->
                    let outcome =
                        if phase = "uncertain" then FS.GG.Telemetry.RemoteClient.Unacknowledged "network-unavailable"
                        elif received.Contains batchId then FS.GG.Telemetry.RemoteClient.Acknowledged(receipt batchId (if phase = "durable" then "durably-received" else "applied") None)
                        else FS.GG.Telemetry.RemoteClient.Unacknowledged "receipt-unavailable"
                    System.Threading.Tasks.Task.FromResult outcome
                  Submit = fun _ bytes _ ->
                    let envelope = TelemetryReceipt.parse bytes |> Result.defaultWith (sprintf "%A" >> failwith)
                    submitted.Add(Int32.Parse envelope.Batch.Cursor)
                    received.Add envelope.BatchId |> ignore
                    System.Threading.Tasks.Task.FromResult(FS.GG.Telemetry.RemoteClient.Acknowledged(receipt envelope.BatchId (if phase = "durable" then "durably-received" else "applied") None)) }
            let drain () = WorkspaceTelemetryApplication.tryDrainWithOperationsForTesting (Some config) (Some "FS-GG/.github") operations
            drain () |> ignore
            Assert.Equal<int>([1], submitted)
            Assert.Equal(32, Directory.GetFiles(spool, "*.ready").Length)
            phase <- "uncertain"
            drain () |> ignore
            Assert.Equal<int>([1], submitted)
            Assert.Equal(32, Directory.GetFiles(spool, "*.ready").Length)
            phase <- "applied"
            for _ in 1..32 do drain () |> ignore
            Assert.Empty(Directory.GetFiles(spool, "*.ready"))
            Assert.Equal<int>([1..32], submitted)

    [<Fact>]
    let ``unsupported and rejected passive receiver retains exact pending bytes without downgrade`` () =
        if OperatingSystem.IsLinux() && Runtime.InteropServices.RuntimeInformation.ProcessArchitecture = Runtime.InteropServices.Architecture.X64 then
            let cleanup, root = temp ()
            use cleanup = cleanup
            let config, spool = passiveRemoteConfig root
            let stateRoot, scope, binding = WorkspaceTelemetryApplication.passiveProducer (Some config) (Some "FS-GG/.github") |> Result.defaultWith (sprintf "%A" >> failwith)
            let home, path = passiveSource root "unsupported"
            File.WriteAllText(path, nativeRow "response-a" 10L 0L)
            let enqueue = WorkspaceTelemetryApplication.tryEnqueueBound (Some config) (Some "FS-GG/.github") (Some scope.Producer) (Some binding)
            SkillTelemetryAdapter.capturePassive stateRoot "owner" scope.Stream home [path] enqueue |> parseCapture |> ignore
            let file = Assert.Single(Directory.GetFiles(spool, "*.ready"))
            let before = File.ReadAllBytes file
            let envelope = TelemetryReceipt.parse before |> Result.defaultWith (sprintf "%A" >> failwith)
            let mutable reject = false
            let submittedBytes = ResizeArray<byte array>()
            let operations: WorkspaceTelemetryApplication.RemoteOperations =
                { Lookup = fun _ _ _ _ ->
                    System.Threading.Tasks.Task.FromResult(FS.GG.Telemetry.RemoteClient.Unacknowledged(if reject then "receipt-unavailable" else "unsupported-version"))
                  Submit = fun _ bytes _ ->
                    submittedBytes.Add(Array.copy bytes)
                    System.Threading.Tasks.Task.FromResult(FS.GG.Telemetry.RemoteClient.Acknowledged
                        { Scope = scope; BatchId = envelope.BatchId; Digest = envelope.Digest; Status = "rejected"; Code = Some "invalid-request" }) }
            let first = WorkspaceTelemetryApplication.tryDrainWithOperationsForTesting (Some config) (Some "FS-GG/.github") operations
            Assert.Equal(Error [ "passive-receiver-capability-refused:unsupported-version" ], first)
            Assert.Empty submittedBytes
            Assert.Equal<byte>(before, File.ReadAllBytes file)
            reject <- true
            let second = WorkspaceTelemetryApplication.tryDrainWithOperationsForTesting (Some config) (Some "FS-GG/.github") operations
            Assert.Equal(Error [ "passive-receiver-capability-or-admission-refused:invalid-request" ], second)
            Assert.Equal<byte>(before, Assert.Single submittedBytes)
            Assert.Equal<byte>(before, File.ReadAllBytes file)
            Assert.False(Directory.Exists(Path.Combine(spool, "outcomes")))

    [<Fact>]
    let ``passive root lock refuses competing capture during retained intent enqueue`` () =
        if OperatingSystem.IsLinux() && Runtime.InteropServices.RuntimeInformation.ProcessArchitecture = Runtime.InteropServices.Architecture.X64 then
            let cleanup, root = temp ()
            use cleanup = cleanup
            let home, path = passiveSource root "concurrent"
            File.WriteAllText(path, nativeRow "response-a" 10L 0L)
            let mutable competingEnqueues = 0
            let enqueue _ =
                let competitor = SkillTelemetryAdapter.capturePassive root "owner" "passive-stream" home [path] (fun _ -> competingEnqueues <- competingEnqueues + 1; Ok "locally-retained")
                Assert.Equal(1, competitor.ExitCode)
                Ok "locally-retained"
            let captured = SkillTelemetryAdapter.capturePassive root "owner" "passive-stream" home [path] enqueue |> parseCapture
            Assert.Equal(0, competingEnqueues)
            Assert.Single(captured["observations"].AsArray()) |> ignore

    [<Fact>]
    let ``generic passive receipt stores canonical corrections without qualified item totals`` () =
        if OperatingSystem.IsLinux() && Runtime.InteropServices.RuntimeInformation.ProcessArchitecture = Runtime.InteropServices.Architecture.X64 then
            let cleanup, root = temp ()
            use cleanup = cleanup
            let unwrap value = value |> Result.defaultWith (sprintf "%A" >> failwith)
            let expectError (expected: string list) (actual: Result<string, string list>) =
                Assert.True((actual = Error expected), sprintf "expected Error %A; actual %A" expected actual)
            let home, path = passiveSource root "receiver"
            let store = Path.Combine(root, "store")
            let approved = TelemetryStore.ApprovedLocalDurable
            TelemetryStoreApplication.initialize store approved |> unwrap |> ignore
            let scope: TelemetryReceipt.Scope = { Workspace = "passive-workspace"; Producer = "passive-owner"; Stream = "observations" }
            TelemetryStoreApplication.enrollReceiptProducer store approved scope |> unwrap |> ignore
            let batches = ResizeArray<byte array>()
            let capture () =
                SkillTelemetryAdapter.capturePassive root "owner" scope.Stream home [path] (fun bytes -> batches.Add(Array.copy bytes); Ok "locally-retained") |> parseCapture
            File.WriteAllText(path, nativeRow "response-a" 10L 0L)
            capture () |> ignore
            let envelope (who: TelemetryReceipt.Scope) (batchId: string) (bytes: byte array) =
                let value = System.Text.Json.Nodes.JsonObject()
                value["schema"] <- System.Text.Json.Nodes.JsonValue.Create TelemetryReceipt.Schema
                value["workspaceId"] <- System.Text.Json.Nodes.JsonValue.Create who.Workspace
                value["producerId"] <- System.Text.Json.Nodes.JsonValue.Create who.Producer
                value["streamId"] <- System.Text.Json.Nodes.JsonValue.Create who.Stream
                value["batchId"] <- System.Text.Json.Nodes.JsonValue.Create batchId
                value["payload"] <- System.Text.Json.Nodes.JsonNode.Parse(bytes: byte array)
                Encoding.UTF8.GetBytes(value.ToJsonString())
            let first = envelope scope "passive-first" batches[0]
            TelemetryStoreApplication.submitReceipt store approved scope first |> unwrap |> ignore
            TelemetryStoreApplication.drainReceipts store approved scope.Workspace |> unwrap |> ignore
            let replay = TelemetryStoreApplication.submitReceipt store approved scope first |> unwrap
            Assert.Contains("\"status\":\"applied\"", replay)
            File.AppendAllText(path, nativeRow "response-a" 20L 5L)
            capture () |> ignore
            TelemetryStoreApplication.submitReceipt store approved scope (envelope scope "passive-correction" batches[1]) |> unwrap |> ignore
            TelemetryStoreApplication.drainReceipts store approved scope.Workspace |> unwrap |> ignore
            let snapshot = System.Text.Json.Nodes.JsonNode.Parse(TelemetryStoreApplication.passiveObservations store approved scope |> unwrap)
            let current = Assert.Single(snapshot["observations"].AsArray())
            Assert.Equal(2L, current["revision"].GetValue<int64>())
            Assert.Equal(92L, current["total"].GetValue<int64>())
            Assert.Equal(5L, current["cacheWriteInput"].GetValue<int64>())
            Assert.Null(current["itemId"])
            Assert.Null(current["model"])
            Assert.False(current.AsObject().ContainsKey "cost")
            Assert.Equal("partial", snapshot["populationCoverage"].GetValue<string>())
            use connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(store, TelemetryStoreApplication.databaseFileName)};Pooling=False")
            connection.Open()
            let scalar (query: string) =
                use command = connection.CreateCommand()
                command.CommandText <- query
                command.ExecuteScalar()
            Assert.Equal(1L, Convert.ToInt64(scalar "SELECT count(*) FROM corrections WHERE kind='provider-usage-observation/1';"))
            Assert.Equal(1L, Convert.ToInt64(scalar "SELECT count(*) FROM ingest_facts WHERE kind='provider-usage-observation/1' AND item_id IS NULL;"))
            Assert.Equal(0L, Convert.ToInt64(scalar "SELECT count(*) FROM usage_observations;"))
            Assert.Equal(0L, Convert.ToInt64(scalar "SELECT count(*) FROM runtime_turn_usage;"))
            Assert.Equal(0L, Convert.ToInt64(scalar "SELECT count(*) FROM budget_dirty_items;"))
            Assert.Equal("generic", string(scalar "SELECT authority_role FROM fact_admissions;"))
            let peer = { scope with Producer = "different-owner"; Stream = "different-stream" }
            TelemetryStoreApplication.enrollReceiptProducer store approved peer |> unwrap |> ignore
            let changed = System.Text.Json.Nodes.JsonNode.Parse batches[1]
            changed["ingestId"] <- System.Text.Json.Nodes.JsonValue.Create "peer-correction"
            changed["events"].[0].["revision"] <- System.Text.Json.Nodes.JsonValue.Create 3L
            let peerEnvelope = envelope peer "peer-correction" (Encoding.UTF8.GetBytes(changed.ToJsonString()))
            TelemetryStoreApplication.submitReceipt store approved peer peerEnvelope |> unwrap |> ignore
            TelemetryStoreApplication.drainReceipts store approved scope.Workspace |> unwrap |> ignore
            Assert.Contains("\"status\":\"rejected\"", TelemetryStoreApplication.lookupReceipt store approved peer "peer-correction" |> unwrap)
            let replacement = System.Text.Json.Nodes.JsonNode.Parse batches[1]
            replacement["ingestId"] <- System.Text.Json.Nodes.JsonValue.Create "kind-replacement"
            replacement["eventCount"] <- System.Text.Json.Nodes.JsonValue.Create 1
            replacement["events"] <- System.Text.Json.Nodes.JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize
                [| {| kind = "diagnostic"; identity = current["identity"].GetValue<string>(); revision = 3L; code = "fixture"; severity = "info" |} |])
            TelemetryStoreApplication.submitReceipt store approved scope (envelope scope "kind-replacement" (Encoding.UTF8.GetBytes(replacement.ToJsonString()))) |> unwrap |> ignore
            TelemetryStoreApplication.drainReceipts store approved scope.Workspace |> unwrap |> ignore
            Assert.Contains("\"status\":\"rejected\"", TelemetryStoreApplication.lookupReceipt store approved scope "kind-replacement" |> unwrap)
            let afterReplacement = System.Text.Json.Nodes.JsonNode.Parse(TelemetryStoreApplication.passiveObservations store approved scope |> unwrap)
            Assert.Equal(snapshot["observations"].ToJsonString(), afterReplacement["observations"].ToJsonString())
            // Exercise the existing application error boundary, including the owner-required
            // refusal whose normal unauthenticated route is direct ingest rather than a receipt.
            let cases = [ "passive-observation-authenticated-owner-required", true
                          "passive-observation-owner-mismatch", true
                          "passive-observation-kind-is-immutable", true
                          "passive-observation-projection-unavailable", false
                          "inert-storage-fault", false ]
            for index, (code, semantic) in List.indexed cases do
                let batchId = "application-boundary-" + string index
                let replayPayload = System.Text.Json.Nodes.JsonNode.Parse batches[1]
                replayPayload["ingestId"] <- System.Text.Json.Nodes.JsonValue.Create batchId
                TelemetryStoreApplication.submitReceipt store approved scope (envelope scope batchId (Encoding.UTF8.GetBytes(replayPayload.ToJsonString()))) |> unwrap |> ignore
                let retainedFile = Assert.Single(Directory.GetFiles(Path.Combine(store, "receipt-inbox"), "*.ready"))
                let retainedBytes = File.ReadAllBytes retainedFile
                let mutable hookCalls = 0
                let hook (stage: string) =
                    if stage = "before-application-commit" then
                        hookCalls <- hookCalls + 1
                        raise (InvalidOperationException code)
                let outcome = TelemetryStoreApplication.drainReceiptsWithHook store approved scope.Workspace hook
                Assert.Equal(1, hookCalls)
                if semantic then
                    Assert.Contains("\"rejected\":1", outcome |> unwrap)
                    Assert.Contains("\"status\":\"rejected\"", TelemetryStoreApplication.lookupReceipt store approved scope batchId |> unwrap)
                    Assert.False(File.Exists retainedFile)
                else
                    expectError [ "storage-unavailable" ] outcome
                    Assert.Contains("\"status\":\"durably-received\"", TelemetryStoreApplication.lookupReceipt store approved scope batchId |> unwrap)
                    Assert.Equal<byte>(retainedBytes, File.ReadAllBytes retainedFile)
                    TelemetryStoreApplication.drainReceipts store approved scope.Workspace |> unwrap |> ignore
                let unchanged = System.Text.Json.Nodes.JsonNode.Parse(TelemetryStoreApplication.passiveObservations store approved scope |> unwrap)
                Assert.Equal(snapshot["observations"].ToJsonString(), unchanged["observations"].ToJsonString())
            let collector: TelemetryReceipt.Principal =
                { Scope = { scope with Producer = "native-collector"; Stream = "inventory" }
                  Role = TelemetryReceipt.NativeCollector; GrantId = Some "native-grant"; GrantGeneration = Some 1L }
            TelemetryStoreApplication.enrollReceiptPrincipal store approved collector |> unwrap |> ignore
            TelemetryStoreApplication.submitReceiptPrincipal store approved collector (envelope collector.Scope "collector-passive" batches[0]) |> unwrap |> ignore
            TelemetryStoreApplication.drainReceipts store approved scope.Workspace |> unwrap |> ignore
            Assert.Contains("\"status\":\"rejected\"", TelemetryStoreApplication.lookupReceipt store approved collector.Scope "collector-passive" |> unwrap)
            let final = System.Text.Json.Nodes.JsonNode.Parse(TelemetryStoreApplication.passiveObservations store approved scope |> unwrap)
            Assert.Equal(snapshot["observations"].ToJsonString(), final["observations"].ToJsonString())
            // The existing scoped-store guard precedes passive fact admission. Neither route
            // may bypass authority: verify its exact public result, then the unscoped inner guard.
            expectError [ "scoped-store-requires-receipt-ingestion" ] (TelemetryStoreApplication.ingest store approved batches[1])
            let fresh = System.Text.Json.Nodes.JsonNode.Parse batches[1]
            fresh["ingestId"] <- System.Text.Json.Nodes.JsonValue.Create "fresh-unauthenticated-passive-attempt"
            let freshBytes = Encoding.UTF8.GetBytes(fresh.ToJsonString())
            expectError [ "scoped-store-requires-receipt-ingestion" ] (TelemetryStoreApplication.ingest store approved freshBytes)
            let afterDirectRefusals = System.Text.Json.Nodes.JsonNode.Parse(TelemetryStoreApplication.passiveObservations store approved scope |> unwrap)
            Assert.Equal(snapshot["observations"].ToJsonString(), afterDirectRefusals["observations"].ToJsonString())
            let unscoped = Path.Combine(root, "unscoped-store")
            TelemetryStoreApplication.initialize unscoped approved |> unwrap |> ignore
            expectError [ "passive-observation-authenticated-owner-required" ] (TelemetryStoreApplication.ingest unscoped approved freshBytes)
            use unscopedConnection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(unscoped, TelemetryStoreApplication.databaseFileName)};Mode=ReadOnly;Pooling=False")
            unscopedConnection.Open()
            use unscopedFacts = unscopedConnection.CreateCommand()
            unscopedFacts.CommandText <- "SELECT count(*) FROM ingest_facts;"
            Assert.Equal(0L, Convert.ToInt64(unscopedFacts.ExecuteScalar()))

    [<Fact>]
    let ``passive source keeps absent dimensions unknown and accounts for nonusage scan budgets`` () =
        if OperatingSystem.IsLinux() && Runtime.InteropServices.RuntimeInformation.ProcessArchitecture = Runtime.InteropServices.Architecture.X64 then
            let cleanup, root = temp ()
            use cleanup = cleanup
            let home, path = passiveSource root "partial-counters"
            let row = System.Text.Json.Nodes.JsonNode.Parse(nativeRow "partial-response" 10L 0L)
            row["timestamp"] <- null
            let usage = (row["payload"]["usage"]).AsObject()
            for name in [ "input_tokens"; "cached_input_tokens"; "cache_write_input_tokens"; "reasoning_output_tokens"; "total_tokens" ] do
                usage.Remove name |> ignore
            File.WriteAllText(path, String.replicate 100000 "{\"type\":\"event_msg\"}\n" + row.ToJsonString() + "\n")
            let scan cursor = SkillTelemetryReaders.NativeUsage.scanPassive home path cursor 200000 64 |> Result.defaultWith (fun error -> failwith error.Message)
            let first = scan None
            Assert.Empty first.Records
            Assert.Contains("scan-pass-budget", first.Diagnostics)
            Assert.True(first.ScannedBytes <= 200000)
            let mutable current = first
            for _ in 1..20 do
                if current.Records.IsEmpty then current <- scan (Some current.Cursor)
            let record = Assert.Single current.Records
            Assert.Equal(None, record.Usage.Input)
            Assert.Equal(None, record.Usage.CachedInput)
            Assert.Equal(None, record.Usage.CacheWriteInput)
            Assert.Equal(None, record.Usage.Reasoning)
            Assert.Equal(None, record.Usage.Total)
            Assert.Equal(Some 72L, record.Usage.Output)
            Assert.Equal(None, record.OccurredAt)
            Assert.Equal(None, record.Provider)

    [<Fact>]
    let ``passive every intent enqueue and cursor persistence cut preserves exact recovery`` () =
        if OperatingSystem.IsLinux() && Runtime.InteropServices.RuntimeInformation.ProcessArchitecture = Runtime.InteropServices.Architecture.X64 then
            for cut in [ "before-intent-write"; "after-intent-write"; "before-local-enqueue"; "after-local-enqueue"; "before-cursor-write"; "after-cursor-write" ] do
                let cleanup, root = temp ()
                use cleanup = cleanup
                let home, path = passiveSource root "cut"
                File.WriteAllText(path, nativeRow "response-a" 10L 0L)
                let enqueued = ResizeArray<byte array>()
                let enqueue (bytes: byte array) = enqueued.Add(Array.copy bytes); Ok "locally-retained"
                let hook (stage: string) = if stage = cut then raise (IOException "inert crash cut")
                let interrupted = SkillTelemetryAdapter.capturePassiveWithHook root "owner" "observations" home [path] enqueue hook
                Assert.Equal(1, interrupted.ExitCode)
                let statePath = Path.Combine(root, "orchestrator-passive", "producer.json")
                let intent =
                    if not (File.Exists statePath) then None
                    else
                        let state = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllBytes statePath)
                        if isNull state["pending"] then None else Some(Convert.FromBase64String((state["pending"]["batchBase64"]).GetValue<string>()))
                let priorCalls = enqueued.Count
                let recovered = SkillTelemetryAdapter.capturePassive root "owner" "observations" home [path] enqueue |> parseCapture
                Assert.Single(recovered["observations"].AsArray()) |> ignore
                Assert.False(recovered["pendingIntent"].GetValue<bool>())
                match intent with
                | Some exact ->
                    Assert.Equal(priorCalls + 1, enqueued.Count)
                    Assert.Equal<byte>(exact, enqueued[enqueued.Count - 1])
                    if priorCalls > 0 then Assert.Equal<byte>(exact, enqueued[0])
                | None when cut = "after-cursor-write" -> Assert.Equal(priorCalls, enqueued.Count)
                | None -> Assert.Equal(1, enqueued.Count)

    [<Fact>]
    let ``passive status-slot failure claims no durable gap and retains original intent cursor`` () =
        if OperatingSystem.IsLinux() && Runtime.InteropServices.RuntimeInformation.ProcessArchitecture = Runtime.InteropServices.Architecture.X64 then
            let cleanup, root = temp ()
            use cleanup = cleanup
            let home, path = passiveSource root "status-failure"
            File.WriteAllText(path, nativeRow "response-a" 10L 0L)
            let statePath = Path.Combine(root, "orchestrator-passive", "producer.json")
            let mutable before = Array.empty<byte>
            let enqueue _ = before <- File.ReadAllBytes statePath; Error [ "spool-overload" ]
            let hook (stage: string) = if stage = "before-status-write" then raise (IOException "inert status slot failure")
            let failure = SkillTelemetryAdapter.capturePassiveWithHook root "owner" "observations" home [path] enqueue hook
            Assert.Equal(1, failure.ExitCode)
            Assert.Equal<byte>(before, File.ReadAllBytes statePath)
            let report = SkillTelemetryAdapter.reportPassive root "owner" "observations" |> parseCapture
            Assert.True(report["pendingIntent"].GetValue<bool>())
            Assert.DoesNotContain("enqueue-deferred-cursor-retained", report["gaps"].ToJsonString())
            let recovered = SkillTelemetryAdapter.capturePassive root "owner" "observations" home [path] (fun _ -> Ok "locally-retained") |> parseCapture
            Assert.Single(recovered["observations"].AsArray()) |> ignore

    [<Fact>]
    let ``shared passive decoder refuses duplicate counters identities and invalid UTF8`` () =
        if OperatingSystem.IsLinux() && Runtime.InteropServices.RuntimeInformation.ProcessArchitecture = Runtime.InteropServices.Architecture.X64 then
            let cleanup, root = temp ()
            use cleanup = cleanup
            let home, path = passiveSource root "duplicate-fields"
            let row = nativeRow "response-a" 10L 0L
            for invalid in [ row.Replace("\"input_tokens\":10", "\"input_tokens\":999,\"input_tokens\":10")
                             row.Replace("\"thread_id\":", "\"thread_id\":\"99999999-9999-9999-9999-999999999999\",\"thread_id\":") ] do
                File.WriteAllText(path, invalid)
                let scan = SkillTelemetryReaders.NativeUsage.scanPassive home path None 4194304 64 |> Result.defaultWith (fun error -> failwith error.Message)
                Assert.Empty scan.Records
                Assert.Contains("malformed-row-skipped", scan.Diagnostics)
            let invalidUtf8 = Encoding.UTF8.GetBytes row
            invalidUtf8[row.IndexOf("response-a", StringComparison.Ordinal) + 9] <- 0xFFuy
            File.WriteAllBytes(path, invalidUtf8)
            let scan = SkillTelemetryReaders.NativeUsage.scanPassive home path None 4194304 64 |> Result.defaultWith (fun error -> failwith error.Message)
            Assert.Empty scan.Records
            Assert.Contains("malformed-row-skipped", scan.Diagnostics)

    [<Fact>]
    let ``passive command capture and report execute offline through actual application route`` () =
        if OperatingSystem.IsLinux() && Runtime.InteropServices.RuntimeInformation.ProcessArchitecture = Runtime.InteropServices.Architecture.X64 then
            let cleanup, root = temp ()
            use cleanup = cleanup
            let config, spool = passiveRemoteConfig root
            let home, path = passiveSource root "command"
            File.WriteAllText(path, nativeRow "response-a" 10L 0L)
            let selection = Path.Combine(root, "selected.json")
            File.WriteAllText(selection, System.Text.Json.JsonSerializer.Serialize {| schema = "fsgg.telemetry.passive-selection/1"; codexHome = home; sources = [| path |] |})
            File.SetUnixFileMode(selection, enum<UnixFileMode> 0o600)
            let run (arguments: string list) =
                let priorOut, priorError = Console.Out, Console.Error
                use stdout = new StringWriter()
                use stderr = new StringWriter()
                try
                    Console.SetOut stdout
                    Console.SetError stderr
                    let code = TelemetryApplication.tryRun ([ "telemetry"; "passive" ] @ arguments) |> Option.defaultValue 99
                    code, stdout.ToString(), stderr.ToString()
                finally
                    Console.SetOut priorOut
                    Console.SetError priorError
            let args = [ "--config"; config; "--repository"; "FS-GG/.github" ]
            let code, output, error = run ([ "capture" ] @ args @ [ "--input"; selection ])
            Assert.True((code = 0), error)
            let captured = System.Text.Json.Nodes.JsonNode.Parse output
            Assert.Single(captured["observations"].AsArray()) |> ignore
            Assert.Equal("locally-retained", captured["capture"].GetValue<string>())
            Assert.Equal("unknown", captured["attribution"].GetValue<string>())
            Assert.Equal("unknown", captured["delivery"].GetValue<string>())
            Assert.DoesNotContain(root, output)
            Assert.Single(Directory.GetFiles(spool, "*.ready")) |> ignore
            let reportCode, reportOutput, reportError = run ([ "report" ] @ args)
            Assert.True((reportCode = 0), reportError)
            let report = System.Text.Json.Nodes.JsonNode.Parse reportOutput
            Assert.Equal(captured["observations"].ToJsonString(), report["observations"].ToJsonString())
            let invalidCode, _, _ = run ([ "capture" ] @ args @ [ "--input"; selection; "--item"; "invented" ])
            Assert.Equal(1, invalidCode)
            Assert.Single(Directory.GetFiles(spool, "*.ready")) |> ignore

    [<Fact>]
    let ``passive command selects an explicit Claude transcript through the actual application route`` () =
        if OperatingSystem.IsLinux() && Runtime.InteropServices.RuntimeInformation.ProcessArchitecture = Runtime.InteropServices.Architecture.X64 then
            let cleanup, root = temp ()
            use cleanup = cleanup
            let config, spool = passiveRemoteConfig root
            let home, path = claudeSource root "command"
            File.WriteAllText(path, claudePrompt claudePromptA + claudeResponse "msg_a" 1 "\"end_turn\"" 9L)
            let run (arguments: string list) =
                let priorOut, priorError = Console.Out, Console.Error
                use stdout = new StringWriter()
                use stderr = new StringWriter()
                try
                    Console.SetOut stdout
                    Console.SetError stderr
                    let code = TelemetryApplication.tryRun ([ "telemetry"; "passive" ] @ arguments) |> Option.defaultValue 99
                    code, stdout.ToString(), stderr.ToString()
                finally
                    Console.SetOut priorOut
                    Console.SetError priorError
            let select (name: string) (body: string) =
                let selection = Path.Combine(root, name)
                File.WriteAllText(selection, body)
                File.SetUnixFileMode(selection, enum<UnixFileMode> 0o600)
                selection
            let args = [ "--config"; config; "--repository"; "FS-GG/.github" ]
            // Exactly one selected root: both homes together are an unsupported shape and capture nothing.
            let ambiguous =
                select "ambiguous.json" (System.Text.Json.JsonSerializer.Serialize {| schema = "fsgg.telemetry.passive-selection/1"; codexHome = home; claudeHome = home; sources = [| path |] |})
            let refusedCode, _, _ = run ([ "capture" ] @ args @ [ "--input"; ambiguous ])
            Assert.Equal(1, refusedCode)
            Assert.True(not (Directory.Exists spool) || Array.isEmpty (Directory.GetFiles(spool, "*.ready")))
            let selection =
                select "selected.json" (System.Text.Json.JsonSerializer.Serialize {| schema = "fsgg.telemetry.passive-selection/1"; claudeHome = home; sources = [| path |] |})
            let code, output, error = run ([ "capture" ] @ args @ [ "--input"; selection ])
            Assert.True((code = 0), error)
            let captured = System.Text.Json.Nodes.JsonNode.Parse output
            let row = Assert.Single(captured["observations"].AsArray())
            Assert.Equal("claude-local-transcript/1", row["provenance"].GetValue<string>())
            Assert.Equal("unknown", captured["attribution"].GetValue<string>())
            Assert.DoesNotContain(root, output)
            Assert.Single(Directory.GetFiles(spool, "*.ready")) |> ignore

    [<Fact>]
    let ``passive blocked chains leave ordinary delivery fair across more than sixteen heads`` () =
        if OperatingSystem.IsLinux() && Runtime.InteropServices.RuntimeInformation.ProcessArchitecture = Runtime.InteropServices.Architecture.X64 then
            let cleanup, root = temp ()
            use cleanup = cleanup
            let config, spool = passiveRemoteConfig root
            let stateRoot, scope, binding = WorkspaceTelemetryApplication.passiveProducer (Some config) (Some "FS-GG/.github") |> Result.defaultWith (sprintf "%A" >> failwith)
            let home, path = passiveSource root "fair"
            File.WriteAllText(path, "")
            let enqueue = WorkspaceTelemetryApplication.tryEnqueueBound (Some config) (Some "FS-GG/.github") (Some scope.Producer) (Some binding)
            for index in 1..32 do
                File.AppendAllText(path, nativeRow "response-a" (int64 index) 0L)
                SkillTelemetryAdapter.capturePassive stateRoot "owner" scope.Stream home [path] enqueue |> parseCapture |> ignore
            let inventory = Directory.GetFiles(spool, "*.ready")
            let first = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllBytes inventory[0])
            let original = first["payload"].ToJsonString()
            let queue (name: string) (ordinary: bool) =
                let payload = System.Text.Json.Nodes.JsonNode.Parse original
                payload["ingestId"] <- System.Text.Json.Nodes.JsonValue.Create name
                payload["sourceIdentity"] <- System.Text.Json.Nodes.JsonValue.Create name
                payload["cursor"] <- System.Text.Json.Nodes.JsonValue.Create "1"
                if ordinary then
                    payload["eventCount"] <- System.Text.Json.Nodes.JsonValue.Create 1
                    payload["events"] <- System.Text.Json.Nodes.JsonNode.Parse "[{\"kind\":\"diagnostic\",\"identity\":\"ordinary-status\",\"revision\":1,\"code\":\"fixture\",\"severity\":\"info\"}]"
                enqueue (Encoding.UTF8.GetBytes(payload.ToJsonString())) |> Result.defaultWith (sprintf "%A" >> failwith) |> ignore
            for index in 1..20 do queue ("independent-passive-" + string index) false
            queue "ordinary-ready" true
            let visited = System.Collections.Generic.HashSet<string>()
            let mutable ordinaryApplied = false
            let mutable unexpectedSubmits = 0
            let mutable canceledEntries = 0
            let operations: WorkspaceTelemetryApplication.RemoteOperations =
                { Lookup = fun _ batchId digest _ ->
                    visited.Add batchId |> ignore
                    if batchId = "ordinary-ready" then ordinaryApplied <- true
                    System.Threading.Tasks.Task.FromResult(FS.GG.Telemetry.RemoteClient.Acknowledged
                        { Scope = scope; BatchId = batchId; Digest = digest; Status = (if batchId = "ordinary-ready" then "applied" else "durably-received"); Code = None })
                  Submit = fun _ _ _ ->
                    unexpectedSubmits <- unexpectedSubmits + 1
                    System.Threading.Tasks.Task.FromResult(FS.GG.Telemetry.RemoteClient.Unacknowledged "unexpected-submit") }
            // Corrupt advisory metadata resets safely; it is neither queued intent nor authority.
            let cursor = Path.Combine(spool, "drain-cursor")
            File.WriteAllText(cursor, "invalid")
            File.SetUnixFileMode(cursor, enum<UnixFileMode> 0o600)
            let originals = Directory.GetFiles(spool, "*.ready") |> Array.map (fun file -> file, File.ReadAllBytes file) |> Map.ofArray
            for _ in 1..8 do
                WorkspaceTelemetryApplication.tryDrainWithOperationsForTesting (Some config) (Some "FS-GG/.github") operations |> ignore
            Assert.Equal(0, unexpectedSubmits)
            Assert.True ordinaryApplied
            Assert.Equal(22, visited.Count)
            Assert.Equal(52, Directory.GetFiles(spool, "*.ready").Length)
            for file in Directory.GetFiles(spool, "*.ready") do Assert.Equal<byte>(originals[file], File.ReadAllBytes file)
            // Metadata reset/crash can alter visit order only; retained correction successors stay held.
            File.Delete cursor
            WorkspaceTelemetryApplication.tryDrainWithOperationsForTesting (Some config) (Some "FS-GG/.github") operations |> ignore
            Assert.Equal(22, visited.Count)

            // Expire every drain immediately after its first usable lookup. Unattempted selected
            // tails must resume next time rather than being skipped by a last-selected cursor.
            queue "ordinary-after-cancellation" true
            let serviced = System.Collections.Generic.HashSet<string>()
            let mutable ordinaryAfterCancellation = false
            for _ in 1..23 do
                use cutoff = new System.Threading.CancellationTokenSource()
                let slow: WorkspaceTelemetryApplication.RemoteOperations =
                    { Lookup = fun _ batchId digest token ->
                        if token.IsCancellationRequested then canceledEntries <- canceledEntries + 1
                        serviced.Add batchId |> ignore
                        if batchId = "ordinary-after-cancellation" then ordinaryAfterCancellation <- true
                        cutoff.Cancel()
                        System.Threading.Tasks.Task.FromResult(FS.GG.Telemetry.RemoteClient.Acknowledged
                            { Scope = scope; BatchId = batchId; Digest = digest; Status = (if batchId = "ordinary-after-cancellation" then "applied" else "durably-received"); Code = None })
                      Submit = fun _ _ _ ->
                        unexpectedSubmits <- unexpectedSubmits + 1
                        System.Threading.Tasks.Task.FromResult(FS.GG.Telemetry.RemoteClient.Unacknowledged "unexpected-submit") }
                WorkspaceTelemetryApplication.tryDrainWithDeadlineForTesting (Some config) (Some "FS-GG/.github") slow cutoff.Token |> ignore
            Assert.Equal(0, unexpectedSubmits)
            Assert.Equal(0, canceledEntries)
            Assert.True ordinaryAfterCancellation
            Assert.Equal(22, serviced.Count)
            Assert.Equal(52, Directory.GetFiles(spool, "*.ready").Length)
            for file in Directory.GetFiles(spool, "*.ready") do Assert.Equal<byte>(originals[file], File.ReadAllBytes file)

            use submitCutoff = new System.Threading.CancellationTokenSource()
            let mutable canceledLookupCalls = 0
            let canceledLookup: WorkspaceTelemetryApplication.RemoteOperations =
                { Lookup = fun _ _ _ token ->
                    canceledLookupCalls <- canceledLookupCalls + 1
                    if token.IsCancellationRequested then canceledEntries <- canceledEntries + 1
                    submitCutoff.Cancel()
                    System.Threading.Tasks.Task.FromResult(FS.GG.Telemetry.RemoteClient.Unacknowledged "receipt-unavailable")
                  Submit = fun _ _ _ ->
                    unexpectedSubmits <- unexpectedSubmits + 1
                    System.Threading.Tasks.Task.FromResult(FS.GG.Telemetry.RemoteClient.Unacknowledged "unexpected-submit") }
            let canceledResult = WorkspaceTelemetryApplication.tryDrainWithDeadlineForTesting (Some config) (Some "FS-GG/.github") canceledLookup submitCutoff.Token
            Assert.Equal(Error [ "unacknowledged-lossy" ], canceledResult)
            Assert.Equal(1, canceledLookupCalls)
            Assert.Equal(0, canceledEntries)
            Assert.Equal(0, unexpectedSubmits)
            Assert.Equal(52, Directory.GetFiles(spool, "*.ready").Length)

    // Retirement of finished passive state. Synthetic transcripts only; every response is written as two identical
    // per-block records, as the Claude main-thread writer does.
    let private retirementStamp (at: DateTimeOffset) = at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", Globalization.CultureInfo.InvariantCulture)

    let private retirementPrompt (session: string) (prompt: string) (at: DateTimeOffset) =
        $"""{{"parentUuid":null,"isSidechain":false,"promptId":"{prompt}","type":"user","message":{{"role":"user","content":"synthetic"}},"uuid":"{Guid.NewGuid()}","timestamp":"{retirementStamp at}","cwd":"/work","sessionId":"{session}","version":"2.1.309"}}""" + "\n"

    let private retirementResponse (session: string) (messageId: string) (at: DateTimeOffset) =
        let record =
            $"""{{"parentUuid":null,"isSidechain":false,"message":{{"model":"claude-opus-5-5","id":"{messageId}","type":"message","role":"assistant","content":[{{"type":"text","text":"synthetic"}}],"stop_reason":"tool_use","usage":{{"input_tokens":3,"cache_creation_input_tokens":20,"cache_read_input_tokens":400,"output_tokens":11}}}},"requestId":"req_{messageId}","type":"assistant","uuid":"{Guid.NewGuid()}","timestamp":"{retirementStamp at}","cwd":"/work","sessionId":"{session}","version":"2.1.309"}}""" + "\n"
        record + record

    /// One prompt followed by `count` completed responses; returns the text and the response ids.
    let private retirementTurn (session: string) (prefix: string) (count: int) (at: DateTimeOffset) =
        let ids = [ for index in 1..count -> $"{prefix}-{index:D4}" ]
        let text = retirementPrompt session (Guid.NewGuid().ToString()) at + (ids |> List.mapi (fun index id -> retirementResponse session id (at.AddSeconds(float (index + 1)))) |> String.concat "")
        text, ids

    /// Marks a file as last written shortly after its final record, as an idle transcript would be.
    let private age (path: string) (lastRecord: DateTimeOffset) = File.SetLastWriteTimeUtc(path, lastRecord.AddMinutes(1.0).UtcDateTime)

    type private RetirementLedger() =
        let seen = Collections.Generic.Dictionary<string, int64>(StringComparer.Ordinal)
        member val Duplicates = 0 with get, set
        member val Batches = Collections.Generic.HashSet<string>(StringComparer.Ordinal)
        member this.Enqueue(bytes: byte array) =
            let batch = TelemetryStore.parseBatch bytes |> Result.defaultWith (sprintf "%A" >> failwith)
            // Replaying an identical retained intent after a crash is the spool's exact-bytes idempotence, not a re-emission.
            let digest = Convert.ToHexString(Security.Cryptography.SHA256.HashData bytes)
            if this.Batches.Add digest then
                for fact in batch.Facts |> List.filter (fun value -> value.Kind = "provider-usage-observation/1") do
                    // A retired identity re-emitted at a revision the receiver already holds would be refused as a conflict.
                    match seen.TryGetValue fact.Identity with
                    | true, revision when fact.Revision <= revision -> this.Duplicates <- this.Duplicates + 1
                    | _ -> seen[fact.Identity] <- fact.Revision
            Ok "locally-retained"
        member _.Count = seen.Count

    let private retirementCapture root home (ledger: RetirementLedger) (paths: string list) =
        SkillTelemetryAdapter.capturePassiveFrom root "owner" "passive-stream" (SkillTelemetryReaders.ClaudeHome home) paths ledger.Enqueue |> parseCapture

    let private passiveStateFile root =
        System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllBytes(Path.Combine(root, "orchestrator-passive", "producer.json")))

    [<Fact>]
    let ``passive retirement lets one active Claude session exceed the response index without re-emission`` () =
        if OperatingSystem.IsLinux() && Runtime.InteropServices.RuntimeInformation.ProcessArchitecture = Runtime.InteropServices.Architecture.X64 then
            let cleanup, root = temp ()
            use cleanup = cleanup
            let home, path = claudeSource root "long-session"
            let session = Guid.NewGuid().ToString()
            let start = DateTimeOffset.UtcNow.AddDays -1.0
            let first, _ = retirementTurn session "first" 300 start
            File.WriteAllText(path, first)
            let ledger = RetirementLedger()
            let mutable last = Unchecked.defaultof<System.Text.Json.Nodes.JsonNode>
            for _ in 1..14 do last <- retirementCapture root home ledger [ path ]
            // A second prompt arrives while the session stays active (fresh modification time).
            let second, _ = retirementTurn session "second" 300 (start.AddHours 1.0)
            File.AppendAllText(path, second)
            for _ in 1..14 do last <- retirementCapture root home ledger [ path ]
            Assert.Equal(600, ledger.Count)
            Assert.Equal(0, ledger.Duplicates)
            Assert.DoesNotContain("response-index-full-capture-deferred", last["gaps"].ToJsonString())
            // Control: the active source is still open, and only its newest responses stay indexed.
            let state = passiveStateFile root
            Assert.Equal(1, state["sources"].AsObject().Count)
            Assert.Equal(24, state["responses"].AsObject().Count)
            Assert.Equal(576L, last["retiredResponses"].GetValue<int64>())
            Assert.Equal(0, last["closedSources"].GetValue<int>())

    [<Fact>]
    let ``passive retirement closes finished files beyond eight resumes appends and never re-reads a rewrite`` () =
        if OperatingSystem.IsLinux() && Runtime.InteropServices.RuntimeInformation.ProcessArchitecture = Runtime.InteropServices.Architecture.X64 then
            let cleanup, root = temp ()
            use cleanup = cleanup
            let home, _ = claudeSource root "unused"
            let start = DateTimeOffset.UtcNow.AddDays -1.0
            let files =
                [ for index in 0..19 ->
                    let _, path = claudeSource root $"subagent-{index:D2}"
                    let session = Guid.NewGuid().ToString()
                    let at = start.AddMinutes(float index)
                    let text, _ = retirementTurn session $"file{index:D2}" 10 at
                    File.WriteAllText(path, text)
                    age path (at.AddSeconds 11.0)
                    session, path ]
            let paths = files |> List.map snd
            let ledger = RetirementLedger()
            let mutable last = Unchecked.defaultof<System.Text.Json.Nodes.JsonNode>
            for pass in 0..29 do
                let group = [ for offset in 0..7 -> paths[(pass * 8 + offset) % paths.Length] ] |> List.distinct
                last <- retirementCapture root home ledger group
            Assert.Equal(200, ledger.Count)
            Assert.Equal(0, ledger.Duplicates)
            Assert.DoesNotContain("source-index-full-capture-deferred", last["gaps"].ToJsonString())
            Assert.Equal(20, last["closedSources"].GetValue<int>())
            Assert.Equal(0, ((passiveStateFile root)["sources"]).AsObject().Count)
            Assert.Equal(0, ((passiveStateFile root)["responses"]).AsObject().Count)
            // A resumed session appends to its closed file: only the new responses are observed.
            let session0, path0 = files[0]
            let resumed, _ = retirementTurn session0 "resumed" 3 (start.AddHours 2.0)
            File.AppendAllText(path0, resumed)
            retirementCapture root home ledger [ path0 ] |> ignore
            Assert.Equal(203, ledger.Count)
            Assert.Equal(0, ledger.Duplicates)
            // A closed file rewritten in place is never re-read from its start.
            let _, path1 = files[1]
            let rewritten, _ = retirementTurn (fst files[1]) "rewritten" 4 (start.AddHours 3.0)
            File.WriteAllText(path1, rewritten)
            let skipped = retirementCapture root home ledger [ path1 ]
            Assert.Equal(203, ledger.Count)
            Assert.Contains("closed-source-discontinuity-skipped", skipped["gaps"].ToJsonString())

    [<Fact>]
    let ``passive retirement keeps a multi-day Claude host inside its finite profile`` () =
        if OperatingSystem.IsLinux() && Runtime.InteropServices.RuntimeInformation.ProcessArchitecture = Runtime.InteropServices.Architecture.X64 then
            let cleanup, root = temp ()
            use cleanup = cleanup
            let home, mainPath = claudeSource root "main-session"
            let mainSession = Guid.NewGuid().ToString()
            let ledger = RetirementLedger()
            let mutable expected = 0
            let mutable last = Unchecked.defaultof<System.Text.Json.Nodes.JsonNode>
            let dayOneFiles = ResizeArray<string>()
            let firstDay = DateTimeOffset.UtcNow.AddDays -4.0
            for day in 0..2 do
                let dayStart = firstDay.AddDays(float day)
                // The long main session grows each day and is active while it grows.
                let text, _ = retirementTurn mainSession $"main-day{day}" 120 dayStart
                File.AppendAllText(mainPath, text)
                expected <- expected + 120
                let dayFiles =
                    [ for index in 0..149 ->
                        let _, path = claudeSource root $"day{day}-agent-{index:D3}"
                        let at = dayStart.AddMinutes(float index)
                        let text, _ = retirementTurn (Guid.NewGuid().ToString()) $"d{day}a{index:D3}" 3 at
                        File.WriteAllText(path, text)
                        age path (at.AddSeconds 4.0)
                        path ]
                if day = 0 then dayOneFiles.AddRange dayFiles
                expected <- expected + 450
                // Each selection is captured until a pass observes nothing new, as a periodic caller would.
                let drain paths =
                    let mutable progressing = true
                    while progressing do
                        let before = ledger.Count
                        last <- retirementCapture root home ledger paths
                        progressing <- ledger.Count > before
                for group in dayFiles |> List.chunkBySize 7 do drain (mainPath :: group)
                drain [ mainPath ]
                // Overnight the main session goes quiet and closes on the next pass.
                age mainPath (dayStart.AddSeconds 121.0)
                last <- retirementCapture root home ledger [ mainPath ]
            Assert.Equal(expected, ledger.Count)
            Assert.Equal(0, ledger.Duplicates)
            let gaps = last["gaps"].ToJsonString()
            Assert.DoesNotContain("response-index-full-capture-deferred", gaps)
            Assert.DoesNotContain("source-index-full-capture-deferred", gaps)
            Assert.DoesNotContain("passive-state-overload", gaps)
            let statePath = Path.Combine(root, "orchestrator-passive", "producer.json")
            Assert.True(FileInfo(statePath).Length < 1024L * 1024L)
            let state = passiveStateFile root
            Assert.True(state["retired"].AsObject().Count <= 384)
            Assert.True(state["responses"].AsObject().Count <= 256)
            Assert.NotNull(state["retiredHorizon"])
            // An evicted day-one file selected again is suppressed below the horizon instead of being counted twice.
            let evicted = dayOneFiles |> Seq.find (fun path -> isNull state["retired"].[Convert.ToHexString(Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath path))).ToLowerInvariant()])
            let suppressed = retirementCapture root home ledger [ evicted ]
            Assert.Equal(expected, ledger.Count)
            Assert.Contains("pre-horizon-record-suppressed", suppressed["gaps"].ToJsonString())
            // Control: a genuinely new transcript after the horizon is still observed.
            let _, fresh = claudeSource root "after-horizon"
            let text, _ = retirementTurn (Guid.NewGuid().ToString()) "fresh" 5 (DateTimeOffset.UtcNow.AddMinutes 5.0)
            File.WriteAllText(fresh, text)
            retirementCapture root home ledger [ fresh ] |> ignore
            Assert.Equal(expected + 5, ledger.Count)
            Assert.Equal(0, ledger.Duplicates)

    [<Fact>]
    let ``passive retirement in an interrupted closing pass is applied exactly once on recovery`` () =
        if OperatingSystem.IsLinux() && Runtime.InteropServices.RuntimeInformation.ProcessArchitecture = Runtime.InteropServices.Architecture.X64 then
            for cut in [ "before-intent-write"; "after-intent-write"; "before-local-enqueue"; "after-local-enqueue"; "before-cursor-write"; "after-cursor-write" ] do
                let cleanup, root = temp ()
                use cleanup = cleanup
                let home, path = claudeSource root "closing"
                let at = DateTimeOffset.UtcNow.AddDays -1.0
                let text, _ = retirementTurn (Guid.NewGuid().ToString()) "closing" 5 at
                File.WriteAllText(path, text)
                age path (at.AddSeconds 6.0)
                let ledger = RetirementLedger()
                let hook (stage: string) = if stage = cut then raise (IOException "inert crash cut")
                let interrupted = SkillTelemetryAdapter.capturePassiveFromWithHook root "owner" "passive-stream" (SkillTelemetryReaders.ClaudeHome home) [ path ] ledger.Enqueue hook
                Assert.Equal(1, interrupted.ExitCode)
                let recovered = retirementCapture root home ledger [ path ]
                // The source is closed once, its five responses are retired once, and nothing is emitted twice.
                Assert.Equal(5, ledger.Count)
                Assert.Equal(0, ledger.Duplicates)
                Assert.Equal(1, recovered["closedSources"].GetValue<int>())
                Assert.Equal(5L, recovered["retiredResponses"].GetValue<int64>())
                Assert.False(recovered["pendingIntent"].GetValue<bool>())
                let again = retirementCapture root home ledger [ path ]
                Assert.Equal(5, ledger.Count)
                Assert.Equal(5L, again["retiredResponses"].GetValue<int64>())

    let private hardLink (target: string) (link: string) =
        use linker = Process.Start(ProcessStartInfo("ln", [| target; link |], UseShellExecute = false))
        linker.WaitForExit()
        Assert.Equal(0, linker.ExitCode)

    [<Fact>]
    let ``passive retirement never re-emits a response through an alias copy or rewrite`` () =
        if OperatingSystem.IsLinux() && Runtime.InteropServices.RuntimeInformation.ProcessArchitecture = Runtime.InteropServices.Architecture.X64 then
            let cleanup, root = temp ()
            use cleanup = cleanup
            let home, _ = claudeSource root "unused"
            let start = DateTimeOffset.UtcNow.AddDays -1.0
            let ledger = RetirementLedger()
            let write name count (at: DateTimeOffset) =
                let _, path = claudeSource root name
                let text, _ = retirementTurn (Guid.NewGuid().ToString()) name count at
                File.WriteAllText(path, text)
                path, text
            // A hard link to a quiescent file in the same selection, closing in the same pass, is skipped as an alias.
            let closing, _ = write "closing" 5 start
            age closing (start.AddSeconds 6.0)
            let closingLink = Path.Combine(Path.GetDirectoryName closing, "closing-link.jsonl")
            hardLink closing closingLink
            let aliased = retirementCapture root home ledger [ closing; closingLink ]
            Assert.Equal(5, ledger.Count)
            Assert.Contains("source-alias-skipped", aliased["gaps"].ToJsonString())
            retirementCapture root home ledger [ closingLink ] |> ignore
            Assert.Equal(0, ledger.Duplicates)
            // A rename of the closed file keeps its inode and is still the same source.
            let renamed = Path.Combine(Path.GetDirectoryName closing, "closing-renamed.jsonl")
            File.Move(closing, renamed)
            retirementCapture root home ledger [ renamed ] |> ignore
            Assert.Equal(5, ledger.Count)
            Assert.Equal(0, ledger.Duplicates)
            // A copy of the closed file under a new inode repeats only retired responses.
            let copied = Path.Combine(Path.GetDirectoryName closing, "closing-copy.jsonl")
            File.Copy(renamed, copied)
            let copy = retirementCapture root home ledger [ copied ]
            Assert.Equal(5, ledger.Count)
            Assert.Equal(0, ledger.Duplicates)
            Assert.Contains("retired-response-suppressed", copy["gaps"].ToJsonString())
            // An active windowed source replaced by a new inode with the same bytes is re-read from its start, and
            // a hard link to it is an alias; neither repeats a response that left its window.
            let active, activeText = write "active" 40 (start.AddHours 1.0)
            for _ in 1..3 do retirementCapture root home ledger [ active ] |> ignore
            Assert.Equal(45, ledger.Count)
            let activeLink = Path.Combine(Path.GetDirectoryName active, "active-link.jsonl")
            hardLink active activeLink
            retirementCapture root home ledger [ active; activeLink ] |> ignore
            File.Delete activeLink
            // Rename a fresh file over the original, which guarantees a new inode on every filesystem.
            let replacement = active + ".replacement"
            File.WriteAllText(replacement, activeText)
            File.Move(replacement, active, true)
            let replaced = retirementCapture root home ledger [ active ]
            Assert.Equal(45, ledger.Count)
            Assert.Equal(0, ledger.Duplicates)
            Assert.Contains("source-continuity-gap", replaced["gaps"].ToJsonString())
            // Control: a later response appended to the replaced file is still observed.
            let tail, _ = retirementTurn (Guid.NewGuid().ToString()) "tail" 1 (start.AddHours 2.0)
            File.AppendAllText(active, tail)
            retirementCapture root home ledger [ active ] |> ignore
            Assert.Equal(46, ledger.Count)
            Assert.Equal(0, ledger.Duplicates)
