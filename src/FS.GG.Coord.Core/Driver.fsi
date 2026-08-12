namespace FS.GG.Coord

/// Pure, fail-closed transitions for the two-wave coordination driver.
module Driver =
    open Batch

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

    /// Machine-validated evidence decision carried by the latest passing independent-review marker.
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

    /// Structural facts a caller reads off the SAME marker classification `parseReviewComments` already
    /// computes, without waiting for the whole chain to validate — additive to the public surface, not a
    /// second marker parser (.github#2175 acceptance 11; `FS.GG.Coord.Core.Review` is the consumer).
    type ReviewPhaseFacts =
        { /// How many comments canonically carry the initial-review marker. `InitialPresent` is
          /// `InitialCount > 0`; a caller that needs to distinguish "absent" from "duplicate/competing"
          /// (.github#2175 acceptance 8) reads this field rather than re-deriving it.
          InitialCount: int
          InitialPresent: bool
          InitialHeadSha: string option
          InitialVerdict: string option
          CriticIdentity: string option
          ConfirmationCount: int
          LatestVerdict: string option
          /// Populated only when `LatestVerdict = None`: every markdown-emphasised near-miss field
          /// found in the comment `LatestVerdict` would have been read from. Empty whenever
          /// `LatestVerdict` is readable, or no near miss was found (.github#2369) — this never widens
          /// what the underlying field grammar accepts, only what a refusal can explain.
          LatestVerdictNearMissHints: string list
          LatestReviewedHeadSha: string option
          EscalationPresent: bool
          RepairPhasePresent: bool
          /// How many comments canonically carry the host-acceptance marker; see `InitialCount`.
          AcceptanceCount: int
          AcceptancePresent: bool }

    /// A `critic:` value that is the bare, undifferentiated agent-type string every critic dispatched at
    /// one route shares (`fsgg-critic-normal`, or any future `fsgg-critic-<route>`) rather than a
    /// minted, distinguishing identity (.github#2451). Two markers that both carry this shape can never
    /// be treated as proof of "the same critic" — reused by both `parseReviewComments`'s same-critic
    /// continuity check and `Review.criticSuccessionValid`, so the recognition rule lives in one place.
    val isGenericCriticIdentity: identity: string -> bool

    /// Classify a PR's review comments into the structural facts `Review.inspect` needs to select a
    /// typed review-protocol state — reusing the same leading-marker-block/quoting-aware detection
    /// `parseReviewComments` uses, never re-scanning comment bodies with a second grammar.
    val reviewPhaseFacts: comments: ReviewComment list -> ReviewPhaseFacts

    val parseReviewComments: comments: ReviewComment list -> Result<ReviewChain, string list>

    /// Parse review evidence while binding any mandatory diff audit to an independently recomputed
    /// inventory from the live PR base/head blobs.
    val parseReviewCommentsWithAudit:
        trustedAudit: SemanticDiff.Receipt -> comments: ReviewComment list -> Result<ReviewChain, string list>

    val parseReviewCommentsWithFacts:
        mechanicallyRequired: bool ->
        trustedAudit: SemanticDiff.TrustedAudit option ->
        comments: ReviewComment list ->
            Result<ReviewChain, string list>

    type Receipt =
        { ObservedAt: int64
          SourceSha: string
          Complete: bool
          Review: ReviewChain option }

    val receiptFresh: now: int64 -> maxAgeSeconds: int64 -> Receipt -> bool

    type WorkerReturn =
        { ClaimLive: bool
          ReviewReady: bool
          ParkedOrDone: bool }

    /// A content-addressed result emitted by one deterministic housekeeping read/command.  Every
    /// constituent is bound to the full live fact source; a same-occupancy state change invalidates it.
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

    val observationReceiptId: kind: string -> observedAt: int64 -> sourceSha: string -> outcome: string -> string

    val contentDispositionReceiptId:
        sourceFinding: string ->
        disposition: ContentDisposition ->
        consumerPaths: string list ->
        decisionMaker: string ->
        rationale: string ->
        evidence: ContentEvidence option ->
        observedAt: int64 ->
        sourceSha: string ->
        string

    val planningReceiptFresh: now: int64 -> maxAgeSeconds: int64 -> sourceSha: string -> PlanningReceipt -> bool

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

    val validateReviewChain: maxRounds: int -> ReviewChain -> string list

    val nextAction:
        model: WaveModel ->
        activeItems: int ->
        consolidationApproved: bool ->
        housekeeping: Housekeeping ->
        workerReturns: WorkerReturn list ->
            Action
