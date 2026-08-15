namespace FS.GG.Coord

/// Content-addressed, append-only machine decisions. Narrative bodies are deliberately absent.
module StructuredDecision =
    [<Literal>]
    val RouteSchema: string = "fsgg.coord.route-decision/v2"

    [<Literal>]
    val ReviewSchema: string = "fsgg.coord.review-decision/v2"

    [<Literal>]
    val PolicyVersion: string = "structured-decisions/1"

    type RouteRecord =
        { Schema: string
          Subject: string
          Revision: int
          PreviousDigest: string option
          Scope: string list
          Dependencies: string list
          TouchSet: string list
          PolicyVersion: string
          Route: DeliveryRoute.Route option
          Agent: string
          Timestamp: string
          ReasonCodes: string list
          Rationale: string
          SddWorkId: string option
          SpecHome: string option
          RequiredGates: string list
          Digest: string }

    type ReviewKind = Initial | Confirmation | Escalation | RepairPhase | Acceptance
    type ReviewVerdict = Pass | ChangesRequired | Accepted

    /// The host-granted transfer of a live review generation's critic seat, written INTO the record the
    /// successor appends (.github#2662). `.github#2417` gave the DECISION layer a typed succession; the
    /// ledger never learned one, so a granted successor could review and then had no honest record shape
    /// to write. This is that shape.
    ///
    /// It is carried in the record rather than passed to the validator because `Driver`'s ledger reader is
    /// the sole gate in front of the host's acceptance-time and `landable`-time consumers, and those
    /// consumers see only the comment thread. A succession that is not legible in the records themselves
    /// is written and then refused later by the very path that has to accept it.
    ///
    /// `OriginalCritic` is the identity handing over — the generation's critic at the moment of the grant,
    /// which is not necessarily the record that opened the generation, because a successor can itself be
    /// succeeded. `GrantedBy` is the accountable granter, typically the host. `GrantUrl` locates the grant
    /// so a human or auditing agent can read it: the validator requires it to be PRESENT, and deliberately
    /// never resolves it — a pure validator does not acquire a network dependency, and no reader should
    /// infer an authenticity that was never checked.
    type SuccessionGrant =
        { OriginalCritic: string
          GrantedBy: string
          GrantUrl: string }

    type ReviewRecord =
        { Schema: string
          Subject: string
          Revision: int
          PreviousDigest: string option
          HeadSha: string
          Critic: string
          Verdict: ReviewVerdict
          AcceptedExceptions: string list
          RouteApplicability: string
          RouteEvidence: string list
          PolicyVersion: string
          Kind: ReviewKind
          Round: int
          InitialReview: string option
          PrecedingReview: string option
          DiffAuditRequired: bool
          DiffAuditReceipts: string list
          /// Absent on every ordinary record. Present only where a granted successor critic takes over a
          /// live generation, and then it contributes to `reviewDigest` — so an engine that predates the
          /// field fails CLOSED on a succession record (digest mismatch) instead of silently dropping the
          /// grant and applying the unwidened continuity rule to a record that no longer satisfies it.
          Succession: SuccessionGrant option
          Timestamp: string
          Digest: string }

    /// A `critic:` value that is the bare, undifferentiated agent-type string every critic dispatched at
    /// one route shares — `fsgg-critic-normal`, or any future `fsgg-critic-<route>` — rather than a
    /// minted, distinguishing identity (.github#2451). Two markers that both carry this shape can never
    /// be treated as proof of "the same critic", and neither slot of a succession grant may carry one.
    ///
    /// This module owns the predicate because it owns the record whose `critic` field it judges;
    /// `Driver.fs` calls it here rather than keeping its own copy. `Review.fs` still carries a private
    /// copy of the same rule — same rename discipline if it ever changes — because that copy's exact
    /// source lines are pinned as gate-inversion anchors by `tests/review-critic-succession-wire/run.sh`.
    val isGenericCriticIdentity: identity: string -> bool

    val routeDigest: RouteRecord -> string
    val reviewDigest: ReviewRecord -> string
    val validateRouteLedger: expectedSubject: string -> RouteRecord list -> Result<RouteRecord, string list>
    val validateReviewLedger: expectedSubject: string -> ReviewRecord list -> Result<ReviewRecord list, string list>
    val toEffectiveRoute: RouteRecord -> DeliveryRoute.Receipt
