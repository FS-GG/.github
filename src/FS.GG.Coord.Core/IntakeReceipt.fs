namespace FS.GG.Coord

/// The idempotency boundary for #2134.  A receipt can only be reused by the exact draft identity and
/// owner/repository binding that created it; a malformed or stale receipt is never evidence of absence.
module IntakeReceipt =
    type Receipt = { DraftId: string; Owner: string; Repository: string; IssueNumber: int }

    let validate (draft: Intake.Draft) (receipt: Receipt) =
        if receipt.IssueNumber <= 0 then Error "receipt issueNumber must be positive"
        elif receipt.DraftId <> draft.Id then Error "receipt draft id does not match this draft"
        elif receipt.Owner <> draft.Owner || receipt.Repository <> draft.Repository then Error "receipt owner/repository does not match this draft"
        else Ok receipt
