namespace FS.GG.Coord.GitHub

open System
open System.Security.Cryptography
open System.Text
open FS.GG.Coordination.GitHub
open FS.GG.Coord.GitHub.ProtectedIntakeJournal

module ProtectedIntakeProvider =
    type UnsupportedAuthority =
        { ProtectedGenesisVerified: bool
          RestUpdateRefHasExpectedHead: bool
          GraphQlPreservesProposalCommit: bool
          ReceivePackHasExpectedHead: bool
          ReceivePackPreservesProposalObjects: bool
          DeclaredWriterAppId: int64
          JournalContractWriterAppIds: int64 list
          ResponseUnknownReconciliationDefined: bool
          Reasons: string list }

    type Feasibility =
        | Unsupported of UnsupportedAuthority

    type ResponseUnknownReconciliation =
        | AppliedWithoutGrant of Snapshot
        | ProvenAbsent of Snapshot
        | ForeignHead of Snapshot
        | Indeterminate of string

    type ReceivePackPlan =
        { Ref: string
          ExpectedOldObjectId: string
          ProposedObjectId: string
          Refspec: string
          ForceWithLease: string
          Objects: Objects }

    [<Literal>]
    let private DeclaredProductionWriterAppId = 5064713L

    let private JournalContractWriterAppIds = [ 4166418L ]

    let private gitOid kind (bytes: byte[]) =
        let header = Encoding.ASCII.GetBytes($"{kind} {bytes.Length}\u0000")
        SHA1.HashData(Array.append header bytes)
        |> Convert.ToHexString
        |> _.ToLowerInvariant()

    let assess read =
        restore read
        |> Result.map (fun _ ->
            // REST PATCH git/refs accepts only the desired sha and force flag. Its non-force mode
            // proves fast-forward ancestry, not equality with one expected old head. GraphQL's
            // createCommitOnBranch has expectedHeadOid, but GitHub authors (and may sign) that commit;
            // it cannot publish the proposal's exact commit bytes and object id. Combining separate
            // object uploads with either surface would therefore weaken the journal's atomic boundary.
            // Receive-pack does carry exact old/new OIDs and the supplied object pack, but the current
            // journal contract accepts only App 4166418 while the protected workflow declares 5064713.
            Unsupported
                { ProtectedGenesisVerified = true
                  RestUpdateRefHasExpectedHead = false
                  GraphQlPreservesProposalCommit = false
                  ReceivePackHasExpectedHead = true
                  ReceivePackPreservesProposalObjects = true
                  DeclaredWriterAppId = DeclaredProductionWriterAppId
                  JournalContractWriterAppIds = JournalContractWriterAppIds
                  ResponseUnknownReconciliationDefined = true
                  Reasons =
                    [ "github-rest-update-ref-lacks-expected-old-head"
                      "github-graphql-cas-does-not-preserve-proposal-commit"
                      "protected-intake-journal-contract-does-not-accept-declared-app:5064713"
                      "installed-protected-intake-write-authority-unavailable" ] })

    let private proposalHasExactObjectCustody (proposal: Proposal) =
        let proposed = proposal.Cas.ProposedCommit
        let objects = proposal.Objects

        proposal.Cas.Address = address
        && proposal.Cas.ObservedObjectId <> proposed.CommitOid
        && proposed.ParentOid = Some proposal.Cas.ObservedObjectId
        && proposed.OperationId = proposal.Cas.OperationId
        && proposed.Head.Address = address
        && proposal.Cas.Refspec = $"{proposed.CommitOid}:{address.Ref}"
        && proposal.Cas.ForceWithLease = $"--force-with-lease={address.Ref}:{proposal.Cas.ObservedObjectId}"
        && objects.CommitObjectId = proposed.CommitOid
        && objects.TreeObjectId = proposed.TreeOid
        && objects.EventObjectId = gitOid "blob" proposed.Event.Bytes
        && objects.HeadObjectId = gitOid "blob" proposed.HeadBytes
        && objects.CommitObjectId = gitOid "commit" objects.CommitBytes
        && objects.TreeObjectId = gitOid "tree" objects.TreeBytes
        && objects.EventBytes = proposed.Event.Bytes
        && objects.HeadBytes = proposed.HeadBytes
        && ShardedJournalAdapter.sha256 objects.EventBytes = proposed.Event.Digest

    let planReceivePack proposal =
        if proposalHasExactObjectCustody proposal then
            Ok
                { Ref = address.Ref
                  ExpectedOldObjectId = proposal.Cas.ObservedObjectId
                  ProposedObjectId = proposal.Cas.ProposedCommit.CommitOid
                  Refspec = proposal.Cas.Refspec
                  ForceWithLease = proposal.Cas.ForceWithLease
                  Objects = proposal.Objects }
        else
            Error "protected-intake-proposal-custody-invalid"

    let private proposalIsExactCurrentHead (proposal: Proposal) (read: Read) (snapshot: Snapshot) =
        let proposed = proposal.Cas.ProposedCommit
        let objects = proposal.Objects
        let current = snapshot.Head.Current

        proposalHasExactObjectCustody proposal
        && current.CommitOid = proposed.CommitOid
        && current.ParentOid = proposed.ParentOid
        && current.TreeOid = proposed.TreeOid
        && current.OperationId = proposal.Cas.OperationId
        && current.Head = proposed.Head
        && current.HeadBytes = proposed.HeadBytes
        && current.Event.Digest = proposed.Event.Digest
        && current.Event.Bytes = proposed.Event.Bytes
        && Map.tryFind proposed.CommitOid read.CommitBytes = Some objects.CommitBytes
        && Map.tryFind proposed.TreeOid read.TreeBytes = Some objects.TreeBytes

    let reconcileResponseUnknown proposal read =
        match proposalHasExactObjectCustody proposal, restore read with
        | false, _ -> Indeterminate "protected-intake-proposal-custody-invalid"
        | true, Error reason -> Indeterminate("protected-intake-readback:" + reason)
        | true, Ok snapshot when snapshot.Head.Current.CommitOid = proposal.Cas.ProposedCommit.CommitOid ->
            if proposalIsExactCurrentHead proposal read snapshot then
                AppliedWithoutGrant snapshot
            else
                Indeterminate "protected-intake-proposal-correspondence-failed"
        | true, Ok snapshot when snapshot.Head.Current.CommitOid = proposal.Cas.ObservedObjectId ->
            ProvenAbsent snapshot
        | true, Ok snapshot -> ForeignHead snapshot
