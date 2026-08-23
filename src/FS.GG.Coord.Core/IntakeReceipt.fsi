namespace FS.GG.Coord

/// The idempotency boundary for #2134. A receipt can only be reused by the exact draft identity and
/// owner/repository binding that created it; a malformed or stale receipt is never evidence of absence.
module IntakeReceipt =
    type Receipt = { DraftId: string; Owner: string; Repository: string; IssueNumber: int; DraftDigest: string }
    val digest: Intake.Draft -> string
    /// Current canonical digest plus the bounded pre-#2835 lowercase-severity representation.
    val compatibleDigests: Intake.Draft -> string list

    /// Durable provenance embedded in the created issue. A title is human text and cannot identify the
    /// result of a crashed transaction; this content-bound marker can.
    val marker: Intake.Draft -> string

    val validate: Intake.Draft -> Receipt -> Result<Receipt, string>
