#r "FS.GG.Coord.Core.dll"
#r "FS.GG.Telemetry.Store.dll"

open System
open System.Diagnostics
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json
open FS.GG.Coord
open FS.GG.Coord.Cli

let fail message = raise (InvalidOperationException message)
let unwrap = function Ok value -> value | Error errors -> fail (String.concat "; " errors)

let percentile95 (values: float array) =
    if values.Length = 0 then fail "performance samples are empty"
    values |> Array.sort |> Array.item (int (Math.Ceiling(0.95 * float values.Length)) - 1)

let summary (values: float array) =
    let ordered = Array.sort values
    let middle = ordered.Length / 2
    let median = if ordered.Length % 2 = 0 then (ordered[middle - 1] + ordered[middle]) / 2.0 else ordered[middle]
    {| medianMilliseconds = Math.Round(median, 3)
       p95Milliseconds = Math.Round(percentile95 values, 3)
       maximumMilliseconds = Math.Round(Array.max values, 3) |}

let scope: TelemetryReceipt.Scope =
    { Workspace = "standalone-performance"
      Producer = "packaged-store-probe"
      Stream = "qualification" }

let envelope series index =
    let filler = String('x', 61 * 1024)
    let ingestSchema = "fsgg.telemetry." + "ingest/1"
    Encoding.UTF8.GetBytes(
        $"""{{"schema":"fsgg.telemetry.envelope/1","workspaceId":"{scope.Workspace}","producerId":"{scope.Producer}","streamId":"{scope.Stream}","batchId":"{series}-{index:D3}","payload":{{"schema":"{ingestSchema}","ingestId":"{series}-{index:D3}","sourceIdentity":"packaged-store-probe","generation":"{series}","cursor":"{index}","eventCount":1,"events":[{{"kind":"item","identity":"{series}-{index:D3}-{filler}","itemId":"standalone-performance","revision":{index}}}]}}}}""")

let elapsed action =
    let timer = Stopwatch.StartNew()
    action ()
    timer.Stop()
    Math.Round(timer.Elapsed.TotalMilliseconds, 3)

let initialize root =
    match TelemetryStoreApplication.assessProductionRoot root with
    | TelemetryStore.ApprovedLocalDurable -> ()
    | assessment -> fail $"packaged Store probe root is not assessor-qualified: {assessment}"
    TelemetryStoreApplication.initialize root TelemetryStore.ApprovedLocalDurable |> unwrap |> ignore
    TelemetryStoreApplication.enrollReceiptProducer root TelemetryStore.ApprovedLocalDurable scope |> unwrap |> ignore

let submit root series index =
    TelemetryStoreApplication.submitReceipt root TelemetryStore.ApprovedLocalDurable scope (envelope series index)
    |> unwrap
    |> ignore

let drain root =
    TelemetryStoreApplication.drainReceipts root TelemetryStore.ApprovedLocalDurable scope.Workspace
    |> unwrap
    |> ignore

let args = fsi.CommandLineArgs |> Array.skip 1
if args.Length <> 4 then fail "usage: store-probe.fsx OUTPUT ROOT STORE-ASSEMBLY-SHA SAMPLE-COUNT"
let output, root, storeAssemblySha256, sampleText = args[0], args[1], args[2], args[3]
let sampleCount = Int32.Parse sampleText
if sampleCount <> 100 then fail "exactly 100 Store samples are required"
if Directory.Exists root || File.Exists root then fail "Store probe root must not exist"

let steadyRoot = Path.Combine(root, "steady")
let backlogRoot = Path.Combine(root, "backlog")
try
    initialize steadyRoot
    for index in 0..4 do
        submit steadyRoot "warmup" index
        drain steadyRoot
    let admission = Array.zeroCreate<float> sampleCount
    let application = Array.zeroCreate<float> sampleCount
    for index in 0..sampleCount - 1 do
        admission[index] <- elapsed (fun () -> submit steadyRoot "steady" index)
        application[index] <- elapsed (fun () -> drain steadyRoot)

    initialize backlogRoot
    let backlog =
        [| for index in 0..sampleCount - 1 ->
            elapsed (fun () -> submit backlogRoot "backlog" index) |]
    let windows =
        backlog
        |> Array.chunkBySize 20
        |> Array.mapi (fun index values ->
            {| firstSample = index * 20 + 1
               lastSample = index * 20 + values.Length
               summary = summary values |})
    let admissionSummary = summary admission
    let limit = 100.0
    let result =
        {| schema = "fsgg.telemetry.packaged-store-performance/1"
           qualified = admissionSummary.p95Milliseconds <= limit
           storeAssemblySha256 = storeAssemblySha256
           assessment = "approved-local-durable"
           samples =
             {| warmInProcessAdmissionMilliseconds = admission
                applicationDrainMilliseconds = application
                accumulatingPendingAdmissionMilliseconds = backlog |}
           summary =
             {| warmInProcessAdmission = admissionSummary
                applicationDrain = summary application
                accumulatingPendingAdmission = summary backlog
                accumulatingPendingWindows = windows |}
           limits = {| warmInProcessAdmissionP95Milliseconds = limit |} |}
    File.WriteAllText(output, JsonSerializer.Serialize result + "\n")
    printfn "%s" (JsonSerializer.Serialize {| qualified = result.qualified; summary = result.summary |})
    if not result.qualified then Environment.ExitCode <- 1
finally
    if Directory.Exists root then Directory.Delete(root, true)
