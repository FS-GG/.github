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

    type ReviewKind = Initial | Confirmation | Acceptance
    type ReviewVerdict = Pass | ChangesRequired | Accepted

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
          Timestamp: string
          Digest: string }

    type RouteReadClassification =
        | StructuredOnly
        | LegacyOnly
        | Equivalent
        | Divergent

    val routeDigest: RouteRecord -> string
    val reviewDigest: ReviewRecord -> string
    val validateRouteLedger: expectedSubject: string -> RouteRecord list -> Result<RouteRecord, string list>
    val validateReviewLedger: expectedSubject: string -> ReviewRecord list -> Result<ReviewRecord list, string list>
    val classifyRoute: DeliveryRoute.Receipt option -> RouteRecord option -> RouteReadClassification
    val routeClassificationName: RouteReadClassification -> string
    val toLegacyReceipt: RouteRecord -> DeliveryRoute.Receipt
