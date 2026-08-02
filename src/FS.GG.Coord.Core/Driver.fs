namespace FS.GG.Coord

module Driver =
    open Batch

    type Housekeeping =
        { HasHostIdentity: bool; StaleClaim: bool; EngineCurrent: bool; PendingWrites: int
          ReconcileDryRunFresh: bool; ReconcileApplied: bool; ReconcileFresh: bool
          TriageFresh: bool; CurrencyScoped: bool }

    type ReviewChain =
        { MarkerValid: bool; CriticIdentity: string option; HeadSha: string option
          Rounds: int list; ChecksGreen: bool; HostAccepted: bool }

    type ReviewComment = { Id: int64; Url: string; Body: string }

    let parseReviewComments (comments: ReviewComment list) =
        let marker name (text: string) = text.Contains($"<!-- fsgg:%s{name}:v1 -->")
        let field key (text: string) =
            text.Split '\n'
            |> Array.choose (fun line ->
                let line = line.Trim()
                let prefix = key + ":"
                if line.StartsWith prefix then Some(line.Substring(prefix.Length).Trim()) else None)
            |> Array.toList
            |> function [ value ] when not (System.String.IsNullOrWhiteSpace value) -> Ok value | _ -> Error $"missing or duplicate %s{key}"
        let ordered = comments |> List.sortBy _.Id
        let initial = ordered |> List.filter (fun c -> marker "independent-review" c.Body)
        let confirmations = ordered |> List.filter (fun c -> marker "independent-review-confirmation" c.Body)
        let acceptances = ordered |> List.filter (fun c -> marker "review-accepted" c.Body)
        let errors = ResizeArray<string>()
        let requireOne what values =
            match values with
            | [ value ] -> Some value
            | _ -> errors.Add $"exactly one %s{what} marker is required"; None
        match requireOne "independent-review" initial, requireOne "review-accepted" acceptances with
        | Some first, Some accepted ->
            match field "critic" first.Body, field "reviewed-head" first.Body, field "verdict" first.Body, field "accepted-head" accepted.Body with
            | Ok critic, Ok initialHead, Ok initialVerdict, Ok acceptedHead ->
                if System.String.IsNullOrWhiteSpace first.Url then errors.Add "the initial review comment URL is missing"
                if List.isEmpty confirmations && initialVerdict <> "pass" then
                    errors.Add "an unconfirmed independent review must have verdict pass"
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
                if accepted.Id <= previousReviewId then errors.Add "host acceptance must follow the latest review comment"
                if acceptedHead <> previousHead then errors.Add "acceptance is not bound to the latest reviewed head"
                if valid && errors.Count = 0 then
                    let rounds = if List.isEmpty confirmations then [ 1 ] else [ 1 .. List.length confirmations ]
                    Ok { MarkerValid = true; CriticIdentity = Some critic; HeadSha = Some previousHead; Rounds = rounds; ChecksGreen = false; HostAccepted = true }
                else Error(List.ofSeq errors)
            | _ -> Error [ "review markers are malformed" ]
        | _ -> Error(List.ofSeq errors)

    type Receipt =
        { ObservedAt: int64; SourceSha: string; Complete: bool; Review: ReviewChain option }

    type WorkerReturn =
        { ClaimLive: bool; ReviewReady: bool; ParkedOrDone: bool }

    type PlanningReceipt =
        { ObservedAt: int64; SourceSha: string; Complete: bool; ConsolidationApproved: bool
          Housekeeping: Housekeeping; WorkerReturns: WorkerReturn list }

    let planningReceiptFresh now maxAgeSeconds sourceSha receipt =
        receipt.Complete
        && not (System.String.IsNullOrWhiteSpace receipt.SourceSha)
        && receipt.SourceSha = sourceSha
        && now >= receipt.ObservedAt
        && now - receipt.ObservedAt <= maxAgeSeconds

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
