namespace FS.GG.Coord.Cli

module ReviewApplication =
    open System
    open System.IO
    open System.Text.Json
    open FS.GG.Coord
    open FS.GG.Coord.Cli.Options

    let private eprint (message: string) = Console.Error.WriteLine(message)

    let private input opts =
        match opts.SnapshotFile with
        | Some path -> File.ReadAllText path
        | None -> Console.In.ReadToEnd()

    let private required (name: string) (element: JsonElement) : JsonElement =
        match element.TryGetProperty name with
        | true, value -> value
        | _ -> invalidArg name "required field is missing"

    let private readString (name: string) (element: JsonElement) : string =
        let value = required name element
        if value.ValueKind <> JsonValueKind.String then invalidArg name "must be a string"
        let parsed = value.GetString()
        if String.IsNullOrWhiteSpace parsed then invalidArg name "must not be empty"
        parsed

    let private readInteger (name: string) (element: JsonElement) : int =
        let value = required name element
        match value.TryGetInt32() with
        | true, parsed -> parsed
        | _ -> invalidArg name "must be a 32-bit integer"

    let private readInt64 (name: string) (element: JsonElement) : int64 =
        let value = required name element
        match value.TryGetInt64() with
        | true, parsed -> parsed
        | _ -> invalidArg name "must be a 64-bit integer"

    let private readBoolean (name: string) (element: JsonElement) : bool =
        let value = required name element
        match value.ValueKind with
        | JsonValueKind.True -> true
        | JsonValueKind.False -> false
        | _ -> invalidArg name "must be a boolean"

    // The seven-word `PrState` wire vocabulary `Landable.name` renders — read here in reverse. Not a
    // second authority: `Landable.name` is forward-only (`PrState -> string`), so the reverse parser is
    // this boundary's own job, exactly as `DeliveryApplication`'s `stage`/`action` readers are.
    let private checks (name: string) (element: JsonElement) : Types.PrState =
        match readString name element with
        | "green" -> Types.PrGreen
        | "conflicted" -> Types.PrConflicted
        | "pending" -> Types.PrPending
        | "red" -> Types.PrRed
        | "unknown" -> Types.PrUnknown
        | "merged" -> Types.PrMerged
        | "closed" -> Types.PrClosed
        | other -> invalidArg name $"must be one of green/conflicted/pending/red/unknown/merged/closed, got '%s{other}'"

    let private phase (name: string) (element: JsonElement) : Review.Phase =
        match readString name element with
        | "ordinary" -> Review.Ordinary
        | "repair" -> Review.Repair
        | other -> invalidArg name $"must be 'ordinary' or 'repair', got '%s{other}'"

    let private binding (element: JsonElement) : Review.Binding =
        { ItemRef = readString "itemRef" element
          Pr = readInteger "pr" element
          HeadSha = readString "headSha" element
          ClaimGeneration = readString "claimGeneration" element
          ImplementerIdentity = readString "implementerIdentity" element
          Phase = phase "phase" element
          Round = readInteger "round" element }

    let private comments (element: JsonElement) : Driver.ReviewComment list =
        let value = required "comments" element
        if value.ValueKind <> JsonValueKind.Array then invalidArg "comments" "must be an array"
        value.EnumerateArray()
        |> Seq.map (fun comment ->
            if comment.ValueKind <> JsonValueKind.Object then invalidArg "comments" "must contain objects"
            ({ Id = readInt64 "id" comment
               Url = readString "url" comment
               Body = readString "body" comment }: Driver.ReviewComment))
        |> List.ofSeq

    let private repairPhaseReceipt (element: JsonElement) : Review.RepairPhaseReceipt =
        { ExhaustedPr = readInteger "exhaustedPr" element
          EscalationCommentId = readInt64 "escalationCommentId" element
          NewClaimGeneration = readString "newClaimGeneration" element
          NewBranchOrPr = readString "newBranchOrPr" element
          NewImplementerIdentity = readString "newImplementerIdentity" element
          NewCriticIdentity = readString "newCriticIdentity" element
          CandidateHeadSha = readString "candidateHeadSha" element }

    let private repairPhaseGranted (element: JsonElement) : Review.RepairPhaseReceipt option =
        let value = required "repairPhaseGranted" element
        match value.ValueKind with
        | JsonValueKind.Null -> None
        | JsonValueKind.Object -> Some(repairPhaseReceipt value)
        | _ -> invalidArg "repairPhaseGranted" "must be an object or null"

    // UNLIKE `required`, absence is not an error here — `criticSuccessionGranted` is additive
    // (.github#2417 FR-005): a snapshot producer that predates this field, or one that simply has no
    // grant to report, omits the key entirely, and that MUST parse exactly as it always has rather than
    // forcing every existing caller to start emitting an explicit `"criticSuccessionGranted": null`.
    let private optionalProperty (name: string) (element: JsonElement) : JsonElement option =
        match element.TryGetProperty name with
        | true, value when value.ValueKind <> JsonValueKind.Null -> Some value
        | _ -> None

    let private criticSuccessionReceipt (element: JsonElement) : Review.CriticSuccessionReceipt =
        { OriginalCriticIdentity = readString "originalCriticIdentity" element
          SuccessorCriticIdentity = readString "successorCriticIdentity" element
          GrantedBy = readString "grantedBy" element
          Reason = readString "reason" element
          CandidateHeadSha = readString "candidateHeadSha" element }

    let private criticSuccessionGranted (element: JsonElement) : Review.CriticSuccessionReceipt option =
        match optionalProperty "criticSuccessionGranted" element with
        | Some value when value.ValueKind = JsonValueKind.Object -> Some(criticSuccessionReceipt value)
        | Some _ -> invalidArg "criticSuccessionGranted" "must be an object or null"
        | None -> None

    let private repairAssertionReceipt (element: JsonElement) : Review.RepairAssertionReceipt =
        { AnsweredReviewUrl = readString "answeredReviewUrl" element
          CandidateHeadSha = readString "candidateHeadSha" element
          GrantedBy = readString "grantedBy" element
          Reason = readString "reason" element }

    // Additive exactly as `criticSuccessionGranted` is (.github#2549): a snapshot producer that
    // predates this field, or one with no grant to report, omits the key and MUST parse exactly as it
    // always has. A present-but-not-an-object value is still an error rather than a silent `None` —
    // a malformed grant must never read as "no grant was offered", because those two lead to
    // different, and in the refusing direction identical-looking, next actions.
    let private repairAssertionGranted (element: JsonElement) : Review.RepairAssertionReceipt option =
        match optionalProperty "repairAssertionGranted" element with
        | Some value when value.ValueKind = JsonValueKind.Object -> Some(repairAssertionReceipt value)
        | Some _ -> invalidArg "repairAssertionGranted" "must be an object or null"
        | None -> None

    // `DiffAuditTrusted` is not yet on this wire contract — the pure snapshot path this command serves
    // (a worker or the #2135 event projection asking "what next") does not need the live, engine-
    // recomputed diff-audit inventory `Driver.parseReviewCommentsWithFacts` optionally consumes; a
    // caller that mechanically requires the diff-audit gate goes through `Driver.parseReviewCommentsWithAudit`
    // directly, as the existing live `delivery` path already does. Always `None` here; a future cut can
    // add the field additively without breaking this one.
    //
    // `criticSuccessionGranted` (.github#2417) is read from the SAME `facts` JSON object on the wire —
    // callers author it alongside `repairPhaseGranted` — but it is NOT a `Review.Facts` field (see that
    // type's own doc comment for why); it is threaded separately below as its own tuple member.
    let private facts (element: JsonElement) : Review.Facts =
        { Comments = comments element
          Checks = checks "checks" element
          RepairPhaseGranted = repairPhaseGranted element
          RepairRouteAvailable = readBoolean "repairRouteAvailable" element
          DiffAuditTrusted = None }

    let private snapshot
        (raw: string)
        : Result<
            Review.Binding
            * Review.Facts
            * Review.CriticSuccessionReceipt option
            * Review.RepairAssertionReceipt option,
            string
          > =
        try
            use document = JsonDocument.Parse raw
            let root = document.RootElement
            if root.ValueKind <> JsonValueKind.Object then invalidArg "snapshot" "must be an object"
            let bindingElement = required "binding" root
            if bindingElement.ValueKind <> JsonValueKind.Object then invalidArg "binding" "must be an object"
            let factsElement = required "facts" root
            if factsElement.ValueKind <> JsonValueKind.Object then invalidArg "facts" "must be an object"
            Ok(
                binding bindingElement,
                facts factsElement,
                criticSuccessionGranted factsElement,
                repairAssertionGranted factsElement
            )
        with error -> Error error.Message

    let private stateName (value: Review.State) =
        match value with
        | Review.AwaitingInitialReview -> "awaitingInitialReview"
        | Review.ChangesRequiringRepair _ -> "changesRequiringRepair"
        | Review.AwaitingImplementerRepair _ -> "awaitingImplementerRepair"
        | Review.AwaitingSuccessorReview _ -> "awaitingSuccessorReview"
        | Review.PassedAwaitingChecks -> "passedAwaitingChecks"
        | Review.AwaitingHostAcceptance -> "awaitingHostAcceptance"
        | Review.AcceptedAwaitingChecks _ -> "acceptedAwaitingChecks"
        | Review.OrdinaryExhaustion -> "ordinaryExhaustion"
        | Review.RepairPhaseSetup -> "repairPhaseSetup"
        | Review.RepairPhaseActive _ -> "repairPhaseActive"
        | Review.Accepted -> "accepted"
        | Review.TerminalHumanPark _ -> "terminalHumanPark"
        | Review.MalformedEvidence _ -> "malformedEvidence"
        | Review.GuardViolation _ -> "guardViolation"

    let private stateRound (value: Review.State) : int option =
        match value with
        | Review.ChangesRequiringRepair round
        | Review.AwaitingImplementerRepair round
        | Review.AwaitingSuccessorReview round
        | Review.RepairPhaseActive round -> Some round
        | _ -> None

    let private stateReason (value: Review.State) : string option =
        match value with
        | Review.TerminalHumanPark reason
        | Review.GuardViolation reason -> Some reason
        // .github#2549. The check word is rendered through `Landable.name` — the SAME forward-only
        // `PrState -> string` vocabulary the `checks` reader above parses in reverse — so the state a
        // consumer reads back is spelled exactly as the one it supplied.
        | Review.AcceptedAwaitingChecks checks ->
            Some
                $"the review chain is complete and accepted at this head; the pull request's checks are '%s{Landable.name checks}'"
        | _ -> None

    let private stateErrors (value: Review.State) : string list option =
        match value with
        | Review.MalformedEvidence errors -> Some errors
        | _ -> None

    let private actionName (value: Review.NextAction) =
        match value with
        | Review.DispatchCritic -> "dispatchCritic"
        | Review.ResumeImplementer _ -> "resumeImplementer"
        | Review.DispatchSuccessor _ -> "dispatchSuccessor"
        | Review.AwaitChecks -> "awaitChecks"
        | Review.AuthorizeDelivery _ -> "authorizeDelivery"
        | Review.RequestHostAcceptance -> "requestHostAcceptance"
        | Review.EnterRepairPhase _ -> "enterRepairPhase"
        | Review.EnterCriticSuccession _ -> "enterCriticSuccession"
        | Review.Accept _ -> "accept"
        | Review.Park _ -> "park"

    let private actionReason (value: Review.NextAction) : string option =
        match value with
        | Review.ResumeImplementer reason
        | Review.DispatchSuccessor reason
        | Review.AuthorizeDelivery reason
        | Review.Park reason -> Some reason
        | _ -> None

    let private receiptJson (receipt: Review.RepairPhaseReceipt) =
        {| exhaustedPr = receipt.ExhaustedPr
           escalationCommentId = receipt.EscalationCommentId
           newClaimGeneration = receipt.NewClaimGeneration
           newBranchOrPr = receipt.NewBranchOrPr
           newImplementerIdentity = receipt.NewImplementerIdentity
           newCriticIdentity = receipt.NewCriticIdentity
           candidateHeadSha = receipt.CandidateHeadSha |}

    let private criticSuccessionJson (receipt: Review.CriticSuccessionReceipt) =
        {| originalCriticIdentity = receipt.OriginalCriticIdentity
           successorCriticIdentity = receipt.SuccessorCriticIdentity
           grantedBy = receipt.GrantedBy
           reason = receipt.Reason
           candidateHeadSha = receipt.CandidateHeadSha |}

    // .github#2527. Serialized on EVERY verdict, empty where nothing retired, so a consumer reads one
    // stable shape rather than a key that appears only in the recovery case. This is the fact that
    // answers "why is a pull request carrying two initial review markers being judged on the later
    // one" — without it the recovery is correct but unexplained, which for a rule whose whole job is to
    // stop a stranger continuing someone else's chain is not good enough.
    let private retiredChainJson (retired: Driver.ChainRetirement) =
        {| initialReview = retired.InitialReviewUrl
           initialReviewCommentId = retired.InitialReviewCommentId
           acceptedHead = retired.AcceptedHead
           acceptanceCommentId = retired.AcceptanceCommentId |}

    let private acceptedJson (receipt: Review.AcceptedReceipt) =
        {| headSha = receipt.HeadSha
           criticIdentity = receipt.CriticIdentity
           rounds = receipt.Rounds
           repairPhase = receipt.RepairPhase
           checksGreen = receipt.ChecksGreen
           diffAuditRequired = receipt.DiffAuditRequired
           diffAuditHead = receipt.DiffAuditHead |}

    let private waitProjection = function
        | None -> None, None, None
        | Some ReviewWait.NoReceipt -> Some "noReceipt", None, None
        | Some (ReviewWait.Waiting receipt) -> Some "waiting", Some receipt, None
        | Some (ReviewWait.Completed (receipt, evidence)) -> Some "completed", Some receipt, Some evidence
        | Some (ReviewWait.Cancelled (receipt, evidence)) -> Some "cancelled", Some receipt, Some evidence
        | Some (ReviewWait.Recoverable (receipt, reason)) -> Some "recoverable", Some receipt, Some reason
        | Some (ReviewWait.Invalid errors) -> Some "invalid", None, Some(String.concat "; " errors)

    let private waitReceiptJson (receipt: ReviewWait.WaitReceipt) =
        {| item = receipt.Item
           claimGeneration = receipt.ClaimGeneration
           reviewGeneration = receipt.ReviewGeneration
           kind = match receipt.Kind with ReviewWait.InitialReview -> "initial-review" | ReviewWait.RepairConfirmation -> "repair-confirmation"
           enteredAt = receipt.EnteredAt
           expiresAt = receipt.ExpiresAt
           evidenceRef = receipt.EvidenceRef |}

    let private isCompletedOrdinaryExhaustion (binding: Review.Binding) (facts: Review.Facts) (waitState: ReviewWait.State option) =
        match binding.Phase, waitState with
        | Review.Ordinary, Some (ReviewWait.Completed (receipt, _)) ->
            receipt.Kind = ReviewWait.RepairConfirmation
            && receipt.ClaimGeneration <> binding.ClaimGeneration
            && receipt.ReviewGeneration =
                ReviewWait.generationToken
                    binding.HeadSha
                    ReviewWait.RepairConfirmation
                    Protocol.reviewPolicy.MaxAutomatedRepairRounds
            && Review.isOrdinaryExhaustionTerminal binding.HeadSha facts.Checks facts.Comments
        | _ -> false

    let private hasRecordedRepairPhaseEntry (binding: Review.Binding) (facts: Review.Facts) (waitState: ReviewWait.State option) =
        let phaseFacts = Driver.reviewPhaseFacts facts.Comments
        isCompletedOrdinaryExhaustion binding facts waitState && phaseFacts.EscalationPresent

    let private waitAuthority (binding: Review.Binding) (facts: Review.Facts) (state: Review.State) (action: Review.NextAction) (waitState: ReviewWait.State option) =
        let dispatchAuthority =
            match action with
            | Review.DispatchCritic -> Some(ReviewWait.InitialReview, ReviewWait.generationToken binding.HeadSha ReviewWait.InitialReview 0)
            | Review.DispatchSuccessor _ ->
                let round = stateRound state |> Option.defaultValue binding.Round
                Some(ReviewWait.RepairConfirmation, ReviewWait.generationToken binding.HeadSha ReviewWait.RepairConfirmation round)
            | _ -> None
        match waitState, dispatchAuthority with
        | _ when hasRecordedRepairPhaseEntry binding facts waitState ->
            Error
                [ "the structured ordinary-exhaustion escalation is recorded; enter the fresh repair phase "
                  + "instead of dispatching, resuming, accepting, or manufacturing ordinary round four on the exhausted pull request" ]
        | None, _ -> Ok () // offline snapshots predate the live durable-wait projection
        | Some (ReviewWait.Invalid errors), _ -> Error errors
        | Some (ReviewWait.Recoverable (_, reason)), _ -> Error [ reason ]
        | Some (ReviewWait.Waiting receipt), Some(expectedKind, expectedGeneration)
            when receipt.Kind = expectedKind && receipt.ReviewGeneration = expectedGeneration -> Ok ()
        | Some (ReviewWait.Waiting receipt), Some(_, expectedGeneration) ->
            Error [ $"the durable review wait does not authorize %s{actionName action}: expected generation '%s{expectedGeneration}', got '%s{receipt.ReviewGeneration}' / %A{receipt.Kind}" ]
        | Some (ReviewWait.Waiting receipt), None ->
            Error [ $"review generation '%s{receipt.ReviewGeneration}' remains unconsumed; record its completion, cancellation, or timeout before advancing" ]
        | Some ReviewWait.NoReceipt, Some _ ->
            Error [ $"%s{actionName action} requires a durable review-wait entry before dispatch" ]
        | Some (ReviewWait.Completed _), Some _
        | Some (ReviewWait.Cancelled _), Some _ ->
            Error [ $"%s{actionName action} requires a new durable review-wait entry for this generation" ]
        | Some (ReviewWait.Cancelled (_, reason)), None -> Error [ $"the durable review wait was cancelled: %s{reason}" ]
        | Some ReviewWait.NoReceipt, None
        | Some (ReviewWait.Completed _), None -> Ok ()

    // `render`'s own public 3-arg shape (`Options -> Review.Binding -> Review.Facts -> int`) is a fixed
    // contract other callers depend on positionally — most importantly `Client.review`'s live
    // `review <ref> --pr N` path, which calls `ReviewApplication.render opts binding facts` as a tail
    // expression whose type must be `int`. This private helper carries the actual rendering logic plus
    // the one extra fact (.github#2417) that path never supplies; `render` below is a thin wrapper that
    // always passes `None`, and `run` (the `--snapshot` path, which DOES parse a grant) calls this
    // directly with the parsed value — so neither existing caller's signature changes.
    let private renderVerdict
        (opts: Options)
        (binding: Review.Binding)
        (facts: Review.Facts)
        (successionGranted: Review.CriticSuccessionReceipt option)
        (repairAssertionGranted: Review.RepairAssertionReceipt option)
        (waitState: ReviewWait.State option)
        : int =
        match Review.inspect binding facts successionGranted repairAssertionGranted with
        | Error reasons ->
            match opts.Render with
            | Json ->
                printfn
                    "%s"
                    (JsonSerializer.Serialize {| schema = "fsgg.coord.review/1"; verdict = "noVerdict"; reasons = reasons |})
            | Text -> reasons |> List.iter (fun reason -> eprint $"UNDETERMINED — %s{reason}")
            ExitCode.toInt ExitCode.NoVerdict
        | Ok verdict ->
            let verdict =
                if isCompletedOrdinaryExhaustion binding facts waitState then
                    Review.projectCompletedOrdinaryExhaustion binding facts verdict
                else
                    verdict
            let waitStatus, waitReceipt, waitReason = waitProjection waitState
            let waitStatus =
                if hasRecordedRepairPhaseEntry binding facts waitState then Some "repairPhaseEntry"
                elif isCompletedOrdinaryExhaustion binding facts waitState then Some "ordinaryExhaustion"
                else waitStatus
            match waitAuthority binding facts verdict.State verdict.NextAction waitState, opts.Render with
            | Error reasons, Json ->
                printfn
                    "%s"
                    (JsonSerializer.Serialize
                        {| schema = "fsgg.coord.review/1"
                           verdict = "noVerdict"
                           reasons = reasons
                           waitStatus = waitStatus
                           waitReceipt = waitReceipt |> Option.map waitReceiptJson
                           waitReason = waitReason |})
                ExitCode.toInt ExitCode.NoVerdict
            | Error reasons, Text ->
                reasons |> List.iter (fun reason -> eprint $"UNDETERMINED — %s{reason}")
                ExitCode.toInt ExitCode.NoVerdict
            | Ok (), Json ->
                let payload =
                    {| schema = "fsgg.coord.review/1"
                       verdict = "next"
                       state = stateName verdict.State
                       stateRound = stateRound verdict.State
                       stateReason = stateReason verdict.State
                       stateErrors = stateErrors verdict.State
                       action = actionName verdict.NextAction
                       actionReason = actionReason verdict.NextAction
                       repairPhaseReceipt =
                        match verdict.NextAction with
                        | Review.EnterRepairPhase receipt -> Some(receiptJson receipt)
                        | _ -> None
                       criticSuccessionReceipt =
                        match verdict.NextAction with
                        | Review.EnterCriticSuccession receipt -> Some(criticSuccessionJson receipt)
                        | _ -> None
                       acceptedReceipt =
                        match verdict.NextAction with
                        | Review.Accept receipt -> Some(acceptedJson receipt)
                        | _ -> None
                       waitStatus = waitStatus
                       waitReceipt = waitReceipt |> Option.map waitReceiptJson
                       waitReason = waitReason
                       retiredChains = verdict.RetiredChains |> List.map retiredChainJson
                       freshnessToken = verdict.FreshnessToken
                       actionKey = verdict.ActionKey |}
                printfn "%s" (JsonSerializer.Serialize payload)
                ExitCode.toInt ExitCode.Green
            // .github#2487 AC3 reaches the TEXT projection too, because "a reader can act without a
            // manual `git` comparison" is not a property of one render mode. The reason the JSON payload
            // has always carried as `actionReason` was simply dropped here, so `--text` printed
            // `passedAwaitingChecks — awaitChecks` for a chain whose only pass bound an abandoned head:
            // the exact two words that read as "you are clear, finish the cycle", and nothing else.
            //
            // Strictly additive: the state and action words, their order and their separator are
            // byte-for-byte unchanged, and the reason is appended only where one exists — so every
            // verdict that carried no reason before still renders exactly one line of exactly the same
            // two words.
            | Ok (), Text ->
                match actionReason verdict.NextAction with
                | Some reason ->
                    printfn "%s — %s: %s" (stateName verdict.State) (actionName verdict.NextAction) reason
                | None -> printfn "%s — %s" (stateName verdict.State) (actionName verdict.NextAction)
                ExitCode.toInt ExitCode.Green

    let render (opts: Options) (binding: Review.Binding) (facts: Review.Facts) : int =
        renderVerdict opts binding facts None None None

    let renderWithWait (opts: Options) (binding: Review.Binding) (facts: Review.Facts) (waitState: ReviewWait.State) : int =
        renderVerdict opts binding facts None None (Some waitState)

    let run (opts: Options) : int =
        let raw = input opts
        if String.IsNullOrWhiteSpace raw then
            eprint "fsgg-coord-engine: review snapshot is empty; refusing to infer protocol state."
            ExitCode.toInt ExitCode.Error
        else
            match snapshot raw with
            | Error error ->
                eprint $"fsgg-coord-engine: review snapshot is malformed: %s{error}"
                ExitCode.toInt ExitCode.Error
            | Ok(binding, facts, successionGranted, repairAssertionGranted) ->
                renderVerdict opts binding facts successionGranted repairAssertionGranted None
