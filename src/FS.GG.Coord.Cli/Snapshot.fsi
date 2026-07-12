namespace FS.GG.Coord.Cli

/// The wire contract between the bash client and the typed engine.
///
/// SHADOW MODE MAY NOT TOUCH THE GRAPHQL BUDGET. The primary limit is 5,000 pt/hr shared by the WHOLE
/// fleet, and five workers looping `take` already drained it in ~15 minutes once (#418). An engine
/// that went and fetched the board for itself would double the spend on the budget whose exhaustion is
/// the loudest open defect in this domain — and it would be comparing two engines against two
/// DIFFERENT boards, which is not a comparison at all.
///
/// So the engine reads NOTHING. The bash client has already paid for the board scan and the claim
/// markers by the time it decides; it serialises THAT state into this snapshot, and the engine decides
/// from it. Both engines see byte-identical input, so a divergence is a difference in the RULE rather
/// than in what the two of them happened to observe.
///
/// THE ONE COST, STATED PLAINLY. Bash short-circuits a CLOSED or BLOCKED candidate without ever
/// reading its body; the typed core checks the touch-set BEFORE the blockers, so it needs that body to
/// reach the same question. Handing it `Undeclared` instead would manufacture a divergence that does
/// not exist in either engine. So a shadowed run reads the bodies bash would have skipped — a bounded
/// number of REST requests (one per closed/blocked candidate), on the 5,000-*requests*/hr budget that
/// is NOT the one that dies under fan-out. Zero GraphQL points, and only when the shadow is switched
/// on. That the two check-orders have different read costs is itself a finding, and it belongs to the
/// Phase 3 flip, not to this wire format.
///
/// THE CODEC MAY NOT FAIL OPEN. Every field is required unless its absence is itself a modelled fact,
/// and a malformed snapshot is an ERROR, never a default. A defaulted field would silently change the
/// verdict — and the shadow would then report agreement, or disagreement, that was manufactured here
/// rather than found in the domain. That is epic #266's shape, hiding in a JSON reader.
module Snapshot =

    open FS.GG.Coord
    open FS.GG.Coord.Types

    /// Where the snapshot is malformed, and how. Carries a JSON path so a fleet-wide shadow failure is
    /// diagnosable from the log alone.
    type Error =
        { Path: string
          Message: string }

    type Candidate =
        { Item: Item

          /// The touch-set the BASH client parsed out of the very same body.
          ///
          /// The engine decides from ITS OWN parse (`TouchSet.parse` of the raw body) — that is the
          /// point, because the touch-set grammar is its own family of incidents (#273, #277, #435,
          /// #496) and a shadow that re-used bash's parse would never exercise it. This field is
          /// carried ONLY so that a divergence can show both parses side by side instead of leaving a
          /// reader to guess which layer disagreed.
          BashPaths: string list option }

    type Request =
        { AllowBacklog: bool

          /// `batch -n`. `None` is unlimited.
          Limit: int option

          /// Touch-sets already spoken for by live claims, as the bash client observed them.
          ///
          /// These arrive PRE-PARSED — bash extracted them from the claimed items' bodies before it
          /// called us — so the shadow does not currently compare the two body-parsers on the
          /// reservation side, only on the candidate side. `TouchSet.classify` still runs over every
          /// token here, so #273's unmatchable-token rule IS exercised. The gap is the EXTRACTION,
          /// and it is named rather than papered over.
          InFlight: Batch.Reservation list

          Candidates: Candidate list }

    /// Read a snapshot. Returns EVERY error, not the first — a shadow that has to be fixed one field
    /// per round-trip across six repos does not get fixed.
    val parse: json: string -> Result<Request, Error list>

    /// Render the engine's decision as the response document.
    ///
    /// `candidates` is threaded through only to echo `BashPaths` back beside the engine's own parse.
    val render: candidates: Candidate list -> decision: Verdict<Batch.BatchResult> -> string
