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
        let root = Path.Combine(Path.GetTempPath(), "fsgg-l1-" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory root |> ignore
        if not (OperatingSystem.IsWindows()) then
            File.SetUnixFileMode(root, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute)
        { new IDisposable with
            member _.Dispose() = if Directory.Exists root then Directory.Delete(root, true) }, root

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
        Encoding.UTF8.GetBytes $"""{{"schema":"{TelemetryStore.BatchSchema}","ingestId":"bad id","sourceIdentity":"source","generation":"g1","cursor":"1","eventCount":1,"events":[{{"kind":"item","identity":"item-1","itemId":"{item}","revision":0}}]}}"""

    let private remoteArgs config spool producer =
        ["--config"; config; "--workspace"; "workspace-a"; "--producer"; producer; "--stream"; "runtime"
         "--repository"; "FS-GG/.github"; "--endpoint"; "https://127.0.0.1:1/"
         "--credential-reference"; "main"; "--spool-root"; spool]

    [<Fact>]
    let ``unconfigured status is read only`` () =
        let cleanup, root = temp ()
        use cleanup = cleanup
        let config = Path.Combine(root, "missing.json")
        let code, stdout, _ = invoke "status" ["--config"; config; "--repository"; "FS-GG/.github"]
        Assert.Equal(0, code)
        Assert.Contains("unconfigured", stdout)
        Assert.False(File.Exists(config + ".lock"))

    [<Fact>]
    let ``remote association spools one identity and refuses changed content`` () =
        if OperatingSystem.IsLinux() && Runtime.InteropServices.RuntimeInformation.ProcessArchitecture=Runtime.InteropServices.Architecture.X64 then
            let cleanup, root = temp ()
            use cleanup = cleanup
            let config = Path.Combine(root, "telemetry.json")
            let spool = Path.Combine(root, "spool")
            Environment.SetEnvironmentVariable("FSGG_TELEMETRY_CREDENTIAL_MAIN", String('x', 32))
            try
                let code,_,error=invoke "activate-remote" ["--config";config;"--workspace";"workspace-a";"--producer";"producer-a";"--stream";"runtime";"--repository";"FS-GG/.github";"--endpoint";"https://127.0.0.1:1/";"--credential-reference";"main";"--spool-root";spool]
                Assert.True((code = 0), error)
                let first=WorkspaceTelemetryApplication.tryPublish (Some config) (Some "FS-GG/.github") (payload "item-a")
                Assert.True((first = Error ["unacknowledged-lossy"]), sprintf "unexpected first result: %A" first)
                Assert.Single(Directory.EnumerateFiles(spool,"*.ready")) |> ignore
                let second=WorkspaceTelemetryApplication.tryPublish (Some config) (Some "FS-GG/.github") (payload "item-b")
                Assert.True((second = Error ["identity-conflict"]), sprintf "unexpected second result: %A" second)
                Assert.Single(Directory.EnumerateFiles(spool,"*.ready")) |> ignore
                let code,status,_=invoke "status" ["--config";config;"--repository";"FS-GG/.github"]
                Assert.Equal(0,code);Assert.Contains("\"pending\":1",status);Assert.Contains("\"unacknowledgedLossy\":true",status)
            finally Environment.SetEnvironmentVariable("FSGG_TELEMETRY_CREDENTIAL_MAIN",null)

    [<Fact>]
    let ``closed config rejects duplicate properties and old host config`` () =
        let cleanup, root = temp ()
        use cleanup = cleanup
        for index, json in [0, "{\"schema\":\"fsgg.telemetry.workspace-config/1\",\"schema\":\"fsgg.telemetry.workspace-config/1\",\"engine\":\"fsgg-coord-engine\",\"associations\":[],\"retiredAssociations\":[]}"; 1, "{\"schema\":\"fsgg.telemetry.host-config/1\",\"storeRoot\":\"/private\",\"engine\":\"fsgg-coord-engine\"}"] do
            let path = Path.Combine(root, string index + ".json")
            File.WriteAllText(path, json)
            File.SetUnixFileMode(path, UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
            let code, _, _ = invoke "status" ["--config"; path; "--repository"; "FS-GG/.github"]
            Assert.NotEqual(0, code)

    [<Fact>]
    let ``default XDG association is selected and preserves repository spelling`` () =
        if OperatingSystem.IsLinux() && Runtime.InteropServices.RuntimeInformation.ProcessArchitecture=Runtime.InteropServices.Architecture.X64 then
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
                let code, _, error = invoke "activate-remote" (remoteArgs config spool "producer-a" |> List.tail |> List.tail)
                Assert.True((code = 0), error)
                match WorkspaceTelemetryApplication.tryBinding None None with
                | Some(path,repository,digest) -> Assert.Equal(config,path); Assert.Equal("FS-GG/.github",repository); Assert.Equal(64,digest.Length)
                | None -> failwith "default association was not selected"
            finally
                Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", oldXdg)
                Environment.SetEnvironmentVariable("FSGG_TELEMETRY_REPOSITORY", null)
                Environment.SetEnvironmentVariable("FSGG_TELEMETRY_CREDENTIAL_MAIN", null)

    [<Fact>]
    let ``cutover archives identity and refuses A to B to A reuse`` () =
        if OperatingSystem.IsLinux() && Runtime.InteropServices.RuntimeInformation.ProcessArchitecture=Runtime.InteropServices.Architecture.X64 then
            let cleanup, root = temp ()
            use cleanup = cleanup
            let config, spoolA, spoolB = Path.Combine(root,"telemetry.json"), Path.Combine(root,"spool-a"), Path.Combine(root,"spool-b")
            Environment.SetEnvironmentVariable("FSGG_TELEMETRY_CREDENTIAL_MAIN", String('x', 32))
            try
                Assert.Equal(0, let code,_,_ = invoke "activate-remote" (remoteArgs config spoolA "producer-a") in code)
                let common producer spool = ["--config";config;"--workspace";"workspace-a";"--producer";producer;"--stream";"runtime";"--to";"remote";"--endpoint";"https://127.0.0.1:1/";"--credential-reference";"main";"--spool-root";spool]
                Assert.Equal(0, let code,_,_ = invoke "cutover" (common "producer-b" spoolB) in code)
                Assert.Equal(Error ["association-stale"], WorkspaceTelemetryApplication.tryPublishExpected (Some config) (Some "FS-GG/.github") (Some "producer-a") (payload "item-a"))
                Assert.NotEqual(0, let code,_,_ = invoke "cutover" (common "producer-a" spoolA) in code)
            finally Environment.SetEnvironmentVariable("FSGG_TELEMETRY_CREDENTIAL_MAIN", null)

    [<Fact>]
    let ``remote spool capacity refuses before writing a new input`` () =
        if OperatingSystem.IsLinux() && Runtime.InteropServices.RuntimeInformation.ProcessArchitecture=Runtime.InteropServices.Architecture.X64 then
            let cleanup, root = temp ()
            use cleanup = cleanup
            let config, spool = Path.Combine(root,"telemetry.json"), Path.Combine(root,"spool")
            Environment.SetEnvironmentVariable("FSGG_TELEMETRY_CREDENTIAL_MAIN", String('x', 32))
            try
                Assert.Equal(0, let code,_,_ = invoke "activate-remote" (remoteArgs config spool "producer-a") in code)
                for index in 1..128 do
                    let file=Path.Combine(spool,$"occupied-{index}.ready")
                    File.WriteAllText(file,"x")
                    File.SetUnixFileMode(file,UnixFileMode.UserRead|||UnixFileMode.UserWrite)
                Assert.Equal(Error ["spool-overload"], WorkspaceTelemetryApplication.tryPublish (Some config) (Some "FS-GG/.github") (payload "item-a"))
                Assert.Equal(128, Directory.EnumerateFiles(spool,"*.ready") |> Seq.length)
            finally Environment.SetEnvironmentVariable("FSGG_TELEMETRY_CREDENTIAL_MAIN", null)

    [<Fact>]
    let ``production local activation refuses the unqualified overlay without residue`` () =
        if OperatingSystem.IsLinux() && Runtime.InteropServices.RuntimeInformation.ProcessArchitecture=Runtime.InteropServices.Architecture.X64 then
            let cleanup, root = temp ()
            use cleanup = cleanup
            let config, store = Path.Combine(root,"telemetry.json"), Path.Combine(root,"store")
            let code,_,_ = invoke "activate-local" ["--config";config;"--workspace";"workspace-a";"--producer";"producer-a";"--stream";"runtime";"--repository";"FS-GG/.github";"--store-root";store]
            Assert.NotEqual(0, code)
            Assert.False(File.Exists config)

    [<Fact>]
    let ``configuration lock excludes a second process and symlink config is refused`` () =
        if OperatingSystem.IsLinux() && Runtime.InteropServices.RuntimeInformation.ProcessArchitecture=Runtime.InteropServices.Architecture.X64 then
            let cleanup, root = temp ()
            use cleanup = cleanup
            let config, spool = Path.Combine(root,"telemetry.json"), Path.Combine(root,"spool")
            Environment.SetEnvironmentVariable("FSGG_TELEMETRY_CREDENTIAL_MAIN", String('x', 32))
            try
                Assert.Equal(0, let code,_,_ = invoke "activate-remote" (remoteArgs config spool "producer-a") in code)
                let start=ProcessStartInfo("flock")
                start.UseShellExecute<-false
                start.RedirectStandardOutput<-true
                for value in ["-n";config+".lock";"sh";"-c";"echo ready; sleep 5"] do start.ArgumentList.Add value
                use holder=Process.Start start
                Assert.Equal("ready", holder.StandardOutput.ReadLine())
                let code,_,error=invoke "associate-repository" ["--config";config;"--workspace";"workspace-a";"--repository";"FS-GG/second"]
                Assert.NotEqual(0,code)
                Assert.Contains("configuration-busy",error)
                holder.Kill(true)
                let link=Path.Combine(root,"linked.json")
                File.CreateSymbolicLink(link,config) |> ignore
                let linkedCode,_,_=invoke "status" ["--config";link;"--repository";"FS-GG/.github"]
                Assert.NotEqual(0,linkedCode)
                let ancestor=Path.Combine(root,"alias")
                Directory.CreateSymbolicLink(ancestor,root) |> ignore
                let ancestorCode,_,_=invoke "status" ["--config";Path.Combine(ancestor,"telemetry.json");"--repository";"FS-GG/.github"]
                Assert.NotEqual(0,ancestorCode)
            finally Environment.SetEnvironmentVariable("FSGG_TELEMETRY_CREDENTIAL_MAIN", null)

    [<Fact>]
    let ``activation refuses duplicate producers and repository case aliases before serialization`` () =
        if OperatingSystem.IsLinux() && Runtime.InteropServices.RuntimeInformation.ProcessArchitecture=Runtime.InteropServices.Architecture.X64 then
            let cleanup, root = temp ()
            use cleanup = cleanup
            let config = Path.Combine(root,"telemetry.json")
            Environment.SetEnvironmentVariable("FSGG_TELEMETRY_CREDENTIAL_MAIN", String('x', 32))
            try
                Assert.Equal(0, let code,_,_ = invoke "activate-remote" (remoteArgs config (Path.Combine(root,"spool-a")) "producer-a") in code)
                let duplicateProducer=["--config";config;"--workspace";"workspace-b";"--producer";"producer-a";"--stream";"runtime";"--repository";"FS-GG/other";"--endpoint";"https://127.0.0.1:1/";"--credential-reference";"main";"--spool-root";Path.Combine(root,"spool-b")]
                Assert.NotEqual(0, let code,_,_ = invoke "activate-remote" duplicateProducer in code)
                let aliases=["--config";config;"--workspace";"workspace-c";"--producer";"producer-c";"--stream";"runtime";"--repository";"FS-GG/Third";"--repository";"fs-gg/third";"--endpoint";"https://127.0.0.1:1/";"--credential-reference";"main";"--spool-root";Path.Combine(root,"spool-c")]
                Assert.NotEqual(0, let code,_,_ = invoke "activate-remote" aliases in code)
                Assert.Equal(0, let code,_,_ = invoke "status" ["--config";config;"--repository";"FS-GG/.github"] in code)
            finally Environment.SetEnvironmentVariable("FSGG_TELEMETRY_CREDENTIAL_MAIN", null)
