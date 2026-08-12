namespace FS.GG.Coord.Cli

/// JSON snapshot boundary for the pure claim-to-done lifecycle decision.
module DeliveryApplication =
    open System
    open System.IO
    open System.Text.Json
    open System.Text.RegularExpressions
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

    let private readBoolean (name: string) (element: JsonElement) : bool =
        let value = required name element
        match value.ValueKind with
        | JsonValueKind.True -> true
        | JsonValueKind.False -> false
        | _ -> invalidArg name "must be a boolean"

    let private readInteger (name: string) (element: JsonElement) : int =
        let value = required name element
        match value.TryGetInt32() with
        | true, parsed -> parsed
        | _ -> invalidArg name "must be a 32-bit integer"

    let private readOptionalInteger (name: string) (element: JsonElement) : int option =
        let value = required name element
        match value.ValueKind with
        | JsonValueKind.Null -> None
        | _ -> Some(readInteger name element)

    /// `declaredPaths` accepts its original wire shape — a plain array of strings, read as tokens
    /// actually declared — or one of three tagged objects distinguishing the ways a touch-set can be
    /// empty-ish (.github#2233 acceptance 4): `{"declaredNone": true}` (an explicit, read `Paths:
    /// none`), `{"undeclared": true}` (a read body with no `Paths:` line at all), or `{"unread":
    /// "<reason>"}` (the body was never read). The array form stays byte-for-byte what every existing
    /// producer (including `tests/coord-engine-e2e/writes.sh`, outside this item's declared `Paths:`)
    /// already emits, so this is additive rather than a breaking wire change.
    let private declaredPaths (element: JsonElement) : Delivery.DeclaredPaths =
        let value = required "declaredPaths" element
        match value.ValueKind with
        | JsonValueKind.Array ->
            let paths =
                value.EnumerateArray()
                |> Seq.map (fun item ->
                    if item.ValueKind <> JsonValueKind.String || String.IsNullOrWhiteSpace(item.GetString()) then
                        invalidArg "declaredPaths" "must contain non-empty strings"
                    item.GetString())
                |> List.ofSeq
            Delivery.Known paths
        | JsonValueKind.Object ->
            if fst (value.TryGetProperty "unread") then
                Delivery.Unread(readString "unread" value)
            elif fst (value.TryGetProperty "declaredNone") then
                Delivery.DeclaredNone
            elif fst (value.TryGetProperty "undeclared") then
                Delivery.Undeclared
            else
                invalidArg "declaredPaths" "object must be {\"unread\": reason}, {\"declaredNone\": true}, or {\"undeclared\": true}"
        | _ -> invalidArg "declaredPaths" "must be an array of paths or a tagged object naming why it has none"

    let private readOptionalString (name: string) (element: JsonElement) : string option =
        let value = required name element
        match value.ValueKind with
        | JsonValueKind.Null -> None
        | JsonValueKind.String when not (String.IsNullOrWhiteSpace(value.GetString())) -> Some(value.GetString())
        | _ -> invalidArg name "must be null or a non-empty string"

    /// A field an older snapshot producer may not emit at all.  Absent means "this producer makes no
    /// diff-audit assertion" — which is exactly the behavior before .github#2144 — and is deliberately
    /// NOT read as a producer asserting `false` about an audit it measured and cleared.
    let private readBooleanOrDefault (name: string) (fallback: bool) (element: JsonElement) : bool =
        match element.TryGetProperty name with
        | true, _ -> readBoolean name element
        | _ -> fallback

    let private readOptionalStringOrAbsent (name: string) (element: JsonElement) : string option =
        match element.TryGetProperty name with
        | true, _ -> readOptionalString name element
        | _ -> None

    let private review (element: JsonElement) : Driver.ReviewChain option =
        let value = required "review" element
        match value.ValueKind with
        | JsonValueKind.Null -> None
        | JsonValueKind.Object ->
            let rounds =
                let raw = required "rounds" value
                if raw.ValueKind <> JsonValueKind.Array then invalidArg "review.rounds" "must be an array"
                raw.EnumerateArray()
                |> Seq.map (fun item ->
                    match item.TryGetInt32() with
                    | true, number -> number
                    | _ -> invalidArg "review.rounds" "must contain integers")
                |> List.ofSeq
            let markerValid = readBoolean "markerValid" value
            let criticIdentity = readOptionalString "criticIdentity" value
            let headSha = readOptionalString "headSha" value
            let repairPhase = readBoolean "repairPhase" value
            let checksGreen = readBoolean "checksGreen" value
            let hostAccepted = readBoolean "hostAccepted" value
            let runtimeRouteEvidence = readOptionalString "routeNotMeaningfulReason" value |> Option.map Driver.NotMeaningful
            let diffAuditRequired = readBooleanOrDefault "diffAuditRequired" false value
            let diffAuditHead = readOptionalStringOrAbsent "diffAuditHead" value
            Some
                ({ MarkerValid = markerValid;
                  CriticIdentity = criticIdentity;
                  HeadSha = headSha;
                  Rounds = rounds;
                  RepairPhase = repairPhase;
                  ChecksGreen = checksGreen;
                  HostAccepted = hostAccepted;
                  RuntimeRouteEvidence = runtimeRouteEvidence;
                  DiffAuditRequired = diffAuditRequired;
                  DiffAuditHead = diffAuditHead } : Driver.ReviewChain)
        | _ -> invalidArg "review" "must be an object or null"

    let private obligations (element: JsonElement) : Delivery.Obligation list =
        let value = required "obligations" element
        if value.ValueKind <> JsonValueKind.Array then invalidArg "obligations" "must be an array"
        value.EnumerateArray()
        |> Seq.map (fun obligation ->
            if obligation.ValueKind <> JsonValueKind.Object then invalidArg "obligations" "must contain objects"
            ({ Id = readString "id" obligation
               Kind = readString "kind" obligation
               Evidence = readOptionalString "evidence" obligation
               HeadSha = readString "headSha" obligation
               Verified = readBoolean "verified" obligation }: Delivery.Obligation))
        |> List.ofSeq

    let private obligationId = "[a-z0-9][a-z0-9_.-]*"
    let private obligationKind = "[a-z0-9][a-z0-9_-]*"
    let private deliveryHead = "[0-9A-Za-z._-]+"
    let private fieldValue = "[^ ]+"

    let private obligationDeclaration =
        Regex($"^<!-- fsgg:delivery-obligation id=(?<id>{obligationId}) kind=(?<kind>{obligationKind}) head=(?<head>{deliveryHead}) -->$", RegexOptions.Compiled)

    let private obligationReceipt =
        Regex($"^<!-- fsgg:delivery-receipt id=(?<id>{obligationId}) head=(?<head>{deliveryHead}) evidence=(?<evidence>{fieldValue}) -->$", RegexOptions.Compiled)

    let private declarationFields =
        Regex($"^<!-- fsgg:delivery-obligation id=(?<id>{fieldValue}) kind=(?<kind>{fieldValue}) head=(?<head>{fieldValue}) -->$", RegexOptions.Compiled)

    let private receiptFields =
        Regex($"^<!-- fsgg:delivery-receipt id=(?<id>{fieldValue}) head=(?<head>{fieldValue}) evidence=(?<evidence>{fieldValue}) -->$", RegexOptions.Compiled)

    // THE LEADING-LINE RULE (.github#2347), applying `.github#2221`'s established correction
    // ("a marker is evidence only as a WHOLE LINE inside the comment's LEADING MARKER BLOCK", never
    // the comment's entire body) to the three delivery markers, which never received it. A delivery
    // marker carries all of its fields inline on one line — unlike a review marker, which pairs a
    // bare marker line with separate `key: value` field lines below it — so its "leading marker
    // block" is exactly the comment's first line: the run of lines, from byte 0, that could be this
    // one marker is never longer than one line for a marker whose own grammar is single-line. Prose
    // that follows on later lines (the org's universal delivery-obligation/receipt writing style —
    // marker line, blank line, explanation) is therefore never part of the marker and is never
    // matched against it. A marker merely quoted later in the body — not the comment's own leading
    // line — still fails this match and stays inert, preserving the `.github#2264` round-1 anchoring
    // fix for the sibling read path.
    let private leadingLine (text: string) =
        let trimmed = text.Trim().Replace("\r\n", "\n")
        match trimmed.IndexOf '\n' with
        | -1 -> trimmed
        | index -> trimmed.Substring(0, index)

    let private malformedField (comment: Driver.ReviewComment) (kind: string) (fields: Regex) =
        let matched = fields.Match(leadingLine comment.Body)
        if not matched.Success then Error $"delivery {kind} comment {comment.Id} has malformed body"
        elif not (Regex($"^{obligationId}$").IsMatch(matched.Groups.["id"].Value)) then Error $"delivery {kind} comment {comment.Id} has malformed id"
        elif kind = "obligation declaration" && not (Regex($"^{obligationKind}$").IsMatch(matched.Groups.["kind"].Value)) then Error $"delivery {kind} comment {comment.Id} has malformed kind"
        elif not (Regex($"^{deliveryHead}$").IsMatch(matched.Groups.["head"].Value)) then Error $"delivery {kind} comment {comment.Id} has malformed head"
        else Error $"delivery {kind} comment {comment.Id} is malformed"

    let obligationsFromComments (headSha: string) (comments: Driver.ReviewComment list) : Result<Delivery.Obligation list, string> =
        let declarations = comments |> List.filter (fun comment -> comment.Body.StartsWith "<!-- fsgg:delivery-obligation")
        let receipts = comments |> List.filter (fun comment -> comment.Body.StartsWith "<!-- fsgg:delivery-receipt")
        let none = $"<!-- fsgg:delivery-obligations none head=%s{headSha} -->"
        if declarations |> List.exists (fun comment -> leadingLine comment.Body = none) then
            if declarations |> List.exists (fun comment -> leadingLine comment.Body <> none) || not (List.isEmpty receipts) then
                Error "the no-obligations declaration cannot be combined with obligation declarations or receipts"
            else Ok []
        elif List.isEmpty declarations then Error "delivery obligations are undeclared"
        else
            let parsedDeclarations =
                declarations
                |> List.map (fun comment ->
                    let matched = obligationDeclaration.Match(leadingLine comment.Body)
                    if not matched.Success then malformedField comment "obligation declaration" declarationFields
                    elif matched.Groups.["head"].Value <> headSha then
                        Error $"delivery obligation declaration comment {comment.Id} is stale for head {headSha}; edit it in place or delete it, because adding a declaration cannot repair it"
                    else Ok(matched.Groups.["id"].Value, matched.Groups.["kind"].Value))
            let firstError values = values |> List.tryPick (function Error error -> Some error | Ok _ -> None)
            match parsedDeclarations |> firstError with
            | Some error -> Error error
            | None ->
                let declarations = parsedDeclarations |> List.choose Result.toOption
                let ids = declarations |> List.map fst
                if ids |> List.distinct |> List.length <> List.length ids then Error "delivery obligation ids must be unique"
                else
                    let parsedReceipts =
                        receipts
                        |> List.map (fun comment ->
                            let matched = obligationReceipt.Match(leadingLine comment.Body)
                            if not matched.Success then malformedField comment "obligation receipt" receiptFields
                            elif matched.Groups.["head"].Value <> headSha then Error "a delivery obligation receipt is stale"
                            else Ok(matched.Groups.["id"].Value, matched.Groups.["evidence"].Value))
                    match parsedReceipts |> firstError with
                    | Some error -> Error error
                    | None ->
                        let receipts = parsedReceipts |> List.choose Result.toOption |> Map.ofList
                        if receipts |> Map.exists (fun id _ -> not (List.contains id ids)) then Error "a delivery obligation receipt names no declared obligation"
                        else
                            declarations
                            |> List.map (fun (id, kind) ->
                                let evidence = Map.tryFind id receipts
                                ({ Id = id; Kind = kind; Evidence = evidence; HeadSha = headSha; Verified = evidence.IsSome }: Delivery.Obligation))
                            |> Ok

    /// The live adapter must consume its delivery receipt and prove that the same claim generation
    /// still wins immediately before it asks GitHub to merge.  Keeping this boundary pure makes the
    /// no-write branch explicit and independently testable.
    type LandingAuthorization =
        | MergeAuthorized
        | MergeRefused of reason: string

    let authorizeGuardedLanding freshnessToken actionKey facts currentClaimGeneration =
        match Delivery.advance freshnessToken actionKey facts with
        | Delivery.NoVerdict reason -> MergeRefused reason
        | Delivery.Next transition when transition.Action <> Delivery.GuardedLand ->
            MergeRefused "delivery receipt does not authorize guarded landing"
        | Delivery.Next _ when Some facts.Freshness.ClaimGeneration <> currentClaimGeneration ->
            MergeRefused "delivery claim generation changed after inspection; GitHub merge was not attempted"
        | Delivery.Next _ -> MergeAuthorized

    /// Invoke the merge adapter only after the receipt and re-read claim generation both authorize it.
    let guardedLanding freshnessToken actionKey facts currentClaimGeneration merge =
        match authorizeGuardedLanding freshnessToken actionKey facts currentClaimGeneration with
        | MergeRefused reason -> Error reason
        | MergeAuthorized -> Ok(merge ())

    let private snapshot (raw: string) : Result<Delivery.Snapshot, string> =
        try
            use document = JsonDocument.Parse raw
            let root = document.RootElement
            if root.ValueKind <> JsonValueKind.Object then invalidArg "snapshot" "must be an object"
            let freshnessElement = required "freshness" root
            if freshnessElement.ValueKind <> JsonValueKind.Object then invalidArg "freshness" "must be an object"
            let freshness: Delivery.Freshness =
                { ItemRef = readString "itemRef" freshnessElement
                  ClaimGeneration = readString "claimGeneration" freshnessElement
                  Executor = readString "executor" freshnessElement
                  Branch = readString "branch" freshnessElement
                  Worktree = readString "worktree" freshnessElement
                  PullRequest = readOptionalInteger "pullRequest" freshnessElement
                  HeadSha = readString "headSha" freshnessElement
                  DeclaredPaths = declaredPaths freshnessElement
                  BoardState = readString "boardState" freshnessElement }
            Ok
                { Freshness = freshness
                  ItemBranchCanonical = readBoolean "itemBranchCanonical" root
                  ClosingLinkageCanonical = readBoolean "closingLinkageCanonical" root
                  PathsVerified = readBoolean "pathsVerified" root
                  InReview = readBoolean "inReview" root
                  Review = review root
                  // `reviewProblem` was added after the first delivery snapshot contract shipped.
                  // Older producers omit it, which means no parser failure was observed; accepting
                  // that shape as None preserves the pure adapter's established wire contract.
                  ReviewProblem = readOptionalStringOrAbsent "reviewProblem" root
                  Landable = readBoolean "landable" root
                  Merged = readBoolean "merged" root
                  MergeReachable = readBoolean "mergeReachable" root
                  IssueClosed = readBoolean "issueClosed" root
                  BoardDone = readBoolean "boardDone" root
                  ClaimReleased = readBoolean "claimReleased" root
                  PendingWrites = readInteger "pendingWrites" root
                  CleanupEligible = readBoolean "cleanupEligible" root
                  ObligationsDeclared = readBoolean "obligationsDeclared" root
                  Obligations = obligations root
                  ParkedReason = readOptionalString "parkedReason" root }
        with error -> Error error.Message

    let private stage (value: Delivery.Stage) =
        match value with
        | Delivery.Claimed -> "claimed"
        | Delivery.Implementation -> "implementation"
        | Delivery.ReviewReady -> "reviewReady"
        | Delivery.ReviewActive -> "reviewActive"
        | Delivery.Accepted -> "accepted"
        | Delivery.Landable -> "landable"
        | Delivery.MergedAwaitingObligations -> "mergedAwaitingObligations"
        | Delivery.Done -> "done"
        | Delivery.Parked -> "parked"

    let private action (value: Delivery.Action) =
        match value with
        | Delivery.ContinueImplementation -> "continueImplementation"
        | Delivery.RepairReviewHandoff _ -> "repairReviewHandoff"
        | Delivery.MoveToReview -> "moveToReview"
        | Delivery.AwaitIndependentReview -> "awaitIndependentReview"
        | Delivery.RefreshReview _ -> "refreshReview"
        | Delivery.AwaitLandability -> "awaitLandability"
        | Delivery.GuardedLand -> "guardedLand"
        | Delivery.VerifyObligation _ -> "verifyObligation"
        | Delivery.Complete -> "complete"
        | Delivery.CleanupWorktree -> "cleanupWorktree"
        | Delivery.RouteFollowUp _ -> "routeFollowUp"

    let render opts facts =
        match Delivery.inspect facts with
        | Delivery.NoVerdict reason ->
            match opts.Render with
            | Json -> printfn "%s" (JsonSerializer.Serialize {| schema = "fsgg.coord.delivery/1"; verdict = "noVerdict"; reason = reason |})
            | Text -> eprint $"UNDETERMINED — %s{reason}"
            ExitCode.toInt ExitCode.NoVerdict
        | Delivery.Next transition ->
            match opts.Render with
            | Json ->
                printfn "%s" (JsonSerializer.Serialize {| schema = "fsgg.coord.delivery/1"; verdict = "next"; stage = stage transition.Stage; action = action transition.Action; freshnessToken = transition.FreshnessToken; actionKey = transition.ActionKey |})
            | Text -> printfn "%s — %s" (stage transition.Stage) (action transition.Action)
            ExitCode.toInt ExitCode.Green

    let run opts =
        let raw = input opts
        if String.IsNullOrWhiteSpace raw then
            eprint "fsgg-coord-engine: delivery snapshot is empty; refusing to infer lifecycle state."
            ExitCode.toInt ExitCode.Error
        else
            match snapshot raw with
            | Error error ->
                eprint $"fsgg-coord-engine: delivery snapshot is malformed: %s{error}"
                ExitCode.toInt ExitCode.Error
            | Ok facts -> render opts facts
