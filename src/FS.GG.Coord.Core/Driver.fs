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

    let parseReviewComments (comments: string list) =
        let marker name (text: string) = text.Contains($"<!-- fsgg:%s{name}:v1 -->")
        let field key (text: string) =
            text.Split '\n'
            |> Array.choose (fun line ->
                let line = line.Trim()
                let prefix = key + ":"
                if line.StartsWith prefix then Some(line.Substring(prefix.Length).Trim()) else None)
            |> Array.toList
            |> function [ value ] when not (System.String.IsNullOrWhiteSpace value) -> Ok value | _ -> Error $"missing or duplicate %s{key}"
        let initial = comments |> List.filter (marker "independent-review")
        let confirmations = comments |> List.filter (marker "independent-review-confirmation")
        let acceptances = comments |> List.filter (marker "review-accepted")
        let errors = ResizeArray<string>()
        let requireOne what values =
            match values with
            | [ value ] -> Some value
            | _ -> errors.Add $"exactly one %s{what} marker is required"; None
        match requireOne "independent-review" initial, requireOne "review-accepted" acceptances with
        | Some first, Some accepted ->
            match field "critic" first, field "reviewed-head" first, field "verdict" first, field "accepted-head" accepted with
            | Ok critic, Ok initialHead, Ok initialVerdict, Ok acceptedHead ->
                if List.isEmpty confirmations && initialVerdict <> "pass" then
                    errors.Add "an unconfirmed independent review must have verdict pass"
                let mutable previousHead = initialHead
                let mutable valid = true
                for expectedRound, confirmation in confirmations |> List.indexed |> List.map (fun (i, c) -> i + 1, c) do
                    match field "critic" confirmation, field "round" confirmation, field "preceding-review" confirmation,
                          field "reviewed-head" confirmation, field "verdict" confirmation with
                    | Ok confirmationCritic, Ok round, Ok preceding, Ok reviewedHead, Ok "pass"
                        when confirmationCritic = critic && round = string expectedRound && preceding = previousHead && reviewedHead <> previousHead ->
                        previousHead <- reviewedHead
                    | Ok _, Ok _, Ok _, Ok _, Ok _ ->
                        valid <- false
                        errors.Add $"review confirmation round %d{expectedRound} does not continue the same critic, round, preceding review, and new head"
                    | _ ->
                        valid <- false
                        errors.Add $"review confirmation round %d{expectedRound} is malformed"
                if acceptedHead <> previousHead then errors.Add "acceptance is not bound to the latest reviewed head"
                if valid && errors.Count = 0 then
                    let rounds = if List.isEmpty confirmations then [ 1 ] else [ 1 .. List.length confirmations ]
                    Ok { MarkerValid = true; CriticIdentity = Some critic; HeadSha = Some previousHead; Rounds = rounds; ChecksGreen = true; HostAccepted = true }
                else Error(List.ofSeq errors)
            | _ -> Error [ "review markers are malformed" ]
        | _ -> Error(List.ofSeq errors)

    type Receipt =
        { ObservedAt: int64; SourceSha: string; Complete: bool; Review: ReviewChain option }

    type WorkerReturn =
        { ClaimLive: bool; ReviewReady: bool; ParkedOrDone: bool }

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

    let receiptFresh now maxAgeSeconds receipt =
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
