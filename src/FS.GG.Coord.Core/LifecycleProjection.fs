namespace FS.GG.Coord

open System
open System.Text.RegularExpressions
open FS.GG.Coord.Types

/// One pure status projection shared by webhook and scheduled-reconciliation callers.
module LifecycleProjection =
    type Fact<'a> = { ObservedAt: int64; Value: 'a }
    type PullRequest = { Number: int; Open: bool; ReviewOrCiActive: bool }
    type Delivery = { Outstanding: bool; DoneStamped: bool }
    type Observation =
        { Claim: Fact<(Claim * Liveness) option>
          PullRequest: Fact<PullRequest option>
          Blockers: Fact<Blocker list>
          Delivery: Fact<Delivery>
          Issue: Fact<IssueState> }

    type IntentRecord =
        { Revision: int64
          Reason: string }

    type SchedulingIntent =
        | Auto
        | Backlog of IntentRecord
        | HumanPark of HumanBlock * IntentRecord
        | Deferred of reason: string * until: int64 option * revision: int64

    /// A typed human scheduling hold is the complete reason for a lifecycle Blocked projection.  This
    /// capability deliberately does not inspect mutable Status or a prose body sentinel.
    let isHumanPark = function
        | HumanPark _ -> true
        | _ -> false

    type PolicyVersion =
        | IntentStatusV1

    type Result =
        | Project of status: BoardStatus * observedAt: int64
        | Withheld of reason: string
        /// The row is a STANDING ITEM and has no lifecycle to project (.github#2712). A DISTINCT case,
        /// never folded into `Withheld of reason: string`: `Withheld` is a string a caller may only log,
        /// so an exempt row folded into it would reach every existing caller as "not this pass" and be
        /// retried forever. A case the compiler forces every caller to handle is what makes "the reducer
        /// wrote nothing" a checked property rather than a hoped-for one.
        | Exempt of kind: ItemKind

    /// The durable portion of a projection receipt.  Callers persist this beside the status write and
    /// feed it back on the next event.  Keeping the water-mark in the typed boundary makes an event that
    /// arrived late a no-op rather than an opportunity to re-derive an older column value.
    type Watermark =
        { ObservedAt: int64
          Status: BoardStatus
          Intent: SchedulingIntent }

    /// The comment-shaped receipt is deliberately small and append-only.  Project fields can be
    /// deferred and later repaired; this receipt is the durable ordering fact which says which
    /// lifecycle observation was actually verified on the row.
    let private intentWireName intent =
        match intent with
        | Auto -> "auto", 0L, None, "automatic"
        | Backlog record -> "backlog", record.Revision, None, record.Reason
        | HumanPark(AwaitingHumanDecision, record) -> "human-decision", record.Revision, None, record.Reason
        | HumanPark(AwaitingHumanAction, record) -> "human-action", record.Revision, None, record.Reason
        | Deferred(reason, until, revision) -> "deferred", revision, until, reason

    let watermarkMarker watermark =
        let intentName, revision, until, reason = intentWireName watermark.Intent
        let untilText = until |> Option.map string |> Option.defaultValue "none"
        let reasonText =
            match Uri.EscapeDataString reason with
            | "" -> "-"
            | value -> value
        $"<!-- fsgg:lifecycle-watermark v=2 observedAt=%d{watermark.ObservedAt} status=%s{statusWireName watermark.Status} intent=%s{intentName} revision=%d{revision} until=%s{untilText} reason=%s{reasonText} -->"

    // ANCHORED, NOT SUBSTRING (round-1 review repair, .github#2264 PR #2271). `body.IndexOf(marker)`
    // found the sentinel wherever it sat in a comment — including a documentation-style comment that
    // merely QUOTES an illustrative marker in prose. A quoted marker carrying a large `observedAt` then
    // silently outranked the real persisted watermark under the `List.sortByDescending` below, corrupting
    // AC-4's guarantee that an older event can never overwrite a newer observed state. This is the same
    // class of defect `.github#2221` fixed for review markers: a marker is evidence only when it is the
    // comment's ENTIRE (trimmed) body, matching `DeliveryApplication.obligationsFromComments`'s own
    // anchored, whole-line semantics exactly (`^<!-- ... -->$` against `Trim()`). It cannot be reused
    // directly — it lives in FS.GG.Coord.Cli, which depends on this project, not the other way around,
    // and its grammar is the unrelated `id=/kind=/head=` obligation shape rather than this marker's
    // `observedAt=/status=` — so this is a parallel implementation of that one rule, not a fork of its
    // logic. `Writes.lifecycleWatermark` posts nothing but this bare line (`watermarkMarker` above), so a
    // genuine receipt always satisfies a whole-body match; only a quotation can fail it, and a quotation
    // is exactly what must fail it.
    let private intentWatermarkLine =
        Regex(
            @"^<!-- fsgg:lifecycle-watermark v=2 observedAt=(?<observedAt>-?[0-9]+) status=(?<status>Backlog|Ready|In progress|Blocked|In review|Done) intent=(?<intent>auto|backlog|human-decision|human-action|deferred) revision=(?<revision>-?[0-9]+) until=(?<until>none|-?[0-9]+) reason=(?<reason>\S+) -->$",
            RegexOptions.Compiled
        )

    let tryWatermark (comments: string list) =
        let status = function
            | "Backlog" -> Some BoardStatus.Backlog
            | "Ready" -> Some BoardStatus.Ready
            | "In progress" -> Some BoardStatus.InProgress
            | "Blocked" -> Some BoardStatus.Blocked
            | "In review" -> Some BoardStatus.InReview
            | "Done" -> Some BoardStatus.Done
            | _ -> None

        let parseIntent (matched: Match) =
            try
                match Int64.TryParse matched.Groups.["revision"].Value with
                | false, _ -> None
                | true, revision ->
                    let rawReason = matched.Groups.["reason"].Value
                    if not (Regex.IsMatch(rawReason, @"^(?:[^%]|%[0-9A-Fa-f]{2})+$")) then None
                    else
                        let reason = if rawReason = "-" then "" else Uri.UnescapeDataString rawReason
                        match matched.Groups.["intent"].Value with
                        | "auto" -> Some Auto
                        | "backlog" -> Some(Backlog { Revision = revision; Reason = reason })
                        | "human-decision" -> Some(HumanPark(AwaitingHumanDecision, { Revision = revision; Reason = reason }))
                        | "human-action" -> Some(HumanPark(AwaitingHumanAction, { Revision = revision; Reason = reason }))
                        | "deferred" ->
                            match matched.Groups.["until"].Value with
                            | "none" -> Some(Deferred(reason, None, revision))
                            | value ->
                                match Int64.TryParse value with
                                | true, until -> Some(Deferred(reason, Some until, revision))
                                | _ -> None
                        | _ -> None
            with :? UriFormatException -> None

        comments
        |> List.choose (fun body ->
            let trimmed = body.Trim()
            let current = intentWatermarkLine.Match trimmed
            if current.Success then
                match Int64.TryParse(current.Groups.["observedAt"].Value), status current.Groups.["status"].Value, parseIntent current with
                | (true, observedAt), Some value, Some intent ->
                    Some { ObservedAt = observedAt; Status = value; Intent = intent }
                | _ -> None
            else None)
        |> List.sortByDescending (fun receipt -> receipt.ObservedAt)
        |> List.tryHead

    /// THE OPERATOR-WRITABLE INTENT CHANNEL (.github#2690).
    ///
    /// `Status` was an OUTPUT of this reducer with no input an operator could write. `set-field <ref>
    /// Status <value>` wrote the column, recorded nothing, exited green, and the next `reconcile --apply`
    /// pass recomputed the column from inputs the operator never touched — reverting them, in both
    /// directions, with no error and no refusal. This is the missing input.
    ///
    /// IT IS NOT THE COLUMN READ BACK, AND THE DIFFERENCE IS THE WHOLE DESIGN. `lifecyclePolicyIntent`'s
    /// refusal to consult `Status` is CORRECT and stays — *"callers at next/claim/reconcile cannot silently
    /// turn a stale column back into intent"*. A stale column is not evidence of anything; it is this
    /// reducer's own last output. What this function mints is a FRESH intent at the instant of an explicit
    /// write, from the value the caller ASKED FOR — a fact that exists nowhere else and cannot be recovered
    /// from the board afterwards. The caller supplies `observedAt`, and it must be the same
    /// Unix-milliseconds clock the reducer observes on, because `tryWatermark`'s `sortByDescending` is what
    /// makes a decision recorded now outrank one frozen hours ago.
    ///
    /// ONLY THE TWO COLUMNS INTENT ACTUALLY DECIDES GET ONE, and the `None`s are load-bearing rather than
    /// unimplemented:
    ///
    ///   * `Ready` -> `Auto` and `Backlog` -> `Backlog`. These are the two arms of `projectWithIntent`'s
    ///     final `match intent`, reached once every observed fact has settled. They are exactly the columns
    ///     an operator can choose and the reducer can honour.
    ///   * `Blocked` -> `None`, DELIBERATELY. A `Status Blocked` write is already refused unless the row
    ///     carries a durable `Blocked by` field or a `Blocked on: human/...` sentinel
    ///     (`requireCoherentParkIfBlocked`), and BOTH of those are re-derived on every pass — by
    ///     `lifecyclePolicyIntent`'s `HumanBlock` arm and by the `Blockers` observation. So `Blocked` never
    ///     had this defect, and minting a `HumanPark` here would INTRODUCE a worse one: `projectWithIntent`
    ///     tests `isHumanPark` ABOVE the blocker observation precisely so a human hold survives active
    ///     facts, which means a frozen `HumanPark` could no longer be lifted by closing the blocker that
    ///     justified it. A park that outlives its own reason is the defect this row is about, wearing the
    ///     fix's badge.
    ///   * `In progress` / `In review` / `Done` -> `None`. Intent does not decide these at all: they are
    ///     projected from claim, PR, delivery and issue facts, every one of them ABOVE the `match intent`.
    ///     A watermark here would record an intent the reducer never reads for that column, while still
    ///     suppressing policy re-derivation for the `Ready`/`Backlog` decision later — cost with no effect.
    ///   * `NoStatus` -> `None`. Not a column anyone can choose; `statusOfName` never yields it.
    ///
    /// KNOWN RESIDUE, RECORDED RATHER THAN LOST (.github#2690 acceptance 5). A watermark's mere EXISTENCE
    /// suppresses policy re-derivation — `Client.lifecycleSelection` defaults to `lifecyclePolicyIntent`
    /// only when there is no watermark at all — so an intent recorded here is frozen against a LATER move
    /// in the facts policy reads (`HumanBlock`, `Class`, `TouchSet`). That is the same freeze that produced
    /// this row's own direction B, and this change does not close it; it makes it latent by putting a
    /// fresher, correct intent on top, and it BROADENS the set of rows carrying a watermark at all.
    /// Re-deriving an intent whose generating fact has moved is deliberately NOT attempted here: it would
    /// have to decide when a recorded human decision may be overruled by a mechanical reclass, which is a
    /// scheduling-authority question this row did not answer and a channel fix must not answer by accident.
    /// It is owed as separate work.
    let explicitStatusWatermark (observedAt: int64) (reason: string) (status: BoardStatus) : Watermark option =
        let intent =
            match status with
            | BoardStatus.Ready -> Some Auto
            | BoardStatus.Backlog -> Some(SchedulingIntent.Backlog { Revision = observedAt; Reason = reason })
            | BoardStatus.Blocked
            | BoardStatus.InProgress
            | BoardStatus.InReview
            | BoardStatus.Done
            | BoardStatus.NoStatus -> None

        intent
        |> Option.map (fun intent ->
            { ObservedAt = observedAt
              Status = status
              Intent = intent })

    let private latest observation =
        [ observation.Claim.ObservedAt; observation.PullRequest.ObservedAt; observation.Blockers.ObservedAt
          observation.Delivery.ObservedAt; observation.Issue.ObservedAt ]
        |> List.max

    let private coherent observation timestamp =
        [ observation.Claim.ObservedAt; observation.PullRequest.ObservedAt; observation.Blockers.ObservedAt
          observation.Delivery.ObservedAt; observation.Issue.ObservedAt ]
        |> List.forall ((=) timestamp)

    let private projectWithIntent intent observation =
        let observedAt = latest observation
        if not (coherent observation observedAt) then
            Withheld "lifecycle facts have different observation timestamps"
        elif observation.Delivery.Value.DoneStamped && observation.Issue.Value = Closed then
            Project(BoardStatus.Done, observedAt)
        // Closure alone is not an instruction to erase the board's lifecycle state.  `Done` is earned
        // only by its immutable receipt; without it a scheduled pass must leave the terminal row for the
        // normal delivery path rather than projecting it back to Ready.
        elif observation.Issue.Value = Closed then
            Withheld "closed issue has no verified done receipt"
        // HumanPark is scheduling intent, not a blocker inferred from mutable observations.  It survives
        // active-looking facts until its own revision is changed: a worker/PR cannot silently answer the
        // human question which parked the item.  A real blocker naturally projects the same column.
        elif isHumanPark intent then
            Project(BoardStatus.Blocked, observedAt)
        elif observation.Blockers.Value |> List.exists (fun blocker -> blocker.State <> BlockerClosed && blocker.State <> BlockerMerged) then
            Project(BoardStatus.Blocked, observedAt)
        elif observation.Delivery.Value.Outstanding then
            Project(BoardStatus.InReview, observedAt)
        elif observation.PullRequest.Value |> Option.exists (fun pr -> pr.Open || pr.ReviewOrCiActive) then
            Project(BoardStatus.InReview, observedAt)
        elif observation.Claim.Value |> Option.exists (fun (_, liveness) -> match liveness with LeaseHeld | LeaseExpiredPrOpen _ | LeaseExpiredBranchPushed -> true | _ -> false) then
            Project(BoardStatus.InProgress, observedAt)
        elif observation.Claim.Value |> Option.exists (fun (_, liveness) -> match liveness with LivenessUnknown -> true | _ -> false) then
            Withheld "claim liveness could not be observed"
        else
            match intent with
            | Auto -> Project(BoardStatus.Ready, observedAt)
            | Backlog _
            | Deferred _ -> Project(BoardStatus.Backlog, observedAt)
            | HumanPark _ -> Project(BoardStatus.Blocked, observedAt) // handled above; exhaustive and stable

    // THE STANDING-ITEM EXEMPTION (.github#2712), AND ITS POSITION IS THE WHOLE DESIGN.
    //
    // It is tested here — FIRST, on `kind`, before `intent` and before `observation` are looked at at all
    // — rather than as an arm of `lifecyclePolicyIntent` or as a `SchedulingIntent` case, and neither of
    // those alternatives was rejected on taste. Both were REFUTED by measurement:
    //
    //   * As a `SchedulingIntent` it would live on the persisted watermark, and `Client.fs:2492`'s
    //     freeze — a watermark's mere EXISTENCE suppresses policy re-derivation — means a row whose
    //     `Kind` later changed could never leave its exemption.
    //   * Inside `lifecyclePolicyIntent` it is suppressed by ANY pre-existing watermark, and since
    //     .github#2690 every `add`-filed row has one. Independent critic `avocet-e644` measured exactly
    //     this for the sibling `Class`-derived arm on PR #2718 (probe 3): a `Class: decision` row
    //     carrying the `Backlog` receipt `add` now mints settles at `Backlog` and never reaches
    //     `Blocked`, while the identical row with NO receipt reaches `Blocked`.
    //
    // A PARAMETER cannot be frozen. The watermark carries `Intent` and never `Kind`, so there is no
    // receipt in this system that can suppress this test — the exemption is re-decided from the item's own
    // body on every pass, by construction rather than by anybody remembering to re-derive it.
    let reduce policy kind intent observation =
        if Kind.isStanding kind then
            Exempt kind
        else
            match policy with
            | IntentStatusV1 -> projectWithIntent intent observation

    /// Accept a newly projected lifecycle result only when it is newer than the last applied receipt.
    /// Equal timestamps are idempotent only when they agree; different values at the same timestamp are
    /// withheld because the ordering source was not strong enough to decide which event won.
    let advance policy kind intent watermark observation =
        // THE EXEMPTION IS ABOVE THE WATERMARK COMPARISON, AND STRUCTURALLY SO RATHER THAN BY A SECOND
        // COPY OF THE TEST. `reduce` runs first and answers `Exempt` before any observation is read; the
        // pass-through arm immediately below is above the `Project` arm, and the two ordering refusals
        // live INSIDE that arm. So a standing row never reaches a verdict ABOUT a watermark — which
        // matters, because those refusals say "we could not decide yet" where the right answer is "there
        // is nothing to decide, ever".
        //
        // AN EARLIER DRAFT ALSO TESTED `exemptIfStanding` HERE, as defence in depth. It was removed
        // because its inversion SURVIVED the suite — removing it changed no observable behaviour, so it
        // was a guard that could not fail, which is `.github#266`'s own class and exactly the thing this
        // row must not ship. The property it was meant to protect is asserted instead, by the
        // `advanceOf` legs of `LifecycleProjectionTests`, which drive `advance` directly with every
        // watermark shape including ones that would otherwise win the ordering comparison.
        match reduce policy kind intent observation with
        | Exempt kind -> Exempt kind
        | Withheld reason -> Withheld reason
        | Project(status, observedAt) ->
            match watermark with
            | Some previous when observedAt < previous.ObservedAt ->
                Withheld "lifecycle observation predates the persisted projection watermark"
            | Some previous when observedAt = previous.ObservedAt && status <> previous.Status ->
                Withheld "lifecycle observation conflicts with the persisted projection watermark"
            | _ -> Project(status, observedAt)
