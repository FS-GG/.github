namespace FS.GG.Coord

module Delivery =
    open System
    open System.Globalization
    open System.Security.Cryptography
    open System.Text
    open System.Text.Json
    open Types

    type Stage =
        | Claimed
        | Implementation
        | ReviewReady
        | ReviewActive
        | Accepted
        | Landable
        | MergedAwaitingObligations
        | Done
        | Parked

    type Obligation =
        { Id: string
          Kind: string
          Evidence: string option
          HeadSha: string
          Verified: bool }

    type DeclaredPaths =
        | Known of string list
        | DeclaredNone
        | Undeclared
        | Unread of reason: string

    // One authority input to the shared path-admissibility classifier. A caller that could not read
    // an input supplies `AuthorityUnknown`; it never substitutes an empty inventory for an unread one.
    type PathAuthority<'value> =
        | AuthorityKnown of revision: string * value: 'value
        | AuthorityUnknown of reason: string

    // The exhaustive admission vocabulary shared by `verify-paths` and live delivery.
    type PathAdmission =
        | DeclaredPath
        | GeneratedPath
        | MandatorySddPath
        | UndeclaredAuthoredPath
        | UnknownPath

    type PathClassification =
        { Path: string
          Admission: PathAdmission
          Reason: string
          // Immutable revisions of the non-touch-set authorities consulted for this answer.
          AuthorityRevisions: string list }

    // Classify every changed path from one closed definition. Declared coverage wins without reading
    // exemption authorities. For uncovered paths, either known exemption may admit the path; only a
    // complete pair of negative authority readings can conclude `UndeclaredAuthoredPath`.
    let classifyPaths
        (touchSet: Types.TouchSet)
        (generated: PathAuthority<Set<string>>)
        (sddPackage: PathAuthority<Types.PathToken list>)
        (files: string list)
        : PathClassification list =
        let coveredByDeclared file =
            match touchSet with
            | Types.Declared tokens -> tokens |> List.exists (fun token -> TouchSet.covers token file)
            | Types.DeclaredChore -> true
            | _ -> false

        let knownGenerated file =
            match generated with
            | AuthorityKnown(revision, paths) when Set.contains file paths -> Some revision
            | _ -> None

        let knownSdd file =
            match sddPackage with
            | AuthorityKnown(revision, tokens) when tokens |> List.exists (fun token -> TouchSet.covers token file) -> Some revision
            | _ -> None

        let unknowns =
            [ match generated with
              | AuthorityUnknown reason -> yield $"generated-path authority: %s{reason}"
              | AuthorityKnown _ -> ()
              match sddPackage with
              | AuthorityUnknown reason -> yield $"sdd-package authority: %s{reason}"
              | AuthorityKnown _ -> () ]

        files
        |> List.map (fun file ->
            if coveredByDeclared file then
                { Path = file
                  Admission = DeclaredPath
                  Reason = "covered by the authored touch set"
                  AuthorityRevisions = [] }
            elif not (List.isEmpty unknowns) then
                { Path = file
                  Admission = UnknownPath
                  Reason = String.concat "; " unknowns
                  AuthorityRevisions =
                    [ match generated with AuthorityKnown(revision, _) -> yield revision | _ -> ()
                      match sddPackage with AuthorityKnown(revision, _) -> yield revision | _ -> () ] }
            else
                match knownSdd file, knownGenerated file with
                | Some revision, _ ->
                    { Path = file
                      Admission = MandatorySddPath
                      Reason = "mandatory output of the current sdd-required delivery route"
                      AuthorityRevisions = [ revision ] }
                | None, Some revision ->
                    { Path = file
                      Admission = GeneratedPath
                      Reason = "generated, CI-gated artifact"
                      AuthorityRevisions = [ revision ] }
                | None, None ->
                    { Path = file
                      Admission = UndeclaredAuthoredPath
                      Reason = "not covered by the authored touch set or an authoritative exemption"
                      AuthorityRevisions =
                        [ match generated with AuthorityKnown(revision, _) -> yield revision | _ -> ()
                          match sddPackage with AuthorityKnown(revision, _) -> yield revision | _ -> () ] })

    // The one admission projection consumed by both command callers. Unknown is deliberately a refusal.
    let pathsVerified (classifications: PathClassification list) =
        classifications
        |> List.forall (fun classification ->
            match classification.Admission with
            | DeclaredPath
            | GeneratedPath
            | MandatorySddPath -> true
            | UndeclaredAuthoredPath
            | UnknownPath -> false)

    type Freshness =
        { ItemRef: string
          ClaimGeneration: string
          Executor: string
          Branch: string
          Worktree: string
          PullRequest: int option
          HeadSha: string
          DeclaredPaths: DeclaredPaths
          BoardState: string }

    type Snapshot =
        { Freshness: Freshness
          ItemBranchCanonical: bool
          ClosingLinkageCanonical: bool
          PathsVerified: bool
          InReview: bool
          Review: Driver.ReviewChain option
          ReviewProblem: string option
          Landable: bool
          Merged: bool
          MergeReachable: bool
          IssueClosed: bool
          BoardDone: bool
          ClaimReleased: bool
          PendingWrites: int
          CleanupEligible: bool
          ObligationsDeclared: bool
          Obligations: Obligation list
          ParkedReason: string option }

    type PostMergeRun =
        { Id: int64
          Attempt: int
          Workflow: string
          Event: string
          Branch: string
          Sha: string
          Status: string
          Conclusion: string
          Url: string }

    type PostMergeVerificationReceipt =
        { MergeSha: string
          DefaultBranch: string
          Runs: PostMergeRun list }

    type PostMergeVerification =
        | NotObserved
        | Awaiting of reason: string
        | Rejected of reason: string
        | Unreadable of reason: string
        | Verified of PostMergeVerificationReceipt

    type CompletionFacts =
        { HeadSha: string
          Merged: bool
          MergeReachable: bool
          PostMergeVerification: PostMergeVerification
          IssueClosed: bool
          BoardDone: bool
          ClaimReleased: bool
          PendingWrites: int
          CleanupEligible: bool
          ObligationsDeclared: bool
          Obligations: Obligation list }

    [<RequireQualifiedAccess>]
    type CompletionDecision =
        | NotMerged
        | Refused of reason: string
        | VerifyOutstandingObligation of name: string
        | AwaitPostMergeVerification of reason: string
        | ProjectCompletion
        | CleanupCompletedDelivery

    type VerifiedObligationReceipt =
        { Id: string
          Kind: string
          Evidence: string
          HeadSha: string }

    type DeliveryCompletionReceipt =
        { Item: string
          PullRequest: int
          MergeSha: string
          MergeReachable: bool
          ObligationReceipts: VerifiedObligationReceipt list
          PostMergeVerification: PostMergeVerificationReceipt option
          PendingBoardWrites: int
          FreshnessToken: string
          ActionKey: string
          CompletedAt: DateTimeOffset
          Digest: string }

    // Durable evidence that reconciliation observed premature issue closure before authoritative
    // completion. The public contract is documented in Delivery.fsi; implementation-side XML comments
    // would be discarded when a sibling signature exists.
    type CompletionCorrectionReceipt =
        { Item: string
          Destination: BoardStatus
          ObservedAt: DateTimeOffset
          Digest: string }

    [<Literal>]
    let CompletionReceiptMarker = "<!-- fsgg:delivery-completion/v1 -->"

    [<Literal>]
    let CompletionCorrectionMarker = "<!-- fsgg:completion-correction/v1 -->"

    type Action =
        | ContinueImplementation
        | RepairReviewHandoff of reason: string
        | MoveToReview
        | AwaitIndependentReview
        | RefreshReview of reason: string
        | AwaitLandability
        | GuardedLand
        | VerifyObligation of name: string
        | AwaitPostMergeVerification of reason: string
        | Complete
        | CleanupWorktree
        | RouteFollowUp of reason: string

    type Transition =
        { Stage: Stage
          Action: Action
          FreshnessToken: string
          ActionKey: string
          PostMergeVerification: PostMergeVerification }

    type Verdict =
        | Next of Transition
        | NoVerdict of reason: string

    let private digest (value: string) =
        value
        |> Encoding.UTF8.GetBytes
        |> SHA256.HashData
        |> Convert.ToHexString
        |> fun hash -> hash.ToLowerInvariant()

    // Folds the CASE in as well as the tokens, so none of the four cases can hash to the same token as
    // another — `Unread "x"` cannot collide with `Known [ "x" ]`, and `DeclaredNone` cannot collide
    // with `Undeclared` or an empty `Known []` — a receipt minted from one fact must never be
    // redeemable against a differently-cased one (.github#2233 scope item 3).
    let private declaredPathsToken =
        function
        | Known paths -> "known\n" + String.concat "\n" paths
        | DeclaredNone -> "declaredNone"
        | Undeclared -> "undeclared"
        | Unread reason -> "unread\n" + reason

    let freshnessToken freshness =
        [ freshness.ItemRef
          freshness.ClaimGeneration
          freshness.Executor
          freshness.Branch
          freshness.Worktree
          freshness.PullRequest |> Option.map string |> Option.defaultValue ""
          freshness.HeadSha
          declaredPathsToken freshness.DeclaredPaths
          freshness.BoardState ]
        |> String.concat "\n"
        |> digest

    let private missing value label =
        if String.IsNullOrWhiteSpace value then Some label else None

    // The reason a decision cannot proceed for the touch-set fact alone. Each of the three empty-ish
    // cases gets its OWN reason text, so a worker reading a `noVerdict` can tell which one it is
    // without opening the issue body (.github#2233 acceptance 4): `Unread` NAMES THE READ as the
    // failure and never the item's own declaration — the reader is at fault, not the item (acceptance
    // 2) — while `DeclaredNone` and `Undeclared` are both facts genuinely read off the body, so they
    // name the item's own state rather than a reader failure, and each keeps the "declared paths"
    // family wording so the omission is still recognizable as the same KIND of gap this validator has
    // always reported (acceptance 2's "the omission reason it answers today").
    //
    // Skipped entirely once the snapshot is `CleanupEligible`: a terminal item (stamped, closed, claim
    // released, nothing pending) has nothing left to touch, so `CleanupWorktree` does not need to know
    // what it once reserved — demanding the field here would block a stamped item's cleanup transition
    // forever on a residual empty fact (.github#2233, the measured `#2225` residue).
    let private declaredPathsProblem (snapshot: Snapshot) =
        if snapshot.CleanupEligible then
            None
        else
            match snapshot.Freshness.DeclaredPaths with
            | Unread reason -> Some $"declared paths were not read: %s{reason}"
            | Undeclared -> Some "declared paths were never declared (no Paths: line)"
            | DeclaredNone -> Some "declared paths are deliberately empty (Paths: none)"
            | Known [] -> Some "declared paths"
            | Known _ -> None

    let private validate (snapshot: Snapshot) =
        let freshness = snapshot.Freshness
        [ missing freshness.ItemRef "item ref"
          missing freshness.ClaimGeneration "claim generation"
          missing freshness.Executor "executor identity"
          missing freshness.Branch "branch"
          missing freshness.Worktree "worktree"
          match freshness.PullRequest with
          | Some value when value <= 0 -> Some "pull request"
          | _ -> None
          missing freshness.HeadSha "head SHA"
          missing freshness.BoardState "board state"
          declaredPathsProblem snapshot ]
        |> List.choose id

    let private postMergeVerificationToken =
        function
        | NotObserved -> "notObserved"
        | Awaiting reason -> "awaiting\n" + reason
        | Rejected reason -> "rejected\n" + reason
        | Unreadable reason -> "unreadable\n" + reason
        | Verified receipt ->
            [ "verified"
              receipt.MergeSha
              receipt.DefaultBranch
              yield!
                  receipt.Runs
                  |> List.sortBy (fun run -> run.Id, run.Attempt)
                  |> List.collect (fun run ->
                      [ string run.Id
                        string run.Attempt
                        run.Workflow
                        run.Event
                        run.Branch
                        run.Sha
                        run.Status
                        run.Conclusion
                        run.Url ]) ]
            |> String.concat "\n"

    let private nextWithPostMergeVerification postMergeVerification (snapshot: Snapshot) stage action =
        let token = freshnessToken snapshot.Freshness
        let actionKey =
            [ token; string stage; string action; postMergeVerificationToken postMergeVerification ]
            |> String.concat "\n"
            |> digest
        Next
            { Stage = stage
              Action = action
              FreshnessToken = token
              ActionKey = actionKey
              PostMergeVerification = postMergeVerification }

    let private next snapshot stage action =
        nextWithPostMergeVerification NotObserved snapshot stage action

    let completionFactsWithPostMergeVerification postMergeVerification (snapshot: Snapshot) =
        { HeadSha = snapshot.Freshness.HeadSha
          Merged = snapshot.Merged
          MergeReachable = snapshot.MergeReachable
          PostMergeVerification = postMergeVerification
          IssueClosed = snapshot.IssueClosed
          BoardDone = snapshot.BoardDone
          ClaimReleased = snapshot.ClaimReleased
          PendingWrites = snapshot.PendingWrites
          CleanupEligible = snapshot.CleanupEligible
          ObligationsDeclared = snapshot.ObligationsDeclared
          Obligations = snapshot.Obligations }

    let completionFacts snapshot =
        completionFactsWithPostMergeVerification NotObserved snapshot

    let decideCompletion (facts: CompletionFacts) =
        if not facts.Merged then
            CompletionDecision.NotMerged
        elif facts.PendingWrites < 0 then
            CompletionDecision.Refused "pending board writes cannot be negative"
        elif not facts.ObligationsDeclared then
            CompletionDecision.Refused "delivery obligations are undeclared"
        else
            let duplicate =
                facts.Obligations
                |> List.groupBy _.Id
                |> List.tryFind (fun (_, obligations) -> List.length obligations > 1)
            let stale =
                facts.Obligations
                |> List.tryFind (fun obligation -> obligation.HeadSha <> facts.HeadSha)
            let contradictory =
                facts.Obligations
                |> List.tryFind (fun obligation ->
                    obligation.Verified <> obligation.Evidence.IsSome
                    || (obligation.Evidence |> Option.exists String.IsNullOrWhiteSpace))

            match duplicate, stale, contradictory with
            | Some (id, _), _, _ ->
                CompletionDecision.Refused($"delivery obligation '%s{id}' is declared more than once")
            | _, Some obligation, _ ->
                CompletionDecision.Refused(
                    $"delivery obligation '%s{obligation.Id}' is for head %s{obligation.HeadSha}, not %s{facts.HeadSha}"
                )
            | _, _, Some obligation ->
                CompletionDecision.Refused(
                    $"delivery obligation '%s{obligation.Id}' has contradictory verification evidence"
                )
            | _ ->
                match facts.Obligations |> List.tryFind (fun obligation -> not obligation.Verified) with
                | Some obligation -> CompletionDecision.VerifyOutstandingObligation obligation.Id
                | None when not facts.MergeReachable ->
                    CompletionDecision.Refused "merged pull request is not reachable from the default branch"
                | None ->
                    match facts.PostMergeVerification with
                    | NotObserved ->
                        CompletionDecision.AwaitPostMergeVerification "no exact-merge default-branch execution has been observed"
                    | Awaiting reason -> CompletionDecision.AwaitPostMergeVerification reason
                    | Rejected reason -> CompletionDecision.Refused($"post-merge verification was rejected: %s{reason}")
                    | Unreadable reason -> CompletionDecision.Refused($"post-merge verification is unreadable: %s{reason}")
                    | Verified receipt when String.IsNullOrWhiteSpace receipt.MergeSha ->
                        CompletionDecision.Refused "post-merge verification carries no merge SHA"
                    | Verified receipt when String.IsNullOrWhiteSpace receipt.DefaultBranch ->
                        CompletionDecision.Refused "post-merge verification carries no default branch"
                    | Verified receipt when List.isEmpty receipt.Runs ->
                        CompletionDecision.Refused "post-merge verification carries no successful execution runs"
                    | Verified receipt when
                        receipt.Runs
                        |> List.exists (fun run ->
                            run.Sha <> receipt.MergeSha
                            || run.Branch <> receipt.DefaultBranch
                            || run.Event <> "push"
                            || run.Status <> "completed"
                            || run.Conclusion <> "success") ->
                        CompletionDecision.Refused "post-merge verification contains a run that is not an exact-merge successful default-branch push execution"
                    | Verified _ when not facts.IssueClosed || not facts.BoardDone || not facts.ClaimReleased || facts.PendingWrites <> 0 ->
                        CompletionDecision.ProjectCompletion
                    | Verified _ when not facts.CleanupEligible ->
                        CompletionDecision.Refused "cleanup is not eligible before completion"
                    | Verified _ -> CompletionDecision.CleanupCompletedDelivery

    let private completionReceiptDigest (receipt: DeliveryCompletionReceipt) =
        [ yield receipt.Item
          yield string receipt.PullRequest
          yield receipt.MergeSha
          yield string receipt.MergeReachable
          yield string receipt.PendingBoardWrites
          yield receipt.FreshnessToken
          yield receipt.ActionKey
          yield receipt.CompletedAt.ToUniversalTime().ToString("O")
          match receipt.PostMergeVerification with
          | Some verification ->
              yield "postMergeVerification"
              yield postMergeVerificationToken (Verified verification)
          | None -> ()
          yield!
              receipt.ObligationReceipts
              |> List.sortBy (fun obligation -> obligation.Id, obligation.Kind)
              |> List.collect (fun obligation ->
                  [ obligation.Id
                    obligation.Kind
                    obligation.Evidence
                    obligation.HeadSha ]) ]
        |> String.concat "\n"
        |> digest

    let createCompletionReceipt item pullRequest mergeSha completedAt freshnessToken actionKey facts =
        let missing name value =
            if String.IsNullOrWhiteSpace value then Some($"%s{name} is required") else None
        let errors =
            [ missing "item" item
              if pullRequest <= 0 then Some "pull request must be positive" else None
              missing "merge SHA" mergeSha
              missing "freshness token" freshnessToken
              missing "action key" actionKey ]
            |> List.choose id

        match errors, decideCompletion facts with
        | _ :: _, _ -> Error errors
        | [], CompletionDecision.ProjectCompletion ->
            let obligations =
                facts.Obligations
                |> List.map (fun obligation ->
                    { Id = obligation.Id
                      Kind = obligation.Kind
                      Evidence = obligation.Evidence.Value
                      HeadSha = obligation.HeadSha })
            match facts.PostMergeVerification with
            | Verified verification when verification.MergeSha = mergeSha ->
                let unsigned =
                    { Item = item
                      PullRequest = pullRequest
                      MergeSha = mergeSha
                      MergeReachable = facts.MergeReachable
                      ObligationReceipts = obligations
                      PostMergeVerification = Some verification
                      PendingBoardWrites = facts.PendingWrites
                      FreshnessToken = freshnessToken
                      ActionKey = actionKey
                      CompletedAt = completedAt
                      Digest = "" }
                Ok { unsigned with Digest = completionReceiptDigest unsigned }
            | Verified verification ->
                Error [ $"post-merge verification is for %s{verification.MergeSha}, not completion merge %s{mergeSha}" ]
            | _ -> Error [ "post-merge verification is required to mint a completion receipt" ]
        | [], decision ->
            Error [ $"completion receipt is not authorized by decision %A{decision}" ]

    let verifyCompletionReceipt (receipt: DeliveryCompletionReceipt) =
        let errors =
            [ if String.IsNullOrWhiteSpace receipt.Item then yield "item is required"
              if receipt.PullRequest <= 0 then yield "pull request must be positive"
              if String.IsNullOrWhiteSpace receipt.MergeSha then yield "merge SHA is required"
              if not receipt.MergeReachable then yield "merge is not reachable"
              match receipt.PostMergeVerification with
              | Some verification when verification.MergeSha <> receipt.MergeSha ->
                  yield "post-merge verification merge SHA does not match the completion merge SHA"
              | Some verification ->
                  match decideCompletion
                      { HeadSha = receipt.MergeSha
                        Merged = true
                        MergeReachable = true
                        PostMergeVerification = Verified verification
                        IssueClosed = false
                        BoardDone = false
                        ClaimReleased = false
                        PendingWrites = 0
                        CleanupEligible = false
                        ObligationsDeclared = true
                        Obligations = [] } with
                  | CompletionDecision.ProjectCompletion -> ()
                  | decision -> yield $"post-merge verification is invalid: %A{decision}"
              | None -> () // Legacy v1 receipts remain replay authority; new minting always writes Some.
              if receipt.PendingBoardWrites <> 0 then yield "pending board writes must be zero"
              if String.IsNullOrWhiteSpace receipt.FreshnessToken then yield "freshness token is required"
              if String.IsNullOrWhiteSpace receipt.ActionKey then yield "action key is required"
              if receipt.ObligationReceipts |> List.exists (fun obligation ->
                    String.IsNullOrWhiteSpace obligation.Id
                    || String.IsNullOrWhiteSpace obligation.Kind
                    || String.IsNullOrWhiteSpace obligation.Evidence
                    || String.IsNullOrWhiteSpace obligation.HeadSha) then
                  yield "verified obligation receipts must be complete"
              let duplicateIds =
                  receipt.ObligationReceipts
                  |> List.countBy _.Id
                  |> List.choose (fun (id, count) -> if count > 1 then Some id else None)
              if not (List.isEmpty duplicateIds) then
                  let names = String.concat ", " duplicateIds
                  yield $"verified obligation receipts contain duplicate ids: %s{names}"
              if completionReceiptDigest { receipt with Digest = "" } <> receipt.Digest then
                  yield "completion receipt digest does not match its facts" ]
        if List.isEmpty errors then Ok () else Error errors

    let encodeCompletionReceipt (receipt: DeliveryCompletionReceipt) =
        let obligations =
            receipt.ObligationReceipts
            |> List.map (fun obligation ->
                {| id = obligation.Id
                   kind = obligation.Kind
                   evidence = obligation.Evidence
                   headSha = obligation.HeadSha |})
            |> List.toArray
        let payload =
            let postMergeVerification =
                receipt.PostMergeVerification
                |> Option.map (fun verification ->
                    {| mergeSha = verification.MergeSha
                       defaultBranch = verification.DefaultBranch
                       runs =
                        verification.Runs
                        |> List.map (fun run ->
                            {| id = run.Id
                               attempt = run.Attempt
                               workflow = run.Workflow
                               event = run.Event
                               branch = run.Branch
                               sha = run.Sha
                               status = run.Status
                               conclusion = run.Conclusion
                               url = run.Url |})
                        |> List.toArray |})
            {| schema = "fsgg.coord.delivery-completion/v1"
               item = receipt.Item
               pullRequest = receipt.PullRequest
               mergeSha = receipt.MergeSha
               mergeReachable = receipt.MergeReachable
               obligationReceipts = obligations
               postMergeVerification = postMergeVerification
               pendingBoardWrites = receipt.PendingBoardWrites
               freshnessToken = receipt.FreshnessToken
               actionKey = receipt.ActionKey
               completedAt = receipt.CompletedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
               digest = receipt.Digest |}
        CompletionReceiptMarker + "\n" + JsonSerializer.Serialize payload

    let tryDecodeCompletionReceipt (body: string) =
        if not (body.StartsWith(CompletionReceiptMarker, StringComparison.Ordinal)) then
            Ok None
        else
            try
                use document = JsonDocument.Parse(body.Substring(CompletionReceiptMarker.Length).Trim())
                let root = document.RootElement
                let requiredString (name: string) (element: JsonElement) =
                    match element.TryGetProperty name with
                    | true, value when value.ValueKind = JsonValueKind.String && not (String.IsNullOrWhiteSpace(value.GetString())) ->
                        value.GetString()
                    | _ -> invalidArg name "must be a non-empty string"
                let requiredInt (name: string) (element: JsonElement) =
                    match element.TryGetProperty name with
                    | true, value when value.ValueKind = JsonValueKind.Number -> value.GetInt32()
                    | _ -> invalidArg name "must be an integer"
                let requiredBool (name: string) (element: JsonElement) =
                    match element.TryGetProperty name with
                    | true, value when value.ValueKind = JsonValueKind.True -> true
                    | true, value when value.ValueKind = JsonValueKind.False -> false
                    | _ -> invalidArg name "must be a boolean"

                if requiredString "schema" root <> "fsgg.coord.delivery-completion/v1" then
                    invalidArg "schema" "must be fsgg.coord.delivery-completion/v1"
                let obligationsElement = root.GetProperty "obligationReceipts"
                if obligationsElement.ValueKind <> JsonValueKind.Array then
                    invalidArg "obligationReceipts" "must be an array"
                let obligations =
                    obligationsElement.EnumerateArray()
                    |> Seq.map (fun obligation ->
                        { Id = requiredString "id" obligation
                          Kind = requiredString "kind" obligation
                          Evidence = requiredString "evidence" obligation
                          HeadSha = requiredString "headSha" obligation })
                    |> Seq.toList
                let postMergeVerification =
                    match root.TryGetProperty "postMergeVerification" with
                    | false, _ -> None
                    | true, value when value.ValueKind = JsonValueKind.Null -> None
                    | true, value when value.ValueKind = JsonValueKind.Object ->
                        let runsElement = value.GetProperty "runs"
                        if runsElement.ValueKind <> JsonValueKind.Array then
                            invalidArg "postMergeVerification.runs" "must be an array"
                        let runs =
                            runsElement.EnumerateArray()
                            |> Seq.map (fun run ->
                                { Id = run.GetProperty("id").GetInt64()
                                  Attempt = requiredInt "attempt" run
                                  Workflow = requiredString "workflow" run
                                  Event = requiredString "event" run
                                  Branch = requiredString "branch" run
                                  Sha = requiredString "sha" run
                                  Status = requiredString "status" run
                                  Conclusion = requiredString "conclusion" run
                                  Url = requiredString "url" run })
                            |> Seq.toList
                        Some
                            { MergeSha = requiredString "mergeSha" value
                              DefaultBranch = requiredString "defaultBranch" value
                              Runs = runs }
                    | _ -> invalidArg "postMergeVerification" "must be an object or null"
                let receipt: DeliveryCompletionReceipt =
                    { Item = requiredString "item" root
                      PullRequest = requiredInt "pullRequest" root
                      MergeSha = requiredString "mergeSha" root
                      MergeReachable = requiredBool "mergeReachable" root
                      ObligationReceipts = obligations
                      PostMergeVerification = postMergeVerification
                      PendingBoardWrites = requiredInt "pendingBoardWrites" root
                      FreshnessToken = requiredString "freshnessToken" root
                      ActionKey = requiredString "actionKey" root
                      CompletedAt = DateTimeOffset.Parse(requiredString "completedAt" root, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                      Digest = requiredString "digest" root }
                match verifyCompletionReceipt receipt with
                | Ok () -> Ok(Some receipt)
                | Error errors -> Error errors
            with error -> Error [ error.Message ]

    let private completionCorrectionDigest (receipt: CompletionCorrectionReceipt) =
        [ "fsgg.coord.completion-correction/v1"
          receipt.Item
          statusWireName receipt.Destination
          receipt.ObservedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ]
        |> String.concat "\n"
        |> digest

    let verifyCompletionCorrectionReceipt (receipt: CompletionCorrectionReceipt) =
        let errors =
            [ if String.IsNullOrWhiteSpace receipt.Item then yield "item is required"
              match receipt.Destination with
              | BoardStatus.InReview
              | BoardStatus.Blocked -> ()
              | destination ->
                  yield $"completion correction destination must be In review or Blocked, not %s{statusWireName destination}"
              if completionCorrectionDigest { receipt with Digest = "" } <> receipt.Digest then
                  yield "completion correction receipt digest does not match its facts" ]
        if List.isEmpty errors then Ok () else Error errors

    let createCompletionCorrectionReceipt (item: string) (destination: BoardStatus) (observedAt: DateTimeOffset) =
        let unsigned: CompletionCorrectionReceipt =
            { Item = item
              Destination = destination
              ObservedAt = observedAt
              Digest = "" }
        let signed = { unsigned with Digest = completionCorrectionDigest unsigned }
        verifyCompletionCorrectionReceipt signed |> Result.map (fun () -> signed)

    let encodeCompletionCorrectionReceipt receipt =
        let payload =
            {| schema = "fsgg.coord.completion-correction/v1"
               item = receipt.Item
               destination = statusWireName receipt.Destination
               observedAt = receipt.ObservedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
               digest = receipt.Digest |}
        CompletionCorrectionMarker + "\n" + JsonSerializer.Serialize payload

    let tryDecodeCompletionCorrectionReceipt (body: string) =
        if not (body.StartsWith(CompletionCorrectionMarker, StringComparison.Ordinal)) then
            Ok None
        else
            try
                use document = JsonDocument.Parse(body.Substring(CompletionCorrectionMarker.Length).Trim())
                let root = document.RootElement
                let requiredString (name: string) : string =
                    match root.TryGetProperty name with
                    | true, value when value.ValueKind = JsonValueKind.String && not (String.IsNullOrWhiteSpace(value.GetString())) ->
                        value.GetString()
                    | _ -> invalidArg name "must be a non-empty string"
                if requiredString "schema" <> "fsgg.coord.completion-correction/v1" then
                    invalidArg "schema" "must be fsgg.coord.completion-correction/v1"
                let destination =
                    match requiredString "destination" with
                    | "In review" -> BoardStatus.InReview
                    | "Blocked" -> BoardStatus.Blocked
                    | value -> invalidArg "destination" $"must be In review or Blocked, not '%s{value}'"
                let receipt: CompletionCorrectionReceipt =
                    { Item = requiredString "item"
                      Destination = destination
                      ObservedAt = DateTimeOffset.Parse(requiredString "observedAt", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                      Digest = requiredString "digest" }
                verifyCompletionCorrectionReceipt receipt
                |> Result.map (fun () -> Some receipt)
            with error -> Error [ error.Message ]

    let private handoffProblem snapshot =
        if not snapshot.ItemBranchCanonical then Some "item branch is not canonical"
        elif not snapshot.ClosingLinkageCanonical then Some "canonical closing linkage is missing"
        elif not snapshot.PathsVerified then Some "declared paths are not verified"
        else None

    // The round ceiling this chain is judged against, read from the ONE policy record that defines
    // both literals (`Protocol.reviewPolicy`) rather than restated here. It was spelled `if
    // review.RepairPhase then 10 else 3` for as long as this function existed, which is the same
    // second-copy-of-one-rule shape `.github#2575` acceptance 2 names — `Driver.receiptFresh` already
    // reads the policy record for exactly this quantity, so the two spellings were one silent reword
    // away from disagreeing. The values are identical today (`MaxAutomatedRepairRounds = 3`,
    // `RepairPhaseMaxRounds = 10`), so this is behaviour-preserving by construction.
    let private reviewCeiling (review: Driver.ReviewChain) =
        if review.RepairPhase then Protocol.reviewPolicy.RepairPhaseMaxRounds
        else Protocol.reviewPolicy.MaxAutomatedRepairRounds

    // What is wrong with the review EVIDENCE — and only that (.github#2575).
    //
    // This reads `Driver.validateReviewChainStructure`, not `Driver.validateReviewChain`. The full
    // list also carries one LIVENESS clause, "review checks are not green", which is a fact about a CI
    // run that has not reported yet rather than anything wrong with what the critic and host durably
    // wrote. Folding it in here made a complete, host-accepted chain report `stage: reviewActive,
    // action: refreshReview` — the state whose taught recovery is to go looking for a repair round
    // that does not exist. `.github#2549` removed exactly that conflation from `review`; leaving it
    // here meant the verb `review`'s own `authorizeDelivery` action routes to answered the corrected
    // question with the uncorrected answer.
    //
    // Worse, the check it fell over was structurally unable to be green yet: `claim-generation` cannot
    // report until the live `delivery` call PATCHes `fsgg:pr-authorization` onto the head
    // (`.github#2504`), and that call is the very one whose output this is.
    //
    // `Driver.validateReviewChain` is untouched and remains the single definition of chain validity;
    // the structural/liveness split lives once, at its source, in `Driver.reviewChainProblems`
    // (acceptance 2). Delivery does not fork a second copy and cannot drift from it.
    let private reviewProblem snapshot =
        match snapshot.ReviewProblem, snapshot.Review with
        | Some problem, _ when not (System.String.IsNullOrWhiteSpace problem) -> Some problem
        | _, None -> Some "independent review evidence is absent"
        | _, Some review when review.HeadSha <> Some snapshot.Freshness.HeadSha ->
            Some "independent review is for a different head SHA"
        | _, Some review ->
            match Driver.validateReviewChainStructure (reviewCeiling review) review with
            | [] -> None
            | errors -> Some(String.concat "; " errors)

    // The one liveness fact `reviewProblem` above deliberately no longer reports, kept as a SEPARATE
    // question so that dropping it from the review problem list cannot loosen what may merge.
    //
    // This guard is load-bearing rather than defensive. In the live adapter `Landable` and the chain's
    // `ChecksGreen` are both derived from the same `landable = PrGreen` read, so they cannot disagree
    // there — but a SUPPLIED snapshot carries them as two independent fields, and host-measured
    // against the pre-fix engine, `landable=true` with `checksGreen=false` was held short of
    // `GuardedLand` by the checks clause inside `reviewProblem` and by nothing else. Removing that
    // clause without this guard would have made that combination authorize a merge on a chain whose
    // own checks are not green, which is the one thing `.github#2575` acceptance 6 forbids. The
    // answer stays `Landable`/`AwaitLandability`: fail-closed, and no new merge authority anywhere.
    let private reviewChecksPending snapshot =
        snapshot.Review |> Option.exists (fun review -> not review.ChecksGreen)

    let fromReviewAcceptance (receipt: Review.AcceptedReceipt) (snapshot: Snapshot) : Snapshot =
        let chain: Driver.ReviewChain =
            { MarkerValid = true
              Subject = None
              ClaimGeneration = None
              BaseSha = None
              CriticIdentity = Some receipt.CriticIdentity
              HeadSha = Some receipt.HeadSha
              Rounds = receipt.Rounds
              RepairPhase = receipt.RepairPhase
              ChecksGreen = receipt.ChecksGreen
              HostAccepted = true
              RuntimeRouteEvidence = receipt.RuntimeRouteEvidence
              DiffAuditRequired = receipt.DiffAuditRequired
              DiffAuditHead = receipt.DiffAuditHead }
        { snapshot with Review = Some chain; ReviewProblem = None }

    let inspectWithPostMergeVerification postMergeVerification snapshot =
        match validate snapshot with
        | missingFacts when not (List.isEmpty missingFacts) ->
            let names = String.concat ", " missingFacts
            NoVerdict $"delivery facts are incomplete: %s{names}"
        | _ when snapshot.PendingWrites < 0 ->
            NoVerdict "pending board writes cannot be negative"
        | _ ->
            match snapshot.ParkedReason with
            | Some reason when not (String.IsNullOrWhiteSpace reason) -> next snapshot Parked (RouteFollowUp reason)
            | _ when snapshot.Merged ->
                match decideCompletion (completionFactsWithPostMergeVerification postMergeVerification snapshot) with
                | CompletionDecision.NotMerged -> NoVerdict "completion facts do not describe a merged pull request"
                | CompletionDecision.Refused reason -> NoVerdict reason
                | CompletionDecision.VerifyOutstandingObligation name ->
                    nextWithPostMergeVerification postMergeVerification snapshot MergedAwaitingObligations (VerifyObligation name)
                | CompletionDecision.AwaitPostMergeVerification reason ->
                    nextWithPostMergeVerification postMergeVerification snapshot MergedAwaitingObligations (AwaitPostMergeVerification reason)
                | CompletionDecision.ProjectCompletion ->
                    nextWithPostMergeVerification postMergeVerification snapshot MergedAwaitingObligations Complete
                | CompletionDecision.CleanupCompletedDelivery ->
                    nextWithPostMergeVerification postMergeVerification snapshot Done CleanupWorktree
            | _ when Option.isNone snapshot.Freshness.PullRequest ->
                next snapshot Implementation ContinueImplementation
            | _ ->
                match handoffProblem snapshot with
                | Some problem -> next snapshot ReviewReady (RepairReviewHandoff problem)
                | None when not snapshot.InReview -> next snapshot ReviewReady MoveToReview
                | None when not snapshot.ObligationsDeclared -> next snapshot ReviewReady (RepairReviewHandoff "delivery obligations are undeclared")
                | None ->
                    match reviewProblem snapshot with
                    | Some "independent review evidence is absent" -> next snapshot ReviewActive AwaitIndependentReview
                    | Some problem -> next snapshot ReviewActive (RefreshReview problem)
                    // Nothing is wrong with the evidence. What remains is a CI verdict, and the stage
                    // that names it already exists: `Landable`/`AwaitLandability` is what this same
                    // snapshot answered the moment `checksGreen` flipped true, and it is also literally
                    // the next step `pnext-item` section 6 prescribes after the live `delivery` call —
                    // wait on `landable` for this exact head. So the two snapshots the finding was
                    // measured with no longer differ in their REVIEW stage, and no parallel vocabulary
                    // is invented for a window `Landable` already describes (acceptance 1 and 3).
                    | None when reviewChecksPending snapshot || not snapshot.Landable ->
                        next snapshot Landable AwaitLandability
                    | None -> next snapshot Accepted GuardedLand

    let inspect snapshot =
        inspectWithPostMergeVerification NotObserved snapshot

    let advanceWithPostMergeVerification postMergeVerification freshnessToken actionKey snapshot =
        match inspectWithPostMergeVerification postMergeVerification snapshot with
        | Next transition when transition.FreshnessToken = freshnessToken && transition.ActionKey = actionKey ->
            Next transition
        | Next _ ->
            NoVerdict "delivery receipt is stale or does not authorize this transition"
        | NoVerdict reason -> NoVerdict reason

    let advance freshnessToken actionKey snapshot =
        advanceWithPostMergeVerification NotObserved freshnessToken actionKey snapshot
