namespace FS.GG.Coord

module Driver =
    open Batch

    type Housekeeping =
        { HasHostIdentity: bool; StaleClaim: bool; EngineCurrent: bool; PendingWrites: int
          ReconcileDryRunFresh: bool; ReconcileApplied: bool; ReconcileFresh: bool
          TriageFresh: bool; CurrencyScoped: bool }

    type RuntimeRouteEvidence =
        | Meaningful of builtArtifact: string * executedCommand: string * comparedRoutes: string * observedResult: string
        | NotMeaningful of reason: string

    type ReviewChain =
        { MarkerValid: bool; CriticIdentity: string option; HeadSha: string option
          Rounds: int list; ChecksGreen: bool; HostAccepted: bool
          RuntimeRouteEvidence: RuntimeRouteEvidence option
          DiffAuditRequired: bool; DiffAuditHead: string option }

    type ReviewComment = { Id: int64; Url: string; Body: string }

    let parseReviewComments (comments: ReviewComment list) =
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
            | [ 0 ] when text.Length = expected.Length || text[expected.Length] = '\n'
                         || (text[expected.Length] = '\r' && text.Length > expected.Length + 1 && text[expected.Length + 1] = '\n') -> true
            | _ -> false
        let malformedMarker name (text: string) = text.Contains(markerText name) && not (marker name text)
        let fieldValues key (text: string) =
            text.Split '\n'
            |> Array.choose (fun line ->
                let line = line.Trim()
                let prefix = key + ":"
                if line.StartsWith prefix then Some(line.Substring(prefix.Length).Trim()) else None)
            |> Array.toList
        let field key text =
            fieldValues key text
            |> function [ value ] when not (System.String.IsNullOrWhiteSpace value) -> Ok value | _ -> Error $"missing or duplicate %s{key}"
        let hasField key text = fieldValues key text |> List.isEmpty |> not
        let meaningfulFields = [ "built-artifact"; "executed-command"; "compared-routes"; "observed-result" ]
        let routeEvidence (text: string) =
            match field "route-applicability" text with
            | Ok "meaningful" when hasField "route-not-meaningful-reason" text ->
                Error "meaningful route evidence must not carry route-not-meaningful-reason"
            | Ok "meaningful" ->
                match field "built-artifact" text, field "executed-command" text,
                      field "compared-routes" text, field "observed-result" text with
                | Ok artifact, Ok command, Ok routes, Ok result -> Ok(Meaningful(artifact, command, routes, result))
                | _ -> Error "meaningful route evidence requires one non-empty built-artifact, executed-command, compared-routes, and observed-result field"
            | Ok "not-meaningful" when meaningfulFields |> List.exists (fun key -> hasField key text) ->
                Error "not-meaningful route evidence must not carry meaningful comparison fields"
            | Ok "not-meaningful" ->
                match field "route-not-meaningful-reason" text with
                | Ok reason when reason.Length <= 500 -> Ok(NotMeaningful reason)
                | Ok _ -> Error "route-not-meaningful-reason exceeds 500 characters"
                | Error _ -> Error "not-meaningful route evidence requires one non-empty route-not-meaningful-reason field"
            | Ok value -> Error $"unknown route-applicability '%s{value}'"
            | Error _ -> Error "a passing review marker requires one route-applicability field"
        let ordered = comments |> List.sortBy _.Id
        let initial = ordered |> List.filter (fun c -> marker "independent-review" c.Body)
        let confirmations = ordered |> List.filter (fun c -> marker "independent-review-confirmation" c.Body)
        let acceptances = ordered |> List.filter (fun c -> marker "review-accepted" c.Body)
        let errors = ResizeArray<string>()
        for comment in ordered do
            for name in [ "independent-review"; "independent-review-confirmation"; "review-accepted" ] do
                if malformedMarker name comment.Body then
                    errors.Add $"%s{name} marker must be the single canonical leading standalone marker"
        if List.length confirmations > 3 then errors.Add "review confirmation round ceiling exceeded"
        let requireOne what values =
            match values with
            | [ value ] -> Some value
            | _ -> errors.Add $"exactly one %s{what} marker is required"; None
        match requireOne "independent-review" initial, requireOne "review-accepted" acceptances with
        | Some first, Some accepted ->
            match field "critic" first.Body, field "reviewed-head" first.Body, field "verdict" first.Body,
                  field "accepted-head" accepted.Body, field "initial-review" accepted.Body, field "latest-confirmation" accepted.Body with
            | Ok critic, Ok initialHead, Ok initialVerdict, Ok acceptedHead, Ok acceptedInitialUrl, Ok acceptedLatestUrl ->
                if System.String.IsNullOrWhiteSpace first.Url then errors.Add "the initial review comment URL is missing"
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
                    match field "initial-review" confirmation.Body, field "critic" confirmation.Body, field "round" confirmation.Body,
                          field "preceding-review" confirmation.Body, field "reviewed-head" confirmation.Body, field "verdict" confirmation.Body with
                    | Ok initialUrl, Ok confirmationCritic, Ok round, Ok preceding, Ok reviewedHead, Ok verdict
                        when initialUrl = first.Url && confirmationCritic = critic && round = string expectedRound
                             && preceding = previousReviewUrl && not (System.String.IsNullOrWhiteSpace confirmation.Url)
                             && confirmation.Id > previousReviewId
                             && (verdict = "pass" || verdict = "changes-required") ->
                        validateRouteEvidence $"review confirmation round %d{expectedRound}" verdict confirmation.Body
                        previousHead <- reviewedHead
                        previousReviewUrl <- confirmation.Url
                        previousReviewId <- confirmation.Id
                    | Ok _, Ok _, Ok _, Ok _, Ok _, Ok _ ->
                        valid <- false
                        errors.Add $"review confirmation round %d{expectedRound} does not continue the initial URL, same critic, round, and preceding comment URL"
                    | _ ->
                        valid <- false
                        errors.Add $"review confirmation round %d{expectedRound} is malformed"
                if not (List.isEmpty confirmations) then
                    match confirmations |> List.last |> fun c -> field "verdict" c.Body with
                    | Ok "pass" -> ()
                    | _ -> errors.Add "the latest review confirmation must have verdict pass"
                let auditRequired =
                    match field "diff-audit-required" first.Body with
                    | Ok "true" -> true
                    | Ok "false" | Error _ -> false
                    | Ok _ -> errors.Add "diff-audit-required must be true or false"; false
                let auditHead =
                    if auditRequired then
                        match field "diff-audit-receipt" accepted.Body, field "diff-audit-base" accepted.Body,
                              field "diff-audit-head" accepted.Body, field "diff-audit-disposition" accepted.Body with
                        | Ok "complete", Ok baseSha, Ok head, Ok "all-resolved" when not (System.String.IsNullOrWhiteSpace baseSha) && head = acceptedHead -> Some head
                        | _ -> errors.Add "required diff-audit receipt is missing, stale, or has unresolved dispositions"; None
                    else None
                if accepted.Id <= previousReviewId then errors.Add "host acceptance must follow the latest review comment"
                if acceptedHead <> previousHead then errors.Add "acceptance is not bound to the latest reviewed head"
                if acceptedInitialUrl <> first.Url then errors.Add "acceptance is not bound to the initial review comment URL"
                if acceptedLatestUrl <> previousReviewUrl then errors.Add "acceptance is not bound to the latest confirmation comment URL"
                if valid && errors.Count = 0 then
                    let rounds = if List.isEmpty confirmations then [ 1 ] else [ 1 .. List.length confirmations ]
                    Ok { MarkerValid = true; CriticIdentity = Some critic; HeadSha = Some previousHead
                         Rounds = rounds; ChecksGreen = false; HostAccepted = true
                         RuntimeRouteEvidence = latestRouteEvidence
                         DiffAuditRequired = auditRequired; DiffAuditHead = auditHead }
                else Error(List.ofSeq errors)
            | _ -> Error [ "review markers are malformed" ]
        | _ -> Error(List.ofSeq errors)

    type Receipt =
        { ObservedAt: int64; SourceSha: string; Complete: bool; Review: ReviewChain option }

    type WorkerReturn =
        { ClaimLive: bool; ReviewReady: bool; ParkedOrDone: bool }

    type PlanningObservation =
        { Kind: string; ObservedAt: int64; SourceSha: string; Outcome: string; ReceiptId: string }

    type PlanningReceipt =
        { ObservedAt: int64; SourceSha: string; Complete: bool; ConsolidationApproved: bool
          Observations: PlanningObservation list }

    let observationReceiptId kind observedAt sourceSha outcome =
        $"%s{kind}\n%d{observedAt}\n%s{sourceSha}\n%s{outcome}"
        |> System.Text.Encoding.UTF8.GetBytes
        |> System.Security.Cryptography.SHA256.HashData
        |> System.Convert.ToHexString
        |> fun value -> value.ToLowerInvariant()

    let planningReceiptFresh now maxAgeSeconds sourceSha receipt =
        let expected =
            [ "reconcile-dry-run", "clean"
              "reconcile-apply", "applied-or-not-needed"
              "reconcile-fresh", "clean"
              "triage", "fresh"
              "engine-currency", "current-scoped" ]
        let observationValid (kind, outcome) =
            receipt.Observations
            |> List.filter (fun observation -> observation.Kind = kind)
            |> function
                | [ observation ] ->
                    observation.Outcome = outcome
                    && observation.SourceSha = sourceSha
                    && now >= observation.ObservedAt
                    && now - observation.ObservedAt <= maxAgeSeconds
                    && observation.ReceiptId = observationReceiptId observation.Kind observation.ObservedAt observation.SourceSha observation.Outcome
                | _ -> false
        receipt.Complete
        && not (System.String.IsNullOrWhiteSpace receipt.SourceSha)
        && receipt.SourceSha = sourceSha
        && now >= receipt.ObservedAt
        && now - receipt.ObservedAt <= maxAgeSeconds
        && List.length receipt.Observations = List.length expected
        && (expected |> List.forall observationValid)

    type Action =
        | RequestHostIdentity | ReapStaleClaims | RepairEngineCurrency | FlushPendingWrites
        | ReconcileBoard | RefreshTriage | Consolidate | ResumeSameWorker
        | DispatchWave of slots: int | ContinueCurrentWave

    let validateReviewChain maxRounds chain =
        [ if not chain.MarkerValid then "review marker is missing or invalid"
          if Option.isNone chain.CriticIdentity then "critic identity is missing"
          if Option.isNone chain.HeadSha then "review head SHA is missing"
          if List.isEmpty chain.Rounds || chain.Rounds <> [ 1 .. List.length chain.Rounds ] then
              "review rounds are not ordered from one"
          if List.length chain.Rounds > maxRounds then "review round ceiling exceeded"
          if Option.isNone chain.RuntimeRouteEvidence then "runtime-route applicability evidence is missing"
          if chain.DiffAuditRequired && chain.DiffAuditHead <> chain.HeadSha then "required diff-audit receipt is missing, stale, or unresolved"
          if not chain.ChecksGreen then "review checks are not green"
          if not chain.HostAccepted then "host acceptance is missing" ]

    let receiptFresh now maxAgeSeconds (receipt: Receipt) =
        receipt.Complete && not (System.String.IsNullOrWhiteSpace receipt.SourceSha) && (receipt.Review |> Option.exists (validateReviewChain 3 >> List.isEmpty)) && now >= receipt.ObservedAt && now - receipt.ObservedAt <= maxAgeSeconds

    let nextAction model activeItems consolidationApproved housekeeping workerReturns =
        if not housekeeping.HasHostIdentity then RequestHostIdentity
        elif housekeeping.StaleClaim then ReapStaleClaims
        elif not housekeeping.EngineCurrent || not housekeeping.CurrencyScoped then RepairEngineCurrency
        elif housekeeping.PendingWrites <> 0 then FlushPendingWrites
        elif not housekeeping.ReconcileDryRunFresh || not housekeeping.ReconcileApplied || not housekeeping.ReconcileFresh then ReconcileBoard
        elif not housekeeping.TriageFresh then RefreshTriage
        elif workerReturns |> List.exists (fun r -> r.ClaimLive && not r.ReviewReady && not r.ParkedOrDone) then ResumeSameWorker
        elif activeItems <= model.ConsolidationThreshold && not consolidationApproved then Consolidate
        elif activeItems <= model.ConsolidationThreshold && consolidationApproved then
            DispatchWave (min model.ImplementerSlotsPerWave (max 0 (model.Waves * model.ImplementerSlotsPerWave - activeItems)))
        else ContinueCurrentWave
