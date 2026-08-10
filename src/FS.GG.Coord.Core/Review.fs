namespace FS.GG.Coord

module Review =
    open System
    open System.Security.Cryptography
    open System.Text
    open Types

    type Phase =
        | Ordinary
        | Repair

    type Binding =
        { ItemRef: string
          Pr: int
          HeadSha: string
          ClaimGeneration: string
          ImplementerIdentity: string
          Phase: Phase
          Round: int }

    type RepairPhaseReceipt =
        { ExhaustedPr: int
          EscalationCommentId: int64
          NewClaimGeneration: string
          NewBranchOrPr: string
          NewImplementerIdentity: string
          NewCriticIdentity: string
          CandidateHeadSha: string }

    type Facts =
        { Comments: Driver.ReviewComment list
          Checks: PrState
          RepairPhaseGranted: RepairPhaseReceipt option
          RepairRouteAvailable: bool
          DiffAuditTrusted: SemanticDiff.TrustedAudit option }

    type State =
        | AwaitingInitialReview
        | ChangesRequiringRepair of round: int
        | AwaitingImplementerRepair of round: int
        | AwaitingSameCriticConfirmation of round: int
        | PassedAwaitingChecks
        | AwaitingHostAcceptance
        | OrdinaryExhaustion
        | RepairPhaseSetup
        | RepairPhaseActive of round: int
        | Accepted
        | TerminalHumanPark of reason: string
        | MalformedEvidence of errors: string list
        | GuardViolation of reason: string

    type AcceptedReceipt =
        { HeadSha: string
          CriticIdentity: string
          Rounds: int list
          RepairPhase: bool
          ChecksGreen: bool
          RuntimeRouteEvidence: Driver.RuntimeRouteEvidence option
          DiffAuditRequired: bool
          DiffAuditHead: string option }

    type NextAction =
        | DispatchCritic
        | ResumeImplementer of reason: string
        | ResumeSameCritic of reason: string
        | AwaitChecks
        | RequestHostAcceptance
        | EnterRepairPhase of RepairPhaseReceipt
        | Accept of AcceptedReceipt
        | Park of reason: string

    type Verdict =
        { State: State
          NextAction: NextAction
          FreshnessToken: string
          ActionKey: string }

    let private digest (value: string) =
        value
        |> Encoding.UTF8.GetBytes
        |> SHA256.HashData
        |> Convert.ToHexString
        |> fun hash -> hash.ToLowerInvariant()

    let private phaseToken =
        function
        | Ordinary -> "ordinary"
        | Repair -> "repair"

    /// Folds the FULL binding — including `HeadSha` — into the digest, so a new commit changes the
    /// token and invalidates any verdict inspected against the old head (acceptance 4/9).
    let freshnessToken (binding: Binding) =
        [ binding.ItemRef
          string binding.Pr
          binding.HeadSha
          binding.ClaimGeneration
          binding.ImplementerIdentity
          phaseToken binding.Phase
          string binding.Round ]
        |> String.concat "\n"
        |> digest

    let private makeVerdict binding state action =
        let token = freshnessToken binding
        let actionKey = digest $"%s{token}\n%A{state}\n%A{action}"
        { State = state; NextAction = action; FreshnessToken = token; ActionKey = actionKey }

    let private missing value label =
        if String.IsNullOrWhiteSpace(value: string) then Some label else None

    let private validateBinding (binding: Binding) =
        [ missing binding.ItemRef "item ref"
          (if binding.Pr <= 0 then Some "pull request" else None)
          missing binding.HeadSha "head SHA"
          missing binding.ClaimGeneration "claim generation"
          missing binding.ImplementerIdentity "implementer identity"
          (if binding.Round < 1 then Some "round" else None) ]
        |> List.choose id

    let private ceilingFor phase =
        match phase with
        | Ordinary -> Protocol.reviewPolicy.MaxAutomatedRepairRounds
        | Repair -> Protocol.reviewPolicy.RepairPhaseMaxRounds

    /// The shared acceptance path for both ordinary and repair-phase review: reuses
    /// `Driver.parseReviewCommentsWithFacts` (the terminal chain parser) and `Driver.validateReviewChain`
    /// (the FS-GG/.github#2136 generated round-ceiling policy) as the sole authorities — no marker text
    /// and no ceiling is defined in this module (acceptance 11). A chain whose head does not match the
    /// current binding is treated exactly like any other unresolved evidence (acceptance 4): the prior
    /// pass/checks/host-acceptance is invalidated by the new commit.
    let private acceptanceOutcome (binding: Binding) (facts: Facts) =
        let mechanicallyRequired = facts.DiffAuditTrusted.IsSome
        match Driver.parseReviewCommentsWithFacts mechanicallyRequired facts.DiffAuditTrusted facts.Comments with
        | Error errors -> MalformedEvidence errors, Park(String.concat "; " errors)
        | Ok chain ->
            let checksGreen = facts.Checks = PrGreen
            let chainWithChecks = { chain with ChecksGreen = checksGreen }
            let ceiling = ceilingFor binding.Phase
            match Driver.validateReviewChain ceiling chainWithChecks, chain.CriticIdentity with
            | [], Some critic when chain.HeadSha = Some binding.HeadSha ->
                Accepted,
                Accept
                    { HeadSha = binding.HeadSha
                      CriticIdentity = critic
                      Rounds = chain.Rounds
                      RepairPhase = chain.RepairPhase
                      ChecksGreen = checksGreen
                      RuntimeRouteEvidence = chain.RuntimeRouteEvidence
                      DiffAuditRequired = chain.DiffAuditRequired
                      DiffAuditHead = chain.DiffAuditHead }
            | [], Some _ ->
                let reason = "the accepted review chain is bound to a different head than the current commit"
                MalformedEvidence [ reason ], Park reason
            | [], None ->
                let reason = "the accepted review chain carries no critic identity"
                MalformedEvidence [ reason ], Park reason
            | errors, _ -> MalformedEvidence errors, Park(String.concat "; " errors)

    let private classify (binding: Binding) (facts: Facts) : State * NextAction =
        let phaseFacts = Driver.reviewPhaseFacts facts.Comments

        if phaseFacts.CriticIdentity = Some binding.ImplementerIdentity then
            let reason = "the critic identity equals the implementer identity; an implementer cannot act as its own critic"
            GuardViolation reason, Park reason
        elif phaseFacts.InitialCount > 1 then
            let reason =
                $"the initial review marker is carried by %d{phaseFacts.InitialCount} comments; exactly one is required"
            MalformedEvidence [ reason ], Park reason
        elif phaseFacts.AcceptanceCount > 1 then
            let reason =
                $"the host-acceptance marker is carried by %d{phaseFacts.AcceptanceCount} comments; exactly one is required"
            MalformedEvidence [ reason ], Park reason
        else
            let ceiling = ceilingFor binding.Phase
            let exhausted = phaseFacts.ConfirmationCount > ceiling && not phaseFacts.AcceptancePresent

            match binding.Phase with
            | Repair ->
                if not phaseFacts.RepairPhasePresent then
                    match facts.RepairPhaseGranted with
                    | Some receipt -> RepairPhaseSetup, EnterRepairPhase receipt
                    | None ->
                        let reason =
                            "binding declares an active repair phase but no repair-phase marker is present in "
                            + "comments and no repair-phase receipt was supplied"
                        TerminalHumanPark reason, Park reason
                elif exhausted then
                    let reason = "the repair-phase confirmation round ceiling is exhausted with no acceptance; no further automatic route exists"
                    TerminalHumanPark reason, Park reason
                elif phaseFacts.AcceptancePresent then
                    acceptanceOutcome binding facts
                elif not phaseFacts.InitialPresent then
                    RepairPhaseSetup, DispatchCritic
                else
                    let round = phaseFacts.ConfirmationCount + 1
                    match phaseFacts.LatestVerdict with
                    | Some "pass" ->
                        if facts.Checks = PrGreen then
                            RepairPhaseActive round, RequestHostAcceptance
                        else
                            RepairPhaseActive round, AwaitChecks
                    | Some "changes-required" ->
                        match phaseFacts.LatestReviewedHeadSha with
                        | None ->
                            let reason = "a changes-required verdict carries no readable reviewed-head field"
                            MalformedEvidence [ reason ], Park reason
                        | Some reviewedHead when reviewedHead = binding.HeadSha ->
                            RepairPhaseActive round,
                            ResumeImplementer "the critic requested changes at the current head; no new commit has landed yet"
                        | Some _ ->
                            RepairPhaseActive round,
                            ResumeSameCritic "a new commit landed after a changes-required verdict; the same critic must confirm it"
                    | _ ->
                        let reason = "the latest review verdict is neither readable pass nor changes-required"
                        MalformedEvidence [ reason ], Park reason
            | Ordinary ->
                if not phaseFacts.InitialPresent then
                    AwaitingInitialReview, DispatchCritic
                elif exhausted then
                    match facts.RepairPhaseGranted with
                    | Some receipt -> RepairPhaseSetup, EnterRepairPhase receipt
                    | None ->
                        if facts.RepairRouteAvailable then
                            let reason =
                                "the ordinary review chain is exhausted; the host must mint the one permitted "
                                + "fresh repair phase (new claim, branch/PR, implementer, critic) and re-inspect "
                                + "with a repair-phase receipt supplied"
                            OrdinaryExhaustion, Park reason
                        else
                            let reason = "the ordinary review chain is exhausted and no repair route is available"
                            TerminalHumanPark reason, Park reason
                elif phaseFacts.AcceptancePresent then
                    acceptanceOutcome binding facts
                else
                    let round = phaseFacts.ConfirmationCount + 1
                    match phaseFacts.LatestVerdict with
                    | Some "pass" ->
                        if facts.Checks = PrGreen then
                            AwaitingHostAcceptance, RequestHostAcceptance
                        else
                            PassedAwaitingChecks, AwaitChecks
                    | Some "changes-required" ->
                        match phaseFacts.LatestReviewedHeadSha with
                        | None ->
                            let reason = "a changes-required verdict carries no readable reviewed-head field"
                            MalformedEvidence [ reason ], Park reason
                        | Some reviewedHead when reviewedHead = binding.HeadSha ->
                            AwaitingImplementerRepair round,
                            ResumeImplementer "the critic requested changes at the current head; no new commit has landed yet"
                        | Some _ ->
                            AwaitingSameCriticConfirmation round,
                            ResumeSameCritic "a new commit landed after a changes-required verdict; the same critic must confirm it"
                    | _ ->
                        let reason = "the latest review verdict is neither readable pass nor changes-required"
                        MalformedEvidence [ reason ], Park reason

    let inspect (binding: Binding) (facts: Facts) : Result<Verdict, string list> =
        match validateBinding binding with
        | problems when not (List.isEmpty problems) ->
            Error(problems |> List.map (fun field -> $"review binding is incomplete: missing %s{field}"))
        | _ ->
            let state, action = classify binding facts
            Ok(makeVerdict binding state action)

    let advance freshnessToken actionKey binding facts =
        match inspect binding facts with
        | Ok verdict when verdict.FreshnessToken = freshnessToken && verdict.ActionKey = actionKey -> Ok verdict
        | Ok _ -> Error [ "review verdict is stale or does not authorize this transition; re-inspect before advancing" ]
        | Error reasons -> Error reasons
