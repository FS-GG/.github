#r "FS.GG.Coord.Core.dll"
#r "FS.GG.Telemetry.Store.dll"

open System
open System.Diagnostics
open System.IO
open System.IO.Compression
open System.Reflection
open System.Runtime
open System.Runtime.InteropServices
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
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

// These fixed declarations are synthetic contract inputs. Only the receiver bytes
// come from the exact installed candidate package; this is not native lineage evidence.
let causalText (node: JsonElement) (name: string) = node.GetProperty(name).GetString()
let causalHash (bytes: byte array) = Convert.ToHexString(SHA256.HashData bytes).ToLowerInvariant()
let causalNode (value: string) : JsonNode = if isNull value then null else JsonValue.Create(value)
let causalObject (pairs: (string * JsonNode) list) =
    let result = JsonObject()
    for key, value in pairs do result[key] <- value
    result

type CausalCase = { name: string; admission: JsonElement; raw: byte array; event: JsonElement }

let causalFixtures manifestPath =
    let manifestBytes = File.ReadAllBytes manifestPath
    if manifestBytes.Length > 65536 then fail "causal fixture manifest exceeds bound"
    use manifest = JsonDocument.Parse manifestBytes
    let document = manifest.RootElement
    if causalText document "schema" <> "fsgg.learn.causal-admission-fixtures/1"
       || document.GetProperty("caseCount").GetInt32() <> 4 then fail "causal fixture manifest differs"
    let directory = Path.GetDirectoryName(Path.GetFullPath manifestPath)
    let read (entry: JsonElement) field limit =
        let name = causalText entry (field + "File")
        if String.IsNullOrWhiteSpace name || Path.GetFileName name <> name
           || name.Contains('/') || name.Contains('\\') || name = "." || name = ".." then
            fail "causal fixture path is unsafe"
        let path = Path.Combine(directory, name)
        let info = FileInfo path
        let count = entry.GetProperty(field + "Bytes").GetInt32()
        if not (isNull info.LinkTarget) || count < 1 || count > limit || info.Length <> int64 count then
            fail "causal fixture size or path differs"
        let bytes = File.ReadAllBytes path
        if bytes.Length <> count || causalHash bytes <> causalText entry (field + "Sha256") then
            fail "causal fixture digest differs"
        bytes
    let cases =
        document.GetProperty("cases").EnumerateArray()
        |> Seq.map (fun entry ->
            let raw = read entry "admission" 4096
            let eventBytes = read entry "event" 16384
            use admission = JsonDocument.Parse raw
            use event = JsonDocument.Parse eventBytes
            if causalText event.RootElement "kind" <> "execution-causal-admission/1"
               || Convert.FromBase64String(causalText event.RootElement "admissionBase64") <> raw
               || causalText event.RootElement "admissionSha256" <> causalHash raw
               || causalText admission.RootElement "invocationId" <> causalText entry "invocationId" then
                fail "causal fixture byte binding differs"
            { name = causalText entry "name"; raw = raw
              admission = admission.RootElement.Clone(); event = event.RootElement.Clone() })
        |> Seq.toArray
    if (cases |> Array.map _.name |> Array.sort) <> [| "child"; "explicit-retry"; "follow-up"; "root" |] then
        fail "causal fixture case inventory differs"
    cases, causalHash manifestBytes

let causalBatch ingestId (events: JsonNode array) =
    let array = JsonArray()
    for event in events do array.Add event
    causalObject
        [ "schema", causalNode TelemetryStore.BatchSchema
          "ingestId", causalNode ingestId; "sourceIdentity", causalNode "packaged-causal-contract"
          "generation", causalNode "fixed-four-case"; "cursor", causalNode ingestId
          "eventCount", JsonValue.Create(events.Length); "events", array ]

let causalEvents (cases: CausalCase array) (case: CausalCase) =
    let admission = case.admission
    let get field = causalText admission field
    let invocation = get "invocationId"
    let dispatch = causalText case.event "dispatchId"
    let activation = "activation-" + case.name
    let common kind =
        [ "kind", causalNode kind; "identity", causalNode (kind + "-" + case.name)
          "itemId", causalNode (get "originalItemId"); "revision", JsonValue.Create(0) :> JsonNode ]
    let parentInvocation = get "parentInvocationId"
    let parentDispatch =
        cases |> Array.tryFind (fun candidate -> causalText candidate.admission "invocationId" = parentInvocation)
        |> Option.map (fun candidate -> causalText candidate.event "dispatchId") |> Option.defaultValue null
    let attempt (field: string) (generation: string) =
        let value = get field
        if isNull value then null else Guid.Parse(value).ToString("N") + "-g" + string (admission.GetProperty(generation).GetInt32())
    [| JsonNode.Parse(case.event.GetRawText())
       causalObject (common "operational-activation" @
            [ "activationId", causalNode activation; "scope", causalNode "explicit-future-dispatches"
              "runtime", causalNode "codex-exec"; "activatedAt", causalNode (get "admittedAt")
              "clockProvenance", causalNode "host-wall"; "lateAfterSeconds", JsonValue.Create(3600) ])
       causalObject (common "expected-dispatch" @
            [ "dispatchId", causalNode dispatch; "activationId", causalNode activation
              "relation", causalNode (get "relation"); "parentDispatchId", causalNode parentDispatch
              "runtime", causalNode "codex-exec"; "expectedAt", causalNode (get "admittedAt")
              "clockProvenance", causalNode "host-wall" ])
       causalObject (common "invocation-lineage" @
            [ "dispatchId", causalNode dispatch; "invocationId", causalNode invocation
              "relation", causalNode (get "relation"); "parentInvocationId", causalNode parentInvocation
              "rootInvocationId", causalNode (get "rootInvocationId"); "runtime", causalNode "codex-exec" ])
       causalObject (common "runtime-admission" @
            [ "invocationId", causalNode invocation; "featureId", causalNode "coordination-orchestration"
              "attemptId", causalNode (attempt "attemptId" "generation")
              "parentAttemptId", causalNode (attempt "parentAttemptId" "parentGeneration")
              "producerStream", causalNode "coordination"; "requestedModel", null
              "requestedEffort", null; "backend", null ]) |]

let causalSnapshot root (cases: CausalCase array) =
    use envelope = JsonDocument.Parse(TelemetryStoreApplication.dashboardSnapshot root TelemetryStore.ApprovedLocalDurable None |> unwrap)
    let document = envelope.RootElement
    if causalText document "schema" <> "fsgg.telemetry.item-detail/2" then fail "causal snapshot is not full format 2"
    use input = new MemoryStream(Convert.FromBase64String(causalText document "canonicalSnapshotGzip"))
    use gzip = new GZipStream(input, CompressionMode.Decompress)
    use output = new MemoryStream()
    let buffer = Array.zeroCreate<byte> 8192
    let mutable count = gzip.Read(buffer, 0, buffer.Length)
    while count > 0 do
        if output.Length + int64 count > 2L * 1024L * 1024L then fail "causal snapshot exceeds bound"
        output.Write(buffer, 0, count)
        count <- gzip.Read(buffer, 0, buffer.Length)
    let bytes = output.ToArray()
    let revision = causalHash bytes
    if revision <> causalText document "revision" then fail "causal snapshot revision differs"
    use canonical = JsonDocument.Parse bytes
    let snapshot = canonical.RootElement
    if snapshot.GetProperty("store").GetProperty("schemaVersion").GetInt32() <> 14 then fail "causal receiver schema differs"
    for relation in [ "admissions"; "expectedDispatches"; "lineage" ] do
        if snapshot.GetProperty(relation).GetArrayLength() <> 4 then fail ("causal coexistence count differs: " + relation)
    let rows = snapshot.GetProperty("learningObservations").EnumerateArray() |> Seq.toArray
    if rows.Length <> 4 then fail "causal declarations are not exported exactly once"
    for case in cases do
        let expectedIdentity = causalText case.event "identity"
        let row = rows |> Array.filter (fun row -> causalText row "identity" = expectedIdentity)
        if row.Length <> 1 || causalText row[0] "kind" <> "execution-causal-admission/1" then fail "causal identity is absent or duplicated"
        let factText = causalText row[0] "canonical"
        if causalHash (Encoding.UTF8.GetBytes factText) <> causalText row[0] "content_digest" then fail "causal fact digest differs"
        use fact = JsonDocument.Parse factText
        if causalText fact.RootElement "admissionSha256" <> causalHash case.raw
           || Convert.FromBase64String(causalText fact.RootElement "admissionBase64") <> case.raw then
            fail "causal declaration bytes changed in export"
    revision

let runCausal (args: string array) =
    if args.Length <> 6 then fail "usage: --causal-receiver ingest|reopen OUTPUT ROOT STORE-SHA FIXTURE-MANIFEST"
    let mode, output, root, storeSha, manifest = args[1], args[2], args[3], args[4], args[5]
    if mode <> "ingest" && mode <> "reopen" then fail "unknown causal receiver phase"
    if loadedDigest "FS.GG.Telemetry.Store" <> storeSha then fail "loaded causal Store bytes differ"
    if TelemetryStoreApplication.assessProductionRoot root <> TelemetryStore.ApprovedLocalDurable then
        fail "causal receiver root is not assessor-qualified"
    let cases, manifestSha = causalFixtures manifest
    if mode = "ingest" then
        if Directory.Exists root || File.Exists root then fail "causal root must be fresh"
        TelemetryStoreApplication.initialize root TelemetryStore.ApprovedLocalDurable |> unwrap |> ignore
        TelemetryStoreApplication.provisionReceiptWorkspace root TelemetryStore.ApprovedLocalDurable scope.Workspace |> unwrap |> ignore
        TelemetryStoreApplication.enrollReceiptProducer root TelemetryStore.ApprovedLocalDurable scope |> unwrap |> ignore
    elif not (Directory.Exists root) then fail "causal root must exist for fresh-process reopen"
    let before = if mode = "reopen" then Some(causalSnapshot root cases) else None
    for case in cases do
        let id = "causal-" + case.name
        let batch = causalBatch id (causalEvents cases case)
        let receipt = causalObject
                          [ "schema", causalNode TelemetryReceipt.Schema
                            "workspaceId", causalNode scope.Workspace; "producerId", causalNode scope.Producer
                            "streamId", causalNode scope.Stream; "batchId", causalNode id; "payload", batch ]
        TelemetryStoreApplication.submitReceipt root TelemetryStore.ApprovedLocalDurable scope (Encoding.UTF8.GetBytes(receipt.ToJsonString())) |> unwrap |> ignore
        use result = JsonDocument.Parse(TelemetryStoreApplication.drainReceipts root TelemetryStore.ApprovedLocalDurable scope.Workspace |> unwrap)
        if result.RootElement.GetProperty("rejected").GetInt32() <> 0 then fail "causal receipt was rejected"
    let revision = causalSnapshot root cases
    if before |> Option.exists ((<>) revision) then fail "causal receipt replay changed canonical revision"
    let changed = JsonNode.Parse(cases[0].event.GetRawText())
    let admission = JsonNode.Parse(cases[0].admission.GetRawText())
    admission["declaration"]["purpose"] <- causalNode "review"
    let changedBytes = Encoding.UTF8.GetBytes(admission.ToJsonString())
    changed["admissionBase64"] <- causalNode (Convert.ToBase64String changedBytes)
    changed["admissionSha256"] <- causalNode (causalHash changedBytes)
    let unknown = JsonNode.Parse(cases[0].event.GetRawText())
    unknown["kind"] <- causalNode "execution-causal-admission/999"
    for id, fact in [ "immutable-conflict", changed; "unknown-kind", unknown ] do
        let bytes = Encoding.UTF8.GetBytes((causalBatch id [| fact |]).ToJsonString())
        match TelemetryStoreApplication.ingest root TelemetryStore.ApprovedLocalDurable bytes with
        | Ok _ -> fail ("causal receiver accepted " + id)
        | Error _ -> ()
        if causalSnapshot root cases <> revision then fail "causal refusal changed canonical revision"
    File.WriteAllText(output, JsonSerializer.Serialize
        {| schema = "fsgg.telemetry.packaged-causal-receiver/1"; phase = mode; qualified = true
           provenance = "synthetic-fixed-contract-through-exact-packed-receiver"
           storeAssemblySha256 = storeSha; coreAssemblySha256 = loadedDigest "FS.GG.Coord.Core"
           fixtureManifestSha256 = manifestSha; canonicalRevision = revision; schemaVersion = 14
           causalDeclarations = 4; runtimeAdmissions = 4; expectedDispatches = 4; invocationLineage = 4
           immutableConflictRefused = true; unknownKindRefused = true; replayUnchanged = before.IsSome |} + "\n")

let runPerformance (args: string array) =

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

let args = fsi.CommandLineArgs |> Array.skip 1
if args.Length > 0 && args[0] = "--causal-receiver" then runCausal args else runPerformance args
