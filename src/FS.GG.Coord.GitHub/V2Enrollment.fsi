namespace FS.GG.Coord.GitHub

/// Pure, offline planner for a batched Coordination V2 programme enrollment successor.
///
/// The planner appends rows to the predecessor's enrollment payload by a raw-byte splice: the
/// predecessor bytes before its closing `]}` are copied verbatim and never re-serialized, so the
/// prefix is exact by construction. It reuses the installed reader's own seams rather than a second
/// decoder: `V2Projection.decodeBinding`, `authenticateProgrammeEnrollment`, `prepareProgrammeBinding`
/// and `V2ProjectionSource.programmePlanBlobGrammar`.
///
/// It performs no IO, no native read and no mutation, and grants no authority. Native facts are a
/// read-only input supplied by a separate step. Every refusal is a closed typed code; no refusal,
/// and no value in this module, carries payload, nonce, native-identity or input text.
module V2Enrollment =

    /// The installed reader's payload row ceiling (`payloadRows` refuses more than 100 rows).
    /// This planner mirrors it; it does not lift it.
    val rowCap: int

    /// Closed refusal set. `MalformedInput` covers undecodable or non-closed request/facts/pair
    /// input, which carries no further detail by design.
    type Refusal =
        | DuplicateIssue
        | AlreadyEnrolled
        | IsPullRequest
        | NodeIdMismatch
        | NotProjectMember
        | RepositoryOutsideBinding
        /// The private pair does not authenticate against the protected public commitment.
        | PredecessorCommitmentMismatch
        | PrefixNotPreserved
        | PlanNotAdmitted
        /// Offline variant: decided from a supplied plan-existence fact, never a native read.
        | PlanBlobAbsentOnMain
        | RowCapExceeded
        | ConflictingHumanValue
        | PartialVisibility
        /// Raised by file-writing wrappers, including a `CreateNew` race; the pure planner never produces it.
        | OutputExists
        | MalformedInput
        /// A supplied input file could not be read within its custody and size bounds.
        | InputUnreadable
        /// An output file or its durability barrier could not be created or retained.
        | CustodyUnavailable
        /// A read of the protected public blob through git failed (distinct from a mismatch).
        | GitReadFailed
        /// A read-only native GET failed or returned an unusable body.
        | NativeReadFailed
        /// Supplied successor template or payload bytes are not exactly what the planner re-derives
        /// from the request, the predecessor and the native facts.
        | SuccessorNotRederived

    /// The stable printable code, exactly the case name.
    val refusalCode: refusal: Refusal -> string

    type PlanSpec =
        /// The single hard-coded programme issue the installed reader admits.
        | ProgrammeIssue3008
        /// An exact `docs/` blob on `main` of a bound repository (rendered as a main blob URL).
        | ProtectedDoc of repository: string * path: string

    type Classification =
        | Outcome
        | Delivery

    type RequestIssue =
        { Ref: string
          Slice: string
          Plan: PlanSpec
          Classification: Classification }

    type Request =
        { Cohort: string
          PredecessorPrivatePath: string
          PredecessorMergeSha: string
          Issues: RequestIssue list }

    /// Strict closed request: schema `fsgg.board-v2-successor-request/1`, bounded UTF-8 JSON,
    /// depth 16, no duplicate or unknown keys.
    val decodeRequest: json: string -> Result<Request, Refusal>

    type NativeKind =
        | NativeIssue
        | NativePullRequest

    type NativeIssueFact =
        { Ref: string
          NodeId: string
          Kind: NativeKind
          Repository: string
          ProjectMember: bool
          /// False when the fact was read with partial visibility.
          Complete: bool
          /// The human Roadmap value already on the board item, when one exists.
          HumanRoadmap: string option }

    type PlanBlobFact =
        { Repository: string
          Path: string
          OnMain: bool }

    type NativeFacts =
        { ProjectId: string
          Issues: NativeIssueFact list
          PlanBlobs: PlanBlobFact list }

    /// Strict closed read-only facts: schema `fsgg.board-v2-native-facts/1`.
    val decodeNativeFacts: json: string -> Result<NativeFacts, Refusal>

    type PlannedRow =
        { Issue: string
          NodeId: string
          Roadmap: string
          Slice: string
          Classification: Classification }

    /// Generic printing (`%A`, `ToString`) is redacted: the plan carries private custody bytes.
    [<StructuredFormatDisplay("{Display}")>]
    type SuccessorPlan =
        { /// Successor binding template: the predecessor private binding without its private
          /// enrollment, with the appended selected issues and the predecessor merge as the
          /// provisional population revision. Private custody; not a public document.
          TemplateJson: string
          /// Successor payload bytes: predecessor prefix, spliced rows, closing `]}`.
          Payload: byte array
          Appended: PlannedRow list
          PredecessorRows: int
          TotalRows: int }
        /// Fixed-shape redacted text: row counts only, never the template or payload.
        member Display: string
        override ToString: unit -> string

    /// Replaceable stages, for fixture controls only. Production composition uses `plan`.
    type Seams =
        {
            /// Installed-reader plan-URL grammar. Production: `V2ProjectionSource.programmePlanBlobGrammar`.
            Grammar: Set<string> -> string -> Result<string * string * string, string>
            /// Splice of the row fragment into the predecessor payload. Production: raw-byte splice.
            Splice: byte array -> byte array -> byte array
        }

    val defaultSeams: Seams

    /// Plan with explicit stages. Fixture seam for causal controls; `plan` is the production entry.
    val planWith:
        seams: Seams ->
        predecessorPrivateJson: string ->
        predecessorPublicJson: string ->
        request: Request ->
        facts: NativeFacts ->
            Result<SuccessorPlan, Refusal>

    /// Authenticate the predecessor pair, check every request row against the facts and the
    /// binding, and build the successor payload by raw-byte splice. The first failing check
    /// returns its refusal; nothing is constructed before the plan grammar is checked.
    val plan:
        predecessorPrivateJson: string ->
        predecessorPublicJson: string ->
        request: Request ->
        facts: NativeFacts ->
            Result<SuccessorPlan, Refusal>

    /// One independent finding. `Issue` is the request issue reference it concerns, or `None` when it
    /// concerns the request as a whole.
    type Finding = { Refusal: Refusal; Issue: string option }

    type Analysis =
        { PredecessorRows: int
          PredecessorPayloadBytes: int
          /// Every independent finding in the planner's own check order; the planner refuses with the first.
          Findings: Finding list }

    /// All independent findings rather than the first, over the same single implementation `plan` uses.
    /// Error only when the predecessor pair does not authenticate or cannot be decoded.
    val analyzeWith:
        seams: Seams ->
        predecessorPrivateJson: string ->
        predecessorPublicJson: string ->
        request: Request ->
        facts: NativeFacts ->
            Result<Analysis, Refusal>

    val analyze:
        predecessorPrivateJson: string ->
        predecessorPublicJson: string ->
        request: Request ->
        facts: NativeFacts ->
            Result<Analysis, Refusal>

    /// Re-derive the successor from the request, predecessor and facts and require the supplied template
    /// and payload to be exactly that. Specific refusals first (`PrefixNotPreserved`, `PlanNotAdmitted`,
    /// `NodeIdMismatch` read from the supplied bytes), then `SuccessorNotRederived` for any other difference.
    /// A hand-edited payload that the installed constructor would accept is refused here.
    val verifyPreparedWith:
        seams: Seams ->
        predecessorPrivateJson: string ->
        predecessorPublicJson: string ->
        request: Request ->
        facts: NativeFacts ->
        template: string ->
        payload: byte array ->
            Result<unit, Refusal>

    val verifyPrepared:
        predecessorPrivateJson: string ->
        predecessorPublicJson: string ->
        request: Request ->
        facts: NativeFacts ->
        template: string ->
        payload: byte array ->
            Result<unit, Refusal>

    type PairVerdict =
        { /// Predecessor bytes before its closing `]}` are a byte-exact prefix of the successor payload.
          PrefixPreserved: bool
          /// Payload header bytes and every successor-binding field except the appended selected
          /// issues and population revision are unchanged.
          HeaderPreserved: bool
          /// The successor public commitment equals the commitment recomputed from the successor
          /// private envelope through the installed authenticator.
          RecomputedCommitment: bool
          PredecessorRows: int
          AppendedRows: int
          TotalRows: int }

    /// Independent verification of a prepared pair against its predecessor. Never throws; any
    /// undecodable input yields all-false and zero counts.
    val verifyPair:
        predecessorPrivateJson: string -> successorPrivateJson: string -> successorPublicJson: string -> PairVerdict
