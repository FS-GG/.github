namespace FS.GG.Coord.Cli.Tests

open System
open System.Diagnostics
open System.IO
open System.Text
open System.Text.Json
open Microsoft.Data.Sqlite
open Xunit
open FS.GG.Coord
open FS.GG.Coord.Cli

module TelemetryReceiptTests =
    let private approved = TelemetryStore.ApprovedLocalDurable
    let private scope : TelemetryReceipt.Scope = { Workspace = "workspace-a"; Producer = "producer-a"; Stream = "runtime" }
    let private unwrap = function Ok value -> value | Error errors -> failwithf "%A" errors
    let private envelope (who: TelemetryReceipt.Scope) batch revision =
        Encoding.UTF8.GetBytes $"""{{"schema":"fsgg.telemetry.envelope/1","workspaceId":"{who.Workspace}","producerId":"{who.Producer}","streamId":"{who.Stream}","batchId":"{batch}","payload":{{"schema":"{TelemetryStore.BatchSchema}","ingestId":"native-batch","sourceIdentity":"native-source","generation":"g1","cursor":"1","eventCount":1,"events":[{{"kind":"item","identity":"item-a","itemId":"item-a","revision":{revision}}}]}}}}"""
    let private withStore action =
        let root = Path.Combine(Path.GetTempPath(), "fsgg-receipt-test-" + Guid.NewGuid().ToString("N"))
        try
            TelemetryStoreApplication.initialize root approved |> unwrap |> ignore
            TelemetryStoreApplication.enrollReceiptProducer root approved scope |> unwrap |> ignore
            action root
        finally if Directory.Exists root then Directory.Delete(root,true)
    let private sql root statement =
        use connection = new SqliteConnection($"Data Source={Path.Combine(root,TelemetryStoreApplication.databaseFileName)};Pooling=False")
        connection.Open()
        use command = connection.CreateCommand()
        command.CommandText <- statement
        command.ExecuteNonQuery() |> ignore
    let private status result =
        use document = JsonDocument.Parse(unwrap result: string)
        document.RootElement.GetProperty("status").GetString()

    [<Fact>]
    let ``receipt canonical identity rejects ambiguity and ignores object ordering`` () =
        let bytes = envelope scope "batch-a" 0
        let first = TelemetryReceipt.parse bytes |> unwrap
        Assert.Equal("c7c5813d3c35ae17b6f955d43a4bfb238699c13f26467a2deb3d05eb50c198bc", first.Digest)
        let text = Encoding.UTF8.GetString bytes
        let reordered = text.Replace("\"workspaceId\":\"workspace-a\",\"producerId\":\"producer-a\"", "\"producerId\":\"producer-a\",\"workspaceId\":\"workspace-a\"")
        Assert.Equal(first.Digest,(TelemetryReceipt.parse(Encoding.UTF8.GetBytes reordered) |> unwrap).Digest)
        for invalid in [text.Replace("\"revision\":0", "\"revision\":0,\"revision\":1"); text.Replace("\"revision\":0", "\"revision\":-0"); text.Replace("\"revision\":0", "\"revision\":0.0"); text.Replace("\"batchId\":", "\"unknown\":true,\"batchId\":")] do
            Assert.True(TelemetryReceipt.parse(Encoding.UTF8.GetBytes invalid) |> Result.isError)
        Assert.Equal(Error ["unsupported-version"],TelemetryReceipt.parse(Encoding.UTF8.GetBytes(text.Replace("envelope/1","envelope/2"))))
        Assert.True(TelemetryReceipt.parse [|0xFFuy|] |> Result.isError)
        Assert.True(TelemetryReceipt.parse(Array.zeroCreate 73729) |> Result.isError)
        for invalid in ["a\n"; "a\r\n"; "../a"; ""; String('a',129)] do
            Assert.False(TelemetryReceipt.validId invalid)

    [<Fact>]
    let ``receipt acceptance replay conflict and applied expiry preserve identity`` () =
        withStore (fun root ->
            let bytes = envelope scope "batch-a" 0
            let submit = TelemetryStoreApplication.submitReceipt root approved scope
            Assert.Equal("durably-received",status(submit bytes))
            Assert.Equal("durably-received",status(submit bytes))
            Assert.Equal(Error ["identity-conflict"],submit(envelope scope "batch-a" 1))
            Assert.Equal(Error ["unauthorized-scope"],TelemetryStoreApplication.lookupReceipt root approved {scope with Workspace="other"} "batch-a")
            Assert.Equal(Error ["unauthorized-scope"],TelemetryStoreApplication.submitReceipt root approved {scope with Workspace="other"} bytes)
            TelemetryStoreApplication.drainReceipts root approved scope.Workspace |> unwrap |> ignore
            Assert.Equal("applied",status(submit bytes))
            Assert.Empty(Directory.GetFiles(Path.Combine(root,"receipt-inbox"),"*.ready"))
            sql root "UPDATE transport_receipts SET terminal_utc='2000-01-01T00:00:00Z';"
            Assert.Equal("expired",status(submit bytes))
            Assert.Equal(Error ["identity-conflict"],submit(envelope scope "batch-a" 1))
            // Re-enrollment/credential replacement preserves logical identity and all receipts.
            TelemetryStoreApplication.enrollReceiptProducer root approved scope |> unwrap |> ignore
            Assert.Equal("expired",status(TelemetryStoreApplication.lookupReceipt root approved scope "batch-a")))

    [<Fact>]
    let ``receipt lookup refuses an incompatible schema even for an applied batch`` () =
        withStore (fun root ->
            TelemetryStoreApplication.submitReceipt root approved scope (envelope scope "batch-a" 0) |> unwrap |> ignore
            TelemetryStoreApplication.drainReceipts root approved scope.Workspace |> unwrap |> ignore
            sql root "PRAGMA user_version=10;"
            Assert.Equal(Error ["unsupported-version"],TelemetryStoreApplication.lookupReceipt root approved scope "batch-a"))

    [<Theory>]
    [<InlineData("before-file-sync")>]
    [<InlineData("after-file-sync")>]
    [<InlineData("after-rename")>]
    [<InlineData("after-directory-sync")>]
    [<InlineData("index-committed")>]
    let ``receipt interrupted admission resumes with the same identity`` stage =
        withStore (fun root ->
            let bytes = envelope scope "batch-a" 0
            let hook current = if current = stage then raise (IOException "injected storage fault")
            Assert.True(TelemetryStoreApplication.submitReceiptWithHook root approved scope bytes hook |> Result.isError)
            Assert.Equal("durably-received",status(TelemetryStoreApplication.submitReceipt root approved scope bytes))
            TelemetryStoreApplication.drainReceipts root approved scope.Workspace |> unwrap |> ignore
            Assert.Equal("applied",status(TelemetryStoreApplication.lookupReceipt root approved scope "batch-a")))

    [<Theory>]
    [<InlineData("before-application-commit", "durably-received")>]
    [<InlineData("after-application-commit", "applied")>]
    [<InlineData("after-cleanup", "applied")>]
    let ``receipt application and terminal outcome survive interrupted cleanup`` stage expected =
        withStore (fun root ->
            let bytes = envelope scope "batch-a" 0
            TelemetryStoreApplication.submitReceipt root approved scope bytes |> unwrap |> ignore
            let hook current = if current = stage then raise (IOException "injected storage fault")
            Assert.True(TelemetryStoreApplication.drainReceiptsWithHook root approved scope.Workspace hook |> Result.isError)
            Assert.Equal(expected,status(TelemetryStoreApplication.lookupReceipt root approved scope "batch-a"))
            TelemetryStoreApplication.drainReceipts root approved scope.Workspace |> unwrap |> ignore
            Assert.Equal("applied",status(TelemetryStoreApplication.lookupReceipt root approved scope "batch-a")))

    [<Fact>]
    let ``receipt exhaustion admits retries without another obligation and fair drain serves peers`` () =
        withStore (fun root ->
            for n in 1..128 do
                TelemetryStoreApplication.submitReceipt root approved scope (envelope scope (string n) 0) |> unwrap |> ignore
            Assert.Equal("durably-received",status(TelemetryStoreApplication.submitReceipt root approved scope (envelope scope "1" 0)))
            Assert.Equal(Error ["overload"],TelemetryStoreApplication.submitReceipt root approved scope (envelope scope "129" 0))
            let peer = {scope with Producer="producer-b"}
            TelemetryStoreApplication.enrollReceiptProducer root approved peer |> unwrap |> ignore
            TelemetryStoreApplication.submitReceipt root approved peer (envelope peer "peer" 0) |> unwrap |> ignore
            TelemetryStoreApplication.drainReceipts root approved scope.Workspace |> unwrap |> ignore
            Assert.Equal("applied",status(TelemetryStoreApplication.lookupReceipt root approved peer "peer")))

    [<Fact>]
    let ``receipt semantic conflict is terminal and missing accepted input refuses recovery`` () =
        withStore (fun root ->
            let initial = envelope scope "first" 0
            TelemetryStoreApplication.submitReceipt root approved scope initial |> unwrap |> ignore
            TelemetryStoreApplication.drainReceipts root approved scope.Workspace |> unwrap |> ignore
            let changed = Encoding.UTF8.GetString(envelope scope "second" 0) |> fun s -> s.Replace("\"itemId\":\"item-a\"", "\"itemId\":\"item-b\"") |> Encoding.UTF8.GetBytes
            TelemetryStoreApplication.submitReceipt root approved scope changed |> unwrap |> ignore
            TelemetryStoreApplication.drainReceipts root approved scope.Workspace |> unwrap |> ignore
            Assert.Equal("rejected",status(TelemetryStoreApplication.lookupReceipt root approved scope "second"))
            TelemetryStoreApplication.submitReceipt root approved scope (envelope scope "third" 1) |> unwrap |> ignore
            let file = Directory.GetFiles(Path.Combine(root,"receipt-inbox"),"*.ready") |> Array.exactlyOne
            File.Delete file
            Assert.Equal(Error ["storage-unavailable"],TelemetryStoreApplication.lookupReceipt root approved scope "third")
            Assert.Equal(Error ["storage-unavailable"],TelemetryStoreApplication.drainReceipts root approved scope.Workspace))

    [<Fact>]
    let ``receipt workspace association cannot move and producer keys are independent`` () =
        withStore (fun root ->
            Assert.Equal(Error ["unauthorized-scope"],TelemetryStoreApplication.enrollReceiptProducer root approved {scope with Workspace="other"})
            let peer = {scope with Producer="producer-b"}
            TelemetryStoreApplication.enrollReceiptProducer root approved peer |> unwrap |> ignore
            for who in [scope;peer] do
                TelemetryStoreApplication.submitReceipt root approved who (envelope who "same-batch" 0) |> unwrap |> ignore
            TelemetryStoreApplication.drainReceipts root approved scope.Workspace |> unwrap |> ignore
            for who in [scope;peer] do
                Assert.Equal("applied",status(TelemetryStoreApplication.lookupReceipt root approved who "same-batch")))

    [<Fact>]
    let ``receipt corrupt pending artifact cannot acknowledge a processing obligation`` () =
        withStore (fun root ->
            TelemetryStoreApplication.submitReceipt root approved scope (envelope scope "corrupt" 0) |> unwrap |> ignore
            let file = Directory.GetFiles(Path.Combine(root,"receipt-inbox"),"*.ready") |> Array.exactlyOne
            File.WriteAllText(file,"{}")
            Assert.Equal(Error ["storage-unavailable"],TelemetryStoreApplication.lookupReceipt root approved scope "corrupt")
            Assert.Equal(Error ["storage-unavailable"],TelemetryStoreApplication.submitReceipt root approved scope (envelope scope "next" 0)))

    [<Fact>]
    let ``receipt migration preserves legacy history without assigning it`` () =
        let root = Path.Combine(Path.GetTempPath(), "fsgg-receipt-legacy-" + Guid.NewGuid().ToString("N"))
        try
            TelemetryStoreApplication.initialize root approved |> unwrap |> ignore
            use document = JsonDocument.Parse(envelope scope "legacy" 0)
            TelemetryStoreApplication.ingest root approved (Encoding.UTF8.GetBytes(document.RootElement.GetProperty("payload").GetRawText())) |> unwrap |> ignore
            sql root "DROP INDEX transport_pending; DROP TABLE transport_receipts; DROP TABLE receipt_producers; DELETE FROM schema_migrations WHERE version=9; PRAGMA user_version=8;"
            TelemetryStoreApplication.initialize root approved |> unwrap |> ignore
            Assert.Equal(Error ["legacy-unassigned; select a new prospective store"],TelemetryStoreApplication.enrollReceiptProducer root approved scope)
            TelemetryStoreApplication.summary root approved "item-a" |> unwrap |> ignore
            sql root "UPDATE schema_migrations SET digest='corrupt' WHERE version=9;"
            Assert.True(TelemetryStoreApplication.initialize root approved |> Result.isError)
        finally if Directory.Exists root then Directory.Delete(root,true)

    [<Theory>]
    [<InlineData("before-file-sync", false)>]
    [<InlineData("after-file-sync", false)>]
    [<InlineData("after-rename", false)>]
    [<InlineData("after-directory-sync", false)>]
    [<InlineData("index-committed", false)>]
    [<InlineData("before-application-commit", true)>]
    [<InlineData("after-application-commit", true)>]
    [<InlineData("after-cleanup", true)>]
    let ``receipt real process exit recovers durable obligations`` stage draining =
        withStore (fun root ->
            let input = Path.Combine(root,"synthetic-input.json")
            let bytes = envelope scope "crash" 0
            File.WriteAllBytes(input,bytes)
            if draining then TelemetryStoreApplication.submitReceipt root approved scope bytes |> unwrap |> ignore
            let script = Path.Combine(root,"crash.fsx")
            let references =
                [ "SQLitePCLRaw.core.dll"; "SQLitePCLRaw.provider.e_sqlite3.dll"; "SQLitePCLRaw.batteries_v2.dll"; "Microsoft.Data.Sqlite.dll"; "FS.GG.Coord.Core.dll"; "fsgg-coord-engine.dll" ]
                |> List.map (fun name -> "#r @\"" + Path.Combine(AppContext.BaseDirectory,name) + "\"")
            let call = if draining then "TelemetryStoreApplication.drainReceiptsWithHook root TelemetryStore.ApprovedLocalDurable scope.Workspace hook" else "TelemetryStoreApplication.submitReceiptWithHook root TelemetryStore.ApprovedLocalDurable scope (File.ReadAllBytes input) hook"
            File.WriteAllLines(script, references @ [
                "open System"; "open System.IO"; "open FS.GG.Coord"; "open FS.GG.Coord.Cli"
                "let root = fsi.CommandLineArgs[1]"; "let input = fsi.CommandLineArgs[2]"; "let stage = fsi.CommandLineArgs[3]"
                "let scope : TelemetryReceipt.Scope = { Workspace=\"workspace-a\"; Producer=\"producer-a\"; Stream=\"runtime\" }"
                "let hook current = if current = stage then Environment.Exit(91)"
                call + " |> printfn \"%A\""; "Environment.Exit(92)" ])
            let start = ProcessStartInfo("dotnet")
            start.UseShellExecute <- false
            start.RedirectStandardOutput <- true
            start.RedirectStandardError <- true
            [ "fsi"; "--exec"; script; root; input; stage ] |> List.iter start.ArgumentList.Add
            use child = Process.Start start
            let stdout = child.StandardOutput.ReadToEndAsync()
            let stderr = child.StandardError.ReadToEndAsync()
            if not (child.WaitForExit(30000)) then child.Kill(true); failwith "crash fixture timed out"
            Assert.True(child.ExitCode = 91, stdout.Result + stderr.Result)
            TelemetryStoreApplication.submitReceipt root approved scope bytes |> unwrap |> ignore
            TelemetryStoreApplication.drainReceipts root approved scope.Workspace |> unwrap |> ignore
            Assert.Equal("applied",status(TelemetryStoreApplication.lookupReceipt root approved scope "crash")))
