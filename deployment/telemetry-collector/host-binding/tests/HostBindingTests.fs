namespace Fs.Gg.Telemetry.HostBinding.Tests

open System
open System.Diagnostics
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json
open Fs.Gg.Telemetry.HostBinding
open Xunit

module private Fixture =
    let repository = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "../../../.."))
    let nativeRoot = "deployment/telemetry-collector"

    let sha256 path =
        use stream = File.OpenRead(path)
        Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant()

    let run root args =
        let start = ProcessStartInfo("git")
        start.WorkingDirectory <- root
        start.UseShellExecute <- false
        start.RedirectStandardOutput <- true
        start.RedirectStandardError <- true
        for argument in args do start.ArgumentList.Add(argument)
        use child = Process.Start(start)
        let output = child.StandardOutput.ReadToEnd()
        let error = child.StandardError.ReadToEnd()
        child.WaitForExit()
        if child.ExitCode <> 0 then failwith error
        output.Trim()

    type Repository(?profileTransform: string -> string) =
        let owner = Path.Combine(Path.GetTempPath(), "host-binding-tests-" + Guid.NewGuid().ToString("N"))
        let root = Path.Combine(owner, "repository")
        let pins = Path.Combine(owner, "source-pins.json")
        do
            Directory.CreateDirectory(Path.Combine(root, nativeRoot)) |> ignore
            for name in HostBinding.SourceFiles do
                let source = Path.Combine(repository, nativeRoot, name)
                let target = Path.Combine(root, nativeRoot, name)
                let text = File.ReadAllText(source)
                let value = if name = "native-operation-v1.json" then defaultArg profileTransform id text else text
                File.WriteAllText(target, value, UTF8Encoding(false))
            let entries =
                HostBinding.SourceFiles
                |> List.map (fun name -> name, sha256 (Path.Combine(root, nativeRoot, name)))
                |> Map.ofList
            File.WriteAllText(pins, JsonSerializer.Serialize(entries), UTF8Encoding(false))
            run root [ "init"; "--quiet" ] |> ignore
            run root [ "config"; "user.email"; "test@example.invalid" ] |> ignore
            run root [ "config"; "user.name"; "host-binding-test" ] |> ignore
            run root [ "add"; nativeRoot ] |> ignore
            run root [ "commit"; "--quiet"; "-m"; "fixture" ] |> ignore
        member _.Root = root
        member _.Pins = pins
        member _.Profile = Path.Combine(root, nativeRoot, "native-operation-v1.json")
        member _.Head = run root [ "rev-parse"; "HEAD" ]
        interface IDisposable with member _.Dispose() = Directory.Delete(owner, true)

    let construct (fixture: Repository) =
        use scope = OwnedProcessScope.enterTest ()
        HostBinding.construct fixture.Root fixture.Head fixture.Profile fixture.Pins

    let inScope action =
        use scope = OwnedProcessScope.enterTest ()
        action scope

module HostBindingTests =
    [<Fact>]
    let ``current profile and source pins form a validated binding`` () =
        use fixture = new Fixture.Repository()
        let binding = Fixture.construct fixture
        Assert.Equal("1ef6d54eb3f9572580407efe9f266f643645af3af17c33723c0aa8368e5f4f34", binding.ProfileSha256)
        Assert.Equal(HostBinding.OperationId, binding.OperationId)
        Assert.Matches("^[0-9a-f]{40}$", binding.SourceTree)
        Assert.Matches("^[0-9a-f]{64}$", binding.BindingSha256)
        Assert.Matches("^[0-9a-f]{64}$", binding.ProducerSha256)

    [<Fact>]
    let ``admission formula matches independent fixed vector`` () =
        let binding =
            { Schema = "fsgg.telemetry.validated-host-binding/1"
              SourceSha = String('a', 40)
              SourceTree = String('b', 40)
              ProfileSha256 = "1ef6d54eb3f9572580407efe9f266f643645af3af17c33723c0aa8368e5f4f34"
              OperationId = HostBinding.OperationId
              SourcePinsSha256 = String('c', 64)
              ProducerSha256 = String('d', 64)
              BindingSha256 = String('e', 64) }
        Assert.Equal("1c12f59d8d04c7310f168eacb9b8403181febf479ee927cc7bd29d9dce5f7fe6", HostBinding.deriveAdmission binding "run-0001")
        let permuted = { binding with SourceSha = binding.ProfileSha256; ProfileSha256 = String('a', 40) }
        Assert.False(HostBinding.deriveAdmission binding "run-0001" = HostBinding.deriveAdmission permuted "run-0001")

    [<Fact>]
    let ``duplicate profile key is refused`` () =
        use fixture = new Fixture.Repository(fun text -> text.Replace("\"schema\":", "\"schema\": \"duplicate\", \"schema\":"))
        Assert.Throws<BindingRefusal>(fun () -> Fixture.construct fixture |> ignore) |> ignore

    [<Fact>]
    let ``wrong profile scalar kind is refused`` () =
        use fixture = new Fixture.Repository(fun text -> text.Replace("\"operationId\": \"v2-host-01.8a-native-collaboration-v1\"", "\"operationId\": 7"))
        Assert.Throws<BindingRefusal>(fun () -> Fixture.construct fixture |> ignore) |> ignore

    [<Fact>]
    let ``malformed profile digest with exact regenerated pin is refused`` () =
        use fixture = new Fixture.Repository(fun text -> text.Replace("167c0148a849d2444f1b5a7fb5f8bb2de1de5ae13a2a504b833fc765980f5cd9", "not-a-digest"))
        Assert.Throws<BindingRefusal>(fun () -> Fixture.construct fixture |> ignore) |> ignore

    [<Fact>]
    let ``profile limits and unknown critical fields are refused`` () =
        use limit = new Fixture.Repository(fun text -> text.Replace("\"maximumEvents\": 4096", "\"maximumEvents\": 2147483647"))
        Assert.Throws<BindingRefusal>(fun () -> Fixture.construct limit |> ignore) |> ignore
        use unknown = new Fixture.Repository(fun text -> text.Replace("\"timeoutSeconds\": 300,", "\"timeoutSeconds\": 300, \"authority\": true,"))
        Assert.Throws<BindingRefusal>(fun () -> Fixture.construct unknown |> ignore) |> ignore

    [<Fact>]
    let ``operation drift is refused`` () =
        use fixture = new Fixture.Repository(fun text -> text.Replace(HostBinding.OperationId, "v2-host-01.8a-readonly-source-compatibility-v1"))
        Assert.Throws<BindingRefusal>(fun () -> Fixture.construct fixture |> ignore) |> ignore

    [<Fact>]
    let ``source mutation after construction is refused`` () =
        use fixture = new Fixture.Repository()
        let binding = Fixture.construct fixture
        File.AppendAllText(fixture.Profile, "\n")
        Assert.Throws<BindingRefusal>(fun () -> Fixture.construct fixture |> ignore) |> ignore
        Assert.Throws<BindingRefusal>(fun () -> HostBinding.verifyAdmission binding "run-0001" binding.BindingSha256 (String('0', 64))) |> ignore

    [<Fact>]
    let ``wrong revision and mixed pin content are refused`` () =
        use fixture = new Fixture.Repository()
        Assert.Throws<BindingRefusal>(fun () -> Fixture.inScope (fun _ -> HostBinding.construct fixture.Root (String('a', 40)) fixture.Profile fixture.Pins |> ignore)) |> ignore
        let fields = JsonSerializer.Deserialize<Map<string, string>>(File.ReadAllText(fixture.Pins))
        let mixed = fields.Add("native-operation-v1.json", "5a30fc507f023d542521aac66c8f49c5ae6ee8d9e34dc90c1a3bf3ab30f6b08f")
        File.WriteAllText(fixture.Pins, JsonSerializer.Serialize(mixed))
        Assert.Throws<BindingRefusal>(fun () -> Fixture.construct fixture |> ignore) |> ignore

    [<Fact>]
    let ``duplicate and wrong-kind source pins are refused`` () =
        use duplicate = new Fixture.Repository()
        let raw = File.ReadAllText(duplicate.Pins)
        File.WriteAllText(duplicate.Pins, raw.Replace("{", "{\"qualify_native.py\":\"" + String('0', 64) + "\","))
        Assert.Throws<BindingRefusal>(fun () -> Fixture.construct duplicate |> ignore) |> ignore
        use wrongKind = new Fixture.Repository()
        let wrong = File.ReadAllText(wrongKind.Pins).Replace("\"native-producer-config.toml\":\"", "\"native-producer-config.toml\":7,\"ignored\":\"")
        File.WriteAllText(wrongKind.Pins, wrong)
        Assert.Throws<BindingRefusal>(fun () -> Fixture.construct wrongKind |> ignore) |> ignore

    [<Fact>]
    let ``binding and admission comparisons are exact`` () =
        use fixture = new Fixture.Repository()
        let binding = Fixture.construct fixture
        let admission = HostBinding.deriveAdmission binding "run-0001"
        HostBinding.verifyAdmission binding "run-0001" binding.BindingSha256 admission
        Assert.Throws<BindingRefusal>(fun () -> HostBinding.verifyAdmission binding "run-0002" binding.BindingSha256 admission) |> ignore
        Assert.Throws<BindingRefusal>(fun () -> HostBinding.verifyAdmission binding "run-0001" (String('0', 64)) admission) |> ignore

    [<Fact>]
    let ``child output capture is concurrent bounded and finite`` () =
        let exitCode, stdout, stderr =
            Fixture.inScope (fun scope -> scope.Run("/usr/bin/python3", "/", [ "-c"; "import sys; sys.stdout.write('ok'); sys.stderr.write('e'*4096)" ], 2000, 16, 4096))
        Assert.Equal(0, exitCode)
        Assert.Equal("ok", stdout)
        Assert.Equal(4096, stderr.Length)
        Assert.Throws<BindingRefusal>(fun () -> Fixture.inScope (fun scope -> scope.Run("/usr/bin/python3", "/", [ "-c"; "import sys; sys.stderr.write('x'*70000); sys.stdout.write('unreachable')" ], 2000, 64, 4096) |> ignore)) |> ignore
        Assert.Throws<BindingRefusal>(fun () -> Fixture.inScope (fun scope -> scope.Run("/usr/bin/python3", "/", [ "-c"; "import sys; sys.stdout.write('x'*70000)" ], 2000, 4096, 64) |> ignore)) |> ignore
        let timer = Stopwatch.StartNew()
        Assert.Throws<BindingRefusal>(fun () -> Fixture.inScope (fun scope -> scope.Run("/usr/bin/python3", "/", [ "-c"; "import time; time.sleep(30)" ], 100, 64, 64) |> ignore)) |> ignore
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(5.0))

    [<Fact>]
    let ``exited parent cannot leave a pipe holding descendant`` () =
        let owner = Path.Combine(Path.GetTempPath(), "host-binding-descendant-" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(owner) |> ignore
        let pidFile = Path.Combine(owner, "pid")
        let script = "import os,time,pathlib\npid=os.fork()\nif pid==0:\n time.sleep(30)\n os._exit(0)\npathlib.Path(" + JsonSerializer.Serialize(pidFile) + ").write_text(str(pid))\nos._exit(0)\n"
        try
            let timer = Stopwatch.StartNew()
            Assert.Throws<BindingRefusal>(fun () -> Fixture.inScope (fun scope -> scope.Run("/usr/bin/python3", owner, [ "-c"; script ], 2000, 128, 128) |> ignore)) |> ignore
            Assert.True(timer.Elapsed < TimeSpan.FromSeconds(5.0))
            Assert.True(File.Exists(pidFile))
            let descendant = Int32.Parse(File.ReadAllText(pidFile))
            Assert.False(Directory.Exists($"/proc/{descendant}"))
        finally
            Directory.Delete(owner, true)

    [<Theory>]
    [<InlineData("setsid")>]
    [<InlineData("double-fork")>]
    [<InlineData("closed-pipes")>]
    [<InlineData("term-ignored")>]
    [<InlineData("nested-subreaper")>]
    let ``session and pipe escape variants are retired`` variant =
        let owner = Path.Combine(Path.GetTempPath(), "host-binding-escape-" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(owner) |> ignore
        let pidFile = Path.Combine(owner, "pid")
        let target = JsonSerializer.Serialize(pidFile)
        let body =
            match variant with
            | "setsid" -> "os.setsid(); pathlib.Path(" + target + ").write_text(str(os.getpid())); time.sleep(30)"
            | "double-fork" -> "os.setsid(); p=os.fork(); (os._exit(0) if p else None); pathlib.Path(" + target + ").write_text(str(os.getpid())); time.sleep(30)"
            | "closed-pipes" -> "os.setsid(); os.close(1); os.close(2); pathlib.Path(" + target + ").write_text(str(os.getpid())); time.sleep(30)"
            | "term-ignored" -> "os.setsid(); signal.signal(signal.SIGTERM, signal.SIG_IGN); pathlib.Path(" + target + ").write_text(str(os.getpid())); time.sleep(30)"
            | "nested-subreaper" -> "os.setsid(); ctypes.CDLL(None).prctl(36,1,0,0,0); p=os.fork(); (os._exit(0) if p else None); pathlib.Path(" + target + ").write_text(str(os.getpid())); time.sleep(30)"
            | _ -> failwith "variant"
        let script = "import os,time,pathlib,signal,ctypes\npid=os.fork()\nif pid==0:\n " + body.Replace("\n", "\n ") + "\n os._exit(0)\nos._exit(0)\n"
        try
            Assert.Throws<BindingRefusal>(fun () -> Fixture.inScope (fun scope -> scope.Run("/usr/bin/python3", owner, [ "-c"; script ], 500, 128, 128) |> ignore)) |> ignore
            let limit = Stopwatch.StartNew()
            while not (File.Exists(pidFile)) && limit.ElapsedMilliseconds < 1000L do Threading.Thread.Sleep(10)
            Assert.True(File.Exists(pidFile))
            let descendant = Int32.Parse(File.ReadAllText(pidFile))
            Assert.False(Directory.Exists($"/proc/{descendant}"))
        finally Directory.Delete(owner, true)

    [<Fact>]
    let ``library caller cannot construct without an owned CLI scope`` () =
        use fixture = new Fixture.Repository()
        let refusal = Assert.Throws<BindingRefusal>(fun () -> HostBinding.construct fixture.Root fixture.Head fixture.Profile fixture.Pins |> ignore)
        Assert.Equal("process-scope-refused", refusal.Data0)

    [<Fact>]
    let ``strict scope refuses a preexisting child without signalling it`` () =
        let start = ProcessStartInfo("/usr/bin/sleep")
        start.ArgumentList.Add("30")
        use unrelated = Process.Start(start)
        try
            let refusal = Assert.Throws<BindingRefusal>(fun () -> OwnedProcessScope.enterStrictTest () |> ignore)
            Assert.Equal("process-baseline-refused", refusal.Data0)
            Assert.False(unrelated.HasExited)
        finally
            if not unrelated.HasExited then unrelated.Kill()
            unrelated.WaitForExit()

    [<Fact>]
    let ``owned scope lease is non reentrant`` () =
        use scope = OwnedProcessScope.enterTest ()
        let refusal = Assert.Throws<BindingRefusal>(fun () -> OwnedProcessScope.enterTest () |> ignore)
        Assert.Equal("process-scope-busy", refusal.Data0)

    let private waitForFile path =
        let timer = Stopwatch.StartNew()
        while not (File.Exists(path)) && timer.ElapsedMilliseconds < 1500L do Threading.Thread.Sleep(10)
        Assert.True(File.Exists(path))

    [<Fact>]
    let ``forced phase kills a child adopted only after its TERM ignoring parent`` () =
        let owner = Path.Combine(Path.GetTempPath(), "host-binding-late-adoption-" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(owner) |> ignore
        let parentFile = Path.Combine(owner, "parent")
        let childFile = Path.Combine(owner, "child")
        let grandchildFile = Path.Combine(owner, "grandchild")
        let script =
            "import ctypes,os,pathlib,signal,time\n" +
            "signal.signal(signal.SIGTERM,signal.SIG_IGN)\n" +
            "pathlib.Path(" + JsonSerializer.Serialize(parentFile) + ").write_text(str(os.getpid()))\n" +
            "p=os.fork()\n" +
            "if p==0:\n os.setsid(); ctypes.CDLL(None).prctl(36,1,0,0,0); pathlib.Path(" + JsonSerializer.Serialize(childFile) + ").write_text(str(os.getpid())); g=os.fork();\n if g==0:\n  os.setsid(); pathlib.Path(" + JsonSerializer.Serialize(grandchildFile) + ").write_text(str(os.getpid())); time.sleep(30); os._exit(0)\n time.sleep(30); os._exit(0)\n" +
            "time.sleep(30)\n"
        try
            use scope = OwnedProcessScope.enterTest ()
            Assert.Throws<BindingRefusal>(fun () -> scope.Run("/usr/bin/python3", owner, [ "-c"; script ], 250, 128, 128) |> ignore) |> ignore
            waitForFile parentFile; waitForFile childFile; waitForFile grandchildFile
            Assert.False(Directory.Exists($"/proc/{Int32.Parse(File.ReadAllText(parentFile))}"))
            Assert.False(Directory.Exists($"/proc/{Int32.Parse(File.ReadAllText(childFile))}"))
            Assert.False(Directory.Exists($"/proc/{Int32.Parse(File.ReadAllText(grandchildFile))}"))
        finally Directory.Delete(owner, true)

    [<Fact>]
    let ``post spawn exception retires direct and adopted children before rethrow`` () =
        let owner = Path.Combine(Path.GetTempPath(), "host-binding-post-spawn-" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(owner) |> ignore
        let parentFile = Path.Combine(owner, "parent")
        let childFile = Path.Combine(owner, "child")
        let script =
            "import os,pathlib,time\n" +
            "pathlib.Path(" + JsonSerializer.Serialize(parentFile) + ").write_text(str(os.getpid()))\n" +
            "p=os.fork()\n" +
            "if p==0:\n pathlib.Path(" + JsonSerializer.Serialize(childFile) + ").write_text(str(os.getpid())); time.sleep(30); os._exit(0)\n" +
            "time.sleep(30)\n"
        try
            use scope = OwnedProcessScope.enterTest ()
            let hook () = waitForFile parentFile; waitForFile childFile; raise (InvalidOperationException("synthetic-post-spawn"))
            let error = Assert.Throws<InvalidOperationException>(fun () -> scope.RunWithPostSpawnHook("/usr/bin/python3", owner, [ "-c"; script ], 2000, 128, 128, hook) |> ignore)
            Assert.Equal("synthetic-post-spawn", error.Message)
            let diagnostic=scope.DiagnosticSnapshot
            Assert.Equal(OwnedProcessScope.RetirementClean,diagnostic.FirstRetirement)
            Assert.Equal(OwnedProcessScope.RetirementClean,diagnostic.FinalRetirement)
            Assert.False(Directory.Exists($"/proc/{Int32.Parse(File.ReadAllText(parentFile))}"))
            Assert.False(Directory.Exists($"/proc/{Int32.Parse(File.ReadAllText(childFile))}"))
        finally Directory.Delete(owner, true)

    [<Fact>]
    let ``unknown candidate is sticky and retained capacity is finite`` () =
        use scope = OwnedProcessScope.enterTest ()
        let first = 2000000000
        scope.ObserveCandidateForTest(first)
        scope.ObserveCandidateForTest(first)
        Assert.True(scope.IsUnknown)
        Assert.Equal(OwnedProcessScope.IdentityRead,scope.DiagnosticSnapshot.FirstFailureSite)
        Assert.Equal(OwnedProcessScope.ExistenceGuard,scope.DiagnosticSnapshot.ReadOrigin)
        Assert.Equal(OwnedProcessScope.ExistsFalse,scope.DiagnosticSnapshot.ReadGuard)
        Assert.Equal(OwnedProcessScope.ManagedNone,scope.DiagnosticSnapshot.ManagedException)
        Assert.Equal(OwnedProcessScope.InitialPass,scope.DiagnosticSnapshot.AcquisitionPass)
        Assert.Equal(OwnedProcessScope.OtherCandidate,scope.DiagnosticSnapshot.CandidateRelation)
        Assert.Equal(1, scope.RejectedCandidateCount)
        for pid in first + 1 .. first + 256 do scope.ObserveCandidateForTest(pid)
        Assert.Equal(256, scope.RejectedCandidateCount)
        Assert.True(scope.RetainedCapacityExhausted)
        Assert.Equal(OwnedProcessScope.IdentityRead,scope.DiagnosticSnapshot.FirstFailureSite)

    [<Fact>]
    let ``failure and retirement ordinals belong to the failing run`` () =
        use scope = OwnedProcessScope.enterTest ()
        let firstCode,_,_=scope.Run("/usr/bin/true",Path.GetTempPath(),[],1000,128,128)
        Assert.Equal(0,firstCode)
        let missing=2000000000
        let refusal=Assert.Throws<BindingRefusal>(fun()->scope.RunWithPostSpawnHook("/usr/bin/sleep",Path.GetTempPath(),["1"],2000,128,128,(fun()->scope.ObserveCandidateForTest(missing)))|>ignore)
        Assert.Equal("process-cleanup-unknown",refusal.Data0)
        let diagnostic=scope.DiagnosticSnapshot
        Assert.Equal(2,diagnostic.RunOrdinal)
        Assert.Equal(2,diagnostic.FirstFailureRunOrdinal)
        Assert.Equal(1,diagnostic.ScopeFirstRetirementRunOrdinal)
        Assert.Equal(OwnedProcessScope.RetirementClean,diagnostic.FirstRetirement)
        Assert.Equal(2,diagnostic.RetirementRunOrdinal)
        Assert.Equal(OwnedProcessScope.RetirementUnknown,diagnostic.RunFirstRetirement)
        Assert.Equal(OwnedProcessScope.RetirementUnknown,diagnostic.RunFinalRetirement)

    [<Fact>]
    let ``managed exception classes and identity contexts are closed`` () =
        use scope = OwnedProcessScope.enterTest ()
        Assert.Equal(OwnedProcessScope.FileNotFound,scope.ClassifyManagedExceptionForTest(FileNotFoundException()))
        Assert.Equal(OwnedProcessScope.DirectoryNotFound,scope.ClassifyManagedExceptionForTest(DirectoryNotFoundException()))
        Assert.Equal(OwnedProcessScope.ManagedUnauthorized,scope.ClassifyManagedExceptionForTest(UnauthorizedAccessException()))
        Assert.Equal(OwnedProcessScope.GenericIo,scope.ClassifyManagedExceptionForTest(IOException()))
        scope.ObserveIdentityContextForTest(2000000000,true,true)
        Assert.Equal(OwnedProcessScope.RecheckPass,scope.DiagnosticSnapshot.AcquisitionPass)
        Assert.Equal(OwnedProcessScope.ActiveDirect,scope.DiagnosticSnapshot.CandidateRelation)

    [<Fact>]
    let ``legacy fast exit loses actual direct identity before acquisition`` () =
        use scope=OwnedProcessScope.enterTest()
        let hook (direct:Process) = direct.WaitForExit();scope.ObserveCandidateForTest(direct.Id)
        Assert.Throws<BindingRefusal>(fun()->scope.RunLegacyForTest("/usr/bin/python3",Path.GetTempPath(),["-c";"import os; os._exit(0)"],1000,128,128,hook)|>ignore)|>ignore
        let diagnostic=scope.DiagnosticSnapshot
        Assert.Equal(OwnedProcessScope.ExistenceGuard,diagnostic.ReadOrigin)
        Assert.Equal(OwnedProcessScope.ActiveDirect,diagnostic.CandidateRelation)
        Assert.True(scope.IsUnknown)

    [<Fact>]
    let ``gated fast exit cannot execute before identity and preserves same pid`` () =
        let owner=Path.Combine(Path.GetTempPath(),"host-binding-gated-"+Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(owner)|>ignore
        let marker=Path.Combine(owner,"marker")
        try
            use scope=OwnedProcessScope.enterTest()
            let mutable launchedPid=0
            let hook (direct:Process) =
                launchedPid<-direct.Id
                Threading.Thread.Sleep(100)
                Assert.False(File.Exists(marker))
            let script="import os,pathlib; pathlib.Path("+JsonSerializer.Serialize(marker)+").write_text(str(os.getpid()))"
            let code,_,_=scope.RunWithPreAcquireHook("/usr/bin/python3",owner,["-c";script],2000,128,128,hook)
            Assert.Equal(0,code)
            Assert.Equal(launchedPid,Int32.Parse(File.ReadAllText(marker)))
            Assert.False(scope.IsUnknown)
            let actions=scope.LaunchTraceForTest|>List.map fst
            Assert.Equal<OwnedLaunch.Action list>(
                [OwnedLaunch.RecordIdentity;OwnedLaunch.RequestRelease;OwnedLaunch.AcknowledgeRelease;OwnedLaunch.BeginRetirement;OwnedLaunch.ObserveSettlement(true,true,true);OwnedLaunch.Finish],
                actions)
            Assert.Equal(OwnedLaunch.Terminal,(scope.LaunchTraceForTest|>List.last|>snd).Phase)
        finally Directory.Delete(owner,true)

    [<Fact>]
    let ``cancellation before identity never releases the guest`` () =
        let owner=Path.Combine(Path.GetTempPath(),"host-binding-gate-cancel-"+Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(owner)|>ignore
        let marker=Path.Combine(owner,"marker")
        try
            use scope=OwnedProcessScope.enterTest()
            let script="import pathlib; pathlib.Path("+JsonSerializer.Serialize(marker)+").write_text('released')"
            Assert.Throws<BindingRefusal>(fun()->scope.RunWithPreAcquireHook("/usr/bin/python3",owner,["-c";script],2000,128,128,(fun _->scope.RequestCancellation()))|>ignore)|>ignore
            Assert.False(File.Exists(marker))
        finally Directory.Delete(owner,true)

    [<Fact>]
    let ``launcher refuses an invalid gate byte without executing the guest`` () =
        let owner=Path.Combine(Path.GetTempPath(),"host-binding-invalid-gate-"+Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(owner)|>ignore
        let marker=Path.Combine(owner,"marker")
        try
            let start=ProcessStartInfo("/usr/bin/dotnet")
            start.UseShellExecute<-false
            start.RedirectStandardInput<-true
            start.RedirectStandardOutput<-true
            start.RedirectStandardError<-true
            start.ArgumentList.Add(typeof<BindingRefusal>.Assembly.Location)
            start.ArgumentList.Add("__owned-launch")
            start.ArgumentList.Add("python3-test")
            start.ArgumentList.Add("-c")
            start.ArgumentList.Add("import pathlib; pathlib.Path("+JsonSerializer.Serialize(marker)+").write_text('released')")
            use child=Process.Start(start)
            child.StandardInput.Write("X")
            child.StandardInput.Close()
            Assert.True(child.WaitForExit(2000))
            Assert.Equal(125,child.ExitCode)
            Assert.False(File.Exists(marker))
        finally Directory.Delete(owner,true)

    [<Fact>]
    let ``unsupported pidfd capability refuses before scope and child creation`` () =
        OwnedProcessScope.setCapabilityProbeForTest(Some(fun () -> false))
        try
            let before = Directory.GetDirectories("/proc/self/task") |> Array.collect (fun thread -> File.ReadAllText(Path.Combine(thread, "children")).Split(' ', StringSplitOptions.RemoveEmptyEntries)) |> Set.ofArray
            let refusal = Assert.Throws<BindingRefusal>(fun () -> OwnedProcessScope.enterTest () |> ignore)
            Assert.Equal("process-pidfd-refused", refusal.Data0)
            let after = Directory.GetDirectories("/proc/self/task") |> Array.collect (fun thread -> File.ReadAllText(Path.Combine(thread, "children")).Split(' ', StringSplitOptions.RemoveEmptyEntries)) |> Set.ofArray
            Assert.Equal<string>(before, after)
        finally OwnedProcessScope.setCapabilityProbeForTest(None)

    [<Fact>]
    let ``successful pidfd capability probe closes its descriptor`` () =
        let descriptors () = Directory.GetFiles("/proc/self/fd") |> Array.length
        let before = descriptors ()
        for _ in 1 .. 32 do Assert.True(OwnedProcessScope.probePidFdCapabilityForTest ())
        Assert.Equal(before, descriptors ())
