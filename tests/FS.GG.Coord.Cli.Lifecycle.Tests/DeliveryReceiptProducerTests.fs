namespace FS.GG.Coord.Cli.Tests

open Xunit
open System
open System.Text.Json

open FS.GG.Coord
open FS.GG.Coord.Cli
open FS.GG.Coord.Cli.Lifecycle
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Transport

module DeliveryReceiptProducerTests =
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

    [<Fact>]
    let ``prospective receipt guards and readback`` () =
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

    [<Fact>]
    let ``head movement during authority check refuses before the writer`` () =
        let _, current, writes, ports = fixture ()
        let moved =
            { ports with AuthorizeWrite = fun () -> current.Value <- String.replicate 40 "b"; Ok() }
        expectError "stale" (DeliveryReceiptProducer.produceWith moved request)
        Assert.Equal(0, writes.Value)

    [<Fact>]
    let ``declaration removal during authority check refuses before the writer`` () =
        let ledger, _, writes, ports = fixture ()
        let removed =
            { ports with AuthorizeWrite = fun () -> ledger.Value <- []; Ok() }
        expectError "undeclared" (DeliveryReceiptProducer.produceWith removed request)
        Assert.Equal(0, writes.Value)

    [<Fact>]
    let ``prospective evidence rejects a final newline`` () =
        let _, _, writes, ports = fixture ()
        expectError "evidence" (DeliveryReceiptProducer.produceWith ports { request with Evidence = evidence + "\n" })
        Assert.Equal(0, writes.Value)

    let private ok body : Errors.IoResult<Response> =
        Ok { Status = 200; Body = body; ETag = None; NextLink = None; Headers = Map.empty }

    let private runLiveReceipt claimBody closingNumber =
        let mutable writes = 0
        let mutable closingReads = 0
        let comments =
            claimBody
            |> Option.map (fun body ->
                JsonSerializer.Serialize [| {| id = 1L; body = body; updated_at = DateTimeOffset.UtcNow.ToString("O") |} |])
            |> Option.defaultValue "[]"

        let transport =
            Fake.Recorder(fun req ->
                match req.Method, req.Path with
                | "GET", "repos/FS-GG/.github/issues/42/comments" -> ok comments
                | "POST", "graphql" ->
                    closingReads <- closingReads + 1
                    ok $"""{{"data":{{"repository":{{"pullRequest":{{"closingIssuesReferences":{{"totalCount":1,"nodes":[{{"number":{closingNumber},"repository":{{"nameWithOwner":"FS-GG/.github"}}}}]}}}}}}}}}}"""
                | "POST", _ ->
                    writes <- writes + 1
                    Error(Errors.NotFound "unexpected write")
                | _, _ -> Error(Errors.NotFound $"unexpected request {req.Method} {req.Path}"))

        let ctx : FS.GG.Coord.Cli.Kernel.Context =
            { Transport = transport; Owner = "FS-GG"; Title = "Coordination"; DefaultRepo = Some ".github"; ChoreLocks = [] }
        let opts =
            match Options.parse [ "delivery"; "42"; "receipt"; request.ObligationId; evidence; "--pr"; "900"; "--worker"; "smew-f1e2" ] with
            | Ok opts -> opts
            | Error reason -> failwithf "fixture argv failed: {reason}"
        let code =
            LiveHandlers.delivery Unchecked.defaultof<_> Unchecked.defaultof<_> Unchecked.defaultof<_>
                Unchecked.defaultof<_> Unchecked.defaultof<_> ctx opts
        code, writes, closingReads

    [<Fact>]
    let ``live receipt command refuses without this worker claim`` () =
        let code, writes, closingReads = runLiveReceipt None 42
        Assert.NotEqual(0, code)
        Assert.Equal(0, writes)
        Assert.Equal(0, closingReads)

    [<Fact>]
    let ``live receipt command refuses a PR closing another item`` () =
        let code, writes, closingReads = runLiveReceipt (Some "<!-- fsgg:claim worker=smew-f1e2 lease=120 -->") 43
        Assert.NotEqual(0, code)
        Assert.Equal(0, writes)
        Assert.Equal(1, closingReads)
