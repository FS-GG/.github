namespace FS.GG.Coord.Cli.Tests

open System
open System.IO
open System.Text.Json
open Xunit
open FS.GG.Coord.Cli

module TelemetryCiApplicationTests =
    let private fixture outcome delivery observedHead =
        let root = Path.Combine(Path.GetTempPath(), "fsgg-ci-workspace-" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory root |> ignore
        let assignment = Path.Combine(root, "assignment.json")
        let result = Path.Combine(root, "delivery.json")
        File.WriteAllText(assignment, """{"schema":"fsgg.telemetry.ci-assignment/1","featureId":"L1","itemId":"L1-CI","attemptId":"attempt-1","parentAttemptId":null,"producerStream":"routine-delivery"}""")
        if not (OperatingSystem.IsWindows()) then
            File.SetUnixFileMode(assignment, UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
        let observed = observedHead |> Option.map (sprintf "\"%s\"") |> Option.defaultValue "null"
        File.WriteAllText(result, $"""{{"schema":"fsgg.routine-delivery/v1","repo":"o/r","pr":7,"baseRef":"main","baseSha":"dddddddddddddddddddddddddddddddddddddddd","expectedHead":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","observedHead":{observed},"outcome":"{outcome}","codeDelivery":"{delivery}","mergeCommit":null,"outcomeAt":null,"observedAt":"2026-09-09T10:04:01Z"}}""")
        { new IDisposable with member _.Dispose() = Directory.Delete(root, true) }, assignment, result

    let private invoke publish drain localRoot args =
        let priorOut, priorError = Console.Out, Console.Error
        use stdout = new StringWriter()
        use stderr = new StringWriter()
        try
            Console.SetOut stdout
            Console.SetError stderr
            let code = TelemetryCiApplication.runWithWorkspaceForTesting (fun _ _ -> Ok "binding") publish drain localRoot "reconcile" args
            code, stdout.ToString(), stderr.ToString()
        finally
            Console.SetOut priorOut
            Console.SetError priorError

    [<Fact>]
    let ``delivered remote reconciliation publishes native outcome and remains admission pending`` () =
        let cleanup, assignment, delivery = fixture "delivered" "delivered" (Some(String.replicate 40 "a"))
        use cleanup = cleanup
        let published = ResizeArray<byte array>()
        let publish binding bytes =
            Assert.Equal("binding", binding)
            published.Add bytes
            Ok "durably-received"
        let code, stdout, error =
            invoke publish (fun _ -> Ok "drained") (fun _ -> Ok None)
                [ "--assignment"; assignment; "--delivery"; delivery; "--config"; "/private/config.json"; "--repository"; "o/r" ]
        Assert.True((code = 0), error)
        Assert.Single published |> ignore
        use batch = JsonDocument.Parse(published[0])
        let event = batch.RootElement.GetProperty("events")[0]
        Assert.Equal("native-item-outcome", event.GetProperty("kind").GetString())
        Assert.Equal("delivered", event.GetProperty("outcome").GetString())
        Assert.Contains("\"status\":\"pending\"", stdout)
        Assert.Contains("remote-admission-query-unavailable", stdout)

    [<Fact>]
    let ``remote admission still refuses a non-ready non-exact initial witness`` () =
        let cleanup, assignment, delivery = fixture "refused" "not-delivered" (Some(String.replicate 40 "c"))
        use cleanup = cleanup
        let mutable publications = 0
        let code, _, error =
            invoke (fun _ _ -> publications <- publications + 1; Ok "queued")
                (fun _ -> Ok "drained") (fun _ -> Ok None)
                [ "--assignment"; assignment; "--delivery"; delivery; "--config"; "/private/config.json"; "--repository"; "o/r" ]
        Assert.Equal(1, code)
        Assert.Equal(1, publications)
        Assert.Contains("first CI population admission requires ready", error)

    [<Fact>]
    let ``explicit missing environment config cannot fall back to legacy store`` () =
        let cleanup, assignment, delivery = fixture "delivered" "delivered" (Some(String.replicate 40 "a"))
        use cleanup = cleanup
        let priorConfig,priorStore=Environment.GetEnvironmentVariable("FSGG_TELEMETRY_CONFIG"),Environment.GetEnvironmentVariable("FSGG_TELEMETRY_STORE")
        Environment.SetEnvironmentVariable("FSGG_TELEMETRY_CONFIG","/missing/selected-workspace.json")
        Environment.SetEnvironmentVariable("FSGG_TELEMETRY_STORE","/otherwise-valid-legacy")
        let mutable resolved=0
        try
            let priorOut,priorError=Console.Out,Console.Error
            use stdout=new StringWriter()
            use stderr=new StringWriter()
            try
                Console.SetOut stdout
                Console.SetError stderr
                let code=TelemetryCiApplication.runWithWorkspaceForTesting (fun _ _ -> resolved<-resolved+1;Error ["unconfigured"]) (fun _ _->failwith "publish") (fun _->failwith "drain") (fun _->failwith "local") "reconcile" ["--assignment";assignment;"--delivery";delivery]
                Assert.Equal(1,code)
                Assert.Equal(1,resolved)
            finally Console.SetOut priorOut;Console.SetError priorError
        finally
            Environment.SetEnvironmentVariable("FSGG_TELEMETRY_CONFIG",priorConfig)
            Environment.SetEnvironmentVariable("FSGG_TELEMETRY_STORE",priorStore)
