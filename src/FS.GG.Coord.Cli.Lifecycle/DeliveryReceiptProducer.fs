namespace FS.GG.Coord.Cli

open FS.GG.Coord
open FS.GG.Coord.GitHub

module DeliveryReceiptProducer =
    type Request =
        { PullRequest: int; HeadSha: string; ObligationId: string; Evidence: string }

    type Ports =
        { CurrentHead: unit -> Result<string, string>
          Comments: unit -> Result<Driver.ReviewComment list, string>
          AuthorizeWrite: unit -> Result<unit, string>
          WriteDurableComment: string -> string -> Result<unit, string> }

    type Outcome =
        | Written
        | AlreadyPresent

    let private observe (ports: Ports) (request: Request) =
        ports.Comments()
        |> Result.bind (DeliveryApplication.obligationsFromComments request.HeadSha)
        |> Result.bind (fun obligations ->
            match obligations |> List.tryFind (fun obligation -> obligation.Id = request.ObligationId) with
            | None -> Error $"delivery obligation {request.ObligationId} is undeclared"
            | Some obligation when obligation.HeadSha <> request.HeadSha -> Error "delivery obligation head is stale"
            | Some obligation -> Ok obligation)

    let private headCurrent (ports: Ports) (request: Request) =
        ports.CurrentHead()
        |> Result.bind (fun current ->
            if current = request.HeadSha then Ok()
            else Error $"delivery receipt head is stale: expected {request.HeadSha}, observed {current}")

    let produceWith (ports: Ports) (request: Request) =
        if request.PullRequest <= 0 then Error "delivery receipt pull request number is invalid"
        else
            DeliveryApplication.formatProspectiveReceipt request.ObligationId request.HeadSha request.Evidence
            |> Result.bind (fun body ->
                headCurrent ports request
                |> Result.bind (fun () -> observe ports request)
                |> Result.bind (fun obligation ->
                    match obligation.Evidence with
                    | Some evidence when evidence = request.Evidence ->
                        headCurrent ports request |> Result.map (fun () -> AlreadyPresent)
                    | Some _ -> Error "delivery obligation already has a conflicting receipt"
                    | None ->
                        headCurrent ports request
                        |> Result.bind (fun () -> ports.AuthorizeWrite())
                        // The authority callback can itself observe a newer state. Refresh both facts
                        // after it, at the final pre-POST boundary, without spending authority twice.
                        |> Result.bind (fun () -> headCurrent ports request)
                        |> Result.bind (fun () -> observe ports request)
                        |> Result.bind (fun fresh ->
                            match fresh.Evidence with
                            | Some evidence when evidence = request.Evidence ->
                                headCurrent ports request |> Result.map (fun () -> AlreadyPresent)
                            | Some _ -> Error "delivery obligation already has a conflicting receipt"
                            | None ->
                                // The whole leading marker is the durable slot. A different body at that
                                // slot is a conflict; the writer rereads after success and response loss.
                                let marker = $"<!-- fsgg:delivery-receipt id={request.ObligationId} head={request.HeadSha} "
                                let write = ports.WriteDurableComment marker body
                                // An attempted POST is not authority. Exact readback is mandatory.
                                headCurrent ports request
                                |> Result.bind (fun () -> observe ports request)
                                |> Result.bind (fun after ->
                                    match after.Evidence, write with
                                    | Some evidence, _ when evidence = request.Evidence -> Ok Written
                                    | Some _, _ -> Error "delivery receipt readback conflicts with requested evidence"
                                    | None, Error reason -> Error $"delivery receipt write failed and no exact readback exists: {reason}"
                                    | None, Ok () -> Error "delivery receipt write returned success without authoritative readback"))))

    let produceLive transport (target: Types.Ref) authorizeWrite request =
        if target.Number <> request.PullRequest then
            Error "delivery receipt target is not the requested pull request"
        else
            let ports =
                { CurrentHead =
                    (fun () ->
                        Reads.prHeadSha transport target.Owner target.Repo target.Number
                        |> Result.mapError Errors.explain)
                  Comments =
                    (fun () ->
                        Reads.commentsWithIdentity transport target.Owner target.Repo target.Number
                        |> Result.map (List.map (fun comment ->
                            ({ Id = comment.Id; Url = comment.Url; Body = comment.Body }: Driver.ReviewComment)))
                        |> Result.mapError Errors.explain)
                  AuthorizeWrite = authorizeWrite
                  WriteDurableComment =
                    (fun marker body ->
                        Writes.writeDurableComment transport target marker body
                        |> Result.map ignore
                        |> Result.mapError Errors.explain) }

            produceWith ports request
