namespace FS.GG.Coord

module IntakeReceipt =
    type Receipt = { DraftId: string; Owner: string; Repository: string; IssueNumber: int; DraftDigest: string }
    val digest: Intake.Draft -> string
    val validate: Intake.Draft -> Receipt -> Result<Receipt, string>
