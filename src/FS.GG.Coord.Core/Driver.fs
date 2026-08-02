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
