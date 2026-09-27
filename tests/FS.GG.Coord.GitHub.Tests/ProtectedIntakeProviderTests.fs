module FS.GG.Coord.GitHub.Tests.ProtectedIntakeProviderTests

open System
open System.IO
open System.Security.Cryptography
open System.Text
open Xunit
open FS.GG.Coord
open FS.GG.Coord.GitHub.ProtectedIntakeJournal
open FS.GG.Coord.GitHub.ProtectedIntakeProvider
open FS.GG.Coordination.GitHub

let private draft: Intake.Draft =
    { Schema = Intake.Schema
      Id = "provider-case"
      Owner = "FS-GG"
      Repository = "example"
      Title = "A finding"
      Observed = "Observed"
      RootCause = "Cause"
      Acceptance = "Acceptance"
      Verification = "Verification"
      Paths = [ "src/example.fs" ]
      Class = "hardening"
      Status = "Backlog"
      Disposition = Some Intake.Create
      Phase = None
      Severity = None
      BlockedBy = None
      BlockedOn = None
      BacklogReason = Some "not-yet-actionable"
      JudgementQuestion = None }

let private identity: IntakeTransaction.Identity =
    { RepositoryId = 123L; RepositoryNodeId = "R_123"; DraftId = draft.Id }

let private intent =
    IntakeTransaction.prepareIntent identity draft [| 0uy; 42uy; 255uy |]
    |> Result.defaultWith failwith

let private gitOid kind (bytes: byte[]) =
    SHA1.HashData(Array.append (Encoding.ASCII.GetBytes($"{kind} {bytes.Length}\u0000")) bytes)
    |> Convert.ToHexString
    |> _.ToLowerInvariant()

let private rawTree (eventOid: string) (headOid: string) =
    use stream = new MemoryStream()
    for name, oid in [ "event.json", eventOid; "head.json", headOid ] do
        let header = Encoding.UTF8.GetBytes("100644 " + name + "\u0000")
        stream.Write(header, 0, header.Length)
        let raw = Convert.FromHexString(oid)
        stream.Write(raw, 0, raw.Length)
    stream.ToArray()

let private rawCommit (treeOid: string) (parent: string option) (operationId: string) =
    Encoding.UTF8.GetBytes(
        String.concat "\n"
            [ "tree " + treeOid
              yield! parent |> Option.map (fun value -> "parent " + value) |> Option.toList
              "author FS.GG Coordination <coordination@fs.gg> 0 +0000"
              "committer FS.GG Coordination <coordination@fs.gg> 0 +0000"
              ""
              "fsgg intake " + operationId
              "" ])

let private genesis () =
    let event =
        ShardedJournalAdapter.canonicalJson "{\"intakeGenesis\":\"fsgg.coord.intake-transaction/v1\"}"
        |> Result.defaultWith failwith
    let provisional =
        { SchemaVersion = 1
          Address = address
          Generation = 1L
          EventDigest = ShardedJournalAdapter.sha256 event
          SnapshotDigest = None
          Terminal = false
          PriorHeadDigest = None
          HeadDigest = String.replicate 64 "0" }
    let head =
        { provisional with
            HeadDigest = ShardedJournalAdapter.journalHeadBytes provisional |> ShardedJournalAdapter.sha256 }
    let headBytes = ShardedJournalAdapter.journalHeadBytes head
    let tree = rawTree (gitOid "blob" event) (gitOid "blob" headBytes)
    let treeOid = gitOid "tree" tree
    let commitBytes = rawCommit treeOid None "preinstalled-empty-genesis"
    { CommitOid = gitOid "commit" commitBytes
      ParentOid = None
      TreeOid = treeOid
      OperationId = "preinstalled-empty-genesis"
      Head = head
      HeadBytes = headBytes
      Event = { Bytes = event; Digest = provisional.EventDigest }
      Checkpoint = None }

let private observed commits : Read =
    let current = List.last commits
    let target = "refs/heads/fsgg/v2/journal/**/*"
    { Repository = "FS-GG/FS.GG.Coordination.Authority"
      RepositoryId = 1351660651L
      Ref = address.Ref
      Protection =
        { Complete = true
          RepositoryId = 1351660651L
          Writer =
            { Id = 21872113L; Name = "v2-journal-writer"; Active = true; Target = target
              BypassAppIds = [ 4166418L ]; RestrictsCreationAndUpdate = true
              RejectsDeletionAndNonFastForward = false }
          Integrity =
            { Id = 21872115L; Name = "v2-journal-integrity"; Active = true; Target = target
              BypassAppIds = []; RestrictsCreationAndUpdate = false
              RejectsDeletionAndNonFastForward = true }
          EffectiveRulesComplete = true
          EffectiveRules = [ CreationRestricted; UpdateRestricted; DeletionRejected; NonFastForwardRejected ] }
      FirstHead = Some current.CommitOid
      SecondHead = Some current.CommitOid
      Observation = JournalComplete(current.CommitOid, commits)
      CommitBytes =
        commits
        |> List.map (fun commit -> commit.CommitOid, rawCommit commit.TreeOid commit.ParentOid commit.OperationId)
        |> Map.ofList
      TreeBytes =
        commits
        |> List.map (fun commit ->
            commit.TreeOid, rawTree (gitOid "blob" commit.Event.Bytes) (gitOid "blob" commit.HeadBytes))
        |> Map.ofList }

[<Fact>]
let ``installed genesis cannot manufacture GitHub expected-head CAS`` () =
    match assess (observed [ genesis () ]) with
    | Ok(Unsupported result) ->
        Assert.True(result.ProtectedGenesisVerified)
        Assert.False(result.RestUpdateRefHasExpectedHead)
        Assert.False(result.GraphQlPreservesProposalCommit)
        Assert.True(result.ResponseUnknownReconciliationDefined)
        Assert.Contains("installed-protected-intake-write-authority-unavailable", result.Reasons)
    | other -> failwithf "expected precise unsupported result: %A" other

[<Fact>]
let ``absent protected genesis refuses feasibility`` () =
    let baseline = observed [ genesis () ]
    let absent =
        { baseline with
            FirstHead = None
            SecondHead = None
            Observation = JournalDeleted
            CommitBytes = Map.empty
            TreeBytes = Map.empty }
    match assess absent with
    | Error "intake-journal-head-unconfirmed" -> ()
    | other -> failwithf "absent genesis must refuse: %A" other

[<Fact>]
let ``unknown response reconciles exact applied absent and foreign heads without a grant`` () =
    let root = genesis ()
    let proposal = planAppend "intent" (observed [ root ]) intent |> Result.defaultWith failwith

    match reconcileResponseUnknown proposal (observed [ root; proposal.Cas.ProposedCommit ]) with
    | AppliedWithoutGrant snapshot -> Assert.Equal(proposal.Cas.ProposedCommit.CommitOid, snapshot.Head.Current.CommitOid)
    | other -> failwithf "exact proposal should be observed without a grant: %A" other

    match reconcileResponseUnknown proposal (observed [ root ]) with
    | ProvenAbsent snapshot -> Assert.Equal(proposal.Cas.ObservedObjectId, snapshot.Head.Current.CommitOid)
    | other -> failwithf "unchanged expected head should prove absence: %A" other

    let foreign = planAppend "foreign" (observed [ root ]) intent |> Result.defaultWith failwith
    match reconcileResponseUnknown proposal (observed [ root; foreign.Cas.ProposedCommit ]) with
    | ForeignHead snapshot -> Assert.Equal(foreign.Cas.ProposedCommit.CommitOid, snapshot.Head.Current.CommitOid)
    | other -> failwithf "different winner should be a conflict: %A" other

[<Fact>]
let ``unknown response requires exact proposal object custody`` () =
    let root = genesis ()
    let proposal = planAppend "intent" (observed [ root ]) intent |> Result.defaultWith failwith
    let altered =
        { proposal with
            Objects = { proposal.Objects with CommitBytes = Array.append proposal.Objects.CommitBytes [| 0uy |] } }
    match reconcileResponseUnknown altered (observed [ root; proposal.Cas.ProposedCommit ]) with
    | Indeterminate "protected-intake-proposal-custody-invalid" -> ()
    | other -> failwithf "altered proposal bytes must remain indeterminate: %A" other
