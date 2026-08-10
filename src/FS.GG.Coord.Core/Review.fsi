namespace FS.GG.Coord

/// THE RESUMABLE REVIEW/REPAIR PROTOCOL (.github#2175) — the typed layer between a claimed item
/// reaching `ReviewActive` (`Delivery.Stage.ReviewActive`, .github#2131) and the accepted-current-head
/// receipt that stage consumes to move on. `Driver.parseReviewComments`/`Driver.validateReviewChain`
/// (.github#2127) validate the TERMINAL review chain — accepted or not — and `Delivery.reviewProblem`
/// only ever sees two shapes of "not yet": absent, or one joined string. Neither represents WHICH of the
/// alternating critic/implementer states is live, who acts next, or whether a repair phase has already
/// been granted. This module is that missing state.
///
/// It reuses, rather than re-derives, everything `Driver` already computes: `Driver.reviewPhaseFacts`
/// for the structural marker classification (leading-marker-block detection, quoting-awareness, field
/// grammar — .github#2221/#2248), and `Driver.parseReviewComments`/`Driver.validateReviewChain` for the
/// terminal-chain validation and the FS-GG/.github#2136 generated round-ceiling policy
/// (`Protocol.reviewPolicy`). No marker text and no round ceiling is defined here (acceptance 11).
module Review =

    open Types

    /// Which chain is currently live: the ordinary automated-confirmation chain, or the one fresh
    /// repair phase ordinary exhaustion permits (acceptance 6).
    type Phase =
        | Ordinary
        | Repair

    /// The identity/context every state and action is bound to (acceptance 4). A caller assembles this
    /// from claim and PR facts; `inspect` never infers identity from comment bodies beyond the critic
    /// identity `Driver.reviewPhaseFacts` already extracts.
    type Binding =
        { ItemRef: string
          Pr: int
          HeadSha: string
          ClaimGeneration: string
          ImplementerIdentity: string
          Phase: Phase
          Round: int }

    /// The complete provenance a granted repair phase carries (acceptance 6): the exhausted PR and its
    /// escalation-marker comment, and the new claim/branch/PR/implementer/critic/head the fresh phase is
    /// bound to. Idempotent: a second `inspect` over the SAME granted receipt reuses it rather than
    /// minting another.
    type RepairPhaseReceipt =
        { ExhaustedPr: int
          EscalationCommentId: int64
          NewClaimGeneration: string
          NewBranchOrPr: string
          NewImplementerIdentity: string
          NewCriticIdentity: string
          CandidateHeadSha: string }

    /// Facts read live by the caller — PR comments, check state, and the two facts this pure engine
    /// cannot observe itself (clarifications DEC-002): whether a fresh repair phase has already been
    /// granted, and whether a repair route (fresh critic/worker capacity) is available at all.
    type Facts =
        { Comments: Driver.ReviewComment list
          Checks: PrState
          RepairPhaseGranted: RepairPhaseReceipt option
          RepairRouteAvailable: bool
          DiffAuditTrusted: SemanticDiff.TrustedAudit option }

    /// The closed review-protocol state model (acceptance 1). `MalformedEvidence` and `GuardViolation`
    /// are additional to the issue's named list — "at least" those names — and exist so a parser error
    /// or a critic-independence violation is a distinct, typed fact rather than folded into an existing
    /// named state (acceptance 8).
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

    /// One accepted-current-head receipt, shaped to be exactly what `Driver.ReviewChain` already
    /// carries (acceptance 10) — `Delivery.fromReviewAcceptance` folds it into a `Delivery.Snapshot`
    /// without `Delivery` learning any of this module's states.
    type AcceptedReceipt =
        { HeadSha: string
          CriticIdentity: string
          Rounds: int list
          RepairPhase: bool
          ChecksGreen: bool
          RuntimeRouteEvidence: Driver.RuntimeRouteEvidence option
          DiffAuditRequired: bool
          DiffAuditHead: string option }

    /// The closed set of typed next actions (acceptance 3) — one constructor per named action in the
    /// issue.
    type NextAction =
        | DispatchCritic
        | ResumeImplementer of reason: string
        | ResumeSameCritic of reason: string
        | AwaitChecks
        | RequestHostAcceptance
        | EnterRepairPhase of RepairPhaseReceipt
        | Accept of AcceptedReceipt
        | Park of reason: string

    /// One inspected verdict: the state, the sole legal next action, and the freshness/idempotency pair
    /// (acceptance 9) a caller must present unchanged to `advance`.
    type Verdict =
        { State: State
          NextAction: NextAction
          FreshnessToken: string
          ActionKey: string }

    /// Hash the complete binding the verdict is bound to. Any field in `Binding` changing — including
    /// `HeadSha` — changes this token and invalidates a prior verdict (acceptance 4/9).
    val freshnessToken: Binding -> string

    /// Inspect live facts into exactly one typed state and next action, or a fail-closed list of
    /// reasons (acceptance 2). Never returns a permissive/absent-review state for unreadable or
    /// contradictory facts. Fails closed before any state classification when the critic identity
    /// equals the implementer identity (acceptance 5).
    val inspect: Binding -> Facts -> Result<Verdict, string list>

    /// Confirm that a caller is still advancing the exact verdict it inspected (acceptance 9). A
    /// changed binding or facts — including a new head SHA — invalidates the token/key pair and this
    /// returns `Error`; a stale replay can never dispatch a duplicate critic, mint a second repair
    /// phase, or accept the wrong head.
    val advance: freshnessToken: string -> actionKey: string -> Binding -> Facts -> Result<Verdict, string list>
