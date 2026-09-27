#r "../../src/FS.GG.Coord.Cli.Lifecycle/bin/Debug/net10.0/FS.GG.Coord.Core.dll"
#r "../../src/FS.GG.Coord.Cli.Lifecycle/bin/Debug/net10.0/FS.GG.Coord.GitHub.dll"
#r "../../src/FS.GG.Coord.Cli.Lifecycle/bin/Debug/net10.0/FS.GG.Coord.Cli.Kernel.dll"
#r "../../src/FS.GG.Coord.Cli.Lifecycle/bin/Debug/net10.0/FS.GG.Coord.Cli.Lifecycle.dll"

open FS.GG.Coord
open FS.GG.Coord.Cli

let head = String.replicate 40 "a"
let evidence = "https://example.test/artifacts/1"
let request : DeliveryReceiptProducer.Request =
    { PullRequest = 42; HeadSha = head; ObligationId = "kit-1.0.0"; Evidence = evidence }

let comment id body : Driver.ReviewComment = { Id = id; Url = $"https://example.test/comments/{id}"; Body = body }
let declaration = comment 1 $"<!-- fsgg:delivery-obligation id=kit-1.0.0 kind=package head={head} -->"
let receipt value = comment 2 $"<!-- fsgg:delivery-receipt id=kit-1.0.0 head={head} evidence={value} -->"
let expectError (phrase: string) (result: Result<DeliveryReceiptProducer.Outcome, string>) =
    match result with
    | Error reason when reason.Contains phrase -> ()
    | other -> failwithf "expected refusal containing %s, got %A" phrase other

let fixture () =
    let ledger = ref [ declaration ]
    let current = ref head
    let writes = ref 0
    let ports : DeliveryReceiptProducer.Ports =
        { CurrentHead = fun () -> Ok current.Value
          Comments = fun () -> Ok ledger.Value
          AuthorizeWrite = fun () -> Ok()
          WriteDurableComment = fun _ body ->
              writes.Value <- writes.Value + 1
              ledger.Value <- ledger.Value @ [ comment 2 body ]
              Ok() }
    ledger, current, writes, ports

do
    let ledger, _, writes, ports = fixture ()
    match DeliveryReceiptProducer.produceWith ports request with
    | Ok DeliveryReceiptProducer.Written -> ()
    | other -> failwithf "expected written, got %A" other
    if writes.Value <> 1 || ledger.Value |> List.length <> 2 then failwith "write or readback missing"
    match DeliveryReceiptProducer.produceWith ports request with
    | Ok DeliveryReceiptProducer.AlreadyPresent when writes.Value = 1 -> ()
    | other -> failwithf "idempotent replay failed: %A" other

    let _, current, writes, ports = fixture ()
    current.Value <- String.replicate 40 "b"
    expectError "stale" (DeliveryReceiptProducer.produceWith ports request)
    if writes.Value <> 0 then failwith "stale head wrote"

    let ledger, _, writes, ports = fixture ()
    ledger.Value <- []
    expectError "undeclared" (DeliveryReceiptProducer.produceWith ports request)
    if writes.Value <> 0 then failwith "undeclared obligation wrote"

    let _, _, writes, ports = fixture ()
    expectError "evidence" (DeliveryReceiptProducer.produceWith ports { request with Evidence = "javascript:alert(1)" })
    if writes.Value <> 0 then failwith "malformed evidence wrote"

    let ledger, _, writes, ports = fixture ()
    ledger.Value <- [ declaration; receipt evidence; comment 3 (receipt "https://example.test/artifacts/2").Body ]
    expectError "duplicate or conflicting" (DeliveryReceiptProducer.produceWith ports request)
    if writes.Value <> 0 then failwith "conflicting receipt wrote"

    let ledger, _, _, ports = fixture ()
    let lostResponse =
        { ports with WriteDurableComment = fun _ body -> ledger.Value <- ledger.Value @ [ comment 2 body ]; Error "POST response lost" }
    match DeliveryReceiptProducer.produceWith lostResponse request with
    | Ok DeliveryReceiptProducer.Written -> ()
    | other -> failwithf "lost response did not converge on readback: %A" other

    let _, _, _, ports = fixture ()
    let noReadback = { ports with WriteDurableComment = fun _ _ -> Ok() }
    expectError "without authoritative readback" (DeliveryReceiptProducer.produceWith noReadback request)

    let _, current, _, ports = fixture ()
    let movedAfterPost =
        { ports with WriteDurableComment = fun _ _ -> current.Value <- String.replicate 40 "c"; Ok() }
    expectError "stale" (DeliveryReceiptProducer.produceWith movedAfterPost request)

    let _, _, writes, ports = fixture ()
    let revoked = { ports with AuthorizeWrite = fun () -> Error "claim revoked" }
    expectError "claim revoked" (DeliveryReceiptProducer.produceWith revoked request)
    if writes.Value <> 0 then failwith "revoked authority wrote"

    let ledger, _, writes, ports = fixture ()
    ledger.Value <- [ declaration; receipt "https://example.test/other" ]
    expectError "conflicting receipt" (DeliveryReceiptProducer.produceWith ports request)
    if writes.Value <> 0 then failwith "conflicting evidence wrote"

    let _, _, writes, ports = fixture ()
    let unreadable = { ports with Comments = fun () -> Error "comment census unavailable" }
    expectError "comment census unavailable" (DeliveryReceiptProducer.produceWith unreadable request)
    if writes.Value <> 0 then failwith "unreadable comments wrote"

    let ledger, _, writes, ports = fixture ()
    ledger.Value <- [ comment 1 ("quoted example\n" + declaration.Body) ]
    expectError "undeclared" (DeliveryReceiptProducer.produceWith ports request)
    if writes.Value <> 0 then failwith "quoted declaration wrote"

    let wrongTarget : Types.Ref = { Owner = "FS-GG"; Repo = ".github"; Number = 43 }
    expectError "not the requested pull request"
        (DeliveryReceiptProducer.produceLive Unchecked.defaultof<_> wrongTarget (fun () -> Ok()) request)

    printfn "DeliveryReceiptProducer: 13 scenarios passed"
