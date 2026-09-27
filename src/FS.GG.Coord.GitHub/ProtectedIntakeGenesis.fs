namespace FS.GG.Coord.GitHub

open System
open System.IO
open System.Security.Cryptography
open System.Text
open FS.GG.Coord
open FS.GG.Coordination.GitHub
open FS.GG.Coord.GitHub.ProtectedIntakeAuthority

module ProtectedIntakeGenesis =
    [<Literal>]
    let Schema = "fsgg.github.protected-intake-genesis-plan/1"
    [<Literal>]
    let OperationId = "preinstalled-empty-genesis"

    type Plan =
        { Ref: string
          ExpectedOldObjectId: string option
          ProposedObjectId: string
          Refspec: string
          ForceWithLease: string
          Proposal: Proposal }
    type ReadbackFailure =
        | PlanDrift
        | ExpectedAbsentRefNotAbsent
        | RefReadbackIndeterminate
        | ResultingRefMismatch
        | ObjectCustodyMismatch
    type ReadbackConfirmed = private ReadbackConfirmed of commitOid: string
    type AuthorizationFailure =
        | IntakeAuthorityRefused of ProtectedIntakeAuthority.Failure list
        | GenesisPolicyMismatch
        | GenesisReadbackRefused of ReadbackFailure list
    type ValidatedGenesis = private ValidatedGenesis of commitOid: string

    let private sha256 (bytes: byte array) =
        SHA256.HashData bytes |> Convert.ToHexString |> _.ToLowerInvariant()

    let private gitOid kind (bytes: byte array) =
        let header = Encoding.ASCII.GetBytes($"{kind} {bytes.Length}\u0000")
        SHA1.HashData(Array.append header bytes) |> Convert.ToHexString |> _.ToLowerInvariant()

    let private treeBytes (eventOid: string) (headOid: string) =
        use stream = new MemoryStream()
        for name, oid in [ "event.json", eventOid; "head.json", headOid ] do
            let entry = Encoding.UTF8.GetBytes("100644 " + name + "\u0000")
            stream.Write(entry, 0, entry.Length)
            let rawOid = Convert.FromHexString oid
            stream.Write(rawOid, 0, rawOid.Length)
        stream.ToArray()

    let private commitBytes treeOid =
        Encoding.UTF8.GetBytes(
            String.concat "\n"
                [ "tree " + treeOid
                  "author FS.GG Coordination <coordination@fs.gg> 0 +0000"
                  "committer FS.GG Coordination <coordination@fs.gg> 0 +0000"
                  ""
                  "fsgg intake " + OperationId
                  "" ])

    let private canonicalPlan () =
        let address = ProtectedIntakeJournal.address
        let eventBytes =
            ShardedJournalAdapter.canonicalJson "{\"intakeGenesis\":\"fsgg.coord.intake-transaction/v1\"}"
            |> Result.defaultWith invalidOp
        let provisional =
            { SchemaVersion = 1
              Address = address
              Generation = 1L
              EventDigest = ShardedJournalAdapter.sha256 eventBytes
              SnapshotDigest = None
              Terminal = false
              PriorHeadDigest = None
              HeadDigest = String.replicate 64 "0" }
        let head =
            { provisional with
                HeadDigest =
                    ShardedJournalAdapter.journalHeadBytes provisional
                    |> ShardedJournalAdapter.sha256 }
        let headBytes = ShardedJournalAdapter.journalHeadBytes head
        let eventOid, headOid = gitOid "blob" eventBytes, gitOid "blob" headBytes
        let tree = treeBytes eventOid headOid
        let treeOid = gitOid "tree" tree
        let commit = commitBytes treeOid
        let commitOid = gitOid "commit" commit
        let planBytes =
            Encoding.UTF8.GetBytes(
                String.concat "\n"
                    [ "schema=" + Schema
                      "aggregate=" + IntakeTransaction.AggregateId
                      "ref=" + address.Ref
                      "intent=genesis"
                      "expected-old=<absent>"
                      "event-blob=" + eventOid
                      "head-blob=" + headOid
                      "tree=" + treeOid
                      "commit=" + commitOid
                      "" ])
        let proposal =
            { PlanBytes = planBytes
              PlanSha256 = sha256 planBytes
              ParentOid = None
              EventBlobOid = eventOid
              EventBlobSha256 = sha256 eventBytes
              EventBlobBytes = eventBytes
              HeadBlobOid = headOid
              HeadBlobSha256 = sha256 headBytes
              HeadBlobBytes = headBytes
              TreeOid = treeOid
              TreeSha256 = sha256 tree
              TreeBytes = tree
              CommitOid = commitOid
              CommitSha256 = sha256 commit
              CommitBytes = commit }
        { Ref = address.Ref
          ExpectedOldObjectId = None
          ProposedObjectId = commitOid
          Refspec = $"{commitOid}:{address.Ref}"
          ForceWithLease = $"--force-with-lease={address.Ref}:"
          Proposal = proposal }

    let plan () = canonicalPlan ()

    let private sameObjects (proposal: Proposal) (objects: ObjectReadback) =
        objects.EventBlobOid = proposal.EventBlobOid
        && objects.EventBlobBytes = proposal.EventBlobBytes
        && objects.HeadBlobOid = proposal.HeadBlobOid
        && objects.HeadBlobBytes = proposal.HeadBlobBytes
        && objects.TreeOid = proposal.TreeOid
        && objects.TreeBytes = proposal.TreeBytes
        && objects.CommitOid = proposal.CommitOid
        && objects.CommitBytes = proposal.CommitBytes
        && objects.CommitParentOid.IsNone
        && gitOid "blob" objects.EventBlobBytes = objects.EventBlobOid
        && gitOid "blob" objects.HeadBlobBytes = objects.HeadBlobOid
        && gitOid "tree" objects.TreeBytes = objects.TreeOid
        && gitOid "commit" objects.CommitBytes = objects.CommitOid

    let validateReadback supplied before after objects =
        let failures = ResizeArray<ReadbackFailure>()
        let expected = canonicalPlan ()
        if supplied <> expected then failures.Add PlanDrift
        match before with
        | Absent -> ()
        | Present _ -> failures.Add ExpectedAbsentRefNotAbsent
        | Partial _ | Unknown _ -> failures.Add RefReadbackIndeterminate
        match after with
        | Present oid when oid = expected.ProposedObjectId -> ()
        | Present _ | Absent -> failures.Add ResultingRefMismatch
        | Partial _ | Unknown _ -> failures.Add RefReadbackIndeterminate
        if not (sameObjects expected.Proposal objects) then failures.Add ObjectCustodyMismatch
        if failures.Count = 0 then Ok(ReadbackConfirmed expected.ProposedObjectId)
        else Error(List.ofSeq failures)

    let validateAuthorized now supplied policy authorization readback =
        let failures = ResizeArray<AuthorizationFailure>()
        match ProtectedIntakeAuthority.validate now policy authorization readback with
        | Ok _ -> ()
        | Error authorityFailures -> failures.Add(IntakeAuthorityRefused authorityFailures)

        let expected = canonicalPlan ()
        if policy.Intent <> Genesis || policy.Proposal <> expected.Proposal then
            failures.Add GenesisPolicyMismatch

        match validateReadback supplied readback.RefBefore readback.RefAfter readback.Objects with
        | Ok _ -> ()
        | Error readbackFailures -> failures.Add(GenesisReadbackRefused readbackFailures)

        if failures.Count = 0 then Ok(ValidatedGenesis expected.ProposedObjectId)
        else Error(List.ofSeq failures)
