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

    /// The accountable, out-of-band grant that recovers a chain whose critic despawned mid-round
    /// (.github#2417) — the same "external fact the pure engine cannot observe itself" pattern as
    /// `RepairPhaseReceipt` (clarifications DEC-002): never inferred from silence, only ever consumed
    /// when a caller supplies it. `GrantedBy` is the accountable identity (typically the host) attesting
    /// the original critic is unavailable; `SuccessorCriticIdentity` is the fresh critic who performs a
    /// genuinely new, full review of the current head rather than a "confirmation" — so the property the
    /// same-critic rule protects is preserved either by the same critic confirming or by the chain being
    /// honestly restarted, never by a stranger silently continuing it.
    type CriticSuccessionReceipt =
        { OriginalCriticIdentity: string
          SuccessorCriticIdentity: string
          GrantedBy: string
          Reason: string
          CandidateHeadSha: string }

    /// Facts read live by the caller — PR comments, check state, and the two facts this pure engine
    /// cannot observe itself (clarifications DEC-002): whether a fresh repair phase has already been
    /// granted, and whether a repair route (fresh critic/worker capacity) is available at all.
    ///
    /// A critic-succession grant (.github#2417) is deliberately NOT a third field here: `Facts` is built
    /// as a record literal at call sites this module does not own (most importantly the live
    /// `review <ref> --pr N` path in `Client.fs`), and a required field would force every one of them to
    /// name it. `inspect`/`advance` take it as their own explicit parameter instead.
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
    /// issue. `EnterCriticSuccession` (.github#2417) is returned in place of `ResumeSameCritic` only when
    /// `inspect`/`advance`'s `successionGranted` parameter carries a receipt that validates against the
    /// exact stuck critic and head; absent a valid receipt, `ResumeSameCritic` is unconditionally the
    /// answer.
    type NextAction =
        | DispatchCritic
        | ResumeImplementer of reason: string
        | ResumeSameCritic of reason: string
        | AwaitChecks
        | RequestHostAcceptance
        | EnterRepairPhase of RepairPhaseReceipt
        | EnterCriticSuccession of CriticSuccessionReceipt
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
    /// equals the implementer identity (acceptance 5). `successionGranted` (.github#2417) is the
    /// explicit, out-of-band critic-succession grant, if any; every caller that never grants one passes
    /// `None` and observes byte-for-byte the same behavior as before this parameter existed.
    val inspect: Binding -> Facts -> successionGranted: CriticSuccessionReceipt option -> Result<Verdict, string list>

    /// Confirm that a caller is still advancing the exact verdict it inspected (acceptance 9). A
    /// changed binding, facts, or succession grant — including a new head SHA — invalidates the
    /// token/key pair and this returns `Error`; a stale replay can never dispatch a duplicate critic,
    /// mint a second repair phase or succession, or accept the wrong head.
    val advance:
        freshnessToken: string ->
        actionKey: string ->
        Binding ->
        Facts ->
        successionGranted: CriticSuccessionReceipt option ->
            Result<Verdict, string list>
