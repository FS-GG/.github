namespace FS.GG.Coord

/// The idempotency boundary for #2134.  A receipt can only be reused by the exact draft identity and
/// owner/repository binding that created it; a malformed or stale receipt is never evidence of absence.
module IntakeReceipt =
    open System.Security.Cryptography
    open System.Text

    type Receipt = { DraftId: string; Owner: string; Repository: string; IssueNumber: int; DraftDigest: string }

    let digest (draft: Intake.Draft) =
        let parts =
            [ draft.Schema; draft.Id; draft.Owner; draft.Repository; draft.Title; draft.Observed; draft.RootCause
              draft.Acceptance; draft.Verification; String.concat "\u001f" draft.Paths; draft.Class; draft.Status
              string draft.Disposition; string draft.Phase; string draft.Severity; string draft.BlockedBy
              string draft.BlockedOn; string draft.BacklogReason; string draft.JudgementQuestion ]
        SHA256.HashData(Encoding.UTF8.GetBytes(String.concat "\u001e" parts)) |> System.Convert.ToHexString |> fun value -> value.ToLowerInvariant()

    /// Durable provenance embedded in the created issue.  A title is human text and cannot identify the
    /// result of a crashed transaction; this content-bound marker can.
    let marker (draft: Intake.Draft) =
        $"<!-- fsgg:intake:v1 id=%s{draft.Id} digest=%s{digest draft} -->"

    let validate (draft: Intake.Draft) (receipt: Receipt) =
        if receipt.IssueNumber <= 0 then Error "receipt issueNumber must be positive"
        elif receipt.DraftId <> draft.Id then Error "receipt draft id does not match this draft"
        elif receipt.Owner <> draft.Owner || receipt.Repository <> draft.Repository then Error "receipt owner/repository does not match this draft"
        elif receipt.DraftDigest <> digest draft then Error "receipt content digest does not match this draft"
        else Ok receipt
