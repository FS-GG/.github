module FS.GG.Coord.GitHub.Tests.ProtectedIntakeJournalTests

open System
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json
open Xunit
open FS.GG.Coord
open FS.GG.Coord.GitHub.ProtectedIntakeJournal
open FS.GG.Coordination.GitHub

let private draft: Intake.Draft =
    { Schema = Intake.Schema
      Id = "case-1"
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

let private intent =
    IntakeTransaction.prepareIntent
        { RepositoryId = 123L; RepositoryNodeId = "R_123"; DraftId = draft.Id }
        draft
        [| 0uy; 42uy; 255uy |]
    |> Result.defaultWith failwith

let private binding =
    match intent with
    | IntakeTransaction.Intent value -> value
    | _ -> failwith "fixture intent"

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

let private initialCommit () =
    let blob =
        ShardedJournalAdapter.canonicalJson "{\"intakeGenesis\":\"fsgg.coord.intake-transaction/v1\"}"
        |> Result.defaultWith failwith
    let provisional =
        { SchemaVersion = 1
          Address = address
          Generation = 1L
          EventDigest = ShardedJournalAdapter.sha256 blob
          SnapshotDigest = None
          Terminal = false
          PriorHeadDigest = None
          HeadDigest = String.replicate 64 "0" }
    let head =
        { provisional with
            HeadDigest = ShardedJournalAdapter.journalHeadBytes provisional |> ShardedJournalAdapter.sha256 }
    let headBytes = ShardedJournalAdapter.journalHeadBytes head
    let tree = rawTree (gitOid "blob" blob) (gitOid "blob" headBytes)
    let treeOid = gitOid "tree" tree
    let commitBytes = rawCommit treeOid None "preinstalled-empty-genesis"
    { CommitOid = gitOid "commit" commitBytes
      ParentOid = None
      TreeOid = treeOid
      OperationId = "preinstalled-empty-genesis"
      Head = head
      HeadBytes = headBytes
      Event = { Bytes = blob; Digest = provisional.EventDigest }
      Checkpoint = None }

let private observed (commits: JournalCommit list) : Read =
    let current = List.last commits
    let target = "refs/heads/fsgg/v2/journal/**/*"
    let protection =
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
    let commitBytes =
        commits
        |> List.map (fun commit ->
            commit.CommitOid, rawCommit commit.TreeOid commit.ParentOid commit.OperationId)
        |> Map.ofList
    let treeBytes =
        commits
        |> List.map (fun commit ->
            commit.TreeOid,
            rawTree (gitOid "blob" commit.Event.Bytes) (gitOid "blob" commit.HeadBytes))
        |> Map.ofList
    { Repository = "FS-GG/FS.GG.Coordination.Authority"
      RepositoryId = 1351660651L
      Ref = address.Ref
      Protection = protection
      FirstHead = Some current.CommitOid
      SecondHead = Some current.CommitOid
      Observation = JournalComplete(current.CommitOid, commits)
      CommitBytes = commitBytes
      TreeBytes = treeBytes }

[<Fact>]
let ``dedicated address and retained request replay are exact`` () =
    Assert.Equal(IntakeTransaction.AggregateId, address.CanonicalId)
    Assert.Equal(IntakeTransaction.JournalRef, address.Ref)
    let snapshot = restore (observed [ initialCommit () ]) |> Result.defaultWith failwith
    Assert.Empty(snapshot.State.Entries)
    let proposal = planAppend "intent-1" (observed [ initialCommit () ]) intent |> Result.defaultWith failwith
    let appended = restore (observed [ initialCommit (); proposal.Cas.ProposedCommit ]) |> Result.defaultWith failwith
    let entry = IntakeTransaction.find appended.State binding.Identity |> Result.defaultWith failwith |> Option.get
    Assert.Equal("Intent", entry.Phase)
    Assert.Equal<byte>(binding.RequestBytes, entry.Binding.RequestBytes)

[<Fact>]
let ``missing or moving protected head refuses intake`` () =
    let commit = initialCommit ()
    let baseline = observed [ commit ]
    let missing = { baseline with FirstHead = None; SecondHead = None; Observation = JournalDeleted }
    let moving = { baseline with SecondHead = Some(String.replicate 40 "c") }
    Assert.True(restore missing |> Result.isError)
    Assert.True(restore moving |> Result.isError)

[<Fact>]
let ``tampered raw object and absent journal never reach CAS`` () =
    let commit = initialCommit ()
    let baseline = observed [ commit ]
    let tampered =
        { baseline with
            CommitBytes = Map.add commit.CommitOid [| 1uy; 2uy |] baseline.CommitBytes }
    Assert.True(restore tampered |> Result.isError)
    Assert.True(restore { baseline with Protection = { baseline.Protection with Complete = false } } |> Result.isError)
    let mutable writes = 0
    let port: Port =
        { Read = fun _ -> { baseline with FirstHead = None; SecondHead = None; Observation = JournalDeleted }
          CompareAndSwap = fun _ -> writes <- writes + 1; Won }
    Assert.True(append port "in-flight" (IntakeTransaction.InFlight binding) |> Result.isError)
    Assert.Equal(0, writes)

[<Fact>]
let ``shared aggregate retains a second draft independently`` () =
    let genesis = initialCommit ()
    let first = planAppend "intent-1" (observed [ genesis ]) intent |> Result.defaultWith failwith
    let inFlight =
        planAppend "in-flight-1" (observed [ genesis; first.Cas.ProposedCommit ])
            (IntakeTransaction.InFlight binding)
        |> Result.defaultWith failwith
    let issue: IntakeTransaction.NativeIssue =
        { RepositoryId = binding.Identity.RepositoryId
          NodeId = "I_123"
          Number = 1
          Url = "https://github.com/FS-GG/example/issues/1" }
    let receipt: IntakeReceipt.Receipt =
        { DraftId = binding.Identity.DraftId
          Owner = binding.Owner
          Repository = binding.Repository
          IssueNumber = issue.Number
          DraftDigest = binding.DraftDigest }
    let bound =
        planAppend "bound-1" (observed [ genesis; first.Cas.ProposedCommit; inFlight.Cas.ProposedCommit ])
            (IntakeTransaction.Bound(binding, issue, receipt))
        |> Result.defaultWith failwith
    Assert.False(bound.Cas.ProposedCommit.Head.Terminal)
    let secondDraft = { draft with Id = "case-2" }
    let secondIdentity = { binding.Identity with DraftId = secondDraft.Id }
    let secondIntent =
        IntakeTransaction.prepareIntent secondIdentity secondDraft [| 9uy; 8uy |]
        |> Result.defaultWith failwith
    let second =
        planAppend "intent-2"
            (observed [ genesis; first.Cas.ProposedCommit; inFlight.Cas.ProposedCommit; bound.Cas.ProposedCommit ])
            secondIntent
        |> Result.defaultWith failwith
    let state =
        restore (observed [ genesis; first.Cas.ProposedCommit; inFlight.Cas.ProposedCommit; bound.Cas.ProposedCommit; second.Cas.ProposedCommit ])
        |> Result.defaultWith failwith
    Assert.Equal(2, state.State.Entries.Count)
    Assert.Equal("Bound", (IntakeTransaction.find state.State binding.Identity |> Result.defaultWith failwith |> Option.get).Phase)
    Assert.True(IntakeTransaction.find state.State secondIdentity |> Result.defaultWith failwith |> Option.isSome)

[<Fact>]
let ``raw commit rejects a hidden second parent and wrong operation message`` () =
    let genesis = initialCommit ()
    let baseline = observed [ genesis ]
    let original = rawCommit genesis.TreeOid None genesis.OperationId
    let split = Encoding.UTF8.GetString(original).Split('\n')
    let hiddenParent =
        String.concat "\n" ([ split[0]; "parent " + String.replicate 40 "a" ] @ (split |> Array.skip 1 |> Array.toList))
        |> Encoding.UTF8.GetBytes
    let wrongMessage = rawCommit genesis.TreeOid None "other-operation"
    for bytes in [ hiddenParent; wrongMessage ] do
        let changed = { genesis with CommitOid = gitOid "commit" bytes }
        let read = observed [ changed ]
        let read = { read with CommitBytes = Map.add changed.CommitOid bytes read.CommitBytes }
        Assert.True(restore read |> Result.isError)
    Assert.True(restore baseline |> Result.isOk)

[<Fact>]
let ``CAS loser cannot claim an observed proposal`` () =
    let genesis = initialCommit ()
    let intentCommit =
        planAppend "intent-1" (observed [ genesis ]) intent
        |> Result.defaultWith failwith
        |> fun proposal -> proposal.Cas.ProposedCommit
    let mutable commits = [ genesis; intentCommit ]
    let port: Port =
        { Read = fun _ -> observed commits
          CompareAndSwap = fun proposal ->
              commits <- commits @ [ proposal.Cas.ProposedCommit ]
              CompareAndSwapResult.ParentConflict }
    match append port "in-flight" (IntakeTransaction.InFlight binding) with
    | Ok(Conflict(Some snapshot)) ->
        Assert.Equal(3, snapshot.Head.Commits.Length)
    | other -> failwithf "CAS loser must not receive a grant: %A" other

[<Fact>]
let ``unknown append needs exact readback and remains in flight`` () =
    let genesis = initialCommit ()
    let intentProposal = planAppend "intent-1" (observed [ genesis ]) intent |> Result.defaultWith failwith
    let mutable commits = [ genesis; intentProposal.Cas.ProposedCommit ]
    let mutable calls = 0
    let port: Port =
        { Read = fun _ -> observed commits
          CompareAndSwap =
            fun proposal ->
                calls <- calls + 1
                if calls < 3 then
                    commits <- commits @ [ proposal.Cas.ProposedCommit ]
                    ResponseUnknown
                else
                    ResponseUnknown }
    match append port "in-flight" (IntakeTransaction.InFlight binding) with
    | Ok(ObservedWithoutGrant state) ->
        let entry = IntakeTransaction.find state.State binding.Identity |> Result.defaultWith failwith |> Option.get
        Assert.Equal("InFlight", entry.Phase)
    | other -> failwithf "expected readback: %A" other
    match append port "unknown-observed" (IntakeTransaction.Unknown binding) with
    | Ok(ObservedWithoutGrant state) ->
        let entry = IntakeTransaction.find state.State binding.Identity |> Result.defaultWith failwith |> Option.get
        Assert.Equal("InFlight", entry.Phase)
    | other -> failwithf "expected retained unknown: %A" other
    match append port "unknown-unobserved" (IntakeTransaction.Unknown binding) with
    | Ok(Indeterminate _) -> ()
    | other -> failwithf "unknown response must stay unresolved: %A" other

[<Fact>]
let ``strong absence binds exact original bytes and rejects unknown alone`` () =
    let genesis = initialCommit ()
    let intentProposal = planAppend "intent-1" (observed [ genesis ]) intent |> Result.defaultWith failwith
    let intentRead = observed [ genesis; intentProposal.Cas.ProposedCommit ]
    let proposal = planAppend "in-flight" intentRead (IntakeTransaction.InFlight binding) |> Result.defaultWith failwith
    let snapshot =
        restore (observed [ genesis; intentProposal.Cas.ProposedCommit; proposal.Cas.ProposedCommit ])
        |> Result.defaultWith failwith
    let proof =
        { OriginalRequestSha256 = binding.RequestSha256
          EvidenceDigest = String.replicate 64 "1"
          Kind = "idempotency-key-excluded" }
    let provider: ProviderReconciliation =
        { Read = fun identity bytes ->
            Assert.Equal(binding.Identity, identity)
            Assert.Equal<byte>(binding.RequestBytes, bytes)
            Ok(Some proof) }
    let original = originalRequestAfterStrongAbsence snapshot binding.Identity provider |> Result.defaultWith failwith
    Assert.Equal<byte>(binding.RequestBytes, original)
    original[0] <- 99uy
    Assert.Equal(0uy, (IntakeTransaction.find snapshot.State binding.Identity |> Result.defaultWith failwith |> Option.get).Binding.RequestBytes[0])
    Assert.True(
        originalRequestAfterStrongAbsence
            snapshot
            binding.Identity
            { Read = fun _ _ -> Ok(Some { proof with OriginalRequestSha256 = String.replicate 64 "2" }) }
        |> Result.isError)
    Assert.True(originalRequestAfterStrongAbsence (restore intentRead |> Result.defaultWith failwith) binding.Identity provider |> Result.isError)
