namespace FS.GG.Coord

module Driver =
    open Batch
    open System
    open System.Text.Json

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

    [<Literal>]
    let private StructuredReviewMarker = "<!-- fsgg:review-decision/v2 -->"

    let decodeStructuredReview (raw: string) : Result<StructuredDecision.ReviewRecord, string> =
        let required (name: string) (root: JsonElement) =
            match root.TryGetProperty name with
            | true, value -> value
            | _ -> invalidArg name "required field is missing"
        let text (name: string) (root: JsonElement) =
            let value = required name root
            if value.ValueKind <> JsonValueKind.String then invalidArg name "must be a string"
            value.GetString()
        let optionalText (name: string) (root: JsonElement) =
            match root.TryGetProperty name with
            | false, _ -> None
            | true, value when value.ValueKind = JsonValueKind.Null -> None
            | true, value when value.ValueKind = JsonValueKind.String -> Some(value.GetString())
            | _ -> invalidArg name "must be a string or null"
        let texts (name: string) (root: JsonElement) =
            let value = required name root
            if value.ValueKind <> JsonValueKind.Array then invalidArg name "must be an array"
            value.EnumerateArray()
            |> Seq.map (fun entry ->
                if entry.ValueKind <> JsonValueKind.String then invalidArg name "must contain strings"
                entry.GetString())
            |> List.ofSeq
        let number (name: string) (root: JsonElement) =
            match (required name root).TryGetInt32() with
            | true, value -> value
            | _ -> invalidArg name "must be a 32-bit integer"
        let optionalBool (name: string) (root: JsonElement) =
            match root.TryGetProperty name with
            | false, _ -> false
            | true, value when value.ValueKind = JsonValueKind.True -> true
            | true, value when value.ValueKind = JsonValueKind.False -> false
            | _ -> invalidArg name "must be a boolean"
        let optionalTexts (name: string) (root: JsonElement) =
            match root.TryGetProperty name with
            | false, _ -> []
            | true, value when value.ValueKind = JsonValueKind.Array ->
                value.EnumerateArray()
                |> Seq.map (fun entry ->
                    if entry.ValueKind <> JsonValueKind.String then invalidArg name "must contain strings"
                    entry.GetString())
                |> List.ofSeq
            | _ -> invalidArg name "must be an array"
        // .github#2662: an absent key and an explicit null are the SAME fact — no grant — exactly as
        // `optionalText` already treats them for `previousDigest`, so a record written before the field
        // existed decodes unchanged. Anything else fails closed, and names `succession.<field>` rather
        // than the bare field name so a malformed grant says which half of the wire is wrong.
        let optionalSuccession (name: string) (root: JsonElement) =
            let inner (field: string) (value: JsonElement) =
                match value.TryGetProperty field with
                | true, entry when entry.ValueKind = JsonValueKind.String -> entry.GetString()
                | true, _ -> invalidArg $"%s{name}.%s{field}" "must be a string"
                | _ -> invalidArg $"%s{name}.%s{field}" "required field is missing"
            match root.TryGetProperty name with
            | false, _ -> None
            | true, value when value.ValueKind = JsonValueKind.Null -> None
            | true, value when value.ValueKind = JsonValueKind.Object ->
                Some
                    ({ OriginalCritic = inner "originalCritic" value
                       GrantedBy = inner "grantedBy" value
                       GrantUrl = inner "grantUrl" value }: StructuredDecision.SuccessionGrant)
            | _ -> invalidArg name "must be an object or null"
        try
            use document = JsonDocument.Parse raw
            let root = document.RootElement
            if root.ValueKind <> JsonValueKind.Object then invalidArg "record" "must be an object"
            let kind =
                match text "kind" root with
                | "initial" -> StructuredDecision.Initial
                | "confirmation" -> StructuredDecision.Confirmation
                | "escalation" -> StructuredDecision.Escalation
                | "repair-phase" -> StructuredDecision.RepairPhase
                | "acceptance" -> StructuredDecision.Acceptance
                | _ -> invalidArg "kind" "must be initial, confirmation, escalation, repair-phase, or acceptance"
            let verdict =
                match text "verdict" root with
                | "pass" -> StructuredDecision.Pass
                | "changes-required" -> StructuredDecision.ChangesRequired
                | "accepted" -> StructuredDecision.Accepted
                | _ -> invalidArg "verdict" "must be pass, changes-required, or accepted"
            Ok
                { Schema = text "schema" root
                  Subject = text "subject" root
                  Revision = number "revision" root
                  PreviousDigest = optionalText "previousDigest" root
                  HeadSha = text "headSha" root
                  Critic = text "critic" root
                  Verdict = verdict
                  AcceptedExceptions = texts "acceptedExceptions" root
                  RouteApplicability = text "routeApplicability" root
                  RouteEvidence = texts "routeEvidence" root
                  PolicyVersion = text "policyVersion" root
                  Kind = kind
                  Round = number "round" root
                  InitialReview = optionalText "initialReview" root
                  PrecedingReview = optionalText "precedingReview" root
                  DiffAuditRequired = optionalBool "diffAuditRequired" root
                  DiffAuditReceipts = optionalTexts "diffAuditReceipts" root
                  Succession = optionalSuccession "succession" root
                  Timestamp = text "timestamp" root
                  Digest = text "digest" root }
        with error -> Error error.Message

    let encodeStructuredReview (record: StructuredDecision.ReviewRecord) =
        let kind =
            match record.Kind with
            | StructuredDecision.Initial -> "initial"
            | StructuredDecision.Confirmation -> "confirmation"
            | StructuredDecision.Escalation -> "escalation"
            | StructuredDecision.RepairPhase -> "repair-phase"
            | StructuredDecision.Acceptance -> "acceptance"
        let verdict =
            match record.Verdict with
            | StructuredDecision.Pass -> "pass"
            | StructuredDecision.ChangesRequired -> "changes-required"
            | StructuredDecision.Accepted -> "accepted"
        // Projected through an anonymous record so the wire keys are the WIRE's (camel-cased, matching
        // every sibling field) rather than F#'s record property names, and emitted as an explicit null
        // when absent — the same spelling this encoder already uses for `previousDigest`,
        // `initialReview` and `precedingReview` (.github#2662).
        let succession =
            record.Succession
            |> Option.map (fun grant ->
                {| originalCritic = grant.OriginalCritic
                   grantedBy = grant.GrantedBy
                   grantUrl = grant.GrantUrl |})
        JsonSerializer.Serialize
            {| schema = record.Schema; subject = record.Subject; revision = record.Revision
               previousDigest = record.PreviousDigest; headSha = record.HeadSha; critic = record.Critic
               verdict = verdict; acceptedExceptions = record.AcceptedExceptions
               routeApplicability = record.RouteApplicability; routeEvidence = record.RouteEvidence
               policyVersion = record.PolicyVersion; kind = kind; round = record.Round
               initialReview = record.InitialReview; precedingReview = record.PrecedingReview
               diffAuditRequired = record.DiffAuditRequired; diffAuditReceipts = record.DiffAuditReceipts
               succession = succession
               timestamp = record.Timestamp; digest = record.Digest |}

    let private structuredReviewLedger (comments: ReviewComment list) =
        let marked =
            comments
            |> List.choose (fun comment ->
                if comment.Body.StartsWith(StructuredReviewMarker + "\n", StringComparison.Ordinal) then
                    Some(comment, comment.Body.Substring(StructuredReviewMarker.Length).Trim())
                else
                    None)

        if List.isEmpty marked then
            Error [ "structured review ledger is missing" ]
        else
            let decoded =
                marked
                |> List.map (fun (comment, raw) -> comment, decodeStructuredReview raw)

            let errors =
                decoded
                |> List.choose (fun (_, result) ->
                    match result with
                    | Error error -> Some error
                    | Ok _ -> None)

            if not (List.isEmpty errors) then
                Error errors
            else
                let pairs =
                    decoded
                    |> List.choose (fun (comment, result) ->
                        result |> Result.toOption |> Option.map (fun record -> comment, record))

                let records = pairs |> List.map snd
                let subject = records.Head.Subject

                StructuredDecision.validateReviewLedger subject records
                |> Result.map (fun _ -> subject, pairs)

    type ReviewPhaseFacts =
        { StructuredErrors: string list
          InitialCount: int
          InitialPresent: bool
          InitialHeadSha: string option
          InitialVerdict: string option
          CriticIdentity: string option
          ConfirmationCount: int
          LatestVerdict: string option
          LatestVerdictNearMissHints: string list
          LatestReviewedHeadSha: string option
          LatestReviewUrl: string option
          EscalationPresent: bool
          RepairPhasePresent: bool
          AcceptanceCount: int
          AcceptancePresent: bool }

    // A `critic:` value that is the bare, undifferentiated agent-type string every critic dispatched at
    // one route shares — `fsgg-critic-normal`, or any future `fsgg-critic-<route>` — rather than a
    // minted, distinguishing identity the way a worker's `whoami --mint` id is (.github#2451). Measured
    // live: two separate critics dispatched during one run both posted `critic: fsgg-critic-normal`.
    //
    // This was a PRIVATE copy here until `.github#2662`, whose ledger validator needs the same rule from
    // `StructuredDecision` — a module that compiles ahead of this one and owns the very record whose
    // `critic` field the predicate judges. The copy is therefore gone and this is an alias for the one
    // exported spelling, so a change to the rule can no longer leave this file disagreeing with the
    // validator. `Review.fs` still carries its own private copy under the same rename discipline: its
    // exact source lines are pinned as gate-inversion anchors by
    // `tests/review-critic-succession-wire/run.sh`, and moving them would silently disarm five legs
    // whose whole purpose is to prove that guard can refuse.
    let private isGenericCriticIdentity = StructuredDecision.isGenericCriticIdentity

    let reviewPhaseFacts (comments: ReviewComment list) : ReviewPhaseFacts =
        if List.isEmpty comments then
            { StructuredErrors = []
              InitialCount = 0
              InitialPresent = false
              InitialHeadSha = None
              InitialVerdict = None
              CriticIdentity = None
              ConfirmationCount = 0
              LatestVerdict = None
              LatestVerdictNearMissHints = []
              LatestReviewedHeadSha = None
              LatestReviewUrl = None
              EscalationPresent = false
              RepairPhasePresent = false
              AcceptanceCount = 0
              AcceptancePresent = false }
        else
            match structuredReviewLedger comments with
            | Error errors ->
                { StructuredErrors = errors
                  InitialCount = 0
                  InitialPresent = false
                  InitialHeadSha = None
                  InitialVerdict = None
                  CriticIdentity = None
                  ConfirmationCount = 0
                  LatestVerdict = None
                  LatestVerdictNearMissHints = []
                  LatestReviewedHeadSha = None
                  LatestReviewUrl = None
                  EscalationPresent = false
                  RepairPhasePresent = false
                  AcceptanceCount = 0
                  AcceptancePresent = false }
            | Ok(_, pairs) ->
                let ofKind kind = pairs |> List.filter (fun (_, record) -> record.Kind = kind)
                let initials = ofKind StructuredDecision.Initial
                let confirmations = ofKind StructuredDecision.Confirmation
                let acceptances = ofKind StructuredDecision.Acceptance
                let initial = initials |> List.tryLast
                let latestReview = confirmations |> List.tryLast |> Option.orElse initial
                let verdictName = function
                    | StructuredDecision.Pass -> "pass"
                    | StructuredDecision.ChangesRequired -> "changes-required"
                    | StructuredDecision.Accepted -> "accepted"

                { StructuredErrors = []
                  InitialCount = List.length initials
                  InitialPresent = not (List.isEmpty initials)
                  InitialHeadSha = initial |> Option.map (snd >> _.HeadSha)
                  InitialVerdict = initial |> Option.map (snd >> _.Verdict >> verdictName)
                  // The critic IN FORCE, not the one that opened the generation (.github#2662). The seat
                  // changes hands at a validated succession grant, so the last record's critic is the
                  // identity a further grant must name as its outgoing critic and the identity whose
                  // pass is being carried. For every ledger without a grant the two are the SAME string:
                  // `validateReviewLedger`'s unwidened conjunct forces every non-initial record in a
                  // generation to bind the generation's critic, and `reviewPhaseFacts` is only ever
                  // reached on a ledger that validated. So this is a correction for the case succession
                  // newly makes reachable, never a change to any answer the engine already gave.
                  CriticIdentity = pairs |> List.tryLast |> Option.map (snd >> _.Critic)
                  ConfirmationCount = List.length confirmations
                  LatestVerdict = latestReview |> Option.map (snd >> _.Verdict >> verdictName)
                  LatestVerdictNearMissHints = []
                  LatestReviewedHeadSha = latestReview |> Option.map (snd >> _.HeadSha)
                  LatestReviewUrl = latestReview |> Option.map (fst >> _.Url)
                  EscalationPresent = ofKind StructuredDecision.Escalation |> List.isEmpty |> not
                  RepairPhasePresent = ofKind StructuredDecision.RepairPhase |> List.isEmpty |> not
                  AcceptanceCount = List.length acceptances
                  AcceptancePresent = not (List.isEmpty acceptances) }

    type ChainRetirement =
        { InitialReviewUrl: string
          InitialReviewCommentId: int64
          AcceptedHead: string
          AcceptanceCommentId: int64 }

    type LiveReviewComments =
        { Live: ReviewComment list
          Retired: ChainRetirement list
          Diagnostics: string list
          StructuredSubject: string option
          StructuredErrors: string list }

    // Partition a PR's review comments into the chain that BINDS the current head and the chains that a
    // host acceptance already settled at a head the PR has moved off (.github#2527).
    //
    // THE RULE. A chain is retired when, and only when, a host-acceptance marker (a) names that chain's
    // initial-review comment URL in `initial-review:`, and (b) carries an `accepted-head:` that is NOT
    // `currentHead`. Both halves are read from the acceptance marker's own REQUIRED fields
    // (`Protocol.lifecyclePolicy.HostAcceptanceFields`), through the same private `field` reader and the
    // same `classifyMarkers` groups every other caller uses — this is not a second marker parser
    // (.github#2175 acceptance 11).
    //
    // WHY READ FROM THE MARKER AND NOT FROM A GRANT. `RepairPhaseReceipt` and `CriticSuccessionReceipt`
    // are out-of-band grants because the facts they carry are genuinely unobservable from the PR. "An
    // acceptance marker names this chain and carries an accepted-head that is not the current head" is
    // observable: it is written in the marker's own required fields. A grant would add a second, less
    // checkable channel for the same conclusion.
    //
    // BE PRECISE ABOUT WHAT IS OBSERVED, THOUGH — it is the marker's STRUCTURE, not the TRUTH of its
    // `accepted-head`. Nothing here verifies that the acceptance was genuine or that the head it names
    // was ever really accepted; a forged acceptance marker will retire a live chain. That is not the
    // hole it first looks like, and the reason is worth stating because "observed, not asserted" alone
    // overstates the guarantee:
    //
    //   A forged acceptance bound to the CURRENT head already yields `Accepted`/`Accept` outright, with
    //   an `AcceptedReceipt`. Retirement, reached from the same forgery, yields only a chain awaiting a
    //   FRESH host acceptance at the current head. So this route confers STRICTLY LESS than the forgery
    //   it presupposes, and it publishes the bogus value in `RetiredChains` where a reader can see it.
    //
    // The guarantee is therefore relative, not absolute: retirement adds no authority an attacker who
    // can forge acceptance markers does not already have, and it adds evidence they would rather not
    // leave. That is what keeps the one-initial-marker rule's protection intact.
    //
    // WHY ONLY WITH COMPETING INITIAL MARKERS. Retirement is a TIE-BREAKER between chains, never a
    // re-classification of one. With a single chain the pre-existing answer for an accepted-then-moved
    // head is unchanged and still refused (`ReviewTests.fs` `#2175 a changed head after acceptance
    // invalidates the prior accepted evidence`) — so this change cannot alter any verdict on a PR that
    // carries one chain, which is every PR the protocol was already able to describe.
    let liveReviewComments (currentHead: string) (comments: ReviewComment list) : LiveReviewComments =
        let structuredPresent =
            comments |> List.exists (fun comment -> comment.Body.StartsWith(StructuredReviewMarker + "\n", StringComparison.Ordinal))

        if not structuredPresent then
            { Live = []
              Retired = []
              Diagnostics = []
              StructuredSubject = None
              StructuredErrors = [] }
        else
            match structuredReviewLedger comments with
            | Error errors ->
                { Live = []
                  Retired = []
                  Diagnostics = []
                  StructuredSubject = None
                  StructuredErrors = errors }
            | Ok(subject, pairs) ->
                let indexed = pairs |> List.indexed
                let initialIndexes =
                    indexed
                    |> List.choose (fun (index, (_, record)) ->
                        if record.Kind = StructuredDecision.Initial then Some index else None)

                let generation start finish = pairs[start .. finish - 1]
                let generations =
                    initialIndexes
                    |> List.mapi (fun index start ->
                        let finish =
                            if index + 1 < initialIndexes.Length then initialIndexes[index + 1]
                            else pairs.Length
                        generation start finish)

                let retired =
                    if generations.Length <= 1 then
                        []
                    else
                        generations
                        |> List.take (generations.Length - 1)
                        |> List.choose (fun entries ->
                            let initialComment, _ = entries.Head
                            entries
                            |> List.tryFind (fun (_, record) -> record.Kind = StructuredDecision.Acceptance)
                            |> Option.bind (fun (acceptanceComment, acceptance) ->
                                if acceptance.HeadSha = currentHead then None
                                else
                                    Some
                                        { InitialReviewUrl = initialComment.Url
                                          InitialReviewCommentId = initialComment.Id
                                          AcceptedHead = acceptance.HeadSha
                                          AcceptanceCommentId = acceptanceComment.Id }))

                let live =
                    match generations with
                    | [] -> []
                    | values -> values |> List.last |> List.map fst

                { Live = live
                  Retired = retired
                  Diagnostics = []
                  StructuredSubject = Some subject
                  StructuredErrors = [] }

    let private parseStructuredComments
        (trustedFacts: (bool * SemanticDiff.TrustedAudit option) option)
        (comments: ReviewComment list)
        =
        match structuredReviewLedger comments with
        | Error errors -> Error errors
        | Ok(_, pairs) ->
            let generation =
                pairs
                |> List.indexed
                |> List.choose (fun (index, (_, record)) ->
                    if record.Kind = StructuredDecision.Initial then Some index else None)
                |> List.tryLast
                |> Option.map (fun start -> pairs[start..])
                |> Option.defaultValue pairs
            let initial = generation |> List.tryFind (fun (_, record) -> record.Kind = StructuredDecision.Initial)
            let confirmations = generation |> List.filter (fun (_, record) -> record.Kind = StructuredDecision.Confirmation)
            let escalations = generation |> List.filter (fun (_, record) -> record.Kind = StructuredDecision.Escalation)
            let repairs = generation |> List.filter (fun (_, record) -> record.Kind = StructuredDecision.RepairPhase)
            let acceptances = generation |> List.filter (fun (_, record) -> record.Kind = StructuredDecision.Acceptance)
            let errors = ResizeArray<string>()

            let routeEvidence (record: StructuredDecision.ReviewRecord) =
                match record.RouteApplicability, record.RouteEvidence with
                | "meaningful", [ built; command; compared; observed ] ->
                    Some(Meaningful(built, command, compared, observed))
                | "not-meaningful", [ reason ] -> Some(NotMeaningful reason)
                | _ -> None

            match initial, acceptances with
            | Some(initialComment, first), [ (acceptanceComment, accepted) ] ->
                let latestComment, latest = confirmations |> List.tryLast |> Option.defaultValue (initialComment, first)
                let repairPhase = not (List.isEmpty repairs)
                let ceiling =
                    if repairPhase then Protocol.reviewPolicy.RepairPhaseMaxRounds
                    else Protocol.reviewPolicy.MaxAutomatedRepairRounds

                if isGenericCriticIdentity first.Critic then
                    errors.Add "review critic identity must be minted and distinguishing"
                if latest.Verdict <> StructuredDecision.Pass then
                    errors.Add "the latest structured critic decision must have verdict pass"
                if confirmations.Length > ceiling then
                    errors.Add "review confirmation round ceiling exceeded"
                if not (List.isEmpty escalations) && not repairPhase then
                    errors.Add "structured escalation requires a structured repair-phase record"
                if String.IsNullOrWhiteSpace initialComment.Url then
                    errors.Add "the initial structured review comment URL is missing"
                if String.IsNullOrWhiteSpace latestComment.Url then
                    errors.Add "the latest structured review comment URL is missing"
                if accepted.HeadSha <> latest.HeadSha then
                    errors.Add "acceptance is not bound to the latest reviewed head"
                if accepted.InitialReview <> Some initialComment.Url then
                    errors.Add "acceptance is not bound to the initial structured review comment URL"
                if accepted.PrecedingReview <> Some latestComment.Url then
                    errors.Add "acceptance is not bound to the latest structured review comment URL"
                if acceptanceComment.Id <= latestComment.Id then
                    errors.Add "host acceptance must follow the latest structured review record"

                let mechanicallyRequired = trustedFacts |> Option.exists fst
                let effectiveAuditRequired = first.DiffAuditRequired || mechanicallyRequired
                let mutable auditHead = None

                if effectiveAuditRequired then
                    if List.isEmpty accepted.DiffAuditReceipts then
                        errors.Add "a required typed diff-audit receipt was not submitted"
                    else
                        let parsed = accepted.DiffAuditReceipts |> List.map SemanticDiff.ofBase64
                        if parsed |> List.exists Result.isError then
                            errors.Add "a submitted typed diff-audit receipt is malformed"
                        else
                            let submitted = parsed |> List.choose Result.toOption

                            // RECEIPT-INTRINSIC, so it is decided on EVERY path (.github#2694). Which head
                            // the submitted receipts bind is a fact about the receipts themselves, and
                            // `reviewChainProblems` reads the answer back as `DiffAuditRequired &&
                            // DiffAuditHead <> HeadSha`. Leaving it unresolved on the facts-free path would
                            // merely MOVE the wedge this item removes out of the parser and into
                            // `Delivery.reviewProblem`, which is why it is hoisted above the live-facts
                            // match rather than left inside it.
                            match submitted |> List.map _.HeadSha |> List.distinct with
                            | [ head ] -> auditHead <- Some head
                            | _ -> errors.Add "the submitted typed diff-audit receipts are not all bound to one head"

                            // THE CALLER'S STATE IS NOT A VERDICT ABOUT THE SUBJECT (.github#2694, an
                            // instance of .github#266 in the `facts` formulation). NO INVENTORY WAS
                            // SUPPLIED is the one fact this arm reads, and it is spelled `None` by BOTH
                            // routes into it — the facts-free callers, who pass no facts at all, and a
                            // facts-bearing caller whose `trustedAudit` is `None`. Neither read the diff,
                            // so neither can be checked against, so this parse renders NO verdict about
                            // the receipts.
                            //
                            // THAT SECOND ROUTE IS NOT HYPOTHETICAL AND IS WHY THIS IS `Option.bind snd`
                            // RATHER THAN A DEFAULTED EMPTY INVENTORY (round-1 finding M1). Every
                            // production caller on the `review` path passes `None` here:
                            // `Review.acceptanceOutcome` derives BOTH of this function's arguments from
                            // the single field `Facts.DiffAuditTrusted` (`Review.fs:368-369`), and that
                            // field is hardcoded `None` at both of its constructors — the snapshot route
                            // (`ReviewApplication.fs:159`, whose own doc comment says "Always `None`
                            // here") and the live `review <ref>` route (`Client.fs:2133`). Treating that
                            // `None` as "the engine recomputed an EMPTY inventory" would make every
                            // correct receipt on the review path "stale or does not match live delivery
                            // facts" — trading a true statement about the caller for a FALSE accusation
                            // against the subject, which is this item's own defect one match-arm over and
                            // in the more damaging direction.
                            //
                            // A caller that genuinely recomputed an empty inventory says so by SPELLING
                            // it: `Some { Expected = []; Discovered = [] }`. That is already expressible
                            // and is the shape `parseReviewCommentsWithAudit` constructs below, so the
                            // two facts stay distinguishable in the type without either being inferred
                            // from an absence.
                            //
                            // Refusing on `None` made a generation whose initial record sets
                            // `diffAuditRequired: true` permanently unacceptable: `review record` seals its
                            // acceptance through `parseEffectiveReviewComments`, the facts-free spelling, and
                            // both escapes were closed — a second `initial` is refused until a host
                            // acceptance that could never be written. Every receipt shape, correct or not,
                            // produced that same one message, so the refusal separated nothing and removing
                            // it withdraws no detection this parser ever had.
                            match trustedFacts |> Option.bind snd with
                            | None -> ()
                            | Some trusted ->
                                for receipt in submitted do
                                    match
                                        trusted.Expected
                                        |> List.tryFind (fun expected ->
                                            expected.OldToken = receipt.OldToken
                                            && expected.NewToken = receipt.NewToken
                                            && expected.DeclaredPaths = receipt.DeclaredPaths)
                                    with
                                    | Some expected when SemanticDiff.validateAgainst expected receipt |> List.isEmpty -> ()
                                    | _ -> errors.Add "a submitted typed diff-audit receipt is stale or does not match live delivery facts"

                                let accounted = submitted |> List.collect _.Occurrences |> List.map _.Id |> Set.ofList
                                let uncovered = trusted.Discovered |> List.filter (fun item -> not (accounted.Contains item.Id))
                                if not (List.isEmpty uncovered) then
                                    errors.Add
                                        $"the submitted typed diff-audit receipts account for %d{trusted.Discovered.Length - uncovered.Length} of %d{trusted.Discovered.Length} discovered occurrences"

                if errors.Count = 0 then
                    let rounds = if List.isEmpty confirmations then [ 1 ] else [ 1 .. confirmations.Length ]
                    // The critic IN FORCE at the end of the live generation (.github#2662) — the one whose
                    // pass the host accepted — rather than the generation's opening record. Identical to
                    // `first.Critic` on every grant-free ledger, for the reason `reviewPhaseFacts` states.
                    let generationCritic =
                        generation |> List.tryLast |> Option.map (snd >> _.Critic) |> Option.defaultValue first.Critic
                    Ok
                        { MarkerValid = true
                          CriticIdentity = Some generationCritic
                          HeadSha = Some latest.HeadSha
                          Rounds = rounds
                          RepairPhase = repairPhase
                          ChecksGreen = false
                          HostAccepted = true
                          RuntimeRouteEvidence = routeEvidence latest
                          DiffAuditRequired = effectiveAuditRequired
                          DiffAuditHead = auditHead }
                else
                    Error(List.ofSeq errors)
            | None, _ -> Error [ "exactly one structured initial review record is required" ]
            | _, [] -> Error [ "exactly one structured acceptance record is required" ]
            | _, _ -> Error [ "exactly one structured acceptance record is required" ]

    let private parseNormalized trusted comments = parseStructuredComments trusted comments

    let parseReviewComments comments = parseNormalized None comments

    let parseReviewCommentsWithAudit (trustedAudit: SemanticDiff.Receipt) comments =
        // The single-receipt spelling stays available: one receipt whose own recomputation IS the whole
        // discovered population, which is the shape every pre-round-2 caller meant.
        parseNormalized
            (Some(true, Some { Expected = [ trustedAudit ]; Discovered = trustedAudit.Occurrences }))
            comments

    let parseReviewCommentsWithFacts mechanicallyRequired trustedAudit comments =
        parseNormalized (Some(mechanicallyRequired, trustedAudit)) comments

    let parseEffectiveReviewComments currentHead comments =
        let live = liveReviewComments currentHead comments
        if not (List.isEmpty live.StructuredErrors) then Error live.StructuredErrors
        else parseReviewComments comments

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

    type ContentDisposition =
        | NotReusable
        | Skill
        | ExampleFixture
        | SkillAndExampleFixture

    type ContentEvidence =
        | EvidenceUrl of string
        | EvidencePath of string

    type ContentDispositionReceipt =
        { SourceFinding: string
          Disposition: ContentDisposition
          ConsumerPaths: string list
          DecisionMaker: string
          Rationale: string
          Evidence: ContentEvidence option
          ObservedAt: int64
          SourceSha: string
          ReceiptId: string }

    type PlanningReceipt =
        { ObservedAt: int64
          SourceSha: string
          Complete: bool
          ConsolidationApproved: bool
          Observations: PlanningObservation list
          ContentIntakes: string list
          ContentDispositions: ContentDispositionReceipt list }

    let observationReceiptId kind observedAt sourceSha outcome =
        $"%s{kind}\n%d{observedAt}\n%s{sourceSha}\n%s{outcome}"
        |> System.Text.Encoding.UTF8.GetBytes
        |> System.Security.Cryptography.SHA256.HashData
        |> System.Convert.ToHexString
        |> fun value -> value.ToLowerInvariant()

    let contentDispositionReceiptId sourceFinding disposition (consumerPaths: string list) decisionMaker rationale evidence (observedAt: int64) sourceSha =
        let kind =
            match disposition with
            | NotReusable -> "not-reusable"
            | Skill -> "skill"
            | ExampleFixture -> "example/fixture"
            | SkillAndExampleFixture -> "skill+example/fixture"

        let evidenceText =
            match evidence with
            | Some(EvidenceUrl value) -> "url:" + value
            | Some(EvidencePath value) -> "path:" + value
            | None -> ""

        [ sourceFinding; kind; String.concat "\u001f" consumerPaths; decisionMaker; rationale; evidenceText; string observedAt; sourceSha ]
        |> String.concat "\n"
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

        let nonEmpty value = not (System.String.IsNullOrWhiteSpace value)
        let skillPath (path: string) =
            (path.StartsWith ".agents/skills/" || path.StartsWith ".claude/skills/")
            && path.EndsWith ".md"
        let executablePath (path: string) =
            path.StartsWith "tests/"
            || path.Contains "/fixtures/"
            || path.EndsWith ".fsx"
            || path.EndsWith ".sh"
            || path.EndsWith ".py"
        let evidencePathValid (value: string) =
            let separator = value.LastIndexOf ':'
            if separator <= 0 || separator = value.Length - 1 then
                false
            else
                let path, line = value.Substring(0, separator), value.Substring(separator + 1)
                match System.Int32.TryParse line with
                | true, positiveLine ->
                    nonEmpty path
                    && path.Contains "/"
                    && not (path.StartsWith "/")
                    && not (path.Contains "..")
                    && positiveLine > 0
                | false, _ -> false
        let evidenceUrlValid (value: string) =
            match System.Uri.TryCreate(value, System.UriKind.Absolute) with
            | true, uri -> (uri.Scheme = System.Uri.UriSchemeHttp || uri.Scheme = System.Uri.UriSchemeHttps) && nonEmpty uri.Host
            | false, _ -> false
        let evidenceValid = function
            | Some(EvidenceUrl value) -> evidenceUrlValid value
            | Some(EvidencePath value) -> evidencePathValid value
            | None -> false
        let dispositionValid disposition =
            let consumerPaths = disposition.ConsumerPaths
            let pathsAreConcrete = consumerPaths |> List.forall nonEmpty
            let consumerShapeValid =
                match disposition.Disposition with
                | NotReusable -> List.isEmpty consumerPaths && nonEmpty disposition.Rationale && evidenceValid disposition.Evidence
                | Skill -> pathsAreConcrete && (consumerPaths |> List.exists skillPath)
                | ExampleFixture -> pathsAreConcrete && (consumerPaths |> List.exists executablePath)
                | SkillAndExampleFixture ->
                    pathsAreConcrete
                    && (consumerPaths |> List.exists skillPath)
                    && (consumerPaths |> List.exists executablePath)

            nonEmpty disposition.SourceFinding
            && nonEmpty disposition.DecisionMaker
            && disposition.SourceSha = sourceSha
            && now >= disposition.ObservedAt
            && now - disposition.ObservedAt <= maxAgeSeconds
            && consumerShapeValid
            && disposition.ReceiptId = contentDispositionReceiptId
                disposition.SourceFinding
                disposition.Disposition
                disposition.ConsumerPaths
                disposition.DecisionMaker
                disposition.Rationale
                disposition.Evidence
                disposition.ObservedAt
                disposition.SourceSha

        let inventoryValid =
            receipt.ContentIntakes |> List.forall nonEmpty
            && (receipt.ContentIntakes |> Set.ofList |> Set.count) = List.length receipt.ContentIntakes
            && (receipt.ContentDispositions |> List.map (fun disposition -> disposition.SourceFinding) |> Set.ofList)
                = (receipt.ContentIntakes |> Set.ofList)
            && List.length receipt.ContentDispositions = List.length receipt.ContentIntakes

        receipt.Complete
        && not (System.String.IsNullOrWhiteSpace receipt.SourceSha)
        && receipt.SourceSha = sourceSha
        && now >= receipt.ObservedAt
        && now - receipt.ObservedAt <= maxAgeSeconds
        && List.length receipt.Observations = List.length expected
        && (expected |> List.forall observationValid)
        && inventoryValid
        && (receipt.ContentDispositions |> List.forall dispositionValid)

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

    // Every problem a review chain can carry, each tagged `true` when it is a STRUCTURAL fact about the
    // durable review evidence and `false` when it is a LIVENESS fact about the pull request's current
    // check run (.github#2549).
    //
    // The two kinds were indistinguishable, and that cost a healthy chain. `Review.acceptanceOutcome`
    // folded every message here into `MalformedEvidence` — the same state a chain carrying two
    // competing initial markers reaches, whose taught recovery is to close the pull request without
    // merging. `.github#2504` then made "checks are not green" a condition every ordinary landing
    // passes through by design: `claim-generation` is a required context on `main` whose marker is
    // written by the `delivery` call `pnext-item` §6 places AFTER host acceptance, so it CANNOT be
    // green at the moment acceptance is first observed. Measured on `.github#2534` / PR #2541: a chain
    // with one initial marker, one confirmation, one critic and a correctly bound acceptance reported
    // `{"state":"malformedEvidence","stateErrors":["review checks are not green"],"action":"park"}`.
    // PR #2514 was closed without merging and reopened as #2528 on that reading on 2026-08-13.
    //
    // ONE list, ONE order. `validateReviewChain` below is exactly this list's messages, so the split is
    // a property of the construction rather than a promise — `Delivery.reviewProblem` and
    // `receiptFresh` cannot drift from it, and a later reword of any message cannot silently
    // reintroduce the conflation the way a string match in a second file would.
    let private reviewChainProblems maxRounds chain =
        [ if not chain.MarkerValid then
              true, "review marker is missing or invalid"
          if Option.isNone chain.CriticIdentity then
              true, "critic identity is missing"
          if Option.isNone chain.HeadSha then
              true, "review head SHA is missing"
          if List.isEmpty chain.Rounds || chain.Rounds <> [ 1 .. List.length chain.Rounds ] then
              true, "review rounds are not ordered from one"
          if List.length chain.Rounds > maxRounds then
              true, "review round ceiling exceeded"
          if Option.isNone chain.RuntimeRouteEvidence then
              true, "runtime-route applicability evidence is missing"
          if chain.DiffAuditRequired && chain.DiffAuditHead <> chain.HeadSha then
              true, "required diff-audit receipt is missing, stale, or unresolved"
          // The ONLY liveness clause. Everything above is a fact about what the critic and host durably
          // wrote; this one is a fact about a CI run that has not reported yet.
          if not chain.ChecksGreen then
              false, "review checks are not green"
          // STRUCTURAL, deliberately: "no host acceptance marker is present" is a completeness fact
          // about the durable evidence, not about a check run. `Review.acceptanceOutcome` is only
          // reached when an acceptance IS present, so this clause can never fire on the path
          // .github#2549 introduces; tagging it structural is therefore both correct and inert there.
          if not chain.HostAccepted then
              true, "host acceptance is missing" ]

    let validateReviewChain maxRounds chain =
        reviewChainProblems maxRounds chain |> List.map snd

    let validateReviewChainStructure maxRounds chain =
        reviewChainProblems maxRounds chain
        |> List.choose (fun (structural, message) -> if structural then Some message else None)

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
