namespace FS.GG.Coord.Cli.Tests

open System
open System.Diagnostics
open System.IO
open System.Text
open Xunit
open Microsoft.Data.Sqlite
open FS.GG.Coord
open FS.GG.Coord.Cli

module TelemetryStoreApplicationTests =
    let private approved = TelemetryStore.ApprovedLocalDurable
    let private unwrap = function Ok value -> value | Error errors -> failwithf "%A" errors
    let private batch ingest identity revision cursor input =
        Encoding.UTF8.GetBytes $"""{{"schema":"%s{TelemetryStore.BatchSchema}","ingestId":"%s{ingest}","sourceIdentity":"worker-a","generation":"g1","cursor":"%s{cursor}","eventCount":3,"events":[{{"kind":"item","identity":"UTEL-02","itemId":"UTEL-02","revision":0}},{{"kind":"usage","identity":"%s{identity}","itemId":"UTEL-02","revision":%d{revision},"provider":"OpenAI","model":"sol","effort":"medium","input":%d{input},"cachedInput":2,"cacheWriteInput":1,"output":5,"reasoning":2,"total":%d{input + 5L},"responses":1,"sessions":1,"turns":1}},{{"kind":"delivery","identity":"delivery-1","itemId":"UTEL-02","revision":0,"state":"delivered","expectedHead":null,"observedHead":null,"prNumber":3348}}]}}"""
    let private root () =
        let path = Path.Combine(Path.GetTempPath(), "fsgg-utel02-" + Guid.NewGuid().ToString("N"))
        { new IDisposable with member _.Dispose() = if Directory.Exists path then Directory.Delete(path, true) }, path
    let private pragma path value =
        use connection = new SqliteConnection($"Data Source=%s{Path.Combine(path, TelemetryStoreApplication.databaseFileName)};Pooling=False")
        connection.Open()
        use command = connection.CreateCommand()
        command.CommandText <- $"PRAGMA user_version=%d{value};"
        command.ExecuteNonQuery() |> ignore

    [<Fact>]
    let ``UTEL-02 init publish drain reopen replay and public summary are durable`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        let initialized = TelemetryStoreApplication.initialize path approved |> unwrap
        Assert.Contains("\"status\":\"ready\"", initialized)
        Assert.Matches("\"nativeEngine\":\"3\\.(5[1-9]|[6-9][0-9])", initialized)
        Assert.Contains("\"remaining\":0", TelemetryStoreApplication.drain path approved |> unwrap)
        let bytes = batch "batch-1" "usage-1" 0L "c1" 10L
        let published = TelemetryStoreApplication.publish path approved bytes |> unwrap
        Assert.Contains("\"status\":\"queued\"", published)
        Assert.Single(Directory.GetFiles(Path.Combine(path, "inbox", "worker-a"), "*.ready")) |> ignore
        let drained = TelemetryStoreApplication.drain path approved |> unwrap
        Assert.Contains("\"accepted\":3", drained)
        Assert.Empty(Directory.GetFiles(Path.Combine(path, "inbox", "worker-a"), "*.ready"))
        let first = TelemetryStoreApplication.summary path approved "UTEL-02" |> unwrap
        Assert.Contains("\"total\":15", first)
        Assert.Contains("\"populationCoverage\":\"unknown\"", first)
        Assert.Contains("\"qualification\":\"not-evaluated\"", first)
        let replay = batch "batch-2" "usage-1" 0L "c2" 10L
        TelemetryStoreApplication.ingest path approved replay |> unwrap |> ignore
        Assert.Equal(first, TelemetryStoreApplication.summary path approved "UTEL-02" |> unwrap)
        Assert.Contains("\"status\":\"ready\"", TelemetryStoreApplication.status path approved |> unwrap)

    [<Fact>]
    let ``UTEL-02 conflicting replay quarantines atomically and preserves accepted totals`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        TelemetryStoreApplication.ingest path approved (batch "batch-1" "usage-1" 0L "c1" 10L) |> unwrap |> ignore
        let before = TelemetryStoreApplication.summary path approved "UTEL-02" |> unwrap
        TelemetryStoreApplication.publish path approved (batch "batch-2" "usage-1" 0L "c2" 11L) |> unwrap |> ignore
        let result = TelemetryStoreApplication.drain path approved |> unwrap
        Assert.Contains("\"quarantined\":1", result)
        Assert.Equal(before, TelemetryStoreApplication.summary path approved "UTEL-02" |> unwrap)
        Assert.Single(Directory.GetFiles(Path.Combine(path, "quarantine"), "*.rejected", SearchOption.AllDirectories)) |> ignore

    [<Fact>]
    let ``UTEL-02 live writer is never displaced and leaves ready batch queued`` () =
        if OperatingSystem.IsLinux() then
            let cleanup, path = root ()
            use cleanup = cleanup
            TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
            TelemetryStoreApplication.publish path approved (batch "batch-1" "usage-1" 0L "c1" 10L) |> unwrap |> ignore
            let start = ProcessStartInfo("flock")
            start.ArgumentList.Add(Path.Combine(path, "writer.lock")); start.ArgumentList.Add("sleep"); start.ArgumentList.Add("2")
            start.UseShellExecute <- false
            use holder = Process.Start start
            System.Threading.Thread.Sleep 150
            match TelemetryStoreApplication.drain path approved with
            | Error [ "writer-busy" ] -> ()
            | result -> failwithf "unexpected lock result: %A" result
            Assert.Single(Directory.GetFiles(Path.Combine(path, "inbox", "worker-a"), "*.ready")) |> ignore
            Assert.Contains("\"total\":0", TelemetryStoreApplication.summary path approved "UTEL-02" |> unwrap)
            holder.WaitForExit()
            Assert.Contains("\"accepted\":3", TelemetryStoreApplication.drain path approved |> unwrap)

    [<Fact>]
    let ``UTEL-02 unconfigured status has no fallback creation`` () =
        let previous = Environment.GetEnvironmentVariable "FSGG_TELEMETRY_STORE"
        try
            Environment.SetEnvironmentVariable("FSGG_TELEMETRY_STORE", null)
            let code = TelemetryStoreApplication.run "status" []
            Assert.Equal(0, code)
        finally Environment.SetEnvironmentVariable("FSGG_TELEMETRY_STORE", previous)

    [<Fact>]
    let ``UTEL-02 crash boundaries retain ready replay and rollback before commit`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        let payload = batch "batch-1" "usage-1" 0L "c1" 10L
        TelemetryStoreApplication.publish path approved payload |> unwrap |> ignore
        let beforeCommit: TelemetryStoreApplication.DrainHooks =
            { BeforeCommit = (fun () -> raise (OperationCanceledException "synthetic precommit termination"))
              AfterCommitBeforeDelete = fun () -> () }
        Assert.Contains("precommit", sprintf "%A" (TelemetryStoreApplication.drainWithHooks path approved beforeCommit))
        Assert.Contains("\"total\":0", TelemetryStoreApplication.summary path approved "UTEL-02" |> unwrap)
        Assert.Single(Directory.GetFiles(Path.Combine(path, "inbox", "worker-a"), "*.ready")) |> ignore
        let afterCommit: TelemetryStoreApplication.DrainHooks =
            { BeforeCommit = fun () -> ()
              AfterCommitBeforeDelete = fun () -> raise (IOException "synthetic postcommit termination") }
        Assert.Contains("committed but ready removal failed", sprintf "%A" (TelemetryStoreApplication.drainWithHooks path approved afterCommit))
        let committed = TelemetryStoreApplication.summary path approved "UTEL-02" |> unwrap
        Assert.Contains("\"total\":15", committed)
        Assert.Single(Directory.GetFiles(Path.Combine(path, "inbox", "worker-a"), "*.ready")) |> ignore
        Assert.Contains("\"replayed\":3", TelemetryStoreApplication.drain path approved |> unwrap)
        Assert.Equal(committed, TelemetryStoreApplication.summary path approved "UTEL-02" |> unwrap)

    [<Fact>]
    let ``UTEL-02 malformed ready and temporary files recover without becoming coverage`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        let producer = Path.Combine(path, "inbox", "worker-a")
        Directory.CreateDirectory producer |> ignore
        File.WriteAllText(Path.Combine(producer, ".unfinished.tmp"), "partial")
        File.WriteAllText(Path.Combine(producer, "bad.deadbeef.ready"), "not-json")
        Assert.Contains("\"quarantined\":1", TelemetryStoreApplication.drain path approved |> unwrap)
        Assert.True(File.Exists(Path.Combine(producer, ".unfinished.tmp")))
        Assert.Contains("\"populationCoverage\":\"unknown\"", TelemetryStoreApplication.summary path approved "UTEL-02" |> unwrap)

    [<Fact>]
    let ``UTEL-02 newer schema leaves immutable inbox and public all-item export is bounded`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        let payload = batch "batch-1" "usage-1" 0L "c1" 10L
        TelemetryStoreApplication.publish path approved payload |> unwrap |> ignore
        pragma path 2
        Assert.Contains("newer than supported", sprintf "%A" (TelemetryStoreApplication.drain path approved))
        Assert.Single(Directory.GetFiles(Path.Combine(path, "inbox", "worker-a"), "*.ready")) |> ignore
        pragma path 1
        TelemetryStoreApplication.drain path approved |> unwrap |> ignore
        let output = Path.Combine(Path.GetTempPath(), "fsgg-utel02-public-" + Guid.NewGuid().ToString("N") + ".json")
        try
            TelemetryStoreApplication.exportPublic path approved None output |> unwrap |> ignore
            let exported = File.ReadAllText output
            Assert.Contains("fsgg.telemetry.public-export/1", exported)
            Assert.Contains("UTEL-02", exported)
            Assert.True(FileInfo(output).Length <= 65536L)
            Assert.Contains("private store root", sprintf "%A" (TelemetryStoreApplication.exportPublic path approved None (Path.Combine(path, "unsafe.json"))))
        finally if File.Exists output then File.Delete output

    [<Fact>]
    let ``UTEL-02 producer inbox rejects the 129th pending batch`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        for index in 1 .. 128 do
            TelemetryStoreApplication.publish path approved (batch ($"batch-%03d{index}") "usage-1" 0L ($"c%d{index}") 10L) |> unwrap |> ignore
        Assert.Contains("inbox is full", sprintf "%A" (TelemetryStoreApplication.publish path approved (batch "batch-129" "usage-1" 0L "c129" 10L)))

    [<Fact>]
    let ``UTEL-02 command shapes include publish drain and item-optional public export`` () =
        Assert.Equal(Some(Ok()), TelemetryApplication.validateInvocation [ "telemetry"; "store"; "publish"; "--input"; "batch.json" ])
        Assert.Equal(Some(Ok()), TelemetryApplication.validateInvocation [ "telemetry"; "store"; "drain" ])
        Assert.Equal(Some(Ok()), TelemetryApplication.validateInvocation [ "telemetry"; "store"; "export"; "--public"; "--output"; "public.json" ])

    [<Fact>]
    let ``UTEL-02 changed unsafe permissions refuse before worker publication`` () =
        if not (OperatingSystem.IsWindows()) then
            let cleanup, path = root ()
            use cleanup = cleanup
            TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
            File.SetUnixFileMode(path, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute ||| UnixFileMode.GroupWrite)
            try
                Assert.Contains("permissions", sprintf "%A" (TelemetryStoreApplication.publish path approved (batch "batch-1" "usage-1" 0L "c1" 10L)))
                Assert.False(Directory.Exists(Path.Combine(path, "inbox")))
            finally File.SetUnixFileMode(path, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute)
