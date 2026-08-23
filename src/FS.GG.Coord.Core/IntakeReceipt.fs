namespace FS.GG.Coord

module IntakeReceipt =
    open System.Security.Cryptography
    open System.Text

    type Receipt = { DraftId: string; Owner: string; Repository: string; IssueNumber: int; DraftDigest: string }

    let private digestWithSeverity severity (draft: Intake.Draft) =
        let parts =
            [ draft.Schema; draft.Id; draft.Owner; draft.Repository; draft.Title; draft.Observed; draft.RootCause
              draft.Acceptance; draft.Verification; String.concat "\u001f" draft.Paths; draft.Class; draft.Status
              string draft.Disposition; string draft.Phase; string severity; string draft.BlockedBy
              string draft.BlockedOn; string draft.BacklogReason; string draft.JudgementQuestion ]
        SHA256.HashData(Encoding.UTF8.GetBytes(String.concat "\u001e" parts)) |> System.Convert.ToHexString |> fun value -> value.ToLowerInvariant()

    let digest (draft: Intake.Draft) = digestWithSeverity draft.Severity draft

    let compatibleDigests (draft: Intake.Draft) =
        [ yield digest draft
          match draft.Severity with
          | Some value ->
              let legacy = digestWithSeverity (Some(value.ToLowerInvariant())) draft
              if legacy <> digest draft then yield legacy
          | None -> () ]

    let marker (draft: Intake.Draft) =
        $"<!-- fsgg:intake:v1 id=%s{draft.Id} digest=%s{digest draft} -->"

    let validate (draft: Intake.Draft) (receipt: Receipt) =
        if receipt.IssueNumber <= 0 then Error "receipt issueNumber must be positive"
        elif receipt.DraftId <> draft.Id then Error "receipt draft id does not match this draft"
        elif receipt.Owner <> draft.Owner || receipt.Repository <> draft.Repository then Error "receipt owner/repository does not match this draft"
        elif compatibleDigests draft |> List.contains receipt.DraftDigest |> not then Error "receipt content digest does not match this draft"
        else Ok receipt
