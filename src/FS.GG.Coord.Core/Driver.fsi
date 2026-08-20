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
        { MarkerValid: bool; Subject: string option; ClaimGeneration: string option; BaseSha: string option
          CriticIdentity: string option; HeadSha: string option
          Rounds: int list; RepairPhase: bool; ChecksGreen: bool; HostAccepted: bool
          RuntimeRouteEvidence: RuntimeRouteEvidence option
          DiffAuditRequired: bool; DiffAuditHead: string option }

    type ReviewComment =
        { Id: int64; Url: string; Body: string }

    val decodeStructuredReview: raw: string -> Result<StructuredDecision.ReviewRecord, string>
    val encodeStructuredReview: record: StructuredDecision.ReviewRecord -> string

    /// Structural facts a caller reads off the SAME marker classification `parseReviewComments` already
    /// computes, without waiting for the whole chain to validate — additive to the public surface, not a
    /// second marker parser (.github#2175 acceptance 11; `FS.GG.Coord.Core.Review` is the consumer).
    type ReviewPhaseFacts =
        { /// Invalid or tampered structured evidence. Non-empty always fails closed.
          StructuredErrors: string list
          /// How many comments canonically carry the initial-review marker. `InitialPresent` is
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
          /// The URL of the comment `LatestVerdict` and `LatestReviewedHeadSha` were read from — the
          /// latest confirmation's when one exists, else the single initial-review comment's
          /// (.github#2549). It exists so an out-of-band grant can be bound to the EXACT review it
          /// answers, rather than to "some review", which would let a grant left over from an earlier
          /// round pass as one answering the current one.
          LatestReviewUrl: string option
          EscalationPresent: bool
          RepairPhasePresent: bool
          /// How many comments canonically carry the host-acceptance marker; see `InitialCount`.
          AcceptanceCount: int
          AcceptancePresent: bool }

    /// Classify a PR's review comments into the structural facts `Review.inspect` needs to select a
    /// typed review-protocol state — reusing the same leading-marker-block/quoting-aware detection
    /// `parseReviewComments` uses, never re-scanning comment bodies with a second grammar.
    val reviewPhaseFacts: comments: ReviewComment list -> ReviewPhaseFacts

    /// One review chain a host-acceptance marker accepted at a head the pull request has since moved off
    /// (.github#2527), addressed by the initial-review comment URL every confirmation and the acceptance
    /// itself already carry as `initial-review:`.
    type ChainRetirement =
        { InitialReviewUrl: string
          InitialReviewCommentId: int64
          AcceptedHead: string
          AcceptanceCommentId: int64 }

    /// A read-time partition of one pull request's review comments (.github#2527). `Live` and `Retired`
    /// are new lists over the SAME comment values — nothing is mutated, reordered, edited, or quoted
    /// inert, so a retired chain stays exactly as its critic posted it. `Diagnostics` names the near
    /// misses that did NOT retire anything, so a refusal can say which condition failed.
    type LiveReviewComments =
        { Live: ReviewComment list
          Retired: ChainRetirement list
          Diagnostics: string list
          StructuredSubject: string option
          StructuredErrors: string list }

    /// Partition review comments into the chain that binds `currentHead` and the chains a host acceptance
    /// already settled at a head the pull request has moved off (.github#2527).
    ///
    /// A chain is retired when, and only when, a host-acceptance marker names its initial-review comment
    /// URL AND carries an `accepted-head` other than `currentHead` — both read from that marker's own
    /// required fields through the same classification `reviewPhaseFacts` and `parseReviewComments` use,
    /// never a second marker parser (.github#2175 acceptance 11).
    ///
    /// Retirement is a TIE-BREAKER and fires only where more than one canonical initial marker is
    /// present. With a single chain the result is the input, unchanged, so no verdict this protocol could
    /// already describe can move.
    val liveReviewComments: currentHead: string -> comments: ReviewComment list -> LiveReviewComments

    /// The FACTS-FREE spelling: it supplies no live delivery facts, so it decides everything about a
    /// review chain EXCEPT whether a submitted diff-audit receipt matches the live diff, which it cannot
    /// read. Where a diff audit is required it still refuses a receipt that is absent, malformed, or not
    /// bound to one single head — every refusal that is decidable from the receipts themselves — and
    /// renders no verdict on the one question it would need the live diff to answer (.github#2694).
    ///
    /// A caller that must have that question ANSWERED, rather than merely not answered wrongly, calls
    /// `parseReviewCommentsWithFacts` AND supplies a recomputed inventory. Passing `None` there is not
    /// that: it is this same "no inventory" fact by another spelling, and every production caller on the
    /// `review` path passes exactly that (.github#2694 round-1 M1).
    ///
    /// A generation gains NO fresh-`initial` escape from having been refused: retirement applies only to
    /// an ACCEPTED generation. A `diffAuditRequired: true` generation is no longer terminal at all — it
    /// was terminal only because of the conflation this function's implementation now removes. The
    /// terminal-generation answer itself is stated where a wedged critic actually meets it, which is not
    /// a signature file: it travels with `StructuredDecision`'s "a new initial review is allowed only
    /// after host acceptance" refusal, the exact string and moment of the wedge.
    val parseReviewComments: comments: ReviewComment list -> Result<ReviewChain, string list>

    /// Parse review evidence while binding any mandatory diff audit to an independently recomputed
    /// inventory from the live PR base/head blobs.
    val parseReviewCommentsWithAudit:
        trustedAudit: SemanticDiff.Receipt -> comments: ReviewComment list -> Result<ReviewChain, string list>

    /// The FACTS-BEARING spelling, and the only one that CAN check a submitted diff-audit receipt against
    /// the diff — but only when it is actually handed an inventory.
    ///
    /// `trustedAudit = None` means NO INVENTORY WAS SUPPLIED, exactly as it does on the facts-free
    /// spelling, and no verdict is rendered about the receipts. It does NOT mean "the engine recomputed
    /// and found nothing" (.github#2694 round-1 M1). Reading it that way would be false at every
    /// production caller on the `review` path: `Review.acceptanceOutcome` derives both of this function's
    /// arguments from `Review.Facts.DiffAuditTrusted`, which is hardcoded `None` at both of its
    /// constructors — `ReviewApplication.fs` (snapshot route) and `Client.fs` (live route) — so a correct
    /// receipt would be accused of being stale on the one route a host actually lands through.
    ///
    /// A CALLER THAT GENUINELY RECOMPUTED AN EMPTY INVENTORY SPELLS IT
    /// `Some { Expected = []; Discovered = [] }`. That distinguishes "I looked and found nothing" from
    /// "I did not look" in the type rather than by inference, which is the whole of this item: an empty
    /// result and an absent read are different facts and must not share a spelling.
    val parseReviewCommentsWithFacts:
        mechanicallyRequired: bool ->
        trustedAudit: SemanticDiff.TrustedAudit option ->
        comments: ReviewComment list ->
            Result<ReviewChain, string list>

    /// Parse the structured review generation effective at the current PR head after retiring accepted
    /// generations for older heads. Facts-free: it reaches `parseReviewComments`, so its diff-audit
    /// contract is that function's (.github#2694). This is the spelling `review record` seals a host
    /// acceptance through, which is why a verdict rendered here on the ABSENCE of live facts could
    /// terminally wedge a generation.
    val parseEffectiveReviewComments:
        currentHead: string -> comments: ReviewComment list -> Result<ReviewChain, string list>

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

    /// Every problem a review chain carries — structural malformations AND the live-check liveness
    /// clause — in one list. Unchanged by .github#2549: the messages and their order are identical
    /// before and after that split, which is what keeps `Delivery.reviewProblem` and `receiptFresh`
    /// behaviourally untouched.
    val validateReviewChain: maxRounds: int -> ReviewChain -> string list

    /// The STRUCTURAL subset of `validateReviewChain`: what is wrong with the durable review evidence
    /// itself, with `"review checks are not green"` withheld (.github#2549).
    ///
    /// Both lists are derived from one shared, ordered source, so they cannot drift. This is the
    /// question `Review.acceptanceOutcome` asks, so that `MalformedEvidence` means only "a host must
    /// inspect this chain" and never "CI has not reported yet" — a conflation that sent a host to the
    /// close-and-reopen recovery on a healthy chain. An empty result asserts nothing about merge
    /// readiness, which remains `landable`'s independent verdict (.github#2360).
    val validateReviewChainStructure: maxRounds: int -> ReviewChain -> string list

    val nextAction:
        model: WaveModel ->
        activeItems: int ->
        consolidationApproved: bool ->
        housekeeping: Housekeeping ->
        workerReturns: WorkerReturn list ->
            Action
