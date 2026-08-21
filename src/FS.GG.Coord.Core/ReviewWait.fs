namespace FS.GG.Coord

module ReviewWait =
    open System
    open System.Globalization
    open System.Text.Json

    [<Literal>]
    let Marker = "<!-- fsgg:review-wait/v1 -->"

    type Kind = InitialReview | RepairConfirmation

    type WaitReceipt =
        { Item: string
          ClaimGeneration: string
          ReviewGeneration: string
          Kind: Kind
          EnteredAt: DateTimeOffset
          ExpiresAt: DateTimeOffset
          EvidenceRef: string }

    type Transition =
        | Enter of WaitReceipt
        | Complete of reviewGeneration: string * at: DateTimeOffset * evidenceRef: string
        | Cancel of reviewGeneration: string * at: DateTimeOffset * evidenceRef: string
        | Timeout of reviewGeneration: string * at: DateTimeOffset * evidenceRef: string

    type State =
        | NoReceipt
        | Waiting of WaitReceipt
        | Completed of WaitReceipt * evidenceRef: string
        | Cancelled of WaitReceipt * evidenceRef: string
        | Recoverable of WaitReceipt * reason: string
        | Invalid of errors: string list

    let private blank name value =
        if String.IsNullOrWhiteSpace value then [ $"%s{name} is required" ] else []

    let validate event =
        let errors =
            match event with
            | Enter receipt ->
                [ yield! blank "item" receipt.Item
                  yield! blank "claimGeneration" receipt.ClaimGeneration
                  yield! blank "reviewGeneration" receipt.ReviewGeneration
                  yield! blank "evidenceRef" receipt.EvidenceRef
                  if receipt.ExpiresAt <= receipt.EnteredAt then
                      yield "expiresAt must be later than enteredAt"
                  if receipt.ExpiresAt - receipt.EnteredAt > TimeSpan.FromHours 24.0 then
                      yield "a review wait may be bounded for at most 24 hours" ]
            | Complete (generation, _, evidence)
            | Cancel (generation, _, evidence)
            | Timeout (generation, _, evidence) ->
                [ yield! blank "reviewGeneration" generation
                  yield! blank "evidenceRef" evidence ]
        if List.isEmpty errors then Ok event else Error errors

    let private kindName = function InitialReview -> "initial-review" | RepairConfirmation -> "repair-confirmation"
    let generationToken head kind round = $"%s{head}:%s{kindName kind}:%d{round}"
    let private instant (value: DateTimeOffset) = value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)

    let encode event =
        let payload: obj =
            match event with
            | Enter receipt ->
                box {| schema = "fsgg.coord.review-wait/v1"; event = "enter"; item = receipt.Item
                       claimGeneration = receipt.ClaimGeneration; reviewGeneration = receipt.ReviewGeneration
                       kind = kindName receipt.Kind; enteredAt = instant receipt.EnteredAt
                       expiresAt = instant receipt.ExpiresAt; evidenceRef = receipt.EvidenceRef |}
            | Complete (generation, at, evidence) ->
                box {| schema = "fsgg.coord.review-wait/v1"; event = "complete"; reviewGeneration = generation
                       at = instant at; evidenceRef = evidence |}
            | Cancel (generation, at, evidence) ->
                box {| schema = "fsgg.coord.review-wait/v1"; event = "cancel"; reviewGeneration = generation
                       at = instant at; evidenceRef = evidence |}
            | Timeout (generation, at, evidence) ->
                box {| schema = "fsgg.coord.review-wait/v1"; event = "timeout"; reviewGeneration = generation
                       at = instant at; evidenceRef = evidence |}
        Marker + "\n" + JsonSerializer.Serialize payload

    let private requiredString (name: string) (root: JsonElement) : string =
        match root.TryGetProperty name with
        | true, value when value.ValueKind = JsonValueKind.String && not (String.IsNullOrWhiteSpace(value.GetString())) -> value.GetString()
        | _ -> invalidArg name "must be a non-empty string"
    let private requiredInstant (name: string) (root: JsonElement) : DateTimeOffset =
        DateTimeOffset.Parse(requiredString name root, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)

    let tryDecode (body: string) =
        if not (body.StartsWith(Marker, StringComparison.Ordinal)) then Ok None else
        try
            use document = JsonDocument.Parse(body.Substring(Marker.Length).Trim())
            let root = document.RootElement
            if requiredString "schema" root <> "fsgg.coord.review-wait/v1" then invalidArg "schema" "must be fsgg.coord.review-wait/v1"
            let generation () = requiredString "reviewGeneration" root
            let at () = requiredInstant "at" root
            let evidence () = requiredString "evidenceRef" root
            let event =
                match requiredString "event" root with
                | "enter" ->
                    let kind = match requiredString "kind" root with | "initial-review" -> InitialReview | "repair-confirmation" -> RepairConfirmation | other -> invalidArg "kind" $"unknown kind '%s{other}'"
                    Enter { Item = requiredString "item" root; ClaimGeneration = requiredString "claimGeneration" root
                            ReviewGeneration = generation (); Kind = kind; EnteredAt = requiredInstant "enteredAt" root
                            ExpiresAt = requiredInstant "expiresAt" root; EvidenceRef = evidence () }
                | "complete" -> Complete(generation (), at (), evidence ())
                | "cancel" -> Cancel(generation (), at (), evidence ())
                | "timeout" -> Timeout(generation (), at (), evidence ())
                | other -> invalidArg "event" $"unknown event '%s{other}'"
            match validate event with Ok valid -> Ok(Some valid) | Error errors -> Error(String.concat "; " errors)
        with error -> Error error.Message

    let project item currentClaimGeneration prOpen now events =
        let errors = events |> List.choose (fun event -> match validate event with Ok _ -> None | Error e -> Some e) |> List.concat
        if not (List.isEmpty errors) then Invalid errors else
        // GitHub comment order is the authority: the first entry for one generation wins, so two
        // racing writers cannot let the later comment replace the receipt the earlier comment made
        // durable. A genuinely new generation may start only after the preceding entry has a terminal
        // transition; two simultaneously unconsumed generations are contradictory authority, never a
        // latest-wins queue.
        let entries =
            events
            |> List.choose (function Enter receipt when receipt.Item = item -> Some receipt | _ -> None)
            |> List.fold (fun (seen, ordered) receipt ->
                if Set.contains receipt.ReviewGeneration seen then seen, ordered
                else Set.add receipt.ReviewGeneration seen, ordered @ [ receipt ]) (Set.empty, [])
            |> snd
        let enteredGenerations = entries |> List.map _.ReviewGeneration |> Set.ofList
        let orphaned =
            events
            |> List.choose (function
                | Complete (generation, _, _)
                | Cancel (generation, _, _)
                | Timeout (generation, _, _) when not (Set.contains generation enteredGenerations) -> Some generation
                | _ -> None)
            |> List.distinct
        let terminalGenerations =
            events
            |> List.choose (function
                | Complete (generation, _, _)
                | Cancel (generation, _, _)
                | Timeout (generation, _, _) -> Some generation
                | _ -> None)
            |> Set.ofList
        let unconsumed =
            entries
            |> List.filter (fun receipt -> not (Set.contains receipt.ReviewGeneration terminalGenerations))
        match entries, unconsumed, orphaned with
        | _, _, generations when not (List.isEmpty generations) ->
            let names = String.concat ", " generations
            Invalid [ $"terminal transition has no entry receipt: %s{names}" ]
        | _, active, _ when List.length active > 1 ->
            let names = active |> List.map _.ReviewGeneration |> String.concat ", "
            Invalid [ $"multiple review generations are unconsumed: %s{names}" ]
        | [], _, _ -> NoReceipt
        | entries, active, _ ->
            let receipt = active |> List.tryExactlyOne |> Option.defaultWith (fun () -> List.last entries)
            // The first terminal comment wins. `at` is checked against the receipt boundary, but is
            // never used to reorder GitHub's append-only authority: a later caller cannot backdate a
            // comment to steal a completion/timeout race.
            let terminal =
                events
                |> List.tryPick (function
                    | Complete (g, at, evidence) when g = receipt.ReviewGeneration -> Some(0, at, evidence)
                    | Cancel (g, at, evidence) when g = receipt.ReviewGeneration -> Some(1, at, evidence)
                    | Timeout (g, at, evidence) when g = receipt.ReviewGeneration -> Some(2, at, evidence)
                    | _ -> None)
            match currentClaimGeneration, terminal with
            | generation, _ when generation <> Some receipt.ClaimGeneration ->
                Recoverable(receipt, "claim generation changed; reacquire before mutation")
            | _, Some (0, at, _) when at < receipt.EnteredAt -> Invalid [ "completion predates queue entry" ]
            | _, Some (0, at, evidence) when at <= receipt.ExpiresAt -> Completed(receipt, evidence)
            | _, Some (0, _, _) -> Recoverable(receipt, "completion arrived after bounded review wait expiry")
            | _, Some (1, at, _) when at < receipt.EnteredAt -> Invalid [ "cancellation predates queue entry" ]
            | _, Some (1, _, evidence) -> Cancelled(receipt, evidence)
            | _, Some (2, at, _) when at < receipt.ExpiresAt -> Invalid [ "timeout predates expiresAt" ]
            | _, Some (2, _, evidence) -> Recoverable(receipt, evidence)
            | _, _ when not prOpen -> Cancelled(receipt, "pull request is closed")
            | _, _ when now >= receipt.ExpiresAt -> Recoverable(receipt, "bounded review wait expired")
            | _, _ -> Waiting receipt
