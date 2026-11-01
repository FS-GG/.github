namespace FS.GG.Coord.GitHub

open System
open System.Text.Json

module V2Tend =
    type WorkLevel =
        | Outcome
        | Delivery

    type Track =
        | ActiveDelivery
        | FollowUp

    type Status =
        | Backlog
        | Ready
        | InProgress
        | InReview
        | Blocked
        | Done

    type ContentKind =
        | Issue
        | PullRequestCard
        | DraftCard
        | Unreadable

    type PrState =
        | PrOpen
        | PrClosed
        | PrMerged

    type Linkage =
        | Strict
        | Loose of reason: string

    type LinkedPr =
        { Id: string
          State: PrState
          Draft: bool
          Linkage: Linkage
          UpdatedAt: DateTimeOffset
          MergedAt: DateTimeOffset option
          Continues: bool }

    type IntendedFields =
        { WorkLevel: WorkLevel option
          Track: Track option
          Status: Status option
          RootAuthored: bool }

    type Blocker = { Ref: string; Open: bool }

    type Card =
        { ItemId: string
          Ref: string
          Kind: ContentKind
          Open: bool
          CreatedAt: DateTimeOffset
          UpdatedAt: DateTimeOffset
          ClosedAt: DateTimeOffset option
          Status: Status option
          StatusUpdatedAt: DateTimeOffset option
          WorkLevel: WorkLevel option
          Track: Track option
          Roadmap: string option
          Parent: string option
          SubIssues: (string * bool) list
          SubIssuesComplete: bool
          Blockers: Blocker list
          BlockersComplete: bool
          MarkerBlockers: Blocker list option
          Prs: LinkedPr list
          TimelineComplete: bool
          Intended: IntendedFields option
          BlockedOn: string option
          AcceptanceEvidence: string option
          Enrolled: bool
          EnrolledRoadmap: string option
          TendClosedBefore: bool }

    type Deferral =
        { Key: string
          FirstDeferredAt: DateTimeOffset
          Runs: int }

    type Snapshot =
        { ObservedAt: DateTimeOffset
          Cards: Card list
          Ledger: Deferral list }

    type Parameters =
        { ActivityHours: float
          DoneSettleMinutes: float
          ReviewSettleHours: float
          LeafSettleHours: float
          CloseSettleHours: float
          ExpiryRuns: int
          ExpiryHours: float
          Enabled: Set<string> }

    let v1 (enabled: Set<string>) =
        { ActivityHours = 48.0
          DoneSettleMinutes = 15.0
          ReviewSettleHours = 48.0
          LeafSettleHours = 24.0
          CloseSettleHours = 48.0
          ExpiryRuns = 3
          ExpiryHours = 24.0
          Enabled = enabled }

    let applyRules = set [ "W1h"; "W1d"; "T1d"; "T1i"; "T1f"; "RM1"; "S1"; "S3"; "S3b"; "S4"; "S9"; "S9r"; "O1"; "C1" ]

    type Field =
        | StatusField
        | WorkLevelField
        | TrackField
        | RoadmapField

    let fieldName (field: Field) =
        match field with
        | StatusField -> "status"
        | WorkLevelField -> "work-level"
        | TrackField -> "track"
        | RoadmapField -> "roadmap"

    type Effect =
        { Id: string
          ItemId: string
          Ref: string
          Field: Field
          Preimage: string option
          Target: string
          Rules: string list
          DependsOn: string list }

    type DeferralReason =
        | InputPending
        | RuleDisabled
        | SettleWindow

    type Deferred =
        { Key: string
          ItemId: string
          Ref: string
          Field: Field
          Rule: string
          Reason: DeferralReason
          Counts: bool
          Expired: bool
          Label: string }

    type Proposal =
        { Id: string
          Ref: string
          Rule: string
          Reason: string
          Suggested: string option }

    type Refusal =
        { Id: string
          Ref: string
          Rule: string
          Reason: string }

    type Comparison = { Pairs: int; Disagreements: int }

    type Closure =
        { Id: string
          ItemId: string
          Ref: string
          MergedPrs: string list }

    type Plan =
        { Ruleset: string
          ObservedAt: DateTimeOffset
          Apply: Effect list
          WouldApply: Effect list
          Deferred: Deferred list
          Propose: Proposal list
          Refuse: Refusal list
          T1iComparison: Comparison
          Close: Closure list
          WouldClose: Closure list }

    let ruleset = "fsgg:board-tend-rules/v1"

    // ---- value names (the Project 3 option names) ----

    let private statusName =
        function
        | Backlog -> "Backlog"
        | Ready -> "Ready"
        | InProgress -> "In progress"
        | InReview -> "In review"
        | Blocked -> "Blocked"
        | Done -> "Done"

    let private statusOf =
        function
        | "Backlog" -> Some Backlog
        | "Ready" -> Some Ready
        | "In progress" -> Some InProgress
        | "In review" -> Some InReview
        | "Blocked" -> Some Blocked
        | "Done" -> Some Done
        | _ -> None

    let private workLevelName = function Outcome -> "Outcome" | Delivery -> "Delivery"
    let private workLevelOf = function "Outcome" -> Some Outcome | "Delivery" -> Some Delivery | _ -> None
    let private trackName = function ActiveDelivery -> "Active delivery" | FollowUp -> "Follow-up"
    let private trackOf = function "Active delivery" -> Some ActiveDelivery | "Follow-up" -> Some FollowUp | _ -> None

    let private valueOf (card: Card) =
        function
        | StatusField -> card.Status |> Option.map statusName
        | WorkLevelField -> card.WorkLevel |> Option.map workLevelName
        | TrackField -> card.Track |> Option.map trackName
        | RoadmapField -> card.Roadmap

    let private withValue (now: DateTimeOffset) (card: Card) (field: Field) (target: string) =
        match field with
        | StatusField -> { card with Status = statusOf target; StatusUpdatedAt = Some now }
        | WorkLevelField -> { card with WorkLevel = workLevelOf target }
        | TrackField -> { card with Track = trackOf target }
        | RoadmapField -> { card with Roadmap = Some target }

    // ---- the intended-fields line (closed grammar) ----

    let parseIntendedLine (line: string) (rootAuthored: bool) =
        let cleaned = line.Replace("*", "").Replace("`", "").Trim()
        let parts = cleaned.Split('·') |> Array.map (fun part -> part.Trim()) |> Array.filter (fun part -> part <> "")
        let parsePart (state: IntendedFields option) (part: string) =
            match state with
            | None -> None
            | Some fields ->
                let colon = part.IndexOf ':'
                if colon <= 0 then None
                else
                    let key = part.Substring(0, colon).Trim()
                    let value = part.Substring(colon + 1).Trim()
                    match key with
                    | "Work level" when fields.WorkLevel.IsNone -> workLevelOf value |> Option.map (fun v -> { fields with WorkLevel = Some v })
                    | "Track" when fields.Track.IsNone -> trackOf value |> Option.map (fun v -> { fields with Track = Some v })
                    | "Intended Status" when fields.Status.IsNone -> statusOf value |> Option.map (fun v -> { fields with Status = Some v })
                    | _ -> None
        if parts.Length = 0 then None
        else
            parts
            |> Array.fold parsePart (Some { WorkLevel = None; Track = None; Status = None; RootAuthored = rootAuthored })

    // ---- derivation state ----

    type private Origin = { Round: int; Fallback: bool }

    type private Chain =
        { Preimage: string option
          Rules: string list
          Target: string
          Deps: Set<string * Field> }

    // What one rule evaluation decided for one field of one card.
    type private Decision =
        | Change of rule: string * target: string * fallback: bool * deps: (string * Field) list
        | Defer of rule: string * reason: DeferralReason
        | Propose of rule: string * reason: string * suggested: string option
        | Refuse of rule: string * reason: string
        | NoChange

    let private hoursBetween (later: DateTimeOffset) (earlier: DateTimeOffset) = (later - earlier).TotalHours

    let private noted (text: string option) =
        match text with
        | None -> None
        | Some value ->
            let trimmed = value.Trim().TrimEnd('.').Trim()
            if trimmed = "" then None else Some trimmed

    let private isNothing (blockedOn: string option) =
        match noted blockedOn with
        | Some value -> value.Equals("Nothing", StringComparison.OrdinalIgnoreCase) || value.Equals("Nothing external", StringComparison.OrdinalIgnoreCase)
        | None -> false

    let private hasEvidence (evidence: string option) =
        match noted evidence with
        | Some value -> not (value.Equals("None yet", StringComparison.OrdinalIgnoreCase))
        | None -> false

    let private expiryReason (rule: string) (field: Field) (reason: DeferralReason) =
        match rule, reason with
        | "T1d", _ -> "track-declared-pending"
        | "W1d", _ -> "work-level-declared-pending"
        | ("T1i" | "T1f"), _ -> "track-undetermined"
        | "O1", _ -> "status-undetermined"
        | "W1h", _ -> "work-level-undetermined"
        | _, _ -> fieldName field + "-undetermined"

    let derive (parameters: Parameters) (snapshot: Snapshot) : Plan =
        let now = snapshot.ObservedAt
        let original = snapshot.Cards |> List.map (fun card -> card.ItemId, card) |> Map.ofList
        let issueCards = snapshot.Cards |> List.filter (fun card -> card.Kind = Issue)
        let itemOfRef =
            issueCards
            |> List.filter (fun card -> card.Ref <> "")
            |> List.map (fun card -> card.Ref.ToLowerInvariant(), card.ItemId)
            |> Map.ofList
        let lookup (reference: string) = itemOfRef |> Map.tryFind (reference.ToLowerInvariant())
        // Depth along native parents that are on the board; a parent cycle marks its cards.
        let depthAndCycle (card: Card) =
            let rec walk (current: Card) (seen: Set<string>) depth =
                match current.Parent |> Option.bind lookup with
                | None -> depth, false
                | Some parentItem when seen.Contains parentItem -> depth, true
                | Some parentItem -> walk original.[parentItem] (seen.Add parentItem) (depth + 1)
            walk card (Set.singleton card.ItemId) 0
        let info = issueCards |> List.map (fun card -> card.ItemId, depthAndCycle card) |> Map.ofList
        let inCycle item = info |> Map.tryFind item |> Option.map snd |> Option.defaultValue false
        let maxDepth = if info.IsEmpty then 0 else info |> Map.toSeq |> Seq.map (fun (_, (d, _)) -> d) |> Seq.max
        let topDown =
            issueCards
            |> List.sortBy (fun card -> fst info.[card.ItemId], card.Ref, card.ItemId)
            |> List.map (fun card -> card.ItemId)
        let bottomUp = List.rev topDown

        let enabled rule = parameters.Enabled.Contains rule
        let mutable state = original
        let mutable chains: Map<string * Field, Chain> = Map.empty
        let mutable origins: Map<string * Field, Origin> = Map.empty

        let card item = state.[item]
        let hasP3Parent (c: Card) = c.Parent |> Option.bind lookup |> Option.isSome
        let structuralLeaf (c: Card) = List.isEmpty c.SubIssues
        let isOutcomeLike (c: Card) =
            match c.WorkLevel with
            | Some Outcome -> true
            | Some Delivery -> false
            | None -> not (structuralLeaf c)

        let settled (round: int) (item: string) (field: Field) =
            match valueOf (card item) field with
            | None -> false
            | Some _ ->
                match origins |> Map.tryFind (item, field) with
                | None -> true
                | Some origin -> not origin.Fallback && origin.Round < round

        // ---- Work level ----
        let evalWorkLevel (c: Card) =
            if c.WorkLevel.IsSome then NoChange
            else
                let declared = c.Intended |> Option.bind (fun i -> i.WorkLevel)
                if inCycle c.ItemId then
                    match declared with
                    | Some level -> Change("W1d", workLevelName level, false, [])
                    | None -> Propose("W1d", "work-level-undetermined", None)
                elif not (structuralLeaf c) then
                    if declared = Some Delivery then Refuse("W1h", "work-level-conflict")
                    else Change("W1h", "Outcome", false, [])
                else
                    match c.Parent with
                    | Some parent ->
                        if declared = Some Outcome then Refuse("W1h", "work-level-conflict")
                        // The settle window runs before the expiry clock, also for a leaf whose parent is off the board.
                        elif hoursBetween now c.CreatedAt < parameters.LeafSettleHours then Defer("W1h", SettleWindow)
                        elif (lookup parent).IsNone then Defer("W1h", InputPending)
                        else Change("W1h", "Delivery", false, [])
                    | None ->
                        match declared with
                        | Some level -> Change("W1d", workLevelName level, false, [])
                        | None -> Propose("W1d", "work-level-undetermined", None)

        let workLevelPending (c: Card) =
            c.WorkLevel.IsNone && (match evalWorkLevel c with Defer _ -> true | _ -> false)

        // ---- Track ----
        let evalTrack (round: int) (c: Card) =
            if c.Track.IsSome then NoChange
            else
                match c.Intended |> Option.bind (fun i -> i.Track) with
                | Some declared -> Change("T1d", trackName declared, false, [])
                | None when inCycle c.ItemId -> Propose("T1d", "track-undetermined", None)
                | None ->
                    match c.Parent |> Option.bind lookup with
                    | Some parentItem when structuralLeaf c ->
                        let parent = card parentItem
                        if parent.WorkLevel = Some Delivery then Propose("T1i", "track-undetermined", None)
                        else
                            match parent.Track with
                            | Some track -> Change("T1i", trackName track, false, [ parentItem, TrackField ])
                            | None -> Defer("T1i", InputPending)
                    | Some _ -> Propose("T1i", "track-undetermined", None)
                    | None ->
                        if workLevelPending c then Defer("T1f", InputPending)
                        elif c.WorkLevel.IsSome && not (settled round c.ItemId WorkLevelField) then Defer("T1f", InputPending)
                        else
                            let deps = if chains.ContainsKey(c.ItemId, WorkLevelField) then [ c.ItemId, WorkLevelField ] else []
                            Change("T1f", "Follow-up", true, deps)

        // ---- C1: close a Delivery whose strictly linked work is merged (decision 9) ----
        // Narrowed by the review of the decision-9 amendment (#5150): Status In review, a populated Work level
        // of Delivery, the last merge at least CloseSettleHours old and not continuing. A reopened issue is
        // never closed again (P-REOPENED proposes it instead).
        let c1Candidate (c: Card) =
            // Work level and Status as they are on the board, not as this run would fill them.
            let onBoard = original.[c.ItemId]
            if c.Kind <> Issue || not c.Open || onBoard.WorkLevel <> Some Delivery || onBoard.Status <> Some InReview || not c.TimelineComplete then None
            else
                let strict = c.Prs |> List.filter (fun pr -> pr.Linkage = Strict)
                let anyOpen = c.Prs |> List.exists (fun pr -> pr.State = PrOpen)
                let merged = strict |> List.filter (fun pr -> pr.State = PrMerged) |> List.sortBy (fun pr -> pr.MergedAt, pr.Id)
                match merged with
                | [] -> None
                | _ when anyOpen -> None
                | _ ->
                    let last = List.last merged
                    match last.MergedAt with
                    | Some mergedAt when not last.Continues && hoursBetween now mergedAt >= parameters.CloseSettleHours ->
                        Some(merged |> List.map (fun pr -> pr.Id))
                    | _ -> None
        let c1Applies (c: Card) = if c.TendClosedBefore then None else c1Candidate c

        // ---- Delivery Status ----
        let evalDeliveryStatus (c: Card) =
            if not c.Open then
                match c.Status, c.ClosedAt with
                | Some Done, _ -> NoChange
                | _, Some closed when (now - closed).TotalMinutes >= parameters.DoneSettleMinutes -> Change("S1", "Done", false, [])
                | _, Some _ -> NoChange
                | _, None -> Refuse("S1", "closed-at-unread") // closed with no closedAt: Unknown, never skipped
            else
                let strictOpen = c.Prs |> List.filter (fun pr -> pr.Linkage = Strict && pr.State = PrOpen && not pr.Draft)
                let strictDraft = c.Prs |> List.filter (fun pr -> pr.Linkage = Strict && pr.State = PrOpen && pr.Draft)
                let strictMerged = c.Prs |> List.filter (fun pr -> pr.Linkage = Strict && pr.State = PrMerged)
                let loose = c.Prs |> List.filter (fun pr -> (match pr.Linkage with Loose _ -> true | Strict -> false) && pr.State <> PrClosed)
                let looseOpen = loose |> List.filter (fun pr -> pr.State = PrOpen)
                let noOpen = List.isEmpty strictOpen && List.isEmpty strictDraft
                match c.Status with
                | Some Done -> Propose("S2", "done-open", None)
                | Some (Backlog | Ready | InProgress) when not (List.isEmpty strictOpen) -> Change("S3", "In review", false, [])
                | Some (Backlog | Ready) when List.isEmpty strictOpen && not (List.isEmpty strictDraft) -> Change("S3b", "In progress", false, [])
                | Some _ when noOpen && not (List.isEmpty looseOpen) ->
                    let first = looseOpen |> List.sortBy (fun pr -> pr.Id) |> List.head
                    let reason = match first.Linkage with Loose why -> why | Strict -> "loose"
                    Propose("P-LINK", "loose-link:" + reason, None)
                | Some InReview when noOpen && List.isEmpty strictMerged && List.isEmpty loose ->
                    if not c.TimelineComplete then Refuse("S4", "timeline-incomplete")
                    else
                        match c.StatusUpdatedAt with
                        | Some updated when hoursBetween now updated >= parameters.ReviewSettleHours -> Change("S4", "In progress", false, [])
                        | _ -> NoChange
                // An enabled C1 closes the card; nothing else is decided for its Status.
                | Some _ when enabled "C1" && (c1Applies c).IsSome -> NoChange
                | Some (InReview | InProgress) when noOpen && not (List.isEmpty strictMerged) && hasEvidence c.AcceptanceEvidence ->
                    if not c.TimelineComplete then Refuse("P-ACCEPT", "timeline-incomplete")
                    else Propose("P-ACCEPT", "acceptance-ready", None)
                | Some InProgress when noOpen ->
                    let activity =
                        [ yield c.UpdatedAt
                          yield! Option.toList c.StatusUpdatedAt
                          yield! c.Prs |> List.map (fun pr -> pr.UpdatedAt) ]
                        |> List.max
                    if not c.TimelineComplete then Refuse("S5", "timeline-incomplete")
                    elif hoursBetween now activity >= parameters.ActivityHours then Propose("S5", "stale-active", None)
                    else NoChange
                | Some Backlog when isNothing c.BlockedOn ->
                    if not c.BlockersComplete then Refuse("P-READY", "blockers-unread")
                    elif c.Blockers |> List.exists (fun b -> b.Open) then NoChange
                    else Propose("P-READY", (if c.Enrolled then "blocked-on-nothing" else "blocked-on-nothing-unenrolled"), Some "Ready")
                | Some Blocked ->
                    match c.BlockersComplete, c.MarkerBlockers with
                    | true, Some marker ->
                        let named = c.Blockers @ marker
                        if List.isEmpty named then Propose("P-NOBLOCKER", "no-blocker", None)
                        elif named |> List.forall (fun b -> not b.Open) then Propose("P-UNBLOCK", "blockers-closed", Some "Ready")
                        else NoChange
                    | _ -> Refuse("P-UNBLOCK", "blockers-unread")
                | None when inCycle c.ItemId ->
                    match c.Intended |> Option.bind (fun i -> i.Status) with
                    | Some Backlog -> Change("S9", "Backlog", true, [])
                    | Some Ready when c.Intended.Value.RootAuthored -> Change("S9r", "Ready", false, [])
                    | _ -> Propose("S9", "status-undetermined", None)
                | None ->
                    match c.Intended |> Option.bind (fun i -> i.Status) with
                    | Some Ready when c.Intended.Value.RootAuthored -> Change("S9r", "Ready", false, [])
                    | _ -> Change("S9", "Backlog", true, [])
                | Some _ -> NoChange

        // P-INTENT reads the ORIGINAL Status: S9 has filled Backlog by the final round.
        let intentProposal (c: Card) =
            let first = original.[c.ItemId]
            match first.Open, first.Status, first.Intended with
            | true, None, Some intended ->
                match intended.Status with
                | Some Backlog | None -> None
                | Some Ready when intended.RootAuthored -> None
                | Some declared -> Some("P-INTENT", "intended-" + (statusName declared).ToLowerInvariant().Replace(' ', '-'), Some(statusName declared))
            | _ -> None

        // ---- Outcome Status (the roll-up) ----
        let evalOutcomeStatus (round: int) (c: Card) =
            if not c.Open then evalDeliveryStatus c // S1 alone applies to a closed card
            elif c.Status = Some Done then Propose("S2", "done-open", None)
            elif inCycle c.ItemId then evalDeliveryStatus c
            elif not c.SubIssuesComplete then Refuse("O1", "children-incomplete")
            else
                let openChildren = c.SubIssues |> List.filter snd |> List.map fst
                let closedChildren = c.SubIssues |> List.filter (snd >> not)
                let resolved = openChildren |> List.map (fun reference -> reference, lookup reference |> Option.map card)
                if resolved |> List.exists (fun (_, child) -> match child with Some child -> child.Status.IsNone | None -> true) then
                    Refuse("O1", "children-incomplete")
                elif resolved |> List.exists (fun (_, child) -> child.Value.Status = Some Done) then Refuse("O1", "child-done-open")
                else
                    let statuses = resolved |> List.map (fun (_, child) -> child.Value.Status.Value)
                    if List.isEmpty statuses then
                        if List.isEmpty closedChildren then Propose("O1", "no-open-delivery", None)
                        else Propose("O1", "children-complete", None)
                    else
                        let desired =
                            if statuses |> List.exists (fun s -> s = InProgress || s = InReview) then InProgress
                            elif statuses |> List.contains Ready then Ready
                            elif statuses |> List.contains Backlog then Backlog
                            else Blocked
                        let childDeps =
                            resolved
                            |> List.choose (fun (_, child) ->
                                let item = child.Value.ItemId
                                if chains.ContainsKey(item, StatusField) then Some(item, StatusField) else None)
                        let writable = function None | Some Backlog | Some Ready | Some InProgress -> true | _ -> false
                        match desired with
                        | Backlog | Ready | InProgress when writable c.Status && c.Status <> Some desired ->
                            Change("O1", statusName desired, false, childDeps)
                        | Backlog | Ready | InProgress -> NoChange
                        | _ when c.Status.IsNone ->
                            let childrenSettled = resolved |> List.forall (fun (_, child) -> settled round child.Value.ItemId StatusField)
                            if childrenSettled then Change("O1", "Backlog", true, childDeps) else Defer("O1", InputPending)
                        | _ -> NoChange

        // Proposals the roll-up adds beside (or instead of) its effect, read from the final state.
        let rollupProposals (c: Card) =
            if not c.Open || c.Status = Some Done || inCycle c.ItemId || not c.SubIssuesComplete then []
            else
                let children =
                    c.SubIssues |> List.filter snd |> List.map fst |> List.map (lookup >> Option.map card)
                if children |> List.exists (fun child -> match child with Some child -> child.Status.IsNone || child.Status = Some Done | None -> true) then []
                else
                    let statuses = children |> List.map (fun child -> child.Value.Status.Value)
                    if List.isEmpty statuses then []
                    elif statuses |> List.forall (fun s -> s = InReview) then [ "O1", "all-children-in-review", Some "In review" ]
                    elif statuses |> List.forall (fun s -> s = Blocked) && c.Status <> Some Blocked then [ "O1", "rollup-blocked", Some "Blocked" ]
                    elif c.Status = Some Blocked then
                        let desired =
                            if statuses |> List.exists (fun s -> s = InProgress || s = InReview) then InProgress
                            elif statuses |> List.contains Ready then Ready
                            elif statuses |> List.contains Backlog then Backlog
                            else Blocked
                        if desired <> Blocked then [ "O1", "rollup-from-blocked", Some(statusName desired) ] else []
                    else []

        // ---- Roadmap ----
        let evalRoadmap (c: Card) =
            if not c.Enrolled then NoChange
            else
                match c.Roadmap, c.EnrolledRoadmap with
                | None, Some enrolled -> Change("RM1", enrolled, false, [])
                | Some present, Some enrolled when present <> enrolled -> Refuse("RM1", "roadmap-conflicts-with-enrollment")
                | _ -> NoChange

        // ---- one round ----
        let evaluate (round: int) (record: string -> Field -> Decision -> unit) =
            for item in topDown do
                let c = card item
                record item WorkLevelField (evalWorkLevel c)
                let c = card item
                record item TrackField (evalTrack round c)
                let c = card item
                if not (isOutcomeLike c) then record item StatusField (evalDeliveryStatus c)
                record item RoadmapField (evalRoadmap (card item))
            for item in bottomUp do
                let c = card item
                if isOutcomeLike c then record item StatusField (evalOutcomeStatus round c)

        let applyChange round item field rule target fallback (deps: (string * Field) list) =
            let before = card item
            let chain =
                match chains |> Map.tryFind (item, field) with
                | Some existing -> { existing with Rules = existing.Rules @ [ rule ]; Target = target; Deps = existing.Deps + Set.ofList deps }
                | None -> { Preimage = valueOf original.[item] field; Rules = [ rule ]; Target = target; Deps = Set.ofList deps }
            chains <- chains.Add((item, field), chain)
            origins <- origins.Add((item, field), { Round = round; Fallback = fallback })
            state <- state.Add(item, withValue now before field target)

        // At most hierarchy depth plus 2 rounds may change something; one more round must change nothing.
        let cap = maxDepth + 2
        let mutable round = 0
        let mutable changed = true
        while changed && round <= cap do
            round <- round + 1
            changed <- false
            evaluate round (fun item field decision ->
                match decision with
                | Change(rule, target, fallback, deps) when enabled rule && valueOf (card item) field <> Some target ->
                    applyChange round item field rule target fallback deps
                    changed <- true
                | _ -> ())
        let converged = not changed

        // The final round decides deferrals, proposals, refusals and would-apply effects on the final state.
        let finalRound = round + 1
        let deferred = Collections.Generic.List<Deferred>()
        let proposals = Collections.Generic.Dictionary<string, Proposal>()
        let refusals = Collections.Generic.Dictionary<string, Refusal>()
        let would = Collections.Generic.Dictionary<string, Effect>()
        let ledger = snapshot.Ledger |> List.map (fun entry -> entry.Key, entry) |> Map.ofList
        let reference item = original.[item].Ref
        let propose rule ref reason suggested =
            let id = rule + ":" + ref + ":" + reason
            proposals.[id] <- { Id = id; Ref = ref; Rule = rule; Reason = reason; Suggested = suggested }
        let refuse rule ref reason =
            let id = rule + ":" + ref + ":" + reason
            refusals.[id] <- { Id = id; Ref = ref; Rule = rule; Reason = reason }
        // A rule that is not enabled expires into a proposal only for T1d, T1i and W1d, whose pending field
        // people must decide. Every other disabled rule stays a would-apply entry: never counted, never posted.
        let expiringWhenDisabled = set [ "T1d"; "T1i"; "W1d" ]
        let addDeferred item field rule reason =
            let key = rule + ":" + item + ":" + fieldName field
            let counts =
                match reason with
                | SettleWindow -> false
                | RuleDisabled -> expiringWhenDisabled.Contains rule
                | InputPending -> true
            let label =
                match reason with
                | RuleDisabled when not counts -> "rule-disabled:" + rule
                | SettleWindow -> "settle-window:" + rule
                | _ -> expiryReason rule field reason
            let expired =
                counts
                && (match ledger |> Map.tryFind key with
                    | Some entry -> entry.Runs >= parameters.ExpiryRuns - 1 || hoursBetween now entry.FirstDeferredAt >= parameters.ExpiryHours
                    | None -> false)
            deferred.Add { Key = key; ItemId = item; Ref = reference item; Field = field; Rule = rule; Reason = reason; Counts = counts; Expired = expired; Label = label }
            if expired then propose rule (reference item) label None
        evaluate finalRound (fun item field decision ->
            match decision with
            | Change(rule, target, _, deps) when not (enabled rule) && valueOf (card item) field <> Some target ->
                let id = rule + ":" + item + ":" + fieldName field + ":" + target
                would.[id] <-
                    { Id = id; ItemId = item; Ref = reference item; Field = field; Preimage = valueOf (card item) field; Target = target
                      Rules = [ rule ]; DependsOn = deps |> List.map (fun (i, f) -> i + ":" + fieldName f) }
                addDeferred item field rule RuleDisabled
            | Change _ | NoChange -> ()
            | Defer(rule, reason) -> addDeferred item field rule reason
            | Propose(rule, reason, suggested) -> propose rule (reference item) reason suggested
            | Refuse(rule, reason) -> refuse rule (reference item) reason)
        for item in topDown do
            let c = card item
            // The roll-up decides an Outcome's Status, so its intended Status is never proposed.
            match (if isOutcomeLike original.[item] then None else intentProposal c) with
            | Some(rule, reason, suggested) -> propose rule c.Ref reason suggested
            | None -> ()
            if isOutcomeLike c then
                for rule, reason, suggested in rollupProposals c do propose rule c.Ref reason suggested
        for c in snapshot.Cards do
            match c.Kind with
            | Issue -> ()
            | PullRequestCard -> refuse "R7" (if c.Ref = "" then c.ItemId else c.Ref) "pull-request-card"
            | DraftCard -> refuse "R7" c.ItemId "draft"
            | Unreadable -> refuse "U" c.ItemId "unreadable"
        if not converged then refuse "fixpoint" "board" "fixpoint-not-reached"

        let effectId (item, field) (chain: Chain) = String.Join("+", chain.Rules) + ":" + item + ":" + fieldName field + ":" + chain.Target
        // A chain that moved a field away and back is no effect, and nothing may depend on it.
        let kept = chains |> Map.filter (fun _ chain -> chain.Preimage <> Some chain.Target)
        let effects =
            if not converged then []
            else
                kept
                |> Map.toList
                |> List.map (fun ((item, field), chain) ->
                    { Id = effectId (item, field) chain
                      ItemId = item
                      Ref = reference item
                      Field = field
                      Preimage = chain.Preimage
                      Target = chain.Target
                      Rules = chain.Rules
                      DependsOn =
                        chain.Deps
                        |> Set.toList
                        |> List.choose (fun key -> kept |> Map.tryFind key |> Option.map (effectId key))
                        |> List.sort })
                |> List.sortBy (fun effect -> effect.Id)

        let closures =
            if not converged then []
            else
                topDown
                |> List.choose (fun item ->
                    c1Applies (card item)
                    |> Option.map (fun prs -> { Id = "C1:" + item + ":closed"; ItemId = item; Ref = reference item; MergedPrs = prs }))
        for item in topDown do
            let c = card item
            if c.TendClosedBefore && (c1Candidate c).IsSome then propose "P-REOPENED" c.Ref "reopened-after-close" None

        // T1i against people's Tracks on existing undeclared leaf-under-Outcome pairs (original values only).
        let comparison =
            issueCards
            |> List.choose (fun child ->
                match child.Track, child.Intended |> Option.bind (fun i -> i.Track), child.Parent |> Option.bind lookup with
                | Some childTrack, None, Some parentItem when structuralLeaf child ->
                    let parent = original.[parentItem]
                    match parent.Track with
                    | Some parentTrack when parent.WorkLevel <> Some Delivery -> Some(childTrack <> parentTrack)
                    | _ -> None
                | _ -> None)
        { Ruleset = ruleset
          ObservedAt = now
          Apply = effects
          WouldApply = would.Values |> Seq.sortBy (fun e -> e.Id) |> List.ofSeq
          Deferred = deferred |> Seq.distinctBy (fun d -> d.Key) |> Seq.sortBy (fun d -> d.Key) |> List.ofSeq
          Propose = proposals.Values |> Seq.sortBy (fun p -> p.Id) |> List.ofSeq
          Refuse = refusals.Values |> Seq.sortBy (fun r -> r.Id) |> List.ofSeq
          T1iComparison = { Pairs = comparison.Length; Disagreements = comparison |> List.filter id |> List.length }
          Close = (if enabled "C1" then closures else [])
          WouldClose = (if enabled "C1" then [] else closures) }

    let applyPlan (plan: Plan) (snapshot: Snapshot) =
        let byItem = plan.Apply |> List.groupBy (fun effect -> effect.ItemId) |> Map.ofList
        let closed = plan.Close |> List.map (fun closure -> closure.ItemId) |> Set.ofList
        { snapshot with
            Cards =
                snapshot.Cards
                |> List.map (fun card ->
                    let card =
                        match byItem |> Map.tryFind card.ItemId with
                        | None -> card
                        | Some effects -> effects |> List.fold (fun c effect -> withValue snapshot.ObservedAt c effect.Field effect.Target) card
                    if closed.Contains card.ItemId then { card with Open = false; ClosedAt = Some snapshot.ObservedAt } else card) }

    let nextLedger (plan: Plan) (previous: Deferral list) (now: DateTimeOffset) =
        let prior = previous |> List.map (fun entry -> entry.Key, entry) |> Map.ofList
        plan.Deferred
        |> List.filter (fun d -> d.Counts)
        |> List.map (fun d ->
            match prior |> Map.tryFind d.Key with
            | Some entry -> { entry with Runs = entry.Runs + 1 }
            | None -> { Key = d.Key; FirstDeferredAt = now; Runs = 1 })
        |> List.sortBy (fun entry -> entry.Key)

    let dependantsOf (plan: Plan) (effectId: string) =
        let rec close (found: Set<string>) (frontier: string list) =
            match frontier with
            | [] -> found
            | current :: rest ->
                let direct =
                    plan.Apply
                    |> List.filter (fun e -> e.DependsOn |> List.contains current && not (found.Contains e.Id))
                    |> List.map (fun e -> e.Id)
                close (found + Set.ofList direct) (rest @ direct)
        close Set.empty [ effectId ] |> Set.toList

    // ---- fixture decoding ----

    let decodeSnapshot (json: string) : Result<Snapshot, string> =
        try
            use document = JsonDocument.Parse json
            let root = document.RootElement
            if root.GetProperty("schema").GetString() <> "fsgg.board-tend-snapshot/1" then failwith "unsupported schema"
            let observed = root.GetProperty("observedAt").GetDateTimeOffset()
            let tryProp (element: JsonElement) (name: string) =
                match element.TryGetProperty name with
                | true, value when value.ValueKind <> JsonValueKind.Null -> Some value
                | _ -> None
            let text element name = tryProp element name |> Option.map (fun v -> v.GetString())
            let time element name = tryProp element name |> Option.map (fun v -> v.GetDateTimeOffset())
            let flag element name fallback = tryProp element name |> Option.map (fun v -> v.GetBoolean()) |> Option.defaultValue fallback
            let required (parse: string -> 'a option) (what: string) (value: string) =
                match parse value with Some parsed -> parsed | None -> failwithf "unknown %s %s" what value
            let blockers (element: JsonElement) =
                [ for b in element.EnumerateArray() -> { Ref = b.GetProperty("ref").GetString(); Open = b.GetProperty("open").GetBoolean() } ]
            let card (c: JsonElement) =
                let created = time c "createdAt" |> Option.defaultValue (observed.AddDays -30.0)
                { ItemId = c.GetProperty("itemId").GetString()
                  Ref = text c "ref" |> Option.defaultValue ""
                  Kind =
                    match text c "kind" |> Option.defaultValue "issue" with
                    | "issue" -> Issue
                    | "pullRequest" -> PullRequestCard
                    | "draft" -> DraftCard
                    | "unreadable" -> Unreadable
                    | other -> failwithf "unknown kind %s" other
                  Open = flag c "open" true
                  CreatedAt = created
                  UpdatedAt = time c "updatedAt" |> Option.defaultValue created
                  ClosedAt = time c "closedAt"
                  Status = text c "status" |> Option.map (required statusOf "status")
                  StatusUpdatedAt = time c "statusUpdatedAt"
                  WorkLevel = text c "workLevel" |> Option.map (required workLevelOf "work level")
                  Track = text c "track" |> Option.map (required trackOf "track")
                  Roadmap = text c "roadmap"
                  Parent = text c "parent"
                  SubIssues =
                    tryProp c "subIssues"
                    |> Option.map (fun a -> [ for s in a.EnumerateArray() -> s.GetProperty("ref").GetString(), s.GetProperty("open").GetBoolean() ])
                    |> Option.defaultValue []
                  SubIssuesComplete = flag c "subIssuesComplete" true
                  Blockers = tryProp c "blockers" |> Option.map blockers |> Option.defaultValue []
                  BlockersComplete = flag c "blockersComplete" true
                  MarkerBlockers =
                    match c.TryGetProperty "markerBlockers" with
                    | true, value when value.ValueKind = JsonValueKind.Null -> None
                    | true, value -> Some(blockers value)
                    | _ -> Some []
                  Prs =
                    tryProp c "prs"
                    |> Option.map (fun a ->
                        [ for pr in a.EnumerateArray() ->
                            { Id = pr.GetProperty("id").GetString()
                              State =
                                match pr.GetProperty("state").GetString() with
                                | "open" -> PrOpen
                                | "closed" -> PrClosed
                                | "merged" -> PrMerged
                                | other -> failwithf "unknown PR state %s" other
                              Draft = flag pr "draft" false
                              Linkage =
                                match tryProp pr "loose" with
                                | Some reason -> Loose(reason.GetString())
                                | None -> Strict
                              UpdatedAt = time pr "updatedAt" |> Option.defaultValue created
                              MergedAt = time pr "mergedAt"
                              Continues = flag pr "continues" false } ])
                    |> Option.defaultValue []
                  TimelineComplete = flag c "timelineComplete" true
                  Intended = text c "intendedLine" |> Option.bind (fun line -> parseIntendedLine line (flag c "rootAuthored" false))
                  BlockedOn = text c "blockedOn"
                  AcceptanceEvidence = text c "acceptanceEvidence"
                  Enrolled = flag c "enrolled" false
                  EnrolledRoadmap = text c "enrolledRoadmap"
                  TendClosedBefore = flag c "tendClosedBefore" false }
            let ledger =
                tryProp root "ledger"
                |> Option.map (fun a ->
                    [ for entry in a.EnumerateArray() ->
                        { Key = entry.GetProperty("key").GetString()
                          FirstDeferredAt = entry.GetProperty("firstDeferredAt").GetDateTimeOffset()
                          Runs = entry.GetProperty("runs").GetInt32() } ])
                |> Option.defaultValue []
            Ok { ObservedAt = observed; Cards = [ for c in root.GetProperty("cards").EnumerateArray() -> card c ]; Ledger = ledger }
        with error -> Error("snapshot decode failed: " + error.Message)

    // ---- plan encoding (fsgg.board-tend-plan/1) ----

    let encodePlan (plan: Plan) =
        use stream = new IO.MemoryStream()
        (
            use w = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))
            let opt (name: string) (value: string option) =
                match value with
                | Some v -> w.WriteString(name, v)
                | None -> w.WriteNull name
            let strings (name: string) (values: string list) =
                w.WriteStartArray name
                for v in values do w.WriteStringValue v
                w.WriteEndArray()
            let effects (name: string) (items: Effect list) =
                w.WriteStartArray name
                for e in items do
                    w.WriteStartObject()
                    w.WriteString("id", e.Id); w.WriteString("item", e.ItemId); w.WriteString("ref", e.Ref)
                    w.WriteString("field", fieldName e.Field); opt "preimage" e.Preimage; w.WriteString("target", e.Target)
                    strings "rules" e.Rules; strings "dependsOn" e.DependsOn
                    w.WriteEndObject()
                w.WriteEndArray()
            let closures (name: string) (items: Closure list) =
                w.WriteStartArray name
                for c in items do
                    w.WriteStartObject()
                    w.WriteString("id", c.Id); w.WriteString("item", c.ItemId); w.WriteString("ref", c.Ref); strings "mergedPrs" c.MergedPrs
                    w.WriteEndObject()
                w.WriteEndArray()
            w.WriteStartObject()
            w.WriteString("schema", "fsgg.board-tend-plan/1")
            w.WriteString("ruleset", plan.Ruleset)
            w.WriteString("observedAt", plan.ObservedAt.ToString("O"))
            effects "apply" plan.Apply
            effects "wouldApply" plan.WouldApply
            closures "close" plan.Close
            closures "wouldClose" plan.WouldClose
            w.WriteStartArray "deferred"
            for d in plan.Deferred do
                w.WriteStartObject()
                w.WriteString("key", d.Key); w.WriteString("ref", d.Ref); w.WriteString("field", fieldName d.Field); w.WriteString("rule", d.Rule)
                w.WriteString("reason", (match d.Reason with InputPending -> "input-pending" | RuleDisabled -> "rule-disabled" | SettleWindow -> "settle-window"))
                w.WriteBoolean("counts", d.Counts); w.WriteBoolean("expired", d.Expired); w.WriteString("label", d.Label)
                w.WriteEndObject()
            w.WriteEndArray()
            w.WriteStartArray "propose"
            for p in plan.Propose do
                w.WriteStartObject()
                w.WriteString("id", p.Id); w.WriteString("ref", p.Ref); w.WriteString("rule", p.Rule); w.WriteString("reason", p.Reason); opt "suggested" p.Suggested
                w.WriteEndObject()
            w.WriteEndArray()
            w.WriteStartArray "refuse"
            for r in plan.Refuse do
                w.WriteStartObject()
                w.WriteString("id", r.Id); w.WriteString("ref", r.Ref); w.WriteString("rule", r.Rule); w.WriteString("reason", r.Reason)
                w.WriteEndObject()
            w.WriteEndArray()
            w.WriteStartObject "t1iComparison"
            w.WriteNumber("pairs", plan.T1iComparison.Pairs); w.WriteNumber("disagreements", plan.T1iComparison.Disagreements)
            w.WriteEndObject()
            w.WriteEndObject())
        Text.Encoding.UTF8.GetString(stream.ToArray())
