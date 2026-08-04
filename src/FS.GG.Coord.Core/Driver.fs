namespace FS.GG.Coord

module Driver =
    open Batch

    type Housekeeping =
        { HasHostIdentity: bool
          StaleClaim: bool
          EngineCurrent: bool
          PendingWrites: int
          ReconcileDryRunFresh: bool
          ReconcileApplied: bool
          ReconcileFresh: bool
          TriageFresh: bool
          CurrencyScoped: bool }

    type RuntimeRouteEvidence =
        | Meaningful of
            builtArtifact: string *
            executedCommand: string *
            comparedRoutes: string *
            observedResult: string
        | NotMeaningful of reason: string

    type ReviewChain =
        { MarkerValid: bool; CriticIdentity: string option; HeadSha: string option
          Rounds: int list; RepairPhase: bool; ChecksGreen: bool; HostAccepted: bool
          RuntimeRouteEvidence: RuntimeRouteEvidence option
          DiffAuditRequired: bool; DiffAuditHead: string option }

    type ReviewComment =
        { Id: int64; Url: string; Body: string }

    let private parseReviewCommentsCore
        (trustedFacts: (bool * SemanticDiff.TrustedAudit option) option)
        (comments: ReviewComment list)
        =
        let markerText name = $"<!-- fsgg:%s{name}:v1 -->"
        // A protocol marker is the first complete line of the comment.  `Contains` made quoted examples,
        // critic prose and a second embedded marker executable evidence.  Preserve the bytes which make
        // the marker canonical: no quote prefix, leading prose, indentation, or duplicate occurrence.
        let marker name (text: string) =
            let expected = markerText name

            let occurrences =
                [ let mutable offset = 0

                  while offset < text.Length do
                      let found = text.IndexOf(expected, offset, System.StringComparison.Ordinal)

                      if found >= 0 then
                          yield found
                          offset <- found + expected.Length
                      else
                          offset <- text.Length ]

            match occurrences with
            | [ 0 ] when
                text.Length = expected.Length
                || text[expected.Length] = '\n'
                || (text[expected.Length] = '\r'
                    && text.Length > expected.Length + 1
                    && text[expected.Length + 1] = '\n')
                ->
                true
            | _ -> false

        let malformedMarker name (text: string) =
            text.Contains(markerText name) && not (marker name text)

        let fieldValues key (text: string) =
            text.Split '\n'
            |> Array.choose (fun line ->
                let line = line.Trim()
                let prefix = key + ":"

                if line.StartsWith prefix then
                    Some(line.Substring(prefix.Length).Trim())
                else
                    None)
            |> Array.toList

        let field key text =
            fieldValues key text
            |> function
                | [ value ] when not (System.String.IsNullOrWhiteSpace value) -> Ok value
                | _ -> Error $"missing or duplicate %s{key}"

        let hasField key text =
            fieldValues key text |> List.isEmpty |> not

        let meaningfulFields =
            [ "built-artifact"; "executed-command"; "compared-routes"; "observed-result" ]

        let routeEvidence (text: string) =
            match field "route-applicability" text with
            | Ok "meaningful" when hasField "route-not-meaningful-reason" text ->
                Error "meaningful route evidence must not carry route-not-meaningful-reason"
            | Ok "meaningful" ->
                match
                    field "built-artifact" text,
                    field "executed-command" text,
                    field "compared-routes" text,
                    field "observed-result" text
                with
                | Ok artifact, Ok command, Ok routes, Ok result -> Ok(Meaningful(artifact, command, routes, result))
                | _ ->
                    Error
                        "meaningful route evidence requires one non-empty built-artifact, executed-command, compared-routes, and observed-result field"
            | Ok "not-meaningful" when meaningfulFields |> List.exists (fun key -> hasField key text) ->
                Error "not-meaningful route evidence must not carry meaningful comparison fields"
            | Ok "not-meaningful" ->
                match field "route-not-meaningful-reason" text with
                | Ok reason when reason.Length <= 500 -> Ok(NotMeaningful reason)
                | Ok _ -> Error "route-not-meaningful-reason exceeds 500 characters"
                | Error _ ->
                    Error "not-meaningful route evidence requires one non-empty route-not-meaningful-reason field"
            | Ok value -> Error $"unknown route-applicability '%s{value}'"
            | Error _ -> Error "a passing review marker requires one route-applicability field"

        let ordered = comments |> List.sortBy _.Id
        let initial = ordered |> List.filter (fun c -> marker Protocol.reviewPolicy.InitialMarker c.Body)
        let confirmations = ordered |> List.filter (fun c -> marker Protocol.reviewPolicy.ConfirmationMarker c.Body)
        let escalations = ordered |> List.filter (fun c -> marker Protocol.reviewPolicy.EscalationMarker c.Body)
        let repairPhases = ordered |> List.filter (fun c -> marker Protocol.reviewPolicy.RepairPhaseMarker c.Body)
        let acceptances = ordered |> List.filter (fun c -> marker Protocol.reviewPolicy.AcceptanceMarker c.Body)
        let errors = ResizeArray<string>()

        for comment in ordered do
            for name in [ Protocol.reviewPolicy.InitialMarker; Protocol.reviewPolicy.ConfirmationMarker; Protocol.reviewPolicy.AcceptanceMarker; Protocol.reviewPolicy.EscalationMarker; Protocol.reviewPolicy.RepairPhaseMarker ] do
                if malformedMarker name comment.Body then
                    errors.Add $"%s{name} marker must be the single canonical leading standalone marker"
        // The marker designates the one escalated phase; it is not a confirmation round itself.  A
        // duplicate durable designation still has one boolean meaning and must not silently spend the
        // phase's confirmation budget.
        let repairPhase = not (List.isEmpty repairPhases)
        let confirmationCeiling =
            if repairPhase then Protocol.reviewPolicy.RepairPhaseMaxRounds
            else Protocol.reviewPolicy.MaxAutomatedRepairRounds
        if List.length confirmations > confirmationCeiling then errors.Add "review confirmation round ceiling exceeded"
        if not (List.isEmpty escalations) && List.isEmpty repairPhases then errors.Add "review escalation requires a repair-phase marker"
        let requireOne what values =
            match values with
            | [ value ] -> Some value
            | _ -> errors.Add $"exactly one %s{what} marker is required"; None
        let hostFields = Protocol.lifecyclePolicy.HostAcceptanceFields
        match requireOne Protocol.reviewPolicy.InitialMarker initial, requireOne Protocol.reviewPolicy.AcceptanceMarker acceptances with
        | Some first, Some accepted ->
            match field "critic" first.Body, field "reviewed-head" first.Body, field "verdict" first.Body,
                  field hostFields[0] accepted.Body, field hostFields[1] accepted.Body, field hostFields[2] accepted.Body with
            | Ok critic, Ok initialHead, Ok initialVerdict, Ok acceptedHead, Ok acceptedInitialUrl, Ok acceptedLatestUrl ->
                if System.String.IsNullOrWhiteSpace first.Url then
                    errors.Add "the initial review comment URL is missing"

                if initialVerdict <> "pass" && initialVerdict <> "changes-required" then
                    errors.Add "the initial independent review verdict must be pass or changes-required"

                if List.isEmpty confirmations && initialVerdict <> "pass" then
                    errors.Add "an unconfirmed independent review must have verdict pass"

                let mutable latestRouteEvidence = None

                let validateRouteEvidence label verdict body =
                    if verdict = "pass" || hasField "route-applicability" body then
                        match routeEvidence body with
                        | Ok evidence -> latestRouteEvidence <- Some evidence
                        | Error error -> errors.Add $"%s{label}: %s{error}"

                validateRouteEvidence "initial review" initialVerdict first.Body
                let mutable previousHead = initialHead
                let mutable previousReviewUrl = first.Url
                let mutable previousReviewId = first.Id
                let mutable valid = true

                for expectedRound, confirmation in confirmations |> List.indexed |> List.map (fun (i, c) -> i + 1, c) do
                    match
                        field "initial-review" confirmation.Body,
                        field "critic" confirmation.Body,
                        field "round" confirmation.Body,
                        field "preceding-review" confirmation.Body,
                        field "reviewed-head" confirmation.Body,
                        field "verdict" confirmation.Body
                    with
                    | Ok initialUrl, Ok confirmationCritic, Ok round, Ok preceding, Ok reviewedHead, Ok verdict when
                        initialUrl = first.Url
                        && confirmationCritic = critic
                        && round = string expectedRound
                        && preceding = previousReviewUrl
                        && not (System.String.IsNullOrWhiteSpace confirmation.Url)
                        && confirmation.Id > previousReviewId
                        && (verdict = "pass" || verdict = "changes-required")
                        ->
                        validateRouteEvidence $"review confirmation round %d{expectedRound}" verdict confirmation.Body
                        previousHead <- reviewedHead
                        previousReviewUrl <- confirmation.Url
                        previousReviewId <- confirmation.Id
                    | Ok _, Ok _, Ok _, Ok _, Ok _, Ok _ ->
                        valid <- false

                        errors.Add
                            $"review confirmation round %d{expectedRound} does not continue the initial URL, same critic, round, and preceding comment URL"
                    | _ ->
                        valid <- false
                        errors.Add $"review confirmation round %d{expectedRound} is malformed"

                if not (List.isEmpty confirmations) then
                    match confirmations |> List.last |> (fun c -> field "verdict" c.Body) with
                    | Ok "pass" -> ()
                    | _ -> errors.Add "the latest review confirmation must have verdict pass"

                let auditRequired =
                    match field "diff-audit-required" first.Body with
                    | Ok "true" -> true
                    | Ok "false"
                    | Error _ -> false
                    | Ok _ ->
                        errors.Add "diff-audit-required must be true or false"
                        false

                let mechanicallyRequired = trustedFacts |> Option.exists fst

                if mechanicallyRequired && not auditRequired then
                    errors.Add "diff-audit-required:false contradicts trusted live delivery facts"

                let effectiveAuditRequired = auditRequired || mechanicallyRequired

                // A receipt names ONE rename pair, so a chain may carry several and the gate is only
                // satisfied by their UNION.  `fieldValues`, not `field`: `field` fails closed on a
                // duplicate key, which is right for a single-valued field and is exactly what made a
                // second receipt unpostable — and therefore what made a covering receipt impossible to
                // author for a two-rename diff (.github#2144 repair-phase round 2).
                //
                // The outcomes below are deliberately DISTINCT rather than collapsed into one sentence.
                // "no receipt was submitted", "a receipt is malformed", "a receipt is dishonest about its
                // own pair" and "the receipts are honest but do not account for the whole diff" are four
                // different repairs, and .github#2207 is what collapsing them costs: an operator told to
                // wait for a review that had already happened, because one message stood for every cause.
                let auditHead =
                    if not effectiveAuditRequired then
                        None
                    else
                        match fieldValues "diff-audit-receipt-v1" accepted.Body with
                        | [] ->
                            errors.Add "a required typed diff-audit receipt was not submitted"
                            None
                        | encoded ->
                            let parsed = encoded |> List.map SemanticDiff.ofBase64

                            if parsed |> List.exists Result.isError then
                                errors.Add "a submitted typed diff-audit receipt is malformed"
                                None
                            else
                                let submitted = parsed |> List.choose Result.toOption

                                match trustedFacts |> Option.bind snd with
                                | None ->
                                    errors.Add "the live delivery facts needed to check a diff-audit receipt are absent"
                                    None
                                | Some trusted ->
                                    // 1. HONESTY. Every submitted receipt must match the engine's own
                                    //    recomputation of the pair and paths it names.
                                    for receipt in submitted do
                                        if not receipt.Required then
                                            errors.Add "a submitted typed diff-audit receipt does not assert the audit was required"
                                        else
                                            match
                                                trusted.Expected
                                                |> List.tryFind (fun expected ->
                                                    expected.OldToken = receipt.OldToken
                                                    && expected.NewToken = receipt.NewToken
                                                    && expected.DeclaredPaths = receipt.DeclaredPaths)
                                            with
                                            | None ->
                                                errors.Add
                                                    "a submitted typed diff-audit receipt names a rename the live delivery facts did not recompute"
                                            | Some expected ->
                                                if SemanticDiff.validateAgainst expected receipt |> List.isEmpty |> not then
                                                    errors.Add
                                                        "a submitted typed diff-audit receipt is stale, or has unresolved dispositions"

                                    // 2. COVERAGE. Honest receipts still have to account for the WHOLE
                                    //    diff, or the author narrows the gate by choosing what to submit.
                                    let accounted =
                                        submitted
                                        |> List.collect _.Occurrences
                                        |> List.map _.Id
                                        |> Set.ofList

                                    let uncovered =
                                        trusted.Discovered
                                        |> List.filter (fun occurrence -> not (accounted.Contains occurrence.Id))

                                    if not (List.isEmpty uncovered) then
                                        errors.Add
                                            $"the submitted typed diff-audit receipts account for %d{trusted.Discovered.Length - uncovered.Length} of %d{trusted.Discovered.Length} discovered occurrences"

                                    match submitted |> List.map _.HeadSha |> List.distinct with
                                    | [ single ] -> Some single
                                    | _ ->
                                        errors.Add "the submitted typed diff-audit receipts are not all bound to one head"
                                        None

                if accepted.Id <= previousReviewId then
                    errors.Add "host acceptance must follow the latest review comment"

                if acceptedHead <> previousHead then
                    errors.Add "acceptance is not bound to the latest reviewed head"

                if acceptedInitialUrl <> first.Url then
                    errors.Add "acceptance is not bound to the initial review comment URL"

                if acceptedLatestUrl <> previousReviewUrl then
                    errors.Add "acceptance is not bound to the latest confirmation comment URL"

                if valid && errors.Count = 0 then
                    let rounds = if List.isEmpty confirmations then [ 1 ] else [ 1 .. List.length confirmations ]
                    Ok { MarkerValid = true; CriticIdentity = Some critic; HeadSha = Some previousHead
                         Rounds = rounds; RepairPhase = repairPhase; ChecksGreen = false; HostAccepted = true
                         RuntimeRouteEvidence = latestRouteEvidence
                         DiffAuditRequired = effectiveAuditRequired; DiffAuditHead = auditHead }
                else Error(List.ofSeq errors)
            | _ -> Error [ "review markers are malformed" ]
        | _ -> Error(List.ofSeq errors)

    let parseReviewComments comments = parseReviewCommentsCore None comments

    let parseReviewCommentsWithAudit (trustedAudit: SemanticDiff.Receipt) comments =
        // The single-receipt spelling stays available: one receipt whose own recomputation IS the whole
        // discovered population, which is the shape every pre-round-2 caller meant.
        parseReviewCommentsCore
            (Some(true, Some { Expected = [ trustedAudit ]; Discovered = trustedAudit.Occurrences }))
            comments

    let parseReviewCommentsWithFacts mechanicallyRequired trustedAudit comments =
        parseReviewCommentsCore (Some(mechanicallyRequired, trustedAudit)) comments

    type Receipt =
        { ObservedAt: int64
          SourceSha: string
          Complete: bool
          Review: ReviewChain option }

    type WorkerReturn =
        { ClaimLive: bool
          ReviewReady: bool
          ParkedOrDone: bool }

    type PlanningObservation =
        { Kind: string
          ObservedAt: int64
          SourceSha: string
          Outcome: string
          ReceiptId: string }

    type PlanningReceipt =
        { ObservedAt: int64
          SourceSha: string
          Complete: bool
          ConsolidationApproved: bool
          Observations: PlanningObservation list }

    let observationReceiptId kind observedAt sourceSha outcome =
        $"%s{kind}\n%d{observedAt}\n%s{sourceSha}\n%s{outcome}"
        |> System.Text.Encoding.UTF8.GetBytes
        |> System.Security.Cryptography.SHA256.HashData
        |> System.Convert.ToHexString
        |> fun value -> value.ToLowerInvariant()

    let planningReceiptFresh now maxAgeSeconds sourceSha receipt =
        let expected = Protocol.ledgerPolicy.RequiredObservations
        let observationValid (kind, outcome) =
            receipt.Observations
            |> List.filter (fun observation -> observation.Kind = kind)
            |> function
                | [ observation ] ->
                    observation.Outcome = outcome
                    && observation.SourceSha = sourceSha
                    && now >= observation.ObservedAt
                    && now - observation.ObservedAt <= maxAgeSeconds
                    && observation.ReceiptId = observationReceiptId
                        observation.Kind
                        observation.ObservedAt
                        observation.SourceSha
                        observation.Outcome
                | _ -> false

        receipt.Complete
        && not (System.String.IsNullOrWhiteSpace receipt.SourceSha)
        && receipt.SourceSha = sourceSha
        && now >= receipt.ObservedAt
        && now - receipt.ObservedAt <= maxAgeSeconds
        && List.length receipt.Observations = List.length expected
        && (expected |> List.forall observationValid)

    type Action =
        | RequestHostIdentity
        | ReapStaleClaims
        | RepairEngineCurrency
        | FlushPendingWrites
        | ReconcileBoard
        | RefreshTriage
        | Consolidate
        | ResumeSameWorker
        | DispatchWave of slots: int
        | ContinueCurrentWave

    let validateReviewChain maxRounds chain =
        [ if not chain.MarkerValid then
              "review marker is missing or invalid"
          if Option.isNone chain.CriticIdentity then
              "critic identity is missing"
          if Option.isNone chain.HeadSha then
              "review head SHA is missing"
          if List.isEmpty chain.Rounds || chain.Rounds <> [ 1 .. List.length chain.Rounds ] then
              "review rounds are not ordered from one"
          if List.length chain.Rounds > maxRounds then
              "review round ceiling exceeded"
          if Option.isNone chain.RuntimeRouteEvidence then
              "runtime-route applicability evidence is missing"
          if chain.DiffAuditRequired && chain.DiffAuditHead <> chain.HeadSha then
              "required diff-audit receipt is missing, stale, or unresolved"
          if not chain.ChecksGreen then
              "review checks are not green"
          if not chain.HostAccepted then
              "host acceptance is missing" ]

    let receiptFresh now maxAgeSeconds (receipt: Receipt) =
        let confirmationCeiling chain =
            if chain.RepairPhase then Protocol.reviewPolicy.RepairPhaseMaxRounds
            else Protocol.reviewPolicy.MaxAutomatedRepairRounds
        receipt.Complete && not (System.String.IsNullOrWhiteSpace receipt.SourceSha) && (receipt.Review |> Option.exists (fun chain -> validateReviewChain (confirmationCeiling chain) chain |> List.isEmpty)) && now >= receipt.ObservedAt && now - receipt.ObservedAt <= maxAgeSeconds

    let nextAction model activeItems consolidationApproved housekeeping workerReturns =
        if not housekeeping.HasHostIdentity then
            RequestHostIdentity
        elif housekeeping.StaleClaim then
            ReapStaleClaims
        elif not housekeeping.EngineCurrent || not housekeeping.CurrencyScoped then
            RepairEngineCurrency
        elif housekeeping.PendingWrites <> 0 then
            FlushPendingWrites
        elif
            not housekeeping.ReconcileDryRunFresh
            || not housekeeping.ReconcileApplied
            || not housekeeping.ReconcileFresh
        then
            ReconcileBoard
        elif not housekeeping.TriageFresh then
            RefreshTriage
        elif
            workerReturns
            |> List.exists (fun r -> r.ClaimLive && not r.ReviewReady && not r.ParkedOrDone)
        then
            ResumeSameWorker
        elif activeItems <= model.ConsolidationThreshold && not consolidationApproved then
            Consolidate
        elif activeItems <= model.ConsolidationThreshold && consolidationApproved then
            DispatchWave(
                min model.ImplementerSlotsPerWave (max 0 (model.Waves * model.ImplementerSlotsPerWave - activeItems))
            )
        else
            ContinueCurrentWave
