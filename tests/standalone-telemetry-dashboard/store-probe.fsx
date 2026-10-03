#r "FS.GG.Coord.Core.dll"
#r "FS.GG.Telemetry.Store.dll"

open System
open System.Diagnostics
open System.IO
open System.Reflection
open System.Runtime
open System.Runtime.InteropServices
open System.Security.Cryptography
open System.Text
open System.Text.Json
open FS.GG.Coord
open FS.GG.Coord.Cli

let fail message =
    raise (InvalidOperationException message)

let unwrap =
    function
    | Ok value -> value
    | Error errors -> fail (String.concat "; " errors)

let percentile95 (values: float array) =
    if values.Length = 0 then
        fail "performance samples are empty"

    values
    |> Array.sort
    |> Array.item (int (Math.Ceiling(0.95 * float values.Length)) - 1)

let summary (values: float array) =
    let ordered = Array.sort values
    let middle = ordered.Length / 2

    let median =
        if ordered.Length % 2 = 0 then
            (ordered[middle - 1] + ordered[middle]) / 2.0
        else
            ordered[middle]

    {|
        medianMilliseconds = Math.Round(median, 3)
        p95Milliseconds = Math.Round(percentile95 values, 3)
        maximumMilliseconds = Math.Round(Array.max values, 3)
    |}

let scope: TelemetryReceipt.Scope =
    {
        Workspace = "standalone-performance"
        Producer = "packaged-store-probe"
        Stream = "qualification"
    }

let envelope series index =
    let filler = String('x', 61 * 1024)
    let ingestSchema = "fsgg.telemetry." + "ingest/1"

    Encoding.UTF8.GetBytes(
        $"""{{"schema":"fsgg.telemetry.envelope/1","workspaceId":"{scope.Workspace}","producerId":"{scope.Producer}","streamId":"{scope.Stream}","batchId":"{series}-{index:D3}","payload":{{"schema":"{ingestSchema}","ingestId":"{series}-{index:D3}","sourceIdentity":"packaged-store-probe","generation":"{series}","cursor":"{index}","eventCount":1,"events":[{{"kind":"item","identity":"{series}-{index:D3}-{filler}","itemId":"standalone-performance","revision":{index}}}]}}}}"""
    )

let elapsed action =
    let timer = Stopwatch.StartNew()
    action ()
    timer.Stop()
    Math.Round(timer.Elapsed.TotalMilliseconds, 3)

type PhaseSample = { name: string; timestamp: int64 }

type AdmissionSample =
    { index: int
      startTimestamp: int64
      endTimestamp: int64
      elapsedMilliseconds: float
      processCpuMilliseconds: float
      gcCollections: int array
      phases: PhaseSample array
      phasesTruncated: bool }

let measuredAdmission index action =
    use currentProcess = Process.GetCurrentProcess()
    let cpuBefore = currentProcess.TotalProcessorTime
    let gcBefore = Array.init 3 GC.CollectionCount
    let phases = ResizeArray<PhaseSample>(16)
    let mutable truncated = false
    let hook name =
        // Hooks remain inside the admission timer. Their cost is not subtracted.
        if phases.Count < 256 then
            phases.Add({ name = name; timestamp = Stopwatch.GetTimestamp() })
        else
            truncated <- true
    let started = Stopwatch.GetTimestamp()
    action hook
    let finished = Stopwatch.GetTimestamp()
    currentProcess.Refresh()
    let cpuAfter = currentProcess.TotalProcessorTime
    let gcAfter = Array.init 3 GC.CollectionCount
    let milliseconds =
        Math.Round(float (finished - started) * 1000.0 / float Stopwatch.Frequency, 3)
    { index = index
      startTimestamp = started
      endTimestamp = finished
      elapsedMilliseconds = milliseconds
      processCpuMilliseconds = Math.Round((cpuAfter - cpuBefore).TotalMilliseconds, 3)
      gcCollections = Array.map2 (-) gcAfter gcBefore
      phases = phases.ToArray()
      phasesTruncated = truncated }

let fileDigest path =
    if String.IsNullOrWhiteSpace path || not (File.Exists path) then null
    else
        use stream = File.OpenRead path
        SHA256.HashData stream |> Convert.ToHexString |> fun value -> value.ToLowerInvariant()

let loadedDigest name =
    match AppDomain.CurrentDomain.GetAssemblies() |> Array.filter (fun assembly -> assembly.GetName().Name = name) with
    | [| assembly |] -> fileDigest assembly.Location
    | _ -> null

let nativeSqliteDigest () =
    if File.Exists "/proc/self/maps" then
        File.ReadLines "/proc/self/maps"
        |> Seq.map (fun line -> line.Split(' ', StringSplitOptions.RemoveEmptyEntries) |> Array.last)
        |> Seq.filter (fun path -> Path.GetFileName path = "libe_sqlite3.so")
        |> Seq.distinct
        |> Seq.toArray
        |> function
            | [| path |] -> fileDigest path
            | _ -> null
    else null

let diagnosticContext () =
    // Only these configuration values are read, never the process environment as a whole.
    // An unset or non-boolean value stays unknown instead of being copied into evidence.
    let settings = System.Collections.Generic.Dictionary<string, string>()
    for name in
        [| "DOTNET_TieredCompilation"; "DOTNET_TieredPGO"; "DOTNET_ReadyToRun"
           "DOTNET_gcServer"; "DOTNET_gcConcurrent"; "COMPlus_TieredCompilation"
           "COMPlus_TieredPGO"; "COMPlus_ReadyToRun"; "COMPlus_gcServer"; "COMPlus_gcConcurrent" |] do
        let value = Environment.GetEnvironmentVariable name
        settings.Add(name, if value = "0" || value = "1" then value else null)
    let cpuModel =
        if File.Exists "/proc/cpuinfo" then
            File.ReadLines "/proc/cpuinfo"
            |> Seq.tryFind (fun line -> line.StartsWith("model name", StringComparison.Ordinal))
            |> Option.map (fun line -> line.Split(':', 2).[1].Trim())
            |> Option.defaultValue null
        else null
    let cpuQuota =
        if File.Exists "/sys/fs/cgroup/cpu.max" then
            let value = File.ReadAllText("/sys/fs/cgroup/cpu.max").Trim()
            if System.Text.RegularExpressions.Regex.IsMatch(value, "^(max|[0-9]+) [0-9]+$") then value else null
        else null
    let fsiHost = Assembly.GetEntryAssembly()
    {|
        runtimeVersion = Environment.Version.ToString()
        runtimeAssemblySha256 = fileDigest (typeof<obj>.Assembly.Location)
        fsiHostVersion = if isNull fsiHost then null else fsiHost.GetName().Version.ToString()
        fsiHostSha256 = if isNull fsiHost then null else fileDigest fsiHost.Location
        framework = RuntimeInformation.FrameworkDescription
        processArchitecture = RuntimeInformation.ProcessArchitecture.ToString()
        osArchitecture = RuntimeInformation.OSArchitecture.ToString()
        processorCount = Environment.ProcessorCount
        cpuModel = cpuModel
        cpuQuota = cpuQuota
        operatingSystem = RuntimeInformation.OSDescription
        isServerGC = GCSettings.IsServerGC
        gcLatencyMode = GCSettings.LatencyMode.ToString()
        observedEnvironment = settings
        // These are loaded bytes, not an assertion about effective JIT policy.
        storeAssemblySha256 = loadedDigest "FS.GG.Telemetry.Store"
        coreAssemblySha256 = loadedDigest "FS.GG.Coord.Core"
        nativeSqliteSha256 = nativeSqliteDigest ()
    |}

let initialize root =
    match TelemetryStoreApplication.assessProductionRoot root with
    | TelemetryStore.ApprovedLocalDurable -> ()
    | assessment -> fail $"packaged Store probe root is not assessor-qualified: {assessment}"

    TelemetryStoreApplication.initialize root TelemetryStore.ApprovedLocalDurable
    |> unwrap
    |> ignore

    TelemetryStoreApplication.enrollReceiptProducer root TelemetryStore.ApprovedLocalDurable scope
    |> unwrap
    |> ignore

let submit root series index =
    TelemetryStoreApplication.submitReceipt root TelemetryStore.ApprovedLocalDurable scope (envelope series index)
    |> unwrap
    |> ignore

let drain root =
    TelemetryStoreApplication.drainReceipts root TelemetryStore.ApprovedLocalDurable scope.Workspace
    |> unwrap
    |> ignore

let args = fsi.CommandLineArgs |> Array.skip 1

if args.Length <> 4 then
    fail "usage: store-probe.fsx OUTPUT ROOT STORE-ASSEMBLY-SHA SAMPLE-COUNT"

let output, root, storeAssemblySha256, sampleText =
    args[0], args[1], args[2], args[3]

let sampleCount = Int32.Parse sampleText

if sampleCount <> 100 then
    fail "exactly 100 Store samples are required"

if Directory.Exists root || File.Exists root then
    fail "Store probe root must not exist"

let steadyRoot = Path.Combine(root, "steady")
let backlogRoot = Path.Combine(root, "backlog")

try
    initialize steadyRoot

    for index in 0..4 do
        submit steadyRoot "warmup" index
        drain steadyRoot

    let admission = Array.zeroCreate<float> sampleCount
    let application = Array.zeroCreate<float> sampleCount
    let diagnostics = Array.zeroCreate<AdmissionSample> sampleCount

    for index in 0 .. sampleCount - 1 do
        let sample =
            measuredAdmission index (fun hook ->
                TelemetryStoreApplication.submitReceiptWithHook
                    steadyRoot TelemetryStore.ApprovedLocalDurable scope (envelope "steady" index) hook
                |> unwrap
                |> ignore)
        diagnostics[index] <- sample
        admission[index] <- sample.elapsedMilliseconds
        application[index] <- elapsed (fun () -> drain steadyRoot)

    initialize backlogRoot

    let backlog =
        [|
            for index in 0 .. sampleCount - 1 -> elapsed (fun () -> submit backlogRoot "backlog" index)
        |]

    let windows =
        backlog
        |> Array.chunkBySize 20
        |> Array.mapi (fun index values ->
            {|
                firstSample = index * 20 + 1
                lastSample = index * 20 + values.Length
                summary = summary values
            |})

    let admissionSummary = summary admission
    let limit = 100.0

    let result =
        {|
            schema = "fsgg.telemetry.packaged-store-performance/1"
            qualified = admissionSummary.p95Milliseconds <= limit
            storeAssemblySha256 = storeAssemblySha256
            assessment = "approved-local-durable"
            diagnostics =
                {|
                    schema = "fsgg.telemetry.store-phase-diagnostics/1"
                    warmupPairs = 5
                    firstSample = 0
                    sampleCount = sampleCount
                    timestampFrequency = Stopwatch.Frequency
                    context = diagnosticContext ()
                    admissions = diagnostics
                    interpretation = "observations-only; CPU includes process-wide work; GC overlap is not attribution"
                |}
            samples =
                {|
                    warmInProcessAdmissionMilliseconds = admission
                    applicationDrainMilliseconds = application
                    accumulatingPendingAdmissionMilliseconds = backlog
                |}
            summary =
                {|
                    warmInProcessAdmission = admissionSummary
                    applicationDrain = summary application
                    accumulatingPendingAdmission = summary backlog
                    accumulatingPendingWindows = windows
                |}
            limits =
                {|
                    warmInProcessAdmissionP95Milliseconds = limit
                |}
        |}

    File.WriteAllText(output, JsonSerializer.Serialize result + "\n")

    printfn
        "%s"
        (JsonSerializer.Serialize
            {|
                qualified = result.qualified
                summary = result.summary
            |})

    if not result.qualified then
        Environment.ExitCode <- 1
finally
    if Directory.Exists root then
        Directory.Delete(root, true)
