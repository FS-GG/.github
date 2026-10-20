namespace FS.GG.Coord

/// A read-only attention reducer for one already admitted original CI operation.
module OperationObservation =
    open System
    open System.Security.Cryptography
    open System.Text
    open System.Text.RegularExpressions

    type Identity =
        { Issue: string; Slice: string; Attempt: string; Owner: string
          SourceRevision: string; IssueUpdatedAt: string; IssueBodySha256: string; Repository: string; PullRequest: int; Head: string
          RunId: int64; RunAttempt: int; PopulationRevision: string; Unknowns: string list }

    type Budget =
        { ClockDomain: string; Started: int64; WorkDeadline: int64; CleanupDeadline: int64
          LastClock: int64; RequestsRemaining: int; OutputBytesRemaining: int }

    type Check =
        { Id: int64; Name: string; Status: string; Conclusion: string option
          StartedAt: string option; CompletedAt: string option }

    type Sample =
        { Head: string; RunId: int64; RunAttempt: int; RunStatus: string
          RunConclusion: string option; PullRequestState: string; Merged: bool; Draft: bool
          Checks: Check list; Complete: bool; Gaps: string list }

    type Attention =
        | Completed | Failed | PullRequestClosed | HeadChanged | RunChanged
        | PopulationChanged | MaterialChange | Unknown | Deadline | BudgetExhausted
        | Interrupted | ObservationFailure

    type Cursor =
        { Identity: Identity; Budget: Budget; Population: string option; Semantic: string option; Control: string option
          Revision: int; Terminal: Attention option; Evidence: string option; Gap: string option }

    type Decision =
        | ObserveOriginal of Cursor
        | AwaitWithinDeadline of Cursor * milliseconds: int
        | ReturnAttention of Cursor * Attention * terminalIdentity: string

    let private digest (value: string) =
        SHA256.HashData(Encoding.UTF8.GetBytes value) |> Convert.ToHexString |> _.ToLowerInvariant()

    let private hash value = not (isNull value) && Regex.IsMatch(value, "\\A[0-9a-f]{64}\\z")
    let private sha value = not (isNull value) && Regex.IsMatch(value, "\\A[0-9a-f]{40}\\z")
    let private text value = not (String.IsNullOrWhiteSpace value) && value.Length <= 512 && not (value.Contains '\u0000')

    let validateIdentity (identity: Identity) =
        if not ([ identity.Issue; identity.Slice; identity.Attempt; identity.Owner; identity.PopulationRevision; identity.IssueUpdatedAt ] |> List.forall text)
           || not (Regex.IsMatch(identity.Issue, "\\A[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+#[1-9][0-9]*\\z"))
           || not (text identity.IssueBodySha256) || not (Regex.IsMatch(identity.IssueBodySha256, "\\A[0-9a-f]{64}\\z"))
           || not (sha identity.SourceRevision && sha identity.Head)
           || not (text identity.Repository) || not (Regex.IsMatch(identity.Repository, "\\A[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+\\z"))
           || identity.Repository.Length > 256 || identity.PullRequest <= 0 || identity.RunId <= 0L || identity.RunAttempt <= 0
           || identity.Unknowns.Length > 512 || not (identity.Unknowns |> List.forall text)
           || Set.count (Set.ofList identity.Unknowns) <> identity.Unknowns.Length then
            Error "invalid original operation identity or retained Unknown population"
        else Ok identity

    let start identity domain now seconds =
        match validateIdentity identity with
        | Error reason -> Error reason
        | Ok _ when not (text domain) || now < 0L || seconds < 1 || seconds > 600 || now > Int64.MaxValue - 600000L ->
            Error "unsupported clock or observation allowance (1..600 seconds)"
        | Ok _ ->
            Ok { Identity = identity
                 Budget = { ClockDomain = domain; Started = now; LastClock = now
                            WorkDeadline = now + int64 seconds * 1000L - 500L
                            CleanupDeadline = now + int64 seconds * 1000L
                            RequestsRemaining = 384; OutputBytesRemaining = 4194304 }
                 Population = None; Semantic = None; Control = None; Revision = 0; Terminal = None; Evidence = None; Gap = None }

    let resume expected domain now (cursor: Cursor) =
        match validateIdentity cursor.Identity with
        | Error reason -> Error reason
        | Ok _ ->
            let budget = cursor.Budget
            if cursor.Identity <> expected then Error "original operation identity drift"
            elif domain <> budget.ClockDomain || now < budget.LastClock then Error "original clock domain changed or moved backwards"
            elif budget.Started < 0L || budget.LastClock < budget.Started || budget.WorkDeadline <= budget.Started
                 || budget.CleanupDeadline - budget.WorkDeadline <> 500L
                 || budget.CleanupDeadline - budget.Started > 600000L
                 || budget.LastClock > budget.CleanupDeadline
                 || budget.RequestsRemaining < 0 || budget.RequestsRemaining > 384
                 || budget.OutputBytesRemaining < 0 || budget.OutputBytesRemaining > 4194304
                 || cursor.Revision < 0 || cursor.Revision > 384
                 || not (cursor.Population |> Option.forall hash) || not (cursor.Semantic |> Option.forall hash)
                 || not (cursor.Control |> Option.forall hash)
                 || not (cursor.Gap |> Option.forall text)
                 || not (cursor.Evidence |> Option.forall (fun path -> not (String.IsNullOrWhiteSpace path) && path.Length <= 2048))
                 || Option.isSome cursor.Population <> Option.isSome cursor.Semantic
                 || Option.isSome cursor.Population <> Option.isSome cursor.Control then Error "corrupt original continuation"
            elif now >= budget.WorkDeadline then Error "original continuation expired; no renewed allowance"
            else Ok { cursor with Budget = { budget with LastClock = now } }

    let attentionName = function
        | Completed -> "completed" | Failed -> "failed" | PullRequestClosed -> "pull-request-closed"
        | HeadChanged -> "head-changed" | RunChanged -> "run-changed" | PopulationChanged -> "population-changed"
        | MaterialChange -> "material-change" | Unknown -> "unknown" | Deadline -> "deadline"
        | BudgetExhausted -> "budget-exhausted" | Interrupted -> "interrupted" | ObservationFailure -> "observation-failure"

    let private attentionIdentity (cursor: Cursor) attention =
        // Unambiguous canonical fields: length-prefixed strings, including every original Unknown.
        let fields = [ cursor.Identity.Issue; cursor.Identity.Slice; cursor.Identity.Attempt; cursor.Identity.Owner
                       cursor.Identity.SourceRevision; cursor.Identity.Repository; string cursor.Identity.PullRequest
                       cursor.Identity.Head; string cursor.Identity.RunId; string cursor.Identity.RunAttempt
                       cursor.Identity.PopulationRevision; cursor.Budget.ClockDomain; string cursor.Budget.Started
                       string cursor.Budget.WorkDeadline; string cursor.Revision; attentionName attention
                       Option.defaultValue "" cursor.Population; Option.defaultValue "" cursor.Semantic
                       cursor.Identity.IssueUpdatedAt; cursor.Identity.IssueBodySha256 ] @ cursor.Identity.Unknowns
        fields |> List.map (fun field -> string field.Length + ":" + field) |> String.concat "" |> digest

    let finish attention (cursor: Cursor) =
        let retained = { cursor with Terminal = Some attention }
        ReturnAttention(retained, attention, attentionIdentity retained attention)

    let private canonicalChecks (checks: Check list) =
        checks |> List.sortBy _.Id |> List.map (fun check ->
            [ string check.Id; check.Name; check.Status; Option.defaultValue "" check.Conclusion
              Option.defaultValue "" check.StartedAt; Option.defaultValue "" check.CompletedAt ]
            |> List.map (fun field -> string field.Length + ":" + field) |> String.concat "") |> String.concat ""

    let decide now (sample: Sample option) (cursor: Cursor) =
        let backwards = now < cursor.Budget.LastClock
        let cursor = { cursor with Budget = { cursor.Budget with LastClock = now } }
        match cursor.Terminal with
        | _ when backwards || now < cursor.Budget.Started || now >= cursor.Budget.WorkDeadline -> finish Deadline cursor
        | Some attention -> finish attention cursor
        | None when cursor.Budget.RequestsRemaining <= 0 || cursor.Budget.OutputBytesRemaining < 4096 -> finish BudgetExhausted cursor
        | None ->
            match sample with
            | None -> ObserveOriginal cursor
            | Some sample when sample.Head <> cursor.Identity.Head -> finish HeadChanged cursor
            | Some sample when sample.RunId <> cursor.Identity.RunId || sample.RunAttempt <> cursor.Identity.RunAttempt -> finish RunChanged cursor
            | Some sample when not sample.Complete || not sample.Gaps.IsEmpty || sample.Checks.IsEmpty || sample.Checks.Length > 128
                               || Set.count (sample.Checks |> List.map _.Id |> Set.ofList) <> sample.Checks.Length -> finish Unknown cursor
            | Some sample ->
                let population = sample.Checks |> List.map _.Id |> List.sort |> List.map string |> String.concat "," |> digest
                let semantic =
                    [ sample.Head; string sample.RunId; string sample.RunAttempt; sample.RunStatus
                      Option.defaultValue "" sample.RunConclusion; sample.PullRequestState; string sample.Merged; string sample.Draft
                      canonicalChecks sample.Checks ] |> String.concat "\n" |> digest
                let control = [ sample.PullRequestState; string sample.Merged; string sample.Draft ] |> String.concat ":" |> digest
                let controlChanged = cursor.Control.IsSome && cursor.Control <> Some control
                let changed = cursor.Semantic <> Some semantic
                let next = { cursor with Population = Some population; Semantic = Some semantic; Control = Some control
                                         Revision = cursor.Revision + (if changed then 1 else 0) }
                let failed _status = function
                    | Some ("failure" | "cancelled" | "timed_out" | "action_required" | "startup_failure" | "stale") -> true
                    | _ -> false
                let knownStatus status = List.contains status [ "queued"; "in_progress"; "completed"; "waiting"; "pending"; "requested" ]
                let successful status conclusion = status = "completed" && List.contains conclusion [ Some "success"; Some "neutral"; Some "skipped" ]
                if not (knownStatus sample.RunStatus) || (sample.Checks |> List.exists (fun check -> not (knownStatus check.Status))) then finish Unknown next
                elif cursor.Population.IsSome && cursor.Population <> Some population then finish PopulationChanged next
                elif sample.PullRequestState = "closed" then finish PullRequestClosed next
                elif sample.PullRequestState <> "open" then finish Unknown next
                elif failed sample.RunStatus sample.RunConclusion || (sample.Checks |> List.exists (fun check -> failed check.Status check.Conclusion)) then finish Failed next
                elif successful sample.RunStatus sample.RunConclusion && (sample.Checks |> List.forall (fun check -> successful check.Status check.Conclusion)) then finish Completed next
                elif sample.RunStatus = "completed" || (sample.Checks |> List.exists (fun check -> check.Status = "completed" && not (successful check.Status check.Conclusion))) then finish Unknown next
                elif controlChanged then finish MaterialChange next
                else AwaitWithinDeadline(next, int (min 5000L (max 0L (next.Budget.WorkDeadline - now))))

    /// Charge before issuing a request; a crash can lose allowance, never replenish it.
    let chargeRequest now cursor =
        if now < cursor.Budget.LastClock || now >= cursor.Budget.WorkDeadline || cursor.Budget.RequestsRemaining <= 0 then
            Error "original request allowance exhausted"
        else Ok { cursor with Budget = { cursor.Budget with LastClock = now; RequestsRemaining = cursor.Budget.RequestsRemaining - 1 } }

    let chargeOutput bytes cursor =
        if bytes < 0 || bytes > cursor.Budget.OutputBytesRemaining then Error "original output allowance exhausted"
        else Ok { cursor with Budget = { cursor.Budget with OutputBytesRemaining = cursor.Budget.OutputBytesRemaining - bytes } }
