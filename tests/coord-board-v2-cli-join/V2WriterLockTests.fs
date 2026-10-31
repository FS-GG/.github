/// The shared board writer lock (board-reconciler design §8, FS-GG/.github#5136 PR 1, #5126).
// Shares a collection with BoardV2ApplicationTests: both mutate XDG_RUNTIME_DIR, so they never run in parallel.
[<Xunit.Collection("xdg-runtime-dir")>]
module FS.GG.Coord.GitHub.Tests.V2WriterLockTests

open System
open System.Diagnostics
open System.IO
open Xunit
open FS.GG.Coord.GitHub

let private repositoryRoot () =
    let rec up (directory: DirectoryInfo) =
        if isNull directory then failwith "repository root (global.json) not found"
        elif File.Exists(Path.Combine(directory.FullName, "global.json")) && Directory.Exists(Path.Combine(directory.FullName, "src")) then directory.FullName
        else up directory.Parent
    up (DirectoryInfo AppContext.BaseDirectory)

let private location directory = V2WriterLock.inDirectory directory "PVT_kwDOEYAWY84Bldpa" V2WriterLock.migrationLegacyName

let private python (script: string) (arguments: string list) =
    let info = ProcessStartInfo("python3", RedirectStandardInput = true, RedirectStandardOutput = true, UseShellExecute = false)
    info.ArgumentList.Add "-I"
    info.ArgumentList.Add "-c"
    info.ArgumentList.Add script
    for argument in arguments do info.ArgumentList.Add argument
    Process.Start info

// Holds flock(LOCK_EX) on argv[1] until stdin closes, printing "held" once it has the lock.
let private holdScript = "import fcntl,os,sys\nfd=os.open(sys.argv[1],os.O_RDWR|os.O_CREAT,0o600)\nfcntl.flock(fd,fcntl.LOCK_EX)\nprint('held',flush=True)\nsys.stdin.read()\n"
// Exits 0 if flock(LOCK_EX|LOCK_NB) on argv[1] succeeds, 1 if another holder has it.
let private tryScript = "import fcntl,os,sys\nfd=os.open(sys.argv[1],os.O_RDWR|os.O_CREAT,0o600)\ntry:\n    fcntl.flock(fd,fcntl.LOCK_EX|fcntl.LOCK_NB)\nexcept BlockingIOError:\n    sys.exit(1)\nsys.exit(0)\n"

[<Fact>]
let ``production location needs an absolute XDG_RUNTIME_DIR and does not follow TMPDIR`` () =
    let saved = Environment.GetEnvironmentVariable "XDG_RUNTIME_DIR"
    try
        Environment.SetEnvironmentVariable("XDG_RUNTIME_DIR", null)
        Assert.True(V2WriterLock.production "PVT_kwDOEYAWY84Bldpa" V2WriterLock.migrationLegacyName |> Result.isError)
        Environment.SetEnvironmentVariable("XDG_RUNTIME_DIR", "relative/run")
        Assert.True(V2WriterLock.production "PVT_kwDOEYAWY84Bldpa" V2WriterLock.migrationLegacyName |> Result.isError)
        Environment.SetEnvironmentVariable("XDG_RUNTIME_DIR", "/run/user/1000")
        match V2WriterLock.production "PVT_kwDOEYAWY84Bldpa" V2WriterLock.migrationLegacyName with
        | Error reason -> failwith reason
        | Ok production ->
            Assert.Equal<string list>(
                [ "/run/user/1000/fsgg/board-writer-PVT_kwDOEYAWY84Bldpa.lock"; "/tmp/fsgg-v2-migration-PVT_kwDOEYAWY84Bldpa.lock" ],
                production.Paths)
    finally Environment.SetEnvironmentVariable("XDG_RUNTIME_DIR", saved)

[<Fact>]
let ``a lease excludes a second taker on either path and releases on dispose`` () =
    let directory = TestLock.fresh ()
    let first = match V2WriterLock.tryAcquire (location directory) with Ok lease -> lease | Error reason -> failwith reason
    Assert.Equal(Error "lock-held", V2WriterLock.tryAcquire (location directory) |> Result.map ignore)
    first.Dispose()
    let legacyOnly = V2WriterLock.inDirectory directory "unrelated" V2WriterLock.migrationLegacyName
    let legacy = match V2WriterLock.tryAcquire legacyOnly with Ok lease -> lease | Error reason -> failwith reason
    Assert.Equal(Error "lock-held", V2WriterLock.tryAcquire (location directory) |> Result.map ignore)
    // A failed lease released what it took: the new-path lock is free again for another taker.
    let newOnly = V2WriterLock.inDirectory directory "PVT_kwDOEYAWY84Bldpa" "other-legacy.lock"
    (match V2WriterLock.tryAcquire newOnly with Ok lease -> lease.Dispose() | Error reason -> failwith reason)
    legacy.Dispose()
    (match V2WriterLock.tryAcquire (location directory) with Ok lease -> lease.Dispose() | Error reason -> failwith reason)
    Assert.Throws<IOException>(fun () ->
        use _held = V2WriterLock.acquire (location directory)
        V2WriterLock.acquire (location directory) |> ignore) |> ignore

[<Fact>]
let ``an installed engine holding the legacy lock with FileShare.None excludes a new writer`` () =
    // The pre-release engine opens /tmp/fsgg-v2-migration-*.lock with FileShare.None, which .NET implements with
    // flock(LOCK_EX). A new writer takes that legacy path too, so the two exclude each other during the transition.
    let directory = TestLock.fresh ()
    let legacyPath = Path.Combine(directory, V2WriterLock.migrationLegacyName)
    (
        use _installed = new FileStream(legacyPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)
        Assert.Equal(Error "lock-held", V2WriterLock.tryAcquire (location directory) |> Result.map ignore))
    match V2WriterLock.tryAcquire (location directory) with
    | Ok lease -> lease.Dispose()
    | Error reason -> failwith reason

[<Fact>]
let ``flock excludes across processes in both directions with the Python writers`` () =
    let directory = TestLock.fresh ()
    let path = (location directory).Paths.Head
    // Python holds: the engine refuses.
    use holder = python holdScript [ path ]
    Assert.Equal("held", holder.StandardOutput.ReadLine())
    Assert.Equal(Error "lock-held", V2WriterLock.tryAcquire (location directory) |> Result.map ignore)
    holder.StandardInput.Close()
    Assert.True(holder.WaitForExit 30000)
    // The engine holds: Python refuses; after release Python succeeds.
    let lease = match V2WriterLock.tryAcquire (location directory) with Ok lease -> lease | Error reason -> failwith reason
    use blocked = python tryScript [ path ]
    Assert.True(blocked.WaitForExit 30000)
    Assert.Equal(1, blocked.ExitCode)
    lease.Dispose()
    use free = python tryScript [ path ]
    Assert.True(free.WaitForExit 30000)
    Assert.Equal(0, free.ExitCode)

[<Fact>]
let ``only the production composition resolves the production lock and no test reaches it`` () =
    let root = repositoryRoot ()
    let source path = File.ReadAllText(Path.Combine(root, path))
    let application = source "src/FS.GG.Coord.Cli/BoardV2Application.fs"
    let count (text: string) (needle: string) = (text.Length - text.Replace(needle, "").Length) / needle.Length
    // One resolver wraps V2WriterLock.production, and only tryRun (the installed command) uses it.
    Assert.Equal(1, count application "V2WriterLock.production ")
    Assert.Equal(3, count application "productionWriterLock") // definition plus the two tryRun arms
    let tryRun = application.Substring(application.IndexOf("let tryRun arguments", StringComparison.Ordinal))
    Assert.Equal(2, count tryRun "productionWriterLock")
    // Every engine source that touches a lock goes through V2WriterLock: no direct GetTempPath lock remains.
    for file in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.fs", SearchOption.AllDirectories) do
        let text = File.ReadAllText file
        Assert.False(text.Contains("fsgg-v2-migration-PVT") && not (file.EndsWith("V2WriterLock.fs", StringComparison.Ordinal)), file)
        Assert.False(text.Contains("\"fsgg-v2-observation-\"") && not (file.EndsWith("V2WriterLock.fs", StringComparison.Ordinal)), file)
    // No test calls the production entry for a writer or resolves the production lock to acquire it.
    for file in Directory.EnumerateFiles(Path.Combine(root, "tests"), "*.fs", SearchOption.AllDirectories) do
        let text = File.ReadAllText file
        // tryRun with board-v2 arguments is the installed command and resolves the production lock.
        Assert.False(Text.RegularExpressions.Regex.IsMatch(text, "BoardV2Application\\.tryRun\\s*\\[\\s*\"board-v2\""), file)
        if not (file.EndsWith("V2WriterLockTests.fs", StringComparison.Ordinal)) then
            Assert.False(text.Contains("V2WriterLock.production"), file)
