namespace FS.GG.Coord.Core.Tests

open System
open System.Text
open Xunit
open FS.GG.Coord

module IntakeTransactionTests =
    let private draft: Intake.Draft =
        { Schema = Intake.Schema
          Id = "Draft-A"
          Owner = "FS-GG"
          Repository = ".github"
          Title = "Retain issue intent"
          Observed = "A native create may have an unknown outcome"
          RootCause = "A read cannot prove absence"
          Acceptance = "One issue is bound"
          Verification = "Read the native issue"
          Paths = [ "src/FS.GG.Coord.Core" ]
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
        { RepositoryId = 42L
          RepositoryNodeId = "R_abc"
          DraftId = draft.Id }

    let private intent () =
        let bytes = Encoding.UTF8.GetBytes("POST /repos/FS-GG/.github/issues\n{\"title\":\"Retain issue intent\"}")
        match IntakeTransaction.prepareIntent identity draft bytes with
        | Ok (IntakeTransaction.Intent binding) -> binding
        | value -> failwithf "unexpected intent: %A" value

    let private issue: IntakeTransaction.NativeIssue =
        { RepositoryId = identity.RepositoryId
          NodeId = "I_abc"
          Number = 123
          Url = "https://github.com/FS-GG/.github/issues/123" }

    let private receipt digest: IntakeReceipt.Receipt =
        { DraftId = draft.Id
          Owner = draft.Owner
          Repository = draft.Repository
          IssueNumber = issue.Number
          DraftDigest = digest }

    let private get = function
        | Ok value -> value
        | Error message -> failwith message

    let private entry state identity =
        IntakeTransaction.find state identity |> get |> Option.get

    [<Fact>]
    let ``unknown outcome retains InFlight and repeated intent cannot recreate`` () =
        let binding = intent ()
        let state = IntakeTransaction.apply None (IntakeTransaction.Intent binding) |> get
        let inFlight = IntakeTransaction.apply (Some state) (IntakeTransaction.InFlight binding) |> get
        let unknown = IntakeTransaction.apply (Some inFlight) (IntakeTransaction.Unknown binding) |> get
        Assert.Equal("InFlight", (entry unknown identity).Phase)
        Assert.True(IntakeTransaction.apply (Some unknown) (IntakeTransaction.Intent binding) |> Result.isError)
        Assert.True(IntakeTransaction.apply None (IntakeTransaction.InFlight binding) |> Result.isError)

    [<Fact>]
    let ``bound replay preserves one native issue and refuses another`` () =
        let binding = intent ()
        let state = IntakeTransaction.apply None (IntakeTransaction.Intent binding) |> get
        let inFlight = IntakeTransaction.apply (Some state) (IntakeTransaction.InFlight binding) |> get
        let event = IntakeTransaction.Bound(binding, issue, receipt binding.DraftDigest)
        let bound = IntakeTransaction.apply (Some inFlight) event |> get
        Assert.Equal("Bound", (entry bound identity).Phase)
        Assert.Equal(bound, IntakeTransaction.apply (Some bound) event |> get)
        let other = { issue with Number = 124; NodeId = "I_other" }
        Assert.True(IntakeTransaction.apply (Some bound) (IntakeTransaction.Bound(binding, other, receipt binding.DraftDigest)) |> Result.isError)
        Assert.True(IntakeTransaction.apply (Some bound) (IntakeTransaction.Unknown binding) |> Result.isError)

    [<Fact>]
    let ``changed request or draft digest cannot cross state boundary`` () =
        let binding = intent ()
        let state = IntakeTransaction.apply None (IntakeTransaction.Intent binding) |> get
        let changed = { binding with RequestBytes = Encoding.UTF8.GetBytes("different") }
        Assert.True(IntakeTransaction.apply (Some state) (IntakeTransaction.InFlight changed) |> Result.isError)
        let changedDigest = { binding with DraftDigest = String.replicate 64 "a" }
        Assert.True(IntakeTransaction.apply (Some state) (IntakeTransaction.InFlight changedDigest) |> Result.isError)
        let wrongReceipt = { receipt binding.DraftDigest with DraftDigest = String.replicate 64 "f" }
        let inFlight = IntakeTransaction.apply (Some state) (IntakeTransaction.InFlight binding) |> get
        Assert.True(IntakeTransaction.apply (Some inFlight) (IntakeTransaction.Bound(binding, issue, wrongReceipt)) |> Result.isError)

    [<Fact>]
    let ``immutable target and case-sensitive draft distinguish slug collisions`` () =
        let key = IntakeTransaction.key identity |> get
        Assert.True(key <> (IntakeTransaction.key ({ identity with RepositoryId = 43L }) |> get))
        Assert.True(key <> (IntakeTransaction.key ({ identity with RepositoryNodeId = "R_other" }) |> get))
        Assert.True(key <> (IntakeTransaction.key ({ identity with DraftId = "draft-a" }) |> get))
        Assert.True(key <> (IntakeTransaction.key ({ identity with DraftId = "Draft-A|other" }) |> get))

    [<Fact>]
    let ``request bytes are copied before retention`` () =
        let source = Encoding.UTF8.GetBytes("request")
        let event = IntakeTransaction.prepareIntent identity draft source |> get
        source[0] <- 0uy
        let state = IntakeTransaction.apply None event |> get
        match event with
        | IntakeTransaction.Intent binding ->
            binding.RequestBytes[0] <- 0uy
            Assert.Equal(byte 'r', (entry state identity).Binding.RequestBytes[0])
        | _ -> failwith "wrong phase"

    [<Fact>]
    let ``canonical roundtrip refuses duplicate keys and incompatible envelope`` () =
        let binding = intent ()
        let bytes = IntakeTransaction.encodeEvent (IntakeTransaction.Intent binding) |> get
        Assert.Equal(IntakeTransaction.Intent binding, IntakeTransaction.decodeEvent bytes |> get)
        let text = Encoding.UTF8.GetString bytes
        let duplicate = text.Replace("{\"schema\":", "{\"schema\":\"bad\",\"schema\":")
        Assert.True(IntakeTransaction.decodeEvent (Encoding.UTF8.GetBytes duplicate) |> Result.isError)
        Assert.True(IntakeTransaction.decodeEvent (Encoding.UTF8.GetBytes(text.Replace(IntakeTransaction.Schema, "fsgg.coord.intake-transaction/v0"))) |> Result.isError)
        Assert.True(IntakeTransaction.decodeEvent (Encoding.UTF8.GetBytes(text.Replace(IntakeTransaction.JournalRef, "refs/heads/wrong"))) |> Result.isError)

    [<Fact>]
    let ``oversized request and event refuse`` () =
        Assert.True(IntakeTransaction.prepareIntent identity draft (Array.zeroCreate 32769) |> Result.isError)
        Assert.True(IntakeTransaction.decodeEvent (Array.zeroCreate 65537) |> Result.isError)

    [<Fact>]
    let ``wrong predecessor and wrong native target refuse`` () =
        let binding = intent ()
        Assert.True(IntakeTransaction.apply None (IntakeTransaction.Bound(binding, issue, receipt binding.DraftDigest)) |> Result.isError)
        let state = IntakeTransaction.apply None (IntakeTransaction.Intent binding) |> get
        Assert.True(IntakeTransaction.apply (Some state) (IntakeTransaction.Bound(binding, issue, receipt binding.DraftDigest)) |> Result.isError)
        let inFlight = IntakeTransaction.apply (Some state) (IntakeTransaction.InFlight binding) |> get
        let wrong = { issue with RepositoryId = 99L }
        Assert.True(IntakeTransaction.apply (Some inFlight) (IntakeTransaction.Bound(binding, wrong, receipt binding.DraftDigest)) |> Result.isError)

    [<Fact>]
    let ``bounded predecessor digest binds the same issue`` () =
        let binding = intent ()
        let predecessor = binding.CompatibleDigests |> List.find (fun value -> value <> binding.DraftDigest)
        let state = IntakeTransaction.apply None (IntakeTransaction.Intent binding) |> get
        let inFlight = IntakeTransaction.apply (Some state) (IntakeTransaction.InFlight binding) |> get
        let bound = IntakeTransaction.apply (Some inFlight) (IntakeTransaction.Bound(binding, issue, receipt predecessor)) |> get
        Assert.Equal(Some issue, (entry bound identity).Issue)
        Assert.True(IntakeTransaction.apply (Some bound) (IntakeTransaction.Bound(binding, issue, receipt binding.DraftDigest)) |> Result.isError)

    [<Fact>]
    let ``shared aggregate interleaves drafts and keeps bound terminal per key`` () =
        let first = intent ()
        let secondDraft = { draft with Id = "Draft-B"; Title = "Other issue" }
        let secondIdentity = { identity with DraftId = secondDraft.Id }
        let second =
            match IntakeTransaction.prepareIntent secondIdentity secondDraft (Encoding.UTF8.GetBytes "second request") |> get with
            | IntakeTransaction.Intent binding -> binding
            | _ -> failwith "wrong phase"
        let secondIssue = { issue with NodeId = "I_second"; Number = 124 }
        let secondReceipt = { receipt second.DraftDigest with DraftId = secondDraft.Id; IssueNumber = secondIssue.Number }
        let events =
            [ IntakeTransaction.Intent first
              IntakeTransaction.Intent second
              IntakeTransaction.InFlight second
              IntakeTransaction.InFlight first
              IntakeTransaction.Bound(second, secondIssue, secondReceipt)
              IntakeTransaction.Unknown first ]
        let replay events =
            events
            |> List.map (IntakeTransaction.encodeEvent >> get >> IntakeTransaction.decodeEvent >> get)
            |> List.fold (fun state event -> IntakeTransaction.apply state event |> get |> Some) None
            |> Option.get
        let state = replay events
        Assert.Equal(2, state.Entries.Count)
        Assert.Equal("InFlight", (entry state identity).Phase)
        Assert.Equal("Bound", (entry state secondIdentity).Phase)
        Assert.Equal(Some secondIssue, (entry state secondIdentity).Issue)
        Assert.Equal(state, replay events)
        Assert.True(IntakeTransaction.apply (Some state) (IntakeTransaction.Intent second) |> Result.isError)
        Assert.True(IntakeTransaction.apply (Some state) (IntakeTransaction.Unknown second) |> Result.isError)
        let firstBound = IntakeTransaction.apply (Some state) (IntakeTransaction.Bound(first, issue, receipt first.DraftDigest)) |> get
        Assert.Equal("Bound", (entry firstBound identity).Phase)
        Assert.Equal(Some secondIssue, (entry firstBound secondIdentity).Issue)

    [<Fact>]
    let ``same key refuses changed valid binding without disturbing another draft`` () =
        let first = intent ()
        let secondDraft = { draft with Id = "Draft-B" }
        let secondIdentity = { identity with DraftId = secondDraft.Id }
        let second =
            match IntakeTransaction.prepareIntent secondIdentity secondDraft (Encoding.UTF8.GetBytes "second request") |> get with
            | IntakeTransaction.Intent binding -> binding
            | _ -> failwith "wrong phase"
        let state = IntakeTransaction.apply None (IntakeTransaction.Intent first) |> get
        let state = IntakeTransaction.apply (Some state) (IntakeTransaction.Intent second) |> get
        let changed = { first with Owner = "Changed" }
        Assert.True(IntakeTransaction.apply (Some state) (IntakeTransaction.Intent changed) |> Result.isError)
        Assert.True(IntakeTransaction.apply (Some state) (IntakeTransaction.InFlight changed) |> Result.isError)
        Assert.Equal(second, (entry state secondIdentity).Binding)
        Assert.True(IntakeTransaction.find state { identity with DraftId = "missing" } |> get |> Option.isNone)
