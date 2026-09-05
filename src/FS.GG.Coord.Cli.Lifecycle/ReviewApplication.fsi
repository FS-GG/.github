namespace FS.GG.Coord.Cli

/// JSON snapshot boundary for the pure resumable review/repair protocol (.github#2175), mirroring
/// `DeliveryApplication`'s pure snapshot-JSON contract exactly.
///
/// This module DECIDES NOTHING. `FS.GG.Coord.Review.inspect` owns the protocol; everything here is
/// the boundary around it — parsing one snapshot document, and rendering one verdict as JSON or
/// text. A state or action word that appears in this command's output is a projection of a
/// `Review.State` or `Review.NextAction` case and never a judgement made at this layer.
///
/// THE WIRE VOCABULARY IS READ IN REVERSE, NOT RE-AUTHORED. The seven `checks` words
/// (`green`/`conflicted`/`pending`/`red`/`unknown`/`merged`/`closed`) are exactly what
/// `Landable.name` renders; that function is forward-only (`PrState -> string`), so parsing them
/// back is this boundary's own job. The consequence a caller can rely on is round-tripping: a check
/// word supplied in the snapshot is spelled identically in the verdict that comes back.
///
/// ADDITIVE FIELDS STAY ADDITIVE. `criticSuccessionGranted` (.github#2417) and
/// `repairAssertionGranted` (.github#2549) are read from the same `facts` object but are OPTIONAL:
/// a producer that predates either field, or that has no grant to report, omits the key and parses
/// exactly as it always did. A present-but-malformed grant is still an error and never degrades to
/// "no grant was offered" — those two lead to different next actions that look identical in the
/// refusing direction.
module ReviewApplication =

    type AnsweredDecisionKey =
        { Subject: string
          DecisionId: int64
          DecisionUrl: string
          DecisionBodySha256: string
          HeadSha: string
          Critic: string
          Kind: string
          Round: int
          Verdict: string }

    type ReviewHostGrant =
        { Decision: AnsweredDecisionKey
          GrantedBy: string
          Provenance: string
          HostGrantDigest: string }

    type ReviewHostGrantComment =
        { Id: int64
          Url: string
          Body: string
          Author: string
          CreatedAt: System.DateTimeOffset
          UpdatedAt: System.DateTimeOffset }

    [<Literal>]
    val ReviewHostGrantMarker: string = "<!-- fsgg:review-host-grant/v1 -->"

    [<Literal>]
    val ReviewHostGrantProvenance: string = "env-minted/v1"

    val sha256Utf8: value: string -> string
    val createReviewHostGrant: decision: AnsweredDecisionKey -> grantedBy: string -> ReviewHostGrant
    val encodeReviewHostGrant: grant: ReviewHostGrant -> string
    val tryDecodeReviewHostGrant: body: string -> Result<ReviewHostGrant option, string>
    val reviewHostGrantsFromComments: expectedAuthor: string -> comments: ReviewHostGrantComment list -> ReviewHostGrant list

    type RepairAssertionPurpose =
        | Confirmation
        | RepairPhaseEntry

    type RepairAssertionAuthority =
        { Purpose: RepairAssertionPurpose
          HostGrantDigest: string
          PredecessorProvenance: string
          Receipt: FS.GG.Coord.Review.RepairAssertionReceipt }

    [<Literal>]
    val RepairAssertionMarker: string = "<!-- fsgg:repair-assertion/v1 -->"

    /// Encode the one canonical append-only accountable same-head repair assertion.
    val encodeRepairAssertion:
        subject: string -> RepairAssertionAuthority -> string

    /// Decode one anchored assertion marker. Non-marker comments are ignored; malformed markers fail.
    val tryDecodeRepairAssertion:
        body: string -> Result<(string * RepairAssertionAuthority) option, string>

    /// Read exactly zero or one assertion for this PR subject; malformed, wrong-subject, or duplicate
    /// authority fails closed instead of choosing a latest comment.
    val repairAssertionFromComments:
        expectedSubject: string ->
        comments: FS.GG.Coord.Driver.ReviewComment list ->
        Result<RepairAssertionAuthority option, string list>

    /// Independently parsed exact-subject facts for the monotone live grant-set adapter. Malformed,
    /// unrelated, and duplicate physical projections are non-authorizing noise.
    val repairAssertionsFromComments:
        expectedSubject: string ->
        comments: FS.GG.Coord.Driver.ReviewComment list ->
        RepairAssertionAuthority list

    /// Render one review-protocol verdict from a binding and facts the caller already holds — the
    /// LIVE path's entry point, used by `Client.review`'s `review <ref> --pr N`.
    ///
    /// THIS 3-ARGUMENT SHAPE IS A FIXED CONTRACT and is depended on positionally, as a tail
    /// expression whose type must be `int`. It does not grow parameters as the protocol gains
    /// optional grants: any grant this overload cannot express is passed as `None`, so a live
    /// caller never has to be re-spelled when a new additive field appears. The `--snapshot` path,
    /// which can parse grants, reaches the full form through `run` instead.
    ///
    /// Returns `ExitCode.Green` for a verdict and `ExitCode.NoVerdict` when `Review.inspect`
    /// refuses. Under `--json` a refusal is a `fsgg.coord.review/1` object with
    /// `verdict: "noVerdict"` and its reasons; under `--text` the reasons go to STDERR, one
    /// `UNDETERMINED — ...` line each, so a shell capturing stdout gets nothing rather than a
    /// sentence it might mistake for an answer.
    val render: Options.Options -> FS.GG.Coord.Review.Binding -> FS.GG.Coord.Review.Facts -> int

    /// Live projection including the durable review-wait ledger parsed from PR comments.
    val renderWithWait:
        Options.Options -> FS.GG.Coord.Review.Binding -> FS.GG.Coord.Review.Facts -> FS.GG.Coord.ReviewWait.State -> int

    /// Live projection with both durable wait and accountable repair-assertion comment authority.
    val renderWithWaitAndRepairAssertion:
        Options.Options ->
        FS.GG.Coord.Review.Binding ->
        FS.GG.Coord.Review.Facts ->
        FS.GG.Coord.Review.RepairAssertionReceipt option ->
        FS.GG.Coord.ReviewWait.State ->
        int

    /// Live projection with durable predecessor-escalation context for exact repair-purpose guidance.
    val renderLiveWithWaitAndRepairAssertion:
        Options.Options ->
        FS.GG.Coord.Review.Binding ->
        FS.GG.Coord.Review.Facts ->
        FS.GG.Coord.Review.RepairAssertionReceipt option ->
        FS.GG.Coord.ReviewWait.State ->
        repairPhaseEntryExpected: bool ->
        int

    /// Run `review --snapshot FILE` (or read the snapshot from stdin), printing one verdict.
    ///
    /// REFUSES AN EMPTY SNAPSHOT EXPLICITLY rather than parsing it into a default: an empty document
    /// would otherwise decode to a plausible-looking binding and the command would infer protocol
    /// state from nothing at all. That is the single most dangerous thing this boundary could do —
    /// the whole point of the typed protocol is that a state is derived from evidence — so an empty
    /// or whitespace-only input is `ExitCode.Error` with a message saying it refused to infer.
    ///
    /// EVERY REQUIRED FIELD IS REQUIRED, and a missing, wrongly typed, or empty one is an error
    /// naming that field. Strings must be non-empty; `pr` and `round` must be 32-bit integers;
    /// comment `id` must be a 64-bit integer, because GitHub comment ids exceed 32 bits and
    /// truncating one would silently mis-identify a review comment.
    ///
    /// A MALFORMED SNAPSHOT IS `ExitCode.Error`, NOT `NoVerdict`. The two are deliberately different:
    /// `NoVerdict` means the protocol itself could not decide on well-formed evidence, while an
    /// error here means the caller's document was unreadable. Collapsing them would let a typo in a
    /// snapshot read as a genuine protocol ambiguity.
    ///
    /// The full verdict payload — state, round, reason, errors, action, any repair-phase,
    /// critic-succession or accepted receipt, the retired chains, the freshness token and the action
    /// key — is serialized on the JSON path. `retiredChains` is present on EVERY verdict, empty
    /// where nothing was retired, so a consumer reads one stable shape rather than a key that
    /// appears only in the recovery case (.github#2527).
    val run: Options.Options -> int
