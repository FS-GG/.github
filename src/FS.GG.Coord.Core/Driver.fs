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

        let markerNames =
            [ Protocol.reviewPolicy.InitialMarker
              Protocol.reviewPolicy.ConfirmationMarker
              Protocol.reviewPolicy.AcceptanceMarker
              Protocol.reviewPolicy.EscalationMarker
              Protocol.reviewPolicy.RepairPhaseMarker ]

        let knownMarkerTexts = markerNames |> List.map markerText

        // THE PLACEMENT RULE (.github#2221).  A marker is evidence only as a WHOLE LINE inside the
        // comment's LEADING MARKER BLOCK — the run of lines, from byte 0, each of which is exactly one
        // known marker's text.  The block ends at the first line that is anything else.
        //
        // WHY A BLOCK AND NOT "the first line".  The predecessor recognised exactly one marker at offset
        // 0, and then — this is the defect — treated ANY other occurrence of those bytes ANYWHERE on the
        // PR as a hard error over the SHARED error list.  So a handoff comment whose only sin was naming
        // a marker in prose, in backticks, made every genuine marker on that PR unparseable, and the
        // message named a marker kind rather than the comment that carried the bytes: the reader was
        // sent to the critic's correct marker.  Measured live on PR #2205 at head 6ba838ac.
        //
        // "This quotation is not evidence" and "this PR can have no review" are different facts, and only
        // the first was ever intended.  For a QUOTATION the guard's stated intent is fully served by
        // `marker` declining to recognise it, so no error is raised.  For a MISPLACED marker it is not —
        // see `misplacedMarkerKinds`, which is why "delete the guard" was the wrong repair and "replace
        // it with one that can tell the two apart" is this one.
        //
        // The block also makes `independent-review` §"Repair phase" step 3 reachable: it sanctions a
        // repair-phase designation riding in "the same comment" as the initial review, which the
        // offset-0 rule could not represent at all (#2221 comment 5179330334).  Two markers, two whole
        // lines, one comment, and no prose between them: both canonical.
        //
        // What stays refused is a COMPETING marker — the same kind twice in one comment's block, which
        // has one meaning and cannot be given two.  A quotation is inert — never evidence.  A MISPLACED
        // marker is neither: see `misplacedMarkerKinds` below, which is the half that must still refuse.
        let commentLines (text: string) =
            text.Replace("\r\n", "\n").Split '\n' |> Array.toList

        let leadingMarkerBlock (text: string) =
            commentLines text |> List.takeWhile (fun line -> List.contains line knownMarkerTexts)

        let canonicalOccurrences name (text: string) =
            let expected = markerText name
            leadingMarkerBlock text |> List.filter (fun line -> line = expected) |> List.length

        let marker name (text: string) = canonicalOccurrences name text = 1

        let competingMarker name (text: string) = canonicalOccurrences name text > 1

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

        // MISPLACED IS NOT QUOTED (#2221 review round 1).  Deleting `malformedMarker` outright traded one
        // #266 shape for its mirror image.  The old rule said "I found bytes I could not interpret, so
        // NOTHING on this PR is readable"; deleting it said "I found bytes I could not interpret, so I
        // will pretend I did not see them" — and then the parser emitted `Ok (HostAccepted = true)`.
        // Measured on the live #2205 corpus: a later confirmation carrying `verdict: changes-required`
        // whose marker line has ONE LEADING SPACE went from base `Error` to `Ok`. Only `initial` and
        // `acceptances` are `requireOne`-guarded, so a dropped confirmation, escalation or repair-phase
        // comment leaves no residue at all. "I could not read this comment" became "I read every comment
        // and the chain is accepted", which is exactly what .github#266 is about.
        //
        // THE DISCRIMINATOR IS STRUCTURAL, and it has to be: a prose sentence mentioning a marker and a
        // marker preceded by a prose sentence can be byte-identical.  What is not identical is what else
        // the comment does.  A comment DESCRIBING the protocol talks about a marker; a comment
        // PARTICIPATING in it also writes the protocol's field lines.  So an occurrence is misplaced when
        // BOTH hold:
        //
        //   1. the marker text is the whole of its line modulo surrounding whitespace, outside the leading
        //      block and outside a markdown fence — an inline mention and a `> ` blockquote are
        //      typographically incapable of being the marker, and a fence is markdown's own way of saying
        //      "this is quoted text"; and
        //   2. the same comment writes at least one review-protocol field as a whole line, outside a fence.
        //
        // Both halves are needed. (1) alone re-breaks the fenced and after-prose quotations this item was
        // filed about; (2) alone re-breaks a critic's own comment that quotes an example field list.
        // Together they leave every quotation fixture inert and catch the escapes.
        //
        // A comment that satisfies both is refused BY NAME rather than dropped, which is the whole
        // correction: the reader is sent to the comment whose marker did not land, and the chain does not
        // silently lose a `changes-required` round.
        let protocolFieldKeys =
            [ "critic"; "reviewed-head"; "verdict"; "initial-review"; "round"; "preceding-review"
              "route-applicability"; "route-not-meaningful-reason"; "diff-audit-required"
              "diff-audit-receipt-v1" ]
            @ meaningfulFields
            @ Protocol.lifecyclePolicy.HostAcceptanceFields
            |> List.distinct

        /// Each line paired with whether it lies in a fenced code region.  The delimiter lines count as
        /// fenced themselves, so an opening ``` cannot be read as protocol content either.
        let annotateFences (text: string) =
            let mutable inFence = false

            commentLines text
            |> List.map (fun line ->
                let trimmed = line.TrimStart()
                let isDelimiter = trimmed.StartsWith "```" || trimmed.StartsWith "~~~"

                if isDelimiter then
                    inFence <- not inFence

                line, (isDelimiter || inFence))

        let misplacedMarkerKinds (text: string) =
            let annotated = annotateFences text

            let writesProtocolFields =
                annotated
                |> List.exists (fun (line, fenced) ->
                    not fenced
                    && protocolFieldKeys |> List.exists (fun key -> line.Trim().StartsWith(key + ":")))

            if not writesProtocolFields then
                []
            else
                let blockLength = leadingMarkerBlock text |> List.length

                annotated
                |> List.indexed
                |> List.collect (fun (index, (line, fenced)) ->
                    if fenced || index < blockLength then
                        []
                    else
                        let trimmed = line.Trim()
                        markerNames |> List.filter (fun name -> trimmed = markerText name))
                |> List.distinct

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

        // EVERY marker error names the comment that caused it (#2221 acceptance criterion 3).  The
        // predecessor named only a marker KIND, so the reader looked at the one correct marker of that
        // kind and concluded the critic had got it wrong.
        let locate (comment: ReviewComment) =
            if System.String.IsNullOrWhiteSpace comment.Url then
                $"comment %d{comment.Id}"
            else
                $"comment %d{comment.Id} (%s{comment.Url})"

        for comment in ordered do
            for name in markerNames do
                if competingMarker name comment.Body then
                    errors.Add
                        $"%s{name} marker is repeated in the leading marker block of %s{locate comment}; one comment may carry a marker kind once"

            for name in misplacedMarkerKinds comment.Body do
                errors.Add
                    $"%s{name} marker in %s{locate comment} is not a leading standalone marker, and that comment writes review-protocol fields — so it is a misplaced marker, not a quotation. Move the marker to the comment's first line, or drop the field lines if the comment only quotes it."
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
            | [] ->
                errors.Add $"exactly one %s{what} marker is required; no comment carries one"
                None
            | competing ->
                let where = competing |> List.map locate |> String.concat ", "
                errors.Add $"exactly one %s{what} marker is required; it is carried by %s{where}"
                None
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
            | _ ->
                // #2221 review round 1, finding 2.  This arm used to be the single sentence "review
                // markers are malformed", and it is the LIKELIEST marker error in practice: it fires
                // whenever any one of the six reads above fails.  It named neither the comment nor the
                // field nor whether the field was absent or written twice, so acceptance criterion 3 was
                // met on two message paths out of three.
                //
                // It is reachable by this item's OWN condition.  A critic comment that quotes an example
                // `verdict: pass` line at column 0 inside a fence makes `field "verdict"` see two values
                // — `fieldValues` is deliberately not fence-aware, because narrowing WHICH LINES COUNT as
                // a field is a different change with a different blast radius — and the whole chain used
                // to come back as four anonymous words.
                let diagnose (comment: ReviewComment) key =
                    match fieldValues key comment.Body with
                    | [ value ] when not (System.String.IsNullOrWhiteSpace value) -> None
                    | [] -> Some $"%s{locate comment} does not carry the required '%s{key}' field"
                    | [ _ ] -> Some $"%s{locate comment} carries '%s{key}' with an empty value"
                    | values ->
                        Some
                            $"%s{locate comment} carries '%s{key}' %d{List.length values} times; it must be written exactly once (a quoted example counts — `fieldValues` reads every line)"

                [ "critic", first
                  "reviewed-head", first
                  "verdict", first
                  hostFields[0], accepted
                  hostFields[1], accepted
                  hostFields[2], accepted ]
                |> List.choose (fun (key, comment) -> diagnose comment key)
                |> Error
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
